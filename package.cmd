@echo off
setlocal
set ROOT=%~dp0
set VERSION=1.0.0
set ZIP=%ROOT%dist\ChargeGuard-v%VERSION%-win.zip

if not exist "%ROOT%dist\ChargeGuard.exe" (
  echo Build first: build.cmd
  exit /b 1
)

set STAGE=%TEMP%\ChargeGuard-package-%RANDOM%-%RANDOM%
mkdir "%STAGE%" >nul 2>&1
if not exist "%STAGE%" (
  echo Could not create staging directory: %STAGE%
  exit /b 1
)

mkdir "%STAGE%\src" >nul 2>&1
mkdir "%STAGE%\assets" >nul 2>&1

copy /Y "%ROOT%dist\ChargeGuard.exe" "%STAGE%\" >nul
copy /Y "%ROOT%README.md" "%STAGE%\" >nul
copy /Y "%ROOT%LICENSE" "%STAGE%\" >nul
copy /Y "%ROOT%.gitignore" "%STAGE%\" >nul
copy /Y "%ROOT%build.cmd" "%STAGE%\" >nul
copy /Y "%ROOT%package.cmd" "%STAGE%\" >nul
copy /Y "%ROOT%config.example.ini" "%STAGE%\" >nul
copy /Y "%ROOT%src\*.cs" "%STAGE%\src\" >nul
copy /Y "%ROOT%assets\*.png" "%STAGE%\assets\" >nul

dir /b "%STAGE%\*.log" >nul 2>&1
if not errorlevel 1 (
  echo Refusing to package: log file found in staging.
  rmdir /S /Q "%STAGE%"
  exit /b 1
)
dir /b "%STAGE%\config.ini" >nul 2>&1
if not errorlevel 1 (
  echo Refusing to package: local config found in staging.
  rmdir /S /Q "%STAGE%"
  exit /b 1
)

if exist "%ZIP%" del /F /Q "%ZIP%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP%' -Force"
set ZIP_RESULT=%ERRORLEVEL%
rmdir /S /Q "%STAGE%"

if not "%ZIP_RESULT%"=="0" (
  echo PACKAGE FAILED
  exit /b 1
)

echo PACKAGE OK: %ZIP%

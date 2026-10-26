@echo off
setlocal
set ROOT=%~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
  echo csc.exe not found: %CSC%
  exit /b 1
)

if not exist "%ROOT%dist" mkdir "%ROOT%dist"

"%CSC%" /nologo /target:winexe /optimize+ /warn:4 /out:"%ROOT%dist\ChargeGuard.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  "%ROOT%src\AssemblyInfo.cs" ^
  "%ROOT%src\Battery.cs" ^
  "%ROOT%src\Config.cs" ^
  "%ROOT%src\Controller.cs" ^
  "%ROOT%src\Log.cs" ^
  "%ROOT%src\PlugClient.cs" ^
  "%ROOT%src\Program.cs" ^
  "%ROOT%src\SelfTest.cs" ^
  "%ROOT%src\SettingsForm.cs" ^
  "%ROOT%src\TrayApp.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)

echo BUILD OK: dist\ChargeGuard.exe

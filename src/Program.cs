using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ChargeGuard
{
    public static class Program
    {
        private const string AppName = "ChargeGuard";
        private static Mutex _mutex;

        public static string DataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ChargeGuard");
            }
        }

        public static string ConfigPath { get { return Path.Combine(DataDir, "config.ini"); } }
        public static string LogPath { get { return Path.Combine(DataDir, "chargeguard.log"); } }

        public static string DocPath
        {
            get
            {
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
                if (File.Exists(local)) return local;
                return Path.GetFullPath(Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "..", "README.md"));
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            Log.FilePath = LogPath;

            string mode = args.Length == 0 ? "--tray" : args[0].ToLowerInvariant();

            if (mode == "--help" || mode == "-h" || mode == "/?" || mode == "--status" ||
                mode == "--selftest" || mode.StartsWith("--screenshot"))
                Cli.Begin();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            int rc;
            try
            {
                if (mode == "--help" || mode == "-h" || mode == "/?")
                {
                    PrintHelp();
                    rc = 0;
                }
                else if (mode == "--status")
                {
                    rc = ShowStatus();
                }
                else if (mode == "--selftest")
                {
                    rc = SelfTest.Run();
                }
                else if (mode.StartsWith("--screenshot"))
                {
                    rc = Screenshot(args);
                }
                else if (mode == "--tray")
                {
                    rc = RunTray();
                }
                else
                {
                    Cli.Line("未知参数：" + args[0]);
                    PrintHelp();
                    rc = 2;
                }
            }
            catch (Exception ex)
            {
                Cli.Line("错误：" + ex);
                Log.Write("程序异常：" + ex);
                rc = 1;
            }
            finally
            {
                Cli.End();
            }
            return rc;
        }

        private static int RunTray()
        {
            bool created;
            _mutex = new Mutex(true, "Local\\ChargeGuard.Tray", out created);
            if (!created)
            {
                MessageBox.Show("ChargeGuard 已在运行。请在任务栏通知区域找到它的电池图标。",
                    "ChargeGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            TrayApp app = new TrayApp();
            try
            {
                Application.Run(app);
            }
            finally
            {
                app.Cleanup();
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
            }
            return 0;
        }

        private static int ShowStatus()
        {
            BatteryStatus s = BatteryReader.Read();
            AppConfig c = AppConfig.Load(ConfigPath);

            Cli.Line("ChargeGuard status");
            Cli.Line("------------------");
            Cli.Line("config:    " + ConfigPath);
            Cli.Line("log:       " + LogPath);
            Cli.Line("battery:   " + s.Describe());
            Cli.Line(string.Format("raw:       percent={0} ac={1} charging={2} hasBattery={3} unknown={4}",
                s.Percent, s.AcOnline, s.Charging, s.HasBattery, s.Unknown));
            Cli.Line("mode:      " + c.Mode +
                (c.EnforcePlug ? " (plug active)" : (c.IsPlugMode ? " (plug NOT configured -> monitor fallback)" : " (monitor)")));
            Cli.Line(string.Format("threshold: limit={0}% resume={1}%", c.Limit, c.Resume));
            Cli.Line(string.Format("state:     override={0} plugOff={1} autostart={2}", c.Override, c.PlugOff, c.Autostart));
            Cli.Line("url on:    " + c.PlugOnUrl);
            Cli.Line("url off:   " + c.PlugOffUrl);
            Cli.Line("output:    " + Cli.OutputFile);
            return 0;
        }

        private static int Screenshot(string[] args)
        {
            string path = null;
            int eq = args[0].IndexOf('=');
            if (eq > 0)
            {
                path = args[0].Substring(eq + 1);
            }
            else if (args.Length > 1)
            {
                path = args[1];
            }
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Path.GetTempPath(), "chargeguard_settings.png");
            path = Path.GetFullPath(path.Trim('"'));

            // Always render a fixed demo state so screenshots never reveal the
            // user's battery level or plug URLs.
            AppConfig display = new AppConfig();
            display.Mode = "SmartPlug";
            display.Limit = 80;
            display.Resume = 75;
            display.PlugOnUrl = "http://192.0.2.50/cm?cmnd=Power%20On";
            display.PlugOffUrl = "http://192.0.2.50/cm?cmnd=Power%20Off";

            BatteryStatus demo = new BatteryStatus();
            demo.HasBattery = true;
            demo.Percent = 76;
            demo.AcOnline = true;
            demo.Charging = true;

            using (SettingsForm form = new SettingsForm(display, demo))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-3000, -3000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();

                using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(path, ImageFormat.Png);
                }
                form.Close();
            }

            Cli.Line("screenshot saved: " + path);
            return File.Exists(path) ? 0 : 1;
        }

        private static void PrintHelp()
        {
            Cli.Line("ChargeGuard - 充电上限控制（ONE-NETBOOK OXP2）");
            Cli.Line("");
            Cli.Line("用法：");
            Cli.Line("  ChargeGuard.exe                    启动托盘程序");
            Cli.Line("  ChargeGuard.exe --tray             启动托盘程序");
            Cli.Line("  ChargeGuard.exe --status           显示当前电池状态与配置");
            Cli.Line("  ChargeGuard.exe --selftest         运行内置自检");
            Cli.Line("  ChargeGuard.exe --screenshot [路径] 生成设置界面截图");
            Cli.Line("  ChargeGuard.exe --help             显示本帮助");
            Cli.Line("");
            Cli.Line("说明：OXP2 没有公开的 Windows 充电阈值接口，本程序不会写入 EC。");
            Cli.Line("      默认监控模式只作提醒；若要真正切断充电，请在设置中配置智能插座。");
            Cli.Line("      剪贴板/文件输出：" + Cli.OutputFile);
            Cli.Line("      详细说明：" + DocPath);
        }

        public static void ApplyAutostart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null)
                    {
                        Log.Write("无法打开 Run 注册表项，开机启动未设置。");
                        return;
                    }
                    if (enabled)
                        key.SetValue(AppName, "\"" + Application.ExecutablePath + "\" --tray");
                    else
                        key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                Log.Write("设置开机启动失败：" + ex.Message);
            }
        }
    }

    public static class Cli
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        private static string _file;

        public static string OutputFile { get { return _file; } }

        public static void Begin()
        {
            try { AttachConsole(-1); } catch { }
            _file = Path.Combine(Path.GetTempPath(), "chargeguard_cli.txt");
            try { File.WriteAllText(_file, "", new UTF8Encoding(false)); } catch { }
        }

        public static void Line(string text)
        {
            try { Console.WriteLine(text); } catch { }
            if (string.IsNullOrEmpty(_file)) return;
            try { File.AppendAllText(_file, text + Environment.NewLine, new UTF8Encoding(false)); } catch { }
        }

        public static void End()
        {
            try { Console.Out.Flush(); } catch { }
        }
    }
}

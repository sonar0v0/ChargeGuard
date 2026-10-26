using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChargeGuard
{
    public class TrayApp : ApplicationContext
    {
        private readonly AppConfig _config;
        private readonly Controller _controller;
        private NotifyIcon _icon;
        private ContextMenuStrip _menu;
        private System.Windows.Forms.Timer _timer;
        private Icon _currentIcon;
        private BatteryStatus _status;
        private ToolStripMenuItem _statusItem;
        private ToolStripMenuItem _lastItem;
        private ToolStripMenuItem _fullItem;
        private ToolStripMenuItem _restoreItem;
        private ToolStripMenuItem _powerItem;
        private bool _exiting;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public TrayApp()
        {
            _config = AppConfig.Load(Program.ConfigPath);
            if (_config.Override)
            {
                // "本次充满" must not survive a restart; fail back to the limit.
                _config.Override = false;
                try { _config.Save(Program.ConfigPath); }
                catch (Exception ex) { Log.Write("清除本次充满状态失败：" + ex.Message); }
                Log.Write("启动时已清除上次的“本次充满”状态。");
            }
            _status = BatteryReader.Read();

            _controller = new Controller(_config);
            _controller.SendOn = PlugClient.Send;
            _controller.SendOff = PlugClient.Send;
            _controller.Notify = ShowNotification;
            _controller.Trace = Log.Write;
            _controller.ConfigChanged = delegate
            {
                try { _config.Save(Program.ConfigPath); }
                catch (Exception ex) { Log.Write("保存配置失败：" + ex.Message); }
            };

            Program.ApplyAutostart(_config.Autostart);

            BuildMenu();
            _icon = new NotifyIcon();
            _icon.ContextMenuStrip = _menu;
            _currentIcon = CreateTrayIcon(_status, _config);
            _icon.Icon = _currentIcon;
            _icon.Text = "ChargeGuard";
            _icon.Visible = true;
            _icon.DoubleClick += delegate { OpenSettings(); };

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 10000;
            _timer.Tick += delegate { Sample(); };
            _timer.Start();

            Log.Write("ChargeGuard 启动。配置：" + Program.ConfigPath);
            Sample();
        }

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();

            _statusItem = new ToolStripMenuItem("正在读取电池...");
            _statusItem.Enabled = false;
            _menu.Items.Add(_statusItem);

            _lastItem = new ToolStripMenuItem("");
            _lastItem.Enabled = false;
            _menu.Items.Add(_lastItem);

            _menu.Items.Add(new ToolStripSeparator());

            _fullItem = new ToolStripMenuItem("充满（本次）");
            _fullItem.Click += delegate
            {
                _controller.ChargeToFull();
                UpdateUi();
            };
            _menu.Items.Add(_fullItem);

            _restoreItem = new ToolStripMenuItem("恢复充电限制");
            _restoreItem.Click += delegate
            {
                _controller.RestoreLimit();
                UpdateUi();
            };
            _menu.Items.Add(_restoreItem);

            _powerItem = new ToolStripMenuItem("恢复插座供电");
            _powerItem.Click += delegate
            {
                _controller.RestorePlugPower("手动恢复插座供电");
                UpdateUi();
            };
            _menu.Items.Add(_powerItem);

            ToolStripMenuItem check = new ToolStripMenuItem("立即检查");
            check.Click += delegate { Sample(); };
            _menu.Items.Add(check);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem settings = new ToolStripMenuItem("设置...");
            settings.Click += delegate { OpenSettings(); };
            _menu.Items.Add(settings);

            ToolStripMenuItem openLog = new ToolStripMenuItem("打开日志");
            openLog.Click += delegate { OpenFile(Program.LogPath); };
            _menu.Items.Add(openLog);

            ToolStripMenuItem help = new ToolStripMenuItem("使用说明...");
            help.Click += delegate { OpenFile(Program.DocPath); };
            _menu.Items.Add(help);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitApp(); };
            _menu.Items.Add(exit);
        }

        private void Sample()
        {
            if (_exiting) return;
            try
            {
                _status = BatteryReader.Read();
                _controller.Tick(_status, DateTime.Now);
            }
            catch (Exception ex)
            {
                Log.Write("检查电池状态失败：" + ex.Message);
            }
            UpdateUi();
        }

        private void UpdateUi()
        {
            string state;
            if (!_config.EnforcePlug)
                state = "监控模式";
            else if (_config.PlugOff)
                state = "插座已关闭";
            else
                state = "插座供电中";
            if (_config.Override) state += " · 本次充满";

            _statusItem.Text = _status.Describe() + "   " + state;

            if (_controller.LastActionAt == DateTime.MinValue)
                _lastItem.Text = "最近动作：尚未执行";
            else
                _lastItem.Text = string.Format("最近动作：{0}  {1:HH:mm:ss}",
                    _controller.LastAction, _controller.LastActionAt);

            _fullItem.Enabled = !_config.Override;
            _fullItem.Text = _config.Override ? "本次充满：已启用" : "充满（本次）";
            _restoreItem.Enabled = _config.Override;
            _restoreItem.Text = string.Format("恢复 {0}% 限制", _config.Limit);
            _powerItem.Enabled = _config.PlugOff && AppConfig.IsHttpUrl(_config.PlugOnUrl);

            string tip = string.Format("ChargeGuard  {0}  上限 {1}%  {2}",
                _status.Describe(), _config.Limit, state);
            if (tip.Length > 62) tip = tip.Substring(0, 59) + "...";
            _icon.Text = tip;

            SetIcon(CreateTrayIcon(_status, _config));
        }

        private void SetIcon(Icon icon)
        {
            Icon previous = _currentIcon;
            _currentIcon = icon;
            if (_icon != null) _icon.Icon = icon;
            if (previous != null) previous.Dispose();
        }

        private void ShowNotification(string message, bool warning)
        {
            Log.Write((warning ? "警告：" : "通知：") + message);
            if (!_config.Notifications || _icon == null) return;
            try
            {
                _icon.BalloonTipTitle = "ChargeGuard";
                _icon.BalloonTipText = message;
                _icon.BalloonTipIcon = warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
                _icon.ShowBalloonTip(5000);
            }
            catch (Exception ex)
            {
                Log.Write("显示通知失败：" + ex.Message);
            }
        }

        private void OpenSettings()
        {
            using (SettingsForm form = new SettingsForm(_config, _status))
            {
                if (form.ShowDialog() != DialogResult.OK) return;

                AppConfig updated = form.ResultConfig;
                bool oldPlugOff = _config.PlugOff;
                bool oldOverride = _config.Override;
                string oldOnUrl = _config.PlugOnUrl;
                bool urlChanged =
                    !string.Equals(updated.PlugOnUrl, _config.PlugOnUrl, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(updated.PlugOffUrl, _config.PlugOffUrl, StringComparison.OrdinalIgnoreCase);
                bool losesPlugControl = oldPlugOff && (!updated.EnforcePlug || urlChanged);

                if (losesPlugControl)
                {
                    bool restored = false;
                    if (AppConfig.IsHttpUrl(oldOnUrl))
                    {
                        try { restored = PlugClient.Send(oldOnUrl); }
                        catch (Exception ex) { Log.Write("恢复原插座供电异常：" + ex.Message); }
                    }

                    // The old config is discarded either way; do not keep a plug-off
                    // flag that the new config can no longer act on.
                    oldPlugOff = false;
                    if (restored)
                    {
                        Log.Write("切换插座配置前已恢复原插座供电。");
                    }
                    else
                    {
                        Log.Write("切换插座配置时未能恢复原插座供电，请手动检查插座。");
                        MessageBox.Show(
                            "原来的插座可能仍处于关闭状态，而新设置已不再控制它。\r\n请手动检查并打开插座。",
                            "ChargeGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }

                _config.CopyFrom(updated);
                _config.Override = oldOverride;
                _config.PlugOff = oldPlugOff;

                try { _config.Save(Program.ConfigPath); }
                catch (Exception ex)
                {
                    MessageBox.Show("保存配置失败：" + ex.Message, "ChargeGuard",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                Program.ApplyAutostart(_config.Autostart);
                Log.Write(string.Format("设置已更新：上限 {0}%，恢复 {1}%，模式 {2}。",
                    _config.Limit, _config.Resume, _config.Mode));
                Sample();
            }
        }

        private void OpenFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    MessageBox.Show("文件不存在：" + path, "ChargeGuard",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开文件：" + ex.Message, "ChargeGuard",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;

            if (_timer != null) _timer.Stop();
            try { _controller.Shutdown(); }
            catch (Exception ex) { Log.Write("退出恢复插座失败：" + ex.Message); }
            if (_icon != null) _icon.Visible = false;
            Log.Write("ChargeGuard 退出。");
            ExitThread();
        }

        public void Cleanup()
        {
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
            if (_icon != null) { _icon.Visible = false; _icon.Dispose(); _icon = null; }
            if (_currentIcon != null) { _currentIcon.Dispose(); _currentIcon = null; }
            if (_menu != null) { _menu.Dispose(); _menu = null; }
        }

        private static Icon CreateTrayIcon(BatteryStatus status, AppConfig config)
        {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (GraphicsPath bg = RoundedRect(new Rectangle(1, 1, 30, 30), 7))
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(235, 30, 32, 36)))
                    g.FillPath(bgBrush, bg);

                int pct = status.HasBattery && !status.Unknown ? status.Percent : 0;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;

                Color fillColor;
                if (pct < 20) fillColor = Color.FromArgb(235, 92, 92);
                else if (pct < 50) fillColor = Color.FromArgb(240, 172, 60);
                else fillColor = Color.FromArgb(88, 200, 120);
                if (config.Override) fillColor = Color.FromArgb(90, 170, 245);

                using (GraphicsPath body = RoundedRect(new Rectangle(4, 10, 22, 15), 3))
                using (Pen outline = new Pen(Color.FromArgb(235, 238, 241, 244), 1.5f))
                    g.DrawPath(outline, body);

                using (SolidBrush cap = new SolidBrush(Color.FromArgb(235, 238, 241, 244)))
                    g.FillRectangle(cap, 26, 14, 3, 7);

                int fillWidth = (int)Math.Round(18.0 * pct / 100.0);
                if (fillWidth > 0)
                {
                    using (SolidBrush fill = new SolidBrush(fillColor))
                        g.FillRectangle(fill, 6, 12, fillWidth, 11);
                }

                if (config.EnforcePlug && config.PlugOff)
                {
                    using (Pen slash = new Pen(Color.FromArgb(240, 230, 70, 70), 2.6f))
                    {
                        slash.StartCap = LineCap.Round;
                        slash.EndCap = LineCap.Round;
                        g.DrawLine(slash, 7, 25, 27, 8);
                    }
                }
                else if (status.Charging || config.Override)
                {
                    Point[] bolt = new Point[]
                    {
                        new Point(17, 7), new Point(11, 17), new Point(15, 17),
                        new Point(13, 25), new Point(21, 14), new Point(16, 14)
                    };
                    using (SolidBrush boltBrush = new SolidBrush(Color.FromArgb(255, 250, 205, 70)))
                    using (Pen boltPen = new Pen(Color.FromArgb(220, 30, 30, 30), 1f))
                    {
                        g.FillPolygon(boltBrush, bolt);
                        g.DrawPolygon(boltPen, bolt);
                    }
                }

                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(h))
                        return (Icon)temp.Clone();
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

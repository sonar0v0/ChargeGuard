using System;

namespace ChargeGuard
{
    public class Controller
    {
        public AppConfig Config;
        public Func<string, bool> SendOn;
        public Func<string, bool> SendOff;
        public Action<string, bool> Notify;
        public Action ConfigChanged;
        public Action<string> Trace;

        private DateTime _lastOffAttempt = DateTime.MinValue;
        private DateTime _lastOnAttempt = DateTime.MinValue;
        private DateTime _lastErrorNotify = DateTime.MinValue;
        private bool _limitNotified;
        private bool _fullNotified;
        private string _lastAction = "尚未执行";
        private DateTime _lastActionAt = DateTime.MinValue;

        public Controller(AppConfig config)
        {
            Config = config;
            SendOn = delegate(string url) { return false; };
            SendOff = delegate(string url) { return false; };
            Notify = delegate(string msg, bool warning) { };
            ConfigChanged = delegate { };
            Trace = delegate(string msg) { };
        }

        public string LastAction { get { return _lastAction; } }
        public DateTime LastActionAt { get { return _lastActionAt; } }

        public void Tick(BatteryStatus s, DateTime now)
        {
            if (!s.HasBattery || s.Unknown) return;

            bool externalPowerLoss = !s.AcOnline && !Config.PlugOff;
            if (externalPowerLoss)
            {
                if (Config.Override)
                {
                    Config.Override = false;
                    SaveConfig();
                    Trace("检测到外部断电，已取消“本次充满”。");
                }
                _limitNotified = false;
                _fullNotified = false;
            }
            if (s.Percent < Config.Limit - 2) _limitNotified = false;
            if (s.Percent < 100) _fullNotified = false;

            if (!Config.EnforcePlug)
            {
                MonitorTick(s, now);
                return;
            }

            if (Config.Override)
            {
                if (Config.PlugOff)
                {
                    if (now - _lastOnAttempt >= TimeSpan.FromSeconds(5))
                        TryOn(now, "本次充满：打开插座");
                }
                if (s.Percent >= 100 && !_fullNotified)
                {
                    _fullNotified = true;
                    Notify("已充至 100%。点击“恢复限制”后，高于上限时会切断插座。", false);
                }
                return;
            }

            if (Config.Limit >= 100)
            {
                if (s.Percent >= 100 && !_fullNotified)
                {
                    _fullNotified = true;
                    Notify("已充至 100%。上限设为 100% 时不会切断供电。", false);
                }
                return;
            }

            if (s.Percent >= Config.Limit)
            {
                // Only act while charging actually happens. If AC is already gone
                // (user unplugged the charger) there is nothing to cut.
                if (!s.AcOnline) return;

                if (!_limitNotified)
                {
                    _limitNotified = true;
                    Notify(string.Format("电量 {0}%，已达到 {1}% 上限，正在关闭插座供电。", s.Percent, Config.Limit), false);
                }

                // If we already asked the plug to turn off but Windows still sees AC,
                // either the command failed silently or power comes from elsewhere.
                TimeSpan cooldown = Config.PlugOff ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(10);
                if (now - _lastOffAttempt >= cooldown)
                    TryOff(now, "达到上限：关闭插座");
                return;
            }

            if (s.Percent <= Config.Resume && Config.PlugOff)
            {
                if (now - _lastOnAttempt >= TimeSpan.FromSeconds(10))
                    TryOn(now, "低于恢复阈值：打开插座");
            }
            // Between Resume and Limit: hysteresis, keep the current plug state.
        }

        private void MonitorTick(BatteryStatus s, DateTime now)
        {
            if (!s.AcOnline) return;

            if (Config.Override)
            {
                if (s.Percent >= 100 && !_fullNotified)
                {
                    _fullNotified = true;
                    Notify("已充至 100%，可恢复充电限制。", false);
                }
                return;
            }

            if (Config.Limit < 100 && s.Percent >= Config.Limit && !_limitNotified)
            {
                _limitNotified = true;
                Notify(string.Format(
                    "电量 {0}%，已达到 {1}% 上限。监控模式无法主动停止充电：请拔掉电源，或在设置中启用智能插座模式。",
                    s.Percent, Config.Limit), true);
            }
        }

        public void ChargeToFull()
        {
            Config.Override = true;
            SaveConfig();

            if (Config.EnforcePlug)
            {
                bool wasOff = Config.PlugOff;
                if (wasOff)
                {
                    TryOn(DateTime.Now, "本次充满：打开插座");
                }
                else
                {
                    SetAction("本次充满已启用", DateTime.Now);
                }
                Notify(wasOff
                    ? "已切换为“本次充满”，并请求打开插座。"
                    : "已切换为“本次充满”，将充至 100%。", false);
            }
            else
            {
                Notify("已切换为“本次充满”。当前无法控制供电，请在充满后手动断电。", false);
            }
        }

        public void RestoreLimit()
        {
            Config.Override = false;
            _fullNotified = false;
            SaveConfig();
            Notify(string.Format("已恢复 {0}% 充电上限，将在下一次检查时生效。", Config.Limit), false);
        }

        // Manual recovery, also usable after switching away from plug mode.
        public bool RestorePlugPower(string why)
        {
            if (!Config.PlugOff) return true;
            if (!AppConfig.IsHttpUrl(Config.PlugOnUrl))
            {
                Notify("没有可用的开插座地址，无法恢复插座供电。", true);
                return false;
            }

            DateTime now = DateTime.Now;
            _lastOnAttempt = now;
            bool ok = false;
            try { ok = SendOn(Config.PlugOnUrl); }
            catch (Exception ex) { Trace("恢复插座供电异常：" + ex.Message); }

            if (ok)
            {
                Config.PlugOff = false;
                SetAction(why + "（成功）", now);
                SaveConfig();
            }
            else
            {
                SetAction(why + "（失败）", now);
                NotifyThrottled(now, "无法恢复插座供电。请检查插座地址、网络，或手动打开插座。");
            }
            return ok;
        }

        public bool Shutdown()
        {
            if (Config.EnforcePlug && Config.RestoreOnExit && Config.PlugOff)
            {
                bool ok = false;
                try { ok = SendOn(Config.PlugOnUrl); }
                catch (Exception ex) { Trace("退出恢复插座异常：" + ex.Message); }
                if (ok)
                {
                    Config.PlugOff = false;
                    SaveConfig();
                    Trace("退出：已恢复插座供电。");
                }
                return ok;
            }
            return true;
        }

        private void TryOff(DateTime now, string why)
        {
            _lastOffAttempt = now;
            bool ok = false;
            try { ok = SendOff(Config.PlugOffUrl); }
            catch (Exception ex) { Trace("关闭插座异常：" + ex.Message); }

            if (ok)
            {
                Config.PlugOff = true;
                SetAction(why + "（成功）", now);
                SaveConfig();
            }
            else
            {
                SetAction(why + "（失败）", now);
                NotifyThrottled(now, "无法关闭插座，将自动重试。请检查插座地址、网络和插座供电对象。");
            }
        }

        private void TryOn(DateTime now, string why)
        {
            _lastOnAttempt = now;
            bool ok = false;
            try { ok = SendOn(Config.PlugOnUrl); }
            catch (Exception ex) { Trace("打开插座异常：" + ex.Message); }

            if (ok)
            {
                Config.PlugOff = false;
                SetAction(why + "（成功）", now);
                SaveConfig();
            }
            else
            {
                SetAction(why + "（失败）", now);
                NotifyThrottled(now, "无法打开插座，将自动重试。请检查插座地址与网络。");
            }
        }

        private void NotifyThrottled(DateTime now, string message)
        {
            if (now - _lastErrorNotify < TimeSpan.FromSeconds(60)) return;
            _lastErrorNotify = now;
            Notify(message, true);
            Trace(message);
        }

        private void SetAction(string text, DateTime now)
        {
            _lastAction = text;
            _lastActionAt = now;
            Trace("动作：" + text);
        }

        private void SaveConfig()
        {
            try { ConfigChanged(); }
            catch (Exception ex) { Trace("保存配置失败：" + ex.Message); }
        }
    }
}

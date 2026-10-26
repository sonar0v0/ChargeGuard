using System;
using System.Collections.Generic;
using System.IO;

namespace ChargeGuard
{
    public static class SelfTest
    {
        private static int _passed;
        private static int _failed;

        public static int Run()
        {
            _passed = 0;
            _failed = 0;

            Cli.Line("ChargeGuard self-test");
            Cli.Line("=====================");

            BatteryStatus live = BatteryReader.Read();
            Cli.Line("live battery: " + live.Describe());
            Cli.Line(string.Format("raw: percent={0} ac={1} charging={2} hasBattery={3}",
                live.Percent, live.AcOnline, live.Charging, live.HasBattery));
            Cli.Line("");

            Section("config");
            TestConfig();

            Section("monitor mode");
            TestMonitor();

            Section("smart plug mode");
            TestSmartPlug();

            Section("override / charge to full");
            TestOverride();

            Section("edge cases");
            TestEdgeCases();

            Cli.Line("");
            Cli.Line(string.Format("RESULT: {0} passed, {1} failed", _passed, _failed));
            Cli.Line("output file: " + Cli.OutputFile);
            return _failed == 0 ? 0 : 1;
        }

        private static void Section(string name)
        {
            Cli.Line("[" + name + "]");
        }

        private static void Check(string name, bool ok)
        {
            Check(name, ok, "");
        }

        private static void Check(string name, bool ok, string detail)
        {
            if (ok)
            {
                _passed++;
                Cli.Line("  PASS  " + name);
            }
            else
            {
                _failed++;
                Cli.Line("  FAIL  " + name + (detail.Length == 0 ? "" : " -- " + detail));
            }
        }

        private static void CheckEq(string name, int expected, int actual)
        {
            Check(name, expected == actual,
                string.Format("expected {0}, got {1}", expected, actual));
        }

        private static void TestConfig()
        {
            AppConfig def = new AppConfig();
            Check("default limit is 80", def.Limit == 80, "got " + def.Limit);
            Check("default resume is 75", def.Resume == 75, "got " + def.Resume);
            Check("default mode is Monitor", def.Mode == "Monitor", def.Mode);
            Check("default autostart off", !def.Autostart);
            Check("default plug state on", !def.PlugOff);

            AppConfig c = new AppConfig();
            c.Limit = 30;
            c.Resume = 95;
            c.Mode = "bogus";
            c.PlugOnUrl = "  http://a/on  ";
            c.Normalize();
            CheckEq("limit clamped to 50", 50, c.Limit);
            CheckEq("resume forced below limit", 45, c.Resume);
            Check("unknown mode falls back to Monitor", c.Mode == "Monitor", c.Mode);
            Check("url is trimmed", c.PlugOnUrl == "http://a/on", "'" + c.PlugOnUrl + "'");

            c.Limit = 110;
            c.Resume = 98;
            c.Normalize();
            CheckEq("limit clamped to 100", 100, c.Limit);
            CheckEq("resume 98 kept under 100", 98, c.Resume);

            c.Limit = 80;
            c.Resume = 80;
            c.Normalize();
            CheckEq("resume == limit becomes 75", 75, c.Resume);

            AppConfig cfg = new AppConfig();
            cfg.Limit = 85;
            cfg.Resume = 70;
            cfg.Mode = "SmartPlug";
            cfg.PlugOnUrl = "http://192.0.2.5/cm?cmnd=Power%20On";
            cfg.PlugOffUrl = "https://example.com/off?x=1&y=2";
            cfg.Autostart = true;
            cfg.Notifications = false;
            cfg.RestoreOnExit = false;
            cfg.Override = true;
            cfg.PlugOff = true;

            string tmp = Path.Combine(Path.GetTempPath(), "chargeguard_cfg_test.ini");
            try
            {
                cfg.Save(tmp);
                AppConfig loaded = AppConfig.Load(tmp);
                Check("ini roundtrip: threshold",
                    loaded.Limit == 85 && loaded.Resume == 70,
                    loaded.Limit + "/" + loaded.Resume);
                Check("ini roundtrip: mode/urls",
                    loaded.Mode == "SmartPlug" &&
                    loaded.PlugOnUrl == cfg.PlugOnUrl &&
                    loaded.PlugOffUrl == cfg.PlugOffUrl,
                    loaded.Mode + " " + loaded.PlugOnUrl);
                Check("ini roundtrip: booleans",
                    loaded.Autostart && !loaded.Notifications && !loaded.RestoreOnExit &&
                    loaded.Override && loaded.PlugOff);
                Check("plug urls are enforced",
                    loaded.EnforcePlug && loaded.PlugConfigured);

                AppConfig invalid = new AppConfig();
                invalid.Mode = "SmartPlug";
                invalid.PlugOnUrl = "ftp://nope";
                invalid.PlugOffUrl = "";
                Check("invalid urls disable plug enforcement", !invalid.EnforcePlug);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private static void TestMonitor()
        {
            Harness h = new Harness("Monitor");

            h.Step(80, true, true);
            CheckEq("notifies once at limit", 1, h.Notes.Count);
            CheckEq("never turns plug off", 0, h.OffCount);
            CheckEq("never turns plug on", 0, h.OnCount);

            h.Step(81, true, true);
            CheckEq("no repeat while above limit", 1, h.Notes.Count);

            h.Step(77, true, true);
            h.Step(80, true, true);
            CheckEq("re-arms after falling below limit-2", 2, h.Notes.Count);
        }

        private static void TestSmartPlug()
        {
            Harness h = new Harness("SmartPlug");

            h.Step(79, true, true);
            CheckEq("no action below limit", 0, h.OffCount);

            h.Step(80, true, true);
            CheckEq("turns plug off at limit", 1, h.OffCount);
            Check("persists plug-off state", h.Cfg.PlugOff);
            Check("saves config after action", h.SaveCount > 0, "saves=" + h.SaveCount);
            Check("sends a limit notification", h.Notes.Count >= 1);

            h.Step(81, false, false);
            CheckEq("no duplicate off while AC is off", 1, h.OffCount);

            h.Step(77, false, false);
            CheckEq("hysteresis keeps plug off at 77", 0, h.OnCount);

            h.Step(75, false, false);
            CheckEq("turns plug on at resume threshold", 1, h.OnCount);
            Check("clears plug-off state", !h.Cfg.PlugOff);

            h.Step(79, true, true);
            CheckEq("keeps charging between resume and limit", 1, h.OffCount);
            CheckEq("keeps plug on in hysteresis zone", 1, h.OnCount);

            Harness d = new Harness("SmartPlug");
            d.Step(80, true, true);
            d.Step(80, true, true);
            CheckEq("desync: respects 30s cooldown", 1, d.OffCount);
            d.Step(80, true, true);
            CheckEq("desync: re-sends off when AC stays on", 2, d.OffCount);

            Harness f = new Harness("SmartPlug");
            f.OffOk = false;
            f.Step(80, true, true);
            CheckEq("failure: attempted off once", 1, f.OffCount);
            Check("failure: plug is not marked off", !f.Cfg.PlugOff);
            CheckEq("failure: warning plus limit note", 2, f.Notes.Count);
            f.Step(80, true, true);
            CheckEq("failure: retries after cooldown", 2, f.OffCount);
            CheckEq("failure: warning is throttled", 2, f.Notes.Count);
            f.OffOk = true;
            f.Step(80, true, true);
            CheckEq("failure: succeeds on retry", 3, f.OffCount);
            Check("failure: plug-off set after success", f.Cfg.PlugOff);

            Harness s = new Harness("SmartPlug");
            s.Step(80, true, true);
            s.Cfg.RestoreOnExit = true;
            bool restored = s.Ctrl.Shutdown();
            Check("shutdown restores plug power",
                restored && !s.Cfg.PlugOff && s.OnCount == 1,
                "on=" + s.OnCount + " off=" + s.Cfg.PlugOff);

            Harness s2 = new Harness("SmartPlug");
            s2.Step(80, true, true);
            s2.Cfg.RestoreOnExit = false;
            s2.Ctrl.Shutdown();
            Check("shutdown respects restore=off",
                s2.Cfg.PlugOff && s2.OnCount == 0,
                "on=" + s2.OnCount);

            Harness r = new Harness("SmartPlug");
            r.Step(80, true, true);
            bool restoredNow = r.Ctrl.RestorePlugPower("test restore");
            Check("manual restore turns plug on",
                restoredNow && !r.Cfg.PlugOff && r.OnCount == 1,
                "on=" + r.OnCount + " plugOff=" + r.Cfg.PlugOff);

            Harness r2 = new Harness("SmartPlug");
            r2.Step(80, true, true);
            r2.OnOk = false;
            bool restoredFail = r2.Ctrl.RestorePlugPower("test restore");
            Check("manual restore failure keeps plug-off flag",
                !restoredFail && r2.Cfg.PlugOff,
                "on=" + r2.OnCount + " plugOff=" + r2.Cfg.PlugOff);
        }

        private static void TestOverride()
        {
            Harness h = new Harness("SmartPlug");
            h.Step(80, true, true);
            Check("setup: plug is off at limit", h.Cfg.PlugOff);

            h.Ctrl.ChargeToFull();
            Check("charge-to-full sets override", h.Cfg.Override);
            Check("charge-to-full turns plug on",
                !h.Cfg.PlugOff && h.OnCount == 1,
                "on=" + h.OnCount + " plugOff=" + h.Cfg.PlugOff);

            h.Step(90, true, true);
            CheckEq("no cut above limit during override", 1, h.OffCount);

            h.Step(100, true, true);
            CheckEq("no cut at 100 during override", 1, h.OffCount);
            Check("full notification shown", HasNote(h, "100%"));

            h.Ctrl.RestoreLimit();
            Check("restore clears override", !h.Cfg.Override);
            h.Step(100, true, true);
            CheckEq("restore cuts plug at 100", 2, h.OffCount);

            Harness u = new Harness("SmartPlug");
            u.Ctrl.ChargeToFull();
            Check("override armed before unplug", u.Cfg.Override);
            u.Step(60, false, false);
            Check("external unplug clears override", !u.Cfg.Override);

            Harness m = new Harness("Monitor");
            m.Ctrl.ChargeToFull();
            int before = m.Notes.Count;
            CheckEq("monitor override announces itself", 1, before);
            m.Step(80, true, true);
            CheckEq("monitor override suppresses limit warning", before, m.Notes.Count);
            m.Step(100, true, true);
            CheckEq("monitor override notifies at 100", before + 1, m.Notes.Count);
        }

        private static void TestEdgeCases()
        {
            Harness h = new Harness("SmartPlug");
            h.Cfg.Limit = 100;
            h.Cfg.Resume = 90;
            h.Step(100, true, true);
            CheckEq("limit 100: never cuts at 100", 0, h.OffCount);
            h.Step(99, true, true);
            CheckEq("limit 100: never cuts below 100", 0, h.OffCount);

            Harness n = new Harness("SmartPlug");
            n.Cfg.PlugOnUrl = "";
            n.Cfg.PlugOffUrl = "";
            Check("missing urls: falls back to monitor", !n.Cfg.EnforcePlug);
            n.Step(80, true, true);
            CheckEq("missing urls: no plug action", 0, n.OffCount);
            CheckEq("missing urls: monitor warning instead", 1, n.Notes.Count);

            Harness b = new Harness("SmartPlug");
            BatteryStatus none = new BatteryStatus();
            none.HasBattery = false;
            none.Unknown = true;
            b.Ctrl.Tick(none, b.Now);
            CheckEq("no battery: no actions", 0, b.OffCount + b.OnCount);
            CheckEq("no battery: no notifications", 0, b.Notes.Count);

            Harness ac = new Harness("SmartPlug");
            ac.Step(80, false, false);
            CheckEq("AC offline at limit: no off attempt", 0, ac.OffCount);
            ac.Step(75, false, false);
            CheckEq("AC offline and plug already on: no on attempt", 0, ac.OnCount);
        }

        private static bool HasNote(Harness h, string fragment)
        {
            foreach (string n in h.Notes)
                if (n.IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private class Harness
        {
            public AppConfig Cfg;
            public Controller Ctrl;
            public int OnCount;
            public int OffCount;
            public int SaveCount;
            public bool OnOk = true;
            public bool OffOk = true;
            public List<string> Notes = new List<string>();
            public DateTime Now = new DateTime(2026, 1, 1, 12, 0, 0);

            public Harness(string mode)
            {
                Cfg = new AppConfig();
                Cfg.Mode = mode;
                Cfg.PlugOnUrl = "http://127.0.0.1:9/on";
                Cfg.PlugOffUrl = "http://127.0.0.1:9/off";

                Ctrl = new Controller(Cfg);
                Ctrl.SendOn = delegate(string url) { OnCount++; return OnOk; };
                Ctrl.SendOff = delegate(string url) { OffCount++; return OffOk; };
                Ctrl.Notify = delegate(string msg, bool warning)
                {
                    Notes.Add((warning ? "WARN: " : "INFO: ") + msg);
                };
                Ctrl.ConfigChanged = delegate { SaveCount++; };
                Ctrl.Trace = delegate(string msg) { };
            }

            public void Step(int percent, bool ac, bool charging)
            {
                Now = Now.AddSeconds(15);
                BatteryStatus s = new BatteryStatus();
                s.HasBattery = true;
                s.Percent = percent;
                s.AcOnline = ac;
                s.Charging = charging;
                Ctrl.Tick(s, Now);
            }
        }
    }
}

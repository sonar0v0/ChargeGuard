using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ChargeGuard
{
    public class AppConfig
    {
        public int Limit = 80;
        public int Resume = 75;
        public string Mode = "Monitor";
        public string PlugOnUrl = "";
        public string PlugOffUrl = "";
        public bool Autostart = false;
        public bool Notifications = true;
        public bool RestoreOnExit = true;
        public bool Override = false;
        public bool PlugOff = false;

        public bool IsPlugMode { get { return Mode == "SmartPlug"; } }

        public static bool IsHttpUrl(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        public bool PlugConfigured
        {
            get { return IsHttpUrl(PlugOnUrl) && IsHttpUrl(PlugOffUrl); }
        }

        // Smart-plug control only makes sense with two valid URLs.
        public bool EnforcePlug { get { return IsPlugMode && PlugConfigured; } }

        public static string DefaultPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ChargeGuard");
                return Path.Combine(dir, "config.ini");
            }
        }

        public void Normalize()
        {
            if (Limit < 50) Limit = 50;
            if (Limit > 100) Limit = 100;
            if (Resume < 40) Resume = 40;
            if (Resume > 99) Resume = 99;
            if (Resume >= Limit) Resume = Math.Max(40, Limit - 5);
            if (Mode != "Monitor" && Mode != "SmartPlug") Mode = "Monitor";
            PlugOnUrl = (PlugOnUrl ?? "").Trim();
            PlugOffUrl = (PlugOffUrl ?? "").Trim();
        }

        public AppConfig Clone()
        {
            AppConfig c = new AppConfig();
            c.CopyFrom(this);
            return c;
        }

        public void CopyFrom(AppConfig o)
        {
            Limit = o.Limit;
            Resume = o.Resume;
            Mode = o.Mode;
            PlugOnUrl = o.PlugOnUrl;
            PlugOffUrl = o.PlugOffUrl;
            Autostart = o.Autostart;
            Notifications = o.Notifications;
            RestoreOnExit = o.RestoreOnExit;
            Override = o.Override;
            PlugOff = o.PlugOff;
        }

        public static AppConfig Load(string path)
        {
            AppConfig c = new AppConfig();
            try
            {
                if (!File.Exists(path)) return c;
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("["))
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    map[key] = val;
                }

                c.Limit = GetInt(map, "Limit", c.Limit);
                c.Resume = GetInt(map, "Resume", c.Resume);
                c.Mode = GetString(map, "Mode", c.Mode);
                c.PlugOnUrl = GetString(map, "PlugOnUrl", c.PlugOnUrl);
                c.PlugOffUrl = GetString(map, "PlugOffUrl", c.PlugOffUrl);
                c.Autostart = GetBool(map, "Autostart", c.Autostart);
                c.Notifications = GetBool(map, "Notifications", c.Notifications);
                c.RestoreOnExit = GetBool(map, "RestoreOnExit", c.RestoreOnExit);
                c.Override = GetBool(map, "Override", c.Override);
                c.PlugOff = GetBool(map, "PlugOff", c.PlugOff);
            }
            catch (Exception ex)
            {
                Log.Write("读取配置失败，使用默认值：" + ex.Message);
            }
            c.Normalize();
            return c;
        }

        public void Save(string path)
        {
            Normalize();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; ChargeGuard 配置。删除本文件可恢复默认值。");
            sb.AppendLine("[ChargeGuard]");
            sb.AppendLine("Limit=" + Limit.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Resume=" + Resume.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("Mode=" + Mode);
            sb.AppendLine("PlugOnUrl=" + PlugOnUrl);
            sb.AppendLine("PlugOffUrl=" + PlugOffUrl);
            sb.AppendLine("Autostart=" + (Autostart ? "True" : "False"));
            sb.AppendLine("Notifications=" + (Notifications ? "True" : "False"));
            sb.AppendLine("RestoreOnExit=" + (RestoreOnExit ? "True" : "False"));
            sb.AppendLine("Override=" + (Override ? "True" : "False"));
            sb.AppendLine("PlugOff=" + (PlugOff ? "True" : "False"));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string GetString(Dictionary<string, string> map, string key, string fallback)
        {
            string v;
            return map.TryGetValue(key, out v) ? v : fallback;
        }

        private static int GetInt(Dictionary<string, string> map, string key, int fallback)
        {
            string v;
            int n;
            if (map.TryGetValue(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;
            return fallback;
        }

        private static bool GetBool(Dictionary<string, string> map, string key, bool fallback)
        {
            string v;
            if (!map.TryGetValue(key, out v)) return fallback;
            v = v.Trim();
            return v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }
}

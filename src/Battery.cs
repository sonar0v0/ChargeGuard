using System;
using System.Runtime.InteropServices;

namespace ChargeGuard
{
    public struct BatteryStatus
    {
        public int Percent;
        public bool AcOnline;
        public bool Charging;
        public bool HasBattery;
        public bool Unknown;

        public string Describe()
        {
            if (!HasBattery) return "未检测到电池";
            if (Unknown) return "电池状态未知";
            return string.Format("{0}% · {1}{2}", Percent,
                AcOnline ? "电源已连接" : "使用电池",
                Charging ? " · 充电中" : "");
        }
    }

    public static class BatteryReader
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        public static BatteryStatus Read()
        {
            BatteryStatus s = new BatteryStatus();
            SYSTEM_POWER_STATUS sps;
            if (!GetSystemPowerStatus(out sps))
            {
                s.Unknown = true;
                return s;
            }

            // BatteryFlag: 8 = charging, 128 = no system battery, 255 = unknown.
            s.HasBattery = (sps.BatteryFlag & 128) == 0 && sps.BatteryLifePercent != 255;
            s.Unknown = sps.BatteryFlag == 255 || sps.BatteryLifePercent == 255;
            s.Percent = s.Unknown ? -1 : (int)sps.BatteryLifePercent;
            s.Charging = (sps.BatteryFlag & 8) != 0;
            s.AcOnline = sps.ACLineStatus == 1 || (sps.ACLineStatus == 255 && s.Charging);
            return s;
        }
    }
}

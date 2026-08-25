using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HeadsetControlTaskbarBatteryIndicator
{
    public static class HeadsetControlNative
    {
        public const string DllName = "headsetcontrol.dll";

        public enum BatteryStatus
        {
            Unavailable = -1,
            Charging = -2,
            Available = 0,
            Error = -100,
            Timeout = -101
        }

        public enum Capability
        {
            Sidetone = 0,
            BatteryStatus = 1,
            NotificationSound = 2,
            Lights = 3,
            InactiveTime = 4,
            ChatmixStatus = 5,
            VoicePrompts = 6,
            RotateToMute = 7,
            EqualizerPreset = 8,
            Equalizer = 9,
            ParametricEqualizer = 10,
            MicrophoneMuteLedBrightness = 11,
            MicrophoneVolume = 12,
            VolumeLimiter = 13,
            BtWhenPoweredOn = 14,
            BtCallVolume = 15,
            NumCapabilities = 16
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HscBattery
        {
            public int LevelPercent;
            public BatteryStatus Status;
            public int VoltageMv;
            public int TimeToFullMin;
            public int TimeToEmptyMin;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr hsc_version();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hsc_discover(out IntPtr headsetsPtr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void hsc_free_headsets(IntPtr headsetsPtr, int count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr hsc_get_name(IntPtr headset);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr hsc_get_product_name(IntPtr headset);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr hsc_get_vendor_name(IntPtr headset);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ushort hsc_get_vendor_id(IntPtr headset);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ushort hsc_get_product_id(IntPtr headset);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool hsc_supports(IntPtr headset, Capability cap);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hsc_get_battery(IntPtr headset, ref HscBattery battery);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hsc_set_inactive_time(IntPtr headset, byte minutes, IntPtr result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int hsc_dump_hid_devices([Out] StringBuilder buffer, int max_len);

        public static string Utf8PtrToString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return string.Empty;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) len++;
            byte[] buffer = new byte[len];
            Marshal.Copy(ptr, buffer, 0, len);
            return Encoding.UTF8.GetString(buffer);
        }
    }
}

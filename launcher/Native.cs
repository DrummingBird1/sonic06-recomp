using System;
using System.Runtime.InteropServices;

namespace Sonic06Launcher
{
    /// <summary>The game window's size and style, for the --play test; the screen's modes.</summary>
    static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);

        /// <summary>The primary screen's modes as (width, height), each once; empty if Windows does not say.</summary>
        public static System.Collections.Generic.HashSet<(int, int)> ScreenModes()
        {
            var set = new System.Collections.Generic.HashSet<(int, int)>();
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            for (var i = 0; i < 1000 && EnumDisplaySettings(null, i, ref dm); i++)
                if (dm.dmBitsPerPel >= 32) set.Add((dm.dmPelsWidth, dm.dmPelsHeight));
            return set;
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);

        public static string Describe(IntPtr h)
        {
            if (h == IntPtr.Zero) return "no window";
            GetClientRect(h, out var c);
            GetWindowRect(h, out var w);
            const uint WS_POPUP = 0x80000000;
            var popup = ((uint)GetWindowLong(h, -16) & WS_POPUP) != 0;   // GWL_STYLE
            return $"client {c.Right - c.Left}x{c.Bottom - c.Top}, window at {w.Left},{w.Top} " +
                   $"{w.Right - w.Left}x{w.Bottom - w.Top}, {(popup ? "borderless" : "framed")}";
        }
    }
}

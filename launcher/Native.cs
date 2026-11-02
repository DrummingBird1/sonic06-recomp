using System;
using System.Runtime.InteropServices;

namespace Sonic06Launcher
{
    /// <summary>The game window's size and style, for the --play test.</summary>
    static class Native
    {
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

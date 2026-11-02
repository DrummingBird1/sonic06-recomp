using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Sonic06Launcher
{
    /// <summary>
    /// The player's settings, saved in launcher.local.json and handed to the
    /// game as the environment switches the runtime reads.
    /// </summary>
    class GameSettings
    {
        public bool Fullscreen;              // D3D12_FULLSCREEN: borderless, over the primary monitor
        public string WindowSize = "1280x720"; // D3D12_WINDOW: the frame is stretched to it
        public bool VSync = true;            // D3D12_VSYNC=0 turns it off
        public string Language = "";         // PS3_LANG; empty is the Windows language
        public bool Mute;                    // PS3_MUTE
        public bool Keyboard = true;         // PAD_NO_KEYBOARD turns the keyboard pad off

        public static readonly string[] Sizes = { "1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160" };
        // The languages the European disc carries, as PS3_LANG codes.
        public static readonly (string Code, string Name)[] Languages =
        {
            ("", "Same as Windows"), ("en", "English"), ("ja", "Japanese"), ("fr", "French"),
            ("de", "German"), ("it", "Italian"), ("es", "Spanish"),
        };

        /// <summary>The largest size that leaves a margin on the primary screen: 1280x720 is small on a 4K monitor.</summary>
        static string DefaultSize()
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;   // pixels: the launcher is DPI-aware
            var fit = Sizes.LastOrDefault(s =>
            {
                var wh = s.Split('x');
                return int.Parse(wh[0]) <= screen.Width * 0.8 && int.Parse(wh[1]) <= screen.Height * 0.8;
            });
            return fit ?? Sizes[0];
        }

        public static GameSettings Load(Config c) => new GameSettings
        {
            Fullscreen = c.Bool("fullscreen"),
            WindowSize = c.Str("windowSize", DefaultSize()),
            VSync = c.Bool("vsync", true),
            Language = c.Str("language"),
            Mute = c.Bool("mute"),
            Keyboard = c.Bool("keyboard", true),
        };

        public void Save(Config c)
        {
            c.Set("fullscreen", Fullscreen); c.Set("windowSize", WindowSize); c.Set("vsync", VSync);
            c.Set("language", Language); c.Set("mute", Mute); c.Set("keyboard", Keyboard);
        }

        public Dictionary<string, string> Env()
        {
            var e = new Dictionary<string, string>();
            if (Fullscreen) e["D3D12_FULLSCREEN"] = "1"; else e["D3D12_WINDOW"] = WindowSize;
            if (!VSync) e["D3D12_VSYNC"] = "0";
            if (Language.Length > 0) e["PS3_LANG"] = Language;
            // Both are read as "set or not", so they are only set when on.
            if (Mute) e["PS3_MUTE"] = "1";
            if (!Keyboard) e["PAD_NO_KEYBOARD"] = "1";
            return e;
        }
    }

    /// <summary>Starts build\sonic06.exe the way tools\run.ps1 does, with its output in work\logs\run-*.log.</summary>
    static class Game
    {
        public static Process Start(Setup setup, GameSettings s, Action<string> log, Action<int> exited)
        {
            Directory.CreateDirectory(Workspace.Logs);
            var logFile = Path.Combine(Workspace.Logs, $"run-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var psi = new ProcessStartInfo(Workspace.GameExe, Runner.Quote(Workspace.EbootElf))
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, WorkingDirectory = Workspace.Root,
            };
            psi.EnvironmentVariables["PS3_VFS_ROOT"] = Workspace.Disc;
            psi.EnvironmentVariables["PS3_TITLE"] = "Sonic the Hedgehog";
            psi.EnvironmentVariables["PS3_VERBOSE"] = "0";   // the per-call log slows the game down
            var ff = setup.FfmpegBin();
            if (ff != null) psi.EnvironmentVariables["PATH"] = ff + ";" + psi.EnvironmentVariables["PATH"];
            foreach (var kv in s.Env()) psi.EnvironmentVariables[kv.Key] = kv.Value;

            var w = new StreamWriter(logFile);
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            DataReceivedEventHandler write = (o, e) => { if (e.Data != null) lock (w) w.WriteLine(e.Data); };
            p.OutputDataReceived += write;
            p.ErrorDataReceived += write;
            p.Exited += (o, e) =>
            {
                p.WaitForExit();   // drains the redirected streams before the log is closed
                lock (w) w.Dispose();
                exited(p.ExitCode);
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            log("Game started. Its log: " + logFile.Substring(Workspace.Root.Length + 1));
            return p;
        }
    }
}

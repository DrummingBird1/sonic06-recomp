using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Sonic06Launcher
{
    /// <summary>
    /// The player's settings, saved in launcher.local.json as they change and
    /// handed to the game as the environment switches the runtime reads.
    /// </summary>
    public class GameSettings : INotifyPropertyChanged
    {
        Config cfg;
        public event PropertyChangedEventHandler PropertyChanged;

        bool fullscreen, vsync = true, mute, keyboard = true, fxaa, escCloses, shaderAsync;
        string windowSize = "1280x720", language = "", aspect = "keep", scaler = "bilinear", resScale = "auto";
        int sharpen, aniso, brightness = 100, deadzone = 24;
        string adapter = "";
        bool mouseLook, mouseInvertX, mouseInvertY;
        int mouseSens = 30;
        readonly int[][] keys = DefaultKeyMap();

        // Display and sound. D3D12_FULLSCREEN, D3D12_WINDOW, D3D12_VSYNC=0, PS3_LANG, PS3_MUTE.
        public bool Fullscreen { get => fullscreen; set => Set(ref fullscreen, value); }
        public string WindowSize { get => windowSize; set => Set(ref windowSize, value); }
        public bool VSync { get => vsync; set => Set(ref vsync, value); }
        public string Language { get => language; set => Set(ref language, value ?? ""); }
        public bool Mute { get => mute; set => Set(ref mute, value); }
        // Controls. PAD_NO_KEYBOARD, D3D12_ESC=ignore.
        public bool Keyboard { get => keyboard; set => Set(ref keyboard, value); }
        public bool EscCloses { get => escCloses; set => Set(ref escCloses, value); }
        // The present pass (patch 0031): D3D12_ASPECT, D3D12_SCALER, D3D12_SHARPEN,
        // D3D12_AA, D3D12_BRIGHTNESS, and D3D12_ANISO for the textures.
        public string Aspect { get => aspect; set => Set(ref aspect, value); }
        public string Scaler { get => scaler; set => Set(ref scaler, value); }
        public int Sharpen { get => sharpen; set => Set(ref sharpen, Math.Max(0, Math.Min(100, value))); }   // percent
        public bool Fxaa { get => fxaa; set => Set(ref fxaa, value); }
        public int Aniso { get => aniso; set => Set(ref aniso, value); }                                      // 0 = the game's own
        public int Brightness { get => brightness; set => Set(ref brightness, Math.Max(50, Math.Min(150, value))); } // percent
        // Internal resolution (patch 0032): D3D12_RES_SCALE, or "auto" to pick one from the window.
        public string ResScale { get => resScale; set => Set(ref resScale, value); }
        // Shaders compile in the background (patch 0038): D3D12_SHADER_ASYNC. Off by default, because
        // a draw is skipped until its shader is ready.
        public bool ShaderAsync { get => shaderAsync; set => Set(ref shaderAsync, value); }

        // The graphics card (CELLMARK_ADAPTER, a part of its name); "" is the high-performance one.
        public string Adapter { get => adapter; set => Set(ref adapter, value ?? ""); }
        // How far the stick must move before it counts (PAD_DEADZONE, percent of its travel).
        public int Deadzone { get => deadzone; set => Set(ref deadzone, Math.Max(0, Math.Min(50, value))); }

        // The mouse as the right stick (PAD_MOUSE=<sensitivity>), and the keys (PAD_KEYS).
        public bool MouseLook { get => mouseLook; set => Set(ref mouseLook, value); }
        public int MouseSens { get => mouseSens; set => Set(ref mouseSens, Math.Max(1, Math.Min(100, value))); }
        // Off: the view turns where the mouse goes. The game orbits its camera with the stick, so that is
        // the stick's x turned around (PAD_MOUSE_FLIP=x) and its y as it is (measured in Wave Ocean).
        public bool MouseInvertX { get => mouseInvertX; set => Set(ref mouseInvertX, value); }
        public bool MouseInvertY { get => mouseInvertY; set => Set(ref mouseInvertY, value); }

        /// <summary>The pad controls a key can be bound to, as cellPad's PAD_KEYS names them.</summary>
        public static readonly string[] Controls =
        {
            "up", "down", "left", "right", "cam_up", "cam_down", "cam_left", "cam_right",
            "cross", "circle", "square", "triangle", "l1", "r1", "l2", "r2", "l3", "r3", "start", "select",
        };
        // Windows virtual keys, two per control: the game's map without PAD_KEYS (cellPad.c).
        static int[][] DefaultKeyMap() => new[]
        {
            new[] { 0x26, 0 }, new[] { 0x28, 0 }, new[] { 0x25, 0 }, new[] { 0x27, 0 },
            new[] { 0, 0 }, new[] { 0, 0 }, new[] { 0, 0 }, new[] { 0, 0 },
            new[] { (int)'Z', 0 }, new[] { (int)'X', 0 }, new[] { (int)'A', 0 }, new[] { (int)'S', 0 },
            new[] { (int)'Q', 0 }, new[] { (int)'W', 0 }, new[] { (int)'1', 0 }, new[] { (int)'2', 0 },
            new[] { 0, 0 }, new[] { 0, 0 }, new[] { 0x0D, 0 }, new[] { 0x09, 0 },
        };

        public int GetKey(int control, int slot) => keys[control][slot];

        /// <summary>Binds a key (0 clears the slot). A key belongs to one control: it leaves any other it had.</summary>
        public void SetKey(int control, int slot, int vk)
        {
            if (vk != 0)
                foreach (var k in keys) for (var j = 0; j < 2; j++) if (k[j] == vk) k[j] = 0;
            keys[control][slot] = vk;
            KeysChanged();
        }

        public void ResetKeys()
        {
            var d = DefaultKeyMap();
            for (var i = 0; i < keys.Length; i++) { keys[i][0] = d[i][0]; keys[i][1] = d[i][1]; }
            KeysChanged();
        }

        void KeysChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Keys"));
            Save();
        }

        /// <summary>"cross=0x5A,start=0x0D+0x20,...": every control that has a key.</summary>
        string KeysText() => string.Join(",", Controls.Select((n, i) => (n, k: keys[i]))
            .Where(c => c.k[0] != 0 || c.k[1] != 0)
            .Select(c => c.n + "=" + string.Join("+", c.k.Where(v => v != 0).Select(v => "0x" + v.ToString("X2")))));

        bool KeysAreDefault()
        {
            var d = DefaultKeyMap();
            return keys.Select((k, i) => k[0] == d[i][0] && k[1] == d[i][1]).All(x => x);
        }

        void LoadKeys(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var k in keys) { k[0] = 0; k[1] = 0; }
            foreach (var part in text.Split(','))
            {
                var eq = part.IndexOf('=');
                if (eq < 0) continue;
                var i = Array.IndexOf(Controls, part.Substring(0, eq));
                if (i < 0) continue;
                var vs = part.Substring(eq + 1).Split('+');
                for (var j = 0; j < 2 && j < vs.Length; j++)
                    try { keys[i][j] = Convert.ToInt32(vs[j], 16) & 0xFF; } catch (Exception) { /* a slot that does not parse stays empty */ }
            }
        }

        /// <summary>F12 saves a BMP here.</summary>
        public static string ScreenshotDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Sonic06");

        /// <summary>The names of the graphics cards Windows reports, for the picker.</summary>
        public static string[] Adapters()
        {
            try
            {
                using (var q = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    return q.Get().Cast<System.Management.ManagementBaseObject>()
                            .Select(o => (o["Name"] as string ?? "").Trim()).Where(n => n.Length > 0).Distinct().ToArray();
            }
            catch (Exception) { return new string[0]; }
        }

        public bool HasMyPreset => cfg != null && cfg.Str("my_resScale").Length > 0;

        /// <summary>Keeps the picture settings as the player's own preset.</summary>
        public void SaveMyPreset()
        {
            if (cfg == null) return;
            cfg.Put("my_resScale", resScale); cfg.Put("my_scaler", scaler); cfg.Put("my_sharpen", sharpen.ToString());
            cfg.Put("my_fxaa", fxaa); cfg.Put("my_aniso", aniso.ToString()); cfg.Put("my_brightness", brightness.ToString());
            cfg.Put("my_aspect", aspect); cfg.Put("my_vsync", vsync); cfg.Put("my_shaderAsync", shaderAsync);
            cfg.Save();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMyPreset)));
        }

        public static readonly string[] Sizes = { "1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160" };
        public static readonly string[] ResScales = { "1", "1.5", "2", "3" };
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>The size the game draws at for a scale, as "2560 × 1440".</summary>
        public static string ResSize(string scale)
        {
            var k = double.Parse(scale, Inv);
            return $"{1280 * k:0} × {720 * k:0}";
        }

        /// <summary>The scale to run with: the chosen one, or for "auto" the smallest that
        /// covers the picture in the window (or on the screen, in fullscreen).</summary>
        public string EffectiveResScale()
        {
            if (ResScale != "auto") return ResScales.Contains(ResScale) ? ResScale : "1";
            int w, h;
            if (Fullscreen)
            {
                var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;   // pixels: the launcher is DPI-aware
                w = b.Width; h = b.Height;
            }
            else
            {
                var wh = WindowSize.Split('x');
                if (wh.Length != 2 || !int.TryParse(wh[0], out w) || !int.TryParse(wh[1], out h)) return "1";
            }
            // Keep: the picture is as large as the smaller side allows. Stretch: it fills both.
            double need = Aspect == "stretch" ? Math.Max(w / 1280.0, h / 720.0) : Math.Min(w / 1280.0, h / 720.0);
            return ResScales.FirstOrDefault(s => double.Parse(s, Inv) >= need - 0.05) ?? ResScales.Last();
        }
        // The languages the European disc carries, as PS3_LANG codes; "" is the Windows language.
        public static readonly (string Code, string Key)[] GameLanguages =
        {
            ("", "lang_system"), ("en", "lang_en"), ("ja", "lang_ja"), ("fr", "lang_fr"),
            ("de", "lang_de"), ("it", "lang_it"), ("es", "lang_es"),
        };

        void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            Save();
        }

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

        static int Int(Config c, string key, int def) => int.TryParse(c.Str(key, def.ToString()), out var v) ? v : def;

        public static GameSettings Load(Config c)
        {
            var s = new GameSettings
            {
                fullscreen = c.Bool("fullscreen"), windowSize = c.Str("windowSize", DefaultSize()), vsync = c.Bool("vsync", true),
                language = c.Str("language"), mute = c.Bool("mute"), keyboard = c.Bool("keyboard", true),
                escCloses = c.Bool("escCloses"), aspect = c.Str("aspect", "keep"), scaler = c.Str("scaler", "bilinear"),
                sharpen = Int(c, "sharpen", 0), fxaa = c.Bool("fxaa"), aniso = Int(c, "aniso", 0), brightness = Int(c, "brightness", 100),
                resScale = c.Str("resScale", "auto"), shaderAsync = c.Bool("shaderAsync"),
                adapter = c.Str("adapter"), deadzone = Int(c, "deadzone", 24),
                mouseLook = c.Bool("mouseLook"), mouseSens = Int(c, "mouseSens", 30),
                mouseInvertX = c.Bool("mouseInvertX"), mouseInvertY = c.Bool("mouseInvertY"),
            };
            s.LoadKeys(c.Str("keys"));
            s.cfg = c;
            return s;
        }

        void Save()
        {
            if (cfg == null) return;
            cfg.Put("fullscreen", fullscreen); cfg.Put("windowSize", windowSize); cfg.Put("vsync", vsync);
            cfg.Put("language", language); cfg.Put("mute", mute); cfg.Put("keyboard", keyboard); cfg.Put("escCloses", escCloses);
            cfg.Put("aspect", aspect); cfg.Put("scaler", scaler); cfg.Put("sharpen", sharpen.ToString());
            cfg.Put("fxaa", fxaa); cfg.Put("aniso", aniso.ToString()); cfg.Put("brightness", brightness.ToString());
            cfg.Put("resScale", resScale); cfg.Put("shaderAsync", shaderAsync);
            cfg.Put("adapter", adapter); cfg.Put("deadzone", deadzone.ToString());
            cfg.Put("mouseLook", mouseLook); cfg.Put("mouseSens", mouseSens.ToString());
            cfg.Put("mouseInvertX", mouseInvertX); cfg.Put("mouseInvertY", mouseInvertY);
            cfg.Put("keys", KeysAreDefault() ? "" : KeysText());
            cfg.Save();
        }

        /// <summary>Performance, balanced and quality: starting points the player can change.</summary>
        public void Preset(string name)
        {
            switch (name)
            {
                case "mine":
                    if (!HasMyPreset) return;
                    ResScale = cfg.Str("my_resScale", "auto"); Scaler = cfg.Str("my_scaler", "bilinear");
                    Sharpen = Int(cfg, "my_sharpen", 0); Fxaa = cfg.Bool("my_fxaa"); Aniso = Int(cfg, "my_aniso", 0);
                    Brightness = Int(cfg, "my_brightness", 100); Aspect = cfg.Str("my_aspect", "keep");
                    VSync = cfg.Bool("my_vsync", true); ShaderAsync = cfg.Bool("my_shaderAsync");
                    break;
                case "performance": ResScale = "1"; Scaler = "bilinear"; Sharpen = 0; Fxaa = false; Aniso = 0; break;
                case "balanced": ResScale = "auto"; Scaler = "sharp"; Sharpen = 25; Fxaa = false; Aniso = 4; break;
                case "quality": ResScale = "auto"; Scaler = "bicubic"; Sharpen = 40; Fxaa = true; Aniso = 16; break;
                default: ResScale = "auto"; Scaler = "bilinear"; Sharpen = 0; Fxaa = false; Aniso = 0; Aspect = "keep"; Brightness = 100; VSync = true; ShaderAsync = false; break;
            }
        }

        public Dictionary<string, string> Env()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var e = new Dictionary<string, string>();
            if (Fullscreen) e["D3D12_FULLSCREEN"] = "1"; else e["D3D12_WINDOW"] = WindowSize;
            if (!VSync) e["D3D12_VSYNC"] = "0";
            if (Language.Length > 0) e["PS3_LANG"] = Language;
            // These two are read as "set or not", so they are only set when on.
            if (Mute) e["PS3_MUTE"] = "1";
            if (!Keyboard) e["PAD_NO_KEYBOARD"] = "1";
            if (!EscCloses) e["D3D12_ESC"] = "ignore";
            e["D3D12_ASPECT"] = Aspect;
            e["D3D12_SCALER"] = Scaler;
            if (Sharpen > 0) e["D3D12_SHARPEN"] = (Sharpen / 100.0).ToString("0.00", inv);
            if (Fxaa) e["D3D12_AA"] = "fxaa";
            if (Aniso > 1) e["D3D12_ANISO"] = Aniso.ToString(inv);
            if (Brightness != 100) e["D3D12_BRIGHTNESS"] = (Brightness / 100.0).ToString("0.00", inv);
            var res = EffectiveResScale();
            if (res != "1") e["D3D12_RES_SCALE"] = res;
            if (ShaderAsync) e["D3D12_SHADER_ASYNC"] = "1";
            if (Adapter.Length > 0) e["CELLMARK_ADAPTER"] = Adapter;
            if (Deadzone != 24) e["PAD_DEADZONE"] = Deadzone.ToString(inv);
            // Without PAD_KEYS the game has its own default map; "start=0" is a map with no key at all.
            if (!KeysAreDefault()) { var k = KeysText(); e["PAD_KEYS"] = k.Length > 0 ? k : "start=0"; }
            if (MouseLook)
            {
                e["PAD_MOUSE"] = MouseSens.ToString(inv);
                var flip = (MouseInvertX ? "" : "x") + (MouseInvertY ? "y" : "");
                if (flip.Length > 0) e["PAD_MOUSE_FLIP"] = flip;
            }
            e["D3D12_SCREENSHOT_DIR"] = ScreenshotDir;
            return e;
        }
    }

    /// <summary>Starts build\sonic06.exe the way tools\run.ps1 does, with its output in work\logs\run-*.log.</summary>
    static class Game
    {
        public static Process Start(Setup setup, GameSettings s, Action<string> log, Action<int> exited)
        {
            Directory.CreateDirectory(Workspace.Logs);
            try { Directory.CreateDirectory(GameSettings.ScreenshotDir); } catch (Exception) { /* F12 then has nowhere to save */ }
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

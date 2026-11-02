using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace Sonic06Launcher
{
    static class Program
    {
        /// <summary>
        /// Launcher.exe opens the window. For tests without a screen:
        ///   --check out.txt                        the setup status, in English
        ///   --smoke out.txt                        opens the window, closes it, writes "window ok"
        ///   --screenshot &lt;page 0-4&gt; &lt;lang&gt; out.png  the window on a page, in a language
        ///   --report out.txt                       a bug report; its path into out.txt
        ///   --play &lt;seconds&gt; out.txt              the game with the saved settings, then its window size
        ///   --setup &lt;iso&gt; &lt;rpcs3.exe&gt; log.txt     "Set up everything" without dialogs (tools installed)
        ///   --install-winsysroot --accept-microsoft-license log.txt
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            if (!Workspace.Find())
            {
                MessageBox.Show("Launcher.exe has to be in the sonic06-recomp folder (next to tools\\ and src\\).", "sonic06-recomp");
                return 1;
            }
            if (args.Length == 2 && args[0] == "--check")
            {
                var s = new Setup(Config.Load(), _ => { }, (_, __) => { });
                File.WriteAllLines(args[1], s.Check().Select(i => $"[{(i.Ok ? "ok" : "--")}] {i.English}"));
                return 0;
            }
            if (args.Length == 2 && args[0] == "--report")
            {
                var c = Config.Load();
                var s = new Setup(c, _ => { }, (_, __) => { });
                File.WriteAllText(args[1], Support.CreateReport(s, GameSettings.Load(c), new[] { "(report made with --report)" }));
                return 0;
            }
            if (args.Length == 3 && args[0] == "--install-winsysroot" && args[1] == "--accept-microsoft-license")
            {
                // The CRT and SDK step alone, for testing; the flag is the license acceptance.
                return Logged(args[2], (s, log) =>
                {
                    s.InstallTools(Setup.Packages.Where(p => p.Install != null), true).GetAwaiter().GetResult();
                    log("WINSYSROOT OK: " + Tools.WinSysRoot());
                });
            }
            if (args.Length == 4 && args[0] == "--setup")
            {
                var c = Config.Load();
                c.Set("rpcs3", args[2]);
                return Logged(args[3], (s, log) =>
                {
                    if (s.MissingTools().Count > 0) throw new InvalidOperationException("build tools missing");
                    if (!Directory.Exists(Path.Combine(Workspace.Disc, "PS3_GAME"))) s.FromIso(args[1]).GetAwaiter().GetResult();
                    if (!File.Exists(Workspace.EbootElf)) s.Decrypt().GetAwaiter().GetResult();
                    if (s.FfmpegBin() == null || s.FfmpegInclude() == null) s.DownloadFfmpeg().GetAwaiter().GetResult();
                    if (!File.Exists(Workspace.GameExe)) s.Build().GetAwaiter().GetResult();
                    log("SETUP OK");
                });
            }
            if (args.Length == 3 && args[0] == "--play" && int.TryParse(args[1], out var seconds))
            {
                // Starts the game with the saved settings, and after the given time
                // writes the size of its window and closes it.
                var c = Config.Load();
                var s = new Setup(c, _ => { }, (_, __) => { });
                var lines = new System.Collections.Generic.List<string>();
                var p = Game.Start(s, GameSettings.Load(c), l => lines.Add(l), code => { });
                System.Threading.Thread.Sleep(seconds * 1000);
                p.Refresh();
                lines.Add(Native.Describe(p.MainWindowHandle));
                lines.Add("exited before the end: " + p.HasExited);
                if (!p.HasExited) { p.Kill(); p.WaitForExit(10000); }
                File.WriteAllLines(args[2], lines);
                return 0;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Launcher;component/Theme.xaml") });
            var w = new MainWindow();
            if (args.Length == 2 && args[0] == "--smoke")
                w.ContentRendered += (o, e) => { File.WriteAllText(args[1], "window ok"); w.Close(); };
            if (args.Length == 4 && args[0] == "--screenshot" && int.TryParse(args[1], out var page))
                w.ContentRendered += async (o, e) =>
                {
                    Loc.I.Set(args[2]);
                    await System.Threading.Tasks.Task.Delay(800);   // the bindings settle
                    w.SaveShot(page, args[3]);
                    w.Close();
                };
            app.Run(w);
            return 0;
        }

        /// <summary>A test step that logs to a file and ends with OK or FAILED.</summary>
        static int Logged(string file, Action<Setup, Action<string>> body)
        {
            using (var w = new StreamWriter(file) { AutoFlush = true })
            {
                Action<string> log = l => { lock (w) w.WriteLine($"{DateTime.Now:HH:mm:ss} {l}"); };
                try { body(new Setup(Config.Load(), log, (_, __) => { }), log); return 0; }
                catch (Exception e) { log("FAILED: " + e.Message); return 1; }
            }
        }
    }
}

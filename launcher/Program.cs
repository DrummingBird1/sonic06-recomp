using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Sonic06Launcher
{
    static class Program
    {
        /// <summary>
        /// Launcher.exe opens the window. For tests without a screen:
        ///   Launcher.exe --check out.txt   writes the setup status and exits
        ///   Launcher.exe --smoke out.txt   opens the window, closes it, writes "window ok"
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (!Workspace.Find())
            {
                MessageBox.Show("Launcher.exe has to be in the sonic06-recomp folder (next to tools\\ and src\\).",
                                "sonic06-recomp", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            if (args.Length == 2 && args[0] == "--check")
            {
                var s = new Setup(Config.Load(), _ => { }, (_, __) => { });
                File.WriteAllLines(args[1], s.Check().Select(i => $"[{(i.Ok ? "ok" : "--")}] {i.Step}: {i.Detail}"));
                return 0;
            }
            if (args.Length == 2 && args[0] == "--report")
            {
                var c = Config.Load();
                var s = new Setup(c, _ => { }, (_, __) => { });
                File.WriteAllText(args[1], Support.CreateReport(s, GameSettings.Load(c), new[] { "(report made with --report)" }));
                return 0;
            }
            if (args.Length == 4 && args[0] == "--setup")
            {
                // "Set up everything" without dialogs, for testing a clean checkout:
                // --setup <your.iso> <rpcs3.exe> <log file>. Build tools must be installed.
                var c = Config.Load();
                c.Set("rpcs3", args[2]);
                using (var w = new StreamWriter(args[3]) { AutoFlush = true })
                {
                    Action<string> log = l => { lock (w) w.WriteLine($"{DateTime.Now:HH:mm:ss} {l}"); };
                    var s = new Setup(c, log, (_, __) => { });
                    try
                    {
                        if (s.MissingTools().Count > 0) throw new InvalidOperationException("build tools missing");
                        if (!Directory.Exists(Path.Combine(Workspace.Disc, "PS3_GAME"))) s.FromIso(args[1]).GetAwaiter().GetResult();
                        if (!File.Exists(Workspace.EbootElf)) s.Decrypt().GetAwaiter().GetResult();
                        if (s.FfmpegBin() == null || s.FfmpegInclude() == null) s.DownloadFfmpeg().GetAwaiter().GetResult();
                        if (!File.Exists(Workspace.GameExe)) s.Build().GetAwaiter().GetResult();
                        log("SETUP OK");
                        return 0;
                    }
                    catch (Exception e) { log("SETUP FAILED: " + e.Message); return 1; }
                }
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
            var form = new MainForm();
            if (args.Length == 2 && args[0] == "--smoke")
            {
                form.Shown += (o, e) => { File.WriteAllText(args[1], "window ok"); form.Close(); };
            }
            if (args.Length == 3 && args[0] == "--screenshot" && int.TryParse(args[1], out var tab))
            {
                // The window drawn into a PNG by itself (tab 0, 1 or 2), not a
                // screen capture: nothing else on the screen gets in.
                form.Shown += async (o, e) =>
                {
                    form.SelectTab(tab);
                    await System.Threading.Tasks.Task.Delay(1500);   // the status check fills in
                    using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.Close();
                };
            }
            Application.Run(form);
            return 0;
        }
    }
}

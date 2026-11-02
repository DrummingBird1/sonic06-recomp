using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Sonic06Launcher
{
    /// <summary>Updating the checkout, and the bug report a player attaches to an issue.</summary>
    static class Support
    {
        // Set once the project has a public repository; until then the
        // launcher only says where the report was saved.
        public const string IssuesUrl = "";

        /// <summary>git pull, and when anything came in, the toolkit, the lift and the build again.</summary>
        public static async Task Update(Setup setup, Action<string> log)
        {
            var git = Tools.Git() ?? throw new InvalidOperationException("Git is not installed.");
            if (!Directory.Exists(Workspace.P(".git")))
                throw new InvalidOperationException("Updates need a git checkout of the project (git clone).");
            var before = Runner.Capture(git, "rev-parse HEAD");
            var rc = await Runner.Run(git, "pull --ff-only", log);
            if (rc != 0) throw new InvalidOperationException($"git pull failed (exit code {rc}).");
            if (Runner.Capture(git, "rev-parse HEAD") == before) { log("Already up to date."); return; }
            // New patches or a new lifter change the translated code, so all of it is redone.
            await setup.Build(resetToolkit: true);
        }

        /// <summary>
        /// A zip on the desktop: this PC's specs, the setup status, the launcher's
        /// log and the last game logs. No game files. The Windows user folder is
        /// replaced by %USERPROFILE% in every text.
        /// </summary>
        public static string CreateReport(Setup setup, GameSettings s, IEnumerable<string> launcherLog)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var zip = Path.Combine(desktop, $"sonic06-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                Add(z, "system.txt", SystemInfo(setup, s));
                Add(z, "launcher.log", string.Join(Environment.NewLine, launcherLog));
                if (Directory.Exists(Workspace.Logs))
                    foreach (var f in new DirectoryInfo(Workspace.Logs).GetFiles("run-*.log")
                                                                     .OrderByDescending(f => f.LastWriteTime).Take(3))
                        Add(z, "logs/" + f.Name, Tail(f.FullName, 4 << 20));
            }
            return zip;
        }

        static void Add(ZipArchive z, string name, string text)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 3) text = text.Replace(home, "%USERPROFILE%");
            using (var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false))) w.Write(text);
        }

        static string Tail(string file, int max)
        {
            using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (f.Length > max) f.Seek(-max, SeekOrigin.End);
                using (var r = new StreamReader(f)) return (f.Position > 0 ? "[earlier lines cut]\n" : "") + r.ReadToEnd();
            }
        }

        static string Wmi(string query, Func<ManagementBaseObject, string> f)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(query))
                    return string.Join("; ", s.Get().Cast<ManagementBaseObject>().Select(f));
            }
            catch (Exception e) { return "unknown (" + e.Message + ")"; }
        }

        static string SystemInfo(Setup setup, GameSettings s)
        {
            var b = new StringBuilder();
            var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            b.AppendLine($"Launcher: {typeof(Support).Assembly.GetName().Version}");
            // ProductName still says "Windows 10" on Windows 11; the build number tells them apart.
            var product = (cv?.GetValue("ProductName") as string) ?? "Windows";
            if (int.TryParse(cv?.GetValue("CurrentBuild") as string, out var build) && build >= 22000)
                product = product.Replace("Windows 10", "Windows 11");
            b.AppendLine($"Windows: {product} {cv?.GetValue("DisplayVersion")} build {build}.{cv?.GetValue("UBR")}");
            b.AppendLine("CPU: " + Wmi("SELECT Name, NumberOfLogicalProcessors FROM Win32_Processor",
                                         o => $"{o["Name"]} ({o["NumberOfLogicalProcessors"]} threads)"));
            b.AppendLine($"Memory: {Tools.RamGb():0.0} GB, build jobs {Tools.BuildJobs()}");
            b.AppendLine("GPU: " + Wmi("SELECT Name, DriverVersion FROM Win32_VideoController",
                                         o => $"{o["Name"]} (driver {o["DriverVersion"]})"));
            var git = Tools.Git();
            if (git != null)
            {
                b.AppendLine("Project commit: " + Runner.Capture(git, "rev-parse --short HEAD"));
                if (Directory.Exists(Workspace.P("work", "ps3recomp", ".git")))
                    b.AppendLine("Toolkit commit: " + Runner.Capture(git, "-C work\\ps3recomp log --oneline -1"));
            }
            b.AppendLine("Visual Studio: " + (Tools.VisualStudio() ?? "not found"));
            var clang = Tools.ClangCl();
            b.AppendLine("clang-cl: " + (clang != null ? Runner.Capture(clang, "--version").Split('\n')[0] : "not found"));
            b.AppendLine("Settings: " + string.Join(" ", s.Env().Select(kv => kv.Key + "=" + kv.Value)));
            b.AppendLine();
            b.AppendLine("Setup:");
            foreach (var i in setup.Check()) b.AppendLine($"  [{(i.Ok ? "ok" : "--")}] {i.English}");
            return b.ToString();
        }
    }
}

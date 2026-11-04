using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace Sonic06Launcher
{
    /// <summary>
    /// The release is one file, Soleanna-x.y.z.exe, that carries the source the build needs
    /// (payload.zip: tools, src, patches, CMakeLists.txt and the license; no documents and
    /// nothing from the game), put in by tools/package_release.ps1. Started anywhere outside
    /// an install, it unpacks that source into a folder the player picks, copies itself there
    /// as Soleanna.exe, adds a desktop shortcut and starts that copy. Started again later, it
    /// opens the installed copy, or first updates it when it carries a different version.
    /// A developer build has no payload and works in its checkout, as before.
    /// </summary>
    static class Installer
    {
        public const string ExeName = "Soleanna.exe";
        public const string VersionFile = "payload.version";
        /// <summary>Left in the install by an update; the launcher then offers to build again.</summary>
        public const string UpdateMarker = "update.pending";

        static Stream Resource(string name) => typeof(Installer).Assembly.GetManifestResourceStream(name);

        public static bool HasPayload { get { using (var s = Resource("payload.zip")) return s != null; } }

        public static string PayloadVersion
        {
            get { using (var s = Resource("payload.version")) return s == null ? "" : new StreamReader(s).ReadToEnd().Trim(); }
        }

        // Where the install is, for this Windows user. LOCALAPPDATA can point elsewhere for tests.
        static string StateFile => Path.Combine(
            Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Soleanna", "install.txt");

        // A folder this exe installed; a git checkout has no payload.version, so it is never one.
        static bool IsInstall(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, VersionFile)) &&
                                             File.Exists(Path.Combine(dir, "tools", "build.ps1"));

        static string InstalledVersion(string dir)
        {
            var f = Path.Combine(dir, VersionFile);
            return File.Exists(f) ? File.ReadAllText(f).Trim() : "";
        }

        /// <summary>The exe is not inside an install or a checkout: install, update, or open the install.</summary>
        public static int Run(string[] args)
        {
            if (!HasPayload)
            {
                MessageBox.Show("Launcher.exe has to be in the sonic06-recomp folder (next to tools\\ and src\\).", "Soleanna");
                return 1;
            }
            // Without dialogs, for tests and scripts: --install <folder>
            if (args.Length == 2 && args[0] == "--install")
            {
                try { Install(args[1], interactive: false); return 0; }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }

            Loc.I.Set(Loc.SystemDefault());
            // The launcher's own dialogs, before there is a main window to own them.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Launcher;component/Theme.xaml") });
            var title = Loc.I["install_title"];
            try
            {
                var known = File.Exists(StateFile) ? File.ReadAllText(StateFile).Trim() : "";
                if (IsInstall(known))
                {
                    if (InstalledVersion(known) != PayloadVersion &&
                        Dialog.Show(null, title, Loc.I.F("install_update", known, PayloadVersion), true, yesNo: true))
                        Install(known, interactive: false);
                    return Start(known, args);
                }

                var dir = Path.Combine(Path.GetDirectoryName(typeof(Installer).Assembly.Location), "Soleanna");
                if (!Dialog.Show(null, title, Loc.I.F("install_where", dir), true, yesNo: true))
                {
                    using (var pick = new System.Windows.Forms.FolderBrowserDialog { Description = Loc.I["install_choose"], ShowNewFolderButton = true })
                    {
                        if (pick.ShowDialog() != System.Windows.Forms.DialogResult.OK) return 1;
                        dir = IsInstall(pick.SelectedPath) ? pick.SelectedPath : Path.Combine(pick.SelectedPath, "Soleanna");
                    }
                }
                Install(dir, interactive: true);
                return Start(dir, args);
            }
            catch (Exception ex)
            {
                Dialog.Show(null, title, Loc.I.F("install_failed", ex.Message), false);
                return 1;
            }
        }

        /// <summary>
        /// The payload into dir, this exe as dir\Soleanna.exe, the version, and where the install is.
        /// Over an older install it only replaces the project's own files: the disc, the build, the
        /// downloads and the settings are not in the payload, so they stay.
        /// </summary>
        static void Install(string dir, bool interactive)
        {
            dir = Path.GetFullPath(dir);
            var update = IsInstall(dir);
            var builtBefore = File.Exists(Path.Combine(dir, "build", "sonic06.exe"));
            Directory.CreateDirectory(dir);
            // Only the payload fills these: a patch that a newer version dropped must not stay and be applied.
            if (update)
                foreach (var d in new[] { "src", "patches" })
                    if (Directory.Exists(Path.Combine(dir, d))) Directory.Delete(Path.Combine(dir, d), true);
            using (var s = Resource("payload.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                foreach (var e in zip.Entries.Where(e => e.Name.Length > 0))
                {
                    var dest = Path.GetFullPath(Path.Combine(dir, e.FullName.Replace('/', '\\')));
                    if (!dest.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) continue;   // nothing outside the folder
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            }
            var self = typeof(Installer).Assembly.Location;
            var target = Path.Combine(dir, ExeName);
            // Bytes, not File.Copy: a copy would carry the download's mark, and Windows would warn about the installed copy too.
            if (!string.Equals(Path.GetFullPath(self), target, StringComparison.OrdinalIgnoreCase)) File.WriteAllBytes(target, File.ReadAllBytes(self));
            File.WriteAllText(Path.Combine(dir, VersionFile), PayloadVersion);
            if (update && builtBefore) File.WriteAllText(Path.Combine(dir, UpdateMarker), PayloadVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile));
            File.WriteAllText(StateFile, dir);
            if (interactive && !update) DesktopShortcut(target, dir);
        }

        static void DesktopShortcut(string target, string dir)
        {
            try
            {
                var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Soleanna.lnk");
                var t = Type.GetTypeFromProgID("WScript.Shell");
                var shell = Activator.CreateInstance(t);
                var sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                var st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { dir });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Soleanna" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            catch (Exception) { /* a shortcut is a convenience; the install works without it */ }
        }

        static int Start(string dir, string[] args)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(dir, ExeName), string.Join(" ", args.Select(Runner.Quote)))
                          { WorkingDirectory = dir, UseShellExecute = false });
            return 0;
        }
    }
}

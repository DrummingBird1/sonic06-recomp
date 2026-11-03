using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sonic06Launcher
{
    /// <summary>
    /// One line of the setup status, as string keys (Strings\*.json) and the
    /// values that fill them, so the window shows it in the player's language
    /// and the bug report in English.
    /// </summary>
    public class StatusItem
    {
        public string Key, KeyArg, DetailKey, DetailArg;
        public bool Ok { get; private set; }
        public StatusItem(string key, bool ok, string detailKey, string detailArg = null, string keyArg = null)
        {
            Key = key; Ok = ok; DetailKey = detailKey; DetailArg = detailArg; KeyArg = keyArg;
        }
        public string Title => KeyArg == null ? Loc.I[Key] : Loc.I.F(Key, KeyArg);
        public string Detail => DetailKey == null ? DetailArg : DetailArg == null ? Loc.I[DetailKey] : Loc.I.F(DetailKey, DetailArg);
        public string English => $"{Loc.I.En(Key, KeyArg)}: {(DetailKey == null ? DetailArg : Loc.I.En(DetailKey, DetailArg))}";
    }

    /// <summary>A build tool, how to tell it is there, and how to install it: winget, or Install when WingetId is null.</summary>
    class ToolPackage
    {
        public string Name, Key, WingetId, DetailKey = "detail_winget";
        public Func<bool> Present;
        public Func<Setup, Task> Install;
    }

    /// <summary>
    /// Everything between "I have a disc" and "the game is built": what is
    /// missing, and the steps that fill it in. It never ships or fetches game
    /// data: the disc is the player's, and EBOOT.BIN is decrypted by the
    /// player's own RPCS3.
    /// </summary>
    class Setup
    {
        readonly Config cfg;
        readonly Action<string> log;
        readonly Action<int, int> progress;   // done, total; total 0 hides the bar

        public Setup(Config cfg, Action<string> log, Action<int, int> progress)
        {
            this.cfg = cfg; this.log = log; this.progress = progress;
        }

        // FFmpeg 8 (avcodec-62), the major the runtime is built against. BtbN's
        // LGPL shared build has the DLLs and the headers in one archive. The
        // address is the fallback: ResolveFfmpeg asks GitHub for the current one.
        public const string FfmpegUrl =
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip";
        const string FfmpegReleases = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases";
        public const string Ps3RecompUrl = "https://github.com/sp00nznet/ps3recomp.git";

        // xwin fetches Microsoft's CRT and Windows SDK, the only part of Visual
        // Studio clang-cl needs, from Microsoft's servers: 630 MB instead of a
        // Build Tools install. Pinned, and checked against its release hash.
        public const string XwinUrl =
            "https://github.com/Jake-Shadle/xwin/releases/download/0.10.0/xwin-0.10.0-x86_64-pc-windows-msvc.tar.gz";
        const string XwinSha256 = "96e83665ef5c1406aabd0be603a8d08e67e604f36fda630b6807ee948a614e26";
        public const string MicrosoftLicenseUrl = "https://go.microsoft.com/fwlink/?LinkId=2086102";

        public static readonly ToolPackage[] Packages =
        {
            new ToolPackage { Name = "Windows SDK + C runtime", Key = "step_crt", DetailKey = "detail_crt",
                Present = () => Tools.VisualStudio() != null || Tools.WinSysRoot() != null,
                Install = s => s.InstallWinSysRoot() },
            new ToolPackage { Name = "LLVM (clang-cl)", Key = "step_llvm", WingetId = "LLVM.LLVM", Present = () => Tools.ClangCl() != null },
            new ToolPackage { Name = "CMake", Key = "step_cmake", WingetId = "Kitware.CMake", Present = () => Tools.CMake() != null },
            new ToolPackage { Name = "Ninja", Key = "step_ninja", WingetId = "Ninja-build.Ninja", Present = () => Tools.Ninja() != null },
            new ToolPackage { Name = "Git", Key = "step_git", WingetId = "Git.Git", Present = () => Tools.Git() != null },
            new ToolPackage { Name = "uv (Python)", Key = "step_uv", WingetId = "astral-sh.uv", Present = () => Tools.Uv() != null },
            new ToolPackage { Name = "PowerShell 7", Key = "step_pwsh", WingetId = "Microsoft.PowerShell", Present = () => Tools.Pwsh() != null },
        };

        // ---- status ---------------------------------------------------------

        public string FfmpegBin()
        {
            foreach (var d in new[] { cfg.Str("ffmpeg"), Path.Combine(Workspace.Deps, "ffmpeg", "bin") })
                if (d.Length > 0 && File.Exists(Path.Combine(d, "avcodec-62.dll"))) return d;
            return null;
        }

        public string FfmpegInclude()
        {
            foreach (var d in new[] { cfg.Str("ffmpegInclude"), Path.Combine(Workspace.Deps, "ffmpeg", "include") })
                if (d.Length > 0 && File.Exists(Path.Combine(d, "libavcodec", "avcodec.h"))) return d;
            return null;
        }

        public string Rpcs3() { var r = cfg.Str("rpcs3"); return r.Length > 0 && File.Exists(r) ? r : null; }

        public List<StatusItem> Check()
        {
            var list = Packages.Select(p => new StatusItem(p.Key, p.Present(), p.DetailKey)).ToList();
            var ram = Tools.RamGb();
            list.Add(ram >= 7.5
                ? new StatusItem("step_memory", true, "detail_memory_ok", Tools.BuildJobs().ToString(), $"{ram:0}")
                : new StatusItem("step_memory", false, "detail_memory_low", null, $"{ram:0}"));
            var elf = File.Exists(Workspace.EbootElf);
            list.Add(elf ? new StatusItem("step_rpcs3", true, "detail_rpcs3_done")
                         : Rpcs3() != null ? new StatusItem("step_rpcs3", true, null, Rpcs3())
                                           : new StatusItem("step_rpcs3", false, "detail_rpcs3_choose"));
            list.Add(new StatusItem("step_disc", Directory.Exists(Path.Combine(Workspace.Disc, "PS3_GAME", "USRDIR")), "detail_disc"));
            list.Add(new StatusItem("step_eboot", elf, "detail_eboot"));
            var ff = FfmpegBin();
            list.Add(ff != null && FfmpegInclude() != null ? new StatusItem("step_ffmpeg", true, null, ff)
                                                           : new StatusItem("step_ffmpeg", false, "detail_ffmpeg_missing"));
            list.Add(new StatusItem("step_built", File.Exists(Workspace.GameExe), "detail_built"));
            return list;
        }

        public bool ReadyToPlay => File.Exists(Workspace.GameExe) && File.Exists(Workspace.EbootElf)
                                   && Directory.Exists(Path.Combine(Workspace.Disc, "PS3_GAME"));

        // ---- build tools ----------------------------------------------------

        public List<ToolPackage> MissingTools() => Packages.Where(p => !p.Present()).ToList();

        /// <summary>
        /// Installs the given tools. The CRT and SDK come under Microsoft's
        /// license, which the player has to accept first (MicrosoftLicenseUrl).
        /// </summary>
        public async Task InstallTools(IEnumerable<ToolPackage> packages, bool microsoftLicenseAccepted)
        {
            foreach (var p in packages.Where(p => p.Install != null))
            {
                if (!microsoftLicenseAccepted)
                    throw new InvalidOperationException($"{p.Name} needs Microsoft's license accepted: {MicrosoftLicenseUrl}");
                await p.Install(this);
                if (!p.Present()) throw new InvalidOperationException($"{p.Name} is still missing.");
                log($"{p.Name}: installed.");
            }
            var viaWinget = packages.Where(p => p.WingetId != null).ToList();
            if (viaWinget.Count == 0) return;
            var winget = Tools.Winget() ?? throw new InvalidOperationException(
                "winget is not available. Install \"App Installer\" from the Microsoft Store, then try again.");
            foreach (var p in viaWinget)
            {
                log($"Installing {p.Name} ({p.WingetId}). Windows may ask for permission.");
                var args = $"install --id {p.WingetId} -e --source winget --accept-package-agreements --accept-source-agreements";
                var rc = await Runner.Run(winget, args, log);
                Tools.RefreshPath();
                if (!p.Present())
                    throw new InvalidOperationException($"{p.Name} is still missing after winget (exit code {rc}).");
                log($"{p.Name}: installed.");
            }
        }

        /// <summary>
        /// xwin, then Microsoft's CRT and Windows SDK in the /winsysroot layout
        /// clang-cl and lld-link read (deps\winsysroot). tools\build.ps1 uses it
        /// when no Visual Studio with the C++ tools is installed. The download
        /// cache goes on the same drive: xwin cannot move files across drives.
        /// </summary>
        public async Task InstallWinSysRoot()
        {
            Directory.CreateDirectory(Workspace.Deps);
            var tgz = Path.Combine(Workspace.Deps, "xwin.tar.gz");
            log("Downloading xwin: " + XwinUrl);
            await Download(XwinUrl, tgz);
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var f = File.OpenRead(tgz))
                hash = string.Concat(sha.ComputeHash(f).Select(b => b.ToString("x2")));
            if (hash != XwinSha256) throw new InvalidOperationException("The xwin download does not match its release hash.");
            var dir = Path.Combine(Workspace.Deps, "xwin");
            Directory.CreateDirectory(dir);
            var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");   // bsdtar, in Windows since 10 1803
            if (await Runner.Run(tar, $"-xzf {Runner.Quote(tgz)} -C {Runner.Quote(dir)}", log) != 0)
                throw new InvalidOperationException("Could not unpack xwin.");
            var xwin = Directory.GetFiles(dir, "xwin.exe", SearchOption.AllDirectories).Single();
            var cache = Path.Combine(Workspace.Deps, ".xwin-cache");
            var outDir = Path.Combine(Workspace.Deps, "winsysroot");
            log("Downloading Microsoft's C runtime and Windows SDK (about 630 MB)");
            var rc = await Runner.Run(xwin, $"--accept-license --cache-dir {Runner.Quote(cache)} --arch x86_64 splat " +
                                            $"--use-winsysroot-style --preserve-ms-arch-notation --disable-symlinks --output {Runner.Quote(outDir)}", log);
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
            File.Delete(tgz);
            if (rc != 0) throw new InvalidOperationException($"xwin failed (exit code {rc}).");
        }

        // ---- the disc -------------------------------------------------------

        /// <summary>Mounts the player's ISO with Windows, copies it into game\disc and unmounts it.</summary>
        public async Task FromIso(string iso)
        {
            var full = Path.GetFullPath(iso).Replace("'", "''");
            log($"Mounting {Path.GetFileName(iso)}");
            var letter = Runner.Capture("powershell.exe",
                $"-NoProfile -Command \"(Mount-DiskImage -ImagePath '{full}' -PassThru | Get-Volume).DriveLetter\"");
            if (letter.Length != 1) throw new InvalidOperationException("Windows could not mount the image: " + letter);
            try { await FromFolder(letter + @":\"); }
            finally
            {
                Runner.Capture("powershell.exe", $"-NoProfile -Command \"Dismount-DiskImage -ImagePath '{full}' | Out-Null\"");
            }
        }

        /// <summary>Copies a disc folder (the one holding PS3_GAME, or PS3_GAME itself) into game\disc.</summary>
        public async Task FromFolder(string folder)
        {
            var src = new DirectoryInfo(folder);
            if (src.Name.Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase)) src = src.Parent;
            var sfo = Path.Combine(src.FullName, "PS3_GAME", "PARAM.SFO");
            if (!File.Exists(sfo)) throw new InvalidOperationException("This is not a PS3 disc: there is no PS3_GAME\\PARAM.SFO.");
            if (!Encoding.ASCII.GetString(File.ReadAllBytes(sfo)).Contains("BLES00028"))
                throw new InvalidOperationException("This is not BLES00028, the European disc the port was made for.");

            var files = src.GetFiles("*", SearchOption.AllDirectories);
            long total = files.Sum(f => f.Length), done = 0;
            log($"Copying {files.Length} files, {total / (1024 * 1024)} MB, into game\\disc");
            await Task.Run(() =>
            {
                foreach (var f in files)
                {
                    var rel = f.FullName.Substring(src.FullName.TrimEnd('\\').Length + 1);
                    var dst = Path.Combine(Workspace.Disc, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    f.CopyTo(dst, true);
                    done += f.Length;
                    progress((int)(done / (1024 * 1024)), (int)(total / (1024 * 1024)));
                }
            });
            File.Copy(Path.Combine(Workspace.Disc, "PS3_GAME", "USRDIR", "EBOOT.BIN"), Workspace.EbootBin, true);
            progress(0, 0);
            log("The disc is in game\\disc.");
        }

        // ---- decryption -----------------------------------------------------

        /// <summary>
        /// rpcs3 --decrypt writes EBOOT.elf next to EBOOT.BIN. The keys are the
        /// player's RPCS3's; the launcher has none.
        /// </summary>
        public async Task Decrypt()
        {
            var rpcs3 = Rpcs3() ?? throw new InvalidOperationException("Choose rpcs3.exe first.");
            if (!File.Exists(Workspace.EbootBin)) throw new InvalidOperationException("Copy the disc first: game\\EBOOT.BIN is missing.");
            log("Decrypting EBOOT.BIN with RPCS3");
            var rc = await Runner.Run(rpcs3, "--decrypt " + Runner.Quote(Workspace.EbootBin), log, cwd: Path.GetDirectoryName(rpcs3));
            if (!File.Exists(Workspace.EbootElf))
                throw new InvalidOperationException($"RPCS3 did not write game\\EBOOT.elf (exit code {rc}).");
            log("Decrypted: game\\EBOOT.elf");
        }

        // ---- FFmpeg ---------------------------------------------------------

        /// <summary>
        /// The FFmpeg 8 archive to download, and its SHA-256 when GitHub publishes one.
        /// BtbN rebuilds "latest" every day and already builds 9.0 too; the 8.1 name
        /// will go when 8.x stops being built. So ask GitHub for the newest 8.x LGPL
        /// shared build, in "latest" and then in the dated releases, and fall back to
        /// the usual address only when GitHub cannot be asked.
        /// </summary>
        public async Task<(string url, string sha256)> ResolveFfmpeg()
        {
            var pick = new Regex(@"^ffmpeg-n8\.\d+.*-win64-lgpl-shared-8\.\d+\.zip$");
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
                using (var http = new HttpClient())
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("sonic06-recomp-launcher");
                    http.Timeout = TimeSpan.FromSeconds(20);
                    foreach (var api in new[] { FfmpegReleases + "/tags/latest", FfmpegReleases + "?per_page=30" })
                    {
                        var parsed = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                            .DeserializeObject(await http.GetStringAsync(api));
                        var releases = parsed as object[] ?? new[] { parsed };
                        foreach (var rel in releases.OfType<Dictionary<string, object>>())
                            if (rel.TryGetValue("assets", out var assets) && assets is object[] list)
                                foreach (var a in list.OfType<Dictionary<string, object>>())
                                    if (a.TryGetValue("name", out var n) && n is string name && pick.IsMatch(name))
                                    {
                                        var digest = a.TryGetValue("digest", out var dg) && dg is string ds && ds.StartsWith("sha256:")
                                            ? ds.Substring(7) : null;
                                        return ((string)a["browser_download_url"], digest);
                                    }
                    }
                }
            }
            catch (Exception e)
            {
                log("Could not ask GitHub which FFmpeg build is current (" + e.Message + "); using the usual address.");
            }
            return (FfmpegUrl, null);
        }

        public async Task DownloadFfmpeg()
        {
            Directory.CreateDirectory(Workspace.Deps);
            var zip = Path.Combine(Workspace.Deps, "ffmpeg.zip");
            var (url, sha256) = await ResolveFfmpeg();
            log("Downloading FFmpeg 8 (LGPL, shared): " + url);
            await Download(url, zip);
            if (sha256 != null)
            {
                string hash;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var f = File.OpenRead(zip))
                    hash = string.Concat(sha.ComputeHash(f).Select(b => b.ToString("x2")));
                if (hash != sha256) throw new InvalidOperationException("The FFmpeg download does not match the hash GitHub gives for it.");
                log("FFmpeg's SHA-256 matches GitHub's.");
            }
            var tmp = Path.Combine(Workspace.Deps, "ffmpeg-unpack");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, tmp));
            var inner = Directory.GetDirectories(tmp).Single();   // the archive holds one top folder
            if (!File.Exists(Path.Combine(inner, "bin", "avcodec-62.dll")))
                throw new InvalidOperationException("The FFmpeg download has no bin\\avcodec-62.dll: it is not FFmpeg 8.");
            var dst = Path.Combine(Workspace.Deps, "ffmpeg");
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            Directory.Move(inner, dst);
            Directory.Delete(tmp, true);
            File.Delete(zip);
            cfg.Set("ffmpeg", Path.Combine(dst, "bin"));
            cfg.Set("ffmpegInclude", Path.Combine(dst, "include"));
            log("FFmpeg is in deps\\ffmpeg.");
        }

        async Task Download(string url, string file)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("sonic06-recomp-launcher");
                using (var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    r.EnsureSuccessStatusCode();
                    var total = r.Content.Headers.ContentLength ?? 0;
                    using (var s = await r.Content.ReadAsStreamAsync())
                    using (var f = File.Create(file))
                    {
                        var buf = new byte[1 << 16];
                        long done = 0; int n;
                        while ((n = await s.ReadAsync(buf, 0, buf.Length)) > 0)
                        {
                            await f.WriteAsync(buf, 0, n);
                            done += n;
                            if (total > 0) progress((int)(done >> 20), (int)(total >> 20));
                        }
                    }
                }
            }
            progress(0, 0);
        }

        // ---- the build ------------------------------------------------------

        /// <summary>The ps3recomp checkout setup_ps3recomp.ps1 copies from: one set as "ps3recomp" in
        /// launcher.local.json (a developer's own clone), or a clone in deps\.</summary>
        async Task<string> Ps3RecompSource()
        {
            var s = cfg.Str("ps3recomp");
            if (s.Length > 0 && Directory.Exists(Path.Combine(s, ".git"))) return s;
            var clone = Path.Combine(Workspace.Deps, "ps3recomp");
            if (!Directory.Exists(Path.Combine(clone, ".git")))
            {
                var git = Tools.Git() ?? throw new InvalidOperationException("Git is not installed.");
                Directory.CreateDirectory(Workspace.Deps);
                log("Downloading ps3recomp: " + Ps3RecompUrl);
                var rc = await Runner.Run(git, $"clone {Ps3RecompUrl} {Runner.Quote(clone)}", log);
                if (rc != 0) throw new InvalidOperationException($"git clone failed (exit code {rc}).");
            }
            return clone;
        }

        async Task Step(string what, Task<int> run)
        {
            log("== " + what);
            var rc = await run;
            if (rc != 0) throw new InvalidOperationException($"{what} failed (exit code {rc}). The log above says why.");
        }

        /// <summary>
        /// ps3recomp with this project's patches, the Python environment, the lift
        /// of the player's EBOOT.elf and the build: the same steps as
        /// tools\launcher.ps1 -Build, with as many parallel jobs as the memory allows.
        /// </summary>
        public async Task Build(bool resetToolkit = false)
        {
            if (!File.Exists(Workspace.EbootElf)) throw new InvalidOperationException("Decrypt the game first: game\\EBOOT.elf is missing.");
            Tools.RefreshPath();
            var src = await Ps3RecompSource();
            if (resetToolkit || !Directory.Exists(Workspace.P("work", "ps3recomp")))
                await Step("ps3recomp with the project's patches",
                           Runner.Script("setup_ps3recomp.ps1", $"-Source {Runner.Quote(src)}" + (resetToolkit ? " -Reset" : ""), log));

            var py = Workspace.P(".venv", "Scripts", "python.exe");
            if (!File.Exists(py))
            {
                var uv = Tools.Uv() ?? throw new InvalidOperationException("uv is not installed.");
                await Step("Python 3.12 environment", Runner.Run(uv, "venv --python 3.12 .venv", log));
                await Step("Python packages", Runner.Run(uv,
                    $"pip install --python {Runner.Quote(py)} -r work\\ps3recomp\\requirements.txt -r tools\\re\\requirements.txt", log));
            }

            if (resetToolkit || !Directory.Exists(Workspace.P("work", "recompiled")))
                await Step("Translating the game code (lift)", Runner.Script("relift.ps1", "", log));

            var jobs = Tools.BuildJobs();
            var inc = FfmpegInclude();
            var args = $"-Jobs {jobs}" + (inc != null ? " -FFmpegInclude " + Runner.Quote(inc) : "");
            log($"Building with {jobs} files at a time. A fast PC takes about 15 minutes; a slower one, or one with less memory, longer.");
            var ninja = new Regex(@"^\[(\d+)/(\d+)\]");
            await Step("Build", Runner.Script("build.ps1", args, line =>
            {
                log(line);
                var m = ninja.Match(line);
                if (m.Success) progress(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            }));
            progress(0, 0);
            if (!File.Exists(Workspace.GameExe)) throw new InvalidOperationException("The build finished without build\\sonic06.exe.");
            log("Built: build\\sonic06.exe");
        }
    }
}

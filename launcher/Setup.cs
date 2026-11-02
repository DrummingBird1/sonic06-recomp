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
    class StatusItem
    {
        public string Step, Detail;
        public bool Ok;
        public StatusItem(string step, bool ok, string detail) { Step = step; Ok = ok; Detail = detail; }
    }

    /// <summary>A package winget installs for the build, and how to tell it is there.</summary>
    class ToolPackage
    {
        public string Name, WingetId, Override;
        public Func<bool> Present;
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
        // LGPL shared build has the DLLs and the headers in one archive.
        public const string FfmpegUrl =
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip";
        public const string Ps3RecompUrl = "https://github.com/sp00nznet/ps3recomp.git";
        // The developer machine's shared copy; players get a clone in deps\.
        const string SharedPs3Recomp = @"<local>/shared\toolchains\ps3recomp";

        public static readonly ToolPackage[] Packages =
        {
            new ToolPackage { Name = "Visual Studio Build Tools (C++, CMake)", WingetId = "Microsoft.VisualStudio.BuildTools",
                Override = "--wait --passive --norestart --add Microsoft.VisualStudio.Workload.VCTools " +
                           "--add Microsoft.VisualStudio.Component.VC.CMake.Project --includeRecommended",
                Present = () => Tools.VisualStudio() != null },
            new ToolPackage { Name = "LLVM (clang-cl)", WingetId = "LLVM.LLVM", Present = () => Tools.ClangCl() != null },
            new ToolPackage { Name = "Git", WingetId = "Git.Git", Present = () => Tools.Git() != null },
            new ToolPackage { Name = "uv (Python)", WingetId = "astral-sh.uv", Present = () => Tools.Uv() != null },
            new ToolPackage { Name = "PowerShell 7", WingetId = "Microsoft.PowerShell", Present = () => Tools.Pwsh() != null },
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
            var list = Packages.Select(p => new StatusItem(p.Name, p.Present(), "build tool, installed with winget")).ToList();
            var ram = Tools.RamGb();
            list.Add(new StatusItem($"Memory: {ram:0} GB", ram >= 7.5,
                ram >= 7.5 ? $"builds {Tools.BuildJobs()} files at a time" : "8 GB or more is needed to build"));
            var elf = File.Exists(Workspace.EbootElf);
            list.Add(new StatusItem("RPCS3, to decrypt the game", elf || Rpcs3() != null,
                elf ? "not needed: already decrypted" : Rpcs3() ?? "choose rpcs3.exe"));
            list.Add(new StatusItem("Game disc (BLES00028)", Directory.Exists(Path.Combine(Workspace.Disc, "PS3_GAME", "USRDIR")),
                "your own copy, as an ISO or a folder"));
            list.Add(new StatusItem("Decrypted EBOOT", elf, "decrypted with your RPCS3"));
            var ff = FfmpegBin();
            list.Add(new StatusItem("FFmpeg 8 (movies)", ff != null && FfmpegInclude() != null, ff ?? "download or choose a folder"));
            list.Add(new StatusItem("Game built", File.Exists(Workspace.GameExe), "translated and built on this PC"));
            return list;
        }

        public bool ReadyToPlay => File.Exists(Workspace.GameExe) && File.Exists(Workspace.EbootElf)
                                   && Directory.Exists(Path.Combine(Workspace.Disc, "PS3_GAME"));

        // ---- build tools ----------------------------------------------------

        public List<ToolPackage> MissingTools() => Packages.Where(p => !p.Present()).ToList();

        public async Task InstallTools(IEnumerable<ToolPackage> packages)
        {
            var winget = Tools.Winget() ?? throw new InvalidOperationException(
                "winget is not available. Install \"App Installer\" from the Microsoft Store, then try again.");
            foreach (var p in packages)
            {
                log($"Installing {p.Name} ({p.WingetId}). Windows may ask for permission.");
                var args = $"install --id {p.WingetId} -e --source winget --accept-package-agreements --accept-source-agreements";
                if (p.Override != null) args += " --override " + Runner.Quote(p.Override);
                var rc = await Runner.Run(winget, args, log);
                Tools.RefreshPath();
                if (!p.Present())
                    throw new InvalidOperationException($"{p.Name} is still missing after winget (exit code {rc}).");
                log($"{p.Name}: installed.");
            }
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

        public async Task DownloadFfmpeg()
        {
            Directory.CreateDirectory(Workspace.Deps);
            var zip = Path.Combine(Workspace.Deps, "ffmpeg.zip");
            log("Downloading FFmpeg 8 (LGPL, shared): " + FfmpegUrl);
            await Download(FfmpegUrl, zip);
            var tmp = Path.Combine(Workspace.Deps, "ffmpeg-unpack");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, tmp));
            var inner = Directory.GetDirectories(tmp).Single();   // the archive holds one top folder
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

        /// <summary>The ps3recomp checkout setup_ps3recomp.ps1 copies from: a configured one, the shared one, or a clone in deps\.</summary>
        async Task<string> Ps3RecompSource()
        {
            foreach (var s in new[] { cfg.Str("ps3recomp"), SharedPs3Recomp })
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
            log($"Building with {jobs} files at a time. On a slower PC this takes an hour or more.");
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

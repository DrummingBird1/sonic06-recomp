using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sonic06Launcher
{
    /// <summary>Runs the scripts and tools the setup is made of, line by line into the log.</summary>
    static class Runner
    {
        public static Task<int> Run(string file, string args, Action<string> onLine,
                                    IDictionary<string, string> env = null, string cwd = null)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = cwd ?? Workspace.Root,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            return Task.Run(() =>
            {
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();   // this overload also waits for the redirected streams
                    return p.ExitCode;
                }
            });
        }

        /// <summary>A pwsh script from tools\, with the arguments as given.</summary>
        public static Task<int> Script(string name, string args, Action<string> onLine)
        {
            var pwsh = Tools.Pwsh();
            if (pwsh == null) throw new InvalidOperationException("PowerShell 7 (pwsh) is not installed.");
            return Run(pwsh, $"-NoProfile -ExecutionPolicy Bypass -File \"{Workspace.P("tools", name)}\" {args}", onLine);
        }

        /// <summary>The trimmed output of a short command, or "" if it cannot run.</summary>
        public static string Capture(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true, WorkingDirectory = Workspace.Root,
                };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);
                    return o.Trim();
                }
            }
            catch (Exception) { return ""; }
        }

        public static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
    }

    /// <summary>Where the build tools are, whether installed by winget, by hand or not at all.</summary>
    static class Tools
    {
        static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";

        /// <summary>
        /// A tool winget just installed is on the machine's PATH in the registry,
        /// not in this process's copy of it. Read it again so the next step and
        /// the scripts it starts find the tool without restarting the launcher.
        /// </summary>
        public static void RefreshPath()
        {
            var m = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            var u = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            Environment.SetEnvironmentVariable("Path", m + ";" + u);
        }

        static string OnPath(string exe)
        {
            foreach (var d in Env("Path").Split(';').Where(d => d.Length > 0))
            {
                try { var f = Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim()), exe); if (File.Exists(f)) return f; }
                catch (ArgumentException) { }
            }
            return null;
        }

        static string First(params string[] candidates) => candidates.FirstOrDefault(c => c != null && File.Exists(c));

        public static string Pwsh() => First(OnPath("pwsh.exe"), Path.Combine(Env("ProgramFiles"), "PowerShell", "7", "pwsh.exe"));
        public static string Git() => First(OnPath("git.exe"), Path.Combine(Env("ProgramFiles"), "Git", "cmd", "git.exe"));
        public static string Uv() => First(OnPath("uv.exe"),
            Path.Combine(Env("USERPROFILE"), ".local", "bin", "uv.exe"),
            Path.Combine(Env("LOCALAPPDATA"), "Microsoft", "WinGet", "Links", "uv.exe"));
        // tools\build.ps1 looks for clang-cl here, where the LLVM installer puts it.
        public static string ClangCl() => First(Path.Combine(Env("ProgramFiles"), "LLVM", "bin", "clang-cl.exe"));
        public static string Winget() => First(OnPath("winget.exe"),
            Path.Combine(Env("LOCALAPPDATA"), "Microsoft", "WindowsApps", "winget.exe"));
        // Visual Studio brings its own CMake and Ninja; without it they come from winget.
        public static string CMake() => First(OnPath("cmake.exe"), Path.Combine(Env("ProgramFiles"), "CMake", "bin", "cmake.exe"))
            ?? (VisualStudio() != null ? "Visual Studio" : null);
        public static string Ninja() => First(OnPath("ninja.exe"), Path.Combine(Workspace.Deps, "ninja.exe"),
            Path.Combine(Env("LOCALAPPDATA"), "Microsoft", "WinGet", "Links", "ninja.exe"))
            ?? (VisualStudio() != null ? "Visual Studio" : null);

        /// <summary>The xwin /winsysroot (Microsoft's CRT and Windows SDK) in deps\, if it is complete.</summary>
        public static string WinSysRoot()
        {
            var root = Path.Combine(Workspace.Deps, "winsysroot");
            var msvc = Path.Combine(root, "VC", "Tools", "MSVC");
            var ok = Directory.Exists(msvc) && Directory.GetDirectories(msvc).Any(v => File.Exists(Path.Combine(v, "lib", "x64", "msvcrt.lib")))
                     && Directory.Exists(Path.Combine(root, "Windows Kits", "10", "Lib"));
            return ok ? root : null;
        }

        /// <summary>The Visual Studio (or Build Tools) install with the C++ tools and CMake/Ninja, as build.ps1 finds it.</summary>
        public static string VisualStudio()
        {
            var vswhere = Path.Combine(Env("ProgramFiles(x86)"), "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(vswhere)) return null;
            var vs = Runner.Capture(vswhere, "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath");
            if (vs.Length == 0) return null;
            var ninja = Path.Combine(vs, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "Ninja", "ninja.exe");
            return File.Exists(Path.Combine(vs, "VC", "Auxiliary", "Build", "vcvars64.bat")) && File.Exists(ninja) ? vs : null;
        }

        public static double RamGb()
        {
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                    foreach (var o in s.Get()) return Convert.ToDouble(o["TotalPhysicalMemory"]) / (1024.0 * 1024 * 1024);
            }
            catch (Exception) { }
            return 0;
        }

        /// <summary>
        /// Parallel compile jobs for this machine. One lifted unit peaked at
        /// 2.69 GB in clang in a two-job build (2.21 in a ten-job one); leave
        /// 3 GB for Windows, and cap at the 10 the build was tuned with and at
        /// the cores there are. An "8 GB" PC reports about 7.8 and gets 1 job,
        /// 16 GB gets 4, 32 GB gets 10.
        /// </summary>
        public static int BuildJobs()
        {
            var byRam = (int)Math.Floor((RamGb() - 3.0) / 2.7);
            return Math.Max(1, Math.Min(Math.Min(byRam, 10), Environment.ProcessorCount));
        }
    }
}

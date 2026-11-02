using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Sonic06Launcher
{
    /// <summary>
    /// The checkout the launcher works in: the repository root, which holds
    /// tools\build.ps1. Launcher.exe sits at the root in a release and under
    /// launcher\bin\... in a developer build, so the root is found by walking up.
    /// </summary>
    public static class Workspace
    {
        public static string Root { get; private set; }

        public static bool Find()
        {
            for (var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); d != null; d = d.Parent)
                // TrimEnd: the base directory keeps its trailing backslash, and
                // Root is cut off the front of paths for the log.
                if (File.Exists(Path.Combine(d.FullName, "tools", "build.ps1"))) { Root = d.FullName.TrimEnd('\\'); return true; }
            return false;
        }

        public static string P(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

        // Game files and everything built from them: never in git, never published.
        public static string Disc => P("game", "disc");
        public static string EbootBin => P("game", "EBOOT.BIN");
        public static string EbootElf => P("game", "EBOOT.elf");
        public static string GameExe => P("build", "sonic06.exe");
        public static string Logs => P("work", "logs");
        // Downloaded tools the launcher manages itself (FFmpeg, the ps3recomp clone).
        public static string Deps => P("deps");
    }

    /// <summary>
    /// launcher.local.json: this machine's paths and the player's settings. It
    /// stays out of git. tools\launcher.ps1 reads the same file for ffmpeg,
    /// ffmpegInclude and ps3recomp, so those keys keep their names.
    /// </summary>
    public class Config
    {
        readonly Dictionary<string, object> v = new Dictionary<string, object>();
        static string PathOf => Workspace.P("launcher.local.json");

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (File.Exists(PathOf))
                    foreach (var kv in new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(PathOf)))
                        c.v[kv.Key] = kv.Value;
            }
            catch (Exception) { /* a broken file starts over from defaults */ }
            return c;
        }

        public void Save() => File.WriteAllText(PathOf, new JavaScriptSerializer().Serialize(v));

        public string Str(string key, string def = "") => v.TryGetValue(key, out var o) && o is string s ? s : def;
        public bool Bool(string key, bool def = false) => v.TryGetValue(key, out var o) && o is bool b ? b : def;
        public void Set(string key, object value) { v[key] = value; Save(); }
        public void Put(string key, object value) => v[key] = value;   // with Save() after a batch
    }
}

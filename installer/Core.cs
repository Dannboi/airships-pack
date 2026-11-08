using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AirshipsInstaller
{
    static class Config
    {
        public const string PackName = "Airships: Conquer the Mines";
        public const string ShortName = "Airships";
        public const string GitHubRepo = "Dannboi/airships-pack";
        public const string Branch = "main";
        public const string PackTomlUrl = "https://raw.githubusercontent.com/" + GitHubRepo + "/" + Branch + "/pack/pack.toml";
        public const string PrismVersion = "11.1.1";
        public const string PrismUrl = "https://github.com/PrismLauncher/PrismLauncher/releases/download/" + PrismVersion +
                                       "/PrismLauncher-Windows-MinGW-w64-Portable-" + PrismVersion + ".zip";
        public const string InstanceId = "airships";
        public const string Minecraft = "1.21.1";
        public const string FallbackNeoForge = "21.1.265";   // used only if pack.toml can't be fetched at install time
        public const string HelperExe = "Airships.exe";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AirshipsConquerTheMines";

        public static string DefaultInstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirshipsConquerTheMines");
    }

    static class Core
    {
        static Core()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        // ---------- small helpers ----------
        public static byte[] Resource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
        }

        public static string HttpGetString(string url, int timeoutMs = 20000)
        {
            var req = (HttpWebRequest)WebRequest.Create(url + (url.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks);
            req.UserAgent = "AirshipsInstaller/1.0";
            req.Timeout = timeoutMs; req.ReadWriteTimeout = timeoutMs;
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
        }

        public static void Download(string url, string dest, Action<long, long> progress)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "AirshipsInstaller/1.0";
            req.Timeout = 30000; req.ReadWriteTimeout = 60000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = File.Create(dest))
            {
                long total = resp.ContentLength, done = 0;
                var buf = new byte[1 << 16]; int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0) { dst.Write(buf, 0, n); done += n; progress?.Invoke(done, total); }
            }
        }

        /// pack.toml URL pinned to the newest commit (immutable -> never a stale/mixed CDN cache right after a
        /// publish). Falls back to the branch URL if the GitHub API is unreachable or rate-limited.
        public static string ResolvePackUrl()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Config.GitHubRepo + "/commits/" + Config.Branch);
                req.UserAgent = "AirshipsInstaller/1.0"; req.Accept = "application/vnd.github.sha";
                req.Timeout = 10000; req.ReadWriteTimeout = 10000;
                using (var resp = req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream()))
                {
                    string sha = r.ReadToEnd().Trim();
                    if (Regex.IsMatch(sha, "^[0-9a-f]{40}$"))
                        return "https://raw.githubusercontent.com/" + Config.GitHubRepo + "/" + sha + "/pack/pack.toml";
                }
            }
            catch (Exception e) { Console.WriteLine("[Airships] GitHub API unavailable (" + e.Message + "), using branch URL"); }
            return Config.PackTomlUrl;
        }

        public static (string mc, string neoforge) FetchPackVersions(string url = null)
        {
            var toml = HttpGetString(url ?? Config.PackTomlUrl);
            string Get(string key) { var m = Regex.Match(toml, "^\\s*" + key + "\\s*=\\s*\"([^\"]+)\"", RegexOptions.Multiline); return m.Success ? m.Groups[1].Value : null; }
            return (Get("minecraft"), Get("neoforge"));
        }

        // ---------- Qt QSettings-compatible INI writing ----------
        static string IniValue(string v)
        {
            bool needQuote = v.Length > 0 && (v.IndexOfAny(new[] { '"', '\\', ',', ';', '=', '#' }) >= 0 || v.Trim() != v);
            if (!needQuote) return v;
            return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// Set keys in the [General] section of a Prism .cfg, keeping everything else.
        public static void MergeIni(string path, IDictionary<string, string> values)
        {
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            int gen = lines.FindIndex(l => l.Trim() == "[General]");
            if (gen < 0) { lines.Insert(0, "[General]"); gen = 0; }
            int end = lines.FindIndex(gen + 1, l => l.TrimStart().StartsWith("["));
            if (end < 0) end = lines.Count;
            foreach (var kv in values)
            {
                string line = kv.Key + "=" + IniValue(kv.Value);
                int at = lines.FindIndex(gen + 1, end - gen - 1, l => l.StartsWith(kv.Key + "="));
                if (at >= 0) lines[at] = line; else { lines.Insert(end, line); end++; }
            }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        // ---------- memory ----------
        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX { public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        public static double TotalRamGb()
        {
            var m = new MEMORYSTATUSEX();
            return GlobalMemoryStatusEx(m) ? m.ullTotalPhys / (1024.0 * 1024 * 1024) : 16;
        }

        public static int RecommendedMb(double totalGb) =>
            totalGb >= 30 ? 10240 : totalGb >= 15 ? 8192 : totalGb >= 11 ? 6144 : 4096;

        // ---------- install ----------
        public class Options { public string Dir; public int MaxMemMb; public bool DesktopShortcut; }

        public static void Install(Options o, Action<string, int> status)
        {
            Directory.CreateDirectory(o.Dir);
            string prismExe = Path.Combine(o.Dir, "prismlauncher.exe");

            // 1. launcher
            status("Downloading Prism Launcher " + Config.PrismVersion + "...", 0);
            string zip = Path.Combine(Path.GetTempPath(), "airships-prism-" + Config.PrismVersion + ".zip");
            Download(Config.PrismUrl, zip, (d, t) => { if (t > 0) status(null, (int)(d * 60 / t)); });
            status("Unpacking launcher...", 60);
            foreach (var p in Process.GetProcessesByName("prismlauncher"))
                if (string.Equals(SafePath(p), prismExe, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("The launcher is still open. Close it and run the installer again.");
            using (var za = ZipFile.OpenRead(zip))
                foreach (var e in za.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(o.Dir, e.FullName));
                    if (!target.StartsWith(Path.GetFullPath(o.Dir), StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                }
            try { File.Delete(zip); } catch { }

            // 2. helper exe (this program) - runs the pack sync before each launch
            status("Setting up the modpack...", 75);
            string helper = Path.Combine(o.Dir, Config.HelperExe);
            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(helper), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, helper, true);

            // 3. launcher settings: skip every first-run question except the Microsoft login
            MergeIni(Path.Combine(o.Dir, "prismlauncher.cfg"), new Dictionary<string, string>
            {
                ["ConfigVersion"] = "1.3",
                ["Language"] = "en_US",
                ["IconTheme"] = "pe_colored",
                ["ApplicationTheme"] = "system",
                ["AutomaticJavaDownload"] = "true",
                ["AutomaticJavaSwitch"] = "true",
                ["UserAskedAboutAutomaticJavaDownload"] = "true",
                ["SelectedInstance"] = Config.InstanceId,
                ["ShowConsoleOnError"] = "true",
            });

            // 4. instance
            string inst = Path.Combine(o.Dir, "instances", Config.InstanceId);
            string mc = Path.Combine(inst, "minecraft");
            Directory.CreateDirectory(mc);
            Directory.CreateDirectory(Path.Combine(o.Dir, "icons"));
            File.WriteAllBytes(Path.Combine(o.Dir, "icons", "airships.png"), Resource("icon.png"));
            File.WriteAllBytes(Path.Combine(mc, "packwiz-installer-bootstrap.jar"), Resource("packwiz-installer-bootstrap.jar"));
            File.WriteAllBytes(Path.Combine(mc, "packwiz-installer.jar"), Resource("packwiz-installer.jar"));

            string neo = Config.FallbackNeoForge, mcVer = Config.Minecraft;
            try { var v = FetchPackVersions(ResolvePackUrl()); neo = v.neoforge ?? neo; mcVer = v.mc ?? mcVer; } catch { }
            string mmcPack = Path.Combine(inst, "mmc-pack.json");
            if (File.Exists(mmcPack)) SetLoaderVersion(mmcPack, neo);
            else File.WriteAllText(mmcPack, MmcPackJson(mcVer, neo), new UTF8Encoding(false));

            MergeIni(Path.Combine(inst, "instance.cfg"), new Dictionary<string, string>
            {
                ["ConfigVersion"] = "1.3",
                ["InstanceType"] = "OneSix",
                ["name"] = Config.PackName,
                ["iconKey"] = "airships",
                ["OverrideCommands"] = "true",
                ["PreLaunchCommand"] = "\"" + helper + "\" --prelaunch \"$INST_DIR\" \"$INST_JAVA\"",
                ["OverrideMemory"] = "true",
                ["MinMemAlloc"] = "2048",
                ["MaxMemAlloc"] = o.MaxMemMb.ToString(),
                ["OverrideConsole"] = "true",
                ["ShowConsole"] = "false",
                ["ShowConsoleOnError"] = "true",
                ["AutoCloseConsole"] = "false",
                ["LogPrePostOutput"] = "true",
            });

            // 5. shortcuts + Add/Remove Programs entry
            status("Creating shortcuts...", 90);
            string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Config.PackName.Replace(":", ""));
            Directory.CreateDirectory(programs);
            string ico = Path.Combine(o.Dir, "airships.ico");
            File.WriteAllBytes(ico, Resource("app.ico"));
            string launchArgs = "--launch " + Config.InstanceId;
            Shortcut(Path.Combine(programs, Config.PackName.Replace(":", "") + ".lnk"), prismExe, launchArgs, o.Dir, ico, "Play " + Config.PackName);
            Shortcut(Path.Combine(programs, "Airships Launcher (settings).lnk"), prismExe, "", o.Dir, prismExe, "Open the launcher");
            Shortcut(Path.Combine(programs, "Uninstall Airships.lnk"), helper, "--uninstall", o.Dir, ico, "Uninstall");
            if (o.DesktopShortcut)
                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Config.PackName.Replace(":", "") + ".lnk"),
                         prismExe, launchArgs, o.Dir, ico, "Play " + Config.PackName);

            using (var k = Registry.CurrentUser.CreateSubKey(Config.UninstallKey))
            {
                k.SetValue("DisplayName", Config.PackName);
                k.SetValue("DisplayIcon", ico);
                k.SetValue("Publisher", "Harvy");
                k.SetValue("DisplayVersion", "1.0");
                k.SetValue("InstallLocation", o.Dir);
                k.SetValue("UninstallString", "\"" + helper + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            status("Done!", 100);
        }

        static string SafePath(Process p) { try { return p.MainModule.FileName; } catch { return ""; } }

        static string MmcPackJson(string mc, string neo) =>
            "{\n    \"components\": [\n" +
            "        {\n            \"important\": true,\n            \"uid\": \"net.minecraft\",\n            \"version\": \"" + mc + "\"\n        },\n" +
            "        {\n            \"uid\": \"net.neoforged\",\n            \"version\": \"" + neo + "\"\n        }\n" +
            "    ],\n    \"formatVersion\": 1\n}\n";

        /// Returns true if the file was changed.
        public static bool SetLoaderVersion(string mmcPack, string neo)
        {
            string json = File.ReadAllText(mmcPack);
            // the net.neoforged component object: {... "uid": "net.neoforged" ... "version": "x" ...}
            var re = new Regex("(\\{[^{}]*\"uid\"\\s*:\\s*\"net\\.neoforged\"[^{}]*\\})", RegexOptions.Singleline);
            var m = re.Match(json);
            if (!m.Success) return false;
            string block = m.Value;
            var vm = Regex.Match(block, "\"version\"\\s*:\\s*\"([^\"]*)\"");
            if (vm.Success && vm.Groups[1].Value == neo) return false;
            // drop cached metadata so Prism re-resolves the new version's libraries
            string nb = Regex.Replace(block, ",?\\s*\"cached(Name|Requires|Version|Volatile)\"\\s*:\\s*(\"[^\"]*\"|\\[[^\\]]*\\]|true|false)", "");
            nb = vm.Success ? Regex.Replace(nb, "\"version\"\\s*:\\s*\"[^\"]*\"", "\"version\": \"" + neo + "\"")
                            : nb.TrimEnd('}').TrimEnd() + ",\n            \"version\": \"" + neo + "\"\n        }";
            File.WriteAllText(mmcPack, json.Replace(block, nb), new UTF8Encoding(false));
            return true;
        }

        static void Shortcut(string lnk, string target, string args, string workDir, string icon, string desc)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            try
            {
                dynamic s = shell.CreateShortcut(lnk);
                s.TargetPath = target; s.Arguments = args; s.WorkingDirectory = workDir;
                s.IconLocation = icon + ",0"; s.Description = desc;
                s.Save();
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        // ---------- pre-launch (run by Prism before every game start) ----------
        public static int PreLaunch(string instDir, string java)
        {
            Console.WriteLine("[Airships] checking for pack updates...");
            string mc = Path.Combine(instDir, "minecraft");
            if (!Directory.Exists(mc) && Directory.Exists(Path.Combine(instDir, ".minecraft"))) mc = Path.Combine(instDir, ".minecraft");
            Directory.CreateDirectory(mc);
            string boot = Path.Combine(mc, "packwiz-installer-bootstrap.jar");
            if (!File.Exists(boot)) File.WriteAllBytes(boot, Resource("packwiz-installer-bootstrap.jar"));
            string inst = Path.Combine(mc, "packwiz-installer.jar");
            if (!File.Exists(inst)) File.WriteAllBytes(inst, Resource("packwiz-installer.jar"));

            string packUrl = ResolvePackUrl();
            Console.WriteLine("[Airships] pack: " + packUrl);
            // keep the NeoForge version in step with the pack
            try
            {
                var v = FetchPackVersions(packUrl);
                if (v.neoforge != null && SetLoaderVersion(Path.Combine(instDir, "mmc-pack.json"), v.neoforge))
                {
                    Console.WriteLine("[Airships] pack now uses NeoForge " + v.neoforge + " - updated the instance; launch again.");
                    System.Windows.Forms.MessageBox.Show(
                        "The modpack switched to a new NeoForge version (" + v.neoforge + ").\n\n" +
                        "The launcher has been updated - just press Play again.",
                        Config.PackName, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                    return 1; // abort this launch: Prism only re-reads mmc-pack.json on the next one
                }
            }
            catch (Exception e) { Console.WriteLine("[Airships] couldn't check NeoForge version (" + e.Message + ") - continuing"); }

            if (string.IsNullOrEmpty(java) || !File.Exists(java))
            {
                // INST_JAVA may point at javaw.exe or a dir; fall back to a sibling java.exe
                string alt = java != null ? Path.Combine(Path.GetDirectoryName(java) ?? "", "java.exe") : null;
                if (alt != null && File.Exists(alt)) java = alt;
            }
            // packwiz-installer shows its own progress window while it downloads
            var psi = new ProcessStartInfo(java, "-jar \"" + boot + "\" \"" + packUrl + "\"")
            { UseShellExecute = false, WorkingDirectory = mc };
            using (var p = Process.Start(psi)) { p.WaitForExit(); Console.WriteLine("[Airships] pack sync exit code " + p.ExitCode); return p.ExitCode; }
        }

        // ---------- uninstall ----------
        public static void Uninstall(string dir, bool keepWorlds)
        {
            string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Config.PackName.Replace(":", ""));
            try { if (Directory.Exists(programs)) Directory.Delete(programs, true); } catch { }
            try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Config.PackName.Replace(":", "") + ".lnk")); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(Config.UninstallKey, false); } catch { }
            if (keepWorlds)
            {
                string saves = Path.Combine(dir, "instances", Config.InstanceId, "minecraft", "saves");
                if (Directory.Exists(saves) && Directory.EnumerateFileSystemEntries(saves).Any())
                {
                    string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Airships worlds backup");
                    CopyDir(saves, dest);
                }
            }
            // we are running from inside dir: let cmd delete it after we exit
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"")
            { CreateNoWindow = true, UseShellExecute = false });
        }

        static void CopyDir(string src, string dst)
        {
            foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(d.Replace(src, dst));
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(f, f.Replace(src, dst), true);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("Siêu Nhân Hồng - Mở khoá Steam")]
[assembly: AssemblyDescription("Siêu Nhân Hồng - Mở khoá Steam bằng GoodbyeDPI, không VPN, không lag")]
[assembly: AssemblyCompany("Newwyn Team")]
[assembly: AssemblyProduct("Siêu Nhân Hồng")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("4.1.0.0")]
[assembly: AssemblyFileVersion("4.1.0.0")]

namespace SieuNhanHong
{
    // ============================================================
    // App presets: GoodbyeDPI filters by domain, so each "app" is its list of domains
    // ============================================================
    public class AppPreset
    {
        public string Id, Name, Glyph;
        public string[] Domains;

        public AppPreset(string id, string name, string glyph, params string[] domains)
        {
            Id = id; Name = name; Glyph = glyph; Domains = domains;
        }

        public static readonly AppPreset[] All = new[]
        {
            new AppPreset("steam", "Steam", "\uE7FC",
                "steampowered.com", "steamcommunity.com", "steamstatic.com", "steamcontent.com",
                "steamserver.net", "steamusercontent.com", "steamgames.com", "steam-chat.com",
                "steamdeck.com", "s.team", "valvesoftware.com",
                "steamcdn-a.akamaihd.net", "steamstore-a.akamaihd.net",
                "steamcommunity-a.akamaihd.net", "steamuserimages-a.akamaihd.net",
                "cdn.akamai.steamstatic.com"),
            new AppPreset("epic", "Epic Games", "\uE7FC",
                "epicgames.com", "epicgames.dev", "epicgames.net", "unrealengine.com", "fortnite.com"),
            new AppPreset("discord", "Discord", "\uE8BD",
                "discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media"),
            new AppPreset("battlenet", "Battle.net", "\uE7FC",
                "battle.net", "blizzard.com", "battlenet.com"),
            new AppPreset("ea", "EA app", "\uE7FC",
                "ea.com", "origin.com", "tnt-ea.com"),
            new AppPreset("ubisoft", "Ubisoft Connect", "\uE7FC",
                "ubisoft.com", "ubi.com", "ubisoftconnect.com"),
            new AppPreset("gog", "GOG Galaxy", "\uE7FC",
                "gog.com", "gog-statics.com"),
            new AppPreset("riot", "Riot (LMHT, Valorant)", "\uE7FC",
                "riotgames.com", "leagueoflegends.com", "playvalorant.com", "riotcdn.net")
        };
    }

    // ============================================================
    // Settings saved next to GoodbyeDPI (C:\GoodbyeDPI\sieunhanhong.ini)
    // ============================================================
    public class AppSettings
    {
        public bool AllSites = false;
        public HashSet<string> Apps = new HashSet<string> { "steam" };
        public bool AutoStart = false;
        public bool StrongMode = false;

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(Gdpi.SettingsPath)) return s;
                foreach (var line in File.ReadAllLines(Gdpi.SettingsPath))
                {
                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;
                    string val = parts[1].Trim();
                    bool v = val == "1";
                    switch (parts[0].Trim())
                    {
                        case "SteamOnly": s.AllSites = !v; break; // v4.0 setting
                        case "AllSites": s.AllSites = v; break;
                        case "Apps":
                            s.Apps = new HashSet<string>(val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
                            break;
                        case "AutoStart": s.AutoStart = v; break;
                        case "StrongMode": s.StrongMode = v; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Gdpi.Root);
                File.WriteAllText(Gdpi.SettingsPath, string.Format(
                    "AllSites={0}\r\nApps={1}\r\nAutoStart={2}\r\nStrongMode={3}\r\n",
                    AllSites ? 1 : 0, string.Join(",", Apps.ToArray()), AutoStart ? 1 : 0, StrongMode ? 1 : 0));
            }
            catch { }
        }

        public string ScopeText()
        {
            if (AllSites) return "Mọi trang web";
            var names = AppPreset.All.Where(p => Apps.Contains(p.Id)).Select(p => p.Name.Split(' ')[0]).ToList();
            bool custom = Gdpi.ReadCustomDomains().Count > 0;
            if (names.Count == 0) return custom ? "Trang web riêng" : "Chưa chọn app";
            if (names.Count > 2) return names[0] + " + " + (names.Count - 1) + " app";
            return string.Join(", ", names.ToArray());
        }
    }

    // ============================================================
    // GoodbyeDPI install / run / service / update
    // ============================================================
    public static class Gdpi
    {
        public const string BundledVersion = "0.2.2";
        public const string ServiceName = "GoodbyeDPI";
        public const string TrayTaskName = "SieuNhanHong";
        public const string RepoApi = "https://api.github.com/repos/ValdikSS/GoodbyeDPI/releases/latest";

        public static readonly string Root = @"C:\GoodbyeDPI";
        public static readonly string BinDir = System.IO.Path.Combine(Root, "x86_64");
        public static readonly string ExePath = System.IO.Path.Combine(BinDir, "goodbyedpi.exe");
        public static readonly string BlacklistPath = System.IO.Path.Combine(Root, "blacklist.txt");
        public static readonly string CustomPath = System.IO.Path.Combine(Root, "custom-domains.txt");
        public static readonly string OldSteamListPath = System.IO.Path.Combine(Root, "steam-blacklist.txt");
        public static readonly string VersionPath = System.IO.Path.Combine(Root, "version.txt");
        public static readonly string SettingsPath = System.IO.Path.Combine(Root, "sieunhanhong.ini");

        // Extract the bundled GoodbyeDPI build if nothing is installed yet
        public static void EnsureInstalled(Action<string> log)
        {
            Directory.CreateDirectory(BinDir);
            if (!File.Exists(ExePath))
            {
                log("Lần đầu sử dụng: đang chuẩn bị công cụ vào " + Root + "...");
                ExtractResource("gdpi.goodbyedpi.exe", ExePath);
                ExtractResource("gdpi.WinDivert.dll", System.IO.Path.Combine(BinDir, "WinDivert.dll"));
                ExtractResource("gdpi.WinDivert64.sys", System.IO.Path.Combine(BinDir, "WinDivert64.sys"));
                string licDir = System.IO.Path.Combine(Root, "licenses");
                Directory.CreateDirectory(licDir);
                foreach (var lic in new[] { "LICENSE-goodbyedpi.txt", "LICENSE-windivert.txt", "LICENSE-getline.txt", "LICENSE-uthash.txt" })
                    ExtractResource("gdpi." + lic, System.IO.Path.Combine(licDir, lic));
                File.WriteAllText(VersionPath, BundledVersion);
                log("   -> Xong.");
            }
            else if (!File.Exists(VersionPath))
            {
                File.WriteAllText(VersionPath, BundledVersion);
            }

            if (!File.Exists(CustomPath))
            {
                var lines = new List<string>
                {
                    "# Thêm trang web riêng, mỗi dòng một tên miền (ví dụ: example.com).",
                    "# Dòng bắt đầu bằng # sẽ được bỏ qua.",
                    ""
                };
                // Carry over domains the user added to the v4.0 Steam list
                if (File.Exists(OldSteamListPath))
                {
                    var steam = AppPreset.All[0].Domains;
                    lines.AddRange(File.ReadAllLines(OldSteamListPath).Select(x => x.Trim())
                        .Where(x => x.Length > 0 && !steam.Contains(x)));
                }
                File.WriteAllLines(CustomPath, lines, new UTF8Encoding(false));
            }
        }

        private static void ExtractResource(string name, string target)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("File app bị thiếu dữ liệu, hãy tải lại app: " + name);
                using (var f = File.Create(target)) s.CopyTo(f);
            }
        }

        public static string InstalledVersion()
        {
            try { return File.Exists(VersionPath) ? File.ReadAllText(VersionPath).Trim() : "chưa cài"; }
            catch { return "?"; }
        }

        public static List<string> ReadCustomDomains()
        {
            try
            {
                if (!File.Exists(CustomPath)) return new List<string>();
                return File.ReadAllLines(CustomPath).Select(x => x.Trim())
                    .Where(x => x.Length > 0 && !x.StartsWith("#")).ToList();
            }
            catch { return new List<string>(); }
        }

        // Writes the combined domain list for the selected apps; returns how many domains it holds
        public static int WriteBlacklist(AppSettings s)
        {
            var domains = AppPreset.All.Where(p => s.Apps.Contains(p.Id)).SelectMany(p => p.Domains)
                .Concat(ReadCustomDomains()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            File.WriteAllLines(BlacklistPath, domains);
            return domains.Count;
        }

        public static string BuildArgs(AppSettings s)
        {
            string args = s.StrongMode ? "-5" : "-e 1 --native-frag --max-payload";
            if (!s.AllSites) args += " --blacklist \"" + BlacklistPath + "\"";
            return args;
        }

        public static bool IsRunning()
        {
            try { return Process.GetProcessesByName("goodbyedpi").Length > 0; }
            catch { return false; }
        }

        public static bool ServiceExists()
        {
            try { return ServiceController.GetServices().Any(x => x.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }

        public static void StopAll(Action<string> log, bool removeAutoStart)
        {
            if (ServiceExists())
            {
                RunCapture("sc.exe", "stop " + ServiceName);
                if (removeAutoStart)
                {
                    RunCapture("sc.exe", "delete " + ServiceName);
                    RunCapture("schtasks.exe", "/Delete /TN " + TrayTaskName + " /F");
                    log("   -> Đã tắt tự bật khi mở máy.");
                }
            }
            foreach (var p in Process.GetProcessesByName("goodbyedpi"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
            for (int i = 0; i < 20 && IsRunning(); i++) Thread.Sleep(250);
        }

        public static bool Start(AppSettings s, Action<string> log)
        {
            if (!s.AllSites && WriteBlacklist(s) == 0)
            {
                log("Chưa chọn app nào. Bấm \"Chọn app\" để chọn ít nhất một app.");
                return false;
            }

            StopAll(log, true);
            string args = BuildArgs(s);

            if (s.AutoStart)
            {
                string bin = "\\\"" + ExePath + "\\\" " + args.Replace("\"", "\\\"");
                string output;
                int code = RunCapture("sc.exe", "create " + ServiceName + " binPath= \"" + bin + "\" start= auto DisplayName= \"GoodbyeDPI (Siêu Nhân Hồng)\"", out output);
                if (code != 0)
                {
                    log("   -> Không bật được chế độ tự bật khi mở máy: " + output.Trim());
                    return false;
                }
                RunCapture("sc.exe", "description " + ServiceName + " \"Mở khoá Steam - quản lý bởi Siêu Nhân Hồng\"");
                RunCapture("sc.exe", "start " + ServiceName, out output);

                // Bring the tray icon back after each sign-in (highest privileges, so no UAC prompt)
                RunCapture("schtasks.exe", "/Create /TN " + TrayTaskName + " /TR \"\\\"" + Program.AppExePath +
                    "\\\" --tray\" /SC ONLOGON /RL HIGHEST /F");
                log("   -> Đã bật chế độ tự bật khi mở máy.");
            }
            else
            {
                var psi = new ProcessStartInfo(ExePath, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = BinDir
                };
                Process.Start(psi);
            }

            Thread.Sleep(1500);
            return IsRunning();
        }

        // ---------------- GitHub releases ----------------
        public class ReleaseInfo { public string Tag; public string ZipUrl; public string ExeUrl; }

        public static ReleaseInfo FetchLatest(string apiUrl)
        {
            var req = (HttpWebRequest)WebRequest.Create(apiUrl);
            req.UserAgent = "SieuNhanHong-Updater";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            string json;
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = sr.ReadToEnd();

            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            var info = new ReleaseInfo { Tag = Convert.ToString(root["tag_name"]) };
            var assets = root["assets"] as object[];
            if (assets != null)
            {
                foreach (var a in assets)
                {
                    var d = (Dictionary<string, object>)a;
                    string name = Convert.ToString(d["name"]);
                    string url = Convert.ToString(d["browser_download_url"]);
                    if (info.ZipUrl == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) info.ZipUrl = url;
                    if (info.ExeUrl == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) info.ExeUrl = url;
                }
            }
            return info;
        }

        public static int CompareVersions(string a, string b)
        {
            Func<string, Version> parse = x =>
            {
                var m = Regex.Match((x ?? "").TrimStart('v', 'V'), @"^\d+(\.\d+)+");
                Version v;
                return m.Success && Version.TryParse(m.Value, out v) ? v : null;
            };
            var va = parse(a);
            var vb = parse(b);
            if (va != null && vb != null) return va.CompareTo(vb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static void InstallUpdate(ReleaseInfo rel, Action<string> log)
        {
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SieuNhanHong_update");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            string zip = System.IO.Path.Combine(tmp, "goodbyedpi.zip");

            log("Đang tải GoodbyeDPI " + rel.Tag + " từ GitHub...");
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "SieuNhanHong-Updater";
                wc.DownloadFile(rel.ZipUrl, zip);
            }
            log(string.Format("   -> Đã tải xong ({0:N0} KB).", new FileInfo(zip).Length / 1024));

            string ex = System.IO.Path.Combine(tmp, "x");
            ZipFile.ExtractToDirectory(zip, ex);
            string newExe = Directory.GetFiles(ex, "goodbyedpi.exe", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.IndexOf("x86_64", StringComparison.OrdinalIgnoreCase) >= 0);
            if (newExe == null) throw new Exception("File tải về bị lỗi, hãy thử lại sau.");
            string newBin = System.IO.Path.GetDirectoryName(newExe);

            log("Đang tạm dừng để thay file mới...");
            StopAll(log, false);
            RunCapture("sc.exe", "stop WinDivert");
            RunCapture("sc.exe", "stop WinDivert1.4");
            Thread.Sleep(800);

            foreach (var f in Directory.GetFiles(newBin))
                File.Copy(f, System.IO.Path.Combine(BinDir, System.IO.Path.GetFileName(f)), true);

            string newLic = Directory.GetDirectories(ex, "licenses", SearchOption.AllDirectories).FirstOrDefault();
            if (newLic != null)
            {
                string licDir = System.IO.Path.Combine(Root, "licenses");
                Directory.CreateDirectory(licDir);
                foreach (var f in Directory.GetFiles(newLic))
                    File.Copy(f, System.IO.Path.Combine(licDir, System.IO.Path.GetFileName(f)), true);
            }

            File.WriteAllText(VersionPath, rel.Tag.TrimStart('v', 'V'));
            try { Directory.Delete(tmp, true); } catch { }
            log("   -> Đã cập nhật GoodbyeDPI lên bản " + rel.Tag + ".");
        }

        // ---------------- helpers ----------------
        public static int RunCapture(string file, string args)
        {
            string o;
            return RunCapture(file, args, out o);
        }

        public static int RunCapture(string file, string args, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }
    }

    // ============================================================
    // Self-update of Siêu Nhân Hồng from its own GitHub releases
    // ============================================================
    public static class AppUpdater
    {
        public const string RepoApi = "https://api.github.com/repos/gentapham/goodbyedpi_sieunhanhong/releases/latest";

        public static string CurrentVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }

        // Downloads the new exe and swaps it in; the caller restarts the app afterwards
        public static void Install(Gdpi.ReleaseInfo rel, Action<string> log)
        {
            string exe = Program.AppExePath;
            string newPath = exe + ".new";
            string oldPath = exe + ".old";

            log("Đang tải Siêu Nhân Hồng " + rel.Tag + " từ GitHub...");
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "SieuNhanHong-Updater";
                wc.DownloadFile(rel.ExeUrl, newPath);
            }

            var bytes = File.ReadAllBytes(newPath);
            if (bytes.Length < 100000 || bytes[0] != 'M' || bytes[1] != 'Z')
            {
                File.Delete(newPath);
                throw new Exception("File tải về bị lỗi, hãy thử lại sau.");
            }

            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(exe, oldPath);
            try
            {
                File.Move(newPath, exe);
            }
            catch
            {
                File.Move(oldPath, exe);
                throw;
            }
            log("   -> Đã tải xong, đang mở lại app...");
        }

        public static void CleanupOldExe()
        {
            string oldPath = Program.AppExePath + ".old";
            for (int i = 0; i < 10 && File.Exists(oldPath); i++)
            {
                try { File.Delete(oldPath); } catch { Thread.Sleep(1000); }
            }
        }
    }

    // ============================================================
    // App
    // ============================================================
    public class Program
    {
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        public static extern int DnsFlushResolverCache();

        public const string DotNetDownloadUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net48";
        public const string SteamDownloadUrl = "https://store.steampowered.com/about/";
        private const string InstanceName = "SieuNhanHong_SingleInstance";
        private const string ShowEventName = "SieuNhanHong_ShowWindow";

        public static readonly string AppExePath = Process.GetCurrentProcess().MainModule.FileName;
        private static Mutex instanceLock;

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
            }
            catch
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            }

            string screenshotPath = null, appsShotPath = null;
            bool startInTray = args.Contains("--tray");
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--screenshot") screenshotPath = args[i + 1];
                if (args[i] == "--shot-apps") appsShotPath = args[i + 1];
            }
            bool previewMode = screenshotPath != null || appsShotPath != null;

            if (!CheckRequirements()) return;

            if (!IsAdministrator())
            {
                try
                {
                    var proc = new ProcessStartInfo(AppExePath, startInTray ? "--tray" : "")
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Environment.CurrentDirectory,
                        Verb = "runas"
                    };
                    Process.Start(proc);
                }
                catch
                {
                    MessageBox.Show("Siêu Nhân Hồng cần quyền quản trị (Administrator) để chạy. Hãy bấm Yes khi Windows hỏi.",
                                    "Cần quyền quản trị", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            // Only one copy at a time: a second launch just brings the first window back
            EventWaitHandle showEvent = null;
            if (!previewMode)
            {
                bool createdNew;
                instanceLock = new Mutex(false, InstanceName, out createdNew);
                if (!createdNew)
                {
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                    return;
                }
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            }

            Task.Factory.StartNew(AppUpdater.CleanupOldExe);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var main = new MainWindow(screenshotPath, appsShotPath);
            var window = main.Build();
            if (!startInTray) window.Show();
            main.Init(startInTray);

            if (showEvent != null)
            {
                var listener = new Thread(() =>
                {
                    while (showEvent.WaitOne())
                        window.Dispatcher.BeginInvoke(new Action(main.ShowFromTray));
                }) { IsBackground = true };
                listener.Start();
            }

            app.Run();
        }

        public static void ReleaseInstanceLock()
        {
            if (instanceLock != null) { instanceLock.Close(); instanceLock = null; }
        }

        // Show a friendly message (and offer the official download page) when the PC is missing something
        private static bool CheckRequirements()
        {
            if (!Environment.Is64BitOperatingSystem)
            {
                MessageBox.Show(
                    "Máy bạn đang dùng Windows 32-bit.\n\nSiêu Nhân Hồng hiện chỉ chạy được trên Windows 64-bit (Windows 10 hoặc 11).",
                    "Máy chưa hỗ trợ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // .NET Framework 4.5 or newer (release key 378389+); Windows 10/11 ship with 4.8
            int release = 0;
            try
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                            .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key != null) release = Convert.ToInt32(key.GetValue("Release", 0));
                }
            }
            catch { }

            if (release < 378389)
            {
                var answer = MessageBox.Show(
                    "Máy bạn đang thiếu .NET Framework 4.8 (phần mềm miễn phí của Microsoft) nên app chưa chạy được.\n\n" +
                    "Bấm Yes để mở trang tải chính thức của Microsoft. Cài xong, hãy khởi động lại máy rồi mở lại app.",
                    "Thiếu .NET Framework", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Yes) OpenUrl(DotNetDownloadUrl);
                return false;
            }
            return true;
        }

        public static string FindSteamExe()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string exe = key == null ? null : key.GetValue("SteamExe") as string;
                    if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return exe;
                }
            }
            catch { }
            return null;
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        private static bool IsAdministrator()
        {
            try
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    // ============================================================
    // Shared XAML styles (pink helmet theme)
    // ============================================================
    public static class Theme
    {
        public const string Resources = @"
        <SolidColorBrush x:Key='Pink' Color='#EE2D9F'/>
        <SolidColorBrush x:Key='PinkSoft' Color='#FF7AC8'/>
        <SolidColorBrush x:Key='Card' Color='#1F1119'/>
        <SolidColorBrush x:Key='CardLine' Color='#3B2032'/>
        <SolidColorBrush x:Key='Row' Color='#28161F'/>
        <SolidColorBrush x:Key='Text' Color='#FFF4FA'/>
        <SolidColorBrush x:Key='Muted' Color='#BFA3B5'/>
        <LinearGradientBrush x:Key='PinkGrad' StartPoint='0,0' EndPoint='1,1'>
            <GradientStop Color='#FF5BBE' Offset='0'/>
            <GradientStop Color='#EE2D9F' Offset='0.5'/>
            <GradientStop Color='#B8137A' Offset='1'/>
        </LinearGradientBrush>
        <LinearGradientBrush x:Key='WindowBg' StartPoint='0,0' EndPoint='1,1'>
            <GradientStop Color='#1D0E18' Offset='0'/>
            <GradientStop Color='#140A11' Offset='0.55'/>
            <GradientStop Color='#100810' Offset='1'/>
        </LinearGradientBrush>

        <Style TargetType='TextBlock'>
            <Setter Property='Foreground' Value='{StaticResource Text}'/>
        </Style>

        <Style x:Key='CardTitle' TargetType='TextBlock'>
            <Setter Property='Foreground' Value='{StaticResource PinkSoft}'/>
            <Setter Property='FontFamily' Value='Segoe UI Variable Display, Segoe UI'/>
            <Setter Property='FontWeight' Value='Bold'/>
            <Setter Property='FontSize' Value='16'/>
            <Setter Property='Margin' Value='2,0,0,12'/>
        </Style>

        <Style x:Key='Icon' TargetType='TextBlock'>
            <Setter Property='FontFamily' Value='Segoe Fluent Icons, Segoe MDL2 Assets'/>
            <Setter Property='VerticalAlignment' Value='Center'/>
        </Style>

        <Style x:Key='PinkBtn' TargetType='Button'>
            <Setter Property='Foreground' Value='White'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='FontSize' Value='15'/>
            <Setter Property='Height' Value='44'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='bd' CornerRadius='12' Background='{StaticResource PinkGrad}' Padding='16,0'>
                            <Border.Effect>
                                <DropShadowEffect Color='#EE2D9F' BlurRadius='16' ShadowDepth='0' Opacity='0.45'/>
                            </Border.Effect>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='bd' Property='Opacity' Value='0.92'/>
                            </Trigger>
                            <Trigger Property='IsPressed' Value='True'>
                                <Setter TargetName='bd' Property='Opacity' Value='0.78'/>
                            </Trigger>
                            <Trigger Property='IsEnabled' Value='False'>
                                <Setter TargetName='bd' Property='Opacity' Value='0.4'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key='GhostBtn' TargetType='Button'>
            <Setter Property='Foreground' Value='{StaticResource Text}'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='FontSize' Value='14'/>
            <Setter Property='Height' Value='44'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='bd' CornerRadius='12' Background='#2A1722' BorderBrush='#4A2A3E' BorderThickness='1' Padding='14,0'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='bd' Property='BorderBrush' Value='#EE2D9F'/>
                                <Setter TargetName='bd' Property='Background' Value='#36192B'/>
                            </Trigger>
                            <Trigger Property='IsPressed' Value='True'>
                                <Setter TargetName='bd' Property='Background' Value='#45203A'/>
                            </Trigger>
                            <Trigger Property='IsEnabled' Value='False'>
                                <Setter TargetName='bd' Property='Opacity' Value='0.4'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key='WinBtn' TargetType='Button'>
            <Setter Property='Width' Value='40'/>
            <Setter Property='Height' Value='30'/>
            <Setter Property='Foreground' Value='{StaticResource Muted}'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='b' Background='Transparent' CornerRadius='8'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='b' Property='Background' Value='#3A1D30'/>
                                <Setter Property='Foreground' Value='White'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key='Switch' TargetType='CheckBox'>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='VerticalAlignment' Value='Center'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='CheckBox'>
                        <Grid Width='54' Height='30' Background='Transparent'>
                            <Border x:Name='track' CornerRadius='15' Background='#3A2533' BorderBrush='#5C3A4F' BorderThickness='1'/>
                            <Ellipse x:Name='thumb' Width='22' Height='22' Fill='#CDB3C3' HorizontalAlignment='Left' Margin='4,0,0,0'/>
                        </Grid>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsChecked' Value='True'>
                                <Setter TargetName='track' Property='Background' Value='{StaticResource PinkGrad}'/>
                                <Setter TargetName='track' Property='BorderBrush' Value='#FF7AC8'/>
                                <Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/>
                                <Setter TargetName='thumb' Property='Margin' Value='0,0,4,0'/>
                                <Setter TargetName='thumb' Property='Fill' Value='White'/>
                            </Trigger>
                            <Trigger Property='IsEnabled' Value='False'>
                                <Setter Property='Opacity' Value='0.45'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style TargetType='ScrollBar'>
            <Setter Property='Width' Value='6'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='ScrollBar'>
                        <Track Name='PART_Track' IsDirectionReversed='True'>
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType='Thumb'>
                                            <Border Background='#EE2D9F' CornerRadius='3' Opacity='0.6'/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>";

        public static Window Parse(string xaml)
        {
            return (Window)XamlReader.Parse(xaml.Replace("%%RESOURCES%%", Resources));
        }
    }

    // ============================================================
    // "Chọn app" dialog
    // ============================================================
    public class AppPickerWindow
    {
        private const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='Chọn app' Width='700' SizeToContent='Height'
        WindowStartupLocation='CenterOwner' ShowInTaskbar='False'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent' ResizeMode='NoResize'
        FontFamily='Segoe UI Variable Text, Segoe UI' FontSize='15'
        TextOptions.TextFormattingMode='Ideal' UseLayoutRounding='True'>
    <Window.Resources>%%RESOURCES%%</Window.Resources>
    <Border CornerRadius='20' BorderBrush='#EE2D9F' BorderThickness='1.5' Margin='10' Padding='22,16,22,22' Background='{StaticResource WindowBg}'>
        <Border.Effect>
            <DropShadowEffect Color='#EE2D9F' BlurRadius='14' ShadowDepth='0' Opacity='0.45'/>
        </Border.Effect>
        <StackPanel>
            <Grid Name='bdDlgTitle' Background='Transparent' Margin='0,0,0,4'>
                <TextBlock Text='CHỌN APP ĐƯỢC MỞ KHOÁ' Style='{StaticResource CardTitle}' FontSize='18' Margin='2,6,0,6'/>
                <Button Name='btnDlgClose' Style='{StaticResource WinBtn}' HorizontalAlignment='Right'>
                    <TextBlock Style='{StaticResource Icon}' Text='&#xE8BB;' FontSize='11' Foreground='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}'/>
                </Button>
            </Grid>
            <TextBlock Text='Bật app nào thì Siêu Nhân Hồng giúp app đó vào mạng bình thường. Các app khác không bị ảnh hưởng.'
                       Foreground='{StaticResource Muted}' FontSize='14' TextWrapping='Wrap' Margin='2,0,0,14'/>

            <Border Background='#33152A' BorderBrush='#5A2547' BorderThickness='1' CornerRadius='14' Padding='16,12' Margin='0,0,0,12'>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width='36'/>
                        <ColumnDefinition Width='*'/>
                        <ColumnDefinition Width='Auto'/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Style='{StaticResource Icon}' Text='&#xE774;' FontSize='20' Foreground='#FF7AC8'/>
                    <StackPanel Grid.Column='1'>
                        <TextBlock Text='Mọi trang web và app' FontWeight='SemiBold'/>
                        <TextBlock Text='Áp dụng cho tất cả, kể cả trình duyệt web' Foreground='{StaticResource Muted}' FontSize='13'/>
                    </StackPanel>
                    <CheckBox Grid.Column='2' Name='chkAll' Style='{StaticResource Switch}'/>
                </Grid>
            </Border>

            <UniformGrid Name='pnlApps' Columns='2'/>

            <Grid Margin='0,8,0,0'>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='*'/>
                    <ColumnDefinition Width='12'/>
                    <ColumnDefinition Width='180'/>
                </Grid.ColumnDefinitions>
                <Button Name='btnCustom' Style='{StaticResource GhostBtn}'>
                    <StackPanel Orientation='Horizontal'>
                        <TextBlock Style='{StaticResource Icon}' Text='&#xE710;' FontSize='15' Foreground='#FF7AC8' Margin='0,0,8,0'/>
                        <TextBlock Name='txtCustomBtn' Text='Thêm trang web khác'/>
                    </StackPanel>
                </Button>
                <Button Grid.Column='2' Name='btnDone' Style='{StaticResource PinkBtn}'>
                    <TextBlock Text='Xong' Foreground='White'/>
                </Button>
            </Grid>
        </StackPanel>
    </Border>
</Window>";

        private readonly AppSettings settings;
        private readonly Dictionary<string, CheckBox> boxes = new Dictionary<string, CheckBox>();
        private Window win;
        private CheckBox chkAll;

        public AppPickerWindow(AppSettings settings)
        {
            this.settings = settings;
        }

        public Window Build(Window owner)
        {
            win = Theme.Parse(Xaml);
            if (owner != null && owner.IsVisible) win.Owner = owner;
            else win.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            ((Grid)win.FindName("bdDlgTitle")).MouseLeftButtonDown += (s, e) => win.DragMove();
            ((Button)win.FindName("btnDlgClose")).Click += (s, e) => win.Close();
            ((Button)win.FindName("btnDone")).Click += (s, e) => { Apply(); win.DialogResult = true; };
            ((Button)win.FindName("btnCustom")).Click += (s, e) => OpenCustomList();

            chkAll = (CheckBox)win.FindName("chkAll");
            chkAll.IsChecked = settings.AllSites;
            chkAll.Checked += (s, e) => UpdateEnabled();
            chkAll.Unchecked += (s, e) => UpdateEnabled();

            var panel = (UniformGrid)win.FindName("pnlApps");
            foreach (var p in AppPreset.All)
            {
                var bd = new Border
                {
                    Background = (Brush)win.FindResource("Row"),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(14, 10, 12, 10),
                    Margin = new Thickness(0, 0, 8, 8)
                };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new TextBlock { Text = p.Glyph, FontSize = 17, Foreground = (Brush)win.FindResource("PinkSoft"), Style = (Style)win.FindResource("Icon") };
                var names = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                names.Children.Add(new TextBlock { Text = p.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                names.Children.Add(new TextBlock
                {
                    Text = string.Join(", ", p.Domains.Take(2).ToArray()) + (p.Domains.Length > 2 ? ", ..." : ""),
                    FontSize = 12,
                    Foreground = (Brush)win.FindResource("Muted"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                var cb = new CheckBox { Style = (Style)win.FindResource("Switch"), IsChecked = settings.Apps.Contains(p.Id) };

                Grid.SetColumn(names, 1);
                Grid.SetColumn(cb, 2);
                g.Children.Add(icon);
                g.Children.Add(names);
                g.Children.Add(cb);
                bd.Child = g;
                panel.Children.Add(bd);
                boxes[p.Id] = cb;
            }

            UpdateCustomCount();
            UpdateEnabled();
            return win;
        }

        private void UpdateEnabled()
        {
            var panel = (UIElement)win.FindName("pnlApps");
            bool all = chkAll.IsChecked == true;
            panel.IsEnabled = !all;
            panel.Opacity = all ? 0.45 : 1;
        }

        private void UpdateCustomCount()
        {
            int n = Gdpi.ReadCustomDomains().Count;
            ((TextBlock)win.FindName("txtCustomBtn")).Text = n > 0 ? "Trang web riêng (" + n + ")" : "Thêm trang web khác";
        }

        private void OpenCustomList()
        {
            try
            {
                Gdpi.EnsureInstalled(_ => { });
                var p = Process.Start("notepad.exe", "\"" + Gdpi.CustomPath + "\"");
                if (p != null)
                {
                    p.EnableRaisingEvents = true;
                    p.Exited += (s, e) => win.Dispatcher.BeginInvoke(new Action(UpdateCustomCount));
                }
            }
            catch { }
        }

        private void Apply()
        {
            settings.AllSites = chkAll.IsChecked == true;
            settings.Apps = new HashSet<string>(boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key));
        }
    }

    // ============================================================
    // Main window
    // ============================================================
    public class MainWindow
    {
        private const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='Siêu Nhân Hồng - Mở khoá Steam'
        Width='1120' Height='720'
        WindowStartupLocation='CenterScreen'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent'
        ResizeMode='CanMinimize'
        FontFamily='Segoe UI Variable Text, Segoe UI'
        FontSize='15'
        TextOptions.TextFormattingMode='Ideal'
        UseLayoutRounding='True' SnapsToDevicePixels='True'>

    <Window.Resources>%%RESOURCES%%</Window.Resources>

    <Border CornerRadius='20' BorderBrush='#EE2D9F' BorderThickness='1.5' Margin='10' Background='{StaticResource WindowBg}'>
        <Border.Effect>
            <DropShadowEffect Color='#EE2D9F' BlurRadius='14' ShadowDepth='0' Opacity='0.45'/>
        </Border.Effect>

        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height='44'/>
                <RowDefinition Height='Auto'/>
                <RowDefinition Height='*'/>
                <RowDefinition Height='128'/>
            </Grid.RowDefinitions>

            <!-- TITLE BAR -->
            <Grid Grid.Row='0' Name='bdTitleBar' Background='Transparent' Margin='20,0,10,0'>
                <StackPanel Orientation='Horizontal' VerticalAlignment='Center'>
                    <Ellipse Width='9' Height='9' Fill='#EE2D9F' Margin='0,0,10,0'>
                        <Ellipse.Effect><DropShadowEffect Color='#EE2D9F' BlurRadius='8' ShadowDepth='0'/></Ellipse.Effect>
                    </Ellipse>
                    <TextBlock Text='SIÊU NHÂN HỒNG' Foreground='#FF7AC8' FontWeight='Bold' FontSize='13'/>
                    <TextBlock Name='txtTitleVer' Text='' Foreground='#8D6F82' FontSize='13'/>
                </StackPanel>
                <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
                    <Button Name='btnMinimize' Style='{StaticResource WinBtn}' ToolTip='Thu nhỏ xuống khay (cạnh đồng hồ)'>
                        <TextBlock Style='{StaticResource Icon}' Text='&#xE921;' FontSize='11' Foreground='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}'/>
                    </Button>
                    <Button Name='btnClose' Style='{StaticResource WinBtn}' Margin='4,0,0,0' ToolTip='Thoát app'>
                        <TextBlock Style='{StaticResource Icon}' Text='&#xE8BB;' FontSize='11' Foreground='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}'/>
                    </Button>
                </StackPanel>
            </Grid>

            <!-- HERO HEADER -->
            <Border Grid.Row='1' Name='bdHeader' Margin='20,0,20,14' CornerRadius='18' Padding='20,14' BorderBrush='#4A2140' BorderThickness='1'>
                <Border.Background>
                    <LinearGradientBrush StartPoint='0,0' EndPoint='1,0'>
                        <GradientStop Color='#3A1230' Offset='0'/>
                        <GradientStop Color='#24101D' Offset='0.6'/>
                        <GradientStop Color='#1A0C15' Offset='1'/>
                    </LinearGradientBrush>
                </Border.Background>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width='Auto'/>
                        <ColumnDefinition Width='*'/>
                        <ColumnDefinition Width='Auto'/>
                    </Grid.ColumnDefinitions>
                    <Grid Width='72' Height='72' Margin='0,0,18,0'>
                        <Ellipse Fill='#2B3238' Stroke='#EE2D9F' StrokeThickness='2'>
                            <Ellipse.Effect><DropShadowEffect Color='#EE2D9F' BlurRadius='18' ShadowDepth='0' Opacity='0.7'/></Ellipse.Effect>
                        </Ellipse>
                        <Image Name='imgLogo' Margin='13' Stretch='Uniform'/>
                    </Grid>
                    <StackPanel Grid.Column='1' VerticalAlignment='Center'>
                        <TextBlock Text='Siêu Nhân Hồng' FontFamily='Segoe UI Variable Display, Segoe UI' FontSize='30' FontWeight='Black'>
                            <TextBlock.Foreground>
                                <LinearGradientBrush StartPoint='0,0' EndPoint='1,0'>
                                    <GradientStop Color='#FFFFFF' Offset='0'/>
                                    <GradientStop Color='#FF8ACF' Offset='0.6'/>
                                    <GradientStop Color='#EE2D9F' Offset='1'/>
                                </LinearGradientBrush>
                            </TextBlock.Foreground>
                        </TextBlock>
                        <TextBlock Text='Giúp vào Steam, tải và cập nhật game bình thường  —  không cần VPN, không giật lag' Foreground='{StaticResource Muted}' FontSize='15' Margin='2,2,0,0'/>
                    </StackPanel>
                    <Border Grid.Column='2' Name='bdStatusPill' CornerRadius='22' Padding='18,10' VerticalAlignment='Center' Background='#2A1722' BorderBrush='#4A2A3E' BorderThickness='1'>
                        <StackPanel Orientation='Horizontal'>
                            <Ellipse Name='ledStatus' Width='12' Height='12' Fill='#6B5563' Margin='0,0,10,0' VerticalAlignment='Center'/>
                            <TextBlock Name='txtStatusPill' Text='Đang kiểm tra...' FontSize='16' FontWeight='Bold' VerticalAlignment='Center'/>
                        </StackPanel>
                    </Border>
                </Grid>
            </Border>

            <!-- MAIN -->
            <Grid Grid.Row='2' Margin='20,0,20,14'>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='300'/>
                    <ColumnDefinition Width='14'/>
                    <ColumnDefinition Width='*'/>
                    <ColumnDefinition Width='14'/>
                    <ColumnDefinition Width='*'/>
                </Grid.ColumnDefinitions>

                <!-- COL 1: POWER -->
                <Border Grid.Column='0' Background='{StaticResource Card}' BorderBrush='{StaticResource CardLine}' BorderThickness='1' CornerRadius='18' Padding='18'>
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height='Auto'/>
                            <RowDefinition Height='*'/>
                            <RowDefinition Height='Auto'/>
                        </Grid.RowDefinitions>
                        <TextBlock Style='{StaticResource CardTitle}' Text='BIẾN HÌNH'/>
                        <Grid Grid.Row='1' Name='btnPower' Width='176' Height='176' Cursor='Hand' VerticalAlignment='Center' HorizontalAlignment='Center' Background='Transparent'>
                            <Ellipse Name='ellPowerRing' StrokeThickness='3' Stroke='#4A2A3E'/>
                            <Ellipse Name='ellPowerCore' Margin='14' Fill='#2A1722'/>
                            <StackPanel VerticalAlignment='Center' HorizontalAlignment='Center'>
                                <TextBlock Name='txtPowerIcon' Style='{StaticResource Icon}' Text='&#xE7E8;' FontSize='52' HorizontalAlignment='Center' Foreground='#BFA3B5'/>
                                <TextBlock Name='txtPowerLabel' Text='BẬT' FontSize='18' FontWeight='Bold' HorizontalAlignment='Center' Margin='0,8,0,0'/>
                            </StackPanel>
                        </Grid>
                        <StackPanel Grid.Row='2' HorizontalAlignment='Center'>
                            <TextBlock Name='txtStateTitle' Text='Chưa bật' FontFamily='Segoe UI Variable Display, Segoe UI' FontSize='22' FontWeight='Bold' HorizontalAlignment='Center'/>
                            <TextBlock Name='txtStateDesc' Text='Bấm vào nút tròn để bật' Foreground='{StaticResource Muted}' FontSize='14' HorizontalAlignment='Center' TextAlignment='Center' TextWrapping='Wrap' Margin='0,4,0,0'/>
                        </StackPanel>
                    </Grid>
                </Border>

                <!-- COL 2: OPTIONS + VERSION -->
                <Grid Grid.Column='2'>
                    <Grid.RowDefinitions>
                        <RowDefinition Height='Auto'/>
                        <RowDefinition Height='14'/>
                        <RowDefinition Height='*'/>
                    </Grid.RowDefinitions>
                    <Border Background='{StaticResource Card}' BorderBrush='{StaticResource CardLine}' BorderThickness='1' CornerRadius='18' Padding='18,16'>
                        <StackPanel>
                            <TextBlock Style='{StaticResource CardTitle}' Text='TUỲ CHỌN'/>
                            <Grid Margin='0,0,0,12'>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='34'/>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='Auto'/>
                                </Grid.ColumnDefinitions>
                                <TextBlock Style='{StaticResource Icon}' Text='&#xE7FC;' FontSize='20' Foreground='#FF7AC8'/>
                                <StackPanel Grid.Column='1' Margin='0,0,10,0'>
                                    <TextBlock Text='Áp dụng cho' FontWeight='SemiBold' FontSize='15'/>
                                    <TextBlock Name='txtScope' Text='Steam' Foreground='{StaticResource Muted}' FontSize='13' TextTrimming='CharacterEllipsis'/>
                                </StackPanel>
                                <Button Grid.Column='2' Name='btnPickApps' Style='{StaticResource GhostBtn}' Height='32' FontSize='13'>
                                    <TextBlock Text='Chọn app' Margin='2,0'/>
                                </Button>
                            </Grid>
                            <Grid Margin='0,0,0,12'>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='34'/>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='Auto'/>
                                </Grid.ColumnDefinitions>
                                <TextBlock Style='{StaticResource Icon}' Text='&#xE7E8;' FontSize='20' Foreground='#FF7AC8'/>
                                <StackPanel Grid.Column='1' Margin='0,0,10,0'>
                                    <TextBlock Text='Tự bật khi mở máy' FontWeight='SemiBold' FontSize='15'/>
                                    <TextBlock Text='Chạy ngầm, hiện biểu tượng ở khay' Foreground='{StaticResource Muted}' FontSize='13' TextWrapping='Wrap'/>
                                </StackPanel>
                                <CheckBox Grid.Column='2' Name='chkAutoStart' Style='{StaticResource Switch}'/>
                            </Grid>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='34'/>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='Auto'/>
                                </Grid.ColumnDefinitions>
                                <TextBlock Style='{StaticResource Icon}' Text='&#xE945;' FontSize='20' Foreground='#FF7AC8'/>
                                <StackPanel Grid.Column='1' Margin='0,0,10,0'>
                                    <TextBlock Text='Chế độ mạnh' FontWeight='SemiBold' FontSize='15'/>
                                    <TextBlock Text='Bật khi vẫn chưa vào được Steam' Foreground='{StaticResource Muted}' FontSize='13' TextWrapping='Wrap'/>
                                </StackPanel>
                                <CheckBox Grid.Column='2' Name='chkStrong' Style='{StaticResource Switch}'/>
                            </Grid>
                        </StackPanel>
                    </Border>

                    <Border Grid.Row='2' Background='{StaticResource Card}' BorderBrush='{StaticResource CardLine}' BorderThickness='1' CornerRadius='18' Padding='18,14'>
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height='Auto'/>
                                <RowDefinition Height='Auto'/>
                                <RowDefinition Height='*'/>
                                <RowDefinition Height='Auto'/>
                            </Grid.RowDefinitions>
                            <TextBlock Style='{StaticResource CardTitle}' Text='PHIÊN BẢN' Margin='2,0,0,6'/>
                            <Grid Grid.Row='1'>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='78'/>
                                    <ColumnDefinition Width='78'/>
                                </Grid.ColumnDefinitions>
                                <Grid.RowDefinitions>
                                    <RowDefinition Height='Auto'/>
                                    <RowDefinition Height='Auto'/>
                                    <RowDefinition Height='Auto'/>
                                </Grid.RowDefinitions>
                                <TextBlock Grid.Column='1' Text='Đang có' Foreground='{StaticResource Muted}' FontSize='12'/>
                                <TextBlock Grid.Column='2' Text='Mới nhất' Foreground='{StaticResource Muted}' FontSize='12'/>
                                <TextBlock Grid.Row='1' Text='Siêu Nhân Hồng' FontSize='15' FontWeight='SemiBold' VerticalAlignment='Center'/>
                                <TextBlock Grid.Row='1' Grid.Column='1' Name='txtAppVer' Text='...' FontSize='17' FontWeight='Bold'/>
                                <TextBlock Grid.Row='1' Grid.Column='2' Name='txtAppLatest' Text='...' FontSize='17' FontWeight='Bold' Foreground='#FF7AC8'/>
                                <TextBlock Grid.Row='2' Text='Công cụ GoodbyeDPI' FontSize='15' FontWeight='SemiBold' VerticalAlignment='Center'/>
                                <TextBlock Grid.Row='2' Grid.Column='1' Name='txtVerInstalled' Text='...' FontSize='17' FontWeight='Bold'/>
                                <TextBlock Grid.Row='2' Grid.Column='2' Name='txtVerLatest' Text='...' FontSize='17' FontWeight='Bold' Foreground='#FF7AC8'/>
                            </Grid>
                            <Grid Grid.Row='3' Margin='0,8,0,0'>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='8'/>
                                    <ColumnDefinition Width='Auto'/>
                                </Grid.ColumnDefinitions>
                                <Button Name='btnUpdate' Style='{StaticResource PinkBtn}'>
                                    <StackPanel Orientation='Horizontal'>
                                        <TextBlock Style='{StaticResource Icon}' Text='&#xE895;' FontSize='15' Foreground='White' Margin='0,0,8,0'/>
                                        <TextBlock Name='txtUpdateBtn' Text='Tìm bản mới' Foreground='White'/>
                                    </StackPanel>
                                </Button>
                                <Button Grid.Column='2' Name='btnOpenFolder' Style='{StaticResource GhostBtn}' ToolTip='Mở thư mục cài đặt'>
                                    <TextBlock Style='{StaticResource Icon}' Text='&#xE838;' FontSize='16'/>
                                </Button>
                            </Grid>
                        </Grid>
                    </Border>
                </Grid>

                <!-- COL 3: CONNECTION TEST -->
                <Border Grid.Column='4' Background='{StaticResource Card}' BorderBrush='{StaticResource CardLine}' BorderThickness='1' CornerRadius='18' Padding='18,16'>
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height='Auto'/>
                            <RowDefinition Height='*'/>
                            <RowDefinition Height='Auto'/>
                        </Grid.RowDefinitions>
                        <TextBlock Style='{StaticResource CardTitle}' Text='THỬ VÀO STEAM'/>
                        <StackPanel Grid.Row='1' Name='pnlChecks'/>
                        <Button Grid.Row='2' Name='btnTest' Style='{StaticResource GhostBtn}' Margin='0,10,0,0'>
                            <StackPanel Orientation='Horizontal'>
                                <TextBlock Style='{StaticResource Icon}' Text='&#xE72C;' FontSize='15' Foreground='#FF7AC8' Margin='0,0,8,0'/>
                                <TextBlock Text='Kiểm tra ngay'/>
                            </StackPanel>
                        </Button>
                    </Grid>
                </Border>
            </Grid>

            <!-- LOG -->
            <Border Grid.Row='3' Background='#0E070C' BorderBrush='{StaticResource CardLine}' BorderThickness='1' CornerRadius='16' Margin='20,0,20,18' Padding='16,10'>
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height='Auto'/>
                        <RowDefinition Height='*'/>
                    </Grid.RowDefinitions>
                    <Grid Margin='0,0,0,4'>
                        <TextBlock Text='NHẬT KÝ HOẠT ĐỘNG' Foreground='#FF7AC8' FontWeight='Bold' FontSize='13' VerticalAlignment='Center'/>
                        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
                            <Button Name='btnGetSteam' Style='{StaticResource WinBtn}' Width='Auto' Margin='0,0,4,0' Visibility='Collapsed'>
                                <TextBlock Text='Tải Steam' FontSize='13' FontWeight='Bold' Margin='10,0' Foreground='#FF7AC8'/>
                            </Button>
                            <Button Name='btnResetDns' Style='{StaticResource WinBtn}' Width='Auto'>
                                <TextBlock Text='Khôi phục mạng mặc định' FontSize='13' Margin='10,0' Foreground='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}'/>
                            </Button>
                            <Button Name='btnClearLog' Style='{StaticResource WinBtn}' Width='Auto' Margin='4,0,0,0'>
                                <TextBlock Text='Xoá' FontSize='13' Margin='10,0' Foreground='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}'/>
                            </Button>
                        </StackPanel>
                    </Grid>
                    <TextBox Grid.Row='1' Name='txtLog' Background='Transparent' Foreground='#F7B6DC' FontFamily='Cascadia Mono, Consolas' FontSize='13'
                             IsReadOnly='True' BorderThickness='0' TextWrapping='Wrap' VerticalScrollBarVisibility='Auto' CaretBrush='Transparent'/>
                </Grid>
            </Border>
        </Grid>
    </Border>
</Window>";

        private class CheckRow
        {
            public string Name, Host, Url;
            public Ellipse Led;
            public TextBlock Status;
        }

        private readonly string screenshotPath, appsShotPath;
        private Window win;
        private AppSettings settings;
        private bool busy;
        private bool running;
        private bool loadingUi;
        private bool trayHintShown;
        private bool exiting;
        private Gdpi.ReleaseInfo gdpiLatest, appLatest;
        private readonly List<CheckRow> checks = new List<CheckRow>();

        private System.Windows.Forms.NotifyIcon tray;
        private System.Windows.Forms.ToolStripMenuItem trayToggle;
        private System.Drawing.Icon iconOn, iconOff;

        private static readonly Brush BrPink = Hex("#EE2D9F");
        private static readonly Brush BrGreen = Hex("#3DDC97");
        private static readonly Brush BrAmber = Hex("#FFC857");
        private static readonly Brush BrRed = Hex("#FF5470");
        private static readonly Brush BrIdle = Hex("#6B5563");
        private static readonly Brush BrMuted = Hex("#BFA3B5");

        public MainWindow(string screenshotPath, string appsShotPath)
        {
            this.screenshotPath = screenshotPath;
            this.appsShotPath = appsShotPath;
        }

        private static SolidColorBrush Hex(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private T Find<T>(string name) where T : class
        {
            return win.FindName(name) as T;
        }

        public Window Build()
        {
            win = Theme.Parse(Xaml);
            settings = AppSettings.Load();

            Find<Grid>("bdTitleBar").MouseLeftButtonDown += DragWindow;
            Find<Border>("bdHeader").MouseLeftButtonDown += DragWindow;
            Find<Button>("btnClose").Click += (s, e) => ExitApp();
            Find<Button>("btnMinimize").Click += (s, e) => HideToTray();
            win.Closing += (s, e) =>
            {
                // Alt+F4 exits the whole app instead of leaving a dead window behind the tray icon
                if (!exiting) { e.Cancel = true; ExitApp(); }
            };
            Find<Button>("btnClearLog").Click += (s, e) => Find<TextBox>("txtLog").Clear();
            Find<TextBlock>("txtTitleVer").Text = "  ·  v" + AppUpdater.CurrentVersion;
            Find<TextBlock>("txtAppVer").Text = "v" + AppUpdater.CurrentVersion;

            LoadLogo();
            BuildCheckRows();
            SetupTray();

            loadingUi = true;
            Find<CheckBox>("chkAutoStart").IsChecked = settings.AutoStart || Gdpi.ServiceExists();
            Find<CheckBox>("chkStrong").IsChecked = settings.StrongMode;
            loadingUi = false;

            foreach (var name in new[] { "chkAutoStart", "chkStrong" })
            {
                var cb = Find<CheckBox>(name);
                cb.Checked += OnOptionChanged;
                cb.Unchecked += OnOptionChanged;
            }

            var power = Find<Grid>("btnPower");
            power.MouseLeftButtonUp += (s, e) => TogglePower();
            power.MouseEnter += (s, e) => power.RenderTransform = new ScaleTransform(1.04, 1.04, 88, 88);
            power.MouseLeave += (s, e) => power.RenderTransform = null;

            Find<Button>("btnPickApps").Click += (s, e) => PickApps();
            Find<Button>("btnTest").Click += (s, e) => RunChecks();
            Find<Button>("btnUpdate").Click += (s, e) => OnUpdateClick();
            Find<Button>("btnOpenFolder").Click += (s, e) => Process.Start("explorer.exe", Gdpi.Root);
            Find<Button>("btnResetDns").Click += (s, e) => ResetDns();
            Find<Button>("btnGetSteam").Click += (s, e) =>
            {
                Log("Đang mở trang tải Steam chính thức. Nếu trang không mở được, hãy bấm nút tròn để bật trước.");
                Program.OpenUrl(Program.SteamDownloadUrl);
            };

            running = Gdpi.IsRunning();
            RenderState();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) =>
            {
                if (busy) return;
                bool now = Gdpi.IsRunning();
                if (now != running) { running = now; RenderState(); }
            };
            timer.Start();

            return win;
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) win.DragMove();
        }

        public void Init(bool startInTray)
        {
            Log("Siêu Nhân Hồng đã sẵn sàng biến hình!");
            if (startInTray) Log("App đang chạy ở khay (cạnh đồng hồ). Bấm đúp vào biểu tượng để mở.");

            if (screenshotPath != null)
            {
                var shot = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                shot.Tick += (s, e) => { shot.Stop(); SaveScreenshot(win, screenshotPath); ExitApp(); };
                shot.Start();
            }
            if (appsShotPath != null)
            {
                var picker = new AppPickerWindow(settings).Build(win);
                picker.Show();
                var shot = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                shot.Tick += (s, e) => { shot.Stop(); SaveScreenshot(picker, appsShotPath); ExitApp(); };
                shot.Start();
                return;
            }

            Background(() =>
            {
                Gdpi.EnsureInstalled(Log);
                Ui(() =>
                {
                    Find<TextBlock>("txtVerInstalled").Text = FormatVer(Gdpi.InstalledVersion());
                    RenderState();
                });
                Log(running ? "Đang hoạt động, Steam đã được mở khoá." : "Chưa bật. Bấm vào nút tròn để bật.");
                if (Program.FindSteamExe() == null)
                {
                    Log("Chưa thấy Steam trên máy này. Bấm nút \"Tải Steam\" ở góc phải để tải từ trang chính thức.");
                    Ui(() => Find<Button>("btnGetSteam").Visibility = Visibility.Visible);
                }
                CheckLatest(false);
            });
            RunChecks();
        }

        // ---------------- tray ----------------
        private void SetupTray()
        {
            try
            {
                iconOn = IconFromLogo(false);
                iconOff = IconFromLogo(true);
            }
            catch
            {
                iconOn = iconOff = System.Drawing.SystemIcons.Application;
            }

            var menu = new System.Windows.Forms.ContextMenuStrip();
            var open = new System.Windows.Forms.ToolStripMenuItem("Mở Siêu Nhân Hồng");
            open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
            open.Click += (s, e) => ShowFromTray();
            trayToggle = new System.Windows.Forms.ToolStripMenuItem("Bật");
            trayToggle.Click += (s, e) => TogglePower();
            var exit = new System.Windows.Forms.ToolStripMenuItem("Thoát app");
            exit.Click += (s, e) => ExitApp();
            menu.Items.Add(open);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(trayToggle);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(exit);

            tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = iconOff,
                Text = "Siêu Nhân Hồng",
                ContextMenuStrip = menu,
                Visible = screenshotPath == null && appsShotPath == null
            };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowFromTray();
            };
        }

        // Tray icon drawn from the helmet logo: pink when on, grey when off
        private static System.Drawing.Icon IconFromLogo(bool gray)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon.png"))
            using (var src = new System.Drawing.Bitmap(s))
            using (var bmp = new System.Drawing.Bitmap(32, 32))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    float scale = Math.Min(32f / src.Width, 32f / src.Height);
                    int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
                    var dest = new System.Drawing.Rectangle((32 - w) / 2, (32 - h) / 2, w, h);

                    System.Drawing.Imaging.ImageAttributes attrs = null;
                    if (gray)
                    {
                        attrs = new System.Drawing.Imaging.ImageAttributes();
                        attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[]
                        {
                            new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
                            new float[] { 0.59f, 0.59f, 0.59f, 0, 0 },
                            new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                            new float[] { 0, 0, 0, 0.8f, 0 },
                            new float[] { 0, 0, 0, 0, 1 }
                        }));
                    }
                    g.DrawImage(src, dest, 0, 0, src.Width, src.Height, System.Drawing.GraphicsUnit.Pixel, attrs);
                }
                return System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void HideToTray()
        {
            win.Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(4000, "Siêu Nhân Hồng vẫn đang chạy",
                    "App đã thu nhỏ xuống đây. Bấm vào biểu tượng mũ hồng để mở lại.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
        }

        public void ShowFromTray()
        {
            win.Show();
            if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            win.Activate();
            win.Topmost = true;
            win.Topmost = false;
        }

        private void ExitApp()
        {
            exiting = true;
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Application.Current.Shutdown();
        }

        // ---------------- power ----------------
        private void TogglePower()
        {
            if (busy) return;
            if (running) StopPower(); else StartPower();
        }

        private void StartPower()
        {
            SetBusy(true, "Đang biến hình...");
            Background(() =>
            {
                Gdpi.EnsureInstalled(Log);
                Log(string.Format("Đang bật (cho {0}, {1})...",
                    settings.ScopeText(), settings.StrongMode ? "chế độ mạnh" : "chế độ thường"));
                bool ok = Gdpi.Start(settings, Log);
                Log(ok ? "BIẾN HÌNH THÀNH CÔNG! Giờ bạn có thể mở Steam." : "Không bật được. Hãy tắt phần mềm diệt virus rồi thử lại.");
                Ui(() =>
                {
                    running = Gdpi.IsRunning();
                    SetBusy(false, null);
                    RenderState();
                    if (ok && !win.IsVisible)
                        tray.ShowBalloonTip(3000, "Siêu Nhân Hồng", "Đã bật. Giờ bạn có thể mở Steam.", System.Windows.Forms.ToolTipIcon.Info);
                });
                if (ok) { Thread.Sleep(500); Ui(RunChecks); }
            });
        }

        private void StopPower()
        {
            SetBusy(true, "Đang tắt...");
            Background(() =>
            {
                Log("Đang tắt...");
                Gdpi.StopAll(Log, true);
                Log("Đã tắt. Mạng trở về bình thường.");
                Ui(() =>
                {
                    running = Gdpi.IsRunning();
                    SetBusy(false, null);
                    RenderState();
                });
            });
        }

        private void OnOptionChanged(object sender, RoutedEventArgs e)
        {
            if (loadingUi) return;
            settings.AutoStart = Find<CheckBox>("chkAutoStart").IsChecked == true;
            settings.StrongMode = Find<CheckBox>("chkStrong").IsChecked == true;
            SettingsChanged();
        }

        private void SettingsChanged()
        {
            settings.Save();
            RenderState();

            if (running && !busy)
            {
                Log("Đã đổi tuỳ chọn, đang áp dụng lại...");
                StartPower();
            }
            else
            {
                Log("Đã lưu tuỳ chọn. Sẽ áp dụng khi bạn bật.");
            }
        }

        private void PickApps()
        {
            if (busy) return;
            var dlg = new AppPickerWindow(settings).Build(win);
            if (dlg.ShowDialog() == true)
            {
                Log("Áp dụng cho: " + settings.ScopeText() + ".");
                SettingsChanged();
            }
        }

        private void SetBusy(bool b, string label)
        {
            busy = b;
            if (b)
            {
                Find<TextBlock>("txtPowerLabel").Text = "...";
                Find<TextBlock>("txtStateTitle").Text = label;
                Find<TextBlock>("txtStatusPill").Text = label;
                Find<Ellipse>("ledStatus").Fill = BrAmber;
                trayToggle.Enabled = false;
            }
            foreach (var name in new[] { "chkAutoStart", "chkStrong", "btnPickApps", "btnUpdate" })
                ((UIElement)win.FindName(name)).IsEnabled = !b;
        }

        private void RenderState()
        {
            if (busy) return;
            var core = Find<Ellipse>("ellPowerCore");
            var ring = Find<Ellipse>("ellPowerRing");
            var icon = Find<TextBlock>("txtPowerIcon");
            string scope = settings.ScopeText();
            string mode = settings.StrongMode ? "chế độ mạnh" : "chế độ thường";
            var appNames = AppPreset.All.Where(p => settings.Apps.Contains(p.Id)).Select(p => p.Name).ToArray();
            Find<TextBlock>("txtScope").Text = settings.AllSites ? "Mọi trang web và app"
                : appNames.Length > 0 ? string.Join(", ", appNames) : scope;

            trayToggle.Enabled = true;
            if (running)
            {
                core.Fill = (Brush)win.FindResource("PinkGrad");
                ring.Stroke = BrPink;
                icon.Foreground = Brushes.White;
                Find<TextBlock>("txtPowerLabel").Text = "ĐANG BẬT";
                Find<TextBlock>("txtStateTitle").Text = "Đã mở khoá";
                Find<TextBlock>("txtStateDesc").Text = scope + " · " + mode + "\nBấm vào nút tròn để tắt";
                Find<TextBlock>("txtStatusPill").Text = "Đang hoạt động · " + scope;
                Find<Ellipse>("ledStatus").Fill = BrGreen;

                var glow = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#EE2D9F"), ShadowDepth = 0, BlurRadius = 30, Opacity = 0.9 };
                core.Effect = glow;
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
                    new DoubleAnimation(22, 48, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

                tray.Icon = iconOn;
                tray.Text = "Siêu Nhân Hồng - Đang hoạt động";
                trayToggle.Text = "Tắt";
            }
            else
            {
                core.Fill = Hex("#2A1722");
                ring.Stroke = Hex("#4A2A3E");
                icon.Foreground = BrMuted;
                core.Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 12, Opacity = 0.6 };
                Find<TextBlock>("txtPowerLabel").Text = "BẬT";
                Find<TextBlock>("txtStateTitle").Text = "Chưa bật";
                Find<TextBlock>("txtStateDesc").Text = "Bấm vào nút tròn để bật\n" + scope + " · " + mode;
                Find<TextBlock>("txtStatusPill").Text = "Chưa bật";
                Find<Ellipse>("ledStatus").Fill = BrIdle;

                tray.Icon = iconOff;
                tray.Text = "Siêu Nhân Hồng - Chưa bật";
                trayToggle.Text = "Bật";
            }
        }

        // ---------------- connection checks ----------------
        private void BuildCheckRows()
        {
            var defs = new[]
            {
                new[] { "Cửa hàng Steam", "store.steampowered.com" },
                new[] { "Cộng đồng Steam", "steamcommunity.com" },
                new[] { "Đăng nhập Steam", "login.steampowered.com" },
                new[] { "Tải và cập nhật game", "client-update.steamstatic.com" }
            };
            var panel = Find<StackPanel>("pnlChecks");
            foreach (var d in defs)
            {
                var row = new CheckRow { Name = d[0], Host = d[1], Url = "https://" + d[1] + "/" };
                var bd = new Border
                {
                    Background = (Brush)win.FindResource("Row"),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Led = new Ellipse { Width = 12, Height = 12, Fill = BrIdle, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                var names = new StackPanel();
                names.Children.Add(new TextBlock { Text = row.Name, FontSize = 15, FontWeight = FontWeights.SemiBold });
                names.Children.Add(new TextBlock { Text = row.Host, FontSize = 12, Foreground = BrMuted });
                row.Status = new TextBlock { Text = "Chưa thử", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = BrMuted, VerticalAlignment = VerticalAlignment.Center };

                Grid.SetColumn(names, 1);
                Grid.SetColumn(row.Status, 2);
                g.Children.Add(row.Led);
                g.Children.Add(names);
                g.Children.Add(row.Status);
                bd.Child = g;
                panel.Children.Add(bd);
                checks.Add(row);
            }
        }

        private void RunChecks()
        {
            var btn = Find<Button>("btnTest");
            if (!btn.IsEnabled) return;
            btn.IsEnabled = false;
            foreach (var c in checks)
            {
                c.Led.Fill = BrAmber;
                c.Status.Text = "Đang thử...";
                c.Status.Foreground = BrAmber;
            }

            int remaining = checks.Count;
            int passed = 0;
            foreach (var c in checks)
            {
                var row = c;
                Background(() =>
                {
                    string detail;
                    bool ok = Probe(row.Url, out detail);
                    if (ok) Interlocked.Increment(ref passed);
                    Ui(() =>
                    {
                        row.Led.Fill = ok ? BrGreen : BrRed;
                        row.Status.Text = ok ? "Vào được" : "Bị chặn";
                        row.Status.Foreground = ok ? BrGreen : BrRed;
                    });
                    if (!ok) Log(row.Name + ": chưa vào được.");
                    if (Interlocked.Decrement(ref remaining) == 0)
                    {
                        Log(passed == checks.Count
                            ? "Kết quả: vào Steam bình thường!"
                            : string.Format("Kết quả: {0}/{1} mục vào được.{2}", passed, checks.Count,
                                running ? " Hãy bật Chế độ mạnh rồi thử lại." : " Hãy bấm vào nút tròn để bật."));
                        Ui(() => btn.IsEnabled = true);
                    }
                });
            }
        }

        private static bool Probe(string url, out string detail)
        {
            detail = "";
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "HEAD";
                req.Timeout = 10000;
                req.KeepAlive = false;
                req.AllowAutoRedirect = false;
                req.UserAgent = "Mozilla/5.0 SieuNhanHong";
                using (req.GetResponse()) { }
                return true;
            }
            catch (WebException ex)
            {
                // Any HTTP answer (403, 404...) means the TLS handshake got through
                if (ex.Response != null) { ex.Response.Close(); return true; }
                detail = ex.Status.ToString();
                return false;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        // ---------------- updates ----------------
        private bool AppHasUpdate()
        {
            return appLatest != null && appLatest.ExeUrl != null &&
                   Gdpi.CompareVersions(appLatest.Tag, AppUpdater.CurrentVersion) > 0;
        }

        private bool GdpiHasUpdate()
        {
            return gdpiLatest != null && gdpiLatest.ZipUrl != null &&
                   Gdpi.CompareVersions(gdpiLatest.Tag, Gdpi.InstalledVersion()) > 0;
        }

        private void CheckLatest(bool userAsked)
        {
            try { appLatest = Gdpi.FetchLatest(AppUpdater.RepoApi); } catch { appLatest = null; }
            try { gdpiLatest = Gdpi.FetchLatest(Gdpi.RepoApi); } catch { gdpiLatest = null; }

            Ui(() =>
            {
                Find<TextBlock>("txtAppLatest").Text = appLatest != null ? FormatVer(appLatest.Tag) : "Không rõ";
                Find<TextBlock>("txtVerLatest").Text = gdpiLatest != null ? FormatVer(gdpiLatest.Tag) : "Không rõ";
                Find<TextBlock>("txtUpdateBtn").Text = AppHasUpdate() ? "Cập nhật app lên " + FormatVer(appLatest.Tag)
                    : GdpiHasUpdate() ? "Cập nhật công cụ lên " + FormatVer(gdpiLatest.Tag)
                    : "Tìm bản mới";
            });

            if (AppHasUpdate()) Log("Có bản Siêu Nhân Hồng mới " + appLatest.Tag + ". Bấm nút Cập nhật để cài.");
            if (GdpiHasUpdate()) Log("Có bản GoodbyeDPI mới " + gdpiLatest.Tag + ". Bấm nút Cập nhật để cài.");
            if (userAsked)
            {
                if (appLatest == null && gdpiLatest == null) Log("Không tìm được bản mới, hãy kiểm tra mạng rồi thử lại.");
                else if (!AppHasUpdate() && !GdpiHasUpdate()) Log("Bạn đang dùng bản mới nhất.");
            }
        }

        private void OnUpdateClick()
        {
            if (busy) return;
            if (AppHasUpdate()) { UpdateApp(); return; }
            if (GdpiHasUpdate()) { UpdateGdpi(); return; }

            Log("Đang tìm bản mới...");
            Find<Button>("btnUpdate").IsEnabled = false;
            Background(() =>
            {
                CheckLatest(true);
                Ui(() => Find<Button>("btnUpdate").IsEnabled = true);
            });
        }

        private void UpdateApp()
        {
            SetBusy(true, "Đang cập nhật...");
            var rel = appLatest;
            Background(() =>
            {
                try
                {
                    AppUpdater.Install(rel, Log);
                    Ui(() =>
                    {
                        Program.ReleaseInstanceLock();
                        Process.Start(Program.AppExePath);
                        ExitApp();
                    });
                }
                catch (Exception ex)
                {
                    Log("Cập nhật không thành công, hãy thử lại sau. (" + ex.Message + ")");
                    Ui(() => { SetBusy(false, null); RenderState(); });
                }
            });
        }

        private void UpdateGdpi()
        {
            bool wasRunning = running;
            SetBusy(true, "Đang cập nhật...");
            var rel = gdpiLatest;
            Background(() =>
            {
                try
                {
                    Gdpi.InstallUpdate(rel, Log);
                    if (wasRunning)
                    {
                        Log("Đang bật lại...");
                        Gdpi.Start(settings, Log);
                    }
                }
                catch (Exception ex)
                {
                    Log("Cập nhật không thành công, hãy thử lại sau. (" + ex.Message + ")");
                }
                Ui(() =>
                {
                    running = Gdpi.IsRunning();
                    Find<TextBlock>("txtVerInstalled").Text = FormatVer(Gdpi.InstalledVersion());
                    Find<TextBlock>("txtUpdateBtn").Text = AppHasUpdate() ? "Cập nhật app lên " + FormatVer(appLatest.Tag) : "Tìm bản mới";
                    SetBusy(false, null);
                    RenderState();
                });
            });
        }

        // ---------------- misc ----------------
        private static string FormatVer(string v)
        {
            if (string.IsNullOrEmpty(v) || !char.IsDigit(v.TrimStart('v', 'V').FirstOrDefault())) return v;
            return "v" + v.TrimStart('v', 'V');
        }

        private void ResetDns()
        {
            Background(() =>
            {
                Log("Đang khôi phục cài đặt mạng mặc định...");
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    try
                    {
                        var p = n.GetIPProperties();
                        if (!p.GatewayAddresses.Any(g => g.Address.ToString() != "0.0.0.0" && g.Address.ToString() != "::")) continue;
                        Gdpi.RunCapture("netsh.exe", string.Format("interface ipv4 set dnsservers name={0} source=dhcp", p.GetIPv4Properties().Index));
                        try { Gdpi.RunCapture("netsh.exe", string.Format("interface ipv6 set dnsservers name={0} source=dhcp", p.GetIPv6Properties().Index)); } catch { }
                        Log("   -> " + n.Name + ": xong.");
                    }
                    catch { }
                }
                try { Program.DnsFlushResolverCache(); } catch { }
                Log("Xong. Mạng đã về cài đặt mặc định.");
            });
        }

        private void LoadLogo()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon.png"))
                {
                    if (stream == null) return;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = stream;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 160;
                    bmp.EndInit();
                    Find<Image>("imgLogo").Source = bmp;
                    win.Icon = bmp;
                }
            }
            catch { }
        }

        private static void SaveScreenshot(Window w, string path)
        {
            try
            {
                var root = (FrameworkElement)w.Content;
                var rtb = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                    dc.DrawRectangle(new VisualBrush(root), null, new Rect(new Point(10, 10), new Size(root.ActualWidth, root.ActualHeight)));
                rtb.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var f = File.Create(path)) enc.Save(f);
            }
            catch { }
        }

        private void Log(string msg)
        {
            Ui(() =>
            {
                var box = Find<TextBox>("txtLog");
                box.AppendText(string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, msg));
                box.ScrollToEnd();
            });
        }

        private void Ui(Action a)
        {
            if (win.Dispatcher.CheckAccess()) a();
            else win.Dispatcher.Invoke(a);
        }

        private void Background(Action a)
        {
            Task.Factory.StartNew(() =>
            {
                try { a(); }
                catch (Exception ex) { Log("Lỗi: " + ex.Message); }
            });
        }
    }
}

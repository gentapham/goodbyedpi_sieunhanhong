using System;
using System.Collections;
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
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

[assembly: AssemblyTitle("Siêu Nhân Hồng - Vượt chặn Steam")]
[assembly: AssemblyDescription("Siêu Nhân Hồng - Vượt chặn Steam bằng GoodbyeDPI, không VPN, không lag")]
[assembly: AssemblyCompany("Newwyn Team")]
[assembly: AssemblyProduct("Siêu Nhân Hồng")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("4.0.0.0")]
[assembly: AssemblyFileVersion("4.0.0.0")]

namespace SieuNhanHong
{
    // ============================================================
    // Settings saved next to GoodbyeDPI (C:\GoodbyeDPI\sieunhanhong.ini)
    // ============================================================
    public class AppSettings
    {
        public bool SteamOnly = true;
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
                    bool v = parts[1].Trim() == "1";
                    switch (parts[0].Trim())
                    {
                        case "SteamOnly": s.SteamOnly = v; break;
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
                    "SteamOnly={0}\r\nAutoStart={1}\r\nStrongMode={2}\r\n",
                    SteamOnly ? 1 : 0, AutoStart ? 1 : 0, StrongMode ? 1 : 0));
            }
            catch { }
        }
    }

    // ============================================================
    // GoodbyeDPI install / run / service / update
    // ============================================================
    public static class Gdpi
    {
        public const string BundledVersion = "0.2.2";
        public const string ServiceName = "GoodbyeDPI";
        public const string RepoApi = "https://api.github.com/repos/ValdikSS/GoodbyeDPI/releases/latest";

        public static readonly string Root = @"C:\GoodbyeDPI";
        public static readonly string BinDir = System.IO.Path.Combine(Root, "x86_64");
        public static readonly string ExePath = System.IO.Path.Combine(BinDir, "goodbyedpi.exe");
        public static readonly string BlacklistPath = System.IO.Path.Combine(Root, "steam-blacklist.txt");
        public static readonly string VersionPath = System.IO.Path.Combine(Root, "version.txt");
        public static readonly string SettingsPath = System.IO.Path.Combine(Root, "sieunhanhong.ini");

        public static readonly string[] DefaultSteamDomains = new[]
        {
            "steampowered.com", "steamcommunity.com", "steamstatic.com", "steamcontent.com",
            "steamserver.net", "steamusercontent.com", "steamgames.com", "steam-chat.com",
            "steamdeck.com", "s.team", "valvesoftware.com",
            "steamcdn-a.akamaihd.net", "steamstore-a.akamaihd.net",
            "steamcommunity-a.akamaihd.net", "steamuserimages-a.akamaihd.net",
            "cdn.akamai.steamstatic.com"
        };

        // Extract the bundled GoodbyeDPI build if nothing is installed yet
        public static void EnsureInstalled(Action<string> log)
        {
            Directory.CreateDirectory(BinDir);
            if (!File.Exists(ExePath))
            {
                log("Đang cài GoodbyeDPI v" + BundledVersion + " (bản đóng gói sẵn) vào " + Root + "...");
                ExtractResource("gdpi.goodbyedpi.exe", ExePath);
                ExtractResource("gdpi.WinDivert.dll", System.IO.Path.Combine(BinDir, "WinDivert.dll"));
                ExtractResource("gdpi.WinDivert64.sys", System.IO.Path.Combine(BinDir, "WinDivert64.sys"));
                string licDir = System.IO.Path.Combine(Root, "licenses");
                Directory.CreateDirectory(licDir);
                foreach (var lic in new[] { "LICENSE-goodbyedpi.txt", "LICENSE-windivert.txt", "LICENSE-getline.txt", "LICENSE-uthash.txt" })
                    ExtractResource("gdpi." + lic, System.IO.Path.Combine(licDir, lic));
                File.WriteAllText(VersionPath, BundledVersion);
                log("   -> Cài đặt xong.");
            }
            else if (!File.Exists(VersionPath))
            {
                File.WriteAllText(VersionPath, BundledVersion);
            }

            if (!File.Exists(BlacklistPath))
            {
                File.WriteAllLines(BlacklistPath, DefaultSteamDomains);
                log("Đã tạo danh sách tên miền Steam: " + BlacklistPath);
            }
        }

        private static void ExtractResource(string name, string target)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("Thiếu tài nguyên đóng gói: " + name);
                using (var f = File.Create(target)) s.CopyTo(f);
            }
        }

        public static string InstalledVersion()
        {
            try { return File.Exists(VersionPath) ? File.ReadAllText(VersionPath).Trim() : "chưa cài"; }
            catch { return "?"; }
        }

        public static string BuildArgs(AppSettings s)
        {
            string args = s.StrongMode ? "-5" : "-e 1 --native-frag --max-payload";
            if (s.SteamOnly) args += " --blacklist \"" + BlacklistPath + "\"";
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

        public static void StopAll(Action<string> log, bool removeService)
        {
            if (ServiceExists())
            {
                RunCapture("sc.exe", "stop " + ServiceName);
                if (removeService)
                {
                    RunCapture("sc.exe", "delete " + ServiceName);
                    log("   -> Đã gỡ chế độ tự chạy cùng Windows.");
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
            StopAll(log, true);
            string args = BuildArgs(s);
            log("Tham số: " + args);

            if (s.AutoStart)
            {
                string bin = "\\\"" + ExePath + "\\\" " + args.Replace("\"", "\\\"");
                string output;
                int code = RunCapture("sc.exe", "create " + ServiceName + " binPath= \"" + bin + "\" start= auto DisplayName= \"GoodbyeDPI (Siêu Nhân Hồng)\"", out output);
                if (code != 0)
                {
                    log("   -> Tạo dịch vụ thất bại: " + output.Trim());
                    return false;
                }
                RunCapture("sc.exe", "description " + ServiceName + " \"Vượt chặn DPI cho Steam - quản lý bởi Siêu Nhân Hồng\"");
                RunCapture("sc.exe", "start " + ServiceName, out output);
                log("   -> Đã cài chế độ tự chạy cùng Windows.");
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

        // ---------------- GitHub update ----------------
        public class ReleaseInfo { public string Tag; public string ZipUrl; }

        public static ReleaseInfo FetchLatest()
        {
            var req = (HttpWebRequest)WebRequest.Create(RepoApi);
            req.UserAgent = "SieuNhanHong-Updater";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            string json;
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = sr.ReadToEnd();

            var ser = new JavaScriptSerializer();
            var root = (Dictionary<string, object>)ser.DeserializeObject(json);
            var info = new ReleaseInfo { Tag = Convert.ToString(root["tag_name"]) };
            var assets = root["assets"] as object[];
            if (assets != null)
            {
                foreach (var a in assets)
                {
                    var d = (Dictionary<string, object>)a;
                    string name = Convert.ToString(d["name"]);
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        info.ZipUrl = Convert.ToString(d["browser_download_url"]);
                        break;
                    }
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

            log("Đang tải " + rel.ZipUrl);
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
            if (newExe == null) throw new Exception("Không tìm thấy goodbyedpi.exe (x86_64) trong gói tải về.");
            string newBin = System.IO.Path.GetDirectoryName(newExe);

            log("Đang dừng GoodbyeDPI để thay file...");
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
            log("   -> Đã cập nhật lên GoodbyeDPI " + rel.Tag + ".");
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
    // App
    // ============================================================
    public class Program
    {
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        public static extern int DnsFlushResolverCache();

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

            string screenshotPath = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--screenshot") screenshotPath = args[i + 1];

            if (!IsAdministrator())
            {
                try
                {
                    var proc = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Environment.CurrentDirectory,
                        Verb = "runas"
                    };
                    Process.Start(proc);
                }
                catch
                {
                    MessageBox.Show("Siêu Nhân Hồng cần quyền Administrator để điều khiển GoodbyeDPI!",
                                    "Cần quyền quản trị", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            var app = new Application();
            var window = new MainWindow(screenshotPath).Build();
            app.Run(window);
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

    public class MainWindow
    {
        private const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='Siêu Nhân Hồng - Vượt chặn Steam'
        Width='1120' Height='720'
        WindowStartupLocation='CenterScreen'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent'
        ResizeMode='CanMinimize'
        FontFamily='Segoe UI Variable Text, Segoe UI'
        FontSize='15'
        TextOptions.TextFormattingMode='Ideal'
        UseLayoutRounding='True' SnapsToDevicePixels='True'>

    <Window.Resources>
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
        </Style>
    </Window.Resources>

    <Border CornerRadius='20' BorderBrush='#EE2D9F' BorderThickness='1.5' Margin='10'>
        <Border.Background>
            <LinearGradientBrush StartPoint='0,0' EndPoint='1,1'>
                <GradientStop Color='#1D0E18' Offset='0'/>
                <GradientStop Color='#140A11' Offset='0.55'/>
                <GradientStop Color='#100810' Offset='1'/>
            </LinearGradientBrush>
        </Border.Background>
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
                    <TextBlock Text='  ·  v4.0' Foreground='#8D6F82' FontSize='13'/>
                </StackPanel>
                <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
                    <Button Name='btnMinimize' Style='{StaticResource WinBtn}'>
                        <TextBlock Style='{StaticResource Icon}' Text='&#xE921;' FontSize='11' Foreground='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}'/>
                    </Button>
                    <Button Name='btnClose' Style='{StaticResource WinBtn}' Margin='4,0,0,0'>
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
                        <TextBlock Text='Vượt chặn Steam bằng GoodbyeDPI  —  không VPN, không lag, vẫn tốc độ tối đa' Foreground='{StaticResource Muted}' FontSize='15' Margin='2,2,0,0'/>
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
                            <TextBlock Name='txtStateTitle' Text='Đang tắt' FontFamily='Segoe UI Variable Display, Segoe UI' FontSize='22' FontWeight='Bold' HorizontalAlignment='Center'/>
                            <TextBlock Name='txtStateDesc' Text='Bấm nút để biến hình!' Foreground='{StaticResource Muted}' FontSize='14' HorizontalAlignment='Center' TextAlignment='Center' TextWrapping='Wrap' Margin='0,4,0,0'/>
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
                                    <TextBlock Text='Chỉ áp dụng cho Steam' FontWeight='SemiBold' FontSize='15'/>
                                    <TextBlock Name='txtSteamOnlyHint' Text='Bỏ chọn để áp dụng cho mọi trang web' Foreground='{StaticResource Muted}' FontSize='13' TextWrapping='Wrap'/>
                                </StackPanel>
                                <CheckBox Grid.Column='2' Name='chkSteamOnly' Style='{StaticResource Switch}' IsChecked='True'/>
                            </Grid>
                            <Grid Margin='0,0,0,12'>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='34'/>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='Auto'/>
                                </Grid.ColumnDefinitions>
                                <TextBlock Style='{StaticResource Icon}' Text='&#xE7E8;' FontSize='20' Foreground='#FF7AC8'/>
                                <StackPanel Grid.Column='1' Margin='0,0,10,0'>
                                    <TextBlock Text='Tự bật cùng Windows' FontWeight='SemiBold' FontSize='15'/>
                                    <TextBlock Text='Chạy ngầm, không cần mở app' Foreground='{StaticResource Muted}' FontSize='13' TextWrapping='Wrap'/>
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
                                    <TextBlock Text='Chỉ bật khi chế độ nhẹ không vào được' Foreground='{StaticResource Muted}' FontSize='13' TextWrapping='Wrap'/>
                                </StackPanel>
                                <CheckBox Grid.Column='2' Name='chkStrong' Style='{StaticResource Switch}'/>
                            </Grid>
                        </StackPanel>
                    </Border>

                    <Border Grid.Row='2' Background='{StaticResource Card}' BorderBrush='{StaticResource CardLine}' BorderThickness='1' CornerRadius='18' Padding='18,16'>
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height='Auto'/>
                                <RowDefinition Height='Auto'/>
                                <RowDefinition Height='*'/>
                                <RowDefinition Height='Auto'/>
                            </Grid.RowDefinitions>
                            <TextBlock Style='{StaticResource CardTitle}' Text='GOODBYEDPI'/>
                            <Grid Grid.Row='1'>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='*'/>
                                </Grid.ColumnDefinitions>
                                <StackPanel>
                                    <TextBlock Text='Đang dùng' Foreground='{StaticResource Muted}' FontSize='13'/>
                                    <TextBlock Name='txtVerInstalled' Text='...' FontSize='22' FontWeight='Bold'/>
                                </StackPanel>
                                <StackPanel Grid.Column='1'>
                                    <TextBlock Text='Mới nhất trên GitHub' Foreground='{StaticResource Muted}' FontSize='13'/>
                                    <TextBlock Name='txtVerLatest' Text='...' FontSize='22' FontWeight='Bold' Foreground='#FF7AC8'/>
                                </StackPanel>
                            </Grid>
                            <Grid Grid.Row='3'>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width='*'/>
                                    <ColumnDefinition Width='8'/>
                                    <ColumnDefinition Width='Auto'/>
                                    <ColumnDefinition Width='8'/>
                                    <ColumnDefinition Width='Auto'/>
                                </Grid.ColumnDefinitions>
                                <Button Name='btnUpdate' Style='{StaticResource PinkBtn}'>
                                    <StackPanel Orientation='Horizontal'>
                                        <TextBlock Style='{StaticResource Icon}' Text='&#xE895;' FontSize='15' Foreground='White' Margin='0,0,8,0'/>
                                        <TextBlock Name='txtUpdateBtn' Text='Kiểm tra cập nhật' Foreground='White'/>
                                    </StackPanel>
                                </Button>
                                <Button Grid.Column='2' Name='btnEditList' Style='{StaticResource GhostBtn}' ToolTip='Sửa danh sách tên miền Steam'>
                                    <TextBlock Style='{StaticResource Icon}' Text='&#xE70F;' FontSize='16'/>
                                </Button>
                                <Button Grid.Column='4' Name='btnOpenFolder' Style='{StaticResource GhostBtn}' ToolTip='Mở thư mục GoodbyeDPI'>
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
                        <TextBlock Style='{StaticResource CardTitle}' Text='KIỂM TRA KẾT NỐI STEAM'/>
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
                            <Button Name='btnResetDns' Style='{StaticResource WinBtn}' Width='Auto' Padding='10,0'>
                                <TextBlock Text='Đưa DNS về mặc định' FontSize='13' Margin='10,0' Foreground='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}'/>
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

        private readonly string screenshotPath;
        private Window win;
        private AppSettings settings;
        private bool busy;
        private bool running;
        private bool loadingUi;
        private Gdpi.ReleaseInfo latest;
        private readonly List<CheckRow> checks = new List<CheckRow>();

        private static readonly Brush BrPink = Hex("#EE2D9F");
        private static readonly Brush BrGreen = Hex("#3DDC97");
        private static readonly Brush BrAmber = Hex("#FFC857");
        private static readonly Brush BrRed = Hex("#FF5470");
        private static readonly Brush BrIdle = Hex("#6B5563");
        private static readonly Brush BrMuted = Hex("#BFA3B5");

        public MainWindow(string screenshotPath)
        {
            this.screenshotPath = screenshotPath;
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
            win = (Window)XamlReader.Parse(Xaml);
            settings = AppSettings.Load();

            Find<Grid>("bdTitleBar").MouseLeftButtonDown += DragWindow;
            Find<Border>("bdHeader").MouseLeftButtonDown += DragWindow;
            Find<Button>("btnClose").Click += (s, e) => win.Close();
            Find<Button>("btnMinimize").Click += (s, e) => win.WindowState = WindowState.Minimized;
            Find<Button>("btnClearLog").Click += (s, e) => Find<TextBox>("txtLog").Clear();

            LoadLogo();
            BuildCheckRows();

            loadingUi = true;
            Find<CheckBox>("chkSteamOnly").IsChecked = settings.SteamOnly;
            Find<CheckBox>("chkAutoStart").IsChecked = settings.AutoStart || Gdpi.ServiceExists();
            Find<CheckBox>("chkStrong").IsChecked = settings.StrongMode;
            loadingUi = false;

            foreach (var name in new[] { "chkSteamOnly", "chkAutoStart", "chkStrong" })
            {
                var cb = Find<CheckBox>(name);
                cb.Checked += OnOptionChanged;
                cb.Unchecked += OnOptionChanged;
            }

            var power = Find<Grid>("btnPower");
            power.MouseLeftButtonUp += (s, e) => TogglePower();
            power.MouseEnter += (s, e) => power.RenderTransform = new ScaleTransform(1.04, 1.04, 88, 88);
            power.MouseLeave += (s, e) => power.RenderTransform = null;

            Find<Button>("btnTest").Click += (s, e) => RunChecks();
            Find<Button>("btnUpdate").Click += (s, e) => OnUpdateClick();
            Find<Button>("btnEditList").Click += (s, e) => OpenBlacklist();
            Find<Button>("btnOpenFolder").Click += (s, e) => Process.Start("explorer.exe", Gdpi.Root);
            Find<Button>("btnResetDns").Click += (s, e) => ResetDns();

            UpdateSteamOnlyHint();
            running = Gdpi.IsRunning();
            RenderState();

            win.Loaded += (s, e) => OnLoaded();

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

        private void OnLoaded()
        {
            Log("Siêu Nhân Hồng đã sẵn sàng biến hình!");
            if (screenshotPath != null)
            {
                var shot = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                shot.Tick += (s, e) => { shot.Stop(); SaveScreenshot(); };
                shot.Start();
            }

            Background(() =>
            {
                Gdpi.EnsureInstalled(Log);
                Ui(() => Find<TextBlock>("txtVerInstalled").Text = FormatVer(Gdpi.InstalledVersion()));
                Log(running ? "GoodbyeDPI đang chạy." : "GoodbyeDPI đang tắt. Bấm nút BIẾN HÌNH để bật.");
                CheckLatest(false);
            });
            RunChecks();
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
                Log(string.Format("Đang bật GoodbyeDPI ({0}, {1})...",
                    settings.SteamOnly ? "chỉ Steam" : "mọi trang web",
                    settings.StrongMode ? "chế độ mạnh" : "chế độ nhẹ"));
                bool ok = Gdpi.Start(settings, Log);
                Log(ok ? "BIẾN HÌNH THÀNH CÔNG! Steam đã được bảo vệ." : "Không bật được GoodbyeDPI. Xem nhật ký phía trên.");
                Ui(() =>
                {
                    running = Gdpi.IsRunning();
                    SetBusy(false, null);
                    RenderState();
                });
                if (ok) { Thread.Sleep(500); Ui(RunChecks); }
            });
        }

        private void StopPower()
        {
            SetBusy(true, "Đang tắt...");
            Background(() =>
            {
                Log("Đang tắt GoodbyeDPI...");
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
            settings.SteamOnly = Find<CheckBox>("chkSteamOnly").IsChecked == true;
            settings.AutoStart = Find<CheckBox>("chkAutoStart").IsChecked == true;
            settings.StrongMode = Find<CheckBox>("chkStrong").IsChecked == true;
            settings.Save();
            UpdateSteamOnlyHint();
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

        private void UpdateSteamOnlyHint()
        {
            Find<TextBlock>("txtSteamOnlyHint").Text = settings.SteamOnly
                ? "Bỏ chọn để áp dụng cho mọi trang web"
                : "Đang áp dụng cho mọi trang web";
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
            }
            foreach (var name in new[] { "chkSteamOnly", "chkAutoStart", "chkStrong", "btnUpdate" })
                ((UIElement)win.FindName(name)).IsEnabled = !b;
        }

        private void RenderState()
        {
            if (busy) return;
            var core = Find<Ellipse>("ellPowerCore");
            var ring = Find<Ellipse>("ellPowerRing");
            var icon = Find<TextBlock>("txtPowerIcon");
            string scope = settings.SteamOnly ? "Chỉ Steam" : "Mọi trang web";
            string mode = settings.StrongMode ? "chế độ mạnh" : "chế độ nhẹ";

            if (running)
            {
                core.Fill = (Brush)win.FindResource("PinkGrad");
                ring.Stroke = BrPink;
                icon.Foreground = Brushes.White;
                Find<TextBlock>("txtPowerLabel").Text = "ĐANG BẬT";
                Find<TextBlock>("txtStateTitle").Text = "Đang bảo vệ";
                Find<TextBlock>("txtStateDesc").Text = scope + " · " + mode + "\nBấm nút để tắt";
                Find<TextBlock>("txtStatusPill").Text = "Đang bảo vệ · " + scope;
                Find<Ellipse>("ledStatus").Fill = BrGreen;

                var glow = new DropShadowEffect { Color = (Color)ColorConverter.ConvertFromString("#EE2D9F"), ShadowDepth = 0, BlurRadius = 30, Opacity = 0.9 };
                core.Effect = glow;
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
                    new DoubleAnimation(22, 48, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                core.Fill = Hex("#2A1722");
                ring.Stroke = Hex("#4A2A3E");
                icon.Foreground = BrMuted;
                core.Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 12, Opacity = 0.6 };
                Find<TextBlock>("txtPowerLabel").Text = "BẬT";
                Find<TextBlock>("txtStateTitle").Text = "Đang tắt";
                Find<TextBlock>("txtStateDesc").Text = "Bấm nút để biến hình!\n" + scope + " · " + mode;
                Find<TextBlock>("txtStatusPill").Text = "Chưa bật";
                Find<Ellipse>("ledStatus").Fill = BrIdle;
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
                new[] { "Máy chủ cập nhật game", "client-update.steamstatic.com" }
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
                row.Status = new TextBlock { Text = "Chưa kiểm tra", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = BrMuted, VerticalAlignment = VerticalAlignment.Center };

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
                        row.Status.Text = ok ? "Thông suốt" : "Bị chặn";
                        row.Status.Foreground = ok ? BrGreen : BrRed;
                    });
                    if (!ok) Log(row.Name + ": bị chặn (" + detail + ")");
                    if (Interlocked.Decrement(ref remaining) == 0)
                    {
                        Log(passed == checks.Count
                            ? "Kiểm tra: tất cả máy chủ Steam đều thông suốt!"
                            : string.Format("Kiểm tra: {0}/{1} máy chủ thông suốt.{2}", passed, checks.Count,
                                running ? " Thử bật Chế độ mạnh." : " Hãy bấm BIẾN HÌNH để bật vượt chặn."));
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

        // ---------------- update ----------------
        private void CheckLatest(bool userAsked)
        {
            try
            {
                var rel = Gdpi.FetchLatest();
                latest = rel;
                string installed = Gdpi.InstalledVersion();
                bool newer = Gdpi.CompareVersions(rel.Tag, installed) > 0 && rel.ZipUrl != null;
                Ui(() =>
                {
                    Find<TextBlock>("txtVerLatest").Text = FormatVer(rel.Tag);
                    Find<TextBlock>("txtUpdateBtn").Text = newer ? "Cập nhật lên " + rel.Tag : "Kiểm tra cập nhật";
                });
                if (newer) Log("Có phiên bản GoodbyeDPI mới: " + rel.Tag + ". Bấm nút Cập nhật để cài.");
                else if (userAsked) Log("Bạn đang dùng bản GoodbyeDPI mới nhất (v" + installed + ").");
            }
            catch (Exception ex)
            {
                Ui(() => Find<TextBlock>("txtVerLatest").Text = "Không rõ");
                if (userAsked) Log("Không kiểm tra được bản mới: " + ex.Message);
            }
        }

        private void OnUpdateClick()
        {
            if (busy) return;
            bool hasNewer = latest != null && latest.ZipUrl != null &&
                            Gdpi.CompareVersions(latest.Tag, Gdpi.InstalledVersion()) > 0;
            if (!hasNewer)
            {
                Log("Đang kiểm tra phiên bản mới trên GitHub...");
                Find<Button>("btnUpdate").IsEnabled = false;
                Background(() =>
                {
                    CheckLatest(true);
                    Ui(() => Find<Button>("btnUpdate").IsEnabled = true);
                });
                return;
            }

            bool wasRunning = running;
            SetBusy(true, "Đang cập nhật...");
            Background(() =>
            {
                try
                {
                    Gdpi.InstallUpdate(latest, Log);
                    if (wasRunning)
                    {
                        Log("Đang bật lại GoodbyeDPI...");
                        Gdpi.Start(settings, Log);
                    }
                }
                catch (Exception ex)
                {
                    Log("Cập nhật thất bại: " + ex.Message);
                }
                Ui(() =>
                {
                    running = Gdpi.IsRunning();
                    Find<TextBlock>("txtVerInstalled").Text = FormatVer(Gdpi.InstalledVersion());
                    Find<TextBlock>("txtUpdateBtn").Text = "Kiểm tra cập nhật";
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

        private void OpenBlacklist()
        {
            try
            {
                Gdpi.EnsureInstalled(Log);
                Log("Đang mở danh sách tên miền. Lưu file xong, tắt rồi bật lại để áp dụng.");
                Process.Start("notepad.exe", "\"" + Gdpi.BlacklistPath + "\"");
            }
            catch (Exception ex)
            {
                Log("Không mở được danh sách: " + ex.Message);
            }
        }

        private void ResetDns()
        {
            Background(() =>
            {
                Log("Đang đưa DNS về tự động (DHCP)...");
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    try
                    {
                        var p = n.GetIPProperties();
                        if (!p.GatewayAddresses.Any(g => g.Address.ToString() != "0.0.0.0" && g.Address.ToString() != "::")) continue;
                        Gdpi.RunCapture("netsh.exe", string.Format("interface ipv4 set dnsservers name={0} source=dhcp", p.GetIPv4Properties().Index));
                        try { Gdpi.RunCapture("netsh.exe", string.Format("interface ipv6 set dnsservers name={0} source=dhcp", p.GetIPv6Properties().Index)); } catch { }
                        Log("   -> " + n.Name + ": đã về tự động.");
                    }
                    catch { }
                }
                try { Program.DnsFlushResolverCache(); } catch { }
                Log("Xong. DNS đã về mặc định của nhà mạng.");
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

        private void SaveScreenshot()
        {
            try
            {
                var root = (FrameworkElement)win.Content;
                var rtb = new RenderTargetBitmap((int)win.ActualWidth, (int)win.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                    dc.DrawRectangle(new VisualBrush(root), null, new Rect(new Point(10, 10), new Size(root.ActualWidth, root.ActualHeight)));
                rtb.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var f = File.Create(screenshotPath)) enc.Save(f);
            }
            catch { }
            win.Close();
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

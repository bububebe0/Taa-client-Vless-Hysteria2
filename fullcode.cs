internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    public static void Main()
    {
        _mutex = new Mutex(true, "TaaClient_Singleton_Mutex_CSharp", out bool isNew);
        if (!isNew) { BringToFront(); return; }

        SystemProxy.ClearStale();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => { SystemProxy.Set(false); KillSwitch.Set(false); };

        var app = new TaaApp();
        app.Run(new MainWindow());
    }

    public static void ReleaseMutex()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _mutex = null;
    }

    private static void BringToFront()
    {
        NativeInterop.EnumWindows((hWnd, _) =>
        {
            int len = NativeInterop.GetWindowTextLength(hWnd);
            if (len > 0)
            {
                var sb = new StringBuilder(len + 1);
                NativeInterop.GetWindowText(hWnd, sb, sb.Capacity);
                if (sb.ToString().Contains("Taa", StringComparison.OrdinalIgnoreCase))
                {
                    NativeInterop.ShowWindow(hWnd, 9);
                    NativeInterop.SetForegroundWindow(hWnd);
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
    }
}

internal class TaaApp : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyDarkTheme();
        DispatcherUnhandledException += (_, ex) =>
        {
            try { Paths.AppendLog(Paths.LogPath("error.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {ex.Exception}\n"); }
            catch { }
            ex.Handled = true;
        };
    }

internal static class NativeInterop
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc fn, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("wininet.dll")] public static extern bool InternetSetOptionW(IntPtr h, int opt, IntPtr buf, int len);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    public static void RefreshIE()
    {
        InternetSetOptionW(IntPtr.Zero, 37, IntPtr.Zero, 0);
        InternetSetOptionW(IntPtr.Zero, 39, IntPtr.Zero, 0);
    }
}

internal static class Paths
{
    public static string Base => AppDomain.CurrentDomain.BaseDirectory;
    public static string DataDir => Path.Combine(Base, "data");
    public static string ListDir => Path.Combine(Base, "list");
    public static string DbFile => Path.Combine(DataDir, "servers.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string LogFile => Path.Combine(Base, "proxy.log");
    public static string Resource(string n) => Path.Combine(Base, n);
    public static string LogPath(string n) => Path.Combine(Base, n);

    private const long MaxLogBytes = 8 * 1024 * 1024;

    public static void RotateLogFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length < MaxLogBytes) return;

            var old1 = path + ".old";
            var old2 = path + ".old2";
            if (File.Exists(old2)) File.Delete(old2);
            if (File.Exists(old1)) File.Move(old1, old2);
            File.Move(path, old1);
        }
        catch { }
    }

    public static void AppendLog(string path, string text)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length >= MaxLogBytes)
            {
                var old1 = path + ".old";
                var old2 = path + ".old2";
                if (File.Exists(old2)) File.Delete(old2);
                if (File.Exists(old1)) File.Move(old1, old2);
                File.Move(path, old1);
            }
            File.AppendAllText(path, text);
        }
        catch { }
    }
}

internal static class SystemProxy
{
    private const string Reg = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public static void Set(bool enable, int port = 1080)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Reg, true)!;
            k.SetValue("ProxyEnable", enable ? 1 : 0, RegistryValueKind.DWord);
            if (enable) k.SetValue("ProxyServer", $"127.0.0.1:{port}", RegistryValueKind.String);
            NativeInterop.RefreshIE();
        }
        catch { }
    }

    public static void ClearStale()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Reg)!;
            if ((int)(k.GetValue("ProxyEnable") ?? 0) == 1)
                if (((k.GetValue("ProxyServer") as string) ?? "").StartsWith("127.0.0.1:"))
                    Set(false);
        }
        catch { }
    }

}

internal static class KillSwitch
{
    private const string Rule = "TaaProxy_KillSwitch";
    public static void Set(bool enable)
    {
        try
        {
            if (enable)
                foreach (var p in new[] { "TCP", "UDP" })
                    Run($"advfirewall firewall add rule name=\"{Rule}\" dir=out action=block protocol={p} remoteaddress=any");
            else
                Run($"advfirewall firewall delete rule name=\"{Rule}\"");
        }
        catch { }
    }
    private static void Run(string args)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo("netsh", args) { CreateNoWindow = true, UseShellExecute = false };
        p.Start(); p.WaitForExit();
    }
}

internal static class Dpapi
{
    public static byte[] Encrypt(byte[] d) => ProtectedData.Protect(d, null, DataProtectionScope.CurrentUser);
    public static byte[] Decrypt(byte[] d) => ProtectedData.Unprotect(d, null, DataProtectionScope.CurrentUser);
}

internal static class FileAcl
{
    public static void SecureFile(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var domain = Environment.UserDomainName;
            var user   = Environment.UserName;
            var account = string.IsNullOrEmpty(domain) || domain == Environment.MachineName
                ? user
                : $"{domain}\\{user}";

            RunIcacls($"\"{path}\" /inheritance:r /grant:r \"{account}:F\"");
        }
        catch { }
    }

    private static void RunIcacls(string args)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo("icacls", args)
        {
            CreateNoWindow  = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };
        p.Start();
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(5000);
    }
}

internal static class Autostart
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(Key)!; return (k.GetValue("TaaClient") as string) == ExePath(); }
        catch { return false; }
    }
    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, true)!;
            if (on) k.SetValue("TaaClient", ExePath(), RegistryValueKind.String);
            else k.DeleteValue("TaaClient", false);
        }
        catch { }
    }
    private static string ExePath() => Process.GetCurrentProcess().MainModule?.FileName ?? "";
}

internal class ServerModel
{
    public string Type { get; set; } = "vless";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 443;
    public Dictionary<string, string> Params { get; set; } = new();
    public string Uuid { get; set; } = "";
    public string Password { get; set; } = "";
    public string Method { get; set; } = "";
    public string PrivateKey { get; set; } = "";
}

internal class AppException
{
    public string ExType { get; set; } = "path";
    public string Value { get; set; } = "";
    public string Name { get; set; } = "";
}

internal class AppSettings
{
    public bool SplitTunneling { get; set; } = false;
    public string Language { get; set; } = "ru";
    public string DefaultServer { get; set; } = "";
    public string DnsType { get; set; } = "system";
    public string DnsServer { get; set; } = "https://1.1.1.1/dns-query";
    public bool DnsThroughProxy { get; set; } = true;
    public List<AppException> AppExceptions { get; set; } = new();
    public List<string> DomainExceptions { get; set; } = new();
    public bool MinimizeOnClose { get; set; } = true;
    public bool DebugMode { get; set; } = false;
    public bool KillSwitch { get; set; } = false;
    public bool AutoReconnect { get; set; } = true;
    public string CurrentRoutesFile { get; set; } = "routes.txt";
    public double WindowLeft { get; set; } = -1;
    public double WindowTop { get; set; } = -1;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 700;
    public bool UseTunMode { get; set; } = false;
    public string HotkeyToggle { get; set; } = "";
    public string HotkeyRouting { get; set; } = "";
    public string HotkeyTun     { get; set; } = "";
    public string HotkeyExit    { get; set; } = "";
    public List<RouteListBinding> RouteListBindings { get; set; } = new();
    public bool EnableNotifications { get; set; } = false;
    public bool AutoConnectOnStart { get; set; } = false;
    public bool MinimizeOnStartup { get; set; } = false;
}

internal class RouteListBinding
{
    public string File    { get; set; } = "";
    public string Hotkey  { get; set; } = "";
}

internal static class Tr
{
    private static string _lang = "ru";
    public static void SetLang(string l) => _lang = l is "ru" or "en" ? l : "ru";
    public static string Get(string k) => (_all.TryGetValue(_lang, out var d) && d.TryGetValue(k, out var v)) ? v : k;

internal static class HotkeyManager
{
    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CTRL     = 0x0002;
    public const uint MOD_SHIFT    = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    public static string Build(bool ctrl, bool alt, bool shift, Key key)
    {
        if (IsModOnly(key)) return "";
        if (!ctrl && !alt && !shift)
        {
            return KeyToStr(key);
        }
        var p = new List<string>();
        if (ctrl)  p.Add("Ctrl");
        if (alt)   p.Add("Alt");
        if (shift) p.Add("Shift");
        p.Add(KeyToStr(key));
        return string.Join("+", p);
    }

    public static bool TryParse(string s, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrEmpty(s)) return false;
        var parts = s.Split('+');
        if (parts.Length == 1)
        {
            var ks = parts[0];
            if (ks.Length == 1 && char.IsDigit(ks[0])) ks = "D" + ks;
            try
            {
                var key = (Key)Enum.Parse(typeof(Key), ks, ignoreCase: true);
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                mods = MOD_NOREPEAT;
                return vk != 0;
            }
            catch { return false; }
        }
        else if (parts.Length >= 2)
        {
            foreach (var t in parts[..^1])
            {
                if (t == "Ctrl")       mods |= MOD_CTRL;
                else if (t == "Alt")   mods |= MOD_ALT;
                else if (t == "Shift") mods |= MOD_SHIFT;
            }
            mods |= MOD_NOREPEAT;
            var ks = parts[^1];
            if (ks.Length == 1 && char.IsDigit(ks[0])) ks = "D" + ks;
            try
            {
                var key = (Key)Enum.Parse(typeof(Key), ks, ignoreCase: true);
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                return vk != 0;
            }
            catch { return false; }
        }
        return false;
    }

    private static bool IsModOnly(Key k) => k is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or
        Key.System or Key.None or Key.Tab or Key.CapsLock;

    private static string KeyToStr(Key k) => k switch
    {
        Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
        Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
        _ => k.ToString()
    };
}


internal static class LinkParser
{
    private static readonly string[] Schemes = { "vless://", "hysteria2://", "ss://", "trojan://" };

    public static ServerModel? Parse(string link)
    {
        if (!Schemes.Any(s => link.StartsWith(s, StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            var uri = new Uri(link);
            var q = ParseQuery(uri.Query);
            var frag = uri.Fragment.TrimStart('#');
            var rawName = string.IsNullOrEmpty(frag) ? uri.Host : Uri.UnescapeDataString(frag);
            var name = rawName.Length > 120 ? rawName[..120] : rawName;
            var sv = new ServerModel
            {
                Type = uri.Scheme,
                Name = name,
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 443,
                Params = q
            };
            switch (uri.Scheme)
            {
                case "vless":
                    sv.Uuid = uri.UserInfo;
                    break;
                case "hysteria2":
                    sv.Password = Uri.UnescapeDataString(uri.UserInfo ?? "");
                    if (q.TryGetValue("security", out var sec) && sec.Equals("reality", StringComparison.OrdinalIgnoreCase))
                    {
                        sv.Type = "vless";
                        sv.Uuid = sv.Password;
                        sv.Password = "";
                    }
                    break;
                case "ss":
                    var ui = uri.UserInfo ?? "";
                    if (ui.Contains(':'))
                    {
                        var idx = ui.IndexOf(':');
                        sv.Method = Uri.UnescapeDataString(ui[..idx]);
                        sv.Password = Uri.UnescapeDataString(ui[(idx + 1)..]);
                    }
                    else
                    {
                        try
                        {
                            var dec = Encoding.UTF8.GetString(Convert.FromBase64String(
                                ui.PadRight(ui.Length + (4 - ui.Length % 4) % 4, '=')));
                            if (dec.Contains(':'))
                            { sv.Method = dec[..dec.IndexOf(':')]; sv.Password = dec[(dec.IndexOf(':') + 1)..]; }
                        }
                        catch { sv.Method = ui; }
                    }
                    if (sv.Method.StartsWith("2022-", StringComparison.OrdinalIgnoreCase)) return null;
                    break;
                case "trojan":
                    sv.Password = Uri.UnescapeDataString(uri.UserInfo ?? "");
                    if (string.IsNullOrEmpty(frag))
                        sv.Name = $"Trojan {uri.Host}:{sv.Port}";
                    break;
            }
            return sv;
        }
        catch { return null; }
    }

    public static List<ServerModel> ExtractAll(string text)
    {
        var pat = @"(vless://[^\s]+|hysteria2://[^\s]+|ss://[^\s]+|trojan://[^\s]+)";
        return Regex.Matches(text, pat).Select(m => Parse(m.Value)).OfType<ServerModel>().ToList();
    }

    public static string ToShareUri(ServerModel sv)
    {
        var q     = string.Join("&", sv.Params.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        var query = q.Length > 0 ? "?" + q : "";
        var frag  = "#" + Uri.EscapeDataString(sv.Name);
        return sv.Type switch
        {
            "vless"     => $"vless://{sv.Uuid}@{sv.Host}:{sv.Port}{query}{frag}",
            "trojan"    => $"trojan://{Uri.EscapeDataString(sv.Password)}@{sv.Host}:{sv.Port}{query}{frag}",
            "ss"        => $"ss://{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{sv.Method}:{sv.Password}"))}@{sv.Host}:{sv.Port}{frag}",
            "hysteria2" => $"hysteria2://{Uri.EscapeDataString(sv.Password)}@{sv.Host}:{sv.Port}{query}{frag}",
            _           => ""
        };
    }

    private static Dictionary<string, string> ParseQuery(string q)
    {
        var d = new Dictionary<string, string>();
        foreach (var part in q.TrimStart('?').Split('&'))
        {
            var i = part.IndexOf('=');
            if (i < 0) continue;
            d[Uri.UnescapeDataString(part[..i])] = Uri.UnescapeDataString(part[(i + 1)..]);
        }
        return d;
    }
}

internal static class SingBoxConfig
{
    public static void Generate(ServerModel sv, AppSettings settings,
        string routesContent, int port, string logFile, string configPath, out string? tunInterfaceName)
    {
        tunInterfaceName = null;
        var p = sv.Params;
        var rules = new List<object>();

        foreach (var app in settings.AppExceptions)
        {
            var r = new Dictionary<string, object> { ["outbound"] = "direct" };
            if (app.ExType == "path") r["process_path"] = app.Value;
            else r["process_name"] = app.Value;
            rules.Add(r);
        }

        if (settings.DomainExceptions.Count > 0)
        {
            var suf = settings.DomainExceptions.ToList();
            rules.Add(new Dictionary<string, object> { ["outbound"] = "direct", ["domain_suffix"] = suf });
        }

        var isSplit = settings.SplitTunneling;
        var finalOut = isSplit ? "direct" : "proxy";
        if (isSplit && !string.IsNullOrWhiteSpace(routesContent))
        {
            var domains = new List<string>();
            var ips = new List<string>();
            foreach (var raw in routesContent.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                if (IsIpOrCidr(raw)) ips.Add(raw.Contains('/') ? raw : raw + "/32");
                else domains.Add(raw);
            }
            var rule = new Dictionary<string, object> { ["outbound"] = "proxy" };
            if (domains.Count > 0) rule["domain_suffix"] = domains;
            if (ips.Count > 0) rule["ip_cidr"] = ips;
            if (rule.Count > 1) rules.Add(rule);
        }

        object[] inbounds;
        if (settings.UseTunMode)
        {
            tunInterfaceName = "taa-tun0";

            var excludeAddresses = new List<string>
            {
                "192.168.0.0/16",
                "10.0.0.0/8",
                "172.16.0.0/12",
                "127.0.0.0/8",
                "169.254.0.0/16"
            };

            var serverExcludeIp = ResolveServerIpForExclusion(sv.Host);
            if (!string.IsNullOrEmpty(serverExcludeIp))
                excludeAddresses.Insert(0, serverExcludeIp);

            inbounds = new object[]
            {
                new
                {
                    type = "tun",
                    interface_name = tunInterfaceName,
                    address = new[] { "172.19.0.1/30" },
                    mtu = 1400,
                    auto_route = true,
                    strict_route = false,
                    stack = "gvisor",
                    endpoint_independent_nat = true,
                    route_exclude_address = excludeAddresses.ToArray()
                }
            };

            var selfExe = System.IO.Path.GetFileName(
                Process.GetCurrentProcess().MainModule?.FileName ?? "TaaProxy.exe");
            rules.Insert(0, new Dictionary<string, object>
            {
                ["process_name"] = selfExe,
                ["outbound"]     = "direct"
            });

            rules.Insert(0, new Dictionary<string, object>
            {
                ["process_name"] = "svchost.exe",
                ["outbound"]     = "direct"
            });

            rules.Insert(0, new Dictionary<string, object>
            {
                ["outbound"]      = "direct",
                ["domain_suffix"] = new[] { "msftconnecttest.com", "msftncsi.com", "dns.msft.net" }
            });
        }
        else
        {
            inbounds = new object[]
            {
                new
                {
                    type = "mixed",
                    listen = "127.0.0.1",
                    listen_port = port
                }
            };
        }

        var actionRules = new List<object>();
        actionRules.Add(new { action = "sniff" });
        if (settings.UseTunMode)
            actionRules.Add(new Dictionary<string, object> { ["protocol"] = "dns", ["action"] = "hijack-dns" });
        actionRules.Add(new { action = "resolve", strategy = "prefer_ipv4" });
        rules.InsertRange(0, actionRules);

        var config = new Dictionary<string, object>
        {
            ["log"]      = new { level = settings.DebugMode ? "debug" : "info", output = logFile },
            ["inbounds"] = inbounds,
            ["route"]    = settings.UseTunMode
                ? (object)new { rules, final = finalOut, auto_detect_interface = false,
                                default_interface = GetDefaultPhysicalInterface() ?? "Ethernet" }
                : new { rules, final = finalOut, auto_detect_interface = true,
                        default_interface = (string?)null }
        };

        config["outbounds"] = new object[] { BuildOutbound(sv, p), new { type = "direct", tag = "direct" } };

        var dns = settings.UseTunMode ? BuildDns(settings, forceFallback: true) : BuildDns(settings, forceFallback: false);
        if (dns != null) config["dns"] = dns;

        Directory.CreateDirectory(Paths.DataDir);
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        FileAcl.SecureFile(configPath);
    }

    private static string ResolveSni(Dictionary<string, string> p, string fallback)
    {
        var sni = p.GetValueOrDefault("sni", "");
        if (string.IsNullOrEmpty(sni) || sni == "undefined" || sni == "null")
            return fallback;
        return sni;
    }

    private static object BuildOutbound(ServerModel sv, Dictionary<string, string> p)
    {
        return sv.Type switch
        {
            "hysteria2" => (object)new
            {
                type = "hysteria2",
                tag = "proxy",
                server = sv.Host,
                server_port = sv.Port,
                password = sv.Password,
                tls = new
                {
                    enabled = true,
                    server_name = ResolveSni(p, sv.Host),
                    insecure = p.GetValueOrDefault("insecure", "0") == "1"
                }
            },
            "ss" => (object)new
            {
                type = "shadowsocks",
                tag = "proxy",
                server = sv.Host,
                server_port = sv.Port,
                method = sv.Method,
                password = sv.Password
            },
            "trojan" => BuildTrojan(sv, p),
            _ => BuildVless(sv, p)
        };
    }

    private static object BuildVless(ServerModel sv, Dictionary<string, string> p)
    {
        string serverName = ResolveSni(p, sv.Host);
        bool isReality = p.GetValueOrDefault("security", "") == "reality";
        bool sniIsIp = System.Net.IPAddress.TryParse(serverName, out _);

        var tls = new Dictionary<string, object>
        {
            ["enabled"] = true,
            ["server_name"] = serverName,
            ["utls"] = new { enabled = true, fingerprint = p.GetValueOrDefault("fp", "chrome") }
        };

        if (isReality && sniIsIp)
            tls["insecure"] = true;

        if (isReality)
            tls["reality"] = new
            {
                enabled = true,
                public_key = p.GetValueOrDefault("pbk", ""),
                short_id = p.GetValueOrDefault("sid", "")
            };

        var outbound = new Dictionary<string, object>
        {
            ["type"] = "vless",
            ["tag"] = "proxy",
            ["server"] = sv.Host,
            ["server_port"] = sv.Port,
            ["uuid"] = sv.Uuid,
            ["packet_encoding"] = "xudp",
            ["tls"] = tls
        };

        var flow = p.GetValueOrDefault("flow", "");
        if (string.IsNullOrEmpty(flow) && isReality)
        {
            flow = "xtls-rprx-vision";
        }
        if (!string.IsNullOrEmpty(flow)) outbound["flow"] = flow;

        var transport = BuildTransport(p);
        if (transport != null) outbound["transport"] = transport;

        return outbound;
    }

    private static object? BuildTransport(Dictionary<string, string> p)
    {
        var netType = p.GetValueOrDefault("type", "tcp");
        return netType switch
        {
            "ws" => (object)new Dictionary<string, object>
            {
                ["type"] = "ws",
                ["path"] = p.GetValueOrDefault("path", "/"),
                ["headers"] = new Dictionary<string, string>
                {
                    ["Host"] = p.GetValueOrDefault("host", p.GetValueOrDefault("sni", ""))
                }
            },
            "grpc" => (object)new Dictionary<string, object>
            {
                ["type"] = "grpc",
                ["service_name"] = p.GetValueOrDefault("serviceName", p.GetValueOrDefault("authority", ""))
            },
            "h2" => (object)new Dictionary<string, object>
            {
                ["type"] = "http",
                ["host"] = new[] { p.GetValueOrDefault("host", p.GetValueOrDefault("sni", "")) },
                ["path"] = p.GetValueOrDefault("path", "/")
            },
            "httpupgrade" => (object)new Dictionary<string, object>
            {
                ["type"] = "httpupgrade",
                ["path"] = p.GetValueOrDefault("path", "/"),
                ["host"] = p.GetValueOrDefault("host", p.GetValueOrDefault("sni", ""))
            },
            _ => null
        };
    }

    private static object BuildTrojan(ServerModel sv, Dictionary<string, string> p)
    {
        string serverName = ResolveSni(p, sv.Host);
        bool isReality = p.GetValueOrDefault("security", "") == "reality";
        bool sniIsIp = System.Net.IPAddress.TryParse(serverName, out _);

        var tls = new Dictionary<string, object>
        {
            ["enabled"] = true,
            ["server_name"] = serverName,
            ["utls"] = new { enabled = true, fingerprint = p.GetValueOrDefault("fp", "chrome") }
        };

        if (isReality && sniIsIp)
            tls["insecure"] = true;

        if (isReality)
            tls["reality"] = new
            {
                enabled = true,
                public_key = p.GetValueOrDefault("pbk", ""),
                short_id = p.GetValueOrDefault("sid", "")
            };

        var outbound = new Dictionary<string, object>
        {
            ["type"] = "trojan",
            ["tag"] = "proxy",
            ["server"] = sv.Host,
            ["server_port"] = sv.Port,
            ["password"] = sv.Password,
            ["tls"] = tls
        };

        var transport = BuildTransport(p);
        if (transport != null) outbound["transport"] = transport;

        return outbound;
    }

    private static object? BuildDns(AppSettings s, bool forceFallback = false)
    {
        if (s.DnsType == "system")
        {
            if (!forceFallback) return null;
            var fallbackSrv = new Dictionary<string, object>
            {
                ["tag"]    = "tun_dns",
                ["type"]   = "udp",
                ["server"] = "1.1.1.1",
                ["detour"] = "proxy"
            };
            return new { servers = new[] { fallbackSrv }, final = "tun_dns" };
        }

        var addr = s.DnsServer;
        var srvDict = new Dictionary<string, object>();
        srvDict["tag"] = "custom_dns";

        if (s.DnsType == "doh")
        {
            if (!addr.StartsWith("https://")) addr = "https://" + addr;
            if (Uri.TryCreate(addr, UriKind.Absolute, out var uri))
            {
                srvDict["type"]   = "https";
                srvDict["server"] = uri.Host;
                srvDict["path"]   = string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/"
                                        ? "/dns-query"
                                        : uri.AbsolutePath;
            }
            else
            {
                srvDict["type"]   = "https";
                srvDict["server"] = addr.Replace("https://", "").Split('/')[0];
                srvDict["path"]   = "/dns-query";
            }
        }
        else if (s.DnsType == "dot")
        {
            var host = addr.Replace("tls://", "").Split(':')[0];
            srvDict["type"]   = "tls";
            srvDict["server"] = host;
        }
        else
        {
            srvDict["type"]   = "udp";
            srvDict["server"] = addr.Split(':')[0];
        }

        if (s.DnsThroughProxy)
            srvDict["detour"] = "proxy";

        return new { servers = new[] { srvDict }, final = "custom_dns" };
    }

    private static bool IsIpOrCidr(string s)
    {
        if (s.Contains('/')) s = s[..s.IndexOf('/')];
        return System.Net.IPAddress.TryParse(s, out _);
    }

    internal static string? GetDefaultPhysicalInterface()
    {
        static bool IsVirtual(string description, string name)
        {
            var tokens = new[]
            {
                "tun", "tap", "virtual", "pseudo", "wintun", "wfp",
                "loopback", "teredo", "isatap", "6to4", "miniport",
                "hyper-v", "vmware", "virtualbox", "docker", "npcap",
                "taa-tun"
            };
            var d = description.ToLowerInvariant();
            var n = name.ToLowerInvariant();
            return tokens.Any(t => d.Contains(t) || n.Contains(t));
        }

        try
        {
            int bestWmiIndex = -1;
            int bestWmiMetric = int.MaxValue;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT InterfaceIndex, Metric1 FROM Win32_IP4RouteTable WHERE Destination='0.0.0.0'");
                foreach (ManagementObject row in searcher.Get())
                {
                    int m = Convert.ToInt32(row["Metric1"]);
                    if (m < bestWmiMetric)
                    {
                        bestWmiMetric = m;
                        bestWmiIndex = Convert.ToInt32(row["InterfaceIndex"]);
                    }
                }
            }
            catch { }

            if (bestWmiIndex >= 0)
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    if (IsVirtual(ni.Description, ni.Name)) continue;
                    try
                    {
                        var ipv4 = ni.GetIPProperties().GetIPv4Properties();
                        if (ipv4 != null && ipv4.Index == bestWmiIndex)
                            return ni.Name;
                    }
                    catch { }
                }
            }

            string? bestName   = null;
            int     bestMetric = int.MaxValue;

            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                if (IsVirtual(ni.Description, ni.Name)) continue;

                var props = ni.GetIPProperties();
                var gateways = props.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .ToList();
                if (gateways.Count == 0) continue;

                int metric = int.MaxValue;
                try
                {
                    var ipv4 = props.GetIPv4Properties();
                    if (ipv4 != null) metric = ipv4.Index;
                }
                catch { }

                if (metric < bestMetric)
                {
                    bestMetric = metric;
                    bestName   = ni.Name;
                }
            }

            return bestName;
        }
        catch { }
        return null;
    }

    internal static string? ResolveServerIpForExclusion(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        try
        {
            if (System.Net.IPAddress.TryParse(host, out var addr)
                && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return addr.ToString() + "/32";

            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
            var resolveTask = System.Net.Dns.GetHostAddressesAsync(host);
            if (resolveTask.Wait(3000))
            {
                var ipv4 = resolveTask.Result
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ipv4 != null) return ipv4.ToString() + "/32";
            }
        }
        catch { }
        return null;
    }
}

internal class MainWindow : Window
{
    private static readonly Color C_BG      = ParseColor("#07090F");
    private static readonly Color C_SIDEBAR = ParseColor("#0B1020");
    private static readonly Color C_CARD    = ParseColor("#0D1526");
    private static readonly Color C_BORDER  = ParseColor("#1A2640");
    private static readonly Color C_ACCENT  = ParseColor("#6366F1");
    private static readonly Color C_HOVER   = ParseColor("#5254CC");
    private static readonly Color C_SUCCESS = ParseColor("#10B981");
    private static readonly Color C_CON_BRD = ParseColor("#0D4A32");
    private static readonly Color C_DANGER  = ParseColor("#EF4444");
    private static readonly Color C_DANG_H  = ParseColor("#DC2626");
    private static readonly Color C_TXT     = ParseColor("#FFFFFF");
    private static readonly Color C_MUTED   = ParseColor("#6B7A99");
    private static readonly Color C_ACTIVE  = ParseColor("#1A2347");

    private static SolidColorBrush Br(Color c) => new(c);
    private static Color ParseColor(string h) => (Color)ColorConverter.ConvertFromString(h);

    private List<ServerModel> _servers = new();
    private AppSettings _settings = new();
    private Process? _proxyProcess;
    private int _selectedIdx = -1;
    private int _connectedIdx = -1;
    private int _proxyPort;
    private bool _hideIp = true;
    private bool _noNetwork = false;
    private int _autoReconnectAttempts = 0;
    private CancellationTokenSource _monitorCts = new();
    private CancellationTokenSource _reconnectCts = new();

    private StackPanel _serverListPanel = null!;
    private TextBlock _nameLabel = null!;
    private TextBlock _serverTitleLabel = null!;
    private TextBlock _titleConnectedLabel = null!;
    private TextBlock _hostLabel = null!;
    private TextBlock _statusLabel = null!;
    private Button _connectBtn = null!;
    private Button _pingBtn = null!;
    private Button _defaultBtn = null!;
    private Button _deleteBtn = null!;
    private TextBlock _pingLabel = null!;
    private Border _serverInfoCard = null!;
    private ComboBox _routesCombo = null!;
    private ToggleButton _splitSwitch = null!;
    private StackPanel _serversPanel = null!;
    private ScrollViewer _serversScrollViewer = null!;
    private Grid _settingsPanel = null!;
    private Grid _importPanel = null!;
    private Grid _proxyListPanel = null!;
    private string _currentPanel = "servers";
    private string _currentRoutesFile = "routes.txt";
    private bool _suppressComboEvents = false;

    private ToggleButton? _autostartChk, _minimizeChk, _debugChk, _killSwitchChk, _autoReconnectChk, _tunModeChk, _notificationsChk, _autoConnectOnStartChk, _minimizeOnStartupChk;
    private ComboBox? _langCombo;

    private ComboBox? _dnsTypeCombo;
    private TextBox? _dnsAddrBox;
    private ToggleButton? _dnsProxyChk;
    private TextBlock? _dnsTestLabel;

    private StackPanel? _appExcList, _domExcList;
    private int _selAppExcIdx = -1;
    private Button? _removeAppBtn, _removeDomBtn;
    private string? _selectedDomainException = null;

    private ComboBox? _proxyRoutesCombo;
    private TextBox? _proxyRoutingText;
    private Button? _proxySaveBtn;
    private Button? _proxyNewBtn;
    private Button? _proxyDeleteBtn;
    private Button? _proxyRenameBtn;

    private DispatcherTimer? _dotTimer;
    private int _dotPhase = 0;
    private Color _statusColor = ParseColor("#9CA3AF");

    private DispatcherTimer? _pingDotsTimer;
    private int _pingDotPhase = 0;

    private DispatcherTimer? _speedTimer;
    private TextBlock _speedLabel = null!;
    private Border _speedRow = null!;
    private TextBlock _headerSpeedLabel = null!;
    private long _lastRxBytes = 0;
    private long _lastTxBytes = 0;
    private DateTime _lastSpeedTick = DateTime.MinValue;

    private DispatcherTimer? _boundsTimer;

    private Forms.NotifyIcon? _tray;

    private Button _titleMaxBtn = null!;
    private Ellipse? _titleStatusDot;

    private static readonly IdnMapping _idn = new IdnMapping();

    private bool _editingName = false;
    private TextBox? _editNameBox = null;

    private HwndSource?  _hwndSource;
    private const int    WM_HOTKEY      = 0x0312;
    private const int    HK_TOGGLE      = 1001;
    private const int    HK_ROUTING     = 1002;
    private const int    HK_TUN         = 1003;
    private const int    HK_EXIT        = 1004;
    private const int    HK_ROUTE_LIST_BASE = 2000;
    private const int    HK_ROUTE_LIST_MAX  = 10;
    private Button?      _hkListeningBtn;
    private string?      _hkListeningProp;
    private readonly Dictionary<string, Button> _hkButtons = new();
    private bool _dataLoaded = false;

    private ToggleButton? _exportServersChk;
    private ToggleButton? _exportRoutesChk;

    private TextBlock?   _updateStatusTb;
    private Button?      _checkUpdateBtn;
    private Button?      _downloadUpdateBtn;
    private ProgressBar? _updateProgressBar;
    private Border?      _updateProgressRow;
    private string?      _latestDownloadUrl;
    private const string GhApiLatest = "https://api.github.com/repos/bububebe0/TaaProxy/releases/latest";
    private const string AppVersion  = "2.6.0";

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void RestartAsAdmin()
    {
        var exe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exe)) return;

        SaveSettings();
        StopProxy();
        UnregisterAllHotkeys();
        _hwndSource?.RemoveHook(HwndHook);
        Program.ReleaseMutex();

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _hwndSource?.AddHook(HwndHook);
            RegisterAllHotkeys();
            MessageBox.Show($"Не удалось перезапустить с правами администратора: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            Application.Current.Shutdown();
        }), DispatcherPriority.Background);
    }

    public MainWindow()
    {
        _proxyPort = new Random().Next(10000, 60000);

        LoadSettings();
        Tr.SetLang(_settings.Language);

        Title = Tr.Get("title");
        Width = _settings.WindowWidth > 0 ? _settings.WindowWidth : 1200;
        Height = _settings.WindowHeight > 0 ? _settings.WindowHeight : 700;
        if (_settings.WindowLeft >= 0) Left = _settings.WindowLeft;
        if (_settings.WindowTop >= 0) Top = _settings.WindowTop;
        MinWidth = 976; MinHeight = 620;
        Background = Br(C_BG);
        WindowStyle = WindowStyle.None;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 31,
            ResizeBorderThickness = new Thickness(5),
            UseAeroCaptionButtons = false,
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0)
        });

        StateChanged += (_, _) =>
        {
            if (_titleMaxBtn != null)
                _titleMaxBtn.Content = WindowState == WindowState.Maximized ? "▢" : "▢";
        };

        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Paths.Resource("pic\\ico.ico"))); }
        catch { }

        BuildUI();
        Loaded += (_, _) =>
        {
            LoadData();
            _dataLoaded = true;
            InitTray();
            CenterIfDefault();
            RegisterAllHotkeys();
            SetWindowIcon(false);

            if (_settings.UseTunMode && !IsAdministrator())
            {
                var result = MessageBox.Show(
                    "TUN-режим требует прав администратора.\nПерезапустить приложение с правами администратора?",
                    "TUN Mode",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    RestartAsAdmin();
                }
                else
                {
                    _settings.UseTunMode = false;
                    SaveSettings();
                    if (_tunModeChk != null) _tunModeChk.IsChecked = false;
                }
            }

            if (_settings.AutoConnectOnStart && !string.IsNullOrEmpty(_settings.DefaultServer))
            {
                SelectDefaultServer();
                if (_selectedIdx >= 0)
                    ToggleConnection();
            }

            if (_settings.MinimizeOnStartup)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Hide();
                    if (_tray != null)
                        _tray.ShowBalloonTip(1500, "Taa Proxy", "Приложение запущено в трее", Forms.ToolTipIcon.Info);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        };
        Closing += OnClosing;
        SizeChanged    += (_, _) => DebounceSaveWindowBounds();
        LocationChanged += (_, _) => DebounceSaveWindowBounds();

        this.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnWindowPreviewKeyDown), true);

        Directory.CreateDirectory(Paths.DataDir);
        Directory.CreateDirectory(Paths.ListDir);

        AllowDrop = true;
        DragEnter += OnWindowDragEnter;
        Drop     += OnWindowDrop;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this);
        _hwndSource?.AddHook(HwndHook);
        if (_dataLoaded) RegisterAllHotkeys();
    }

    private void SetWindowIcon(bool connected)
    {
        try
        {
            var icoName = connected ? "icoon.ico" : "ico.ico";
            var icoPath = Paths.Resource($"pic\\{icoName}");
            if (File.Exists(icoPath))
            {
                var icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(icoPath));
                Icon = icon;
                InvalidateVisual();
                UpdateLayout();
            }
        }
        catch { }
    }

    private void BuildUI()
    {
        var dock = new DockPanel();

        var titleBar = BuildTitleBar();
        DockPanel.SetDock(titleBar, Dock.Top);
        dock.Children.Add(titleBar);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(308) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        var mainArea = BuildMainArea();
        Grid.SetColumn(mainArea, 1);
        root.Children.Add(mainArea);

        dock.Children.Add(root);
        Content = dock;
    }

    private Border BuildTitleBar()
    {
        var bar = new Border
        {
            Background = Br(ParseColor("#05070E")),
            Height = 34,
            BorderBrush = Br(C_BORDER),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };

        var titleTxt = new TextBlock
        {
            Text = "Taa Proxy",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Br(C_TXT),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        left.Children.Add(titleTxt);

        _titleStatusDot = new Ellipse
        {
            Width = 7, Height = 7,
            Fill = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0)
        };
        WindowChrome.SetIsHitTestVisibleInChrome(_titleStatusDot, false);
        left.Children.Add(_titleStatusDot);

        _titleConnectedLabel = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 1, 0, 0)
        };
        WindowChrome.SetIsHitTestVisibleInChrome(_titleConnectedLabel, false);
        left.Children.Add(_titleConnectedLabel);

        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var minBtn  = MakeTitleBarBtn("─", false, () => WindowState = WindowState.Minimized);
        _titleMaxBtn = MakeTitleBarBtn("▢", false,
            () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        var closeBtn = MakeTitleBarBtn("✕", true,
            () => { if (_settings.MinimizeOnClose) Hide(); else CleanupAndExit(); });

        foreach (var b in new[] { minBtn, _titleMaxBtn, closeBtn })
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);

        right.Children.Add(minBtn);
        right.Children.Add(_titleMaxBtn);
        right.Children.Add(closeBtn);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        bar.Child = grid;
        return bar;
    }

    private static readonly string _titleBtnXaml = @"
<ControlTemplate TargetType='Button' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Border x:Name='bd' Background='{TemplateBinding Background}'>
    <TextBlock Text='{TemplateBinding Content}' HorizontalAlignment='Center' VerticalAlignment='Center'
               Foreground='{TemplateBinding Foreground}' FontSize='12' FontWeight='Normal'/>
  </Border>
</ControlTemplate>";

    private static ControlTemplate? _titleBtnTemplate;
    private static ControlTemplate GetTitleBtnTemplate()
    {
        _titleBtnTemplate ??= (ControlTemplate)XamlReader.Parse(_titleBtnXaml);
        return _titleBtnTemplate;
    }

    private Button MakeTitleBarBtn(string symbol, bool isClose, Action click)
    {
        var hoverBg = isClose ? C_DANGER : ParseColor("#131D35");
        var btn = new Button
        {
            Content = symbol,
            Width = 42,
            Height = 34,
            Background = Brushes.Transparent,
            Foreground = Br(C_MUTED),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = GetTitleBtnTemplate(),
            FontSize = 13
        };
        btn.MouseEnter += (_, _) =>
        {
            btn.Background = Br(hoverBg);
            btn.Foreground = Brushes.White;
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = Br(C_MUTED);
        };
        btn.Click += (_, _) => click();
        return btn;
    }

    private Border BuildSidebar()
    {
        var bg = new Border
        {
            Background = Br(C_SIDEBAR),
            BorderBrush = Br(C_BORDER),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _serverListPanel = new StackPanel { Margin = new Thickness(10, 0, 10, 8) };
        scroll.Content = _serverListPanel;

        var dockPanel = new DockPanel { LastChildFill = true };
        bg.Child = dockPanel;

        var bottomSp = new StackPanel
        {
            Margin = new Thickness(10, 0, 10, 14),
            Background = Brushes.Transparent
        };
        DockPanel.SetDock(bottomSp, Dock.Bottom);

        bottomSp.Children.Add(new Border
        {
            Height = 1,
            Background = Br(C_BORDER),
            Margin = new Thickness(0, 0, 0, 10)
        });

        bottomSp.Children.Add(MakeSidebarFooterBtn(
            Tr.Get("btn_exit"), "➜]", C_DANGER,
            () => CleanupAndExit()));

        dockPanel.Children.Add(bottomSp);

        var topSp = new StackPanel();
        DockPanel.SetDock(topSp, Dock.Top);

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 20, 16, 20),
            VerticalAlignment = VerticalAlignment.Center
        };
        brand.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Taa Proxy", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Br(C_TXT) },
                new TextBlock { Text = "VLESS · Hysteria2 · Trojan", FontSize = 9, Foreground = Br(C_MUTED), Margin = new Thickness(0, 1, 0, 0) }
            }
        });
        topSp.Children.Add(brand);

        topSp.Children.Add(MakeBtn(Tr.Get("add_from_clipboard"), C_ACCENT, C_TXT,
            h: 42, margin: new Thickness(10, 0, 10, 6),
            click: AddFromClipboard, radius: 10, bold: false, fontSize: 16, hPad: 9, w: 240));

        topSp.Children.Add(MakeSidebarNavBtn(Tr.Get("import_configs"), "", () => ShowPanel("import")));
        topSp.Children.Add(MakeSidebarNavBtn(Tr.Get("proxy_list"),     "", () => ShowPanel("proxylist")));
        topSp.Children.Add(MakeSidebarNavBtn(Tr.Get("settings"),       "", () => ShowPanel("settings")));

        var serverHeader = new Border { Margin = new Thickness(14, 14, 10, 6) };
        var serverLbl = new TextBlock
        {
            Text = Tr.Get("servers").ToUpperInvariant(),
            FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center
        };
        serverHeader.Child = serverLbl;
        topSp.Children.Add(serverHeader);

        dockPanel.Children.Add(topSp);
        dockPanel.Children.Add(scroll);
        return bg;
    }

    private Button MakeSidebarNavBtn(string text, string icon, Action click)
    {
        var mutedColor = C_MUTED;
        var txtColor   = C_TXT;

        var iconTb = new TextBlock
        {
            Text = icon,
            FontSize = 24,
            Foreground = Br(mutedColor),
            Width = 26,
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var labelTb = new TextBlock
        {
            Text = text,
            FontSize = 15,
            Foreground = Br(mutedColor),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.Normal
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(iconTb);
        row.Children.Add(labelTb);

        var inner = new Border
        {
            Child = row,
            Padding = new Thickness(10, 9, 10, 9),
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        var btn = new Button
        {
            Content = inner,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 10, 2),
            Cursor = Cursors.Hand,
            Template = GetBtnTemplate(),
            Tag = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        DispatcherTimer? animTimer = null;
        double animProgress = 0;
        bool hovering = false;

        void Animate()
        {
            animTimer?.Stop();
            animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            animTimer.Tick += (_, _) =>
            {
                animProgress = hovering
                    ? Math.Min(1, animProgress + 0.12)
                    : Math.Max(0, animProgress - 0.12);

                var fg = LerpColor(mutedColor, txtColor, animProgress);
                iconTb.Foreground  = Br(fg);
                labelTb.Foreground = Br(fg);

                if ((hovering && animProgress >= 1) || (!hovering && animProgress <= 0))
                    animTimer!.Stop();
            };
            animTimer.Start();
        }

        inner.MouseEnter += (_, _) => { hovering = true;  Animate(); };
        inner.MouseLeave += (_, _) => { hovering = false; Animate(); };
        btn.Click += (_, _) => click();
        return btn;
    }

    private Border MakeSidebarFooterBtn(string text, string icon, Color fgColor, Action click)
    {
        var iconTb = new TextBlock
        {
            Text = icon, FontSize = 13,
            Foreground = Br(C_MUTED),
            Width = 20, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var labelTb = new TextBlock
        {
            Text = text, FontSize = 15,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(iconTb); row.Children.Add(labelTb);
        var border = new Border
        {
            Child = row,
            Padding = new Thickness(9, 8, 9, 8),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };

        DispatcherTimer? animTimer = null;
        double animProgress = 0;
        bool hovering = false;

        void Animate()
        {
            animTimer?.Stop();
            animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            animTimer.Tick += (_, _) =>
            {
                animProgress = hovering
                    ? Math.Min(1, animProgress + 0.12)
                    : Math.Max(0, animProgress - 0.12);
                var fg = LerpColor(C_MUTED, fgColor, animProgress);
                iconTb.Foreground  = Br(fg);
                labelTb.Foreground = Br(fg);
                if ((hovering && animProgress >= 1) || (!hovering && animProgress <= 0))
                    animTimer!.Stop();
            };
            animTimer.Start();
        }

        border.MouseEnter += (_, _) => { hovering = true;  Animate(); };
        border.MouseLeave += (_, _) => { hovering = false; Animate(); };
        border.MouseLeftButtonUp += (_, _) => click();
        return border;
    }

    private Grid BuildMainArea()
    {
        var g = new Grid { Margin = new Thickness(28, 24, 28, 24) };

        _serversPanel = BuildServersPanel();
        _serversScrollViewer = new ScrollViewer
        {
            Content = _serversPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _settingsPanel = BuildSettingsPanel();
        _settingsPanel.Visibility = Visibility.Collapsed;
        _importPanel = BuildImportPanel();
        _importPanel.Visibility = Visibility.Collapsed;
        _proxyListPanel = BuildProxyListPanel();
        _proxyListPanel.Visibility = Visibility.Collapsed;

        g.Children.Add(_serversScrollViewer);
        g.Children.Add(_settingsPanel);
        g.Children.Add(_importPanel);
        g.Children.Add(_proxyListPanel);

        return g;
    }
        var toggle = new ToggleButton
        {
            IsChecked = isChecked,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0),
            Content = text,
            Foreground = Br(C_TXT),
            Template = (ControlTemplate)XamlReader.Parse(templateXaml)
        };

        void ApplyToggleState(bool on, bool animate)
        {
            if (toggle.Template.FindName("Thumb", toggle) is not Border thumb) return;
            var translate      = thumb.RenderTransform as TranslateTransform;
            var trackBrush     = toggle.Template.FindName("TrackBrush",       toggle) as SolidColorBrush;
            var trackBorder    = toggle.Template.FindName("TrackBorderBrush", toggle) as SolidColorBrush;
            var thumbBrush     = toggle.Template.FindName("ThumbBrush",       toggle) as SolidColorBrush;
            if (translate == null) return;

            double targetX          = on ? 18.0 : 0.0;
            var    targetTrackColor  = on ? Color.FromRgb(0x63, 0x66, 0xF1) : Color.FromRgb(0x16, 0x20, 0x40);
            var    targetBorderColor = on ? Color.FromRgb(0x63, 0x66, 0xF1) : Color.FromRgb(0x1A, 0x26, 0x40);
            var    targetThumbColor  = on ? Colors.White                     : Color.FromRgb(0x3A, 0x4A, 0x6A);

            if (animate)
            {
                var dur  = new Duration(TimeSpan.FromMilliseconds(200));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                translate.BeginAnimation(
                    TranslateTransform.XProperty,
                    new DoubleAnimation(targetX, dur) { EasingFunction = ease });

                trackBrush? .BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(targetTrackColor,  dur));
                trackBorder?.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(targetBorderColor, dur));
                thumbBrush? .BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(targetThumbColor,  dur));
            }
            else
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.X = targetX;

                if (trackBrush  != null) { trackBrush.BeginAnimation(SolidColorBrush.ColorProperty,  null); trackBrush.Color  = targetTrackColor;  }
                if (trackBorder != null) { trackBorder.BeginAnimation(SolidColorBrush.ColorProperty, null); trackBorder.Color = targetBorderColor; }
                if (thumbBrush  != null) { thumbBrush.BeginAnimation(SolidColorBrush.ColorProperty,  null); thumbBrush.Color  = targetThumbColor;  }
            }
        }

        toggle.Loaded   += (_, _) => ApplyToggleState(toggle.IsChecked == true, animate: false);
        toggle.Checked  += (_, _) => { ApplyToggleState(true,  animate: true); onChanged(true);  };
        toggle.Unchecked+= (_, _) => { ApplyToggleState(false, animate: true); onChanged(false); };
        return toggle;
    }

    private StackPanel BuildServersPanel()
    {
        var sp = new StackPanel();

        _serverTitleLabel = MakeText(Tr.Get("server_not_selected"), 17, true, C_TXT);
        _hostLabel = MakeText("—", 13, false, C_MUTED);
        _pingLabel = MakeText("", 13, true, C_TXT);

        _statusLabel = new TextBlock
        {
            Text = Tr.Get("status_disconnected"),
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusPill = new Border
        {
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding         = new Thickness(0)
        };
        var statusRow2 = new StackPanel { Orientation = Orientation.Horizontal };
        statusRow2.Children.Add(_statusLabel);
        statusPill.Child = statusRow2;

        _headerSpeedLabel = new TextBlock
        {
            Text = "↓ —   ↑ —",
            FontSize = 13,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            FontWeight = FontWeights.Normal,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center
        };

        var downArrow = new TextBlock
        {
            Text = "↓",
            FontSize = 11,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 3, 0)
        };
        var _headerDownLabel = new TextBlock
        {
            Text = "—",
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 52
        };
        var upArrow = new TextBlock
        {
            Text = "↑",
            FontSize = 11,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 3, 0)
        };
        var _headerUpLabel = new TextBlock
        {
            Text = "—",
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 52
        };

        var speedStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        speedStack.Children.Add(downArrow);
        speedStack.Children.Add(_headerDownLabel);
        speedStack.Children.Add(upArrow);
        speedStack.Children.Add(_headerUpLabel);

        var speedBadge = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Child = speedStack,
            VerticalAlignment = VerticalAlignment.Center
        };

        _headerSpeedLabel.Tag = (_headerDownLabel, _headerUpLabel, downArrow, upArrow);

        var headerRow = new Grid { Margin = new Thickness(23, 0, 23, 20) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(speedBadge, 0);
        headerRow.Children.Add(speedBadge);
        Grid.SetColumn(statusPill, 1);
        headerRow.Children.Add(statusPill);
        sp.Children.Add(headerRow);

        var nameRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameKeyTb = MakeText(Tr.Get("name"), 12, false, C_MUTED, margin: new Thickness(0, 0, 12, 0));
        Grid.SetColumn(nameKeyTb, 0); nameRow.Children.Add(nameKeyTb);
        var nameContainer = new Border { Child = _nameLabel = MakeText("—", 13, false, C_TXT) };
        _nameLabel.MouseLeftButtonDown += (s, e) => StartEditName();
        _nameLabel.Cursor = Cursors.Hand;
        Grid.SetColumn(nameContainer, 1); nameRow.Children.Add(nameContainer);
        var editNameBtn = MakeSmallTextBtn(Tr.Get("edit_name"), C_MUTED, click: StartEditName);
        editNameBtn.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(editNameBtn, 2); nameRow.Children.Add(editNameBtn);

        var hostRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        hostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var addrKeyTb = MakeText(Tr.Get("address"), 12, false, C_MUTED, margin: new Thickness(0, 0, 12, 0));
        Grid.SetColumn(addrKeyTb, 0); hostRow.Children.Add(addrKeyTb);
        Grid.SetColumn(_hostLabel, 1); hostRow.Children.Add(_hostLabel);
        var hideToggle = MakeToggleSwitch(Tr.Get("hide_ip"), _hideIp, (v) => { _hideIp = v; UpdateHostDisplay(); RefreshServerList(); });
        hideToggle.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(hideToggle, 2); hostRow.Children.Add(hideToggle);

        _speedLabel = new TextBlock
        {
            Text = "↓ 0 B/s   ↑ 0 B/s",
            FontSize = 12,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas, Segoe UI"),
        };
        var speedInner = new StackPanel { Orientation = Orientation.Horizontal };
        speedInner.Children.Add(_speedLabel);
        _speedRow = new Border
        {
            Child = speedInner,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Visible
        };

        var pingRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        _pingBtn = MakeOutlineBtn(Tr.Get("check_ping"), margin: new Thickness(0, 0, 8, 0),
            click: () => _ = CheckPingAsync(), h: 34, enabled: false, w: 110, fontSize: 12);
        _defaultBtn = MakeOutlineBtn(Tr.Get("set_default"), fg: C_DANGER, margin: new Thickness(0, 0, 8, 0),
            click: SetCurrentDefault, h: 34, enabled: false, w: 110, fontSize: 12);
        pingRow.Children.Add(_pingBtn);
        pingRow.Children.Add(_defaultBtn);
        pingRow.Children.Add(_pingLabel);

        var infoHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        _deleteBtn = MakeSmallTextBtn(Tr.Get("btn_delete"), C_DANGER, click: DeleteCurrentServer, enabled: false);
        DockPanel.SetDock(_deleteBtn, Dock.Right);
        infoHeader.Children.Add(_deleteBtn);
        infoHeader.Children.Add(MakeText(Tr.Get("connection_info"), 14, true, C_TXT));

        var infoCnt = new StackPanel();
        infoCnt.Children.Add(infoHeader);
        infoCnt.Children.Add(nameRow);
        infoCnt.Children.Add(hostRow);
        infoCnt.Children.Add(pingRow);

        _serverInfoCard = MakeCard(infoCnt);
        _serverInfoCard.Margin = new Thickness(0, 0, 0, 16);
        sp.Children.Add(_serverInfoCard);

        _connectBtn = MakeBtn(Tr.Get("btn_connect"), C_ACCENT, C_TXT, h: 48, radius: 12,
            bold: true, enabled: false, click: ToggleConnection, fontSize: 14);
        _connectBtn.Margin = new Thickness(0, 0, 0, 16);
        sp.Children.Add(_connectBtn);

        _splitSwitch = MakeToggleSwitch(Tr.Get("split_tunneling"), _settings.SplitTunneling,
            (v) => { _settings.SplitTunneling = v; OnSplitToggle(); });

        _routesCombo = new ComboBox
        {
            Height = 40,
            MinWidth = 220,
            Background = Br(C_SIDEBAR),
            Foreground = Br(C_TXT),
            BorderBrush = Br(C_BORDER),
            FontSize = 13,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _routesCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressComboEvents) return;
            if (_routesCombo.SelectedItem is string s && s != _currentRoutesFile)
            {
                SaveCurrentRoutes();
                _currentRoutesFile = s;
                LoadRoutesFile(s);
                if (_proxyRoutesCombo != null && _proxyRoutesCombo.SelectedItem as string != s)
                    _proxyRoutesCombo.SelectedItem = s;
                if (_settings.SplitTunneling) RestartIfNeeded();
                UpdateTrayMenu();
            }
        };

        var routeHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
        DockPanel.SetDock(_splitSwitch, Dock.Right);
        routeHeader.Children.Add(_splitSwitch);
        routeHeader.Children.Add(MakeText(Tr.Get("routing"), 14, true, C_TXT));

        var routeCardContent = new StackPanel();
        routeCardContent.Children.Add(routeHeader);
        routeCardContent.Children.Add(_routesCombo);

        var routeCard = MakeCard(routeCardContent);
        sp.Children.Add(routeCard);

        return sp;
    }

    private Grid BuildProxyListPanel()
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(MakeOutlineBtn(Tr.Get("back"), h: 32, click: () => ShowPanel("servers"), hPad: 14, w: 80));
        header.Children.Add(MakeText(Tr.Get("proxy_list_title"), 14, true, C_TXT, margin: new Thickness(20, 0, 0, 0)));
        Grid.SetRow(header, 0);
        g.Children.Add(header);

        var content = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };

        var fileRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        fileRow.Children.Add(MakeText(Tr.Get("current_file"), 14, false, C_MUTED, margin: new Thickness(0, 0, 10, 0)));
        _proxyRoutesCombo = new ComboBox
        {
            Height = 36,
            MinWidth = 200,
            Background = Br(C_SIDEBAR),
            Foreground = Br(C_TXT),
            BorderBrush = Br(C_BORDER),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        _proxyRoutesCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressComboEvents) return;
            if (_proxyRoutesCombo.SelectedItem is string s && s != _currentRoutesFile)
            {
                SaveCurrentRoutes();
                _currentRoutesFile = s;
                LoadRoutesFile(s);
                if (_routesCombo.SelectedItem as string != s)
                    _routesCombo.SelectedItem = s;
                if (_settings.SplitTunneling) RestartIfNeeded();
                UpdateTrayMenu();
            }
        };
        fileRow.Children.Add(_proxyRoutesCombo);
        content.Children.Add(fileRow);

        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        _proxyNewBtn = MakeOutlineBtn(Tr.Get("new_routes_file"), margin: new Thickness(0, 0, 8, 0), h: 36, click: CreateRoutesFile, w: 120);
        _proxyDeleteBtn = MakeOutlineBtn(Tr.Get("delete_routes_file"), fg: C_DANGER, margin: new Thickness(0, 0, 8, 0), h: 36, click: DeleteRoutesFile, w: 120);
        _proxyRenameBtn = MakeOutlineBtn(Tr.Get("rename_routes_file"), margin: new Thickness(0, 0, 8, 0), h: 36, click: RenameRoutesFile, w: 120);
        buttonsRow.Children.Add(_proxyNewBtn);
        buttonsRow.Children.Add(_proxyDeleteBtn);
        buttonsRow.Children.Add(_proxyRenameBtn);
        content.Children.Add(buttonsRow);

        _proxyRoutingText = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Background = Br(C_SIDEBAR),
            Foreground = Br(C_TXT),
            BorderBrush = Br(C_BORDER),
            BorderThickness = new Thickness(1),
            CaretBrush = Br(C_TXT),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Padding = new Thickness(12),
            Height = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 16),
            IsEnabled = true
        };
        var textBoxStyle = new Style(typeof(TextBox));
        textBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Br(C_BORDER)));
        textBoxStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        var focusTrigger = new Trigger { Property = UIElement.IsFocusedProperty, Value = true };
        focusTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, Br(C_BORDER)));
        var mouseOverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        mouseOverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, Br(C_BORDER)));
        textBoxStyle.Triggers.Add(focusTrigger);
        textBoxStyle.Triggers.Add(mouseOverTrigger);
        _proxyRoutingText.Style = textBoxStyle;

        content.Children.Add(_proxyRoutingText);

        _proxySaveBtn = MakeBtn(Tr.Get("save_routes"), C_ACCENT, C_TXT, h: 40, w: 120, radius: 8, click: SaveRoutesFromEditor);
        _proxySaveBtn.IsEnabled = true;
        content.Children.Add(_proxySaveBtn);

        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        g.Children.Add(scroll);

        return g;
    }

    private Grid BuildSettingsPanel()
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(MakeOutlineBtn(Tr.Get("back"), h: 32, click: () => ShowPanel("servers"), hPad: 14, w: 80));
        header.Children.Add(MakeText(Tr.Get("settings_title"), 14, true, C_TXT, margin: new Thickness(20, 0, 0, 0)));
        Grid.SetRow(header, 0); g.Children.Add(header);

        var tabs = new TabControl { Background = Br(C_CARD), BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0) };
        ApplyTabStyle(tabs);

        tabs.Items.Add(BuildTabGeneral());
        tabs.Items.Add(BuildTabDns());
        tabs.Items.Add(BuildTabExceptions());
        _hotkeysTab = BuildTabHotkeys();
        tabs.Items.Add(_hotkeysTab);
        tabs.Items.Add(BuildTabConfig());
        tabs.Items.Add(BuildTabVersion());

        var tabWrapper = new Border
        {
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            BorderThickness = new Thickness(0),
            Child = tabs
        };
        Grid.SetRow(tabWrapper, 1); g.Children.Add(tabWrapper);
        return g;
    }

    private TabItem BuildTabGeneral()
    {
        var sp = new StackPanel { Margin = new Thickness(20) };

        _autostartChk = MakeToggleSwitch(Tr.Get("autostart"), Autostart.IsEnabled(), v => Autostart.Set(v));
        _minimizeChk = MakeToggleSwitch(Tr.Get("minimize_to_tray"), _settings.MinimizeOnClose, v => { _settings.MinimizeOnClose = v; SaveSettings(); });
        _killSwitchChk = MakeToggleSwitch(Tr.Get("kill_switch"), _settings.KillSwitch, v => { _settings.KillSwitch = v; SaveSettings(); });
        _autoReconnectChk = MakeToggleSwitch(Tr.Get("auto_reconnect"), _settings.AutoReconnect, v => { _settings.AutoReconnect = v; SaveSettings(); });
        _debugChk = MakeToggleSwitch(Tr.Get("debug_mode"), _settings.DebugMode, v => { _settings.DebugMode = v; SaveSettings(); });
        _notificationsChk = MakeToggleSwitch(Tr.Get("notifications"), _settings.EnableNotifications, v => { _settings.EnableNotifications = v; SaveSettings(); });
        _autoConnectOnStartChk = MakeToggleSwitch(Tr.Get("auto_connect_on_start"), _settings.AutoConnectOnStart, v => { _settings.AutoConnectOnStart = v; SaveSettings(); });
        _minimizeOnStartupChk = MakeToggleSwitch(Tr.Get("minimize_on_startup"), _settings.MinimizeOnStartup, v => { _settings.MinimizeOnStartup = v; SaveSettings(); });

        _tunModeChk = MakeToggleSwitch(Tr.Get("tun_mode"), _settings.UseTunMode, v =>
        {
            if (v && !IsAdministrator())
            {
                var result = MessageBox.Show(
                    "TUN-режим требует прав администратора.\nПерезапустить приложение с правами администратора?",
                    "TUN Mode",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    _settings.UseTunMode = true;
                    SaveSettings();
                    RestartAsAdmin();
                }
                else
                {
                    _tunModeChk!.IsChecked = false;
                }
                return;
            }

            _settings.UseTunMode = v;
            SaveSettings();
            if (_proxyProcess != null)
                RestartIfNeeded();
        });
        _tunModeChk.Margin = new Thickness(0, 0, 0, 8);

        var langRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        langRow.Children.Add(MakeText(Tr.Get("language_label"), 14, false, C_MUTED));
        _langCombo = new ComboBox { Margin = new Thickness(10, 0, 0, 0), MinWidth = 120, Height = 36, FontSize = 14 };
        _langCombo.Items.Add("Русский"); _langCombo.Items.Add("English");
        _langCombo.SelectedIndex = _settings.Language == "ru" ? 0 : 1;
        _langCombo.SelectionChanged += (_, _) =>
        {
            _settings.Language = _langCombo.SelectedIndex == 0 ? "ru" : "en";
            SaveSettings();
            MessageBox.Show("Перезапустите приложение для смены языка.\nRestart app to change language.",
                "Language", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        langRow.Children.Add(_langCombo);

        sp.Children.Add(MakeText(Tr.Get("settings_title"), 18, true, C_TXT, margin: new Thickness(0, 0, 0, 16)));
        foreach (var chk in new[] { _autostartChk, _minimizeChk, _minimizeOnStartupChk, _killSwitchChk, _autoReconnectChk, _autoConnectOnStartChk, _debugChk, _notificationsChk, _tunModeChk })
        { chk.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(chk); }
        sp.Children.Add(langRow);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        btnRow.Children.Add(MakeOutlineBtn(Tr.Get("view_logs"), h: 36, margin: new Thickness(0, 0, 8, 0), click: ViewLogs, w: 140));
        btnRow.Children.Add(MakeOutlineBtn(Tr.Get("restart_app"), h: 36, click: RestartApp, w: 140));
        sp.Children.Add(btnRow);

        var scroll = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return new TabItem { Header = Tr.Get("tab_general"), Content = scroll, Foreground = Br(C_TXT) };
    }

    private TabItem BuildTabDns()
    {
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(MakeText(Tr.Get("dns_settings"), 18, true, C_TXT, margin: new Thickness(0, 0, 0, 16)));

        var typeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        typeRow.Children.Add(MakeText(Tr.Get("dns_type"), 14, false, C_MUTED));
        _dnsTypeCombo = new ComboBox { Margin = new Thickness(10, 0, 0, 0), MinWidth = 200, Height = 36 };
        _dnsTypeCombo.Items.Add(Tr.Get("dns_system"));
        _dnsTypeCombo.Items.Add(Tr.Get("dns_doh"));
        _dnsTypeCombo.Items.Add(Tr.Get("dns_dot"));
        _dnsTypeCombo.SelectedIndex = _settings.DnsType switch { "doh" => 1, "dot" => 2, _ => 0 };
        typeRow.Children.Add(_dnsTypeCombo);

        var addrRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        addrRow.Children.Add(MakeText(Tr.Get("dns_server_address"), 14, false, C_MUTED));
        _dnsAddrBox = new TextBox
        {
            Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 250,
            Text = _settings.DnsServer,
            Background = Br(C_SIDEBAR),
            Foreground = Br(C_TXT),
            BorderBrush = Br(C_BORDER),
            CaretBrush = Br(C_TXT),
            Padding = new Thickness(6, 4, 6, 4)
        };
        addrRow.Children.Add(_dnsAddrBox);

        _dnsProxyChk = MakeToggleSwitch(Tr.Get("dns_through_proxy"), _settings.DnsThroughProxy, _ => { });
        _dnsProxyChk.Margin = new Thickness(0, 0, 0, 12);

        _dnsTestLabel = MakeText("", 13, false, C_MUTED);
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        saveRow.Children.Add(MakeBtn(Tr.Get("save"), C_ACCENT, C_TXT, h: 36, margin: new Thickness(0, 0, 8, 0), click: SaveDns, w: 120));
        saveRow.Children.Add(MakeOutlineBtn(Tr.Get("dns_test"), h: 36, margin: new Thickness(0, 0, 10, 0), click: TestDns, w: 120));
        saveRow.Children.Add(_dnsTestLabel);

        sp.Children.Add(typeRow);
        sp.Children.Add(addrRow);
        sp.Children.Add(_dnsProxyChk);
        sp.Children.Add(saveRow);

        return new TabItem { Header = Tr.Get("tab_dns"), Content = sp, Foreground = Br(C_TXT) };
    }

    private TabItem BuildTabExceptions()
    {
        var sp = new StackPanel { Margin = new Thickness(20) };

        sp.Children.Add(MakeText(Tr.Get("app_exceptions"), 16, true, C_TXT, margin: new Thickness(0, 0, 0, 4)));
        sp.Children.Add(MakeText(Tr.Get("app_exceptions_desc"), 12, false, C_MUTED, margin: new Thickness(0, 0, 0, 10)));
        var appBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var addAppBtn = MakeOutlineBtn(Tr.Get("add_app"), h: 32, margin: new Thickness(0, 0, 6, 0), click: ShowAddAppMenu, w: 170);
        _removeAppBtn = MakeOutlineBtn(Tr.Get("remove_app"), fg: C_DANGER, h: 32, enabled: false, click: RemoveAppException, w: 100);
        appBtns.Children.Add(addAppBtn); appBtns.Children.Add(_removeAppBtn);
        sp.Children.Add(appBtns);
        _appExcList = new StackPanel();
        var appExcScroll = new ScrollViewer { Content = _appExcList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 172 };
        sp.Children.Add(MakeCard(appExcScroll, margin: new Thickness(0, 0, 0, 16), maxHeight: 220));

        sp.Children.Add(MakeText(Tr.Get("domain_exceptions"), 16, true, C_TXT, margin: new Thickness(0, 0, 0, 4)));
        sp.Children.Add(MakeText(Tr.Get("domain_exceptions_desc"), 12, false, C_MUTED, margin: new Thickness(0, 0, 0, 10)));
        var domBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var addDomBtn = MakeOutlineBtn(Tr.Get("add_domain"), h: 32, margin: new Thickness(0, 0, 6, 0), click: AddDomainException, w: 140);
        _removeDomBtn = MakeOutlineBtn(Tr.Get("remove_domain"), fg: C_DANGER, h: 32, enabled: false, click: RemoveDomainException, w: 100);
        domBtns.Children.Add(addDomBtn); domBtns.Children.Add(_removeDomBtn);
        sp.Children.Add(domBtns);
        _domExcList = new StackPanel();
        var domExcScroll = new ScrollViewer { Content = _domExcList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 172 };
        sp.Children.Add(MakeCard(domExcScroll, margin: new Thickness(0, 0, 0, 0), maxHeight: 220));

        var scroll = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return new TabItem { Header = Tr.Get("tab_exceptions"), Content = scroll, Foreground = Br(C_TXT) };
    }

    private Grid BuildImportPanel()
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(MakeOutlineBtn(Tr.Get("back"), h: 32, click: () => ShowPanel("servers"), hPad: 14, w: 80));
        header.Children.Add(MakeText(Tr.Get("import_title"), 14, true, C_TXT, margin: new Thickness(20, 0, 0, 0)));
        Grid.SetRow(header, 0); g.Children.Add(header);

        var sp = new StackPanel { Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 400 };
        sp.Children.Add(MakeBtn(Tr.Get("import_file"), C_SIDEBAR, C_TXT, h: 48, radius: 8,
            bold: true, margin: new Thickness(0, 0, 0, 12), click: ImportSitesFromFile));
        sp.Children.Add(MakeBtn(Tr.Get("import_clipboard"), C_ACCENT, C_TXT, h: 48, radius: 8,
            bold: true, click: ImportServersFromFile));
        Grid.SetRow(sp, 1); g.Children.Add(sp);

        return g;
    }

    private TabItem BuildTabConfig()
    {
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(MakeText(Tr.Get("tab_config"), 18, true, C_TXT, margin: new Thickness(0, 0, 0, 16)));

        var exportGroup = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        exportGroup.Children.Add(MakeText(Tr.Get("export_label"), 14, true, C_TXT, margin: new Thickness(0, 0, 0, 8)));

        var exportBtn = MakeOutlineBtn(Tr.Get("export_config"), h: 36, click: ExportConfiguration, w: 200);
        exportBtn.Margin = new Thickness(0, 0, 0, 8);
        exportGroup.Children.Add(exportBtn);

        _exportServersChk = MakeToggleSwitch(Tr.Get("export_servers"), false, _ => { });
        _exportServersChk.Margin = new Thickness(0, 0, 0, 4);
        exportGroup.Children.Add(_exportServersChk);

        _exportRoutesChk = MakeToggleSwitch(Tr.Get("export_routes"), false, _ => { });
        _exportRoutesChk.Margin = new Thickness(0, 0, 0, 0);
        exportGroup.Children.Add(_exportRoutesChk);

        sp.Children.Add(exportGroup);

        var importGroup = new StackPanel();
        importGroup.Children.Add(MakeText(Tr.Get("import_label"), 14, true, C_TXT, margin: new Thickness(0, 0, 0, 8)));

        var importBtn = MakeOutlineBtn(Tr.Get("import_config"), h: 36, click: ImportConfiguration, w: 200);
        importGroup.Children.Add(importBtn);

        sp.Children.Add(importGroup);

        var scroll = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return new TabItem { Header = Tr.Get("tab_config"), Content = scroll, Foreground = Br(C_TXT) };
    }

    private TabItem BuildTabVersion()
    {
        var sp = new StackPanel { Margin = new Thickness(20) };

        sp.Children.Add(MakeText(Tr.Get("tab_version"), 18, true, C_TXT,
            margin: new Thickness(0, 0, 0, 24)));

        var appCard = new Border
        {
            Background      = Br(C_SIDEBAR),
            BorderBrush     = Br(C_ACCENT),
            BorderThickness = new Thickness(0, 0, 0, 2),
            CornerRadius    = new CornerRadius(10),
            Padding         = new Thickness(20, 16, 20, 16),
            Margin          = new Thickness(0, 0, 0, 12)
        };
        var appRow  = new StackPanel { Orientation = Orientation.Horizontal };
        var appIcon = new Border
        {
            Width = 40, Height = 40,
            Background   = Br(C_ACCENT),
            CornerRadius = new CornerRadius(8),
            Margin       = new Thickness(0, 0, 16, 0),
            Child        = new TextBlock
            {
                Text = "A", FontSize = 20, FontWeight = FontWeights.Bold,
                Foreground          = Br(C_TXT),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            }
        };
        var appText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        appText.Children.Add(MakeText(Tr.Get("version_app_label"), 12, false, C_MUTED));
        appText.Children.Add(MakeText(Tr.Get("version_app_val"),   22, true,  C_TXT));
        appRow.Children.Add(appIcon);
        appRow.Children.Add(appText);
        appCard.Child = appRow;
        sp.Children.Add(appCard);

        var sbCard = new Border
        {
            Background      = Br(C_SIDEBAR),
            BorderBrush     = Br(C_SUCCESS),
            BorderThickness = new Thickness(0, 0, 0, 2),
            CornerRadius    = new CornerRadius(10),
            Padding         = new Thickness(20, 16, 20, 16),
            Margin          = new Thickness(0, 0, 0, 24)
        };
        var sbRow  = new StackPanel { Orientation = Orientation.Horizontal };
        var sbIcon = new Border
        {
            Width = 40, Height = 40,
            Background   = Br(C_SUCCESS),
            CornerRadius = new CornerRadius(8),
            Margin       = new Thickness(0, 0, 16, 0),
            Child        = new TextBlock
            {
                Text = "S", FontSize = 20, FontWeight = FontWeights.Bold,
                Foreground          = Br(C_TXT),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            }
        };
        var sbText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sbText.Children.Add(MakeText(Tr.Get("version_sb_label"), 12, false, C_MUTED));
        sbText.Children.Add(MakeText(Tr.Get("version_sb_val"),   22, true,  C_TXT));
        sbRow.Children.Add(sbIcon);
        sbRow.Children.Add(sbText);
        sbCard.Child = sbRow;
        sp.Children.Add(sbCard);

        sp.Children.Add(new Border
        {
            Height     = 1,
            Background = Br(C_BORDER),
            Margin     = new Thickness(0, 0, 0, 20)
        });

        var infoGrid = new Grid();
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void AddInfoRow(int row, string label, string value)
        {
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = MakeText(label, 13, false, C_MUTED, margin: new Thickness(0, 0, 16, 8));
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
            var val = MakeText(value, 13, true, C_TXT, margin: new Thickness(0, 0, 0, 8));
            Grid.SetRow(val, row); Grid.SetColumn(val, 1);
            infoGrid.Children.Add(lbl);
            infoGrid.Children.Add(val);
        }

        var archStr = Environment.Is64BitProcess ? "x64" : "x86";
        AddInfoRow(0, Tr.Get("version_built_on"), "05.05.2026");
        AddInfoRow(1, Tr.Get("version_core"),     "sing-box " + Tr.Get("version_sb_val"));
        AddInfoRow(2, Tr.Get("upd_arch"),          archStr);
        sp.Children.Add(infoGrid);

        sp.Children.Add(new Border
        {
            Height     = 1,
            Background = Br(C_BORDER),
            Margin     = new Thickness(0, 24, 0, 20)
        });

        var updCard = new Border
        {
            Background      = Br(C_SIDEBAR),
            BorderBrush     = Br(C_BORDER),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(10),
            Padding         = new Thickness(20, 18, 20, 18),
            Margin          = new Thickness(0, 0, 0, 0)
        };
        var updSp = new StackPanel();

        var updHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 14)
        };
        var updIconBorder = new Border
        {
            Width        = 36, Height = 36,
            Background   = Br(ParseColor("#312E81")),
            CornerRadius = new CornerRadius(8),
            Margin       = new Thickness(0, 0, 12, 0),
            Child        = new TextBlock
            {
                Text                = "↑",
                FontSize            = 18,
                FontWeight          = FontWeights.Bold,
                Foreground          = Br(C_ACCENT),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            }
        };
        var updTitle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        updTitle.Children.Add(MakeText(Tr.Get("upd_section"), 15, true, C_TXT));
        updTitle.Children.Add(MakeText(AppVersion, 11, false, C_MUTED));
        updHeader.Children.Add(updIconBorder);
        updHeader.Children.Add(updTitle);
        updSp.Children.Add(updHeader);

        var checkRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 12)
        };

        _updateStatusTb = new TextBlock
        {
            Text               = "",
            FontSize           = 13,
            Foreground         = Br(C_MUTED),
            VerticalAlignment  = VerticalAlignment.Center,
            TextWrapping       = TextWrapping.Wrap,
            Margin             = new Thickness(0, 0, 12, 0),
            MaxWidth           = 280
        };

        _checkUpdateBtn = MakeOutlineBtn(
            Tr.Get("upd_check_btn"),
            h: 36, fontSize: 13, hPad: 40, w: 200,
            click: () => _ = CheckForUpdatesAsync());

        _updateStatusTb.Margin = new Thickness(20, 0, 0, 0);

        checkRow.Children.Add(_checkUpdateBtn);
        checkRow.Children.Add(_updateStatusTb);
        updSp.Children.Add(checkRow);

        if (_settings.Language == "ru")
        {
            var ruHint = new TextBlock
            {
                Text         = "⚠  Перед обновлением подключитесь к серверу, если вы живёте в России",
                FontSize     = 12,
                Foreground   = Br(C_MUTED),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 12),
                Opacity      = 0.75
            };
            updSp.Children.Add(ruHint);
        }

        _updateProgressBar = new ProgressBar
        {
            Height    = 6,
            Minimum   = 0,
            Maximum   = 100,
            Value     = 0,
            Foreground = Br(C_ACCENT),
            Background = Br(C_BORDER),
            BorderThickness = new Thickness(0)
        };
        _updateProgressRow = new Border
        {
            Margin      = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Child        = _updateProgressBar,
            Visibility   = Visibility.Collapsed
        };
        updSp.Children.Add(_updateProgressRow);

        _downloadUpdateBtn = MakeBtn(
            Tr.Get("upd_download"),
            C_ACCENT, C_TXT,
            h: 38, radius: 9, fontSize: 14, bold: true,
            click: () => _ = DownloadAndInstallUpdateAsync());
        _downloadUpdateBtn.Visibility = Visibility.Collapsed;
        _downloadUpdateBtn.HorizontalAlignment = HorizontalAlignment.Left;
        _downloadUpdateBtn.MinWidth = 130;
        updSp.Children.Add(_downloadUpdateBtn);

        updCard.Child = updSp;
        sp.Children.Add(updCard);

        var scroll = new ScrollViewer
        {
            Content = sp,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        return new TabItem
        {
            Header     = Tr.Get("tab_version"),
            Content    = scroll,
            Foreground = Br(C_TXT)
        };
    }

    private async Task CheckForUpdatesAsync()
    {
        SetUpdateStatus(Tr.Get("upd_checking"), C_MUTED);
        _checkUpdateBtn!.IsEnabled       = false;
        _downloadUpdateBtn!.Visibility   = Visibility.Collapsed;
        _updateProgressRow!.Visibility   = Visibility.Collapsed;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var http = MakeUpdaterHttpClient(_proxyProcess != null);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TaaProxy-Updater/1.0");

            var json = await http.GetStringAsync(GhApiLatest, cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var latestVer = tagName.Split('-')[0].Trim();
            var currentVer = AppVersion.TrimEnd('0').TrimEnd('.');

            bool hasUpdate = CompareVersions(latestVer, currentVer) > 0;

            bool is64 = Environment.Is64BitProcess;
            string? downloadUrl = null;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    bool isX64 = name.Contains("x64", StringComparison.OrdinalIgnoreCase);
                    bool isX86 = name.Contains("x86", StringComparison.OrdinalIgnoreCase)
                              || name.Contains("x32", StringComparison.OrdinalIgnoreCase);

                    if ((is64 && isX64) || (!is64 && isX86))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            _latestDownloadUrl = downloadUrl;

            Dispatcher.Invoke(() =>
            {
                if (!hasUpdate)
                {
                    SetUpdateStatus(Tr.Get("upd_latest"), C_SUCCESS);
                }
                else
                {
                    var arch = is64 ? "x64" : "x86";
                    SetUpdateStatus($"{Tr.Get("upd_available")}v{latestVer} ({arch})", C_ACCENT);
                    if (downloadUrl != null)
                        _downloadUpdateBtn!.Visibility = Visibility.Visible;
                }
                _checkUpdateBtn!.IsEnabled = true;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                SetUpdateStatus(Tr.Get("upd_error") + ex.Message, C_DANGER);
                _checkUpdateBtn!.IsEnabled = true;
            });
        }
    }

    private HttpClient MakeUpdaterHttpClient(bool useProxy)
    {
        HttpClientHandler handler;
        if (useProxy && _proxyProcess != null)
        {
            handler = new HttpClientHandler
            {
                Proxy    = new System.Net.WebProxy($"socks5://127.0.0.1:{_proxyPort}"),
                UseProxy = true,
            };
        }
        else
        {
            handler = new HttpClientHandler { UseProxy = false };
        }
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private async Task DownloadAndInstallUpdateAsync()
    {
        if (string.IsNullOrEmpty(_latestDownloadUrl)) return;

        _downloadUpdateBtn!.IsEnabled    = false;
        _checkUpdateBtn!.IsEnabled       = false;
        _updateProgressRow!.Visibility   = Visibility.Visible;
        _updateProgressBar!.Value        = 0;
        SetUpdateStatus(Tr.Get("upd_downloading"), C_MUTED);

        var tempRar = Path.Combine(Path.GetTempPath(), "TaaProxy_update.rar");
        try
        {
            bool proxyActive = _proxyProcess != null;

            var candidates = new List<(string url, bool useProxy, string label)>();
            if (proxyActive)
            {
                candidates.Add((_latestDownloadUrl, true,  "через прокси"));
            }
            candidates.Add((_latestDownloadUrl.Replace("https://objects.githubusercontent.com", "https://ghfast.top/https://objects.githubusercontent.com"), false, "зеркало 1"));
            candidates.Add((_latestDownloadUrl.Replace("https://objects.githubusercontent.com", "https://gh-proxy.com/https://objects.githubusercontent.com"), false, "зеркало 2"));
            candidates.Add((_latestDownloadUrl.Replace("https://objects.githubusercontent.com", "https://mirror.ghproxy.com/https://objects.githubusercontent.com"), false, "зеркало 3"));
            candidates.Add((_latestDownloadUrl, false, "github.com"));

            bool downloaded = false;
            Exception? lastEx = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                var (url, useProxy, mirrorLabel) = candidates[i];
                Dispatcher.Invoke(() =>
                {
                    _updateProgressBar!.Value = 0;
                    SetUpdateStatus($"{Tr.Get("upd_downloading")} ({mirrorLabel})", C_MUTED);
                });

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    var token = cts.Token;

                    using (var http = MakeUpdaterHttpClient(useProxy))
                    {
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("TaaProxy-Updater/1.0");

                        using var response = await http.GetAsync(
                            url,
                            HttpCompletionOption.ResponseHeadersRead,
                            token);
                        response.EnsureSuccessStatusCode();

                        var total = response.Content.Headers.ContentLength ?? -1L;
                        await using var src  = await response.Content.ReadAsStreamAsync(token);
                        await using var dest = new FileStream(tempRar, FileMode.Create, FileAccess.Write, FileShare.None);

                        var buf       = new byte[81920];
                        long received = 0;
                        int  read;
                        while ((read = await src.ReadAsync(buf, token)) > 0)
                        {
                            await dest.WriteAsync(buf.AsMemory(0, read), token);
                            received += read;
                            cts.CancelAfter(TimeSpan.FromSeconds(30));
                            if (total > 0)
                            {
                                double pct = received * 100.0 / total;
                                Dispatcher.Invoke(() => _updateProgressBar!.Value = pct);
                            }
                        }
                    }

                    downloaded = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    lastEx = new Exception($"Таймаут ({mirrorLabel}) — переключаемся на следующий источник");
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (!downloaded)
                throw lastEx ?? new Exception("All download mirrors failed");

            Process? procToWait = null;
            Dispatcher.Invoke(() =>
            {
                procToWait = _proxyProcess;
                if (_proxyProcess != null) StopProxy();
            });

            if (procToWait != null)
            {
                await Task.Run(() =>
                {
                    try { procToWait.WaitForExit(8000); } catch { }
                });
            }

            await Task.Run(() =>
            {
                foreach (var f in Directory.GetFiles(Paths.Base, "*.new"))
                    try { File.Delete(f); } catch { }
                var coreDir2 = Path.Combine(Paths.Base, "core");
                if (Directory.Exists(coreDir2))
                    foreach (var f in Directory.GetFiles(coreDir2, "*.new"))
                        try { File.Delete(f); } catch { }
            });

            SetUpdateStatus(Tr.Get("upd_extracting"), C_MUTED);
            Dispatcher.Invoke(() => _updateProgressBar!.IsIndeterminate = true);

            await Task.Run(() => ExtractUpdate(tempRar));

            Dispatcher.Invoke(() =>
            {
                _updateProgressRow!.Visibility       = Visibility.Collapsed;
                _updateProgressBar!.IsIndeterminate  = false;
                SetUpdateStatus(Tr.Get("upd_done"), C_SUCCESS);
                _downloadUpdateBtn!.Visibility       = Visibility.Collapsed;
                _checkUpdateBtn!.IsEnabled           = true;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                _updateProgressRow!.Visibility       = Visibility.Collapsed;
                _updateProgressBar!.IsIndeterminate  = false;
                SetUpdateStatus(Tr.Get("upd_error") + ex.Message, C_DANGER);
                _downloadUpdateBtn!.IsEnabled        = true;
                _checkUpdateBtn!.IsEnabled           = true;
            });
        }
        finally
        {
            try { if (File.Exists(tempRar)) File.Delete(tempRar); } catch { }
        }
    }

    private static void ExtractUpdate(string rarPath)
    {
        var baseDir = Paths.Base;
        var coreDir = Path.Combine(baseDir, "core");
        Directory.CreateDirectory(coreDir);

        using var archive = ArchiveFactory.Open(rarPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory) continue;

            var entryKey = entry.Key?.Replace('\\', '/') ?? "";

            if (!entryKey.Contains('/') &&
                entryKey.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ExtractEntryToFile(entry, Path.Combine(baseDir, Path.GetFileName(entryKey)));
                continue;
            }

            if (entryKey.StartsWith("core/", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(entryKey);
                if (string.IsNullOrEmpty(fileName)) continue;
                ExtractEntryToFile(entry, Path.Combine(coreDir, fileName));
            }
        }
    }

    private static void ExtractEntryToFile(SharpCompress.Archives.IArchiveEntry entry, string destPath)
    {
        var tmp = destPath + ".new";

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            entry.WriteTo(fs);

        if (File.Exists(destPath))
        {
            File.Replace(tmp, destPath, null);
        }
        else
        {
            File.Move(tmp, destPath);
        }
    }

    private void SetUpdateStatus(string text, Color color)
    {
        if (_updateStatusTb == null) return;
        _updateStatusTb.Text       = text;
        _updateStatusTb.Foreground = Br(color);
    }

    private static int CompareVersions(string a, string b)
    {
        static Version Parse(string s)
        {
            var parts = s.Split('.');
            int major = parts.Length > 0 && int.TryParse(parts[0], out int mj) ? mj : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out int mn) ? mn : 0;
            int patch = parts.Length > 2 && int.TryParse(parts[2], out int pt) ? pt : 0;
            return new Version(major, minor, patch);
        }
        return Parse(a).CompareTo(Parse(b));
    }

    private void ExportConfiguration()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Taa Configuration (*.taa)|*.taa|All files (*.*)|*.*",
            Title = "Export configuration",
            FileName = $"TaaConfig_{DateTime.Now:yyyyMMdd_HHmmss}.taa"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var export = new Dictionary<string, object>
            {
                ["version"] = "1.0",
                ["settings"] = _settings
            };

            if (_exportServersChk?.IsChecked == true)
            {
                export["servers"] = _servers;
            }

            if (_exportRoutesChk?.IsChecked == true)
            {
                var routes = new Dictionary<string, string>();
                if (Directory.Exists(Paths.ListDir))
                {
                    foreach (var file in Directory.GetFiles(Paths.ListDir, "*.txt"))
                    {
                        var name = Path.GetFileName(file);
                        var content = File.ReadAllText(file);
                        routes[name] = content;
                    }
                }
                export["routes"] = routes;
            }

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);

            MessageBox.Show(Tr.Get("export_success"), "Taa Proxy", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Tr.Get("export_error")} {ex.Message}", Tr.Get("error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportConfiguration()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Taa Configuration (*.taa)|*.taa|JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import configuration"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("settings", out var settingsElem))
            {
                var importedSettings = JsonSerializer.Deserialize<AppSettings>(settingsElem.GetRawText());
                if (importedSettings != null)
                {
                    importedSettings.WindowLeft = _settings.WindowLeft;
                    importedSettings.WindowTop = _settings.WindowTop;
                    importedSettings.WindowWidth = _settings.WindowWidth;
                    importedSettings.WindowHeight = _settings.WindowHeight;
                    _settings = importedSettings;
                    SaveSettings();
                    Tr.SetLang(_settings.Language);
                }
            }

            if (root.TryGetProperty("servers", out var serversElem))
            {
                var importedServers = JsonSerializer.Deserialize<List<ServerModel>>(serversElem.GetRawText());
                if (importedServers != null)
                {
                    _servers = importedServers;
                    SaveServers();
                }
            }

            if (root.TryGetProperty("routes", out var routesElem))
            {
                var routes = JsonSerializer.Deserialize<Dictionary<string, string>>(routesElem.GetRawText());
                if (routes != null)
                {
                    Directory.CreateDirectory(Paths.ListDir);
                    foreach (var kv in routes)
                    {
                        var path = Path.Combine(Paths.ListDir, kv.Key);
                        File.WriteAllText(path, kv.Value);
                    }
                    RefreshRoutesListForCombos();
                    LoadRoutesFile(_currentRoutesFile);
                }
            }

            RefreshServerList();
            SelectDefaultServer();
            RefreshSettingsUI();
            UpdateSplitState();
            UpdateTrayMenu();

            MessageBox.Show(Tr.Get("import_success"), "Taa Proxy", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Tr.Get("import_error")} {ex.Message}", Tr.Get("error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private static ControlTemplate? _btnTemplate;
    private static ControlTemplate GetBtnTemplate()
    {
        _btnTemplate ??= (ControlTemplate)XamlReader.Parse(_btnXaml);
        return _btnTemplate;
    }

    private static readonly IEasingFunction _btnEase =
        new CubicEase { EasingMode = EasingMode.EaseOut };

    private static void AnimateBrushColor(SolidColorBrush brush, Color to, int ms)
    {
        var anim = new ColorAnimation(to, TimeSpan.FromMilliseconds(ms))
            { EasingFunction = _btnEase };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private static Color ComputeHoverColor(Color c)
    {
        if (c == C_ACCENT) return C_HOVER;
        if (c == C_DANGER) return C_DANG_H;
        return Color.FromArgb(c.A,
            (byte)Math.Max(0, c.R - 18),
            (byte)Math.Max(0, c.G - 18),
            (byte)Math.Max(0, c.B - 18));
    }

    private Button MakeBtn(string text, Color bg, Color fg, double h = 44, double w = double.NaN,
        double radius = 12, bool bold = false, bool enabled = true, Thickness margin = default,
        Action? click = null, double fontSize = 14, double hPad = 16)
    {
        var btn = new Button
        {
            Content = text,
            Height = h,
            IsEnabled = enabled,
            Background = Br(bg),
            Foreground = Br(fg),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Cursor = Cursors.Hand,
            Margin = margin,
            Template = GetBtnTemplate(),
            Tag = new CornerRadius(radius),
            Padding = new Thickness(hPad, 0, hPad, 0)
        };
        if (!double.IsNaN(w)) btn.Width = w;

        SolidColorBrush? activeBrush = null;
        Color normalColor = bg;

        btn.MouseEnter += (_, _) =>
        {
            if (!btn.IsEnabled) return;
            normalColor = (btn.Background as SolidColorBrush)?.Color ?? bg;
            var ab = new SolidColorBrush(normalColor);
            btn.Background = ab;
            activeBrush = ab;
            AnimateBrushColor(ab, ComputeHoverColor(normalColor), 130);
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.Opacity = 1.0;
            if (activeBrush != null && ReferenceEquals(btn.Background, activeBrush))
                AnimateBrushColor(activeBrush, normalColor, 200);
            activeBrush = null;
        };
        btn.PreviewMouseDown += (_, _) => { if (btn.IsEnabled) btn.Opacity = 0.82; };
        btn.PreviewMouseUp   += (_, _) => { btn.Opacity = 1.0; };
        if (click != null) btn.Click += (_, _) => click();
        return btn;
    }

    private Button MakeOutlineBtn(string text, Color? fg = null, Thickness margin = default,
        double h = 44, bool enabled = true, Action? click = null, double hPad = 16, double fontSize = 13, double? w = null)
    {
        var isDanger   = fg.HasValue && fg.Value == C_DANGER;
        var hoverBgCol = isDanger
            ? Color.FromArgb(30, 239, 68, 68)
            : Color.FromArgb(255, 14, 24, 50);
        var hoverBrdCol = isDanger ? C_DANG_H : C_ACCENT;

        var bgStart  = Color.FromArgb(0, hoverBgCol.R, hoverBgCol.G, hoverBgCol.B);

        var bgBrush  = new SolidColorBrush(bgStart);
        var brdBrush = new SolidColorBrush(C_BORDER);

        var btn = new Button
        {
            Content = text,
            Height = h,
            IsEnabled = enabled,
            Background = bgBrush,
            Foreground = Br(fg ?? ParseColor("#8B9BB8")),
            BorderBrush = brdBrush,
            BorderThickness = new Thickness(1),
            FontSize = fontSize,
            Cursor = Cursors.Hand,
            Margin = margin,
            Template = GetBtnTemplate(),
            Tag = new CornerRadius(10),
            Padding = new Thickness(hPad, 0, hPad, 0)
        };
        if (w.HasValue) btn.Width = w.Value;

        btn.MouseEnter += (_, _) =>
        {
            if (!btn.IsEnabled) return;
            AnimateBrushColor(bgBrush,  hoverBgCol,  130);
            AnimateBrushColor(brdBrush, hoverBrdCol, 130);
            var curFg = (btn.Foreground as SolidColorBrush)?.Color;
            if (curFg != C_SUCCESS)
                btn.Foreground = isDanger ? Br(C_DANGER) : Br(C_TXT);
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.Opacity = 1.0;
            AnimateBrushColor(bgBrush,  bgStart,  200);
            AnimateBrushColor(brdBrush, C_BORDER, 200);
            var curFg = (btn.Foreground as SolidColorBrush)?.Color;
            if (curFg != C_SUCCESS)
                btn.Foreground = Br(fg ?? ParseColor("#8B9BB8"));
        };
        btn.PreviewMouseDown += (_, _) => { if (btn.IsEnabled) btn.Opacity = 0.80; };
        btn.PreviewMouseUp   += (_, _) => { btn.Opacity = 1.0; };
        if (click != null) btn.Click += (_, _) => click();
        return btn;
    }

    private Button MakeSmallTextBtn(string text, Color fg, Action? click = null, bool enabled = true)
    {
        var fgBrush = Br(fg);
        var hoverColor = fg == C_DANGER ? ParseColor("#FF6B6B") : ParseColor("#E2E8F0");

        var btn = new Button
        {
            Content = text,
            Height = 26,
            Padding = new Thickness(6, 0, 6, 0),
            Background = Brushes.Transparent,
            Foreground = fgBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Cursor = Cursors.Hand,
            IsEnabled = enabled,
            Template = GetBtnTemplate(),
            Tag = new CornerRadius(4)
        };

        btn.MouseEnter += (_, _) => AnimateBrushColor(fgBrush, hoverColor, 140);
        btn.MouseLeave += (_, _) => AnimateBrushColor(fgBrush, fg, 140);

        if (click != null) btn.Click += (_, _) => click();
        return btn;
    }

    private TextBlock MakeText(string text, double fs = 14, bool bold = false, Color? fg = null,
        Thickness margin = default)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fs,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Br(fg ?? C_TXT),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = margin,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private TextBlock MakeHyperText(string text, Color fg, Action? click = null,
        Thickness margin = default, HorizontalAlignment align = HorizontalAlignment.Left, bool underline = true, double fontSize = 13)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = Br(fg),
            Cursor = Cursors.Hand,
            Margin = margin,
            HorizontalAlignment = align
        };
        if (underline)
            tb.TextDecorations = TextDecorations.Underline;
        if (click != null) tb.MouseLeftButtonUp += (_, _) => click();
        return tb;
    }

    private Border MakeCard(UIElement content, Thickness margin = default, double maxHeight = double.NaN)
    {
        var b = new Border
        {
            Background      = Br(C_CARD),
            BorderBrush     = Br(C_BORDER),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(14),
            Padding         = new Thickness(22, 18, 22, 18),
            Child           = content,
            Margin          = margin
        };
        if (!double.IsNaN(maxHeight)) b.MaxHeight = maxHeight;
        return b;
    }

    private static Brush MakeConnectedCardBrush()
    {
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(
            new SolidColorBrush(ParseColor("#0D1526")),
            null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))
        ));
        var rg = new RadialGradientBrush
        {
            GradientOrigin = new System.Windows.Point(0.50, 1.00),
            Center         = new System.Windows.Point(0.50, 1.00),
            RadiusX        = 1.2,
            RadiusY        = 1.2,
            MappingMode    = BrushMappingMode.RelativeToBoundingBox
        };
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(28, 16, 185, 129), 0.0));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(8,  16, 185, 129), 0.4));
        rg.GradientStops.Add(new GradientStop(Color.FromArgb(0,  16, 185, 129), 0.75));
        dg.Children.Add(new GeometryDrawing(rg, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        return new DrawingBrush(dg)
        {
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport      = new Rect(0, 0, 1, 1),
            Stretch       = Stretch.Fill
        };
    }

    private void ApplyTabStyle(TabControl tc)
    {
        tc.Background = Br(C_CARD);
        tc.BorderBrush = Br(C_BORDER);
        tc.Foreground = Br(C_TXT);
    }

    private void ShowPanel(string name)
    {
        _serversScrollViewer.Visibility = name == "servers" ? Visibility.Visible : Visibility.Collapsed;
        _settingsPanel.Visibility = name == "settings" ? Visibility.Visible : Visibility.Collapsed;
        _importPanel.Visibility = name == "import" ? Visibility.Visible : Visibility.Collapsed;
        _proxyListPanel.Visibility = name == "proxylist" ? Visibility.Visible : Visibility.Collapsed;
        _currentPanel = name;
        if (name == "settings") RefreshSettingsUI();
        if (name == "exceptions") { RefreshAppExcListUI(); RefreshDomExcListUI(); }
        if (name == "proxylist") RefreshProxyListPanel();
    }

    private void RefreshProxyListPanel()
    {
        if (_proxyRoutesCombo != null)
        {
            RefreshRoutesListForCombos();
            _proxyRoutesCombo.SelectedItem = _currentRoutesFile;
        }
        if (_proxyRoutingText != null)
            _proxyRoutingText.Text = _routingTextBacking;
        if (_proxyRoutingText != null) _proxyRoutingText.IsEnabled = true;
        if (_proxySaveBtn != null) _proxySaveBtn.IsEnabled = true;
    }

    private string _routingTextBacking = "";
    private void LoadRoutesFile(string filename)
    {
        var path = Path.Combine(Paths.ListDir, filename);
        _routingTextBacking = File.Exists(path) ? File.ReadAllText(path) : "";
        if (_proxyRoutingText != null && _proxyRoutingText.IsLoaded)
            _proxyRoutingText.Text = _routingTextBacking;
    }

    private void SaveCurrentRoutes()
    {
        if (string.IsNullOrEmpty(_currentRoutesFile)) return;
        Directory.CreateDirectory(Paths.ListDir);
        string content = _routingTextBacking;
        if (_proxyRoutingText != null && _proxyListPanel.Visibility == Visibility.Visible)
            content = _proxyRoutingText.Text;
        File.WriteAllText(Path.Combine(Paths.ListDir, _currentRoutesFile), content);
        _routingTextBacking = content;
    }

    private void SaveRoutesFromEditor()
    {
        if (_proxyRoutingText != null)
        {
            _routingTextBacking = _proxyRoutingText.Text;
            SaveCurrentRoutes();
            RestartIfNeeded();
        }
    }

    private void RefreshRoutesListForCombos()
    {
        var files = Directory.Exists(Paths.ListDir)
            ? Directory.GetFiles(Paths.ListDir, "*.txt").Select(Path.GetFileName).OfType<string>().OrderBy(x => x).ToList()
            : new List<string>();
        if (!files.Any())
        {
            var def = Path.Combine(Paths.ListDir, "routes.txt");
            Directory.CreateDirectory(Paths.ListDir);
            if (!File.Exists(def)) File.WriteAllText(def, "instagram.com\ntwitter.com\n2ip.ru");
            files = new List<string> { "routes.txt" };
        }
        _suppressComboEvents = true;
        try
        {
            _routesCombo.ItemsSource = files;
            if (_proxyRoutesCombo != null)
                _proxyRoutesCombo.ItemsSource = files;
            if (!files.Contains(_currentRoutesFile)) _currentRoutesFile = files[0];
            if (_routesCombo.SelectedItem as string != _currentRoutesFile)
                _routesCombo.SelectedItem = _currentRoutesFile;
            if (_proxyRoutesCombo != null && _proxyRoutesCombo.SelectedItem as string != _currentRoutesFile)
                _proxyRoutesCombo.SelectedItem = _currentRoutesFile;
        }
        finally
        {
            _suppressComboEvents = false;
        }
    }

    private void LoadData()
    {
        LoadServers();
        RefreshRoutesListForCombos();
        LoadRoutesFile(_currentRoutesFile);
        UpdateSplitState();
        SelectDefaultServer();
    }

    private void LoadSettings()
    {
        if (!File.Exists(Paths.SettingsFile)) return;
        try
        {
            var json = File.ReadAllText(Paths.SettingsFile);
            _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { _settings = new AppSettings(); }
        _currentRoutesFile = _settings.CurrentRoutesFile;

        for (int i = 0; i < _settings.DomainExceptions.Count; i++)
        {
            var d = _settings.DomainExceptions[i];
            if (string.IsNullOrEmpty(d)) continue;
            if (d.Contains("xn--", StringComparison.OrdinalIgnoreCase)) continue;
            if (d.Any(c => c > 127))
            {
                try
                {
                    bool startsWithDot = d.StartsWith('.');
                    string part = startsWithDot ? d.Substring(1) : d;
                    string puny = _idn.GetAscii(part);
                    string newVal = startsWithDot ? "." + puny : puny;
                    _settings.DomainExceptions[i] = newVal;
                }
                catch { }
            }
        }
    }

    private void SaveSettings()
    {
        _settings.CurrentRoutesFile = _currentRoutesFile;
        Directory.CreateDirectory(Paths.DataDir);
        File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        FileAcl.SecureFile(Paths.SettingsFile);
    }

    private void DebounceSaveWindowBounds()
    {
        if (_boundsTimer == null)
        {
            _boundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _boundsTimer.Tick += (_, _) => { _boundsTimer.Stop(); SaveWindowBounds(); };
        }
        _boundsTimer.Stop();
        _boundsTimer.Start();
    }

    private void SaveWindowBounds()
    {
        _settings.WindowLeft = Left; _settings.WindowTop = Top;
        _settings.WindowWidth = Width; _settings.WindowHeight = Height;
        SaveSettings();
    }

    private void LoadServers()
    {
        if (!File.Exists(Paths.DbFile)) return;
        try
        {
            var raw = File.ReadAllBytes(Paths.DbFile);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("enc", out var encProp) && encProp.GetString() == "dpapi")
            {
                var b64 = doc.RootElement.GetProperty("data").GetString()!;
                var decrypted = Dpapi.Decrypt(Convert.FromBase64String(b64));
                _servers = JsonSerializer.Deserialize<List<ServerModel>>(decrypted) ?? new();
            }
            else
            {
                _servers = JsonSerializer.Deserialize<List<ServerModel>>(raw) ?? new();
                SaveServers();
            }
        }
        catch { _servers = new(); }
        MigrateServers();
        RefreshServerList();
    }

    private void MigrateServers()
    {
        bool changed = false;
        int before = _servers.Count;
        _servers.RemoveAll(sv => sv.Type == "ss" &&
            sv.Method.StartsWith("2022-", StringComparison.OrdinalIgnoreCase));
        if (_servers.Count != before) changed = true;
        if (changed)
            SaveServers();
    }

    private void SaveServers()
    {
        Directory.CreateDirectory(Paths.DataDir);
        var plain = JsonSerializer.SerializeToUtf8Bytes(_servers, new JsonSerializerOptions { WriteIndented = true });
        var enc = Dpapi.Encrypt(plain);
        var wrap = new { enc = "dpapi", data = Convert.ToBase64String(enc) };
        File.WriteAllText(Paths.DbFile, JsonSerializer.Serialize(wrap));
        FileAcl.SecureFile(Paths.DbFile);
    }

    private void SelectDefaultServer()
    {
        var def = _settings.DefaultServer;
        if (!string.IsNullOrEmpty(def))
        {
            var idx = _servers.FindIndex(s => s.Name == def);
            if (idx >= 0) SelectServer(idx);
        }
    }

    private List<Border> _serverFrames = new();
    private Popup? _serverContextPopup;

    private void RefreshServerList()
    {
        _serverListPanel.Children.Clear();
        _serverFrames.Clear();
        var def = _settings.DefaultServer;

        var displayOrder = Enumerable.Range(0, _servers.Count)
            .OrderBy(i => _servers[i].Name == def ? 0 : 1)
            .ToList();

        _serverFrames.AddRange(Enumerable.Repeat<Border>(null!, _servers.Count));

        for (int di = 0; di < displayOrder.Count; di++)
        {
            var idx         = displayOrder[di];
            var sv          = _servers[idx];
            bool isDefault  = sv.Name == def;
            bool isConnected = idx == _connectedIdx;
            bool isSelected  = idx == _selectedIdx;

            var borderColor   = isConnected ? ParseColor("#0F3D28") : isSelected ? C_BORDER : ParseColor("#0D1830");
            var borderThick   = isConnected || isSelected ? 1.0 : 1.0;

            var frame = new Border
            {
                Background      = isConnected ? Br(ParseColor("#081F14")) : isSelected ? Br(C_ACTIVE) : Brushes.Transparent,
                BorderBrush     = Br(borderColor),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(10),
                Margin          = new Thickness(0, 0, 0, 4),
                Padding         = new Thickness(11, 9, 11, 9),
                Cursor          = Cursors.Hand
            };

            var inner = new StackPanel();

            var row1 = new Grid();
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameTb = new TextBlock
            {
                Text         = sv.Name,
                FontSize     = 13,
                FontWeight   = isConnected ? FontWeights.SemiBold : FontWeights.Medium,
                Foreground   = isConnected ? Br(C_SUCCESS) : Br(C_TXT),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameTb, 0);
            row1.Children.Add(nameTb);

            if (isConnected)
            {
                var dot = new Ellipse
                {
                    Width = 7, Height = 7,
                    Fill  = Br(C_SUCCESS),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 1, 0, 0)
                };
                Grid.SetColumn(dot, 1);
                row1.Children.Add(dot);
            }
            inner.Children.Add(row1);

            var hostTb = new TextBlock
            {
                Text       = _hideIp ? MaskHost(sv.Host) : $"{sv.Host}:{sv.Port}",
                FontSize   = 11,
                Foreground = Br(C_MUTED),
                Margin     = new Thickness(0, 3, 0, 5),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            inner.Children.Add(hostTb);

            var row3 = new StackPanel { Orientation = Orientation.Horizontal };
            var typeBadge = new Border
            {
                Background      = Br(ParseColor("#10183A")),
                BorderBrush     = Br(ParseColor("#1A2A5A")),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(6, 2, 6, 2),
                Child           = new TextBlock
                {
                    Text       = sv.Type.ToUpperInvariant(),
                    FontSize   = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Br(ParseColor("#818CF8"))
                }
            };
            row3.Children.Add(typeBadge);

            if (isDefault)
            {
                var defBadge = new Border
                {
                    Background      = Br(ParseColor("#0E2010")),
                    BorderBrush     = Br(ParseColor("#1A4028")),
                    BorderThickness = new Thickness(1),
                    CornerRadius    = new CornerRadius(4),
                    Padding         = new Thickness(6, 2, 6, 2),
                    Margin          = new Thickness(4, 0, 0, 0),
                    Child           = new TextBlock
                    {
                        Text       = Tr.Get("badge_default"),
                        FontSize   = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Br(C_SUCCESS)
                    }
                };
                row3.Children.Add(defBadge);
            }
            inner.Children.Add(row3);

            frame.Child = inner;

            var idxCopy = idx;
            var normalBg     = isConnected ? ParseColor("#081F14") : isSelected ? C_ACTIVE : Colors.Transparent;
            var normalBorder = isConnected ? ParseColor("#0F3D28") : isSelected ? C_BORDER : ParseColor("#0D1830");
            var hoverBg      = ParseColor("#0D1830");
            var hoverBorder  = C_BORDER;

            DispatcherTimer? frameAnimTimer = null;
            double frameAnimProg = 0;
            bool frameHovering = false;

            void AnimateFrame()
            {
                frameAnimTimer?.Stop();
                frameAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                frameAnimTimer.Tick += (_, _) =>
                {
                    bool sel  = _selectedIdx == idxCopy;
                    bool conn = _connectedIdx == idxCopy;
                    if (sel || conn) { frameAnimTimer!.Stop(); return; }

                    frameAnimProg = frameHovering
                        ? Math.Min(1, frameAnimProg + 0.14)
                        : Math.Max(0, frameAnimProg - 0.14);

                    var bgColor = Color.FromArgb(
                        (byte)(hoverBg.A * frameAnimProg),
                        hoverBg.R, hoverBg.G, hoverBg.B);
                    frame.Background  = Br(bgColor);
                    frame.BorderBrush = Br(LerpColor(normalBorder, hoverBorder, frameAnimProg));

                    if ((frameHovering && frameAnimProg >= 1) || (!frameHovering && frameAnimProg <= 0))
                    {
                        if (!frameHovering) frame.Background = Brushes.Transparent;
                        frameAnimTimer!.Stop();
                    }
                };
                frameAnimTimer.Start();
            }

            frame.MouseEnter += (_, _) =>
            {
                if (_selectedIdx == idxCopy) return;
                if (_connectedIdx == idxCopy)
                {
                    frame.Background = Br(ParseColor("#081F14"));
                    return;
                }
                frameHovering = true;
                AnimateFrame();
            };
            frame.MouseLeave += (_, _) =>
            {
                bool sel  = _selectedIdx == idxCopy;
                bool conn = _connectedIdx == idxCopy;
                if (sel && conn)
                {
                    frame.Background  = Br(ParseColor("#081F14"));
                    frame.BorderBrush = Br(ParseColor("#0F3D28"));
                    return;
                }
                if (sel)
                {
                    frame.Background  = Br(C_ACTIVE);
                    frame.BorderBrush = Br(C_BORDER);
                    return;
                }
                if (conn)
                {
                    frame.Background  = Brushes.Transparent;
                    frame.BorderBrush = Br(ParseColor("#0F3D28"));
                    return;
                }
                frameHovering = false;
                AnimateFrame();
            };
            frame.MouseLeftButtonDown += (_, _) => SelectServer(idx);
            frame.MouseRightButtonUp  += (_, e) => { ShowServerContextMenu(idx); e.Handled = true; };

            _serverFrames[idx] = frame;
            _serverListPanel.Children.Add(frame);
        }
        UpdateTrayMenu();
    }

    private void ShowServerContextMenu(int idx)
    {
        if (_serverContextPopup != null) _serverContextPopup.IsOpen = false;

        if (idx < 0 || idx >= _servers.Count) return;

        bool isConnected = idx == _connectedIdx && _proxyProcess != null;
        bool isDefault   = _servers[idx].Name == _settings.DefaultServer;

        var popupBg  = ParseColor("#0A1020");
        var sepColor = ParseColor("#1A2640");

        var sp = new StackPanel { Width = 162 };

        string connText = isConnected ? Tr.Get("ctx_disconnect") : Tr.Get("ctx_connect");
        Color  connClr  = isConnected ? C_DANGER : C_SUCCESS;
        sp.Children.Add(MakeCtxItem("", connText, connClr, () =>
        {
            _serverContextPopup!.IsOpen = false;
            SelectServer(idx);
            if (_proxyProcess != null && _connectedIdx != idx)
            {
                StopProxy();
                _connectBtn.IsEnabled = false;
                ShowConnecting();
                Task.Run(ConnectWorker);
            }
            else
            {
                ToggleConnection();
            }
        }));

        sp.Children.Add(new Border
        {
            Height           = 1,
            Background       = Br(sepColor),
            Margin           = new Thickness(8, 2, 8, 2),
            IsHitTestVisible = false
        });

        string defText = isDefault ? Tr.Get("ctx_is_default") : Tr.Get("ctx_set_default");
        Color  defClr  = isDefault ? ParseColor("#10b981") : C_TXT;
        sp.Children.Add(MakeCtxItem("", defText, defClr, () =>
        {
            _serverContextPopup!.IsOpen = false;
            SelectServer(idx);
            SetCurrentDefault();
        }));

        sp.Children.Add(new Border
        {
            Height           = 1,
            Background       = Br(sepColor),
            Margin           = new Thickness(8, 2, 8, 2),
            IsHitTestVisible = false
        });

        sp.Children.Add(MakeCtxItem("", Tr.Get("ctx_qr"), ParseColor("#818CF8"), () =>
        {
            _serverContextPopup!.IsOpen = false;
            ShowQrWindow(idx);
        }));

        sp.Children.Add(new Border
        {
            Height           = 1,
            Background       = Br(sepColor),
            Margin           = new Thickness(8, 2, 8, 2),
            IsHitTestVisible = false
        });

        sp.Children.Add(MakeCtxItem("", Tr.Get("ctx_delete"), C_DANGER, () =>
        {
            _serverContextPopup!.IsOpen = false;
            SelectServer(idx);
            DeleteCurrentServer();
        }));

        var card = new Border
        {
            Background      = Br(ParseColor("#0A1020")),
            BorderBrush     = Br(ParseColor("#1A2640")),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(12),
            Padding         = new Thickness(5),
            Child           = sp,
            Effect          = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color       = Colors.Black,
                BlurRadius  = 32,
                ShadowDepth = 6,
                Opacity     = 0.65
            }
        };

        _serverContextPopup = new Popup
        {
            Child              = card,
            StaysOpen          = false,
            Placement          = PlacementMode.MousePoint,
            AllowsTransparency = true,
            PopupAnimation     = PopupAnimation.Fade,
            IsOpen             = true
        };
    }

    private void ShowQrWindow(int idx)
    {
        if (idx < 0 || idx >= _servers.Count) return;
        var sv  = _servers[idx];
        var uri = LinkParser.ToShareUri(sv);
        if (string.IsNullOrEmpty(uri)) return;

        var qrImage = new System.Windows.Controls.Image
        {
            Width  = 240, Height = 240,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var spinner = new TextBlock
        {
            Text                = "Loading QR code...",
            Foreground          = Br(ParseColor("#A1A1AA")),
            FontSize            = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment       = TextAlignment.Center
        };
        var errorTb = new TextBlock
        {
            Foreground          = Br(ParseColor("#F87171")),
            FontSize            = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment       = TextAlignment.Center,
            TextWrapping        = TextWrapping.Wrap,
            MaxWidth            = 260,
            Visibility          = Visibility.Collapsed
        };
        var qrBorder = new Border
        {
            Background          = Brushes.White,
            Padding             = new Thickness(10),
            CornerRadius        = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child               = qrImage,
            Visibility          = Visibility.Collapsed
        };
        var copyBtn = new Border
        {
            Background          = Br(ParseColor("#6366F1")),
            CornerRadius        = new CornerRadius(8),
            Padding             = new Thickness(18, 8, 18, 8),
            Cursor              = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child               = new TextBlock { Text = Tr.Get("copy_link"), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.Medium }
        };
        bool copyDone = false;
        copyBtn.MouseLeftButtonUp += (_, _) =>
        {
            try { System.Windows.Clipboard.SetText(uri); } catch { }
            if (!copyDone) { copyDone = true; ((TextBlock)copyBtn.Child).Text = Tr.Get("copied"); copyBtn.Background = Br(ParseColor("#22C55E")); }
        };
        copyBtn.MouseEnter += (_, _) => { if (!copyDone) copyBtn.Background = Br(ParseColor("#818CF8")); };
        copyBtn.MouseLeave += (_, _) => { copyBtn.Background = Br(copyDone ? ParseColor("#22C55E") : ParseColor("#6366F1")); };

        var stack = new StackPanel { Margin = new Thickness(28), Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock { Text = sv.Name, Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 280, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new Border { Height = 14 });
        stack.Children.Add(spinner);
        stack.Children.Add(errorTb);
        stack.Children.Add(qrBorder);
        stack.Children.Add(new Border { Height = 12 });
        stack.Children.Add(new TextBlock { Text = "Scan in v2rayNG, Shadowrocket, Streisand...", Foreground = Br(ParseColor("#71717A")), FontSize = 11, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new Border { Height = 8 });
        stack.Children.Add(new TextBlock
        {
            Text = "⚠ " + (_settings.Language == "ru"
                ? "api.qrserver.com"
                : "api.qrserver.com"),
            Foreground = Br(ParseColor("#F59E0B")), FontSize = 10,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 300
        });
        stack.Children.Add(new Border { Height = 6 });
        stack.Children.Add(copyBtn);

        var win = new Window
        {
            Title = $"QR Code — {sv.Name}", SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
            ResizeMode = ResizeMode.NoResize, Background = Br(ParseColor("#07090F")),
            WindowStyle = WindowStyle.ToolWindow, MinWidth = 340,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI")
        };
        win.Content = stack;
        win.Show();

        Task.Run(async () =>
        {
            try
            {
                var encoded = Uri.EscapeDataString(uri);
                var apiUrl  = $"https://api.qrserver.com/v1/create-qr-code/?size=480x480&ecc=M&data={encoded}";
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var bytes = await http.GetByteArrayAsync(apiUrl);
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new System.IO.MemoryStream(bytes);
                bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit(); bmp.Freeze();
                Dispatcher.Invoke(() =>
                {
                    qrImage.Source = bmp;
                    spinner.Visibility  = Visibility.Collapsed;
                    qrBorder.Visibility = Visibility.Visible;
                    win.SizeToContent   = SizeToContent.WidthAndHeight;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    spinner.Visibility = Visibility.Collapsed;
                    errorTb.Text       = $"Failed to load QR:\n{ex.Message}";
                    errorTb.Visibility = Visibility.Visible;
                });
            }
        });
    }

    private Border MakeCtxItem(string icon, string text, Color fg, Action onClick)
    {
        var normalBg = Brushes.Transparent;
        var hoverBg  = Br(ParseColor("#111E38"));

        var textTb = new TextBlock
        {
            Text              = text,
            FontSize          = 13,
            Foreground        = Br(fg),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily        = new FontFamily("Segoe UI"),
            FontWeight        = FontWeights.Normal
        };

        var item = new Border
        {
            Child        = textTb,
            Padding      = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(7),
            Background   = normalBg,
            Cursor       = Cursors.Hand
        };

        item.MouseEnter        += (_, _) => item.Background = hoverBg;
        item.MouseLeave        += (_, _) => item.Background = normalBg;
        item.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };

        return item;
    }

    private void SelectServer(int index)
    {
        if (_editingName) CancelEditName();
        if (index < 0 || index >= _servers.Count) return;
        if (_selectedIdx >= 0 && _selectedIdx < _serverFrames.Count)
            _serverFrames[_selectedIdx].Background = Brushes.Transparent;
        _selectedIdx = index;
        _serverFrames[index].Background = Br(C_ACTIVE);

        var sv = _servers[index];
        _nameLabel.Text = sv.Name;
        _serverTitleLabel.Text = sv.Name;
        UpdateHostDisplay();
        _pingLabel.Text = "";

        _serverInfoCard.BorderBrush = Br(C_BORDER);
        _serverInfoCard.Background  = (_proxyProcess != null && _connectedIdx == index)
            ? MakeConnectedCardBrush() : Br(C_CARD);

        _pingBtn.IsEnabled = true;
        _connectBtn.IsEnabled = true;
        _deleteBtn.IsEnabled = true;
        _defaultBtn.IsEnabled = true;
        _defaultBtn.Foreground = sv.Name == _settings.DefaultServer ? Br(C_SUCCESS) : Br(C_DANGER);
        UpdateTrayMenu();
    }

    private void UpdateHostDisplay()
    {
        if (_selectedIdx < 0 || _selectedIdx >= _servers.Count) return;
        var sv = _servers[_selectedIdx];
        _hostLabel.Text = _hideIp ? MaskHost(sv.Host) : $"{sv.Host}:{sv.Port}";
    }

    private static string MaskHost(string host)
    {
        if (host.Length <= 4) return "***";
        return host[..2] + new string('*', host.Length - 4) + host[^2..];
    }

    private void StartEditName()
    {
        if (_selectedIdx < 0 || _editingName) return;
        _editingName = true;

        var parent = _nameLabel.Parent as Border;
        if (parent == null) return;

        _editNameBox = new TextBox
        {
            Text = _servers[_selectedIdx].Name,
            Background = Br(ParseColor("#0D1526")),
            Foreground = Br(C_TXT),
            BorderBrush = Br(C_ACCENT),
            BorderThickness = new Thickness(1),
            CaretBrush = Br(C_TXT),
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Padding = new Thickness(8, 4, 8, 4),
            MinWidth = 160
        };
        _editNameBox.KeyDown += EditNameBox_KeyDown;
        _editNameBox.LostFocus += EditNameBox_LostFocus;

        parent.Child = _editNameBox;
        _editNameBox.Focus();
        _editNameBox.SelectAll();
    }

    private void EditNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ApplyEditName();
        else if (e.Key == Key.Escape)
            CancelEditName();
    }

    private void EditNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyEditName();
    }

    private void ApplyEditName()
    {
        if (!_editingName || _selectedIdx < 0 || _editNameBox == null) return;
        string newName = _editNameBox.Text.Trim();
        if (!string.IsNullOrEmpty(newName))
        {
            var oldName = _servers[_selectedIdx].Name;
            if (newName != oldName)
            {
                _servers[_selectedIdx].Name = newName;
                if (_settings.DefaultServer == oldName)
                {
                    _settings.DefaultServer = newName;
                    SaveSettings();
                }
                SaveServers();
                RefreshServerList();
                _nameLabel.Text = newName;
                _serverTitleLabel.Text = newName;
                _defaultBtn.Foreground = newName == _settings.DefaultServer ? Br(C_SUCCESS) : Br(C_DANGER);
            }
        }
        CancelEditName();
    }

    private void CancelEditName()
    {
        if (!_editingName) return;
        _editingName = false;
        if (_editNameBox != null)
        {
            var parent = _editNameBox.Parent as Border;
            if (parent != null)
                parent.Child = _nameLabel;
            _editNameBox = null;
        }
    }

    private void ToggleConnection()
    {
        if (_proxyProcess == null)
        {
            if (_selectedIdx < 0) return;
            _connectBtn.IsEnabled = false;
            ShowConnecting();
            Task.Run(ConnectWorker);
        }
        else
        {
            StopProxy();
        }
    }

    private void ShowToast(string title, string message, ToastType type)
    {
        if (!_settings.EnableNotifications) return;
        Dispatcher.Invoke(() =>
        {
            var toast = new ToastWindow(title, message, type);
            toast.Show();
        });
    }

    private void ConnectWorker()
    {
        var configPath = Path.Combine(Paths.DataDir, $"cfg_{Path.GetRandomFileName()}.json");
        try
        {
            var sv = _servers[_selectedIdx];
            string? tunIfName = null;
            Paths.RotateLogFile(Paths.LogFile);

            if (_settings.UseTunMode && _autoReconnectAttempts > 0)
                System.Threading.Thread.Sleep(2000);

            SingBoxConfig.Generate(sv, _settings, _routingTextBacking, _proxyPort, Paths.LogFile, configPath, out tunIfName);

            var singBox = Path.Combine(Paths.Base, "core", "sing-box.exe");
            if (!File.Exists(singBox))
            {
                Dispatcher.Invoke(() => OnConnectFailed("sing-box.exe не найден в папке core"));
                return;
            }

            if (_settings.UseTunMode && !IsAdministrator())
            {
                Dispatcher.Invoke(() => OnConnectFailed("TUN-режим требует прав администратора.\nПерезапустите приложение от имени администратора."));
                return;
            }

            var psi = new ProcessStartInfo(singBox, $"run -c \"{configPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError  = true,
                RedirectStandardOutput = true
            };
            psi.EnvironmentVariables["ENABLE_DEPRECATED_LEGACY_DNS_SERVERS"] = "true";
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var logBuffer = new System.Text.StringBuilder();
            int warnRecoveryActive = 0;

            void ReadProcStream(StreamReader reader)
            {
                Task.Run(() =>
                {
                    try
                    {
                        while (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            if (line == null) break;
                            lock (logBuffer) logBuffer.AppendLine(line);

                            if (_settings.UseTunMode &&
                                line.Contains("open interface take too much time", StringComparison.OrdinalIgnoreCase)
                                && Interlocked.CompareExchange(ref warnRecoveryActive, 1, 0) == 0)
                            {
                                Paths.AppendLog(Paths.LogFile,
                                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [TaaProxy] TUN WARN: запускаем мониторинг восстановления (до 10с)...\n{line}\n");

                                Task.Run(() =>
                                {
                                    try
                                    {
                                        System.Threading.Thread.Sleep(1000);
                                        if (proc.HasExited) { Interlocked.Exchange(ref warnRecoveryActive, 0); return; }

                                        int baselineLog;
                                        TimeSpan baselineCpu;
                                        lock (logBuffer) { baselineLog = logBuffer.Length; }
                                        try { baselineCpu = proc.TotalProcessorTime; }
                                        catch { baselineCpu = TimeSpan.Zero; }

                                        for (int i = 0; i < 18; i++)
                                        {
                                            System.Threading.Thread.Sleep(500);
                                            if (proc.HasExited) { Interlocked.Exchange(ref warnRecoveryActive, 0); return; }

                                            int curLog;
                                            TimeSpan curCpu;
                                            lock (logBuffer) { curLog = logBuffer.Length; }
                                            try { curCpu = proc.TotalProcessorTime; }
                                            catch { curCpu = baselineCpu; }

                                            bool logGrew = curLog > baselineLog;
                                            bool cpuGrew = curCpu > baselineCpu + TimeSpan.FromMilliseconds(5);

                                            if (logGrew || cpuGrew)
                                            {
                                                Paths.AppendLog(Paths.LogFile,
                                                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [TaaProxy] TUN WARN: " +
                                                    $"TUN самовосстановился (log+{curLog - baselineLog}б, " +
                                                    $"cpu+{(curCpu - baselineCpu).TotalMilliseconds:F0}мс), процесс оставлен.\n");
                                                Interlocked.Exchange(ref warnRecoveryActive, 0);
                                                return;
                                            }
                                        }

                                        Paths.AppendLog(Paths.LogFile,
                                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [TaaProxy] TUN WARN: sing-box завис (нет логов и CPU-активности 9с), принудительный перезапуск.\n");
                                        try { proc.Kill(); } catch { }
                                        Interlocked.Exchange(ref warnRecoveryActive, 0);
                                    }
                                    catch { Interlocked.Exchange(ref warnRecoveryActive, 0); }
                                });
                            }
                        }
                    }
                    catch { }
                });
            }

            proc.Start();
            ReadProcStream(proc.StandardOutput);
            ReadProcStream(proc.StandardError);

            Thread.Sleep(3000);

            if (proc.HasExited)
            {
                try
                {
                    var diagText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] sing-box завершился с кодом {proc.ExitCode}\n";
                    string buffered;
                    lock (logBuffer) buffered = logBuffer.ToString();
                    if (!string.IsNullOrWhiteSpace(buffered)) diagText += buffered;
                    Paths.AppendLog(Paths.LogFile, diagText);
                }
                catch { }
                Dispatcher.Invoke(() => OnConnectFailed("Процесс sing-box завершился сразу"));
                return;
            }

            if (!_settings.UseTunMode)
            {
                SystemProxy.Set(true, _proxyPort);
            }

            _proxyProcess = proc;
            _connectedIdx = _selectedIdx;

            Dispatcher.Invoke(() =>
            {
                _connectBtn.Content = Tr.Get("btn_disconnect");
                _connectBtn.Background = Br(C_DANGER);
                _connectBtn.IsEnabled = true;
                _serverInfoCard.BorderBrush = Br(C_BORDER);
                _serverInfoCard.Background  = MakeConnectedCardBrush();
                _autoReconnectAttempts = 0;
                if (_titleStatusDot != null) _titleStatusDot.Fill = Br(C_SUCCESS);
                if (_titleConnectedLabel != null && _connectedIdx >= 0 && _connectedIdx < _servers.Count)
                    _titleConnectedLabel.Text = _servers[_connectedIdx].Name;
                SmoothStatusTransition(Tr.Get("status_connected"), C_SUCCESS, null);
                UpdateTray(connected: true);
                SetWindowIcon(true);
                RefreshServerList();
                StartSpeedMeter();

                ShowToast(Tr.Get("notification_connected"), sv.Name, ToastType.Success);
            });

            StartMonitors();
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => OnConnectFailed(ex.Message));
        }
        finally
        {
            try { if (File.Exists(configPath)) File.Delete(configPath); } catch { }
        }
    }

    private void StopProxy()
    {
        _dotTimer?.Stop();
        _monitorCts.Cancel();
        _reconnectCts.Cancel();
        StopSpeedMeter();

        if (!_settings.UseTunMode)
            SystemProxy.Set(false);
        KillSwitch.Set(false);

        if (_proxyProcess != null)
        {
            try { _proxyProcess.Kill(); _proxyProcess.WaitForExit(3000); } catch { }
            _proxyProcess = null;
        }

        _connectedIdx = -1;
        _autoReconnectAttempts = 0;
        _noNetwork = false;

        if (_selectedIdx >= 0 && _selectedIdx < _servers.Count)
        {
            var selSv = _servers[_selectedIdx];
            _nameLabel.Text = selSv.Name;
            _serverTitleLabel.Text = selSv.Name;
        }

        SmoothStatusTransition(Tr.Get("status_disconnected"), C_MUTED);
        _connectBtn.Content = Tr.Get("btn_connect");
        _connectBtn.Background = Br(C_ACCENT);
        _connectBtn.IsEnabled = true;
        _serverInfoCard.BorderBrush = Br(C_BORDER);
        _serverInfoCard.Background  = Br(C_CARD);
        if (_titleStatusDot != null) _titleStatusDot.Fill = Br(C_MUTED);
        if (_titleConnectedLabel != null) _titleConnectedLabel.Text = "";
        UpdateTray(connected: false);
        SetWindowIcon(false);
        RefreshServerList();

        ShowToast(Tr.Get("notification_disconnected"), Tr.Get("status_disconnected"), ToastType.Info);
    }

    private void OnConnectFailed(string reason = "")
    {
        _proxyProcess = null;
        SetWindowIcon(false);
        if (_settings.AutoReconnect)
        {
            ScheduleReconnect();
            return;
        }
        SmoothStatusTransition(Tr.Get("status_error"), C_DANGER);
        _connectBtn.Content = Tr.Get("btn_connect");
        _connectBtn.Background = Br(C_ACCENT);
        _connectBtn.IsEnabled = true;
        _serverInfoCard.BorderBrush = Br(C_BORDER);
        _serverInfoCard.Background  = Br(C_CARD);
        if (_titleStatusDot != null) _titleStatusDot.Fill = Br(C_DANGER);
        UpdateTray(connected: false);
    }

    private void ScheduleReconnect()
    {
        _reconnectCts.Cancel();
        _reconnectCts = new CancellationTokenSource();
        var ct = _reconnectCts.Token;

        int delaySeconds = (_settings.UseTunMode) ? 7 : 3;
        Task.Run(async () =>
        {
            for (int i = delaySeconds; i > 0; i--)
            {
                if (ct.IsCancellationRequested) return;
                var sec = i;
                Dispatcher.Invoke(() =>
                {
                    if (_proxyProcess != null) return;
                    _statusLabel.Text = $"{Tr.Get("status_reconnecting")} {sec} сек...";
                });
                await Task.Delay(1000, ct).ContinueWith(_ => { });
            }

            if (ct.IsCancellationRequested) return;

            Dispatcher.Invoke(() =>
            {
                if (_proxyProcess != null) return;
                _connectBtn.IsEnabled = false;
                ShowConnecting();
                Task.Run(ConnectWorker);
            });
        }, ct);
    }

    private bool TryNextServer()
    {
        if (_autoReconnectAttempts >= _servers.Count) { _autoReconnectAttempts = 0; return false; }
        _autoReconnectAttempts++;
        var next = (_selectedIdx + 1) % _servers.Count;
        SelectServer(next);
        SmoothStatusTransition($"{Tr.Get("status_connecting")} [{_autoReconnectAttempts}/{_servers.Count}]", C_MUTED);
        Dispatcher.BeginInvoke(ToggleConnection, DispatcherPriority.Background);
        return true;
    }


    private void StartMonitors()
    {
        _monitorCts = new CancellationTokenSource();
        var cts = _monitorCts;
        Task.Run(() => MonitorProxy(cts.Token));
        Task.Run(() => MonitorNetwork(cts.Token));
    }

    private void MonitorProxy(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Thread.Sleep(1000);
            var proc = _proxyProcess;
            if (proc == null) return;
            if (proc.HasExited)
            {
                SystemProxy.Set(false);
                _proxyProcess = null;
                Dispatcher.Invoke(OnProxyDied);
                return;
            }
        }
    }

    private void MonitorNetwork(CancellationToken ct)
    {
        if (_settings.UseTunMode) return;

        while (!ct.IsCancellationRequested)
        {
            Thread.Sleep(5000);
            if (_proxyProcess == null) return;
            bool hasNet = CheckInternet();
            Dispatcher.Invoke(() =>
            {
                if (!hasNet && !_noNetwork)
                {
                    _noNetwork = true;
                    SmoothStatusTransition(Tr.Get("status_no_network"), C_DANGER);
                }
                else if (hasNet && _noNetwork)
                {
                    _noNetwork = false;
                    SmoothStatusTransition(Tr.Get("status_connected"), C_SUCCESS, null);
                }
            });
        }
    }

    private void OnProxyDied()
    {
        _connectedIdx = -1;
        SetWindowIcon(false);
        ShowToast(Tr.Get("notification_server_died"), Tr.Get("status_error"), ToastType.Error);
        if (_settings.AutoReconnect)
        {
            if (_settings.KillSwitch) KillSwitch.Set(true);
            ScheduleReconnect();
            return;
        }
        if (_settings.KillSwitch)
        {
            KillSwitch.Set(true);
            SmoothStatusTransition(Tr.Get("kill_switch_active"), C_DANGER);
        }
        else SmoothStatusTransition(Tr.Get("status_error"), C_DANGER);
        _connectBtn.Content = Tr.Get("btn_connect");
        _connectBtn.Background = Br(C_ACCENT);
        _serverInfoCard.BorderBrush = Br(C_BORDER);
        _serverInfoCard.Background  = Br(C_CARD);
        UpdateTray(connected: false);
        RefreshServerList();
    }

    private static bool CheckInternet()
    {
        try { using var c = new TcpClient(); c.Connect("8.8.8.8", 53); return true; }
        catch { return false; }
    }

    private DispatcherTimer? _fadeTimer;
    private double _fadeProgress = 0;
    private string _fadeTargetText = "";
    private Color _fadeTargetColor;
    private Action? _fadeCallback;

    private void SmoothStatusTransition(string newText, Color newColor, Action? callback = null)
    {
        _dotTimer?.Stop();
        _fadeTimer?.Stop();

        _fadeTargetText = newText;
        _fadeTargetColor = newColor;
        _fadeCallback = callback;
        _fadeProgress = 0;

        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _fadeTimer.Tick += FadeTick;
        _fadeTimer.Start();
    }

    private void FadeTick(object? s, EventArgs e)
    {
        _fadeProgress += 1.0 / 20;
        if (_fadeProgress >= 1.0)
        {
            _fadeTimer?.Stop();
            _statusLabel.Text = _fadeTargetText;
            _statusLabel.Foreground = Br(_fadeTargetColor);
            _statusColor = _fadeTargetColor;
            _fadeCallback?.Invoke();
            return;
        }
        if (_fadeProgress < 0.5)
        {
            double t = _fadeProgress * 2;
            _statusLabel.Foreground = Br(LerpColor(_statusColor, C_CARD, t));
        }
        else
        {
            _statusLabel.Text = _fadeTargetText;
            double t = (_fadeProgress - 0.5) * 2;
            _statusLabel.Foreground = Br(LerpColor(C_CARD, _fadeTargetColor, t));
        }
    }

    private static Color LerpColor(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private void ShowConnecting()
    {
        _dotPhase = 0;
        SmoothStatusTransition(Tr.Get("status_connecting"), C_MUTED,
            callback: () =>
            {
                _dotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _dotTimer.Tick += (_, _) =>
                {
                    _dotPhase = (_dotPhase + 1) % 4;
                    var dots = new string('.', _dotPhase);
                    _statusLabel.Text = Tr.Get("status_connecting") + dots;
                };
                _dotTimer.Start();
            });
    }

    private void StartConnectedAnimation() { }

    private void AddFromClipboard()
    {
        string raw;
        try { raw = Clipboard.GetText().Trim(); } catch { return; }
        if (string.IsNullOrEmpty(raw)) return;

        var direct = new[] { "vless://", "hysteria2://", "ss://", "trojan://" };
        if (direct.Any(s => raw.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            var sv = LinkParser.Parse(raw);
            if (sv != null) { _servers.Add(sv); SaveServers(); RefreshServerList(); ShowPanel("servers"); }
            else MessageBox.Show("Не удалось распарсить ссылку.", Tr.Get("error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            Task.Run(() => FetchAndImport(raw));
        else
            MessageBox.Show("Неизвестный формат.", Tr.Get("error"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ImportServersFromFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Config files (*.txt;*.conf;*.cfg;*.json)|*.txt;*.conf;*.cfg;*.json|All files (*.*)|*.*",
            Title = "Import servers from file"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            string content = File.ReadAllText(dlg.FileName);
            List<ServerModel> newServers = new List<ServerModel>();

            var links = LinkParser.ExtractAll(content);
            if (links.Any())
            {
                newServers.AddRange(links);
            }
            else
            {
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content.Trim()));
                    links = LinkParser.ExtractAll(decoded);
                    if (links.Any())
                        newServers.AddRange(links);
                    else
                    {
                        MessageBox.Show("No valid server links found in file.", Tr.Get("error"));
                        return;
                    }
                }
                catch
                {
                    MessageBox.Show("No valid server links found in file.", Tr.Get("error"));
                    return;
                }
            }

            if (newServers.Count == 0)
            {
                MessageBox.Show("No servers found in file.", Tr.Get("error"));
                return;
            }

            foreach (var sv in newServers)
                _servers.Add(sv);
            SaveServers();
            RefreshServerList();
            ShowPanel("servers");
            MessageBox.Show($"Added {newServers.Count} server(s).", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error reading file: {ex.Message}", Tr.Get("error"));
        }
    }

    private async Task FetchAndImport(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Dispatcher.Invoke(() => MessageBox.Show(
                _settings.Language == "ru"
                    ? "Разрешены только ссылки http:// и https://"
                    : "Only http:// and https:// URLs are allowed.",
                Tr.Get("error"), MessageBoxButton.OK, MessageBoxImage.Warning));
            return;
        }
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            hc.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            var resp = await hc.GetStringAsync(url);

            var list = LinkParser.ExtractAll(resp);
            if (!list.Any())
            {
                try { var dec = Encoding.UTF8.GetString(Convert.FromBase64String(resp.Trim())); list = LinkParser.ExtractAll(dec); }
                catch { }
            }
            if (!list.Any()) { Dispatcher.Invoke(() => MessageBox.Show("Ссылки не найдены.", Tr.Get("error"))); return; }

            Dispatcher.Invoke(() =>
            {
                foreach (var sv in list) _servers.Add(sv);
                SaveServers(); RefreshServerList(); ShowPanel("servers");
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => MessageBox.Show($"Ошибка загрузки URL:\n{ex.Message}", Tr.Get("error")));
        }
    }

    private void DeleteCurrentServer()
    {
        if (_selectedIdx < 0) return;
        if (_proxyProcess != null && _connectedIdx == _selectedIdx) StopProxy();
        var name = _servers[_selectedIdx].Name;
        if (_settings.DefaultServer == name) { _settings.DefaultServer = ""; SaveSettings(); }
        _servers.RemoveAt(_selectedIdx);
        _selectedIdx = -1;
        _nameLabel.Text = Tr.Get("server_not_selected");
        _serverTitleLabel.Text = Tr.Get("server_not_selected");
        _hostLabel.Text = "—";
        _pingLabel.Text = "";
        _connectBtn.IsEnabled = false;
        _pingBtn.IsEnabled = false;
        _deleteBtn.IsEnabled = false;
        _defaultBtn.IsEnabled = false;
        SaveServers();
        RefreshServerList();
    }

    private void SetCurrentDefault()
    {
        if (_selectedIdx < 0) return;
        _settings.DefaultServer = _servers[_selectedIdx].Name;
        _defaultBtn.Foreground = Br(C_SUCCESS);
        SaveSettings();
        RefreshServerList();
    }

    private async Task CheckPingAsync()
    {
        if (_selectedIdx < 0) return;
        var sv = _servers[_selectedIdx];
        _pingBtn.IsEnabled = false;

        _pingDotPhase = 0;
        var baseText = _settings.Language == "ru" ? "Проверка" : "Checking";
        _pingLabel.Text = baseText;
        _pingLabel.Opacity = 1;
        _pingLabel.Foreground = Br(C_MUTED);

        _pingDotsTimer?.Stop();
        _pingDotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _pingDotsTimer.Tick += (_, _) =>
        {
            _pingDotPhase = (_pingDotPhase + 1) % 4;
            _pingLabel.Text = baseText + new string('.', _pingDotPhase);
        };
        _pingDotsTimer.Start();

        var ms = await Task.Run(() => MeasurePing(sv.Host, sv.Port));

        _pingDotsTimer?.Stop();
        _pingDotsTimer = null;
        _pingBtn.IsEnabled = true;

        string resultText;
        Color targetColor;
        if (ms < 0)
        {
            resultText = "timeout";
            targetColor = C_DANGER;
        }
        else
        {
            resultText = $"{ms} ms";
            targetColor = ms < 150 ? C_SUCCESS : ms < 400 ? ParseColor("#F59E0B") : C_DANGER;
        }

        AnimatePingResult(resultText, targetColor);
    }

    private void AnimatePingResult(string text, Color targetColor)
    {
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = easeOut
        };
        fadeOut.Completed += (_, _) =>
        {
            _pingLabel.Text = text;
            _pingLabel.Foreground = new SolidColorBrush(targetColor);

            var easeIn = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(340))
            {
                EasingFunction = easeIn
            };
            _pingLabel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };
        _pingLabel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private static long MeasurePing(string host, int port)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var tc = new TcpClient();
            tc.Connect(host, port);
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
        catch { return -1; }
    }

    private void StartSpeedMeter()
    {
        (_lastRxBytes, _lastTxBytes) = GetNetworkBytes();
        _lastSpeedTick = DateTime.UtcNow;
        _speedRow.Visibility = Visibility.Visible;
        _speedLabel.Text = "—";
        UpdateHeaderSpeed("—", "—", active: false);

        _speedTimer?.Stop();
        _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _speedTimer.Tick += (_, _) => UpdateSpeedLabel();
        _speedTimer.Start();
    }

    private void StopSpeedMeter()
    {
        _speedTimer?.Stop();
        _speedTimer = null;
        _speedLabel.Text = "↓ 0 B/s   ↑ 0 B/s";
        _speedLabel.Foreground = Br(C_MUTED);
        UpdateHeaderSpeed("—", "—", active: false);
    }

    private void UpdateSpeedLabel()
    {
        var (rx, tx) = GetNetworkBytes();
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastSpeedTick).TotalSeconds;
        if (elapsed < 0.1) return;

        var downBps = (long)((rx - _lastRxBytes) / elapsed);
        var upBps   = (long)((tx - _lastTxBytes) / elapsed);
        _lastRxBytes = rx;
        _lastTxBytes = tx;
        _lastSpeedTick = now;

        if (downBps < 0) downBps = 0;
        if (upBps   < 0) upBps   = 0;

        _speedLabel.Text = $"↓ {FormatSpeed(downBps)}   ↑ {FormatSpeed(upBps)}";

        bool active = downBps > 1024 || upBps > 1024;
        _speedLabel.Foreground = active ? Br(C_SUCCESS) : Br(C_MUTED);

        UpdateHeaderSpeed(FormatSpeed(downBps), FormatSpeed(upBps), active);
    }

    private void UpdateHeaderSpeed(string down, string up, bool active)
    {
        if (_headerSpeedLabel?.Tag is not (TextBlock downLbl, TextBlock upLbl, TextBlock downArrow, TextBlock upArrow))
            return;
        var color = active ? Br(C_SUCCESS) : Br(C_MUTED);
        downLbl.Text = down;
        upLbl.Text   = up;
        downLbl.Foreground = color;
        upLbl.Foreground   = color;
        downArrow.Foreground = color;
        upArrow.Foreground   = color;
    }

    private static (long rx, long tx) GetNetworkBytes()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                var stats = ni.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
        }
        catch { }
        return (rx, tx);
    }

    private static string FormatSpeed(long bps)
    {
        if (bps >= 1_000_000_000) return $"{bps / 1_000_000_000.0:F1} GB/s";
        if (bps >= 1_000_000)     return $"{bps / 1_000_000.0:F1} MB/s";
        if (bps >= 1_000)         return $"{bps / 1_000.0:F0} KB/s";
        return $"{bps} B/s";
    }
    private void OnSplitToggle()
    {
        SaveCurrentRoutes();
        SaveSettings();
        RestartIfNeeded();
        UpdateTrayMenu();
    }

    private void UpdateSplitState()
    {
        _routesCombo.IsEnabled = true;
        if (_proxyRoutingText != null)
            _proxyRoutingText.IsEnabled = true;
        if (_proxyRoutesCombo != null)
            _proxyRoutesCombo.IsEnabled = true;
        if (_proxySaveBtn != null)
            _proxySaveBtn.IsEnabled = true;
    }

    private void RestartIfNeeded()
    {
        if (_proxyProcess != null && _selectedIdx >= 0) { StopProxy(); ToggleConnection(); }
    }

    private void CreateRoutesFile()
    {
        var dlg = new InputDialog(Tr.Get("new_routes_file"), Tr.Get("enter_name"), "new_list.txt");
        dlg.Owner = this;
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
        var name = dlg.Result.EndsWith(".txt") ? dlg.Result : dlg.Result + ".txt";
        var path = Path.Combine(Paths.ListDir, name);
        if (File.Exists(path)) { MessageBox.Show($"Файл {name} уже существует.", Tr.Get("error")); return; }
        File.WriteAllText(path, "");
        RefreshRoutesListForCombos();
        SaveCurrentRoutes();
        _currentRoutesFile = name;
        _routesCombo.SelectedItem = name;
        if (_proxyRoutesCombo != null) _proxyRoutesCombo.SelectedItem = name;
        _routingTextBacking = "";
        if (_proxyRoutingText != null) _proxyRoutingText.Text = "";
        if (_settings.SplitTunneling) RestartIfNeeded();
        UpdateTrayMenu();
    }

    private void DeleteRoutesFile()
    {
        var files = (_routesCombo.ItemsSource as IEnumerable<string>)?.ToList() ?? new();
        if (files.Count <= 1) { MessageBox.Show(Tr.Get("cannot_delete_last"), Tr.Get("error")); return; }
        if (MessageBox.Show(string.Format(Tr.Get("confirm_delete_text"), _currentRoutesFile),
                Tr.Get("confirm_delete"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var deletedFile = _currentRoutesFile;
            File.Delete(Path.Combine(Paths.ListDir, deletedFile));
            RefreshRoutesListForCombos();
            var remaining = files.First(f => f != deletedFile);
            _currentRoutesFile = remaining;
            _routesCombo.SelectedItem = remaining;
            if (_proxyRoutesCombo != null) _proxyRoutesCombo.SelectedItem = remaining;
            LoadRoutesFile(remaining);
            if (_settings.SplitTunneling) RestartIfNeeded();
            UpdateTrayMenu();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Tr.Get("error")); }
    }

    private void RenameRoutesFile()
    {
        var dlg = new InputDialog(Tr.Get("rename_routes_file"), Tr.Get("enter_name"), _currentRoutesFile);
        dlg.Owner = this;
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
        var newName = dlg.Result.EndsWith(".txt") ? dlg.Result : dlg.Result + ".txt";
        if (newName == _currentRoutesFile) return;
        try
        {
            File.Move(Path.Combine(Paths.ListDir, _currentRoutesFile), Path.Combine(Paths.ListDir, newName));
            _currentRoutesFile = newName;
            RefreshRoutesListForCombos();
            _routesCombo.SelectedItem = newName;
            if (_proxyRoutesCombo != null) _proxyRoutesCombo.SelectedItem = newName;
            UpdateTrayMenu();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Tr.Get("error")); }
    }

    private void ImportSitesFromFile()
    {
        var dlg = new OpenFileDialog { Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*", Title = Tr.Get("select_sites_file") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var content = File.ReadAllText(dlg.FileName);
            var baseName = Path.GetFileName(dlg.FileName);
            var dest = Path.Combine(Paths.ListDir, baseName);
            int i = 1;
            while (File.Exists(dest)) dest = Path.Combine(Paths.ListDir, $"{Path.GetFileNameWithoutExtension(baseName)}_{i++}.txt");
            File.WriteAllText(dest, content);
            RefreshRoutesListForCombos();
            _currentRoutesFile = Path.GetFileName(dest);
            _routesCombo.SelectedItem = _currentRoutesFile;
            if (_proxyRoutesCombo != null) _proxyRoutesCombo.SelectedItem = _currentRoutesFile;
            LoadRoutesFile(_currentRoutesFile);
            ShowPanel("servers");
            UpdateTrayMenu();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Tr.Get("error")); }
    }

    private void RefreshSettingsUI()
    {
        _autostartChk!.IsChecked = Autostart.IsEnabled();
        _minimizeChk!.IsChecked = _settings.MinimizeOnClose;
        _killSwitchChk!.IsChecked = _settings.KillSwitch;
        _autoReconnectChk!.IsChecked = _settings.AutoReconnect;
        _debugChk!.IsChecked = _settings.DebugMode;
        _notificationsChk!.IsChecked = _settings.EnableNotifications;
        if (_autoConnectOnStartChk != null) _autoConnectOnStartChk.IsChecked = _settings.AutoConnectOnStart;
        if (_minimizeOnStartupChk != null) _minimizeOnStartupChk.IsChecked = _settings.MinimizeOnStartup;
        if (_tunModeChk != null) _tunModeChk.IsChecked = _settings.UseTunMode;
        _dnsTypeCombo!.SelectedIndex = _settings.DnsType switch { "doh" => 1, "dot" => 2, _ => 0 };
        _dnsAddrBox!.Text = _settings.DnsServer;
        _dnsProxyChk!.IsChecked = _settings.DnsThroughProxy;
        RefreshAppExcListUI();
        RefreshDomExcListUI();
    }

    private void SaveDns()
    {
        _settings.DnsType = _dnsTypeCombo!.SelectedIndex switch { 1 => "doh", 2 => "dot", _ => "system" };
        _settings.DnsServer = _dnsAddrBox!.Text.Trim();
        _settings.DnsThroughProxy = _dnsProxyChk!.IsChecked == true;
        SaveSettings();
        if (_proxyProcess != null) RestartIfNeeded();
        ShowPanel("servers");
    }

    private void TestDns()
    {
        if (_dnsTestLabel == null) return;
        _dnsTestLabel.Text = "...";
        Task.Run(() =>
        {
            bool ok;
            try
            {
                var addr = (_dnsAddrBox?.Text ?? "").Trim().Replace("https://", "").Replace("tls://", "").Split('/')[0].Split(':')[0];
                using var tc = new TcpClient();
                tc.Connect(addr, 443);
                ok = true;
            }
            catch { ok = false; }
            Dispatcher.Invoke(() =>
            {
                _dnsTestLabel.Text = ok ? Tr.Get("dns_test_success") : Tr.Get("dns_test_fail");
                _dnsTestLabel.Foreground = Br(ok ? C_SUCCESS : C_DANGER);
            });
        });
    }

    private void ViewLogs()
    {
        if (File.Exists(Paths.LogFile)) Process.Start(new ProcessStartInfo(Paths.LogFile) { UseShellExecute = true });
        else MessageBox.Show(Tr.Get("log_not_found"));
    }

    private void RestartApp()
    {
        CleanupAndExit(restart: true);
    }

    private void ShowAddAppMenu()
    {
        var menu = new ContextMenu();
        var byPath = new MenuItem { Header = Tr.Get("add_app_by_path") };
        byPath.Click += (_, _) => AddAppByPath();
        var byName = new MenuItem { Header = Tr.Get("add_app_by_name") };
        byName.Click += (_, _) => AddAppByName();
        menu.Items.Add(byPath);
        menu.Items.Add(byName);
        menu.IsOpen = true;
    }

    private void AddAppByPath()
    {
        var dlg = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe|All (*.*)|*.*", Title = Tr.Get("select_exe") };
        if (dlg.ShowDialog(this) != true) return;
        if (_settings.AppExceptions.Any(a => a.ExType == "path" && a.Value.Equals(dlg.FileName, StringComparison.OrdinalIgnoreCase))) return;
        _settings.AppExceptions.Add(new AppException { ExType = "path", Value = dlg.FileName, Name = Path.GetFileName(dlg.FileName) });
        SaveSettings(); RefreshAppExcListUI(); RestartIfNeeded();
    }

    private void AddAppByName()
    {
        var dlg = new ProcessPickerDialog();
        dlg.Owner = this;
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.SelectedProcessName)) return;
        var name = dlg.SelectedProcessName.Trim();
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        if (_settings.AppExceptions.Any(a => a.ExType == "name" && a.Value.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        _settings.AppExceptions.Add(new AppException { ExType = "name", Value = name, Name = name });
        SaveSettings(); RefreshAppExcListUI(); RestartIfNeeded();
    }

    private void RemoveAppException()
    {
        if (_selAppExcIdx < 0 || _selAppExcIdx >= _settings.AppExceptions.Count) return;
        _settings.AppExceptions.RemoveAt(_selAppExcIdx);
        _selAppExcIdx = -1;
        SaveSettings(); RefreshAppExcListUI(); RestartIfNeeded();
    }

    private void RefreshAppExcListUI()
    {
        if (_appExcList == null) return;
        _appExcList.Children.Clear();
        if (!_settings.AppExceptions.Any())
        {
            _appExcList.Children.Add(MakeText(Tr.Get("no_exceptions"), 12, false, C_MUTED));
            _removeAppBtn!.IsEnabled = false; return;
        }
        for (int i = 0; i < _settings.AppExceptions.Count; i++)
        {
            var exc = _settings.AppExceptions[i];
            var idx = i;
            var row = new Border
            {
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                Background = idx == _selAppExcIdx ? Br(C_ACTIVE) : Brushes.Transparent
            };
            var tag = exc.ExType == "path" ? "[Путь]" : "[Имя]";
            row.Child = MakeText($"{tag} {exc.Name}", 13, false, C_TXT);
            row.MouseLeftButtonDown += (_, _) =>
            {
                _selAppExcIdx = idx; _removeAppBtn!.IsEnabled = true;
                RefreshAppExcListUI();
            };
            _appExcList.Children.Add(row);
        }
    }

    private void AddDomainException()
    {
        var dlg = new InputDialog(Tr.Get("add_domain"), Tr.Get("enter_domain"), "");
        dlg.Owner = this;
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Result)) return;
        var dom = dlg.Result.Trim();
        if (string.IsNullOrEmpty(dom)) return;

        var clean = dom.TrimStart('.');
        if (clean.Contains("..") || clean.EndsWith('.') || clean.Length == 0)
        { MessageBox.Show(Tr.Get("domain_invalid"), Tr.Get("error")); return; }

        string stored;
        bool startsWithDot = dom.StartsWith('.');
        string part = startsWithDot ? dom.Substring(1) : dom;
        try
        {
            string puny = _idn.GetAscii(part);
            stored = startsWithDot ? "." + puny : puny;
        }
        catch
        {
            stored = dom;
        }

        if (_settings.DomainExceptions.Contains(stored)) return;
        _settings.DomainExceptions.Add(stored);
        SaveSettings();
        RefreshDomExcListUI();
        RestartIfNeeded();
    }

    private void RemoveDomainException()
    {
        if (_selectedDomainException == null) return;
        _settings.DomainExceptions.Remove(_selectedDomainException);
        _selectedDomainException = null;
        SaveSettings();
        RefreshDomExcListUI();
        if (_removeDomBtn != null) _removeDomBtn.IsEnabled = false;
        RestartIfNeeded();
    }

    private void RefreshDomExcListUI()
    {
        if (_domExcList == null) return;
        _domExcList.Children.Clear();
        if (!_settings.DomainExceptions.Any())
        {
            _domExcList.Children.Add(MakeText(Tr.Get("no_exceptions"), 12, false, C_MUTED));
            if (_removeDomBtn != null) _removeDomBtn.IsEnabled = false;
            _selectedDomainException = null;
            return;
        }

        foreach (var dom in _settings.DomainExceptions)
        {
            string display = dom;
            try
            {
                bool startsWithDot = dom.StartsWith('.');
                string part = startsWithDot ? dom.Substring(1) : dom;
                string unicode = _idn.GetUnicode(part);
                display = startsWithDot ? "." + unicode : unicode;
            }
            catch { }

            var isSelected = _selectedDomainException == dom;
            var row = new Border
            {
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                Background = isSelected ? Br(C_ACTIVE) : Brushes.Transparent
            };
            row.Child = MakeText(display, 13, false, C_TXT);
            row.MouseLeftButtonDown += (_, _) =>
            {
                _selectedDomainException = dom;
                if (_removeDomBtn != null) _removeDomBtn.IsEnabled = true;
                RefreshDomExcListUI();
            };
            _domExcList.Children.Add(row);
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            Dispatcher.Invoke(() => OnHotkeyTriggered(id));
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void RegisterAllHotkeys()
    {
        if (_hwndSource == null) return;
        var hwnd = _hwndSource.Handle;
        var map = new (int id, string combo)[]
        {
            (HK_TOGGLE,  _settings.HotkeyToggle),
            (HK_ROUTING, _settings.HotkeyRouting),
            (HK_TUN,     _settings.HotkeyTun),
            (HK_EXIT,    _settings.HotkeyExit),
        };
        foreach (var (id, _) in map)
            NativeInterop.UnregisterHotKey(hwnd, id);
        foreach (var (id, combo) in map)
        {
            if (string.IsNullOrEmpty(combo)) continue;
            if (HotkeyManager.TryParse(combo, out var mods, out var vk))
            {
                if (!NativeInterop.RegisterHotKey(hwnd, id, mods, vk))
                    Dispatcher.BeginInvoke(() =>
                        MessageBox.Show($"{Tr.Get("hk_failed_reg")}\n{combo}", Tr.Get("error"),
                            MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        }

        for (int i = 0; i < HK_ROUTE_LIST_MAX; i++)
            NativeInterop.UnregisterHotKey(hwnd, HK_ROUTE_LIST_BASE + i);
        for (int i = 0; i < _settings.RouteListBindings.Count && i < HK_ROUTE_LIST_MAX; i++)
        {
            var combo = _settings.RouteListBindings[i].Hotkey;
            if (string.IsNullOrEmpty(combo)) continue;
            if (HotkeyManager.TryParse(combo, out var mods, out var vk))
            {
                var idx = i;
                if (!NativeInterop.RegisterHotKey(hwnd, HK_ROUTE_LIST_BASE + idx, mods, vk))
                    Dispatcher.BeginInvoke(() =>
                        MessageBox.Show($"{Tr.Get("hk_failed_reg")}\n{combo}", Tr.Get("error"),
                            MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        }
    }

    private void UnregisterAllHotkeys()
    {
        if (_hwndSource == null) return;
        var hwnd = _hwndSource.Handle;
        foreach (var id in new[] { HK_TOGGLE, HK_ROUTING, HK_TUN, HK_EXIT })
            NativeInterop.UnregisterHotKey(hwnd, id);
        for (int i = 0; i < HK_ROUTE_LIST_MAX; i++)
            NativeInterop.UnregisterHotKey(hwnd, HK_ROUTE_LIST_BASE + i);
    }

    private void OnHotkeyTriggered(int id)
    {
        switch (id)
        {
            case HK_TOGGLE:
                if (_proxyProcess == null && _selectedIdx >= 0)
                {
                    _connectBtn.IsEnabled = false;
                    ShowConnecting();
                    Task.Run(ConnectWorker);
                }
                else if (_proxyProcess != null)
                {
                    StopProxy();
                }
                break;

            case HK_ROUTING:
                _settings.SplitTunneling = !_settings.SplitTunneling;
                _splitSwitch.IsChecked = _settings.SplitTunneling;
                OnSplitToggle();
                break;

            case HK_TUN:
                if (_tunModeChk == null) break;
                var newVal = !(_tunModeChk.IsChecked ?? false);
                if (newVal && !IsAdministrator())
                {
                    MessageBox.Show(
                        "TUN-режим требует прав администратора.\nПерезапустите приложение от имени администратора.",
                        "TUN Mode", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
                _tunModeChk.IsChecked = newVal;
                _settings.UseTunMode = newVal;
                SaveSettings();
                if (_proxyProcess != null) RestartIfNeeded();
                break;

            case HK_EXIT:
                CleanupAndExit();
                break;

            default:
                if (id >= HK_ROUTE_LIST_BASE && id < HK_ROUTE_LIST_BASE + HK_ROUTE_LIST_MAX)
                {
                    int idx = id - HK_ROUTE_LIST_BASE;
                    if (idx < _settings.RouteListBindings.Count)
                    {
                        var file = _settings.RouteListBindings[idx].File;
                        if (!string.IsNullOrEmpty(file) && file != _currentRoutesFile)
                        {
                            SaveCurrentRoutes();
                            _currentRoutesFile = file;
                            LoadRoutesFile(file);
                            if (_routesCombo.SelectedItem as string != file)
                                _routesCombo.SelectedItem = file;
                            if (_proxyRoutesCombo != null && _proxyRoutesCombo.SelectedItem as string != file)
                                _proxyRoutesCombo.SelectedItem = file;
                            RestartIfNeeded();
                            UpdateTrayMenu();
                        }
                    }
                }
                break;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_hkListeningBtn == null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _hkListeningBtn.Content = GetHkDisplay(GetHkValue(_hkListeningProp!));
            _hkListeningBtn.BorderBrush = Br(C_BORDER);
            _hkListeningBtn = null;
            _hkListeningProp = null;
            e.Handled = true;
            return;
        }

        bool ctrl  = Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl);
        bool alt   = Keyboard.IsKeyDown(Key.LeftAlt)   || Keyboard.IsKeyDown(Key.RightAlt);
        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        var combo = HotkeyManager.Build(ctrl, alt, shift, key);
        
        if (string.IsNullOrEmpty(combo))
        {
            MessageBox.Show(Tr.Get("hk_need_modifier"), Tr.Get("error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
            return;
        }

        var curProp = _hkListeningProp!;
        foreach (var other in new[] { "Toggle", "Routing", "Tun", "Exit" })
        {
            if (other != curProp && GetHkValue(other) == combo)
            {
                MessageBox.Show(Tr.Get("hk_conflict"), Tr.Get("error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
                return;
            }
        }
        for (int i = 0; i < _settings.RouteListBindings.Count; i++)
        {
            var rlProp = $"RouteList:{i}";
            if (rlProp != curProp && _settings.RouteListBindings[i].Hotkey == combo)
            {
                MessageBox.Show(Tr.Get("hk_conflict"), Tr.Get("error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
                return;
            }
        }

        SetHkValue(curProp, combo);
        _hkListeningBtn.Content = combo;
        _hkListeningBtn.BorderBrush = Br(C_BORDER);
        _hkListeningBtn = null;
        _hkListeningProp = null;
        SaveSettings();
        RegisterAllHotkeys();
        e.Handled = true;
    }

    private static readonly string[] _proxySchemes =
        { "vless://", "hysteria2://", "hy2://", "ss://", "trojan://", "vmess://", "tuic://", "hysteria://" };

    private static List<string> ExtractLinksFromDragData(IDataObject data)
    {
        var lines = new List<string>();

        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = data.GetData(DataFormats.UnicodeText) as string ?? "";
            lines.AddRange(text.Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }
        else if (data.GetDataPresent(DataFormats.Text))
        {
            var text = data.GetData(DataFormats.Text) as string ?? "";
            lines.AddRange(text.Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files != null)
            {
                foreach (var f in files)
                {
                    if (f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && File.Exists(f))
                    {
                        try { lines.AddRange(File.ReadAllLines(f)); }
                        catch { }
                    }
                }
            }
        }

        return lines
            .Select(l => l.Trim())
            .Where(l => _proxySchemes.Any(s => l.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();
    }

    internal static List<string> ExtractDropLinks(IDataObject data) => ExtractLinksFromDragData(data);

    private static bool DataHasProxyLinks(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text))
        {
            var text = (data.GetData(DataFormats.UnicodeText) ?? data.GetData(DataFormats.Text)) as string ?? "";
            if (_proxySchemes.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Any(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = DataHasProxyLinks(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        var links = ExtractLinksFromDragData(e.Data);
        if (links.Count == 0) return;
        ImportDroppedLinks(links);
        e.Handled = true;
    }

    internal void ImportDroppedLinks(IEnumerable<string> rawLinks)
    {
        var newServers = new List<ServerModel>();
        foreach (var link in rawLinks)
        {
            var parsed = LinkParser.ExtractAll(link);
            newServers.AddRange(parsed);
        }

        if (newServers.Count == 0)
        {
            MessageBox.Show("Не найдено допустимых серверов в перетащенных данных.", Tr.Get("error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int added = 0;
        foreach (var sv in newServers)
        {
            if (_servers.Any(s => s.Host == sv.Host)) continue;
            _servers.Add(sv);
            added++;
        }

        if (added > 0)
        {
            SaveServers();
            RefreshServerList();
            ShowPanel("servers");
        }

        var msg = added == newServers.Count
            ? $"Добавлено серверов: {added}"
            : $"Добавлено: {added}, дубликаты пропущены: {newServers.Count - added}";
        MessageBox.Show(msg, "Taa Proxy — Drag & Drop", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void StartHotkeyListening(string prop, Button btn)
    {
        if (_hkListeningBtn != null && _hkListeningBtn != btn)
        {
            _hkListeningBtn.Content = GetHkDisplay(GetHkValue(_hkListeningProp!));
            _hkListeningBtn.BorderBrush = Br(C_BORDER);
        }
        _hkListeningBtn = btn;
        _hkListeningProp = prop;
        btn.Content = Tr.Get("hk_listening");
        btn.BorderBrush = Br(C_ACCENT);
        
        this.Focus();
        Keyboard.Focus(this);
    }

    private string GetHkValue(string prop) => prop switch
    {
        "Toggle"   => _settings.HotkeyToggle,
        "Routing"  => _settings.HotkeyRouting,
        "Tun"      => _settings.HotkeyTun,
        "Exit"     => _settings.HotkeyExit,
        _ when prop.StartsWith("RouteList:") && int.TryParse(prop[10..], out var idx)
               && idx < _settings.RouteListBindings.Count
               => _settings.RouteListBindings[idx].Hotkey,
        _          => ""
    };

    private void SetHkValue(string prop, string val)
    {
        switch (prop)
        {
            case "Toggle":   _settings.HotkeyToggle   = val; break;
            case "Routing":  _settings.HotkeyRouting  = val; break;
            case "Tun":      _settings.HotkeyTun      = val; break;
            case "Exit":     _settings.HotkeyExit     = val; break;
            default:
                if (prop.StartsWith("RouteList:") && int.TryParse(prop[10..], out var idx)
                    && idx < _settings.RouteListBindings.Count)
                    _settings.RouteListBindings[idx].Hotkey = val;
                break;
        }
    }

    private string GetHkDisplay(string val)
        => string.IsNullOrEmpty(val) ? Tr.Get("hk_none") : val;

    private StackPanel? _routeBindingsPanel;

    private TabItem BuildTabHotkeys()
    {
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(MakeText(Tr.Get("hk_title"), 18, true, C_TXT, margin: new Thickness(0, 0, 0, 6)));
        sp.Children.Add(MakeText(Tr.Get("hk_desc"), 12, false, C_MUTED, margin: new Thickness(0, 0, 0, 20)));

        _hkButtons.Clear();

        var actions = new[]
        {
            ("Toggle",   Tr.Get("hk_toggle")),
            ("Routing",  Tr.Get("hk_routing")),
            ("Tun",      Tr.Get("hk_tun")),
            ("Exit",     Tr.Get("hk_exit")),
        };

        foreach (var (propName, label) in actions)
        {
            var pn = propName;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = MakeText(label, 13, false, C_TXT);
            lbl.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(lbl, 0);
            row.Children.Add(lbl);

            var bindBtn = new Button
            {
                Content = GetHkDisplay(GetHkValue(pn)),
                MinWidth = 150,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Background = Br(C_SIDEBAR),
                Foreground = Br(C_TXT),
                BorderBrush = Br(C_BORDER),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                FontSize = 13,
                Padding = new Thickness(10, 0, 10, 0),
                Template = GetBtnTemplate(),
                Tag = new CornerRadius(6)
            };
            bindBtn.Click += (_, _) => StartHotkeyListening(pn, bindBtn);
            Grid.SetColumn(bindBtn, 1);
            row.Children.Add(bindBtn);

            var bindLblBtn = MakeOutlineBtn(Tr.Get("hk_bind"), h: 32,
                margin: new Thickness(4, 0, 0, 0), fontSize: 12, click: () => StartHotkeyListening(pn, bindBtn), w: 80);
            Grid.SetColumn(bindLblBtn, 2);
            row.Children.Add(bindLblBtn);

            var clearBtn = MakeOutlineBtn("×", fg: C_DANGER, h: 32,
                margin: new Thickness(4, 0, 0, 0), fontSize: 14, w: 34, click: () =>
                {
                    if (_hkListeningProp == pn)
                    { _hkListeningBtn = null; _hkListeningProp = null; }
                    SetHkValue(pn, "");
                    bindBtn.Content = Tr.Get("hk_none");
                    bindBtn.BorderBrush = Br(C_BORDER);
                    SaveSettings();
                    RegisterAllHotkeys();
                });
            Grid.SetColumn(clearBtn, 3);
            row.Children.Add(clearBtn);

            sp.Children.Add(row);
            _hkButtons[propName] = bindBtn;
        }

        sp.Children.Add(new Border
        {
            Height = 1,
            Background = Br(C_BORDER),
            Margin = new Thickness(0, 20, 0, 16)
        });

        sp.Children.Add(MakeText(Tr.Get("hk_route_lists"), 14, true, C_TXT, margin: new Thickness(0, 0, 0, 4)));
        sp.Children.Add(MakeText(Tr.Get("hk_route_desc"), 12, false, C_MUTED, margin: new Thickness(0, 0, 0, 14)));

        _routeBindingsPanel = new StackPanel();
        for (int i = 0; i < _settings.RouteListBindings.Count; i++)
            _routeBindingsPanel.Children.Add(BuildRouteBindingRow(i));
        sp.Children.Add(_routeBindingsPanel);

        if (_settings.RouteListBindings.Count < HK_ROUTE_LIST_MAX)
        {
            var addBtn = MakeOutlineBtn(Tr.Get("hk_add_binding"), h: 34, fontSize: 13,
                margin: new Thickness(0, 6, 0, 0), w: 180, click: AddRouteBinding);
            sp.Children.Add(addBtn);
        }

        var scroll = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return new TabItem { Header = Tr.Get("tab_hotkeys"), Content = scroll, Foreground = Br(C_TXT) };
    }

    private Grid BuildRouteBindingRow(int idx)
    {
        var binding = _settings.RouteListBindings[idx];
        var prop    = $"RouteList:{idx}";

        var row = new Grid { Margin = new Thickness(0, 0, 0, 8), Tag = idx };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var files = Directory.Exists(Paths.ListDir)
            ? Directory.GetFiles(Paths.ListDir, "*.txt").Select(Path.GetFileName).OfType<string>().OrderBy(x => x).ToList()
            : new List<string>();

        var placeholder = Tr.Get("hk_select_file");
        var combo = new ComboBox
        {
            Height   = 32,
            MinWidth = 160,
            Margin   = new Thickness(0, 0, 0, 0),
        };
        combo.Items.Add(placeholder);
        foreach (var f in files) combo.Items.Add(f);
        combo.SelectedItem = string.IsNullOrEmpty(binding.File) ? placeholder : (files.Contains(binding.File) ? (object)binding.File : placeholder);

        combo.SelectionChanged += (_, _) =>
        {
            var sel = combo.SelectedItem as string;
            binding.File = (sel == placeholder || sel == null) ? "" : sel;
            SaveSettings();
        };
        Grid.SetColumn(combo, 0);
        row.Children.Add(combo);

        var bindBtn = new Button
        {
            Content = GetHkDisplay(binding.Hotkey),
            MinWidth = 140,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            Background = Br(C_SIDEBAR),
            Foreground = Br(C_TXT),
            BorderBrush = Br(C_BORDER),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            FontSize = 13,
            Padding = new Thickness(10, 0, 10, 0),
            Template = GetBtnTemplate(),
            Tag = new CornerRadius(6)
        };
        bindBtn.Click += (_, _) => StartHotkeyListening(prop, bindBtn);
        Grid.SetColumn(bindBtn, 1);
        row.Children.Add(bindBtn);

        var bindLbl = MakeOutlineBtn(Tr.Get("hk_bind"), h: 32,
            margin: new Thickness(4, 0, 0, 0), fontSize: 12, click: () => StartHotkeyListening(prop, bindBtn), w: 80);
        Grid.SetColumn(bindLbl, 2);
        row.Children.Add(bindLbl);

        var clearBtn = MakeOutlineBtn("×", fg: C_DANGER, h: 32,
            margin: new Thickness(4, 0, 0, 0), fontSize: 14, w: 34, click: () =>
            {
                if (_hkListeningProp == prop)
                { _hkListeningBtn = null; _hkListeningProp = null; }
                binding.Hotkey = "";
                bindBtn.Content = Tr.Get("hk_none");
                bindBtn.BorderBrush = Br(C_BORDER);
                SaveSettings();
                RegisterAllHotkeys();
            });
        Grid.SetColumn(clearBtn, 3);
        row.Children.Add(clearBtn);

        var removeBtn = MakeOutlineBtn("−", fg: C_MUTED, h: 32,
            margin: new Thickness(4, 0, 0, 0), fontSize: 16, w: 34, click: () => RemoveRouteBinding(idx));
        Grid.SetColumn(removeBtn, 4);
        row.Children.Add(removeBtn);

        return row;
    }

    private void AddRouteBinding()
    {
        if (_settings.RouteListBindings.Count >= HK_ROUTE_LIST_MAX) return;
        _settings.RouteListBindings.Add(new RouteListBinding());
        SaveSettings();
        RebuildHotkeyTab();
    }

    private void RemoveRouteBinding(int idx)
    {
        if (idx < 0 || idx >= _settings.RouteListBindings.Count) return;
        var prop = $"RouteList:{idx}";
        if (_hkListeningProp == prop) { _hkListeningBtn = null; _hkListeningProp = null; }
        _settings.RouteListBindings.RemoveAt(idx);
        SaveSettings();
        RegisterAllHotkeys();
        RebuildHotkeyTab();
    }

    private TabItem? _hotkeysTab;

    private void RebuildHotkeyTab()
    {
        if (_hotkeysTab == null) return;
        var newTab = BuildTabHotkeys();
        if (_hotkeysTab.Parent is TabControl tc)
        {
            int tabIdx = tc.Items.IndexOf(_hotkeysTab);
            if (tabIdx >= 0)
            {
                bool wasSelected = tc.SelectedItem == _hotkeysTab;
                tc.Items[tabIdx] = newTab;
                _hotkeysTab = newTab;
                if (wasSelected) tc.SelectedItem = newTab;
            }
        }
    }

    private TrayDropZone? _trayDropZone;

    private void InitTray()
    {
        Forms.ToolStripManager.Renderer = new Forms.ToolStripProfessionalRenderer(new LightProfessionalColorTable());

        _tray = new Forms.NotifyIcon
        {
            Text = "Taa Proxy",
            Visible = true
        };
        try
        {
            var ico = Paths.Resource("pic\\ico.ico");
            if (File.Exists(ico)) _tray.Icon = new System.Drawing.Icon(ico);
            else _tray.Icon = System.Drawing.SystemIcons.Application;
        }
        catch { _tray.Icon = System.Drawing.SystemIcons.Application; }

        _tray.DoubleClick += (_, _) => ShowWindow();

        _trayDropZone = new TrayDropZone(this);
        UpdateTrayMenu();
    }

    private void UpdateTray(bool connected)
    {
        if (_tray == null) return;
        try
        {
            var icoName = connected ? "icoon.ico" : "ico.ico";
            var icoPath = Paths.Resource($"pic\\{icoName}");
            if (File.Exists(icoPath)) _tray.Icon = new System.Drawing.Icon(icoPath);
        }
        catch { }
        UpdateTrayMenu();
    }

    private void UpdateTrayMenu()
    {
        if (_tray == null) return;
        var menu = new Forms.ContextMenuStrip();

        var status = _proxyProcess != null ? "Подключено" : "Отключено";
        menu.Items.Add(status).Enabled = false;
        menu.Items.Add(new Forms.ToolStripSeparator());

        if (_proxyProcess == null && _selectedIdx >= 0)
        {
            var ci = menu.Items.Add(Tr.Get("btn_connect"));
            ci.Font = new System.Drawing.Font(ci.Font, System.Drawing.FontStyle.Bold);
            ci.Click += (_, _) => Dispatcher.Invoke(ToggleConnection);
        }
        if (_proxyProcess != null)
        {
            var di = menu.Items.Add(Tr.Get("btn_disconnect"));
            di.Click += (_, _) => Dispatcher.Invoke(StopProxy);
        }
        menu.Items.Add(new Forms.ToolStripSeparator());

        var routingItem = new Forms.ToolStripMenuItem(Tr.Get("split_tunneling"));
        routingItem.CheckOnClick = true;
        routingItem.Checked = _settings.SplitTunneling;
        routingItem.Click += (_, _) =>
        {
            _settings.SplitTunneling = !_settings.SplitTunneling;
            _splitSwitch.IsChecked = _settings.SplitTunneling;
            OnSplitToggle();
        };
        menu.Items.Add(routingItem);

        var routesSubMenu = new Forms.ToolStripMenuItem("Список маршрутизации");
        var files = Directory.Exists(Paths.ListDir)
            ? Directory.GetFiles(Paths.ListDir, "*.txt").Select(Path.GetFileName).OfType<string>().OrderBy(x => x).ToList()
            : new List<string>();
        foreach (var file in files)
        {
            var item = new Forms.ToolStripMenuItem(file.Length > 35 ? file[..35] + "…" : file);
            item.Checked = file == _currentRoutesFile;
            item.Click += (_, _) =>
            {
                if (file != _currentRoutesFile)
                {
                    SaveCurrentRoutes();
                    _currentRoutesFile = file;
                    LoadRoutesFile(file);
                    if (_routesCombo.SelectedItem as string != file)
                        _routesCombo.SelectedItem = file;
                    if (_proxyRoutesCombo != null && _proxyRoutesCombo.SelectedItem as string != file)
                        _proxyRoutesCombo.SelectedItem = file;
                    RestartIfNeeded();
                    UpdateTrayMenu();
                }
            };
            routesSubMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(routesSubMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var sub = new Forms.ToolStripMenuItem(Tr.Get("app_name"));
        for (int i = 0; i < _servers.Count; i++)
        {
            var idx = i; var name = _servers[i].Name;
            var item = new Forms.ToolStripMenuItem(name.Length > 35 ? name[..35] + "…" : name);
            item.Checked = idx == _selectedIdx;
            item.Click += (_, _) => Dispatcher.Invoke(() => SelectServer(idx));
            sub.DropDownItems.Add(item);
        }
        menu.Items.Add(sub);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var open = menu.Items.Add(Tr.Get("tray_open"));
        open.Click += (_, _) => Dispatcher.Invoke(ShowWindow);
        open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);

        var dropZoneItem = new Forms.ToolStripMenuItem("📌 Зона D&D ссылок")
        {
            Checked = _trayDropZone?.IsVisible ?? false,
            CheckOnClick = false
        };
        dropZoneItem.Click += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_trayDropZone != null)
                {
                    if (_trayDropZone.IsVisible)
                        _trayDropZone.Hide();
                    else
                        _trayDropZone.Show();
                    UpdateTrayMenu();
                }
            });
        };
        menu.Items.Add(dropZoneItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var quit = menu.Items.Add(Tr.Get("tray_exit"));
        quit.Click += (_, _) => Dispatcher.Invoke(() => CleanupAndExit());

        _tray.ContextMenuStrip = menu;
    }

    private void OnClosing(object? s, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        if (_settings.MinimizeOnClose) Hide();
        else CleanupAndExit();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();
        RegisterAllHotkeys();
        SetWindowIcon(_proxyProcess != null);
    }

    private void CleanupAndExit(bool restart = false)
    {
        _dotTimer?.Stop();
        _fadeTimer?.Stop();
        _monitorCts.Cancel();
        SaveCurrentRoutes();
        SaveSettings();
        StopProxy();
        UnregisterAllHotkeys();
        _hwndSource?.RemoveHook(HwndHook);
        _tray?.Dispose();
        if (restart)
        {
            Program.ReleaseMutex();
            Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule!.FileName!) { UseShellExecute = true });
        }
        Application.Current.Shutdown();
    }

    private void CenterIfDefault()
    {
        if (_settings.WindowLeft < 0)
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        }
    }
}

internal enum ToastType { Info, Success, Error }

internal class ToastWindow : Window
{
    private static readonly Color C_BG = (Color)ColorConverter.ConvertFromString("#08101E");
    private static readonly Color C_BORDER = (Color)ColorConverter.ConvertFromString("#1A2640");
    private static readonly Color C_TXT = (Color)ColorConverter.ConvertFromString("#F0F4FF");
    private static readonly Color C_MUTED = (Color)ColorConverter.ConvertFromString("#4B5A7A");
    private static readonly Color C_SUCCESS = (Color)ColorConverter.ConvertFromString("#10B981");
    private static readonly Color C_INFO = (Color)ColorConverter.ConvertFromString("#6366F1");
    private static readonly Color C_ERROR = (Color)ColorConverter.ConvertFromString("#EF4444");

    private static SolidColorBrush Br(Color c) => new(c);

    public ToastWindow(string title, string message, ToastType type)
    {
        Width = 300;
        Height = 90;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var screen = SystemParameters.WorkArea;
        Left = screen.Right - Width - 20;
        Top = screen.Bottom - Height - 20;

        var border = new Border
        {
            Background = Br(C_BG),
            BorderBrush = Br(C_BORDER),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Opacity = 0,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 28, ShadowDepth = 4, Opacity = 0.55
            }
        };

        var grid = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var color = type switch { ToastType.Success => C_SUCCESS, ToastType.Error => C_ERROR, _ => C_INFO };
        var dot = new Ellipse { Width = 10, Height = 10, Fill = Br(color), Margin = new Thickness(0, 0, 8, 0) };
        var titleBlock = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Br(C_TXT) };
        header.Children.Add(dot);
        header.Children.Add(titleBlock);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var msgBlock = new TextBlock { Text = message, FontSize = 12, Foreground = Br(C_MUTED), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(msgBlock, 1);
        grid.Children.Add(msgBlock);

        border.Child = grid;
        Content = border;

        Loaded += async (_, _) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            border.BeginAnimation(OpacityProperty, fadeIn);

            await Task.Delay(4000);

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (_, _) => Close();
            border.BeginAnimation(OpacityProperty, fadeOut);
        };
    }
}

internal class TrayDropZone : Window
{
    private readonly MainWindow _owner;
    private static readonly string[] _proxySchemes =
        { "vless://", "hysteria2://", "hy2://", "ss://", "trojan://", "vmess://", "tuic://", "hysteria://" };

    private static readonly Color C_BG   = (Color)ColorConverter.ConvertFromString("#0B1020");
    private static readonly Color C_ACC  = (Color)ColorConverter.ConvertFromString("#6366F1");
    private static readonly Color C_TXT  = (Color)ColorConverter.ConvertFromString("#F0F4FF");
    private static readonly Color C_MUTED= (Color)ColorConverter.ConvertFromString("#4B5A7A");
    private static SolidColorBrush Br(Color c) => new(c);

    private Border _border = null!;

    public TrayDropZone(MainWindow owner)
    {
        _owner = owner;

        Title = "Taa Drop Zone";
        Width = 130; Height = 80;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 12;
        Top  = wa.Bottom - Height - 12;

        _border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 11, 16, 32)),
            BorderBrush = Br(C_ACC),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Cursor = Cursors.Arrow
        };

        var sp = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        sp.Children.Add(new TextBlock
        {
            Text = "🔗",
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        });
        sp.Children.Add(new TextBlock
        {
            Text = "Бросить ссылку",
            FontSize = 10,
            Foreground = Br(C_MUTED),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _border.Child = sp;
        Content = _border;

        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        MouseLeftButtonDown += (_, _) => DragMove();

        MouseRightButtonDown += (_, _) => Hide();
    }

    private bool HasLinks(IDataObject d)
    {
        string text = "";
        if (d.GetDataPresent(DataFormats.UnicodeText))
            text = d.GetData(DataFormats.UnicodeText) as string ?? "";
        else if (d.GetDataPresent(DataFormats.Text))
            text = d.GetData(DataFormats.Text) as string ?? "";
        return _proxySchemes.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase))
            || (d.GetDataPresent(DataFormats.FileDrop) &&
                (d.GetData(DataFormats.FileDrop) as string[])?.Any(f =>
                    f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) == true);
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (HasLinks(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            _border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            _border.Background  = new SolidColorBrush(Color.FromArgb(200, 8, 28, 18));
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        _border.BorderBrush = Br(C_ACC);
        _border.Background  = new SolidColorBrush(Color.FromArgb(225, 11, 16, 32));
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _border.BorderBrush = Br(C_ACC);
        _border.Background  = new SolidColorBrush(Color.FromArgb(225, 11, 16, 32));

        var links = MainWindow.ExtractDropLinks(e.Data);
        if (links.Count > 0)
            _owner.ImportDroppedLinks(links);
        e.Handled = true;
    }
}

internal class ProcessPickerDialog : Window
{
    public string SelectedProcessName { get; private set; } = "";

    private static readonly Color C_BG   = (Color)ColorConverter.ConvertFromString("#07090F");
    private static readonly Color C_HEAD = (Color)ColorConverter.ConvertFromString("#0B1020");
    private static readonly Color C_BORD = (Color)ColorConverter.ConvertFromString("#1A2640");
    private static readonly Color C_TXT  = (Color)ColorConverter.ConvertFromString("#F0F4FF");
    private static readonly Color C_MUTED= (Color)ColorConverter.ConvertFromString("#4B5A7A");
    private static readonly Color C_ACC  = (Color)ColorConverter.ConvertFromString("#6366F1");
    private static readonly Color C_CARD = (Color)ColorConverter.ConvertFromString("#0D1526");
    private static readonly Color C_SEL  = (Color)ColorConverter.ConvertFromString("#1A2A55");
    private static SolidColorBrush Br(Color c) => new(c);

    private TextBox   _searchBox  = null!;
    private StackPanel _list      = null!;
    private TextBox   _manualBox  = null!;
    private string?   _selected;

    public ProcessPickerDialog()
    {
        Title = "Выбор приложения";
        Width = 480; Height = 520;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Br(C_BG);
        WindowStyle = WindowStyle.None;
        FontFamily = new FontFamily("Segoe UI");

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 36,
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            GlassFrameThickness = new Thickness(0)
        });

        Content = BuildContent();
        Loaded += (_, _) => { RefreshList(""); _searchBox.Focus(); };
    }

    private UIElement BuildContent()
    {
        var titleBar = new Border
        {
            Background = Br(C_HEAD), Height = 36,
            BorderBrush = Br(C_BORD), BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var tbGrid = new Grid();
        tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = new TextBlock
        {
            Text = "Выбор запущенного приложения",
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };
        Grid.SetColumn(titleText, 0);
        tbGrid.Children.Add(titleText);

        var closeBtn = new Button
        {
            Content = "⛌", Width = 40, Height = 36,
            Background = Brushes.Transparent, Foreground = Br(C_MUTED),
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FontSize = 11
        };
        closeBtn.MouseEnter += (_, _) => { closeBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); closeBtn.Foreground = Brushes.White; };
        closeBtn.MouseLeave += (_, _) => { closeBtn.Background = Brushes.Transparent; closeBtn.Foreground = Br(C_MUTED); };
        closeBtn.Click += (_, _) => DialogResult = false;
        WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
        Grid.SetColumn(closeBtn, 1);
        tbGrid.Children.Add(closeBtn);
        titleBar.Child = tbGrid;

        var body = new Grid { Margin = new Thickness(16, 12, 16, 14) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _searchBox = new TextBox
        {
            Height = 34, FontSize = 14,
            Background = Br((Color)ColorConverter.ConvertFromString("#09090B")),
            Foreground = Br(C_TXT), BorderBrush = Br(C_ACC), BorderThickness = new Thickness(1),
            CaretBrush = Br(C_TXT), Padding = new Thickness(9, 0, 9, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var hint = new TextBlock
        {
            Text = "🔍  Поиск по имени процесса...",
            Foreground = Br(C_MUTED), FontSize = 13, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
        };
        _searchBox.TextChanged += (_, _) =>
        {
            hint.Visibility = _searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshList(_searchBox.Text);
        };
        var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchGrid.Children.Add(_searchBox);
        searchGrid.Children.Add(hint);
        Grid.SetRow(searchGrid, 0);
        body.Children.Add(searchGrid);

        _list = new StackPanel();
        var listScroll = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Br(C_CARD),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var listBorder = new Border
        {
            Child = listScroll,
            BorderBrush = Br(C_BORD), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(listBorder, 1);
        body.Children.Add(listBorder);

        var manualLabel = new TextBlock
        {
            Text = "Или введите вручную:",
            FontSize = 12, Foreground = Br(C_MUTED), Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(manualLabel, 2);
        body.Children.Add(manualLabel);

        _manualBox = new TextBox
        {
            Height = 34, FontSize = 13,
            Background = Br((Color)ColorConverter.ConvertFromString("#09090B")),
            Foreground = Br(C_TXT), BorderBrush = Br(C_BORD), BorderThickness = new Thickness(1),
            CaretBrush = Br(C_TXT), Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _manualBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitManual(); };
        _manualBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_manualBox.Text))
            {
                _selected = null;
                RefreshListHighlight();
            }
        };
        Grid.SetRow(_manualBox, 3);
        body.Children.Add(_manualBox);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var okBtn = MakeBtn("Добавить", C_ACC);
        okBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_manualBox.Text))
                CommitManual();
            else if (_selected != null)
                Commit(_selected);
        };
        var cancelBtn = MakeBtn("Отмена", (Color)ColorConverter.ConvertFromString("#27272A"));
        cancelBtn.Click += (_, _) => DialogResult = false;
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        Grid.SetRow(btnRow, 4);
        body.Children.Add(btnRow);

        var outer = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        outer.Children.Add(titleBar);
        outer.Children.Add(body);
        return outer;
    }

    private void RefreshList(string filter)
    {
        _list.Children.Clear();
        IEnumerable<string> procs = Process.GetProcesses()
            .Where(p => !string.IsNullOrEmpty(p.ProcessName))
            .Select(p => p.ProcessName + ".exe")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n);

        if (!string.IsNullOrWhiteSpace(filter))
            procs = procs.Where(n => n.Contains(filter, StringComparison.OrdinalIgnoreCase));

        bool any = false;
        foreach (var name in procs)
        {
            any = true;
            var n = name;
            var row = new Border
            {
                Padding = new Thickness(10, 7, 10, 7),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                Background = n == _selected
                    ? new SolidColorBrush(C_SEL)
                    : Brushes.Transparent,
                Margin = new Thickness(4, 2, 4, 2)
            };
            var txt = new TextBlock
            {
                Text = n, FontSize = 13,
                Foreground = Br(C_TXT),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Child = txt;
            row.MouseLeftButtonDown += (_, e) =>
            {
                _selected = n;
                _manualBox.Text = "";
                if (e.ClickCount >= 2)
                {
                    Commit(n);
                }
                else
                {
                    RefreshList(_searchBox.Text);
                }
            };
            _list.Children.Add(row);
        }

        if (!any)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "Нет совпадений",
                FontSize = 13, Foreground = Br(C_MUTED),
                Margin = new Thickness(12, 8, 0, 0)
            });
        }
    }

    private void RefreshListHighlight()
    {
        foreach (var child in _list.Children.OfType<Border>())
        {
            if (child.Child is TextBlock tb)
                child.Background = tb.Text == _selected
                    ? new SolidColorBrush(C_SEL) : Brushes.Transparent;
        }
    }

    private void CommitManual()
    {
        var v = _manualBox.Text.Trim();
        if (string.IsNullOrEmpty(v)) return;
        SelectedProcessName = v;
        DialogResult = true;
    }

    private void Commit(string name)
    {
        SelectedProcessName = name;
        DialogResult = true;
    }
internal class LightProfessionalColorTable : Forms.ProfessionalColorTable
{
    public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(200, 200, 200);
    public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(230, 230, 230);
    public override System.Drawing.Color MenuItemSelectedGradientBegin => System.Drawing.Color.FromArgb(240, 240, 240);
    public override System.Drawing.Color MenuItemSelectedGradientEnd => System.Drawing.Color.FromArgb(240, 240, 240);
    public override System.Drawing.Color MenuItemPressedGradientBegin => System.Drawing.Color.FromArgb(220, 220, 220);
    public override System.Drawing.Color MenuItemPressedGradientEnd => System.Drawing.Color.FromArgb(220, 220, 220);
    public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(180, 180, 180);
    public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.White;
    public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.White;
    public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.White;
    public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.White;
    public override System.Drawing.Color ToolStripBorder => System.Drawing.Color.FromArgb(180, 180, 180);
    public override System.Drawing.Color MenuStripGradientBegin => System.Drawing.Color.White;
    public override System.Drawing.Color MenuStripGradientEnd => System.Drawing.Color.White;
    public override System.Drawing.Color CheckBackground => System.Drawing.Color.FromArgb(220, 220, 220);
    public override System.Drawing.Color CheckPressedBackground => System.Drawing.Color.FromArgb(200, 200, 200);
    public override System.Drawing.Color CheckSelectedBackground => System.Drawing.Color.FromArgb(200, 200, 200);
    public override System.Drawing.Color ButtonSelectedBorder => System.Drawing.Color.FromArgb(180, 180, 180);
}

internal class InputDialog : Window
{
    private readonly TextBox _input;
    public string Result => _input.Text;

    private static readonly Color C_BG = (Color)ColorConverter.ConvertFromString("#07090F");
    private static readonly Color C_HEAD = (Color)ColorConverter.ConvertFromString("#0B1020");
    private static readonly Color C_BORD = (Color)ColorConverter.ConvertFromString("#1A2640");
    private static readonly Color C_TXT = (Color)ColorConverter.ConvertFromString("#F0F4FF");
    private static readonly Color C_MUTED = (Color)ColorConverter.ConvertFromString("#4B5A7A");
    private static readonly Color C_ACC = (Color)ColorConverter.ConvertFromString("#6366F1");

    private static SolidColorBrush Br(Color c) => new(c);

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        Title = title;
        Width = 420; Height = 185;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Br(C_BG);
        WindowStyle = WindowStyle.None;
        FontFamily = new FontFamily("Segoe UI");

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 36,
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
            GlassFrameThickness = new Thickness(0)
        });

        var titleBar = new Border
        {
            Background = Br(C_HEAD),
            Height = 36,
            BorderBrush = Br(C_BORD),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var tbGrid = new Grid();
        tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Br(C_MUTED),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };
        Grid.SetColumn(titleText, 0);
        tbGrid.Children.Add(titleText);

        var closeBtn = new Button
        {
            Content = "⛌",
            Width = 40,
            Height = 36,
            Background = Brushes.Transparent,
            Foreground = Br(C_MUTED),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontSize = 11
        };
        closeBtn.MouseEnter += (_, _) => { closeBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); closeBtn.Foreground = Brushes.White; };
        closeBtn.MouseLeave += (_, _) => { closeBtn.Background = Brushes.Transparent; closeBtn.Foreground = Br(C_MUTED); };
        closeBtn.Click += (_, _) => { DialogResult = false; };
        WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
        Grid.SetColumn(closeBtn, 1);
        tbGrid.Children.Add(closeBtn);
        titleBar.Child = tbGrid;

        var body = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };
        body.Children.Add(new TextBlock
        {
            Text = prompt,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Br(C_MUTED),
            TextWrapping = TextWrapping.Wrap
        });

        _input = new TextBox
        {
            Text = defaultValue,
            FontSize = 13,
            Height = 38,
            Background = Br((Color)ColorConverter.ConvertFromString("#0D1526")),
            Foreground = Br(C_TXT),
            BorderBrush = Br(C_ACC),
            BorderThickness = new Thickness(1),
            CaretBrush = Br(C_TXT),
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 14),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) DialogResult = true; if (e.Key == Key.Escape) DialogResult = false; };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = MakeDialogBtn("OK", C_ACC, true);
        var cancelBtn = MakeDialogBtn("Отмена", (Color)ColorConverter.ConvertFromString("#111E38"), false);
        okBtn.Click += (_, _) => { DialogResult = true; };
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);

        body.Children.Add(_input);
        body.Children.Add(btnRow);

        var outer = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        outer.Children.Add(titleBar);
        outer.Children.Add(body);
        Content = outer;

        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    private static Button MakeDialogBtn(string text, Color bg, bool isOk)
    {
        var btn = new Button
        {
            Content = text,
            Width = 80,
            Height = 32,
            Margin = isOk ? new Thickness(0, 0, 8, 0) : new Thickness(0),
            Background = new SolidColorBrush(bg),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(isOk ? 0 : 1),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3F3F46")),
            Cursor = Cursors.Hand,
            FontSize = 13
        };
        const string x = @"<ControlTemplate TargetType='Button'
            xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
            xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
          <Border x:Name='bd' Background='{TemplateBinding Background}'
                  BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}'
                  CornerRadius='6'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
        </ControlTemplate>";
        btn.Template = (ControlTemplate)XamlReader.Parse(x);
        return btn;
    }
}

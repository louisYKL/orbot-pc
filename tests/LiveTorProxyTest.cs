using Microsoft.Win32;
using OrbotPc.Core;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace OrbotPc.Tests;

internal static class LiveTorProxyTest
{
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private static readonly Uri TorCheckUri = new("https://check.torproject.org/api/ip");

    public static async Task RunAsync(string root)
    {
        var evidenceDirectory = Path.Combine(root, ".tmp", "live-tor-proxy-test");
        Directory.CreateDirectory(evidenceDirectory);
        var logPath = Path.Combine(evidenceDirectory, "live-test.log");
        await using var log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var originalProxy = CaptureProxy();
        var socksPort = ReservePort();
        var httpPort = ReservePort();
        var controlPort = ReservePort();
        var upstreamProxy = await WindowsProxyController.DetectLoopbackSocks5Async(new HashSet<int> { socksPort, httpPort, controlPort })
            ?? throw new InvalidOperationException("No reachable loopback SOCKS5 upstream was found in the current Windows proxy settings.");
        var stateDirectory = Path.Combine(evidenceDirectory, "app-state");
        var settings = new OrbotSettings
        {
            SocksPort = socksPort,
            HttpPort = httpPort,
            ControlPort = controlPort,
            OutboundProxyType = "socks5",
            OutboundProxyHost = upstreamProxy.Host,
            OutboundProxyPort = upstreamProxy.Port,
            UseWindowsProxy = true,
            EnableSmartConnect = false
        };
        var tor = new TorRuntime(root);
        var proxy = new WindowsProxyController(stateDirectory);
        HttpProxyServer? localHttpProxy = null;
        var torStarted = false;
        var proxyEnabled = false;

        void Record(string message)
        {
            var line = $"{DateTimeOffset.Now:O} {message}";
            Console.WriteLine(line);
            log.WriteLine(line);
        }

        try
        {
            Record($"Original Windows proxy: {Describe(originalProxy)}");
            Record($"Detected upstream SOCKS5 {upstreamProxy.Host}:{upstreamProxy.Port}; starting bundled Tor through it.");
            tor.LogMessage += Record;
            await tor.StartAsync(settings);
            torStarted = true;

            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(6);
            var lastProgress = -1;
            while (DateTime.UtcNow < deadline)
            {
                var status = await tor.GetBootstrapAsync();
                var match = System.Text.RegularExpressions.Regex.Match(status, @"PROGRESS=(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var progress) && progress != lastProgress)
                {
                    lastProgress = progress;
                    Record($"Tor bootstrap {progress}%: {status}");
                    if (progress >= 100) break;
                }
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            if (lastProgress < 100) throw new TimeoutException($"Tor bootstrap did not reach 100%; last reported progress was {lastProgress}%.");

            localHttpProxy = new HttpProxyServer(settings.HttpPort, settings.SocksPort);
            localHttpProxy.LogMessage += Record;
            localHttpProxy.Start();
            await AssertTorExitAsync(new WebProxy($"http://127.0.0.1:{settings.HttpPort}"), "app local HTTP proxy", Record);

            proxy.Enable(settings.HttpPort);
            proxyEnabled = true;
            var enabled = CaptureProxy();
            if (enabled.Enable != 1 || !string.Equals(enabled.Server, $"http=127.0.0.1:{settings.HttpPort};https=127.0.0.1:{settings.HttpPort}", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows current-user HTTP/HTTPS proxy registry values do not match the app endpoint.");
            Record($"Windows current-user proxy enabled: {Describe(enabled)}");

            await AssertTorExitAsync(WebRequest.GetSystemWebProxy(), "Windows current-user system proxy", Record);
            Console.WriteLine("PASS live Tor bootstrap, app HTTP proxy IsTor=true, Windows system proxy IsTor=true");
        }
        finally
        {
            Exception? cleanupError = null;
            if (proxyEnabled)
            {
                try
                {
                    proxy.Disable();
                    var restored = CaptureProxy();
                    Record($"Windows current-user proxy after restore: {Describe(restored)}");
                    if (restored != originalProxy)
                        cleanupError = new InvalidOperationException("Windows current-user proxy settings did not exactly match the pre-test snapshot after restore.");
                    else Record("Windows current-user proxy snapshot restored exactly.");
                }
                catch (Exception ex) { cleanupError = ex; }
            }
            if (localHttpProxy is not null) await localHttpProxy.DisposeAsync();
            if (torStarted) await tor.StopAsync();
            await tor.DisposeAsync();
            Record("Live test cleanup complete.");
            if (cleanupError is not null) throw cleanupError;
        }
    }

    private static async Task AssertTorExitAsync(IWebProxy webProxy, string route, Action<string> record)
    {
        using var handler = new HttpClientHandler { Proxy = webProxy, UseProxy = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        using var response = await client.GetAsync(TorCheckUri);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(body);
        if (!json.RootElement.TryGetProperty("IsTor", out var isTor) || isTor.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException($"{route} reached Tor check API but it did not report IsTor=true.");
        record($"{route} Tor check passed: IsTor=true.");
    }

    private static ProxySnapshot CaptureProxy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false)
            ?? throw new InvalidOperationException("Could not read current-user Internet Settings.");
        var enable = key.GetValue("ProxyEnable");
        var server = key.GetValue("ProxyServer") as string;
        var bypass = key.GetValue("ProxyOverride") as string;
        var enableKind = enable is null ? (RegistryValueKind?)null : key.GetValueKind("ProxyEnable");
        var serverKind = server is null ? (RegistryValueKind?)null : key.GetValueKind("ProxyServer");
        var bypassKind = bypass is null ? (RegistryValueKind?)null : key.GetValueKind("ProxyOverride");
        return new ProxySnapshot(enable is null ? null : Convert.ToInt32(enable), server, bypass, enableKind, serverKind, bypassKind);
    }

    private static string Describe(ProxySnapshot snapshot) =>
        $"Enable={snapshot.Enable?.ToString() ?? "<absent>"}, Server={snapshot.Server ?? "<absent>"}, Override={snapshot.Bypass ?? "<absent>"}";

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record ProxySnapshot(int? Enable, string? Server, string? Bypass, RegistryValueKind? EnableKind, RegistryValueKind? ServerKind, RegistryValueKind? BypassKind);
}

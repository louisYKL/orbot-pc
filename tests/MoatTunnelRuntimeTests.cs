using OrbotPc.Core;
using System.IO;
using System.Net;

namespace OrbotPc.Tests;

public static class MoatTunnelRuntimeTests
{
    public static void Run()
    {
        Check(MoatTunnelRuntime.TryParseMethodLine("CMETHOD meek_lite socks5 127.0.0.1:49241", out var port) && port == 49241,
            "Could not parse a managed meek SOCKS endpoint.");
        Check(MoatTunnelRuntime.TryParseMethodLine("CMETHOD meek_lite socks5 [::1]:9050", out port) && port == 9050,
            "Could not parse a managed meek IPv6 endpoint.");
        Check(!MoatTunnelRuntime.TryParseMethodLine("CMETHOD obfs4 socks5 127.0.0.1:49241", out _),
            "A non-meek managed transport was accepted as Moat meek.");
        Check(!MoatTunnelRuntime.TryParseMethodLine("CMETHOD meek_lite socks4 127.0.0.1:49241", out _),
            "A non-SOCKS5 managed endpoint was accepted.");
        Check(!MoatTunnelRuntime.TryParseMethodLine("CMETHOD meek_lite socks5 127.0.0.1:65536", out _),
            "An out-of-range managed port was accepted.");
        Check(!MoatTunnelRuntime.TryParseMethodLine("CMETHODS DONE", out _),
            "A protocol terminator was accepted as an endpoint.");
        Check(MoatTunnelRuntime.TryParseMethodLine("CMETHOD dnstt socks5 127.0.0.1:49242", "dnstt", out var dnsttPort) && dnsttPort == 49242,
            "Could not parse a managed DNSTT SOCKS endpoint.");
        Console.WriteLine("PASS managed Moat meek transport protocol parsing");

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var lyrebird = Path.Combine(root, "runtime", "tor", "tor", "pluggable_transports", "lyrebird.exe");
        if (File.Exists(lyrebird))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var tunnel = new MoatTunnelRuntime(root);
            tunnel.StartAsync(cancellationToken: timeout.Token).GetAwaiter().GetResult();
            try
            {
                Check(tunnel.IsRunning && tunnel.SocksPort is >= 1 and <= 65535, "The bundled lyrebird failed to start a local meek SOCKS listener.");
                using var client = tunnel.CreateHttpClient();
                Check(client.Timeout == TimeSpan.FromSeconds(30), "The managed Moat HttpClient timeout was not initialized.");
                Console.WriteLine("PASS bundled lyrebird managed meek startup and local SOCKS endpoint");
            }
            finally { tunnel.StopAsync().GetAwaiter().GetResult(); }
            Check(!tunnel.IsRunning && tunnel.SocksPort == 0, "The managed meek process did not stop cleanly.");
            Console.WriteLine("PASS managed Moat meek process shutdown");
        }

        var dnstt = Path.Combine(root, "runtime", "dnstt", "dnstt-client.exe");
        if (File.Exists(dnstt))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var tunnel = new MoatTunnelRuntime(root);
            tunnel.StartDnsttAsync("doh=https://dns.google/dns-query;pubkey=241169008830694749fe96bb070c4855c5bb5b9c47b3833ed7d88521ba30a43f;domain=t.ruhnama.net", timeout.Token).GetAwaiter().GetResult();
            try
            {
                Check(tunnel.IsRunning && tunnel.SocksPort is >= 1 and <= 65535, "The bundled DNSTT client failed to publish its managed SOCKS listener.");
                using var client = tunnel.CreateDnsttHttpClient("doh=https://dns.google/dns-query;pubkey=241169008830694749fe96bb070c4855c5bb5b9c47b3833ed7d88521ba30a43f;domain=t.ruhnama.net");
                Check(client.Timeout == TimeSpan.FromSeconds(30), "The managed DNSTT HttpClient timeout was not initialized.");
                Console.WriteLine("PASS bundled DNSTT managed startup without opening a tunnel connection");
            }
            finally { tunnel.StopAsync().GetAwaiter().GetResult(); }
            Check(!tunnel.IsRunning && tunnel.SocksPort == 0, "The managed DNSTT process did not stop cleanly.");
            Console.WriteLine("PASS managed DNSTT process shutdown");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

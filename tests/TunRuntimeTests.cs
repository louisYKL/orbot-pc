using OrbotPc.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OrbotPc.Tests;

public static class TunRuntimeTests
{
    public static void Run(string root)
    {
        var configText = TunRuntime.BuildConfiguration(root, 9150);
        using var document = JsonDocument.Parse(configText);
        var config = document.RootElement;
        var inbound = config.GetProperty("inbounds")[0];
        Check(inbound.GetProperty("type").GetString() == "tun", "TUN inbound was not generated.");
        Check(inbound.GetProperty("auto_route").GetBoolean(), "TUN must own default route lifecycle.");
        Check(inbound.GetProperty("strict_route").GetBoolean(), "TUN must use strict route handling to avoid unsupported-network leaks.");
        Check(inbound.GetProperty("dns_mode").GetString() == "hijack", "TUN DNS must use the documented hijack mode.");
        Check(inbound.GetProperty("dns_address").GetArrayLength() == 2, "IPv4 and IPv6 DNS hijack addresses must be present.");

        var outbounds = config.GetProperty("outbounds");
        var socks = outbounds.EnumerateArray().Single(x => x.GetProperty("tag").GetString() == "tor-socks");
        Check(socks.GetProperty("server").GetString() == "127.0.0.1" && socks.GetProperty("server_port").GetInt32() == 9150,
            "Default outbound must target the local Tor SOCKS endpoint.");
        Check(config.GetProperty("route").GetProperty("auto_detect_interface").GetBoolean(),
            "The physical default interface must be detected to prevent Tor egress loops.");
        Check(config.GetProperty("route").GetProperty("final").GetString() == "tor-socks",
            "Unmatched traffic must use Tor SOCKS.");

        var routes = config.GetProperty("route").GetProperty("rules").EnumerateArray().ToArray();
        var processRule = routes.Single(x => x.TryGetProperty("process_path", out _));
        Check(processRule.GetProperty("process_path").GetArrayLength() >= 5 && processRule.GetProperty("outbound").GetString() == "tor-network",
            "All bundled Tor/transport processes must have an explicit physical-network bypass.");
        Check(routes.Any(x => x.TryGetProperty("protocol", out var protocol) && protocol.GetString() == "dns" && x.GetProperty("action").GetString() == "hijack-dns"),
            "DNS packets must be intercepted instead of escaping directly.");
        Check(routes.Any(x => x.TryGetProperty("network", out var network) && network.GetString() == "udp" &&
                              x.TryGetProperty("outbound", out var outbound) && outbound.GetString() == "block"),
            "Unsupported UDP must fail closed rather than leak outside Tor.");

        var dnsServer = config.GetProperty("dns").GetProperty("servers")[0];
        Check(dnsServer.GetProperty("type").GetString() == "https" && dnsServer.GetProperty("detour").GetString() == "tor-socks",
            "DNS-over-HTTPS must use Tor SOCKS.");
        Check(dnsServer.GetProperty("tls").GetProperty("enabled").GetBoolean(), "DNS-over-HTTPS TLS must be enabled.");

        try
        {
            TunRuntime.BuildConfiguration(root, 0);
            throw new InvalidOperationException("Invalid Tor SOCKS port should be rejected.");
        }
        catch (ArgumentOutOfRangeException) { }

        var markerRoot = Path.Combine(root, ".data", "tun-marker-test");
        var markerDirectory = Path.Combine(markerRoot, ".data", "tun");
        Directory.CreateDirectory(markerDirectory);
        var markerPath = Path.Combine(markerDirectory, "cleanup-unconfirmed");
        File.WriteAllText(markerPath, TunRuntime.CurrentBootSessionTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var markerRuntime = new TunRuntime(markerRoot, 9150);
        Check(markerRuntime.CleanupUnconfirmed, "An unconfirmed TUN cleanup from the current Windows boot must survive app restart.");
        File.WriteAllText(markerPath, (TunRuntime.CurrentBootSessionTicks - TimeSpan.FromMinutes(1).Ticks).ToString(System.Globalization.CultureInfo.InvariantCulture));
        markerRuntime = new TunRuntime(markerRoot, 9150);
        Check(!markerRuntime.CleanupUnconfirmed && !File.Exists(markerPath), "A cleanup warning from a previous Windows boot must be cleared after reboot.");
        Console.WriteLine("PASS TUN cleanup warning survives app restart and expires after a Windows reboot");

        var executable = Path.Combine(root, "runtime", "tunnel", "sing-box-1.14.4-windows-amd64", "sing-box.exe");
        if (File.Exists(executable))
        {
            var checkPath = Path.Combine(root, ".tmp", "sing-box-config-check.json");
            Directory.CreateDirectory(Path.GetDirectoryName(checkPath)!);
            try
            {
                File.WriteAllText(checkPath, configText);
                var start = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                start.ArgumentList.Add("check");
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add(checkPath);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not run sing-box config check.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15_000))
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException("sing-box config check exceeded 15 seconds.");
                }
                var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
                Check(process.ExitCode == 0, "sing-box rejected generated TUN config: " + output.Trim());
                Console.WriteLine("PASS sing-box 1.14.4 config parser without starting TUN");
            }
            finally { try { File.Delete(checkPath); } catch (IOException) { } }
        }
        Console.WriteLine("PASS sing-box TUN configuration routes Tor egress directly, proxies TCP via Tor, hijacks DNS, and blocks UDP");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OrbotPc.Core;

public sealed class TorRuntime : IAsyncDisposable
{
    private readonly string _basePath;
    private readonly string _dataPath;
    private readonly string _logPath;
    private readonly string _torExe;
    private readonly string _defaultsPath;
    private readonly string _configPath;
    private Process? _process;
    private OrbotSettings _settings = new();

    public event Action<string>? LogMessage;
    public event Action? Exited;
    public bool IsRunning => _process is { HasExited: false };

    public TorRuntime(string basePath)
    {
        _basePath = Path.GetFullPath(basePath);
        _dataPath = Path.Combine(_basePath, ".data", "tor");
        _logPath = Path.Combine(_basePath, ".data", "logs", "tor.log");
        _torExe = Path.Combine(_basePath, "runtime", "tor", "tor", "tor.exe");
        _defaultsPath = Path.Combine(_basePath, ".data", "torrc-defaults");
        _configPath = Path.Combine(_basePath, ".data", "torrc");
    }

    public async Task StartAsync(OrbotSettings settings, CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        if (!File.Exists(_torExe)) throw new FileNotFoundException("Tor Expert Bundle was not found beside this application.", _torExe);
        _settings = settings;
        Directory.CreateDirectory(_dataPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        WriteConfiguration(settings);
        await VerifyConfigurationAsync(cancellationToken);
        var cookiePath = Path.Combine(_dataPath, "control_auth_cookie");
        try { File.Delete(cookiePath); } catch (IOException) { }

        var start = new ProcessStartInfo
        {
            FileName = _torExe,
            WorkingDirectory = Path.GetDirectoryName(_torExe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("--defaults-torrc");
        start.ArgumentList.Add(_defaultsPath);
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add(_configPath);

        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogMessage?.Invoke(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogMessage?.Invoke(e.Data); };
        _process.Exited += (_, _) => Exited?.Invoke();
        if (!_process.Start()) throw new InvalidOperationException("Tor process could not start.");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process.HasExited)
                throw new InvalidOperationException($"Tor exited during startup (code {_process.ExitCode}). Check the runtime log.");
            if (File.Exists(cookiePath)) return;
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("Tor did not create its control authentication cookie within 20 seconds.");
    }

    public async Task ValidateConfigurationAsync(OrbotSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        Directory.CreateDirectory(_dataPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        WriteConfiguration(settings);
        await VerifyConfigurationAsync(cancellationToken);
    }

    public async Task StopAsync()
    {
        var process = _process;
        if (process is null) return;
        if (!process.HasExited)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await TorControlClient.SendAsync(_settings.ControlPort, _dataPath, "SIGNAL SHUTDOWN", timeout.Token);
            }
            catch { }
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
            }
            catch
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                try { await process.WaitForExitAsync(); } catch { }
            }
        }
        _process = null;
        process.Dispose();
    }

    public async Task<string> GetBootstrapAsync(CancellationToken cancellationToken = default)
    {
        var response = await TorControlClient.SendAsync(_settings.ControlPort, _dataPath, "GETINFO status/bootstrap-phase", cancellationToken);
        return response.FirstOrDefault(line => line.StartsWith("status/bootstrap-phase=", StringComparison.Ordinal)) ?? "";
    }

    public Task NewIdentityAsync(CancellationToken cancellationToken = default) =>
        TorControlClient.SendAsync(_settings.ControlPort, _dataPath, "SIGNAL NEWNYM", cancellationToken);

    public async Task ApplySettingsAsync(OrbotSettings settings, CancellationToken cancellationToken = default)
    {
        var wasRunning = IsRunning;
        if (wasRunning) await StopAsync();
        _settings = settings;
        if (wasRunning) await StartAsync(settings, cancellationToken);
        else WriteConfiguration(settings);
    }

    public IReadOnlyList<(string Name, string Hostname)> ReadOnionServiceAddresses(OrbotSettings settings)
    {
        var output = new List<(string, string)>();
        foreach (var service in settings.OnionServices)
        {
            var directory = Path.Combine(_dataPath, "onion-services", SafeName(service.Name));
            var hostnameFile = Path.Combine(directory, "hostname");
            var hostname = File.Exists(hostnameFile) ? File.ReadAllText(hostnameFile).Trim() : "等待 Tor 创建";
            output.Add((service.Name, hostname));
        }
        return output;
    }

    private async Task VerifyConfigurationAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = _torExe,
            WorkingDirectory = Path.GetDirectoryName(_torExe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--defaults-torrc");
        start.ArgumentList.Add(_defaultsPath);
        start.ArgumentList.Add("--verify-config");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add(_configPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not validate Tor configuration.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout) + (await stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException("Tor configuration is invalid: " + output.Trim());
        LogMessage?.Invoke("Tor configuration validated.");
    }

    private void WriteConfiguration(OrbotSettings settings)
    {
        settings.ValidateAdvanced();
        var bridgeMode = (settings.BridgeMode ?? "none").Trim().ToLowerInvariant();
        if (bridgeMode is not ("none" or "obfs4" or "snowflake" or "snowflake-amp" or "meek" or "webtunnel" or "dnstt" or "custom"))
            throw new InvalidOperationException($"Unsupported bridge mode: {settings.BridgeMode}.");
        var runtimeRoot = Path.Combine(_basePath, "runtime", "tor");
        var runtimeData = Path.Combine(runtimeRoot, "data");
        var ptDir = Path.Combine(runtimeRoot, "tor", "pluggable_transports");
        Directory.CreateDirectory(Path.Combine(_dataPath, "client-onion-auth"));
        var lines = new List<string>
        {
            "AvoidDiskWrites 1",
            "SafeLogging 1",
            "Log notice stdout",
            $"GeoIPFile {Quote(Path.Combine(runtimeData, "geoip"))}",
            $"GeoIPv6File {Quote(Path.Combine(runtimeData, "geoip6"))}",
            $"ClientTransportPlugin meek_lite,obfs2,obfs3,obfs4,scramblesuit,webtunnel exec {ExecPath(Path.Combine(ptDir, "lyrebird.exe"))}",
            $"ClientTransportPlugin snowflake exec {ExecPath(Path.Combine(ptDir, "lyrebird.exe"))}",
            $"ClientTransportPlugin dnstt exec {ExecPath(Path.Combine(_basePath, "runtime", "dnstt", "dnstt-client.exe"))}",
            $"ClientTransportPlugin conjure exec {ExecPath(Path.Combine(ptDir, "conjure-client.exe"))} -registerURL https://registration.refraction.network/api",
            $"DataDirectory {Quote(_dataPath)}",
            $"ClientOnionAuthDir {Quote(Path.Combine(_dataPath, "client-onion-auth"))}",
            $"PidFile {Quote(Path.Combine(_dataPath, "tor.pid"))}",
            $"SocksPort 127.0.0.1:{settings.SocksPort}{FormatListenerOptions(settings, includeIpv6: true)}",
            $"ControlPort 127.0.0.1:{settings.ControlPort}",
            "CookieAuthentication 1",
            $"CookieAuthFile {Quote(Path.Combine(_dataPath, "control_auth_cookie"))}"
        };
        if (settings.DisableNetwork) lines.Add("DisableNetwork 1");

        AddOptionalListeners(lines, settings);
        AddTorRoutingOptions(lines, settings);
        AddOutboundProxy(lines, settings);

        var bridgeLines = ResolveBridgeLines(settings);
        if (bridgeMode != "none")
        {
            if (bridgeLines.Count == 0) throw new InvalidOperationException("No bridge lines are available for the selected bridge mode.");
            lines.Add("UseBridges 1");
            foreach (var bridge in bridgeLines) lines.Add("Bridge " + bridge);
        }
        else
        {
            lines.Add("UseBridges 0");
        }

        if (!string.IsNullOrWhiteSpace(settings.ExitCountry))
        {
            var code = settings.ExitCountry.Trim().ToLowerInvariant();
            if (code.Length != 2 || !code.All(char.IsAsciiLetter)) throw new InvalidOperationException("Exit country must be a two-letter country code.");
            lines.Add($"ExitNodes {{{code}}}");
        }

        var serviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in settings.OnionServices)
        {
            if (service.VirtualPort is < 1 or > 65535 || service.TargetPort is < 1 or > 65535)
                throw new InvalidOperationException("Onion service ports must be between 1 and 65535.");
            var safeName = SafeName(service.Name);
            if (!serviceNames.Add(safeName)) throw new InvalidOperationException("Onion service names must be unique.");
            var serviceDirectory = Path.Combine(_dataPath, "onion-services", safeName);
            Directory.CreateDirectory(serviceDirectory);
            lines.Add($"HiddenServiceDir {Quote(serviceDirectory)}");
            lines.Add($"HiddenServicePort {service.VirtualPort} 127.0.0.1:{service.TargetPort}");
        }

        File.WriteAllLines(_defaultsPath, ["AvoidDiskWrites 1", "SafeLogging 1", "Log notice stdout"]);
        File.WriteAllLines(_configPath, lines, new UTF8Encoding(false));
    }

    private static void AddOptionalListeners(ICollection<string> lines, OrbotSettings settings)
    {
        if (settings.DnsPort > 0)
        {
            lines.Add($"DNSPort 127.0.0.1:{settings.DnsPort}{FormatIsolation(settings)}");
            lines.Add("VirtualAddrNetworkIPv4 " + settings.VirtualAddrNetwork);
            if (settings.AutomapHostsOnResolve)
            {
                lines.Add("AutomapHostsOnResolve 1");
            }
        }
        if (settings.TransPort > 0) lines.Add($"TransPort 127.0.0.1:{settings.TransPort}{FormatListenerOptions(settings, includeIpv6: true)}");
        if (settings.HttpTunnelPort > 0) lines.Add($"HTTPTunnelPort 127.0.0.1:{settings.HttpTunnelPort}{FormatListenerOptions(settings, includeIpv6: true)}");
    }

    private static void AddTorRoutingOptions(ICollection<string> lines, OrbotSettings settings)
    {
        AddIfPresent(lines, "EntryNodes", settings.EntryNodes);
        AddIfPresent(lines, "ExcludeNodes", settings.ExcludeNodes);
        AddIfPresent(lines, "ExcludeExitNodes", settings.ExcludeExitNodes);
        AddIfPresent(lines, "ReachableAddresses", settings.ReachableAddresses);
        if (settings.StrictNodes) lines.Add("StrictNodes 1");
        AddNullableBoolean(lines, "ConnectionPadding", settings.ConnectionPadding);
        AddNullableBoolean(lines, "CircuitPadding", settings.CircuitPadding);
        AddNullableBoolean(lines, "ReducedConnectionPadding", settings.ReducedConnectionPadding);
        AddNullableBoolean(lines, "ReducedCircuitPadding", settings.ReducedCircuitPadding);
        AddNullableBoolean(lines, "ClientUseIPv4", settings.ClientUseIPv4);
        AddNullableBoolean(lines, "ClientUseIPv6", settings.ClientUseIPv6);
    }

    private static string FormatListenerOptions(OrbotSettings settings, bool includeIpv6)
    {
        var options = IsolationOptions(settings);
        if (includeIpv6)
        {
            if (settings.IPv6Traffic == true) options.Add("IPv6Traffic");
            if (settings.PreferIPv6 == true) options.Add("PreferIPv6");
            if (settings.NoIPv4Traffic == true) options.Add("NoIPv4Traffic");
        }
        return FormatOptions(options);
    }

    private static string FormatIsolation(OrbotSettings settings) => FormatOptions(IsolationOptions(settings));

    private static string FormatOptions(IReadOnlyList<string> options) =>
        options.Count == 0 ? string.Empty : " " + string.Join(" ", options);

    private static List<string> IsolationOptions(OrbotSettings settings)
    {
        var options = new List<string>();
        if (settings.IsolateDestAddr == true) options.Add("IsolateDestAddr");
        if (settings.IsolateDestPort == true) options.Add("IsolateDestPort");
        if (settings.IsolateClientProtocol == true) options.Add("IsolateClientProtocol");
        if (settings.KeepAliveIsolateSocksAuth == true) options.Add("KeepAliveIsolateSOCKSAuth");
        return options;
    }

    private static void AddOutboundProxy(ICollection<string> lines, OrbotSettings settings)
    {
        var type = (settings.OutboundProxyType ?? "none").Trim().ToLowerInvariant();
        if (type == "none") return;
        var endpoint = FormatEndpoint(settings.OutboundProxyHost, settings.OutboundProxyPort);
        switch (type)
        {
            case "https":
                lines.Add($"HTTPSProxy {endpoint}");
                if (!string.IsNullOrWhiteSpace(settings.OutboundProxyUsername) || !string.IsNullOrWhiteSpace(settings.OutboundProxyPassword))
                    lines.Add($"HTTPSProxyAuthenticator {Quote(settings.OutboundProxyUsername + ":" + settings.OutboundProxyPassword)}");
                break;
            case "socks4":
                lines.Add($"Socks4Proxy {endpoint}");
                break;
            case "socks5":
                lines.Add($"Socks5Proxy {endpoint}");
                if (!string.IsNullOrWhiteSpace(settings.OutboundProxyUsername)) lines.Add($"Socks5ProxyUsername {Quote(settings.OutboundProxyUsername)}");
                if (!string.IsNullOrWhiteSpace(settings.OutboundProxyPassword)) lines.Add($"Socks5ProxyPassword {Quote(settings.OutboundProxyPassword)}");
                break;
        }
    }

    private static void AddIfPresent(ICollection<string> lines, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{name} {value.Trim()}");
    }

    private static void AddNullableBoolean(ICollection<string> lines, string name, bool? value)
    {
        if (value.HasValue) lines.Add($"{name} {(value.Value ? 1 : 0)}");
    }

    private static string FormatEndpoint(string host, int port)
    {
        var normalizedHost = host.Trim();
        if (normalizedHost.Contains(':') && !normalizedHost.StartsWith("[", StringComparison.Ordinal)) normalizedHost = "[" + normalizedHost + "]";
        return normalizedHost + ":" + port;
    }

    private IReadOnlyList<string> ResolveBridgeLines(OrbotSettings settings)
    {
        var mode = (settings.BridgeMode ?? "none").Trim().ToLowerInvariant();
        var custom = settings.CustomBridges.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length <= 2048 && !line.Contains('"') && !line.StartsWith('#'))
            .ToList();
        if (mode == "custom")
        {
            ValidateBridgeTransports(custom);
            return custom;
        }
        if (mode == "snowflake-amp")
        {
            if (custom.Count > 0)
                return AmpBridgeBuilder.BuildMany(custom);

            var bundledSnowflakeBridges = ReadBundledBridgeLines("snowflake");
            return AmpBridgeBuilder.BuildMany(bundledSnowflakeBridges);
        }
        if (mode == "dnstt")
        {
            if (custom.Count == 0) return ReadBundledBridgeLines(mode);
            ValidateBridgeTransports(custom);
            var matchingDnstt = custom.Where(line => line.StartsWith("dnstt ", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matchingDnstt.Length == 0) throw new InvalidOperationException("The supplied bridge lines do not contain DNSTT bridges.");
            return matchingDnstt;
        }
        if (custom.Count > 0)
        {
            var expectedPrefix = mode switch
            {
                "obfs4" => "obfs4 ",
                "snowflake" => "snowflake ",
                "meek" => "meek_lite ",
                "webtunnel" => "webtunnel ",
                _ => string.Empty
            };
            var matching = custom.Where(line => line.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length == 0)
                throw new InvalidOperationException($"The supplied bridge lines do not match the selected {mode} transport.");
            return matching;
        }
        return ReadBundledBridgeLines(mode);
    }

    private IReadOnlyList<string> ReadBundledBridgeLines(string mode)
    {
        var ptConfig = Path.Combine(_basePath, "runtime", "tor", "tor", "pluggable_transports", "pt_config.json");
        if (!File.Exists(ptConfig)) return [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ptConfig));
            if (doc.RootElement.TryGetProperty("bridges", out var bridges) && bridges.TryGetProperty(mode, out var items))
                return items.EnumerateArray().Select(item => item.GetString() ?? "").Where(line => line.Length > 0).ToList();
        }
        catch (JsonException) { }
        return [];
    }

    private static void ValidateBridgeTransports(IEnumerable<string> bridgeLines)
    {
        foreach (var line in bridgeLines)
        {
            var transport = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
            if (transport is not ("obfs4" or "obfs2" or "obfs3" or "scramblesuit" or "meek_lite" or "webtunnel" or "snowflake" or "conjure" or "dnstt"))
                throw new InvalidOperationException($"Unsupported pluggable transport in bridge line: {transport ?? "empty"}.");
        }
    }

    // Regular Tor path options use escaped Windows backslashes inside quotes.
    // Pluggable transport executables use ExecPath below because Tor passes
    // the exec token directly to CreateProcess on Windows.
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static string ExecPath(string value) => value.Replace('\\', '/').Replace("\"", "");
    private static string SafeName(string name)
    {
        var normalized = new string(name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "service" : normalized[..Math.Min(normalized.Length, 48)];
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _process?.Dispose();
    }
}

internal static class TorControlClient
{
    public static async Task<string[]> SendAsync(int port, string dataPath, string command, CancellationToken cancellationToken)
    {
        var cookiePath = Path.Combine(dataPath, "control_auth_cookie");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(cookiePath) && DateTime.UtcNow < deadline)
            await Task.Delay(100, cancellationToken);
        var cookie = await File.ReadAllBytesAsync(cookiePath, cancellationToken);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("AUTHENTICATE " + Convert.ToHexString(cookie).ToLowerInvariant());
        var auth = await ReadResponseAsync(reader, cancellationToken);
        EnsureSuccess(auth, "Tor control authentication failed.");
        await writer.WriteLineAsync(command);
        var response = await ReadResponseAsync(reader, cancellationToken);
        EnsureSuccess(response, "Tor control command failed.");
        return response.Select(line => line.StartsWith("250-", StringComparison.Ordinal) || line.StartsWith("250 ", StringComparison.Ordinal)
            ? line[4..]
            : line).Where(line => line.Length > 0).ToArray();
    }

    private static async Task<string[]> ReadResponseAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken) ?? throw new EndOfStreamException("Tor control port closed the connection.");
            lines.Add(line);
            if (line.Length < 4 || line[3] == ' ') break;
        }
        return lines.ToArray();
    }

    private static void EnsureSuccess(IReadOnlyList<string> lines, string message)
    {
        if (lines.Count == 0 || !lines[^1].StartsWith("250 ", StringComparison.Ordinal))
            throw new InvalidOperationException(message + " " + string.Join(" ", lines));
    }
}

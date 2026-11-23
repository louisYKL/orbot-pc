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
        var runtimeRoot = Path.Combine(_basePath, "runtime", "tor");
        var runtimeData = Path.Combine(runtimeRoot, "data");
        var ptDir = Path.Combine(runtimeRoot, "tor", "pluggable_transports");
        var lines = new List<string>
        {
            "AvoidDiskWrites 1",
            "SafeLogging 1",
            "Log notice stdout",
            $"GeoIPFile {Quote(Path.Combine(runtimeData, "geoip"))}",
            $"GeoIPv6File {Quote(Path.Combine(runtimeData, "geoip6"))}",
            $"ClientTransportPlugin meek_lite,obfs2,obfs3,obfs4,scramblesuit,webtunnel exec {ExecPath(Path.Combine(ptDir, "lyrebird.exe"))}",
            $"ClientTransportPlugin snowflake exec {ExecPath(Path.Combine(ptDir, "lyrebird.exe"))}",
            $"ClientTransportPlugin conjure exec {ExecPath(Path.Combine(ptDir, "conjure-client.exe"))} -registerURL https://registration.refraction.network/api",
            $"DataDirectory {Quote(_dataPath)}",
            $"PidFile {Quote(Path.Combine(_dataPath, "tor.pid"))}",
            $"SocksPort 127.0.0.1:{settings.SocksPort}",
            $"ControlPort 127.0.0.1:{settings.ControlPort}",
            "CookieAuthentication 1",
            $"CookieAuthFile {Quote(Path.Combine(_dataPath, "control_auth_cookie"))}"
        };
        if (settings.DisableNetwork) lines.Add("DisableNetwork 1");

        var bridgeLines = ResolveBridgeLines(settings);
        if (settings.BridgeMode != "none")
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

    private IReadOnlyList<string> ResolveBridgeLines(OrbotSettings settings)
    {
        var custom = settings.CustomBridges.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length <= 2048 && !line.Contains('"') && !line.StartsWith('#'))
            .ToList();
        if (custom.Count > 0) return custom;
        var ptConfig = Path.Combine(_basePath, "runtime", "tor", "tor", "pluggable_transports", "pt_config.json");
        if (!File.Exists(ptConfig)) return [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ptConfig));
            if (doc.RootElement.TryGetProperty("bridges", out var bridges) && bridges.TryGetProperty(settings.BridgeMode, out var items))
                return items.EnumerateArray().Select(item => item.GetString() ?? "").Where(line => line.Length > 0).ToList();
        }
        catch (JsonException) { }
        return [];
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

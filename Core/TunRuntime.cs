using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace OrbotPc.Core;

/// <summary>
/// Runs the optional Windows TUN engine. Starting this class changes the host
/// route table through sing-box; callers must only start it after Tor is ready.
/// </summary>
public sealed class TunRuntime : IAsyncDisposable
{
    private readonly string _root;
    private readonly int _torSocksPort;
    private readonly string _singBoxPath;
    private readonly string _configPath;
    private readonly string _cleanupMarkerPath;
    private Process? _process;
    private readonly string _logPath;
    private bool _shutdownRequested;

    public bool IsRunning => _process is { HasExited: false };
    public bool CleanupUnconfirmed { get; private set; }
    public event Action<string>? LogMessage;

    public TunRuntime(string basePath, int torSocksPort)
    {
        _root = Path.GetFullPath(basePath);
        _torSocksPort = torSocksPort;
        _singBoxPath = Path.Combine(_root, "runtime", "tunnel", "sing-box-1.14.4-windows-amd64", "sing-box.exe");
        _configPath = Path.Combine(_root, ".data", "tun", "config.json");
        _cleanupMarkerPath = Path.Combine(_root, ".data", "tun", "cleanup-unconfirmed");
        _logPath = Path.Combine(_root, ".data", "logs", "tun.log");
        CleanupUnconfirmed = LoadCleanupMarker();
    }

    internal static long CurrentBootSessionTicks => (DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64)).Ticks;

    public static string BuildConfiguration(string basePath, int torSocksPort)
    {
        if (torSocksPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(torSocksPort));
        var root = Path.GetFullPath(basePath);
        var torRuntime = Path.Combine(root, "runtime", "tor", "tor");
        var ptRuntime = Path.Combine(torRuntime, "pluggable_transports");
        var directPaths = new[]
        {
            Path.Combine(torRuntime, "tor.exe"),
            Path.Combine(ptRuntime, "lyrebird.exe"),
            Path.Combine(ptRuntime, "conjure-client.exe"),
            Path.Combine(root, "runtime", "dnstt", "dnstt-client.exe"),
            Path.Combine(root, "runtime", "snowflake", "snowflake-proxy.exe")
        };

        var config = new
        {
            log = new { level = "warn", timestamp = true, output = Path.Combine(root, ".data", "logs", "tun.log") },
            dns = new
            {
                servers = new[]
                {
                    new
                    {
                        type = "https",
                        tag = "tor-doh",
                        server = "1.1.1.1",
                        server_port = 443,
                        path = "/dns-query",
                        tls = new { enabled = true, server_name = "cloudflare-dns.com" },
                        detour = "tor-socks"
                    }
                },
                rules = new[] { new { action = "route", server = "tor-doh" } },
                final = "tor-doh",
                strategy = "prefer_ipv4",
                cache_capacity = 4096
            },
            inbounds = new[]
            {
                new
                {
                    type = "tun",
                    tag = "orbot-tun-in",
                    interface_name = "OrbotTUN",
                    address = new[] { "172.19.0.1/30", "fdfe:dcba:9876::1/126" },
                    dns_mode = "hijack",
                    dns_address = new[] { "172.19.0.2", "fdfe:dcba:9876::2" },
                    mtu = 1400,
                    auto_route = true,
                    strict_route = true,
                    stack = "system"
                }
            },
            outbounds = new object[]
            {
                new { type = "socks", tag = "tor-socks", server = "127.0.0.1", server_port = torSocksPort, version = "5" },
                new { type = "direct", tag = "tor-network" },
                new { type = "block", tag = "block" }
            },
            route = new
            {
                auto_detect_interface = true,
                final = "tor-socks",
                rules = new object[]
                {
                    new { process_path = directPaths, action = "route", outbound = "tor-network" },
                    new { protocol = "dns", action = "hijack-dns" },
                    new { network = "udp", action = "route", outbound = "block" }
                }
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (CleanupUnconfirmed)
            throw new InvalidOperationException("A previous sing-box process exited unexpectedly and route cleanup could not be confirmed. Check Windows network routing or restart the device before enabling TUN again.");
        if (IsRunning) return;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The TUN mode is supported only on Windows.");
        if (!IsElevated()) throw new UnauthorizedAccessException("Windows TUN mode requires an elevated administrator process.");
        if (!File.Exists(_singBoxPath)) throw new FileNotFoundException("The bundled sing-box TUN runtime is missing.", _singBoxPath);
        var wintunPath = Path.Combine(Path.GetDirectoryName(_singBoxPath)!, "wintun.dll");
        if (!File.Exists(wintunPath)) throw new FileNotFoundException("The official Wintun runtime must be beside sing-box.", wintunPath);
        foreach (var path in GetRequiredTorPaths())
            if (!File.Exists(path)) throw new FileNotFoundException("A required Tor or bridge executable is missing; refusing to route traffic without its TUN bypass.", path);
        if (!await IsTorSocksReadyAsync(_torSocksPort, cancellationToken))
            throw new InvalidOperationException("Tor SOCKS5 is not accepting connections. Start Tor and wait for its bootstrap before enabling TUN.");

        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        await File.WriteAllTextAsync(_configPath, BuildConfiguration(_root, _torSocksPort), new UTF8Encoding(false), cancellationToken);
        await ValidateConfigurationAsync(cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        WriteCleanupMarker();
        Process process;
        try { process = StartHiddenConsoleProcess(_singBoxPath, ["run", "-c", _configPath], Path.GetDirectoryName(_singBoxPath)!); }
        catch
        {
            ClearCleanupMarker();
            throw;
        }
        _process = process;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var startup = Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
            var exited = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(startup, exited) == exited)
                throw new InvalidOperationException($"sing-box exited while creating the Windows TUN interface (code {process.ExitCode}).");
            await startup;
            if (process.HasExited)
                throw new InvalidOperationException($"sing-box exited while creating the Windows TUN interface (code {process.ExitCode}).");
            LogMessage?.Invoke("sing-box TUN process is running; route configuration is owned by sing-box.");
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        var process = _process;
        if (process is null) return;
        if (process.HasExited)
        {
            var shutdownWasRequested = _shutdownRequested;
            var exitCode = process.ExitCode;
            _process = null;
            process.Dispose();
            _shutdownRequested = false;
            if (!shutdownWasRequested || exitCode != 0)
            {
                MarkCleanupUnconfirmed();
                var message = shutdownWasRequested
                    ? "sing-box exited with a non-zero code after shutdown was requested. Windows TUN route cleanup cannot be confirmed; check network routing or restart Windows before enabling TUN again."
                    : "sing-box exited unexpectedly. Its Windows TUN route and adapter cleanup cannot be confirmed; do not start another TUN instance until Windows network routing has been checked or the device has been restarted.";
                LogMessage?.Invoke(message);
                throw new InvalidOperationException(message);
            }
            LogMessage?.Invoke("sing-box exited after a requested shutdown.");
            return;
        }

        if (!process.HasExited)
        {
            if (!AttachConsole((uint)process.Id))
                throw new InvalidOperationException($"Could not attach to sing-box's hidden console (Win32 {Marshal.GetLastWin32Error()}); refusing to terminate TUN without graceful route cleanup.");
            try
            {
                if (!GenerateConsoleCtrlEvent(CtrlBreakEvent, (uint)process.Id))
                    throw new InvalidOperationException($"Could not request graceful sing-box shutdown (Win32 {Marshal.GetLastWin32Error()}).");
                _shutdownRequested = true;
            }
            finally { FreeConsole(); }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("sing-box did not confirm graceful shutdown; Tor and the application will remain running so TUN cleanup can be retried.");
            }
        }
        if (process.ExitCode != 0)
        {
            MarkCleanupUnconfirmed();
            _process = null;
            process.Dispose();
            _shutdownRequested = false;
            const string message = "sing-box exited with a non-zero code after shutdown was requested. Windows TUN route cleanup cannot be confirmed; check network routing or restart Windows before enabling TUN again.";
            LogMessage?.Invoke(message);
            throw new InvalidOperationException(message);
        }
        _process = null;
        _shutdownRequested = false;
        process.Dispose();
        ClearCleanupMarker();
        LogMessage?.Invoke("sing-box exited gracefully; its TUN interface and owned routes were released.");
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private bool LoadCleanupMarker()
    {
        if (!File.Exists(_cleanupMarkerPath)) return false;
        var currentBootTicks = CurrentBootSessionTicks;
        var tolerance = TimeSpan.FromSeconds(30).Ticks;
        if (long.TryParse(File.ReadAllText(_cleanupMarkerPath).Trim(), out var bootTicks) &&
            bootTicks >= currentBootTicks - tolerance && bootTicks <= currentBootTicks + tolerance)
            return true;
        ClearCleanupMarker();
        return false;
    }

    private void WriteCleanupMarker()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cleanupMarkerPath)!);
        File.WriteAllText(_cleanupMarkerPath, CurrentBootSessionTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void MarkCleanupUnconfirmed()
    {
        CleanupUnconfirmed = true;
        try { WriteCleanupMarker(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMessage?.Invoke("Could not persist the TUN cleanup warning: " + ex.Message);
        }
    }

    private void ClearCleanupMarker()
    {
        try { if (File.Exists(_cleanupMarkerPath)) File.Delete(_cleanupMarkerPath); }
        catch (IOException ex) { LogMessage?.Invoke("Could not clear the TUN cleanup marker: " + ex.Message); }
        catch (UnauthorizedAccessException ex) { LogMessage?.Invoke("Could not clear the TUN cleanup marker: " + ex.Message); }
        CleanupUnconfirmed = false;
    }

    private async Task ValidateConfigurationAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = _singBoxPath,
            WorkingDirectory = Path.GetDirectoryName(_singBoxPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("check");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(_configPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not validate sing-box configuration.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = (await stdout) + (await stderr);
            if (process.ExitCode != 0) throw new InvalidDataException("sing-box rejected its generated TUN configuration: " + output.Trim());
            LogMessage?.Invoke("sing-box TUN configuration validated.");
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private const uint CreateNewConsole = 0x00000010;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint StartfUseShowWindow = 0x00000001;
    private const ushort SwHide = 0;
    private const uint CtrlBreakEvent = 1;

    private static Process StartHiddenConsoleProcess(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var commandLine = new StringBuilder(QuoteArgument(executable));
        foreach (var argument in arguments) commandLine.Append(' ').Append(QuoteArgument(argument));
        var startup = new StartupInfo
        {
            Size = (uint)Marshal.SizeOf<StartupInfo>(),
            Flags = StartfUseShowWindow,
            ShowWindow = SwHide
        };
        if (!CreateProcess(executable, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                CreateNewConsole | CreateNewProcessGroup, IntPtr.Zero, workingDirectory, ref startup, out var info))
            throw new InvalidOperationException($"Could not start sing-box TUN runtime (Win32 {Marshal.GetLastWin32Error()}).");
        CloseHandle(info.Thread);
        CloseHandle(info.Process);
        return Process.GetProcessById(checked((int)info.ProcessId));
    }

    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"')
            {
                result.Append('\\', slashes * 2 + 1).Append('"');
                slashes = 0;
                continue;
            }
            result.Append('\\', slashes).Append(character);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"');
        return result.ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string applicationName, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);

    private string[] GetRequiredTorPaths()
    {
        var tor = Path.Combine(_root, "runtime", "tor", "tor");
        var pt = Path.Combine(tor, "pluggable_transports");
        return
        [
            Path.Combine(tor, "tor.exe"),
            Path.Combine(pt, "lyrebird.exe"),
            Path.Combine(pt, "conjure-client.exe"),
            Path.Combine(_root, "runtime", "dnstt", "dnstt-client.exe"),
            Path.Combine(_root, "runtime", "snowflake", "snowflake-proxy.exe")
        ];
    }

    private static async Task<bool> IsTorSocksReadyAsync(int port, CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535) return false;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, cancellationToken);
            return true;
        }
        catch (SocketException) { return false; }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

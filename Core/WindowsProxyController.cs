using Microsoft.Win32;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OrbotPc.Core;

public sealed class WindowsProxyController
{
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private readonly string _backupPath;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);
            if (key is null || !File.Exists(_backupPath)) return false;
            var backup = ReadBackup();
            return backup is not null && key.GetValue("ProxyEnable") is int value && value == 1
                && string.Equals(key.GetValue("ProxyServer") as string, backup.OwnedProxyServer, StringComparison.OrdinalIgnoreCase);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public WindowsProxyController(string stateDirectory)
    {
        Directory.CreateDirectory(stateDirectory);
        _backupPath = Path.Combine(stateDirectory, "windows-proxy-backup.json");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Enable(int httpPort)
    {
        if (httpPort is < 1 or > 65535) throw new InvalidOperationException("HTTP proxy port must be between 1 and 65535.");
        if (IsEnabled) return;
        var snapshot = new ProxyBackup(Capture(), $"http=127.0.0.1:{httpPort};https=127.0.0.1:{httpPort}");
        File.WriteAllText(_backupPath, JsonSerializer.Serialize(snapshot));
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true)
                ?? throw new InvalidOperationException("Could not open the current user's Windows Internet Settings.");
            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", snapshot.OwnedProxyServer, RegistryValueKind.String);
            key.SetValue("ProxyOverride", "<local>;localhost;127.0.0.1", RegistryValueKind.String);
            NotifySettingsChanged();
        }
        catch
        {
            RestoreSnapshot(snapshot.Original);
            TryDeleteBackup();
            throw;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public bool Disable()
    {
        if (!File.Exists(_backupPath)) return false;
        var backup = ReadBackup();
        if (backup is null) { TryDeleteBackup(); return false; }
        var current = Capture();
        var ownsCurrent = current.ProxyEnable.Exists && Convert.ToInt32(current.ProxyEnable.Value ?? 0) == 1
            && current.ProxyServer.Exists
            && string.Equals(current.ProxyServer.Value as string, backup.OwnedProxyServer, StringComparison.OrdinalIgnoreCase);
        if (ownsCurrent)
        {
            RestoreSnapshot(backup.Original);
        }
        TryDeleteBackup();
        NotifySettingsChanged();
        return ownsCurrent;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void RestoreIfOwned()
    {
        if (!File.Exists(_backupPath) || !IsEnabled) return;
        Disable();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static async Task<(string Host, int Port)?> DetectLoopbackSocks5Async(
        IReadOnlySet<int>? excludedPorts = null, CancellationToken cancellationToken = default)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);
        if (key?.GetValue("ProxyEnable") is not int enabled || enabled != 1) return null;
        if (key.GetValue("ProxyServer") is not string configured || string.IsNullOrWhiteSpace(configured)) return null;

        foreach (var entry in configured.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator >= 0 && !entry[..separator].Equals("socks", StringComparison.OrdinalIgnoreCase)) continue;
            var endpoint = separator >= 0 ? entry[(separator + 1)..].Trim() : entry;
            if (!TryParseLoopbackEndpoint(endpoint, out var host, out var address, out var port)) continue;
            if (excludedPorts?.Contains(port) == true) continue;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                using var client = new TcpClient(address.AddressFamily);
                await client.ConnectAsync(address, port, timeout.Token);
                await using var stream = client.GetStream();
                await stream.WriteAsync(new byte[] { 5, 1, 0 }, timeout.Token);
                var reply = new byte[2];
                await stream.ReadExactlyAsync(reply, timeout.Token);
                if (reply[0] == 5 && reply[1] == 0) return (host, port);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (SocketException) { }
            catch (IOException) { }
        }
        return null;
    }

    private static bool TryParseLoopbackEndpoint(string endpoint, out string host, out IPAddress address, out int port)
    {
        host = "";
        address = IPAddress.None;
        port = 0;
        string portText;
        if (endpoint.StartsWith("[", StringComparison.Ordinal))
        {
            var end = endpoint.IndexOf(']');
            if (end < 0 || end + 1 >= endpoint.Length || endpoint[end + 1] != ':') return false;
            host = endpoint[1..end];
            portText = endpoint[(end + 2)..];
        }
        else
        {
            var separator = endpoint.LastIndexOf(':');
            if (separator <= 0) return false;
            host = endpoint[..separator];
            portText = endpoint[(separator + 1)..];
        }
        if (!IPAddress.TryParse(host, out var parsedAddress) || parsedAddress is null || !IPAddress.IsLoopback(parsedAddress) ||
            !int.TryParse(portText, out port) || port is < 1 or > 65535) return false;
        address = parsedAddress;
        host = address.ToString();
        return true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static RegistrySnapshot Capture()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false)
            ?? throw new InvalidOperationException("Could not read the current user's Windows Internet Settings.");
        return new RegistrySnapshot(
            Read(key, "ProxyEnable"),
            Read(key, "ProxyServer"),
            Read(key, "ProxyOverride"));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static RegistryEntry Read(RegistryKey key, string name)
    {
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null) return new RegistryEntry(false, null, null, RegistryValueKind.String);
        var kind = key.GetValueKind(name);
        return kind == RegistryValueKind.DWord
            ? new RegistryEntry(true, null, Convert.ToInt32(value), kind)
            : new RegistryEntry(true, Convert.ToString(value), null, kind);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestoreSnapshot(RegistrySnapshot snapshot)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true)
            ?? throw new InvalidOperationException("Could not restore the current user's Windows Internet Settings.");
        Restore(key, "ProxyEnable", snapshot.ProxyEnable);
        Restore(key, "ProxyServer", snapshot.ProxyServer);
        Restore(key, "ProxyOverride", snapshot.ProxyOverride);
        NotifySettingsChanged();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Restore(RegistryKey key, string name, RegistryEntry entry)
    {
        if (entry.Exists) key.SetValue(name, entry.Kind == RegistryValueKind.DWord ? entry.DwordValue ?? 0 : entry.StringValue ?? "", entry.Kind);
        else key.DeleteValue(name, throwOnMissingValue: false);
    }

    private ProxyBackup? ReadBackup()
    {
        try { return JsonSerializer.Deserialize<ProxyBackup>(File.ReadAllText(_backupPath)); }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    private void TryDeleteBackup()
    {
        try { File.Delete(_backupPath); } catch { }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void NotifySettingsChanged()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int bufferLength);

    private sealed record RegistryEntry(bool Exists, string? StringValue, int? DwordValue, RegistryValueKind Kind)
    {
        public object? Value => Kind == RegistryValueKind.DWord ? DwordValue : StringValue;
    }
    private sealed record RegistrySnapshot(RegistryEntry ProxyEnable, RegistryEntry ProxyServer, RegistryEntry ProxyOverride);
    private sealed record ProxyBackup(RegistrySnapshot Original, string OwnedProxyServer);
}

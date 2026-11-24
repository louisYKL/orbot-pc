namespace OrbotPc.Core;

public sealed class OrbotSettings
{
    public int SettingsVersion { get; set; } = 2;
    public int SocksPort { get; set; } = 9150;
    public int HttpPort { get; set; } = 8118;
    public int ControlPort { get; set; } = 9151;
    public bool UseWindowsProxy { get; set; } = true;
    public bool StartOnLogin { get; set; }
    public string BridgeMode { get; set; } = "none";
    public string CustomBridges { get; set; } = "";
    public string ExitCountry { get; set; } = "";
    public bool DisableNetwork { get; set; }
    // Windows-native equivalents for the Tor controls exposed by Orbot. A zero
    // port keeps the optional listener disabled and avoids opening extra local
    // sockets unless the user explicitly configures one.
    public int DnsPort { get; set; }
    public int TransPort { get; set; }
    public int HttpTunnelPort { get; set; }
    public bool AutomapHostsOnResolve { get; set; }
    public string VirtualAddrNetwork { get; set; } = "10.192.0.0/10";
    public string EntryNodes { get; set; } = "";
    public string ExcludeNodes { get; set; } = "";
    public string ExcludeExitNodes { get; set; } = "";
    public bool StrictNodes { get; set; }
    public string ReachableAddresses { get; set; } = "";
    public bool? ConnectionPadding { get; set; }
    public bool? CircuitPadding { get; set; }
    public bool? ReducedConnectionPadding { get; set; }
    public bool? ReducedCircuitPadding { get; set; }
    public bool? ClientUseIPv4 { get; set; }
    public bool? ClientUseIPv6 { get; set; }
    public bool? PreferIPv6 { get; set; }
    public bool? IPv6Traffic { get; set; }
    public bool? NoIPv4Traffic { get; set; }
    public bool? IsolateDestAddr { get; set; }
    public bool? IsolateDestPort { get; set; }
    public bool? IsolateClientProtocol { get; set; }
    public bool? KeepAliveIsolateSocksAuth { get; set; }

    // Optional upstream proxy used by Tor to reach relays/bridges. Windows
    // system proxy settings are separate and are handled by WindowsProxyController.
    public string OutboundProxyType { get; set; } = "none";
    public string OutboundProxyHost { get; set; } = "";
    public int OutboundProxyPort { get; set; }
    public string OutboundProxyUsername { get; set; } = "";
    public string OutboundProxyPassword { get; set; } = "";

    // When enabled, SmartConnectPlanner supplies the ordered transport fallback
    // sequence. The runtime remains deterministic and never silently changes a
    // user-selected bridge unless this flag is explicitly enabled.
    public bool EnableSmartConnect { get; set; }
    public List<string> SmartConnectModes { get; set; } = ["none", "snowflake", "snowflake-amp", "obfs4", "dnstt"];
    public int SmartConnectFallbackSeconds { get; set; } = 90;
    public List<OnionServiceEntry> OnionServices { get; set; } = [];

    public void ValidateAdvanced()
    {
        EntryNodes ??= "";
        ExcludeNodes ??= "";
        ExcludeExitNodes ??= "";
        ReachableAddresses ??= "";
        VirtualAddrNetwork ??= "10.192.0.0/10";
        OutboundProxyType ??= "none";
        OutboundProxyHost ??= "";
        OutboundProxyUsername ??= "";
        OutboundProxyPassword ??= "";
        SmartConnectModes ??= ["none", "snowflake", "snowflake-amp", "obfs4", "dnstt"];
        OnionServices ??= [];
        ValidateOptionalPort(DnsPort, nameof(DnsPort));
        ValidateOptionalPort(TransPort, nameof(TransPort));
        ValidateOptionalPort(HttpTunnelPort, nameof(HttpTunnelPort));
        if (DnsPort == SocksPort || DnsPort == HttpPort || DnsPort == ControlPort ||
            TransPort == SocksPort || TransPort == HttpPort || TransPort == ControlPort ||
            HttpTunnelPort == SocksPort || HttpTunnelPort == HttpPort || HttpTunnelPort == ControlPort)
            throw new InvalidOperationException("Optional Tor listener ports must not overlap SOCKS5, HTTP, or control ports.");
        var listeners = new[] { SocksPort, HttpPort, ControlPort, DnsPort, TransPort, HttpTunnelPort }.Where(port => port > 0).ToArray();
        if (listeners.Distinct().Count() != listeners.Length)
            throw new InvalidOperationException("Tor listener ports must be unique.");

        ValidateSelector(EntryNodes, nameof(EntryNodes));
        ValidateSelector(ExcludeNodes, nameof(ExcludeNodes));
        ValidateSelector(ExcludeExitNodes, nameof(ExcludeExitNodes));
        ValidateSelector(ReachableAddresses, nameof(ReachableAddresses));
        ValidateVirtualNetwork(VirtualAddrNetwork);

        var proxyType = (OutboundProxyType ?? "none").Trim().ToLowerInvariant();
        if (proxyType is not ("none" or "https" or "socks4" or "socks5"))
            throw new InvalidOperationException("Outbound proxy type must be none, https, socks4, or socks5.");
        if (proxyType == "none")
        {
            if (OutboundProxyPort != 0 || !string.IsNullOrWhiteSpace(OutboundProxyHost) ||
                !string.IsNullOrWhiteSpace(OutboundProxyUsername) || !string.IsNullOrWhiteSpace(OutboundProxyPassword))
                throw new InvalidOperationException("Outbound proxy host, port, and credentials require an enabled proxy type.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(OutboundProxyHost)) throw new InvalidOperationException("Outbound proxy host is required.");
            if (OutboundProxyPort is < 1 or > 65535) throw new InvalidOperationException("Outbound proxy port must be between 1 and 65535.");
            ValidateSingleLine(OutboundProxyHost, nameof(OutboundProxyHost), 255);
            ValidateSingleLine(OutboundProxyUsername, nameof(OutboundProxyUsername), 255);
            ValidateSingleLine(OutboundProxyPassword, nameof(OutboundProxyPassword), 1024);
            if (proxyType == "socks4" &&
                (!string.IsNullOrWhiteSpace(OutboundProxyUsername) || !string.IsNullOrWhiteSpace(OutboundProxyPassword)))
                throw new InvalidOperationException("SOCKS4 outbound proxies do not support username/password authentication in Tor.");
        }

        if (SmartConnectModes.Count == 0)
            throw new InvalidOperationException("Smart Connect requires at least one transport mode.");
        if (SmartConnectFallbackSeconds is < 10 or > 900)
            throw new InvalidOperationException("Smart Connect fallback timeout must be between 10 and 900 seconds.");
        foreach (var mode in SmartConnectModes)
        {
            var normalized = mode.Trim().ToLowerInvariant();
            if (normalized is not ("none" or "snowflake" or "snowflake-amp" or "obfs4" or "meek" or "webtunnel" or "dnstt" or "custom"))
                throw new InvalidOperationException($"Unsupported Smart Connect mode: {mode}.");
        }
    }

    private static void ValidateOptionalPort(int value, string name)
    {
        if (value is < 0 or > 65535) throw new InvalidOperationException($"{name} must be 0 or between 1 and 65535.");
    }

    private static void ValidateVirtualNetwork(string value)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !int.TryParse(parts[1], out var prefix) || prefix is < 1 or > 32)
            throw new InvalidOperationException("Virtual address network must be an IPv4 CIDR, such as 10.192.0.0/10.");
    }

    private static void ValidateSelector(string value, string name)
    {
        if (value.Length > 2048) throw new InvalidOperationException($"{name} is limited to 2048 characters.");
        ValidateSingleLine(value, name, 2048);
    }

    private static void ValidateSingleLine(string value, string name, int maxLength)
    {
        if (value.Length > maxLength || value.Any(c => c is '\r' or '\n' or '\0' || c == '"'))
            throw new InvalidOperationException($"{name} contains an invalid character or is too long.");
    }
}

public sealed class OnionServiceEntry
{
    public string Name { get; set; } = "";
    public int VirtualPort { get; set; } = 80;
    public int TargetPort { get; set; } = 8080;
}

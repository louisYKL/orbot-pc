namespace OrbotPc.Core;

public sealed class OrbotSettings
{
    public int SettingsVersion { get; set; } = 2;
    public int SocksPort { get; set; } = 9150;
    public int HttpPort { get; set; } = 8118;
    public int ControlPort { get; set; } = 9151;
    public bool UseWindowsProxy { get; set; } = true;
    public string BridgeMode { get; set; } = "none";
    public string CustomBridges { get; set; } = "";
    public string ExitCountry { get; set; } = "";
    public bool DisableNetwork { get; set; }
    public List<OnionServiceEntry> OnionServices { get; set; } = [];
}

public sealed class OnionServiceEntry
{
    public string Name { get; set; } = "";
    public int VirtualPort { get; set; } = 80;
    public int TargetPort { get; set; } = 8080;
}

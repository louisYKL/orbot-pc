using OrbotPc.Core;

namespace OrbotPc.Tests;

internal static class AmpBridgeBuilderTests
{
    public static void Run()
    {
        const string bundled = "snowflake 192.0.2.3:80 2B280B23E1107BB62ABFC40DDCC8824814F80A72 " +
            "url=https://1098762253.rsc.cdn77.org/ fronts=app.datapacket.com ice=stun:stun.epygi.com:3478 " +
            "utls-imitate=hellorandomizedalpn";
        var line = AmpBridgeBuilder.Build(bundled);

        Check(line.StartsWith("snowflake 192.0.2.5:80 2B280B23E1107BB62ABFC40DDCC8824814F80A72 ", StringComparison.Ordinal), "AMP builder did not preserve fingerprint and port.");
        Check(line.Contains("url=https://snowflake-broker.torproject.net/", StringComparison.Ordinal), "AMP builder did not set the broker URL.");
        Check(line.Contains("ampcache=https://cdn.ampproject.org/", StringComparison.Ordinal), "AMP builder did not set the AMP cache URL.");
        Check(line.Contains("front=www.google.com", StringComparison.Ordinal), "AMP builder did not set the AMP front.");
        Check(line.Contains("ice=stun:stun.epygi.com:3478", StringComparison.Ordinal), "AMP builder did not preserve ICE servers.");
        Check(line.Contains("utls-imitate=hellorandomizedalpn", StringComparison.Ordinal), "AMP builder did not preserve uTLS imitation.");
        Check(!line.Contains("fronts=", StringComparison.Ordinal), "AMP builder must emit the AMP singular front argument.");

        var many = AmpBridgeBuilder.BuildMany([bundled, bundled.Replace("2B280B23E1107BB62ABFC40DDCC8824814F80A72", "8838024498816A039FCBBAB14E6F40A0843051FA")]);
        Check(many.Count == 2 && many[1].StartsWith("snowflake 192.0.2.6:80 ", StringComparison.Ordinal), "AMP builder did not assign deterministic TEST-NET placeholders.");
        ExpectFailure(() => AmpBridgeBuilder.Build(bundled.Replace("ice=stun:stun.epygi.com:3478", "")), "missing ICE was accepted");
        ExpectFailure(() => AmpBridgeBuilder.Build(bundled, "10.0.0.1"), "non TEST-NET placeholder was accepted");
        Console.WriteLine("PASS Snowflake AMP bridge builder");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectFailure(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException(message);
    }
}

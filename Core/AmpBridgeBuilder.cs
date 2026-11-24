using System.Text.RegularExpressions;

namespace OrbotPc.Core;

/// <summary>
/// Builds a Snowflake AMP-cache bridge line from a bundled Snowflake bridge.
/// AMP rendezvous is implemented by the bundled Snowflake client; the TEST-NET
/// address is a syntax-only placeholder and is never a reachable relay.
/// </summary>
public static class AmpBridgeBuilder
{
    public const string DefaultBrokerUrl = "https://snowflake-broker.torproject.net/";
    public const string DefaultAmpCacheUrl = "https://cdn.ampproject.org/";
    public const string DefaultFrontDomain = "www.google.com";

    private static readonly Regex FingerprintPattern = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex HostPattern = new("^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\\.)+[A-Za-z]{2,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex Ipv4Pattern = new("^(?:[0-9]{1,3}\\.){3}[0-9]{1,3}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Converts one bundled Snowflake bridge to AMP-cache rendezvous.
    /// </summary>
    /// <param name="bundledBridge">A complete bridge line without the leading "Bridge" keyword.</param>
    /// <param name="placeholderAddress">
    /// A TEST-NET-1 IPv4 address used as the bridge endpoint in the Tor bridge
    /// line.  The Snowflake client uses the fingerprint for the relay; this
    /// address is a required syntactic field and is never contacted directly.
    /// </param>
    public static string Build(
        string bundledBridge,
        string? placeholderAddress = null,
        string brokerUrl = DefaultBrokerUrl,
        string ampCacheUrl = DefaultAmpCacheUrl,
        string frontDomain = DefaultFrontDomain)
    {
        if (string.IsNullOrWhiteSpace(bundledBridge))
            throw new ArgumentException("A bundled Snowflake bridge line is required.", nameof(bundledBridge));
        if (bundledBridge.Any(c => c is '\r' or '\n' or '\0'))
            throw new ArgumentException("The bridge line contains a control character.", nameof(bundledBridge));

        var tokens = bundledBridge.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || !string.Equals(tokens[0], "snowflake", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The bridge line must be a Snowflake bridge.", nameof(bundledBridge));

        var endpoint = ParseEndpoint(tokens[1]);
        var fingerprint = FindFingerprint(tokens);
        if (fingerprint is null)
            throw new ArgumentException("The Snowflake bridge does not contain a valid relay fingerprint.", nameof(bundledBridge));

        var ice = FindValue(tokens, "ice");
        if (string.IsNullOrWhiteSpace(ice))
            throw new ArgumentException("The Snowflake bridge does not contain ICE servers.", nameof(bundledBridge));

        var utls = FindValue(tokens, "utls");
        var utlsImitate = FindValue(tokens, "utls-imitate");
        var address = ValidatePlaceholderAddress(placeholderAddress ?? "192.0.2.5");
        var broker = ValidateHttpsUrl(brokerUrl, nameof(brokerUrl));
        var cache = ValidateHttpsUrl(ampCacheUrl, nameof(ampCacheUrl));
        var front = ValidateFrontDomain(frontDomain);

        var result = new List<string>
        {
            "snowflake",
            $"{address}:{endpoint.Port}",
            fingerprint,
            $"url={broker}",
            $"ampcache={cache}",
            $"front={front}",
            $"ice={ice}"
        };
        if (!string.IsNullOrWhiteSpace(utls)) result.Add($"utls={ValidateTokenValue(utls, "utls")}");
        if (!string.IsNullOrWhiteSpace(utlsImitate)) result.Add($"utls-imitate={ValidateTokenValue(utlsImitate, "utls-imitate")}");
        return string.Join(' ', result);
    }

    /// <summary>Builds deterministic AMP bridge lines for all bundled Snowflake bridges.</summary>
    public static IReadOnlyList<string> BuildMany(IEnumerable<string> bundledBridges)
    {
        ArgumentNullException.ThrowIfNull(bundledBridges);
        var result = new List<string>();
        var index = 5;
        foreach (var bridge in bundledBridges)
        {
            if (string.IsNullOrWhiteSpace(bridge)) continue;
            if (index > 254) throw new ArgumentException("Too many Snowflake bridges for TEST-NET-1 placeholders.", nameof(bundledBridges));
            result.Add(Build(bridge, $"192.0.2.{index}"));
            index++;
        }
        if (result.Count == 0) throw new ArgumentException("At least one Snowflake bridge is required.", nameof(bundledBridges));
        return result;
    }

    private static (string Host, int Port) ParseEndpoint(string value)
    {
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1 || !int.TryParse(value[(separator + 1)..], out var port) || port is < 1 or > 65535)
            throw new ArgumentException("The Snowflake bridge endpoint is invalid.", nameof(value));
        var host = value[..separator];
        if (!Ipv4Pattern.IsMatch(host) || host.Split('.').Any(part => !int.TryParse(part, out var octet) || octet > 255))
            throw new ArgumentException("The Snowflake bridge endpoint must use an IPv4 address.", nameof(value));
        return (host, port);
    }

    private static string? FindFingerprint(IReadOnlyList<string> tokens)
    {
        foreach (var token in tokens.Skip(2))
        {
            var value = token.StartsWith("fingerprint=", StringComparison.OrdinalIgnoreCase) ? token[12..] : token;
            if (FingerprintPattern.IsMatch(value)) return value.ToUpperInvariant();
        }
        return null;
    }

    private static string? FindValue(IEnumerable<string> tokens, string name)
    {
        var token = tokens.FirstOrDefault(token => token.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));
        return token is null ? null : token[(name.Length + 1)..];
    }

    private static string ValidatePlaceholderAddress(string value)
    {
        if (!Ipv4Pattern.IsMatch(value) || value.Split('.').Any(part => !int.TryParse(part, out var octet) || octet > 255))
            throw new ArgumentException("The AMP bridge placeholder must be an IPv4 address.", nameof(value));
        var octets = value.Split('.').Select(int.Parse).ToArray();
        if (octets[0] != 192 || octets[1] != 0 || octets[2] != 2 || octets[3] is < 1 or > 254)
            throw new ArgumentException("The AMP bridge placeholder must be in TEST-NET-1 (192.0.2.1-254).", nameof(value));
        return value;
    }

    private static string ValidateHttpsUrl(string value, string name)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("The AMP broker and cache URLs must be absolute HTTPS URLs.", name);
        return uri.AbsoluteUri;
    }

    private static string ValidateFrontDomain(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (!HostPattern.IsMatch(normalized))
            throw new ArgumentException("The AMP front must be a DNS hostname.", nameof(value));
        return normalized;
    }

    private static string ValidateTokenValue(string value, string name)
    {
        if (value.Any(char.IsWhiteSpace) || value.Contains('=') || value.Contains('"') || value.Contains('\''))
            throw new ArgumentException($"The Snowflake {name} value is invalid.", name);
        return value;
    }
}

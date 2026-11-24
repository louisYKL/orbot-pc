namespace OrbotPc.Core;

/// <summary>
/// Deterministic Windows equivalent of Orbot's Smart Connect policy. It only
/// chooses the next configured transport; starting Tor and deciding whether a
/// bridge actually bootstraps remain the responsibility of TorRuntime/UI.
/// </summary>
public sealed class SmartConnectPlanner
{
    private static readonly HashSet<string> SupportedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "snowflake", "snowflake-amp", "obfs4", "meek", "webtunnel", "dnstt", "custom"
    };

    private readonly IReadOnlyList<string> _modes;
    private int _index;

    public SmartConnectPlanner(IEnumerable<string>? modes = null)
    {
        var selected = (modes ?? ["none", "snowflake", "obfs4"])
            .Select(Normalize)
            .ToArray();
        var unsupported = selected.Where(mode => !SupportedModes.Contains(mode)).Distinct().ToArray();
        if (unsupported.Length > 0)
            throw new ArgumentException("Unsupported Smart Connect mode(s): " + string.Join(", ", unsupported), nameof(modes));

        _modes = selected
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (_modes.Count == 0) throw new ArgumentException("Smart Connect requires at least one supported transport mode.", nameof(modes));
    }

    public IReadOnlyList<string> Modes => _modes;
    public string Current => _modes[Math.Min(_index, _modes.Count - 1)];
    public bool HasNext => _index + 1 < _modes.Count;

    public string Reset()
    {
        _index = 0;
        return Current;
    }

    public string? Advance()
    {
        if (!HasNext) return null;
        _index++;
        return Current;
    }

    public bool TryAdvance(out string nextMode)
    {
        var next = Advance();
        nextMode = next ?? string.Empty;
        return next is not null;
    }

    public static bool IsSupported(string mode) => SupportedModes.Contains(Normalize(mode));

    private static string Normalize(string? mode) => (mode ?? string.Empty).Trim().ToLowerInvariant();
}

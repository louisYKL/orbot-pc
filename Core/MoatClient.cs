using System.Net;
using System.Net.Http;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbotPc.Core;

/// <summary>
/// Windows-native client for the Tor Project Moat circumvention service.
/// The default HttpClientHandler uses the Windows system trust store. A proxy
/// can be supplied when the caller already has a working egress proxy; no
/// proxy is invented or silently taken from the Windows system proxy.
/// </summary>
public sealed class MoatClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public Uri BaseUri { get; }

    public MoatClient(
        Uri baseUri,
        HttpClient? httpClient = null,
        IWebProxy? proxy = null,
        TimeSpan? timeout = null)
    {
        if (baseUri is null || !baseUri.IsAbsoluteUri || baseUri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(baseUri.Host))
            throw new ArgumentException("Moat base URL must be an absolute HTTPS URL.", nameof(baseUri));

        BaseUri = new Uri(baseUri.AbsoluteUri.EndsWith('/') ? baseUri.AbsoluteUri : baseUri.AbsoluteUri + '/', UriKind.Absolute);
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            // Do not set ServerCertificateCustomValidationCallback: system TLS
            // validation is required for Moat and its bridge responses.
            var handler = new HttpClientHandler
            {
                UseProxy = proxy is not null,
                Proxy = proxy
            };
            _httpClient = new HttpClient(handler, disposeHandler: true);
            _ownsHttpClient = true;
        }

        if (_ownsHttpClient) _httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        if (!_httpClient.DefaultRequestHeaders.Accept.Any(value =>
                string.Equals(value.MediaType, "application/vnd.api+json", StringComparison.OrdinalIgnoreCase)))
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
    }

    public Task<MoatSettingsResponse> FetchSettingsAsync(
        string? country = null,
        IEnumerable<MoatTransportKind>? transports = null,
        CancellationToken cancellationToken = default) =>
        PostAsync("settings", new MoatSettingsRequest
        {
            Country = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToLowerInvariant(),
            Transports = (transports ?? MoatTransportKindDefaults.All)
                .Select(ToWireName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        }, cancellationToken);

    public Task<MoatSettingsResponse> FetchDefaultsAsync(CancellationToken cancellationToken = default) =>
        PostAsync("defaults", new MoatSettingsRequest { Transports = MoatTransportKindDefaults.All.Select(ToWireName).ToArray() }, cancellationToken);

    public async Task<MoatBuiltinBridges> FetchBuiltinAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(new Uri(BaseUri, "builtin"), cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        try
        {
            return JsonSerializer.Deserialize<MoatBuiltinBridges>(body, JsonOptions)
                ?? throw new MoatApiException("Moat returned an empty builtin bridge document.");
        }
        catch (JsonException ex)
        {
            throw new MoatApiException("Moat returned malformed builtin bridge JSON.", null, ex);
        }
    }

    /// <summary>
    /// Applies Orbot's server-order selection policy to one settings response.
    /// Built-in entries are surfaced separately so the caller can persist them;
    /// custom bridge lines are returned in the selection for immediate use.
    /// </summary>
    public static MoatSelection? SelectConfiguration(MoatSettingsResponse? response)
    {
        if (response is null) return null;
        var custom = new List<string>();
        MoatTransportKind? selected = null;
        var builtIn = new Dictionary<MoatTransportKind, IReadOnlyList<string>>();

        foreach (var setting in response.Settings ?? [])
        {
            var bridge = setting.Bridge;
            if (bridge is null || bridge.Bridges is null || bridge.Bridges.Count == 0) continue;
            var kind = ParseTransport(bridge.Type);
            if (kind is null) continue;

            if (kind is MoatTransportKind.Obfs4 or MoatTransportKind.WebTunnel)
            {
                if (string.Equals(bridge.Source, "builtin", StringComparison.OrdinalIgnoreCase))
                    builtIn[kind.Value] = bridge.Bridges;
                else
                    custom.AddRange(bridge.Bridges);
            }
            else if (kind == MoatTransportKind.Snowflake)
            {
                builtIn[kind.Value] = bridge.Bridges;
            }
            else if (kind == MoatTransportKind.Dnstt)
            {
                if (string.Equals(bridge.Source, "builtin", StringComparison.OrdinalIgnoreCase))
                    builtIn[kind.Value] = bridge.Bridges;
                else
                    custom.AddRange(bridge.Bridges);
            }
            else if (kind == MoatTransportKind.Meek)
            {
                if (string.Equals(bridge.Source, "builtin", StringComparison.OrdinalIgnoreCase))
                    builtIn[kind.Value] = bridge.Bridges;
                else
                    custom.AddRange(bridge.Bridges);
            }

            selected ??= string.Equals(bridge.Source, "builtin", StringComparison.OrdinalIgnoreCase) ? kind : MoatTransportKind.Custom;
        }

        if (selected is null) return null;
        return new MoatSelection(selected.Value, custom, builtIn, response.Country);
    }

    private async Task<MoatSettingsResponse> PostAsync(string endpoint, MoatSettingsRequest request, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/vnd.api+json");
        using var response = await _httpClient.PostAsync(new Uri(BaseUri, endpoint), content, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        try
        {
            return JsonSerializer.Deserialize<MoatSettingsResponse>(body, JsonOptions)
                ?? throw new MoatApiException("Moat returned an empty settings document.");
        }
        catch (JsonException ex)
        {
            throw new MoatApiException("Moat returned malformed settings JSON.", null, ex);
        }
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadAsStringAsync(cancellationToken);

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode) return;
        MoatError? error = null;
        try
        {
            var document = JsonSerializer.Deserialize<MoatErrorDocument>(body, JsonOptions);
            error = document?.Errors?.FirstOrDefault();
        }
        catch (JsonException) { }
        throw new MoatApiException($"Moat request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).", error);
    }

    private static MoatTransportKind? ParseTransport(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "obfs4" => MoatTransportKind.Obfs4,
        "snowflake" => MoatTransportKind.Snowflake,
        "webtunnel" => MoatTransportKind.WebTunnel,
        "dnstt" => MoatTransportKind.Dnstt,
        "meek" or "meek_lite" => MoatTransportKind.Meek,
        _ => null
    };

    private static string ToWireName(MoatTransportKind kind) => kind switch
    {
        MoatTransportKind.Obfs4 => "obfs4",
        MoatTransportKind.Snowflake => "snowflake",
        MoatTransportKind.WebTunnel => "webtunnel",
        MoatTransportKind.Dnstt => "dnstt",
        MoatTransportKind.Meek => "meek_lite",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported Moat transport.")
    };

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}

public enum MoatTransportKind
{
    Obfs4,
    Snowflake,
    WebTunnel,
    Dnstt,
    Meek,
    Custom
}

public static class MoatTransportKindDefaults
{
    public static IReadOnlyList<MoatTransportKind> All { get; } =
        [MoatTransportKind.Obfs4, MoatTransportKind.Snowflake, MoatTransportKind.WebTunnel, MoatTransportKind.Dnstt];
}

public sealed class MoatSettingsRequest
{
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("transports")]
    public IReadOnlyList<string> Transports { get; set; } = [];
}

public sealed class MoatSettingsResponse
{
    [JsonPropertyName("settings")]
    public List<MoatSetting>? Settings { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("errors")]
    public List<MoatError>? Errors { get; set; }
}

public sealed class MoatSetting
{
    [JsonPropertyName("bridges")]
    public MoatBridge? Bridge { get; set; }
}

public sealed class MoatBridge
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("bridge_strings")]
    public List<string>? Bridges { get; set; }
}

public sealed class MoatBuiltinBridges
{
    public List<string>? Meek { get; set; }
    public List<string>? Obfs4 { get; set; }
    public List<string>? Snowflake { get; set; }
    public List<string>? Webtunnel { get; set; }
    public List<string>? Dnstt { get; set; }
}

public sealed record MoatSelection(
    MoatTransportKind Transport,
    IReadOnlyList<string> CustomBridgeLines,
    IReadOnlyDictionary<MoatTransportKind, IReadOnlyList<string>> BuiltInBridgeUpdates,
    string? Country)
{
    public MoatBridgeApplication ToBridgeApplication()
    {
        var mode = Transport switch
        {
            MoatTransportKind.Obfs4 => "obfs4",
            MoatTransportKind.Snowflake => "snowflake",
            MoatTransportKind.WebTunnel => "webtunnel",
            MoatTransportKind.Dnstt => "dnstt",
            MoatTransportKind.Meek => "meek",
            _ => "custom"
        };
        IReadOnlyList<string> lines = BuiltInBridgeUpdates.TryGetValue(Transport, out var updated) && updated.Count > 0
            ? updated
            : CustomBridgeLines;
        return new MoatBridgeApplication(mode, lines);
    }
}

public sealed record MoatBridgeApplication(string BridgeMode, IReadOnlyList<string> BridgeLines);

public sealed class MoatError
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Version { get; set; }
    public int? Code { get; set; }
    public string? Status { get; set; }
    public string? Detail { get; set; }

    public override string ToString() => Detail ?? $"{Code} {Status}".Trim();
}

internal sealed class MoatErrorDocument
{
    public List<MoatError>? Errors { get; set; }
}

public sealed class MoatApiException : Exception
{
    public MoatError? ApiError { get; }

    public MoatApiException(string message, MoatError? apiError = null, Exception? innerException = null)
        : base(message + (apiError is null ? string.Empty : " " + apiError), innerException) => ApiError = apiError;
}

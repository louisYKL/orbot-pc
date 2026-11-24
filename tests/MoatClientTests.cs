using OrbotPc.Core;
using System.Net;
using System.Text;

namespace OrbotPc.Tests;

internal static class MoatClientTests
{
    public static async Task RunAsync()
    {
        var handler = new FixtureHandler((request, body) =>
        {
            Check(request.Method == HttpMethod.Post, "Moat settings should use POST.");
            Check(request.RequestUri?.AbsolutePath == "/moat/circumvention/settings", "Moat settings endpoint path is incorrect.");
            Check(request.Content?.Headers.ContentType?.MediaType == "application/vnd.api+json", "Moat settings content type is incorrect.");
            Check(body.Contains("\"country\":\"ru\"", StringComparison.Ordinal), "Moat country must be normalized to lowercase.");
            Check(body.Contains("\"transports\":[\"obfs4\",\"snowflake\",\"webtunnel\",\"dnstt\"]", StringComparison.Ordinal), "Moat request must only advertise transports bundled in this Windows runtime.");
            return Json(HttpStatusCode.OK, """
                {"country":"RU","settings":[
                  {"bridges":{"type":"obfs4","source":"builtin","bridge_strings":["obfs4 192.0.2.4:443 FINGERPRINT cert=x"]}},
                  {"bridges":{"type":"webtunnel","source":"bridgedb","bridge_strings":["webtunnel 192.0.2.5:443 FINGERPRINT url=https://example.test/"]}},
                  {"bridges":{"type":"snowflake","source":"builtin","bridge_strings":["snowflake 192.0.2.3:80 FINGERPRINT"]}},
                  {"bridges":{"type":"dnstt","source":"builtin","bridge_strings":["dnstt 192.0.2.4:1 FINGERPRINT doh=https://dns.google/dns-query pubkey=abc domain=example.test"]}}
                ],"errors":[]}
                """);
        });
        using var http = new HttpClient(handler);
        using var moat = new MoatClient(new Uri("https://bridges.torproject.org/moat/circumvention/"), http);
        var response = await moat.FetchSettingsAsync("RU");
        var selected = MoatClient.SelectConfiguration(response);
        Check(selected?.Transport == MoatTransportKind.Obfs4, "Moat selection should respect server ordering.");
        Check(selected?.CustomBridgeLines.Count == 1 && selected.CustomBridgeLines[0].StartsWith("webtunnel ", StringComparison.Ordinal), "Custom bridge lines were not preserved.");
        Check(selected?.BuiltInBridgeUpdates.ContainsKey(MoatTransportKind.Obfs4) == true, "Built-in obfs4 bridge update was not extracted.");
        Check(selected?.BuiltInBridgeUpdates.ContainsKey(MoatTransportKind.Snowflake) == true, "Built-in Snowflake bridge update was not extracted.");
        Check(selected?.BuiltInBridgeUpdates.ContainsKey(MoatTransportKind.Dnstt) == true, "Built-in DNSTT bridge update was not extracted.");
        var applied = selected!.ToBridgeApplication();
        Check(applied.BridgeMode == "obfs4" && applied.BridgeLines.Count == 1 && applied.BridgeLines[0].StartsWith("obfs4 ", StringComparison.Ordinal),
            "Moat selection was not converted to an applicable Tor bridge mode and line set.");

        var dnsttApplication = new MoatSelection(MoatTransportKind.Dnstt, [], new Dictionary<MoatTransportKind, IReadOnlyList<string>>
        {
            [MoatTransportKind.Dnstt] = ["dnstt 192.0.2.4:1 FINGERPRINT doh=https://dns.google/dns-query pubkey=abc domain=example.test"]
        }, "RU").ToBridgeApplication();
        Check(dnsttApplication.BridgeMode == "dnstt" && dnsttApplication.BridgeLines[0].StartsWith("dnstt ", StringComparison.Ordinal),
            "Built-in DNSTT selection was not mapped to the native Windows transport.");

        var builtinHandler = new FixtureHandler((request, _) =>
        {
            Check(request.Method == HttpMethod.Get, "Moat builtin should use GET.");
            Check(request.RequestUri?.AbsolutePath == "/moat/circumvention/builtin", "Moat builtin endpoint path is incorrect.");
            return Json(HttpStatusCode.OK, """
                {"meek":["meek_lite 192.0.2.2:80 url=https://example.test/"],"obfs4":[],"snowflake":["snowflake 192.0.2.3:80 FINGERPRINT"],"webtunnel":[],"dnstt":[]}
                """);
        });
        using var builtinHttp = new HttpClient(builtinHandler);
        using var builtinMoat = new MoatClient(new Uri("https://bridges.torproject.org/moat/circumvention/"), builtinHttp);
        var builtin = await builtinMoat.FetchBuiltinAsync();
        Check(builtin.Meek?.Count == 1 && builtin.Snowflake?.Count == 1, "Moat builtin bridge JSON was not parsed.");

        var errorHandler = new FixtureHandler((_, _) => Json(HttpStatusCode.NotFound, """
            {"errors":[{"id":"needs-transport","code":404,"status":"Not Found","detail":"Transport required"}]}
            """));
        using var errorHttp = new HttpClient(errorHandler);
        using var errorMoat = new MoatClient(new Uri("https://bridges.torproject.org/moat/circumvention/"), errorHttp);
        try
        {
            await errorMoat.FetchSettingsAsync();
            throw new InvalidOperationException("Moat API errors must be surfaced.");
        }
        catch (MoatApiException ex)
        {
            Check(ex.ApiError?.Code == 404 && ex.Message.Contains("Transport required", StringComparison.Ordinal), "Moat API error details were lost.");
        }

        try
        {
            using var invalid = new MoatClient(new Uri("http://bridges.torproject.org/moat/circumvention/"), new HttpClient(new FixtureHandler((_, _) => Json(HttpStatusCode.OK, "{}"))));
            throw new InvalidOperationException("Moat client should reject non-HTTPS endpoints.");
        }
        catch (ArgumentException) { }

        Console.WriteLine("PASS Moat client request, server-ordered selection, builtin data, API errors, and HTTPS-only endpoint");
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) => new(code)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/vnd.api+json")
    };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, body);
        }
    }
}

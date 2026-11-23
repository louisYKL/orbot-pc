using OrbotPc.Core;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;

if (args.Length != 1) throw new ArgumentException("Pass the Orbot PC project root as the only argument.");
var root = Path.GetFullPath(args[0]);
Check(new OrbotSettings().UseWindowsProxy, "Windows proxy should be enabled automatically after Tor bootstrap by default.");
Console.WriteLine("PASS automatic Windows proxy default");
var runtime = new TorRuntime(root);
runtime.LogMessage += Console.WriteLine;

await runtime.ValidateConfigurationAsync(new OrbotSettings());
Console.WriteLine("PASS Tor direct configuration");
await runtime.ValidateConfigurationAsync(new OrbotSettings { BridgeMode = "obfs4" });
Console.WriteLine("PASS Tor obfs4 bridge configuration");
await runtime.ValidateConfigurationAsync(new OrbotSettings { BridgeMode = "snowflake" });
Console.WriteLine("PASS Tor Snowflake bridge configuration");
await runtime.ValidateConfigurationAsync(new OrbotSettings
{
    ExitCountry = "de",
    OnionServices = [new OnionServiceEntry { Name = "integration-test", VirtualPort = 8080, TargetPort = 18080 }]
});
Console.WriteLine("PASS Tor exit preference and v3 onion service configuration");

var runtimePort = ReservePort();
var runtimeSettings = new OrbotSettings { SocksPort = ReservePort(), HttpPort = ReservePort(), ControlPort = runtimePort, DisableNetwork = true };
try
{
    await runtime.StartAsync(runtimeSettings);
    var bootstrap = await runtime.GetBootstrapAsync();
    Check(bootstrap.Contains("status/bootstrap-phase=", StringComparison.Ordinal), "Tor control port did not return bootstrap status.");
    await runtime.NewIdentityAsync();
    await runtime.StopAsync();
    Check(!runtime.IsRunning, "Tor process did not stop cleanly.");
}
finally { await runtime.DisposeAsync(); }
Console.WriteLine("PASS Tor process start, cookie-authenticated control, and clean shutdown");

await VerifyHttpProxyAsync();
Console.WriteLine("PASS local HTTP absolute-form request through SOCKS5");
await VerifyConnectTunnelAsync();
Console.WriteLine("PASS local HTTP CONNECT tunnel through SOCKS5");

static async Task VerifyHttpProxyAsync()
{
    var destination = new TcpListener(IPAddress.Loopback, 0);
    destination.Start();
    var destinationPort = ((IPEndPoint)destination.LocalEndpoint).Port;
    var socksPort = ReservePort();
    var httpPort = ReservePort();
    var originTask = OriginHttpOnceAsync(destination);
    await using var socks = new FakeSocksServer(socksPort, destinationPort);
    socks.Start();
    await using var proxy = new HttpProxyServer(httpPort, socksPort);
    proxy.Start();

    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, httpPort);
    var stream = client.GetStream();
    await stream.WriteAsync(Encoding.ASCII.GetBytes("GET http://example.test/resource?q=1 HTTP/1.1\r\nHost: example.test\r\nConnection: close\r\n\r\n"));
    var response = await ReadToEndAsync(stream, TimeSpan.FromSeconds(5));
    Check(response.Contains("HTTP/1.1 200 OK", StringComparison.Ordinal), "HTTP proxy did not return an origin response.");
    Check(response.EndsWith("ok", StringComparison.Ordinal), "HTTP proxy response body was not preserved.");
    Check(socks.LastDestination == "example.test:80", "HTTP proxy did not use the expected SOCKS destination.");
    await originTask;
    await proxy.StopAsync();
    destination.Stop();
}

static async Task VerifyConnectTunnelAsync()
{
    var destination = new TcpListener(IPAddress.Loopback, 0);
    destination.Start();
    var destinationPort = ((IPEndPoint)destination.LocalEndpoint).Port;
    var socksPort = ReservePort();
    var httpPort = ReservePort();
    await using var socks = new FakeSocksServer(socksPort, destinationPort);
    socks.Start();
    var echoTask = EchoOnceAsync(destination);
    await using var proxy = new HttpProxyServer(httpPort, socksPort);
    proxy.Start();

    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, httpPort);
    var stream = client.GetStream();
    await stream.WriteAsync(Encoding.ASCII.GetBytes("CONNECT example.test:443 HTTP/1.1\r\nHost: example.test:443\r\n\r\n"));
    var responseHeader = await ReadHeaderAsync(stream, TimeSpan.FromSeconds(5));
    Check(responseHeader.StartsWith("HTTP/1.1 200", StringComparison.Ordinal), "HTTP CONNECT did not establish a tunnel.");
    await stream.WriteAsync(Encoding.ASCII.GetBytes("hello"));
    var echoed = new byte[5];
    await ReadExactlyAsync(stream, echoed, TimeSpan.FromSeconds(5));
    Check(Encoding.ASCII.GetString(echoed) == "hello", "HTTP CONNECT tunnel did not relay bytes bidirectionally.");
    Check(socks.LastDestination == "example.test:443", "CONNECT proxy did not use the expected SOCKS destination.");
    client.Close();
    await echoTask;
    await proxy.StopAsync();
    destination.Stop();
}

static async Task EchoOnceAsync(TcpListener listener)
{
    using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
    var stream = client.GetStream();
    var data = new byte[5];
    await ReadExactlyAsync(stream, data, TimeSpan.FromSeconds(5));
    await stream.WriteAsync(data);
}

static async Task OriginHttpOnceAsync(TcpListener listener)
{
    using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
    var stream = client.GetStream();
    var request = await ReadHeaderAsync(stream, TimeSpan.FromSeconds(5));
    Check(request.StartsWith("GET /resource?q=1 HTTP/1.1", StringComparison.Ordinal), "Proxy did not rewrite the absolute URL to origin-form.");
    var body = "ok";
    var response = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
    await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
}

static int ReservePort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static async Task<string> ReadHeaderAsync(Stream stream, TimeSpan timeout)
{
    using var cts = new CancellationTokenSource(timeout);
    using var memory = new MemoryStream();
    var one = new byte[1];
    while (memory.Length < 16 * 1024)
    {
        await ReadExactlyAsync(stream, one, timeout);
        memory.WriteByte(one[0]);
        var data = memory.GetBuffer();
        var length = (int)memory.Length;
        if (length >= 4 && data[length - 4] == 13 && data[length - 3] == 10 && data[length - 2] == 13 && data[length - 1] == 10)
            return Encoding.Latin1.GetString(memory.ToArray());
    }
    throw new InvalidDataException("HTTP response header too large.");
}

static async Task<string> ReadToEndAsync(Stream stream, TimeSpan timeout)
{
    using var cts = new CancellationTokenSource(timeout);
    using var memory = new MemoryStream();
    await stream.CopyToAsync(memory, cts.Token);
    return Encoding.Latin1.GetString(memory.ToArray());
}

static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, TimeSpan timeout)
{
    using var cts = new CancellationTokenSource(timeout);
    var offset = 0;
    while (offset < buffer.Length)
    {
        var count = await stream.ReadAsync(buffer[offset..], cts.Token);
        if (count == 0) throw new EndOfStreamException("Unexpected end of stream.");
        offset += count;
    }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static class NetworkTestIO
{
    public static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[offset..], cts.Token);
            if (count == 0) throw new EndOfStreamException("Unexpected end of stream.");
            offset += count;
        }
    }
}

sealed class FakeSocksServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly int _destinationPort;
    private Task? _serveTask;
    public string? LastDestination { get; private set; }

    public FakeSocksServer(int port, int destinationPort)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _destinationPort = destinationPort;
    }

    public void Start()
    {
        _listener.Start();
        _serveTask = ServeOneAsync();
    }

    private async Task ServeOneAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await using var stream = client.GetStream();
        var greeting = new byte[3];
        await NetworkTestIO.ReadExactlyAsync(stream, greeting, TimeSpan.FromSeconds(5));
        await stream.WriteAsync(new byte[] { 5, 0 });
        var request = new byte[5];
        await NetworkTestIO.ReadExactlyAsync(stream, request.AsMemory(0, 4), TimeSpan.FromSeconds(5));
        if (request[3] != 3) throw new InvalidOperationException("Expected SOCKS domain-name destination.");
        await NetworkTestIO.ReadExactlyAsync(stream, request.AsMemory(4, 1), TimeSpan.FromSeconds(5));
        var domain = new byte[request[4]];
        await NetworkTestIO.ReadExactlyAsync(stream, domain, TimeSpan.FromSeconds(5));
        var port = new byte[2];
        await NetworkTestIO.ReadExactlyAsync(stream, port, TimeSpan.FromSeconds(5));
        LastDestination = Encoding.ASCII.GetString(domain) + ":" + ((port[0] << 8) | port[1]);

        using var target = new TcpClient();
        await target.ConnectAsync(IPAddress.Loopback, _destinationPort);
        await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 0 });
        var targetStream = target.GetStream();
        using var relayCancellation = new CancellationTokenSource();
        var upstream = stream.CopyToAsync(targetStream, 4096, relayCancellation.Token);
        var downstream = targetStream.CopyToAsync(stream, 4096, relayCancellation.Token);
        await Task.WhenAny(upstream, downstream);
        relayCancellation.Cancel();
        try { await Task.WhenAll(upstream, downstream); } catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        if (_serveTask is not null)
        {
            try { await _serveTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
    }
}

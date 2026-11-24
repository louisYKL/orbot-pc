using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace OrbotPc.Core;

/// <summary>
/// Runs bundled managed pluggable transports for Moat.
///
/// Orbot's Android implementation starts IPtProxy.MeekLite and passes the
/// Moat configuration through SOCKS5 username/password authentication. The
/// Windows managed-transport protocol provides the same boundary. This class
/// supervises the transport process and creates an HTTP client whose TCP
/// connections use the returned local SOCKS5 port.
/// </summary>
public sealed class MoatTunnelRuntime : IAsyncDisposable
{
    public const string TransportName = "meek_lite";
    public const string DnsttTransportName = "dnstt";
    public const string DefaultTorProjectConfig = "url=https://1723079976.rsc.cdn77.org;front=www.phpmyadmin.net";
    public const string DefaultStateDirectoryName = "moat-meek";

    private readonly string _lyrebirdPath;
    private readonly string _dnsttPath;
    private readonly string _statePath;
    private Process? _process;
    private StreamWriter? _stdin;
    private int _socksPort;

    public bool IsRunning => _process is { HasExited: false };
    public int SocksPort => _socksPort;

    public MoatTunnelRuntime(string basePath, string? lyrebirdPath = null)
    {
        var root = Path.GetFullPath(basePath);
        _lyrebirdPath = lyrebirdPath ?? Path.Combine(root, "runtime", "tor", "tor", "pluggable_transports", "lyrebird.exe");
        _dnsttPath = Path.Combine(root, "runtime", "dnstt", "dnstt-client.exe");
        _statePath = Path.Combine(root, ".data", DefaultStateDirectoryName);
    }

    public async Task StartAsync(string moatConfig = DefaultTorProjectConfig, CancellationToken cancellationToken = default)
        => await StartManagedTransportAsync(TransportName, _lyrebirdPath, moatConfig, cancellationToken);

    public async Task StartDnsttAsync(string dnsttConfig, CancellationToken cancellationToken = default)
        => await StartManagedTransportAsync(DnsttTransportName, _dnsttPath, dnsttConfig, cancellationToken);

    private async Task StartManagedTransportAsync(string transportName, string executablePath, string transportConfig, CancellationToken cancellationToken)
    {
        if (IsRunning) return;
        if (string.IsNullOrWhiteSpace(transportConfig) || transportConfig.Any(c => c is '\r' or '\n' or '\0'))
            throw new ArgumentException("Managed transport configuration must be a non-empty single line.", nameof(transportConfig));
        if (!File.Exists(executablePath)) throw new FileNotFoundException("The bundled managed transport was not found.", executablePath);

        var transportStatePath = Path.Combine(_statePath, transportName);
        Directory.CreateDirectory(transportStatePath);
        var start = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.Environment["TOR_PT_MANAGED_TRANSPORT_VER"] = "1";
        start.Environment["TOR_PT_CLIENT_TRANSPORTS"] = transportName;
        start.Environment["TOR_PT_STATE_LOCATION"] = transportStatePath;
        start.Environment["TOR_PT_EXIT_ON_STDIN_CLOSE"] = "1";
        if (transportName == TransportName)
        {
            start.ArgumentList.Add("-logLevel");
            start.ArgumentList.Add("ERROR");
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start the managed {transportName} transport.");
        _process = process;
        _stdin = process.StandardInput;
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                    throw new InvalidOperationException($"Managed {transportName} exited during startup (code {process.ExitCode}).");
                var remaining = deadline - DateTime.UtcNow;
                var lineTask = process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
                var completed = await Task.WhenAny(lineTask, Task.Delay(remaining, cancellationToken));
                if (completed != lineTask) break;
                var line = await lineTask;
                if (line is null) break;
                if (TryParseMethodLine(line, transportName, out var port))
                {
                    _socksPort = port;
                    continue;
                }
                if (line.StartsWith("CMETHOD-ERROR", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Managed {transportName} rejected startup: " + line);
                if (line.Equals("CMETHODS DONE", StringComparison.Ordinal) && _socksPort > 0)
                    return;
            }
            throw new TimeoutException($"The managed {transportName} transport did not publish a SOCKS5 endpoint.");
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    /// <summary>Creates an HTTPS-capable HttpClient that authenticates to the PT with the Moat config.</summary>
    public HttpClient CreateHttpClient(string moatConfig = DefaultTorProjectConfig, TimeSpan? timeout = null)
        => CreateHttpClientForTransport(TransportName, moatConfig, timeout);

    public HttpClient CreateDnsttHttpClient(string dnsttConfig, TimeSpan? timeout = null)
        => CreateHttpClientForTransport(DnsttTransportName, dnsttConfig, timeout);

    private HttpClient CreateHttpClientForTransport(string transportName, string transportConfig, TimeSpan? timeout)
    {
        if (!IsRunning || _socksPort is < 1 or > 65535)
            throw new InvalidOperationException($"The managed {transportName} tunnel is not running.");
        if (string.IsNullOrWhiteSpace(transportConfig) || transportConfig.Any(c => c is '\r' or '\n' or '\0'))
            throw new ArgumentException("Managed transport configuration must be a non-empty single line.", nameof(transportConfig));

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = (context, cancellationToken) =>
                ConnectThroughSocks5Async(context.DnsEndPoint, _socksPort, transportName, transportConfig, cancellationToken),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30)
        };
        return client;
    }

    public async Task<TResult> ExecuteWithMoatClientAsync<TResult>(
        Uri baseUri,
        Func<MoatClient, CancellationToken, Task<TResult>> operation,
        string moatConfig = DefaultTorProjectConfig,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await StartAsync(moatConfig, cancellationToken);
        try
        {
            using var http = CreateHttpClient(moatConfig);
            using var moat = new MoatClient(baseUri, httpClient: http);
            return await operation(moat, cancellationToken);
        }
        finally
        {
            await StopAsync();
        }
    }

    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        _socksPort = 0;
        var input = _stdin;
        _stdin = null;
        if (input is not null)
        {
            try { input.Close(); } catch (IOException) { }
            input.Dispose();
        }
        if (process is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(); } catch { }
        }
        process.Dispose();
    }

    public static bool TryParseMethodLine(string line, out int port)
    {
        return TryParseMethodLine(line, TransportName, out port);
    }

    public static bool TryParseMethodLine(string line, string transportName, out int port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(line)) return false;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || !parts[0].Equals("CMETHOD", StringComparison.Ordinal) ||
            !parts[1].Equals(transportName, StringComparison.OrdinalIgnoreCase) ||
            !parts[2].Equals("socks5", StringComparison.OrdinalIgnoreCase)) return false;
        var endpoint = parts[3];
        var separator = endpoint.LastIndexOf(':');
        return separator > 0 && int.TryParse(endpoint[(separator + 1)..], out port) && port is >= 1 and <= 65535;
    }

    private static async ValueTask<Stream> ConnectThroughSocks5Async(
        DnsEndPoint destination,
        int proxyPort,
        string transportName,
        string transportConfig,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient(destination.AddressFamily);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, proxyPort, cancellationToken);
            var stream = client.GetStream();
            await WriteAsync(stream, [0x05, 0x02, 0x00, 0x02], cancellationToken);
            var methodReply = await ReadExactAsync(stream, 2, cancellationToken);
            if (methodReply[0] != 0x05) throw new InvalidDataException($"Managed {transportName} returned an invalid SOCKS5 greeting.");
            if (methodReply[1] == 0x02)
            {
                var username = Encoding.UTF8.GetBytes(transportConfig);
                if (username.Length is 0 or > 255) throw new ArgumentException("Managed transport configuration is too long.", nameof(transportConfig));
                await WriteAsync(stream, [0x01, (byte)username.Length], cancellationToken);
                await stream.WriteAsync(username, cancellationToken);
                await WriteAsync(stream, [0x01, 0x00], cancellationToken);
                var authReply = await ReadExactAsync(stream, 2, cancellationToken);
                if (authReply[1] != 0x00) throw new InvalidDataException($"Managed {transportName} rejected SOCKS5 authentication.");
            }
            else if (methodReply[1] != 0x00 || transportConfig.Length > 0)
            {
                throw new InvalidDataException($"Managed {transportName} did not offer SOCKS5 username/password authentication required to pass its configuration.");
            }

            var host = Encoding.UTF8.GetBytes(destination.Host);
            if (host.Length is 0 or > 255) throw new ArgumentException("Destination host is invalid.", nameof(destination));
            var request = new byte[7 + host.Length];
            request[0] = 0x05; request[1] = 0x01; request[2] = 0x00; request[3] = 0x03; request[4] = (byte)host.Length;
            Buffer.BlockCopy(host, 0, request, 5, host.Length);
            request[^2] = (byte)(destination.Port >> 8); request[^1] = (byte)destination.Port;
            await WriteAsync(stream, request, cancellationToken);
            var response = await ReadExactAsync(stream, 4, cancellationToken);
            if (response[0] != 0x05 || response[1] != 0x00)
                throw new SocketException((int)SocketError.ConnectionRefused);
            var addressLength = response[3] switch { 0x01 => 4, 0x04 => 16, 0x03 => (await ReadExactAsync(stream, 1, cancellationToken))[0], _ => throw new InvalidDataException("Managed meek returned an invalid SOCKS5 address type.") };
            await ReadExactAsync(stream, addressLength + 2, cancellationToken);
            return new DetachableNetworkStream(client, stream);
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task WriteAsync(NetworkStream stream, byte[] data, CancellationToken cancellationToken) => await stream.WriteAsync(data, cancellationToken);

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var data = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(data.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException("Managed meek closed the SOCKS5 connection.");
            offset += read;
        }
        return data;
    }

    private sealed class DetachableNetworkStream(TcpClient client, NetworkStream stream) : Stream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { stream.Dispose(); } finally { client.Dispose(); } }
            base.Dispose(disposing);
        }
        public override bool CanRead => stream.CanRead; public override bool CanSeek => false; public override bool CanWrite => stream.CanWrite;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => stream.Flush(); public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => stream.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => stream.WriteAsync(buffer, cancellationToken);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

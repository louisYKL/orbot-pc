using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OrbotPc.Core;

public sealed class HttpProxyServer : IAsyncDisposable
{
    private readonly int _listenPort;
    private readonly int _socksPort;
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _acceptLoop;
    private int _activeConnections;

    public event Action<string>? LogMessage;
    public int ActiveConnections => Volatile.Read(ref _activeConnections);
    public bool IsRunning => _listener is not null;

    public HttpProxyServer(int listenPort, int socksPort)
    {
        _listenPort = listenPort;
        _socksPort = socksPort;
    }

    public void Start()
    {
        if (IsRunning) return;
        _stop = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _listenPort);
        _listener.Start(64);
        _acceptLoop = AcceptLoopAsync(_stop.Token);
        LogMessage?.Invoke($"Local HTTP proxy listening at 127.0.0.1:{_listenPort}.");
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        var stop = _stop;
        _listener = null;
        _stop = null;
        if (stop is null) return;
        stop.Cancel();
        listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        }
        stop.Dispose();
        _acceptLoop = null;
        LogMessage?.Invoke("Local HTTP proxy stopped.");
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var listener = _listener!;
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(cancellationToken); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activeConnections);
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(90));
                await using var clientStream = client.GetStream();
                var headerBytes = await ReadHeaderAsync(clientStream, connectTimeout.Token);
                var header = Encoding.Latin1.GetString(headerBytes);
                var end = header.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (end < 0) throw new InvalidDataException("Incomplete HTTP proxy request.");
                var headerText = header[..end];
                var lines = headerText.Split("\r\n");
                var request = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (request.Length != 3) throw new InvalidDataException("Invalid HTTP proxy request line.");

                var isConnect = request[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase);
                string host;
                int port;
                string outboundHeader;
                if (isConnect)
                {
                    (host, port) = ParseAuthority(request[1], 443);
                    outboundHeader = "";
                }
                else
                {
                    if (!Uri.TryCreate(request[1], UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !string.IsNullOrEmpty(uri.UserInfo))
                    {
                        await WriteErrorAsync(clientStream, 400, "Only HTTP absolute-form and CONNECT requests are supported.", connectTimeout.Token);
                        return;
                    }
                    host = uri.IdnHost;
                    port = uri.IsDefaultPort ? 80 : uri.Port;
                    var path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
                    var rewritten = new List<string> { $"{request[0]} {path} {request[2]}" };
                    rewritten.AddRange(lines.Skip(1).Where(line =>
                    {
                        var colon = line.IndexOf(':');
                        if (colon < 0) return false;
                        var name = line[..colon].Trim();
                        return !name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                            && !name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                            && !name.Equals("Connection", StringComparison.OrdinalIgnoreCase);
                    }));
                    rewritten.Add("Connection: close");
                    outboundHeader = string.Join("\r\n", rewritten) + "\r\n\r\n";
                }

                var remoteConnection = await OpenSocksConnectionAsync(host, port, connectTimeout.Token);
                connectTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                using var remoteClient = remoteConnection.Client;
                await using var remote = remoteConnection.Stream;
                if (isConnect)
                    await clientStream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\nProxy-Agent: Orbot-PC\r\n\r\n"), connectTimeout.Token);
                else
                    await remote.WriteAsync(Encoding.Latin1.GetBytes(outboundHeader), connectTimeout.Token);

                using var relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var upload = clientStream.CopyToAsync(remote, 64 * 1024, relayCancellation.Token);
                var download = remote.CopyToAsync(clientStream, 64 * 1024, relayCancellation.Token);
                await Task.WhenAny(upload, download);
                relayCancellation.Cancel();
                try { await Task.WhenAll(upload, download); } catch (OperationCanceledException) { }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException or ArgumentException)
            {
                LogMessage?.Invoke("HTTP proxy request failed: " + ex.Message);
                try { await WriteErrorAsync(client.GetStream(), 502, "Tor proxy connection failed.", CancellationToken.None); } catch { }
            }
            finally { Interlocked.Decrement(ref _activeConnections); }
        }
    }

    private async Task<(TcpClient Client, NetworkStream Stream)> OpenSocksConnectionAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535) throw new InvalidDataException("Target port is invalid.");
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, _socksPort, cancellationToken);
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 5, 1, 0 }, cancellationToken);
            var greeting = new byte[2];
            await ReadExactlyAsync(stream, greeting, cancellationToken);
            if (greeting[0] != 5 || greeting[1] != 0) throw new IOException("Tor SOCKS5 proxy did not accept unauthenticated localhost clients.");

            var domain = Encoding.ASCII.GetBytes(new IdnMapping().GetAscii(host));
            if (domain.Length is 0 or > 255) throw new InvalidDataException("Target hostname is invalid.");
            var connect = new byte[7 + domain.Length];
            connect[0] = 5;
            connect[1] = 1;
            connect[2] = 0;
            connect[3] = 3;
            connect[4] = (byte)domain.Length;
            domain.CopyTo(connect, 5);
            connect[^2] = (byte)(port >> 8);
            connect[^1] = (byte)port;
            await stream.WriteAsync(connect, cancellationToken);

            var response = new byte[4];
            await ReadExactlyAsync(stream, response, cancellationToken);
            if (response[0] != 5 || response[1] != 0) throw new IOException($"Tor SOCKS5 connection failed (reply {response[1]}).");
            var remaining = response[3] switch
            {
                1 => 6,
                4 => 18,
                3 => await ReadDomainTailLengthAsync(stream, cancellationToken),
                _ => throw new IOException("Tor SOCKS5 returned an invalid address type.")
            };
            var tail = new byte[remaining];
            await ReadExactlyAsync(stream, tail, cancellationToken);
            return (client, stream);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<int> ReadDomainTailLengthAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var size = new byte[1];
        await ReadExactlyAsync(stream, size, cancellationToken);
        return size[0] + 2;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (count == 0) throw new EndOfStreamException("Proxy peer closed the connection.");
            offset += count;
        }
    }

    private static async Task<byte[]> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        while (buffer.Length < 32 * 1024)
        {
            var count = await stream.ReadAsync(one, cancellationToken);
            if (count == 0) break;
            buffer.WriteByte(one[0]);
            var length = (int)buffer.Length;
            if (length >= 4)
            {
                var bytes = buffer.GetBuffer();
                if (bytes[length - 4] == 13 && bytes[length - 3] == 10 && bytes[length - 2] == 13 && bytes[length - 1] == 10)
                    return buffer.ToArray();
            }
        }
        throw new InvalidDataException("HTTP proxy header exceeded 32 KiB or ended early.");
    }

    private static (string Host, int Port) ParseAuthority(string authority, int defaultPort)
    {
        var parsed = Uri.TryCreate("http://" + authority, UriKind.Absolute, out var uri) ? uri : null;
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo))
            throw new InvalidDataException("Invalid proxy destination.");
        var port = parsed.IsDefaultPort ? defaultPort : parsed.Port;
        return (parsed.IdnHost, port);
    }

    private static async Task WriteErrorAsync(Stream stream, int status, string message, CancellationToken cancellationToken)
    {
        var text = $"HTTP/1.1 {status} Error\r\nConnection: close\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(message)}\r\n\r\n{message}";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

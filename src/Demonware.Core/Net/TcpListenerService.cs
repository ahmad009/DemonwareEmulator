using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Demonware.Core.Net;

public interface IConnectionHandler
{
    void OnConnected(TcpConnection connection);
    void OnData(TcpConnection connection, byte[] data);
    void OnDisconnected(TcpConnection connection);
}

public sealed class TcpConnection
{
    public required Socket Socket { get; init; }
    public required string Id { get; init; }
    public object? State { get; set; }

    public void Send(byte[] payload)
    {
        try
        {
            if (payload.Length == 0) return;
            Socket.Send(payload);
        }
        catch { /* drop */ }
    }
}

/// <summary>Minimal async TCP acceptor — one profile per listener.</summary>
public sealed class TcpListenerService : IAsyncDisposable
{
    private readonly int _port;
    private readonly IConnectionHandler _handler;
    private readonly string _name;
    private Socket? _listen;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, TcpConnection> _clients = new();

    public TcpListenerService(string name, int port, IConnectionHandler handler)
    {
        _name = name;
        _port = port;
        _handler = handler;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listen.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listen.Bind(new IPEndPoint(IPAddress.Any, _port));
        _listen.Listen(128);
        Log.Ok(_name, $"TCP listening on {_port}");

        _ = Task.Run(() => AcceptLoop(_cts.Token), _cts.Token);
        await Task.CompletedTask;
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listen is not null)
        {
            Socket client;
            try { client = await _listen.AcceptAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch { continue; }

            var id = ((IPEndPoint)client.RemoteEndPoint!).ToString();
            var conn = new TcpConnection { Socket = client, Id = id };
            _clients[id] = conn;
            try { _handler.OnConnected(conn); } catch (Exception ex) { Log.Error(_name, ex.Message); }
            _ = Task.Run(() => ReadLoop(conn, ct), ct);
        }
    }

    private async Task ReadLoop(TcpConnection conn, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var n = await conn.Socket.ReceiveAsync(buffer, SocketFlags.None, ct);
                if (n <= 0) break;
                var chunk = new byte[n];
                Buffer.BlockCopy(buffer, 0, chunk, 0, n);
                try { _handler.OnData(conn, chunk); }
                catch (Exception ex) { Log.Error(_name, ex.ToString()); }
            }
        }
        catch { /* disconnect */ }
        finally
        {
            _clients.TryRemove(conn.Id, out _);
            try { _handler.OnDisconnected(conn); } catch { }
            try { conn.Socket.Dispose(); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listen?.Dispose(); } catch { }
        foreach (var c in _clients.Values)
            try { c.Socket.Dispose(); } catch { }
        _clients.Clear();
        await Task.CompletedTask;
    }
}

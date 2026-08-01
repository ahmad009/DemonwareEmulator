using System.Net;
using System.Text;
using System.Text.Json;
using Demonware.Core;
using Demonware.Core.Crypto;
using Demonware.Core.Store;
using Demonware.Modern;

namespace Demonware.Service.Gateway;

public sealed class GatewayWorker(IConfiguration config, FileStore store) : BackgroundService
{
    private HttpListener? _listener;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var preferred = config.GetValue("Ports:Http", Ports.GatewayHttp);
        var ports = preferred == 80 ? new[] { 80, Ports.GatewayHttpFallback } : new[] { preferred, 80, Ports.GatewayHttpFallback };

        foreach (var port in ports)
        {
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add($"http://+:{port}/");
                l.Start();
                _listener = l;
                Log.Ok("HTTP", $"Auth3/Umbrella/Uno/Fileshare on :{port}");
                break;
            }
            catch
            {
                try
                {
                    var l = new HttpListener();
                    l.Prefixes.Add($"http://127.0.0.1:{port}/");
                    l.Prefixes.Add($"http://localhost:{port}/");
                    l.Start();
                    _listener = l;
                    Log.Warn("HTTP", $"bound loopback :{port}");
                    break;
                }
                catch { /* try next */ }
            }
        }

        if (_listener is null) { Log.Error("HTTP", "bind failed — run as Admin for port 80"); return; }

        while (!stoppingToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().WaitAsync(stoppingToken); }
            catch { break; }
            _ = Task.Run(() => Route(ctx), stoppingToken);
        }
    }

    private void Route(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var host = (ctx.Request.Headers["Host"] ?? "").ToLowerInvariant();
            if (host.Contains(':')) host = host[..host.IndexOf(':')];

            if (path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) || host.Contains("auth3") || host.Contains("-auth"))
            { Auth3(ctx); return; }

            if (host.Contains("fileshare") || path.StartsWith("/fileshare", StringComparison.OrdinalIgnoreCase))
            { Fileshare(ctx, path); return; }

            if (host.Contains("umbrella") || path.Contains("umbrella", StringComparison.OrdinalIgnoreCase))
            { Write(ctx, 200, "application/json", "{}"); return; }

            if (host.Contains("uno") || path.StartsWith("/v1.0", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            { Write(ctx, 200, "application/json", "{\"status\":\"ok\"}"); return; }

            Write(ctx, 200, "text/plain", "Demonware Gateway\n");
        }
        catch (Exception ex)
        {
            Log.Error("HTTP", ex.Message);
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private void Auth3(HttpListenerContext ctx)
    {
        using var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
        var body = sr.ReadToEnd();
        var path = ctx.Request.Url?.AbsolutePath ?? "";

        if (path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) && ctx.Request.HttpMethod == "POST" && string.IsNullOrEmpty(body))
        { Write(ctx, 200, "text/plain", "OK"); return; }

        var titleId = ParseUInt(body, "title_id");
        var ivSeed = ParseUInt(body, "iv_seed");
        var extra = Extract(body, "extra_data");
        var tokenB64 = Extract(extra ?? "", "token") ?? Extract(body, "token");

        var token = new byte[128];
        if (!string.IsNullOrEmpty(tokenB64))
        {
            try { token = Convert.FromBase64String(tokenB64); } catch { }
        }
        if (token.Length < 88) Array.Resize(ref token, 128);

        var authKey = new byte[24];
        if (token.Length >= 56) Array.Copy(token, 32, authKey, 0, 24);
        else authKey = DwCrypto.RandomBytes(24);
        if (authKey.All(b => b == 0)) authKey = DwCrypto.RandomBytes(24);

        var sessionKey = Auth3Keys.DefaultSessionKey;
        Auth3Keys.SetShared(sessionKey);

        var ticket = BuildAuthTicket(titleId, token);
        var iv = DwCrypto.TigerIv(ivSeed);
        var ticketEnc = DwCrypto.TripleDesEncrypt(iv, authKey, Pad(ticket, 128));
        if (ticketEnc.Length != 128) Array.Resize(ref ticketEnc, 128);

        var authData = new byte[128];
        Array.Copy(sessionKey, authData, 24);

        var json = new StringBuilder();
        json.Append('{');
        json.Append("\"auth_task\":\"29\",\"code\":\"700\",");
        json.Append($"\"iv_seed\":\"{ivSeed}\",");
        json.Append($"\"client_ticket\":\"{Convert.ToBase64String(ticketEnc)}\",");
        json.Append($"\"server_ticket\":\"{Convert.ToBase64String(authData)}\",");
        json.Append("\"client_id\":\"\",\"account_type\":\"steam\",");
        json.Append("\"crossplay_enabled\":false,\"loginqueue_enabled\":false,");
        json.Append("\"lsg_endpoint\":null,\"extra_data\":\"{\\\"extended_data\\\": \\\"\\\"}\"");
        json.Append('}');

        var bytes = Encoding.ASCII.GetBytes(json.ToString());
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers["Server"] = "TornadoServer/6.0.3";
        ctx.Response.Headers["X-Signature"] = "1337";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
        Log.Ok("Auth3", $"title={titleId}");
    }

    private static byte[] BuildAuthTicket(uint titleId, byte[] token)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)0x0EFBDADDE);
        bw.Write((byte)0);
        bw.Write(titleId);
        var now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bw.Write(now);
        bw.Write(now + 30000);
        bw.Write((ulong)0);
        ulong userId = token.Length >= 64 ? BitConverter.ToUInt64(token, 56) : 1UL;
        bw.Write(userId);
        var name = new byte[64];
        if (token.Length >= 128) Array.Copy(token, 64, name, 0, 64);
        else Encoding.ASCII.GetBytes("Player").CopyTo(name, 0);
        bw.Write(name);
        bw.Write(Auth3Keys.DefaultSessionKey);
        bw.Write(new byte[] { 0, 0, 0 });
        bw.Write(new byte[4]);
        return ms.ToArray();
    }

    private void Fileshare(HttpListenerContext ctx, string path)
    {
        if (path.StartsWith("/fileshare", StringComparison.OrdinalIgnoreCase))
            path = path["/fileshare".Length..];
        path = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(store.FileshareDir, path));
        if (!full.StartsWith(store.FileshareDir, StringComparison.OrdinalIgnoreCase))
        { Write(ctx, 403, "text/plain", "Forbidden"); return; }

        if (ctx.Request.HttpMethod == "GET")
        {
            if (!File.Exists(full)) { Write(ctx, 404, "text/html", "<h1>Not Found</h1>"); return; }
            var data = File.ReadAllBytes(full);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.OutputStream.Write(data);
            ctx.Response.Close();
            return;
        }

        if (ctx.Request.HttpMethod is "PUT" or "POST")
        {
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var fs = File.Create(full);
            ctx.Request.InputStream.CopyTo(fs);
            Write(ctx, 201, "text/plain", "");
            return;
        }

        Write(ctx, 501, "text/plain", "Not Implemented");
    }

    private static byte[] Pad(byte[] data, int size)
    {
        if (data.Length == size) return data;
        var r = new byte[size];
        Array.Copy(data, r, Math.Min(data.Length, size));
        return r;
    }

    private static void Write(HttpListenerContext ctx, int code, string type, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = type;
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }

    private static uint ParseUInt(string json, string name)
    {
        var s = Extract(json, name);
        return uint.TryParse(s, out var v) ? v : 0;
    }

    private static string? Extract(string json, string name)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var key = $"\"{name}\"";
        var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        idx = json.IndexOf(':', idx);
        if (idx < 0) return null;
        idx++;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        if (idx >= json.Length) return null;
        if (json[idx] == '"')
        {
            idx++;
            var sb = new StringBuilder();
            while (idx < json.Length)
            {
                if (json[idx] == '\\' && idx + 1 < json.Length) { sb.Append(json[idx + 1]); idx += 2; continue; }
                if (json[idx] == '"') break;
                sb.Append(json[idx++]);
            }
            return sb.ToString();
        }
        var end = idx;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
        return json[idx..end];
    }

    public override void Dispose()
    {
        try { _listener?.Stop(); } catch { }
        base.Dispose();
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(_ => new FileStore());
        builder.Services.AddHostedService<GatewayWorker>();
        builder.Build().Run();
    }
}

using System.Net;
using System.Net.Sockets;
using Demonware.Core;

namespace Demonware.Service.Stun;

public sealed class StunWorker(IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = config.GetValue("Ports:Stun", Ports.StunUdp);
        var report = config.GetValue("Ports:Report", Ports.LegacyTcp);
        using var udp = new UdpClient(port);
        Log.Ok("STUN", $"UDP listening on {port} (report {report})");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(stoppingToken);
                if (result.Buffer.Length < 3) continue;
                switch (result.Buffer[0])
                {
                    case 30: await SendIp(udp, result.RemoteEndPoint, report); break;
                    case 20: await SendNat(udp, result.RemoteEndPoint, report); break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Error("STUN", ex.Message); }
        }
    }

    private static async Task SendIp(UdpClient udp, IPEndPoint ep, int report)
    {
        var ip = BitConverter.ToUInt32(ep.Address.GetAddressBytes(), 0);
        var buf = new byte[9];
        buf[0] = 31; buf[1] = 2;
        Array.Copy(BitConverter.GetBytes(ip), 0, buf, 3, 4);
        Array.Copy(BitConverter.GetBytes((ushort)report), 0, buf, 7, 2);
        await udp.SendAsync(buf, ep);
    }

    private static async Task SendNat(UdpClient udp, IPEndPoint ep, int report)
    {
        var clientIp = BitConverter.ToUInt32(ep.Address.GetAddressBytes(), 0);
        uint serverIp = 0x0100007F;
        try
        {
            var local = ((IPEndPoint)udp.Client.LocalEndPoint!).Address;
            if (local.AddressFamily == AddressFamily.InterNetwork)
                serverIp = BitConverter.ToUInt32(local.GetAddressBytes(), 0);
        }
        catch { }

        var buf = new byte[15];
        buf[0] = 21; buf[1] = 2;
        Array.Copy(BitConverter.GetBytes(clientIp), 0, buf, 3, 4);
        Array.Copy(BitConverter.GetBytes((ushort)report), 0, buf, 7, 2);
        Array.Copy(BitConverter.GetBytes(serverIp), 0, buf, 9, 4);
        Array.Copy(BitConverter.GetBytes((ushort)report), 0, buf, 13, 2);
        await udp.SendAsync(buf, ep);
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddHostedService<StunWorker>();
        builder.Build().Run();
    }
}

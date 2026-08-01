using Demonware.Core;
using Demonware.Core.Net;
using Demonware.Core.Store;
using Demonware.Modern;

namespace Demonware.Service.ModernLobby;

public sealed class ModernLobbyWorker(IConfiguration config, FileStore store) : BackgroundService
{
    private TcpListenerService? _tcp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = config.GetValue("Ports:Modern", Ports.ModernTcp);
        var handler = new ModernLobbyHandler(store);
        _tcp = new TcpListenerService("Modern", port, handler);
        await _tcp.StartAsync(stoppingToken);
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_tcp is not null) await _tcp.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(_ => new FileStore());
        builder.Services.AddHostedService<ModernLobbyWorker>();
        builder.Build().Run();
    }
}

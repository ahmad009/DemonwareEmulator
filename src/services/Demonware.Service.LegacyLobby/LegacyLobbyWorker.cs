using Demonware.Core;
using Demonware.Core.Net;
using Demonware.Core.Store;
using Demonware.Legacy.Protocol;

namespace Demonware.Service.LegacyLobby;

public sealed class LegacyLobbyWorker(IConfiguration config, FileStore store, SessionKeyMap keys) : BackgroundService
{
    private TcpListenerService? _tcp;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = config.GetValue("Ports:Legacy", Ports.LegacyTcp);
        var handler = new LegacyLobbyHandler(keys, store, TitleId.Iw6);
        _tcp = new TcpListenerService("Legacy", port, handler);
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
        builder.Services.AddSingleton<SessionKeyMap>();
        builder.Services.AddHostedService<LegacyLobbyWorker>();
        builder.Build().Run();
    }
}

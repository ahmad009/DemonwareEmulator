using Demonware.Core;
using Demonware.Core.Store;
using Demonware.Service.Gateway;
using Demonware.Service.LegacyLobby;
using Demonware.Service.ModernLobby;
using Demonware.Service.Stun;

Log.Banner();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(_ => new FileStore());
builder.Services.AddSingleton<SessionKeyMap>();
builder.Services.AddHostedService<StunWorker>();
builder.Services.AddHostedService<GatewayWorker>();
builder.Services.AddHostedService<LegacyLobbyWorker>();
builder.Services.AddHostedService<ModernLobbyWorker>();

var host = builder.Build();
Log.Ok("Host", $"Legacy :{Ports.LegacyTcp} | Modern :{Ports.ModernTcp} | STUN :{Ports.StunUdp} | HTTP :{Ports.GatewayHttp}");
await host.RunAsync();

using Microsoft.AspNetCore.SignalR;
using RobRun.Server.Abstractions;
using RobRun.Server.Hubs;

namespace RobRun.Server.Infrastructure;

public sealed class GameNotifier(IHubContext<GameHub> hub) : IGameNotifier
{
    public Task Changed(string scope, int id, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("stateChanged", new { scope, id }, ct);
}

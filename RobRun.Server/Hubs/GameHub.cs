using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace RobRun.Server.Hubs;

[Authorize]
public sealed class GameHub : Hub;

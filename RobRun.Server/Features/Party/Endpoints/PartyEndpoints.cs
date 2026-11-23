using RobRun.Server.Abstractions;
using RobRun.Server.Features.Party.Data;

namespace RobRun.Server.Features.Party.Endpoints;

public static class PartyEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/party").RequireAuthorization();
        g.MapPost("/", async (ICurrentUser user, PartyService party, CancellationToken ct) =>
            HttpMap.From(await party.Create(user.PlayerId, ct)));
        g.MapPost("/invite", async (NameBody body, ICurrentUser user, PartyService party, CancellationToken ct) =>
            HttpMap.From(await party.Invite(user.PlayerId, body.Name, ct)));
        g.MapPost("/invites/{id:int}/accept", async (int id, ICurrentUser user, PartyService party, CancellationToken ct) =>
            HttpMap.From(await party.Accept(user.PlayerId, id, ct)));
        g.MapPost("/leave", async (ICurrentUser user, PartyService party, CancellationToken ct) =>
            HttpMap.From(await party.Leave(user.PlayerId, ct)));
    }
}

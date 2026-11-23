using RobRun.Server.Abstractions;
using RobRun.Server.Features.Clan.Data;

namespace RobRun.Server.Features.Clan.Endpoints;

public static class ClanEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/clan").RequireAuthorization();
        g.MapPost("/", async (NameBody body, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Create(user.PlayerId, body.Name, ct)));
        g.MapPost("/invite", async (NameBody body, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Invite(user.PlayerId, body.Name, ct)));
        g.MapPost("/invites/{id:int}/accept", async (int id, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Accept(user.PlayerId, id, ct)));
        g.MapPost("/kick", async (PlayerBody body, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Kick(user.PlayerId, body.PlayerId, ct)));
        g.MapPost("/leave", async (ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Leave(user.PlayerId, ct)));
        g.MapPost("/roles", async (RoleBody body, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.CreateRole(user.PlayerId, body.Name, body.Priority, body.Flags, ct)));
        g.MapPut("/roles/{id:int}", async (int id, RoleBody body, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.UpdateRole(user.PlayerId, id, body.Name, body.Priority, body.Flags, ct)));
        g.MapDelete("/roles/{id:int}", async (int id, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.DeleteRole(user.PlayerId, id, ct)));
        g.MapPost("/members/{playerId:int}/role", async (int playerId, RoleIdBody body, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Assign(user.PlayerId, playerId, body.RoleId, ct)));
        g.MapPost("/house", async (LocationBody body, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.DeclareHouse(user.PlayerId, body.LocationId, ct)));
        g.MapPost("/relocate", async (LocationBody body, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Relocate(user.PlayerId, body.LocationId, ct)));
        g.MapPost("/transfer", async (PlayerBody body, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Transfer(user.PlayerId, body.PlayerId, ct)));
        g.MapPost("/disband", async (ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Disband(user.PlayerId, ct)));
        g.MapPost("/treasury", async (AmountBody body, ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Withdraw(user.PlayerId, body.Amount, ct)));
        g.MapPost("/taxes", async (ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Taxes(user.PlayerId, ct)));
        g.MapPost("/claims", async (ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.Claim(user.PlayerId, ct)));
        g.MapPost("/war", async (ICurrentUser user, ClanService clan, CancellationToken ct) =>
            HttpMap.From(await clan.War(user.PlayerId, ct)));
    }
}

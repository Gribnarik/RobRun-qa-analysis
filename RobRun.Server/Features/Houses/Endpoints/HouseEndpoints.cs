using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Infrastructure;
using RobRun.Server.Features.Houses.Data;

namespace RobRun.Server.Features.Houses.Endpoints;

public static class HouseEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/locations", async (AppDb db, CancellationToken ct) =>
            Results.Ok(await db.Locations.AsNoTracking().OrderBy(l => l.HousePrice)
                .Select(l => new { l.Id, l.Tier, l.HousePrice }).ToListAsync(ct)));

        var g = app.MapGroup("/houses").RequireAuthorization();
        g.MapPost("/buy", async (LocationBody body, ICurrentUser user, HouseService houses, CancellationToken ct) =>
            HttpMap.From(await houses.BuyPersonal(user.PlayerId, body.LocationId, ct)));
        g.MapPost("/{id:int}/spend", async (int id, AmountBody body, ICurrentUser user, HouseService houses, CancellationToken ct) =>
            HttpMap.From(await houses.Spend(user.PlayerId, id, body.Amount, ct)));
        g.MapPost("/{id:int}/structures", async (int id, TextBody body, ICurrentUser user, HouseService houses, CancellationToken ct) =>
            HttpMap.From(await houses.Build(user.PlayerId, id, body.Name, ct)));
    }
}

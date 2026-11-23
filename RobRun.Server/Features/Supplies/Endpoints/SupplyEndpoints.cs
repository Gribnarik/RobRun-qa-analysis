using RobRun.Server.Abstractions;
using RobRun.Server.Features.Supplies.Data;

namespace RobRun.Server.Features.Supplies.Endpoints;

public static class SupplyEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/supplies").RequireAuthorization();
        g.MapPost("/{id:int}/study", async (int id, HouseBody body, ICurrentUser user, SupplyService supplies, CancellationToken ct) =>
            HttpMap.From(await supplies.Study(user.PlayerId, id, body.HouseId, ct)));
        g.MapPost("/{id:int}/buy", async (int id, HouseBody body, ICurrentUser user, SupplyService supplies, CancellationToken ct) =>
            HttpMap.From(await supplies.Buy(user.PlayerId, id, body.HouseId, ct)));
        g.MapPost("/migrate", async (SupplyService supplies, CancellationToken ct) =>
            HttpMap.From(await supplies.Migrate(ct)));
    }
}

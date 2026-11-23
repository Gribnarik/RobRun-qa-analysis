using RobRun.Server.Abstractions;
using RobRun.Server.Features.Me.Data;

namespace RobRun.Server.Features.Me.Endpoints;

public static class MeEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/me", async (ICurrentUser user, MeQuery me, CancellationToken ct) =>
        {
            var data = await me.Load(user.PlayerId, ct);
            return Results.Ok(new { ok = true, data });
        }).RequireAuthorization();
    }
}

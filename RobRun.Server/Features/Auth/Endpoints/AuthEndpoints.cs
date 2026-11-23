using RobRun.Server.Features.Auth.Data;

namespace RobRun.Server.Features.Auth.Endpoints;

public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/auth/register", async (NamePass body, AuthService auth, CancellationToken ct) =>
        {
            var (result, data) = await auth.Register(body.Name, body.Password, ct);
            return HttpMap.From(result, data);
        });
        app.MapPost("/auth/login", async (NamePass body, AuthService auth, CancellationToken ct) =>
        {
            var (result, data) = await auth.Login(body.Name, body.Password, ct);
            return HttpMap.From(result, data);
        });
    }
}

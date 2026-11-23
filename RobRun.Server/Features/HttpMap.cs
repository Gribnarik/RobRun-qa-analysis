using RobRun.Server.Abstractions;

namespace RobRun.Server.Features;

public static class HttpMap
{
    public static IResult From(OpResult result, object? data = null) =>
        result.Ok
            ? Results.Ok(new { ok = true, message = result.Message, data })
            : Results.BadRequest(new { ok = false, error = result.Error, message = result.Message });
}

public sealed record NamePass(string Name, string Password);
public sealed record NameBody(string Name);
public sealed record LocationBody(int LocationId);
public sealed record AmountBody(long Amount);
public sealed record TextBody(string Name);
public sealed record HouseBody(int HouseId);
public sealed record RoleBody(string Name, int Priority, int Flags);
public sealed record PlayerBody(int PlayerId);
public sealed record RoleIdBody(int RoleId);

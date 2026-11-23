using System.Security.Claims;
using RobRun.Server.Abstractions;

namespace RobRun.Server.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public int PlayerId =>
        int.Parse(http.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

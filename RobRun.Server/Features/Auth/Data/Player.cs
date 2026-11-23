using RobRun.Server.Abstractions;

namespace RobRun.Server.Features.Auth.Data;

public sealed class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public long Money { get; set; }
    public int? PartyId { get; set; }
}

using RobRun.Server.Abstractions;

namespace RobRun.Server.Features.Houses.Data;

public sealed class Location
{
    public int Id { get; set; }
    public string Tier { get; set; } = "";
    public long HousePrice { get; set; }
}

public sealed class House
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public int LocationId { get; set; }
    public HouseKind Kind { get; set; }
    public int? ClanId { get; set; }
    public long Money { get; set; }
}

public sealed class Structure
{
    public int Id { get; set; }
    public int HouseId { get; set; }
    public string Name { get; set; } = "";
    public int PlacedByPlayerId { get; set; }
}

public sealed class HouseLogEntry
{
    public int Id { get; set; }
    public int HouseId { get; set; }
    public int PlayerId { get; set; }
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime At { get; set; }
}

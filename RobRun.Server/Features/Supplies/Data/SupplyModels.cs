namespace RobRun.Server.Features.Supplies.Data;

public sealed class Supply
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long StudyPrice { get; set; }
    public long BuyPrice { get; set; }
}

public sealed class PlayerStudy
{
    public int PlayerId { get; set; }
    public int SupplyId { get; set; }
}

public sealed class HousePoolEntry
{
    public int HouseId { get; set; }
    public int SupplyId { get; set; }
}

public sealed class PlayerItem
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public int SupplyId { get; set; }
    public int HouseId { get; set; }
}

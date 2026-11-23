namespace RobRun.Server.Features.Party.Data;

public sealed class Party
{
    public int Id { get; set; }
    public int LeaderId { get; set; }
}

public sealed class PartyInvite
{
    public int Id { get; set; }
    public int PartyId { get; set; }
    public int FromPlayerId { get; set; }
    public int ToPlayerId { get; set; }
}
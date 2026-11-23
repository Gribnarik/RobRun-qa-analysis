using RobRun.Server.Abstractions;

namespace RobRun.Server.Features.Clan.Data;

public sealed class Clan
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long Treasury { get; set; }
}

public sealed class ClanRole
{
    public int Id { get; set; }
    public int ClanId { get; set; }
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public bool IsPreset { get; set; }
    public bool IsLeader { get; set; }
    public ClanPermission Flags { get; set; }
}

public sealed class ClanMember
{
    public int PlayerId { get; set; }
    public int ClanId { get; set; }
    public int RoleId { get; set; }
}

public sealed class ClanInvite
{
    public int Id { get; set; }
    public int ClanId { get; set; }
    public int FromPlayerId { get; set; }
    public int ToPlayerId { get; set; }
}

public sealed class ClanLogEntry
{
    public int Id { get; set; }
    public int ClanId { get; set; }
    public int PlayerId { get; set; }
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime At { get; set; }
}

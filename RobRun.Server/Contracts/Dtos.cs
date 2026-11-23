namespace RobRun.Server.Contracts;

public sealed class PlayerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long Money { get; set; }
}

public sealed class MemberDto
{
    public int PlayerId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class InviteDto
{
    public int Id { get; set; }
    public string FromName { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class HouseDto
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = "";
    public string Kind { get; set; } = "";
    public int? ClanId { get; set; }
    public int LocationId { get; set; }
    public string Tier { get; set; } = "";
    public long HousePrice { get; set; }
    public bool CanAccess { get; set; }
    public long? Money { get; set; }
    public List<int> Pool { get; set; } = [];
    public List<string> Structures { get; set; } = [];
}

public sealed class ItemDto
{
    public int SupplyId { get; set; }
    public int HouseId { get; set; }
}

public sealed class LocationDto
{
    public int Id { get; set; }
    public string Tier { get; set; } = "";
    public long HousePrice { get; set; }
}

public sealed class SupplyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long StudyPrice { get; set; }
    public long BuyPrice { get; set; }
}

public sealed class RoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public bool IsPreset { get; set; }
    public bool IsLeader { get; set; }
    public int Flags { get; set; }
}

public sealed class ClanMemberDto
{
    public int PlayerId { get; set; }
    public string Name { get; set; } = "";
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public int Priority { get; set; }
    public bool IsLeader { get; set; }
}

public sealed class ClanStateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long Treasury { get; set; }
    public int? HouseId { get; set; }
    public int MyRoleId { get; set; }
    public string MyRoleName { get; set; } = "";
    public int MyPriority { get; set; }
    public int MyFlags { get; set; }
    public bool IamLeader { get; set; }
    public List<RoleDto> Roles { get; set; } = [];
    public List<ClanMemberDto> Members { get; set; } = [];
}

public sealed class MeDto
{
    public PlayerDto Player { get; set; } = new();
    public int? PartyId { get; set; }
    public List<MemberDto> PartyMembers { get; set; } = [];
    public List<InviteDto> PartyInvites { get; set; } = [];
    public List<InviteDto> ClanInvites { get; set; } = [];
    public List<HouseDto> Houses { get; set; } = [];
    public List<int> Studies { get; set; } = [];
    public List<ItemDto> Items { get; set; } = [];
    public ClanStateDto? Clan { get; set; }
    public List<LocationDto> Locations { get; set; } = [];
    public List<SupplyDto> Supplies { get; set; } = [];
    public List<string> HouseLog { get; set; } = [];
    public List<string> ClanLog { get; set; } = [];
}

public sealed class LoginDto
{
    public string Token { get; set; } = "";
    public int PlayerId { get; set; }
    public string Name { get; set; } = "";
}

namespace RobRun.Server.Abstractions;

[Flags]
public enum ClanPermission
{
    None = 0,
    JoinClanWar = 1 << 0,
    Invite = 1 << 1,
    Kick = 1 << 2,
    ManageRoles = 1 << 3,
    UseClanHouse = 1 << 4,
    ClaimBusiness = 1 << 5,
    CollectTaxes = 1 << 6,
    WithdrawTreasury = 1 << 7,
    DeclareClanHouse = 1 << 8,
    All = JoinClanWar | Invite | Kick | ManageRoles | UseClanHouse | ClaimBusiness | CollectTaxes | WithdrawTreasury | DeclareClanHouse
}

public enum HouseAction
{
    Spend,
    Build,
    Study,
    Buy
}

public enum HouseKind
{
    Personal = 1,
    Clan = 2
}

public sealed record OpResult(bool Ok, string? Error = null, string? Message = null)
{
    public static OpResult Success(string? message = null) => new(true, null, message);
    public static OpResult Fail(string error, string message) => new(false, error, message);
}

public sealed record AccessResult(bool Ok, string? Error, string? Message)
{
    public static AccessResult Allow() => new(true, null, null);
    public static AccessResult Deny(string error, string message) => new(false, error, message);
    public OpResult ToOp() => Ok ? OpResult.Success() : OpResult.Fail(Error!, Message!);
}

public interface ICurrentUser
{
    int PlayerId { get; }
}

public interface IHouseLock
{
    Task<IAsyncDisposable> AcquireAsync(int houseId, CancellationToken ct);
}

public interface IGameNotifier
{
    Task Changed(string scope, int id, CancellationToken ct = default);
}

public interface IHouseAccess
{
    Task<AccessResult> CanAsync(int playerId, int houseId, HouseAction action, bool forUpdate, CancellationToken ct);
}

public interface IPoolSync
{
    Task AddAsync(int houseId, int supplyId, CancellationToken ct);
    Task CopyPlayerToClanAsync(int playerId, int clanId, CancellationToken ct);
    Task CopyMembersAsync(int clanId, CancellationToken ct);
}

public interface IClanAuth
{
    Task<ClanActor?> LoadAsync(int playerId, CancellationToken ct);
    OpResult RequireFlag(ClanActor actor, ClanPermission flag);
    OpResult CanKick(ClanActor actor, bool targetIsLeader, int targetPriority);
    OpResult CanEditRole(ClanActor actor, bool roleIsLeader, int rolePriority, ClanPermission newFlags);
    OpResult CanAssign(ClanActor actor, bool targetIsLeader, int targetPriority, bool newRoleIsLeader, int newRolePriority);
    OpResult CanCreateRole(ClanActor actor, int priority, ClanPermission flags, int existingCount);
}

public sealed record ClanActor(int ClanId, int PlayerId, int RoleId, string RoleName, int Priority, bool IsLeader, ClanPermission Flags);

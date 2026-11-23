using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Features.Clan.Data;

public sealed class ClanAuthService(AppDb db) : IClanAuth
{
    public async Task<ClanActor?> LoadAsync(int playerId, CancellationToken ct)
    {
        var row = await (
            from m in db.ClanMembers.AsNoTracking()
            join r in db.ClanRoles.AsNoTracking() on m.RoleId equals r.Id
            where m.PlayerId == playerId
            select new ClanActor(m.ClanId, m.PlayerId, r.Id, r.Name, r.Priority, r.IsLeader, r.Flags)
        ).FirstOrDefaultAsync(ct);
        return row;
    }

    public OpResult RequireFlag(ClanActor actor, ClanPermission flag) =>
        actor.Flags.HasFlag(flag)
            ? OpResult.Success()
            : OpResult.Fail("missing_flag", "Нет такого права");

    public OpResult CanKick(ClanActor actor, bool targetIsLeader, int targetPriority)
    {
        var gate = RequireFlag(actor, ClanPermission.Kick);
        if (!gate.Ok) return gate;
        if (targetIsLeader) return OpResult.Fail("hierarchy", "Главу исключить нельзя");
        if (targetPriority >= actor.Priority)
            return OpResult.Fail("hierarchy", "Нельзя исключить игрока с приоритетом не ниже своего");
        return OpResult.Success();
    }

    public OpResult CanEditRole(ClanActor actor, bool roleIsLeader, int rolePriority, ClanPermission newFlags)
    {
        var gate = RequireFlag(actor, ClanPermission.ManageRoles);
        if (!gate.Ok) return gate;
        if (roleIsLeader) return OpResult.Fail("leader_fixed", "Права главы фиксированы");
        if (rolePriority >= actor.Priority)
            return OpResult.Fail("hierarchy", "Нельзя менять роль с приоритетом не ниже своего");
        if ((newFlags & ~actor.Flags) != 0)
            return OpResult.Fail("missing_flag", "Нельзя выдать право, которого нет у вас");
        return OpResult.Success();
    }

    public OpResult CanAssign(ClanActor actor, bool targetIsLeader, int targetPriority, bool newRoleIsLeader, int newRolePriority)
    {
        var gate = RequireFlag(actor, ClanPermission.ManageRoles);
        if (!gate.Ok) return gate;
        if (targetIsLeader || newRoleIsLeader)
            return OpResult.Fail("leader_fixed", "Глава назначается только передачей");
        if (targetPriority >= actor.Priority || newRolePriority >= actor.Priority)
            return OpResult.Fail("hierarchy", "Нельзя менять роль с приоритетом не ниже своего");
        return OpResult.Success();
    }

    public OpResult CanCreateRole(ClanActor actor, int priority, ClanPermission flags, int existingCount)
    {
        var gate = RequireFlag(actor, ClanPermission.ManageRoles);
        if (!gate.Ok) return gate;
        if (existingCount >= 10) return OpResult.Fail("role_limit", "Лимит 10 ролей");
        if (priority <= 0 || priority >= actor.Priority)
            return OpResult.Fail("hierarchy", "Приоритет новой роли должен быть ниже вашего");
        if ((flags & ~actor.Flags) != 0)
            return OpResult.Fail("missing_flag", "Нельзя выдать право, которого нет у вас");
        return OpResult.Success();
    }
}

public static class ClanPresets
{
    public static readonly (string Name, int Priority, bool Leader, ClanPermission Flags)[] All =
    [
        ("Новобранец", 10, false, ClanPermission.UseClanHouse),
        ("Боец", 20, false, ClanPermission.UseClanHouse | ClanPermission.JoinClanWar),
        ("Офицер", 30, false, ClanPermission.UseClanHouse | ClanPermission.JoinClanWar | ClanPermission.Invite | ClanPermission.ClaimBusiness),
        ("Глава", 100, true, ClanPermission.All)
    ];
}

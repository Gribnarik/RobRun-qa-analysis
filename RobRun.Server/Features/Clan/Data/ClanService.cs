using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Features.Clan.Data;

public sealed class ClanService(AppDb db, IClanAuth auth, IPoolSync pools, IGameNotifier notifier)
{
    public async Task<OpResult> Create(int playerId, string name, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length is < 3 or > 32) return OpResult.Fail("validation", "Название клана от 3 до 32 символов");
        if (await db.ClanMembers.AnyAsync(m => m.PlayerId == playerId, ct))
            return OpResult.Fail("validation", "Вы уже в клане");
        if (await db.Clans.AnyAsync(c => c.Name == name, ct))
            return OpResult.Fail("duplicate", "Клан с таким именем уже есть");

        var clan = new Clan { Name = name };
        db.Clans.Add(clan);
        await db.SaveChangesAsync(ct);

        ClanRole? leader = null;
        foreach (var preset in ClanPresets.All)
        {
            var role = new ClanRole
            {
                ClanId = clan.Id,
                Name = preset.Name,
                Priority = preset.Priority,
                IsPreset = true,
                IsLeader = preset.Leader,
                Flags = preset.Flags
            };
            db.ClanRoles.Add(role);
            if (preset.Leader) leader = role;
        }
        await db.SaveChangesAsync(ct);
        db.ClanMembers.Add(new ClanMember { PlayerId = playerId, ClanId = clan.Id, RoleId = leader!.Id });
        LogService.Clan(db, clan.Id, playerId, "Создание", name);
        await db.SaveChangesAsync(ct);
        await notifier.Changed("clan", clan.Id, ct);
        return OpResult.Success("Клан создан");
    }

    public async Task<OpResult> Invite(int playerId, string name, CancellationToken ct)
    {
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        var gate = auth.RequireFlag(actor.Actor!, ClanPermission.Invite);
        if (!gate.Ok) return gate;

        var target = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name.Trim(), ct);
        if (target is null) return OpResult.Fail("not_found", "Игрок не найден");
        if (target.Id == playerId) return OpResult.Fail("validation", "Нельзя пригласить себя");
        if (await db.ClanMembers.AnyAsync(m => m.PlayerId == target.Id, ct))
            return OpResult.Fail("validation", "Игрок уже в клане");
        if (await db.ClanInvites.AnyAsync(i => i.ClanId == actor.Actor!.ClanId && i.ToPlayerId == target.Id, ct))
            return OpResult.Fail("duplicate", "Приглашение уже отправлено");

        db.ClanInvites.Add(new ClanInvite
        {
            ClanId = actor.Actor!.ClanId,
            FromPlayerId = playerId,
            ToPlayerId = target.Id
        });
        LogService.Clan(db, actor.Actor.ClanId, playerId, "Приглашение", target.Name);
        await db.SaveChangesAsync(ct);
        await notifier.Changed("clan", actor.Actor.ClanId, ct);
        return OpResult.Success("Приглашение в клан отправлено");
    }

    public async Task<OpResult> Accept(int playerId, int inviteId, CancellationToken ct)
    {
        await using var tx = await SqlGate.Begin(db, ct);
        await SqlGate.LockPlayer(db, playerId, ct);
        if (await db.ClanMembers.AnyAsync(m => m.PlayerId == playerId, ct))
            return OpResult.Fail("validation", "Вы уже в клане");
        var invite = await db.ClanInvites.FirstOrDefaultAsync(i => i.Id == inviteId && i.ToPlayerId == playerId, ct);
        if (invite is null) return OpResult.Fail("not_found", "Приглашение не найдено");

        var recruit = await db.ClanRoles.FirstAsync(r => r.ClanId == invite.ClanId && r.Name == "Новобранец", ct);
        db.ClanMembers.Add(new ClanMember { PlayerId = playerId, ClanId = invite.ClanId, RoleId = recruit.Id });
        db.ClanInvites.Remove(invite);
        await db.SaveChangesAsync(ct);
        await pools.CopyPlayerToClanAsync(playerId, invite.ClanId, ct);
        LogService.Clan(db, invite.ClanId, playerId, "Вступление", "Роль: Новобранец");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("clan", invite.ClanId, ct);
        return OpResult.Success("Вы вступили в клан");
    }

    public async Task<OpResult> Kick(int playerId, int targetId, CancellationToken ct)
    {
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        var target = await MemberRole(targetId, ct);
        if (target is null || target.Value.ClanId != actor.Actor!.ClanId)
            return OpResult.Fail("not_found", "Игрок не в вашем клане");
        var gate = auth.CanKick(actor.Actor, target.Value.IsLeader, target.Value.Priority);
        if (!gate.Ok) return gate;

        var row = await db.ClanMembers.FirstAsync(m => m.PlayerId == targetId, ct);
        db.ClanMembers.Remove(row);
        LogService.Clan(db, actor.Actor.ClanId, playerId, "Исключение", $"Игрок {targetId}");
        await db.SaveChangesAsync(ct);
        await notifier.Changed("clan", actor.Actor.ClanId, ct);
        return OpResult.Success("Игрок исключён");
    }

    public async Task<OpResult> Leave(int playerId, CancellationToken ct)
    {
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        if (actor.Actor!.IsLeader)
            return OpResult.Fail("leader_leave", "Глава не может выйти, пока не передаст главу или не распустит клан");

        var row = await db.ClanMembers.FirstAsync(m => m.PlayerId == playerId, ct);
        db.ClanMembers.Remove(row);
        LogService.Clan(db, actor.Actor.ClanId, playerId, "Выход", actor.Actor.RoleName);
        await db.SaveChangesAsync(ct);
        await notifier.Changed("clan", actor.Actor.ClanId, ct);
        return OpResult.Success("Вы вышли из клана");
    }

    public async Task<OpResult> CreateRole(int playerId, string name, int priority, int flags, CancellationToken ct)
    {
        if (!Known(flags)) return OpResult.Fail("validation", "Неизвестный набор прав");
        name = name.Trim();
        if (name.Length is < 1 or > 24) return OpResult.Fail("validation", "Название роли от 1 до 24 символов");
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        var count = await db.ClanRoles.CountAsync(r => r.ClanId == actor.Actor!.ClanId, ct);
        var gate = auth.CanCreateRole(actor.Actor!, priority, (ClanPermission)flags, count);
        if (!gate.Ok) return gate;
        if (await db.ClanRoles.AnyAsync(r => r.ClanId == actor.Actor!.ClanId && r.Name == name, ct))
            return OpResult.Fail("duplicate", "Роль с таким именем уже есть");

        db.ClanRoles.Add(new ClanRole
        {
            ClanId = actor.Actor!.ClanId,
            Name = name,
            Priority = priority,
            IsPreset = false,
            IsLeader = false,
            Flags = (ClanPermission)flags
        });
        LogService.Clan(db, actor.Actor.ClanId, playerId, "Роль", $"Создана {name}");
        await db.SaveChangesAsync(ct);
        await notifier.Changed("clan", actor.Actor.ClanId, ct);
        return OpResult.Success("Роль создана");
    }

    public async Task<OpResult> UpdateRole(int playerId, int roleId, string name, int priority, int flags, CancellationToken ct)
    {
        if (!Known(flags)) return OpResult.Fail("validation", "Неизвестный набор прав");
        name = name.Trim();
        if (name.Length is < 1 or > 24) return OpResult.Fail("validation", "Название роли от 1 до 24 символов");
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        var role = await db.ClanRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.ClanId == actor.Actor!.ClanId, ct);
        if (role is null) return OpResult.Fail("not_found", "Роль не найдена");
        var gate = auth.CanEditRole(actor.Actor!, role.IsLeader, role.Priority, (ClanPermission)flags);
        if (!gate.Ok) return gate;
        if (priority <= 0 || priority >= actor.Actor!.Priority)
            return OpResult.Fail("hierarchy", "Приоритет роли должен быть ниже вашего");
        if (await db.ClanRoles.AnyAsync(r => r.ClanId == role.ClanId && r.Name == name && r.Id != role.Id, ct))
            return OpResult.Fail("duplicate", "Роль с таким именем уже есть");

        role.Name = name;
        role.Priority = priority;
        role.Flags = (ClanPermission)flags;
        LogService.Clan(db, role.ClanId, playerId, "Роль", $"Изменена {name}");
        await db.SaveChangesAsync(ct);
        await notifier.Changed("clan", role.ClanId, ct);
        return OpResult.Success("Роль обновлена");
    }

    public async Task<OpResult> DeleteRole(int playerId, int roleId, CancellationToken ct)
    {
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        var gate = auth.RequireFlag(actor.Actor!, ClanPermission.ManageRoles);
        if (!gate.Ok) return gate;
        var role = await db.ClanRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.ClanId == actor.Actor!.ClanId, ct);
        if (role is null) return OpResult.Fail("not_found", "Роль не найдена");
        if (role.IsPreset) return OpResult.Fail("preset_role", "Предустановленные роли не удаляются");
        if (role.Priority >= actor.Actor!.Priority)
            return OpResult.Fail("hierarchy", "Нельзя удалить роль с приоритетом не ниже своего");
        if (await db.ClanMembers.AnyAsync(m => m.RoleId == role.Id, ct))
            return OpResult.Fail("validation", "Сначала назначьте этим игрокам другую роль");

        db.ClanRoles.Remove(role);
        LogService.Clan(db, actor.Actor.ClanId, playerId, "Роль", $"Удалена {role.Name}");
        await db.SaveChangesAsync(ct);
        await notifier.Changed("clan", actor.Actor.ClanId, ct);
        return OpResult.Success("Роль удалена");
    }

    public async Task<OpResult> Assign(int playerId, int targetId, int roleId, CancellationToken ct)
    {
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        var target = await MemberRole(targetId, ct);
        if (target is null || target.Value.ClanId != actor.Actor!.ClanId)
            return OpResult.Fail("not_found", "Игрок не в вашем клане");
        var role = await db.ClanRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId && r.ClanId == actor.Actor.ClanId, ct);
        if (role is null) return OpResult.Fail("not_found", "Роль не найдена");
        var gate = auth.CanAssign(actor.Actor, target.Value.IsLeader, target.Value.Priority, role.IsLeader, role.Priority);
        if (!gate.Ok) return gate;

        var row = await db.ClanMembers.FirstAsync(m => m.PlayerId == targetId, ct);
        row.RoleId = role.Id;
        LogService.Clan(db, actor.Actor.ClanId, playerId, "Назначение", $"{targetId} → {role.Name}");
        await db.SaveChangesAsync(ct);
        await notifier.Changed("clan", actor.Actor.ClanId, ct);
        return OpResult.Success("Роль назначена");
    }

    public async Task<OpResult> DeclareHouse(int playerId, int locationId, CancellationToken ct)
    {
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        var gate = auth.RequireFlag(actor.Actor!, ClanPermission.DeclareClanHouse);
        if (!gate.Ok) return gate;
        var loc = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct);
        if (loc is null) return OpResult.Fail("not_found", "Локация не найдена");

        await using var tx = await SqlGate.Begin(db, ct);
        await SqlGate.LockPlayer(db, playerId, ct);
        if (await db.Houses.AnyAsync(h => h.ClanId == actor.Actor!.ClanId && h.Kind == HouseKind.Clan, ct))
            return OpResult.Fail("clan_house_exists", "Клановый дом уже объявлен");
        if (!await SqlGate.DebitPlayer(db, playerId, loc.HousePrice, ct))
            return OpResult.Fail("no_money", "Не хватает своих денег на клановый дом");

        var house = new House
        {
            OwnerId = playerId,
            LocationId = loc.Id,
            Kind = HouseKind.Clan,
            ClanId = actor.Actor!.ClanId,
            Money = 0
        };
        db.Houses.Add(house);
        SqlGate.ForgetBalances(db);
        await db.SaveChangesAsync(ct);
        await pools.CopyMembersAsync(actor.Actor.ClanId, ct);
        LogService.House(db, house.Id, playerId, "Клановый дом", loc.Tier);
        LogService.Clan(db, actor.Actor.ClanId, playerId, "Клановый дом", loc.Tier);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("clan", actor.Actor.ClanId, ct);
        return OpResult.Success($"Клановый дом объявлен в {loc.Tier}");
    }

    public async Task<OpResult> Relocate(int playerId, int locationId, CancellationToken ct)
    {
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        var gate = auth.RequireFlag(actor.Actor!, ClanPermission.DeclareClanHouse);
        if (!gate.Ok) return gate;
        var loc = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct);
        if (loc is null) return OpResult.Fail("not_found", "Локация не найдена");
        var house = await db.Houses.AsNoTracking().FirstOrDefaultAsync(h => h.ClanId == actor.Actor!.ClanId && h.Kind == HouseKind.Clan, ct);
        if (house is null) return OpResult.Fail("not_found", "Кланового дома нет");
        if (house.LocationId == loc.Id) return OpResult.Success("Дом уже в этой локации");

        var oldPrice = await db.Locations.Where(l => l.Id == house.LocationId).Select(l => l.HousePrice).FirstAsync(ct);
        var diff = Math.Max(0, loc.HousePrice - oldPrice);
        await using var tx = await SqlGate.Begin(db, ct);
        await SqlGate.LockPlayer(db, playerId, ct);
        await SqlGate.LockHouse(db, house.Id, ct);
        if (!await SqlGate.DebitPlayer(db, playerId, diff, ct))
            return OpResult.Fail("no_money", "Не хватает денег на переезд");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Houses SET LocationId = {loc.Id} WHERE Id = {house.Id}", ct);
        LogService.House(db, house.Id, playerId, "Переезд", loc.Tier);
        LogService.Clan(db, actor.Actor!.ClanId, playerId, "Переезд", loc.Tier);
        SqlGate.ForgetBalances(db);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("house", house.Id, ct);
        return OpResult.Success($"Клан переехал в {loc.Tier}, пул остался на том же доме");
    }

    public async Task<OpResult> Transfer(int playerId, int targetId, CancellationToken ct)
    {
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        if (!actor.Actor!.IsLeader) return OpResult.Fail("missing_flag", "Передать главу может только глава");
        if (targetId == playerId) return OpResult.Fail("validation", "Нельзя передать главу себе");
        var target = await db.ClanMembers.FirstOrDefaultAsync(m => m.PlayerId == targetId && m.ClanId == actor.Actor.ClanId, ct);
        if (target is null) return OpResult.Fail("not_found", "Игрок не в вашем клане");

        var leader = await db.ClanRoles.FirstAsync(r => r.ClanId == actor.Actor.ClanId && r.IsLeader, ct);
        var officer = await db.ClanRoles.FirstAsync(r => r.ClanId == actor.Actor.ClanId && r.Name == "Офицер", ct);
        var me = await db.ClanMembers.FirstAsync(m => m.PlayerId == playerId, ct);
        target.RoleId = leader.Id;
        me.RoleId = officer.Id;
        LogService.Clan(db, actor.Actor.ClanId, playerId, "Передача главы", $"Новый глава: {targetId}");
        await db.SaveChangesAsync(ct);
        await notifier.Changed("clan", actor.Actor.ClanId, ct);
        return OpResult.Success("Глава передан, вы стали офицером");
    }

    public async Task<OpResult> Disband(int playerId, CancellationToken ct)
    {
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        if (!actor.Actor!.IsLeader) return OpResult.Fail("missing_flag", "Распустить клан может только глава");

        var clanId = actor.Actor.ClanId;
        await using var tx = await SqlGate.Begin(db, ct);
        var house = await db.Houses.FirstOrDefaultAsync(h => h.ClanId == clanId && h.Kind == HouseKind.Clan, ct);
        if (house is not null)
        {
            await db.HousePools.Where(p => p.HouseId == house.Id).ExecuteDeleteAsync(ct);
            await db.Structures.Where(s => s.HouseId == house.Id).ExecuteDeleteAsync(ct);
            await db.HouseLogs.Where(l => l.HouseId == house.Id).ExecuteDeleteAsync(ct);
            db.Houses.Remove(house);
        }
        await db.ClanInvites.Where(i => i.ClanId == clanId).ExecuteDeleteAsync(ct);
        await db.ClanMembers.Where(m => m.ClanId == clanId).ExecuteDeleteAsync(ct);
        await db.ClanLogs.Where(l => l.ClanId == clanId).ExecuteDeleteAsync(ct);
        await db.ClanRoles.Where(r => r.ClanId == clanId).ExecuteDeleteAsync(ct);
        var clan = await db.Clans.FirstAsync(c => c.Id == clanId, ct);
        db.Clans.Remove(clan);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("clan", clanId, ct);
        return OpResult.Success("Клан распущен, клановый дом и пул удалены");
    }

    public async Task<OpResult> Withdraw(int playerId, long amount, CancellationToken ct)
    {
        if (amount <= 0) return OpResult.Fail("validation", "Сумма должна быть больше нуля");
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        var gate = auth.RequireFlag(actor.Actor!, ClanPermission.WithdrawTreasury);
        if (!gate.Ok) return gate;

        await using var tx = await SqlGate.Begin(db, ct);
        await SqlGate.LockPlayer(db, playerId, ct);
        if (!await SqlGate.DebitTreasury(db, actor.Actor!.ClanId, amount, ct))
            return OpResult.Fail("no_money", "В казне недостаточно средств");
        await SqlGate.CreditPlayer(db, playerId, amount, ct);
        LogService.Clan(db, actor.Actor.ClanId, playerId, "Казна", $"Снято {amount}");
        SqlGate.ForgetBalances(db);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("clan", actor.Actor.ClanId, ct);
        return OpResult.Success($"Из казны снято {amount}");
    }

    public Task<OpResult> Taxes(int playerId, CancellationToken ct) =>
        FlagAction(playerId, ClanPermission.CollectTaxes, "Налоги", "Собраны налоги", async (clanId, token) =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Clans SET Treasury = Treasury + {1000} WHERE Id = {clanId}", token);
        }, ct);

    public Task<OpResult> Claim(int playerId, CancellationToken ct) =>
        FlagAction(playerId, ClanPermission.ClaimBusiness, "Притязание", "Объявлено притязание на бизнес", null, ct);

    public Task<OpResult> War(int playerId, CancellationToken ct) =>
        FlagAction(playerId, ClanPermission.JoinClanWar, "КВ", "Участие в клановой войне", null, ct);

    private async Task<OpResult> FlagAction(
        int playerId,
        ClanPermission flag,
        string action,
        string details,
        Func<int, CancellationToken, Task>? extra,
        CancellationToken ct)
    {
        var actor = await Need(playerId, ct);
        if (actor.Error is not null) return actor.Error;
        var gate = auth.RequireFlag(actor.Actor!, flag);
        if (!gate.Ok) return gate;
        if (extra is not null) await extra(actor.Actor!.ClanId, ct);
        LogService.Clan(db, actor.Actor!.ClanId, playerId, action, details);
        SqlGate.ForgetBalances(db);
        await db.SaveChangesAsync(ct);
        await notifier.Changed("clan", actor.Actor.ClanId, ct);
        return OpResult.Success(details);
    }

    private static bool Known(int flags) => (flags & ~(int)ClanPermission.All) == 0;

    private async Task<(ClanActor? Actor, OpResult? Error)> Need(int playerId, CancellationToken ct)
    {
        var actor = await auth.LoadAsync(playerId, ct);
        return actor is null ? (null, OpResult.Fail("no_clan", "Вы не в клане")) : (actor, null);
    }

    private async Task<(int ClanId, bool IsLeader, int Priority)?> MemberRole(int playerId, CancellationToken ct)
    {
        var row = await (
            from m in db.ClanMembers.AsNoTracking()
            join r in db.ClanRoles.AsNoTracking() on m.RoleId equals r.Id
            where m.PlayerId == playerId
            select new { m.ClanId, r.IsLeader, r.Priority }).FirstOrDefaultAsync(ct);
        return row is null ? null : (row.ClanId, row.IsLeader, row.Priority);
    }
}

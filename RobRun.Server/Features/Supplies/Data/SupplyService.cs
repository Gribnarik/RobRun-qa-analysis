using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Features.Supplies.Data;

public sealed class SupplyService(AppDb db, IHouseAccess access, IHouseLock houseLock, IPoolSync pools, IGameNotifier notifier)
{
    public async Task<OpResult> Study(int playerId, int supplyId, int houseId, CancellationToken ct)
    {
        var supply = await db.Supplies.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplyId, ct);
        if (supply is null) return OpResult.Fail("not_found", "Поставка не найдена");

        await using var hold = await houseLock.AcquireAsync(houseId, ct);
        await using var tx = await SqlGate.Begin(db, ct);
        var gate = await access.CanAsync(playerId, houseId, HouseAction.Study, true, ct);
        if (!gate.Ok) return gate.ToOp();

        var already = await db.PlayerStudies.AnyAsync(s => s.PlayerId == playerId && s.SupplyId == supplyId, ct);
        if (already)
            return OpResult.Fail("already_studied", "Поставка уже изучена, деньги не списаны");

        if (!await SqlGate.DebitPlayer(db, playerId, supply.StudyPrice, ct))
            return OpResult.Fail("no_money", "Не хватает денег на изучение");

        db.PlayerStudies.Add(new PlayerStudy { PlayerId = playerId, SupplyId = supplyId });
        var house = await db.Houses.AsNoTracking().FirstAsync(h => h.Id == houseId, ct);
        await pools.AddAsync(house.Id, supplyId, ct);

        var clanId = await db.ClanMembers.AsNoTracking()
            .Where(m => m.PlayerId == playerId)
            .Select(m => (int?)m.ClanId)
            .FirstOrDefaultAsync(ct);
        if (clanId is int cid)
        {
            var clanHouseId = await db.Houses.AsNoTracking()
                .Where(h => h.ClanId == cid && h.Kind == HouseKind.Clan)
                .Select(h => (int?)h.Id)
                .FirstOrDefaultAsync(ct);
            if (clanHouseId is int hid)
                await pools.AddAsync(hid, supplyId, ct);
        }

        var name = await db.Players.Where(p => p.Id == playerId).Select(p => p.Name).FirstAsync(ct);
        LogService.House(db, houseId, playerId, "Изучение", $"{name} изучил {supply.Name}");
        SqlGate.ForgetBalances(db);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("house", houseId, ct);
        return OpResult.Success($"Изучено: {supply.Name}");
    }

    public async Task<OpResult> Buy(int playerId, int supplyId, int houseId, CancellationToken ct)
    {
        var supply = await db.Supplies.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplyId, ct);
        if (supply is null) return OpResult.Fail("not_found", "Поставка не найдена");

        await using var hold = await houseLock.AcquireAsync(houseId, ct);
        await using var tx = await SqlGate.Begin(db, ct);
        var gate = await access.CanAsync(playerId, houseId, HouseAction.Buy, true, ct);
        if (!gate.Ok) return gate.ToOp();

        var known = await db.PlayerStudies.AnyAsync(s => s.PlayerId == playerId && s.SupplyId == supplyId, ct);
        if (!known)
        {
            var inPool = await db.HousePools.AnyAsync(p => p.HouseId == houseId && p.SupplyId == supplyId, ct);
            if (!inPool)
                return OpResult.Fail("not_studied", "Нет личного изучения и записи в пуле этого дома");
        }

        if (!await SqlGate.DebitPlayer(db, playerId, supply.BuyPrice, ct))
            return OpResult.Fail("no_money", "Не хватает денег на покупку");

        db.PlayerItems.Add(new PlayerItem { PlayerId = playerId, SupplyId = supplyId, HouseId = houseId });
        var name = await db.Players.Where(p => p.Id == playerId).Select(p => p.Name).FirstAsync(ct);
        LogService.House(db, houseId, playerId, "Покупка", $"{name} купил {supply.Name}");
        SqlGate.ForgetBalances(db);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("house", houseId, ct);
        return OpResult.Success($"Куплено: {supply.Name}");
    }

    public async Task<OpResult> Migrate(CancellationToken ct)
    {
        await using var tx = await SqlGate.Begin(db, ct);
        var personal = await db.Houses.AsNoTracking().Where(h => h.Kind == HouseKind.Personal).ToListAsync(ct);
        foreach (var house in personal)
        {
            var studies = await db.PlayerStudies.AsNoTracking()
                .Where(s => s.PlayerId == house.OwnerId)
                .Select(s => s.SupplyId)
                .ToListAsync(ct);
            foreach (var supplyId in studies)
                await pools.AddAsync(house.Id, supplyId, ct);
        }

        var clanIds = await db.Houses.AsNoTracking()
            .Where(h => h.Kind == HouseKind.Clan && h.ClanId != null)
            .Select(h => h.ClanId!.Value)
            .ToListAsync(ct);
        foreach (var clanId in clanIds)
            await pools.CopyMembersAsync(clanId, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("house", 0, ct);
        return OpResult.Success("Миграция пулов выполнена");
    }
}

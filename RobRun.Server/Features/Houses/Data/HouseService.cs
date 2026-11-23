using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Features.Houses.Data;

public sealed class HouseService(AppDb db, IHouseAccess access, IHouseLock houseLock, IGameNotifier notifier)
{
    public async Task<OpResult> BuyPersonal(int playerId, int locationId, CancellationToken ct)
    {
        if (await db.Houses.AnyAsync(h => h.OwnerId == playerId && h.Kind == HouseKind.Personal, ct))
            return OpResult.Fail("validation", "Личный дом уже есть");
        var loc = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct);
        if (loc is null) return OpResult.Fail("not_found", "Локация не найдена");

        await using var tx = await SqlGate.Begin(db, ct);
        await SqlGate.LockPlayer(db, playerId, ct);
        if (!await SqlGate.DebitPlayer(db, playerId, loc.HousePrice, ct))
            return OpResult.Fail("no_money", "Не хватает денег на дом");

        var house = new House
        {
            OwnerId = playerId,
            LocationId = loc.Id,
            Kind = HouseKind.Personal,
            Money = 0
        };
        db.Houses.Add(house);
        SqlGate.ForgetBalances(db);
        await db.SaveChangesAsync(ct);
        LogService.House(db, house.Id, playerId, "Покупка дома", loc.Tier);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("house", house.Id, ct);
        return OpResult.Success($"Куплен личный дом {loc.Tier}");
    }

    public async Task<OpResult> Spend(int playerId, int houseId, long amount, CancellationToken ct)
    {
        if (amount <= 0) return OpResult.Fail("validation", "Сумма должна быть больше нуля");
        await using var hold = await houseLock.AcquireAsync(houseId, ct);
        await using var tx = await SqlGate.Begin(db, ct);
        var gate = await access.CanAsync(playerId, houseId, HouseAction.Spend, true, ct);
        if (!gate.Ok) return gate.ToOp();
        if (!await SqlGate.DebitHouse(db, houseId, amount, ct))
            return OpResult.Fail("no_money", "Не хватает денег дома");

        var name = await PlayerName(playerId, ct);
        LogService.House(db, houseId, playerId, "Трата", $"{name} списал {amount}");
        SqlGate.ForgetBalances(db);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("house", houseId, ct);
        return OpResult.Success($"Списано {amount} из денег дома");
    }

    public async Task<OpResult> Build(int playerId, int houseId, string name, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 40) return OpResult.Fail("validation", "Название постройки от 1 до 40 символов");
        await using var hold = await houseLock.AcquireAsync(houseId, ct);
        await using var tx = await SqlGate.Begin(db, ct);
        var gate = await access.CanAsync(playerId, houseId, HouseAction.Build, true, ct);
        if (!gate.Ok) return gate.ToOp();

        db.Structures.Add(new Structure { HouseId = houseId, Name = name, PlacedByPlayerId = playerId });
        var player = await PlayerName(playerId, ct);
        LogService.House(db, houseId, playerId, "Постройка", $"{player} поставил {name}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("house", houseId, ct);
        return OpResult.Success($"Постройка «{name}» привязана к дому");
    }

    private Task<string> PlayerName(int playerId, CancellationToken ct) =>
        db.Players.Where(p => p.Id == playerId).Select(p => p.Name).FirstAsync(ct);
}

using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Features.Houses.Data;

public sealed class HouseAccessService(AppDb db) : IHouseAccess
{
    public async Task<AccessResult> CanAsync(int playerId, int houseId, HouseAction action, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate)
            await SqlGate.LockHouse(db, houseId, ct);

        var house = await db.Houses.AsNoTracking().FirstOrDefaultAsync(h => h.Id == houseId, ct);
        if (house is null)
            return AccessResult.Deny("not_found", "Дом не найден");

        if (forUpdate)
            await SqlGate.LockPlayer(db, playerId, ct);

        if (action == HouseAction.Study)
            return await CanStudy(playerId, house, forUpdate, ct);

        if (house.Kind == HouseKind.Personal)
            return await PersonalAccess(playerId, house, forUpdate, ct);

        return await ClanHouseAccess(playerId, house, ct);
    }

    private async Task<AccessResult> CanStudy(int playerId, House house, bool forUpdate, CancellationToken ct)
    {
        if (house.Kind == HouseKind.Personal)
        {
            if (house.OwnerId != playerId)
                return AccessResult.Deny("no_access", "Изучение доступно только в своём доме");
            return AccessResult.Allow();
        }

        return await ClanHouseAccess(playerId, house, ct);
    }

    private async Task<AccessResult> PersonalAccess(int playerId, House house, bool forUpdate, CancellationToken ct)
    {
        if (house.OwnerId == playerId)
            return AccessResult.Allow();

        if (forUpdate)
            await SqlGate.LockPlayer(db, house.OwnerId, ct);

        var meParty = await db.Players.AsNoTracking().Where(p => p.Id == playerId).Select(p => p.PartyId).FirstAsync(ct);
        var ownerParty = await db.Players.AsNoTracking().Where(p => p.Id == house.OwnerId).Select(p => p.PartyId).FirstAsync(ct);
        if (meParty is not null && meParty == ownerParty)
            return AccessResult.Allow();

        return AccessResult.Deny("no_access", "Нет доступа к дому");
    }

    private async Task<AccessResult> ClanHouseAccess(int playerId, House house, CancellationToken ct)
    {
        if (house.ClanId is null)
            return AccessResult.Deny("not_found", "У кланового дома нет клана");

        var flags = await (
            from m in db.ClanMembers.AsNoTracking()
            join r in db.ClanRoles.AsNoTracking() on m.RoleId equals r.Id
            where m.PlayerId == playerId && m.ClanId == house.ClanId
            select (ClanPermission?)r.Flags).FirstOrDefaultAsync(ct);

        if (flags is null || !flags.Value.HasFlag(ClanPermission.UseClanHouse))
            return AccessResult.Deny("no_access", "Нет права пользоваться клановым домом");

        return AccessResult.Allow();
    }
}

using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Features.Supplies.Data;

public sealed class PoolSync(AppDb db) : IPoolSync
{
    public async Task AddAsync(int houseId, int supplyId, CancellationToken ct)
    {
        if (db.HousePools.Local.Any(x => x.HouseId == houseId && x.SupplyId == supplyId)) return;
        var exists = await db.HousePools.AnyAsync(x => x.HouseId == houseId && x.SupplyId == supplyId, ct);
        if (!exists)
            db.HousePools.Add(new HousePoolEntry { HouseId = houseId, SupplyId = supplyId });
    }

    public async Task CopyPlayerToClanAsync(int playerId, int clanId, CancellationToken ct)
    {
        var houseId = await db.Houses.AsNoTracking()
            .Where(h => h.ClanId == clanId && h.Kind == HouseKind.Clan)
            .Select(h => (int?)h.Id)
            .FirstOrDefaultAsync(ct);
        if (houseId is null) return;
        var studies = await db.PlayerStudies.AsNoTracking()
            .Where(s => s.PlayerId == playerId)
            .Select(s => s.SupplyId)
            .ToListAsync(ct);
        foreach (var supplyId in studies)
            await AddAsync(houseId.Value, supplyId, ct);
    }

    public async Task CopyMembersAsync(int clanId, CancellationToken ct)
    {
        var members = await db.ClanMembers.AsNoTracking()
            .Where(m => m.ClanId == clanId)
            .Select(m => m.PlayerId)
            .ToListAsync(ct);
        foreach (var playerId in members)
            await CopyPlayerToClanAsync(playerId, clanId, ct);
    }
}

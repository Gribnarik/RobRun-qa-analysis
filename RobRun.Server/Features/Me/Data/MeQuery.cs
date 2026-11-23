using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Contracts;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Features.Me.Data;

public sealed class MeQuery(AppDb db, IHouseAccess access)
{
    public async Task<MeDto> Load(int playerId, CancellationToken ct)
    {
        var player = await db.Players.AsNoTracking().FirstAsync(p => p.Id == playerId, ct);
        var dto = new MeDto
        {
            Player = new PlayerDto { Id = player.Id, Name = player.Name, Money = player.Money },
            PartyId = player.PartyId,
            Studies = await db.PlayerStudies.AsNoTracking().Where(s => s.PlayerId == playerId).Select(s => s.SupplyId).ToListAsync(ct),
            Items = await db.PlayerItems.AsNoTracking().Where(i => i.PlayerId == playerId)
                .Select(i => new ItemDto { SupplyId = i.SupplyId, HouseId = i.HouseId }).ToListAsync(ct),
            Locations = await db.Locations.AsNoTracking().OrderBy(l => l.HousePrice)
                .Select(l => new LocationDto { Id = l.Id, Tier = l.Tier, HousePrice = l.HousePrice }).ToListAsync(ct),
            Supplies = await db.Supplies.AsNoTracking().OrderBy(s => s.Id)
                .Select(s => new SupplyDto { Id = s.Id, Name = s.Name, StudyPrice = s.StudyPrice, BuyPrice = s.BuyPrice }).ToListAsync(ct)
        };

        if (player.PartyId is int partyId)
        {
            dto.PartyMembers = await db.Players.AsNoTracking().Where(p => p.PartyId == partyId)
                .Select(p => new MemberDto { PlayerId = p.Id, Name = p.Name }).ToListAsync(ct);
        }

        dto.PartyInvites = await (
            from i in db.PartyInvites.AsNoTracking()
            join p in db.Players.AsNoTracking() on i.FromPlayerId equals p.Id
            where i.ToPlayerId == playerId
            select new InviteDto { Id = i.Id, FromName = p.Name, Detail = "Пачка" }).ToListAsync(ct);

        dto.ClanInvites = await (
            from i in db.ClanInvites.AsNoTracking()
            join p in db.Players.AsNoTracking() on i.FromPlayerId equals p.Id
            join c in db.Clans.AsNoTracking() on i.ClanId equals c.Id
            where i.ToPlayerId == playerId
            select new InviteDto { Id = i.Id, FromName = p.Name, Detail = c.Name }).ToListAsync(ct);

        var houses = await (
            from h in db.Houses.AsNoTracking()
            join o in db.Players.AsNoTracking() on h.OwnerId equals o.Id
            join l in db.Locations.AsNoTracking() on h.LocationId equals l.Id
            orderby h.Id
            select new { h.Id, h.OwnerId, Owner = o.Name, h.Kind, h.ClanId, h.LocationId, l.Tier, l.HousePrice, h.Money }
        ).ToListAsync(ct);

        var accessible = new List<int>();
        foreach (var h in houses)
        {
            var can = await access.CanAsync(playerId, h.Id, HouseAction.Spend, false, ct);
            var row = new HouseDto
            {
                Id = h.Id,
                OwnerId = h.OwnerId,
                OwnerName = h.Owner,
                Kind = h.Kind.ToString(),
                ClanId = h.ClanId,
                LocationId = h.LocationId,
                Tier = h.Tier,
                HousePrice = h.HousePrice,
                CanAccess = can.Ok
            };
            if (can.Ok)
            {
                row.Money = h.Money;
                row.Pool = await db.HousePools.AsNoTracking().Where(p => p.HouseId == h.Id).Select(p => p.SupplyId).ToListAsync(ct);
                row.Structures = await db.Structures.AsNoTracking().Where(s => s.HouseId == h.Id).Select(s => s.Name).ToListAsync(ct);
                accessible.Add(h.Id);
            }
            dto.Houses.Add(row);
        }

        if (accessible.Count > 0)
        {
            dto.HouseLog = await (
                from l in db.HouseLogs.AsNoTracking()
                join p in db.Players.AsNoTracking() on l.PlayerId equals p.Id
                where accessible.Contains(l.HouseId)
                orderby l.Id descending
                select $"{l.At:HH:mm:ss} {p.Name}: {l.Action}. {l.Details}"
            ).Take(40).ToListAsync(ct);
        }

        var membership = await (
            from m in db.ClanMembers.AsNoTracking()
            join r in db.ClanRoles.AsNoTracking() on m.RoleId equals r.Id
            join c in db.Clans.AsNoTracking() on m.ClanId equals c.Id
            where m.PlayerId == playerId
            select new { m.ClanId, c.Name, c.Treasury, r.Id, Role = r.Name, r.Priority, r.IsLeader, r.Flags }
        ).FirstOrDefaultAsync(ct);

        if (membership is not null)
        {
            dto.Clan = new ClanStateDto
            {
                Id = membership.ClanId,
                Name = membership.Name,
                Treasury = membership.Treasury,
                HouseId = await db.Houses.AsNoTracking()
                    .Where(h => h.ClanId == membership.ClanId && h.Kind == HouseKind.Clan)
                    .Select(h => (int?)h.Id).FirstOrDefaultAsync(ct),
                MyRoleId = membership.Id,
                MyRoleName = membership.Role,
                MyPriority = membership.Priority,
                MyFlags = (int)membership.Flags,
                IamLeader = membership.IsLeader,
                Roles = await db.ClanRoles.AsNoTracking().Where(r => r.ClanId == membership.ClanId).OrderBy(r => r.Priority)
                    .Select(r => new RoleDto
                    {
                        Id = r.Id,
                        Name = r.Name,
                        Priority = r.Priority,
                        IsPreset = r.IsPreset,
                        IsLeader = r.IsLeader,
                        Flags = (int)r.Flags
                    }).ToListAsync(ct),
                Members = await (
                    from m in db.ClanMembers.AsNoTracking()
                    join p in db.Players.AsNoTracking() on m.PlayerId equals p.Id
                    join r in db.ClanRoles.AsNoTracking() on m.RoleId equals r.Id
                    where m.ClanId == membership.ClanId
                    orderby r.Priority descending
                    select new ClanMemberDto
                    {
                        PlayerId = p.Id,
                        Name = p.Name,
                        RoleId = r.Id,
                        RoleName = r.Name,
                        Priority = r.Priority,
                        IsLeader = r.IsLeader
                    }).ToListAsync(ct)
            };
            dto.ClanLog = await (
                from l in db.ClanLogs.AsNoTracking()
                join p in db.Players.AsNoTracking() on l.PlayerId equals p.Id
                where l.ClanId == membership.ClanId
                orderby l.Id descending
                select $"{l.At:HH:mm:ss} {p.Name}: {l.Action}. {l.Details}"
            ).Take(40).ToListAsync(ct);
        }

        return dto;
    }
}

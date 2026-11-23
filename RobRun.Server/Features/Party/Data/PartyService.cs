using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Features.Party.Data;

public sealed class PartyService(AppDb db, IGameNotifier notifier)
{
    public async Task<OpResult> Create(int playerId, CancellationToken ct)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct);
        if (player is null) return OpResult.Fail("not_found", "Игрок не найден");
        if (player.PartyId is not null) return OpResult.Fail("in_party", "Вы уже в пачке");

        var party = new Party { LeaderId = playerId };
        db.Parties.Add(party);
        await db.SaveChangesAsync(ct);
        player.PartyId = party.Id;
        await db.SaveChangesAsync(ct);
        await notifier.Changed("party", party.Id, ct);
        return OpResult.Success("Пачка создана");
    }

    public async Task<OpResult> Invite(int playerId, string name, CancellationToken ct)
    {
        var me = await db.Players.AsNoTracking().FirstAsync(p => p.Id == playerId, ct);
        if (me.PartyId is null) return OpResult.Fail("not_in_party", "Сначала создайте пачку");
        var count = await db.Players.CountAsync(p => p.PartyId == me.PartyId, ct);
        if (count >= 3) return OpResult.Fail("party_full", "В пачке уже 3 человека");

        var target = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name.Trim(), ct);
        if (target is null) return OpResult.Fail("not_found", "Игрок не найден");
        if (target.Id == playerId) return OpResult.Fail("validation", "Нельзя пригласить себя");
        if (target.PartyId is not null) return OpResult.Fail("in_party", "Игрок уже в пачке");
        if (await db.PartyInvites.AnyAsync(i => i.PartyId == me.PartyId && i.ToPlayerId == target.Id, ct))
            return OpResult.Fail("duplicate", "Приглашение уже отправлено");

        db.PartyInvites.Add(new PartyInvite
        {
            PartyId = me.PartyId.Value,
            FromPlayerId = playerId,
            ToPlayerId = target.Id
        });
        await db.SaveChangesAsync(ct);
        await notifier.Changed("party", me.PartyId.Value, ct);
        return OpResult.Success("Приглашение отправлено");
    }

    public async Task<OpResult> Accept(int playerId, int inviteId, CancellationToken ct)
    {
        await using var tx = await SqlGate.Begin(db, ct);
        await SqlGate.LockPlayer(db, playerId, ct);
        var invite = await db.PartyInvites.FirstOrDefaultAsync(i => i.Id == inviteId && i.ToPlayerId == playerId, ct);
        if (invite is null) return OpResult.Fail("not_found", "Приглашение не найдено");

        var memberIds = await db.Players.Where(p => p.PartyId == invite.PartyId).Select(p => p.Id).ToListAsync(ct);
        foreach (var id in memberIds)
            await SqlGate.LockPlayer(db, id, ct);

        var me = await db.Players.FirstAsync(p => p.Id == playerId, ct);
        if (me.PartyId is not null) return OpResult.Fail("in_party", "Вы уже в пачке");
        var count = await db.Players.CountAsync(p => p.PartyId == invite.PartyId, ct);
        if (count >= 3) return OpResult.Fail("party_full", "В пачке уже 3 человека");

        me.PartyId = invite.PartyId;
        db.PartyInvites.Remove(invite);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.Changed("party", invite.PartyId, ct);
        return OpResult.Success("Вы вступили в пачку");
    }

    public async Task<OpResult> Leave(int playerId, CancellationToken ct)
    {
        await using var tx = await SqlGate.Begin(db, ct);
        var partyId = await db.Players.Where(p => p.Id == playerId).Select(p => p.PartyId).FirstAsync(ct);
        if (partyId is null) return OpResult.Fail("not_in_party", "Вы не в пачке");

        var memberIds = await db.Players.Where(p => p.PartyId == partyId).Select(p => p.Id).ToListAsync(ct);
        foreach (var id in memberIds)
            await SqlGate.LockPlayer(db, id, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Players SET PartyId = NULL WHERE Id = {playerId}", ct);
        var left = memberIds.Count - (memberIds.Contains(playerId) ? 1 : 0);
        if (left < 2)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Players SET PartyId = NULL WHERE PartyId = {partyId}", ct);
            await db.PartyInvites.Where(i => i.PartyId == partyId).ExecuteDeleteAsync(ct);
            await db.Parties.Where(p => p.Id == partyId).ExecuteDeleteAsync(ct);
        }

        await tx.CommitAsync(ct);
        await notifier.Changed("party", partyId.Value, ct);
        return OpResult.Success("Вы вышли из пачки");
    }

    public async Task<OpResult> Kick(int leaderId, string targetName, CancellationToken ct)
    {
        var leader = await db.Players.AsNoTracking().FirstAsync(p => p.Id == leaderId, ct);
        if (leader.PartyId is null) return OpResult.Fail("not_in_party", "Вы не в пачке");

        var party = await db.Parties.AsNoTracking().FirstAsync(p => p.Id == leader.PartyId, ct);
        if (party.LeaderId != leaderId) return OpResult.Fail("not_leader", "Только лидер может исключать участников");

        var target = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Name == targetName.Trim(), ct);
        if (target is null) return OpResult.Fail("not_found", "Игрок не найден");
        if (target.Id == leaderId) return OpResult.Fail("validation", "Нельзя исключить себя");
        if (target.PartyId != leader.PartyId) return OpResult.Fail("not_in_party", "Игрок не в вашей пачке");

        await using var tx = await SqlGate.Begin(db, ct);
        var memberIds = await db.Players.Where(p => p.PartyId == leader.PartyId).Select(p => p.Id).ToListAsync(ct);
        foreach (var id in memberIds)
            await SqlGate.LockPlayer(db, id, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Players SET PartyId = NULL WHERE Id = {target.Id}", ct);

        var left = memberIds.Count - 1;
        if (left < 2)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Players SET PartyId = NULL WHERE PartyId = {leader.PartyId}", ct);
            await db.PartyInvites.Where(i => i.PartyId == leader.PartyId).ExecuteDeleteAsync(ct);
            await db.Parties.Where(p => p.Id == leader.PartyId).ExecuteDeleteAsync(ct);
        }

        await tx.CommitAsync(ct);
        await notifier.Changed("party", leader.PartyId.Value, ct);
        return OpResult.Success($"Игрок {targetName} исключён из пачки");
    }
}
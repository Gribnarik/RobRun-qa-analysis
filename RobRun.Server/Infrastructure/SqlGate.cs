using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RobRun.Server.Infrastructure;

public static class SqlGate
{
    public static Task<IDbContextTransaction> Begin(AppDb db, CancellationToken ct) =>
        db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

    public static Task LockHouse(AppDb db, int houseId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Houses WHERE Id = {houseId} FOR UPDATE", ct);

    public static Task LockPlayer(AppDb db, int playerId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Players WHERE Id = {playerId} FOR UPDATE", ct);

    public static async Task<bool> DebitPlayer(AppDb db, int playerId, long amount, CancellationToken ct)
    {
        if (amount < 0) return false;
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Players SET Money = Money - {amount} WHERE Id = {playerId} AND Money >= {amount}", ct);
        return rows == 1;
    }

    public static async Task<bool> DebitHouse(AppDb db, int houseId, long amount, CancellationToken ct)
    {
        if (amount <= 0) return false;
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Houses SET Money = Money - {amount} WHERE Id = {houseId} AND Money >= {amount}", ct);
        return rows == 1;
    }

    public static async Task<bool> DebitTreasury(AppDb db, int clanId, long amount, CancellationToken ct)
    {
        if (amount <= 0) return false;
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Clans SET Treasury = Treasury - {amount} WHERE Id = {clanId} AND Treasury >= {amount}", ct);
        return rows == 1;
    }

    public static Task CreditPlayer(AppDb db, int playerId, long amount, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Players SET Money = Money + {amount} WHERE Id = {playerId}", ct);

    public static void ForgetBalances(AppDb db)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Unchanged or EntityState.Modified)) continue;
            if (entry.Entity is Features.Auth.Data.Player or Features.Houses.Data.House or Features.Clan.Data.Clan)
                entry.State = EntityState.Detached;
        }
    }
}

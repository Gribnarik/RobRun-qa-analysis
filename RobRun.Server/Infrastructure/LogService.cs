using RobRun.Server.Features.Clan.Data;
using RobRun.Server.Features.Houses.Data;

namespace RobRun.Server.Infrastructure;

public static class LogService
{
    public static void House(AppDb db, int houseId, int playerId, string action, string details) =>
        db.HouseLogs.Add(new HouseLogEntry
        {
            HouseId = houseId,
            PlayerId = playerId,
            Action = action,
            Details = details,
            At = DateTime.UtcNow
        });

    public static void Clan(AppDb db, int clanId, int playerId, string action, string details) =>
        db.ClanLogs.Add(new ClanLogEntry
        {
            ClanId = clanId,
            PlayerId = playerId,
            Action = action,
            Details = details,
            At = DateTime.UtcNow
        });
}

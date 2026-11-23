using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Features.Auth.Data;
using RobRun.Server.Features.Houses.Data;
using RobRun.Server.Features.Supplies.Data;

namespace RobRun.Server.Infrastructure;

public static class Seed
{
    public const string DemoPassword = "demo";
    public static readonly string[] DemoNames = ["hikkanikita", "gribnarik", "player3"];

    public static async Task Run(AppDb db, CancellationToken ct = default)
    {
        if (await db.Players.AnyAsync(ct)) return;

        db.Locations.AddRange(
            new Location { Tier = "T1", HousePrice = 10_000 },
            new Location { Tier = "T2", HousePrice = 25_000 },
            new Location { Tier = "T3", HousePrice = 50_000 });
        db.Supplies.AddRange(
            new Supply { Name = "KL-545 Mk2", StudyPrice = 50_000, BuyPrice = 6_000 },
            new Supply { Name = "NV-Scope", StudyPrice = 20_000, BuyPrice = 3_000 });
        await db.SaveChangesAsync(ct);

        var t1 = await db.Locations.SingleAsync(x => x.Tier == "T1", ct);
        foreach (var name in DemoNames)
        {
            var player = new Player
            {
                Name = name,
                PasswordHash = Passwords.Hash(DemoPassword),
                Money = 500_000
            };
            db.Players.Add(player);
            await db.SaveChangesAsync(ct);
            db.Houses.Add(new House
            {
                OwnerId = player.Id,
                LocationId = t1.Id,
                Kind = HouseKind.Personal,
                Money = 15_000
            });
            await db.SaveChangesAsync(ct);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Features.Auth.Data;
using RobRun.Server.Features.Clan.Data;
using RobRun.Server.Features.Houses.Data;
using RobRun.Server.Features.Party.Data;
using RobRun.Server.Features.Supplies.Data;

namespace RobRun.Server.Infrastructure;

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<PartyInvite> PartyInvites => Set<PartyInvite>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<House> Houses => Set<House>();
    public DbSet<Structure> Structures => Set<Structure>();
    public DbSet<HouseLogEntry> HouseLogs => Set<HouseLogEntry>();
    public DbSet<Supply> Supplies => Set<Supply>();
    public DbSet<PlayerStudy> PlayerStudies => Set<PlayerStudy>();
    public DbSet<HousePoolEntry> HousePools => Set<HousePoolEntry>();
    public DbSet<PlayerItem> PlayerItems => Set<PlayerItem>();
    public DbSet<Clan> Clans => Set<Clan>();
    public DbSet<ClanRole> ClanRoles => Set<ClanRole>();
    public DbSet<ClanMember> ClanMembers => Set<ClanMember>();
    public DbSet<ClanInvite> ClanInvites => Set<ClanInvite>();
    public DbSet<ClanLogEntry> ClanLogs => Set<ClanLogEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Player>(e =>
        {
            e.ToTable("Players");
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(16);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
        });
        model.Entity<PartyInvite>(e =>
        {
            e.HasIndex(x => new { x.PartyId, x.ToPlayerId }).IsUnique();
        });
        model.Entity<Location>(e => e.Property(x => x.Tier).HasMaxLength(8));
        model.Entity<House>(e =>
        {
            e.ToTable("Houses");
            e.Property(x => x.Kind).HasConversion<int>();
            e.HasIndex(x => x.OwnerId);
            e.HasIndex(x => x.ClanId);
        });
        model.Entity<Structure>(e => e.Property(x => x.Name).HasMaxLength(40));
        model.Entity<HouseLogEntry>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(40);
            e.Property(x => x.Details).HasMaxLength(300);
        });
        model.Entity<Supply>(e => e.Property(x => x.Name).HasMaxLength(40));
        model.Entity<PlayerStudy>(e => e.HasKey(x => new { x.PlayerId, x.SupplyId }));
        model.Entity<HousePoolEntry>(e => e.HasKey(x => new { x.HouseId, x.SupplyId }));
        model.Entity<Clan>(e =>
        {
            e.ToTable("Clans");
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(32);
        });
        model.Entity<ClanRole>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(24);
            e.Property(x => x.Flags).HasConversion<int>();
            e.HasIndex(x => new { x.ClanId, x.Name }).IsUnique();
        });
        model.Entity<ClanMember>(e =>
        {
            e.HasKey(x => x.PlayerId);
            e.HasIndex(x => x.ClanId);
        });
        model.Entity<ClanInvite>(e => e.HasIndex(x => new { x.ClanId, x.ToPlayerId }).IsUnique());
        model.Entity<ClanLogEntry>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(40);
            e.Property(x => x.Details).HasMaxLength(300);
        });
    }
}

using System.Net.Http.Headers;
using Xunit;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using RobRun.Server.Contracts;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Cs = "Server=127.0.0.1;Port=3307;Database=robrun_test;User=root;Password=robrun;TreatTinyAsBoolean=true";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:RobRun", Cs);
        builder.UseEnvironment("Testing");
    }
}

public static class DbReset
{
    public static async Task Run()
    {
        await using var db = DbFactory.Open(ApiFactory.Cs);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await Seed.Run(db);
    }
}

public sealed class ApiEnvelope<T>
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
}

[CollectionDefinition("db", DisableParallelization = true)]
public sealed class DbCollection;

[Collection("db")]
public sealed class RuleTests : IAsyncLifetime
{
    private static readonly ApiFactory Factory = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = Factory.CreateClient();

    public Task InitializeAsync() => DbReset.Run();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Party_access_is_mutual_and_drops_on_leave()
    {
        var a = await Login("hikkanikita");
        var b = await Login("gribnarik");
        var c = await Login("player3");
        await Ok(a, "/party");
        await Ok(a, "/party/invite", new { name = "gribnarik" });
        await Ok(a, "/party/invite", new { name = "player3" });
        await Accept(b, "/party/invites/");
        await Accept(c, "/party/invites/");

        var ma = await Me(a);
        var mb = await Me(b);
        var h1 = House(ma, "hikkanikita");
        var h2 = House(mb, "gribnarik");
        await Ok(b, $"/houses/{h1}/spend", new { amount = 1000 });
        await Ok(c, $"/houses/{h1}/spend", new { amount = 1000 });
        await Ok(a, $"/houses/{h2}/structures", new { name = "модуль" });
        var outsider = await Register("outsider");
        Assert.Equal("no_access", (await Send(outsider, $"/houses/{h1}/spend", new { amount = 1000 })).Error);

        await Ok(b, "/party/leave");
        var denied = await Send(b, $"/houses/{h1}/spend", new { amount = 1000 });
        Assert.False(denied.Ok);
        Assert.Equal("no_access", denied.Error);

        var own = await Send(b, $"/houses/{h2}/spend", new { amount = 1000 });
        Assert.True(own.Ok);
        var after = await Me(b);
        Assert.Contains("модуль", HouseRow(after, "gribnarik").Structures);
    }

    [Fact]
    public async Task Study_is_personal_and_purchase_follows_pool_rules()
    {
        var a = await Login("hikkanikita");
        var b = await Login("gribnarik");
        var c = await Login("player3");
        await Party(a, b, c);
        var ma = await Me(a);
        var mb = await Me(b);
        var mc = await Me(c);
        var kl = Supply(ma, "KL-545 Mk2");
        var nv = Supply(ma, "NV-Scope");
        var h1 = House(ma, "hikkanikita");
        var h2 = House(mb, "gribnarik");
        var h3 = House(mc, "player3");

        var studyThere = await Send(b, $"/supplies/{kl}/study", new { houseId = h1 });
        Assert.Equal("no_access", studyThere.Error);

        await Ok(a, $"/supplies/{kl}/study", new { houseId = h1 });
        Assert.True((await Send(a, $"/supplies/{kl}/buy", new { houseId = h1 })).Ok);
        Assert.True((await Send(a, $"/supplies/{kl}/buy", new { houseId = h2 })).Ok);
        Assert.True((await Send(a, $"/supplies/{kl}/buy", new { houseId = h3 })).Ok);
        Assert.True((await Send(b, $"/supplies/{kl}/buy", new { houseId = h1 })).Ok);
        Assert.True((await Send(c, $"/supplies/{kl}/buy", new { houseId = h1 })).Ok);
        Assert.Equal("not_studied", (await Send(b, $"/supplies/{kl}/buy", new { houseId = h2 })).Error);
        Assert.Equal("not_studied", (await Send(c, $"/supplies/{kl}/buy", new { houseId = h3 })).Error);
        Assert.Equal("not_studied", (await Send(b, $"/supplies/{kl}/buy", new { houseId = h3 })).Error);
        Assert.Equal("not_studied", (await Send(c, $"/supplies/{kl}/buy", new { houseId = h2 })).Error);

        await Ok(b, $"/supplies/{nv}/study", new { houseId = h2 });
        Assert.DoesNotContain(nv, HouseRow(await Me(a), "hikkanikita").Pool);
        Assert.Contains(nv, (await Me(b)).Studies);
        Assert.True((await Send(b, $"/supplies/{nv}/buy", new { houseId = h1 })).Ok);
        Assert.Equal("not_studied", (await Send(a, $"/supplies/{nv}/buy", new { houseId = h1 })).Error);

        var moneyAfter = (await Me(b)).Player.Money;
        var repeat = await Send(b, $"/supplies/{nv}/study", new { houseId = h2 });
        Assert.Equal("already_studied", repeat.Error);
        Assert.Equal(moneyAfter, (await Me(b)).Player.Money);

        await Ok(b, "/party/leave");
        Assert.Equal("no_access", (await Send(b, $"/supplies/{kl}/buy", new { houseId = h1 })).Error);
        Assert.DoesNotContain(kl, (await Me(b)).Studies);
        await Ok(a, "/party/leave");
        Assert.True((await Send(a, $"/supplies/{kl}/buy", new { houseId = h1 })).Ok);
        Assert.Equal("no_access", (await Send(a, $"/supplies/{kl}/buy", new { houseId = h2 })).Error);
    }

    [Fact]
    public async Task Two_spends_cannot_take_the_same_money()
    {
        var a = await Login("hikkanikita");
        var b = await Login("gribnarik");
        await Ok(a, "/party");
        await Ok(a, "/party/invite", new { name = "gribnarik" });
        await Accept(b, "/party/invites/");
        var house = House(await Me(a), "hikkanikita");
        var first = Send(a, $"/houses/{house}/spend", new { amount = 10_000 });
        var second = Send(b, $"/houses/{house}/spend", new { amount = 10_000 });
        var results = await Task.WhenAll(first, second);
        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Equal(1, results.Count(r => r.Error == "no_money"));
        Assert.Equal(5_000, HouseRow(await Me(a), "hikkanikita").Money);
    }

    [Fact]
    public async Task Migration_does_not_duplicate_pool()
    {
        var a = await Login("hikkanikita");
        var me = await Me(a);
        var house = House(me, "hikkanikita");
        var kl = Supply(me, "KL-545 Mk2");
        await Ok(a, $"/supplies/{kl}/study", new { houseId = house });
        await using (var db = DbFactory.Open(ApiFactory.Cs))
        {
            await db.HousePools.Where(p => p.HouseId == house && p.SupplyId == kl).ExecuteDeleteAsync();
        }
        await Ok(a, "/supplies/migrate");
        await Ok(a, "/supplies/migrate");
        Assert.Equal(1, HouseRow(await Me(a), "hikkanikita").Pool.Count(id => id == kl));
    }

    [Fact]
    public async Task Clan_flags_hierarchy_and_shared_pool()
    {
        var a = await Login("hikkanikita");
        var b = await Login("gribnarik");
        var c = await Login("player3");
        await Ok(a, "/clan", new { name = "Альфа" });
        await Ok(a, "/clan/invite", new { name = "gribnarik" });
        await Ok(a, "/clan/invite", new { name = "player3" });
        await Accept(b, "/clan/invites/");
        await Accept(c, "/clan/invites/");
        Assert.Equal("missing_flag", (await Send(c, "/clan/invite", new { name = "nobody" })).Error);

        var clan = (await Me(a)).Clan!;
        var officer = clan.Roles.Single(r => r.Name == "Офицер");
        var fighter = clan.Roles.Single(r => r.Name == "Боец");
        var recruit = clan.Roles.Single(r => r.Name == "Новобранец");
        var leader = clan.Roles.Single(r => r.IsLeader);
        var gribnarik = clan.Members.Single(m => m.Name == "gribnarik").PlayerId;
        var player3 = clan.Members.Single(m => m.Name == "player3").PlayerId;
        var kickAndRoles = officer.Flags | 4 | 8;
        await Put(a, $"/clan/roles/{officer.Id}", new { name = "Офицер", priority = officer.Priority, flags = kickAndRoles });
        await Ok(a, "/clan/roles", new { name = "Смотрящий", priority = 30, flags = 2 });
        var watcher = (await Me(a)).Clan!.Roles.Single(r => r.Name == "Смотрящий");
        await Ok(a, $"/clan/members/{gribnarik}/role", new { roleId = officer.Id });
        await Ok(a, $"/clan/members/{player3}/role", new { roleId = watcher.Id });

        Assert.Equal("hierarchy", (await Send(b, "/clan/kick", new { playerId = player3 })).Error);
        Assert.Equal("hierarchy", (await Send(b, HttpMethod.Put, $"/clan/roles/{officer.Id}", new { name = "Офицер", priority = 30, flags = kickAndRoles })).Error);
        Assert.Equal("hierarchy", (await Send(b, HttpMethod.Put, $"/clan/roles/{watcher.Id}", new { name = "Смотрящий", priority = 30, flags = 2 })).Error);

        await Ok(a, $"/clan/members/{player3}/role", new { roleId = fighter.Id });
        Assert.Equal("missing_flag", (await Send(b, HttpMethod.Put, $"/clan/roles/{fighter.Id}", new { name = "Боец", priority = fighter.Priority, flags = fighter.Flags | 128 })).Error);
        await Ok(b, "/clan/kick", new { playerId = player3 });
        Assert.Equal("leader_fixed", (await Send(a, HttpMethod.Put, $"/clan/roles/{leader.Id}", new { name = "Глава", priority = 100, flags = 1 })).Error);
        Assert.Equal("preset_role", (await Send(a, HttpMethod.Delete, $"/clan/roles/{recruit.Id}")).Error);

        for (var i = 0; i < 5; i++)
            await Ok(a, "/clan/roles", new { name = "Роль" + i, priority = 40 + i, flags = 16 });
        Assert.Equal("role_limit", (await Send(a, "/clan/roles", new { name = "Лишняя", priority = 50, flags = 16 })).Error);
        await Put(a, $"/clan/roles/{officer.Id}", new { name = "Офицер", priority = 30, flags = kickAndRoles | 64 });

        await Ok(a, "/clan/invite", new { name = "player3" });
        await Accept(c, "/clan/invites/");
        var t2 = (await Me(a)).Locations.Single(l => l.Tier == "T2").Id;
        await Ok(a, "/clan/house", new { locationId = t2 });
        var home = await Me(b);
        var clanHouse = home.Houses.Single(h => h.Kind == "Clan").Id;
        var nv = Supply(home, "NV-Scope");
        await Ok(b, $"/supplies/{nv}/study", new { houseId = clanHouse });
        var personal = House(home, "gribnarik");
        Assert.DoesNotContain(nv, HouseRow(await Me(b), "gribnarik").Pool);
        Assert.True((await Send(c, $"/supplies/{nv}/buy", new { houseId = clanHouse })).Ok);
        Assert.Equal("not_studied", (await Send(c, $"/supplies/{nv}/buy", new { houseId = House(await Me(c), "player3") })).Error);

        player3 = (await Me(a)).Clan!.Members.Single(m => m.Name == "player3").PlayerId;
        await Ok(a, "/clan/kick", new { playerId = player3 });
        Assert.Equal("no_access", (await Send(c, $"/houses/{clanHouse}/spend", new { amount = 1 })).Error);

        await Ok(a, "/clan/transfer", new { playerId = gribnarik });
        await Ok(a, "/clan/leave");
        Assert.Equal("leader_leave", (await Send(b, "/clan/leave")).Error);
        Assert.NotEqual(personal, clanHouse);
    }

    private async Task Party(HttpClient a, HttpClient b, HttpClient c)
    {
        await Ok(a, "/party");
        await Ok(a, "/party/invite", new { name = "gribnarik" });
        await Ok(a, "/party/invite", new { name = "player3" });
        await Accept(b, "/party/invites/");
        await Accept(c, "/party/invites/");
    }

    private async Task Accept(HttpClient client, string prefix)
    {
        var me = await Me(client);
        var invite = prefix.Contains("clan") ? me.ClanInvites.Single() : me.PartyInvites.Single();
        await Ok(client, prefix + invite.Id + "/accept");
    }

    private async Task<HttpClient> Register(string name)
    {
        var env = await Read<LoginDto>(await _http.PostAsJsonAsync("/auth/register", new { name, password = "demo" }));
        Assert.True(env.Ok, env.Message);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", env.Data!.Token);
        return client;
    }

    private async Task<HttpClient> Login(string name)
    {
        var env = await Read<LoginDto>(await _http.PostAsJsonAsync("/auth/login", new { name, password = "demo" }));
        Assert.True(env.Ok, env.Message);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", env.Data!.Token);
        return client;
    }

    private async Task<MeDto> Me(HttpClient client)
    {
        var env = await Read<MeDto>(await client.GetAsync("/me"));
        Assert.True(env.Ok, env.Message);
        return env.Data!;
    }

    private async Task Ok(HttpClient client, string url, object? body = null)
    {
        var env = await Send(client, url, body);
        Assert.True(env.Ok, env.Message);
    }

    private async Task Put(HttpClient client, string url, object body)
    {
        var env = await Send(client, HttpMethod.Put, url, body);
        Assert.True(env.Ok, env.Message);
    }

    private Task<ApiEnvelope<object>> Send(HttpClient client, string url, object? body = null) =>
        Send(client, HttpMethod.Post, url, body);

    private async Task<ApiEnvelope<object>> Send(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await Read<object>(await client.SendAsync(request));
    }

    private static async Task<ApiEnvelope<T>> Read<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApiEnvelope<T>>(text, Json) ?? new ApiEnvelope<T> { Message = text };
    }

    private static int House(MeDto me, string owner) => HouseRow(me, owner).Id;
    private static HouseDto HouseRow(MeDto me, string owner) => me.Houses.Single(h => h.OwnerName == owner && h.Kind == "Personal");
    private static int Supply(MeDto me, string name) => me.Supplies.Single(s => s.Name == name).Id;
}

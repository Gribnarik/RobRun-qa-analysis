using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RobRun.Server.Contracts;
using RobRun.Server.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace RobRun.Server.Tests.Problem2;

public sealed class SupplyPoolTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private static readonly ApiFactory Factory = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = Factory.CreateClient();

    public SupplyPoolTests(ITestOutputHelper output) => _output = output;

    public Task InitializeAsync() => DbReset.Run();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Owner_study_adds_supply_to_own_house_pool()
    {
        _output.WriteLine("✅ ТЕСТ: Изучение владельцем попадает в пул его дома");
        var owner = await Login("hikkanikita");
        var me = await Me(owner);
        var houseId = House(me, "hikkanikita"); // Исправлено: убрано .Id
        var kl = Supply(me, "KL-545 Mk2");

        await Ok(owner, $"/supplies/{kl}/study", new { houseId });

        var after = await Me(owner);
        Assert.Contains(kl, HouseRow(after, "hikkanikita").Pool);
        _output.WriteLine("  🎉 Пул дома обновлён корректно");
    }

    [Fact]
    public async Task Party_member_buys_from_owner_pool_in_owner_house()
    {
        _output.WriteLine("✅ ТЕСТ: Член пачки покупает из пула владельца в его доме");
        var owner = await Login("hikkanikita");
        var member = await Login("gribnarik");
        await Party(owner, member);

        var ownerMe = await Me(owner);
        var h1 = House(ownerMe, "hikkanikita"); // Исправлено
        var kl = Supply(ownerMe, "KL-545 Mk2");

        await Ok(owner, $"/supplies/{kl}/study", new { houseId = h1 });
        var buy = await Send(member, $"/supplies/{kl}/buy", new { houseId = h1 });

        Assert.True(buy.Ok, $"Покупка должна пройти, но: {buy.Message}");
        _output.WriteLine("  🎉 Покупка из пула владельца успешна");
    }

    [Fact]
    public async Task Party_member_cannot_buy_from_pool_in_own_house()
    {
        _output.WriteLine("✅ ТЕСТ: Пул соседа НЕ переезжает на персонажа в его доме");
        var owner = await Login("hikkanikita");
        var member = await Login("gribnarik");
        await Party(owner, member);

        var ownerMe = await Me(owner);
        var memberMe = await Me(member);
        var h1 = House(ownerMe, "hikkanikita"); // Исправлено
        var h2 = House(memberMe, "gribnarik");  // Исправлено
        var kl = Supply(ownerMe, "KL-545 Mk2");

        await Ok(owner, $"/supplies/{kl}/study", new { houseId = h1 });

        var buy = await Send(member, $"/supplies/{kl}/buy", new { houseId = h2 });
        Assert.False(buy.Ok);
        Assert.Equal("not_studied", buy.Error);
        _output.WriteLine("  🎉 Корректно: не изучено лично, не в пуле этого дома");
    }

    [Fact]
    public async Task Study_does_not_leak_to_other_party_members_pool()
    {
        _output.WriteLine("✅ ТЕСТ: Чужое изучение не пишется в пул соседа");
        var a = await Login("hikkanikita");
        var b = await Login("gribnarik");
        await Party(a, b);

        var aMe = await Me(a);
        var bMe = await Me(b);
        var h1 = House(aMe, "hikkanikita"); // Исправлено
        var h2 = House(bMe, "gribnarik");   // Исправлено
        var nv = Supply(aMe, "NV-Scope");

        await Ok(b, $"/supplies/{nv}/study", new { houseId = h2 });

        var poolA = HouseRow(await Me(a), "hikkanikita").Pool;
        Assert.DoesNotContain(nv, poolA);
        _output.WriteLine("  🎉 Пул hikkanikita чист — изучение соседа не просочилось");
    }

    [Fact]
    public async Task Pool_access_lost_after_party_leave()
    {
        _output.WriteLine("✅ ТЕСТ: После выхода из пачки доступ к пулу пропадает");
        var owner = await Login("hikkanikita");
        var member = await Login("gribnarik");
        await Party(owner, member);

        var me = await Me(owner);
        var h1 = House(me, "hikkanikita"); // Исправлено
        var kl = Supply(me, "KL-545 Mk2");
        await Ok(owner, $"/supplies/{kl}/study", new { houseId = h1 });

        await Ok(member, "/party/leave");
        var buy = await Send(member, $"/supplies/{kl}/buy", new { houseId = h1 });

        Assert.False(buy.Ok);
        Assert.Equal("no_access", buy.Error);
        _output.WriteLine("  🎉 Доступ к чужому пулу потерян мгновенно");
    }

    [Fact]
    public async Task Re_study_is_blocked_and_money_not_spent()
    {
        _output.WriteLine("✅ ТЕСТ: Повторное изучение не списывает деньги");
        var owner = await Login("hikkanikita");
        var me = await Me(owner);
        var h1 = House(me, "hikkanikita"); // Исправлено
        var kl = Supply(me, "KL-545 Mk2");
        var moneyBefore = me.Player.Money;

        await Ok(owner, $"/supplies/{kl}/study", new { houseId = h1 });
        var repeat = await Send(owner, $"/supplies/{kl}/study", new { houseId = h1 });

        Assert.Equal("already_studied", repeat.Error);
        Assert.Equal(moneyBefore - 50_000, (await Me(owner)).Player.Money);
        _output.WriteLine("  🎉 Деньги списаны ровно один раз");
    }

    // --- Хелперы ---
    private async Task<HttpClient> Login(string name)
    {
        var env = await Read<LoginDto>(await _http.PostAsJsonAsync("/auth/login", new { name, password = "demo" }));
        Assert.True(env.Ok, env.Message);
        var c = Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", env.Data!.Token);
        return c;
    }

    private async Task Party(HttpClient a, HttpClient b)
    {
        await Ok(a, "/party");
        await Ok(a, "/party/invite", new { name = "gribnarik" });
        var me = await Me(b);
        var inv = me.PartyInvites.Single();
        await Ok(b, $"/party/invites/{inv.Id}/accept");
    }

    private async Task<MeDto> Me(HttpClient c)
    {
        var env = await Read<MeDto>(await c.GetAsync("/me"));
        Assert.True(env.Ok, env.Message);
        return env.Data!;
    }

    private async Task Ok(HttpClient c, string url, object? body = null)
    {
        var env = await Send(c, url, body);
        Assert.True(env.Ok, env.Message);
    }

    private Task<ApiEnvelope<object>> Send(HttpClient c, string url, object? body = null)
        => Send(c, HttpMethod.Post, url, body);

    private async Task<ApiEnvelope<object>> Send(HttpClient c, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        return await Read<object>(await c.SendAsync(req));
    }

    private static async Task<ApiEnvelope<T>> Read<T>(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ApiEnvelope<T>>(text, Json) ?? new ApiEnvelope<T> { Message = text };
    }

    // ИСПРАВЛЕННЫЕ ХЕЛПЕРЫ: возвращают сразу int или HouseDto
    private static int House(MeDto me, string owner) => me.Houses.Single(h => h.OwnerName == owner && h.Kind == "Personal").Id;
    private static HouseDto HouseRow(MeDto me, string owner) => me.Houses.Single(h => h.OwnerName == owner && h.Kind == "Personal");
    private static int Supply(MeDto me, string name) => me.Supplies.Single(s => s.Name == name).Id;
}
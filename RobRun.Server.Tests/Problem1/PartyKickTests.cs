using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RobRun.Server.Contracts;
using RobRun.Server.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace RobRun.Server.Tests.Problem1;

public sealed class PartyKickTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private static readonly ApiFactory Factory = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = Factory.CreateClient();

    public PartyKickTests(ITestOutputHelper output) => _output = output;

    public Task InitializeAsync() => DbReset.Run();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Leader_can_kick_member()
    {
        _output.WriteLine("✅ ТЕСТ: Лидер может исключить участника из пачки");
        
        var leader = await Login("hikkanikita");
        var member = await Login("gribnarik");
        
        await Ok(leader, "/party");
        await Ok(leader, "/party/invite", new { name = "gribnarik" });
        await Accept(member, "/party/invites/");
        
        _output.WriteLine("  1. Лидер исключает участника...");
        var kickResult = await Send(leader, "/party/kick", new { name = "gribnarik" });
        
        Assert.True(kickResult.Ok, $"Кик должен пройти, но: {kickResult.Message}");
        _output.WriteLine("  2. Проверяем, что участник вышел из пачки...");
        
        var memberMe = await Me(member);
        Assert.Null(memberMe.PartyId);
        
        _output.WriteLine("  🎉 Участник успешно исключён!");
    }

    [Fact]
    public async Task Non_leader_cannot_kick()
    {
        _output.WriteLine("✅ ТЕСТ: Обычный участник не может исключать других");
        
        var leader = await Login("hikkanikita");
        var member = await Login("gribnarik");
        
        await Ok(leader, "/party");
        await Ok(leader, "/party/invite", new { name = "gribnarik" });
        await Accept(member, "/party/invites/");
        
        _output.WriteLine("  1. Участник пытается исключить лидера...");
        var kickResult = await Send(member, "/party/kick", new { name = "hikkanikita" });
        
        Assert.False(kickResult.Ok);
        Assert.Equal("not_leader", kickResult.Error);
        
        _output.WriteLine("  🎉 Корректно: только лидер может кикать!");
    }

    [Fact]
    public async Task Kicked_member_loses_house_access()
    {
        _output.WriteLine("✅ ТЕСТ: После кика доступ к дому лидера пропадает");
        
        var leader = await Login("hikkanikita");
        var member = await Login("gribnarik");
        
        await Ok(leader, "/party");
        await Ok(leader, "/party/invite", new { name = "gribnarik" });
        await Accept(member, "/party/invites/");
        
        var leaderMe = await Me(leader);
        var leaderHouseId = House(leaderMe, "hikkanikita");
        
        _output.WriteLine("  1. Лидер исключает участника...");
        await Ok(leader, "/party/kick", new { name = "gribnarik" });
        
        _output.WriteLine("  2. Участник пытается потратить деньги в доме лидера...");
        var spendResult = await Send(member, $"/houses/{leaderHouseId}/spend", new { amount = 1000 });
        
        Assert.False(spendResult.Ok);
        Assert.Equal("no_access", spendResult.Error);
        
        _output.WriteLine("  🎉 Доступ к дому потерян мгновенно после кика!");
    }

    [Fact]
    public async Task Leader_cannot_kick_self()
    {
        _output.WriteLine("✅ ТЕСТ: Лидер не может исключить себя");
        
        var leader = await Login("hikkanikita");
        await Ok(leader, "/party");
        
        _output.WriteLine("  1. Лидер пытается исключить себя...");
        var kickResult = await Send(leader, "/party/kick", new { name = "hikkanikita" });
        
        Assert.False(kickResult.Ok);
        Assert.Equal("validation", kickResult.Error);
        
        _output.WriteLine("  🎉 Корректно: нельзя исключить себя!");
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

    private async Task Accept(HttpClient client, string prefix)
    {
        var me = await Me(client);
        var invite = me.PartyInvites.Single();
        await Ok(client, prefix + invite.Id + "/accept");
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

    private static int House(MeDto me, string owner) => me.Houses.Single(h => h.OwnerName == owner && h.Kind == "Personal").Id;
}
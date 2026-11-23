using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;
using RobRun.Server.Contracts;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Tests.Problem1;

// Используем тот же ApiFactory и DbReset, что и в корневых тестах
// (В реальном проекте их лучше вынести в отдельный файл Shared/TestBase.cs)

public sealed class PartyAccessTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private static readonly ApiFactory Factory = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = Factory.CreateClient();

    public PartyAccessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync() => DbReset.Run();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Party_member_can_build_and_spend_in_owner_house()
    {
        _output.WriteLine("✅ ТЕСТ: Член пачки может строить и тратить деньги в доме владельца");
        
        var owner = await Login("hikkanikita");
        var member = await Login("gribnarik");

        _output.WriteLine("  1. Создаем пачку и добавляем участника...");
        await Ok(owner, "/party");
        await Ok(owner, "/party/invite", new { name = "gribnarik" });
        await Accept(member, "/party/invites/");

        var ownerMe = await Me(owner);
        var memberMe = await Me(member);
        var ownerHouseId = House(ownerMe, "hikkanikita").Id;

        _output.WriteLine("  2. Проверяем строительство членом пачки...");
        var buildResult = await Send(member, $"/houses/{ownerHouseId}/structures", new { name = "Станок" });
        Assert.True(buildResult.Ok, $"Ожидался успех, но получено: {buildResult.Message}");

        _output.WriteLine("  3. Проверяем трату денег членом пачки...");
        var spendResult = await Send(member, $"/houses/{ownerHouseId}/spend", new { amount = 1000 });
        Assert.True(spendResult.Ok, $"Ожидался успех, но получено: {spendResult.Message}");
        
        _output.WriteLine("  🎉 Тест пройден успешно!");
    }

    [Fact]
    public async Task Access_drops_immediately_on_party_leave()
    {
        _output.WriteLine("✅ ТЕСТ: Доступ к дому пропадает мгновенно после выхода из пачки");

        var owner = await Login("hikkanikita");
        var member = await Login("gribnarik");

        await Ok(owner, "/party");
        await Ok(owner, "/party/invite", new { name = "gribnarik" });
        await Accept(member, "/party/invites/");

        var ownerMe = await Me(owner);
        var memberMe = await Me(member);
        var ownerHouseId = House(ownerMe, "hikkanikita").Id;

        _output.WriteLine("  1. Участник выходит из пачки...");
        await Ok(member, "/party/leave");

        _output.WriteLine("  2. Попытка действия после выхода...");
        var deniedResult = await Send(member, $"/houses/{ownerHouseId}/spend", new { amount = 1000 });
        
        Assert.False(deniedResult.Ok, "Доступ должен быть запрещен после выхода из пачки");
        Assert.Equal("no_access", deniedResult.Error);
        
        _output.WriteLine("  🎉 Тест пройден успешно! Time-of-Check to Time-of-Use работает.");
    }

    [Fact]
    public async Task Study_is_forbidden_in_other_player_house_even_in_party()
    {
        _output.WriteLine("✅ ТЕСТ: Изучение поставок запрещено в чужом доме (даже в пачке)");

        var owner = await Login("hikkanikita");
        var member = await Login("gribnarik");

        await Ok(owner, "/party");
        await Ok(owner, "/party/invite", new { name = "gribnarik" });
        await Accept(member, "/party/invites/");

        var ownerMe = await Me(owner);
        var ownerHouseId = House(ownerMe, "hikkanikita").Id;
        var supplyId = Supply(ownerMe, "KL-545 Mk2");

        _output.WriteLine("  1. Член пачки пытается изучить поставку в доме владельца...");
        var studyResult = await Send(member, $"/supplies/{supplyId}/study", new { houseId = ownerHouseId });

        Assert.False(studyResult.Ok, "Изучение в чужом доме должно быть запрещено");
        Assert.Equal("no_access", studyResult.Error);

        _output.WriteLine("  🎉 Тест пройден успешно! Изучение остается личным действием.");
    }

    [Fact]
    public async Task Concurrent_spends_only_one_succeeds()
    {
        _output.WriteLine("✅ ТЕСТ: Race Condition - два одновременных списания обрабатываются атомарно");

        var owner = await Login("hikkanikita");
        var member = await Login("gribnarik");

        await Ok(owner, "/party");
        await Ok(owner, "/party/invite", new { name = "gribnarik" });
        await Accept(member, "/party/invites/");

        var ownerMe = await Me(owner);
        var houseId = House(ownerMe, "hikkanikita").Id;
        
        // В сиде у дома 15 000. Пытаемся списать по 10 000 одновременно.
        _output.WriteLine("  1. Запускаем два запроса на списание 10 000 ОДНОВРЕМЕННО...");
        var task1 = Send(member, $"/houses/{houseId}/spend", new { amount = 10_000 });
        var task2 = Send(member, $"/houses/{houseId}/spend", new { amount = 10_000 });

        var results = await Task.WhenAll(task1, task2);

        var successCount = results.Count(r => r.Ok);
        var noMoneyCount = results.Count(r => r.Error == "no_money");

        _output.WriteLine($"  2. Результат: Успешных = {successCount}, Отказов 'no_money' = {noMoneyCount}");

        Assert.Equal(1, successCount);
        Assert.Equal(1, noMoneyCount);

        var finalMe = await Me(owner);
        var finalMoney = HouseRow(finalMe, "hikkanikita").Money;
        _output.WriteLine($"  3. Итоговый баланс дома: {finalMoney} (ожидалось 5000)");
        
        Assert.Equal(5_000, finalMoney);
        _output.WriteLine("  🎉 Тест пройден успешно! Атомарность и транзакции работают.");
    }

    // --- Вспомогательные методы (дублируем из RuleTests для автономности файла) ---
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

    private async Task Accept(HttpClient client, string prefix)
    {
        var me = await Me(client);
        var invite = me.PartyInvites.Single();
        await Ok(client, prefix + invite.Id + "/accept");
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

    private static HouseDto House(MeDto me, string owner) => me.Houses.Single(h => h.OwnerName == owner && h.Kind == "Personal");
    private static HouseDto HouseRow(MeDto me, string owner) => me.Houses.Single(h => h.OwnerName == owner && h.Kind == "Personal");
    private static int Supply(MeDto me, string name) => me.Supplies.Single(s => s.Name == name).Id;
}
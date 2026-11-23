using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RobRun.Demo.Scenarios;

public static class Problem1Demo
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://127.0.0.1:5088") };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task Run()
    {
        Console.WriteLine("\n🎬 СЦЕНАРИЙ: Проблема 1 — Асимметрия прав доступа в пачке");
        Console.WriteLine(new string('─', 60));

        try
        {
            Console.WriteLine("[00:01] 👤 Логиним игроков: hikkanikita и gribnarik...");
            var hikkaToken = await Login("hikkanikita", "demo");
            var gribToken = await Login("gribnarik", "demo");
            Console.WriteLine("   ✅ Оба игрока вошли в систему");

            var hikkaHouseId = await GetPersonalHouseId(hikkaToken);
            var gribHouseId = await GetPersonalHouseId(gribToken);
            Console.WriteLine($"   🏠 Дом hikkanikita: ID={hikkaHouseId}");
            Console.WriteLine($"   🏠 Дом gribnarik: ID={gribHouseId}");

            Console.WriteLine("\n[00:02] 🤝 Подготовка пачки...");
            await CreatePartySafe(hikkaToken);
            await InviteToPartySafe(hikkaToken, "gribnarik");
            var inviteId = await GetInviteId(gribToken);
            
            if (inviteId > 0)
            {
                await AcceptInvite(gribToken, inviteId);
                Console.WriteLine("   ✅ Пачка активна");
            }
            else
            {
                Console.WriteLine("   ⚠️ Игроки уже в одной пачке (пропускаем создание)");
            }

            Console.WriteLine("\n[00:03] 🔨 gribnarik пытается построить сооружение в доме hikkanikita...");
            var buildResult = await BuildStructure(gribToken, hikkaHouseId, "Станок");
            Console.WriteLine(buildResult.ok ? "   ✅ УСПЕХ! Член пачки может строить" : $"   ❌ ОТКАЗ: {buildResult.error}");

            Console.WriteLine("\n[00:04] 💰 gribnarik пытается потратить деньги из дома hikkanikita...");
            var spendResult = await SpendMoney(gribToken, hikkaHouseId, 1000);
            Console.WriteLine(spendResult.ok ? "   ✅ УСПЕХ! Член пачки может тратить деньги" : $"   ❌ ОТКАЗ: {spendResult.error}");

            Console.WriteLine("\n[00:05] 🚪 gribnarik выходит из пачки...");
            await LeaveParty(gribToken);
            Console.WriteLine("   ✅ Вышел из пачки");

            Console.WriteLine("\n[00:06] 🔨 gribnarik снова пытается построить в доме hikkanikita...");
            var failResult = await BuildStructure(gribToken, hikkaHouseId, "Ещё станок");
            Console.WriteLine(!failResult.ok ? "   ✅ ОТКАЗ! Доступ пропал мгновенно" : "   ❌ ПРОБАЛ: Доступ остался, баг!");

            Console.WriteLine("\n[00:07] 📚 gribnarik пытается изучить поставку в доме hikkanikita...");
            var studyResult = await StudySupply(gribToken, hikkaHouseId, 1);
            Console.WriteLine(!studyResult.ok ? "   ✅ ОТКАЗ! Изучение запрещено в чужом доме" : "   ❌ ПРОБАЛ: Изучение прошло, баг!");

            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("🎉 Демонстрация Проблемы 1 завершена!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Ошибка: {ex.Message}");
        }
    }

    private static async Task<string> Login(string name, string password)
    {
        var response = await Client.PostAsJsonAsync("/auth/login", new { name, password });
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new Exception($"Логин '{name}' не удался: {content}");
        
        var data = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        if (data.TryGetProperty("data", out var d) && d.TryGetProperty("token", out var t)) return t.GetString()!;
        throw new Exception("Токен не найден");
    }

    private static async Task<int> GetPersonalHouseId(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await Client.SendAsync(req);
        var data = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), JsonOptions);
        
        foreach (var h in data.GetProperty("data").GetProperty("houses").EnumerateArray())
            if (h.GetProperty("kind").GetString() == "Personal" && h.GetProperty("canAccess").GetBoolean())
                return h.GetProperty("id").GetInt32();
                
        throw new Exception("Дом не найден");
    }

    // --- Безопасные методы для пачки (игнорируют ошибки, если состояние уже верное) ---
    private static async Task CreatePartySafe(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/party");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await Client.SendAsync(req);
        if (!res.IsSuccessStatusCode) 
            Console.WriteLine($"   ⚠️ {await res.Content.ReadAsStringAsync()}");
    }

    private static async Task InviteToPartySafe(string token, string target)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/party/invite");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { name = target });
        var res = await Client.SendAsync(req);
        if (!res.IsSuccessStatusCode) 
            Console.WriteLine($"   ⚠️ {await res.Content.ReadAsStringAsync()}");
    }

    private static async Task<int> GetInviteId(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await Client.SendAsync(req);
        var data = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), JsonOptions);
        var invites = data.GetProperty("data").GetProperty("partyInvites").EnumerateArray();
        return invites.Any() ? invites.First().GetProperty("id").GetInt32() : 0;
    }

    private static async Task AcceptInvite(string token, int inviteId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/party/invites/{inviteId}/accept");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await Client.SendAsync(req);
    }

    private static async Task<(bool ok, string error)> BuildStructure(string token, int houseId, string name)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/houses/{houseId}/structures");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { name });
        var res = await Client.SendAsync(req);
        var data = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), JsonOptions);
        return (data.GetProperty("ok").GetBoolean(), data.GetProperty("ok").GetBoolean() ? "" : data.GetProperty("error").GetString() ?? "Ошибка");
    }

    private static async Task<(bool ok, string error)> SpendMoney(string token, int houseId, long amount)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/houses/{houseId}/spend");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { amount });
        var res = await Client.SendAsync(req);
        var data = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), JsonOptions);
        return (data.GetProperty("ok").GetBoolean(), data.GetProperty("ok").GetBoolean() ? "" : data.GetProperty("error").GetString() ?? "Ошибка");
    }

    private static async Task LeaveParty(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/party/leave");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await Client.SendAsync(req);
    }

    private static async Task<(bool ok, string error)> StudySupply(string token, int houseId, int supplyId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/supplies/{supplyId}/study");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { houseId });
        var res = await Client.SendAsync(req);
        var data = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), JsonOptions);
        return (data.GetProperty("ok").GetBoolean(), data.GetProperty("ok").GetBoolean() ? "" : data.GetProperty("error").GetString() ?? "Ошибка");
    }
}
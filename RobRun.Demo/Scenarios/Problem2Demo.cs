using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RobRun.Demo.Scenarios;

public static class Problem2Demo
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://127.0.0.1:5088") };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task Run()
    {
        Console.WriteLine("\n🎬 СЦЕНАРИЙ: Проблема 2 — Пул изучений поставок");
        Console.WriteLine(new string('─', 60));

        try
        {
            Console.WriteLine("[00:01] 👤 Логиним hikkanikita и gribnarik...");
            var hikka = await Login("hikkanikita", "demo");
            var grib = await Login("gribnarik", "demo");

            var hikkaHouse = await GetPersonalHouseId(hikka);
            var gribHouse = await GetPersonalHouseId(grib);
            var kl545 = await GetSupplyId(hikka, "KL-545 Mk2");
            var nvScope = await GetSupplyId(grib, "NV-Scope");
            Console.WriteLine($"   ✅ Домики: {hikkaHouse}/{gribHouse}, поставки: KL={kl545}, NV={nvScope}");

            Console.WriteLine("\n[00:02] 🤝 Создаём пачку...");
            await CreatePartySafe(hikka);
            await InviteSafe(hikka, "gribnarik");
            var inv = await GetInviteId(grib);
            if (inv > 0) await AcceptInvite(grib, inv);
            Console.WriteLine("   ✅ Пачка активна");

            // Сценарий 1: владелец изучает — попадает в пул его дома
            Console.WriteLine("\n[00:03] 📚 hikkanikita изучает KL-545 в своём доме...");
            var study1 = await Study(hikka, kl545, hikkaHouse);
            Console.WriteLine(study1.ok ? "   ✅ Изучено, попало в пул дома hikkanikita" : $"   ❌ {study1.error}");

            // Сценарий 2: член пачки покупает из пула владельца в его доме
            Console.WriteLine("\n[00:04] 🛒 gribnarik покупает KL-545 в доме hikkanikita (через пул)...");
            var buy1 = await Buy(grib, kl545, hikkaHouse);
            Console.WriteLine(buy1.ok ? "   ✅ УСПЕХ! Член пачки купил из пула владельца" : $"   ❌ {buy1.error}");

            // Сценарий 3: член пачки пытается купить в СВОЁМ доме — нет в пуле
            Console.WriteLine("\n[00:05] 🛒 gribnarik пытается купить KL-545 в СВОЁМ доме...");
            var buy2 = await Buy(grib, kl545, gribHouse);
            Console.WriteLine(!buy2.ok && buy2.error == "not_studied"
                ? "   ✅ ОТКАЗ 'not_studied'! Пул соседа не переехал на персонажа"
                : $"   ❌ Неожиданный результат: {buy2.error}");

            // Сценарий 4: член пачки изучает у себя — НЕ попадает в пул соседа
            Console.WriteLine("\n[00:06] 📚 gribnarik изучает NV-Scope в своём доме...");
            var study2 = await Study(grib, nvScope, gribHouse);
            Console.WriteLine(study2.ok ? "   ✅ Изучено, попало в пул дома gribnarik" : $"   ❌ {study2.error}");

            Console.WriteLine("\n[00:07] 🔍 hikkanikita проверяет свой пул (NV-Scope там быть не должно)...");
            var hikkaPool = await GetHousePool(hikka, hikkaHouse);
            Console.WriteLine(!hikkaPool.Contains(nvScope)
                ? "   ✅ NV-Scope НЕ в пуле hikkanikita (чужое изучение не пишется в соседний пул)"
                : "   ❌ ПРОБАЛ: NV-Scope просочился в пул соседа!");

            Console.WriteLine("\n[00:08] 🛒 hikkanikita пытается купить NV-Scope в своём доме...");
            var buy3 = await Buy(hikka, nvScope, hikkaHouse);
            Console.WriteLine(!buy3.ok && buy3.error == "not_studied"
                ? "   ✅ ОТКАЗ 'not_studied'! Чужое изучение не открывает покупку"
                : $"   ❌ Неожиданно: {buy3.error}");

            // Сценарий 5: после выхода из пачки доступ к пулу пропадает
            Console.WriteLine("\n[00:09] 🚪 gribnarik выходит из пачки...");
            await LeaveParty(grib);
            Console.WriteLine("\n[00:10] 🛒 gribnarik пытается купить KL-545 в доме hikkanikita...");
            var buy4 = await Buy(grib, kl545, hikkaHouse);
            Console.WriteLine(!buy4.ok && buy4.error == "no_access"
                ? "   ✅ ОТКАЗ 'no_access'! Доступ к пулу потерян вместе с пачкой"
                : $"   ❌ Неожиданно: {buy4.error}");

            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("🎉 Демонстрация Проблемы 2 завершена!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Ошибка: {ex.Message}");
        }
    }

    private static async Task<string> Login(string name, string pass)
    {
        var r = await Client.PostAsJsonAsync("/auth/login", new { name, password = pass });
        var c = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw new Exception($"Логин {name}: {c}");
        var d = JsonSerializer.Deserialize<JsonElement>(c, Json);
        if (d.TryGetProperty("data", out var dd) && dd.TryGetProperty("token", out var t)) return t.GetString()!;
        throw new Exception($"Токен не найден: {c}");
    }

    private static async Task<int> GetPersonalHouseId(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await Client.SendAsync(req);
        var d = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), Json);
        foreach (var h in d.GetProperty("data").GetProperty("houses").EnumerateArray())
            if (h.GetProperty("kind").GetString() == "Personal" && h.GetProperty("canAccess").GetBoolean())
                return h.GetProperty("id").GetInt32();
        throw new Exception("Дом не найден");
    }

    private static async Task<int> GetSupplyId(string token, string name)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await Client.SendAsync(req);
        var d = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), Json);
        foreach (var s in d.GetProperty("data").GetProperty("supplies").EnumerateArray())
            if (s.GetProperty("name").GetString() == name) return s.GetProperty("id").GetInt32();
        throw new Exception($"Поставка '{name}' не найдена");
    }

    private static async Task<List<int>> GetHousePool(string token, int houseId)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await Client.SendAsync(req);
        var d = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), Json);
        var result = new List<int>();
        foreach (var h in d.GetProperty("data").GetProperty("houses").EnumerateArray())
            if (h.GetProperty("id").GetInt32() == houseId)
                foreach (var p in h.GetProperty("pool").EnumerateArray())
                    result.Add(p.GetInt32());
        return result;
    }

    private static async Task CreatePartySafe(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/party");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await Client.SendAsync(req);
    }

    private static async Task InviteSafe(string token, string name)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/party/invite");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { name });
        await Client.SendAsync(req);
    }

    private static async Task<int> GetInviteId(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await Client.SendAsync(req);
        var d = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), Json);
        var invites = d.GetProperty("data").GetProperty("partyInvites").EnumerateArray();
        return invites.Any() ? invites.First().GetProperty("id").GetInt32() : 0;
    }

    private static async Task AcceptInvite(string token, int id)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/party/invites/{id}/accept");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await Client.SendAsync(req);
    }

    private static async Task<(bool ok, string error)> Study(string token, int supplyId, int houseId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/supplies/{supplyId}/study");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { houseId });
        var res = await Client.SendAsync(req);
        var d = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), Json);
        var ok = d.GetProperty("ok").GetBoolean();
        return (ok, ok ? "" : d.GetProperty("error").GetString() ?? "err");
    }

    private static async Task<(bool ok, string error)> Buy(string token, int supplyId, int houseId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/supplies/{supplyId}/buy");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { houseId });
        var res = await Client.SendAsync(req);
        var d = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync(), Json);
        var ok = d.GetProperty("ok").GetBoolean();
        return (ok, ok ? "" : d.GetProperty("error").GetString() ?? "err");
    }

    private static async Task LeaveParty(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/party/leave");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await Client.SendAsync(req);
    }
}
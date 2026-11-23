using Microsoft.EntityFrameworkCore;
using RobRun.Server.Abstractions;
using RobRun.Server.Contracts;
using RobRun.Server.Infrastructure;

namespace RobRun.Server.Features.Auth.Data;

public sealed class AuthService(AppDb db, IConfiguration config)
{
    public async Task<(OpResult Result, LoginDto? Data)> Register(string name, string password, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length is < 3 or > 16)
            return (OpResult.Fail("validation", "Имя от 3 до 16 символов"), null);
        if (password.Length < 4)
            return (OpResult.Fail("validation", "Пароль короче 4 символов"), null);
        if (await db.Players.AnyAsync(p => p.Name == name, ct))
            return (OpResult.Fail("duplicate", "Имя занято"), null);

        var player = new Player
        {
            Name = name,
            PasswordHash = Passwords.Hash(password),
            Money = 500_000
        };
        db.Players.Add(player);
        await db.SaveChangesAsync(ct);
        return (OpResult.Success("Игрок создан"), Token(player));
    }

    public async Task<(OpResult Result, LoginDto? Data)> Login(string name, string password, CancellationToken ct)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Name == name.Trim(), ct);
        if (player is null || !Passwords.Verify(password, player.PasswordHash))
            return (OpResult.Fail("auth", "Неверное имя или пароль"), null);
        return (OpResult.Success(), Token(player));
    }

    private LoginDto Token(Player player) => new()
    {
        Token = JwtHelper.Create(config, player.Id, player.Name),
        PlayerId = player.Id,
        Name = player.Name
    };
}

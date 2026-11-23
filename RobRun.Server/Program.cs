using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RobRun.Server.Abstractions;
using RobRun.Server.Features.Auth.Data;
using RobRun.Server.Features.Auth.Endpoints;
using RobRun.Server.Features.Clan.Data;
using RobRun.Server.Features.Clan.Endpoints;
using RobRun.Server.Features.Houses.Data;
using RobRun.Server.Features.Houses.Endpoints;
using RobRun.Server.Features.Me.Data;
using RobRun.Server.Features.Me.Endpoints;
using RobRun.Server.Features.Party.Data;
using RobRun.Server.Features.Party.Endpoints;
using RobRun.Server.Features.Supplies.Data;
using RobRun.Server.Features.Supplies.Endpoints;
using RobRun.Server.Hubs;
using RobRun.Server.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration.GetConnectionString("RobRun")
    ?? throw new InvalidOperationException("Нет строки подключения RobRun");

builder.Services.AddDbContext<AppDb>(o =>
    o.UseMySql(cs, DbFactory.Version(cs)));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs/game"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<IHouseLock, HouseLock>();
builder.Services.AddSingleton<IGameNotifier, GameNotifier>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IHouseAccess, HouseAccessService>();
builder.Services.AddScoped<IClanAuth, ClanAuthService>();
builder.Services.AddScoped<IPoolSync, PoolSync>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PartyService>();
builder.Services.AddScoped<HouseService>();
builder.Services.AddScoped<SupplyService>();
builder.Services.AddScoped<ClanService>();
builder.Services.AddScoped<MeQuery>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

AuthEndpoints.Map(app);
HouseEndpoints.Map(app);
PartyEndpoints.Map(app);
SupplyEndpoints.Map(app);
ClanEndpoints.Map(app);
MeEndpoints.Map(app);
app.MapHub<GameHub>("/hubs/game");
app.MapGet("/health", () => Results.Ok(new { ok = true }));

if (!app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.EnsureCreatedAsync();
    await Seed.Run(db);
}

app.Run();

public partial class Program;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShopFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("identity");

string? cs = builder.Configuration.GetConnectionString("Identity");
builder.Services.AddDbContext<IdentityContext>(o =>
{
    if (string.IsNullOrEmpty(cs)) o.UseInMemoryDatabase("identity");
    else o.UseNpgsql(cs);
});
if (!string.IsNullOrEmpty(cs)) builder.Services.AddHealthChecks().AddNpgSql(cs, tags: new[] { "ready" });

builder.Services.AddIdentityCore<ShopUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<IdentityContext>();
builder.Services.AddSingleton<TokenService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IdentityContext>().Database.EnsureCreatedAsync();
    await Seed.RunAsync(scope.ServiceProvider, app.Configuration);
}

app.MapDefaultEndpoints();
var api = app.MapGroup("/api/identity").WithTags("Identity");

api.MapPost("/register", async (RegisterRequest req, UserManager<ShopUser> users, TokenService tokens) =>
{
    var user = new ShopUser { UserName = req.Email, Email = req.Email, FullName = req.FullName.Trim() };
    if (string.IsNullOrWhiteSpace(user.FullName))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["fullName"] = new[] { "Full name is required" } });
    var result = await users.CreateAsync(user, req.Password);
    if (!result.Succeeded)
        return Results.ValidationProblem(result.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
    await users.AddToRoleAsync(user, "customer");
    return Results.Ok(await tokens.IssueAsync(user, users));
});

api.MapPost("/login", async (LoginRequest req, UserManager<ShopUser> users, TokenService tokens) =>
{
    var user = await users.FindByEmailAsync(req.Email);
    if (user == null || await users.IsLockedOutAsync(user)) return Results.Unauthorized();
    if (!await users.CheckPasswordAsync(user, req.Password))
    {
        await users.AccessFailedAsync(user);
        return Results.Unauthorized();
    }
    await users.ResetAccessFailedCountAsync(user);
    return Results.Ok(await tokens.IssueAsync(user, users));
});

api.MapGet("/me", (ClaimsPrincipal user) => new
{
    id = user.UserId(),
    email = user.Email(),
    name = user.FindFirstValue("name"),
    roles = user.FindAll("role").Select(c => c.Value),
}).RequireAuthorization();

app.Run();

public class ShopUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
}

public class IdentityContext(DbContextOptions<IdentityContext> options) : IdentityDbContext<ShopUser>(options);

public record RegisterRequest(string Email, string Password, string FullName);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, DateTime ExpiresAt, string UserId, string Email, string FullName, IList<string> Roles);

public class TokenService(JwtSettings jwt)
{
    public async Task<AuthResponse> IssueAsync(ShopUser user, UserManager<ShopUser> users)
    {
        var roles = await users.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new("sub", user.Id),
            new("email", user.Email!),
            new("name", user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim("role", r)));
        var expires = DateTime.UtcNow.AddMinutes(jwt.LifetimeMinutes);
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims, expires: expires,
            signingCredentials: new SigningCredentials(jwt.SigningKey(), SecurityAlgorithms.HmacSha256));
        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, user.Id, user.Email!, user.FullName, roles);
    }
}

static class Seed
{
    public static async Task RunAsync(IServiceProvider sp, IConfiguration config)
    {
        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "admin", "customer" })
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

        string email = config["Seed:AdminEmail"] ?? "admin@shopflow.dev";
        string? password = config["Seed:AdminPassword"];
        if (string.IsNullOrEmpty(password)) return;
        var users = sp.GetRequiredService<UserManager<ShopUser>>();
        if (await users.FindByEmailAsync(email) != null) return;
        var admin = new ShopUser { UserName = email, Email = email, FullName = "ShopFlow Admin", EmailConfirmed = true };
        await users.CreateAsync(admin, password);
        await users.AddToRolesAsync(admin, new[] { "admin", "customer" });
    }
}

public partial class Program;

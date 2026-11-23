using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Identity.Tests;

public class IdentityApiTests : IClassFixture<IdentityApiTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Identity", "");
            builder.UseSetting("Seed:AdminPassword", "Admin#2025");
        }
    }

    private readonly Factory _f;
    public IdentityApiTests(Factory f) => _f = f;

    [Fact]
    public async Task Register_login_and_me()
    {
        var c = _f.CreateClient();
        var reg = await c.PostAsJsonAsync("/api/identity/register", new { email = "new@shop.dev", password = "Str0ng!Pass", fullName = "New Buyer" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var auth = await reg.Content.ReadFromJsonAsync<JsonElement>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.GetProperty("token").GetString());
        Assert.Contains(jwt.Claims, x => x.Type == "role" && x.Value == "customer");
        Assert.Contains(jwt.Claims, x => x.Type == "email" && x.Value == "new@shop.dev");

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/identity/register", new { email = "new@shop.dev", password = "Str0ng!Pass", fullName = "Dup" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/identity/register", new { email = "weak@shop.dev", password = "weak", fullName = "W" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/identity/register", new { email = "noname@shop.dev", password = "Str0ng!Pass", fullName = " " })).StatusCode);

        var login = await c.PostAsJsonAsync("/api/identity/login", new { email = "new@shop.dev", password = "Str0ng!Pass" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        string token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/identity/login", new { email = "new@shop.dev", password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/identity/login", new { email = "nobody@shop.dev", password = "x" })).StatusCode);

        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await c.GetFromJsonAsync<JsonElement>("/api/identity/me");
        Assert.Equal("New Buyer", me.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Seeded_admin_gets_the_admin_role()
    {
        var res = await _f.CreateClient().PostAsJsonAsync("/api/identity/login", new { email = "admin@shopflow.dev", password = "Admin#2025" });
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(jwt.Claims, x => x.Type == "role" && x.Value == "admin");
    }
}

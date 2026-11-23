using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using ShopFlow.ServiceDefaults;

namespace ShopFlow.Testing;

/// <summary>Creates JWTs signed with the development key, as the Identity service would.</summary>
public static class TestTokens
{
    public static string For(string userId, string email = "buyer@test.dev", params string[] roles)
    {
        var jwt = new JwtSettings();
        var claims = new List<Claim> { new("sub", userId), new("email", email), new("name", "Test User") };
        claims.AddRange(roles.Select(r => new Claim("role", r)));
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims, expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(jwt.SigningKey(), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static HttpClient As(this HttpClient client, string userId, params string[] roles)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", For(userId, $"{userId}@test.dev", roles));
        return client;
    }
}

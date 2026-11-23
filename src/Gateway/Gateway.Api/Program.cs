using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

// API gateway: one entry point for the storefront. Routes /api/{service}/** to the
// right microservice (YARP), applies CORS and per-client rate limiting.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173" })
    .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-client", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimit:PerMinute", 300), Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseCors();
app.UseRateLimiter();
app.MapHealthChecks("/health/live");
app.MapReverseProxy().RequireRateLimiting("per-client");
app.Run();

public partial class Program;

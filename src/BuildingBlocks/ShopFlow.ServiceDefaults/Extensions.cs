using System.Security.Claims;
using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ShopFlow.ServiceDefaults;

/// <summary>Cross-cutting setup shared by every service: auth, health checks, tracing, OpenAPI, problem details.</summary>
public static class Extensions
{
    public const string AdminPolicy = "admin";

    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        var services = builder.Services;
        services.AddProblemDetails();
        services.AddExceptionHandler<ValidationExceptionHandler>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = $"ShopFlow {serviceName}", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>(),
            });
        });

        services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" });

        // Every service validates the same JWTs issued by the Identity service.
        var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
        services.AddSingleton(jwt);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = jwt.ValidationParameters();
            });
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, p => p.RequireRole("admin"));

        string? otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource("MassTransit");
                if (!string.IsNullOrEmpty(otlp)) t.AddOtlpExporter();
            });
        return builder;
    }

    /// <summary>Configures MassTransit with RabbitMQ, or the in-memory transport when Messaging:Transport is "InMemory" (tests, local runs).</summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration config, Action<IBusRegistrationConfigurator> configure)
    {
        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            configure(x);
            if (string.Equals(config["Messaging:Transport"], "InMemory", StringComparison.OrdinalIgnoreCase))
            {
                x.UsingInMemory((ctx, cfg) => cfg.ConfigureEndpoints(ctx));
            }
            else
            {
                x.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.Host(config["RabbitMq:Host"] ?? "localhost", "/", h =>
                    {
                        h.Username(config["RabbitMq:Username"] ?? "guest");
                        h.Password(config["RabbitMq:Password"] ?? "guest");
                    });
                    cfg.UseMessageRetry(r => r.Intervals(100, 500, 1000));
                    cfg.ConfigureEndpoints(ctx);
                });
            }
        });
        return services;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready");
        return app;
    }

    /// <summary>The caller's user id (JWT "sub").</summary>
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("Token has no subject");

    public static string? Email(this ClaimsPrincipal user) => user.FindFirstValue("email");
}

public class JwtSettings
{
    public string Issuer { get; set; } = "shopflow-identity";
    public string Audience { get; set; } = "shopflow";
    public string Key { get; set; } = "development-only-signing-key-change-me-0123456789";
    public int LifetimeMinutes { get; set; } = 120;

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SigningKey(),
        ValidateIssuerSigningKey = true,
        NameClaimType = "name",
        RoleClaimType = "role",
        ClockSkew = TimeSpan.FromSeconds(30),
    };
}

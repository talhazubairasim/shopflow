using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ordering.Api;
using ShopFlow.Contracts;
using ShopFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("ordering");

string? cs = builder.Configuration.GetConnectionString("Ordering");
builder.Services.AddDbContext<OrderingDbContext>(o =>
{
    if (string.IsNullOrEmpty(cs))
        o.UseInMemoryDatabase("ordering").ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
    else o.UseNpgsql(cs);
});
if (!string.IsNullOrEmpty(cs)) builder.Services.AddHealthChecks().AddNpgSql(cs, tags: new[] { "ready" });

builder.Services.AddMessaging(builder.Configuration, x =>
{
    x.AddSagaStateMachine<OrderStateMachine, OrderState>()
        .EntityFrameworkRepository(r =>
        {
            r.ExistingDbContext<OrderingDbContext>();
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
        });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.EnsureCreatedAsync();

app.MapDefaultEndpoints();

var orders = app.MapGroup("/api/orders").WithTags("Orders").RequireAuthorization();

orders.MapGet("/", async (HttpContext http, OrderingDbContext db) =>
{
    string buyer = http.User.UserId();
    var list = await db.Orders.AsNoTracking().Where(o => o.BuyerId == buyer).OrderByDescending(o => o.CreatedAt).ToListAsync();
    return list.Select(OrderDto.From);
});

orders.MapGet("/{id:guid}", async (Guid id, HttpContext http, OrderingDbContext db) =>
{
    var o = await db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.CorrelationId == id);
    if (o == null || (o.BuyerId != http.User.UserId() && !http.User.IsInRole("admin"))) return Results.NotFound();
    return Results.Ok(OrderDto.From(o));
});

orders.MapGet("/all", async (OrderingDbContext db, string? status) =>
{
    var q = db.Orders.AsNoTracking();
    if (!string.IsNullOrEmpty(status)) q = q.Where(o => o.CurrentState == status);
    var list = await q.OrderByDescending(o => o.CreatedAt).Take(200).ToListAsync();
    return list.Select(OrderDto.From);
}).RequireAuthorization(Extensions.AdminPolicy);

app.Run();

public record OrderDto(Guid Id, string Status, decimal Total, IReadOnlyList<OrderLine> Lines, ShippingAddress? Address,
    string CardLast4, string? TransactionId, string? FailureReason, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static OrderDto From(OrderState s) => new(s.CorrelationId, s.CurrentState, s.Total, s.Lines,
        JsonSerializer.Deserialize<ShippingAddress>(s.AddressJson), s.CardLast4, s.TransactionId, s.FailureReason, s.CreatedAt, s.UpdatedAt);
}

public partial class Program;

using Catalog.Api.Consumers;
using Catalog.Api.Features;
using Catalog.Api.Infrastructure;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("catalog");

string? cs = builder.Configuration.GetConnectionString("Catalog");
builder.Services.AddDbContext<CatalogDbContext>(o =>
{
    if (string.IsNullOrEmpty(cs)) o.UseInMemoryDatabase("catalog");
    else o.UseNpgsql(cs);
});
if (!string.IsNullOrEmpty(cs)) builder.Services.AddHealthChecks().AddNpgSql(cs, tags: new[] { "ready" });

builder.Services.AddMediatR(c =>
{
    c.RegisterServicesFromAssemblyContaining<Program>();
    c.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddScoped<StockService>();
builder.Services.AddMessaging(builder.Configuration, x =>
{
    x.AddConsumer<ReserveStockConsumer>();
    x.AddConsumer<ReleaseStockConsumer>();
    x.AddConsumer<OrderConfirmedConsumer>();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.EnsureCreatedAsync();
    await CatalogSeed.SeedAsync(db);
}

app.MapDefaultEndpoints();

var products = app.MapGroup("/api/catalog").WithTags("Catalog");

products.MapGet("/products", (string? search, string? category, int? page, int? pageSize, ISender mediator) =>
    mediator.Send(new GetProducts(search, category, page ?? 1, pageSize ?? 20)));

products.MapGet("/products/{id:guid}", async (Guid id, ISender mediator) =>
    await mediator.Send(new GetProduct(id)) is { } p ? Results.Ok(p) : Results.NotFound());

products.MapGet("/categories", (ISender mediator) => mediator.Send(new GetCategories()));

products.MapPost("/products", async (UpsertProduct cmd, ISender mediator) =>
{
    var p = await mediator.Send(cmd with { Id = null });
    return Results.Created($"/api/catalog/products/{p!.Id}", p);
}).RequireAuthorization(Extensions.AdminPolicy);

products.MapPut("/products/{id:guid}", async (Guid id, UpsertProduct cmd, ISender mediator) =>
    await mediator.Send(cmd with { Id = id }) is { } p ? Results.Ok(p) : Results.NotFound())
    .RequireAuthorization(Extensions.AdminPolicy);

products.MapDelete("/products/{id:guid}", async (Guid id, ISender mediator) =>
    await mediator.Send(new DeleteProduct(id)) ? Results.NoContent() : Results.NotFound())
    .RequireAuthorization(Extensions.AdminPolicy);

app.Run();

public partial class Program;

using Basket.Api;
using FluentValidation;
using MassTransit;
using ShopFlow.Contracts;
using ShopFlow.ServiceDefaults;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("basket");

string? redis = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrEmpty(redis))
{
    builder.Services.AddSingleton<IBasketRepository, InMemoryBasketRepository>();
}
else
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
    builder.Services.AddSingleton<IBasketRepository, RedisBasketRepository>();
    builder.Services.AddHealthChecks().AddRedis(redis, tags: new[] { "ready" });
}
builder.Services.AddHttpClient<ICatalogClient, CatalogClient>(c =>
    c.BaseAddress = new Uri(builder.Configuration["Services:Catalog"] ?? "http://localhost:5101"))
    .AddStandardResilienceHandler(); // retries, timeouts and a circuit breaker
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddMessaging(builder.Configuration, _ => { });

var app = builder.Build();
app.MapDefaultEndpoints();

var basket = app.MapGroup("/api/basket").WithTags("Basket").RequireAuthorization();

basket.MapGet("/", async (HttpContext http, IBasketRepository repo) =>
    await repo.GetAsync(http.User.UserId()) ?? new CustomerBasket(http.User.UserId(), new()));

basket.MapPost("/items", async (AddItemRequest req, HttpContext http, IBasketRepository repo, ICatalogClient catalog) =>
{
    if (req.Quantity is < 1 or > 99) return Results.ValidationProblem(new Dictionary<string, string[]> { ["quantity"] = new[] { "Quantity must be 1–99" } });
    var product = await catalog.GetProductAsync(req.ProductId);
    if (product == null) return Results.NotFound(new { error = "Product not found" });

    string buyer = http.User.UserId();
    var b = await repo.GetAsync(buyer) ?? new CustomerBasket(buyer, new());
    var existing = b.Items.FirstOrDefault(i => i.ProductId == req.ProductId);
    int qty = req.Quantity + (existing?.Quantity ?? 0);
    if (qty > product.AvailableStock)
        return Results.Conflict(new { error = $"Only {product.AvailableStock} of {product.Name} in stock" });
    b.Items.RemoveAll(i => i.ProductId == req.ProductId);
    b.Items.Add(new BasketItem(product.Id, product.Name, product.Price, qty));
    await repo.SaveAsync(b);
    return Results.Ok(b);
});

basket.MapPut("/items/{productId:guid}", async (Guid productId, AddItemRequest req, HttpContext http, IBasketRepository repo) =>
{
    var b = await repo.GetAsync(http.User.UserId());
    var item = b?.Items.FirstOrDefault(i => i.ProductId == productId);
    if (b == null || item == null) return Results.NotFound();
    b.Items.Remove(item);
    if (req.Quantity > 0) b.Items.Add(item with { Quantity = Math.Min(req.Quantity, 99) });
    await repo.SaveAsync(b);
    return Results.Ok(b);
});

basket.MapDelete("/items/{productId:guid}", async (Guid productId, HttpContext http, IBasketRepository repo) =>
{
    var b = await repo.GetAsync(http.User.UserId());
    if (b == null || b.Items.RemoveAll(i => i.ProductId == productId) == 0) return Results.NotFound();
    await repo.SaveAsync(b);
    return Results.Ok(b);
});

basket.MapDelete("/", async (HttpContext http, IBasketRepository repo) =>
{
    await repo.DeleteAsync(http.User.UserId());
    return Results.NoContent();
});

// Checkout: re-price every line from the catalog, publish BasketCheckedOut and empty the basket.
// The order is then driven asynchronously by the saga in the Ordering service.
basket.MapPost("/checkout", async (CheckoutRequest req, HttpContext http, IBasketRepository repo,
    ICatalogClient catalog, IValidator<CheckoutRequest> validator, IPublishEndpoint bus) =>
{
    var validation = await validator.ValidateAsync(req);
    if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

    string buyer = http.User.UserId();
    var b = await repo.GetAsync(buyer);
    if (b == null || b.Items.Count == 0) return Results.BadRequest(new { error = "Basket is empty" });

    var lines = new List<OrderLine>();
    foreach (var item in b.Items)
    {
        var product = await catalog.GetProductAsync(item.ProductId);
        if (product == null) return Results.Conflict(new { error = $"{item.ProductName} is no longer sold" });
        lines.Add(new OrderLine(product.Id, product.Name, product.Price, item.Quantity));
    }

    var orderId = Guid.NewGuid();
    await bus.Publish(new BasketCheckedOut(orderId, buyer, http.User.Email() ?? "", lines,
        new ShippingAddress(req.FullName, req.Street, req.City, req.Country, req.PostalCode),
        req.CardNumber, DateTime.UtcNow));
    await repo.DeleteAsync(buyer);
    return Results.Accepted($"/api/orders/{orderId}", new { orderId, total = lines.Sum(l => l.LineTotal) });
});

app.Run();

public partial class Program;

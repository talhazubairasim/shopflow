using System.Collections.Concurrent;
using System.Text.Json;
using FluentValidation;
using StackExchange.Redis;

namespace Basket.Api;

public record BasketItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);

public record CustomerBasket(string BuyerId, List<BasketItem> Items)
{
    public decimal Total => Items.Sum(i => i.UnitPrice * i.Quantity);
}

public record AddItemRequest(Guid ProductId, int Quantity);

public record CheckoutRequest(string FullName, string Street, string City, string Country, string PostalCode, string CardNumber);

public class CheckoutValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Street).NotEmpty();
        RuleFor(x => x.City).NotEmpty();
        RuleFor(x => x.Country).NotEmpty();
        RuleFor(x => x.PostalCode).NotEmpty();
        RuleFor(x => x.CardNumber).Matches(@"^\d{12,19}$").WithMessage("Card number must be 12–19 digits");
    }
}

public interface IBasketRepository
{
    Task<CustomerBasket?> GetAsync(string buyerId);
    Task SaveAsync(CustomerBasket basket);
    Task DeleteAsync(string buyerId);
}

/// <summary>Baskets live in Redis with a 30-day sliding expiry.</summary>
public class RedisBasketRepository(IConnectionMultiplexer redis) : IBasketRepository
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);
    private IDatabase Db => redis.GetDatabase();
    private static string Key(string buyerId) => $"basket:{buyerId}";

    public async Task<CustomerBasket?> GetAsync(string buyerId)
    {
        var value = await Db.StringGetAsync(Key(buyerId));
        if (value.IsNullOrEmpty) return null;
        await Db.KeyExpireAsync(Key(buyerId), Ttl);
        return JsonSerializer.Deserialize<CustomerBasket>(value!.ToString());
    }

    public Task SaveAsync(CustomerBasket basket) =>
        Db.StringSetAsync(Key(basket.BuyerId), JsonSerializer.Serialize(basket), Ttl);

    public Task DeleteAsync(string buyerId) => Db.KeyDeleteAsync(Key(buyerId));
}

/// <summary>Used when no Redis connection is configured (local runs and tests).</summary>
public class InMemoryBasketRepository : IBasketRepository
{
    private readonly ConcurrentDictionary<string, CustomerBasket> _store = new();
    public Task<CustomerBasket?> GetAsync(string buyerId) => Task.FromResult(_store.TryGetValue(buyerId, out var b) ? b : null);
    public Task SaveAsync(CustomerBasket basket) { _store[basket.BuyerId] = basket; return Task.CompletedTask; }
    public Task DeleteAsync(string buyerId) { _store.TryRemove(buyerId, out _); return Task.CompletedTask; }
}

public record CatalogProduct(Guid Id, string Name, decimal Price, int AvailableStock);

/// <summary>Reads current product data from the Catalog service, so clients cannot set their own prices.</summary>
public interface ICatalogClient
{
    Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct = default);
}

public class CatalogClient(HttpClient http) : ICatalogClient
{
    public async Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct = default)
    {
        var res = await http.GetAsync($"/api/catalog/products/{id}", ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<CatalogProduct>(cancellationToken: ct);
    }
}

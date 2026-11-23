using System.Net;
using System.Net.Http.Json;
using Basket.Api;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Contracts;
using ShopFlow.Testing;

namespace Basket.Tests;

public class FakeCatalog : ICatalogClient
{
    public static readonly CatalogProduct Keyboard = new(Guid.NewGuid(), "Keyboard", 80m, 5);
    public static readonly CatalogProduct Mouse = new(Guid.NewGuid(), "Mouse", 20m, 100);
    public decimal KeyboardPrice { get; set; } = 80m;

    public Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct = default) => Task.FromResult(
        id == Keyboard.Id ? Keyboard with { Price = KeyboardPrice } :
        id == Mouse.Id ? Mouse : (CatalogProduct?)null);
}

public class BasketApiTests : IClassFixture<BasketApiTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        public FakeCatalog Catalog { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("Messaging:Transport", "InMemory");
            builder.ConfigureTestServices(s =>
            {
                s.AddSingleton<ICatalogClient>(Catalog);
                s.AddMassTransitTestHarness();
            });
        }
    }

    private readonly Factory _f;
    public BasketApiTests(Factory f) => _f = f;

    private static readonly object Address = new
    {
        fullName = "Talha", street = "1 Main", city = "Islamabad", country = "PK", postalCode = "44000", cardNumber = "4242424242424242",
    };

    [Fact]
    public async Task Basket_requires_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/basket")).StatusCode);
    }

    [Fact]
    public async Task Add_update_remove_and_stock_checks()
    {
        var c = _f.CreateClient().As("user-a");
        var b = await (await c.PostAsJsonAsync("/api/basket/items", new { productId = FakeCatalog.Keyboard.Id, quantity = 2 })).Content.ReadFromJsonAsync<CustomerBasket>();
        Assert.Equal(160m, b!.Total);

        // more than in stock
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/basket/items", new { productId = FakeCatalog.Keyboard.Id, quantity = 4 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/api/basket/items", new { productId = Guid.NewGuid(), quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/basket/items", new { productId = FakeCatalog.Mouse.Id, quantity = 0 })).StatusCode);

        await c.PostAsJsonAsync("/api/basket/items", new { productId = FakeCatalog.Mouse.Id, quantity = 1 });
        b = await (await c.PutAsJsonAsync($"/api/basket/items/{FakeCatalog.Mouse.Id}", new { productId = FakeCatalog.Mouse.Id, quantity = 3 })).Content.ReadFromJsonAsync<CustomerBasket>();
        Assert.Equal(160m + 60m, b!.Total);

        b = await (await c.DeleteAsync($"/api/basket/items/{FakeCatalog.Mouse.Id}")).Content.ReadFromJsonAsync<CustomerBasket>();
        Assert.Single(b!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/basket/items/{FakeCatalog.Mouse.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/basket")).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<CustomerBasket>("/api/basket"))!.Items);
    }

    [Fact]
    public async Task Checkout_reprices_from_catalog_and_publishes_the_event()
    {
        var harness = _f.Services.GetRequiredService<ITestHarness>();
        var c = _f.CreateClient().As("user-b");
        await c.PostAsJsonAsync("/api/basket/items", new { productId = FakeCatalog.Keyboard.Id, quantity = 1 });

        _f.Catalog.KeyboardPrice = 75m; // price changed after the item was added
        var res = await c.PostAsJsonAsync("/api/basket/checkout", Address);
        _f.Catalog.KeyboardPrice = 80m;
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        Assert.True(await harness.Published.Any<BasketCheckedOut>(x =>
            x.Context.Message.BuyerId == "user-b" && x.Context.Message.Total == 75m && x.Context.Message.Address.City == "Islamabad"));
        Assert.Empty((await c.GetFromJsonAsync<CustomerBasket>("/api/basket"))!.Items);
    }

    [Fact]
    public async Task Checkout_validates_input_and_requires_items()
    {
        var c = _f.CreateClient().As("user-c");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/basket/checkout", Address)).StatusCode); // empty basket
        await c.PostAsJsonAsync("/api/basket/items", new { productId = FakeCatalog.Mouse.Id, quantity = 1 });
        var bad = new { fullName = "", street = "", city = "", country = "", postalCode = "", cardNumber = "12" };
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/basket/checkout", bad)).StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using Catalog.Api.Domain;
using Catalog.Api.Features;
using Catalog.Api.Infrastructure;
using MassTransit.Testing;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Contracts;
using ShopFlow.Testing;

namespace Catalog.Tests;

public class StockServiceTests
{
    private static CatalogDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static OrderLine Line(Product p, int qty) => new(p.Id, p.Name, p.Price, qty);

    [Fact]
    public async Task Reserves_and_releases_stock_idempotently()
    {
        using var db = NewDb();
        var p = new Product { Name = "Desk", Category = "Furniture", Price = 100, AvailableStock = 5 };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        var stock = new StockService(db);
        var order = Guid.NewGuid();

        Assert.Null(await stock.ReserveAsync(order, new[] { Line(p, 2), Line(p, 1) }));
        Assert.Equal(2, (await db.Products.FindAsync(p.Id))!.AvailableStock);
        Assert.Null(await stock.ReserveAsync(order, new[] { Line(p, 3) })); // duplicate message: no double reservation
        Assert.Equal(2, p.AvailableStock);

        await stock.ReleaseAsync(order);
        Assert.Equal(5, p.AvailableStock);
        await stock.ReleaseAsync(order); // releasing twice is harmless
        Assert.Equal(5, p.AvailableStock);
    }

    [Fact]
    public async Task Refuses_when_stock_is_short_or_product_missing()
    {
        using var db = NewDb();
        var p = new Product { Name = "Chair", Category = "Furniture", Price = 50, AvailableStock = 1 };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        var stock = new StockService(db);

        string? short_ = await stock.ReserveAsync(Guid.NewGuid(), new[] { Line(p, 2) });
        Assert.Contains("Only 1 of Chair", short_);
        string? missing = await stock.ReserveAsync(Guid.NewGuid(), new[] { new OrderLine(Guid.NewGuid(), "Ghost", 1, 1) });
        Assert.Contains("Ghost", missing);
    }

    [Fact]
    public async Task Commit_makes_the_sale_permanent()
    {
        using var db = NewDb();
        var p = new Product { Name = "Lamp", Category = "Home", Price = 20, AvailableStock = 3 };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        var stock = new StockService(db);
        var order = Guid.NewGuid();
        await stock.ReserveAsync(order, new[] { Line(p, 2) });
        await stock.CommitAsync(order);
        await stock.ReleaseAsync(order); // nothing left to release after commit
        Assert.Equal(1, p.AvailableStock);
    }

    [Fact]
    public void Product_rejects_non_positive_quantities()
    {
        var p = new Product { AvailableStock = 1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => p.TryReserve(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Release(-1));
    }
}

public class CatalogApiTests : IClassFixture<CatalogApiTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Catalog", "");
            builder.UseSetting("Messaging:Transport", "InMemory");
            builder.ConfigureServices(s => s.AddMassTransitTestHarness());
        }
    }

    private readonly Factory _f;
    public CatalogApiTests(Factory f) => _f = f;

    [Fact]
    public async Task Lists_searches_and_filters_seeded_products()
    {
        var client = _f.CreateClient();
        var page = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/catalog/products?pageSize=3");
        Assert.True(page!.Total >= 8);
        Assert.Equal(3, page.Items.Count);

        var books = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/catalog/products?category=Books");
        Assert.All(books!.Items, p => Assert.Equal("Books", p.Category));

        var search = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/catalog/products?search=keyboard");
        Assert.Single(search!.Items);

        var categories = await client.GetFromJsonAsync<string[]>("/api/catalog/categories");
        Assert.Contains("Electronics", categories!);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/catalog/products/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_can_change_products_and_input_is_validated()
    {
        var anon = _f.CreateClient();
        var body = new { name = "Webcam", description = "1080p", category = "Electronics", price = 59.0, imageUrl = (string?)null, availableStock = 4 };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/catalog/products", body)).StatusCode);

        var customer = _f.CreateClient().As("u1", "customer");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync("/api/catalog/products", body)).StatusCode);

        var admin = _f.CreateClient().As("admin1", "admin");
        var created = await admin.PostAsJsonAsync("/api/catalog/products", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var p = await created.Content.ReadFromJsonAsync<ProductDto>();

        var bad = await admin.PostAsJsonAsync("/api/catalog/products", body with { price = -1.0 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var updated = await admin.PutAsJsonAsync($"/api/catalog/products/{p!.Id}", body with { price = 49.0 });
        Assert.Equal(49m, (await updated.Content.ReadFromJsonAsync<ProductDto>())!.Price);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/catalog/products/{Guid.NewGuid()}", body)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/catalog/products/{p.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/catalog/products/{p.Id}")).StatusCode);
    }

    [Fact]
    public async Task Reserve_stock_messages_are_answered()
    {
        var harness = _f.Services.GetRequiredService<ITestHarness>();
        var client = _f.CreateClient();
        var keyboard = (await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/catalog/products?search=keyboard"))!.Items[0];
        var usb = (await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/catalog/products?search=hub"))!.Items[0];

        var ok = Guid.NewGuid();
        await harness.Bus.Publish(new ReserveStock(ok, new[] { new OrderLine(keyboard.Id, keyboard.Name, keyboard.Price, 1) }));
        Assert.True(await harness.Published.Any<StockReserved>(x => x.Context.Message.OrderId == ok));

        var fail = Guid.NewGuid();
        await harness.Bus.Publish(new ReserveStock(fail, new[] { new OrderLine(usb.Id, usb.Name, usb.Price, 1) }));
        Assert.True(await harness.Published.Any<StockReservationFailed>(x => x.Context.Message.OrderId == fail));

        await harness.Bus.Publish(new ReleaseStock(ok, Array.Empty<OrderLine>()));
        Assert.True(await harness.Consumed.Any<ReleaseStock>(x => x.Context.Message.OrderId == ok));
    }
}

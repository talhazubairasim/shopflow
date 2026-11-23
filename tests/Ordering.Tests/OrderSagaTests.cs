using System.Net;
using System.Net.Http.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Api;
using ShopFlow.Contracts;
using ShopFlow.Testing;

namespace Ordering.Tests;

public class OrderSagaTests : IAsyncLifetime
{
    private ServiceProvider _sp = null!;
    private ITestHarness _harness = null!;
    private ISagaStateMachineTestHarness<OrderStateMachine, OrderState> _saga = null!;
    private OrderStateMachine _machine = null!;

    public async Task InitializeAsync()
    {
        _sp = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddSagaStateMachine<OrderStateMachine, OrderState>().InMemoryRepository())
            .BuildServiceProvider(true);
        _harness = _sp.GetRequiredService<ITestHarness>();
        await _harness.Start();
        _saga = _harness.GetSagaStateMachineHarness<OrderStateMachine, OrderState>();
        _machine = _saga.StateMachine;
    }

    public async Task DisposeAsync() => await _sp.DisposeAsync();

    private static BasketCheckedOut Checkout(Guid id, string card = "4242424242424242") => new(id, "buyer-1", "b@x.dev",
        new[] { new OrderLine(Guid.NewGuid(), "Keyboard", 50m, 2) },
        new ShippingAddress("A B", "1 Main St", "Islamabad", "PK", "44000"), card, DateTime.UtcNow);

    [Fact]
    public async Task Happy_path_reserves_stock_charges_and_confirms()
    {
        var id = Guid.NewGuid();
        await _harness.Bus.Publish(Checkout(id));

        Assert.NotNull(await _saga.Exists(id, _machine.Submitted));
        Assert.True(await _harness.Published.Any<ReserveStock>(x => x.Context.Message.OrderId == id));
        var order = _saga.Sagas.Contains(id)!;
        Assert.Equal(100m, order.Total);
        Assert.Equal("4242", order.CardLast4);

        await _harness.Bus.Publish(new StockReserved(id));
        Assert.NotNull(await _saga.Exists(id, _machine.AwaitingPayment));
        Assert.True(await _harness.Published.Any<ProcessPayment>(x => x.Context.Message.OrderId == id && x.Context.Message.Amount == 100m));
        Assert.Null(_saga.Sagas.Contains(id)!.PendingCardNumber); // card number is not kept after the payment request

        await _harness.Bus.Publish(new PaymentSucceeded(id, "txn_1"));
        Assert.NotNull(await _saga.Exists(id, _machine.Confirmed));
        Assert.True(await _harness.Published.Any<OrderConfirmed>(x => x.Context.Message.OrderId == id && x.Context.Message.TransactionId == "txn_1"));
    }

    [Fact]
    public async Task Out_of_stock_cancels_without_charging()
    {
        var id = Guid.NewGuid();
        await _harness.Bus.Publish(Checkout(id));
        Assert.NotNull(await _saga.Exists(id, _machine.Submitted));

        await _harness.Bus.Publish(new StockReservationFailed(id, "Only 0 left"));
        Assert.NotNull(await _saga.Exists(id, _machine.Cancelled));
        Assert.True(await _harness.Published.Any<OrderCancelled>(x => x.Context.Message.OrderId == id && x.Context.Message.Reason == "Only 0 left"));
        Assert.False(await _harness.Published.Any<ProcessPayment>(x => x.Context.Message.OrderId == id));
    }

    [Fact]
    public async Task Failed_payment_releases_the_reserved_stock()
    {
        var id = Guid.NewGuid();
        await _harness.Bus.Publish(Checkout(id));
        Assert.NotNull(await _saga.Exists(id, _machine.Submitted));
        await _harness.Bus.Publish(new StockReserved(id));
        Assert.NotNull(await _saga.Exists(id, _machine.AwaitingPayment));

        await _harness.Bus.Publish(new PaymentFailed(id, "Card declined"));
        Assert.NotNull(await _saga.Exists(id, _machine.Cancelled));
        Assert.True(await _harness.Published.Any<ReleaseStock>(x => x.Context.Message.OrderId == id && x.Context.Message.Lines.Count == 1));
        Assert.True(await _harness.Published.Any<OrderCancelled>(x => x.Context.Message.OrderId == id));
        Assert.Equal("Card declined", _saga.Sagas.Contains(id)!.FailureReason);
    }

    [Fact]
    public async Task Late_duplicates_are_ignored_once_settled()
    {
        var id = Guid.NewGuid();
        await _harness.Bus.Publish(Checkout(id));
        await _harness.Bus.Publish(new StockReservationFailed(id, "none"));
        Assert.NotNull(await _saga.Exists(id, _machine.Cancelled));
        await _harness.Bus.Publish(new StockReserved(id));
        await _harness.Bus.Publish(new PaymentSucceeded(id, "late"));
        await Task.Delay(200);
        Assert.NotNull(await _saga.Exists(id, _machine.Cancelled));
        Assert.Null(_saga.Sagas.Contains(id)!.TransactionId);
    }
}

public class OrdersApiTests : IClassFixture<OrdersApiTests.Factory>
{
    public class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Ordering", "");
            // The real in-memory bus, not the test harness: the harness swaps saga
            // repositories for in-memory ones, and this test reads orders through EF.
            builder.UseSetting("Messaging:Transport", "InMemory");
        }
    }

    private readonly Factory _f;
    public OrdersApiTests(Factory f) => _f = f;

    [Fact]
    public async Task Buyers_see_only_their_orders_and_admins_see_all()
    {
        var bus = _f.Services.GetRequiredService<IBus>();
        var id = Guid.NewGuid();
        await bus.Publish(new BasketCheckedOut(id, "alice", "alice@x.dev",
            new[] { new OrderLine(Guid.NewGuid(), "Book", 10m, 1) },
            new ShippingAddress("Alice", "1 St", "City", "PK", "1"), "4242424242424242", DateTime.UtcNow));

        var alice = _f.CreateClient().As("alice");
        OrderDto? order = null;
        string last = "";
        for (int i = 0; i < 50 && order == null; i++)
        {
            var res = await alice.GetAsync($"/api/orders/{id}");
            if (res.IsSuccessStatusCode) order = await res.Content.ReadFromJsonAsync<OrderDto>();
            else { last = $"{(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}"; await Task.Delay(100); }
        }
        Assert.True(order != null, $"order never appeared; last response: {last}");
        Assert.Equal("Submitted", order!.Status);
        Assert.Equal("City", order.Address!.City);
        Assert.Single(await alice.GetFromJsonAsync<List<OrderDto>>("/api/orders") ?? new());

        var bob = _f.CreateClient().As("bob");
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/orders/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/orders/all")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/orders")).StatusCode);

        var admin = _f.CreateClient().As("root", "admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/orders/{id}")).StatusCode);
        var all = await admin.GetFromJsonAsync<List<OrderDto>>("/api/orders/all?status=Submitted");
        Assert.Contains(all!, o => o.Id == id);
    }
}

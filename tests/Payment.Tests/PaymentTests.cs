using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Payment.Api;
using ShopFlow.Contracts;

namespace Payment.Tests;

public class PaymentGatewayTests
{
    private static PaymentGateway Gateway() => new(new PaymentOptions { CardLimit = 1000m }, TimeProvider.System);

    [Theory]
    [InlineData("4242424242424242", true)]
    [InlineData("4000000000000002", true)]
    [InlineData("4242424242424241", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void Luhn(string card, bool valid) => Assert.Equal(valid, PaymentGateway.LuhnValid(card));

    [Fact]
    public void Approves_valid_cards_within_the_limit()
    {
        var r = Gateway().Charge(new ProcessPayment(Guid.NewGuid(), "b", 99m, "4242424242424242"));
        Assert.True(r.Succeeded);
        Assert.StartsWith("txn_", r.TransactionId);
        Assert.Equal("4242", r.CardLast4);
    }

    [Theory]
    [InlineData("4000000000000002", 10, "declined")]
    [InlineData("4242424242424241", 10, "invalid")]
    [InlineData("4242424242424242", 5000, "limit")]
    [InlineData("4242424242424242", 0, "positive")]
    public void Declines(string card, decimal amount, string reason)
    {
        var r = Gateway().Charge(new ProcessPayment(Guid.NewGuid(), "b", amount, card));
        Assert.False(r.Succeeded);
        Assert.Contains(reason, r.FailureReason);
    }

    [Fact]
    public void Charging_the_same_order_twice_returns_the_first_result()
    {
        var g = Gateway();
        var id = Guid.NewGuid();
        var first = g.Charge(new ProcessPayment(id, "b", 10m, "4242424242424242"));
        var second = g.Charge(new ProcessPayment(id, "b", 10m, "4000000000000002"));
        Assert.Equal(first, second);
        Assert.Single(g.All);
    }

    [Fact]
    public async Task Consumer_publishes_the_outcome()
    {
        await using var sp = new ServiceCollection()
            .AddSingleton(new PaymentOptions())
            .AddSingleton(TimeProvider.System)
            .AddSingleton<PaymentGateway>()
            .AddMassTransitTestHarness(x => x.AddConsumer<ProcessPaymentConsumer>())
            .BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();
        var ok = Guid.NewGuid();
        var bad = Guid.NewGuid();
        await harness.Bus.Publish(new ProcessPayment(ok, "b", 10m, "4242424242424242"));
        await harness.Bus.Publish(new ProcessPayment(bad, "b", 10m, "4000000000000002"));
        Assert.True(await harness.Published.Any<PaymentSucceeded>(x => x.Context.Message.OrderId == ok));
        Assert.True(await harness.Published.Any<PaymentFailed>(x => x.Context.Message.OrderId == bad));
    }
}

using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Contracts;

namespace Ordering.Api;

/// <summary>
/// The order, persisted as the saga's state. It is both the process manager's
/// memory and the read model the Orders API returns.
/// </summary>
public class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }          // = OrderId
    public string CurrentState { get; set; } = null!;
    public string BuyerId { get; set; } = null!;
    public string BuyerEmail { get; set; } = string.Empty;
    public string LinesJson { get; set; } = "[]";
    public string AddressJson { get; set; } = "{}";
    public decimal Total { get; set; }
    public string CardLast4 { get; set; } = string.Empty;
    /// <summary>
    /// Held only between checkout and the payment request, then cleared.
    /// A real system would hold a token from a payment provider instead of the card number.
    /// </summary>
    public string? PendingCardNumber { get; set; }
    public string? TransactionId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int Version { get; set; }

    public IReadOnlyList<OrderLine> Lines => JsonSerializer.Deserialize<List<OrderLine>>(LinesJson) ?? new();
}

public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State Submitted { get; private set; } = null!;
    public State AwaitingPayment { get; private set; } = null!;
    public State Confirmed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<BasketCheckedOut> CheckedOut { get; private set; } = null!;
    public Event<StockReserved> StockReserved { get; private set; } = null!;
    public Event<StockReservationFailed> StockFailed { get; private set; } = null!;
    public Event<PaymentSucceeded> PaymentSucceeded { get; private set; } = null!;
    public Event<PaymentFailed> PaymentFailed { get; private set; } = null!;

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => CheckedOut, e => e.CorrelateById(c => c.Message.OrderId));
        Event(() => StockReserved, e => e.CorrelateById(c => c.Message.OrderId));
        Event(() => StockFailed, e => e.CorrelateById(c => c.Message.OrderId));
        Event(() => PaymentSucceeded, e => e.CorrelateById(c => c.Message.OrderId));
        Event(() => PaymentFailed, e => e.CorrelateById(c => c.Message.OrderId));

        Initially(
            When(CheckedOut)
                .Then(ctx =>
                {
                    var m = ctx.Message;
                    var s = ctx.Saga;
                    s.BuyerId = m.BuyerId;
                    s.BuyerEmail = m.BuyerEmail;
                    s.LinesJson = JsonSerializer.Serialize(m.Lines);
                    s.AddressJson = JsonSerializer.Serialize(m.Address);
                    s.Total = m.Total;
                    s.PendingCardNumber = m.CardNumber;
                    s.CardLast4 = m.CardNumber.Length >= 4 ? m.CardNumber[^4..] : m.CardNumber;
                    s.CreatedAt = s.UpdatedAt = m.CheckedOutAt;
                })
                .Publish(ctx => new ReserveStock(ctx.Saga.CorrelationId, ctx.Message.Lines))
                .TransitionTo(Submitted));

        During(Submitted,
            When(StockReserved)
                .Publish(ctx => new ProcessPayment(ctx.Saga.CorrelationId, ctx.Saga.BuyerId, ctx.Saga.Total, ctx.Saga.PendingCardNumber ?? ""))
                .Then(ctx => { ctx.Saga.PendingCardNumber = null; ctx.Saga.UpdatedAt = DateTime.UtcNow; })
                .TransitionTo(AwaitingPayment),
            When(StockFailed)
                .Then(ctx => Fail(ctx.Saga, ctx.Message.Reason))
                .Publish(ctx => new OrderCancelled(ctx.Saga.CorrelationId, ctx.Saga.BuyerId, ctx.Saga.BuyerEmail, ctx.Message.Reason))
                .TransitionTo(Cancelled));

        During(AwaitingPayment,
            When(PaymentSucceeded)
                .Then(ctx => { ctx.Saga.TransactionId = ctx.Message.TransactionId; ctx.Saga.UpdatedAt = DateTime.UtcNow; })
                .Publish(ctx => new OrderConfirmed(ctx.Saga.CorrelationId, ctx.Saga.BuyerId, ctx.Saga.BuyerEmail, ctx.Saga.Total, ctx.Message.TransactionId))
                .TransitionTo(Confirmed),
            When(PaymentFailed)
                .Then(ctx => Fail(ctx.Saga, ctx.Message.Reason))
                // compensate: give the reserved stock back
                .Publish(ctx => new ReleaseStock(ctx.Saga.CorrelationId, ctx.Saga.Lines))
                .Publish(ctx => new OrderCancelled(ctx.Saga.CorrelationId, ctx.Saga.BuyerId, ctx.Saga.BuyerEmail, ctx.Message.Reason))
                .TransitionTo(Cancelled));

        // Duplicate deliveries after the order is settled are ignored.
        During(Confirmed, Cancelled,
            Ignore(CheckedOut), Ignore(StockReserved), Ignore(StockFailed), Ignore(PaymentSucceeded), Ignore(PaymentFailed));
    }

    private static void Fail(OrderState s, string reason)
    {
        s.FailureReason = reason;
        s.PendingCardNumber = null;
        s.UpdatedAt = DateTime.UtcNow;
    }
}

public class OrderStateMap : SagaClassMap<OrderState>
{
    protected override void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<OrderState> e,
        Microsoft.EntityFrameworkCore.ModelBuilder model)
    {
        e.ToTable("orders");
        e.Ignore(x => x.Lines);
        e.Property(x => x.CurrentState).HasMaxLength(40);
        e.Property(x => x.BuyerId).HasMaxLength(64);
        e.Property(x => x.Total).HasPrecision(18, 2);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasIndex(x => x.BuyerId);
    }
}

public class OrderingDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<OrderingDbContext> options)
    : MassTransit.EntityFrameworkCoreIntegration.SagaDbContext(options)
{
    protected override IEnumerable<MassTransit.EntityFrameworkCoreIntegration.ISagaClassMap> Configurations
    {
        get { yield return new OrderStateMap(); }
    }

    public Microsoft.EntityFrameworkCore.DbSet<OrderState> Orders => Set<OrderState>();
}

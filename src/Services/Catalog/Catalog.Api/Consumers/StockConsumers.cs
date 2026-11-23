using Catalog.Api.Features;
using MassTransit;
using ShopFlow.Contracts;

namespace Catalog.Api.Consumers;

public class ReserveStockConsumer(StockService stock, ILogger<ReserveStockConsumer> log) : IConsumer<ReserveStock>
{
    public async Task Consume(ConsumeContext<ReserveStock> ctx)
    {
        string? failure = await stock.ReserveAsync(ctx.Message.OrderId, ctx.Message.Lines, ctx.CancellationToken);
        if (failure == null)
        {
            log.LogInformation("Reserved stock for order {OrderId}", ctx.Message.OrderId);
            await ctx.Publish(new StockReserved(ctx.Message.OrderId));
        }
        else
        {
            log.LogWarning("Stock reservation failed for order {OrderId}: {Reason}", ctx.Message.OrderId, failure);
            await ctx.Publish(new StockReservationFailed(ctx.Message.OrderId, failure));
        }
    }
}

public class ReleaseStockConsumer(StockService stock) : IConsumer<ReleaseStock>
{
    public Task Consume(ConsumeContext<ReleaseStock> ctx) => stock.ReleaseAsync(ctx.Message.OrderId, ctx.CancellationToken);
}

public class OrderConfirmedConsumer(StockService stock) : IConsumer<OrderConfirmed>
{
    public Task Consume(ConsumeContext<OrderConfirmed> ctx) => stock.CommitAsync(ctx.Message.OrderId, ctx.CancellationToken);
}

using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Contracts;

namespace Catalog.Api.Features;

/// <summary>Reserves and releases stock for orders. Both operations are idempotent per order.</summary>
public class StockService(CatalogDbContext db)
{
    /// <returns>null on success, otherwise the reason the reservation failed.</returns>
    public async Task<string?> ReserveAsync(Guid orderId, IReadOnlyList<OrderLine> lines, CancellationToken ct = default)
    {
        if (await db.Reservations.AnyAsync(r => r.OrderId == orderId, ct)) return null; // already reserved

        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var line in lines.GroupBy(l => l.ProductId))
        {
            int qty = line.Sum(l => l.Quantity);
            if (!products.TryGetValue(line.Key, out var product))
                return $"Product {line.First().ProductName} no longer exists";
            if (!product.TryReserve(qty))
                return $"Only {product.AvailableStock} of {product.Name} left in stock";
            db.Reservations.Add(new StockReservation { OrderId = orderId, ProductId = product.Id, Quantity = qty });
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return "Stock changed while reserving; please try again";
        }
        return null;
    }

    public async Task ReleaseAsync(Guid orderId, CancellationToken ct = default)
    {
        var reservations = await db.Reservations.Where(r => r.OrderId == orderId).ToListAsync(ct);
        if (reservations.Count == 0) return; // nothing held (or already released)
        var ids = reservations.Select(r => r.ProductId).ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var r in reservations)
        {
            if (products.TryGetValue(r.ProductId, out var p)) p.Release(r.Quantity);
        }
        db.Reservations.RemoveRange(reservations);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Once an order is confirmed its reservations become permanent sales.</summary>
    public async Task CommitAsync(Guid orderId, CancellationToken ct = default)
    {
        var reservations = await db.Reservations.Where(r => r.OrderId == orderId).ToListAsync(ct);
        db.Reservations.RemoveRange(reservations);
        await db.SaveChangesAsync(ct);
    }
}

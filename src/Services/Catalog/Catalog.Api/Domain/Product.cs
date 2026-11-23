namespace Catalog.Api.Domain;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Units on the shelf that are not reserved by a pending order.</summary>
    public int AvailableStock { get; set; }

    /// <summary>Incremented on every change; used for optimistic concurrency.</summary>
    public int Version { get; set; }

    public bool TryReserve(int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (AvailableStock < quantity) return false;
        AvailableStock -= quantity;
        Version++;
        return true;
    }

    public void Release(int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        AvailableStock += quantity;
        Version++;
    }
}

/// <summary>Records which order holds which stock, so reserve/release are idempotent.</summary>
public class StockReservation
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime ReservedAt { get; set; } = DateTime.UtcNow;
}

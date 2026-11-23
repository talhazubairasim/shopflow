using Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Infrastructure;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Category).HasMaxLength(100);
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.Property(p => p.Version).IsConcurrencyToken();
            e.HasIndex(p => p.Category);
        });
        b.Entity<StockReservation>(e => e.HasKey(r => new { r.OrderId, r.ProductId }));
    }
}

public static class CatalogSeed
{
    public static async Task SeedAsync(CatalogDbContext db)
    {
        if (await db.Products.AnyAsync()) return;
        db.Products.AddRange(
            new Product { Name = "Mechanical Keyboard", Category = "Electronics", Price = 89.99m, AvailableStock = 50, Description = "Hot-swappable switches, RGB backlight." },
            new Product { Name = "Noise-Cancelling Headphones", Category = "Electronics", Price = 199m, AvailableStock = 25, Description = "40 hours battery, USB-C." },
            new Product { Name = "4K Monitor 27\"", Category = "Electronics", Price = 329.5m, AvailableStock = 10, Description = "IPS panel, 60 Hz, HDR400." },
            new Product { Name = "Ergonomic Chair", Category = "Furniture", Price = 249m, AvailableStock = 8, Description = "Adjustable lumbar support." },
            new Product { Name = "Standing Desk", Category = "Furniture", Price = 399m, AvailableStock = 5, Description = "Dual motor, memory presets." },
            new Product { Name = "Clean Architecture (book)", Category = "Books", Price = 34.99m, AvailableStock = 100, Description = "Robert C. Martin." },
            new Product { Name = "Designing Data-Intensive Applications", Category = "Books", Price = 44.5m, AvailableStock = 60, Description = "Martin Kleppmann." },
            new Product { Name = "USB-C Hub", Category = "Electronics", Price = 39m, AvailableStock = 0, Description = "7-in-1, out of stock to demo failed checkouts." });
        await db.SaveChangesAsync();
    }
}

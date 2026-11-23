using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Features;

public record ProductDto(Guid Id, string Name, string Description, string Category, decimal Price, string? ImageUrl, int AvailableStock);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

static class Mapping
{
    public static ProductDto ToDto(this Product p) => new(p.Id, p.Name, p.Description, p.Category, p.Price, p.ImageUrl, p.AvailableStock);
}

// ---------- queries ----------

public record GetProducts(string? Search, string? Category, int Page = 1, int PageSize = 20) : IRequest<PagedResult<ProductDto>>;

public class GetProductsHandler(CatalogDbContext db) : IRequestHandler<GetProducts, PagedResult<ProductDto>>
{
    public async Task<PagedResult<ProductDto>> Handle(GetProducts q, CancellationToken ct)
    {
        int page = Math.Max(1, q.Page);
        int size = Math.Clamp(q.PageSize, 1, 100);
        IQueryable<Product> query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Category)) query = query.Where(p => p.Category == q.Category);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            string s = q.Search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(s) || p.Description.ToLower().Contains(s));
        }
        int total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.Name).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<ProductDto>(items.Select(p => p.ToDto()).ToList(), page, size, total);
    }
}

public record GetProduct(Guid Id) : IRequest<ProductDto?>;

public class GetProductHandler(CatalogDbContext db) : IRequestHandler<GetProduct, ProductDto?>
{
    public async Task<ProductDto?> Handle(GetProduct q, CancellationToken ct) =>
        (await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == q.Id, ct))?.ToDto();
}

public record GetCategories : IRequest<IReadOnlyList<string>>;

public class GetCategoriesHandler(CatalogDbContext db) : IRequestHandler<GetCategories, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(GetCategories q, CancellationToken ct) =>
        await db.Products.Select(p => p.Category).Distinct().OrderBy(c => c).ToListAsync(ct);
}

// ---------- commands ----------

public record UpsertProduct(Guid? Id, string Name, string Description, string Category, decimal Price, string? ImageUrl, int AvailableStock) : IRequest<ProductDto?>;

public class UpsertProductValidator : AbstractValidator<UpsertProduct>
{
    public UpsertProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.AvailableStock).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ImageUrl).Must(u => u == null || Uri.TryCreate(u, UriKind.Absolute, out _)).WithMessage("ImageUrl must be an absolute URL");
    }
}

public class UpsertProductHandler(CatalogDbContext db) : IRequestHandler<UpsertProduct, ProductDto?>
{
    public async Task<ProductDto?> Handle(UpsertProduct c, CancellationToken ct)
    {
        Product? p;
        if (c.Id is Guid id)
        {
            p = await db.Products.FindAsync(new object[] { id }, ct);
            if (p == null) return null;
            p.Version++;
        }
        else
        {
            p = new Product();
            db.Products.Add(p);
        }
        p.Name = c.Name.Trim();
        p.Description = c.Description.Trim();
        p.Category = c.Category.Trim();
        p.Price = c.Price;
        p.ImageUrl = c.ImageUrl;
        p.AvailableStock = c.AvailableStock;
        await db.SaveChangesAsync(ct);
        return p.ToDto();
    }
}

public record DeleteProduct(Guid Id) : IRequest<bool>;

public class DeleteProductHandler(CatalogDbContext db) : IRequestHandler<DeleteProduct, bool>
{
    public async Task<bool> Handle(DeleteProduct c, CancellationToken ct)
    {
        var p = await db.Products.FindAsync(new object[] { c.Id }, ct);
        if (p == null) return false;
        db.Products.Remove(p);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>Runs FluentValidation validators before every MediatR handler.</summary>
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(request, ct))))
            .SelectMany(r => r.Errors).Where(e => e != null).ToList();
        if (failures.Count > 0) throw new ValidationException(failures);
        return await next();
    }
}

using System.Data;
using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Data;
using BusinessPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Services;

public sealed class ProductService(BusinessPilotDbContext db)
{
    public async Task<object> ListAsync(Guid organizationId, string? search, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking().Where(x => x.OrganizationId == organizationId && !x.IsArchived);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Sku.Contains(search) || x.Name.Contains(search));
        var items = await query.OrderBy(x => x.Name).Take(500).Select(x => new {
            x.Id, x.Sku, x.Name, x.Description, x.CostPrice, x.SellingPrice, x.TaxRate, x.StockQuantity,
            x.LowStockThreshold, IsLowStock = x.StockQuantity <= x.LowStockThreshold, x.CreatedAtUtc, x.UpdatedAtUtc
        }).ToListAsync(ct);
        var lowStockCount = await db.Products.CountAsync(x => x.OrganizationId == organizationId && !x.IsArchived && x.StockQuantity <= x.LowStockThreshold, ct);
        return new { items, lowStockCount };
    }

    public async Task<ServiceResult<object>> CreateAsync(Guid organizationId, ProductRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var sku = NormalizeSku(request.Sku);
        if (await db.Products.AnyAsync(x => x.OrganizationId == organizationId && x.Sku == sku, ct))
            return ServiceResult<object>.Duplicate("That SKU is already used in this organization.");
        var product = new Product { OrganizationId = organizationId, Sku = sku };
        Apply(product, request); db.Products.Add(product);
        if (product.StockQuantity > 0)
            db.InventoryMovements.Add(new InventoryMovement { Product = product, QuantityDelta = product.StockQuantity, Reason = "Opening stock" });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return ServiceResult<object>.Success(ToDto(product));
    }

    public async Task<ServiceResult<object>> UpdateAsync(Guid organizationId, Guid id, ProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id && !x.IsArchived, ct);
        if (product is null) return ServiceResult<object>.Missing();
        var sku = NormalizeSku(request.Sku);
        if (await db.Products.AnyAsync(x => x.OrganizationId == organizationId && x.Id != id && x.Sku == sku, ct))
            return ServiceResult<object>.Duplicate("That SKU is already used in this organization.");
        product.Sku = sku; Apply(product, request, preserveStock: true); product.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return ServiceResult<object>.Success(ToDto(product));
    }

    public async Task<ServiceResult<object>> AdjustStockAsync(Guid organizationId, Guid id, StockAdjustmentRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var product = await db.Products.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id && !x.IsArchived, ct);
        if (product is null) return ServiceResult<object>.Missing();
        var delta = request.Direction == "Increase" ? request.Quantity : -request.Quantity;
        if (product.StockQuantity + delta < 0) return ServiceResult<object>.Failure("Stock cannot be reduced below zero.");
        product.StockQuantity += delta; product.UpdatedAtUtc = DateTime.UtcNow;
        db.InventoryMovements.Add(new InventoryMovement { ProductId = product.Id, QuantityDelta = delta, Reason = request.Reason.Trim() });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return ServiceResult<object>.Success(ToDto(product));
    }

    public async Task<object?> MovementsAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var exists = await db.Products.AnyAsync(x => x.OrganizationId == organizationId && x.Id == id, ct);
        if (!exists) return null;
        return await db.InventoryMovements.AsNoTracking().Where(x => x.ProductId == id)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100)
            .Select(x => new { x.Id, x.QuantityDelta, x.Reason, x.CreatedAtUtc }).ToListAsync(ct);
    }

    public async Task<bool> ArchiveAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id && !x.IsArchived, ct);
        if (product is null) return false;
        product.IsArchived = true; product.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); return true;
    }

    private static object ToDto(Product x) => new { x.Id, x.Sku, x.Name, x.Description, x.CostPrice, x.SellingPrice, x.TaxRate,
        x.StockQuantity, x.LowStockThreshold, IsLowStock = x.StockQuantity <= x.LowStockThreshold, x.CreatedAtUtc, x.UpdatedAtUtc };
    private static void Apply(Product p, ProductRequest r, bool preserveStock = false)
    {
        p.Name = r.Name.Trim(); p.Description = Clean(r.Description); p.CostPrice = decimal.Round(r.CostPrice, 2);
        p.SellingPrice = decimal.Round(r.SellingPrice, 2); p.TaxRate = decimal.Round(r.TaxRate, 2);
        p.LowStockThreshold = r.LowStockThreshold; if (!preserveStock) p.StockQuantity = r.StockQuantity;
    }
    private static string NormalizeSku(string value) => value.Trim().ToUpperInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

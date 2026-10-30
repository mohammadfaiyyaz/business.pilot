using System.Data;
using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Data;
using BusinessPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Services;

public sealed record CreatedInvoice(Guid Id, object Body);

public sealed class InvoiceService(BusinessPilotDbContext db, BillingService billing)
{
    public async Task<IReadOnlyList<object>> ListAsync(Guid orgId, string? status, string? search, CancellationToken ct)
    {
        IQueryable<Invoice> query = db.Invoices.AsNoTracking().Include(x => x.Items).Include(x => x.Payments).Where(x => x.OrganizationId == orgId);
        if (string.Equals(status, "Overdue", StringComparison.OrdinalIgnoreCase))
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            query = query.Where(x => x.Status != "Cancelled" && x.Status != "Paid" && x.DueDate < today &&
                (x.Payments.Sum(p => (decimal?)p.Amount) ?? 0m) < x.Total);
        }
        else if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Number.Contains(search) || x.CustomerNameSnapshot.Contains(search));
        var data = await query.OrderByDescending(x => x.IssueDate).Take(200).ToListAsync(ct);
        return data.Select(ToDto).ToArray();
    }
    public async Task<object?> GetAsync(Guid orgId, Guid id, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(x => x.Items).Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId, ct);
        return invoice is null ? null : ToDto(invoice);
    }
    public async Task<ServiceResult<CreatedInvoice>> CreateAsync(Guid orgId, CreateInvoiceRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var limitError = await billing.CheckInvoiceLimitAsync(orgId, ct);
        if (limitError is not null) return ServiceResult<CreatedInvoice>.Failure(limitError);
        if (request.DueDate < request.IssueDate) return ServiceResult<CreatedInvoice>.Failure("Due date must be on or after the issue date.");
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == request.CustomerId && x.OrganizationId == orgId && !x.IsArchived, ct);
        if (customer is null) return ServiceResult<CreatedInvoice>.Failure("Select an active customer in your organization.");
        var organization = await db.Organizations.FindAsync([orgId], ct);
        if (organization is null) return ServiceResult<CreatedInvoice>.Failure("Organization was not found.");
        var productIds = request.Items.Where(x => x.ProductId.HasValue).Select(x => x.ProductId!.Value).Distinct().ToArray();
        var catalogProducts = await db.Products.Where(x => productIds.Contains(x.Id) && x.OrganizationId == orgId && !x.IsArchived)
            .ToDictionaryAsync(x => x.Id, ct);
        if (catalogProducts.Count != productIds.Length) return ServiceResult<CreatedInvoice>.Failure("A selected catalog product is unavailable in this organization.");
        foreach (var line in request.Items.Where(x => x.ProductId.HasValue))
            if (catalogProducts[line.ProductId!.Value].StockQuantity < request.Items.Where(x => x.ProductId == line.ProductId).Sum(x => x.Quantity))
                return ServiceResult<CreatedInvoice>.Failure($"There is not enough stock for {catalogProducts[line.ProductId.Value].Name}.");
        if (request.Items.Any(x => !x.ProductId.HasValue && string.IsNullOrWhiteSpace(x.Description)))
            return ServiceResult<CreatedInvoice>.Failure("Custom invoice lines need a description.");
        var year = DateTime.UtcNow.Year;
        var count = await db.Invoices.CountAsync(x => x.OrganizationId == orgId && x.CreatedAtUtc.Year == year, ct);
        var items = request.Items.Select(item => {
            var product = item.ProductId.HasValue ? catalogProducts[item.ProductId.Value] : null;
            var description = product?.Name ?? item.Description!.Trim();
            var unitPrice = product?.SellingPrice ?? item.UnitPrice;
            var taxRate = product?.TaxRate ?? item.TaxRate;
            var baseAmount = decimal.Round(item.Quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
            var tax = decimal.Round(baseAmount * taxRate / 100m, 2, MidpointRounding.AwayFromZero);
            return new InvoiceItem { ProductId = product?.Id, ProductSkuSnapshot = product?.Sku, Description = description,
                Quantity = item.Quantity, UnitPrice = unitPrice, TaxRate = taxRate, LineTotal = baseAmount + tax };
        }).ToList();
        var subtotal = items.Sum(x => decimal.Round(x.Quantity * x.UnitPrice, 2, MidpointRounding.AwayFromZero));
        var taxTotal = items.Sum(x => x.LineTotal - decimal.Round(x.Quantity * x.UnitPrice, 2, MidpointRounding.AwayFromZero));
        var invoice = new Invoice { OrganizationId = orgId, CustomerId = customer.Id, Number = $"{organization.InvoicePrefix}-{year}-{count + 1:0000}",
            CustomerNameSnapshot = customer.Name, CustomerAddressSnapshot = customer.Address, CustomerGstinSnapshot = customer.Gstin,
            IssueDate = request.IssueDate, DueDate = request.DueDate, Status = "Sent", Subtotal = subtotal, TaxTotal = taxTotal,
            Total = subtotal + taxTotal, Notes = request.Notes?.Trim(), Items = items };
        foreach (var group in request.Items.Where(x => x.ProductId.HasValue).GroupBy(x => x.ProductId!.Value))
        {
            var product = catalogProducts[group.Key]; var quantity = group.Sum(x => x.Quantity);
            product.StockQuantity -= quantity; product.UpdatedAtUtc = DateTime.UtcNow;
            db.InventoryMovements.Add(new InventoryMovement { ProductId = product.Id, InvoiceId = invoice.Id,
                QuantityDelta = -quantity, Reason = $"Invoice {invoice.Number} issued" });
        }
        db.Invoices.Add(invoice); await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ServiceResult<CreatedInvoice>.Success(new CreatedInvoice(invoice.Id, ToDto(invoice)));
    }

    public async Task<ServiceResult<object>> CancelAsync(Guid orgId, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var invoice = await db.Invoices.Include(x => x.Payments).Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId, ct);
        if (invoice is null) return ServiceResult<object>.Missing();
        if (invoice.Status == "Cancelled") return ServiceResult<object>.Success(ToDto(invoice));
        if (invoice.Payments.Count > 0 || invoice.Status == "Paid")
            return ServiceResult<object>.Failure("An invoice with recorded payments cannot be cancelled. Resolve or refund payments first.");
        invoice.Status = "Cancelled";
        var issuedMovements = await db.InventoryMovements.Where(x => x.InvoiceId == id && x.QuantityDelta < 0).ToListAsync(ct);
        foreach (var movement in issuedMovements)
        {
            var product = await db.Products.FirstOrDefaultAsync(x => x.Id == movement.ProductId && x.OrganizationId == orgId, ct);
            if (product is null) continue;
            var restored = -movement.QuantityDelta; product.StockQuantity += restored; product.UpdatedAtUtc = DateTime.UtcNow;
            db.InventoryMovements.Add(new InventoryMovement { ProductId = product.Id, InvoiceId = id,
                QuantityDelta = restored, Reason = $"Invoice {invoice.Number} cancelled; stock restored" });
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return ServiceResult<object>.Success(ToDto(invoice));
    }
    public async Task<ServiceResult<object>> RecordPaymentAsync(Guid orgId, Guid id, RecordPaymentRequest request, CancellationToken ct)
    {
        var invoice = await db.Invoices.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId, ct);
        if (invoice is null) return ServiceResult<object>.Missing();
        if (invoice.Status == "Cancelled") return ServiceResult<object>.Failure("Cancelled invoices cannot receive payments.");
        var balance = invoice.Total - invoice.Payments.Sum(x => x.Amount);
        if (request.Amount > balance) return ServiceResult<object>.Failure("Payment cannot exceed the outstanding balance.");
        invoice.Payments.Add(new Payment { Amount = request.Amount, PaidOn = request.PaidOn,
            Method = string.IsNullOrWhiteSpace(request.Method) ? "Bank transfer" : request.Method.Trim(), Reference = request.Reference?.Trim() });
        invoice.Status = request.Amount == balance ? "Paid" : "Partially Paid";
        await db.SaveChangesAsync(ct); return ServiceResult<object>.Success(ToDto(invoice));
    }
    private static object ToDto(Invoice x) => new { x.Id, x.Number, x.CustomerId, CustomerName = x.CustomerNameSnapshot,
        x.CustomerAddressSnapshot, x.CustomerGstinSnapshot, x.IssueDate, x.DueDate,
        Status = x.Status != "Paid" && x.Status != "Cancelled" && x.DueDate < DateOnly.FromDateTime(DateTime.UtcNow) ? "Overdue" : x.Status,
        x.Subtotal, x.TaxTotal, x.Total, AmountPaid = x.Payments.Sum(p => p.Amount),
        BalanceDue = Math.Max(0, x.Total - x.Payments.Sum(p => p.Amount)), x.Notes,
        Items = x.Items.Select(i => new { i.Id, i.ProductSkuSnapshot, i.Description, i.Quantity, i.UnitPrice, i.TaxRate, i.LineTotal }),
        Payments = x.Payments.Select(p => new { p.Id, p.Amount, p.PaidOn, p.Method, p.Reference }) };
}

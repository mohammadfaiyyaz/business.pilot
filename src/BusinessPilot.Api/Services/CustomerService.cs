using System.Data;
using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Data;
using BusinessPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Services;

public sealed class CustomerService(BusinessPilotDbContext db, BillingService billing)
{
    public async Task<object> ListAsync(Guid organizationId, string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Customers.AsNoTracking().Where(x => x.OrganizationId == organizationId && !x.IsArchived);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Name.Contains(search) || (x.Email != null && x.Email.Contains(search)));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new { items, total, page, pageSize };
    }

    public async Task<ServiceResult<Customer>> CreateAsync(Guid organizationId, CustomerRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var limitError = await billing.CheckCustomerLimitAsync(organizationId, ct);
        if (limitError is not null) return ServiceResult<Customer>.Failure(limitError);
        var customer = MakeCustomer(organizationId, request);
        db.Customers.Add(customer); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return ServiceResult<Customer>.Success(customer);
    }

    public async Task<object?> GetAsync(Guid organizationId, Guid id, CancellationToken ct) => await db.Customers.AsNoTracking()
        .Where(x => x.Id == id && x.OrganizationId == organizationId && !x.IsArchived)
        .Select(x => new { x.Id, x.Name, x.Email, x.Phone, x.Address, x.Gstin, x.Notes, x.CreatedAtUtc,
            Invoices = db.Invoices.Where(i => i.CustomerId == x.Id && i.OrganizationId == organizationId)
                .Select(i => new { i.Id, i.Number, i.IssueDate, i.DueDate, i.Status, i.Total, AmountPaid = i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m }).ToList() })
        .FirstOrDefaultAsync(ct);

    public async Task<bool> UpdateAsync(Guid organizationId, Guid id, CustomerRequest request, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId && !x.IsArchived, ct);
        if (customer is null) return false;
        Apply(customer, request); await db.SaveChangesAsync(ct); return true;
    }

    public async Task<bool> ArchiveAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId && !x.IsArchived, ct);
        if (customer is null) return false;
        customer.IsArchived = true; await db.SaveChangesAsync(ct); return true;
    }

    private static Customer MakeCustomer(Guid orgId, CustomerRequest request)
    {
        var customer = new Customer { OrganizationId = orgId }; Apply(customer, request); return customer;
    }
    private static void Apply(Customer c, CustomerRequest r)
    {
        c.Name = r.Name.Trim(); c.Email = Clean(r.Email); c.Phone = Clean(r.Phone); c.Address = Clean(r.Address);
        c.Gstin = Clean(r.Gstin)?.ToUpperInvariant(); c.Notes = Clean(r.Notes);
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

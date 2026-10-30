using BusinessPilot.Api.Contracts;
using BusinessPilot.Api.Data;
using BusinessPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Services;

public sealed class ExpenseService(BusinessPilotDbContext db)
{
    public async Task<object> ListAsync(Guid organizationId, string? search, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var query = db.Expenses.AsNoTracking().Where(x => x.OrganizationId == organizationId && !x.IsArchived);
        if (from.HasValue) query = query.Where(x => x.ExpenseDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.ExpenseDate <= to.Value);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Category.Contains(search) || (x.Vendor != null && x.Vendor.Contains(search)) || x.Description.Contains(search));
        var total = await query.SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var items = await query.OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync(ct);
        return new { items, total };
    }

    public async Task<Expense> CreateAsync(Guid organizationId, ExpenseRequest request, CancellationToken ct)
    {
        var expense = new Expense { OrganizationId = organizationId };
        Apply(expense, request); db.Expenses.Add(expense); await db.SaveChangesAsync(ct); return expense;
    }

    public async Task<Expense?> UpdateAsync(Guid organizationId, Guid id, ExpenseRequest request, CancellationToken ct)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id && !x.IsArchived, ct);
        if (expense is null) return null;
        Apply(expense, request); expense.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); return expense;
    }

    public async Task<bool> ArchiveAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id && !x.IsArchived, ct);
        if (expense is null) return false;
        expense.IsArchived = true; expense.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); return true;
    }

    private static void Apply(Expense expense, ExpenseRequest request)
    {
        expense.Category = request.Category.Trim(); expense.Vendor = Clean(request.Vendor);
        expense.Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        expense.ExpenseDate = request.ExpenseDate; expense.Description = request.Description.Trim();
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

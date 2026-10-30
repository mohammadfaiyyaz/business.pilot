using System.Globalization;
using BusinessPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Services;

public sealed class DashboardService(BusinessPilotDbContext db)
{
    public async Task<object> GetAsync(Guid organizationId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var periodDays = to.DayNumber - from.DayNumber + 1;
        var previousTo = from.AddDays(-1);
        var previousFrom = previousTo.AddDays(-periodDays + 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var invoices = db.Invoices.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.Status != "Cancelled");
        var currentInvoices = invoices.Where(x => x.IssueDate >= from && x.IssueDate <= to);
        var previousInvoices = invoices.Where(x => x.IssueDate >= previousFrom && x.IssueDate <= previousTo);
        var payments = db.Payments.AsNoTracking().Where(x => x.Invoice.OrganizationId == organizationId);
        var expenses = db.Expenses.AsNoTracking().Where(x => x.OrganizationId == organizationId && !x.IsArchived);

        var currentSales = await currentInvoices.SumAsync(x => (decimal?)x.Total, ct) ?? 0m;
        var previousSales = await previousInvoices.SumAsync(x => (decimal?)x.Total, ct) ?? 0m;
        var currentPayments = await payments.Where(x => x.PaidOn >= from && x.PaidOn <= to).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var previousPayments = await payments.Where(x => x.PaidOn >= previousFrom && x.PaidOn <= previousTo).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var currentExpenses = await expenses.Where(x => x.ExpenseDate >= from && x.ExpenseDate <= to).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var previousExpenses = await expenses.Where(x => x.ExpenseDate >= previousFrom && x.ExpenseDate <= previousTo).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

        var openBalances = await invoices.Where(x => x.Status != "Paid")
            .Select(x => new { x.Total, x.DueDate, Paid = x.Payments.Sum(p => (decimal?)p.Amount) ?? 0m }).ToListAsync(ct);
        var monthlyStart = new DateOnly(from.Year, from.Month, 1);
        var monthlyEndExclusive = new DateOnly(to.Year, to.Month, 1).AddMonths(1);
        var salesRows = await invoices.Where(x => x.IssueDate >= monthlyStart && x.IssueDate < monthlyEndExclusive)
            .Select(x => new { x.IssueDate, x.Total }).ToListAsync(ct);
        var paymentRows = await payments.Where(x => x.PaidOn >= monthlyStart && x.PaidOn < monthlyEndExclusive)
            .Select(x => new { x.PaidOn, x.Amount }).ToListAsync(ct);
        var expenseRows = await expenses.Where(x => x.ExpenseDate >= monthlyStart && x.ExpenseDate < monthlyEndExclusive)
            .Select(x => new { x.ExpenseDate, x.Amount }).ToListAsync(ct);
        var monthCount = (monthlyEndExclusive.Year - monthlyStart.Year) * 12 + monthlyEndExclusive.Month - monthlyStart.Month;
        var monthlyActivity = Enumerable.Range(0, monthCount).Select(offset => {
            var month = monthlyStart.AddMonths(offset);
            return new {
                month = $"{month.Year:0000}-{month.Month:00}",
                label = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month.Month),
                sales = salesRows.Where(x => x.IssueDate.Year == month.Year && x.IssueDate.Month == month.Month).Sum(x => x.Total),
                received = paymentRows.Where(x => x.PaidOn.Year == month.Year && x.PaidOn.Month == month.Month).Sum(x => x.Amount),
                expenses = expenseRows.Where(x => x.ExpenseDate.Year == month.Year && x.ExpenseDate.Month == month.Month).Sum(x => x.Amount)
            };
        }).ToArray();

        var periodInvoices = await currentInvoices.Select(x => new {
            x.Status, x.DueDate, x.Total,
            Paid = x.Payments.Sum(p => (decimal?)p.Amount) ?? 0m
        }).ToListAsync(ct);
        var paidCount = periodInvoices.Count(x => x.Status == "Paid");
        var partialCount = periodInvoices.Count(x => x.Status == "Partially Paid");
        var overdueCountInPeriod = periodInvoices.Count(x => x.Status != "Paid" && x.DueDate < today && x.Total > x.Paid);
        var awaitingCount = periodInvoices.Count(x => x.Status != "Paid" && x.Status != "Partially Paid" && !(x.DueDate < today && x.Total > x.Paid));

        var recentInvoices = await db.Invoices.AsNoTracking().Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(5)
            .Select(x => new { x.Id, x.Number, customerName = x.CustomerNameSnapshot, x.IssueDate, x.DueDate, x.Status, x.Total }).ToListAsync(ct);
        var recentCustomers = await db.Customers.AsNoTracking().Where(x => x.OrganizationId == organizationId && !x.IsArchived)
            .OrderByDescending(x => x.CreatedAtUtc).Take(5)
            .Select(x => new { x.Id, x.Name, x.Email, x.CreatedAtUtc }).ToListAsync(ct);
        var lowStockProducts = await db.Products.AsNoTracking().Where(x => x.OrganizationId == organizationId && !x.IsArchived && x.StockQuantity <= x.LowStockThreshold)
            .OrderBy(x => x.StockQuantity).Take(5)
            .Select(x => new { x.Id, x.Sku, x.Name, x.StockQuantity, x.LowStockThreshold }).ToListAsync(ct);

        var activity = new List<DashboardActivity>();
        var recentPaymentRows = await payments.OrderByDescending(x => x.PaidOn).Take(5)
            .Select(x => new { x.PaidOn, x.Amount, x.Invoice.Number, x.Invoice.CustomerNameSnapshot }).ToListAsync(ct);
        activity.AddRange(recentPaymentRows.Select(x => new DashboardActivity(x.PaidOn, "Payment", $"Payment received from {x.CustomerNameSnapshot} · {x.Number}", x.Amount)));
        var recentExpenseRows = await expenses.OrderByDescending(x => x.ExpenseDate).Take(5)
            .Select(x => new { x.ExpenseDate, x.Amount, x.Category, x.Description }).ToListAsync(ct);
        activity.AddRange(recentExpenseRows.Select(x => new DashboardActivity(x.ExpenseDate, "Expense", $"{x.Category} · {x.Description}", -x.Amount)));
        activity.AddRange(recentInvoices.Where(x => x.Status != "Cancelled")
            .Select(x => new DashboardActivity(x.IssueDate, "Invoice", $"Invoice {x.Number} · {x.customerName}", x.Total)));

        var overdueBalances = openBalances.Where(x => x.DueDate < today).Sum(x => Math.Max(0m, x.Total - x.Paid));
        return new {
            from, to,
            totalSales = currentSales,
            paymentsReceived = currentPayments,
            totalExpenses = currentExpenses,
            outstanding = openBalances.Sum(x => Math.Max(0m, x.Total - x.Paid)),
            overdueCount = openBalances.Count(x => x.DueDate < today && x.Total > x.Paid),
            overdueBalance = overdueBalances,
            invoiceCount = await invoices.CountAsync(ct),
            salesChangePercent = ChangePercent(currentSales, previousSales),
            paymentsChangePercent = ChangePercent(currentPayments, previousPayments),
            expensesChangePercent = ChangePercent(currentExpenses, previousExpenses),
            monthlyActivity,
            paymentStatus = new { paid = paidCount, partiallyPaid = partialCount, overdue = overdueCountInPeriod, awaiting = awaitingCount },
            recentInvoices,
            recentCustomers,
            lowStockProducts,
            recentActivity = activity.OrderByDescending(x => x.Date).Take(8).ToArray()
        };
    }

    private static decimal? ChangePercent(decimal current, decimal previous)
        => previous == 0 ? null : decimal.Round((current - previous) / previous * 100m, 1, MidpointRounding.AwayFromZero);

    private sealed record DashboardActivity(DateOnly Date, string Type, string Description, decimal Amount);
}

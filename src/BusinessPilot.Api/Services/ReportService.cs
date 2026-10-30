using System.Globalization;
using System.Text;
using BusinessPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Services;

public sealed class ReportService(BusinessPilotDbContext db)
{
    public async Task<object> GetSummaryAsync(Guid organizationId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var invoices = db.Invoices.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.Status != "Cancelled" && x.IssueDate >= from && x.IssueDate <= to);
        var payments = db.Payments.AsNoTracking().Where(x => x.Invoice.OrganizationId == organizationId && x.PaidOn >= from && x.PaidOn <= to);
        var expenses = db.Expenses.AsNoTracking().Where(x => x.OrganizationId == organizationId && !x.IsArchived && x.ExpenseDate >= from && x.ExpenseDate <= to);
        var balances = await invoices.Select(x => new { x.Total, x.Status, x.DueDate, Paid = x.Payments.Sum(p => (decimal?)p.Amount) ?? 0m }).ToListAsync(ct);
        var invoiceRows = await invoices.Select(x => new { x.IssueDate, x.Total }).ToListAsync(ct);
        var paymentRows = await payments.Select(x => new { x.PaidOn, x.Amount }).ToListAsync(ct);
        var expenseRows = await expenses.Select(x => new { x.ExpenseDate, x.Amount }).ToListAsync(ct);
        var firstMonth = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(to.Year, to.Month, 1);
        var monthCount = (lastMonth.Year - firstMonth.Year) * 12 + lastMonth.Month - firstMonth.Month + 1;
        var monthly = Enumerable.Range(0, monthCount).Select(offset => {
            var month = firstMonth.AddMonths(offset);
            return new {
                month = $"{month.Year:0000}-{month.Month:00}",
                label = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month.Month),
                sales = invoiceRows.Where(x => x.IssueDate.Year == month.Year && x.IssueDate.Month == month.Month).Sum(x => x.Total),
                payments = paymentRows.Where(x => x.PaidOn.Year == month.Year && x.PaidOn.Month == month.Month).Sum(x => x.Amount),
                expenses = expenseRows.Where(x => x.ExpenseDate.Year == month.Year && x.ExpenseDate.Month == month.Month).Sum(x => x.Amount)
            };
        }).ToArray();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var open = balances.Where(x => x.Status != "Paid").Select(x => new { Balance = Math.Max(0m, x.Total - x.Paid), x.DueDate });
        var overdue = open.Where(x => x.DueDate < today && x.Balance > 0m).ToArray();
        return new {
            from, to,
            totalSales = invoiceRows.Sum(x => x.Total),
            paymentsReceived = paymentRows.Sum(x => x.Amount),
            totalExpenses = expenseRows.Sum(x => x.Amount),
            netCashFlow = paymentRows.Sum(x => x.Amount) - expenseRows.Sum(x => x.Amount),
            outstanding = balances.Sum(x => Math.Max(0m, x.Total - x.Paid)),
            outstandingInvoiceCount = balances.Count(x => x.Status != "Paid" && x.Total > x.Paid),
            overdueBalance = overdue.Sum(x => x.Balance),
            overdueInvoiceCount = overdue.Length,
            invoiceCount = invoiceRows.Count,
            paymentCount = paymentRows.Count,
            expenseCount = expenseRows.Count,
            monthly
        };
    }

    public async Task<byte[]> ExportCsvAsync(Guid organizationId, string type, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var lines = new List<string>();
        switch (type.ToLowerInvariant())
        {
            case "sales":
                lines.Add(Row("Invoice", "Customer", "Issue date", "Due date", "Status", "Total INR"));
                var sales = await db.Invoices.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.Status != "Cancelled" && x.IssueDate >= from && x.IssueDate <= to)
                    .OrderBy(x => x.IssueDate).Select(x => new { x.Number, Customer = x.CustomerNameSnapshot, x.IssueDate, x.DueDate, x.Status, x.Total }).ToListAsync(ct);
                lines.AddRange(sales.Select(x => Row(x.Number, x.Customer, x.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    x.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Status, x.Total.ToString(CultureInfo.InvariantCulture))));
                break;
            case "payments":
                lines.Add(Row("Invoice", "Paid on", "Method", "Reference", "Amount INR"));
                var payments = await db.Payments.AsNoTracking().Where(x => x.Invoice.OrganizationId == organizationId && x.PaidOn >= from && x.PaidOn <= to)
                    .OrderBy(x => x.PaidOn).Select(x => new { x.Invoice.Number, x.PaidOn, x.Method, x.Reference, x.Amount }).ToListAsync(ct);
                lines.AddRange(payments.Select(x => Row(x.Number, x.PaidOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Method, x.Reference, x.Amount.ToString(CultureInfo.InvariantCulture))));
                break;
            case "expenses":
                lines.Add(Row("Date", "Category", "Vendor", "Description", "Amount INR"));
                var expenses = await db.Expenses.AsNoTracking().Where(x => x.OrganizationId == organizationId && !x.IsArchived && x.ExpenseDate >= from && x.ExpenseDate <= to)
                    .OrderBy(x => x.ExpenseDate).Select(x => new { x.ExpenseDate, x.Category, x.Vendor, x.Description, x.Amount }).ToListAsync(ct);
                lines.AddRange(expenses.Select(x => Row(x.ExpenseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Category, x.Vendor, x.Description, x.Amount.ToString(CultureInfo.InvariantCulture))));
                break;
            case "outstanding":
            case "overdue":
                lines.Add(Row("Invoice", "Customer", "Issue date", "Due date", "Status", "Total INR", "Paid INR", "Balance INR"));
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var openInvoices = await db.Invoices.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.Status != "Cancelled" && x.IssueDate >= from && x.IssueDate <= to)
                    .Select(x => new { x.Number, Customer = x.CustomerNameSnapshot, x.IssueDate, x.DueDate, x.Status, x.Total, Paid = x.Payments.Sum(p => (decimal?)p.Amount) ?? 0m }).ToListAsync(ct);
                var balances = openInvoices.Select(x => new { x.Number, x.Customer, x.IssueDate, x.DueDate, x.Status, x.Total, x.Paid, Balance = Math.Max(0m, x.Total - x.Paid) })
                    .Where(x => x.Balance > 0m && (type == "outstanding" || x.DueDate < today))
                    .OrderBy(x => x.DueDate);
                lines.AddRange(balances.Select(x => Row(x.Number, x.Customer, x.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    x.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.DueDate < today ? "Overdue" : x.Status,
                    x.Total.ToString(CultureInfo.InvariantCulture), x.Paid.ToString(CultureInfo.InvariantCulture), x.Balance.ToString(CultureInfo.InvariantCulture))));
                break;
            case "monthly":
                lines.Add(Row("Month", "Sales INR", "Payments INR", "Expenses INR", "Net cash flow INR"));
                var monthInvoices = await db.Invoices.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.Status != "Cancelled" && x.IssueDate >= from && x.IssueDate <= to)
                    .Select(x => new { x.IssueDate, x.Total }).ToListAsync(ct);
                var monthPayments = await db.Payments.AsNoTracking().Where(x => x.Invoice.OrganizationId == organizationId && x.PaidOn >= from && x.PaidOn <= to)
                    .Select(x => new { x.PaidOn, x.Amount }).ToListAsync(ct);
                var monthExpenses = await db.Expenses.AsNoTracking().Where(x => x.OrganizationId == organizationId && !x.IsArchived && x.ExpenseDate >= from && x.ExpenseDate <= to)
                    .Select(x => new { x.ExpenseDate, x.Amount }).ToListAsync(ct);
                var first = new DateOnly(from.Year, from.Month, 1); var lastExclusive = new DateOnly(to.Year, to.Month, 1).AddMonths(1);
                var count = (lastExclusive.Year - first.Year) * 12 + lastExclusive.Month - first.Month;
                foreach (var offset in Enumerable.Range(0, count))
                {
                    var month = first.AddMonths(offset);
                    var salesTotal = monthInvoices.Where(x => x.IssueDate.Year == month.Year && x.IssueDate.Month == month.Month).Sum(x => x.Total);
                    var paymentTotal = monthPayments.Where(x => x.PaidOn.Year == month.Year && x.PaidOn.Month == month.Month).Sum(x => x.Amount);
                    var expenseTotal = monthExpenses.Where(x => x.ExpenseDate.Year == month.Year && x.ExpenseDate.Month == month.Month).Sum(x => x.Amount);
                    lines.Add(Row(month.ToString("yyyy-MM", CultureInfo.InvariantCulture), salesTotal.ToString(CultureInfo.InvariantCulture), paymentTotal.ToString(CultureInfo.InvariantCulture),
                        expenseTotal.ToString(CultureInfo.InvariantCulture), (paymentTotal - expenseTotal).ToString(CultureInfo.InvariantCulture)));
                }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(string.Join("\r\n", lines) + "\r\n");
    }

    private static string Row(params string?[] fields) => string.Join(",", fields.Select(EscapeCsv));
    private static string EscapeCsv(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

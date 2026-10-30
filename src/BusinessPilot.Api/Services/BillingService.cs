using BusinessPilot.Api.Data;
using BusinessPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Services;

public sealed class PlanOffer
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? MonthlyPriceInr { get; set; }
    public int MonthlyInvoiceLimit { get; set; }
    public int CustomerLimit { get; set; }
    public int UserLimit { get; set; }
    public int AiMonthlyMessageLimit { get; set; }
}
public sealed record BillingOverview(string PlanCode, string PlanName, string SubscriptionStatus, DateTime StartedAtUtc,
    int InvoicesUsedThisMonth, int MonthlyInvoiceLimit, int CustomersUsed, int CustomerLimit, int UsersUsed, int UserLimit,
    string PaymentProvider, bool CheckoutConfigured, string? SetupMessage, DateTime? TrialEndsAtUtc,
    int TrialDaysRemaining, IReadOnlyList<PlanOffer> Plans);

public sealed class BillingService(BusinessPilotDbContext db, IConfiguration configuration)
{
    private readonly PlanOffer[] catalog = configuration.GetSection("Billing:Plans").Get<PlanOffer[]>() ?? [
        new() { Code = "Free", Name = "Free", MonthlyPriceInr = 0m, MonthlyInvoiceLimit = 5, CustomerLimit = 25, UserLimit = 1, AiMonthlyMessageLimit = 25 },
        new() { Code = "Starter", Name = "Starter", MonthlyInvoiceLimit = 100, CustomerLimit = 500, UserLimit = 3, AiMonthlyMessageLimit = 500 },
        new() { Code = "Pro", Name = "Pro", MonthlyInvoiceLimit = 0, CustomerLimit = 0, UserLimit = 10, AiMonthlyMessageLimit = 5000 }
    ];
    private string Provider => configuration["Billing:Provider"] ?? "NotConfigured";
    public int TrialDays => Math.Clamp(configuration.GetValue("Billing:TrialDays", 14), 1, 90);

    public async Task<BillingOverview> GetOverviewAsync(Guid organizationId, CancellationToken ct, bool checkoutConfigured)
    {
        var subscription = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId, ct);
        var plan = catalog.FirstOrDefault(x => x.Code == subscription?.PlanCode) ?? catalog[0];
        var now = DateTime.UtcNow;
        DateTime? trialEndsAt = plan.Code == "Free" && subscription is not null
            ? subscription.CurrentPeriodEndsAtUtc ?? subscription.StartedAtUtc.AddDays(TrialDays)
            : null;
        var trialRemaining = trialEndsAt.HasValue
            ? Math.Max(0, (int)Math.Ceiling((trialEndsAt.Value - now).TotalDays))
            : 0;
        var subscriptionStatus = trialEndsAt.HasValue && trialEndsAt.Value <= now
            ? "TrialExpired"
            : subscription?.Status ?? "Active";
        var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var invoiceCount = await db.Invoices.CountAsync(x => x.OrganizationId == organizationId && x.CreatedAtUtc >= start && x.CreatedAtUtc < end, ct);
        var customerCount = await db.Customers.CountAsync(x => x.OrganizationId == organizationId && !x.IsArchived, ct);
        var userCount = await db.Memberships.CountAsync(x => x.OrganizationId == organizationId, ct);
        var setupMessage = checkoutConfigured ? null
            : "Set Razorpay live API keys, webhook secret, and the Starter/Pro plan IDs in environment configuration before accepting subscriptions.";
        return new BillingOverview(plan.Code, plan.Name, subscriptionStatus, subscription?.StartedAtUtc ?? now,
            invoiceCount, plan.MonthlyInvoiceLimit, customerCount, plan.CustomerLimit, userCount, plan.UserLimit,
            Provider, checkoutConfigured, setupMessage, trialEndsAt, trialRemaining, catalog);
    }

    public bool IsTrialActive(OrganizationSubscription subscription, DateTime now)
    {
        if (subscription.PlanCode != "Free") return false;
        var trialEndsAt = subscription.CurrentPeriodEndsAtUtc ?? subscription.StartedAtUtc.AddDays(TrialDays);
        return trialEndsAt > now;
    }

    public async Task<bool> CanWriteAsync(Guid organizationId, CancellationToken ct)
    {
        var subscription = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId, ct);
        if (subscription is null) return false;
        var now = DateTime.UtcNow;
        if (IsTrialActive(subscription, now)) return true;
        return subscription.PlanCode != "Free"
            && (subscription.Status == "Active" || subscription.Status == "Cancelled")
            && (!subscription.CurrentPeriodEndsAtUtc.HasValue || subscription.CurrentPeriodEndsAtUtc.Value > now);
    }

    public async Task<string?> CheckInvoiceLimitAsync(Guid organizationId, CancellationToken ct)
    {
        var subscription = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId, ct);
        var plan = catalog.FirstOrDefault(x => x.Code == subscription?.PlanCode) ?? catalog[0];
        if (plan.MonthlyInvoiceLimit == 0) return null;
        var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var used = await db.Invoices.CountAsync(x => x.OrganizationId == organizationId && x.CreatedAtUtc >= start && x.CreatedAtUtc < end, ct);
        return used >= plan.MonthlyInvoiceLimit
            ? $"Your {plan.Name} plan allows {plan.MonthlyInvoiceLimit} invoices per month. Upgrade your plan to create more."
            : null;
    }

    public async Task<string?> CheckCustomerLimitAsync(Guid organizationId, CancellationToken ct)
    {
        var subscription = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId, ct);
        var plan = catalog.FirstOrDefault(x => x.Code == subscription?.PlanCode) ?? catalog[0];
        if (plan.CustomerLimit == 0) return null;
        var used = await db.Customers.CountAsync(x => x.OrganizationId == organizationId && !x.IsArchived, ct);
        return used >= plan.CustomerLimit
            ? $"Your {plan.Name} plan allows {plan.CustomerLimit} active customers. Upgrade your plan to add more."
            : null;
    }

    public async Task<int> GetAiMessageLimitAsync(Guid organizationId, CancellationToken ct)
    {
        var subscription = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId, ct);
        var plan = catalog.FirstOrDefault(x => x.Code == subscription?.PlanCode) ?? catalog[0];
        return Math.Max(0, plan.AiMonthlyMessageLimit);
    }
}

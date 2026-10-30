using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Services;

public sealed record BillingCheckout(string SubscriptionId, string CheckoutUrl);

public sealed class RazorpayBillingService(HttpClient client, BusinessPilotDbContext db,
    BillingService billing, IConfiguration configuration, ILogger<RazorpayBillingService> logger)
{
    private string? KeyId => configuration["Razorpay:KeyId"];
    private string? KeySecret => configuration["Razorpay:KeySecret"];
    private string? WebhookSecret => configuration["Razorpay:WebhookSecret"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret)
        && !string.IsNullOrWhiteSpace(WebhookSecret)
        && !string.IsNullOrWhiteSpace(PlanId("Starter"))
        && !string.IsNullOrWhiteSpace(PlanId("Pro"));

    public async Task<ServiceResult<BillingCheckout>> CreateCheckoutAsync(Guid organizationId, string planCode, CancellationToken ct)
    {
        if (!IsConfigured) return ServiceResult<BillingCheckout>.Failure("Razorpay checkout is not configured yet.");
        if (planCode is not ("Starter" or "Pro")) return ServiceResult<BillingCheckout>.Failure("Choose the Starter or Pro plan.");

        var subscription = await db.OrganizationSubscriptions.FirstOrDefaultAsync(x => x.OrganizationId == organizationId, ct);
        if (subscription is null) return ServiceResult<BillingCheckout>.Missing();
        if (subscription.PlanCode != "Free" && subscription.Status == "Active"
            && (!subscription.CurrentPeriodEndsAtUtc.HasValue || subscription.CurrentPeriodEndsAtUtc.Value > DateTime.UtcNow))
            return ServiceResult<BillingCheckout>.Failure("A paid subscription is already active. Contact support to change plans.");

        if (!string.IsNullOrWhiteSpace(subscription.ProviderSubscriptionId))
        {
            using var existing = await SendGatewayAsync(HttpMethod.Get, $"subscriptions/{Uri.EscapeDataString(subscription.ProviderSubscriptionId)}", null, ct);
            if (existing.IsSuccessStatusCode)
            {
                using var existingJson = await JsonDocument.ParseAsync(await existing.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var existingRoot = existingJson.RootElement;
                if (existingRoot.TryGetProperty("plan_id", out var existingPlan)
                    && existingPlan.GetString() == PlanId(planCode)
                    && existingRoot.TryGetProperty("short_url", out var existingUrl)
                    && IsRazorpayCheckoutUrl(existingUrl.GetString()))
                    return ServiceResult<BillingCheckout>.Success(new BillingCheckout(subscription.ProviderSubscriptionId, existingUrl.GetString()!));
                if (existingRoot.TryGetProperty("status", out var existingStatus)
                    && existingStatus.GetString() is "created" or "authenticated" or "active")
                    return ServiceResult<BillingCheckout>.Failure("A different Razorpay subscription is already pending or active. Finish or cancel it before choosing another plan.");
            }
        }

        var requestBody = new Dictionary<string, object?>
        {
            ["plan_id"] = PlanId(planCode),
            ["total_count"] = 1200,
            ["quantity"] = 1,
            ["customer_notify"] = true,
            ["notes"] = new Dictionary<string, string>
            {
                ["organization_id"] = organizationId.ToString(),
                ["plan_code"] = planCode
            }
        };
        var now = DateTime.UtcNow;
        DateTime? trialEndsAt = subscription.PlanCode == "Free"
            ? subscription.CurrentPeriodEndsAtUtc ?? subscription.StartedAtUtc.AddDays(billing.TrialDays)
            : null;
        if (trialEndsAt.HasValue && trialEndsAt.Value > now)
            requestBody["start_at"] = new DateTimeOffset(trialEndsAt.Value).ToUnixTimeSeconds();

        using var response = await SendGatewayAsync(HttpMethod.Post, "subscriptions", requestBody, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Razorpay subscription creation failed with status {StatusCode}.", (int)response.StatusCode);
            return ServiceResult<BillingCheckout>.Failure("Razorpay could not create the subscription. Check that the configured plan IDs belong to these API keys and subscriptions are enabled.");
        }

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = json.RootElement;
        var subscriptionId = root.TryGetProperty("id", out var id) ? id.GetString() : null;
        var checkoutUrl = root.TryGetProperty("short_url", out var shortUrl) ? shortUrl.GetString() : null;
        if (string.IsNullOrWhiteSpace(subscriptionId) || !IsRazorpayCheckoutUrl(checkoutUrl))
            return ServiceResult<BillingCheckout>.Failure("Razorpay did not return a valid hosted checkout link.");

        subscription.ProviderSubscriptionId = subscriptionId;
        subscription.Status = trialEndsAt.HasValue && trialEndsAt.Value > now ? "Trialing" : "Pending";
        subscription.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return ServiceResult<BillingCheckout>.Success(new BillingCheckout(subscriptionId, checkoutUrl!));
    }

    public async Task<bool> HandleWebhookAsync(byte[] rawBody, string? signature, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(WebhookSecret) || !VerifySignature(rawBody, signature, WebhookSecret)) return false;

        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("subscription", out var subscriptionPayload)
            || !subscriptionPayload.TryGetProperty("entity", out var eventSubscription)
            || !eventSubscription.TryGetProperty("id", out var eventId)) return true;
        var id = eventId.GetString();
        if (string.IsNullOrWhiteSpace(id)) return true;

        var local = await db.OrganizationSubscriptions.FirstOrDefaultAsync(x => x.ProviderSubscriptionId == id, ct);
        if (local is null) return true;

        // Razorpay retries webhook deliveries and does not guarantee event ordering. Applying
        // entity snapshots is idempotent; reject snapshots older than our last applied update.
        var eventCreatedAt = root.TryGetProperty("created_at", out var createdAtValue) && createdAtValue.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(createdAtValue.GetInt64()).UtcDateTime
            : DateTime.UtcNow;
        if (eventCreatedAt <= local.UpdatedAtUtc) return true;

        var entity = eventSubscription;
        var providerStatus = entity.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null;
        var providerPlanId = entity.TryGetProperty("plan_id", out var planValue) ? planValue.GetString() : null;
        var planCode = PlanCodeForId(providerPlanId);
        if (providerStatus == "active" && planCode is not null)
        {
            local.PlanCode = planCode;
            local.Status = "Active";
        }
        else if (providerStatus is "halted" or "pending" or "cancelled" or "completed" or "expired")
        {
            local.Status = providerStatus switch
            {
                "halted" or "pending" => "PastDue",
                "cancelled" => "Cancelled",
                "completed" or "expired" => "Expired",
                _ => local.Status
            };
        }

        var currentEnd = entity.TryGetProperty("current_end", out var currentEndValue) && currentEndValue.ValueKind == JsonValueKind.Number
            ? currentEndValue.GetInt64()
            : 0;
        if (currentEnd > 0) local.CurrentPeriodEndsAtUtc = DateTimeOffset.FromUnixTimeSeconds(currentEnd).UtcDateTime;
        local.UpdatedAtUtc = eventCreatedAt;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<HttpResponseMessage> SendGatewayAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{KeySecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private string? PlanId(string code) => configuration[$"Razorpay:PlanIds:{code}"];

    private string? PlanCodeForId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (id == PlanId("Starter")) return "Starter";
        if (id == PlanId("Pro")) return "Pro";
        return null;
    }

    private static bool IsRazorpayCheckoutUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
           && (uri.Host.Equals("rzp.io", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".rzp.io", StringComparison.OrdinalIgnoreCase));

    private static bool VerifySignature(byte[] body, string? signature, string secret)
    {
        if (string.IsNullOrWhiteSpace(signature) || signature.Length != 64) return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}

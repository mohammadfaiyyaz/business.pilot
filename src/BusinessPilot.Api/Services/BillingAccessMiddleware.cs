namespace BusinessPilot.Api.Services;

public sealed class BillingAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, BillingService billing)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            && !HttpMethods.IsPut(context.Request.Method)
            && !HttpMethods.IsPatch(context.Request.Method)
            && !HttpMethods.IsDelete(context.Request.Method))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        if (path.Equals("/api/billing/checkout", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/billing/webhook", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(context.User.FindFirst("org_id")?.Value, out var organizationId))
        {
            await next(context);
            return;
        }

        if (await billing.CanWriteAsync(organizationId, context.RequestAborted))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new {
            message = "Your 14-day Free trial has ended. Your workspace is read-only until you subscribe to Starter or Pro."
        }, context.RequestAborted);
    }
}

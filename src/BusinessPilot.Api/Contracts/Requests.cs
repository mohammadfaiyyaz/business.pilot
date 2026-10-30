using System.ComponentModel.DataAnnotations;

namespace BusinessPilot.Api.Contracts;

public sealed record RegisterRequest([Required, StringLength(120, MinimumLength = 2)] string Name,
    [Required, EmailAddress] string Email, [Required, MinLength(12)] string Password,
    [Required, StringLength(160, MinimumLength = 2)] string OrganizationName);
public sealed record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public sealed record AuthResponse(string Token, DateTime ExpiresAtUtc, string Name, string Email, Guid OrganizationId, string OrganizationName, string Role);
public sealed record CustomerRequest([Required, StringLength(160, MinimumLength = 2)] string Name,
    [EmailAddress, StringLength(254)] string? Email, [StringLength(40)] string? Phone,
    [StringLength(500)] string? Address, [StringLength(15)] string? Gstin, [StringLength(2000)] string? Notes);
public sealed record InvoiceItemRequest([StringLength(250)] string? Description,
    [Range(typeof(decimal), "0.01", "1000000")] decimal Quantity,
    [Range(typeof(decimal), "0", "999999999")] decimal UnitPrice,
    [Range(typeof(decimal), "0", "100")] decimal TaxRate, Guid? ProductId = null);
public sealed record CreateInvoiceRequest([Required] Guid CustomerId, DateOnly IssueDate, DateOnly DueDate,
    [Required, MinLength(1), MaxLength(100)] List<InvoiceItemRequest> Items, [StringLength(2000)] string? Notes);
public sealed record RecordPaymentRequest([Range(typeof(decimal), "0.01", "999999999")] decimal Amount,
    DateOnly PaidOn, [StringLength(60)] string Method, [StringLength(120)] string? Reference);
public sealed record ExpenseRequest([Required, StringLength(80, MinimumLength = 2)] string Category,
    [StringLength(160)] string? Vendor, [Range(typeof(decimal), "0.01", "999999999")] decimal Amount,
    DateOnly ExpenseDate, [Required, StringLength(500, MinimumLength = 2)] string Description);
public sealed record ProductRequest([Required, StringLength(64, MinimumLength = 1)] string Sku,
    [Required, StringLength(160, MinimumLength = 2)] string Name, [StringLength(1000)] string? Description,
    [Range(typeof(decimal), "0", "999999999")] decimal CostPrice,
    [Range(typeof(decimal), "0", "999999999")] decimal SellingPrice,
    [Range(typeof(decimal), "0", "100")] decimal TaxRate,
    [Range(typeof(decimal), "0", "999999999")] decimal StockQuantity,
    [Range(typeof(decimal), "0", "999999999")] decimal LowStockThreshold);
public sealed record StockAdjustmentRequest([Range(typeof(decimal), "0.001", "999999999")] decimal Quantity,
    [Required, RegularExpression("^(Increase|Decrease)$")] string Direction,
    [Required, StringLength(200, MinimumLength = 2)] string Reason);
public sealed record SupportKnowledgeRequest([Required, RegularExpression("^(FAQ|Document)$")] string Kind,
    [Required, StringLength(160, MinimumLength = 2)] string Title,
    [StringLength(1000)] string? Question, [StringLength(4000)] string? Answer,
    [Required, StringLength(12000, MinimumLength = 2)] string Content, bool IsApproved);
public sealed record SupportMessageRequest(Guid? ConversationId,
    [Required, StringLength(4000, MinimumLength = 1)] string Message);
public sealed record WidgetSupportMessageRequest(Guid? ConversationId,
    [Required, StringLength(4000, MinimumLength = 1)] string Message,
    [StringLength(64)] string? VisitorKey, [StringLength(120)] string? Name,
    [EmailAddress, StringLength(254)] string? Email, bool ConsentToContact);
public sealed record UpdateProfileRequest([Required, StringLength(120, MinimumLength = 2)] string Name,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(160, MinimumLength = 2)] string OrganizationName);

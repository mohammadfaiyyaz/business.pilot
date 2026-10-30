using Microsoft.AspNetCore.Identity;

namespace BusinessPilot.Api.Models;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Gstin { get; set; }
    public string? Address { get; set; }
    public string InvoicePrefix { get; set; } = "BP";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Membership> Memberships { get; set; } = [];
    public OrganizationSubscription? Subscription { get; set; }
    public ICollection<Expense> Expenses { get; set; } = [];
    public ICollection<Product> Products { get; set; } = [];
}

public sealed class SupportWidget
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string PublicKey { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SupportKnowledgeItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Kind { get; set; } = "FAQ";
    public string Title { get; set; } = "";
    public string? Question { get; set; }
    public string? Answer { get; set; }
    public string Content { get; set; } = "";
    public bool IsApproved { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SupportConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Source { get; set; } = "InApp";
    public string Status { get; set; } = "Open";
    public string? VisitorKey { get; set; }
    public string? LeadName { get; set; }
    public string? LeadEmail { get; set; }
    public bool ContactConsent { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<SupportMessage> Messages { get; set; } = [];
}

public sealed class SupportMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public SupportConversation Conversation { get; set; } = null!;
    public string Role { get; set; } = "Customer";
    public string Content { get; set; } = "";
    public string? Provider { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SupportMonthlyUsage
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public DateOnly PeriodStart { get; set; }
    public int MessagesUsed { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class OrganizationSubscription
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string PlanCode { get; set; } = "Free";
    public string Status { get; set; } = "Active";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CurrentPeriodEndsAtUtc { get; set; }
    public string? ProviderSubscriptionId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Membership
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public string Role { get; set; } = "Owner";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Gstin { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; set; }
    public string Number { get; set; } = "";
    public string CustomerNameSnapshot { get; set; } = "";
    public string? CustomerAddressSnapshot { get; set; }
    public string? CustomerGstinSnapshot { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = "Draft";
    public decimal Subtotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<InvoiceItem> Items { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal AmountPaid => Payments.Sum(x => x.Amount);
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal BalanceDue => Math.Max(0, Total - AmountPaid);
}

public sealed class InvoiceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string? ProductSkuSnapshot { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateOnly PaidOn { get; set; }
    public string Method { get; set; } = "Bank transfer";
    public string? Reference { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Category { get; set; } = "Other";
    public string? Vendor { get; set; }
    public decimal Amount { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public string Description { get; set; } = "";
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal TaxRate { get; set; }
    public decimal StockQuantity { get; set; }
    public decimal LowStockThreshold { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<InventoryMovement> InventoryMovements { get; set; } = [];
}

public sealed class InventoryMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public decimal QuantityDelta { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

using BusinessPilot.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Data;

public sealed class BusinessPilotDbContext(DbContextOptions<BusinessPilotDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationSubscription> OrganizationSubscriptions => Set<OrganizationSubscription>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<SupportWidget> SupportWidgets => Set<SupportWidget>();
    public DbSet<SupportKnowledgeItem> SupportKnowledgeItems => Set<SupportKnowledgeItem>();
    public DbSet<SupportConversation> SupportConversations => Set<SupportConversation>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<SupportMonthlyUsage> SupportMonthlyUsage => Set<SupportMonthlyUsage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Organization>().HasIndex(x => x.Name);
        builder.Entity<OrganizationSubscription>().HasKey(x => x.OrganizationId);
        builder.Entity<OrganizationSubscription>().HasOne(x => x.Organization).WithOne(x => x.Subscription)
            .HasForeignKey<OrganizationSubscription>(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<OrganizationSubscription>().HasIndex(x => new { x.PlanCode, x.Status });
        builder.Entity<OrganizationSubscription>().Property(x => x.PlanCode).HasMaxLength(32);
        builder.Entity<OrganizationSubscription>().Property(x => x.Status).HasMaxLength(32);
        builder.Entity<OrganizationSubscription>().Property(x => x.ProviderSubscriptionId).HasMaxLength(128);
        builder.Entity<Membership>().HasKey(x => new { x.OrganizationId, x.UserId });
        builder.Entity<Membership>().HasOne(x => x.Organization).WithMany(x => x.Memberships).HasForeignKey(x => x.OrganizationId);
        builder.Entity<Membership>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        builder.Entity<Customer>().HasIndex(x => new { x.OrganizationId, x.Name });
        builder.Entity<Customer>().HasIndex(x => new { x.OrganizationId, x.Email });
        builder.Entity<Invoice>().HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
        builder.Entity<Invoice>().HasIndex(x => new { x.OrganizationId, x.IssueDate, x.Status });
        builder.Entity<InvoiceItem>().Property(x => x.UnitPrice).HasPrecision(12, 2);
        builder.Entity<InvoiceItem>().Property(x => x.Quantity).HasPrecision(12, 3);
        builder.Entity<InvoiceItem>().Property(x => x.TaxRate).HasPrecision(5, 2);
        builder.Entity<InvoiceItem>().Property(x => x.LineTotal).HasPrecision(12, 2);
        builder.Entity<InvoiceItem>().Property(x => x.ProductSkuSnapshot).HasMaxLength(64);
        builder.Entity<Invoice>().Property(x => x.Subtotal).HasPrecision(12, 2);
        builder.Entity<Invoice>().Property(x => x.TaxTotal).HasPrecision(12, 2);
        builder.Entity<Invoice>().Property(x => x.Total).HasPrecision(12, 2);
        builder.Entity<Payment>().Property(x => x.Amount).HasPrecision(12, 2);
        builder.Entity<Expense>().Property(x => x.Amount).HasPrecision(12, 2);
        builder.Entity<Expense>().Property(x => x.Category).HasMaxLength(80);
        builder.Entity<Expense>().Property(x => x.Vendor).HasMaxLength(160);
        builder.Entity<Expense>().Property(x => x.Description).HasMaxLength(500);
        builder.Entity<Expense>().HasIndex(x => new { x.OrganizationId, x.ExpenseDate });
        builder.Entity<Expense>().HasIndex(x => new { x.OrganizationId, x.Category });
        builder.Entity<Expense>().HasOne(x => x.Organization).WithMany(x => x.Expenses)
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Product>().Property(x => x.Sku).HasMaxLength(64);
        builder.Entity<Product>().Property(x => x.Name).HasMaxLength(160);
        builder.Entity<Product>().Property(x => x.Description).HasMaxLength(1000);
        builder.Entity<Product>().Property(x => x.CostPrice).HasPrecision(12, 2);
        builder.Entity<Product>().Property(x => x.SellingPrice).HasPrecision(12, 2);
        builder.Entity<Product>().Property(x => x.TaxRate).HasPrecision(5, 2);
        builder.Entity<Product>().Property(x => x.StockQuantity).HasPrecision(12, 3);
        builder.Entity<Product>().Property(x => x.LowStockThreshold).HasPrecision(12, 3);
        builder.Entity<Product>().HasIndex(x => new { x.OrganizationId, x.Sku }).IsUnique();
        builder.Entity<Product>().HasIndex(x => new { x.OrganizationId, x.Name });
        builder.Entity<Product>().HasOne(x => x.Organization).WithMany(x => x.Products)
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InventoryMovement>().Property(x => x.QuantityDelta).HasPrecision(12, 3);
        builder.Entity<InventoryMovement>().Property(x => x.Reason).HasMaxLength(200);
        builder.Entity<InventoryMovement>().HasIndex(x => new { x.ProductId, x.CreatedAtUtc });
        builder.Entity<Product>().HasMany(x => x.InventoryMovements).WithOne(x => x.Product)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InvoiceItem>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InventoryMovement>().HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Invoice>().HasMany(x => x.Items).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Invoice>().HasMany(x => x.Payments).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SupportWidget>().HasKey(x => x.OrganizationId);
        builder.Entity<SupportWidget>().HasOne(x => x.Organization).WithOne().HasForeignKey<SupportWidget>(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupportWidget>().Property(x => x.PublicKey).HasMaxLength(64);
        builder.Entity<SupportWidget>().HasIndex(x => x.PublicKey).IsUnique();
        builder.Entity<SupportKnowledgeItem>().Property(x => x.Kind).HasMaxLength(24);
        builder.Entity<SupportKnowledgeItem>().Property(x => x.Title).HasMaxLength(160);
        builder.Entity<SupportKnowledgeItem>().Property(x => x.Question).HasMaxLength(1000);
        builder.Entity<SupportKnowledgeItem>().Property(x => x.Answer).HasMaxLength(4000);
        builder.Entity<SupportKnowledgeItem>().Property(x => x.Content).HasMaxLength(12000);
        builder.Entity<SupportKnowledgeItem>().HasIndex(x => new { x.OrganizationId, x.IsApproved, x.IsArchived });
        builder.Entity<SupportKnowledgeItem>().HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupportConversation>().Property(x => x.Source).HasMaxLength(24);
        builder.Entity<SupportConversation>().Property(x => x.Status).HasMaxLength(24);
        builder.Entity<SupportConversation>().Property(x => x.VisitorKey).HasMaxLength(64);
        builder.Entity<SupportConversation>().Property(x => x.LeadName).HasMaxLength(120);
        builder.Entity<SupportConversation>().Property(x => x.LeadEmail).HasMaxLength(254);
        builder.Entity<SupportConversation>().HasIndex(x => new { x.OrganizationId, x.UpdatedAtUtc });
        builder.Entity<SupportConversation>().HasIndex(x => new { x.OrganizationId, x.Status });
        builder.Entity<SupportConversation>().HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupportConversation>().HasMany(x => x.Messages).WithOne(x => x.Conversation).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupportMessage>().Property(x => x.Role).HasMaxLength(24);
        builder.Entity<SupportMessage>().Property(x => x.Provider).HasMaxLength(80);
        builder.Entity<SupportMessage>().Property(x => x.Content).HasMaxLength(8000);
        builder.Entity<SupportMessage>().HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });
        builder.Entity<SupportMonthlyUsage>().HasKey(x => new { x.OrganizationId, x.PeriodStart });
        builder.Entity<SupportMonthlyUsage>().HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

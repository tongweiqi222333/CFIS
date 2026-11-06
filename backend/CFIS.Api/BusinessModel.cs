using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public class Counterparty
{
    public string? RequestHash { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Customer";
    public string Contact { get; set; } = "";
    public string Status { get; set; } = "Active";
}
public abstract class TradeDocument
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CancelledAt { get; set; }
    public string? RequestHash { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CounterpartyId { get; set; }
    public Guid CategoryId { get; set; }
    public string Number { get; set; } = "";
    public string Currency { get; set; } = "CNY";
    public decimal Amount { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Description { get; set; } = "";
    public bool Cancelled { get; set; }
}
public class Receivable : TradeDocument
{
}
public class Payable : TradeDocument
{
}
public class Budget
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? RequestHash { get; set; }
    public string Status { get; set; } = "Draft";
    public Guid? PreviousVersionId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "CNY";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}
public class BudgetItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid BudgetId { get; set; }
    public Guid CategoryId { get; set; }
    public decimal Amount { get; set; }
}
public static class BusinessModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<Counterparty>().ToTable("counterparties");
        b.Entity<Counterparty>().HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.Entity<Counterparty>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Counterparty>().Property(x => x.Contact).HasMaxLength(300);
        b.Entity<Counterparty>().HasIndex(x => new { x.OrganizationId, x.Kind, x.Name }).IsUnique();
        b.Entity<Counterparty>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TradeDocument>().UseTpcMappingStrategy();
        b.Entity<TradeDocument>().HasKey(x => x.Id);
        b.Entity<TradeDocument>().HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.Entity<Receivable>().ToTable("receivables", t => { t.HasCheckConstraint("ck_receivable_amount", "\"Amount\" > 0"); t.HasCheckConstraint("ck_receivable_dates", "\"DueDate\" >= \"IssueDate\""); });
        b.Entity<Payable>().ToTable("payables", t => { t.HasCheckConstraint("ck_payable_amount", "\"Amount\" > 0"); t.HasCheckConstraint("ck_payable_dates", "\"DueDate\" >= \"IssueDate\""); });
        b.Entity<TradeDocument>().Property(x => x.Amount).HasPrecision(19, 4);
        b.Entity<TradeDocument>().Property(x => x.Number).HasMaxLength(100);
        b.Entity<TradeDocument>().Property(x => x.Description).HasMaxLength(500);
        b.Entity<TradeDocument>().Property(x => x.Currency).HasMaxLength(3);
        b.Entity<TradeDocument>().HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
        b.Entity<TradeDocument>().HasOne<Counterparty>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CounterpartyId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TradeDocument>().HasOne<Category>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CategoryId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FinancialTransaction>().ToTable("transactions", t => t.HasCheckConstraint("ck_transaction_document", "\"ReceivableId\" IS NULL OR \"PayableId\" IS NULL"));
        b.Entity<FinancialTransaction>().HasOne<Receivable>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ReceivableId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FinancialTransaction>().HasOne<Payable>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PayableId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Budget>().ToTable("budgets", t => t.HasCheckConstraint("ck_budget_dates", "\"EndDate\" >= \"StartDate\""));
        b.Entity<Budget>().HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.Entity<Budget>().HasOne<Budget>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PreviousVersionId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Budget>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Budget>().Property(x => x.Currency).HasMaxLength(3);
        b.Entity<Budget>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BudgetItem>().ToTable("budget_items", t => t.HasCheckConstraint("ck_budget_item_amount", "\"Amount\" > 0"));
        b.Entity<BudgetItem>().Property(x => x.Amount).HasPrecision(19, 4);
        b.Entity<BudgetItem>().HasIndex(x => new { x.OrganizationId, x.BudgetId, x.CategoryId }).IsUnique();
        b.Entity<BudgetItem>().HasOne<Budget>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BudgetId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<BudgetItem>().HasOne<Category>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CategoryId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}


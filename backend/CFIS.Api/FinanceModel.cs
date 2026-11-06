using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public class FinancialAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "CNY";
    public string Status { get; set; } = "Active";
    public string Kind { get; set; } = "Bank";
    public decimal Balance { get; set; }
}
public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Status { get; set; } = "Active";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Income";
}
public class FinancialTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = "";
    public Guid AccountId { get; set; }
    public Guid? DestinationAccountId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? CorrectionOfId { get; set; }
    public Guid? ReceivableId { get; set; }
    public Guid? PayableId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public Guid? ReversalOfId { get; set; }
    public string? ReversalReason { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public string ActorId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid ResourceId { get; set; }
    public string Detail { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public static class FinanceModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<FinancialAccount>().ToTable("financial_accounts", t => t.HasCheckConstraint("ck_account_balance", "\"Balance\" >= 0"));
        b.Entity<FinancialAccount>().HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.Entity<FinancialAccount>().Property(x => x.Balance).HasPrecision(19, 4);
        b.Entity<FinancialAccount>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<FinancialAccount>().Property(x => x.Currency).HasMaxLength(3);
        b.Entity<FinancialAccount>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Category>().ToTable("categories").HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.Entity<Category>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Category>().HasIndex(x => new { x.OrganizationId, x.Kind, x.Name }).IsUnique();
        b.Entity<Category>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FinancialTransaction>().ToTable("transactions", t => t.HasCheckConstraint("ck_transaction_amount", "\"Amount\" > 0"));
        b.Entity<FinancialTransaction>().HasIndex(x => new { x.OrganizationId, x.AccountId, x.BusinessDate, x.Id }).HasDatabaseName("ix_transactions_account_date_cover").IncludeProperties(x => new { x.Amount, x.Kind });
        b.Entity<FinancialTransaction>().HasIndex(x => new { x.OrganizationId, x.DestinationAccountId, x.BusinessDate, x.Id }).HasDatabaseName("ix_transactions_destination_date_cover").HasFilter("\"DestinationAccountId\" IS NOT NULL").IncludeProperties(x => new { x.Amount, x.Kind });
        b.Entity<FinancialTransaction>().Property(x => x.Amount).HasPrecision(19, 4);
        b.Entity<FinancialTransaction>().Property(x => x.Description).HasMaxLength(500);
        b.Entity<FinancialTransaction>().HasOne<FinancialTransaction>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CorrectionOfId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FinancialTransaction>().HasIndex(x => new { x.OrganizationId, x.CorrectionOfId }).IsUnique();
        b.Entity<FinancialTransaction>().HasIndex(x => new { x.OrganizationId, x.RequestId }).IsUnique();
        b.Entity<FinancialTransaction>().HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.Entity<FinancialTransaction>().HasIndex(x => new { x.OrganizationId, x.ReversalOfId }).IsUnique();
        b.Entity<FinancialTransaction>().HasOne<FinancialTransaction>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ReversalOfId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FinancialTransaction>().HasIndex(x => new { x.OrganizationId, x.BusinessDate, x.Id });
        b.Entity<FinancialTransaction>().HasIndex(x => new { x.OrganizationId, x.CreatedAt, x.Id });
        b.Entity<FinancialTransaction>().HasOne<FinancialAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AccountId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FinancialTransaction>().HasOne<FinancialAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DestinationAccountId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FinancialTransaction>().HasOne<Category>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CategoryId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AuditLog>().ToTable("audit_logs");
        b.Entity<AuditLog>().HasIndex(x => new { x.OrganizationId, x.CreatedAt, x.Id });
        b.Entity<AuditLog>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}



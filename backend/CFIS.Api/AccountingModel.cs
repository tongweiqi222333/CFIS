using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public class LedgerAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Asset";
    public string Currency { get; set; } = "CNY";
    public Guid? ParentId { get; set; }
    public bool Active { get; set; } = true;
}
public class FiscalPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Code { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = "Open";
    public long NextNumber { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
public class JournalEntry
{
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public Guid PeriodId { get; set; }
    public string? Number { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string Currency { get; set; } = "CNY";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public string ActorId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PostedAt { get; set; }
    public Guid? ReversalOfId { get; set; }
    public Guid? SourceTransactionId { get; set; }
    public Guid? SourceReceivableId { get; set; }
    public Guid? SourcePayableId { get; set; }
    public decimal DebitTotal { get; set; }
    public decimal CreditTotal { get; set; }
}
public class JournalLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid EntryId { get; set; }
    public Guid LedgerAccountId { get; set; }
    public int Position { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}
public class AccountingMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Kind { get; set; } = "";
    public Guid ResourceId { get; set; }
    public string Currency { get; set; } = "CNY";
    public Guid LedgerAccountId { get; set; }
}
public static class AccountingModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<LedgerAccount>().ToTable("chart_of_accounts", t => { t.HasCheckConstraint("ck_ledger_kind", "\"Kind\" IN ('Asset','Liability','Equity','Revenue','Expense')"); t.HasCheckConstraint("ck_ledger_parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\""); });
        b.Entity<LedgerAccount>().HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.Entity<LedgerAccount>().HasIndex(x => new { x.OrganizationId, x.Code, x.Currency }).IsUnique();
        b.Entity<LedgerAccount>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LedgerAccount>().HasOne<LedgerAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ParentId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LedgerAccount>().Property(x => x.Code).HasMaxLength(30);
        b.Entity<LedgerAccount>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<LedgerAccount>().Property(x => x.Currency).HasMaxLength(3);
        b.Entity<FiscalPeriod>().ToTable("fiscal_periods", t => { t.HasCheckConstraint("ck_period_dates", "\"StartDate\" <= \"EndDate\""); t.HasCheckConstraint("ck_period_status", "\"Status\" IN ('Open','Closed')"); });
        b.Entity<FiscalPeriod>().HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.Entity<FiscalPeriod>().HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();
        b.Entity<FiscalPeriod>().Property(x => x.Code).HasMaxLength(20);
        b.Entity<FiscalPeriod>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<JournalEntry>().ToTable("journal_entries", t => { t.HasCheckConstraint("ck_journal_status", "\"Status\" IN ('Draft','Posted')"); t.HasCheckConstraint("ck_journal_totals", "\"DebitTotal\" >= 0 AND \"CreditTotal\" >= 0 AND (\"Status\" <> 'Posted' OR (\"DebitTotal\" = \"CreditTotal\" AND \"DebitTotal\" > 0))"); t.HasCheckConstraint("ck_journal_source", "num_nonnulls(\"SourceTransactionId\",\"SourceReceivableId\",\"SourcePayableId\") <= 1"); });
        b.Entity<JournalEntry>().HasIndex(x => new { x.OrganizationId, x.Currency, x.BusinessDate, x.Id }).HasDatabaseName("ix_journal_posted_date_cover").HasFilter("\"Status\" = 'Posted'");
        b.Entity<JournalLine>().HasIndex(x => new { x.OrganizationId, x.EntryId }).HasDatabaseName("ix_journal_lines_entry_cover").IncludeProperties(x => new { x.LedgerAccountId, x.Debit, x.Credit });
        b.Entity<JournalEntry>().HasAlternateKey(x => new { x.OrganizationId, x.Id });
        b.Entity<JournalEntry>().HasIndex(x => new { x.OrganizationId, x.RequestId }).IsUnique();
        b.Entity<JournalEntry>().HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
        b.Entity<JournalEntry>().HasIndex(x => new { x.OrganizationId, x.ReversalOfId }).IsUnique();
        b.Entity<JournalEntry>().HasIndex(x => new { x.OrganizationId, x.SourceTransactionId }).IsUnique();
        b.Entity<JournalEntry>().HasIndex(x => new { x.OrganizationId, x.SourceReceivableId }).IsUnique();
        b.Entity<JournalEntry>().HasIndex(x => new { x.OrganizationId, x.SourcePayableId }).IsUnique();
        b.Entity<JournalEntry>().HasIndex(x => new { x.OrganizationId, x.Status, x.BusinessDate, x.Id });
        b.Entity<JournalEntry>().HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PeriodId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<JournalEntry>().HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ReversalOfId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<JournalEntry>().HasOne<FinancialTransaction>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SourceTransactionId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<JournalEntry>().HasOne<Receivable>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SourceReceivableId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<JournalEntry>().HasOne<Payable>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SourcePayableId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<JournalEntry>().Property(x => x.Description).HasMaxLength(500);
        b.Entity<JournalEntry>().Property(x => x.Number).HasMaxLength(60);
        b.Entity<JournalEntry>().Property(x => x.DebitTotal).HasPrecision(19, 4);
        b.Entity<JournalEntry>().Property(x => x.CreditTotal).HasPrecision(19, 4);
        b.Entity<JournalLine>().ToTable("journal_lines", t => t.HasCheckConstraint("ck_line_sides", "(\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Credit\" > 0 AND \"Debit\" = 0)"));
        b.Entity<JournalLine>().HasIndex(x => new { x.OrganizationId, x.EntryId, x.Position }).IsUnique();
        b.Entity<JournalLine>().HasIndex(x => new { x.OrganizationId, x.LedgerAccountId, x.EntryId });
        b.Entity<JournalLine>().Property(x => x.Debit).HasPrecision(19, 4);
        b.Entity<JournalLine>().Property(x => x.Credit).HasPrecision(19, 4);
        b.Entity<JournalLine>().HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.EntryId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<JournalLine>().HasOne<LedgerAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LedgerAccountId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AccountingMapping>().ToTable("accounting_mappings");
        b.Entity<AccountingMapping>().HasIndex(x => new { x.OrganizationId, x.Kind, x.ResourceId, x.Currency }).IsUnique();
        b.Entity<AccountingMapping>().HasOne<LedgerAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LedgerAccountId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

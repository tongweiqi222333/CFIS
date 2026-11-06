using System.Globalization;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public record AllocationInput(Guid CategoryId, string Amount);
public sealed class TransactionAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid CategoryId { get; set; }
    public decimal Amount { get; set; }
}
// Read model includes legacy single-category entries, so history never needs rewriting.
public sealed class CategoryTransaction
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? CategoryId { get; set; }
    public string Kind { get; set; } = "";
    public decimal Amount { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public static class Allocations
{
    public static bool Parse(PostingInput input, decimal total, out List<TransactionAllocation> rows)
    {
        rows = [];
        if (input.Allocations == null || input.Allocations.Length == 0)
            return true;
        if (input.Kind is not ("Income" or "Expense") || input.ReceivableId != null || input.PayableId != null || input.Allocations.Length is < 2 or > 100 || input.Allocations.Any(x => x == null) || input.Allocations.Select(x => x.CategoryId).Distinct().Count() != input.Allocations.Length)
            return false;
        foreach (var line in input.Allocations)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(line.Amount ?? "", @"^[0-9]{1,15}(\.[0-9]{1,4})?$") || !decimal.TryParse(line.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0 || amount > Finance.Maximum)
                return false;
            rows.Add(new()
            {
                CategoryId = line.CategoryId,
                Amount = amount
            });
        }
        return rows.Sum(x => x.Amount) == total && rows.Any(x => x.CategoryId == input.CategoryId);
    }
    public static void Configure(ModelBuilder b)
    {
        b.Entity<TransactionAllocation>().ToTable("transaction_allocations", t => t.HasCheckConstraint("ck_allocation_amount", "\"Amount\">0 AND \"Amount\"<>'NaN'::numeric"));
        b.Entity<TransactionAllocation>().Property(x => x.Amount).HasPrecision(19, 4);
        b.Entity<TransactionAllocation>().HasIndex(x => new { x.OrganizationId, x.TransactionId, x.CategoryId }).IsUnique();
        b.Entity<TransactionAllocation>().HasOne<FinancialTransaction>().WithMany().HasForeignKey(x => new { x.OrganizationId, Id = x.TransactionId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TransactionAllocation>().HasOne<Category>().WithMany().HasForeignKey(x => new { x.OrganizationId, Id = x.CategoryId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CategoryTransaction>().HasNoKey().ToView("category_transactions");
        b.Entity<CategoryTransaction>().Property(x => x.Amount).HasPrecision(19, 4);
    }
}

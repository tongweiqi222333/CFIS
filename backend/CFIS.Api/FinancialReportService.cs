using System.Globalization;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public record ReportFilter(DateOnly DateFrom, DateOnly DateTo, string Currency, Guid? AccountId = null, DateTimeOffset? KnowledgeAt = null);
public record CashRow(DateOnly Date, string Kind, decimal Amount);
public record ReportMoney(string Opening, string PeriodOpening, string Income, string Expense, string TransferIn, string TransferOut, string Closing, string AllRegistered);
public record TrendRow(string Date, string Income, string Expense, string Net, string PeriodOpening, string TransferIn, string TransferOut, string CashNet);
public record AgingRow(string Type, string Bucket, string Amount);
public record BudgetReportRow(Guid Id, string Name, string Status, Guid? PreviousVersionId, DateTimeOffset? ActivatedAt, DateTimeOffset? ClosedAt, string Planned, string Actual);
public record CategoryReportRow(Guid Id, string Name, string Kind, string Amount);
public record FinancialReport(string Organization, string Currency, Guid? AccountId, DateOnly DateFrom, DateOnly DateTo, DateTimeOffset GeneratedAt, DateTimeOffset KnowledgeAt, string Basis, ReportMoney Summary, List<TrendRow> Trend, List<AgingRow> Aging, List<BudgetReportRow> Budgets, List<CategoryReportRow> Categories);
public sealed class FinancialReportService(AppDb db)
{
    static string Money(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
    public static bool Valid(ReportFilter f) => f.DateFrom != default && f.DateTo >= f.DateFrom && f.DateTo.DayNumber - f.DateFrom.DayNumber <= 730 && System.Text.RegularExpressions.Regex.IsMatch(f.Currency ?? "", "^[A-Z]{3}$");
    public async Task<FinancialReport> Build(Guid org, ReportFilter f)
    {
        if (!Valid(f))
            throw new ArgumentException("报表日期范围最多731天，须指定三位币种");
        var now = DateTimeOffset.UtcNow;
        var known = (f.KnowledgeAt ?? now).ToUniversalTime();
        if (known > now)
            throw new ArgumentException("记录截止时间不能晚于当前时间");
        await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        if (f.KnowledgeAt != null)
        {
            var boundary = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT started_at AS \"Value\" FROM report_history_boundary").SingleAsync();
            if (known < boundary)
                throw new ArgumentException("所选记录时间早于完整历史追踪起点");
        }
        var accounts = db.Set<FinancialAccount>().AsNoTracking().Where(x => x.OrganizationId == org && x.Currency == f.Currency);
        if (f.AccountId != null)
        {
            accounts = accounts.Where(x => x.Id == f.AccountId);
            if (!await accounts.AnyAsync())
                throw new KeyNotFoundException("账户不存在或币种不符");
        }
        var ids = await accounts.Select(x => x.Id).ToArrayAsync();
        var txs = db.Set<FinancialTransaction>().AsNoTracking().Where(x => x.OrganizationId == org && x.CreatedAt <= known);
        var source = txs.Where(x => ids.Contains(x.AccountId));
        var sourceSum = await source.GroupBy(x => 1).Select(g => new { Opening = g.Sum(x => x.BusinessDate < f.DateFrom ? (x.Kind == "Income" || x.Kind == "Opening" ? x.Amount : -x.Amount) : 0), Closing = g.Sum(x => x.BusinessDate <= f.DateTo ? (x.Kind == "Income" || x.Kind == "Opening" ? x.Amount : -x.Amount) : 0), All = g.Sum(x => x.Kind == "Income" || x.Kind == "Opening" ? x.Amount : -x.Amount) }).SingleOrDefaultAsync();
        var incoming = txs.Where(x => x.Kind == "Transfer" && ids.Contains(x.DestinationAccountId!.Value));
        var incomingSum = await incoming.GroupBy(x => 1).Select(g => new { Opening = g.Sum(x => x.BusinessDate < f.DateFrom ? x.Amount : 0), Closing = g.Sum(x => x.BusinessDate <= f.DateTo ? x.Amount : 0), All = g.Sum(x => x.Amount) }).SingleOrDefaultAsync();
        var period = from t in source join original in db.Set<FinancialTransaction>() on new { t.OrganizationId, Id = t.ReversalOfId } equals new { original.OrganizationId, Id = (Guid?)original.Id } into joined from original in joined.DefaultIfEmpty() where t.BusinessDate >= f.DateFrom && t.BusinessDate <= f.DateTo select new { t.BusinessDate, Kind = original == null ? t.Kind : original.Kind, Signed = t.ReversalOfId == null ? t.Amount : -t.Amount, t.DestinationAccountId };
        var days = await period.GroupBy(x => x.BusinessDate).Select(g => new { Date = g.Key, Income = g.Sum(x => x.Kind == "Income" ? x.Signed : 0), Expense = g.Sum(x => x.Kind == "Expense" ? x.Signed : 0), Opening = g.Sum(x => x.Kind == "Opening" ? x.Signed : 0) }).ToDictionaryAsync(x => x.Date);
        var transfersOut = await source.Where(x => x.Kind == "Transfer" && x.BusinessDate >= f.DateFrom && x.BusinessDate <= f.DateTo && !ids.Contains(x.DestinationAccountId!.Value)).SumAsync(x => (decimal?)x.Amount) ?? 0;
        var transfersIn = await incoming.Where(x => x.BusinessDate >= f.DateFrom && x.BusinessDate <= f.DateTo && !ids.Contains(x.AccountId)).SumAsync(x => (decimal?)x.Amount) ?? 0;
        var opening = (sourceSum?.Opening ?? 0) + (incomingSum?.Opening ?? 0);
        var closing = (sourceSum?.Closing ?? 0) + (incomingSum?.Closing ?? 0);
        var income = days.Values.Sum(x => x.Income);
        var expense = days.Values.Sum(x => x.Expense);
        var openingEntries = days.Values.Sum(x => x.Opening);
        // Keep opening entries separate from the true start-of-period balance.
        var summary = new ReportMoney(Money(opening), Money(openingEntries), Money(income), Money(expense), Money(transfersIn), Money(transfersOut), Money(closing), Money((sourceSum?.All ?? 0) + (incomingSum?.All ?? 0)));
        var dailyIn = await incoming.Where(x => x.BusinessDate >= f.DateFrom && x.BusinessDate <= f.DateTo && !ids.Contains(x.AccountId)).GroupBy(x => x.BusinessDate).Select(g => new { Date = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Date, x => x.Amount);
        var dailyOut = await source.Where(x => x.Kind == "Transfer" && x.BusinessDate >= f.DateFrom && x.BusinessDate <= f.DateTo && !ids.Contains(x.DestinationAccountId!.Value)).GroupBy(x => x.BusinessDate).Select(g => new { Date = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Date, x => x.Amount);
        var trends = new List<TrendRow>();
        for (var d = f.DateFrom; d <= f.DateTo; d = d.AddDays(1))
        {
            var day = days.GetValueOrDefault(d);
            trends.Add(new(d.ToString("yyyy-MM-dd"), Money(day?.Income ?? 0), Money(day?.Expense ?? 0), Money((day?.Income ?? 0) - (day?.Expense ?? 0)), Money(day?.Opening ?? 0), Money(dailyIn.GetValueOrDefault(d)), Money(dailyOut.GetValueOrDefault(d)), Money((day?.Income ?? 0) - (day?.Expense ?? 0) + (day?.Opening ?? 0) + dailyIn.GetValueOrDefault(d) - dailyOut.GetValueOrDefault(d))));
            if (d == DateOnly.MaxValue)
                break;
        }
        var aging = new List<AgingRow>();
        if (f.AccountId == null)
        {
            await Aging<Receivable>(org, f, known, true, aging);
            await Aging<Payable>(org, f, known, false, aging);
        }
        var budgets = new List<BudgetReportRow>();
        if (f.AccountId == null)
        {
            var active = await db.Set<Budget>().AsNoTracking().Where(x => x.OrganizationId == org && x.Currency == f.Currency && x.CreatedAt <= known && x.ActivatedAt != null && x.ActivatedAt <= known && (x.ClosedAt == null || x.ClosedAt > known) && x.StartDate <= f.DateTo && x.EndDate >= f.DateFrom).OrderBy(x => x.Id).Take(101).ToListAsync();
            if (active.Count > 100)
                throw new ArgumentException("启用预算超过100份，请缩小范围");

            var budgetIds = active.Select(x => x.Id).ToArray();
            var plans = await db.Set<BudgetItem>().Where(x => x.OrganizationId == org && budgetIds.Contains(x.BudgetId)).GroupBy(x => x.BudgetId).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount);
            var execution = await (from item in db.Set<BudgetItem>() join budget in db.Set<Budget>() on new { item.OrganizationId, Id = item.BudgetId } equals new { budget.OrganizationId, budget.Id } join t in db.Set<CategoryTransaction>().Where(x => x.OrganizationId == org && x.CreatedAt <= known && ids.Contains(x.AccountId)) on new { item.OrganizationId, Id = item.CategoryId } equals new { t.OrganizationId, Id = t.CategoryId!.Value } where item.OrganizationId == org && budgetIds.Contains(budget.Id) && t.BusinessDate >= budget.StartDate && t.BusinessDate <= budget.EndDate && t.BusinessDate >= f.DateFrom && t.BusinessDate <= f.DateTo select new { budget.Id, Amount = t.Kind == "Expense" ? t.Amount : -t.Amount }).GroupBy(x => x.Id).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount);
            foreach (var b in active)
                budgets.Add(new(b.Id, b.Name, "Active", b.PreviousVersionId, b.ActivatedAt, b.ClosedAt <= known ? b.ClosedAt : null, Money(plans.GetValueOrDefault(b.Id)), Money(execution.GetValueOrDefault(b.Id))));
        }
        var categoryTotals = await (from t in db.Set<CategoryTransaction>() join c in db.Set<Category>() on new { t.OrganizationId, Id = t.CategoryId } equals new { c.OrganizationId, Id = (Guid?)c.Id } where t.OrganizationId == org && ids.Contains(t.AccountId) && t.CreatedAt <= known && t.BusinessDate >= f.DateFrom && t.BusinessDate <= f.DateTo group new { t, c } by new { c.Id, c.Name, c.Kind } into g select new { g.Key.Id, g.Key.Name, g.Key.Kind, Amount = g.Sum(x => x.t.Kind == x.c.Kind ? x.t.Amount : -x.t.Amount) }).OrderBy(x => x.Id).Take(5001).ToListAsync();
        if (categoryTotals.Count > 5000)
            throw new ArgumentException("分类统计最多5000项，请缩小期间");
        var categoryReport = categoryTotals.Select(x => new CategoryReportRow(x.Id, x.Name, x.Kind, Money(x.Amount))).ToList();
        var name = await db.Organizations.Where(x => x.Id == org).Select(x => x.Name).SingleAsync();
        await snapshot.CommitAsync();
        return new(name, f.Currency, f.AccountId, f.DateFrom, f.DateTo, now, known, "CashMovement; period opening entries shown separately; aging is organization-wide", summary, trends, aging, budgets, categoryReport);
    }
    async Task Aging<T>(Guid org, ReportFilter f, DateTimeOffset known, bool incoming, List<AgingRow> result) where T : TradeDocument
    {
        var docs = db.Set<T>().AsNoTracking().Where(x => x.OrganizationId == org && x.Currency == f.Currency && x.CreatedAt <= known && x.IssueDate <= f.DateTo && (x.CancelledAt == null || x.CancelledAt > known));
        var balances = await docs.Select(d => new { d.DueDate, Remaining = d.Amount - (db.Set<FinancialTransaction>().Where(t => t.OrganizationId == org && t.CreatedAt <= known && t.BusinessDate <= f.DateTo && (incoming ? t.ReceivableId == d.Id : t.PayableId == d.Id)).Sum(t => (decimal?)(incoming ? (t.Kind == "Income" ? t.Amount : -t.Amount) : (t.Kind == "Expense" ? t.Amount : -t.Amount))) ?? 0) }).Where(x => x.Remaining > 0).GroupBy(x => x.DueDate).Select(g => new { Date = g.Key, Amount = g.Sum(x => x.Remaining) }).ToListAsync();
        string[] buckets = ["NotDue", "1-30", "31-60", "61-90", "90+"];
        var amounts = new decimal[5];
        foreach (var d in balances)
        {
            var age = f.DateTo.DayNumber - d.Date.DayNumber;
            amounts[age <= 0 ? 0 : age <= 30 ? 1 : age <= 60 ? 2 : age <= 90 ? 3 : 4] += d.Amount;
        }
        for (var i = 0; i < 5; i++)
            result.Add(new(incoming ? "Receivable" : "Payable", buckets[i], Money(amounts[i])));
    }
}

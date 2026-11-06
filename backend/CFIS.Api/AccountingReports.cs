using Microsoft.EntityFrameworkCore;
using static CFIS.Api.Finance;
namespace CFIS.Api;

public sealed class AccountingReports(AppDb db)
{
    static void Dates(DateOnly from, DateOnly to)
    {
        if (from == default || to < from || to.DayNumber - from.DayNumber > 3660)
            throw new AccountingFault(400, "报表日期无效或超过3661天");
    }
    static void Currency(string value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^[A-Z]{3}$"))
            throw new AccountingFault(400, "币种无效");
    }
    public async Task<object> Trial(Guid org, DateOnly from, DateOnly to, string currency)
    {
        Dates(from, to);
        Currency(currency);
        await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var q = from l in db.Set<JournalLine>() join e in db.Set<JournalEntry>() on new { l.OrganizationId, Id = l.EntryId } equals new { e.OrganizationId, e.Id } where l.OrganizationId == org && e.Status == "Posted" && e.Currency == currency && e.BusinessDate <= to select new { l.LedgerAccountId, l.Debit, l.Credit, e.BusinessDate };
        var balances = await q.GroupBy(x => x.LedgerAccountId).Select(g => new { Id = g.Key, Opening = g.Sum(x => x.BusinessDate < from ? x.Debit - x.Credit : 0), Debit = g.Sum(x => x.BusinessDate >= from ? x.Debit : 0), Credit = g.Sum(x => x.BusinessDate >= from ? x.Credit : 0) }).ToDictionaryAsync(x => x.Id);
        var accounts = await db.Set<LedgerAccount>().AsNoTracking().Where(x => x.OrganizationId == org && x.Currency == currency).OrderBy(x => x.Code).ThenBy(x => x.Id).Take(5001).ToListAsync();
        if (accounts.Count > 5000)
            throw new AccountingFault(400, "试算表最多5000科目");
        var items = accounts.Select(a => { var b = balances.GetValueOrDefault(a.Id); var close = (b?.Opening ?? 0) + (b?.Debit ?? 0) - (b?.Credit ?? 0); return new { a.Id, a.Code, a.Name, a.Kind, a.ParentId, openingNetDebit = Money(b?.Opening ?? 0), debit = Money(b?.Debit ?? 0), credit = Money(b?.Credit ?? 0), closingNetDebit = Money(close), normalBalance = Money(a.Kind is "Asset" or "Expense" ? close : -close) }; }).ToList();
        var revenue = accounts.Where(x => x.Kind == "Revenue").Sum(x => { var b = balances.GetValueOrDefault(x.Id); return (b?.Credit ?? 0) - (b?.Debit ?? 0); });
        var expense = accounts.Where(x => x.Kind == "Expense").Sum(x => { var b = balances.GetValueOrDefault(x.Id); return (b?.Debit ?? 0) - (b?.Credit ?? 0); });
        var debit = balances.Values.Sum(x => x.Debit);
        var credit = balances.Values.Sum(x => x.Credit);
        await snapshot.CommitAsync();
        return new
        {
            currency,
            dateFrom = from,
            dateTo = to,
            debit = Money(debit),
            credit = Money(credit),
            difference = Money(debit - credit),
            revenue = Money(revenue),
            expense = Money(expense),
            profitBeforeClosing = Money(revenue - expense),
            items
        };
    }
    public async Task<object> Ledger(Guid org, Guid id, DateOnly from, DateOnly to, int page, int pageSize)
    {
        Dates(from, to);
        if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100)
            throw new AccountingFault(400, "分页无效");
        await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var account = await db.Set<LedgerAccount>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id) ?? throw new AccountingFault(404, "科目不存在");
        var q = from l in db.Set<JournalLine>() join e in db.Set<JournalEntry>() on new { l.OrganizationId, Id = l.EntryId } equals new { e.OrganizationId, e.Id } where l.OrganizationId == org && l.LedgerAccountId == id && e.Status == "Posted" && e.BusinessDate <= to select new { e.Id, e.Number, e.BusinessDate, e.Description, l.Position, l.Debit, l.Credit };
        var opening = await q.Where(x => x.BusinessDate < from).SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0;
        var closing = await q.SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0;
        var period = q.Where(x => x.BusinessDate >= from);
        var total = await period.CountAsync();
        var rows = await period.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id).ThenBy(x => x.Position).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        await snapshot.CommitAsync();
        return new
        {
            account.Id,
            account.Code,
            account.Name,
            account.Currency,
            openingNetDebit = Money(opening),
            closingNetDebit = Money(closing),
            total,
            items = rows.Select(x => new { x.Id, x.Number, x.BusinessDate, x.Description, x.Position, debit = Money(x.Debit), credit = Money(x.Credit) })
        };
    }
    public async Task<object> Reconcile(Guid org, DateOnly to, string currency)
    {
        Dates(to, to);
        Currency(currency);
        await using var snapshot = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead) : null;
        var funds = await db.Set<FinancialAccount>().AsNoTracking().Where(x => x.OrganizationId == org && x.Currency == currency).ToListAsync();
        var ids = funds.Select(x => x.Id).ToArray();
        var cash = await db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org && ids.Contains(x.AccountId) && x.BusinessDate <= to).GroupBy(x => x.AccountId).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Kind == "Income" || x.Kind == "Opening" ? x.Amount : -x.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount);
        var transfers = await db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org && x.DestinationAccountId != null && ids.Contains(x.DestinationAccountId.Value) && x.BusinessDate <= to).GroupBy(x => x.DestinationAccountId!.Value).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount);
        var mappings = await db.Set<AccountingMapping>().Where(x => x.OrganizationId == org && x.Kind == "FinancialAccount" && x.Currency == currency && ids.Contains(x.ResourceId)).ToListAsync();
        var ledgerIds = mappings.Select(x => x.LedgerAccountId).Distinct().ToArray();
        var balances = await (from l in db.Set<JournalLine>() join e in db.Set<JournalEntry>() on new { l.OrganizationId, Id = l.EntryId } equals new { e.OrganizationId, e.Id } where l.OrganizationId == org && e.Status == "Posted" && e.BusinessDate <= to && ledgerIds.Contains(l.LedgerAccountId) select l).GroupBy(x => x.LedgerAccountId).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Debit - x.Credit) }).ToDictionaryAsync(x => x.Id, x => x.Amount);
        var pending = await db.Set<FinancialTransaction>().CountAsync(t => t.OrganizationId == org && ids.Contains(t.AccountId) && t.BusinessDate <= to && !db.Set<JournalEntry>().Any(e => e.OrganizationId == org && e.SourceTransactionId == t.Id && e.Status == "Posted"));
        var items = mappings.GroupBy(x => x.LedgerAccountId).Select(g => { var expected = g.Sum(x => cash.GetValueOrDefault(x.ResourceId) + transfers.GetValueOrDefault(x.ResourceId)); var actual = balances.GetValueOrDefault(g.Key); return new { ledgerAccountId = g.Key, financialAccounts = g.Select(x => x.ResourceId), cash = Money(expected), ledger = Money(actual), difference = Money(actual - expected) }; }).ToList();
        if (snapshot != null)
            await snapshot.CommitAsync();
        return new
        {
            currency,
            dateTo = to,
            pendingTransactions = pending,
            unmappedAccounts = ids.Except(mappings.Select(x => x.ResourceId)).Count(),
            items
        };
    }
}

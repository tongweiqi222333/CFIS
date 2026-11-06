using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public static partial class Finance
{
    static void MapReportingBasis(RouteGroupBuilder g)
    {
        g.MapGet("/historical-balance-warnings", async (Guid org, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权核对");
            var rows = await db.Database.SqlQuery<HistoricalBalanceWarning>($"""
 WITH movements AS (
 SELECT "OrganizationId", "AccountId" AS "AccountId", "BusinessDate", CASE WHEN "Kind" IN ('Income','Opening') THEN "Amount" ELSE -"Amount" END AS delta FROM transactions WHERE "OrganizationId"={org}
 UNION ALL SELECT "OrganizationId", "DestinationAccountId", "BusinessDate", "Amount" FROM transactions WHERE "OrganizationId"={org} AND "Kind"='Transfer'
 ), daily AS (SELECT "OrganizationId","AccountId","BusinessDate",sum(delta) AS delta FROM movements GROUP BY 1,2,3), balances AS (
 SELECT "AccountId","BusinessDate",sum(delta) OVER(PARTITION BY "AccountId" ORDER BY "BusinessDate") AS "Balance" FROM daily)
 SELECT "AccountId","BusinessDate","Balance" FROM balances WHERE "Balance"<0 ORDER BY "BusinessDate","AccountId" LIMIT 100
 """).ToListAsync();
            return Results.Ok(new
            {
                limit = 100,
                items = rows.Select(x => new { x.AccountId, x.BusinessDate, balance = Money(x.Balance) })
            });
        });
        g.MapGet("/reporting-basis", async (Guid org, ClaimsPrincipal u, AppDb db, DateOnly? dateFrom = null, DateOnly? dateTo = null) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问");
            if (dateFrom > dateTo)
                return Error(400, "日期范围无效");
            await using var snapshot = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var q = from t in db.Set<FinancialTransaction>().AsNoTracking()
                    join a in db.Set<FinancialAccount>() on new
                    {
                        t.OrganizationId,
                        Id = t.AccountId
                    } equals new
                    {
                        a.OrganizationId,
                        a.Id
                    }
                    join o in db.Set<FinancialTransaction>() on new
                    {
                        t.OrganizationId,
                        Id = t.ReversalOfId
                    } equals new
                    {
                        o.OrganizationId,
                        Id = (Guid?)o.Id
                    } into originals
                    from original in originals.DefaultIfEmpty()
                    where t.OrganizationId == org && (dateFrom == null || t.BusinessDate >= dateFrom) && (dateTo == null || t.BusinessDate <= dateTo)
                    select new
                    {
                        a.Currency,
                        OriginalKind = original == null ? t.Kind : original.Kind,
                        Signed = t.ReversalOfId == null ? t.Amount : -t.Amount
                    };
            var sums = await q.GroupBy(x => x.Currency).Select(g => new { currency = g.Key, income = g.Sum(x => x.OriginalKind == "Income" ? x.Signed : 0), expense = g.Sum(x => x.OriginalKind == "Expense" ? x.Signed : 0), opening = g.Sum(x => x.OriginalKind == "Opening" ? x.Signed : 0) }).OrderBy(x => x.currency).ToListAsync();
            var receivables = await db.Set<Receivable>().AsNoTracking().Where(x => x.OrganizationId == org && !x.Cancelled && (dateFrom == null || x.IssueDate >= dateFrom) && (dateTo == null || x.IssueDate <= dateTo)).GroupBy(x => x.Currency).Select(g => new { currency = g.Key, amount = g.Sum(x => x.Amount) }).ToListAsync();
            var payables = await db.Set<Payable>().AsNoTracking().Where(x => x.OrganizationId == org && !x.Cancelled && (dateFrom == null || x.IssueDate >= dateFrom) && (dateTo == null || x.IssueDate <= dateTo)).GroupBy(x => x.Currency).Select(g => new { currency = g.Key, amount = g.Sum(x => x.Amount) }).ToListAsync();
            await snapshot.CommitAsync();
            return Results.Ok(new
            {
                basis = "CashMovement",
                dateFrom,
                dateTo,
                documentOccurrences = new
                {
                    receivables = receivables.Select(x => new { x.currency, amount = Money(x.amount) }),
                    payables = payables.Select(x => new { x.currency, amount = Money(x.amount) })
                },
                items = sums.Select(x => new { x.currency, income = Money(x.income), expense = Money(x.expense), opening = Money(x.opening), netCashMovement = Money(x.income - x.expense + x.opening) })
            });
        });
    }
}

public class HistoricalBalanceWarning
{
    public Guid AccountId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public decimal Balance { get; set; }
}

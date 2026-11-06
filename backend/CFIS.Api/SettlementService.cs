using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public sealed class SettlementService(AppDb db)
{
    public async Task<TradeDocument?> LockDocument(Guid org, Guid? receivable, Guid? payable)
    {
        if (receivable != null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM receivables WHERE \"OrganizationId\"={org} AND \"Id\"={receivable} FOR UPDATE");
            return await db.Set<Receivable>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == receivable);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM payables WHERE \"OrganizationId\"={org} AND \"Id\"={payable} FOR UPDATE");
        return await db.Set<Payable>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == payable);
    }
    public async Task<decimal> Settled(Guid org, Guid? receivable, Guid? payable)
    {
        var q = db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org);
        return receivable != null ? await q.Where(x => x.ReceivableId == receivable).SumAsync(x => (decimal?)(x.Kind == "Income" ? x.Amount : -x.Amount)) ?? 0 : await q.Where(x => x.PayableId == payable).SumAsync(x => (decimal?)(x.Kind == "Expense" ? x.Amount : -x.Amount)) ?? 0;
    }

}

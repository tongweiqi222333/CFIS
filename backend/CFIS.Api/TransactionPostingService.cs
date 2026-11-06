using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using static CFIS.Api.Finance;
namespace CFIS.Api;

public sealed class TransactionPostingService(AppDb db)
{
    public async Task<IResult> Post(Guid org, PostingInput r, ClaimsPrincipal u, bool transfer)
    {
        var access = await Access(db, org, u, true);
        if (access != 200)
            return Error(access, "无权写入企业");
        if (r.BusinessDate == DateOnly.MinValue)
            return Error(400, "业务日期无效");
        if (r.RequestId == Guid.Empty || r.Description?.Length > 500 || r.Kind is not ("Income" or "Expense" or "Transfer" or "Opening") || transfer != (r.Kind == "Transfer"))
            return Error(400, "交易参数无效");
        if (r.Amount is null || !System.Text.RegularExpressions.Regex.IsMatch(r.Amount, @"^[0-9]{1,15}(\.[0-9]{1,4})?$") || !decimal.TryParse(r.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0 || amount > Maximum)
            return Error(400, "金额必须为正十进制字符串，最多15位整数和4位小数");
        if (transfer ? (r.DestinationAccountId is null || r.DestinationAccountId == r.AccountId || r.CategoryId != null) : (r.DestinationAccountId != null || (r.Kind == "Opening" ? r.CategoryId != null : r.CategoryId == null)))
            return Error(400, "收支须选择分类；转账须选择不同账户且不含分类");
        if (!Allocations.Parse(r, amount, out var allocations))
            return Error(400, "分摊限普通收入支出，分类须唯一、正金额且合计等于交易金额");
        await using var tx = await db.Database.BeginTransactionAsync();
        await AccountingService.LockFinancial(db, org);
        TradeDocument? document = null;
        if (r.ReceivableId != null && r.PayableId != null)
            return Error(400, "一笔结算只能关联一张单据");
        if (r.ReceivableId != null || r.PayableId != null)
        {
            document = await LockDocument(db, org, r.ReceivableId, r.PayableId);
            if (document is null)
                return Error(404, "单据不存在");
            if (r.Kind != (r.ReceivableId != null ? "Income" : "Expense") || r.CategoryId != document.CategoryId)
                return Error(400, "结算类型或分类与单据不符");
        }
        if (r.CorrectionOfId is Guid corrected)
        {
            await RequestLock(db, org, corrected);
            var original = await db.Set<FinancialTransaction>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == corrected);
            if (original is null)
                return Error(404, "更正原交易不存在");
            if (original.ReversalOfId != null || !await db.Set<FinancialTransaction>().AnyAsync(x => x.OrganizationId == org && x.ReversalOfId == corrected) || original.ReceivableId != r.ReceivableId || original.PayableId != r.PayableId)
                return Error(409, "更正须关联已撤销的原交易及相同单据");
        }
        // Lock accounts in deterministic UUID order to serialize balance changes and avoid opposing-transfer deadlocks.
        var ids = new[] { r.AccountId, r.DestinationAccountId ?? r.AccountId }.Distinct().Order().ToArray();
        var accounts = new List<FinancialAccount>();
        foreach (var id in ids)
        {
            var a = await db.Set<FinancialAccount>().FromSqlInterpolated($"SELECT * FROM financial_accounts WHERE \"OrganizationId\"={org} AND \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync();
            if (a is null)
                return Error(404, "账户不存在");
            accounts.Add(a);
        }
        var previous = await db.Set<FinancialTransaction>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.RequestId == r.RequestId);
        if (previous != null)
        {
            if (previous.CorrectionOfId != r.CorrectionOfId || previous.ReceivableId != r.ReceivableId || previous.PayableId != r.PayableId || previous.AccountId != r.AccountId || previous.DestinationAccountId != r.DestinationAccountId || previous.CategoryId != r.CategoryId || previous.Kind != r.Kind || previous.Amount != amount || previous.Description != (r.Description ?? "") || (r.BusinessDate != null && previous.BusinessDate != r.BusinessDate))
                return Error(409, "幂等请求标识已用于不同交易");
            var prior = await db.Set<TransactionAllocation>().Where(x => x.OrganizationId == org && x.TransactionId == previous.Id).OrderBy(x => x.CategoryId).Select(x => new { x.CategoryId, x.Amount }).ToListAsync();
            if (!prior.Select(x => (x.CategoryId, x.Amount)).SequenceEqual(allocations.OrderBy(x => x.CategoryId).Select(x => (x.CategoryId, x.Amount))))
                return Error(409, "请求标识对应的分摊已变化");
            return Results.Ok(TransactionDto(previous));
        }
        var postingDate = r.BusinessDate ?? await LocalToday(db, org);
        if (!await AccountingService.DateOpen(db, org, postingDate))
            return Error(409, "该业务日期不在开放会计期间");
        var source = accounts.Single(x => x.Id == r.AccountId);
        if (document != null)
        {
            if (await db.Set<JournalEntry>().AnyAsync(e => e.OrganizationId == org && e.Status == "Posted" && (r.ReceivableId != null ? e.SourceReceivableId == r.ReceivableId : e.SourcePayableId == r.PayableId) && db.Set<JournalEntry>().Any(rev => rev.OrganizationId == org && rev.ReversalOfId == e.Id && rev.Status == "Posted")))
                return Error(409, "单据会计确认已冲销，请取消重建单据");
            if (document.Cancelled || source.Currency != document.Currency)
                return Error(409, "单据已取消或账户币种不符");
            if (r.BusinessDate == null || r.BusinessDate < document.IssueDate)
                return Error(400, "结算须提供不早于单据日期的业务日期");
            var settled = await Settled(db, org, r.ReceivableId, r.PayableId);
            if (amount > document.Amount - settled)
                return Error(409, "结算金额超过未结余额");
        }
        if (accounts.Any(x => x.Status != "Active"))
            return Error(409, "账户已停用或归档");
        if (!transfer && r.Kind != "Opening" && allocations.Count == 0)
        {
            var category = await db.Set<Category>().FromSqlInterpolated($"SELECT * FROM categories WHERE \"OrganizationId\"={org} AND \"Id\"={r.CategoryId} FOR UPDATE").SingleOrDefaultAsync();
            if (category is null || category.Kind != r.Kind || category.Status != "Active")
                return Error(400, "分类不存在、停用或与收支类型不符");
        }
        foreach (var line in allocations.OrderBy(x => x.CategoryId))
        {
            var category = await db.Set<Category>().FromSqlInterpolated($"SELECT * FROM categories WHERE \"OrganizationId\"={org} AND \"Id\"={line.CategoryId} FOR UPDATE").SingleOrDefaultAsync();
            if (category == null || category.Kind != r.Kind || category.Status != "Active")
                return Error(400, "分摊分类须为本企业有效同方向分类");
        }
        if (r.Kind == "Opening")
        {
            if (string.IsNullOrWhiteSpace(r.Description))
                return Error(400, "期初余额须记录来源说明");
            var history = db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org && (x.AccountId == source.Id || x.DestinationAccountId == source.Id));
            if (await history.AnyAsync(x => x.Kind == "Opening" && !db.Set<FinancialTransaction>().Any(y => y.OrganizationId == org && y.ReversalOfId == x.Id)) ||
            await history.AnyAsync(x => x.Kind != "Opening" && !(x.ReversalOfId != null && db.Set<FinancialTransaction>().Any(y => y.OrganizationId == org && y.Id == x.ReversalOfId && y.Kind == "Opening"))))
                return Error(409, "期初余额须在业务发生前登记；仅已撤销的期初记录可重新登记");
        }
        var destination = transfer ? accounts.Single(x => x.Id == r.DestinationAccountId) : null;
        if (destination != null && source.Currency != destination.Currency)
            return Error(400, "Phase 2 仅支持同币种转账");
        if (r.Kind is not ("Income" or "Opening") && source.Balance < amount)
            return Error(409, "账户余额不足");
        if (r.Kind is ("Income" or "Opening") && source.Balance > Maximum - amount || destination != null && destination.Balance > Maximum - amount)
            return Error(409, "账户余额超过上限");
        source.Balance += r.Kind is ("Income" or "Opening") ? amount : -amount;
        if (destination != null)
            destination.Balance += amount;
        var t = new FinancialTransaction { CorrectionOfId = r.CorrectionOfId, ReceivableId = r.ReceivableId, PayableId = r.PayableId, OrganizationId = org, RequestId = r.RequestId, Kind = r.Kind, AccountId = r.AccountId, DestinationAccountId = r.DestinationAccountId, CategoryId = r.CategoryId, Amount = amount, Description = r.Description ?? "", ActorId = Uid(u), BusinessDate = r.BusinessDate ?? await LocalToday(db, org) };
        db.Add(t);
        foreach (var line in allocations)
        {
            line.OrganizationId = org;
            line.TransactionId = t.Id;
            db.Add(line);
        }
        db.Add(Audit(org, u, "TransactionPosted", t.Id, $"{t.Kind};amount={Money(amount)};currency={source.Currency};from={source.Id};to={destination?.Id}"));
        try
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Error(409, "请求标识冲突，请查询交易后重试"); }
        return Results.Created($"/api/v1/organizations/{org}/transactions", TransactionDto(t));

    }
}

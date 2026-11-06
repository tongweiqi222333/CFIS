using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public record ResourceChange(string Name, string Status);
public record ReverseInput(Guid RequestId, string Reason, DateOnly BusinessDate);
public static partial class Finance
{
    internal static async Task<DateOnly> LocalToday(AppDb db, Guid org)
    {
        var zone = await db.Organizations.Where(x => x.Id == org).Select(x => x.TimeZone).SingleAsync();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
    }
    static bool ValidChange(ResourceChange r) => !string.IsNullOrWhiteSpace(r.Name) && r.Name.Length <= 200 && r.Status is "Active" or "Inactive" or "Archived";
    static void MapExtensions(RouteGroupBuilder g)
    {
        g.MapGet("/finance-settings", async (Guid org, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问");
            return Results.Ok(new
            {
                timeZone = await db.Organizations.Where(x => x.Id == org).Select(x => x.TimeZone).SingleAsync(),
                today = await LocalToday(db, org),
                permissions = new
                {
                    write = await Access(db, org, u, true) == 200
                }
            });
        });
        g.MapPatch("/accounts/{id:guid}", async (Guid org, Guid id, ResourceChange r, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权修改");
            if (!ValidChange(r))
                return Error(400, "名称或状态无效");
            await using var tx = await db.Database.BeginTransactionAsync();
            var a = await db.Set<FinancialAccount>().FromSqlInterpolated($"SELECT * FROM financial_accounts WHERE \"OrganizationId\"={org} AND \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync();
            if (a is null)
                return Error(404, "账户不存在");
            if (r.Status == "Archived" && a.Balance != 0)
                return Error(409, "归档账户余额必须为零");
            var before = new
            {
                a.Name,
                a.Status
            };
            a.Name = r.Name.Trim();
            a.Status = r.Status;
            db.Add(Audit(org, u, "AccountChanged", id, JsonSerializer.Serialize(new
            {
                before,
                after = new
                {
                    a.Name,
                    a.Status
                }
            })));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
        g.MapPatch("/categories/{id:guid}", async (Guid org, Guid id, ResourceChange r, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权修改");
            if (!ValidChange(r))
                return Error(400, "名称或状态无效");
            await using var tx = await db.Database.BeginTransactionAsync();
            var c = await db.Set<Category>().FromSqlInterpolated($"SELECT * FROM categories WHERE \"OrganizationId\"={org} AND \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync();
            if (c is null)
                return Error(404, "分类不存在");
            var before = new
            {
                c.Name,
                c.Status
            };
            c.Name = r.Name.Trim();
            c.Status = r.Status;
            db.Add(Audit(org, u, "CategoryChanged", id, JsonSerializer.Serialize(new
            {
                before,
                after = new
                {
                    c.Name,
                    c.Status
                }
            })));
            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Error(409, "分类名称重复"); }
            return Results.NoContent();
        });
        g.MapGet("/reconciliation", async (Guid org, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权核对");
            // A consistent snapshot prevents false differences while concurrent postings commit.
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            var accounts = await db.Set<FinancialAccount>().AsNoTracking().Where(x => x.OrganizationId == org).OrderBy(x => x.Id).ToListAsync();
            var debits = await db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org).GroupBy(x => x.AccountId).Select(x => new { Id = x.Key, Amount = x.Sum(t => t.Kind == "Income" || t.Kind == "Opening" ? t.Amount : -t.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount);
            var credits = await db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org && x.DestinationAccountId != null).GroupBy(x => x.DestinationAccountId!.Value).Select(x => new { Id = x.Key, Amount = x.Sum(t => t.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount);
            var result = accounts.Select(a => new { a.Id, a.Name, a.Currency, actual = Money(a.Balance), expected = Money(debits.GetValueOrDefault(a.Id) + credits.GetValueOrDefault(a.Id)), difference = Money(a.Balance - debits.GetValueOrDefault(a.Id) - credits.GetValueOrDefault(a.Id)) }).ToList();
            await tx.CommitAsync();
            return Results.Ok(result);
        });
        g.MapGet("/transactions/{id:guid}", async (Guid org, Guid id, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权查看");
            var row = await db.Set<FinancialTransaction>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id);
            if (row is null)
                return Error(404, "流水不存在");
            var source = await db.Set<FinancialAccount>().Where(x => x.OrganizationId == org && x.Id == row.AccountId).Select(x => new { x.Name, x.Currency }).SingleAsync();
            var destination = row.DestinationAccountId is null ? null : await db.Set<FinancialAccount>().Where(x => x.OrganizationId == org && x.Id == row.DestinationAccountId).Select(x => x.Name).SingleAsync();
            var category = row.CategoryId is null ? null : await db.Set<Category>().Where(x => x.OrganizationId == org && x.Id == row.CategoryId).Select(x => x.Name).SingleAsync();
            var actor = await db.Users.Where(x => x.Id == row.ActorId).Select(x => x.Email).SingleOrDefaultAsync();
            var reversal = await db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org && x.ReversalOfId == id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
            var correctedBy = await db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org && x.CorrectionOfId == id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
            var allocations = await db.Set<TransactionAllocation>().Where(x => x.OrganizationId == org && x.TransactionId == id).Select(x => new { x.CategoryId, amount = Money(x.Amount) }).ToListAsync();
            return Results.Ok(new
            {
                allocations,
                correctedBy,
                transaction = TransactionDto(row),
                accountName = source.Name,
                source.Currency,
                destinationName = destination,
                categoryName = category,
                actor,
                reversedBy = reversal
            });
        });
        g.MapPost("/transactions/{id:guid}/reverse", Reverse);
    }
    static async Task<IResult> Reverse(Guid org, Guid id, ReverseInput r, ClaimsPrincipal u, AppDb db)
    {
        var access = await Access(db, org, u, true);
        if (access != 200)
            return Error(access, "无权撤销");
        if (r.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500 || r.BusinessDate == default)
            return Error(400, "撤销须提供请求ID、日期与原因");
        await using var tx = await db.Database.BeginTransactionAsync();
        await AccountingService.LockFinancial(db, org);
        // Advisory lock serializes reversals without granting UPDATE on immutable transactions.
        var lockKey = org.ToString("N") + id.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey},0))");
        var original = await db.Set<FinancialTransaction>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id);
        if (original is null)
            return Error(404, "原交易不存在");
        if (original.ReversalOfId != null)
            return Error(409, "不能撤销撤销流水");
        var previous = await db.Set<FinancialTransaction>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.RequestId == r.RequestId);
        if (previous != null)
            return previous.ReversalOfId == id && previous.ReversalReason == r.Reason.Trim() && previous.BusinessDate == r.BusinessDate ? Results.Ok(TransactionDto(previous)) : Error(409, "请求ID冲突");
        if (await db.Set<FinancialTransaction>().AnyAsync(x => x.OrganizationId == org && x.ReversalOfId == id))
            return Error(409, "原交易已撤销");
        if (!await AccountingService.DateOpen(db, org, r.BusinessDate))
            return Error(409, "撤销日期不在开放会计期间");
        if (r.BusinessDate < original.BusinessDate)
            return Error(400, "撤销日期不得早于原交易");
        if (original.ReceivableId != null || original.PayableId != null)
            await LockDocument(db, org, original.ReceivableId, original.PayableId);
        var accounts = new List<FinancialAccount>();
        foreach (var aid in new[] { original.AccountId, original.DestinationAccountId ?? original.AccountId }.Distinct().Order())
        {
            var a = await db.Set<FinancialAccount>().FromSqlInterpolated($"SELECT * FROM financial_accounts WHERE \"OrganizationId\"={org} AND \"Id\"={aid} FOR UPDATE").SingleAsync();
            accounts.Add(a);
        }
        if (accounts.Any(x => x.Status == "Archived"))
            return Error(409, "请先重新启用归档账户");
        var transfer = original.Kind == "Transfer";
        var source = accounts.Single(x => x.Id == (transfer ? original.DestinationAccountId : original.AccountId));
        var dest = transfer ? accounts.Single(x => x.Id == original.AccountId) : null;
        var inverseKind = transfer ? "Transfer" : original.Kind == "Expense" ? "Income" : "Expense";
        var amount = original.Amount;
        if (inverseKind != "Income" && source.Balance < amount)
            return Error(409, "撤销余额不足");
        if (inverseKind == "Income" && source.Balance > Maximum - amount || dest != null && dest.Balance > Maximum - amount)
            return Error(409, "撤销超出余额上限");
        source.Balance += inverseKind == "Income" ? amount : -amount;
        if (dest != null)
            dest.Balance += amount;
        var reversal = new FinancialTransaction { ReceivableId = original.ReceivableId, PayableId = original.PayableId, OrganizationId = org, RequestId = r.RequestId, AccountId = source.Id, DestinationAccountId = dest?.Id, CategoryId = original.CategoryId, Kind = inverseKind, Amount = amount, ReversalOfId = id, ReversalReason = r.Reason.Trim(), Description = "撤销：" + r.Reason.Trim(), ActorId = Uid(u), BusinessDate = r.BusinessDate };
        if (reversal.Description.Length > 500)
            reversal.Description = reversal.Description[..500];
        db.Add(reversal);
        foreach (var line in await db.Set<TransactionAllocation>().AsNoTracking().Where(x => x.OrganizationId == org && x.TransactionId == id).ToListAsync())
            db.Add(new TransactionAllocation { OrganizationId = org, TransactionId = reversal.Id, CategoryId = line.CategoryId, Amount = line.Amount });
        db.Add(Audit(org, u, "TransactionReversed", reversal.Id, JsonSerializer.Serialize(new
        {
            original = id,
            reason = r.Reason.Trim(),
            before = TransactionDto(original),
            after = TransactionDto(reversal)
        })));
        try
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Error(409, "并发撤销或请求ID冲突"); }
        return Results.Created($"/api/v1/organizations/{org}/transactions/{reversal.Id}", TransactionDto(reversal));
    }
}



using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public record BatchSource(Guid RequestId, string SourceKind, Guid SourceId);
public record BatchPost(Guid Id, Guid Version);
public static class AccountingWorkspace
{
    sealed record Blocker(Guid Id, DateOnly? Date, string Description);
    public static void MapAccountingWorkspace(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/organizations/{org:guid}/accounting").RequireAuthorization();
        g.MapGet("/periods/{id:guid}/blockers", async (Guid org, Guid id, string kind, ClaimsPrincipal user, AppDb db, int page = 1, int pageSize = 20) =>
        {
            var access = await Finance.Access(db, org, user);
            if (access != 200)
                return Finance.Error(access, "无权访问");
            if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100)
                return Finance.Error(400, "分页无效");
            var period = await db.Set<FiscalPeriod>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id);
            if (period == null)
                return Results.NotFound();
            var entries = db.Set<JournalEntry>().Where(x => x.OrganizationId == org);
            IQueryable<Blocker> query;
            if (kind == "Transaction")
                query = db.Set<FinancialTransaction>().Where(t => t.OrganizationId == org && t.BusinessDate <= period.EndDate && !entries.Any(e => e.SourceTransactionId == t.Id && e.Status == "Posted")).OrderBy(t => t.Id).Select(t => new Blocker(t.Id, t.BusinessDate, t.Description ?? ""));
            else if (kind == "Receivable")
                query = db.Set<Receivable>().Where(d => d.OrganizationId == org && !d.Cancelled && d.IssueDate <= period.EndDate && !entries.Any(e => e.SourceReceivableId == d.Id && e.Status == "Posted" && !entries.Any(r => r.ReversalOfId == e.Id && r.Status == "Posted"))).OrderBy(d => d.Id).Select(d => new Blocker(d.Id, d.IssueDate, d.Number));
            else if (kind == "Payable")
                query = db.Set<Payable>().Where(d => d.OrganizationId == org && !d.Cancelled && d.IssueDate <= period.EndDate && !entries.Any(e => e.SourcePayableId == d.Id && e.Status == "Posted" && !entries.Any(r => r.ReversalOfId == e.Id && r.Status == "Posted"))).OrderBy(d => d.Id).Select(d => new Blocker(d.Id, d.IssueDate, d.Number));
            else if (kind == "Draft")
                query = entries.Where(e => e.PeriodId == id && e.Status == "Draft").OrderBy(e => e.Id).Select(e => new Blocker(e.Id, e.BusinessDate, e.Description));
            else if (kind == "Unmapped")
                query = db.Set<FinancialAccount>().Where(a => a.OrganizationId == org && !db.Set<AccountingMapping>().Any(m => m.OrganizationId == org && m.Kind == "FinancialAccount" && m.ResourceId == a.Id && m.Currency == a.Currency)).OrderBy(a => a.Id).Select(a => new Blocker(a.Id, null, a.Name));
            else
                return Finance.Error(400, "阻塞类型无效");
            return Results.Ok(new
            {
                total = await query.CountAsync(),
                items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync()
            });
        });
        g.MapGet("/setup-check", async (Guid org, ClaimsPrincipal user, AppDb db) =>
        {
            var access = await Finance.Access(db, org, user);
            if (access != 200)
                return Finance.Error(access, "无权访问");
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            var entries = db.Set<JournalEntry>().Where(x => x.OrganizationId == org);
            var cash = db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org);
            var accounts = db.Set<FinancialAccount>().Where(x => x.OrganizationId == org);
            var missingAccounts = await accounts.Where(a => !db.Set<AccountingMapping>().Any(m => m.OrganizationId == org && m.Kind == "FinancialAccount" && m.ResourceId == a.Id && m.Currency == a.Currency)).Select(a => new { a.Id, a.Name, a.Currency }).OrderBy(a => a.Id).Take(100).ToListAsync();
            var missingAccountCount = await accounts.CountAsync(a => !db.Set<AccountingMapping>().Any(m => m.OrganizationId == org && m.Kind == "FinancialAccount" && m.ResourceId == a.Id && m.Currency == a.Currency));
            var pendingCash = await cash.CountAsync(t => !entries.Any(e => e.SourceTransactionId == t.Id && e.Status == "Posted"));
            var pendingAr = await db.Set<Receivable>().CountAsync(d => d.OrganizationId == org && !d.Cancelled && !entries.Any(e => e.SourceReceivableId == d.Id && e.Status == "Posted" && !entries.Any(r => r.ReversalOfId == e.Id && r.Status == "Posted")));
            var pendingAp = await db.Set<Payable>().CountAsync(d => d.OrganizationId == org && !d.Cancelled && !entries.Any(e => e.SourcePayableId == d.Id && e.Status == "Posted" && !entries.Any(r => r.ReversalOfId == e.Id && r.Status == "Posted")));
            var earliest = await cash.Select(x => (DateOnly?)x.BusinessDate).Concat(db.Set<Receivable>().Where(x => x.OrganizationId == org && !x.Cancelled).Select(x => (DateOnly?)x.IssueDate)).Concat(db.Set<Payable>().Where(x => x.OrganizationId == org && !x.Cancelled).Select(x => (DateOnly?)x.IssueDate)).MinAsync();
            var drafts = await entries.CountAsync(x => x.Status == "Draft");
            var periods = await db.Set<FiscalPeriod>().CountAsync(x => x.OrganizationId == org);
            var uncovered = await cash.CountAsync(t => !db.Set<FiscalPeriod>().Any(p => p.OrganizationId == org && t.BusinessDate >= p.StartDate && t.BusinessDate <= p.EndDate));
            await tx.CommitAsync();
            return Results.Ok(new
            {
                earliestBusinessDate = earliest,
                periods,
                uncoveredCash = uncovered,
                pendingCash,
                pendingReceivables = pendingAr,
                pendingPayables = pendingAp,
                drafts,
                missingAccountCount,
                missingAccounts,
                historyPolicy = "FullHistory",
                guidance = "从最早业务日期建立期间，完整导入历史来源；已有期初流水只导入一次，不另建重复期初凭证。试算与资金对账一致后结账。"
            });
        });
        g.MapPost("/batch-import", async (Guid org, BatchSource[] items, ClaimsPrincipal user, AppDb db) =>
        {
            var access = await Finance.Access(db, org, user, true);
            if (access != 200)
                return Finance.Error(access, "无权写入");
            if (items == null || items.Length is < 1 or > 50 || items.Any(x => x == null))
                return Finance.Error(400, "每批1至50项");
            var results = new List<object>();
            foreach (var item in items)
            {
                try
                {
                    var e = await new AccountingService(db).Import(org, new(item.RequestId, item.SourceKind, item.SourceId), user);
                    results.Add(new
                    {
                        item.SourceId,
                        success = true,
                        journalId = (Guid?)e.Id,
                        error = (string?)null
                    });
                }
                catch (AccountingFault ex) { results.Add(new { item.SourceId, success = false, journalId = (Guid?)null, error = ex.Message }); }
                catch (DbUpdateException) { results.Add(new { item.SourceId, success = false, journalId = (Guid?)null, error = "数据冲突，请刷新后重试" }); }
                finally { db.ChangeTracker.Clear(); }
            }
            return Results.Ok(new
            {
                items = results
            });
        });
        g.MapPost("/batch-post", async (Guid org, BatchPost[] items, ClaimsPrincipal user, AppDb db) =>
        {
            var access = await Finance.Access(db, org, user, true);
            if (access != 200)
                return Finance.Error(access, "无权写入");
            if (items == null || items.Length is < 1 or > 50 || items.Any(x => x == null))
                return Finance.Error(400, "每批1至50项");
            var results = new List<object>();
            foreach (var item in items)
            {
                try
                {
                    var e = await new AccountingService(db).Post(org, item.Id, new(item.Version), user);
                    results.Add(new
                    {
                        item.Id,
                        success = true,
                        number = e.Number,
                        error = (string?)null
                    });
                }
                catch (AccountingFault ex) { results.Add(new { item.Id, success = false, number = (string?)null, error = ex.Message }); }
                catch (DbUpdateException) { results.Add(new { item.Id, success = false, number = (string?)null, error = "数据冲突，请刷新后重试" }); }
                finally { db.ChangeTracker.Clear(); }
            }
            return Results.Ok(new
            {
                items = results
            });
        });
        // Query direction from business source to journal; tenant is part of every predicate.
        g.MapGet("/source-journal", async (Guid org, string kind, Guid sourceId, ClaimsPrincipal user, AppDb db) =>
        {
            var access = await Finance.Access(db, org, user);
            if (access != 200)
                return Finance.Error(access, "无权访问");
            if (kind is not ("Transaction" or "Receivable" or "Payable"))
                return Finance.Error(400, "来源类型无效");
            var entry = await db.Set<JournalEntry>().AsNoTracking().Where(e => e.OrganizationId == org && (kind == "Transaction" ? e.SourceTransactionId == sourceId : kind == "Receivable" ? e.SourceReceivableId == sourceId : e.SourcePayableId == sourceId)).Select(e => new { e.Id, e.Number, e.Status, e.BusinessDate }).SingleOrDefaultAsync();
            return entry == null ? Results.NotFound() : Results.Ok(entry);
        });
    }
}

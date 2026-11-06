using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using static CFIS.Api.Finance;
namespace CFIS.Api;

public static class AccountingApi
{
    static object Header(JournalEntry e) => new { e.Id, e.Version, e.Number, e.PeriodId, e.BusinessDate, e.Currency, e.Description, e.Status, e.ReversalOfId, e.SourceTransactionId, e.SourceReceivableId, e.SourcePayableId, e.CreatedAt, e.PostedAt, debitTotal = Money(e.DebitTotal), creditTotal = Money(e.CreditTotal) };
    static async Task<IResult> Run(Guid org, ClaimsPrincipal user, AppDb db, bool write, Func<Task<IResult>> action)
    {
        var access = await Access(db, org, user, write);
        if (access != 200)
            return Error(access, "无权访问会计模块");
        try
        {
            return await action();
        }
        catch (AccountingFault ex) { return Error(ex.Status, ex.Message, "ACCOUNTING_RULE"); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Error(409, "凭证、科目或来源已存在"); }
    }
    static bool Page(int page, int size) => page >= 1 && page <= 1000000 && size >= 1 && size <= 100;
    public static void MapAccounting(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/organizations/{org:guid}/accounting").RequireAuthorization();
        g.MapGet("/accounts", (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20, string? search = null) => Run(org, u, db, false, async () => { if (!Page(page, pageSize) || search?.Length > 200) return Error(400, "分页或搜索无效"); var q = db.Set<LedgerAccount>().AsNoTracking().Where(x => x.OrganizationId == org); if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || x.Code.Contains(search)); return Results.Ok(new { total = await q.CountAsync(), items = await q.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync() }); }));
        g.MapPost("/accounts", (Guid org, LedgerAccountInput r, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => Results.Created("", await new AccountingService(db).CreateAccount(org, r, u))));
        g.MapPatch("/accounts/{id:guid}/status", (Guid org, Guid id, LedgerStatusInput r, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => { if (r.Active is not bool active) return Error(400, "请明确指定科目启用状态"); await new AccountingService(db).SetAccountStatus(org, id, active, u); return Results.NoContent(); }));
        g.MapGet("/periods", (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20) => Run(org, u, db, false, async () => { if (!Page(page, pageSize)) return Error(400, "分页无效"); var q = db.Set<FiscalPeriod>().AsNoTracking().Where(x => x.OrganizationId == org); return Results.Ok(new { total = await q.CountAsync(), items = await q.OrderByDescending(x => x.StartDate).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync() }); }));
        g.MapPost("/periods", (Guid org, PeriodInput r, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => Results.Created("", await new AccountingService(db).CreatePeriod(org, r, u))));
        g.MapGet("/periods/{id:guid}/precheck", (Guid org, Guid id, ClaimsPrincipal u, AppDb db) => Run(org, u, db, false, async () => Results.Ok(await new AccountingService(db).PeriodPrecheck(org, id))));
        g.MapPost("/periods/{id:guid}/close", (Guid org, Guid id, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => Results.Ok(await new AccountingService(db).ClosePeriod(org, id, u))));
        g.MapGet("/mappings", (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20) => Run(org, u, db, false, async () => { if (!Page(page, pageSize)) return Error(400, "分页无效"); var q = db.Set<AccountingMapping>().AsNoTracking().Where(x => x.OrganizationId == org); return Results.Ok(new { total = await q.CountAsync(), items = await q.OrderBy(x => x.Kind).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync() }); }));
        g.MapPut("/mappings", (Guid org, MappingInput r, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => Results.Ok(await new AccountingService(db).SetMapping(org, r, u))));
        g.MapGet("/journals", (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20, string? status = null, DateOnly? dateFrom = null, DateOnly? dateTo = null) => Run(org, u, db, false, async () => { if (!Page(page, pageSize) || status is not (null or "Draft" or "Posted") || dateFrom > dateTo) return Error(400, "凭证筛选无效"); var q = db.Set<JournalEntry>().AsNoTracking().Where(x => x.OrganizationId == org); if (status != null) q = q.Where(x => x.Status == status); if (dateFrom != null) q = q.Where(x => x.BusinessDate >= dateFrom); if (dateTo != null) q = q.Where(x => x.BusinessDate <= dateTo); return Results.Ok(new { total = await q.CountAsync(), items = (await q.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync()).Select(Header) }); }));
        g.MapGet("/journals/{id:guid}", (Guid org, Guid id, ClaimsPrincipal u, AppDb db) => Run(org, u, db, false, async () => { var e = await db.Set<JournalEntry>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id); if (e == null) return Error(404, "凭证不存在"); var lines = await db.Set<JournalLine>().AsNoTracking().Where(x => x.OrganizationId == org && x.EntryId == id).Join(db.Set<LedgerAccount>(), l => new { l.OrganizationId, Id = l.LedgerAccountId }, a => new { a.OrganizationId, a.Id }, (l, a) => new { l.Position, l.LedgerAccountId, a.Code, a.Name, l.Debit, l.Credit }).OrderBy(x => x.Position).ToListAsync(); var reversedBy = await db.Set<JournalEntry>().Where(x => x.OrganizationId == org && x.ReversalOfId == id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(); return Results.Ok(new { entry = Header(e), reversedBy, lines = lines.Select(x => new { x.Position, x.LedgerAccountId, x.Code, x.Name, debit = Money(x.Debit), credit = Money(x.Credit) }) }); }));
        g.MapPost("/journals", (Guid org, JournalInput r, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => Results.Created("", Header(await new AccountingService(db).CreateDraft(org, r, u)))));
        g.MapPut("/journals/{id:guid}", (Guid org, Guid id, JournalEditInput r, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => Results.Ok(Header(await new AccountingService(db).Edit(org, id, r, u)))));
        g.MapDelete("/journals/{id:guid}", (Guid org, Guid id, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => { await new AccountingService(db).DeleteDraft(org, id, u); return Results.NoContent(); }));
        g.MapPost("/journals/{id:guid}/post", (Guid org, Guid id, JournalPostInput r, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => Results.Ok(Header(await new AccountingService(db).Post(org, id, r, u)))));
        g.MapPost("/journals/{id:guid}/reverse", (Guid org, Guid id, JournalReverseInput r, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => Results.Created("", Header(await new AccountingService(db).Reverse(org, id, r, u)))));
        g.MapPost("/import", (Guid org, JournalImportInput r, ClaimsPrincipal u, AppDb db) => Run(org, u, db, true, async () => Results.Ok(Header(await new AccountingService(db).Import(org, r, u)))));
        g.MapGet("/sources", (Guid org, ClaimsPrincipal u, AppDb db, string kind = "Transaction", int page = 1, int pageSize = 20) => Run(org, u, db, false, async () =>
        {
            if (!Page(page, pageSize) || kind is not ("Transaction" or "Receivable" or "Payable"))
                return Error(400, "来源筛选无效");
            var journals = db.Set<JournalEntry>().Where(x => x.OrganizationId == org);
            if (kind == "Transaction")
            {
                var q = from t in db.Set<FinancialTransaction>().AsNoTracking() join a in db.Set<FinancialAccount>() on new { t.OrganizationId, Id = t.AccountId } equals new { a.OrganizationId, a.Id } where t.OrganizationId == org && !journals.Any(e => e.SourceTransactionId == t.Id) select new { t.Id, t.BusinessDate, a.Currency, t.Amount, t.Description };
                var total = await q.CountAsync();
                var rows = await q.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
                return Results.Ok(new
                {
                    total,
                    items = rows.Select(x => new { x.Id, x.BusinessDate, x.Currency, amount = Money(x.Amount), x.Description })
                });
            }
            if (kind == "Receivable")
            {
                var q = db.Set<Receivable>().AsNoTracking().Where(x => x.OrganizationId == org && !x.Cancelled && !journals.Any(e => e.SourceReceivableId == x.Id));
                var total = await q.CountAsync();
                var rows = await q.OrderBy(x => x.IssueDate).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
                return Results.Ok(new
                {
                    total,
                    items = rows.Select(x => new { x.Id, businessDate = x.IssueDate, x.Currency, amount = Money(x.Amount), description = x.Number })
                });
            }
            else
            {
                var q = db.Set<Payable>().AsNoTracking().Where(x => x.OrganizationId == org && !x.Cancelled && !journals.Any(e => e.SourcePayableId == x.Id));
                var total = await q.CountAsync();
                var rows = await q.OrderBy(x => x.IssueDate).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
                return Results.Ok(new
                {
                    total,
                    items = rows.Select(x => new { x.Id, businessDate = x.IssueDate, x.Currency, amount = Money(x.Amount), description = x.Number })
                });
            }
        }));
        g.MapGet("/trial-balance", (Guid org, DateOnly dateFrom, DateOnly dateTo, string currency, ClaimsPrincipal u, AppDb db) => Run(org, u, db, false, async () => Results.Ok(await new AccountingReports(db).Trial(org, dateFrom, dateTo, currency))));
        g.MapGet("/ledger/{id:guid}", (Guid org, Guid id, DateOnly dateFrom, DateOnly dateTo, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20) => Run(org, u, db, false, async () => Results.Ok(await new AccountingReports(db).Ledger(org, id, dateFrom, dateTo, page, pageSize))));
        g.MapGet("/reconciliation", (Guid org, DateOnly dateTo, string currency, ClaimsPrincipal u, AppDb db) => Run(org, u, db, false, async () => Results.Ok(await new AccountingReports(db).Reconcile(org, dateTo, currency))));
    }
}

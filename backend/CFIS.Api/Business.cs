using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public record CounterpartyInput(string Name, string Kind, string? Contact, Guid? RequestId = null);
public record CounterpartyChange(string Name, string Status, string? Contact = null);
public record DocumentInput(string Number, Guid CounterpartyId, Guid CategoryId, string Currency, string Amount, DateOnly IssueDate, DateOnly DueDate, string? Description, Guid? RequestId = null);
public record SettlementInput(Guid RequestId, Guid AccountId, string Amount, DateOnly BusinessDate, string? Description);
public record BudgetLineInput(Guid CategoryId, string Amount);
public record BudgetInput(string Name, string Currency, DateOnly StartDate, DateOnly EndDate, BudgetLineInput[] Items, Guid? RequestId = null, Guid? PreviousVersionId = null);
public static partial class Finance
{
    static string Fingerprint(object value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    internal static Task RequestLock(AppDb db, Guid org, Guid id) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({org.ToString() + id.ToString()},0))");

    static bool ParseMoney(string? s, out decimal value)
    {
        value = 0;
        return s != null && System.Text.RegularExpressions.Regex.IsMatch(s, @"^[0-9]{1,15}(\.[0-9]{1,4})?$") && decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && value > 0 && value <= Maximum;
    }
    static bool CurrencyValid(string? s) => s != null && System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Z]{3}$");
    internal static Task<TradeDocument?> LockDocument(AppDb db, Guid org, Guid? receivable, Guid? payable) => new SettlementService(db).LockDocument(org, receivable, payable);
    internal static Task<decimal> Settled(AppDb db, Guid org, Guid? receivable, Guid? payable) => new SettlementService(db).Settled(org, receivable, payable);
    static void MapBusiness(RouteGroupBuilder g)
    {
        g.MapGet("/counterparties", async (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20, string? search = null) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问");
            if (!Page(page, pageSize) || search?.Length > 200)
                return Error(400, "分页或搜索无效");
            var q = db.Set<Counterparty>().AsNoTracking().Where(x => x.OrganizationId == org);
            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(x => x.Name.Contains(search));
            return Results.Ok(new
            {
                total = await q.CountAsync(),
                items = await q.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync()
            });
        });
        g.MapPost("/counterparties", async (Guid org, CounterpartyInput r, ClaimsPrincipal u, AppDb db, HttpContext context) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权写入");
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200 || r.Contact?.Length > 300 || r.Kind is not ("Customer" or "Supplier" or "Both"))
                return Error(400, "往来方参数无效");
            await using var tx = await db.Database.BeginTransactionAsync();
            if (r.RequestId is null)
            {
                if (context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("Business:RequireIdempotencyKeys"))
                    return Error(400, "必须提供requestId", "REQUEST_ID_REQUIRED");
                context.Response.Headers.Append("Deprecation", "true");
            }
            if (r.RequestId == Guid.Empty)
                return Error(400, "请求标识无效");
            var hash = Fingerprint(r);
            if (r.RequestId is Guid request)
            {
                await RequestLock(db, org, request);
                var old = await db.Set<Counterparty>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == request);
                if (old != null)
                    return old.RequestHash == hash ? Results.Ok(old) : Error(409, "请求ID冲突");
            }
            var c = new Counterparty { Id = r.RequestId ?? Guid.NewGuid(), RequestHash = hash, OrganizationId = org, Name = r.Name.Trim(), Kind = r.Kind, Contact = r.Contact ?? "" };
            db.Add(c);
            db.Add(Audit(org, u, "CounterpartyCreated", c.Id, JsonSerializer.Serialize(c)));
            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Error(409, "同类型往来方名称重复"); }
            return Results.Created($"/api/v1/organizations/{org}/counterparties", c);
        });
        g.MapPatch("/counterparties/{id:guid}", async (Guid org, Guid id, CounterpartyChange r, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权写入");
            if (!ValidChange(new ResourceChange(r.Name, r.Status)) || r.Contact?.Length > 300)
                return Error(400, "参数无效");
            await using var tx = await db.Database.BeginTransactionAsync();
            var c = await db.Set<Counterparty>().FromSqlInterpolated($"SELECT * FROM counterparties WHERE \"OrganizationId\"={org} AND \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync();
            if (c is null)
                return Error(404, "往来方不存在");
            var before = new
            {
                c.Name,
                c.Status,
                c.Contact
            };
            c.Name = r.Name.Trim();
            c.Status = r.Status;
            if (r.Contact != null)
                c.Contact = r.Contact;
            db.Add(Audit(org, u, "CounterpartyChanged", id, JsonSerializer.Serialize(new
            {
                before,
                after = new
                {
                    c.Name,
                    c.Status,
                    c.Contact
                }
            })));
            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Error(409, "往来方重名"); }
            return Results.NoContent();
        });
        MapDocuments<Receivable>(g, "/receivables", true);
        MapDocuments<Payable>(g, "/payables", false);
        MapBudgets(g);
        MapExports(g);
    }
    static void MapDocuments<T>(RouteGroupBuilder g, string path, bool incoming) where T : TradeDocument, new()
    {
        g.MapPost(path, async (Guid org, DocumentInput r, ClaimsPrincipal u, AppDb db, HttpContext context) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权写入");
            if (string.IsNullOrWhiteSpace(r.Number) || r.Number.Length > 100 || !CurrencyValid(r.Currency) || !ParseMoney(r.Amount, out var amount) || r.IssueDate == default || r.DueDate < r.IssueDate || r.Description?.Length > 500)
                return Error(400, "单据参数无效");
            await using var tx = await db.Database.BeginTransactionAsync();
            await AccountingService.Lock(db, org);
            if (!await AccountingService.DateOpen(db, org, r.IssueDate))
                return Error(409, "单据日期不在开放会计期间");
            if (r.RequestId is null)
            {
                if (context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("Business:RequireIdempotencyKeys"))
                    return Error(400, "必须提供requestId", "REQUEST_ID_REQUIRED");
                context.Response.Headers.Append("Deprecation", "true");
            }
            if (r.RequestId == Guid.Empty)
                return Error(400, "请求标识无效");
            var hash = Fingerprint(r);
            if (r.RequestId is Guid request)
            {
                await RequestLock(db, org, request);
                var old = await db.Set<T>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == request);
                if (old != null)
                    return old.RequestHash == hash ? Results.Ok(new
                    {
                        old.Id,
                        old.Number,
                        amount = Money(old.Amount)
                    }) : Error(409, "请求ID冲突");
            }
            var c = await db.Set<Counterparty>().FromSqlInterpolated($"SELECT * FROM counterparties WHERE \"OrganizationId\"={org} AND \"Id\"={r.CounterpartyId} FOR UPDATE").SingleOrDefaultAsync();
            if (c is null || c.Status != "Active" || c.Kind != (incoming ? "Customer" : "Supplier") && c.Kind != "Both")
                return Error(400, "往来方不存在、停用或类型不符");
            var category = await db.Set<Category>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == r.CategoryId);
            if (category is null || category.Status != "Active" || category.Kind != (incoming ? "Income" : "Expense"))
                return Error(400, "分类无效");
            var d = new T { Id = r.RequestId ?? Guid.NewGuid(), RequestHash = hash, OrganizationId = org, Number = r.Number.Trim(), CounterpartyId = r.CounterpartyId, CategoryId = r.CategoryId, Currency = r.Currency, Amount = amount, IssueDate = r.IssueDate, DueDate = r.DueDate, Description = r.Description ?? "" };
            db.Add(d);
            db.Add(Audit(org, u, incoming ? "ReceivableCreated" : "PayableCreated", d.Id, r.Number));
            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Error(409, "单据编号重复"); }
            return Results.Created($"/api/v1/organizations/{org}{path}/{d.Id}", new
            {
                d.Id,
                d.Number,
                amount = Money(d.Amount)
            });
        });
        g.MapGet(path, async (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20, Guid? counterpartyId = null, string? status = null, string? currency = null, DateOnly? dueFrom = null, DateOnly? dueTo = null, string sort = "asc") =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问");
            if (!Page(page, pageSize))
                return Error(400, "分页无效");
            var q = db.Set<T>().AsNoTracking().Where(x => x.OrganizationId == org);
            if (counterpartyId != null)
                q = q.Where(x => x.CounterpartyId == counterpartyId);
            if (dueFrom > dueTo || sort is not ("asc" or "desc") || status is not (null or "Cancelled" or "Paid" or "Overdue" or "PartiallyPaid" or "Open"))
                return Error(400, "单据筛选无效");
            if (currency != null)
                q = q.Where(x => x.Currency == currency);
            if (dueFrom != null)
                q = q.Where(x => x.DueDate >= dueFrom);
            if (dueTo != null)
                q = q.Where(x => x.DueDate <= dueTo);
            var localDate = await LocalToday(db, org);
            if (status != null)
                q = q.Where(d => (d.Cancelled ? "Cancelled" : (db.Set<FinancialTransaction>().Where(t => t.OrganizationId == org && (incoming ? t.ReceivableId == d.Id : t.PayableId == d.Id)).Sum(t => (decimal?)(incoming ? (t.Kind == "Income" ? t.Amount : -t.Amount) : (t.Kind == "Expense" ? t.Amount : -t.Amount))) ?? 0) == d.Amount ? "Paid" : d.DueDate < localDate ? "Overdue" : (db.Set<FinancialTransaction>().Where(t => t.OrganizationId == org && (incoming ? t.ReceivableId == d.Id : t.PayableId == d.Id)).Sum(t => (decimal?)(incoming ? (t.Kind == "Income" ? t.Amount : -t.Amount) : (t.Kind == "Expense" ? t.Amount : -t.Amount))) ?? 0) > 0 ? "PartiallyPaid" : "Open") == status);
            var total = await q.CountAsync();
            var ordered = sort == "asc" ? q.OrderBy(x => x.DueDate).ThenBy(x => x.Id) : q.OrderByDescending(x => x.DueDate).ThenByDescending(x => x.Id);
            var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            var ids = rows.Select(x => x.Id).ToArray();
            var entries = await db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org && (incoming ? ids.Contains(x.ReceivableId ?? Guid.Empty) : ids.Contains(x.PayableId ?? Guid.Empty))).GroupBy(x => incoming ? x.ReceivableId : x.PayableId).Select(x => new { Id = x.Key, Amount = x.Sum(t => incoming ? (t.Kind == "Income" ? t.Amount : -t.Amount) : (t.Kind == "Expense" ? t.Amount : -t.Amount)) }).ToDictionaryAsync(x => x.Id!.Value, x => x.Amount);
            var names = await db.Set<Counterparty>().Where(x => x.OrganizationId == org && rows.Select(d => d.CounterpartyId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
            var today = await LocalToday(db, org);
            return Results.Ok(new
            {
                total,
                items = rows.Select(d => { var paid = entries.GetValueOrDefault(d.Id); return new { d.Id, d.Number, d.CounterpartyId, counterpartyName = names.GetValueOrDefault(d.CounterpartyId), d.CategoryId, d.Currency, amount = Money(d.Amount), settled = Money(paid), remaining = Money(d.Amount - paid), d.IssueDate, d.DueDate, d.Description, status = d.Cancelled ? "Cancelled" : paid == d.Amount ? "Paid" : d.DueDate < today ? "Overdue" : paid > 0 ? "PartiallyPaid" : "Open" }; })
            });
        });
        g.MapGet(path + "/{id:guid}", async (Guid org, Guid id, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问");
            if (!Page(page, pageSize))
                return Error(400, "分页无效");
            var d = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id);
            if (d is null)
                return Error(404, "单据不存在");
            var paid = await Settled(db, org, incoming ? id : null, incoming ? null : id);
            var q = db.Set<FinancialTransaction>().AsNoTracking().Where(x => x.OrganizationId == org && (incoming ? x.ReceivableId == id : x.PayableId == id));
            var total = await q.CountAsync();
            var rows = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Results.Ok(new
            {
                d.Id,
                d.Number,
                d.CounterpartyId,
                d.CategoryId,
                d.Currency,
                amount = Money(d.Amount),
                settled = Money(paid),
                remaining = Money(d.Amount - paid),
                d.IssueDate,
                d.DueDate,
                d.Description,
                d.Cancelled,
                settlements = new
                {
                    total,
                    items = rows.Select(TransactionDto)
                }
            });
        });
        g.MapPost(path + "/{id:guid}/settlements", async (Guid org, Guid id, SettlementInput r, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权结算");
            var doc = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id);
            if (doc is null)
                return Error(404, "单据不存在");
            return await Post(org, new PostingInput(r.RequestId, r.AccountId, null, doc.CategoryId, incoming ? "Income" : "Expense", r.Amount, r.Description, r.BusinessDate, incoming ? id : null, incoming ? null : id), u, db, false);
        });
        g.MapPost(path + "/{id:guid}/cancel", async (Guid org, Guid id, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权取消");
            await using var tx = await db.Database.BeginTransactionAsync();
            await AccountingService.Lock(db, org);
            var doc = await LockDocument(db, org, incoming ? id : null, incoming ? null : id);
            if (doc is null)
                return Error(404, "单据不存在");
            if (await Settled(db, org, incoming ? id : null, incoming ? null : id) != 0)
                return Error(409, "已有未撤销结算，不能取消");
            if (await db.Set<JournalEntry>().AnyAsync(e => e.OrganizationId == org && e.Status == "Posted" && (incoming ? e.SourceReceivableId == id : e.SourcePayableId == id) && !db.Set<JournalEntry>().Any(rev => rev.OrganizationId == org && rev.ReversalOfId == e.Id && rev.Status == "Posted")))
                return Error(409, "请先冲销该单据的会计确认凭证");
            if (doc.Cancelled)
                return Results.NoContent();
            doc.Cancelled = true;
            doc.CancelledAt = DateTimeOffset.UtcNow;
            db.Add(Audit(org, u, "DocumentCancelled", id, doc.Number));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
    }
}



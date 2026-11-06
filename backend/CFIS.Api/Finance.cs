using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public record AccountInput(string Name, string Currency, string Kind);
public record CategoryInput(string Name, string Kind);
public record PostingInput(Guid RequestId, Guid AccountId, Guid? DestinationAccountId, Guid? CategoryId, string Kind, string Amount, string? Description, DateOnly? BusinessDate = null, Guid? ReceivableId = null, Guid? PayableId = null, Guid? CorrectionOfId = null, AllocationInput[]? Allocations = null);
public static partial class Finance
{
    internal const decimal Maximum = 999999999999999.9999m;
    internal static string Uid(ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.NameIdentifier)!;
    internal static async Task<int> Access(AppDb db, Guid org, ClaimsPrincipal u, bool write = false)
    {
        var role = await db.Members.AsNoTracking().Where(x => x.OrganizationId == org && x.UserId == Uid(u)).Select(x => (MemberRole?)x.Role).SingleOrDefaultAsync();
        return role is null ? 404 : !FinancePermissions.Allows(role.Value, write ? FinancePermission.Write : FinancePermission.Read) ? 403 : 200;
    }
    internal static IResult Error(int status, string title, string? code = null) => Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { { "code", code ?? (title.Contains("余额不足") ? "INSUFFICIENT_FUNDS" : title.Contains("超过未结") ? "SETTLEMENT_EXCEEDS_REMAINING" : title.Contains("请求") && status == 409 ? "IDEMPOTENCY_CONFLICT" : title.Contains("编号重复") ? "DOCUMENT_NUMBER_CONFLICT" : title.Contains("停用") || title.Contains("归档") ? "RESOURCE_INACTIVE" : "HTTP_" + status) } });
    static bool Page(int page, int size) => page >= 1 && page <= 1000000 && size >= 1 && size <= 100;
    internal static string Money(decimal amount) => amount.ToString("0.0000", CultureInfo.InvariantCulture);
    internal static object TransactionDto(FinancialTransaction t) => new { t.Id, t.RequestId, t.Kind, t.AccountId, t.DestinationAccountId, t.CategoryId, amount = Money(t.Amount), t.Description, t.ActorId, t.BusinessDate, t.ReversalOfId, t.ReversalReason, t.CorrectionOfId, t.ReceivableId, t.PayableId, t.CreatedAt };
    internal static AuditLog Audit(Guid org, ClaimsPrincipal u, string action, Guid resource, string detail) => new() { OrganizationId = org, ActorId = Uid(u), Action = action, ResourceId = resource, Detail = detail };
    public static void MapFinance(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/organizations/{org:guid}").RequireAuthorization();
        MapExtensions(g);
        MapBusiness(g);
        MapReportingBasis(g);
        MapReports(g);
        g.MapGet("/accounts", async (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20, string? search = null) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问企业");
            if (!Page(page, pageSize))
                return Error(400, "分页参数无效");
            var query = db.Set<FinancialAccount>().AsNoTracking().Where(x => x.OrganizationId == org);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(x => x.Name.Contains(search));
            var total = await query.CountAsync();
            var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Results.Ok(new
            {
                total,
                items = rows.Select(x => new { x.Id, x.Name, x.Currency, x.Kind, x.Status, balance = Money(x.Balance) })
            });
        });
        g.MapPost("/accounts", async (Guid org, AccountInput r, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权写入企业");
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200 || r.Currency is null || !System.Text.RegularExpressions.Regex.IsMatch(r.Currency, "^[A-Z]{3}$") || r.Kind is not ("Cash" or "Bank"))
                return Error(400, "账户名称、币种或类型无效");
            var a = new FinancialAccount { OrganizationId = org, Name = r.Name.Trim(), Currency = r.Currency, Kind = r.Kind };
            db.Add(a);
            db.Add(Audit(org, u, "AccountCreated", a.Id, a.Name));
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/organizations/{org}/accounts", new
            {
                a.Id,
                a.Name,
                a.Currency,
                a.Kind,
                a.Status,
                balance = "0.0000"
            });
        });
        g.MapGet("/categories", async (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20, string? search = null) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问企业");
            if (!Page(page, pageSize))
                return Error(400, "分页参数无效");
            var query = db.Set<Category>().AsNoTracking().Where(x => x.OrganizationId == org);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(x => x.Name.Contains(search));
            return Results.Ok(new
            {
                total = await query.CountAsync(),
                items = await query.OrderBy(x => x.Kind).ThenBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync()
            });
        });
        g.MapPost("/categories", async (Guid org, CategoryInput r, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权写入企业");
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200 || r.Kind is not ("Income" or "Expense"))
                return Error(400, "分类名称或类型无效");
            var c = new Category { OrganizationId = org, Name = r.Name.Trim(), Kind = r.Kind };
            db.Add(c);
            db.Add(Audit(org, u, "CategoryCreated", c.Id, c.Name));
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Error(409, "同类型分类名称已存在"); }
            return Results.Created($"/api/v1/organizations/{org}/categories", c);
        });
        g.MapPost("/transactions", (Guid org, PostingInput r, ClaimsPrincipal u, AppDb db) => Post(org, r, u, db, false));
        g.MapPost("/transfers", (Guid org, PostingInput r, ClaimsPrincipal u, AppDb db) => Post(org, r, u, db, true));
        g.MapGet("/transactions", async (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20, string? kind = null, Guid? accountId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string sort = "desc", DateOnly? dateFrom = null, DateOnly? dateTo = null) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问企业");
            if ((dateFrom != null && dateTo != null && dateFrom > dateTo) || !Page(page, pageSize) || sort is not ("asc" or "desc") || kind is not (null or "Income" or "Expense" or "Transfer" or "Opening") || (from.HasValue && to.HasValue && from > to))
                return Error(400, "筛选或分页参数无效");
            var q = db.Set<FinancialTransaction>().AsNoTracking().Where(x => x.OrganizationId == org);
            if (kind != null)
                q = q.Where(x => x.Kind == kind);
            if (accountId != null)
                q = q.Where(x => x.AccountId == accountId || x.DestinationAccountId == accountId);
            if (from != null)
                q = q.Where(x => x.CreatedAt >= from.Value.ToUniversalTime());
            if (to != null)
                q = q.Where(x => x.CreatedAt <= to.Value.ToUniversalTime());
            if (dateFrom != null)
                q = q.Where(x => x.BusinessDate >= dateFrom);
            if (dateTo != null)
                q = q.Where(x => x.BusinessDate <= dateTo);
            var total = await q.CountAsync();
            q = sort == "asc" ? q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id) : q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);
            return Results.Ok(new
            {
                total,
                items = (await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync()).Select(TransactionDto)
            });
        });
        g.MapGet("/audit-logs", async (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20, string? actorId = null, string? action = null, Guid? resourceId = null, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问企业");
            var role = await db.Members.Where(x => x.OrganizationId == org && x.UserId == Uid(u)).Select(x => x.Role).SingleAsync();
            if (!FinancePermissions.Allows(role, FinancePermission.Audit))
                return Error(403, "仅企业管理员可读审计");
            if (!Page(page, pageSize))
                return Error(400, "分页参数无效");
            if (from > to || actorId?.Length > 450 || action?.Length > 100)
                return Error(400, "审计筛选无效");
            var q = db.Set<AuditLog>().AsNoTracking().Where(x => x.OrganizationId == org);
            if (actorId != null)
                q = q.Where(x => x.ActorId == actorId);
            if (action != null)
                q = q.Where(x => x.Action == action);
            if (resourceId != null)
                q = q.Where(x => x.ResourceId == resourceId);
            if (from != null)
                q = q.Where(x => x.CreatedAt >= from.Value.ToUniversalTime());
            if (to != null)
                q = q.Where(x => x.CreatedAt <= to.Value.ToUniversalTime());
            return Results.Ok(new
            {
                total = await q.CountAsync(),
                items = await q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync()
            });
        });
    }
    internal static Task<IResult> Post(Guid org, PostingInput r, ClaimsPrincipal u, AppDb db, bool transfer) => new TransactionPostingService(db).Post(org, r, u, transfer);

}





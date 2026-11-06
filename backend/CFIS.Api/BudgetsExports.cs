using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
namespace CFIS.Api;

public record BudgetStatusInput(string Status);
public static partial class Finance
{
    static void MapBudgets(RouteGroupBuilder g)
    {
        g.MapPost("/budgets/{id:guid}/status", (Guid org, Guid id, BudgetStatusInput r, ClaimsPrincipal u, AppDb db) => new BudgetLifecycleService(db).Change(org, id, r, u));
        g.MapPost("/budgets", async (Guid org, BudgetInput r, ClaimsPrincipal u, AppDb db, HttpContext context) =>
        {
            var access = await Access(db, org, u, true);
            if (access != 200)
                return Error(access, "无权制定预算");
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200 || !CurrencyValid(r.Currency) || r.StartDate == default || r.EndDate < r.StartDate || r.Items is null || r.Items.Length is < 1 or > 100 || r.Items.Any(x => x is null) || r.Items.Select(x => x.CategoryId).Distinct().Count() != r.Items.Length)
                return Error(400, "预算名称、日期或明细无效");
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
                var old = await db.Set<Budget>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == request);
                if (old != null)
                    return old.RequestHash == hash ? Results.Ok(new
                    {
                        old.Id,
                        old.Name
                    }) : Error(409, "请求ID冲突");
            }
            if (r.PreviousVersionId is Guid prior)
            {
                var old = await db.Set<Budget>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == prior);
                if (old is null)
                    return Error(404, "原预算不存在");
                if (old.Currency != r.Currency)
                    return Error(400, "调整版本须保持原币种");
            }
            var lines = new List<BudgetItem>();
            var budget = new Budget { Id = r.RequestId ?? Guid.NewGuid(), RequestHash = hash, PreviousVersionId = r.PreviousVersionId, OrganizationId = org, Name = r.Name.Trim(), Currency = r.Currency, StartDate = r.StartDate, EndDate = r.EndDate };
            foreach (var item in r.Items)
            {
                if (!ParseMoney(item.Amount, out var amount))
                    return Error(400, "预算金额无效");
                lines.Add(new()
                {
                    OrganizationId = org,
                    BudgetId = budget.Id,
                    CategoryId = item.CategoryId,
                    Amount = amount
                });
            }
            var ids = lines.Select(x => x.CategoryId).ToArray();
            if (await db.Set<Category>().CountAsync(x => x.OrganizationId == org && ids.Contains(x.Id) && x.Kind == "Expense" && x.Status == "Active") != ids.Length)
                return Error(400, "仅可选择本企业有效支出分类");
            db.Add(budget);
            db.AddRange(lines);
            db.Add(Audit(org, u, "BudgetCreated", budget.Id, r.Name));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Created($"/api/v1/organizations/{org}/budgets/{budget.Id}", new
            {
                budget.Id,
                budget.Name
            });
        });
        g.MapGet("/budgets", async (Guid org, ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问");
            if (!Page(page, pageSize))
                return Error(400, "分页无效");
            var q = db.Set<Budget>().AsNoTracking().Where(x => x.OrganizationId == org);
            return Results.Ok(new
            {
                total = await q.CountAsync(),
                items = await q.OrderByDescending(x => x.StartDate).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync()
            });
        });
        g.MapGet("/budgets/{id:guid}", async (Guid org, Guid id, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问");
            var b = await db.Set<Budget>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id);
            if (b is null)
                return Error(404, "预算不存在");
            var lines = await db.Set<BudgetItem>().AsNoTracking().Where(x => x.OrganizationId == org && x.BudgetId == id).ToListAsync();
            return Results.Ok(new
            {
                b.Id,
                b.Name,
                b.Currency,
                b.StartDate,
                b.EndDate,
                b.Status,
                b.PreviousVersionId,
                b.ActivatedAt,
                b.ClosedAt,
                items = lines.Select(x => new { x.CategoryId, amount = Money(x.Amount) })
            });
        });
        g.MapGet("/budgets/{id:guid}/execution", async (Guid org, Guid id, ClaimsPrincipal u, AppDb db) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权访问");
            var b = await db.Set<Budget>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id);
            if (b is null)
                return Error(404, "预算不存在");
            var lines = await db.Set<BudgetItem>().Where(x => x.OrganizationId == org && x.BudgetId == id).Join(db.Set<Category>(), x => new { x.OrganizationId, Id = x.CategoryId }, c => new { c.OrganizationId, c.Id }, (x, c) => new { x.CategoryId, c.Name, x.Amount }).ToListAsync();
            var ids = lines.Select(x => x.CategoryId).ToArray();
            var query = from t in db.Set<CategoryTransaction>() join a in db.Set<FinancialAccount>() on new { t.OrganizationId, Id = t.AccountId } equals new { a.OrganizationId, a.Id } where t.OrganizationId == org && a.Currency == b.Currency && t.BusinessDate >= b.StartDate && t.BusinessDate <= b.EndDate && t.CategoryId != null && ids.Contains(t.CategoryId.Value) select t;
            var spent = await query.GroupBy(x => x.CategoryId!.Value).Select(x => new { Id = x.Key, Amount = x.Sum(t => t.Kind == "Expense" ? t.Amount : -t.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount);
            return Results.Ok(new
            {
                b.Id,
                b.Name,
                b.Currency,
                b.StartDate,
                b.EndDate,
                b.Status,
                b.PreviousVersionId,
                b.ActivatedAt,
                b.ClosedAt,
                items = lines.Select(x => new { x.CategoryId, x.Name, planned = Money(x.Amount), actual = Money(spent.GetValueOrDefault(x.CategoryId)), remaining = Money(x.Amount - spent.GetValueOrDefault(x.CategoryId)), overBudget = spent.GetValueOrDefault(x.CategoryId) > x.Amount })
            });
        });
    }
    static void MapExports(RouteGroupBuilder g)
    {
        g.MapGet("/exports/transactions.xlsx", async (Guid org, ClaimsPrincipal u, AppDb db, DateOnly? dateFrom = null, DateOnly? dateTo = null, Guid? accountId = null) =>
        {
            var access = await Access(db, org, u);
            if (access != 200)
                return Error(access, "无权导出");
            if (dateFrom != null && dateTo != null && dateFrom > dateTo)
                return Error(400, "日期范围无效");
            var q = db.Set<FinancialTransaction>().AsNoTracking().Where(x => x.OrganizationId == org);
            if (dateFrom != null)
                q = q.Where(x => x.BusinessDate >= dateFrom);
            if (dateTo != null)
                q = q.Where(x => x.BusinessDate <= dateTo);
            if (accountId != null)
                q = q.Where(x => x.AccountId == accountId || x.DestinationAccountId == accountId);
            if (await q.CountAsync() > 10000)
                return Error(400, "导出最多10000条，请缩小筛选范围");
            var rows = await q.OrderBy(x => x.BusinessDate).ThenBy(x => x.Id).Take(10001).ToListAsync();
            if (rows.Count > 10000)
                return Error(400, "导出最多10000条");
            var accounts = await db.Set<FinancialAccount>().Where(x => x.OrganizationId == org).ToDictionaryAsync(x => x.Id);
            using var book = new XLWorkbook();
            var sheet = book.AddWorksheet("交易流水");
            string[] headers = ["交易ID", "业务日期", "类型", "资金账户", "转入账户", "币种", "金额（精确文本）", "说明", "原交易", "应收单", "应付单"];
            for (var col = 0; col < headers.Length; col++)
                sheet.Cell(1, col + 1).Value = headers[col];
            var row = 2;
            foreach (var t in rows)
            {
                string[] values = [t.Id.ToString(), t.BusinessDate.ToString("yyyy-MM-dd"), t.Kind, accounts[t.AccountId].Name, t.DestinationAccountId is null ? "" : accounts[t.DestinationAccountId.Value].Name, accounts[t.AccountId].Currency, Money(t.Amount), t.Description, t.ReversalOfId?.ToString() ?? "", t.ReceivableId?.ToString() ?? "", t.PayableId?.ToString() ?? ""];
                for (var col = 0; col < values.Length; col++)
                    sheet.Cell(row, col + 1).Value = values[col];
                row++;
            }
            var transactionIds = rows.Select(x => x.Id).ToArray();
            var allocationQuery = db.Set<TransactionAllocation>().Where(x => x.OrganizationId == org && transactionIds.Contains(x.TransactionId));
            if (await allocationQuery.CountAsync() > 50000)
                return Error(400, "分摊明细超过50000条，请缩小筛选范围");
            var allocationRows = await allocationQuery.Join(db.Set<Category>(), x => new { x.OrganizationId, Id = x.CategoryId }, c => new { c.OrganizationId, c.Id }, (x, c) => new { x.TransactionId, c.Name, x.Amount }).OrderBy(x => x.TransactionId).ThenBy(x => x.Name).ToListAsync();
            var allocationSheet = book.AddWorksheet("分类分摊");
            allocationSheet.Cell(1, 1).Value = "交易ID";
            allocationSheet.Cell(1, 2).Value = "分类";
            allocationSheet.Cell(1, 3).Value = "分摊金额（精确文本，非额外资金变动）";
            var allocationRow = 2;
            foreach (var a in allocationRows)
            {
                allocationSheet.Cell(allocationRow, 1).Value = a.TransactionId.ToString();
                allocationSheet.Cell(allocationRow, 2).Value = a.Name;
                allocationSheet.Cell(allocationRow++, 3).Value = Money(a.Amount);
            }
            allocationSheet.Columns().Width = 35;
            sheet.Row(1).Style.Font.Bold = true;
            sheet.SheetView.FreezeRows(1);
            sheet.Columns().Width = 22;
            sheet.Column(8).Width = 45;
            using var output = new MemoryStream();
            book.SaveAs(output);
            db.Add(Audit(org, u, "TransactionsExported", Guid.Empty, System.Text.Json.JsonSerializer.Serialize(new
            {
                count = rows.Count,
                dateFrom,
                dateTo,
                accountId
            })));
            await db.SaveChangesAsync();
            return Results.File(output.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "CFIS-transactions.xlsx");
        });
    }
}


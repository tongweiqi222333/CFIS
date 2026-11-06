using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public static class Demo
{
    static Guid Key(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
    public static void MapDemo(this WebApplication app)
    {
        app.MapPost("/api/v1/demo", async (ClaimsPrincipal user, AppDb db) =>
        {
            var uid = Finance.Uid(user);
            var org = Key("CFIS-v1-demo:" + uid);
            var prefix = org.ToString();
            Guid Id(string value) => Key(prefix + value);
            await db.Database.OpenConnectionAsync();
            var lockKey = "cfis-demo:" + uid;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({lockKey},0))");
            try
            {
                var organization = await db.Organizations.SingleOrDefaultAsync(x => x.Id == org);
                if (organization != null && !await db.Members.AnyAsync(x => x.OrganizationId == org && x.UserId == uid && x.Role == MemberRole.Owner))
                    return Results.Forbid();
                if (organization != null && await db.Set<AuditLog>().AnyAsync(x=>x.OrganizationId==org&&x.Action=="DemoCompleted"))
                    return Results.Ok(new{id=org,name=organization.Name,guide="演示账套已存在，保留你之后的操作，不重置数据。"});
                if (organization == null)
                {
                    await using var tx = await db.Database.BeginTransactionAsync();
                    organization = new Organization { Id = org, Name = "CFIS v1.0 演示账套（合成数据）" };
                    db.Add(organization);
                    db.Members.Add(new()
                    {
                        OrganizationId = org,
                        UserId = uid,
                        Role = MemberRole.Owner
                    });
                    var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(organization.CreatedAt, "Asia/Shanghai").DateTime);
                    var start = new DateOnly(day.Year, day.Month, 1);
                    var end = start.AddMonths(1).AddDays(-1);
                    foreach (var key in new[] { "bank", "cash" })
                        db.Add(new FinancialAccount { Id = Id(key), OrganizationId = org, Name = key == "bank" ? "演示银行" : "演示现金", Kind = key == "bank" ? "Bank" : "Cash" });
                    db.AddRange(new Category { Id = Id("income"), OrganizationId = org, Name = "演示销售", Kind = "Income" }, new Category { Id = Id("office"), OrganizationId = org, Name = "演示办公", Kind = "Expense" }, new Category { Id = Id("travel"), OrganizationId = org, Name = "演示差旅", Kind = "Expense" });
                    db.Add(new Counterparty { Id = Id("party"), OrganizationId = org, Name = "演示客户供应商", Kind = "Both" });
                    db.Add(new Receivable { Id = Id("ar"), OrganizationId = org, CounterpartyId = Id("party"), CategoryId = Id("income"), Number = "DEMO-AR", Amount = 3000, IssueDate = day, DueDate = day.AddDays(10), Description = "合成演示应收" });
                    db.Add(new Payable { Id = Id("ap"), OrganizationId = org, CounterpartyId = Id("party"), CategoryId = Id("office"), Number = "DEMO-AP", Amount = 2000, IssueDate = day, DueDate = day.AddDays(5), Description = "合成演示应付" });
                    db.Add(new Budget { Id = Id("budget"), OrganizationId = org, Name = "演示月预算", StartDate = start, EndDate = end, Status = "Active", ActivatedAt = DateTimeOffset.UtcNow });
                    foreach (var category in new[] { "office", "travel" })
                        db.Add(new BudgetItem { OrganizationId = org, BudgetId = Id("budget"), CategoryId = Id(category), Amount = 700 });
                    db.Add(new FiscalPeriod { Id = Id("period"), OrganizationId = org, Code = "DEMO", StartDate = start, EndDate = end });
                    var ledgers = new[] { ("asset", "1002", "银行现金", "Asset"), ("equity", "4001", "期初权益", "Equity"), ("revenue", "6001", "营业收入", "Revenue"), ("expense", "6401", "期间费用", "Expense"), ("arLedger", "1122", "应收账款", "Asset"), ("apLedger", "2202", "应付账款", "Liability") };
                    foreach (var (key, code, name, kind) in ledgers)
                        db.Add(new LedgerAccount { Id = Id(key), OrganizationId = org, Code = code, Name = "演示" + name, Kind = kind });
                    void Map(string kind, string resource, string ledger) => db.Add(new AccountingMapping { OrganizationId = org, Kind = kind, ResourceId = resource == "" ? Guid.Empty : Id(resource), LedgerAccountId = Id(ledger) });
                    Map("FinancialAccount", "bank", "asset");
                    Map("FinancialAccount", "cash", "asset");
                    Map("Category", "income", "revenue");
                    Map("Category", "office", "expense");
                    Map("Category", "travel", "expense");
                    Map("Opening", "", "equity");
                    Map("Receivable", "", "arLedger");
                    Map("Payable", "", "apLedger");
                    db.Add(Finance.Audit(org, user, "DemoCreated", org, "合成数据，仅用于演示"));
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(organization.CreatedAt, "Asia/Shanghai").DateTime);
                var requests = new[] { new PostingInput(Id("opening"), Id("bank"), null, null, "Opening", "10000", "演示期初", date), new PostingInput(Id("sale"), Id("bank"), null, Id("income"), "Income", "4500", "演示销售收款", date), new PostingInput(Id("split"), Id("bank"), null, Id("office"), "Expense", "1000", "演示办公差旅分摊", date, Allocations: [new(Id("office"), "600"), new(Id("travel"), "400")]), new PostingInput(Id("transfer"), Id("bank"), Id("cash"), null, "Transfer", "500", "演示内部转账", date), new PostingInput(Id("arPayment"), Id("bank"), null, Id("income"), "Income", "1500", "演示部分收款", date, Id("ar")), new PostingInput(Id("apPayment"), Id("bank"), null, Id("office"), "Expense", "500", "演示部分付款", date, null, Id("ap")) };
                foreach (var request in requests)
                {
                    var result = await new TransactionPostingService(db).Post(org, request, user, request.Kind == "Transfer");
                    if (result is IStatusCodeHttpResult status && status.StatusCode >= 400)
                        return result;
                    db.ChangeTracker.Clear();
                }
                var accounting = new AccountingService(db);
                foreach (var (kind, id) in new[] { ("Receivable", Id("ar")), ("Payable", Id("ap")) })
                {
                    var entry = await accounting.Import(org, new(Id("journal" + id), kind, id), user);
                    await accounting.Post(org, entry.Id, new(entry.Version), user);
                }
                foreach (var request in requests)
                {
                    var source = await db.Set<FinancialTransaction>().Where(x => x.OrganizationId == org && x.RequestId == request.RequestId).Select(x => x.Id).SingleAsync();
                    var entry = await accounting.Import(org, new(Id("journal" + source), "Transaction", source), user);
                    await accounting.Post(org, entry.Id, new(entry.Version), user);
                }
                var manualId = Id("manual");
                var draftId = Id("draft");
                var attachmentId = Id("attachment");
                if (!await db.Set<JournalEntry>().AnyAsync(x => x.OrganizationId == org && x.RequestId == manualId))
                {
                    var entry = await accounting.CreateDraft(org, new(Id("manual"), date, "CNY", "演示手工调整及冲销", [new(Id("expense"), "25.1234", "0"), new(Id("equity"), "0", "25.1234")]), user);
                    await accounting.Post(org, entry.Id, new(entry.Version), user);
                    await accounting.Reverse(org, entry.Id, new(Id("manualReverse"), date, "演示追加冲销，原凭证保留"), user);
                }
                if (!await db.Set<JournalEntry>().AnyAsync(x => x.OrganizationId == org && x.RequestId == draftId))
                    await accounting.CreateDraft(org, new(Id("draft"), date, "CNY", "演示待审核草稿（结账前请处理）", [new(Id("expense"), "20", "0"), new(Id("equity"), "0", "20")]), user);
                if (!await db.Set<Attachment>().AnyAsync(x => x.Id == attachmentId))
                {
                    var content = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aO9sAAAAASUVORK5CYII=");
                    db.Add(new Attachment { Id = Id("attachment"), OrganizationId = org, ResourceKind = "Receivable", ResourceId = Id("ar"), Name = "演示附件.png", ContentType = "image/png", Content = content, Sha256 = Convert.ToHexString(SHA256.HashData(content)), ActorId = uid });
                    db.Add(Finance.Audit(org, user, "AttachmentAdded", Id("attachment"), "明确标识的合成演示附件"));
                    await db.SaveChangesAsync();
                }
                db.Add(Finance.Audit(org,user,"DemoCompleted",org,"合成账套初始化完成，后续打开不重置"));await db.SaveChangesAsync();
                return Results.Ok(new
                {
                    id = org,
                    name = organization.Name,
                    date,
                    guide = "这是独立合成账套，可查看分摊、部分结算、预算预警、现金流和会计对账；重复生成不会重复入账。"
                });
            }
            catch(AccountingFault ex){return Finance.Error(ex.Status,ex.Message);}
            finally { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({lockKey},0))"); await db.Database.CloseConnectionAsync(); }
        }).RequireAuthorization();
    }
}

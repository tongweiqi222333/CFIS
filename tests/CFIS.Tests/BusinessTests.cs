using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using CFIS.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
public sealed class BusinessTests:IAsyncLifetime {
 TestDatabase database=null!;WebApplicationFactory<Program> factory=null!;HttpClient client=null!;
 Guid org,party,account,usd,income,expense;string root="";
 public async Task InitializeAsync(){
 database=new();await database.InitializeAsync();
 factory=new WebApplicationFactory<Program>().WithWebHostBuilder(w=>w.UseSetting("ConnectionStrings:Database",database.ConnectionString));
 client=factory.CreateClient();await Register(client);
 org=await Create("/api/v1/organizations",new{name="Phase3测试"});root="/api/v1/organizations/"+org;
 party=await Create(root+"/counterparties",new{name="客户及供应商",kind="Both",contact="测试联系"});
 account=await Create(root+"/accounts",new{name="银行",currency="CNY",kind="Bank"});
 usd=await Create(root+"/accounts",new{name="美元",currency="USD",kind="Bank"});
 income=await Create(root+"/categories",new{name="收款",kind="Income"});
 expense=await Create(root+"/categories",new{name="付款",kind="Expense"});
 }
 async Task<string> Register(HttpClient c){var email=Guid.NewGuid()+"@test.local";var body=new{email,password="Business-Test!123456"};(await c.PostAsJsonAsync("/api/v1/auth/register",body)).EnsureSuccessStatusCode();var token=await(await c.PostAsJsonAsync("/api/v1/auth/login",body)).Content.ReadFromJsonAsync<JsonElement>();c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.GetProperty("accessToken").GetString());return email;}
 async Task<Guid> Create(string path,object body){var r=await client.PostAsJsonAsync(path,body);r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
 object Doc(string number,bool receivable,string amount,Guid? counterparty=null)=>new{number,counterpartyId=counterparty??party,categoryId=receivable?income:expense,currency="CNY",amount,issueDate="2026-10-01",dueDate="2026-10-10",description="测试单据"};
 object Settlement(string amount,Guid? request=null,Guid? target=null)=>new{requestId=request??Guid.NewGuid(),accountId=target??account,amount,businessDate="2026-10-02",description="结算测试"};
 async Task<JsonElement> Get(string path)=>await client.GetFromJsonAsync<JsonElement>(root+path);
 async Task Seed()=> (await client.PostAsJsonAsync(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=account,kind="Opening",amount="1000",description="测试期初",businessDate="2026-10-01"})).EnsureSuccessStatusCode();
 public async Task DisposeAsync(){client.Dispose();await factory.DisposeAsync();await database.DisposeAsync();}
 [Fact]public async Task ReceivablePartialPaymentIdempotencyAndReversal(){
 var id=await Create(root+"/receivables",Doc("AR-001",true,"100.0001"));
 Assert.Equal("100.0001",(await Get("/receivables/"+id)).GetProperty("amount").GetString());
 var request=Guid.NewGuid();var path=root+"/receivables/"+id+"/settlements";
 var paid=await client.PostAsJsonAsync(path,Settlement("40.0001",request));Assert.Equal(HttpStatusCode.Created,paid.StatusCode);
 var transaction=await paid.Content.ReadFromJsonAsync<JsonElement>();
 Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(path,Settlement("40.0001",request))).StatusCode);
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(path,Settlement("61"))).StatusCode);
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(root+"/receivables/"+id+"/cancel",new{})).StatusCode);
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(path,Settlement("1",target:usd))).StatusCode);
 var row=(await Get("/receivables")).GetProperty("items")[0];Assert.Equal("60.0000",row.GetProperty("remaining").GetString());
 (await client.PostAsJsonAsync(root+"/transactions/"+transaction.GetProperty("id").GetGuid()+"/reverse",new{requestId=Guid.NewGuid(),reason="结算更正",businessDate="2026-10-03"})).EnsureSuccessStatusCode();
 row=(await Get("/receivables")).GetProperty("items")[0];Assert.Equal("0.0000",row.GetProperty("settled").GetString());
 (await client.PostAsJsonAsync(root+"/receivables/"+id+"/cancel",new{})).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(path,Settlement("1"))).StatusCode);
 }
 [Fact]public async Task PayableConcurrentSettlementAndBudgetNetReversal(){
 await Seed();
 var id=await Create(root+"/payables",Doc("AP-001",false,"80"));
 var budget=await Create(root+"/budgets",new{name="十月预算",currency="CNY",startDate="2026-10-01",endDate="2026-10-31",items=new[]{new{categoryId=expense,amount="70"}}});
 Assert.Equal("70.0000",(await Get("/budgets/"+budget)).GetProperty("items")[0].GetProperty("amount").GetString());
 var path=root+"/payables/"+id+"/settlements";
 var race=await Task.WhenAll(client.PostAsJsonAsync(path,Settlement("50")),client.PostAsJsonAsync(path,Settlement("50")));
 Assert.Single(race,x=>x.StatusCode==HttpStatusCode.Created);Assert.Single(race,x=>x.StatusCode==HttpStatusCode.Conflict);
 var posted=await race.Single(x=>x.StatusCode==HttpStatusCode.Created).Content.ReadFromJsonAsync<JsonElement>();
 (await client.PostAsJsonAsync(path,Settlement("30"))).EnsureSuccessStatusCode();
 Assert.Equal("Paid",(await Get("/payables")).GetProperty("items")[0].GetProperty("status").GetString());
 var execution=(await Get("/budgets/"+budget+"/execution")).GetProperty("items")[0];
 Assert.Equal("80.0000",execution.GetProperty("actual").GetString());Assert.True(execution.GetProperty("overBudget").GetBoolean());
 (await client.PostAsJsonAsync(root+"/transactions/"+posted.GetProperty("id").GetGuid()+"/reverse",new{requestId=Guid.NewGuid(),reason="退款",businessDate="2026-10-03"})).EnsureSuccessStatusCode();
 execution=(await Get("/budgets/"+budget+"/execution")).GetProperty("items")[0];
 Assert.Equal("30.0000",execution.GetProperty("actual").GetString());Assert.False(execution.GetProperty("overBudget").GetBoolean());
 Assert.Equal("50.0000",(await Get("/payables")).GetProperty("items")[0].GetProperty("remaining").GetString());
 Assert.All((await Get("/reconciliation")).EnumerateArray(),x=>Assert.Equal("0.0000",x.GetProperty("difference").GetString()));
 }
 [Fact]public async Task IsolationPermissionsAndFormulaSafePreciseExcel(){
 using var outsider=factory.CreateClient();var email=await Register(outsider);
 Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync(root+"/counterparties")).StatusCode);
 foreach(var format in new[]{"xlsx","pdf"})Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync(root+"/reports/export."+format+"?dateFrom=2026-10-01&dateTo=2026-10-31&currency=CNY")).StatusCode);
 var other=await Create("/api/v1/organizations",new{name="其他企业"});
 var foreign=await Create("/api/v1/organizations/"+other+"/counterparties",new{name="他企",kind="Customer"});
 Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(root+"/receivables",Doc("WRONG",true,"1",foreign))).StatusCode);
 (await client.PostAsJsonAsync(root+"/members",new{email,role=3})).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.Forbidden,(await outsider.PostAsJsonAsync(root+"/counterparties",new{name="越权",kind="Customer"})).StatusCode);
 (await client.PostAsJsonAsync(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=account,kind="Opening",amount="999999999999999.9999",description="=HYPERLINK(\"http://example.test\")",businessDate="2026-10-01"})).EnsureSuccessStatusCode();
 var response=await outsider.GetAsync(root+"/exports/transactions.xlsx");response.EnsureSuccessStatusCode();
 Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",response.Content.Headers.ContentType!.MediaType);
 using var book=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
 var sheet=book.Worksheet(1);Assert.Equal("999999999999999.9999",sheet.Cell(2,7).GetString());Assert.False(sheet.Cell(2,8).HasFormula);
 }
 [Fact]public async Task FailedAuditRollsBackSettlementAndBalance(){
 await Seed();var id=await Create(root+"/payables",Doc("FAIL",false,"100"));
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);
 await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_business_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test failure'; END $$; CREATE TRIGGER reject_business_audit BEFORE INSERT ON audit_logs FOR EACH ROW EXECUTE FUNCTION reject_business_audit();");
 Assert.Equal(HttpStatusCode.InternalServerError,(await client.PostAsJsonAsync(root+"/payables/"+id+"/settlements",Settlement("10"))).StatusCode);
 Assert.Equal(1000m,await db.Set<FinancialAccount>().Where(x=>x.Id==account).Select(x=>x.Balance).SingleAsync());
 Assert.Equal("100.0000",(await Get("/payables")).GetProperty("items")[0].GetProperty("remaining").GetString());
 }

 [Fact]public async Task CreationRetryAndStatusFiltering(){
 var request=Guid.NewGuid();var body=new{requestId=request,number="RETRY",counterpartyId=party,categoryId=income,currency="CNY",amount="20",issueDate="2026-10-01",dueDate="2026-10-10"};
 var race=await Task.WhenAll(client.PostAsJsonAsync(root+"/receivables",body),client.PostAsJsonAsync(root+"/receivables",body));
 Assert.Single(race,x=>x.StatusCode==HttpStatusCode.Created);Assert.Single(race,x=>x.StatusCode==HttpStatusCode.OK);
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(root+"/receivables",new{requestId=request,number="DIFFERENT",counterpartyId=party,categoryId=income,currency="CNY",amount="20",issueDate="2026-10-01",dueDate="2026-10-10"})).StatusCode);
 Assert.Equal(1,(await Get("/receivables?status=Overdue&currency=CNY&dueFrom=2026-10-01&sort=desc")).GetProperty("total").GetInt32());
 (await client.PostAsJsonAsync(root+"/receivables/"+request+"/settlements",Settlement("20"))).EnsureSuccessStatusCode();
 Assert.Equal(1,(await Get("/receivables?status=Paid")).GetProperty("total").GetInt32());
 Assert.Equal(0,(await Get("/receivables?status=Overdue")).GetProperty("total").GetInt32());
 }
 [Fact]public async Task BudgetVersionsOverlapAndRetry(){
 var request=Guid.NewGuid();var body=new{requestId=request,name="版本预算",currency="CNY",startDate="2026-10-01",endDate="2026-10-31",items=new[]{new{categoryId=expense,amount="20"}}};
 var first=await Create(root+"/budgets",body);Assert.Equal(request,first);
 Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(root+"/budgets",body)).StatusCode);
 Assert.Equal("Draft",(await Get("/budgets/"+first)).GetProperty("status").GetString());
 (await client.PostAsJsonAsync(root+"/budgets/"+first+"/status",new{status="Active"})).EnsureSuccessStatusCode();
 var second=await Create(root+"/budgets",new{name="调整预算",previousVersionId=first,currency="CNY",startDate="2026-10-01",endDate="2026-10-31",items=new[]{new{categoryId=expense,amount="30"}}});
 (await client.PostAsJsonAsync(root+"/budgets/"+second+"/status",new{status="Active"})).EnsureSuccessStatusCode();
 Assert.Equal("Closed",(await Get("/budgets/"+first)).GetProperty("status").GetString());
 var overlap=await Create(root+"/budgets",new{name="冲突预算",currency="CNY",startDate="2026-10-31",endDate="2026-11-01",items=new[]{new{categoryId=expense,amount="40"}}});
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(root+"/budgets/"+overlap+"/status",new{status="Active"})).StatusCode);
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(root+"/budgets/"+first+"/status",new{status="Active"})).StatusCode);
 }
 [Fact]public async Task SettlementCancellationAndReversalRaces(){
 await Seed();var id=await Create(root+"/receivables",Doc("RACE",true,"20"));
 var race=await Task.WhenAll(client.PostAsJsonAsync(root+"/receivables/"+id+"/settlements",Settlement("20")),client.PostAsJsonAsync(root+"/receivables/"+id+"/cancel",new{}));
 Assert.Single(race,x=>x.IsSuccessStatusCode);Assert.Single(race,x=>x.StatusCode==HttpStatusCode.Conflict);
 var next=await Create(root+"/payables",Doc("REV-RACE",false,"40"));
 var posted=await Create(root+"/payables/"+next+"/settlements",Settlement("20"));
 var operations=await Task.WhenAll(client.PostAsJsonAsync(root+"/payables/"+next+"/settlements",Settlement("20",target:account)),client.PostAsJsonAsync(root+"/transactions/"+posted+"/reverse",new{requestId=Guid.NewGuid(),reason="并发退款",businessDate="2026-10-03"}));
 Assert.All(operations,x=>x.EnsureSuccessStatusCode());
 Assert.Equal("20.0000",(await Get("/payables/"+next)).GetProperty("remaining").GetString());
 }
 [Fact]public async Task CorrectionReportingAndResourceRules(){
 await Seed();
 var original=await Create(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=account,categoryId=expense,kind="Expense",amount="10",businessDate="2026-10-02"});
 (await client.PostAsJsonAsync(root+"/transactions/"+original+"/reverse",new{requestId=Guid.NewGuid(),reason="更正",businessDate="2026-10-03"})).EnsureSuccessStatusCode();
 var request=Guid.NewGuid();var body=new{requestId=request,correctionOfId=original,accountId=account,categoryId=expense,kind="Expense",amount="12",businessDate="2026-10-03"};
 var correction=await Create(root+"/transactions",body);
 Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(root+"/transactions",body)).StatusCode);
 Assert.Equal(correction,(await Get("/transactions/"+original)).GetProperty("correctedBy").GetGuid());
 var basis=(await Get("/reporting-basis")).GetProperty("items")[0];Assert.Equal("12.0000",basis.GetProperty("expense").GetString());Assert.Equal("0.0000",basis.GetProperty("income").GetString());Assert.Equal("1000.0000",basis.GetProperty("opening").GetString());
 var audit=await Get("/audit-logs?resourceId="+correction+"&action=TransactionPosted");Assert.Equal(1,audit.GetProperty("total").GetInt32());
 var doc=await Create(root+"/receivables",Doc("INACTIVE-PARTY",true,"1"));
 (await client.PatchAsJsonAsync(root+"/counterparties/"+party,new{name="已停用往来方",status="Inactive"})).EnsureSuccessStatusCode();
 (await client.PostAsJsonAsync(root+"/receivables/"+doc+"/settlements",Settlement("1"))).EnsureSuccessStatusCode();
 (await client.PatchAsJsonAsync(root+"/accounts/"+account,new{name="已停用账户",status="Inactive"})).EnsureSuccessStatusCode();
 var denied=await client.PostAsJsonAsync(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=account,categoryId=income,kind="Income",amount="1"});
 Assert.Equal("RESOURCE_INACTIVE",(await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
 }

 [Fact]public async Task HistoricalWarningsAndRemoteLookup(){
 await Seed();
 (await client.PostAsJsonAsync(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=account,categoryId=expense,kind="Expense",amount="10",businessDate="2026-09-30"})).EnsureSuccessStatusCode();
 var warnings=await Get("/historical-balance-warnings");Assert.Equal("-10.0000",warnings.GetProperty("items")[0].GetProperty("balance").GetString());
 var future=DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1).ToString("yyyy-MM-dd");
 (await client.PostAsJsonAsync(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=account,categoryId=income,kind="Income",amount="1",businessDate=future})).EnsureSuccessStatusCode();
 Assert.Equal(0,(await Get("/accounts?search=不存在")).GetProperty("total").GetInt32());
 Assert.Equal(1,(await Get("/categories?search=付款")).GetProperty("total").GetInt32());
 }
 [Fact]public async Task MultiAccountSettlementAndLiveRoleChange(){
 var other=await Create(root+"/accounts",new{name="另一个人民币账户",currency="CNY",kind="Cash"});
 var doc=await Create(root+"/receivables",Doc("MULTI",true,"30"));
 var race=await Task.WhenAll(client.PostAsJsonAsync(root+"/receivables/"+doc+"/settlements",Settlement("20")),client.PostAsJsonAsync(root+"/receivables/"+doc+"/settlements",Settlement("20",target:other)));
 Assert.Single(race,x=>x.StatusCode==HttpStatusCode.Created);Assert.Single(race,x=>x.StatusCode==HttpStatusCode.Conflict);
 Assert.Equal("10.0000",(await Get("/receivables/"+doc)).GetProperty("remaining").GetString());
 using var member=factory.CreateClient();var email=await Register(member);(await client.PostAsJsonAsync(root+"/members",new{email,role=2})).EnsureSuccessStatusCode();
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);
 var uid=await db.Users.Where(x=>x.Email==email).Select(x=>x.Id).SingleAsync();var membership=await db.Members.SingleAsync(x=>x.OrganizationId==org&&x.UserId==uid);membership.Role=MemberRole.Viewer;await db.SaveChangesAsync();
 Assert.Equal(HttpStatusCode.Forbidden,(await member.PostAsJsonAsync(root+"/accounts",new{name="旧会话越权",currency="CNY",kind="Bank"})).StatusCode);
 Assert.False(Organizations.CanManage(MemberRole.Admin,MemberRole.Owner));Assert.False(Organizations.CanManage(MemberRole.Owner,MemberRole.Owner));
 }

 [Fact]public async Task ReportsReconcileTransfersDatesAgingAndExports(){
 await Seed();var other=await Create(root+"/accounts",new{name="报表转入账户",currency="CNY",kind="Cash"});
 (await client.PostAsJsonAsync(root+"/transfers",new{requestId=Guid.NewGuid(),accountId=account,destinationAccountId=other,kind="Transfer",amount="100",businessDate="2026-10-02"})).EnsureSuccessStatusCode();
 var doc=await Create(root+"/receivables",Doc("AGING",true,"120"));
 (await client.PostAsJsonAsync(root+"/receivables/"+doc+"/settlements",Settlement("40"))).EnsureSuccessStatusCode();
 (await client.PostAsJsonAsync(root+"/receivables/"+doc+"/settlements",new{requestId=Guid.NewGuid(),accountId=account,amount="10",businessDate="2026-11-01"})).EnsureSuccessStatusCode();
 const string query="?dateFrom=2026-10-01&dateTo=2026-10-31&currency=CNY";
 var report=await Get("/reports/summary"+query);var summary=report.GetProperty("summary");
 Assert.Equal("1040.0000",summary.GetProperty("closing").GetString());Assert.Equal("1050.0000",summary.GetProperty("allRegistered").GetString());Assert.Equal("0.0000",summary.GetProperty("transferOut").GetString());
 var aging=report.GetProperty("aging").EnumerateArray().Single(x=>x.GetProperty("type").GetString()=="Receivable"&&x.GetProperty("bucket").GetString()=="1-30");Assert.Equal("80.0000",aging.GetProperty("amount").GetString());
 var accountReport=await Get("/reports/summary"+query+"&accountId="+account);Assert.Equal(940m,accountReport.GetProperty("trend").EnumerateArray().Sum(x=>decimal.Parse(x.GetProperty("cashNet").GetString()!,System.Globalization.CultureInfo.InvariantCulture)));var single=accountReport.GetProperty("summary");Assert.Equal("100.0000",single.GetProperty("transferOut").GetString());Assert.Equal("940.0000",single.GetProperty("closing").GetString());
 var excel=await client.GetAsync(root+"/reports/export.xlsx"+query);excel.EnsureSuccessStatusCode();using var book=new XLWorkbook(new MemoryStream(await excel.Content.ReadAsByteArrayAsync()));Assert.Contains(book.Worksheet(1).RowsUsed(),r=>r.Cell(1).GetString()=="期末余额"&&r.Cell(2).GetString()=="1040.0000");
 var pdf=await client.GetAsync(root+"/reports/export.pdf"+query);pdf.EnsureSuccessStatusCode();var bytes=await pdf.Content.ReadAsByteArrayAsync();Assert.StartsWith("%PDF",System.Text.Encoding.ASCII.GetString(bytes[..8]));Directory.CreateDirectory("output/pdf");await File.WriteAllBytesAsync("output/pdf/cfis-report-validation.pdf",bytes);
 Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(root+"/reports/summary?dateFrom=2020-01-01&dateTo=2026-10-31&currency=CNY")).StatusCode);
 }
 [Fact]public async Task ReportsPreserveKnowledgeCutoffAndBudgetVersions(){
 var doc=await Create(root+"/receivables",Doc("HISTORY",true,"25"));var cutoff=DateTimeOffset.UtcNow;await Task.Delay(10);
 (await client.PostAsJsonAsync(root+"/receivables/"+doc+"/cancel",new{})).EnsureSuccessStatusCode();
 var query="?dateFrom=2026-10-01&dateTo=2026-10-31&currency=CNY";
 var before=await Get("/reports/summary"+query+"&knowledgeAt="+Uri.EscapeDataString(cutoff.ToString("O")));Assert.Equal("25.0000",before.GetProperty("aging").EnumerateArray().Single(x=>x.GetProperty("type").GetString()=="Receivable"&&x.GetProperty("bucket").GetString()=="1-30").GetProperty("amount").GetString());
 Assert.All((await Get("/reports/summary"+query)).GetProperty("aging").EnumerateArray(),x=>Assert.Equal("0.0000",x.GetProperty("amount").GetString()));
 Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(root+"/reports/summary"+query+"&knowledgeAt=2020-01-01T00:00:00Z")).StatusCode);
 var budget=await Create(root+"/budgets",new{name="历史预算",currency="CNY",startDate="2026-10-01",endDate="2026-10-31",items=new[]{new{categoryId=expense,amount="10"}}});
 (await client.PostAsJsonAsync(root+"/budgets/"+budget+"/status",new{status="Active"})).EnsureSuccessStatusCode();var activeAt=DateTimeOffset.UtcNow;await Task.Delay(10);
 (await client.PostAsJsonAsync(root+"/budgets/"+budget+"/status",new{status="Closed"})).EnsureSuccessStatusCode();
 Assert.Single((await Get("/reports/summary"+query+"&knowledgeAt="+Uri.EscapeDataString(activeAt.ToString("O")))).GetProperty("budgets").EnumerateArray());Assert.Empty((await Get("/reports/summary"+query)).GetProperty("budgets").EnumerateArray());
 }

 [Fact]public async Task StrictCreationPolicyAndEmptyPreciseReports(){
 await using var strict=factory.WithWebHostBuilder(w=>w.UseSetting("Business:RequireIdempotencyKeys","true"));using var c=strict.CreateClient();c.DefaultRequestHeaders.Authorization=client.DefaultRequestHeaders.Authorization;
 Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync(root+"/counterparties",new{name="请求编号必填",kind="Customer"})).StatusCode);
 var request=Guid.NewGuid();var body=new{requestId=request,name="严格创建",kind="Customer"};Assert.Equal(HttpStatusCode.Created,(await c.PostAsJsonAsync(root+"/counterparties",body)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync(root+"/counterparties",body)).StatusCode);
 var empty=await Get("/reports/summary?dateFrom=2026-10-01&dateTo=2026-10-01&currency=USD");Assert.Equal("0.0000",empty.GetProperty("summary").GetProperty("closing").GetString());Assert.Single(empty.GetProperty("trend").EnumerateArray());
 (await client.PostAsJsonAsync(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=account,kind="Opening",amount="999999999999999.9999",description="大金额验收",businessDate="2026-10-01"})).EnsureSuccessStatusCode();
 var big=await Get("/reports/summary?dateFrom=2026-10-01&dateTo=2026-10-31&currency=CNY");Assert.Equal("999999999999999.9999",big.GetProperty("summary").GetProperty("closing").GetString());
 using var outsider=factory.CreateClient();await Register(outsider);Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync(root+"/reports/summary?dateFrom=2026-10-01&dateTo=2026-10-31&currency=CNY")).StatusCode);
 }
}


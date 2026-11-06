using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using CFIS.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
public sealed class AccountingTests:IAsyncLifetime {
 TestDatabase database=null!;WebApplicationFactory<Program> factory=null!;HttpClient client=null!;Guid org,period,bank,capital,revenue,expense,ar,ap,cash,incomeCategory,expenseCategory,party;string root="",accounting="";
 public async Task InitializeAsync(){database=new();await database.InitializeAsync();factory=new WebApplicationFactory<Program>().WithWebHostBuilder(w=>w.UseSetting("ConnectionStrings:Database",database.ConnectionString));client=factory.CreateClient();await Login(client);org=await Create("/api/v1/organizations",new{name="会计测试"});root="/api/v1/organizations/"+org;accounting=root+"/accounting";
 period=await Create(accounting+"/periods",new{code="2026-10",startDate="2026-10-01",endDate="2026-10-31"});
 bank=await Ledger("1002","银行","Asset");capital=await Ledger("4001","实收资本","Equity");revenue=await Ledger("6001","业务收入","Revenue");expense=await Ledger("6401","业务成本","Expense");ar=await Ledger("1122","应收账款","Asset");ap=await Ledger("2202","应付账款","Liability");
 cash=await Create(root+"/accounts",new{name="资金账户",currency="CNY",kind="Bank"});incomeCategory=await Create(root+"/categories",new{name="收入",kind="Income"});expenseCategory=await Create(root+"/categories",new{name="费用",kind="Expense"});party=await Create(root+"/counterparties",new{name="客户供应商",kind="Both"});
 await Mapping("FinancialAccount",cash,bank);await Mapping("Category",incomeCategory,revenue);await Mapping("Category",expenseCategory,expense);await Mapping("Opening",Guid.Empty,capital);await Mapping("Receivable",Guid.Empty,ar);await Mapping("Payable",Guid.Empty,ap);
 }
 async Task Login(HttpClient c){var body=new{email=Guid.NewGuid()+"@accounting.test",password="Accounting-Test!123456"};(await c.PostAsJsonAsync("/api/v1/auth/register",body)).EnsureSuccessStatusCode();var token=await(await c.PostAsJsonAsync("/api/v1/auth/login",body)).Content.ReadFromJsonAsync<JsonElement>();c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.GetProperty("accessToken").GetString());}
 async Task<Guid> Create(string path,object body){var r=await client.PostAsJsonAsync(path,body);if(!r.IsSuccessStatusCode)throw new Exception(r.StatusCode+":"+await r.Content.ReadAsStringAsync());return(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
 async Task<Guid> Ledger(string code,string name,string kind)=>await Create(accounting+"/accounts",new{code,name,kind,currency="CNY"});
 async Task Mapping(string kind,Guid resourceId,Guid ledgerAccountId)=>(await client.PutAsJsonAsync(accounting+"/mappings",new{kind,resourceId,currency="CNY",ledgerAccountId})).EnsureSuccessStatusCode();
 async Task<JsonElement> Get(string path)=>await client.GetFromJsonAsync<JsonElement>(path);
 object Journal(Guid request,string debit="10.0001",string credit="10.0001",Guid? debitAccount=null)=>new{requestId=request,businessDate="2026-10-02",currency="CNY",description="测试复式分录",lines=new[]{new{ledgerAccountId=debitAccount??bank,debit,credit="0"},new{ledgerAccountId=capital,debit="0",credit}}};
 async Task<JsonElement> Post(Guid id){var e=(await Get(accounting+"/journals/"+id)).GetProperty("entry");var r=await client.PostAsJsonAsync(accounting+"/journals/"+id+"/post",new{version=e.GetProperty("version").GetGuid()});if(!r.IsSuccessStatusCode)throw new Exception(r.StatusCode+":"+await r.Content.ReadAsStringAsync());return await r.Content.ReadFromJsonAsync<JsonElement>();}
 async Task<Guid> Import(string sourceKind,Guid sourceId)=>await Create(accounting+"/import",new{requestId=Guid.NewGuid(),sourceKind,sourceId});
 async Task<Guid> Cash(string kind,string amount,Guid? category=null,string date="2026-10-02")=>await Create(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=cash,kind,amount,categoryId=category,description="资金来源",businessDate=date});
 public async Task DisposeAsync(){client.Dispose();await factory.DisposeAsync();await database.DisposeAsync();}
 [Fact]public async Task PrecheckReportsBlockersWithoutClosingPeriod(){
 var source=await Cash("Opening","10");
 var check=await Get(accounting+"/periods/"+period+"/precheck");Assert.False(check.GetProperty("canClose").GetBoolean());Assert.Equal(1,check.GetProperty("missingCash").GetInt32());Assert.Equal("Open",check.GetProperty("status").GetString());
 await Post(await Import("Transaction",source));
 Assert.True((await Get(accounting+"/periods/"+period+"/precheck")).GetProperty("canClose").GetBoolean());
 (await client.PostAsJsonAsync(accounting+"/periods/"+period+"/close",new{})).EnsureSuccessStatusCode();
 Assert.False((await Get(accounting+"/periods/"+period+"/precheck")).GetProperty("canClose").GetBoolean());
 }
 [Fact]public async Task DatabaseRejectsSourceMismatchAndCashDifferenceOnClose(){
 var source=await Cash("Opening","10");var draft=await Import("Transaction",source);
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);
 await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE journal_lines SET \"Debit\"=CASE WHEN \"Debit\">0 THEN 11 ELSE 0 END,\"Credit\"=CASE WHEN \"Credit\">0 THEN 11 ELSE 0 END WHERE \"EntryId\"={draft}");
 var ex=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE journal_entries SET \"Status\"='Posted',\"Number\"='INVALID-SOURCE',\"PostedAt\"=now(),\"DebitTotal\"=11,\"CreditTotal\"=11 WHERE \"Id\"={draft}"));Assert.Equal("23514",ex.SqlState);
 await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE journal_lines SET \"Debit\"=CASE WHEN \"Debit\">0 THEN 10 ELSE 0 END,\"Credit\"=CASE WHEN \"Credit\">0 THEN 10 ELSE 0 END WHERE \"EntryId\"={draft}");
 var dateMismatch=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE journal_entries SET \"Status\"='Posted',\"Number\"='INVALID-DATE',\"PostedAt\"=now(),\"DebitTotal\"=10,\"CreditTotal\"=10,\"BusinessDate\"=DATE '2026-10-03' WHERE \"Id\"={draft}"));Assert.Equal("23514",dateMismatch.SqlState);await Post(draft);
 await Post(await Create(accounting+"/journals",Journal(Guid.NewGuid())));
 var close=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fiscal_periods SET \"Status\"='Closed' WHERE \"Id\"={period}"));Assert.Equal("23514",close.SqlState);
 }
 [Fact]public async Task HistoricalSetupAndPartialBatchRetryRemainIdempotent(){
 var source=await Cash("Opening","10");var setup=await Get(accounting+"/setup-check");Assert.Equal(1,setup.GetProperty("pendingCash").GetInt32());
 var blockers=await Get(accounting+"/periods/"+period+"/blockers?kind=Transaction");Assert.Equal(source,blockers.GetProperty("items")[0].GetProperty("id").GetGuid());
 var body=new[]{new{requestId=Guid.NewGuid(),sourceKind="Transaction",sourceId=source},new{requestId=Guid.NewGuid(),sourceKind="Transaction",sourceId=Guid.NewGuid()}};
 for(var i=0;i<2;i++){var r=await(await client.PostAsJsonAsync(accounting+"/batch-import",body)).Content.ReadFromJsonAsync<JsonElement>();Assert.True(r.GetProperty("items")[0].GetProperty("success").GetBoolean());Assert.False(r.GetProperty("items")[1].GetProperty("success").GetBoolean());}
 var entry=(await Get(accounting+"/journals")).GetProperty("items");Assert.Single(entry.EnumerateArray());var id=entry[0].GetProperty("id").GetGuid();var version=entry[0].GetProperty("version").GetGuid();
 var posting=new[]{new{id,version}};for(var i=0;i<2;i++){var r=await(await client.PostAsJsonAsync(accounting+"/batch-post",posting)).Content.ReadFromJsonAsync<JsonElement>();Assert.True(r.GetProperty("items")[0].GetProperty("success").GetBoolean());}
 Assert.Equal(id,(await Get(accounting+"/source-journal?kind=Transaction&sourceId="+source)).GetProperty("id").GetGuid());Assert.Equal(0,(await Get(accounting+"/setup-check")).GetProperty("pendingCash").GetInt32());
 }
 [Fact]public async Task FullReceivableAndPayableLifecycleReconcilesAcrossCashAndLedger(){
 await Post(await Import("Transaction",await Cash("Opening","1000")));
 foreach(var incoming in new[]{true,false}){
 var path=root+(incoming?"/receivables":"/payables");
 var doc=await Create(path,new{number="PHASE6-"+(incoming?"AR":"AP"),counterpartyId=party,categoryId=incoming?incomeCategory:expenseCategory,currency="CNY",amount="150",issueDate="2026-10-02",dueDate="2026-10-20"});
 var recognition=await Import(incoming?"Receivable":"Payable",doc);await Post(recognition);
 async Task<Guid> Settle(string amount){var id=await Create(path+"/"+doc+"/settlements",new{requestId=Guid.NewGuid(),accountId=cash,amount,businessDate="2026-10-03"});await Post(await Import("Transaction",id));return id;}
 async Task ReverseCash(Guid id){var reverse=await Create(root+"/transactions/"+id+"/reverse",new{requestId=Guid.NewGuid(),businessDate="2026-10-04",reason="完整流程撤回结算"});await Post(await Import("Transaction",reverse));}
 var first=await Settle("40");var second=await Settle("60");await ReverseCash(first);var third=await Settle("90");
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(path+"/"+doc+"/settlements",new{requestId=Guid.NewGuid(),accountId=cash,amount="0.0001",businessDate="2026-10-03"})).StatusCode);
 await ReverseCash(second);await ReverseCash(third);
 await Create(accounting+"/journals/"+recognition+"/reverse",new{requestId=Guid.NewGuid(),businessDate="2026-10-05",reason="取消确认"});
 (await client.PostAsJsonAsync(path+"/"+doc+"/cancel",new{})).EnsureSuccessStatusCode();
 }
 var trial=await Get(accounting+"/trial-balance?dateFrom=2026-10-01&dateTo=2026-10-31&currency=CNY");Assert.Equal("0.0000",trial.GetProperty("revenue").GetString());Assert.Equal("0.0000",trial.GetProperty("expense").GetString());Assert.Equal("0.0000",trial.GetProperty("difference").GetString());
 var rec=await Get(accounting+"/reconciliation?dateTo=2026-10-31&currency=CNY");Assert.Equal("1000.0000",rec.GetProperty("items")[0].GetProperty("cash").GetString());Assert.Equal("0.0000",rec.GetProperty("items")[0].GetProperty("difference").GetString());
 (await client.PostAsJsonAsync(accounting+"/periods/"+period+"/close",new{})).EnsureSuccessStatusCode();
 }
 [Fact]public async Task DatabaseCloseCannotHideOlderCashOrReversedRecognition(){
 var oldOrg=await Create("/api/v1/organizations",new{name="会计启用前历史企业"});var oldRoot="/api/v1/organizations/"+oldOrg;
 var oldAccount=await Create(oldRoot+"/accounts",new{name="历史资金",kind="Bank",currency="CNY"});
 await Create(oldRoot+"/transactions",new{requestId=Guid.NewGuid(),accountId=oldAccount,kind="Opening",amount="10",businessDate="2026-09-01",description="启用前历史来源"});
 var oldPeriod=await Create(oldRoot+"/accounting/periods",new{code="OCT",startDate="2026-10-01",endDate="2026-10-31"});
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);
 var oldEx=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fiscal_periods SET \"Status\"='Closed' WHERE \"Id\"={oldPeriod}"));Assert.Contains("historical cash",oldEx.MessageText);
 var doc=await Create(root+"/receivables",new{number="REVERSED-ACTIVE",counterpartyId=party,categoryId=incomeCategory,currency="CNY",amount="20",issueDate="2026-10-02",dueDate="2026-10-10"});var journal=await Import("Receivable",doc);await Post(journal);
 await Create(accounting+"/journals/"+journal+"/reverse",new{requestId=Guid.NewGuid(),businessDate="2026-10-03",reason="尚未取消原单"});
 var ex=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fiscal_periods SET \"Status\"='Closed' WHERE \"Id\"={period}"));Assert.Contains("Unrecognised",ex.MessageText);
 }
 [Fact]public async Task MissingAccountStatusCannotSilentlyDisableAccount(){
 Assert.Equal(HttpStatusCode.BadRequest,(await client.PatchAsJsonAsync(accounting+"/accounts/"+bank+"/status",new{})).StatusCode);
 Assert.Equal(HttpStatusCode.BadRequest,(await client.PatchAsJsonAsync(accounting+"/accounts/"+bank+"/status",new{active=(bool?)null})).StatusCode);
 await Post(await Create(accounting+"/journals",Journal(Guid.NewGuid())));
 }
 [Fact]public async Task SplitExpenseIsAtomicIdempotentBudgetedPostedAndReversed(){
 await Post(await Import("Transaction",await Cash("Opening","1000")));
 var second=await Create(root+"/categories",new{name="分摊费用",kind="Expense"});await Mapping("Category",second,expense);
 var budget=await Create(root+"/budgets",new{requestId=Guid.NewGuid(),name="分摊预算",currency="CNY",startDate="2026-10-01",endDate="2026-10-31",items=new[]{new{categoryId=expenseCategory,amount="100"},new{categoryId=second,amount="100"}}});
 var request=Guid.NewGuid();var body=new{requestId=request,accountId=cash,categoryId=expenseCategory,kind="Expense",amount="100.0001",businessDate="2026-10-02",description="精确分摊",allocations=new[]{new{categoryId=expenseCategory,amount="60"},new{categoryId=second,amount="40.0001"}}};
 var id=await Create(root+"/transactions",body);Assert.Equal(id,await Create(root+"/transactions",body));
 var execution=await Get(root+"/budgets/"+budget+"/execution");Assert.Contains(execution.GetProperty("items").EnumerateArray(),x=>x.GetProperty("actual").GetString()=="40.0001");
 var report=await Get(root+"/reports/summary?currency=CNY&dateFrom=2026-10-01&dateTo=2026-10-31");Assert.Contains(report.GetProperty("categories").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==second&&x.GetProperty("amount").GetString()=="40.0001");
 var export=await client.GetAsync(root+"/exports/transactions.xlsx");export.EnsureSuccessStatusCode();using(var workbook=new ClosedXML.Excel.XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync())))Assert.Equal(3,workbook.Worksheet("分类分摊").RowsUsed().Count());
 var journal=await Import("Transaction",id);await Post(journal);Assert.Equal(4,(await Get(accounting+"/journals/"+journal)).GetProperty("lines").GetArrayLength());
 var detail=await Get(root+"/transactions/"+id);Assert.Equal(2,detail.GetProperty("allocations").GetArrayLength());
 var reverse=await Create(root+"/transactions/"+id+"/reverse",new{requestId=Guid.NewGuid(),businessDate="2026-10-03",reason="分摊撤销"});await Post(await Import("Transaction",reverse));
 execution=await Get(root+"/budgets/"+budget+"/execution");Assert.All(execution.GetProperty("items").EnumerateArray(),x=>Assert.Equal("0.0000",x.GetProperty("actual").GetString()));
 report=await Get(root+"/reports/summary?currency=CNY&dateFrom=2026-10-01&dateTo=2026-10-31");Assert.All(report.GetProperty("categories").EnumerateArray(),x=>Assert.Equal("0.0000",x.GetProperty("amount").GetString()));
 var recon=await Get(accounting+"/reconciliation?dateTo=2026-10-31&currency=CNY");Assert.Equal("1000.0000",recon.GetProperty("items")[0].GetProperty("cash").GetString());Assert.Equal("0.0000",recon.GetProperty("items")[0].GetProperty("difference").GetString());
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);Assert.Equal(4,await db.Set<TransactionAllocation>().CountAsync(x=>x.OrganizationId==org));
 var ex=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM transaction_allocations WHERE \"TransactionId\"={id}"));Assert.Equal("23514",ex.SqlState);
 }
 [Fact]public async Task AttachmentsAreTenantScopedAppendOnlyAndAudited(){
 var source=await Cash("Opening","10");var bytes=System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nattachment test\n%%EOF");
 MultipartFormDataContent Body(string fileName="receipt.pdf"){var body=new MultipartFormDataContent();body.Add(new StringContent("Transaction"),"resourceKind");body.Add(new StringContent(source.ToString()),"resourceId");body.Add(new ByteArrayContent(bytes),"file",fileName);return body;}
 using(var body=Body("receipt.exe"))Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(root+"/attachments",body)).StatusCode);
 Guid id;using(var body=Body()){var r=await client.PostAsync(root+"/attachments",body);r.EnsureSuccessStatusCode();id=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
 Assert.Equal(bytes,await client.GetByteArrayAsync(root+"/attachments/"+id));
 using var outsider=factory.CreateClient();await Login(outsider);Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync(root+"/attachments/"+id)).StatusCode);
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);Assert.True(await db.Set<AuditLog>().AnyAsync(x=>x.OrganizationId==org&&x.ResourceId==id&&x.Action=="AttachmentAdded"));
 var ex=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM attachments WHERE \"Id\"={id}"));Assert.Equal("23514",ex.SqlState);
 var journal=await Create(accounting+"/journals",Journal(Guid.NewGuid()));
 MultipartFormDataContent JournalBody(){var body=new MultipartFormDataContent();body.Add(new StringContent("Journal"),"resourceKind");body.Add(new StringContent(journal.ToString()),"resourceId");body.Add(new ByteArrayContent(bytes),"file","posted.pdf");return body;}
 using(var body=JournalBody())Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsync(root+"/attachments",body)).StatusCode);
 await Post(journal);using(var body=JournalBody())(await client.PostAsync(root+"/attachments",body)).EnsureSuccessStatusCode();
 }
 [Fact]public async Task SplitRejectsWrongTotalAndForeignCategoryWithoutMoneyMovement(){
 var other=await Create("/api/v1/organizations",new{name="外企业"});var foreign=await Create("/api/v1/organizations/"+other+"/categories",new{name="外费用",kind="Expense"});
 foreach(var amount in new[]{"0.9999","1"})Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=cash,categoryId=incomeCategory,kind="Income",amount="2",businessDate="2026-10-02",allocations=new[]{new{categoryId=incomeCategory,amount="1"},new{categoryId=foreign,amount}}})).StatusCode);
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);Assert.Equal(0,await db.Set<FinancialTransaction>().CountAsync(x=>x.OrganizationId==org));
 }
 [Fact]public async Task DemoIsIsolatedRepeatableAndReconciled(){
 var first=await Create("/api/v1/demo",new{});Assert.NotEqual(org,first);Assert.Equal(first,await Create("/api/v1/demo",new{}));
 var path="/api/v1/organizations/"+first;var setup=await Get(path+"/accounting/setup-check");Assert.Equal(0,setup.GetProperty("pendingCash").GetInt32());
 var settings=await Get(path+"/finance-settings");var today=settings.GetProperty("today").GetString();var recon=await Get(path+"/accounting/reconciliation?currency=CNY&dateTo="+today);Assert.All(recon.GetProperty("items").EnumerateArray(),x=>Assert.Equal("0.0000",x.GetProperty("difference").GetString()));
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);Assert.Equal(6,await db.Set<FinancialTransaction>().CountAsync(x=>x.OrganizationId==first));Assert.Equal(1,await db.Set<Attachment>().CountAsync(x=>x.OrganizationId==first));
 Assert.Equal(0,await db.Set<FinancialTransaction>().CountAsync(x=>x.OrganizationId==org));
 }
 [Fact]public async Task BalancedPostingImmutableAndIdempotent(){
 var request=Guid.NewGuid();var id=await Create(accounting+"/journals",Journal(request));Assert.Equal(id,await Create(accounting+"/journals",Journal(request)));
 Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(accounting+"/journals",Journal(Guid.NewGuid(),credit:"10"))).StatusCode);
 var posted=await Post(id);Assert.Equal("Posted",posted.GetProperty("status").GetString());Assert.Equal("10.0001",posted.GetProperty("debitTotal").GetString());Assert.Equal(posted.GetProperty("number").GetString(),(await Post(id)).GetProperty("number").GetString());
 Assert.Equal(HttpStatusCode.Conflict,(await client.DeleteAsync(accounting+"/journals/"+id)).StatusCode);
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);
 foreach(var sql in new[]{"UPDATE journal_entries SET \"Description\"='tamper' WHERE \"Id\"='{0}'","DELETE FROM journal_entries WHERE \"Id\"='{0}'","UPDATE journal_lines SET \"Debit\"=2 WHERE \"EntryId\"='{0}'","DELETE FROM journal_lines WHERE \"EntryId\"='{0}'"}){var command=sql.Replace("{0}",id.ToString());var exception=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(command));Assert.Equal("23514",exception.SqlState);}
 var trial=await Get(accounting+"/trial-balance?dateFrom=2026-10-01&dateTo=2026-10-31&currency=CNY");Assert.Equal("0.0000",trial.GetProperty("difference").GetString());Assert.Equal("10.0001",trial.GetProperty("debit").GetString());
 }
 [Fact]public async Task ConcurrentPostingReversalAndPeriodClose(){
 var id=await Create(accounting+"/journals",Journal(Guid.NewGuid()));var version=(await Get(accounting+"/journals/"+id)).GetProperty("entry").GetProperty("version").GetGuid();var race=await Task.WhenAll(client.PostAsJsonAsync(accounting+"/journals/"+id+"/post",new{version}),client.PostAsJsonAsync(accounting+"/journals/"+id+"/post",new{version}));Assert.All(race,x=>x.EnsureSuccessStatusCode());
 var request=Guid.NewGuid();var reverseBody=new{requestId=request,businessDate="2026-10-03",reason="会计纠错"};var reversals=await Task.WhenAll(client.PostAsJsonAsync(accounting+"/journals/"+id+"/reverse",reverseBody),client.PostAsJsonAsync(accounting+"/journals/"+id+"/reverse",reverseBody));Assert.All(reversals,x=>x.EnsureSuccessStatusCode());Assert.Equal((await reversals[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),(await reversals[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
 (await client.PostAsJsonAsync(accounting+"/periods/"+period+"/close",new{})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(accounting+"/journals",Journal(Guid.NewGuid()))).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=cash,kind="Opening",amount="1",businessDate="2026-10-02",description="封账拒绝"})).StatusCode);
 }
 [Fact]public async Task CashAndAccrualImportsDoNotDoubleCountRevenue(){
 var opening=await Cash("Opening","1000");await Post(await Import("Transaction",opening));
 var doc=await Create(root+"/receivables",new{number="AR-ACCOUNT",counterpartyId=party,categoryId=incomeCategory,currency="CNY",amount="100",issueDate="2026-10-02",dueDate="2026-10-10"});await Post(await Import("Receivable",doc));
 var paid=await Create(root+"/receivables/"+doc+"/settlements",new{requestId=Guid.NewGuid(),accountId=cash,amount="40",businessDate="2026-10-03"});var journal=await Import("Transaction",paid);Assert.Equal(journal,await Import("Transaction",paid));await Post(journal);
 var trial=await Get(accounting+"/trial-balance?dateFrom=2026-10-01&dateTo=2026-10-31&currency=CNY");Assert.Equal("100.0000",trial.GetProperty("revenue").GetString());Assert.Equal("60.0000",trial.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==ar).GetProperty("normalBalance").GetString());
 var rec=await Get(accounting+"/reconciliation?dateTo=2026-10-31&currency=CNY");Assert.Equal(0,rec.GetProperty("pendingTransactions").GetInt32());Assert.Equal("0.0000",rec.GetProperty("items")[0].GetProperty("difference").GetString());
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(accounting+"/journals/"+journal+"/reverse",new{requestId=Guid.NewGuid(),businessDate="2026-10-04",reason="禁止脱离资金冲销"})).StatusCode);
 var reversal=await Create(root+"/transactions/"+paid+"/reverse",new{requestId=Guid.NewGuid(),businessDate="2026-10-04",reason="退款"});await Post(await Import("Transaction",reversal));
 Assert.Equal("100.0000",(await Get(accounting+"/trial-balance?dateFrom=2026-10-01&dateTo=2026-10-31&currency=CNY")).GetProperty("revenue").GetString());
 }
 [Fact]public async Task DraftVersionValidationAndSourceCompleteness(){
 var id=await Create(accounting+"/journals",Journal(Guid.NewGuid()));var version=(await Get(accounting+"/journals/"+id)).GetProperty("entry").GetProperty("version").GetGuid();var change=new{version,businessDate="2026-10-04",description="修改草稿",lines=new[]{new{ledgerAccountId=bank,debit="11",credit="0"},new{ledgerAccountId=capital,debit="0",credit="11"}}};(await client.PutAsJsonAsync(accounting+"/journals/"+id,change)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync(accounting+"/journals/"+id,change)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(accounting+"/journals/"+id+"/post",new{version})).StatusCode);await Post(id);
 var pending=await Cash("Opening","10");Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(accounting+"/periods/"+period+"/close",new{})).StatusCode);await Post(await Import("Transaction",pending));await Create(accounting+"/journals/"+id+"/reverse",new{requestId=Guid.NewGuid(),businessDate="2026-10-05",reason="冲回手工资金调整"});(await client.PostAsJsonAsync(accounting+"/periods/"+period+"/close",new{})).EnsureSuccessStatusCode();
 }
 [Fact]public async Task InvalidAccountsPeriodsIsolationAndDatabaseBalanceGuard(){
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(accounting+"/periods",new{code="OVERLAP",startDate="2026-10-31",endDate="2026-11-30"})).StatusCode);
 var usd=await Create(accounting+"/accounts",new{code="1002",name="美元银行",kind="Asset",currency="USD"});Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(accounting+"/journals",Journal(Guid.NewGuid(),debitAccount:usd))).StatusCode);
 using var outsider=factory.CreateClient();await Login(outsider);Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync(accounting+"/accounts")).StatusCode);
 var profile=await outsider.GetFromJsonAsync<JsonElement>("/api/v1/me");(await client.PostAsJsonAsync(root+"/members",new{email=profile.GetProperty("email").GetString(),role=3})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await outsider.PostAsJsonAsync(accounting+"/journals",Journal(Guid.NewGuid()))).StatusCode);
 var foreignOrg=await Create("/api/v1/organizations",new{name="科目隔离企业"});var foreign=await Create("/api/v1/organizations/"+foreignOrg+"/accounting/accounts",new{code="1002",name="外企科目",kind="Asset",currency="CNY"});Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(accounting+"/journals",Journal(Guid.NewGuid(),debitAccount:foreign))).StatusCode);
 var parent=await Ledger("1000","父级","Asset");await Create(accounting+"/accounts",new{code="1000.1",name="子级",kind="Asset",currency="CNY",parentId=parent});Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(accounting+"/journals",Journal(Guid.NewGuid(),debitAccount:parent))).StatusCode);
 var draft=await Create(accounting+"/journals",Journal(Guid.NewGuid()));await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);var finite=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE journal_lines SET \"Debit\"='NaN'::numeric WHERE \"EntryId\"={draft} AND \"Debit\">0"));Assert.Equal("ck_journal_lines_finite",finite.ConstraintName);await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE journal_lines SET \"Credit\"=9 WHERE \"EntryId\"={draft} AND \"Credit\">0");
 var ex=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE journal_entries SET \"Status\"='Posted',\"Number\"='INVALID',\"PostedAt\"=now(),\"DebitTotal\"=10.0001,\"CreditTotal\"=10.0001 WHERE \"Id\"={draft}"));Assert.Equal("23514",ex.SqlState);
 }

 [Fact]public async Task ClosingPeriodRacesWithCashWithoutLosingAtomicity(){
 var posting=client.PostAsJsonAsync(root+"/transactions",new{requestId=Guid.NewGuid(),accountId=cash,kind="Opening",amount="10",description="并发期初",businessDate="2026-10-02"});var closing=client.PostAsJsonAsync(accounting+"/periods/"+period+"/close",new{});var race=await Task.WhenAll(posting,closing);Assert.Single(race,x=>x.IsSuccessStatusCode);Assert.Single(race,x=>x.StatusCode==HttpStatusCode.Conflict);
 }
 [Fact]public async Task RecognitionMustReverseBeforeDocumentCancellation(){
 var doc=await Create(root+"/receivables",new{number="CANCEL-GL",counterpartyId=party,categoryId=incomeCategory,currency="CNY",amount="20",issueDate="2026-10-02",dueDate="2026-10-10"});var journal=await Import("Receivable",doc);await Post(journal);Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(root+"/receivables/"+doc+"/cancel",new{})).StatusCode);
 await Create(accounting+"/journals/"+journal+"/reverse",new{requestId=Guid.NewGuid(),businessDate="2026-10-03",reason="取消错误单据"});(await client.PostAsJsonAsync(root+"/receivables/"+doc+"/cancel",new{})).EnsureSuccessStatusCode();
 }
 [Fact]public async Task AuditFailureRollsBackWholeDraft(){
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_accounting_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test failure'; END $$; CREATE TRIGGER reject_accounting_audit BEFORE INSERT ON audit_logs FOR EACH ROW EXECUTE FUNCTION reject_accounting_audit();");
 Assert.Equal(HttpStatusCode.InternalServerError,(await client.PostAsJsonAsync(accounting+"/journals",Journal(Guid.NewGuid()))).StatusCode);Assert.Equal(0,await db.Set<JournalEntry>().CountAsync(x=>x.OrganizationId==org));Assert.Equal(0,await db.Set<JournalLine>().CountAsync(x=>x.OrganizationId==org));
 }
 [Fact]public async Task RestrictedRuntimeCanPostAndReverseWithoutTampering(){
 var role="cfis_test_role_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(database.ConnectionString);await admin.OpenAsync();
 await using(var command=new NpgsqlCommand($"CREATE ROLE {role} LOGIN PASSWORD 'Accounting-Role!123456'; GRANT USAGE ON SCHEMA public TO {role}; GRANT SELECT,INSERT ON ALL TABLES IN SCHEMA public TO {role}; GRANT DELETE ON journal_entries,journal_lines TO {role}; GRANT UPDATE (\"Version\",\"PeriodId\",\"BusinessDate\",\"Description\",\"Status\",\"Number\",\"PostedAt\",\"DebitTotal\",\"CreditTotal\") ON journal_entries TO {role}; GRANT UPDATE (\"NextNumber\",\"Status\",\"ClosedAt\") ON fiscal_periods TO {role};",admin))await command.ExecuteNonQueryAsync();
 try{
 var builder=new NpgsqlConnectionStringBuilder(database.ConnectionString){Username=role,Password="Accounting-Role!123456"};await using var runtime=factory.WithWebHostBuilder(w=>w.UseSetting("ConnectionStrings:Database",builder.ConnectionString));using var c=runtime.CreateClient();c.DefaultRequestHeaders.Authorization=client.DefaultRequestHeaders.Authorization;
 var response=await c.PostAsJsonAsync(accounting+"/journals",Journal(Guid.NewGuid()));response.EnsureSuccessStatusCode();var e=await response.Content.ReadFromJsonAsync<JsonElement>();var id=e.GetProperty("id").GetGuid();(await c.PostAsJsonAsync(accounting+"/journals/"+id+"/post",new{version=e.GetProperty("version").GetGuid()})).EnsureSuccessStatusCode();
 (await c.PostAsJsonAsync(accounting+"/journals/"+id+"/reverse",new{requestId=Guid.NewGuid(),businessDate="2026-10-03",reason="受限角色冲销"})).EnsureSuccessStatusCode();
 await using var connection=new NpgsqlConnection(builder.ConnectionString);await connection.OpenAsync();await using var tamper=new NpgsqlCommand($"DELETE FROM journal_entries WHERE \"Id\"='{id}'",connection);var ex=await Assert.ThrowsAsync<PostgresException>(async()=>await tamper.ExecuteNonQueryAsync());Assert.Equal("23514",ex.SqlState);
 }finally{NpgsqlConnection.ClearAllPools();await using var cleanup=new NpgsqlCommand($"DROP OWNED BY {role}; DROP ROLE {role};",admin);await cleanup.ExecuteNonQueryAsync();}
 }
}

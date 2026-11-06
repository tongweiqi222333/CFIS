using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using CFIS.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
public class FinanceTests {
 [Fact]
 public async Task MoneyTransfersConcurrencyIsolationAndAudit(){
 await using var database=new TestDatabase();await database.InitializeAsync();
 await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseSetting("ConnectionStrings:Database",database.ConnectionString));
 using var a=factory.CreateClient();using var outsider=factory.CreateClient();
 async Task<string> Login(HttpClient c){var email=Guid.NewGuid()+"@test.local";var credentials=new{email,password="Finance-Test!123456"};(await c.PostAsJsonAsync("/api/v1/auth/register",credentials)).EnsureSuccessStatusCode();var token=await (await c.PostAsJsonAsync("/api/v1/auth/login",credentials)).Content.ReadFromJsonAsync<JsonElement>();c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.GetProperty("accessToken").GetString());return email;}
 await Login(a);var viewerEmail=await Login(outsider);
 async Task<Guid> Create(string path,object data){var r=await a.PostAsJsonAsync(path,data);r.EnsureSuccessStatusCode();return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
 var org=await Create("/api/v1/organizations",new{name="财务测试"});
 var other=await Create("/api/v1/organizations",new{name="其他企业"});
 string Base(Guid id)=>"/api/v1/organizations/"+id;
 var root=Base(org);var source=await Create(root+"/accounts",new{name="银行",currency="CNY",kind="Bank"});
 var destination=await Create(root+"/accounts",new{name="现金",currency="CNY",kind="Cash"});
 var usd=await Create(root+"/accounts",new{name="美元",currency="USD",kind="Bank"});
 var foreign=await Create(Base(other)+"/accounts",new{name="外部",currency="CNY",kind="Bank"});
 var income=await Create(root+"/categories",new{name="营业收入",kind="Income"});
 var expense=await Create(root+"/categories",new{name="办公支出",kind="Expense"});
 object Body(Guid request,string kind,string amount,Guid account,Guid? target=null,Guid? category=null)=>new{requestId=request,kind,amount,accountId=account,destinationAccountId=target,categoryId=category,description="测试"};
 async Task<HttpStatusCode> Post(object body,bool transfer=false)=> (await a.PostAsJsonAsync(root+(transfer?"/transfers":"/transactions"),body)).StatusCode;
 Assert.Equal(HttpStatusCode.Created,await Post(Body(Guid.NewGuid(),"Income","100.0001",source,category:income)));
 var request=Guid.NewGuid();var transferBody=Body(request,"Transfer","25.0001",source,destination);
 var races=await Task.WhenAll(a.PostAsJsonAsync(root+"/transfers",transferBody),a.PostAsJsonAsync(root+"/transfers",transferBody));
 Assert.Single(races,x=>x.StatusCode==HttpStatusCode.Created);Assert.Single(races,x=>x.StatusCode==HttpStatusCode.OK);
 Assert.Equal(HttpStatusCode.Conflict,await Post(Body(request,"Transfer","26",source,destination),true));
 Assert.Equal(HttpStatusCode.Created,await Post(Body(Guid.NewGuid(),"Expense","0.0001",source,category:expense)));
 Assert.Equal(HttpStatusCode.BadRequest,await Post(Body(Guid.NewGuid(),"Income","0.00001",source,category:income)));
 Assert.Equal(HttpStatusCode.BadRequest,await Post(Body(Guid.NewGuid(),"Income","-1",source,category:income)));
 Assert.Equal(HttpStatusCode.BadRequest,await Post(Body(Guid.NewGuid(),"Expense","1",source,category:income)));
 Assert.Equal(HttpStatusCode.NotFound,await Post(Body(Guid.NewGuid(),"Transfer","1",source,foreign),true));
 Assert.Equal(HttpStatusCode.BadRequest,await Post(Body(Guid.NewGuid(),"Transfer","1",source,usd),true));
 Assert.Equal(HttpStatusCode.BadRequest,await Post(Body(Guid.NewGuid(),"Transfer","1",source,source),true));
 var withdrawals=await Task.WhenAll(a.PostAsJsonAsync(root+"/transactions",Body(Guid.NewGuid(),"Expense","50",source,category:expense)),a.PostAsJsonAsync(root+"/transactions",Body(Guid.NewGuid(),"Expense","50",source,category:expense)));
 Assert.Single(withdrawals,x=>x.StatusCode==HttpStatusCode.Created);Assert.Single(withdrawals,x=>x.StatusCode==HttpStatusCode.Conflict);
 Assert.Equal(HttpStatusCode.NotFound,(await outsider.GetAsync(root+"/accounts")).StatusCode);
 (await a.PostAsJsonAsync(root+"/members",new{email=viewerEmail,role=3})).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.Forbidden,(await outsider.PostAsJsonAsync(root+"/accounts",new{name="禁止",currency="CNY",kind="Cash"})).StatusCode);
 Assert.Equal(HttpStatusCode.Forbidden,(await outsider.PostAsJsonAsync(root+"/transactions",Body(Guid.NewGuid(),"Income","1",source,category:income))).StatusCode);
 Assert.Equal(HttpStatusCode.Forbidden,(await outsider.GetAsync(root+"/audit-logs")).StatusCode);
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);
 Assert.Equal(24.9999m,(await db.Set<FinancialAccount>().FindAsync(source))!.Balance);
 Assert.Equal(25.0001m,(await db.Set<FinancialAccount>().FindAsync(destination))!.Balance);
 Assert.Equal(4,await db.Set<FinancialTransaction>().CountAsync());
 Assert.Equal(4,await db.Set<AuditLog>().CountAsync(x=>x.Action=="TransactionPosted"));
 var ledger=await db.Set<FinancialTransaction>().ToListAsync();
 foreach(var account in await db.Set<FinancialAccount>().ToListAsync()){
 var expected=ledger.Where(x=>x.AccountId==account.Id).Sum(x=>x.Kind=="Income"?x.Amount:-x.Amount)+ledger.Where(x=>x.DestinationAccountId==account.Id).Sum(x=>x.Amount);
 Assert.Equal(expected,account.Balance);
 }
 var listed=await a.GetFromJsonAsync<JsonElement>(root+"/transactions?pageSize=2&kind=Transfer");
 Assert.Equal(1,listed.GetProperty("total").GetInt32());
 Assert.Equal("25.0001",listed.GetProperty("items")[0].GetProperty("amount").GetString());
 // Force the final audit insert to fail; neither transfer leg nor its transaction may survive.
 await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test failure'; END $$; CREATE TRIGGER reject_audit BEFORE INSERT ON audit_logs FOR EACH ROW EXECUTE FUNCTION reject_audit();");
 Assert.Equal(HttpStatusCode.InternalServerError,await Post(Body(Guid.NewGuid(),"Transfer","1",source,destination),true));
 Assert.Equal(24.9999m,await db.Set<FinancialAccount>().AsNoTracking().Where(x=>x.Id==source).Select(x=>x.Balance).SingleAsync());
 Assert.Equal(25.0001m,await db.Set<FinancialAccount>().AsNoTracking().Where(x=>x.Id==destination).Select(x=>x.Balance).SingleAsync());
 Assert.Equal(4,await db.Set<FinancialTransaction>().CountAsync());
 }
}


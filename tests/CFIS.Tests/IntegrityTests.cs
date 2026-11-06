using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using CFIS.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
public sealed class IntegrityTests:IAsyncLifetime {
 TestDatabase database=null!;WebApplicationFactory<Program> factory=null!;HttpClient client=null!;
 Guid org,a,b,income,expense;string root="";
 public async Task InitializeAsync(){
 database=new();await database.InitializeAsync();
 factory=new WebApplicationFactory<Program>().WithWebHostBuilder(w=>w.UseSetting("ConnectionStrings:Database",database.ConnectionString));
 client=factory.CreateClient();await Register(client);
 org=await Create("/api/v1/organizations",new{name="加固测试",timeZone="Asia/Shanghai"});root="/api/v1/organizations/"+org;
 a=await Create(root+"/accounts",new{name="甲",currency="CNY",kind="Bank"});
 b=await Create(root+"/accounts",new{name="乙",currency="CNY",kind="Cash"});
 income=await Create(root+"/categories",new{name="收入",kind="Income"});
 expense=await Create(root+"/categories",new{name="支出",kind="Expense"});
 }
 async Task<string> Register(HttpClient c){
 var email=Guid.NewGuid()+"@test.local";var body=new{email,password="Integrity-Test!123456"};
 (await c.PostAsJsonAsync("/api/v1/auth/register",body)).EnsureSuccessStatusCode();
 var token=await (await c.PostAsJsonAsync("/api/v1/auth/login",body)).Content.ReadFromJsonAsync<JsonElement>();
 c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.GetProperty("accessToken").GetString());return email;
 }
 async Task<Guid> Create(string path,object body){var r=await client.PostAsJsonAsync(path,body);r.EnsureSuccessStatusCode();return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
 object Body(string kind,string amount,Guid? account=null,Guid? target=null,Guid? request=null,string date="2026-10-01")=>new{requestId=request??Guid.NewGuid(),kind,amount,accountId=account??a,destinationAccountId=target,categoryId=kind=="Income"?(Guid?)income:kind=="Expense"?expense:null,description="来源：测试",businessDate=date};
 Task<HttpResponseMessage> Post(string kind,string amount,Guid? account=null,Guid? target=null,Guid? request=null,string date="2026-10-01")=>client.PostAsJsonAsync(root+(kind=="Transfer"?"/transfers":"/transactions"),Body(kind,amount,account,target,request,date));
 async Task<JsonElement> Json(string path)=>await client.GetFromJsonAsync<JsonElement>(root+path);
 async Task AssertBalanced(){var rows=await Json("/reconciliation");Assert.All(rows.EnumerateArray(),x=>Assert.Equal("0.0000",x.GetProperty("difference").GetString()));}
 public async Task DisposeAsync(){client.Dispose();await factory.DisposeAsync();await database.DisposeAsync();}
 [Fact] public async Task OpeningLifecycleDatesAndMismatchDetection(){
 var request=Guid.NewGuid();Assert.Equal(HttpStatusCode.Created,(await Post("Opening","100",request:request)).StatusCode);
 Assert.Equal(HttpStatusCode.OK,(await Post("Opening","100",request:request)).StatusCode);
 Assert.Equal(HttpStatusCode.Conflict,(await Post("Opening","1")).StatusCode);
 (await client.PatchAsJsonAsync(root+"/accounts/"+a,new{name="甲停用",status="Inactive"})).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.Conflict,(await Post("Income","1")).StatusCode);
 Assert.Equal(HttpStatusCode.Conflict,(await client.PatchAsJsonAsync(root+"/accounts/"+a,new{name="甲",status="Archived"})).StatusCode);
 (await client.PatchAsJsonAsync(root+"/accounts/"+a,new{name="甲恢复",status="Active"})).EnsureSuccessStatusCode();
 (await client.PatchAsJsonAsync(root+"/categories/"+income,new{name="收入停用",status="Inactive"})).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.BadRequest,(await Post("Income","1")).StatusCode);
 (await Post("Expense","1",date:"2026-10-02")).EnsureSuccessStatusCode();
 var list=await Json("/transactions?dateFrom=2026-10-01&dateTo=2026-10-01");Assert.Equal(1,list.GetProperty("total").GetInt32());
 Assert.Equal("Opening",list.GetProperty("items")[0].GetProperty("kind").GetString());
 await AssertBalanced();
 await using var db=new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(database.ConnectionString).Options);
 await db.Set<FinancialAccount>().Where(x=>x.Id==a).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Balance,x=>x.Balance+1));
 var mismatch=await Json("/reconciliation");Assert.Contains(mismatch.EnumerateArray(),x=>x.GetProperty("difference").GetString()=="1.0000");
 Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(root+"/accounts?pageSize=1&page=2")).StatusCode);
 }
 [Fact] public async Task ReversalConcurrencyCorrectionAndFundsCheck(){
 (await Post("Opening","100")).EnsureSuccessStatusCode();
 var original=await (await Post("Expense","30")).Content.ReadFromJsonAsync<JsonElement>();var id=original.GetProperty("id").GetGuid();
 var request=new{requestId=Guid.NewGuid(),reason="金额录错",businessDate="2026-10-02"};
 var results=await Task.WhenAll(client.PostAsJsonAsync(root+"/transactions/"+id+"/reverse",request),client.PostAsJsonAsync(root+"/transactions/"+id+"/reverse",request));
 Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Created);Assert.Single(results,x=>x.StatusCode==HttpStatusCode.OK);
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(root+"/transactions/"+id+"/reverse",new{requestId=Guid.NewGuid(),reason="再次撤销",businessDate="2026-10-02"})).StatusCode);
 var detail=await Json("/transactions/"+id);Assert.NotEqual(JsonValueKind.Null,detail.GetProperty("reversedBy").ValueKind);
 Assert.Equal("甲",detail.GetProperty("accountName").GetString());
 (await Post("Expense","25",date:"2026-10-02")).EnsureSuccessStatusCode();
 var opening=(await Json("/transactions?kind=Opening")).GetProperty("items")[0].GetProperty("id").GetGuid();
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(root+"/transactions/"+opening+"/reverse",new{requestId=Guid.NewGuid(),reason="余额不足",businessDate="2026-10-03"})).StatusCode);
 var reversalId=detail.GetProperty("reversedBy").GetGuid();
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(root+"/transactions/"+reversalId+"/reverse",new{requestId=Guid.NewGuid(),reason="撤销撤销",businessDate="2026-10-03"})).StatusCode);
 await AssertBalanced();
 }
 [Fact] public async Task OpposingTransfersAndRequestCollision(){
 (await Post("Opening","100")).EnsureSuccessStatusCode();(await Post("Opening","100",b)).EnsureSuccessStatusCode();
 var results=await Task.WhenAll(Enumerable.Range(0,12).Select(i=>i%2==0?Post("Transfer","1",a,b):Post("Transfer","1",b,a)));
 Assert.All(results,x=>Assert.Equal(HttpStatusCode.Created,x.StatusCode));
 var key=Guid.NewGuid();var collisions=await Task.WhenAll(Post("Income","1",a,request:key),Post("Income","1",b,request:key));
 Assert.Single(collisions,x=>x.StatusCode==HttpStatusCode.Created);Assert.Single(collisions,x=>x.StatusCode==HttpStatusCode.Conflict);
 // Discard a committed response as if the network had lost it, then retry the same request.
 var retry=Guid.NewGuid();(await Post("Income","0.0001",request:retry)).Dispose();
 Assert.Equal(HttpStatusCode.OK,(await Post("Income","0.0001",request:retry)).StatusCode);
 await AssertBalanced();
 }
 [Fact] public async Task ReversalLimitsAndDistinctConcurrentRequests(){
 (await Post("Opening","100")).EnsureSuccessStatusCode();
 var transfer=await (await Post("Transfer","30",a,b)).Content.ReadFromJsonAsync<JsonElement>();
 var id=transfer.GetProperty("id").GetGuid();
 var race=await Task.WhenAll(client.PostAsJsonAsync(root+"/transactions/"+id+"/reverse",new{requestId=Guid.NewGuid(),reason="甲",businessDate="2026-10-02"}),client.PostAsJsonAsync(root+"/transactions/"+id+"/reverse",new{requestId=Guid.NewGuid(),reason="乙",businessDate="2026-10-02"}));
 Assert.Single(race,x=>x.StatusCode==HttpStatusCode.Created);Assert.Single(race,x=>x.StatusCode==HttpStatusCode.Conflict);
 var empty=await Create(root+"/accounts",new{name="上限",currency="CNY",kind="Bank"});
 (await Post("Opening","999999999999999.9999",empty)).EnsureSuccessStatusCode();
 var spent=await (await Post("Expense","1",empty)).Content.ReadFromJsonAsync<JsonElement>();
 (await Post("Income","1",empty)).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(root+"/transactions/"+spent.GetProperty("id").GetGuid()+"/reverse",new{requestId=Guid.NewGuid(),reason="上限",businessDate="2026-10-02"})).StatusCode);
 await AssertBalanced();
 }
 [Fact] public async Task OpeningCorrectionAndTimeZone(){
 var posted=await(await Post("Opening","10")).Content.ReadFromJsonAsync<JsonElement>();var id=posted.GetProperty("id").GetGuid();
 (await client.PostAsJsonAsync(root+"/transactions/"+id+"/reverse",new{requestId=Guid.NewGuid(),reason="期初更正",businessDate="2026-10-02"})).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.Created,(await Post("Opening","20",date:"2026-10-02")).StatusCode);
 await AssertBalanced();
 var zoneOrg=await Create("/api/v1/organizations",new{name="时区测试",timeZone="Pacific/Kiritimati"});
 var settings=await client.GetFromJsonAsync<JsonElement>("/api/v1/organizations/"+zoneOrg+"/finance-settings");
 Assert.Equal("Pacific/Kiritimati",settings.GetProperty("timeZone").GetString());
 var local=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati")).DateTime);
 Assert.Equal(local.ToString("yyyy-MM-dd"),settings.GetProperty("today").GetString());
 Assert.False(FinancePermissions.Allows((MemberRole)999,FinancePermission.Read));
 }
 [Theory]
 [InlineData("0")][InlineData("-1")][InlineData("0.00001")][InlineData("1000000000000000")][InlineData("1e2")][InlineData("NaN")]
 public async Task InvalidMoneyNeverPosts(string amount){Assert.Equal(HttpStatusCode.BadRequest,(await Post("Opening",amount)).StatusCode);Assert.Equal(0,(await Json("/transactions")).GetProperty("total").GetInt32());}
 [Fact] public async Task BalanceMaximumAndExplicitPermissions(){
 (await Post("Opening","999999999999999.9999")).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.Conflict,(await Post("Income","0.0001")).StatusCode);
 foreach(var role in new[]{1,2}){
 using var c=factory.CreateClient();var email=await Register(c);(await client.PostAsJsonAsync(root+"/members",new{email,role})).EnsureSuccessStatusCode();
 var result=await c.PostAsJsonAsync(root+"/accounts",new{name="角色测试",currency="CNY",kind="Cash"});
 Assert.Equal(role==1?HttpStatusCode.Forbidden:HttpStatusCode.Created,result.StatusCode);
 }
 await AssertBalanced();
 }
}



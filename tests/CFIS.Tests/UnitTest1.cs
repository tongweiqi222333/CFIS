using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using CFIS.Api;
using Microsoft.AspNetCore.Mvc.Testing;
public class IsolationTests {
 [Fact]
 public async Task ExpiryRefreshLockoutAndRateLimit(){
 await using var database=new TestDatabase();await database.InitializeAsync();
 await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>{
 b.UseSetting("ConnectionStrings:Database",database.ConnectionString);
 b.ConfigureServices(s=>s.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme,o=>o.BearerTokenExpiration=TimeSpan.FromSeconds(1)));
 });
 using var c=factory.CreateClient();
 var email=Guid.NewGuid()+"@example.test";var credentials=new{email,password="Lifecycle-Test!12345"};
 (await c.PostAsJsonAsync("/api/v1/auth/register",credentials)).EnsureSuccessStatusCode();
 var response=await c.PostAsJsonAsync("/api/v1/auth/login",credentials);
 var token=await response.Content.ReadFromJsonAsync<JsonElement>();
 c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.GetProperty("accessToken").GetString());
 await Task.Delay(1500);
 Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/v1/me")).StatusCode);
 c.DefaultRequestHeaders.Authorization=null;
 var refreshed=await c.PostAsJsonAsync("/api/v1/auth/refresh",new{refreshToken=token.GetProperty("refreshToken").GetString()});
 Assert.Equal(HttpStatusCode.OK,refreshed.StatusCode);
 for(int i=0;i<5;i++)Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/v1/auth/login",new{email,password="Wrong-Password!123"})).StatusCode);
 Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/v1/auth/login",credentials)).StatusCode);
 HttpResponseMessage? last=null;
 for(int i=0;i<31;i++)last=await c.PostAsJsonAsync("/api/v1/auth/login",credentials);
 Assert.Equal(HttpStatusCode.TooManyRequests,last!.StatusCode);
 }
 [Theory]
 [InlineData(MemberRole.Owner,MemberRole.Admin,true)]
 [InlineData(MemberRole.Admin,MemberRole.Owner,false)]
 [InlineData(MemberRole.Viewer,MemberRole.Viewer,false)]
 public void RoleMatrix(MemberRole actor,MemberRole target,bool expected)=>Assert.Equal(expected,Organizations.CanManage(actor,target));
 [Fact]
 public async Task RealPostgresAuthenticationAndIsolation() {
 await using var database=new TestDatabase();
await database.InitializeAsync();
await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseSetting("ConnectionStrings:Database",database.ConnectionString));
 using var a=factory.CreateClient();using var b=factory.CreateClient();using var anonymous=factory.CreateClient();
 Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/organizations")).StatusCode);
 async Task<string> Register(HttpClient c) {
 var email=Guid.NewGuid()+"@example.test";var credentials=new{email,password="Cfis-Test!123456"};
 Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync("/api/v1/auth/register",credentials)).StatusCode);
 var login=await c.PostAsJsonAsync("/api/v1/auth/login",credentials);login.EnsureSuccessStatusCode();
 var json=await login.Content.ReadFromJsonAsync<JsonElement>();c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",json.GetProperty("accessToken").GetString());return email;
 }
 await Register(a);var emailB=await Register(b);using var c=factory.CreateClient();var emailC=await Register(c);
 var created=await a.PostAsJsonAsync("/api/v1/organizations",new{name="隔离测试企业"});Assert.Equal(HttpStatusCode.Created,created.StatusCode);
 var org=await created.Content.ReadFromJsonAsync<JsonElement>();var id=org.GetProperty("id").GetGuid();
 Assert.Equal(HttpStatusCode.NotFound,(await b.GetAsync($"/api/v1/organizations/{id}")).StatusCode);
 Assert.Equal(HttpStatusCode.NotFound,(await b.PostAsJsonAsync($"/api/v1/organizations/{id}/members",new{email=emailB,role=1})).StatusCode);
 Assert.Equal(HttpStatusCode.NoContent,(await a.PostAsJsonAsync($"/api/v1/organizations/{id}/members",new{email=emailB,role=3})).StatusCode);
  var races=await Task.WhenAll(a.PostAsJsonAsync($"/api/v1/organizations/{id}/members",new{email=emailC,role=3}),a.PostAsJsonAsync($"/api/v1/organizations/{id}/members",new{email=emailC,role=3}));
 Assert.Single(races,x=>x.StatusCode==HttpStatusCode.NoContent);Assert.Single(races,x=>x.StatusCode==HttpStatusCode.Conflict);
 Assert.Equal(HttpStatusCode.OK,(await b.GetAsync($"/api/v1/organizations/{id}")).StatusCode);
 Assert.Equal(HttpStatusCode.Forbidden,(await b.PostAsJsonAsync($"/api/v1/organizations/{id}/members",new{email=emailB,role=1})).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await anonymous.GetAsync("/health/ready")).StatusCode);
 Assert.Equal(HttpStatusCode.BadRequest,(await a.GetAsync("/api/v1/organizations?page=2147483647")).StatusCode);
 Assert.Equal(HttpStatusCode.BadRequest,(await a.PostAsJsonAsync($"/api/v1/organizations/{id}/members",new{email=emailB,role=999})).StatusCode);
 Assert.Equal(HttpStatusCode.Conflict,(await a.PostAsJsonAsync($"/api/v1/organizations/{id}/members",new{email=emailB,role=3})).StatusCode);
 Assert.Equal(HttpStatusCode.NoContent,(await b.PostAsync("/api/v1/session/logout",null)).StatusCode);
 Assert.Equal(HttpStatusCode.Unauthorized,(await b.GetAsync("/api/v1/organizations")).StatusCode);
 }
}





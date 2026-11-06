using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CFIS.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
public class Phase6SecurityTests {
 [Fact]public async Task SmtpSenderDeliversToConfiguredServer(){
 using var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
 using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));var port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
 var received=new List<string>();
 var server=Task.Run(async()=>{using var socket=await listener.AcceptTcpClientAsync(timeout.Token);await using var stream=socket.GetStream();using var reader=new StreamReader(stream);await using var writer=new StreamWriter(stream){AutoFlush=true,NewLine="\r\n"};await writer.WriteLineAsync("220 localhost SMTP");var data=false;
 while(true){var line=await reader.ReadLineAsync(timeout.Token);if(line==null)break;received.Add(line);if(data){if(line=="."){data=false;await writer.WriteLineAsync("250 queued");}continue;}if(line.StartsWith("DATA")){data=true;await writer.WriteLineAsync("354 End with dot");}else if(line.StartsWith("QUIT")){await writer.WriteLineAsync("221 Bye");break;}else await writer.WriteLineAsync("250 OK");}});
 var config=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Smtp:Host","127.0.0.1"},{"Smtp:Port",port.ToString()},{"Smtp:EnableSsl","false"},{"Smtp:From","cfis@example.test"}}).Build();
 await new IdentityEmail(config).SendPasswordResetCodeAsync(new IdentityUser(),"recipient@example.test","SMTP-DELIVERY-TEST");await server;
 Assert.Contains(received,x=>x.StartsWith("RCPT",StringComparison.OrdinalIgnoreCase)&&x.Contains("recipient@example.test"));Assert.Contains(received,x=>x==".");
 }
 sealed class MailCapture:IEmailSender<IdentityUser>{
  public string Code="";
  public Task SendConfirmationLinkAsync(IdentityUser u,string e,string l)=>Task.CompletedTask;
  public Task SendPasswordResetLinkAsync(IdentityUser u,string e,string l)=>Task.CompletedTask;
  public Task SendPasswordResetCodeAsync(IdentityUser u,string e,string code){Code=code;return Task.CompletedTask;}
 }
 static async Task Login(HttpClient c,string email,string password="Phase6-User!123456"){
  var r=await c.PostAsJsonAsync("/api/v1/auth/login",new{email,password});r.EnsureSuccessStatusCode();var j=await r.Content.ReadFromJsonAsync<JsonElement>();c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",j.GetProperty("accessToken").GetString());
 }
 [Fact]public async Task InvitationsRolesRemovalAndPasswordResetRevokeAccess(){
 await using var db=new TestDatabase();await db.InitializeAsync();var mail=new MailCapture();
 await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>{b.UseSetting("ConnectionStrings:Database",db.ConnectionString);b.UseSetting("Smtp:Host","capture.test");b.ConfigureServices(s=>s.AddSingleton<IEmailSender<IdentityUser>>(mail));});
 using var owner=factory.CreateClient();using var member=factory.CreateClient();using var anonymous=factory.CreateClient();
 var ownerEmail=Guid.NewGuid()+"@phase6.test";var memberEmail=Guid.NewGuid()+"@phase6.test";
 (await owner.PostAsJsonAsync("/api/v1/auth/register",new{email=ownerEmail,password="Phase6-User!123456"})).EnsureSuccessStatusCode();await Login(owner,ownerEmail);
 var org=(await(await owner.PostAsJsonAsync("/api/v1/organizations",new{name="Phase6安全验收"})).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var root="/api/v1/organizations/"+org;
 var invitation=await(await owner.PostAsJsonAsync(root+"/invitations",new{email=memberEmail,role=2})).Content.ReadFromJsonAsync<JsonElement>();var token=invitation.GetProperty("token").GetString();
 (await anonymous.PostAsJsonAsync("/api/v1/invitations/accept",new{token,password="Phase6-User!123456"})).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.Conflict,(await anonymous.PostAsJsonAsync("/api/v1/invitations/accept",new{token,password="Phase6-User!123456"})).StatusCode);await Login(member,memberEmail);
 var id=(await member.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetString();
 (await member.PostAsJsonAsync(root+"/accounts",new{name="会计可写",kind="Bank",currency="CNY"})).EnsureSuccessStatusCode();
 (await owner.PatchAsJsonAsync(root+"/members/"+id+"/role",new{role=3})).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.Forbidden,(await member.PostAsJsonAsync(root+"/accounts",new{name="降权立即拒绝",kind="Bank",currency="CNY"})).StatusCode);
 (await owner.DeleteAsync(root+"/members/"+id)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await member.GetAsync(root+"/accounts")).StatusCode);
 (await anonymous.PostAsJsonAsync("/api/v1/auth/forgotPassword",new{email=memberEmail})).EnsureSuccessStatusCode();Assert.NotEmpty(mail.Code);
 (await anonymous.PostAsJsonAsync("/api/v1/auth/resetPassword",new{email=memberEmail,resetCode=mail.Code,newPassword="Phase6-New!123456"})).EnsureSuccessStatusCode();
 Assert.Equal(HttpStatusCode.Unauthorized,(await member.GetAsync("/api/v1/me")).StatusCode);await Login(member,memberEmail,"Phase6-New!123456");
 Assert.Equal(HttpStatusCode.BadRequest,(await anonymous.PostAsJsonAsync("/api/v1/auth/resetPassword",new{email=memberEmail,resetCode=mail.Code,newPassword="Phase6-Reuse!123456"})).StatusCode);
 Assert.Equal("no-store",(await member.GetAsync("/api/v1/me")).Headers.CacheControl?.ToString());
 await using var closed=factory.WithWebHostBuilder(b=>b.UseSetting("Auth:AllowRegistration","false"));using var c=closed.CreateClient();Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync("/api/v1/auth/register",new{email=Guid.NewGuid()+"@test.invalid",password="Phase6-User!123456"})).StatusCode);
 }
}

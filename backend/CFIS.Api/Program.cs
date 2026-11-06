using CFIS.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Security.Claims;
using System.Threading.RateLimiting;
var b = WebApplication.CreateBuilder(args);
b.Host.UseSerilog((c, l) => l.ReadFrom.Configuration(c.Configuration).WriteTo.Console());
b.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
{
    c.ProblemDetails.Extensions["traceId"] = c.HttpContext.TraceIdentifier;
    c.ProblemDetails.Extensions.TryAdd("code", "HTTP_" + (c.ProblemDetails.Status ?? 500));
});
b.Services.AddDataProtection().SetApplicationName("CFIS");
if (b.Configuration["DataProtection:Path"] is string keyPath)
    b.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath)).SetApplicationName("CFIS");
b.Services.AddOpenApi();
b.Services.AddScoped<FinancialReportService>();
b.Services.AddDbContext<AppDb>(o => o.UseNpgsql(b.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("ConnectionStrings__Database required")));
b.Services.AddIdentityApiEndpoints<IdentityUser>(o => { o.Password.RequiredLength = 12; o.User.RequireUniqueEmail = true; o.Lockout.MaxFailedAccessAttempts = 5; }).AddEntityFrameworkStores<AppDb>();
b.Services.AddAuthorization();
b.Services.AddTransient<IEmailSender<IdentityUser>, IdentityEmail>();
b.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) })); });
if (b.Configuration["DataProtection:CertificatePath"] is string certificatePath)
{
    var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(certificatePath, b.Configuration["DataProtection:CertificatePassword"]);
    b.Services.AddDataProtection().ProtectKeysWithCertificate(certificate);
}
if (b.Environment.IsProduction() && (string.IsNullOrWhiteSpace(b.Configuration["DataProtection:Path"]) || string.IsNullOrWhiteSpace(b.Configuration["DataProtection:CertificatePath"])))
    throw new InvalidOperationException("Production requires persistent DataProtection:Path and CertificatePath");
var app = b.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseMiddleware<SessionMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    if (context.Request.Path.Equals("/api/v1/auth/register") && !b.Configuration.GetValue("Auth:AllowRegistration", b.Environment.IsDevelopment()))
    {
        context.Response.StatusCode = 403;
        return;
    }
    if (context.Request.Path.Equals("/api/v1/auth/forgotPassword") && string.IsNullOrWhiteSpace(b.Configuration["Smtp:Host"]))
    {
        context.Response.StatusCode = 503;
        return;
    }
    await next();
});
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGroup("/api/v1/auth").MapIdentityApi<IdentityUser>().RequireRateLimiting("auth");
app.MapGet("/api/v1/auth/options", () => Results.Ok(new { allowRegistration = b.Configuration.GetValue("Auth:AllowRegistration", b.Environment.IsDevelopment()), passwordRecoveryAvailable = !string.IsNullOrWhiteSpace(b.Configuration["Smtp:Host"]) }));
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (AppDb db) => { try { return await db.Database.CanConnectAsync() && !(await db.Database.GetPendingMigrationsAsync()).Any() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503); } catch { return Results.StatusCode(503); } });
app.MapGet("/api/v1/me", (ClaimsPrincipal u) => Results.Ok(new { id = u.FindFirstValue(ClaimTypes.NameIdentifier), email = u.FindFirstValue(ClaimTypes.Email) })).RequireAuthorization();
app.MapPost("/api/v1/session/logout", async (System.Security.Claims.ClaimsPrincipal principal, UserManager<IdentityUser> users) =>
{
    var user = await users.GetUserAsync(principal);
    if (user is null)
        return Results.Unauthorized();
    await users.UpdateSecurityStampAsync(user);
    return Results.NoContent();
}).RequireAuthorization();
app.MapOrganizations();
app.MapMembership();
app.MapFinance();
app.MapAccounting();
app.MapAccountingWorkspace();
app.MapAttachments();
app.MapDemo();
if (args.Contains("--migrate")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync(); return; }
app.Run();
public partial class Program
{
}

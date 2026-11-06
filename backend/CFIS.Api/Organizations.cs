using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public record OrganizationInput(string Name, string TimeZone = "Asia/Shanghai");
public record MemberInput(string Email, MemberRole Role);
public static class Organizations
{
    static string Uid(ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public static bool CanManage(MemberRole actor, MemberRole target) => actor == MemberRole.Owner && target != MemberRole.Owner || actor == MemberRole.Admin && target is MemberRole.Accountant or MemberRole.Viewer;
    public static void MapOrganizations(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/organizations").RequireAuthorization();
        g.MapPost("/", async (OrganizationInput r, ClaimsPrincipal u, AppDb db) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 200)
                return Results.ValidationProblem(new Dictionary<string, string[]> { { "name", ["企业名称长度须为1–200"] } });
            if (string.IsNullOrWhiteSpace(r.TimeZone))
                return Results.Problem(statusCode: 400, title: "企业时区无效");
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(r.TimeZone);
            }
            catch (TimeZoneNotFoundException) { return Results.Problem(statusCode: 400, title: "企业时区无效"); }
            catch (InvalidTimeZoneException) { return Results.Problem(statusCode: 400, title: "企业时区无效"); }
            var org = new Organization { Name = r.Name.Trim(), TimeZone = r.TimeZone };
            db.Organizations.Add(org);
            db.Members.Add(new()
            {
                Organization = org,
                UserId = Uid(u),
                Role = MemberRole.Owner
            });
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/organizations/{org.Id}", org);
        });
        g.MapGet("/", async (ClaimsPrincipal u, AppDb db, int page = 1, int pageSize = 20) =>
        {
            if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100)
                return Results.Problem(statusCode: 400, title: "分页参数无效");
            var q = db.Members.AsNoTracking().Where(x => x.UserId == Uid(u));
            return Results.Ok(new
            {
                total = await q.CountAsync(),
                items = await q.OrderBy(x => x.Organization.CreatedAt).ThenBy(x => x.OrganizationId).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new { id = x.OrganizationId, x.Organization.Name, role = x.Role.ToString() }).ToListAsync()
            });
        });
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal u, AppDb db) =>
        {
            var m = await db.Members.AsNoTracking().Where(x => x.OrganizationId == id && x.UserId == Uid(u)).Select(x => new { id = x.OrganizationId, x.Organization.Name, role = x.Role.ToString() }).SingleOrDefaultAsync();
            return m is null ? Results.NotFound() : Results.Ok(m);
        });
        g.MapGet("/{id:guid}/members", async (Guid id, ClaimsPrincipal u, AppDb db) =>
        {
            var actor = await db.Members.FindAsync(id, Uid(u));
            if (actor is null)
                return Results.NotFound();
            if (actor.Role is not (MemberRole.Owner or MemberRole.Admin))
                return Results.Forbid();
            return Results.Ok(await db.Members.AsNoTracking().Where(x => x.OrganizationId == id).Select(x => new { x.UserId, x.User.Email, role = x.Role.ToString() }).ToListAsync());
        });
        g.MapPost("/{id:guid}/members", async (Guid id, MemberInput r, ClaimsPrincipal u, AppDb db, UserManager<IdentityUser> users) =>
        {
            var actor = await db.Members.FindAsync(id, Uid(u));
            if (actor is null)
                return Results.NotFound();
            if (!Enum.IsDefined(r.Role))
                return Results.Problem(statusCode: 400, title: "角色无效");
            if (!CanManage(actor.Role, r.Role))
                return Results.Forbid();
            if (!System.Net.Mail.MailAddress.TryCreate(r.Email, out _))
                return Results.Problem(statusCode: 400, title: "邮箱格式无效");
            var target = await users.FindByEmailAsync(r.Email ?? "");
            if (target is null)
                return Results.Problem(statusCode: 400, title: "用户须先注册");
            if (await db.Members.AnyAsync(x => x.OrganizationId == id && x.UserId == target.Id))
                return Results.Conflict();
            db.Members.Add(new()
            {
                OrganizationId = id,
                UserId = target.Id,
                Role = r.Role
            });
            db.Add(Finance.Audit(id, u, "MemberAdded", id, target.Id + ":" + r.Role));
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Conflict(); }
            return Results.NoContent();
        });
    }
}

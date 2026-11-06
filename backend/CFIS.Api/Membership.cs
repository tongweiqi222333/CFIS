using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public class MemberInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public MemberRole Role { get; set; }
    public string InviterId { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
public record RoleInput(MemberRole Role);
public record InvitationAcceptance(string Token, string? Password);
public static class Membership
{
    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static void MapMembership(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/organizations/{org:guid}").RequireAuthorization();
        g.MapPatch("/members/{userId}/role", async (Guid org, string userId, RoleInput input, ClaimsPrincipal user, AppDb db) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await AccountingService.Lock(db, org);
            var actor = await db.Members.FindAsync(org, Finance.Uid(user));
            if (actor == null)
                return Results.NotFound();
            var target = await db.Members.FindAsync(org, userId);
            if (target == null)
                return Results.NotFound();
            if (!Enum.IsDefined(input.Role))
                return Finance.Error(400, "角色无效");
            if (!Organizations.CanManage(actor.Role, target.Role) || !Organizations.CanManage(actor.Role, input.Role))
                return Results.Forbid();
            target.Role = input.Role;
            db.Add(Finance.Audit(org, user, "MemberRoleChanged", org, userId + ":" + input.Role));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
        g.MapDelete("/members/{userId}", async (Guid org, string userId, ClaimsPrincipal user, AppDb db) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await AccountingService.Lock(db, org);
            var actor = await db.Members.FindAsync(org, Finance.Uid(user));
            if (actor == null)
                return Results.NotFound();
            var target = await db.Members.FindAsync(org, userId);
            if (target == null)
                return Results.NotFound();
            if (!Organizations.CanManage(actor.Role, target.Role))
                return Results.Forbid();
            db.Remove(target);
            db.Add(Finance.Audit(org, user, "MemberRemoved", org, userId));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
        g.MapPost("/invitations", async (Guid org, MemberInput input, ClaimsPrincipal user, AppDb db) =>
        {
            var actor = await db.Members.FindAsync(org, Finance.Uid(user));
            if (actor == null)
                return Results.NotFound();
            if (!Enum.IsDefined(input.Role) || !System.Net.Mail.MailAddress.TryCreate(input.Email, out _))
                return Finance.Error(400, "邮箱或角色无效");
            if (!Organizations.CanManage(actor.Role, input.Role))
                return Results.Forbid();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var invitation = new MemberInvitation { OrganizationId = org, Email = input.Email.Trim().ToUpperInvariant(), TokenHash = Hash(token), Role = input.Role, InviterId = Finance.Uid(user), ExpiresAt = DateTimeOffset.UtcNow.AddDays(2) };
            db.Add(invitation);
            db.Add(Finance.Audit(org, user, "MemberInvited", invitation.Id, input.Role.ToString()));
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                invitation.Id,
                token,
                invitation.ExpiresAt
            });
        });
        app.MapPost("/api/v1/invitations/accept", async (InvitationAcceptance input, ClaimsPrincipal principal, AppDb db, UserManager<IdentityUser> users) =>
        {
            if (input.Token == null || input.Token.Length != 64)
                return Finance.Error(400, "邀请无效");
            var hash = Hash(input.Token);
            var invitation = await db.Set<MemberInvitation>().AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash);
            if (invitation == null)
                return Finance.Error(400, "邀请无效");
            await using var tx = await db.Database.BeginTransactionAsync();
            await AccountingService.Lock(db, invitation.OrganizationId);
            invitation = await db.Set<MemberInvitation>().SingleAsync(x => x.Id == invitation.Id);
            if (invitation.AcceptedAt != null || invitation.ExpiresAt <= DateTimeOffset.UtcNow)
                return Finance.Error(409, "邀请已使用或过期");
            var inviter = await db.Members.FindAsync(invitation.OrganizationId, invitation.InviterId);
            if (inviter == null || !Organizations.CanManage(inviter.Role, invitation.Role))
                return Results.Forbid();
            var target = await users.FindByEmailAsync(invitation.Email);
            if (target != null)
            {
                if (principal.Identity?.IsAuthenticated != true || Finance.Uid(principal) != target.Id)
                    return Results.Unauthorized();
            }
            else
            {
                target = new IdentityUser { UserName = invitation.Email, Email = invitation.Email, EmailConfirmed = true };
                var result = await users.CreateAsync(target, input.Password ?? "");
                if (!result.Succeeded)
                    return Results.ValidationProblem(result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));
            }
            if (await db.Members.AnyAsync(x => x.OrganizationId == invitation.OrganizationId && x.UserId == target.Id))
                return Results.Conflict();
            db.Members.Add(new()
            {
                OrganizationId = invitation.OrganizationId,
                UserId = target.Id,
                Role = invitation.Role
            });
            invitation.AcceptedAt = DateTimeOffset.UtcNow;
            db.Set<AuditLog>().Add(new()
            {
                OrganizationId = invitation.OrganizationId,
                ActorId = target.Id,
                Action = "InvitationAccepted",
                ResourceId = invitation.Id,
                Detail = invitation.Role.ToString()
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        }).RequireRateLimiting("auth");
    }
}

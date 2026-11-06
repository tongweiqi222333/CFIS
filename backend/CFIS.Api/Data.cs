using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public enum MemberRole
{
    Owner, Admin, Accountant, Viewer
}
public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TimeZone { get; set; } = "Asia/Shanghai";
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class OrganizationMember
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string UserId { get; set; } = "";
    public IdentityUser User { get; set; } = null!;
    public MemberRole Role { get; set; }
}
public class AppDb(DbContextOptions<AppDb> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> Members => Set<OrganizationMember>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        FinanceModel.Configure(b);
        BusinessModel.Configure(b);
        AccountingModel.Configure(b);
        Allocations.Configure(b);
        b.Entity<Attachment>().ToTable("attachments");
        b.Entity<Attachment>().HasIndex(x => new { x.OrganizationId, x.ResourceKind, x.ResourceId });
        b.Entity<Attachment>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<Attachment>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MemberInvitation>().ToTable("member_invitations");
        b.Entity<MemberInvitation>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<MemberInvitation>().Property(x => x.Email).HasMaxLength(256);
        b.Entity<MemberInvitation>().Property(x => x.TokenHash).HasMaxLength(64);
        b.Entity<MemberInvitation>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Organization>().ToTable("organizations");
        b.Entity<Organization>().Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Entity<OrganizationMember>().ToTable("organization_members").HasKey(x => new { x.OrganizationId, x.UserId });
        b.Entity<OrganizationMember>().Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        b.Entity<OrganizationMember>().HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        b.Entity<OrganizationMember>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
    }
}



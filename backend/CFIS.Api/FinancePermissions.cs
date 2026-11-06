namespace CFIS.Api;

public enum FinancePermission
{
    Read, Write, Audit
}
public static class FinancePermissions
{
    public static bool Allows(MemberRole role, FinancePermission permission) => permission switch
    {
        FinancePermission.Read => role is MemberRole.Owner or MemberRole.Admin or MemberRole.Accountant or MemberRole.Viewer,
        FinancePermission.Write => role is MemberRole.Owner or MemberRole.Accountant,
        FinancePermission.Audit => role is MemberRole.Owner or MemberRole.Admin,
        _ => false
    };
}

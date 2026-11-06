using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using static CFIS.Api.Finance;
namespace CFIS.Api;

public sealed class BudgetLifecycleService(AppDb db)
{
    public async Task<IResult> Change(Guid org, Guid id, BudgetStatusInput r, ClaimsPrincipal u)
    {
        var access = await Access(db, org, u, true);
        if (access != 200)
            return Error(access, "无权调整预算");
        if (r.Status is not ("Active" or "Closed"))
            return Error(400, "预算状态无效");
        await using var tx = await db.Database.BeginTransactionAsync();
        await RequestLock(db, org, org);
        var b = await db.Set<Budget>().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id);
        if (b is null)
            return Error(404, "预算不存在");
        if (b.Status == r.Status)
            return Results.NoContent();
        if (b.Status == "Closed" || b.Status == "Draft" && r.Status == "Closed")
            return Error(409, "预算状态不允许此操作");
        var effectiveAt = DateTimeOffset.UtcNow;
        if (r.Status == "Active")
        {
            var ids = await db.Set<BudgetItem>().Where(x => x.OrganizationId == org && x.BudgetId == id).Select(x => x.CategoryId).ToArrayAsync();
            if (await db.Set<Category>().CountAsync(x => x.OrganizationId == org && ids.Contains(x.Id) && x.Status == "Active") != ids.Length)
                return Error(409, "预算分类已停用");
            var overlaps = await db.Set<Budget>().Where(x => x.OrganizationId == org && x.Id != id && x.Status == "Active" && x.Currency == b.Currency && x.StartDate <= b.EndDate && x.EndDate >= b.StartDate && db.Set<BudgetItem>().Any(i => i.OrganizationId == org && i.BudgetId == x.Id && ids.Contains(i.CategoryId))).ToListAsync();
            if (overlaps.Any(x => x.Id != b.PreviousVersionId))
                return Error(409, "预算期间及分类与启用预算重叠", "BUDGET_OVERLAP");
            if (b.PreviousVersionId is Guid prior)
            {
                var old = await db.Set<Budget>().SingleAsync(x => x.OrganizationId == org && x.Id == prior);
                if (old.Status != "Active")
                    return Error(409, "原版本已关闭或被替代");
                old.Status = "Closed";
                old.ClosedAt = effectiveAt;
                db.Add(Audit(org, u, "BudgetSuperseded", old.Id, id.ToString()));
            }
        }
        var before = b.Status;
        b.Status = r.Status;
        if (r.Status == "Active")
            b.ActivatedAt = effectiveAt;
        else
            b.ClosedAt = effectiveAt;
        db.Add(Audit(org, u, "BudgetStatusChanged", id, System.Text.Json.JsonSerializer.Serialize(new
        {
            before,
            after = b.Status
        })));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.NoContent();
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
namespace CFIS.Api;

public sealed class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ResourceId { get; set; }
    public string ResourceKind { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string ActorId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public static class Attachments
{
    static async Task<bool> Exists(AppDb db, Guid org, string kind, Guid id) => kind switch
    {
        "Transaction" => await db.Set<FinancialTransaction>().AnyAsync(x => x.OrganizationId == org && x.Id == id),
        "Receivable" => await db.Set<Receivable>().AnyAsync(x => x.OrganizationId == org && x.Id == id),
        "Payable" => await db.Set<Payable>().AnyAsync(x => x.OrganizationId == org && x.Id == id),
        "Journal" => await db.Set<JournalEntry>().AnyAsync(x => x.OrganizationId == org && x.Id == id && x.Status == "Posted"),
        _ => false
    };
    public static void MapAttachments(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/organizations/{org:guid}/attachments").RequireAuthorization();
        g.MapGet("/", async (Guid org, string resourceKind, Guid resourceId, ClaimsPrincipal user, AppDb db) =>
        {
            var access = await Finance.Access(db, org, user);
            if (access != 200)
                return Finance.Error(access, "无权访问");
            if (!await Exists(db, org, resourceKind, resourceId))
                return Results.NotFound();
            return Results.Ok(await db.Set<Attachment>().AsNoTracking().Where(x => x.OrganizationId == org && x.ResourceKind == resourceKind && x.ResourceId == resourceId).OrderBy(x => x.CreatedAt).Take(100).Select(x => new { x.Id, x.Name, x.ContentType, x.Sha256, x.CreatedAt, size = x.Content.Length }).ToListAsync());
        });
        g.MapPost("/", async (Guid org, HttpRequest request, ClaimsPrincipal user, AppDb db) =>
        {
            var access = await Finance.Access(db, org, user, true);
            if (access != 200)
                return Finance.Error(access, "无权写入");
            if (!request.HasFormContentType || request.ContentLength is null or > 5300000)
                return Finance.Error(400, "附件限5MB");
            IFormCollection form;
            try { form = await request.ReadFormAsync(); }
            catch (InvalidDataException) { return Finance.Error(400,"附件表单格式无效"); }
            catch (BadHttpRequestException) { return Finance.Error(400,"附件请求格式无效"); }
            var kind = form["resourceKind"].ToString();
            if (!Guid.TryParse(form["resourceId"], out var id))
                return Finance.Error(400, "业务编号无效");
            await using var tx = await db.Database.BeginTransactionAsync();
            await AccountingService.Lock(db, org);
            if (!await Exists(db, org, kind, id))
                return Results.NotFound();
            if (await db.Set<Attachment>().CountAsync(x => x.OrganizationId == org && x.ResourceKind == kind && x.ResourceId == id) >= 100)
                return Finance.Error(400, "每项业务最多100个附件");
            if (form.Files.Count != 1)
                return Finance.Error(400, "请选择一个附件");
            var file = form.Files[0];
            if (file.Length is < 1 or > 5242880)
                return Finance.Error(400, "附件限5MB");
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            if (name.Length is < 1 or > 200 || name.Any(char.IsControl))
                return Finance.Error(400, "文件名无效");
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            var bytes = stream.ToArray();
            var type = bytes.AsSpan().StartsWith("%PDF-"u8) ? "application/pdf" : bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png" : bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? "image/jpeg" : null;
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (type == null || !(type == "application/pdf" && extension == ".pdf" || type == "image/png" && extension == ".png" || type == "image/jpeg" && extension is ".jpg" or ".jpeg"))
                return Finance.Error(400, "仅支持PDF、PNG、JPEG，文件内容须匹配扩展名");
            var attachment = new Attachment { OrganizationId = org, ResourceKind = kind, ResourceId = id, Name = name, ContentType = type, Content = bytes, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), ActorId = Finance.Uid(user) };
            db.Add(attachment);
            db.Add(Finance.Audit(org, user, "AttachmentAdded", attachment.Id, kind + ":" + id + ":" + attachment.Sha256));
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Created("", new
            {
                attachment.Id,
                attachment.Name,
                attachment.Sha256
            });
        });
        g.MapGet("/{id:guid}", async (Guid org, Guid id, ClaimsPrincipal user, AppDb db) =>
        {
            var access = await Finance.Access(db, org, user);
            if (access != 200)
                return Finance.Error(access, "无权访问");
            var attachment = await db.Set<Attachment>().AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == org && x.Id == id);
            if (attachment == null)
                return Results.NotFound();
            return Results.File(attachment.Content, attachment.ContentType, attachment.Name);
        });
    }
}

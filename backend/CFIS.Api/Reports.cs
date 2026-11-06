using System.Security.Claims;
using System.Text.Json;
namespace CFIS.Api;

public static partial class Finance
{
    static void MapReports(RouteGroupBuilder group)
    {
        foreach (var path in new[] { "/reports/summary", "/reports/cashflow", "/reports/aging", "/reports/export.xlsx", "/reports/export.pdf" })
        {
            group.MapGet(path, async (Guid org, DateOnly dateFrom, DateOnly dateTo, string currency, ClaimsPrincipal user, AppDb db, FinancialReportService service, HttpContext context, Guid? accountId = null, DateTimeOffset? knowledgeAt = null) =>
            {
                var access = await Access(db, org, user);
                if (access != 200)
                    return Error(access, "无权访问报表");
                context.Response.Headers.CacheControl = "no-store";
                var timer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var report = await service.Build(org, new(dateFrom, dateTo, currency, accountId, knowledgeAt));
                    if (path.EndsWith(".xlsx") || path.EndsWith(".pdf"))
                    {
                        var pdf = path.EndsWith(".pdf");
                        var bytes = pdf ? ReportExport.Pdf(report) : ReportExport.Excel(report);
                        db.Add(Audit(org, user, "FinancialReportExported", Guid.Empty, JsonSerializer.Serialize(new
                        {
                            dateFrom,
                            dateTo,
                            currency,
                            accountId,
                            knowledgeAt,
                            format = pdf ? "pdf" : "xlsx"
                        })));
                        await db.SaveChangesAsync();
                        return Results.File(bytes, pdf ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", pdf ? "CFIS-report.pdf" : "CFIS-report.xlsx");
                    }
                    return Results.Ok(report);
                }
                catch (ArgumentException ex) { return Error(400, ex.Message); }
                catch (KeyNotFoundException ex) { return Error(404, ex.Message); }
                finally { context.RequestServices.GetRequiredService<ILogger<FinancialReportService>>().LogInformation("Report {Path} completed in {Elapsed}ms", path, timer.ElapsedMilliseconds); }
            });
        }
    }
}

using ClosedXML.Excel;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
namespace CFIS.Api;

public sealed class ReportFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new("CFIS-Noto");
    public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "NotoSansSC.ttf"));
}
public static class ReportExport
{
    public static IEnumerable<(string Label, string Value)> Values(FinancialReport r)
    {
        yield return ("企业", r.Organization);
        yield return ("币种", r.Currency);
        yield return ("账户范围", r.AccountId?.ToString() ?? "企业全部账户");
        yield return ("业务期间", $"{r.DateFrom} - {r.DateTo}");
        yield return ("生成时间 UTC", r.GeneratedAt.ToString("O"));
        yield return ("记录截止 UTC", r.KnowledgeAt.ToString("O"));
        yield return ("统计口径", "资金流量；金额精确到4位；账龄为企业单据，不随账户过滤");
        yield return ("期初余额", r.Summary.Opening);
        yield return ("期间期初登记", r.Summary.PeriodOpening);
        yield return ("净收入", r.Summary.Income);
        yield return ("净支出", r.Summary.Expense);
        yield return ("外部于所选账户的转入", r.Summary.TransferIn);
        yield return ("外部于所选账户的转出", r.Summary.TransferOut);
        yield return ("期末余额", r.Summary.Closing);
        yield return ("全部已登记余额", r.Summary.AllRegistered);
        foreach (var c in r.Categories)
            yield return ($"分类 {c.Name} / {c.Kind}", c.Amount);
        foreach (var a in r.Aging)
            yield return ($"账龄 {a.Type} {a.Bucket}", a.Amount);
        foreach (var b in r.Budgets)
        {
            yield return ($"预算 {b.Name} / {b.Id}", $"计划 {b.Planned} / 期间实际 {b.Actual}");
            yield return ("版本与生效", $"前版本 {b.PreviousVersionId} / {b.ActivatedAt:O}");
        }
    }
    public static byte[] Excel(FinancialReport report)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("财务报表");
        var row = 1;
        foreach (var v in Values(report))
        {
            sheet.Cell(row, 1).Value = v.Label;
            sheet.Cell(row++, 2).Value = v.Value;
        }
        sheet.Columns(1, 2).Width = 45;
        var trend = book.AddWorksheet("收支趋势");
        string[] names = ["业务日期", "净收入（精确文本）", "净支出（精确文本）", "净额（精确文本）", "期间期初", "转入", "转出", "资金净变动"];
        for (int i = 0; i < names.Length; i++)
            trend.Cell(1, i + 1).Value = names[i];
        row = 2;
        foreach (var t in report.Trend)
        {
            trend.Cell(row, 1).Value = t.Date;
            trend.Cell(row, 2).Value = t.Income;
            trend.Cell(row, 3).Value = t.Expense;
            trend.Cell(row, 4).Value = t.Net;
            trend.Cell(row, 5).Value = t.PeriodOpening;
            trend.Cell(row, 6).Value = t.TransferIn;
            trend.Cell(row, 7).Value = t.TransferOut;
            trend.Cell(row++, 8).Value = t.CashNet;
        }
        trend.Columns().Width = 24;
        trend.SheetView.FreezeRows(1);
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }
    static readonly object FontLock = new();
    public static byte[] Pdf(FinancialReport report)
    {
        lock (FontLock)
        {
            GlobalFontSettings.FontResolver ??= new ReportFontResolver();
        }
        using var doc = new PdfDocument();
        doc.Info.Title = "CFIS 财务报表";
        XGraphics? graphics = null;
        double y = 0;
        var pageNumber = 0;
        var font = new XFont("CFIS", 10);
        var heading = new XFont("CFIS", 18);
        void Page()
        {
            graphics?.Dispose();
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            pageNumber++;
            graphics.DrawString("CFIS / 企业财务报表", heading, XBrushes.DarkSlateGray, new XPoint(40, 44));
            graphics.DrawString($"{report.Currency} | {report.DateFrom} - {report.DateTo} | {pageNumber}", font, XBrushes.Gray, new XPoint(40, 66));
            y = 96;
        }
        void Line(string value)
        {
            var current = "";
            foreach (var c in value.Replace("\r", " ").Replace("\n", " "))
            {
                if (graphics!.MeasureString(current + c, font).Width > 510)
                {
                    if (y > 780)
                        Page();
                    graphics!.DrawString(current, font, XBrushes.Black, new XPoint(40, y));
                    y += 17;
                    current = "";
                }
                current += c;
            }
            if (y > 780)
                Page();
            graphics!.DrawString(current, font, XBrushes.Black, new XPoint(40, y));
            y += 20;
        }
        Page();
        foreach (var v in Values(report))
            Line(v.Label + ": " + v.Value);
        Line("收支趋势 / 日期 | 收入 | 支出 | 净额");
        foreach (var t in report.Trend)
            Line($"{t.Date} | 收入 {t.Income} | 支出 {t.Expense} | 收支净额 {t.Net} | 资金净变动 {t.CashNet}");
        graphics?.Dispose();
        using var output = new MemoryStream();
        doc.Save(output, false);
        return output.ToArray();
    }
}

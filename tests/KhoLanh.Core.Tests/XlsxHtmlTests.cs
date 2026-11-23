using ClosedXML.Excel;
using KhoLanh.Core.Services;
using KhoLanh.Core.ViTri;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class XlsxHtmlTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-in-" + Guid.NewGuid().ToString("N"));
    public XlsxHtmlTests() { Directory.CreateDirectory(_dir); }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Renders_merges_styles_borders_orientation_and_sheets()
    {
        var p = Path.Combine(_dir, "a.xlsx");
        using (var wb = new XLWorkbook())
        {
            var a = wb.AddWorksheet("Day 01"); a.Cell(1, 1).Value = "TIÊU ĐỀ <1>"; a.Range(1, 1, 1, 3).Merge(); a.Cell(1, 1).Style.Font.Bold = true; a.Cell(1, 1).Style.Font.FontSize = 15;
            a.Cell(2, 1).Value = "A"; a.Cell(2, 2).Value = 12.5; a.Cell(2, 3).Value = new DateTime(2026, 11, 20);
            a.Range(2, 1, 2, 3).Style.Border.OutsideBorder = XLBorderStyleValues.Thin; a.Cell(2, 2).Style.NumberFormat.Format = "#,##0.00";
            a.Cell(3, 1).Value = "gộp dọc"; a.Range(3, 1, 4, 1).Merge(); a.Cell(2, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699");
            a.Column(1).Width = 20; a.Row(4).Hide();
            var b = wb.AddWorksheet("Ngang"); b.Cell(1, 1).Value = "x"; b.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            wb.SaveAs(p);
        }
        var html = XlsxHtml.Render(p, "Phiếu thử");
        Assert.Contains("<title>Phiếu thử</title>", html);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "class=\"sheet ").Count);
        Assert.Contains("colspan=\"3\"", html); Assert.Contains("rowspan=\"2\"", html);
        Assert.Contains("font-weight:700", html); Assert.Contains("TIÊU ĐỀ &lt;1&gt;", html);       // chữ được mã hóa HTML
        Assert.Contains("12.50", html); Assert.Contains("2026", html);
        Assert.Contains("border-top:1px solid #000", html); Assert.Contains("background:#FFE699", html);
        Assert.Contains("sheet l", html); Assert.Contains("sheet p", html);                          // sheet ngang + sheet đứng
        Assert.Contains("window.print()", html);
        var f = XlsxHtml.WritePage(p, "x", Path.Combine(_dir, "in")); Assert.True(File.Exists(f)); Assert.EndsWith(".html", f);
    }

    [Fact]
    public void Prints_the_real_ViTri_slip_export()
    {
        var db = Path.Combine(_dir, "kho.db");
        var s = new AppServices(db); s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1"); s.Auth.Login("admin", "matkhau1");
        var items = new List<ViTriService.ViewItem> { new() { Ma = "TPC-ABC", Vt = "A.02.01", Dt = "MVS", Hd = "HD1", X = 12 }, new() { Ma = "TPC-XYZ", Vt = "B.01.03", Dt = "ISHI", Hd = "HD2", X = 3.5 } };
        var x = Path.Combine(_dir, "px.xlsx"); s.ViTri.ExportExcel(x, items, "PHIẾU THAY THÙNG", new DateTime(2026, 11, 20));
        var html = XlsxHtml.Render(x, "Phiếu");
        Assert.Contains("PHIẾU THAY THÙNG : 20/11/2026", html); Assert.Contains("TPC-ABC", html); Assert.Contains("A.02.01", html); Assert.Contains("Tổng (2 dòng)", html);
        Assert.Contains("15.50", html);
        Assert.Contains("font-family:'Times New Roman'", html);
    }
}

using ClosedXML.Excel;
using KhoLanh.Core.TonKho;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.73 (N4): bộ đọc xlsx nhanh cho kết quả GIỐNG HỆT cách đọc cũ (ClosedXML) — chữ đang hiện theo định dạng ô, vùng có dữ liệu.</summary>
public class DocExcelNhanhTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-xl-" + Guid.NewGuid().ToString("N"));
    public DocExcelNhanhTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private string Mau()
    {
        var p = Path.Combine(_dir, "mau.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Dữ liệu");
        ws.Cell(3, 2).Value = "Mã"; ws.Cell(3, 3).Value = "Ngày"; ws.Cell(3, 4).Value = "%"; ws.Cell(3, 5).Value = "Bool"; ws.Cell(3, 6).Value = "CT";
        ws.Cell(4, 2).Value = "0123"; ws.Cell(4, 3).Value = new DateTime(2026, 11, 24); ws.Cell(4, 3).Style.DateFormat.Format = "dd/mm/yyyy";
        ws.Cell(4, 4).Value = 0.125; ws.Cell(4, 4).Style.NumberFormat.NumberFormatId = 10;
        ws.Cell(4, 5).Value = true; ws.Cell(4, 6).FormulaA1 = "D4*2";
        ws.Cell(5, 3).Value = -1234.5; ws.Cell(5, 3).Style.NumberFormat.Format = "#,##0.00;[Red](#,##0.00)";
        ws.Cell(5, 4).Value = 3; ws.Cell(5, 4).Style.NumberFormat.Format = "#,##0.###0";
        ws.Cell(5, 5).Value = 30; ws.Cell(5, 5).Style.NumberFormat.Format = "#,###.##";
        ws.Cell(7, 2).Value = "  có khoảng  "; ws.Cell(7, 7).Value = 1e12; ws.Cell(8, 2).Style.Fill.BackgroundColor = XLColor.Red;
        ws.Cell(7, 3).Value = 45000.75; ws.Cell(7, 3).Style.NumberFormat.Format = "dd/mm/yyyy hh:mm";
        ws.Cell(7, 4).Value = 0.000012345; ws.Cell(7, 5).Value = "";
        ws.Cell(6, 6).FormulaA1 = "\"chữ\" & B4";
        var rt = ws.Cell(9, 2); rt.GetRichText().AddText("Đậm").SetBold(); rt.GetRichText().AddText(" thường");
        for (var r = 10; r < 400; r++) { ws.Cell(r, 2).Value = "MA" + r; ws.Cell(r, 3).Value = r * 1.25; ws.Cell(r, 3).Style.NumberFormat.Format = r % 2 == 0 ? "#,##0.00" : "0.0"; }
        wb.AddWorksheet("Trống");
        var w3 = wb.AddWorksheet("Lỗi"); w3.Cell(1, 1).FormulaA1 = "1/0"; w3.Cell(1, 2).Value = 12.5; w3.Cell(1, 2).Style.NumberFormat.Format = "0.0000";
        wb.SaveAs(p);
        return p;
    }

    [Fact]
    public void Doc_nhanh_giong_het_cach_cu()
    {
        var p = Mau();
        var a = SheetText.ReadClosedXml(p);
        var b = SheetText.ReadXlsxFast(p);
        Assert.Equal(a.SheetNames, b.SheetNames);
        foreach (var n in a.SheetNames)
        {
            Assert.Equal(a.Sheets[n].Count, b.Sheets[n].Count);
            for (var i = 0; i < a.Sheets[n].Count; i++) Assert.Equal(a.Sheets[n][i], b.Sheets[n][i]);
        }
        Assert.Equal("Đậm thường", b.Sheets["Dữ liệu"].Single(r => r[0].StartsWith("Đậm"))[0]);
    }

    [Fact]
    public void Ton_theo_vi_tri_doc_nhanh_giong_cach_cu()
    {
        var p = Path.Combine(_dir, "TON VI TRI 23.11.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("TON");
            ws.Cell(1, 1).Value = "BÁO CÁO TỒN KHO THEO VỊ TRÍ";
            var h = new[] { "Mã hàng", "Tên hàng", "ĐVT", "Lô (Batch)", "Vị trí", "SL Tồn", "T.Lượng" };
            for (var c = 0; c < h.Length; c++) ws.Cell(3, c + 1).Value = h[c];
            for (var r = 4; r < 300; r++)
            {
                ws.Cell(r, 1).Value = r % 37 == 0 ? "" : "VRHLA" + (r % 20);
                ws.Cell(r, 2).Value = "Tôm " + r; ws.Cell(r, 3).Value = "Kiện"; ws.Cell(r, 4).Value = r % 3 == 0 ? (XLCellValue)(20260000 + r) : "L" + r;
                ws.Cell(r, 5).Value = $"TG1.A.{r % 9 + 1:00}.1.{r % 4 + 1}";
                ws.Cell(r, 6).Value = r * 1.5; ws.Cell(r, 6).Style.NumberFormat.Format = "#,##0";   // định dạng làm tròn → phải lấy số gốc
                ws.Cell(r, 7).Value = 10.8;
            }
            wb.SaveAs(p);
        }
        var s = new AppServices(Path.Combine(_dir, "kho.db"));
        s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); s.Auth.Login("admin", "matkhau1");
        var oldF = s.ViTri.ReadFileClosedXml(p);
        var newF = s.ViTri.ReadFile(p);
        Assert.Equal(oldF.HeaderRow, newF.HeaderRow); Assert.Equal(oldF.Sheet, newF.Sheet);
        Assert.Equal(oldF.Rows.Count, newF.Rows.Count);
        for (var i = 0; i < oldF.Rows.Count; i++)
        {
            var (x, y) = (oldF.Rows[i], newF.Rows[i]);
            Assert.Equal((x.MaGoc, x.Ma, x.Ten, x.Dvt, x.Lo, x.Vt, x.Sl, x.Tl), (y.MaGoc, y.Ma, y.Ten, y.Dvt, y.Lo, y.Vt, y.Sl, y.Tl));
        }
        Assert.Equal(6, newF.Rows[0].Sl);   // 4 × 1.5 — số gốc, không phải chữ "6" làm tròn
    }
}

using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.TonKho;
using KhoLanh.Core.TonKhoGui;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.69: tab Báo Cáo Tồn kho An An (Bột + Sốt + Bếp luôn hiện, dòng Tồn Kho Gửi, nhắc khi xuất) + cảnh báo đối soát An An / Gửi.</summary>
public class BaoCaoTongTonTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-bc-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public BaoCaoTongTonTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1"); _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static readonly string[] Heads = { "Item ID", "Mã hàng", "Tên hàng", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn đầu", "Nhập", "Xuất", "Tồn cuối", "Tồn cuối (NET)", "Trọng lượng", "Nguyên liệu", "Qui cách đông", "Tình trạng", "Hóa chất", "Thị trường", "Khách hàng", "Mạ băng", "Size", "Qui cách gói" };

    private string AnAnXlsx()
    {
        var path = Path.Combine(_dir, "TON 20.11.xlsx");
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Sheet");
        ws.Cell(1, 1).Value = "Báo cáo - Kho - 05-Nhập xuất tồn";
        for (var i = 0; i < Heads.Length; i++) ws.Cell(2, i + 1).Value = Heads[i];
        var rows = new[] { ("A1", "KD", 10.0, 2.0, "16/20", "Thành phẩm"), ("B2", "EU", 5.0, 3.0, "21/25", "Thành phẩm"), ("C3", "EU", 4.0, 1.0, "31/40", "") };
        var r = 3;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = r; ws.Cell(r, 2).Value = "TPC-" + x.Item1; ws.Cell(r, 3).Value = "Tôm " + x.Item1; ws.Cell(r, 4).Value = x.Item2; ws.Cell(r, 5).Value = "HD"; ws.Cell(r, 6).Value = "Thùng";
            ws.Cell(r, 10).Value = x.Item3; ws.Cell(r, 11).Value = x.Item3 * x.Item4; ws.Cell(r, 12).Value = x.Item4; ws.Cell(r, 15).Value = x.Item6; ws.Cell(r, 20).Value = x.Item5; r++;
        }
        wb.SaveAs(path); return path;
    }

    [Fact]
    public void Bot_sot_bep_always_shown_as_zero_and_export_warns()
    {
        var aa = _s.AnAn; aa.Load();
        aa.Commit(aa.Preview(AnAnXlsx()), keepUndo: false);
        var rows = aa.D.TongTonRows();
        Assert.Equal(AnAnData.ManualLabel, rows[0].Label);
        Assert.Equal(0, rows[0].Weight);                                        // chưa nhập → vẫn hiện, = 0
        Assert.True(BaoCaoTongTon.ManualMissing(aa.D));
        var warn = BaoCaoTongTon.CanhBaoXuat(aa.D);
        Assert.Equal(2, warn.Count);                                             // Bột + Sốt + Bếp + Kho Gửi chưa có
        Assert.Contains(warn, w => w.Contains(AnAnData.ManualLabel));

        // file Excel tổng tồn: dòng Bột + Sốt + Bếp = 0 ngay dưới tiêu đề
        var p = Path.Combine(_dir, "tt.xlsx"); AnAnExport.BaoCaoAll(aa.D, "20/11/2026")!.Save(p);
        using (var wb = new XLWorkbook(p))
        {
            var ws = wb.Worksheet("Tổng Tồn");
            Assert.Equal(AnAnData.ManualLabel, ws.Cell(3, 1).GetString());
            Assert.Equal(0, ws.Cell(3, 2).GetDouble());
        }

        // nhập Bột + Sốt + Bếp, có Kho Gửi → hết nhắc; bảng có Tồn Kho Gửi + tổng cộng dồn (Kho Gửi không cộng vào TỔNG TỒN KHO)
        aa.SetManualValue(AnAnData.ManualLabel, 120);
        _s.Lan.Write("ton-kho-gui", new Dictionary<string, JsonArray?>
        {
            ["DuLieuChinh"] = new JsonArray(new JsonObject { ["Trọng Lượng (KG)"] = "300" }),
            ["DuLieuMeta"] = new JsonArray(new JsonObject { ["data"] = "__JSON__{\"dataDate\":\"21/11/2026\",\"importedAt\":1}" }),
        });
        aa.SyncKhoGui(true);
        Assert.Empty(BaoCaoTongTon.CanhBaoXuat(aa.D));
        var bang = BaoCaoTongTon.Bang(aa.D);
        Assert.Equal(new[] { "total", "khogui", "grand" }, bang.TakeLast(3).Select(b => b.Kind));
        var total = bang.Single(b => b.Kind == "total").Weight!.Value;
        Assert.Equal(aa.D.TongTonRows().Sum(r => r.Weight ?? 0), total, 6);
        Assert.Equal(300, bang.Single(b => b.Kind == "khogui").Weight);
        Assert.Equal(total + 300, bang.Single(b => b.Kind == "grand").Weight!.Value, 6);
    }

    [Fact]
    public void An_an_doi_soat_ignores_manual_row_and_flags_difference()
    {
        var aa = _s.AnAn; aa.Load();
        aa.Commit(aa.Preview(AnAnXlsx()), keepUndo: false);
        var ds = BaoCaoTongTon.DoiSoat(aa.D);
        double? V(string k) => ds.Rows.Single(r => r.Key == k).Value;
        Assert.Equal(V("mod")!.Value - V("all")!.Value, V("diff")!.Value, 6);
        Assert.Equal(BaoCaoTongTon.Lech(V("diff")), ds.CanhBao.Any(c => c.Key == "diff" && c.Loi));
        var before = V("diff");
        aa.SetManualValue(AnAnData.ManualLabel, 999);                           // số nhập tay không có trong Load Data → không làm lệch
        var ds2 = BaoCaoTongTon.DoiSoat(aa.D);
        Assert.Equal(before, ds2.Rows.Single(r => r.Key == "diff").Value);
        Assert.Equal(ds2.Rows.Single(r => r.Key == "mod").Value!.Value + 999, ds2.Rows.Single(r => r.Key == "tong").Value!.Value, 6);
    }

    [Fact]
    public void Gui_doi_soat_warnings()
    {
        var ok = new GuiData.DoiSoat { LoadTotal = 100, ModuleTotal = 100 };
        Assert.Empty(BaoCaoTongTon.GuiCanhBao(ok, 100));
        var w = BaoCaoTongTon.GuiCanhBao(ok, 90);                                // Bảng Tổng Hợp thiếu 10 kg không rõ lý do → lỗi
        Assert.Contains(w, c => c.Key == "c" && c.Loi && c.Text.Contains("10.00"));
        var dup = new GuiData.DoiSoat { LoadTotal = 100, ModuleTotal = 120, DupSum = 20, DupCount = 1, UngroupedSum = 30, UngroupedCount = 2 };
        var w2 = BaoCaoTongTon.GuiCanhBao(dup, 70);
        Assert.DoesNotContain(w2, c => c.Key == "c");                            // lệch đã giải thích được (đếm trùng + chưa gộp)
        Assert.Contains(w2, c => c.Key == "m" && !c.Loi);
        Assert.Contains(w2, c => c.Key == "!ung" && !c.Loi);
    }
}

using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.Services;
using KhoLanh.Core.SoKho;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.SoKho.SoKhoCore;

namespace KhoLanh.Core.Tests;

public class SoKhoServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-sk-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private SoKhoService Sk => _s.SoKho;
    public SoKhoServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); _s.Auth.Login("admin", "matkhau1");
        Sk.Now = () => new DateTime(2026, 11, 23, 9, 0, 0);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static SkLine L(string loai, string ngay, string ma, double? sl, double? netDv = null, string vt = "A1", string hd = "", string ct = "") =>
        NormLine(new JsonObject { ["loai"] = loai, ["ngay"] = ngay, ["maHang"] = ma, ["soLuong"] = sl, ["netDv"] = netDv, ["viTri"] = vt, ["hopDong"] = hd, ["chungTu"] = ct });

    [Fact]
    public void Luu_sua_phieu_hoan_tac_chot_ton()
    {
        Sk.Load();
        Sk.SavePhieu(new[] { L("nhap", "2026-11-01", "BOT-01", 10, 25, ct: "NK-0111-01"), L("nhap", "2026-11-01", "SOT-01", 4, 20, ct: "NK-0111-01") }, null, "nhap", "NK-0111-01");
        Assert.Equal(2, Sk.Lines.Count);
        Assert.Equal(250, Sk.Lines.Single(l => l.MaHang == "BOT-01").Net);
        var x = Sk.SavePhieu(new[] { L("xuat", "2026-11-05", "BOT-01", 3, hd: "LSX 26.11.001", ct: "BS-0511-01") }, null, "xuat", "BS-0511-01");
        Assert.Equal(175, Stock(Sk.Lines, null).Single(r => r["maHang"] == "BOT-01").Net);
        // sửa phiếu xuất: 3 → 4 bao (giữ id dòng)
        var old = Sk.Lines.Single(l => l.Loai == "xuat"); var ed = old.Clone(); ed.SoLuong = 4;
        Sk.SavePhieu(new[] { ed }, new[] { old.Id }, "xuat", "BS-0511-01");
        Assert.Equal(6, Stock(Sk.Lines, null).Single(r => r["maHang"] == "BOT-01").Sl);
        // hoàn tác lần sửa → về 3 bao; hoàn tác lần lưu xuất → hết dòng xuất
        var ops = HistOps(Sk.Hist);
        Sk.Undo(ops[0].OpId);
        Assert.Equal(3, Sk.Lines.Single(l => l.Loai == "xuat").SoLuong);
        Sk.Undo(x.OpId);
        Assert.DoesNotContain(Sk.Lines, l => l.Loai == "xuat");
        Assert.Throws<BusinessException>(() => Sk.Undo(x.OpId));
        // chốt tồn ngày 10/11 → 2 dòng tồn đầu; chốt lại cùng ngày → báo đã có
        var (r, _, ex) = Sk.Snapshot("2026-11-10");
        Assert.False(ex); Assert.Equal(2, r!.Added);
        Assert.True(Sk.Snapshot("2026-11-10").Exists);
        // phiếu lỗi (thiếu mã) bị chặn
        Assert.Throws<BusinessException>(() => Sk.SavePhieu(new[] { L("nhap", "2026-11-12", "", 1) }, null, "nhap", "x"));
    }

    [Fact]
    public void Nap_Excel_va_dinh_muc_canh_bao()
    {
        var path = Path.Combine(_dir, "sk.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("S");
            ws.Cell(1, 1).Value = "SỔ KHO BỘT";
            string[] h = { "Ngày", "Loại", "Vị trí", "Mã hàng", "Số lượng", "Net (kg/đơn vị)", "Hạn dùng" };
            for (var i = 0; i < h.Length; i++) ws.Cell(2, i + 1).Value = h[i];
            ws.Cell(3, 1).Value = "01/11/2026"; ws.Cell(3, 2).Value = "Tồn đầu"; ws.Cell(3, 3).Value = "A1"; ws.Cell(3, 4).Value = "BOT-01"; ws.Cell(3, 5).Value = 40; ws.Cell(3, 6).Value = 25; ws.Cell(3, 7).Value = "31/12/2026";
            ws.Cell(4, 1).Value = new DateTime(2026, 11, 2); ws.Cell(4, 3).Value = "A2"; ws.Cell(4, 4).Value = "BOT-01"; ws.Cell(4, 5).Value = 10; ws.Cell(4, 6).Value = 25;
            wb.SaveAs(path);
        }
        Sk.Load();
        var c = Sk.ImportExcel(path, "nhap");
        Assert.Equal(2, c.Added);
        Assert.Equal(new[] { "tondau", "nhap" }, Sk.Lines.Select(l => l.Loai).OrderByDescending(x => x));
        Assert.Equal(1250, Stock(Sk.Lines, null).Single().Net);
        Sk.SaveNorms(new[] { new Norm("", "", "BOT-01", "", 5, "kg/100kg", "") });
        Sk.SaveOutputs(new[] { new Output("", "LSX 1", 10000, "K", "") });
        var chk = Sk.CheckDraft(new[] { L("xuat", "2026-11-05", "BOT-01", 24, hd: "LSX 1") });   // 600 kg so với định mức 500 kg
        Assert.Equal("vuot", Assert.Single(chk.DinhMuc).St);
        Sk.SaveConfig(5, 45, new() { ["BOT-01"] = 100 });
        Assert.Equal(5, Sk.Tol); Assert.Single(Sk.BelowMin());
    }
}

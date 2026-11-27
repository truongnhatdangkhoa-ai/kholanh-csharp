using System.Text.Json.Nodes;
using KhoLanh.Core.SoKho;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.SoKho.SoKhoCore;

namespace KhoLanh.Core.Tests;

/// <summary>1.177: Vị Trí Bột có 2 ngăn — Excel (nạp lại là THAY) và NXT (đưa từ sổ kho).</summary>
public class VtbExcel177Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-vtbx-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public VtbExcel177Tests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static SkLine L(string ngay, string ma, double sl, double net, string vt) =>
        NormLine(new JsonObject { ["loai"] = "nhap", ["ngay"] = ngay, ["maHang"] = ma, ["soLuong"] = sl, ["netDv"] = net, ["viTri"] = vt, ["chungTu"] = "X" });
    private static List<IReadOnlyList<object?>> Aoa(params object?[][] rows) => rows.Select(r => (IReadOnlyList<object?>)r).ToList();

    [Fact]
    public void Nhan_dien_cot_khac_mau_chi_can_vi_tri_ma_bot_so_luong()
    {
        var p = VtbService.ParseSheet(Aoa(new object?[] { "Kho bột" }, new object?[] { "Số TT", "Vị trí", "Mã bột", "Số lượng", "Ghi chú lạ" },
            new object?[] { 1, "A.1.1", "BE6FD", 30.0, "x" }, new object?[] { 2, "A.1.2", "BE6FD", 0.0, "" }, new object?[] { 3, "", "WBC1", 5.0, "" }, new object?[] { 4, "A.1.3", "WBC1", "12,5", "" }), "S", onlyStock: true);
        Assert.True(p.Ok, p.Error);
        Assert.Equal(new[] { "A.1.1|BE6FD|30", "A.1.3|WBC1|12.5" }, p.Rows.Select(r => $"{r.Vt}|{r.Ma}|{r.Sl.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        Assert.Equal((1, 1), (p.SkippedZero, p.Skipped));   // SL = 0 bỏ; thiếu vị trí bỏ
        Assert.All(p.Rows, r => Assert.True(r.IsExcel));
    }

    [Fact]
    public void Chon_cot_bang_tay_khi_khong_nhan_ra()
    {
        var aoa = Aoa(new object?[] { "a", "b", "c" }, new object?[] { "A.1.1", "BE6FD", 7.0 });
        Assert.False(VtbService.ParseSheet(aoa, "S", true).Ok);
        Assert.Equal(0, VtbService.GuessHeaderRow(aoa));
        var p = VtbService.ParseSheet(aoa, "S", true, new() { ["vt"] = 0, ["ma"] = 1, ["sl"] = 2 }, 0);
        Assert.True(p.Ok, p.Error); Assert.Equal(7, p.Rows[0].Sl);
    }

    [Fact]
    public void Nap_Excel_thay_ngan_Excel_giu_ngan_NXT_va_doi_chieu_cong_tat()
    {
        var v = _s.Vtb; v.Load(); v.Now = () => new DateTime(2026, 11, 27, 8, 0, 0);
        v.Push(VtbService.FromLedger(new[] { L("2026-11-01", "BOT-A", 10, 25, "B1"), L("2026-11-01", "BOT-B", 4, 20, "B2") }, "2026-11-05"), "2026-11-05");
        VtbService.FileParse File(params (string vt, string ma, double sl)[] r) => VtbService.ParseSheet(Aoa(new object?[] { "VT", "MÃ HÀNG", "SL" }).Concat(r.Select(x => (IReadOnlyList<object?>)new object?[] { x.vt, x.ma, x.sl })).ToList(), "S", true);
        Assert.Equal(2, v.LoadExcel(File(("B1", "BOT-A", 6), ("E1", "BOT-C", 9)), "f1.xlsx"));
        Assert.Equal(4, v.Rows.Count);
        // nạp file 2: ngăn Excel bị THAY (không cộng dồn), ngăn NXT giữ nguyên
        v.Now = () => new DateTime(2026, 11, 27, 9, 0, 0);
        var s1 = v.CreateSlip(new[] { new VtbService.Pick(3, 2) }, "");   // dòng Excel E1
        Assert.Equal(7, v.Rows[3].Sl);
        v.Now = () => new DateTime(2026, 11, 27, 10, 0, 0);
        Assert.Equal(1, v.LoadExcel(File(("E1", "BOT-C", 20)), "f2.xlsx"));
        Assert.Equal(3, v.Rows.Count); Assert.Equal(2, v.Rows.Count(r => !r.IsExcel)); Assert.Equal(20, v.Rows.Single(r => r.IsExcel).Sl);
        Assert.True(v.Locked(s1));                       // phiếu của đợt Excel cũ bị khóa
        v.DeleteSlip(s1.Id);
        // đưa lại NXT: ngăn Excel còn nguyên
        v.Now = () => new DateTime(2026, 11, 27, 11, 0, 0);
        v.Push(VtbService.FromLedger(new[] { L("2026-11-01", "BOT-A", 10, 25, "B1") }, "2026-11-05"), "2026-11-05");
        Assert.Equal((1, 1), (v.Rows.Count(r => !r.IsExcel), v.Rows.Count(r => r.IsExcel)));
        // đối chiếu: cùng mã + vị trí ở cả 2 ngăn
        v.LoadExcel(File(("B1", "bot-a", 3), ("E1", "BOT-C", 1)), "f3.xlsx");
        var o = v.Overlaps(); Assert.Single(o); Assert.Equal((10, 3), (o[0].SlNxt, o[0].SlExcel));
        // công tắc: tắt Excel → điện thoại chỉ nhận ngăn NXT; khóa dòng không đổi
        var all = v.PushRows(); Assert.Equal(3, all.Count); Assert.Contains(all, r => r["Nguồn"]!.ToString() == "Excel");
        v.SetPush(true, false);
        var nxt = v.PushRows(); Assert.Single(nxt); Assert.Equal("NXT", nxt[0]["Nguồn"]!.ToString());
        var v2 = new VtbService(() => _s.Lan, _s.Session); v2.Load();
        Assert.False(v2.PushExcel); Assert.Equal("f3.xlsx", v2.ExcelFile); Assert.Equal(3, v2.Rows.Count);
        Assert.StartsWith("X:", VtbService.Keys(v2.Rows)[1]);
    }
}

using System.Text.Json.Nodes;
using KhoLanh.Core.Services;
using KhoLanh.Core.SoKho;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.SoKho.SoKhoCore;

namespace KhoLanh.Core.Tests;

public class VtbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-vtb-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public VtbTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static SkLine L(string loai, string ngay, string ma, double sl, double net, string vt) =>
        NormLine(new JsonObject { ["loai"] = loai, ["ngay"] = ngay, ["maHang"] = ma, ["soLuong"] = sl, ["netDv"] = net, ["viTri"] = vt, ["chungTu"] = "X" });

    [Fact]
    public void Day_ton_lap_phieu_huy_tru_xoa_khoa()
    {
        var lines = new List<SkLine> { L("nhap", "2026-11-01", "BOT-A", 10, 25, "B1"), L("nhap", "2026-11-01", "BOT-B", 4, 20, "B2"), L("xuat", "2026-11-03", "BOT-B", 4, 20, "B2"), L("nhap", "2026-11-02", "BOT-A", 3, 25, "B3") };
        var push = VtbService.FromLedger(lines, "2026-11-05");
        Assert.Equal(new[] { "B1|BOT-A|10", "B3|BOT-A|3" }, push.Select(r => $"{r.ViTri}|{r.MaHang}|{r.Sl}"));   // BOT-B đã hết → bỏ
        var v = _s.Vtb; v.Load();
        v.Now = () => new DateTime(2026, 11, 5, 8, 0, 0);
        Assert.Equal(2, v.Push(push, "2026-11-05"));
        Assert.Equal("05/11/2026", v.DataDate);
        var s = v.CreateSlip(new[] { new VtbService.Pick(0, 4) }, "xuất cho xưởng");
        Assert.Equal(6, v.Rows[0].Sl);
        Assert.Throws<BusinessException>(() => v.CreateSlip(new[] { new VtbService.Pick(1, 9) }, ""));
        // máy khác đọc lại: cùng tồn + phiếu
        var v2 = new VtbService(() => _s.Lan, _s.Session); v2.Load();
        Assert.Equal(6, v2.Rows[0].Sl); Assert.Single(v2.Slips); Assert.Equal("NXT Bột/Sốt · 250 kg", v2.Rows[0].GhiChu);
        v.SetDeducted(s.Id, false); Assert.Equal(10, v.Rows[0].Sl);
        v.SetDeducted(s.Id, true); Assert.Equal(6, v.Rows[0].Sl);
        var s2 = v.CreateSlip(new[] { new VtbService.Pick(1, 1) }, "");
        v.DeleteSlip(s2.Id); Assert.Equal(3, v.Rows[1].Sl); Assert.Single(v.Slips);
        // đẩy lại tồn → phiếu đợt cũ bị khóa
        v.Now = () => new DateTime(2026, 11, 6, 8, 0, 0);
        v.Push(push, "2026-11-06");
        Assert.True(v.Locked(v.Slips[0]));
        Assert.Throws<BusinessException>(() => v.SetDeducted(s.Id, false));
        v.DeleteSlip(s.Id); Assert.Equal(10, v.Rows[0].Sl);   // xóa phiếu khóa không hoàn SL
    }

    [Fact]
    public void Dinh_dang_ngay_giong_app_cu()
    {
        Assert.Equal("23/09/2026", VtbService.FmtDate("23092026"));
        Assert.Equal("05/11/2026", VtbService.FmtDate("2026-11-05"));
        Assert.Equal("19/11/2025", VtbService.FmtDate("19/11/2025"));
        Assert.Equal("13/05/2026", VtbService.FmtDate("05/13/2026"));
        Assert.Equal("01/10/2025", VtbService.FmtDate("45931"));
        Assert.Equal("abc", VtbService.FmtDate("abc"));
        Assert.Equal(new[] { "A|V||#0", "A|V||#1", "" }, VtbService.Keys(new List<VtbRow> { new() { Ma = "A", Vt = "V" }, new() { Ma = "A", Vt = "V" }, new() }));
    }
}

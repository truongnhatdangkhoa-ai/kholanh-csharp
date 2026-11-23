using KhoLanh.Core.KiemKe;
using Xunit;

namespace KhoLanh.Core.Tests;

public class PhatSinhTests
{
    [Fact]
    public void ParseNgay_cac_dang()
    {
        Assert.Equal("2026-12-25", PhatSinh.ParseNgay("25/12/2026", 2026).Iso);
        Assert.Equal("2026-12-25", PhatSinh.ParseNgay("25-12-26", 2026).Iso);
        Assert.Equal("2027-01-02", PhatSinh.ParseNgay("02/01", 2026, "2026-12-31").Iso);   // gần ngày chốt nhất
        Assert.True(PhatSinh.ParseNgay("", 2026).Blank);
        Assert.True(PhatSinh.ParseNgay("abc", 2026).Bad);
        Assert.True(PhatSinh.ParseNgay("31/02/2026", 2026).Bad);
    }

    private static KiemKeCore.Line Ln(string vt, string ma, double kien, string ngay) => new() { Vt = vt, Ma = ma, Key = ma, Kien = kien, Kg = kien * 10, Ngay = ngay };
    private static PhatSinh.Mv Mv(string type, string vt, string ma, double kien, string ngay) { var d = PhatSinh.ParseNgay(ngay, 2026); return new() { Type = type, Vt = vt, Ma = ma, Key = ma, Kien = kien, NgayRaw = ngay, Ngay = d.Iso, DateBlank = d.Blank, DateBad = d.Bad, PageNgay = "2026-12-01" }; }

    [Fact]
    public void Quy_ve_ngay_chot()
    {
        var rows = new List<KiemKeCore.Line> { Ln("TG1.A.01.1.1", "M1", 10, "2026-12-01") };
        var mvs = new List<PhatSinh.Mv>
        {
            Mv("x", "TG1.A.01.1.1", "M1", 3, "05/12/2026"),    // sau ngày đếm → trừ
            Mv("n", "TG1.A.01.1.1", "M1", 1, "06/12/2026"),    // cộng
            Mv("x", "TG1.A.01.1.1", "M1", 2, "30/11/2026"),    // trước ngày đếm → không tính, cần rà
            Mv("x", "TG1.A.01.1.1", "M1", 2, ""),              // thiếu ngày
            Mv("x", "TG1.A.01.1.1", "M1", 2, "05/01/2027"),    // sau chốt
        };
        var adj = PhatSinh.Build(mvs, rows, "2026-12-31");
        Assert.Equal((1, 1, 1), (adj.NTru, adj.NCong, adj.NSauChot));
        Assert.Equal(2, adj.NReview);
        var a = adj.ByKey["M1"]; Assert.Equal((1.0, 3.0), (a.NKien, a.XKien));
        Assert.Equal(30, a.XKg);                                   // kg ước tính theo kg/kiện của số đếm
        var stock = new List<KiemKeCore.Stock> { new() { Ma = "M1", Key = "M1", Ton = 8, Tl = 10 } };
        var t = KiemKeCore.BuildTotals(rows, stock, null, adj);
        var r = Assert.Single(t.Rows);
        Assert.Equal(8, r.KkKien);                                  // 10 + 1 − 3
        Assert.Equal(KiemKeCore.Khop, r.Status);
        Assert.Equal((1.0, 3.0), (r.NhapKien, r.XuatKien));
    }

    [Fact]
    public void Chi_co_phat_sinh_khong_la_dong_kiem_ke()
    {
        var cols = new[] { "Mã hàng", "Số kiện", "Ngày xuất", "SL xuất" };
        var ex = KiemKeCore.ExtractRows(new[] { new KiemKeCore.Page { Id = "p", Name = "T 01-12-2026", Ngay = "2026-12-01", Columns = cols.ToList(), Rows = new() { new[] { "M1", "", "05/12", "2" } } } }, "2026-12-31");
        Assert.Empty(ex.Rows);
        var mv = Assert.Single(ex.Mv);
        Assert.Equal(("x", "2026-12-05", 2.0), (mv.Type, mv.Ngay, mv.Kien));
    }
}

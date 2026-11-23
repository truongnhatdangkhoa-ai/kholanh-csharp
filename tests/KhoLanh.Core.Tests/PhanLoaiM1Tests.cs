using KhoLanh.Core.KiemKe;
using Xunit;

namespace KhoLanh.Core.Tests;

public class PhanLoaiM1Tests
{
    private static readonly string[] H = { "Tình trạng", "Mã hàng", "Khách hàng", "Mạ băng", "Tồn cuối" };
    private static string[] R(string tt, string ma, string kh = "", string mb = "", string ton = "1") => new[] { tt, ma, kh, mb, ton };

    [Fact]
    public void Phan_loai_co_ban()
    {
        var rows = new List<string[]>
        {
            R("Thành phẩm", "ARHL0123"),                      // HLSO? có Tình trạng TP, mã RHL -> tpc (primary tpc)
            R("Thành phẩm", "ARBR0123"),                      // BM không phải tpc
            R("Tạm", "ARHO0123"),                             // tôm nguyên liệu
            R("Tạm", "AXXX0123", "ECUADOR"),                  // nhập khẩu
            R("Tạm", "ARHL0123", "TÂN PHONG PHÚ"),            // gia công
        };
        var res = PhanLoaiM1.Classify(H, rows);
        Assert.Equal("tpc", res.Primary[0]);
        Assert.DoesNotContain("tpc", res.Hit[1]);
        Assert.Contains("tomnl", res.Hit[2]);
        Assert.Equal("tomnk", res.Primary[3]);
        Assert.Equal("tomgc", res.Primary[4]);
    }

    [Fact]
    public void Chuyen_thu_cong_va_dieu_kien_tu_them()
    {
        var rows = new List<string[]> { R("Thành phẩm", "ARHL0123"), R("Thành phẩm", "ARHL0456") };
        var manual = new Dictionary<int, string> { [1] = "bm" };
        var res = PhanLoaiM1.Classify(H, rows, manual);
        Assert.Equal("tpc", res.Primary[0]);
        Assert.Equal("bm", res.Primary[1]);
        var cf = new Dictionary<string, List<PhanLoaiM1.CustomFilter>> { ["tpc"] = new() { new() { ColLabel = "Mã hàng", Op = "eq", Value = "ARHL0123" } } };
        var r2 = PhanLoaiM1.Classify(H, rows, null, cf);
        Assert.Equal("tpc", r2.Primary[0]);
        Assert.NotEqual("tpc", r2.Primary[1]);
    }

    [Fact]
    public void Stock_va_dong_chinh_theo_ton_lon_nhat()
    {
        var stock = new List<KiemKeCore.Stock>
        {
            new() { Ma = "A1", Key = "A1", Ton = 5, Nhom = "tpc" },
            new() { Ma = "A1", Key = "A1", Ton = 20, Nhom = "tomnl" },
        };
        var t = KiemKeCore.BuildTotals(new List<KiemKeCore.Line>(), stock, null);
        var r = Assert.Single(t.Rows);
        Assert.Equal("tomnl", r.Nhom);
        Assert.Equal(2, r.NhomAll.Count);
        Assert.Contains(r.Notes, n => n.Contains("nhiều nhóm"));
    }
}

public class LsxTests
{
    [Theory]
    [InlineData("hd 12 - 05/03", "HD12")]
    [InlineData("LSX01-5/3/25", "LSX01")]
    [InlineData("LSX01", "LSX01")]
    public void LsxOf(string hd, string want) => Assert.Equal(want, KiemKeCore.LsxOf(hd));

    [Fact]
    public void So_theo_lsx()
    {
        var stock = new List<KiemKeCore.Stock>
        {
            new() { Ma = "A1", Key = "A1", Ton = 5, Nhom = "tpc", Hd = "L1-01/01", Tl = 10 },
            new() { Ma = "A1", Key = "A1", Ton = 3, Nhom = "tpc", Hd = "L2-02/01", Tl = 10 },
        };
        KiemKeCore.Line Ln(string hd, double k) => new() { Ma = "A1", Key = "A1", Kien = k, Kg = k * 10, Hd = hd };
        var ok = new List<KiemKeCore.Line> { Ln("L1", 5), Ln("L2", 3) };
        var t = KiemKeCore.BuildTotals(ok, stock, null); KiemKeCore.AddLsx(t.Rows, ok, stock);
        Assert.Equal("Khớp", t.Rows[0].LsxStatus);
        var bad = new List<KiemKeCore.Line> { Ln("L1", 3), Ln("L2", 5) };
        t = KiemKeCore.BuildTotals(bad, stock, null); KiemKeCore.AddLsx(t.Rows, bad, stock);
        Assert.Equal("Lệch", t.Rows[0].LsxStatus);   // tổng bằng nhau nhưng lệch từng LSX
        var none = new List<KiemKeCore.Line> { Ln("", 8) };
        t = KiemKeCore.BuildTotals(none, stock, null); KiemKeCore.AddLsx(t.Rows, none, stock);
        Assert.Equal("Thiếu thông tin", t.Rows[0].LsxStatus);
    }
}

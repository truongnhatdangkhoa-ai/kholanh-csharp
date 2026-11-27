using KhoLanh.Core.KhangSinh;
using Xunit;
using Nk = KhoLanh.Core.KhangSinh.KhangSinhNhapKhau;

namespace KhoLanh.Core.Tests;

/// <summary>1.182: mẫu ghi "Ngày 02.02, 60 kiện, Mẫu 28 :" (không có tên hàng / Size) — ảnh người dùng phiếu TP 230 / TP 236.</summary>
public class TomNhapKhauSize182Tests
{
    private static Nk.Mau M(int stt, string ten) => new() { Stt = stt, Ten = ten, Kien = 60, DatTinh = "23.MKS" };

    [Fact]
    public void Mau_khong_ghi_Size_lay_theo_Size_duy_nhat_cua_phieu()
    {
        var p = new Nk.Phieu();
        for (var i = 1; i <= 3; i++) p.Mau.Add(M(i, "Tôm HLSO 31/40 nhập khẩu"));
        p.Mau.Add(M(4, "Ngày 02.02, 60 kiện, Mẫu 4 :"));
        Nk.FillMissingSize(p);
        var g = Assert.Single(p.BySize());
        Assert.Equal("31/40", g.Size);
        Assert.Equal(4, g.Mau.Count);
        Assert.Equal("Ngày 02.02, 60 kiện, Mẫu 4", p.Mau[3].GhiChu);
        Assert.Contains(p.Issues, i => i.Contains("Mẫu 4") && i.Contains("31/40"));
        Nk.Check(p);
        Assert.DoesNotContain(p.Issues, i => i.Contains("không đọc được tên hàng"));
    }

    [Fact]
    public void Phieu_nhieu_Size_khong_doan()
    {
        var p = new Nk.Phieu();
        p.Mau.Add(M(1, "Tôm HLSO 31/35 nhập khẩu"));
        p.Mau.Add(M(2, "Tôm HLSO 31/40 nhập khẩu"));
        p.Mau.Add(M(3, "Ngày 02.02, 60 kiện, Mẫu 3 :"));
        Nk.FillMissingSize(p);
        Assert.Equal("Ngày 02.02, 60 kiện, Mẫu 3 :", p.Mau[2].Ten);
        Nk.Check(p);
        Assert.Contains(p.Issues, i => i.Contains("Mẫu 3: không đọc được tên hàng"));
    }
}

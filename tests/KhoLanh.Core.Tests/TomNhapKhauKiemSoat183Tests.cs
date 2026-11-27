using KhoLanh.Core;
using KhoLanh.Core.KhangSinh;
using Microsoft.Data.Sqlite;
using Xunit;
using Nk = KhoLanh.Core.KhangSinh.KhangSinhNhapKhau;

namespace KhoLanh.Core.Tests;

/// <summary>1.183: kiểm soát trước khi nạp tôm nhập khẩu — tình trạng từng Size, sửa tay, nạp từng phần, hoàn tác lần nạp.</summary>
public class TomNhapKhauKiemSoat183Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-nk183-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public TomNhapKhauKiemSoat183Tests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quan tri", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static Nk.Mau M(int stt, string ten, int kien = 50) => new()
    {
        Stt = stt, Ten = ten, Kien = kien, DatTinh = "23.MKS", Kq = "ĐẠT MKS", NgayNhap = "2026-11-18",
        Inputs = KhangSinhCore.FieldKeys.ToDictionary(k => k, _ => "ND"),
    };
    private static Nk.Phieu P(string so = "19/KS 11", string inv = "INV-1")
    {
        var p = new Nk.Phieu { File = "a.pdf", So = so, Date = "2026-11-19", Country = "Ấn Độ", Invoice = inv, Contract = "TP-143/24-MAA", Tp = "143" };
        p.Mau.Add(M(1, "Tôm HLSO 31/35 nhập khẩu")); p.Mau.Add(M(2, "Tôm HLSO 31/35 nhập khẩu"));
        p.Mau.Add(M(3, "Tôm HLSO 31/40 nhập khẩu")); p.Mau.Add(M(4, "Tôm HLSO 31/40 nhập khẩu"));
        return p;
    }

    [Fact]
    public void Tinh_trang_xanh_vang_do_theo_thong_tin_doc_duoc()
    {
        var p = P(); Nk.Check(p);
        Assert.All(p.BySize(), g => Assert.Equal(0, Nk.Status(p, g.Hang, g.Size, g.Mau).Level));
        Assert.Equal("4/4", Nk.MauDoc(p));
        // thiếu mẫu 3 → cảnh báo vàng, số mẫu đọc 3/4
        p.Mau.RemoveAll(m => m.Stt == 3); p.Mau.Add(M(5, "Tôm HLSO 31/40 nhập khẩu")); Nk.Check(p);
        Assert.Contains(p.Issues, i => i.Contains("Thiếu mẫu số"));
        var g40 = p.BySize().First(g => g.Size == "31/40");
        Assert.Equal(1, Nk.Status(p, g40.Hang, g40.Size, g40.Mau).Level);
        // thiếu INVOICE → đỏ, sửa xong hết đỏ
        p.Invoice = ""; Nk.Check(p);
        Assert.All(p.BySize(), g => Assert.Equal(2, Nk.Status(p, g.Hang, g.Size, g.Mau).Level));
        p.Invoice = "INV-2"; Nk.Check(p);
        Assert.All(p.BySize(), g => Assert.NotEqual(2, Nk.Status(p, g.Hang, g.Size, g.Mau).Level));
    }

    [Fact]
    public void Sua_Size_tach_nhom_lai_va_ngay_go_tay_duoc_hieu()
    {
        var p = P();
        var g = p.BySize().First(x => x.Size == "31/40");
        Nk.SetHangSize(g.Mau, "Tôm HLSO", "41/50");
        Assert.Equal(new[] { "31/35", "41/50" }, p.BySize().Select(x => x.Size).OrderBy(x => x));
        Assert.Equal("2026-11-25", Nk.ParseDate("25.11.2026")); Assert.Equal("2026-11-05", Nk.ParseDate("5/11/2026"));
        Assert.Equal("", Nk.ParseDate("")); Assert.Null(Nk.ParseDate("abc"));
    }

    [Fact]
    public void Clone_doc_lap_va_chi_nap_cac_Size_da_duyet_ghi_dau_sua_tay()
    {
        var ks = _s.KhangSinh; ks.Load();
        var goc = P(); var p = goc.Clone();
        p.Invoice = "INV-SUA"; p.Edits.Add("INVOICE: INV-1 → INV-SUA");
        Assert.Equal("INV-1", goc.Invoice);
        var chi35 = p.Clone(m => HangSizeOf(m) == "31/35");
        var r = ks.ImportNhapKhau(new[] { chi35 });
        Assert.Equal(1, r.Added); Assert.Single(ks.NhapKhau);
        Assert.Equal("INV-SUA", ks.NhapKhau[0].Invoice);
        Assert.Contains("INVOICE", ks.NhapKhau[0].J["suaTay"]?.ToString() ?? "");
    }
    private static string HangSizeOf(Nk.Mau m) => Nk.HangSize(m.Ten).Size;

    [Fact]
    public void Hoan_tac_lan_nap_xoa_dong_moi_va_tra_lai_ban_cu_bi_thay()
    {
        var ks = _s.KhangSinh; ks.Load();
        var cu = P(so: "11/KS 11"); ks.ImportNhapKhau(new[] { cu });
        Assert.Equal(2, ks.NhapKhau.Count);
        var moi = P(so: "19/KS 11"); moi.Mau[0].Inputs["doxy"] = "7,52";
        var r = ks.ImportNhapKhau(new[] { moi });
        Assert.Equal(2, r.Replaced); Assert.All(ks.NhapKhau, n => Assert.Equal("19/KS 11", n.So)); Assert.Equal(2, ks.NhapKhauCu.Count);
        Assert.Equal(2, ks.UndoNhapKhau(r));
        Assert.Equal(2, ks.NhapKhau.Count); Assert.All(ks.NhapKhau, n => Assert.Equal("11/KS 11", n.So));
        Assert.Empty(ks.NhapKhauCu);
        // nạp mới hoàn toàn rồi hoàn tác → hết
        var lai = ks.ImportNhapKhau(new[] { P(so: "20/KS 11", inv: "INV-9") });
        Assert.Equal(2, lai.Added); Assert.Equal(4, ks.NhapKhau.Count);
        ks.UndoNhapKhau(lai); Assert.Equal(2, ks.NhapKhau.Count);
        var re = new KhangSinhService(() => _s.Lan, _s.Session, _s.Audit); re.Load();
        Assert.Equal(2, re.NhapKhau.Count);
    }
}

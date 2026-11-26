using KhoLanh.Core.KiemKe;
using KhoLanh.Core.SoDo;
using Xunit;
using static KhoLanh.Core.SoDo.SoDoCore;

namespace KhoLanh.Core.Tests;

/// <summary>1.130 (người dùng duyệt "Chỉ ra lệch ở vị trí nào"): mã đang lệch → từng ô vị trí sổ / đếm / chênh + nghi để nhầm chỗ.</summary>
public class LechViTri130Tests
{
    private static Built Map(params (string Vt, string Ma, double Sl)[] stock)
    {
        var cfg = new Cfg { ["TG1"] = new List<KhuCfg> { new() { Khu = "A", D = 3, K = 2, T = 1 } } };
        return Build(stock.Select(s => new StockRow { Vt = s.Vt, Ma = s.Ma, Sl = s.Sl, Tl = 10 }), cfg, _ => null, (_, _) => "");
    }
    private static KiemKeCore.VtCmp Cmp(Built b, params string[][] rows)
    {
        var page = new KiemKeCore.Page
        {
            Id = "p1", Name = "Kho 1 A", Columns = new() { "Vị trí", "Mã hàng", "Size", "Trọng lượng", "Số lượng" },
            Rows = rows.Select(r => r.Concat(Enumerable.Repeat("", 5 - r.Length)).ToArray()).ToList(),
        };
        return KiemKeCore.CompareVt(b, KiemKeCore.ExtractRows(new[] { page }).Rows, _ => null);
    }

    [Fact]
    public void Ma_lech_chi_ra_o_vi_tri_va_nghi_de_nham_cho()
    {
        // sổ: MA1 10 kiện ở A.01.1.1, 5 kiện ở A.02.1.1; ô A.03.1.1 trống
        var b = Map(("TG1.A.01.1.1", "MA1", 10), ("TG1.A.02.1.1", "MA1", 5), ("TG1.A.01.1.2", "MA2", 4));
        // đếm: A.01.1.1 = 10 (khớp), A.02.1.1 = 2 (thiếu 3), A.03.1.1 = 3 (thừa 3 → nghi để nhầm chỗ), MA2 khớp
        var c = Cmp(b, new[] { "TG1.A.01.1.1", "MA1", "", "", "10" }, new[] { "TG1.A.02.1.1", "MA1", "", "", "2" },
                       new[] { "TG1.A.03.1.1", "MA1", "", "", "3" }, new[] { "TG1.A.01.1.2", "MA2", "", "", "4" });
        var l = KiemKeCore.ViTriCuaMa(c, "MA1");
        Assert.Equal(new[] { "TG1.A.02.1.1", "TG1.A.03.1.1", "TG1.A.01.1.1" }, l.Select(x => x.Code));   // lệch lên trước
        Assert.Equal((5, 2, -3), (l[0].Exp, l[0].Cnt, l[0].Diff));
        Assert.Equal((0, 3, 3), (l[1].Exp, l[1].Cnt, l[1].Diff));
        Assert.False(l[2].Lech);
        Assert.Equal(2, l[0].Lines[0].Row);                                     // dòng 2 của trang (1 = dòng đầu)
        var nc = KiemKeCore.NhamCho(l);
        Assert.Single(nc);
        Assert.Equal(("TG1.A.02.1.1", "TG1.A.03.1.1", 3.0), (nc[0].Thieu.Code, nc[0].Thua.Code, nc[0].Kien));
        // mã khớp mọi ô → không có lệch; mã không có ở đâu / chưa có sơ đồ → rỗng
        Assert.DoesNotContain(KiemKeCore.ViTriCuaMa(c, "MA2"), x => x.Lech);
        Assert.Empty(KiemKeCore.ViTriCuaMa(c, "MAX"));
        Assert.Empty(KiemKeCore.ViTriCuaMa(null, "MA1"));
    }

    [Fact]
    public void O_chua_dem_khong_tinh_la_lech()
    {
        // sổ có MA1 ở 2 ô; trang kiểm kê chỉ đếm ô đầu (khớp) → ô kia "chưa đếm", không phải thiếu
        var b = Map(("TG1.A.01.1.1", "MA1", 10), ("TG1.A.03.1.2", "MA1", 6));
        var c = Cmp(b, new[] { "TG1.A.01.1.1", "MA1", "", "", "10" });
        var l = KiemKeCore.ViTriCuaMa(c, "MA1");
        Assert.Equal(new[] { "TG1.A.01.1.1", "TG1.A.03.1.2" }, l.Select(x => x.Code));
        Assert.DoesNotContain(l, x => x.Lech);
        Assert.True(l[1].ChuaDem);
        // ô đã đếm (có mã khác) mà thiếu mã này → vẫn là lệch
        c = Cmp(b, new[] { "TG1.A.01.1.1", "MA1", "", "", "10" }, new[] { "TG1.A.03.1.2", "MA2", "", "", "6" });
        Assert.True(KiemKeCore.ViTriCuaMa(c, "MA1").Single(x => x.Code == "TG1.A.03.1.2").Lech);
    }

    [Fact]
    public void Thieu_khong_cung_so_voi_thua_thi_khong_bao_nham_cho()
    {
        var b = Map(("TG1.A.01.1.1", "MA1", 10));
        var c = Cmp(b, new[] { "TG1.A.01.1.1", "MA1", "", "", "6" }, new[] { "TG1.A.02.1.1", "MA1", "", "", "1" });
        var l = KiemKeCore.ViTriCuaMa(c, "MA1");
        Assert.Equal(2, l.Count(x => x.Lech));
        Assert.Empty(KiemKeCore.NhamCho(l));
    }
}

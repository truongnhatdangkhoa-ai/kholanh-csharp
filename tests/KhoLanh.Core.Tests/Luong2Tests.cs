using KhoLanh.Core.DanhMuc;
using KhoLanh.Core.KiemKe;
using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.SoDo;
using Xunit;
using static KhoLanh.Core.SoDo.SoDoCore;

namespace KhoLanh.Core.Tests;

/// <summary>1.72 (luồng 2): trạng thái từng ô kiểm kê trên sơ đồ · gộp tồn Cargo theo size + đặc tính · dấu phiên bản số "Cần xử lý" Danh mục.</summary>
public class Luong2Tests
{
    // ------------------------------------------------------------------ B1 · Kiểm kê ↔ Sơ đồ kho

    /// <summary>TG1 khu A: 2 dãy × 2 khoang × 1 tầng.</summary>
    private static Built Map(params (string Vt, string Ma, double Sl)[] stock)
    {
        var cfg = new Cfg { ["TG1"] = new List<KhuCfg> { new() { Khu = "A", D = 2, K = 2, T = 1 } } };
        return Build(stock.Select(s => new StockRow { Vt = s.Vt, Ma = s.Ma, Sl = s.Sl, Tl = 10 }), cfg, _ => null, (_, _) => "");
    }
    private static KiemKeCore.Page Page(params string[][] rows) => new()
    {
        Id = "p1", Name = "Kho 1 A dãy 01",
        Columns = new() { "Vị trí", "Mã hàng", "Size", "Trọng lượng", "Số lượng", "Net(kg)", "Ghi chú", "Ngày nhập" },
        Rows = rows.Select(r => r.Concat(Enumerable.Repeat("", 8 - r.Length)).ToArray()).ToList(),
    };

    [Fact]
    public void Kiem_ke_tung_o_khop_lech_thieu_thua_chua_dem()
    {
        var b = Map(("TG1.A.01.1.1", "MA1", 10), ("TG1.A.01.1.2", "MA2", 5), ("TG1.A.02.1.1", "MA3", 7));
        // trang chỉ có dãy 01: ô 1.1 đếm 10 (khớp), ô 1.2 đếm 3 (lệch)
        var m = KiemKeCore.MapPage(b, Page(new[] { "TG1.A.01.1.1", "MA1", "", "", "10" }, new[] { "TG1.A.01.1.2", "MA2", "", "", "3" }));
        Assert.Equal(KiemKeCore.KkKhop, m.St["TG1.A.01.1.1"]);
        Assert.Equal(KiemKeCore.KkLech, m.St["TG1.A.01.1.2"]);
        Assert.Equal(KiemKeCore.KkChuaDem, m.St["TG1.A.02.1.1"]);   // dãy 02 không có trong trang
        Assert.Equal(KiemKeCore.KkChuaDem, m.St["TG1.A.02.1.2"]);
        Assert.Equal(("TG1", "A", 1, true), (m.Kho, m.Khu, m.D, m.OneKhu));
        Assert.Equal(0, m.RowOf["TG1.A.01.1.1"]); Assert.Equal(1, m.RowOf["TG1.A.01.1.2"]);

        // ô có hàng sổ sách, trang ghi 0 → thiếu; ô sổ trống mà đếm có → thừa; ô có trên trang nhưng không ghi số → thiếu
        m = KiemKeCore.MapPage(b, Page(
            new[] { "TG1.A.01.1.1", "MA1", "", "", "0" },
            new[] { "TG1.A.02.1.2", "MA9", "", "", "4" },
            new[] { "TG1.A.02.1.1", "MA3", "", "", "" }));
        Assert.Equal(KiemKeCore.KkThieu, m.St["TG1.A.01.1.1"]);
        Assert.Equal(KiemKeCore.KkThieu, m.St["TG1.A.01.1.2"]);      // dãy 01 có trong trang, ô 1.2 không có dòng → sổ có, đếm không
        Assert.Equal(KiemKeCore.KkThua, m.St["TG1.A.02.1.2"]);
        Assert.Equal(KiemKeCore.KkThieu, m.St["TG1.A.02.1.1"]);
        Assert.Equal(1, m.N[KiemKeCore.KkThua]); Assert.Equal(3, m.N[KiemKeCore.KkThieu]);
        Assert.False(m.Days.Count == 0);
    }

    [Fact]
    public void Kiem_ke_o_trong_dung_va_so_luong_con_lai()
    {
        var b = Map(("TG1.A.01.1.1", "MA1", 10));
        // trang có cột "Số lượng còn lại" (mẫu 12 cột): đối chiếu theo số còn lại
        var p = new KiemKeCore.Page
        {
            Id = "p2", Name = "x",
            Columns = new() { "Vị trí", "Mã hàng", "Số lượng", "SL xuất", "Số lượng còn lại" },
            Rows = new() { new[] { "TG1.A.01.1.1", "MA1", "12", "2", "10" }, new[] { "TG1.A.01.1.2", "", "", "", "" } },
        };
        var m = KiemKeCore.MapPage(b, p);
        Assert.Equal(KiemKeCore.KkKhop, m.St["TG1.A.01.1.1"]);
        Assert.Equal(KiemKeCore.KkKhop, m.St["TG1.A.01.1.2"]);       // ô trống, đếm trống = khớp (trống đúng)
        Assert.Equal(1, m.Empty);
    }

    // ------------------------------------------------------------------ B2 · Cargo › Chọn từ tồn

    private static CargoCfg Cfg() => CargoCore.NormCfg(CargoCore.DefaultCfg());
    private static KiemKeCore.Stock St(string ma, double ton, string dt = "", double? tl = 10.8) => new() { Ma = ma, Key = KiemKeCore.NormKey(ma), Ton = ton, Dt = dt, Tl = tl };

    [Fact]
    public void Cargo_ton_gop_theo_size_dac_tinh_khong_tach_lo()
    {
        var cfg = Cfg();
        // BTP size 40, quy cách 6 (IME1), KQ 1 — 2 dòng tồn khác hợp đồng / lô nhưng cùng size + đặc tính → 1 nhóm
        var a = "VRHLBTNXXXR00" + "0040" + "IME1" + "0001";
        var c = "VRHLBTNXXXR00" + "0040" + "IME1" + "0002";      // khác đặc tính
        var d = "VRHOBTNXXXR00" + "0130" + "JPA1" + "0001";      // NL 130, 8Kg x 1
        var stock = new[] { St(a, 30), St(a, 20), St(c, 5), St(d, 12), St("VRHLBTNXXXR00" + "0040" + "IME1" + "0003", 0) };
        var rows = CargoStock.BySize(stock, cfg, null, null);
        Assert.Equal(3, rows.Count);                              // tồn 0 bị bỏ
        var g = rows.Single(r => r.Size == "BTP40" && r.KsStt == 1);
        Assert.Equal((50.0, "1.8Kg x 6", "BTP", "0040-6"), (g.Ton, g.Pack, g.Loai, g.Sn));
        var nl = rows.Single(r => r.Loai == "NL");
        Assert.Equal(("NL130", "8Kg x 1", "0130-8"), (nl.Size, nl.Pack, nl.Sn));

        // cont đã có 18 kiện BTP40 1.8Kg x 6 KQ 1 (gõ KQ tay) → còn lại 32
        var rec = new CargoRec { Blocks = new() { new CargoBlock { Loai = "ks", Lines = new() { new CargoLine { Loai = "BTP", Sn = "0040-6", KsTay = "1", Dai = new() { ["1"] = "18" } } } } } };
        rows = CargoStock.BySize(stock, cfg, rec, null);
        g = rows.Single(r => r.Size == "BTP40" && r.KsStt == 1);
        Assert.Equal((18.0, 32.0), (g.Have, g.Left));
        Assert.Equal(0, rows.Single(r => r.KsStt == 2).Have);

        // dòng mới từ nhóm: Loại + Size-quy cách + KQ tay, lô trống → ra đúng mã của tồn
        var (ln, ok) = CargoStock.LineFor(g, ksBlock: true);
        Assert.True(ok);
        Assert.Equal(("BTP", "0040-6", "1", ""), (ln.Loai, ln.Sn, ln.KsTay, ln.Lo));
        var res = CargoCore.ResolveLine(cfg, rec.Blocks[0], ln, null);
        Assert.Equal(a, res.Ma);
        // mục "khác": Size + Quy cách
        var (k2, _) = CargoStock.LineFor(g, ksBlock: false);
        Assert.Equal(("BTP40", "1.8Kg x 6"), (k2.Size, k2.Pack));
    }

    [Fact]
    public void Cargo_ton_quy_cach_la_khong_suy_duoc_size_quy_cach()
    {
        var cfg = Cfg();
        var x = "VRHLBTNXXXR00" + "0040" + "ZZZ9" + "0001";      // mã gói không có trong danh mục
        var rows = CargoStock.BySize(new[] { St(x, 7, tl: 9.5) }, cfg, null, null);
        var r = Assert.Single(rows);
        Assert.Equal("", r.Sn);
        var (ln, ok) = CargoStock.LineFor(r, ksBlock: true);
        Assert.False(ok); Assert.Equal("", ln.Sn);
        var (k, _) = CargoStock.LineFor(r, ksBlock: false);
        Assert.Equal("9.5 kg x 1", k.Pack);                      // mục khác: quy cách theo trọng lượng
        Assert.NotNull(CargoCore.KgPerCarton(k.Pack));
    }

    // ------------------------------------------------------------------ B4 · số "Cần xử lý" Danh mục mã hàng

    [Fact]
    public void Dau_phien_ban_danh_muc_chi_theo_bang_lien_quan()
    {
        var v1 = new List<(string, string, long)> { ("tong-hop", "MaDaTao", 3), ("ton-kho-an-an", "LoadData", 5), ("khac", "X", 9) };
        var f1 = DanhMucService.Fingerprint(v1);
        Assert.Equal(f1, DanhMucService.Fingerprint(v1.Select(x => x.Item1 == "khac" ? (x.Item1, x.Item2, 99L) : x)));   // bảng khác đổi → không tính lại
        Assert.NotEqual(f1, DanhMucService.Fingerprint(v1.Select(x => x.Item2 == "LoadData" ? (x.Item1, x.Item2, 6L) : x))); // nạp tồn mới → tính lại
        Assert.Contains(DanhMucService.WatchedSheets, w => w == ("tong-hop", "MaHoa"));
    }
}

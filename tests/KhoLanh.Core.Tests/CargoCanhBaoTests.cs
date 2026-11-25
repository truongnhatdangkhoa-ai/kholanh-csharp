using KhoLanh.Core.KiemKe;
using KhoLanh.Core.PhieuKho;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.88: cảnh báo từng dòng Cargo khi gõ Size-quy cách (chỉ cảnh báo, không chặn lưu).</summary>
public class CargoCanhBaoTests
{
    private static CargoCfg Cfg() => CargoCore.NormCfg(CargoCore.DefaultCfg());
    private const string A1 = "VRHLBTNXXXR00" + "0040" + "IME1" + "0001";   // BTP40 1.8Kg x 6 KQ 1
    private const string A2 = "VRHLBTNXXXR00" + "0040" + "IME1" + "0002";   // khác đặc tính
    private static List<CargoStock.Item> Stock(params (string Ma, double Ton)[] xs) =>
        CargoStock.Aggregate(xs.Select(x => new KiemKeCore.Stock { Ma = x.Ma, Key = KiemKeCore.NormKey(x.Ma), Ton = x.Ton }));
    private static CargoRec Rec(params CargoLine[] lines) => new() { Blocks = new() { new CargoBlock { Loai = "ks", Lines = lines.ToList() } } };

    [Fact]
    public void Size_quy_cach_sai_la_canh_bao_do()
    {
        var w = CargoStock.LineWarnings(Cfg(), Rec(new CargoLine { Loai = "BTP", Sn = "abc" }), null, Stock((A1, 10)));
        Assert.Single(w); Assert.True(w[0].Bad); Assert.Equal((0, 0), (w[0].Block, w[0].Line));
        // dòng có kiện: lỗi đã vào "Cần xử lý trước khi xuất" (CheckRec) → không lặp lại
        w = CargoStock.LineWarnings(Cfg(), Rec(new CargoLine { Loai = "BTP", Sn = "abc", Dai = new() { ["1"] = "5" } }), null, Stock((A1, 10)));
        Assert.DoesNotContain(w, x => x.Bad);
    }

    [Fact]
    public void Ma_khong_co_trong_ton_het_hang_va_goi_y()
    {
        var cfg = Cfg();
        // size 50 không có trong tồn → gợi ý BTP40 cùng quy cách
        var w = CargoStock.LineWarnings(cfg, Rec(new CargoLine { Loai = "BTP", Sn = "0050-6" }), null, Stock((A1, 10)));
        Assert.Single(w); Assert.False(w[0].Bad);
        Assert.Contains("không có trong Tồn kho An An", w[0].Text); Assert.Contains("BTP40", w[0].Text);
        // có mã nhưng tồn 0
        w = CargoStock.LineWarnings(cfg, Rec(new CargoLine { Loai = "BTP", Sn = "0040-6" }), null, Stock((A1, 0)));
        Assert.Contains("hết hàng", Assert.Single(w).Text);
        // đủ tồn → không cảnh báo; chưa nạp tồn → không dò tồn
        Assert.Empty(CargoStock.LineWarnings(cfg, Rec(new CargoLine { Loai = "BTP", Sn = "0040-6", Dai = new() { ["1"] = "8" } }), null, Stock((A1, 10))));
        Assert.Empty(CargoStock.LineWarnings(cfg, Rec(new CargoLine { Loai = "BTP", Sn = "0050-6" }), null, new List<CargoStock.Item>()));
    }

    [Fact]
    public void Dac_tinh_khong_con_hang_va_vuot_ton()
    {
        var cfg = Cfg();
        // KQ 2 hết, KQ 1 còn → báo đặc tính không còn, kèm đặc tính đang còn
        var w = CargoStock.LineWarnings(cfg, Rec(new CargoLine { Loai = "BTP", Sn = "0040-6", KsTay = "2" }), null, Stock((A1, 10), (A2, 0)));
        Assert.Contains("không còn hàng", Assert.Single(w).Text);
        // 2 dòng cùng mã cộng 12 > tồn 10 → cả 2 dòng cảnh báo vượt tồn
        w = CargoStock.LineWarnings(cfg, Rec(
            new CargoLine { Loai = "BTP", Sn = "0040-6", KsTay = "1", Dai = new() { ["1"] = "7" } },
            new CargoLine { Loai = "BTP", Sn = "0040-6", KsTay = "1", Dai = new() { ["2"] = "5" } }), null, Stock((A1, 10)));
        Assert.Equal(2, w.Count); Assert.All(w, x => Assert.Contains("vượt tồn", x.Text));
        // chưa có KQ (chưa gõ lô): so tổng size + quy cách mọi đặc tính (10 + 3)
        w = CargoStock.LineWarnings(cfg, Rec(new CargoLine { Loai = "BTP", Sn = "0040-6", Dai = new() { ["1"] = "14" } }), null, Stock((A1, 10), (A2, 3)));
        Assert.Contains("còn 13", Assert.Single(w).Text);
        Assert.Empty(CargoStock.LineWarnings(cfg, Rec(new CargoLine { Loai = "BTP", Sn = "0040-6", Dai = new() { ["1"] = "13" } }), null, Stock((A1, 10), (A2, 3))));
    }
}

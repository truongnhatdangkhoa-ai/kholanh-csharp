using KhoLanh.Core.PhieuKho;
using Xunit;

namespace KhoLanh.Core.Tests;

public class CargoStockTests
{
    // 25 ký tự: 13 (loại) + 4 (size) + 4 (quy cách) + 4 (đặc tính)
    private const string Base = "ABCDEFGHIJKLM" + "0040" + "0006" + "0001";
    private static CargoStock.Item It(string ma, double ton) => new(ma, ton, "", "");

    [Fact]
    public void Check_ok_over_zero_none()
    {
        var stock = new List<CargoStock.Item> { It(Base, 10), It("ABCDEFGHIJKLM" + "0040" + "0006" + "0002", 5), It("ABCDEFGHIJKLM" + "0050" + "0006" + "0001", 0) };
        var sums = new Dictionary<string, double> { [Base] = 8 };
        Assert.Equal("ok", CargoStock.Check(stock, sums, Base)!.St);
        sums[Base] = 11;
        Assert.Equal("over", CargoStock.Check(stock, sums, Base)!.St);
        Assert.Equal("zero", CargoStock.Check(stock, sums, "ABCDEFGHIJKLM" + "0050" + "0006" + "0001")!.St);
        var none = CargoStock.Check(stock, sums, "ABCDEFGHIJKLM" + "0040" + "0009" + "0001")!;   // chỉ khác quy cách → gợi ý Base
        Assert.Equal("none", none.St);
        Assert.Contains(none.Similar, s => s.Ma == Base && s.Diff.Contains("Net / quy cách"));
    }

    [Fact]
    public void Similar_prefers_one_part_difference()
    {
        var stock = new List<CargoStock.Item> { It(Base, 3), It("XBCDEFGHIJKLM" + "0050" + "0006" + "0001", 9) };   // 2 phần khác nhau
        var s = CargoStock.SimilarMa("ABCDEFGHIJKLM" + "0040" + "0006" + "0002", stock);
        Assert.Single(s); Assert.Equal(Base, s[0].Ma);
    }
}

public class CargoSuggestTests
{
    [Fact]
    public void Suggest_pack_and_prefix()
    {
        var d = new CargoSuggest.Dict();
        d.W["IM"] = "1.8 Kg/túi"; d.Q["E3"] = "3 túi/ctn";
        var cfg = new CargoCfg { Loai = new() { new CargoLoai { Code = "BTP", Ten = "HLSO" } }, Qc = new() { new CargoQc { D = "6", Label = "1.8Kg x 6", Pack = "IME6", Pre = new() { ["BTP"] = "PRHL0000000AB" } } } };
        var ma = new[] { "PRHL0000000AB" + "0040" + "IME3" + "0" };   // chưa đủ 21 → bỏ qua
        var sg = CargoSuggest.Suggest("1.8Kg x 3", d, cfg, ma, "3");
        Assert.Equal("IME3", sg.Pack);
        Assert.Equal("PRHL0000000AB", sg.Pre["BTP"]);          // chép theo quy cách cùng khối lượng
        Assert.Null(CargoSuggest.ParseLabel("abc"));
    }
}

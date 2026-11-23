using KhoLanh.Core.KiemKe;
using Xunit;

namespace KhoLanh.Core.Tests;

public class KhoGuiTests
{
    // mã kho gửi 19 ký tự = 7 ký tự đầu + size(4) + trọng lượng(2) + quy cách(2) + đuôi riêng (4)
    private const string G = "VRHLBTN" + "0040" + "IM" + "E1" + "0046";

    [Theory]
    [InlineData("L1", "BTP40", "VRHLBTNXXXR000040IME1", "BTP thường")]
    [InlineData("TGS-9", "TGS 40", "VRHLBTNXXXTGS0040IME1", "Tôm Giám Sát")]
    [InlineData("TGS TGC-2", "40", "VRHLBTNXTCTGS0040IME1", "Giám Sát + Gia Công")]
    [InlineData("AD 7", "40", "VRHLBTNXAIR000040IME1", "Tôm Ấn Độ")]
    [InlineData("ECUADOR-01", "40", "VRHLBTNXECR000040IME1", "Tôm Ecuador")]
    [InlineData("TGC-TQC", "40", "VRHLBTNXTCR000040IME1", "Tôm Gia Công")]
    public void Auto_ma21_rules(string lo, string size, string want, string rule)
    {
        var a = KhoGui.AutoMa21(G, lo, size)!.Value;
        Assert.Equal(want, a.Ma); Assert.Equal(rule, a.Rule); Assert.Equal(21, a.Ma.Length);
    }

    [Fact]
    public void Conflict_and_bad_length_are_not_guessed()
    {
        Assert.Contains("Xung đột", KhoGui.AutoMa21(G, "TGS AD", "40")!.Value.Why);
        Assert.Contains("ký tự", KhoGui.AutoMa21("ABC123", "", "")!.Value.Why);
        Assert.Null(KhoGui.AutoMa21("VRHLBTNXXXR000040IME1", "", ""));                // đã là mã 21
    }

    [Fact]
    public void Map_table_wins_and_totals_add_to_ke_toan()
    {
        var rows = new List<KhoGui.Line>
        {
            new() { Ma = G, Lo = "L1", Size = "40", Kg = 100, Kien = 10 },
            new() { Ma = "VRHLBTN0050IME10023", Lo = "L2", Size = "50", Kg = 50, Kien = 5 },
            new() { Ma = "XYZ", Kg = 7 },                                                   // không đổi được → không tính
        };
        var map = new[] { ("VRHLBTN0050IME10023", "VRHLBTNXXXR000050IME1") };
        var r = KhoGui.Build(rows, map);
        Assert.Equal((1, 1, 1), (r.NTay, r.NAuto, r.Bad.Count));
        Assert.Equal(150, r.KgAll); Assert.Equal(7, r.KgBad);
        Assert.Equal(100, r.Map[KiemKeCore.NormKey("VRHLBTNXXXR000040IME1")].Kg);
        // đối chiếu kế toán: mã A có kiểm kê 60 kg + gửi 100 kg = 160 = kế toán; mã B chỉ ở kho gửi
        var lines = new List<KiemKeCore.Line> { new() { Ma = "VRHLBTNXXXR000040IME1", Key = KiemKeCore.NormKey("VRHLBTNXXXR000040IME1"), Kien = 6, Kg = 60 } };
        var kt = KiemKeCore.MakeKeToan(new[] { new KiemKeCore.KtRow("VRHLBTNXXXR000040IME1", "", "", "", "160"), new KiemKeCore.KtRow("VRHLBTNXXXR000050IME1", "", "", "", "50") });
        var t = KiemKeCore.BuildTotals(lines, new List<KiemKeCore.Stock>(), kt, null, r.Map);
        var a = t.Rows.First(x => x.Ma == "VRHLBTNXXXR000040IME1");
        Assert.Equal((60.0, 100.0, 160.0, "Khớp"), (a.ChotKg!.Value, a.GuiKg!.Value, a.TongKg!.Value, a.KtStatus));
        var b = t.Rows.First(x => x.Ma == "VRHLBTNXXXR000050IME1");
        Assert.Equal(KiemKeCore.ChiGui, b.Status); Assert.Equal("Khớp", b.KtStatus);
        Assert.Equal(0, KiemKeCore.Summarize(t.Rows).ChiKK + KiemKeCore.Summarize(t.Rows).ChuaKK);   // mã chỉ ở kho gửi không vào đối chiếu An An
    }
}

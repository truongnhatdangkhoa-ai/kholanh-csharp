using KhoLanh.Core.Entry;
using KhoLanh.Core.TimNhanh;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Gợi ý mã hàng ở cột Mã hàng của Nhập liệu: gộp tồn mọi module + danh mục, tìm không dấu, xếp mã đầu khớp / còn tồn trước.</summary>
public class MaGoiYTests
{
    [Fact]
    public void Gop_ton_va_danh_muc_roi_tim()
    {
        var stock = new[]
        {
            new GsRow { Src = "M01VT", Ma = "VRHLBTNXECR001620IMA1", Ten = "Tôm PD BTP (2 kg x 5 gói)", Size = "16/20", Dt = "Semi", Ton = 8 },
            new GsRow { Src = "M01", Ma = "TPC-VRHLBTNXECR001620IMA1", Ten = "", Size = "16/20", Ton = 12 },
            new GsRow { Src = "M02", Ma = "VRHOITNXXXR000170JPA1", Ten = "Tôm HOSO 170", Size = "170", Dt = "XANH", Ton = 50 },
        };
        var cat = new[] { ("VRHLBTNXECR001620IMA1", "BTP 16/20 Semi"), ("VRHLBTNXXXR009990IMB1", "Mã mới chưa có tồn") };
        var all = MaGoiY.Build(stock, cat);
        Assert.Equal(3, all.Count);
        var a = all.Single(x => x.Ma == "VRHLBTNXECR001620IMA1");
        Assert.Equal(20, a.Ton); Assert.True(a.TrongDanhMuc); Assert.Equal("16/20", a.Size); Assert.Contains("Tồn", a.Nguon);
        Assert.Equal("Mã mới chưa có tồn", all.Single(x => x.Ma.EndsWith("009990IMB1")).Ten);

        Assert.Empty(MaGoiY.Search(all, "v"));                                  // < 2 ký tự
        Assert.Equal(2, MaGoiY.Search(all, "VR").Count);                        // 1.89: mã chỉ có trong danh mục không gợi ý
        var hoso = MaGoiY.Search(all, "hoso 170");                              // tên + size, không dấu
        Assert.Equal("VRHOITNXXXR000170JPA1", Assert.Single(hoso).Ma);
        Assert.Single(MaGoiY.Search(all, "tôm pd semi"));
        var vrhl = MaGoiY.Search(all, "vrhlbtnx");
        Assert.Equal("VRHLBTNXECR001620IMA1", Assert.Single(vrhl).Ma);          // mã danh mục 009990 không vào gợi ý
        Assert.Contains("VRHLBTNXXXR009990IMB1", MaGoiY.Known(all));             // …nhưng vẫn là mã đã biết (không tô mã lạ)
        Assert.Equal("Mã mới chưa có tồn", MaGoiY.Lookup(all)["VRHLBTNXXXR009990IMB1"].Ten);   // …và vẫn tự điền được
        Assert.Contains("VRHOITNXXXR000170JPA1", MaGoiY.Known(all));
        }

    [Fact]
    public void Kiem_ke_go_4_so_size_chi_ra_ma_ton_an_an_dung_size()
    {
        // Tồn kho An An (mọi dòng còn tồn) — 0050 phải khớp đoạn Size, không kèm 005060 / mã danh mục chứa "0050"
        var anAn = new[]
        {
            new GsRow { Src = "M01", Ma = "VRHLITNXXXR000050JRA1", Ten = "Tôm TCT vỏ (An An)", Size = "50", Dt = "12.N-EU-H", Ton = 24, Kg = 216 },
            new GsRow { Src = "M01", Ma = "VRHLITNXXXR000050JRA1", Ten = "Tôm TCT vỏ (An An)", Size = "50", Dt = "46.EU-H", Ton = 107 },
            new GsRow { Src = "M01", Ma = "VRHLBTNXXXR000050IMD1", Ten = "Tôm BTP", Size = "50", Dt = "03.N-EU-H", Ton = 3 },
            new GsRow { Src = "M01", Ma = "PBXPICYESGR005060JYB1", Ten = "Tôm PD 50/60", Size = "50/60", Ton = 9 },
        };
        var cat = new[] { ("PRHLBTNXTPR000050IME1", "Tôm TCT vỏ, Block, TPC, 50"), ("PCTOICYUBER005060LDD1", "PTO 50/60") };
        var all = MaGoiY.Build(anAn, cat);

        var hit = MaGoiY.Search(all, "0050");
        Assert.Equal(new[] { "VRHLBTNXXXR000050IMD1", "VRHLITNXXXR000050JRA1" }, hit.Select(x => x.Ma).OrderBy(x => x).ToArray());
        var a = hit.Single(x => x.Ma == "VRHLITNXXXR000050JRA1");
        Assert.Equal(131, a.Ton); Assert.Equal("12.N-EU-H, 46.EU-H", a.Dt); Assert.Equal("Tồn kho An An", a.Nguon);
        Assert.Equal("9", a.Tl);                                                 // 216 kg / 24 thùng
        Assert.Equal("0050", MaGoiY.SizeSeg("VRHLITNXXXR000050JRA1"));

        // không có size đúng 4 số → tìm như thường (chứa chuỗi)
        Assert.Equal("PBXPICYESGR005060JYB1", Assert.Single(MaGoiY.Search(all, "5060")).Ma);
        // mã chỉ có trong danh mục: không gợi ý, vẫn tự điền / không lạ
        Assert.Empty(MaGoiY.Search(all, "PRHL"));
        Assert.Contains("PRHLBTNXTPR000050IME1", MaGoiY.Known(all));
        Assert.Equal("PTO 50/60", MaGoiY.Lookup(all)["PCTOICYUBER005060LDD1"].Ten);
    }
}

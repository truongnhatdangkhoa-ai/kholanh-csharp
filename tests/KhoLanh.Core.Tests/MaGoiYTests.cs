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
        Assert.Equal(3, MaGoiY.Search(all, "VR").Count);
        var hoso = MaGoiY.Search(all, "hoso 170");                              // tên + size, không dấu
        Assert.Equal("VRHOITNXXXR000170JPA1", Assert.Single(hoso).Ma);
        Assert.Single(MaGoiY.Search(all, "tôm pd semi"));
        var vrhl = MaGoiY.Search(all, "vrhlbtnx");
        Assert.Equal("VRHLBTNXECR001620IMA1", vrhl[0].Ma);                       // còn tồn xếp trước mã chỉ có trong danh mục
        Assert.Contains("VRHOITNXXXR000170JPA1", MaGoiY.Known(all));
    }
}

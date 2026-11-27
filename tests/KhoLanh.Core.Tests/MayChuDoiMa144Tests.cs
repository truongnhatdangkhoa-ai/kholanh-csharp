using KhoLanh.Core.Lan;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.144: cài đặt bị xóa → mã máy sinh lại; máy chủ vẫn nhận ra mình theo tên máy ghi trên Sheet.</summary>
public class MayChuDoiMa144Tests
{
    private static LanPlan Plan()
    {
        var p = new LanPlan { Key = "k", Port = 47820 };
        p.Chu = new LanPlan.Slot("MA-CU", "KHOLANH-PC", new() { "192.168.1.126:47820" }, "");
        return p;
    }

    [Fact]
    public void May_chu_doi_ma_van_la_may_chu_theo_ten()
    {
        var d = Plan().Decide("admin", "MA-MOI", "", "kholanh-pc")!;
        Assert.Equal(LanService.RoleServer, d.Role);
        Assert.Equal(LanService.RoleClient, Plan().Decide("admin", "MA-MOI", "", "MAY-KHAC")!.Role);   // máy khác tên vẫn là máy con
    }

    [Fact]
    public void May_chu_doi_ma_ghi_lai_ma_moi_len_ke_hoach()
    {
        var p = Plan();
        Assert.True(p.Fix("MA-MOI", "KHOLANH-PC", LanService.RoleServer, "admin", new() { "192.168.1.126:47820" }, "k", 47820));
        Assert.Equal("MA-MOI", p.Chu!.Id);
        var q = Plan();   // máy khác tên không giành được vai
        Assert.False(q.Fix("MA-X", "MAY-KHAC", LanService.RoleServer, "admin", new() { "1.1.1.1:1" }, "k", 47820));
        Assert.Equal("MA-CU", q.Chu!.Id);
    }
}

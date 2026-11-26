using KhoLanh.Core.HenGio;
using KhoLanh.Core.Models;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Hẹn giờ tắt máy: tính giờ (phút / giờ đồng hồ), lệnh hệ điều hành, quyền và lưu thời điểm hẹn.</summary>
public class HenGioTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 21, 10, 30);

    [Fact]
    public void Theo_phut_cong_dung_so_phut_va_chan_ngoai_khoang()
    {
        var r = HenGioCore.TheoPhut(Now, 45)!.Value;
        Assert.Equal(new DateTime(2026, 11, 26, 21, 55, 30), r.Luc); Assert.Equal(2700, r.Giay);
        Assert.Null(HenGioCore.TheoPhut(Now, 0)); Assert.Null(HenGioCore.TheoPhut(Now, -5));
        Assert.NotNull(HenGioCore.TheoPhut(Now, HenGioCore.MaxPhut)); Assert.Null(HenGioCore.TheoPhut(Now, HenGioCore.MaxPhut + 1));
    }

    [Fact]
    public void Theo_gio_dong_ho_hom_nay_hoac_ngay_mai()
    {
        var a = HenGioCore.TheoGio(Now, 22, 30)!.Value;                       // còn ở phía trước hôm nay
        Assert.Equal(new DateTime(2026, 11, 26, 22, 30, 0), a.Luc); Assert.Equal(79 * 60 + 30, a.Giay);   // 21:10:30 → 22:30:00 = 1 giờ 19 phút 30 giây
        var b = HenGioCore.TheoGio(Now, 6, 0)!.Value;                         // đã qua → ngày mai
        Assert.Equal(new DateTime(2026, 11, 27, 6, 0, 0), b.Luc);
        var c = HenGioCore.TheoGio(Now, 21, 10)!.Value;                       // trùng phút hiện tại (đã qua giây 0) → ngày mai
        Assert.Equal(new DateTime(2026, 11, 27, 21, 10, 0), c.Luc);
        Assert.Null(HenGioCore.TheoGio(Now, 24, 0)); Assert.Null(HenGioCore.TheoGio(Now, 10, 60));
        // qua nửa đêm / qua ngày cuối tháng
        var d = HenGioCore.TheoGio(new DateTime(2026, 11, 30, 23, 50, 0), 0, 5)!.Value;
        Assert.Equal(new DateTime(2026, 12, 1, 0, 5, 0), d.Luc); Assert.Equal(15 * 60, d.Giay);
    }

    [Theory]
    [InlineData("22:30", true, 22, 30)] [InlineData("22h30", true, 22, 30)] [InlineData("22.30", true, 22, 30)] [InlineData("2230", true, 22, 30)]
    [InlineData("930", true, 9, 30)] [InlineData("7", true, 7, 0)] [InlineData(" 07:05 ", true, 7, 5)] [InlineData("22h", true, 22, 0)]
    [InlineData("24:00", false, 0, 0)] [InlineData("12:60", false, 0, 0)] [InlineData("abc", false, 0, 0)] [InlineData("", false, 0, 0)] [InlineData("1:2:3", false, 0, 0)] [InlineData("-5", false, 0, 0)]
    public void Doc_gio_nhieu_dang(string text, bool ok, int h, int m)
    {
        Assert.Equal(ok, HenGioCore.TryParseGio(text, out var gh, out var gm));
        if (ok) { Assert.Equal(h, gh); Assert.Equal(m, gm); }
    }

    [Fact]
    public void Lenh_he_dieu_hanh()
    {
        Assert.Equal("shutdown", HenGioCore.Dat(HgOs.Windows, 3600).File);
        Assert.StartsWith("/s /t 3600", HenGioCore.Dat(HgOs.Windows, 3600).Args);
        Assert.Equal("-P +2", HenGioCore.Dat(HgOs.Linux, 61).Args);           // làm tròn lên phút
        Assert.Equal("-h +1", HenGioCore.Dat(HgOs.Mac, 30).Args);
        Assert.Equal("/a", HenGioCore.Huy(HgOs.Windows).Args); Assert.Equal("-c", HenGioCore.Huy(HgOs.Linux).Args);
    }

    [Fact]
    public void Chu_dem_nguoc_va_nhan_gio()
    {
        Assert.Equal("9 giây", HenGioCore.DemNguoc(TimeSpan.FromSeconds(9)));
        Assert.Equal("5 phút 09 giây", HenGioCore.DemNguoc(TimeSpan.FromSeconds(309)));
        Assert.Equal("1 giờ 05 phút 09 giây", HenGioCore.DemNguoc(TimeSpan.FromSeconds(3909)));
        Assert.Equal("1 ngày 00 giờ 00 phút 00 giây", HenGioCore.DemNguoc(TimeSpan.FromDays(1)));
        Assert.Equal("22:30 hôm nay", HenGioCore.NhanLuc(new DateTime(2026, 11, 26, 22, 30, 0), Now));
        Assert.Equal("06:00 ngày mai", HenGioCore.NhanLuc(new DateTime(2026, 11, 27, 6, 0, 0), Now));
        Assert.Equal("06:00 ngày 28/11", HenGioCore.NhanLuc(new DateTime(2026, 11, 28, 6, 0, 0), Now));
    }

    private static (HenGioService Svc, AppServices S, List<HgLenh> Ran, Func<DateTime> Clock, Action<DateTime> SetNow) Make(string vaiTro = Roles.Admin, int code = 0)
    {
        var dir = Path.Combine(Path.GetTempPath(), "kl-hengio-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var s = new AppServices(Path.Combine(dir, "t.db"));
        s.Session.CurrentUser = new User { Username = "tk", Role = vaiTro, IsActive = true };
        var ran = new List<HgLenh>(); var now = Now;
        var svc = new HenGioService(s.Settings, s.Session, s.Audit, () => now, l => { ran.Add(l); return (code, code == 0 ? "" : "lỗi giả"); }, HgOs.Windows);
        return (svc, s, ran, () => now, t => now = t);
    }

    [Fact]
    public void Dat_hen_phut_goi_lenh_va_nho_thoi_diem_roi_het_gio_tu_xoa()
    {
        var (svc, _, ran, _, setNow) = Make();
        var r = svc.HenPhut(30);
        Assert.True(r.Ok); Assert.Equal(Now.AddMinutes(30), r.Luc);
        Assert.Contains("/t 1800", ran.Single().Args);
        Assert.Equal(Now.AddMinutes(30), svc.DangHen());
        setNow(Now.AddMinutes(31));
        Assert.Null(svc.DangHen());                                        // qua giờ hẹn → không còn hiện
    }

    [Fact]
    public void Hen_moi_thay_hen_cu_va_huy_xoa_hen()
    {
        var (svc, _, ran, _, _) = Make();
        svc.HenPhut(60);
        var r = svc.HenGio("23:00");
        Assert.True(r.Ok);
        Assert.Equal(3, ran.Count);
        Assert.StartsWith("/s /t 3600", ran[0].Args); Assert.Equal("/a", ran[1].Args); Assert.StartsWith("/s /t 6570", ran[2].Args);   // 21:10:30 → 23:00:00 = 6570 giây
        Assert.Equal(new DateTime(2026, 11, 26, 23, 0, 0), svc.DangHen());
        Assert.True(svc.Huy().Ok);
        Assert.Null(svc.DangHen());
        Assert.Equal("/a", ran[^1].Args);
    }

    [Fact]
    public void Gio_sai_hoac_so_phut_sai_khong_goi_lenh()
    {
        var (svc, _, ran, _, _) = Make();
        Assert.False(svc.HenGio("25:00").Ok); Assert.False(svc.HenPhut(0).Ok);
        Assert.Empty(ran); Assert.Null(svc.DangHen());
    }

    [Fact]
    public void Lenh_loi_thi_khong_ghi_nho_hen()
    {
        var (svc, _, _, _, _) = Make(code: 5);
        var r = svc.HenPhut(10);
        Assert.False(r.Ok); Assert.Contains("lỗi giả", r.Text); Assert.Null(svc.DangHen());
    }

    [Fact]
    public void Chi_Quan_tri_duoc_dung_mac_dinh_quan_ly_va_nhan_vien_khong()
    {
        foreach (var role in new[] { Roles.Manager, Roles.Staff })
        {
            var (svc, _, ran, _, _) = Make(role);
            Assert.False(svc.CanUse);
            Assert.Throws<KhoLanh.Core.Services.BusinessException>(() => svc.HenPhut(10));
            Assert.Empty(ran);
        }
        var (adm, _, _, _, _) = Make(Roles.Admin);
        Assert.True(adm.CanUse);
        Assert.NotNull(Perm.Find("hengio"));
    }
}

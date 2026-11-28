using KhoLanh.Core.Services;
using KhoLanh.Core.TroLy;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.TroLy.TroLyMoRong;

namespace KhoLanh.Core.Tests;

/// <summary>1.186: Trợ lý nâng cấp — kiểm tra lịch xuất / email / đồng bộ / sau cập nhật, Sửa nhanh mới, báo cáo tuần.</summary>
public class TroLyMoRong186Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-troly186-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now0 = new(2026, 11, 28, 10, 0, 0);
    private readonly AppServices _s;
    public TroLyMoRong186Tests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1"); _s.Auth.Login("admin", "matkhau1");
        _s.TroLy.Now = () => Now0; _s.TroLy.Version = "1.186.0";
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Lich_xuat_va_phieu_xuat_khau_bat_cac_loi_thuong_gap()
    {
        var lich = new List<XkLich> {
            new("2026-11-28", "LSX 26.11.044", false, false),   // hôm nay chưa phiếu
            new("2026-11-29", "LSX 26.11.069", false, true),    // ngày mai có phiếu → ổn
            new("2026-11-25", "LSX 26.11.010", false, true),    // qua ngày chưa xuất
        };
        var phieu = new List<XkPhieu> {
            new("PX1", "2026-11-28", "26.11.069", "OOLU1234567", "", "Nguyễn A", "S1"),     // thiếu số xe
            new("PX2", "2026-11-28", "26.11.069", "OOLU1234567", "51C-1", "B", "S2"),       // trùng LSX + ngày
            new("PX3", "2026-11-20", "26.11.001", "HMMU5529825", "51C-2", "C", "S3"),
            new("PX4", "2026-11-22", "26.11.002", "HMMU5529825", "51C-3", "D", "S4"),       // cont trùng khác LSX
        };
        var f = CheckXuatKhau(lich, phieu, Now0.Date);
        Assert.Contains(f, x => x.Id == "xk-chua-phieu" && x.Evidence.Any(e => e.Contains("26.11.044")));
        Assert.DoesNotContain(f, x => x.Id == "xk-chua-phieu" && x.Evidence.Any(e => e.Contains("26.11.069")));
        Assert.Contains(f, x => x.Id == "xk-qua-ngay");
        Assert.Contains(f, x => x.Id == "xk-thieu-xe" && x.Evidence.Any(e => e.Contains("số xe")));
        Assert.Contains(f, x => x.Id == "xk-trung-phieu");
        Assert.Contains(f, x => x.Id == "xk-trung-cont");
        Assert.All(f, x => Assert.NotEqual("do", x.Sev));
    }

    [Fact]
    public void Du_lieu_sach_thi_bao_on()
    {
        var f = CheckXuatKhau(new[] { new XkLich("2026-11-28", "LSX 1", true, true) }, new[] { new XkPhieu("P", "2026-11-28", "1", "A", "B", "C", "D") }, Now0.Date);
        Assert.Single(f); Assert.Equal("xanh", f[0].Sev);
        Assert.Empty(CheckXuatKhau(new List<XkLich>(), new List<XkPhieu>(), Now0.Date));
    }

    [Fact]
    public void Dong_bo_giua_cac_may()
    {
        Assert.Empty(CheckDongBo(null)); Assert.Empty(CheckDongBo(new DongBoInfo { Enabled = false, Pending = 9 }));
        var f = CheckDongBo(new DongBoInfo { Enabled = true, Role = "con", Health = 1, LastSyncedMin = 90, Pending = 2, PendingAgeMin = 30 });
        Assert.Contains(f, x => x.Id == "db-lau-chua-sync"); Assert.Contains(f, x => x.Id == "db-so-cho-dung");
        f = CheckDongBo(new DongBoInfo { Enabled = true, Role = "chu", Health = 1, Machines = { ("KHO-02", 3, "LAN"), ("VP", 0, "Internet") } });
        var m = Assert.Single(f); Assert.Contains("KHO-02", m.Evidence[0]); Assert.DoesNotContain(m.Evidence, e => e.Contains("VP"));
        f = CheckDongBo(new DongBoInfo { Enabled = true, Role = "con", Health = 3, Away = true, Pending = 4, PendingAgeMin = 2000 });
        Assert.Contains(f, x => x.Id == "db-ngoai-tuyen-lau");
    }

    [Fact]
    public void Email_lich_xuat()
    {
        Assert.Empty(CheckEmail(new EmailInfo { Ready = false, Now = Now0 }));
        var i = new EmailInfo { Ready = true, Auto = true, IntervalMin = 10, Now = Now0, LastScan = Now0.AddHours(-3), Waiting = 12, OldestWaiting = Now0.AddDays(-3) };
        i.Xe.Add(new XeCase("26.11.044", Now0.AddHours(-1), "phieu_trong")); i.Xe.Add(new XeCase("26.11.069", Now0.AddHours(-5), "no_phieu")); i.Xe.Add(new XeCase("26.11.070", Now0.AddMinutes(-5), "phieu_trong"));
        var f = CheckEmail(i);
        Assert.Contains(f, x => x.Id == "mail-lau-chua-quet"); Assert.Contains(f, x => x.Id == "mail-ton-doong");
        Assert.Contains(f, x => x.Id == "mail-xe-chua-dien" && x.Evidence.Count == 1);   // thư vừa đến 5 phút chưa tính
        Assert.Contains(f, x => x.Id == "mail-xe-chua-phieu");
        Assert.All(f, x => Assert.Equal("quet-email", TroLyFix.Pick(x).Key));
    }

    [Fact]
    public void Sau_cap_nhat_bat_bang_mat_dong_va_chi_muc()
    {
        var truoc = new Dictionary<string, long> { ["an-an/Ton"] = 5000, ["gui/Ton"] = 300, ["nho/x"] = 5 };
        var nay = new Dictionary<string, long> { ["an-an/Ton"] = 1200, ["gui/Ton"] = 290, ["nho/x"] = 0 };
        var f = CheckSauCapNhat(truoc, nay, "1.185.0", "1.186.0", "ok", true);
        var m = Assert.Single(f); Assert.Equal("do", m.Sev); Assert.Contains("an-an/Ton", m.Evidence[0]); Assert.DoesNotContain(m.Evidence, e => e.Contains("nho/x"));
        // cùng bản → không so
        Assert.Empty(CheckSauCapNhat(truoc, nay, "1.186.0", "1.186.0", null, true));
        // sạch → xanh
        Assert.Equal("xanh", Assert.Single(CheckSauCapNhat(truoc, truoc, "1.185.0", "1.186.0", "ok", true)).Sev);
        // thiếu chỉ mục + toàn vẹn lỗi
        f = CheckSauCapNhat(null, nay, "", "1.186.0", "*** in database main ***", false);
        Assert.Contains(f, x => x.Id == "capnhat-toanven" && x.Sev == "do");
        var ix = Assert.Single(f, x => x.Id == "capnhat-chimuc"); TroLyFix.Assign(f); Assert.Equal(TroLyFix.TaoLaiChiMuc, ix.Fix);
    }

    [Fact]
    public void Sua_nhanh_moi_duoc_gan_dung_loai()
    {
        var f = new List<Finding> {
            TroLyChecks.Mk("dong-bo", "do", "Mạng LAN", "Không nối được máy chủ LAN", "x"),
            TroLyChecks.Mk("dong-bo", "vang", "Đồng bộ giữa các máy", "Sổ chờ gửi không giảm dù đang nối máy chủ", "x"),
            TroLyChecks.Mk("cai-dat", "vang", "Cài đặt máy", "Cài đặt máy bị mất 5 khóa so với bản dự phòng", "x") };
        TroLyFix.Assign(f);
        Assert.Equal(new[] { TroLyFix.NoiLaiLan, TroLyFix.NoiLaiLan, TroLyFix.NapLaiCaiDat }, f.Select(x => x.Fix).ToArray());
    }

    [Fact]
    public void Quet_that_chay_cac_buoc_moi_khong_loi_va_sua_nhanh_chay_duoc()
    {
        var r = _s.TroLy.Scan(deep: false);
        foreach (var n in new[] { "Lịch xuất & email", "Đồng bộ giữa các máy", "Sau cập nhật" }) Assert.Contains(r.Steps, s => s.Name == n && s.Error.Length == 0);
        Assert.Contains("chỉ mục", _s.TroLy.RebuildIndexes());
        Assert.Contains("nối lại", _s.TroLy.ReconnectLan());
        // lần quét sau đổi bản: ghi mốc bản cũ rồi so
        _s.TroLy.Version = "1.187.0";
        var r2 = _s.TroLy.Scan(deep: false);
        Assert.Contains(r2.Findings, f => f.Id == "capnhat-on");
    }

    [Fact]
    public void Bao_cao_tuan()
    {
        var d = new TuanData { From = Now0.Date.AddDays(-6), To = Now0.Date, Version = "1.186.0", Machine = "KHO-PC", ErrorKinds = 2, ErrorTotal = 7, LastBackup = "28/11 03:00", DbBytes = 52_000_000 };
        d.Open.Add(("do", "Đồng bộ", "Không nối được máy chủ")); d.Open.Add(("vang", "Email", "Lâu chưa quét"));
        d.TopErrors.Add("NullReferenceException: x ×5");
        d.Machines.Add(("KHO-01", "1.185.0", false, 0, 1)); d.Machines.Add(("VP", "1.186.0", true, 0, 0));
        var t = BuildTuan(d);
        Assert.Contains("1 Nặng · 1 Cần xem", t); Assert.Contains("NullReferenceException", t); Assert.Contains("MẤT LIÊN LẠC", t); Assert.Contains("2 bản khác nhau", t); Assert.Contains("sao lưu cuối: 28/11", t);
        Assert.Contains("Hiện 1 Nặng", TuanShort(d));
        // dịch vụ thật: dựng được + đến hạn sau 7 ngày
        Assert.Contains("BÁO CÁO TUẦN", _s.TroLy.WeeklyReport());
        Assert.False(_s.TroLy.WeeklyDue());                 // lần đầu chỉ đặt mốc
        _s.TroLy.Now = () => Now0.AddDays(8);
        Assert.True(_s.TroLy.WeeklyDue()); _s.TroLy.WeeklyDone(); Assert.False(_s.TroLy.WeeklyDue());
    }
}

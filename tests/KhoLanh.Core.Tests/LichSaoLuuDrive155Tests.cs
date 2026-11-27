using KhoLanh.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.155: chọn "Mỗi giờ" ở Sao lưu Drive cá nhân, bảng settings bị làm trống (dọn rác / lỗi ghi) → mở lại app vẫn "Mỗi giờ", không về "Mỗi ngày".</summary>
public class LichSaoLuuDrive155Tests
{
    [Fact]
    public void Chu_ky_tu_sao_luu_giu_nguyen_khi_bang_cai_dat_bi_lam_trong()
    {
        var path = Path.Combine(Path.GetTempPath(), "kl155_" + Guid.NewGuid().ToString("N") + ".db");
        var a = new AppServices(path);
        a.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        a.Auth.Login("admin", "matkhau1");
        a.DriveCaNhan.SetEvery(1);
        a.Settings.Set("phone_sync_interval", "300");
        Assert.Equal(1, a.DriveCaNhan.Config.Every);

        // mở lại bình thường: giữ
        var b = new AppServices(path); b.Auth.Login("admin", "matkhau1");
        Assert.Equal(1, b.DriveCaNhan.Config.Every);

        // bảng settings bị xóa ngoài app → nạp lại từ bản dự phòng
        using (var c = b.Db.Open()) Dapper.SqlMapper.Execute(c, "DROP TABLE settings");
        SqliteConnection.ClearAllPools();
        var d = new AppServices(path); d.Auth.Login("admin", "matkhau1");
        Assert.Equal(1, d.DriveCaNhan.Config.Every);
        Assert.Equal("300", d.Settings.Get("phone_sync_interval"));
        foreach (var s in new[] { a, b, d }) s.Lan.Dispose();
    }
}

/// <summary>1.156: sao lưu mỗi giờ → hôm nay giữ đủ 24 bản, qua ngày chỉ còn bản mới nhất của từng ngày cũ.</summary>
public class GiuBanSaoLuu156Tests
{
    [Fact]
    public void Moi_gio_1_ban_qua_ngay_chi_giu_ban_moi_nhat_cua_ngay_cu()
    {
        var now = new DateTime(2026, 11, 27, 9, 0, 0);
        var files = new List<(string, DateTime)>();
        for (var at = new DateTime(2026, 11, 25, 0, 0, 0); at <= now; at = at.AddHours(1)) files.Add((KhoLanh.Core.SaoLuu.DriveCaNhanService.FileName(at), at));
        var keep = KhoLanh.Core.SaoLuu.DriveCaNhanService.KeepSet(files, now);
        string N(int d, int h) => KhoLanh.Core.SaoLuu.DriveCaNhanService.FileName(new DateTime(2026, 11, d, h, 0, 0));
        for (var h = 0; h <= 9; h++) Assert.Contains(N(27, h), keep);          // hôm nay: đủ 10 bản 0h–9h
        Assert.Contains(N(26, 23), keep); Assert.Contains(N(25, 23), keep);   // ngày cũ: bản mới nhất (23 giờ)
        for (var h = 0; h < 23; h++) { Assert.DoesNotContain(N(26, h), keep); Assert.DoesNotContain(N(25, h), keep); }
        Assert.Equal(12, keep.Count);
    }
}

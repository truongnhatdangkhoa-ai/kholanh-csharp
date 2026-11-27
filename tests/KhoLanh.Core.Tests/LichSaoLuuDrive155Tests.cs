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

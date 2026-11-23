using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Bật tự đăng nhập (dùng cho Khởi động cùng Windows): kiểm mật khẩu không đăng nhập, rồi nhớ để lần mở sau vào thẳng.</summary>
public class BootLoginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-boot-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Kiem_mat_khau_roi_nho_tu_dang_nhap()
    {
        Directory.CreateDirectory(_dir);
        var s = new AppServices(Path.Combine(_dir, "kho.db"));
        s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); s.Auth.Login("admin", "matkhau1");
        s.Auth.CreateUser("nhanvien", "NV", "matkhau2", Models.Roles.Staff);
        Assert.True(s.Auth.CheckPassword("nhanvien", "matkhau2"));
        Assert.False(s.Auth.CheckPassword("nhanvien", "sai"));
        Assert.Equal("admin", s.Session.Username);                 // không đổi người đang đăng nhập
        s.Remember.Save("nhanvien", "matkhau2", remember: true, auto: true);
        var again = new AppServices(Path.Combine(_dir, "kho.db"));
        Assert.True(again.Remember.Auto);
        Assert.Equal("nhanvien", again.Remember.Username);
        Assert.Equal("matkhau2", again.Remember.Password);
    }
}

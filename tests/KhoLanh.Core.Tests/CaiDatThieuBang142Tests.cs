using KhoLanh.Core;
using KhoLanh.Core.Data;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.142: file dữ liệu bị phần mềm dọn rác xóa / làm rỗng → đọc / ghi cài đặt không văng lỗi, tự tạo lại bảng.</summary>
public class CaiDatThieuBang142Tests
{
    private static (Database db, string path) Moi()
    {
        var path = Path.Combine(Path.GetTempPath(), "kl142_" + Guid.NewGuid().ToString("N") + ".db");
        return (new Database(path), path);
    }

    [Fact]
    public void Doc_cai_dat_khi_file_rong_khong_loi_va_tao_lai_bang()
    {
        var (db, path) = Moi();
        var s = new SettingsService(db);
        File.WriteAllBytes(path, Array.Empty<byte>());           // như bị dọn rác: file còn nhưng không có bảng nào
        Assert.Equal("mac-dinh", s.Get("may_chu_chay_an", "mac-dinh"));
        s.Set("khoa", "gia-tri");
        Assert.Equal("gia-tri", s.Get("khoa"));
    }

    [Fact]
    public void Ghi_cai_dat_khi_thieu_bang_tu_tao_lai()
    {
        var (db, path) = Moi();
        db.Initialize();
        var s = new SettingsService(db);
        using (var c = db.Open()) Dapper.SqlMapper.Execute(c, "DROP TABLE settings");
        s.Set("a", "1");
        Assert.Equal("1", s.Get("a"));
    }
}

/// <summary>1.143: cài đặt quan trọng có bản dự phòng ngoài bảng settings.</summary>
public class CaiDatDuPhong143Tests
{
    private static string Path_() => Path.Combine(Path.GetTempPath(), "kl143_" + Guid.NewGuid().ToString("N") + ".db");

    [Fact]
    public void Bang_settings_bi_xoa_thi_nap_lai_vai_may_chu()
    {
        var path = Path_();
        var a = new AppServices(path);
        a.Settings.Set("lan_role", "chu"); a.Settings.Set("lan_key", "KEY"); a.Settings.Set("acct_url", "https://x");
        a.Settings.Set("lan_last_seq", "99");
        using (var c = a.Db.Open()) Dapper.SqlMapper.Execute(c, "DROP TABLE settings");
        var b = new AppServices(path);
        Assert.Equal("chu", b.Settings.Get("lan_role"));
        Assert.Equal("KEY", b.Settings.Get("lan_key"));
        Assert.Equal("https://x", b.Settings.Get("acct_url"));
        Assert.Equal("", b.Settings.Get("lan_last_seq"));          // khóa phụ thuộc dữ liệu không nạp lại
        Assert.Contains("khôi phục", b.SettingsNotice);
    }

    [Fact]
    public void Khoa_dang_co_ke_ca_rong_khong_bi_ghi_de()
    {
        var path = Path_();
        var a = new AppServices(path);
        a.Settings.Set("lan_role", "chu");
        a.Settings.Set("lan_role", "");                            // người dùng chủ động bỏ vai
        var b = new AppServices(path);
        Assert.Equal("", b.Settings.Get("lan_role"));
        Assert.Equal("", b.SettingsNotice);
    }

    [Fact]
    public void Mat_cai_dat_khong_co_ban_du_phong_thi_bao_ro()
    {
        var path = Path_();
        var a = new AppServices(path);
        a.Auth.CreateFirstAdmin("admin", "QT", "matkhau1");
        using (var c = a.Db.Open()) Dapper.SqlMapper.Execute(c, "DROP TABLE settings");
        var b = new AppServices(path);
        Assert.Contains("MÁY ĐƠN", b.SettingsNotice);
    }
}

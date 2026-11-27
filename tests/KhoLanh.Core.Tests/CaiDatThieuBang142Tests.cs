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

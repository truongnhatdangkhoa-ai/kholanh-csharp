using KhoLanh.Core.Lan;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.179: mục Hệ thống chỉ máy chủ sửa — bản chụp tình trạng dùng chung, máy chủ tắt theo lịch không bị báo hỏng, máy đơn không bị khóa.</summary>
public class HeThong179Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-he-thong-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private static readonly DateTime T0 = new(2026, 11, 27, 15, 0, 0, DateTimeKind.Utc);

    public HeThong179Tests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static HtTrangThai Ts(DateTime at, string tat = "", DateTime? hen = null) =>
        new() { Id = "m1", Ten = "KHO-PC", At = HtTrangThai.Iso(at), Tat = tat, HenTat = hen is null ? "" : HtTrangThai.Iso(hen.Value) };

    [Fact]
    public void May_don_khong_bi_khoa_va_khong_ghi_ban_chup()
    {
        Assert.False(_s.HeThong.ChiXem);
        Assert.False(_s.HeThong.Publish());          // không làm máy chủ LAN → không ghi
        Assert.Null(_s.HeThong.Read());
    }

    [Fact]
    public void Ban_chup_khong_chua_token_va_doc_lai_duoc()
    {
        _s.Settings.Set("phone_pc_token", "BI-MAT-123"); _s.Settings.Set("phone_webapp_url", "https://script.google.com/macros/s/ABC/exec");
        var t = _s.HeThong.Build();
        Assert.DoesNotContain("BI-MAT-123", t.ToJson());
        var store = new HeThongTrangThaiStore(() => _s.Lan);
        Assert.True(store.Write(t));
        var back = store.Read();
        Assert.NotNull(back);
        Assert.Equal(t.Id, back!.Id);
        Assert.False(store.Write(t));                // chưa đổi, chưa quá 5 phút → không ghi lại
    }

    [Fact]
    public void Phan_loai_may_chu_dang_chay_tat_theo_lich_tat_bat_thuong()
    {
        Assert.Equal(HtTrangThai.May.Chay, Ts(T0).Phan(T0.AddMinutes(10)));
        // im lặng quá 15 phút, không dấu, không hẹn → chưa báo (tắt bất thường)
        Assert.Equal(HtTrangThai.May.KhongBao, Ts(T0).Phan(T0.AddHours(3)));
        // có dấu "lich" → tắt theo lịch, KHÔNG báo hỏng ban đêm
        Assert.Equal(HtTrangThai.May.TatTheoLich, Ts(T0, "lich").Phan(T0.AddHours(9)));
        // chưa kịp ghi dấu nhưng giờ hẹn tắt đã qua → vẫn là tắt theo lịch
        Assert.Equal(HtTrangThai.May.TatTheoLich, Ts(T0, hen: T0.AddMinutes(1)).Phan(T0.AddHours(9)));
        Assert.Equal(HtTrangThai.May.DaThoat, Ts(T0, "thoat").Phan(T0.AddHours(2)));
        // hẹn tắt nhưng máy chủ im lặng TRƯỚC giờ hẹn quá lâu → bất thường, không phải tắt theo lịch
        Assert.Equal(HtTrangThai.May.KhongBao, Ts(T0, hen: T0.AddHours(8)).Phan(T0.AddHours(2)));
    }

    [Fact]
    public void Chu_hien_thi_tat_theo_lich_khong_bao_loi()
    {
        var (k, txt) = HeThongService.MayChuText(Ts(T0, "lich", T0.AddMinutes(1)), T0.AddHours(8));
        Assert.Equal("info", k);
        Assert.Contains("tắt theo lịch", txt);
        Assert.Equal("warn", HeThongService.MayChuText(null, T0).Kind);
    }

    [Fact]
    public void Gmail_duoc_che_bot()
    {
        Assert.Equal("abc***@gmail.com", HeThongService.Mask("abcdef@gmail.com"));
        Assert.Equal("", HeThongService.Mask(""));
    }
}

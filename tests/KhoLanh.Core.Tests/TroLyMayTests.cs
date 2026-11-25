using KhoLanh.Core.Services;
using KhoLanh.Core.TroLy;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.TroLy.TroLyChecks;

namespace KhoLanh.Core.Tests;

/// <summary>Trợ lý giai đoạn 5: tình trạng nhiều máy (database dùng chung), lịch sử quét 30 ngày, báo cáo đầy đủ mà không lộ bí mật.</summary>
public class TroLyMayTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-troly-may-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private static readonly DateTime Now0 = new(2026, 11, 25, 10, 0, 0);

    public TroLyMayTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
        _s.TroLy.Now = () => Now0;
        _s.TroLy.Version = "1.102.0";
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static PerfStore.Sample Sample(DateTime at, double cpu, double ram) => new(at, cpu, 3, ram, 420, 80, 237, 12, 140);

    private static MayTinhTrang May(string id, string name, DateTime at, int nDo = 1, double cpu = 41, string title = "Lỗi phần mềm: khach=Công ty ABC bị lỗi")
        => TroLyMay.Build(id, name, "1.102.0", "Máy con LAN", at, Now0.AddMinutes(-10), nDo, 2, 5,
            new[] { ("vang", "Đồng bộ", "Sổ chờ LAN còn 3 mục"), ("do", "Phần mềm", title), ("xanh", "Kết nối", "Cấu hình kết nối ổn") },
            Sample(at, cpu, 63), 35, 60);

    [Fact]
    public void Ghi_doc_tinh_trang_hai_may()
    {
        var store = new TroLyMayStore(() => _s.Lan);
        Assert.True(store.Write(May("may-a", "KHO-01", Now0.AddMinutes(-5))));
        Assert.True(store.Write(May("may-b", "VANPHONG", Now0)));
        var all = store.ReadAll();
        Assert.Equal(new[] { "may-b", "may-a" }, all.Select(x => x.Id));
        var a = all.Single(x => x.Id == "may-a");
        Assert.Equal("KHO-01", a.Name);
        Assert.Equal(2, a.Canh.Count);                                // chỉ Nặng + Cần xem, không có mục Ổn
        Assert.Equal("do", a.Canh[0].Sev);                            // Nặng trước
        Assert.DoesNotContain("Công ty ABC", a.Canh[0].Title);       // ẩn tên khách
        Assert.Equal(41, a.Cpu); Assert.Equal(35, a.Cpu24); Assert.Equal(80, a.DiskFree);
        // mỗi máy 1 sheet riêng trong db dùng chung
        Assert.Contains(TroLyMay.SheetOf("may-a"), _s.Lan.List(TroLyMay.DbKey));
        Assert.Contains(TroLyMay.SheetOf("may-b"), _s.Lan.List(TroLyMay.DbKey));
    }

    [Fact]
    public void Chi_ghi_khi_doi_noi_dung_hoac_qua_nhip()
    {
        var store = new TroLyMayStore(() => _s.Lan);
        Assert.True(store.Write(May("may-a", "KHO-01", Now0)));
        Assert.False(store.Write(May("may-a", "KHO-01", Now0.AddMinutes(15))));               // không đổi gì
        Assert.False(store.Write(May("may-a", "KHO-01", Now0.AddMinutes(15), cpu: 43)));      // CPU dao động nhỏ
        Assert.True(store.Write(May("may-a", "KHO-01", Now0.AddMinutes(15), nDo: 2)));        // số Nặng đổi
        Assert.True(store.Write(May("may-a", "KHO-01", Now0.AddMinutes(16), title: "Lỗi khác"))); // cảnh báo đổi
        Assert.False(store.Write(May("may-a", "KHO-01", Now0.AddMinutes(30), title: "Lỗi khác")));
        Assert.True(store.Write(May("may-a", "KHO-01", Now0.AddMinutes(47), title: "Lỗi khác")));  // quá nhịp 30 phút
        Assert.Equal(Now0.AddMinutes(47), store.Read("may-a")!.At);
    }

    [Fact]
    public void Mat_lien_lac_sau_mot_gio()
    {
        var t = May("may-a", "KHO-01", Now0);
        Assert.False(TroLyMay.MatLienLac(t, Now0.AddMinutes(59)));
        Assert.True(TroLyMay.MatLienLac(t, Now0.AddMinutes(61)));
    }

    [Fact]
    public void Lich_su_giu_30_ngay_va_gop_theo_ngay()
    {
        var ls = new TroLyLichSu(Path.Combine(_dir, PerfStore.FileName));
        for (var d = 0; d < 40; d++) ls.Add(new TroLyLichSu.Scan(Now0.AddDays(-d), false, d % 3, 4, 10, 1, 0, 0));
        ls.Add(new TroLyLichSu.Scan(Now0.AddHours(-3), true, 5, 1, 9, 2, 1, 3));
        Assert.Equal(41, ls.Count());
        Assert.Equal(9, ls.Prune(Now0));               // cũ hơn 30 ngày: 31..39 ngày trước (đúng mốc 30 ngày còn giữ)
        Assert.All(ls.Range(Now0.AddYears(-1), Now0), s => Assert.True(Now0 - s.At <= TimeSpan.FromDays(30)));
        var days = TroLyLichSu.ByDay(ls.Range(Now0.AddDays(-6).Date, Now0), Now0, 7);
        Assert.Equal(7, days.Count);
        var today = days[^1]!;
        Assert.Equal(5, today.Do);                     // cao nhất trong ngày
        Assert.Equal(2, today.Scans);
        Assert.Equal(3, today.Moi); Assert.Equal(3, today.Dong);
        // ngày không quét → null
        var empty = TroLyLichSu.ByDay(Array.Empty<TroLyLichSu.Scan>(), Now0, 30);
        Assert.Equal(30, empty.Count); Assert.All(empty, Assert.Null);
    }

    [Fact]
    public void Quet_ghi_lich_su_va_tinh_trang_may_minh()
    {
        var r = _s.TroLy.Scan(false);
        var me = Assert.Single(_s.TroLy.Machines());
        Assert.Equal(_s.Lan.MachineId, me.Id);
        Assert.Equal(r.Do, me.Do); Assert.Equal(r.Vang, me.Vang);
        Assert.Equal("1.102.0", me.Ver);
        Assert.Equal(Now0, me.ScanAt);
        var h = _s.TroLy.History(7);
        Assert.Equal(1, h[^1]!.Scans);
        Assert.False(_s.TroLy.PublishStatus());       // không đổi → không ghi lại
    }

    [Fact]
    public void Bao_cao_khong_lo_token_mat_khau_email()
    {
        const string token = "TOKENBIMAT12345", url = "https://script.google.com/macros/s/AKfycbBIMATxyz987/exec", pc = "PCTOKEN-xyz-98765";
        _s.Settings.Set("acct_url", url); _s.Settings.Set("acct_token", token);
        _s.Settings.Set(PhoneWebApp.KeyToken, Secret.Protect(pc));
        _s.Settings.Set("lx_mail_email", "nguyenvana@gmail.com"); _s.Settings.Set("lx_mail_pass", Secret.Protect("matkhauungdung"));
        var f = Mk("ket-noi", "do", "Tài khoản dùng chung", "Tài khoản dùng chung tải lỗi",
            "Gọi " + url + "?token=" + token + " lỗi", new[] { "người gửi nguyenvana@gmail.com", "PC_TOKEN " + pc });
        f.Go = "backup"; f.GoLabel = "Cài đặt máy";
        var txt = TroLyReport.Build(new[] { f }, "1.102.0", Now0, false, _s.TroLy.ReportExtra(new[] { f }));
        Assert.DoesNotContain(token, txt);
        Assert.DoesNotContain(pc, txt);
        Assert.DoesNotContain("AKfycbBIMATxyz987", txt);
        Assert.DoesNotContain("matkhauungdung", txt);
        Assert.DoesNotContain("nguyenvana@gmail.com", txt);
        Assert.Contains("n***@gmail.com", txt);
        Assert.Contains("Cài đặt liên quan", txt);
        Assert.Contains("Tài khoản dùng chung — mã truy cập: có", txt);
        Assert.Contains("có (…/exec)", txt);
        Assert.Contains("Trang sửa trong app: Cài đặt máy", txt);
        // chữ ký cũ vẫn dùng được
        Assert.Contains("Tài khoản dùng chung tải lỗi", TroLyReport.Build(new[] { f }, "1.102.0", Now0));
    }

    [Fact]
    public void Bao_cao_trich_log_quanh_loi_phan_mem()
    {
        var log = string.Join("\n",
            "2026-11-25 08:00:00 bản 1.101.0 [Lỗi chưa bắt]",
            "System.InvalidOperationException: Khác hẳn",
            "   at KhoLanh.App.Views.AnAnView.Foo()",
            "2026-11-25 09:00:00 bản 1.101.0 [Lỗi chưa bắt]",
            "System.NullReferenceException: Object reference not set to an instance of an object.",
            string.Join("\n", Enumerable.Range(1, 30).Select(i => $"   at KhoLanh.App.Views.ViTriView.Render{i}()")),
            "2026-11-25 09:30:00 bản 1.101.0 [Lỗi chưa bắt]",
            "System.InvalidOperationException: Khác hẳn",
            "");
        var errs = ParseErrorLog(log, Now0);
        var nre = CheckErrors(errs).Single(x => x.Title.Contains("Object reference"));
        var lines = TroLyBaoCao.LogAround(log, nre);
        Assert.Equal(TroLyBaoCao.LogMax, lines.Count);
        Assert.StartsWith("2026-11-25 09:00:00", lines[0]);
        Assert.DoesNotContain(lines, l => l.Contains("Khác hẳn"));
        var x = new TroLyBaoCao.Extra { LogText = log };
        var txt = TroLyReport.Build(new[] { nre }, "1.102.0", Now0, true, x);
        Assert.Contains("Log quanh lỗi", txt);
        Assert.Contains("ViTriView.Render1()", txt);
        Assert.DoesNotContain("Render25()", txt);
        // cảnh báo khác loại: không trích log
        Assert.Empty(TroLyBaoCao.LogAround(log, Mk("csdl", "vang", "CSDL", "x", "y")));
    }

    [Fact]
    public void Che_email_va_duong_dan()
    {
        Assert.Equal("a***@gmail.com", TroLyBaoCao.MaskEmail("anan.kho@gmail.com"));
        Assert.Equal("không", TroLyBaoCao.MaskEmail(""));
        Assert.Equal("có (…/exec)", TroLyBaoCao.MaskUrl("https://script.google.com/macros/s/AKfycbXYZ/exec"));
        Assert.Equal("có", TroLyBaoCao.MaskUrl("https://x.com/?token=abc"));
        Assert.EndsWith("KhoLanh)", TroLyBaoCao.MaskPath(@"G:\My Drive\KhoLanh"));
        Assert.DoesNotContain("My Drive", TroLyBaoCao.MaskPath(@"G:\My Drive\KhoLanh"));
    }
}

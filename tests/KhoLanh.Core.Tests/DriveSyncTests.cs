using System.Text.Json.Nodes;
using KhoLanh.Core.Lan;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class DriveSyncTests : IDisposable
{
    private const string Db = "ton-kho-an-an";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "klcs-drv-" + Guid.NewGuid().ToString("N"));
    private readonly string _drive;
    private readonly List<AppServices> _live = new();

    public DriveSyncTests() { _drive = Path.Combine(_root, "drive"); Directory.CreateDirectory(_drive); }
    public void Dispose() { foreach (var s in _live) { s.Drive.Stop(); s.Lan.Dispose(); } SqliteConnection.ClearAllPools(); try { Directory.Delete(_root, true); } catch { } }

    private AppServices Machine(string name)
    {
        var s = new AppServices(Path.Combine(_root, name, "kho.db"));
        s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1"); s.Auth.Login("admin", "matkhau1");
        _live.Add(s); return s;
    }
    private static JsonArray Rows(params string[] names) => new(names.Select(n => (JsonNode)new JsonObject { ["ma"] = n }).ToArray());
    private static Dictionary<string, JsonArray?> One(string sheet, JsonArray rows) => new() { [sheet] = rows };
    private static string Names(string json) => string.Join(",", JsonNode.Parse(json)!.AsArray().Select(r => r!["ma"]!.ToString()));

    private (AppServices main, AppServices remote) Pair()
    {
        var main = Machine("chinh"); main.Drive.Configure("chinh", _drive, null, main.Session);
        var remote = Machine("xa"); remote.Drive.Configure("xa", _drive, main.Drive.Code(), remote.Session);
        remote.ReloadLan(start: false);
        return (main, remote);
    }

    [Fact]
    public void Remote_pulls_main_data_and_files_are_encrypted()
    {
        var (main, remote) = Pair();
        main.Lan.Write(Db, One("S", Rows("a", "b")));
        Assert.Contains("đã đưa bản #1", main.Drive.SyncNow());
        var snap = Directory.GetFiles(Path.Combine(_drive, DriveSyncService.Folder, "snap")).Single();
        Assert.DoesNotContain("\"ma\"", File.ReadAllText(snap));                 // đã mã hóa + nén
        Assert.Contains("đã tải bản #1", remote.Drive.SyncNow());
        Assert.Equal("a,b", Names(remote.Lan.Read(Db, "S")));
        Assert.Contains("đã khớp", remote.Drive.SyncNow());
    }

    [Fact]
    public void Remote_changes_go_back_and_are_merged_by_main()
    {
        var (main, remote) = Pair();
        main.Lan.Write(Db, One("S", Rows("a")));
        main.Drive.SyncNow(); remote.Drive.SyncNow();
        remote.Lan.Write(Db, One("S", Rows("a", "xa")));                  // sửa ở máy xa → sổ chờ
        Assert.Equal(1, remote.Lan.PendingCount());
        main.Lan.Write(Db, One("T", Rows("t")));                          // main sửa bảng khác cùng lúc
        Assert.Contains("đã gửi 1 bảng", remote.Drive.SyncNow());
        var r = main.Drive.SyncNow();
        Assert.Contains("nhận 1 bảng", r);
        Assert.Equal("a,xa", Names(main.Lan.Read(Db, "S")));
        remote.Drive.SyncNow();
        Assert.Equal(0, remote.Lan.PendingCount());
        Assert.Equal("t", Names(remote.Lan.Read(Db, "T")));
        Assert.Equal("a,xa", Names(remote.Lan.Read(Db, "S")));
    }

    [Fact]
    public void Same_sheet_edited_on_both_sides_is_merged_or_flagged_not_lost()
    {
        var (main, remote) = Pair();
        main.Lan.Write(Db, One("S", Rows("a")));
        main.Drive.SyncNow(); remote.Drive.SyncNow();
        remote.Lan.Write(Db, One("S", Rows("a", "xa")));
        main.Lan.Write(Db, One("S", Rows("a", "main")));
        remote.Drive.SyncNow(); main.Drive.SyncNow(); remote.Drive.SyncNow();
        var m = Names(main.Lan.Read(Db, "S"));
        Assert.Contains("main", m);                                               // không mất số liệu của máy chính
        Assert.True(m.Contains("xa") || main.Lan.Conflicts().Count > 0);          // của máy xa: đã gộp hoặc chờ Quản trị chọn
        Assert.Equal(m, Names(remote.Lan.Read(Db, "S")));
    }


    // ---------------- vai gắn với tài khoản ----------------

    private AppServices Account(string name, string key, params string[] mainUsers) => AccountWith(name, key, null, mainUsers);
    private AppServices AccountWith(string name, string key, string? extraUser, params string[] mainUsers)
    {
        var m = Machine(name);
        if (extraUser is not null) m.Auth.CreateUser(extraUser, "Nhân viên", "matkhau1", "staff");
        m.Settings.Set("acct_url", "http://x"); m.Settings.Set("acct_token", KhoLanh.Core.Services.Secret.Protect("t"));   // bật "tài khoản dùng chung" giả (chỉ đọc kế hoạch đã lưu)
        var plan = new LanPlan { On = false };
        plan.Drive = new DrivePlan { On = true, Key = key };
        foreach (var u in mainUsers) plan.Drive.Main.Add(u);
        m.Settings.Set("lan_plan", plan.ToJson());
        m.Drive.SetFolder(_drive);
        return m;
    }

    [Fact]
    public void Same_admin_on_two_machines_first_is_main_second_is_remote_until_released()
    {
        var key = DriveSyncService.NewKey();
        var a = Account("cty", key, "admin"); var b = Account("nha", key, "admin");
        Assert.Contains("MÁY CHÍNH", DrivePlanSync.ApplyAtLogin(a)!);
        Assert.Equal(DriveSyncService.ModeMain, a.Drive.Mode);
        a.Lan.Write(Db, One("S", Rows("a", "b"))); a.Drive.SyncNow();
        var note = DrivePlanSync.ApplyAtLogin(b)!;                                // máy thứ hai: máy chính đang hoạt động
        Assert.Contains("đang là máy chính", note);
        Assert.Equal(DriveSyncService.ModeRemote, b.Drive.Mode);
        b.Drive.SyncNow();
        Assert.Equal("a,b", Names(b.Lan.Read(Db, "S")));
        // máy công ty thoát → nhả khóa → máy kia đăng nhập lại thì làm máy chính, kèm việc đã sửa ở máy xa
        b.Lan.Write(Db, One("S", Rows("a", "b", "nha")));
        a.Lan.Write(Db, One("T", Rows("t")));
        a.Drive.ReleaseOnExit(); a.Drive.Stop();
        Assert.Contains("MÁY CHÍNH", DrivePlanSync.ApplyAtLogin(b)!);
        Assert.Equal(DriveSyncService.ModeMain, b.Drive.Mode);
        Assert.Equal("a,b,nha", Names(b.Lan.Read(Db, "S")));                       // thay đổi ở máy xa còn nguyên
        Assert.Equal("t", Names(b.Lan.Read(Db, "T")));                              // dữ liệu máy cũ đã đưa lên trước khi thoát
        Assert.Equal(0, b.Lan.PendingCount());
    }

    [Fact]
    public void Non_main_account_is_remote_and_stale_lease_can_be_forced()
    {
        var key = DriveSyncService.NewKey();
        var a = Account("cty", key, "admin"); var c = AccountWith("kho", key, "nhanvien", "admin");
        DrivePlanSync.ApplyAtLogin(a); a.Lan.Write(Db, One("S", Rows("a"))); a.Drive.SyncNow();
        c.Auth.Logout(); c.Auth.Login("nhanvien", "matkhau1");
        Assert.Contains("xem từ xa", DrivePlanSync.ApplyAtLogin(c)!);
        Assert.Equal(DriveSyncService.ModeRemote, c.Drive.Mode);
        Assert.Throws<KhoLanh.Core.Services.BusinessException>(() => c.Drive.BecomeMain(c, "nhanvien"));          // khóa còn hạn
        var msg = c.Drive.BecomeMain(c, "nhanvien", force: true);                                                  // chiếm quyền (máy kia treo)
        Assert.Contains("máy chính", msg);
    }

    [Fact]
    public void Wrong_code_is_rejected()
    {
        var main = Machine("chinh"); main.Drive.Configure("chinh", _drive, null, main.Session);
        main.Lan.Write(Db, One("S", Rows("a"))); main.Drive.SyncNow();
        var remote = Machine("xa"); remote.Drive.Configure("xa", _drive, DriveSyncService.CodePrefix + "eyJrIjoic2FpIn0", remote.Session);
        remote.ReloadLan(start: false);
        Assert.StartsWith("Lỗi", remote.Drive.SyncNow());
        Assert.Equal("[]", remote.Lan.Read(Db, "S"));
    }

    // ------------------------------------------------------------------ máy ở nhà · sao lưu Drive · khôi phục máy ở nhà · khóa gửi lên

    [Fact]
    public void Ba_may_nha_doc_lap_may_cong_ty_mo_lai_thi_gop()
    {
        var (main, home1) = Pair();
        var home2 = Machine("nha2"); home2.Drive.Configure("xa", _drive, main.Drive.Code(), home2.Session); home2.ReloadLan(start: false);
        main.Lan.Write(Db, One("S", Rows("a"))); main.Lan.Write(Db, One("T", Rows("t")));
        main.Drive.SyncNow(); main.Drive.ReleaseOnExit();                          // chiều: máy công ty tắt app
        home1.Drive.SetHome(true); home2.Drive.SetHome(true);
        home1.Drive.SyncNow(); home2.Drive.SyncNow();
        home1.Lan.Write(Db, One("S", Rows("a", "nha1")));                          // tối: 2 máy nhà làm độc lập
        home2.Lan.Write(Db, One("T", Rows("t", "nha2")));
        home1.Drive.ReleaseOnExit(); home2.Drive.ReleaseOnExit();                  // tắt app → gửi nốt
        Assert.Throws<KhoLanh.Core.Services.BusinessException>(() => home1.Drive.BecomeMain(home1, "admin"));   // máy nhà không tự làm máy chính
        main.Drive.SyncNow();                                                       // sáng: máy công ty mở lại → gộp
        Assert.Equal("a,nha1", Names(main.Lan.Read(Db, "S")));
        Assert.Equal("t,nha2", Names(main.Lan.Read(Db, "T")));
        home1.Drive.SyncNow();
        Assert.Equal(0, home1.Lan.PendingCount());
        Assert.Equal("t,nha2", Names(home1.Lan.Read(Db, "T")));
    }

    [Fact]
    public void Sao_luu_len_Drive_moi_ngay_tai_ve_va_khoi_phuc_may_nha()
    {
        var (main, home) = Pair();
        var day = new DateTime(2026, 11, 23);
        main.Drive.Now = () => day;
        main.Lan.Write(Db, One("S", Rows("a", "b")));
        Assert.Contains("đã sao lưu lên Drive", main.Drive.SyncNow());
        Assert.DoesNotContain("đã sao lưu", main.Drive.SyncNow());                 // mỗi ngày 1 bản
        for (var d = 1; d <= 20; d++) { main.Drive.Now = () => day.AddDays(d); main.Drive.BackupToDrive(); }
        var list = main.Drive.DriveBackups();
        Assert.Equal(15, list.Count);                                              // 14 gần nhất + bản đầu tháng 11
        Assert.Contains(list, b => b.Name == "b-2026-11-23.kldb");
        var bk = Directory.GetFiles(Path.Combine(_drive, DriveSyncService.Folder, "backup")).First();
        Assert.DoesNotContain("SQLite format", File.ReadAllText(bk));             // đã mã hóa

        // máy nhà: tải bản về, sửa (chờ gửi), rồi khôi phục — cài đặt máy giữ nguyên, sổ chờ bỏ
        home.Drive.SyncNow();
        home.Lan.Write(Db, One("S", Rows("a", "b", "nha")));
        Assert.Equal(1, home.Lan.PendingCount());
        var tmp = Path.Combine(_root, "kp.db");
        home.Drive.DownloadBackup("b-2026-11-23.kldb", tmp);
        home.Backup.Restore(tmp);
        Assert.Equal(0, home.Lan.PendingCount());
        Assert.Equal("xa", home.Settings.Get("drive_mode"));
        Assert.Equal(_drive, home.Settings.Get("drive_dir"));
        Assert.Equal("a,b", Names(home.Lan.Read(Db, "S")));
        Assert.Contains("đã tải bản", home.Drive.SyncNow());                      // đồng bộ lại theo máy chính
    }

    [Fact]
    public void Tai_khoan_khong_co_quyen_gui_len_thi_may_nha_chi_xem()
    {
        var (main, home) = Pair();
        main.Lan.Write(Db, One("S", Rows("a"))); main.Drive.SyncNow(); home.Drive.SyncNow();
        home.Auth.CreateUser("nhanvien", "NV", "matkhau1", KhoLanh.Core.Models.Roles.Staff,
            KhoLanh.Core.Models.Perm.Serialize(new Dictionary<string, string> { ["m1"] = "sua", ["drivepush"] = "" }));
        home.Auth.Logout(); home.Auth.Login("nhanvien", "matkhau1");
        var e = Assert.Throws<KhoLanh.Core.Services.BusinessException>(() => home.Lan.Write(Db, One("S", Rows("a", "x"))));
        Assert.Contains("chỉ được XEM", e.Message);
        Assert.Equal("a", Names(home.Lan.Read(Db, "S")));
        // tài khoản chỉnh quyền từ trước khi có mục này → vẫn được gửi (mặc định bật)
        home.Auth.Logout(); home.Auth.Login("admin", "matkhau1");
        home.Auth.CreateUser("nhanvien2", "NV2", "matkhau1", KhoLanh.Core.Models.Roles.Staff, "{\"m1\":\"sua\"}");
        home.Auth.Logout(); home.Auth.Login("nhanvien2", "matkhau1");
        // 1.76: tạo tài khoản / đăng nhập còn ghi mã điện thoại (db "dien-thoai") vào sổ chờ → đếm phần tăng của chính lần ghi bảng S
        var before = home.Lan.PendingCount();
        home.Lan.Write(Db, One("S", Rows("a", "y")));
        Assert.Equal(before + 1, home.Lan.PendingCount());
    }
}

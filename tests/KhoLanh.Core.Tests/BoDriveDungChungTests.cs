using System.Text.Json.Nodes;
using KhoLanh.Core.Lan;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.121: bỏ Thư mục / Drive dùng chung — máy cũ tự chuyển lúc mở app (<see cref="DriveRetire"/>).</summary>
public class BoDriveDungChungTests : IDisposable
{
    private const string Db = "ton-kho-an-an";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "klcs-bodrv-" + Guid.NewGuid().ToString("N"));
    private readonly string _drive;
    private readonly List<AppServices> _live = new();

    public BoDriveDungChungTests() { _drive = Path.Combine(_root, "drive"); Directory.CreateDirectory(_drive); }
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
    public void Bo_drive_dung_chung_may_chinh_nhan_not_thay_doi_may_xa_roi_tat_khong_xoa_tep_tren_Drive()
    {
        var (main, remote) = Pair();
        main.Lan.Write(Db, One("S", Rows("a"))); main.Drive.SyncNow(); remote.Drive.SyncNow();
        remote.Lan.Write(Db, One("S", Rows("a", "xa")));
        Assert.Contains("đã gửi 1 bảng", remote.Drive.SyncNow());             // thay đổi máy xa nằm trong thư mục, máy chính chưa nhận

        var r = DriveRetire.Run(main, () => new DateTime(2026, 11, 26, 8, 0, 0));
        Assert.NotNull(r);
        Assert.Equal(DriveSyncService.ModeMain, r!.OldMode);
        Assert.Equal("", r.Snapshot);
        Assert.Equal("a,xa", Names(main.Lan.Read(Db, "S")));                // lượt cuối đã nhận nốt thay đổi của máy xa
        Assert.Equal("", main.Settings.Get("drive_mode"));
        Assert.False(main.Drive.Enabled);                                       // không còn chạy nền
        Assert.Equal("2026-11-26T08:00:00", main.Settings.Get(DriveRetire.DoneKey));
        Assert.True(File.Exists(Path.Combine(_drive, DriveSyncService.Folder, "latest.json")));   // tệp trên Drive giữ nguyên
        Assert.Contains(main.Audit.Recent(50), e => e.Action == DriveRetire.AuditAction);
        Assert.Null(DriveRetire.Run(main));                                     // chạy lại: không còn gì để chuyển
        main.Drive.Start(); Assert.False(main.Drive.Enabled);
    }

    [Fact]
    public void Bo_drive_dung_chung_may_xem_tu_xa_chup_du_lieu_giu_so_cho_roi_thanh_may_don()
    {
        var (main, remote) = Pair();
        main.Lan.Write(Db, One("S", Rows("a"))); main.Drive.SyncNow(); remote.Drive.SyncNow();
        remote.Lan.Write(Db, One("S", Rows("a", "nha")));                      // chưa gửi qua Drive
        var pend = remote.Lan.PendingCount();
        Assert.True(pend > 0);
        Assert.Equal(LanService.RoleRemote, remote.Lan.Role);

        var r = DriveRetire.Run(remote, () => new DateTime(2026, 11, 26, 9, 30, 5));
        Assert.NotNull(r);
        Assert.Equal(DriveSyncService.ModeRemote, r!.OldMode);
        Assert.Equal("truoc-bo-drive_2026-11-26_093005.db", Path.GetFileName(r.Snapshot));
        Assert.True(File.Exists(r.Snapshot));
        Assert.Contains(remote.Backup.ListAutoBackups(), f => f.Name == "truoc-bo-drive_2026-11-26_093005.db");
        Assert.Equal("", remote.Settings.Get("drive_mode"));
        Assert.Equal("", remote.Settings.Get("lan_role"));
        Assert.Equal(LanService.RoleSingle, remote.Lan.Role);                  // phần LAN đọc lại vai: máy đơn, chờ kế hoạch LAN lúc đăng nhập
        Assert.False(remote.Lan.Enabled);
        Assert.False(remote.Drive.Enabled);
        Assert.Equal("a,nha", Names(remote.Lan.Read(Db, "S")));               // dữ liệu trên máy giữ nguyên
        Assert.Equal(pend, remote.Lan.PendingCount());                          // sổ chờ giữ — gửi lên máy chủ khi nối LAN / Internet
        Assert.False(Directory.Exists(Path.Combine(_drive, DriveSyncService.Folder, "in"))
                     && Directory.GetFiles(Path.Combine(_drive, DriveSyncService.Folder, "in")).Length > 0);   // không gửi thêm qua Drive
        Assert.Contains(remote.Audit.Recent(50), e => e.Action == DriveRetire.AuditAction);
        Assert.Equal("a", Names(main.Lan.Read(Db, "S")));
    }

    [Fact]
    public void Bo_drive_dung_chung_may_khong_dung_Drive_khong_doi_gi()
    {
        var s = Machine("don");
        Assert.False(DriveRetire.Needed(s.Settings));
        Assert.Null(DriveRetire.Run(s));
        Assert.Null(s.RetireDriveSync());
        Assert.Equal("", s.Settings.Get(DriveRetire.DoneKey));
        Assert.DoesNotContain(s.Audit.Recent(50), e => e.Action == DriveRetire.AuditAction);
    }
}

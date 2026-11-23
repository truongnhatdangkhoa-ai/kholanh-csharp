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
}

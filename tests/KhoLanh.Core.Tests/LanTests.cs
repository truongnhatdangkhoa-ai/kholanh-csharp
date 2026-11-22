using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using KhoLanh.Core.Lan;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class SheetMergeTests
{
    private static string J(params string[] rows) => new JsonArray(rows.Select(r => (JsonNode)new JsonObject { ["a"] = r }).ToArray()).ToJsonString();

    [Fact]
    public void Different_rows_merge_cleanly()
    {
        var m = SheetMerge.ThreeWay(J("1", "2", "3"), J("1", "2", "3", "4"), J("0", "1", "3"));
        Assert.True(m.Clean);
        Assert.Equal(J("0", "1", "3", "4"), m.Merged);
    }

    [Fact]
    public void Same_row_edited_on_both_sides_is_a_conflict()
    {
        var m = SheetMerge.ThreeWay(J("1", "2"), J("1", "2x"), J("1", "2y"));
        Assert.False(m.Clean);
    }

    [Fact]
    public void Reload_is_a_conflict_and_already_applied_changes_are_clean()
    {
        Assert.False(SheetMerge.ThreeWay(J("1", "2"), J("5", "6"), J("1", "2", "3")).Clean);
        var m = SheetMerge.ThreeWay(J("1", "2"), J("1", "2x"), J("1", "2x", "9"));
        Assert.True(m.Clean);
        Assert.Equal(J("1", "2x", "9"), m.Merged);
    }
}

/// <summary>Nhiều "máy" chạy trong cùng tiến trình, nối nhau qua 127.0.0.1 (mỗi máy một cổng).</summary>
public class LanTests : IDisposable
{
    private const string Db = "ton-kho-an-an";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "klcs-lan-" + Guid.NewGuid().ToString("N"));
    private readonly List<AppServices> _live = new();

    public void Dispose()
    {
        foreach (var s in _live) s.Lan.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private record Machine(string Path, int Port);

    private Machine Setup(string name, string role, string? code)
    {
        var dir = System.IO.Path.Combine(_root, name);
        var path = System.IO.Path.Combine(dir, "kho.db");
        var port = FreePort();
        var s = new AppServices(path);
        s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        s.Auth.Login("admin", "matkhau1");
        s.Settings.Set("lan_addrs", $"127.0.0.1:{port}");
        s.Settings.Set("lan_name", name);
        LanService.Configure(s.Settings, s.Session, role, code, port);
        return new Machine(path, port);
    }

    private AppServices Boot(Machine m)
    {
        var s = new AppServices(m.Path);
        s.Auth.Login("admin", "matkhau1");
        s.Lan.TickMs = 40;
        s.Lan.Speed = 0.05;
        s.Lan.Start();
        _live.Add(s);
        return s;
    }

    private void Stop(AppServices s) { s.Lan.Dispose(); _live.Remove(s); }

    private static void Until(Func<bool> ok, string what, int seconds = 15)
    {
        var end = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < end) { if (ok()) return; Thread.Sleep(30); }
        Assert.Fail("Hết giờ chờ: " + what);
    }

    private static JsonArray Rows(params string[] names) => new(names.Select(n => (JsonNode)new JsonObject { ["ma"] = n }).ToArray());
    private static Dictionary<string, JsonArray?> One(string sheet, JsonArray? rows) => new() { [sheet] = rows };
    private static string Names(string json) => string.Join(",", JsonNode.Parse(json)!.AsArray().Select(r => r!["ma"]!.ToString()));
    private static string Local(AppServices s, string sheet) => Names(s.Modules.ReadVersioned(Db, sheet).Json);

    private (AppServices srv, AppServices cli, Machine ms, Machine mc) Pair()
    {
        var ms = Setup("chu", LanService.RoleServer, null);
        var srv = Boot(ms);
        Until(() => srv.Lan.Serving, "máy chủ lên");
        var mc = Setup("con", LanService.RoleClient, srv.Lan.ConnectionCode());
        var cli = Boot(mc);
        Until(() => cli.Lan.Online, "máy con nối máy chủ");
        return (srv, cli, ms, mc);
    }

    [Fact]
    public void Client_reads_and_writes_the_server_directly()
    {
        var (srv, cli, _, _) = Pair();
        cli.Lan.Write(Db, One("S", Rows("a", "b")));
        Assert.Equal("a,b", Local(srv, "S"));
        srv.Lan.Write(Db, One("T", Rows("x")));
        Assert.Equal("x", Names(cli.Lan.Read(Db, "T")));
        Assert.Contains("T", cli.Lan.List(Db));
        // xóa bảng cũng lan sang
        cli.Lan.Write(Db, One("T", null));
        Assert.DoesNotContain("T", srv.Modules.ListSheets(Db));
    }

    [Fact]
    public void Second_saver_of_a_changed_sheet_is_told_to_reload()
    {
        var (srv, cli, _, _) = Pair();
        srv.Lan.Write(Db, One("S", Rows("a")));
        cli.Lan.Read(Db, "S");
        srv.Lan.Read(Db, "S");
        srv.Lan.Write(Db, One("S", Rows("a", "srv")));
        var ex = Assert.Throws<SheetConflictException>(() => cli.Lan.Write(Db, One("S", Rows("a", "cli"))));
        Assert.Contains("F5", ex.Message);
        Assert.Equal("a,srv", Local(srv, "S"));
        cli.Lan.Read(Db, "S");
        cli.Lan.Write(Db, One("S", Rows("a", "srv", "cli")));
        Assert.Equal("a,srv,cli", Local(srv, "S"));
    }

    [Fact]
    public void Offline_client_keeps_working_and_syncs_when_the_server_returns()
    {
        var (srv, cli, ms, _) = Pair();
        cli.Lan.Write(Db, One("S", Rows("a", "b")));
        cli.Lan.Write(Db, One("U", Rows("u1", "u2")));
        cli.Lan.Write(Db, One("W", Rows("w1", "w2")));
        Stop(srv);

        // máy chủ tắt: máy con làm trên database riêng + sổ chờ
        cli.Lan.Write(Db, One("S", Rows("a", "b", "c")));       // chỉ máy con đổi → lấy thẳng
        cli.Lan.Write(Db, One("U", Rows("u1", "u2", "u3")));    // hai bên đổi khác dòng → tự gộp
        cli.Lan.Write(Db, One("W", Rows("w1", "w2-con")));      // hai bên sửa cùng dòng → Quản trị chọn
        Assert.False(cli.Lan.Online);
        Assert.Equal(3, cli.Lan.PendingCount());
        Assert.Equal("a,b,c", Names(cli.Lan.Read(Db, "S")));

        // trong lúc đó trên máy chủ (người dùng ngay tại máy chủ) cũng sửa
        var off = new AppServices(ms.Path);
        off.Modules.WriteSheets(Db, One("U", Rows("u0", "u1", "u2")));
        off.Modules.WriteSheets(Db, One("W", Rows("w1", "w2-chu")));

        var srv2 = Boot(ms);
        Until(() => cli.Lan.PendingCount() == 0 && cli.Lan.Online, "máy con gửi sổ chờ");
        Assert.Equal("a,b,c", Local(srv2, "S"));
        Assert.Equal("u0,u1,u2,u3", Local(srv2, "U"));
        Assert.Equal("w1,w2-chu", Local(srv2, "W"));
        Assert.Equal("u0,u1,u2,u3", Local(cli, "U")); // máy con nhận bản đã gộp

        var conflicts = srv2.Lan.Conflicts();
        Assert.Contains(conflicts, c => c.Sheet == "U" && c.Status == "gop");
        var w = Assert.Single(conflicts, c => c.Sheet == "W");
        Assert.Equal("cho", w.Status);
        srv2.Lan.Resolve(w.Id, "maycon");
        Assert.Equal("w1,w2-con", Local(srv2, "W"));
        Until(() => Local(cli, "W") == "w1,w2-con" || Names(cli.Lan.Read(Db, "W")) == "w1,w2-con", "máy con thấy bản đã chọn");
    }

    [Fact]
    public void Standby_takes_over_and_hands_back_to_the_main_server()
    {
        var ms = Setup("chu", LanService.RoleServer, null);
        var srv = Boot(ms);
        Until(() => srv.Lan.Serving, "máy chủ lên");
        var code = srv.Lan.ConnectionCode();
        var mb = Setup("duphong", LanService.RoleStandby, code);
        var mc = Setup("con", LanService.RoleClient, code);
        var sb = Boot(mb);
        var cli = Boot(mc);
        Until(() => cli.Lan.Online && sb.Lan.Online, "nối máy chủ");
        cli.Lan.Write(Db, One("S", Rows("a")));
        Until(() => Local(sb, "S") == "a", "máy dự phòng chép dữ liệu");
        Assert.False(sb.Lan.Serving);

        Stop(srv);
        Until(() => sb.Lan.Serving, "máy dự phòng lên làm chủ");
        Until(() => cli.Lan.Online && cli.Lan.ServerName == "duphong", "máy con chuyển sang máy dự phòng");
        cli.Lan.Read(Db, "S");
        cli.Lan.Write(Db, One("S", Rows("a", "b")));
        Assert.Equal("a,b", Local(sb, "S"));
        Assert.Equal(0, cli.Lan.PendingCount());

        var srv2 = Boot(ms);
        Until(() => srv2.Lan.Serving && !sb.Lan.Serving, "máy chủ chính nhận lại vai", 30);
        Until(() => Local(srv2, "S") == "a,b", "máy chủ chính có phần sửa lúc nó tắt");
        Until(() => cli.Lan.Online && cli.Lan.ServerName == "chu", "máy con về máy chủ chính", 30);
        cli.Lan.Read(Db, "S");
        cli.Lan.Write(Db, One("S", Rows("a", "b", "c")));
        Assert.Equal("a,b,c", Local(srv2, "S"));
        Until(() => Local(sb, "S") == "a,b,c", "máy dự phòng chép lại");
    }

    [Fact]
    public void Two_servers_after_a_split_merge_into_one()
    {
        var ms = Setup("chu", LanService.RoleServer, null);
        var srv = Boot(ms);
        Until(() => srv.Lan.Serving, "máy chủ lên");
        srv.Lan.Write(Db, One("S", Rows("a")));
        var mb = Setup("duphong", LanService.RoleStandby, srv.Lan.ConnectionCode());
        var sb = Boot(mb);
        Until(() => Local(sb, "S") == "a", "máy dự phòng chép");

        // đứt mạng: hai bên cùng làm chủ, mỗi bên sửa một phần
        Stop(srv);
        Until(() => sb.Lan.Serving, "máy dự phòng lên làm chủ");
        sb.Lan.Write(Db, One("B", Rows("b1")));
        Stop(sb);
        var srv2 = Boot(ms);
        Until(() => srv2.Lan.Serving, "máy chủ chính làm chủ một mình");
        srv2.Lan.Write(Db, One("A", Rows("a1")));

        // nối lại
        var sb2 = Boot(mb);
        Until(() => (srv2.Lan.Serving ^ sb2.Lan.Serving) && srv2.Lan.PendingCount() == 0 && sb2.Lan.PendingCount() == 0, "còn một máy chủ", 30);
        var win = srv2.Lan.Serving ? srv2 : sb2;
        Until(() => win.Modules.ListSheets(Db).Contains("A") && win.Modules.ListSheets(Db).Contains("B"), "gộp đủ hai phần", 30);
        Assert.Equal("a1", Local(win, "A"));
        Assert.Equal("b1", Local(win, "B"));
        Assert.Equal("a", Local(win, "S"));
    }
}

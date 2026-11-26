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
    public async Task Plain_old_clients_work_unless_encryption_is_required()
    {
        var ms = Setup("chu", LanService.RoleServer, null);
        var srv = Boot(ms);
        Until(() => srv.Lan.Serving, "máy chủ lên");
        var key = srv.Lan.Key;
        var ep = $"127.0.0.1:{ms.Port}";
        var ping = new JsonObject { ["op"] = "ping", ["k"] = key, ["id"] = "x", ["r"] = "con" };
        using (var old = new LanConnection(ep))           // máy con bản cũ: kênh thường
            Assert.NotNull(await old.CallAsync(ping, TimeSpan.FromSeconds(5)));
        using (var neu = new LanConnection(ep, key, true))
        { Assert.NotNull(await neu.CallAsync(ping, TimeSpan.FromSeconds(5))); Assert.True(neu.Encrypted); }
        srv.Settings.Set("lan_enc", "1");                 // bắt buộc mã hóa
        using (var old2 = new LanConnection(ep))
            await Assert.ThrowsAsync<LanUnreachableException>(() => old2.CallAsync(ping, TimeSpan.FromSeconds(3)));
        using (var neu2 = new LanConnection(ep, key, true))
            Assert.NotNull(await neu2.CallAsync(ping, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task May_o_xa_noi_may_chu_qua_duong_ham_https()
    {
        // 1.104: máy chủ ở nhà + Cloudflare Tunnel → máy con ở xa chỉ nối được địa chỉ từ xa (http(s)://…), không vào được IP trong nhà
        var ms = Setup("chu", LanService.RoleServer, null);
        var httpPort = FreePort();
        {
            var s0 = new AppServices(ms.Path);
            s0.Settings.Set("lan_addrs", "127.0.0.1:1");   // địa chỉ "trong nhà" máy ở xa không tới được
            s0.Settings.Set("lan_http_port", httpPort.ToString());
            LanService.SetPublicUrl(s0.Settings, $"http://127.0.0.1:{httpPort}/");
        }
        var srv = Boot(ms);
        Until(() => srv.Lan.Serving, "máy chủ lên");
        var (addrs, _) = LanService.ParseCode(srv.Lan.ConnectionCode());
        Assert.Equal(new[] { "127.0.0.1:1", $"http://127.0.0.1:{httpPort}" }, addrs);
        var mc = Setup("con", LanService.RoleClient, srv.Lan.ConnectionCode());
        var cli = Boot(mc);
        Until(() => cli.Lan.Online, "máy con nối máy chủ qua đường hầm");
        Assert.Equal($"http://127.0.0.1:{httpPort}", cli.Lan.ServerAddress);
        cli.Lan.Write(Db, One("S", Rows("a", "b")));
        Assert.Equal("a,b", Local(srv, "S"));
        srv.Lan.Write(Db, One("T", Rows("x")));
        Assert.Equal("x", Names(cli.Lan.Read(Db, "T")));
        Assert.True(srv.Lan.TunnelLastOk > DateTime.Now.AddMinutes(-1));
        Assert.Contains("đang làm máy chủ", await srv.Lan.TestPublicAsync());
    }

    [Fact]
    public async Task Duong_ham_chan_sai_ma_lan_va_goi_lai_goi_tin_cu()
    {
        var key = "khoa-that"; var guard = new LanHttp.ReplayGuard();
        var req = new JsonObject { ["op"] = "hello", ["k"] = key };
        var (body, nonce) = LanHttp.SealRequest(key, req);
        var open = LanHttp.OpenRequest(key, body, guard);
        Assert.NotNull(open); Assert.Equal("hello", open!.Value.Req["op"]!.ToString()); Assert.Null(open.Value.Req["ts"]);
        Assert.Null(LanHttp.OpenRequest(key, body, guard));                              // gửi lại đúng gói cũ
        Assert.Null(LanHttp.OpenRequest("khoa-sai", LanHttp.SealRequest(key, req).Body, guard));
        Assert.Null(LanHttp.OpenRequest(key, LanHttp.SealRequest(key, req, DateTime.UtcNow.AddMinutes(-10)).Body, guard)); // gói quá hạn
        var res = LanHttp.SealResponse(key, nonce, new JsonObject { ["ok"] = true });
        Assert.True(LanHttp.OpenResponse(key, nonce, res)!["ok"]!.GetValue<bool>());
        Assert.Null(LanHttp.OpenResponse("khoa-sai", nonce, res));
        Assert.Equal("https://kho.kcenter.xyz", LanHttp.NormUrl(" kho.kcenter.xyz/ "));
        Assert.Equal("https://kho.kcenter.xyz", LanHttp.NormUrl("https://kho.kcenter.xyz/"));
        Assert.Null(LanHttp.NormUrl("kho kcenter"));
        // máy chủ thật: sai khóa → 403 → máy con báo không nối được (không lộ dữ liệu)
        var port = FreePort();
        using var srv = new LanHttpServer(port, () => key, (r, _) => new JsonObject { ["ok"] = true, ["op"] = r["op"]!.ToString() });
        using var good = new HttpLanConnection($"http://127.0.0.1:{port}", key);
        Assert.Equal("hello", (await good.CallAsync(req, TimeSpan.FromSeconds(5)))["op"]!.ToString());
        using var bad = new HttpLanConnection($"http://127.0.0.1:{port}", "khoa-sai");
        var ex = await Assert.ThrowsAsync<LanUnreachableException>(() => bad.CallAsync(req, TimeSpan.FromSeconds(5)));
        Assert.Contains("từ chối", ex.Message);
        using var http = new HttpClient();
        Assert.Equal("KhoLanh OK", await http.GetStringAsync($"http://127.0.0.1:{port}/"));
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
        // tiền đề của kịch bản: dữ liệu ban đầu đã lên máy chủ (máy chậm có thể đang nằm trong sổ chờ) — bản gốc chung để gộp sau này
        Until(() => cli.Lan.PendingCount() == 0 && Local(srv, "S") == "a,b" && Local(srv, "U") == "u1,u2" && Local(srv, "W") == "w1,w2", "dữ liệu ban đầu lên máy chủ");
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

    /// <summary>
    /// Lỗi CI cũ (S thành "a,b,c,a,b"): lượt Pull tải lại toàn bộ (since = 0) nhận phản hồi TRƯỚC khi máy con ghi "a,b",
    /// áp sau → xóa bảng vừa ghi ở database riêng → mất máy chủ, sổ chờ lấy bản gốc rỗng → gộp 3 bên thêm "a,b" lần nữa.
    /// Chặn đúng khe đó để tái hiện chắc chắn.
    /// </summary>
    [Fact]
    public async Task Stale_pull_response_does_not_undo_a_write_made_while_it_was_in_flight()
    {
        var (srv, cli, ms, _) = Pair();
        cli.Lan.Write(Db, One("R", Rows("r1")));
        Until(() => cli.Lan.PendingCount() == 0, "ghi R");

        var arrived = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var armed = 1;
        cli.Lan.TestAfterPullResponse = async () =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            arrived.TrySetResult();
            await release.Task;
        };
        cli.Settings.Set("lan_sync_epoch", ""); // lượt Pull sau tải lại toàn bộ
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // phản hồi đã có (máy chủ chưa có S, R = r1) — giờ máy con ghi
        cli.Lan.Write(Db, One("S", Rows("a", "b")));
        cli.Lan.Write(Db, One("R", Rows("r1", "r2")));
        Assert.Equal(0, cli.Lan.PendingCount());
        var before = cli.Lan.PullsApplied;
        release.SetResult();
        Until(() => cli.Lan.PullsApplied > before, "lượt Pull bị chặn áp xong");
        Assert.Equal("a,b", Local(cli, "S"));
        Assert.Equal("r1,r2", Local(cli, "R"));

        Stop(srv);
        cli.Lan.Write(Db, One("S", Rows("a", "b", "c")));
        Assert.False(cli.Lan.Online);
        var srv2 = Boot(ms);
        Until(() => cli.Lan.PendingCount() == 0 && cli.Lan.Online, "máy con gửi sổ chờ");
        Assert.Equal("a,b,c", Local(srv2, "S"));
        Assert.Equal("r1,r2", Local(srv2, "R"));
        Assert.DoesNotContain(srv2.Lan.Conflicts(), c => c.Sheet == "S");
    }

    [Fact]
    public void Client_learns_standby_from_a_write_before_its_next_pull()
    {
        // 1.50: máy con biết máy dự phòng ngay từ câu trả lời lượt ghi — trước đây chỉ biết ở lượt "changes" (mỗi 15 s),
        // máy chủ tắt ngay sau khi máy dự phòng vừa nối thì máy con không tìm được máy chủ mới (CI đỏ ở Standby_takes_over…).
        var ms = Setup("chu", LanService.RoleServer, null);
        var srv = Boot(ms);
        Until(() => srv.Lan.Serving, "máy chủ lên");
        var code = srv.Lan.ConnectionCode();
        var cli = Boot(Setup("con", LanService.RoleClient, code));
        Until(() => cli.Lan.Online, "máy con nối");
        cli.Lan.TickMs = 600_000;          // máy con thôi tự kéo "changes"
        Thread.Sleep(300);                 // chờ vòng đang chạy dở kết thúc
        var sb = Boot(Setup("duphong", LanService.RoleStandby, code));
        Until(() => sb.Lan.Online, "máy dự phòng nối");
        Assert.DoesNotContain("duphong", cli.Settings.Get("lan_nodes", "[]"));
        cli.Lan.Write(Db, One("S", Rows("a")));
        Assert.Contains("duphong", cli.Settings.Get("lan_nodes", "[]"));
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

    /// <summary>
    /// Lỗi chập chờn CI 24/11 ("a,b,c,a,b"): máy con gửi sổ chờ, máy chủ áp xong nhưng câu trả lời bị mất (máy chủ tắt đúng lúc) →
    /// máy con sửa tiếp bảng đó → mục chờ mới vẫn mang bản gốc cũ → máy chủ gộp 3 bên sai, nhân đôi dòng.
    /// Nay sổ chờ nhớ lần gửi trước (đã đánh dấu gửi); máy chủ thấy lần đó đã áp thì lấy nội dung lần đó làm bản gốc.
    /// </summary>
    [Fact]
    public void Resent_pending_after_lost_reply_is_not_duplicated()
    {
        var (srv, cli, _, _) = Pair();
        Stop(srv);
        cli.Lan.Write(Db, One("S", Rows("a", "b")));                 // máy chủ tắt → vào sổ chờ (bản gốc: chưa có bảng)
        Until(() => !cli.Lan.Online || cli.Lan.PendingCount() > 0, "vào sổ chờ");
        var first = Assert.Single(cli.Lan.PendingItems());           // "gửi" lần 1 (đánh dấu đã gửi)
        Assert.Null(first["prev"]);
        var ms2 = Setup("chu2", LanService.RoleServer, null);
        var srv2 = Boot(ms2);
        Until(() => srv2.Lan.Serving, "máy chủ 2 lên");
        first["u"] = "admin";
        Assert.Equal("ok", srv2.Lan.ApplyExternal(new[] { first }, "may-con").Single()["st"]!.ToString());   // máy chủ áp xong, máy con không biết
        Assert.Equal("a,b", Local(srv2, "S"));

        cli.Lan.Write(Db, One("S", Rows("a", "b", "c")));            // người dùng sửa tiếp khi chưa có trả lời
        var second = Assert.Single(cli.Lan.PendingItems());
        Assert.Equal(first["op"]!.ToString(), second["prev"]![0]!["op"]!.ToString());
        second["u"] = "admin";
        srv2.Lan.ApplyExternal(new[] { second }, "may-con");
        Assert.Equal("a,b,c", Local(srv2, "S"));                      // trước đây: "a,b,c,a,b"

        // gửi lại đúng mục đã áp → bỏ qua (không áp 2 lần)
        Assert.Equal("trung", srv2.Lan.ApplyExternal(new[] { second }, "may-con").Single()["st"]!.ToString());
        Assert.Equal("a,b,c", Local(srv2, "S"));
    }
}

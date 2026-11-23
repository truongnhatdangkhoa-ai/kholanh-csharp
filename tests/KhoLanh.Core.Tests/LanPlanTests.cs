using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using KhoLanh.Core.Lan;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Phân vai mạng LAN theo tài khoản: Quản trị gán vai trên Google Sheet, máy tự nhận vai lúc đăng nhập.</summary>
public class LanPlanTests : IDisposable
{
    private const string Url = "https://script.google.com/macros/s/ABC123/exec";
    private const string Db = "ton-kho-an-an";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-lanplan-" + Guid.NewGuid().ToString("N"));
    private readonly AccountSyncTests.FakeWebApp _web = new();
    private readonly List<AppServices> _live = new();

    public void Dispose()
    {
        foreach (var s in _live) s.Lan.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static void Until(Func<bool> ok, string what, int seconds = 20)
    {
        var end = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < end) { if (ok()) return; Thread.Sleep(30); }
        Assert.Fail("Hết giờ chờ: " + what);
    }

    private static string Between(string s, string a, string b) { var i = s.IndexOf(a) + a.Length; return s[i..s.IndexOf(b, i)]; }

    private AppServices Machine(string name)
    {
        var s = new AppServices(Path.Combine(_dir, name, "kho.db"));
        var acc = new AccountSyncService(s.Db, s.Settings, s.Session, s.Audit, _web);
        s.Auth.Sync = acc; acc.Updates = s.Updates; s.Accounts = acc;
        var port = FreePort();
        s.Settings.Set("lan_addrs", $"127.0.0.1:{port}");
        s.Settings.Set("lan_port", port.ToString());
        s.Settings.Set("lan_name", name);
        s.ReloadLan(start: false);
        s.Lan.TickMs = 40; s.Lan.Speed = 0.05;
        _live.Add(s);
        return s;
    }

    /// <summary>Đăng nhập như LoginWindow: tải danh sách (kèm kế hoạch LAN) → đăng nhập → tự nhận vai.</summary>
    private static async Task<string?> Login(AppServices s, string user, string pass)
    {
        if (s.Session.CurrentUser is not null) s.Auth.Logout();
        await s.Accounts.PullAsync();
        s.Auth.Login(user, pass);
        var msg = LanPlanSync.ApplyAtLogin(s);
        if (!s.Lan.Enabled || msg is null) s.Lan.Start();
        return msg;
    }

    private static JsonArray Rows(params string[] names) => new(names.Select(n => (JsonNode)new JsonObject { ["ma"] = n }).ToArray());
    private static string Local(AppServices s, string sheet) => string.Join(",", JsonNode.Parse(s.Modules.ReadVersioned(Db, sheet).Json)!.AsArray().Select(r => r!["ma"]!.ToString()));

    [Fact]
    public void Decide_keeps_roles_with_the_machine()
    {
        var p = new LanPlan { On = true };
        p.Vai["admin"] = "chu"; p.Vai["quanly"] = "duphong";
        // chưa có máy chủ: tài khoản được gán máy chủ nhận vai; tài khoản khác → máy đơn
        Assert.Equal(("chu", true), (p.Decide("admin", "A", "")!.Role, p.Decide("admin", "A", "")!.ClaimServer));
        Assert.Equal("", p.Decide("nhanvien", "C", "")!.Role);
        Assert.Equal("", p.Decide("quanly", "B", "")!.Role); // dự phòng cần có máy chủ trước
        p.Chu = new LanPlan.Slot("A", "May A", new() { "10.0.0.1:47820" }, ""); p.Key = "k";
        // máy chủ giữ vai dù ai đăng nhập
        Assert.Equal("chu", p.Decide("nhanvien", "A", "chu")!.Role);
        // quản trị đăng nhập máy khác → máy con
        Assert.Equal("con", p.Decide("admin", "C", "")!.Role);
        // máy chủ cũ (cài tay) mà kế hoạch ghi máy khác → máy con
        Assert.Equal("con", p.Decide("nhanvien", "X", "chu")!.Role);
        Assert.True(p.Decide("quanly", "B", "")!.ClaimStandby);
        p.DuPhong = new LanPlan.Slot("B", "May B", new() { "10.0.0.2:47820" }, "");
        Assert.Equal("duphong", p.Decide("nhanvien", "B", "con")!.Role);
        Assert.Equal("con", p.Decide("quanly", "D", "")!.Role);
        // máy dự phòng bị Quản trị bỏ → lần đăng nhập sau là máy con (trừ khi người đăng nhập được gán dự phòng)
        p.DuPhong = null;
        Assert.Equal("con", p.Decide("nhanvien", "B", "duphong")!.Role);
        Assert.Null(new LanPlan().Decide("admin", "A", "chu"));
        // đọc / ghi JSON
        var q = LanPlan.Parse(p.ToJson())!;
        Assert.True(q.On); Assert.Equal("May A", q.Chu!.Name); Assert.Equal("duphong", q.RoleOf("QUANLY")); Assert.Equal("con", q.RoleOf("nhanvien"));
    }

    [Fact]
    public async Task Machines_take_roles_by_account_and_admin_moves_the_server()
    {
        // Máy A (quản trị) kết nối Google Sheet, tạo tài khoản quản lý + nhân viên.
        var a = Machine("A");
        a.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        a.Auth.Login("admin", "matkhau1");
        var script = a.Accounts.BuildScript();
        _web.Token = Between(script, "var MA_DOC = '", "'");
        _web.Admin = Between(script, "var MA_QUAN_TRI = '", "'");
        await a.Accounts.ConnectAsync(Url);
        a.Auth.CreateUser("quanly", "Quản lý A", "matkhau2", Roles.Manager, "");
        a.Auth.CreateUser("nhanvien", "Nhân viên", "matkhau3", Roles.Staff, Perm.Serialize(new Dictionary<string, string> { ["m1"] = Perm.Edit }));
        var staffCode = a.Accounts.ConnectionCode(forAdmin: false);
        var adminCode = a.Accounts.ConnectionCode(forAdmin: true);
        a.Modules.WriteRaw(Db, new Dictionary<string, string?> { ["S"] = Rows("a").ToJsonString() }, null, "admin");

        // Quản trị gán vai: admin = máy chủ, ql = máy dự phòng; nhân viên không gán (máy con).
        await a.Accounts.UpdatePlanAsync(p => { p.On = true; p.Vai["admin"] = "chu"; p.Vai["quanly"] = "duphong"; return true; });
        Assert.Contains(_web.Rows, r => r["username"] == LanPlan.RowName);
        Assert.Equal(3, a.Auth.ListUsers().Count);          // dòng "#mang-lan" không thành tài khoản

        Assert.NotNull(await Login(a, "admin", "matkhau1"));
        Assert.Equal(LanService.RoleServer, a.Lan.Role);
        Until(() => a.Lan.Serving, "máy A lên làm máy chủ");
        Assert.Null(await LanPlanSync.PublishAsync(a));
        var plan = LanPlan.Parse(_web.Rows.Single(r => r["username"] == LanPlan.RowName)["quyen"])!;
        Assert.Equal(a.Lan.MachineId, plan.Chu!.Id);
        Assert.Equal(a.Lan.Key, plan.Key);

        // Máy B: quản lý A đăng nhập → máy dự phòng, không cần dán mã LAN.
        var b = Machine("B");
        await b.Accounts.ConnectAsync(staffCode);
        await Login(b, "quanly", "matkhau2");
        Assert.Equal(LanService.RoleStandby, b.Lan.Role);
        Until(() => b.Lan.Online && Local(b, "S") == "a", "máy B chép dữ liệu máy chủ");
        // máy chủ thấy máy dự phòng → ghi lên Sheet
        Until(() => a.Lan.Machines().Any(m => m.Role == LanService.RoleStandby), "máy chủ thấy máy dự phòng");
        Assert.Null(await LanPlanSync.PublishAsync(a));
        Assert.Equal(b.Lan.MachineId, a.Accounts.Plan!.DuPhong!.Id);

        // Máy C: nhân viên → máy con; ghi vào máy chủ.
        var c = Machine("C");
        await c.Accounts.ConnectAsync(adminCode);
        await Login(c, "nhanvien", "matkhau3");
        Assert.Equal(LanService.RoleClient, c.Lan.Role);
        Until(() => c.Lan.Online, "máy C nối máy chủ");
        c.Lan.Read(Db, "S");
        c.Lan.Write(Db, new Dictionary<string, JsonArray?> { ["S"] = Rows("a", "c") });
        Assert.Equal("a,c", Local(a, "S"));

        // Nhân viên đăng nhập trên máy A: máy A vẫn là máy chủ (giữ vai theo máy).
        Assert.Null(await Login(a, "nhanvien", "matkhau3"));
        Assert.True(a.Lan.Serving);
        // Quản trị đăng nhập máy C: đã có máy chủ → máy C vẫn là máy con.
        Assert.Null(await Login(c, "admin", "matkhau1"));
        Assert.Equal(LanService.RoleClient, c.Lan.Role);

        // Quản trị chuyển máy chủ về máy C: máy A trả vai khi C đã chép đủ, không mất dữ liệu.
        Until(() => c.Lan.Online, "máy C nối lại sau khi đăng nhập");
        var msg = await LanPlanSync.ClaimServerAsync(c);
        Assert.Contains("máy chủ chính", msg);
        Until(() => c.Lan.Serving && !a.Lan.Serving, "máy C nhận vai máy chủ", 30);
        Assert.Equal("a,c", Local(c, "S"));
        Assert.Equal(LanService.RoleClient, a.Settings.Get("lan_role"));
        Assert.Equal(c.Lan.MachineId, LanPlan.Parse(_web.Rows.Single(r => r["username"] == LanPlan.RowName)["quyen"])!.Chu!.Id);
        // máy A đăng nhập lại → máy con của máy C
        await Login(a, "nhanvien", "matkhau3");
        Assert.Equal(LanService.RoleClient, a.Lan.Role);
        Until(() => a.Lan.Online && a.Lan.ServerName == "C", "máy A nối máy chủ mới", 30);
    }

    [Fact]
    public async Task Client_without_server_works_alone_and_merges_later()
    {
        // Không nối được máy chủ / dự phòng: máy con làm tiếp trên dữ liệu trong máy (sổ chờ), có máy chủ lại thì tự gộp.
        var a = Machine("A");
        a.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        a.Auth.Login("admin", "matkhau1");
        var script = a.Accounts.BuildScript();
        _web.Token = Between(script, "var MA_DOC = '", "'");
        _web.Admin = Between(script, "var MA_QUAN_TRI = '", "'");
        await a.Accounts.ConnectAsync(Url);
        a.Auth.CreateUser("nhanvien", "Nhân viên", "matkhau3", Roles.Staff, Perm.Serialize(new Dictionary<string, string> { ["m1"] = Perm.Edit }));
        var code = a.Accounts.ConnectionCode(forAdmin: false);
        await a.Accounts.UpdatePlanAsync(p => { p.On = true; p.Vai["admin"] = "chu"; return true; });
        await Login(a, "admin", "matkhau1");
        Until(() => a.Lan.Serving, "máy chủ lên");
        Assert.Null(await LanPlanSync.PublishAsync(a));

        var c = Machine("C");
        await c.Accounts.ConnectAsync(code);
        await Login(c, "nhanvien", "matkhau3");
        Until(() => c.Lan.Online, "máy con nối");
        a.Lan.Dispose();                                     // máy chủ tắt
        _web.Down = true;                                    // mất cả Internet: đăng nhập dùng bản lưu trong máy
        await Assert.ThrowsAsync<BusinessException>(() => c.Accounts.PullAsync());
        c.Lan.Write(Db, new Dictionary<string, JsonArray?> { ["T"] = Rows("x") });
        Assert.Equal(1, c.Lan.PendingCount());
        Assert.Equal("x", Local(c, "T"));

        a.ReloadLan();                                        // máy chủ bật lại
        Until(() => a.Lan.Serving && c.Lan.PendingCount() == 0, "gộp sổ chờ", 30);
        Assert.Equal("x", Local(a, "T"));
    }
}

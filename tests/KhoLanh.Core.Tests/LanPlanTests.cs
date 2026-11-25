using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using KhoLanh.Core.Lan;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Mạng LAN tự nối (1.106: vai theo MÁY — máy chủ / dự phòng chọn bằng nút ở chính máy đó; máy khác đăng nhập là tự thành máy con).</summary>
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
        await s.Accounts.PublishTask;
        var msg = LanPlanSync.ApplyAtLogin(s);
        if (!s.Lan.Enabled || msg is null) s.Lan.Start();
        return msg;
    }

    private static JsonArray Rows(params string[] names) => new(names.Select(n => (JsonNode)new JsonObject { ["ma"] = n }).ToArray());
    private static string Local(AppServices s, string sheet) => string.Join(",", JsonNode.Parse(s.Modules.ReadVersioned(Db, sheet).Json)!.AsArray().Select(r => r!["ma"]!.ToString()));

    [Fact]
    public void Decide_keeps_roles_with_the_machine()
    {
        // 1.106: không cài tay — máy chủ do Quản trị bật (ghi theo máy), dự phòng theo quyền "Máy dự phòng LAN", còn lại máy con.
        var p = new LanPlan();
        p.Vai["admin"] = "chu";                                                // gán vai cũ trên Sheet: bỏ qua
        Assert.Equal("", p.Decide("admin", "A", "")!.Role);                    // chưa bật máy chủ: máy đơn
        Assert.Equal(("chu", true), (p.Decide("x", "A", "chu")!.Role, p.Decide("x", "A", "chu")!.ClaimServer));
        Assert.Equal("", p.Decide("ql", "B", "", standbyAllowed: true)!.Role); // dự phòng cần có máy chủ trước
        p.Chu = new LanPlan.Slot("A", "May A", new() { "10.0.0.1:47820" }, ""); p.Key = "k";
        Assert.Equal("chu", p.Decide("nhanvien", "A", "chu")!.Role);           // máy chủ giữ vai dù ai đăng nhập
        Assert.Equal("con", p.Decide("admin", "C", "")!.Role);                 // Quản trị đăng nhập máy khác → máy con (bật máy chủ bằng nút)
        Assert.Equal("con", p.Decide("nhanvien", "X", "chu")!.Role);
        var sb = p.Decide("ql", "B", "", standbyAllowed: true)!;
        Assert.Equal(("duphong", true), (sb.Role, sb.ClaimStandby));
        Assert.Equal("con", p.Decide("ql", "B", "")!.Role);                    // không có quyền → máy con
        // máy chủ thấy máy dự phòng: chỉ ghi khi tài khoản trên máy đó có quyền dự phòng
        var standbys = new[] { ("B", "May B", new List<string> { "10.0.0.2:47820" }, "ql") };
        Assert.False(p.Fix("A", "May A", "chu", "admin", new() { "10.0.0.1:47820" }, "k", 0, standbys, _ => false));
        Assert.Null(p.DuPhong);
        Assert.True(p.Fix("A", "May A", "chu", "admin", new() { "10.0.0.1:47820" }, "k", 0, standbys, u => u == "ql"));
        Assert.Equal("duphong", p.Decide("nhanvien", "B", "con")!.Role);
        Assert.Equal("con", p.Decide("ql2", "D", "", standbyAllowed: true)!.Role);  // đã có máy dự phòng
        p.DuPhong = null;
        Assert.Equal("con", p.Decide("nhanvien", "B", "duphong")!.Role);
        var q = LanPlan.Parse(p.ToJson())!;
        Assert.Equal("May A", q.Chu!.Name);
        // quyền: chỉ khi tích riêng
        Assert.False(LanPlan.CanStandby(new User { Username = "a", Role = Roles.Admin, IsActive = true }));
        Assert.False(LanPlan.CanStandby(new User { Username = "m", Role = Roles.Manager, IsActive = true }));
        Assert.True(LanPlan.CanStandby(new User { Username = "m", Role = Roles.Manager, IsActive = true, Permissions = Perm.Serialize(new Dictionary<string, string> { [LanPlan.StandbyPerm] = Perm.Edit }) }));
        Assert.Equal(Perm.None, Perm.Defaults(Roles.Manager)[LanPlan.StandbyPerm]);
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
        a.Auth.CreateUser("quanly", "Quản lý A", "matkhau2", Roles.Manager, Perm.Serialize(new Dictionary<string, string> { [LanPlan.StandbyPerm] = Perm.Edit, ["m1"] = Perm.Edit }));
        a.Auth.CreateUser("nhanvien", "Nhân viên", "matkhau3", Roles.Staff, Perm.Serialize(new Dictionary<string, string> { ["m1"] = Perm.Edit }));
        var staffCode = a.Accounts.ConnectionCode(forAdmin: false);
        var adminCode = a.Accounts.ConnectionCode(forAdmin: true);
        a.Modules.WriteRaw(Db, new Dictionary<string, string?> { ["S"] = Rows("a").ToJsonString() }, null, "admin");

        // 1.106: Quản trị bấm "Bật máy chủ trên máy này" ở máy A (không cài tay IP / mã); quản lý có quyền Máy dự phòng LAN.
        await a.Accounts.UpdatePlanAsync(p => { p.On = true; return true; });
        Assert.Contains(_web.Rows, r => r["username"] == LanPlan.RowName);
        Assert.Equal(3, a.Auth.ListUsers().Count);          // dòng "#mang-lan" không thành tài khoản

        await Login(a, "admin", "matkhau1");
        await LanPlanSync.ClaimServerAsync(a);
        Assert.Equal(LanService.RoleServer, a.Lan.Role);
        Until(() => a.Lan.Serving, "máy A lên làm máy chủ");
        Assert.Null(await LanPlanSync.PublishAsync(a));
        var plan = LanPlan.Parse(_web.Rows.Single(r => r["username"] == LanPlan.RowName)["quyen"])!;
        Assert.Equal(a.Lan.MachineId, plan.Chu!.Id);
        Assert.Equal(a.Lan.Key, plan.Key);

        // Máy B: quản lý (có quyền Máy dự phòng LAN) đăng nhập → máy dự phòng, không cần dán mã LAN.
        var b = Machine("B");
        await b.Accounts.ConnectAsync(staffCode);
        await Login(b, "quanly", "matkhau2");
        Assert.Equal(LanService.RoleStandby, b.Lan.Role);
        Until(() => b.Lan.Online && Local(b, "S") == "a", "máy B chép dữ liệu máy chủ");
        // máy dự phòng đã ghi trên Sheet; máy chủ thấy nó, ghi lại không xóa
        Until(() => a.Lan.Machines().Any(m => m.Role == LanService.RoleStandby), "máy chủ thấy máy dự phòng");
        await a.Accounts.PullAsync();
        Assert.Null(await LanPlanSync.PublishAsync(a));
        Assert.Equal(b.Lan.MachineId, LanPlan.Parse(_web.Rows.Single(r => r["username"] == LanPlan.RowName)["quyen"])!.DuPhong!.Id);

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
        await a.Accounts.UpdatePlanAsync(p => { p.On = true; return true; });
        await Login(a, "admin", "matkhau1");
        await LanPlanSync.ClaimServerAsync(a);
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

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
        // 1.106: không cài tay — máy chủ do Quản trị bật (ghi theo máy), còn lại máy con.
        // 1.131 (người dùng: "bỏ máy dự phòng, chỉ dùng 1 máy chính"): không còn vai dự phòng — máy từng là dự phòng thành máy con.
        var p = new LanPlan();
        p.Vai["admin"] = "chu";                                                // gán vai cũ trên Sheet: bỏ qua
        Assert.Equal("", p.Decide("admin", "A", "")!.Role);                    // chưa bật máy chủ: máy đơn
        Assert.Equal(("chu", true), (p.Decide("x", "A", "chu")!.Role, p.Decide("x", "A", "chu")!.ClaimServer));
        p.Chu = new LanPlan.Slot("A", "May A", new() { "10.0.0.1:47820" }, ""); p.Key = "k";
        Assert.Equal("chu", p.Decide("nhanvien", "A", "chu")!.Role);           // máy chủ giữ vai dù ai đăng nhập
        Assert.Equal("con", p.Decide("admin", "C", "")!.Role);                 // Quản trị đăng nhập máy khác → máy con (bật máy chủ bằng nút)
        Assert.Equal("con", p.Decide("nhanvien", "X", "chu")!.Role);
        Assert.Equal("con", p.Decide("ql", "B", "")!.Role);
        // kế hoạch cũ còn ghi máy dự phòng B → B thành máy con; máy chủ ghi lại kế hoạch thì xóa ô dự phòng
        p.DuPhong = new LanPlan.Slot("B", "May B", new() { "10.0.0.2:47820" }, "");
        Assert.Equal("con", p.Decide("ql", "B", "duphong")!.Role);
        Assert.True(p.Fix("A", "May A", "chu", "admin", new() { "10.0.0.1:47820" }, "k", 0));
        Assert.Null(p.DuPhong);
        Assert.False(p.Fix("A", "May A", "chu", "admin", new() { "10.0.0.1:47820" }, "k", 0));
        var q = LanPlan.Parse(p.ToJson())!;
        Assert.Equal("May A", q.Chu!.Name);
        Assert.Null(q.DuPhong);
        // quyền "Máy dự phòng LAN" không còn trong Phân quyền
        Assert.DoesNotContain(Perm.Items, i => i.Key == "landuphong");
    }

    [Fact]
    public void May_du_phong_cu_tu_thanh_may_con_khi_mo_app()
    {
        // 1.131: máy còn vai dự phòng (kế hoạch cũ / cài tay) → lúc mở app thành máy con
        var b = Machine("B");
        b.Settings.Set("lan_role", LanService.RoleStandby);
        b.ReloadLan(start: false);
        Assert.Equal(LanService.RoleStandby, b.Lan.Role);
        Assert.NotNull(b.RetireStandby());
        Assert.Equal(LanService.RoleClient, b.Settings.Get("lan_role"));
        Assert.Equal(LanService.RoleClient, b.Lan.Role);
        Assert.Null(b.RetireStandby());                      // lần sau không đổi gì
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
        a.Auth.CreateUser("quanly", "Quản lý A", "matkhau2", Roles.Manager, Perm.Serialize(new Dictionary<string, string> { ["landuphong"] = Perm.Edit, ["m1"] = Perm.Edit }));   // quyền dự phòng cũ (1.106) còn lưu → bỏ qua
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

        // Máy B: quản lý (tài khoản còn lưu quyền "Máy dự phòng LAN" cũ) đăng nhập → 1.131: máy con, không còn máy dự phòng.
        var b = Machine("B");
        await b.Accounts.ConnectAsync(staffCode);
        await Login(b, "quanly", "matkhau2");
        Assert.Equal(LanService.RoleClient, b.Lan.Role);
        Until(() => b.Lan.Online && Local(b, "S") == "a", "máy B chép dữ liệu máy chủ");
        await a.Accounts.PullAsync();
        Assert.Null(await LanPlanSync.PublishAsync(a));
        Assert.Null(LanPlan.Parse(_web.Rows.Single(r => r["username"] == LanPlan.RowName)["quyen"])!.DuPhong);

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

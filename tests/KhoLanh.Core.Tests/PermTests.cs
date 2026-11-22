using System.Text.Json.Nodes;
using KhoLanh.Core;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class PermTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-perm-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public PermTests()
    {
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Auth(string page) =>
        JsonNode.Parse(_s.Bridge.Handle(new JsonObject { ["op"] = "authState", ["args"] = new JsonArray(page) }.ToJsonString()))!["user"]!["vaiTro"]!.GetValue<string>();

    private bool Write(string key) =>
        JsonNode.Parse(_s.Bridge.Handle(new JsonObject { ["op"] = "writeSheet", ["args"] = new JsonArray(key, "S", new JsonArray()) }.ToJsonString()))!["ok"]!.GetValue<bool>();

    [Fact]
    public void Role_defaults_apply_when_no_custom_permissions()
    {
        var staff = new User { Role = Roles.Staff };
        Assert.False(Perm.Has(staff, "backup", Perm.Edit));
        Assert.False(Perm.Has(staff, "audit"));
        Assert.Equal(Perm.Export, Perm.Level(staff, "m1"));
        Assert.True(Perm.Has(new User { Role = Roles.Manager }, "m2", Perm.Edit));
        Assert.False(Perm.Has(new User { Role = Roles.Manager }, "users"));
    }

    [Fact]
    public void Custom_permissions_limit_screens_and_module_writes()
    {
        var u = _s.Auth.CreateUser("kho1", "Kho", "matkhau1", Roles.Manager,
            Perm.Serialize(new Dictionary<string, string> { ["m1"] = Perm.Edit, ["m2"] = Perm.View, ["audit"] = Perm.View }));
        _s.Auth.Login("kho1", "matkhau1");
        Assert.True(_s.Session.Can("m1", Perm.Edit));
        Assert.False(_s.Session.Can("backup"));
        Assert.True(_s.Session.Can("audit"));
        Assert.Throws<BusinessException>(() => _s.Backup.ManualBackup(Path.Combine(_dir, "x.db")));
        Assert.True(Write("ton-kho-an-an"));
        Assert.False(Write("ton-kho-gui"));
        Assert.Equal("quanly", Auth("/html/ton-kho-an-an.html"));
        Assert.Equal("xem", Auth("/html/ton-kho-gui.html"));
    }

    [Fact]
    public void Clearing_custom_permissions_returns_to_role_defaults()
    {
        var u = _s.Auth.CreateUser("kho2", "Kho", "matkhau1", Roles.Staff, "{\"m1\":\"sua\"}");
        _s.Auth.SetPermissions(u.Id, "");
        _s.Auth.Login("kho2", "matkhau1");
        Assert.Equal(Perm.Export, Perm.Level(_s.Session.CurrentUser, "m1"));
        Assert.Equal("xuat", Auth("/html/ton-kho-an-an.html"));
    }

    [Fact]
    public void Invalid_levels_are_dropped()
    {
        var d = Perm.Parse("{\"in\":\"sua\",\"audit\":\"xuat\",\"m1\":\"xuat\",\"la\":\"sua\"}");
        Assert.False(d.ContainsKey("in"));   // mục cũ đã gỡ: bỏ qua
        Assert.False(d.ContainsKey("audit")); // mức không hợp lệ
        Assert.Equal("xuat", d["m1"]);
        Assert.False(d.ContainsKey("la"));
    }
}

public class LoginMemoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-nho-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Xunit.Fact]
    public void Remember_and_auto_login_settings()
    {
        var s = new AppServices(Path.Combine(_dir, "kho.db"));
        var m = s.Remember;
        Xunit.Assert.False(m.Remember);
        m.Save("nv1", "matkhau2", remember: false, auto: false);
        Xunit.Assert.Equal("nv1", m.Username); Xunit.Assert.False(m.Remember); Xunit.Assert.Equal("", m.Password);
        m.Save("nv1", "matkhau2", remember: false, auto: true); // tự đăng nhập kéo theo nhớ mật khẩu
        Xunit.Assert.True(m.Remember); Xunit.Assert.True(m.Auto); Xunit.Assert.Equal("matkhau2", m.Password);
        if (OperatingSystem.IsWindows()) Xunit.Assert.DoesNotContain("matkhau2", s.Settings.Get("login_pass")); // DPAPI
        m.Forget();
        Xunit.Assert.False(m.Remember); Xunit.Assert.False(m.Auto); Xunit.Assert.Equal("nv1", m.Username);
    }
}

using System.Text.Json.Nodes;
using KhoLanh.Core;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Xunit;

namespace KhoLanh.Core.Tests;

public class CoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-test-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public CoreTests()
    {
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Login_rejects_wrong_password()
    {
        _s.Auth.Logout();
        var e = Assert.Throws<BusinessException>(() => _s.Auth.Login("admin", "sai"));
        Assert.Contains("Sai", e.Message);
        Assert.Null(_s.Session.CurrentUser);
    }

    [Fact]
    public void First_admin_only_once()
    {
        Assert.Throws<BusinessException>(() => _s.Auth.CreateFirstAdmin("admin2", "", "matkhau2"));
    }

    [Fact]
    public void Last_admin_cannot_be_demoted()
    {
        var me = _s.Session.CurrentUser!;
        Assert.Throws<BusinessException>(() => _s.Auth.UpdateUser(me.Id, "x", Roles.Staff, true));
    }

    [Fact]
    public void Staff_cannot_back_up()
    {
        _s.Auth.CreateUser("nv1", "NV", "matkhau2", Roles.Staff);
        _s.Auth.Logout();
        _s.Auth.Login("nv1", "matkhau2");
        Assert.Throws<BusinessException>(() => _s.Backup.ManualBackup(Path.Combine(_dir, "x.db")));
        Assert.True(_s.Session.Can("m1", Perm.Export));
    }

    [Fact]
    public void Backup_and_restore_round_trip()
    {
        JsonArray Rows(string v) => new() { new JsonObject { ["Ma"] = v } };
        _s.Modules.WriteSheets("ton-kho-an-an", new Dictionary<string, JsonArray?> { ["DacTinh"] = Rows("truoc") });
        var bak = Path.Combine(_dir, "bk.db");
        _s.Backup.ManualBackup(bak);
        _s.Modules.WriteSheets("ton-kho-an-an", new Dictionary<string, JsonArray?> { ["DacTinh"] = Rows("sau") });
        _s.Backup.Restore(bak);
        Assert.Contains("truoc", _s.Modules.ReadSheetJson("ton-kho-an-an", "DacTinh"));
    }
}

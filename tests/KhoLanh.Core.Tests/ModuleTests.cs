using System.Text.Json.Nodes;
using KhoLanh.Core;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class ModuleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-mod-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public ModuleTests()
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

    [Fact]
    public void Legacy_import_builds_rows_like_old_engine()
    {
        var old = Path.Combine(_dir, "khokhanh.db");
        using (var c = new SqliteConnection($"Data Source={old}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE cols(tab TEXT NOT NULL, key TEXT NOT NULL, title TEXT, width INTEGER, ord INTEGER, PRIMARY KEY(tab,key));
                CREATE TABLE rows(tab TEXT NOT NULL, id TEXT NOT NULL, cells TEXT NOT NULL, meta TEXT NOT NULL, seq INTEGER NOT NULL, del INTEGER NOT NULL DEFAULT 0, born REAL NOT NULL, PRIMARY KEY(tab,id));
                CREATE TABLE sheets(tab TEXT PRIMARY KEY, ord INTEGER NOT NULL DEFAULT 0);
                INSERT INTO sheets VALUES('ton-kho-gui/DuLieuChinh',0),('nxt-bot-sot/X',0);
                INSERT INTO cols VALUES('ton-kho-gui/DuLieuChinh','b','b',0,1),('ton-kho-gui/DuLieuChinh','a','a',0,0);
                INSERT INTO rows VALUES('ton-kho-gui/DuLieuChinh','r2','{"a":2,"z":"thêm"}','{}',1,0,2.0),
                                       ('ton-kho-gui/DuLieuChinh','r1','{"b":"x","a":1}','{}',1,0,1.0),
                                       ('ton-kho-gui/DuLieuChinh','r3','{"a":3}','{}',1,1,0.5),
                                       ('nxt-bot-sot/X','q','{"a":1}','{}',1,0,1.0);
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var r = _s.Legacy.Import(old);
        Assert.Equal(1, r.Sheets);
        var d = JsonNode.Parse(_s.Lan.Read("ton-kho-gui", "DuLieuChinh"))!;
        Assert.Equal("""[{"a":1,"b":"x"},{"a":2,"b":"","z":"thêm"}]""", d.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Empty(JsonNode.Parse(_s.Lan.Read("nxt-bot-sot", "X"))!.AsArray());
    }
}

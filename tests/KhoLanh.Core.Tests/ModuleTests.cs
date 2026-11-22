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

    private JsonNode Call(string op, params object?[] args) =>
        JsonNode.Parse(_s.Bridge.Handle(new JsonObject { ["op"] = op, ["args"] = JsonSerializerArgs(args) }.ToJsonString()))!;

    private static JsonArray JsonSerializerArgs(object?[] args) =>
        new(args.Select(a => a is JsonNode n ? n.DeepClone() : JsonValue.Create(a)).ToArray());

    [Fact]
    public void Read_missing_sheet_is_empty_array()
    {
        var r = Call("readSheet", "ton-kho-an-an", "LoadData");
        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Empty(r["data"]!.AsArray());
    }

    [Fact]
    public void Write_then_read_keeps_values_and_types()
    {
        var rows = JsonNode.Parse("""[{"c0":"TPC-ABC","c1":12.5,"c2":"","c3":true,"c4":null},{"x":"Tôm sú"}]""")!;
        Assert.True(Call("writeSheet", "ton-kho-gui", "DuLieuChinh", rows)["ok"]!.GetValue<bool>());
        var back = Call("readSheet", "ton-kho-gui", "DuLieuChinh")["data"]!;
        Assert.Equal(rows.ToJsonString(), back.ToJsonString());
    }

    [Fact]
    public void WriteSheets_null_deletes_sheet()
    {
        Call("writeSheets", "ton-kho-gui", JsonNode.Parse("""{"A":[{"a":1}],"B":[{"b":2}]}"""));
        Call("writeSheets", "ton-kho-gui", JsonNode.Parse("""{"A":null}"""));
        var list = Call("listSheets", "ton-kho-gui")["sheets"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.Equal(new[] { "B" }, list);
    }

    [Fact]
    public void LoadData_strips_TPC_prefix_like_old_main()
    {
        var meta = new JsonArray(new JsonObject { ["data"] = "__JSON__" + """{"headers":["Item ID","Mã Hàng"]}""" });
        Call("writeSheet", "ton-kho-an-an", "LoadDataMeta", meta);
        Call("writeSheet", "ton-kho-an-an", "LoadData", JsonNode.Parse("""[{"c0":"1","c1":"TPC-RHL01"},{"c0":"2","c1":"VRHL02"}]"""));
        var d = Call("readSheet", "ton-kho-an-an", "LoadData")["data"]!.AsArray();
        Assert.Equal("RHL01", d[0]!["c1"]!.GetValue<string>());
        Assert.Equal("VRHL02", d[1]!["c1"]!.GetValue<string>());
    }

    [Fact]
    public void Staff_can_read_but_not_write()
    {
        _s.Auth.CreateUser("nv1", "Nhân viên", "matkhau1", Roles.Staff);
        _s.Auth.Login("nv1", "matkhau1");
        Assert.False(Call("writeSheet", "ton-kho-gui", "X", new JsonArray())["ok"]!.GetValue<bool>());
        Assert.True(Call("readSheet", "ton-kho-gui", "X")["ok"]!.GetValue<bool>());
        var st = Call("authState", "/html/ton-kho-gui.html");
        Assert.Equal("xuat", st["user"]!["vaiTro"]!.GetValue<string>());
        Assert.False(st["user"]!["canWrite"]!.GetValue<bool>());
    }

    [Fact]
    public void Bad_names_are_rejected()
    {
        Assert.False(Call("readSheet", "../x", "A")["ok"]!.GetValue<bool>());
        Assert.False(Call("nope")["ok"]!.GetValue<bool>());
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
        var d = Call("readSheet", "ton-kho-gui", "DuLieuChinh")["data"]!;
        Assert.Equal("""[{"a":1,"b":"x"},{"a":2,"b":"","z":"thêm"}]""", d.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Empty(Call("readSheet", "nxt-bot-sot", "X")["data"]!.AsArray());
    }
}

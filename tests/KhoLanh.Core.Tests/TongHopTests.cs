using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class TongHopTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-th-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public TongHopTests()
    {
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static string SampleCode(Dictionary<int, List<TongHopService.Entry>> cat) =>
        string.Concat(TongHopService.Segs.Select(s => cat[s.Seg].First(e => e.Code.Length == s.Length).Code));

    [Fact]
    public void First_load_seeds_catalog_default_codes_and_old_sheet_format()
    {
        var t = _s.TongHop;
        t.Load();
        Assert.All(TongHopService.Segs, s => Assert.NotEmpty(t.SegmentData[s.Seg]));
        Assert.True(t.Results.Count > 10000);
        // định dạng sheet cũ để module m1/m2 vẫn đọc được
        var maHoa = _s.Modules.ReadSheet(TongHopService.Db, "MaHoa");
        Assert.NotNull(maHoa[0]!["Tổ hợp"]); Assert.NotNull(maHoa[0]!["Mã"]); Assert.NotNull(maHoa[0]!["Tên"]);
        var meta = _s.Modules.ReadSheet(TongHopService.Db, "M8_Meta");
        Assert.StartsWith("__JSON__", meta[0]!["data"]!.GetValue<string>());
        Assert.Contains("AnhXaTen", _s.Modules.ListSheets(TongHopService.Db));
        // nạp lần 2 không ghi lại (đã seed)
        var n = t.Results.Count;
        var again = _s.TongHop; again.Load();
        Assert.Equal(n, again.Results.Count);
    }

    [Fact]
    public void Build_note_split_and_unknown_segments()
    {
        var t = _s.TongHop; t.Load();
        var code = SampleCode(t.SegmentData);
        Assert.Equal(21, code.Length);
        Assert.Equal(10, TongHopService.Split(code).Length);
        Assert.Empty(t.UnknownSegments(code));
        var note = t.BuildNote(code);
        Assert.Contains(t.SegmentData[1].First(e => e.Code.Length == 4).Name, note);
        Assert.Equal("", t.BuildNote("ABC"));
        Assert.Contains(1, t.UnknownSegments("ZZZZ" + code[4..]));
        Assert.Equal("a, b (x)", TongHopService.CleanNote(" , a,, b , (x) "));
    }

    [Fact]
    public void Tab2_round_trip_and_groups()
    {
        var t = _s.TongHop; t.Load();
        var code = SampleCode(t.SegmentData);
        t.Tab2 = new TongHopService.Table
        {
            Headers = new() { "Ngày", "Mã hàng", "Số kg" }, CodeCol = "Mã hàng", DateCol = "Ngày",
            Rows = new()
            {
                new() { ["Ngày"] = "01/11/2026", ["Mã hàng"] = code, ["Số kg"] = 10 },
                new() { ["Ngày"] = "01/11/2026", ["Mã hàng"] = code, ["Số kg"] = "2,5" },
                new() { ["Ngày"] = "02/11/2026", ["Mã hàng"] = code, ["Số kg"] = 1, ["Ghi chú"] = "x" },
            },
        };
        t.Save();
        var back = _s.TongHop; back.Load();
        Assert.Equal(3, back.Tab2.Rows.Count);
        Assert.Equal("x", back.Tab2.Rows[2]["Ghi chú"]!.ToString());
        var (nums, groups, hasDate) = back.ComputeGroups();
        Assert.True(hasDate);
        Assert.Equal(new[] { "Số kg" }, nums);
        Assert.Equal(2, groups.Count);
        Assert.Equal(12.5, groups[0].Sums["Số kg"]);
        Assert.Equal(2, groups[0].Count);
    }

    [Fact]
    public void Remove_duplicates_and_anhxa_resolution()
    {
        var t = _s.TongHop; t.Load();
        t.Results = new() { new() { Code = "A" }, new() { Code = "a " }, new() { Code = "B" } };
        Assert.Equal(1, t.RemoveDuplicates());
        Assert.Equal(new[] { 1, 2 }, t.Results.Select(r => r.Stt));
        var ax = AnhXa.Defaults();
        var code = "VRHL" + "XXX" + "XEC" + "TGS" + "12345678";
        var r = ax.Resolve(code, true);
        Assert.Equal(new[] { "SP", "TT", "MB" }, r.Steps.Select(x => x.Id));
        Assert.Equal("TGS", r.Label); // đoạn sau ghi đè nhãn (giống m8-anhxa.js)
        Assert.Equal("BTP", ax.SpFull("VRHL"));
        Assert.Equal("VRHL", AnhXa.NormMa("SP", "prhl"));
        var back = AnhXa.FromSheets(ax.ToSheets()["AnhXaTen"], ax.ToSheets()["AnhXaCauHinh"])!;
        Assert.Equal(ax.Rows.Count, back.Rows.Count);
    }

    [Fact]
    public void Staff_with_export_only_cannot_save()
    {
        _s.Auth.CreateUser("nv01", "NV", "matkhau1", Roles.Staff, Perm.Serialize(new Dictionary<string, string> { ["m3"] = Perm.Export }));
        _s.TongHop.Load();
        _s.Auth.Login("nv01", "matkhau1");
        var t = _s.TongHop; t.Load();
        Assert.False(t.CanEdit);
        Assert.Throws<BusinessException>(() => t.Save());
    }
}

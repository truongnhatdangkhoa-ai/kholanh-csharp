using System.Text.Json.Nodes;
using KhoLanh.Core.Container3D;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>
/// Lõi Container 3D phải ra ĐÚNG kết quả như c3d-engine.js của app cũ: Data/c3d-golden.json do Data/c3d-golden.gen.js tạo bằng engine JS
/// (cùng đầu vào → cùng số thùng, cùng toạ độ từng thùng, cùng gợi ý).
/// </summary>
public class Container3DTests
{
    private static JsonObject Golden() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "c3d-golden.json")))!;

    private static C3dOptions Opts(JsonNode? n)
    {
        var o = C3dOptions.FromJson(n);
        if (n is JsonObject j && j["sampleOn"] is not null) o.SampleOn = C3dJson.Flag(j["sampleOn"], true);
        return o;
    }

    public static IEnumerable<object[]> Cases() => Golden()["cases"]!.AsArray().Select(c => new object[] { c!["name"]!.GetValue<string>() });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Solve_matches_js_engine(string name)
    {
        var cs = Golden()["cases"]!.AsArray().First(c => c!["name"]!.GetValue<string>() == name)!;
        var cont = C3dContainer.FromJson(cs["cont"]);
        var skus = cs["skus"]!.AsArray().Select((k, i) => C3dSku.FromJson(k, i)).ToList();
        var p = C3dEngine.Solve(cont, skus, Opts(cs["opts"]));
        var r = cs["res"]!;
        Assert.Equal(r["loaded"]!.GetValue<int>(), p.Totals.Loaded);
        Assert.Equal(r["left"]!.GetValue<int>(), p.Totals.Left);
        Assert.Equal(r["rows"]!.GetValue<int>(), p.Totals.Rows);
        Assert.Equal(r["sample"]!.GetValue<int>(), p.Totals.Sample);
        Assert.Equal(r["maxTier"]!.GetValue<int>(), p.MaxTier);
        Assert.Equal(r["ok"]!.GetValue<bool>(), p.Verify.Ok);
        Assert.Equal(r["layoutNote"]!.GetValue<string>(), p.LayoutNote);
        Assert.Equal(r["spreadNote"]!.GetValue<string>(), p.SpreadNote);
        Assert.Equal(r["errors"]!.AsArray().Select(x => x!.GetValue<string>()), p.Errors);
        Assert.Equal(r["warnings"]!.AsArray().Select(x => x!.GetValue<string>()), p.Warnings);
        var gs = r["groups"]!.AsArray();
        Assert.Equal(gs.Count, p.Groups.Count);
        for (var i = 0; i < gs.Count; i++)
        {
            Assert.Equal(gs[i]!["id"]!.GetValue<string>(), p.Groups[i].Sku.Id);
            Assert.Equal(gs[i]!["loaded"]!.GetValue<int>(), p.Groups[i].Loaded);
            Assert.Equal(gs[i]!["sample"]!.GetValue<int>(), p.Groups[i].Sample);
            Assert.Equal(gs[i]!["reasons"]!.AsArray().Select(x => x!.GetValue<string>()), p.Groups[i].Reasons);
            Assert.Equal(gs[i]!["patterns"]!.AsArray().Select(x => x!.GetValue<string>()), p.Groups[i].Patterns.Select(x => x.Text));
        }
        var bx = r["boxes"]!.AsArray();
        Assert.Equal(bx.Count, p.Boxes.Count);
        for (var i = 0; i < bx.Count; i++)
        {
            var e = bx[i]!.AsArray().Select(v => v!.GetValue<double>()).ToArray(); var b = p.Boxes[i];
            var a = new[] { b.X, b.Y, b.Z, b.Dx, b.Dy, b.Dz, b.Tier, b.Group, b.Row, b.Sample ? 1 : 0 };
            for (var j = 0; j < a.Length; j++) Assert.True(Math.Abs(e[j] - Math.Round(a[j] * 1000) / 1000) < 0.002, $"thùng {i} trường {j}: JS {e[j]} ≠ C# {a[j]}");
        }
        if (r["compare"] is JsonObject cmp) { Assert.NotNull(p.Compare); Assert.Equal(cmp["loaded"]!.GetValue<int>(), p.Compare!.Loaded); }
        var hs = r["hints"]!.AsArray();
        Assert.Equal(hs.Select(h => h!["id"]!.GetValue<string>() + ":" + h["gain"]!.GetValue<int>() + ":" + h["text"]!.GetValue<string>()), p.Hints.Select(h => h.Id + ":" + h.Gain + ":" + h.Text));
    }

    [Fact]
    public void Cbm_matches_js()
    {
        var g = Golden(); var cs = g["cases"]!.AsArray()[0]!; var cb = g["cbm"]!;
        var skus = cs["skus"]!.AsArray().Select((k, i) => C3dSku.FromJson(k, i)).ToList();
        var s = C3dCbm.Summarize(skus, C3dContainer.FromJson(cs["cont"]).Normalized(), new C3dCbm.Pallet());
        Assert.Equal(cb["totalPallets"]!.GetValue<int>(), s.TotalPallets);
        Assert.Equal(cb["floor"]!.GetValue<int>(), s.Floor.FloorCount);
        Assert.Equal(cb["tiers"]!.GetValue<int>(), s.Floor.Tiers);
        Assert.Equal(cb["per"]!.AsArray().Select(x => x!.GetValue<int>()), s.Items.Select(x => x.plan.PerPallet));
        Assert.Equal(cb["pack"]!.GetValue<int>(), C3dCbm.Pack2D(1000, 1200, 520, 280));
    }

    [Fact]
    public void Verify_detects_overlap_and_missing_support()
    {
        var sp = C3dEngine.MakeSpace(C3dEngine.Presets[0]);
        var boxes = new List<C3dBox>
        {
            new() { X = 100, Y = 100, Z = 0, Dx = 500, Dy = 300, Dz = 200, Seq = 1 },
            new() { X = 300, Y = 100, Z = 0, Dx = 500, Dy = 300, Dz = 200, Seq = 2 },   // chồng thùng 1
            new() { X = 2000, Y = 100, Z = 400, Dx = 500, Dy = 300, Dz = 200, Seq = 3 }, // lơ lửng
        };
        var v = C3dEngine.Verify(boxes, sp);
        Assert.False(v.Ok);
        Assert.Contains(v.Violations, x => x.Type == "chong-nhau");
        Assert.Contains(v.Violations, x => x.Type == "thieu-do");
    }

    [Fact]
    public void Paste_and_template_parse()
    {
        var ks = C3dService.ParsePaste("VRHLCK\t31/40\t15/09/2026\t420\t52\t28\t19\t10\nxx\n", 10);
        Assert.Single(ks);
        Assert.Equal(520, ks[0].L); Assert.Equal(420, ks[0].Qty);
        var specs = C3dEngine.ParseColPlan("6 nằm x10 2 đứng · 1 dựng cao");
        Assert.Equal(new[] { "nam:6:10", "dung:2:0", "cao:1:0" }, specs.Select(s => s.Tok + ":" + s.N + ":" + s.T));
        var pv = C3dEngine.PreviewTpl(new C3dSku { L = 520, W = 280, H = 190, AllowLay = true }, C3dEngine.Presets[2], "6 nằm 2 đứng");
        Assert.Equal(8, pv.Cols.Count);
        Assert.True(pv.Cap > 0);
    }

    [Fact]
    public void Json_roundtrip_keeps_old_format()
    {
        var k = new C3dSku { Id = "s1", Code = "A", Qty = 5, L = 1, W = 2, H = 3, Orient = "WLH", RowSet = { ["0"] = "nam" } };
        var j = k.ToJson();
        Assert.Equal(1, j["L"]!.GetValue<double>());
        Assert.Equal("nam", j["rowSet"]!["0"]!.GetValue<string>());
        var back = C3dSku.FromJson(JsonNode.Parse(j.ToJsonString()));
        Assert.Equal("WLH", back.Orient); Assert.Equal("nam", back.RowSet["0"]);
        var c = C3dContainer.FromJson(JsonNode.Parse("{\"L\":\"5450\",\"W\":2290,\"H\":2270,\"redLine\":\"2170,5\"}"));
        Assert.Equal(5450, c.L); Assert.Equal(2170.5, c.RedLine);
    }

    [Fact]
    public void Shade_matches_js_hsl()
    {
        Assert.Equal("#3b82f6", C3dService.Shade("#3b82f6", 0));
        Assert.NotEqual("#3b82f6", C3dService.Shade("#3b82f6", 0.1));
    }
}

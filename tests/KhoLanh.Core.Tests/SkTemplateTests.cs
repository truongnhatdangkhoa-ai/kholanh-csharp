using System.Text.Json.Nodes;
using KhoLanh.Core.SoKho;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Mẫu cột NXT Bột/Sốt (SoKhoMau) phải đọc / ghi / chọn giống so-kho-core.js (Data/sk-tpl-golden.json do Data/sk-tpl-golden.gen.js tạo).</summary>
public class SkTemplateTests
{
    private static readonly JsonObject G = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "sk-tpl-golden.json")))!;
    private static string Sig(SkCol c) => $"{c.Key}|{c.Label}|{c.Type}|{c.Hidden}|{c.W}|{c.Builtin}";

    [Fact]
    public void Doc_ghi_chon_mau_giong_JS()
    {
        var tpls = G["stores"]!.AsArray().Select(s => SkTemplate.FromStore((JsonObject)s!)).ToList();
        var exp = G["templates"]!.AsArray();
        for (var i = 0; i < exp.Count; i++)
        {
            var e = exp[i]!; var a = tpls[i];
            Assert.Equal(e["ten"]!.GetValue<string>(), a.Ten); Assert.Equal(e["loai"]!.GetValue<string>(), a.Loai); Assert.Equal(e["macDinh"]!.GetValue<bool>(), a.MacDinh);
            Assert.Equal(e["removed"]!.AsArray().Select(x => x!.GetValue<string>()).OrderBy(x => x), a.Removed.OrderBy(x => x));
            Assert.Equal(e["columns"]!.AsArray().Select(c => $"{c!["key"]}|{c["label"]}|{c["type"]}|{c["hidden"]!.GetValue<bool>()}|{c["w"]!.GetValue<double>()}|{c["builtin"]!.GetValue<bool>()}"), a.Columns.Select(Sig));
        }
        // ghi lại rồi đọc lại: giữ nguyên
        var again = SkTemplate.FromStore(tpls[0].ToStore());
        Assert.Equal(tpls[0].Columns.Select(Sig), again.Columns.Select(Sig)); Assert.Equal(tpls[0].Removed, again.Removed);
        var st = G["stored"]!;
        Assert.Equal(st["columnsJson"]!.GetValue<string>().Contains("\"removed\":true"), tpls[0].ToStore()["columnsJson"]!.GetValue<string>().Contains("\"removed\":true"));
        var pick = G["pick"]!;
        Assert.Equal(pick["xuat"]!.GetValue<string>(), SkTemplate.For(tpls, "xuat").Ten);
        Assert.Equal(pick["nhap"]!.GetValue<string>(), SkTemplate.For(tpls, "nhap").Ten);
        Assert.Equal(pick["none"]!.GetValue<string>(), SkTemplate.For(new List<SkTemplate>(), "nhap").Ten);
        Assert.Equal("x_so_xe_2", SkTemplate.NewExtraKey("Số xe", new[] { "x_so_xe" }));
        // công thức (giá trị đối chiếu với evalFormula của app cũ — xem sk-tpl-golden.json "eval")
        var ev = G["eval"]!.AsArray();
        foreach (var e in ev)
        {
            var vals = e!["vals"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value is null ? (double?)null : kv.Value.GetValue<double>());
            var got = SkTemplate.Eval(e["f"], k => vals.GetValueOrDefault(k));
            Assert.Equal(e["out"] is null ? null : e["out"]!.GetValue<double>(), got);
        }
    }
}

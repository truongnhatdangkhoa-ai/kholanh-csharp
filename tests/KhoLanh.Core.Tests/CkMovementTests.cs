using System.Text.Json.Nodes;
using KhoLanh.Core.CuoiKy;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Chứng từ nhập / xuất An An của Số liệu cuối kỳ: đọc + đối chiếu phải giống lõi JS (Data/cm-golden.json do Data/cm-golden.gen.js tạo).</summary>
public class CkMovementTests
{
    private static JsonObject Load(string f) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", f)))!;
    private static readonly JsonObject G = Load("cm-golden.json"), CK = Load("ck-golden.json");
    private static List<IReadOnlyList<object?>> Aoa(JsonObject g, string k) => g["aoa"]![k]!.AsArray().Select(r => (IReadOnlyList<object?>)r!.AsArray().Select(c => c is JsonValue v ? (v.TryGetValue<double>(out var d) ? d : (object?)v.GetValue<string>()) : null).ToList()).ToList();
    private static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : n?.GetValue<string>() ?? "";

    [Theory]
    [InlineData("nhap", "anan_nhap")]
    [InlineData("xuat", "anan_xuat")]
    public void Doc_bao_cao_giong_JS(string key, string kind)
    {
        var e = G[key]!; var a = CkMovement.Read(Aoa(G, key), kind);
        Assert.True(a.Ok, a.Error);
        Assert.Equal(e["headerRow"]!.GetValue<int>(), a.HeaderRow);
        Assert.Equal(e["mapping"]!.AsObject().Select(kv => kv.Key + "=" + kv.Value).OrderBy(x => x), a.Mapping.Select(kv => kv.Key + "=" + kv.Value).OrderBy(x => x));
        Assert.Equal(e["labels"]!.AsArray().Select(S), a.Labels);
        var rows = e["rows"]!.AsArray();
        Assert.Equal(rows.Select(r => string.Join("|", S(r!["id"]), S(r["src"]), S(r["row"]), S(r["phieu"]), S(r["ngay"]), S(r["kho"]), S(r["ma"]), S(r["ten"]), S(r["loai"]), S(r["dir"]), S(r["kg"]))),
            a.Vouchers.Select(v => string.Join("|", v.Id, v.Src, v.Row, v.Phieu, v.Ngay, v.Kho, v.Ma, v.Ten, v.Loai, v.Dir, v.Kg.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        var st = e["stats"]!;
        Assert.Equal((st["nRows"]!.GetValue<int>(), st["totalKg"]!.GetValue<double>(), st["skipped"]!.GetValue<int>(), st["totals"]!.GetValue<int>(), st["issues"]!.GetValue<int>()), (a.Stats.NRows, a.Stats.TotalKg, a.Stats.Skipped, a.Stats.Totals, a.Stats.Issues));
        Assert.Equal(e["issues"]!.AsArray().Select(i => S(i!["row"]) + ":" + S(i["ma"]) + ":" + S(i["msg"])), a.Issues.Select(i => i.Row + ":" + i.Ma + ":" + i.Msg));
        var p = e["period"];
        Assert.Equal(p is null ? "" : S(p["from"]) + "–" + S(p["to"]), a.Period is null ? "" : a.Period.From + "–" + a.Period.To);
    }

    [Fact]
    public void Doi_chieu_co_chung_tu_An_An_giong_JS()
    {
        var kt = CkImport.ReadKetoan(Aoa(CK, "nxt")); var ct = CkImport.ReadCT(Aoa(CK, "ct")); kt.Ct = ct;
        var an = CkImport.ReadAnAn(Aoa(CK, "anan")); var dm = CkImport.ReadDanhMuc(Aoa(CK, "dm"));
        var nh = CkMovement.Read(Aoa(G, "nhap"), "anan_nhap"); var xu = CkMovement.Read(Aoa(G, "xuat"), "anan_xuat");
        var inp = new CkInput { AnAn = an, KeToan = kt, AnAnNhap = nh, AnAnXuat = xu, Ct = ct.Vouchers.Concat(nh.Vouchers).Concat(xu.Vouchers).ToList(), DanhMuc = dm, Period = new CkPeriod("2026-10-01", "2026-10-31") };
        var R = CkEngine.Reconcile(inp);
        var items = G["items"]!.AsArray();
        Assert.Equal(items.Count, R.Items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var e = items[i]!; var a = R.Items[i];
            Assert.Equal(S(e["ma"]), a.Ma); Assert.Equal(S(e["status"]), a.Status);
            Assert.Equal(e["causes"]!.AsArray().Select(S), a.Causes.Select(c => c.Code + "/" + c.Conf));
            Assert.Equal(e["ct"]!["flags"]!.AsArray().Select(S), a.Ct.Lines.Select(l => l.V.Src + ":" + l.V.Phieu + ":" + string.Join(",", l.Flags.Select(f => f.Code))));
            Assert.Equal(e["ct"]!["nFlag"]!.GetValue<int>(), a.Ct.NFlag);
        }
        Assert.Equal(G["validation"]!.AsArray().Select(x => S(x!["level"]) + ":" + S(x["text"])), R.Validation.Select(x => x.Level + ":" + x.Text));
    }
}

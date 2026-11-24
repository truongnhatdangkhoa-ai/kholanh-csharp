using System.Text.Json.Nodes;
using KhoLanh.Core.KiemKe;
using Xunit;
using static KhoLanh.Core.KiemKe.KiemKeCore;

namespace KhoLanh.Core.Tests;

/// <summary>
/// Kiểm kê nhanh: đối chiếu lõi C# với CHÍNH kk-nhanh-core.js của app cũ (Data/kkn-golden.json do `node Data/kkn-golden.gen.js` sinh):
/// nhận loại file theo tiêu đề, tổng hợp + mã sửa tự động được, Việc cần làm, lọc / tìm / sắp xếp.
/// </summary>
public class KkNhanhTests
{
    private static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n?.ToJsonString() ?? "";

    [Fact]
    public void Nhan_file_tong_hop_viec_can_lam_loc_giong_app_cu()
    {
        var g = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "kkn-golden.json")))!.AsObject();
        Assert.Equal(g["strip"]!.AsArray().Select(S), new[] { "Mã  Hàng", "Đặc tính (kg)", "  TỒN-CUỐI kỳ ", "Trọng lượng/kiện" }.Select(KkNhanh.Strip));

        var aoas = g["aoas"]!.AsArray();
        for (var i = 0; i < aoas.Count; i++)
        {
            var aoa = aoas[i]!.AsArray().Select(r => (IReadOnlyList<string?>)r!.AsArray().Select(c => (string?)S(c)).ToList()).ToList();
            var d = KkNhanh.DetectKind(aoa); var e = g["detect"]![i]!;
            Assert.Equal(S(e["kind"]), d.Kind); Assert.Equal(e["header"]!.GetValue<int>(), d.Header); Assert.Equal(S(e["why"]), d.Why);
        }

        var nn = g["nn"]!.AsArray().ToDictionary(x => S(x![0]), x => x![1]!.AsObject());
        var rows = g["rows"]!.AsArray().Select(x =>
        {
            var o = x!.AsObject(); var r = new Row { Key = S(o["key"]), Ma = S(o["ma"]), Ten = S(o["ten"]), Status = S(o["status"]), DKg = o["dKg"]!.GetValue<double>() };
            if (nn.TryGetValue(r.Key, out var n))
                r.Nn = new Nn { Code = S(n["code"]), Side = n["side"] is null ? null : S(n["side"]), FixTo = n["fixTo"] is null ? null : S(n["fixTo"]), Strong = n["strong"]?.GetValue<bool>() ?? false, Level = S(n["level"]) };
            return r;
        }).ToList();

        var s = KkNhanh.Summarize(rows); var es = g["sum"]!;
        Assert.Equal(new[] { "total", "khop", "thua", "thieu", "lechKg", "chiKK", "chuaKK", "open", "auto", "nghi", "chuaRo", "noName" }.Select(k => es[k]!.GetValue<int>()),
            new[] { s.Total, s.Khop, s.Thua, s.Thieu, s.LechKg, s.ChiKK, s.ChuaKK, s.Open, s.Auto, s.Nghi, s.ChuaRo, s.NoName });
        Assert.Equal(es["kgLech"]!.GetValue<double>(), s.KgLech);
        Assert.Equal(es["autoList"]!.AsArray().Select(a => $"{S(a!["key"])}>{S(a["to"])}"), s.AutoList.Select(a => $"{a.Key}>{a.To}"));

        var ctxs = g["ctxs"]!.AsArray();
        for (var i = 0; i < ctxs.Count; i++)
        {
            var c = ctxs[i]!.AsObject();
            bool B(string k) => c[k]?.GetValue<bool>() ?? false;
            var ctx = new KkNhanh.Ctx(B("hasKK"), B("hasAn"), B("hasKT"), c["dirty"]?.GetValue<int>() ?? 0, B("locked"), c["nPages"]?.GetValue<int>() ?? 0);
            var got = KkNhanh.TodoList(s, ctx).Select(t => $"{t.Id}|{t.Tone}|{t.Act}|{t.Label}").ToList();
            Assert.Equal(g["todo"]![i]!.AsArray().Select(t => $"{S(t![0])}|{S(t[1])}|{S(t[2])}|{S(t[3]).Replace("Số liệu tổng", "Số liệu kiểm kê")}"), got);   // tab ở C# tên "Số liệu kiểm kê"
        }

        var fs = g["filters"]!.AsArray();
        for (var i = 0; i < fs.Count; i++)
            Assert.Equal(g["filt"]![i]!.AsArray().Select(S), KkNhanh.FilterRows(rows, S(fs[i]![0]), S(fs[i]![1])).Select(r => r.Key));
    }
}

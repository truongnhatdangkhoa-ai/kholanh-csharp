using System.Text.Json.Nodes;
using KhoLanh.Core.Lan;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.134 — Tồn theo vị trí bị nhân đôi sau khi đồng bộ (người dùng: "nạp file mới không bị dupe, bị dupe trong quá trình cập nhật").</summary>
public class NhanDoi134Tests
{
    private static string Rows(params string[] ma) => new JsonArray(ma.Select(m => (JsonNode)new JsonObject { ["ma"] = m, ["sl"] = 1 }).ToArray()).ToJsonString();
    private static List<string> Ma(string? json) => JsonNode.Parse(json!)!.AsArray().Select(n => n!["ma"]!.ToString()).ToList();

    [Fact]
    public void Dong_hai_ben_cung_them_chi_lay_mot_lan()
    {
        // gốc A; máy con thêm X, Y; máy chủ cũng đã có X và thêm Z → trước đây X ×2
        var r = SheetMerge.ThreeWay(Rows("A"), Rows("A", "X", "Y"), Rows("A", "X", "Z"));
        Assert.True(r.Clean);
        Assert.Equal(new[] { "A", "X", "Y", "Z" }, Ma(r.Merged).OrderBy(x => x));
        // máy con thêm X 2 lần, máy chủ 1 lần → còn thêm 1
        var r2 = SheetMerge.ThreeWay(Rows("A"), Rows("A", "X", "X"), Rows("A", "X"));
        Assert.True(r2.Clean);
        Assert.Equal(2, Ma(r2.Merged).Count(x => x == "X"));
    }

    [Fact]
    public void Ket_qua_gop_lon_hon_han_hai_ben_thi_khong_tu_gop()
    {
        // gốc 2 dòng chung; mỗi bên nạp file riêng 30 dòng khác hẳn nhau nhưng còn giữ 2 dòng gốc → trước đây nối thành 62 dòng
        var a = Enumerable.Range(0, 30).Select(i => "A" + i).ToArray(); var b = Enumerable.Range(0, 30).Select(i => "B" + i).ToArray();
        var r = SheetMerge.ThreeWay(Rows("G1", "G2"), Rows(a.Concat(new[] { "G1", "G2" }).ToArray()), Rows(b.Concat(new[] { "G1", "G2" }).ToArray()));
        Assert.False(r.Clean);
        Assert.Contains("nhân đôi", r.Note);
        // thêm ít dòng mỗi bên vẫn tự gộp
        var ok = SheetMerge.ThreeWay(Rows(a), Rows(a.Append("X").ToArray()), Rows(a.Append("Y").ToArray()));
        Assert.True(ok.Clean);
    }
}

using System.Text.Json.Nodes;
using KhoLanh.Core.Lan;
using Xunit;
using static KhoLanh.Core.TroLy.TroLyChecks;

namespace KhoLanh.Core.Tests;

/// <summary>1.120 — báo cáo Trợ lý 26/11 (app 1.119): Tồn kho gửi / Tồn vị trí gần như mọi dòng ×2 do tự gộp LAN khi chưa có bản chung.</summary>
public class BaoCaoTroLy120Tests
{
    private static string Rows(params string[] ma) => new JsonArray(ma.Select(m => (JsonNode)new JsonObject { ["ma"] = m, ["sl"] = 1 }).ToArray()).ToJsonString();

    [Fact]
    public void Hai_may_cung_nap_file_khi_chua_co_ban_chung_khong_nhan_doi()
    {
        // máy chủ đã nạp file hôm qua; máy con (chưa từng có bảng) nạp file hôm nay → 3 dòng giống nhau, 1 dòng khác
        var server = Rows("A", "B", "C", "D");
        var client = Rows("A", "B", "C", "E");
        foreach (var gốc in new string?[] { null, "[]" })
        {
            var r = SheetMerge.ThreeWay(gốc, client, server);
            Assert.False(r.Clean);                                  // trước 1.120: tự gộp = A,B,C,D,A,B,C,E (nhân đôi)
            Assert.Contains("cùng nạp", r.Note);
        }
        // cùng một file → máy chủ đã có sẵn, không đổi gì
        var same = SheetMerge.ThreeWay(null, Rows("A", "B"), Rows("A", "B", "C"));
        Assert.True(same.Clean);
        Assert.Equal(3, JsonNode.Parse(same.Merged!)!.AsArray().Count);
        // máy chủ chưa có bảng → lấy bản máy con
        var empty = SheetMerge.ThreeWay(null, client, "[]");
        Assert.True(empty.Clean);
        Assert.Equal(4, JsonNode.Parse(empty.Merged!)!.AsArray().Count);
        // có bản chung: hai bên thêm dòng khác nhau vẫn tự gộp như cũ
        var gop = SheetMerge.ThreeWay(Rows("A"), Rows("A", "X"), Rows("A", "Y"));
        Assert.True(gop.Clean);
        Assert.Equal(3, JsonNode.Parse(gop.Merged!)!.AsArray().Count);
    }

    [Fact]
    public void Tro_ly_bo_qua_file_nap_va_trang_nhap_lieu_bao_ca_bang_nhan_doi()
    {
        Assert.True(SkipDupSheet("ton-kho-gui", "FileNap"));
        Assert.True(SkipDupSheet("ton-kho-an-an", "FileNap_Meta"));
        Assert.True(SkipDupSheet("nhap-lieu", "T-261124173613277"));
        Assert.False(SkipDupSheet("ton-kho-gui", "DuLieuChinh"));
        Assert.False(SkipDupSheet("ton-vi-tri-an-an", "ViTri"));
        var doubled = JsonNode.Parse(Rows("A", "B", "C", "A", "B", "C"))!.AsArray();
        var f = FindDupKeys("Tồn kho gửi", "DuLieuChinh", doubled);
        Assert.Contains(f, x => x.Detail.Contains("nhân đôi") && x.Detail.Contains("Hoàn tác gộp"));
        var few = JsonNode.Parse(Rows("A", "A", "B", "C", "D", "E"))!.AsArray();
        Assert.DoesNotContain(FindDupKeys("Tồn kho gửi", "DuLieuChinh", few), x => x.Detail.Contains("nhân đôi"));
    }
}

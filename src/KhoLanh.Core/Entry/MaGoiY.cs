using System.Text.Json.Nodes;
using KhoLanh.Core.Modules;
using KhoLanh.Core.TimNhanh;

namespace KhoLanh.Core.Entry;

/// <summary>
/// GỢI Ý MÃ HÀNG khi gõ ở cột "Mã hàng" của Nhập liệu (1.54): gom các mã đang có tồn ở mọi module (Tồn An An, Tồn theo vị trí, Vị Trí Bột, Tồn kho gửi,
/// NXT Bột/Sốt — theo quyền xem, qua <see cref="GsService"/>) và Danh mục mã hàng (tong-hop / MaDaTao). Tìm không dấu, không tính khoảng trắng,
/// mọi từ gõ phải có trong mã / tên / size / đặc tính; mã bắt đầu bằng chữ gõ xếp trước, rồi mã còn tồn.
/// </summary>
public static class MaGoiY
{
    public sealed record Sug(string Ma, string Ten, string Size, string Dt, double Ton, string Nguon, bool TrongDanhMuc)
    {
        internal string Hay = "";
    }

    /// <summary>Danh mục mã hàng (cột Mã, Ghi chú) đọc thẳng sheet dùng chung.</summary>
    public static List<(string Code, string Note)> ReadCatalog(ISheetSource src)
    {
        try
        {
            return (JsonNode.Parse(src.Read("tong-hop", "MaDaTao")) as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(o => (Code: Gs.MaKey(o["Mã"]?.ToString()), Note: o["Ghi chú"]?.ToString() ?? "")).Where(x => x.Code.Length > 0).ToList();
        }
        catch { return new(); }
    }

    /// <summary>Gộp tồn (mỗi mã 1 gợi ý: cộng tồn, tên / size / đặc tính đầu tiên có chữ, các nguồn) + mã danh mục chưa có tồn.</summary>
    public static List<Sug> Build(IEnumerable<GsRow> stock, IEnumerable<(string Code, string Note)> catalog)
    {
        var cat = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (c, n) in catalog) { var k = Gs.MaKey(c); if (k.Length > 0 && !cat.ContainsKey(k)) cat[k] = n; }
        var res = new List<Sug>();
        foreach (var g in stock.Where(r => Gs.MaKey(r.Ma).Length > 0).GroupBy(r => Gs.MaKey(r.Ma)))
        {
            string First(Func<GsRow, string> f) => g.Select(f).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";
            var ten = First(r => r.Ten); if (ten.Length == 0 && cat.TryGetValue(g.Key, out var note)) ten = note;
            res.Add(new Sug(g.Key, ten, First(r => r.Size), First(r => r.Dt), g.Sum(r => r.Ton ?? 0),
                string.Join(", ", g.Select(r => Gs.SrcName(r.Src)).Distinct()), cat.ContainsKey(g.Key)));
        }
        var have = res.Select(r => r.Ma).ToHashSet();
        res.AddRange(cat.Where(kv => !have.Contains(kv.Key)).Select(kv => new Sug(kv.Key, kv.Value, "", "", 0, "Danh mục mã hàng", true)));
        foreach (var s in res) s.Hay = Gs.Cmp(Gs.Norm(s.Ma + " " + s.Ten + " " + s.Size + " " + s.Dt));
        return res;
    }

    /// <summary>Tìm gợi ý cho chữ đang gõ (≥ 2 ký tự).</summary>
    public static List<Sug> Search(IReadOnlyList<Sug> all, string text, int lim = 8)
    {
        var words = Gs.Norm(text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Gs.Cmp).Where(w => w.Length > 0).ToList();
        if (words.Sum(w => w.Length) < 2) return new();
        var head = Gs.Norm(Gs.MaKey(text));
        return all.Where(s => words.All(w => s.Hay.Contains(w, StringComparison.Ordinal)))
            .OrderByDescending(s => Gs.Norm(s.Ma).StartsWith(head, StringComparison.Ordinal))
            .ThenByDescending(s => s.Ton > 0).ThenBy(s => s.Ma, StringComparer.Ordinal).Take(lim).ToList();
    }

    /// <summary>Mã có trong tồn hoặc danh mục không (mã gõ tay lạ → tô vàng nhạt).</summary>
    public static HashSet<string> Known(IEnumerable<Sug> all) => all.Select(s => s.Ma).ToHashSet(StringComparer.Ordinal);
}

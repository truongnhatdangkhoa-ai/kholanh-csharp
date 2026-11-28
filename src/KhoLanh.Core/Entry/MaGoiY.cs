using System.Text.Json.Nodes;
using KhoLanh.Core.Modules;
using KhoLanh.Core.TimNhanh;

namespace KhoLanh.Core.Entry;

/// <summary>
/// GỢI Ý MÃ HÀNG khi gõ ở cột "Mã hàng" của Nhập liệu (1.54). Từ 1.89 danh sách gợi ý CHỈ gồm mã đang có tồn (Nhập liệu đưa vào dòng Tồn kho An An
/// <see cref="Gs.Data.AnAn"/>); mã chỉ có trong Danh mục mã hàng (tong-hop / MaDaTao) vẫn dùng để tự điền Tên / Size / Trọng lượng và để biết mã
/// không lạ, nhưng KHÔNG hiện trong danh sách gợi ý (người dùng: tránh nhầm với mã danh mục gần giống). Tìm không dấu, không tính khoảng trắng,
/// mọi từ gõ phải có trong mã / tên / size / đặc tính; gõ đúng 4 chữ số (vd "0050") = đoạn Size của mã 21 ký tự → chỉ mã đúng size đó.
/// </summary>
public static class MaGoiY
{
    /// <summary>Gợi ý 1 mã. <paramref name="Tl"/> = trọng lượng 1 thùng / kiện (kg, dạng "10.8"; rỗng = chưa biết) — 1.62 tự điền cột Trọng lượng.</summary>
    public sealed record Sug(string Ma, string Ten, string Size, string Dt, double Ton, string Nguon, bool TrongDanhMuc, string Tl = "")
    {
        internal string Hay = "";
        /// <summary>Có tồn (từ dòng tồn), không phải mã chỉ có trong danh mục → được hiện trong danh sách gợi ý.</summary>
        public bool CoTon { get; internal set; }
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
    public static List<Sug> Build(IEnumerable<GsRow> stock, IEnumerable<(string Code, string Note)> catalog, Dictionary<int, Dictionary<string, string>>? seg = null)
    {
        var cat = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (c, n) in catalog) { var k = Gs.MaKey(c); if (k.Length > 0 && !cat.ContainsKey(k)) cat[k] = n; }
        var res = new List<Sug>();
        foreach (var g in stock.Where(r => Gs.MaKey(r.Ma).Length > 0).GroupBy(r => Gs.MaKey(r.Ma)))
        {
            string First(Func<GsRow, string> f) => g.Select(f).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";
            var ten = First(r => r.Ten); if (ten.Length == 0 && cat.TryGetValue(g.Key, out var note)) ten = note;
            // trọng lượng 1 thùng = kg / số thùng của từng dòng tồn, lấy giá trị gặp nhiều nhất
            var tl = g.Where(r => r.Ton is > 0 && r.Kg is > 0).Select(r => Math.Round(r.Kg!.Value / r.Ton!.Value, 3))
                .GroupBy(x => x).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).Select(x => x.Key).FirstOrDefault();
            var size = First(r => r.Size);
            // đặc tính còn tồn của mã (nhiều đặc tính → 3 cái đầu + số còn lại)
            var dts = g.Select(r => (r.Dt ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var dt = dts.Count <= 3 ? string.Join(", ", dts) : string.Join(", ", dts.Take(3)) + $" +{dts.Count - 3}";
            res.Add(new Sug(g.Key, ten, size.Length > 0 ? size : SegSize(seg, g.Key), dt, g.Sum(r => r.Ton ?? 0),
                string.Join(", ", g.Select(r => Gs.SrcName(r.Src)).Distinct()), cat.ContainsKey(g.Key), tl > 0 ? KhoLanh.Core.Entry.CongThuc.Num(tl) : SegTl(seg, g.Key)) { CoTon = true });
        }
        var have = res.Select(r => r.Ma).ToHashSet();
        res.AddRange(cat.Where(kv => !have.Contains(kv.Key)).Select(kv => new Sug(kv.Key, kv.Value, SegSize(seg, kv.Key), "", 0, "Danh mục mã hàng", true, SegTl(seg, kv.Key))));
        foreach (var s in res) s.Hay = Gs.Cmp(Gs.Norm(s.Ma + " " + s.Ten + " " + s.Size + " " + s.Dt));
        return res;
    }

    /// <summary>Đoạn của mã 21 ký tự đã khai báo tên ở Mã hóa sản phẩm (đoạn 8 = Size, đoạn 9 = Trọng lượng).</summary>
    private static string SegName(Dictionary<int, Dictionary<string, string>>? seg, string ma, int n)
    {
        if (seg is null || !seg.TryGetValue(n, out var map)) return "";
        var parts = Gs.SegDefs.FirstOrDefault(d => d.Seg == n);
        var m = Gs.MaKey(ma);
        if (m.Length != 21 || parts.Len == 0) return "";
        return map.TryGetValue(m.Substring(parts.Start, parts.Len), out var name) ? name.Trim() : "";
    }
    private static string SegSize(Dictionary<int, Dictionary<string, string>>? seg, string ma) => SegName(seg, ma, 8);
    /// <summary>Tên đoạn Trọng lượng có số kg ("10 kg", "2KG", "1,8kg") → "10" / "2" / "1.8".</summary>
    private static string SegTl(Dictionary<int, Dictionary<string, string>>? seg, string ma)
    {
        var m = System.Text.RegularExpressions.Regex.Match(SegName(seg, ma, 9), @"(\d+(?:[.,]\d+)?)\s*kg", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && EntryService.TryNumber(m.Groups[1].Value, out var d) && d > 0 ? KhoLanh.Core.Entry.CongThuc.Num(d) : "";
    }

    /// <summary>Bảng tra mã → thông tin tự điền (Tên hàng / Size / Trọng lượng) cho Nhập liệu.</summary>
    public static Dictionary<string, EntryRules.Info> Lookup(IEnumerable<Sug> all)
    {
        var d = new Dictionary<string, EntryRules.Info>(StringComparer.Ordinal);
        foreach (var s in all) d.TryAdd(s.Ma, new EntryRules.Info(s.Ten, s.Size, s.Tl));
        return d;
    }

    /// <summary>Đoạn Size (ký tự 14–17) của mã ≥ 21 ký tự, vd VRHLITNXXXR000050JRA1 → "0050".</summary>
    public static string SizeSeg(string ma) { var m = Gs.MaKey(ma); return m.Length >= 21 ? m.Substring(13, 4) : ""; }

    /// <summary>Tìm gợi ý cho chữ đang gõ (≥ 2 ký tự) — chỉ mã có tồn (<see cref="Sug.CoTon"/>).</summary>
    public static List<Sug> Search(IReadOnlyList<Sug> all, string text, int lim = 8)
    {
        var words = Gs.Norm(text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Gs.Cmp).Where(w => w.Length > 0).ToList();
        if (words.Sum(w => w.Length) < 2) return new();
        var head = Gs.Norm(Gs.MaKey(text));
        var hits = all.Where(s => s.CoTon && words.All(w => s.Hay.Contains(w, StringComparison.Ordinal))).ToList();
        // gõ đúng 4 chữ số = đoạn Size của mã: có mã đúng size thì chỉ giữ chúng (không kèm 005060, 000500… chỉ chứa chuỗi số đó)
        if (System.Text.RegularExpressions.Regex.IsMatch(head, @"^\d{4}$") && hits.Where(s => SizeSeg(s.Ma) == head).ToList() is { Count: > 0 } exact) hits = exact;
        return hits
            .OrderByDescending(s => Gs.Norm(s.Ma).StartsWith(head, StringComparison.Ordinal))
            .ThenByDescending(s => s.Ton > 0).ThenBy(s => s.Ma, StringComparer.Ordinal).Take(lim).ToList();
    }

    /// <summary>1.187: 1 gợi ý có khớp chữ đang gõ không (cùng luật <see cref="Search"/>: mã còn tồn, mọi từ có trong mã / tên / size / đặc tính) — cho ô tự gợi ý lọc theo từng mục.</summary>
    public static bool Match(Sug s, string? text)
    {
        if (!s.CoTon) return false;
        var words = Gs.Norm(text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Gs.Cmp).Where(w => w.Length > 0).ToList();
        if (words.Sum(w => w.Length) < 2) return false;
        var head = Gs.Norm(Gs.MaKey(text));
        if (System.Text.RegularExpressions.Regex.IsMatch(head, @"^\d{4}$")) return SizeSeg(s.Ma) == head || s.Hay.Contains(head, StringComparison.Ordinal) && SizeSeg(s.Ma).Length == 0;
        return words.All(w => s.Hay.Contains(w, StringComparison.Ordinal));
    }

    /// <summary>Mã có trong tồn hoặc danh mục không (mã gõ tay lạ → tô vàng nhạt).</summary>
    public static HashSet<string> Known(IEnumerable<Sug> all) => all.Select(s => s.Ma).ToHashSet(StringComparer.Ordinal);
}

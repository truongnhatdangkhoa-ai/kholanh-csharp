using KhoLanh.Core.Grid;

namespace KhoLanh.Core.Entry;

/// <summary>
/// TÔ MÀU Ô của trang Nhập liệu (1.62): mỗi dòng lưu thêm khóa <see cref="EntryService.ColorKey"/> dạng "c1:y,c3:r"
/// (mã cột : mã màu) — đi theo dòng khi chèn / xóa / lọc, cột đổi chỗ vẫn đúng. Module khác đọc trang bỏ qua khóa này.
/// </summary>
public static class CellColors
{
    public static readonly (string Code, string Name, string Hex)[] Palette =
    {
        ("y", "Vàng", "#FFEB9C"), ("g", "Xanh lá", "#C6EFCE"), ("b", "Xanh dương", "#CFE2FA"), ("r", "Đỏ", "#FFC7CE"),
        ("o", "Cam", "#FFDDB8"), ("p", "Tím", "#E2DAF7"), ("s", "Xám", "#E1E6EC"),
    };

    public static string? Hex(string code) => Palette.FirstOrDefault(p => p.Code == code).Hex;

    /// <summary>"c1:y,c3:r" → (mã cột, mã màu).</summary>
    public static Dictionary<string, string> Codes(string? s)
    {
        var d = new Dictionary<string, string>();
        foreach (var part in (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = part.LastIndexOf(':');
            if (i > 0 && Hex(part[(i + 1)..]) is not null) d[part[..i]] = part[(i + 1)..];
        }
        return d;
    }

    /// <summary>(mã cột, mã màu HTML) để vẽ / xuất Excel.</summary>
    public static IEnumerable<(string Key, string Hex)> Parse(string? s) => Codes(s).Select(kv => (kv.Key, Hex(kv.Value)!));

    public static string Join(Dictionary<string, string> d) => string.Join(",", d.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value));

    /// <summary>Tô (code = mã màu) hoặc bỏ màu (code rỗng) các cột <paramref name="keys"/> của một dòng.</summary>
    public static string Set(string? current, IEnumerable<string> keys, string code)
    {
        var d = Codes(current);
        foreach (var k in keys) { if (string.IsNullOrEmpty(code) || Hex(code) is null) d.Remove(k); else d[k] = code; }
        return Join(d);
    }

    /// <summary>Bỏ màu của cột không còn trong trang.</summary>
    public static string Clean(string? s, IReadOnlyList<GridColumn> cols)
    {
        var d = Codes(s);
        foreach (var k in d.Keys.Where(k => cols.All(c => c.Key != k)).ToList()) d.Remove(k);
        return Join(d);
    }
}

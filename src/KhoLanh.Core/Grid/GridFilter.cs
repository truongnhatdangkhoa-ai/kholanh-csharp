using System.Globalization;
using System.Text;

namespace KhoLanh.Core.Grid;

/// <summary>Bộ lọc một cột: dòng tìm kiếm riêng của cột + danh sách giá trị được chọn (null = mọi giá trị).</summary>
public sealed class ColumnFilter
{
    /// <summary>Chữ gõ ở dòng tìm kiếm: chứa (không phân biệt hoa thường / dấu), nhiều từ cách nhau bởi dấu cách đều phải có;
    /// cột số nhận thêm &gt; &lt; &gt;= &lt;= = (vd "&gt;100", "&lt;=0").</summary>
    public string Text { get; set; } = "";
    /// <summary>Giá trị (chữ hiển thị) được giữ lại từ danh sách lọc; null = không lọc theo danh sách.</summary>
    public HashSet<string>? Allowed { get; set; }
    public bool Active => Text.Trim().Length > 0 || Allowed is not null;
}

/// <summary>Một mục trong danh sách lọc phân tầng (Năm › Tháng › Ngày cho cột ngày; còn lại một tầng).</summary>
public sealed class FilterNode
{
    public string Label { get; init; } = "";
    /// <summary>Giá trị gốc (chữ hiển thị) — chỉ có ở mục lá.</summary>
    public string? Value { get; init; }
    public int Count { get; set; }
    public int Level { get; init; }
    public List<FilterNode> Children { get; } = new();
    public IEnumerable<string> Values() => Value is not null ? new[] { Value } : Children.SelectMany(c => c.Values());
}

/// <summary>Lọc dòng kiểu Excel cho lưới: lọc theo chữ từng cột + danh sách giá trị, danh sách phân tầng (chỉ hiện giá trị còn lại sau các cột khác).</summary>
public static class GridFilter
{
    public const string Blank = "(Trống)";

    /// <summary>Bỏ dấu tiếng Việt + chữ thường để tìm không phân biệt dấu.</summary>
    public static string Fold(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch switch { 'đ' or 'Đ' => 'd', _ => char.ToLowerInvariant(ch) });
        }
        return sb.ToString();
    }

    public static bool TryNumber(string s, out double v)
    {
        v = 0;
        s = (s ?? "").Trim().Replace(" ", "");
        if (s.Length == 0) return false;
        // 1,234.5 (kiểu app) hoặc 1.234,5 (kiểu Việt) — có cả hai dấu thì dấu đứng sau là dấu thập phân
        if (s.Contains(',') && s.Contains('.'))
            s = s.LastIndexOf(',') > s.LastIndexOf('.') ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (s.Contains(',')) s = s.Count(c => c == ',') == 1 && s.Length - s.IndexOf(',') - 1 != 3 ? s.Replace(',', '.') : s.Replace(",", "");
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>Ô có khớp chữ tìm của cột không.</summary>
    public static bool MatchText(string cell, string query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return true;
        foreach (var op in new[] { ">=", "<=", "<>", ">", "<", "=" })
        {
            if (!q.StartsWith(op, StringComparison.Ordinal)) continue;
            var rest = q[op.Length..].Trim();
            if (TryNumber(rest, out var n))
            {
                if (!TryNumber(cell, out var v)) return false;
                return op switch { ">=" => v >= n, "<=" => v <= n, "<>" => Math.Abs(v - n) > 1e-9, ">" => v > n, "<" => v < n, _ => Math.Abs(v - n) < 1e-9 };
            }
            if (op == "=") return Fold(cell.Trim()) == Fold(rest);
            if (op == "<>") return Fold(cell.Trim()) != Fold(rest);
            break;
        }
        var f = Fold(cell);
        return Fold(q).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => f.Contains(w, StringComparison.Ordinal));
    }

    public static string Key(string cell) => string.IsNullOrWhiteSpace(cell) ? Blank : cell.Trim();

    public static bool Match(string cell, ColumnFilter f) =>
        (f.Allowed is null || f.Allowed.Contains(Key(cell))) && MatchText(cell, f.Text);

    /// <summary>Các dòng (chỉ số trong <paramref name="rowCount"/>) qua hết bộ lọc; <paramref name="skipCol"/> = bỏ qua bộ lọc cột đó (để dựng danh sách phân tầng).</summary>
    public static List<int> Rows(int rowCount, Func<int, int, string> cell, IReadOnlyDictionary<int, ColumnFilter> filters, int skipCol = -1)
    {
        var act = filters.Where(kv => kv.Key != skipCol && kv.Value.Active).ToList();
        var list = new List<int>(rowCount);
        for (var r = 0; r < rowCount; r++)
        {
            var ok = true;
            foreach (var (c, f) in act) if (!Match(cell(r, c), f)) { ok = false; break; }
            if (ok) list.Add(r);
        }
        return list;
    }

    private static readonly string[] DateFormats = { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss" };

    public static bool TryDate(string s, out DateTime d) =>
        DateTime.TryParseExact((s ?? "").Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    /// <summary>Dựng danh sách lọc từ các giá trị: cột ngày (≥ 80% ô có chữ là ngày) chia Năm › Tháng › Ngày, cột số xếp theo số, còn lại xếp chữ.</summary>
    public static List<FilterNode> BuildTree(IEnumerable<string> cells, bool dateHint = false)
    {
        var counts = new Dictionary<string, int>();
        foreach (var c in cells) { var k = Key(c); counts[k] = counts.GetValueOrDefault(k) + 1; }
        var filled = counts.Keys.Where(k => k != Blank).ToList();
        var dates = filled.Count(k => TryDate(k, out _));
        var nodes = new List<FilterNode>();
        if (filled.Count > 0 && (dates == filled.Count || (dateHint || filled.Count >= 3) && dates >= filled.Count * 0.8))
        {
            var dated = filled.Where(k => TryDate(k, out _)).Select(k => { TryDate(k, out var d); return (k, d); }).ToList();
            foreach (var y in dated.GroupBy(x => x.d.Year).OrderByDescending(g => g.Key))
            {
                var yn = new FilterNode { Label = "Năm " + y.Key, Level = 0 };
                foreach (var m in y.GroupBy(x => x.d.Month).OrderBy(g => g.Key))
                {
                    var mn = new FilterNode { Label = "Tháng " + m.Key.ToString("00"), Level = 1 };
                    foreach (var (k, d) in m.OrderBy(x => x.d).ThenBy(x => x.k, StringComparer.Ordinal))
                        mn.Children.Add(new FilterNode { Label = k, Value = k, Level = 2, Count = counts[k] });
                    mn.Count = mn.Children.Sum(x => x.Count); yn.Children.Add(mn);
                }
                yn.Count = yn.Children.Sum(x => x.Count); nodes.Add(yn);
            }
            foreach (var k in filled.Where(k => !TryDate(k, out _)).OrderBy(k => k, StringComparer.CurrentCulture))
                nodes.Add(new FilterNode { Label = k, Value = k, Count = counts[k] });
        }
        else
        {
            var nums = filled.Count > 0 && filled.All(k => TryNumber(k, out _));
            var ordered = nums ? filled.OrderBy(k => { TryNumber(k, out var v); return v; }) : filled.OrderBy(k => k, StringComparer.Create(new CultureInfo("vi-VN"), true));
            nodes.AddRange(ordered.Select(k => new FilterNode { Label = k, Value = k, Count = counts[k] }));
        }
        if (counts.TryGetValue(Blank, out var nb)) nodes.Add(new FilterNode { Label = Blank, Value = Blank, Count = nb });
        return nodes;
    }
}

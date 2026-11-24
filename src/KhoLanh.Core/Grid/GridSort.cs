using System.Globalization;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.Grid;

/// <summary>
/// Sắp xếp bảng khi bấm tiêu đề cột (1.71): A→Z / Z→A. Chỉ đổi THỨ TỰ HIỂN THỊ (danh sách chỉ số dòng), không đổi dữ liệu.
/// Cột số (đa số ô là số) xếp theo giá trị, cột ngày (dd/MM/yyyy, yyyy-MM-dd) theo ngày, còn lại theo chữ tiếng Việt không phân biệt hoa thường;
/// ô trống luôn nằm cuối. Sắp xếp ổn định (dòng bằng nhau giữ thứ tự cũ).
/// </summary>
public static class GridSort
{
    private enum Kind { Text, Number, Date }
    private static readonly Regex DateDmy = new(@"^(\d{1,2})[/.-](\d{1,2})[/.-](\d{2,4})(\s+(\d{1,2}):(\d{2})(:(\d{2}))?)?$", RegexOptions.Compiled);
    private static readonly Regex DateYmd = new(@"^(\d{4})-(\d{1,2})-(\d{1,2})([ T](\d{1,2}):(\d{2})(:(\d{2}))?)?", RegexOptions.Compiled);
    private static readonly StringComparer Vi = StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), CompareOptions.IgnoreCase);

    public static List<int> Order(IEnumerable<int> rows, Func<int, string> text, bool desc)
    {
        var list = rows.Select(r => (r, t: (text(r) ?? "").Trim())).ToList();
        var filled = list.Where(x => x.t.Length > 0).ToList();
        var empty = list.Where(x => x.t.Length == 0).Select(x => x.r);
        var kind = KindOf(filled.Select(x => x.t));
        IEnumerable<(int r, string t)> ordered;
        if (kind == Kind.Text)
            ordered = desc ? filled.OrderByDescending(x => x.t, Vi) : filled.OrderBy(x => x.t, Vi);
        else
        {
            // số / ngày: ô không đọc được (chữ lẫn trong cột số) xếp sau các ô đọc được, giữa chúng theo chữ
            var keyed = filled.Select(x => (x.r, x.t, ok: TryKey(x.t, kind, out var k), k)).ToList();
            var good = desc ? keyed.Where(x => x.ok).OrderByDescending(x => x.k) : keyed.Where(x => x.ok).OrderBy(x => x.k);
            var bad = desc ? keyed.Where(x => !x.ok).OrderByDescending(x => x.t, Vi) : keyed.Where(x => !x.ok).OrderBy(x => x.t, Vi);
            ordered = good.Concat(bad).Select(x => (x.r, x.t));
        }
        return ordered.Select(x => x.r).Concat(empty).ToList();
    }

    private static Kind KindOf(IEnumerable<string> values)
    {
        int n = 0, num = 0, date = 0;
        foreach (var v in values.Take(400))
        {
            n++;
            if (TryKey(v, Kind.Date, out _)) date++;
            else if (TryKey(v, Kind.Number, out _)) num++;
        }
        if (n == 0) return Kind.Text;
        if (date * 10 >= n * 8) return Kind.Date;
        if (num * 10 >= n * 8) return Kind.Number;
        return Kind.Text;
    }

    private static bool TryKey(string s, Kind kind, out double key)
    {
        key = 0;
        if (kind == Kind.Date)
        {
            var m = DateDmy.Match(s);
            if (m.Success)
            {
                int d = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), y = int.Parse(m.Groups[3].Value);
                if (y < 100) y += 2000;
                if (mo is < 1 or > 12 || d is < 1 or > 31) return false;
                key = y * 1e8 + mo * 1e6 + d * 1e4 + (m.Groups[5].Success ? int.Parse(m.Groups[5].Value) * 100 + int.Parse(m.Groups[6].Value) : 0);
                return true;
            }
            m = DateYmd.Match(s);
            if (m.Success)
            {
                key = int.Parse(m.Groups[1].Value) * 1e8 + int.Parse(m.Groups[2].Value) * 1e6 + int.Parse(m.Groups[3].Value) * 1e4
                    + (m.Groups[5].Success ? int.Parse(m.Groups[5].Value) * 100 + int.Parse(m.Groups[6].Value) : 0);
                return true;
            }
            return false;
        }
        // số theo kiểu của app (1,500.00); bỏ đơn vị / % ở cuối
        var t = Regex.Replace(s, @"\s*(kg|%|kiện|bao|thùng)$", "", RegexOptions.IgnoreCase).Replace(" ", "");
        if (t.Length == 0 || !(char.IsDigit(t[0]) || t[0] is '-' or '+' or '.')) return false;
        return NumFmt.TryParse(t, out key);
    }
}

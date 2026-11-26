using System.Text.RegularExpressions;

namespace KhoLanh.Core.Grid;

/// <summary>
/// Dòng TỔNG cuối bảng (1.54): cột nào được cộng và tổng theo các dòng đang hiện. Tự chọn theo TÊN cột (số lượng / kiện / thùng / bao / net / tổng / tồn / xuất / nhập…; "Kg" trơn thường là kg / đơn vị → không),
/// bỏ cột đơn vị (T.Lượng, kg/đơn vị, Size, ngày, mã, lô, giá…); người dùng bật / tắt từng cột (lưu theo tên cột, áp cho mọi bảng có cột cùng tên).
/// </summary>
public static class GridTotals
{
    private static readonly Regex Include = new(@"(^|[^a-z])(sl|so luong|luong|kien|thung|bao|net|tong|ton|xuat|nhap|tan|qty|quantity|amount|so cai|khoi luong|da lam phieu)([^a-z]|$)", RegexOptions.Compiled);
    private static readonly Regex Exclude = new(@"kg\s*/|/\s*(don vi|dv)\b|don vi|dv\b|t\.\s*luong|^trong luong$|size|ngay|thang|nam\b|^ma\b|\bma (hang|lo|phieu)|\blo\b|han dung|ty le|ti le|%|gia\b|don gia|stt|^#$|\bid\b|so phieu|so cont|so xe|hop dong|vi tri|ghi chu|dac tinh|ten\b|khach|ky\b|thoi gian|gio\b", RegexOptions.Compiled);

    /// <summary>Cột có nên cộng mặc định không (theo tên cột).</summary>
    public static bool DefaultSum(string header)
    {
        var h = Regex.Replace(GridFilter.Fold(header ?? "").Trim(), @"\s+", " ");
        if (h.Length == 0) return false;
        // "Trọng lượng (kg)" / "Tổng trọng lượng" là tổng → cộng; "Trọng lượng" trơn hay "T.Lượng" là kg / đơn vị → không
        if (Regex.IsMatch(h, @"trong luong") && !Regex.IsMatch(h, @"^t\.") && (h.Contains("(kg)") || h.Contains("tong") || h.Contains("ton") || h.Contains("xuat") || h.Contains("nhap"))) return !Regex.IsMatch(h, @"kg\s*/|/\s*(don vi|dv)\b");
        // 26/11: cột số liệu theo kg ghi đơn vị trong ngoặc ("Kiểm kê (kg)", "Kế toán (kg)", "Kho gửi (kg)", "Chênh (kg)") là tổng kg → cộng
        if (Regex.IsMatch(h, @"\((kg|kien|thung|bao)\)") && !Regex.IsMatch(h, @"kg\s*/|/\s*(don vi|dv)\b|don gia|gia\b|%|ty le|ti le")) return true;
        return Include.IsMatch(h) && !Exclude.IsMatch(h);
    }

    /// <summary>Tổng từng cột (null = không cộng / không có số) trên các dòng <paramref name="rows"/>.</summary>
    /// <param name="keepRow">dòng trông như dòng tổng / nhóm nhưng vẫn cộng (vd ngày đang thu gọn chỉ còn dòng tổng ngày).</param>
    public static double?[] Sum(GridModel m, IReadOnlyList<int> rows, Func<int, bool> sumCol, Func<int, bool>? rowFilter = null, Func<int, bool>? keepRow = null)
    {
        var res = new double?[m.ColCount];
        var cols = Enumerable.Range(0, m.ColCount).Where(sumCol).ToArray();
        // cột ghi số kiểu Việt đã định dạng ("3.891", "35.019,00") → dấu chấm là phân cách nghìn
        var vi = cols.ToDictionary(c => c, c => IsViFormat(rows.Where(r => r >= 0 && r < m.RowCount).Select(r => m.Get(r, c))));
        foreach (var r in rows)
        {
            if (r < 0 || r >= m.RowCount || (rowFilter is not null && !rowFilter(r)) || (IsSubtotalRow(m, r) && keepRow?.Invoke(r) != true)) continue;
            foreach (var c in cols)
            {
                var v = m.Get(r, c);
                if (v.Length > 0 && Parse(v, vi[c], out var d)) res[c] = (res[c] ?? 0) + d;
            }
        }
        return res;
    }

    private static readonly Regex ViNum = new(@"^[+-]?\d{1,3}(\.\d{3})+(,\d+)?$|^[+-]?\d+,(\d{1,2}|\d{4,})$", RegexOptions.Compiled);
    private static readonly Regex Dotted = new(@"^[+-]?\d+\.(\d+)$", RegexOptions.Compiled);
    /// <summary>Cột dùng dấu chấm phân cách nghìn: có số dạng 1.234,5 / 1.234.567 / 12,5, hoặc mọi số có dấu chấm đều đúng 3 chữ số sau dấu chấm.</summary>
    public static bool IsViFormat(IEnumerable<string> values)
    {
        int dotted = 0, dotted3 = 0;
        foreach (var raw in values)
        {
            var v = (raw ?? "").Trim().Replace(" ", "");
            if (v.Length == 0) continue;
            if (v.Contains(',') && ViNum.IsMatch(v)) return true;
            if (Regex.IsMatch(v, @"^[+-]?\d{1,3}(\.\d{3}){2,}$")) return true;
            var m = Dotted.Match(v);
            if (m.Success) { dotted++; if (m.Groups[1].Value.Length == 3) dotted3++; }
        }
        return dotted > 0 && dotted == dotted3;
    }
    private static bool Parse(string v, bool vi, out double d)
    {
        if (!vi) return GridFilter.TryNumber(v, out d);
        var t = v.Trim().Replace(" ", "").Replace(".", "").Replace(',', '.');
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
    }

    private static readonly Regex SubtotalText = new(@"^(tong(?!\s+hop)|cong|ca nam|total|sum|subtotal)(\s|:|$)|^[▾▸▼►]", RegexOptions.Compiled);
    /// <summary>Dòng tổng / dòng nhóm có sẵn trong bảng ("TỔNG CỘNG", "Tổng ngày…", "Cả năm", "▾ 20/11/2026") — không cộng vào dòng Tổng cuối bảng (tránh cộng 2 lần).</summary>
    public static bool IsSubtotalRow(GridModel m, int r)
    {
        for (var c = 0; c < Math.Min(3, m.ColCount); c++)
        {
            var v = GridFilter.Fold(m.Get(r, c)).Trim();
            if (v.Length > 0 && SubtotalText.IsMatch(v)) return true;
        }
        return false;
    }

    /// <summary>Khóa lưu bật / tắt theo tên cột.</summary>
    public static string Key(string header) => Regex.Replace(GridFilter.Fold(header ?? "").Trim(), @"\s+", " ");
}

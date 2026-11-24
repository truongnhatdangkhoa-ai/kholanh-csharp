using System.Text.RegularExpressions;

namespace KhoLanh.Core.Grid;

/// <summary>
/// Dòng TỔNG cuối bảng (1.54): cột nào được cộng và tổng theo các dòng đang hiện. Tự chọn theo TÊN cột (số lượng / kiện / thùng / bao / kg / net / tổng / tồn / xuất / nhập…),
/// bỏ cột đơn vị (T.Lượng, kg/đơn vị, Size, ngày, mã, lô, giá…); người dùng bật / tắt từng cột (lưu theo tên cột, áp cho mọi bảng có cột cùng tên).
/// </summary>
public static class GridTotals
{
    private static readonly Regex Include = new(@"(^|[^a-z])(sl|so luong|luong|kien|thung|bao|kg|net|tong|ton|xuat|nhap|tan|qty|quantity|amount|so cai|khoi luong)([^a-z]|$)", RegexOptions.Compiled);
    private static readonly Regex Exclude = new(@"kg\s*/|/\s*(don vi|dv)\b|don vi|dv\b|t\.\s*luong|^trong luong$|size|ngay|thang|nam\b|^ma\b|\bma (hang|lo|phieu)|\blo\b|han dung|ty le|ti le|%|gia\b|don gia|stt|^#$|\bid\b|so phieu|so cont|so xe|hop dong|vi tri|ghi chu|dac tinh|ten\b|khach|ky\b|thoi gian|gio\b", RegexOptions.Compiled);

    /// <summary>Cột có nên cộng mặc định không (theo tên cột).</summary>
    public static bool DefaultSum(string header)
    {
        var h = Regex.Replace(GridFilter.Fold(header ?? "").Trim(), @"\s+", " ");
        if (h.Length == 0) return false;
        // "Trọng lượng (kg)" / "Tổng trọng lượng" là tổng → cộng; "Trọng lượng" trơn hay "T.Lượng" là kg / đơn vị → không
        if (Regex.IsMatch(h, @"trong luong") && !Regex.IsMatch(h, @"^t\.") && (h.Contains("(kg)") || h.Contains("tong") || h.Contains("ton") || h.Contains("xuat") || h.Contains("nhap"))) return !Regex.IsMatch(h, @"kg\s*/|/\s*(don vi|dv)\b");
        return Include.IsMatch(h) && !Exclude.IsMatch(h);
    }

    /// <summary>Tổng từng cột (null = không cộng / không có số) trên các dòng <paramref name="rows"/>.</summary>
    public static double?[] Sum(GridModel m, IReadOnlyList<int> rows, Func<int, bool> sumCol, Func<int, bool>? rowFilter = null)
    {
        var res = new double?[m.ColCount];
        var cols = Enumerable.Range(0, m.ColCount).Where(sumCol).ToArray();
        foreach (var r in rows)
        {
            if (r < 0 || r >= m.RowCount || (rowFilter is not null && !rowFilter(r))) continue;
            foreach (var c in cols)
            {
                var v = m.Get(r, c);
                if (v.Length > 0 && GridFilter.TryNumber(v, out var d)) res[c] = (res[c] ?? 0) + d;
            }
        }
        return res;
    }

    /// <summary>Khóa lưu bật / tắt theo tên cột.</summary>
    public static string Key(string header) => Regex.Replace(GridFilter.Fold(header ?? "").Trim(), @"\s+", " ");
}

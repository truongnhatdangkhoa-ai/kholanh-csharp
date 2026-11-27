using System.Globalization;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.Grid;

/// <summary>
/// ĐỊNH DẠNG SỐ DUY NHẤT CỦA APP (1.71, người dùng chốt): dấu phẩy ngăn nghìn, dấu chấm thập phân — <c>1,500.00</c> = một nghìn năm trăm,
/// <c>1.5</c> = một phẩy năm. Trọng lượng / Net: 2 số lẻ (<c>1,500.00 kg</c>); số lượng kiện / bao / thùng: số nguyên (<c>1,500</c>).
/// Gõ vào cũng theo đúng kiểu này (không còn hiểu kiểu cũ 1.500,5 khi gõ). Ngày tháng giữ kiểu Việt (dd/MM/yyyy).
/// <see cref="Culture"/> = vi-VN nhưng phần số theo kiểu trên — đặt làm văn hóa mặc định của app nên mọi chỗ định dạng theo văn hóa hiện hành
/// (ToString("N2"), "#,0.##"…) tự theo kiểu mới.
/// </summary>
public static class NumFmt
{
    public static readonly CultureInfo Culture = Make();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static CultureInfo Make()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("vi-VN").Clone();
        var n = c.NumberFormat;
        n.NumberDecimalSeparator = n.CurrencyDecimalSeparator = n.PercentDecimalSeparator = ".";
        n.NumberGroupSeparator = n.CurrencyGroupSeparator = n.PercentGroupSeparator = ",";
        return CultureInfo.ReadOnly(c);
    }

    public enum Cls { None, Kg, Count }

    /// <summary>Loại cột theo tên: trọng lượng / net (2 số lẻ) · số lượng kiện / bao / thùng (số nguyên) · khác (giữ nguyên).</summary>
    public static Cls ClassOf(string? header)
    {
        var h = Regex.Replace(GridFilter.Fold(header ?? "").Trim(), @"\s+", " ");
        if (h.Length == 0 || Regex.IsMatch(h, @"%|ty le|ti le|\bgia\b|don gia|ngay|size|\bma\b|so phieu|so dong|stt|\blo\b|day|tang|khoang|cot")) return Cls.None;
        if (Regex.IsMatch(h, @"\bkg\b|\bnet\b|trong luong|khoi luong|\btl\b|t\.\s*luong|\btan\b")) return Cls.Kg;
        if (Regex.IsMatch(h, @"so luong|\bsl\b|kien|\bbao\b|thung|\bqty\b|so dem|\bton\b.*\((bao|kien|thung)\)")) return Cls.Count;
        return Cls.None;
    }

    // 26/11: số âm rất nhỏ làm tròn ra -0 → .NET in "-0.00"; "+ 0.0" đổi -0 thành 0
    public static string Kg(double v) => (Math.Round(v, 2, MidpointRounding.AwayFromZero) + 0.0).ToString("#,0.00", Inv);
    public static string Count(double v) => (Math.Round(v, 2, MidpointRounding.AwayFromZero) + 0.0).ToString("#,0.##", Inv);
    public static string Num(double v) => (Math.Round(v, 3, MidpointRounding.AwayFromZero) + 0.0).ToString("#,0.###", Inv);
    public static string Of(double v, Cls cls) => cls == Cls.Kg ? Kg(v) : cls == Cls.Count ? Count(v) : Num(v);

    private static readonly Regex Strict = new(@"^[+-]?(\d{1,3}(,\d{3})+|\d+)(\.\d+)?$|^[+-]?\.\d+$", RegexOptions.Compiled);
    /// <summary>Đọc số theo ĐÚNG kiểu của app: 1,500.00 / 1500.5 / -2 / .5 — "1.500,5" hay "1,5" không phải số.</summary>
    public static bool TryParse(string? s, out double v)
    {
        v = 0;
        var t = (s ?? "").Trim().Replace(" ", "");
        if (t.Length == 0 || !Strict.IsMatch(t)) return false;
        return double.TryParse(t.Replace(",", ""), NumberStyles.Float, Inv, out v);
    }

    private static readonly Regex ViNum = new(@"^[+-]?(\d{1,3}(\.\d{3})+|\d+)(,\d+)?$", RegexOptions.Compiled);
    /// <summary>Đọc số kiểu Việt cũ (1.234,5) — chỉ dùng cho DỮ LIỆU ĐÃ LƯU từ trước (cột được nhận ra là kiểu Việt), không dùng cho chữ người dùng gõ.</summary>
    public static bool TryParseVi(string? s, out double v)
    {
        v = 0;
        var t = (s ?? "").Trim().Replace(" ", "");
        if (t.Length == 0 || !ViNum.IsMatch(t)) return false;
        return double.TryParse(t.Replace(".", "").Replace(',', '.'), NumberStyles.Float, Inv, out v);
    }

    /// <summary>Chữ hiển thị của 1 ô số: đọc theo kiểu app (hoặc kiểu Việt nếu cả cột là dữ liệu cũ kiểu Việt), viết lại theo loại cột.
    /// Ô không phải số (mã, chữ, ngày…) giữ nguyên.</summary>
    public static string Show(string text, Cls cls, bool viColumn)
    {
        if (string.IsNullOrEmpty(text) || (cls == Cls.None && !viColumn)) return text;
        var t = text.Trim();
        if (t.Length == 0 || !(char.IsDigit(t[0]) || t[0] is '-' or '+' or '.')) return text;
        if (t.Length > 1 && t[0] == '0' && char.IsDigit(t[1])) return text;   // mã như 0123
        double v;
        if (viColumn ? !TryParseVi(t, out v) && !TryParse(t, out v) : !TryParse(t, out v)) return text;
        return Of(v, cls);
    }
}

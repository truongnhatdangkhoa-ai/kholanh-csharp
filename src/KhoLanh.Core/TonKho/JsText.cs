using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// Các hàm chữ / số dùng chung của 2 module Tồn kho (port đúng từng ký tự từ ton-kho-an-an.js / ton-kho-gui.js) —
/// để số liệu, cách so khớp tên cột và cách hiện số khớp hệt bản WebView2 cũ.
/// </summary>
public static class JsText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>fuzzyNormalize: bỏ dấu, đ→d, chữ thường, dấu câu .,()/\-_:; → khoảng trắng, gộp khoảng trắng.</summary>
    public static string Fuzzy(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (c >= '\u0300' && c <= '\u036f') continue;
            sb.Append(c is 'đ' or 'Đ' ? 'd' : c);
        }
        var t = JsLower(sb.ToString());
        t = Regex.Replace(t, @"[.,()/\\\-_:;]", " ");
        return JsTrim(JsCollapse(t));
    }

    /// <summary>normalize: trim, chữ thường, gộp khoảng trắng.</summary>
    public static string Norm(string? s) => JsCollapse(JsLower(JsTrim(s ?? "")));

    public static string NoSpace(string? s) => Regex.Replace(s ?? "", @"\s+", "");

    /// <summary>normalizeCellForExactFilter: NFC, bỏ ký tự rộng 0, NBSP → cách, gộp khoảng trắng, trim.</summary>
    public static string Exact(string? s)
    {
        var t = (s ?? "").Normalize(NormalizationForm.FormC);
        t = Regex.Replace(t, "[\u200B-\u200D\uFEFF]", "").Replace('\u00A0', ' ');
        return JsTrim(JsCollapse(t));
    }

    public static bool HeadersMatch(string? a, string? b)
    {
        var fa = Fuzzy(a); var fb = Fuzzy(b);
        if (fa.Length == 0 || fb.Length == 0) return false;
        return fa == fb || fa.Contains(fb, StringComparison.Ordinal) || fb.Contains(fa, StringComparison.Ordinal);
    }

    public static int FindCol(IReadOnlyList<string> headers, string label)
    {
        for (var i = 0; i < headers.Count; i++) if (HeadersMatch(headers[i], label)) return i;
        return -1;
    }

    public static int FindColExact(IReadOnlyList<string> headers, string label)
    {
        var f = Fuzzy(label);
        for (var i = 0; i < headers.Count; i++) if (Fuzzy(headers[i]) == f) return i;
        return -1;
    }

    /// <summary>parseLooseNumber: "1,234.5" / "1.234,5" / "12,5" (= 12.5) / "1,234" (= 1234); NaN nếu không phải số.</summary>
    public static double LooseNum(string? v)
    {
        var s = JsTrim(v ?? "");
        if (s.Length == 0) return double.NaN;
        s = Regex.Replace(s, @"[\u00A0\s]", "");
        var hasComma = s.Contains(','); var hasDot = s.Contains('.');
        if (hasComma && hasDot) s = s.LastIndexOf(',') > s.LastIndexOf('.') ? ReplaceFirst(s.Replace(".", ""), ",", ".") : s.Replace(",", "");
        else if (hasComma) s = Regex.IsMatch(s, @"^-?\d+,\d{1,2}$") ? ReplaceFirst(s, ",", ".") : s.Replace(",", "");
        var m = Regex.Match(s, @"^-?\d+(\.\d+)?");
        return m.Success ? double.Parse(m.Value, NumberStyles.Float, Inv) : double.NaN;
    }

    private static string ReplaceFirst(string s, string a, string b) { var i = s.IndexOf(a, StringComparison.Ordinal); return i < 0 ? s : s[..i] + b + s[(i + a.Length)..]; }

    /// <summary>Number(x) của JavaScript cho chuỗi: "" / khoảng trắng → 0; "1,170" → NaN; "  12 " → 12; "0x1A" → 26.</summary>
    public static double JsNumber(string? v)
    {
        var s = JsTrim(v ?? "");
        if (s.Length == 0) return 0;
        if (Regex.IsMatch(s, @"^0[xX][0-9a-fA-F]+$")) return (double)BigInteger.Parse("0" + s[2..], NumberStyles.HexNumber);
        if (s is "Infinity" or "+Infinity") return double.PositiveInfinity;
        if (s == "-Infinity") return double.NegativeInfinity;
        if (!Regex.IsMatch(s, @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$")) return double.NaN;
        return double.Parse(s, NumberStyles.Float, Inv);
    }

    /// <summary>String(n) của JavaScript cho số thường gặp (số nguyên, số lẻ ngắn).</summary>
    public static string JsStr(double n)
    {
        if (double.IsNaN(n)) return "NaN";
        if (n == 0) return "0";
        if (Math.Abs(n) >= 1e21 || Math.Abs(n) < 1e-6) return n.ToString("R", Inv).Replace("E+", "e+").Replace("E-", "e-");
        return n.ToString("R", Inv);
    }

    // ------------------------------------------------------------ toLocaleString('en-US')

    /// <summary>n.toLocaleString('en-US', {minimumFractionDigits: min, maximumFractionDigits: max}) — làm tròn nửa-chẵn trên giá trị nhị phân thật (giống ICU).</summary>
    public static string En(double n, int min, int max)
    {
        if (double.IsNaN(n)) return "NaN";
        if (double.IsInfinity(n)) return n > 0 ? "∞" : "-∞";
        var (neg, intPart, frac) = RoundExact(n, max);
        while (frac.Length > min && frac.EndsWith('0')) frac = frac[..^1];
        var g = Group(intPart);
        var s = frac.Length > 0 ? g + "." + frac : g;
        if (neg && (intPart.Trim('0').Length > 0 || frac.Trim('0').Length > 0)) s = "-" + s;
        return s;
    }

    public static string En2(double n) => En(n, 2, 2);

    private static string Group(string digits)
    {
        digits = digits.TrimStart('0'); if (digits.Length == 0) digits = "0";
        var sb = new StringBuilder();
        for (var i = 0; i < digits.Length; i++) { if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(','); sb.Append(digits[i]); }
        return sb.ToString();
    }

    /// <summary>Làm tròn |n| đến <paramref name="dec"/> chữ số thập phân theo nửa-chẵn trên giá trị CHÍNH XÁC của số double.</summary>
    private static (bool neg, string intPart, string frac) RoundExact(double n, int dec)
    {
        var neg = n < 0 || (n == 0 && double.IsNegative(n));
        var bits = BitConverter.DoubleToInt64Bits(Math.Abs(n));
        var exp = (int)((bits >> 52) & 0x7FF);
        var man = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0) exp++; else man |= 1L << 52;
        exp -= 1075;
        // |n| = man * 2^exp ; cần round(|n| * 10^dec) = round(man * 10^dec * 2^exp)
        BigInteger num = new BigInteger(man) * BigInteger.Pow(10, dec), den = BigInteger.One;
        if (exp > 0) num <<= exp; else den <<= -exp;
        var q = BigInteger.DivRem(num, den, out var r);
        var twice = r * 2;
        if (twice > den || (twice == den && !q.IsEven)) q += 1;
        var s = q.ToString(Inv).PadLeft(dec + 1, '0');
        return (neg, dec == 0 ? s : s[..(s.Length - dec)], dec == 0 ? "" : s[(s.Length - dec)..]);
    }

    /// <summary>Math.round của JavaScript (nửa lên về +∞).</summary>
    public static double JsRound(double v) => Math.Floor(v + 0.5);

    // ------------------------------------------------------------ chuẩn chữ kiểu JS

    private static string JsLower(string s) => s.ToLowerInvariant();
    private static string JsCollapse(string s) => Regex.Replace(s, @"\s+", " ");
    /// <summary>String.prototype.trim của JS (khoảng trắng Unicode + BOM).</summary>
    public static string JsTrim(string s) => s.Trim(TrimChars);
    private static readonly char[] TrimChars = " \t\n\v\f\r\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF".ToCharArray();

    // ------------------------------------------------------------ sắp xếp kiểu localeCompare('vi', {numeric:true, sensitivity:'base'})

    private static readonly CompareInfo ViCompare = CultureInfo.GetCultureInfo("vi-VN").CompareInfo;

    public static int ViCompareBase(string a, string b) => ViCompare.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType);

    /// <summary>localeCompare(…,'vi',{numeric:true, sensitivity:'base'}): dãy số so theo giá trị.</summary>
    public static int ViNumericCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0'); var nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length < nb.Length ? -1 : 1;
                var c = string.CompareOrdinal(na, nb);
                if (c != 0) return c < 0 ? -1 : 1;
                continue;
            }
            int ei = i, ej = j;
            while (ei < a.Length && !char.IsAsciiDigit(a[ei])) ei++;
            while (ej < b.Length && !char.IsAsciiDigit(b[ej])) ej++;
            var ta = a[i..ei]; var tb = b[j..ej];
            // so cả đoạn chữ: phần chung trước, đoạn ngắn hơn đứng trước nếu là tiền tố của đoạn kia và đều tiếp nối bằng số
            var cmp = ViCompareBase(ta, tb);
            if (cmp != 0)
            {
                // "A" vs "AB1": nếu ta là tiền tố của tb và a tiếp theo là số → so ký tự số với chữ như ICU (số đứng trước chữ)
                if (ei < a.Length && tb.StartsWith(ta, StringComparison.OrdinalIgnoreCase) && tb.Length > ta.Length) return -1;
                if (ej < b.Length && ta.StartsWith(tb, StringComparison.OrdinalIgnoreCase) && ta.Length > tb.Length) return 1;
                return cmp < 0 ? -1 : 1;
            }
            i = ei; j = ej;
        }
        if (i < a.Length) return 1;
        if (j < b.Length) return -1;
        return 0;
    }

    /// <summary>localeCompare(…,'vi') mặc định (phân biệt hoa thường ở bậc cuối).</summary>
    public static int ViCompareDefault(string a, string b) => ViCompare.Compare(a, b, CompareOptions.None);
}

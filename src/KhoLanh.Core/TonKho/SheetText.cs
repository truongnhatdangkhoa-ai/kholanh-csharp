using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// Đọc file Excel thành bảng chữ GIỐNG HỆT XLSX.utils.sheet_to_json(ws, {header:1, defval:'', raw:false}) của SheetJS trong app cũ:
/// mỗi ô lấy đúng CHỮ ĐANG HIỆN theo định dạng số của ô (VD số 30 định dạng "#,###.##" → "30.", 3 định dạng "#,##0.###0" → "3.0000").
/// Nhờ vậy dữ liệu nạp vào (sheet LoadData) khớp từng ký tự với bản WebView2.
/// </summary>
public static partial class SheetText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed class Book
    {
        public List<string> SheetNames = new();
        public Dictionary<string, List<string[]>> Sheets = new();
    }

    public static Book Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".csv") return ReadCsv(path);
        if (ext == ".xls") return XlsBiff.Read(path);
        // 1.72 (N4): đọc thẳng gói xlsx (zip + XML, đọc tuần tự) — nhanh hơn nhiều so với nạp cả workbook bằng ClosedXML; lỗi lạ thì quay về cách cũ
        if (UseFast && ext is ".xlsx" or ".xlsm")
        {
            try { return ReadXlsxFast(path); }
            catch (Exception e) when (e is not OutOfMemoryException) { /* file lạ → cách cũ bên dưới */ }
        }
        return ReadClosedXml(path);
    }
    /// <summary>Tắt bộ đọc nhanh (thử nghiệm so sánh).</summary>
    public static bool UseFast { get; set; } = true;

    public static Book ReadClosedXml(string path)
    {
        using var wb = new XLWorkbook(path);
        var book = new Book();
        foreach (var ws in wb.Worksheets)
        {
            book.SheetNames.Add(ws.Name);
            book.Sheets[ws.Name] = ReadSheet(ws);
        }
        return book;
    }

    private static List<string[]> ReadSheet(IXLWorksheet ws)
    {
        var rows = new List<string[]>();
        var used = ws.RangeUsed(XLCellsUsedOptions.Contents);
        if (used is null) return rows;
        int r1 = used.FirstRow().RowNumber(), r2 = used.LastRow().RowNumber(), c1 = used.FirstColumn().ColumnNumber(), c2 = used.LastColumn().ColumnNumber();
        for (var r = r1; r <= r2; r++)
        {
            var arr = new string[c2 - c1 + 1];
            for (var c = c1; c <= c2; c++) arr[c - c1] = CellText(ws.Cell(r, c));
            rows.Add(arr);
        }
        return rows;
    }

    private static string CellText(IXLCell cell)
    {
        XLCellValue v;
        try { v = cell.HasFormula ? cell.CachedValue : cell.Value; } catch { return cell.GetFormattedString(); }
        switch (v.Type)
        {
            case XLDataType.Blank: return "";
            case XLDataType.Text: return v.GetText();
            case XLDataType.Boolean: return v.GetBoolean() ? "TRUE" : "FALSE";
            case XLDataType.Error: return "#" + v.GetError().ToString().ToUpperInvariant();
            case XLDataType.Number:
            {
                var nf = cell.Style.NumberFormat;
                return FormatNumber(v.GetNumber(), nf.Format, nf.NumberFormatId);
            }
            case XLDataType.DateTime: return FormatNumber(v.GetDateTime().ToOADate(), cell.Style.NumberFormat.Format, cell.Style.NumberFormat.NumberFormatId);
            case XLDataType.TimeSpan: return FormatNumber(v.GetTimeSpan().TotalDays, cell.Style.NumberFormat.Format, cell.Style.NumberFormat.NumberFormatId);
            default: return cell.GetFormattedString();
        }
    }

    private static readonly Dictionary<int, string> Builtin = new()
    {
        [0] = "General", [1] = "0", [2] = "0.00", [3] = "#,##0", [4] = "#,##0.00", [9] = "0%", [10] = "0.00%", [11] = "0.00E+00",
        [12] = "# ?/?", [13] = "# ??/??", [14] = "m/d/yy", [15] = "d-mmm-yy", [16] = "d-mmm", [17] = "mmm-yy", [18] = "h:mm AM/PM",
        [19] = "h:mm:ss AM/PM", [20] = "h:mm", [21] = "h:mm:ss", [22] = "m/d/yy h:mm", [37] = "#,##0 ;(#,##0)", [38] = "#,##0 ;[Red](#,##0)",
        [39] = "#,##0.00;(#,##0.00)", [40] = "#,##0.00;[Red](#,##0.00)", [45] = "mm:ss", [46] = "[h]:mm:ss", [47] = "mmss.0", [48] = "##0.0E+0", [49] = "@",
    };

    /// <summary>Định dạng 1 số theo mã định dạng Excel như SSF (SheetJS). Hỗ trợ các mẫu hay gặp; mẫu lạ → dạng General.</summary>
    public static string FormatNumber(double n, string? fmt, int fmtId = -1)
    {
        var f = string.IsNullOrEmpty(fmt) ? (fmtId >= 0 && Builtin.TryGetValue(fmtId, out var b) ? b : "General") : fmt!;
        if (f.Equals("General", StringComparison.OrdinalIgnoreCase) || f == "@") return General(n);
        // nhiều đoạn ;  → chọn đoạn theo dấu (1.72: tách / làm sạch đoạn 1 lần cho mỗi mã định dạng — trước chạy regex ở MỌI ô số)
        var plan = Plans.GetOrAdd(f, BuildPlan);
        // số lặp lại rất nhiều trong file tồn (số kiện, trọng lượng gói…) → nhớ chữ đã định dạng theo từng mã định dạng
        if (plan.Memo.TryGetValue(n, out var hit)) return hit;
        var res = FormatWith(plan, n);
        if (plan.Memo.Count < 50_000) plan.Memo.TryAdd(n, res);
        return res;
    }
    private static string FormatWith(Plan plan, double n)
    {
        int k; var useAbs = false;
        if (plan.Secs.Length >= 2 && n < 0) { k = 1; useAbs = true; }
        else if (plan.Secs.Length >= 3 && n == 0) k = 2;
        else k = 0;
        if (plan.Date[k]) return FormatDate(n, plan.Secs[k]);
        var v = useAbs ? Math.Abs(n) : n;
        return FormatNumeric(v, plan.Secs[k]);
    }
    private sealed record Plan(string[] Secs, bool[] Date)
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<double, string> Memo = new();
    }
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Plan> Plans = new();
    private static Plan BuildPlan(string f)
    {
        var secs = SplitSections(f).Select(sec =>
        {
            sec = Regex.Replace(sec, @"\[(Red|Black|Blue|Green|Cyan|Magenta|White|Yellow|Color\s*\d+)\]", "", RegexOptions.IgnoreCase);
            return Regex.Replace(sec, @"\[\$[^\]]*\]", "");
        }).ToArray();
        return new Plan(secs, secs.Select(IsDate).ToArray());
    }

    private static List<string> SplitSections(string f)
    {
        var res = new List<string>(); var sb = new StringBuilder(); var q = false;
        for (var i = 0; i < f.Length; i++)
        {
            var ch = f[i];
            if (ch == '"') q = !q;
            if (ch == '\\' && i + 1 < f.Length) { sb.Append(ch).Append(f[++i]); continue; }
            if (ch == ';' && !q) { res.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        res.Add(sb.ToString());
        return res;
    }

    private static bool IsDate(string sec)
    {
        var s = Regex.Replace(sec, "\"[^\"]*\"", "");
        s = Regex.Replace(s, @"\\.", "");
        return Regex.IsMatch(s, "[dDyY]|[hH]|[sS]") || (Regex.IsMatch(s, "[mM]") && !Regex.IsMatch(s, "[0#?]"));
    }

    /// <summary>Dạng General của SSF: tối đa 11 chữ số có nghĩa, bỏ số 0 thừa.</summary>
    public static string General(double v)
    {
        if (v == 0) return "0";
        if (v == Math.Floor(v) && Math.Abs(v) < 1e11) return v.ToString("0", Inv);
        var a = Math.Abs(v);
        if (a >= 1e11 || a < 1e-9)
        {
            var e = v.ToString("0.#####E+0", Inv);
            return e;
        }
        var s = v.ToString("G11", Inv);
        if (s.Contains('E')) s = v.ToString("0.##########", Inv);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        // SSF: tối đa 11 ký tự số cho số lẻ
        var digits = s.Replace("-", "").Replace(".", "").TrimStart('0').Length;
        if (digits > 11)
        {
            var intLen = Math.Floor(Math.Log10(a)) + 1;
            var dec = (int)Math.Max(0, 11 - Math.Max(1, intLen));
            s = Math.Round(v, Math.Min(dec, 15), MidpointRounding.AwayFromZero).ToString("0." + new string('#', Math.Max(1, dec)), Inv);
        }
        return s;
    }

    /// <summary>Mẫu số kiểu "#,##0.00", "#,###.##", "0", "0.0%", "#,##0.###0" theo cách SSF hiện.</summary>
    private static string FormatNumeric(double v, string sec)
    {
        // tách phần chữ (trong ngoặc kép / sau \) ở đầu-cuối
        var prefix = new StringBuilder(); var suffix = new StringBuilder(); var core = new StringBuilder();
        var stage = 0;
        for (var i = 0; i < sec.Length; i++)
        {
            var ch = sec[i];
            string lit;
            if (ch == '"') { var j = sec.IndexOf('"', i + 1); if (j < 0) j = sec.Length; lit = sec[(i + 1)..j]; i = j; }
            else if (ch == '\\' && i + 1 < sec.Length) { lit = sec[i + 1].ToString(); i++; }
            else if (ch == '_' && i + 1 < sec.Length) { lit = " "; i++; }
            else if (ch == '*' && i + 1 < sec.Length) { lit = ""; i++; }
            else if ("0#?,.%Ee+-".Contains(ch) && !(ch is '+' or '-' && stage == 0)) { stage = 1; core.Append(ch); if (ch == '%' ) { } continue; }
            else lit = ch.ToString();
            if (stage == 0) prefix.Append(lit); else { suffix.Append(lit); stage = 2; }
        }
        var c = core.ToString();
        if (c.Length == 0) return prefix.ToString() + suffix;
        var pct = c.Count(ch => ch == '%');
        c = c.Replace("%", "");
        if (c.Contains('E')) return prefix + v.ToString(c.Replace("+", "+"), Inv) + suffix;
        for (var k = 0; k < pct; k++) v *= 100;
        // dấu , cuối phần nguyên = chia 1000
        var dot = c.IndexOf('.');
        var ip = dot < 0 ? c : c[..dot];
        var fp = dot < 0 ? "" : c[(dot + 1)..];
        while (ip.EndsWith(',')) { ip = ip[..^1]; v /= 1000; }
        var grouping = ip.Contains(',');
        ip = ip.Replace(",", "");
        var minInt = ip.Count(ch => ch == '0');
        var fixedDec = fp.Count(ch => ch is '0' or '#' or '?');
        var zerosInFrac = fp.Contains('0');
        var neg = v < 0;
        var a = Math.Abs(v);
        string intS, fracS;
        if (fixedDec > 0)
        {
            // SSF: phần lẻ có số 0 → giữ đủ số chữ số; chỉ có # → bỏ 0 thừa
            var s = JsText.En(a, fixedDec, fixedDec).Replace(",", "");
            var p = s.Split('.');
            intS = p[0]; fracS = p.Length > 1 ? p[1] : "";
            if (!zerosInFrac) fracS = fracS.TrimEnd('0');
        }
        else
        {
            intS = JsText.En(a, 0, 0).Replace(",", ""); fracS = "";
        }
        if (intS == "0" && minInt == 0) intS = "";
        if (intS.Length < minInt) intS = intS.PadLeft(minInt, '0');
        if (grouping && intS.Length > 3)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < intS.Length; i++) { if (i > 0 && (intS.Length - i) % 3 == 0) sb.Append(','); sb.Append(intS[i]); }
            intS = sb.ToString();
        }
        var body = dot >= 0 ? intS + "." + fracS : intS;
        if (neg && (intS.Trim('0').Length > 0 || fracS.Trim('0').Length > 0)) body = "-" + body;
        return prefix + body + new string('%', pct) + suffix;
    }

    private static readonly string[] Months = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
    private static readonly string[] Days = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    /// <summary>Ngày / giờ theo mẫu Excel (d, dd, m, mm, mmm, yy, yyyy, h, hh, mm (phút), ss, AM/PM).</summary>
    private static string FormatDate(double serial, string sec)
    {
        DateTime d;
        try { d = DateTime.FromOADate(serial); } catch { return General(serial); }
        // làm tròn tới giây như SSF
        d = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second).AddSeconds(d.Millisecond >= 500 ? 1 : 0);
        var toks = new List<string>();
        var s = sec;
        var ampm = Regex.IsMatch(s, "AM/PM|A/P", RegexOptions.IgnoreCase);
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length;)
        {
            var ch = s[i];
            if (ch == '"') { var j = s.IndexOf('"', i + 1); if (j < 0) j = s.Length; sb.Append(s[(i + 1)..j]); i = j + 1; continue; }
            if (ch == '\\' && i + 1 < s.Length) { sb.Append(s[i + 1]); i += 2; continue; }
            if (s.Length - i >= 5 && s.Substring(i, 5).Equals("AM/PM", StringComparison.OrdinalIgnoreCase)) { sb.Append(d.Hour < 12 ? "AM" : "PM"); i += 5; continue; }
            if (s.Length - i >= 3 && s.Substring(i, 3).Equals("A/P", StringComparison.OrdinalIgnoreCase)) { sb.Append(d.Hour < 12 ? "A" : "P"); i += 3; continue; }
            var lc = char.ToLowerInvariant(ch);
            if (lc is 'y' or 'm' or 'd' or 'h' or 's')
            {
                var j = i; while (j < s.Length && char.ToLowerInvariant(s[j]) == lc) j++;
                var len = j - i;
                toks.Add(lc.ToString());
                switch (lc)
                {
                    case 'y': sb.Append(len <= 2 ? (d.Year % 100).ToString("00") : d.Year.ToString("0000")); break;
                    case 'd': sb.Append(len == 1 ? d.Day.ToString() : len == 2 ? d.Day.ToString("00") : len == 3 ? Days[(int)d.DayOfWeek][..3] : Days[(int)d.DayOfWeek]); break;
                    case 'h': { var h = ampm ? (d.Hour % 12 == 0 ? 12 : d.Hour % 12) : d.Hour; sb.Append(len == 1 ? h.ToString() : h.ToString("00")); break; }
                    case 's': sb.Append(len == 1 ? d.Second.ToString() : d.Second.ToString("00")); break;
                    case 'm':
                    {
                        // m sau h hoặc trước s = phút
                        var prevH = toks.Count >= 2 && toks[^2] == "h";
                        var rest = s[j..]; var nextS = Regex.IsMatch(rest, @"^[^a-zA-Z]*[sS]");
                        if ((prevH || nextS) && len <= 2) sb.Append(len == 1 ? d.Minute.ToString() : d.Minute.ToString("00"));
                        else sb.Append(len == 1 ? d.Month.ToString() : len == 2 ? d.Month.ToString("00") : len == 3 ? Months[d.Month - 1][..3] : len == 5 ? Months[d.Month - 1][..1] : Months[d.Month - 1]);
                        break;
                    }
                }
                i = j; continue;
            }
            sb.Append(ch); i++;
        }
        return sb.ToString();
    }

    private static Book ReadCsv(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var rows = new List<string[]>();
        var cur = new List<string>(); var sb = new StringBuilder(); var q = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (q) { if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(ch); continue; }
            if (ch == '"') { q = true; continue; }
            if (ch == ',') { cur.Add(sb.ToString()); sb.Clear(); continue; }
            if (ch == '\r') continue;
            if (ch == '\n') { cur.Add(sb.ToString()); sb.Clear(); rows.Add(cur.ToArray()); cur.Clear(); continue; }
            sb.Append(ch);
        }
        if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); rows.Add(cur.ToArray()); }
        var w = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        for (var i = 0; i < rows.Count; i++) if (rows[i].Length < w) rows[i] = rows[i].Concat(Enumerable.Repeat("", w - rows[i].Length)).ToArray();
        var b = new Book(); b.SheetNames.Add("Sheet1"); b.Sheets["Sheet1"] = rows;
        return b;
    }
}

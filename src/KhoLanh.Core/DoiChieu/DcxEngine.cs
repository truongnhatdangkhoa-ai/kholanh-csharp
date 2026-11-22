using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.DoiChieu;

/// <summary>
/// ĐỐI CHIẾU HÀNG NGÀY — LÕI XỬ LÝ (viết lại bằng C# theo dcx-engine.js của app cũ, giữ đúng quy tắc KHÔNG ĐOÁN):
///  - Cột chỉ nhận khi TÊN KHỚP NGUYÊN CỤM (bỏ dấu, bỏ khoảng trắng thừa); thiếu / trùng → trả NeedMap để giao diện hỏi.
///  - Dòng có mã mà ngày / số lượng / net không đọc được → dòng LỖI, không tự bỏ, không tự sửa.
///  - Mã so NGUYÊN VĂN (bỏ khoảng trắng, chữ hoa, bỏ "TPC-"); chỉ GỢI Ý khi cùng ngày, cùng SL và Net nhưng khác mã.
///  - File Xưởng: dòng chi tiết trống ngày lấy ngày của DÒNG TỔNG ngay bên dưới.
///  - Net = Số lượng × trọng lượng theo mã (khi biết trọng lượng); Net trong file chỉ để báo "file ghi khác công thức".
///  - Đối chiếu nhanh theo Size + kg/thùng cho dòng Xưởng chưa có mã ↔ mã Kho VRHL/PRHL (VRHO/PRHO khi Xưởng có dòng cùng size).
/// Mảng ô (aoa) là List&lt;object?[]&gt;: double (số / số ngày Excel), string, bool hoặc null.
/// </summary>
public static class DcxEngine
{
    public const double EpsNet = 0.0005;

    // ------------------------------------------------------------------ kiểu dữ liệu

    public sealed class Row
    {
        public int Row1;                 // số dòng Excel (1-based); 0 = dòng ảo ("sửa tay")
        public string RowLabel = "";     // "sửa tay" …
        public string Ngay = "", Ma = "", Ten = "", Size = "", Ghichu = "", Loai = "";
        public double Sl, Net;
        public double? NetFile, W;
        public bool Edited;
        public string RowText => Row1 > 0 ? Row1.ToString(CultureInfo.InvariantCulture) : RowLabel;
        public Row Clone() => (Row)MemberwiseClone();
    }
    public sealed record Total(int Row, string Ngay, double Sl, double Net);
    public sealed class NoCodeRow { public int Row; public string Ngay = "", Label = "", Size = "", Ghichu = ""; public double Sl, Net; }
    public sealed record ErrCell(int Col, string Name, string Value, string Msg);
    public sealed record ErrorRow(int Row, string Ma, string Why, List<ErrCell> Cells);
    public sealed record Conflict(int Row, string Ngay, string Ma, string MaSize, double Sl, double Net);
    public sealed record HeaderCol(int J, string Name, bool Blank = false, bool Numeric = false);
    public sealed class NeedMap
    {
        public string Reason = "";
        public int? HeaderRow;
        public List<HeaderCol> Headers = new();
        public List<string> Fields = new();
        public Dictionary<string, List<int>> Choices = new();
    }
    public sealed class ParseResult
    {
        public bool Ok;
        public NeedMap? Need;
        public List<Row> Rows = new();
        public List<Total> Totals = new();
        public List<ErrorRow> Errors = new();
        public List<NoCodeRow> NoCode = new();
        public List<Conflict> Conflicts = new();
        public int NFromTotal, NMaTuSize, Skipped, HeaderRow;
        public Dictionary<string, int> Map = new();
        public List<HeaderCol> Headers = new();
    }

    // ------------------------------------------------------------------ chuẩn hóa

    public static string Strip(object? v)
    {
        var s = Str(v).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch is 'đ' or 'Đ' ? 'd' : ch);
        }
        return Regex.Replace(sb.ToString().ToLowerInvariant(), @"\s+", " ").Trim();
    }
    /// <summary>Giống String(v) của JS cho giá trị ô.</summary>
    public static string Str(object? v) => v switch
    {
        null => "",
        string s => s,
        double d => JsNum(d),
        bool b => b ? "true" : "false",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };
    public static string JsNum(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    /// <summary>Chuẩn mã toàn app: bỏ HẾT khoảng trắng, chữ hoa, bỏ "TPC-".</summary>
    public static string NormMa(object? v)
    {
        var s = Regex.Replace(Str(v).Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"\s+", "");
        return s.StartsWith("TPC-", StringComparison.Ordinal) ? s[4..] : s;
    }
    /// <summary>Mã người dùng GÕ để ghi vào file: gộp khoảng trắng, chữ hoa (không bỏ TPC-).</summary>
    public static string CleanMa(string? v) => Regex.Replace(v ?? "", @"\s+", " ").Trim().ToUpperInvariant();
    public static string NormLoai(object? v) => Strip(Regex.Replace(Regex.Replace(Str(v), "_x000d_", " ", RegexOptions.IgnoreCase), @"[\r\n]+", " "));
    public static string SlotOfLoai(object? l) => NormLoai(l) switch { "xuat thay bao bi" => "thay", "xuat xa" => "radong", _ => "" };
    private static bool IsBlank(object? v) => v is null || (v is string s && s.Trim().Length == 0);
    public static double R6(double x) => Math.Round(x * 1e6, MidpointRounding.AwayFromZero) / 1e6;

    // ------------------------------------------------------------------ ngày / số (quy chuẩn chung kl-num.js)

    public static string? Ymd(int y, int m, int d)
    {
        if (m < 1 || m > 12 || d < 1 || d > DateTime.DaysInMonth(Math.Clamp(y, 1, 9999), m) || y < 1 || y > 9999) return null;
        return $"{y:D4}-{m:D2}-{d:D2}";
    }
    public static string? ParseDate(object? v)
    {
        switch (v)
        {
            case null: return null;
            case DateTime dt: return Ymd(dt.Year, dt.Month, dt.Day);
            case double n:
                if (double.IsNaN(n) || double.IsInfinity(n) || n < 20000 || n > 80000) return null;
                var t = DateTime.FromOADate(Math.Round(n * 86400) / 86400);
                return Ymd(t.Year, t.Month, t.Day);
            case bool: return null;
        }
        var s = Str(v).Trim();
        if (s.Length == 0) return null;
        var m = Regex.Match(s, @"^(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{4}|\d{2})(?:[ T]+\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AaPp][Mm])?)?$");
        if (m.Success) { var y = int.Parse(m.Groups[3].Value); if (m.Groups[3].Value.Length == 2) y += 2000; return Ymd(y, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)); }
        m = Regex.Match(s, @"^(\d{4})[\/\-.](\d{1,2})[\/\-.](\d{1,2})(?:[ T].*)?$");
        return m.Success ? Ymd(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
    }
    /// <summary>Số theo quy chuẩn chung: null = trống HOẶC không đọc được (không đoán).</summary>
    public static double? ParseNum(object? v)
    {
        switch (v)
        {
            case null: return null;
            case double d: return double.IsFinite(d) ? d : null;
            case bool: return null;
        }
        var s = Regex.Replace(Str(v), @"[\s   ]+", "");
        if (s is "" or "-" or "—" or "–") return null;
        s = s.Replace('−', '-');
        var neg = false;
        if (s.Length >= 2 && s[0] == '(' && s[^1] == ')') { neg = true; s = s[1..^1]; }
        var sign = "";
        if (s.Length > 0 && (s[0] == '-' || s[0] == '+')) { sign = s[..1]; s = s[1..]; }
        if (!Regex.IsMatch(s, @"^[\d.,]+$") || !Regex.IsMatch(s, @"\d") || Regex.IsMatch(s, @"^[.,]|[.,]$")) return null;
        int nC = s.Count(c => c == ','), nD = s.Count(c => c == '.');
        if (nC > 0 && nD > 0)
        {
            int c = s.LastIndexOf(','), d = s.LastIndexOf('.');
            if (c > d) { if (nC > 1) return null; s = s.Replace(".", "").Replace(',', '.'); }
            else { if (nD > 1) return null; s = s.Replace(",", ""); }
        }
        else if (nC > 0)
        {
            if (Regex.IsMatch(s, @"^\d{1,3}(,\d{3})+$")) s = s.Replace(",", "");
            else if (nC == 1) s = s.Replace(',', '.');
            else return null;
        }
        else if (nD > 1)
        {
            if (Regex.IsMatch(s, @"^\d{1,3}(\.\d{3})+$")) s = s.Replace(".", "");
            else return null;
        }
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)) return null;
        if (sign == "-") n = -n;
        return neg ? -n : n;
    }
    public static string Dmy(string? k) { var m = Regex.Match(k ?? "", @"^(\d{4})-(\d{2})-(\d{2})$"); return m.Success ? $"{m.Groups[3]}/{m.Groups[2]}/{m.Groups[1]}" : k ?? ""; }
    public static double Serial(string ymd) { var m = Regex.Match(ymd ?? "", @"^(\d{4})-(\d{2})-(\d{2})$"); return m.Success ? new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)).ToOADate() : double.NaN; }

    // ------------------------------------------------------------------ cột

    public static readonly (string Key, string[] Names)[] Field =
    {
        ("ngay", new[] { "ngay", "ngay chung tu", "ngay ct" }),
        ("ma", new[] { "ma hang", "mahang", "ma", "ma hoa", "ten hang xuat thay bao bi" }),
        ("ten", new[] { "ten hang", "tenhang" }),
        ("sl", new[] { "so luong", "soluong", "sl", "so luong (ctns)", "so luong ctns", "sl (ctns)", "sl ctns" }),
        ("net", new[] { "net", "net kg", "so luong (kg)", "so luong kg", "sl (kg)", "sl kg" }),
        ("tl", new[] { "trong luong", "trongluong" }),
        ("size", new[] { "loai size", "size" }),
        ("ghichu", new[] { "ghi chu" }),
    };
    public static readonly string[] Required = { "ngay", "ma", "sl", "net" };
    public static readonly Dictionary<string, string> Label = new()
    {
        ["ngay"] = "Ngày", ["ma"] = "Mã hàng", ["ten"] = "Tên hàng", ["sl"] = "Số lượng", ["net"] = "Net (kg)", ["tl"] = "Trọng lượng", ["size"] = "Loại size", ["ghichu"] = "Ghi chú",
    };
    private static string[] Names(string f) => Field.First(x => x.Key == f).Names;
    private static object? At(List<object?[]> aoa, int r, int j) => r < aoa.Count && aoa[r] is { } row && j >= 0 && j < row.Length ? row[j] : null;

    private sealed record Hdr(int R, Dictionary<string, List<int>> Hit, int N);
    private static Hdr? FindHeader(List<object?[]> aoa)
    {
        Hdr? best = null;
        for (var r = 0; r < Math.Min(aoa.Count, 30); r++)
        {
            var row = aoa[r] ?? Array.Empty<object?>();
            var hit = new Dictionary<string, List<int>>();
            for (var j = 0; j < row.Length; j++)
            {
                var t = Strip(row[j]); if (t.Length == 0) continue;
                foreach (var (k, names) in Field) if (names.Contains(t)) { if (!hit.TryGetValue(k, out var l)) hit[k] = l = new(); l.Add(j); }
            }
            var n = Required.Count(hit.ContainsKey);
            if (n >= 3 && (best is null || n > best.N)) best = new Hdr(r, hit, n);
        }
        return best;
    }
    public static List<HeaderCol> HeaderList(List<object?[]> aoa, int r)
    {
        var hr = r < aoa.Count ? aoa[r] ?? Array.Empty<object?>() : Array.Empty<object?>();
        var outL = new List<HeaderCol>();
        var maxC = hr.Length;
        for (var i = r + 1; i < Math.Min(aoa.Count, r + 81); i++) maxC = Math.Max(maxC, (aoa[i] ?? Array.Empty<object?>()).Length);
        for (var j = 0; j < maxC; j++)
        {
            var name = Str(j < hr.Length ? hr[j] : null).Trim();
            if (name.Length > 0) { outL.Add(new HeaderCol(j, name)); continue; }
            int n = 0, num = 0;
            for (var i = r + 1; i < Math.Min(aoa.Count, r + 81); i++) { var v = At(aoa, i, j); if (IsBlank(v)) continue; n++; if (v is double d && d != 0) num++; }
            if (n >= 3 && num >= 1) outL.Add(new HeaderCol(j, "(không có tiêu đề)", true, num * 2 >= n));
            else if (n >= 3) outL.Add(new HeaderCol(j, "(không có tiêu đề)", true, false));
        }
        return outL;
    }
    private static int? FindTotalNet(List<object?[]> aoa, int hr, List<int> cands, int slCol)
    {
        var found = new HashSet<int>();
        foreach (var p in cands)
            foreach (var t in cands)
            {
                if (p == t) continue;
                int n = 0, ok = 0;
                for (var i = hr + 1; i < aoa.Count; i++)
                {
                    if (At(aoa, i, slCol) is not double sl || At(aoa, i, p) is not double pv || At(aoa, i, t) is not double tv || (sl == 0 && tv == 0) || pv == 0) continue;
                    n++; if (Math.Abs(tv - sl * pv) <= 0.01) ok++;
                }
                if (n >= 3 && (double)ok / n >= 0.95) found.Add(t);
            }
        return found.Count == 1 ? found.First() : null;
    }

    public sealed class ParseOptions
    {
        public Dictionary<string, int>? Map;
        public int? HeaderRow;
        public bool DateFromTotals;
    }

    public static ParseResult ParseSheet(List<object?[]> aoa, ParseOptions? opt = null)
    {
        opt ??= new ParseOptions();
        Hdr? hdr = opt.HeaderRow is int hr0 ? new Hdr(hr0, new(), 0) : FindHeader(aoa);
        if (hdr is null)
            return new ParseResult { Need = new NeedMap { Reason = "Không tìm thấy dòng tiêu đề (cần có các cột: Ngày, Mã hàng, Số lượng, Net).", HeaderRow = null, Fields = Required.ToList() } };
        var headers = HeaderList(aoa, hdr.R);
        var hrow = hdr.R < aoa.Count ? aoa[hdr.R] ?? Array.Empty<object?>() : Array.Empty<object?>();
        List<int> NamedCols(string f) { var ns = Names(f); var l = new List<int>(); for (var j = 0; j < hrow.Length; j++) if (ns.Contains(Strip(hrow[j]))) l.Add(j); return l; }
        var map = new Dictionary<string, int>();
        var missing = new List<string>();
        var choices = new Dictionary<string, List<int>>();
        foreach (var (f, _) in Field)
        {
            if (f == "net") continue;
            if (opt.Map is not null && opt.Map.TryGetValue(f, out var given)) { map[f] = given; continue; }
            var c = NamedCols(f);
            if (c.Count == 1) map[f] = c[0];
            else if (c.Count > 1) { if (Required.Contains(f)) { missing.Add(f); choices[f] = c; } }
            else if (Required.Contains(f)) missing.Add(f);
        }
        {
            int? givenNet = opt.Map is not null && opt.Map.TryGetValue("net", out var gn) ? gn : null;
            var named = NamedCols("net");
            var blanks = headers.Where(h => h.Blank && h.Numeric).Select(h => h.J).ToList();
            var cands = givenNet is int g ? new List<int> { g }.Concat(named).Concat(blanks).ToList() : named.Concat(blanks).ToList();
            var uniq = cands.Distinct().ToList();
            var totalOf = map.TryGetValue("sl", out var slc) && uniq.Count > 1 ? FindTotalNet(aoa, hdr.R, uniq, slc) : null;
            missing.Remove("net");
            if (totalOf is int to) map["net"] = to;
            else if (givenNet is int gv) map["net"] = gv;
            else if (uniq.Count == 1) map["net"] = uniq[0];
            else { missing.Add("net"); choices["net"] = uniq; }
        }
        if (missing.Count > 0)
            return new ParseResult { Need = new NeedMap { Reason = "Có cột chưa xác định được (thiếu hoặc trùng tên) — chọn đúng cột:", HeaderRow = hdr.R, Headers = headers, Fields = missing, Choices = choices } };

        var loaiCol = -1; for (var j = 0; j < hrow.Length; j++) if (Strip(hrow[j]) == "loai chung tu") { loaiCol = j; break; }
        var groupLoai = "";
        var fromTot = opt.DateFromTotals;
        var res = new ParseResult { Ok = true, Map = map, HeaderRow = hdr.R, Headers = headers };
        var pending = new List<(bool ct, Row? o, NoCodeRow? nc)>();
        int? Col(string f) => map.TryGetValue(f, out var c) ? c : null;
        int cMa = map["ma"], cNgay = map["ngay"], cSl = map["sl"], cNet = map["net"];
        int? cSize = Col("size"), cGhi = Col("ghichu"), cTen = Col("ten");
        string Cell(object?[] row, int? j) => j is int x && x < row.Length ? Str(row[x]).Trim() : "";
        string LabelOf(object?[] row) => string.Join(" · ", new[] { Cell(row, cSize), Cell(row, cGhi) }.Where(s => s.Length > 0));
        void Flush(string d)
        {
            foreach (var p in pending)
            {
                if (p.ct) { p.o!.Ngay = d; res.Rows.Add(p.o); res.NFromTotal++; }
                else { p.nc!.Ngay = d; res.NoCode.Add(p.nc); }
            }
            pending.Clear();
        }
        string HName(int j) => headers.FirstOrDefault(h => h.J == j)?.Name ?? "";
        for (var r = hdr.R + 1; r < aoa.Count; r++)
        {
            var row = aoa[r] ?? Array.Empty<object?>();
            object? G(int j) => j < row.Length ? row[j] : null;
            object? vMa = G(cMa), vNgay = G(cNgay), vSl = G(cSl), vNet = G(cNet);
            var ma = NormMa(vMa); var maTuSize = false;
            if (cSize is int cs && cs != cMa)
            {
                var c = NormMa(G(cs));
                if (Regex.IsMatch(c, "^[A-Z0-9]{21}$"))
                {
                    if (ma.Length == 0) { ma = c; maTuSize = true; }
                    else if (Regex.IsMatch(ma, "^[A-Z0-9]{21}$") && c != ma)
                        res.Conflicts.Add(new Conflict(r + 1, ParseDate(vNgay) ?? "", ma, c, ParseNum(vSl) ?? 0, ParseNum(vNet) ?? 0));
                }
            }
            if (ma.Length == 0 && loaiCol < 0 && G(0) is string s0 && NormLoai(s0).Length > 0 && !IsBlank(s0) && ParseDate(vNgay) is null)
            { var g = NormLoai(s0); if (g.StartsWith("xuat ", StringComparison.Ordinal)) groupLoai = g; }
            if (ma.Length == 0)
            {
                var d = ParseDate(vNgay); double? sl = ParseNum(vSl), net = ParseNum(vNet);
                if (d is not null && (sl is not null || net is not null)) { res.Totals.Add(new Total(r + 1, d, sl ?? 0, net ?? 0)); if (fromTot) Flush(d); }
                else if (fromTot && IsBlank(vNgay) && ((sl is double a && a != 0) || (net is double b && b != 0)))
                    pending.Add((false, null, new NoCodeRow { Row = r + 1, Label = LabelOf(row), Size = Cell(row, cSize), Ghichu = Cell(row, cGhi), Sl = sl ?? 0, Net = net ?? 0 }));
                else res.Skipped++;
                continue;
            }
            var dateBlank = fromTot && IsBlank(vNgay);
            var ngay = dateBlank ? null : ParseDate(vNgay); double? slv = ParseNum(vSl), netv = ParseNum(vNet);
            var why = new List<string>(); var cells = new List<ErrCell>();
            void Bad(string what, object? val, int col) { var msg = what + " không đọc được" + (IsBlank(val) ? " (trống)" : $" (“{Str(val)}”)"); why.Add(msg); cells.Add(new ErrCell(col, HName(col), IsBlank(val) ? "" : Str(val), msg)); }
            if (!dateBlank && ngay is null) Bad("Ngày", vNgay, cNgay);
            if (slv is null) Bad("Số lượng", vSl, cSl);
            if (netv is null) Bad("Net", vNet, cNet);
            if (why.Count > 0) { res.Errors.Add(new ErrorRow(r + 1, ma, string.Join("; ", why), cells)); continue; }
            if (maTuSize) res.NMaTuSize++;
            var o = new Row
            {
                Row1 = r + 1, Ngay = ngay ?? "", Ma = ma, Ten = Cell(row, cTen), Size = cSize is not null && !maTuSize ? Cell(row, cSize) : "",
                Ghichu = Cell(row, cGhi), Sl = slv!.Value, Net = netv!.Value, Loai = loaiCol >= 0 ? NormLoai(G(loaiCol)) : groupLoai,
            };
            if (dateBlank) pending.Add((true, o, null)); else res.Rows.Add(o);
        }
        foreach (var p in pending)
            res.Errors.Add(new ErrorRow(p.ct ? p.o!.Row1 : p.nc!.Row, p.ct ? p.o!.Ma : "(chưa có mã)", "Không có dòng tổng ngày bên dưới nên không biết ngày — không tính", new()));
        return res;
    }

    // ------------------------------------------------------------------ gộp + đối chiếu

    public sealed class Agg
    {
        public string Ngay = "", Ma = "", Ten = "", Size = "";
        public List<string> Ghichu = new(), Rows = new();
        public double Sl, Net, NetFile; public double? W; public int N;
    }
    public static Dictionary<string, Agg> Aggregate(IEnumerable<Row> rows)
    {
        var m = new Dictionary<string, Agg>();
        foreach (var x in rows)
        {
            var k = x.Ngay + "|" + x.Ma;
            if (!m.TryGetValue(k, out var a)) m[k] = a = new Agg { Ngay = x.Ngay, Ma = x.Ma, Ten = x.Ten, Size = x.Size };
            a.Sl += x.Sl; a.Net += x.Net; a.NetFile += x.NetFile ?? x.Net; if (x.W is > 0) a.W = x.W; a.N++; a.Rows.Add(x.RowText);
            if (a.Ten.Length == 0 && x.Ten.Length > 0) a.Ten = x.Ten;
            if (a.Size.Length == 0 && x.Size.Length > 0) a.Size = x.Size;
            if (x.Ghichu.Length > 0 && !a.Ghichu.Contains(x.Ghichu)) a.Ghichu.Add(x.Ghichu);
        }
        return m;
    }
    private static bool SameNet(double a, double b) => Math.Abs(a - b) <= EpsNet;
    private static List<int>? DiffChars(string a, string b) { if (a.Length != b.Length) return null; var p = new List<int>(); for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) p.Add(i + 1); return p; }
    public static bool IsBtpCode(string ma) => Regex.IsMatch(ma, "^[VP]RHL");
    public static bool IsNlCode(string ma) => Regex.IsMatch(ma, "^[VP]RHO");
    public static bool IsCode21(string s) => Regex.IsMatch(s, "^[A-Z0-9]{21}$");
    public static string NormSize(string? s)
    {
        var t = Regex.Replace((s ?? "").ToUpperInvariant(), @"\s+", " ").Trim();
        t = Regex.Replace(t, @"^(?:(?:BTP|NL|TGS|TGC|AĐ|ECUADOR|SIZE)\s*)+", "");
        t = Regex.Replace(t, @"\s+", "");
        t = Regex.Replace(t, @"^(\d{1,2}L|L|M)XX$", "$1");
        t = Regex.Replace(t, @"^(\d{1,3})\/(\d{1,3})$", m => int.Parse(m.Groups[1].Value) + "/" + int.Parse(m.Groups[2].Value));
        return t == "L" ? "1L" : t;
    }
    private static double? PcOf(double? sl, double? net) => sl is double s && s != 0 && net is double n ? Math.Round(n / s * 100, MidpointRounding.AwayFromZero) / 100 : null;
    private static string SizeOfCode(string ma, string ten, Func<string, string, string>? sizeOf)
    {
        var sz = "";
        try { sz = sizeOf?.Invoke(ma, ten) ?? ""; } catch { sz = ""; }
        if (sz.Length == 0 && ma.Length >= 17)
        {
            var c4 = ma.Substring(13, 4); var m = Regex.Match(c4, @"^(\d{1,2})LXX$");
            if (m.Success) sz = m.Groups[1].Value + "L"; else if (c4 == "1MXX") sz = "M";
        }
        return NormSize(sz);
    }

    public sealed record GoiY(string Ma, List<int>? Khac);
    public sealed record GoiYPart(string Ma, double? Sl);

    public sealed class Item
    {
        public string Key = "", Ngay = "", Ma = "", Ten = "", Size = "", Ghichu = "", Status = "";
        public double? KSl, KNet, XSl, XNet, KNetFile, XNetFile, KW, XW;
        public int KN, XN;
        public List<string> KRows = new(), XRows = new(), KMas = new();
        public bool KFix, XFix, TheoSize;
        public double DSl, DNet;
        public List<GoiY>? GoiY;
        public List<List<GoiYPart>>? GoiYNhom;
    }
    public sealed class Side { public double Sl, Net, NetFile; public int N; }
    public sealed class Check { public double Sl, Net, SumSl, SumNet, DSl, DNet; public int N; public bool Khop; }
    public sealed class NcSum { public int N; public double Sl, Net; }
    public sealed class Day
    {
        public string Ngay = "";
        public Side Kho = new(), Xuong = new();
        public int NKhop, NLech, NChiKho, NChiXuong, NZero;
        public List<Item> Items = new();
        public Check? CheckKho, CheckXuong;
        public NcSum? NcKho, NcXuong;
        public double DSl, DNet;
        public bool CoLech;
        public bool HasWarn => (CheckKho is { Khop: false }) || (CheckXuong is { Khop: false });
        public bool Attn => CoLech || HasWarn || NcKho is not null || NcXuong is not null;
    }
    public sealed class Summary
    {
        public int NNgay, NNgayLech, NMuc, NKhop, NLech, NChiKho, NChiXuong, NZero, NNetFix;
        public double KSl, KNet, XSl, XNet, DSl, DNet;
        public NcSum NcKho = new(), NcXuong = new();
    }
    public sealed class Result
    {
        public List<Day> Days = new();
        public List<Item> Items = new();
        public Summary Summary = new();
        public List<NoCodeRow> NoCodeLeftKho = new(), NoCodeLeftXuong = new();
        public int NNhanh, NGiuMa;
        private Dictionary<string, Item>? _idx;
        public Item? ByKey(string key) { _idx ??= Items.GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First()); return _idx.GetValueOrDefault(key); }
    }
    public sealed class CompareOptions
    {
        public List<Total>? KhoTotals, XuongTotals;
        public List<NoCodeRow>? KhoNoCode, XuongNoCode;
        public Func<string, string, string>? SizeOf;
        public Func<string, double?>? WeightOf;
    }

    public static int StatusRank(string s) => s switch { "lech" => 0, "chi_kho" => 1, "chi_xuong" => 2, "zero" => 3, _ => 4 };
    public static readonly Dictionary<string, string> StatusText = new()
    {
        ["khop"] = "Khớp", ["lech"] = "Lệch", ["chi_kho"] = "Chỉ có ở Kho", ["chi_xuong"] = "Chỉ có ở Xưởng", ["zero"] = "Xưởng/Kho ghi 0",
    };
    private static int Ord(string a, string b) => string.CompareOrdinal(a, b);

    public static Result Compare(List<Row> khoRows, List<Row> xuongRows, CompareOptions? opt = null)
    {
        opt ??= new CompareOptions();
        var xnc = new List<(NoCodeRow t, string raw, string code)>(); var promoted = new List<Row>();
        foreach (var t in opt.XuongNoCode ?? new())
        {
            var raw = t.Size.Trim();
            if (raw.Length == 0 && t.Label.IndexOf(" · ", StringComparison.Ordinal) > 0) raw = t.Label.Split(" · ")[0].Trim();
            var code = NormMa(raw);
            if (IsCode21(code) && !IsBtpCode(code)) promoted.Add(new Row { Row1 = t.Row, Ngay = t.Ngay, Ma = code, Ghichu = t.Ghichu, Sl = t.Sl, Net = t.Net });
            else xnc.Add((t, raw, code));
        }
        var wCache = new Dictionary<string, double?>();
        double? W(string m)
        {
            if (opt.WeightOf is null || m.Length == 0) return null;
            if (!wCache.TryGetValue(m, out var w)) { try { w = opt.WeightOf(m); } catch { w = null; } wCache[m] = w = w is > 0 && double.IsFinite(w.Value) ? w : null; }
            return w;
        }
        List<Row> WithFormula(IEnumerable<Row> rows) => opt.WeightOf is null ? rows.ToList() : rows.Select(x =>
        {
            var w = W(x.Ma);
            if (w is null || !double.IsFinite(x.Sl) || (x.Edited && x.Sl == 0)) return x;
            var c = x.Clone(); c.NetFile = x.Net; c.Net = Math.Round(x.Sl * w.Value * 1e6, MidpointRounding.AwayFromZero) / 1e6; c.W = w; return c;
        }).ToList();
        var K = Aggregate(WithFormula(khoRows)); var X = Aggregate(WithFormula(xuongRows.Concat(promoted)));
        var keys = new List<string>(K.Keys); foreach (var k in X.Keys) if (!K.ContainsKey(k)) keys.Add(k);
        var items = new List<Item>();
        foreach (var key in keys)
        {
            K.TryGetValue(key, out var k); X.TryGetValue(key, out var x); var b = k ?? x!;
            var it = new Item
            {
                Key = key, Ngay = b.Ngay, Ma = b.Ma, Ten = (k?.Ten is { Length: > 0 } kt ? kt : x?.Ten) ?? "", Size = x?.Size ?? "", Ghichu = x is null ? "" : string.Join(" / ", x.Ghichu),
                KSl = k?.Sl, KNet = k?.Net, KN = k?.N ?? 0, XSl = x?.Sl, XNet = x?.Net, XN = x?.N ?? 0,
                KRows = k?.Rows ?? new(), XRows = x?.Rows ?? new(), KNetFile = k?.NetFile, XNetFile = x?.NetFile, KW = k?.W, XW = x?.W,
            };
            it.KFix = k is { W: not null } && !SameNet(k.NetFile, k.Net); it.XFix = x is { W: not null } && !SameNet(x.NetFile, x.Net);
            if (k is not null && x is not null) { it.DSl = x.Sl - k.Sl; it.DNet = x.Net - k.Net; it.Status = it.DSl == 0 && SameNet(x.Net, k.Net) ? "khop" : "lech"; }
            else if (k is not null) { it.Status = k.Sl == 0 && k.Net == 0 ? "zero" : "chi_kho"; it.DSl = -k.Sl; it.DNet = -k.Net; }
            else { it.Status = x!.Sl == 0 && x.Net == 0 ? "zero" : "chi_xuong"; it.DSl = x.Sl; it.DNet = x.Net; }
            items.Add(it);
        }
        // đối chiếu nhanh theo SIZE + TRỌNG LƯỢNG
        var pool = new Dictionary<string, Item>(); var poolOrder = new List<string>(); var keep = new List<Item>(); var leftX = new List<NoCodeRow>();
        var gGhi = new Dictionary<string, List<string>>(); var gKFix = new Dictionary<string, int>();
        string PKey(string ngay, string size, double pc) => "sz|" + ngay + "|" + size + "|" + pc.ToString("F2", CultureInfo.InvariantCulture);
        Item PoolGet(string ngay, string size, double pc)
        {
            var key = PKey(ngay, size, pc);
            if (!pool.TryGetValue(key, out var g))
            {
                pool[key] = g = new Item { Key = key, Ngay = ngay, Size = size, TheoSize = true, Ma = "SIZE " + size + " · " + JsNum(pc) + " kg/thùng" };
                poolOrder.Add(key); gGhi[key] = new(); gKFix[key] = 0;
            }
            return g;
        }
        void AddKho(Item g, Item it)
        {
            g.KSl = (g.KSl ?? 0) + it.KSl; g.KNet = (g.KNet ?? 0) + it.KNet; g.KNetFile = (g.KNetFile ?? 0) + (it.KNetFile ?? it.KNet);
            if (it.KFix) gKFix[g.Key]++; g.KN += it.KN; g.KRows.AddRange(it.KRows); g.KMas.Add(it.Ma + " (" + JsNum(it.KSl ?? 0) + ")");
        }
        var nlWait = new List<(Item it, string sz, double pc)>();
        foreach (var it in items)
        {
            if (it.Status == "chi_kho" && it.KSl is double ks && ks != 0 && (IsBtpCode(it.Ma) || IsNlCode(it.Ma)))
            {
                var sz = SizeOfCode(it.Ma, it.Ten, opt.SizeOf); var pc = PcOf(it.KSl, it.KNet);
                if (sz.Length > 0 && pc is double p)
                {
                    if (IsBtpCode(it.Ma)) { AddKho(PoolGet(it.Ngay, sz, p), it); continue; }
                    nlWait.Add((it, sz, p)); continue;
                }
            }
            keep.Add(it);
        }
        foreach (var (t, raw, code) in xnc)
        {
            var sz = IsCode21(code) ? SizeOfCode(code, "", opt.SizeOf) : NormSize(raw); var pc = PcOf(t.Sl, t.Net);
            if (sz.Length > 0 && pc is double p)
            {
                var g = PoolGet(t.Ngay, sz, p); g.XSl = (g.XSl ?? 0) + t.Sl; g.XNet = (g.XNet ?? 0) + t.Net; g.XN++; g.XRows.Add(t.Row.ToString(CultureInfo.InvariantCulture));
                if (t.Ghichu.Length > 0 && !gGhi[g.Key].Contains(t.Ghichu)) gGhi[g.Key].Add(t.Ghichu);
            }
            else leftX.Add(t);
        }
        foreach (var (it, sz, pc) in nlWait)
        {
            if (pool.TryGetValue(PKey(it.Ngay, sz, pc), out var g) && g.XN > 0) AddKho(g, it); else keep.Add(it);
        }
        foreach (var key in poolOrder)
        {
            var g = pool[key];
            g.Ten = g.KMas.Count > 0 ? "Kho: " + string.Join(" + ", g.KMas) : ""; g.Ghichu = string.Join(" / ", gGhi[key]); g.KFix = gKFix[key] > 0;
            if (g.KSl is not null && g.XSl is not null) { g.DSl = g.XSl.Value - g.KSl.Value; g.DNet = g.XNet!.Value - g.KNet!.Value; g.Status = g.DSl == 0 && SameNet(g.XNet.Value, g.KNet.Value) ? "khop" : "lech"; }
            else if (g.KSl is not null) { g.Status = "chi_kho"; g.DSl = -g.KSl.Value; g.DNet = -g.KNet!.Value; }
            else { g.Status = g.XSl == 0 && g.XNet == 0 ? "zero" : "chi_xuong"; g.DSl = g.XSl!.Value; g.DNet = g.XNet!.Value; }
            keep.Add(g);
        }
        items = keep;
        // gợi ý: cùng ngày, cùng SL & Net, khác mã — CHỈ để xem
        var byDay = new Dictionary<string, (List<Item> k, List<Item> x)>(); var dayOrder = new List<string>();
        foreach (var it in items)
            if (!it.TheoSize && it.Status is "chi_kho" or "chi_xuong")
            {
                if (!byDay.TryGetValue(it.Ngay, out var l)) { byDay[it.Ngay] = l = (new(), new()); dayOrder.Add(it.Ngay); }
                (it.Status == "chi_kho" ? l.k : l.x).Add(it);
            }
        foreach (var d in dayOrder)
        {
            var l = byDay[d];
            foreach (var a in l.k) { var c = l.x.Where(b => b.XSl == a.KSl && SameNet(b.XNet ?? 0, a.KNet ?? 0)).ToList(); if (c.Count > 0) a.GoiY = c.Select(b => new GoiY(b.Ma, DiffChars(a.Ma, b.Ma))).ToList(); }
            foreach (var b in l.x) { var c = l.k.Where(a => a.KSl == b.XSl && SameNet(a.KNet ?? 0, b.XNet ?? 0)).ToList(); if (c.Count > 0) b.GoiY = c.Select(a => new GoiY(a.Ma, DiffChars(b.Ma, a.Ma))).ToList(); }
        }
        foreach (var d in dayOrder)
        {
            var l = byDay[d];
            void Grp(List<Item> single, List<Item> many, Func<Item, bool, double> sl, Func<Item, bool, double> net, bool sideK)
            {
                foreach (var a in single)
                {
                    if (a.GoiY is not null) continue;
                    var pl = many.Where(b => b.GoiY is null).ToList(); var n = pl.Count;
                    if (n < 2 || n > 14) continue;
                    var hit = new List<List<Item>>();
                    for (var i = 0; i < n && hit.Count < 3; i++)
                        for (var j = i + 1; j < n && hit.Count < 3; j++)
                        {
                            var s2 = sl(pl[i], false) + sl(pl[j], false); var n2 = net(pl[i], false) + net(pl[j], false);
                            if (s2 == sl(a, true) && SameNet(n2, net(a, true))) hit.Add(new() { pl[i], pl[j] });
                            for (var q = j + 1; q < n && hit.Count < 3; q++)
                                if (sl(pl[i], false) + sl(pl[j], false) + sl(pl[q], false) == sl(a, true) && SameNet(net(pl[i], false) + net(pl[j], false) + net(pl[q], false), net(a, true)))
                                    hit.Add(new() { pl[i], pl[j], pl[q] });
                        }
                    if (hit.Count > 0) a.GoiYNhom = hit.Select(g => g.Select(b => new GoiYPart(b.Ma, sideK ? b.KSl : b.XSl)).ToList()).ToList();
                }
            }
            Grp(l.x, l.k, (b, own) => (own ? b.XSl : b.KSl) ?? 0, (b, own) => (own ? b.XNet : b.KNet) ?? 0, true);
            Grp(l.k, l.x, (b, own) => (own ? b.KSl : b.XSl) ?? 0, (b, own) => (own ? b.KNet : b.XNet) ?? 0, false);
        }
        // theo ngày
        var days = new Dictionary<string, Day>();
        Day DayOf(string d) { if (!days.TryGetValue(d, out var o)) days[d] = o = new Day { Ngay = d }; return o; }
        foreach (var it in items)
        {
            var o = DayOf(it.Ngay); o.Items.Add(it);
            if (it.KSl is not null) { o.Kho.Sl += it.KSl.Value; o.Kho.Net += it.KNet ?? 0; o.Kho.NetFile += it.KNetFile ?? it.KNet ?? 0; o.Kho.N++; }
            if (it.XSl is not null) { o.Xuong.Sl += it.XSl.Value; o.Xuong.Net += it.XNet ?? 0; o.Xuong.NetFile += it.XNetFile ?? it.XNet ?? 0; o.Xuong.N++; }
            switch (it.Status) { case "khop": o.NKhop++; break; case "lech": o.NLech++; break; case "chi_kho": o.NChiKho++; break; case "chi_xuong": o.NChiXuong++; break; default: o.NZero++; break; }
        }
        foreach (var (side, tots) in new[] { (true, opt.KhoTotals), (false, opt.XuongTotals) })
            foreach (var t in tots ?? new())
            {
                var o = DayOf(t.Ngay); var c = side ? (o.CheckKho ??= new Check()) : (o.CheckXuong ??= new Check());
                c.Sl += t.Sl; c.Net += t.Net; c.N++;
            }
        foreach (var (side, lst) in new[] { (true, opt.KhoNoCode), (false, leftX) })
            foreach (var x in lst ?? new())
            {
                var o = DayOf(x.Ngay); var c = side ? (o.NcKho ??= new NcSum()) : (o.NcXuong ??= new NcSum());
                c.N++; c.Sl += x.Sl; c.Net += x.Net;
            }
        var list = days.Values.OrderBy(o => o.Ngay, StringComparer.Ordinal).ToList();
        foreach (var o in list)
        {
            o.DSl = o.Xuong.Sl - o.Kho.Sl; o.DNet = o.Xuong.Net - o.Kho.Net;
            o.Items = o.Items.OrderBy(a => StatusRank(a.Status)).ThenBy(a => a.Ma, StringComparer.Ordinal).ToList();
            o.CoLech = o.NLech + o.NChiKho + o.NChiXuong > 0;
            foreach (var side in new[] { true, false })
            {
                var c = side ? o.CheckKho : o.CheckXuong; if (c is null) continue;
                var nc = side ? o.NcKho : o.NcXuong; var sd = side ? o.Kho : o.Xuong;
                double sSl = sd.Sl + (nc?.Sl ?? 0), sNet = sd.NetFile + (nc?.Net ?? 0);
                c.SumSl = sSl; c.SumNet = sNet; c.Khop = Math.Abs(c.Sl - sSl) < 1e-9 && SameNet(c.Net, sNet); c.DSl = c.Sl - sSl; c.DNet = c.Net - sNet;
            }
        }
        items = items.OrderBy(a => a.Ngay, StringComparer.Ordinal).ThenBy(a => StatusRank(a.Status)).ThenBy(a => a.Ma, StringComparer.Ordinal).ToList();
        var S = new Summary { NNgay = list.Count, NNgayLech = list.Count(o => o.CoLech), NMuc = items.Count };
        foreach (var it in items)
        {
            switch (it.Status) { case "khop": S.NKhop++; break; case "lech": S.NLech++; break; case "chi_kho": S.NChiKho++; break; case "chi_xuong": S.NChiXuong++; break; default: S.NZero++; break; }
            if (it.KSl is not null) { S.KSl += it.KSl.Value; S.KNet += it.KNet ?? 0; }
            if (it.XSl is not null) { S.XSl += it.XSl.Value; S.XNet += it.XNet ?? 0; }
        }
        S.DSl = S.XSl - S.KSl; S.DNet = S.XNet - S.KNet; S.NNetFix = items.Count(i => i.KFix || i.XFix);
        foreach (var o in list)
        {
            if (o.NcKho is { } a) { S.NcKho.N += a.N; S.NcKho.Sl += a.Sl; S.NcKho.Net += a.Net; }
            if (o.NcXuong is { } b) { S.NcXuong.N += b.N; S.NcXuong.Sl += b.Sl; S.NcXuong.Net += b.Net; }
        }
        return new Result { Days = list, Items = items, Summary = S, NoCodeLeftKho = opt.KhoNoCode ?? new(), NoCodeLeftXuong = leftX, NNhanh = xnc.Count - leftX.Count, NGiuMa = promoted.Count };
    }

    /// <summary>Ghi chú khi Net trong file KHÁC SL × trọng lượng theo mã và app đã tính lại theo công thức.</summary>
    public static List<string> NetFixNotes(Item it, Func<double, string> f, Func<double, string> g)
    {
        var outL = new List<string>();
        foreach (var (nm, fix, nf, nc, sl, w) in new[] { ("Kho", it.KFix, it.KNetFile, it.KNet, it.KSl, it.KW), ("Xưởng", it.XFix, it.XNetFile, it.XNet, it.XSl, it.XW) })
        {
            if (!fix) continue;
            outL.Add($"Net {nm} trong file ghi {g(nf ?? 0)} kg không khớp công thức → app tính lại {g(nc ?? 0)} kg" +
                (w is not null && !it.TheoSize ? $" (= {f(sl ?? 0)} × {g(w.Value)} kg/thùng theo mã)" : " (= SL × trọng lượng theo mã)") + " — nên sửa Net trong file cho đúng");
        }
        return outL;
    }

    public sealed class LoaiOther { public string Loai = ""; public int N; public double Sl, Net; }
    public sealed class Split { public List<Row> Thay = new(), RaDong = new(); public List<LoaiOther> Khac = new(); public int KhongLoai; }
    public static Split SplitByLoai(IEnumerable<Row> rows)
    {
        var o = new Split(); var other = new Dictionary<string, LoaiOther>(); var order = new List<string>();
        foreach (var r in rows)
        {
            if (r.Loai.Length == 0) { o.KhongLoai++; continue; }
            var s = SlotOfLoai(r.Loai);
            if (s == "thay") o.Thay.Add(r);
            else if (s == "radong") o.RaDong.Add(r);
            else { if (!other.TryGetValue(r.Loai, out var x)) { other[r.Loai] = x = new LoaiOther { Loai = r.Loai }; order.Add(r.Loai); } x.N++; x.Sl += r.Sl; x.Net += r.Net; }
        }
        o.Khac = order.Select(k => other[k]).ToList();
        return o;
    }

    // ------------------------------------------------------------------ nạp MỘT file gốc vào HAI file đã liên kết

    public sealed class Op
    {
        public string Type = "";        // move | setsl | delete | add | retag | rename
        public int Slot, From, To;      // 1 = Xuất thay thùng, 2 = Xuất rã đông
        public string Ngay = "", Ma = "", Ten = "";
        public double? Sl, Tl, Target;
        public string? NewNgay, NewMa;
        public List<RenameItem>? Items;
        public Op Clone() { var c = (Op)MemberwiseClone(); c.Items = Items?.Select(i => i with { }).ToList(); return c; }
    }
    public sealed record RenameItem(string Ngay, string Ma, string Ten);
    public sealed class Cur { public double Sl, Net; public string Ten = ""; }
    public sealed class SideSum { public int N; public double Sl, Net; }
    public sealed record GocDiff(string Ma, string Ten, double NewSl, double CurSl, double Delta);
    public sealed class GocDay
    {
        public string Ngay = "", State = "";   // moi | giong | khac | sai
        public SideSum Thay = new(), RaDong = new();
        public List<GocDiff> Diffs = new();
        public List<Op> AddOps = new(), ReplaceOps = new();
    }
    public sealed class GocPlan { public List<GocDay> Days = new(); public int NMoi, NGiong, NKhac, NSai; }

    public static GocPlan PlanGoc(List<Row> thay, List<Row> radong, Dictionary<string, Cur> cur1, Dictionary<string, Cur> cur2)
    {
        const double eps = 1e-6;
        var byDay = new SortedDictionary<string, Dictionary<string, (Cur thay, Cur radong)>>(StringComparer.Ordinal);
        var maOrder = new Dictionary<string, List<string>>();
        foreach (var (k, rows) in new[] { ("thay", thay), ("radong", radong) })
            foreach (var r in rows)
            {
                if (r.Ngay.Length == 0 || r.Ma.Length == 0) continue;
                if (!byDay.TryGetValue(r.Ngay, out var d)) { byDay[r.Ngay] = d = new(); maOrder[r.Ngay] = new(); }
                if (!d.TryGetValue(r.Ma, out var m)) { d[r.Ma] = m = (new Cur(), new Cur()); maOrder[r.Ngay].Add(r.Ma); }
                var x = k == "thay" ? m.thay : m.radong; x.Sl = R6(x.Sl + r.Sl); x.Net = R6(x.Net + r.Net); if (x.Ten.Length == 0 && r.Ten.Length > 0) x.Ten = r.Ten;
            }
        var curDays = new Dictionary<string, Dictionary<string, Cur[]>>(); var curOrder = new Dictionary<string, List<string>>();
        foreach (var (id, cur) in new[] { (1, cur1), (2, cur2) })
            foreach (var (key, v) in cur)
            {
                var i = key.IndexOf('|'); var ngay = key[..i]; var ma = key[(i + 1)..]; if (!(v.Sl > eps)) continue;
                if (!curDays.TryGetValue(ngay, out var d)) { curDays[ngay] = d = new(); curOrder[ngay] = new(); }
                if (!d.TryGetValue(ma, out var m)) { d[ma] = m = new[] { new Cur(), new Cur(), new Cur() }; curOrder[ngay].Add(ma); }
                m[id] = new Cur { Sl = R6(m[id].Sl + v.Sl), Net = R6(m[id].Net + v.Net), Ten = m[id].Ten.Length > 0 ? m[id].Ten : v.Ten };
            }
        double? TlOf(Cur x) => x.Sl > eps && x.Net > eps ? R6(x.Net / x.Sl) : null;
        var plan = new GocPlan();
        foreach (var (ngay, nd) in byDay)
        {
            var di = new GocDay { Ngay = ngay };
            foreach (var ma in maOrder[ngay])
            {
                var m = nd[ma];
                if (m.thay.Sl > eps) { di.Thay.N++; di.Thay.Sl = R6(di.Thay.Sl + m.thay.Sl); di.Thay.Net = R6(di.Thay.Net + m.thay.Net); }
                if (m.radong.Sl > eps) { di.RaDong.N++; di.RaDong.Sl = R6(di.RaDong.Sl + m.radong.Sl); di.RaDong.Net = R6(di.RaDong.Net + m.radong.Net); }
            }
            curDays.TryGetValue(ngay, out var cd);
            if (cd is null || cd.Count == 0)
            {
                di.State = "moi";
                foreach (var ma in maOrder[ngay])
                {
                    var m = nd[ma];
                    if (m.thay.Sl > eps) di.AddOps.Add(new Op { Type = "add", Slot = 1, Ngay = ngay, Ma = ma, Ten = m.thay.Ten, Sl = m.thay.Sl, Tl = TlOf(m.thay) });
                    if (m.radong.Sl > eps) di.AddOps.Add(new Op { Type = "add", Slot = 2, Ngay = ngay, Ma = ma, Ten = m.radong.Ten, Sl = m.radong.Sl, Tl = TlOf(m.radong) });
                }
                plan.Days.Add(di); continue;
            }
            var mas = new List<string>(maOrder[ngay]); foreach (var ma in curOrder[ngay]) if (!nd.ContainsKey(ma)) mas.Add(ma);
            foreach (var ma in mas)
            {
                var hasN = nd.TryGetValue(ma, out var n); cd.TryGetValue(ma, out var c);
                double newT = hasN ? R6(n.thay.Sl + n.radong.Sl) : 0, curT = c is not null ? R6(c[1].Sl + c[2].Sl) : 0;
                if (Math.Abs(newT - curT) <= eps) continue;
                var ten = hasN && (n.thay.Ten.Length > 0 || n.radong.Ten.Length > 0) ? (n.thay.Ten.Length > 0 ? n.thay.Ten : n.radong.Ten)
                    : c is not null ? (c[1].Ten.Length > 0 ? c[1].Ten : c[2].Ten) : "";
                var delta = R6(newT - curT);
                di.Diffs.Add(new GocDiff(ma, ten, newT, curT, delta));
                var pref = hasN ? (n.thay.Sl >= n.radong.Sl ? 1 : 2) : (c is not null && c[1].Sl >= c[2].Sl ? 1 : 2);
                var other = pref == 1 ? 2 : 1;
                if (delta > eps) di.ReplaceOps.Add(new Op { Type = "add", Slot = pref, Ngay = ngay, Ma = ma, Ten = ten, Sl = delta, Tl = hasN ? TlOf(pref == 1 ? n.thay : n.radong) : null });
                else
                {
                    var need = -delta; var have = new Dictionary<int, double> { [pref] = c?[pref].Sl ?? 0, [other] = c?[other].Sl ?? 0 };
                    foreach (var s in new[] { pref, other })
                        if (need > eps && have[s] > eps) { var take = Math.Min(need, have[s]); di.ReplaceOps.Add(new Op { Type = "setsl", Slot = s, Ngay = ngay, Ma = ma, Target = R6(have[s] - take) }); need = R6(need - take); }
                }
            }
            di.State = di.Diffs.Count > 0 ? "khac" : "giong";
            plan.Days.Add(di);
        }
        plan.NMoi = plan.Days.Count(d => d.State == "moi"); plan.NGiong = plan.Days.Count(d => d.State == "giong"); plan.NKhac = plan.Days.Count(d => d.State == "khac");
        return plan;
    }

    public sealed class Month { public string Value = ""; public SortedDictionary<string, int> Counts = new(StringComparer.Ordinal); public int N; }
    /// <summary>Tháng có NHIỀU NGÀY nhất (hòa → tháng muộn hơn).</summary>
    public static Month MonthOf(IEnumerable<string> ngays)
    {
        var seen = new HashSet<string>(); var m = new Month();
        foreach (var d in ngays)
        {
            if (!Regex.IsMatch(d ?? "", @"^\d{4}-\d{2}-\d{2}$") || !seen.Add(d!)) continue;
            var k = d![..7]; m.Counts[k] = m.Counts.GetValueOrDefault(k) + 1;
        }
        var bn = 0; foreach (var (k, n) in m.Counts) if (n >= bn) { m.Value = k; bn = n; }
        m.N = seen.Count; return m;
    }
    public static string MonthLabel(string m) => Regex.IsMatch(m ?? "", @"^\d{4}-\d{2}$") ? "tháng " + m![5..] + "/" + m[..4] : "";
    /// <summary>Ngày của file gốc KHÁC tháng với file liên kết → 'sai' (không nạp). Trả (ngày sai, mọi ngày đều sai?).</summary>
    public static (List<string> Wrong, bool All) MonthGuard(GocPlan plan, string linkedMonth)
    {
        var wrong = new List<string>();
        if (!string.IsNullOrEmpty(linkedMonth))
            foreach (var d in plan.Days)
                if (d.Ngay[..7] != linkedMonth) { wrong.Add(d.Ngay); d.State = "sai"; d.AddOps.Clear(); d.ReplaceOps.Clear(); d.Diffs.Clear(); }
        plan.NMoi = plan.Days.Count(d => d.State == "moi"); plan.NGiong = plan.Days.Count(d => d.State == "giong"); plan.NKhac = plan.Days.Count(d => d.State == "khac"); plan.NSai = wrong.Count;
        return (wrong, wrong.Count > 0 && wrong.Count == plan.Days.Count);
    }

    public static (int Chung, int Kho, int Xuong) Overlap(IEnumerable<Row> kho, IEnumerable<Row> xuong)
    {
        var a = kho.Select(r => r.Ma).ToHashSet(); var b = xuong.Select(r => r.Ma).ToHashSet();
        return (a.Count(b.Contains), a.Count, b.Count);
    }

    /// <summary>Mã gần giống trong danh sách (cùng độ dài, khác ≤ maxDiff ký tự) — chỉ để gợi ý.</summary>
    public static List<(string Ma, int D)> NearCodes(string ma, IEnumerable<string> codes, int maxDiff = 2, int maxOut = 3)
    {
        var o = new List<(string, int)>(); if (ma.Length == 0) return o;
        foreach (var c in codes)
        {
            if (c.Length != ma.Length || c == ma) continue;
            var d = 0; for (var i = 0; i < c.Length && d <= maxDiff; i++) if (c[i] != ma[i]) d++;
            if (d <= maxDiff) o.Add((c, d));
        }
        return o.OrderBy(x => x.Item2).ThenBy(x => x.Item1, StringComparer.Ordinal).Take(maxOut).ToList();
    }
}

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.PhieuKho;

/// <summary>Giá trị ghi đè cả ô công thức (VD cột NET người dùng gõ tay).</summary>
public sealed record Forced(object? V);

public sealed record WrapSpec(string Ref, int Chars, double Line, string[] TopRefs);
public sealed record FitSpec(string[] Cols, Dictionary<string, int> Dec, Dictionary<string, string> Span, int From, int To);

public sealed class FillOptions
{
    public string Sheet = "";
    public List<int> Hide = new(), Show = new(), ShowCols = new(), HideCols = new();
    public string? Unit;
    public List<(int From, int To)> CopyStyle = new();
    public List<WrapSpec> Wrap = new();
    public FitSpec? Fit;
}

/// <summary>
/// ĐIỀN MẪU EXCEL GIỮ NGUYÊN ĐỊNH DẠNG (port từ pnx-xlsx-fill.js của app cũ): lấy file mẫu (.xlsm) làm khuôn, CHỈ thay giá trị ở các ô nhập liệu — sửa thẳng XML nên
/// giữ nguyên kiểu ô, ô gộp, hình, vùng in, công thức, macro. Công thức tự tính lại khi mở (fullCalcOnLoad + bỏ giá trị tạm của mọi ô công thức).
/// Ẩn / hiện dòng và cột, ghi đè ô công thức khi chủ ý, đổi đơn vị trong công thức email, sao kiểu dáng dòng, tự xuống dòng ô gộp, tự nới độ rộng cột.
/// </summary>
public static class XlsxTemplate
{
    public const string MacroMime = "application/vnd.ms-excel.sheet.macroEnabled.12";

    private static string EscXml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    private static string Read(ZipArchive z, string name)
    {
        var e = z.GetEntry(name) ?? throw new InvalidOperationException("Mẫu hỏng: thiếu " + name);
        using var r = new StreamReader(e.Open(), new UTF8Encoding(false));
        return r.ReadToEnd();
    }
    private static void Write(ZipArchive z, string name, string content)
    {
        z.GetEntry(name)?.Delete();
        var e = z.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    public static string SheetPath(ZipArchive z, string name)
    {
        var wb = Read(z, "xl/workbook.xml"); var rels = Read(z, "xl/_rels/workbook.xml.rels");
        var m = Regex.Match(wb, "<sheet [^>]*name=\"" + Regex.Escape(EscXml(name)) + "\"[^>]*r:id=\"([^\"]+)\"");
        if (!m.Success) throw new InvalidOperationException($"Mẫu không có sheet \"{name}\"");
        var id = m.Groups[1].Value;
        var t = Regex.Match(rels, "<Relationship [^>]*Id=\"" + id + "\"[^>]*Target=\"([^\"]+)\"");
        if (!t.Success) t = Regex.Match(rels, "<Relationship [^>]*Target=\"([^\"]+)\"[^>]*Id=\"" + id + "\"");
        if (!t.Success) throw new InvalidOperationException($"Mẫu hỏng: không thấy file của sheet \"{name}\"");
        return "xl/" + Regex.Replace(t.Groups[1].Value, @"^/?xl/", "");
    }

    public static string SetCell(string xml, string cref, object? val)
    {
        var force = false;
        if (val is Forced f) { force = true; val = f.V; }
        var re = new Regex("<c r=\"" + cref + "\"((?:\\s+[a-zA-Z:]+=\"[^\"]*\")*)\\s*(?:/>|>([\\s\\S]*?)</c>)");
        var m = re.Match(xml);
        if (!m.Success) throw new InvalidOperationException($"Mẫu không có ô {cref} (mẫu đã đổi?)");
        if (Regex.IsMatch(m.Groups[2].Value, @"<f[\s>/]") && !force) throw new InvalidOperationException($"Ô {cref} là ô CÔNG THỨC trong mẫu — không ghi đè");
        var s = Regex.Match(m.Groups[1].Value, "\\ss=\"(\\d+)\"");
        var sa = s.Success ? " s=\"" + s.Groups[1].Value + "\"" : "";
        string cell = val switch
        {
            null => $"<c r=\"{cref}\"{sa}/>",
            string str when str.Length == 0 => $"<c r=\"{cref}\"{sa}/>",
            double d when double.IsFinite(d) => $"<c r=\"{cref}\"{sa}><v>{Num(d)}</v></c>",
            int i => $"<c r=\"{cref}\"{sa}><v>{i}</v></c>",
            long l => $"<c r=\"{cref}\"{sa}><v>{l}</v></c>",
            _ => $"<c r=\"{cref}\"{sa} t=\"inlineStr\"><is><t xml:space=\"preserve\">{EscXml(Convert.ToString(val, CultureInfo.InvariantCulture) ?? "")}</t></is></c>",
        };
        return xml[..m.Index] + cell + xml[(m.Index + m.Length)..];
    }

    public static string SetRowHidden(string xml, int row, bool hidden)
    {
        var m = new Regex("<row\\b([^>]*?\\sr=\"" + row + "\"[^>]*?)(/?)>").Match(xml);
        if (!m.Success) throw new InvalidOperationException("Mẫu không có dòng " + row);
        var a = m.Groups[1].Value.Replace(" hidden=\"1\"", "");
        if (hidden) a = Regex.Replace(a, "(\\sr=\"" + row + "\")", "$1 hidden=\"1\"");
        return xml[..m.Index] + "<row" + a + m.Groups[2].Value + ">" + xml[(m.Index + m.Length)..];
    }

    public static string SetColHidden(string xml, int col, bool hidden)
    {
        var m = new Regex("<col\\b([^>]*?\\smin=\"" + col + "\"[^>]*?\\smax=\"" + col + "\"[^>]*?)(/?)>").Match(xml);
        if (!m.Success) throw new InvalidOperationException("Mẫu không khai báo riêng cột " + col);
        var a = m.Groups[1].Value.Replace(" hidden=\"1\"", "");
        if (hidden) a += " hidden=\"1\"";
        return xml[..m.Index] + "<col" + a + m.Groups[2].Value + ">" + xml[(m.Index + m.Length)..];
    }

    public static string CopyRowStyle(string xml, int from, int to)
    {
        static Regex RowRe(int r) => new("<row\\b[^>]*?\\sr=\"" + r + "\"[^>]*?(?:/>|>[\\s\\S]*?</row>)");
        var f = RowRe(from).Match(xml); var t = RowRe(to).Match(xml);
        if (!f.Success || !t.Success) return xml;
        var styleOf = new Dictionary<string, string>();
        foreach (Match c in Regex.Matches(f.Value, "<c\\b[^>]*?\\sr=\"([A-Z]+)\\d+\"[^>]*>"))
        {
            var s = Regex.Match(c.Value, "\\ss=\"(\\d+)\"");
            if (s.Success) styleOf[c.Groups[1].Value] = s.Groups[1].Value;
        }
        var nt = Regex.Replace(t.Value, "<c\\b([^>]*?)(/?)>", mm =>
        {
            var attrs = mm.Groups[1].Value;
            var col = Regex.Match(attrs, "\\sr=\"([A-Z]+)\\d+\"");
            if (!col.Success || !styleOf.TryGetValue(col.Groups[1].Value, out var st)) return mm.Value;
            var a = Regex.IsMatch(attrs, "\\ss=\"\\d+\"") ? Regex.Replace(attrs, "\\ss=\"\\d+\"", " s=\"" + st + "\"") : attrs + " s=\"" + st + "\"";
            return "<c" + a + mm.Groups[2].Value + ">";
        });
        return xml[..t.Index] + nt + xml[(t.Index + t.Length)..];
    }

    private static string DropCachedFormulaValues(string xml) =>
        Regex.Replace(xml, @"<c\b[^>]*?(?<!/)>[\s\S]*?</c>", cell =>
            Regex.IsMatch(cell.Value, @"<f[\s>/]") ? Regex.Replace(cell.Value, @"(</f>|<f\b[^>]*/>)<v(?:\s[^>]*)?>[\s\S]*?</v>", "$1") : cell.Value);

    private static string SetUnit(string xml, string unit) =>
        Regex.Replace(xml, "<c\\b[^>]*?\\sr=\"N\\d+\"[^>]*>[\\s\\S]*?</c>", cell =>
            Regex.IsMatch(cell.Value, @"<f[\s>]")
                ? Regex.Replace(cell.Value, "(<f\\b[^>]*>)([^<]*)(</f>)", mm => mm.Groups[1].Value + Regex.Replace(mm.Groups[2].Value, @"\b(kiện|bdl)\b", unit) + mm.Groups[3].Value)
                : cell.Value);

    // ---- tự xuống dòng ô gộp chữ dài (Excel không tự co giãn chiều cao dòng có ô gộp)
    private static double WLen(string t) { double n = 0; foreach (var ch in t) n += Regex.IsMatch(ch.ToString(), "[A-ZÀ-Ỹ0-9]") ? 1.25 : 1; return n; }
    public static int WrapLines(string text, int chars)
    {
        int lines = 1; double cur = 0;
        foreach (var w in Regex.Split(text, @"\s+").Where(x => x.Length > 0))
        {
            var l = WLen(w);
            if (cur > 0 && cur + 1 + l > chars) { lines++; cur = l; } else cur = cur > 0 ? cur + 1 + l : l;
            while (cur > chars) { lines++; cur -= chars; }
        }
        return lines;
    }

    private static (string Xml, int Idx) CloneXf(string styles, int idx, bool wrap, string? vertical, Dictionary<string, int> cache)
    {
        var key = idx + "|" + (wrap ? "w" : "") + vertical;
        if (cache.TryGetValue(key, out var hit)) return (styles, hit);
        var m = Regex.Match(styles, @"<cellXfs\b[^>]*>([\s\S]*?)</cellXfs>");
        if (!m.Success) return (styles, idx);
        var xfs = Regex.Matches(m.Groups[1].Value, @"<xf\b[^>]*?/>|<xf\b[^>]*?>[\s\S]*?</xf>").Select(x => x.Value).ToList();
        if (idx >= xfs.Count) return (styles, idx);
        var xf = xfs[idx];
        string Attrs(string a) { a = Regex.Replace(a, "\\s(wrapText|vertical)=\"[^\"]*\"", ""); return a + (vertical is not null ? $" vertical=\"{vertical}\"" : "") + (wrap ? " wrapText=\"1\"" : ""); }
        if (Regex.IsMatch(xf, @"<alignment\b"))
            xf = Regex.Replace(xf, @"<alignment\b([^>]*?)(/?)>", mm => "<alignment" + Attrs(mm.Groups[1].Value) + mm.Groups[2].Value + ">", RegexOptions.None, TimeSpan.FromSeconds(2));
        else
            xf = Regex.Replace(xf, @"^<xf\b([^>]*?)(/?)>", mm => mm.Groups[2].Value.Length > 0 ? "<xf" + mm.Groups[1].Value + "><alignment" + Attrs("") + "/></xf>" : "<xf" + mm.Groups[1].Value + "><alignment" + Attrs("") + "/>");
        if (!xf.Contains("applyAlignment=\"1\"")) xf = Regex.Replace(xf, @"^<xf\b", "<xf applyAlignment=\"1\"");
        var n = xfs.Count; cache[key] = n;
        var outXml = Regex.Replace(styles, @"(<cellXfs\b[^>]*?)count=""\d+""", "$1count=\"" + (n + 1) + "\"").Replace("</cellXfs>", xf + "</cellXfs>");
        return (outXml, n);
    }
    private static int CellStyle(string xml, string cref)
    {
        var m = Regex.Match(xml, "<c r=\"" + cref + "\"([^>]*?)/?>");
        var s = m.Success ? Regex.Match(m.Groups[1].Value, "\\ss=\"(\\d+)\"") : Match.Empty;
        return s.Success ? int.Parse(s.Groups[1].Value) : 0;
    }
    private static string SetCellStyle(string xml, string cref, int sidx) =>
        Regex.Replace(xml, "<c r=\"" + cref + "\"([^>]*?)(/?)>", m =>
            "<c r=\"" + cref + "\"" + (Regex.IsMatch(m.Groups[1].Value, "\\ss=\"\\d+\"") ? Regex.Replace(m.Groups[1].Value, "\\ss=\"\\d+\"", " s=\"" + sidx + "\"") : m.Groups[1].Value + " s=\"" + sidx + "\"") + m.Groups[2].Value + ">");
    private static string SetRowHeight(string xml, int row, double ht) =>
        Regex.Replace(xml, "<row\\b([^>]*?\\sr=\"" + row + "\"[^>]*?)(/?)>", m =>
            "<row" + Regex.Replace(m.Groups[1].Value, "\\s(ht|customHeight)=\"[^\"]*\"", "") + " ht=\"" + Num(ht) + "\" customHeight=\"1\"" + m.Groups[2].Value + ">");

    // ---- tự căn độ rộng cột theo dữ liệu: chỉ NỚI, không bao giờ thu hẹp mẫu
    private static int ColNum(string L) => L.ToUpperInvariant().Aggregate(0, (a, ch) => a * 26 + ch - 64);
    private static double? ColWidth(string xml, int col)
    {
        var m = Regex.Match(xml, "<col\\b[^>]*?\\smin=\"" + col + "\"[^>]*?\\smax=\"" + col + "\"[^>]*?>");
        var w = m.Success ? Regex.Match(m.Value, "\\swidth=\"([\\d.]+)\"") : Match.Empty;
        return w.Success ? double.Parse(w.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }
    private static string SetColWidth(string xml, int col, double width)
    {
        var m = new Regex("<col\\b([^>]*?\\smin=\"" + col + "\"[^>]*?\\smax=\"" + col + "\"[^>]*?)(/?)>").Match(xml);
        if (!m.Success) return xml;
        var a = Regex.IsMatch(m.Groups[1].Value, "\\swidth=\"[\\d.]+\"") ? Regex.Replace(m.Groups[1].Value, "\\swidth=\"[\\d.]+\"", " width=\"" + Num(width) + "\"") : m.Groups[1].Value + " width=\"" + Num(width) + "\"";
        if (!a.Contains("customWidth=")) a += " customWidth=\"1\"";
        return xml[..m.Index] + "<col" + a + m.Groups[2].Value + ">" + xml[(m.Index + m.Length)..];
    }
    internal static Dictionary<string, double> FitWidths(Dictionary<string, object?> cells, FitSpec fit, IEnumerable<int> hiddenRows)
    {
        var need = new Dictionary<string, double>(); var hid = hiddenRows.ToHashSet(); var want = fit.Cols.Select(c => c.ToUpperInvariant()).ToHashSet();
        foreach (var (cref, raw) in cells)
        {
            var m = Regex.Match(cref, @"^([A-Z]+)(\d+)$");
            if (!m.Success || !want.Contains(m.Groups[1].Value)) continue;
            var row = int.Parse(m.Groups[2].Value);
            if (hid.Contains(row) || (fit.From > 0 && row < fit.From) || (fit.To > 0 && row > fit.To)) continue;
            var v = raw is Forced f ? f.V : raw;
            if (v is null || (v is string s0 && (s0.Length == 0 || s0 == " "))) continue;
            double w;
            if (v is double d && double.IsFinite(d))
            {
                var dec = fit.Dec.GetValueOrDefault(m.Groups[1].Value);
                w = d.ToString("#,0." + new string('0', dec) + new string('#', Math.Max(dec, 2) - dec), CultureInfo.GetCultureInfo("en-US")).TrimEnd('.').Length + 2.5;
            }
            else w = Convert.ToString(v, CultureInfo.InvariantCulture)!.Split('\n').Max(x => x.Length) * 1.1 + 2;
            need[m.Groups[1].Value] = Math.Max(need.GetValueOrDefault(m.Groups[1].Value), w);
        }
        return need;
    }
    private static string ApplyFit(string xml, Dictionary<string, object?> cells, FitSpec fit, IEnumerable<int> hiddenRows)
    {
        var need = FitWidths(cells, fit, hiddenRows);
        foreach (var (L, nd) in need)
        {
            var c = ColNum(L); var cur = ColWidth(xml, c);
            if (cur is null) continue;
            var hasSpan = fit.Span.TryGetValue(L, out var spanL);
            var tgt = hasSpan ? ColNum(spanL!) : c;
            var bas = hasSpan ? cur.Value + (ColWidth(xml, tgt) ?? 0) : cur.Value;
            if (nd > bas)
            {
                var want = Math.Min(70, Math.Ceiling((nd - (hasSpan ? cur.Value : 0)) * 10) / 10);
                if (want > (ColWidth(xml, tgt) ?? 0)) xml = SetColWidth(xml, tgt, want);
            }
        }
        return xml;
    }

    /// <summary>Điền mẫu: trả về bytes của file (.xlsm) mới. <paramref name="cells"/> = { ô → giá trị } của sheet <see cref="FillOptions.Sheet"/>.</summary>
    public static byte[] Fill(byte[] template, Dictionary<string, object?> cells, FillOptions o)
    {
        using var ms = new MemoryStream();
        ms.Write(template, 0, template.Length);
        using (var z = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var p = SheetPath(z, o.Sheet);
            var xml = Read(z, p);
            foreach (var (cref, v) in cells) xml = SetCell(xml, cref, v);
            foreach (var (from, to) in o.CopyStyle) xml = CopyRowStyle(xml, from, to);
            foreach (var r in o.Hide) xml = SetRowHidden(xml, r, true);
            foreach (var r in o.Show) xml = SetRowHidden(xml, r, false);
            foreach (var c in o.ShowCols) xml = SetColHidden(xml, c, false);
            foreach (var c in o.HideCols) xml = SetColHidden(xml, c, true);
            if (o.Fit is not null) xml = ApplyFit(xml, cells, o.Fit, o.Hide);
            if (o.Unit is not null) xml = SetUnit(xml, o.Unit);
            var cache = new Dictionary<string, int>();
            foreach (var w in o.Wrap)
            {
                var text = cells.TryGetValue(w.Ref, out var cv) ? Convert.ToString(cv is Forced f ? f.V : cv, CultureInfo.InvariantCulture) ?? "" : "";
                var lines = text.Length > 0 ? WrapLines(text, w.Chars) : 1;
                if (lines <= 1) continue;
                var row = int.Parse(Regex.Match(w.Ref, @"\d+").Value);
                var st = Read(z, "xl/styles.xml");
                var (s1, i1) = CloneXf(st, CellStyle(xml, w.Ref), true, "top", cache); st = s1; xml = SetCellStyle(xml, w.Ref, i1);
                foreach (var t in w.TopRefs) { var (s2, i2) = CloneXf(st, CellStyle(xml, t), false, "top", cache); st = s2; xml = SetCellStyle(xml, t, i2); }
                Write(z, "xl/styles.xml", st);
                xml = SetRowHeight(xml, row, Math.Min(409, Math.Round(lines * w.Line * 100) / 100));
            }
            Write(z, p, xml);
            foreach (var name in z.Entries.Select(e => e.FullName).Where(n => Regex.IsMatch(n, @"^xl/worksheets/sheet\d+\.xml$")).ToList())
                Write(z, name, DropCachedFormulaValues(Read(z, name)));
            var wb = Read(z, "xl/workbook.xml");
            if (!Regex.IsMatch(wb, @"<calcPr\b[^>]*fullCalcOnLoad=")) wb = Regex.Replace(wb, @"<calcPr\b", "<calcPr fullCalcOnLoad=\"1\"");
            Write(z, "xl/workbook.xml", wb);
        }
        return ms.ToArray();
    }
}

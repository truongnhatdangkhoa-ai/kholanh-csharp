using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// 1.72 (N4): ĐỌC NHANH file .xlsx / .xlsm — mở gói zip, đọc tuần tự XML (XmlReader) các phần cần: workbook (tên sheet), sharedStrings,
/// styles (mã định dạng số từng kiểu ô), từng sheet. Không dựng cả mô hình workbook như ClosedXML nên nhanh và ít bộ nhớ hơn nhiều.
/// Kết quả GIỐNG <see cref="SheetText.ReadClosedXml"/>: chữ đang hiện của ô theo định dạng số (SheetJS raw:false), vùng = các ô có nội dung.
/// </summary>
public static partial class SheetText
{
    private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <param name="rawNumbers">true: ô số ghi số gốc (dạng "R", không theo định dạng ô) — cho nơi cần số chính xác (Tồn theo vị trí).</param>
    /// <param name="fromA1">true: bảng bắt đầu từ ô A1 (dòng / cột đúng số thứ tự của Excel), không cắt theo vùng có dữ liệu.</param>
    public static Book ReadXlsxFast(string path, bool rawNumbers = false, bool fromA1 = false)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        ZipArchiveEntry? E(string name) => zip.GetEntry(name) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));

        // workbook: tên sheet theo thứ tự + r:id → đường dẫn
        var rels = new Dictionary<string, string>();
        if (E("xl/_rels/workbook.xml.rels") is { } re)
            using (var r = Xml(re))
                while (r.Read())
                    if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship" && r.GetAttribute("Id") is { } id && r.GetAttribute("Target") is { } t)
                        rels[id] = t.StartsWith('/') ? t.TrimStart('/') : "xl/" + t;
        var sheets = new List<(string Name, string Path)>();
        var wbEntry = E("xl/workbook.xml") ?? throw new InvalidDataException("Không có xl/workbook.xml");
        using (var r = Xml(wbEntry))
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet")
                {
                    var name = r.GetAttribute("name") ?? ""; var rid = r.GetAttribute("id", NsRel) ?? "";
                    if (rels.TryGetValue(rid, out var p)) sheets.Add((name, NormPath(p)));
                }

        var shared = ReadShared(E("xl/sharedStrings.xml"));
        var (xfFmt, custom) = ReadStyles(E("xl/styles.xml"));

        var book = new Book();
        foreach (var (name, p) in sheets)
        {
            var entry = E(p);
            book.SheetNames.Add(name);
            book.Sheets[name] = entry is null ? new List<string[]>() : ReadSheetFast(entry, shared, xfFmt, custom, rawNumbers, fromA1);
        }
        return book;
    }

    private static string NormPath(string p)
    {
        var parts = new List<string>();
        foreach (var s in p.Split('/')) { if (s == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); } else if (s.Length > 0 && s != ".") parts.Add(s); }
        return string.Join("/", parts);
    }

    private static XmlReader Xml(ZipArchiveEntry e) =>
        XmlReader.Create(e.Open(), new XmlReaderSettings { IgnoreWhitespace = false, IgnoreComments = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

    private static List<string> ReadShared(ZipArchiveEntry? e)
    {
        var list = new List<string>();
        if (e is null) return list;
        using var r = Xml(e);
        StringBuilder? sb = null; var inPh = 0; var inT = false;
        while (r.Read())
        {
            switch (r.NodeType)
            {
                case XmlNodeType.Element:
                    if (r.LocalName == "si") { sb = new StringBuilder(); if (r.IsEmptyElement) { list.Add(""); sb = null; } }
                    else if (r.LocalName == "rPh") { if (!r.IsEmptyElement) inPh++; }
                    else if (r.LocalName == "t") inT = !r.IsEmptyElement;
                    break;
                case XmlNodeType.Text: case XmlNodeType.SignificantWhitespace: case XmlNodeType.Whitespace: case XmlNodeType.CDATA:
                    if (inT && inPh == 0) sb?.Append(r.Value);
                    break;
                case XmlNodeType.EndElement:
                    if (r.LocalName == "rPh") inPh--;
                    else if (r.LocalName == "t") inT = false;
                    else if (r.LocalName == "si" && sb is not null) { list.Add(sb.ToString()); sb = null; }
                    break;
            }
        }
        return list;
    }

    private static (List<int> XfFmt, Dictionary<int, string> Custom) ReadStyles(ZipArchiveEntry? e)
    {
        var xf = new List<int>(); var custom = new Dictionary<int, string>();
        if (e is null) return (xf, custom);
        using var r = Xml(e);
        var inCellXfs = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                if (r.LocalName == "numFmt" && int.TryParse(r.GetAttribute("numFmtId"), out var id)) custom[id] = r.GetAttribute("formatCode") ?? "";
                else if (r.LocalName == "cellXfs") inCellXfs = !r.IsEmptyElement;
                else if (r.LocalName == "xf" && inCellXfs) xf.Add(int.TryParse(r.GetAttribute("numFmtId"), out var n) ? n : 0);
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "cellXfs") inCellXfs = false;
        }
        return (xf, custom);
    }

    private static readonly Dictionary<string, string> ErrName = new()
    {
        ["#N/A"] = "NoValueAvailable", ["#DIV/0!"] = "DivisionByZero", ["#VALUE!"] = "IncompatibleValue", ["#REF!"] = "CellReference",
        ["#NAME?"] = "NameNotRecognized", ["#NUM!"] = "NumberInvalid", ["#NULL!"] = "NullValue",
    };

    private static List<string[]> ReadSheetFast(ZipArchiveEntry e, List<string> shared, List<int> xfFmt, Dictionary<int, string> custom, bool rawNumbers = false, bool fromA1 = false)
    {
        var cells = new Dictionary<int, Dictionary<int, string>>();
        int r1 = int.MaxValue, r2 = -1, c1 = int.MaxValue, c2 = -1;
        using var r = Xml(e);
        int curRow = 0, nextCol = 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.LocalName == "row")
            {
                curRow = int.TryParse(r.GetAttribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rn) ? rn : curRow + 1;
                nextCol = 1;
                continue;
            }
            if (r.LocalName != "c") continue;
            var refA = r.GetAttribute("r");
            int row = curRow, col = nextCol;
            if (refA is { Length: > 0 }) ParseRef(refA, out row, out col);
            nextCol = col + 1;
            var t = r.GetAttribute("t") ?? "n";
            var sIdx = int.TryParse(r.GetAttribute("s"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var si) ? si : 0;
            string? v = null, inline = null; var hasF = false;
            if (!r.IsEmptyElement)
            {
                var depth = r.Depth; StringBuilder? vb = null, ib = null; var where = 0; var inPh = 0;   // where: 1 = trong <v>, 2 = trong <is><t>
                while (r.Read() && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
                {
                    switch (r.NodeType)
                    {
                        case XmlNodeType.Element:
                            if (r.LocalName == "v") { vb ??= new StringBuilder(); where = r.IsEmptyElement ? 0 : 1; }
                            else if (r.LocalName == "f") hasF = true;
                            else if (r.LocalName == "is") ib ??= new StringBuilder();
                            else if (r.LocalName == "rPh") { if (!r.IsEmptyElement) inPh++; }
                            else if (r.LocalName == "t" && ib is not null) where = r.IsEmptyElement ? 0 : 2;
                            break;
                        case XmlNodeType.Text: case XmlNodeType.SignificantWhitespace: case XmlNodeType.Whitespace: case XmlNodeType.CDATA:
                            if (where == 1) vb!.Append(r.Value); else if (where == 2 && inPh == 0) ib!.Append(r.Value);
                            break;
                        case XmlNodeType.EndElement:
                            if (r.LocalName == "rPh") inPh--;
                            else if (r.LocalName is "v" or "t") where = 0;
                            break;
                    }
                }
                v = vb?.ToString(); inline = ib?.ToString();
            }
            string text;
            switch (t)
            {
                case "s": text = v is not null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ix) && ix >= 0 && ix < shared.Count ? shared[ix] : ""; break;
                case "inlineStr": text = inline ?? ""; break;
                case "str": text = v ?? ""; break;
                case "b": text = v is null ? "" : v.Trim() is "1" or "true" ? "TRUE" : "FALSE"; break;
                case "e": text = v is null ? "" : "#" + (ErrName.TryGetValue(v.Trim(), out var en) ? en : v.Trim().TrimStart('#')).ToUpperInvariant(); break;
                case "d":
                    text = v is not null && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? (rawNumbers ? dt.ToOADate().ToString("R", CultureInfo.InvariantCulture) : Num(dt.ToOADate(), sIdx, xfFmt, custom)) : (v ?? "");
                    break;
                default:
                    text = v is { Length: > 0 } && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (rawNumbers ? d.ToString("R", CultureInfo.InvariantCulture) : Num(d, sIdx, xfFmt, custom)) : "";
                    break;
            }
            // vùng dùng = ô có nội dung (giống RangeUsed(XLCellsUsedOptions.Contents)): có giá trị hoặc có công thức
            if (text.Length == 0 && !hasF && !(t is "s" or "inlineStr" or "str" && v is not null)) continue;
            if (!cells.TryGetValue(row, out var rowMap)) cells[row] = rowMap = new Dictionary<int, string>();
            rowMap[col] = text;
            if (row < r1) r1 = row; if (row > r2) r2 = row; if (col < c1) c1 = col; if (col > c2) c2 = col;
        }
        var rows = new List<string[]>();
        if (r2 < 0) return rows;
        if (fromA1) { r1 = 1; c1 = 1; }
        for (var rr = r1; rr <= r2; rr++)
        {
            var arr = new string[c2 - c1 + 1];
            if (cells.TryGetValue(rr, out var m)) { for (var c = c1; c <= c2; c++) arr[c - c1] = m.TryGetValue(c, out var x) ? x : ""; }
            else Array.Fill(arr, "");
            rows.Add(arr);
        }
        return rows;
    }

    private static string Num(double d, int sIdx, List<int> xfFmt, Dictionary<int, string> custom)
    {
        var id = sIdx >= 0 && sIdx < xfFmt.Count ? xfFmt[sIdx] : 0;
        return FormatNumber(d, custom.TryGetValue(id, out var code) ? code : "", id);
    }

    private static void ParseRef(string a, out int row, out int col)
    {
        col = 0; var i = 0;
        while (i < a.Length && char.IsLetter(a[i])) { col = col * 26 + (char.ToUpperInvariant(a[i]) - 'A' + 1); i++; }
        row = int.TryParse(a.AsSpan(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rr) ? rr : 0;
    }
}

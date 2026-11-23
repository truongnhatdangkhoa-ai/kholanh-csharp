using KhoLanh.Core.Services;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.DoiChieu;

/// <summary>
/// Đọc / SỬA FILE EXCEL KHO GIỮ NGUYÊN MẪU GỐC (viết lại bằng C# theo dcx-xlsx-edit.js của app cũ).
/// Sửa thẳng XML của sheet trong file .xlsx (không đi qua thư viện dựng lại file) để giữ nguyên: kiểu ô, độ rộng cột, ngăn cố định,
/// nhóm dòng, công thức của các khối không đụng tới, mọi phần khác của file.
///   • BỚT số lượng ở dòng nguồn (hết → xóa dòng) · THÊM / CỘNG vào đúng khối NGÀY (chưa có → khối mới + dòng tổng đúng thứ tự ngày)
///   • tính lại dòng TỔNG NGÀY của khối bị đổi, đánh số lại dòng, chỉnh lại công thức / vùng lọc / dimension theo số dòng mới.
/// Không đoán: thiếu cột Mã hàng / Số lượng / Ngày → báo lỗi, không ghi file.
/// </summary>
public static class DcxXlsx
{
    private const double Eps = 1e-9;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------ tiện ích

    private static string Xesc(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static string EscF(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    public static string Xunesc(string? s)
    {
        var t = (s ?? "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'");
        t = Regex.Replace(t, @"&#(\d+);", m => char.ConvertFromUtf32(int.Parse(m.Groups[1].Value)));
        t = Regex.Replace(t, @"&#x([0-9a-fA-F]+);", m => char.ConvertFromUtf32(int.Parse(m.Groups[1].Value, NumberStyles.HexNumber)));
        return t.Replace("&amp;", "&");
    }
    private static string Strip(string s) => DcxEngine.Strip(s);
    public static int ColIdx(string c) { var n = 0; foreach (var ch in c) n = n * 26 + (ch - 64); return n; }
    public static string ColName(int n) { var t = ""; while (n > 0) { var r = (n - 1) % 26; t = (char)(65 + r) + t; n = (n - 1) / 26; } return t; }
    private static double? Num(string? v) => string.IsNullOrEmpty(v) ? null : double.TryParse(v, NumberStyles.Float, Inv, out var d) && double.IsFinite(d) ? d : null;
    private static string FmtNum(double x) => double.Parse(x.ToString("G15", Inv), Inv).ToString("R", Inv);
    public static double SerialOf(string ymd) => DcxEngine.Serial(ymd);
    public static string YmdOf(double serial) { var d = DateTime.FromOADate(serial); return $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}"; }
    private static string NormMa(string v) => DcxEngine.NormMa(v);

    private static readonly (string Key, string[] Names)[] Field =
    {
        ("ngay", new[] { "ngay", "ngay chung tu", "ngay ct" }),
        ("ma", new[] { "ma hang", "mahang", "ma", "ma hoa" }),
        ("ten", new[] { "ten hang", "tenhang" }),
        ("sl", new[] { "so luong", "soluong", "sl", "so luong (ctns)", "so luong ctns", "sl (ctns)", "sl ctns" }),
        ("tl", new[] { "trong luong", "trongluong" }),
        ("net", new[] { "net", "net kg", "so luong (kg)", "so luong kg", "sl (kg)", "sl kg" }),
        ("size", new[] { "size", "loai size" }),
    };

    // ------------------------------------------------------------------ công thức

    private static readonly Regex Ref = new(@"(?<![A-Za-z0-9_.!$])(\$?)([A-Z]{1,3})(\$?)(\d+)(?![A-Za-z0-9_(])");
    private static string MapOutsideQuotes(string text, Func<string, string> fn)
    {
        var parts = text.Split('"');
        for (var i = 0; i < parts.Length; i += 2) parts[i] = fn(parts[i]);
        return string.Join("\"", parts);
    }
    public static string ShiftFormula(string text, int dR, int dC) => MapOutsideQuotes(text, s => Ref.Replace(s, m =>
        (m.Groups[1].Value.Length > 0 ? "$" + m.Groups[2].Value : ColName(ColIdx(m.Groups[2].Value) + dC)) +
        (m.Groups[3].Value.Length > 0 ? "$" + m.Groups[4].Value : (int.Parse(m.Groups[4].Value) + dR).ToString(Inv))));

    public sealed class RowMap
    {
        public Func<int, int?> Point = x => x, Start = x => x, End = x => x;
    }
    private static readonly Regex RangeRe = new(@"((?:\$?[A-Z]{1,3}\$?\d+)(?::\$?[A-Z]{1,3}\$?\d+)?)(?![A-Za-z0-9_(])");
    private static readonly Regex RangeParts = new(@"^(\$?)([A-Z]{1,3})(\$?)(\d+)(?::(\$?)([A-Z]{1,3})(\$?)(\d+))?$");
    public static string RemapFormula(string text, RowMap fm) => MapOutsideQuotes(text, s => RangeRe.Replace(s, m =>
    {
        var off = m.Index;
        if (off > 0 && Regex.IsMatch(s[off - 1].ToString(), "[A-Za-z0-9_.!$]")) return m.Value;
        var mm = RangeParts.Match(m.Groups[1].Value); if (!mm.Success) return m.Value;
        if (!mm.Groups[8].Success) { var n = fm.Point(int.Parse(mm.Groups[4].Value)); return n is null ? "#REF!" : mm.Groups[1].Value + mm.Groups[2].Value + mm.Groups[3].Value + n; }
        int? a = fm.Start(int.Parse(mm.Groups[4].Value)), b = fm.End(int.Parse(mm.Groups[8].Value));
        return a is null || b is null || a > b ? "#REF!" : mm.Groups[1].Value + mm.Groups[2].Value + mm.Groups[3].Value + a + ":" + mm.Groups[5].Value + mm.Groups[6].Value + mm.Groups[7].Value + b;
    }));

    // ------------------------------------------------------------------ zip

    private sealed class Zip
    {
        public readonly List<string> Order = new();
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        public static Zip Load(byte[] bytes)
        {
            var z = new Zip();
            try
            {
                using var za = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                foreach (var e in za.Entries)
                {
                    using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms);
                    z.Order.Add(e.FullName); z.Files[e.FullName] = ms.ToArray();
                }
            }
            catch (InvalidDataException) { throw new InvalidDataException("File không phải Excel .xlsx (hoặc file bị hỏng)."); }
            return z;
        }
        public bool Has(string p) => Files.ContainsKey(p);
        public string Text(string p) => Unprefix(Encoding.UTF8.GetString(Files[p]).TrimStart('﻿'));
        /// <summary>Một số chương trình (OpenXML SDK, ClosedXML, phần mềm kế toán) ghi thẻ có tiền tố "x:sheetData" —
        /// đổi về dạng không tiền tố (tương đương về XML) để đọc / sửa như file Excel lưu.</summary>
        private static string Unprefix(string xml)
        {
            var head = xml.Length > 4000 ? xml[..4000] : xml;
            var m = Regex.Match(head, @"\sxmlns:([A-Za-z_][\w.-]*)=""http://schemas\.openxmlformats\.org/spreadsheetml/2006/main""");
            if (!m.Success || Regex.IsMatch(head, @"\sxmlns=""")) return xml;   // không tiền tố, hoặc đã có namespace mặc định → không đụng
            var px = Regex.Escape(m.Groups[1].Value);
            xml = Regex.Replace(xml, @"<(/?)" + px + ":", "<$1");
            return Regex.Replace(xml, @"\sxmlns:" + px + @"=""", " xmlns=\"");
        }
        public void Set(string p, string text) { if (!Files.ContainsKey(p)) Order.Add(p); Files[p] = Encoding.UTF8.GetBytes(text); }
        public void Remove(string p) { Files.Remove(p); Order.Remove(p); }
        public byte[] Save()
        {
            using var ms = new MemoryStream();
            using (var za = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var p in Order)
                {
                    var e = za.CreateEntry(p, CompressionLevel.Optimal);
                    using var s = e.Open(); s.Write(Files[p]);
                }
            return ms.ToArray();
        }
    }

    // ------------------------------------------------------------------ đọc workbook

    private sealed record SheetRef(string Name, string Path);
    private static List<SheetRef> Sheets(Zip zip)
    {
        if (!zip.Has("xl/workbook.xml")) throw new InvalidDataException("File Excel hỏng: không có xl/workbook.xml.");
        var wbXml = zip.Text("xl/workbook.xml"); var relXml = zip.Has("xl/_rels/workbook.xml.rels") ? zip.Text("xl/_rels/workbook.xml.rels") : "";
        var list = new List<SheetRef>();
        foreach (Match m in Regex.Matches(wbXml, @"<(?:\w+:)?sheet\b([^>]*?)/>"))
        {
            var a = m.Groups[1].Value; var n = Regex.Match(a, @"\sname=""([^""]*)"""); var r = Regex.Match(a, @"\sr:id=""([^""]*)""");
            if (!n.Success || !r.Success) continue;
            var rel = Regex.Match(relXml, @"<Relationship\b[^>]*?\sId=""" + Regex.Escape(r.Groups[1].Value) + @"""[^>]*?/>");
            var tg = rel.Success ? Regex.Match(rel.Value, @"\sTarget=""([^""]*)""") : Match.Empty;
            if (!tg.Success) continue;
            var path = tg.Groups[1].Value.StartsWith('/') ? tg.Groups[1].Value[1..] : "xl/" + tg.Groups[1].Value;
            list.Add(new SheetRef(Xunesc(n.Groups[1].Value), path));
        }
        if (list.Count == 0) throw new InvalidDataException("File Excel không có sheet.");
        return list;
    }
    private static List<string> SharedStrings(Zip zip)
    {
        var sst = new List<string>();
        if (!zip.Has("xl/sharedStrings.xml")) return sst;
        foreach (Match m in Regex.Matches(zip.Text("xl/sharedStrings.xml"), @"<si\b[^>]*?(?:/>|>([\s\S]*?)</si>)"))
            sst.Add(Xunesc(string.Concat(Regex.Matches(Regex.Replace(m.Groups[1].Value, @"<rPh\b[\s\S]*?</rPh>", ""), @"<t\b[^>]*?>([\s\S]*?)</t>").Select(t => t.Groups[1].Value))));
        return sst;
    }

    public sealed class Workbook
    {
        public List<string> Names = new();
        public Dictionary<string, List<object?[]>> Aoa = new();
    }

    /// <summary>Đọc mọi sheet thành mảng ô (giống SheetJS raw: số giữ nguyên, ngày = số ngày Excel). Chỉ lấy vùng có dữ liệu thật, tối đa <paramref name="maxCol"/> cột.</summary>
    public static Workbook ReadWorkbook(byte[] bytes, int maxCol = 40)
    {
        var zip = Zip.Load(bytes); var sst = SharedStrings(zip); var wb = new Workbook();
        foreach (var sh in Sheets(zip))
        {
            wb.Names.Add(sh.Name);
            wb.Aoa[sh.Name] = zip.Has(sh.Path) ? ReadAoa(zip.Text(sh.Path), sst, maxCol) : new();
        }
        return wb;
    }
    private static List<object?[]> ReadAoa(string xml, List<string> sst, int maxCol)
    {
        var cells = new List<(int r, int c, object v)>(); int maxR = -1, maxC = -1;
        foreach (Match rm in Regex.Matches(xml, @"<row\b([^>]*?)(?:/>|>([\s\S]*?)</row>)"))
        {
            var rr = Regex.Match(rm.Groups[1].Value, @"\sr=""(\d+)""");
            var rowNo = rr.Success ? int.Parse(rr.Groups[1].Value) : maxR + 2;
            var ci = 0;
            foreach (Match cm in Regex.Matches(rm.Groups[2].Value, @"<c\b([^>]*?)(?:/>|>([\s\S]*?)</c>)"))
            {
                var ca = cm.Groups[1].Value; var refm = Regex.Match(ca, @"\sr=""([A-Z]+)(\d+)""");
                var col = refm.Success ? ColIdx(refm.Groups[1].Value) : ci + 1; ci = col;
                var t = Regex.Match(ca, @"\st=""([^""]*)""").Groups[1].Value; var inner = cm.Groups[2].Value;
                var vm = Regex.Match(inner, @"<v>([\s\S]*?)</v>");
                object? v = null;
                switch (t)
                {
                    case "s": if (vm.Success && int.TryParse(vm.Groups[1].Value, out var si) && si < sst.Count) v = sst[si]; break;
                    case "inlineStr": v = Xunesc(string.Concat(Regex.Matches(inner, @"<t\b[^>]*?>([\s\S]*?)</t>").Select(x => x.Groups[1].Value))); break;
                    case "str": case "e": if (vm.Success) v = Xunesc(vm.Groups[1].Value); break;
                    case "b": if (vm.Success) v = vm.Groups[1].Value.Trim() == "1"; break;
                    case "d": if (vm.Success && DateTime.TryParse(vm.Groups[1].Value, Inv, DateTimeStyles.RoundtripKind, out var dt)) v = dt.ToOADate(); break;
                    default: if (vm.Success) v = Num(vm.Groups[1].Value.Trim()); break;
                }
                if (v is null || (v is string s && s.Length == 0)) continue;
                int r0 = rowNo - 1, c0 = col - 1;
                cells.Add((r0, c0, v)); if (r0 > maxR) maxR = r0; if (c0 > maxC) maxC = c0;
            }
        }
        var aoa = new List<object?[]>();
        if (maxR < 0) return aoa;
        var width = Math.Min(maxC, maxCol - 1) + 1;
        for (var i = 0; i <= maxR; i++) aoa.Add(new object?[width]);
        foreach (var (r, c, v) in cells) if (c < width) aoa[r][c] = v;
        return aoa;
    }

    // ------------------------------------------------------------------ sheet để sửa

    private sealed class FCell { public string Attrs = ""; public string? Text; }
    private sealed class XCell
    {
        public string Col = ""; public string? S, T; public string Extra = ""; public FCell? F; public string? V, Is;
    }
    private sealed class XRow
    {
        public int R; public int? Orig; public string Attrs = ""; public List<XCell> Cells = new();
        public string Kind = ""; public string? Dk; public bool Del, Changed, IsNew; public int Nr;
    }
    private sealed class Block { public List<XRow> Dets = new(); public XRow? Tot; public string? Date; public bool IsNew, Gone; }
    private sealed class Book
    {
        public Zip Zip = null!; public string Path = "", WbXml = "", SheetName = "", Head = "", Tail = "";
        public List<string> Sst = new(); public List<XRow> Rows = new(); public List<Block> Blocks = new();
    }

    private static Book OpenBook(byte[] bytes, string? sheetName)
    {
        var zip = Zip.Load(bytes); var sheets = Sheets(zip);
        var sh = (sheetName is not null ? sheets.FirstOrDefault(s => s.Name == sheetName) : null) ?? sheets[0];
        if (sheetName is not null && sh.Name != sheetName) throw new InvalidDataException($"File không có sheet “{sheetName}”.");
        if (!zip.Has(sh.Path)) throw new InvalidDataException("File Excel hỏng: không thấy dữ liệu sheet.");
        var xml = zip.Text(sh.Path);
        var i0 = xml.IndexOf("<sheetData", StringComparison.Ordinal); var i1 = xml.IndexOf("</sheetData>", StringComparison.Ordinal);
        var book = new Book { Zip = zip, Path = sh.Path, WbXml = zip.Text("xl/workbook.xml"), SheetName = sh.Name, Sst = SharedStrings(zip) };
        string body;
        if (i0 >= 0 && i1 < 0 && Regex.IsMatch(xml[i0..], @"^<sheetData\s*/>"))
        {   // sheet rỗng
            var end = xml.IndexOf("/>", i0, StringComparison.Ordinal) + 2;
            book.Head = xml[..i0] + "<sheetData>"; book.Tail = xml[end..]; body = "";
        }
        else
        {
            if (i0 < 0 || i1 < 0) throw new InvalidDataException("Sheet không có dữ liệu.");
            var gt = xml.IndexOf('>', i0) + 1;
            book.Head = xml[..i0] + "<sheetData>"; book.Tail = xml[(i1 + 12)..]; body = xml[gt..i1];
        }
        foreach (Match m in Regex.Matches(body, @"<row\b([^>]*?)(?:/>|>([\s\S]*?)</row>)"))
        {
            var attrs = m.Groups[1].Value; var r = int.Parse(Regex.Match(attrs, @"\sr=""(\d+)""").Groups[1].Value);
            var row = new XRow { R = r, Orig = r, Attrs = Regex.Replace(attrs, @"\sr=""\d+""", "") };
            foreach (Match mc in Regex.Matches(m.Groups[2].Value, @"<c\b([^>]*?)(?:/>|>([\s\S]*?)</c>)"))
            {
                var ca = mc.Groups[1].Value; var refm = Regex.Match(ca, @"\sr=""([A-Z]+)(\d+)"""); if (!refm.Success) continue;
                var s = Regex.Match(ca, @"\ss=""(\d+)"""); var t = Regex.Match(ca, @"\st=""([^""]*)""");
                var extra = Regex.Replace(Regex.Replace(Regex.Replace(ca, @"\sr=""[^""]*""", ""), @"\ss=""[^""]*""", ""), @"\st=""[^""]*""", "");
                var cell = new XCell { Col = refm.Groups[1].Value, S = s.Success ? s.Groups[1].Value : null, T = t.Success ? t.Groups[1].Value : null, Extra = extra };
                var ci2 = mc.Groups[2].Value;
                var f = Regex.Match(ci2, @"<f\b([^>]*?)(?:/>|>([\s\S]*?)</f>)"); if (f.Success) cell.F = new FCell { Attrs = f.Groups[1].Value, Text = f.Groups[2].Success ? Xunesc(f.Groups[2].Value) : null };
                var v = Regex.Match(ci2, @"<v>([\s\S]*?)</v>"); if (v.Success) cell.V = v.Groups[1].Value;
                var isn = Regex.Match(ci2, @"<is>[\s\S]*?</is>"); if (isn.Success) cell.Is = isn.Value;
                row.Cells.Add(cell);
            }
            book.Rows.Add(row);
        }
        ExpandShared(book);
        return book;
    }
    /// <summary>Công thức dùng chung (t="shared") → công thức riêng từng ô để chèn / xóa dòng không làm hỏng.</summary>
    private static void ExpandShared(Book book)
    {
        var master = new Dictionary<string, (string text, int r, int c)>();
        foreach (var row in book.Rows) foreach (var c in row.Cells)
        {
            if (c.F is null || !Regex.IsMatch(c.F.Attrs, @"\bt=""shared""")) continue;
            var si = Regex.Match(c.F.Attrs, @"\bsi=""(\d+)""").Groups[1].Value;
            if (!string.IsNullOrEmpty(c.F.Text)) master[si] = (c.F.Text, row.R, ColIdx(c.Col));
        }
        foreach (var row in book.Rows) foreach (var c in row.Cells)
        {
            if (c.F is null || !Regex.IsMatch(c.F.Attrs, @"\bt=""shared""")) continue;
            var si = Regex.Match(c.F.Attrs, @"\bsi=""(\d+)""").Groups[1].Value;
            if (!master.TryGetValue(si, out var m)) throw new InvalidDataException($"Công thức dùng chung hỏng ở ô {c.Col}{row.R} — không sửa file này.");
            c.F = new FCell { Attrs = "", Text = !string.IsNullOrEmpty(c.F.Text) ? c.F.Text : ShiftFormula(m.text, row.R - m.r, ColIdx(c.Col) - m.c) };
        }
    }
    private static XCell? CellOf(XRow row, string? col) => col is null ? null : row.Cells.FirstOrDefault(c => c.Col == col);
    private static string TextOf(Book book, XCell? c)
    {
        if (c is null) return "";
        if (c.T == "s") return c.V is null || !int.TryParse(c.V, out var i) || i >= book.Sst.Count ? "" : book.Sst[i];
        if (c.T == "inlineStr") return Xunesc(string.Concat(Regex.Matches(c.Is ?? "", @"<t\b[^>]*?>([\s\S]*?)</t>").Select(x => x.Groups[1].Value)));
        if (c.V is null) return "";
        return c.T is "str" or "e" ? Xunesc(c.V) : c.V;
    }
    private static double? NumOf(XCell? c) => c is not null && c.V is not null && (c.T is null || c.T == "n") ? Num(c.V) : null;

    private sealed record Cols(int Header, Dictionary<string, string> Map)
    {
        public string? this[string k] => Map.TryGetValue(k, out var v) ? v : null;
    }
    private static Cols FindCols(Book book)
    {
        for (var i = 0; i < Math.Min(book.Rows.Count, 30); i++)
        {
            var row = book.Rows[i]; var map = new Dictionary<string, string>();
            foreach (var c in row.Cells)
            {
                var t = Strip(TextOf(book, c)); if (t.Length == 0) continue;
                foreach (var (f, names) in Field) if (names.Contains(t) && !map.ContainsKey(f)) map[f] = c.Col;
            }
            if (map.ContainsKey("ma") && map.ContainsKey("sl") && map.ContainsKey("ngay")) return new Cols(row.R, map);
        }
        throw new BusinessException("Không tìm thấy dòng tiêu đề có đủ cột Mã hàng, Số lượng, Ngày trong file Excel gốc.");
    }
    private static string? DateKeyOf(Book book, XCell? c)
    {
        if (c is null) return null;
        var n = NumOf(c); if (n is > 20000 and < 80000) return YmdOf(n.Value);
        var t = TextOf(book, c).Trim();
        var m = Regex.Match(t, @"^(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{4})$");
        if (m.Success) return m.Groups[3].Value + "-" + m.Groups[2].Value.PadLeft(2, '0') + "-" + m.Groups[1].Value.PadLeft(2, '0');
        m = Regex.Match(t, @"^(\d{4})-(\d{2})-(\d{2})");
        return m.Success ? $"{m.Groups[1]}-{m.Groups[2]}-{m.Groups[3]}" : null;
    }
    private static void Classify(Book book, Cols info)
    {
        foreach (var row in book.Rows)
        {
            if (row.R <= info.Header) { row.Kind = "pre"; continue; }
            var ma = TextOf(book, CellOf(row, info["ma"])).Trim(); var dk = DateKeyOf(book, CellOf(row, info["ngay"]));
            var sl = CellOf(row, info["sl"]); var nt = info["net"] is null ? null : CellOf(row, info["net"]);
            if (ma.Length > 0) row.Kind = "det";
            else if (dk is not null && ((sl is not null && (sl.F is not null || sl.V is not null)) || (nt is not null && (nt.F is not null || nt.V is not null)))) row.Kind = "tot";
            else row.Kind = "oth";
            row.Dk = dk;
        }
        var blocks = new List<Block>(); var cur = new List<XRow>();
        foreach (var row in book.Rows)
        {
            if (row.Kind == "det") cur.Add(row);
            else if (row.Kind == "tot") { blocks.Add(new Block { Dets = cur, Tot = row, Date = row.Dk ?? cur.FirstOrDefault()?.Dk }); cur = new(); }
            else if (cur.Count > 0) { blocks.Add(new Block { Dets = cur, Date = cur[0].Dk }); cur = new(); }
        }
        if (cur.Count > 0) blocks.Add(new Block { Dets = cur, Date = cur[0].Dk });
        book.Blocks = blocks;
    }

    // ------------------------------------------------------------------ đọc vài dòng

    public sealed record CellInfo(string Ma, string Ten, double? Sl, double? Tl, double? Net, string? Ngay);
    public static Dictionary<int, CellInfo> ReadCells(byte[] bytes, string sheetName, IEnumerable<int> rowNums)
    {
        var book = OpenBook(bytes, sheetName); var info = FindCols(book); var o = new Dictionary<int, CellInfo>();
        foreach (var n in rowNums)
        {
            var row = book.Rows.FirstOrDefault(r => r.R == n); if (row is null) continue;
            XCell? G(string k) => info[k] is null ? null : CellOf(row, info[k]);
            o[n] = new CellInfo(TextOf(book, G("ma")), info["ten"] is not null ? TextOf(book, G("ten")) : "", NumOf(G("sl")),
                info["tl"] is not null ? NumOf(G("tl")) : null, info["net"] is not null ? NumOf(G("net")) : null, DateKeyOf(book, G("ngay")));
        }
        return o;
    }

    // ------------------------------------------------------------------ kiểu ô: xanh lá (chuyển kho) / tổng ngày (nền vàng, chữ đỏ đậm)

    private const string Green = "FF92D050", TotFill = "FFFFFF00", TotRed = "FFFF0000";
    private sealed class Styler
    {
        private readonly string _xml; private readonly Match _fm, _cm, _nm;
        private readonly List<string> _fills, _xfs, _fonts;
        private readonly Dictionary<int, int> _cache = new(), _fontCache = new();
        private readonly int _fillId; private readonly bool _tot;
        private Styler(string xml, Match fm, Match cm, Match nm, bool tot, string fillRgb)
        {
            _xml = xml; _fm = fm; _cm = cm; _nm = nm; _tot = tot;
            _fills = Regex.Matches(fm.Groups[2].Value, @"<fill\b[^>]*?/>|<fill\b[\s\S]*?</fill>").Select(x => x.Value).ToList();
            _xfs = Regex.Matches(cm.Groups[2].Value, @"<xf\b[^>]*?/>|<xf\b[^>]*?>[\s\S]*?</xf>").Select(x => x.Value).ToList();
            _fonts = nm.Success ? Regex.Matches(nm.Groups[2].Value, @"<font\b[^>]*?/>|<font\b[\s\S]*?</font>").Select(x => x.Value).ToList() : new();
            _fillId = _fills.Count; _fills.Add($"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{fillRgb}\"/><bgColor indexed=\"64\"/></patternFill></fill>");
        }
        public static Styler? Create(string xml, bool tot)
        {
            var fm = Regex.Match(xml, @"<fills\b([^>]*)>([\s\S]*?)</fills>"); var cm = Regex.Match(xml, @"<cellXfs\b([^>]*)>([\s\S]*?)</cellXfs>"); var nm = Regex.Match(xml, @"<fonts\b([^>]*)>([\s\S]*?)</fonts>");
            if (!fm.Success || !cm.Success || (tot && !nm.Success)) return null;
            var s = new Styler(xml, fm, cm, nm, tot, tot ? TotFill : Green);
            if (s._xfs.Count == 0 || (tot && s._fonts.Count == 0)) return null;
            return s;
        }
        private int RedBold(int fi)
        {
            if (_fontCache.TryGetValue(fi, out var k)) return k;
            var f = fi < _fonts.Count ? _fonts[fi] : _fonts[0];
            f = Regex.Replace(Regex.Replace(f, @"<b\b[^>]*?/>", ""), @"<color\b[^>]*?/>", "");
            if (Regex.IsMatch(f, @"^<font\b[^>]*/>$")) f = Regex.Replace(f, @"/>$", "></font>");
            f = Regex.Replace(f, @"^(<font\b[^>]*>)", "$1<b/>");
            if (Regex.IsMatch(f, @"<sz\b[^>]*?/>")) f = new Regex(@"(<sz\b[^>]*?/>)").Replace(f, $"$1<color rgb=\"{TotRed}\"/>", 1);
            else if (Regex.IsMatch(f, @"<name\b")) f = new Regex(@"(<name\b)").Replace(f, $"<color rgb=\"{TotRed}\"/>$1", 1);
            else f = Regex.Replace(f, @"</font>$", $"<color rgb=\"{TotRed}\"/></font>");
            _fonts.Add(f); _fontCache[fi] = _fonts.Count - 1; return _fonts.Count - 1;
        }
        public string Get(string? s0)
        {
            var i = s0 is null ? 0 : int.Parse(s0, Inv);
            if (_cache.TryGetValue(i, out var k)) return k.ToString(Inv);
            var x0 = i < _xfs.Count ? _xfs[i] : _xfs[0];
            var x = Regex.Replace(Regex.Replace(x0, @"\sfillId=""[^""]*""", ""), @"\sapplyFill=""[^""]*""", "");
            if (_tot)
            {
                var fi = Regex.Match(x0, @"\sfontId=""(\d+)"""); var nf = RedBold(fi.Success ? int.Parse(fi.Groups[1].Value) : 0);
                x = Regex.Replace(Regex.Replace(x, @"\sfontId=""[^""]*""", ""), @"\sapplyFont=""[^""]*""", "");
                x = Regex.Replace(x, @"^<xf\b", $"<xf fillId=\"{_fillId}\" applyFill=\"1\" fontId=\"{nf}\" applyFont=\"1\"");
            }
            else x = Regex.Replace(x, @"^<xf\b", $"<xf fillId=\"{_fillId}\" applyFill=\"1\"");
            _xfs.Add(x); _cache[i] = _xfs.Count - 1; return (_xfs.Count - 1).ToString(Inv);
        }
        public string Finish()
        {
            var o = _xml;
            if (_tot) o = o.Replace(_nm.Value, $"<fonts count=\"{_fonts.Count}\"" + Regex.Replace(_nm.Groups[1].Value, @"\scount=""\d+""", "") + ">" + string.Concat(_fonts) + "</fonts>");
            o = o.Replace(_fm.Value, $"<fills count=\"{_fills.Count}\"" + Regex.Replace(_fm.Groups[1].Value, @"\scount=""\d+""", "") + ">" + string.Concat(_fills) + "</fills>");
            o = o.Replace(_cm.Value, $"<cellXfs count=\"{_xfs.Count}\"" + Regex.Replace(_cm.Groups[1].Value, @"\scount=""\d+""", "") + ">" + string.Concat(_xfs) + "</cellXfs>");
            return o;
        }
    }

    // ------------------------------------------------------------------ chuyển số liệu

    public sealed record Remove(int Row, double Sl);
    public sealed record Add(string Ngay, string Ma, string Ten, double Sl, double? Tl, double? Net, bool NoMerge = false);
    public sealed record Rename(int Row, string Ma, string Ten);
    public sealed class Spec
    {
        public List<Remove> Removes = new();
        public List<Add> Adds = new();
        public List<Rename> Renames = new();
        public bool Green = true, MarkTotals = true;
        public Func<string, string, string>? SizeOf;
    }
    public sealed class Report
    {
        public List<string> Removed = new(), Reduced = new(), Merged = new(), Added = new(), NewBlocks = new(), EmptiedDays = new(), Renamed = new();
        public List<(int Row, string Ma)> NetKept = new();
        public List<(string Ngay, double Sl, double Net, double? OldTot, double? OldSum, bool OldSumOk, bool IsNew)> Recalc = new();
        public int GreenCells, TotMarked;
    }

    public static (byte[] Bytes, Report Report) Transform(byte[] bytes, string sheetName, Spec spec)
    {
        var book = OpenBook(bytes, sheetName); var info = FindCols(book); Classify(book, info);
        string C(string k) => info[k]!;
        var report = new Report();
        var touched = new HashSet<Block>();
        Block? BlockOf(XRow row) => book.Blocks.FirstOrDefault(b => b.Dets.Contains(row));
        double? ValSl(XRow row) { var c = CellOf(row, C("sl")); return c is null ? null : Num(c.V); }
        void SetSl(XRow row, double v) { var c = CellOf(row, C("sl"))!; c.V = FmtNum(v); if (c.T is not null) c.T = null; }
        void BumpSl(XRow row, double delta)
        {
            var c = CellOf(row, C("sl"))!; var old = ValSl(row) ?? 0; var nv = old + delta;
            if (c.F is { Text: not null }) c.F.Text = c.F.Text + (delta < 0 ? "-" : "+") + FmtNum(Math.Abs(delta));
            SetSl(row, nv);
            if (info["net"] is not null && info["tl"] is not null)
            {
                var nc = CellOf(row, C("net")); var tc = CellOf(row, C("tl")); var tl = NumOf(tc);
                if (nc is not null && nc.F is null && nc.V is not null && tl is not null)
                {
                    var on = Num(nc.V);
                    if (on is not null && Math.Abs(on.Value - old * tl.Value) <= 1e-6 * Math.Max(1, Math.Abs(on.Value))) nc.V = FmtNum(nv * tl.Value);
                    else report.NetKept.Add((row.Orig ?? 0, TextOf(book, CellOf(row, C("ma"))).Trim()));
                }
            }
        }
        var greens = new HashSet<XRow>();
        var oldInfo = new Dictionary<Block, (double sum, double? tot)>();
        void NoteOld(Block b)
        {
            if (oldInfo.ContainsKey(b)) return;
            var sum = b.Dets.Sum(r => ValSl(r) ?? 0); var t = b.Tot is null ? null : CellOf(b.Tot, C("sl"));
            oldInfo[b] = (sum, t is null ? null : Num(t.V));
        }

        foreach (var rn in spec.Renames)
        {
            if (info["ten"] is null) throw new BusinessException("File không có cột “Tên hàng” — không cập nhật tên được.");
            var row = book.Rows.FirstOrDefault(r => r.R == rn.Row);
            if (row is null || row.Kind != "det") throw new BusinessException($"Dòng {rn.Row} không còn là dòng chi tiết trong file gốc — không đổi tên được (file có bị sửa sau khi nạp?).");
            if (rn.Ma.Length > 0 && NormMa(TextOf(book, CellOf(row, C("ma")))) != NormMa(rn.Ma)) throw new BusinessException($"Dòng {rn.Row} không còn là mã {rn.Ma} (file có bị sửa sau khi nạp?).");
            var c = CellOf(row, C("ten")) ?? throw new BusinessException($"Dòng {rn.Row} không có ô Tên hàng.");
            var before = TextOf(book, c);
            c.T = "inlineStr"; c.Is = "<is><t xml:space=\"preserve\">" + Xesc(rn.Ten) + "</t></is>"; c.V = null; c.F = null; row.Changed = true;
            report.Renamed.Add($"Dòng {rn.Row}: {before} → {rn.Ten}");
        }

        foreach (var rm in spec.Removes)
        {
            var row = book.Rows.FirstOrDefault(r => r.R == rm.Row);
            if (row is null || row.Kind != "det") throw new BusinessException($"Dòng {rm.Row} không còn là dòng chi tiết trong file gốc — không bớt được (file có bị sửa sau khi nạp?).");
            var cur = ValSl(row) ?? throw new BusinessException($"Dòng {rm.Row}: không đọc được Số lượng.");
            if (rm.Sl > cur + Eps) throw new BusinessException($"Dòng {rm.Row}: cần bớt {FmtNum(rm.Sl)} nhưng file chỉ có {FmtNum(cur)}.");
            var b = BlockOf(row)!; NoteOld(b); touched.Add(b);
            var ma = TextOf(book, CellOf(row, C("ma"))).Trim();
            if (Math.Abs(cur - rm.Sl) <= Eps) { row.Del = true; report.Removed.Add($"{ma} · {row.Dk} · {FmtNum(rm.Sl)}"); }
            else { BumpSl(row, -rm.Sl); row.Changed = true; if (spec.Green) greens.Add(row); report.Reduced.Add($"{ma} · {row.Dk} · −{FmtNum(rm.Sl)}"); }
        }

        XCell MkCell(XCell? tpl, string col, (string? str, string? f, double? v)? content)
        {
            var c = new XCell { Col = col, S = tpl?.S, Extra = tpl?.Extra ?? "" };
            if (content is { } ct)
            {
                if (ct.str is not null) { c.T = "inlineStr"; c.Is = "<is><t xml:space=\"preserve\">" + Xesc(ct.str) + "</t></is>"; }
                else { if (ct.f is not null) c.F = new FCell { Attrs = "", Text = ct.f }; if (ct.v is not null) c.V = FmtNum(ct.v.Value); }
            }
            return c;
        }
        XRow CloneRow(XRow tplRow, Func<string, (string? str, string? f, double? v)?> build)
        {
            var row = new XRow { R = -1, Orig = null, IsNew = true, Attrs = Regex.Replace(tplRow.Attrs, @"\shidden=""1""", "") };
            foreach (var tc in tplRow.Cells) row.Cells.Add(MkCell(tc, tc.Col, build(tc.Col)));
            return row;
        }
        List<XRow> LiveDets(Block b) => b.Dets.Where(r => !r.Del).ToList();
        XRow? AnyDet() { foreach (var b in book.Blocks) { var r = b.Dets.FirstOrDefault(x => !x.Del); if (r is not null) return r; } return null; }
        var netIsFormula = info["net"] is not null && AnyDet() is { } ad0 && CellOf(ad0, C("net")) is { F: not null };

        foreach (var ad in spec.Adds)
        {
            if (!(ad.Sl > 0)) continue;
            var serial = SerialOf(ad.Ngay); if (double.IsNaN(serial)) throw new BusinessException("Ngày không hợp lệ: " + ad.Ngay);
            var b = book.Blocks.FirstOrDefault(x => x.Date == ad.Ngay);
            var tlOk = info["tl"] is not null && ad.Tl is not null;
            if (b is not null)
            {
                NoteOld(b); touched.Add(b);
                var same = ad.NoMerge ? null : LiveDets(b).FirstOrDefault(r => NormMa(TextOf(book, CellOf(r, C("ma")))) == NormMa(ad.Ma) &&
                    (!tlOk || (NumOf(CellOf(r, C("tl"))) is double rt && Math.Abs(rt - ad.Tl!.Value) < 1e-9)));
                if (same is not null) { BumpSl(same, ad.Sl); same.Changed = true; if (spec.Green) greens.Add(same); report.Merged.Add($"{ad.Ma} · {ad.Ngay} · +{FmtNum(ad.Sl)}"); continue; }
            }
            var tpl = (b is not null ? (LiveDets(b).FirstOrDefault() ?? b.Dets.FirstOrDefault()) : null) ?? AnyDet()
                ?? throw new BusinessException("File Kho gốc không có dòng chi tiết nào để lấy làm mẫu định dạng.");
            (string? str, string? f, double? v)? Build(string col)
            {
                if (col == C("ma")) return (ad.Ma, null, null);
                if (info["ten"] is not null && col == C("ten")) return (ad.Ten, null, null);
                if (col == C("sl")) return (null, null, ad.Sl);
                if (col == C("ngay")) return (null, null, serial);
                if (info["tl"] is not null && col == C("tl")) return tlOk ? (null, null, ad.Tl) : null;
                if (info["size"] is not null && col == C("size"))
                {
                    var z = ""; try { z = spec.SizeOf?.Invoke(ad.Ma, ad.Ten) ?? ""; } catch { z = ""; }
                    return z.Length > 0 ? (z, null, null) : null;
                }
                if (info["net"] is not null && col == C("net"))
                    return netIsFormula && info["tl"] is not null ? (null, "__NET__", tlOk ? ad.Sl * ad.Tl!.Value : 0) : (null, null, ad.Net ?? (tlOk ? ad.Sl * ad.Tl!.Value : 0));
                return null;
            }
            var nr = CloneRow(tpl, Build); nr.Dk = ad.Ngay; nr.Kind = "det"; nr.Changed = true; if (spec.Green) greens.Add(nr);
            if (b is null)
            {
                var totTpl = book.Blocks.FirstOrDefault(x => x.Tot is not null)?.Tot ?? throw new BusinessException("File Kho gốc không có dòng tổng ngày nào để lấy làm mẫu.");
                var tot = CloneRow(totTpl, col => col == C("ngay") ? (null, null, serial) : col == C("sl") || (info["net"] is not null && col == C("net")) ? (null, "__SUM__", 0) : null);
                tot.Kind = "tot"; tot.Dk = ad.Ngay; tot.Changed = true;
                b = new Block { Dets = new() { nr }, Tot = tot, Date = ad.Ngay, IsNew = true }; touched.Add(b);
                var before = book.Blocks.FirstOrDefault(x => x.Date is not null && string.CompareOrdinal(x.Date, ad.Ngay) > 0);
                int at;
                if (before is not null) { var first = before.Dets.FirstOrDefault() ?? before.Tot!; at = book.Rows.IndexOf(first); }
                else
                {
                    var last = book.Blocks.LastOrDefault(); var lr = last is null ? null : last.Tot ?? last.Dets.LastOrDefault();
                    at = lr is not null ? book.Rows.IndexOf(lr) + 1 : book.Rows.Count;
                }
                book.Rows.InsertRange(at, new[] { nr, tot });
                book.Blocks.Insert(before is not null ? book.Blocks.IndexOf(before) : book.Blocks.Count, b);
                report.NewBlocks.Add(ad.Ngay); report.Added.Add($"{ad.Ma} · {ad.Ngay} · {FmtNum(ad.Sl)} (ngày mới)"); continue;
            }
            var live = LiveDets(b);
            string Key(XRow r) => NormMa(TextOf(book, CellOf(r, C("ma"))));
            var sorted = true; for (var i = 1; i < live.Count; i++) if (string.CompareOrdinal(Key(live[i - 1]), Key(live[i])) > 0) { sorted = false; break; }
            XRow? after = live.LastOrDefault();
            if (sorted) { var nm = NormMa(ad.Ma); after = null; foreach (var r in live) if (string.CompareOrdinal(Key(r), nm) <= 0) after = r; }
            var anchor = live.FirstOrDefault() ?? b.Dets[0];
            var at2 = after is not null ? book.Rows.IndexOf(after) + 1 : book.Rows.IndexOf(anchor);
            book.Rows.Insert(at2, nr);
            var bi = after is not null ? b.Dets.IndexOf(after) + 1 : b.Dets.IndexOf(anchor); b.Dets.Insert(Math.Max(0, bi), nr);
            report.Added.Add($"{ad.Ma} · {ad.Ngay} · {FmtNum(ad.Sl)}");
        }

        foreach (var b in book.Blocks)
            if (touched.Contains(b) && LiveDets(b).Count == 0) { if (b.Tot is not null) b.Tot.Del = true; b.Gone = true; report.EmptiedDays.Add(b.Date ?? ""); }

        // ---------- đánh số lại dòng
        var exist = book.Rows.Where(r => r.Orig is not null).Select(r => r.Orig!.Value).ToHashSet();
        var outRows = book.Rows.Where(r => !r.Del).ToList();
        XRow? prev = null; var n = 0;
        foreach (var row in outRows)
        {
            if (prev is null) n = row.IsNew ? 1 : row.R;
            else
            {
                var gap = 1;
                if (!row.IsNew && !prev.IsNew && prev.Orig is not null) for (var k = prev.Orig.Value + 1; k < row.Orig!.Value; k++) if (!exist.Contains(k)) gap++;
                n += gap;
            }
            row.Nr = n; prev = row;
        }
        var survive = outRows.Where(r => r.Orig is not null).ToList();
        var fm = new RowMap
        {
            Point = x =>
            {
                var f = survive.FirstOrDefault(r => r.Orig == x); if (f is not null) return f.Nr;
                if (book.Rows.Any(r => r.Orig == x)) return null;
                XRow? p = null; foreach (var r in survive) if (r.Orig <= x) p = r; return p is not null ? p.Nr + (x - p.Orig!.Value) : x;
            },
            Start = x => { var f = survive.FirstOrDefault(r => r.Orig >= x); return f is not null ? f.Nr : survive.Count > 0 ? survive[^1].Nr + 1 : x; },
            End = x => { XRow? p = null; foreach (var r in survive) if (r.Orig <= x) p = r; return p?.Nr; },
        };

        // ---------- công thức + giá trị tạm
        var liveBlocks = book.Blocks.Where(b => !b.Gone).ToList();
        foreach (var row in outRows) foreach (var c in row.Cells)
            if (c.F is { Text: not null } && c.F.Text != "__NET__" && c.F.Text != "__SUM__") c.F.Text = RemapFormula(c.F.Text, fm);
        foreach (var b in liveBlocks)
        {
            var dets = LiveDets(b);
            foreach (var row in dets)
            {
                if (info["net"] is null) break;
                var c = CellOf(row, C("net"));
                if (c is { F.Text: "__NET__" }) c.F.Text = info["tl"] is not null ? C("sl") + row.Nr + "*" + C("tl") + row.Nr : null;
                if (row.Changed && c is { F: not null } && info["tl"] is not null)
                {
                    var sl = Num(CellOf(row, C("sl"))?.V); var tl = Num(CellOf(row, C("tl"))?.V);
                    if (sl is not null && tl is not null) c.V = FmtNum(sl.Value * tl.Value);
                }
            }
            if (!touched.Contains(b) || b.Tot is null || dets.Count == 0) continue;
            int first = dets[0].Nr, last = dets[^1].Nr; var tot = b.Tot;
            var sumSl = dets.Sum(r => Num(CellOf(r, C("sl"))?.V) ?? 0);
            var sumNet = info["net"] is not null ? dets.Sum(r => Num(CellOf(r, C("net"))?.V) ?? 0) : 0;
            void SetSum(string col, double val) { var c = CellOf(tot, col); if (c is null) return; c.F = new FCell { Attrs = "", Text = $"SUM({col}{first}:{col}{last})" }; c.V = FmtNum(val); c.T = null; }
            var had = oldInfo.TryGetValue(b, out var oi);
            SetSum(C("sl"), sumSl); if (info["net"] is not null) SetSum(C("net"), sumNet);
            report.Recalc.Add((b.Date ?? "", sumSl, sumNet, had ? oi.tot : null, had ? oi.sum : null, !had || oi.tot is null || Math.Abs(oi.tot.Value - oi.sum) <= 1e-6, b.IsNew));
        }

        // ---------- tô xanh lá ô Số lượng các dòng chuyển kho
        if (greens.Count > 0 && book.Zip.Has("xl/styles.xml") && Styler.Create(book.Zip.Text("xl/styles.xml"), false) is { } gs)
        {
            foreach (var row in greens) { if (row.Del) continue; var c = CellOf(row, C("sl")); if (c is not null) { c.S = gs.Get(c.S); report.GreenCells++; } }
            book.Zip.Set("xl/styles.xml", gs.Finish());
        }
        // ---------- dòng TỔNG NGÀY nền vàng, chữ đỏ đậm
        if (spec.MarkTotals && book.Zip.Has("xl/styles.xml"))
        {
            var tcols = new[] { info["sl"], info["ngay"], info["tl"], info["net"] }.Where(x => x is not null).ToList();
            var tgt = new List<XCell>();
            foreach (var b in liveBlocks) if (b.Tot is { Del: false }) foreach (var col in tcols) if (CellOf(b.Tot, col) is { } c) tgt.Add(c);
            if (tgt.Count > 0 && Styler.Create(book.Zip.Text("xl/styles.xml"), true) is { } ts)
            {
                foreach (var c in tgt) c.S = ts.Get(c.S);
                report.TotMarked = liveBlocks.Count(b => b.Tot is { Del: false });
                book.Zip.Set("xl/styles.xml", ts.Finish());
            }
        }

        // ---------- ghi lại sheet.xml
        var lastNr = outRows.Count > 0 ? outRows[^1].Nr : 1;
        string MapRef(string rf) => Regex.Replace(rf, @"(\$?[A-Z]{1,3}\$?)(\d+)(?::(\$?[A-Z]{1,3}\$?)(\d+))?", m =>
        {
            if (!m.Groups[4].Success) { var p = fm.Point(int.Parse(m.Groups[2].Value)); return p is null ? m.Value : m.Groups[1].Value + p; }
            int? a = fm.Start(int.Parse(m.Groups[2].Value)), z = fm.End(int.Parse(m.Groups[4].Value));
            return a is null || z is null || a > z ? m.Value : m.Groups[1].Value + a + ":" + m.Groups[3].Value + z;
        });
        var head = Regex.Replace(book.Head, @"<dimension\s+ref=""([^""]*)""", m => "<dimension ref=\"" + Regex.Replace(m.Groups[1].Value, @"(\d+)$", lastNr.ToString(Inv)) + "\"");
        var tail = book.Tail;
        tail = new Regex(@"(<autoFilter\b[^>]*?\sref="")([^""]*)("")").Replace(tail, m => m.Groups[1].Value + MapRef(m.Groups[2].Value) + m.Groups[3].Value, 1);
        tail = new Regex(@"(<sortState\b[^>]*?\sref="")([^""]*)("")").Replace(tail, m => m.Groups[1].Value + MapRef(m.Groups[2].Value) + m.Groups[3].Value, 1);
        tail = Regex.Replace(tail, @"(<sortCondition\b[^>]*?\sref="")([^""]*)("")", m => m.Groups[1].Value + MapRef(m.Groups[2].Value) + m.Groups[3].Value);
        tail = Regex.Replace(tail, @"(<ignoredError\b[^>]*?\ssqref="")([^""]*)("")", m => m.Groups[1].Value + string.Join(" ", Regex.Split(m.Groups[2].Value, @"\s+").Select(MapRef)) + m.Groups[3].Value);
        book.Zip.Set(book.Path, head + string.Concat(outRows.Select(r => SerRow(r, r.Nr))) + "</sheetData>" + tail);

        var wb = Regex.Replace(book.WbXml, @"(<definedName\b[^>]*name=""_xlnm\._FilterDatabase""[^>]*>)([^<]*)(</definedName>)", m => m.Groups[1].Value + MapRef(m.Groups[2].Value) + m.Groups[3].Value);
        wb = Regex.IsMatch(wb, @"<calcPr\b")
            ? new Regex(@"<calcPr\b([^>]*?)/>").Replace(wb, m => m.Groups[1].Value.Contains("fullCalcOnLoad=") ? m.Value : "<calcPr" + m.Groups[1].Value + " fullCalcOnLoad=\"1\"/>", 1)
            : wb.Replace("</workbook>", "<calcPr fullCalcOnLoad=\"1\"/></workbook>");
        book.Zip.Set("xl/workbook.xml", wb);
        DropCalcChain(book.Zip);
        return (book.Zip.Save(), report);
    }

    private static string SerRow(XRow row, int nr)
    {
        var sb = new StringBuilder();
        foreach (var c in row.Cells)
        {
            var inner = (c.F is not null ? "<f" + c.F.Attrs + (c.F.Text is null ? "/>" : ">" + EscF(c.F.Text) + "</f>") : "") + (c.V is not null ? "<v>" + c.V + "</v>" : "") + (c.Is ?? "");
            sb.Append("<c r=\"").Append(c.Col).Append(nr).Append('"');
            if (c.S is not null) sb.Append(" s=\"").Append(c.S).Append('"');
            if (c.T is not null) sb.Append(" t=\"").Append(c.T).Append('"');
            sb.Append(c.Extra);
            sb.Append(inner.Length > 0 ? ">" + inner + "</c>" : "/>");
        }
        return sb.Length > 0 ? "<row r=\"" + nr + "\"" + row.Attrs + ">" + sb + "</row>" : "<row r=\"" + nr + "\"" + row.Attrs + "/>";
    }
    private static void DropCalcChain(Zip zip)
    {
        if (!zip.Has("xl/calcChain.xml")) return;
        zip.Remove("xl/calcChain.xml");
        if (zip.Has("[Content_Types].xml")) zip.Set("[Content_Types].xml", Regex.Replace(zip.Text("[Content_Types].xml"), @"<Override\b[^>]*?PartName=""/xl/calcChain\.xml""[^>]*?/>", ""));
        if (zip.Has("xl/_rels/workbook.xml.rels")) zip.Set("xl/_rels/workbook.xml.rels", Regex.Replace(zip.Text("xl/_rels/workbook.xml.rels"), @"<Relationship\b[^>]*?calcChain[^>]*?/>", ""));
    }

    // ------------------------------------------------------------------ thêm công thức tổng (cột Net theo mã + dòng tổng ngày)

    public sealed class FormulaReport { public int NetAdded, NetHas, TotAdded, TotHas, Blocks; public bool Nothing; }
    /// <summary>Chỉ ô đang là SỐ GÕ TAY và ĐÚNG bằng công thức mới được đổi sang công thức (Net = SL × TL; tổng ngày = SUM chi tiết). Giá trị hiển thị không đổi.</summary>
    public static (byte[]? Bytes, FormulaReport Report) AddTotalFormulas(byte[] bytes, string sheetName)
    {
        var book = OpenBook(bytes, sheetName); var info = FindCols(book); Classify(book, info);
        var rep = new FormulaReport();
        static bool Tol(double a, double b) => Math.Abs(a - b) <= 1e-6 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
        foreach (var b in book.Blocks)
        {
            var dets = b.Dets.Where(r => r.Kind == "det").ToList(); if (dets.Count == 0) continue; rep.Blocks++;
            if (info["net"] is not null && info["tl"] is not null)
                foreach (var row in dets)
                {
                    var nc = CellOf(row, info["net"]); var sc = CellOf(row, info["sl"]); var tc = CellOf(row, info["tl"]);
                    if (nc is null) continue;
                    if (nc.F is not null) { rep.NetHas++; continue; }
                    var nv = Num(nc.V); var s = Num(sc?.V); var t = Num(tc?.V);
                    if (nv is null || s is null || t is null || !Tol(nv.Value, s.Value * t.Value)) continue;
                    nc.F = new FCell { Attrs = "", Text = info["sl"] + row.R + "*" + info["tl"] + row.R }; nc.T = null; rep.NetAdded++;
                }
            if (b.Tot is null) continue;
            int first = dets[0].R, last = dets[^1].R;
            foreach (var col in new[] { info["sl"], info["net"] })
            {
                if (col is null) continue; var c = CellOf(b.Tot, col); if (c is null) continue;
                if (c.F is not null) { rep.TotHas++; continue; }
                var old = Num(c.V); var sum = dets.Sum(r => Num(CellOf(r, col)?.V) ?? 0);
                if (old is null || !Tol(old.Value, sum)) continue;
                c.F = new FCell { Attrs = "", Text = $"SUM({col}{first}:{col}{last})" }; c.V = FmtNum(sum); c.T = null; rep.TotAdded++;
            }
        }
        rep.Nothing = rep.NetAdded == 0 && rep.TotAdded == 0;
        if (rep.Nothing) return (null, rep);
        book.Zip.Set(book.Path, book.Head + string.Concat(book.Rows.Select(r => SerRow(r, r.R))) + "</sheetData>" + book.Tail);
        DropCalcChain(book.Zip);
        return (book.Zip.Save(), rep);
    }

    // ------------------------------------------------------------------ thêm cột SIZE vào file kho (để in chứng từ)

    public sealed class SizeReport
    {
        public string Sheet = "", Col = "", Mode = ""; public int Header, Rows, Filled, Empty, Width; public List<string> NoSize = new();
        public string? PrintArea, Filter; public bool FitAlready, FitWillSet, FitCanSet, Nothing;
    }
    /// <summary>Thêm cột "Size" SAU cột cuối đang dùng (không dịch ô / công thức nào); chỉ ghi chữ, chép kiểu ô từ ô Tên hàng / Mã hàng. Đã có cột Size → chỉ điền ô TRỐNG của dòng chi tiết.</summary>
    public static (byte[]? Bytes, SizeReport Report) AddSizeColumn(byte[] bytes, string sheetName, Func<string, string, string> sizeOf, bool fitWidth = false, bool dry = false)
    {
        var book = OpenBook(bytes, sheetName); var info = FindCols(book); Classify(book, info);
        var hdr = book.Rows.FirstOrDefault(r => r.R == info.Header);
        var lastIdx = 0; foreach (var r in book.Rows) foreach (var c in r.Cells) lastIdx = Math.Max(lastIdx, ColIdx(c.Col));
        var exists = info["size"] is not null; var col = exists ? info["size"]! : ColName(lastIdx + 1); var newIdx = ColIdx(col);
        var rep = new SizeReport { Sheet = book.SheetName, Header = info.Header, Col = col, Mode = exists ? "fill" : "add" };
        string? StyleOf(XRow row, params string?[] cols) { foreach (var k in cols) { var c = k is null ? null : CellOf(row, k); if (c?.S is not null) return c.S; } return null; }
        string NoSpans(string a) => Regex.Replace(a, @"\sspans=""[^""]*""", "");
        var maxLen = 4;
        if (!exists && hdr is not null)
        {
            hdr.Cells.Add(new XCell { Col = col, S = StyleOf(hdr, info["ten"], info["ma"]), T = "inlineStr", Is = "<is><t>Size</t></is>" }); hdr.Attrs = NoSpans(hdr.Attrs);
        }
        foreach (var row in book.Rows)
        {
            if (row.R <= info.Header) continue;
            var s0 = StyleOf(row, info["ten"], info["ma"], info["ngay"]);
            if (row.Kind == "det")
            {
                rep.Rows++;
                var ma = TextOf(book, CellOf(row, info["ma"])).Trim(); var ten = info["ten"] is null ? "" : TextOf(book, CellOf(row, info["ten"])).Trim();
                var z = ""; try { z = (sizeOf(ma, ten) ?? "").Trim(); } catch { z = ""; }
                var cur = exists ? CellOf(row, col) : null; var curTxt = cur is null ? "" : TextOf(book, cur).Trim();
                if (exists && curTxt.Length > 0) continue;
                if (z.Length == 0) { rep.Empty++; if (rep.NoSize.Count < 12 && ma.Length > 0) rep.NoSize.Add(ma); if (!exists && s0 is not null) row.Cells.Add(new XCell { Col = col, S = s0 }); continue; }
                var isx = "<is><t xml:space=\"preserve\">" + Xesc(z) + "</t></is>";
                if (cur is not null) { cur.T = "inlineStr"; cur.Is = isx; cur.V = null; cur.F = null; cur.S ??= s0; }
                else { row.Cells.Add(new XCell { Col = col, S = s0, T = "inlineStr", Is = isx }); row.Attrs = NoSpans(row.Attrs); }
                rep.Filled++; maxLen = Math.Max(maxLen, z.Length);
            }
            else if (!exists && row.Cells.Count > 0 && s0 is not null) { row.Cells.Add(new XCell { Col = col, S = s0 }); row.Attrs = NoSpans(row.Attrs); }
        }
        rep.Width = Math.Min(16, Math.Max(8, maxLen + 3));
        rep.Nothing = exists && rep.Filled == 0;
        string head = book.Head, tail = book.Tail;
        string? Extend(string rf)
        {
            if (Regex.IsMatch(rf, @"[,\s]")) return null;
            var m = Regex.Match(rf, @"^(.*:)(\$?)([A-Z]{1,3})(.*)$"); if (!m.Success) return null;
            var e = ColIdx(m.Groups[3].Value); if (e >= newIdx || e < lastIdx) return null;
            return m.Groups[1].Value + m.Groups[2].Value + col + m.Groups[4].Value;
        }
        var wb = book.WbXml;
        if (!exists)
        {
            head = Regex.Replace(head, @"<dimension\s+ref=""([^""]*)""", m => { var x = Regex.Match(m.Groups[1].Value, @"^(.*:)([A-Z]{1,3})(\d+)$"); return x.Success && ColIdx(x.Groups[2].Value) < newIdx ? "<dimension ref=\"" + x.Groups[1].Value + col + x.Groups[3].Value + "\"" : m.Value; });
            string ColTag(int mn, int mx, string extra) => $"<col min=\"{mn}\" max=\"{mx}\"{extra}/>";
            if (head.Contains("<cols>"))
            {
                var done = false;
                head = Regex.Replace(head, @"<col\b([^>]*?)/>", m =>
                {
                    var a = m.Groups[1].Value; var mnm = Regex.Match(a, @"\smin=""(\d+)"""); var mxm = Regex.Match(a, @"\smax=""(\d+)""");
                    if (done || !mnm.Success || !mxm.Success) return m.Value; int mn = int.Parse(mnm.Groups[1].Value), mx = int.Parse(mxm.Groups[1].Value);
                    if (newIdx < mn || newIdx > mx) return m.Value; done = true;
                    var rest = Regex.Replace(Regex.Replace(Regex.Replace(Regex.Replace(a, @"\smin=""\d+""", ""), @"\smax=""\d+""", ""), @"\swidth=""[^""]*""", ""), @"\scustomWidth=""[^""]*""", "");
                    var plain = Regex.Replace(Regex.Replace(a, @"\smin=""\d+""", ""), @"\smax=""\d+""", "");
                    return (mn < newIdx ? ColTag(mn, newIdx - 1, plain) : "") + ColTag(newIdx, newIdx, rest + $" width=\"{rep.Width}\" customWidth=\"1\"") + (mx > newIdx ? ColTag(newIdx + 1, mx, plain) : "");
                });
                if (!done) head = head.Replace("</cols>", ColTag(newIdx, newIdx, $" width=\"{rep.Width}\" customWidth=\"1\"") + "</cols>");
            }
            else head = Regex.Replace(head, @"<sheetData>$", "<cols>" + ColTag(newIdx, newIdx, $" width=\"{rep.Width}\" customWidth=\"1\"") + "</cols><sheetData>");
            tail = Regex.Replace(tail, @"(<autoFilter\b[^>]*?\sref="")([^""]*)("")", m => { var n = Extend(m.Groups[2].Value); if (n is not null) rep.Filter = m.Groups[2].Value + " → " + n; return n is null ? m.Value : m.Groups[1].Value + n + m.Groups[3].Value; });
            var names = new List<string>(); foreach (Match m in Regex.Matches(wb, @"<sheet\b([^>]*?)/>")) { var n = Regex.Match(m.Groups[1].Value, @"\sname=""([^""]*)"""); if (n.Success) names.Add(Xunesc(n.Groups[1].Value)); }
            var sidx = names.IndexOf(book.SheetName);
            wb = Regex.Replace(wb, @"<definedName\b([^>]*)>([^<]*)</definedName>", m =>
            {
                var a = m.Groups[1].Value; if (sidx < 0 || !Regex.IsMatch(a, $@"\slocalSheetId=""{sidx}""")) return m.Value;
                var pa = a.Contains("name=\"_xlnm.Print_Area\""); var fd = a.Contains("name=\"_xlnm._FilterDatabase\""); if (!pa && !fd) return m.Value;
                var rf = m.Groups[2].Value; var bang = rf.LastIndexOf('!'); var n = Extend(rf[(bang + 1)..]); if (n is null) return m.Value;
                if (pa) rep.PrintArea = rf[(bang + 1)..] + " → " + n;
                return "<definedName" + a + ">" + rf[..(bang + 1)] + n + "</definedName>";
            });
        }
        var fitOn = Regex.IsMatch(head, @"<pageSetUpPr\b[^>]*fitToPage=""(1|true)""");
        var ps = Regex.Match(tail, @"<pageSetup\b[^>]*>"); var fw = ps.Success ? Regex.Match(ps.Value, @"\sfitToWidth=""(\d+)""") : null;
        rep.FitAlready = fitOn && (fw is null || !fw.Success || fw.Groups[1].Value == "1");
        rep.FitCanSet = ps.Success || Regex.IsMatch(tail, @"<pageMargins\b[^>]*/>");
        if (fitWidth && !rep.FitAlready && rep.FitCanSet)
        {
            rep.FitWillSet = true;
            if (Regex.IsMatch(head, @"<pageSetUpPr\b")) head = Regex.Replace(head, @"<pageSetUpPr\b[^>]*/>", "<pageSetUpPr fitToPage=\"1\"/>");
            else if (Regex.IsMatch(head, @"<sheetPr\b[^>]*/>")) head = Regex.Replace(head, @"<sheetPr\b([^>]*)/>", "<sheetPr${1}><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
            else if (head.Contains("</sheetPr>")) head = head.Replace("</sheetPr>", "<pageSetUpPr fitToPage=\"1\"/></sheetPr>");
            else head = Regex.Replace(head, @"(<worksheet\b[^>]*>)", "${1}<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
            if (ps.Success) tail = Regex.Replace(tail, @"<pageSetup\b([^>]*?)(/?)>", m => "<pageSetup" + Regex.Replace(Regex.Replace(m.Groups[1].Value, @"\sfitToWidth=""[^""]*""", ""), @"\sfitToHeight=""[^""]*""", "") + " fitToWidth=\"1\" fitToHeight=\"0\"" + m.Groups[2].Value + ">");
            else tail = Regex.Replace(tail, @"(<pageMargins\b[^>]*/>)", "${1}<pageSetup fitToWidth=\"1\" fitToHeight=\"0\"/>");
        }
        if (dry || rep.Nothing) return (null, rep);
        book.Zip.Set(book.Path, head + string.Concat(book.Rows.Select(r => SerRow(r, r.R))) + "</sheetData>" + tail);
        if (wb != book.WbXml) book.Zip.Set("xl/workbook.xml", wb);
        return (book.Zip.Save(), rep);
    }
}

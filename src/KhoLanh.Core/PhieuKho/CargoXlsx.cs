using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.PhieuKho;

/// <summary>Ô công thức kèm giá trị đã tính (mọi trình xem file đều thấy số ngay; Excel tính lại khi mở).</summary>
public sealed record Fv(string F, double? V);
public sealed record RichText(string Xml);
/// <summary>Ghi giá trị nhưng lấy KIỂU Ô của ô khác trong dòng mẫu.</summary>
public sealed record StyleFrom(string FromCol, object? V);

public sealed class CargoItem
{
    public bool IsTitle;
    public string Text = "";
    public object? A, B, C, V, W, X, Y, Z, Aa, Ab;
    public double[] Dai = Array.Empty<double>();
    public double Total;
}

public sealed class CargoXlsxData
{
    public string Kho = "", Ngay = "", NgayText = "", SoPhieu = "", NguoiNhan = "", ContXe = "", Seal = "", BangChu = "", Khach = "THUẬN PHƯỚC", LyDo = "XUẤT HÀNG VỀ THUẬN PHƯỚC";
    public string ContSo = "", KieuXuat = "";
    public CargoCore.PhieuData? Phieu;
    public CargoCore.GkData? Gk;
    /// <summary>Bảng Cargo: Thuận Phước 2 bảng × 20 dãy; Gửi kho 3 bảng × 15 dãy.</summary>
    public List<List<CargoItem>> Tables = new();
}

/// <summary>
/// GHI FILE EXCEL "CARGO LIST + PHIẾU XUẤT KHO" (port từ pnx-cargo-xlsx.js) từ mẫu thật: giữ NGUYÊN định dạng (kiểu ô, ô gộp, logo, hộp chữ "Ngày … tháng … năm", vùng in),
/// nhưng DỰNG LẠI thân của 2 sheet bằng cách NHÂN dòng mẫu (tiêu đề khối / dòng hàng / dòng TOTAL / dòng đệm) đúng số dòng cần — không có dòng ẩn.
/// Các ô TỔNG là CÔNG THỨC (SUM…) kèm giá trị đã tính; số liệu nhập (kiện từng dãy…) là số. Vị trí dòng mẫu: xem chú thích từng hàm; đổi mẫu phải chỉnh lại các số dòng.
/// </summary>
public static class CargoXlsx
{
    private const int NDay = CargoCore.NDay, Half = CargoCore.Half, SH = Half - 17;   // SH = số cột chèn thêm so với mẫu (mẫu có 17 dãy D…T; Tổng ở U)
    private const int HGK = 15;                                                       // số dãy mỗi bảng của Cargo gửi kho (F…T)
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static int ColIdx(string c) => c.Aggregate(0, (n, ch) => n * 26 + ch - 64);
    public static string ColName(int n) { var s = ""; while (n > 0) { var m = (n - 1) % 26; s = (char)(65 + m) + s; n = (n - 1) / 26; } return s; }
    private static double R3(double x) => Math.Round(x * 1000) / 1000;
    private static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static Fv F(string f, double v) => new(f, v);
    private static string SumF(string col, int a, int b) => $"SUM({col}{a}:{col}{b})";

    private sealed class CellX { public string Col = "", S = "", Raw = ""; }
    private sealed class RowX { public int N; public string Attrs = ""; public List<CellX> Cells = new(); }
    private sealed class Sheet { public string Head = "", Tail = ""; public Dictionary<int, RowX> Rows = new(); }

    private static Sheet ParseSheet(string xml)
    {
        var a = xml.IndexOf("<sheetData>", StringComparison.Ordinal); var b = xml.IndexOf("</sheetData>", StringComparison.Ordinal);
        if (a < 0 || b < 0) throw new InvalidOperationException("Mẫu hỏng: sheet không có <sheetData>");
        var P = new Sheet { Head = xml[..a], Tail = xml[(b + 12)..] };
        var body = xml.Substring(a + 11, b - a - 11);
        foreach (Match m in Regex.Matches(body, @"<row\b([^>]*?)(?:/>|>([\s\S]*?)</row>)"))
        {
            var attrs = m.Groups[1].Value; var n = int.Parse(Regex.Match(attrs, "\\sr=\"(\\d+)\"").Groups[1].Value);
            var row = new RowX { N = n, Attrs = attrs };
            foreach (Match c in Regex.Matches(m.Groups[2].Value, @"<c\b([^>]*?)(?:/>|>([\s\S]*?)</c>)"))
            {
                var rf = Regex.Match(c.Groups[1].Value, "\\sr=\"([A-Z]+)\\d+\""); if (!rf.Success) continue;
                var s = Regex.Match(c.Groups[1].Value, "\\ss=\"(\\d+)\"");
                row.Cells.Add(new CellX { Col = rf.Groups[1].Value, S = s.Success ? s.Groups[1].Value : "", Raw = c.Value });
            }
            P.Rows[n] = row;
        }
        return P;
    }
    private static string ShCol(string c) { var i = ColIdx(c); return i >= 21 ? ColName(i + SH) : c; }
    /// <summary>Mẫu Cargo có 17 dãy (D…T) + Tổng (U) + cột phụ V…AB. Muốn Half dãy: nhân cột T thêm SH lần và đẩy U… sang phải SH cột.</summary>
    private static Sheet WidenCargo(Sheet P)
    {
        if (SH == 0) return P;
        foreach (var row in P.Rows.Values)
        {
            var cells = new List<CellX>();
            foreach (var c in row.Cells)
            {
                if (ColIdx(c.Col) >= 21)
                {
                    var nc = ShCol(c.Col);
                    cells.Add(new CellX { Col = nc, S = c.S, Raw = Regex.Replace(c.Raw, "\\sr=\"[A-Z]+(\\d+)\"", " r=\"" + nc + "$1\"", RegexOptions.None, TimeSpan.FromSeconds(2)) });
                    continue;
                }
                cells.Add(c);
                if (c.Col == "T") for (var k = 1; k <= SH; k++) { var nc = ColName(20 + k); cells.Add(new CellX { Col = nc, S = c.S, Raw = c.S.Length > 0 ? $"<c r=\"{nc}{row.N}\" s=\"{c.S}\"/>" : "" }); }
            }
            row.Cells = cells;
        }
        return P;
    }

    private static string CellXml(string col, int r, string s, object? v)
    {
        var sa = s.Length > 0 ? $" s=\"{s}\"" : "";
        switch (v)
        {
            case null: return $"<c r=\"{col}{r}\"{sa}/>";
            case string str when str.Length == 0: return $"<c r=\"{col}{r}\"{sa}/>";
            case Fv f: return $"<c r=\"{col}{r}\"{sa}><f>{Esc(f.F)}</f>{(f.V is { } fv && double.IsFinite(fv) ? "<v>" + D(fv) + "</v>" : "")}</c>";
            case RichText rt: return $"<c r=\"{col}{r}\"{sa} t=\"inlineStr\"><is>{rt.Xml}</is></c>";
            case double d: return double.IsFinite(d) ? $"<c r=\"{col}{r}\"{sa}><v>{D(d)}</v></c>" : $"<c r=\"{col}{r}\"{sa}/>";
            case int i: return $"<c r=\"{col}{r}\"{sa}><v>{i}</v></c>";
            case long l: return $"<c r=\"{col}{r}\"{sa}><v>{l}</v></c>";
            default: return $"<c r=\"{col}{r}\"{sa} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "")}</t></is></c>";
        }
    }

    /// <summary>proto = dòng mẫu; r = số dòng mới; vals ghi đè; keep = true: ô KHÔNG có trong vals giữ nguyên nội dung mẫu (chữ tĩnh); false: để trống (chỉ giữ kiểu ô).</summary>
    private static string RowXml(RowX proto, int r, Dictionary<string, object?>? vals, bool keep)
    {
        vals ??= new();
        var attrs = Regex.Replace(Regex.Replace(Regex.Replace(proto.Attrs, "\\sr=\"\\d+\"", " r=\"" + r + "\""), "\\shidden=\"1\"", ""), "\\sspans=\"[^\"]*\"", "");
        var list = proto.Cells.ToList();
        foreach (var k in vals.Keys) if (!list.Any(x => x.Col == k)) list.Add(new CellX { Col = k });
        list = list.OrderBy(x => ColIdx(x.Col)).ToList();
        var outS = new StringBuilder(); var seen = new HashSet<string>();
        foreach (var c in list)
        {
            if (!seen.Add(c.Col)) continue;
            if (vals.TryGetValue(c.Col, out var v))
            {
                var st = c.S;
                if (v is StyleFrom sf) { var f = proto.Cells.FirstOrDefault(x => x.Col == sf.FromCol); if (f is not null) st = f.S; v = sf.V; }
                outS.Append(CellXml(c.Col, r, st, v));
            }
            else if (keep && c.Raw.Length > 0) outS.Append(Regex.Replace(c.Raw, "\\sr=\"([A-Z]+)\\d+\"", " r=\"${1}" + r + "\"", RegexOptions.None, TimeSpan.FromSeconds(2)));
            else outS.Append(CellXml(c.Col, r, c.S, null));
        }
        return "<row" + attrs + ">" + outS + "</row>";
    }

    private static RowX Need(Sheet P, int n, string what) => P.Rows.TryGetValue(n, out var r) ? r : throw new InvalidOperationException($"Mẫu thiếu dòng {n} ({what}) — mẫu đã đổi? Dựng lại mẫu và chỉnh CargoXlsx.cs");

    private static string Assemble(Sheet P, List<string> rows, List<string> merges, string lastCol, int last, string? cols = null, bool? tabSelected = null)
    {
        var head = Regex.Replace(P.Head, @"<dimension\b[^>]*/>", $"<dimension ref=\"A1:{lastCol}{last}\"/>");
        if (cols is not null) head = Regex.Replace(head, @"<cols>[\s\S]*?</cols>", cols.Replace("$", "$$"));
        if (tabSelected is { } ts) { head = head.Replace(" tabSelected=\"1\"", ""); if (ts) head = Regex.Replace(head, @"<sheetView\b", "<sheetView tabSelected=\"1\""); }
        var mc = merges.Count > 0 ? $"<mergeCells count=\"{merges.Count}\">" + string.Concat(merges.Select(m => $"<mergeCell ref=\"{m}\"/>")) + "</mergeCells>" : "";
        var tail = Regex.Replace(P.Tail, @"<mergeCells\b[\s\S]*?</mergeCells>", mc.Replace("$", "$$"));
        return head + "<sheetData>" + string.Concat(rows) + "</sheetData>" + tail;
    }
    private static string Rich(string text) =>
        "<r><t xml:space=\"preserve\">* </t></r><r><rPr><b/><u/><sz val=\"18\"/><rFont val=\"Cambria\"/><family val=\"1\"/><scheme val=\"major\"/></rPr><t>BẰNG CHỮ</t></r><r><rPr><b/><sz val=\"18\"/><rFont val=\"Cambria\"/><family val=\"1\"/><scheme val=\"major\"/></rPr><t xml:space=\"preserve\">: " + Esc(text) + "</t></r>";
    private static string RichBc(string text) =>
        "<r><t xml:space=\"preserve\">* </t></r><r><rPr><b/><u/><sz val=\"20\"/><rFont val=\"Times New Roman\"/><family val=\"1\"/></rPr><t>BẰNG CHỮ</t></r><r><rPr><b/><sz val=\"20\"/><rFont val=\"Times New Roman\"/><family val=\"1\"/></rPr><t xml:space=\"preserve\">: " + Esc(text) + "</t></r>";

    // ================================================================== Thuận Phước / Gia công: sheet "Cargo"
    // Sheet "Cargo" (mẫu): 1–7 đầu trang + tiêu đề bảng 1 · 8 tiêu đề khối · 9 dòng hàng · 32 TỔNG bảng 1 · 33 đệm · 34 tiêu đề bảng 2 · 35 tiêu đề khối (bảng 2) · 36 dòng hàng (bảng 2) · 44 TỔNG bảng 2 · 45 bằng chữ · 46 chữ ký + TỔNG · 47–48 đệm

    private static (string Xml, int Last) BuildCargo(string xml, CargoXlsxData d)
    {
        var P = WidenCargo(ParseSheet(xml)); var outR = new List<string>();
        var merges = new List<string> { "A1:" + ColName(21 + SH) + "1", "A2:" + ColName(21 + SH) + "2", "C3:G3", "C4:F4", "C5:F5" };
        var cTot = ColName(21 + SH); var cLast = ColName(3 + Half); var r = 0;
        void Put(int n, Dictionary<string, object?>? vals, bool keep = true) { r++; outR.Add(RowXml(Need(P, n, "cargo"), r, vals, keep)); }
        for (var n = 1; n <= 6; n++)
        {
            var v = new Dictionary<string, object?>();
            if (n == 3) v["C"] = "Kho xuất: " + d.Kho;
            if (n == 4) { v["A"] = "Cont/Xe : " + d.ContXe; v["K"] = ""; }
            if (n == 5) { v["A"] = "Seal : " + d.Seal; v["C"] = "Ngày xuất: " + d.NgayText; v["K"] = ""; }
            Put(n, v);
        }
        var tot = new List<double>(); var totRow = new List<int>();
        for (var t = 0; t < 2; t++)
        {
            var off = t * Half; var items = t < d.Tables.Count ? d.Tables[t] : new List<CargoItem>();
            if (t == 1 && items.Count == 0) { tot.Add(0); continue; }   // xuất tới dãy ≤ 20 thì BỎ HẲN bảng dãy 21–40
            var hv = new Dictionary<string, object?>(); for (var i = 0; i < Half; i++) hv[ColName(4 + i)] = (double)(off + i + 1);
            Put(t == 0 ? 7 : 34, hv);
            var sums = new double[Half]; double sumU = 0; int rA = 0, rB = 0;
            foreach (var it in items)
            {
                if (it.IsTitle) { Put(t == 0 ? 8 : 35, new() { ["A"] = it.Text }, false); if (rA == 0) rA = r; rB = r; continue; }
                var v = new Dictionary<string, object?> { ["A"] = it.A, ["B"] = it.B, ["C"] = it.C };
                var ex = new[] { it.V, it.W, it.X, it.Y, it.Z, it.Aa, it.Ab };
                for (var k = 0; k < ex.Length; k++) v[ColName(22 + SH + k)] = ex[k];
                for (var i = 0; i < Half; i++) { var q = i < it.Dai.Length ? it.Dai[i] : 0; if (q != 0) { v[ColName(4 + i)] = q; sums[i] += q; } }
                v[cTot] = F($"SUM(D{r + 1}:{cLast}{r + 1})", it.Total);
                sumU += it.Total; Put(t == 0 ? 9 : 36, v, false); if (rA == 0) rA = r; rB = r;
            }
            var vt = new Dictionary<string, object?> { [cTot] = sumU };
            for (var i = 0; i < Half; i++) vt[ColName(4 + i)] = sums[i];
            if (rA > 0) { vt[cTot] = F(SumF(cTot, rA, rB), sumU); for (var i = 0; i < Half; i++) vt[ColName(4 + i)] = F(SumF(ColName(4 + i), rA, rB), sums[i]); }
            tot.Add(sumU); Put(t == 0 ? 32 : 44, vt); totRow.Add(r);
            if (t == 0 && d.Tables.Count > 1 && d.Tables[1].Count > 0) Put(33, null);   // dòng đệm giữa 2 bảng chỉ khi có bảng 2
        }
        r++; outR.Add(RowXml(Need(P, 45, "bằng chữ"), r, new() { ["A"] = "Bằng chữ: " + d.BangChu }, true));
        r++; outR.Add(RowXml(Need(P, 46, "chữ ký"), r, new() { [cTot] = F(totRow.Count > 0 ? string.Join("+", totRow.Select(x => cTot + x)) : "0", tot.Sum()) }, true));
        Put(47, null); Put(48, null);
        double[][] wd =
        {
            new double[] { 4, 3 + Half, 7.140625 }, new double[] { 4 + Half, 4 + Half, 9.5703125 }, new double[] { 5 + Half, 5 + Half, 13.7109375 }, new double[] { 6 + Half, 6 + Half, 5.7109375 },
            new double[] { 7 + Half, 7 + Half, 23.42578125 }, new double[] { 8 + Half, 8 + Half, 27.140625 }, new double[] { 9 + Half, 9 + Half, 15.42578125 }, new double[] { 10 + Half, 10 + Half, 51.85546875 }, new double[] { 11 + Half, 11 + Half, 48.7109375 },
        };
        var inv = CultureInfo.InvariantCulture;
        var cols = "<cols><col min=\"1\" max=\"1\" width=\"50.7109375\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"15.42578125\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"12.28515625\" customWidth=\"1\"/>"
            + string.Concat(wd.Select(x => $"<col min=\"{x[0].ToString(inv)}\" max=\"{x[1].ToString(inv)}\" width=\"{x[2].ToString(inv)}\" customWidth=\"1\"/>")) + "</cols>";
        return (Assemble(P, outR, merges, ColName(11 + Half), r, cols, true), r);
    }

    // ================================================================== Thuận Phước / Gia công: phiếu (sheet "01" → "Phiếu xuất kho")
    // Sheet "01": 1–12 đầu phiếu + hàng tiêu đề cột · 13 tiêu đề khối · 14 dòng hàng · 15 dòng hàng cuối (viền đậm) · 16 TOTAL · 17 đệm · 33 đệm · 34 "Đã kiểm tra hình ảnh" · 35 Tổng · 36 bằng chữ · 37–39 nhiệt độ / chữ ký

    private sealed record Layout(int RsIdx, Func<int, double> WidthOf);

    private static (string Xml, int Last, Layout Layout) BuildPhieu(string xml, CargoXlsxData d)
    {
        var P = ParseSheet(xml); var outR = new List<string>(); var merges = new List<string> { "A1:AD1", "A2:AD2", "A3:AD3", "A4:AD4", "A5:AD5", "A6:AD6" };
        var ph = d.Phieu!; var M = CargoCore.Markets; var r = 0;
        var colsXml = Regex.Match(P.Head, @"<cols>([\s\S]*?)</cols>"); var defs = new List<(int Mn, int Mx, string A)>();
        foreach (Match m in Regex.Matches(colsXml.Success ? colsXml.Groups[1].Value : "", @"<col\b([^>]*?)/>"))
        {
            var a = m.Groups[1].Value;
            defs.Add((int.Parse(Regex.Match(a, "\\smin=\"(\\d+)\"").Groups[1].Value), int.Parse(Regex.Match(a, "\\smax=\"(\\d+)\"").Groups[1].Value), Regex.Replace(a, "\\s(min|max|hidden)=\"[^\"]*\"", "")));
        }
        double BaseW(int c) { var df = defs.FirstOrDefault(x => c >= x.Mn && c <= x.Mx); var w = df.A is null ? Match.Empty : Regex.Match(df.A, "\\swidth=\"([\\d.]+)\""); return w.Success ? double.Parse(w.Groups[1].Value, CultureInfo.InvariantCulture) : 9.14; }
        var show = new HashSet<string>(ph.Cols);
        var visIdx = M.Select((m, i) => show.Contains(m) ? 5 + i : 0).Where(x => x > 0).ToList(); var abVis = ph.Other > 0;
        var lastLeft = visIdx.Count > 0 ? visIdx.Max() : 4; var rs = abVis ? "AB" : "AC"; var rsIdx = abVis ? 28 : 29;
        var leftChars = BaseW(1) + BaseW(2) + BaseW(3) + BaseW(4); foreach (var c in visIdx) leftChars += BaseW(c);
        var rightChars = (abVis ? BaseW(28) : 0) + BaseW(29) + BaseW(30); const double UNIT = 1.7;
        var leftTxt = new[] { "Kho xuất: " + d.Kho, "Người nhận hàng : " + d.NguoiNhan, "Lý do xuất : " + (d.LyDo.Length > 0 ? d.LyDo : "XUẤT HÀNG VỀ THUẬN PHƯỚC") };
        var rightTxt = new[] { "Số : " + d.SoPhieu, "Cont/Xe : " + d.ContXe, "Seal : " + d.Seal, "Kiểu xuất : Xuất nội địa" };
        var leftNeed = Math.Ceiling(leftTxt.Max(t => t.Length) * UNIT + 2); var rightNeed = Math.Max(52, Math.Ceiling(rightTxt.Max(t => t.Length) * UNIT + 2));
        var extraD = Math.Max(0, leftNeed - leftChars); var extraAD = Math.Max(0, rightNeed - rightChars);
        const double MIN_TOTAL = 135;   // phiếu quá hẹp thì hộp chữ ngày đè lên dòng "( An An + Long An )" → nới cột Packing cho đủ bề rộng tối thiểu
        extraD += Math.Max(0, MIN_TOTAL - (leftChars + extraD + rightChars + extraAD));
        double WidthOf(int c) => BaseW(c) + (c == 4 ? extraD : 0) + (c == 30 ? extraAD : 0);
        var lastLeftName = ColName(lastLeft);
        foreach (var n in new[] { "7", "8", "9" }) merges.Add("A" + n + ":" + lastLeftName + n);
        foreach (var n in new[] { "7", "8", "9", "10" }) merges.Add(rs + n + ":AD" + n);
        int Put(int n, Dictionary<string, object?>? vals, bool keep = true) { r++; outR.Add(RowXml(Need(P, n, "phiếu"), r, vals, keep)); return r; }
        for (var n = 1; n <= 12; n++)
        {
            var v = new Dictionary<string, object?>();
            void Right(string t) { v["AA"] = ""; v[rs] = new StyleFrom("AA", t); }
            if (n == 6) v["A"] = "( " + d.Kho + " )";
            if (n == 7) { v["A"] = "Kho xuất: " + d.Kho; Right("Số : " + d.SoPhieu); }
            if (n == 8) { v["A"] = "Người nhận hàng : " + d.NguoiNhan; Right("Cont/Xe : " + d.ContXe); }
            if (n == 9) Right("Seal : " + d.Seal);
            if (n == 10) Right("Kiểu xuất : Xuất nội địa");
            if (n == 11) { for (var i = 0; i < M.Length; i++) v[ColName(5 + i)] = ph.Cols.Contains(M[i]) ? M[i] : ""; v["AB"] = ph.Other > 0 ? "." : ""; }
            Put(n, v);
        }
        var sums = M.ToDictionary(m => m, _ => 0.0); double sOther = 0, sKien = 0, sKg = 0; var rngs = new List<(int A, int B)>(); var totRows = new List<int>();
        for (var bi = 0; bi < ph.Blocks.Count; bi++)
        {
            var b = ph.Blocks[bi];
            var rt = Put(13, new() { ["A"] = (bi + 1).ToString(), ["B"] = b.Title }, false); merges.Add($"B{rt}:AB{rt}"); int rA = 0, rB = 0;
            for (var ri = 0; ri < b.Rows.Count; ri++)
            {
                var row = b.Rows[ri];
                var v = new Dictionary<string, object?> { ["C"] = row.Size, ["D"] = row.Pack, ["AC"] = row.Kien, ["AD"] = R3(row.Kg) };
                for (var i = 0; i < M.Length; i++) if (row.Cols.TryGetValue(M[i], out var q) && q != 0) { v[ColName(5 + i)] = q; sums[M[i]] += q; }
                if (row.Other > 0) { v["AB"] = row.Other; sOther += row.Other; }
                var nr = r + 1; var cs = row.Other + row.Cols.Values.Sum();
                if (cs == row.Kien) v["AC"] = F($"SUM(E{nr}:AB{nr})", row.Kien);
                if (row.PerCarton > 0) v["AD"] = F($"ROUND(AC{nr}*{row.PerCarton.ToString(CultureInfo.InvariantCulture)},3)", R3(row.Kien * row.PerCarton));
                Put(ri == b.Rows.Count - 1 ? 15 : 14, v, false); if (rA == 0) rA = r; rB = r;
            }
            if (rA > 0) rngs.Add((rA, rB));
            Put(16, rA > 0 ? new() { ["AC"] = F(SumF("AC", rA, rB), b.Kien), ["AD"] = F(SumF("AD", rA, rB), R3(b.Kg)) } : new() { ["AC"] = b.Kien, ["AD"] = R3(b.Kg) }); totRows.Add(rA > 0 ? r : 0); Put(17, null);
            sKien += b.Kien; sKg += b.Kg;
        }
        Put(33, null);
        r++; var rKt = r; outR.Add(RowXml(Need(P, 34, "đã kiểm tra"), r, new() { ["B"] = $"Đã kiểm tra hình ảnh: {ph.SoDay} dãy, có hình ảnh bên ngoài" }, true)); merges.Add($"B{rKt}:AD{rKt}");
        var vt = new Dictionary<string, object?> { ["AC"] = sKien, ["AD"] = R3(sKg), ["AB"] = sOther };
        for (var i = 0; i < M.Length; i++) vt[ColName(5 + i)] = sums[M[i]];
        if (rngs.Count > 0)
        {
            var tr = totRows.Where(x => x != 0).ToList(); string Lst(string c) => string.Join(",", rngs.Select(g => $"{c}{g.A}:{c}{g.B}"));
            vt["AC"] = F("SUM(" + string.Join(",", tr.Select(x => "AC" + x)) + ")", sKien); vt["AD"] = F("SUM(" + string.Join(",", tr.Select(x => "AD" + x)) + ")", R3(sKg)); vt["AB"] = F("SUM(" + Lst("AB") + ")", sOther);
            for (var i = 0; i < M.Length; i++) vt[ColName(5 + i)] = F("SUM(" + Lst(ColName(5 + i)) + ")", sums[M[i]]);
        }
        Put(35, vt);
        r++; var rBc = r; outR.Add(RowXml(Need(P, 36, "bằng chữ"), r, new() { ["B"] = new RichText(Rich(d.BangChu)) }, true)); merges.Add($"B{rBc}:AD{rBc}");
        r++; outR.Add(RowXml(Need(P, 37, "nhiệt độ 1"), r, null, true)); merges.Add($"A{r}:AD{r}");
        r++; outR.Add(RowXml(Need(P, 38, "nhiệt độ 2"), r, null, true)); merges.Add($"A{r}:AD{r}");
        {   // dòng chữ ký: chia các cột CÒN HIỆN thành 5 nhóm liền nhau (theo bề rộng), mỗi nhóm gộp ô + 1 nhãn căn giữa
            var vis = new List<int> { 1, 2, 3, 4 }; vis.AddRange(visIdx); if (abVis) vis.Add(28); vis.Add(29); vis.Add(30);
            var total = vis.Sum(WidthOf); string[] labels = { "Thủ kho xuất", "Người nhận", "P. Phụ trách", "QL. Kho", "Giám Đốc" }; var groups = new List<List<int>>();
            double acc = 0; var k = 0; var cur = new List<int>();
            for (var i = 0; i < vis.Count; i++)
            {
                var c = vis[i]; var w = WidthOf(c); var mid = acc + w / 2; var left = vis.Count - i; acc += w;
                if (k < 4 && cur.Count > 0 && (mid > total * (k + 1) / 5 || left <= 4 - k)) { groups.Add(cur); cur = new(); k++; }
                cur.Add(c);
            }
            groups.Add(cur); while (groups.Count < labels.Length) groups.Add(new List<int> { vis[^1] });
            var sg = new Dictionary<string, object?> { ["A"] = "" };
            for (var gi = 0; gi < groups.Count && gi < labels.Length; gi++) sg[ColName(groups[gi][0])] = new StyleFrom("A", labels[gi]);
            r++; outR.Add(RowXml(Need(P, 39, "chữ ký"), r, sg, true));
            foreach (var g in groups) if (g.Count > 1) merges.Add($"{ColName(g[0])}{r}:{ColName(g[^1])}{r}");
        }
        var parts = new List<string>();
        foreach (var dd in defs)
        {
            if (dd.Mn > 33) { parts.Add($"<col min=\"{dd.Mn}\" max=\"{dd.Mx}\"{dd.A}/>"); continue; }
            for (var c = dd.Mn; c <= Math.Min(dd.Mx, 33); c++)
            {
                var hid = false; if (c >= 5 && c <= 27) hid = !show.Contains(M[c - 5]); else if (c == 28) hid = !abVis;
                parts.Add($"<col min=\"{c}\" max=\"{c}\"{Regex.Replace(dd.A, "\\swidth=\"[\\d.]+\"", " width=\"" + WidthOf(c).ToString(CultureInfo.InvariantCulture) + "\"")}{(hid ? " hidden=\"1\"" : "")}/>");
            }
            if (dd.Mx > 33) parts.Add($"<col min=\"34\" max=\"{dd.Mx}\"{dd.A}/>");
        }
        return (Assemble(P, outR, merges, "AD", r, "<cols>" + string.Concat(parts) + "</cols>", false), r, new Layout(rsIdx, WidthOf));
    }

    /// <summary>Hộp chữ trong mẫu neo theo 2 ô (AA…AD) nên co lại khi cột bị ẩn → chữ "năm 2026" bị cắt. Đổi sang neo 1 ô + kích thước CỐ ĐỊNH, đặt sát phải (AD) theo bề rộng cột thật.</summary>
    private static string AnchorBoxes(string dr, Layout L)
    {
        long Emu(double w) => (long)Math.Round((w * 7 + 5) * 9525);
        var cols = new List<(int C, long W)>(); for (var c = L.RsIdx; c <= 30; c++) cols.Add((c, Emu(L.WidthOf(c)))); var total = cols.Sum(x => x.W);
        return Regex.Replace(dr, @"<xdr:twoCellAnchor\b[^>]*>([\s\S]*?)</xdr:twoCellAnchor>", all =>
        {
            var body = all.Groups[1].Value; if (!body.Contains("Text Box")) return all.Value;
            var ext = Regex.Match(body, "<a:ext cx=\"(\\d+)\" cy=\"(\\d+)\"");
            var fr = Regex.Match(body, @"<xdr:from>[\s\S]*?<xdr:col>(\d+)</xdr:col>[\s\S]*?<xdr:row>(\d+)</xdr:row>[\s\S]*?<xdr:rowOff>(\d+)</xdr:rowOff>[\s\S]*?</xdr:from>");
            if (!ext.Success || !fr.Success) return all.Value;
            long cx = long.Parse(ext.Groups[1].Value), cy = long.Parse(ext.Groups[2].Value); var margin = int.Parse(fr.Groups[1].Value) >= 27 ? 175000 : 545000;
            double x = Math.Max(0, total - cx - margin); var col = L.RsIdx;
            foreach (var (c, w) in cols) { if (x < w) { col = c; break; } x -= w; col = c; }
            var from = $"<xdr:from><xdr:col>{col - 1}</xdr:col><xdr:colOff>{Math.Round(x)}</xdr:colOff><xdr:row>{fr.Groups[2].Value}</xdr:row><xdr:rowOff>{fr.Groups[3].Value}</xdr:rowOff></xdr:from>";
            var rest = Regex.Replace(Regex.Replace(body, @"<xdr:from>[\s\S]*?</xdr:from>", ""), @"<xdr:to>[\s\S]*?</xdr:to>", "");
            return $"<xdr:oneCellAnchor>{from}<xdr:ext cx=\"{cx}\" cy=\"{cy}\"/>{rest}</xdr:oneCellAnchor>";
        });
    }

    // ================================================================== Xuất gửi kho
    // Cargo 15 dãy / bảng (cột F…T) + cột KQKS, KHÔNG chia theo mục / nhóm thị trường; phiếu liệt kê từng dòng (STT · Mã hàng 19 ký tự · Tên hàng · Ghi chú = lô · Size · Packing · Kiện · Kg).
    //   Sheet "Cargo" (sheet2): 1–6 đầu trang · 7 tiêu đề bảng · 12 dòng hàng bảng 1 · 20 dòng hàng bảng 2+ · 16 TỔNG bảng 1 · 26 TỔNG bảng 2+ · 17 đệm giữa 2 bảng · 27 bằng chữ · 28 chữ ký + TỔNG
    //   Sheet "Phiếu xuất kho" (sheet1): 1–13 đầu phiếu + tiêu đề cột · 14 dòng hàng · 26 dòng đóng khung · 27 TOTAL · 28 "Tổng : N dãy" · 29 Tổng · 30 bằng chữ · 31–32 nhiệt độ · 33 chữ ký · 34 CCCD

    private static void TrimCols(Sheet P, int maxCol) { foreach (var row in P.Rows.Values) row.Cells = row.Cells.Where(c => ColIdx(c.Col) <= maxCol).ToList(); }
    private static string ResetView(string x) => Regex.Replace(Regex.Replace(x, "\\stopLeftCell=\"[^\"]*\"", ""), @"<selection\b[^>]*/>", "<selection activeCell=\"A1\" sqref=\"A1\"/>");

    private static (string Xml, int Last) BuildCargoGk(string xml, CargoXlsxData d)
    {
        var P = ParseSheet(xml); TrimCols(P, 21); var outR = new List<string>(); var merges = new List<string> { "A1:T1", "A2:T2", "C4:I4", "C5:I5" }; var r = 0; double tot = 0;
        void Put(int n, Dictionary<string, object?>? vals, bool keep = true) { r++; outR.Add(RowXml(Need(P, n, "cargo gửi kho"), r, vals, keep)); }
        for (var n = 1; n <= 6; n++)
        {
            var v = new Dictionary<string, object?>();
            if (n == 3) v["A"] = "Khách hàng : " + d.Khach;
            if (n == 4) { v["A"] = "Cont gửi kho số: " + d.ContSo; v["C"] = "Số Cont : " + d.ContXe; }
            if (n == 5) { v["A"] = "Ngày xuất : " + d.NgayText; v["C"] = "Seal số : " + d.Seal; }
            Put(n, v);
        }
        var printed = 0; var totRowsGk = new List<int>();
        for (var t = 0; t < d.Tables.Count; t++)
        {
            var items = d.Tables[t]; if (t > 0 && items.Count == 0) continue;
            if (printed > 0) Put(17, null); printed++;
            var hdr = new Dictionary<string, object?>(); for (var i = 0; i < HGK; i++) hdr[ColName(6 + i)] = (double)(t * HGK + i + 1);
            Put(7, hdr);
            var sums = new double[HGK]; double sumU = 0; int rA = 0, rB = 0;
            foreach (var it in items)
            {
                var v = new Dictionary<string, object?> { ["A"] = it.A, ["B"] = it.B, ["C"] = it.C, ["D"] = it.V, ["E"] = it.W, ["U"] = F($"SUM(F{r + 1}:T{r + 1})", it.Total) };
                for (var i = 0; i < HGK && i < it.Dai.Length; i++) if (it.Dai[i] != 0) { v[ColName(6 + i)] = it.Dai[i]; sums[i] += it.Dai[i]; }
                sumU += it.Total; Put(t == 0 ? 12 : 20, v, false); if (rA == 0) rA = r; rB = r;
            }
            var vt = new Dictionary<string, object?> { ["U"] = sumU != 0 ? sumU : "" };
            for (var i = 0; i < HGK; i++) vt[ColName(6 + i)] = sums[i] != 0 ? sums[i] : "";
            if (rA > 0) { vt["U"] = F(SumF("U", rA, rB), sumU); for (var i = 0; i < HGK; i++) vt[ColName(6 + i)] = F(SumF(ColName(6 + i), rA, rB), sums[i]); }
            tot += sumU; Put(t == 0 ? 16 : 26, vt); totRowsGk.Add(r);
        }
        Put(27, new() { ["A"] = "Bằng chữ: " + d.BangChu });
        Put(28, new() { ["U"] = totRowsGk.Count > 0 ? F(string.Join("+", totRowsGk.Select(x => "U" + x)), tot) : tot });
        return (ResetView(Assemble(P, outR, merges, "U", r, null, false)), r);
    }

    private static (string Xml, int Last) BuildPhieuGk(string xml, CargoXlsxData d)
    {
        var P = ParseSheet(xml); TrimCols(P, 8); var g = d.Gk!; var outR = new List<string>();
        var merges = new List<string> { "A1:H1", "A2:H2", "A3:H3", "A4:H4", "A5:H5", "A6:H6", "A7:C7", "E7:H7", "A8:C8", "E8:H8", "A9:C9", "E9:H9", "A10:C10", "E10:H10" }; var r = 0;
        int Put(int n, Dictionary<string, object?>? vals, bool keep = true) { r++; outR.Add(RowXml(Need(P, n, "phiếu gửi kho"), r, vals, keep)); return r; }
        for (var n = 1; n <= 13; n++)
        {
            var v = new Dictionary<string, object?>();
            if (n == 6) v["A"] = "(" + d.Kho + ")";
            if (n == 7) { v["A"] = "Kho xuất: " + d.Kho + "                   CONT GỬI KHO SỐ:  " + d.ContSo; v["E"] = "Số : " + d.SoPhieu; }
            if (n == 8) { v["A"] = "Người nhận hàng : " + d.NguoiNhan; v["E"] = "Cont/Xe : " + d.ContXe; }
            if (n == 9) { v["A"] = "Lý do xuất : " + d.LyDo; v["E"] = "Seal : " + d.Seal; }
            if (n == 10) { v["A"] = "Theo hợp đồng số : "; v["E"] = "Kiểu xuất : " + d.KieuXuat; }
            Put(n, v);
        }
        int gA = 0, gB = 0;
        foreach (var row in g.Rows)
        {
            var nr = r + 1;
            Put(14, new()
            {
                ["A"] = (double)row.Stt, ["B"] = row.Ma, ["C"] = row.Ten, ["D"] = row.Lo, ["E"] = row.Size, ["F"] = row.Pack, ["G"] = row.Kien,
                ["H"] = row.Per > 0 ? F($"ROUND(G{nr}*{row.Per.ToString(CultureInfo.InvariantCulture)},3)", R3(row.Kien * row.Per)) : R3(row.Kg),
            }, false);
            if (gA == 0) gA = r; gB = r;
        }
        Put(26, null);
        var rTot = Put(27, gA > 0 ? new() { ["G"] = F(SumF("G", gA, gB), g.Kien), ["H"] = F(SumF("H", gA, gB), R3(g.Kg)) } : new() { ["G"] = g.Kien, ["H"] = R3(g.Kg) });
        { var rr = Put(28, new() { ["B"] = $"Tổng : {g.SoDay} dãy" }); merges.Add($"B{rr}:F{rr}"); }
        Put(29, gA > 0 ? new() { ["G"] = F("G" + rTot, g.Kien), ["H"] = F("H" + rTot, R3(g.Kg)) } : new() { ["G"] = g.Kien, ["H"] = R3(g.Kg) });
        { var rr = Put(30, new() { ["B"] = new RichText(RichBc(d.BangChu)) }); merges.Add($"B{rr}:F{rr}"); }
        { var rr = Put(31, null); merges.Add($"A{rr}:F{rr}"); }
        { var rr = Put(32, null); merges.Add($"A{rr}:F{rr}"); }
        { var rr = Put(33, null); merges.Add($"A{rr}:H{rr}"); }
        Put(34, null);
        return (ResetView(Assemble(P, outR, merges, "H", r, null, true)), r);
    }

    // ================================================================== ghép file

    private static string ReadEntry(ZipArchive z, string name)
    {
        var e = z.GetEntry(name) ?? throw new InvalidOperationException("Mẫu hỏng: thiếu " + name);
        using var rd = new StreamReader(e.Open(), new UTF8Encoding(false)); return rd.ReadToEnd();
    }
    private static void WriteEntry(ZipArchive z, string name, string content)
    {
        z.GetEntry(name)?.Delete();
        using var w = new StreamWriter(z.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false)); w.Write(content);
    }
    private static string CalcOnLoad(string wb) =>
        Regex.IsMatch(wb, @"<calcPr\b[^>]*/>") ? Regex.Replace(wb, @"<calcPr\b[^>]*/>", "<calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/>") : wb.Replace("</workbook>", "<calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/></workbook>");
    private static string FixDrawingDate(string dr, string ngay)
    {
        var m = Regex.Match(ngay ?? "", @"^(\d{4})-(\d{2})-(\d{2})$"); if (!m.Success) return dr;
        return Regex.Replace(dr, "<a:t>([^<]*)</a:t>", all => Regex.IsMatch(all.Groups[1].Value, @"Ngày\s+\d+\s+tháng\s+\d+\s+năm\s+\d+")
            ? "<a:t>" + Regex.Replace(all.Groups[1].Value, @"Ngày\s+\d+\s+tháng\s+\d+\s+năm\s+\d+", $"Ngày {int.Parse(m.Groups[3].Value)} tháng {m.Groups[2].Value} năm {m.Groups[1].Value}") + "</a:t>" : all.Value);
    }
    private static string Names(string defs, string wb) => Regex.Replace(wb, @"<definedNames/>|<definedNames>[\s\S]*?</definedNames>", defs.Replace("$", "$$"));

    public static byte[] BuildTp(byte[] template, CargoXlsxData d)
    {
        using var ms = new MemoryStream(); ms.Write(template, 0, template.Length);
        using (var z = new ZipArchive(ms, ZipArchiveMode.Update, true))
        {
            var cg = BuildCargo(ReadEntry(z, "xl/worksheets/sheet2.xml"), d); var ph = BuildPhieu(ReadEntry(z, "xl/worksheets/sheet3.xml"), d);
            var cgXml = cg.Xml; var phXml = ph.Xml;
            var khach = d.Khach.Length > 0 ? d.Khach : "THUẬN PHƯỚC"; var lyDo = d.LyDo.Length > 0 ? d.LyDo : "XUẤT HÀNG VỀ THUẬN PHƯỚC";
            if (khach != "THUẬN PHƯỚC") cgXml = cgXml.Replace("Khách hàng : THUẬN PHƯỚC", "Khách hàng : " + Esc(khach));
            if (lyDo != "XUẤT HÀNG VỀ THUẬN PHƯỚC") phXml = phXml.Replace("Lý do xuất : XUẤT HÀNG VỀ THUẬN PHƯỚC", "Lý do xuất : " + Esc(lyDo));
            WriteEntry(z, "xl/worksheets/sheet2.xml", cgXml); WriteEntry(z, "xl/worksheets/sheet3.xml", phXml);
            WriteEntry(z, "xl/drawings/drawing1.xml", FixDrawingDate(AnchorBoxes(ReadEntry(z, "xl/drawings/drawing1.xml"), ph.Layout), d.Ngay));
            var defs = $"<definedNames><definedName name=\"_xlnm.Print_Area\" localSheetId=\"0\">Cargo!$A$1:${ColName(25 + SH)}${cg.Last}</definedName><definedName name=\"_xlnm.Print_Area\" localSheetId=\"1\">'Phiếu xuất kho'!$A$1:$AD${ph.Last}</definedName><definedName name=\"_xlnm.Print_Titles\" localSheetId=\"1\">'Phiếu xuất kho'!$1:$12</definedName></definedNames>";
            var wb = CalcOnLoad(Names(defs, ReadEntry(z, "xl/workbook.xml")).Replace("<sheet name=\"01\"", "<sheet name=\"Phiếu xuất kho\""));
            WriteEntry(z, "xl/workbook.xml", wb);
        }
        return ms.ToArray();
    }

    public static byte[] BuildGkFile(byte[] template, CargoXlsxData d)
    {
        using var ms = new MemoryStream(); ms.Write(template, 0, template.Length);
        using (var z = new ZipArchive(ms, ZipArchiveMode.Update, true))
        {
            var ph = BuildPhieuGk(ReadEntry(z, "xl/worksheets/sheet1.xml"), d); var cg = BuildCargoGk(ReadEntry(z, "xl/worksheets/sheet2.xml"), d);
            WriteEntry(z, "xl/worksheets/sheet1.xml", ph.Xml); WriteEntry(z, "xl/worksheets/sheet2.xml", cg.Xml);
            WriteEntry(z, "xl/drawings/drawing1.xml", FixDrawingDate(ReadEntry(z, "xl/drawings/drawing1.xml"), d.Ngay));
            var defs = $"<definedNames><definedName name=\"_xlnm.Print_Area\" localSheetId=\"0\">'Phiếu xuất kho'!$A$1:$H${ph.Last}</definedName><definedName name=\"_xlnm.Print_Area\" localSheetId=\"1\">Cargo!$A$1:$U${cg.Last}</definedName></definedNames>";
            WriteEntry(z, "xl/workbook.xml", CalcOnLoad(Names(defs, ReadEntry(z, "xl/workbook.xml"))));
        }
        return ms.ToArray();
    }

    // ================================================================== dữ liệu → data của bộ ghi

    /// <summary>Thuận Phước / Gia công: 2 bảng × 20 dãy, mỗi khối có dòng tiêu đề + các dòng hàng (cột thị trường, size, quy cách, lô, kết quả, mã hàng).</summary>
    public static CargoXlsxData ToDataTp(CargoCfg cfg, CargoRec cargo, CargoCore.KsIndex? ks, CargoProfile prof, bool preview = false)
    {
        var ph = CargoCore.BuildPhieu(cfg, cargo, ks); var m = Regex.Match(cargo.Ngay, @"^(\d{4})-(\d{2})-(\d{2})$");
        var tables = new List<List<CargoItem>> { new(), new() };
        foreach (var b in cargo.Blocks)
        {
            var per = new List<List<CargoItem>> { new(), new() };
            foreach (var ln in b.Lines)
            {
                var r = CargoCore.ResolveLine(cfg, b, ln, ks); if (r.Kien <= 0 && !(preview && (r.Size.Length > 0 || r.Pack.Length > 0))) continue;
                for (var t = 0; t < 2; t++)
                {
                    var dai = new double[Half]; double tot = 0; for (var i = 0; i < Half; i++) { var q = CargoCore.DayQty(ln, t * Half + i + 1); dai[i] = q; tot += q; }
                    if (tot <= 0 && !(preview && r.Kien <= 0 && t == 0)) continue;
                    var it = new CargoItem { Dai = dai, Total = tot, B = r.Pack, C = r.Size };
                    if (r.Other) it.A = ".";
                    else
                    {
                        it.A = r.Market; it.V = r.Sn is { Ok: true } sn ? sn.Size4 + "-" + sn.Digit : ""; it.W = r.Ks is null ? "" : (double)r.Ks.Stt; it.X = r.LoText; it.Y = r.KsText; it.Z = r.Loai; it.Aa = r.Ma; it.Ab = r.Size + " - " + r.Pack + " - " + r.Market;
                    }
                    per[t].Add(it);
                }
            }
            for (var t = 0; t < 2; t++) if (per[t].Count > 0) { tables[t].Add(new CargoItem { IsTitle = true, Text = b.Ten.Trim() }); tables[t].AddRange(per[t]); }
        }
        return new CargoXlsxData
        {
            Khach = prof.Khach, LyDo = prof.LyDo, Kho = cargo.Kho, Ngay = cargo.Ngay, NgayText = m.Success ? $"{m.Groups[3].Value}.{m.Groups[2].Value}.{m.Groups[1].Value}" : "", SoPhieu = cargo.SoPhieu,
            NguoiNhan = cargo.NguoiNhan, ContXe = cargo.ContXe, Seal = cargo.Seal, BangChu = PhieuKhoCore.DocSo(ph.Kien) + " kiện", Phieu = ph, Tables = tables,
        };
    }

    /// <summary>Gửi kho: 3 bảng × 15 dãy, mỗi dòng hàng 1 dòng (không gộp), mã rút gọn 15 ký tự + cột KQKS.</summary>
    public static CargoXlsxData ToDataGk(CargoCfg cfg, CargoRec cargo, CargoCore.KsIndex? ks, CargoProfile prof, bool preview = false)
    {
        var g = CargoCore.BuildGk(cfg, cargo, ks, preview); var m = Regex.Match(cargo.Ngay, @"^(\d{4})-(\d{2})-(\d{2})$"); var nt = (int)Math.Ceiling(NDay / (double)HGK);
        var tables = Enumerable.Range(0, nt).Select(_ => new List<CargoItem>()).ToList();
        foreach (var row in g.Rows)
            for (var t = 0; t < nt; t++)
            {
                var dai = new double[HGK]; double tot = 0; for (var i = 0; i < HGK; i++) { var di = t * HGK + i; var q = di < row.Dai.Length ? row.Dai[di] : 0; dai[i] = q; tot += q; }
                if (tot > 0 || (preview && row.Kien <= 0 && t == 0)) tables[t].Add(new CargoItem { A = row.Ma15, B = row.Lo, C = row.Pack, V = row.Ks, W = row.Size, Dai = dai, Total = tot });
            }
        var khoNhan = cargo.KhoNhan.Trim().Length > 0 ? cargo.KhoNhan.Trim() : prof.KhoNhan;
        return new CargoXlsxData
        {
            Khach = "GỬI KHO " + khoNhan.ToUpperInvariant(), LyDo = "Xuất hàng gửi kho " + khoNhan, KieuXuat = prof.KieuXuat, Kho = cargo.Kho, Ngay = cargo.Ngay, NgayText = m.Success ? $"{m.Groups[3].Value}.{m.Groups[2].Value}.{m.Groups[1].Value}" : "",
            SoPhieu = cargo.SoPhieu, ContSo = cargo.ContSo.Trim().Length > 0 ? cargo.ContSo.Trim() : CargoCore.ContSoOf(cargo, prof.Code), NguoiNhan = cargo.NguoiNhan, ContXe = cargo.ContXe, Seal = cargo.Seal,
            BangChu = PhieuKhoCore.DocSo(g.Kien) + " kiện", Gk = g, Tables = tables,
        };
    }
}

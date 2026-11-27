using ClosedXML.Excel;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// Dựng file Excel giống cách app cũ dựng bằng SheetJS (aoa_to_sheet + style từng ô): mảng 2 chiều giá trị (chữ / số),
/// mỗi ô một kiểu (font Times New Roman 14, nền, căn lề, viền, định dạng số), độ rộng cột tự tính như computeAutoColWidths,
/// cố định dòng đầu, AutoFilter, công thức SUBTOTAL, gộp ô.
/// </summary>
public sealed class XlsxOut
{
    public sealed record St(bool Bold = false, string? Color = null, string? Fill = null, string HAlign = "left", bool Wrap = false, bool TopMedium = false, string? NumFmt = null, bool Border = true, string? Font = "Times New Roman", double Size = 14);

    public sealed class Sheet
    {
        public string Name = "Sheet1";
        public List<object?[]> Aoa = new();
        public Dictionary<(int r, int c), St> Styles = new();
        public Dictionary<(int r, int c), string> Formulas = new();
        public List<(int r1, int c1, int r2, int c2)> Merges = new();
        public (int r, int c1, int c2)? AutoFilter;   // dòng tiêu đề (0-based) + cột đầu / cuối
        public int FreezeRows = 1;
        public double[]? Widths;
        public bool FitToPage;
        public void Set(int r, int c, St st) => Styles[(r, c)] = st;
    }

    public List<Sheet> Sheets = new();

    /// <summary>computeAutoColWidths(aoa, 8, 120): wch = max(8, min(120, ceil(dài × 1.15) + 3)); số đo theo toLocaleString en-US tối đa 2 số lẻ.</summary>
    public static double[] AutoWidths(List<object?[]> aoa, int min = 8, int max = 120)
    {
        var n = aoa.Count == 0 ? 0 : aoa.Max(r => r.Length);
        var w = new double[n];
        for (var c = 0; c < n; c++)
        {
            var len = 0;
            foreach (var row in aoa)
            {
                if (c >= row.Length) continue;
                var s = row[c] switch { null => "", string x => x, double d => JsText.En(d, 0, 2), int i => JsText.En(i, 0, 2), _ => row[c]!.ToString() ?? "" };
                len = Math.Max(len, JsLength(s));
            }
            w[c] = Math.Max(min, Math.Min(max, Math.Ceiling(len * 1.15) + 3));
        }
        return w;
    }

    /// <summary>String.length của JS (đếm theo UTF-16).</summary>
    public static int JsLength(string s) => s.Length;

    public void Save(string path)
    {
        using var wb = new XLWorkbook();
        foreach (var sh in Sheets) Write(wb, sh);
        wb.SaveInPlace(path);
    }

    public MemoryStream ToStream()
    {
        using var wb = new XLWorkbook();
        foreach (var sh in Sheets) Write(wb, sh);
        var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0; return ms;
    }

    private static void Write(XLWorkbook wb, Sheet sh)
    {
        var ws = wb.Worksheets.Add(SafeName(sh.Name));
        for (var r = 0; r < sh.Aoa.Count; r++)
        {
            var row = sh.Aoa[r];
            for (var c = 0; c < row.Length; c++)
            {
                var v = row[c];
                if (v is null) continue;
                var cell = ws.Cell(r + 1, c + 1);
                if (v is double d) cell.Value = d;
                else if (v is int i) cell.Value = i;
                else { var s = v.ToString() ?? ""; if (s.Length > 0) cell.Value = s; }
            }
        }
        foreach (var ((r, c), st) in sh.Styles)
        {
            var cell = ws.Cell(r + 1, c + 1);
            var s = cell.Style;
            if (st.Font is not null) s.Font.FontName = st.Font;
            s.Font.FontSize = st.Size;
            s.Font.Bold = st.Bold;
            if (st.Color is not null) s.Font.FontColor = XLColor.FromHtml("#" + st.Color);
            if (st.Fill is not null) s.Fill.SetBackgroundColor(XLColor.FromHtml("#" + st.Fill));
            s.Alignment.Horizontal = st.HAlign switch { "center" => XLAlignmentHorizontalValues.Center, "right" => XLAlignmentHorizontalValues.Right, _ => XLAlignmentHorizontalValues.Left };
            s.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            if (st.Wrap) s.Alignment.WrapText = true;
            if (st.Border)
            {
                s.Border.LeftBorder = s.Border.RightBorder = s.Border.BottomBorder = XLBorderStyleValues.Thin;
                s.Border.TopBorder = st.TopMedium ? XLBorderStyleValues.Medium : XLBorderStyleValues.Thin;
                s.Border.LeftBorderColor = s.Border.RightBorderColor = s.Border.BottomBorderColor = s.Border.TopBorderColor = XLColor.Black;
            }
            var val = r < sh.Aoa.Count && c < sh.Aoa[r].Length ? sh.Aoa[r][c] : null;
            if (st.NumFmt is not null && val is double or int) s.NumberFormat.Format = st.NumFmt;
        }
        foreach (var ((r, c), f) in sh.Formulas) ws.Cell(r + 1, c + 1).FormulaA1 = f;
        foreach (var (r1, c1, r2, c2) in sh.Merges) ws.Range(r1 + 1, c1 + 1, r2 + 1, c2 + 1).Merge();
        var widths = sh.Widths ?? AutoWidths(sh.Aoa);
        for (var c = 0; c < widths.Length; c++) ws.Column(c + 1).Width = widths[c] + 0.83203125 - 0.71062475; // ClosedXML tự cộng thêm ~0.71 khi ghi
        if (sh.FreezeRows > 0) ws.SheetView.FreezeRows(sh.FreezeRows);
        if (sh.AutoFilter is var (ar, ac1, ac2) && sh.Aoa.Count > ar)
            ws.Range(ar + 1, ac1 + 1, Math.Max(ar + 1, sh.Aoa.Count), ac2 + 1).SetAutoFilter();
        if (sh.FitToPage) { ws.PageSetup.PageOrientation = XLPageOrientation.Portrait; ws.PageSetup.FitToPages(1, 0); }
    }

    private static string SafeName(string s)
    {
        foreach (var ch in new[] { ':', '\\', '/', '?', '*', '[', ']' }) s = s.Replace(ch, ' ');
        return s.Length > 31 ? s[..31] : s.Length == 0 ? "Sheet1" : s;
    }

    /// <summary>Tên cột Excel (0 → A).</summary>
    public static string ColName(int c) { var s = ""; c++; while (c > 0) { var m = (c - 1) % 26; s = (char)('A' + m) + s; c = (c - 1) / 26; } return s; }
}

using ClosedXML.Excel;
using KhoLanh.Core.CuoiKy;

namespace KhoLanh.Core.Bang;

/// <summary>Đọc / ghi bảng Excel cho "Xử lý PDF &amp; Excel" và "So sánh &amp; Tra cứu bảng". File gốc chỉ đọc; kết quả luôn là file mới.</summary>
public static class ExcelOps
{
    public sealed record Sheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Aoa);

    /// <summary>Đọc mọi sheet của file (xlsx / xlsm / xls / csv); số giữ là số, ngày → số ngày Excel.</summary>
    public static List<Sheet> Read(string path)
    {
        var (book, names) = CkService.ReadExcel(path);
        return names.Select(n => new Sheet(n, book[n])).ToList();
    }

    /// <summary>Sheet → bảng (tự đoán dòng tiêu đề nếu <paramref name="headerRow"/> &lt; 0).</summary>
    public static CcTable ToTable(Sheet s, int headerRow = -1) => CcBang.FromAoa(s.Aoa, headerRow >= 0 ? headerRow : CcBang.GuessHeader(s.Aoa));

    /// <summary>Ghi nhiều bảng vào 1 file (mỗi bảng 1 sheet; tên sheet tự cắt ≤ 31 ký tự, không trùng).</summary>
    public static void Write(string path, IEnumerable<(string Name, CcTable Tbl)> sheets, Func<int, int, bool>? highlight = null)
    {
        using var wb = new XLWorkbook(); var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name0, t) in sheets)
        {
            var name = SheetName(name0, used);
            var ws = wb.AddWorksheet(name);
            for (var c = 0; c < t.Head.Count; c++) { var cell = ws.Cell(1, c + 1); cell.Value = t.Head[c]; cell.Style.Font.Bold = true; cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2"); }
            for (var r = 0; r < t.Rows.Count; r++)
                for (var c = 0; c < t.Rows[r].Length; c++)
                {
                    var v = t.Rows[r][c]; var cell = ws.Cell(r + 2, c + 1);
                    switch (v) { case double d: cell.Value = d; break; case int i: cell.Value = i; break; case DateTime dt: cell.Value = dt; break; case null: break; default: cell.Value = CcBang.Str(v); break; }
                    if (highlight?.Invoke(r, c) == true) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
                }
            if (t.Head.Count > 0) { ws.SheetView.FreezeRows(1); ws.Range(1, 1, Math.Max(1, t.Rows.Count + 1), t.Head.Count).SetAutoFilter(); }
            try { ws.Columns(1, Math.Max(1, t.Head.Count)).AdjustToContents(1, Math.Min(t.Rows.Count + 1, 300), 6, 60); } catch { /* máy không có phông: bỏ qua căn cột */ }
        }
        if (!wb.Worksheets.Any()) wb.AddWorksheet("Sheet1");
        wb.SaveAs(path);
    }

    public static string SheetName(string name0, HashSet<string> used)
    {
        var n = string.Concat((name0 ?? "").Select(ch => "[]:*?/\\".Contains(ch) ? '_' : ch)).Trim();
        if (n.Length == 0) n = "Sheet"; if (n.Length > 31) n = n[..31];
        var b = n; var i = 2; while (used.Contains(n)) { var suf = $" ({i++})"; n = (b.Length + suf.Length > 31 ? b[..(31 - suf.Length)] : b) + suf; }
        used.Add(n); return n;
    }
}

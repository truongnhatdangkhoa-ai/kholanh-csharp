using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;

namespace KhoLanh.Core.Services;

/// <summary>
/// IN TRỰC TIẾP: chuyển file Excel vừa xuất thành trang in A4 (mỗi sheet 1 phần, ngắt trang giữa các sheet), giữ độ rộng cột, ô gộp, đậm / nghiêng, cỡ chữ, màu nền, viền,
/// căn lề và khổ giấy (đứng / ngang). Mở trang này bằng trình duyệt rồi bấm In (hoặc chọn "Lưu dưới dạng PDF"). Mọi nút "In" dùng lại hàm xuất Excel sẵn có nên in ra đúng bản Excel.
/// </summary>
public static class XlsxHtml
{
    private static string H(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static double Px(double widthChars) => Math.Round(widthChars * 7 + 5);

    private static string? Hex(XLColor c)
    {
        try
        {
            if (c.ColorType == XLColorType.Color) { var x = c.Color; return $"#{x.R:X2}{x.G:X2}{x.B:X2}"; }
            if (c.ColorType == XLColorType.Theme) return c.ThemeColor == XLThemeColor.Text1 ? "#000000" : c.ThemeColor == XLThemeColor.Background1 ? "#FFFFFF" : null;
            if (c.ColorType == XLColorType.Indexed) return null;
        }
        catch { }
        return null;
    }
    private static string Border(XLBorderStyleValues s) => s switch
    {
        XLBorderStyleValues.None => "", XLBorderStyleValues.Medium or XLBorderStyleValues.Thick or XLBorderStyleValues.MediumDashed => "2px solid #000",
        XLBorderStyleValues.Double => "3px double #000", _ => "1px solid #000",
    };

    public static string Render(string xlsxPath, string title)
    {
        using var fs = new FileStream(xlsxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(fs);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>").Append(H(title)).Append("</title><style>")
          .Append("""
            body{margin:0;background:#e9edf2;font-family:'Times New Roman',Times,serif;color:#000}
            .bar{position:sticky;top:0;background:#0b2a45;color:#fff;padding:8px 14px;font:13px Arial,sans-serif;display:flex;gap:10px;align-items:center;z-index:2}
            .bar button{font:13px Arial,sans-serif;padding:5px 14px;border-radius:5px;border:0;background:#1f8fd1;color:#fff;cursor:pointer}
            .sheet{background:#fff;margin:10px auto;padding:10mm 8mm;box-shadow:0 1px 6px rgba(0,0,0,.25);box-sizing:border-box;overflow:hidden}
            .sheet.p{width:210mm}.sheet.l{width:297mm}
            table{border-collapse:collapse;table-layout:fixed}
            td{padding:1px 3px;overflow:hidden;vertical-align:bottom;word-wrap:break-word}
            .nm{font:11px Arial,sans-serif;color:#666;margin-bottom:4px}
            @media print{body{background:#fff}.bar{display:none}.sheet{margin:0;box-shadow:none;padding:0;width:auto!important;page-break-after:always}.sheet:last-child{page-break-after:auto}.nm{display:none}}
            """);
        var landscapeAny = wb.Worksheets.Any(w => w.PageSetup.PageOrientation == XLPageOrientation.Landscape);
        var portraitAny = wb.Worksheets.Any(w => w.PageSetup.PageOrientation != XLPageOrientation.Landscape);
        // khổ giấy in theo sheet đầu (nếu có cả hai hướng, trình duyệt cho chọn khi in)
        sb.Append("@page{size:A4 ").Append(wb.Worksheets.First().PageSetup.PageOrientation == XLPageOrientation.Landscape && !portraitAny || (landscapeAny && wb.Worksheets.First().PageSetup.PageOrientation == XLPageOrientation.Landscape) ? "landscape" : "portrait").Append(";margin:10mm}");
        sb.Append("</style></head><body><div class=\"bar\"><b>").Append(H(title)).Append("</b><span>").Append(wb.Worksheets.Count).Append(" trang tính</span><button onclick=\"window.print()\">In / Lưu PDF</button><span style=\"opacity:.75\">Chọn máy in hoặc “Lưu dưới dạng PDF”.</span></div>");
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed(); if (used is null) continue;
            int r1 = used.FirstRow().RowNumber(), r2 = used.LastRow().RowNumber(), c1 = used.FirstColumn().ColumnNumber(), c2 = used.LastColumn().ColumnNumber();
            var land = ws.PageSetup.PageOrientation == XLPageOrientation.Landscape;
            var cols = Enumerable.Range(c1, c2 - c1 + 1).Where(c => !ws.Column(c).IsHidden).ToList();
            var colPx = cols.ToDictionary(c => c, c => Px(ws.Column(c).Width));
            var totalPx = colPx.Values.Sum();
            var pagePx = (land ? 297 - 16 : 210 - 16) / 25.4 * 96;
            var zoom = Math.Min(1.0, pagePx / Math.Max(1, totalPx));
            var merges = new Dictionary<(int, int), (int Rs, int Cs)>(); var covered = new HashSet<(int, int)>();
            foreach (var m in ws.MergedRanges)
            {
                int a = m.FirstRow().RowNumber(), b = m.FirstColumn().ColumnNumber(), rs = m.RowCount(), cs = m.ColumnCount();
                merges[(a, b)] = (rs, cs);
                for (var r = a; r < a + rs; r++) for (var c = b; c < b + cs; c++) if (r != a || c != b) covered.Add((r, c));
            }
            sb.Append("<div class=\"sheet ").Append(land ? "l" : "p").Append("\"><div class=\"nm\">").Append(H(ws.Name)).Append("</div><div style=\"zoom:").Append(zoom.ToString("0.###", CultureInfo.InvariantCulture)).Append("\"><table style=\"width:").Append(totalPx.ToString("0", CultureInfo.InvariantCulture)).Append("px\"><colgroup>");
            foreach (var c in cols) sb.Append("<col style=\"width:").Append(colPx[c].ToString("0", CultureInfo.InvariantCulture)).Append("px\">");
            sb.Append("</colgroup>");
            for (var r = r1; r <= r2; r++)
            {
                var row = ws.Row(r); if (row.IsHidden) continue;
                var ht = row.Height > 0 ? row.Height * 96 / 72 : 20;
                sb.Append("<tr style=\"height:").Append(ht.ToString("0", CultureInfo.InvariantCulture)).Append("px\">");
                foreach (var c in cols)
                {
                    if (covered.Contains((r, c))) continue;
                    var cell = ws.Cell(r, c); var st = cell.Style; var css = new StringBuilder();
                    var rs = 1; var cs = 1; if (merges.TryGetValue((r, c), out var mg)) { rs = mg.Rs; cs = mg.Cs; }
                    var f = st.Font; css.Append("font-size:").Append(f.FontSize.ToString("0.#", CultureInfo.InvariantCulture)).Append("pt;font-family:'").Append(f.FontName.Replace("'", "")).Append("',serif;");
                    if (f.Bold) css.Append("font-weight:700;"); if (f.Italic) css.Append("font-style:italic;"); if (f.Underline != XLFontUnderlineValues.None) css.Append("text-decoration:underline;");
                    if (Hex(f.FontColor) is { } fc && fc != "#000000") css.Append("color:").Append(fc).Append(';');
                    if (st.Fill.PatternType == XLFillPatternValues.Solid && Hex(st.Fill.BackgroundColor) is { } bg && bg != "#FFFFFF") css.Append("background:").Append(bg).Append(';');
                    var h = st.Alignment.Horizontal;
                    var isNum = cell.DataType == XLDataType.Number || cell.DataType == XLDataType.DateTime;
                    css.Append("text-align:").Append(h switch { XLAlignmentHorizontalValues.Center or XLAlignmentHorizontalValues.CenterContinuous => "center", XLAlignmentHorizontalValues.Right => "right", XLAlignmentHorizontalValues.Left => "left", _ => isNum ? "right" : "left" }).Append(';');
                    css.Append("vertical-align:").Append(st.Alignment.Vertical switch { XLAlignmentVerticalValues.Top => "top", XLAlignmentVerticalValues.Center => "middle", _ => "bottom" }).Append(';');
                    if (!st.Alignment.WrapText) css.Append("white-space:nowrap;"); else css.Append("white-space:pre-wrap;");
                    // viền: lấy của ô góc; ô gộp lấy viền phải / dưới của ô cuối
                    var lastCell = rs > 1 || cs > 1 ? ws.Cell(r + rs - 1, c + cs - 1).Style : st;
                    if (Border(st.Border.TopBorder) is { Length: > 0 } bt) css.Append("border-top:").Append(bt).Append(';');
                    if (Border(st.Border.LeftBorder) is { Length: > 0 } bl) css.Append("border-left:").Append(bl).Append(';');
                    if (Border(lastCell.Border.BottomBorder) is { Length: > 0 } bb) css.Append("border-bottom:").Append(bb).Append(';');
                    if (Border(lastCell.Border.RightBorder) is { Length: > 0 } br) css.Append("border-right:").Append(br).Append(';');
                    sb.Append("<td");
                    if (rs > 1) sb.Append(" rowspan=\"").Append(rs).Append('"'); if (cs > 1) sb.Append(" colspan=\"").Append(cs).Append('"');
                    sb.Append(" style=\"").Append(css).Append("\">").Append(H(cell.IsEmpty() ? "" : cell.GetFormattedString())).Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</table></div></div>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Ghi trang in vào thư mục <paramref name="dir"/> (xóa bản cũ quá 2 ngày) và trả về đường dẫn file .html.</summary>
    public static string WritePage(string xlsxPath, string title, string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "in-*.html").Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-2))) try { File.Delete(old); } catch { }
        var path = Path.Combine(dir, $"in-{DateTime.Now:yyMMdd-HHmmss}.html");
        File.WriteAllText(path, Render(xlsxPath, title), new UTF8Encoding(false));
        return path;
    }
}

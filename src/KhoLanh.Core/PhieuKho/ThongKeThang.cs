using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace KhoLanh.Core.PhieuKho;

/// <summary>
/// THỐNG KÊ XUẤT KHẨU THEO THÁNG (port từ pnx-thong-ke-thang.js). Nguồn: các phiếu xuất khẩu ĐÃ LƯU.
/// Mỗi dòng = (LSX · phiếu · ngày sản xuất của dòng size · size) → số kiện. Mỗi PHIẾU = 1 nhóm = 1 STT (cont ghép nhiều LSX vẫn là 1);
/// dưới mỗi phiếu có dòng "Tổng", cuối có "TỔNG THÁNG"; thêm sheet "Tổng theo Size".
/// </summary>
public static class ThongKeThang
{
    public sealed record Row(string Lsx, string Ngay, string Size, double Kien);
    public sealed class Group
    {
        public string SoPhieu = "", Xuat = "", Khach = "";
        public double Tong;
        public List<Row> Rows = new();
        public List<string> Lsxs => Rows.Select(r => r.Lsx).Where(x => x.Length > 0).Distinct().ToList();
    }
    public sealed record SizeTotal(string Size, double Kien);
    public sealed record Data(string MonthKey, List<Group> Groups, double TongThang, List<SizeTotal> BySize);

    private static double Num(string? v) => double.TryParse((v ?? "").Replace(" ", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : 0;
    private static string DayKeyOf(Phieu p) { var m = Regex.Match(p.Ngay ?? "", @"^(\d{4}-\d{2}-\d{2})"); return m.Success ? m.Groups[1].Value : ""; }

    /// <summary>Ngày dòng size 'dd.mm.yyyy' / 'dd/mm/yyyy' / 'yyyy-mm-dd' → 'yyyy-mm-dd' ("" nếu không đọc được).</summary>
    public static string DayOfRow(string? s)
    {
        s = (s ?? "").Trim();
        var m = Regex.Match(s, @"^(\d{1,2})[\/\-. ](\d{1,2})[\/\-. ](\d{4})$");
        if (m.Success) return $"{m.Groups[3].Value}-{int.Parse(m.Groups[2].Value):00}-{int.Parse(m.Groups[1].Value):00}";
        m = Regex.Match(s, @"^(\d{4})[\/\-.](\d{1,2})[\/\-.](\d{1,2})");
        return m.Success ? $"{m.Groups[1].Value}-{int.Parse(m.Groups[2].Value):00}-{int.Parse(m.Groups[3].Value):00}" : "";
    }
    private static string LsxKey(string? v) => Regex.Replace((v ?? "").Trim().ToUpperInvariant(), @"\s+", "");
    private static (int, int, string) SizeKey(string s) { var m = Regex.Match(s, @"^\s*(\d+)"); return m.Success ? (0, int.Parse(m.Groups[1].Value), s) : (1, 0, s); }
    private static int CmpSize(string a, string b) { var x = SizeKey(a); var y = SizeKey(b); var c = x.Item1.CompareTo(y.Item1); if (c != 0) return c; c = x.Item2.CompareTo(y.Item2); return c != 0 ? c : string.CompareOrdinal(x.Item3, y.Item3); }

    /// <summary>monthKey = "yyyy-mm" theo ngày xuất của PHIẾU (chỉ phiếu xuất khẩu).</summary>
    public static Data Build(IEnumerable<Phieu> phieus, string monthKey)
    {
        var byP = new Dictionary<string, Group>(); var order = new List<Group>(); var rowIdx = new Dictionary<(Group, string), int>();
        foreach (var p in phieus)
        {
            if (!p.Loai.StartsWith("xuat_khau") || !DayKeyOf(p).StartsWith(monthKey)) continue;
            var pk = p.SoPhieu.Length > 0 ? p.SoPhieu : "#" + p.Id;
            foreach (var m in p.Mh) foreach (var r in m.DongList)
            {
                var kien = Num(r.Kien); if (kien == 0) continue;
                var lsx = (m.Lsx.Length > 0 ? m.Lsx : p.Lsx).Trim(); var ngay = DayOfRow(r.Date); var size = r.Size.Trim().Length > 0 ? r.Size.Trim() : "(không có size)";
                if (!byP.TryGetValue(pk, out var g)) { byP[pk] = g = new Group { SoPhieu = p.SoPhieu, Xuat = DayKeyOf(p), Khach = p.Khach }; order.Add(g); }
                var rk = LsxKey(lsx) + "|" + ngay + "|" + size;
                if (rowIdx.TryGetValue((g, rk), out var i)) g.Rows[i] = g.Rows[i] with { Kien = g.Rows[i].Kien + kien };
                else { rowIdx[(g, rk)] = g.Rows.Count; g.Rows.Add(new Row(lsx, ngay, size, kien)); }
                g.Tong += kien;
            }
        }
        foreach (var g in order)
            g.Rows = g.Rows.OrderBy(r => LsxKey(r.Lsx), StringComparer.Ordinal).ThenBy(r => r.Ngay, StringComparer.Ordinal).ThenBy(r => r.Size, Comparer<string>.Create(CmpSize)).ToList();
        var groups = order.OrderBy(g => g.SoPhieu, Comparer<string>.Create(PhieuKhoCore.NaturalCmp)).ThenBy(g => g.Xuat, StringComparer.Ordinal).ToList();
        var bySize = new Dictionary<string, double>();
        foreach (var g in groups) foreach (var r in g.Rows) bySize[r.Size] = bySize.GetValueOrDefault(r.Size) + r.Kien;
        return new Data(monthKey, groups, groups.Sum(g => g.Tong), bySize.OrderBy(p => p.Key, Comparer<string>.Create(CmpSize)).Select(p => new SizeTotal(p.Key, p.Value)).ToList());
    }

    private static string FmtD(string k) => k.Length >= 10 ? $"{k[8..10]}/{k[5..7]}/{k[0..4]}" : "";
    public static string FileName(string mk) => $"Thong_ke_xuat_khau_{mk[5..7]}-{mk[0..4]}.xlsx";

    public static void Export(Data d, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet($"Tháng {d.MonthKey[5..7]}-{d.MonthKey[0..4]}");
        const int N = 8;
        var head = ws.Range(1, 1, 1, N).Merge(); head.Value = $"THỐNG KÊ XUẤT KHẨU THÁNG {d.MonthKey[5..7]}/{d.MonthKey[0..4]} — KHO LẠNH AN AN";
        head.Style.Font.SetBold().Font.SetFontSize(14); head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        var sub = ws.Range(2, 1, 2, N).Merge(); sub.Value = "(Theo phiếu xuất khẩu đã lưu)"; sub.Style.Font.Italic = true; sub.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        var hdr = new[] { "STT", "LSX", "Khách hàng", "Số phiếu", "Ngày xuất", "Ngày sản xuất", "Size", "Số lượng (kiện)" };
        for (var i = 0; i < N; i++)
        {
            var c = ws.Cell(4, i + 1); c.Value = hdr[i];
            c.Style.Font.SetBold().Font.SetFontColor(XLColor.White); c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        var r = 5; var stt = 0; var tongRows = new List<int>();
        void Gop(int col, int a, int b, bool center)
        {
            if (b > a) ws.Range(a, col, b, col).Merge();
            ws.Cell(a, col).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            if (center) ws.Cell(a, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        foreach (var g in d.Groups)
        {
            var start = r; stt++;
            foreach (var x in g.Rows)
            {
                ws.Cell(r, 1).Value = stt; ws.Cell(r, 2).Value = x.Lsx; ws.Cell(r, 3).Value = g.Khach; ws.Cell(r, 4).Value = g.SoPhieu;
                ws.Cell(r, 5).Value = FmtD(g.Xuat); ws.Cell(r, 6).Value = FmtD(x.Ngay); ws.Cell(r, 7).Value = x.Size; ws.Cell(r, 8).Value = x.Kien;
                ws.Cell(r, 8).Style.NumberFormat.Format = "#,##0"; ws.Cell(r, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Range(r, 1, r, N).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                for (var c = 1; c <= N; c++) ws.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                r++;
            }
            var end = r - 1;
            Gop(1, start, end, true); Gop(3, start, end, false); Gop(4, start, end, true); Gop(5, start, end, true);
            var a = start;
            for (var k = start; k <= end; k++) { var last = k == end || g.Rows[k - start + 1].Lsx != g.Rows[a - start].Lsx; if (last) { Gop(2, a, k, false); a = k + 1; } }
            a = start;
            for (var k = start; k <= end; k++)
            {
                var n = k == end ? null : g.Rows[k - start + 1]; var f = g.Rows[a - start];
                var last = n is null || n.Lsx != f.Lsx || n.Ngay != f.Ngay;
                if (last) { Gop(6, a, k, true); a = k + 1; }
            }
            ws.Range(r, 1, r, 7).Merge().Value = "Tổng"; ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Cell(r, 8).FormulaA1 = $"SUM(H{start}:H{r - 1})"; ws.Cell(r, 8).Style.NumberFormat.Format = "#,##0";
            for (var c = 1; c <= N; c++) { ws.Cell(r, c).Style.Font.Bold = true; ws.Cell(r, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7"); ws.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin; }
            tongRows.Add(r); r++;
        }
        ws.Range(r, 1, r, 7).Merge().Value = "TỔNG THÁNG"; ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell(r, 8).FormulaA1 = tongRows.Count > 0 ? string.Join("+", tongRows.Select(x => "H" + x)) : "0"; ws.Cell(r, 8).Style.NumberFormat.Format = "#,##0";
        for (var c = 1; c <= N; c++) { ws.Cell(r, c).Style.Font.Bold = true; ws.Cell(r, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699"); ws.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin; }
        double[] w = { 7, 16, 22, 16, 14, 14, 18, 18 };
        for (var i = 0; i < w.Length; i++) ws.Column(i + 1).Width = w[i];
        ws.SheetView.FreezeRows(4);
        var w2 = wb.AddWorksheet("Tổng theo Size");
        var h2 = new[] { "STT", "Size", "Số lượng (kiện)" };
        for (var i = 0; i < 3; i++) { var c = w2.Cell(1, i + 1); c.Value = h2[i]; c.Style.Font.SetBold().Font.SetFontColor(XLColor.White); c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78"); c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; }
        for (var j = 0; j < d.BySize.Count; j++)
        {
            w2.Cell(j + 2, 1).Value = j + 1; w2.Cell(j + 2, 2).Value = d.BySize[j].Size; w2.Cell(j + 2, 3).Value = d.BySize[j].Kien; w2.Cell(j + 2, 3).Style.NumberFormat.Format = "#,##0";
            for (var c = 1; c <= 3; c++) w2.Cell(j + 2, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        double[] w3 = { 7, 18, 18 };
        for (var i = 0; i < 3; i++) w2.Column(i + 1).Width = w3[i];
        wb.SaveAs(path);
    }
}

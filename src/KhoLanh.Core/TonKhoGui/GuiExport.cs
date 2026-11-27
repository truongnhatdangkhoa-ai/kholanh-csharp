using System.Reflection;
using System.Text.RegularExpressions;
using KhoLanh.Core.TonKho;
using static KhoLanh.Core.TonKho.XlsxOut;

namespace KhoLanh.Core.TonKhoGui;

/// <summary>Các file Excel xuất ra của Tồn kho gửi (cùng tên sheet / cột như app cũ).</summary>
public static class GuiExport
{
    private const string White = "FFFFFFFF", Navy = "FF1F4E78", TotalFill = "FFDDEBF7", GroupFill = "FFD6EAD3";
    public static string Stamp(DateTime t) => $"{t:yyyyMMdd}_{t:HHmm}";

    private static XlsxOut Table(string sheet, List<object?[]> aoa, int cols)
    {
        var ws = new Sheet { Name = sheet, Aoa = aoa, Widths = AutoWidths(aoa) };
        for (var r = 0; r < aoa.Count; r++) for (var c = 0; c < cols; c++) ws.Set(r, c, r == 0 ? new St(true, White, Navy, "center", true) : new St(false, null, null, "left"));
        ws.AutoFilter = (0, 0, cols - 1); ws.FreezeRows = 1;
        return new XlsxOut { Sheets = { ws } };
    }

    /// <summary>du-lieu.xlsx / tom-*.xlsx: 10 cột TARGET_COLS, Đặc tính suy từ Tên hàng, Trọng Lượng định dạng 2 số lẻ.</summary>
    public static XlsxOut Rows(GuiData d, IEnumerable<GuiRow> rows, string sheet)
    {
        var aoa = new List<object?[]> { GuiData.TargetCols.Select(c => (object?)c.Label).ToArray() };
        foreach (var r in rows)
            aoa.Add(GuiData.TargetCols.Select(c => c.Key switch
            {
                "trongLuong" => (object?)GuiData.FormatWeight(r.TrongLuong), "dacTinh" => d.DeriveDacTinh(r.TenHang, r.SoLo), "soKien" => r.SoKien, _ => GuiData.Get(r, c.Key),
            }).ToArray());
        return Table(sheet, aoa, GuiData.TargetCols.Length);
    }
    public static string GroupFileName(string id) => id switch { "nl" => "tom-nguyen-lieu.xlsx", "5" => "tom-hlso.xlsx", "4" => "tom-giam-sat.xlsx", "3" => "tom-gia-cong.xlsx", "ad" => "tom-an-do.xlsx", _ => "tom-ecuador.xlsx" };
    public static string GroupSheetName(string id) => id switch { "nl" => "Tom Nguyen Lieu", "5" => "Tom HLSO", "4" => "Tom Giam Sat", "3" => "Tom Gia Cong", "ad" => "Tom An Do", _ => "Tom Ecuador" };

    public static XlsxOut DacTinhManual(GuiData d) => Table("Dac tinh nhap tay", new List<object?[]> { new object?[] { "Diễn giải đặc tính", "Đặc tính" } }.Concat(d.DtManual.Select(r => new object?[] { r.MaHang, r.DacTinh })).ToList(), 2);
    public static XlsxOut DacTinhList(GuiData d)
    {
        var aoa = new List<object?[]> { new object?[] { "Đặc tính", "Diễn giải đặc tính", "Nhóm" } };
        foreach (var v in d.UniqueDacTinh().Values) aoa.Add(new object?[] { v, d.DienGiai(v), d.DtGroupMap.GetValueOrDefault(v, "") });
        return Table("Dac tinh", aoa, 3);
    }
    public static XlsxOut DacTinhGroups(GuiData d)
    {
        var aoa = new List<object?[]> { new object?[] { "Nhóm", "Đặc tính đã gộp" } };
        foreach (var g in GuiData.DtGroups)
        {
            var items = d.DtGroupMap.Where(kv => kv.Value == g).Select(kv => kv.Key).ToList();
            items = AnAnData.StableSort(items, (a, b) => JsText.ViNumericCompare(a, b));
            aoa.Add(new object?[] { g, string.Join(", ", items) });
        }
        return Table("Gop dac tinh", aoa, 2);
    }

    // ---------- Bảng Tổng Hợp (Báo Cáo)
    public static string BangTongHopFileName(DateTime t) => $"BangTongHop_{t:yyyyMMdd}_{t:HHmm}.xlsx";
    /// <summary>1.126 "Xuất file HLSO tự chọn" (như Tồn kho An An): sheet Bảng Tổng Hợp chỉ gồm nhóm đã chọn (summary) + 1 sheet dữ liệu mỗi nhóm đã chọn (pick). Không có sheet nào → null.</summary>
    public static XlsxOut? TuChon(GuiData d, string reportDate, bool summary, IReadOnlyCollection<string> pick)
    {
        var x = new XlsxOut();
        if (summary && pick.Count > 0) x.Sheets.Add(BangTongHop(d.ComputePivot(pick.ToHashSet()), reportDate).Sheets[0]);
        foreach (var g in GuiData.Groups)
            if (pick.Contains(g.Id)) x.Sheets.Add(Rows(d, d.GroupRows(g.Id), GroupSheetName(g.Id)).Sheets[0]);
        return x.Sheets.Count == 0 ? null : x;
    }
    public static string TuChonFileName(DateTime t) => $"BangTongHop_TuChon_{t:yyyyMMdd}_{t:HHmm}.xlsx";

    public static XlsxOut BangTongHop(GuiData.Pivot p, string reportDate)
    {
        var used = GuiData.DtGroups.Where(g => (p.Grand.ByGroup.TryGetValue(g, out var v) && v != 0) || p.Cats.Any(c => c.B.ByGroup.TryGetValue(g, out var x) && x != 0)).ToList();
        var colCount = used.Count + 3;
        object?[] Vals(string label, GuiData.Bucket b, bool mirror)
        {
            var a = new List<object?> { label };
            foreach (var g in used) a.Add(b.ByGroup.TryGetValue(g, out var v) && v != 0 ? v : "");
            a.Add(b.Tong != 0 ? b.Tong : ""); a.Add(mirror ? label : ""); return a.ToArray();
        }
        var ws = new Sheet { Name = "Bang Tong Hop", FreezeRows = 2 };
        var kinds = new List<string>();
        ws.Aoa.Add(new object?[] { $"Ngày báo cáo: {reportDate}" }); kinds.Add("title");
        ws.Aoa.Add(new List<object?> { "Size" }.Concat(used).Concat(new object?[] { "Tổng", "Size" }).ToArray()); kinds.Add("header");
        ws.Aoa.Add(Vals("TỔNG TỒN", p.Grand, false)); kinds.Add("total");
        foreach (var c in p.Cats) { ws.Aoa.Add(Vals(c.Label, c.B, true)); kinds.Add("group"); foreach (var s in c.Sizes) { ws.Aoa.Add(Vals(s.Size, s.B, true)); kinds.Add("size"); } }
        ws.Aoa.Add(Vals("TỔNG TỒN", p.Grand, false)); kinds.Add("total");
        for (var r = 0; r < ws.Aoa.Count; r++)
        {
            var k = kinds[r];
            if (k == "title") { ws.Set(r, 0, new St(true, null, null, "left", false, false, null, false, "Times New Roman", 16)); continue; }
            for (var c = 0; c < colCount; c++)
            {
                var label = c == 0 || c == colCount - 1; var num = !label && c != colCount - 1;
                var bold = k != "size" || c == colCount - 2;
                ws.Set(r, c, k switch
                {
                    "header" => new St(true, White, Navy, "center", false, false, null, true),
                    "total" => new St(true, null, TotalFill, label ? "left" : "right", false, true, num ? "#,##0.00" : null, true),
                    "group" => new St(true, null, GroupFill, label ? "left" : "right", false, false, num ? "#,##0.00" : null, true),
                    _ => new St(bold, null, null, label ? "right" : "right", false, false, num ? "#,##0.00" : null, true),
                });
            }
        }
        ws.Merges.Add((0, 0, 0, colCount - 1));
        ws.AutoFilter = (1, 0, colCount - 1);
        ws.Widths = AutoWidths(ws.Aoa, 8, 45);
        return new XlsxOut { Sheets = { ws } };
    }

    // ---------- Phiếu xuất ("PHIẾU YÊU CẦU XUẤT HÀNG")
    public static string SlipFileName(GuiStore.Slip p) { var n = Regex.Replace(Regex.Replace(p.MaPhieu ?? "", @"[\\/:*?""<>|\r\n]+", " "), @"\s+", " ").Trim(); return (n.Length > 0 ? n : "phieu-xuat") + ".xlsx"; }
    public static byte[] SlipWorkbook(GuiStore.Slip p)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook(); var ws = wb.AddWorksheet("PYC");
        ws.PageSetup.PageOrientation = ClosedXML.Excel.XLPageOrientation.Portrait;
        ws.Style.Font.FontName = "Times New Roman"; ws.Style.Font.FontSize = 16;
        ws.Range("A3:G3").Merge().Value = "PHIẾU YÊU CẦU XUẤT HÀNG"; ws.Cell("A3").Style.Font.Bold = true; ws.Cell("A3").Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        ws.Range("A6:G6").Merge().Value = "XUẤT HÀNG THEO THỨ TỰ PHIẾU YÊU CẦU - GHI RÕ THÔNG TIN SIZE, QUY CÁCH, GHI CHÚ TRÊN CARGO";
        ws.Cell("A6").Style.Font.Bold = true; ws.Cell("A6").Style.Font.FontColor = ClosedXML.Excel.XLColor.Red; ws.Cell("A6").Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        try
        {
            using var s = typeof(GuiExport).Assembly.GetManifestResourceStream("KhoLanh.Core.ChungTu.logo-long-an.png");
            if (s is not null) { var pic = ws.AddPicture(s).MoveTo(ws.Cell("A1")); pic.Width = 250; pic.Height = 79; }   // logo 375×119 → giữ tỉ lệ
        }
        catch { /* không có logo thì bỏ qua */ }
        var headers = new[] { "STT", "Mã hàng", "Tên hàng", "Số phiếu nhập", "GHI CHÚ - LÔ ( IN TRÊN THÙNG )", "Kích cỡ/size", "Số lượng \n(thùng/bao)" };
        ws.Row(7).Height = 37.5;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(7, c + 1); cell.Value = headers[c]; cell.Style.Font.Bold = true; cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center; cell.Style.Alignment.WrapText = true; cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        }
        var first = 8; var qtyFmt = "_(* #,##0_);_(* \\(#,##0\\);_(* \"-\"??_);_(@_)";
        for (var i = 0; i < p.Items.Count; i++)
        {
            var it = p.Items[i]; var r = first + i;
            ws.Cell(r, 1).Value = i + 1; ws.Cell(r, 2).Value = it.MaHang; ws.Cell(r, 3).Value = it.TenHang; ws.Cell(r, 4).Value = it.PhieuNhap; ws.Cell(r, 5).Value = it.SoLo; ws.Cell(r, 6).Value = it.Size;
            if (it.XuatKg is double kg && kg != 0) ws.Cell(r, 7).Value = GuiData.FormatNumber(kg) + " kg"; else { ws.Cell(r, 7).Value = it.SoLuongXuat; ws.Cell(r, 7).Style.NumberFormat.Format = qtyFmt; }
            for (var c = 1; c <= 7; c++) ws.Cell(r, c).Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            ws.Cell(r, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            foreach (var c in new[] { 2, 3, 4, 6 }) ws.Cell(r, c).Style.Alignment.WrapText = true;
        }
        var tr = first + p.Items.Count; ws.Row(tr).Height = 22.5;
        for (var c = 1; c <= 7; c++) { var cell = ws.Cell(tr, c); cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin; cell.Style.Font.FontSize = 18; cell.Style.Font.Bold = true; }
        ws.Cell(tr, 3).Value = "TỔNG";
        if (p.Items.Count > 0) { ws.Cell(tr, 7).FormulaA1 = $"SUM(G{first}:G{tr - 1})"; ws.Cell(tr, 7).Style.NumberFormat.Format = qtyFmt; } else ws.Cell(tr, 7).Value = 0;
        ws.Range(tr + 2, 1, tr + 2, 3).Merge().Value = "Người giao hàng"; ws.Cell(tr + 2, 1).Style.Font.Bold = true; ws.Cell(tr + 2, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        ws.Range(tr + 3, 1, tr + 3, 3).Merge().Value = "(Ký và ghi rõ họ tên)"; ws.Cell(tr + 3, 1).Style.Font.Italic = true; ws.Cell(tr + 3, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        var min = new[] { 4.5, 10, 14, 10, 12, 8, 9 }; var max = new[] { 6.0, 30, 55, 20, 40, 16, 16 };
        var lens = headers.Select(h => h.Length).ToArray();
        for (var i = 0; i < p.Items.Count; i++)
        {
            var it = p.Items[i];
            var vals = new[] { (i + 1).ToString(), it.MaHang, it.TenHang, it.PhieuNhap, it.SoLo, it.Size, it.XuatKg is double k && k != 0 ? GuiData.FormatNumber(k) + " kg" : GuiData.FormatNumber(it.SoLuongXuat) };
            for (var c = 0; c < 7; c++) lens[c] = Math.Max(lens[c], vals[c].Length);
        }
        lens[2] = Math.Max(lens[2], 4); lens[6] = Math.Max(lens[6], 8);
        for (var c = 0; c < 7; c++) ws.Column(c + 1).Width = Math.Min(max[c], Math.Max(min[c], lens[c] * (16.0 / 11) * 1.1 + 2));
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }
}

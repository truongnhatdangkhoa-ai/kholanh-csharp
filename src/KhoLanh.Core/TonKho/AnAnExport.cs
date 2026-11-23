using static KhoLanh.Core.TonKho.JsText;
using St = KhoLanh.Core.TonKho.XlsxOut.St;

namespace KhoLanh.Core.TonKho;

/// <summary>Các file Excel của Tồn kho An An — cùng nội dung, cùng kiểu chữ / màu / định dạng như bản WebView2.</summary>
public static class AnAnExport
{
    private const string NumFmt = "#,##0.00", NumFmtInt = "#,##0", Navy = "1F4E78", Total = "DDEBF7", White = "FFFFFF";

    public static string Stamp(DateTime t) => $"{t:yyyyMMdd}_{t:HHmm}";

    /// <summary>Một cột hiển thị: cột dữ liệu (Ci), cột ảo "Tổng KL (kg)" (Virtual) hoặc "Loại hàng" (4 ký tự đầu Mã hàng).</summary>
    public sealed record DCol(string Label, int Ci, bool Virtual = false, bool LoaiHang = false);

    private static bool IsNumCol(DCol c) => AnAnData.IsNumericCol(c.Label) || c.Virtual;

    /// <summary>styleExportedSheet(ws, displayCols, totalRowIdx, {headerRowIdx, subtotalRowIdx, plain}).</summary>
    public static void StyleSheet(XlsxOut.Sheet ws, IList<(string Label, bool Num)> cols, int totalRowIdx, int headerRowIdx = 0, int? subtotalRowIdx = null, bool plain = false)
    {
        for (var r = 0; r <= totalRowIdx; r++)
            for (var c = 0; c < cols.Count; c++)
            {
                var num = cols[c].Num;
                St st;
                if (subtotalRowIdx == r) st = new St(true, White, Navy, num ? "right" : "center", NumFmt: num ? NumFmt : null);
                else if (r == headerRowIdx) st = plain ? new St(true, "000000", null, "center", true, NumFmt: NumFmt) : new St(true, White, Navy, "center", true, NumFmt: NumFmt);
                else if (r == totalRowIdx) st = new St(true, null, plain ? null : Total, num ? "right" : "left", TopMedium: true, NumFmt: num ? NumFmt : null);
                else st = new St(false, null, null, num ? "right" : "left", NumFmt: num ? NumFmt : null);
                ws.Set(r, c, st);
                EnsureCell(ws, r, c);
            }
        ws.AutoFilter = (headerRowIdx, 0, cols.Count - 1);
        ws.FreezeRows = headerRowIdx + 1;
    }

    private static void EnsureCell(XlsxOut.Sheet ws, int r, int c)
    {
        while (ws.Aoa.Count <= r) ws.Aoa.Add(Array.Empty<object?>());
        if (ws.Aoa[r].Length <= c) { var a = ws.Aoa[r]; Array.Resize(ref a, c + 1); ws.Aoa[r] = a; }
        ws.Aoa[r][c] ??= "";
    }

    private static object CellValue(AnAnData d, string modId, int i, DCol col, (int Qty, int W)? basis)
    {
        if (col.Virtual) return basis is not null && d.RowWeight(i, basis.Value) is double v ? v : "";
        var val = d.Cell(i, col.Ci);
        if (col.LoaiHang) return Take4(JsTrim(val));
        if (AnAnData.IsNumericCol(col.Label) && val.Length > 0) { var n = LooseNum(val); return double.IsNaN(n) ? val : n; }
        return AnAnData.SizePrefix(modId, col.Label, val);
    }

    private static string Take4(string s) => s.Length <= 4 ? s : s[..4];

    private static object?[] TotalsRow(AnAnData d, IList<int> rows, IList<DCol> cols, (int Qty, int W)? basis, out List<object?> totals)
    {
        totals = cols.Select(col =>
        {
            if (col.Virtual) return basis is null ? null : (object?)d.SumWeight(rows, basis.Value);
            if (!AnAnData.IsSummableCol(col.Label)) return "__undef";
            return d.SumCol(rows, col.Ci);
        }).ToList();
        var placed = false; var t = totals;
        return cols.Select((col, idx) =>
        {
            var x = t[idx];
            if (x is "__undef") { if (!placed) { placed = true; return (object?)$"Tổng ({En(rows.Count, 0, 0)} dòng)"; } return ""; }
            return x ?? "";
        }).ToArray();
    }

    private static List<DCol> ModuleCols(AnAnData d, bool export)
    {
        var cols = (export ? d.ExportCols() : d.FilterCols()).Select(c => new DCol(c.Label, c.Ci)).ToList();
        if (d.WeightBasis() is not null) cols.Insert(cols.FindIndex(c => c.Label == "Trọng lượng") + 1, new DCol("Tổng KL (kg)", -1, true));
        return cols;
    }

    // ------------------------------------------------------------------ Dữ Liệu

    /// <summary>DuLieu_{stamp}.xlsx — sheet "Dữ Liệu": mọi cột, các dòng đang lọc / sắp xếp, dòng Tổng.</summary>
    public static XlsxOut DuLieu(AnAnData d, IList<int> rows)
    {
        var cols = d.Headers.Select((h, ci) => new DCol(h, ci)).ToList();
        var ws = new XlsxOut.Sheet { Name = "Dữ Liệu" };
        ws.Aoa.Add(cols.Select(c => (object?)c.Label).ToArray());
        foreach (var i in rows)
            ws.Aoa.Add(cols.Select(col => { var v = d.Cell(i, col.Ci); if (AnAnData.IsNumericCol(col.Label) && v.Length > 0) { var n = LooseNum(v); return double.IsNaN(n) ? (object?)v : n; } return v; }).ToArray());
        var placed = false;
        ws.Aoa.Add(cols.Select(col =>
        {
            if (AnAnData.IsSummableCol(col.Label)) { double s = 0; var any = false; foreach (var i in rows) { var n = LooseNum(d.Cell(i, col.Ci)); if (!double.IsNaN(n)) { s += n; any = true; } } return any ? (object?)s : ""; }
            if (!placed) { placed = true; return $"Tổng ({En(rows.Count, 0, 0)} dòng)"; }
            return "";
        }).ToArray());
        ws.Widths = XlsxOut.AutoWidths(ws.Aoa);
        StyleSheet(ws, cols.Select(c => (c.Label, AnAnData.IsNumericCol(c.Label))).ToList(), ws.Aoa.Count - 1);
        return new XlsxOut { Sheets = { ws } };
    }

    // ------------------------------------------------------------------ 1 bảng Phân Loại

    /// <summary>{Tên bảng}_{stamp}.xlsx — các dòng đang hiện của 1 bảng (đã lọc / sắp xếp).</summary>
    public static XlsxOut Module(AnAnData d, string modId, IList<int> rows)
    {
        var cols = ModuleCols(d, true); var basis = d.WeightBasis();
        var ws = new XlsxOut.Sheet { Name = Trunc31(AnAnData.LabelOf(modId)) };
        ws.Aoa.Add(cols.Select(c => (object?)c.Label).ToArray());
        foreach (var i in rows) ws.Aoa.Add(cols.Select(c => (object?)CellValue(d, modId, i, c, basis)).ToArray());
        ws.Aoa.Add(TotalsRow(d, rows, cols, basis, out _));
        ws.Widths = XlsxOut.AutoWidths(ws.Aoa);
        StyleSheet(ws, cols.Select(c => (c.Label, IsNumCol(c))).ToList(), ws.Aoa.Count - 1);
        return new XlsxOut { Sheets = { ws } };
    }

    public static string ModuleFileName(string modId, DateTime t) => System.Text.RegularExpressions.Regex.Replace(AnAnData.LabelOf(modId), @"\s+", "_") + "_" + Stamp(t) + ".xlsx";

    private static string Trunc31(string s) => s.Length > 31 ? s[..31] : s;

    // ------------------------------------------------------------------ Báo cáo tổng tồn (7 module / HLSO)

    public static readonly string[] HlsoExportIds = { "hlso", "tomgc", "tomnk", "tomnl", "tomgs" };

    /// <summary>
    /// BaoCao_TongTon_7Module_{stamp}.xlsx (only = null) hoặc BaoCao_TongTon_HLSO_{stamp}.xlsx (only = 5 loại tôm, pick = sheet đã tick, summary = có sheet Tổng Tồn).
    /// Trả null nếu không có sheet nào.
    /// </summary>
    public static XlsxOut? BaoCaoAll(AnAnData d, string reportDate, ISet<string>? only = null, ISet<string>? pick = null, bool summary = true)
    {
        var x = new XlsxOut(); var count = 0;
        if (summary) { x.Sheets.Add(TongTonSheet(d, reportDate, only)); if (pick is not null) count++; }
        foreach (var m in AnAnData.FilterModules)
        {
            if (only is not null && !only.Contains(m.Id)) continue;
            if (pick is not null && !pick.Contains(m.Id)) continue;
            x.Sheets.Add(ModuleSheet(d, m.Id)); count++;
        }
        return count == 0 ? null : x;
    }

    private static XlsxOut.Sheet ModuleSheet(AnAnData d, string modId)
    {
        var cols = ModuleCols(d, true); var basis = d.WeightBasis();
        if (modId is "tpc" or "tptam")
        {
            var ma = d.Col("Mã hàng"); var kh = d.Col("Khách hàng");
            if (kh != -1) { var at = cols.FindIndex(c => c.Label == "Size"); cols.Insert(at == -1 ? cols.Count : at + 1, new DCol("Khách hàng", kh)); }
            if (ma != -1) { var at = cols.FindIndex(c => c.Label == "Tên hàng"); cols.Insert(at == -1 ? cols.Count : at + 1, new DCol("Loại hàng", ma, LoaiHang: true)); }
        }
        var rows = d.BaseRows(modId);
        var ws = new XlsxOut.Sheet { Name = Trunc31(AnAnData.LabelOf(modId)) };
        if (cols.Count == 0)
        {
            ws.Aoa.Add(new object?[] { "(Không tìm thấy cột dữ liệu)" });
            return ws;
        }
        var totalsRow = TotalsRow(d, rows, cols, basis, out var totals);
        ws.Aoa.Add(cols.Select((c, idx) => AnAnData.IsSummableCol(c.Label) || c.Virtual ? (object?)(totals[idx] is double t ? t : 0d) : "").ToArray());
        ws.Aoa.Add(cols.Select(c => (object?)c.Label).ToArray());
        foreach (var i in rows) ws.Aoa.Add(cols.Select(c => (object?)CellValue(d, modId, i, c, basis)).ToArray());
        ws.Aoa.Add(totalsRow);
        ws.Widths = XlsxOut.AutoWidths(ws.Aoa);
        StyleSheet(ws, cols.Select(c => (c.Label, IsNumCol(c))).ToList(), ws.Aoa.Count - 1, headerRowIdx: 1, subtotalRowIdx: 0);
        if (rows.Count > 0)
        {
            int first = 3, last = 3 + rows.Count - 1;
            for (var idx = 0; idx < cols.Count; idx++)
            {
                if (!(AnAnData.IsSummableCol(cols[idx].Label) || cols[idx].Virtual)) continue;
                var L = XlsxOut.ColName(idx);
                ws.Formulas[(0, idx)] = $"SUBTOTAL(109,{L}{first}:{L}{last})";
            }
        }
        return ws;
    }

    public const string TongTonTitle = "BÁO CÁO TỒN KHO AN AN ĐẾN", TongTonMain = "Tồn Kho An An", TongTonMainTotal = "TỔNG TỒN AN AN",
        TongTonKhoGui = "Tồn Kho Gửi", TongTonKhoGuiRow = "Tồn kho gởi kho tại Kho Transimex Long An", TongTonGrand = "TỔNG TỒN KHO AN AN + KHO GỬI";

    private static XlsxOut.Sheet TongTonSheet(AnAnData d, string date, ISet<string>? only)
    {
        var onlyLabels = only is null ? null : AnAnData.FilterModules.Where(m => only.Contains(m.Id)).Select(m => m.Label).ToHashSet();
        var rows = d.TongTonRows().Where(r => onlyLabels is null || (!r.Manual && onlyLabels.Contains(r.Label))).ToList();
        var total = AnAnData.TotalOf(rows);
        var ws = new XlsxOut.Sheet { Name = "Tổng Tồn" };
        var aoa = ws.Aoa;
        aoa.Add(new object?[] { TongTonTitle, $"Ngày : {date}" });
        aoa.Add(new object?[] { TongTonMain, "Tổng trọng lượng (kg)" });
        var headerRow = 1;
        foreach (var r in rows) aoa.Add(new object?[] { r.Label, r.Weight is double w ? w : "" });
        aoa.Add(new object?[] { TongTonMainTotal, total is double t ? t : "" });
        var mainTotal = aoa.Count - 1;
        var kg = d.KhoGui;
        aoa.Add(new object?[] { "", "" }); var blank1 = aoa.Count - 1;
        aoa.Add(new object?[] { TongTonKhoGui, "" }); var kgHeader = aoa.Count - 1;
        aoa.Add(new object?[] { TongTonKhoGuiRow, kg is double k ? k : "" });
        aoa.Add(new object?[] { "", "" }); var blank2 = aoa.Count - 1;
        double? grand = total is null && kg is null ? null : (total ?? 0) + (kg ?? 0);
        aoa.Add(new object?[] { TongTonGrand, grand is double g ? g : "" }); var kgTotal = aoa.Count - 1;
        var legacy = aoa.Count;

        // Báo Cáo Tổng Hợp
        var maCi = d.Col("Mã hàng"); var netCi = d.ColExact("Tồn cuối (NET)"); var tcCi = d.ColExact("Tồn cuối");
        var thCols = new List<(string Label, int Ci, bool Num, bool Int, bool Loai, bool Bang)>
        {
            ("Tên hàng", d.Col("Tên hàng"), false, false, false, false), ("Mã hàng", maCi, false, false, false, false), ("Loại hàng", maCi, false, false, true, false),
            ("Size", d.Col("Size"), false, false, false, false), ("Khách hàng", d.Col("Khách hàng"), false, false, false, false), ("Đặc tính", d.Col("Đặc tính"), false, false, false, false),
            ("Hợp đồng", d.Col("Hợp đồng"), false, false, false, false), ("Tồn cuối (kiện)", tcCi, true, true, false, false), ("Tồn cuối (net)", netCi, true, false, false, false),
            ("Bảng", maCi, false, false, false, true),
        }.Where(c => c.Ci != -1).ToList();
        var rowMod = new Dictionary<int, string>();
        foreach (var id in AnAnData.FilterOrder) foreach (var i in d.BaseRows(id)) rowMod[i] = AnAnData.LabelOf(id);
        var entries = Enumerable.Range(0, d.Rows.Count).Where(i => onlyLabels is null || (rowMod.TryGetValue(i, out var l) && onlyLabels.Contains(l))).ToList();
        int thTitle = -1, thSub = -1, thHeader = -1, thTotal = -1;
        if (thCols.Count > 0 && entries.Count > 0)
        {
            aoa.Add(Array.Empty<object?>());
            var title = thCols.Select(_ => (object?)"").ToArray(); title[0] = "Báo Cáo Tổng Hợp"; aoa.Add(title); thTitle = aoa.Count - 1;
            double? SumCol(int ci) { double s = 0; var has = false; foreach (var i in entries) { var v = LooseNum(d.Cell(i, ci)); if (!double.IsNaN(v)) { s += v; has = true; } } return has ? s : null; }
            aoa.Add(thCols.Select(c => { if (!c.Num) return (object?)""; var s = SumCol(c.Ci); return s is null ? 0d : c.Int ? JsRound(s.Value) : s.Value; }).ToArray()); thSub = aoa.Count - 1;
            aoa.Add(thCols.Select(c => (object?)c.Label).ToArray()); thHeader = aoa.Count - 1;
            foreach (var i in entries)
                aoa.Add(thCols.Select(c =>
                {
                    if (c.Bang) return (object?)(rowMod.GetValueOrDefault(i) ?? "");
                    var raw = d.Cell(i, c.Ci);
                    if (c.Loai) return Take4(JsTrim(raw));
                    if (c.Num && raw.Length > 0) { var n = LooseNum(raw); if (double.IsNaN(n)) return raw; return c.Int ? JsRound(n) : n; }
                    return raw;
                }).ToArray());
            var placed = false;
            aoa.Add(thCols.Select(c =>
            {
                if (!c.Num) { if (!placed) { placed = true; return (object?)$"Tổng ({En(entries.Count, 0, 0)} dòng)"; } return ""; }
                var s = SumCol(c.Ci); return s is null ? "" : c.Int ? JsRound(s.Value) : s.Value;
            }).ToArray());
            thTotal = aoa.Count - 1;
        }
        ws.Widths = XlsxOut.AutoWidths(aoa);
        for (var r = 0; r < legacy; r++)
        {
            if (r == blank1 || r == blank2) continue;
            for (var c = 0; c < 2; c++)
            {
                var num = c == 1;
                St st = r == 0 ? new St(true, null, null, "center", true)
                    : r == headerRow || r == kgHeader ? new St(true, White, Navy, "center", true)
                    : r == mainTotal || r == kgTotal ? new St(true, null, Total, num ? "right" : "left", TopMedium: true, NumFmt: num ? NumFmt : null)
                    : new St(false, null, null, num ? "right" : "left", NumFmt: num ? NumFmt : null);
                ws.Set(r, c, st); EnsureCell(ws, r, c);
            }
        }
        if (thHeader != -1)
        {
            for (var r = thTitle; r <= thTotal; r++)
                for (var c = 0; c < thCols.Count; c++)
                {
                    var num = thCols[c].Num; var fmt = thCols[c].Int ? NumFmtInt : NumFmt;
                    St st = r == thTitle || r == thHeader ? new St(true, White, Navy, "center", true)
                        : r == thSub ? new St(true, White, Navy, num ? "right" : "center", NumFmt: num ? fmt : null)
                        : r == thTotal ? new St(true, null, Total, num ? "right" : "left", TopMedium: true, NumFmt: num ? fmt : null)
                        : new St(false, null, null, num ? "right" : "left", NumFmt: num ? fmt : null);
                    ws.Set(r, c, st); EnsureCell(ws, r, c);
                }
            int first = thHeader + 2, last = first + entries.Count - 1;
            for (var idx = 0; idx < thCols.Count; idx++)
            {
                if (!thCols[idx].Num) continue;
                var L = XlsxOut.ColName(idx);
                ws.Formulas[(thSub, idx)] = $"SUBTOTAL(109,{L}{first}:{L}{last})";
            }
            ws.AutoFilter = (thHeader, 0, thCols.Count - 1);
        }
        else ws.AutoFilter = (1, 0, 1);
        ws.FreezeRows = 1;
        return ws;
    }

    // ------------------------------------------------------------------ Bảng Tổng Hợp HLSO

    public sealed record CustomRow(string Label, string Kind, double?[] Cells, double? Total);

    /// <summary>BangTongHopHLSO_{stamp}.xlsx (custom = null) hoặc Bảng Tổng Hợp Tùy Chọn.</summary>
    public static XlsxOut HlsoSummary(AnAnData d, string reportDate, (string[] Columns, List<CustomRow> Rows)? custom = null)
    {
        var src = custom?.Columns ?? AnAnData.HlsoColumns;
        var raw = custom is { } cu
            ? cu.Rows.Select(r => (Display: r.Label, Kind: r.Kind, Title: r.Kind == "group-title", Total: r.Total is double t && double.IsFinite(t) ? r.Total : null, Vals: r.Cells.Select(v => v is double x && double.IsFinite(x) ? v : null).ToArray())).ToList()
            : d.HlsoRowsInStock().Select(r => (Display: r.Display, Kind: r.Kind, Title: r.Kind == "group-title", Total: d.HlsoTotal(r), Vals: AnAnData.HlsoColumns.Select(g => d.HlsoCell(r, g)).ToArray())).ToList();
        var kept = Enumerable.Range(0, src.Length).Where(gi => custom is not null || raw.Any(x => x.Vals[gi] is double v && !double.IsNaN(v))).ToList();
        double grand = 0; var grandHas = false;
        foreach (var x in raw) if (!x.Title && x.Total is double v && !double.IsNaN(v)) { grand += v; grandHas = true; }
        var header = new List<object?> { "Size" }; header.AddRange(kept.Select(g => (object?)src[g])); header.Add(custom is null ? "Tổng" : "Tổng chọn"); header.Add("Size");
        var ws = new XlsxOut.Sheet { Name = custom is null ? "Bang Tong Hop HLSO" : "Bang Tong Hop Tuy Chon" };
        var aoa = ws.Aoa;
        var title = new object?[header.Count]; title[0] = $"Ngày báo cáo: {reportDate}"; for (var i = 1; i < title.Length; i++) title[i] = "";
        aoa.Add(title); aoa.Add(header.ToArray());
        var kinds = new List<string> { "title", "header" };
        var colTot = kept.Select(_ => (Sum: 0.0, Has: false)).ToArray();
        foreach (var x in raw)
        {
            var row = new List<object?> { x.Display };
            for (var ki = 0; ki < kept.Count; ki++)
            {
                var g = x.Vals[kept[ki]];
                if (!x.Title && g is double v && !double.IsNaN(v)) colTot[ki] = (colTot[ki].Sum + v, true);
                row.Add(g is double gv ? gv : "");
            }
            row.Add(x.Total is double tv ? tv : ""); row.Add(x.Display);
            aoa.Add(row.ToArray()); kinds.Add(x.Kind);
        }
        var totalVals = new List<object?> { custom is null ? "TỔNG TỒN" : "TỔNG CHỌN" };
        totalVals.AddRange(colTot.Select(c => c.Has ? (object?)c.Sum : ""));
        totalVals.Add(grandHas ? grand : ""); totalVals.Add("");
        aoa.Insert(2, totalVals.ToArray()); kinds.Insert(2, "total");
        aoa.Add(totalVals.ToArray()); kinds.Add("total");
        ws.Widths = XlsxOut.AutoWidths(aoa);
        var n = header.Count; var totalRow = aoa.Count - 1;
        StyleSheet(ws, header.Select(h => ((string)h!, true)).ToList(), totalRow, headerRowIdx: 1);
        for (var c = 0; c < n; c++) { ws.Set(0, c, new St(true, null, null, "left")); }
        ws.Merges.Add((0, 0, 0, n - 1));
        for (var c = 0; c < n; c++) { var num = c > 0 && c < n - 1; ws.Set(2, c, new St(true, null, Total, num ? "right" : "left", TopMedium: true, NumFmt: num ? NumFmt : null)); }
        for (var r = 1; r < totalRow; r++)
        {
            var k = kinds[r]; if (k is not ("static" or "group-title")) continue;
            var fill = k == "static" ? "FCE9B0" : "D6EAD3";
            for (var c = 0; c < n; c++) { var num = c > 0 && c < n - 1; ws.Set(r, c, new St(true, null, fill, num ? "right" : "left", NumFmt: num ? NumFmt : null)); }
        }
        var lastIdx = n - 2;
        for (var r = 1; r < totalRow; r++) if (ws.Styles.TryGetValue((r, lastIdx), out var st)) ws.Set(r, lastIdx, st with { Bold = true });
        return new XlsxOut { Sheets = { ws } };
    }

    // ------------------------------------------------------------------ Đặc Tính

    private static XlsxOut OneTable(string sheet, List<object?[]> aoa, int cols)
    {
        var ws = new XlsxOut.Sheet { Name = sheet, Aoa = aoa };
        ws.Widths = XlsxOut.AutoWidths(aoa);
        for (var r = 0; r < aoa.Count; r++)
            for (var c = 0; c < cols; c++)
            { ws.Set(r, c, r == 0 ? new St(true, White, Navy, "center", true) : new St(false, null, null, "left")); EnsureCell(ws, r, c); }
        ws.AutoFilter = (0, 0, cols - 1); ws.FreezeRows = 1;
        return new XlsxOut { Sheets = { ws } };
    }

    /// <summary>DacTinh_{stamp}.xlsx — danh sách đặc tính (sheet "Dac Tinh").</summary>
    public static XlsxOut DacTinh(List<AnAnData.DtRow> rows)
    {
        var aoa = new List<object?[]> { new object?[] { "Đặc Tính" } };
        aoa.AddRange(rows.Select(r => new object?[] { r.Label }));
        return OneTable("Dac Tinh", aoa, 1);
    }

    /// <summary>GopDacTinh_{stamp}.xlsx — Nhóm + Đặc Tính đã gộp (nhóm trống vẫn có 1 dòng).</summary>
    public static XlsxOut GopDacTinh(AnAnData d)
    {
        var aoa = new List<object?[]> { new object?[] { "Nhóm", "Đặc Tính" } };
        foreach (var g in AnAnData.HlsoColumns)
        {
            var m = d.GroupMembers(g);
            if (m.Count > 0) aoa.AddRange(m.Select(x => new object?[] { g, x.Label })); else aoa.Add(new object?[] { g, "" });
        }
        return OneTable("Gop Dac Tinh", aoa, 2);
    }
}

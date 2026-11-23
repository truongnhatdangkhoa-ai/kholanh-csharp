using System.Text.Json.Nodes;

namespace KhoLanh.Core.TonKhoGui;

/// <summary>
/// "Bảng Tổng Hợp Tùy Chọn" (port kl-custom-summary.js): bảng RIÊNG chỉ lọc lại Size × nhóm Đặc Tính từ Bảng Tổng Hợp có sẵn —
/// không đọc lại tồn chi tiết, không ghi ngược. Dòng tiêu đề nhóm hiện đậm nhưng KHÔNG cộng vào dòng TỔNG CHỌN.
/// </summary>
public static class CustomSummary
{
    public sealed record Row(string Key, string Label, bool Title, double?[] Cells, double? Total);
    public sealed record Data(List<string> Cols, List<Row> Rows);

    /// <summary>Lựa chọn đã lưu (null = tất cả) + ẩn cột trống + các mẫu đã đặt tên.</summary>
    public sealed class State
    {
        public List<string>? Sizes, Cols; public bool HideEmpty = true;
        public Dictionary<string, (List<string>? Sizes, List<string>? Cols)> Views = new();
        public JsonObject ToJson()
        {
            static JsonNode? L(List<string>? l) => l is null ? null : new JsonArray(l.Select(x => (JsonNode)x).ToArray());
            var v = new JsonObject(); foreach (var (k, x) in Views) v[k] = new JsonObject { ["sizes"] = L(x.Sizes), ["cols"] = L(x.Cols) };
            return new JsonObject { ["sizes"] = L(Sizes), ["cols"] = L(Cols), ["hideEmpty"] = HideEmpty, ["views"] = v };
        }
        public static State From(string? json)
        {
            var s = new State(); if (string.IsNullOrWhiteSpace(json)) return s;
            try
            {
                if (JsonNode.Parse(json) is not JsonObject o) return s;
                static List<string>? L(JsonNode? n) => n is JsonArray a ? a.Select(x => x?.ToString() ?? "").ToList() : null;
                s.Sizes = L(o["sizes"]); s.Cols = L(o["cols"]); s.HideEmpty = o["hideEmpty"]?.GetValue<bool>() != false;
                if (o["views"] is JsonObject v) foreach (var (k, x) in v) if (x is JsonObject xo) s.Views[k] = (L(xo["sizes"]), L(xo["cols"]));
            }
            catch { }
            return s;
        }
    }

    /// <summary>Dữ liệu từ Bảng Tổng Hợp (Báo Cáo) của Tồn kho gửi: dòng nhóm + dòng size, cột = 23 nhóm đặc tính cố định.</summary>
    public static Data FromPivot(GuiData.Pivot p)
    {
        var cols = GuiData.DtGroups.ToList(); var rows = new List<Row>();
        double?[] Cells(GuiData.Bucket b) => cols.Select(g => b.ByGroup.TryGetValue(g, out var v) ? (double?)v : null).ToArray();
        foreach (var c in p.Cats)
        {
            rows.Add(new Row("T|" + c.Label, c.Label, true, Cells(c.B), c.B.Tong));
            foreach (var s in c.Sizes) rows.Add(new Row("S|" + c.Label + "|" + s.Size, s.Size, false, Cells(s.B), s.B.Tong));
        }
        return new Data(cols, rows);
    }

    public sealed class Result
    {
        public List<Row> Shown = new(); public List<int> ColIdx = new(); public double[] Sums = Array.Empty<double>(); public bool[] HasSum = Array.Empty<bool>();
        public List<double?> RowTotals = new(); public double Grand; public bool GrandHas; public double Diff;
    }

    public static Result Compute(Data d, ISet<string> selRows, ISet<string> selCols, bool hideEmpty)
    {
        var r = new Result { Shown = d.Rows.Where(x => selRows.Contains(x.Key)).ToList() };
        var colIdx = Enumerable.Range(0, d.Cols.Count).Where(i => selCols.Contains(d.Cols[i])).ToList();
        if (hideEmpty) { var has = colIdx.Where(i => r.Shown.Any(x => x.Cells[i] is double v && Math.Abs(v) > 0)).ToList(); if (has.Count > 0) colIdx = has; }
        r.ColIdx = colIdx; r.Sums = new double[colIdx.Count]; r.HasSum = new bool[colIdx.Count];
        foreach (var row in r.Shown)
        {
            double rt = 0; var rh = false;
            for (var k = 0; k < colIdx.Count; k++)
                if (row.Cells[colIdx[k]] is double v) { rt += v; rh = true; if (!row.Title) { r.Sums[k] += v; r.HasSum[k] = true; } }
            if (rh && !row.Title) { r.Grand += rt; r.GrandHas = true; }
            r.RowTotals.Add(rh ? rt : null);
        }
        if (selCols.Count == d.Cols.Count)
            foreach (var row in r.Shown) { if (row.Title || row.Total is not double t) continue; r.Diff += t - row.Cells.Sum(x => x ?? 0); }
        return r;
    }

    /// <summary>File TongHopTuyChon_{stamp}.xlsx (sheet "Tuy chon").</summary>
    public static TonKho.XlsxOut Export(Data d, Result r)
    {
        var aoa = new List<object?[]> { new object?[] { "Size" }.Concat(r.ColIdx.Select(i => (object?)d.Cols[i])).Concat(new object?[] { "Tổng chọn" }).ToArray() };
        for (var j = 0; j < r.Shown.Count; j++)
            aoa.Add(new object?[] { r.Shown[j].Label }.Concat(r.ColIdx.Select(i => r.Shown[j].Cells[i] is double v ? (object?)v : "")).Concat(new object?[] { r.RowTotals[j] is double t ? t : "" }).ToArray());
        aoa.Add(new object?[] { "TỔNG CHỌN" }.Concat(r.ColIdx.Select((i, k) => r.HasSum[k] ? (object?)r.Sums[k] : "")).Concat(new object?[] { r.GrandHas ? r.Grand : "" }).ToArray());
        var ws = new TonKho.XlsxOut.Sheet { Name = "Tuy chon", Aoa = aoa, FreezeRows = 1 };
        var n = r.ColIdx.Count + 2;
        for (var row = 0; row < aoa.Count; row++)
        {
            var title = row > 0 && row <= r.Shown.Count && r.Shown[row - 1].Title; var foot = row == aoa.Count - 1;
            for (var c = 0; c < n; c++)
                ws.Set(row, c, row == 0 ? new TonKho.XlsxOut.St(true, "FFFFFFFF", "FF1F4E78", "center", true)
                    : new TonKho.XlsxOut.St(title || foot || c == n - 1, null, title ? "FFDDEBF7" : foot ? "FFFFF2CC" : null, c == 0 ? "left" : "right", false, foot, c == 0 ? null : "#,##0.00"));
        }
        ws.Widths = new[] { 22.0 }.Concat(r.ColIdx.Select(_ => 16.0)).Concat(new[] { 16.0 }).ToArray();
        return new TonKho.XlsxOut { Sheets = { ws } };
    }
}

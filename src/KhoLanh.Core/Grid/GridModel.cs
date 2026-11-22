using System.Text;

namespace KhoLanh.Core.Grid;

/// <summary>Một vùng ô (đã chuẩn hóa: R1 ≤ R2, C1 ≤ C2).</summary>
public readonly record struct CellRange(int R1, int C1, int R2, int C2)
{
    public static CellRange Of(int r1, int c1, int r2, int c2) => new(Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2));
    public int Rows => R2 - R1 + 1;
    public int Cols => C2 - C1 + 1;
    public bool Contains(int r, int c) => r >= R1 && r <= R2 && c >= C1 && c <= C2;
    public bool Single => R1 == R2 && C1 == C2;
}

/// <summary>Định nghĩa một cột của lưới.</summary>
public sealed class GridColumn
{
    public string Key { get; set; } = "";
    public string Header { get; set; } = "";
    public double Width { get; set; } = 110;
    /// <summary>"text" | "number" | "date"</summary>
    public string Type { get; set; } = "text";
    public bool ReadOnly { get; set; }
    public GridColumn() { }
    public GridColumn(string key, string header, double width = 110, string type = "text") { Key = key; Header = header; Width = width; Type = type; }
}

/// <summary>
/// Dữ liệu của lưới kiểu Excel: mảng ô chuỗi, chép / dán dạng TSV (giống Excel), hoàn tác / làm lại.
/// Mọi thay đổi đi qua <see cref="Apply"/> để ghi được vào sổ hoàn tác.
/// </summary>
public sealed class GridModel
{
    private readonly List<string[]> _rows = new();
    private readonly Stack<Edit> _undo = new(), _redo = new();
    public List<GridColumn> Columns { get; } = new();
    public event Action? Changed;
    /// <summary>Cho phép dán thêm cột mới khi dữ liệu dán rộng hơn số cột hiện có.</summary>
    public bool AllowAddColumns { get; set; }
    public bool ReadOnly { get; set; }
    public int RowCount => _rows.Count;
    public int ColCount => Columns.Count;
    public int Version { get; private set; }

    /// <summary>Một thao tác: danh sách thay đổi ô + thay đổi số dòng / cột (để hoàn tác).</summary>
    private sealed class Edit
    {
        public List<(int r, int c, string before, string after)> Cells = new();
        public int RowsBefore, RowsAfter, ColsBefore, ColsAfter;
        public List<(int at, string[] row)>? RemovedRows;   // xóa dòng
        public List<(int at, int count)>? InsertedRows;      // chèn dòng
        public List<GridColumn>? AddedColumns;
    }

    public string Get(int r, int c) => r >= 0 && r < _rows.Count && c >= 0 && c < _rows[r].Length ? _rows[r][c] ?? "" : "";

    public IReadOnlyList<string> Row(int r) => _rows[r];

    /// <summary>Nạp dữ liệu mới (xóa sổ hoàn tác).</summary>
    public void Load(IEnumerable<GridColumn> columns, IEnumerable<IEnumerable<string?>> rows)
    {
        Columns.Clear(); Columns.AddRange(columns);
        _rows.Clear();
        foreach (var r in rows) _rows.Add(Fit(r.Select(x => x ?? "").ToArray()));
        _undo.Clear(); _redo.Clear();
        Bump();
    }

    public List<string[]> Snapshot() => _rows.Select(r => r.ToArray()).ToList();

    private string[] Fit(string[] r)
    {
        if (r.Length == Columns.Count) return r;
        var a = new string[Columns.Count];
        for (var i = 0; i < a.Length; i++) a[i] = i < r.Length ? r[i] ?? "" : "";
        return a;
    }

    private void Bump() { Version++; Changed?.Invoke(); }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    // -------------------------------------------------------------- sửa ô

    /// <summary>Ghi giá trị cho nhiều ô (tự thêm dòng nếu cần). Trả về vùng đã đổi.</summary>
    public void SetCells(IEnumerable<(int r, int c, string v)> cells)
    {
        if (ReadOnly) return;
        var e = new Edit { RowsBefore = _rows.Count, ColsBefore = Columns.Count };
        foreach (var (r, c, v0) in cells)
        {
            if (r < 0 || c < 0 || c >= Columns.Count || Columns[c].ReadOnly) continue;
            while (_rows.Count <= r) _rows.Add(new string[Columns.Count].Select(_ => "").ToArray());
            var before = _rows[r][c] ?? "";
            var v = v0 ?? "";
            if (before == v) continue;
            _rows[r][c] = v;
            e.Cells.Add((r, c, before, v));
        }
        e.RowsAfter = _rows.Count; e.ColsAfter = Columns.Count;
        Push(e);
    }

    public void Set(int r, int c, string v) => SetCells(new[] { (r, c, v) });

    private void Push(Edit e)
    {
        if (e.Cells.Count == 0 && e.RowsBefore == e.RowsAfter && e.ColsBefore == e.ColsAfter && e.RemovedRows is null && e.InsertedRows is null) return;
        _undo.Push(e); _redo.Clear();
        if (_undo.Count > 200) { var keep = _undo.Take(200).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
        Bump();
    }

    /// <summary>Xóa nội dung vùng chọn (phím Delete).</summary>
    public void Clear(CellRange sel) =>
        SetCells(Enumerable.Range(sel.R1, sel.Rows).Where(r => r < _rows.Count)
            .SelectMany(r => Enumerable.Range(sel.C1, sel.Cols).Select(c => (r, c, ""))));

    // -------------------------------------------------------------- dòng

    public void InsertRows(int at, int count)
    {
        if (ReadOnly || count <= 0) return;
        at = Math.Clamp(at, 0, _rows.Count);
        var e = new Edit { RowsBefore = _rows.Count, ColsBefore = Columns.Count, InsertedRows = new() { (at, count) } };
        for (var i = 0; i < count; i++) _rows.Insert(at, Enumerable.Repeat("", Columns.Count).ToArray());
        e.RowsAfter = _rows.Count; e.ColsAfter = Columns.Count;
        Push(e);
    }

    public void DeleteRows(int from, int to)
    {
        if (ReadOnly) return;
        from = Math.Max(0, from); to = Math.Min(_rows.Count - 1, to);
        if (to < from) return;
        var e = new Edit { RowsBefore = _rows.Count, ColsBefore = Columns.Count, RemovedRows = new() };
        for (var r = to; r >= from; r--) { e.RemovedRows.Add((r, _rows[r])); _rows.RemoveAt(r); }
        e.RowsAfter = _rows.Count; e.ColsAfter = Columns.Count;
        Push(e);
    }

    // -------------------------------------------------------------- hoàn tác

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var e = _undo.Pop();
        foreach (var (r, c, before, _) in Enumerable.Reverse(e.Cells)) if (r < _rows.Count && c < _rows[r].Length) _rows[r][c] = before;
        if (e.RemovedRows is not null) foreach (var (at, row) in Enumerable.Reverse(e.RemovedRows)) _rows.Insert(Math.Min(at, _rows.Count), Fit(row));
        if (e.InsertedRows is not null) foreach (var (at, n) in e.InsertedRows) _rows.RemoveRange(at, Math.Min(n, _rows.Count - at));
        while (_rows.Count > e.RowsBefore) _rows.RemoveAt(_rows.Count - 1);
        if (e.AddedColumns is not null)
        {
            Columns.RemoveRange(e.ColsBefore, Columns.Count - e.ColsBefore);
            for (var i = 0; i < _rows.Count; i++) _rows[i] = Fit(_rows[i]);
        }
        _redo.Push(e);
        Bump();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var e = _redo.Pop();
        if (e.AddedColumns is not null)
        {
            Columns.AddRange(e.AddedColumns);
            for (var i = 0; i < _rows.Count; i++) _rows[i] = Fit(_rows[i]);
        }
        if (e.InsertedRows is not null) foreach (var (at, n) in e.InsertedRows) for (var i = 0; i < n; i++) _rows.Insert(at, Enumerable.Repeat("", Columns.Count).ToArray());
        if (e.RemovedRows is not null) foreach (var (at, _) in e.RemovedRows) _rows.RemoveAt(at);
        while (_rows.Count < e.RowsAfter) _rows.Add(Enumerable.Repeat("", Columns.Count).ToArray());
        foreach (var (r, c, _, after) in e.Cells) _rows[r][c] = after;
        _undo.Push(e);
        Bump();
        return true;
    }

    // -------------------------------------------------------------- chép / dán

    /// <summary>Chép vùng chọn ra dạng TSV (giống Excel: ô có tab / xuống dòng / ngoặc kép thì bọc ngoặc kép).</summary>
    public string CopyText(CellRange sel)
    {
        var sb = new StringBuilder();
        for (var r = sel.R1; r <= sel.R2; r++)
        {
            for (var c = sel.C1; c <= sel.C2; c++)
            {
                if (c > sel.C1) sb.Append('\t');
                var v = Get(r, c);
                sb.Append(v.IndexOfAny(new[] { '\t', '\n', '\r', '"' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v);
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Đọc TSV (Excel, Google Sheets, bảng tính khác) thành bảng ô.</summary>
    public static List<List<string>> ParseTsv(string text)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text)) return rows;
        var row = new List<string>();
        var cell = new StringBuilder();
        var i = 0; var quotedCell = false; var atStart = true;
        while (i < text.Length)
        {
            var ch = text[i];
            if (atStart && ch == '"')
            {
                // ô bọc ngoặc kép: đọc tới ngoặc kép đóng (cho phép tab / xuống dòng bên trong)
                var j = i + 1; var sb = new StringBuilder(); var closed = false;
                while (j < text.Length)
                {
                    if (text[j] == '"')
                    {
                        if (j + 1 < text.Length && text[j + 1] == '"') { sb.Append('"'); j += 2; continue; }
                        closed = true; j++; break;
                    }
                    sb.Append(text[j]); j++;
                }
                if (closed && (j >= text.Length || text[j] is '\t' or '\r' or '\n'))
                {
                    cell.Append(sb); i = j; quotedCell = true; atStart = false; continue;
                }
                // không phải ô bọc ngoặc đúng chuẩn → coi ngoặc kép là chữ thường
            }
            atStart = false;
            if (ch == '\t') { row.Add(cell.ToString()); cell.Clear(); atStart = true; quotedCell = false; i++; continue; }
            if (ch == '\r' || ch == '\n')
            {
                row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>();
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                i++; atStart = true; quotedCell = false; continue;
            }
            cell.Append(ch); i++;
        }
        if (!atStart || cell.Length > 0 || row.Count > 0 || quotedCell) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }

    /// <summary>
    /// Dán TSV vào lưới tại ô hiện hành / vùng chọn, như Excel:
    ///  - dán 1 ô vào vùng nhiều ô → điền cả vùng;
    ///  - khối dán vừa khít số lần trong vùng chọn → lặp lại;
    ///  - còn lại dán từ góc trên trái, tự thêm dòng (và cột nếu cho phép).
    /// Trả về vùng đã dán.
    /// </summary>
    public CellRange Paste(CellRange sel, string text)
    {
        if (ReadOnly) return sel;
        var data = ParseTsv(text);
        if (data.Count == 0) return sel;
        var h = data.Count; var w = data.Max(r => r.Count);
        int rows = h, cols = w;
        if ((h == 1 && w == 1) || (sel.Rows % h == 0 && sel.Cols % w == 0 && !sel.Single)) { rows = Math.Max(h, sel.Rows); cols = Math.Max(w, sel.Cols); }
        var e = new Edit { RowsBefore = _rows.Count, ColsBefore = Columns.Count };
        if (AllowAddColumns && sel.C1 + cols > Columns.Count)
        {
            e.AddedColumns = new();
            while (Columns.Count < sel.C1 + cols)
            {
                var col = new GridColumn("c" + (Columns.Count + 1), ColumnName(Columns.Count));
                Columns.Add(col); e.AddedColumns.Add(col);
            }
            for (var i = 0; i < _rows.Count; i++) _rows[i] = Fit(_rows[i]);
        }
        for (var dr = 0; dr < rows; dr++)
        {
            var r = sel.R1 + dr;
            for (var dc = 0; dc < cols; dc++)
            {
                var c = sel.C1 + dc;
                if (c >= Columns.Count || Columns[c].ReadOnly) continue;
                var src = data[dr % h];
                var v = dc % w < src.Count ? src[dc % w] : "";
                while (_rows.Count <= r) _rows.Add(Enumerable.Repeat("", Columns.Count).ToArray());
                var before = _rows[r][c] ?? "";
                if (before == v) continue;
                _rows[r][c] = v;
                e.Cells.Add((r, c, before, v));
            }
        }
        e.RowsAfter = _rows.Count; e.ColsAfter = Columns.Count;
        Push(e);
        return new CellRange(sel.R1, sel.C1, sel.R1 + rows - 1, Math.Min(Columns.Count - 1, sel.C1 + cols - 1));
    }

    /// <summary>Ctrl+D: chép dòng đầu vùng chọn xuống các dòng dưới.</summary>
    public void FillDown(CellRange sel)
    {
        if (sel.Rows < 2) return;
        SetCells(Enumerable.Range(sel.R1 + 1, sel.Rows - 1).SelectMany(r => Enumerable.Range(sel.C1, sel.Cols).Select(c => (r, c, Get(sel.R1, c)))));
    }

    /// <summary>Tên cột kiểu Excel: A, B, …, Z, AA…</summary>
    public static string ColumnName(int index)
    {
        var s = "";
        index++;
        while (index > 0) { var m = (index - 1) % 26; s = (char)('A' + m) + s; index = (index - 1) / 26; }
        return s;
    }

    /// <summary>Số dòng cuối có dữ liệu (bỏ dòng trống ở cuối).</summary>
    public int UsedRows()
    {
        var n = _rows.Count;
        while (n > 0 && _rows[n - 1].All(string.IsNullOrWhiteSpace)) n--;
        return n;
    }
}

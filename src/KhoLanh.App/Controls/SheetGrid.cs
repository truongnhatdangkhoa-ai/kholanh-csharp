using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.Core.Grid;

namespace KhoLanh.App.Controls;

/// <summary>
/// Lưới kiểu Excel tự vẽ (chỉ vẽ phần đang thấy nên mượt với hàng chục nghìn dòng):
/// chọn ô / vùng bằng chuột hoặc phím, gõ là sửa, F2 / nhấp đúp sửa tiếp, Enter / Tab di chuyển,
/// Ctrl+C / X / V chép dán qua Excel, Delete xóa, Ctrl+Z / Y hoàn tác, Ctrl+D điền xuống, kéo mép tiêu đề để đổi độ rộng cột.
/// </summary>
public sealed class SheetGrid : UserControl
{
    private readonly GridSurface _surface;
    private readonly ScrollBar _vbar = new() { Orientation = Orientation.Vertical, SmallChange = 1, LargeChange = 20 };
    private readonly ScrollBar _hbar = new() { Orientation = Orientation.Horizontal, SmallChange = 40, LargeChange = 400 };
    private readonly TextBox _editor = new()
    {
        IsVisible = false, BorderThickness = new Thickness(2), BorderBrush = AppTheme.Brush("#1F6FD6"),
        Padding = new Thickness(4, 2), MinHeight = 0, CornerRadius = new CornerRadius(0), AcceptsReturn = false, FontSize = 13,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly Canvas _overlay = new();

    public GridModel Model { get; }
    // Dòng chọn lưu theo "dòng đang hiện" (VR / VA); khi có bộ lọc, dòng hiện thứ v là dòng dữ liệu ToModel(v).
    // Mọi API công khai (Row, AnchorRow, Selection, Select, SelectRange, Display, CellBackground) dùng chỉ số dòng DỮ LIỆU như trước.
    internal int VR, VA;
    public int Row => ToModel(VR);
    public int AnchorRow => ToModel(VA);
    public int Col { get; internal set; }
    public int AnchorCol { get; internal set; }
    public CellRange Selection => CellRange.Of(ToModel(VA), AnchorCol, ToModel(VR), Col);
    internal CellRange VSel => CellRange.Of(VA, AnchorCol, VR, Col);
    /// <summary>Số dòng trống thêm ở cuối để gõ / dán tiếp.</summary>
    public int SpareRows { get; set; } = 20;
    /// <summary>Chữ hiện giữa bảng khi bảng chỉ đọc không có dòng nào (null = không hiện).</summary>
    public string? EmptyText { get; set; } = "Không có dòng nào.";
    /// <summary>Định dạng hiển thị (giá trị lưu vẫn là chuỗi gốc).</summary>
    public Func<int, int, string, string>? Display { get; set; }
    /// <summary>Màu nền riêng từng ô (null = mặc định).</summary>
    public Func<int, int, IBrush?>? CellBackground { get; set; }
    /// <summary>Màu chữ riêng từng ô (null = mặc định) — vd dòng đã khóa hiện mờ.</summary>
    public Func<int, int, IBrush?>? CellForeground { get; set; }
    /// <summary>1.73: chú thích khi rê chuột lên ô (dòng DỮ LIỆU, cột) — null = không có (vd Nhập liệu kiểm kê báo trùng / số lỗi).</summary>
    public Func<int, int, string?>? CellTip { get; set; }
    /// <summary>Dòng nào được cộng vào dòng Tổng cuối bảng (null = mọi dòng trừ dòng tổng có sẵn) — vd bảng có dòng nhóm + dòng chi tiết thì chỉ cộng dòng chi tiết.</summary>
    public Func<int, bool>? TotalsRowFilter { get; set; }
    /// <summary>Dòng tổng / nhóm (▾ ▸ "Tổng …") vẫn được cộng vào dòng Tổng — vd nhóm đang thu gọn chỉ còn dòng tổng nhóm.</summary>
    public Func<int, bool>? TotalsKeepRow { get; set; }
    /// <summary>1.95: dòng chữ đậm (vd dòng tổng ngày ở Đối chiếu hàng ngày).</summary>
    public Func<int, bool>? RowBold { get; set; }
    public event Action? SelectionChanged;
    /// <summary>Bấm chuột trái (thả ra) vào một dòng dữ liệu — chỉ số dòng dữ liệu (model).</summary>
    public Action<int>? RowClicked { get; set; }
    /// <summary>Đổi bộ lọc cột (dòng tìm kiếm / danh sách lọc).</summary>
    public event Action? FilterChanged;
    /// <summary>Có dòng tìm kiếm theo cột + nút lọc ▾ ở tiêu đề (mặc định bật cho mọi bảng; tắt cho bảng tóm tắt vài dòng).</summary>
    public bool Filterable { get => _filterable; set { _filterable = value; if (!value) ClearFilters(); UpdateScroll(); Redraw(); } }
    private bool _filterable = true;

    /// <summary>Chiều cao dòng theo mật độ bảng của tài khoản (Gọn 22 · Vừa 26 · Thoáng 32 — <see cref="AppTheme.Density"/>).</summary>
    internal static double RowH => AppTheme.RowHeight;
    internal const double HeadH = 28;
    /// <summary>Chiều cao dòng tìm kiếm theo cột (dưới tiêu đề).</summary>
    internal const double FilterH = 26;
    /// <summary>Tổng chiều cao phần đầu bảng (tiêu đề + dòng tìm kiếm nếu có).</summary>
    internal double TopH => HeadH + (ShowFilterRow ? FilterH : 0);
    /// <summary>Dòng tìm kiếm chỉ hiện khi bảng đủ cao (≥ 6 dòng sau khi thêm) — bảng con thấp / cố định chiều cao không bị mất dòng
    /// (1.51: sau 1.49 nhiều bảng con chỉ còn thấy 1 dòng). Bảng thấp vẫn lọc được bằng nút ▾ ở tiêu đề; đang lọc bằng chữ thì luôn hiện.</summary>
    /// <summary>Chiều cao để thấy đủ <paramref name="rows"/> dòng (kèm thanh cuộn ngang; từ 6 dòng kèm cả dòng tìm kiếm cột), tối đa <paramref name="max"/>.</summary>
    public static double FitHeight(int rows, double max = double.PositiveInfinity)
    {
        rows = Math.Max(1, rows);
        return Math.Min(max, HeadH + RowH * rows + 22 + (rows >= 6 ? FilterH : 0));
    }
    internal bool ShowFilterRow => _filterable && (_surface.Bounds.Height <= 0 || _surface.Bounds.Height >= HeadH + FilterH + RowH * 6
        || _filters.Values.Any(f => f.Text.Trim().Length > 0));
    internal double NumW => Math.Max(44, (Math.Max(Model.RowCount + SpareRows, 1).ToString().Length) * 8 + 18);
    internal double RowHeadW => NumW + (HasCheck ? CheckW : 0);

    // ------------------------------------------------------------------ ô chọn đầu dòng (1.50 — chọn hàng xuất thay vì gõ số)
    internal const double CheckW = 26;
    /// <summary>Cột số lượng gắn với ô chọn ☐ đầu dòng: tích = điền <see cref="CheckFill"/> (thường là tồn còn xuất được), bỏ tích = xóa;
    /// ô có số &gt; 0 thì hiện đã tích. Tích ô tiêu đề = chọn / bỏ mọi dòng ĐANG HIỆN (sau lọc). Phím cách = tích các dòng đang chọn. -1 = không có.</summary>
    public int CheckCol { get => _checkCol; set { _checkCol = value; _sumDirty = true; UpdateScroll(); Redraw(); } }
    private int _checkCol = -1;
    /// <summary>Giá trị điền vào cột <see cref="CheckCol"/> khi tích dòng (dòng dữ liệu → chữ).</summary>
    public Func<int, string>? CheckFill { get; set; }
    internal bool HasCheckCol => _checkCol >= 0 && _checkCol < Model.ColCount && !Model.ReadOnly && !Model.Columns[_checkCol].ReadOnly;
    internal bool HasCheck => HasCheckCol || TickMode;

    // 1.93: ô tích CHỌN DÒNG (không ghi vào dữ liệu) — mọi bảng sửa được tự có (bảng chỉ xem thì không), ô tiêu đề = tích hết.
    // Dòng đã tích: dòng "Đã chọn" ở đáy bảng, chuột phải › Xóa / Chép dòng đã tích, View đọc CheckedRows (vd In chứng từ › ngày in).
    /// <summary>Ô tích chọn dòng: null = tự (bảng sửa được thì có), true / false = ép bật / tắt. Bảng có <see cref="CheckCol"/> dùng cột đó.</summary>
    public bool? RowChecks { get => _rowChecks; set { _rowChecks = value; _sumDirty = true; UpdateScroll(); Redraw(); } }
    private bool? _rowChecks;
    internal bool TickMode => _checkCol < 0 && (_rowChecks ?? !Model.ReadOnly) && Model.ColCount > 0;
    internal HashSet<int> _ticks = new();
    private (int Loads, int Rows) _tickVer = (-1, -1);
    /// <summary>Đổi dòng đã tích (chế độ ô tích chọn dòng).</summary>
    public event Action? ChecksChanged;
    /// <summary>Các dòng dữ liệu đã tích (theo thứ tự dòng).</summary>
    public IReadOnlyList<int> CheckedRows => Enumerable.Range(0, Model.RowCount).Where(IsChecked).ToList();
    /// <summary>Nạp dữ liệu mới / thêm / xóa dòng → bỏ tích (chỉ số dòng đã đổi).</summary>
    private void SyncTicks()
    {
        var v = (Model.Loads, Model.RowCount);
        if (v == _tickVer) return;
        _tickVer = v;
        if (_ticks.Count > 0) { _ticks.Clear(); ChecksChanged?.Invoke(); }
    }
    /// <summary>Phím cách = tích: bảng có cột tích, hoặc bảng chỉ xem có ô tích (bảng sửa được: phím cách để gõ).</summary>
    private bool SpaceTicks => HasCheckCol || (TickMode && Model.ReadOnly);
    internal bool IsChecked(int m)
    {
        if (m < 0 || m >= Model.RowCount) return false;
        if (TickMode) { SyncTicks(); return _ticks.Contains(m); }
        var v = Model.Get(m, _checkCol).Trim();
        return v.Length > 0 && v != "0" && !(GridFilter.TryNumber(v, out var n) && n <= 0);
    }
    /// <summary>Tích / bỏ tích các dòng dữ liệu (1 lần hoàn tác).</summary>
    public void SetChecked(IEnumerable<int> rows, bool on)
    {
        if (TickMode)
        {
            SyncTicks();
            var ch = false;
            foreach (var m in rows) if (m >= 0 && m < Model.RowCount && (on ? _ticks.Add(m) : _ticks.Remove(m))) ch = true;
            if (ch) { _sumDirty = true; Redraw(); ChecksChanged?.Invoke(); }
            return;
        }
        if (!HasCheck) return;
        Model.SetCells(rows.Where(m => m >= 0 && m < Model.RowCount && IsChecked(m) != on)
            .Select(m => (m, _checkCol, on ? (CheckFill?.Invoke(m) ?? "1") : "")).ToList());
    }
    internal void ToggleRows(IReadOnlyCollection<int> rows)
    {
        if (rows.Count == 0) return;
        SetChecked(rows, !rows.All(IsChecked));
    }
    /// <summary>Trạng thái ô tích tiêu đề: true = mọi dòng đang hiện đã tích, null = một phần, false = chưa dòng nào.</summary>
    internal bool? MasterState()
    {
        var cnt = VCount; if (cnt == 0) return false;
        var n = 0; for (var v = 0; v < cnt; v++) if (IsChecked(ToModel(v))) n++;   // không cấp phát — gọi mỗi lần vẽ
        return n == 0 ? false : n == cnt ? true : null;
    }
    internal int TopRow;          // dòng đầu đang thấy
    internal double OffsetX;      // cuộn ngang (px)
    private bool _editing, _typedEdit;

    // ------------------------------------------------------------------ lọc (1.49)
    private readonly Dictionary<string, ColumnFilter> _filters = new();
    private List<int>? _map;                       // null = không lọc; còn lại: các dòng dữ liệu đang hiện
    private int _seenLoads = -1, _seenRows = -1;
    private readonly TextBox _fEditor = new()
    {
        IsVisible = false, BorderThickness = new Thickness(2), BorderBrush = AppTheme.Brush("#1F6FD6"), Watermark = "Tìm trong cột…",
        Padding = new Thickness(4, 1), MinHeight = 0, CornerRadius = new CornerRadius(0), FontSize = 12.5, VerticalContentAlignment = VerticalAlignment.Center,
    };
    private int _fCol = -1;
    private readonly DispatcherTimer _fTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };

    // ------------------------------------------------------------------ sắp xếp (1.71): bấm tiêu đề cột → A→Z, bấm lại → Z→A (chỉ đổi thứ tự hiện)
    private string? _sortKey; private bool _sortDesc;
    private int[]? _inv;                           // dòng dữ liệu → vị trí đang hiện (khi sắp xếp; -1 = đang ẩn)
    public bool Sortable { get; set; } = true;
    internal int SortCol { get { if (_sortKey is null) return -1; for (var c = 0; c < Model.ColCount; c++) if (FKey(Model.Columns[c], c) == _sortKey) return c; return -1; } }
    internal bool SortDesc => _sortDesc;
    /// <summary>Bấm tiêu đề cột: lần đầu A→Z, các lần sau đảo A→Z ↔ Z→A.</summary>
    public void ToggleSort(int c)
    {
        if (!Sortable || c < 0 || c >= Model.ColCount) return;
        if (_editing) Commit(0, 0);
        var k = FKey(Model.Columns[c], c);
        if (_sortKey == k) _sortDesc = !_sortDesc; else { _sortKey = k; _sortDesc = false; }
        ApplyFilter();
    }
    public void ClearSort() { if (_sortKey is null) return; _sortKey = null; _sortDesc = false; ApplyFilter(); }
    /// <summary>1.94: co giãn cột vừa dữ liệu (bấm đúp viền phải tiêu đề cột, như Excel): rộng = chữ dài nhất của tiêu đề / các dòng đang hiện.</summary>
    public void AutoFitColumn(int c)
    {
        if (c < 0 || c >= Model.ColCount || Model.Columns[c].Width < 4) return;   // cột ẩn (rộng 0) giữ nguyên
        if (_editing) Commit(0, 0);
        var col = Model.Columns[c];
        var face = new Typeface(FontFamily.Default);
        var head = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
        double W(string t, Typeface tf) => new FormattedText(t, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, 13, null).Width;
        var hdr = col.Header + (SortCol == c ? " ▼" : "");
        var w = W(hdr, head) + 14 + (Filterable ? GridSurface.DropW : 0);
        // chỉ đo các chữ dài nhất (theo số ký tự) — bảng hàng chục nghìn dòng vẫn tức thì
        var texts = new HashSet<string>();
        foreach (var m in ShownRows)
        {
            var t = CellText(m, c);
            var nl = t.IndexOfAny(new[] { '\r', '\n' }); if (nl >= 0) t = t[..nl] + " …";
            if (t.Length > 0) texts.Add(t);
        }
        foreach (var t in texts.OrderByDescending(t => t.Length).Take(300)) w = Math.Max(w, W(t, face) + 14);
        col.Width = Math.Clamp(Math.Ceiling(w), 36, 720);
        _sumDirty = true; UpdateScroll(); Redraw();
    }

    /// <summary>1.94: sắp xếp theo cột (false = A→Z, true = Z→A, null = bỏ sắp xếp) — từ nút ▾ của cột / chuột phải.</summary>
    public void SetSort(int c, bool? desc)
    {
        if (!Sortable || c < 0 || c >= Model.ColCount) return;
        if (_editing) Commit(0, 0);
        if (desc is null) { ClearSort(); return; }
        _sortKey = FKey(Model.Columns[c], c); _sortDesc = desc.Value;
        ApplyFilter();
    }
    private bool _filterOn;
    public bool IsFiltered => _filterOn;
    /// <summary>Các dòng dữ liệu đang hiện (khi không lọc: mọi dòng).</summary>
    public IReadOnlyList<int> ShownRows => _map ?? (IReadOnlyList<int>)Enumerable.Range(0, Model.RowCount).ToList();
    internal int VCount => _map?.Count ?? Model.RowCount;
    internal int ToModel(int v) => _map is null || v < 0 ? v : v < _map.Count ? _map[v] : Model.RowCount + (v - _map.Count);
    internal int ToVisual(int m)
    {
        if (_map is null) return m;
        if (m >= Model.RowCount) return _map.Count + (m - Model.RowCount);
        if (_inv is not null) { var v = m >= 0 && m < _inv.Length ? _inv[m] : -1; return v >= 0 ? v : Math.Max(0, Math.Min(m, _map.Count - 1)); }
        var i = _map.BinarySearch(m);
        return i >= 0 ? i : Math.Min(~i, Math.Max(0, _map.Count - 1));
    }
    private static string FKey(GridColumn col, int c) => string.IsNullOrEmpty(col.Key) ? "#" + c : col.Key;
    internal ColumnFilter? FilterOf(int c) => c >= 0 && c < Model.ColCount && _filters.TryGetValue(FKey(Model.Columns[c], c), out var f) && f.Active ? f : null;
    internal string CellText(int m, int c) { var raw = Model.Get(m, c); return ShowText(m, c, raw); }

    // ------------------------------------------------------------------ định dạng số thống nhất (1.71): 1,500.00 kg · 1,500 kiện
    /// <summary>Tự viết lại số theo loại cột (trọng lượng / net 2 số lẻ, kiện / bao số nguyên). Tắt cho bảng tự lo định dạng riêng.</summary>
    public bool AutoNumberFormat { get; set; } = true;
    private NumFmt.Cls[] _fmtCls = Array.Empty<NumFmt.Cls>();
    private bool[] _fmtVi = Array.Empty<bool>();
    private (int, int, int, int) _fmtVer = (-1, -1, -1, -1);
    private void EnsureFmt()
    {
        var ver = (Model.Loads, Model.ColCount, Model.RowCount, Model.ReadOnly ? 1 : 0);
        if (ver == _fmtVer && _fmtCls.Length == Model.ColCount) return;
        _fmtVer = ver;
        _fmtCls = Model.Columns.Select(col => NumFmt.ClassOf(col.Header)).ToArray();
        // dữ liệu CŨ kiểu Việt (1.234,5) chỉ nhận ra ở bảng chỉ đọc (số do module / file cũ đưa vào); bảng gõ tay luôn đọc theo kiểu mới
        _fmtVi = Enumerable.Range(0, Model.ColCount).Select(c => Model.ReadOnly && GridTotals.IsViFormat(Enumerable.Range(0, Math.Min(Model.RowCount, 400)).Select(r => Model.Get(r, c)))).ToArray();
    }
    /// <summary>Chữ hiển thị của ô (Display riêng của bảng + định dạng số thống nhất).</summary>
    public string ShowText(int m, int c, string raw)
    {
        var shown = Display?.Invoke(m, c, raw) ?? raw;
        if (!AutoNumberFormat || shown.Length == 0 || c < 0 || c >= Model.ColCount) return shown;
        EnsureFmt();
        return NumFmt.Show(shown, _fmtCls[c], _fmtVi[c]);
    }
    internal NumFmt.Cls FmtClass(int c) { EnsureFmt(); return c >= 0 && c < _fmtCls.Length ? _fmtCls[c] : NumFmt.Cls.None; }

    private Dictionary<int, ColumnFilter> FilterByIndex()
    {
        var d = new Dictionary<int, ColumnFilter>();
        for (var c = 0; c < Model.ColCount; c++) if (_filters.TryGetValue(FKey(Model.Columns[c], c), out var f) && f.Active) d[c] = f;
        return d;
    }

    /// <summary>Lọc lại theo bộ lọc hiện có (gọi sau khi đổi bộ lọc hoặc nạp dữ liệu mới).</summary>
    public void ApplyFilter()
    {
        _sumDirty = true;
        var keepRow = Row; var keepAnchor = AnchorRow;
        var act = FilterByIndex();
        _filterOn = act.Count > 0;
        _map = act.Count == 0 ? null : GridFilter.Rows(Model.RowCount, CellText, act);
        _inv = null;
        var sc = SortCol;
        if (sc >= 0)
        {
            _map = GridSort.Order(_map ?? (IEnumerable<int>)Enumerable.Range(0, Model.UsedRows()), r => CellText(r, sc), _sortDesc);
            if (!_filterOn) for (var r = Model.UsedRows(); r < Model.RowCount; r++) _map.Add(r);   // dòng trống cuối bảng giữ ở cuối
            _inv = new int[Model.RowCount]; Array.Fill(_inv, -1);
            for (var i = 0; i < _map.Count; i++) if (_map[i] >= 0 && _map[i] < _inv.Length) _inv[_map[i]] = i;
        }
        else if (_sortKey is not null) _sortKey = null;   // cột đã không còn
        _seenLoads = Model.Loads; _seenRows = Model.RowCount;
        VR = Math.Clamp(ToVisual(keepRow), 0, Math.Max(0, TotalRows - 1));
        VA = Math.Clamp(ToVisual(keepAnchor), 0, Math.Max(0, TotalRows - 1));
        TopRow = Math.Min(TopRow, Math.Max(0, TotalRows - 1));
        UpdateScroll(); Redraw();
        FilterChanged?.Invoke(); SelectionChanged?.Invoke();
    }

    /// <summary>Đặt bộ lọc cho cột (chữ tìm và / hoặc danh sách giá trị; null = giữ nguyên).</summary>
    public void SetFilter(int c, string? text = null, HashSet<string>? allowed = null, bool setAllowed = false)
    {
        if (c < 0 || c >= Model.ColCount) return;
        var k = FKey(Model.Columns[c], c);
        if (!_filters.TryGetValue(k, out var f)) _filters[k] = f = new ColumnFilter();
        if (text is not null) f.Text = text;
        if (setAllowed) f.Allowed = allowed;
        if (!f.Active) _filters.Remove(k);
        ApplyFilter();
    }

    public void ClearFilters()
    {
        if (_filters.Count == 0 && !_filterOn) return;
        _filters.Clear(); HideFilterEditor(false);
        if (Model is not null) ApplyFilter();
    }

    private void OnModelChanged()
    {
        _sumDirty = true;
        // Nạp dữ liệu mới / đổi số dòng → lọc lại (giữ bộ lọc của cột còn tồn tại); sửa ô thì giữ nguyên các dòng đang hiện như Excel.
        if (Model.Loads != _seenLoads || Model.RowCount != _seenRows)
        {
            if (_filters.Count > 0)
            {
                var keys = Model.Columns.Select(FKey).ToHashSet();
                foreach (var k in _filters.Keys.Where(k => !keys.Contains(k)).ToList()) _filters.Remove(k);
            }
            _seenLoads = Model.Loads; _seenRows = Model.RowCount;
            if (_sortKey is not null && _filters.Count == 0 && Model.Loads == _seenLoads && _map is not null && Model.RowCount > _seenRows)
            {   // đang sắp xếp, chỉ thêm dòng mới (gõ tiếp cuối bảng): giữ nguyên thứ tự đang hiện, dòng mới nằm cuối — như Excel
                for (var r = _seenRows; r < Model.RowCount; r++) _map.Add(r);
                _inv = new int[Model.RowCount]; Array.Fill(_inv, -1);
                for (var i = 0; i < _map.Count; i++) if (_map[i] >= 0 && _map[i] < _inv.Length) _inv[_map[i]] = i;
                _seenRows = Model.RowCount; UpdateScroll(); Redraw(); return;
            }
            if (_filters.Count > 0 || _map is not null || _sortKey is not null) { ApplyFilter(); return; }
        }
        UpdateScroll(); Redraw();
    }

    internal void ShowFilterEditor(int c)
    {
        if (!ShowFilterRow || c < 0 || c >= Model.ColCount) return;
        if (_editing) Commit(0, 0);
        _fCol = c;
        _fEditor.Text = FilterOf(c)?.Text ?? "";
        _fEditor.IsVisible = true;
        PlaceFilterEditor();
        _fEditor.Focus();
        _fEditor.CaretIndex = _fEditor.Text?.Length ?? 0;
    }

    private void PlaceFilterEditor()
    {
        if (_fCol < 0 || _fCol >= Model.ColCount) { _fEditor.IsVisible = false; return; }
        var w = Model.Columns[_fCol].Width;
        Canvas.SetLeft(_fEditor, ColX(_fCol) - 1); Canvas.SetTop(_fEditor, HeadH - 1);
        _fEditor.Width = Math.Max(w + 2, 90); _fEditor.Height = FilterH + 1;
    }

    private void HideFilterEditor(bool focusGrid)
    {
        if (!_fEditor.IsVisible) { _fCol = -1; return; }
        _fTimer.Stop();
        if (_fCol >= 0 && _fCol < Model.ColCount && (FilterOf(_fCol)?.Text ?? "") != (_fEditor.Text ?? "")) SetFilter(_fCol, _fEditor.Text ?? "");
        _fEditor.IsVisible = false; _fCol = -1;
        if (focusGrid) _surface.Focus();
        Redraw();
    }

    /// <summary>Mở danh sách lọc phân tầng của cột (nút ▾ ở tiêu đề, hoặc Alt+↓).</summary>
    internal void OpenFilterMenu(int c)
    {
        if (!_filterable || c < 0 || c >= Model.ColCount) return;
        if (_editing) Commit(0, 0);
        HideFilterEditor(false);
        var col = Model.Columns[c];
        var others = FilterByIndex(); others.Remove(c);
        var rows = GridFilter.Rows(Model.RowCount, CellText, others);   // phân tầng: chỉ giá trị còn lại sau các cột khác
        var tree = GridFilter.BuildTree(rows.Select(r => CellText(r, c)), col.Type == "date");
        var cur = FilterOf(c);
        // mở ngay dưới tiêu đề cột đó
        var fl = new Flyout
        {
            Placement = PlacementMode.AnchorAndGravity, PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
            HorizontalOffset = Math.Max(0, ColX(c)), VerticalOffset = HeadH,
        };
        fl.Content = FilterMenu.Build(col.Header, tree, cur?.Allowed, (allowed, set) => { fl.Hide(); if (set) SetFilter(c, null, allowed, true); },
            () => { fl.Hide(); SetFilter(c, "", null, true); }, () => fl.Hide(),
            Sortable ? d => { fl.Hide(); SetSort(c, d); } : null, SortCol == c ? _sortDesc : null);
        fl.ShowAt(_surface);
    }

    public SheetGrid() : this(new GridModel()) { }

    public SheetGrid(GridModel model)
    {
        Model = model;
        _surface = new GridSurface(this) { Focusable = true, ClipToBounds = true };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("*,Auto,Auto") };
        var host = new Panel { ClipToBounds = true };
        host.Children.Add(_surface);
        host.Children.Add(_overlay);
        _overlay.Children.Add(_editor);
        _overlay.Children.Add(_fEditor);
        g.Children.Add(host);
        Grid.SetColumn(_vbar, 1); g.Children.Add(_vbar);
        _totals = new TotalsBar(this) { IsVisible = false };
        Grid.SetRow(_totals, 1); g.Children.Add(_totals);
        Grid.SetRow(_hbar, 2); g.Children.Add(_hbar);
        Content = new Border { BorderBrush = AppTheme.Brush("#B9C3D0"), BorderThickness = new Thickness(1), Background = AppTheme.Card, Child = g };
        _vbar.Scroll += (_, _) => { TopRow = (int)Math.Round(_vbar.Value); Redraw(); };
        _hbar.Scroll += (_, _) => { OffsetX = _hbar.Value; Redraw(); };
        _vbar.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { TopRow = (int)Math.Round(_vbar.Value); Redraw(); } };
        _hbar.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { OffsetX = _hbar.Value; Redraw(); } };
        _surface.SizeChanged += (_, _) => UpdateScroll();
        // nghe Model chỉ khi đang hiện: lưới trong cửa sổ lớn (OpenLarge) đóng rồi thì thôi nghe, không giữ lại bộ nhớ.
        // Gắn lại vào màn hình → đồng bộ những gì Model đổi trong lúc ẩn.
        AttachedToVisualTree += (_, _) => { Model.Changed -= OnModelChanged; Model.Changed += OnModelChanged; OnModelChanged(); };
        DetachedFromVisualTree += (_, _) => Model.Changed -= OnModelChanged;
        _seenLoads = Model.Loads; _seenRows = Model.RowCount;
        _editor.KeyDown += OnEditorKey;
        _sug.Child = new Border { Background = AppTheme.Card, BorderBrush = AppTheme.Brush("#B9C3D0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(3), Child = _sugList, MinWidth = 380, MaxWidth = 620 };
        _overlay.Children.Add(_sug);
        _editor.TextChanged += (_, _) => UpdateSuggest();
        _fTimer.Tick += (_, _) => { _fTimer.Stop(); if (_fCol >= 0) SetFilter(_fCol, _fEditor.Text ?? ""); };
        _fEditor.TextChanged += (_, _) => { if (_fEditor.IsVisible) { _fTimer.Stop(); _fTimer.Start(); } };
        _fEditor.LostFocus += (_, _) => HideFilterEditor(false);
        _fEditor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { _fEditor.Text = ""; HideFilterEditor(true); e.Handled = true; }
            else if (e.Key is Key.Enter or Key.Down) { HideFilterEditor(true); e.Handled = true; }
            else if (e.Key == Key.Tab) { var n = _fCol + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); HideFilterEditor(false); if (n >= 0 && n < Model.ColCount) ShowFilterEditor(n); else _surface.Focus(); e.Handled = true; }
        };
        _editor.LostFocus += (_, _) => { if (_editing) Commit(0, 0); };
        BuildContextMenu();
    }

    public new void Focus() => _surface.Focus();

    // ------------------------------------------------------------------ cửa sổ lớn (1.52)
    /// <summary>Tiêu đề cửa sổ lớn (mặc định: tên trang đang mở).</summary>
    public string? LargeTitle { get; set; }
    /// <summary>Có nút ⤢ ở góc trên trái để mở bảng ra cửa sổ lớn (mặc định bật; lưới trong chính cửa sổ lớn thì tắt).</summary>
    public bool CanEnlarge { get; set; } = true;
    /// <summary>Cửa sổ lớn mở gần nhất (công cụ chụp màn hình dùng).</summary>
    internal static Window? LastLarge;

    /// <summary>
    /// Mở bảng ra cửa sổ lớn (≈ 92 % màn hình): cùng dữ liệu (cùng GridModel — sửa ở đâu cũng cập nhật cả hai), cùng định dạng,
    /// ô tích, bộ lọc riêng. Dùng cho bảng con / bảng trong khung hẹp chỉ thấy vài dòng.
    /// </summary>
    public async Task OpenLarge()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (_editing) Commit(0, 0);
        var g = new SheetGrid(Model)
        {
            SpareRows = SpareRows, EmptyText = EmptyText, Display = Display, AutoNumberFormat = AutoNumberFormat, CellBackground = CellBackground, CellForeground = CellForeground, CanEnlarge = false,
            CheckFill = CheckFill, Filterable = Filterable, Suggest = Suggest, TotalsRowFilter = TotalsRowFilter, TotalsKeepRow = TotalsKeepRow, RowBold = RowBold,
        };
        g.SuggestionPicked += (r, c, it) => SuggestionPicked?.Invoke(r, c, it);
        g.CheckCol = CheckCol; g.RowChecks = RowChecks; g._ticks = _ticks; g._tickVer = _tickVer;   // cửa sổ lớn dùng chung dòng đã tích
        g.ChecksChanged += () => { _sumDirty = true; Redraw(); ChecksChanged?.Invoke(); };
        foreach (var (k, f) in _filters) g._filters[k] = new ColumnFilter { Text = f.Text, Allowed = f.Allowed is null ? null : new HashSet<string>(f.Allowed) };
        var title = LargeTitle ?? owner.Title ?? "Bảng";
        var close = new Button { Content = "Đóng", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var info = new TextBlock { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
        void Info() => info.Text = g.IsFiltered ? $"Đang hiện {g.VCount:N0} / {Model.RowCount:N0} dòng" : $"{Model.RowCount:N0} dòng";
        g.FilterChanged += Info; Info();
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Right); bar.Children.Add(close); bar.Children.Add(info);
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom); root.Children.Add(bar); root.Children.Add(g);
        var scr = owner.Screens.ScreenFromWindow(owner)?.WorkingArea;
        var sc = owner.RenderScaling <= 0 ? 1 : owner.RenderScaling;
        var w = new Window
        {
            Title = title, Width = Math.Max(800, (scr?.Width ?? 1400) / sc * 0.92), Height = Math.Max(500, (scr?.Height ?? 860) / sc * 0.88),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = AppTheme.Card, MinWidth = 600, MinHeight = 360,
            Content = new Border { Padding = new Thickness(14, 12), Child = root },
        };
        close.Click += (_, _) => w.Close();
        LastLarge = w;
        var (row, col) = (Row, Col);
        w.Opened += (_, _) => { g.ApplyFilter(); g.Select(row, col); g.Focus(); };
        await w.ShowDialog(owner);
        Redraw();
    }

    internal void Redraw() { _totals.Update(); UpdateMinHeight(); _surface.InvalidateVisual(); if (_editing) PlaceEditor(); if (_fEditor.IsVisible) PlaceFilterEditor(); }

    /// <summary>Gọi sau khi đổi cột / nạp dữ liệu.</summary>
    public void Refresh()
    {
        VR = Math.Clamp(VR, 0, Math.Max(0, TotalRows - 1)); Col = Math.Clamp(Col, 0, Math.Max(0, Model.ColCount - 1));
        VA = VR; AnchorCol = Col;
        UpdateScroll(); Redraw();
    }

    /// <summary>1.85: mỗi bảng luôn thấy tối thiểu ngần này dòng (bảng chỉ đọc ít dòng hơn thì đủ số dòng của nó, ít nhất 3; bảng sửa được luôn đủ 8, phần dưới là dòng trống để gõ) — không đủ chỗ thì
    /// trang cuộn dọc (<see cref="FillScroll"/> ở MainWindow) thay vì ép bảng còn 2–3 dòng. 0 = không đặt.</summary>
    public int MinRows { get => _minRows; set { _minRows = Math.Max(0, value); UpdateMinHeight(); } }
    private int _minRows = 8;
    /// <summary>Số dòng tối thiểu thật sự áp cho bảng này (xem <see cref="MinRows"/>).</summary>
    internal int MinRowsEffective => _minRows <= 0 ? 0 : Model.ReadOnly ? Math.Min(_minRows, Math.Max(3, TotalRows)) : _minRows;
    /// <summary>Chiều cao tối thiểu để thấy <paramref name="rows"/> dòng: tiêu đề + dòng tìm kiếm (từ 6 dòng) + dòng Tổng + thanh cuộn ngang + viền.</summary>
    internal double MinHeightFor(int rows)
    {
        if (rows <= 0) return 0;
        var h = HeadH + RowH * rows + 2;
        if (_filterable && rows >= 6) h += FilterH;
        if (_totals.IsVisible && !double.IsNaN(_totals.Height)) h += _totals.Height;
        if (_hbar.IsVisible) h += Math.Max(12, _hbar.DesiredSize.Height);
        return Math.Ceiling(h);
    }
    private void UpdateMinHeight()
    {
        var h = MinHeightFor(MinRowsEffective);
        if (Math.Abs(MinHeight - h) > 0.5) MinHeight = h;
    }

    internal int TotalRows => VCount + (Model.ReadOnly || _filterOn ? 0 : SpareRows);
    internal double TotalWidth => Model.Columns.Sum(c => c.Width);
    internal int VisibleRows => Math.Max(1, (int)((_surface.Bounds.Height - TopH) / RowH));

    private void UpdateScroll()
    {
        var vis = VisibleRows;
        _vbar.Maximum = Math.Max(0, TotalRows - vis + 1);
        _vbar.ViewportSize = vis;
        _vbar.LargeChange = Math.Max(1, vis - 1);
        TopRow = Math.Clamp(TopRow, 0, (int)_vbar.Maximum);
        _vbar.Value = TopRow;
        var viewW = Math.Max(0, _surface.Bounds.Width - RowHeadW);
        _hbar.Maximum = Math.Max(0, TotalWidth - viewW + 20);
        _hbar.ViewportSize = viewW;
        _hbar.LargeChange = Math.Max(40, viewW - 40);
        OffsetX = Math.Clamp(OffsetX, 0, _hbar.Maximum);
        _hbar.Value = OffsetX;
        _hbar.IsVisible = _hbar.Maximum > 0;
        UpdateMinHeight();
    }

    internal double ColX(int c)
    {
        double x = RowHeadW - OffsetX;
        for (var i = 0; i < c && i < Model.ColCount; i++) x += Model.Columns[i].Width;
        return x;
    }

    internal (int r, int c) HitCell(Point p)
    {
        var r = p.Y < TopH ? -1 : TopRow + (int)((p.Y - TopH) / RowH);
        var c = -1;
        if (p.X >= RowHeadW)
        {
            double x = RowHeadW - OffsetX;
            for (var i = 0; i < Model.ColCount; i++) { x += Model.Columns[i].Width; if (p.X < x) { c = i; break; } }
            if (c < 0) c = Model.ColCount - 1;
        }
        return (Math.Min(r, TotalRows - 1), c);
    }

    // ------------------------------------------------------------------ chọn ô

    /// <summary>Chọn ô theo dòng DỮ LIỆU (dòng bị bộ lọc ẩn → chọn dòng đang hiện gần nhất).</summary>
    public void Select(int r, int c, bool extend = false) => SelectV(ToVisual(r), c, extend);

    internal void SelectV(int r, int c, bool extend = false)
    {
        r = Math.Clamp(r, 0, Math.Max(0, TotalRows - 1));
        var lastC = Model.ColCount - 1;
        while (lastC > 0 && Model.Columns[lastC].Width < 4) lastC--;      // không dừng ở cột ẩn cuối bảng (mã dòng, màu ô…)
        c = Math.Clamp(c, 0, Math.Max(0, lastC));
        VR = r; Col = c;
        if (!extend) { VA = r; AnchorCol = c; }
        ScrollIntoView(r, c);
        Redraw();
        SelectionChanged?.Invoke();
    }

    public void SelectRange(CellRange range) => SelectRangeV(new CellRange(ToVisual(range.R1), range.C1, ToVisual(range.R2), range.C2));

    internal void SelectRangeV(CellRange range)
    {
        VA = range.R1; AnchorCol = range.C1; VR = range.R2; Col = range.C2;
        Redraw(); SelectionChanged?.Invoke();
    }

    private void ScrollIntoView(int r, int c)
    {
        var vis = VisibleRows;
        if (_surface.Bounds.Height < TopH + RowH) vis = int.MaxValue / 2; // chưa có kích thước
        if (r < TopRow) TopRow = r;
        else if (r >= TopRow + vis) TopRow = r - vis + 1;
        var x = ColX(c) + OffsetX - RowHeadW;
        var w = c < Model.ColCount ? Model.Columns[c].Width : 0;
        var viewW = _surface.Bounds.Width - RowHeadW;
        if (viewW > 40)
        {
            if (x < OffsetX) OffsetX = x;
            else if (x + w > OffsetX + viewW) OffsetX = Math.Min(x, x + w - viewW);
        }
        UpdateScroll();
    }

    // ------------------------------------------------------------------ phím

    internal async void OnKey(KeyEventArgs e)
    {
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Alt): OpenFilterMenu(Col); break;
            case Key.Up: Move(ctrl ? Edge(-1, 0) : (VR - 1, Col), shift); break;
            case Key.Down: Move(ctrl ? Edge(1, 0) : (VR + 1, Col), shift); break;
            case Key.Left: Move(ctrl ? Edge(0, -1) : (VR, Col - 1), shift); break;
            case Key.Right: Move(ctrl ? Edge(0, 1) : (VR, Col + 1), shift); break;
            case Key.PageDown: Move((VR + VisibleRows - 1, Col), shift); break;
            case Key.PageUp: Move((VR - VisibleRows + 1, Col), shift); break;
            case Key.Home: Move(ctrl ? (0, 0) : (VR, 0), shift); break;
            case Key.End: Move(ctrl ? (Math.Max(0, (_map is null ? Model.UsedRows() : VCount) - 1), Model.ColCount - 1) : (VR, Model.ColCount - 1), shift); break;
            case Key.Tab: Move((VR, Col + (shift ? -1 : 1)), false); break;
            case Key.Enter: Move((VR + (shift ? -1 : 1), Col), false); break;
            case Key.F2: BeginEdit(null); break;
            case Key.F11 when CanEnlarge: _ = OpenLarge(); break;
            case Key.Space when SpaceTicks && !ctrl: ToggleRows(SelModelRows().Where(m => m < Model.RowCount).ToList()); break;
            case Key.Delete: ClearSelection(); break;
            case Key.Back: BeginEdit(""); break;
            case Key.A when ctrl: VA = 0; AnchorCol = 0; VR = Math.Max(0, VCount - 1); Col = Model.ColCount - 1; Redraw(); SelectionChanged?.Invoke(); break;
            case Key.C when ctrl: await CopyAsync(); break;
            case Key.X when ctrl: await CopyAsync(); ClearSelection(); break;
            case Key.V when ctrl: await PasteAsync(); break;
            case Key.Z when ctrl: Model.Undo(); break;
            case Key.Y when ctrl: Model.Redo(); break;
            case Key.D when ctrl: FillDownSelection(); break;
            case Key.R when ctrl: FillRightSelection(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Move((int r, int c) to, bool extend) => SelectV(to.r, to.c, extend);

    /// <summary>Các dòng dữ liệu (đang hiện) trong vùng chọn.</summary>
    private IEnumerable<int> SelModelRows() { var s = VSel; for (var v = s.R1; v <= s.R2; v++) yield return ToModel(v); }

    private void ClearSelection()
    {
        var s = VSel;
        if (SoftLock is { } lk)
        {   // 1.116: ô tự điền đang khóa tạm không bị Delete xóa nhầm (dòng hết phần nhập tay thì ô tự điền tự trống)
            var cells = SelModelRows().Where(m => m < Model.RowCount).SelectMany(m => Enumerable.Range(s.C1, s.Cols).Select(c => (m, c))).ToList();
            var free = cells.Where(x => !lk(x.m, x.c)).Select(x => (x.m, x.c, "")).ToList();
            if (free.Count < cells.Count) SoftLockHit?.Invoke();
            if (free.Count > 0) Model.SetCells(free);
            return;
        }
        if (_map is null) { Model.Clear(Selection); return; }
        Model.SetCells(SelModelRows().Where(m => m < Model.RowCount).SelectMany(m => Enumerable.Range(s.C1, s.Cols).Select(c => (m, c, ""))));
    }

    /// <summary>
    /// 1.116: ô "khóa tạm" (dòng dữ liệu, cột) — vd ô đã tự điền ở Nhập liệu: gõ / F2 / Backspace / Delete không sửa được, chỉ NHẤN ĐÚP mới sửa
    /// (tránh gõ nhầm đè). Dán / điền xuống vẫn ghi được.
    /// </summary>
    public Func<int, int, bool>? SoftLock { get; set; }
    /// <summary>Người dùng vừa chạm vào ô khóa tạm (để màn hình báo "nhấn đúp để sửa").</summary>
    public event Action? SoftLockHit;
    private bool Locked(int c) => SoftLock is { } lk && Row < Model.RowCount && c >= 0 && c < Model.ColCount && lk(Row, c);

    /// <summary>Ctrl+D như Excel: chép ô trên cùng của vùng chọn xuống các ô còn lại (chọn 1 ô → lấy ô ngay trên).
    /// 1.88: vùng chọn chỉ 1 dòng nhiều cột (vd hàng KIỆN / DÃY) → chép ô đầu tiên sang phải (= Ctrl+R).</summary>
    private void FillDownSelection()
    {
        if (Model.ReadOnly) return;
        var s = VSel;
        if (s.Rows < 2 && s.Cols > 1) { FillRightSelection(); return; }
        if (s.Rows < 2)
        {   // 1 ô: lấy giá trị ô ngay trên (Excel)
            if (s.R1 < 1) return;
            int up = ToModel(s.R1 - 1), me = ToModel(s.R1);
            Model.SetCells(Enumerable.Range(s.C1, s.Cols).Where(c => Writable(c)).Select(c => (me, c, Model.Get(up, c))));
            return;
        }
        var src = ToModel(s.R1);
        Model.SetCells(SelModelRows().Skip(1).SelectMany(m => Enumerable.Range(s.C1, s.Cols).Where(c => Writable(c)).Select(c => (m, c, Model.Get(src, c)))));
    }

    /// <summary>Ctrl+R như Excel: chép ô trái nhất của vùng chọn sang phải (từng dòng); chọn 1 ô → lấy ô ngay bên trái.</summary>
    private void FillRightSelection()
    {
        if (Model.ReadOnly) return;
        var s = VSel;
        int from = s.Cols > 1 ? s.C1 : s.C1 - 1, start = s.Cols > 1 ? s.C1 + 1 : s.C1;
        if (from < 0) return;
        Model.SetCells(SelModelRows().SelectMany(m => Enumerable.Range(start, s.C2 - start + 1).Where(c => Writable(c)).Select(c => (m, c, Model.Get(m, from)))));
    }
    private bool Writable(int c) => c >= 0 && c < Model.ColCount && !Model.Columns[c].ReadOnly;

    private void DeleteSelectedRows()
    {
        var keep = VSel.R1;
        if (_map is null) Model.DeleteRows(Selection.R1, Selection.R2);
        else foreach (var m in SelModelRows().Where(m => m < Model.RowCount).OrderByDescending(m => m).ToList()) Model.DeleteRows(m, m);
        SelectV(keep, Col);
    }

    /// <summary>Ctrl+mũi tên: nhảy tới mép khối dữ liệu như Excel.</summary>
    private (int, int) Edge(int dr, int dc)
    {
        int r = VR, c = Col;
        bool Filled(int rr, int cc) => !string.IsNullOrEmpty(Model.Get(ToModel(rr), cc));
        int maxR = Math.Max(0, VCount - 1), maxC = Model.ColCount - 1;
        bool In(int rr, int cc) => rr >= 0 && cc >= 0 && rr <= maxR && cc <= maxC;
        if (!In(r + dr, c + dc)) return (Math.Clamp(r + dr, 0, maxR), Math.Clamp(c + dc, 0, maxC));
        if (Filled(r, c) && Filled(r + dr, c + dc))
        {
            while (In(r + dr, c + dc) && Filled(r + dr, c + dc)) { r += dr; c += dc; }
        }
        else
        {
            r += dr; c += dc;
            while (In(r, c) && !Filled(r, c) && In(r + dr, c + dc)) { r += dr; c += dc; }
        }
        return (r, c);
    }

    internal void OnText(string text)
    {
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0])) return;
        if (text == " " && SpaceTicks) return;   // phím cách = tích dòng
        BeginEdit(text);
    }

    // ------------------------------------------------------------------ sửa ô

    public void BeginEdit(string? initial, bool force = false)
    {
        if (Model.ReadOnly || Col >= Model.ColCount || Model.Columns[Col].ReadOnly) return;
        if (!force && Locked(Col)) { SoftLockHit?.Invoke(); return; }
        VA = VR; AnchorCol = Col;
        HideFilterEditor(false);
        _editing = true; _typedEdit = initial is not null;
        _editor.Text = initial ?? Model.Get(Row, Col);
        _editor.IsVisible = true;
        PlaceEditor();
        _editor.Focus();
        _editor.CaretIndex = _editor.Text?.Length ?? 0;
        _editor.SelectionStart = _editor.SelectionEnd = _editor.CaretIndex;
    }

    private void PlaceEditor()
    {
        var x = ColX(Col); var y = TopH + (VR - TopRow) * RowH;
        var w = Col < Model.ColCount ? Model.Columns[Col].Width : 100;
        Canvas.SetLeft(_editor, x - 1); Canvas.SetTop(_editor, y - 1);
        _editor.Width = Math.Max(w + 2, 60); _editor.Height = RowH + 2;
        _editor.IsVisible = _editing && VR >= TopRow && VR < TopRow + VisibleRows + 1;
    }

    private void Commit(int dr, int dc)
    {
        if (!_editing) return;
        _editing = false; CloseSuggest();
        _editor.IsVisible = false;
        var v = _editor.Text ?? "";
        if (v != Model.Get(Row, Col)) Model.Set(Row, Col, v);
        _surface.Focus();
        if (dr != 0 || dc != 0) SelectV(VR + dr, Col + dc); else Redraw();
    }

    private void Cancel()
    {
        _editing = false; CloseSuggest(); _editor.IsVisible = false; _surface.Focus(); Redraw();
    }

    private void OnEditorKey(object? sender, KeyEventArgs e)
    {
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_sug.IsOpen && _sugItems.Count > 0)
        {
            switch (e.Key)
            {
                case Key.Down: _sugSel = Math.Min(_sugItems.Count - 1, _sugSel + 1); PaintSuggest(); e.Handled = true; return;
                case Key.Up: _sugSel = Math.Max(-1, _sugSel - 1); PaintSuggest(); e.Handled = true; return;
                case Key.Enter or Key.Tab when _sugSel >= 0: PickSuggest(_sugSel); e.Handled = true; return;
                case Key.Escape: CloseSuggest(); e.Handled = true; return;
            }
        }
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                var i = _editor.CaretIndex; _editor.Text = (_editor.Text ?? "").Insert(i, "\n"); _editor.CaretIndex = i + 1; break;
            case Key.Enter: Commit(shift ? -1 : 1, 0); break;
            case Key.Tab: Commit(0, shift ? -1 : 1); break;
            case Key.Escape: Cancel(); break;
            case Key.Up when _typedEdit: Commit(-1, 0); break;
            case Key.Down when _typedEdit: Commit(1, 0); break;
            case Key.Left when _typedEdit && _editor.CaretIndex == 0: Commit(0, -1); break;
            case Key.Right when _typedEdit && _editor.CaretIndex >= (_editor.Text?.Length ?? 0): Commit(0, 1); break;
            default: return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ gợi ý khi gõ (1.54 — vd cột Mã hàng của Nhập liệu)

    /// <summary>Một gợi ý: <see cref="Value"/> là chữ điền vào ô; <see cref="Label"/> / <see cref="Detail"/> để hiện; <see cref="Tag"/> tuỳ nơi dùng.</summary>
    public sealed record GridSuggestion(string Value, string Label, string Detail, object? Tag = null);
    /// <summary>Gợi ý cho (cột, chữ đang gõ) — null / rỗng = không gợi ý. Chọn bằng ↑ ↓ + Enter / Tab hoặc bấm chuột.</summary>
    public Func<int, string, IReadOnlyList<GridSuggestion>>? Suggest { get; set; }
    /// <summary>Đã chọn 1 gợi ý (dòng dữ liệu, cột, gợi ý) — sau khi ô đã nhận giá trị.</summary>
    public event Action<int, int, GridSuggestion>? SuggestionPicked;
    private readonly Popup _sug = new() { Placement = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = false };
    private readonly StackPanel _sugList = new() { Spacing = 1 };
    private List<GridSuggestion> _sugItems = new();
    private int _sugSel = -1;

    private void UpdateSuggest()
    {
        if (!_editing || Suggest is null || Col < 0 || Col >= Model.ColCount) { CloseSuggest(); return; }
        IReadOnlyList<GridSuggestion> items;
        try { items = Suggest(Col, _editor.Text ?? "") ?? Array.Empty<GridSuggestion>(); } catch { items = Array.Empty<GridSuggestion>(); }
        _sugItems = items.ToList(); _sugSel = -1;
        if (_sugItems.Count == 0) { CloseSuggest(); return; }
        PaintSuggest();
        _sug.PlacementTarget = _editor;
        _sug.IsOpen = true;
    }
    private void PaintSuggest()
    {
        _sugList.Children.Clear();
        for (var i = 0; i < _sugItems.Count; i++)
        {
            var it = _sugItems[i]; var idx = i;
            var row = new Border
            {
                Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(4), Cursor = new Cursor(StandardCursorType.Hand),
                Background = i == _sugSel ? AppTheme.Brush("#E6F0FC") : Brushes.Transparent,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = it.Label, FontWeight = FontWeight.SemiBold, FontSize = 13 },
                        new TextBlock { Text = it.Detail, FontSize = 11.5, Foreground = AppTheme.Brush("#5D6B78"), TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                },
            };
            row.PointerPressed += (_, e) => { e.Handled = true; PickSuggest(idx); };
            _sugList.Children.Add(row);
        }
        _sugList.Children.Add(new TextBlock { Text = "↑ ↓ chọn · Enter điền · Esc bỏ qua", FontSize = 11, Foreground = AppTheme.Brush("#7A8797"), Margin = new Thickness(8, 3, 8, 2) });
    }
    private void CloseSuggest() { if (_sug.IsOpen) _sug.IsOpen = false; _sugItems.Clear(); _sugSel = -1; }
    private void PickSuggest(int i)
    {
        if (i < 0 || i >= _sugItems.Count) return;
        var it = _sugItems[i]; var r = Row; var c = Col;
        _editor.Text = it.Value;
        CloseSuggest();
        Commit(1, 0);
        SuggestionPicked?.Invoke(r, c, it);
    }

    // ------------------------------------------------------------------ chép / dán

    public async Task CopyAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(CopyTextOf(VSel));
    }

    /// <summary>Chữ chép của vùng (theo dòng đang hiện — dòng bị lọc ẩn không chép, như Excel).</summary>
    private string CopyTextOf(CellRange v)
    {
        if (_map is null) return Model.CopyText(v);
        var sb = new System.Text.StringBuilder();
        for (var r = v.R1; r <= v.R2; r++) { var m = ToModel(r); sb.Append(Model.CopyText(new CellRange(m, v.C1, m, v.C2))); }
        return sb.ToString();
    }

    public async Task CopyAllAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb || Model.ColCount == 0) return;
        var head = string.Join("\t", Model.Columns.Select(c => c.Header)) + "\r\n";
        await cb.SetTextAsync(head + CopyTextOf(new CellRange(0, 0, Math.Max(0, (_map is null ? Model.UsedRows() : VCount) - 1), Model.ColCount - 1)));
    }

    public async Task PasteAsync()
    {
        if (Model.ReadOnly) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb) return;
        var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(cb);
        if (string.IsNullOrEmpty(text)) return;
        if (_map is null) { SelectRange(Model.Paste(Selection, text)); return; }
        // đang lọc: dán lần lượt vào các dòng đang hiện (không đè dòng bị ẩn)
        var data = GridModel.ParseTsv(text);
        if (data.Count == 0) return;
        var s = VSel;
        var cells = new List<(int, int, string)>();
        if (data.Count == 1 && data[0].Count == 1)
            cells.AddRange(SelModelRows().Where(m => m < Model.RowCount).SelectMany(m => Enumerable.Range(s.C1, s.Cols).Select(c => (m, c, data[0][0]))));
        else
            for (var i = 0; i < data.Count && s.R1 + i < VCount; i++)
                for (var j = 0; j < data[i].Count && s.C1 + j < Model.ColCount; j++) cells.Add((ToModel(s.R1 + i), s.C1 + j, data[i][j]));
        Model.SetCells(cells);
        SelectRangeV(new CellRange(s.R1, s.C1, Math.Min(VCount - 1, s.R1 + Math.Max(data.Count, s.Rows) - 1), Math.Min(Model.ColCount - 1, s.C1 + Math.Max(data.Max(x => x.Count), s.Cols) - 1)));
    }

    /// <summary>Menu chuột phải sắp mở (sau khi bảng đã chọn ô dưới chuột): View ẩn / hiện / đổi chữ các mục riêng đã gắn bằng <see cref="AddMenuItems"/>.</summary>
    public event Action? MenuOpening;

    /// <summary>Thêm mục vào menu chuột phải của bảng (sau một vạch ngăn, cuối menu).</summary>
    /// <returns>Vạch ngăn đứng trước các mục (View ẩn cùng khi ẩn hết các mục).</returns>
    public Separator? AddMenuItems(params Control[] items)
    {
        if (_surface.ContextMenu is not ContextMenu menu || items.Length == 0) return null;
        var sep = new Separator();
        menu.Items.Add(sep);
        foreach (var i in items) menu.Items.Add(i);
        return sep;
    }

    private void BuildContextMenu()
    {
        MenuItem Item(string header, Action a, string? gesture = null)
        {
            var m = new MenuItem { Header = header };
            if (gesture is not null) m.InputGesture = KeyGesture.Parse(gesture);
            m.Click += (_, _) => a();
            return m;
        }
        var menu = new ContextMenu
        {
            Items =
            {
                Item("Cắt", async () => { await CopyAsync(); ClearSelection(); }, "Ctrl+X"),
                Item("Chép", async () => await CopyAsync(), "Ctrl+C"),
                Item("Dán", async () => await PasteAsync(), "Ctrl+V"),
                new Separator(),
                Item("Chèn dòng phía trên", () => Model.InsertRows(Selection.R1, Selection.Rows)),
                Item("Chèn dòng phía dưới", () => Model.InsertRows(Selection.R2 + 1, Selection.Rows)),
                Item("Xóa dòng đang chọn", DeleteSelectedRows),
                Item("Xóa nội dung", ClearSelection, "Delete"),
                new Separator(),
                Item("Điền xuống", FillDownSelection, "Ctrl+D"),
                Item("Điền sang phải", FillRightSelection, "Ctrl+R"),
                Item("Hoàn tác", () => Model.Undo(), "Ctrl+Z"),
                Item("Làm lại", () => Model.Redo(), "Ctrl+Y"),
            },
        };
        var big = Item("Mở bảng ra cửa sổ lớn", async () => await OpenLarge(), "F11");
        var selAll = Item("Chọn tất cả", () => SelectRangeV(new CellRange(0, 0, Math.Max(0, VCount - 1), Math.Max(0, Model.ColCount - 1))), "Ctrl+A");
        // 1.69: giữ thẳng các vạch ngăn (trước đây tìm theo vị trí menu.Items[1] / [2] / [6] — vị trí [2] là mục "Lọc cột này…" chứ không phải
        // vạch ngăn → InvalidCastException "Specified cast is not valid" mỗi lần chuột phải ở bảng có lọc, vd Tồn kho gửi › Gộp đặc tính).
        var sepBig = new Separator(); var sepFilter = new Separator(); var sepSum = new Separator();
        menu.Items.Insert(0, big); menu.Items.Insert(1, sepBig);
        menu.Items.Add(new Separator()); menu.Items.Add(selAll);
        var sepSort = new Separator();
        var sAz = Item("Sắp xếp A → Z", () => SetSort(Col, false));
        var sZa = Item("Sắp xếp Z → A", () => SetSort(Col, true));
        var sNo = Item("Bỏ sắp xếp", ClearSort);
        var fit = Item("Co giãn cột vừa dữ liệu", () => { var v = VSel; for (var c = v.C1; c <= v.C2; c++) AutoFitColumn(c); });
        menu.Items.Add(sepSort); menu.Items.Add(sAz); menu.Items.Add(sZa); menu.Items.Add(sNo); menu.Items.Add(fit);
        // 1.93: thao tác với dòng đã tích (ô tích chọn dòng)
        var sepTick = new Separator();
        var tDel = Item("Xóa dòng đã tích", () => { var rows = CheckedRows.OrderByDescending(m => m).ToList(); SetChecked(rows, false); foreach (var m in rows) Model.DeleteRows(m, m); });
        var tCopy = Item("Chép dòng đã tích", async () =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb) return;
            await cb.SetTextAsync(string.Concat(CheckedRows.Select(m => Model.CopyText(new CellRange(m, 0, m, Model.ColCount - 1)))));
        });
        var tClear = Item("Bỏ tích tất cả", () => SetChecked(CheckedRows, false));
        menu.Items.Add(sepTick); menu.Items.Add(tCopy); menu.Items.Add(tDel); menu.Items.Add(tClear);
        var fMenu = Item("Lọc cột này…", () => OpenFilterMenu(Col), "Alt+Down");
        var fClear = Item("Bỏ mọi bộ lọc", ClearFilters);
        menu.Items.Insert(2, fMenu); menu.Items.Insert(3, fClear); menu.Items.Insert(4, sepFilter);
        var sumItem = new MenuItem { ToggleType = MenuItemToggleType.CheckBox };
        sumItem.Click += (_, _) => { if (Col >= 0 && Col < Model.ColCount) { ToggleSum(Model.Columns[Col].Header); } };
        menu.Items.Insert(5, sumItem); menu.Items.Insert(6, sepSum);
        menu.Opening += (_, _) =>
        {
            big.IsVisible = sepBig.IsVisible = CanEnlarge;
            sepSort.IsVisible = sAz.IsVisible = sZa.IsVisible = Sortable && Col >= 0 && Col < Model.ColCount; sNo.IsVisible = Sortable && _sortKey is not null;
            var nt = TickMode ? CheckedRows.Count : 0;
            sepTick.IsVisible = tCopy.IsVisible = tClear.IsVisible = nt > 0; tDel.IsVisible = nt > 0 && !Model.ReadOnly;
            tCopy.Header = $"Chép {nt:N0} dòng đã tích"; tDel.Header = $"Xóa {nt:N0} dòng đã tích";
            fMenu.IsVisible = fClear.IsVisible = sepFilter.IsVisible = _filterable;
            fClear.IsEnabled = _filterOn;
            fClear.Header = !_filterOn || _map is null ? "Bỏ mọi bộ lọc" : $"Bỏ mọi bộ lọc (đang hiện {_map.Count:N0} / {Model.RowCount:N0} dòng)";
            var okc = ShowTotals && Col >= 0 && Col < Model.ColCount;
            sumItem.IsVisible = sepSum.IsVisible = okc;
            if (okc) { sumItem.Header = $"Cộng cột “{Model.Columns[Col].Header}” ở dòng Tổng"; sumItem.IsChecked = SumCol(Col); }
            try { MenuOpening?.Invoke(); } catch { /* mục riêng của View lỗi thì vẫn mở menu chung */ }
        };
        _surface.ContextMenu = menu;
    }

    // ------------------------------------------------------------------ dòng tổng (1.54)

    private readonly TotalsBar _totals;
    private bool _sumDirty = true;
    private double?[] _sumAll = Array.Empty<double?>(), _sumChk = Array.Empty<double?>();
    private int _chkCount;
    /// <summary>Có dòng "Tổng" cố định ở đáy bảng (cộng các dòng đang hiện; bảng có ô tích thêm dòng "Đã chọn"). Mặc định bật; tự ẩn khi không có cột nào để cộng hoặc bảng quá thấp.</summary>
    public bool ShowTotals { get; set; } = true;
    /// <summary>Dòng trạng thái ngắn ("112 dòng gốc · 98 nhóm") hiện ở dòng Tổng, sau số dòng (1.84: không còn dòng chữ riêng trên bảng).</summary>
    public string Status { get => _status; set { if (_status == (value ?? "")) return; _status = value ?? ""; _totals?.Update(); } }
    private string _status = "";
    private static HashSet<string>? _sumOn, _sumOff;
    private const string KeyOn = "ui_sum_on", KeyOff = "ui_sum_off";
    private static void LoadSumPrefs()
    {
        if (_sumOn is not null) return;
        HashSet<string> Read(string k) { try { return App.Services.Settings.Get(k).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(); } catch { return new(); } }
        _sumOn = Read(KeyOn); _sumOff = Read(KeyOff);
    }
    /// <summary>Cột c có được cộng ở dòng Tổng không (người dùng bật / tắt theo tên cột, còn lại theo <see cref="GridTotals.DefaultSum"/>).</summary>
    internal bool SumCol(int c)
    {
        if (c < 0 || c >= Model.ColCount || Model.Columns[c].Width < 4) return false;
        LoadSumPrefs();
        var k = GridTotals.Key(Model.Columns[c].Header);
        return _sumOn!.Contains(k) || (!_sumOff!.Contains(k) && GridTotals.DefaultSum(Model.Columns[c].Header));
    }
    private void ToggleSum(string header)
    {
        LoadSumPrefs();
        var k = GridTotals.Key(header); var on = !SumCol(Model.Columns.ToList().FindIndex(x => x.Header == header));
        _sumOn!.Remove(k); _sumOff!.Remove(k);
        if (on != GridTotals.DefaultSum(header)) (on ? _sumOn : _sumOff).Add(k);
        try { App.Services.Settings.Set(KeyOn, string.Join("\n", _sumOn)); App.Services.Settings.Set(KeyOff, string.Join("\n", _sumOff)); } catch { /* chỉ là tuỳ chọn hiển thị */ }
        _sumDirty = true; Redraw();
    }
    private (int, int, int) _sumVer = (-1, -1, -1);
    private void ComputeSums()
    {
        var ver = (Model.Version, Model.Loads, Model.ColCount);
        if (!_sumDirty && ver == _sumVer) return;
        _sumDirty = false; _sumVer = ver;
        var rows = ShownRows;
        _sumAll = GridTotals.Sum(Model, rows, SumCol, TotalsRowFilter, TotalsKeepRow);
        _chkCount = 0;
        if (HasCheck)
        {
            _chkCount = rows.Count(IsChecked);
            _sumChk = _chkCount > 0 ? GridTotals.Sum(Model, rows, SumCol, r => IsChecked(r) && TotalsRowFilter?.Invoke(r) != false, TotalsKeepRow) : Array.Empty<double?>();
        }
        else _sumChk = Array.Empty<double?>();
    }

    /// <summary>Dòng "Tổng" (và "Đã chọn") vẽ thẳng hàng với các cột của bảng, cuộn ngang theo bảng.</summary>
    private sealed class TotalsBar : Control
    {
        private readonly SheetGrid _g;
        private static readonly IBrush Bg = AppTheme.Brush("#F3F6FA");
        private static readonly IBrush ChkBg = AppTheme.Brush("#E6F0FC");
        private static readonly IPen Top = new Pen(AppTheme.Brush("#B9C3D0"), 1);
        private static readonly IPen Sep = new Pen(AppTheme.Brush("#DDE3EA"), 1);
        private static readonly IBrush Fg = AppTheme.Brush("#1A2433");
        private static readonly IBrush Mut = AppTheme.Brush("#5D6B78");
        private static readonly IBrush AccentFg = AppTheme.Brush("#1859AE");
        private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
        private static readonly Typeface Norm = new(FontFamily.Default);
        public TotalsBar(SheetGrid g) { _g = g; ClipToBounds = true; }

        public void Update()
        {
            var g = _g;
            var show = g.ShowTotals && g.Model.ColCount > 0 && (g.Bounds.Height <= 0 || g.Bounds.Height >= HeadH + RowH * 5 + 30);
            if (show)
            {
                g.ComputeSums();
                show = g._sumAll.Any(x => x.HasValue) || g._status.Length > 0;
            }
            var h = show ? RowH * (g._chkCount > 0 ? 2 : 1) + 1 : 0;
            if (IsVisible != show) IsVisible = show;
            if (double.IsNaN(Height) || Math.Abs(Height - h) > 0.5) Height = h;
            InvalidateVisual();
        }

        public override void Render(DrawingContext dc)
        {
            var g = _g; var b = Bounds; if (b.Width <= 0 || b.Height <= 0) return;
            var rh = RowH; var rhw = g.RowHeadW;
            var lines = new List<(string Label, double?[] Sums, IBrush Bg, string Count)>
            {
                ("Tổng", g._sumAll, Bg, $"{g.VCount:#,0} dòng" + (g._status.Length > 0 ? " · " + g._status : "")),
            };
            if (g._chkCount > 0) lines.Add(("Đã chọn", g._sumChk, ChkBg, $"{g._chkCount:#,0} dòng"));
            for (var i = 0; i < lines.Count; i++)
            {
                var (label, sums, bg, cnt) = lines[i];
                var y = 1 + i * rh;
                dc.FillRectangle(bg, new Rect(0, y, b.Width, rh));
                Text(dc, label, Bold, i == 0 ? Fg : AccentFg, new Rect(6, y, rhw - 8, rh), left: true);
                using (dc.PushClip(new Rect(rhw, y, Math.Max(0, b.Width - rhw), rh)))
                {
                    var x = rhw - g.OffsetX; var cntDone = false; var spanEnd = double.MinValue;
                    for (var c = 0; c < g.Model.ColCount; c++)
                    {
                        var w = g.Model.Columns[c].Width;
                        if (x + w > rhw && x < b.Width)
                        {
                            if (c < sums.Length && sums[c] is { } v)
                                Text(dc, NumFmt.Of(v, g.FmtClass(c)), Bold, i == 0 ? Fg : AccentFg, new Rect(x + 4, y, w - 8, rh), left: false);
                            else if (!cntDone && w >= 60 && !(c < sums.Length && g.SumCol(c)))
                            {
                                // chữ đếm dòng (+ trạng thái) giãn qua các cột không có số cộng kế tiếp
                                var span = w;
                                for (var k = c + 1; k < g.Model.ColCount && !(k < sums.Length && sums[k].HasValue); k++) span += g.Model.Columns[k].Width;
                                if (g._status.Length == 0) span = w;
                                Text(dc, cnt, Norm, Mut, new Rect(x + 6, y, span - 10, rh), left: true); cntDone = true; spanEnd = x + span - 1;
                            }
                        }
                        else if (x + w <= rhw) { /* cột đã cuộn khuất bên trái */ }
                        x += w;
                        if (x < b.Width && x >= spanEnd) dc.DrawLine(Sep, new Point(x, y), new Point(x, y + rh));
                    }
                }
            }
            dc.DrawLine(Top, new Point(0, 0.5), new Point(b.Width, 0.5));
        }

        private static void Text(DrawingContext dc, string s, Typeface tf, IBrush fg, Rect r, bool left)
        {
            if (r.Width <= 4) return;
            var ft = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, 12.5, fg) { MaxTextWidth = r.Width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            var x = left ? r.X : r.Right - ft.Width;
            dc.DrawText(ft, new Point(x, r.Y + (r.Height - ft.Height) / 2));
        }
    }

    // ------------------------------------------------------------------ bề mặt vẽ

    private sealed class GridSurface : Control
    {
        private readonly SheetGrid _g;
        private static readonly IBrush HeadBg = AppTheme.Brush("#EDF1F6");
        private static readonly IBrush HeadSelBg = AppTheme.Brush("#C3D8F5");   // 1.94: đậm hơn (người dùng: "không tô đen được")
        private static readonly IBrush SelBg = AppTheme.Brush("#CFE0F8");
        private static readonly IPen Line = new Pen(AppTheme.Brush("#DDE3EA"), 1);
        private static readonly IPen HeadLine = new Pen(AppTheme.Brush("#B9C3D0"), 1);
        private static readonly IPen SelPen = new Pen(AppTheme.Brush("#1F6FD6"), 2);
        private static readonly IBrush Text = AppTheme.Brush("#1A2433");
        private static readonly IBrush HeadText = AppTheme.Brush("#1A2433");
        private static readonly Typeface Face = new(FontFamily.Default);
        private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        private static readonly Typeface HeadFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);   // tiêu đề cột đậm, sẫm — dễ đọc (1.49)
        private enum Drag { None, Cells, Rows, Cols, Resize }
        private Drag _drag;
        private int _headDown = -1; private bool _headMoved;   // bấm tiêu đề không kéo sang cột khác = sắp xếp (1.71)
        private int _resizeCol; private double _resizeStartX, _resizeStartW;

        public GridSurface(SheetGrid g) { _g = g; Cursor = Cursor.Default; }

        private static readonly IBrush Accent = AppTheme.Brush("#1F6FD6");
        private static readonly IBrush Muted = AppTheme.Brush("#5D6B78");
        private static readonly IBrush FilterOnBg = AppTheme.Brush("#E6F0FC");
        private static readonly Typeface Semi = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        internal const double DropW = 18;   // vùng nút ▾ ở mép phải tiêu đề cột

        public override void Render(DrawingContext dc)
        {
            var m = _g.Model;
            var b = Bounds;
            var top = _g.TopH; var rh = SheetGrid.RowH; var hh = SheetGrid.HeadH;
            dc.FillRectangle(AppTheme.Card, new Rect(b.Size));
            var rhw = _g.RowHeadW;
            var sel = _g.VSel;
            var vis = _g.VisibleRows + 1;
            var last = Math.Min(_g.TotalRows - 1, _g.TopRow + vis);
            var filtered = _g.IsFiltered;
            // cột đang thấy
            var cols = new List<(int c, double x, double w)>();
            double x = rhw - _g.OffsetX;
            for (var c = 0; c < m.ColCount; c++)
            {
                var w = m.Columns[c].Width;
                if (x + w > rhw && x < b.Width) cols.Add((c, x, w));
                x += w;
            }
            var right = Math.Min(b.Width, x);
            // ô
            using (dc.PushClip(new Rect(rhw, top, Math.Max(0, b.Width - rhw), Math.Max(0, b.Height - top))))
            {
                for (var r = _g.TopRow; r <= last; r++)
                {
                    var y = top + (r - _g.TopRow) * rh;
                    var mr = _g.ToModel(r);
                    foreach (var (c, cx, cw) in cols)
                    {
                        var rect = new Rect(cx, y, cw, rh);
                        var bg = _g.CellBackground?.Invoke(mr, c);
                        if (sel.Contains(r, c) && !sel.Single && !(r == _g.VR && c == _g.Col)) dc.FillRectangle(SelBg, rect);
                        else if (bg is not null) dc.FillRectangle(bg, rect);
                        var raw = m.Get(mr, c);
                        if (raw.Length == 0 && _g.Display is null) continue;
                        var shown = _g.ShowText(mr, c, raw);
                        if (shown.Length == 0) continue;
                        var nl = shown.IndexOfAny(new[] { '\r', '\n' });
                        if (nl >= 0) shown = shown[..nl] + " …";
                        var ft = new FormattedText(shown, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _g.RowBold?.Invoke(mr) == true ? HeadFace : Face, 13, _g.CellForeground?.Invoke(mr, c) ?? Text);
                        var num = m.Columns[c].Type == "number" || (m.Columns[c].Type == "text" && LooksNumber(raw));
                        var tx = num ? cx + cw - ft.Width - 6 : cx + 6;
                        using (dc.PushClip(rect.Deflate(new Thickness(2, 0))))
                            dc.DrawText(ft, new Point(Math.Max(cx + 4, tx), y + (rh - ft.Height) / 2));
                    }
                }
                // đường kẻ
                for (var r = _g.TopRow; r <= last + 1; r++)
                {
                    var y = top + (r - _g.TopRow) * rh + 0.5;
                    dc.DrawLine(Line, new Point(rhw, y), new Point(right, y));
                }
                foreach (var (_, cx, cw) in cols) dc.DrawLine(Line, new Point(cx + cw - 0.5, top), new Point(cx + cw - 0.5, b.Height));
                // khung vùng chọn
                if (m.ColCount > 0)
                {
                    var x1 = _g.ColX(sel.C1); var x2 = _g.ColX(sel.C2) + m.Columns[Math.Min(sel.C2, m.ColCount - 1)].Width;
                    var y1 = top + (sel.R1 - _g.TopRow) * rh; var y2 = top + (sel.R2 - _g.TopRow + 1) * rh;
                    dc.DrawRectangle(null, SelPen, new Rect(x1, y1, x2 - x1, y2 - y1).Deflate(1));
                    dc.FillRectangle(SelPen.Brush!, new Rect(x2 - 4, y2 - 4, 6, 6));
                }
            }
            // tiêu đề cột (+ nút ▾ lọc theo danh sách)
            dc.FillRectangle(HeadBg, new Rect(0, 0, b.Width, hh));
            using (dc.PushClip(new Rect(rhw, 0, Math.Max(0, b.Width - rhw), hh)))
                foreach (var (c, cx, cw) in cols)
                {
                    var f = _g.Filterable ? _g.FilterOf(c) : null;
                    var drop = _g.Filterable && cw >= 46;
                    if (c >= sel.C1 && c <= sel.C2) dc.FillRectangle(HeadSelBg, new Rect(cx, 0, cw, hh));
                    var avail = cw - (drop ? DropW : 0);
                    var sorted = c == _g.SortCol;
                    var head = sorted ? m.Columns[c].Header + (_g.SortDesc ? " ▼" : " ▲") : m.Columns[c].Header;
                    var ft = new FormattedText(head, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HeadFace, 13, f is not null || sorted ? Accent : HeadText);
                    using (dc.PushClip(new Rect(cx + 2, 0, Math.Max(0, avail - 4), hh)))
                        dc.DrawText(ft, new Point(cx + Math.Max(4, (avail - ft.Width) / 2), (hh - ft.Height) / 2));
                    if (drop)
                    {
                        var gx = cx + cw - DropW + 3; var gy = hh / 2;
                        if (f?.Allowed is not null)
                        {   // đang lọc theo danh sách: phễu xanh trên nền nhạt
                            dc.FillRectangle(FilterOnBg, new Rect(cx + cw - DropW, 4, DropW - 2, hh - 8));
                            var fun = new StreamGeometry();
                            using (var g = fun.Open()) { g.BeginFigure(new Point(gx, gy - 5), true); g.LineTo(new Point(gx + 11, gy - 5)); g.LineTo(new Point(gx + 7, gy)); g.LineTo(new Point(gx + 7, gy + 5)); g.LineTo(new Point(gx + 4, gy + 4)); g.LineTo(new Point(gx + 4, gy)); g.EndFigure(true); }
                            dc.DrawGeometry(Accent, null, fun);
                        }
                        else
                        {
                            var tri = new StreamGeometry();
                            using (var g = tri.Open()) { g.BeginFigure(new Point(gx + 1.5, gy - 2), true); g.LineTo(new Point(gx + 9.5, gy - 2)); g.LineTo(new Point(gx + 5.5, gy + 3)); g.EndFigure(true); }
                            dc.DrawGeometry(Muted, null, tri);
                        }
                    }
                    dc.DrawLine(HeadLine, new Point(cx + cw - 0.5, 0), new Point(cx + cw - 0.5, hh));
                }
            dc.DrawLine(HeadLine, new Point(0, hh - 0.5), new Point(b.Width, hh - 0.5));
            // dòng tìm kiếm riêng từng cột
            if (_g.ShowFilterRow)
            {
                dc.FillRectangle(AppTheme.Card, new Rect(0, hh, b.Width, SheetGrid.FilterH));
                using (dc.PushClip(new Rect(rhw, hh, Math.Max(0, b.Width - rhw), SheetGrid.FilterH)))
                    foreach (var (c, cx, cw) in cols)
                    {
                        if (cw < 24) continue;   // cột ẩn (rộng 0, giữ mã dòng) / quá hẹp: không có ô tìm
                        var q = _g.FilterOf(c)?.Text ?? "";
                        var cell = new Rect(cx, hh, cw, SheetGrid.FilterH);
                        if (q.Trim().Length > 0) dc.FillRectangle(FilterOnBg, cell);
                        var ix = cx + 7; var iy = hh + SheetGrid.FilterH / 2;
                        var lens = new Pen(q.Length > 0 ? Accent : Muted, 1.3);
                        dc.DrawEllipse(null, lens, new Point(ix + 3.5, iy - 1), 3.5, 3.5);
                        dc.DrawLine(lens, new Point(ix + 6, iy + 1.5), new Point(ix + 8.5, iy + 4));
                        var ft = q.Length > 0
                            ? new FormattedText(q, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Semi, 12.5, Accent)
                            : new FormattedText("Tìm…", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 12, Muted) ;
                        using (dc.PushClip(cell.Deflate(new Thickness(2, 0))))
                        {
                            if (q.Length == 0) using (dc.PushOpacity(0.7)) dc.DrawText(ft, new Point(ix + 14, iy - ft.Height / 2));
                            else dc.DrawText(ft, new Point(ix + 14, iy - ft.Height / 2));
                        }
                        dc.DrawLine(Line, new Point(cx + cw - 0.5, hh), new Point(cx + cw - 0.5, top));
                    }
                dc.DrawLine(HeadLine, new Point(0, top - 0.5), new Point(b.Width, top - 0.5));
            }
            // trạng thái trống: bảng chỉ đọc không có dòng nào / bộ lọc không còn dòng nào
            if (m.ColCount > 0 && _g.VCount == 0 && (filtered || (m.ReadOnly && !string.IsNullOrEmpty(_g.EmptyText))))
            {
                var msg = filtered ? "Không có dòng nào khớp bộ lọc — bấm chuột phải › Bỏ mọi bộ lọc." : _g.EmptyText!;
                var et = new FormattedText(msg, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 13, HeadText);
                dc.DrawText(et, new Point(Math.Max(rhw + 12, (b.Width - et.Width) / 2), top + 22));
            }
            // số dòng (số dòng dữ liệu gốc — khi lọc hiện chữ xanh như Excel)
            dc.FillRectangle(HeadBg, new Rect(0, hh, rhw, b.Height));
            using (dc.PushClip(new Rect(0, top, rhw, Math.Max(0, b.Height - top))))
                for (var r = _g.TopRow; r <= last; r++)
                {
                    var y = top + (r - _g.TopRow) * rh;
                    if (r >= sel.R1 && r <= sel.R2) dc.FillRectangle(HeadSelBg, new Rect(0, y, rhw, rh));
                    var mr = _g.ToModel(r);
                    var ft = new FormattedText((mr + 1).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11.5, filtered ? Accent : HeadText);
                    dc.DrawText(ft, new Point(_g.NumW - ft.Width - 6, y + (rh - ft.Height) / 2));
                    if (_g.HasCheck && mr < m.RowCount) DrawCheck(dc, new Point(_g.NumW + 5, y + (rh - 15) / 2), _g.IsChecked(mr));
                    dc.DrawLine(HeadLine, new Point(0, y + rh - 0.5), new Point(rhw, y + rh - 0.5));
                }
            if (_g.ShowFilterRow)
            {   // ô góc của dòng tìm kiếm: phễu (xanh khi đang lọc — bấm để bỏ mọi bộ lọc)
                var gx = rhw / 2 - 5.5; var gy = hh + SheetGrid.FilterH / 2;
                var fun = new StreamGeometry();
                using (var g = fun.Open()) { g.BeginFigure(new Point(gx, gy - 5), true); g.LineTo(new Point(gx + 11, gy - 5)); g.LineTo(new Point(gx + 7, gy)); g.LineTo(new Point(gx + 7, gy + 5)); g.LineTo(new Point(gx + 4, gy + 4)); g.LineTo(new Point(gx + 4, gy)); g.EndFigure(true); }
                if (filtered) { dc.FillRectangle(FilterOnBg, new Rect(1, hh + 1, rhw - 2, SheetGrid.FilterH - 2)); dc.DrawGeometry(Accent, null, fun); }
                else dc.DrawGeometry(null, new Pen(Muted, 1), fun);
                dc.DrawLine(HeadLine, new Point(0, top - 0.5), new Point(rhw, top - 0.5));
            }
            if (_g.CanEnlarge)
            {   // ô góc: nút ⤢ mở bảng ra cửa sổ lớn (hai mũi tên chéo)
                var cx = _g.NumW / 2; var cy = hh / 2; var pen = new Pen(Muted, 1.4) { LineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(cx - 5, cy + 5), new Point(cx + 5, cy - 5));
                dc.DrawLine(pen, new Point(cx + 1, cy - 5), new Point(cx + 5, cy - 5)); dc.DrawLine(pen, new Point(cx + 5, cy - 5), new Point(cx + 5, cy - 1));
                dc.DrawLine(pen, new Point(cx - 5, cy + 1), new Point(cx - 5, cy + 5)); dc.DrawLine(pen, new Point(cx - 5, cy + 5), new Point(cx - 1, cy + 5));
            }
            if (_g.HasCheck)
            {   // ô tích tiêu đề = chọn / bỏ mọi dòng đang hiện
                dc.FillRectangle(HeadBg, new Rect(_g.NumW, 0, SheetGrid.CheckW, hh - 1));
                DrawCheck(dc, new Point(_g.NumW + 5, (hh - 15) / 2), _g.MasterState());
            }
            dc.DrawLine(HeadLine, new Point(rhw - 0.5, 0), new Point(rhw - 0.5, b.Height));
        }

        private static readonly IPen CheckPen = new Pen(AppTheme.Brush("#5D6B78"), 1.3);
        private static readonly IPen TickPen = new Pen(Brushes.White, 2) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        private static void DrawCheck(DrawingContext dc, Point at, bool? on)
        {
            var box = new Rect(at.X, at.Y, 15, 15);
            if (on == false) { dc.DrawRectangle(AppTheme.Card, CheckPen, box, 3, 3); return; }
            dc.DrawRectangle(Accent, null, box, 3, 3);
            if (on == true)
            {
                var g = new StreamGeometry();
                using (var c = g.Open()) { c.BeginFigure(new Point(at.X + 3.5, at.Y + 7.8), false); c.LineTo(new Point(at.X + 6.4, at.Y + 10.6)); c.LineTo(new Point(at.X + 11.6, at.Y + 4.6)); c.EndFigure(false); }
                dc.DrawGeometry(null, TickPen, g);
            }
            else dc.DrawLine(TickPen, new Point(at.X + 4, at.Y + 7.5), new Point(at.X + 11, at.Y + 7.5));
        }

        private static bool LooksNumber(string s) =>
            s.Length is > 0 and < 20 && (char.IsDigit(s[0]) || s[0] is '-' or '+') && double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            && !(s.Length > 1 && s[0] == '0' && char.IsDigit(s[1])); // mã như 0123 giữ canh trái

        private int ResizeHit(Point p)
        {
            if (p.Y > SheetGrid.HeadH) return -1;   // chỉ ở dòng tiêu đề (không ở dòng tìm kiếm)
            double x = _g.RowHeadW - _g.OffsetX;
            for (var c = 0; c < _g.Model.ColCount; c++)
            {
                x += _g.Model.Columns[c].Width;
                if (Math.Abs(p.X - x) <= 4) return c;
            }
            return -1;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            var p = e.GetPosition(this);
            var props = e.GetCurrentPoint(this).Properties;
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (props.IsRightButtonPressed)
            {
                var (rr, cc) = _g.HitCell(p);
                if (rr >= 0 && cc >= 0 && !_g.VSel.Contains(rr, cc)) _g.SelectV(rr, cc);
                return;
            }
            if (!props.IsLeftButtonPressed) return;
            var rc = ResizeHit(p);
            if (rc >= 0 && e.ClickCount == 2) { _drag = Drag.None; e.Pointer.Capture(null); _g.AutoFitColumn(rc); e.Handled = true; return; }   // 1.94: bấm đúp viền cột = vừa dữ liệu
            if (rc >= 0) { _drag = Drag.Resize; _resizeCol = rc; _resizeStartX = p.X; _resizeStartW = _g.Model.Columns[rc].Width; e.Pointer.Capture(this); return; }
            var (r, c) = _g.HitCell(p);
            if (_g.ShowFilterRow && p.Y >= SheetGrid.HeadH && p.Y < _g.TopH)
            {   // dòng tìm kiếm: ô góc = bỏ mọi bộ lọc; ô cột = gõ chữ tìm
                if (c < 0) _g.ClearFilters(); else _g.ShowFilterEditor(c);
                e.Handled = true; return;
            }
            if (_g.HasCheck && c < 0 && p.X >= _g.NumW)
            {
                if (r < 0 && p.Y < SheetGrid.HeadH) { var all = _g.ShownRows; _g.SetChecked(all, _g.MasterState() != true); e.Handled = true; return; }
                if (r >= 0) { var m = _g.ToModel(r); if (m < _g.Model.RowCount) { _g.SelectV(r, _g.Col); _g.ToggleRows(new[] { m }); } e.Handled = true; return; }
            }
            if (r < 0 && c < 0 && _g.CanEnlarge && p.Y < SheetGrid.HeadH && p.X < _g.NumW) { e.Handled = true; _ = _g.OpenLarge(); return; }
            if (r < 0 && c < 0) { _g.SelectRangeV(new CellRange(0, 0, Math.Max(0, _g.VCount - 1), _g.Model.ColCount - 1)); return; }
            if (r < 0) // tiêu đề cột
            {
                if (_g.Filterable && c >= 0 && _g.Model.Columns[c].Width >= 46 && p.X >= _g.ColX(c) + _g.Model.Columns[c].Width - DropW)
                { _g.OpenFilterMenu(c); e.Handled = true; return; }
                _drag = Drag.Cols;
                _headDown = shift ? -1 : c; _headMoved = false;
                if (!shift) { _g.AnchorCol = c; }
                _g.VA = 0; _g.Col = c; _g.VR = Math.Max(0, _g.VCount - 1);
                _g.Redraw(); e.Pointer.Capture(this); return;
            }
            if (c < 0) // số dòng
            {
                _drag = Drag.Rows;
                if (!shift) _g.VA = r;
                _g.AnchorCol = 0; _g.VR = r; _g.Col = _g.Model.ColCount - 1;
                _g.Redraw(); e.Pointer.Capture(this); return;
            }
            if (e.ClickCount == 2) { _g.SelectV(r, c); _g.BeginEdit(null, force: true); return; }   // 1.116: nhấn đúp mở cả ô khóa tạm
            _g.SelectV(r, c, shift);
            _drag = Drag.Cells;
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            if (_drag == Drag.None)
            {
                var onBig = _g.CanEnlarge && p.Y < SheetGrid.HeadH && p.X < _g.NumW;
                Cursor = ResizeHit(p) >= 0 ? new Cursor(StandardCursorType.SizeWestEast)
                    : onBig ? new Cursor(StandardCursorType.Hand)
                    : _g.ShowFilterRow && p.Y >= SheetGrid.HeadH && p.Y < _g.TopH ? new Cursor(StandardCursorType.Ibeam) : Cursor.Default;
                var tip = onBig ? "Mở bảng ra cửa sổ lớn (F11)" : null;
                if (tip is null && _g.CellTip is { } ct && p.Y >= _g.TopH && p.X >= _g.RowHeadW)
                {
                    var (vr, vc) = _g.HitCell(p);
                    if (vr >= 0 && vr < _g.TotalRows && vc >= 0) { var mr = _g.ToModel(vr); if (mr < _g.Model.RowCount) tip = ct(mr, vc); }
                }
                if (!Equals(ToolTip.GetTip(this), tip)) { ToolTip.SetTip(this, tip); ToolTip.SetIsOpen(this, tip is not null); }
                return;
            }
            if (_drag == Drag.Resize)
            {
                _g.Model.Columns[_resizeCol].Width = Math.Max(30, _resizeStartW + p.X - _resizeStartX);
                _g.UpdateScrollPublic(); _g.Redraw(); return;
            }
            // tự cuộn khi kéo ra mép
            if (p.Y > Bounds.Height - 4) _g.TopRow = Math.Min(_g.TopRow + 1, Math.Max(0, _g.TotalRows - _g.VisibleRows));
            if (p.Y < _g.TopH && _drag != Drag.Cols) _g.TopRow = Math.Max(0, _g.TopRow - 1);
            var (r, c) = _g.HitCell(new Point(Math.Max(p.X, _g.RowHeadW + 1), Math.Max(p.Y, _g.TopH + 1)));
            switch (_drag)
            {
                case Drag.Cells: _g.VR = Math.Max(0, r); _g.Col = Math.Max(0, c); break;
                case Drag.Cols: if (c != _headDown) _headMoved = true; _g.Col = Math.Max(0, c); break;
                case Drag.Rows: _g.VR = Math.Max(0, r); break;
            }
            _g.UpdateScrollPublic();
            _g.Redraw();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _headDown = -1;   // 1.94: bấm tiêu đề = bôi đen cả cột (không sắp xếp — sắp xếp ở nút ▾ / chuột phải)
            if (_drag is Drag.Cells && e.InitialPressMouseButton == MouseButton.Left && _g.RowClicked is { } rc)
            {   // 1.82: bấm chuột trái vào 1 dòng dữ liệu (dòng nhóm tháng ở danh sách phiếu xuất → mở / thu)
                var p = e.GetPosition(this); var (hr, _) = _g.HitCell(p);
                if (p.Y >= _g.TopH && hr >= 0 && hr < _g.VCount && hr == _g.VR) rc(_g.ToModel(hr));
            }
            if (_drag is Drag.Cells or Drag.Rows or Drag.Cols) _g.SelectionChanged?.Invoke();
            _drag = Drag.None;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            var (top0, off0) = (_g.TopRow, _g.OffsetX);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
                _g.OffsetX = Math.Max(0, _g.OffsetX - (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 60);
            else
                _g.TopRow = Math.Clamp(_g.TopRow - (int)Math.Round(e.Delta.Y * 3), 0, Math.Max(0, _g.TotalRows - _g.VisibleRows + 1));
            _g.UpdateScrollPublic();
            _g.Redraw();
            // 1.85: bảng đã cuộn thì giữ sự kiện; hết chỗ cuộn (đầu / cuối bảng) thì để trang (FillScroll) cuộn tiếp
            if (_g.TopRow != top0 || Math.Abs(_g.OffsetX - off0) > 0.5) e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (!e.Handled) _g.OnKey(e); }
        protected override void OnTextInput(TextInputEventArgs e) { base.OnTextInput(e); if (e.Text is { } t) { _g.OnText(t); e.Handled = true; } }
    }

    internal void UpdateScrollPublic() => UpdateScroll();
}

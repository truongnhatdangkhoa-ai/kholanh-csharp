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
    public event Action? SelectionChanged;
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
    internal bool ShowFilterRow => _filterable && (_surface.Bounds.Height <= 0 || _surface.Bounds.Height >= HeadH + FilterH + RowH * 6
        || _filters.Values.Any(f => f.Text.Trim().Length > 0));
    internal double NumW => Math.Max(44, (Math.Max(Model.RowCount + SpareRows, 1).ToString().Length) * 8 + 18);
    internal double RowHeadW => NumW + (HasCheck ? CheckW : 0);

    // ------------------------------------------------------------------ ô chọn đầu dòng (1.50 — chọn hàng xuất thay vì gõ số)
    internal const double CheckW = 26;
    /// <summary>Cột số lượng gắn với ô chọn ☐ đầu dòng: tích = điền <see cref="CheckFill"/> (thường là tồn còn xuất được), bỏ tích = xóa;
    /// ô có số &gt; 0 thì hiện đã tích. Tích ô tiêu đề = chọn / bỏ mọi dòng ĐANG HIỆN (sau lọc). Phím cách = tích các dòng đang chọn. -1 = không có.</summary>
    public int CheckCol { get => _checkCol; set { _checkCol = value; UpdateScroll(); Redraw(); } }
    private int _checkCol = -1;
    /// <summary>Giá trị điền vào cột <see cref="CheckCol"/> khi tích dòng (dòng dữ liệu → chữ).</summary>
    public Func<int, string>? CheckFill { get; set; }
    internal bool HasCheck => _checkCol >= 0 && _checkCol < Model.ColCount && !Model.ReadOnly && !Model.Columns[_checkCol].ReadOnly;
    internal bool IsChecked(int m)
    {
        if (m < 0 || m >= Model.RowCount) return false;
        var v = Model.Get(m, _checkCol).Trim();
        return v.Length > 0 && v != "0" && !(GridFilter.TryNumber(v, out var n) && n <= 0);
    }
    /// <summary>Tích / bỏ tích các dòng dữ liệu (1 lần hoàn tác).</summary>
    public void SetChecked(IEnumerable<int> rows, bool on)
    {
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

    public bool IsFiltered => _map is not null;
    /// <summary>Các dòng dữ liệu đang hiện (khi không lọc: mọi dòng).</summary>
    public IReadOnlyList<int> ShownRows => _map ?? (IReadOnlyList<int>)Enumerable.Range(0, Model.RowCount).ToList();
    internal int VCount => _map?.Count ?? Model.RowCount;
    internal int ToModel(int v) => _map is null || v < 0 ? v : v < _map.Count ? _map[v] : Model.RowCount + (v - _map.Count);
    internal int ToVisual(int m)
    {
        if (_map is null) return m;
        if (m >= Model.RowCount) return _map.Count + (m - Model.RowCount);
        var i = _map.BinarySearch(m);
        return i >= 0 ? i : Math.Min(~i, Math.Max(0, _map.Count - 1));
    }
    private static string FKey(GridColumn col, int c) => string.IsNullOrEmpty(col.Key) ? "#" + c : col.Key;
    internal ColumnFilter? FilterOf(int c) => c >= 0 && c < Model.ColCount && _filters.TryGetValue(FKey(Model.Columns[c], c), out var f) && f.Active ? f : null;
    internal string CellText(int m, int c) { var raw = Model.Get(m, c); return Display?.Invoke(m, c, raw) ?? raw; }

    private Dictionary<int, ColumnFilter> FilterByIndex()
    {
        var d = new Dictionary<int, ColumnFilter>();
        for (var c = 0; c < Model.ColCount; c++) if (_filters.TryGetValue(FKey(Model.Columns[c], c), out var f) && f.Active) d[c] = f;
        return d;
    }

    /// <summary>Lọc lại theo bộ lọc hiện có (gọi sau khi đổi bộ lọc hoặc nạp dữ liệu mới).</summary>
    public void ApplyFilter()
    {
        var keepRow = Row; var keepAnchor = AnchorRow;
        var act = FilterByIndex();
        _map = act.Count == 0 ? null : GridFilter.Rows(Model.RowCount, CellText, act);
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
        if (_filters.Count == 0 && _map is null) return;
        _filters.Clear(); HideFilterEditor(false);
        if (Model is not null) ApplyFilter();
    }

    private void OnModelChanged()
    {
        // Nạp dữ liệu mới / đổi số dòng → lọc lại (giữ bộ lọc của cột còn tồn tại); sửa ô thì giữ nguyên các dòng đang hiện như Excel.
        if (Model.Loads != _seenLoads || Model.RowCount != _seenRows)
        {
            if (_filters.Count > 0)
            {
                var keys = Model.Columns.Select(FKey).ToHashSet();
                foreach (var k in _filters.Keys.Where(k => !keys.Contains(k)).ToList()) _filters.Remove(k);
            }
            _seenLoads = Model.Loads; _seenRows = Model.RowCount;
            if (_filters.Count > 0 || _map is not null) { ApplyFilter(); return; }
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
            () => { fl.Hide(); SetFilter(c, "", null, true); }, () => fl.Hide());
        fl.ShowAt(_surface);
    }

    public SheetGrid() : this(new GridModel()) { }

    public SheetGrid(GridModel model)
    {
        Model = model;
        _surface = new GridSurface(this) { Focusable = true, ClipToBounds = true };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("*,Auto") };
        var host = new Panel { ClipToBounds = true };
        host.Children.Add(_surface);
        host.Children.Add(_overlay);
        _overlay.Children.Add(_editor);
        _overlay.Children.Add(_fEditor);
        g.Children.Add(host);
        Grid.SetColumn(_vbar, 1); g.Children.Add(_vbar);
        Grid.SetRow(_hbar, 1); g.Children.Add(_hbar);
        Content = new Border { BorderBrush = AppTheme.Brush("#B9C3D0"), BorderThickness = new Thickness(1), Background = AppTheme.Card, Child = g };
        _vbar.Scroll += (_, _) => { TopRow = (int)Math.Round(_vbar.Value); Redraw(); };
        _hbar.Scroll += (_, _) => { OffsetX = _hbar.Value; Redraw(); };
        _vbar.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { TopRow = (int)Math.Round(_vbar.Value); Redraw(); } };
        _hbar.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { OffsetX = _hbar.Value; Redraw(); } };
        _surface.SizeChanged += (_, _) => UpdateScroll();
        Model.Changed += OnModelChanged;
        _seenLoads = Model.Loads; _seenRows = Model.RowCount;
        _editor.KeyDown += OnEditorKey;
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

    internal void Redraw() { _surface.InvalidateVisual(); if (_editing) PlaceEditor(); if (_fEditor.IsVisible) PlaceFilterEditor(); }

    /// <summary>Gọi sau khi đổi cột / nạp dữ liệu.</summary>
    public void Refresh()
    {
        VR = Math.Clamp(VR, 0, Math.Max(0, TotalRows - 1)); Col = Math.Clamp(Col, 0, Math.Max(0, Model.ColCount - 1));
        VA = VR; AnchorCol = Col;
        UpdateScroll(); Redraw();
    }

    internal int TotalRows => VCount + (Model.ReadOnly || _map is not null ? 0 : SpareRows);
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
        c = Math.Clamp(c, 0, Math.Max(0, Model.ColCount - 1));
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
            case Key.Space when HasCheck && !ctrl: ToggleRows(SelModelRows().Where(m => m < Model.RowCount).ToList()); break;
            case Key.Delete: ClearSelection(); break;
            case Key.Back: BeginEdit(""); break;
            case Key.A when ctrl: VA = 0; AnchorCol = 0; VR = Math.Max(0, VCount - 1); Col = Model.ColCount - 1; Redraw(); SelectionChanged?.Invoke(); break;
            case Key.C when ctrl: await CopyAsync(); break;
            case Key.X when ctrl: await CopyAsync(); ClearSelection(); break;
            case Key.V when ctrl: await PasteAsync(); break;
            case Key.Z when ctrl: Model.Undo(); break;
            case Key.Y when ctrl: Model.Redo(); break;
            case Key.D when ctrl: FillDownSelection(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Move((int r, int c) to, bool extend) => SelectV(to.r, to.c, extend);

    /// <summary>Các dòng dữ liệu (đang hiện) trong vùng chọn.</summary>
    private IEnumerable<int> SelModelRows() { var s = VSel; for (var v = s.R1; v <= s.R2; v++) yield return ToModel(v); }

    private void ClearSelection()
    {
        if (_map is null) { Model.Clear(Selection); return; }
        var s = VSel;
        Model.SetCells(SelModelRows().Where(m => m < Model.RowCount).SelectMany(m => Enumerable.Range(s.C1, s.Cols).Select(c => (m, c, ""))));
    }

    private void FillDownSelection()
    {
        if (_map is null) { Model.FillDown(Selection); return; }
        var s = VSel; if (s.Rows < 2) return;
        var src = ToModel(s.R1);
        Model.SetCells(SelModelRows().Skip(1).SelectMany(m => Enumerable.Range(s.C1, s.Cols).Select(c => (m, c, Model.Get(src, c)))));
    }

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
        if (text == " " && HasCheck) return;   // phím cách = tích dòng
        BeginEdit(text);
    }

    // ------------------------------------------------------------------ sửa ô

    public void BeginEdit(string? initial)
    {
        if (Model.ReadOnly || Col >= Model.ColCount || Model.Columns[Col].ReadOnly) return;
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
        _editing = false;
        _editor.IsVisible = false;
        var v = _editor.Text ?? "";
        if (v != Model.Get(Row, Col)) Model.Set(Row, Col, v);
        _surface.Focus();
        if (dr != 0 || dc != 0) SelectV(VR + dr, Col + dc); else Redraw();
    }

    private void Cancel()
    {
        _editing = false; _editor.IsVisible = false; _surface.Focus(); Redraw();
    }

    private void OnEditorKey(object? sender, KeyEventArgs e)
    {
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
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
                Item("Hoàn tác", () => Model.Undo(), "Ctrl+Z"),
                Item("Làm lại", () => Model.Redo(), "Ctrl+Y"),
            },
        };
        var fMenu = Item("Lọc cột này…", () => OpenFilterMenu(Col), "Alt+Down");
        var fClear = Item("Bỏ mọi bộ lọc", ClearFilters);
        menu.Items.Insert(0, fMenu); menu.Items.Insert(1, fClear); menu.Items.Insert(2, new Separator());
        menu.Opening += (_, _) =>
        {
            fMenu.IsVisible = fClear.IsVisible = _filterable;
            fClear.IsEnabled = _map is not null;
            fClear.Header = _map is null ? "Bỏ mọi bộ lọc" : $"Bỏ mọi bộ lọc (đang hiện {_map.Count:N0} / {Model.RowCount:N0} dòng)";
            ((Separator)menu.Items[2]!).IsVisible = _filterable;
        };
        _surface.ContextMenu = menu;
    }

    // ------------------------------------------------------------------ bề mặt vẽ

    private sealed class GridSurface : Control
    {
        private readonly SheetGrid _g;
        private static readonly IBrush HeadBg = AppTheme.Brush("#EDF1F6");
        private static readonly IBrush HeadSelBg = AppTheme.Brush("#E6F0FC");
        private static readonly IBrush SelBg = AppTheme.Brush("#E6F0FC");
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
                        var shown = _g.Display?.Invoke(mr, c, raw) ?? raw;
                        if (shown.Length == 0) continue;
                        var nl = shown.IndexOfAny(new[] { '\r', '\n' });
                        if (nl >= 0) shown = shown[..nl] + " …";
                        var ft = new FormattedText(shown, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 13, Text);
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
                    var ft = new FormattedText(m.Columns[c].Header, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HeadFace, 13, f is not null ? Accent : HeadText);
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
            if (r < 0 && c < 0) { _g.SelectRangeV(new CellRange(0, 0, Math.Max(0, _g.VCount - 1), _g.Model.ColCount - 1)); return; }
            if (r < 0) // tiêu đề cột
            {
                if (_g.Filterable && c >= 0 && _g.Model.Columns[c].Width >= 46 && p.X >= _g.ColX(c) + _g.Model.Columns[c].Width - DropW)
                { _g.OpenFilterMenu(c); e.Handled = true; return; }
                _drag = Drag.Cols;
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
            if (e.ClickCount == 2) { _g.SelectV(r, c); _g.BeginEdit(null); return; }
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
                Cursor = ResizeHit(p) >= 0 ? new Cursor(StandardCursorType.SizeWestEast)
                    : _g.ShowFilterRow && p.Y >= SheetGrid.HeadH && p.Y < _g.TopH ? new Cursor(StandardCursorType.Ibeam) : Cursor.Default;
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
                case Drag.Cols: _g.Col = Math.Max(0, c); break;
                case Drag.Rows: _g.VR = Math.Max(0, r); break;
            }
            _g.UpdateScrollPublic();
            _g.Redraw();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_drag is Drag.Cells or Drag.Rows or Drag.Cols) _g.SelectionChanged?.Invoke();
            _drag = Drag.None;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
                _g.OffsetX = Math.Max(0, _g.OffsetX - (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 60);
            else
                _g.TopRow = Math.Clamp(_g.TopRow - (int)Math.Round(e.Delta.Y * 3), 0, Math.Max(0, _g.TotalRows - _g.VisibleRows + 1));
            _g.UpdateScrollPublic();
            _g.Redraw();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (!e.Handled) _g.OnKey(e); }
        protected override void OnTextInput(TextInputEventArgs e) { base.OnTextInput(e); if (e.Text is { } t) { _g.OnText(t); e.Handled = true; } }
    }

    internal void UpdateScrollPublic() => UpdateScroll();
}

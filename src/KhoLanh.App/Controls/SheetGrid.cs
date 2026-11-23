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
        IsVisible = false, BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(Color.Parse("#1F6FD1")),
        Padding = new Thickness(4, 2), MinHeight = 0, CornerRadius = new CornerRadius(0), AcceptsReturn = false, FontSize = 13,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly Canvas _overlay = new();

    public GridModel Model { get; }
    public int Row { get; private set; }
    public int Col { get; private set; }
    public int AnchorRow { get; private set; }
    public int AnchorCol { get; private set; }
    public CellRange Selection => CellRange.Of(AnchorRow, AnchorCol, Row, Col);
    /// <summary>Số dòng trống thêm ở cuối để gõ / dán tiếp.</summary>
    public int SpareRows { get; set; } = 20;
    /// <summary>Định dạng hiển thị (giá trị lưu vẫn là chuỗi gốc).</summary>
    public Func<int, int, string, string>? Display { get; set; }
    /// <summary>Màu nền riêng từng ô (null = mặc định).</summary>
    public Func<int, int, IBrush?>? CellBackground { get; set; }
    public event Action? SelectionChanged;

    internal const double RowH = 24, HeadH = 26;
    internal double RowHeadW => Math.Max(44, (Math.Max(Model.RowCount + SpareRows, 1).ToString().Length) * 8 + 18);
    internal int TopRow;          // dòng đầu đang thấy
    internal double OffsetX;      // cuộn ngang (px)
    private bool _editing, _typedEdit;

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
        g.Children.Add(host);
        Grid.SetColumn(_vbar, 1); g.Children.Add(_vbar);
        Grid.SetRow(_hbar, 1); g.Children.Add(_hbar);
        Content = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#D5DBE3")), BorderThickness = new Thickness(1), Background = Brushes.White, Child = g };
        _vbar.Scroll += (_, _) => { TopRow = (int)Math.Round(_vbar.Value); Redraw(); };
        _hbar.Scroll += (_, _) => { OffsetX = _hbar.Value; Redraw(); };
        _vbar.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { TopRow = (int)Math.Round(_vbar.Value); Redraw(); } };
        _hbar.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { OffsetX = _hbar.Value; Redraw(); } };
        _surface.SizeChanged += (_, _) => UpdateScroll();
        Model.Changed += () => { UpdateScroll(); Redraw(); };
        _editor.KeyDown += OnEditorKey;
        _editor.LostFocus += (_, _) => { if (_editing) Commit(0, 0); };
        BuildContextMenu();
    }

    public new void Focus() => _surface.Focus();

    internal void Redraw() { _surface.InvalidateVisual(); if (_editing) PlaceEditor(); }

    /// <summary>Gọi sau khi đổi cột / nạp dữ liệu.</summary>
    public void Refresh()
    {
        Row = Math.Clamp(Row, 0, Math.Max(0, TotalRows - 1)); Col = Math.Clamp(Col, 0, Math.Max(0, Model.ColCount - 1));
        AnchorRow = Row; AnchorCol = Col;
        UpdateScroll(); Redraw();
    }

    internal int TotalRows => Model.RowCount + (Model.ReadOnly ? 0 : SpareRows);
    internal double TotalWidth => Model.Columns.Sum(c => c.Width);
    internal int VisibleRows => Math.Max(1, (int)((_surface.Bounds.Height - HeadH) / RowH));

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
        var r = p.Y < HeadH ? -1 : TopRow + (int)((p.Y - HeadH) / RowH);
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

    public void Select(int r, int c, bool extend = false)
    {
        r = Math.Clamp(r, 0, Math.Max(0, TotalRows - 1));
        c = Math.Clamp(c, 0, Math.Max(0, Model.ColCount - 1));
        Row = r; Col = c;
        if (!extend) { AnchorRow = r; AnchorCol = c; }
        ScrollIntoView(r, c);
        Redraw();
        SelectionChanged?.Invoke();
    }

    public void SelectRange(CellRange range)
    {
        AnchorRow = range.R1; AnchorCol = range.C1; Row = range.R2; Col = range.C2;
        Redraw(); SelectionChanged?.Invoke();
    }

    private void ScrollIntoView(int r, int c)
    {
        var vis = VisibleRows;
        if (_surface.Bounds.Height < HeadH + RowH) vis = int.MaxValue / 2; // chưa có kích thước
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
            case Key.Up: Move(ctrl ? Edge(-1, 0) : (Row - 1, Col), shift); break;
            case Key.Down: Move(ctrl ? Edge(1, 0) : (Row + 1, Col), shift); break;
            case Key.Left: Move(ctrl ? Edge(0, -1) : (Row, Col - 1), shift); break;
            case Key.Right: Move(ctrl ? Edge(0, 1) : (Row, Col + 1), shift); break;
            case Key.PageDown: Move((Row + VisibleRows - 1, Col), shift); break;
            case Key.PageUp: Move((Row - VisibleRows + 1, Col), shift); break;
            case Key.Home: Move(ctrl ? (0, 0) : (Row, 0), shift); break;
            case Key.End: Move(ctrl ? (Math.Max(0, Model.UsedRows() - 1), Model.ColCount - 1) : (Row, Model.ColCount - 1), shift); break;
            case Key.Tab: Move((Row, Col + (shift ? -1 : 1)), false); break;
            case Key.Enter: Move((Row + (shift ? -1 : 1), Col), false); break;
            case Key.F2: BeginEdit(null); break;
            case Key.Delete: Model.Clear(Selection); break;
            case Key.Back: BeginEdit(""); break;
            case Key.A when ctrl: AnchorRow = 0; AnchorCol = 0; Row = Math.Max(0, Model.RowCount - 1); Col = Model.ColCount - 1; Redraw(); SelectionChanged?.Invoke(); break;
            case Key.C when ctrl: await CopyAsync(); break;
            case Key.X when ctrl: await CopyAsync(); Model.Clear(Selection); break;
            case Key.V when ctrl: await PasteAsync(); break;
            case Key.Z when ctrl: Model.Undo(); break;
            case Key.Y when ctrl: Model.Redo(); break;
            case Key.D when ctrl: Model.FillDown(Selection); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Move((int r, int c) to, bool extend) => Select(to.r, to.c, extend);

    /// <summary>Ctrl+mũi tên: nhảy tới mép khối dữ liệu như Excel.</summary>
    private (int, int) Edge(int dr, int dc)
    {
        int r = Row, c = Col;
        bool Filled(int rr, int cc) => !string.IsNullOrEmpty(Model.Get(rr, cc));
        int maxR = Math.Max(0, Model.RowCount - 1), maxC = Model.ColCount - 1;
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
        BeginEdit(text);
    }

    // ------------------------------------------------------------------ sửa ô

    public void BeginEdit(string? initial)
    {
        if (Model.ReadOnly || Col >= Model.ColCount || Model.Columns[Col].ReadOnly) return;
        AnchorRow = Row; AnchorCol = Col;
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
        var x = ColX(Col); var y = HeadH + (Row - TopRow) * RowH;
        var w = Col < Model.ColCount ? Model.Columns[Col].Width : 100;
        Canvas.SetLeft(_editor, x - 1); Canvas.SetTop(_editor, y - 1);
        _editor.Width = Math.Max(w + 2, 60); _editor.Height = RowH + 2;
        _editor.IsVisible = _editing && Row >= TopRow && Row < TopRow + VisibleRows + 1;
    }

    private void Commit(int dr, int dc)
    {
        if (!_editing) return;
        _editing = false;
        _editor.IsVisible = false;
        var v = _editor.Text ?? "";
        if (v != Model.Get(Row, Col)) Model.Set(Row, Col, v);
        _surface.Focus();
        if (dr != 0 || dc != 0) Select(Row + dr, Col + dc); else Redraw();
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
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(Model.CopyText(Selection));
    }

    public async Task CopyAllAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb || Model.ColCount == 0) return;
        var head = string.Join("\t", Model.Columns.Select(c => c.Header)) + "\r\n";
        await cb.SetTextAsync(head + Model.CopyText(new CellRange(0, 0, Math.Max(0, Model.UsedRows() - 1), Model.ColCount - 1)));
    }

    public async Task PasteAsync()
    {
        if (Model.ReadOnly) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } cb) return;
        var text = await cb.GetTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        var r = Model.Paste(Selection, text);
        SelectRange(r);
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
                Item("Cắt", async () => { await CopyAsync(); Model.Clear(Selection); }, "Ctrl+X"),
                Item("Chép", async () => await CopyAsync(), "Ctrl+C"),
                Item("Dán", async () => await PasteAsync(), "Ctrl+V"),
                new Separator(),
                Item("Chèn dòng phía trên", () => Model.InsertRows(Selection.R1, Selection.Rows)),
                Item("Chèn dòng phía dưới", () => Model.InsertRows(Selection.R2 + 1, Selection.Rows)),
                Item("Xóa dòng đang chọn", () => { Model.DeleteRows(Selection.R1, Selection.R2); Select(Selection.R1, Col); }),
                Item("Xóa nội dung", () => Model.Clear(Selection), "Delete"),
                new Separator(),
                Item("Điền xuống", () => Model.FillDown(Selection), "Ctrl+D"),
                Item("Hoàn tác", () => Model.Undo(), "Ctrl+Z"),
                Item("Làm lại", () => Model.Redo(), "Ctrl+Y"),
            },
        };
        _surface.ContextMenu = menu;
    }

    // ------------------------------------------------------------------ bề mặt vẽ

    private sealed class GridSurface : Control
    {
        private readonly SheetGrid _g;
        private static readonly IBrush HeadBg = new SolidColorBrush(Color.Parse("#F1F4F8"));
        private static readonly IBrush HeadSelBg = new SolidColorBrush(Color.Parse("#D9E6F7"));
        private static readonly IBrush SelBg = new SolidColorBrush(Color.Parse("#E3EEFB"));
        private static readonly IPen Line = new Pen(new SolidColorBrush(Color.Parse("#E3E7ED")), 1);
        private static readonly IPen HeadLine = new Pen(new SolidColorBrush(Color.Parse("#CBD3DD")), 1);
        private static readonly IPen SelPen = new Pen(new SolidColorBrush(Color.Parse("#1F6FD1")), 2);
        private static readonly IBrush Text = new SolidColorBrush(Color.Parse("#1F2937"));
        private static readonly IBrush HeadText = new SolidColorBrush(Color.Parse("#475569"));
        private static readonly Typeface Face = new(FontFamily.Default);
        private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        private enum Drag { None, Cells, Rows, Cols, Resize }
        private Drag _drag;
        private int _resizeCol; private double _resizeStartX, _resizeStartW;

        public GridSurface(SheetGrid g) { _g = g; Cursor = Cursor.Default; }

        public override void Render(DrawingContext dc)
        {
            var m = _g.Model;
            var b = Bounds;
            dc.FillRectangle(Brushes.White, new Rect(b.Size));
            var rhw = _g.RowHeadW;
            var sel = _g.Selection;
            var vis = _g.VisibleRows + 1;
            var last = Math.Min(_g.TotalRows - 1, _g.TopRow + vis);
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
            using (dc.PushClip(new Rect(rhw, SheetGrid.HeadH, Math.Max(0, b.Width - rhw), Math.Max(0, b.Height - SheetGrid.HeadH))))
            {
                for (var r = _g.TopRow; r <= last; r++)
                {
                    var y = SheetGrid.HeadH + (r - _g.TopRow) * SheetGrid.RowH;
                    foreach (var (c, cx, cw) in cols)
                    {
                        var rect = new Rect(cx, y, cw, SheetGrid.RowH);
                        var bg = _g.CellBackground?.Invoke(r, c);
                        if (sel.Contains(r, c) && !sel.Single && !(r == _g.Row && c == _g.Col)) dc.FillRectangle(SelBg, rect);
                        else if (bg is not null) dc.FillRectangle(bg, rect);
                        var raw = m.Get(r, c);
                        if (raw.Length == 0 && _g.Display is null) continue;
                        var shown = _g.Display?.Invoke(r, c, raw) ?? raw;
                        if (shown.Length == 0) continue;
                        var nl = shown.IndexOfAny(new[] { '\r', '\n' });
                        if (nl >= 0) shown = shown[..nl] + " …";
                        var ft = new FormattedText(shown, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 13, Text);
                        var num = m.Columns[c].Type == "number" || (m.Columns[c].Type == "text" && LooksNumber(raw));
                        var tx = num ? cx + cw - ft.Width - 6 : cx + 6;
                        using (dc.PushClip(rect.Deflate(new Thickness(2, 0))))
                            dc.DrawText(ft, new Point(Math.Max(cx + 4, tx), y + (SheetGrid.RowH - ft.Height) / 2));
                    }
                }
                // đường kẻ
                for (var r = _g.TopRow; r <= last + 1; r++)
                {
                    var y = SheetGrid.HeadH + (r - _g.TopRow) * SheetGrid.RowH + 0.5;
                    dc.DrawLine(Line, new Point(rhw, y), new Point(right, y));
                }
                foreach (var (_, cx, cw) in cols) dc.DrawLine(Line, new Point(cx + cw - 0.5, SheetGrid.HeadH), new Point(cx + cw - 0.5, b.Height));
                // khung vùng chọn
                if (m.ColCount > 0)
                {
                    var x1 = _g.ColX(sel.C1); var x2 = _g.ColX(sel.C2) + m.Columns[Math.Min(sel.C2, m.ColCount - 1)].Width;
                    var y1 = SheetGrid.HeadH + (sel.R1 - _g.TopRow) * SheetGrid.RowH; var y2 = SheetGrid.HeadH + (sel.R2 - _g.TopRow + 1) * SheetGrid.RowH;
                    dc.DrawRectangle(null, SelPen, new Rect(x1, y1, x2 - x1, y2 - y1).Deflate(1));
                    dc.FillRectangle(SelPen.Brush!, new Rect(x2 - 4, y2 - 4, 6, 6));
                }
            }
            // tiêu đề cột
            dc.FillRectangle(HeadBg, new Rect(0, 0, b.Width, SheetGrid.HeadH));
            using (dc.PushClip(new Rect(rhw, 0, Math.Max(0, b.Width - rhw), SheetGrid.HeadH)))
                foreach (var (c, cx, cw) in cols)
                {
                    if (c >= sel.C1 && c <= sel.C2) dc.FillRectangle(HeadSelBg, new Rect(cx, 0, cw, SheetGrid.HeadH));
                    var ft = new FormattedText(m.Columns[c].Header, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Bold, 12.5, HeadText);
                    using (dc.PushClip(new Rect(cx + 2, 0, Math.Max(0, cw - 4), SheetGrid.HeadH)))
                        dc.DrawText(ft, new Point(cx + Math.Max(4, (cw - ft.Width) / 2), (SheetGrid.HeadH - ft.Height) / 2));
                    dc.DrawLine(HeadLine, new Point(cx + cw - 0.5, 0), new Point(cx + cw - 0.5, SheetGrid.HeadH));
                }
            dc.DrawLine(HeadLine, new Point(0, SheetGrid.HeadH - 0.5), new Point(b.Width, SheetGrid.HeadH - 0.5));
            // số dòng
            dc.FillRectangle(HeadBg, new Rect(0, SheetGrid.HeadH, rhw, b.Height));
            using (dc.PushClip(new Rect(0, SheetGrid.HeadH, rhw, Math.Max(0, b.Height - SheetGrid.HeadH))))
                for (var r = _g.TopRow; r <= last; r++)
                {
                    var y = SheetGrid.HeadH + (r - _g.TopRow) * SheetGrid.RowH;
                    if (r >= sel.R1 && r <= sel.R2) dc.FillRectangle(HeadSelBg, new Rect(0, y, rhw, SheetGrid.RowH));
                    var ft = new FormattedText((r + 1).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11.5, HeadText);
                    dc.DrawText(ft, new Point(rhw - ft.Width - 6, y + (SheetGrid.RowH - ft.Height) / 2));
                    dc.DrawLine(HeadLine, new Point(0, y + SheetGrid.RowH - 0.5), new Point(rhw, y + SheetGrid.RowH - 0.5));
                }
            dc.DrawLine(HeadLine, new Point(rhw - 0.5, 0), new Point(rhw - 0.5, b.Height));
        }

        private static bool LooksNumber(string s) =>
            s.Length is > 0 and < 20 && (char.IsDigit(s[0]) || s[0] is '-' or '+') && double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            && !(s.Length > 1 && s[0] == '0' && char.IsDigit(s[1])); // mã như 0123 giữ canh trái

        private int ResizeHit(Point p)
        {
            if (p.Y > SheetGrid.HeadH) return -1;
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
                if (rr >= 0 && cc >= 0 && !_g.Selection.Contains(rr, cc)) _g.Select(rr, cc);
                return;
            }
            if (!props.IsLeftButtonPressed) return;
            var rc = ResizeHit(p);
            if (rc >= 0) { _drag = Drag.Resize; _resizeCol = rc; _resizeStartX = p.X; _resizeStartW = _g.Model.Columns[rc].Width; e.Pointer.Capture(this); return; }
            var (r, c) = _g.HitCell(p);
            if (r < 0 && c < 0) { _g.SelectRange(new CellRange(0, 0, Math.Max(0, _g.Model.RowCount - 1), _g.Model.ColCount - 1)); return; }
            if (r < 0) // tiêu đề cột
            {
                _drag = Drag.Cols;
                if (!shift) { _g.AnchorCol = c; }
                _g.AnchorRow = 0; _g.Col = c; _g.Row = Math.Max(0, _g.Model.RowCount - 1);
                _g.Redraw(); e.Pointer.Capture(this); return;
            }
            if (c < 0) // số dòng
            {
                _drag = Drag.Rows;
                if (!shift) _g.AnchorRow = r;
                _g.AnchorCol = 0; _g.Row = r; _g.Col = _g.Model.ColCount - 1;
                _g.Redraw(); e.Pointer.Capture(this); return;
            }
            if (e.ClickCount == 2) { _g.Select(r, c); _g.BeginEdit(null); return; }
            _g.Select(r, c, shift);
            _drag = Drag.Cells;
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            if (_drag == Drag.None) { Cursor = ResizeHit(p) >= 0 ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default; return; }
            if (_drag == Drag.Resize)
            {
                _g.Model.Columns[_resizeCol].Width = Math.Max(30, _resizeStartW + p.X - _resizeStartX);
                _g.UpdateScrollPublic(); _g.Redraw(); return;
            }
            // tự cuộn khi kéo ra mép
            if (p.Y > Bounds.Height - 4) _g.TopRow = Math.Min(_g.TopRow + 1, Math.Max(0, _g.TotalRows - _g.VisibleRows));
            if (p.Y < SheetGrid.HeadH && _drag != Drag.Cols) _g.TopRow = Math.Max(0, _g.TopRow - 1);
            var (r, c) = _g.HitCell(new Point(Math.Max(p.X, _g.RowHeadW + 1), Math.Max(p.Y, SheetGrid.HeadH + 1)));
            switch (_drag)
            {
                case Drag.Cells: _g.Row = Math.Max(0, r); _g.Col = Math.Max(0, c); break;
                case Drag.Cols: _g.Col = Math.Max(0, c); break;
                case Drag.Rows: _g.Row = Math.Max(0, r); break;
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

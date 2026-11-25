using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;

namespace KhoLanh.App.Controls;

/// <summary>
/// Thanh công cụ chuẩn (1.84, "mẫu chuẩn module" phương án A): luôn 1 hàng.
/// <list type="bullet">
/// <item>Phần tử đặt <see cref="SetRight"/> (hoặc đứng sau <see cref="Gap"/>) dồn sang phải — nút chính để cuối.</item>
/// <item>Nút đặt <see cref="SetMore"/> luôn nằm trong menu "⋯ Thêm"; hết chỗ thì các nút thường (không phải nút chính) chuyển dần vào menu, từ cuối lên.</item>
/// <item>Chỉ ô nhập / ô chọn cũng không vừa (cửa sổ rất hẹp) → xuống dòng như WrapPanel, không cắt mất gì.</item>
/// <item>Nằm sát đầu nội dung của tab con (TabControl lồng) → dải tab con (kiểu nút gạt) được kéo vào đầu hàng này, không còn hàng tab riêng.</item>
/// </list>
/// Thay cho WrapPanel ở thanh công cụ: cùng cách dùng <c>Children.Add(...)</c>.
/// </summary>
public sealed class ToolRow : Panel
{
    public static readonly AttachedProperty<bool> MoreProperty = AvaloniaProperty.RegisterAttached<ToolRow, Control, bool>("More");
    public static readonly AttachedProperty<bool> RightProperty = AvaloniaProperty.RegisterAttached<ToolRow, Control, bool>("Right");
    public static readonly AttachedProperty<bool> PinProperty = AvaloniaProperty.RegisterAttached<ToolRow, Control, bool>("Pin");
    /// <summary>Luôn nằm trong menu "⋯ Thêm" (việc ít dùng).</summary>
    public static T More<T>(T c) where T : Control { c.SetValue(MoreProperty, true); return c; }
    public static void SetMore(Control c, bool v) => c.SetValue(MoreProperty, v);
    /// <summary>Dồn sang phải.</summary>
    public static T Right<T>(T c) where T : Control { c.SetValue(RightProperty, true); return c; }
    public static void SetRight(Control c, bool v) => c.SetValue(RightProperty, v);
    /// <summary>Không bao giờ chuyển vào menu.</summary>
    public static T Pin<T>(T c) where T : Control { c.SetValue(PinProperty, true); return c; }
    /// <summary>Vạch ngăn: mọi phần tử thêm sau nó dồn sang phải.</summary>
    public static Control Gap() => new GapMark();
    private sealed class GapMark : Control { }

    public double Spacing { get; set; } = 6;
    /// <summary>Khoảng trống bên trái dành cho dải tab con kéo vào hàng này.</summary>
    internal double Inset { get => _inset; set { if (Math.Abs(_inset - value) > 0.5) { _inset = value; InvalidateMeasure(); } } }
    private double _inset;
    private readonly Button _more;
    private readonly HashSet<Control> _hidden = new();
    private bool _wrap;
    private double _rowH;

    public ToolRow()
    {
        ClipToBounds = true;
        VerticalAlignment = VerticalAlignment.Top;
        _more = new Button { Content = "⋯ Thêm", Padding = new Thickness(10, 4), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(_more, "Các việc khác");
        _more.Click += (_, _) => OpenMore();
        VisualChildren.Add(_more);
        LogicalChildren.Add(_more);
        AttachedToVisualTree += (_, _) => { _retry = 0; QueueLift(); };
        DetachedFromVisualTree += (_, _) => Unlift();
    }

    private IEnumerable<Control> Items => Children.Where(c => c.IsVisible);

    protected override Size MeasureOverride(Size avail)
    {
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (var c in Children) c.Measure(inf);
        _more.Measure(inf);
        _hidden.Clear();
        var items = Items.ToList();
        foreach (var c in items) if (c.GetValue(MoreProperty) && IsButton(c)) _hidden.Add(c);
        double W(IEnumerable<Control> cs) { var l = cs.Where(c => !_hidden.Contains(c) && c is not GapMark).ToList(); return l.Sum(c => c.DesiredSize.Width) + Spacing * Math.Max(0, l.Count - 1); }
        var width = double.IsInfinity(avail.Width) ? double.MaxValue : avail.Width - _inset;
        double Need() => W(items) + (_hidden.Count > 0 ? _more.DesiredSize.Width + Spacing : 0) + (items.Any(c => c is GapMark) ? 16 : 0);
        // hết chỗ: chuyển nút thường vào menu, từ cuối lên (nút chính / nút ghim / ô nhập giữ lại)
        var cands = Enumerable.Reverse(items).Where(c => IsButton(c) && !c.GetValue(PinProperty) && !c.Classes.Contains("primary")).ToList();
        foreach (var c in cands) { if (Need() <= width) break; _hidden.Add(c); }
        _wrap = Need() > width + 0.5;
        if (_wrap) _hidden.RemoveWhere(c => !c.GetValue(MoreProperty));
        _rowH = Math.Max(items.Where(c => !_hidden.Contains(c)).Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max(), _hidden.Count > 0 ? _more.DesiredSize.Height : 0);
        if (!_wrap) return new Size(Math.Min(Need() + _inset, double.IsInfinity(avail.Width) ? double.MaxValue : avail.Width), _rowH);
        // xuống dòng (như WrapPanel)
        double x = _inset, y = 0, lineH = 0, maxW = 0;
        foreach (var c in Visible().Append(_hidden.Count > 0 ? _more : null).OfType<Control>())
        {
            var s = c.DesiredSize;
            if (x > _inset && x + s.Width > avail.Width) { y += lineH + 4; x = 0; lineH = 0; }
            x += s.Width + Spacing; lineH = Math.Max(lineH, s.Height); maxW = Math.Max(maxW, x);
        }
        return new Size(Math.Min(maxW, avail.Width), y + lineH);
    }

    private IEnumerable<Control> Visible() => Items.Where(c => !_hidden.Contains(c) && c is not GapMark);

    protected override Size ArrangeOverride(Size final)
    {
        foreach (var c in _hidden) c.Arrange(new Rect(-10000, 0, c.DesiredSize.Width, c.DesiredSize.Height));
        foreach (var c in Children.Where(c => c is GapMark)) c.Arrange(new Rect());
        if (_hidden.Count == 0) _more.Arrange(new Rect(-10000, 0, 0, 0));
        if (_wrap)
        {
            double x = _inset, y = 0, lineH = 0;
            foreach (var c in Visible().Append(_hidden.Count > 0 ? _more : null).OfType<Control>())
            {
                var s = c.DesiredSize;
                if (x > _inset && x + s.Width > final.Width) { y += lineH + 4; x = 0; lineH = 0; }
                c.Arrange(new Rect(x, y, s.Width, s.Height));
                x += s.Width + Spacing; lineH = Math.Max(lineH, s.Height);
            }
            return final;
        }
        var all = Items.ToList();
        var gapAt = all.FindIndex(c => c is GapMark);
        bool IsRight(Control c) => c.GetValue(RightProperty) || (gapAt >= 0 && all.IndexOf(c) > gapAt);
        var left = Visible().Where(c => !IsRight(c)).ToList();
        var right = Visible().Where(IsRight).ToList();
        double lx = _inset;
        foreach (var c in left) { Place(c, lx, final.Height); lx += c.DesiredSize.Width + Spacing; }
        var rw = right.Sum(c => c.DesiredSize.Width + Spacing) + (_hidden.Count > 0 ? _more.DesiredSize.Width + Spacing : 0);
        var rx = Math.Max(lx, final.Width - rw + Spacing);
        // "⋯ Thêm" đứng ngay trước nút chính (nút chính luôn ở mép phải)
        var prim = right.Count > 0 && right[^1].Classes.Contains("primary") ? right[^1] : null;
        foreach (var c in right)
        {
            if (c == prim && _hidden.Count > 0) { Place(_more, rx, final.Height); rx += _more.DesiredSize.Width + Spacing; }
            Place(c, rx, final.Height); rx += c.DesiredSize.Width + Spacing;
        }
        if (prim is null && _hidden.Count > 0) Place(_more, rx, final.Height);
        Dispatcher().Post(Lift, Avalonia.Threading.DispatcherPriority.Background);
        return final;
    }

    private static Avalonia.Threading.Dispatcher Dispatcher() => Avalonia.Threading.Dispatcher.UIThread;

    private static void Place(Control c, double x, double h) =>
        c.Arrange(new Rect(x, Math.Max(0, (h - c.DesiredSize.Height) / 2), c.DesiredSize.Width, c.DesiredSize.Height));

    private static bool IsButton(Control c) => c is Button or ToggleButton or SplitButton;

    /// <summary>Chữ hiện trên nút (bỏ ký hiệu đầu) — dùng cho mục menu.</summary>
    internal static string LabelOf(Control c)
    {
        var content = c is ContentControl cc ? cc.Content : null;
        var t = Icon.TextOf(content);
        if (string.IsNullOrWhiteSpace(t) && content is Control cont)
            t = string.Join(" ", cont.GetLogicalDescendants().OfType<TextBlock>().Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(t)) t = ToolTip.GetTip(c) as string;
        return (t ?? "").Trim();
    }

    private void OpenMore()
    {
        var fly = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var c in Children.Where(c => _hidden.Contains(c)))
        {
            var label = LabelOf(c);
            var mi = new MenuItem { Header = label.Length > 0 ? label : "…", IsEnabled = c.IsEffectivelyEnabled };
            if (ToolTip.GetTip(c) is string tip && tip != label) ToolTip.SetTip(mi, tip);
            if (c is ToggleButton tb)
            {
                mi.ToggleType = MenuItemToggleType.CheckBox; mi.IsChecked = tb.IsChecked == true;
                mi.Click += (_, _) => { tb.IsChecked = !(tb.IsChecked == true); tb.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            }
            else if (c is Button b)
                mi.Click += (_, _) =>
                {
                    if (b.Flyout is { } f) { f.ShowAt(_more); return; }
                    b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (b.Command is { } cmd && cmd.CanExecute(b.CommandParameter)) cmd.Execute(b.CommandParameter);
                };
            fly.Items.Add(mi);
        }
        fly.ShowAt(_more);
    }

    // ------------------------------------------------------------------ kéo dải tab con vào hàng này

    private TabControl? _liftNext;
    private int _mode, _retry;
    private ContentPresenter? _liftHost;
    private Control? _liftStrip;
    private double _liftShift;
    private void QueueLift() => Dispatcher().Post(Lift, Avalonia.Threading.DispatcherPriority.Background);

    /// <summary>Dải tab con (kiểu nút gạt) nằm đầu thanh này, không còn hàng tab riêng. 2 trường hợp:
    /// (A) thanh là hàng trên cùng của nội dung 1 tab con → dời nội dung tab lên ngang dải tab;
    /// (B) ngay dưới thanh là 1 TabControl (tab con) → kéo TabControl lên, dải tab nằm đầu thanh.</summary>
    private void Lift()
    {
        if (!this.IsAttachedToVisualTree() || !IsEffectivelyVisible || _wrap) { Unlift(); return; }
        if (LiftA() || LiftB()) { _retry = 0; return; }
        Unlift();
        // dải tab con có thể chưa kịp xếp (mới gắn vào cây) → thử lại vài lần
        if (_retry++ < 6) _ = Task.Delay(120).ContinueWith(_ => Dispatcher().Post(() => { if (_mode == 0) Lift(); }));
    }

    private static bool Liftable(TabControl tc) =>
        !tc.Classes.Contains("pagetabs") && !tc.Classes.Contains("nolift") && tc.IsEffectivelyVisible && tc.ItemCount > 1
        && (tc.Classes.Contains("seg") || tc.GetVisualAncestors().OfType<TabControl>().Any());

    private static ItemsPresenter? StripOf(TabControl tc) =>
        tc.GetVisualDescendants().OfType<ItemsPresenter>().FirstOrDefault(p => p.Name == "PART_ItemsPresenter" && p.TemplatedParent == tc);
    /// <summary>Bề rộng thật của dải tab (ItemsPresenter giãn hết chiều ngang, phải lấy mép phải tab cuối).</summary>
    private static double StripW(ItemsPresenter strip)
    {
        double w = 0;
        foreach (var it in strip.GetVisualDescendants().OfType<TabItem>().Where(t => t.IsVisible))
            if (it.TranslatePoint(new Point(it.Bounds.Width, 0), strip) is { } p) w = Math.Max(w, p.X);
        return w;
    }

    private static readonly bool Dbg = Environment.GetEnvironmentVariable("KL_DEBUG_LIFT") == "1";
    private bool LiftA()
    {
        var host = this.GetVisualAncestors().OfType<ContentPresenter>().FirstOrDefault(p => p.Name == "PART_SelectedContentHost");
        if (Dbg) Console.WriteLine($"[lift] A host={host is not null} tp={host?.TemplatedParent?.GetType().Name} liftable={(host?.TemplatedParent is TabControl t0 ? Liftable(t0) : false)} strip={(host?.TemplatedParent is TabControl t1 ? StripOf(t1)?.Bounds.ToString() : "-")} pt={(host is null ? "-" : this.TranslatePoint(new Point(0, 0), host)?.ToString())} w={Bounds.Width}");
        if (host?.TemplatedParent is not TabControl tc || !Liftable(tc)) return false;
        var strip = StripOf(tc);
        if (strip is null || strip.Bounds.Width <= 0 || strip.Bounds.Height > 40) return false;
        var p = this.TranslatePoint(new Point(0, 0), host);
        if (p is not { } pt || pt.X > 20 || pt.Y > 14) return false;
        var sw = StripW(strip); if (sw <= 0 || sw + 12 > Bounds.Width * 0.6) return false;
        if (_mode == 2) Unlift();
        _mode = 1; _liftHost = host; _liftStrip = strip;
        strip.ZIndex = 1;
        Inset = sw + 12;
        var sp = strip.TranslatePoint(new Point(0, 0), tc) ?? default;
        var hp = host.TranslatePoint(new Point(0, 0), tc) ?? default;
        var delta = hp.Y + pt.Y - (sp.Y + (strip.Bounds.Height - Bounds.Height) / 2);
        if (Math.Abs(delta) > 0.5)
        {
            _liftShift += delta;
            host.Margin = new Thickness(host.Margin.Left, -_liftShift, host.Margin.Right, host.Margin.Bottom);
        }
        return true;
    }

    private bool LiftB()
    {
        if (Parent is not Panel par) return false;
        var idx = par.Children.IndexOf(this);
        var next = par.Children.Skip(idx + 1).FirstOrDefault(c => c.IsVisible);
        if (next is not TabControl tc || !Liftable(tc)) return false;
        if (par is DockPanel && DockPanel.GetDock(this) != Dock.Top) return false;
        var strip = StripOf(tc);
        if (strip is null || strip.Bounds.Width <= 0 || strip.Bounds.Height > 40) return false;
        var sw = StripW(strip); if (sw <= 0 || sw + 12 > Bounds.Width * 0.6) return false;
        if (_mode == 1) Unlift();
        _mode = 2; _liftNext = tc; _liftStrip = strip; ZIndex = 1;
        Inset = sw + 12;
        var my = this.TranslatePoint(new Point(0, 0), par)?.Y ?? 0;
        var sy = strip.TranslatePoint(new Point(0, 0), par)?.Y ?? 0;
        var delta = sy - (my + (Bounds.Height - strip.Bounds.Height) / 2);
        if (Math.Abs(delta) > 0.5)
        {
            _liftShift += delta;
            tc.Margin = new Thickness(tc.Margin.Left, -_liftShift, tc.Margin.Right, tc.Margin.Bottom);
        }
        return true;
    }

    private void Unlift()
    {
        if (_liftHost is { } h && Math.Abs(_liftShift) > 0) h.Margin = new Thickness(h.Margin.Left, 0, h.Margin.Right, h.Margin.Bottom);
        if (_liftNext is { } n && Math.Abs(_liftShift) > 0) n.Margin = new Thickness(n.Margin.Left, 0, n.Margin.Right, n.Margin.Bottom);
        if (_liftStrip is { } s) s.ZIndex = 0;
        if (_mode == 2) ZIndex = 0;
        _liftShift = 0; _liftHost = null; _liftNext = null; _liftStrip = null; _mode = 0;
        if (_inset != 0) Inset = 0;
    }
}

/// <summary>Nút gạt (segmented): chọn 1 trong vài mục, có thể kèm số đếm. Dùng cho tab con / lọc nhanh / bật tắt chế độ xem.</summary>
public sealed class Seg : Border
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly List<ToggleButton> _btns = new();
    private int _sel = -1;
    public event Action<int>? Changed;
    public int Selected { get => _sel; set => Select(value, raise: false); }

    public Seg(params string[] items)
    {
        Classes.Add("seg");
        Child = _row;
        VerticalAlignment = VerticalAlignment.Center;
        foreach (var it in items) Add(it);
        if (items.Length > 0) Select(0, raise: false);
    }

    public ToggleButton Add(string text)
    {
        var i = _btns.Count;
        var b = new ToggleButton { Content = text, Classes = { "segitem" } };
        b.Click += (_, _) => { if (_sel == i) b.IsChecked = true; else Select(i, raise: true); };
        _btns.Add(b); _row.Children.Add(b);
        return b;
    }

    /// <summary>Đổi chữ mục i (vd kèm số đếm "Đang lệch 11").</summary>
    public void SetText(int i, string text) { if (i >= 0 && i < _btns.Count) _btns[i].Content = text; }
    public void SetTip(int i, string tip) { if (i >= 0 && i < _btns.Count) ToolTip.SetTip(_btns[i], tip); }
    public int Count => _btns.Count;

    private void Select(int i, bool raise)
    {
        if (i < 0 || i >= _btns.Count) return;
        var changed = _sel != i;
        _sel = i;
        for (var k = 0; k < _btns.Count; k++) _btns[k].IsChecked = k == i;
        if (raise && changed) Changed?.Invoke(i);
    }
}

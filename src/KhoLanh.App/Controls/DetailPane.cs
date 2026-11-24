using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.Core.Grid;

namespace KhoLanh.App.Controls;

/// <summary>
/// Khung chi tiết bên phải của bảng lớn (1.53): bấm một dòng → hiện đủ mọi cột của dòng đó (tên cột + giá trị, xuống dòng),
/// khỏi kéo ngang bảng. Thu / mở bằng nút ở đầu khung; trạng thái nhớ theo tài khoản (ui_detail:&lt;tk&gt;).
/// </summary>
public sealed class DetailPane : UserControl
{
    private const double PaneW = 330;
    private readonly SheetGrid _g;
    private readonly string _title;
    private readonly StackPanel _body = new() { Spacing = 6 };
    private readonly TextBlock _head = new() { FontWeight = FontWeight.Bold, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly Border _pane;
    private readonly Button _open, _close;
    private readonly Func<int, IEnumerable<(string Label, string Value)>>? _extra;

    private static string Key => "ui_detail:" + (App.Services.Session.Username ?? "").ToLowerInvariant();
    private static bool Remembered
    {
        get { try { return App.Services.Settings.Get(Key, "1") != "0"; } catch { return true; } }
        set { try { App.Services.Settings.Set(Key, value ? "1" : "0"); } catch { } }
    }

    /// <param name="gridHost">phần bảng (thường là khung viền bọc <paramref name="grid"/>)</param>
    /// <param name="extra">dòng thông tin thêm cho dòng dữ liệu (hiện trước các cột)</param>
    public DetailPane(Control gridHost, SheetGrid grid, string title = "Chi tiết dòng", Func<int, IEnumerable<(string Label, string Value)>>? extra = null)
    {
        _g = grid; _title = title; _extra = extra;
        _close = new Button { Content = Icon.Make("chevron-right", 15), Padding = new Thickness(6, 2), MinHeight = 0 };
        _close.Classes.Add("ghost"); ToolTip.SetTip(_close, "Thu khung chi tiết");
        _open = new Button { Content = new LayoutTransformControl { LayoutTransform = new RotateTransform(-90), Child = new TextBlock { Text = "Chi tiết dòng", FontSize = 12.5, FontWeight = FontWeight.SemiBold } },
            Padding = new Thickness(4, 10), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(_open, "Mở khung chi tiết của dòng đang chọn");

        var headRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_close, Dock.Right); headRow.Children.Add(_close); headRow.Children.Add(_head);
        var copy = new Button { Content = "Chép chi tiết", FontSize = 12, Padding = new Thickness(8, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        copy.Classes.Add("ghost");
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(string.Join("\r\n", Pairs(_g.Row).Select(p => p.Label + ": " + p.Value)));
        };
        var inner = new DockPanel();
        DockPanel.SetDock(headRow, Dock.Top); inner.Children.Add(headRow);
        DockPanel.SetDock(copy, Dock.Bottom); inner.Children.Add(copy);
        var sv = new ScrollViewer { Content = _body };
        ScrollViewer.SetHorizontalScrollBarVisibility(sv, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        inner.Children.Add(sv);
        _pane = new Border
        {
            Width = PaneW, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1), BorderBrush = AppTheme.Brush("#B9C3D0"), Background = AppTheme.Card, Child = inner,
        };

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        root.Children.Add(gridHost);
        Grid.SetColumn(_pane, 1); root.Children.Add(_pane);
        Grid.SetColumn(_open, 2); root.Children.Add(_open);
        Content = root;

        _close.Click += (_, _) => SetOpen(false);
        _open.Click += (_, _) => SetOpen(true);
        _g.SelectionChanged += Paint;
        void OnModel() { if (_pane.IsVisible) Paint(); }
        AttachedToVisualTree += (_, _) => { _g.Model.Changed -= OnModel; _g.Model.Changed += OnModel; SetOpen(Remembered, save: false); };
        DetachedFromVisualTree += (_, _) => _g.Model.Changed -= OnModel;
    }

    public bool IsOpen => _pane.IsVisible;

    public void SetOpen(bool open, bool save = true)
    {
        _pane.IsVisible = open; _open.IsVisible = !open;
        if (save) Remembered = open;
        if (open) Paint();
    }

    private IEnumerable<(string Label, string Value)> Pairs(int r)
    {
        var m = _g.Model;
        if (r < 0 || r >= m.RowCount) yield break;
        if (_extra is not null) foreach (var p in _extra(r)) yield return p;
        for (var c = 0; c < m.ColCount; c++)
        {
            var col = m.Columns[c];
            if (col.Width < 4) continue;                                  // cột ẩn (giữ mã dòng)
            var raw = m.Get(r, c);
            var v = _g.Display?.Invoke(r, c, raw) ?? raw;
            if (string.IsNullOrWhiteSpace(v)) continue;
            yield return (string.IsNullOrWhiteSpace(col.Header) ? GridModel.ColumnName(c) : col.Header, v);
        }
    }

    private void Paint()
    {
        if (!_pane.IsVisible) return;
        _body.Children.Clear();
        var m = _g.Model; var r = _g.Row;
        if (r < 0 || r >= m.RowCount)
        {
            _head.Text = _title;
            _body.Children.Add(new TextBlock { Text = m.RowCount == 0 ? "Bảng chưa có dòng nào." : "Bấm một dòng trong bảng để xem đủ thông tin ở đây.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            return;
        }
        _head.Text = $"{_title} · dòng {r + 1:N0} / {m.RowCount:N0}";
        var any = false;
        foreach (var (label, value) in Pairs(r))
        {
            any = true;
            var item = new StackPanel { Spacing = 1 };
            item.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Tag = "keep" });
            item.Children.Add(new SelectableTextBlock { Text = value, FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            _body.Children.Add(item);
        }
        if (!any) _body.Children.Add(new TextBlock { Text = "Dòng trống.", Classes = { "muted" } });
    }
}

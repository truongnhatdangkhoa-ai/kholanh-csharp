using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace KhoLanh.App.Controls;

/// <summary>
/// Dòng tóm tắt thông tin (1.84, mẫu chuẩn kiểu 3): các ô ít sửa (số phiếu, ngày lập, người ký…) thu thành 1 dòng 34px
/// "Thông tin phiếu  Số phiếu XA-1122 · Ngày lập … [✎ Sửa thông tin ▾]"; bấm nút → khung thả xuống chứa đủ ô nhập (<paramref name="editor"/>).
/// </summary>
public sealed class InfoLine : Border
{
    private readonly TextBlock _sum = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 };
    private readonly Func<IEnumerable<(string Label, string Value)>> _items;
    private readonly Button _edit;
    private readonly Flyout _fly;
    public event Action? Closed;

    public InfoLine(string title, Func<IEnumerable<(string Label, string Value)>> items, Control editor, string editText = "✎ Sửa thông tin")
    {
        _items = items;
        Classes.Add("infoline");
        Margin = new Thickness(0, 0, 0, 6);
        _fly = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new ScrollViewer { MaxHeight = 560, MaxWidth = 900, Content = new Border { Padding = new Thickness(6), Child = editor } },
        };
        _fly.Closed += (_, _) => { Refresh(); Closed?.Invoke(); };
        _edit = new Button { Content = editText + " ▾", Padding = new Thickness(10, 2), FontSize = 12, MinHeight = 24, Height = 26, VerticalAlignment = VerticalAlignment.Center, Flyout = _fly };
        var dp = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_edit, Dock.Right);
        var head = new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(head, Dock.Left);
        dp.Children.Add(_edit); dp.Children.Add(head); dp.Children.Add(_sum);
        Child = dp;
        AttachedToVisualTree += (_, _) => Refresh();
    }

    public bool CanEdit { get => _edit.IsEnabled; set => _edit.IsEnabled = value; }

    /// <summary>Viết lại dòng tóm tắt (gọi sau khi nạp / đổi dữ liệu).</summary>
    public void Refresh()
    {
        var inl = new Avalonia.Controls.Documents.InlineCollection();
        var first = true;
        foreach (var (l, v) in _items())
        {
            if (!first) inl.Add(new Avalonia.Controls.Documents.Run(" · ") { Foreground = AppTheme.Brush("#5D6B78") });
            inl.Add(new Avalonia.Controls.Documents.Run(l + " ") { Foreground = AppTheme.Brush("#5D6B78") });
            inl.Add(new Avalonia.Controls.Documents.Run(string.IsNullOrWhiteSpace(v) ? "—" : v));
            first = false;
        }
        _sum.Inlines = inl;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace KhoLanh.App.Controls;

/// <summary>
/// 1.119: chép chữ toàn app (người dùng: "app không cho copy ký tự"). Chữ hiển thị thường (TextBlock) của Avalonia không bôi đen được →
/// chuột phải vào dòng chữ bất kỳ: "Chép chữ này" / "Chép cả khung" (mọi dòng chữ trong khung chứa gần nhất). Bỏ qua chữ nằm trong nút,
/// mục menu, tab, ô chọn và chỗ đã có menu chuột phải riêng (bảng, ô nhập…). Hộp thông báo / lỗi / xác nhận dùng
/// <see cref="SelectableTextBlock"/> (bôi đen + Ctrl+C trực tiếp) — xem <c>Ui.Text</c>.
/// </summary>
public static class CopyText
{
    public static void Install() =>
        Control.ContextRequestedEvent.AddClassHandler<TextBlock>((tb, e) => OnContext(tb, e), RoutingStrategies.Bubble);

    private static void OnContext(TextBlock tb, ContextRequestedEventArgs e)
    {
        if (e.Handled || tb is SelectableTextBlock || !Eligible(tb)) return;
        var sel = TextSelect.SelectedText();   // 1.135: vừa bôi đen → mục đầu "Chép đoạn đã bôi đen"
        var text = TextSelect.TextOf(tb).Trim();
        if (text.Length == 0 && sel.Length == 0) return;
        var box = Box(tb);
        var all = box is null ? "" : AllText(box);
        var top = TopLevel.GetTopLevel(tb);
        var items = new List<MenuItem>();
        if (sel.Length > 0) items.Add(Item(top, "Chép đoạn đã bôi đen", sel));
        if (text.Length > 0) items.Add(Item(top, "Chép chữ này", text));
        if (all.Length > 0 && all != text) items.Add(Item(top, "Chép cả khung", all));
        var menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
        menu.Open(tb);
        e.Handled = true;
    }

    /// <summary>Chữ thường trên màn hình — không thuộc nút / menu / tab / ô chọn, không nằm trong control đã có menu chuột phải.</summary>
    internal static bool Eligible(Control tb)
    {
        if (tb.ContextMenu is not null || tb.ContextFlyout is not null) return false;
        foreach (var a in tb.GetVisualAncestors())
        {
            if (a is Button or MenuItem or TabItem or ComboBox or ComboBoxItem or TextBox or ToggleButton or ToolTip) return false;
            if (a is Control c && (c.ContextMenu is not null || c.ContextFlyout is not null)) return false;
            if (a is Window or PopupRoot) break;
        }
        return true;
    }

    /// <summary>Khung chứa gần nhất có từ 2 dòng chữ trở lên (thẻ / khung chi tiết / hộp thoại).</summary>
    private static Control? Box(TextBlock tb)
    {
        foreach (var a in tb.GetVisualAncestors().OfType<Control>())
        {
            if (a is Window or PopupRoot or ScrollViewer) return null;
            if (a is Border { Child: not null } or StackPanel or Grid or DockPanel or WrapPanel)
            {
                var n = a.GetVisualDescendants().OfType<TextBlock>().Count(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text) && Eligible(t));
                if (n >= 2 && n <= 80) return a;
            }
        }
        return null;
    }

    internal static string AllText(Control box) =>
        string.Join("\n", box.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text) && Eligible(t)).Select(t => t.Text!.Trim()));

    private static MenuItem Item(TopLevel? top, string header, string text)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += async (_, _) => { if (top?.Clipboard is { } cb) await cb.SetTextAsync(text); };
        return mi;
    }
}

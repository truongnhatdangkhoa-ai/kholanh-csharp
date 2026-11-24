using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace KhoLanh.App.Controls;

/// <summary>
/// Gọn màn hình (1.51): ghi chú hướng dẫn dài (chữ mờ "muted", xuống dòng, có sẵn chữ khi màn hình mở) trong trang chính
/// được ẩn để bảng có thêm chỗ — nội dung gom vào nút "Hướng dẫn" ở đầu trang (<see cref="Collect"/>).
/// Dòng trạng thái đổi chữ theo dữ liệu (chữ đổi sau khi hiện) thì hiện lại ngay. Muốn giữ một ghi chú: đặt Tag = "keep".
/// </summary>
public static class HintTrim
{
    /// <summary>Ghi chú từ độ dài này trở lên mới bị ẩn (câu ngắn như "Ô trống." giữ nguyên).</summary>
    public const int MinLength = 60;

    public static readonly AttachedProperty<bool> TrimmedProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("Trimmed", typeof(HintTrim));

    /// <summary>Bật / tắt cho toàn app (hộp "Giao diện & cỡ chữ" có thể đổi; mặc định bật).</summary>
    public static bool Enabled { get; set; } = true;

    public static void Install()
    {
        Control.LoadedEvent.AddClassHandler<TextBlock>((tb, _) => OnLoaded(tb), RoutingStrategies.Direct);
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((tb, e) =>
        {
            // chữ đổi sau khi đã ẩn → là dòng trạng thái động, không phải ghi chú cố định → hiện lại
            if (tb.GetValue(TrimmedProperty) && !Equals(e.OldValue, e.NewValue)) { tb.SetValue(TrimmedProperty, false); tb.IsVisible = true; }
        });
    }

    private static void OnLoaded(TextBlock tb)
    {
        if (!Enabled || tb.GetValue(TrimmedProperty) || !tb.IsVisible) return;
        if (!tb.Classes.Contains("muted") || tb.TextWrapping == TextWrapping.NoWrap) return;
        if (tb.Tag is "keep" || (tb.Text?.Trim().Length ?? 0) < MinLength) return;
        if (!InPage(tb)) return;
        tb.SetValue(TrimmedProperty, true);
        tb.IsVisible = false;
    }

    private static bool InPage(Visual v)
    {
        // chỉ trang trong khung chính (không đụng hộp thoại, màn hình đăng nhập, khung Tìm nhanh)
        foreach (var a in v.GetVisualAncestors())
            if (a is ContentControl { Name: "PageHost" }) return true;
        return false;
    }

    /// <summary>Các ghi chú đã ẩn thuộc phần đang thấy của trang (tab đang mở), theo thứ tự trên màn hình.</summary>
    public static List<string> Collect(Visual page)
    {
        var list = new List<string>();
        foreach (var tb in page.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!tb.GetValue(TrimmedProperty) || tb.Text is not { Length: > 0 } t) continue;
            if (tb.GetVisualParent() is Visual p && !p.IsEffectivelyVisible) continue;   // thuộc tab đang ẩn
            if (!list.Contains(t)) list.Add(t);
        }
        return list;
    }

    /// <summary>Nội dung khung "Hướng dẫn" ở đầu trang.</summary>
    public static Control Panel(IReadOnlyList<string> hints)
    {
        var sp = new StackPanel { Spacing = 8, Width = 410 };   // khung Flyout mặc định rộng tối đa ~456 px
        sp.Children.Add(new TextBlock { Text = "Hướng dẫn màn hình này", FontWeight = FontWeight.Bold, FontSize = 14 });
        if (hints.Count == 0) sp.Children.Add(new TextBlock { Text = "Màn hình này không có ghi chú hướng dẫn.", Classes = { "muted" } });
        foreach (var h in hints)
            sp.Children.Add(new SelectableTextBlock { Text = "• " + h, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        var sv = new ScrollViewer { MaxHeight = 520, Content = sp };
        ScrollViewer.SetHorizontalScrollBarVisibility(sv, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        return sv;
    }
}

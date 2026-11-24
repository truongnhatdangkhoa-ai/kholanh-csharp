using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace KhoLanh.App.Controls;

/// <summary>
/// Bộ biểu tượng nét mảnh dùng chung (thay emoji trong nút / tiêu đề — emoji hiển thị khác nhau giữa các máy).
/// Vẽ bằng đường nét trên khung 24 × 24, nét 1.8, màu theo chữ của nơi đặt (hoặc màu truyền vào) nên tự đổi theo Sáng / Tối.
/// Dùng: <c>Icon.Make("box")</c>, <c>Icon.Button("Xuất Excel", "download")</c>.
/// </summary>
public static class Icon
{
    /// <summary>Tên → dữ liệu đường vẽ (cú pháp path SVG, toạ độ 0–24).</summary>
    public static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>
    {
        ["home"] = "M3 11 L12 3 L21 11 M5 10 V20 H19 V10",
        ["box"] = "M21 8 L12 3 L3 8 L12 13 Z M3 8 V16 L12 21 L21 16 V8 M12 13 V21",
        ["pin"] = "M12 21 C12 21 5 14.8 5 10 A7 7 0 0 1 19 10 C19 14.8 12 21 12 21 Z M12 7.5 A2.5 2.5 0 1 1 12 12.5 A2.5 2.5 0 1 1 12 7.5",
        ["snow"] = "M12 2 V22 M4.9 6 L19.1 18 M19.1 6 L4.9 18",
        ["map"] = "M3 6 L9 3 L15 6 L21 3 V18 L15 21 L9 18 L3 21 Z M9 3 V18 M15 6 V21",
        ["edit"] = "M4 20 H8 L19 9 L15 5 L4 16 Z M13 7 L17 11",
        ["chart"] = "M4 20 V10 M10 20 V4 M16 20 V13 M22 20 H2",
        ["check"] = "M9 11 L12 14 L20 6 M20 12 V19 A2 2 0 0 1 18 21 H6 A2 2 0 0 1 4 19 V5 A2 2 0 0 1 6 3 H15",
        ["doc"] = "M14 3 H6 A2 2 0 0 0 4 5 V19 A2 2 0 0 0 6 21 H18 A2 2 0 0 0 20 19 V9 Z M14 3 V9 H20 M8 13 H16 M8 17 H13",
        ["list"] = "M8 6 H21 M8 12 H21 M8 18 H21 M3.5 6 H4 M3.5 12 H4 M3.5 18 H4",
        ["truck"] = "M3 6 H14 V16 H3 Z M14 9 H18 L21 12 V16 H14 M5 18 A2 2 0 1 0 9 18 A2 2 0 1 0 5 18 M15 18 A2 2 0 1 0 19 18 A2 2 0 1 0 15 18",
        ["print"] = "M6 9 V3 H18 V9 M6 18 H4 A2 2 0 0 1 2 16 V11 A2 2 0 0 1 4 9 H20 A2 2 0 0 1 22 11 V16 A2 2 0 0 1 20 18 H18 M6 14 H18 V21 H6 Z",
        ["tool"] = "M14.7 6.3 A4 4 0 0 1 9.3 11.7 L4 17 L7 20 L12.3 14.7 A4 4 0 0 1 17.7 9.3 L15.1 11.9 L12.7 11.3 L12.1 8.9 Z",
        ["cube"] = "M12 2 L21 7 V17 L12 22 L3 17 V7 Z M3 7 L12 12 L21 7 M12 12 V22",
        ["scan"] = "M3 7 V4 A1 1 0 0 1 4 3 H7 M17 3 H20 A1 1 0 0 1 21 4 V7 M21 17 V20 A1 1 0 0 1 20 21 H17 M7 21 H4 A1 1 0 0 1 3 20 V17 M7 12 H17",
        ["compare"] = "M8 3 V21 M16 3 V21 M3 8 H8 M16 16 H21 M3 16 H8 M16 8 H21",
        ["user"] = "M20 21 V19 A4 4 0 0 0 16 15 H8 A4 4 0 0 0 4 19 V21 M12 3 A4 4 0 1 1 12 11 A4 4 0 1 1 12 3",
        ["users"] = "M17 21 V19 A4 4 0 0 0 13 15 H5 A4 4 0 0 0 1 19 V21 M9 3 A4 4 0 1 1 9 11 A4 4 0 1 1 9 3 M23 21 V19 A4 4 0 0 0 20 15.1 M16 3.1 A4 4 0 0 1 16 10.9",
        ["save"] = "M19 21 H5 A2 2 0 0 1 3 19 V5 A2 2 0 0 1 5 3 H16 L21 8 V19 A2 2 0 0 1 19 21 Z M17 21 V13 H7 V21 M7 3 V8 H15",
        ["network"] = "M9 2 H15 V8 H9 Z M2 16 H8 V22 H2 Z M16 16 H22 V22 H16 Z M12 8 V12 M5 16 V12 H19 V16",
        ["cloud"] = "M18 10 H16.7 A7 7 0 1 0 9 19 H18 A4.5 4.5 0 0 0 18 10 Z",
        ["phone"] = "M7 2 H17 A2 2 0 0 1 19 4 V20 A2 2 0 0 1 17 22 H7 A2 2 0 0 1 5 20 V4 A2 2 0 0 1 7 2 Z M11 18 H13",
        ["history"] = "M3 12 A9 9 0 1 0 6 5.3 L3 8 M3 3 V8 H8 M12 7 V12 L15 14",
        ["assist"] = "M12 3 L14.1 8.9 L20 11 L14.1 13.1 L12 19 L9.9 13.1 L4 11 L9.9 8.9 Z M19 3 V7 M17 5 H21",
        ["gear"] = "M12 9 A3 3 0 1 1 12 15 A3 3 0 1 1 12 9 M12 2 V5 M12 19 V22 M4.9 4.9 L7 7 M17 17 L19.1 19.1 M2 12 H5 M19 12 H22 M4.9 19.1 L7 17 M17 7 L19.1 4.9",
        ["search"] = "M11 4 A7 7 0 1 1 11 18 A7 7 0 1 1 11 4 M21 21 L16 16",
        ["plus"] = "M12 5 V19 M5 12 H19",
        ["download"] = "M12 4 V16 M6 10 L12 16 L18 10 M4 20 H20",
        ["upload"] = "M12 20 V8 M6 14 L12 8 L18 14 M4 4 H20",
        ["refresh"] = "M21 12 A9 9 0 1 1 18.4 5.6 L21 8 M21 3 V8 H16",
        ["trash"] = "M3 6 H21 M8 6 V4 H16 V6 M6 6 L7 21 H17 L18 6 M10 11 V17 M14 11 V17",
        ["undo"] = "M9 14 L4 9 L9 4 M4 9 H15 A5 5 0 0 1 15 19 H11",
        ["copy"] = "M9 9 H20 V20 H9 Z M5 15 H4 V4 H15 V5",
        ["filter"] = "M3 4 H21 L14 12.5 V19 L10 21 V12.5 Z",
        ["chevron-left"] = "M15 6 L9 12 L15 18",
        ["chevron-right"] = "M9 6 L15 12 L9 18",
        ["chevron-down"] = "M6 9 L12 15 L18 9",
        ["x"] = "M6 6 L18 18 M18 6 L6 18",
        ["warn"] = "M12 3 L22 20 H2 Z M12 9 V14 M12 17 V17.5",
        ["info"] = "M12 3 A9 9 0 1 1 12 21 A9 9 0 1 1 12 3 M12 11 V16 M12 7.5 V8",
        ["ok"] = "M12 3 A9 9 0 1 1 12 21 A9 9 0 1 1 12 3 M8 12 L11 15 L16 9",
        ["bolt"] = "M13 2 L4 14 H12 L11 22 L20 10 H12 Z",
        ["lock"] = "M5 11 H19 V21 H5 Z M8 11 V7 A4 4 0 0 1 16 7 V11",
        ["calendar"] = "M4 5 H20 V21 H4 Z M4 10 H20 M8 3 V7 M16 3 V7",
        ["mail"] = "M3 5 H21 V19 H3 Z M3 6 L12 13 L21 6",
        ["logout"] = "M9 21 H5 A2 2 0 0 1 3 19 V5 A2 2 0 0 1 5 3 H9 M16 17 L21 12 L16 7 M21 12 H9",
    };

    /// <summary>Biểu tượng cỡ <paramref name="size"/> px; <paramref name="brush"/> null = theo màu chữ của control cha.</summary>
    public static Control Make(string name, double size = 16, IBrush? brush = null, double stroke = 1.8)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(Paths.TryGetValue(name, out var d) ? d : Paths["info"]),
            StrokeThickness = stroke, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent, Width = 24, Height = 24, Stretch = Stretch.None,
        };
        if (brush is not null) path.Stroke = brush;
        else path.Bind(Shape.StrokeProperty, path.GetObservable(TextElement.ForegroundProperty));
        return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = new Canvas { Width = 24, Height = 24, Children = { path } }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    }

    /// <summary>Nội dung nút: biểu tượng + chữ.</summary>
    public static Control Label(string text, string icon, double size = 15) =>
        new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 7, Children = { Make(icon, size), new TextBlock { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } } };

    /// <summary>Nút có biểu tượng; <paramref name="kind"/>: "" (thường) · "primary" · "ghost" · "danger".</summary>
    public static Button Button(string text, string icon, string kind = "")
    {
        var b = new Button { Content = Label(text, icon) };
        if (kind.Length > 0) b.Classes.Add(kind);
        return b;
    }
}

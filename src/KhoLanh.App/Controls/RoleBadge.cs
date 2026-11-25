using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Controls;

/// <summary>
/// 1.97 (phương án B người dùng chọn): biểu tượng tài khoản theo quyền — ô vuông bo góc màu theo quyền, hình khiên (Quản trị) /
/// cặp (Quản lý) / người (Nhân viên), kèm nhãn quyền màu nhạt cùng tông. Dùng ở thanh menu, menu tài khoản, Hệ thống › Tài khoản.
/// </summary>
public static class RoleBadge
{
    public static (string Bg, string SoftBg, string Fg, string Icon) Style(string role) => role switch
    {
        Roles.Admin => ("#5B4BC4", "#EEEBFC", "#5B4BC4", "shield-check"),
        Roles.Manager => ("#1F6FD6", "#E6F0FC", "#1859AE", "briefcase"),
        _ => ("#1E7B4A", "#E3F4EA", "#1E7B4A", "user"),
    };

    /// <summary>Ô biểu tượng tài khoản (vuông bo góc, hình trắng).</summary>
    public static Control Avatar(string role, double size = 28)
    {
        var st = Style(role);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(Math.Round(size * 0.27)), Background = AppTheme.Brush(st.Bg),
            Child = Icon.Make(st.Icon, Math.Round(size * 0.58), Brushes.White, 2.2), VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Nhãn quyền (nền nhạt, chữ đậm cùng tông).</summary>
    public static Control Pill(string role, double fontSize = 11.5)
    {
        var st = Style(role);
        return new Border
        {
            Background = AppTheme.Brush(st.SoftBg), CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 1.5), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Roles.Label(role), FontSize = fontSize, FontWeight = FontWeight.SemiBold, Foreground = AppTheme.Brush(st.Fg) },
        };
    }

    /// <summary>Biểu tượng + tên + nhãn quyền trên 1 hàng.</summary>
    public static Control Line(string role, string name, double size = 26, double nameSize = 13)
        => new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Avatar(role, size),
                new TextBlock { Text = name, FontSize = nameSize, FontWeight = FontWeight.SemiBold, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
                Pill(role),
            },
        };
}

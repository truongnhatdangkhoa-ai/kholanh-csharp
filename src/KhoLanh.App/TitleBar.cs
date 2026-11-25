using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace KhoLanh.App;

/// <summary>
/// Thanh tiêu đề của Windows (dải chứa tên cửa sổ + nút thu nhỏ / phóng to / đóng) theo màu giao diện (1.74):
/// cửa sổ chính = màu thanh menu trên cùng (NavBg) → liền một khối; hộp thoại = màu nền thẻ (CardBg) của Sáng dịu / Tối.
/// Dùng DWM (dwmapi): màu nền thanh tiêu đề + màu chữ chạy trên Windows 11; chế độ tối của thanh tiêu đề chạy cả Windows 10 (2004+).
/// Hệ điều hành khác / Windows cũ: bỏ qua, không lỗi. Tự áp khi mở cửa sổ và khi đổi giao diện.
/// </summary>
public static class TitleBar
{
    private const int DwmUseImmersiveDarkMode = 20, DwmUseImmersiveDarkModeOld = 19, DwmCaptionColor = 35, DwmTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Install()
    {
        if (!OperatingSystem.IsWindows()) return;
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => Apply(w), RoutingStrategies.Direct);
        AppTheme.Changed += () =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                foreach (var w in d.Windows) Apply(w);
        };
    }

    /// <summary>Màu thanh tiêu đề của cửa sổ (theo giao diện hiện tại).</summary>
    public static (Color Caption, Color Text, bool Dark) ColorsFor(Window w)
    {
        var main = w is MainWindow;
        var cap = main ? AppTheme.Color("#10243D") : AppTheme.Color("#FFFFFF");
        var dark = main || AppTheme.Current == AppTheme.Mode.Dark;
        var text = dark ? AppTheme.Color("#DCE5F0") : AppTheme.Color("#1A2433");
        if (main && AppTheme.Current == AppTheme.Mode.Dark) text = Colors.White;
        return (cap, text, dark);
    }

    public static void Apply(Window w)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (w.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero) return;
            var (cap, text, dark) = ColorsFor(w);
            var on = dark ? 1 : 0;
            if (DwmSetWindowAttribute(h, DwmUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(h, DwmUseImmersiveDarkModeOld, ref on, sizeof(int));
            var c = ColorRef(cap); DwmSetWindowAttribute(h, DwmCaptionColor, ref c, sizeof(int));
            var t = ColorRef(text); DwmSetWindowAttribute(h, DwmTextColor, ref t, sizeof(int));
        }
        catch { /* Windows cũ không có dwmapi / thuộc tính: giữ thanh tiêu đề mặc định */ }
    }

    /// <summary>COLORREF = 0x00BBGGRR.</summary>
    public static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}

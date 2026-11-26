using System.Globalization;
using Avalonia;

namespace KhoLanh.App;

internal static class Program
{
    public static string[] Args { get; private set; } = Array.Empty<string>();

    [STAThread]
    public static int Main(string[] args)
    {
        Args = args;
        // 1.129: máy chủ đang chạy ẩn ở khay → mở KhoLanh.exe lần nữa chỉ hiện lại cửa sổ đó (không mở bản thứ 2 tranh cổng máy chủ)
        if (args.Length == 0 && ChayAn.WakeExisting()) return 0;
        // Lỗi chưa bắt → logs/loi.log (Trợ lý hệ thống đọc lại để báo Quản trị).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex && App.DataFolder.Length > 0) KhoLanh.Core.TroLy.TroLyService.LogUnhandled(App.DataFolder, ex, "Lỗi chưa bắt");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            if (App.DataFolder.Length > 0) KhoLanh.Core.TroLy.TroLyService.LogUnhandled(App.DataFolder, e.Exception, "Tác vụ nền lỗi");
            e.SetObserved();
        };
        // 1.71: văn hóa vi-VN nhưng SỐ theo 1 kiểu duy nhất 1,500.00 (dấu phẩy ngăn nghìn, dấu chấm thập phân) — người dùng chốt.
        var vi = KhoLanh.Core.Grid.NumFmt.Culture;
        CultureInfo.DefaultThreadCurrentCulture = vi;
        CultureInfo.DefaultThreadCurrentUICulture = vi;
        CultureInfo.CurrentCulture = vi;
        CultureInfo.CurrentUICulture = vi;
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown); }
        catch (InvalidOperationException e) when (e.Message.Contains("Dispatcher shut down", StringComparison.OrdinalIgnoreCase))
        {
            // 1.117 (Trợ lý: "Cannot perform requested operation because the Dispatcher shut down" ở Program.Main, 4 lần / 7 ngày): app đang thoát
            // (đóng / tự cập nhật) mà vòng giao diện còn lượt chờ — Avalonia ném lỗi này SAU khi đã đóng mọi cửa sổ. Không mất dữ liệu, không ghi lỗi.
            return 0;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

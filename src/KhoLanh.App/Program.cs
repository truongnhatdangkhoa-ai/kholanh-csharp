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
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

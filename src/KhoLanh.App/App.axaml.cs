using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KhoLanh.Core;
using KhoLanh.Core.Data;

namespace KhoLanh.App;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;
    public static string DataFolder { get; private set; } = "";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                InitServices(Environment.GetEnvironmentVariable("KHOLANH_DATA") is { Length: > 0 } custom
                    ? custom : Database.DefaultDataFolder);
                try { Services.Backup.DailyAutoBackup(); } catch { /* sao lưu tự động lỗi không chặn mở app */ }
                ShowLogin(desktop);
            }
            catch (Exception ex)
            {
                var w = new Window
                {
                    Title = "Kho Lạnh - lỗi khởi động", Width = 560, Height = 240,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new TextBlock { Text = "Không mở được dữ liệu:\n" + ex.Message, Margin = new Thickness(20), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                };
                w.Closed += (_, _) => desktop.Shutdown();
                desktop.MainWindow = w;
                w.Show();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Mở CSDL trong thư mục dữ liệu (dùng cả cho công cụ chụp màn hình).</summary>
    public static void InitServices(string dataFolder)
    {
        DataFolder = dataFolder;
        Services = new AppServices(Path.Combine(DataFolder, "khodata.db"));
    }

    public static void ShowLogin(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var login = new LoginWindow();
        login.LoggedIn += () =>
        {
            var main = new MainWindow();
            main.LoggedOut += () => { ShowLogin(desktop); main.Close(); };
            main.Closed += (_, _) => { if (!main.IsLoggingOut) desktop.Shutdown(); };
            desktop.MainWindow = main;
            main.Show();
            login.Close();
        };
        login.Closed += (_, _) => { if (!login.Succeeded) desktop.Shutdown(); };
        desktop.MainWindow = login;
        login.Show();
    }
}

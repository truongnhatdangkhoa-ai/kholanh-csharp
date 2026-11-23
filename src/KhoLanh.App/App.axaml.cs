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
            var ti = Array.IndexOf(Program.Args, "--tu-kiem-module");
            if (ti >= 0)
            {
                SelfTest.Run(desktop, Program.Args.Length > ti + 1 ? Program.Args[ti + 1] : "tu-kiem-module.json");
                base.OnFrameworkInitializationCompleted();
                return;
            }
            // Máy mới cài lần đầu → chọn nơi lưu database trước khi đăng nhập.
            var loc = new DataLocation();
            if (loc.NeedsChoice && Array.IndexOf(Program.Args, "--da-cap-nhat") < 0)
            {
                var w = new DataFolderWindow(loc);
                w.Done += folder => { Start(desktop, folder); w.Close(); };
                w.Closed += (_, _) => { if (w.Chosen is null) desktop.Shutdown(); };
                desktop.MainWindow = w;
                w.Show();
                base.OnFrameworkInitializationCompleted();
                return;
            }
            Start(desktop, loc.Resolve());
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void Start(IClassicDesktopStyleApplicationLifetime desktop, string dataFolder)
    {
            try
            {
                InitServices(dataFolder);
                // Bản mới đã tải ngầm từ lần trước → áp dụng ngay trước khi hiện cửa sổ (không cần cài lại).
                AppUpdater.Cleanup();
                if (Array.IndexOf(Program.Args, "--da-cap-nhat") >= 0 && Environment.GetEnvironmentVariable("KHOLANH_TU_KIEM_THOAT") == "1")
                {
                    // Chỉ dùng cho kiểm thử tự động: xác nhận bản mới đã được mở lại sau khi thay exe.
                    File.WriteAllText(Path.Combine(DataFolder, "da-cap-nhat.txt"), Environment.ProcessPath ?? "");
                    desktop.Shutdown(); return;
                }
                if (Array.IndexOf(Program.Args, "--tu-kiem-cap-nhat") >= 0 && Environment.GetEnvironmentVariable("KHOLANH_TU_KIEM_THOAT") == "1")
                {
                    // Chỉ dùng cho kiểm thử tự động: cập nhật khi app ĐANG MỞ (cửa sổ chính + module WebView2), như người dùng bấm Cập nhật.
                    if (!Services.Auth.HasAnyUser()) Services.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
                    Services.Auth.Login("admin", "matkhau1");
                    var main = new MainWindow();
                    desktop.MainWindow = main;
                    main.Show();
                    main.Navigate("m1");
                    Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                    {
                        try { if (AppUpdater.ApplyAndRestart()) AppUpdater.ExitForUpdate(); }
                        catch (Exception ex) { File.WriteAllText(Path.Combine(DataFolder, "loi-tu-kiem.txt"), ex.ToString()); desktop.Shutdown(1); }
                    }, TimeSpan.FromSeconds(10));
                    return;
                }
                if (OperatingSystem.IsWindows() && AppUpdater.StagedVersion is not null && Services.Updates.Auto)
                {
                    try { if (AppUpdater.ApplyAndRestart()) { AppUpdater.ExitForUpdate(); return; } }
                    catch { /* không thay được exe: chạy tiếp bản hiện tại */ }
                }
                try { Services.Backup.DailyAutoBackup(); } catch { /* sao lưu tự động lỗi không chặn mở app */ }
                StartLan(desktop);
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

    /// <summary>Mở CSDL trong thư mục dữ liệu (dùng cả cho công cụ chụp màn hình).</summary>
    public static void InitServices(string dataFolder)
    {
        DataFolder = dataFolder;
        var db = Path.Combine(DataFolder, "khodata.db");
        Services = new AppServices(db);
        // Bộ cài máy mới có mã LAN → máy này làm máy con (dùng chung dữ liệu với máy chủ).
        if (!Services.Auth.HasAnyUser() && !Services.Lan.Enabled && KhoLanh.Core.Lan.LanService.BundledCode() is { } lan)
        {
            try { KhoLanh.Core.Lan.LanService.ImportBundle(Services.Settings, lan); Services = new AppServices(db); }
            catch { /* mã hỏng: dùng như máy đơn */ }
        }
    }

    /// <summary>Câu báo lần tự nhận vai LAN gần nhất (lúc đăng nhập).</summary>
    public static string LanNote { get; set; } = "";
    /// <summary>Lỗi ghi địa chỉ máy chủ lên Google Sheet (rỗng = ổn).</summary>
    public static string LanPublishNote { get; private set; } = "";
    private static int _publishing;

    /// <summary>Bật chế độ LAN (nếu đã cấu hình) và tắt khi thoát. Máy chủ / dự phòng ghi địa chỉ lên Google Sheet mỗi 2 phút.</summary>
    public static void StartLan(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try { Services.Lan.Start(); } catch (Exception ex) { Ui.Describe(ex); }
        try { Services.Drive.Start(); } catch { }
        desktop.Exit += (_, _) => { try { Services.Drive.Stop(); Services.Lan.Dispose(); } catch { } };
        var t = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        t.Tick += (_, _) => PublishLan();
        t.Start();
    }

    /// <summary>Ghi địa chỉ / khóa máy chủ lên Google Sheet nếu khác (chạy ngầm, không chặn giao diện).</summary>
    public static void PublishLan()
    {
        if (Services.Session.CurrentUser is null || Interlocked.Exchange(ref _publishing, 1) == 1) return;
        var s = Services;
        _ = Task.Run(async () =>
        {
            try { LanPublishNote = await KhoLanh.Core.Lan.LanPlanSync.PublishAsync(s) ?? ""; }
            catch (Exception ex) { LanPublishNote = ex.Message; }
            finally { Interlocked.Exchange(ref _publishing, 0); }
        });
    }

    public static void ShowLogin(IClassicDesktopStyleApplicationLifetime desktop, bool afterLogout = false)
    {
        var login = new LoginWindow { AfterLogout = afterLogout };
        login.LoggedIn += () =>
        {
            var main = new MainWindow();
            Avalonia.Threading.DispatcherTimer.RunOnce(PublishLan, TimeSpan.FromSeconds(5));
            if (LanNote.Length > 0)
            {
                var note = LanNote; LanNote = "";
                Avalonia.Threading.DispatcherTimer.RunOnce(() => _ = Ui.Message(main, note + "\n\nVai mạng LAN do Quản trị gán theo tài khoản (Hệ thống › Mạng LAN)."), TimeSpan.FromMilliseconds(600));
            }
            main.LoggedOut += () => { ShowLogin(desktop, afterLogout: true); main.Close(); };
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

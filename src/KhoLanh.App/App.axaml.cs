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

    public override void Initialize() { AvaloniaXamlLoader.Load(this); AppTheme.Install(this); Controls.Icon.InstallEmojiSwap(); Controls.HintTrim.Install(); Controls.CopyText.Install(); TitleBar.Install(); }

    public override void OnFrameworkInitializationCompleted()
    {
        // Lỗi xảy ra trong thao tác giao diện (nút bấm, hộp thoại…) KHÔNG làm sập app: ghi logs/loi.log rồi báo người dùng.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            try { if (DataFolder.Length > 0) KhoLanh.Core.TroLy.TroLyService.LogUnhandled(DataFolder, e.Exception, "Lỗi giao diện"); } catch { /* ghi log lỗi thì bỏ qua */ }
            var top = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (top is not null)
                _ = Ui.Message(top, "Thao tác vừa rồi gặp lỗi nên chưa hoàn tất (app vẫn chạy bình thường, lỗi đã ghi vào nhật ký):\n\n" + Ui.Describe(e.Exception), "Có lỗi");
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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
                StartPerf();
                // Bản mới đã tải ngầm từ lần trước → áp dụng ngay trước khi hiện cửa sổ (không cần cài lại).
                AppUpdater.Cleanup();
                AppUpdater.Warmup();
                if (Array.IndexOf(Program.Args, "--da-cap-nhat") >= 0 && Environment.GetEnvironmentVariable("KHOLANH_TU_KIEM_THOAT") == "1")
                {
                    // Chỉ dùng cho kiểm thử tự động: xác nhận bản mới đã được mở lại sau khi thay exe.
                    File.WriteAllText(Path.Combine(DataFolder, "da-cap-nhat.txt"), Environment.ProcessPath ?? "");
                    desktop.Shutdown(); return;
                }
                if (Array.IndexOf(Program.Args, "--tu-kiem-cap-nhat") >= 0 && Environment.GetEnvironmentVariable("KHOLANH_TU_KIEM_THOAT") == "1")
                {
                    // Chỉ dùng cho kiểm thử tự động: cập nhật khi app ĐANG MỞ (cửa sổ chính), như người dùng bấm Cập nhật.
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
                // Sao lưu mỗi ngày chạy NGẦM (trước 1.59 chép cả CSDL trước khi hiện màn hình đăng nhập).
                var backup = Services.Backup;
                _ = Task.Run(() => { try { backup.DailyAutoBackup(); } catch { /* sao lưu tự động lỗi không chặn mở app */ } });
                WindowsStartup.Refresh();
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
    /// <summary>
    /// 1.102: thời điểm app chạy bản MỚI lần đầu (settings "app_upd_at"), đổi khi phiên bản khác "app_last_ver". Trả true nếu lần mở này là lần đầu
    /// sau cập nhật (Trợ lý quét sớm). Máy mới (chưa có "app_last_ver") không tính là cập nhật.
    /// </summary>
    public static bool NoteVersion()
    {
        try
        {
            var st = Services.Settings; var cur = KhoLanh.Core.Services.UpdateService.Current.ToString(3);
            var last = st.Get("app_last_ver");
            if (last == cur) return false;
            st.Set("app_last_ver", cur);
            if (last.Length == 0) return false;
            st.Set("app_upd_at", DateTime.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }
        catch { return false; }
    }

    public static DateTime? UpdatedAt()
    {
        try
        {
            return DateTime.TryParse(Services.Settings.Get("app_upd_at"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        }
        catch { return null; }
    }

    public static void InitServices(string dataFolder)
    {
        DataFolder = dataFolder;
        KhoLanh.Core.TroLy.Perf.DataDir = dataFolder;   // 1.72: ghi thao tác chậm vào logs/cham.log
        var db = Path.Combine(DataFolder, "khodata.db");
        Services = new AppServices(db);
        // Bộ cài máy mới có mã LAN → máy này làm máy con (dùng chung dữ liệu với máy chủ).
        if (!Services.Auth.HasAnyUser() && !Services.Lan.Enabled && KhoLanh.Core.Lan.LanService.BundledCode() is { } lan)
        {
            try { KhoLanh.Core.Lan.LanService.ImportBundle(Services.Settings, lan); Services = new AppServices(db); }
            catch { /* mã hỏng: dùng như máy đơn */ }
        }
        // 1.121: bỏ Thư mục / Drive dùng chung — máy cũ còn cài tự chuyển (trước khi bật LAN); câu báo hiện ở tab Máy chủ / nhật ký.
        DriveRetireNote = Services.RetireDriveSync() ?? "";
    }

    /// <summary>1.121: câu báo lần chuyển máy cũ khỏi Drive dùng chung (rỗng = máy không dùng Drive dùng chung) — tab Máy chủ hiện.</summary>
    public static string DriveRetireNote { get; private set; } = "";

    /// <summary>Câu báo lần tự nhận vai LAN gần nhất (lúc đăng nhập).</summary>
    public static string LanNote { get; set; } = "";
    /// <summary>Lỗi ghi địa chỉ máy chủ lên Google Sheet (rỗng = ổn).</summary>
    public static string LanPublishNote { get; private set; } = "";
    private static int _publishing;

    /// <summary>Bật chế độ LAN (nếu đã cấu hình) và tắt khi thoát. Máy chủ / dự phòng ghi địa chỉ lên Google Sheet mỗi 2 phút.</summary>
    public static void StartLan(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try { Services.Lan.Start(); } catch (Exception ex) { Ui.Describe(ex); }
        // 1.121: không còn đồng bộ Thư mục / Drive dùng chung (DriveRetire lúc mở app) — DriveSyncService không chạy nền nữa.
        desktop.Exit += (_, _) => { try { Services.Lan.Dispose(); } catch { } };
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

    /// <summary>
    /// Đã vào bằng danh sách tài khoản lưu trong máy, danh sách mới còn đang tải ngầm: tải xong mà tài khoản bị khóa / đổi mật khẩu
    /// ở máy khác thì báo và đăng xuất (quyền mới đã tự cập nhật vào phiên).
    /// </summary>
    private static async void WatchAccounts(MainWindow main, KhoLanh.Core.Services.LoginFlow.Result r)
    {
        try { await r.Pending!; } catch { return; }   // lỗi mạng: giữ bản lưu như trước
        try
        {
            if (main.IsLoggingOut || Services.Session.CurrentUser?.Id != r.User.Id) return;
            var why = KhoLanh.Core.Services.LoginFlow.Recheck(Services.Db, r.User, r.PasswordHash);
            if (why is null) return;
            Services.Remember.Forget();
            await Ui.Message(main, why, "Đăng nhập lại");
            if (!main.IsLoggingOut && Services.Session.CurrentUser?.Id == r.User.Id) main.Logout();
        }
        catch { /* kiểm lại thất bại: giữ phiên hiện tại */ }
    }

    /// <summary>1.100: bộ đo hiệu năng chạy ngầm (mỗi phút; lưu hieu-nang.db trong thư mục dữ liệu, giữ 30 ngày).</summary>
    public static KhoLanh.Core.TroLy.PerfMonitor? Perf;
    private static bool _openMarked;
    private static void StartPerf()
    {
        try { Perf ??= new KhoLanh.Core.TroLy.PerfMonitor(DataFolder, () => Services?.Lan.ServerAddress); Perf.Start(); }
        catch { /* không đo được thì thôi */ }
    }

    public static void ShowLogin(IClassicDesktopStyleApplicationLifetime desktop, bool afterLogout = false)
    {
        var login = new LoginWindow { AfterLogout = afterLogout };
        if (!_openMarked)
        {   // thời gian mở app = từ lúc tiến trình chạy tới khi cửa sổ đăng nhập hiện
            _openMarked = true;
            login.Opened += (_, _) => { try { KhoLanh.Core.TroLy.PerfMonitor.Mark(KhoLanh.Core.TroLy.PerfMonitor.MarkMoApp, (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds); } catch { } };
        }
        login.LoggedIn += () =>
        {
            var main = new MainWindow();
            Avalonia.Threading.DispatcherTimer.RunOnce(PublishLan, TimeSpan.FromSeconds(5));
            if (LanNote.Length > 0)
            {
                var note = LanNote; LanNote = "";
                Avalonia.Threading.DispatcherTimer.RunOnce(() => _ = Ui.Message(main, note + "\n\nVai mạng LAN do Quản trị gán theo tài khoản (Đồng bộ & sao lưu › Máy chủ)."), TimeSpan.FromMilliseconds(600));
            }
            main.LoggedOut += () => { ShowLogin(desktop, afterLogout: true); main.Close(); };
            main.Closed += (_, _) => { if (!main.IsLoggingOut) desktop.Shutdown(); };
            desktop.MainWindow = main;
            main.Show();
            if (login.Result is { Pending: not null } r) WatchAccounts(main, r);
            login.Close();
        };
        login.Closed += (_, _) => { if (!login.Succeeded) desktop.Shutdown(); };
        desktop.MainWindow = login;
        login.Show();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using KhoLanh.Core.Models;

namespace KhoLanh.App;

/// <summary>
/// 1.129 — Máy chủ chạy ẩn (người dùng 26/11: "chạy ẩn cho máy chủ để không thể tắt phần mềm mà ẩn đi").
/// Máy đang làm máy chủ: bấm ✕ / Alt+F4 → cửa sổ ẩn xuống khay hệ thống (cạnh đồng hồ), máy chủ vẫn chạy.
/// Bấm biểu tượng ở khay = mở lại; "Thoát hẳn" ở khay chỉ Quản trị, có hỏi lại. Tắt máy Windows / tự cập nhật / đăng xuất vẫn thoát như cũ.
/// Máy mở cùng Windows (--tu-khoi-dong) mà là máy chủ → tự ẩn ngay sau khi mở.
/// </summary>
public static class ChayAn
{
    /// <summary>Settings: "0" = tắt chạy ẩn (đóng cửa sổ = thoát như cũ); mặc định bật.</summary>
    public const string Key = "may_chu_chay_an";
    private const string KeyDaBao = "may_chu_chay_an_bao";

    public static bool ExitRequested { get; private set; }
    private static TrayIcon? _tray;
    private static MainWindow? _main;

    public static bool Enabled => App.Services?.Settings.Get(Key) != "0";
    public static bool IsServer => App.Services is { } s && (s.Lan.Serving || s.Settings.Get("lan_serving") == "1");
    /// <summary>Đang có biểu tượng ở khay (công cụ chụp tự kiểm).</summary>
    public static bool TrayShown => _tray is not null;

    /// <summary>Cửa sổ chính sắp đóng: true = ẩn xuống khay thay vì thoát.
    /// Chỉ khi người dùng tự bấm đóng (✕ / Alt+F4) — Close() trong mã (đăng xuất…), tắt app (cập nhật), tắt Windows đều thoát.</summary>
    public static bool ShouldHide(WindowCloseReason reason, bool programmatic, bool loggingOut, bool server, bool enabled, bool exitRequested)
        => server && enabled && !loggingOut && !exitRequested && !programmatic
           && reason is WindowCloseReason.WindowClosing or WindowCloseReason.Undefined;

    public static void Install(MainWindow w)
    {
        _main = w;
        w.Closing += (_, e) =>
        {
            if (!ShouldHide(e.CloseReason, e.IsProgrammatic, w.IsLoggingOut, IsServer, Enabled, ExitRequested)) return;
            e.Cancel = true;
            _ = HideToTray(w);
        };
        if (App.Services?.Lan is { } lan)
        {
            void Changed() => Dispatcher.UIThread.Post(Sync);
            lan.Changed += Changed;
            w.Closed += (_, _) => lan.Changed -= Changed;
        }
        w.Closed += (_, _) => { if (_main == w) { _main = null; RemoveTray(); RemoveWake(); } };
        Sync();
        if (WindowsStartup.LaunchedAtBoot && IsServer && Enabled)
            w.Opened += (_, _) => Dispatcher.UIThread.Post(() => { EnsureTray(); w.Hide(); });
    }

    /// <summary>Có biểu tượng khay khi máy là máy chủ; thôi làm máy chủ lúc đang ẩn → hiện lại cửa sổ.</summary>
    public static void Sync()
    {
        if (_main is null) return;
        if (IsServer && Enabled) { EnsureTray(); EnsureWake(); return; }
        if (!_main.IsVisible) ShowMain();
        RemoveTray(); RemoveWake();
    }

    public static async Task HideToTray(MainWindow w)
    {
        EnsureTray();
        if (App.Services is { } s && s.Settings.Get(KeyDaBao) != "1")
        {
            s.Settings.Set(KeyDaBao, "1");
            await Ui.Message(w, "Máy này đang làm MÁY CHỦ nên đóng cửa sổ chỉ ẨN phần mềm xuống khay hệ thống (góc phải thanh tác vụ, cạnh đồng hồ) — máy chủ vẫn chạy, các máy khác vẫn nối được.\n\n"
                + "• Mở lại: bấm biểu tượng Kho Lạnh ở khay (có thể nằm trong mũi tên ^).\n"
                + "• Thoát hẳn: bấm chuột phải biểu tượng › Thoát hẳn (chỉ Quản trị).\n"
                + "• Tắt chạy ẩn: Hệ thống › Đồng bộ & sao lưu › Máy chủ › ⋯ Thêm.", "Máy chủ chạy ẩn");
        }
        w.Hide();
    }

    public static void ShowMain()
    {
        var w = _main; if (w is null) return;
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Maximized;
        w.Activate();
    }

    public static async Task ExitAsync()
    {
        var w = _main; if (w is null) return;
        ShowMain();
        if (App.Services is not { } s) return;
        if (!Roles.IsAdmin(s.Session.Role))
        {
            await Ui.Message(w, "Chỉ tài khoản Quản trị được thoát hẳn phần mềm trên máy chủ (để các máy khác không mất máy chủ).", "Thoát hẳn");
            return;
        }
        if (!await Ui.Confirm(w, "Thoát hẳn Kho Lạnh trên máy chủ?\n\nCác máy khác sẽ mất máy chủ: máy dự phòng (nếu có) làm thay, không thì từng máy làm riêng và tự gửi phần sửa khi máy chủ mở lại.", "Thoát hẳn", "Thoát hẳn")) return;
        ExitRequested = true;
        RemoveTray();
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    // ---- mở KhoLanh.exe lần nữa trên máy chủ đang ẩn → hiện lại cửa sổ đó (không chạy bản thứ 2 tranh cổng máy chủ)
    private const string WakeName = @"Local\KhoLanh.MayChu.HienCuaSo";
    private static EventWaitHandle? _wake;

    /// <summary>Gọi đầu <c>Program.Main</c>: có máy chủ KhoLanh đang chạy trên máy này → báo nó hiện cửa sổ, trả true (bản mới thoát luôn).</summary>
    public static bool WakeExisting()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            if (!EventWaitHandle.TryOpenExisting(WakeName, out var h)) return false;
            using (h) h.Set();
            return true;
        }
        catch { return false; }
    }

    private static void EnsureWake()
    {
        if (_wake is not null || !OperatingSystem.IsWindows()) return;
        try
        {
            var h = new EventWaitHandle(false, EventResetMode.AutoReset, WakeName, out var created);
            if (!created) { h.Dispose(); return; }
            _wake = h;
            new Thread(() =>
            {
                try { while (h.WaitOne() && ReferenceEquals(_wake, h)) Dispatcher.UIThread.Post(ShowMain); }
                catch { /* đã đóng */ }
            }) { IsBackground = true, Name = "KhoLanh.ChayAn" }.Start();
        }
        catch { _wake = null; }
    }

    private static void RemoveWake()
    {
        var h = _wake; _wake = null;
        if (h is null) return;
        try { h.Set(); h.Dispose(); } catch { }
    }

    private static void EnsureTray()
    {
        if (_tray is not null || Application.Current is not { } app) return;
        try
        {
            var menu = new NativeMenu();
            var open = new NativeMenuItem("Mở Kho Lạnh"); open.Click += (_, _) => ShowMain(); menu.Items.Add(open);
            menu.Items.Add(new NativeMenuItemSeparator());
            var quit = new NativeMenuItem("Thoát hẳn (tắt máy chủ)…"); quit.Click += (_, _) => _ = ExitAsync(); menu.Items.Add(quit);
            _tray = new TrayIcon { ToolTipText = "Kho Lạnh — máy chủ đang chạy", Menu = menu, IsVisible = true };
            try { _tray.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://KhoLanh/Assets/app.ico"))); } catch { }
            _tray.Clicked += (_, _) => ShowMain();
            TrayIcon.SetIcons(app, new TrayIcons { _tray });
        }
        catch { _tray = null; }   // máy không có khay hệ thống → đóng cửa sổ vẫn ẩn, mở lại bằng cách chạy KhoLanh.exe lần nữa
    }

    private static void RemoveTray()
    {
        if (_tray is null) return;
        try { _tray.IsVisible = false; if (Application.Current is { } app) TrayIcon.SetIcons(app, new TrayIcons()); _tray.Dispose(); } catch { }
        _tray = null;
    }
}

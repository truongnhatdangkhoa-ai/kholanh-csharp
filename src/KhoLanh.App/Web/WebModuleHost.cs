using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;

namespace KhoLanh.App.Web;

/// <summary>
/// Khung trình duyệt WebView2 (Edge) nhúng trong cửa sổ Avalonia, chạy nguyên trang module gốc (ton-kho-an-an.html / ton-kho-gui.html).
/// Trang được phục vụ từ thư mục giải nén qua tên miền ảo https://kholanh.local/ (cùng gốc → localStorage / BroadcastChannel dùng chung giữa 2 module như app cũ).
/// window.dbAPI / window.authAPI do kl-bridge.js cấp; mọi lệnh chuyển sang App.Services.Bridge (SQLite).
/// </summary>
public class WebModuleHost : NativeControlHost
{
    public const string Host = "kholanh.local";
    private static CoreWebView2Environment? _env;
    private static Task<CoreWebView2Environment>? _envTask;
    // Mọi lệnh ghi / đọc từ 2 module chạy tuần tự theo đúng thứ tự gửi (như hàng đợi ghi queueWrite của app cũ).
    private static Task _chain = Task.CompletedTask;
    private static readonly object ChainGate = new();

    private readonly string _page;
    private IntPtr _hwnd;
    private CoreWebView2Controller? _ctl;

    /// <summary>Báo lỗi / trạng thái cho lớp bao (ModuleView) hiện ra.</summary>
    public event Action<string>? Status;
    public event Action? Ready;

    public WebModuleHost(string page) { _page = page; }

    public CoreWebView2? Core => _ctl?.CoreWebView2;

    /// <summary>Chạy JS trong trang, trả kết quả dạng JSON (dùng cho chế độ tự kiểm tra).</summary>
    public Task<string> EvalAsync(string js) => _ctl?.CoreWebView2.ExecuteScriptAsync(js) ?? Task.FromResult("null");

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows()) return base.CreateNativeControlCore(parent);
        _hwnd = Win32.CreateWindowExW(0, "Static", null, Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN | Win32.WS_CLIPSIBLINGS,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Dispatcher.UIThread.Post(async () => await InitAsync(), DispatcherPriority.Background);
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (!OperatingSystem.IsWindows()) { base.DestroyNativeControlCore(control); return; }
        try { _ctl?.Close(); } catch { /* bỏ qua */ }
        _ctl = null;
        if (_hwnd != IntPtr.Zero) { Win32.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }

    private static Task<CoreWebView2Environment> GetEnv()
    {
        if (_env is not null) return Task.FromResult(_env);
        return _envTask ??= Create();
        static async Task<CoreWebView2Environment> Create()
        {
            var userData = Path.Combine(App.DataFolder, "WebView2");
            Directory.CreateDirectory(userData);
            return _env = await CoreWebView2Environment.CreateAsync(null, userData);
        }
    }

    private async Task InitAsync()
    {
        try
        {
            var root = await Task.Run(() => WebAssets.EnsureExtracted(App.DataFolder));
            var env = await GetEnv();
            if (_hwnd == IntPtr.Zero) return; // đã bị hủy trong lúc chờ
            var ctl = await env.CreateCoreWebView2ControllerAsync(_hwnd);
            if (_hwnd == IntPtr.Zero) { ctl.Close(); return; }
            _ctl = ctl;
            var wv = ctl.CoreWebView2;
            wv.Settings.IsStatusBarEnabled = false;
            wv.Settings.AreDevToolsEnabled = true;           // F12 khi cần dò lỗi (như Electron cũ)
            wv.Settings.IsPasswordAutosaveEnabled = false;
            wv.Settings.IsGeneralAutofillEnabled = false;
            wv.SetVirtualHostNameToFolderMapping(Host, root, CoreWebView2HostResourceAccessKind.Allow);
            await wv.AddScriptToExecuteOnDocumentCreatedAsync(WebAssets.BridgeScript());
            wv.WebMessageReceived += OnWebMessage;
            wv.NavigationStarting += OnNavigationStarting;
            wv.NewWindowRequested += OnNewWindow;
            wv.DownloadStarting += OnDownloadStarting;
            wv.DocumentTitleChanged += (_, _) => { };
            wv.NavigationCompleted += (_, e) => { if (e.IsSuccess) Ready?.Invoke(); else Status?.Invoke("Không mở được trang module (" + e.WebErrorStatus + ")."); };
            UpdateBounds();
            ctl.IsVisible = IsEffectivelyVisible;
            wv.Navigate($"https://{Host}/html/{_page}");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Status?.Invoke("Máy chưa có Microsoft Edge WebView2 Runtime. Cài miễn phí tại https://go.microsoft.com/fwlink/p/?LinkId=2124703 rồi mở lại phần mềm.");
        }
        catch (Exception ex)
        {
            Status?.Invoke("Không khởi động được khung module: " + ex.Message);
        }
    }

    // ---------- kích thước / ẩn hiện ----------
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateBounds();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && _ctl is not null)
            _ctl.IsVisible = IsEffectivelyVisible;
    }

    public void SetActive(bool active)
    {
        if (_ctl is null) return;
        _ctl.IsVisible = active;
        if (active) { UpdateBounds(); try { _ctl.MoveFocus(CoreWebView2MoveFocusReason.Programmatic); } catch { } }
    }

    private void UpdateBounds()
    {
        if (_ctl is null) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        _ctl.Bounds = new System.Drawing.Rectangle(0, 0, Math.Max(1, (int)Math.Round(Bounds.Width * scale)), Math.Max(1, (int)Math.Round(Bounds.Height * scale)));
    }

    // ---------- cầu nối dữ liệu ----------
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string msg;
        try { msg = e.TryGetWebMessageAsString(); } catch { return; }
        long id; string req;
        try
        {
            using var doc = JsonDocument.Parse(msg);
            var r = doc.RootElement;
            if (!r.TryGetProperty("__klBridge", out _)) return;
            id = r.GetProperty("id").GetInt64();
            req = r.GetProperty("req").GetString() ?? "{}";
        }
        catch { return; }
        var wv = sender as CoreWebView2;
        lock (ChainGate)
        {
            _chain = _chain.ContinueWith(_ =>
            {
                var res = App.Services.Bridge.Handle(req);
                var reply = "{\"__klBridge\":1,\"id\":" + id + ",\"result\":" + res + "}";
                Dispatcher.UIThread.Post(() => { try { wv?.PostWebMessageAsString(reply); } catch { /* trang đã đóng */ } });
            }, TaskScheduler.Default);
        }
    }

    // ---------- điều hướng / cửa sổ mới / tải file ----------
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)) return;
        if (u.Scheme is "about" or "data" or "blob") return;
        if (u.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) && u.AbsolutePath.Equals("/html/" + _page, StringComparison.OrdinalIgnoreCase)) return;
        // Chỉ ở lại trang module của mình; trang khác của app cũ (trang chính, module khác) không có trong app mới.
        e.Cancel = true;
        if (u.Scheme is "http" or "https" && !u.Host.Equals(Host, StringComparison.OrdinalIgnoreCase)) Ui.OpenExternal(u.ToString());
    }

    private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && !u.Host.Equals(Host, StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            Ui.OpenExternal(u.ToString());
        }
        // Còn lại (xem trước khi in, blob:…) để WebView2 tự mở cửa sổ phụ như trình duyệt.
    }

    private async void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // Giống Electron cũ: mỗi file xuất ra hỏi nơi lưu (hộp thoại Lưu file).
        var deferral = e.GetDeferral();
        try
        {
            var top = TopLevel.GetTopLevel(this);
            var suggested = Path.GetFileName(e.ResultFilePath);
            if (top is null) { e.Cancel = true; return; }
            var ext = Path.GetExtension(suggested).TrimStart('.');
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Lưu file",
                SuggestedFileName = suggested,
                DefaultExtension = ext,
                SuggestedStartLocation = await top.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads),
                ShowOverwritePrompt = true,
                FileTypeChoices = string.IsNullOrEmpty(ext) ? null : new[] { new FilePickerFileType(ext.ToUpperInvariant()) { Patterns = new[] { "*." + ext } } },
            });
            var path = file?.TryGetLocalPath();
            if (path is null) { e.Cancel = true; return; }
            e.ResultFilePath = path;
            e.Handled = true; // không hiện bảng tải xuống của Edge
            e.DownloadOperation.StateChanged += (_, _) =>
            {
                if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed)
                    Dispatcher.UIThread.Post(() => MainWindow.Of(this)?.SetSubtitle($"Đã lưu {Path.GetFileName(path)} lúc {DateTime.Now:HH:mm}."));
            };
        }
        catch { e.Cancel = true; }
        finally { deferral.Complete(); }
    }

    private static class Win32
    {
        public const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(int exStyle, string className, string? windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hwnd);
    }
}

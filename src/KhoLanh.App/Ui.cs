using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using KhoLanh.Core.Services;

namespace KhoLanh.App;

/// <summary>Hộp thoại, chọn file, định dạng số dùng chung cho mọi màn hình.</summary>
public static class Ui
{
    /// <summary>Văn hóa định dạng của app: ngày kiểu Việt, SỐ kiểu 1,500.00 (1.71, <see cref="KhoLanh.Core.Grid.NumFmt"/>).</summary>
    public static readonly CultureInfo Vi = KhoLanh.Core.Grid.NumFmt.Culture;

    public static Window? Owner(Visual v) => TopLevel.GetTopLevel(v) as Window;

    public static string Num(double d) => d.ToString("#,0.##", Vi);

    /// <summary>Đọc số người dùng gõ — 1 kiểu duy nhất (1.71): "1,500.00" = một nghìn năm trăm, "1.5" = một phẩy năm; ô trống = 0.</summary>
    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        var s = (text ?? "").Trim().Replace(" ", "");
        if (s.Length == 0) return true;
        return KhoLanh.Core.Grid.NumFmt.TryParse(s, out value);
    }

    public static double ParseNumber(string? text, string field)
    {
        if (!TryParseNumber(text, out var v)) throw new BusinessException($"\"{text}\" ở ô {field} không phải là số.");
        return v;
    }

    /// <summary>Chạy một thao tác; lỗi nghiệp vụ hiện hộp thông báo thay vì làm sập app.</summary>
    public static async Task<bool> Run(Visual v, Action action)
    {
        try { action(); return true; }
        catch (BusinessException e) { await Message(v, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Message(v, Describe(e), "Lỗi"); }
        return false;
    }

    /// <summary>
    /// 1.72 (A2): chạy việc nặng (đọc / nạp file Excel lớn…) trên luồng nền + cửa sổ "Đang …" có thanh chạy và số giây, không đơ cửa sổ chính.
    /// <paramref name="cancellable"/> = có nút Hủy (chỉ dùng cho bước ĐỌC: bấm Hủy thì bỏ kết quả, không ghi gì). Ghi thời gian vào đo thao tác chậm.
    /// Trả (xong, kết quả); lỗi ném lại cho nơi gọi xử lý như cũ.
    /// </summary>
    public static async Task<(bool Done, T? Result)> Busy<T>(Visual v, string text, Func<T> work, bool cancellable = false, string op = "", string detail = "", Func<T, int>? rows = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var task = Task.Run(work);
        // việc xong nhanh (< 0,4 s) thì không hiện cửa sổ cho đỡ nháy
        if (await Task.WhenAny(task, Task.Delay(400)) == task) { var r0 = await task; Rec(r0); return (true, r0); }
        var msg = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        var sec = new TextBlock { Classes = { "muted" }, FontSize = 12 };
        var bar = new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 10, 0, 4) };
        var body = new StackPanel(); body.Children.Add(msg); body.Children.Add(bar); body.Children.Add(sec);
        var cancelled = new TaskCompletionSource<bool>();
        if (cancellable)
        {
            var cancel = new Button { Content = "Hủy", IsCancel = true };
            cancel.Click += (_, _) => cancelled.TrySetResult(true);
            body.Children.Add(Buttons(cancel));
        }
        var w = Dialog("Đang xử lý", body, 380);
        w.Closing += (_, e) => { if (!task.IsCompleted && !cancelled.Task.IsCompleted) { if (cancellable) cancelled.TrySetResult(true); else e.Cancel = true; } };
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => sec.Text = $"{sw.Elapsed.TotalSeconds:0} giây…";
        timer.Start();
        ApplyZoom(w);
        if (Owner(v) is { } owner) _ = w.ShowDialog(owner); else w.Show();
        try
        {
            var first = await Task.WhenAny(task, cancelled.Task);
            if (first != task) { _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default); return (false, default); }   // bỏ kết quả, việc nền tự kết thúc
            var r = await task; Rec(r); return (true, r);
        }
        finally { timer.Stop(); cancelled.TrySetResult(false); try { w.Close(); } catch { } }

        void Rec(T r) { if (op.Length > 0) KhoLanh.Core.TroLy.Perf.Record(op, detail, sw.ElapsedMilliseconds, rows is null || r is null ? 0 : rows(r)); }
    }
    public static Task<(bool Done, bool Result)> Busy(Visual v, string text, Action work, string op = "", string detail = "") =>
        Busy(v, text, () => { work(); return true; }, false, op, detail);

    /// <summary>1.72 (A2): "dấu vân tay" file (tên + cỡ + giờ sửa) — nạp lại đúng file đã nạp thì hỏi trước (bỏ qua cho nhanh).</summary>
    public static string FileStamp(string path) { try { var f = new FileInfo(path); return $"{f.Name}|{f.Length}|{f.LastWriteTimeUtc.Ticks}"; } catch { return ""; } }
    public static async Task<bool> ConfirmChangedFile(Visual v, string key, string path)
    {
        var st = App.Services.Settings.Get("nap_fp:" + key); var cur = FileStamp(path);
        if (cur.Length == 0 || st.Length == 0 || !st.StartsWith(cur + "|")) return true;
        var at = st[(cur.Length + 1)..];
        return await Confirm(v, $"File “{Path.GetFileName(path)}” đã nạp lúc {at} và CHƯA ĐỔI từ đó (cùng cỡ, cùng giờ sửa).\n\nVẫn nạp lại?", "File không đổi", "Nạp lại");
    }
    public static void RememberFile(string key, string path)
    {
        var cur = FileStamp(path); if (cur.Length == 0) return;
        try { App.Services.Settings.Set("nap_fp:" + key, cur + "|" + DateTime.Now.ToString("dd/MM/yyyy HH:mm")); } catch { }
    }

    public static async Task<bool> RunAsync(Visual v, Func<Task> action)
    {
        try { await action(); return true; }
        catch (BusinessException e) { await Message(v, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Message(v, Describe(e), "Lỗi"); }
        return false;
    }

    /// <summary>Nội dung lỗi luôn có chi tiết (kể cả khi lỗi không kèm lời nhắn) và ghi vào logs\loi.log để gửi cho người hỗ trợ.</summary>
    public static string Describe(Exception e)
    {
        var parts = new List<string>();
        for (Exception? x = e; x is not null && parts.Count < 4; x = x.InnerException)
        {
            var msg = string.IsNullOrWhiteSpace(x.Message) ? "(không có lời nhắn)" : x.Message.Trim();
            parts.Add($"{msg} [{x.GetType().Name}, 0x{x.HResult:X8}]");
        }
        string logPath = "";
        try
        {
            var dir = Path.Combine(App.DataFolder, "logs");
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "loi.log");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} bản {KhoLanh.Core.Services.UpdateService.Current}\n{e}\n\n");
        }
        catch { logPath = ""; }
        return "Có lỗi xảy ra: " + string.Join("\n→ ", parts) + (logPath.Length > 0 ? $"\n\nChi tiết đã ghi vào {logPath}" : "");
    }

    public static Window Dialog(string title, Control body, double width = 440) => new()
    {
        Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        Background = AppTheme.Card,
        Content = new Border { Padding = new Thickness(22, 18), Child = body },
    };

    public static StackPanel Buttons(params Button[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    public static async Task Message(Visual v, string text, string title = "Thông báo")
    {
        var ok = new Button { Content = "Đóng", IsDefault = true, IsCancel = true, Classes = { "primary" } };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Buttons(ok));
        var w = Dialog(title, body);
        ok.Click += (_, _) => w.Close();
        await Show(v, w);
    }

    public static async Task<bool> Confirm(Visual v, string text, string title = "Xác nhận", string yes = "Đồng ý")
    {
        var result = false;
        var y = new Button { Content = yes, IsDefault = true, Classes = { "primary" } };
        var n = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Buttons(n, y));
        var w = Dialog(title, body);
        y.Click += (_, _) => { result = true; w.Close(); };
        n.Click += (_, _) => w.Close();
        await Show(v, w);
        return result;
    }

    /// <summary>Hỏi với nhiều lựa chọn (mỗi lựa chọn 1 nút xếp dọc, nút đầu là đề xuất). Trả chỉ số đã chọn, -1 = Đóng.</summary>
    public static async Task<int> Pick(Visual v, string text, string title, IList<string> options, double width = 520)
    {
        var result = -1;
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        Window? w = null;
        for (var i = 0; i < options.Count; i++)
        {
            var k = i;
            var b = new Button { Content = new TextBlock { Text = options[i], TextWrapping = TextWrapping.Wrap }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            if (i == 0) b.Classes.Add("primary");
            b.Click += (_, _) => { result = k; w?.Close(); };
            body.Children.Add(b);
        }
        var n = new Button { Content = "Đóng", IsCancel = true };
        body.Children.Add(Buttons(n));
        w = Dialog(title, body, width);
        n.Click += (_, _) => w.Close();
        await Show(v, w);
        return result;
    }

    public static async Task<string?> Prompt(Visual v, string text, string title, string initial = "", bool password = false)
    {
        string? result = null;
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 0) };
        if (password) box.PasswordChar = '•';
        var y = new Button { Content = "Đồng ý", IsDefault = true, Classes = { "primary" } };
        var n = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(box);
        body.Children.Add(Buttons(n, y));
        var w = Dialog(title, body);
        y.Click += (_, _) => { result = box.Text ?? ""; w.Close(); };
        n.Click += (_, _) => w.Close();
        w.Opened += (_, _) => box.Focus();
        await Show(v, w);
        return result;
    }

    /// <summary>Chọn một mục trong danh sách (kèm ô nhập chữ nếu có <paramref name="inputLabel"/>). Null = bấm Đóng.</summary>
    public static async Task<(int Index, string Text)?> Choose(Visual v, string text, string title, IList<string> items, int selected = 0,
        string? inputLabel = null, string inputInitial = "", string yes = "Đồng ý")
    {
        (int, string)? result = null;
        var list = new ComboBox { ItemsSource = items, SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, items.Count - 1)), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Margin = new Thickness(0, 10, 0, 0) };
        var box = new TextBox { Text = inputInitial, Margin = new Thickness(0, 4, 0, 0) };
        var y = new Button { Content = yes, IsDefault = true, Classes = { "primary" } };
        var n = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(list);
        if (inputLabel is not null)
        {
            body.Children.Add(new TextBlock { Text = inputLabel, Margin = new Thickness(0, 10, 0, 0), Classes = { "label" } });
            body.Children.Add(box);
        }
        body.Children.Add(Buttons(n, y));
        var w = Dialog(title, body);
        y.Click += (_, _) => { result = (list.SelectedIndex, box.Text ?? ""); w.Close(); };
        n.Click += (_, _) => w.Close();
        w.Opened += (_, _) => { if (inputLabel is not null) box.Focus(); else list.Focus(); };
        await Show(v, w);
        return result;
    }

    /// <summary>Cho hộp thoại theo cỡ chữ đang chọn (Giao diện &amp; cỡ chữ): bọc nội dung trong LayoutTransformControl, nới kích thước cửa sổ.</summary>
    public static void ApplyZoom(Window w)
    {
        var sc = AppTheme.Scale; if (Math.Abs(sc - 1) < 0.001 || w.Content is LayoutTransformControl || w.Content is not Control c) return;
        w.Content = null;
        w.Content = new LayoutTransformControl { Child = c, LayoutTransform = new Avalonia.Media.ScaleTransform(sc, sc) };
        if (!double.IsNaN(w.Width)) w.Width *= sc;
        if (!double.IsNaN(w.Height)) w.Height *= sc;
        if (w.MinWidth > 0) w.MinWidth *= sc;
        if (w.MinHeight > 0) w.MinHeight *= sc;
    }

    public static async Task Show(Visual v, Window w)
    {
        ApplyZoom(w);
        AllowResize(w);
        if (Owner(v) is { } owner) await w.ShowDialog(owner);
        else { var tcs = new TaskCompletionSource(); w.Closed += (_, _) => tcs.TrySetResult(); w.Show(); await tcs.Task; }
    }

    /// <summary>1.62: hộp thoại có bảng dữ liệu (nút ⤢) / ảnh / vùng cuộn thì KHÔNG khóa cỡ: kéo giãn + phóng to được;
    /// phần nội dung lớn nhất (bảng / vùng cuộn có chiều cao cố định) giãn theo cửa sổ, nút giữ ở đáy.</summary>
    public static void AllowResize(Window w)
    {
        if (w.CanResize || w.Content is not Control) return;
        w.Opened += (_, _) =>
        {
            if (w.Content is not Control root) return;
            var all = root.GetVisualDescendants().OfType<Control>().ToList();
            if (!all.Any(c => c is Controls.SheetGrid or DataGrid or Image || c is ScrollViewer { MaxHeight: < double.PositiveInfinity })) return;
            // phần giãn: bảng có Height cố định, hoặc vùng cuộn có MaxHeight — lấy cái cao nhất
            Control? grow = all.Where(c => c is Controls.SheetGrid or DataGrid && !double.IsNaN(c.Height))
                .Concat(all.Where(c => c is ScrollViewer && c.MaxHeight < double.PositiveInfinity))
                .OrderByDescending(c => c.Bounds.Height).FirstOrDefault();
            w.CanResize = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var h0 = w.ClientSize.Height; var sc = AppTheme.Scale <= 0 ? 1 : AppTheme.Scale;
                if (w.SizeToContent != SizeToContent.Manual) { w.Height = h0; w.SizeToContent = SizeToContent.Manual; }
                if (w.MinHeight <= 0) w.MinHeight = Math.Min(h0, 240);
                if (w.MinWidth <= 0) w.MinWidth = Math.Min(w.Bounds.Width, 360);
                if (grow is null) return;
                var isMax = grow is ScrollViewer; var g0 = isMax ? grow.MaxHeight : grow.Height;
                w.GetObservable(Window.ClientSizeProperty).Subscribe(new Watch(sz =>
                {
                    var v2 = Math.Max(Math.Min(g0, 120), g0 + (sz.Height - h0) / sc);
                    if (isMax) grow.MaxHeight = v2; else grow.Height = v2;
                }));
            }, Avalonia.Threading.DispatcherPriority.Background);
        };
    }

    private sealed class Watch(Action<Size> on) : IObserver<Size>
    {
        public void OnNext(Size v) => on(v);
        public void OnError(Exception e) { }
        public void OnCompleted() { }
    }

    /// <summary>Ảnh xem trước của RenderTargetBitmap độ nét ×2: chép sang Bitmap thường (ảnh 192 dpi vẽ thẳng bị cắt còn 1/4 góc trên trái
    /// trên Windows), hiện rộng tối đa <paramref name="maxWidth"/>, co theo cửa sổ.</summary>
    public static Control ImagePreview(Avalonia.Media.Imaging.Bitmap bmp, double maxWidth = 860, double maxHeight = 560)
    {
        Avalonia.Media.Imaging.Bitmap src = bmp;
        if (bmp is Avalonia.Media.Imaging.RenderTargetBitmap)
        {
            using var ms = new MemoryStream(); bmp.Save(ms); ms.Position = 0;
            src = new Avalonia.Media.Imaging.Bitmap(ms);
        }
        var natural = src.PixelSize.Width / 2.0;   // ảnh vẽ ×2 → cỡ thật
        return new ScrollViewer
        {
            MaxHeight = maxHeight, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border
            {
                BorderBrush = AppTheme.Brush("#D3D3D3"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = Math.Min(natural, maxWidth) + 2,
                Child = new Image { Source = src, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
            },
        };
    }

    /// <summary>Độ rộng hộp thoại vừa ảnh xem trước.</summary>
    public static double ImageDialogWidth(Avalonia.Media.Imaging.Bitmap bmp, double maxWidth = 860) => Math.Max(420, Math.Min(bmp.PixelSize.Width / 2.0, maxWidth) + 70);

    public static async Task<string?> SaveFile(Visual v, string title, string suggestedName, string ext, string typeName)
    {
        var top = TopLevel.GetTopLevel(v);
        if (top is null) return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedName, DefaultExtension = ext, ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType(typeName) { Patterns = ext.Split(';').Select(e => "*." + e).ToArray() } },
        });
        return file?.TryGetLocalPath();
    }

    public static async Task<string?> OpenFile(Visual v, string title, string ext, string typeName)
    {
        var top = TopLevel.GetTopLevel(v);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = ext.Split(';').Select(e => "*." + e).ToArray() } },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> OpenFolder(Visual v, string title)
    {
        var top = TopLevel.GetTopLevel(v);
        if (top is null) return null;
        var dirs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return dirs.Count > 0 ? dirs[0].TryGetLocalPath() : null;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TopLevel, Avalonia.Controls.Notifications.WindowNotificationManager> Notifs = new();
    /// <summary>Lần báo "đã lưu" gần nhất (công cụ chụp kiểm tra).</summary>
    internal static string? LastExported;

    /// <summary>
    /// 1.99: xuất file (Excel / ảnh / PDF / thư mục) xong KHÔNG tự mở nữa (người dùng: "mở file cũ bị chạy vì đã sắp xếp, người dùng tự mở") —
    /// chỉ hiện thông báo nhỏ góc phải dưới "Đã lưu …", tự ẩn sau 10 giây; bấm vào thì mở thư mục chứa file (chọn sẵn file).
    /// Nút "Mở file" / "Mở thư mục" người dùng tự bấm và trang In (trình duyệt) vẫn dùng <see cref="OpenExternal"/>.
    /// </summary>
    public static void Exported(Visual? v, string path)
    {
        LastExported = path;
        var dir = Directory.Exists(path);
        Notify(v, dir ? "Đã xuất vào thư mục" : "Đã lưu file", (dir ? path : Path.GetFileName(path)) + "\nBấm vào đây để mở thư mục chứa file.",
            Avalonia.Controls.Notifications.NotificationType.Success, () => ShowInFolder(path));
    }

    /// <summary>Lần thông báo gần nhất (công cụ chụp / test kiểm).</summary>
    public static string LastNotify { get; private set; } = "";

    /// <summary>1.102: thông báo góc phải dưới (không chặn thao tác), bấm vào → <paramref name="onClick"/>.</summary>
    public static void Notify(Visual? v, string title, string msg, Avalonia.Controls.Notifications.NotificationType type, Action? onClick = null, int seconds = 10)
    {
        LastNotify = title + " | " + msg;
        try
        {
            var top = (v is null ? null : TopLevel.GetTopLevel(v)) is { } t && t.IsVisible ? t
                : (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (top is null) return;
            var fresh = false;
            var mgr = Notifs.GetValue(top, x => { fresh = true; return new Avalonia.Controls.Notifications.WindowNotificationManager(x) { Position = Avalonia.Controls.Notifications.NotificationPosition.BottomRight, MaxItems = 3 }; });
            var note = new Avalonia.Controls.Notifications.Notification(title, msg, type, TimeSpan.FromSeconds(seconds), onClick);
            // khung thông báo vừa tạo chưa dựng xong (chưa có template) thì Show bỏ qua → chờ dựng xong mới hiện
            if (fresh) mgr.TemplateApplied += (_, _) => mgr.Show(note);
            else mgr.Show(note);
        }
        catch { /* thông báo lỗi thì thôi */ }
    }

    /// <summary>Mở thư mục chứa file (Windows: chọn sẵn file); là thư mục thì mở chính nó.</summary>
    public static void ShowInFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) { OpenExternal(path); return; }
            if (OperatingSystem.IsWindows() && File.Exists(path)) { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); return; }
            if (Path.GetDirectoryName(path) is { Length: > 0 } d) OpenExternal(d);
        }
        catch { }
    }

    /// <summary>Mở file / thư mục bằng chương trình mặc định của Windows.</summary>
    public static void OpenExternal(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* không có chương trình mở: bỏ qua */ }
    }

    /// <summary>"In trực tiếp": chạy hàm xuất Excel ra file tạm, chuyển thành trang in A4 rồi mở bằng trình duyệt (In / Lưu PDF). Dùng lại đúng hàm xuất Excel nên in ra đúng như bản Excel.</summary>
    public static async Task<bool> PrintVia(Visual v, string title, Action<string> exportToXlsx)
    {
        var dir = Path.Combine(App.DataFolder, "in"); Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, "tam-" + Guid.NewGuid().ToString("N")[..8] + ".xlsx");
        string html = "";
        var ok = await Run(v, () => { try { exportToXlsx(tmp); html = XlsxHtml.WritePage(tmp, title, dir); } finally { try { File.Delete(tmp); } catch { } } });
        if (ok) OpenExternal(html);
        return ok;
    }

    /// <summary>
    /// Khung cảnh báo đối soát (1.69 — Tồn kho An An / Tồn kho gửi): có lệch → khung nền nhạt đỏ (lỗi) / vàng (cần kiểm tra), mỗi cảnh báo 1 dòng;
    /// không lệch → nhãn xanh "khớp". Chữ không mờ để <c>HintTrim</c> không ẩn.
    /// </summary>
    public static Control CanhBaoBox(IReadOnlyList<KhoLanh.Core.TonKho.CanhBao> list, string okText)
    {
        if (list.Count == 0)
            return new Border { Classes = { "pill", "ok" }, Margin = new Thickness(0, 6, 0, 6), Padding = new Thickness(10, 3), Child = new TextBlock { Text = "✓ " + okText, FontSize = 12.5 } };
        var loi = list.Any(x => x.Loi);
        var fg = AppTheme.Brush(loi ? "#B42318" : "#9A5B00");
        var sp = new StackPanel { Spacing = 3 };
        sp.Children.Add(new TextBlock { Text = loi ? "⚠ CÓ SAI LỆCH SỐ LIỆU — kiểm tra trước khi báo cáo" : "⚠ Cần kiểm tra số liệu", FontWeight = FontWeight.Bold, FontSize = 13.5, Foreground = fg });
        foreach (var c in list)
            sp.Children.Add(new TextBlock { Text = "• " + c.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Brush(c.Loi ? "#B42318" : "#9A5B00"), FontWeight = c.Loi ? FontWeight.SemiBold : FontWeight.Normal });
        return new Border
        {
            Background = AppTheme.Brush(loi ? "#FDE8E6" : "#FFF2D6"), BorderBrush = fg, BorderThickness = new Thickness(1, 1, 1, 1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 7), Margin = new Thickness(0, 6, 0, 6), Child = sp,
        };
    }

    /// <summary>
    /// Tab con trong 1 trang (1.57 — màn hình nhỏ): mỗi bảng dữ liệu 1 tab thay vì 2 – 4 bảng chen nhau trên cùng màn hình.
    /// Nội dung tab được bọc khoảng cách trên 6 px; trả về TabControl để nơi dùng chuyển tab bằng code (vd chọn dòng → mở tab chi tiết).
    /// </summary>
    public static TabControl SubTabs(params (string Header, Control Content)[] tabs)
    {
        var tc = new TabControl();
        foreach (var (h, c) in tabs) tc.Items.Add(new TabItem { Header = h, Content = new Border { Padding = new Thickness(0, 6, 0, 0), Child = c } });
        // Không để đổi tab con nổi lên TabControl cha (cha thường vẽ lại cả trang khi đổi tab → mất dòng đang chọn).
        tc.SelectionChanged += (_, e) => e.Handled = true;
        return tc;
    }

    /// <summary>Danh sách + chi tiết thành 2 tab con (màn hình nhỏ không phải xem 2 bảng chồng nhau):
    /// bấm đúp ở danh sách → mở tab chi tiết; <paramref name="info"/> (nếu có) nằm trên bảng chi tiết.</summary>
    public static TabControl MasterDetail(string listHeader, Control list, Control listGrid, string detHeader, Control detail, Control? info = null)
    {
        Control det = detail;
        if (info is not null)
        {
            var dp = new DockPanel();
            info.Margin = new Thickness(0, 0, 0, 6);
            DockPanel.SetDock(info, Dock.Top);
            dp.Children.Add(info); dp.Children.Add(detail);
            det = dp;
        }
        var tc = SubTabs((listHeader, list), (detHeader, det));
        listGrid.DoubleTapped += (_, _) => tc.SelectedIndex = 1;
        return tc;
    }

    public static string SafeFileName(string s) =>
        string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.Core.PhoneSync;

namespace KhoLanh.App.Views;

/// <summary>
/// ĐỒNG BỘ ĐIỆN THOẠI (Cài đặt › Đồng bộ điện thoại của app cũ): kết nối Web App, chọn bảng đẩy lên, đẩy tồn / nhận phiếu / đồng bộ ngay,
/// trạng thái từng bảng (lần đẩy, số dòng, có đổi dữ liệu, lỗi), tự đồng bộ theo chu kỳ. Hạ tầng dùng chung cho bản mobile (docs/DONG-BO-DIEN-THOAI.md).
/// </summary>
public sealed class PhoneSyncView : UserControl, IPage
{
    public string Title => "Đồng bộ điện thoại";
    public string Subtitle => "Đẩy tồn lên Google Sheet cho điện thoại, nhận phiếu điện thoại về đúng module.";

    private static PhoneSyncService Svc => App.Services.PhoneSync;
    private static IBrush B(string c) => AppTheme.Brush(c);
    private readonly Border _connPill = new() { Classes = { "pill" }, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock() };
    private readonly Button _connBtn = new() { Content = "Kết nối…", Classes = { "ghost" } };
    private readonly StackPanel _tables = new() { Spacing = 4 };
    private readonly TextBlock _log = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _auto = new() { ItemsSource = PhoneAutoSync.Choices.Select(c => c.Label).ToList(), MinWidth = 160 };
    private readonly List<Button> _acts = new();

    public PhoneSyncView()
    {
        // 1.105: nằm trong Hệ thống › Đồng bộ & sao lưu › Điện thoại. 1 hàng: trạng thái kết nối (đổi ở Hệ thống › Kết nối) · tự đồng bộ · nút chạy (nút chính cuối).
        _auto.SelectionChanged += (_, _) => { if (_auto.SelectedIndex >= 0) PhoneAutoSync.Minutes = PhoneAutoSync.Choices[_auto.SelectedIndex].Min; };
        // -1 = "Ngay khi có thay đổi": đẩy bảng vừa đổi sau ~25 giây yên lặng (nền), vẫn đồng bộ đủ mỗi 5 phút làm dự phòng.
        var autoLbl = new TextBlock { Text = "Tự đồng bộ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        ToolTip.SetTip(autoLbl, "Chỉ chạy khi app đang mở và đã đăng nhập trên máy này.\n\"Ngay khi có thay đổi\": gom các sửa trong ~25 giây rồi chỉ đẩy bảng vừa đổi (chạy nền, không chặn); máy con LAN không đẩy; vẫn đồng bộ đủ mỗi 5 phút làm dự phòng.");
        var row = new KhoLanh.App.Controls.ToolRow { Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(_connPill);
        row.Children.Add(_connBtn);
        row.Children.Add(autoLbl); row.Children.Add(_auto);
        row.Children.Add(KhoLanh.App.Controls.ToolRow.Gap());
        row.Children.Add(Act("⬆ Chỉ đẩy tồn", "Đẩy tồn các bảng đã chọn lên Google Sheet", false, async () => PushText(await Svc.PushAsync())));
        row.Children.Add(Act("⬇ Chỉ nhận phiếu", "Kéo phiếu xuất điện thoại tạo (chưa xử lý) về đúng module, trừ tồn, đánh dấu Đã xử lý", false, async () => PullText(await Svc.PullAsync())));
        row.Children.Add(Act("⇅ Đồng bộ ngay", "Nhận phiếu điện thoại + yêu cầu lịch xuất về, lưu trữ phiếu cũ (1 lần / ngày), rồi đẩy tồn các bảng đã chọn (và bảng vừa nhận phiếu)", true, async () =>
        { var (p, push) = await Svc.SyncAsync(); return PullText(p) + "\n" + PushText(push); }));
        _connBtn.Click += (_, _) => MainWindow.Of(this)?.Navigate("ketnoi");

        var tTitle = new TextBlock { Text = "Bảng đẩy lên điện thoại", Classes = { "section" } };
        ToolTip.SetTip(tTitle, "Tick bảng cần đẩy. Chỉ hiện bảng của module tài khoản được xem. Kèm theo: phiếu xuất tạo trên PC; Tồn kho An An / Tồn kho gửi đẩy luôn báo cáo Excel + Phân loại; Tổng hợp đẩy Kết quả kháng sinh (chỉ phần mới).");
        var left = new Border { Classes = { "card" }, VerticalAlignment = VerticalAlignment.Top, Child = new StackPanel { Spacing = 8, Children = { tTitle, _tables } } };
        var right = new Border { Classes = { "card" }, VerticalAlignment = VerticalAlignment.Top, Child = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "Kết quả lần chạy", Classes = { "section" } }, _log } } };
        _log.Tag = "keep";
        var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 14 };
        cols.Children.Add(left); Grid.SetColumn(right, 1); cols.Children.Add(right);
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 6, 0) };
        body.Children.Add(row); body.Children.Add(cols);
        Content = new ScrollViewer { Content = body };
        PhoneAutoSync.Ran += OnAutoRan;
        DetachedFromVisualTree += (_, _) => PhoneAutoSync.Ran -= OnAutoRan;
        Paint();
    }

    private void OnAutoRan(string text) => Dispatcher.UIThread.Post(() => { _log.Text = $"[Tự đồng bộ {DateTime.Now:HH:mm}] " + text; Paint(); });

    private static Button Btn(string t, Func<Task> click) { var b = new Button { Content = t, Padding = new Thickness(10, 4), FontSize = 12.5 }; b.Click += async (_, _) => await click(); return b; }
    private Button Act(string t, string tip, bool primary, Func<Task<string>> work)
    {
        var b = new Button { Content = t, Padding = new Thickness(12, 5) }; if (primary) b.Classes.Add("primary"); ToolTip.SetTip(b, tip);
        b.Click += async (_, _) => await Run("Đang chạy…", work); _acts.Add(b); return b;
    }

    private async Task Run(string busy, Func<Task<string>> work)
    {
        if (!App.Services.Phone.Enabled) { await PhoneConnect.ShowAsync(this); Paint(); if (!App.Services.Phone.Enabled) return; }
        foreach (var b in _acts) b.IsEnabled = false;
        _log.Foreground = AppTheme.Muted; _log.Text = busy;
        try { var t = await PhoneAutoSync.Exclusive(work); _log.Foreground = AppTheme.Text; _log.Text = t; }
        catch (Exception e) { _log.Foreground = AppTheme.Danger; _log.Text = "⛔ " + e.Message; }
        finally { foreach (var b in _acts) b.IsEnabled = true; Paint(); }
    }

    public static string PullText(PhoneSyncService.PullResult p) =>
        (p.NewPhieu == 0 ? "Không có phiếu mới từ điện thoại." : $"Nhận {p.NewPhieu} phiếu: " + string.Join(", ", p.ByModule.Where(kv => kv.Value > 0).Select(kv => $"{Name(kv.Key)} {kv.Value}")))
        + (p.ErrPhieu > 0 ? $" · {p.ErrPhieu} phiếu lỗi (không có dòng hàng)" : "")
        + (p.Errors.Count > 0 ? "\nChưa nhận được (giữ \"Chưa xử lý\"):\n• " + string.Join("\n• ", p.Errors) : "");
    public static string PushText(PhoneSyncService.PushResult r) =>
        (r.Tables.Count == 0 ? "Chưa chọn bảng nào để đẩy." : "Đã đẩy: " + string.Join(" · ", r.Rows.Select(kv => $"{Name(kv.Key)} {kv.Value:#,0} dòng")))
        + (r.Errors.Count > 0 ? "\nLỗi: " + string.Join("; ", r.Errors) : "");
    private static string Name(string code) => PhoneSyncService.Tables.FirstOrDefault(t => t.Code == code).Name ?? code;

    private void Paint()
    {
        var ph = App.Services.Phone;
        var err = ph.Enabled ? ph.LastError : null;
        ((TextBlock)_connPill.Child!).Text = !ph.Enabled ? "○ Chưa nối điện thoại" : err is not null ? "● Lỗi kết nối" : "● Đã kết nối Google Sheet chung";
        _connPill.Classes.Remove("ok"); _connPill.Classes.Remove("warn"); _connPill.Classes.Remove("bad");
        _connPill.Classes.Add(!ph.Enabled ? "warn" : err is not null ? "bad" : "ok");
        ToolTip.SetTip(_connPill, ph.Enabled ? Short(ph.Url) + (App.Services.PhoneSync.LastSlipPush is { Length: > 0 } lp ? "\n" + lp : "") + (err is not null ? $"\nLỗi lúc {err.At:HH:mm dd/MM}: {err.Msg}" : "") : "Kết nối ở Hệ thống › Kết nối (dán mã kết nối máy, hoặc mã PC_TOKEN).");
        _connBtn.Content = ph.Enabled ? "Đổi ở Kết nối" : "Kết nối…";
        var i = Array.FindIndex(PhoneAutoSync.Choices, c => c.Min == PhoneAutoSync.Minutes); _auto.SelectedIndex = Math.Max(0, i);
        _tables.Children.Clear();
        var pick = Svc.Pick; var st = Svc.Status;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        var row = 0;
        foreach (var (code, name, _) in PhoneSyncService.Tables)
        {
            if (!Svc.CanTable(code)) continue;
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cb = new CheckBox { Content = name, IsChecked = pick.Contains(code), MinWidth = 280, Margin = new Thickness(0, 1) };
            var c = code; cb.IsCheckedChanged += (_, _) => { var p = Svc.Pick; if (cb.IsChecked == true) p.Add(c); else p.Remove(c); Svc.Pick = p; };
            Grid.SetRow(cb, row); g.Children.Add(cb);
            var tag = new TextBlock { Text = code, FontSize = 11, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0) }; Grid.SetRow(tag, row); Grid.SetColumn(tag, 1); g.Children.Add(tag);
            var s = st.GetValueOrDefault(code);
            var txt = s is null ? "Chưa đẩy lần nào" : s.Error.Length > 0 ? $"⛔ {When(s.At)}: {s.Error}" : $"Đẩy {When(s.At)} · {s.Rows:#,0} dòng · " + (s.Changed ? "có dữ liệu mới" : "không đổi" + (s.ChangedAt.Length > 0 ? $" (đổi lần cuối {When(s.ChangedAt)})" : ""));
            var t = new TextBlock { Text = txt, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = s is { Error.Length: > 0 } ? AppTheme.Danger : AppTheme.Muted, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(t, row); Grid.SetColumn(t, 2); g.Children.Add(t); row++;
        }
        if (row == 0) _tables.Children.Add(new TextBlock { Text = "Tài khoản chưa được xem module nào có bảng điện thoại.", Classes = { "muted" } }); else _tables.Children.Add(g);
    }
    private static string Short(string u) => u.Length > 60 ? u[..40] + "…" + u[^14..] : u;
    private static string When(string iso) => DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime().ToString("HH:mm dd/MM") : iso;
}

/// <summary>Tự đồng bộ điện thoại theo chu kỳ (mặc định tắt — giống app cũ); chỉ 1 lần chạy cùng lúc.</summary>
public static class PhoneAutoSync
{
    public static readonly (int Min, string Label)[] Choices = { (0, "Tắt"), (-1, "Ngay khi có thay đổi"), (5, "Mỗi 5 phút"), (10, "Mỗi 10 phút"), (15, "Mỗi 15 phút"), (30, "Mỗi 30 phút"), (60, "Mỗi 60 phút") };
    private const string Key = "phone_sync_interval";
    private static readonly SemaphoreSlim Gate = PhoneSyncService.Gate;
    private static DispatcherTimer? _timer; private static DateTime _last = DateTime.MinValue;
    public static event Action<string>? Ran;
    public static int Minutes
    {
        get => App.Services.PhoneSync.InstantOn ? -1 : int.TryParse(App.Services.Settings.Get(Key, "0"), out var m) ? Math.Max(0, m) : 0;
        set
        {
            App.Services.PhoneSync.InstantOn = value < 0;
            App.Services.Settings.Set(Key, (value < 0 ? 5 : value).ToString(CultureInfo.InvariantCulture));   // đẩy ngay: vẫn chạy 5 phút làm dự phòng
        }
    }
    public static async Task<T> Exclusive<T>(Func<Task<T>> f)
    {
        if (!await Gate.WaitAsync(0)) throw new InvalidOperationException("Đang có một lần đồng bộ điện thoại chạy — chờ xong rồi thử lại.");
        try { return await f(); } finally { Gate.Release(); }
    }
    /// <summary>Gọi 1 lần sau khi đăng nhập (MainWindow).</summary>
    public static void Start()
    {
        _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, async (_, _) => await Tick());
        _timer.Start();
    }
    private static async Task Tick()
    {
        var m = Minutes; if (m < 0) m = 5; if (m <= 0 || !App.Services.Phone.Enabled || App.Services.Session.CurrentUser is null) return;
        if ((DateTime.Now - _last).TotalMinutes < m) return;
        _last = DateTime.Now;
        try
        {
            var text = await Exclusive(async () => { var (p, push) = await Task.Run(() => App.Services.PhoneSync.SyncAsync()); return PhoneSyncView.PullText(p) + " " + PhoneSyncView.PushText(push); });
            Ran?.Invoke(text);
        }
        catch (Exception e) { Ran?.Invoke("⛔ " + e.Message); }
    }
}

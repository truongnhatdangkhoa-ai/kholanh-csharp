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
    public string Subtitle => "Đẩy tồn các module lên Google Sheet cho điện thoại xem / xuất, nhận phiếu xuất điện thoại tạo về đúng module.";

    private static PhoneSyncService Svc => App.Services.PhoneSync;
    private static IBrush B(string c) => AppTheme.Brush(c);
    private readonly TextBlock _conn = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _tables = new() { Spacing = 4 };
    private readonly TextBlock _log = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _auto = new() { ItemsSource = PhoneAutoSync.Choices.Select(c => c.Label).ToList(), MinWidth = 160 };
    private readonly List<Button> _acts = new();

    public PhoneSyncView()
    {
        var top = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        top.Children.Add(_conn);
        top.Children.Add(Btn("Kết nối điện thoại…", async () => { await PhoneConnect.ShowAsync(this); Paint(); }));
        top.Children.Add(Btn("Kiểm tra kết nối", async () => await Run("Đang kiểm tra…", async () => { var (t, sh) = await App.Services.Phone.PingAsync(); return $"✔ Kết nối được Google Sheet \"{t}\" ({sh.Count} tab)."; })));

        var acts = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 10, 0, 0) };
        acts.Children.Add(Act("⇅ Đồng bộ ngay", "Nhận phiếu điện thoại về các module rồi đẩy tồn các bảng đã chọn (và bảng vừa nhận phiếu)", true, async () =>
        { var (p, push) = await Svc.SyncAsync(); return PullText(p) + "\n" + PushText(push); }));
        acts.Children.Add(Act("⬆ Chỉ đẩy tồn", "Đẩy tồn các bảng đã chọn lên Google Sheet", false, async () => PushText(await Svc.PushAsync())));
        acts.Children.Add(Act("⬇ Chỉ nhận phiếu", "Kéo phiếu xuất điện thoại tạo (chưa xử lý) về đúng module, trừ tồn, đánh dấu Đã xử lý", false, async () => PullText(await Svc.PullAsync())));
        _auto.SelectionChanged += (_, _) => { if (_auto.SelectedIndex >= 0) PhoneAutoSync.Minutes = PhoneAutoSync.Choices[_auto.SelectedIndex].Min; };
        var autoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        autoRow.Children.Add(new TextBlock { Text = "Tự đồng bộ trên máy này:", VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 }); autoRow.Children.Add(_auto);
        autoRow.Children.Add(new TextBlock { Text = "(chỉ chạy khi app đang mở và đã đăng nhập)", Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

        var body = new StackPanel { Spacing = 6, MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(top); body.Children.Add(acts); body.Children.Add(autoRow);
        body.Children.Add(new TextBlock { Text = "Bảng đẩy lên điện thoại", FontWeight = FontWeight.Bold, FontSize = 14, Margin = new Thickness(0, 12, 0, 2) });
        body.Children.Add(new TextBlock { Text = "Tick bảng cần đẩy khi bấm Đẩy tồn / Đồng bộ ngay. Chỉ hiện bảng của module tài khoản được xem. Phiếu xuất tạo trên PC được đẩy kèm (điện thoại chỉ xem).", Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new Border { BorderBrush = B("#D0D7E2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8), Child = _tables });
        body.Children.Add(new TextBlock { Text = "Kết quả lần chạy", FontWeight = FontWeight.Bold, FontSize = 14, Margin = new Thickness(0, 12, 0, 2) });
        body.Children.Add(new Border { Background = AppTheme.Card, BorderBrush = B("#D0D7E2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8), Child = _log });
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
        _conn.Text = ph.Enabled ? $"📱 Đã kết nối: {Short(ph.Url)}" : "📱 Chưa kết nối — bấm \"Kết nối điện thoại…\" và nhập đường dẫn Web App + mã PC_TOKEN (giống app cũ: Cài Đặt › Điện Thoại).";
        _conn.Foreground = ph.Enabled ? B("#1B5E20") : B("#8A5300");
        var i = Array.FindIndex(PhoneAutoSync.Choices, c => c.Min == PhoneAutoSync.Minutes); _auto.SelectedIndex = Math.Max(0, i);
        _tables.Children.Clear();
        var pick = Svc.Pick; var st = Svc.Status;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        var row = 0;
        foreach (var (code, name, _) in PhoneSyncService.Tables)
        {
            if (!Svc.CanTable(code)) continue;
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cb = new CheckBox { Content = name, IsChecked = pick.Contains(code), MinWidth = 300, Margin = new Thickness(0, 1) };
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
    public static readonly (int Min, string Label)[] Choices = { (0, "Tắt"), (5, "Mỗi 5 phút"), (10, "Mỗi 10 phút"), (15, "Mỗi 15 phút"), (30, "Mỗi 30 phút"), (60, "Mỗi 60 phút") };
    private const string Key = "phone_sync_interval";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DispatcherTimer? _timer; private static DateTime _last = DateTime.MinValue;
    public static event Action<string>? Ran;
    public static int Minutes
    {
        get => int.TryParse(App.Services.Settings.Get(Key, "0"), out var m) ? m : 0;
        set => App.Services.Settings.Set(Key, value.ToString(CultureInfo.InvariantCulture));
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
        var m = Minutes; if (m <= 0 || !App.Services.Phone.Enabled || App.Services.Session.CurrentUser is null) return;
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

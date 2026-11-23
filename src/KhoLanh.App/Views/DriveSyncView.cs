using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.Core.Lan;

namespace KhoLanh.App.Views;

/// <summary>
/// Đồng bộ qua Google Drive: dữ liệu chính làm việc và sao lưu trên máy tính bình thường (Máy chính); máy ở mạng khác (Máy xem từ xa)
/// tải dữ liệu về xem và có thể gửi thay đổi ngược lên. Dùng thư mục của Google Drive for Desktop — không cần đăng nhập Google trong app.
/// </summary>
public sealed class DriveSyncView : UserControl, IPage
{
    public string Title => "Đồng bộ qua Google Drive";
    public string Subtitle => "Máy chính đưa dữ liệu lên thư mục Google Drive; máy ở mạng khác tải về xem và gửi thay đổi ngược lại. Tệp được mã hóa.";

    private static DriveSyncService Svc => App.Services.Drive;
    private readonly RadioButton _off = new() { GroupName = "drv", Content = "Tắt (không dùng đồng bộ Drive)" };
    private readonly RadioButton _main = new() { GroupName = "drv", Content = "Máy chính — giữ dữ liệu chính, đưa lên Drive" };
    private readonly RadioButton _remote = new() { GroupName = "drv", Content = "Máy xem từ xa — tải dữ liệu từ Drive về (sửa được, gửi ngược lên)" };
    private readonly TextBox _dir = new() { Watermark = @"VD  G:\My Drive\KhoLanh  (thư mục Google Drive trên máy này)", MinWidth = 380 };
    private readonly TextBox _code = new() { TextWrapping = TextWrapping.Wrap, Height = 64, AcceptsReturn = false };
    private readonly StackPanel _codePanel = new() { Spacing = 4 };
    private readonly StackPanel _mainPanel = new() { Spacing = 4 };
    private readonly TextBlock _status = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _detail = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _msg = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private CheckBox _planOn = null!;
    private StackPanel _planBox = null!;
    private readonly Dictionary<string, CheckBox> _mainBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    public DriveSyncView()
    {
        var s = new StackPanel { Spacing = 12, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(Card("Trạng thái", _status, _detail));
        s.Children.Add(Card("Cách dùng",
            Note("1) Cài Google Drive for Desktop trên cả hai máy, cùng đăng nhập một tài khoản Google (hoặc chia sẻ thư mục cho nhau)."),
            Note("2) Máy chính (máy đang giữ dữ liệu): chọn vai Máy chính + thư mục Drive → Lưu → chép “mã đồng bộ”."),
            Note("3) Máy ở mạng khác: chọn vai Máy xem từ xa + thư mục Drive (thư mục đã đồng bộ về máy) + dán mã đồng bộ → Lưu."),
            Note("Dữ liệu cập nhật theo chu kỳ (khoảng 1 phút + thời gian Google Drive đồng bộ), không tức thời. Máy xa mất mạng vẫn làm việc với bản đã tải, nối lại sẽ tự gửi thay đổi."),
            Note("Máy xem từ xa cần tài khoản đăng nhập trùng tên với tài khoản trên máy chính (dùng Tài khoản dùng chung qua Google Sheet).")));
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        dirRow.Children.Add(_dir); dirRow.Children.Add(Btn("Chọn thư mục…", async () => { var p = await Ui.OpenFolder(this, "Chọn thư mục Google Drive"); if (p is not null) _dir.Text = p; }));
        _codePanel.Children.Add(new TextBlock { Text = "Mã đồng bộ (KLDRV1-…) chép từ máy chính", FontSize = 12.5 }); _codePanel.Children.Add(_code);
        _mainPanel.Children.Add(new TextBlock { Text = "Mã đồng bộ cho máy xem từ xa (chứa khóa bí mật — đừng gửi nơi công khai):", FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        _mainPanel.Children.Add(Btn("Chép mã đồng bộ", async () =>
        {
            if (!Svc.Enabled) { _msg.Text = "Bấm “Lưu” để bật vai Máy chính trước."; return; }
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(Svc.Code());
            _msg.Text = "Đã chép mã đồng bộ. Dán vào máy xem từ xa.";
        }));
        var buttons = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        buttons.Children.Add(Btn("Lưu", async () => await Save(), primary: true));
        buttons.Children.Add(Btn("Đồng bộ ngay", async () => await SyncNow()));
        // ----- phân vai theo tài khoản
        _planOn = new CheckBox { Content = "Bật: vai Drive theo TÀI KHOẢN đăng nhập (tài khoản “máy chính” đăng nhập ở máy nào, máy đó là máy chính)" };
        _planBox = new StackPanel { Spacing = 2 };
        var planBtns = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        planBtns.Children.Add(Btn("Lưu phân vai", async () => await SavePlan(), primary: true));
        planBtns.Children.Add(Btn("Chỉ lưu thư mục trên máy này", async () => { if (await Ui.Run(this, () => Svc.SetFolder((_dir.Text ?? "").Trim()))) _msg.Text = "Đã lưu thư mục Drive của máy này."; await Task.CompletedTask; }));
        planBtns.Children.Add(Btn("Chiếm quyền máy chính (khi máy kia treo)", async () => await Force()));
        s.Children.Add(Card("Phân vai theo tài khoản (khuyên dùng)",
            Note("Quản trị chọn các tài khoản “máy chính”. Máy nào đăng nhập tài khoản đó trước thì là MÁY CHÍNH; máy đăng nhập sau (kể cả cùng tài khoản) và mọi tài khoản khác là MÁY XEM TỪ XA — nên không có hai máy chính cùng ghi đè nhau."),
            Note("Máy chính thoát phần mềm thì đưa bản cuối lên Drive và nhả quyền; máy kia đăng nhập lại là thành máy chính, giữ nguyên việc đã sửa ở máy xa. Nếu máy chính treo / mất điện, quyền hết hạn sau 15 phút hoặc bấm “Chiếm quyền”. Mỗi máy chỉ cần chọn thư mục Drive một lần."),
            _planOn, _planBox, planBtns));
        s.Children.Add(Card("Vai của máy này (cài tay)", _off, _main, _remote, new TextBlock { Text = "Thư mục Google Drive", FontSize = 12.5 }, dirRow, _mainPanel, _codePanel, buttons, _msg));
        Content = new ScrollViewer { Content = s };
        foreach (var r in new[] { _off, _main, _remote }) r.IsCheckedChanged += (_, _) => ShowPanels();
        _timer.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => { Load(); _timer.Start(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private void Load()
    {
        _dir.Text = Svc.Dir; _code.Text = "";
        _off.IsChecked = Svc.Mode == DriveSyncService.ModeOff; _main.IsChecked = Svc.Mode == DriveSyncService.ModeMain; _remote.IsChecked = Svc.Mode == DriveSyncService.ModeRemote;
        ShowPanels(); Refresh(); LoadPlan();
    }
    private void LoadPlan()
    {
        _planBox.Children.Clear(); _mainBoxes.Clear();
        var acc = App.Services.Accounts; var plan = acc.Plan; var dp = plan?.Drive ?? new DrivePlan();
        _planOn.IsChecked = dp.On; _planOn.IsEnabled = acc.CanWrite;
        if (!acc.Enabled) { _planBox.Children.Add(Note("Cần bật Tài khoản dùng chung (Google Sheet) ở Sao lưu và cài đặt để lưu phân vai.")); return; }
        foreach (var u in App.Services.Auth.ListUsers().Where(u => u.IsActive).OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase))
        {
            var cb = new CheckBox { Content = $"{u.Username} — {u.FullName}  (máy chính)", IsChecked = dp.IsMain(u.Username), IsEnabled = acc.CanWrite };
            _mainBoxes[u.Username] = cb; _planBox.Children.Add(cb);
        }
        if (!acc.CanWrite) _planBox.Children.Add(Note("Chỉ Quản trị (đã mở khóa ghi) mới sửa được phân vai."));
    }
    private async Task SavePlan()
    {
        var on = _planOn.IsChecked == true; var mains = _mainBoxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        if (on && mains.Count == 0) { _msg.Text = "Chọn ít nhất một tài khoản máy chính."; return; }
        if (!await Ui.RunAsync(this, async () => await App.Services.Accounts.UpdatePlanAsync(p =>
        {
            p.Drive.On = on; p.Drive.Main = new HashSet<string>(mains, StringComparer.OrdinalIgnoreCase);
            if (p.Drive.Key.Length == 0) p.Drive.Key = DriveSyncService.NewKey();
            return true;
        }))) return;
        _msg.Text = "Đã lưu phân vai. Mỗi máy sẽ nhận vai ở lần đăng nhập kế tiếp (máy này: đăng xuất rồi đăng nhập lại).";
    }
    private async Task Force()
    {
        if (!await Ui.Confirm(this, "Chiếm quyền máy chính ngay cả khi máy kia chưa nhả? Chỉ làm khi máy chính cũ đã tắt / treo — nếu nó vẫn đang chạy sẽ ghi đè lẫn nhau.", "Chiếm quyền", "Chiếm quyền")) return;
        string r = "";
        if (await Ui.RunAsync(this, async () => r = await Task.Run(() => Svc.BecomeMain(App.Services, App.Services.Session.Username, force: true)))) _msg.Text = r;
        Load();
    }
    private void ShowPanels() { _mainPanel.IsVisible = _main.IsChecked == true; _codePanel.IsVisible = _remote.IsChecked == true && Svc.Mode != DriveSyncService.ModeRemote; }
    private void Refresh()
    {
        var on = Svc.Enabled;
        _status.Text = !on ? "Đang tắt." : (Svc.Mode == DriveSyncService.ModeMain ? "Máy chính — " : "Máy xem từ xa — ") + (Svc.Status.Length > 0 ? Svc.Status : "chưa đồng bộ lượt nào");
        _detail.Text = on ? $"Thư mục: {Svc.Dir}" + (Svc.LastOk > DateTime.MinValue ? $" · lần thành công gần nhất {Svc.LastOk:HH:mm:ss}" : "") + (Svc.Mode == DriveSyncService.ModeRemote ? $" · {App.Services.Lan.PendingCount()} thay đổi chờ gửi" : "") : "";
    }

    private async Task Save()
    {
        var mode = _main.IsChecked == true ? DriveSyncService.ModeMain : _remote.IsChecked == true ? DriveSyncService.ModeRemote : DriveSyncService.ModeOff;
        if (mode == DriveSyncService.ModeRemote && Svc.Mode != DriveSyncService.ModeRemote && !await Ui.Confirm(this,
            "Máy xem từ xa sẽ lấy dữ liệu từ Google Drive; dữ liệu module đang có trong máy này sẽ được thay bằng dữ liệu của máy chính khi tải về. Tiếp tục?", "Máy xem từ xa", "Tiếp tục")) return;
        if (!await Ui.Run(this, () =>
        {
            Svc.Stop();
            Svc.Configure(mode, (_dir.Text ?? "").Trim(), _code.Text, App.Services.Session);
            App.Services.ReloadLan(start: true);
            Svc.Start();
        })) return;
        _msg.Text = mode == DriveSyncService.ModeOff ? "Đã tắt đồng bộ Drive." : "Đã lưu. Đang đồng bộ lượt đầu…";
        _code.Text = ""; Load();
        if (mode != DriveSyncService.ModeOff) await SyncNow();
    }

    private async Task SyncNow()
    {
        if (!Svc.Enabled) { _msg.Text = "Chưa bật đồng bộ."; return; }
        _msg.Text = "Đang đồng bộ…";
        var r = await Task.Run(() => Svc.SyncNow());
        _msg.Text = r; Refresh();
    }

    // ----- dựng giao diện
    private static readonly IBrush Line_ = new SolidColorBrush(Color.Parse("#D0D7E2"));
    private static TextBlock Note(string t) => new() { Text = t, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private static Border Card(string title, params Control[] kids)
    {
        var p = new StackPanel { Spacing = 6 };
        p.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 14 });
        foreach (var k in kids) p.Children.Add(k);
        return new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = p };
    }
    private static Button Btn(string text, Func<Task> click, bool primary = false)
    {
        var b = new Button { Content = text }; if (primary) b.Classes.Add("primary");
        b.Click += async (_, _) => await click();
        return b;
    }
}

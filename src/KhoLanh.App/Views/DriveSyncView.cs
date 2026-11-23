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
    private readonly CheckBox _home = new() { Content = "Máy này ở NHÀ — luôn là máy xem từ xa, không tự nhận làm máy chính (máy công ty mở lại là tự gộp việc làm ở nhà)" };
    private readonly ComboBox _every = new() { ItemsSource = DriveSyncService.Intervals.Select(x => x.Label).ToList(), MinWidth = 330 };
    private readonly ListBox _bk = new() { Height = 150 };
    private readonly TextBlock _bkMsg = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private Button _bkNow = null!, _bkRestore = null!;
    private List<DriveSyncService.DriveBackup> _bkList = new();

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
        // ----- máy ở nhà · chu kỳ · sao lưu trên Drive
        var homeBtns = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        homeBtns.Children.Add(Btn("Lưu cài đặt máy này", async () => await SaveHome(), primary: true));
        s.Children.Add(Card("Máy ở nhà & chu kỳ đồng bộ",
            Note("Ban ngày dùng LAN ở công ty; ban đêm các máy ở nhà đồng bộ qua Drive và làm việc độc lập. Việc làm ở nhà được gửi lên Drive và máy chủ công ty tự gộp khi mở lại (trùng dòng → Quản trị chọn ở Xung đột). Tắt app thì app gửi nốt thay đổi còn chờ."),
            _home, new TextBlock { Text = "Đồng bộ mỗi", FontSize = 12.5 }, _every, homeBtns));
        _bkNow = Btn("☁ Sao lưu lên Drive ngay", async () => await BackupNow());
        var bkDl = Btn("⬇ Tải bản đã chọn về file .db…", async () => await DownloadSel());
        _bkRestore = Btn("⏪ Khôi phục máy này từ bản đã chọn", async () => await RestoreSel());
        var bkBtns = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        bkBtns.Children.Add(_bkNow); bkBtns.Children.Add(bkDl); bkBtns.Children.Add(_bkRestore); bkBtns.Children.Add(Btn("Tải lại danh sách", () => { LoadBackups(); return Task.CompletedTask; }));
        s.Children.Add(Card("Sao lưu trên Google Drive",
            Note("Máy chính tự sao lưu toàn bộ dữ liệu lên Drive mỗi ngày (mã hóa bằng mã đồng bộ); giữ 14 ngày gần nhất + 1 bản mỗi tháng trong 12 tháng."),
            Note("Máy công ty (LAN): khôi phục ở Sao lưu và cài đặt — tải bản đã chọn về file .db rồi khôi phục (Quản trị). Máy ở nhà: khôi phục thẳng tại đây; chỉ ảnh hưởng máy này, những gì sửa sau khi khôi phục mới được gửi lên."),
            _bk, bkBtns, _bkMsg));
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
        _home.IsChecked = Svc.IsHome;
        _every.SelectedIndex = Math.Max(0, Array.FindIndex(DriveSyncService.Intervals, x => x.Sec == Svc.IntervalSeconds));
        LoadBackups();
    }

    private void LoadBackups()
    {
        try { _bkList = Svc.DriveBackups(); } catch { _bkList = new(); }
        _bk.ItemsSource = _bkList.Select(b => $"{b.Day:dd/MM/yyyy (ddd)}  ·  {b.Bytes / 1024:#,0} KB  ·  {b.Name}").ToList();
        if (_bkList.Count > 0) _bk.SelectedIndex = 0;
        _bkNow.IsVisible = Svc.Mode == DriveSyncService.ModeMain;
        _bkRestore.IsVisible = Svc.Mode == DriveSyncService.ModeRemote;
        _bkMsg.Text = !Svc.Enabled ? "Bật đồng bộ Drive để dùng sao lưu trên Drive." : _bkList.Count == 0 ? "Chưa có bản sao lưu nào trên Drive (máy chính tự tạo ở lượt đồng bộ đầu tiên trong ngày)." : $"{_bkList.Count} bản · mới nhất {_bkList[0].Day:dd/MM/yyyy}.";
    }

    private async Task SaveHome()
    {
        var sec = DriveSyncService.Intervals[Math.Max(0, _every.SelectedIndex)].Sec; var home = _home.IsChecked == true;
        if (!await Ui.Run(this, () =>
        {
            App.Services.Session.Require("backup");
            Svc.SetHome(home); Svc.SetInterval(sec);
            if (home && Svc.Mode == DriveSyncService.ModeMain && Svc.Enabled) { Svc.ReleaseOnExit(); Svc.BecomeRemote(App.Services); }
            Svc.Stop(); Svc.Start();
        })) return;
        _msg.Text = "Đã lưu: " + (home ? "máy ở nhà (luôn là máy xem từ xa)" : "máy bình thường") + " · đồng bộ " + DriveSyncService.Intervals[Math.Max(0, _every.SelectedIndex)].Label + ".";
        Load();
    }

    private DriveSyncService.DriveBackup? SelBk => _bk.SelectedIndex >= 0 && _bk.SelectedIndex < _bkList.Count ? _bkList[_bk.SelectedIndex] : null;

    private async Task BackupNow()
    {
        string r = "";
        if (await Ui.RunAsync(this, async () => r = await Task.Run(() => Svc.BackupToDrive(force: true)))) { _bkMsg.Text = r; LoadBackups(); _bkMsg.Text = r; }
    }

    private async Task DownloadSel()
    {
        if (SelBk is not { } b) { _bkMsg.Text = "Chọn một bản sao lưu."; return; }
        var path = await Ui.SaveFile(this, "Lưu bản sao lưu", $"KhoLanh_{b.Day:yyyy-MM-dd}.db", "db", "Bản sao lưu Kho Lạnh");
        if (path is null) return;
        if (await Ui.RunAsync(this, async () => await Task.Run(() => Svc.DownloadBackup(b.Name, path))))
            _bkMsg.Text = $"Đã tải về {path}. Khôi phục ở Hệ thống › Sao lưu và cài đặt › Khôi phục từ file.";
    }

    private async Task RestoreSel()
    {
        if (SelBk is not { } b) { _bkMsg.Text = "Chọn một bản sao lưu."; return; }
        var pend = App.Services.Lan.PendingCount();
        if (!await Ui.Confirm(this, $"Khôi phục MÁY NÀY về bản sao lưu ngày {b.Day:dd/MM/yyyy}?\n\n• Chỉ ảnh hưởng máy này, không đổi dữ liệu ở công ty.\n• Cài đặt của máy (thư mục Drive, mã đồng bộ…) giữ nguyên." +
            (pend > 0 ? $"\n• {pend} thay đổi chưa gửi của máy này sẽ BỎ (bản hiện tại được lưu lại trong thư mục SaoLuu)." : "") + "\n• Lần đồng bộ tới tải bản mới nhất của máy chính về.\n\nSau khi khôi phục cần đăng nhập lại.", "Khôi phục máy ở nhà", "Khôi phục")) return;
        var tmp = Path.Combine(Path.GetTempPath(), "kl-khoi-phuc-" + Guid.NewGuid().ToString("N")[..6] + ".db");
        if (!await Ui.RunAsync(this, async () => await Task.Run(() => { Svc.Stop(); Svc.DownloadBackup(b.Name, tmp); App.Services.Backup.Restore(tmp); }))) { Svc.Start(); return; }
        try { File.Delete(tmp); } catch { }
        Svc.Start();
        await Ui.Message(this, "Đã khôi phục máy này. Đăng nhập lại để tiếp tục — app sẽ tải bản mới nhất từ Drive.", "Khôi phục xong");
        MainWindow.Of(this)?.Logout();
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
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.Core.Lan;
using KhoLanh.Core.Models;
using KhoLanh.Core.SaoLuu;

namespace KhoLanh.App.Views;

/// <summary>
/// DỮ LIỆU &amp; SAO LƯU (1.68 — người dùng chốt): 1 module, 2 tab.
///  • Đồng bộ (chọn 1 — máy này lấy database chính từ đâu): 1. Chỉ trên máy này · 2. Thư mục / Google Drive dùng chung (DriveSyncView) ·
///    3. Máy chủ LAN (LanView). Chỉ Quản trị.
///  • Sao lưu (dùng cho MỌI kiểu đồng bộ, kể cả LAN): Trên máy này (tự sao lưu mỗi ngày, ra file, khôi phục) · Google Drive cá nhân (đăng nhập Gmail
///    như app cũ, <see cref="DriveCaNhanService"/>). Sao lưu chỉ chép dữ liệu ra ngoài, không đụng database chính.
/// Thay 3 mục menu cũ: Mạng LAN, Đồng bộ Google Drive và phần sao lưu của "Sao lưu và cài đặt" (nay là "Cài đặt máy").
/// </summary>
public sealed class DuLieuView : UserControl, IPage
{
    public string Title => "Dữ liệu & sao lưu";
    public string Subtitle => "Đồng bộ: máy này lấy dữ liệu chính từ đâu (chỉ máy này / thư mục Drive dùng chung / máy chủ LAN). Sao lưu: trên máy + Google Drive cá nhân.";

    /// <summary>Mở thẳng: "lan" / "drive" (tab Đồng bộ) · "saoluu" / "dcn" (tab Sao lưu).</summary>
    public DuLieuView(string start = "")
    {
        var s = App.Services;
        var admin = Roles.IsAdmin(s.Session.Role);
        var tabs = new TabControl();
        var sync = admin ? BuildSync(start) : (Control)Muted("Chỉ tài khoản Quản trị đổi được cách đồng bộ của máy.");
        tabs.Items.Add(new TabItem { Header = "Đồng bộ", Content = new Border { Padding = new Thickness(0, 6, 0, 0), Child = sync } });
        var dcn = new DriveCaNhanPanel();
        var backup = Ui.SubTabs(("Trên máy này", new LocalBackupPanel()), ("Google Drive cá nhân (Gmail)", dcn));
        if (start == "dcn") backup.SelectedIndex = 1;
        tabs.Items.Add(new TabItem { Header = "Sao lưu", Content = new Border { Padding = new Thickness(0, 6, 0, 0), Child = backup } });
        tabs.SelectedIndex = start is "saoluu" or "dcn" || !admin ? 1 : 0;
        Content = tabs;
    }

    internal static TextBlock Muted(string t) => new() { Text = t, Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
    internal static Border Card(string title, params Control[] kids)
    {
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(new TextBlock { Text = title, Classes = { "section" } });
        foreach (var k in kids) p.Children.Add(k);
        return new Border { Classes = { "card" }, VerticalAlignment = VerticalAlignment.Top, Child = p };
    }
    internal static Button Btn(string text, Func<Task> click, string? cls = null)
    {
        var b = new Button { Content = text }; if (cls is not null) b.Classes.Add(cls);
        b.Click += async (_, _) => await click();
        return b;
    }

    /// <summary>Kiểu đồng bộ hiện tại: 0 chỉ máy này · 1 thư mục / Drive dùng chung · 2 máy chủ LAN.</summary>
    internal static int CurrentMode()
    {
        var s = App.Services;
        if (s.Lan.Enabled && s.Lan.Role != LanService.RoleRemote) return 2;
        if (s.Drive.Mode is DriveSyncService.ModeMain or DriveSyncService.ModeRemote || s.Lan.Role == LanService.RoleRemote) return 1;
        return 0;
    }

    private static Control BuildSync(string start)
    {
        var s = App.Services;
        var mode = CurrentMode();
        string[] names = { "Chỉ trên máy này", "Thư mục / Google Drive dùng chung", "Máy chủ LAN" };
        var lan = s.Lan;
        var now = mode switch
        {
            2 => $"{names[2]} — vai: {LanService.RoleLabel(lan.Role)}" + (lan.Serving ? " (đang làm máy chủ)" : lan.Online ? $" · nối {lan.ServerName}" : " · chưa nối được máy chủ"),
            1 => $"{names[1]} — " + (s.Drive.Mode == DriveSyncService.ModeMain ? "máy chính (giữ dữ liệu chính)" : "máy xem từ xa"),
            _ => names[0] + " — dữ liệu chỉ nằm trên máy này",
        };
        var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8), Children =
        {
            new TextBlock { Text = "Máy này đang: " + now, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            Muted("Mỗi máy chọn MỘT cách lấy database chính. Sao lưu (tab Sao lưu) độc lập — bật được với mọi cách, kể cả máy chủ LAN."),
        } };

        var local = new StackPanel { Spacing = 10, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left, Children =
        {
            Card("1. Chỉ trên máy này (ngoài LAN)",
                Muted("Dữ liệu nằm trong file database trên máy này, không dùng chung với máy khác. Hợp với máy làm việc một mình."),
                Muted("• Tự sao lưu mỗi ngày vào thư mục SaoLuu (giữ 30 bản) — xem tab Sao lưu › Trên máy này.\n• Muốn có bản ngoài máy (máy hỏng vẫn còn): tab Sao lưu › Google Drive cá nhân — đăng nhập Gmail, tự sao lưu theo lịch."),
                mode == 0
                    ? new TextBlock { Text = "✔ Máy này đang dùng cách này.", Foreground = AppTheme.Brush("#1E7B4A"), FontWeight = FontWeight.SemiBold }
                    : Muted("Máy này đang dùng cách " + (mode + 1) + ". Muốn về cách 1: " + (mode == 2 ? "tab “Máy chủ LAN” › Quản trị bấm Tắt máy chủ trên máy này." : "tab “Thư mục / Google Drive dùng chung” › chọn Tắt › Lưu."))),
        } };
        var sub = Ui.SubTabs(("1. " + names[0], new ScrollViewer { Content = local }), ("2. " + names[1], new DriveSyncView()), ("3. " + names[2], new LanView()));
        sub.SelectedIndex = start switch { "lan" => 2, "drive" => 1, _ => mode };
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head); dock.Children.Add(sub);
        return dock;
    }
}

/// <summary>Sao lưu trên máy này: bản tự động mỗi ngày (thư mục SaoLuu, giữ 30 bản), sao lưu ra file, khôi phục (Quản trị). Chuyển từ "Sao lưu và cài đặt".</summary>
internal sealed class LocalBackupPanel : UserControl
{
    private sealed record Row(string Name, string FullName, DateTime LastWriteTime, long Kb);
    private readonly DataGrid _grid = new() { IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };

    public LocalBackupPanel()
    {
        var s = App.Services;
        var admin = Roles.IsAdmin(s.Session.Role);
        _grid.Columns.Add(new DataGridTextColumn { Header = "Tên file", Binding = new Binding(nameof(Row.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Thời gian", Binding = new Binding(nameof(Row.LastWriteTime)) { StringFormat = "dd/MM/yyyy HH:mm" }, Width = new DataGridLength(140) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Dung lượng (KB)", Binding = new Binding(nameof(Row.Kb)) { StringFormat = "#,0" }, Width = new DataGridLength(130) });
        var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        bar.Children.Add(DuLieuView.Btn("Sao lưu ra file…", Backup, "primary"));
        if (admin)
        {
            bar.Children.Add(DuLieuView.Btn("Khôi phục bản đang chọn", async () =>
            {
                if (_grid.SelectedItem is Row r) await Restore(r.FullName); else await Ui.Message(this, "Chọn một bản sao lưu trong danh sách trước.");
            }, "danger"));
            bar.Children.Add(DuLieuView.Btn("Khôi phục từ file…", async () =>
            {
                var path = await Ui.OpenFile(this, "Chọn file sao lưu", "db", "File sao lưu Kho Lạnh");
                if (path is not null) await Restore(path);
            }, "danger"));
        }
        bar.Children.Add(DuLieuView.Btn("Mở thư mục SaoLuu", () => { Directory.CreateDirectory(s.Backup.AutoBackupFolder); Ui.OpenExternal(s.Backup.AutoBackupFolder); return Task.CompletedTask; }));
        var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8), Children =
        {
            bar,
            DuLieuView.Muted("Mỗi ngày, lần mở đầu tiên app tự sao lưu (tu-dong_…, giữ 30 bản). Bản truoc-khoi-phuc_… / truoc-doi-vai-… là bản chụp dữ liệu cũ ngay trước khi khôi phục / đổi vai LAN."
                + (admin ? "" : " Khôi phục: chỉ Quản trị.")),
        } };
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head); dock.Children.Add(_grid);
        Content = dock;
        AttachedToVisualTree += (_, _) => Load();
    }

    private void Load() => _grid.ItemsSource = App.Services.Backup.ListAutoBackups().Select(f => new Row(f.Name, f.FullName, f.LastWriteTime, f.Length / 1024)).ToList();

    private async Task Backup()
    {
        var path = await Ui.SaveFile(this, "Sao lưu dữ liệu", $"KhoLanh_SaoLuu_{DateTime.Now:yyyyMMdd_HHmm}.db", "db", "File sao lưu Kho Lạnh");
        if (path is null) return;
        if (await Ui.Run(this, () => App.Services.Backup.ManualBackup(path))) await Ui.Message(this, $"Đã sao lưu ra:\n{path}");
    }

    private async Task Restore(string path)
    {
        if (!await Ui.Confirm(this,
                $"Khôi phục dữ liệu từ:\n{path}\n\nToàn bộ dữ liệu hiện tại sẽ được thay bằng bản sao lưu này. Phần mềm sẽ tự sao lưu bản hiện tại trước khi khôi phục, rồi yêu cầu đăng nhập lại.",
                "Khôi phục dữ liệu", "Khôi phục")) return;
        string safety = "";
        if (!await Ui.Run(this, () => safety = App.Services.Backup.Restore(path))) return;
        var main = MainWindow.Of(this);
        await Ui.Message(this, $"Đã khôi phục xong. Bản dữ liệu trước khi khôi phục được giữ ở:\n{safety}\n\nVui lòng đăng nhập lại.");
        main?.Logout();
    }
}

/// <summary>Sao lưu lên Google Drive cá nhân: đăng nhập Gmail (như app cũ), sao lưu ngay / theo lịch, danh sách bản trên Drive (mọi máy), tải về, khôi phục.</summary>
internal sealed class DriveCaNhanPanel : UserControl
{
    private static DriveCaNhanService Svc => App.Services.DriveCaNhan;
    private sealed record Row(string May, DateTime At, string Size, string Name, DriveCaNhanService.Item It);

    private readonly TextBlock _acct = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _state = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
    private readonly TextBlock _msg = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
    private readonly ComboBox _every = new() { ItemsSource = DriveCaNhanService.Intervals.Select(i => i.Label).ToList(), MinWidth = 220 };
    private readonly TextBox _cid = new() { Watermark = "Client ID (….apps.googleusercontent.com)", MinWidth = 380 };
    private readonly TextBox _csec = new() { Watermark = "Client Secret", PasswordChar = '•', MinWidth = 220 };
    private readonly DataGrid _grid = new() { IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, Height = 260 };
    private readonly Button _signIn, _signOut, _now, _cancel;
    private readonly Expander _setup;
    private CancellationTokenSource? _signCts;

    public DriveCaNhanPanel()
    {
        var admin = Roles.IsAdmin(App.Services.Session.Role);
        _signIn = DuLieuView.Btn("Đăng nhập Gmail", SignIn, "primary");
        _signOut = DuLieuView.Btn("Đăng xuất Gmail", async () =>
        {
            if (!await Ui.Confirm(this, "Gỡ liên kết Gmail này khỏi máy (bản sao lưu trên Drive vẫn giữ nguyên)?", "Đăng xuất Gmail", "Đăng xuất")) return;
            await Ui.RunAsync(this, Svc.SignOutAsync); Render();
        });
        _cancel = DuLieuView.Btn("Đóng (hủy đăng nhập)", () => { _signCts?.Cancel(); return Task.CompletedTask; });
        _now = DuLieuView.Btn("Sao lưu lên Drive ngay", BackupNow);
        _every.SelectionChanged += async (_, _) =>
        {
            if (_every.SelectedIndex < 0 || !Svc.CanUse) return;
            var h = DriveCaNhanService.Intervals[_every.SelectedIndex].Hours;
            if (h != Svc.Config.Every && await Ui.Run(this, () => Svc.SetEvery(h))) _msg.Text = "Đã lưu lịch: " + DriveCaNhanService.Intervals[_every.SelectedIndex].Label + ".";
        };
        _setup = new Expander
        {
            Header = "Client ID Google của máy này (Quản trị nhập 1 lần — dùng chung với Lưu trữ cá nhân)", HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = admin,
            Content = new StackPanel { Spacing = 6, Children =
            {
                DuLieuView.Muted("Tạo trên Google Cloud Console: API & Services › Credentials › Create credentials › OAuth client ID › loại Desktop app; bật Google Drive API. Máy mới cũng nhập ĐÚNG Client ID này thì mới thấy bản sao lưu cũ (quyền drive.file: app chỉ thấy tệp do chính Client ID đó tạo). Hướng dẫn chi tiết: docs/LUU-TRU-CA-NHAN.md."),
                new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { _cid, _csec, DuLieuView.Btn("Lưu", async () =>
                {
                    if (await Ui.Run(this, () => Svc.SetClient(_cid.Text ?? "", _csec.Text ?? ""))) { _csec.Text = ""; _msg.Text = "Đã lưu Client ID."; Render(); }
                }) } },
            } },
        };
        var acctCard = DuLieuView.Card("Tài khoản Google", _acct, new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { _signIn, _cancel, _signOut } }, _setup);
        var backupCard = DuLieuView.Card("Sao lưu database lên Drive",
            _state,
            new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { _now, new TextBlock { Text = "Tự sao lưu:", VerticalAlignment = VerticalAlignment.Center }, _every } },
            DuLieuView.Muted($"Lưu ở Drive của Gmail đang đăng nhập: {DriveCaNhanService.Root} / <tên máy> / KhoLanh_<ngày giờ>.db.gz. Giữ mỗi ngày 1 bản trong {DriveCaNhanService.KeepDays} ngày + 1 bản mỗi tháng trong {DriveCaNhanService.KeepMonths} tháng. Chỉ đọc database (chụp bản sao rồi tải lên) — không đụng đồng bộ LAN / Drive dùng chung. Khôi phục không cần mã đồng bộ: đăng nhập đúng Gmail là được, kể cả trên máy mới."));
        _grid.Columns.Add(new DataGridTextColumn { Header = "Máy", Binding = new Binding(nameof(Row.May)), Width = new DataGridLength(160) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Thời gian", Binding = new Binding(nameof(Row.At)) { StringFormat = "dd/MM/yyyy HH:mm" }, Width = new DataGridLength(140) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Dung lượng", Binding = new Binding(nameof(Row.Size)), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Tên tệp", Binding = new Binding(nameof(Row.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var listBtns = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        listBtns.Children.Add(DuLieuView.Btn("Tải lại danh sách", LoadList));
        listBtns.Children.Add(DuLieuView.Btn("Tải bản đang chọn về file .db…", Download));
        if (admin) listBtns.Children.Add(DuLieuView.Btn("Khôi phục máy này từ bản đang chọn", Restore, "danger"));
        var listCard = DuLieuView.Card("Bản sao lưu trên Drive (mọi máy của Gmail này)", listBtns, _grid);
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 6, 0) };
        body.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 14, Children = { acctCard, backupCard.Also(b => Grid.SetColumn(b, 1)) } });
        body.Children.Add(listCard);
        body.Children.Add(_msg);
        Content = new ScrollViewer { Content = body };
        Svc.StatusChanged += OnStatus;
        AttachedToVisualTree += (_, _) => { Render(); _ = LoadList(); };
        DetachedFromVisualTree += (_, _) => Svc.StatusChanged -= OnStatus;
    }

    private void OnStatus() => Avalonia.Threading.Dispatcher.UIThread.Post(Render);

    private void Render()
    {
        if (!Svc.CanUse)
        {
            _acct.Text = "Tài khoản này chưa có quyền «Sao lưu và cài đặt» (mức Sửa) nên không dùng được sao lưu Drive cá nhân.";
            _signIn.IsVisible = _signOut.IsVisible = _cancel.IsVisible = _now.IsEnabled = _every.IsEnabled = false; _state.Text = ""; return;
        }
        var c = Svc.Config; var client = Svc.Client;
        _cid.Text = client.Id;
        _acct.Text = !client.Ok ? "Máy này chưa có Client ID Google — Quản trị mở mục bên dưới để nhập (1 lần mỗi máy)."
            : c.Linked ? $"Đã đăng nhập Gmail: {c.Email}" : c.Email.Length > 0 ? $"Chưa đăng nhập (lần trước: {c.Email})." : "Chưa đăng nhập Gmail.";
        if (!client.Ok) _setup.IsExpanded = true;
        _signIn.Content = c.Linked ? "Đổi Gmail" : "Đăng nhập Gmail";
        _signIn.IsVisible = _signCts is null; _cancel.IsVisible = _signCts is not null;
        _signIn.IsEnabled = client.Ok; _signOut.IsVisible = c.Linked && _signCts is null;
        var why = Svc.WhyNotMain();
        _now.IsEnabled = c.Linked && why is null && !Svc.Busy;
        _every.IsEnabled = c.Linked;
        var idx = Array.FindIndex(DriveCaNhanService.Intervals, i => i.Hours == c.Every);
        if (_every.SelectedIndex != idx) _every.SelectedIndex = Math.Max(0, idx);
        _state.Text = (why is not null ? "⚠ " + why + "\n" : "")
            + (c.Last is { } l ? $"Lần sao lưu gần nhất: {l:dd/MM/yyyy HH:mm} ({c.LastName})." : "Chưa sao lưu lần nào từ máy này.")
            + (c.LastErr.Length > 0 ? "\nLỗi lần trước: " + c.LastErr : "");
        if (c.LastErr.Length > 0 || why is not null) _state.Foreground = AppTheme.Brush("#9A5B00"); else _state.ClearValue(TextBlock.ForegroundProperty);
    }

    private async Task SignIn()
    {
        _signCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        Render();
        try
        {
            var mail = await Svc.SignInAsync(Ui.OpenExternal, _signCts.Token);
            _msg.Text = "Đã đăng nhập Google: " + mail;
            await LoadList();
        }
        catch (OperationCanceledException) { _msg.Text = "Đã hủy đăng nhập."; }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Đăng nhập Gmail"); }
        finally { _signCts?.Dispose(); _signCts = null; Render(); }
    }

    private async Task BackupNow()
    {
        _msg.Text = "Đang sao lưu lên Drive…"; _now.IsEnabled = false;
        DriveCaNhanService.Result? r = null;
        if (await Ui.RunAsync(this, async () => r = await Task.Run(() => Svc.BackupNowAsync()))) { _msg.Text = r!.Message; await LoadList(); }
        else _msg.Text = "";
        Render();
    }

    private async Task LoadList()
    {
        if (!Svc.CanUse || !Svc.Config.Linked) { _grid.ItemsSource = null; return; }
        try
        {
            var list = await Task.Run(() => Svc.ListAsync());
            _grid.ItemsSource = list.Select(i => new Row(i.Machine, i.At, $"{i.Size / 1024.0 / 1024:0.0} MB", i.Name, i)).ToList();
            if (list.Count == 0) _msg.Text = "Chưa có bản sao lưu nào trên Drive của Gmail này.";
        }
        catch (Exception e) { _msg.Text = "Không đọc được danh sách trên Drive: " + Ui.Describe(e); }
    }

    private DriveCaNhanService.Item? Sel => (_grid.SelectedItem as Row)?.It;

    private async Task Download()
    {
        if (Sel is not { } it) { await Ui.Message(this, "Chọn một bản trong danh sách trước."); return; }
        var path = await Ui.SaveFile(this, "Lưu bản sao lưu", $"KhoLanh_{it.Machine}_{it.At:yyyy-MM-dd_HHmm}.db", "db", "File sao lưu Kho Lạnh");
        if (path is null) return;
        if (await Ui.RunAsync(this, () => Task.Run(() => Svc.DownloadAsync(it, path))))
            _msg.Text = $"Đã tải về {path}. Khôi phục bằng tab Sao lưu › Trên máy này › Khôi phục từ file (Quản trị).";
    }

    private async Task Restore()
    {
        if (Sel is not { } it) { await Ui.Message(this, "Chọn một bản trong danh sách trước."); return; }
        if (Svc.WhyNotMain() is { } why) { await Ui.Message(this, why, "Khôi phục"); return; }
        if (!await Ui.Confirm(this,
                $"Khôi phục MÁY NÀY về bản trên Drive:\n{it.Machine} · {it.At:dd/MM/yyyy HH:mm}\n\nToàn bộ dữ liệu hiện tại sẽ được thay bằng bản này. App tự chụp bản hiện tại vào thư mục SaoLuu (truoc-khoi-phuc_…) trước khi ghi đè, rồi yêu cầu đăng nhập lại.",
                "Khôi phục từ Google Drive", "Khôi phục")) return;
        _msg.Text = "Đang tải và khôi phục…";
        if (!await Ui.RunAsync(this, () => Task.Run(() => Svc.RestoreAsync(it)))) { _msg.Text = ""; return; }
        await Ui.Message(this, "Đã khôi phục xong. Vui lòng đăng nhập lại.", "Khôi phục xong");
        MainWindow.Of(this)?.Logout();
    }
}

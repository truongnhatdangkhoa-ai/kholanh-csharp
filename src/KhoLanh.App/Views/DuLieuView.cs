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
/// Hàm dùng chung cho phần Sao lưu (<see cref="LocalBackupPanel"/>, <see cref="DriveCaNhanPanel"/> — nằm ở Đồng bộ &amp; sao lưu › Sao lưu).
/// 1.121: bỏ trang "Dữ liệu &amp; sao lưu" cũ (tab Đồng bộ: Chỉ máy này / Thư mục - Drive dùng chung / Máy chủ LAN) — tình trạng nối máy chủ ở
/// <see cref="MayChuView"/>, Drive dùng chung bỏ hẳn (<see cref="DriveRetire"/>).
/// </summary>
internal static class DuLieuView
{
    // ------------------------------------------------------------------ 1.114: khôi phục theo 2 chế độ tự động

    /// <summary>Máy đang nối máy chủ LAN → báo khóa (trả Task hiện thông báo); null = được khôi phục.</summary>
    internal static Task? RestoreLocked(Visual v) =>
        KhoLanh.Core.AppServices.RestoreLock(App.Services.Lan) is { } why ? Ui.Message(v, why, "Khôi phục bị khóa") : null;

    /// <summary>Máy đang ở mạng LAN / Drive dùng chung (nhưng ngoài mạng) → câu báo sẽ tách ra dùng riêng.</summary>
    internal static string DetachNote()
    {
        var s = App.Services;
        if (s.Lan.Role.Length == 0) return "";
        return "\n\nMáy này đang ở NGOÀI mạng LAN (không nối máy chủ) nên được khôi phục: sau khi khôi phục, máy TÁCH ra dùng dữ liệu riêng — "
             + "không gửi gì lên máy chủ, máy chủ và các máy trong LAN không bị ảnh hưởng. Muốn dùng chung lại: Đồng bộ & sao lưu › Máy chủ › «Nối lại mạng LAN» (dữ liệu riêng được thay bằng dữ liệu máy chủ).";
    }

    internal static string DetachedDone() => App.Services.Backup.LastDetached
        ? "\n\nMáy này đã tách khỏi mạng LAN, đang dùng dữ liệu riêng (máy chủ không bị ảnh hưởng)."
        : "";

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
}

/// <summary>Sao lưu trên máy này: bản tự động mỗi ngày (thư mục SaoLuu, giữ 30 bản), sao lưu ra file, khôi phục (Quản trị). Chuyển từ "Sao lưu và cài đặt".</summary>
internal sealed class LocalBackupPanel : UserControl
{
    private sealed record Row(string Name, string FullName, DateTime LastWriteTime, long Kb);
    private readonly DataGrid _grid = new() { IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };

    public LocalBackupPanel()
    {
        var s = App.Services;
        var admin = s.Session.Can("khoiphuc", Perm.Edit);   // 1.114: quyền Khôi phục dữ liệu (Quản trị luôn có)
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
                + (admin ? " Máy đang nối máy chủ LAN thì khóa khôi phục; máy ngoài LAN khôi phục xong tự tách ra dùng dữ liệu riêng." : " Khôi phục: cần quyền «Khôi phục dữ liệu».")),
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
        if (DuLieuView.RestoreLocked(this) is { } t) { await t; return; }
        if (!await Ui.Confirm(this,
                $"Khôi phục dữ liệu từ:\n{path}\n\nToàn bộ dữ liệu module trên máy này sẽ được thay bằng bản sao lưu này (cài đặt máy giữ nguyên). Phần mềm sẽ tự sao lưu bản hiện tại trước khi khôi phục, rồi yêu cầu đăng nhập lại."
                + DuLieuView.DetachNote(),
                "Khôi phục dữ liệu", "Khôi phục")) return;
        string safety = "";
        if (!await Ui.Run(this, () => safety = App.Services.Backup.Restore(path))) return;
        var main = MainWindow.Of(this);
        await Ui.Message(this, $"Đã khôi phục xong. Bản dữ liệu trước khi khôi phục được giữ ở:\n{safety}" + DuLieuView.DetachedDone() + "\n\nVui lòng đăng nhập lại.");
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
            DuLieuView.Muted($"Lưu ở Drive của Gmail đang đăng nhập: {DriveCaNhanService.Root} / <tên máy> / KhoLanh_<ngày giờ>.db.gz. Giữ mỗi ngày 1 bản trong {DriveCaNhanService.KeepDays} ngày + 1 bản mỗi tháng trong {DriveCaNhanService.KeepMonths} tháng. Chỉ đọc database (chụp bản sao rồi tải lên) — không đụng đồng bộ giữa các máy. Khôi phục không cần mã đồng bộ: đăng nhập đúng Gmail là được, kể cả trên máy mới."));
        _grid.Columns.Add(new DataGridTextColumn { Header = "Máy", Binding = new Binding(nameof(Row.May)), Width = new DataGridLength(160) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Thời gian", Binding = new Binding(nameof(Row.At)) { StringFormat = "dd/MM/yyyy HH:mm" }, Width = new DataGridLength(140) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Dung lượng", Binding = new Binding(nameof(Row.Size)), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Tên tệp", Binding = new Binding(nameof(Row.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var listBtns = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        listBtns.Children.Add(DuLieuView.Btn("Tải lại danh sách", LoadList));
        listBtns.Children.Add(DuLieuView.Btn("Tải bản đang chọn về file .db…", Download));
        if (App.Services.Session.Can("khoiphuc", Perm.Edit)) listBtns.Children.Add(DuLieuView.Btn("Khôi phục máy này từ bản đang chọn", Restore, "danger"));
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
        if (DuLieuView.RestoreLocked(this) is { } t) { await t; return; }
        if (!await Ui.Confirm(this,
                $"Khôi phục MÁY NÀY về bản trên Drive:\n{it.Machine} · {it.At:dd/MM/yyyy HH:mm}\n\nToàn bộ dữ liệu module trên máy này sẽ được thay bằng bản này (cài đặt máy giữ nguyên). App tự chụp bản hiện tại vào thư mục SaoLuu (truoc-khoi-phuc_…) trước khi ghi đè, rồi yêu cầu đăng nhập lại."
                + DuLieuView.DetachNote(),
                "Khôi phục từ Google Drive", "Khôi phục")) return;
        _msg.Text = "Đang tải và khôi phục…";
        if (!await Ui.RunAsync(this, () => Task.Run(() => Svc.RestoreAsync(it)))) { _msg.Text = ""; return; }
        await Ui.Message(this, "Đã khôi phục xong." + DuLieuView.DetachedDone() + "\n\nVui lòng đăng nhập lại.", "Khôi phục xong");
        MainWindow.Of(this)?.Logout();
    }
}

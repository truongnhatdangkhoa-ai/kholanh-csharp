using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using KhoLanh.App.Views;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App;

/// <summary>Màn hình có thể hiển thị trong khung chính.</summary>
public interface IPage
{
    string Title { get; }
    string Subtitle => "";
}

public partial class MainWindow : Window
{
    public event Action? LoggedOut;
    public bool IsLoggingOut { get; private set; }

    private record NavItem(string Key, string Label, Func<Control> Create);

    /// <summary>Mục "users" chỉ Quản trị; các mục khác theo phân quyền tài khoản (mức Xem trở lên).</summary>
    private static bool Allowed(NavItem i) => i.Key is "users" or "lan" or "drive" or "troly" ? Roles.IsAdmin(App.Services.Session.Role) : App.Services.Session.Can(i.Key);

    private readonly List<(string group, NavItem[] items)> _menu;
    private readonly Dictionary<string, Button> _buttons = new();
    private string _current = "";


    public MainWindow()
    {
        InitializeComponent();
        var s = App.Services;
        var u = s.Session.CurrentUser!;
        AppTheme.LoadFor(u.Username);
        Views.PhoneAutoSync.Start();
        var root = (Control)Content!; Content = null;
        Content = _zoom = new LayoutTransformControl { Child = root };
        ApplyZoom();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) || e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift)) return;
            if (e.Key == Avalonia.Input.Key.K) { e.Handled = true; OpenSearch(); return; }
            if (e.Key == Avalonia.Input.Key.B) { e.Handled = true; SetMini(!_mini); return; }
            if (e.Key is Avalonia.Input.Key.OemPlus or Avalonia.Input.Key.Add) { Zoom(0.1); e.Handled = true; }
            else if (e.Key is Avalonia.Input.Key.OemMinus or Avalonia.Input.Key.Subtract) { Zoom(-0.1); e.Handled = true; }
            else if (e.Key is Avalonia.Input.Key.D0 or Avalonia.Input.Key.NumPad0) { Zoom(0); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        VersionText.Text = $"Phiên bản {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        WarehouseText.Text = s.Settings.Get(SettingsService.WarehouseName, "Quản lý kho");

        _menu = new()
        {
            ("TỔNG QUAN", new[]
            {
                new NavItem("lx", "Tổng quan", () => new TongQuanView()),
            }),
            ("NHẬP LIỆU", new[]
            {
                new NavItem("m6", "Nhập liệu", () => new EntryView()),
            }),
            ("TỒN KHO", new[]
            {
                new NavItem("m1", "Tồn kho An An", () => new AnAnView()),
                new NavItem("m2", "Tồn kho gửi", () => new GuiView()),
                new NavItem("m7", "Tồn kho theo vị trí", () => new ViTriView()),
                new NavItem("m3sk", "NXT Bột/Sốt", () => new SoKhoView()),
                new NavItem("m1vtb", "Vị Trí Bột", () => new VtbView()),
                new NavItem("m8", "Sơ đồ kho", () => new SoDoView()),
            }),
            ("CHỨC NĂNG", new[]
            {
                new NavItem("m4", "Báo cáo rã đông", () => new RaDongView()),
                new NavItem("m5", "Đối chiếu hàng ngày", () => new DoiChieuView()),
                new NavItem("m9", "Đối chiếu kiểm kê", () => new KiemKeView()),
                new NavItem("m7ck", "Số liệu cuối kỳ", () => new CuoiKyView()),
                new NavItem("m3", "Tổng hợp", () => new TongHopView()),
                new NavItem("m3dm", "Danh mục mã hàng", () => new DanhMucMaView()),
                new NavItem("m10", "Kết quả kháng sinh", () => new KhangSinhView()),
            }),
            ("CHỨNG TỪ", new[]
            {
                new NavItem("m13", "Phiếu xuất kho", () => new PhieuKhoView()),
                new NavItem("m12", "In chứng từ kho", () => new ChungTuView()),
                new NavItem("m11", "Phiếu xuất hệ thống", () => new PxhtView()),
            }),
            ("CÔNG CỤ", new[]
            {
                new NavItem("m14", "Container 3D", () => new C3dView()),
                new NavItem("m15", "Scan tài liệu", () => new ScanView()),
                new NavItem("m21", "Xử lý PDF & Excel", () => new XuLyTepView()),
                new NavItem("m22", "So sánh & Tra cứu bảng", () => new SoSanhBangView()),
            }),
            ("HỆ THỐNG", new[]
            {
                new NavItem("users", "Tài khoản", () => new UsersView()),
                new NavItem("backup", "Sao lưu và cài đặt", () => new BackupView()),
                new NavItem("lan", "Mạng LAN", () => new LanView()),
                new NavItem("drive", "Đồng bộ Google Drive", () => new DriveSyncView()),
                new NavItem("phone", "Đồng bộ điện thoại", () => new PhoneSyncView()),
                new NavItem("audit", "Nhật ký thao tác", () => new AuditView()),
                new NavItem("troly", "Trợ lý hệ thống", () => new TroLyView()),
            }),
        };
        // Thanh bên trái (1.44): nhóm → các mục (biểu tượng + tên + số việc cần xử lý). Thu gọn chỉ còn biểu tượng; rê chuột hiện tên ngay.
        // Module mới: thêm NavItem vào nhóm (hoặc nhóm mới) + biểu tượng trong NavIcons, thêm mục quyền trong Perm.Items.
        BuildSide();
        SetMini(s.Settings.Get(KeyMini(u.Username)) == "1", save: false);
        SearchButton.Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children =
        {
            Controls.Icon.Make("search", 15), new TextBlock { Text = "Tìm nhanh", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            new Border { BorderBrush = AppTheme.Brush("#CDD5DF"), BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(4), Padding = new Avalonia.Thickness(5, 0),
                Child = new TextBlock { Text = "Ctrl K", FontSize = 11, Classes = { "muted" } } },
        } };
        var initials = string.Concat((string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName).Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeLast(2).Select(w => char.ToUpper(w[0])));
        UserButton.Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children =
        {
            new Border { Width = 28, Height = 28, CornerRadius = new Avalonia.CornerRadius(14), Background = AppTheme.Brush("#E6F0FC"),
                Child = new TextBlock { Text = initials, FontSize = 11.5, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = AppTheme.Brush("#1859AE"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
            new TextBlock { Text = u.Username, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            Controls.Icon.Make("chevron-down", 13),
        } };
        ToolTip.SetTip(UserButton, $"{u.FullName} · {u.RoleLabel}");
        UserInfo.Header = $"{(string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName)} ({u.RoleLabel})";
        // Cập nhật online: có bản mới đã tải xong → hiện nút trên thanh menu.
        AppUpdater.Ready += OnUpdateReady;
        Closed += (_, _) => AppUpdater.Ready -= OnUpdateReady;
        if (AppUpdater.StagedVersion is { } sv) OnUpdateReady(sv);
        if (!_autoStarted) { _autoStarted = true; AppUpdater.StartAuto(); }
        s.Lan.Changed += OnLanChanged;
        Closed += (_, _) => s.Lan.Changed -= OnLanChanged;
        _lanTimer.Tick += (_, _) => ShowLan();
        _lanTimer.Start();
        Closed += (_, _) => _lanTimer.Stop();
        ShowLan();
        // Lịch xuất: tự quét Gmail cập nhật lịch (tài khoản có quyền «Duyệt email Lịch xuất», đã khai báo Gmail, bật tự quét).
        s.LichXuat.StartAutoEmail();
        s.LichXuat.Auto = true; // tự tạo List (/ Phiếu) theo Lịch xuất — nhật ký PNX_AutoLog dùng chung app cũ
        s.LichXuat.AutoMade += OnAutoMade;
        Closed += (_, _) => s.LichXuat.AutoMade -= OnAutoMade;
        Closed += (_, _) => s.LichXuat.StopAutoEmail();
        // Trợ lý hệ thống: chỉ Quản trị — quét nhanh tự động 90 giây sau khi đăng nhập rồi mỗi 6 giờ; có cảnh báo Nặng thì hiện nút trên thanh menu.
        if (Roles.IsAdmin(s.Session.Role))
        {
            s.TroLy.Scanned += OnTroLyScanned;
            Closed += (_, _) => { s.TroLy.Scanned -= OnTroLyScanned; s.TroLy.StopAuto(); };
            s.TroLy.StartAuto(TimeSpan.FromSeconds(90), TimeSpan.FromHours(6));
            if (s.TroLy.Last is { } last) OnTroLyScanned(last);
        }
        // Số "Cần xử lý" của Danh mục mã hàng trên thanh bên: tính ngầm 30 giây sau khi mở app (như app cũ), không làm chậm lúc khởi động.
        if (s.Session.Can(KhoLanh.Core.DanhMuc.DanhMucService.PermKey))
            Avalonia.Threading.DispatcherTimer.RunOnce(async () =>
            {
                try { var n = await Task.Run(() => { var d = new KhoLanh.Core.DanhMuc.DanhMucService(() => s.Lan, s.Session, s.Audit, () => null); d.Load(); return d.XlNow().Count; }); SetBadge("m3dm", n); }
                catch { /* chỉ là số gợi ý, lỗi thì bỏ qua */ }
            }, TimeSpan.FromSeconds(30));
        Navigate(_menu.SelectMany(m => m.items).FirstOrDefault(Allowed)?.Key ?? "backup");
    }

    private readonly Avalonia.Threading.DispatcherTimer _lanTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private void OnLanChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(ShowLan);

    /// <summary>Ô trạng thái LAN trên thanh menu: xanh = nối tốt / đang làm máy chủ, cam = làm riêng (mất máy chủ) hoặc đang chuyển.</summary>
    private void ShowLan()
    {
        var lan = App.Services.Lan;
        LanPill.IsVisible = lan.Enabled;
        if (!lan.Enabled) return;
        LanText.Text = lan.Short;
        LanText.Foreground = AppTheme.Brush("#C8D4E3", "#A9B5C3");
        if (_mini) ToolTip.SetTip(LanPill, "Mạng LAN: " + lan.Short);
        LanDot.Fill = AppTheme.Brush(lan.Health == 2 ? "#F0A030" : "#3FB950");
        ToolTip.SetTip(LanPill, lan.Health == 2
            ? "Không nối được máy chủ: vẫn làm việc bình thường trên dữ liệu của máy này; có máy chủ lại sẽ tự gửi lên và gộp."
            : "Đang dùng chung dữ liệu qua mạng LAN." + (lan.LastSync.Length > 0 ? "\n" + lan.LastSync : ""));
    }

    private void OnLanPill(object? sender, RoutedEventArgs e) { if (Roles.IsAdmin(App.Services.Session.Role)) Navigate("lan"); }

    private static bool _autoStarted;

    private void OnTroLyScanned(KhoLanh.Core.TroLy.TroLyService.ScanResult r) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        TroLyButton.IsVisible = r.Do > 0;
        TroLyButton.Content = Controls.Icon.Label($"Trợ lý: {r.Do}", "warn");
        ToolTip.SetTip(TroLyButton, $"Lần quét lúc {r.At:HH:mm}: {r.Do} cảnh báo nặng, {r.Vang} cần xem. Bấm để mở Trợ lý hệ thống.");
    });

    private void OnTroLyClick(object? sender, RoutedEventArgs e) => Navigate("troly");

    private int _autoN;
    /// <summary>Lịch xuất vừa tự tạo List / Phiếu → nút ⚡ trên thanh menu (bấm mở tab "Tự động đã tạo").</summary>
    private void OnAutoMade(List<string> msgs) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        var made = msgs.Count(m => m.Contains("tạo List") || m.Contains("List TRỐNG") || m.Contains("tạo phiếu"));
        if (made == 0 && !msgs.Any(m => m.StartsWith("Chưa tự tạo"))) return;
        _autoN += made;
        AutoButton.Content = Controls.Icon.Label(_autoN > 0 ? $"Tự tạo: {_autoN}" : "Lỗi tự tạo", "bolt");
        ToolTip.SetTip(AutoButton, string.Join("\n", msgs.Take(12)) + "\n\nBấm để xem ở Phiếu xuất kho › Tự động đã tạo.");
        AutoButton.IsVisible = true;
    });
    private void OnAutoClick(object? sender, RoutedEventArgs e) { AutoButton.IsVisible = false; _autoN = 0; Show("m13", new Views.PhieuKhoView(3)); }

    private void OnUpdateReady(Version v) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        UpdateButton.Content = Controls.Icon.Label($"Cập nhật {v}", "download");
        ToolTip.SetTip(UpdateButton, $"Đã tải xong bản {v}. Bấm để dùng ngay (app tự mở lại, dữ liệu giữ nguyên).");
        UpdateButton.IsVisible = true;
    });

    private async void OnUpdateClick(object? sender, RoutedEventArgs e)
    {
        var v = AppUpdater.StagedVersion;
        if (v is null) { UpdateButton.IsVisible = false; return; }
        if (!await Ui.Confirm(this, $"Cập nhật lên bản {v} ngay bây giờ?\n\nPhần mềm sẽ tự đóng và mở lại sau vài giây. Dữ liệu giữ nguyên. Nhớ lưu việc đang làm dở trước.", "Cập nhật phần mềm", "Cập nhật")) return;
        if (await Ui.Run(this, () => { if (!AppUpdater.ApplyAndRestart()) throw new KhoLanh.Core.Services.BusinessException("Không áp dụng được bản mới — thử lại sau khi mở lại phần mềm."); }))
            AppUpdater.ExitForUpdate();
    }

    // ------------------------------------------------------------------ thanh bên

    /// <summary>Biểu tượng từng module (tên trong <see cref="Controls.Icon.Paths"/>).</summary>
    private static readonly Dictionary<string, string> NavIcons = new()
    {
        ["lx"] = "home", ["m6"] = "edit",
        ["m1"] = "box", ["m2"] = "warehouse", ["m7"] = "pin", ["m3sk"] = "list", ["m1vtb"] = "map", ["m8"] = "grid",
        ["m4"] = "snow", ["m5"] = "calendar", ["m9"] = "check", ["m7ck"] = "chart", ["m3"] = "doc", ["m3dm"] = "tag", ["m10"] = "ok",
        ["m13"] = "truck", ["m12"] = "print", ["m11"] = "layers",
        ["m14"] = "cube", ["m15"] = "scan", ["m21"] = "tool", ["m22"] = "compare",
        ["users"] = "users", ["backup"] = "save", ["lan"] = "network", ["drive"] = "cloud", ["phone"] = "phone", ["audit"] = "history", ["troly"] = "assist",
    };
    private readonly Dictionary<string, (Button B, TextBlock Name, Border Badge, Border Dot, string Label)> _side = new();
    private readonly List<Control> _groupHeads = new();
    private bool _mini;
    private static string KeyMini(string user) => "ui_nav_mini:" + user.ToLowerInvariant();

    private void BuildSide()
    {
        NavPanel.Children.Clear(); _side.Clear(); _groupHeads.Clear(); _buttons.Clear();
        var first = true;
        foreach (var (group, items) in _menu)
        {
            var visible = items.Where(Allowed).ToList();
            if (visible.Count == 0) continue;
            // nhóm 1 mục (Tổng quan, Nhập liệu) không cần tiêu đề nhóm
            if (visible.Count > 1 || !first)
            {
                var head = new TextBlock { Text = group, Classes = { "sidegrp" } };
                var line = new Border { Classes = { "sideline" } };
                _groupHeads.Add(head); _groupHeads.Add(line);
                if (visible.Count > 1) { NavPanel.Children.Add(head); NavPanel.Children.Add(line); }
            }
            first = false;
            foreach (var item in visible)
            {
                var name = new TextBlock { Text = item.Label, Classes = { "sidelbl" }, Margin = new Avalonia.Thickness(10, 0, 0, 0), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                var badgeText = new TextBlock { FontSize = 11, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.White };
                var badge = new Border { Classes = { "sidebadge" }, IsVisible = false, Child = badgeText };
                var dot = new Border { Classes = { "sidedot" }, IsVisible = false };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto") };
                var ic = Controls.Icon.Make(NavIcons.GetValueOrDefault(item.Key, "doc"), 17);
                grid.Children.Add(new Panel { Children = { ic, dot } });
                Grid.SetColumn(name, 1); grid.Children.Add(name);
                Grid.SetColumn(badge, 2); grid.Children.Add(badge);
                var b = new Button { Classes = { "side" }, Content = grid };
                ToolTip.SetShowDelay(b, 0); ToolTip.SetPlacement(b, PlacementMode.Right); ToolTip.SetHorizontalOffset(b, 8);
                b.Click += (_, _) => Navigate(item.Key);
                _side[item.Key] = (b, name, badge, dot, item.Label);
                _buttons[item.Key] = b;
                NavPanel.Children.Add(b);
            }
        }
        if (_current.Length > 0 && _buttons.TryGetValue(_current, out var cur)) cur.Classes.Add("active");
    }

    /// <summary>Thu gọn (chỉ biểu tượng) / mở rộng thanh bên; lưu theo tài khoản trên máy này.</summary>
    internal void SetMini(bool mini, bool save = true)
    {
        _mini = mini;
        Side.Width = mini ? 60 : 232;
        LogoText.IsVisible = !mini;
        LogoRow.Margin = mini ? new Avalonia.Thickness(15, 14, 0, 10) : new Avalonia.Thickness(14, 14, 10, 10);
        foreach (var c in _groupHeads) c.Classes.Set("mini", mini);
        foreach (var (key, x) in _side)
        {
            x.Name.IsVisible = !mini;
            var n = x.Badge.Child is TextBlock t && int.TryParse(t.Text, out var v) ? v : 0;
            x.Badge.IsVisible = !mini && n > 0; x.Dot.IsVisible = mini && n > 0;
            ToolTip.SetTip(x.B, mini ? x.Label + (n > 0 ? $" · {n} cần xử lý" : "") : null);
        }
        foreach (var c in new Control[] { LanText }) c.IsVisible = !mini;
        ToolTip.SetShowDelay(LanPill, 0); ToolTip.SetPlacement(LanPill, PlacementMode.Right);
        CollapseButton.Content = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*"), Children =
        {
            Controls.Icon.Make(mini ? "chevron-right" : "chevron-left", 16),
            new TextBlock { Text = "Thu gọn menu", Classes = { "sidelbl" }, Margin = new Avalonia.Thickness(10, 0, 0, 0), IsVisible = !mini }.Also(t => Grid.SetColumn(t, 1)),
        } };
        ToolTip.SetShowDelay(CollapseButton, 0); ToolTip.SetPlacement(CollapseButton, PlacementMode.Right);
        ToolTip.SetTip(CollapseButton, mini ? "Mở rộng menu (Ctrl + B)" : "Thu gọn menu (Ctrl + B)");
        if (save) App.Services.Settings.Set(KeyMini(App.Services.Session.Username), mini ? "1" : "");
    }
    private void OnCollapseClick(object? sender, RoutedEventArgs e) => SetMini(!_mini);

    /// <summary>Số việc cần xử lý cạnh tên module (0 = ẩn). Khi thu gọn hiện chấm cam trên biểu tượng.</summary>
    public void SetBadge(string key, int n)
    {
        if (!_side.TryGetValue(key, out var x)) return;
        if (x.Badge.Child is TextBlock t) t.Text = n > 0 ? (n > 99 ? "99+" : n.ToString()) : "";
        x.Badge.IsVisible = !_mini && n > 0; x.Dot.IsVisible = _mini && n > 0;
        if (_mini) ToolTip.SetTip(x.B, x.Label + (n > 0 ? $" · {n} cần xử lý" : ""));
    }

    private static string GroupLabel(string group) =>
        string.Concat(group[..1], group[1..].ToLower(System.Globalization.CultureInfo.GetCultureInfo("vi-VN")));


    public void Navigate(string key) => Show(key, null);

    /// <summary>Mở một màn hình; nếu truyền sẵn control thì dùng control đó.</summary>
    public void Show(string key, Control? page)
    {
        var item = _menu.SelectMany(m => m.items).FirstOrDefault(i => i.Key == key);
        if (item is null) return;
        if (!Allowed(item)) return;
        page ??= item.Create();
        PageHost.IsVisible = true;
        PageHost.Content = page;
        if (page is IPage p) { PageTitle.Text = p.Title; PageSubtitle.Text = p.Subtitle; }
        foreach (var (k, b) in _buttons) { if (k == key) b.Classes.Add("active"); else b.Classes.Remove("active"); }
        _current = key;
    }

    public void SetSubtitle(string text) => PageSubtitle.Text = text;

    private async void OnChangePassword(object? sender, RoutedEventArgs e)
    {
        var oldPw = await Ui.Prompt(this, "Mật khẩu hiện tại:", "Đổi mật khẩu", password: true);
        if (oldPw is null) return;
        var newPw = await Ui.Prompt(this, "Mật khẩu mới (ít nhất 6 ký tự):", "Đổi mật khẩu", password: true);
        if (newPw is null) return;
        var again = await Ui.Prompt(this, "Nhập lại mật khẩu mới:", "Đổi mật khẩu", password: true);
        if (again is null) return;
        if (again != newPw) { await Ui.Message(this, "Hai lần nhập mật khẩu mới không khớp."); return; }
        if (await Ui.Run(this, () => App.Services.Auth.ChangeOwnPassword(oldPw, newPw)))
            await Ui.Message(this, "Đã đổi mật khẩu.");
    }

    private void OnSearchClick(object? sender, RoutedEventArgs e) => OpenSearch();
    private Views.TimNhanhWindow? _search;
    /// <summary>Tìm nhanh toàn app (Ctrl + K).</summary>
    public Views.TimNhanhWindow OpenSearch(string text = "")
    {
        if (_search is { } open) { open.Activate(); return open; }
        var mods = _menu.SelectMany(m => m.items.Where(Allowed).Select(i => new Views.TimNhanhWindow.ModuleLink(i.Key, i.Label, GroupLabel(m.group)))).ToList();
        var w = _search = new Views.TimNhanhWindow(mods, Navigate, text, (k, c) => Show(k, c));
        w.Closed += (_, _) => _search = null;
        Ui.ApplyZoom(w);
        w.Show(this);
        return w;
    }

    private readonly LayoutTransformControl _zoom;
    private void ApplyZoom() => _zoom.LayoutTransform = Math.Abs(AppTheme.Scale - 1) < 0.001 ? null : new Avalonia.Media.ScaleTransform(AppTheme.Scale, AppTheme.Scale);

    /// <summary>Giao diện Sáng dịu / Tối + cỡ chữ 90–130 % — lưu theo tài khoản trên máy này, áp dụng ngay.</summary>
    private async void OnThemeSettings(object? sender, RoutedEventArgs e)
    {
        var mode0 = AppTheme.Current; var scale0 = AppTheme.Scale; var dens0 = AppTheme.Density;
        var dens = new ComboBox { ItemsSource = AppTheme.Densities.Select(x => x.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(AppTheme.Densities, x => x.Key == dens0)), MinWidth = 220 };
        var light = new RadioButton { Content = "Sáng dịu — nền xám lạnh nhạt, đỡ chói", GroupName = "th", IsChecked = mode0 == AppTheme.Mode.Light };
        var dark = new RadioButton { Content = "Tối — nền xám đen", GroupName = "th", IsChecked = mode0 == AppTheme.Mode.Dark };
        var scales = new[] { 0.9, 1.0, 1.1, 1.2, 1.3 };
        var size = new ComboBox { ItemsSource = scales.Select(x => $"{x * 100:0} %" + (x == 1.0 ? " (chuẩn)" : "")).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(scales, x => Math.Abs(x - scale0) < 0.001)), MinWidth = 160 };
        void Preview() { AppTheme.SetDensity(AppTheme.Densities[Math.Max(0, dens.SelectedIndex)].Key); AppTheme.Apply(dark.IsChecked == true ? AppTheme.Mode.Dark : AppTheme.Mode.Light, scales[Math.Max(0, size.SelectedIndex)]); ApplyZoom(); }
        light.IsCheckedChanged += (_, _) => Preview(); size.SelectionChanged += (_, _) => Preview(); dens.SelectionChanged += (_, _) => Preview();
        var ok = new Button { Content = "Lưu", Classes = { "primary" }, IsDefault = true }; var cancel = new Button { Content = "Hủy", IsCancel = true };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = "Bộ màu", FontWeight = Avalonia.Media.FontWeight.SemiBold }); body.Children.Add(light); body.Children.Add(dark);
        body.Children.Add(new TextBlock { Text = "Cỡ chữ (thu phóng toàn bộ màn hình)", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 6, 0, 0) }); body.Children.Add(size);
        body.Children.Add(new TextBlock { Text = "Lưu riêng cho tài khoản này trên máy này. Phím tắt: Ctrl + / Ctrl − / Ctrl 0.", Classes = { "muted" }, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Mật độ bảng (chiều cao dòng)", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 6, 0, 0) });
        body.Children.Add(dens);
        body.Children.Add(Ui.Buttons(cancel, ok));
        var w = Ui.Dialog("Giao diện & cỡ chữ", body, 400); var saved = false;
        ok.Click += (_, _) => { saved = true; w.Close(); }; cancel.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (!saved) { AppTheme.SetDensity(dens0); AppTheme.Apply(mode0, scale0); ApplyZoom(); }
        else AppTheme.SaveFor(App.Services.Session.Username);
        ReloadPage();
    }

    /// <summary>Dựng lại màn hình đang mở (để các ô vẽ tay lấy màu mới).</summary>
    private void ReloadPage() { if (_current.Length > 0 && PageHost.Content is Control) Show(_current, null); }

    private void Zoom(double delta)
    {
        var sc = delta == 0 ? 1.0 : AppTheme.Scale + delta;
        AppTheme.Apply(AppTheme.Current, sc); ApplyZoom(); AppTheme.SaveFor(App.Services.Session.Username);
    }

    private async void OnBootSettings(object? sender, RoutedEventArgs e) => await BootSettings.ShowDialog(this);

    private void OnLogout(object? sender, RoutedEventArgs e) => Logout();

    public void Logout()
    {
        IsLoggingOut = true;
        App.Services.Auth.Logout();
        LoggedOut?.Invoke();
    }

    public static MainWindow? Of(Control c) => TopLevel.GetTopLevel(c) as MainWindow;
}

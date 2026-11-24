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
                new NavItem("m13", "Phiếu xuất kho", () => new PhieuKhoView()),
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
        // Menu ngang 2 tầng (1.54): tầng 2 = nhóm (nhóm 1 mục bấm mở luôn, nhóm nhiều mục thả xuống chỉ có tên module) + số việc cần xử lý.
        // Module mới: thêm NavItem vào nhóm (hoặc nhóm mới) + biểu tượng trong NavIcons, thêm mục quyền trong Perm.Items.
        BuildNav();
        HelpButton.Content = Controls.Icon.Make("info", 17);
        var sg = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        sg.Children.Add(Controls.Icon.Make("search", 15));
        sg.Children.Add(new TextBlock { Text = "Tìm mã hàng, LSX, phiếu, vị trí hoặc module…", Margin = new Avalonia.Thickness(9, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis }.Also(t => Grid.SetColumn(t, 1)));
        sg.Children.Add(new Border { Classes = { "kbd" }, Child = new TextBlock { Text = "Ctrl K", FontSize = 11 } }.Also(t => Grid.SetColumn(t, 2)));
        SearchButton.Content = sg;
        var initials = string.Concat((string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName).Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeLast(2).Select(w => char.ToUpper(w[0])));
        UserButton.Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children =
        {
            new Border { Width = 28, Height = 28, CornerRadius = new Avalonia.CornerRadius(14), Background = AppTheme.Brush("#1F6FD6"),
                Child = new TextBlock { Text = initials, FontSize = 11.5, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
            new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Children =
            {
                new TextBlock { Text = string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName, FontSize = 12.5, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.White, MaxWidth = 160, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis },
                new TextBlock { Text = u.RoleLabel, FontSize = 10.5, Foreground = AppTheme.Brush("#8FA0B6", "#7A8797") },
            } },
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

    // ------------------------------------------------------------------ menu ngang

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
    /// <summary>Biểu tượng nhóm trên tầng 2 của menu.</summary>
    private static readonly Dictionary<string, string> GroupIcons = new()
    {
        ["TỒN KHO"] = "box", ["CHỨC NĂNG"] = "chart", ["CHỨNG TỪ"] = "doc", ["CÔNG CỤ"] = "tool", ["HỆ THỐNG"] = "gear",
    };
    private sealed class NavGroup
    {
        public Button Head = null!; public TextBlock Count = null!; public Border CountBox = null!; public List<string> Keys = new();
    }
    private readonly Dictionary<string, NavGroup> _navGroups = new();          // key module → nhóm
    private readonly Dictionary<string, (TextBlock Text, Border Box)> _badges = new();
    private readonly Dictionary<string, int> _badgeN = new();
    private readonly Dictionary<string, MenuItem> _menuItems = new();

    private static Border CountBadge(out TextBlock t)
    {
        t = new TextBlock { FontSize = 11, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Avalonia.Media.Brushes.White };
        return new Border { Classes = { "navbadge" }, IsVisible = false, Child = t };
    }

    private void BuildNav()
    {
        NavPanel.Children.Clear(); _buttons.Clear(); _navGroups.Clear(); _badges.Clear(); _menuItems.Clear();
        foreach (var (group, items) in _menu)
        {
            var visible = items.Where(Allowed).ToList();
            if (visible.Count == 0) continue;
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            var cnt = CountBadge(out var cntText);
            if (visible.Count == 1)
            {
                var item = visible[0];
                row.Children.Add(Controls.Icon.Make(NavIcons.GetValueOrDefault(item.Key, "doc"), 16));
                row.Children.Add(new TextBlock { Text = item.Label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                row.Children.Add(cnt);
                var b = new Button { Classes = { "navtab" }, Content = row };
                b.Click += (_, _) => Navigate(item.Key);
                _buttons[item.Key] = b; _badges[item.Key] = (cntText, cnt);
                NavPanel.Children.Add(b);
                continue;
            }
            row.Children.Add(Controls.Icon.Make(GroupIcons.GetValueOrDefault(group, "menu"), 16));
            row.Children.Add(new TextBlock { Text = GroupLabel(group), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            row.Children.Add(cnt);
            row.Children.Add(Controls.Icon.Make("chevron-down", 12));
            var head = new Button { Classes = { "navtab" }, Content = row };
            var g = new NavGroup { Head = head, Count = cntText, CountBox = cnt, Keys = visible.Select(i => i.Key).ToList() };
            var fl = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            foreach (var item in visible)
            {
                var ib = CountBadge(out var it);
                var ig = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinWidth = 210 };
                ig.Children.Add(Controls.Icon.Make(NavIcons.GetValueOrDefault(item.Key, "doc"), 16));
                ig.Children.Add(new TextBlock { Text = item.Label, Margin = new Avalonia.Thickness(10, 0, 12, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }.Also(t => Grid.SetColumn(t, 1)));
                Grid.SetColumn(ib, 2); ig.Children.Add(ib);
                var mi = new MenuItem { Header = ig, Classes = { "navitem" } };
                mi.Click += (_, _) => Navigate(item.Key);
                fl.Items.Add(mi);
                _menuItems[item.Key] = mi; _badges[item.Key] = (it, ib);
                _buttons[item.Key] = head; _navGroups[item.Key] = g;
            }
            head.Flyout = fl;
            NavPanel.Children.Add(head);
        }
        if (_current.Length > 0 && _buttons.TryGetValue(_current, out var cur)) cur.Classes.Add("active");
        foreach (var (k, n) in _badgeN.ToList()) SetBadge(k, n);
    }

    /// <summary>Số việc cần xử lý cạnh tên module (0 = ẩn); nhóm thả xuống hiện tổng số của các mục bên trong.</summary>
    public void SetBadge(string key, int n)
    {
        _badgeN[key] = n;
        if (!_badges.TryGetValue(key, out var x)) return;
        x.Text.Text = n > 99 ? "99+" : n.ToString(); x.Box.IsVisible = n > 0;
        if (_navGroups.TryGetValue(key, out var g))
        {
            var sum = g.Keys.Sum(k => _badgeN.GetValueOrDefault(k));
            g.Count.Text = sum > 99 ? "99+" : sum.ToString(); g.CountBox.IsVisible = sum > 0;
        }
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
        foreach (var b in _buttons.Values) b.Classes.Remove("active");
        if (_buttons.TryGetValue(key, out var act)) act.Classes.Add("active");
        foreach (var (k, mi) in _menuItems) mi.Classes.Set("current", k == key);
        var grp = _menu.First(m => m.items.Contains(item)).group;
        PageGroup.Text = _navGroups.ContainsKey(key) ? GroupLabel(grp) + "  ›" : "";
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

    /// <summary>Nút "Hướng dẫn": hiện các ghi chú đã thu gọn của trang đang mở (<see cref="Controls.HintTrim"/>).</summary>
    private void OnHelpClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ShowHelp();
    internal Flyout ShowHelp()
    {
        var hints = PageHost.Content is Avalonia.Visual v ? Controls.HintTrim.Collect(v) : new List<string>();
        var fl = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight, Content = Controls.HintTrim.Panel(hints) };
        fl.ShowAt(HelpButton);
        return fl;
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

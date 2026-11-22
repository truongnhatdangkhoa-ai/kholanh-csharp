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
    private static bool Allowed(NavItem i) => i.Key is "users" or "lan" ? Roles.IsAdmin(App.Services.Session.Role) : App.Services.Session.Can(i.Key);

    private readonly List<(string group, NavItem[] items)> _menu;
    private readonly Dictionary<string, Button> _buttons = new();
    private string _current = "";
    private readonly Dictionary<string, ModuleView> _modules = new();

    private ModuleView Module(string key, string page, string title, string subtitle)
    {
        if (!_modules.TryGetValue(key, out var v)) _modules[key] = v = new ModuleView(page, title, subtitle);
        return v;
    }

    public MainWindow()
    {
        InitializeComponent();
        var s = App.Services;
        var u = s.Session.CurrentUser!;
        VersionText.Text = $"Phiên bản {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        WarehouseText.Text = s.Settings.Get(SettingsService.WarehouseName, "Quản lý kho");

        _menu = new()
        {
            ("TỒN KHO GỐC", new[]
            {
                new NavItem("m1", "Tồn kho An An", () => Module("m1", "ton-kho-an-an.html", "Tồn kho An An",
                    "Nạp file tồn gốc, phân loại 8 nhóm, báo cáo Tổng Tồn / HLSO — chạy nguyên mã module của app cũ.")),
                new NavItem("m2", "Tồn kho gửi", () => Module("m2", "ton-kho-gui.html", "Tồn kho gửi",
                    "Nạp file tồn kho gửi, phân loại, phiếu xuất, Đặc tính — chạy nguyên mã module của app cũ.")),
            }),
            ("BÁO CÁO", new[]
            {
                new NavItem("m3", "Tổng hợp", () => new TongHopView()),
                new NavItem("m4", "Báo cáo rã đông", () => new RaDongView()),
            }),
            ("NHẬP LIỆU", new[]
            {
                new NavItem("m6", "Nhập liệu", () => new EntryView()),
            }),
            ("HỆ THỐNG", new[]
            {
                new NavItem("users", "Tài khoản", () => new UsersView()),
                new NavItem("backup", "Sao lưu và cài đặt", () => new BackupView()),
                new NavItem("lan", "Mạng LAN", () => new LanView()),
                new NavItem("audit", "Nhật ký thao tác", () => new AuditView()),
            }),
        };
        // Menu ngang: các module trải phẳng thành nút; nhóm Hệ thống là nút thả xuống.
        // Module mới: thêm NavItem vào nhóm (hoặc nhóm mới), thêm mục quyền trong Perm.Items.
        foreach (var (group, items) in _menu)
        {
            var visible = items.Where(Allowed).ToList();
            if (visible.Count == 0) continue;
            var flat = group != "HỆ THỐNG";
            if (flat)
            {
                foreach (var item in visible)
                {
                    var b = new Button { Content = item.Label, Classes = { "topnav" } };
                    b.Click += (_, _) => Navigate(item.Key);
                    _buttons[item.Key] = b;
                    NavPanel.Children.Add(b);
                }
                NavPanel.Children.Add(new Border { Width = 1, Height = 22, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A4D6B")), Margin = new Avalonia.Thickness(3, 0) });
            }
            else
            {
                var groupButton = new Button { Content = GroupLabel(group) + "  ▾", Classes = { "topnav" } };
                var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
                foreach (var item in visible)
                {
                    var mi = new MenuItem { Header = item.Label };
                    mi.Click += (_, _) => Navigate(item.Key);
                    flyout.Items.Add(mi);
                    _buttons[item.Key] = groupButton;
                }
                groupButton.Flyout = flyout;
                NavPanel.Children.Add(groupButton);
            }
        }
        if (NavPanel.Children.Count > 0 && NavPanel.Children[^1] is Border) NavPanel.Children.RemoveAt(NavPanel.Children.Count - 1);
        UserButton.Content = $"{u.Username}  ▾";
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
        LanText.Foreground = Avalonia.Media.Brushes.White;
        LanDot.Fill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(lan.Health == 2 ? "#F0A030" : "#3FB950"));
        ToolTip.SetTip(LanPill, lan.Health == 2
            ? "Không nối được máy chủ: vẫn làm việc bình thường trên dữ liệu của máy này; có máy chủ lại sẽ tự gửi lên và gộp."
            : "Đang dùng chung dữ liệu qua mạng LAN." + (lan.LastSync.Length > 0 ? "\n" + lan.LastSync : ""));
    }

    private void OnLanPill(object? sender, RoutedEventArgs e) { if (Roles.IsAdmin(App.Services.Session.Role)) Navigate("lan"); }

    private static bool _autoStarted;

    private void OnUpdateReady(Version v) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        UpdateButton.Content = $"⬆ Cập nhật {v}";
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
        if (page is ModuleView mv)
        {
            if (!ModuleLayer.Children.Contains(mv)) ModuleLayer.Children.Add(mv);
            foreach (var other in _modules.Values) if (other != mv) other.SetActive(false);
            PageHost.IsVisible = false;
            mv.SetActive(true);
        }
        else
        {
            foreach (var other in _modules.Values) other.SetActive(false);
            PageHost.IsVisible = true;
            PageHost.Content = page;
        }
        if (page is IPage p) { PageTitle.Text = p.Title; PageSubtitle.Text = p.Subtitle; }
        foreach (var b in _buttons.Values) b.Classes.Remove("active");
        if (_buttons.TryGetValue(key, out var now)) now.Classes.Add("active");
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

    private void OnLogout(object? sender, RoutedEventArgs e) => Logout();

    public void Logout()
    {
        IsLoggingOut = true;
        App.Services.Auth.Logout();
        LoggedOut?.Invoke();
    }

    public static MainWindow? Of(Control c) => TopLevel.GetTopLevel(c) as MainWindow;
}

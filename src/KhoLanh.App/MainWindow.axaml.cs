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

    private record NavItem(string Key, string Label, Func<Control> Create, Func<string, bool> Allowed);

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

        static bool Any(string _) => true;
        _menu = new()
        {
            ("TỔNG QUAN", new[] { new NavItem("dash", "Tổng quan", () => new DashboardView(), Any) }),
            ("NGHIỆP VỤ", new[]
            {
                new NavItem("in", "Phiếu nhập", () => new VoucherEditView(VoucherTypes.In), Any),
                new NavItem("out", "Phiếu xuất", () => new VoucherEditView(VoucherTypes.Out), Any),
                new NavItem("list", "Danh sách phiếu", () => new VoucherListView(), Any),
            }),
            ("KHO", new[]
            {
                new NavItem("stock", "Tồn kho", () => new StockView(), Any),
                new NavItem("ledger", "Sổ kho", () => new LedgerView(), Any),
                new NavItem("nxt", "Báo cáo NXT", () => new ReportView(), Any),
            }),
            ("TỒN KHO GỐC", new[]
            {
                new NavItem("m1", "Tồn kho An An", () => Module("m1", "ton-kho-an-an.html", "Tồn kho An An",
                    "Nạp file tồn gốc, phân loại 8 nhóm, báo cáo Tổng Tồn / HLSO — chạy nguyên mã module của app cũ."), Any),
                new NavItem("m2", "Tồn kho gửi", () => Module("m2", "ton-kho-gui.html", "Tồn kho gửi",
                    "Nạp file tồn kho gửi, phân loại, phiếu xuất, Đặc tính — chạy nguyên mã module của app cũ."), Any),
            }),
            ("DANH MỤC", new[]
            {
                new NavItem("products", "Mã hàng", () => new ProductsView(), Any),
                new NavItem("owners", "Chủ hàng", () => new OwnersView(), Any),
            }),
            ("HỆ THỐNG", new[]
            {
                new NavItem("users", "Tài khoản", () => new UsersView(), Roles.IsAdmin),
                new NavItem("backup", "Sao lưu và cài đặt", () => new BackupView(), Roles.CanManage),
                new NavItem("audit", "Nhật ký thao tác", () => new AuditView(), Roles.CanManage),
            }),
        };
        // Menu ngang: nhóm có 1 mục là nút thẳng; nhóm nhiều mục (Danh mục, Hệ thống) là nút thả xuống.
        // Nhóm NGHIỆP VỤ và KHO được trải phẳng vì dùng nhiều nhất.
        foreach (var (group, items) in _menu)
        {
            var visible = items.Where(i => i.Allowed(u.Role)).ToList();
            if (visible.Count == 0) continue;
            var flat = group is "TỔNG QUAN" or "NGHIỆP VỤ" or "KHO" or "TỒN KHO GỐC";
            if (flat)
            {
                foreach (var item in visible)
                {
                    var b = new Button { Content = item.Label, Classes = { "topnav" } };
                    var hotkey = item.Key switch { "in" => "F2", "out" => "F3", "stock" => "F4", _ => null };
                    if (hotkey is not null) ToolTip.SetTip(b, $"Phím tắt {hotkey}");
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
        KeyDown += OnKeyDown;
        Navigate("dash");
    }

    private static string GroupLabel(string group) =>
        string.Concat(group[..1], group[1..].ToLower(System.Globalization.CultureInfo.GetCultureInfo("vi-VN")));

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.Key switch { Key.F2 => "in", Key.F3 => "out", Key.F4 => "stock", _ => null };
        if (key is not null) { Navigate(key); e.Handled = true; }
    }

    public void Navigate(string key) => Show(key, null);

    /// <summary>Mở một màn hình; nếu truyền sẵn control thì dùng control đó.</summary>
    public void Show(string key, Control? page)
    {
        var item = _menu.SelectMany(m => m.items).FirstOrDefault(i => i.Key == key);
        if (item is null) return;
        if (!item.Allowed(App.Services.Session.Role)) return;
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

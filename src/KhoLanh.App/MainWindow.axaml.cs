using Avalonia;
using Avalonia.LogicalTree;
using Avalonia.Controls.Presenters;
using Avalonia.Controls;
using Avalonia.VisualTree;
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

/// <summary>1.72: màn hình KHÔNG giữ lại khi chuyển menu (luôn dựng mới khi mở).</summary>
public interface IKeepFresh { }

public partial class MainWindow : Window
{
    public event Action? LoggedOut;
    public bool IsLoggingOut { get; private set; }

    private record NavItem(string Key, string Label, Func<Control> Create);

    /// <summary>Mục "users" chỉ Quản trị; các mục khác theo phân quyền tài khoản (mức Xem trở lên).</summary>
    private static bool Allowed(NavItem i) => i.Key is "users" ? App.Services.Session.CanManageAccounts   // 1.110: Quản trị hoặc quyền "Tạo / sửa tài khoản"
        : i.Key is "troly" ? Roles.IsAdmin(App.Services.Session.Role)
        : i.Key is "dongbo" ? App.Services.Session.CurrentUser is not null   // 1.121: tab Máy chủ cho MỌI tài khoản (nội dung theo quyền)
        : i.Key is "ketnoi" ? Roles.IsAdmin(App.Services.Session.Role) || App.Services.Session.Can("backup") || App.Services.Session.Can("phone")
        : App.Services.Session.Can(i.Key);

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
        // 1.85: trang lấp đầy cửa sổ, nội dung cần cao hơn (mỗi bảng ≥ 8 dòng) thì cuộn dọc
        // (tab của trang nằm trên hàng tên trang; cuộn xuống thì dải tên trang che phần đã cuộn qua)
        var pi = PageArea.Children.IndexOf(PageHost);
        PageArea.Children.Remove(PageHost);
        var fs = new Controls.FillScroll(PageHost);
        PageArea.Children.Insert(pi, fs);
        fs.ScrollChanged += (_, _) => { var up = fs.Offset.Y > 0.5; PageBand.ZIndex = up ? 1 : 0; PageHead.ZIndex = up ? 2 : 0; };
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
        // 1.81: ESC = quay lại màn hình trước (tối đa 3 lần), hết thì về Tổng quan. Đăng ký dạng Bubble: ô bảng đang sửa,
        // danh sách lọc, hộp thoại… xử lý ESC trước (e.Handled) thì không chuyển trang. Đang gõ trong ô chữ: ESC đầu chỉ rời ô.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Handled || e.Key != Avalonia.Input.Key.Escape || e.KeyModifiers != Avalonia.Input.KeyModifiers.None) return;
            if (FocusManager?.GetFocusedElement() is TextBox) { PageHost.Focusable = true; PageHost.Focus(); e.Handled = true; return; }   // rời ô chữ (vùng trang nhận tiêu điểm)
            e.Handled = GoBack();
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        VersionItem.Header = $"Phiên bản {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        ToolTip.SetTip(LogoImage, "KHO LẠNH · " + s.Settings.Get(SettingsService.WarehouseName, "Quản lý kho"));
        PageHost.LayoutUpdated += (_, _) =>
        {
            if (_alignQueued) return;
            if (_pageTabs is { } t)
            {
                if (!t.IsAttachedToVisualTree()) QueueAlign();
                // 1.88: dải tab đã kéo lên bị ẩn (vd mở khung soạn phiếu chiếm cả trang) → trang lại nằm sát đầu, đè lên tên trang → xếp lại
                else if (!t.IsEffectivelyVisible) { _hiddenTabs = t; QueueAlign(); }
            }
            else if (_hiddenTabs is { } h && h.IsAttachedToVisualTree() && h.IsEffectivelyVisible) { _hiddenTabs = null; QueueAlign(); }   // hiện lại → kéo tab lên như cũ
        };

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
            ("PHIẾU XUẤT KHO", new[]
            {
                new NavItem("m13", "Phiếu xuất kho", () => new PhieuKhoView()),
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
                new NavItem("m24cc", "Chấm công", () => new ChamCongView()),
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
                new NavItem("m19", "Lưu trữ cá nhân", () => new LuuTruView()),
                new NavItem("m21", "Xử lý PDF & Excel", () => new XuLyTepView()),
                new NavItem("m22", "So sánh & Tra cứu bảng", () => new SoSanhBangView()),
            }),
            ("HỆ THỐNG", new[]
            {
                new NavItem("users", "Tài khoản", () => new UsersView()),
                // 1.105 (phương án A): Kết nối (mọi mã / Gmail / Google) · Đồng bộ & sao lưu (thay Dữ liệu & sao lưu + Đồng bộ điện thoại) · Cài đặt máy gọn
                new NavItem("ketnoi", "Kết nối", () => new KetNoiView()),
                new NavItem("dongbo", "Đồng bộ & sao lưu", () => new DongBoView()),
                new NavItem("backup", "Cài đặt máy", () => new BackupView()),
                new NavItem("audit", "Nhật ký thao tác", () => new AuditView()),
                new NavItem("troly", "Trợ lý hệ thống", () => new TroLyView()),
            }),
        };
        // Menu ngang 2 tầng (1.54): tầng 2 = nhóm (nhóm 1 mục bấm mở luôn, nhóm nhiều mục thả xuống chỉ có tên module) + số việc cần xử lý.
        // Module mới: thêm NavItem vào nhóm (hoặc nhóm mới) + biểu tượng trong NavIcons, thêm mục quyền trong Perm.Items.
        BuildNav();
        HelpButton.Content = Controls.Icon.Make("info", 17);
        // 1.97: biểu tượng tài khoản theo quyền (phương án B): ô màu + hình khiên / cặp / người, tên, nhãn quyền
        var shownName = string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName;
        UserButton.Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children =
        {
            Controls.RoleBadge.Line(u.Role, shownName, 26, 13),
            Controls.Icon.Make("chevron-down", 13),
        } };
        ToolTip.SetTip(UserButton, $"{u.FullName} · {u.RoleLabel}");
        UserInfo.Header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, Children =
        {
            Controls.RoleBadge.Avatar(u.Role, 34),
            new StackPanel { Spacing = 3, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Children =
            {
                new TextBlock { Text = shownName, FontSize = 14, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = AppTheme.Brush("#1A2433") },
                Controls.RoleBadge.Pill(u.Role),
            } },
        } };
        UserInfo.IsEnabled = true; UserInfo.IsHitTestVisible = false;   // hiện rõ (mục tắt bị làm mờ), không bấm được
        // Cập nhật online: có bản mới đã tải xong → hiện nút trên thanh menu.
        AppUpdater.Ready += OnUpdateReady;
        Closed += (_, _) => AppUpdater.Ready -= OnUpdateReady;
        if (AppUpdater.StagedVersion is { } sv) OnUpdateReady(sv);
        if (!_autoStarted) { _autoStarted = true; AppUpdater.StartAuto(); }
        s.Lan.Changed += OnLanChanged;
        try { s.PhoneSync.StartInstantSlipPush(); } catch { /* 1.72 (B6): đẩy phiếu PC lên điện thoại ngay khi lưu */ }
        Closed += (_, _) => s.Lan.Changed -= OnLanChanged;
        _lanTimer.Tick += (_, _) => ShowLan();
        _lanTimer.Start();
        Closed += (_, _) => _lanTimer.Stop();
        ShowLan();
        ChayAn.Install(this);   // 1.129: máy chủ — đóng cửa sổ = ẩn xuống khay, máy chủ vẫn chạy
        // Lịch xuất: tự quét Gmail cập nhật lịch (tài khoản có quyền «Duyệt email Lịch xuất», đã khai báo Gmail, bật tự quét).
        s.LichXuat.StartAutoEmail();
        s.LichXuat.Auto = true; // tự tạo List (/ Phiếu) theo Lịch xuất — nhật ký PNX_AutoLog dùng chung app cũ
        s.LichXuat.AutoMade += OnAutoMade;
        Closed += (_, _) => s.LichXuat.AutoMade -= OnAutoMade;
        Closed += (_, _) => s.LichXuat.StopAutoEmail();
        // Lưu trữ cá nhân: tự sao lưu Google Drive theo lịch của tài khoản (chỉ chạy khi đã đăng nhập Google trong module).
        if (s.LuuTru.CanUse) { s.LuuTru.StartAuto(); Closed += (_, _) => s.LuuTru.StopAuto(); }
        if (s.DriveCaNhan.CanUse) { s.DriveCaNhan.StartAuto(); Closed += (_, _) => s.DriveCaNhan.StopAuto(); }
        // Trợ lý giai đoạn 5: mọi máy (mọi tài khoản) ghi ảnh chụp tình trạng lên database dùng chung — 2 phút sau khi mở rồi mỗi 15 phút
        s.TroLy.StartStatus(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15)); Closed += (_, _) => s.TroLy.StopStatus();
        // Trợ lý hệ thống: chỉ Quản trị — quét nhanh tự động 90 giây sau khi đăng nhập rồi mỗi 6 giờ; có cảnh báo Nặng thì hiện nút trên thanh menu.
        if (Roles.IsAdmin(s.Session.Role))
        {
            s.TroLy.Scanned += OnTroLyScanned;
            Closed += (_, _) => { s.TroLy.Scanned -= OnTroLyScanned; s.TroLy.StopAuto(); };
            // 1.102: lần mở đầu sau cập nhật → quét sớm (20 giây) để báo ngay lỗi do bản mới
            _justUpdated = App.NoteVersion();
            s.TroLy.StartAuto(TimeSpan.FromSeconds(_justUpdated ? 20 : 90), TimeSpan.FromHours(6));
            if (s.TroLy.Last is { } last) OnTroLyScanned(last);
        }
        // Số "Cần xử lý" của Danh mục mã hàng trên thanh menu: tính ngầm 30 giây sau khi mở app (như app cũ), rồi tự tính lại khi
        // danh mục / Mã hóa / tồn (An An, Gửi, vị trí, Sổ kho) đổi — kể cả do máy khác qua LAN (1.72).
        if (s.Session.Can(KhoLanh.Core.DanhMuc.DanhMucService.PermKey))
        {
            Avalonia.Threading.DispatcherTimer.RunOnce(() => { RefreshDmBadge(); _dmTimer.Start(); }, TimeSpan.FromSeconds(30));
            _dmTimer.Tick += (_, _) => RefreshDmBadge(onlyIfChanged: true);
            Closed += (_, _) => _dmTimer.Stop();
        }
        Navigate(_menu.SelectMany(m => m.items).FirstOrDefault(Allowed)?.Key ?? "backup");
    }

    private readonly Avalonia.Threading.DispatcherTimer _lanTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private void OnLanChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(ShowLan);

    /// <summary>Dấu chấm trạng thái LAN trên thanh menu: xanh = nối tốt / đang làm máy chủ, cam = làm riêng (mất máy chủ) hoặc đang chuyển.</summary>
    private void ShowLan()
    {
        ChayAn.Sync();   // 1.129: vừa nhận / trả vai máy chủ → thêm / bỏ biểu tượng khay
        var lan = App.Services.Lan;
        LanPill.IsVisible = lan.Enabled;
        if (!lan.Enabled) return;
        // 1.65: chỉ còn dấu chấm (xanh = có kết nối, đỏ = mất máy chủ / làm riêng); chữ trạng thái chuyển vào chú thích khi rê chuột.
        LanDot.Fill = AppTheme.Brush(lan.Health == 2 ? "#E5534B" : "#3FB950");
        ToolTip.SetTip(LanPill, lan.Short + "\n" + (lan.Health == 2
            ? "Không nối được máy chủ: vẫn làm việc bình thường trên dữ liệu của máy này; có máy chủ lại sẽ tự gửi lên và gộp."
            : "Đang dùng chung dữ liệu qua mạng LAN." + (lan.LastSync.Length > 0 ? "\n" + lan.LastSync : "")));
    }

    private void OnLanPill(object? sender, RoutedEventArgs e) => Navigate("lan");   // 1.121: mọi tài khoản xem tab Máy chủ

    private static bool _autoStarted;

    private bool _justUpdated;
    private static HashSet<string>? _notified;
    /// <summary>Mã cảnh báo đã báo (lưu settings "troly_notified", ≤ 300) — không báo lại cùng cảnh báo mỗi lần mở app; tái phát thì mã gồm số lần.</summary>
    private static HashSet<string> Notified()
    {
        if (_notified is not null) return _notified;
        try { _notified = new(App.Services.Settings.Get("troly_notified").Split('|', StringSplitOptions.RemoveEmptyEntries)); } catch { _notified = new(); }
        return _notified;
    }
    private static void SaveNotified()
    {
        try { App.Services.Settings.Set("troly_notified", string.Join("|", Notified().TakeLast(300))); } catch { }
    }

    private void OnTroLyScanned(KhoLanh.Core.TroLy.TroLyService.ScanResult r) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        TroLyButton.IsVisible = r.Do > 0;
        TroLyButton.Content = Controls.Icon.Label($"{r.Do}", "warn");
        ToolTip.SetTip(TroLyButton, $"Trợ lý hệ thống · lần quét lúc {r.At:HH:mm}: {r.Do} cảnh báo nặng, {r.Vang} cần xem. Bấm để mở Trợ lý hệ thống.");
        NotifyFindings(r);
    });

    /// <summary>
    /// 1.102: cảnh báo Nặng MỚI (Mới / Tái phát, chưa báo trong phiên này) → thông báo góc màn hình, bấm mở Trợ lý. Lượt quét đầu sau cập nhật
    /// có lỗi phần mềm mới → nhắc có thể Quay lại bản trước.
    /// </summary>
    private void NotifyFindings(KhoLanh.Core.TroLy.TroLyService.ScanResult r)
    {
        var seen = Notified(); var n0 = seen.Count;
        string Key(KhoLanh.Core.TroLy.Finding f) => f.Id + "#" + f.Recur;
        var hot = r.Findings.Where(f => f.Sev == "do" && f.Q is "moi" or "taiPhat" && seen.Add(Key(f))).ToList();
        if (_justUpdated)
        {
            _justUpdated = false;
            var upd = App.UpdatedAt();
            var news = r.Findings.Where(f => f.Sev != "xanh" && f.Q is "moi" or "taiPhat" && f.QFirstAt >= upd
                && (f.Cat == "loi-app" || f.Title.StartsWith("Chậm hơn sau cập nhật"))).ToList();
            if (news.Count > 0 && AppUpdater.HasPrevious)
            {
                foreach (var f in news) seen.Add(Key(f));
                Ui.Notify(this, $"Bản {KhoLanh.Core.Services.UpdateService.Current.ToString(3)} có {news.Count} lỗi mới",
                    news[0].Title + "\nBấm để xem — có nút «Quay lại bản trước» nếu bản mới lỗi.",
                    Avalonia.Controls.Notifications.NotificationType.Warning, () => Navigate("troly"), 20);
                hot.RemoveAll(f => news.Contains(f));
            }
        }
        if (seen.Count != n0) SaveNotified();
        if (hot.Count == 0) return;
        Ui.Notify(this, hot.Count == 1 ? "Trợ lý: 1 cảnh báo nặng" : $"Trợ lý: {hot.Count} cảnh báo nặng",
            string.Join("\n", hot.Take(3).Select(f => "• " + f.Title)) + (hot.Count > 3 ? $"\n… và {hot.Count - 3} cảnh báo nữa" : "") + "\nBấm để mở Trợ lý hệ thống.",
            Avalonia.Controls.Notifications.NotificationType.Error, () => Navigate("troly"), 15);
    }

    private void OnTroLyClick(object? sender, RoutedEventArgs e) => Navigate("troly");

    private int _autoN;
    /// <summary>Lịch xuất vừa tự tạo List / Phiếu → nút ⚡ trên thanh menu (bấm mở tab "Tự động đã tạo").</summary>
    private void OnAutoMade(List<string> msgs) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        var made = msgs.Count(m => m.Contains("tạo List") || m.Contains("List TRỐNG") || m.Contains("tạo phiếu"));
        if (made == 0 && !msgs.Any(m => m.StartsWith("Chưa tự tạo"))) return;
        _autoN += made;
        AutoButton.Content = Controls.Icon.Label(_autoN > 0 ? $"{_autoN}" : "!", "bolt");
        ToolTip.SetTip(AutoButton, "Lịch xuất tự tạo List / phiếu:\n" + string.Join("\n", msgs.Take(12)) + "\n\nBấm để xem ở Phiếu xuất kho › Tự động đã tạo.");
        AutoButton.IsVisible = true;
    });
    private void OnAutoClick(object? sender, RoutedEventArgs e) { AutoButton.IsVisible = false; _autoN = 0; Show("m13", new Views.PhieuKhoView(3)); }

    private void OnUpdateReady(Version v) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        UpdateButton.Content = Controls.Icon.Label("Cập nhật", "download");
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
        ["m4"] = "snow", ["m5"] = "calendar", ["m9"] = "check", ["m7ck"] = "chart", ["m3"] = "doc", ["m3dm"] = "tag", ["m24cc"] = "calendar", ["m10"] = "ok",
        ["m13"] = "truck", ["m12"] = "print", ["m11"] = "layers",
        ["m14"] = "cube", ["m15"] = "scan", ["m19"] = "folder", ["m21"] = "tool", ["m22"] = "compare",
        ["users"] = "users", ["backup"] = "gear", ["ketnoi"] = "network", ["dongbo"] = "cloud", ["audit"] = "history", ["troly"] = "assist",
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
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 5 };
            var cnt = CountBadge(out var cntText);
            if (visible.Count == 1)
            {
                var item = visible[0];
                row.Children.Add(new TextBlock { Text = item.Label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                row.Children.Add(cnt);
                var b = new Button { Classes = { "navtab" }, Content = row };
                b.Click += (_, _) => Navigate(item.Key);
                _buttons[item.Key] = b; _badges[item.Key] = (cntText, cnt);
                NavPanel.Children.Add(b);
                continue;
            }
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

    // ------------------------------------------------------------------ số "Cần xử lý" Danh mục mã hàng (1.72)

    private readonly Avalonia.Threading.DispatcherTimer _dmTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private string _dmPrint = "";
    private bool _dmBusy, _dmAgain;
    /// <summary>Tính lại số "Cần xử lý" của Danh mục mã hàng (chạy ngầm). <paramref name="onlyIfChanged"/>: chỉ tính khi các bảng liên quan đã đổi phiên bản.</summary>
    public void RefreshDmBadge(bool onlyIfChanged = false)
    {
        var s = App.Services;
        if (!s.Session.Can(KhoLanh.Core.DanhMuc.DanhMucService.PermKey)) return;
        if (_dmBusy) { if (!onlyIfChanged) _dmAgain = true; return; }
        _dmBusy = true;
        Task.Run(() =>
        {
            var fp = KhoLanh.Core.DanhMuc.DanhMucService.Fingerprint(s.Modules.AllVersions());
            if (onlyIfChanged && fp == _dmPrint) return (int?)null;
            var d = new KhoLanh.Core.DanhMuc.DanhMucService(() => s.Lan, s.Session, s.Audit, () => null);
            var n = d.XlCount(); _dmPrint = fp; return n;
        }).ContinueWith(t => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _dmBusy = false;
            if (t.Status == TaskStatus.RanToCompletion && t.Result is { } n) SetBadge("m3dm", n);   // lỗi: chỉ là số gợi ý, bỏ qua
            if (_dmAgain) { _dmAgain = false; RefreshDmBadge(); }
        }));
    }
    /// <summary>Báo cho cửa sổ chính (nếu có) tính lại số "Cần xử lý" của Danh mục mã hàng — gọi sau khi sửa danh mục / nạp tồn mới.</summary>
    public static void DmChanged(Control from) => Of(from)?.RefreshDmBadge();

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
        // 1.68: Mạng LAN / Đồng bộ Google Drive / sao lưu gom vào "Dữ liệu & sao lưu" — giữ khóa cũ để các nút / Trợ lý mở đúng tab
        // 1.105: Dữ liệu & sao lưu + Đồng bộ điện thoại gộp vào "Đồng bộ & sao lưu" — khóa cũ mở đúng tab
        // 1.121: 3 tab Máy chủ · Điện thoại · Sao lưu — "lan" / "drive" / "dulieu" → Máy chủ
        if (key is "lan" or "drive" or "saoluu" or "dcn" or "dulieu" or "phone") { page ??= new DongBoView(key); key = "dongbo"; }
        var item = _menu.SelectMany(m => m.items).FirstOrDefault(i => i.Key == key);
        if (item is null) return;
        if (!Allowed(item)) return;
        // 1.72 (A3): giữ màn hình đã mở trong phiên — quay lại không dựng lại nếu dữ liệu chưa đổi (mốc CurrentSeq của CSDL module)
        var seq = DataSeq();
        if (page is null && CacheEnabled && _cache.TryGetValue(key, out var c0) && c0.Seq == seq) { page = c0.Page; _cache[key] = (c0.Page, seq, DateTime.Now); }
        if (page is null)
        {
            using (var m = KhoLanh.Core.TroLy.Perf.Measure("Mở module", item.Label)) page = item.Create();
            if (page is not IKeepFresh) _cache[key] = (page, seq, DateTime.Now);
            while (_cache.Count > CacheMax) _cache.Remove(_cache.OrderBy(x => x.Value.At).First().Key);   // bỏ màn hình lâu không mở
        }
        PageHost.IsVisible = true;
        PageHost.Content = page;
        // 1.62: bỏ dòng mô tả cạnh tiêu đề ở mọi module (gọn) — mô tả chuyển vào nút ⓘ Hướng dẫn
        if (page is IPage p) { PageTitle.Text = p.Title; PageSubtitle.Text = ""; }
        foreach (var b in _buttons.Values) b.Classes.Remove("active");
        if (_buttons.TryGetValue(key, out var act)) act.Classes.Add("active");
        foreach (var (k, mi) in _menuItems) mi.Classes.Set("current", k == key);
        _hiddenTabs = null; QueueAlign();
        if (!_goingBack && _current.Length > 0 && _current != key)
        {
            _back.Remove(key);                       // mở lại màn hình đã có trong lịch sử → không lặp
            _back.Add(_current);
            while (_back.Count > BackMax) _back.RemoveAt(0);
        }
        _current = key;
    }

    // ------------------------------------------------------------------ 1.81: ESC quay lại
    private const int BackMax = 3;
    private readonly List<string> _back = new();
    private bool _goingBack;
    /// <summary>Lịch sử màn hình (mới nhất cuối) — công cụ chụp kiểm tra.</summary>
    internal IReadOnlyList<string> BackHistory => _back;
    internal string CurrentKey => _current;

    /// <summary>Quay lại màn hình trước (tối đa 3 bước); hết lịch sử thì về Tổng quan. Trả false nếu đã ở Tổng quan, không còn gì để lùi.</summary>
    internal bool GoBack()
    {
        while (_back.Count > 0)
        {
            var k = _back[^1]; _back.RemoveAt(_back.Count - 1);
            if (k == _current) continue;
            var item = _menu.SelectMany(m => m.items).FirstOrDefault(i => i.Key == k);
            if (item is null || !Allowed(item)) continue;
            _goingBack = true;
            try { Show(k, null); } finally { _goingBack = false; }
            return true;
        }
        if (_current == "lx") return false;
        _goingBack = true;
        try { Show("lx", null); } finally { _goingBack = false; }
        return _current == "lx";
    }

    public void SetSubtitle(string text) => PageSubtitle.Text = text;

    // ------------------------------------------------------------------ 1.83: tab đầu trang chung hàng với tên trang
    private TabControl? _pageTabs;
    private TabControl? _hiddenTabs;
    private bool _alignQueued;
    private int _alignRetry;
    // phần đầu trang (dòng trạng thái / nút chung) đã chuyển lên hàng tên trang — trả lại chỗ cũ khi đổi trang
    private readonly List<(Panel Parent, int Index, Control Ctrl, Avalonia.Thickness Margin)> _lifted = new();
    private readonly List<(TextBlock Tb, bool Vis, Avalonia.Media.TextWrapping Wrap, Avalonia.Media.TextTrimming Trim)> _liftedText = new();
    private readonly List<string> _liftHints = new();

    private void QueueAlign()
    {
        _alignQueued = true; _alignRetry = 0;
        Avalonia.Threading.Dispatcher.UIThread.Post(AlignPageTabs, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void RestoreLifted()
    {
        if (_pageTabs is { } old) { old.Classes.Remove("pagetabs"); if (StripOf(old) is { } op) op.Margin = default; _pageTabs = null; }
        foreach (var (tb, vis, wrap, trim) in _liftedText) { tb.IsVisible = vis; tb.TextWrapping = wrap; tb.TextTrimming = trim; }
        _liftedText.Clear(); _liftHints.Clear();
        if (PageExtra.Child is StackPanel sp0) sp0.Children.Clear();
        PageExtra.Child = null; PageExtra.MaxWidth = 620;
        foreach (var (par, idx, c, m) in _lifted) { c.Margin = m; par.Children.Insert(Math.Min(idx, par.Children.Count), c); }
        _lifted.Clear();
    }

    private void TitleOnly()
    {
        PageSep.IsVisible = false;
        PageHost.Margin = new Thickness(12, 42, 12, 8);
        InvalidateVisual();
    }

    /// <summary>TabControl nằm ở đầu trang (tab cấp 1 của module) → dải tab lên hàng tên trang (36px), bắt đầu sau tên + vạch.
    /// Phần nhỏ nằm trên tab (dòng trạng thái, nút "Tải lại"…) chuyển sang bên phải hàng đó; ghi chú dài vào nút ⓘ.
    /// Không xếp được (đầu trang có thanh công cụ lớn, tab tràn xuống 2 dòng) → hàng tên trang chỉ có tên.</summary>
    internal void AlignPageTabs()
    {
        _alignQueued = false;
        RestoreLifted();
        if (PageHost.Content is not Control root || !root.IsAttachedToVisualTree()) { TitleOnly(); return; }
        var tc = root.GetVisualDescendants().OfType<TabControl>().FirstOrDefault(t => t.IsEffectivelyVisible && t.ItemCount > 0);
        var pt = tc?.TranslatePoint(new Point(0, 0), root);
        if (tc is null || pt is not { } p0 || p0.X >= 10 || p0.Y > 60)
        {
            TitleOnly();
            if (tc is null && _alignRetry++ < 3)   // trang dựng nội dung sau (nạp ngầm) → thử lại
                Avalonia.Threading.DispatcherTimer.RunOnce(() => { if (_pageTabs is null && !_alignQueued) { var r = _alignRetry; AlignPageTabs(); _alignRetry = r; } }, TimeSpan.FromMilliseconds(250));
            return;
        }
        if (p0.Y >= 6)
        {
            // chỉ nhận dạng thường gặp: DockPanel { phần trên Dock=Top, TabControl } ở sát đầu trang
            if (tc.GetVisualParent() is not DockPanel par || par.TranslatePoint(new Point(0, 0), root) is not { Y: < 6 }) { TitleOnly(); return; }
            var others = par.Children.Where(c => c != tc && c.IsVisible).ToList();
            if (others.Any(c => DockPanel.GetDock(c) != Dock.Top || c.Bounds.Height > 48)) { TitleOnly(); return; }
            foreach (var c in par.Children.Where(c => c != tc).ToList())
            {
                _lifted.Add((par, par.Children.IndexOf(c), c, c.Margin));
                par.Children.Remove(c);
            }
            var shown = new List<Control>();
            foreach (var (_, _, c, _) in _lifted)
            {
                if (!c.IsVisible) continue;
                c.Margin = default;
                foreach (var tb in c.GetLogicalDescendants().OfType<TextBlock>().Prepend(c as TextBlock).OfType<TextBlock>())
                {
                    _liftedText.Add((tb, tb.IsVisible, tb.TextWrapping, tb.TextTrimming));
                    var text = tb.Text ?? "";
                    if (tb.Classes.Contains("muted") && text.Length >= 40 && tb.TextWrapping != Avalonia.Media.TextWrapping.NoWrap && tb.Parent is not Button)
                    { tb.IsVisible = false; _liftHints.Add(text); continue; }   // ghi chú hướng dẫn → nút ⓘ
                    tb.TextWrapping = Avalonia.Media.TextWrapping.NoWrap; tb.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
                }
                shown.Add(c);
            }
            PageExtra.Child = shown.Count == 1 ? shown[0] : new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 }.Also(sp => { foreach (var c in shown) sp.Children.Add(c); });
        }
        _pageTabs = tc; tc.Classes.Add("pagetabs");
        PageSep.IsVisible = true;
        PageHost.Margin = new Thickness(12, 0, 12, 8);
        PageTitle.Measure(Size.Infinity);
        var left = PageTitle.DesiredSize.Width + 29;
        if (StripOf(tc) is { } ip) ip.Margin = new Thickness(left, 0, 34, 0);
        if (PageExtra.Child is not null)
        {
            // chỗ còn lại bên phải dải tab cho phần đầu trang đã chuyển lên (chữ dài tự cắt "…")
            double tabsW = 0;
            foreach (var it in tc.Items.OfType<TabItem>()) { it.Measure(Size.Infinity); tabsW += it.DesiredSize.Width + it.Margin.Left + it.Margin.Right; }
            var room = PageHost.Bounds.Width + 24 - 12 - left - tabsW - 16 - 40;
            if (room < 160) { RestoreLifted(); TitleOnly(); return; }
            PageExtra.MaxWidth = Math.Min(room, 760);
        }
        // kiểm sau khi xếp: dải tab phải nằm gọn 1 hàng ở đầu trang
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_pageTabs != tc) return;
            var strip = StripOf(tc);
            var y = tc.TranslatePoint(new Point(0, 0), PageHost)?.Y ?? 0;
            if (strip is null || strip.Bounds.Height > 40) { RestoreLifted(); TitleOnly(); return; }
            // 1.85.1: lúc xếp, phần trên TabControl chưa kịp có chiều cao (p0.Y < 6) nhưng sau khi vẽ thì cao hơn hẳn (vd Container 3D:
            // thanh công cụ 38px) → trước đây dời cả trang lên −y, thanh công cụ chui lên thanh menu. Chỉ bù lệch nhỏ (viền / lề);
            // lệch lớn → xếp lại theo vị trí thật (nhánh chuyển phần trên lên hàng tên trang), quá số lần → chỉ tên trang.
            if (y > MaxTabShift)
            {
                RestoreLifted();
                if (_alignRetry++ < 3) { var r = _alignRetry; AlignPageTabs(); _alignRetry = r; } else TitleOnly();
                return;
            }
            if (Math.Abs(y) > 0.5) PageHost.Margin = new Thickness(12, -y, 12, 8);
            InvalidateVisual();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }
    /// <summary>Lệch tối đa (px) giữa dải tab và đầu trang được bù bằng lề âm — lớn hơn là có phần nội dung nằm trên tab.</summary>
    internal const double MaxTabShift = 6;
    private static Control? StripOf(TabControl tc) =>
        tc.GetVisualDescendants().OfType<ItemsPresenter>().FirstOrDefault(p => p.Name == "PART_ItemsPresenter" && p.TemplatedParent == tc);
    /// <summary>Công cụ chụp / kiểm tra: TabControl đang nằm chung hàng tên trang.</summary>
    internal TabControl? PageTabs => _pageTabs;

    // ------------------------------------------------------------------ 1.72 (A3): giữ màn hình đã mở
    private const int CacheMax = 8;
    /// <summary>Tắt giữ màn hình (công cụ chụp ảnh cần màn hình mới mỗi lần mở).</summary>
    public static bool CacheEnabled { get; set; } = true;
    private readonly Dictionary<string, (Control Page, long Seq, DateTime At)> _cache = new();
    /// <summary>Mốc dữ liệu module (tăng mỗi lần có sheet đổi, kể cả nhận từ LAN / Drive) — đổi thì dựng lại màn hình khi mở.</summary>
    private static long DataSeq() { try { return App.Services.Modules.CurrentSeq(); } catch { return -1; } }
    /// <summary>Bỏ các màn hình đã giữ (đổi giao diện / cỡ chữ, đổi quyền…).</summary>
    public void ClearPageCache() => _cache.Clear();

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
        hints.InsertRange(0, _liftHints);
        if (PageHost.Content is IPage { Subtitle: { Length: > 0 } sub }) hints.Insert(0, sub);
        var fl = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight, Content = Controls.HintTrim.Panel(hints) };
        fl.ShowAt(HelpButton);
        return fl;
    }

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
    /// <summary>Hộp "Giao diện & cỡ chữ" đang / vừa mở (công cụ chụp màn hình kiểm tra).</summary>
    internal Window? ThemeWindow;
    internal void OpenThemeForTest() => OnThemeSettings(null, new RoutedEventArgs());

    private async void OnThemeSettings(object? sender, RoutedEventArgs e)
    {
        var mode0 = AppTheme.Current; var scale0 = AppTheme.Scale; var dens0 = AppTheme.Density;
        var dens = new ComboBox { ItemsSource = AppTheme.Densities.Select(x => x.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(AppTheme.Densities, x => x.Key == dens0)), MinWidth = 220 };
        var light = new RadioButton { Content = "Sáng dịu — nền xám lạnh nhạt, đỡ chói", GroupName = "th", IsChecked = mode0 == AppTheme.Mode.Light };
        var dark = new RadioButton { Content = "Tối — nền xám đen", GroupName = "th", IsChecked = mode0 == AppTheme.Mode.Dark };
        var scales = new[] { 0.9, 1.0, 1.1, 1.2, 1.3 };
        var size = new ComboBox { ItemsSource = scales.Select(x => $"{x * 100:0} %" + (x == 1.0 ? " (chuẩn)" : "")).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(scales, x => Math.Abs(x - scale0) < 0.001)), MinWidth = 160 };
        // 1.74: chế độ lấy từ nút VỪA được chọn. Trước đây chỉ nghe nút Sáng rồi đọc dark.IsChecked — lúc nút Sáng vừa bật,
        // nút Tối chưa kịp tắt (nhóm RadioButton tắt nút kia SAU sự kiện) → áp lại Tối: chuyển Tối → Sáng không được.
        var pick = mode0;
        void Preview() { AppTheme.SetDensity(AppTheme.Densities[Math.Max(0, dens.SelectedIndex)].Key); AppTheme.Apply(pick, scales[Math.Max(0, size.SelectedIndex)]); ApplyZoom(); TitleBar.Apply(this); }
        light.IsCheckedChanged += (_, _) => { if (light.IsChecked == true) { pick = AppTheme.Mode.Light; Preview(); } };
        dark.IsCheckedChanged += (_, _) => { if (dark.IsChecked == true) { pick = AppTheme.Mode.Dark; Preview(); } };
        size.SelectionChanged += (_, _) => Preview(); dens.SelectionChanged += (_, _) => Preview();
        var ok = new Button { Content = "Lưu", Classes = { "primary" }, IsDefault = true }; var cancel = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = "Bộ màu", FontWeight = Avalonia.Media.FontWeight.SemiBold }); body.Children.Add(light); body.Children.Add(dark);
        body.Children.Add(new TextBlock { Text = "Cỡ chữ (thu phóng toàn bộ màn hình)", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 6, 0, 0) }); body.Children.Add(size);
        body.Children.Add(new TextBlock { Text = "Lưu riêng cho tài khoản này trên máy này. Phím tắt: Ctrl + / Ctrl − / Ctrl 0.", Classes = { "muted" }, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Mật độ bảng (chiều cao dòng)", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 6, 0, 0) });
        body.Children.Add(dens);
        body.Children.Add(Ui.Buttons(cancel, ok));
        var w = Ui.Dialog("Giao diện & cỡ chữ", body, 400); var saved = false;
        ThemeWindow = w;
        ok.Click += (_, _) => { saved = true; w.Close(); }; cancel.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (!saved) { AppTheme.SetDensity(dens0); AppTheme.Apply(mode0, scale0); ApplyZoom(); }
        else AppTheme.SaveFor(App.Services.Session.Username);
        ReloadPage();
    }

    /// <summary>Dựng lại màn hình đang mở (để các ô vẽ tay lấy màu mới).</summary>
    private void ReloadPage() { _cache.Clear(); if (_current.Length > 0 && PageHost.Content is Control) Show(_current, null); }

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

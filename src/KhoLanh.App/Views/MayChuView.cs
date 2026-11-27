using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Lan;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Views;

/// <summary>
/// 1.121 — Đồng bộ &amp; sao lưu › tab MÁY CHỦ (người dùng duyệt mẫu): 1 khung trạng thái theo tình huống của máy này, không hiện IP / cổng / mã LAN.
///  a) Máy này làm máy chủ: nối được trong kho / qua Internet · 3 ô số · bảng Máy đang nối (lọc Trong kho / Internet) · Trùng sửa cần xem.
///  b) Máy con đang nối: đường nối (Trong kho / Internet — app tự chọn) · gửi lần cuối · chờ gửi · Máy trong hệ thống.
///  c) Máy đơn có thay đổi chờ gửi (không nối được máy chủ): đã thử LAN / Internet · Nối lại ngay · bảng thay đổi chờ gửi.
///  d) Máy đơn dùng bản Drive cá nhân của máy chủ (<see cref="LanService.OfflineSource"/>): nguồn dữ liệu.
///  e) Máy đơn chưa bật LAN: Quản trị bật máy chủ; máy đã tách sau khôi phục → Nối lại mạng LAN.
/// Tài khoản không phải Quản trị chỉ xem trạng thái. Cập nhật theo <see cref="LanService.Changed"/> + 5 giây / lần khi đang hiện.
/// </summary>
public sealed class MayChuView : UserControl
{
    private static KhoLanh.Core.AppServices S => App.Services;
    private static bool Admin => Roles.IsAdmin(S.Session.Role);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private LanService? _sub;

    private readonly StackPanel _root = new() { Spacing = 12, Margin = new Thickness(0, 0, 8, 12) };
    private readonly Border _status = new() { Classes = { "card" }, Padding = new Thickness(18, 14) };
    private readonly Grid _tiles = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 10 };
    private readonly StackPanel _boxes = new() { Spacing = 10 };
    private readonly StackPanel _machSec = new() { Spacing = 6 };
    private readonly StackPanel _conflictSec = new() { Spacing = 6 };
    private readonly StackPanel _sysSec = new() { Spacing = 6 };
    private readonly StackPanel _pendSec = new() { Spacing = 6 };
    private readonly StackPanel _notes = new() { Spacing = 4 };

    private readonly GridModel _machM = new() { ReadOnly = true };
    private readonly SheetGrid _mach;
    private readonly Seg _machSeg = new("Tất cả", "Trong kho", "Qua Internet");
    private readonly TextBlock _machTitle = new() { Text = "Máy đang nối (5 phút gần nhất)", Classes = { "section" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly GridModel _pendM = new() { ReadOnly = true };
    private readonly SheetGrid _pend;

    /// <summary>Tình huống đang hiện (công cụ chụp kiểm): may-chu · may-con · may-don-cho · may-don-drive · may-don.</summary>
    internal string State { get; private set; } = "";
    /// <summary>Số mục Trùng sửa đang hiện (công cụ chụp kiểm).</summary>
    internal int ConflictRows { get; private set; }
    /// <summary>Số máy trong bảng Máy đang nối theo bộ lọc hiện tại (công cụ chụp kiểm).</summary>
    internal int MachineRows => _machM.RowCount;
    internal Seg MachineFilter => _machSeg;

    public MayChuView()
    {
        _mach = new SheetGrid(_machM) { SpareRows = 0, RowChecks = false, MinRows = 0 };
        _mach.CellBackground = (r, c) => c == 2 && r < _machM.RowCount ? AppTheme.Brush(_machM.Get(r, 2) == "Internet" ? "#EEEBFC" : "#E6F0FC") : null;
        _mach.CellForeground = (r, c) => c == 2 && r < _machM.RowCount ? AppTheme.Brush(_machM.Get(r, 2) == "Internet" ? "#5B4BC4" : "#1F6FD6") : null;
        _pend = new SheetGrid(_pendM) { SpareRows = 0, RowChecks = false, MinRows = 0 };
        _machSeg.Changed += _ => FillMachines();

        var head = new DockPanel();
        DockPanel.SetDock(_machSeg, Dock.Right);
        head.Children.Add(_machSeg); head.Children.Add(_machTitle);
        _machSec.Children.Add(head); _machSec.Children.Add(_mach);
        _pendSec.Children.Add(new TextBlock { Text = "Thay đổi chờ gửi lên máy chủ", Classes = { "section" } });
        _pendSec.Children.Add(_pend);

        _root.Children.Add(_status);
        _root.Children.Add(_tiles);
        _root.Children.Add(_boxes);
        _root.Children.Add(_machSec);
        _root.Children.Add(_conflictSec);
        _root.Children.Add(_sysSec);
        _root.Children.Add(_pendSec);
        _root.Children.Add(_notes);
        Content = new ScrollViewer { Content = _root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        _timer.Tick += (_, _) => Render();
        AttachedToVisualTree += (_, _) => { Hook(); _timer.Start(); Render(); };
        DetachedFromVisualTree += (_, _) => { _timer.Stop(); if (_sub is not null) _sub.Changed -= OnLanChanged; _sub = null; };
        Render();
    }

    private void OnLanChanged() => Dispatcher.UIThread.Post(Render);

    /// <summary>Phần LAN được tạo lại khi đổi vai (ReloadLan) → nghe sự kiện của phần mới.</summary>
    private void Hook()
    {
        if (ReferenceEquals(_sub, S.Lan)) return;
        if (_sub is not null) _sub.Changed -= OnLanChanged;
        _sub = S.Lan; _sub.Changed += OnLanChanged;
    }

    // ================================================================== vẽ

    internal void Render()
    {
        if (VisualRoot is not null) Hook();
        var lan = S.Lan;
        _boxes.Children.Clear(); _conflictSec.Children.Clear(); _sysSec.Children.Clear(); _notes.Children.Clear();
        _tiles.Children.Clear();
        _tiles.IsVisible = _machSec.IsVisible = _conflictSec.IsVisible = _sysSec.IsVisible = _pendSec.IsVisible = false;
        _status.ClearValue(Border.BorderBrushProperty); _status.ClearValue(Border.BackgroundProperty);
        int pending = 0; try { pending = lan.PendingCount(); } catch { }

        if (lan.Serving) RenderServer(lan);
        else if (lan.Enabled && lan.Online) RenderClient(lan, pending);
        else if (lan.Enabled && lan.OfflineSource.Length > 0) RenderOfflineDrive(lan, pending);
        else if (lan.Enabled) RenderOffline(lan, pending);
        else RenderSingle();

        // máy đã tách ra dùng riêng sau khi khôi phục → nối lại (1.114)
        if (S.Settings.Get(KhoLanh.Core.Services.BackupService.DetachedKey) == "1") _boxes.Children.Add(RejoinBox());
        // máy cũ vừa bỏ Thư mục / Drive dùng chung (1.121)
        if (DateTime.TryParse(S.Settings.Get(DriveRetire.DoneKey), out var bo) && DateTime.Now - bo < TimeSpan.FromDays(14))
            _notes.Children.Add(Note(App.DriveRetireNote.Length > 0 ? App.DriveRetireNote
                : $"Máy này đã bỏ «Thư mục / Drive dùng chung» lúc {bo:HH:mm dd/MM} (bản 1.121) — máy ở xa nay nối máy chủ qua Internet, dữ liệu cũ được chụp ở Sao lưu nếu là máy xem từ xa."));
    }

    // ---------------------------------------------------------------- a) máy này làm máy chủ

    private void RenderServer(LanService lan)
    {
        State = "may-chu";
        var plan = S.Accounts.Plan;
        var (lanM, netM) = lan.MachinesByWay();
        var waiting = SafeConflicts(lan);
        var inKho = lan.ListenError.Length == 0
            ? Ok("✓") : Bad("✖ " + Short(lan.ListenError, 120));
        Run[] net;
        if (lan.PublicUrl.Length == 0) net = new[] { Muted("chưa cài") };
        else if (lan.HttpError.Length > 0) net = new[] { Bad("✖ " + Short(lan.HttpError, 100)) };
        else if (lan.TunnelLastOk > DateTime.MinValue) net = new[] { Ok("✓"), Muted($" (tự kiểm {lan.TunnelLastOk:HH:mm})") };
        else net = new[] { Muted("chưa kiểm — bấm ⋯ Thêm › Địa chỉ Internet của máy chủ") };
        var subs = new List<Control>
        {
            Line(new[] { Muted("Máy trong kho nối được: ") }.Concat(new[] { inKho }).ToArray()),
            Line(new[] { Muted("Máy ở ngoài nối được (Internet): ") }.Concat(net).ToArray()),
            Line(Muted("Máy chủ tắt: "), Plain("các máy con làm riêng trên dữ liệu trong máy, bật lại thì tự gộp")),
        };
        if (Admin && App.LanPublishNote.Length > 0) subs.Add(Line(Warn("⚠ " + App.LanPublishNote)));
        Control? acts = null;
        if (Admin)
        {
            var more = new Button { Content = "⋯ Thêm" };
            var menu = new MenuFlyout();
            Item(menu, "Chuyển máy chủ sang máy khác…", () => Ui.Message(this,
                "Muốn máy khác làm máy chủ: đăng nhập tài khoản Quản trị trên máy đó › Đồng bộ & sao lưu › Máy chủ › «Chuyển máy chủ về máy này».\n\n"
                + "Máy này trả vai sau khi máy mới chép đủ dữ liệu rồi thành máy con — không mất dữ liệu.", "Chuyển máy chủ"));
            Item(menu, "Nhật ký nối máy", ShowLog);
            Item(menu, "Địa chỉ Internet của máy chủ…", PublicUrlDialog);
            Item(menu, ChayAn.Enabled ? "Tắt chạy ẩn (đóng cửa sổ = thoát hẳn)" : "Bật chạy ẩn (đóng cửa sổ = ẩn xuống khay)", ToggleChayAn);
            more.Flyout = menu;
            var off = new Button { Content = "Tắt máy chủ", Classes = { "danger" } };
            off.Click += async (_, _) => await Release();
            acts = Row(more, off);
        }
        _status.Child = Head("ok", "Đang làm máy chủ", lan.MachineName + " (máy này)", subs, acts);

        // 3 ô số
        _tiles.IsVisible = true;
        PutTile(0, "Máy trong kho (LAN)", lanM.Count, null, null);
        PutTile(1, "Máy qua Internet", netM.Count, null, null);
        PutTile(2, "Trùng sửa cần xem", waiting.Count, waiting.Count > 0 ? "warn" : null, () => _conflictSec.BringIntoView());

        // bảng máy đang nối
        _machSec.IsVisible = true;
        _machSeg.SetText(0, $"Tất cả · {lanM.Count + netM.Count}");
        _machSeg.SetText(1, $"Trong kho · {lanM.Count}");
        _machSeg.SetText(2, $"Qua Internet · {netM.Count}");
        FillMachines();

        // trùng sửa cần xem — chỉ khi có
        ConflictRows = 0;
        if (waiting.Count > 0) RenderConflicts(lan, waiting);
    }

    private static List<LanService.Conflict> SafeConflicts(LanService lan)
    {
        try { return lan.Conflicts(false).Where(c => c.Waiting).ToList(); } catch { return new(); }
    }

    private void FillMachines()
    {
        var lan = S.Lan;
        var (lanM, netM) = lan.MachinesByWay();
        IEnumerable<(LanService.Seen M, bool Net)> all = _machSeg.Selected switch
        {
            1 => lanM.Select(m => (m, false)),
            2 => netM.Select(m => (m, true)),
            _ => lanM.Select(m => (m, false)).Concat(netM.Select(m => (m, true))),
        };
        var rows = all.OrderBy(x => x.M.Name).Select(x => new[]
        {
            x.M.Name, x.M.User.Length > 0 ? x.M.User : "—", x.Net ? "Internet" : "Trong kho",
            x.Net ? (x.M.NetIp.Length > 0 ? x.M.NetIp : "(không rõ)") : NoPort(x.M.Addr),
            x.M.At.ToString("HH:mm:ss"), x.M.Pending > 0 ? x.M.Pending.ToString() : "0",
        }).ToList();
        _machM.Load(new[]
        {
            new GridColumn("may", "Máy", 190), new GridColumn("tk", "Tài khoản", 130), new GridColumn("qua", "Nối qua", 110),
            new GridColumn("dc", "Địa chỉ", 170), new GridColumn("luc", "Lần cuối", 100), new GridColumn("cho", "Chờ gửi", 90),
        }, rows);
        _mach.Height = SheetGrid.FitHeight(Math.Max(3, rows.Count), 340);
        _mach.Status = rows.Count == 0 ? "Chưa có máy nào nối trong 5 phút gần nhất" : $"{rows.Count} máy";
        _mach.Refresh();
    }

    /// <summary>Địa chỉ trong kho: bỏ cổng ("192.168.1.5:47820" → "192.168.1.5"), nhiều địa chỉ lấy địa chỉ đầu.</summary>
    internal static string NoPort(string addr)
    {
        var x = (addr ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var i = x.LastIndexOf(':');
        return i > 0 && x.IndexOf(':') == i ? x[..i] : x;
    }

    private void RenderConflicts(LanService lan, List<LanService.Conflict> list)
    {
        _conflictSec.IsVisible = true;
        var head = new DockPanel();
        var full = new Button { Content = "Mở danh sách đầy đủ" };
        full.Click += async (_, _) => await OpenFull();
        DockPanel.SetDock(full, Dock.Right);
        head.Children.Add(full);
        head.Children.Add(new TextBlock { Text = $"Trùng sửa cần xem · {list.Count}", Classes = { "section" }, VerticalAlignment = VerticalAlignment.Center });
        _conflictSec.Children.Add(head);
        var canAct = Admin && lan.Serving;
        foreach (var c in list.Take(20))
        {
            var pill = Pill(c.Status == "cho" ? "warn" : "info", c.Status == "cho" ? "Chờ chọn" : "Đã tự gộp");
            var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 13,
                Inlines = new InlineCollection
                {
                    new Run(KhoLanh.Core.TroLy.TroLyService.DbLabel(c.DbKey) + " › " + c.Sheet) { FontWeight = FontWeight.SemiBold },
                    new Run($" · máy {c.Machine}" + (c.Username.Length > 0 ? $" ({c.Username})" : "") + $" · {c.At}") { Foreground = AppTheme.Brush("#5D6B78") },
                },
            });
            if (c.Note.Length > 0) info.Children.Add(new TextBlock { Text = c.Note, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "keep" });
            var btns = new WrapPanel { ItemSpacing = 6, LineSpacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            if (canAct)
            {
                btns.Children.Add(Small("Dùng bản máy con", () => Act(c, "maycon", "Thay bảng trên máy chủ bằng bản của máy con? Những gì máy chủ sửa trong bảng này lúc mất kết nối sẽ mất (vẫn còn trong sao lưu).")));
                if (c.Status == "cho") btns.Children.Add(Small("Giữ bản máy chủ", () => Act(c, "maychu", "Giữ nguyên bản máy chủ, bỏ bản của máy con?")));
                if (c.Status == "gop") btns.Children.Add(Small("Hoàn tác gộp", () => Act(c, "hoantac", "Bỏ kết quả tự gộp, đưa bảng về bản máy chủ trước khi gộp?")));
                btns.Children.Add(Small("Đã xem", () => Act(c, "daxem", "Đánh dấu đã xem (giữ kết quả hiện tại)?")));
            }
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            pill.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(pill);
            Grid.SetColumn(info, 1); g.Children.Add(info);
            Grid.SetColumn(btns, 2); g.Children.Add(btns);
            _conflictSec.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(14, 10), Child = g });
            ConflictRows++;
        }
        if (list.Count > 20) _conflictSec.Children.Add(Note($"Còn {list.Count - 20} mục — bấm «Mở danh sách đầy đủ»."));
        if (!canAct) _conflictSec.Children.Add(Note("Chỉ tài khoản Quản trị trên máy chủ chọn được bản giữ lại."));
    }

    private async Task Act(LanService.Conflict c, string action, string question)
    {
        if (!await Ui.Confirm(this, $"{question}\n\n{KhoLanh.Core.TroLy.TroLyService.DbLabel(c.DbKey)} › bảng {c.Sheet} (máy {c.Machine}, {c.Username}, {c.At}).", "Trùng sửa", "Đồng ý")) return;
        if (await Ui.Run(this, () => S.Lan.Resolve(c.Id, action))) Render();
    }

    private async Task OpenFull()
    {
        var w = new Window
        {
            Title = "Máy đang nối & trùng sửa", Width = 1180, Height = 740, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = AppTheme.Card, Content = new Border { Padding = new Thickness(14, 10), Child = new LanView() },
        };
        await Ui.Show(this, w);
        Render();
    }

    // ---------------------------------------------------------------- b) máy con đang nối

    private void RenderClient(LanService lan, int pending)
    {
        State = "may-con";
        var net = lan.Via == "net";
        var plan = S.Accounts.Plan;
        var subs = new List<Control>
        {
            Line(Muted("Đường nối: "), Plain(net ? "Internet (máy đang ở ngoài kho — app tự chọn)" : "Trong kho (LAN)")),
            Line(Muted("Gửi lần cuối: "), Plain(lan.LastSync.Length > 0 ? lan.LastSync : "chưa gửi lần nào từ lúc mở app"),
                Muted(" · Nhận dữ liệu lần cuối: "), Plain(lan.LastSyncedAt is { } t ? $"{t:HH:mm dd/MM}" : "—")),
            Line(Muted("Chờ gửi: "), pending > 0 ? Warn($"{pending} thay đổi") : Plain("0")),
        };
        Control? acts = null;
        if (Admin)
        {
            var b = new Button { Content = "Chuyển máy chủ về máy này" };
            b.Click += async (_, _) => await Claim();
            acts = b;
        }
        var title = new[] { (Control)Pill(net ? "info" : "accent", net ? "qua Internet" : "trong kho") };
        _status.Child = Head("ok", "Đã nối máy chủ", lan.ServerName, subs, acts, title);

        _sysSec.IsVisible = true;
        _sysSec.Children.Add(new TextBlock { Text = "Máy trong hệ thống", Classes = { "section" } });
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 10 };
        Put(g, 0, Box("Máy chủ", plan?.Chu?.Name ?? lan.ServerName, null));
        Put(g, 1, Box("Khi máy chủ tắt", "máy này làm riêng, tự gộp khi nối lại", null));
        Put(g, 2, Box("Máy này", $"{lan.MachineName} · {LanService.RoleLabel(lan.Role).ToLowerInvariant()}", null));
        _sysSec.Children.Add(g);
        _notes.Children.Add(Note("Không cần nhập địa chỉ / cổng: đăng nhập là app tự nối máy chủ — trong kho đi qua mạng LAN, ở ngoài đi qua Internet."));
        FillPending(pending);
    }

    // ---------------------------------------------------------------- c) máy đơn, không nối được máy chủ

    private void RenderOffline(LanService lan, int pending)
    {
        if (lan.AwayMode) { RenderAway(lan, pending); return; }
        State = pending > 0 ? "may-don-cho" : "may-don-mat";
        _status.BorderBrush = AppTheme.Brush("#E2938A");
        _status.Background = AppTheme.Brush("#FFF8F7");
        var subs = new List<Control>
        {
            Line(TriedRuns()),
            Line(Muted("Dữ liệu: "), Plain(lan.LastSyncedAt is { } t ? $"đồng bộ lần cuối {t:HH:mm dd/MM/yyyy}" : "chưa đồng bộ với máy chủ lần nào")),
            Line(Muted("Chờ gửi: "), pending > 0 ? Warn($"{pending} thay đổi") : Plain("0")),
        };
        if (pending > 0) subs.Add(Line(Muted("Có thay đổi chờ gửi nên KHÔNG tải bản Drive (để không mất phần đang sửa).")));
        subs.Add(Line(Muted("Lý do: ")));
        foreach (var d in Diag(lan)) subs.Add(Line(Muted("  • " + d)));
        Control acts = RetryButton();
        if (Admin)
        {
            // 1.144: máy chủ mất vai (cài đặt bị xóa) vẫn có lối bật lại ngay tại đây
            var mine = new Button { Content = "Đặt máy này làm máy chủ" };
            mine.Click += async (_, _) => await Claim();
            acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { acts, mine } };
            subs.Add(Line(Muted("Nếu CHÍNH máy này là máy chủ của kho (vừa bị mất cài đặt): bấm «Đặt máy này làm máy chủ».")));
        }
        // 1.176: máy ở ngoài mạng kho, không có Internet tới máy chủ → chuyển sang làm ngoại tuyến (hết báo đỏ)
        var away = new Button { Content = "Máy này ở ngoài mạng kho" };
        ToolTip.SetTip(away, "Làm ngoại tuyến: không báo lỗi, không thử Internet, thay đổi được giữ lại và tự gửi khi máy về mạng kho.");
        away.Click += (_, _) => { S.Lan.SetAwayMode(true); Render(); };
        acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { acts, away } };
        _status.Child = Head("bad", "Máy đơn", "Không nối được máy chủ — đang làm trên dữ liệu của máy này", subs, acts);
        _boxes.Children.Add(SoftBox("#FFF2D6", "#9A5B00", "Bạn vẫn làm việc bình thường",
            "Mọi thay đổi được giữ trên máy này và tự gửi lên máy chủ khi nối lại. Nếu cùng một bảng bị sửa ở cả hai nơi, Quản trị chọn bản giữ lại ở tab Máy chủ trên máy chủ.\n"
            + "Kiểm tra: máy chủ còn bật · máy này có Internet (ở ngoài kho) hoặc cùng mạng kho."));
        FillPending(pending);
    }

    // ---------------------------------------------------------------- d) máy đơn dùng bản Drive cá nhân của máy chủ

    /// <summary>1.176: máy ngoài mạng kho (không Internet tới máy chủ) — trạng thái trung tính, không đỏ.</summary>
    private void RenderAway(LanService lan, int pending)
    {
        State = "ngoai-kho";
        var subs = new List<Control>
        {
            Line(Muted("Máy này đang làm việc ngoại tuyến trên dữ liệu của chính nó. Không cần Internet.")),
            Line(Muted("Dữ liệu: "), Plain(lan.LastSyncedAt is { } t ? $"đồng bộ lần cuối {t:HH:mm dd/MM/yyyy}" : "chưa đồng bộ với máy chủ lần nào")),
            Line(Muted("Chờ gửi: "), pending > 0 ? Warn($"{pending} thay đổi") : Plain("0")),
            Line(Muted("Khi máy về mạng kho (cùng mạng với máy chủ) app tự nối, gửi các thay đổi lên và gộp — không cần làm gì thêm.")),
        };
        subs.Add(Line(Muted("Chi tiết lần thử nối gần nhất:")));
        foreach (var d in Diag(lan)) subs.Add(Line(Muted("  • " + d)));
        var off = new Button { Content = "Tắt chế độ ngoại tuyến" };
        off.Click += (_, _) => { S.Lan.SetAwayMode(false); Render(); };
        var retry = new Button { Content = "Thử nối mạng kho ngay" };
        retry.Click += async (_, _) =>
        {
            retry.IsEnabled = false;
            try { await Ui.Run(this, () => { S.Lan.RetryNow(); }); Hook(); }
            finally { retry.IsEnabled = true; Render(); }
        };
        Control acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { retry, off } };
        _status.Child = Head("info", "Ngoại tuyến", "Đang làm ngoại tuyến — sẽ tự gửi khi về mạng kho", subs, acts);
        FillPending(pending);
    }

    private void RenderOfflineDrive(LanService lan, int pending)
    {
        State = "may-don-drive";
        _status.BorderBrush = AppTheme.Brush("#F2C46B");
        var subs = new List<Control>
        {
            Line(TriedRuns()),
            Line(Muted("Dữ liệu: "), Plain(lan.OfflineSource)),
            Line(Muted("Chờ gửi: "), pending > 0 ? Warn($"{pending} thay đổi") : Plain("0")),
        };
        _status.Child = Head("warn", "Máy đơn", "Máy chủ đang tắt — dùng bản sao lưu Drive của máy chủ", subs, RetryButton());
        _boxes.Children.Add(SoftBox("#E3F4EA", "#1E7B4A", "Đã tự tải bản dữ liệu mới hơn từ Google Drive cá nhân",
            "Bản dữ liệu cũ của máy này được cất ở Sao lưu › Trên máy này. Phần bạn sửa từ giờ được giữ trên máy và tự gửi lên khi máy chủ bật lại."));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 10 };
        Put(g, 0, Box("Trên máy này", lan.LastSyncedAt is { } t ? $"đồng bộ lần cuối {t:HH:mm dd/MM}" : "chưa đồng bộ với máy chủ", null));
        Put(g, 1, Box("Drive cá nhân", lan.OfflineSource, "Đang dùng"));
        string mail = "";
        try { var raw = S.DriveCaNhan.Config.Email; if (raw.Length > 0) mail = KhoLanh.Core.TroLy.TroLyBaoCao.MaskEmail(raw); } catch { }
        Put(g, 2, Box("Gmail sao lưu", mail.Length > 0 ? mail : "chưa kết nối", null));
        _sysSec.IsVisible = true;
        _sysSec.Children.Add(new TextBlock { Text = "Nguồn dữ liệu", Classes = { "section" } });
        _sysSec.Children.Add(g);
        _notes.Children.Add(Note("Máy chưa kết nối Drive cá nhân (Hệ thống › Kết nối › Gmail sao lưu) thì khi mất máy chủ, máy dùng dữ liệu đang có trên máy."));
        FillPending(pending);
    }

    private static List<string> Diag(LanService lan) { try { return lan.Diagnose(); } catch { return new(); } }

    private Run[] TriedRuns()
    {
        var plan = S.Accounts.Plan;
        var hasNet = plan?.Chu?.Addrs.Any(LanHttp.IsUrl) == true;
        return new[] { Muted("Đã thử: LAN "), Bad("✖"), Muted(" · Internet "), hasNet ? Bad("✖") : Muted("(máy chủ chưa cài địa chỉ Internet)"), Muted(" · tự thử lại mỗi 15 giây") };
    }

    private Button RetryButton()
    {
        var b = new Button { Content = "Nối lại ngay", Classes = { "primary" } };
        b.Click += async (_, _) =>
        {
            b.IsEnabled = false;
            try
            {
                await Ui.Run(this, () =>
                {
                    S.Lan.RetryNow();   // vòng sau thử tìm máy chủ ngay
                });
                Hook();
            }
            finally { b.IsEnabled = true; Render(); }
        };
        return b;
    }

    private void FillPending(int pending)
    {
        List<(string DbKey, string Sheet, string At, string User)> list = new();
        try { list = S.Lan.PendingList(); } catch { }
        if (pending == 0 || list.Count == 0) return;
        _pendSec.IsVisible = true;
        var rows = list.Select(x => new[] { KhoLanh.Core.TroLy.TroLyService.DbLabel(x.DbKey), x.Sheet, x.At, x.User }).ToList();
        _pendM.Load(new[] { new GridColumn("mod", "Module", 200), new GridColumn("bang", "Bảng", 180), new GridColumn("luc", "Lúc", 150), new GridColumn("ai", "Người sửa", 130) }, rows);
        _pend.Height = SheetGrid.FitHeight(Math.Max(3, rows.Count), 300);
        _pend.Status = $"{rows.Count} bảng";
        _pend.Refresh();
    }

    // ---------------------------------------------------------------- e) máy đơn chưa bật LAN

    private void RenderSingle()
    {
        State = "may-don";
        var subs = new List<Control> { Line(Muted("Dữ liệu chỉ nằm trên máy này, không dùng chung với máy khác.")) };
        Control? acts = null;
        if (Admin)
        {
            var b = new Button { Content = "Bật máy chủ trên máy này", Classes = { "primary" }, IsEnabled = S.Accounts.Enabled };
            b.Click += async (_, _) => await Claim();
            acts = b;
            subs.Add(Line(Muted(S.Accounts.Enabled
                ? "Bật máy chủ: máy này giữ dữ liệu chính, các máy khác đăng nhập tài khoản dùng chung là tự nối (trong kho qua LAN, ở ngoài qua Internet). Nên là máy luôn bật."
                : "Cần kết nối Google Sheet chung trước (Hệ thống › Kết nối) để các máy khác tự tìm thấy máy chủ.")));
        }
        else subs.Add(Line(Muted("Đăng nhập bằng tài khoản dùng chung là máy tự nối máy chủ (khi Quản trị đã bật máy chủ).")));
        _status.Child = Head("off", "Máy đơn", "Máy đơn — chưa nối máy chủ", subs, acts);
    }

    private Control RejoinBox()
    {
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(new TextBlock { Text = "Máy này đã tách khỏi mạng LAN sau khi khôi phục", FontWeight = FontWeight.Bold, Foreground = AppTheme.Brush("#9A5B00") });
        p.Children.Add(new TextBlock { Text = "Dữ liệu riêng, không tự nối lại máy chủ khi đăng nhập. Nối lại: dữ liệu riêng của máy này được chụp vào thư mục SaoLuu (truoc-noi-lai-…) rồi thay bằng dữ liệu máy chủ.", TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Tag = "keep" });
        var b = new Button { Content = "Nối lại mạng LAN", HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += async (_, _) =>
        {
            if (!await Ui.Confirm(b, "Nối lại mạng LAN: dữ liệu riêng trên máy này (đã chụp vào SaoLuu) sẽ được thay bằng dữ liệu máy chủ. Tiếp tục?", "Nối lại mạng LAN", "Nối lại")) return;
            string msg = "";
            if (await Ui.Run(b, () => msg = LanPlanSync.Rejoin(S))) await Ui.Message(b, msg + "\n\nNên đăng nhập lại để app nạp dữ liệu máy chủ.", "Nối lại mạng LAN");
            Render();
        };
        p.Children.Add(b);
        return new Border { Background = AppTheme.Brush("#FFF2D6"), BorderBrush = AppTheme.Brush("#F2C46B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), Child = p };
    }

    // ================================================================== hành động có sẵn (LanPlanSync — như LanView cũ)

    private async Task Claim()
    {
        var plan = S.Accounts.Plan;
        var old = plan?.Chu is { } c && c.Id != S.Lan.MachineId ? c.Name : null;
        if (!await Ui.Confirm(this, "Máy này sẽ làm Máy chủ chính (giữ database chính cho mọi máy)."
                + (old is null ? "" : $"\n\nMáy chủ cũ ({old}) trả vai sau khi máy này chép đủ dữ liệu, rồi thành máy con. Không mất dữ liệu.")
                + "\n\nMáy chủ nên là máy luôn bật.", old is null ? "Bật máy chủ" : "Chuyển máy chủ", old is null ? "Bật" : "Chuyển")) return;
        string msg = "";
        if (!await Ui.RunAsync(this, async () => msg = await LanPlanSync.ClaimServerAsync(S))) return;
        Hook();
        App.PublishLan();
        Render();
        await Ui.Message(this, msg, "Máy chủ");
    }

    private async Task Release()
    {
        if (!await Ui.Confirm(this, "Tắt máy chủ trên máy này?\n\nCác máy con làm tiếp trên dữ liệu trong máy, bật máy chủ lại (ở máy này hoặc máy khác) thì tự gộp.", "Tắt máy chủ", "Tắt")) return;
        string msg = "";
        if (!await Ui.RunAsync(this, async () => msg = await LanPlanSync.ReleaseServerAsync(S))) return;
        Hook();
        Render();
        await Ui.Message(this, msg, "Tắt máy chủ");
    }

    /// <summary>Địa chỉ Internet của máy chủ (tên miền đường hầm Cloudflare) — như khung "Máy chủ từ xa" cũ (1.113).</summary>
    private async Task PublicUrlDialog()
    {
        var lan = S.Lan;
        var box = new TextBox { Text = lan.PublicUrl, Watermark = "kho.kcenter.xyz", Margin = new Thickness(0, 8, 0, 0) };
        var msg = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0), Tag = "keep" };
        var info = new SelectableTextBlock
        {
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            Text = $"Cloudflare Tunnel trên máy này trỏ Public hostname về HTTP localhost:{lan.HttpPort}. Lưu → địa chỉ ghi lên Google Sheet chung, máy ở xa chỉ cần đăng nhập là tự nối."
                   + (lan.HttpError.Length > 0 ? "\n⚠ " + lan.HttpError : ""),
        };
        var test = new Button { Content = "Kiểm tra nối từ xa" };
        var save = new Button { Content = "Lưu địa chỉ", Classes = { "primary" } };
        var close = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "Địa chỉ Internet (tên miền đường hầm) của máy chủ", FontWeight = FontWeight.SemiBold });
        body.Children.Add(box); body.Children.Add(info); body.Children.Add(msg);
        body.Children.Add(Ui.Buttons(close, test, save));
        var w = Ui.Dialog("Địa chỉ Internet của máy chủ", body, 540);
        close.Click += (_, _) => w.Close();
        save.Click += async (_, _) =>
        {
            if (!await Ui.Run(w, () => LanService.SetPublicUrl(S.Settings, box.Text))) return;
            var u = S.Lan.PublicUrl; box.Text = u;
            S.Audit.Log(S.Session.Username, "Địa chỉ máy chủ từ xa", u.Length > 0 ? u : "(bỏ)");
            msg.Foreground = AppTheme.Brush("#1A2433"); msg.Text = "Đang ghi lên Google Sheet chung…";
            var err = await LanPlanSync.PublishAsync(S);
            msg.Text = err ?? (u.Length > 0 ? "Đã lưu và ghi lên Google Sheet chung. Bấm «Kiểm tra nối từ xa» để thử." : "Đã bỏ địa chỉ Internet.");
        };
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            msg.Foreground = AppTheme.Brush("#5D6B78"); msg.Text = "Đang thử nối qua Internet…";
            try { var r = await S.Lan.TestPublicAsync(box.Text); msg.Foreground = AppTheme.Brush("#1E7B4A"); msg.Text = "✓ " + r; }
            catch (Exception ex)
            {
                msg.Foreground = AppTheme.Brush("#B42318");
                msg.Text = "✖ " + ex.Message + $"\nKiểm tra: cloudflared đang chạy, Public hostname trỏ HTTP localhost:{S.Lan.HttpPort}, DNS tên miền đã chuyển sang Cloudflare.";
            }
            finally { test.IsEnabled = true; }
        };
        await Ui.Show(this, w);
        Render();
    }

    private static readonly string[] LogWords = { "lan", "máy chủ", "nối lại", "vai", "drive dùng chung", "dự phòng", "trùng sửa" };
    internal static bool IsLanLog(string action) { var a = action.ToLowerInvariant(); return LogWords.Any(a.Contains); }

    private async Task ShowLog()
    {
        var since = DateTime.Now.AddDays(-30);
        var rows = S.Audit.Recent(1500).Where(e => e.At >= since && IsLanLog(e.Action)).Take(300)
            .Select(e => new[] { e.At.ToString("dd/MM HH:mm"), e.Action, e.Detail, e.Username }).ToList();
        var m = new GridModel { ReadOnly = true };
        m.Load(new[] { new GridColumn("at", "Lúc", 100), new GridColumn("viec", "Việc", 190), new GridColumn("ct", "Chi tiết", 460), new GridColumn("ai", "Người", 100) }, rows);
        var g = new SheetGrid(m) { SpareRows = 0, RowChecks = false, Height = 440, Status = rows.Count == 0 ? "Chưa có việc nối máy nào trong 30 ngày" : $"{rows.Count} việc · 30 ngày" };
        var close = new Button { Content = "Đóng", IsCancel = true, IsDefault = true };
        var body = new StackPanel { Children = { g, Ui.Buttons(close) } };
        var w = Ui.Dialog("Nhật ký nối máy", body, 900);
        close.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
    }

    // ================================================================== khối dựng dùng chung

    private static Run Muted(string t) => new(t) { Foreground = AppTheme.Brush("#5D6B78") };
    private static Run Plain(string t) => new(t) { Foreground = AppTheme.Brush("#1A2433"), FontWeight = FontWeight.SemiBold };
    private static Run Ok(string t) => new(t) { Foreground = AppTheme.Brush("#1E7B4A"), FontWeight = FontWeight.Bold };
    private static Run Bad(string t) => new(t) { Foreground = AppTheme.Brush("#B42318"), FontWeight = FontWeight.SemiBold };
    private static Run Warn(string t) => new(t) { Foreground = AppTheme.Brush("#9A5B00"), FontWeight = FontWeight.SemiBold };
    private static string Short(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private static TextBlock Line(params Run[] runs)
    {
        var t = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep", Inlines = new InlineCollection() };
        foreach (var r in runs) t.Inlines!.Add(r);
        return t;
    }

    private static TextBlock Note(string t) => new() { Text = t, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "keep" };

    internal static Border Pill(string kind, string text)
    {
        var b = new Border { Classes = { "pill" }, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = text } };
        if (kind is "ok" or "warn" or "bad" or "info" or "accent") b.Classes.Add(kind);
        else { b.Background = AppTheme.Brush("#EEF1F5"); ((TextBlock)b.Child).Foreground = AppTheme.Brush("#5D6B78"); }
        return b;
    }

    /// <summary>Khung trạng thái: [pill] tiêu đề [pill phụ] · các dòng phụ · nút bên phải.</summary>
    private static Control Head(string kind, string pill, string title, List<Control> subs, Control? actions, Control[]? extra = null)
    {
        var top = new WrapPanel { ItemSpacing = 10, LineSpacing = 4 };
        top.Children.Add(Pill(kind, pill));
        top.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        if (extra is not null) foreach (var e in extra) top.Children.Add(e);
        var left = new StackPanel { Spacing = 5 };
        left.Children.Add(top);
        foreach (var s in subs) left.Children.Add(s);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14 };
        g.Children.Add(left);
        if (actions is not null) { actions.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(actions, 1); g.Children.Add(actions); }
        return g;
    }

    private static StackPanel Row(params Control[] c)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var x in c) p.Children.Add(x);
        return p;
    }

    /// <summary>1.129: máy chủ chạy ẩn — mặc định bật (đóng cửa sổ chỉ ẩn xuống khay, máy chủ vẫn chạy).</summary>
    private async Task ToggleChayAn()
    {
        var on = !ChayAn.Enabled;
        App.Services.Settings.Set(ChayAn.Key, on ? "1" : "0");
        ChayAn.Sync();
        await Ui.Message(this, on
            ? "Đã BẬT chạy ẩn: trên máy chủ, đóng cửa sổ chỉ ẩn phần mềm xuống khay hệ thống (cạnh đồng hồ) — máy chủ vẫn chạy. Thoát hẳn: chuột phải biểu tượng ở khay › Thoát hẳn."
            : "Đã TẮT chạy ẩn: đóng cửa sổ là thoát hẳn phần mềm (máy chủ tắt theo).", "Máy chủ chạy ẩn");
        Render();
    }

    private static void Item(MenuFlyout m, string text, Func<Task> click)
    {
        var mi = new MenuItem { Header = text };
        mi.Click += async (_, _) => await click();
        m.Items.Add(mi);
    }

    private static Button Small(string text, Func<Task> click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3), FontSize = 12.5 };
        b.Click += async (_, _) => await click();
        return b;
    }

    private void PutTile(int col, string label, int n, string? kind, Action? click)
    {
        var p = new StackPanel { Spacing = 2 };
        p.Children.Add(new TextBlock { Text = n.ToString("#,0"), FontSize = 24, FontWeight = FontWeight.Bold, Foreground = AppTheme.Brush(kind == "warn" ? "#9A5B00" : "#1A2433") });
        p.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Foreground = AppTheme.Brush("#5D6B78") });
        var b = new Border { Classes = { "card" }, Padding = new Thickness(16, 10), Child = p };
        if (kind == "warn") { b.Background = AppTheme.Brush("#FFF2D6"); b.BorderBrush = AppTheme.Brush("#F2C46B"); }
        if (click is not null && n > 0)
        {
            b.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(b, "Bấm để xem danh sách bên dưới");
            b.PointerReleased += (_, _) => click();
        }
        Grid.SetColumn(b, col); _tiles.Children.Add(b);
    }

    private static Border Box(string label, string value, string? pill)
    {
        var p = new StackPanel { Spacing = 3 };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        top.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = AppTheme.Brush("#5D6B78"), VerticalAlignment = VerticalAlignment.Center });
        if (pill is not null) top.Children.Add(Pill("ok", pill));
        p.Children.Add(top);
        p.Children.Add(new TextBlock { Text = value, FontSize = 13.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var b = new Border { Classes = { "card" }, Padding = new Thickness(14, 10), Child = p };
        if (pill is not null) { b.BorderBrush = AppTheme.Brush("#1F6FD6"); b.BorderThickness = new Thickness(2); }
        return b;
    }

    private static void Put(Grid g, int col, Control c) { Grid.SetColumn(c, col); g.Children.Add(c); }

    private static Border SoftBox(string bg, string fg, string title, string text)
    {
        var p = new StackPanel { Spacing = 4 };
        p.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, Foreground = AppTheme.Brush(fg) });
        p.Children.Add(new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep", Foreground = AppTheme.Brush("#1A2433") });
        return new Border { Background = AppTheme.Brush(bg), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), Child = p };
    }
}

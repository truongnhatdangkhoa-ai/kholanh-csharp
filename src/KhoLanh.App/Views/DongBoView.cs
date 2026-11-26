using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Lan;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Views;

/// <summary>
/// ĐỒNG BỘ &amp; SAO LƯU (1.105 — người dùng chọn phương án A, mẫu anh-giao-dien/mau-he-thong/A3–A6): MỌI thứ đồng bộ ở 1 trang, 4 tab:
///  • Tình trạng: 3 thẻ (Giữa các máy · Điện thoại · Sao lưu) + nút chính "Đồng bộ tất cả ngay" + bảng việc đồng bộ gần đây (nhật ký).
///  • Giữa các máy (Quản trị): "Máy này lấy dữ liệu từ đâu?" — 3 lựa chọn lớn (Chỉ máy này / Mạng LAN / Thư mục - Drive dùng chung), dưới là phần cài của lựa chọn.
///  • Điện thoại: <see cref="PhoneSyncView"/> (kết nối đổi ở Hệ thống › Kết nối).
///  • Sao lưu: 2 thẻ trạng thái (Trên máy này / Google Drive) + danh sách từng nơi.
/// Thay "Dữ liệu &amp; sao lưu" (1.68) và "Đồng bộ điện thoại". Khóa cũ "dulieu" / "phone" / "lan" / "drive" / "saoluu" / "dcn" vẫn mở đúng tab.
/// </summary>
public sealed class DongBoView : UserControl, IPage, IKeepFresh
{
    public string Title => "Đồng bộ & sao lưu";
    public string Subtitle => "Tình trạng đồng bộ của máy này: giữa các máy (LAN / Drive dùng chung), điện thoại, sao lưu. Kết nối (mã, Gmail) ở Hệ thống › Kết nối.";

    private static KhoLanh.Core.AppServices S => App.Services;
    private static bool Admin => Roles.IsAdmin(S.Session.Role);
    private static bool CanBackup => Admin || S.Session.Can("backup");
    private static bool CanPhone => S.Session.Can("phone");

    internal TabControl Tabs { get; } = new();
    private readonly Grid _tiles = new() { ColumnSpacing = 10 };
    private readonly GridModel _logM = new() { ReadOnly = true };
    private readonly SheetGrid _log;
    private readonly TextBlock _msg = new() { Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep", VerticalAlignment = VerticalAlignment.Center };
    private int _tabTinhTrang = -1, _tabMay = -1, _tabPhone = -1, _tabSaoLuu = -1;

    /// <summary>Mở thẳng: "lan" / "drive" → Giữa các máy · "phone" → Điện thoại · "saoluu" / "dcn" → Sao lưu.</summary>
    public DongBoView(string start = "")
    {
        _log = new SheetGrid(_logM) { SpareRows = 0, RowChecks = false };
        _log.CellBackground = (r, c) =>
        {
            if (c != 4 || r >= _logM.RowCount) return null;
            var v = _logM.Get(r, 4);
            return v.StartsWith("Lỗi") ? AppTheme.Brush("#FDE8E6") : v.StartsWith("Xem") ? AppTheme.Brush("#FFF2D6") : null;
        };
        int n = 0;
        if (CanBackup || CanPhone) { Tabs.Items.Add(Tab("Tình trạng", BuildStatus())); _tabTinhTrang = n++; }
        if (Admin) { Tabs.Items.Add(Tab("Giữa các máy", BuildMachines(start))); _tabMay = n++; }
        if (CanPhone) { Tabs.Items.Add(Tab("Điện thoại", new PhoneSyncView())); _tabPhone = n++; }
        if (CanBackup) { Tabs.Items.Add(Tab("Sao lưu", BuildBackup(start))); _tabSaoLuu = n++; }
        var pick = start switch { "lan" or "drive" => _tabMay, "phone" => _tabPhone, "saoluu" or "dcn" => _tabSaoLuu, _ => _tabTinhTrang };
        Tabs.SelectedIndex = Math.Max(0, pick);
        Content = Tabs;
        AttachedToVisualTree += (_, _) => RenderStatus();
        Tabs.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.SelectingItemsControl.SelectedIndexProperty && Tabs.SelectedIndex == _tabTinhTrang) RenderStatus(); };
    }

    private static TabItem Tab(string h, Control c) => new() { Header = h, Content = new Border { Padding = new Thickness(0, 6, 0, 0), Child = c } };

    // ---------------- Tình trạng ----------------

    private Control BuildStatus()
    {
        var row = new ToolRow { Margin = new Thickness(0, 0, 0, 10) };
        var refresh = new Button { Content = Icon.Make("refresh", 15), Padding = new Thickness(8, 4) }; ToolTip.SetTip(refresh, "Đọc lại tình trạng");
        refresh.Click += (_, _) => RenderStatus();
        row.Children.Add(refresh); row.Children.Add(_msg); row.Children.Add(ToolRow.Gap());
        var all = new Button { Content = "⇅ Đồng bộ tất cả ngay", Classes = { "primary" } };
        ToolTip.SetTip(all, "Tài khoản dùng chung + điện thoại + Drive dùng chung (nếu có). LAN tự đồng bộ liên tục, không cần bấm.");
        all.Click += async (_, _) => await SyncAll(all);
        row.Children.Add(all);
        _tiles.ColumnDefinitions = new ColumnDefinitions("*,*,*");
        var dock = new DockPanel();
        DockPanel.SetDock(row, Dock.Top); dock.Children.Add(row);
        var tiles = new Border { Child = _tiles, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(tiles, Dock.Top); dock.Children.Add(tiles);
        var cap = new TextBlock { Text = "Việc đồng bộ gần đây (7 ngày)", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(cap, Dock.Top); dock.Children.Add(cap);
        dock.Children.Add(_log);
        return dock;
    }

    private async Task SyncAll(Button btn)
    {
        btn.IsEnabled = false; var parts = new List<string>();
        _msg.Text = "Đang đồng bộ…";
        try
        {
            if (S.Accounts.Enabled)
                try { var n = await S.Accounts.PullAsync(TimeSpan.FromSeconds(30)); parts.Add($"Tài khoản: {n}"); }
                catch (Exception e) { parts.Add("Tài khoản lỗi: " + Ui.Describe(e)); }
            if (S.Phone.Enabled && CanPhone)
                try
                {
                    var t = await PhoneAutoSync.Exclusive(async () => { var (p, push) = await Task.Run(() => S.PhoneSync.SyncAsync()); return $"nhận {p.NewPhieu} phiếu, đẩy {push.Rows.Count} bảng" + (push.Errors.Count + p.Errors.Count > 0 ? " (có lỗi)" : ""); });
                    parts.Add("Điện thoại: " + t);
                }
                catch (Exception e) { parts.Add("Điện thoại lỗi: " + Ui.Describe(e)); }
            if (S.Drive.Enabled && Admin)
                try { parts.Add("Drive dùng chung: " + await Task.Run(() => S.Drive.SyncNow())); }
                catch (Exception e) { parts.Add("Drive lỗi: " + Ui.Describe(e)); }
            _msg.Text = parts.Count == 0 ? "Chưa có gì để đồng bộ — máy này chưa kết nối (xem Hệ thống › Kết nối)." : "Xong " + DateTime.Now.ToString("HH:mm") + " · " + string.Join(" · ", parts);
        }
        finally { btn.IsEnabled = true; RenderStatus(); }
    }

    private static Border Pill(string kind, string text)
    {
        var b = new Border { Classes = { "pill" }, HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = text } };
        if (kind is "ok" or "warn" or "bad") b.Classes.Add(kind);
        else { b.Background = AppTheme.Brush("#EEF1F5"); ((TextBlock)b.Child).Foreground = AppTheme.Brush("#5D6B78"); }
        return b;
    }

    /// <summary>Tình trạng từng mục (công cụ chụp kiểm): tên → (kind, chữ nhãn).</summary>
    internal Dictionary<string, (string Kind, string Text)> State { get; } = new();

    private Border Tile(string title, string icon, string iconBg, string kind, string pill, string body, params Control[] acts)
    {
        State[title] = (kind, pill);
        var ic = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(10), Background = AppTheme.Brush(iconBg),
            Child = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = Icon.Make(icon, 20) } };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        head.Children.Add(ic);
        head.Children.Add(new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Bold }, Pill(kind, pill) } });
        var p = new StackPanel { Spacing = 10 };
        p.Children.Add(head);
        p.Children.Add(new TextBlock { Text = body, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Tag = "keep", LineHeight = 18 });
        var ar = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var a in acts) ar.Children.Add(a);
        p.Children.Add(ar);
        var kindBorder = kind == "warn" ? "#F2C46B" : kind == "bad" ? "#E2938A" : null;
        var b = new Border { Classes = { "card" }, Padding = new Thickness(18, 16), Child = p };
        if (kindBorder is not null) b.BorderBrush = AppTheme.Brush(kindBorder);
        return b;
    }

    private Button Small(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3), FontSize = 12.5 };
        b.Click += (_, _) => click();
        return b;
    }

    private void GoTab(int i) { if (i >= 0) Tabs.SelectedIndex = i; }

    internal void RenderStatus()
    {
        _tiles.Children.Clear(); State.Clear();
        var col = 0;
        void Put(Border b) { Grid.SetColumn(b, col++); _tiles.Children.Add(b); }

        // 1) giữa các máy
        var lan = S.Lan; var mode = CurrentMode();
        string k1, p1, b1;
        if (mode == 2)
        {
            k1 = lan.Health == 1 ? "ok" : "bad"; p1 = lan.Health == 1 ? "● Đang chạy tốt" : "● Mất kết nối";
            b1 = $"Mạng LAN trong kho · máy này là {LanService.RoleLabel(lan.Role).ToLowerInvariant()}\n"
                 + (lan.Serving ? $"{lan.Machines().Count} máy khác đang nối" : lan.Online ? $"Nối máy chủ {lan.ServerName}" : "Chưa nối được máy chủ — kiểm tra máy chủ còn bật, cùng mạng");
        }
        else if (mode == 1)
        {
            var d = S.Drive;
            k1 = d.Status.StartsWith("Lỗi") || d.Status.StartsWith("Không") ? "bad" : "ok"; p1 = k1 == "ok" ? "● Đang dùng Drive" : "● Lỗi";
            b1 = "Thư mục / Google Drive dùng chung · " + (d.Mode == DriveSyncService.ModeMain ? "máy chính" : "máy xem từ xa")
                 + (d.LastOk != default ? $"\nĐồng bộ lần cuối {d.LastOk:HH:mm dd/MM}" : "") + (d.Status.Length > 0 ? "\n" + d.Status : "");
        }
        else { k1 = "off"; p1 = "○ Chỉ máy này"; b1 = "Dữ liệu chỉ nằm trên máy này, không dùng chung với máy khác."; }
        Put(Tile("Giữa các máy", "network", "#E6F0FC", k1, p1, b1, Admin ? new Control[] { Small("Xem chi tiết", () => GoTab(_tabMay)) } : Array.Empty<Control>()));

        // 2) điện thoại
        if (CanPhone)
        {
            var ph = S.Phone; var st = S.PhoneSync.Status;
            var last = st.Values.Select(x => DateTime.TryParse(x.At, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime() : (DateTime?)null).Where(x => x is not null).DefaultIfEmpty().Max();
            var err = ph.LastError; var tabErr = st.Values.Count(x => x.Error.Length > 0);
            string k2 = !ph.Enabled ? "warn" : err is not null || tabErr > 0 ? "bad" : "ok";
            string p2 = !ph.Enabled ? "● Chưa nối" : k2 == "bad" ? "● Có lỗi" : last is { } l ? $"● Đồng bộ lúc {l:HH:mm}" : "● Đã nối";
            var auto = PhoneAutoSync.Minutes;
            var b2 = !ph.Enabled ? "Chưa nối Google Sheet điện thoại — vào Hệ thống › Kết nối, dán mã kết nối máy."
                : (auto > 0 ? $"Tự đồng bộ mỗi {auto} phút" : "Tự đồng bộ đang tắt") + $" · đẩy {S.PhoneSync.Pick.Count} bảng"
                  + (err is not null ? $"\nLỗi lúc {err.At:HH:mm dd/MM}: {err.Msg}" : tabErr > 0 ? $"\n{tabErr} bảng đẩy lỗi — xem tab Điện thoại" : "");
            var acts = new List<Control>();
            if (!ph.Enabled) acts.Add(Small("Kết nối…", () => MainWindow.Of(this)?.Navigate("ketnoi")));
            else acts.Add(Small("Xem chi tiết", () => GoTab(_tabPhone)));
            Put(Tile("Điện thoại", "phone", "#E3F4EA", k2, p2, b2, acts.ToArray()));
        }

        // 3) sao lưu
        if (CanBackup)
        {
            var files = S.Backup.ListAutoBackups();
            var newest = files.Where(f => !f.Name.StartsWith("truoc-")).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            var dc = S.DriveCaNhan.CanUse ? S.DriveCaNhan.Config : null;
            var local = newest is not null && (DateTime.Now - newest.LastWriteTime).TotalDays < 2;
            string k3 = !local ? "bad" : dc is { Linked: true } && dc.LastErr.Length == 0 ? "ok" : "warn";
            string p3 = k3 == "ok" ? "● Trên máy + Google Drive" : k3 == "warn" ? "● Chỉ có trên máy này" : "● Chưa sao lưu gần đây";
            var b3 = (newest is not null ? $"Trên máy: {newest.LastWriteTime:HH:mm dd/MM} (tự động mỗi ngày)" : "Trên máy: chưa có bản nào")
                     + "\nGoogle Drive: " + (dc is null ? "—" : !dc.Linked ? "chưa bật — máy hỏng sẽ mất dữ liệu" : dc.Last is { } dl ? $"{dc.Email} · {dl:HH:mm dd/MM}" : dc.Email + " · chưa sao lưu lần nào")
                     + (dc is { LastErr.Length: > 0 } ? "\nLỗi Drive: " + dc.LastErr : "");
            var acts = new List<Control> { Small("Xem chi tiết", () => GoTab(_tabSaoLuu)) };
            if (dc is { Linked: false }) acts.Add(Small("Bật sao lưu Drive", () => MainWindow.Of(this)?.Navigate("dcn")));
            Put(Tile("Sao lưu", "save", "#FFF2D6", k3, p3, b3, acts.ToArray()));
        }
        _tiles.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", Math.Max(1, col))));

        // nhật ký đồng bộ 7 ngày
        var since = DateTime.Now.AddDays(-7);
        var rows = S.Audit.Recent(600).Where(e => e.At >= since && IsSync(e.Action)).Take(200)
            .Select(e => new[] { e.At.ToString("dd/MM HH:mm"), Kind(e.Action), e.Action + (e.Detail.Length > 0 ? " — " + e.Detail : ""), e.Username, Result(e.Action, e.Detail) }).ToList();
        _logM.Load(new[]
        {
            new GridColumn("at", "Lúc", 110), new GridColumn("loai", "Loại", 120), new GridColumn("nd", "Nội dung", 620), new GridColumn("ai", "Người", 110), new GridColumn("kq", "Kết quả", 110),
        }, rows);
        _log.Refresh();
        _log.Status = rows.Count == 0 ? "Chưa có việc đồng bộ nào trong 7 ngày" : $"{rows.Count} việc · {rows.Count(r => r[4].StartsWith("Lỗi"))} lỗi";
    }

    private static readonly string[] SyncWords = { "đồng bộ", "lan", "drive", "sao lưu", "khôi phục", "dùng chung", "kết nối", "điện thoại", "vai", "máy chủ" };
    internal static bool IsSync(string action) { var a = action.ToLowerInvariant(); return SyncWords.Any(a.Contains); }
    private static string Kind(string action)
    {
        var a = action.ToLowerInvariant();
        return a.Contains("điện thoại") ? "Điện thoại" : a.Contains("sao lưu") || a.Contains("khôi phục") ? "Sao lưu" : a.Contains("tài khoản") || a.Contains("kết nối") ? "Tài khoản" : "Giữa các máy";
    }
    private static string Result(string action, string detail) =>
        (action + " " + detail).Contains("lỗi", StringComparison.OrdinalIgnoreCase) ? "Lỗi" : action.Contains("gộp", StringComparison.OrdinalIgnoreCase) ? "Xem lại" : "Xong";

    /// <summary>Kiểu đồng bộ hiện tại: 0 chỉ máy này · 1 thư mục / Drive dùng chung · 2 máy chủ LAN.</summary>
    internal static int CurrentMode()
    {
        if (S.Lan.Enabled && S.Lan.Role != LanService.RoleRemote) return 2;
        if (S.Drive.Mode is DriveSyncService.ModeMain or DriveSyncService.ModeRemote || S.Lan.Role == LanService.RoleRemote) return 1;
        return 0;
    }

    // ---------------- Giữa các máy ----------------

    private Control BuildMachines(string start)
    {
        var mode = CurrentMode();
        var host = new ContentControl();
        var opts = new List<Border>();
        var shown = start switch { "lan" => 2, "drive" => 1, _ => mode };
        Control[] panes = { LocalPane(mode), new DriveSyncView(), new LanView() };
        (string T, string D)[] info =
        {
            ("Chỉ máy này", "Làm một mình, không chung dữ liệu với máy khác."),
            ("Thư mục / Drive dùng chung", "Máy ở xa xem dữ liệu qua Google Drive (chậm hơn)."),
            ("Mạng LAN trong kho", "Các máy cùng mạng dùng chung 1 dữ liệu, thấy ngay."),
        };
        int[] order = { 0, 2, 1 };   // hiện: Chỉ máy này · Mạng LAN (khuyên dùng) · Drive dùng chung
        void Select(int m)
        {
            shown = m; host.Content = panes[m];
            for (var i = 0; i < opts.Count; i++)
            {
                var on = order[i] == m;
                opts[i].BorderBrush = AppTheme.Brush(on ? "#1F6FD6" : "#DDE3EA");
                opts[i].Background = on ? AppTheme.Brush("#F3F8FE") : AppTheme.Card;
                opts[i].BorderThickness = new Thickness(on ? 2 : 1);
            }
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 10, Margin = new Thickness(0, 6, 0, 10) };
        for (var i = 0; i < 3; i++)
        {
            var m = order[i];
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            title.Children.Add(new TextBlock { Text = info[m].T, FontWeight = FontWeight.Bold, FontSize = 14 });
            if (m == mode) title.Children.Add(Pill("ok", "Máy này đang dùng"));
            else if (m == 2) title.Children.Add(Pill("off", "khuyên dùng"));
            var b = new Border
            {
                Classes = { "card" }, Padding = new Thickness(14, 10), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Spacing = 3, Children = { title, new TextBlock { Text = info[m].D, Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep" } } },
            };
            b.PointerReleased += (_, _) => Select(m);
            Grid.SetColumn(b, i); row.Children.Add(b); opts.Add(b);
        }
        var head = new StackPanel { Spacing = 0 };
        head.Children.Add(new TextBlock { Text = "Máy này lấy dữ liệu từ đâu?", FontSize = 14.5, FontWeight = FontWeight.Bold });
        head.Children.Add(row);
        head.Children.Add(new TextBlock { Text = "Bấm vào một lựa chọn để xem / đổi cài đặt của cách đó. Mỗi máy chỉ dùng MỘT cách; sao lưu (tab Sao lưu) bật được với mọi cách.", Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "keep", Margin = new Thickness(0, 0, 0, 8) });
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head); dock.Children.Add(host);
        Select(shown);
        return dock;
    }

    private static Control LocalPane(int mode)
    {
        var p = new StackPanel { Spacing = 8, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        p.Children.Add(new TextBlock { Text = "Chỉ máy này (không dùng LAN / Drive)", Classes = { "section" } });
        p.Children.Add(DuLieuView.Muted("Dữ liệu nằm trong file database trên máy này, không dùng chung với máy khác. Hợp với máy làm việc một mình.\n• App tự sao lưu mỗi ngày (tab Sao lưu › Trên máy này).\n• Muốn có bản ngoài máy (máy hỏng vẫn còn): tab Sao lưu › Google Drive — đăng nhập Gmail, tự sao lưu theo lịch."));
        var lan = App.Services.Lan;
        p.Children.Add(mode == 0
            ? new TextBlock { Text = "✔ Máy này đang dùng cách này.", Foreground = AppTheme.Brush("#1E7B4A"), FontWeight = FontWeight.SemiBold }
            : DuLieuView.Muted("Máy này đang dùng cách khác. Muốn về cách này: " + (mode == 2
                ? (lan.Role == LanService.RoleServer
                    ? "Quản trị bấm \"Tắt máy chủ trên máy này\" ở tab Giữa các máy › Máy chủ LAN."
                    : "máy con / dự phòng tự tách ra dùng riêng khi khôi phục một bản sao lưu lúc đang NGOÀI mạng LAN (không nối máy chủ) — tab Sao lưu.")
                : "chọn Thư mục / Drive dùng chung › Tắt › Lưu.")));
        // 1.114: máy đã tách ra dùng riêng sau khi khôi phục → nối lại
        if (mode == 0 && App.Services.Settings.Get(KhoLanh.Core.Services.BackupService.DetachedKey) == "1")
        {
            p.Children.Add(DuLieuView.Muted("Máy này đã TÁCH khỏi mạng LAN sau khi khôi phục bản sao lưu — dữ liệu riêng, không tự nối lại máy chủ khi đăng nhập. "
                + "Nối lại: dữ liệu riêng của máy này được chụp vào thư mục SaoLuu (truoc-noi-lai-…) rồi thay bằng dữ liệu máy chủ."));
            var b = new Button { Content = "Nối lại mạng LAN", HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += async (_, _) =>
            {
                if (!await Ui.Confirm(b, "Nối lại mạng LAN: dữ liệu riêng trên máy này (đã chụp vào SaoLuu) sẽ được thay bằng dữ liệu máy chủ. Tiếp tục?", "Nối lại mạng LAN", "Nối lại")) return;
                string msg = "";
                if (await Ui.Run(b, () => msg = KhoLanh.Core.Lan.LanPlanSync.Rejoin(App.Services))) await Ui.Message(b, msg + "\n\nNên đăng nhập lại để app nạp dữ liệu máy chủ.", "Nối lại mạng LAN");
            };
            p.Children.Add(b);
        }
        return new ScrollViewer { Content = new Border { Classes = { "card" }, Child = p, HorizontalAlignment = HorizontalAlignment.Left } };
    }

    // ---------------- Sao lưu ----------------

    private Control BuildBackup(string start)
    {
        var sub = Ui.SubTabs(("Trên máy này", new LocalBackupPanel()), ("Google Drive (Gmail)", new DriveCaNhanPanel()));
        if (start == "dcn") sub.SelectedIndex = 1;
        return sub;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.TroLy;

namespace KhoLanh.App.Views;

/// <summary>
/// TRỢ LÝ HỆ THỐNG (theo "Trợ lý hệ thống" của app cũ — chỉ Quản trị). Quét CHỈ ĐỌC: lỗi phần mềm, cơ sở dữ liệu, ghi phiếu, số liệu nghiệp vụ,
/// dòng trùng, mã / tên hàng, kiểm tra chéo giữa các module, sao lưu, đồng bộ. Hiện «Việc cần chú ý hôm nay», hàng đợi đề xuất có trạng thái,
/// và soạn báo cáo để sao chép đưa cho Claude.
/// </summary>
public sealed class TroLyView : UserControl, IPage
{
    public string Title => "Trợ lý hệ thống";
    public string Subtitle => "Tự kiểm tra dữ liệu và phần mềm (chỉ đọc, không sửa gì) — chọn cảnh báo rồi sao chép báo cáo cho Claude.";

    private static TroLyService Svc => App.Services.TroLy;
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45");
    private static readonly Dictionary<string, (string Bg, string Fg, string Text)> SevStyle = new()
    {
        ["do"] = ("#FDE7E7", "#B42318", "NẶNG"), ["vang"] = ("#FFF4CC", "#8A5A00", "CẦN XEM"), ["xanh"] = ("#E6F4EA", "#1E7B34", "ỔN"), ["xam"] = ("#EEF1F5", "#4B5565", "NHẮC"),
    };

    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _kpi = new() { ItemSpacing = 8, LineSpacing = 6 };
    private readonly StackPanel _chuY = new() { Spacing = 6 };
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly ComboBox _filter = new() { MinWidth = 170, ItemsSource = new[] { "Mọi mức", "Chỉ Nặng", "Nặng + Cần xem", "Chỉ Ổn" }, SelectedIndex = 2 };
    private readonly CheckBox _showSkipped = new() { Content = "Hiện mục đã bỏ qua / lỗi từ nguồn", FontSize = 12.5 };
    private readonly CheckBox _anDanh = new() { Content = "Ẩn tên khách / người dùng trong báo cáo", IsChecked = true, FontSize = 12.5 };
    private readonly HashSet<string> _sel = new();
    private readonly Button _quick, _deep;
    private readonly GridModel _qm = new() { ReadOnly = true };
    private readonly SheetGrid _qg;
    private readonly TextBlock _qInfo = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private List<(string Id, TroLyQueue.Item Item)> _queue = new();
    private bool _busy;

    public TroLyView()
    {
        _qg = new SheetGrid(_qm) { SpareRows = 0 };
        _quick = Btn("⟳ Quét nhanh", () => _ = Scan(false), "Vài giây: kiểm tra nhanh toàn bộ (không mở thử bản sao lưu)");
        _deep = Btn("🔍 Quét sâu", () => _ = Scan(true), "Kiểm tra toàn vẹn đầy đủ + thử mở bản sao lưu mới nhất (lâu hơn)");
        _filter.SelectionChanged += (_, _) => Render();
        _showSkipped.IsCheckedChanged += (_, _) => Render();

        var actions = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        actions.Children.Add(Btn("📋 Sao chép báo cáo cho Claude", () => _ = CopyReport(), "Các cảnh báo đã tick → báo cáo dán thẳng vào Claude; cảnh báo chuyển sang «Đã gửi Claude»"));
        actions.Children.Add(Btn("✔ Đã sửa (chờ kiểm tra)", () => _ = Mark("daSua"), "Lần quét sau còn thấy → Tái phát; hết → Đã đóng"));
        actions.Children.Add(Btn("Bỏ qua", () => _ = Mark("boQua")));
        actions.Children.Add(Btn("Lỗi từ nguồn", () => _ = Mark("loiNguon"), "Do file Excel / phần mềm kho gốc — chỉ nhắc 1 lần, không đếm tái phát"));
        actions.Children.Add(Btn("Mở lại (Mới)", () => _ = Mark("moi")));
        actions.Children.Add(Btn("Chọn hết", () => { foreach (var f in Visible()) _sel.Add(f.Id); Render(); }));
        actions.Children.Add(Btn("Bỏ chọn", () => { _sel.Clear(); Render(); }));

        var top = new DockPanel();
        var scanBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        scanBtns.Children.Add(_quick); scanBtns.Children.Add(_deep);
        DockPanel.SetDock(scanBtns, Dock.Left); top.Children.Add(scanBtns);
        _status.Margin = new Thickness(10, 0, 0, 0); top.Children.Add(_status);

        var chuYBox = Card("Việc cần chú ý hôm nay", _chuY, Btn("⟳", () => LoadChuY(), "Đọc lại"));

        var filters = new WrapPanel { ItemSpacing = 10, LineSpacing = 6 };
        filters.Children.Add(_filter); filters.Children.Add(_showSkipped); filters.Children.Add(_anDanh);
        // 1.99 (người dùng: "bảng cảnh báo quá nhỏ"): danh sách cảnh báo có thanh cuộn riêng, cao hết phần còn lại của trang
        var findTop = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        findTop.Children.Add(_kpi); findTop.Children.Add(filters); findTop.Children.Add(actions);
        var findPanel = new DockPanel();
        DockPanel.SetDock(findTop, Dock.Top); findPanel.Children.Add(findTop);
        findPanel.Children.Add(new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, MinHeight = 360 });

        var qTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        qTools.Children.Add(Btn("⟳ Tải lại", LoadQueue));
        qTools.Children.Add(Btn("Xóa lịch sử đã xử lý", () => _ = PurgeDone(), "Xóa các mục Đã đóng / Bỏ qua (mục đang xử lý không bao giờ bị xóa)"));
        var qPanel = new DockPanel();
        var qTop = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6) }; qTop.Children.Add(qTools); qTop.Children.Add(_qInfo);
        DockPanel.SetDock(qTop, Dock.Top); qPanel.Children.Add(qTop);
        qPanel.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = _qg, MinHeight = 300 });

        // 1.99: Việc cần chú ý hôm nay thành tab (trước là khung cố định phía trên, đẩy bảng cảnh báo xuống đáy trang)
        var tabs = new TabControl();
        var qTab = new TabItem { Header = "Hàng đợi đề xuất", Content = qPanel };
        tabs.Items.Add(new TabItem { Header = "Cảnh báo", Content = findPanel });
        tabs.Items.Add(new TabItem { Header = "Việc cần chú ý hôm nay", Content = new ScrollViewer { Content = chuYBox, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } });
        tabs.Items.Add(qTab);
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs && tabs.SelectedItem == qTab) LoadQueue(); };

        var head = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(top);   // thẻ số Nặng / Cần xem / Ổn nằm đầu tab Cảnh báo (1 hàng ở đây được kéo lên ngang tên trang)
        var body = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); body.Children.Add(head); body.Children.Add(tabs);
        Content = body;

        Svc.Scanned += OnScanned;
        DetachedFromVisualTree += (_, _) => Svc.Scanned -= OnScanned;
        AttachedToVisualTree += (_, _) =>
        {
            LoadChuY();
            if (Svc.Last is null) _ = Scan(false); else { Svc.Refresh(); Render(); }
        };
    }

    private void OnScanned(TroLyService.ScanResult _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!_busy) Render(); });

    // ------------------------------------------------------------------ dựng giao diện

    private static Button Btn(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private static Border Card(string title, Control content, Control? right = null)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (right is not null) { DockPanel.SetDock(right, Dock.Right); head.Children.Add(right); }
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = 14, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center });
        var s = new StackPanel(); s.Children.Add(head); s.Children.Add(content);
        return new Border { Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10), Child = s };
    }

    private static Border Badge(string sev)
    {
        var (bg, fg, text) = SevStyle.GetValueOrDefault(sev, SevStyle["xam"]);
        return new Border
        {
            Background = AppTheme.Brush(bg), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = AppTheme.Brush(fg) },
        };
    }

    private static Border Kpi(string label, int value, string sev)
    {
        var (bg, fg, _) = SevStyle[sev];
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = value.ToString("#,0", Ui.Vi), FontSize = 18, FontWeight = FontWeight.Bold, Foreground = AppTheme.Brush(fg) });
        s.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Classes = { "muted" } });
        return new Border { Background = AppTheme.Brush(bg), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4), MinWidth = 100, Child = s };
    }

    private void LoadChuY()
    {
        _chuY.Children.Clear();
        List<TroLyChuY.Viec> list;
        try { list = Svc.ChuY(); }
        catch (Exception e) { _chuY.Children.Add(new TextBlock { Text = "Không đọc được: " + e.Message, Classes = { "muted" } }); return; }
        if (list.Count == 0) { _chuY.Children.Add(new TextBlock { Text = "✓ Không có việc dang dở nào.", Foreground = AppTheme.Brush("#1E7B34"), FontSize = 12.5 }); return; }
        foreach (var v in list)
        {
            var row = new DockPanel();
            var b = Badge(v.Sev); b.Margin = new Thickness(0, 1, 8, 0); DockPanel.SetDock(b, Dock.Left); row.Children.Add(b);
            if (v.Go.Length > 0)
            {
                var go = Btn("Mở →", () => MainWindow.Of(this)?.Navigate(v.Go)); go.VerticalAlignment = VerticalAlignment.Top;
                DockPanel.SetDock(go, Dock.Right); row.Children.Add(go);
            }
            var t = new StackPanel();
            t.Children.Add(new TextBlock { Text = v.Mod + " · " + v.Title, FontWeight = FontWeight.SemiBold, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            if (v.Detail.Length > 0) t.Children.Add(new TextBlock { Text = v.Detail, FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(t);
            _chuY.Children.Add(row);
        }
    }

    private IEnumerable<Finding> Visible()
    {
        var all = Svc.Last?.Findings ?? new();
        var lvl = _filter.SelectedIndex;
        return all.Where(f => (_showSkipped.IsChecked == true || f.Q is not ("boQua" or "loiNguon"))
            && lvl switch { 1 => f.Sev == "do", 2 => f.Sev != "xanh", 3 => f.Sev == "xanh", _ => true });
    }

    private void Render()
    {
        var r = Svc.Last;
        _kpi.Children.Clear(); _list.Children.Clear();
        if (r is null) { _status.Text = "Chưa quét."; return; }
        _kpi.Children.Add(Kpi("Nặng", r.Do, "do")); _kpi.Children.Add(Kpi("Cần xem", r.Vang, "vang")); _kpi.Children.Add(Kpi("Ổn", r.Xanh, "xanh"));
        if (r.BoQua > 0) _kpi.Children.Add(Kpi("Bỏ qua / từ nguồn", r.BoQua, "xam"));
        var ch = r.Changes;
        _status.Text = $"{(r.Deep ? "Quét sâu" : "Quét nhanh")} lúc {r.At:HH:mm dd/MM} · {r.Ms / 1000.0:0.0} giây" +
            (ch.Moi + ch.TaiPhat + ch.Dong > 0 ? $" · {ch.Moi} mới, {ch.TaiPhat} tái phát, {ch.Dong} đã hết" : "") +
            (r.Steps.Any(s => s.Error.Length > 0) ? " · ⚠ có bước quét lỗi (xem cảnh báo «Trợ lý hệ thống»)" : "") + " · Tự quét nhanh mỗi 6 giờ khi Quản trị đang đăng nhập.";
        var vis = Visible().ToList();
        _sel.IntersectWith(vis.Select(f => f.Id));
        if (vis.Count == 0) { _list.Children.Add(new TextBlock { Text = "Không có cảnh báo nào ở mức đang lọc.", Classes = { "muted" }, FontSize = 12.5 }); return; }
        foreach (var f in vis) _list.Children.Add(FindingCard(f));
    }

    private Control FindingCard(Finding f)
    {
        var cb = new CheckBox { IsChecked = _sel.Contains(f.Id), VerticalAlignment = VerticalAlignment.Top, IsVisible = f.Sev != "xanh" };
        cb.IsCheckedChanged += (_, _) => { if (cb.IsChecked == true) _sel.Add(f.Id); else _sel.Remove(f.Id); };
        var head = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        head.Children.Add(Badge(f.Sev));
        if (f.Q.Length > 0 && f.Sev != "xanh")
            head.Children.Add(new Border
            {
                Background = AppTheme.Brush(f.Q == "taiPhat" ? "#FDE7E7" : "#EEF1F5"), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1),
                Child = new TextBlock { Text = TroLyQueue.Label.GetValueOrDefault(f.Q, f.Q) + (f.Recur > 0 ? $" ×{f.Recur}" : ""), FontSize = 11 },
            });
        head.Children.Add(new TextBlock { Text = f.Mod, FontSize = 12, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        var body = new StackPanel { Spacing = 2 };
        body.Children.Add(head);
        body.Children.Add(new TextBlock { Text = f.Title, FontWeight = FontWeight.SemiBold, FontSize = 13.5, Foreground = Dark, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = f.Detail, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        if (f.QNote.Length > 0) body.Children.Add(new TextBlock { Text = "↳ " + f.QNote, FontSize = 12, Foreground = AppTheme.Brush("#B42318"), TextWrapping = TextWrapping.Wrap });
        foreach (var e in f.Evidence) body.Children.Add(new SelectableTextBlock { Text = "• " + e, FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        if (f.Fixes is { Count: > 0 } fx)
        {
            var ok = fx.Count(x => x.Near.Length == 0);
            var add = Btn($"＋ Thêm {ok} mã vào Danh sách mã hàng", () => _ = AddCodes(f), "Chỉ thêm mã không có mã gần giống (mã gần giống = nghi gõ sai, để bạn tự xem). Không sửa / xóa dòng đang có.");
            add.IsEnabled = ok > 0; add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(add);
        }
        var g = new DockPanel();
        cb.Margin = new Thickness(0, 0, 6, 0); DockPanel.SetDock(cb, Dock.Left); g.Children.Add(cb); g.Children.Add(body);
        return new Border { Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = g };
    }

    // ------------------------------------------------------------------ thao tác

    private async Task Scan(bool deep)
    {
        if (_busy) return;
        _busy = true; _quick.IsEnabled = _deep.IsEnabled = false;
        _status.Text = deep ? "Đang quét sâu… (kiểm tra toàn vẹn + thử mở bản sao lưu)" : "Đang quét…";
        try { await Ui.RunAsync(this, () => Task.Run(() => Svc.Scan(deep))); }
        finally { _busy = false; _quick.IsEnabled = _deep.IsEnabled = true; }
        Render(); LoadChuY();
    }

    private List<string> Selected()
    {
        var ids = _sel.ToList();
        return ids;
    }

    private async Task CopyReport()
    {
        var ids = Selected();
        if (ids.Count == 0) { await Ui.Message(this, "Tick chọn các cảnh báo cần gửi trước (ô vuông bên trái mỗi cảnh báo)."); return; }
        string? text = null;
        if (!await Ui.Run(this, () => text = Svc.Report(ids, _anDanh.IsChecked == true)) || text is null) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(text);
        _sel.Clear(); Render();
        _status.Text = $"Đã sao chép báo cáo {ids.Count} cảnh báo — dán vào Claude. Sửa xong bấm «Đã sửa» để lần quét sau xác nhận.";
    }

    private async Task Mark(string st)
    {
        var ids = Selected();
        if (ids.Count == 0) { await Ui.Message(this, "Tick chọn các cảnh báo trước."); return; }
        if (await Ui.Run(this, () => Svc.Mark(ids, st))) { _sel.Clear(); Render(); }
    }

    private async Task AddCodes(Finding f)
    {
        var ok = f.Fixes!.Count(x => x.Near.Length == 0);
        if (!await Ui.Confirm(this, $"Thêm {ok} mã chưa có vào Tổng hợp › Danh sách mã hàng (tên lấy theo file tồn)?\n\nMã có «gần giống» (nghi gõ sai) KHÔNG được thêm. Không sửa / xóa dòng đang có; có ghi nhật ký thao tác.", "Thêm mã", "Thêm")) return;
        TroLyService.AddResult? r = null;
        if (!await Ui.Run(this, () => r = Svc.AddMissingCodes(f.Id)) || r is null) return;
        await Ui.Message(this, $"Đã thêm {r.Added} mã." + (r.SkippedNear > 0 ? $" Bỏ qua {r.SkippedNear} mã gần giống mã có sẵn (tự xem lại)." : ""));
        await Scan(false);
    }

    private void LoadQueue()
    {
        try { _queue = Svc.Queue(); } catch (Exception e) { _qInfo.Text = e.Message; return; }
        static string T(DateTime? d) => d is { } x && x != default ? x.ToString("dd/MM HH:mm", Ui.Vi) : "";
        _qm.Load(new[]
        {
            new GridColumn("st", "Trạng thái", 110), new GridColumn("sev", "Mức", 70), new GridColumn("mod", "Nơi", 170), new GridColumn("title", "Cảnh báo", 360),
            new GridColumn("first", "Lần đầu", 90), new GridColumn("last", "Lần cuối thấy", 100), new GridColumn("recur", "Tái phát", 70, "number"), new GridColumn("note", "Ghi chú", 300),
        }, _queue.Select(x => new[]
        {
            TroLyQueue.Label.GetValueOrDefault(x.Item.St, x.Item.St), SevStyle.GetValueOrDefault(x.Item.Sev, SevStyle["xam"]).Text, x.Item.Mod, x.Item.Title,
            T(x.Item.FirstAt), T(x.Item.LastSeenAt), x.Item.Recur.ToString(), x.Item.Note,
        }));
        _qg.Refresh();
        var by = _queue.GroupBy(x => x.Item.St).ToDictionary(g => g.Key, g => g.Count());
        _qInfo.Text = _queue.Count == 0 ? "Hàng đợi trống." : string.Join(" · ", TroLyQueue.States.Where(by.ContainsKey).Select(s => $"{TroLyQueue.Label[s]}: {by[s]}")) +
            ". Mục đã đóng giữ 60 ngày rồi tự xóa.";
    }

    private async Task PurgeDone()
    {
        var ids = _queue.Where(x => x.Item.St is "dong" or "boQua").Select(x => x.Id).ToList();
        if (ids.Count == 0) { await Ui.Message(this, "Không có mục Đã đóng / Bỏ qua nào."); return; }
        if (!await Ui.Confirm(this, $"Xóa {ids.Count} mục đã xử lý khỏi lịch sử?\n\nMục đã đóng vẫn giữ dấu vết ẩn để nhận ra nếu lỗi quay lại (Tái phát).", "Xóa lịch sử", "Xóa")) return;
        if (await Ui.Run(this, () => Svc.Purge(ids))) LoadQueue();
    }
}

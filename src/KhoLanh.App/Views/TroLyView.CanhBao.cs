using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.Core.TroLy;

namespace KhoLanh.App.Views;

/// <summary>
/// Tab «Cảnh báo» của Trợ lý hệ thống (Giai đoạn 3): thẻ số Nặng / Cần xem / Ổn, lọc theo mức + theo module, danh sách gọn bên trái
/// (ô tích, nhãn mức, nơi, tiêu đề) và khung chi tiết bên phải (mô tả, bằng chứng, file nghi ngờ, nút «Mở trang sửa» / thao tác cho đúng cảnh báo đó).
/// Các nút thao tác hàng loạt (theo ô tích) giữ nguyên như trước.
/// </summary>
public sealed partial class TroLyView
{
    private const string AllMods = "Mọi module";
    private readonly ComboBox _modFilter = new() { MinWidth = 190, MaxWidth = 300 };
    private readonly StackPanel _detail = new() { Spacing = 6 };
    private readonly ScrollViewer _detailScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private string _cur = "";
    private bool _modSync;

    private Control BuildFindPanel()
    {
        _list.Spacing = 2;
        _modFilter.ItemsSource = new[] { AllMods }; _modFilter.SelectedIndex = 0;
        ToolTip.SetTip(_modFilter, "Chỉ hiện cảnh báo của một nơi (module / màn hình)");
        _modFilter.SelectionChanged += (_, e) => { e.Handled = true; if (!_modSync) Render(); };
        _filter.SelectionChanged += (_, e) => e.Handled = true;   // không nổi lên TabControl cha

        var actions = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        actions.Children.Add(Btn("📋 Sao chép báo cáo cho Claude", () => _ = CopyReport(), "Các cảnh báo đã tick → báo cáo dán thẳng vào Claude; cảnh báo chuyển sang «Đã gửi Claude»"));
        actions.Children.Add(Btn("✔ Đã sửa (chờ kiểm tra)", () => _ = Mark("daSua"), "Lần quét sau còn thấy → Tái phát; hết → Đã đóng"));
        actions.Children.Add(Btn("Bỏ qua", () => _ = Mark("boQua")));
        actions.Children.Add(Btn("Lỗi từ nguồn", () => _ = Mark("loiNguon"), "Do file Excel / phần mềm kho gốc — chỉ nhắc 1 lần, không đếm tái phát"));
        actions.Children.Add(Btn("Mở lại (Mới)", () => _ = Mark("moi")));
        actions.Children.Add(Btn("Chọn hết", () => { foreach (var f in Visible()) if (f.Sev != "xanh") _sel.Add(f.Id); Render(); }));
        actions.Children.Add(Btn("Bỏ chọn", () => { _sel.Clear(); Render(); }));

        var filters = new WrapPanel { ItemSpacing = 10, LineSpacing = 6 };
        filters.Children.Add(_filter); filters.Children.Add(_modFilter); filters.Children.Add(_showSkipped); filters.Children.Add(_anDanh);
        foreach (var c in filters.Children) c.VerticalAlignment = VerticalAlignment.Center;

        var findTop = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        var kpiRow = new DockPanel();   // 1.101: thẻ số + dòng trạng thái quét
        var scan = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        scan.Children.Add(_quick); scan.Children.Add(_deep);
        DockPanel.SetDock(scan, Dock.Right); kpiRow.Children.Add(scan);
        DockPanel.SetDock(_kpi, Dock.Left); kpiRow.Children.Add(_kpi);
        _status.Margin = new Thickness(14, 0, 0, 0); _status.Tag = "keep"; kpiRow.Children.Add(_status);
        findTop.Children.Add(kpiRow); findTop.Children.Add(filters); findTop.Children.Add(actions);

        // danh sách | chi tiết — danh sách hẹp hơn, chi tiết đủ chỗ đọc bằng chứng (màn hình 1366 px: ~520 | ~780)
        var left = new Border
        {
            BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = AppTheme.Card,
            Child = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
        _list.Margin = new Thickness(4);
        _detail.Margin = new Thickness(14, 10, 16, 12);   // lề trên nội dung (Padding của ScrollViewer không tính vào bề rộng xuống dòng)
        _detailScroll.Content = _detail;
        var right = new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = AppTheme.Card, Child = _detailScroll };
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,10,3*"), MinHeight = 360 };
        left.MinWidth = 300; right.MinWidth = 340;
        Grid.SetColumn(left, 0); Grid.SetColumn(right, 2);
        split.Children.Add(left); split.Children.Add(right);

        var panel = new DockPanel();
        DockPanel.SetDock(findTop, Dock.Top); panel.Children.Add(findTop);
        panel.Children.Add(split);
        return panel;
    }

    /// <summary>Cảnh báo đang hiện theo mức + bỏ qua (chưa xét module) — để dựng danh sách module.</summary>
    private IEnumerable<Finding> VisibleByLevel()
    {
        var all = Svc.Last?.Findings ?? new();
        var lvl = _filter.SelectedIndex;
        return all.Where(f => (_showSkipped.IsChecked == true || f.Q is not ("boQua" or "loiNguon"))
            && lvl switch { 1 => f.Sev == "do", 2 => f.Sev != "xanh", 3 => f.Sev == "xanh", _ => true });
    }

    private IEnumerable<Finding> Visible()
    {
        var mod = _modFilter.SelectedItem as string ?? AllMods;
        return VisibleByLevel().Where(f => mod == AllMods || f.Mod == mod);
    }

    /// <summary>Danh sách module trong ô lọc = các nơi đang có cảnh báo ở mức đang lọc (giữ lựa chọn cũ nếu còn).</summary>
    private void SyncModFilter()
    {
        var cur = _modFilter.SelectedItem as string ?? AllMods;
        var mods = VisibleByLevel().GroupBy(f => f.Mod).OrderBy(g => g.Key, StringComparer.CurrentCulture)
            .Select(g => $"{g.Key}").ToList();
        var items = new List<string> { AllMods };
        items.AddRange(mods);
        if (cur != AllMods && !items.Contains(cur)) items.Add(cur);   // module đang chọn hết cảnh báo: giữ để người dùng thấy "không có"
        var old = _modFilter.ItemsSource as IEnumerable<string>;
        if (old is not null && old.SequenceEqual(items)) return;
        _modSync = true;
        try { _modFilter.ItemsSource = items; _modFilter.SelectedItem = cur; }
        finally { _modSync = false; }
    }

    private void RenderList()
    {
        _list.Children.Clear();
        SyncModFilter();
        if (Svc.Last is null) { ShowDetail(null); return; }
        var vis = Visible().ToList();
        _sel.IntersectWith(vis.Select(f => f.Id));
        if (vis.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "Không có cảnh báo nào ở mức / module đang lọc.", Classes = { "muted" }, FontSize = 12.5, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap, Tag = "keep" });
            ShowDetail(null); return;
        }
        if (vis.All(f => f.Id != _cur)) _cur = vis[0].Id;
        foreach (var f in vis) _list.Children.Add(ListRow(f));
        ShowDetail(vis.First(f => f.Id == _cur));
    }

    private Control ListRow(Finding f)
    {
        var cb = new CheckBox { IsChecked = _sel.Contains(f.Id), VerticalAlignment = VerticalAlignment.Center, IsVisible = f.Sev != "xanh", MinWidth = 0, Margin = new Thickness(0, 0, 4, 0) };
        cb.IsCheckedChanged += (_, _) => { if (cb.IsChecked == true) _sel.Add(f.Id); else _sel.Remove(f.Id); };
        var badge = Badge(f.Sev); badge.VerticalAlignment = VerticalAlignment.Center; badge.Margin = new Thickness(0, 0, 8, 0);
        var text = new StackPanel { Spacing = 0 };
        var sub = f.Mod + (f.Q.Length > 0 && f.Sev != "xanh" ? " · " + TroLyQueue.Label.GetValueOrDefault(f.Q, f.Q) + (f.Recur > 0 ? $" ×{f.Recur}" : "") : "");
        text.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = f.Title, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Dark, TextTrimming = TextTrimming.CharacterEllipsis });
        var row = new DockPanel();
        DockPanel.SetDock(cb, Dock.Left); row.Children.Add(cb);
        DockPanel.SetDock(badge, Dock.Left); row.Children.Add(badge);
        if (f.Go.Length > 0)
        {
            var go = new TextBlock { Text = "↗", FontSize = 12, Foreground = AppTheme.Brush("#1F6FD6"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) };
            ToolTip.SetTip(go, "Có trang sửa: " + f.GoLabel);
            DockPanel.SetDock(go, Dock.Right); row.Children.Add(go);
        }
        row.Children.Add(text);
        var sel = f.Id == _cur;
        var b = new Border
        {
            Child = row, Padding = new Thickness(6, 4), CornerRadius = new CornerRadius(5), Cursor = new Cursor(StandardCursorType.Hand),
            Background = sel ? AppTheme.Brush("#E6F0FC") : Brushes.Transparent,
            BorderBrush = sel ? AppTheme.Brush("#1F6FD6") : Brushes.Transparent, BorderThickness = new Thickness(1),
        };
        ToolTip.SetTip(b, f.Title);
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            if (_cur == f.Id) return;
            _cur = f.Id; RenderList();
        };
        return b;
    }

    private void ShowDetail(Finding? f)
    {
        _detail.Children.Clear();
        if (f is null)
        {
            _detail.Children.Add(new TextBlock { Text = Svc.Last is null ? "Chưa quét." : "Chọn một cảnh báo bên trái để xem chi tiết.", Classes = { "muted" }, FontSize = 12.5 });
            return;
        }
        var head = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        head.Children.Add(Badge(f.Sev));
        if (f.Q.Length > 0 && f.Sev != "xanh")
            head.Children.Add(new Border
            {
                Background = AppTheme.Brush(f.Q == "taiPhat" ? "#FDE7E7" : "#EEF1F5"), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1),
                Child = new TextBlock { Text = TroLyQueue.Label.GetValueOrDefault(f.Q, f.Q) + (f.Recur > 0 ? $" ×{f.Recur}" : ""), FontSize = 11 },
            });
        head.Children.Add(new TextBlock { Text = f.Mod, FontSize = 12, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        _detail.Children.Add(head);
        _detail.Children.Add(new SelectableTextBlock { Text = f.Title, FontWeight = FontWeight.Bold, FontSize = 15, Foreground = Dark, TextWrapping = TextWrapping.Wrap });
        if (f.Detail.Length > 0) _detail.Children.Add(new SelectableTextBlock { Text = f.Detail, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        if (f.QNote.Length > 0) _detail.Children.Add(new TextBlock { Text = "↳ " + f.QNote, FontSize = 12, Foreground = AppTheme.Brush("#B42318"), TextWrapping = TextWrapping.Wrap });

        // nút hành động cho đúng cảnh báo này
        var acts = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 6, 0, 2) };
        if (f.Go.Length > 0)
        {
            var go = Btn("Mở trang sửa → " + f.GoLabel, () => GoFix(f.Go), "Mở trang cài đặt liên quan để sửa cấu hình");
            go.Classes.Add("primary");
            acts.Children.Add(go);
        }
        if (f.Fixes is { Count: > 0 } fx)
        {
            var ok = fx.Count(x => x.Near.Length == 0);
            var add = Btn($"＋ Thêm {ok} mã vào Danh sách mã hàng", () => _ = AddCodes(f), "Chỉ thêm mã không có mã gần giống (mã gần giống = nghi gõ sai, để bạn tự xem). Không sửa / xóa dòng đang có.");
            add.IsEnabled = ok > 0;
            acts.Children.Add(add);
        }
        if (f.Sev != "xanh")
        {
            var one = new List<string> { f.Id };
            acts.Children.Add(Btn("📋 Sao chép báo cáo", () => _ = CopyReport(one), "Chỉ cảnh báo này → báo cáo dán vào Claude"));
            acts.Children.Add(Btn("✔ Đã sửa", () => _ = Mark("daSua", one), "Lần quét sau còn thấy → Tái phát; hết → Đã đóng"));
            acts.Children.Add(Btn("Bỏ qua", () => _ = Mark("boQua", one)));
            acts.Children.Add(Btn("Lỗi từ nguồn", () => _ = Mark("loiNguon", one), "Do file Excel / phần mềm kho gốc — chỉ nhắc 1 lần, không đếm tái phát"));
            if (f.Q is "boQua" or "loiNguon" or "daSua" or "dong") acts.Children.Add(Btn("Mở lại (Mới)", () => _ = Mark("moi", one)));
        }
        if (acts.Children.Count > 0) _detail.Children.Add(acts);

        if (f.Evidence.Count > 0)
        {
            _detail.Children.Add(Section("Bằng chứng"));
            foreach (var e in f.Evidence) _detail.Children.Add(new SelectableTextBlock { Text = "• " + e, FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Tag = "keep" });
        }
        if (f.Fixes is { Count: > 0 } fixes)
        {
            _detail.Children.Add(Section($"Mã chưa có trong Danh sách mã hàng ({fixes.Count})"));
            foreach (var x in fixes.Take(30))
                _detail.Children.Add(new SelectableTextBlock { Text = "• " + x.Ma + (x.Ten.Length > 0 ? " — " + x.Ten : "") + (x.Near.Length > 0 ? " (gần giống " + x.Near + ")" : ""), FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Tag = "keep" });
            if (fixes.Count > 30) _detail.Children.Add(new TextBlock { Text = $"… và {fixes.Count - 30} mã nữa", FontSize = 12, Classes = { "muted" } });
        }
        if (f.Files.Count > 0 && f.Sev != "xanh")
        {
            _detail.Children.Add(Section("File nghi ngờ (gợi ý cho Claude)"));
            foreach (var x in f.Files) _detail.Children.Add(new SelectableTextBlock { Text = "• " + x, FontSize = 11.5, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Tag = "keep" });
        }
        _detailScroll.Offset = default;
    }

    private static TextBlock Section(string t) => new() { Text = t, FontWeight = FontWeight.SemiBold, FontSize = 12.5, Foreground = Dark, Margin = new Thickness(0, 8, 0, 0) };

    /// <summary>1.101: "Mở trang sửa" — khóa menu thì chuyển trang; "#hieunang" thì sang tab Hiệu năng của Trợ lý.</summary>
    private void GoFix(string go)
    {
        if (go == KhoLanh.Core.TroLy.TroLyService.GoHieuNang)
        {
            var tc = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<TabControl>()
                .FirstOrDefault(t => t.Items.OfType<TabItem>().Any(i => KhoLanh.App.Controls.Icon.TextOf(i.Header) == "Hiệu năng"));
            if (tc is not null) tc.SelectedItem = tc.Items.OfType<TabItem>().First(i => KhoLanh.App.Controls.Icon.TextOf(i.Header) == "Hiệu năng");
            return;
        }
        MainWindow.Of(this)?.Navigate(go);
    }
}

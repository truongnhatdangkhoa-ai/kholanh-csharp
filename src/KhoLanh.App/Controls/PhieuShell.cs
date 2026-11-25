using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.Core.Grid;

namespace KhoLanh.App.Controls;

/// <summary>
/// 1.82: danh sách phiếu xuất nhóm theo THÁNG (dòng nhóm "▾ Tháng 11/2026 · n phiếu · tổng kiện / kg", bấm để mở / thu, tháng mới nhất trên cùng).
/// Mặc định mở 2 tháng mới nhất; tháng người dùng tự mở / thu được nhớ theo tài khoản (settings <c>ui_thang:&lt;phạm vi&gt;:&lt;tk&gt;</c>).
/// Đang lọc / tìm theo cột → mở mọi tháng (dòng thu gọn không có trong bảng nên không tìm được).
/// </summary>
public sealed class MonthGroups
{
    private readonly string _scope;
    private readonly Dictionary<string, bool> _over = new();
    private List<string> _keys = new();
    /// <summary>Mỗi dòng bảng: chỉ số phần tử trong danh sách gốc, hoặc -1 = dòng nhóm tháng.</summary>
    public List<int> Map { get; } = new();
    /// <summary>Khóa tháng (yyyy-MM, "" = chưa có ngày) của từng dòng bảng.</summary>
    public List<string> RowKey { get; } = new();
    public bool ExpandAll { get; set; }
    public const int DefaultOpen = 2;

    private static readonly IBrush HeadBg = AppTheme.Brush("#EAF1FB"), HeadBgClosed = AppTheme.Brush("#F3F6FA"), HeadFg = AppTheme.Brush("#1A2433");

    public MonthGroups(string scope) { _scope = scope; Load(); }

    private string SettingKey => "ui_thang:" + _scope + ":" + (App.Services.Session.Username ?? "").ToLowerInvariant();
    private void Load()
    {
        try
        {
            foreach (var part in App.Services.Settings.Get(SettingKey, "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (part.Length > 2 && part[^2] == '=') _over[part[..^2]] = part[^1] == '1';
        }
        catch { }
    }
    private void Save() { try { App.Services.Settings.Set(SettingKey, string.Join(";", _over.Select(kv => kv.Key + "=" + (kv.Value ? "1" : "0")))); } catch { } }

    public static string KeyOf(string ngay) => ngay.Length >= 7 && ngay[4] == '-' && char.IsDigit(ngay[0]) && char.IsDigit(ngay[5]) && char.IsDigit(ngay[6]) ? ngay[..7] : "";
    public static string Label(string key) => key.Length == 0 ? "Chưa có ngày" : $"Tháng {key[5..7]}/{key[..4]}";

    private bool Default(string key) => _keys.Where(k => k.Length > 0).Take(DefaultOpen).Contains(key);
    public bool IsOpen(string key) => ExpandAll || (_over.TryGetValue(key, out var o) ? o : Default(key));
    public bool IsHeader(int row) => row >= 0 && row < Map.Count && Map[row] < 0;
    public int ItemAt(int row) => row >= 0 && row < Map.Count ? Map[row] : -1;
    public int RowOf(int item) => Map.IndexOf(item);

    public void Toggle(string key) => Set(key, !IsOpen(key));
    public void Set(string key, bool open)
    {
        if (open == Default(key)) _over.Remove(key); else _over[key] = open;
        Save();
    }
    public void SetAll(bool open) { foreach (var k in _keys) Set(k, open); }
    /// <summary>Chỉ mở tháng mới nhất.</summary>
    public void OnlyNewest() { for (var i = 0; i < _keys.Count; i++) Set(_keys[i], i == 0); }
    public void Open(string key) { if (!IsOpen(key)) Set(key, true); }

    /// <summary>Dựng các dòng bảng: nhóm theo tháng (mới → cũ, "chưa có ngày" cuối), trong tháng ngày mới trước.</summary>
    public List<string[]> Build<T>(IReadOnlyList<T> items, Func<T, string> ngay, Func<T, string[]> row, Func<string, List<T>, bool, string[]> header)
    {
        Map.Clear(); RowKey.Clear();
        var groups = items.Select((x, i) => (x, i)).GroupBy(t => KeyOf(ngay(t.x)))
            .OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenByDescending(g => g.Key, StringComparer.Ordinal).ToList();
        _keys = groups.Select(g => g.Key).ToList();
        var rows = new List<string[]>();
        foreach (var g in groups)
        {
            var open = IsOpen(g.Key);
            var list = g.OrderByDescending(t => ngay(t.x), StringComparer.Ordinal).ToList();
            rows.Add(header(g.Key, list.Select(t => t.x).ToList(), open)); Map.Add(-1); RowKey.Add(g.Key);
            if (!open) continue;
            foreach (var (x, i) in list) { rows.Add(row(x)); Map.Add(i); RowKey.Add(g.Key); }
        }
        return rows;
    }

    /// <summary>Gắn vào lưới danh sách: tô dòng nhóm, dòng Tổng không cộng dòng nhóm, tắt sắp xếp (giữ thứ tự tháng), bấm dòng nhóm = mở / thu.</summary>
    public void Attach(SheetGrid g, Action rerender)
    {
        g.Sortable = false;
        g.CellBackground = (r, _) => IsHeader(r) ? (IsOpen(RowKey[r]) ? HeadBg : HeadBgClosed) : null;
        g.CellForeground = (r, _) => IsHeader(r) ? HeadFg : null;
        g.TotalsRowFilter = r => !IsHeader(r) || !IsOpen(RowKey[r]);   // tháng đang thu: cộng số của dòng tháng (tổng = mọi phiếu)
        g.RowClicked = r => { if (IsHeader(r)) { Toggle(RowKey[r]); rerender(); } };
        g.FilterChanged += () => { if (ExpandAll == g.IsFiltered) return; ExpandAll = g.IsFiltered; rerender(); };
    }

    /// <summary>Các nút Mở hết / Thu hết / Chỉ tháng mới nhất (thanh công cụ danh sách).</summary>
    public IEnumerable<Button> ToolButtons(Action rerender)
    {
        Button B(string t, string tip, Action a)
        {
            var b = new Button { Content = t, Padding = new Thickness(8, 3), FontSize = 12, Classes = { "ghost" } };
            ToolTip.SetTip(b, tip); b.Click += (_, _) => { a(); rerender(); }; return b;
        }
        yield return B("▾ Mở hết", "Mở mọi tháng", () => SetAll(true));
        yield return B("▸ Thu hết", "Thu mọi tháng (chỉ còn dòng tháng)", () => SetAll(false));
        yield return B("Chỉ tháng mới nhất", "Mở tháng mới nhất, thu các tháng khác", OnlyNewest);
    }
}

/// <summary>
/// 1.82: khung soạn phiếu xuất (phương án B đã duyệt) — thanh trên 1 dòng (← Danh sách · tên phiếu · trạng thái · nút · ⋯ Thêm · ✕ Đóng phiếu),
/// bên trái nội dung chính (mặt hàng xếp dọc, mặt hàng đang sửa mở to), bên phải khung thông tin thu / mở được (nhớ theo tài khoản).
/// </summary>
public static class PhieuShell
{
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush CardBg = AppTheme.Brush("#FFFFFF");
    private static readonly IBrush Ink = AppTheme.Brush("#1A2433");

    public const double SideW = 310;

    public static Button Btn(string text, Func<Task> click, bool primary = false, bool danger = false, bool ghost = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        if (primary) b.Classes.Add("primary"); if (danger) b.Classes.Add("danger"); if (ghost) b.Classes.Add("ghost");
        b.Click += (_, _) => { _ = click(); };
        return b;
    }

    /// <summary>Nút "⋯ Thêm" mở menu các thao tác ít dùng.</summary>
    public static Button More(params (string Text, Func<Task> Click, bool Danger)[] items)
    {
        var b = new Button { Content = "⋯ Thêm", Padding = new Thickness(10, 4), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var it in items)
        {
            if (it.Text == "-") { menu.Items.Add(new Separator()); continue; }
            var mi = new MenuItem { Header = it.Text };
            if (it.Danger) mi.Foreground = AppTheme.Brush("#B42318");
            var f = it.Click; mi.Click += (_, _) => { _ = f(); };
            menu.Items.Add(mi);
        }
        b.Flyout = menu;
        return b;
    }

    /// <summary>Thanh trên cùng khi soạn phiếu.</summary>
    public static Control TopBar(Func<Task> back, TextBlock title, Control status, IEnumerable<Control> right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        var bk = Btn("← Danh sách", back); bk.FontWeight = FontWeight.Bold;
        ToolTip.SetTip(bk, "Đóng phiếu, về danh sách (ESC). Chưa lưu thì hỏi trước.");
        g.Children.Add(bk);
        title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(10, 0, 8, 0); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; title.MaxWidth = 420;
        Grid.SetColumn(title, 1); g.Children.Add(title);
        status.VerticalAlignment = VerticalAlignment.Center; status.ClipToBounds = true; status.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(status, 2); g.Children.Add(status);
        var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var c in right) r.Children.Add(c);
        Grid.SetColumn(r, 3); g.Children.Add(r);
        return g;
    }

    public static Button CloseButton(Func<Task> close)
    {
        var b = Btn("✕ Đóng phiếu", close, ghost: true);
        ToolTip.SetTip(b, "Đóng phiếu, về danh sách (ESC)");
        return b;
    }

    /// <summary>Thẻ trắng viền mảnh.</summary>
    public static Border Card(Control child, Thickness? pad = null) => new()
    {
        Background = CardBg, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
        Padding = pad ?? new Thickness(10, 7), Child = child,
    };

    public static TextBlock H(string text, double size = 13) => new() { Text = text, FontWeight = FontWeight.Bold, FontSize = size, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Ô nhập có nhãn nhỏ phía trên, giãn hết bề ngang ô lưới.</summary>
    public static StackPanel Field(string label, Control c)
    {
        var s = new StackPanel { Spacing = 1, Margin = new Thickness(0, 0, 0, 5) };
        s.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
        if (c is Layoutable l) { l.HorizontalAlignment = HorizontalAlignment.Stretch; if (c is TextBox or ComboBox or CalendarDatePicker) l.Width = double.NaN; }
        s.Children.Add(c);
        return s;
    }

    /// <summary>2 ô cạnh nhau trong khung phải.</summary>
    public static Grid Pair(Control a, Control? b)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,*") };
        g.Children.Add(a);
        if (b is not null) { Grid.SetColumn(b, 2); g.Children.Add(b); }
        return g;
    }

    private static string SideKey(string scope) => "ui_px_side:" + scope + ":" + (App.Services.Session.Username ?? "").ToLowerInvariant();

    /// <summary>
    /// Bố cục chính: <paramref name="main"/> bên trái (giãn), khung <paramref name="side"/> bên phải rộng <see cref="SideW"/> (cuộn dọc),
    /// thu lại thành 1 nút dọc hẹp; nhớ trạng thái theo tài khoản.
    /// </summary>
    public static Control Body(Control main, string sideTitle, Control side, string scope)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,Auto") };
        g.Children.Add(main);
        bool open; try { open = App.Services.Settings.Get(SideKey(scope), "1") != "0"; } catch { open = true; }
        var panel = new DockPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var collapse = new Button { Content = "⟩ Thu", Padding = new Thickness(6, 1), FontSize = 12, Classes = { "ghost" } };
        ToolTip.SetTip(collapse, "Thu khung thông tin để bảng rộng hơn");
        DockPanel.SetDock(collapse, Dock.Right); head.Children.Add(collapse); head.Children.Add(H(sideTitle));
        DockPanel.SetDock(head, Dock.Top); panel.Children.Add(head);
        panel.Children.Add(new ScrollViewer { Content = new Border { Padding = new Thickness(0, 0, 8, 4), Child = side }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var full = Card(panel, new Thickness(10, 8, 2, 8)); full.Width = SideW;
        var expand = new Button
        {
            Padding = new Thickness(4, 8), VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Width = 30,
            Content = new LayoutTransformControl { LayoutTransform = new RotateTransform(90), Child = new TextBlock { Text = "⟨  " + sideTitle, FontWeight = FontWeight.SemiBold, FontSize = 12.5 } },
        };
        ToolTip.SetTip(expand, "Mở khung " + sideTitle.ToLowerInvariant());
        var host = new Panel(); host.Children.Add(full); host.Children.Add(expand);
        void Apply() { full.IsVisible = open; expand.IsVisible = !open; }
        void SetOpen(bool v) { open = v; Apply(); try { App.Services.Settings.Set(SideKey(scope), v ? "1" : "0"); } catch { } }
        collapse.Click += (_, _) => SetOpen(false); expand.Click += (_, _) => SetOpen(true);
        Apply();
        Grid.SetColumn(host, 2); g.Children.Add(host);
        return g;
    }

    /// <summary>
    /// Danh sách mục xếp dọc: mục đang mở giãn hết chiều cao còn lại, các mục khác thu thành 1 dòng (bấm để mở).
    /// <paramref name="build"/>(i, open) dựng 1 mục; <paramref name="footer"/> = nút thêm mục…
    /// </summary>
    public static Control Stack(Control? top, int count, int open, Func<int, bool, Control> build, Control? footer)
    {
        var g = new Grid();
        var row = 0;
        void Add(Control c, GridLength h, double gap = 8)
        {
            g.RowDefinitions.Add(new RowDefinition(h));
            c.Margin = new Thickness(0, 0, 0, gap); Grid.SetRow(c, row++); g.Children.Add(c);
        }
        if (top is not null) Add(top, GridLength.Auto);
        for (var i = 0; i < count; i++)
        {
            var o = i == open;
            Add(build(i, o), o ? new GridLength(1, GridUnitType.Star) : GridLength.Auto, o ? 8 : 6);
            if (o) g.RowDefinitions[^1].MinHeight = 230;
        }
        if (footer is not null) Add(footer, GridLength.Auto, 0);
        return g;
    }

    /// <summary>Đầu một mục thu gọn / mở: "▾ Mặt hàng 1" bấm được + các ô.</summary>
    public static Button Toggle(string text, bool open, Action click)
    {
        var b = new Button
        {
            Content = (open ? "▾ " : "▸ ") + text, FontWeight = FontWeight.Bold, FontSize = 13, Padding = new Thickness(4, 3), Classes = { "ghost" },
            VerticalAlignment = VerticalAlignment.Center, Foreground = Ink,
        };
        ToolTip.SetTip(b, open ? "Đang mở" : "Bấm để mở mục này (mục đang mở sẽ thu lại)");
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Hỏi khi đóng phiếu còn thay đổi chưa lưu: 0 = Lưu rồi đóng, 1 = Bỏ thay đổi, 2 = Ở lại.</summary>
    public static async Task<int> AskClose(Visual v, string what)
    {
        var res = 2;
        var save = new Button { Content = "Lưu rồi đóng", IsDefault = true, Classes = { "primary" } };
        var drop = new Button { Content = "Bỏ thay đổi" }; drop.Classes.Add("danger");
        var stay = new Button { Content = "Ở lại", IsCancel = true };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = what + " có thay đổi chưa lưu.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Ui.Buttons(stay, drop, save));
        var w = Ui.Dialog("Chưa lưu", body);
        save.Click += (_, _) => { res = 0; w.Close(); };
        drop.Click += (_, _) => { res = 1; w.Close(); };
        stay.Click += (_, _) => { res = 2; w.Close(); };
        await Ui.Show(v, w);
        return res;
    }

    /// <summary>
    /// ESC trong khung soạn phiếu: đang gõ ô chữ → chỉ rời ô (khung nhận tiêu điểm); không thì đóng phiếu.
    /// Ô bảng / danh sách lọc / menu đã xử lý ESC (e.Handled) thì bỏ qua.
    /// </summary>
    public static void HandleEsc(Control root, Func<Task> close)
    {
        root.Focusable = true;
        root.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            if (TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() is TextBox) { root.Focus(); return; }
            _ = close();
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    /// <summary>Nhãn trạng thái nhỏ (pill) trên thanh soạn phiếu.</summary>
    public static Border Pill(TextBlock text, string kind)
    {
        var b = new Border { Padding = new Thickness(8, 1), CornerRadius = new CornerRadius(10), Child = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        b.Classes.Add("pill"); b.Classes.Add(kind);
        text.FontSize = 11.5; text.FontWeight = FontWeight.SemiBold;
        return b;
    }
    public static void SetPill(Border pill, string kind)
    {
        foreach (var k in new[] { "ok", "warn", "bad", "info", "accent" }) pill.Classes.Remove(k);
        pill.Classes.Add(kind);
    }

    /// <summary>Chiều rộng cột Grid "*,…" dùng cho các ô đầu mục.</summary>
    public static Grid Row(params (Control C, string W)[] cells)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", cells.Select(c => c.W))) };
        for (var i = 0; i < cells.Length; i++)
        {
            var c = cells[i].C; Grid.SetColumn(c, i);
            if (c is Layoutable l) { l.Margin = new Thickness(0, 0, i < cells.Length - 1 ? 6 : 0, 0); l.VerticalAlignment = VerticalAlignment.Center; if (cells[i].W != "Auto" && c is TextBox or ComboBox) l.Width = double.NaN; }
            g.Children.Add(c);
        }
        return g;
    }
}

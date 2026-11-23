using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.PhieuXuat;
using static KhoLanh.Core.PhieuXuat.PxhtService;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Phiếu xuất hệ thống (C#, theo M09 "Phiếu xuất toàn hệ thống" của app cũ — chỉ đọc).
/// Gom phiếu xuất của Tồn kho An An, Tồn theo vị trí, Tồn kho gửi, NXT Bột/Sốt: lọc theo ngày / module / nguồn / người tạo,
/// tìm cả mã hàng bên trong phiếu, sắp xếp, xem chi tiết, mở module gốc, xuất Excel.
/// </summary>
public sealed class PxhtView : UserControl, IPage
{
    public string Title => "Phiếu xuất hệ thống";
    public string Subtitle => "Tất cả phiếu xuất của các module (Tồn kho An An, Tồn theo vị trí, Tồn kho gửi, NXT Bột/Sốt) — chỉ xem, không sửa dữ liệu.";

    private static PxhtService Svc => App.Services.Pxht;
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45");
    private static readonly IBrush Warn = AppTheme.Brush("#B23B00");
    private static readonly IBrush PhoneBg = AppTheme.Brush("#E8F1FB");

    private readonly CalendarDatePicker _from = DatePick(), _to = DatePick();
    private readonly ComboBox _src = new() { MinWidth = 200 };
    private readonly ComboBox _nguon = new() { MinWidth = 120 };
    private readonly ComboBox _nguoi = new() { MinWidth = 140 };
    private readonly ComboBox _sort = new() { MinWidth = 170 };
    private readonly TextBox _q = new() { Watermark = "Tìm mã phiếu, ghi chú, mã / tên hàng trong phiếu…", Width = 300 };
    private readonly WrapPanel _kpi = new() { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Border _warn = new() { Background = AppTheme.Brush("#FFF4CC"), BorderBrush = AppTheme.Brush("#E8C766"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), IsVisible = false };
    private readonly GridModel _lm = new() { ReadOnly = true };
    private readonly SheetGrid _lg;
    private readonly GridModel _dm = new() { ReadOnly = true };
    private readonly SheetGrid _dg;
    private readonly StackPanel _meta = new() { Spacing = 2 };
    private readonly TextBlock _dTitle = new() { FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Dark, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _dKicker = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button _open, _copy;
    private List<Slip> _view = new();
    private Slip? _cur;
    private bool _loading;

    private static readonly (string Key, int Dir, string Text)[] Sorts =
    {
        ("ts", -1, "Ngày tạo (mới nhất)"), ("ts", 1, "Ngày tạo (cũ nhất)"), ("maPhieu", 1, "Mã phiếu"), ("module", 1, "Module"),
        ("nguon", 1, "Nguồn (PC trước)"), ("nguoiTao", 1, "Người tạo"), ("soMH", -1, "Số mặt hàng (nhiều nhất)"),
    };

    public PxhtView()
    {
        _lg = new SheetGrid(_lm) { SpareRows = 0, CellBackground = (r, _) => r < _view.Count && _view[r].Phone ? PhoneBg : null };
        _lg.SelectionChanged += () => ShowDetail(_lg.Row >= 0 && _lg.Row < _view.Count ? _view[_lg.Row] : null);
        _dg = new SheetGrid(_dm) { SpareRows = 0 };

        _src.ItemsSource = new[] { "Tất cả module" }.Concat(Sources.Select(s => SrcLabel(s.Key) + " · " + s.Sub)).ToList();
        _src.SelectedIndex = 0;
        _nguon.ItemsSource = new[] { "Mọi nguồn", "PC", "Điện thoại" };
        _nguon.SelectedIndex = 0;
        _nguoi.ItemsSource = new[] { "Mọi người tạo" };
        _nguoi.SelectedIndex = 0;
        _sort.ItemsSource = Sorts.Select(s => "Sắp: " + s.Text).ToList();
        _sort.SelectedIndex = 0;
        foreach (var cb in new[] { _src, _nguon, _nguoi, _sort }) cb.SelectionChanged += (_, _) => { if (!_loading) Apply(); };
        _from.SelectedDateChanged += (_, _) => { if (!_loading) Apply(); };
        _to.SelectedDateChanged += (_, _) => { if (!_loading) Apply(); };
        var deb = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        deb.Tick += (_, _) => { deb.Stop(); Apply(); };
        _q.TextChanged += (_, _) => { deb.Stop(); deb.Start(); };

        _open = Btn("Mở module →", OpenModule);
        _copy = Btn("Sao chép mã phiếu", async () => { if (_cur is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { await cb.SetTextAsync(_cur.MaPhieu); _status.Text = "Đã sao chép mã phiếu " + _cur.MaPhieu; } });

        var filters = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        filters.Children.Add(Label("Từ ngày")); filters.Children.Add(_from);
        filters.Children.Add(Label("Đến")); filters.Children.Add(_to);
        filters.Children.Add(_src); filters.Children.Add(_nguon); filters.Children.Add(_nguoi);
        filters.Children.Add(_q); filters.Children.Add(_sort);
        filters.Children.Add(Btn("Xóa lọc", ClearFilters));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        tools.Children.Add(Btn("⟳ Tải lại", Load));
        if (Svc.CanExport) tools.Children.Add(Btn("Xuất Excel", async () => await Export(), tip: "Danh sách phiếu đang lọc + toàn bộ dòng hàng của các phiếu đó"));
        var row = new DockPanel(); DockPanel.SetDock(tools, Dock.Right); row.Children.Add(tools); row.Children.Add(filters);

        var top = new StackPanel { Spacing = 4 };
        top.Children.Add(row); top.Children.Add(_warn); top.Children.Add(_kpi); top.Children.Add(_status);

        var head = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(_dKicker); head.Children.Add(_dTitle);
        var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        acts.Children.Add(_open); acts.Children.Add(_copy);
        head.Children.Add(acts);
        var right = new DockPanel();
        var rtop = new StackPanel(); rtop.Children.Add(head); rtop.Children.Add(_meta);
        DockPanel.SetDock(rtop, Dock.Top); right.Children.Add(rtop);
        var dgf = Framed(_dg); dgf.Margin = new Thickness(0, 8, 0, 0); right.Children.Add(dgf);

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,6,2*") };
        split.Children.Add(Framed(_lg));
        var gs = new GridSplitter { Background = Brushes.Transparent }; Grid.SetColumn(gs, 1); split.Children.Add(gs);
        var rb = new Border { Padding = new Thickness(8, 0, 0, 0), Child = right }; Grid.SetColumn(rb, 2); split.Children.Add(rb);

        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(split);
        Content = root;
        ShowDetail(null);
        AttachedToVisualTree += (_, _) => Load();
    }

    private static CalendarDatePicker DatePick() => new() { Width = 130, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy", Watermark = "dd/mm/yyyy" };
    private static Button Btn(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
    private static Button Btn(string text, Func<Task> click, string? tip = null) => Btn(text, () => { _ = click(); }, tip);
    private static TextBlock Label(string t) => new() { Text = t, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private static Border Framed(Control c) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = c };
    private static Border Kpi(string label, string value)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = value, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Dark });
        s.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Classes = { "muted" } });
        return new Border { Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3), MinWidth = 96, Child = s };
    }

    private async void Load()
    {
        var keep = _cur?.Key;
        if (!await Ui.Run(this, Svc.Load)) return;
        _loading = true;
        var people = Svc.All.Select(s => s.NguoiTao).Where(n => n.Length > 0).Distinct().OrderBy(n => n, StringComparer.Create(Ui.Vi, true)).ToList();
        var oldPerson = _nguoi.SelectedIndex > 0 ? _nguoi.SelectedItem as string : null;
        _nguoi.ItemsSource = new[] { "Mọi người tạo" }.Concat(people).ToList();
        _nguoi.SelectedIndex = oldPerson is not null && people.IndexOf(oldPerson) is var i && i >= 0 ? i + 1 : 0;
        _loading = false;
        if (Svc.Errors.Count > 0)
        {
            _warn.Child = new TextBlock
            {
                Text = "⚠️ Không đọc được một số nguồn: " + string.Join(" · ", Svc.Errors.Select(e => SrcFull(e.Src) + ": " + e.Error)),
                Foreground = Warn, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
            };
            _warn.IsVisible = true;
        }
        else _warn.IsVisible = false;
        Apply(keep);
        _status.Text = $"Cập nhật {DateTime.Now:HH:mm:ss} · {Svc.All.Count:#,0} phiếu trong dữ liệu. Dòng nền xanh = phiếu từ điện thoại.";
    }

    private void ClearFilters()
    {
        _loading = true;
        _from.SelectedDate = null; _to.SelectedDate = null; _q.Text = "";
        _src.SelectedIndex = 0; _nguon.SelectedIndex = 0; _nguoi.SelectedIndex = 0;
        _loading = false;
        Apply();
    }

    private Filter CurrentFilter()
    {
        var s = Sorts[Math.Max(0, _sort.SelectedIndex)];
        return new Filter
        {
            From = _from.SelectedDate?.ToString("yyyy-MM-dd") ?? "",
            To = _to.SelectedDate?.ToString("yyyy-MM-dd") ?? "",
            Query = _q.Text ?? "",
            Src = _src.SelectedIndex > 0 ? Sources[_src.SelectedIndex - 1].Key : "",
            Nguon = _nguon.SelectedIndex switch { 1 => "pc", 2 => "dt", _ => "" },
            Nguoi = _nguoi.SelectedIndex > 0 ? _nguoi.SelectedItem as string ?? "" : "",
            SortKey = s.Key, SortDir = s.Dir,
        };
    }

    private void Apply(string? keepKey = null)
    {
        keepKey ??= _cur?.Key;
        _view = PxhtService.Apply(Svc.All, CurrentFilter());
        _kpi.Children.Clear();
        _kpi.Children.Add(Kpi("Phiếu xuất", _view.Count.ToString("#,0", Ui.Vi)));
        _kpi.Children.Add(Kpi("Lập trên PC", _view.Count(s => !s.Phone).ToString("#,0", Ui.Vi)));
        _kpi.Children.Add(Kpi("Từ điện thoại", _view.Count(s => s.Phone).ToString("#,0", Ui.Vi)));
        _kpi.Children.Add(Kpi("Tổng mặt hàng", _view.Sum(s => s.SoMH).ToString("#,0", Ui.Vi)));
        _lm.Load(new[]
        {
            new GridColumn("ma", "Mã phiếu", 130), new GridColumn("ngay", "Ngày tạo", 120), new GridColumn("mod", "Module", 250),
            new GridColumn("nguon", "Nguồn", 85), new GridColumn("nguoi", "Người tạo", 110), new GridColumn("somh", "Số mặt hàng", 90, "number"),
            new GridColumn("gc", "Ghi chú", 220),
        }, _view.Select(s => new[] { s.MaPhieu, s.DateText, s.ModuleText, s.NguonText, s.NguoiTao, s.SoMH.ToString(), s.GhiChu }));
        _lg.Refresh();
        if (_view.Count == 0) { ShowDetail(null); _meta.Children.Add(new TextBlock { Text = Svc.All.Count > 0 ? "Không có phiếu nào khớp bộ lọc." : "Chưa có phiếu xuất nào trong dữ liệu của các module.", Classes = { "muted" }, FontSize = 12.5 }); return; }
        var idx = keepKey is null ? 0 : Math.Max(0, _view.FindIndex(s => s.Key == keepKey));
        _lg.Select(idx, 0);
        ShowDetail(_view[idx]);
    }

    private void ShowDetail(Slip? s)
    {
        _cur = s;
        _meta.Children.Clear();
        _open.IsEnabled = s is not null && Src(s.Src).NavKey.Length > 0;
        _copy.IsEnabled = s is not null;
        if (s is null)
        {
            _dKicker.Text = ""; _dTitle.Text = "Chọn một phiếu để xem chi tiết";
            _dm.Load(Array.Empty<GridColumn>(), Array.Empty<string[]>()); _dg.Refresh();
            return;
        }
        _dKicker.Text = SrcFull(s.Src);
        _dTitle.Text = "Phiếu " + s.MaPhieu;
        ToolTip.SetTip(_open, _open.IsEnabled ? $"Mở {Src(s.Src).Sub} của {SrcLabel(s.Src)}" : "Module này chưa có trong phần mềm mới — xem phiếu ngay tại đây.");
        var meta = new List<(string, string)>
        {
            ("Ngày tạo", s.DateText), ("Nguồn", s.Phone ? "📱 Điện thoại" : "🖥 PC"), ("Người tạo", s.NguoiTao.Length > 0 ? s.NguoiTao : "—"),
            ("Số mặt hàng", s.SoMH.ToString("#,0", Ui.Vi)), ("Ghi chú", s.GhiChu.Length > 0 ? s.GhiChu : "—"),
        };
        meta.AddRange(s.Meta.Where(m => m.V.Length > 0));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        for (int i = 0; i < meta.Count; i++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var k = new TextBlock { Text = meta[i].Item1, Classes = { "muted" }, FontSize = 12, Margin = new Thickness(0, 1, 12, 1) };
            var v = new TextBlock { Text = meta[i].Item2, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1) };
            Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            g.Children.Add(k); g.Children.Add(v);
        }
        _meta.Children.Add(g);

        var cols = Cols[s.Src];
        var gcols = new List<GridColumn> { new("stt", "#", 40, "number") };
        gcols.AddRange(cols.Select(c => new GridColumn(c.Key, c.Label, c.Num ? 95 : c.Key is "tenHang" or "ten" ? 220 : 110, c.Num ? "number" : "text")));
        var rows = s.Items.Select((it, i) => new[] { (i + 1).ToString() }.Concat(cols.Select(c => ItemText(it, c))).ToArray()).ToList();
        if (cols.Any(c => c.Sum) && s.Items.Count > 0)
            rows.Add(new[] { "" }.Concat(cols.Select((c, i) => c.Sum ? FmtNum(s.Sum(c.Key)) : i == 0 ? "Tổng cộng" : "")).ToArray());
        _dm.Load(gcols, rows);
        _dg.Refresh();
    }

    private void OpenModule()
    {
        if (_cur is null) return;
        var nav = Src(_cur.Src).NavKey;
        if (nav.Length == 0) return;
        if (!App.Services.Session.Can(nav)) { _ = Ui.Message(this, "Tài khoản không có quyền mở module này."); return; }
        MainWindow.Of(this)?.Navigate(nav);
    }

    private async Task Export()
    {
        if (_view.Count == 0) { await Ui.Message(this, "Không có phiếu nào để xuất."); return; }
        var path = await Ui.SaveFile(this, "Xuất danh sách phiếu xuất", $"Phieu-xuat-he-thong_{DateTime.Now:yyyy-MM-dd}.xlsx", "xlsx", "Excel");
        if (path is null) return;
        var list = _view;
        if (await Ui.Run(this, () => Svc.Export(list, path)))
        {
            _status.Text = $"Đã xuất {list.Count:#,0} phiếu ra {Path.GetFileName(path)}";
            Ui.OpenExternal(path);
        }
    }
}

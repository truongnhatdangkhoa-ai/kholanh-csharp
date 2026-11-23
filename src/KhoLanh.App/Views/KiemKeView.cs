using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.KiemKe;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.SoDo;
using static KhoLanh.Core.KiemKe.KiemKeCore;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Đối chiếu kiểm kê (C#, nghiệp vụ theo "Số liệu kiểm kê" của app cũ — không dùng WebView2).
/// Số liệu kiểm kê = các trang Nhập liệu (mẫu Kiểm kê); đối chiếu với Tồn kho An An theo mã, với Kế toán theo kg, với Tồn theo vị trí trên sơ đồ; lệnh đếm; chốt kỳ.
/// </summary>
public sealed class KiemKeView : UserControl, IPage
{
    public string Title => "Đối chiếu kiểm kê";
    public string Subtitle => "Số liệu kiểm kê (trang Nhập liệu) ↔ Tồn kho An An, Kế toán và Tồn theo vị trí; gợi ý nguyên nhân lệch, lệnh đếm theo dãy, chốt kỳ kiểm kê.";

    private static KiemKeService Svc => App.Services.KiemKe;
    private static readonly IBrush Line_ = new SolidColorBrush(Color.Parse("#D0D7E2"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#B23B00"));
    private static readonly IBrush Dark = new SolidColorBrush(Color.Parse("#0B2A45"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#1F8FD1"));
    private static readonly IBrush OkBg = new SolidColorBrush(Color.Parse("#E2EFDA"));
    private static readonly IBrush BadBg = new SolidColorBrush(Color.Parse("#F8CBAD"));
    private static readonly IBrush WarnBg = new SolidColorBrush(Color.Parse("#FFE699"));
    private static readonly IBrush PurpleBg = new SolidColorBrush(Color.Parse("#D9C6F0"));
    private static readonly IBrush GrayBg = new SolidColorBrush(Color.Parse("#EDEDED"));
    private static readonly IBrush TodoBg = new SolidColorBrush(Color.Parse("#DCE6F0"));

    private readonly bool _edit, _export;
    private bool _loading;
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _info = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly Border _lockBar = new() { Background = new SolidColorBrush(Color.Parse("#FFF4CC")), BorderBrush = new SolidColorBrush(Color.Parse("#E8C766")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), Margin = new Thickness(0, 6, 0, 0), IsVisible = false };
    private readonly TabControl _tabs;

    // ---------- tab Số liệu kiểm kê
    private readonly StackPanel _pageList = new() { Spacing = 2 };
    private readonly CheckBox _latest = new() { Content = "Chỉ lấy lần đếm mới nhất (cùng vị trí · mã · lô đếm ở 2 ngày)", FontSize = 12.5 };
    private readonly TextBlock _pageInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _exInfo = new() { Spacing = 4 };
    private readonly CheckBox _gop = new() { Content = "Gộp theo mã", FontSize = 12.5 };
    private readonly GridModel _xm = new() { ReadOnly = true };
    private readonly SheetGrid _xg;
    private readonly HashSet<string> _pick = new();

    // ---------- tab Đối chiếu An An
    private readonly ComboBox _src = new() { MinWidth = 230 };
    private readonly TextBlock _srcInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _kpi = new() { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 6, 0, 4) };
    private readonly WrapPanel _todo = new() { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 0, 0, 4) };
    private readonly ComboBox _flt = new() { MinWidth = 170 };
    private readonly TextBox _q = new() { Watermark = "Tìm mã, tên…", Width = 240 };
    private readonly TextBlock _gridInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly GridModel _am = new() { ReadOnly = true };
    private readonly SheetGrid _ag;
    private List<Row> _aRows = new();
    private readonly StackPanel _det = new() { Spacing = 6 };
    private string _fltKey = "LECH";
    private static readonly (string Key, string Text)[] Flts =
        { ("LECH", "Đang lệch (cần xử lý)"), ("ALL", "Tất cả mã"), (Khop, Khop), (Thua, Thua), (Thieu, Thieu), (LechKg, LechKg), (ChiKK, ChiKK), (ChuaKK, ChuaKK) };

    // ---------- tab Kế toán
    private readonly GridModel _km = new();
    private readonly SheetGrid _kg;
    private readonly GridModel _krm = new() { ReadOnly = true };
    private readonly SheetGrid _krg;
    private readonly TextBlock _ktInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _ktOnlyLech = new() { Content = "Chỉ dòng lệch", IsChecked = true, FontSize = 12.5 };
    private List<Row> _kRows = new();
    private bool _ktDirty;

    // ---------- tab Theo vị trí
    private readonly WrapPanel _khoBar = new() { ItemSpacing = 6, LineSpacing = 6 };
    private readonly ComboBox _khu = new() { MinWidth = 120 };
    private readonly WarehouseMap _map = new() { ShowTangBar = false };
    private readonly TextBlock _vtInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _vdet = new() { Spacing = 6 };
    private string _kho = "TG1";

    // ---------- tab Lệnh đếm
    private readonly TextBox _people = new() { Watermark = "Người đếm, cách nhau bằng dấu phẩy (VD: An, Bình, Cường)", Width = 380 };
    private readonly CheckBox _onlyStock = new() { Content = "Chỉ dãy có hàng", IsChecked = true, FontSize = 12.5 };
    private readonly CheckBox _withBook = new() { Content = "Phiếu đếm có cột Sổ sách", IsChecked = false, FontSize = 12.5 };
    private readonly GridModel _om = new() { ReadOnly = true };
    private readonly SheetGrid _og;
    private List<Order> _orders = new();
    private readonly TextBlock _oInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };

    // ---------- tab Chốt kỳ
    private readonly StackPanel _ready = new() { Spacing = 4 };
    private readonly GridModel _hm = new() { ReadOnly = true };
    private readonly SheetGrid _hg;
    private List<KiemKeService.Chot> _hRows = new();
    private readonly GridModel _bm = new() { ReadOnly = true };
    private readonly TextBlock _banInfo = new() { FontSize = 12.5, FontWeight = FontWeight.SemiBold };
    private readonly Button _bChot, _bMo;

    public KiemKeView()
    {
        _edit = Svc.CanEdit; _export = Svc.CanExport;
        _xg = new SheetGrid(_xm) { SpareRows = 0 };
        _ag = new SheetGrid(_am) { SpareRows = 0, CellBackground = ABg };
        _ag.SelectionChanged += () => { if (_ag.Row >= 0 && _ag.Row < _aRows.Count) ShowDetail(_aRows[_ag.Row]); };
        _kg = new SheetGrid(_km) { SpareRows = 30 };
        _km.Changed += () => { if (!_loading) { _ktDirty = true; KtInfo(); } };
        _krg = new SheetGrid(_krm) { SpareRows = 0, CellBackground = KBg };
        _og = new SheetGrid(_om) { SpareRows = 0, CellBackground = OBg };
        _hg = new SheetGrid(_hm) { SpareRows = 0 };
        _hg.SelectionChanged += ShowBan;
        _map.Picked += VtPick;
        _map.Fill = p => Svc.Vt?.Cells.GetValueOrDefault(p.Code) is { } c ? VtBrush(c.St) : null;
        _map.CellText = p =>
        {
            if (Svc.Vt?.Cells.GetValueOrDefault(p.Code) is not { } c) return "";
            return c.St switch
            {
                VtLech => Math.Abs(c.Cnt - c.Exp) <= VtTol ? "sai lô" : (c.Cnt - c.Exp > 0 ? "+" : "") + Q(c.Cnt - c.Exp),
                VtChua => Q(c.Exp),
                VtThua => "+" + Q(c.Cnt),
                _ => "",
            };
        };
        _bChot = Btn("🔒 Chốt kỳ kiểm kê…", async () => await DoChot(), primary: true);
        _bMo = Btn("Hủy chốt…", async () => await MoChot(), danger: true);

        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Đối chiếu An An", Content = BuildAn() },
                new TabItem { Header = "Số liệu kiểm kê", Content = BuildPages() },
                new TabItem { Header = "Đối chiếu kế toán", Content = BuildKt() },
                new TabItem { Header = "Theo vị trí", Content = BuildVt() },
                new TabItem { Header = "Lệnh đếm", Content = BuildOrders() },
                new TabItem { Header = "Chốt kỳ", Content = BuildChot() },
            },
        };
        _tabs.SelectionChanged += (_, _) => { if (!_loading && Svc.Loaded) RenderTab(); };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        tools.Children.Add(Btn("⟳ Tải lại", async () => await LoadAsync()));
        if (_export) tools.Children.Add(Btn("Xuất báo cáo Excel", async () => await ExportReport(), tip: "Đối chiếu An An, kế toán, chi tiết kiểm kê, theo vị trí — mỗi phần 1 sheet"));
        var row = new DockPanel(); DockPanel.SetDock(tools, Dock.Right); row.Children.Add(tools);
        var left = new StackPanel(); left.Children.Add(_info); left.Children.Add(_status); row.Children.Add(left);
        var top = new StackPanel { Spacing = 2 }; top.Children.Add(row); top.Children.Add(_lockBar);
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(_tabs);
        Content = root;
        AttachedToVisualTree += async (_, _) => await LoadAsync();
    }

    // =====================================================================================
    // Chung
    // =====================================================================================

    private static Button Btn(string text, Action click, bool primary = false, bool danger = false, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (danger) b.Classes.Add("danger");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
    private static Button Btn(string text, Func<Task> click, bool primary = false, bool danger = false, string? tip = null) => Btn(text, () => { _ = click(); }, primary, danger, tip);
    private static TextBlock Label(string t, bool bold = false, double size = 12.5, IBrush? fg = null)
    {
        var l = new TextBlock { Text = t, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        if (fg is not null) l.Foreground = fg;
        return l;
    }
    private static TextBlock Muted(string t) => new() { Text = t, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private static string Q(double v) => v.ToString("#,0.##", Ui.Vi);
    private static string Q(double? v) => v is { } x ? Q(x) : "";
    private static string Kg(double? v) => v is { } x ? x.ToString("#,0.00", Ui.Vi) : "";
    private static string D(double? v, bool kg = false) => v is { } x ? (x > 0 ? "+" : "") + (kg ? Kg(x) : Q(x)) : "";
    private static Border Framed(Control c) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = c };
    private static Control Split(Control left, Control right, string cols = "*,10,380")
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(cols) };
        g.Children.Add(left); Grid.SetColumn(right, 2); g.Children.Add(right);
        return g;
    }
    private static Border Kpi(string label, string value, IBrush? fg = null, Action? click = null, bool active = false)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = value, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = fg ?? Dark });
        s.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Classes = { "muted" } });
        var b = new Border { Background = active ? new SolidColorBrush(Color.Parse("#E3F2FB")) : Brushes.White, BorderBrush = active ? Accent : Line_, BorderThickness = new Thickness(active ? 2 : 1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3), MinWidth = 96, Child = s };
        if (click is not null) { b.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand); b.PointerPressed += (_, _) => click(); }
        return b;
    }
    private static IBrush? StBrush(string st) => st switch { Khop => OkBg, Thua or Thieu => BadBg, LechKg => WarnBg, ChiKK => PurpleBg, ChuaKK => TodoBg, _ => null };

    private async Task LoadAsync()
    {
        _status.Text = "Đang tải…";
        try { await Task.Run(Svc.Load); }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        _status.Text = "";
        _pick.Clear(); foreach (var id in Svc.Chosen) _pick.Add(id);
        _ktDirty = false;
        _loading = true;
        _latest.IsChecked = Svc.LatestOnly;
        _people.Text = string.Join(", ", Svc.People);
        Combo(_src, new[] { "Tồn kho An An hiện tại", "Bản chụp tồn" }, Svc.StockSrc == "snap" ? 1 : 0);
        _loading = false;
        var khos = Svc.SoDo.Loaded ? Svc.SoDo.KhoList() : new List<string>();
        if (!khos.Contains(_kho)) _kho = khos.FirstOrDefault() ?? "TG1";
        RenderAll();
        if (Svc.Chosen.Count == 0 && _tabs.SelectedIndex == 0) _tabs.SelectedIndex = 1;
    }
    private static void Combo(ComboBox c, IEnumerable<string> items, int sel = 0) { var l = items.ToList(); c.ItemsSource = l; c.SelectedIndex = l.Count == 0 ? -1 : Math.Clamp(sel, 0, l.Count - 1); }

    private async Task<bool> Act(Action a, string? ok = null)
    {
        try
        {
            await Task.Run(a);
            if (ok is not null) _status.Text = ok + " (" + DateTime.Now.ToString("HH:mm:ss") + ")";
            RenderAll();
            return true;
        }
        catch (SheetConflictException)
        {
            await Ui.Message(this, "Dữ liệu vừa được máy khác sửa nên thao tác này CHƯA được lưu (để không ghi đè). Bấm Đóng để tải bản mới nhất rồi làm lại.", "Máy khác vừa sửa");
            await LoadAsync();
        }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); }
        return false;
    }

    private void RenderAll()
    {
        var s = Svc.Summary;
        _info.Text = $"{Svc.Chosen.Count} trang kiểm kê · {Svc.Lines.Count:#,0} dòng · "
            + (Svc.Stock.Count == 0 ? "chưa có Tồn kho An An" : (Svc.StockSrc == "snap" ? "đối chiếu với bản chụp tồn" : $"Tồn kho An An: {Svc.StockFile} (ngày {Svc.StockDate})"))
            + (Svc.Kt.Map.Count > 0 ? $" · Kế toán {Svc.Kt.Map.Count:#,0} mã" : "")
            + (s.Total > 0 ? $" · {s.Open} mã lệch" : "");
        var cur = Svc.CurrentChot;
        _lockBar.IsVisible = cur is not null;
        if (cur is not null)
        {
            var p = new WrapPanel { ItemSpacing = 8 };
            p.Children.Add(Label($"🔒 Kỳ kiểm kê đã chốt — bản v{cur.Ver} lúc {KiemKeService.AtText(cur.At)} ({cur.By}). Lý do: {cur.LyDo}. Không đổi được trang chọn, số kế toán, nguồn tồn cho tới khi hủy chốt.", fg: new SolidColorBrush(Color.Parse("#7A5200"))));
            if (_edit) p.Children.Add(Btn("Hủy chốt…", async () => await MoChot()));
            _lockBar.Child = p;
        }
        RenderTab();
    }
    private void RenderTab()
    {
        switch (_tabs.SelectedIndex)
        {
            case 0: RenderAn(); break;
            case 1: RenderPages(); break;
            case 2: RenderKt(); break;
            case 3: RenderVt(); break;
            case 4: RenderOrders(); break;
            case 5: RenderChot(); break;
        }
    }

    // =====================================================================================
    // Tab Số liệu kiểm kê
    // =====================================================================================

    private Control BuildPages()
    {
        var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 6) };
        head.Children.Add(Label("Trang Nhập liệu dùng làm số liệu kiểm kê", true, 14));
        head.Children.Add(Muted("Các trang mẫu Kiểm kê (hoặc mẫu riêng có cột Mã hàng + Số thùng / Số kiện / Net). Đánh dấu các trang của kỳ kiểm kê này rồi bấm Áp dụng."));
        var btns = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        btns.Children.Add(Btn("Chọn tất cả", () => { foreach (var p in Svc.Pages) _pick.Add(p.Id); RenderPageList(); }));
        btns.Children.Add(Btn("Bỏ chọn", () => { _pick.Clear(); RenderPageList(); }));
        if (_edit) btns.Children.Add(Btn("✔ Áp dụng", async () => await Act(() => Svc.SetChosen(_pick, _latest.IsChecked == true), "Đã cập nhật số liệu kiểm kê"), primary: true));
        head.Children.Add(btns); head.Children.Add(_latest); head.Children.Add(_pageInfo);
        var left = new DockPanel(); DockPanel.SetDock(head, Dock.Top); left.Children.Add(head);
        left.Children.Add(Framed(new ScrollViewer { Content = _pageList, Padding = new Thickness(6) }));

        var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 8, 0, 6) };
        bar.Children.Add(Label("Số liệu đã chọn", true, 14)); bar.Children.Add(_gop);
        _gop.IsCheckedChanged += (_, _) => { if (!_loading) RenderLines(); };
        var rhead = new StackPanel(); rhead.Children.Add(bar); rhead.Children.Add(_exInfo);
        var right = new DockPanel(); DockPanel.SetDock(rhead, Dock.Top); right.Children.Add(rhead); right.Children.Add(Framed(_xg));
        return Split(left, right, "380,10,*");
    }
    private void RenderPages() { RenderPageList(); RenderLines(); }
    private void RenderPageList()
    {
        _pageList.Children.Clear();
        if (Svc.Pages.Count == 0) _pageList.Children.Add(Muted("Chưa có trang kiểm kê nào trong Nhập liệu. Tạo trang theo mẫu Kiểm kê ở module Nhập liệu (hoặc từ Sơ đồ kho › Phiếu In vị trí › Chuyển sang Nhập liệu)."));
        foreach (var p in Svc.Pages)
        {
            var n = p.Rows.Count(r => r.Any(c => !string.IsNullOrWhiteSpace(c)));
            var cb = new CheckBox { IsChecked = _pick.Contains(p.Id), IsEnabled = _edit && !Svc.Locked, FontSize = 12.5 };
            var t = new StackPanel(); t.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            t.Children.Add(Muted($"{(p.Ngay.Length > 0 ? DateTime.ParseExact(p.Ngay, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dd/MM/yyyy") : "—")} · {n} dòng" + (Svc.Chosen.Contains(p.Id) ? " · đang dùng" : "")));
            cb.Content = t;
            cb.IsCheckedChanged += (_, _) => { if (cb.IsChecked == true) _pick.Add(p.Id); else _pick.Remove(p.Id); PageInfo(); };
            _pageList.Children.Add(cb);
        }
        PageInfo();
    }
    private void PageInfo()
    {
        var changed = !_pick.SetEquals(Svc.Chosen) || (_latest.IsChecked == true) != Svc.LatestOnly;
        _pageInfo.Text = $"Đã đánh dấu {_pick.Count} / {Svc.Pages.Count} trang" + (changed ? " — chưa áp dụng" : "");
        _pageInfo.Foreground = changed ? Warn : null;
    }
    private void RenderLines()
    {
        _exInfo.Children.Clear();
        var ex = Svc.Ex;
        var w = new List<string>();
        if (ex.KhongMa > 0) w.Add($"{ex.KhongMa} dòng có số nhưng không có mã (không tính)");
        if (ex.SoLoi > 0) w.Add($"{ex.SoLoi} dòng có số không đọc được");
        if (ex.ThieuKien > 0) w.Add($"{ex.ThieuKien} dòng thiếu số kiện");
        if (ex.ThieuKg > 0) w.Add($"{ex.ThieuKg} dòng chưa tính được kg (thiếu Kg / Net)");
        foreach (var s in ex.Skipped) w.Add($"Bỏ qua trang “{s.Name}”: {s.Why}");
        if (Svc.Recounts.Count > 0) w.Add($"{Svc.Recounts.Count} nhóm vị trí · mã · lô đếm ở ≥ 2 ngày" + (Svc.LatestOnly ? $" — đã bỏ {Svc.Dropped.Count} dòng của lần đếm cũ" : " — có thể cộng trùng"));
        _exInfo.Children.Add(Label($"{Svc.Lines.Count:#,0} dòng kiểm kê · {Q(Svc.Lines.Sum(l => l.Kien))} kiện · {Kg(Svc.Lines.Sum(l => l.Kg ?? 0))} kg", true));
        foreach (var s in w) _exInfo.Children.Add(Label("⚠ " + s, fg: Warn, size: 12));
        if (ex.Samples.Count > 0) _exInfo.Children.Add(Muted("Ví dụ: " + string.Join(" · ", ex.Samples.Take(4))));
        if (_gop.IsChecked == true)
        {
            var g = Svc.Lines.GroupBy(l => l.Key).OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new[] { x.First().Ma, Svc.T.Rows.FirstOrDefault(r => r.Key == x.Key)?.Ten ?? x.First().Ten, x.Count().ToString(CultureInfo.InvariantCulture), Q(x.Sum(l => l.Kien)), Kg(x.Sum(l => l.Kg ?? 0)), string.Join(", ", x.Select(l => l.Page).Distinct()) });
            _xm.Load(new[] { new GridColumn("ma", "Mã hàng", 235), new GridColumn("ten", "Tên hàng", 320), new GridColumn("n", "Số dòng", 70, "number"), new GridColumn("k", "Số kiện", 90, "number"), new GridColumn("kg", "Kg", 100, "number"), new GridColumn("p", "Trang", 260) }, g);
        }
        else
            _xm.Load(new[]
            {
                new GridColumn("ng", "Ngày", 85), new GridColumn("p", "Trang", 200), new GridColumn("r", "Dòng", 50, "number"), new GridColumn("vt", "Vị trí", 110), new GridColumn("ma", "Mã hàng", 235),
                new GridColumn("hd", "Hợp đồng", 90), new GridColumn("lo", "Lô", 90), new GridColumn("k", "Số kiện", 75, "number"), new GridColumn("kg", "Kg", 90, "number"), new GridColumn("gc", "Ghi chú", 160),
            }, Svc.Lines.Select(l => new[] { l.Ngay.Length > 0 ? l.Ngay[8..] + "/" + l.Ngay[5..7] : "", l.Page, l.Row.ToString(CultureInfo.InvariantCulture), l.Vt, l.Ma, l.Hd, l.Lo, Q(l.Kien), Kg(l.Kg), l.Gc }));
        _xg.Refresh();
    }

    // =====================================================================================
    // Tab Đối chiếu An An
    // =====================================================================================

    private Control BuildAn()
    {
        var srcRow = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        srcRow.Children.Add(Label("Đối chiếu với", true)); srcRow.Children.Add(_src);
        _src.SelectionChanged += async (_, _) => { if (_loading) return; var want = _src.SelectedIndex == 1 ? "snap" : "live"; if (want != Svc.StockSrc && !await Act(() => Svc.SetStockSrc(want))) { _loading = true; _src.SelectedIndex = Svc.StockSrc == "snap" ? 1 : 0; _loading = false; } };
        if (_edit) srcRow.Children.Add(Btn("📸 Chụp tồn kho", async () => { if (await Ui.Confirm(this, $"Chụp Tồn kho An An hiện tại ({Svc.StockFile}, ngày {Svc.StockDate}) làm bản cố định để đối chiếu? Nạp file tồn mới sau đó sẽ không làm đổi kết quả đối chiếu.", "Chụp tồn kho", "Chụp")) await Act(() => Svc.SnapStock(), "Đã chụp tồn kho"); },
            tip: "Giữ cố định số tồn lúc kiểm kê — nạp file tồn mới sau đó không làm đổi đối chiếu"));
        srcRow.Children.Add(_srcInfo);
        var bar = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 2, 0, 6) };
        Combo(_flt, Flts.Select(f => f.Text));
        _flt.SelectionChanged += (_, _) => { if (_loading) return; _fltKey = Flts[Math.Max(0, _flt.SelectedIndex)].Key; RenderAnGrid(); RenderKpi(); };
        _q.TextChanged += (_, _) => { if (!_loading) RenderAnGrid(); };
        bar.Children.Add(_flt); bar.Children.Add(_q); bar.Children.Add(_gridInfo);
        var head = new StackPanel(); head.Children.Add(srcRow); head.Children.Add(_kpi); head.Children.Add(_todo); head.Children.Add(bar);
        var left = new DockPanel(); DockPanel.SetDock(head, Dock.Top); left.Children.Add(head); left.Children.Add(Framed(_ag));
        var det = new Border { Child = new ScrollViewer { Content = _det, Padding = new Thickness(0, 0, 8, 0) }, Margin = new Thickness(0, 8, 0, 0) };
        return Split(left, det);
    }
    private void RenderAn()
    {
        _srcInfo.Text = Svc.StockSrc == "snap" ? "Bản chụp: " + Svc.SnapInfo : Svc.SnapInfo.Length > 0 ? "Có bản chụp: " + Svc.SnapInfo : "";
        RenderKpi(); RenderTodo(); RenderAnGrid();
    }
    private void SetFlt(string key) { _fltKey = key; _loading = true; _flt.SelectedIndex = Math.Max(0, Array.FindIndex(Flts, f => f.Key == key)); _loading = false; RenderKpi(); RenderAnGrid(); }
    private void RenderKpi()
    {
        var s = Svc.Summary; _kpi.Children.Clear();
        _kpi.Children.Add(Kpi("Mã đối chiếu", s.Total.ToString("#,0", Ui.Vi), null, () => SetFlt("ALL"), _fltKey == "ALL"));
        _kpi.Children.Add(Kpi("Khớp", s.Khop.ToString("#,0", Ui.Vi), new SolidColorBrush(Color.Parse("#2E7D32")), () => SetFlt(Khop), _fltKey == Khop));
        _kpi.Children.Add(Kpi("Đang lệch", s.Open.ToString("#,0", Ui.Vi), s.Open > 0 ? Warn : null, () => SetFlt("LECH"), _fltKey == "LECH"));
        _kpi.Children.Add(Kpi("Thừa", s.Thua.ToString(Ui.Vi), null, () => SetFlt(Thua), _fltKey == Thua));
        _kpi.Children.Add(Kpi("Thiếu", s.Thieu.ToString(Ui.Vi), null, () => SetFlt(Thieu), _fltKey == Thieu));
        _kpi.Children.Add(Kpi("Lệch kg", s.LechKg.ToString(Ui.Vi), null, () => SetFlt(LechKg), _fltKey == LechKg));
        _kpi.Children.Add(Kpi("Chỉ có ở kiểm kê", s.ChiKK.ToString(Ui.Vi), null, () => SetFlt(ChiKK), _fltKey == ChiKK));
        _kpi.Children.Add(Kpi("Chưa kiểm kê", s.ChuaKK.ToString(Ui.Vi), null, () => SetFlt(ChuaKK), _fltKey == ChuaKK));
        _kpi.Children.Add(Kpi("Tổng kg lệch", s.KgLech.ToString("#,0.##", Ui.Vi), s.KgLech > 0 ? Warn : null));
    }
    private void RenderTodo()
    {
        _todo.Children.Clear(); var s = Svc.Summary;
        if (Svc.Lines.Count == 0) { _todo.Children.Add(Label("📝 Chưa có số liệu kiểm kê — chọn trang ở tab “Số liệu kiểm kê”.", fg: Warn)); _todo.Children.Add(Btn("Mở Số liệu kiểm kê", () => _tabs.SelectedIndex = 1)); }
        if (Svc.Stock.Count == 0) _todo.Children.Add(Label("📦 Chưa có Tồn kho An An — nạp file ở module Tồn kho An An.", fg: Warn));
        if (s.Auto > 0 && _edit && !Svc.Locked)
            _todo.Children.Add(Btn($"🪄 Sửa {s.Auto} mã kiểm kê gõ sai theo mã An An", async () => await FixAll(), tip: "Cặp mã chỉ khác 1–2 ký tự và cùng số kg — đổi mã ở trang Nhập liệu nguồn"));
        if (Svc.Recounts.Count > 0 && !Svc.LatestOnly) _todo.Children.Add(Btn($"⚠ {Svc.Recounts.Count} nhóm đếm ở 2 ngày — xem", () => _tabs.SelectedIndex = 1));
    }
    private List<Row> AnFiltered()
    {
        var q = (_q.Text ?? "").Trim().ToLowerInvariant();
        return Svc.T.Rows.Where(r => r.Status != ChiKT)
            .Where(r => _fltKey == "ALL" || (_fltKey == "LECH" ? KiemKeCore.Lech.Contains(r.Status) : r.Status == _fltKey))
            .Where(r => q.Length == 0 || (r.Ma + " " + r.Ten).ToLowerInvariant().Contains(q))
            .OrderByDescending(r => _fltKey is "ALL" or Khop ? 0 : Math.Abs(r.DKg ?? 0)).ThenBy(r => r.Ma, StringComparer.Ordinal).ToList();
    }
    private void RenderAnGrid()
    {
        _aRows = AnFiltered();
        _am.Load(new[]
        {
            new GridColumn("ma", "Mã hàng", 235), new GridColumn("ten", "Tên hàng", 190), new GridColumn("kk", "Kiểm kê (kiện)", 90, "number"), new GridColumn("ton", "Tồn kho (kiện)", 90, "number"),
            new GridColumn("dk", "Chênh (kiện)", 85, "number"), new GridColumn("kkg", "Kiểm kê (kg)", 95, "number"), new GridColumn("tkg", "Tồn kho (kg)", 95, "number"), new GridColumn("dkg", "Chênh (kg)", 90, "number"),
            new GridColumn("st", "Kết quả", 110), new GridColumn("nn", "Nguyên nhân (gợi ý)", 200),
        }, _aRows.Select(r => new[] { r.Ma, r.Ten, Q(r.KkKien), Q(r.TonKien), D(r.DKien), Kg(r.KkKg), Kg(r.TonKg), D(r.DKg, true), r.Status, r.Nn?.Label ?? "" }));
        _ag.Refresh();
        _gridInfo.Text = $"{_aRows.Count:#,0} mã";
        if (_aRows.Count > 0) { _ag.Select(0, 0); ShowDetail(_aRows[0]); } else ShowDetail(null);
    }
    private IBrush? ABg(int r, int c)
    {
        if (r < 0 || r >= _aRows.Count) return null;
        var x = _aRows[r];
        if (c == 8) return StBrush(x.Status);
        if ((c == 4 && (x.DKien ?? 0) != 0) || (c == 7 && Math.Abs(x.DKg ?? 0) > KgTol)) return BadBg;
        if (c == 9 && x.Nn is { Level: "xd" }) return OkBg;
        return null;
    }
    private void ShowDetail(Row? r)
    {
        _det.Children.Clear();
        if (r is null) { _det.Children.Add(Muted("Bấm một dòng để xem chi tiết: dòng kiểm kê ở trang nào, tồn theo hợp đồng, gợi ý nguyên nhân.")); return; }
        _det.Children.Add(new SelectableTextBlock { Text = r.Ma, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Dark });
        if (r.Ten.Length > 0) _det.Children.Add(Muted(r.Ten));
        _det.Children.Add(new Border { Background = StBrush(r.Status) ?? GrayBg, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = r.Status, FontSize = 12 } });
        _det.Children.Add(Label($"Kiểm kê: {Q(r.KkKien ?? 0)} kiện · {Kg(r.KkKg ?? 0)} kg ({r.KkRows} dòng, {r.KkDays} ngày)"));
        _det.Children.Add(Label($"Tồn kho: {Q(r.TonKien ?? 0)} kiện · {Kg(r.TonKg ?? 0)} kg"));
        if (r.DKien is not null || r.DKg is not null) _det.Children.Add(Label($"Chênh: {D(r.DKien)} kiện · {D(r.DKg, true)} kg", true, fg: KiemKeCore.Lech.Contains(r.Status) ? Warn : null));
        if (r.Nn is { } n)
        {
            _det.Children.Add(Label((n.Level == "xd" ? "✔ " : n.Level == "ng" ? "? " : "") + n.Label + (n.Level == "xd" ? " (xác định)" : n.Level == "ng" ? " (nghi ngờ — cần xác nhận)" : ""), true, 13.5));
            foreach (var e in n.Evidence) _det.Children.Add(Muted("• " + e));
            if (n is { Code: "sai-ma", Side: "kk", FixTo: { } to } && _edit && !Svc.Locked)
                _det.Children.Add(Btn($"Sửa mã kiểm kê thành {to}", async () => { if (await Ui.Confirm(this, $"Đổi mã {r.Ma} → {to} ở mọi dòng kiểm kê (ghi vào trang Nhập liệu nguồn)?", "Sửa mã", "Đổi mã")) await Act(() => Svc.FixMa(new[] { (r.Key, to) }), "Đã sửa mã"); }, primary: true));
            if (n.PartnerMa is { } pm) _det.Children.Add(Btn("Xem mã " + pm, () => { _q.Text = ""; SetFlt("ALL"); var i = _aRows.FindIndex(x => x.Ma == pm); if (i >= 0) { _ag.Select(i, 0); ShowDetail(_aRows[i]); } }));
            if (n.Sug.Count > 0) _det.Children.Add(Muted("Mã gần giống: " + string.Join(", ", n.Sug.Select(s => $"{s.Ma} (khác {s.Dist} ký tự)"))));
        }
        foreach (var note in r.Notes) _det.Children.Add(Label("⚠ " + note, fg: Warn, size: 12));
        var lines = Svc.Lines.Where(l => l.Key == r.Key).ToList();
        if (lines.Count > 0)
        {
            _det.Children.Add(Label("Dòng kiểm kê", true, 13.5));
            foreach (var l in lines.Take(40)) _det.Children.Add(Muted($"{l.Page} · dòng {l.Row}" + (l.Vt.Length > 0 ? " · " + l.Vt : "") + (l.Lo.Length > 0 ? " · lô " + l.Lo : "") + (l.Hd.Length > 0 ? " · HĐ " + l.Hd : "") + $" · {Q(l.Kien)} kiện · {Kg(l.Kg)} kg"));
            if (lines.Count > 40) _det.Children.Add(Muted($"… và {lines.Count - 40} dòng nữa"));
        }
        var hd = DrillHd(r.Key, Svc.Lines, Svc.Stock);
        if (hd.Count > 1)
        {
            _det.Children.Add(Label("Theo hợp đồng (kiểm kê ↔ tồn)", true, 13.5));
            foreach (var h in hd) _det.Children.Add(Label($"{h.Hd}: {Q(h.Kk)} ↔ {Q(h.Ton)} kiện" + (Math.Abs(h.D) > 1e-9 ? $" (chênh {D(h.D)})" : ""), size: 12, fg: Math.Abs(h.D) > 1e-9 ? Warn : null));
        }
    }
    private async Task FixAll()
    {
        var list = Svc.T.Rows.Where(r => r.Nn is { Code: "sai-ma", Side: "kk", Strong: true, FixTo: not null }).Select(r => (r.Key, To: r.Nn!.FixTo!, r.Ma)).ToList();
        if (list.Count == 0) return;
        var msg = $"Đổi mã kiểm kê theo mã An An cho {list.Count} mã?\n\n" + string.Join("\n", list.Take(8).Select(x => x.Ma + " → " + x.To)) + (list.Count > 8 ? $"\n… và {list.Count - 8} mã nữa" : "")
            + "\n\nMỗi cặp chỉ khác 1–2 ký tự và cùng số kg. Mã được sửa thẳng trong trang Nhập liệu nguồn (ghi nhật ký).";
        if (!await Ui.Confirm(this, msg, "Sửa mã gõ sai", $"Đổi {list.Count} mã")) return;
        await Act(() => Svc.FixMa(list.Select(x => (x.Key, x.To))), $"Đã sửa {list.Count} mã");
    }

    // =====================================================================================
    // Tab Đối chiếu kế toán
    // =====================================================================================

    private static readonly GridColumn[] KtCols = { new("ma", "Mã hàng", 235), new("ten", "Tên hàng", 180), new("tl", "Trọng lượng", 85, "number"), new("sl", "Số lượng", 80, "number"), new("net", "Net (kg)", 95, "number") };
    private Control BuildKt()
    {
        var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 6) };
        head.Children.Add(Label("Số liệu Kế toán", true, 14));
        head.Children.Add(Muted("Dán thẳng từ Excel (Ctrl+V) hoặc nạp file: Mã hàng, Tên hàng, Trọng lượng, Số lượng, Net. Net trống thì tính Số lượng × Trọng lượng."));
        var b = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        if (_edit)
        {
            b.Children.Add(Btn("📂 Nạp file Excel…", async () => await KtFile()));
            b.Children.Add(Btn("💾 Lưu số kế toán", async () => await KtSave(), primary: true));
            b.Children.Add(Btn("Xóa hết", async () => { if (await Ui.Confirm(this, "Xóa toàn bộ số kế toán đã lưu?", "Xóa số kế toán", "Xóa")) await Act(() => Svc.SaveKeToan(Array.Empty<KtRow>(), ""), "Đã xóa số kế toán"); }, danger: true));
        }
        head.Children.Add(b); head.Children.Add(_ktInfo);
        var left = new DockPanel(); DockPanel.SetDock(head, Dock.Top); left.Children.Add(head); left.Children.Add(Framed(_kg));
        var rh = new WrapPanel { ItemSpacing = 8, Margin = new Thickness(0, 8, 0, 6) };
        rh.Children.Add(Label("Kiểm kê (kg) ↔ Kế toán (Net kg)", true, 14)); rh.Children.Add(_ktOnlyLech);
        _ktOnlyLech.IsCheckedChanged += (_, _) => { if (!_loading) RenderKtResult(); };
        var right = new DockPanel(); DockPanel.SetDock(rh, Dock.Top); right.Children.Add(rh); right.Children.Add(Framed(_krg));
        return Split(left, right, "5*,10,6*");
    }
    private void RenderKt()
    {
        if (!_ktDirty)
        {
            _loading = true;
            _km.Load(KtCols, Svc.KtRows.Select(r => new[] { r.Ma, r.Ten, r.Tl, r.Sl, r.Net }));
            _km.ReadOnly = !_edit || Svc.Locked;
            _loading = false;
            _kg.Refresh();
        }
        KtInfo(); RenderKtResult();
    }
    private void KtInfo()
    {
        var k = Svc.Kt;
        _ktInfo.Text = (Svc.KtRows.Count == 0 ? "Chưa có số kế toán." : $"Đã lưu {k.N:#,0} dòng / {k.Map.Count:#,0} mã · {Svc.KtInfo}"
            + (k.KhongMa + k.ThieuNet + k.SoLoi > 0 ? $" · ⚠ {k.KhongMa} dòng thiếu mã, {k.ThieuNet} dòng thiếu Net, {k.SoLoi} số lỗi" : ""))
            + (_ktDirty ? " — có thay đổi chưa lưu" : "");
        _ktInfo.Foreground = _ktDirty ? Warn : null;
    }
    private List<KtRow> KtFromGrid() => _km.Snapshot().Select(r => new KtRow(At(r, 0), At(r, 1), At(r, 2), At(r, 3), At(r, 4))).Where(r => (r.Ma + r.Tl + r.Sl + r.Net).Trim().Length > 0).ToList();
    private static string At(string[] r, int i) => i < r.Length ? r[i] ?? "" : "";
    private async Task KtSave() { if (await Act(() => Svc.SaveKeToan(KtFromGrid(), "Dán / gõ tay"), "Đã lưu số kế toán")) { _ktDirty = false; RenderKt(); } }
    private async Task KtFile()
    {
        var path = await Ui.OpenFile(this, "Chọn file Excel kế toán", "xlsx", "Excel");
        if (path is null) return;
        List<KtRow> rows;
        try { rows = KiemKeService.ReadKeToanFile(path); }
        catch (Exception e) { await Ui.Message(this, e is BusinessException ? e.Message : Ui.Describe(e), "Không đọc được file"); return; }
        if (await Act(() => Svc.SaveKeToan(rows, "File " + Path.GetFileName(path)), $"Đã nạp {rows.Count} dòng kế toán")) { _ktDirty = false; RenderKt(); }
    }
    private void RenderKtResult()
    {
        _kRows = Svc.T.Rows.Where(r => Svc.T.HasKT && (r.KtStatus.Length > 0 || r.KtKg is not null)).Where(r => _ktOnlyLech.IsChecked != true || r.KtStatus is "Lệch" or "Chưa tính được")
            .OrderByDescending(r => Math.Abs(r.TongKt ?? r.ChotKg ?? 0)).ThenBy(r => r.Ma, StringComparer.Ordinal).ToList();
        _krm.Load(new[] { new GridColumn("ma", "Mã hàng", 235), new GridColumn("ten", "Tên hàng", 200), new GridColumn("kk", "Kiểm kê (kg)", 95, "number"), new GridColumn("kt", "Kế toán (kg)", 95, "number"), new GridColumn("d", "KK − KT (kg)", 95, "number"), new GridColumn("st", "Kết quả", 95), new GridColumn("gc", "Ghi chú", 200) },
            _kRows.Select(r => new[] { r.Ma, r.Ten, Kg(r.ChotKg), Kg(r.KtKg), D(r.TongKt, true), r.KtStatus, string.Join("; ", r.Notes.Where(n => n.Contains("Kế toán"))) }));
        _krg.Refresh();
    }
    private IBrush? KBg(int r, int c) => r >= 0 && r < _kRows.Count && c == 5 ? (_kRows[r].KtStatus == "Khớp" ? OkBg : _kRows[r].KtStatus == "Lệch" ? BadBg : WarnBg) : null;

    // =====================================================================================
    // Tab Theo vị trí
    // =====================================================================================

    private static IBrush VtBrush(string st) => st switch { VtKhop => OkBg, VtLech => BadBg, VtThua => PurpleBg, VtChua => TodoBg, _ => Brushes.White };
    private Control BuildVt()
    {
        var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 8, 0, 6) };
        bar.Children.Add(_khoBar); bar.Children.Add(Label("Khu")); bar.Children.Add(_khu);
        _khu.SelectionChanged += (_, _) => { if (!_loading) RenderMap(); };
        void Legend(string text, IBrush bg) { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }; s.Children.Add(new Border { Width = 16, Height = 12, Background = bg, BorderBrush = Line_, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }); s.Children.Add(Label(text, size: 12)); bar.Children.Add(s); }
        Legend("Chưa đếm (số = sổ sách)", TodoBg); Legend("Khớp", OkBg); Legend("Lệch (số = chênh kiện)", BadBg); Legend("Thừa hàng (sổ trống)", PurpleBg); Legend("Trống đúng", Brushes.White);
        var head = new StackPanel(); head.Children.Add(bar); head.Children.Add(_vtInfo);
        var left = new DockPanel(); DockPanel.SetDock(head, Dock.Top); left.Children.Add(head);
        left.Children.Add(Framed(new ScrollViewer { Content = _map, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }));
        var det = new Border { Child = new ScrollViewer { Content = _vdet, Padding = new Thickness(0, 0, 8, 0) }, Margin = new Thickness(0, 8, 0, 0) };
        return Split(left, det, "*,10,340");
    }
    private void RenderVt()
    {
        _khoBar.Children.Clear();
        if (!Svc.SoDo.Loaded || Svc.Vt is null) { _vtInfo.Text = "Chưa đọc được Sơ đồ kho / Tồn theo vị trí."; _map.SetData(new(), null); return; }
        foreach (var kho in Svc.SoDo.KhoList())
        {
            var days = Svc.Vt.Days.Values.Where(d => d.Kho == kho).ToList(); int exp = days.Sum(d => d.NExp), done = days.Sum(d => d.NDone);
            var b = new Button { Content = $"{KhoLabel(kho)} · {(exp > 0 ? done * 100 / exp : 0)}%", Padding = new Thickness(12, 5), FontSize = 13, CornerRadius = new CornerRadius(14) };
            if (kho == _kho) b.Classes.Add("primary");
            ToolTip.SetTip(b, $"Đã đếm {done}/{exp} vị trí có hàng");
            var k = kho; b.Click += (_, _) => { _kho = k; RenderVt(); };
            _khoBar.Children.Add(b);
        }
        _loading = true;
        var khus = Svc.SoDo.KhuList(_kho); var cur = _khu.SelectedItem as string;
        var items = new[] { "Tất cả khu" }.Concat(khus.Select(k => "Khu " + k)).ToList();
        Combo(_khu, items, Math.Max(0, cur is null ? 0 : items.IndexOf(cur)));
        _loading = false;
        RenderMap();
    }
    private static string KhoLabel(string k) => SoDoCore.KhoLabel(k);
    private void RenderMap()
    {
        var v = Svc.Vt!; var b = Svc.SoDo.B;
        var khu = _khu.SelectedIndex <= 0 ? "ALL" : (_khu.SelectedItem as string ?? "").Replace("Khu ", "");
        var sections = new List<WarehouseMap.Section>();
        foreach (var e in (b.Cfg.GetValueOrDefault(_kho) ?? new()).Where(e => khu == "ALL" || e.Khu == khu))
        {
            var blocks = new List<WarehouseMap.Block>();
            for (var d = 1; d <= e.D; d++)
            {
                var cells = new Dictionary<(int, int), SoDoCore.Pos>(); var nt = SoDoCore.TangOf(e, d);
                for (var t = 1; t <= nt; t++) for (var k = 1; k <= e.K; k++) if (b.ByCode.GetValueOrDefault(SoDoCore.CodeOf(_kho, e.Khu, d, t, k)) is { } p) cells[(t, k)] = p;
                if (cells.Count > 0) blocks.Add(new WarehouseMap.Block(e.Khu, d, nt, e.K, false, cells));
            }
            var days = v.Days.Values.Where(x => x.Kho == _kho && x.Khu == e.Khu).ToList();
            sections.Add(new WarehouseMap.Section($"Khu {e.Khu}  ·  đã đếm {days.Sum(x => x.NDone)}/{days.Sum(x => x.NExp)} vị trí có hàng  ·  {days.Sum(x => x.NLech)} lệch  ·  {days.Sum(x => x.NThua)} thừa", blocks));
        }
        _map.SetData(sections, null);
        var S = v.S;
        _vtInfo.Text = $"Toàn bộ kho: {S["coHang"]:#,0} vị trí có hàng · chưa đếm {S[VtChua]:#,0} · khớp {S[VtKhop]:#,0} · lệch {S[VtLech]:#,0} · thừa hàng {S[VtThua]:#,0}"
            + (v.Bad.Count > 0 ? $" · ⚠ {v.Bad.Count} dòng kiểm kê không vẽ được (không có / sai vị trí)" : "");
        VtPick(null);
    }
    private void VtPick(SoDoCore.Pos? p)
    {
        _vdet.Children.Clear();
        if (p is null || Svc.Vt?.Cells.GetValueOrDefault(p.Code) is not { } c)
        {
            _vdet.Children.Add(Label("Chi tiết vị trí", true, 15));
            _vdet.Children.Add(Muted("Bấm một ô để xem sổ sách ↔ số đếm theo mã · lô."));
            if (Svc.Vt is { Bad.Count: > 0 } v)
            {
                _vdet.Children.Add(Label($"Dòng kiểm kê không vẽ được ({v.Bad.Count})", true, 13.5, Warn));
                foreach (var (r, why) in v.Bad.Take(30)) _vdet.Children.Add(Muted($"{r.Page} · dòng {r.Row} · {r.Ma}: {why}"));
            }
            return;
        }
        _vdet.Children.Add(new TextBlock { Text = c.P.Code, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Dark });
        _vdet.Children.Add(new Border { Background = VtBrush(c.St), BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = VtText(c.St), FontSize = 12 } });
        _vdet.Children.Add(Label($"Sổ sách {Q(c.Exp)} kiện ({c.NExp} dòng) · Kiểm kê {Q(c.Cnt)} kiện ({c.NCnt} dòng)"));
        foreach (var i in c.Items)
        {
            var s = new StackPanel();
            s.Children.Add(new TextBlock { Text = Svc.SoDo.ShowMa(i.Ma, "", true) is { Length: > 0 } m && m != i.Ma ? m : i.Ma, FontWeight = FontWeight.SemiBold, FontSize = 12.5 });
            s.Children.Add(Muted((i.Lo.Length > 0 ? "Lô " + i.Lo + " · " : "") + $"sổ {Q(i.Exp)} · đếm {Q(i.Cnt)}" + (Math.Abs(i.Diff) > VtTol ? $" · chênh {D(i.Diff)}" : " · khớp")));
            if (i.Note.Length > 0) s.Children.Add(Muted(i.Note));
            _vdet.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4), Background = Math.Abs(i.Diff) > VtTol ? BadBg : null, Child = s });
        }
        if (c.Lines.Count > 0)
        {
            _vdet.Children.Add(Label("Dòng kiểm kê", true, 13.5));
            foreach (var l in c.Lines) _vdet.Children.Add(Muted($"{l.Page} · dòng {l.Row} · {l.Ma}" + (l.Lo.Length > 0 ? " · lô " + l.Lo : "") + $" · {Q(l.Kien)} kiện"));
        }
    }

    // =====================================================================================
    // Tab Lệnh đếm
    // =====================================================================================

    private Control BuildOrders()
    {
        var bar = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 8, 0, 6) };
        bar.Children.Add(_people); bar.Children.Add(_onlyStock);
        bar.Children.Add(Btn("Chia lệnh", async () => { if (_edit) await Act(() => Svc.SetPeople(PeopleList())); RenderOrders(); }, primary: true, tip: "Mỗi dãy 1 lệnh, chia đều cho người đếm theo số vị trí có hàng"));
        bar.Children.Add(_withBook);
        if (_export)
        {
            bar.Children.Add(Btn("Xuất phiếu đếm (dòng đang chọn)", async () => await ExportOrders(sel: true)));
            bar.Children.Add(Btn("Xuất phiếu đếm tất cả", async () => await ExportOrders(sel: false)));
        }
        _onlyStock.IsCheckedChanged += (_, _) => { if (!_loading) RenderOrders(); };
        var hint = Muted("Phạm vi theo kho đang chọn ở tab “Theo vị trí”. Tiến độ tự cập nhật theo các trang kiểm kê đã chọn (vị trí có hàng đã có dòng kiểm kê / tổng vị trí có hàng). Phiếu đếm mặc định KHÔNG in số sổ sách để đếm khách quan.");
        var head = new StackPanel(); head.Children.Add(bar); head.Children.Add(hint); head.Children.Add(_oInfo);
        _oInfo.Margin = new Thickness(0, 4, 0, 6);
        var p = new DockPanel(); DockPanel.SetDock(head, Dock.Top); p.Children.Add(head); p.Children.Add(Framed(_og));
        return p;
    }
    private List<string> PeopleList() => (_people.Text ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    private void RenderOrders()
    {
        if (!Svc.SoDo.Loaded) { _oInfo.Text = "Chưa đọc được Sơ đồ kho / Tồn theo vị trí."; _orders = new(); _om.Load(Array.Empty<GridColumn>(), Array.Empty<string[]>()); return; }
        _orders = MakeOrders(Svc.SoDo.B, _kho, "ALL", _onlyStock.IsChecked == true, PeopleList());
        var pr = _orders.Select(o => Progress(o, Svc.Vt)).ToList();
        _om.Load(new[]
        {
            new GridColumn("id", "Lệnh (dãy)", 120), new GridColumn("ng", "Người đếm", 130), new GridColumn("pos", "Số vị trí", 75, "number"), new GridColumn("exp", "Vị trí có hàng", 95, "number"),
            new GridColumn("done", "Đã đếm", 75, "number"), new GridColumn("pct", "Tiến độ", 70), new GridColumn("lech", "Lệch", 60, "number"), new GridColumn("thua", "Thừa", 60, "number"), new GridColumn("st", "Trạng thái", 130),
        }, _orders.Select((o, i) => new[] { $"{KhoLabel(o.Kho)} · {o.Khu} · Dãy {SoDoCore.Two(o.D)}", o.Nguoi, o.NPos.ToString(CultureInfo.InvariantCulture), o.NExp.ToString(CultureInfo.InvariantCulture), pr[i].NDone.ToString(CultureInfo.InvariantCulture), pr[i].Pct + "%", pr[i].Lech.ToString(CultureInfo.InvariantCulture), pr[i].Thua.ToString(CultureInfo.InvariantCulture), ProgText(pr[i].State) }));
        _og.Refresh();
        int exp = pr.Sum(x => x.NExp), done = pr.Sum(x => x.NDone);
        _oInfo.Text = $"{KhoLabel(_kho)}: {_orders.Count} lệnh · đã đếm {done:#,0}/{exp:#,0} vị trí có hàng ({(exp > 0 ? done * 100 / exp : 0)}%) · {pr.Count(x => x.State == "xong")} lệnh xong khớp · {pr.Count(x => x.State == "xong-lech")} lệnh xong còn lệch";
    }
    private IBrush? OBg(int r, int c)
    {
        if (r < 0 || r >= _orders.Count || c != 8) return null;
        return Progress(_orders[r], Svc.Vt).State switch { "xong" => OkBg, "xong-lech" => BadBg, "dang" => WarnBg, "chua" => TodoBg, _ => null };
    }
    private async Task ExportOrders(bool sel)
    {
        var list = sel ? _orders.Skip(_og.Selection.R1).Take(Math.Max(1, _og.Selection.R2 - _og.Selection.R1 + 1)).ToList() : _orders;
        if (list.Count == 0) return;
        var path = await Ui.SaveFile(this, "Xuất phiếu đếm", Ui.SafeFileName($"Phieu dem {KhoLabel(_kho)} {DateTime.Today:dd.MM.yyyy}") + ".xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.ExportOrders(path, list, _withBook.IsChecked == true))) Ui.OpenExternal(path);
    }

    // =====================================================================================
    // Tab Chốt kỳ
    // =====================================================================================

    private Control BuildChot()
    {
        var left = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 12, 0) };
        left.Children.Add(Label("Kiểm tra trước khi chốt", true, 14));
        left.Children.Add(_ready);
        var b = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        if (_edit) { b.Children.Add(_bChot); b.Children.Add(_bMo); }
        left.Children.Add(b);
        left.Children.Add(Muted("Chốt lưu lại bảng đối chiếu hiện tại (số kiểm kê, tồn, kế toán theo mã) thành một bản có số phiên bản, kèm lý do. Khi đã chốt, không đổi được trang chọn, số kế toán và nguồn tồn cho tới khi hủy chốt (cũng phải ghi lý do)."));
        var right = new DockPanel();
        var rh = new StackPanel { Margin = new Thickness(0, 8, 0, 6) }; rh.Children.Add(Label("Lịch sử chốt", true, 14));
        DockPanel.SetDock(rh, Dock.Top); right.Children.Add(rh);
        var g = new Grid { RowDefinitions = new RowDefinitions("200,Auto,*") };
        g.Children.Add(Framed(_hg));
        _banInfo.Margin = new Thickness(0, 8, 0, 4); Grid.SetRow(_banInfo, 1); g.Children.Add(_banInfo);
        var bg = Framed(new SheetGrid(_bm) { SpareRows = 0 }); Grid.SetRow(bg, 2); g.Children.Add(bg);
        right.Children.Add(g);
        return Split(new ScrollViewer { Content = left }, right, "420,10,*");
    }
    private void RenderChot()
    {
        _ready.Children.Clear();
        foreach (var c in Svc.Readiness())
            _ready.Children.Add(Label((c.Ok == "ok" ? "✔ " : c.Ok == "warn" ? "⚠ " : "✖ ") + c.Text, fg: c.Ok == "ok" ? new SolidColorBrush(Color.Parse("#2E7D32")) : c.Ok == "warn" ? new SolidColorBrush(Color.Parse("#9A6700")) : Warn));
        _bChot.IsEnabled = !Svc.Locked && Svc.Lines.Count > 0; _bMo.IsEnabled = Svc.Locked;
        _hRows = Svc.Chots.OrderByDescending(c => c.At, StringComparer.Ordinal).ToList();
        _hm.Load(new[] { new GridColumn("v", "Bản", 50), new GridColumn("k", "Thao tác", 80), new GridColumn("at", "Lúc", 125), new GridColumn("by", "Người", 120), new GridColumn("n", "Số mã", 65, "number"), new GridColumn("l", "Còn lệch", 70, "number"), new GridColumn("ly", "Lý do", 320) },
            _hRows.Select(c => new[] { "v" + c.Ver, c.Kind == "chot" ? "Chốt" : "Hủy chốt", KiemKeService.AtText(c.At), c.By, c.Kind == "chot" ? c.NMa.ToString(CultureInfo.InvariantCulture) : "", c.Kind == "chot" ? c.NChenh.ToString(CultureInfo.InvariantCulture) : "", c.LyDo }));
        _hg.Refresh();
        if (_hRows.Count > 0) _hg.Select(0, 0); else ShowBan();
    }
    private void ShowBan()
    {
        var c = _hg.Row >= 0 && _hg.Row < _hRows.Count ? _hRows[_hg.Row] : null;
        if (c is null) { _banInfo.Text = "Chưa có bản chốt."; _bm.Load(Array.Empty<GridColumn>(), Array.Empty<string[]>()); return; }
        var rows = Svc.ChotBan(c.Ver);
        _banInfo.Text = $"Bản chốt v{c.Ver} — {rows.Count:#,0} mã";
        _bm.Load(new[] { new GridColumn("ma", "Mã hàng", 235), new GridColumn("ten", "Tên hàng", 220), new GridColumn("kk", "Kiểm kê (kiện)", 90, "number"), new GridColumn("kkg", "Kiểm kê (kg)", 95, "number"), new GridColumn("t", "Tồn (kiện)", 80, "number"), new GridColumn("tg", "Tồn (kg)", 90, "number"), new GridColumn("kt", "Kế toán (kg)", 95, "number"), new GridColumn("st", "Kết quả", 110) },
            rows.Select(r => new[] { r.Ma, r.Ten, Q(r.KkKien), Kg(r.KkKg), Q(r.TonKien), Kg(r.TonKg), Kg(r.KtKg), r.Status }));
    }
    private async Task DoChot()
    {
        var s = Svc.Summary;
        var why = await Ui.Prompt(this, (s.Open > 0 ? $"Còn {s.Open} mã lệch ({s.KgLech:#,0.##} kg) — bản chốt sẽ ghi rõ “chốt còn chênh”.\n\n" : "") + "Nhập lý do chốt:", "Chốt kỳ kiểm kê");
        if (why is null) return;
        if (why.Trim().Length == 0) { await Ui.Message(this, "Cần nhập lý do chốt."); return; }
        await Act(() => Svc.DoChot(why), "Đã chốt kỳ kiểm kê");
    }
    private async Task MoChot()
    {
        var why = await Ui.Prompt(this, $"Hủy chốt bản v{Svc.CurrentChot?.Ver}? Bản chốt vẫn được giữ trong lịch sử. Nhập lý do hủy chốt:", "Hủy chốt kỳ kiểm kê");
        if (why is null) return;
        if (why.Trim().Length == 0) { await Ui.Message(this, "Cần nhập lý do hủy chốt."); return; }
        await Act(() => Svc.MoChot(why), "Đã hủy chốt");
    }

    private async Task ExportReport()
    {
        var path = await Ui.SaveFile(this, "Xuất báo cáo đối chiếu kiểm kê", Ui.SafeFileName($"Doi chieu kiem ke {DateTime.Today:dd.MM.yyyy}") + ".xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.ExportReport(path))) Ui.OpenExternal(path);
    }
}

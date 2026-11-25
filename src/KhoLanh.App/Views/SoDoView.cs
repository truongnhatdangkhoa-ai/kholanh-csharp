using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.SoDo;
using static KhoLanh.Core.SoDo.SoDoCore;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Sơ đồ kho (C#, nghiệp vụ theo "Sơ đồ kho" của app cũ — không dùng WebView2; dùng chung dữ liệu với app cũ).
/// Tồn lấy từ Tồn kho theo vị trí (chỉ đọc); cấu hình kho, quy cách, khóa kiểm kê lưu ở database so-do-vi-tri.
/// Giao diện mới: sơ đồ trực quan từng dãy (tầng × khoang), bấm ô xem chi tiết; phiếu in vị trí sửa thẳng trên bảng.
/// </summary>
public sealed partial class SoDoView : UserControl, IPage
{
    public string Title => "Sơ đồ kho";
    public string Subtitle => "Sơ đồ từng dãy (tầng × khoang) theo tồn vị trí; pallet theo tầng, quy cách, phiếu in vị trí, kiểm kê và khóa kiểm kê — dùng chung dữ liệu với app cũ.";

    private static SoDoService Svc => App.Services.SoDo;
    private static readonly IBrush Line = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush Warn = AppTheme.Brush("#B23B00");
    private static readonly IBrush GrayBg = AppTheme.Brush("#F1F3F5");
    private static readonly IBrush MoveBg = AppTheme.Brush("#FFF1C9");
    private static readonly IBrush MovedBg = AppTheme.Brush("#E2EFDA");
    private static readonly IBrush NearBg = AppTheme.Brush("#FFF4CC");
    private static readonly IBrush Accent = AppTheme.Brush("#1F8FD1");
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45");

    private readonly bool _edit, _export;
    private bool _loading;
    private string _kho = "TG1", _khu = "ALL";
    private Pos? _sel;

    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _info = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _khoBar = new() { ItemSpacing = 6, LineSpacing = 6 };
    private readonly WrapPanel _khuBar = new() { ItemSpacing = 6, LineSpacing = 6 };
    private readonly CheckBox _rut = new() { Content = "Mã rút gọn", IsChecked = true, FontSize = 12.5 };
    private readonly Border _lockBar = new() { Background = AppTheme.Brush("#FFF4CC"), BorderBrush = AppTheme.Brush("#E8C766"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), Margin = new Thickness(0, 6, 0, 0) };
    private readonly WrapPanel _kpi = new() { ItemSpacing = 6, LineSpacing = 4, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TabControl _tabs;

    // ---------- tab Sơ đồ
    private readonly WarehouseMap _map = new();
    private readonly TextBox _mq = new() { Watermark = "Tìm trên sơ đồ: mã, size, lô, vị trí… (VD: 31/40)", Width = 330 };
    private readonly TextBlock _mapInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _det1 = new() { Spacing = 6 };

    // ---------- tab Danh sách vị trí
    private readonly ComboBox _fSt = new() { MinWidth = 150 }, _fD = new() { MinWidth = 90 }, _fT = new() { MinWidth = 90 }, _fK = new() { MinWidth = 100 };
    private readonly TextBox _fQ = new() { Watermark = "Tìm mã, tên, lô, vị trí…", Width = 260 };
    private readonly TextBlock _listInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly GridModel _lm = new() { ReadOnly = true };
    private readonly SheetGrid _lg;
    private List<Pos> _listRows = new();
    private readonly StackPanel _det2 = new() { Spacing = 6 };
    private static readonly (string Key, string Text)[] StFilters =
        { ("", "Tất cả trạng thái"), ("e", "Trống"), ("u", "Có hàng (1 mã)"), ("mm", "Nhiều mã"), ("ov", "Tầng vượt mức"), ("near", "Gần đầy (≥ 75%)"), ("full", "Đầy / vượt") };

    // ---------- tab Chưa có vị trí
    private readonly GridModel _zm = new() { ReadOnly = true };
    private readonly SheetGrid _zg;
    private readonly TextBlock _zInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 6) };

    // ---------- tab Quy cách
    private readonly ComboBox _qF = new() { MinWidth = 150 }, _qG = new() { MinWidth = 140 };
    private readonly TextBox _qQ = new() { Watermark = "Tìm mã, tên…", Width = 220 };
    private readonly TextBlock _qInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly GridModel _qm = new();
    private readonly SheetGrid _qg;
    private List<(string Ma, string Ten)> _qRows = new();
    private const int QDv = 4, QGk = 5;

    // ---------- tab Phiếu In vị trí
    private readonly ComboBox _kKhu = new() { MinWidth = 90 }, _kDay = new() { MinWidth = 110 }, _kMode = new() { MinWidth = 140 };
    private readonly GridModel _km = new();
    private readonly SheetGrid _kg;
    private readonly TextBlock _kInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly Button _bMerge, _bMove, _bMoveAll, _bUndo;
    private SoDoService.KkSheet? _k;
    private List<(int Pi, SoDoService.KkLine L)> _kRows = new();
    private (string Kho, string Khu, int D, string Mode) _kKey;
    private TabControl? _kSub;                       // 2 tab con: Vị trí có hàng | Vị trí trống (ghi tay)
    private readonly List<Control> _kSoOnly = new(); // nút chỉ dùng ở tab Vị trí có hàng
    private static readonly GridColumn[] KkCols =
    {
        new("d", "Dãy", 44) { ReadOnly = true }, new("vt", "Vị trí", 118), new("ma", "Mã hàng", 230), new("tl", "Trọng lượng", 95),
        new("sl", "Số lượng", 95), new("ghi", "Ghi chú", 210), new("mv", "Đề xuất chuyển", 130) { ReadOnly = true },
    };

    public SoDoView()
    {
        _edit = Svc.CanEdit; _export = Svc.CanExport;
        _lg = new SheetGrid(_lm) { SpareRows = 0, CellBackground = ListBg };
        _lg.SelectionChanged += () => { if (_lg.Row >= 0 && _lg.Row < _listRows.Count) Pick(_listRows[_lg.Row], fromList: true); };
        _zg = new SheetGrid(_zm) { SpareRows = 0 };
        _qg = new SheetGrid(_qm) { SpareRows = 0, CellBackground = QcBg };
        _qm.Changed += () => { if (!_loading) QcInfo(); };
        _kg = new SheetGrid(_km) { SpareRows = 0, CellBackground = KkBg };
        _km.Changed += () => { if (!_loading) KkFromGrid(); };
        _bMerge = Btn("Gộp size", KkMerge, tip: "Trong cùng vị trí: các dòng cùng nhãn size + cùng trọng lượng + cùng đơn vị → cộng thành 1 dòng");
        _bMove = Btn("Chuyển dòng đang chọn", KkMoveOne, tip: "Chuyển nhóm hàng của dòng đang chọn sang ô trống đề xuất (chỉ trên phiếu)");
        _bMoveAll = Btn("Chuyển tất cả đề xuất", KkMoveAll);
        _bUndo = Btn("↶ Hoàn tác", KkUndo);
        _map.Picked += p => Pick(p, fromMap: true);
        _map.CellText = p => KkCellText(p) ?? CellText(p);
        _map.Fill = KkFill;                                           // 1.72: chế độ "Kết quả kiểm kê" tô theo trạng thái ô
        _map.DoubleTapped += (_, _) => { if (_kkMap is not null && _sel is not null) _ = KkOpenEntry(_sel); };
        _map.TangInfo = key => Svc.B.Tang.TryGetValue(key, out var m) ? (m.Nk > 0 ? (int)Math.Round(m.Used * 100.0 / m.Nk) : 0, Lv(m), m.Used, m.Nk) : (0, "", 0, 0);
        _map.IsOverPos = p => IsOver(p, Svc.B);

        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Sơ đồ", Content = BuildMap() },
                new TabItem { Header = "Danh sách vị trí", Content = BuildList() },
                new TabItem { Header = "Chưa có vị trí", Content = BuildZ() },
                new TabItem { Header = "Quy cách", Content = BuildQc() },
                new TabItem { Header = "Phiếu In vị trí", Content = BuildKk() },
            },
        };
        _tabs.SelectionChanged += (_, _) => { if (!_loading && Svc.Loaded) RenderTab(); };
        _rut.IsCheckedChanged += async (_, _) => { if (_loading) return; RenderAll(); await KkRebuildIfClean(); };

        var tools = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        tools.Children.Add(_rut);
        tools.Children.Add(Btn("⟳ Tải lại", async () => await LoadAsync()));
        if (_edit) tools.Children.Add(Btn("🔒 Khóa kiểm kê…", async () => await LockDialog()));
        if (_edit) tools.Children.Add(Btn("⚙ Cấu hình kho…", async () => await ConfigDialog()));
        if (_export) tools.Children.Add(Btn("🖨 In", async () => await PrintList(), tip: "Mở trang in A4 danh sách vị trí đang lọc (In hoặc Lưu PDF)"));
        if (_export) tools.Children.Add(Btn("Xuất Excel", async () => await ExportList(), tip: "Xuất danh sách vị trí đang lọc ra Excel"));

        // 1.50: gọn phần đầu để bảng / sơ đồ có thêm chỗ — Kho + Khu chung 1 dòng (công cụ bên phải), thẻ số 1 dòng thấp
        var row1 = new DockPanel();
        var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        chips.Children.Add(_khoBar);
        chips.Children.Add(new Border { Width = 1, Height = 22, Background = Line, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center });
        chips.Children.Add(_khuBar);
        row1.Children.Add(chips);
        var top = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 0, 0) };
        // dòng đầu: thông tin file (trái) + công cụ (phải) — Kho + Khu có trọn 1 dòng bên dưới
        var infoLeft = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        infoLeft.Children.Add(_info); infoLeft.Children.Add(_status);
        _status.IsVisible = false;
        _status.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) _status.IsVisible = !string.IsNullOrEmpty(_status.Text); };
        var infoRow = new DockPanel(); DockPanel.SetDock(tools, Dock.Right); infoRow.Children.Add(tools); infoRow.Children.Add(infoLeft);
        top.Children.Add(infoRow); top.Children.Add(row1); top.Children.Add(_lockBar); top.Children.Add(_kpi);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top); root.Children.Add(_tabs);
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
    private static string Kg(double v) => v.ToString("#,0.00", Ui.Vi);
    private static Border Framed(Control c) => new() { BorderBrush = Line, BorderThickness = new Thickness(1), Child = c };
    private static string Lv(Tang m) => m.Used > m.Nk ? "over" : m.Used == m.Nk && m.Nk > 0 ? "full" : m.Nk > 0 && m.Used * 100.0 / m.Nk >= 75 ? "near" : m.Used > 0 ? "ok" : "";
    private static string LvText(string lv) => lv switch { "over" => "vượt mức", "full" => "đầy", "near" => "gần đầy", "ok" => "còn chỗ", _ => "trống" };
    private bool Rut => _rut.IsChecked == true;
    private string Ma(string ma, string ten) => Svc.ShowMa(ma, ten, Rut);
    private string Size(Item x) => Svc.SizeParts(x.Ma, x.Ten).Size;
    private static void Combo(ComboBox c, IEnumerable<string> items, int sel = 0) { var l = items.ToList(); c.ItemsSource = l; c.SelectedIndex = l.Count == 0 ? -1 : Math.Clamp(sel, 0, l.Count - 1); }

    private async Task LoadAsync()
    {
        _status.Text = "Đang tải…";
        try { await Task.Run(Svc.Load); }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        _status.Text = "";
        var khos = Svc.KhoList();
        if (!khos.Contains(_kho)) _kho = khos.FirstOrDefault() ?? "TG1";
        if (_khu != "ALL" && !Svc.KhuList(_kho).Contains(_khu)) _khu = "ALL";
        _sel = _sel is null ? null : Svc.B.ByCode.GetValueOrDefault(_sel.Code);
        _k = null;
        KkAfterLoad();
        RenderAll();
    }

    /// <summary>Chạy thao tác ghi; máy khác vừa sửa → tải lại.</summary>
    private async Task<bool> Act(Action a, string? ok = null)
    {
        try
        {
            a();
            if (ok is not null) _status.Text = ok + " (" + DateTime.Now.ToString("HH:mm:ss") + ")";
            _sel = _sel is null ? null : Svc.B.ByCode.GetValueOrDefault(_sel.Code);
            RenderAll();
            return true;
        }
        catch (SheetConflictException)
        {
            await Ui.Message(this, "Dữ liệu Sơ đồ kho vừa được máy khác sửa nên thao tác này CHƯA được lưu (để không ghi đè). Bấm Đóng để tải bản mới nhất rồi làm lại.", "Máy khác vừa sửa");
            await LoadAsync();
        }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); }
        return false;
    }

    private List<Pos> ScopeList() => Svc.Scope(_kho, _khu);

    private void RenderAll()
    {
        _loading = true;
        try
        {
            var live = Svc.Live.Count;
            _info.Text = live == 0
                ? "Chưa có tồn theo vị trí — nạp file ở module “Tồn kho theo vị trí” trước (sơ đồ dưới đây là các ô trống theo cấu hình)."
                : $"Tồn theo vị trí: {(Svc.FileName.Length > 0 ? Svc.FileName : "—")} · Ngày dữ liệu: {(Svc.DataDate.Length > 0 ? Svc.DataDate : "—")} · {live:#,0} dòng"
                  + (Svc.B.Notes.Count > 0 ? " · " + Svc.B.Notes.Count + " ghi chú cấu hình" : "")
                  + (Svc.B.Unparsed.Count > 0 ? $" · {Svc.B.Unparsed.Count} dòng vị trí sai định dạng" : "");
            if (Svc.B.Notes.Count > 0) ToolTip.SetTip(_info, string.Join("\n", Svc.B.Notes));
            RenderKho(); RenderKhu(); RenderLockBar(); RenderKpi();
        }
        finally { _loading = false; }
        RenderTab();
    }
    private void RenderTab()
    {
        switch (_tabs.SelectedIndex)
        {
            case 0: RenderMap(); break;
            case 1: RenderList(); break;
            case 2: RenderZ(); break;
            case 3: RenderQc(); break;
            case 4: _ = KkEnsure(); break;
        }
    }

    private Button Chip(string text, bool active, Action click, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 5), FontSize = 13, CornerRadius = new CornerRadius(14) };
        if (active) { b.Classes.Add("primary"); b.Padding = new Thickness(12, 5); }
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
    private void RenderKho()
    {
        _khoBar.Children.Clear();
        foreach (var kho in Svc.KhoList())
        {
            var c = CountsOf(Svc.Scope(kho, "ALL"), Svc.B);
            var lk = Svc.IsLocked(kho) ? " 🔒" : "";
            _khoBar.Children.Add(Chip($"{KhoLabel(kho)} · {c.Fill}%{lk}", kho == _kho, async () => await SwitchKho(kho), $"{c.All} vị trí, {c.U + c.Mm} có hàng"));
        }
    }
    private void RenderKhu()
    {
        _khuBar.Children.Clear();
        _khuBar.Children.Add(Label("Khu:", true));
        _khuBar.Children.Add(Chip("Tất cả", _khu == "ALL", () => SwitchKhu("ALL")));
        foreach (var k in Svc.KhuList(_kho))
        {
            var c = CountsOf(Svc.Scope(_kho, k), Svc.B);
            _khuBar.Children.Add(Chip($"Khu {k}" + (c.Ov > 0 ? $" ⚠{c.Ov}" : ""), _khu == k, () => SwitchKhu(k), c.Ov > 0 ? $"{c.Ov} tầng vượt mức pallet" : null));
        }
        var z = Svc.Scope(_kho, "Z").Sum(p => p.It.Count);
        if (z > 0) _khuBar.Children.Add(Chip($"Chưa có vị trí ({z})", false, () => _tabs.SelectedIndex = 2));
    }
    private async Task SwitchKho(string kho)
    {
        if (kho == _kho) return;
        if (!await KkCanLeave()) return;
        _kho = kho; _khu = "ALL"; _sel = null; _k = null;
        RenderAll();
    }
    private void SwitchKhu(string khu)
    {
        if (khu == _khu) return;
        _khu = khu; _sel = null;
        RenderAll();
    }

    private void RenderLockBar()
    {
        var locks = Svc.Locks.Where(l => l.Kho == _kho).ToList();
        _lockBar.IsVisible = Svc.Locks.Count > 0;
        if (Svc.Locks.Count == 0) return;
        var p = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
        var diff = Svc.LockDiff.Count;
        p.Children.Add(Label("🔒 Đang khóa kiểm kê: " + string.Join("; ", Svc.Locks.Select(SoDoService.LockName))
            + $" — sơ đồ giữ số liệu lúc khóa. {(diff > 0 ? diff + " thay đổi tồn từ lúc khóa." : "Chưa có thay đổi tồn.")}", fg: AppTheme.Brush("#7A5200")));
        p.Children.Add(Btn("Xem thay đổi", async () => await DiffDialog()));
        if (_edit) p.Children.Add(Btn("Mở khóa…", async () => await LockDialog()));
        _lockBar.Child = p;
        _ = locks;
    }

    private Border Kpi(string label, string value, IBrush? fg = null, string? tip = null)
    {
        // thẻ số 1 dòng (số đậm + nhãn) — thấp hơn thẻ 2 dòng cũ để bảng có thêm chỗ
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        s.Children.Add(new TextBlock { Text = value, FontSize = 14.5, FontWeight = FontWeight.Bold, Foreground = fg ?? Dark, VerticalAlignment = VerticalAlignment.Center });
        s.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        var b = new Border { Background = AppTheme.Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3), Child = s };
        if (tip is not null) ToolTip.SetTip(b, tip);
        return b;
    }
    private void RenderKpi()
    {
        _kpi.Children.Clear();
        if (KkRenderKpi()) return;
        var list = ScopeList(); var c = CountsOf(list, Svc.B);
        var pal = Svc.B.Tang.Where(t => t.Value.Pos[0].Kho == _kho && (_khu == "ALL" || t.Value.Pos[0].Khu == _khu)).Select(t => t.Value).ToList();
        int used = pal.Sum(t => t.Used), cap = pal.Sum(t => t.Nk);
        var miss = MissingQc(list, Svc.QcOf).Count;
        _kpi.Children.Add(Kpi("Tổng vị trí", c.All.ToString("#,0", Ui.Vi)));
        _kpi.Children.Add(Kpi("Ô trống", c.E.ToString("#,0", Ui.Vi), AppTheme.Brush("#5D6B78")));
        _kpi.Children.Add(Kpi("Có hàng (1 mã)", c.U.ToString("#,0", Ui.Vi), AppTheme.Brush("#2E7D32")));
        _kpi.Children.Add(Kpi("Nhiều mã", c.Mm.ToString("#,0", Ui.Vi), AppTheme.Brush("#B26A00")));
        _kpi.Children.Add(Kpi("Tầng vượt mức", c.Ov.ToString("#,0", Ui.Vi), c.Ov > 0 ? AppTheme.Brush("#C0392B") : null, "Tầng có số pallet lớn hơn số khoang"));
        _kpi.Children.Add(Kpi("Lấp đầy ô", c.Fill + "%", Accent, "Số ô có hàng / tổng số ô"));
        _kpi.Children.Add(Kpi("Pallet / sức chứa", $"{used:#,0} / {cap:#,0}", null, "Pallet = mỗi nhóm cùng loại + cùng size trong 1 ô; sức chứa = số khoang × số tầng"));
        _kpi.Children.Add(Kpi("Mã thiếu quy cách", miss.ToString("#,0", Ui.Vi), miss > 0 ? Warn : null, "Mã có hàng nhưng chưa khai kiện / gói — xem tab Quy cách"));
    }

    private void Pick(Pos p, bool fromMap = false, bool fromList = false)
    {
        _sel = p;
        if (!fromMap) _map.Select(p);
        FillDetail(_det1, p); FillDetail(_det2, p);
        if (!fromList) { var i = _listRows.IndexOf(p); if (i >= 0 && _lg.Row != i) _lg.Select(i, 0); }
    }
    /// <summary>Dùng cho công cụ chụp ảnh giao diện.</summary>
    public void PickForDemo(Pos p) { _kho = p.Kho; _khu = p.Khu; _sel = p; RenderAll(); Pick(p); }
    private void PickCode(string code)
    {
        if (Svc.B.ByCode.GetValueOrDefault(code) is not { } p) return;
        if (p.Kho != _kho || (_khu != "ALL" && _khu != p.Khu)) { _kho = p.Kho; _khu = p.Khu; _sel = p; RenderAll(); }
        Pick(p);
    }

    // =====================================================================================
    // Chi tiết vị trí (dùng chung cho Sơ đồ + Danh sách)
    // =====================================================================================

    private void FillDetail(StackPanel host, Pos? p)
    {
        host.Children.Clear();
        if (p is null)
        {
            host.Children.Add(Label("Chi tiết vị trí", true, 15));
            host.Children.Add(Muted("Bấm vào một ô trên sơ đồ (hoặc một dòng trong danh sách) để xem hàng trong ô, số pallet của tầng và gợi ý chuyển khi tầng vượt mức."));
            return;
        }
        var st = Status(p);
        host.Children.Add(new TextBlock { Text = p.Code, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Dark });
        host.Children.Add(Muted(p.Z ? $"{KhoLabel(p.Kho)} · Chưa có vị trí" : $"{KhoLabel(p.Kho)} · Khu {p.Khu} · Dãy {Two(p.D)} · Tầng {p.T} · Khoang {p.K}"));
        var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        chips.Children.Add(Pill(StatusLabel(st), st == "e" ? WarehouseMap.EmptyBg : st == "u" ? WarehouseMap.OneBg : WarehouseMap.ManyBg));
        if (Svc.PosLock(p) is { } lk) chips.Children.Add(Pill("🔒 khóa kiểm kê " + lk.Ts.Replace('T', ' '), GrayBg));
        host.Children.Add(chips);
        KkDetail(host, p);

        if (!p.Z && Svc.B.Tang.TryGetValue(p.TangKey, out var m))
        {
            var lv = Lv(m);
            var pct = m.Nk > 0 ? (int)Math.Round(m.Used * 100.0 / m.Nk) : 0;
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            t.Inlines!.Add(new Avalonia.Controls.Documents.Run($"Tầng {p.T} của dãy {Two(p.D)}: "));
            t.Inlines.Add(new Avalonia.Controls.Documents.Run($"{m.Used}/{m.Nk} pallet ({pct}%) — {LvText(lv)}") { FontWeight = FontWeight.SemiBold, Foreground = WarehouseMap.LevelBrush(lv == "ok" ? "" : lv) is var br && lv is "over" or "full" ? br : Dark });
            host.Children.Add(t);
            var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, m.Nk), Value = Math.Min(m.Used, m.Nk), Height = 8, Foreground = WarehouseMap.LevelBrush(lv) };
            host.Children.Add(bar);
        }

        if (p.It.Count == 0) host.Children.Add(Muted("Ô trống."));
        foreach (var x in p.It)
        {
            var s = new StackPanel { Spacing = 1 };
            s.Children.Add(new SelectableTextBlock { Text = Ma(x.Ma, x.Ten), FontWeight = FontWeight.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap });
            if (Rut && Ma(x.Ma, x.Ten) != x.Ma) s.Children.Add(new SelectableTextBlock { Text = x.Ma, FontSize = 11.5, Foreground = AppTheme.Muted });
            s.Children.Add(Muted(x.Ten));
            s.Children.Add(Label($"Lô {(x.Lo.Length > 0 ? x.Lo : "—")} · {Q(x.Sl)} {x.Dv} · T.Lượng {Q(x.Tl)} · Net {Kg(x.Net)} kg"));
            host.Children.Add(new Border { BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Child = s });
        }
        if (p.It.Count > 0)
            host.Children.Add(Label("Tổng: " + string.Join(" + ", p.Tot.Select(t => Q(t.Sl) + " " + t.Dv)) + $" · Net {Kg(p.Net)} kg" + (p.Z ? "" : $" · {p.Pal} pallet"), true));
        if (p.Grps.Count > 1) host.Children.Add(Muted("Nhóm pallet trong ô: " + string.Join(", ", p.Grps.Select(g => GrpText(g.G) + (g.N > 1 ? $" ({g.N} mã)" : "")))));

        // quy cách các mã trong ô
        var mas = p.It.GroupBy(x => x.Ma).Select(g => g.First()).ToList();
        if (mas.Count > 0)
        {
            host.Children.Add(Label("Quy cách", true, 13.5));
            foreach (var x in mas) host.Children.Add(QcEditor(x));
        }

        if (!p.Z && IsOver(p, Svc.B))
        {
            host.Children.Add(Label("Tầng vượt mức — gợi ý chuyển bớt sang:", true, 13.5, Warn));
            var sug = Suggest(p, Svc.B);
            if (sug.Count == 0) host.Children.Add(Muted("Không còn tầng nào cùng khu có chỗ trống."));
            foreach (var s in sug)
            {
                var b = Btn($"→ {s.Code}  (tầng còn {s.Free} chỗ{(s.Same ? ", đã có cùng mã" : "")})", () => PickCode(s.Code));
                b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left;
                host.Children.Add(b);
            }
        }
        if (!p.Z && Svc.B.Tang.TryGetValue(p.TangKey, out var mt) && mt.Pos.Count > 1)
        {
            host.Children.Add(Label("Các ô cùng tầng", true, 13.5));
            var w = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
            foreach (var o in mt.Pos.OrderBy(o => o.K))
            {
                var b = Chip($"K{o.K}: {(o.It.Count == 0 ? "trống" : o.Pal + " pallet")}", ReferenceEquals(o, p), () => Pick(o));
                b.FontSize = 11.5; b.Padding = new Thickness(8, 3);
                w.Children.Add(b);
            }
            host.Children.Add(w);
        }
    }
    private static string GrpText(string g) => g.StartsWith("?|", StringComparison.Ordinal) ? g[2..] : g.Contains('|') ? SoDoService.GrpName(g) : g;
    private static Border Pill(string text, IBrush bg) => new() { Background = bg, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2), Child = new TextBlock { Text = text, FontSize = 11.5 } };

    private Control QcEditor(Item x)
    {
        var q = Svc.QcOf(x.Ma); var g = GoiY(x.Ten);
        var row = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        row.Children.Add(new TextBlock { Text = Ma(x.Ma, x.Ten), FontSize = 12, Width = 150, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        if (!_edit)
        {
            row.Children.Add(Label(HasQc(q) ? (q!.Dv == "gói" ? $"gói · {q.Gk} gói/kiện" : "kiện") : "chưa khai", fg: HasQc(q) ? null : Warn));
            return row;
        }
        var dv = new ComboBox { ItemsSource = new[] { "kiện", "gói" }, SelectedIndex = q?.Dv == "gói" ? 1 : 0, FontSize = 12, MinWidth = 70 };
        var gk = new TextBox { Text = q is { Gk: > 0 } ? q.Gk.ToString(CultureInfo.InvariantCulture) : g is { } gy && q?.Dv == "gói" ? gy.Gk.ToString(CultureInfo.InvariantCulture) : "", Watermark = "gói/kiện", Width = 64, FontSize = 12 };
        gk.IsEnabled = dv.SelectedIndex == 1;
        dv.SelectionChanged += (_, _) => { gk.IsEnabled = dv.SelectedIndex == 1; if (gk.IsEnabled && string.IsNullOrWhiteSpace(gk.Text) && g is { } gy2) gk.Text = gy2.Gk.ToString(CultureInfo.InvariantCulture); };
        row.Children.Add(dv); row.Children.Add(gk);
        row.Children.Add(Btn("Lưu", async () =>
        {
            int.TryParse(gk.Text?.Trim(), out var n);
            await Act(() => Svc.SaveQc(new[] { (x.Ma, dv.SelectedIndex == 1 ? "gói" : "kiện", n) }), "Đã lưu quy cách " + KeyMa(x.Ma));
        }));
        if (!HasQc(q)) row.Children.Add(Label("chưa lưu", fg: Warn, size: 11.5));
        if (g is { } gg) row.Children.Add(Label("theo tên: " + gg.Text, size: 11.5, fg: AppTheme.Muted));
        return row;
    }

    // =====================================================================================
    // Tab Sơ đồ
    // =====================================================================================

    private Control BuildMap()
    {
        Panel bar = new ToolRow { Margin = new Thickness(0, 8, 0, 6) };
        bar.Children.Add(_mq);
        _mq.TextChanged += (_, _) => { if (!_loading) RenderMap(); };
        void Legend(string text, IBrush bg) { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }; s.Children.Add(new Border { Width = 16, Height = 12, Background = bg, BorderBrush = Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }); s.Children.Add(Label(text, size: 12)); bar.Children.Add(s); }
        bar.Children.Add(KkBar());
        bar.Children.Add(_legendKk);
        var std = bar; bar = _legendStd; std.Children.Add(_legendStd);
        Legend("Trống", WarehouseMap.EmptyBg); Legend("Có hàng", WarehouseMap.OneBg); Legend("Nhiều mã", WarehouseMap.ManyBg); Legend("Tầng vượt mức", WarehouseMap.OverBg);
        Legend("Cột mức đầy tầng: xanh còn chỗ · vàng gần đầy · cam đầy · đỏ vượt", new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(AppTheme.Color("#4CAF50"), 0), new GradientStop(AppTheme.Color("#F1C40F"), 0.4), new GradientStop(AppTheme.Color("#E67E22"), 0.7), new GradientStop(AppTheme.Color("#C0392B"), 1) },
        });
        bar = std;
        bar.Children.Add(_mapInfo);
        var sv = new ScrollViewer { Content = _map, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var left = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        left.Children.Add(bar); left.Children.Add(Framed(sv));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,360") };
        grid.Children.Add(left);
        var det = new Border { Child = new ScrollViewer { Content = _det1, Padding = new Thickness(0, 0, 8, 0) }, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetColumn(det, 2); grid.Children.Add(det);
        return grid;
    }
    private string CellText(Pos p)
    {
        if (p.It.Count == 0) return "";
        var mas = p.It.Select(x => x.Ma).Distinct().Count();
        if (mas > 1) return $"{mas} mã";
        var x = p.It[0]; var s = Size(x);
        return s.Length > 0 ? s : x.Ma.Length > 6 ? "…" + x.Ma[^6..] : x.Ma;
    }
    private void RenderMap()
    {
        var q = (_mq.Text ?? "").Trim();
        var sections = new List<WarehouseMap.Section>();
        var khus = _khu == "ALL" ? Svc.B.Cfg.GetValueOrDefault(_kho)?.Select(e => e.Khu).ToList() ?? new() : new List<string> { _khu };
        var hits = 0;
        foreach (var khu in khus)
        {
            var e = Svc.B.Cfg[_kho].FirstOrDefault(x => x.Khu == khu); if (e is null) continue;
            var blocks = new List<WarehouseMap.Block>();
            for (var d = 1; d <= e.D; d++)
            {
                var cells = new Dictionary<(int, int), Pos>();
                var nt = TangOf(e, d);
                for (var t = 1; t <= nt; t++) for (var k = 1; k <= e.K; k++) if (Svc.B.ByCode.GetValueOrDefault(CodeOf(_kho, khu, d, t, k)) is { } p) cells[(t, k)] = p;
                if (cells.Count == 0) continue;
                blocks.Add(new WarehouseMap.Block(khu, d, nt, e.K, Svc.Locks.Any(l => l.Kho == _kho && l.Khu == khu && (l.D == 0 || l.D == d)), cells));
            }
            var c = CountsOf(blocks.SelectMany(b => b.Cells.Values), Svc.B);
            sections.Add(new WarehouseMap.Section($"Khu {khu}  ·  {e.D} dãy × {e.K} khoang × {(e.Td.Count > 0 ? "≤" + Math.Max(e.T ?? NT, e.Td.Values.Max()) : (e.T ?? NT).ToString(CultureInfo.InvariantCulture))} tầng  ·  {c.U + c.Mm}/{c.All} ô có hàng" + (c.Ov > 0 ? $"  ·  ⚠ {c.Ov} tầng vượt mức" : ""), blocks));
        }
        if (q.Length > 0)
        {
            var lq = q.ToLowerInvariant();
            bool Hit(Pos p) => p.It.Count > 0 && Pass(p, new Filter { Q = lq }, Svc.B, x => TextOf(x, Svc.Label));
            _map.Hit = Hit;
            hits = sections.SelectMany(s => s.Blocks).SelectMany(b => b.Cells.Values).Count(Hit);
            _mapInfo.Text = $"{hits} ô khớp";
        }
        else { _map.Hit = null; _mapInfo.Text = "Bấm 1 ô để xem chi tiết"; }
        if (_sel is not null && (_sel.Kho != _kho || (_khu != "ALL" && _sel.Khu != _khu))) _sel = null;
        _map.SetData(sections, _sel);
        FillDetail(_det1, _sel);
    }

    // =====================================================================================
    // Tab Danh sách vị trí
    // =====================================================================================

    private Control BuildList()
    {
        var bar = new ToolRow { Margin = new Thickness(0, 8, 0, 6) };
        Combo(_fSt, StFilters.Select(f => f.Text));
        bar.Children.Add(_fSt); bar.Children.Add(_fD); bar.Children.Add(_fT); bar.Children.Add(_fK); bar.Children.Add(_fQ); bar.Children.Add(_listInfo);
        foreach (var c in new[] { _fSt, _fD, _fT, _fK }) c.SelectionChanged += (_, _) => { if (!_loading) RenderList(); };
        _fQ.TextChanged += (_, _) => { if (!_loading) RenderList(); };
        var left = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        left.Children.Add(bar); left.Children.Add(Framed(_lg));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,360") };
        grid.Children.Add(left);
        var det = new Border { Child = new ScrollViewer { Content = _det2, Padding = new Thickness(0, 0, 8, 0) }, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetColumn(det, 2); grid.Children.Add(det);
        return grid;
    }
    private static int PickNum(ComboBox c) => c.SelectedIndex <= 0 ? 0 : int.TryParse(Regex(c.SelectedItem as string), out var n) ? n : 0;
    private static string Regex(string? s) => System.Text.RegularExpressions.Regex.Match(s ?? "", @"\d+").Value;
    private Filter CurFilter() => new() { St = StFilters[Math.Max(0, _fSt.SelectedIndex)].Key, D = PickNum(_fD), T = PickNum(_fT), K = PickNum(_fK), Q = (_fQ.Text ?? "").Trim() };
    private List<Pos> FilteredList() { var f = CurFilter(); return ScopeList().Where(p => Pass(p, f, Svc.B, x => TextOf(x, Svc.Label))).ToList(); }

    private void RenderList()
    {
        _loading = true;
        try
        {
            var es = _khu == "ALL" ? Svc.B.Cfg.GetValueOrDefault(_kho) ?? new() : (Svc.B.Cfg.GetValueOrDefault(_kho) ?? new()).Where(e => e.Khu == _khu).ToList();
            int maxD = es.Count > 0 ? es.Max(e => e.D) : 0, maxK = es.Count > 0 ? es.Max(e => e.K) : 0, maxT = es.Count > 0 ? es.Max(e => Enumerable.Range(1, Math.Max(1, e.D)).Max(d => TangOf(e, d))) : 0;
            void Keep(ComboBox c, string all, string unit, int n) { var cur = c.SelectedIndex; Combo(c, new[] { all }.Concat(Enumerable.Range(1, n).Select(i => unit + " " + (unit == "Dãy" ? Two(i) : i.ToString(CultureInfo.InvariantCulture)))), cur < 0 ? 0 : cur <= n ? cur : 0); }
            Keep(_fD, "Tất cả dãy", "Dãy", maxD); Keep(_fT, "Tất cả tầng", "Tầng", maxT); Keep(_fK, "Tất cả khoang", "Khoang", maxK);
        }
        finally { _loading = false; }
        _listRows = FilteredList();
        var rows = _listRows.Select(p =>
        {
            var tg = Svc.B.Tang.GetValueOrDefault(p.TangKey); var (pct, lv) = FillLevel(p, Svc.B);
            return new[]
            {
                p.Code, StatusLabel(Status(p)), string.Join(" · ", p.It.Select(x => Ma(x.Ma, x.Ten))), string.Join(" · ", p.It.Select(x => x.Lo).Where(l => l.Length > 0).Distinct()),
                string.Join(" + ", p.Tot.Select(t => Q(t.Sl) + " " + t.Dv)), p.It.Count > 0 ? Kg(p.Net) : "", tg is null ? "" : $"{tg.Used}/{tg.Nk}", lv.Length > 0 ? pct + "% " + LvText(lv) : "",
            };
        }).ToList();
        _lm.Load(new[]
        {
            new GridColumn("vt", "Vị trí", 115), new GridColumn("st", "Trạng thái", 80), new GridColumn("ma", "Mã hàng", 260), new GridColumn("lo", "Lô", 120),
            new GridColumn("sl", "Số lượng", 110), new GridColumn("net", "Net (kg)", 85, "number"), new GridColumn("pal", "Pallet tầng", 80), new GridColumn("lv", "Mức đầy tầng", 110),
        }, rows);
        _lg.Refresh();
        var c = CountsOf(_listRows, Svc.B);
        _listInfo.Text = $"{_listRows.Count:#,0} vị trí · {c.U + c.Mm} có hàng · Net {Kg(_listRows.Sum(p => p.Net))} kg";
        var i = _sel is null ? -1 : _listRows.IndexOf(_sel);
        if (i >= 0) _lg.Select(i, 0);
        FillDetail(_det2, _sel);
    }
    private IBrush? ListBg(int r, int c)
    {
        if (r < 0 || r >= _listRows.Count) return null;
        var p = _listRows[r];
        if (c == 1) return Status(p) switch { "u" => WarehouseMap.OneBg, "mm" => WarehouseMap.ManyBg, _ => null };
        if (c >= 6 && IsOver(p, Svc.B)) return WarehouseMap.OverBg;
        if (c == 7 && FillLevel(p, Svc.B).Lv is "near" or "full") return NearBg;
        return null;
    }
    private async Task PrintList()
    {
        var list = _tabs.SelectedIndex == 1 ? FilteredList() : ScopeList();
        if (list.Count == 0) { await Ui.Message(this, "Không có vị trí nào để in."); return; }
        await Ui.PrintVia(this, $"Danh sách vị trí {KhoLabel(_kho)}{(_khu == "ALL" ? "" : " khu " + _khu)} {DateTime.Today:dd/MM/yyyy}", tmp => Svc.ExportList(tmp, list));
    }

    private async Task ExportList()
    {
        var list = _tabs.SelectedIndex == 1 ? FilteredList() : ScopeList();
        var name = $"So do kho {KhoLabel(_kho)}{(_khu == "ALL" ? "" : " khu " + _khu)} {DateTime.Today:dd.MM.yyyy}";
        var path = await Ui.SaveFile(this, "Xuất danh sách vị trí ra Excel", Ui.SafeFileName(name) + ".xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.ExportList(path, list))) Ui.OpenExternal(path);
    }

    // =====================================================================================
    // Tab Chưa có vị trí
    // =====================================================================================

    private Control BuildZ()
    {
        var p = new DockPanel();
        DockPanel.SetDock(_zInfo, Dock.Top);
        p.Children.Add(_zInfo); p.Children.Add(Framed(_zg));
        return p;
    }
    private void RenderZ()
    {
        var rows = new List<string[]>();
        foreach (var p in Svc.Scope(_kho, "Z"))
            foreach (var x in p.It) rows.Add(new[] { p.Code, Ma(x.Ma, x.Ten), x.Ten, x.Lo, Q(x.Sl) + " " + x.Dv, Kg(x.Net), "Khu Z — chưa xếp vị trí" });
        foreach (var r in Svc.B.Unparsed) rows.Add(new[] { r.Vt, Ma(r.Ma, r.Ten), r.Ten, r.Lo, Q(r.Sl), Kg(R2(r.Sl * r.Tl)), "Vị trí không đúng dạng Kho.Khu.Dãy.Tầng.Khoang" });
        _zm.Load(new[]
        {
            new GridColumn("vt", "Vị trí", 120), new GridColumn("ma", "Mã hàng", 210), new GridColumn("ten", "Tên hàng", 300), new GridColumn("lo", "Lô", 100),
            new GridColumn("sl", "Số lượng", 90), new GridColumn("net", "Net (kg)", 85, "number"), new GridColumn("gc", "Ghi chú", 280),
        }, rows);
        _zg.Refresh();
        var z = Svc.Scope(_kho, "Z").Sum(p => p.It.Count);
        _zInfo.Text = z + Svc.B.Unparsed.Count == 0
            ? $"{KhoLabel(_kho)}: không có hàng chưa có vị trí."
            : $"{KhoLabel(_kho)}: {z} dòng hàng ở khu Z (chưa xếp vị trí)" + (Svc.B.Unparsed.Count > 0 ? $" · {Svc.B.Unparsed.Count} dòng (mọi kho) có vị trí sai định dạng." : ".")
              + " Các dòng này không tính vào sơ đồ và pallet.";
    }

    // =====================================================================================
    // Tab Quy cách
    // =====================================================================================

    private Control BuildQc()
    {
        var bar = new ToolRow { Margin = new Thickness(0, 8, 0, 4) };
        Combo(_qF, new[] { "Tất cả mã", "Chưa có quy cách", "Đã có quy cách" }, 1);
        bar.Children.Add(_qF); bar.Children.Add(_qG); bar.Children.Add(_qQ); bar.Children.Add(_qInfo);
        _qF.SelectionChanged += (_, _) => { if (!_loading) RenderQc(); };
        _qG.SelectionChanged += (_, _) => { if (!_loading) RenderQc(); };
        _qQ.TextChanged += (_, _) => { if (!_loading) RenderQc(); };
        var btns = new ToolRow { Margin = new Thickness(0, 0, 0, 6) };
        if (_edit)
        {
            btns.Children.Add(Btn("💾 Lưu quy cách", async () => await QcSave(), primary: true));
            btns.Children.Add(Btn("Điền theo tên (dòng đang chọn)", () => QcFill(goiY: true), tip: "Tên có “(1.8 kg x 6 gói)” → đơn vị gói, 6 gói/kiện"));
            btns.Children.Add(Btn("Điền = kiện (dòng đang chọn)", () => QcFill(goiY: false)));
            btns.Children.Add(Btn("Điền theo tên cho mọi dòng chưa có", () => QcFill(goiY: true, all: true)));
        }
        var hint = Muted(_edit
            ? "Gõ thẳng vào cột “Đơn vị ✎” (kiện / gói) và “Gói/kiện ✎” rồi bấm Lưu. Chọn nhiều dòng (kéo chuột / Shift) để điền hàng loạt. Quy cách dùng để quy số lượng ra kiện và hiện đơn vị trên sơ đồ, phiếu."
            : "Chỉ xem: tài khoản không có quyền sửa quy cách.");
        var head = new StackPanel(); head.Children.Add(bar); head.Children.Add(btns); head.Children.Add(hint);
        hint.Margin = new Thickness(0, 0, 0, 6);
        var p = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        p.Children.Add(head); p.Children.Add(Framed(_qg));
        return p;
    }
    private void RenderQc()
    {
        var all = Svc.B.Pos.SelectMany(p => p.It).GroupBy(x => x.Ma).Select(g => (g.Key, g.First().Ten, N: g.Count())).OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
        _loading = true;
        try
        {
            var groups = all.Select(x => Svc.GrpOf(x.Key, x.Ten)).Where(g => g.Length > 0).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();
            var cur = _qG.SelectedItem as string;
            var items = new[] { "Tất cả nhóm / size" }.Concat(groups.Select(GrpText)).ToList();
            Combo(_qG, items, Math.Max(0, cur is null ? 0 : items.IndexOf(cur)));
        }
        finally { _loading = false; }
        var fg = _qG.SelectedIndex > 0 ? _qG.SelectedItem as string : null; var qq = (_qQ.Text ?? "").Trim().ToLowerInvariant();
        var rows = all.Where(x =>
        {
            var has = HasQc(Svc.QcOf(x.Key));
            if (_qF.SelectedIndex == 1 && has) return false;
            if (_qF.SelectedIndex == 2 && !has) return false;
            if (fg is not null && GrpText(Svc.GrpOf(x.Key, x.Ten)) != fg) return false;
            return qq.Length == 0 || (x.Key + " " + x.Ten + " " + Svc.Label(x.Key, x.Ten)).ToLowerInvariant().Contains(qq);
        }).ToList();
        _qRows = rows.Select(x => (x.Key, x.Ten)).ToList();
        var data = rows.Select(x =>
        {
            var q = Svc.QcOf(x.Key);
            return new[]
            {
                x.Key, Svc.Label(x.Key, x.Ten), x.Ten, GoiY(x.Ten)?.Text ?? "", q is { Saved: true } ? q.Dv : "", q is { Saved: true, Gk: > 0 } ? q.Gk.ToString(CultureInfo.InvariantCulture) : "",
                x.N.ToString(CultureInfo.InvariantCulture), HasQc(q) ? "Đã lưu " + Ts(q!.Ts) : "Chưa có",
            };
        }).ToList();
        _loading = true;
        _qm.Load(new[]
        {
            new GridColumn("ma", "Mã hàng", 190) { ReadOnly = true }, new GridColumn("rg", "Mã rút gọn", 170) { ReadOnly = true }, new GridColumn("ten", "Tên hàng", 300) { ReadOnly = true },
            new GridColumn("gy", "Gợi ý theo tên", 105) { ReadOnly = true }, new GridColumn("dv", "Đơn vị ✎", 75) { ReadOnly = !_edit }, new GridColumn("gk", "Gói/kiện ✎", 80, "number") { ReadOnly = !_edit },
            new GridColumn("n", "Số dòng tồn", 85, "number") { ReadOnly = true }, new GridColumn("st", "Trạng thái", 140) { ReadOnly = true },
        }, data);
        _qm.ReadOnly = !_edit;
        _loading = false;
        _qg.Refresh();
        QcInfo();
    }
    private static string Ts(string ts) => DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime().ToString("dd/MM/yyyy", Ui.Vi) : "";
    private static string NormDv(string s) => s.Trim().ToLowerInvariant() switch { "gói" or "goi" or "g" => "gói", "kiện" or "kien" or "k" or "thùng" or "thung" => "kiện", "" => "", var x => "?" + x };
    private List<(string Ma, string Dv, int Gk)> QcChanges(out List<string> bad)
    {
        var o = new List<(string, string, int)>(); bad = new();
        for (var r = 0; r < _qRows.Count && r < _qm.RowCount; r++)
        {
            var ma = _qRows[r].Ma; var dv = NormDv(_qm.Get(r, QDv)); var gkS = _qm.Get(r, QGk).Trim();
            var q = Svc.QcOf(ma);
            if (dv.Length == 0 && gkS.Length == 0) continue;
            if (dv.StartsWith('?')) { bad.Add($"{ma}: đơn vị “{dv[1..]}” (chỉ kiện hoặc gói)"); continue; }
            if (dv.Length == 0) dv = gkS.Length > 0 ? "gói" : "kiện";
            var gk = 0;
            if (gkS.Length > 0)
            {
                if (!Ui.TryParseNumber(gkS, out var g) || g < 1 || g != Math.Floor(g)) { bad.Add($"{ma}: gói/kiện “{gkS}” không phải số nguyên ≥ 1"); continue; }
                gk = (int)g;
            }
            if (dv == "gói" && gk < 1) { bad.Add($"{ma}: tính theo gói thì phải có số gói/kiện"); continue; }
            if (q is { Saved: true } && q.Dv == dv && (dv == "kiện" || q.Gk == gk)) continue;
            o.Add((ma, dv, dv == "gói" ? gk : 0));
        }
        return o;
    }
    private void QcInfo()
    {
        var ch = _edit ? QcChanges(out var bad).Count : 0;
        var miss = _qRows.Count(x => !HasQc(Svc.QcOf(x.Ma)));
        _qInfo.Text = $"{_qRows.Count:#,0} mã · {miss} chưa có quy cách" + (ch > 0 ? $" · {ch} mã đang sửa (chưa lưu)" : "");
    }
    private void QcFill(bool goiY, bool all = false)
    {
        var sel = _qg.Selection;
        var cells = new List<(int, int, string)>();
        var range = all ? Enumerable.Range(0, _qRows.Count).Where(r => !HasQc(Svc.QcOf(_qRows[r].Ma))) : Enumerable.Range(sel.R1, Math.Max(0, Math.Min(sel.R2, _qRows.Count - 1) - sel.R1 + 1));
        var n = 0;
        foreach (var r in range)
        {
            if (goiY) { if (GoiY(_qRows[r].Ten) is not { } g) continue; cells.Add((r, QDv, "gói")); cells.Add((r, QGk, g.Gk.ToString(CultureInfo.InvariantCulture))); }
            else { cells.Add((r, QDv, "kiện")); cells.Add((r, QGk, "")); }
            n++;
        }
        if (cells.Count > 0) _qm.SetCells(cells);
        _qg.Refresh();
        _status.Text = n > 0 ? $"Đã điền {n} dòng — bấm “Lưu quy cách” để lưu." : goiY ? "Các dòng đã chọn không có “(… kg x … gói)” trong tên." : "";
    }
    private async Task QcSave()
    {
        var ch = QcChanges(out var bad);
        if (bad.Count > 0) { await Ui.Message(this, "Chưa lưu được vì có dòng sai:\n\n" + string.Join("\n", bad.Take(12)) + (bad.Count > 12 ? $"\n… và {bad.Count - 12} dòng nữa" : ""), "Kiểm tra quy cách"); return; }
        if (ch.Count == 0) { _status.Text = "Không có thay đổi quy cách."; return; }
        await Act(() => Svc.SaveQc(ch), $"Đã lưu quy cách {ch.Count} mã");
    }
    private IBrush? QcBg(int r, int c)
    {
        if (r < 0 || r >= _qRows.Count) return null;
        var q = Svc.QcOf(_qRows[r].Ma);
        if (c is QDv or QGk)
        {
            var dv = NormDv(_qm.Get(r, QDv)); var gk = _qm.Get(r, QGk).Trim();
            var changed = q is { Saved: true } ? dv != q.Dv || (dv == "gói" && gk != q.Gk.ToString(CultureInfo.InvariantCulture)) : dv.Length + gk.Length > 0;
            if (dv.StartsWith('?') || (dv == "gói" && gk.Length == 0)) return WarehouseMap.OverBg;
            if (changed) return MoveBg;
        }
        if (c == 7) return HasQc(q) ? MovedBg : NearBg;
        return null;
    }

    // =====================================================================================
    // Tab Phiếu In vị trí (kiểm kê)
    // =====================================================================================

    private Control BuildKk()
    {
        var bar = new ToolRow { Margin = new Thickness(0, 6, 0, 4) };
        // 1.71: tab "Vị trí trống (ghi tay)" = phiếu trắng MỌI vị trí theo sơ đồ (bỏ lựa chọn "Chỉ vị trí trống" — người dùng chốt).
        Combo(_kMode, new[] { "Mọi vị trí (phiếu trắng)" });
        _kMode.IsVisible = false;
        bar.Children.Add(Label("Khu")); bar.Children.Add(_kKhu); bar.Children.Add(Label("Dãy")); bar.Children.Add(_kDay);
        bar.Children.Add(Btn("Tạo lại phiếu", async () => { if (await KkCanLeave()) { _k = null; await KkEnsure(); } }));
        foreach (var c in new[] { _kKhu, _kDay, _kMode })
            c.SelectionChanged += async (_, _) => { if (!_loading) await KkSelChanged(); };
        var b2 = bar;   // 1.50: chung 1 thanh (tự xuống dòng khi hẹp) thay vì 2 dòng + dòng ghi chú
        b2.Children.Add(_bMerge); b2.Children.Add(_bMove); b2.Children.Add(_bMoveAll); b2.Children.Add(_bUndo);
        _kSoOnly.AddRange(new Control[] { _bMerge, _bMove, _bMoveAll });
        b2.Children.Add(Btn("+ Thêm dòng", KkAddRow, tip: "Thêm 1 dòng cùng vị trí ngay dưới dòng đang chọn"));
        b2.Children.Add(Btn("− Xóa dòng", KkDelRows, danger: true));
        if (_export) b2.Children.Add(Btn("🖨 In phiếu (A4)", async () => await KkPrint(), primary: true, tip: "Mỗi dãy 1 trang A4 — mở trong trình duyệt rồi bấm In hoặc Lưu PDF"));
        if (_export) b2.Children.Add(Btn("Xuất Excel (A4)", async () => await KkExport(), primary: true, tip: "Mỗi dãy 1 sheet, Times New Roman 15, khổ A4 đứng — mở Excel để in"));
        var nl = Btn("Chuyển sang Nhập liệu", async () => await KkToNhapLieu(), tip: "Tạo trang Nhập liệu (mẫu Kiểm kê) cho 1 dãy, số lượng điền sẵn theo sổ để nhập số thực");
        var rs = Btn("Rà soát chênh lệch", async () => await KkReview(), tip: "So trang Nhập liệu kiểm kê mới nhất của dãy với sổ sách");
        b2.Children.Add(nl); b2.Children.Add(rs); _kSoOnly.Add(nl); _kSoOnly.Add(rs);
        const string hint = "Vị trí có hàng: phiếu theo sổ sách (không in ô trống), sửa thẳng trên bảng (chỉ trên phiếu, không đổi tồn); dòng vàng có đề xuất chuyển (ô lẫn nhiều size) sang ô trống cùng tầng, dòng xanh là dòng đã chuyển. Vị trí trống (ghi tay): phiếu trắng mọi vị trí theo sơ đồ kho để ghi tay.";
        ToolTip.SetTip(_kInfo, hint);
        var head = new StackPanel(); head.Children.Add(bar); head.Children.Add(_kInfo);
        _kInfo.Margin = new Thickness(0, 0, 0, 4);
        var p = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        p.Children.Add(head); p.Children.Add(Framed(_kg));
        // 2 tab con dùng chung 1 bảng phiếu: đổi tab = đổi loại phiếu (bảng chuyển sang tab đang mở).
        var t1 = new Border { Padding = new Thickness(0, 4, 0, 0) }; var t2 = new Border { Padding = new Thickness(0, 4, 0, 0) };
        _kSub = new TabControl { Items = { new TabItem { Header = "Vị trí có hàng", Content = t1 }, new TabItem { Header = "Vị trí trống (ghi tay)", Content = t2 } } };
        t1.Child = p;
        _kSub.SelectionChanged += async (_, e) =>
        {
            if (!ReferenceEquals(e.Source, _kSub)) return;   // ô chọn bên trong (Khu / Dãy…) nổi lên
            e.Handled = true;   // không nổi lên tab cha
            var to = _kSub.SelectedIndex == 1 ? t2 : t1; var from = to == t1 ? t2 : t1;
            if (p.Parent != to) { from.Child = null; to.Child = p; }
            KkModeUi();
            if (!_loading) await KkSelChanged();
        };
        KkModeUi();
        return _kSub;
    }
    private bool KkSoTab => (_kSub?.SelectedIndex ?? 0) == 0;
    private void KkModeUi()
    {
        foreach (var c in _kSoOnly) c.IsVisible = KkSoTab;
    }
    /// <summary>Đặt tab con + ô loại phiếu theo mode (không kích hoạt tạo lại phiếu).</summary>
    private void KkShowMode(string mode)
    {
        var was = _loading; _loading = true;
        try
        {
            if (_kSub is not null) _kSub.SelectedIndex = mode == "so" ? 0 : 1;
            KkModeUi();
        }
        finally { _loading = was; }
    }
    private (string Kho, string Khu, int D, string Mode) KkWant()
    {
        var khu = _kKhu.SelectedItem as string ?? ""; var d = _kDay.SelectedIndex <= 0 ? 0 : PickNum(_kDay);
        return (_kho, khu.Replace("Khu ", ""), d, KkSoTab ? "so" : "trong");
    }
    private void KkFillSelectors()
    {
        _loading = true;
        try
        {
            var khus = Svc.KhuList(_kho);
            var want = _k is not null && _k.Kho == _kho ? _k.Khu : _khu != "ALL" ? _khu : khus.FirstOrDefault() ?? "";
            Combo(_kKhu, khus.Select(k => "Khu " + k), Math.Max(0, khus.IndexOf(want)));
            var khu = khus.Count > 0 ? khus[Math.Max(0, _kKhu.SelectedIndex)] : "";
            var days = Svc.DayList(_kho, khu);
            var dWant = _k is not null && _k.Kho == _kho && _k.Khu == khu ? _k.D : days.Count > 0 ? 1 : 0;
            Combo(_kDay, new[] { "Tất cả dãy" }.Concat(days.Select(d => "Dãy " + Two(d))), dWant);
            if (_k is not null) KkShowMode(_k.Mode);
        }
        finally { _loading = false; }
    }
    private async Task<bool> KkCanLeave()
    {
        if (_k is null || !_k.Dirty || _k.Out) return true;
        return await Ui.Confirm(this, "Phiếu In vị trí đang có chỉnh sửa chưa xuất Excel. Bỏ các chỉnh sửa đó?", "Bỏ chỉnh sửa phiếu?", "Bỏ chỉnh sửa");
    }
    private async Task KkSelChanged()
    {
        if (_kKhu.SelectedItem is string kh && _k is not null && kh != "Khu " + _k.Khu)
        {
            // đổi khu → đổi danh sách dãy
            _loading = true;
            var days = Svc.DayList(_kho, kh.Replace("Khu ", ""));
            Combo(_kDay, new[] { "Tất cả dãy" }.Concat(days.Select(d => "Dãy " + Two(d))), days.Count > 0 ? 1 : 0);
            _loading = false;
        }
        if (_k is not null && KkWant() == _kKey) return;
        if (!await KkCanLeave()) { _loading = true; var k = _k!; Combo(_kKhu, Svc.KhuList(_kho).Select(x => "Khu " + x), Svc.KhuList(_kho).IndexOf(k.Khu)); var days = Svc.DayList(_kho, k.Khu); Combo(_kDay, new[] { "Tất cả dãy" }.Concat(days.Select(d => "Dãy " + Two(d))), k.D); _loading = false; KkShowMode(k.Mode); return; }
        _k = null; await KkEnsure();
    }
    private async Task KkRebuildIfClean()
    {
        if (_k is null || (_k.Dirty && !_k.Out)) return;
        _k = null;
        if (_tabs.SelectedIndex == 4) await KkEnsure();
    }
    private Task KkEnsure()
    {
        if (_k is null || _k.Kho != _kho)
        {
            _k = null;
            KkFillSelectors();
            var w = KkWant();
            if (w.Khu.Length == 0) { _kRows = new(); KkRender(); return Task.CompletedTask; }
            _k = Svc.BuildKk(w.Kho, w.Khu, w.D, w.Mode, Rut); _kKey = w;
        }
        KkRender();
        return Task.CompletedTask;
    }
    private void KkRender(int keepRow = -1)
    {
        _kRows = _k is null ? new() : _k.Visible().Select(x => (x.Pi, x.L)).ToList();
        var rows = _kRows.Select(x => new[] { Two(_k!.Pages[x.Pi].N), x.L.Code, x.L.Ma == "trống" ? "" : x.L.Ma, x.L.Tl, x.L.Sl, x.L.Ghi, x.L.Mv }).ToList();
        _loading = true;
        _km.Load(KkCols, rows);
        _loading = false;
        _kg.Refresh();
        if (keepRow >= 0 && _kRows.Count > 0) _kg.Select(Math.Min(keepRow, _kRows.Count - 1), Math.Max(1, _kg.Col));
        KkInfo();
    }
    private void KkInfo()
    {
        if (_k is null) { _kInfo.Text = "Chọn khu / dãy để tạo phiếu."; _bMerge.IsEnabled = _bMove.IsEnabled = _bMoveAll.IsEnabled = _bUndo.IsEnabled = false; return; }
        var merge = SoDoService.MergeCount(_k); var mv = _k.MoveCount;
        _bMerge.Content = merge > 0 ? $"Gộp size ({merge})" : "Gộp size"; _bMerge.IsEnabled = merge > 0;
        _bMoveAll.Content = mv > 0 ? $"Chuyển tất cả đề xuất ({mv})" : "Chuyển tất cả đề xuất"; _bMoveAll.IsEnabled = mv > 0; _bMove.IsEnabled = mv > 0;
        _bUndo.IsEnabled = _k.History > 0;
        var lines = _kRows.Count(x => x.L.Ma.Length > 0 && x.L.Ma != "trống");
        _kInfo.Text = $"{KhoLabel(_k.Kho)} · Khu {_k.Khu} · {(_k.D == 0 ? _k.Pages.Count + " dãy" : "Dãy " + Two(_k.D))} · {(_k.Mode == "rong" ? "vị trí trống để ghi tay" : _k.Mode == "trong" ? "phiếu trắng mọi vị trí" : "theo sổ sách")} · {_kRows.Count} dòng{(_k.Blank ? "" : $", {lines} dòng có hàng")}"
            + (_k.Dirty ? (_k.Out ? " · đã xuất Excel" : " · có chỉnh sửa chưa xuất") : "");
    }
    private IBrush? KkBg(int r, int c)
    {
        if (r < 0 || r >= _kRows.Count) return null;
        var l = _kRows[r].L;
        if (c == 0) return GrayBg;
        if (l.Mv.Length > 0) return MoveBg;
        if (l.Ghi.Contains("chuyển từ")) return MovedBg;
        if (l.Ma == "trống" || (l.Ma.Length == 0 && _k?.Mode == "so")) return GrayBg;
        return null;
    }
    private void KkFromGrid()
    {
        if (_k is null) return;
        var rows = _km.Snapshot();
        _k.Snap();
        var changed = false;
        for (var i = 0; i < rows.Count; i++)
        {
            string G(int c) => c < rows[i].Length ? rows[i][c] ?? "" : "";
            if (i >= _kRows.Count)
            {
                var pg = _k.Pages.Count > 0 ? _k.Pages[^1] : null; if (pg is null) break;
                var nl = new SoDoService.KkLine(); pg.Rows.Add(nl); _kRows.Add((_k.Pages.Count - 1, nl));
            }
            var l = _kRows[i].L; var ma = G(2).Trim();
            var nv = (Code: G(1).Trim(), Ma: ma.Length == 0 && l.Ma == "trống" ? "trống" : ma, Tl: G(3).Trim(), Sl: G(4).Trim(), Ghi: G(5).Trim());
            if (nv.Code != l.Code || nv.Ma != l.Ma || nv.Tl != l.Tl || nv.Sl != l.Sl || nv.Ghi != l.Ghi)
            {
                l.Code = nv.Code; l.Ma = nv.Ma; l.Tl = nv.Tl; l.Sl = nv.Sl; l.Ghi = nv.Ghi; changed = true;
            }
        }
        if (changed) _k.Dirty = true; else _k.DropSnap();
        if (rows.Count != _km.RowCount || _kRows.Count != rows.Count) KkRender(_kg.Row);
        else { _kg.Refresh(); KkInfo(); }
    }
    private SoDoService.KkLine? KkCur() => _kg.Row >= 0 && _kg.Row < _kRows.Count ? _kRows[_kg.Row].L : null;
    private void KkMerge() { if (_k is null) return; _k.Snap(); if (SoDoService.Merge(_k) == 0) _k.DropSnap(); KkRender(_kg.Row); }
    private void KkMoveOne()
    {
        if (_k is null) return;
        var l = KkCur();
        if (l is null || l.Mv.Length == 0) { _status.Text = "Dòng đang chọn không có đề xuất chuyển (chọn 1 dòng màu vàng)."; return; }
        _k.Snap();
        if (!SoDoService.Move(_k, l)) { _k.DropSnap(); _status.Text = "Không chuyển được (ô đích không còn trống trên phiếu)."; return; }
        KkRender(_kg.Row);
    }
    private void KkMoveAll() { if (_k is null) return; _k.Snap(); var n = SoDoService.MoveAll(_k); if (n == 0) _k.DropSnap(); _status.Text = n > 0 ? $"Đã chuyển {n} nhóm hàng trên phiếu." : ""; KkRender(_kg.Row); }
    private void KkUndo() { if (_k?.Undo() == true) KkRender(_kg.Row); }
    private void KkAddRow()
    {
        if (_k is null || _k.Pages.Count == 0) return;
        var i = Math.Clamp(_kg.Row, 0, Math.Max(0, _kRows.Count - 1));
        _k.Snap();
        if (_kRows.Count == 0) _k.Pages[0].Rows.Add(new SoDoService.KkLine());
        else
        {
            var (pi, cur) = _kRows[i]; var pg = _k.Pages[pi];
            pg.Rows.Insert(pg.Rows.IndexOf(cur) + 1, new SoDoService.KkLine { Code = cur.Code, Pos = cur.Pos });
        }
        _k.Dirty = true;
        KkRender(i + 1);
    }
    private void KkDelRows()
    {
        if (_k is null || _kRows.Count == 0) return;
        var s = _kg.Selection; var lines = _kRows.Skip(s.R1).Take(Math.Max(1, s.R2 - s.R1 + 1)).ToList();
        _k.Snap();
        foreach (var (pi, l) in lines) _k.Pages[pi].Rows.Remove(l);
        _k.Dirty = true;
        KkRender(s.R1);
    }
    private async Task KkPrint()
    {
        if (_k is null) return;
        var k = _k;
        if (k.Pages.Count == 0) { await Ui.Message(this, "Phiếu chưa có dãy nào để in."); return; }
        if (await Ui.PrintVia(this, $"Phiếu in vị trí {KhoLabel(k.Kho)} khu {k.Khu}{(k.D > 0 ? " dãy " + Two(k.D) : "")}", tmp => Svc.ExportKk(tmp, k))) KkInfo();
    }
    private async Task KkExport()
    {
        if (_k is null) return;
        var name = $"Phieu in vi tri {KhoLabel(_k.Kho)} khu {_k.Khu}{(_k.D > 0 ? " day " + Two(_k.D) : "")} {DateTime.Today:dd.MM.yyyy}";
        var path = await Ui.SaveFile(this, "Xuất phiếu in vị trí ra Excel", Ui.SafeFileName(name) + ".xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.ExportKk(path, _k))) { KkInfo(); Ui.OpenExternal(path); }
    }
    private async Task<(string Khu, int D)?> KkPickDay(string what)
    {
        if (_k is null) return null;
        if (_k.D > 0) return (_k.Khu, _k.D);
        var days = Svc.DayList(_k.Kho, _k.Khu);
        var r = await Ui.Choose(this, $"{what} làm theo từng dãy. Chọn dãy:", what, days.Select(d => $"{KhoLabel(_k.Kho)} · Khu {_k.Khu} · Dãy {Two(d)}").ToList());
        return r is { } x ? (_k.Khu, days[x.Index]) : null;
    }
    private async Task KkToNhapLieu()
    {
        if (await KkPickDay("Chuyển sang Nhập liệu") is not { } d) return;
        var name = "";
        if (!await Act(() => name = Svc.ToNhapLieu(_kho, d.Khu, d.D).Name)) return;
        await Ui.Message(this, $"Đã tạo trang Nhập liệu “{name}” (mẫu Kiểm kê), số lượng điền sẵn theo sổ sách.\n\nMở module Nhập liệu, sửa cột “Số thùng” theo số đếm thực tế (thêm dòng cho hàng ngoài sổ), rồi quay lại bấm “Rà soát chênh lệch”.", "Đã chuyển sang Nhập liệu");
    }

    private async Task KkReview()
    {
        if (await KkPickDay("Rà soát chênh lệch") is not { } d) return;
        SoDoService.ReviewResult res;
        try { res = Svc.ReviewDay(_kho, d.Khu, d.D); }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Chưa rà soát được"); return; }
        var r = res.R;
        var body = new DockPanel();
        var head = new StackPanel { Spacing = 4 };
        head.Children.Add(Label($"Trang Nhập liệu: {res.PageName} · {res.PageRows} dòng", true));
        var k = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        k.Children.Add(Kpi("Dòng chênh lệch", r.N.ToString(Ui.Vi), r.N > 0 ? Warn : null));
        k.Children.Add(Kpi("Tổng chênh SL", (r.Dsl > 0 ? "+" : "") + Q(r.Dsl)));
        k.Children.Add(Kpi("Chênh Net (kg)", (r.Dnet > 0 ? "+" : "") + Kg(r.Dnet)));
        k.Children.Add(Kpi("Sổ có, thực = 0", r.Gone.ToString(Ui.Vi)));
        k.Children.Add(Kpi("Hàng ngoài sổ", r.Extra.ToString(Ui.Vi) + (r.NoMa > 0 ? $" ({r.NoMa} chưa mã)" : "")));
        k.Children.Add(Kpi("Tầng vượt mức (thực)", r.OverT.Count.ToString(Ui.Vi), r.OverT.Count > 0 ? Warn : null));
        head.Children.Add(k);
        if (r.Neg > 0) head.Children.Add(Label($"⚠ {r.Neg} dòng số lượng âm — kiểm tra lại trang Nhập liệu.", fg: Warn));
        if (r.OverT.Count > 0) head.Children.Add(Muted("Tầng vượt mức theo số thực: " + string.Join(", ", r.OverT)));
        var gm = new GridModel { ReadOnly = true };
        var rows = res.Rows.Select(x => (x, eff: x.Sl ?? (x.Known ? x.Sl0 : 0))).Where(t => Math.Abs(t.eff - t.x.Sl0) > 1e-9 || (t.x.Known && t.eff == 0))
            .Select(t => new[] { t.x.Code, t.x.Ma, t.x.Lo, Q(t.x.Sl0), Q(t.eff), (t.eff - t.x.Sl0 > 0 ? "+" : "") + Q(t.eff - t.x.Sl0), Kg(R2((t.eff - t.x.Sl0) * t.x.Tl)), t.x.Known ? (t.eff == 0 ? "Thực = 0, sổ có hàng" : "") : "Hàng ngoài sổ" }).ToList();
        gm.Load(new[] { new GridColumn("vt", "Vị trí", 110), new GridColumn("ma", "Mã hàng", 190), new GridColumn("lo", "Lô", 80), new GridColumn("a", "SL sổ", 70, "number"), new GridColumn("b", "SL thực", 70, "number"), new GridColumn("d", "Chênh", 70, "number"), new GridColumn("n", "Chênh Net", 85, "number"), new GridColumn("g", "Ghi chú", 160) }, rows);
        var grid = new SheetGrid(gm) { SpareRows = 0 };
        head.Children.Add(Label(rows.Count == 0 ? "Khớp sổ sách — không có dòng chênh lệch." : $"{rows.Count} dòng chênh lệch:", true));
        var close = new Button { Content = "Đóng", IsCancel = true };
        var exp = new Button { Content = "Xuất Excel", Classes = { "primary" }, IsVisible = _export && rows.Count > 0 };
        var btns = Ui.Buttons(exp, close);
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(btns, Dock.Bottom);
        body.Children.Add(head); body.Children.Add(btns); body.Children.Add(Framed(grid));
        var w = Ui.Dialog("Rà soát chênh lệch — " + NlTitle(d.Khu, d.D), body, 960);
        w.SizeToContent = SizeToContent.Manual; w.Height = 600; w.CanResize = true;
        close.Click += (_, _) => w.Close();
        exp.Click += async (_, _) =>
        {
            var path = await Ui.SaveFile(w, "Xuất chênh lệch kiểm kê", Ui.SafeFileName("Chenh lech " + NlTitle(d.Khu, d.D)) + ".xlsx", "xlsx", "Excel");
            if (path is not null && await Ui.Run(w, () => Svc.ExportReview(path, res))) Ui.OpenExternal(path);
        };
        await Ui.Show(this, w);
    }
    private string NlTitle(string khu, int d) => SoDoService.NlName(_kho, khu, d);

    // =====================================================================================
    // Khóa kiểm kê
    // =====================================================================================

    private async Task DiffDialog()
    {
        var gm = new GridModel { ReadOnly = true };
        var names = Svc.Locks.ToDictionary(l => l.Id, SoDoService.LockName);
        gm.Load(new[] { new GridColumn("vt", "Vị trí", 115), new GridColumn("ma", "Mã hàng", 200), new GridColumn("lo", "Lô", 90), new GridColumn("a", "Lúc khóa", 75, "number"), new GridColumn("b", "Hiện tại", 75, "number"), new GridColumn("d", "Chênh", 70, "number"), new GridColumn("k", "Loại", 85), new GridColumn("l", "Khóa", 180) },
            Svc.LockDiff.Select(x => new[] { x.Vt, Ma(x.Ma, x.Ten), x.Lo, Q(x.Was), Q(x.Now), (x.D > 0 ? "+" : "") + Q(x.D), x.Kind, names.GetValueOrDefault(x.LockId) ?? "" }));
        var body = new DockPanel();
        var head = Label(Svc.LockDiff.Count == 0 ? "Tồn ở các phần đang khóa chưa thay đổi so với lúc khóa." : $"{Svc.LockDiff.Count} thay đổi tồn (theo file Tồn kho theo vị trí mới nhất) ở các phần đang khóa — sơ đồ vẫn giữ số lúc khóa cho tới khi mở khóa.", true);
        head.Margin = new Thickness(0, 0, 0, 8);
        var close = new Button { Content = "Đóng", IsCancel = true, IsDefault = true };
        var btns = Ui.Buttons(close);
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(btns, Dock.Bottom);
        body.Children.Add(head); body.Children.Add(btns); body.Children.Add(Framed(new SheetGrid(gm) { SpareRows = 0 }));
        var w = Ui.Dialog("Thay đổi từ lúc khóa kiểm kê", body, 940);
        w.SizeToContent = SizeToContent.Manual; w.Height = 520; w.CanResize = true;
        close.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
    }

    private async Task LockDialog()
    {
        var w = Ui.Dialog("Khóa kiểm kê", new StackPanel(), 600);
        void Build()
        {
            var s = new StackPanel { Spacing = 8 };
            s.Children.Add(Muted("Khóa 1 phần kho khi đang kiểm kê: sơ đồ, phiếu in và rà soát của phần đó giữ nguyên số liệu lúc khóa (tồn nạp sau vẫn được ghi nhận ở “Xem thay đổi”). Mở khóa để quay về số liệu hiện tại."));
            s.Children.Add(Label("Đang khóa", true, 13.5));
            if (Svc.Locks.Count == 0) s.Children.Add(Muted("Chưa khóa phần nào."));
            foreach (var l in Svc.Locks)
            {
                var row = new DockPanel();
                var un = Btn("Mở khóa", async () =>
                {
                    if (!await Ui.Confirm(w, $"Mở khóa {SoDoService.LockName(l)}? Sơ đồ phần này sẽ dùng tồn hiện tại.", "Mở khóa kiểm kê", "Mở khóa")) return;
                    if (await Act(() => Svc.Unlock(l.Id), "Đã mở khóa " + SoDoService.LockName(l))) Build();
                }, danger: true);
                DockPanel.SetDock(un, Dock.Right); row.Children.Add(un);
                row.Children.Add(Label($"{SoDoService.LockName(l)} · {l.Ts.Replace('T', ' ')} · {l.By}" + (l.Note.Length > 0 ? " · " + l.Note : "") + $" · {Svc.Snaps.GetValueOrDefault(l.Id)?.Count ?? 0} dòng"));
                s.Children.Add(row);
            }
            s.Children.Add(Label("Khóa thêm", true, 13.5));
            var khus = Svc.KhuList(_kho).Select(k => "Khu " + k).ToList(); if (Svc.HasZ(_kho)) khus.Add("Chưa có vị trí (Z)");
            var cKhu = new ComboBox { ItemsSource = khus, SelectedIndex = Math.Max(0, _khu == "ALL" ? 0 : khus.IndexOf("Khu " + _khu)), MinWidth = 160 };
            var cDay = new ComboBox { MinWidth = 120 };
            void Days() { var k = (cKhu.SelectedItem as string ?? "").Replace("Khu ", ""); var ds = k.StartsWith("Chưa") ? new List<int>() : Svc.DayList(_kho, k); cDay.ItemsSource = new[] { "Cả khu" }.Concat(ds.Select(d => "Dãy " + Two(d))).ToList(); cDay.SelectedIndex = 0; }
            cKhu.SelectionChanged += (_, _) => Days(); Days();
            var note = new TextBox { Watermark = "Ghi chú (VD: kiểm kê cuối tháng 11)" };
            var form = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            form.Children.Add(Label(KhoLabel(_kho), true)); form.Children.Add(cKhu); form.Children.Add(cDay);
            s.Children.Add(form); s.Children.Add(note);
            var add = new Button { Content = "🔒 Khóa", Classes = { "primary" } };
            var close = new Button { Content = "Đóng", IsCancel = true };
            add.Click += async (_, _) =>
            {
                var kText = cKhu.SelectedItem as string ?? ""; var khu = kText.StartsWith("Chưa") ? "Z" : kText.Replace("Khu ", "");
                var d = cDay.SelectedIndex <= 0 ? 0 : PickNum(cDay);
                if (await Act(() => Svc.AddLocks(new[] { (_kho, khu, d) }, note.Text ?? ""), "Đã khóa kiểm kê")) Build();
            };
            close.Click += (_, _) => w.Close();
            s.Children.Add(Ui.Buttons(close, add));
            ((Border)w.Content!).Child = new ScrollViewer { Content = s, MaxHeight = 620 };
        }
        Build();
        await Ui.Show(this, w);
    }

    // =====================================================================================
    // Cấu hình kho
    // =====================================================================================

    private async Task ConfigDialog()
    {
        var cfg = Svc.Config.Clone();
        var kho = cfg.ContainsKey(_kho) ? _kho : cfg.Keys.FirstOrDefault() ?? "TG1";
        var gm = new GridModel();
        var cols = new[] { new GridColumn("khu", "Khu", 60), new GridColumn("d", "Số dãy", 70, "number"), new GridColumn("k", "Số khoang", 80, "number"), new GridColumn("t", "Số tầng", 70, "number"), new GridColumn("td", "Dãy khác số tầng (VD: 3:6, 10-12:5)", 260) };
        var grid = new SheetGrid(gm) { SpareRows = 4, Height = 260 };
        var khoBox = new ComboBox { MinWidth = 140 };
        var err = new TextBlock { Foreground = Warn, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        void Fill()
        {
            var keys = cfg.Keys.ToList();
            khoBox.ItemsSource = keys.Select(KhoLabel).Concat(new[] { "+ Thêm kho…" }).ToList();
            khoBox.SelectedIndex = keys.IndexOf(kho);
            gm.Load(cols, (cfg.GetValueOrDefault(kho) ?? new()).Select(e => new[] { e.Khu, e.D.ToString(CultureInfo.InvariantCulture), e.K.ToString(CultureInfo.InvariantCulture), (e.T ?? NT).ToString(CultureInfo.InvariantCulture), TdText(e.Td) }));
            grid.Refresh();
        }
        // đọc lưới → cfg[kho]; trả lỗi
        string? Take()
        {
            var list = new List<KhuCfg>(); var seen = new HashSet<string>();
            var rows = gm.Snapshot();
            for (var i = 0; i < rows.Count; i++)
            {
                string G(int c) => c < rows[i].Length ? (rows[i][c] ?? "").Trim() : "";
                if (rows[i].All(string.IsNullOrWhiteSpace)) continue;
                var khu = G(0).ToUpperInvariant();
                if (!System.Text.RegularExpressions.Regex.IsMatch(khu, "^[A-Y]{1,2}$")) return $"Dòng {i + 1}: khu “{G(0)}” phải là chữ cái (A, B…; Z dành cho hàng chưa có vị trí).";
                if (!seen.Add(khu)) return $"Khu {khu} bị trùng.";
                if (!int.TryParse(G(1), out var d) || d is < 1 or > 99) return $"Khu {khu}: số dãy 1–99.";
                if (!int.TryParse(G(2), out var k) || k is < 1 or > 9) return $"Khu {khu}: số khoang 1–9.";
                var t = NT; if (G(3).Length > 0 && (!int.TryParse(G(3), out t) || t is < 1 or > NT_MAX)) return $"Khu {khu}: số tầng 1–{NT_MAX}.";
                var td = ParseTd(G(4));
                if (G(4).Length > 0 && td.Count == 0) return $"Khu {khu}: “{G(4)}” không đọc được — ghi dạng dãy:tầng, VD 3:6, 10-12:5.";
                list.Add(new KhuCfg { Khu = khu, D = d, K = k, T = t, Td = new SortedDictionary<int, int>(td.Where(x => x.Key <= d && x.Value != t).ToDictionary(x => x.Key, x => x.Value)) });
            }
            if (list.Count == 0) return $"{KhoLabel(kho)} chưa có khu nào.";
            cfg[kho] = list.OrderBy(x => x.Khu, StringComparer.Ordinal).ToList();
            return null;
        }
        var save = new Button { Content = "💾 Lưu cấu hình", Classes = { "primary" } };
        var reset = new Button { Content = "Khôi phục mặc định" };
        var close = new Button { Content = "Đóng", IsCancel = true };
        var del = new Button { Content = "Xóa kho này", Classes = { "danger" } };
        var body = new StackPanel { Spacing = 8 };
        var top = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        top.Children.Add(Label("Kho", true)); top.Children.Add(khoBox); top.Children.Add(del);
        body.Children.Add(Muted("Mỗi dòng 1 khu: số dãy × số khoang × số tầng (tối đa 7). Dãy có số tầng khác ghi ở cột cuối. Vị trí trong file tồn vượt cấu hình sẽ được tự nới khi vẽ."));
        body.Children.Add(top); body.Children.Add(Framed(grid)); body.Children.Add(err);
        body.Children.Add(Ui.Buttons(reset, close, save));
        var w = Ui.Dialog("Cấu hình kho", body, 640);
        var busy = false;
        khoBox.SelectionChanged += async (_, _) =>
        {
            if (busy || khoBox.SelectedIndex < 0) return;
            var keys = cfg.Keys.ToList();
            if (Take() is { } e) { err.Text = e; busy = true; khoBox.SelectedIndex = keys.IndexOf(kho); busy = false; return; }
            err.Text = "";
            if (khoBox.SelectedIndex >= keys.Count)
            {
                var n = Enumerable.Range(1, 30).Select(i => "TG" + i).First(x => !cfg.ContainsKey(x));
                var name = await Ui.Prompt(w, "Mã kho mới (dạng TG + số, VD TG5):", "Thêm kho", n);
                busy = true;
                if (name is not null && System.Text.RegularExpressions.Regex.IsMatch(name.Trim().ToUpperInvariant(), @"^TG\d{1,2}$") && !cfg.ContainsKey(name.Trim().ToUpperInvariant()))
                {
                    kho = name.Trim().ToUpperInvariant(); cfg[kho] = new List<KhuCfg> { new() { Khu = "A", D = 10, K = 4, T = NT } };
                }
                else if (name is not null) err.Text = "Mã kho phải dạng TG + số và chưa có.";
                Fill(); busy = false; return;
            }
            kho = keys[khoBox.SelectedIndex]; busy = true; Fill(); busy = false;
        };
        del.Click += async (_, _) =>
        {
            if (cfg.Count <= 1) { err.Text = "Phải còn ít nhất 1 kho."; return; }
            if (!await Ui.Confirm(w, $"Xóa {KhoLabel(kho)} khỏi cấu hình? (Hàng có vị trí ở kho này vẫn được vẽ theo dữ liệu.)", "Xóa kho", "Xóa")) return;
            cfg.Remove(kho); kho = cfg.Keys.First(); busy = true; Fill(); busy = false;
        };
        reset.Click += async (_, _) =>
        {
            if (!await Ui.Confirm(w, "Đưa cấu hình về mặc định của app (4 kho TG1–TG4)?", "Khôi phục mặc định", "Khôi phục")) return;
            cfg = DefaultCfg(); kho = cfg.ContainsKey(kho) ? kho : cfg.Keys.First(); busy = true; Fill(); busy = false; err.Text = "";
        };
        close.Click += (_, _) => w.Close();
        save.Click += async (_, _) =>
        {
            if (Take() is { } e) { err.Text = e; return; }
            if (await Act(() => Svc.SaveConfig(cfg), "Đã lưu cấu hình kho")) { _k = null; w.Close(); }
        };
        busy = true; Fill(); busy = false;
        await Ui.Show(this, w);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.KhangSinh;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using static KhoLanh.Core.KhangSinh.KhangSinhCore;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Kết quả kháng sinh (C#, nghiệp vụ theo "Tra cứu kháng sinh" của app cũ — không dùng WebView2).
/// Dữ liệu (lần 1 / kiểm lại, tìm nhanh, lọc cột, xóa theo ngày / phiếu / tháng), Tra cứu nhanh 7 chỉ tiêu,
/// Nhập theo lô trên lưới, Nhập từ Excel (mỗi sheet = 1 ngày), Nạp PDF phiếu kết quả (lần 1 / kiểm lại, đối soát 2 cách đọc),
/// Quy định kháng sinh sửa trên lưới.
/// </summary>
public sealed partial class KhangSinhView : UserControl, IPage
{
    public string Title => "Kết quả kháng sinh";
    public string Subtitle => "Kết quả kiểm kháng sinh theo lô (lần 1 và kiểm lại), tra cứu Đạt / Không đạt theo thị trường, nhập theo lô hoặc từ Excel, quy định kháng sinh.";

    private static KhangSinhService Svc => App.Services.KhangSinh;
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45");
    private static readonly IBrush Red = AppTheme.Brush("#C0392B");
    private static readonly IBrush GreenBg = AppTheme.Brush("#E2EFDA");
    private static readonly IBrush RedBg = AppTheme.Brush("#F8CBAD");
    private static readonly IBrush AmberBg = AppTheme.Brush("#FFE699");
    private static readonly IBrush BlueBg = AppTheme.Brush("#DDEBF7");
    private static readonly IBrush CalcBg = AppTheme.Brush("#F3F6FA");
    private static IBrush? ToneBg(string tone) => tone switch { "red" => RedBg, "green" => GreenBg, "amber" => AmberBg, _ => null };

    private readonly bool _edit, _export;
    private readonly TextBlock _info = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TabControl _tabs;

    // ---------- Dữ liệu
    private string _mode = ModeL1;
    private readonly ToggleButton _bL1 = new() { Content = "Lần 1", IsChecked = true, Padding = new Thickness(10, 4), FontSize = 12.5 };
    private readonly ToggleButton _bKl = new() { Content = "Kiểm lại", Padding = new Thickness(10, 4), FontSize = 12.5 };
    private readonly ComboBox _day = new() { MinWidth = 150 };
    private readonly ToggleButton _allDays = new() { Content = "Tất cả ngày", Padding = new Thickness(10, 4), FontSize = 12.5 };
    private readonly TextBox _q = new() { Watermark = "Tìm nhanh — VD: L1 3/5/2026 · MKS2 9.9.26 · MC3 · mẫu chờ 2 · 46.EU", Width = 460 };
    private readonly TextBox[] _colF;
    private static readonly string[] ColFNames = { "Lô hàng", "Kết quả", "Ký hiệu", "KQ kiểm lại", "Ghi chú" };
    private readonly TextBlock _hInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly GridModel _hm = new() { ReadOnly = true };
    private readonly SheetGrid _hg;
    private List<Row> _matched = new(), _shown = new();
    private List<string> _days = new();
    private readonly StackPanel _det = new() { Spacing = 6 };
    private bool _busy;

    // ---------- Tra cứu nhanh
    private readonly TextBox[] _in = Fields.Select(f => new TextBox { Watermark = "ND", Width = 90 }).ToArray();
    private readonly Border _res = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), BorderThickness = new Thickness(1), BorderBrush = Line_, IsVisible = false };

    // ---------- Nhập theo lô
    private readonly TextBox _bDate = new() { Width = 110 };
    private readonly GridModel _bm = new();
    private readonly SheetGrid _bg;
    private readonly TextBlock _bInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private bool _calc;
    private Dictionary<int, KhangSinhService.Entry> _bRes = new();

    // ---------- Nhập từ Excel
    private List<KhangSinhService.SheetIn> _sheets = new();
    private string _file = "";
    private readonly GridModel _sm = new();
    private readonly SheetGrid _sg;
    private readonly WrapPanel _mapPanel = new() { ItemSpacing = 10, LineSpacing = 6 };
    private readonly Dictionary<string, ComboBox> _mapBox = new();
    private readonly GridModel _pm = new() { ReadOnly = true };
    private readonly SheetGrid _pg;
    private readonly CheckBox _skipDup = new() { Content = "Bỏ qua dòng đã có bản ghi trùng Ngày + Mã lô", IsChecked = true, FontSize = 12.5 };
    private readonly TextBlock _xInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _xBody = new() { Spacing = 8, IsVisible = false };
    private bool _sLoading;

    // ---------- Quy định
    private readonly GridModel _rm = new();
    private readonly SheetGrid _rg;
    private readonly TextBlock _rInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private bool _rDirty;

    public KhangSinhView()
    {
        _edit = Svc.CanEdit; _export = Svc.CanExport;
        _colF = ColFNames.Select(n => new TextBox { Watermark = n, Width = 130, FontSize = 12 }).ToArray();
        _hg = new SheetGrid(_hm) { SpareRows = 0, CellBackground = HBg };
        _hg.SelectionChanged += () => { if (_hg.Row >= 0 && _hg.Row < _shown.Count) ShowDetail(_shown[_hg.Row]); };
        _bg = new SheetGrid(_bm) { SpareRows = 30, CellBackground = BBg };
        _bg.Display = (r, c, raw) => c < 10 ? raw : _bRes.TryGetValue(r, out var e) ? (c == 10 ? e.Dat : e.Rule?.KyHieu ?? "") : "";
        _bm.Changed += () => { if (!_calc) Recalc(); };
        _sg = new SheetGrid(_sm) { SpareRows = 0 };
        _sm.Changed += () => { if (!_sLoading) SheetsEdited(); };
        _sg.SelectionChanged += () => { if (!_sLoading) ShowPreview(); };
        _pg = new SheetGrid(_pm) { SpareRows = 0 };
        _rg = new SheetGrid(_rm) { SpareRows = 5 };
        _rm.Changed += () => { _rDirty = true; _rInfo.Text = "Có thay đổi chưa lưu — bấm \"Lưu quy định\"."; };

        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Dữ liệu", Content = BuildHistory() },
                new TabItem { Header = "Tra cứu nhanh", Content = BuildLookup() },
                new TabItem { Header = "Nhập theo lô", Content = BuildBatch() },
                new TabItem { Header = "Nhập từ Excel", Content = BuildImport() },
                new TabItem { Header = "Nạp PDF phiếu", Content = BuildPdf() },
                new TabItem { Header = "Quy định kháng sinh", Content = BuildRules() },
                new TabItem { Header = "Tôm nhập khẩu", Content = BuildNk() },
            },
        };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        tools.Children.Add(Btn("⟳ Tải lại", async () => await LoadAsync()));
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) }; DockPanel.SetDock(tools, Dock.Right); row.Children.Add(tools); row.Children.Add(_info);
        var root = new DockPanel(); DockPanel.SetDock(row, Dock.Top); root.Children.Add(row); root.Children.Add(_tabs);
        Content = root;
        AttachedToVisualTree += async (_, _) => { if (!Ui.SkipReload(this)) await LoadAsync(); };   // 1.136: quay lại trang dữ liệu chưa đổi → không đọc lại
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
    private static Border Framed(Control c) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = c };
    private static StackPanel HRow(params Control[] cs) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; foreach (var c in cs) p.Children.Add(c); return p; }
    private static WrapPanel Wrap(params Control[] cs) { var p = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 }; foreach (var c in cs) p.Children.Add(c); return p; }

    private async Task LoadAsync()
    {
        _info.Text = "Đang tải…";
        try { await Task.Run(Svc.Load); }
        catch (Exception ex) { _info.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        RenderInfo();
        ApplyFilters(keepDay: false);
        LoadRulesGrid();
        NkRefresh();
    }
    private void RenderInfo()
    {
        var l1 = Svc.Records.Count(r => r.HasLan1); var kl = Svc.Records.Sum(r => r.Lan2.Count);
        _info.Text = Svc.Records.Count == 0
            ? "Chưa có kết quả nào. Nhập theo lô / từ Excel, hoặc chép dữ liệu app cũ ở Cài đặt máy › Chép dữ liệu từ app cũ."
            : $"{Svc.Records.Count:#,0} lô · {l1:#,0} kết quả lần 1 · {kl:#,0} kết quả kiểm lại · quy định {Svc.Rules.Count} trường hợp" + (Svc.RulesCustom ? " (đã sửa)" : "");
    }
    private async Task<bool> Act(Action a, string? okMsg = null)
    {
        try
        {
            await Task.Run(a);
            RenderInfo(); ApplyFilters(keepDay: true);
            if (okMsg is not null) await Ui.Message(this, okMsg);
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

    // =====================================================================================
    // Dữ liệu
    // =====================================================================================

    private Control BuildHistory()
    {
        _bL1.Click += (_, _) => SetMode(ModeL1);
        _bKl.Click += (_, _) => SetMode(ModeKl);
        ToolTip.SetTip(_bL1, "Lô kiểm lần 1 — chọn theo ngày kiểm in cuối phiếu (chưa có thì ngày nhập)");
        ToolTip.SetTip(_bKl, "Kết quả kiểm lại (lần 2, 3…) — mỗi mẫu 1 dòng, chọn theo ngày phân tích của phiếu");
        _day.SelectionChanged += (_, _) => { if (!_busy) RenderPage(); };
        _allDays.Click += (_, _) => RenderPage();
        var t = new System.Timers.Timer(220) { AutoReset = false };
        t.Elapsed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyFilters(keepDay: false));
        _q.TextChanged += (_, _) => { t.Stop(); t.Start(); };
        foreach (var f in _colF) f.TextChanged += (_, _) => { t.Stop(); t.Start(); };

        var nav = Wrap(Label("Xem theo:"), _bL1, _bKl,
            Btn("‹ Ngày trước", () => Step(+1)), _day, Btn("Ngày sau ›", () => Step(-1)), _allDays, _hInfo);
        var search = Wrap(_q, Btn("Xóa bộ lọc", ClearFilters));
        var cols = Wrap(new Control[] { Label("Lọc cột:") }.Concat(_colF).ToArray());
        // 26/11: lệnh xóa ít dùng (theo phiếu / tháng / lỗi ngày / toàn bộ) luôn nằm trong "⋯ Thêm" — thanh còn Xuất + 2 lệnh xóa hay dùng
        var acts = new ToolRow { };
        if (_export) acts.Children.Add(Btn("Xuất Excel (đang lọc)", async () => await ExportAsync()));
        if (_edit)
        {
            acts.Children.Add(Btn("Xóa dòng đang chọn…", async () => await DeleteSelected(), danger: true));
            acts.Children.Add(Btn("Xóa dữ liệu ngày này…", async () => await DeleteDay(), danger: true, tip: "Xóa mọi bản ghi của ngày đang xem để nhập lại file ngày đó"));
            acts.Children.Add(ToolRow.More(Btn("Xóa theo phiếu…", async () => await DeletePhieu(), tip: "Xóa đúng phần dữ liệu đã nhập từ 1 phiếu PDF (lần 1 hoặc lần 2)")));
            acts.Children.Add(ToolRow.More(Btn("Xóa theo tháng…", async () => await DeleteMonth())));
            acts.Children.Add(ToolRow.More(Btn("Xóa dòng lỗi ngày", async () => await DeleteBad(), tip: "Xóa các bản ghi có Ngày không hợp lệ")));
            acts.Children.Add(ToolRow.More(Btn("Xóa toàn bộ dữ liệu…", async () => await ClearAll(), danger: true)));
        }
        var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 6) };
        head.Children.Add(nav); head.Children.Add(search); head.Children.Add(cols); head.Children.Add(acts);
        var left = new DockPanel(); DockPanel.SetDock(head, Dock.Top); left.Children.Add(head); left.Children.Add(Framed(_hg));
        var right = new Border { BorderBrush = Line_, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(12, 6, 4, 6), Child = new ScrollViewer { Content = _det } };
        _det.Children.Add(Muted("Bấm một dòng để xem chi tiết: chỉ tiêu, mô tả quy định, các lần kiểm lại và phiếu nguồn."));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,340") };
        g.Children.Add(left); Grid.SetColumn(right, 2); g.Children.Add(right);
        return g;
    }
    private void SetMode(string m)
    {
        _mode = m; _bL1.IsChecked = m == ModeL1; _bKl.IsChecked = m == ModeKl; _allDays.IsChecked = false;
        ApplyFilters(keepDay: false);
    }
    private void ClearFilters()
    {
        _q.Text = ""; foreach (var f in _colF) f.Text = "";
        ApplyFilters(keepDay: false);
    }
    private void Step(int d)
    {
        if (_days.Count == 0) return;
        _allDays.IsChecked = false;
        var i = Math.Clamp((_day.SelectedIndex < 0 ? 0 : _day.SelectedIndex) + d, 0, _days.Count - 1);
        _day.SelectedIndex = i; RenderPage();
    }
    private bool Filtering => (_q.Text ?? "").Trim().Length > 0 || _colF.Any(f => (f.Text ?? "").Trim().Length > 0);

    private void ApplyFilters(bool keepDay)
    {
        if (!Svc.Loaded) return;
        var m = Match(_q.Text);
        if (_mode == ModeL1 && m.MksMode) { _mode = ModeKl; _bL1.IsChecked = false; _bKl.IsChecked = true; }   // mẫu chờ chỉ có ở phiếu kiểm lại
        var cf = _colF.Select(f => NormText(f.Text)).ToArray();
        bool ColOk(Row r)
        {
            string[] v = { r.Batch, r.Dat, r.KyHieu, Lan2Text(r), r.Note };
            for (var i = 0; i < cf.Length; i++) if (cf[i].Length > 0 && !NormText(v[i]).Contains(cf[i])) return false;
            return true;
        }
        _matched = BaseRows(Svc.Records, _mode).Where(r => ColOk(r) && m.Test(r)).ToList();
        var prev = keepDay && _day.SelectedItem is string s ? s : null;
        _days = _matched.Select(r => r.GroupDate).Where(x => x.Length > 0).Distinct().OrderByDescending(x => x, StringComparer.Ordinal).ToList();
        _busy = true;
        _day.ItemsSource = _days.Select(Dmy).ToList();
        var idx = prev is not null ? _days.Select(Dmy).ToList().IndexOf(prev) : -1;
        _day.SelectedIndex = _days.Count == 0 ? -1 : Math.Max(0, idx);
        _busy = false;
        if (!keepDay) _allDays.IsChecked = Filtering;   // đang tìm / lọc → hiện mọi ngày
        RenderPage();
    }
    private void RenderPage()
    {
        var all = _allDays.IsChecked == true || _days.Count == 0;
        var day = !all && _day.SelectedIndex >= 0 && _day.SelectedIndex < _days.Count ? _days[_day.SelectedIndex] : null;
        _shown = Sort(day is null ? _matched : _matched.Where(r => r.GroupDate == day), _mode);
        var cols = new List<GridColumn>
        {
            new("d", "Ngày", 82), new("b", "Lô hàng", 150), new("n", "Ghi chú", 140), new("kq", "Kết quả", 165), new("kh", "Ký hiệu", 95),
            new("l2", _mode == ModeKl ? "Lần / phiếu" : "KQ kiểm lại (mới nhất)", 215),
        };
        cols.AddRange(Fields.Select(f => new GridColumn(f.Key, f.Label, 46)));
        cols.Add(new GridColumn("s", "Lưu lúc", 130));
        _hm.Load(cols, _shown.Select(r => (IEnumerable<string?>)new[] { Dmy(r.Date), r.Batch, r.Note, r.Dat, r.KyHieu, Lan2Text(r) }
            .Concat(FieldKeys.Select(k => IsBlank(r.Inputs.GetValueOrDefault(k)) ? "ND" : r.Inputs[k].Trim()))
            .Append(KhangSinhService.SavedText(r.SavedAt)).ToArray()));
        _hg.Refresh();
        _hInfo.Text = Svc.Records.Count == 0 ? "Chưa có kết quả nào được lưu."
            : _matched.Count == 0 ? "Không có kết quả khớp bộ lọc."
            : day is null ? $"{_shown.Count:#,0} dòng · {_days.Count:#,0} ngày" : $"Ngày {Dmy(day)}: {_shown.Count:#,0} dòng · {_day.SelectedIndex + 1}/{_days.Count} ngày";
        if (_shown.Count > 0) { _hg.Select(0, 1); ShowDetail(_shown[0]); }
        else { _det.Children.Clear(); _det.Children.Add(Muted("Không có dòng nào.")); }
    }
    private IBrush? HBg(int r, int c)
    {
        if (r < 0 || r >= _shown.Count) return null;
        var row = _shown[r];
        if (c == 3) return row.Dat.ToUpperInvariant().Contains("KHÔNG ĐẠT") ? RedBg : ToneBg(Tone(row.Inputs));
        if (c == 5) return row.Kl is not null ? BlueBg : row.R.Lan2Latest is { } l ? ToneBg(Tone(InputsOf(l["inputs"]))) : null;
        if (c >= 6 && c < 6 + FieldKeys.Length && !IsBlank(row.Inputs.GetValueOrDefault(FieldKeys[c - 6]))) return FieldKeys[c - 6] == "aoz" ? RedBg : AmberBg;
        return null;
    }
    private void ShowDetail(Row r)
    {
        _det.Children.Clear();
        var fail = r.Dat.ToUpperInvariant().Contains("KHÔNG ĐẠT");
        _det.Children.Add(Label(r.Batch, bold: true, size: 15, fg: Dark));
        if (r.Note.Length > 0) _det.Children.Add(Muted(r.Note));
        _det.Children.Add(new Border { Background = fail ? RedBg : ToneBg(Tone(r.Inputs)) ?? GreenBg, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4), HorizontalAlignment = HorizontalAlignment.Left,
            Child = Label($"{(r.Dat.Length > 0 ? r.Dat : "—")}  {r.KyHieu}", bold: true, fg: fail ? Red : Dark) });
        _det.Children.Add(Label((r.Kl is not null ? $"Kiểm lại lần {r.LanNo} · ngày phân tích " : "Ngày ") + Dmy(r.Date)));
        _det.Children.Add(Label(Summary(r.Inputs), size: 12));
        if (r.Kl is null && r.R.MoTa.Length > 0) _det.Children.Add(Muted("Mô tả: " + r.R.MoTa));
        if (r.Kl is null && r.R.Lan1Src is { } s)
            _det.Children.Add(Muted($"Phiếu lần 1: {Str(s["file"])} · kiểm {Dmy(Str(s["ngayKiem"]))} · mẫu {Str(s["stt"])}" + (Str(s["trang"]).Length > 0 ? " · tr." + Str(s["trang"]) : "")));
        var lans = r.Kl is not null ? new List<System.Text.Json.Nodes.JsonObject> { r.Kl } : r.R.Lan2;
        if (lans.Count > 0)
        {
            _det.Children.Add(Label(r.Kl is not null ? "Phiếu kiểm lại" : $"Kiểm lại ({lans.Count} mẫu)", bold: true));
            foreach (var l in lans)
            {
                var p = new StackPanel { Spacing = 1, Margin = new Thickness(0, 0, 0, 4) };
                p.Children.Add(Label($"Lần {r.R.RoundOf(l)} · {Dmy(Str(l["date"]))} · {Str(l["dat"])} {Str(l["kyhieu"])}", bold: true, size: 12));
                if (Str(l["mau"]).Length > 0) p.Children.Add(Muted("Mẫu: " + Str(l["mau"])));
                p.Children.Add(Muted(Summary(InputsOf(l["inputs"]))));
                p.Children.Add(Muted($"Phiếu {Str(l["so"])} · mẫu {Str(l["stt"])}" + (Str(l["trang"]).Length > 0 ? " · tr." + Str(l["trang"]) : "") + (Str(l["file"]).Length > 0 ? " · " + Str(l["file"]) : "")
                    + (Str(l["suaTay"]) == "true" ? " · đã sửa tay" : "") + (Str(l["xacNhanTay"]) == "true" ? " · đã xác nhận" : "")));
                if (Str(l["ghichu"]).Length > 0) p.Children.Add(Muted("Ghi chú: " + Str(l["ghichu"])));
                _det.Children.Add(p);
            }
        }
        if (r.Kl is not null)
        {
            _det.Children.Add(Label("Lô gốc", bold: true));
            _det.Children.Add(Muted($"{r.ParentBatch} · nhập {Dmy(r.ParentDate)}" + (r.R.HasLan1 ? $" · lần 1: {r.R.Dat} {r.R.KyHieu}" : " · chưa có kết quả lần 1")));
        }
        _det.Children.Add(Muted("Lưu lúc " + KhangSinhService.SavedText(r.SavedAt)));
    }

    private async Task ExportAsync()
    {
        if (_shown.Count == 0) { await Ui.Message(this, "Không có dòng nào để xuất."); return; }
        var path = await Ui.SaveFile(this, "Xuất kết quả kháng sinh", Ui.SafeFileName($"Ket qua khang sinh {DateTime.Now:dd.MM.yyyy}") + ".xlsx", "xlsx", "Excel");
        if (path is null) return;
        var rows = _shown.ToList(); var mode = _mode;
        if (await Ui.Run(this, () => Svc.Export(path, rows, mode))) Ui.Exported(this, path);
    }
    private async Task DeleteSelected()
    {
        if (_hg.Row < 0 || _hg.Row >= _shown.Count) return;
        var r = _shown[_hg.Row];
        if (!await Ui.Confirm(this, r.Kl is not null ? $"Xóa kết quả kiểm lại lần {r.LanNo} của \"{r.Batch}\"?\n(kết quả lần 1 của lô giữ nguyên)" : $"Xóa bản ghi lô \"{r.Batch}\" ngày {Dmy(r.Date)}?", "Xóa dòng", "Xóa")) return;
        await Act(() => Svc.DeleteRow(r));
    }
    private async Task DeleteDay()
    {
        var day = _allDays.IsChecked != true && _day.SelectedIndex >= 0 && _day.SelectedIndex < _days.Count ? _days[_day.SelectedIndex] : null;
        if (day is null) { await Ui.Message(this, "Chọn 1 ngày (tắt \"Tất cả ngày\") rồi bấm lại."); return; }
        var mode = _mode;
        var n = BaseRows(Svc.Records, mode).Count(r => r.GroupDate == day);
        var msg = mode == ModeKl
            ? $"Xóa mọi kết quả KIỂM LẠI ngày phân tích {Dmy(day)} ({n} dòng)?\nKết quả lần 1 của các lô được GIỮ NGUYÊN. Không thể hoàn tác."
            : $"Xóa kết quả LẦN 1 của {n} lô, ngày kiểm {Dmy(day)}?\nLô có kết quả kiểm lại: giữ phần kiểm lại, chỉ bỏ lần 1. Không thể hoàn tác.";
        if (!await Ui.Confirm(this, msg, "Xóa dữ liệu ngày", "Xóa")) return;
        await Act(() => Svc.DeleteDay(mode, day));
    }
    private async Task DeletePhieu()
    {
        var list = Svc.CollectPhieu();
        if (list.Count == 0) { await Ui.Message(this, "Chưa có phiếu nào nhập từ PDF. Dữ liệu nhập tay / Excel không có thông tin phiếu — dùng \"Xóa dữ liệu ngày này\"."); return; }
        var pick = await Ui.Choose(this, "Chọn phiếu cần xóa. Phiếu lần 2: kết quả lần 1 của các lô giữ nguyên. Phiếu lần 1: kết quả lần 2 giữ nguyên.", "Xóa theo phiếu",
            list.Select(p => $"{p.Label} — {p.Samples.Count} {(p.Kind == 2 ? "mẫu" : "lô")}, {p.Recs.Count} dòng").ToList(), yes: "Xóa phiếu này");
        if (pick is null) return;
        var ph = list[pick.Value.Index];
        if (!await Ui.Confirm(this, $"Xóa {(ph.Kind == 2 ? "kết quả LẦN 2" : "kết quả LẦN 1")} của phiếu:\n{ph.Label}\n({ph.Recs.Count} dòng dữ liệu)?\n\nKhông thể hoàn tác.", "Xóa theo phiếu", "Xóa")) return;
        (int Del, int Upd) res = default;
        if (await Act(() => res = Svc.DeletePhieu(ph))) await Ui.Message(this, $"Đã xóa phiếu: {res.Del} dòng bị xóa hẳn, {res.Upd} dòng được giữ lại.");
    }
    private async Task DeleteMonth()
    {
        var kinds = new[] { "Dữ liệu nhập kiểm LẦN 1 (theo ngày kiểm)", "Dữ liệu KIỂM LẠI — lần 2 (theo ngày phân tích)" };
        var k = await Ui.Choose(this, "Xóa loại dữ liệu nào?", "Xóa theo tháng", kinds, _mode == ModeKl ? 1 : 0);
        if (k is null) return;
        var kind = k.Value.Index == 1 ? ModeKl : ModeL1;
        var months = Svc.Months(kind);
        if (months.Count == 0) { await Ui.Message(this, "Chưa có dữ liệu."); return; }
        var m = await Ui.Choose(this, kind == ModeL1 ? "Kết quả kiểm lại của các lô được giữ nguyên." : "Kết quả lần 1 của các lô được giữ nguyên.", "Xóa theo tháng",
            months.Select(x => $"{KhangSinhService.MonthVn(x.Month)} — {x.N} {(kind == ModeL1 ? "lô" : "mẫu kiểm lại")}").ToList(), yes: "Tiếp tục");
        if (m is null) return;
        var mo = months[m.Value.Index];
        if (!await Ui.Confirm(this, $"Xóa TOÀN BỘ {kinds[k.Value.Index]} của {KhangSinhService.MonthVn(mo.Month)}?\n({mo.N} {(kind == ModeL1 ? "lô" : "mẫu")}, {mo.Recs} dòng dữ liệu)\n\nKhông thể hoàn tác.", "Xóa theo tháng", "Xóa")) return;
        await Act(() => Svc.DeleteMonth(kind, mo.Month));
    }
    private async Task DeleteBad()
    {
        var n = Svc.CountBadDates();
        if (n == 0) { await Ui.Message(this, "Không có bản ghi nào bị lỗi ngày."); return; }
        if (!await Ui.Confirm(this, $"Xóa {n} bản ghi có Ngày không hợp lệ? Các bản ghi đúng không bị ảnh hưởng.", "Xóa dòng lỗi ngày", "Xóa")) return;
        await Act(() => Svc.DeleteBadDates());
    }
    private async Task ClearAll()
    {
        if (Svc.Records.Count == 0) return;
        var t = await Ui.Prompt(this, $"Xóa toàn bộ {Svc.Records.Count:#,0} bản ghi đã lưu? Không thể hoàn tác.\nGõ XOA để xác nhận:", "Xóa toàn bộ dữ liệu");
        if (t?.Trim().ToUpperInvariant() != "XOA") return;
        await Act(() => Svc.ClearAll());
    }

    // =====================================================================================
    // Tra cứu nhanh
    // =====================================================================================

    private Control BuildLookup()
    {
        var p = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        p.Children.Add(Label("Nhập chỉ tiêu kháng sinh", bold: true, size: 14));
        var grid = new WrapPanel { ItemSpacing = 10, LineSpacing = 8 };
        for (var i = 0; i < Fields.Length; i++)
        {
            var s = new StackPanel { Spacing = 2 }; s.Children.Add(Label(Fields[i].Label, bold: true)); s.Children.Add(_in[i]);
            _in[i].KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) DoLookup(); };
            grid.Children.Add(s);
        }
        p.Children.Add(grid);
        p.Children.Add(Muted("Để trống hoặc gõ ND nếu chỉ tiêu không phát hiện. Có phát hiện thì nhập số (vd: 8, 0.5, 12.3). Một chỉ tiêu ND vẫn được tính là đạt bất kỳ ngưỡng \"<X\" nào của quy định."));
        p.Children.Add(HRow(Btn("Tra cứu kết quả", DoLookup, primary: true), Btn("Nhập lại", () => { foreach (var t in _in) t.Text = ""; _res.IsVisible = false; })));
        p.Children.Add(_res);
        return new ScrollViewer { Content = p };
    }
    private Dictionary<string, string> Inputs() => FieldKeys.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => _in[x.i].Text ?? "");
    private void DoLookup()
    {
        var inputs = Inputs(); var row = Svc.Pick(inputs);
        var s = new StackPanel { Spacing = 6 };
        if (row is null)
        {
            _res.Background = AppTheme.Brush("#F3F3F3");
            s.Children.Add(Label("Không tìm thấy trường hợp phù hợp", bold: true, size: 16));
            s.Children.Add(Label($"Tổ hợp giá trị vừa nhập không khớp với bất kỳ dòng nào trong {Svc.Rules.Count} quy định. Kiểm tra lại giá trị đã nhập, hoặc xem tab Quy định kháng sinh."));
        }
        else
        {
            _res.Background = row.IsFail ? RedBg : GreenBg;
            s.Children.Add(Label(row.Dat, bold: true, size: 18, fg: row.IsFail ? Red : Dark));
            s.Children.Add(Label($"Trường hợp số {row.Stt} — Ký hiệu {row.KyHieu}", bold: true));
            s.Children.Add(Label("Chỉ tiêu đã nhập: " + Summary(inputs)));
            if (row.MoTa.Length > 0) s.Children.Add(Label("Mô tả quy định: " + row.MoTa));
            var notes = AutoPassNotes(row, inputs);
            if (notes.Count > 0) s.Children.Add(Label("Ghi chú:\n" + string.Join("\n", notes), size: 12));
        }
        _res.Child = s; _res.IsVisible = true;
    }

    // =====================================================================================
    // Nhập theo lô
    // =====================================================================================

    private static readonly GridColumn[] BCols = new[] { new GridColumn("d", "Ngày", 95), new GridColumn("b", "Mã / số lô hàng", 190), new GridColumn("n", "Ghi chú", 170) }
        .Concat(Fields.Select(f => new GridColumn(f.Key, f.Label, 62)))
        .Concat(new[] { new GridColumn("kq", "Kết quả", 200) { ReadOnly = true }, new GridColumn("kh", "Ký hiệu", 110) { ReadOnly = true } }).ToArray();
    private Control BuildBatch()
    {
        _bDate.Text = DateTime.Today.ToString("dd/MM/yyyy");
        _bDate.TextChanged += (_, _) => Recalc();
        _bm.Load(BCols, new List<string[]>());
        var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 6) };
        head.Children.Add(Muted("Gõ hoặc dán thẳng từ Excel (Ctrl+V) nhiều lô một lúc: Ngày, Mã / số lô, Ghi chú, rồi 7 chỉ tiêu (trống hoặc ND = không phát hiện). Kết quả và Ký hiệu tự tính ngay trên từng dòng; chỉ ghi vào Dữ liệu khi bấm \"Lưu vào Dữ liệu\"."));
        var acts = Wrap(Label("Ngày mặc định (ô Ngày để trống):"), _bDate);
        if (_edit) acts.Children.Add(Btn("Lưu vào Dữ liệu…", async () => await SaveBatch(), primary: true));
        acts.Children.Add(Btn("Xóa bảng", async () => { if (_bm.UsedRows() == 0 || await Ui.Confirm(this, "Xóa toàn bộ các dòng đang nhập (chưa lưu)?", "Xóa bảng", "Xóa")) { _bm.Load(BCols, new List<string[]>()); _bg.Refresh(); Recalc(); } }));
        head.Children.Add(acts); head.Children.Add(_bInfo);
        var d = new DockPanel(); DockPanel.SetDock(head, Dock.Top); d.Children.Add(head); d.Children.Add(Framed(_bg));
        Recalc();
        return d;
    }
    private List<(int Row, KhangSinhService.Entry E)> BatchEntries()
    {
        var o = new List<(int, KhangSinhService.Entry)>();
        var def = _bDate.Text ?? "";
        for (var r = 0; r < _bm.RowCount; r++)
        {
            var row = _bm.Row(r);
            string G(int c) => c < row.Count ? (row[c] ?? "").Trim() : "";
            if (Enumerable.Range(0, 10).All(c => G(c).Length == 0)) continue;
            var inputs = FieldKeys.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => G(3 + x.i));
            o.Add((r, Svc.MakeEntry(G(0).Length > 0 ? G(0) : def, G(1), G(2), inputs)));
        }
        return o;
    }
    private void Recalc()
    {
        if (_calc || !Svc.Loaded && _bm.RowCount == 0) { _bInfo.Text = ""; return; }
        _calc = true;
        try
        {
            var list = BatchEntries();
            _bRes = list.ToDictionary(x => x.Row, x => x.E);
            _bg.Refresh();
            var bad = list.Count(x => !IsIso(x.E.Date) || x.E.Batch.Length == 0);
            var dup = list.Count(x => IsIso(x.E.Date) && Svc.Loaded && Svc.Exists(x.E.Date, x.E.Batch));
            var fail = list.Count(x => x.E.Rule?.IsFail == true);
            _bInfo.Text = list.Count == 0 ? "Chưa có dòng nào." :
                $"{list.Count} lô · {fail} không đạt" + (bad > 0 ? $" · {bad} dòng thiếu lô / sai ngày" : "") + (dup > 0 ? $" · {dup} lô đã có trong Dữ liệu (cùng ngày)" : "");
        }
        finally { _calc = false; }
    }
    private IBrush? BBg(int r, int c)
    {
        if (c < 10) return null;
        if (!_bRes.TryGetValue(r, out var e)) return CalcBg;
        return e.Rule is null ? AmberBg : e.Rule.IsFail ? RedBg : GreenBg;
    }
    private async Task SaveBatch()
    {
        var list = BatchEntries();
        if (list.Count == 0) { await Ui.Message(this, "Bảng đang trống — chưa có gì để lưu."); return; }
        var dup = list.Where(x => Svc.Exists(x.E.Date, x.E.Batch)).Select(x => $"{x.E.Batch} ({Dmy(x.E.Date)})").ToList();
        var msg = $"Lưu {list.Count} lô vào Dữ liệu?" + (dup.Count > 0 ? $"\n\n{dup.Count} lô đã có kết quả cùng ngày ({string.Join(", ", dup.Take(5))}{(dup.Count > 5 ? "…" : "")}) — vẫn thêm dòng mới." : "");
        if (!await Ui.Confirm(this, msg, "Lưu kết quả", "Lưu")) return;
        var entries = list.Select(x => x.E).ToList();
        if (await Act(() => Svc.SaveEntries(entries), $"Đã lưu {entries.Count} lô vào Dữ liệu."))
        {
            _bm.Load(BCols, new List<string[]>()); _bg.Refresh(); Recalc();
        }
    }

    // =====================================================================================
    // Nhập từ Excel
    // =====================================================================================

    private Control BuildImport()
    {
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        p.Children.Add(Muted("Dùng khi đã có sẵn danh sách lô (có thể đã có kết quả) trong file Excel. Mỗi sheet được coi là dữ liệu của 1 ngày (đoán từ tên sheet, sửa được), hoặc lấy Ngày từ cột Ngày nếu file có. App tự bỏ dòng công ty / tiêu đề phía trên để tìm đúng dòng tiêu đề cột. File PDF (phiếu kết quả hoặc bảng): dùng tab Nạp PDF phiếu."));
        p.Children.Add(HRow(Btn("Chọn file Excel…", async () => await PickExcel(), primary: true), _xInfo));
        // 1.53: tách 2 tab con (trước 2 bảng nhỏ 170 / 260 px chồng nhau trong một trang cuộn)
        var t1 = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        t1.Children.Add(Label("Danh sách sheet (mỗi sheet = 1 ngày) — sửa Dòng tiêu đề / Ngày nếu cần; sheet không có Ngày sẽ bị bỏ qua", bold: true));
        t1.Children.Add(new Border { Height = 300, BorderBrush = Line_, BorderThickness = new Thickness(1), Child = _sg });
        t1.Children.Add(Label("Khớp cột dữ liệu (dùng chung cho mọi sheet) — Mã lô hàng là bắt buộc", bold: true));
        t1.Children.Add(_mapPanel);
        var t2 = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        t2.Children.Add(Label("Xem trước sheet đang chọn (chọn sheet ở tab 1)", bold: true));
        t2.Children.Add(new Border { Height = 520, BorderBrush = Line_, BorderThickness = new Thickness(1), Child = _pg });
        var run = Wrap(_skipDup);
        if (_edit) run.Children.Add(Btn("Nhập toàn bộ vào Dữ liệu", async () => await RunImport(), primary: true));
        t2.Children.Add(run);
        _xBody.Children.Add(new TabControl
        {
            Items = { new TabItem { Header = "1. Sheet & khớp cột", Content = t1 }, new TabItem { Header = "2. Xem trước & nhập", Content = t2 } },
        });
        p.Children.Add(_xBody);
        foreach (var t in ImportTargets)
        {
            var cb = new ComboBox { MinWidth = 150, FontSize = 12 };
            cb.SelectionChanged += (_, _) => { if (!_sLoading) ShowPreview(); };
            _mapBox[t.Key] = cb;
            var s = new StackPanel { Spacing = 2 }; s.Children.Add(Label(t.Label + (t.Required && t.Key == "batch" ? " *" : ""), size: 12)); s.Children.Add(cb);
            _mapPanel.Children.Add(s);
        }
        return new ScrollViewer { Content = p };
    }
    private async Task PickExcel()
    {
        var path = await Ui.OpenFile(this, "Chọn file Excel kết quả kháng sinh", "xlsx", "Excel");
        if (path is null) return;
        List<KhangSinhService.SheetIn>? sh = null;
        if (!await Ui.Run(this, () => sh = KhangSinhService.ReadExcel(path)) || sh is null) return;
        LoadSheets(sh, Path.GetFileName(path));
    }
    /// <summary>Đưa bảng (từ Excel, hoặc từ PDF dạng bảng) vào phần khớp cột của tab Nhập từ Excel.</summary>
    private void LoadSheets(List<KhangSinhService.SheetIn> sh, string label)
    {
        _sheets = sh; _file = label;
        _sLoading = true;
        _sm.Load(new[] { new GridColumn("s", "Sheet", 200) { ReadOnly = true }, new GridColumn("h", "Dòng tiêu đề", 95, "number"), new GridColumn("n", "Số dòng dữ liệu", 110) { ReadOnly = true }, new GridColumn("d", "Ngày", 110) },
            _sheets.Select(s => (IEnumerable<string?>)new[] { s.Name, (s.HeaderIdx + 1).ToString(), s.Rows.Count.ToString(), Dmy(s.Date) }));
        _sLoading = false;
        _sg.Refresh();
        _xBody.IsVisible = true;
        FillMapping(guess: true);
        _sg.Select(0, 0);
        ShowPreview();
        _xInfo.Text = $"{_file}: {_sheets.Count} sheet, {_sheets.Sum(s => s.Rows.Count):#,0} dòng dữ liệu.";
    }
    private void SheetsEdited()
    {
        for (var i = 0; i < _sheets.Count && i < _sm.RowCount; i++)
        {
            if (int.TryParse(_sm.Get(i, 1), out var h) && h >= 1 && h <= Math.Max(1, _sheets[i].Aoa.Count)) _sheets[i].HeaderIdx = h - 1;
            var d = _sm.Get(i, 3).Trim(); _sheets[i].Date = d.Length == 0 ? "" : ToIso(d);
        }
        _sLoading = true;
        _sm.SetCells(_sheets.Select((s, i) => (i, 2, s.Rows.Count.ToString())).Where(x => _sm.Get(x.i, 2) != x.Item3));
        _sLoading = false;
        FillMapping(guess: false); ShowPreview();
    }
    private KhangSinhService.SheetIn? CurSheet => _sheets.Count == 0 ? null : _sheets[Math.Clamp(_sg.Row, 0, _sheets.Count - 1)];
    private void FillMapping(bool guess)
    {
        var s = _sheets.FirstOrDefault(x => x.Rows.Count > 0) ?? _sheets.FirstOrDefault();
        var heads = s?.Headers ?? new List<string>();
        var items = new List<string> { "(không có)" }.Concat(heads.Select((h, i) => $"{GridModel.ColumnName(i)}: {(h.Length > 0 ? h : "(trống)")}")).ToList();
        var g = GuessMapping(heads);
        _sLoading = true;
        foreach (var t in ImportTargets)
        {
            var cb = _mapBox[t.Key]; var prev = cb.SelectedIndex;
            cb.ItemsSource = items;
            cb.SelectedIndex = guess || prev < 0 || prev >= items.Count ? g[t.Key] + 1 : prev;
        }
        _sLoading = false;
    }
    private Dictionary<string, int> Mapping() => ImportTargets.ToDictionary(t => t.Key, t => _mapBox[t.Key].SelectedIndex - 1);
    private void ShowPreview()
    {
        var s = CurSheet; if (s is null) return;
        var heads = s.Headers; var rows = s.Rows; var w = Math.Max(heads.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Length));
        _pm.Load(Enumerable.Range(0, w).Select(i => new GridColumn("c" + i, i < heads.Count && heads[i].Length > 0 ? heads[i] : GridModel.ColumnName(i), 100)), rows.Take(500).Select(r => (IEnumerable<string?>)r));
        _pg.Refresh();
    }
    private async Task RunImport()
    {
        var map = Mapping();
        if (map["batch"] < 0) { await Ui.Message(this, "Vui lòng khớp cột Mã lô hàng trước khi nhập."); return; }
        var with = _sheets.Where(s => s.Rows.Count > 0).ToList();
        var noDate = map["date"] < 0 ? with.Where(s => !IsIso(s.Date)).Select(s => s.Name).ToList() : new List<string>();
        var msg = $"Nhập {with.Sum(s => s.Rows.Count):#,0} dòng từ {with.Count} sheet vào Dữ liệu?" + (noDate.Count > 0 ? $"\n\n{noDate.Count} sheet chưa có Ngày sẽ bị BỎ QUA: {string.Join(", ", noDate)}" : "");
        if (!await Ui.Confirm(this, msg, "Nhập từ Excel", "Nhập")) return;
        KhangSinhService.ImportResult? res = null; var skip = _skipDup.IsChecked == true;
        if (!await Act(() => res = Svc.ImportSheets(with, map, skip)) || res is null) return;
        var txt = $"Hoàn tất: đã nhập {res.Ok:#,0}/{res.Total:#,0} dòng."
            + (res.SkippedSheets.Count > 0 ? $"\nBỏ qua {res.SkippedSheets.Count} sheet chưa gán Ngày ({string.Join(", ", res.SkippedSheets)})." : "")
            + (res.Dup > 0 ? $"\nBỏ qua {res.Dup} dòng trùng (đã có Ngày + Mã lô)." : "")
            + (res.Skip > 0 ? $"\nBỏ qua {res.Skip} dòng thiếu Mã lô / sai ngày:\n" + string.Join("\n", res.Details.Take(15)) + (res.Details.Count > 15 ? "\n…" : "") : "")
            + "\n\nXem tại tab Dữ liệu.";
        await Ui.Message(this, txt, "Nhập từ Excel");
    }

    // =====================================================================================
    // Quy định
    // =====================================================================================

    private static readonly GridColumn[] RCols = new[] { new GridColumn("stt", "STT", 45) { ReadOnly = true } }
        .Concat(Fields.Select(f => new GridColumn(f.Key, f.Label, 62)))
        .Concat(new[] { new GridColumn("dat", "Đạt theo thị trường", 230), new GridColumn("kh", "Ký hiệu", 120), new GridColumn("mt", "Mô tả", 380) }).ToArray();
    private Control BuildRules()
    {
        var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 6) };
        head.Children.Add(Muted("Toàn bộ các trường hợp dùng để tra cứu / tính kết quả — sửa thẳng trên lưới (thêm dòng ở cuối, xóa dòng bằng chuột phải). Ngưỡng ghi dạng ND, <10, <0.5, >0.3 (\">\" dùng cho trường hợp KHÔNG ĐẠT). Thay đổi áp dụng ngay cho Tra cứu nhanh, Nhập theo lô, Nhập Excel."));
        var acts = Wrap();
        if (_edit)
        {
            acts.Children.Add(Btn("Lưu quy định", async () => await SaveRules(), primary: true));
            acts.Children.Add(Btn("Bỏ thay đổi", () => { LoadRulesGrid(); }));
            acts.Children.Add(Btn("Khôi phục 83 trường hợp gốc…", async () => { if (await Ui.Confirm(this, "Bỏ mọi chỉnh sửa và dùng lại bảng quy định gốc (ksinhTT 26.05.25, 83 trường hợp)?", "Khôi phục quy định", "Khôi phục")) { if (await Act(Svc.ResetRules)) LoadRulesGrid(); } }));
        }
        else _rm.ReadOnly = true;
        acts.Children.Add(_rInfo);
        head.Children.Add(acts);
        var d = new DockPanel(); DockPanel.SetDock(head, Dock.Top); d.Children.Add(head); d.Children.Add(Framed(_rg));
        return d;
    }
    private void LoadRulesGrid()
    {
        _rm.Load(RCols, Svc.Rules.Select(r => (IEnumerable<string?>)new[] { r.Stt.ToString() }.Concat(FieldKeys.Select(k => r[k])).Concat(new[] { r.Dat, r.KyHieu, r.MoTa }).ToArray()));
        _rg.Refresh();
        _rDirty = false;
        _rInfo.Text = $"{Svc.Rules.Count} trường hợp" + (Svc.RulesCustom ? " (đã chỉnh sửa so với bảng gốc)" : " (bảng gốc)");
    }
    private async Task SaveRules()
    {
        if (!_rDirty) { await Ui.Message(this, "Chưa có thay đổi."); return; }
        var rules = new List<Rule>();
        for (var r = 0; r < _rm.RowCount; r++)
        {
            var row = _rm.Row(r);
            string G(int c) => c < row.Count ? (row[c] ?? "").Trim() : "";
            if (Enumerable.Range(1, 10).All(c => G(c).Length == 0)) continue;
            var x = new Rule { Dat = G(8), KyHieu = G(9), MoTa = G(10) };
            for (var i = 0; i < FieldKeys.Length; i++) x.C[FieldKeys[i]] = G(1 + i);
            rules.Add(x);
        }
        if (!await Ui.Confirm(this, $"Lưu bảng quy định ({rules.Count} trường hợp)? Số thứ tự được đánh lại 1, 2, 3…", "Lưu quy định", "Lưu")) return;
        if (await Act(() => Svc.SaveRules(rules))) LoadRulesGrid();
    }
}

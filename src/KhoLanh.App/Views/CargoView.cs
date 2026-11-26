using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Modules;
using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.Services;
using static KhoLanh.Core.PhieuKho.CargoCore;

namespace KhoLanh.App.Views;

/// <summary>
/// Cargo list + phiếu xuất kho cho Xuất Thuận Phước / Gia công / Gửi kho (C#, theo "Cargo list" của app cũ). Mỗi cont = 1 Cargo + 1 phiếu xuất kho (hàng hạn tạm, không LSX).
/// Mỗi dòng gõ Loại + Size-quy cách (0040-6) + Lô nguyên liệu (22.05.26 L16) + số kiện từng dãy → app tự ra mã hàng, size, quy cách và TRA kết quả kháng sinh theo (ngày, lô).
/// </summary>
public sealed class CargoView : UserControl
{
    private static CargoService Svc => App.Services.Cargo;
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45");
    private static readonly IBrush Warn = AppTheme.Brush("#B23B00");
    private static readonly IBrush CardBg = AppTheme.Brush("#F6F9FC");
    private static readonly IBrush BadBg = AppTheme.Brush("#FBD5C8");
    private static readonly IBrush OkBg = AppTheme.Brush("#E2EFDA");
    private static readonly IBrush WarnBg = AppTheme.Brush("#FFF2D6");
    /// <summary>1.88: cảnh báo từng dòng (mục, dòng) — tô ô Loại / Size-quy cách + chú thích, gom vào "Cần xử lý".</summary>
    private Dictionary<(int, int), CargoStock.LineWarn> _warn = new();

    private readonly bool _edit, _export;
    private bool _loading, _dirty;
    private CargoProfile _prof = Profiles[0];
    private CargoRec? _cur;
    private List<CargoRec> _list = new();
    // 1.82: danh sách cont nhóm theo tháng; mở cont → khung soạn phiếu chiếm cả trang (phương án B), "← Danh sách" / ESC để về.
    private readonly GridModel _cm = new() { ReadOnly = true };
    private readonly SheetGrid _cg;
    private readonly MonthGroups _groups = new("nd");
    private readonly TextBlock _listInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _curTitle = new() { FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Dark };
    private readonly TabControl _inner;
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _savedText = new(), _probText = new(), _stockText = new();
    private readonly Border _savedPill, _probPill, _stockPill;
    private readonly TextBlock _totals = new() { FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Dark, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _problems = new() { FontSize = 12, Foreground = AppTheme.Brush("#B42318"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _stockInfo = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _ksInfo = new() { Classes = { "muted" }, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
    private readonly Border _probBox, _stockBox;
    private readonly StackPanel _kindBar = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly GridModel _pm = new() { ReadOnly = true };
    private readonly SheetGrid _pg;
    private readonly ToggleButton _previewBtn = new() { Content = "Xem trước", Padding = new Thickness(10, 4), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _save, _exp;
    private readonly StackPanel _side = new();
    private readonly Border _main = new();
    private readonly Control _editPage;
    private int _openBlock;
    private TextBox? _soBox;
    private readonly List<(CargoBlock B, GridModel M)> _blocks = new();

    /// <summary>Nơi hiện khung soạn phiếu chiếm cả trang (PhieuKhoView gán); null = tự thay nội dung của chính view này.</summary>
    public Action<Control?>? FullHost { get; set; }

    public CargoView()
    {
        _edit = Svc.CanEdit; _export = Svc.CanExport;
        _pg = new SheetGrid(_pm) { SpareRows = 0 };
        _cg = new SheetGrid(_cm) { SpareRows = 0 };
        _cg.DoubleTapped += async (_, _) => await OpenSelected();
        _cg.AddHandler(KeyDownEvent, async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; await OpenSelected(); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _groups.Attach(_cg, () => RenderList());
        _savedPill = PhieuShell.Pill(_savedText, "ok"); _probPill = PhieuShell.Pill(_probText, "bad"); _stockPill = PhieuShell.Pill(_stockText, "warn");
        _probBox = new Border { Background = AppTheme.Brush("#FDE8E6"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Margin = new Thickness(0, 0, 0, 6), Child = _problems };
        _stockBox = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Margin = new Thickness(0, 0, 0, 6), Child = _stockInfo };

        foreach (var p in Profiles)
        {
            var b = new ToggleButton { Content = p.Key switch { "cargo" => "Thuận Phước", "cargogc" => "Gia công", _ => "Gửi kho" }, Padding = new Thickness(14, 5), FontSize = 13, Tag = p.Key };
            b.Click += async (_, _) => { if (!_loading) await SwitchProfile(p); };
            _kindBar.Children.Add(b);
        }
        _save = PhieuShell.Btn("Lưu cont", async () => await DoSave(), primary: true); _save.IsEnabled = _edit;
        _exp = PhieuShell.Btn("Xuất Excel", async () => await DoExport()); _exp.IsEnabled = _export; ToolTip.SetTip(_exp, "Xuất Excel (Cargo list + phiếu xuất kho)");
        var ks = PhieuShell.Btn("Tra lại KS", async () => await ReloadRefs()); ToolTip.SetTip(ks, "Tra lại kết quả kháng sinh (sau khi nhập ở module Kết quả kháng sinh)");
        _previewBtn.IsCheckedChanged += (_, _) => ShowMain();
        ToolTip.SetTip(_previewBtn, "Xem phiếu xuất kho sẽ xuất ra (theo thị trường). Bấm lần nữa để về nhập Cargo.");
        var more = new List<(string, Func<Task>, bool)> { ("Dán thông tin xe…", PasteXe, false) };
        if (_edit) { more.Add(("-", () => Task.CompletedTask, false)); more.Add(("Xóa cont", DoDelete, true)); }

        // Danh sách: chọn loại (Thuận Phước · Gia công · Gửi kho) + bảng cont nhóm theo tháng; bấm đúp / Enter / "Mở cont" → khung soạn phiếu.
        var listTools = new WrapPanel { ItemSpacing = 6, LineSpacing = 4, Margin = new Thickness(0, 0, 0, 6) };
        listTools.Children.Add(_kindBar);
        if (_edit) listTools.Children.Add(Btn("➕ Cont mới", async () => await NewCont(), primary: true));
        listTools.Children.Add(Btn("Mở cont đang chọn", async () => await OpenSelected()));
        foreach (var b in _groups.ToolButtons(() => RenderList())) listTools.Children.Add(b);
        listTools.Children.Add(_listInfo);
        var listPage = new DockPanel();
        DockPanel.SetDock(listTools, Dock.Top); listPage.Children.Add(listTools); listPage.Children.Add(Framed(_cg));

        // Khung soạn phiếu: thanh trên · bên trái Kiện / dãy + mục hàng (mục đang sửa mở to) · bên phải thông tin cont, tổng, cần xử lý.
        var status = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = true };
        status.Children.Add(_savedPill); status.Children.Add(_probPill); status.Children.Add(_stockPill); status.Children.Add(_status);
        var bar = PhieuShell.TopBar(CloseEditor, _curTitle, status, new Control[] { _save, _exp, ks, _previewBtn, PhieuShell.More(more.ToArray()), PhieuShell.CloseButton(CloseEditor) });
        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top); page.Children.Add(bar);
        page.Children.Add(PhieuShell.Body(_main, "Thông tin cont", _side, "nd"));
        PhieuShell.HandleEsc(page, CloseEditor);
        _editPage = page;

        _inner = Ui.SubTabs(("Danh sách phiếu nội địa", listPage), ("Danh mục", BuildCfgTab()));
        Content = _inner;
        ShowEditor();
        AttachedToVisualTree += async (_, _) => await LoadAsync();
    }

    /// <summary>Đang mở khung soạn phiếu.</summary>
    internal bool EditorOpen { get; private set; }

    private void ShowFull(Control? c)
    {
        EditorOpen = c is not null;
        if (FullHost is { } h) h(c); else Content = c ?? _inner;
    }

    // ------------------------------------------------------------------ chung

    private static Button Btn(string text, Func<Task> click, bool primary = false, bool danger = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary"); if (danger) b.Classes.Add("danger");
        b.Click += (_, _) => { _ = click(); };
        return b;
    }
    private static Border Framed(Control c) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = c };
    private static Border Section(string title, Control body)
    {
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = Dark }); s.Children.Add(body);
        return new Border { Background = CardBg, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = s };
    }
    private void Dirty() { if (_loading) return; _dirty = true; RefreshAll(); }
    private TextBox Tb(string value, Action<string> set, double w = 150, string? watermark = null)
    {
        var tb = new TextBox { Text = value, Width = w, FontSize = 12.5, IsReadOnly = !_edit, Watermark = watermark }; var last = value;
        tb.TextChanged += (_, _) => { var t = tb.Text ?? ""; if (_loading || t == last) return; last = t; set(t); Dirty(); };
        return tb;
    }
    private ComboBox Cb(IEnumerable<string> items, string value, Action<string> set, double w = 150)
    {
        var l = items.ToList(); var cb = new ComboBox { ItemsSource = l, SelectedIndex = Math.Max(0, l.IndexOf(value)), Width = w, FontSize = 12.5, IsEnabled = _edit }; var cur = value;
        cb.SelectionChanged += (_, _) => { if (_loading || cb.SelectedItem is not string s || s == cur) return; cur = s; set(s); Dirty(); };
        return cb;
    }

    private async Task LoadAsync()
    {
        _status.Text = "Đang tải…";
        try { await Task.Run(() => { Svc.LoadRefs(); _list = Svc.List(_prof); }); }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        _status.Text = "";
        RefreshKindBar(); RenderList(); RenderCfg();
    }

    private async Task ReloadRefs()
    {
        try { await Task.Run(Svc.LoadRefs); } catch (Exception e) { await Ui.Message(this, e is BusinessException ? e.Message : Ui.Describe(e)); return; }
        RefreshAll(); _status.Text = $"Đã tra lại kết quả kháng sinh lúc {DateTime.Now:HH:mm:ss}.";
    }

    private void RefreshKindBar() { _loading = true; foreach (var b in _kindBar.Children.OfType<ToggleButton>()) b.IsChecked = (string?)b.Tag == _prof.Key; _loading = false; }

    private async Task SwitchProfile(CargoProfile p)
    {
        if (p == _prof) { RefreshKindBar(); return; }
        if (_dirty && !await Ui.Confirm(this, "Cont đang soạn chưa lưu. Bỏ thay đổi và chuyển loại?", "Chưa lưu", "Bỏ thay đổi")) { RefreshKindBar(); return; }
        _prof = p; _cur = null; _dirty = false;
        await LoadAsync(); ShowEditor();
    }

    private void RenderList(string? keepId = null)
    {
        var force = keepId ?? _cur?.Id;   // cont vừa lưu / đang mở → mở tháng của nó
        keepId = force ?? (_groups.ItemAt(_cg.Row) is var ki && ki >= 0 && ki < _list.Count ? _list[ki].Id : null);
        if (force is { Length: > 0 } && _list.Find(c => c.Id == force) is { } kc) _groups.Open(MonthGroups.KeyOf(kc.Ngay));
        static double Kien(CargoRec c) => c.Blocks.Sum(b => b.Lines.Sum(LineTotal));
        var rows = _groups.Build(_list, c => c.Ngay, c => new[]
        {
            "   " + (c.SoPhieu.Length > 0 ? c.SoPhieu : "(chưa số)"), c.Ngay.Length >= 10 ? $"{c.Ngay[8..10]}/{c.Ngay[5..7]}/{c.Ngay[..4]}" : c.Ngay, c.ContSo, c.NguoiNhan,
            c.ContXe, c.Seal, c.Kho, Kien(c).ToString("#,0.##", Ui.Vi),
        }, (key, list, open) => new[] { (open ? "▾ " : "▸ ") + MonthGroups.Label(key), $"{list.Count:#,0} cont", "", "", "", "", "", list.Sum(Kien).ToString("#,0.##", Ui.Vi) });
        _cm.Load(new[]
        {
            new GridColumn("so", "Số phiếu", 140), new GridColumn("ngay", "Ngày xuất", 95), new GridColumn("cont", "Cont số", 80), new GridColumn("nhan", "Người nhận hàng", 190),
            new GridColumn("xe", "Cont / Xe", 150), new GridColumn("seal", "Seal", 110), new GridColumn("kho", "Kho xuất", 120), new GridColumn("kien", "Tổng kiện", 90, "number"),
        }, rows);
        _cg.Refresh();
        var i = keepId is null ? -1 : _list.FindIndex(c => c.Id == keepId);
        var r = i >= 0 ? _groups.RowOf(i) : -1;
        if (r >= 0) _cg.Select(r, 0);
        _listInfo.Text = $"{_list.Count:#,0} cont {_prof.Ten.Replace("Cargo ", "")} · {_groups.RowKey.Distinct().Count()} tháng · bấm dòng tháng để mở / thu · bấm đúp cont để mở";
    }

    private async Task OpenSelected()
    {
        var row = _cg.Row;
        if (_groups.IsHeader(row)) { _groups.Toggle(_groups.RowKey[row]); RenderList(); return; }
        var i = _groups.ItemAt(row); if (i < 0 || i >= _list.Count) return;
        var rec = _list[i];
        if (_dirty && _cur is not null && _cur.Id == rec.Id) { ShowFull(_editPage); return; }   // đang soạn đúng cont này → giữ thay đổi
        if (_dirty && _cur is not null && !await Ui.Confirm(this, "Cont đang soạn chưa lưu. Bỏ thay đổi và mở cont khác?", "Chưa lưu", "Bỏ thay đổi")) return;
        _cur = CargoCore.Clone(rec); _dirty = false; _openBlock = 0; _previewBtn.IsChecked = false; ShowEditor();
        ShowFull(_editPage);
    }

    private async Task NewCont()
    {
        if (_dirty && !await Ui.Confirm(this, "Cont đang soạn chưa lưu. Bỏ thay đổi và tạo cont mới?", "Chưa lưu", "Bỏ thay đổi")) return;
        _cur = Svc.NewRec(_prof, _list); _dirty = true; _openBlock = 0; _previewBtn.IsChecked = false;
        ShowEditor();
        ShowFull(_editPage);
    }

    /// <summary>"← Danh sách" / "✕ Đóng phiếu" / ESC: chưa lưu thì hỏi Lưu rồi đóng · Bỏ thay đổi · Ở lại.</summary>
    private async Task CloseEditor()
    {
        if (_dirty && _cur is not null && _edit)
        {
            var a = await PhieuShell.AskClose(this, "Cont " + (_cur.SoPhieu.Trim().Length > 0 ? _cur.SoPhieu.Trim() : "mới"));
            if (a == 2) return;
            if (a == 0) { if (_cur.SoPhieu.Trim().Length == 0) { await Ui.Message(this, "Chưa nhập Số phiếu."); return; } await DoSave(); if (_dirty) return; }
        }
        var keep = _cur?.Id;
        _cur = null; _dirty = false;
        ShowEditor(); ShowFull(null);
        RenderList(keep is { Length: > 0 } ? keep : null);
    }

    // ------------------------------------------------------------------ form (phương án B)

    private void ShowEditor()
    {
        _side.Children.Clear(); _blocks.Clear(); _soBox = null; _main.Child = null;
        if (_cur is null) { RefreshAll(); return; }
        _loading = true; var c = _cur;
        _openBlock = Math.Clamp(_openBlock, 0, Math.Max(0, c.Blocks.Count - 1));
        var F = PhieuShell.Field; var P = PhieuShell.Pair;
        var date = new CalendarDatePicker { SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy", IsEnabled = _edit };
        if (DateTime.TryParse(c.Ngay, out var dt)) date.SelectedDate = dt;
        date.SelectedDateChanged += (_, _) =>
        {
            if (_loading || date.SelectedDate is not { } d || d.ToString("yyyy-MM-dd") == c.Ngay) return;
            c.Ngay = d.ToString("yyyy-MM-dd");
            if (c.SoPhieu.Length == 0 || (c.SoPhieuTuCap.Length > 0 && c.SoPhieu == c.SoPhieuTuCap) && c.Id.Length == 0)
            { c.SoPhieu = NextSo(_list, c.Ngay, c.Id, _prof.Code); c.SoPhieuTuCap = c.SoPhieu; _loading = true; if (_soBox is not null) _soBox.Text = c.SoPhieu; _loading = false; }
            Dirty();
        };
        _soBox = Tb(c.SoPhieu, v => c.SoPhieu = v); _soBox.Width = double.NaN;
        var so = new DockPanel();
        if (_edit) { var re = new Button { Content = "↻", Padding = new Thickness(7, 3), Margin = new Thickness(4, 0, 0, 0) }; ToolTip.SetTip(re, $"Cấp lại số phiếu tự động ({_prof.SoEg})"); re.Click += (_, _) => { c.SoPhieu = NextSo(_list, c.Ngay, c.Id, _prof.Code); c.SoPhieuTuCap = c.SoPhieu; _soBox.Text = c.SoPhieu; Dirty(); }; DockPanel.SetDock(re, Dock.Right); so.Children.Add(re); }
        so.Children.Add(_soBox);
        _side.Children.Add(P(F("Ngày xuất", date), F("Số phiếu", so)));
        _side.Children.Add(F("Kho xuất", Cb(new[] { "An An", "Long An", "An An + Long An" }, c.Kho, v => c.Kho = v)));
        _side.Children.Add(F("Người nhận hàng", Tb(c.NguoiNhan, v => c.NguoiNhan = v)));
        _side.Children.Add(F("Cont / Xe", Tb(c.ContXe, v => c.ContXe = v)));
        _side.Children.Add(F("Seal", Tb(c.Seal, v => c.Seal = v)));
        if (_prof.Gk) _side.Children.Add(P(F("Kho nhận (gửi kho)", Tb(c.KhoNhan, v => c.KhoNhan = v)), F("Cont gửi kho số", Tb(c.ContSo, v => c.ContSo = v))));
        _side.Children.Add(SideHead("Tổng")); _side.Children.Add(_totals);
        _side.Children.Add(SideHead("Cần xử lý"));
        _side.Children.Add(_probBox); _side.Children.Add(_stockBox); _side.Children.Add(_ksInfo);
        ShowMain();
        _loading = false; RefreshAll();
    }

    private static TextBlock SideHead(string t) => new() { Text = t, FontWeight = FontWeight.Bold, FontSize = 13, Foreground = Dark, Margin = new Thickness(0, 8, 0, 4) };

    /// <summary>Phần bên trái: nhập Cargo (Kiện / dãy + mục hàng) hoặc phiếu xem trước.</summary>
    private void ShowMain()
    {
        if (_cur is null) { _main.Child = null; return; }
        if (_previewBtn.IsChecked == true) { RenderPreview(); _main.Child = PhieuShell.Card(Framed(_pg), new Thickness(8)); return; }
        var wasLoading = _loading; _loading = true;
        _blocks.Clear();
        var c = _cur;
        Control? footer = null;
        if (_edit)
        {
            var add = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            add.Children.Add(Btn("➕ Thêm mục kháng sinh (HLSO / HOSO…)", async () => { c.Blocks.Add(new CargoBlock { Loai = "ks", Id = NewId() }); _openBlock = c.Blocks.Count - 1; _dirty = true; ShowEditor(); await Task.CompletedTask; }));
            add.Children.Add(Btn("➕ Thêm mục khác (tôm vụn, sauce…)", async () => { c.Blocks.Add(new CargoBlock { Loai = "khac", Id = NewId() }); _openBlock = c.Blocks.Count - 1; _dirty = true; ShowEditor(); await Task.CompletedTask; }));
            footer = add;
        }
        _main.Child = PhieuShell.Stack(BuildCapRow(c), c.Blocks.Count, _openBlock, (i, open) => BuildBlock(c, i, open), footer);
        _loading = wasLoading;
    }

    private void OpenBlock(int i) { if (i == _openBlock) return; _openBlock = i; ShowMain(); }

    private static string NewId() => "b" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString("x") + Guid.NewGuid().ToString("N")[..3];

    /// <summary>1.71: hàng "KIỆN / DÃY" — số kiện thực tế từng dãy, dùng chung cả cont (dán / kéo nhiều ô như Excel). Gõ Tổng ở dòng mục hàng → app đổ đầy theo hàng này.</summary>
    private Control BuildCapRow(CargoRec c)
    {
        var gm = new GridModel { ReadOnly = !_edit };
        var cols = new List<GridColumn> { new("lb", "", 110) { ReadOnly = true }, new("sum", "Tổng", 70, "number") { ReadOnly = true } };
        for (var d = 1; d <= NDay; d++) cols.Add(Day(d));
        string[] Row() => new[] { "KIỆN / DÃY", "" }.Concat(Enumerable.Range(1, NDay).Select(d => c.DayCap.GetValueOrDefault(d.ToString()) ?? "")).ToArray();
        gm.Load(cols, new[] { Row() });
        // 1.88: hàng nhập 1 dòng — không áp tối thiểu 8 dòng (bảng cao 8 dòng đẩy khung soạn phiếu tràn, trang cuộn khuất thanh trên)
        var grid = new SheetGrid(gm) { SpareRows = 0, Filterable = false, ShowTotals = false, Sortable = false, MinRows = 0 };
        grid.Display = (r, col, raw) => col == 1 ? (Caps(c).Values.Sum() is var t && t > 0 ? t.ToString("#,0", Ui.Vi) : "") : raw;
        grid.Height = SheetGrid.HeadH + SheetGrid.RowH + ScrollH + 4;
        gm.Changed += () =>
        {
            if (_loading) return;
            if (gm.RowCount > 1) return;
            var cap = new Dictionary<string, string>();
            for (var d = 1; d <= NDay; d++) { var v = gm.Get(0, 1 + d).Trim(); if (v.Length > 0 && Ui.TryParseNumber(v, out var n) && n > 0) cap[d.ToString()] = ((int)Math.Floor(n)).ToString(System.Globalization.CultureInfo.InvariantCulture); }
            c.DayCap = cap; grid.Refresh(); Dirty();
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (_edit)
        {
            bar.Children.Add(Btn("Điền nhanh…", async () =>
            {
                var t = await Ui.Prompt(this, "Số dãy × số kiện mỗi dãy (từ dãy 1). Ví dụ: 20x63 = dãy 1 đến 20, mỗi dãy 63 kiện.", "KIỆN / DÃY", "");
                if (t is null) return;
                var m = System.Text.RegularExpressions.Regex.Match(t.Trim(), @"^(\d+)\s*[x×*]\s*(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!m.Success || int.Parse(m.Groups[1].Value) is < 1 or > NDay) { await Ui.Message(this, $"Gõ theo dạng số dãy × số kiện, ví dụ 20x63 (tối đa {NDay} dãy)."); return; }
                var n = int.Parse(m.Groups[1].Value); var k = m.Groups[2].Value;
                gm.SetCells(Enumerable.Range(1, NDay).Select(d => (0, 1 + d, d <= n ? k : "")).ToList());
            }));
        }
        // 1.82: gọn 1 dòng đầu (ghi chú dài vào chú thích khi rê chuột)
        var note = new TextBlock { Text = "— gõ / dán số kiện thực tế từng dãy (dùng chung cả cont)", Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "keep", Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(note, "Gõ / dán số kiện thực tế từng dãy (dùng chung cả cont). Ở mục hàng, gõ số vào cột Tổng → app tự đổ đầy từ dãy 1; dãy đã có hàng của dòng khác chỉ nhận phần còn trống.");
        var head = PhieuShell.Row((PhieuShell.H("Kiện / dãy"), "Auto"), (note, "*"), (bar, "Auto"));
        head.Margin = new Thickness(0, 0, 0, 5);
        var body = new StackPanel(); body.Children.Add(head); body.Children.Add(Framed(grid));
        return PhieuShell.Card(body, new Thickness(10, 6, 10, 8));
    }

    private static GridColumn Day(int d) => new("d" + d, d.ToString(), 46, "number");

    private Control BuildBlock(CargoRec c, int bi, bool open)
    {
        var b = c.Blocks[bi]; var ks = b.Loai != "khac";
        Button? rm = null;
        if (_edit)
        {
            rm = new Button { Content = "Xóa", Padding = new Thickness(8, 3) }; rm.Classes.Add("danger");
            ToolTip.SetTip(rm, $"Xóa mục hàng {bi + 1}");
            rm.Click += async (_, _) =>
            {
                if (b.Lines.Any(l => !IsEmptyLine(l)) && !await Ui.Confirm(this, $"Xóa mục hàng {bi + 1}" + (b.Ten.Length > 0 ? $" ({b.Ten})" : "") + "?", "Xóa mục hàng", "Xóa")) return;
                c.Blocks.Remove(b); if (c.Blocks.Count == 0) c.Blocks.Add(new CargoBlock { Loai = "ks" }); if (_openBlock >= bi && _openBlock > 0) _openBlock--; _dirty = true; ShowEditor();
            };
        }
        var kind = new Border { Child = new TextBlock { Text = ks ? "kháng sinh" : "khác" }, Classes = { "pill", ks ? "info" : "accent" }, Padding = new Thickness(7, 1), CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center };
        var tog = PhieuShell.Toggle($"Mục hàng {bi + 1}", open, () => OpenBlock(bi));
        if (!open)
        {
            var used = b.Lines.Where(l => !IsEmptyLine(l)).ToList();
            var info = new TextBlock
            {
                Text = (b.Ten.Length > 0 ? b.Ten : "(chưa có tên mục)") + (b.Ma.Length > 0 ? " " + b.Ma : "") + $"  —  {used.Count} dòng · {used.Sum(LineTotal).ToString("#,0.##", Ui.Vi)} kiện",
                Classes = { "muted" }, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var cells = new List<(Control, string)> { (tog, "Auto"), (kind, "Auto"), (info, "*") };
            if (rm is not null) cells.Add((rm, "Auto"));
            var card = PhieuShell.Card(PhieuShell.Row(cells.ToArray()), new Thickness(8, 4));
            card.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            card.PointerPressed += (_, e) => { if (e.Source is not Button && (e.Source as Visual)?.GetVisualAncestors().OfType<Button>().Any() != true) OpenBlock(bi); };
            return card;
        }
        var ten = Tb(b.Ten, v => b.Ten = v, watermark: ks ? "Tên mục (VD HLSO)" : "Tên mục (VD TÔM VỤN)"); ToolTip.SetTip(ten, "Tên mục");
        var ma = Tb(b.Ma, v => b.Ma = v, watermark: "Mã (ghi sau tên)"); ToolTip.SetTip(ma, "Mã (ghi sau tên)");
        var tag = Tb(b.Tag, v => b.Tag = v, watermark: "Ghi chú sau tên"); ToolTip.SetTip(tag, "Ghi chú sau tên");
        var headCells = new List<(Control, string)> { (tog, "Auto"), (kind, "Auto"), (ten, "*"), (ma, "0.7*"), (tag, "0.9*") };
        if (_edit)
        {
            // 1.72: chọn từ Tồn kho An An theo SIZE + ĐẶC TÍNH (không theo lô) → thêm dòng, số kiện đổ đầy theo KIỆN / DÃY
            var pick = Btn("Chọn từ tồn…", async () => await PickFromStock(c, bi));
            ToolTip.SetTip(pick, "Tồn kho An An gộp theo Size + Đặc tính + Quy cách (không tách lô): tích / gõ SL xuất → thêm dòng vào mục này, Lô NL để trống để gõ sau.");
            headCells.Add((pick, "Auto"));
        }
        if (rm is not null) headCells.Add((rm, "Auto"));
        var head = PhieuShell.Row(headCells.ToArray());
        ToolTip.SetTip(kind, ks
            ? "Mỗi dòng: Loại (BTP/NL) + Size-quy cách + Lô nguyên liệu; app tự ra mã hàng và tra kết quả kháng sinh. Ô đỏ = còn lỗi (chưa có kết quả…), xanh = đã ra thị trường. Số kiện gõ vào cột dãy 1–40 (dán được từ Excel), hoặc gõ cột Tổng để tự đổ đầy theo Kiện / dãy."
            : "Hàng không kháng sinh: gõ tay Size + Quy cách; kiện vào cột “.” của phiếu.");

        var gm = new GridModel { ReadOnly = !_edit };
        var cols = new List<GridColumn>();
        if (ks)
        {
            cols.AddRange(new[] { new GridColumn("loai", "Loại", 55), new GridColumn("sn", "Size-quy cách (0040-6)", 130), new GridColumn("lo", "Lô NL (22.05.26 L16)", 130), new GridColumn("kstay", "KQ KS tay", 70),
                new GridColumn("size", "Size", 65) { ReadOnly = true }, new GridColumn("pack", "Quy cách", 80) { ReadOnly = true }, new GridColumn("mk", "Thị trường", 150) { ReadOnly = true },
                new GridColumn("ks", "Ký hiệu KS", 100) { ReadOnly = true }, new GridColumn("ma", "Mã hàng", 190) { ReadOnly = true }, new GridColumn("tong", "Tổng", 65, "number") { ReadOnly = !_edit } });
        }
        else
        {
            cols.AddRange(new[] { new GridColumn("size", "Size", 100), new GridColumn("pack", "Quy cách (10.0 kg x 1)", 150), new GridColumn("tong", "Tổng", 65, "number") { ReadOnly = !_edit } });
        }
        for (var d = 1; d <= NDay; d++) cols.Add(Day(d));
        gm.Load(cols, b.Lines.Select(l => RowOfLine(l, ks)));
        // 1.82: lưới giãn hết phần còn lại của trang (mục đang mở), luôn có sẵn dòng trống để gõ thêm.
        var grid = new SheetGrid(gm) { SpareRows = Math.Max(SpareOpen, MinRows - gm.RowCount) };
        grid.Display = (r, col, raw) =>
        {
            if (r >= b.Lines.Count) return raw;
            if (ks ? col is < 4 or >= 10 : col is < 2 or >= 3) return raw;
            var ln = b.Lines[r]; var res = ResolveLine(Svc.Cfg, b, ln, Svc.Ks);
            if (ks) return col switch { 4 => res.Size, 5 => res.Pack, 6 => res.Market, 7 => res.KsText, 8 => res.Ma, _ => LineTotal(ln) > 0 ? LineTotal(ln).ToString("0.###", Ui.Vi) : "" };
            return LineTotal(ln) > 0 ? LineTotal(ln).ToString("0.###", Ui.Vi) : "";
        };
        grid.CellBackground = (r, col) =>
        {
            if (ks && r < b.Lines.Count && col <= 1)
            {
                if (_warn.TryGetValue((bi, r), out var w)) return w.Bad ? BadBg : WarnBg;
                return ResolveLine(Svc.Cfg, b, b.Lines[r], Svc.Ks).SizeErrs > 0 ? BadBg : null;
            }
            if (!ks || r >= b.Lines.Count || col is < 4 or > 9) return null;
            var ln = b.Lines[r]; if (LineTotal(ln) <= 0 && ln.Sn.Trim().Length == 0) return null;
            var res = ResolveLine(Svc.Cfg, b, ln, Svc.Ks); return res.Errors.Count > 0 ? BadBg : (res.Market.Length > 0 ? OkBg : null);
        };
        grid.CellTip = (r, col) =>
        {
            if (!ks || r >= b.Lines.Count || col is > 1 and < 4 || col > 9) return null;
            if (_warn.TryGetValue((bi, r), out var w)) return "⚠ " + w.Text;
            var res = ResolveLine(Svc.Cfg, b, b.Lines[r], Svc.Ks);
            var errs = col <= 1 ? res.Errors.Take(res.SizeErrs) : res.Errors;
            return errs.Any() ? string.Join("\n", errs) : null;
        };
        gm.Changed += () => { _hintRows = null; if (_loading) return; SyncLines(b, gm, ks); grid.SpareRows = Math.Max(SpareOpen, MinRows - gm.RowCount); Dirty(); };
        if (ks && _edit)
        {
            // 1.128: gợi ý khi gõ — cột Loại (danh mục) · cột Size-quy cách (hàng còn tồn An An thuộc phân loại của Loại dòng đó)
            grid.Suggest = (col, text) =>
            {
                var cfg = Svc.Cfg;
                if (col == 0)
                    return cfg.Loai.Where(l => text.Trim().Length == 0 || l.Code.StartsWith(text.Trim(), StringComparison.OrdinalIgnoreCase))
                        .Select(l => new SheetGrid.GridSuggestion(l.Code, l.Code + (l.Ten.Length > 0 ? " · " + l.Ten : ""), "Phân loại An An: " + NhomText(NhomOf(cfg, l.Code)))).ToList();
                if (col != 1) return null;
                var hints = CargoStock.SuggestSn(HintRows(c), cfg, gm.Get(grid.Row, 0), text);
                return hints.Select(h => new SheetGrid.GridSuggestion(h.Sn, $"{h.Loai} {h.Sn}  ·  {h.Size}  ·  {h.Pack}",
                    $"Còn {h.Left.ToString("#,0.##", Ui.Vi)} kiện · {h.NhomLabel}" + (h.Dts.Count > 0 ? " · đặc tính " + string.Join(", ", h.Dts.Take(4)) + (h.Dts.Count > 4 ? "…" : "") : ""), h)).ToList();
            };
            grid.SuggestionPicked += (r, col, it) =>
            {
                if (col == 1 && it.Tag is CargoStock.SnHint h && !string.Equals(gm.Get(r, 0).Trim(), h.Loai, StringComparison.OrdinalIgnoreCase)) gm.Set(r, 0, h.Loai);
            };
        }
        _blocks.Add((b, gm));
        // 1.71: gõ TỔNG kiện của dòng → đổ đầy từng dãy từ dãy 1 theo hàng KIỆN / DÃY (dãy đã có hàng dòng khác chỉ nhận phần trống) — như app cũ
        var tongCol = ks ? 9 : 2; var dayFirst = ks ? 10 : 3;
        if (_edit) gm.Derive = ch =>
        {
            var hits = ch.Where(x => x.c == tongCol && x.after.Trim() != x.before.Trim()).ToList();
            if (hits.Count == 0 || _loading) return Array.Empty<(int, int, string)>();
            var res = new List<(int, int, string)>();
            SyncLines(b, gm, ks);
            var caps = Caps(c);
            foreach (var h in hits)
            {
                res.Add((h.r, tongCol, ""));   // ô Tổng chỉ để gõ — tổng thật luôn tính từ các dãy
                var txt = h.after.Trim();
                while (b.Lines.Count <= h.r) b.Lines.Add(new CargoLine());
                var ln = b.Lines[h.r];
                if (txt.Length == 0) { ln.Dai = new(); }
                else
                {
                    if (!Ui.TryParseNumber(txt, out var want) || want < 0) { _status.Text = "Tổng kiện phải là số."; continue; }
                    if (caps.Count == 0) { _status.Text = "Nhập số kiện từng dãy ở hàng “Kiện / dãy” (phía trên các mục hàng) rồi gõ lại Tổng."; continue; }
                    var r = RaiTong(c, bi, h.r, want, caps);
                    _status.Text = want <= 0 ? "Đã xóa số kiện của dòng." : $"Đã xếp {r.Placed:#,0} kiện vào {ln.Dai.Count} dãy" + (r.Left > 0 ? $" — CÒN DƯ {r.Left:#,0} kiện, hết dãy." : ".");
                }
                for (var d = 1; d <= NDay; d++) res.Add((h.r, dayFirst + d - 1, ln.Dai.GetValueOrDefault(d.ToString()) ?? ""));
            }
            return res;
        };
        var body = new DockPanel();
        head.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(head, Dock.Top); body.Children.Add(head);
        body.Children.Add(Framed(grid));
        return PhieuShell.Card(body, new Thickness(8, 6, 8, 8));
    }
    // ------------------------------------------------------------------ 1.72: Chọn từ tồn (size + đặc tính)

    /// <summary>Cửa sổ chọn hàng đang mở (công cụ chụp ảnh dùng).</summary>
    internal Window? PickerWindow { get; private set; }
    /// <summary>1.128: tồn An An gộp theo size (đã trừ cont đang soạn) cho ô gợi ý — tính lại khi lưới mục hàng đổi.</summary>
    private List<CargoStock.SizeRow>? _hintRows;
    private List<CargoStock.SizeRow> HintRows(CargoRec c)
    {
        if (_hintRows is not null) return _hintRows;
        try { _hintRows = Svc.StockBySize(c); } catch { _hintRows = new(); }
        return _hintRows;
    }
    /// <summary>1.128: nút lọc phân loại của cửa sổ Chọn từ tồn (công cụ chụp ảnh dùng).</summary>
    internal Seg? PickerSeg { get; private set; }

    private async Task PickFromStock(CargoRec c, int bi)
    {
        if (bi >= c.Blocks.Count || TopLevel.GetTopLevel(this) is not Window owner) return;
        var b = c.Blocks[bi]; var ks = b.Loai != "khac";
        if (Svc.StockRows.Count == 0) Svc.LoadStock();
        var rows = Svc.StockBySize(c);
        if (rows.Count == 0) { await Ui.Message(this, "Chưa có Tồn kho An An (hoặc tồn đều bằng 0) — nạp file ở module Tồn kho An An trước.", "Chọn từ tồn"); return; }
        const int CSl = 8;
        string N(double v) => v == 0 ? "" : v.ToString("#,0.##", Ui.Vi);
        var gm = new GridModel();
        gm.Load(new[]
        {
            new GridColumn("size", "Size", 80) { ReadOnly = true }, new GridColumn("dt", "Đặc tính", 140) { ReadOnly = true }, new GridColumn("pack", "Quy cách", 95) { ReadOnly = true },
            new GridColumn("sn", "Size-quy cách", 115) { ReadOnly = true }, new GridColumn("nma", "Số mã", 70, "number") { ReadOnly = true },
            new GridColumn("ton", "Tồn (kiện)", 95, "number") { ReadOnly = true }, new GridColumn("co", "Đã có trong cont này", 150, "number") { ReadOnly = true },
            new GridColumn("con", "Còn lại", 90, "number") { ReadOnly = true }, new GridColumn("sl", "SL xuất", 95, "number"),
            new GridColumn("nhom", "Phân loại", 140) { ReadOnly = true },
        }, rows.Select(r => new[] { r.Size, r.Dt, r.Pack.Length > 0 ? r.Pack : r.Tl is > 0 ? $"({r.Tl.Value.ToString("0.###", Ui.Vi)} kg)" : "", r.Sn, r.NMa.ToString(), N(r.Ton), N(r.Have), N(r.Left), "", r.NhomLabel }));
        var grid = new SheetGrid(gm) { SpareRows = 0, CheckCol = CSl, CheckFill = r => r < rows.Count ? (rows[r].Left > 0 ? rows[r].Left.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "") : "" };
        double Sl(int r) => Ui.TryParseNumber(gm.Get(r, CSl).Trim(), out var v) && v > 0 ? v : 0;
        grid.CellBackground = (r, col) => r < rows.Count && col == CSl && Sl(r) > rows[r].Left + 1e-9 ? BadBg : r < rows.Count && col == 3 && ks && rows[r].Sn.Length == 0 ? BadBg : null;
        var info = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var warn = new TextBlock { FontSize = 12.5, Foreground = Warn, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        void Info()
        {
            var sel = Enumerable.Range(0, Math.Min(gm.RowCount, rows.Count)).Where(r => Sl(r) > 0).ToList();
            info.Text = sel.Count == 0 ? "Chưa chọn dòng nào — tích ô đầu dòng (lấy hết phần còn lại) hoặc gõ SL xuất." : $"Đang chọn {sel.Count} dòng · {sel.Sum(Sl).ToString("#,0.##", Ui.Vi)} kiện";
            var over = sel.Where(r => Sl(r) > rows[r].Left + 1e-9).ToList();
            var noSn = ks ? sel.Where(r => rows[r].Sn.Length == 0).ToList() : new();
            var msgs = new List<string>();
            if (over.Count > 0) msgs.Add($"⚠ {over.Count} dòng vượt phần còn lại (vẫn thêm được): " + string.Join(", ", over.Take(4).Select(r => $"{rows[r].Size} {rows[r].Dt} — xuất {Sl(r).ToString("#,0.##", Ui.Vi)} / còn {rows[r].Left.ToString("#,0.##", Ui.Vi)}")));
            if (noSn.Count > 0) msgs.Add($"⚠ {noSn.Count} dòng chưa suy được Size-quy cách (quy cách chưa có trong Danh mục) — sẽ để trống để gõ tay.");
            warn.Text = string.Join("\n", msgs); warn.IsVisible = msgs.Count > 0;
            grid.Refresh();
        }
        gm.Changed += Info;
        var add = new Button { Content = "Thêm vào phiếu", Classes = { "primary" }, MinWidth = 150, HorizontalContentAlignment = HorizontalAlignment.Center };
        var close = new Button { Content = "Đóng", IsCancel = true };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; btns.Children.Add(close); btns.Children.Add(add);
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(btns, Dock.Right); bar.Children.Add(btns); bar.Children.Add(info);
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(new TextBlock { Text = $"Mục hàng {bi + 1} ({(ks ? "kháng sinh" : "khác")}) · {Svc.StockInfo}", FontSize = 12.5, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = "Tồn gộp theo Size + Đặc tính + Quy cách, KHÔNG tách lô. Thêm vào phiếu: mỗi dòng chọn thành 1 dòng" + (ks ? " (Loại + Size-quy cách + KQ KS theo đặc tính; Lô NL để trống để gõ sau)" : " (Size + Quy cách)") + ", số kiện đổ đầy từ dãy 1 theo hàng KIỆN / DÃY.",
            FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Tag = "keep" });
        // 1.128: lọc theo phân loại Tồn kho An An (mặc định: phân loại của Loại đang dùng nhiều nhất trong mục này)
        var nhoms = rows.Select(r => r.NhomLabel).Distinct().OrderBy(l => Array.IndexOf(KhoLanh.Core.KiemKe.PhanLoaiM1.Order, rows.First(r => r.NhomLabel == l).Nhom) is var i && i < 0 ? 99 : i).ToList();
        if (nhoms.Count > 1)
        {
            var seg = new Seg(new[] { $"Tất cả ({rows.Count})" }.Concat(nhoms.Select(l => $"{l} ({rows.Count(r => r.NhomLabel == l)})")).ToArray()) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
            ToolTip.SetTip(seg, "Phân loại theo tab Phân loại của Tồn kho An An. Loại hàng ↔ phân loại sửa ở tab Danh mục (cột Phân loại An An). Dòng đã nhập SL ở phân loại khác vẫn được thêm.");
            void Filt(int i) => grid.SetFilter(9, setAllowed: true, allowed: i <= 0 ? null : new HashSet<string> { nhoms[i - 1] });
            seg.Changed += Filt;
            var loaiDung = ks ? b.Lines.Where(l => l.Loai.Trim().Length > 0).GroupBy(l => l.Loai.Trim().ToUpperInvariant()).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() : null;
            var nhMd = loaiDung is null ? "" : NhomOf(Svc.Cfg, loaiDung);
            var idx = nhMd.Length == 0 ? -1 : nhoms.IndexOf(KhoLanh.Core.KiemKe.PhanLoaiM1.LabelOf(nhMd));
            if (idx >= 0) { seg.Selected = idx + 1; Filt(idx + 1); }
            PickerSeg = seg;
            top.Children.Add(seg);
        }
        top.Children.Add(warn);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(top); root.Children.Add(bar); root.Children.Add(Framed(grid));
        var scr = owner.Screens.ScreenFromWindow(owner)?.WorkingArea; var sc = owner.RenderScaling <= 0 ? 1 : owner.RenderScaling;
        var w = new Window
        {
            Title = "Chọn từ tồn — Tồn kho An An theo size + đặc tính",
            Width = Math.Max(860, Math.Min(1180, (scr?.Width ?? 1400) / sc * 0.85)), Height = Math.Max(520, (scr?.Height ?? 860) / sc * 0.8),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = AppTheme.Card, MinWidth = 700, MinHeight = 420,
            Content = new Border { Padding = new Thickness(14, 12), Child = root },
        };
        var ok = false;
        add.Click += (_, _) => { ok = true; w.Close(); };
        close.Click += (_, _) => w.Close();
        w.Opened += (_, _) => grid.Focus();
        PickerWindow = w; Info();
        await Ui.Show(this, w);
        PickerWindow = null;
        if (!ok) return;
        var pick = Enumerable.Range(0, Math.Min(gm.RowCount, rows.Count)).Select(r => (Row: rows[r], Sl: Sl(r))).Where(x => x.Sl > 0).ToList();
        if (pick.Count == 0) return;
        var msg = AddFromStock(c, bi, pick.Select(x => (x.Row, x.Sl)).ToList());
        _dirty = true; ShowEditor();
        _status.Text = msg;   // sau ShowEditor (RefreshAll ghi đè dòng trạng thái)
    }

    /// <summary>Thêm các dòng chọn từ tồn vào mục <paramref name="bi"/> (thay các dòng trống ở cuối), đổ đầy số kiện theo KIỆN / DÃY. Trả câu báo kết quả.</summary>
    internal static string AddFromStock(CargoRec c, int bi, List<(CargoStock.SizeRow Row, double Sl)> pick)
    {
        var b = c.Blocks[bi]; var ks = b.Loai != "khac";
        while (b.Lines.Count > 0 && IsEmptyLine(b.Lines[^1])) b.Lines.RemoveAt(b.Lines.Count - 1);
        var caps = Caps(c); var noCap = caps.Count == 0; double left = 0; var noSn = 0;
        foreach (var (row, sl) in pick)
        {
            var (ln, okSn) = CargoStock.LineFor(row, ks); if (!okSn) noSn++;
            b.Lines.Add(ln);
            var want = Math.Floor(sl);
            if (noCap) { if (want > 0) ln.Dai["1"] = want.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            else left += RaiTong(c, bi, b.Lines.Count - 1, want, caps).Left;
        }
        var msg = $"Đã thêm {pick.Count} dòng từ tồn vào mục {bi + 1} ({pick.Sum(x => Math.Floor(x.Sl)).ToString("#,0", Ui.Vi)} kiện).";
        if (noCap) msg += " Chưa có hàng KIỆN / DÃY nên số kiện đặt hết vào dãy 1 — nhập KIỆN / DÃY rồi gõ lại Tổng để đổ đầy.";
        else if (left > 0) msg += $" CÒN DƯ {left.ToString("#,0", Ui.Vi)} kiện chưa xếp được (hết chỗ các dãy).";
        if (noSn > 0) msg += $" {noSn} dòng chưa có Size-quy cách (quy cách chưa có trong Danh mục) — gõ tay.";
        if (ks) msg += " Nhớ gõ Lô NL cho từng dòng.";
        return msg;
    }

    /// <summary>Lưới mục hàng: tối thiểu số dòng hiện (kể cả dòng trống), số dòng trống luôn có ở cuối, chỗ cho thanh cuộn ngang.</summary>
    internal const int MinRows = 10, MinSpare = 3, SpareOpen = 12; private const double ScrollH = 14;

    private static string[] RowOfLine(CargoLine l, bool ks)
    {
        var lead = ks ? new[] { l.Loai, l.Sn, l.Lo, l.KsTay, "", "", "", "", "", "" } : new[] { l.Size, l.Pack, "" };
        return lead.Concat(Enumerable.Range(1, NDay).Select(d => l.Dai.GetValueOrDefault(d.ToString()) ?? "")).ToArray();
    }

    /// <summary>Chép lưới vào các dòng (giữ trường lạ theo vị trí dòng); bỏ các dòng trống ở cuối nhưng luôn giữ ít nhất 1 dòng.</summary>
    private static void SyncLines(CargoBlock b, GridModel gm, bool ks)
    {
        var old = b.Lines; var res = new List<CargoLine>(); var first = ks ? 10 : 3;
        for (var r = 0; r < gm.RowCount; r++)
        {
            var l = ks
                ? new CargoLine { Loai = gm.Get(r, 0).Trim().ToUpperInvariant(), Sn = gm.Get(r, 1).Trim(), Lo = gm.Get(r, 2).Trim(), KsTay = gm.Get(r, 3).Trim() }
                : new CargoLine { Size = gm.Get(r, 0).Trim(), Pack = gm.Get(r, 1).Trim() };
            for (var d = 1; d <= NDay; d++) { var v = gm.Get(r, first + d - 1).Trim(); if (v.Length > 0 && Num(v) != 0) l.Dai[d.ToString()] = v; }
            if (r < old.Count) l.Extra = old[r].Extra;
            res.Add(l);
        }
        while (res.Count > 1 && IsEmptyLine(res[^1])) res.RemoveAt(res.Count - 1);
        b.Lines = res.Count > 0 ? res : new List<CargoLine> { new() };
    }
    private static bool IsEmptyLine(CargoLine l) => l.Sn.Length == 0 && l.Lo.Length == 0 && l.KsTay.Length == 0 && l.Size.Length == 0 && l.Pack.Length == 0 && l.Dai.Count == 0 && l.Loai.Length == 0;
    private static double Num(string v) => double.TryParse(v.Replace(",", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

    // ------------------------------------------------------------------ tổng hợp / xem trước

    private void RefreshTitle()
    {
        var so = _cur is null ? "" : _cur.SoPhieu.Trim().Length > 0 ? _cur.SoPhieu.Trim() : "(chưa số)";
        _curTitle.Text = _cur is null ? "" : $"{_prof.Ten}{(_cur.Id.Length == 0 ? " · cont mới" : "")} · {so}";
    }

    /// <summary>"Mục 1 dòng 4, 5" cho các cảnh báo cùng nội dung.</summary>
    private static string WhereOf(IReadOnlyList<CargoStock.LineWarn> ws) =>
        string.Join(" · ", ws.GroupBy(w => w.Block).Select(g => $"Mục {g.Key + 1} dòng {string.Join(", ", g.Select(w => w.Line + 1).Distinct())}"));

    private void RefreshAll()
    {
        RefreshTitle();
        if (_cur is null) { _warn = new(); _totals.Text = ""; _probPill.IsVisible = _stockPill.IsVisible = _savedPill.IsVisible = false; _save.IsEnabled = false; _exp.IsEnabled = false; _ksInfo.Text = ""; _stockInfo.Text = ""; return; }
        _save.IsEnabled = _edit; _exp.IsEnabled = _export;
        var chk = Svc.CheckRec(_prof, _cur);
        double kg;
        if (_prof.Gk) kg = BuildGk(Svc.Cfg, _cur, Svc.Ks).Kg; else kg = BuildPhieu(Svc.Cfg, _cur, Svc.Ks).Kg;
        _totals.Text = $"{chk.Kien:#,0.##} kiện · {kg:#,0.00} kg · {DayCount(_cur)} dãy" + (chk.Kien > 0 ? "\n" + PhieuKhoCore.DocSo(chk.Kien) + " kiện" : "");
        _probBox.IsVisible = _problems.IsVisible = chk.Problems.Count > 0;
        _problems.Text = chk.Problems.Count == 0 ? "" : $"⚠ Cần xử lý trước khi xuất ({chk.Problems.Count}):\n• " + string.Join("\n• ", chk.Problems.Take(10)) + (chk.Problems.Count > 10 ? $"\n(+{chk.Problems.Count - 10} nữa)" : "");
        _probPill.IsVisible = chk.Problems.Count > 0; _probText.Text = $"⚠ {chk.Problems.Count} cần xử lý"; ToolTip.SetTip(_probPill, _problems.Text);
        // 1.88: cảnh báo từng dòng (Size-quy cách sai · không có / hết / vượt tồn · đặc tính không còn hàng) — chỉ cảnh báo, không chặn lưu
        var sc = Svc.StockCheck(_cur); var lw = Svc.LineWarnings(_cur);
        _warn = lw.GroupBy(w => (w.Block, w.Line)).ToDictionary(g => g.Key, g => g.First());
        var lines = lw.GroupBy(w => w.Text).Select(g => $"{WhereOf(g.ToList())}: {g.Key}").ToList();
        string list = lines.Count == 0 ? "" : "• " + string.Join("\n• ", lines.Take(10)) + (lines.Count > 10 ? $"\n(+{lines.Count - 10} nữa)" : "");
        var anyBad = lw.Any(w => w.Bad);
        _stockInfo.Foreground = lw.Count > 0 ? AppTheme.Brush(anyBad ? "#B42318" : "#9A5B00") : AppTheme.Brush("#1E7B4A");
        _stockBox.Background = lw.Count > 0 ? AppTheme.Brush(anyBad ? "#FDE8E6" : "#FFF2D6") : AppTheme.Brush("#E3F4EA");
        _stockInfo.Text = lw.Count > 0
            ? $"⚠ Cảnh báo {_warn.Count} dòng (chỉ cảnh báo, vẫn lưu được):\n" + list + (Svc.Stock.Count == 0 ? "\nChưa có Tồn kho An An để dò tồn (nạp file ở module Tồn kho An An)." : "")
            : Svc.Stock.Count == 0 ? "Chưa có Tồn kho An An để dò tồn (nạp file ở module Tồn kho An An)." : sc.Count > 0 ? $"✔ Dò tồn An An: {sc.Count} mã đều đủ tồn." : "";
        if (Svc.Stock.Count > 0 && lw.Count == 0 && sc.Any(x => x.St != "ok"))   // dự phòng: mã đủ 25 ký tự lệch tồn mà dòng không bắt được
        {
            var bad = sc.Where(x => x.St != "ok").ToList();
            _stockBox.Background = AppTheme.Brush("#FFF2D6"); _stockInfo.Foreground = AppTheme.Brush("#9A5B00");
            _stockInfo.Text = $"Dò tồn An An — {bad.Count}/{sc.Count} mã cần xem (không chặn xuất):\n• " + string.Join("\n• ", bad.Take(8).Select(x => x.Ma + " — " + x.Text));
        }
        _stockBox.IsVisible = _stockInfo.Text.Length > 0;
        _stockPill.IsVisible = lw.Count > 0; _stockText.Text = $"⚠ {_warn.Count} dòng cần xem"; ToolTip.SetTip(_stockPill, _stockInfo.Text);
        _ksInfo.Text = $"Kết quả kháng sinh đã nạp: {Svc.Ks.Size:#,0} lô (đọc lúc {Svc.KsAt:HH:mm:ss}). Chưa có kết quả thì nhập ở module “Kết quả kháng sinh” rồi bấm “Tra lại”, hoặc gõ số kết quả tay.";
        _savedPill.IsVisible = true;
        if (_dirty) { _savedText.Text = "● Chưa lưu"; PhieuShell.SetPill(_savedPill, "warn"); }
        else if (_cur.Id.Length > 0) { _savedText.Text = "Đã lưu"; PhieuShell.SetPill(_savedPill, "ok"); }
        else { _savedText.Text = "Cont mới"; PhieuShell.SetPill(_savedPill, "info"); }
        if (_previewBtn.IsChecked == true) RenderPreview();
    }


    private void RenderPreview()
    {
        if (_cur is null) { _pm.Load(Array.Empty<GridColumn>(), Array.Empty<string[]>()); _pg.Refresh(); return; }
        if (_prof.Gk)
        {
            var g = BuildGk(Svc.Cfg, _cur, Svc.Ks);
            _pm.Load(new[] { new GridColumn("stt", "STT", 45, "number"), new GridColumn("ma", "Mã hàng (19 ký tự)", 170), new GridColumn("ten", "Tên hàng", 340), new GridColumn("lo", "Lô", 100), new GridColumn("size", "Size", 70), new GridColumn("pack", "Packing", 90), new GridColumn("kien", "Kiện", 70, "number"), new GridColumn("kg", "Kg", 90, "number") },
                g.Rows.Select(r => new[] { r.Stt.ToString(), r.Ma, r.Ten, r.Lo, r.Size, r.Pack, r.Kien.ToString("0.###", Ui.Vi), r.Kg.ToString("#,0.###", Ui.Vi) }).Append(new[] { "", "TOTAL", "", "", "", "", g.Kien.ToString("0.###", Ui.Vi), g.Kg.ToString("#,0.###", Ui.Vi) }));
        }
        else
        {
            var ph = BuildPhieu(Svc.Cfg, _cur, Svc.Ks, preview: true);
            var cols = new List<GridColumn> { new("blk", "Mục hàng", 170), new("size", "Size", 70), new("pack", "Quy cách", 90) };
            cols.AddRange(ph.Cols.Select(m => new GridColumn("m" + m, m, 110, "number")));
            cols.Add(new GridColumn("other", ".", 50, "number")); cols.Add(new GridColumn("chua", "Chưa có KQ", 85, "number")); cols.Add(new GridColumn("kien", "Kiện", 70, "number")); cols.Add(new GridColumn("kg", "Kg", 90, "number"));
            var rows = new List<string[]>();
            foreach (var b in ph.Blocks)
            {
                foreach (var r in b.Rows) rows.Add(new[] { b.Title, r.Size, r.Pack }.Concat(ph.Cols.Select(m => r.Cols.TryGetValue(m, out var q) ? q.ToString("0.###", Ui.Vi) : "")).Concat(new[] { r.Other > 0 ? r.Other.ToString("0.###", Ui.Vi) : "", r.Chua > 0 ? r.Chua.ToString("0.###", Ui.Vi) : "", r.Kien.ToString("0.###", Ui.Vi), r.Kg.ToString("#,0.###", Ui.Vi) }).ToArray());
                rows.Add(new[] { "TOTAL " + b.Title, "", "" }.Concat(ph.Cols.Select(_ => "")).Concat(new[] { "", "", b.Kien.ToString("0.###", Ui.Vi), b.Kg.ToString("#,0.###", Ui.Vi) }).ToArray());
            }
            rows.Add(new[] { "TỔNG", "", "" }.Concat(ph.Cols.Select(m => ph.Per[m].ToString("0.###", Ui.Vi))).Concat(new[] { ph.Other.ToString("0.###", Ui.Vi), "", ph.Kien.ToString("0.###", Ui.Vi), ph.Kg.ToString("#,0.###", Ui.Vi) }).ToArray());
            _pm.Load(cols, rows);
        }
        _pg.Refresh();
    }

    // ------------------------------------------------------------------ thao tác

    private async Task DoSave()
    {
        if (_cur is null) return;
        try
        {
            if (_cur.SoPhieu.Trim().Length == 0) { await Ui.Message(this, "Chưa nhập Số phiếu."); return; }
            var saved = await Task.Run(() => Svc.Save(_prof, _cur));
            _list = await Task.Run(() => Svc.List(_prof)); _cur = CargoCore.Clone(saved); _dirty = false;
            RenderList(saved.Id); ShowEditor(); _status.Text = "Đã lưu cont " + saved.SoPhieu + " (" + DateTime.Now.ToString("HH:mm:ss") + ")";
        }
        catch (SheetConflictException) { await Ui.Message(this, "Dữ liệu vừa được máy khác sửa nên cont CHƯA được lưu (để không ghi đè). Bấm Đóng để tải bản mới nhất rồi lưu lại.", "Máy khác vừa sửa"); await LoadAsync(); }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); }
    }

    private async Task DoDelete()
    {
        if (_cur is null || _cur.Id.Length == 0) return;
        if (!await Ui.Confirm(this, $"Xóa cont {_cur.SoPhieu}? Không khôi phục được.", "Xóa cont", "Xóa")) return;
        var id = _cur.Id;
        if (await Ui.Run(this, () => Svc.Delete(_prof, id))) { _cur = null; _dirty = false; _list = Svc.List(_prof); RenderList(""); ShowEditor(); ShowFull(null); }
    }

    private async Task DoExport()
    {
        if (_cur is null) return;
        if (_dirty && _edit && _cur.SoPhieu.Trim().Length > 0) await DoSave();
        var cur = CargoCore.Clone(_cur);
        var path = await Ui.SaveFile(this, "Xuất Cargo list + phiếu xuất kho", FileName(cur, _prof.Code), "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.Export(_prof, cur, path))) { _status.Text = "Đã xuất " + Path.GetFileName(path); Ui.Exported(this, path); RefreshAll(); }
    }

    private async Task PasteXe()
    {
        if (_cur is null) return;
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 220, Watermark = "Dán email / bảng có: Họ tên tài xế, SĐT, Số xe, Số cont, Seal, Ngày xuất…" };
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { try { var t = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(cb); if (!string.IsNullOrWhiteSpace(t)) box.Text = t; } catch { } }
        var ok = new Button { Content = "Nhận thông tin xe", Classes = { "primary" } }; var close = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel(); body.Children.Add(new TextBlock { Text = "Dán nội dung thông tin xe — app tách tài xế + SĐT (→ Người nhận hàng), số xe + cont (→ Cont / Xe), seal và ngày xuất.", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        body.Children.Add(box); body.Children.Add(Ui.Buttons(close, ok));
        var w = Ui.Dialog("Dán thông tin xe", body, 620); var applied = false;
        ok.Click += (_, _) => { applied = true; w.Close(); }; close.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (!applied) return;
        var x = ParseXe(box.Text);
        if (x.TaiXe.Length + x.Sdt.Length > 0) _cur.NguoiNhan = NguoiNhanOf(x);
        if (ContXeOf(x).Length > 0) _cur.ContXe = ContXeOf(x);
        if (x.Seal.Length > 0) _cur.Seal = x.Seal;
        if (x.Ngay.Length > 0) _cur.Ngay = x.Ngay;
        _dirty = true; ShowEditor();
        _status.Text = "Đã nhận thông tin xe" + (x.Seal.Length == 0 ? " (không thấy seal)" : "") + ".";
    }

    // ------------------------------------------------------------------ danh mục (loại hàng + size-quy cách)

    private readonly GridModel _lm = new(), _qm = new();
    private SheetGrid? _lg, _qg;
    private readonly TextBlock _cfgMsg = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Warn };

    private Control BuildCfgTab()
    {
        // 1.53: bảng Size-quy cách chiếm hết phần còn lại của trang (trước cố định 260 px trong khung cuộn); Loại hàng cao theo số dòng
        _lg = new SheetGrid(_lm) { SpareRows = 2 }; _qg = new SheetGrid(_qm) { SpareRows = 3 };
        _lm.Changed += () => _lg.Height = SheetGrid.FitHeight(_lm.RowCount + _lg.SpareRows, 220);
        // 1.128: cột Phân loại An An gợi ý tên nhóm
        _lg.Suggest = (col, text) => col != 2 ? null : KhoLanh.Core.KiemKe.PhanLoaiM1.Order.Select(KhoLanh.Core.KiemKe.PhanLoaiM1.LabelOf).Append(NoNhom)
            .Where(x => text.Trim().Length == 0 || KhoLanh.Core.KiemKe.PhanLoaiM1.Fuzzy(x).Contains(KhoLanh.Core.KiemKe.PhanLoaiM1.Fuzzy(text)))
            .Select(x => new SheetGrid.GridSuggestion(x, x, x == NoNhom ? "Không lọc tồn theo phân loại" : "Phân loại của Tồn kho An An")).ToList();
        var save = Btn("💾 Lưu danh mục", async () => await SaveCfgAsync(), primary: true); save.IsEnabled = _edit;
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 0, 8) };
        top.Children.Add(new TextBlock { Text = "Mã hàng = tiền tố 13 ký tự (theo Loại + số quy cách) + Size 4 số + Mã gói 4 ký tự + số kết quả kháng sinh 4 số. Mã gói = ký tự 18–21 của mã hàng (trọng lượng + quy cách đóng thùng trong Tổng hợp › Mã hóa sản phẩm).", TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Classes = { "muted" } });
        top.Children.Add(Section("Loại hàng (mã loại gõ ở cột Loại: BTP = HLSO, NL = HOSO…; Phân loại An An = nhóm tồn dùng để gợi ý)", Framed(_lg)));
        top.Children.Add(new TextBlock { Text = "Size-quy cách (số quy cách = số cuối của 0040-6; cột tiền tố theo từng loại hàng ở trên)", FontWeight = FontWeight.SemiBold });
        var sug = Btn("🪄 Gợi ý Mã gói + Tiền tố", async () => { SuggestCfg(); await Task.CompletedTask; }); sug.IsEnabled = _edit;
        var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        bottom.Children.Add(_cfgMsg); bottom.Children.Add(new WrapPanel { ItemSpacing = 8, Children = { save, sug } });
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(Framed(_qg));
        return root;
    }

    private const string NoNhom = "(không gắn)";
    private static string NhomText(string nhom) => nhom.Length == 0 ? NoNhom : KhoLanh.Core.KiemKe.PhanLoaiM1.LabelOf(nhom);

    private void RenderCfg()
    {
        var cfg = Svc.Cfg; _lm.ReadOnly = !_edit; _qm.ReadOnly = !_edit;
        _lm.Load(new[] { new GridColumn("code", "Mã loại", 90), new GridColumn("ten", "Tên (HLSO / HOSO…)", 200), new GridColumn("nhom", "Phân loại An An (gợi ý từ tồn)", 220) },
            cfg.Loai.Select(l => new[] { l.Code, l.Ten, NhomText(NhomOf(cfg, l.Code)) }));
        var qcols = new List<GridColumn> { new("d", "Số", 50), new("label", "Nhãn (1.8Kg x 6)", 150), new("pack", "Mã gói (IME1)", 110) };
        qcols.AddRange(cfg.Loai.Select(l => new GridColumn("pre" + l.Code, "Tiền tố " + l.Code, 160)));
        _qm.Load(qcols, cfg.Qc.Select(q => new[] { q.D, q.Label, q.Pack }.Concat(cfg.Loai.Select(l => q.Pre.GetValueOrDefault(l.Code) ?? "")).ToArray()));
        _lg?.Refresh(); _qg?.Refresh();
    }

    private void SuggestCfg()
    {
        (CargoSuggest.Dict Dict, List<string> Ma) refs;
        try { refs = Svc.LoadSuggestRefs(); } catch (Exception e) { _cfgMsg.Text = e.Message; return; }
        var cfg = Svc.Cfg; var lines = new List<string>(); var filled = 0;
        for (var r = 0; r < _qm.RowCount; r++)
        {
            var d = _qm.Get(r, 0).Trim(); var label = _qm.Get(r, 1).Trim(); if (label.Length == 0) continue;
            var cur = new CargoCfg { Loai = cfg.Loai };
            for (var rr = 0; rr < _qm.RowCount; rr++) { var q = new CargoQc { D = _qm.Get(rr, 0).Trim(), Label = _qm.Get(rr, 1).Trim() }; for (var i = 0; i < cfg.Loai.Count; i++) { var v = _qm.Get(rr, 3 + i).Trim(); if (v.Length > 0) q.Pre[cfg.Loai[i].Code] = v; } if (q.Label.Length > 0) cur.Qc.Add(q); }
            var sg = CargoSuggest.Suggest(label, refs.Dict, cur, refs.Ma, d);
            if (sg.Pack.Length > 0 && _qm.Get(r, 2).Trim().Length == 0) { _qm.Set(r, 2, sg.Pack); filled++; lines.Add($"{label}: mã gói {sg.Pack}"); }
            for (var i = 0; i < cfg.Loai.Count; i++) if (_qm.Get(r, 3 + i).Trim().Length == 0 && sg.Pre.TryGetValue(cfg.Loai[i].Code, out var pv)) { _qm.Set(r, 3 + i, pv); filled++; lines.Add($"{label}: tiền tố {cfg.Loai[i].Code} {pv}"); }
            foreach (var n in sg.Notes.Where(n => _qm.Get(r, 2).Trim().Length == 0 || n.Contains("tiền tố"))) lines.Add($"⚠ {label}: {n}");
        }
        _qg?.Refresh();
        _cfgMsg.Text = filled == 0 && lines.Count == 0 ? "Không có ô trống nào cần gợi ý." : $"Đã điền {filled} ô (chưa lưu — kiểm tra rồi bấm Lưu danh mục).\n" + string.Join("\n", lines.Take(12));
    }

    private async Task SaveCfgAsync()
    {
        var loai = new List<CargoLoai>();
        for (var r = 0; r < _lm.RowCount; r++)
        {
            if (_lm.Get(r, 0).Trim().Length == 0) continue;
            // 1.128: Phân loại An An — tên nhóm như tab Phân loại của Tồn kho An An; trống = theo mặc định, "(không gắn)" = không gợi ý theo phân loại
            if (ParseNhom(_lm.Get(r, 2)) is not { } nhom) { _cfgMsg.Text = $"Loại hàng dòng {r + 1}: phân loại \"{_lm.Get(r, 2).Trim()}\" không có ở Tồn kho An An. Chọn một trong: {string.Join(", ", KhoLanh.Core.KiemKe.PhanLoaiM1.Order.Select(KhoLanh.Core.KiemKe.PhanLoaiM1.LabelOf))} (hoặc để trống)."; return; }
            loai.Add(new CargoLoai { Code = _lm.Get(r, 0), Ten = _lm.Get(r, 1), Nhom = nhom });
        }
        if (loai.Count == 0) { _cfgMsg.Text = "Phải có ít nhất 1 loại hàng."; return; }
        var cfg = new CargoCfg { Loai = NormCfg(new CargoCfg { Loai = loai }).Loai };
        var warn = new List<string>();
        for (var r = 0; r < _qm.RowCount; r++)
        {
            var d = _qm.Get(r, 0).Trim(); if (d.Length == 0 && _qm.Get(r, 1).Trim().Length == 0) continue;
            if (!System.Text.RegularExpressions.Regex.IsMatch(d, @"^\d$")) { _cfgMsg.Text = $"Quy cách dòng {r + 1}: số quy cách phải là 1 chữ số 0–9."; return; }
            var q = new CargoQc { D = d, Label = _qm.Get(r, 1).Trim(), Pack = _qm.Get(r, 2).Trim() };
            if (q.Label.Length == 0) { _cfgMsg.Text = $"Quy cách số {d}: thiếu nhãn (VD 1.8Kg x 3)."; return; }
            if (KgPerCarton(q.Label) is null) warn.Add($"nhãn \"{q.Label}\" chưa tính được kg/thùng (gõ 1.8Kg x 3 hoặc 100G x 60)");
            if (q.Pack.Length != 4) warn.Add($"mã gói số {d} nên đủ 4 ký tự");
            for (var i = 0; i < cfg.Loai.Count; i++) { var v = _qm.Get(r, 3 + i).Trim(); if (v.Length > 0) q.Pre[cfg.Loai[i].Code] = v; }
            cfg.Qc.Add(q);
        }
        if (cfg.Qc.GroupBy(q => q.D).Any(g => g.Count() > 1)) { _cfgMsg.Text = "Có số quy cách bị trùng."; return; }
        if (await Ui.Run(this, () => Svc.SaveCfg(cfg))) { _cfgMsg.Text = warn.Count > 0 ? "Đã lưu. Lưu ý: " + string.Join("; ", warn) + "." : ""; RenderCfg(); RefreshAll(); _status.Text = "Đã lưu danh mục"; }
    }
}

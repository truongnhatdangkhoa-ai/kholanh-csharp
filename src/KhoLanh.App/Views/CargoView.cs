using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
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

    private readonly bool _edit, _export;
    private bool _loading, _dirty;
    private CargoProfile _prof = Profiles[0];
    private CargoRec? _cur;
    private List<CargoRec> _list = new();
    // 1.61: danh sách cont và nhập liệu cont là 2 tab con riêng (không chung 1 màn hình).
    private readonly GridModel _cm = new() { ReadOnly = true };
    private readonly SheetGrid _cg;
    private readonly TextBlock _listInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _curTitle = new() { FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Dark, TextWrapping = TextWrapping.Wrap };
    private readonly TabControl _inner;
    private readonly TabItem _editTab;
    private readonly StackPanel _editor = new() { Spacing = 8 };
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _totals = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Dark, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _problems = new() { FontSize = 12.5, Foreground = Warn, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _stockInfo = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _ksInfo = new() { Classes = { "muted" }, FontSize = 12 };
    private readonly StackPanel _kindBar = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly GridModel _pm = new() { ReadOnly = true };
    private readonly SheetGrid _pg;
    private readonly TabControl _tabs;
    private readonly Button _save, _del, _exp;
    private TextBox? _soBox;
    private readonly List<(CargoBlock B, GridModel M)> _blocks = new();

    public CargoView()
    {
        _edit = Svc.CanEdit; _export = Svc.CanExport;
        _pg = new SheetGrid(_pm) { SpareRows = 0 };
        _cg = new SheetGrid(_cm) { SpareRows = 0 };
        _cg.DoubleTapped += async (_, _) => await OpenSelected();
        _cg.AddHandler(KeyDownEvent, async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; await OpenSelected(); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        foreach (var p in Profiles)
        {
            var b = new ToggleButton { Content = p.Key switch { "cargo" => "Thuận Phước", "cargogc" => "Gia công", _ => "Gửi kho" }, Padding = new Thickness(14, 5), FontSize = 13, Tag = p.Key };
            b.Click += async (_, _) => { if (!_loading) await SwitchProfile(p); };
            _kindBar.Children.Add(b);
        }
        _save = Btn("💾 Lưu cont", async () => await DoSave(), primary: true); _save.IsEnabled = _edit;
        _del = Btn("Xóa cont", async () => await DoDelete(), danger: true); _del.IsEnabled = false;
        _exp = Btn("📤 Xuất Excel (Cargo + phiếu)", async () => await DoExport()); _exp.IsEnabled = _export;
        var bar = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        bar.Children.Add(_save); bar.Children.Add(_exp); bar.Children.Add(_del);
        bar.Children.Add(Btn("📩 Dán thông tin xe…", async () => await PasteXe()));
        bar.Children.Add(Btn("⟳ Tra lại kháng sinh", async () => await ReloadRefs()));

        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Nhập Cargo", Content = new ScrollViewer { Content = new Border { Padding = new Thickness(0, 6, 12, 12), Child = _editor }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } },
                new TabItem { Header = "Phiếu xem trước", Content = new Border { Padding = new Thickness(0, 6, 0, 0), Child = Framed(_pg) } },
            },
        };
        _tabs.SelectionChanged += (_, _) => { if (_tabs.SelectedIndex == 1) RenderPreview(); };

        // Tab con 1: chọn loại (Thuận Phước · Gia công · Gửi kho) + bảng danh sách cont; bấm đúp / Enter / "Mở cont" → tab nhập liệu.
        var listTools = new WrapPanel { ItemSpacing = 6, LineSpacing = 4, Margin = new Thickness(0, 0, 0, 6) };
        listTools.Children.Add(_kindBar);
        if (_edit) listTools.Children.Add(Btn("➕ Cont mới", async () => await NewCont(), primary: true));
        listTools.Children.Add(Btn("Mở cont đang chọn", async () => await OpenSelected()));
        listTools.Children.Add(_listInfo);
        var listPage = new DockPanel();
        DockPanel.SetDock(listTools, Dock.Top); listPage.Children.Add(listTools); listPage.Children.Add(Framed(_cg));

        // Tab con 2: nhập liệu cont đang mở (Nhập Cargo / Phiếu xem trước).
        var head = new StackPanel { Spacing = 4 };
        head.Children.Add(_curTitle); head.Children.Add(bar); head.Children.Add(_problems); head.Children.Add(_totals); head.Children.Add(_stockInfo); head.Children.Add(_ksInfo); head.Children.Add(_status);
        var editPage = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); editPage.Children.Add(head); editPage.Children.Add(_tabs);
        _inner = Ui.SubTabs(("Danh sách phiếu nội địa", listPage), ("Nhập liệu phiếu", editPage), ("Danh mục", BuildCfgTab()));
        _editTab = (TabItem)_inner.Items[1]!;
        Content = _inner;
        ShowEditor();
        AttachedToVisualTree += async (_, _) => await LoadAsync();
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
    private static StackPanel Labeled(string label, Control c) { var s = new StackPanel { Spacing = 1 }; s.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, FontSize = 11.5 }); s.Children.Add(c); return s; }
    private static Border Section(string title, Control body)
    {
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = Dark }); s.Children.Add(body);
        return new Border { Background = CardBg, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = s };
    }
    private void Dirty() { if (_loading) return; _dirty = true; RefreshAll(); }
    private TextBox Tb(string value, Action<string> set, double w = 150)
    {
        var tb = new TextBox { Text = value, Width = w, FontSize = 12.5, IsReadOnly = !_edit }; var last = value;
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
        keepId ??= _cur?.Id;
        _cm.Load(new[]
        {
            new GridColumn("so", "Số phiếu", 130), new GridColumn("ngay", "Ngày xuất", 95), new GridColumn("cont", "Cont số", 80), new GridColumn("nhan", "Người nhận hàng", 190),
            new GridColumn("xe", "Cont / Xe", 150), new GridColumn("seal", "Seal", 110), new GridColumn("kho", "Kho xuất", 120), new GridColumn("kien", "Tổng kiện", 90, "number"),
        }, _list.Select(c => new[]
        {
            c.SoPhieu.Length > 0 ? c.SoPhieu : "(chưa số)", c.Ngay.Length >= 10 ? $"{c.Ngay[8..10]}/{c.Ngay[5..7]}/{c.Ngay[..4]}" : c.Ngay, c.ContSo, c.NguoiNhan,
            c.ContXe, c.Seal, c.Kho, c.Blocks.Sum(b => b.Lines.Sum(LineTotal)).ToString("#,0.##", Ui.Vi),
        }));
        _cg.Refresh();
        var i = keepId is null ? -1 : _list.FindIndex(c => c.Id == keepId);
        if (i >= 0) _cg.Select(i, 0);
        _listInfo.Text = $"{_list.Count:#,0} cont {_prof.Ten.Replace("Cargo ", "")} · bấm đúp (hoặc Enter) để mở sang tab Nhập liệu";
    }

    private async Task OpenSelected()
    {
        var i = _cg.Row; if (i < 0 || i >= _list.Count) return;
        var rec = _list[i];
        if (_dirty && _cur is not null && _cur.Id == rec.Id) { _inner.SelectedIndex = 1; return; }   // đang soạn đúng cont này → giữ thay đổi
        if (_dirty && _cur is not null && !await Ui.Confirm(this, "Cont đang soạn chưa lưu. Bỏ thay đổi và mở cont khác?", "Chưa lưu", "Bỏ thay đổi")) return;
        _cur = CargoCore.Clone(rec); _dirty = false; ShowEditor();
        _inner.SelectedIndex = 1;
    }

    private async Task NewCont()
    {
        if (_dirty && !await Ui.Confirm(this, "Cont đang soạn chưa lưu. Bỏ thay đổi và tạo cont mới?", "Chưa lưu", "Bỏ thay đổi")) return;
        _cur = Svc.NewRec(_prof, _list); _dirty = true;
        ShowEditor();
        _inner.SelectedIndex = 1;
    }

    // ------------------------------------------------------------------ form

    private void ShowEditor()
    {
        _editor.Children.Clear(); _blocks.Clear(); _soBox = null;
        _del.IsEnabled = _edit && _cur is { Id.Length: > 0 };
        if (_cur is null) { _editor.Children.Add(new TextBlock { Text = _edit ? "Chưa mở cont nào. Bấm đúp một cont ở tab “Danh sách phiếu nội địa”, hoặc bấm “Cont mới” ở đó." : "Chưa mở cont nào. Bấm đúp một cont ở tab “Danh sách phiếu nội địa”.", Classes = { "muted" } }); RefreshAll(); return; }
        _loading = true; var c = _cur;
        var g1 = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        var date = new CalendarDatePicker { Width = 140, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy", IsEnabled = _edit };
        if (DateTime.TryParse(c.Ngay, out var dt)) date.SelectedDate = dt;
        date.SelectedDateChanged += (_, _) =>
        {
            if (_loading || date.SelectedDate is not { } d || d.ToString("yyyy-MM-dd") == c.Ngay) return;
            c.Ngay = d.ToString("yyyy-MM-dd");
            if (c.SoPhieu.Length == 0 || (c.SoPhieuTuCap.Length > 0 && c.SoPhieu == c.SoPhieuTuCap) && c.Id.Length == 0)
            { c.SoPhieu = NextSo(_list, c.Ngay, c.Id, _prof.Code); c.SoPhieuTuCap = c.SoPhieu; _loading = true; if (_soBox is not null) _soBox.Text = c.SoPhieu; _loading = false; }
            Dirty();
        };
        g1.Children.Add(Labeled("Ngày xuất", date));
        _soBox = Tb(c.SoPhieu, v => c.SoPhieu = v, 150);
        var so = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom }; so.Children.Add(_soBox);
        if (_edit) { var re = new Button { Content = "↻", Padding = new Thickness(8, 3) }; ToolTip.SetTip(re, $"Cấp lại số phiếu tự động ({_prof.SoEg})"); re.Click += (_, _) => { c.SoPhieu = NextSo(_list, c.Ngay, c.Id, _prof.Code); c.SoPhieuTuCap = c.SoPhieu; _soBox.Text = c.SoPhieu; Dirty(); }; so.Children.Add(re); }
        g1.Children.Add(Labeled("Số phiếu", so));
        g1.Children.Add(Labeled("Kho xuất", Cb(new[] { "An An", "Long An", "An An + Long An" }, c.Kho, v => c.Kho = v, 150)));
        g1.Children.Add(Labeled("Người nhận hàng", Tb(c.NguoiNhan, v => c.NguoiNhan = v, 250)));
        g1.Children.Add(Labeled("Cont / Xe", Tb(c.ContXe, v => c.ContXe = v, 230)));
        g1.Children.Add(Labeled("Seal", Tb(c.Seal, v => c.Seal = v, 130)));
        if (_prof.Gk)
        {
            g1.Children.Add(Labeled("Kho nhận (gửi kho)", Tb(c.KhoNhan, v => c.KhoNhan = v, 190)));
            g1.Children.Add(Labeled("Cont gửi kho số", Tb(c.ContSo, v => c.ContSo = v, 110)));
        }
        _editor.Children.Add(Section(_prof.Ten + " — thông tin cont" + $" (ví dụ số phiếu {_prof.SoEg})", g1));
        _editor.Children.Add(BuildCapRow(c));
        for (var i = 0; i < c.Blocks.Count; i++) _editor.Children.Add(BuildBlock(c, i));
        if (_edit)
        {
            var add = new WrapPanel { ItemSpacing = 6 };
            add.Children.Add(Btn("➕ Thêm mục hàng kháng sinh (HLSO / HOSO…)", async () => { c.Blocks.Add(new CargoBlock { Loai = "ks", Id = NewId() }); _dirty = true; ShowEditor(); await Task.CompletedTask; }));
            add.Children.Add(Btn("➕ Thêm mục hàng khác (tôm vụn, sauce…)", async () => { c.Blocks.Add(new CargoBlock { Loai = "khac", Id = NewId() }); _dirty = true; ShowEditor(); await Task.CompletedTask; }));
            _editor.Children.Add(add);
        }
        _loading = false; RefreshAll();
    }
    private static string NewId() => "b" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString("x") + Guid.NewGuid().ToString("N")[..3];

    /// <summary>1.71: hàng "KIỆN / DÃY" — số kiện thực tế từng dãy, dùng chung cả cont (dán / kéo nhiều ô như Excel). Gõ Tổng ở dòng mục hàng → app đổ đầy theo hàng này.</summary>
    private Control BuildCapRow(CargoRec c)
    {
        var gm = new GridModel { ReadOnly = !_edit };
        var cols = new List<GridColumn> { new("lb", "", 110) { ReadOnly = true }, new("sum", "Tổng", 70, "number") { ReadOnly = true } };
        for (var d = 1; d <= NDay; d++) cols.Add(Day(d));
        string[] Row() => new[] { "KIỆN / DÃY", "" }.Concat(Enumerable.Range(1, NDay).Select(d => c.DayCap.GetValueOrDefault(d.ToString()) ?? "")).ToArray();
        gm.Load(cols, new[] { Row() });
        var grid = new SheetGrid(gm) { SpareRows = 0, Filterable = false, ShowTotals = false, Sortable = false };
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
        var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
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
        bar.Children.Add(new TextBlock { Text = "Gõ / dán số kiện thực tế từng dãy (dùng chung cả cont). Ở mục hàng, gõ số vào cột Tổng → app tự đổ đầy từ dãy 1; dãy đã có hàng của dòng khác chỉ nhận phần còn trống.",
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Tag = "keep" });
        var body = new StackPanel { Spacing = 6 }; body.Children.Add(Framed(grid)); body.Children.Add(bar);
        return Section("KIỆN / DÃY — số kiện mỗi dãy", body);
    }

    private static GridColumn Day(int d) => new("d" + d, d.ToString(), 46, "number");

    private Control BuildBlock(CargoRec c, int bi)
    {
        var b = c.Blocks[bi]; var ks = b.Loai != "khac";
        var head = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        head.Children.Add(Labeled(ks ? "Tên mục (VD HLSO)" : "Tên mục (VD TÔM VỤN)", Tb(b.Ten, v => b.Ten = v, 220)));
        head.Children.Add(Labeled("Mã (ghi sau tên)", Tb(b.Ma, v => b.Ma = v, 130)));
        head.Children.Add(Labeled("Ghi chú sau tên", Tb(b.Tag, v => b.Tag = v, 160)));
        if (_edit)
        {
            var rm = new Button { Content = "🗑 Xóa mục", Padding = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Bottom }; rm.Classes.Add("danger");
            rm.Click += (_, _) => { c.Blocks.Remove(b); if (c.Blocks.Count == 0) c.Blocks.Add(new CargoBlock { Loai = "ks" }); _dirty = true; ShowEditor(); };
            head.Children.Add(rm);
        }
        var gm = new GridModel { ReadOnly = !_edit };
        var cols = new List<GridColumn>();
        int first;
        if (ks)
        {
            cols.AddRange(new[] { new GridColumn("loai", "Loại", 55), new GridColumn("sn", "Size-quy cách (0040-6)", 130), new GridColumn("lo", "Lô NL (22.05.26 L16)", 130), new GridColumn("kstay", "KQ KS tay", 70),
                new GridColumn("size", "Size", 65) { ReadOnly = true }, new GridColumn("pack", "Quy cách", 80) { ReadOnly = true }, new GridColumn("mk", "Thị trường", 150) { ReadOnly = true },
                new GridColumn("ks", "Ký hiệu KS", 100) { ReadOnly = true }, new GridColumn("ma", "Mã hàng", 190) { ReadOnly = true }, new GridColumn("tong", "Tổng", 65, "number") { ReadOnly = !_edit } });
            first = 10;
        }
        else
        {
            cols.AddRange(new[] { new GridColumn("size", "Size", 100), new GridColumn("pack", "Quy cách (10.0 kg x 1)", 150), new GridColumn("tong", "Tổng", 65, "number") { ReadOnly = !_edit } });
            first = 3;
        }
        for (var d = 1; d <= NDay; d++) cols.Add(Day(d));
        gm.Load(cols, b.Lines.Select(l => RowOfLine(l, ks)));
        var grid = new SheetGrid(gm) { SpareRows = MinSpare };
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
            if (!ks || r >= b.Lines.Count || col is < 4 or > 9) return null;
            var ln = b.Lines[r]; if (LineTotal(ln) <= 0 && ln.Sn.Trim().Length == 0) return null;
            var res = ResolveLine(Svc.Cfg, b, ln, Svc.Ks); return res.Errors.Count > 0 ? BadBg : (res.Market.Length > 0 ? OkBg : null);
        };
        // 1.53: cao theo dòng thật (trước tính 24 px/dòng < 26 px thật → hụt dòng), tối đa 560; ⤢ ở góc mở cửa sổ lớn.
        // 1.67: luôn hiện ít nhất 10 dòng (dòng có số liệu + dòng trống để gõ thêm), còn ≥ 3 dòng trống ở cuối. Trước đây chỉ
        // 1 dòng + 2 dòng trống, thanh cuộn ngang che dòng cuối, Enter xuống dòng mới không thấy → tưởng không thêm dòng được.
        void FixH()
        {
            grid.SpareRows = Math.Max(MinSpare, MinRows - gm.RowCount);
            grid.Height = SheetGrid.FitHeight(gm.RowCount + grid.SpareRows, 560) + ScrollH;
        }
        FixH();
        gm.Changed += () => { if (_loading) return; SyncLines(b, gm, ks); FixH(); Dirty(); };
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
                    if (caps.Count == 0) { _status.Text = "Nhập số kiện từng dãy ở hàng “KIỆN / DÃY” (phía trên các mục hàng) rồi gõ lại Tổng."; continue; }
                    var r = RaiTong(c, bi, h.r, want, caps);
                    _status.Text = want <= 0 ? "Đã xóa số kiện của dòng." : $"Đã xếp {r.Placed:#,0} kiện vào {ln.Dai.Count} dãy" + (r.Left > 0 ? $" — CÒN DƯ {r.Left:#,0} kiện, hết dãy." : ".");
                }
                for (var d = 1; d <= NDay; d++) res.Add((h.r, dayFirst + d - 1, ln.Dai.GetValueOrDefault(d.ToString()) ?? ""));
            }
            return res;
        };
        var note = new TextBlock { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Text = ks
            ? "Mỗi dòng: Loại (BTP/NL) + Size-quy cách + Lô nguyên liệu; app tự ra mã hàng và tra kết quả kháng sinh. Ô đỏ = còn lỗi (chưa có kết quả…), xanh = đã ra thị trường. Số kiện gõ vào cột dãy 1–40 (dán được từ Excel)."
            : "Hàng không kháng sinh: gõ tay Size + Quy cách; kiện vào cột “.” của phiếu." };
        var body = new StackPanel { Spacing = 6 }; body.Children.Add(head); body.Children.Add(Framed(grid)); body.Children.Add(note);
        return Section($"Mục hàng {bi + 1} — {(ks ? "kháng sinh" : "khác")}", body);
    }
    /// <summary>Lưới mục hàng: tối thiểu số dòng hiện (kể cả dòng trống), số dòng trống luôn có ở cuối, chỗ cho thanh cuộn ngang.</summary>
    internal const int MinRows = 10, MinSpare = 3; private const double ScrollH = 14;

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
        _curTitle.Text = _cur is null ? "" : $"{_prof.Ten}{(_cur.Id.Length == 0 ? " · cont mới" : "")} · {so}{(_dirty ? " ●" : "")}";
        _curTitle.IsVisible = _cur is not null;
        _editTab.Header = _cur is null ? "Nhập liệu phiếu" : "Nhập liệu phiếu · " + so + (_dirty ? " ●" : "");
    }

    private void RefreshAll()
    {
        RefreshTitle();
        if (_cur is null) { _totals.Text = ""; _problems.IsVisible = false; _save.IsEnabled = false; _exp.IsEnabled = false; _ksInfo.Text = ""; _stockInfo.Text = ""; return; }
        _save.IsEnabled = _edit; _exp.IsEnabled = _export;
        var chk = Svc.CheckRec(_prof, _cur);
        double kg;
        if (_prof.Gk) kg = BuildGk(Svc.Cfg, _cur, Svc.Ks).Kg; else kg = BuildPhieu(Svc.Cfg, _cur, Svc.Ks).Kg;
        _totals.Text = $"Tổng: {chk.Kien:#,0.##} kiện · {kg:#,0.###} kg · {DayCount(_cur)} dãy" + (chk.Kien > 0 ? "  —  " + PhieuKhoCore.DocSo(chk.Kien) + " kiện" : "");
        _problems.IsVisible = chk.Problems.Count > 0;
        _problems.Text = chk.Problems.Count == 0 ? "" : $"Cần xử lý trước khi xuất ({chk.Problems.Count}): " + string.Join(" · ", chk.Problems.Take(3)) + (chk.Problems.Count > 3 ? " …" : "");
        var sc = Svc.StockCheck(_cur); var bad = sc.Where(x => x.St != "ok").ToList();
        _stockInfo.Foreground = bad.Count > 0 ? Warn : null;
        _stockInfo.Text = Svc.Stock.Count == 0 ? "Chưa có Tồn kho An An để dò tồn (nạp file ở module Tồn kho An An)." : bad.Count == 0 ? (sc.Count > 0 ? $"✔ Dò tồn An An: {sc.Count} mã đều đủ tồn." : "") : $"⚠ Dò tồn An An ({bad.Count}/{sc.Count} mã cần xem, không chặn xuất): " + string.Join(" · ", bad.Take(4).Select(x => x.Ma + " — " + x.Text)) + (bad.Count > 4 ? " …" : "");
        _ksInfo.Text = $"Kết quả kháng sinh đã nạp: {Svc.Ks.Size:#,0} lô (đọc lúc {Svc.KsAt:HH:mm:ss}). Chưa có kết quả thì nhập ở module “Kết quả kháng sinh” rồi bấm “Tra lại”, hoặc gõ số kết quả tay.";
        _status.Text = _dirty ? "● Có thay đổi chưa lưu" : (_cur.Id.Length > 0 ? "Đã lưu" : "");
        if (_tabs?.SelectedIndex == 1) RenderPreview();
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
        if (await Ui.Run(this, () => Svc.Delete(_prof, id))) { _cur = null; _dirty = false; _list = Svc.List(_prof); RenderList(""); ShowEditor(); _status.Text = "Đã xóa cont"; }
    }

    private async Task DoExport()
    {
        if (_cur is null) return;
        if (_dirty && _edit && _cur.SoPhieu.Trim().Length > 0) await DoSave();
        var cur = CargoCore.Clone(_cur);
        var path = await Ui.SaveFile(this, "Xuất Cargo list + phiếu xuất kho", FileName(cur, _prof.Code), "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.Export(_prof, cur, path))) { _status.Text = "Đã xuất " + Path.GetFileName(path); Ui.OpenExternal(path); RefreshAll(); }
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
        var save = Btn("💾 Lưu danh mục", async () => await SaveCfgAsync(), primary: true); save.IsEnabled = _edit;
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 0, 8) };
        top.Children.Add(new TextBlock { Text = "Mã hàng = tiền tố 13 ký tự (theo Loại + số quy cách) + Size 4 số + Mã gói 4 ký tự + số kết quả kháng sinh 4 số. Mã gói = ký tự 18–21 của mã hàng (trọng lượng + quy cách đóng thùng trong Tổng hợp › Mã hóa sản phẩm).", TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Classes = { "muted" } });
        top.Children.Add(Section("Loại hàng (mã loại gõ ở cột Loại: BTP = HLSO, NL = HOSO…)", Framed(_lg)));
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

    private void RenderCfg()
    {
        var cfg = Svc.Cfg; _lm.ReadOnly = !_edit; _qm.ReadOnly = !_edit;
        _lm.Load(new[] { new GridColumn("code", "Mã loại", 90), new GridColumn("ten", "Tên (HLSO / HOSO…)", 200) }, cfg.Loai.Select(l => new[] { l.Code, l.Ten }));
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
        for (var r = 0; r < _lm.RowCount; r++) if (_lm.Get(r, 0).Trim().Length > 0) loai.Add(new CargoLoai { Code = _lm.Get(r, 0), Ten = _lm.Get(r, 1) });
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

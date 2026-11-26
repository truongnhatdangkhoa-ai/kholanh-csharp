using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using KhoLanh.Core.Modules;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.ChungTu;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Services;
using static KhoLanh.Core.ChungTu.ChungTuService;

namespace KhoLanh.App.Views;

/// <summary>
/// Module In chứng từ kho (C#, theo "In chứng từ kho" của app cũ — không dùng WebView2).
/// Nạp Excel xuất kho → mỗi ngày chứng từ là 1 Phiếu xuất (DISPATCHING NOTE, BM02/QT23); gộp nhiều ngày, tách dòng sang phiếu riêng,
/// bổ sung tên hàng bị cắt từ Mã hóa sản phẩm, người ký, in / lưu PDF qua trình duyệt, xuất Excel theo mẫu.
/// </summary>
public sealed class ChungTuView : UserControl, IPage
{
    public string Title => "In chứng từ kho";
    public string Subtitle => "Nạp file Excel xuất kho (Xuất TBB / Xuất Xả / Xuất Kiểm) → Phiếu xuất theo ngày, gộp / tách phiếu, in hoặc xuất Excel theo mẫu BM02/QT23.";

    private static ChungTuService Svc => App.Services.ChungTu;
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45");
    private static readonly IBrush Warn = AppTheme.Brush("#B23B00");
    private static readonly IBrush TachBg = AppTheme.Brush("#FFF1CC");
    private static readonly IBrush MergedBg = AppTheme.Brush("#E3F2FB");
    private static readonly IBrush GreenFg = AppTheme.Brush("#0A8A3A");

    private readonly bool _edit, _export;
    private bool _loading;
    private string _mod = "tbb";
    private readonly TabControl _kinds = new();       // 1.84: Xuất TBB / Xả / Kiểm = tab cạnh tên trang
    private readonly DockPanel _body = new();
    private InfoLine _info = null!;
    private readonly TextBlock _file = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _warn = new() { FontSize = 12.5, Foreground = Warn, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Dictionary<string, TextBox> _form = new();
    private readonly TabControl _tabs;

    private readonly GridModel _gm = new() { ReadOnly = true };
    private readonly SheetGrid _gg;
    private readonly GridModel _im = new() { ReadOnly = true };
    private readonly SheetGrid _ig;
    private readonly TextBlock _gInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private List<Group> _groups = new();

    private readonly GridModel _rm = new() { ReadOnly = true };
    private readonly SheetGrid _rg;
    private readonly TextBlock _rInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _q = new() { Watermark = "Tìm mã, tên hàng…", Width = 240 };
    private List<Row> _rows = new();

    private readonly GridModel _sm = new();
    private readonly SheetGrid _sg;
    private bool _signDirty;

    public ChungTuView()
    {
        _edit = Svc.CanEdit; _export = Svc.CanExport;
        _gg = new SheetGrid(_gm) { SpareRows = 0, CellBackground = (r, _) => r < _groups.Count ? (_groups[r].Tach ? TachBg : _groups[r].IsMerged ? MergedBg : null) : null };
        _gg.SelectionChanged += ShowItems;
        _gg.RowChecks = true; _gg.ChecksChanged += ShowItems;   // 1.93: ô tích chọn ngày in / gộp / xuất (ô tiêu đề = tích hết)
        _ig = new SheetGrid(_im) { SpareRows = 0 };
        _rg = new SheetGrid(_rm) { SpareRows = 0, CellBackground = (r, _) => r < _rows.Count && _rows[r].Tach ? TachBg : null };
        _sg = new SheetGrid(_sm) { SpareRows = 3 };
        _sm.Changed += () => { if (!_loading) _signDirty = true; };

        var form = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
        (string Key, string Label, double W)[] fields =
        {
            ("fSoPhieu", "Số phiếu (nhãn)", 130), ("fNgayLap", "Ngày lập (yyyy-mm-dd)", 150), ("fKho", "Bộ phận", 190), ("fLoaiChungTu", "Loại chứng từ", 200),
            ("fMaBieuMau", "Mã biểu mẫu", 110), ("fSoBanMoiNgay", "Số bản / ngày", 80), ("fGhiChu", "Ghi chú", 400),
        };
        foreach (var (key, label, w) in fields)
        {
            var tb = new TextBox { Width = w, FontSize = 12.5, IsEnabled = _edit };
            tb.LostFocus += (_, _) => { if (!_loading) SaveForm(); };
            _form[key] = tb;
            var st = new StackPanel(); st.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, FontSize = 11.5 }); st.Children.Add(tb);
            form.Children.Add(st);
        }
        // 1.84 (mẫu chuẩn kiểu 3): 7 ô thông tin + Người ký thu thành 1 dòng tóm tắt, bấm "Sửa thông tin" mới mở
        // 1.127: rộng 780 (vừa 1366×768): ô thông tin xếp 2 hàng, 5 cột người ký 134 px
        var editor = new StackPanel { Spacing = 10, Width = 780 };
        editor.Children.Add(form);
        editor.Children.Add(BuildSigners());
        _info = new InfoLine("Thông tin phiếu", InfoItems, editor);
        _info.Closed += () => { if (!_loading) SaveForm(); };

        foreach (var k in Kinds) _kinds.Items.Add(new TabItem { Header = k.Label, Tag = k.Key });
        _kinds.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, _kinds)) return;
            PlaceBody();
            if (_loading || _kinds.SelectedItem is not TabItem { Tag: string key } || key == _mod) return;
            SaveForm(); _mod = key; Svc.Mod = key; RenderAll(); SaveForm();
        };

        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Phiếu theo ngày", Content = BuildGroups() },
                new TabItem { Header = "Bảng dữ liệu", Content = BuildRows() },
            },
        };
        _tabs.SelectionChanged += (_, e) => e.Handled = true;
        _warn.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(_warn, Dock.Top);
        _body.Children.Add(_warn); _body.Children.Add(_tabs);
        // file đang dùng + trạng thái: nhãn nhỏ bên phải hàng tên trang (MainWindow.AlignPageTabs kéo lên)
        var chip = new Border { Classes = { "chip" }, Child = _file };
        _file.TextWrapping = TextWrapping.NoWrap; _file.FontSize = 12;
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        top.Children.Add(_status); top.Children.Add(chip);
        _status.TextWrapping = TextWrapping.NoWrap;
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(_kinds);
        Content = root;
        PlaceBody();
        AttachedToVisualTree += async (_, _) => { if (!Ui.SkipReload(this)) await LoadAsync(); };   // 1.136: quay lại trang dữ liệu chưa đổi → không đọc lại
    }

    /// <summary>Nội dung (2 tab con) dùng chung cho 3 loại chứng từ: gắn vào tab loại đang chọn.</summary>
    private void PlaceBody()
    {
        foreach (var it in _kinds.Items.OfType<TabItem>()) if (!ReferenceEquals(it, _kinds.SelectedItem) && ReferenceEquals(it.Content, _body)) it.Content = null;
        if (_kinds.SelectedItem is TabItem sel && !ReferenceEquals(sel.Content, _body)) sel.Content = _body;
        else if (_kinds.SelectedItem is null && _kinds.ItemCount > 0) _kinds.SelectedIndex = 0;
    }

    private IEnumerable<(string, string)> InfoItems()
    {
        string F(string k) => _form.TryGetValue(k, out var tb) ? (tb.Text ?? "").Trim() : "";
        yield return ("Số phiếu", F("fSoPhieu"));
        yield return ("Ngày lập", F("fNgayLap"));
        yield return ("Bộ phận", F("fKho"));
        yield return ("Mẫu", F("fMaBieuMau"));
        yield return ("Bản/ngày", F("fSoBanMoiNgay"));
        var signer = _sm.RowCount > 0 ? string.Join(", ", Enumerable.Range(0, _sm.ColCount).Select(c => _sm.Get(0, c)).Where(x => !string.IsNullOrWhiteSpace(x))) : "";
        yield return ("Người ký", signer);
        if (F("fGhiChu") is { Length: > 0 } gc) yield return ("Ghi chú", gc);
    }

    // ------------------------------------------------------------------ chung

    private static Button Btn(string text, Func<Task> click, bool primary = false, bool danger = false, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (danger) b.Classes.Add("danger");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => { _ = click(); };
        return b;
    }
    private static TextBlock Label(string t, bool bold = false, double size = 12.5) => new() { Text = t, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private static Border Framed(Control c) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = c };

    private async Task LoadAsync()
    {
        _status.Text = "Đang tải…";
        try { await Task.Run(Svc.Load); }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        _status.Text = "";
        _mod = Svc.Mod;
        RenderAll();
    }

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

    private void SaveForm()
    {
        if (!_edit) return;
        foreach (var (k, tb) in _form) Svc.Form[k] = (tb.Text ?? "").Trim();
        try { Svc.SaveForm(); } catch (Exception e) { _status.Text = e is BusinessException ? e.Message : "Không lưu được: " + e.Message; }
    }

    private void RenderAll()
    {
        _loading = true;
        _kinds.SelectedIndex = Math.Max(0, Array.FindIndex(Kinds, k => k.Key == _mod)); PlaceBody();
        foreach (var (k, tb) in _form) tb.Text = Svc.Form.GetValueOrDefault(k, "");
        var f = Svc.Files[_mod];
        _file.Text = f.Length > 0 ? $"📄 {f} — {Svc.Rows[_mod].Count:#,0} dòng" : "Chưa nạp dữ liệu cho " + K(_mod).Label + ".";
        _loading = false;
        RenderGroups(); RenderRows(); RenderSigners();
        _info.Refresh();
    }

    // ------------------------------------------------------------------ nạp / xóa

    private async Task ImportFile()
    {
        var path = await Ui.OpenFile(this, "Chọn file Excel xuất kho", "xlsx", "Excel (.xlsx)");
        if (path is null) return;
        if (Svc.Rows[_mod].Count > 0 && !await Ui.Confirm(this, $"{K(_mod).Label} đang có {Svc.Rows[_mod].Count:#,0} dòng. Nạp file mới sẽ thay toàn bộ dữ liệu và các ngày đã gộp của loại phiếu này. Tiếp tục?", "Nạp file mới", "Nạp")) return;
        _warn.IsVisible = false;
        string warning = "";
        try
        {
            if (await Act(() => warning = Svc.Import(_mod, path), $"Đã nạp {Path.GetFileName(path)}"))
            { _warn.Text = warning; _warn.IsVisible = warning.Length > 0; _tabs.SelectedIndex = 0; }
        }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e)); }
    }

    private async Task ClearData()
    {
        if (Svc.Rows[_mod].Count == 0) return;
        if (!await Ui.Confirm(this, $"Xóa toàn bộ dữ liệu đã nạp của {K(_mod).Label}?", "Xóa dữ liệu", "Xóa")) return;
        await Act(() => Svc.Clear(_mod), "Đã xóa");
    }

    // ------------------------------------------------------------------ tab Phiếu theo ngày

    private Control BuildGroups()
    {
        var tools = new ToolRow { Margin = new Thickness(0, 0, 0, 6) };
        AddFileButtons(tools);
        tools.Children.Add(ToolRow.Gap());
        if (_edit)
        {
            tools.Children.Add(Btn("Gộp ngày chọn", async () => await DoMerge(), tip: "Tích ô đầu các ngày liền nhau (hoặc kéo chuột chọn) rồi bấm"));
            tools.Children.Add(ToolRow.More(Btn("Bỏ gộp phiếu chọn", async () => await DoUnmerge())));
            tools.Children.Add(ToolRow.More(Btn("Bỏ gộp tất cả", async () => { if (await Ui.Confirm(this, "Bỏ gộp tất cả các phiếu đã gộp?")) await Act(() => Svc.UnmergeAll(_mod)); })));
        }
        if (_export)
        {
            tools.Children.Add(Btn("Xuất Excel phiếu chọn", async () => await ExcelSel(false)));
            tools.Children.Add(ToolRow.More(Btn("Xuất Excel tất cả", async () => await ExcelSel(true))));
            tools.Children.Add(ToolRow.More(Btn("🖨 In tất cả các ngày…", async () => await PrintSel(true))));
        }
        if (_edit) tools.Children.Add(ToolRow.More(Btn("Xóa dữ liệu", async () => await ClearData(), danger: true)));
        if (_export)
            tools.Children.Add(Btn("🖨 In phiếu chọn…", async () => await PrintSel(false), primary: true, tip: "Mở trang in trong trình duyệt: In hoặc chọn \"Lưu dưới dạng PDF\""));
        // 1.84: danh sách phiếu và dòng của phiếu cạnh nhau (bỏ tầng tab thứ 3), cùng cao hết trang
        var right = new DockPanel();
        _gInfo.FontWeight = FontWeight.SemiBold; _gInfo.Margin = new Thickness(0, 0, 0, 4); _gInfo.TextWrapping = TextWrapping.NoWrap; _gInfo.TextTrimming = TextTrimming.CharacterEllipsis;
        DockPanel.SetDock(_gInfo, Dock.Top); right.Children.Add(_gInfo); right.Children.Add(Framed(_ig));
        var leftCap = new TextBlock { Text = "Phiếu theo ngày · tích ô đầu dòng để chọn ngày in (ô tiêu đề = tất cả) · bấm 1 phiếu → dòng bên phải", FontWeight = FontWeight.SemiBold, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 4) };
        var left = new DockPanel(); DockPanel.SetDock(leftCap, Dock.Top); left.Children.Add(leftCap); left.Children.Add(Framed(_gg));
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("1*,8,1*") };
        Grid.SetColumn(left, 0); Grid.SetColumn(right, 2);
        split.Children.Add(left); split.Children.Add(right);
        var all = new DockPanel();
        DockPanel.SetDock(tools, Dock.Top); DockPanel.SetDock(_info, Dock.Top);
        all.Children.Add(tools); all.Children.Add(_info); all.Children.Add(split);
        return all;
    }

    /// <summary>Nạp file / tải lại — đầu thanh công cụ của cả 2 tab con.</summary>
    private void AddFileButtons(ToolRow tools)
    {
        if (_edit) tools.Children.Add(Btn("⬆ Nạp file Excel…", async () => await ImportFile()));
        var re = Btn("⟳", async () => await LoadAsync(), tip: "Tải lại");
        tools.Children.Add(re);
    }

    private void RenderGroups()
    {
        _groups = Svc.Effective(_mod);
        _gm.Load(new[]
        {
            new GridColumn("so", "Số phiếu", 95), new GridColumn("ngay", "Ngày chứng từ", 150), new GridColumn("ma", "Mã hàng", 75, "number"),
            new GridColumn("sl", "Tổng SL", 80, "number"), new GridColumn("net", "Tổng Net (kg)", 105, "number"), new GridColumn("loai", "Loại", 90),
        }, _groups.Select(g => new[]
        {
            g.SoPhieu, g.IsMerged ? string.Join(" → ", g.Labels) : g.Label, g.Items.Count.ToString(), Fmt(g.SumSL), FmtNet(g.SumNet),
            g.Tach ? "✂ Tách riêng" : g.IsMerged ? "Đã gộp" : "",
        }));
        _gg.Refresh();
        if (_groups.Count > 0) _gg.Select(0, 0);
        ShowItems();
    }

    /// <summary>Phiếu cần in / gộp / xuất: các ngày đã TÍCH; chưa tích ngày nào thì dùng các dòng đang chọn (bôi đen).</summary>
    private IEnumerable<Group> Selected()
    {
        var t = _gg.CheckedRows.Where(r => r < _groups.Count).Select(r => _groups[r]).ToList();
        return t.Count > 0 ? t : Cursor();
    }

    private IEnumerable<Group> Cursor()
    {
        var s = _gg.Selection;
        for (var r = Math.Max(0, s.R1); r <= s.R2 && r < _groups.Count; r++) yield return _groups[r];
    }

    private void ShowItems()
    {
        var sel = Cursor().ToList();
        var tick = _gg.CheckedRows.Where(r => r < _groups.Count).Select(r => _groups[r]).ToList();
        var tickTxt = tick.Count > 0 ? $"Đã tích {tick.Count} ngày · SL {Fmt(tick.Sum(x => x.SumSL))} · Net {FmtNet(tick.Sum(x => x.SumNet))} kg — In / Xuất / Gộp theo ngày đã tích · " : "";
        if (sel.Count == 0)
        {
            _gInfo.Text = tickTxt;
            if (tickTxt.Length == 0) _gInfo.Text = Svc.Rows[_mod].Count == 0 ? "Nạp file Excel xuất kho để bắt đầu." : "";
            _im.Load(Array.Empty<GridColumn>(), Array.Empty<string[]>()); _ig.Refresh();
            return;
        }
        var g = sel[0];
        _gInfo.Text = tickTxt + (sel.Count > 1
            ? $"Đã chọn {sel.Count} phiếu · {sel.Sum(x => x.Items.Count):#,0} mã · SL {Fmt(sel.Sum(x => x.SumSL))} · Net {FmtNet(sel.Sum(x => x.SumNet))} kg"
            : $"Phiếu {g.SoPhieu} — {g.DateLine}" + (g.Tach ? " (phiếu tách riêng cùng ngày)" : "") + $" · {g.Items.Count:#,0} mã · SL {Fmt(g.SumSL)} · Net {FmtNet(g.SumNet)} kg");
        _im.Load(new[]
        {
            new GridColumn("stt", "STT", 45, "number"), new GridColumn("ma", "Mã hàng", 190), new GridColumn("ten", "Tên hàng", 380), new GridColumn("size", "Size", 70),
            new GridColumn("sl", "Số lượng", 80, "number"), new GridColumn("tl", "W/pkg", 70, "number"), new GridColumn("net", "Net (kg)", 90, "number"),
        }, g.Items.Select((r, i) => new[] { (i + 1).ToString(), r.MaHang, r.TenHang, r.SizeText, Fmt(r.SoLuong), Fmt(r.TrongLuong), FmtNet(r.Net) })
            .Append(new[] { "", "Tổng kiện", "", "", Fmt(g.SumSL), "", FmtNet(g.SumNet) }));
        _ig.Refresh();
    }

    private async Task DoMerge()
    {
        var sel = Selected().ToList();
        if (sel.Count < 2) { await Ui.Message(this, "Tích (hoặc chọn) ít nhất 2 ngày liền nhau trong danh sách để gộp thành 1 phiếu."); return; }
        await Act(() => Svc.Merge(_mod, sel), $"Đã gộp {sel.Count} ngày thành 1 phiếu");
    }

    private async Task DoUnmerge()
    {
        var sel = Selected().Where(g => g.IsMerged).ToList();
        if (sel.Count == 0) { await Ui.Message(this, "Chọn phiếu đã gộp (nền xanh) để bỏ gộp."); return; }
        await Act(() => { foreach (var g in sel) Svc.Unmerge(_mod, g); }, "Đã bỏ gộp");
    }

    private async Task PrintSel(bool all)
    {
        var list = all ? _groups : Selected().ToList();
        if (list.Count == 0) { await Ui.Message(this, "Chọn ít nhất 1 phiếu trong danh sách."); return; }
        var ans = await Ui.Prompt(this, $"Số bản in cho mỗi ngày (sắp in {list.Count} phiếu):", "Số bản in / ngày", Svc.Copies.ToString());
        if (ans is null) return;
        if (!int.TryParse(ans.Trim(), out var n) || n < 1) { await Ui.Message(this, "Số bản phải là số nguyên từ 1 đến 20."); return; }
        n = Math.Min(20, n);
        _form["fSoBanMoiNgay"].Text = n.ToString(); SaveForm();
        var dir = Path.Combine(App.DataFolder, "in-chung-tu");
        string path = "";
        if (await Act(() => path = Svc.WritePrintFile(_mod, list, n, dir)))
        {
            Ui.OpenExternal(path);
            _status.Text = "Đã mở trang in trong trình duyệt — bấm In hoặc chọn \"Lưu dưới dạng PDF\". " + path;
        }
    }

    private async Task ExcelSel(bool all)
    {
        var list = all ? _groups : Selected().ToList();
        if (list.Count == 0) { await Ui.Message(this, "Chọn ít nhất 1 phiếu trong danh sách."); return; }
        var name = list.Count == 1 ? list[0].SoPhieu : (Svc.Meta().SoPhieu is { Length: > 1 } sp && sp != "—" ? sp : K(_mod).Prefix);
        var path = await Ui.SaveFile(this, "Xuất Excel phiếu xuất", Ui.SafeFileName(name) + ".xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Act(() => Svc.ExportExcel(_mod, list, path), $"Đã xuất {list.Count} phiếu ra {Path.GetFileName(path)}")) Ui.Exported(this, path);
    }

    // ------------------------------------------------------------------ tab Bảng dữ liệu

    private Control BuildRows()
    {
        var tools = new ToolRow { Margin = new Thickness(0, 0, 0, 6) };
        _q.TextChanged += (_, _) => { if (!_loading) RenderRows(); };
        AddFileButtons(tools);
        tools.Children.Add(_q);
        tools.Children.Add(ToolRow.Gap());
        if (_edit)
        {
            tools.Children.Add(Btn("✂ Tách dòng chọn sang phiếu riêng", async () => await DoTach(true), tip: "Dòng được tách thành phiếu riêng cùng ngày (nền vàng) — nhãn chỉ hiện trong app, không in"));
            tools.Children.Add(ToolRow.More(Btn("Gộp lại về phiếu thường", async () => await DoTach(false))));
            tools.Children.Add(ToolRow.More(Btn("✂ Tách các dòng có ghi chú gợi ý", async () => await TachHints(), tip: "Ghi chú trong file (VD markfood) chỉ để gợi ý, không in ra phiếu")));
            tools.Children.Add(Btn("Bổ sung tên hàng…", async () => await NamesDialog(), tip: "Ghép lại tên đầy đủ từ mã 21 ký tự theo Mã hóa sản phẩm (tên trong file gốc hay bị cắt ~100 ký tự)"));
        }
        var dock = new DockPanel();
        DockPanel.SetDock(tools, Dock.Top); dock.Children.Add(tools);
        dock.Children.Add(Framed(_rg));   // 1.84: số dòng / SL / Net ở dòng Tổng của bảng
        return dock;
    }

    private void RenderRows()
    {
        var all = Svc.Rows[_mod];
        var q = StripAccents(_q.Text ?? "");
        _rows = q.Length == 0 ? all.ToList() : all.Where(r => StripAccents(r.MaHang + " " + r.TenHang + " " + r.NgayChungTu + " " + r.GoiY).Contains(q)).ToList();
        _rm.Load(new[]
        {
            new GridColumn("stt", "STT", 50, "number"), new GridColumn("ma", "Mã hàng", 190), new GridColumn("ten", "Tên hàng", 400), new GridColumn("size", "Size", 70),
            new GridColumn("sl", "Số lượng", 80, "number"), new GridColumn("ngay", "Ngày chứng từ", 110), new GridColumn("tl", "Trọng lượng", 90, "number"),
            new GridColumn("net", "Net (kg)", 95, "number"), new GridColumn("tach", "Tách", 60), new GridColumn("gy", "Gợi ý (không in)", 130),
        }, _rows.Select((r, i) => new[] { (i + 1).ToString(), r.MaHang, r.TenHang, r.SizeText, Fmt(r.SoLuong), r.NgayChungTu, Fmt(r.TrongLuong), FmtNet(r.Net), r.Tach ? "✂" : "", r.GoiY }));
        _rg.Refresh();
        _rg.Status = _rInfo.Text = $"{_rows.Count:#,0}" + (_rows.Count != all.Count ? $" / {all.Count:#,0}" : "") + $" dòng · SL {Fmt(_rows.Sum(r => r.SoLuong))} · Net {FmtNet(_rows.Sum(r => r.Net))} kg"
            + (all.Count(r => r.GoiY.Length > 0 && !r.Tach) is var h and > 0 ? $" · {h} dòng có ghi chú gợi ý trong file" : "");
    }

    private IEnumerable<Row> SelectedRows()
    {
        var s = _rg.Selection;
        for (var r = Math.Max(0, s.R1); r <= s.R2 && r < _rows.Count; r++) yield return _rows[r];
    }

    private async Task DoTach(bool v)
    {
        var sel = SelectedRows().ToList();
        if (sel.Count == 0) { await Ui.Message(this, "Chọn các dòng trong bảng (kéo chuột / Shift+mũi tên)."); return; }
        await Act(() => Svc.SetTach(_mod, sel, v), v ? "Đã tách sang phiếu riêng" : "Đã gộp lại về phiếu thường");
    }

    private async Task TachHints()
    {
        var rows = Svc.Rows[_mod].Where(r => !r.Tach && r.GoiY.Length > 0).ToList();
        if (rows.Count == 0) { await Ui.Message(this, "Không có dòng nào có ghi chú gợi ý trong file."); return; }
        var names = string.Join(", ", rows.Select(r => r.GoiY).Distinct().Take(5));
        if (!await Ui.Confirm(this, $"Tách {rows.Count} dòng có ghi chú trong file ({names}) sang phiếu riêng cùng ngày?\n\nGhi chú chỉ để gợi ý, không in ra phiếu. Có thể gộp lại bất cứ lúc nào.", "Tách theo gợi ý", "Tách")) return;
        await Act(() => Svc.SetTach(_mod, rows, true), $"Đã tách {rows.Count} dòng");
    }

    private async Task NamesDialog()
    {
        if (Svc.Rows[_mod].Count == 0) { await Ui.Message(this, "Chưa có dữ liệu — hãy nạp file trước."); return; }
        List<NameFix> fixes;
        try { fixes = await Task.Run(() => Svc.AnalyzeNames(_mod, true)); }
        catch (BusinessException e) { await Ui.Message(this, e.Message); return; }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e)); return; }
        var chkAll = new CheckBox { Content = "Ghi đè cả những tên khác cách ghi trong Mã hóa (mặc định chỉ bổ sung tên bị cụt / ô trống)", FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0) };
        var chkMark = new CheckBox { Content = "Tô xanh các tên đã bổ sung trong bảng (phiếu in vẫn chữ đen)", IsChecked = true, FontSize = 12.5 };
        var sum = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 4) };
        var pm = new GridModel { ReadOnly = true };
        var pg = new SheetGrid(pm) { SpareRows = 0, Height = 280 };
        var go = new Button { Content = "Bổ sung", Classes = { "primary" }, IsDefault = true };
        var close = new Button { Content = "Đóng", IsCancel = true };
        List<NameFix> cur = new();
        void Recalc()
        {
            var all = chkAll.IsChecked == true;
            cur = fixes.Where(f => ShouldFix(f, all)).ToList();
            var errs = fixes.Where(f => f.Status == "loi").ToList();
            sum.Text = $"Sẽ bổ sung {cur.Count} dòng (tên bị cụt: {fixes.Count(f => f.Status == "cut")} · ô tên trống: {fixes.Count(f => f.Status == "thieu")}{(all ? $" · khác cách ghi: {fixes.Count(f => f.Status == "khac")}" : "")}). "
                + $"Giữ nguyên: {fixes.Count(f => f.Status == "dung")} dòng đã đủ tên" + (errs.Count > 0 ? $" · {errs.Count} dòng mã lỗi / không có trong Mã hóa (không đoán, bỏ qua). Ví dụ: {errs[0].Err}" : "") + ".";
            pm.Load(new[] { new GridColumn("ma", "Mã hàng", 180), new GridColumn("old", "Tên cũ", 300), new GridColumn("new", "Tên mới (đủ)", 420) },
                cur.Take(300).Select(f => new[] { f.Row.MaHang, f.Old.Length > 0 ? f.Old : "(trống)", f.New }));
            pg.Refresh();
            go.Content = $"Bổ sung {cur.Count} dòng"; go.IsEnabled = cur.Count > 0;
        }
        chkAll.IsCheckedChanged += (_, _) => Recalc();
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = $"Cột \"Tên hàng\" của file gốc hay bị cắt ở khoảng 100 ký tự. Công cụ ghép lại tên đầy đủ từ mã 21 ký tự theo Mã hóa sản phẩm (module Tổng hợp), chỉ áp cho {fixes.Count:#,0} dòng của {K(_mod).Label}. Phiếu in và file Excel xuất ra dùng tên mới.", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        body.Children.Add(chkAll); body.Children.Add(chkMark); body.Children.Add(sum); body.Children.Add(Framed(pg));
        body.Children.Add(Ui.Buttons(close, go));
        var w = Ui.Dialog("Bổ sung tên hàng — " + K(_mod).Label, body, 900);
        var doIt = false;
        go.Click += (_, _) => { doIt = true; w.Close(); };
        close.Click += (_, _) => w.Close();
        Recalc();
        await Ui.Show(this, w);
        if (doIt && cur.Count > 0) { var list = cur; var mark = chkMark.IsChecked == true; await Act(() => Svc.ApplyNames(_mod, list, mark), $"Đã bổ sung {list.Count} tên hàng"); }
    }

    // ------------------------------------------------------------------ tab Người ký

    private Control BuildSigners()
    {
        var info = new TextBlock { Text = "Người ký (in trên phiếu = DÒNG ĐẦU của bảng; dòng sau lưu sẵn tên thay thế).", FontWeight = FontWeight.SemiBold, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), Tag = "keep" };
        var save = Btn("Lưu người ký", async () =>
        {
            var rows = new List<Dictionary<string, string>>();
            var k = K(_mod);
            for (var r = 0; r < _sm.RowCount; r++) rows.Add(k.SignCols.Select((c, i) => (c, v: _sm.Get(r, i))).ToDictionary(x => x.c, x => x.v));
            if (await Act(() => Svc.SaveSigners(_mod, rows), "Đã lưu người ký")) { _signDirty = false; _info.Refresh(); }
        });
        save.IsEnabled = _edit;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 4) };
        tools.Children.Add(save);
        var dock = new DockPanel();
        DockPanel.SetDock(info, Dock.Top); DockPanel.SetDock(tools, Dock.Top);
        _sg.Height = 170;
        dock.Children.Add(info); dock.Children.Add(tools); dock.Children.Add(Framed(_sg));
        return dock;
    }

    private void RenderSigners()
    {
        var k = K(_mod);
        _loading = true;
        _sm.ReadOnly = !_edit;
        _sm.Load(k.Roles.Select((r, i) => new GridColumn(k.SignCols[i], r, 134)),
            Svc.Signers[_mod].Select(row => k.SignCols.Select(c => row.GetValueOrDefault(c) ?? "").ToArray()));
        _sg.Refresh();
        _loading = false;
        _signDirty = false;
    }
}

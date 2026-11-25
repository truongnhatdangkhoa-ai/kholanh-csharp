using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;

namespace KhoLanh.App.Views;

/// <summary>Module Tổng hợp (C#): Mã hóa sản phẩm, Danh sách mã hàng, Dữ liệu tổng hợp (Bảng 1 / Bảng 2), Ánh xạ tên hàng.</summary>
public sealed class TongHopView : UserControl, IPage
{
    public string Title => "Tổng hợp";
    public string Subtitle => "Mã hóa sản phẩm 21 ký tự, danh sách mã hàng, gộp dữ liệu theo Tên sản phẩm + Size, ánh xạ tên hàng.";

    private static TongHopService Svc => App.Services.TongHop;
    private static readonly IBrush Bad = AppTheme.Brush("#FDE2E1");
    private static readonly IBrush Warn = AppTheme.Brush("#FFF1C9");
    private static readonly IBrush Total = AppTheme.Brush("#FFF4CC");

    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _dirty, _loading;
    private readonly TextBlock _saveState = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly bool _edit;

    // --- Mã hóa sản phẩm
    private readonly ListBox _segList = new();
    private readonly GridModel _segModel = new();
    private readonly SheetGrid _segGrid;
    private readonly TextBlock _segInfo = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly StackPanel _segButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly Panel _builderHost = new();
    private int _seg = 1; // 0 = Danh sách mã hàng
    private readonly TextBox _find = new() { Watermark = "Tìm mã / diễn giải (Enter = tìm tiếp)", Width = 280 };

    // --- Dữ liệu tổng hợp
    private readonly GridModel _t1 = new() { ReadOnly = true }, _t2 = new() { ReadOnly = true };
    private readonly SheetGrid _t1Grid, _t2Grid;
    private readonly TextBlock _t1Info = new() { Classes = { "muted" }, FontSize = 12 }, _t2Info = new() { Classes = { "muted" }, FontSize = 12 };
    private HashSet<int> _t2TotalRows = new();

    // --- Ánh xạ
    private readonly GridModel _axRules = new(), _axCombos = new(), _axSegs = new();
    private readonly SheetGrid _axRulesGrid, _axCombosGrid, _axSegsGrid;
    private readonly WrapPanel _axOpts = new() { ItemSpacing = 12, LineSpacing = 4 };
    private readonly TextBox _axTest = new() { Watermark = "Thử một mã hàng 21 ký tự…", Width = 260 };
    private readonly TextBlock _axTestOut = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Dictionary<string, CheckBox> _optBoxes = new();

    public TongHopView()
    {
        _edit = Svc.CanEdit;
        _segGrid = new SheetGrid(_segModel) { CellBackground = SegCellBg, Display = SegDisplay };
        _t1Grid = new SheetGrid(_t1);
        _t2Grid = new SheetGrid(_t2) { CellBackground = (r, _) => _t2TotalRows.Contains(r) ? Total : null, Display = NumDisplay };
        _axRulesGrid = new SheetGrid(_axRules) { SpareRows = 10 };
        _axCombosGrid = new SheetGrid(_axCombos) { SpareRows = 5 };
        _axSegsGrid = new SheetGrid(_axSegs) { SpareRows = 3 };
        _autosave.Tick += async (_, _) => { _autosave.Stop(); await SaveAsync(); };
        _segModel.Changed += () => { if (!_loading) { PullSegGrid(); MarkDirty(); } };
        foreach (var m in new[] { _axRules, _axCombos, _axSegs }) m.Changed += () => { if (!_loading) { _axDirty = true; MarkDirty(); } };
        DetachedFromVisualTree += async (_, _) => { if (_dirty) await SaveAsync(); };
        KeyDown += async (_, e) => { if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; await SaveAsync(); } };

        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Mã hóa sản phẩm", Content = BuildCatalogTab() },
                new TabItem { Header = "Dữ liệu tổng hợp", Content = BuildTab2() },
                new TabItem { Header = "Ánh xạ tên hàng", Content = BuildAnhXaTab() },
            },
        };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_saveState, Dock.Right);
        top.Children.Add(_saveState);
        top.Children.Add(new TextBlock { Text = _edit ? "Tự lưu sau mỗi lần sửa (Ctrl+S lưu ngay). Dữ liệu dùng chung với Tồn kho An An / Tồn kho gửi." : "Chỉ xem (tài khoản không có quyền sửa Tổng hợp).", Classes = { "muted" }, FontSize = 12 });
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(tabs);
        Content = root;
        AttachedToVisualTree += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _saveState.Text = "Đang đọc dữ liệu…";
        try { await Task.Run(Svc.Load); }
        catch (Exception ex) { _saveState.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        _saveState.Text = "";
        _segList.SelectedIndex = 0;
        ShowSegment(1);
        ShowTab2();
        ShowAnhXa();
    }

    private void MarkDirty()
    {
        if (!_edit) return;
        _dirty = true; _saveState.Text = "Chưa lưu…";
        _autosave.Stop(); _autosave.Start();
    }

    private bool _saving, _axDirty;
    private async Task SaveAsync()
    {
        if (!_dirty || _saving || !_edit) return;
        if (_axDirty && !PullAnhXa()) return;
        _saving = true; _saveState.Text = "Đang lưu…";
        try
        {
            await Task.Run(Svc.Save);
            _dirty = false; _axDirty = false;
            _saveState.Text = $"Đã lưu {DateTime.Now:HH:mm:ss}";
        }
        catch (SheetConflictException)
        {
            _saveState.Text = "Chưa lưu";
            await Ui.Message(this, "Dữ liệu Tổng hợp vừa được máy khác sửa nên phần bạn vừa sửa CHƯA được lưu (để không ghi đè). Bấm Đồng ý để tải bản mới nhất rồi làm lại.", "Máy khác vừa sửa");
            _dirty = false;
            await LoadAsync();
        }
        catch (Exception ex) { _saveState.Text = "Lưu lỗi"; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex), "Lỗi lưu"); }
        finally { _saving = false; }
    }

    private static Button Btn(string text, Action click, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        b.Click += (_, _) => click();
        return b;
    }

    // =====================================================================================
    // Mã hóa sản phẩm + Danh sách mã hàng
    // =====================================================================================

    private Control BuildCatalogTab()
    {
        var items = TongHopService.Segs.Select(s => $"{s.Seg}. {s.Name}  ({s.Length} ký tự)").ToList();
        items.Add("★ Danh sách mã hàng");
        _segList.ItemsSource = items;
        _segList.SelectionChanged += (_, _) =>
        {
            var i = _segList.SelectedIndex;
            if (i < 0) return;
            ShowSegment(i < TongHopService.Segs.Length ? i + 1 : 0);
        };
        var left = new Border { Classes = { "card" }, Padding = new Thickness(6), Child = _segList, Width = 250 };
        _find.KeyDown += (_, e) => { if (e.Key == Key.Enter) { FindNext(); e.Handled = true; } };
        var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        var row1 = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        row1.Children.Add(_find);
        row1.Children.Add(_segButtons);
        head.Children.Add(row1);
        head.Children.Add(_segInfo);
        head.Children.Add(_builderHost);
        var right = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        right.Children.Add(head);
        right.Children.Add(_segGrid);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        g.Children.Add(left);
        Grid.SetColumn(right, 1); g.Children.Add(right);
        return g;
    }

    private void ShowSegment(int seg)
    {
        _seg = seg;
        _loading = true;
        _segModel.ReadOnly = !_edit;
        _segButtons.Children.Clear();
        _builderHost.Children.Clear();
        if (seg > 0)
        {
            var def = TongHopService.Segs[seg - 1];
            _segModel.Load(new[] { new GridColumn("ma", $"Mã ({def.Length} ký tự)", 140), new GridColumn("ten", "Tên", 420) },
                Svc.SegmentData[seg].Select(e => new[] { e.Code, e.Name }));
            if (_edit)
            {
                _segButtons.Children.Add(Btn("Nhập Excel…", async () => await ImportSegment()));
                _segButtons.Children.Add(Btn("Áp danh sách chuẩn", ApplyStandard));
            }
            if (Svc.CanExport) _segButtons.Children.Add(Btn("Xuất Excel…", async () => await ExportSegment()));
        }
        else
        {
            _segModel.Load(new[] { new GridColumn("ma", "Mã hàng (21 ký tự)", 200), new GridColumn("note", "Diễn giải", 640), new GridColumn("du", "Đủ mã", 90) { ReadOnly = true } },
                Svc.Results.Select(r => new[] { r.Code, r.Note, "" }));
            if (_edit)
            {
                _segButtons.Children.Add(Btn("Nhập Excel…", async () => await ImportResults()));
                _segButtons.Children.Add(Btn("Điền diễn giải còn trống", FillNotes));
                _segButtons.Children.Add(Btn("Làm gọn diễn giải", async () => await CleanNotes()));
                _segButtons.Children.Add(Btn("Xóa mã trùng", async () => await RemoveDups()));
                _segButtons.Children.Add(Btn("Nạp danh sách mặc định", async () => await LoadDefault()));
                _builderHost.Children.Add(BuildComboBuilder());
            }
            if (Svc.CanExport) _segButtons.Children.Add(Btn("Xuất Excel…", async () => await ExportResults()));
        }
        _loading = false;
        _segGrid.Refresh();
        _segGrid.Select(0, 0);
        UpdateSegInfo();
    }

    /// <summary>Lưới → dữ liệu (sau mỗi lần sửa).</summary>
    private void PullSegGrid()
    {
        var n = _segModel.UsedRows();
        if (_seg > 0)
            Svc.SegmentData[_seg] = Enumerable.Range(0, n).Select(r => new TongHopService.Entry(_segModel.Get(r, 0).Trim(), _segModel.Get(r, 1).Trim()))
                .Where(e => e.Code.Length > 0 || e.Name.Length > 0).ToList();
        else
            Svc.Results = Enumerable.Range(0, n).Select(r => (code: _segModel.Get(r, 0).Trim(), note: _segModel.Get(r, 1)))
                .Where(x => x.code.Length > 0 || x.note.Length > 0)
                .Select((x, i) => new TongHopService.Result { Stt = i + 1, Code = x.code, Note = x.note, Complete = x.code.Length == TongHopService.CodeLength })
                .ToList();
        UpdateSegInfo();
    }

    private void UpdateSegInfo()
    {
        if (_seg > 0)
        {
            var def = TongHopService.Segs[_seg - 1];
            var list = Svc.SegmentData[_seg];
            var wrong = list.Count(e => e.Code.Length != def.Length);
            var dup = list.GroupBy(e => TongHopService.Norm(e.Code)).Count(g => g.Count() > 1);
            _segInfo.Text = $"{def.Seg}. {def.Name} · {def.Range} · {list.Count:#,0} mã" +
                            (wrong > 0 ? $" · {wrong} mã sai độ dài (tô đỏ)" : "") + (dup > 0 ? $" · {dup} mã trùng (tô vàng)" : "") +
                            (_edit ? " — gõ hoặc dán thẳng từ Excel (cột Mã, cột Tên)." : "");
        }
        else
        {
            var r = Svc.Results;
            var bad = r.Count(x => x.Code.Length != TongHopService.CodeLength);
            _segInfo.Text = $"Danh sách mã hàng · {r.Count:#,0} mã" + (bad > 0 ? $" · {bad} mã chưa đủ 21 ký tự (tô đỏ)" : "") +
                            " · Đoạn mã chưa khai báo ở Mã hóa sản phẩm tô vàng. Dán cột mã hàng từ Excel vào cột đầu; diễn giải trống được tự ghép khi bấm “Điền diễn giải còn trống”.";
        }
    }

    private IBrush? SegCellBg(int r, int c)
    {
        if (r >= _segModel.RowCount) return null;
        var code = _segModel.Get(r, 0).Trim();
        if (code.Length == 0) return null;
        if (_seg > 0)
        {
            if (c != 0) return null;
            if (code.Length != TongHopService.Segs[_seg - 1].Length) return Bad;
            var n = TongHopService.Norm(code);
            var count = 0;
            for (var i = 0; i < _segModel.RowCount && count < 2; i++) if (TongHopService.Norm(_segModel.Get(i, 0)) == n) count++;
            return count > 1 ? Warn : null;
        }
        if (c == 0 && code.Length != TongHopService.CodeLength) return Bad;
        if (c == 0 && Svc.UnknownSegments(code).Count > 0) return Warn;
        return null;
    }

    private string SegDisplay(int r, int c, string raw) => raw;

    private void FindNext()
    {
        var q = (_find.Text ?? "").Trim();
        if (q.Length == 0) return;
        var nq = TongHopService.NormSearch(q);
        var n = _segModel.RowCount;
        for (var k = 1; k <= n; k++)
        {
            var r = (_segGrid.Row + k) % n;
            if (TongHopService.NormSearch(_segModel.Get(r, 0)).Contains(nq) || TongHopService.NormSearch(_segModel.Get(r, 1)).Contains(nq))
            { _segGrid.Select(r, 0); return; }
        }
        _segInfo.Text = $"Không tìm thấy “{q}”.";
    }

    private void Reload() { _loading = true; ShowSegment(_seg); }

    private void ApplyStandard()
    {
        if (Svc.ApplyFixed(_seg, TongHopService.FixedCatalog()[_seg], overwrite: true)) { Reload(); MarkDirty(); }
        else _segInfo.Text = "Danh sách đã khớp danh sách chuẩn — không có gì thay đổi.";
    }

    private async Task<List<List<string>>?> PickExcelRows()
    {
        var path = await Ui.OpenFile(this, "Chọn file Excel", "xlsx", "Excel");
        if (path is null) return null;
        List<List<string>>? rows = null;
        await Ui.RunAsync(this, () => Task.Run(() =>
        {
            var (cols, data, _) = KhoLanh.Core.Entry.EntryService.ReadExcel(path, null, firstRowHeader: false);
            rows = data.Select(r => r.ToList()).ToList();
        }));
        return rows;
    }

    private async Task ImportSegment()
    {
        var rows = await PickExcelRows();
        if (rows is null) return;
        var def = TongHopService.Segs[_seg - 1];
        var list = Svc.SegmentData[_seg];
        int add = 0, upd = 0, wrong = 0;
        foreach (var r in rows)
        {
            var code = r.ElementAtOrDefault(0)?.Trim() ?? ""; var name = string.Join(" ", r.Skip(1).Where(x => x.Trim().Length > 0)).Trim();
            if (code.Length == 0) continue;
            if (code.Length != def.Length) { wrong++; continue; }
            var e = Svc.Find(_seg, code);
            if (e is null) { list.Add(new TongHopService.Entry(code, name)); add++; }
            else if (name.Length > 0 && e.Name != name) { e.Name = name; upd++; }
        }
        Reload(); MarkDirty();
        await Ui.Message(this, $"Đã thêm {add} mã mới, cập nhật tên {upd} mã." + (wrong > 0 ? $" Bỏ qua {wrong} dòng sai độ dài ({def.Length} ký tự)." : "") + " Ctrl+Z trong lưới không hoàn tác được thao tác này — kiểm tra trước khi tiếp tục.");
    }

    private async Task ExportSegment()
    {
        var def = TongHopService.Segs[_seg - 1];
        var path = await Ui.SaveFile(this, "Xuất Excel", Ui.SafeFileName($"Ma-hoa-{def.Seg}-{def.Name}") + ".xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.ExportList(path, def.Name, new[] { "Mã", "Tên" }, Svc.SegmentData[_seg].Select(e => new[] { e.Code, e.Name })))) Ui.OpenExternal(path);
    }

    private async Task ImportResults()
    {
        var rows = await PickExcelRows();
        if (rows is null || rows.Count == 0) return;
        // dòng đầu có chữ "mã hàng" → là tiêu đề
        var head = rows[0].Select(TongHopService.NormSearch).ToList();
        int ci = head.FindIndex(h => h.Contains("ma hang") || h == "ma" || h == "code"), ni = head.FindIndex(h => h.Contains("dien giai") || h.Contains("ghi chu") || h.Contains("ten hang"));
        var start = ci >= 0 ? 1 : 0;
        if (ci < 0) { ci = 0; ni = rows[0].Count > 1 ? 1 : -1; }
        var have = new HashSet<string>(Svc.Results.Select(r => TongHopService.Norm(r.Code)));
        int add = 0, dup = 0;
        foreach (var r in rows.Skip(start))
        {
            var code = r.ElementAtOrDefault(ci)?.Trim().ToUpperInvariant() ?? "";
            if (code.Length == 0) continue;
            if (!have.Add(TongHopService.Norm(code))) { dup++; continue; }
            var note = ni >= 0 ? r.ElementAtOrDefault(ni)?.Trim() ?? "" : "";
            Svc.Results.Add(new TongHopService.Result { Code = code, Note = note.Length > 0 ? note : Svc.BuildNote(code), Complete = code.Length == TongHopService.CodeLength });
            add++;
        }
        Svc.Renumber();
        Reload(); MarkDirty();
        await Ui.Message(this, $"Đã thêm {add} mã mới" + (dup > 0 ? $", bỏ qua {dup} mã đã có." : "."));
    }

    private async Task ExportResults()
    {
        var path = await Ui.SaveFile(this, "Xuất Excel", "Danh-sach-ma-hang.xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.ExportList(path, "Danh sách mã hàng", new[] { "STT", "Mã hàng", "Diễn giải", "Đủ mã" },
                Svc.Results.Select((r, i) => new[] { (i + 1).ToString(), r.Code, r.Note, r.Code.Length == TongHopService.CodeLength ? "Hợp lệ" : "Thiếu ký tự" })))) Ui.OpenExternal(path);
    }

    private void FillNotes()
    {
        if (Svc.RefreshNotes()) { Reload(); MarkDirty(); }
        else _segInfo.Text = "Không có dòng nào cần điền diễn giải (dòng trống diễn giải nhưng mã chưa đủ / chưa khai báo thì không ghép được).";
    }

    private async Task CleanNotes()
    {
        var changed = Svc.Results.Where(r => r.Note.Length > 0 && TongHopService.CleanNote(r.Note) != r.Note).ToList();
        if (changed.Count == 0) { await Ui.Message(this, "Diễn giải đã gọn — không có dòng nào cần sửa."); return; }
        if (!await Ui.Confirm(this, $"Có {changed.Count} diễn giải có dấu phẩy / khoảng trắng thừa. Làm gọn ngay?", "Làm gọn diễn giải", "Làm gọn")) return;
        foreach (var r in changed) r.Note = TongHopService.CleanNote(r.Note);
        Reload(); MarkDirty();
    }

    private async Task RemoveDups()
    {
        var n = Svc.Results.Count - Svc.Results.Select(r => TongHopService.Norm(r.Code)).Where(c => c.Length > 0).Distinct().Count() - Svc.Results.Count(r => r.Code.Trim().Length == 0);
        if (n <= 0) { await Ui.Message(this, "Không có mã trùng."); return; }
        if (!await Ui.Confirm(this, $"Xóa {n} dòng mã trùng (giữ dòng xuất hiện đầu tiên)?", "Xóa mã trùng", "Xóa")) return;
        Svc.RemoveDuplicates();
        Reload(); MarkDirty();
    }

    private async Task LoadDefault()
    {
        var d = TongHopService.DefaultCodes();
        if (!await Ui.Confirm(this, $"Thêm các mã còn thiếu từ danh sách mặc định ({d.Rows.Count:#,0} mã)? Mã đang có (và diễn giải đã sửa) được giữ nguyên.", "Danh sách mặc định", "Thêm")) return;
        var n = Svc.AddDefaultCodes();
        if (n > 0) { Reload(); MarkDirty(); }
        await Ui.Message(this, n > 0 ? $"Đã thêm {n:#,0} mã còn thiếu." : "Danh sách đã có đủ các mã mặc định.");
    }

    // ----- ghép mã theo tổ hợp

    private Control BuildComboBuilder()
    {
        var boxes = new List<AutoCompleteBox>();
        var preview = new TextBlock { FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var note = new TextBlock { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var wrap = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        string Code(int i) { var t = (boxes[i].Text ?? "").Trim(); var k = t.IndexOf(" — ", StringComparison.Ordinal); return k >= 0 ? t[..k] : t; }
        void Update()
        {
            var parts = TongHopService.Segs.Select((d, i) => { var c = Code(i); return c.Length == d.Length ? c : new string('·', d.Length); }).ToList();
            var code = string.Concat(parts);
            preview.Text = code;
            var ok = !code.Contains('·');
            note.Text = ok ? Svc.BuildNote(code) : "Chọn đủ 10 tổ hợp (gõ mã hoặc tên để tìm).";
        }
        foreach (var d in TongHopService.Segs)
        {
            var box = new AutoCompleteBox
            {
                Watermark = $"{d.Seg}. {d.Name}", Width = d.Seg is 1 or 6 or 8 or 9 ? 200 : 150, FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
                ItemsSource = Svc.SegmentData[d.Seg].Where(e => e.Code.Length == d.Length).Select(e => $"{e.Code} — {e.Name}").ToList(),
                MinimumPrefixLength = 0, FontSize = 12,
            };
            box.TextChanged += (_, _) => Update();
            boxes.Add(box); wrap.Children.Add(box);
        }
        var add = Btn("Thêm vào danh sách", async () =>
        {
            Update();
            var code = preview.Text ?? "";
            if (code.Contains('·')) { await Ui.Message(this, "Chưa chọn đủ 10 tổ hợp."); return; }
            if (Svc.Results.Any(r => TongHopService.Norm(r.Code) == TongHopService.Norm(code))) { await Ui.Message(this, $"Mã {code} đã có trong danh sách."); return; }
            Svc.Results.Add(new TongHopService.Result { Stt = Svc.Results.Count + 1, Code = code, Note = Svc.BuildNote(code), Complete = true });
            Reload(); MarkDirty();
            _segGrid.Select(Svc.Results.Count - 1, 0);
        }, primary: true);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        line.Children.Add(new TextBlock { Text = "Ghép mã:", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold });
        line.Children.Add(preview); line.Children.Add(add);
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(wrap); panel.Children.Add(line); panel.Children.Add(note);
        Update();
        return new Expander { Header = "Ghép mã theo tổ hợp tự chọn", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    // =====================================================================================
    // Dữ liệu tổng hợp
    // =====================================================================================

    private Control BuildTab2()
    {
        var bar = new ToolRow { Margin = new Thickness(0, 8, 0, 8) };
        if (_edit)
        {
            bar.Children.Add(Btn("Nạp file Excel…", async () => await ImportTab2(), primary: true));
            bar.Children.Add(Btn("Xóa dữ liệu Bảng 1 & 2", async () =>
            {
                if (Svc.Tab2.Rows.Count == 0) return;
                if (!await Ui.Confirm(this, "Xóa toàn bộ dữ liệu Bảng 1 (và Bảng 2 tính từ đó)?", "Xóa dữ liệu", "Xóa")) return;
                Svc.Tab2 = new TongHopService.Table(); ShowTab2(); MarkDirty();
            }));
        }
        if (Svc.CanExport) bar.Children.Add(Btn("Xuất Excel (Bảng 2)…", async () =>
        {
            var path = await Ui.SaveFile(this, "Xuất Excel", "Tong-hop-Bang-2.xlsx", "xlsx", "Excel");
            if (path is not null && await Ui.Run(this, () => Svc.ExportGroups(path))) Ui.OpenExternal(path);
        }));
        // 1.57: Bảng 1 / Bảng 2 thành 2 tab con (trước xếp chồng — màn hình nhỏ mỗi bảng vài dòng).
        static Control Pane(Control info, Control g) { var d = new DockPanel(); info.Margin = new Thickness(0, 0, 0, 6); DockPanel.SetDock(info, Dock.Top); d.Children.Add(info); d.Children.Add(g); return d; }
        var grid = Ui.SubTabs(("Bảng 2 — Gộp theo Ngày + Tên SP + Size", Pane(_t2Info, _t2Grid)), ("Bảng 1 — Dữ liệu nhập từ Excel", Pane(_t1Info, _t1Grid)));
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar); root.Children.Add(grid);
        return root;
    }

    private void ShowTab2()
    {
        var t = Svc.Tab2;
        _t1.Load(t.Headers.Select((h, i) => new GridColumn("c" + i, h + (h == t.CodeCol ? " (mã)" : h == t.DateCol ? " (ngày)" : ""), Math.Clamp(h.Length * 9 + 30, 80, 260))),
            t.Rows.Select(r => t.Headers.Select(h => TongHopService.Cell(t, r, h))));
        _t1Info.Text = $"{t.Rows.Count:#,0} dòng" + (t.CodeCol.Length > 0 ? $" · cột mã: {t.CodeCol}" : "") + (t.DateCol.Length > 0 ? $" · cột ngày: {t.DateCol}" : "");
        var (numeric, groups, hasDate) = Svc.ComputeGroups();
        var cols = new List<GridColumn>();
        if (hasDate) cols.Add(new GridColumn("d", "Ngày", 100));
        cols.Add(new GridColumn("p", "Tên sản phẩm", 260)); cols.Add(new GridColumn("s", "Size", 200)); cols.Add(new GridColumn("n", "Số dòng", 80, "number"));
        cols.AddRange(numeric.Select((c, i) => new GridColumn("x" + i, "Tổng " + c, 120, "number")));
        var rows = new List<string[]>(); _t2TotalRows = new();
        foreach (var dg in groups.GroupBy(g => g.Date))
        {
            foreach (var g in dg)
            {
                var r = new List<string>(); if (hasDate) r.Add(g.Date);
                r.Add(g.Product); r.Add(g.Size); r.Add(g.Count.ToString());
                r.AddRange(numeric.Select(c => Math.Round(g.Sums[c], 6).ToString(CultureInfo.InvariantCulture)));
                rows.Add(r.ToArray());
            }
            if (hasDate)
            {
                _t2TotalRows.Add(rows.Count);
                var r = new List<string> { dg.Key, "Tổng ngày " + dg.Key, "", dg.Sum(x => x.Count).ToString() };
                r.AddRange(numeric.Select(c => Math.Round(dg.Sum(x => x.Sums[c]), 6).ToString(CultureInfo.InvariantCulture)));
                rows.Add(r.ToArray());
            }
        }
        _t2.Load(cols, rows);
        _t2Info.Text = $"{groups.Count:#,0} nhóm";
        _t1Grid.Refresh(); _t2Grid.Refresh();
    }

    private static string NumDisplay(int r, int c, string raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && raw.Length < 20 ? d.ToString("#,0.##", Ui.Vi) : raw;

    private async Task ImportTab2()
    {
        var path = await Ui.OpenFile(this, "Chọn file Excel", "xlsx", "Excel");
        if (path is null) return;
        TongHopService.Table? t = null;
        if (!await Ui.RunAsync(this, () => Task.Run(() => t = TongHopService.ReadTable(path)))) return;
        if (t!.Rows.Count == 0) { await Ui.Message(this, "File không có dòng dữ liệu nào (dòng đầu là tiêu đề)."); return; }
        if (Svc.Tab2.Rows.Count > 0 && !await Ui.Confirm(this, $"Thay Bảng 1 hiện có ({Svc.Tab2.Rows.Count:#,0} dòng) bằng {t.Rows.Count:#,0} dòng từ file {Path.GetFileName(path)}?", "Nạp file Excel", "Thay")) return;
        Svc.Tab2 = t;
        ShowTab2(); MarkDirty();
        Svc.Log("nạp Excel Bảng 1", $"{Path.GetFileName(path)} ({t.Rows.Count} dòng)");
    }

    // =====================================================================================
    // Ánh xạ tên hàng
    // =====================================================================================

    private Control BuildAnhXaTab()
    {
        var help = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Classes = { "muted" },
            Text = "Quy tắc đơn: Loại (SP = Tên sản phẩm ký tự 1–4, TT = Thị trường + Khách hàng 8–10, MB = Mạ băng 11–13, hoặc loại tự thêm) + Mã đúng độ dài. " +
                   "Tên đầy đủ dùng ở Báo cáo rã đông; Tên viết tắt dùng ở Tồn kho An An / ô tìm chung (trống = không đổi tên). " +
                   "Quy tắc kết hợp (VD SP=VRHL&TT=XTC) thắng quy tắc đơn; nhiều điều kiện hơn thắng, bằng nhau thì dòng trên thắng.",
        };
        _axTest.TextChanged += (_, _) => TestAnhXa();
        var test = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        test.Children.Add(_axTest); test.Children.Add(_axTestOut);
        // 1.57: 3 bảng (đơn lẻ / kết hợp / loại quy tắc) thành 3 tab con.
        var segBox = new DockPanel();
        _axOpts.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(_axOpts, Dock.Bottom); segBox.Children.Add(_axOpts); segBox.Children.Add(_axSegsGrid);
        var grids = Ui.SubTabs(("Quy tắc đơn lẻ", _axRulesGrid), ("Quy tắc kết hợp", _axCombosGrid), ("Loại quy tắc · tách theo nơi dùng", segBox));
        var root = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(help); top.Children.Add(test);
        if (_edit) top.Children.Add(Btn("Khôi phục quy tắc mặc định", async () =>
        {
            if (!await Ui.Confirm(this, "Thay toàn bộ ánh xạ bằng bộ mặc định (BTP, NL, RAW PD, Ecuador, TGC, AĐ, TGS)?", "Ánh xạ mặc định", "Khôi phục")) return;
            Svc.AnhXa = AnhXa.Defaults(); ShowAnhXa(); _axDirty = false; MarkDirty();
        }));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top); root.Children.Add(grids);
        return root;
    }

    private void ShowAnhXa()
    {
        var ax = Svc.AnhXa;
        _loading = true;
        foreach (var m in new[] { _axRules, _axCombos, _axSegs }) m.ReadOnly = !_edit;
        _axRules.Load(new[] { new GridColumn("pv", "Loại", 60), new GridColumn("ma", "Mã", 90), new GridColumn("full", "Tên đầy đủ", 220), new GridColumn("short", "Tên viết tắt", 140) },
            ax.Rows.Select(r => new[] { r.Pv, r.Ma, r.Full, r.Short }));
        _axCombos.Load(new[] { new GridColumn("when", "Điều kiện (SP=…&TT=…)", 190), new GridColumn("full", "Tên đầy đủ", 160), new GridColumn("short", "Viết tắt", 100) },
            ax.Combos.Select(c => new[] { AnhXa.CondKey(c.When, ax.Segs), c.Full, c.Short }));
        _axSegs.Load(new[] { new GridColumn("id", "Mã loại", 70), new GridColumn("label", "Tên loại", 180), new GridColumn("from", "Từ ký tự", 75, "number"), new GridColumn("to", "Đến", 60, "number") },
            ax.Segs.Select(s => new[] { s.Id, s.Label, s.From.ToString(), s.To.ToString() }));
        _loading = false;
        _axOpts.Children.Clear(); _optBoxes.Clear();
        foreach (var (place, label) in new[] { ("ratd", "Báo cáo rã đông"), ("tk", "Tồn kho / tìm kiếm") })
            foreach (var (f, on) in ax.Opts[place])
            {
                var id = f[4..];
                var cb = new CheckBox { Content = $"{label}: tách {id}", IsChecked = on, IsEnabled = _edit, FontSize = 12 };
                cb.IsCheckedChanged += (_, _) => { _axDirty = true; MarkDirty(); };
                _optBoxes[place + "." + f] = cb; _axOpts.Children.Add(cb);
            }
        foreach (var g in new[] { _axRulesGrid, _axCombosGrid, _axSegsGrid }) g.Refresh();
        TestAnhXa();
    }

    /// <summary>Lưới ánh xạ → dữ liệu. Sai thì báo và không lưu.</summary>
    private bool PullAnhXa()
    {
        var segs = Enumerable.Range(0, _axSegs.UsedRows()).Select(r => new AnhXa.Seg(_axSegs.Get(r, 0), _axSegs.Get(r, 1),
            int.TryParse(_axSegs.Get(r, 2), out var a) ? a : 0, int.TryParse(_axSegs.Get(r, 3), out var b) ? b : 0)).ToList();
        var rules = Enumerable.Range(0, _axRules.UsedRows()).Select(r => new AnhXa.Rule(_axRules.Get(r, 0), _axRules.Get(r, 1), _axRules.Get(r, 2), _axRules.Get(r, 3)))
            .Where(r => r.Pv.Trim().Length > 0 || r.Ma.Trim().Length > 0).ToList();
        var combos = Enumerable.Range(0, _axCombos.UsedRows()).Select(r => new AnhXa.Combo(AnhXa.StrToCond(_axCombos.Get(r, 0)), _axCombos.Get(r, 1), _axCombos.Get(r, 2)))
            .Where(c => c.When.Count > 0).ToList();
        var opts = new Dictionary<string, Dictionary<string, bool>> { ["ratd"] = new(), ["tk"] = new() };
        foreach (var (k, cb) in _optBoxes) { var p = k.Split('.'); opts[p[0]][p[1]] = cb.IsChecked == true; }
        var ax = new AnhXa(segs, rules, combos, opts);
        var dropped = rules.Count - ax.Rows.Count;
        Svc.AnhXa = ax;
        if (dropped > 0 || combos.Count != ax.Combos.Count)
            _saveState.Text = $"Bỏ {dropped + combos.Count - ax.Combos.Count} quy tắc sai loại / sai độ dài mã";
        TestAnhXa();
        return true;
    }

    private void TestAnhXa()
    {
        var code = (_axTest.Text ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) { _axTestOut.Text = ""; return; }
        var ax = Svc.AnhXa;
        var f = ax.Resolve(code, true, Svc.SegmentLabel(code, 1, ""));
        var s = ax.Resolve(code, false);
        string Via(AnhXa.Resolution r) => r.Via switch { "combo" => "quy tắc kết hợp", "rules" => string.Join(" → ", r.Steps.Select(x => $"{x.Id}={x.Ma}")), "base" => "tên sản phẩm gốc", _ => "không đổi tên" };
        _axTestOut.Text = $"Rã đông: {(f.Label.Length > 0 ? f.Label : "—")} ({Via(f)})   ·   Tồn kho: {(s.Label.Length > 0 ? s.Label : "—")} ({Via(s)})";
    }
}

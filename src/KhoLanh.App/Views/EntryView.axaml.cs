using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.Entry;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

/// <summary>Module Nhập liệu: các trang dạng lưới Excel, tự lưu, nhập / xuất Excel, mẫu cột.</summary>
public partial class EntryView : UserControl, IPage
{
    public string Title => "Nhập liệu";
    public string Subtitle => "Bảng nhập kiểu Excel: chép / dán thẳng từ Excel, tự lưu, dùng chung qua mạng LAN.";

    private static EntryService Svc => App.Services.Entry;
    private readonly GridModel _model = new();
    private readonly SheetGrid _grid;
    private EntryService.PageInfo? _page;
    private bool _dirty, _loading, _saving;
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(2) };
    private List<Item> _items = new();

    public record Item(EntryService.PageInfo Page)
    {
        public string Name => Page.Name;
        public string Line => $"{Page.Rows:#,0} dòng · {Short(Page.UpdatedAt)}" + (Page.UpdatedBy.Length > 0 ? " · " + Page.UpdatedBy : "");
        public override string ToString() => Name;
        private static string Short(string at) => DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("dd/MM HH:mm") : at;
    }

    public EntryView()
    {
        InitializeComponent();
        _grid = new SheetGrid(_model) { IsVisible = false };
        _grid.Display = FormatCell;
        _grid.Suggest = SuggestFor;
        _grid.SuggestionPicked += OnSuggestionPicked;
        _grid.CellBackground = CellBg;
        _model.Derive = DeriveCells;
        _grid.AddMenuItems(ColorMenu());
        BuildExtraMenu();
        ColorButton.Flyout = new Flyout { Content = ColorPalette(), Placement = PlacementMode.BottomEdgeAlignedLeft };
        BuildMauTab();
        AttachedToVisualTree += (_, _) => LoadSuggestions();
        GridHost.Children.Add(_grid);
        _model.Changed += () =>
        {
            if (_loading) return;
            _dirty = true; SaveState.Text = "Chưa lưu…";
            _autosave.Stop(); _autosave.Start();
            UpdateStatus();
        };
        _grid.SelectionChanged += UpdateStatus;
        _autosave.Tick += async (_, _) => { _autosave.Stop(); await SaveAsync(quiet: true); };
        SearchBox.TextChanged += (_, _) => FillList();
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; await SaveAsync(quiet: false); }
        };
        DetachedFromVisualTree += async (_, _) => { if (_dirty) await SaveAsync(quiet: true); };
        var edit = Svc.CanEdit;
        NewButton.IsEnabled = RenameButton.IsEnabled = DeleteButton.IsEnabled = RestoreButton.IsEnabled = edit;
        Tabs.SelectionChanged += (_, e) => { if (e.Source == Tabs && Tabs.SelectedIndex == 1) Dispatcher.UIThread.Post(() => PageList.ScrollIntoView(PageList.SelectedItem ?? new object())); };
        SaveButton.IsVisible = ImportButton.IsVisible = ColumnsButton.IsVisible = TemplateButton.IsVisible = ColorButton.IsVisible = edit;
        ExportButton.IsVisible = Svc.CanExport;
        _model.ReadOnly = !edit;
        ReloadList(selectId: null);
    }

    // ------------------------------------------------------------------ danh sách

    private void ReloadList(string? selectId)
    {
        try { _items = Svc.Pages(TrashToggle.IsChecked == true).Select(p => new Item(p)).ToList(); }
        catch (BusinessException ex) { _items = new(); CountText.Text = ex.Message; }
        FillList(selectId ?? _page?.Id);
    }

    private void FillList(string? selectId = null)
    {
        var q = (SearchBox.Text ?? "").Trim();
        var shown = q.Length == 0 ? _items : _items.Where(i => i.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        PageList.ItemsSource = shown;
        CountText.Text = $"{shown.Count} trang";
        var trash = TrashToggle.IsChecked == true;
        RestoreButton.IsVisible = trash; RenameButton.IsVisible = !trash;
        DeleteButton.Content = trash ? "Xóa hẳn" : "Xóa";
        var target = shown.FirstOrDefault(i => i.Page.Id == selectId) ?? (_page is null && !trash ? shown.FirstOrDefault() : null);
        if (target is not null) PageList.SelectedItem = target;
        SyncCombo();
        EmptyText.IsVisible = _page is null;
        EmptyText.Text = trash ? "Chọn một trang trong thùng rác để xem hoặc khôi phục." : "Chưa có trang nào. Bấm “+ Trang mới” để bắt đầu nhập (chọn mẫu có sẵn hoặc bảng trống).";
    }

    private bool _syncing;
    /// <summary>Ô chọn trang trên đầu bảng: các trang đang dùng (không gồm thùng rác), chọn đúng trang đang mở.</summary>
    private void SyncCombo()
    {
        _syncing = true;
        try
        {
            var trash = TrashToggle.IsChecked == true;
            var list = trash ? new List<Item>() : _items;
            PageCombo.ItemsSource = list;
            PageCombo.SelectedItem = list.FirstOrDefault(i => i.Page.Id == _page?.Id);
            PageCombo.PlaceholderText = trash ? "(đang xem thùng rác)" : list.Count == 0 ? "(chưa có trang)" : "Chọn trang…";
        }
        finally { _syncing = false; }
    }

    private async void OnComboSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PageCombo.SelectedItem is not Item it || it.Page.Id == _page?.Id) return;
        if (_dirty && !await SaveAsync(quiet: true)) { SyncCombo(); return; }
        PageList.SelectedItem = _items.FirstOrDefault(i => i.Page.Id == it.Page.Id);
        if (_page?.Id != it.Page.Id) Open(it.Page);
    }

    private void OnOpenPage(object? sender, RoutedEventArgs e) { if (_page is not null) Tabs.SelectedIndex = 0; }
    private void OnListDoubleTapped(object? sender, TappedEventArgs e) { if (_page is not null) Tabs.SelectedIndex = 0; }

    private async void OnPageSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (PageList.SelectedItem is not Item it || it.Page.Id == _page?.Id) return;
        if (_dirty && !await SaveAsync(quiet: true)) { PageList.SelectedItem = _items.FirstOrDefault(i => i.Page.Id == _page?.Id); return; }
        Open(it.Page);
    }

    private void Open(EntryService.PageInfo p)
    {
        List<string[]> rows;
        try { rows = Svc.LoadWithColors(p); }
        catch (BusinessException ex) { _ = Ui.Message(this, ex.Message); return; }
        _loading = true;
        _page = p;
        LoadModel(p.Columns, rows);
        _model.ReadOnly = !Svc.CanEdit || p.Deleted;
        _loading = false;
        _dirty = false;
        _grid.IsVisible = true; EmptyText.IsVisible = false;
        _grid.Refresh();
        _grid.Select(0, 0);
        SyncCombo();
        SaveState.Text = p.Deleted ? "Trong thùng rác (chỉ xem)" : $"Đã lưu {p.UpdatedAt}";
        UpdateStatus();
        Dispatcher.UIThread.Post(() => _grid.Focus());
    }

    private void OnTrashToggle(object? sender, RoutedEventArgs e)
    {
        _page = null; _grid.IsVisible = false; SaveState.Text = ""; StatusText.Text = "";
        ReloadList(null);
    }

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        var tpls = Svc.Templates();
        var r = await Ui.Choose(this, "Chọn mẫu cột cho trang mới (đổi cột được sau, ở nút Cột…).", "Trang mới",
            tpls.Select(t => t.BuiltIn ? t.Name + (t.Edited ? " (đã sửa)" : "") : t.Name + " (mẫu tự tạo)").ToList(), 0, "Tên trang", "", "Tạo trang");
        if (r is not { } pick || pick.Index < 0) return;
        if (_dirty && !await SaveAsync(quiet: true)) return;
        EntryService.PageInfo? p = null;
        if (!await Ui.Run(this, () => p = Svc.Create(pick.Text, tpls[pick.Index]))) return;
        if (TrashToggle.IsChecked == true) TrashToggle.IsChecked = false;
        ReloadList(p!.Id);
        Open(p);
        Tabs.SelectedIndex = 0;
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (_page is null) return;
        var name = await Ui.Prompt(this, "Tên mới của trang:", "Đổi tên trang", _page.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!await Ui.Run(this, () => Svc.Rename(_page.Id, name))) return;
        _page = _page with { Name = name.Trim() };
        ReloadList(_page.Id);
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_page is null) return;
        var forever = TrashToggle.IsChecked == true;
        var msg = forever ? $"Xóa hẳn trang “{_page.Name}”? Không khôi phục được (trừ từ file sao lưu)." : $"Đưa trang “{_page.Name}” vào thùng rác? Khôi phục lại được.";
        if (!await Ui.Confirm(this, msg, "Xóa trang", forever ? "Xóa hẳn" : "Xóa")) return;
        if (!forever && _dirty) await SaveAsync(quiet: true);
        if (!await Ui.Run(this, () => Svc.Delete(_page.Id, forever))) return;
        _dirty = false; _page = null; _grid.IsVisible = false; SaveState.Text = ""; StatusText.Text = "";
        ReloadList(null);
    }

    private async void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (_page is null) return;
        var id = _page.Id;
        if (!await Ui.Run(this, () => Svc.Restore(id))) return;
        TrashToggle.IsChecked = false;
        _page = null;
        ReloadList(id);
    }

    // ------------------------------------------------------------------ lưu

    private async Task<bool> SaveAsync(bool quiet)
    {
        if (_page is null || _saving || !_dirty || _model.ReadOnly) { if (!quiet && _page is not null && !_dirty) SaveState.Text = $"Đã lưu {_page.UpdatedAt}"; return true; }
        _saving = true;
        var page = _page;
        var cols = PageCols.Select(c => c.Clone()).ToList();
        var rows = _model.Snapshot();
        var version = _model.Version;
        SaveState.Text = "Đang lưu…";
        try
        {
            var saved = await Task.Run(() => Svc.Save(page, cols, rows));
            if (_page?.Id == saved.Id) _page = saved;
            if (_model.Version == version) _dirty = false;
            SaveState.Text = _dirty ? "Chưa lưu…" : $"Đã lưu {DateTime.Now:HH:mm:ss}";
            var idx = _items.FindIndex(i => i.Page.Id == saved.Id);
            if (idx >= 0) { _items[idx] = new Item(saved); var sel = PageList.SelectedItem; FillList(saved.Id); }
            return true;
        }
        catch (SheetConflictException)
        {
            // Máy khác vừa sửa trang này: không ghi đè. Chép phần mình đang có vào bộ nhớ rồi tải bản mới.
            await _grid.CopyAllAsync();
            await Ui.Message(this, "Trang này vừa được máy khác sửa nên phần bạn vừa nhập CHƯA được lưu (để không ghi đè dữ liệu của họ).\n\n" +
                                   "Toàn bộ bảng của bạn đã được chép vào bộ nhớ. Bấm Đồng ý để tải bản mới nhất, rồi Ctrl+V vào chỗ cần nếu muốn đưa lại phần của mình.", "Trang vừa được sửa ở máy khác");
            _dirty = false;
            Reload();
            return false;
        }
        catch (Exception ex)
        {
            SaveState.Text = "Lưu lỗi";
            if (!quiet || ex is not BusinessException) await Ui.Message(this, Ui.Describe(ex), "Lỗi lưu");
            else await Ui.Message(this, ex.Message, "Chưa lưu được");
            return false;
        }
        finally { _saving = false; }
    }

    private async void OnSave(object? sender, RoutedEventArgs e) { _autosave.Stop(); await SaveAsync(quiet: false); }

    private void Reload()
    {
        var id = _page?.Id;
        _page = null;
        ReloadList(id);
        if (_page is null && _items.FirstOrDefault(i => i.Page.Id == id) is { } it) Open(it.Page);
    }

    private async void OnReload(object? sender, RoutedEventArgs e)
    {
        if (_dirty && !await Ui.Confirm(this, "Bỏ phần chưa lưu và tải lại trang từ dữ liệu đã lưu?", "Tải lại", "Tải lại")) return;
        _dirty = false;
        Reload();
    }

    // ------------------------------------------------------------------ Excel

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        var path = await Ui.OpenFile(this, "Chọn file Excel", "xlsx", "Excel");
        if (path is null) return;
        (List<GridColumn> cols, List<string[]> rows, List<string> sheets) data = default;
        if (!await Ui.RunAsync(this, async () => data = await Task.Run(() => EntryService.ReadExcel(path)))) return;
        var sheet = data.sheets.FirstOrDefault();
        if (data.sheets.Count > 1)
        {
            var pick = await Ui.Choose(this, "File có nhiều trang tính. Chọn trang cần nhập:", "Nhập Excel", data.sheets);
            if (pick is not { } p) return;
            sheet = data.sheets[p.Index];
            if (!await Ui.RunAsync(this, async () => data = await Task.Run(() => EntryService.ReadExcel(path, sheet)))) return;
        }
        var options = new List<string> { "Tạo trang mới từ file (lấy dòng đầu làm tên cột)" };
        if (_page is not null && !_model.ReadOnly) options.Add($"Thêm vào cuối trang “{_page.Name}” (khớp theo tên cột)");
        var how = await Ui.Choose(this, $"Đọc được {data.rows.Count:#,0} dòng, {data.cols.Count} cột.", "Nhập Excel", options, 0, null, "", "Nhập");
        if (how is not { } h) return;
        if (h.Index == 0)
        {
            if (_dirty && !await SaveAsync(quiet: true)) return;
            EntryService.PageInfo? page = null;
            var name = Path.GetFileNameWithoutExtension(path) + (data.sheets.Count > 1 ? " - " + sheet : "");
            if (!await Ui.RunAsync(this, () => Task.Run(() =>
            {
                page = Svc.Create(name, new EntryService.Template("excel", "Excel", data.cols, false));
                page = Svc.Save(page, data.cols, data.rows);
            }))) return;
            TrashToggle.IsChecked = false;
            ReloadList(page!.Id);
            Open(page);
            Tabs.SelectedIndex = 0;
            return;
        }
        // thêm vào cuối, khớp tên cột (không phân biệt hoa thường); cột không khớp bỏ qua
        var map = data.cols.Select(c => PageCols.FindIndex(x => x.Header.Trim().Equals(c.Header.Trim(), StringComparison.CurrentCultureIgnoreCase) && x.Formula.Length == 0)).ToList();
        var matched = map.Count(i => i >= 0);
        if (matched == 0) { await Ui.Message(this, "Không có cột nào trùng tên với trang đang mở. Hãy tạo trang mới từ file."); return; }
        var start = _model.UsedRows();
        var cells = new List<(int, int, string)>();
        for (var r = 0; r < data.rows.Count; r++)
            for (var c = 0; c < map.Count; c++)
                if (map[c] >= 0 && data.rows[r][c].Length > 0) cells.Add((start + r, map[c], data.rows[r][c]));
        _model.SetCells(cells);
        _grid.Select(start, 0);
        await Ui.Message(this, $"Đã thêm {data.rows.Count:#,0} dòng ({matched}/{data.cols.Count} cột khớp tên). Ctrl+Z để hoàn tác.");
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_page is null) return;
        var path = await Ui.SaveFile(this, "Xuất Excel", Ui.SafeFileName(_page.Name) + ".xlsx", "xlsx", "Excel");
        if (path is null) return;
        var cols = PageCols.ToList(); var rows = _model.Snapshot();
        var title = _page.Name;
        if (await Ui.RunAsync(this, () => Task.Run(() => Svc.ExportExcel(path, title, cols, rows)))) Ui.OpenExternal(path);
    }

    // ------------------------------------------------------------------ cột / mẫu

    private async void OnColumns(object? sender, RoutedEventArgs e)
    {
        if (_page is null || _model.ReadOnly) return;
        var cur = PageCols;
        var result = await ColumnsDialog.Edit(this, cur);
        if (result is null) return;
        var removed = cur.Where(c => result.All(n => n.Key != c.Key)).ToList();
        var lost = removed.Count(c => Enumerable.Range(0, _model.RowCount).Any(r => _model.Get(r, cur.IndexOf(c)).Length > 0));
        if (lost > 0 && !await Ui.Confirm(this, $"Bỏ {lost} cột đang có dữ liệu? Dữ liệu các cột đó sẽ mất khi lưu.", "Bỏ cột", "Bỏ cột")) return;
        var rows = _model.Snapshot();
        var idx = result.Select(c => cur.FindIndex(o => o.Key == c.Key)).Append(cur.Count).ToList();   // + cột màu ô
        LoadModel(result, rows.Select(r => idx.Select(i => i >= 0 && i < r.Length ? r[i] : "").ToArray()).ToList());
        var fx = EntryRules.RecomputeAll(PageCols, _model.Snapshot());
        if (fx.Count > 0) _model.SetCells(fx, force: true);
        _dirty = true; SaveState.Text = "Chưa lưu…"; _autosave.Stop(); _autosave.Start();
        _grid.Refresh();
    }

    /// <summary>Nạp cột trang + cột ẩn màu ô (cuối, rộng 0); cột công thức chỉ đọc (tự tính).</summary>
    private void LoadModel(IEnumerable<GridColumn> cols, IEnumerable<string[]> rows)
    {
        var list = cols.Select(c => { var x = c.Clone(); x.ReadOnly = c.Formula.Length > 0; return x; }).ToList();
        list.Add(new GridColumn(EntryService.ColorKey, "", 0) { ReadOnly = true });
        _model.Load(list, rows);
    }

    /// <summary>Cột của trang (không gồm cột ẩn màu ô).</summary>
    private List<GridColumn> PageCols => _model.Columns.Where(c => c.Key != EntryService.ColorKey).ToList();
    private int ColorCol => _model.Columns.FindIndex(c => c.Key == EntryService.ColorKey);

    private async void OnSaveTemplate(object? sender, RoutedEventArgs e)
    {
        if (_page is null) return;
        var name = await Ui.Prompt(this, "Tên mẫu (lưu bộ cột của trang này để tạo trang mới sau). Trùng tên mẫu cũ thì thay mẫu cũ.", "Lưu thành mẫu", _page.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (await Ui.Run(this, () => Svc.SaveTemplate(name, PageCols))) { SaveState.Text = $"Đã lưu mẫu “{name.Trim()}”"; ReloadMau(null); }
    }

    // ------------------------------------------------------------------ gợi ý mã hàng (1.54)

    private static List<MaGoiY.Sug>? _sugs;          // dùng chung giữa các lần mở trang; làm mới sau 5 phút
    private static HashSet<string> _known = new();
    private static Dictionary<string, EntryRules.Info>? _lookup;
    // 1.71: trang KIỂM KÊ chỉ gợi ý / tự điền theo Tồn kho An An (ưu tiên) rồi Danh mục mã hàng — không lấy Tồn kho gửi hay module khác
    private static List<MaGoiY.Sug>? _sugsKk;
    private static HashSet<string> _knownKk = new();
    private static Dictionary<string, EntryRules.Info>? _lookupKk;
    private bool IsKk => _page is not null && (_page.Template == "kiem-ke" || KhoLanh.Core.KiemKe.KiemKeCore.IsKiemKe(_page.Columns.Select(c => c.Header)));
    private List<MaGoiY.Sug>? Sugs => IsKk ? _sugsKk : _sugs;
    private HashSet<string> Known => IsKk ? _knownKk : _known;
    private Dictionary<string, EntryRules.Info>? Look => IsKk ? _lookupKk : _lookup;
    private static DateTime _sugAt;
    private static readonly IBrush UnknownBg = AppTheme.Brush("#FFF2D6");

    /// <summary>Nạp ngầm danh sách gợi ý: mã đang có tồn ở mọi module (theo quyền xem) + Danh mục mã hàng.</summary>
    private void LoadSuggestions()
    {
        if (_sugs is not null && (DateTime.Now - _sugAt).TotalMinutes < 5) return;
        _sugAt = DateTime.Now;
        var s = App.Services;
        Task.Run(() =>
        {
            try
            {
                var data = new KhoLanh.Core.TimNhanh.GsService(s).Build();
                var list = MaGoiY.Build(data.Rows, MaGoiY.ReadCatalog(s.Lan), data.Seg);
                var look = MaGoiY.Lookup(list);
                var kk = MaGoiY.Build(data.Rows.Where(r => r.Src == "M01"), MaGoiY.ReadCatalog(s.Lan), data.Seg);   // Tồn kho An An → Danh mục
                var lookKk = MaGoiY.Lookup(kk);
                Dispatcher.UIThread.Post(() =>
                {
                    _sugs = list; _known = MaGoiY.Known(list); _lookup = look;
                    _sugsKk = kk; _knownKk = MaGoiY.Known(kk); _lookupKk = lookKk;
                    FillPending(); _grid.Refresh();
                });
            }
            catch { /* gợi ý chỉ để tiện, lỗi thì thôi */ }
        });
    }

    private static bool IsCodeCol(GridColumn c) => EntryRules.IsCodeCol(c);
    private int ColBy(params string[] names)
    {
        for (var i = 0; i < _model.ColCount; i++) if (names.Contains(KhoLanh.Core.Grid.GridFilter.Fold(_model.Columns[i].Header).Trim())) return i;
        return -1;
    }

    private IReadOnlyList<SheetGrid.GridSuggestion> SuggestFor(int col, string text)
    {
        if (Sugs is not { } sugs || col >= _model.ColCount || !IsCodeCol(_model.Columns[col])) return Array.Empty<SheetGrid.GridSuggestion>();
        return MaGoiY.Search(sugs, text).Select(x => new SheetGrid.GridSuggestion(x.Ma, x.Ma,
            string.Join(" · ", new[] { x.Ten, x.Size, x.Dt, x.Ton > 0 ? "tồn " + x.Ton.ToString("#,0.###", Ui.Vi) : "", x.Nguon }.Where(t => t.Length > 0)), x)).ToList();
    }

    /// <summary>Chọn gợi ý → điền thêm Tên hàng / Size / Đặc tính của dòng đó nếu đang trống.</summary>
    private void OnSuggestionPicked(int row, int col, SheetGrid.GridSuggestion it)
    {
        if (it.Tag is not MaGoiY.Sug x || _model.ReadOnly) return;
        var cells = new List<(int, int, string)>();
        void Fill(string v, params string[] names) { var c = ColBy(names); if (c >= 0 && v.Length > 0 && _model.Get(row, c).Trim().Length == 0) cells.Add((row, c, v)); }
        Fill(x.Ten, "ten hang", "ten", "ten san pham", "dien giai");
        Fill(x.Size, "size", "co", "kich co");
        Fill(x.Dt, "dac tinh");
        if (cells.Count > 0) _model.SetCells(cells);
    }

    /// <summary>Nền ô: màu người dùng tô → cột công thức (xám rất nhạt) → mã gõ tay không có trong tồn lẫn danh mục (vàng nhạt).</summary>
    private IBrush? CellBg(int r, int c)
    {
        if (c >= _model.ColCount) return null;
        var col = _model.Columns[c];
        var bg = _model.Get(r, ColorCol);
        if (bg.Length > 0 && CellColors.Codes(bg).TryGetValue(col.Key, out var code) && CellColors.Hex(code) is { } hex) return AppTheme.Brush(hex);
        if (col.Formula.Length > 0) return FormulaBg;
        if (Sugs is null || !IsCodeCol(col)) return null;
        var v = _model.Get(r, c).Trim();
        return v.Length > 0 && !Known.Contains(KhoLanh.Core.TimNhanh.Gs.MaKey(v)) ? UnknownBg : null;
    }
    private static readonly IBrush FormulaBg = AppTheme.Brush("#F3F5F8");

    // ------------------------------------------------------------------ tự điền theo mã / ngày / công thức (1.62)

    private readonly HashSet<int> _pending = new();   // dòng đổi mã khi bảng tra chưa nạp xong

    private EntryRules.Info? Lookup(string ma) =>
        Look is { } lk && lk.TryGetValue(KhoLanh.Core.TimNhanh.Gs.MaKey(ma), out var i) ? i : null;

    private IEnumerable<(int r, int c, string v)> DeriveCells(IReadOnlyList<(int r, int c, string before, string after)> changed)
    {
        var cols = PageCols;
        var ch = changed.Where(x => x.c < cols.Count).ToList();
        if (ch.Count == 0) return Array.Empty<(int, int, string)>();
        if (Look is null)
        {
            var code = EntryRules.CodeCol(cols);
            foreach (var x in ch) if (x.c == code) _pending.Add(x.r);
        }
        return EntryRules.Derive(cols, _model.Get, ch, Lookup, DateTime.Today);
    }

    /// <summary>Bảng tra vừa nạp xong → điền các dòng đã đổi mã trong lúc chờ.</summary>
    private void FillPending()
    {
        if (_pending.Count == 0 || _model.ReadOnly) { _pending.Clear(); return; }
        var cols = PageCols; var code = EntryRules.CodeCol(cols);
        var rows = _pending.Where(r => r < _model.RowCount).ToList(); _pending.Clear();
        if (code < 0 || rows.Count == 0) return;
        var cells = EntryRules.Derive(cols, _model.Get, rows.Select(r => (r, code, "", _model.Get(r, code))), Lookup, DateTime.Today);
        if (cells.Count > 0) _model.SetCells(cells, force: true);
    }

    // ------------------------------------------------------------------ tô màu ô (1.62)

    private void PaintSelection(string code)
    {
        if (_page is null || _model.ReadOnly) return;
        var sel = _grid.Selection; var cols = PageCols; var cc = ColorCol;
        var keys = Enumerable.Range(sel.C1, sel.Cols).Where(c => c < cols.Count).Select(c => cols[c].Key).ToList();
        if (keys.Count == 0 || cc < 0) return;
        var cells = new List<(int, int, string)>();
        var last = Math.Min(sel.R2, Math.Max(_model.RowCount, _model.UsedRows()) + 50);
        for (var r = sel.R1; r <= last; r++)
        {
            var nv = CellColors.Set(_model.Get(r, cc), keys, code);
            if (nv != _model.Get(r, cc) && (r < _model.RowCount || nv.Length > 0)) cells.Add((r, cc, nv));
        }
        _model.SetCells(cells, force: true);
        _grid.Focus();
    }

    private Control ColorPalette()
    {
        var wrap = new WrapPanel { MaxWidth = 250 };
        foreach (var (code, name, hex) in CellColors.Palette)
        {
            var b = new Button { Width = 30, Height = 30, Margin = new Thickness(3), Padding = new Thickness(0), Background = AppTheme.Brush(hex), BorderBrush = AppTheme.Brush("#B9C3D0"), BorderThickness = new Thickness(1) };
            ToolTip.SetTip(b, name);
            var c0 = code;
            b.Click += (_, _) => { ColorButton.Flyout?.Hide(); PaintSelection(c0); };
            wrap.Children.Add(b);
        }
        var clear = new Button { Content = "Bỏ màu", Classes = { "ghost" }, Margin = new Thickness(3, 6, 3, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        clear.Click += (_, _) => { ColorButton.Flyout?.Hide(); PaintSelection(""); };
        return new StackPanel { Children = { wrap, clear } };
    }

    private MenuItem ColorMenu()
    {
        var m = new MenuItem { Header = "Tô màu ô" };
        foreach (var (code, name, hex) in CellColors.Palette)
        {
            var c0 = code;
            var it = new MenuItem { Header = name, Icon = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = AppTheme.Brush(hex), BorderBrush = AppTheme.Brush("#B9C3D0"), BorderThickness = new Thickness(1) } };
            it.Click += (_, _) => PaintSelection(c0);
            m.Items.Add(it);
        }
        m.Items.Add(new Separator());
        var clr = new MenuItem { Header = "Bỏ màu" }; clr.Click += (_, _) => PaintSelection(""); m.Items.Add(clr);
        return m;
    }

    // ------------------------------------------------------------------ 1.72: chuột phải — thêm mã lạ vào Danh mục · xem vị trí trên Sơ đồ kho

    private readonly MenuItem _miDm = new() { Header = "Thêm vào danh mục mã hàng" };
    private readonly MenuItem _miMap = new() { Header = "Xem kết quả trên sơ đồ kho" };
    private Separator? _miSep;

    private void BuildExtraMenu()
    {
        _miDm.Click += async (_, _) => await AddToDanhMuc();
        _miMap.Click += async (_, _) => await ShowOnMap();
        _miSep = _grid.AddMenuItems(_miDm, _miMap);
        _grid.MenuOpening += () =>
        {
            var codes = UnknownCodes();
            _miDm.IsVisible = codes.Count > 0 && CanEditDm;
            // chữ mục menu giữ ngắn (Avalonia không nới cột chữ của menu khi đổi chữ lúc mở) — mã cụ thể ở chú thích
            if (_miDm.IsVisible)
            {
                _miDm.Header = codes.Count == 1 ? "Thêm vào danh mục mã hàng" : $"Thêm {codes.Count} mã vào danh mục";
                ToolTip.SetTip(_miDm, "Mã lạ (nền vàng) sẽ thêm vào Danh mục mã hàng, diễn giải tự dựng từ Mã hóa sản phẩm:\n" + string.Join("\n", codes.Take(15)) + (codes.Count > 15 ? "\n…" : ""));
            }
            var code = CurPos();
            _miMap.IsVisible = IsKk && _page is { Deleted: false } && App.Services.SoDo.CanView && ColBy("vi tri") >= 0;
            if (_miMap.IsVisible)
            {
                _miMap.Header = code.Length > 0 ? "Xem vị trí trên sơ đồ kho" : "Xem kết quả trên sơ đồ kho";
                ToolTip.SetTip(_miMap, (code.Length > 0 ? $"Mở Sơ đồ kho, chọn ô {code}" : "Mở Sơ đồ kho") + " — tô từng ô theo kết quả kiểm kê của trang này (khớp / lệch / thiếu / thừa / chưa đếm).");
            }
            if (_miSep is not null) _miSep.IsVisible = _miDm.IsVisible || _miMap.IsVisible;
        };
    }

    private static bool CanEditDm => App.Services.Session.Can(KhoLanh.Core.DanhMuc.DanhMucService.PermKey, KhoLanh.Core.Models.Perm.Edit);

    /// <summary>Mã hàng (21 ký tự) nền vàng — không có trong tồn lẫn danh mục — ở các dòng đang chọn.</summary>
    private List<string> UnknownCodes()
    {
        if (_page is null || Sugs is null) return new();
        var sel = _grid.Selection; var o = new List<string>(); var seen = new HashSet<string>();
        var r2 = Math.Min(sel.R2, _model.RowCount - 1);
        for (var r = Math.Max(0, sel.R1); r <= r2; r++)
            for (var c = 0; c < _model.ColCount; c++)
            {
                if (!IsCodeCol(_model.Columns[c])) continue;
                var k = KhoLanh.Core.TimNhanh.Gs.MaKey(_model.Get(r, c));
                if (k.Length == KhoLanh.Core.DanhMuc.DanhMucMa.Len && !Known.Contains(k) && seen.Add(k)) o.Add(k);
            }
        return o;
    }

    private async Task AddToDanhMuc()
    {
        var codes = UnknownCodes(); if (codes.Count == 0) return;
        var s = App.Services; var n = 0;
        if (!await Ui.RunAsync(this, () => Task.Run(() =>
        {
            var dm = new KhoLanh.Core.DanhMuc.DanhMucService(() => s.Lan, s.Session, s.Audit, () => s.TongHop);
            dm.Load(); n = dm.AddMissing(codes);
        }))) return;
        // ô hết vàng ngay; gợi ý mã nạp lại (có diễn giải từ danh mục) ở lần gõ tới
        foreach (var c in codes) { _known.Add(c); _knownKk.Add(c); }
        _sugAt = DateTime.MinValue; LoadSuggestions();
        _grid.Refresh();
        MainWindow.DmChanged(this);
        SaveState.Text = n > 0 ? $"Đã thêm {n} mã vào Danh mục" : "Mã đã có trong Danh mục";
    }

    /// <summary>Vị trí (mã chuẩn Kho.Khu.Dãy.Tầng.Khoang) của dòng đang chọn; "" nếu không đọc được.</summary>
    private string CurPos()
    {
        var c = ColBy("vi tri"); var r = _grid.Row;
        if (c < 0 || r < 0 || r >= _model.RowCount) return "";
        return KhoLanh.Core.SoDo.SoDoCore.Parse(_model.Get(r, c)) is { Z: false } p ? p.Code : "";
    }

    private async Task ShowOnMap()
    {
        if (_page is null) return;
        if (_dirty && !await SaveAsync(quiet: true)) return;
        var code = CurPos();
        MainWindow.Of(this)?.Show("m8", new SoDoView(_page.Id, code.Length > 0 ? code : null));
    }

    /// <summary>Mở trang <paramref name="pageId"/> và chọn dòng <paramref name="row"/> (cột Vị trí nếu có) — Sơ đồ kho › kết quả kiểm kê gọi.</summary>
    public void OpenAt(string pageId, int row)
    {
        if (TrashToggle.IsChecked == true) TrashToggle.IsChecked = false;
        if (_page?.Id != pageId)
        {
            ReloadList(pageId);
            if (_page?.Id != pageId && _items.FirstOrDefault(i => i.Page.Id == pageId) is { } it) Open(it.Page);
        }
        if (_page?.Id != pageId) return;
        Tabs.SelectedIndex = 0;
        var c = Math.Max(0, ColBy("vi tri"));
        Dispatcher.UIThread.Post(() => { _grid.Select(Math.Max(0, row), c); _grid.Focus(); });
    }

    // ------------------------------------------------------------------ hiển thị

    private string FormatCell(int r, int c, string raw)
    {
        if (c < _model.ColCount && _model.Columns[c].Type == "number" && !CongThuc.IsError(raw) && EntryService.TryNumber(raw, out var d))
            return d.ToString("#,0.###", Ui.Vi);
        return raw;
    }

    private void UpdateStatus()
    {
        if (_page is null) { StatusText.Text = ""; return; }
        var sel = _grid.Selection;
        var used = _model.UsedRows();
        var text = $"{used:#,0} dòng";
        if (!sel.Single)
        {
            double sum = 0; int nums = 0, filled = 0;
            var r2 = Math.Min(sel.R2, _model.RowCount - 1);
            for (var r = sel.R1; r <= r2; r++)
                for (var c = sel.C1; c <= sel.C2; c++)
                {
                    var v = _model.Get(r, c);
                    if (v.Length == 0) continue;
                    filled++;
                    if (EntryService.TryNumber(v, out var d)) { sum += d; nums++; }
                }
            text += $"   ·   Chọn {sel.Rows:#,0} × {sel.Cols} ô   ·   Đếm: {filled:#,0}";
            if (nums > 0) text += $"   ·   Tổng: {sum.ToString("#,0.###", Ui.Vi)}   ·   TB: {(sum / nums).ToString("#,0.###", Ui.Vi)}";
        }
        else text += $"   ·   Ô {GridModel.ColumnName(sel.C1)}{sel.R1 + 1}";
        StatusText.Text = text;
    }
}

/// <summary>
/// Bảng sửa bộ cột (dùng ở hộp "Cột…" của trang và tab Mẫu): mỗi dòng một cột — Tên cột, Kiểu, Rộng, Tự điền, Công thức.
/// Dán được danh sách tên cột từ Excel; ô Kiểu / Tự điền gợi ý lựa chọn khi gõ; ô Công thức gõ "[" để chọn tên cột.
/// </summary>
internal static class ColumnTable
{
    private static readonly string[] Types = { "Chữ", "Số", "Ngày" };
    public static string TypeLabel(string t) => t switch { "number" => "Số", "date" => "Ngày", _ => "Chữ" };
    public static string TypeKey(string l) => KhoLanh.Core.Grid.GridFilter.Fold(l).Trim() switch { "so" or "number" => "number", "ngay" or "date" => "date", _ => "text" };

    public static GridModel Model(IReadOnlyList<GridColumn> cols)
    {
        var m = new GridModel();
        Load(m, cols);
        return m;
    }

    public static void Load(GridModel m, IReadOnlyList<GridColumn> cols) =>
        m.Load(new[]
        {
            new GridColumn("h", "Tên cột", 200), new GridColumn("t", "Kiểu", 80), new GridColumn("w", "Rộng", 64, "number"),
            new GridColumn("a", "Tự điền", 170), new GridColumn("f", "Công thức", 280), new GridColumn("k", "", 0) { ReadOnly = true },
        }, cols.Select(c => new[] { c.Header, TypeLabel(c.Type), Math.Round(c.Width).ToString(CultureInfo.InvariantCulture), c.Auto.Length > 0 ? EntryRules.AutoLabel(c.Auto) : "", c.Formula, c.Key }));

    public static SheetGrid Grid(GridModel m)
    {
        var g = new SheetGrid(m) { SpareRows = 10, ShowTotals = false, Filterable = false, CanEnlarge = false };
        g.Suggest = (col, text) =>
        {
            var q = KhoLanh.Core.Grid.GridFilter.Fold(text ?? "").Trim();
            IEnumerable<string> opts = col switch
            {
                1 => Types,
                3 => EntryRules.AutoKinds.Select(a => a.Label),
                _ => Array.Empty<string>(),
            };
            if (col == 4)
            {
                // gõ "[" → chọn tên cột
                var t = text ?? ""; var open = t.LastIndexOf('[');
                if (open < 0 || t.IndexOf(']', open) >= 0) return Array.Empty<SheetGrid.GridSuggestion>();
                var part = KhoLanh.Core.Grid.GridFilter.Fold(t[(open + 1)..]).Trim();
                return Enumerable.Range(0, m.RowCount).Select(r => m.Get(r, 0).Trim()).Where(h => h.Length > 0 && KhoLanh.Core.Grid.GridFilter.Fold(h).Contains(part))
                    .Distinct().Take(10).Select(h => new SheetGrid.GridSuggestion((t.Length == 0 ? "=" : t[..open]) + "[" + h + "]", h, "tên cột")).ToList();
            }
            return opts.Where(o => q.Length == 0 || KhoLanh.Core.Grid.GridFilter.Fold(o).Contains(q)).Select(o => new SheetGrid.GridSuggestion(o, o, "")).ToList();
        };
        return g;
    }

    /// <summary>Đọc lại bộ cột; giữ mã cột cũ (dữ liệu đi theo), cột mới cấp mã chưa dùng. Lỗi (công thức sai, trùng tên) → thông báo.</summary>
    public static List<GridColumn> Read(GridModel m, IEnumerable<string> usedKeys, out string? error)
    {
        error = null;
        var cols = new List<GridColumn>();
        var used = new HashSet<string>(usedKeys);
        for (var r = 0; r < m.RowCount; r++) used.Add(m.Get(r, 5));
        var next = 0;
        string NewKey() { string k; do k = "c" + next++; while (used.Contains(k)); used.Add(k); return k; }
        for (var r = 0; r < m.RowCount; r++)
        {
            var h = m.Get(r, 0).Trim();
            if (h.Length == 0) continue;
            var key = m.Get(r, 5);
            if (key.Length == 0 || cols.Any(c => c.Key == key)) key = NewKey();
            var width = double.TryParse(m.Get(r, 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var wd) ? Math.Clamp(wd, 30, 800) : 110;
            var fx = m.Get(r, 4).Trim();
            if (fx.Length > 0 && !fx.StartsWith('=')) fx = "=" + fx;
            if (fx.Length > 0 && CongThuc.Check(fx) is { } err) { error ??= $"Cột “{h}”: {err}"; }
            cols.Add(new GridColumn(key, h, width, TypeKey(m.Get(r, 1))) { Auto = fx.Length > 0 ? "" : EntryRules.AutoKey(m.Get(r, 3)), Formula = fx });
        }
        var dup = cols.GroupBy(c => c.Header, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) error ??= $"Có 2 cột cùng tên “{dup.Key}”.";
        if (cols.Count == 0) error ??= "Cần ít nhất một cột.";
        return cols;
    }

    /// <summary>Nút "Công thức…": danh sách hàm hỗ trợ.</summary>
    public static Button HelpButton()
    {
        var sp = new StackPanel { Spacing = 3, Width = 400 };
        sp.Children.Add(new TextBlock { Text = "Công thức tính cho từng dòng", FontWeight = FontWeight.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var t in new[]
        {
            "=[Số lượng]*[Trọng lượng]  — gọi cột bằng tên trong [ ]",
            "=E*D hoặc =E2*D2  — gọi cột bằng chữ cột như Excel",
            "Toán tử: + - * / ^ %  ·  nối chữ &  ·  so sánh = <> < > <= >=",
        }) sp.Children.Add(new SelectableTextBlock { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        sp.Children.Add(new TextBlock { Text = "Hàm", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        foreach (var (_, help) in CongThuc.Functions) sp.Children.Add(new SelectableTextBlock { Text = help, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        var sv = new ScrollViewer { MaxHeight = 480, Content = sp };
        return new Button { Content = "Công thức…", Classes = { "ghost" }, Flyout = new Flyout { Content = sv, Placement = PlacementMode.BottomEdgeAlignedRight } };
    }
}

/// <summary>Hộp "Cột…" của trang đang mở.</summary>
internal static class ColumnsDialog
{
    public static async Task<List<GridColumn>?> Edit(Visual owner, IReadOnlyList<GridColumn> current)
    {
        var model = ColumnTable.Model(current);
        var grid = ColumnTable.Grid(model); grid.Height = 340;
        List<GridColumn>? result = null;
        var ok = new Button { Content = "Áp dụng", Classes = { "primary" }, IsDefault = false };
        var cancel = new Button { Content = "Đóng" };
        var body = new DockPanel { Margin = new Thickness(18) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ColumnTable.HelpButton()); buttons.Children.Add(cancel); buttons.Children.Add(ok);
        DockPanel.SetDock(buttons, Dock.Bottom); body.Children.Add(buttons);
        body.Children.Add(grid);
        var w = new Window
        {
            Title = "Cột của trang", Width = 900, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body, ShowInTaskbar = false,
        };
        ok.Click += async (_, _) =>
        {
            var cols = ColumnTable.Read(model, current.Select(c => c.Key), out var err);
            if (err is not null) { await Ui.Message(w, err); return; }
            result = cols; w.Close();
        };
        cancel.Click += (_, _) => w.Close();
        w.Opened += (_, _) => grid.Focus();
        await Ui.Show(owner, w);
        return result;
    }
}

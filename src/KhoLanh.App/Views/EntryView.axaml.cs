using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
        private static string Short(string at) => DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("dd/MM HH:mm") : at;
    }

    public EntryView()
    {
        InitializeComponent();
        _grid = new SheetGrid(_model) { IsVisible = false };
        _grid.Display = FormatCell;
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
        SaveButton.IsVisible = ImportButton.IsVisible = ColumnsButton.IsVisible = TemplateButton.IsVisible = edit;
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
        EmptyText.IsVisible = _page is null;
        EmptyText.Text = trash ? "Chọn một trang trong thùng rác để xem hoặc khôi phục." : "Chưa có trang nào. Bấm “+ Trang mới” để bắt đầu nhập (chọn mẫu có sẵn hoặc bảng trống).";
    }

    private async void OnPageSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (PageList.SelectedItem is not Item it || it.Page.Id == _page?.Id) return;
        if (_dirty && !await SaveAsync(quiet: true)) { PageList.SelectedItem = _items.FirstOrDefault(i => i.Page.Id == _page?.Id); return; }
        Open(it.Page);
    }

    private void Open(EntryService.PageInfo p)
    {
        List<string[]> rows;
        try { rows = Svc.Load(p); }
        catch (BusinessException ex) { _ = Ui.Message(this, ex.Message); return; }
        _loading = true;
        _page = p;
        _model.Load(p.Columns.Select(c => new GridColumn(c.Key, c.Header, c.Width, c.Type)), rows);
        _model.ReadOnly = !Svc.CanEdit || p.Deleted;
        _loading = false;
        _dirty = false;
        _grid.IsVisible = true; EmptyText.IsVisible = false;
        _grid.Refresh();
        _grid.Select(0, 0);
        PageTitle.Text = p.Name;
        SaveState.Text = p.Deleted ? "Trong thùng rác (chỉ xem)" : $"Đã lưu {p.UpdatedAt}";
        UpdateStatus();
        Dispatcher.UIThread.Post(() => _grid.Focus());
    }

    private void OnTrashToggle(object? sender, RoutedEventArgs e)
    {
        _page = null; _grid.IsVisible = false; PageTitle.Text = ""; SaveState.Text = ""; StatusText.Text = "";
        ReloadList(null);
    }

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        var tpls = Svc.Templates();
        var r = await Ui.Choose(this, "Chọn mẫu cột cho trang mới (đổi cột được sau, ở nút Cột…).", "Trang mới",
            tpls.Select(t => t.BuiltIn ? t.Name : t.Name + " (mẫu đã lưu)").ToList(), 0, "Tên trang", "", "Tạo trang");
        if (r is not { } pick || pick.Index < 0) return;
        if (_dirty && !await SaveAsync(quiet: true)) return;
        EntryService.PageInfo? p = null;
        if (!await Ui.Run(this, () => p = Svc.Create(pick.Text, tpls[pick.Index]))) return;
        if (TrashToggle.IsChecked == true) TrashToggle.IsChecked = false;
        ReloadList(p!.Id);
        Open(p);
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (_page is null) return;
        var name = await Ui.Prompt(this, "Tên mới của trang:", "Đổi tên trang", _page.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!await Ui.Run(this, () => Svc.Rename(_page.Id, name))) return;
        _page = _page with { Name = name.Trim() };
        PageTitle.Text = _page.Name;
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
        _dirty = false; _page = null; _grid.IsVisible = false; PageTitle.Text = ""; SaveState.Text = ""; StatusText.Text = "";
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
        var cols = _model.Columns.Select(c => new GridColumn(c.Key, c.Header, c.Width, c.Type)).ToList();
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
            return;
        }
        // thêm vào cuối, khớp tên cột (không phân biệt hoa thường); cột không khớp bỏ qua
        var map = data.cols.Select(c => _model.Columns.FindIndex(x => x.Header.Trim().Equals(c.Header.Trim(), StringComparison.CurrentCultureIgnoreCase))).ToList();
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
        var cols = _model.Columns.ToList(); var rows = _model.Snapshot();
        var title = _page.Name;
        if (await Ui.RunAsync(this, () => Task.Run(() => Svc.ExportExcel(path, title, cols, rows)))) Ui.OpenExternal(path);
    }

    // ------------------------------------------------------------------ cột / mẫu

    private async void OnColumns(object? sender, RoutedEventArgs e)
    {
        if (_page is null || _model.ReadOnly) return;
        var result = await ColumnsDialog.Edit(this, _model.Columns);
        if (result is null) return;
        var removed = _model.Columns.Where(c => result.All(n => n.Key != c.Key)).ToList();
        var lost = removed.Count(c => Enumerable.Range(0, _model.RowCount).Any(r => _model.Get(r, _model.Columns.IndexOf(c)).Length > 0));
        if (lost > 0 && !await Ui.Confirm(this, $"Bỏ {lost} cột đang có dữ liệu? Dữ liệu các cột đó sẽ mất khi lưu.", "Bỏ cột", "Bỏ cột")) return;
        var old = _model.Columns.ToList();
        var rows = _model.Snapshot();
        var idx = result.Select(c => old.FindIndex(o => o.Key == c.Key)).ToList();
        _model.Load(result, rows.Select(r => idx.Select(i => i >= 0 && i < r.Length ? r[i] : "")));
        _dirty = true; SaveState.Text = "Chưa lưu…"; _autosave.Stop(); _autosave.Start();
        _grid.Refresh();
    }

    private async void OnSaveTemplate(object? sender, RoutedEventArgs e)
    {
        if (_page is null) return;
        var name = await Ui.Prompt(this, "Tên mẫu (lưu bộ cột của trang này để tạo trang mới sau). Trùng tên mẫu cũ thì thay mẫu cũ.", "Lưu thành mẫu", _page.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (await Ui.Run(this, () => Svc.SaveTemplate(name, _model.Columns))) SaveState.Text = $"Đã lưu mẫu “{name.Trim()}”";
    }

    // ------------------------------------------------------------------ hiển thị

    private string FormatCell(int r, int c, string raw)
    {
        if (c < _model.ColCount && _model.Columns[c].Type == "number" && EntryService.TryNumber(raw, out var d))
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

/// <summary>Hộp sửa danh sách cột: dùng chính lưới Excel (tên cột, kiểu, độ rộng) — dán được cả danh sách tên cột từ Excel.</summary>
internal static class ColumnsDialog
{
    private static readonly string[] Types = { "Chữ", "Số", "Ngày" };
    private static string TypeLabel(string t) => t switch { "number" => "Số", "date" => "Ngày", _ => "Chữ" };
    private static string TypeKey(string l) => l.Trim().ToLowerInvariant() switch { "số" or "so" or "number" => "number", "ngày" or "ngay" or "date" => "date", _ => "text" };

    public static async Task<List<GridColumn>?> Edit(Visual owner, IReadOnlyList<GridColumn> current)
    {
        var model = new GridModel();
        model.Load(new[] { new GridColumn("h", "Tên cột", 220), new GridColumn("t", "Kiểu (Chữ / Số / Ngày)", 160), new GridColumn("w", "Rộng", 70, "number"), new GridColumn("k", "Mã", 60) { ReadOnly = true } },
            current.Select(c => new[] { c.Header, TypeLabel(c.Type), Math.Round(c.Width).ToString(CultureInfo.InvariantCulture), c.Key }));
        var grid = new SheetGrid(model) { Height = 320, SpareRows = 10 };
        List<GridColumn>? result = null;
        var ok = new Button { Content = "Áp dụng", Classes = { "primary" }, IsDefault = false };
        var cancel = new Button { Content = "Thôi" };
        var body = new DockPanel { Margin = new Thickness(18) };
        var top = new TextBlock
        {
            Text = "Mỗi dòng một cột, theo thứ tự từ trái sang phải. Sửa tên / kiểu / độ rộng; thêm dòng = thêm cột; chuột phải › Xóa dòng = bỏ cột; dán được danh sách tên cột từ Excel.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        DockPanel.SetDock(buttons, Dock.Bottom); body.Children.Add(buttons);
        body.Children.Add(grid);
        var w = new Window
        {
            Title = "Cột của trang", Width = 620, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body, ShowInTaskbar = false,
        };
        ok.Click += (_, _) =>
        {
            var cols = new List<GridColumn>();
            var used = new HashSet<string>(current.Select(c => c.Key));
            var next = 0;
            string NewKey() { string k; do k = "c" + next++; while (used.Contains(k)); used.Add(k); return k; }
            for (var r = 0; r < model.RowCount; r++)
            {
                var h = model.Get(r, 0).Trim();
                if (h.Length == 0) continue;
                var key = model.Get(r, 3);
                if (key.Length == 0 || cols.Any(c => c.Key == key)) key = NewKey();
                var width = double.TryParse(model.Get(r, 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var wd) ? Math.Clamp(wd, 30, 800) : 110;
                cols.Add(new GridColumn(key, h, width, TypeKey(model.Get(r, 1))));
            }
            if (cols.Count == 0) return;
            result = cols; w.Close();
        };
        cancel.Click += (_, _) => w.Close();
        w.Opened += (_, _) => grid.Focus();
        await Ui.Show(owner, w);
        return result;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.Entry;
using KhoLanh.Core.Grid;

namespace KhoLanh.App.Views;

/// <summary>Tab "Mẫu" (1.62): xem / sửa / thêm mẫu cột — tên cột, kiểu, độ rộng, tự điền theo mã hàng / ngày, công thức Excel.</summary>
public partial class EntryView
{
    private ListBox _mauList = null!;
    private TextBox _mauName = null!;
    private readonly GridModel _mauModel = new();
    private SheetGrid _mauGrid = null!;
    private Button _mauSave = null!, _mauDel = null!, _mauCopy = null!, _mauUse = null!, _mauNew = null!;
    private TextBlock _mauState = null!, _mauKind = null!;
    private EntryService.Template? _mau;
    private HashSet<string> _mauLocked = new();
    private static IBrush LockedBg => AppTheme.Brush("#EEF1F5");
    private bool _mauLoading, _mauDirty;
    private string _mauSig = "";
    /// <summary>Nội dung đang sửa (tên + bảng cột) — khác lúc mở mẫu thì là "chưa lưu".</summary>
    private string MauSig() => (_mauName.Text ?? "").Trim() + "\n" + string.Join("\n", _mauModel.Snapshot().Select(r => string.Join("\t", r.Take(5))).Where(l => l.Trim('\t').Length > 0));

    private void BuildMauTab()
    {
        var edit = Svc.CanEdit;
        _mauNew = new Button { Content = "+ Mẫu mới" };
        _mauCopy = new Button { Content = "Nhân bản" };
        _mauUse = new Button { Content = "Tạo trang từ mẫu" };
        _mauSave = new Button { Content = "Lưu mẫu", Classes = { "primary" } };
        _mauDel = new Button { Content = "Xóa mẫu", Classes = { "danger" } };
        _mauState = new TextBlock { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _mauNew.Click += async (_, _) => { if (await MauLeave()) MauShow(NewTemplate(), isNew: true); };
        _mauCopy.Click += async (_, _) =>
        {
            if (_mau is null || !await MauLeave()) return;
            var cols = ColumnTable.Read(_mauModel, _mau.Columns.Select(c => c.Key), out _);
            MauShow(new EntryService.Template("", (_mauName.Text ?? _mau.Name).Trim() + " (bản sao)", cols, false), isNew: true);
        };
        _mauSave.Click += async (_, _) => await MauSave();
        _mauDel.Click += async (_, _) => await MauDelete();
        _mauUse.Click += async (_, _) => await MauUse();

        var tools = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (edit) { tools.Children.Add(_mauNew); tools.Children.Add(_mauCopy); }
        tools.Children.Add(_mauUse);
        tools.Children.Add(ColumnTable.HelpButton());
        if (edit) { tools.Children.Add(_mauDel); tools.Children.Add(_mauSave); }
        var top = new Border
        {
            Classes = { "card" }, Padding = new Thickness(10, 8), Margin = new Thickness(0, 0, 0, 8),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _mauState, tools } },
        };
        Grid.SetColumn(tools, 1);
        DockPanel.SetDock(top, Dock.Top);

        _mauList = new ListBox();
        _mauList.SelectionChanged += async (_, _) =>
        {
            if (_mauLoading || _mauList.SelectedItem is not ListBoxItem { Tag: EntryService.Template t } || t.Id == _mau?.Id) return;
            if (!await MauLeave()) { MauSelect(_mau?.Id); return; }
            MauShow(t, isNew: false);
        };
        var left = new Border { Classes = { "card" }, Padding = new Thickness(4), Child = _mauList, Width = 270, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(left, Dock.Left);

        _mauName = new TextBox { Width = 320, IsReadOnly = !edit };
        _mauName.TextChanged += (_, _) => MauTouched();
        _mauKind = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" }, FontSize = 12 };
        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 8),
            Children = { new TextBlock { Text = "Tên mẫu", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }, _mauName, _mauKind },
        };
        DockPanel.SetDock(head, Dock.Top);
        _mauGrid = ColumnTable.Grid(_mauModel);
        _mauModel.ReadOnly = !edit;
        // 1.117: cột hệ thống của mẫu có sẵn (Size, Trọng lượng, Đặc tính, Tổng Net…) khóa cứng Kiểu / Tự điền / Công thức, không xóa dòng — chỉ đổi tên / độ rộng
        _mauModel.CellLocked = (r, c) => c is 1 or 3 or 4 && _mauLocked.Contains(_mauModel.Get(r, 5));
        _mauGrid.CellBackground = (r, c) => c is 1 or 3 or 4 && r < _mauModel.RowCount && _mauLocked.Contains(_mauModel.Get(r, 5)) ? LockedBg : null;
        _mauGrid.CellForeground = (r, c) => c is 1 or 3 or 4 && r < _mauModel.RowCount && _mauLocked.Contains(_mauModel.Get(r, 5)) ? AutoFg : null;
        _mauGrid.SoftLockHit += () => { _mauState.Text = "Cột hệ thống (nền xám): chỉ đổi tên / độ rộng — không sửa kiểu, tự điền, công thức, không xóa."; _mauState.Foreground = AppTheme.Brush("#9A5B00"); };
        _mauModel.Changed += MauTouched;
        var right = new Border { Classes = { "card" }, Padding = new Thickness(10), Child = new DockPanel { Children = { head, _mauGrid } } };

        var body = new DockPanel { Margin = new Thickness(0, 8, 0, 0), Children = { top, left, right } };
        Tabs.Items.Add(new TabItem { Header = "Mẫu", Content = body });
        ReloadMau(null);
    }

    private static EntryService.Template NewTemplate() => new("", "Mẫu mới", new List<GridColumn>
    {
        new("c0", "Mã hàng", 190), new("c1", "Tên hàng", 240) { Auto = EntryRules.Ten }, new("c2", "Size", 90) { Auto = EntryRules.Size },
        new("c3", "Số lượng", 90, "number"), new("c4", "Ghi chú", 200), new("c5", "Ngày", 100, "date") { Auto = EntryRules.Today },
    }, false);

    private void MauTouched()
    {
        if (_mauLoading || _mau is null || _mau.Id.Length == 0) return;
        _mauDirty = MauSig() != _mauSig; _mauState.Text = _mauDirty ? "Chưa lưu mẫu…" : ""; _mauState.ClearValue(TextBlock.ForegroundProperty);
    }

    private void ReloadMau(string? selectId)
    {
        List<EntryService.Template> list;
        try { list = Svc.Templates(); } catch { list = new(); }
        _mauLoading = true;
        try
        {
            _mauList.ItemsSource = list.Select(t => new ListBoxItem
            {
                Tag = t,
                Content = new StackPanel
                {
                    Margin = new Thickness(2, 2),
                    Children =
                    {
                        new TextBlock { Text = t.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = KindText(t) + $" · {t.Columns.Count} cột", Classes = { "muted" }, FontSize = 11.5 },
                    },
                },
            }).ToList();
        }
        finally { _mauLoading = false; }
        var pick = list.FirstOrDefault(t => t.Id == (selectId ?? _mau?.Id)) ?? list.FirstOrDefault();
        if (pick is not null && (_mau is null || _mau.Id.Length > 0 || selectId is not null)) MauShow(pick, isNew: false);
        else MauSelect(_mau?.Id);
    }

    private static string KindText(EntryService.Template t) => t.BuiltIn ? (t.Edited ? "Có sẵn · đã sửa" : "Có sẵn") : "Tự tạo";

    private void MauSelect(string? id)
    {
        _mauLoading = true;
        try { _mauList.SelectedItem = _mauList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is EntryService.Template t && t.Id == id); }
        finally { _mauLoading = false; }
    }

    private void MauShow(EntryService.Template t, bool isNew)
    {
        _mauLoading = true;
        try
        {
            _mau = t;
            _mauLocked = EntryService.SystemKeys(t);
            _mauName.Text = t.Name;
            ColumnTable.Load(_mauModel, t.Columns);
            _mauGrid.Refresh();
            _mauKind.Text = isNew ? "Mẫu mới — bấm Lưu mẫu" : KindText(t) + (_mauLocked.Count > 0 ? $" · {_mauLocked.Count} cột hệ thống khóa (nền xám, chỉ đổi tên được)" : "");
            _mauState.ClearValue(TextBlock.ForegroundProperty);
            _mauDirty = isNew;
            _mauSig = MauSig();
            _mauState.Text = isNew ? "Chưa lưu mẫu…" : "";
            var edit = Svc.CanEdit;
            _mauDel.IsVisible = edit && !isNew && (!t.BuiltIn || t.Edited);
            _mauDel.Content = t.BuiltIn ? "Về mặc định" : "Xóa mẫu";
            _mauCopy.IsEnabled = !isNew; _mauUse.IsEnabled = !isNew;
        }
        finally { _mauLoading = false; }
        MauSelect(isNew ? null : t.Id);
    }

    /// <summary>Rời mẫu đang sửa chưa lưu → hỏi.</summary>
    private async Task<bool> MauLeave()
    {
        if (!_mauDirty || _mau is null || !Svc.CanEdit) return true;
        return await Ui.Confirm(this, $"Mẫu “{_mau.Name}” đang sửa chưa lưu. Bỏ phần đã sửa?", "Mẫu chưa lưu", "Bỏ");
    }

    private async Task MauSave()
    {
        if (_mau is null) return;
        var cols = ColumnTable.Read(_mauModel, _mau.Columns.Select(c => c.Key), out var err);
        cols = EntryService.KeepSystemCols(_mau, cols, out var sysErr);   // 1.117
        err ??= sysErr;
        if (err is not null) { await Ui.Message(this, err, "Chưa lưu được mẫu"); return; }
        cols = EntryService.RenameRefs(_mau.Columns, cols);               // 1.117: đổi tên cột → công thức gọi theo tên mới
        var name = (_mauName.Text ?? "").Trim();
        string? id = _mau.Id.Length > 0 ? _mau.Id : null;
        if (id is null && Svc.Templates().FirstOrDefault(t => t.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)) is { } same
            && !await Ui.Confirm(this, $"Đã có mẫu “{same.Name}”. Thay mẫu đó bằng mẫu này?", "Trùng tên mẫu", "Thay")) return;
        EntryService.Template? saved = null;
        if (!await Ui.Run(this, () => saved = Svc.SaveTemplate(name, cols, id))) return;
        _mauDirty = false;
        _mau = saved;
        ReloadMau(saved!.Id);
        _mauState.Text = $"Đã lưu mẫu {DateTime.Now:HH:mm:ss}";
    }

    private async Task MauDelete()
    {
        if (_mau is null || _mau.Id.Length == 0) return;
        var msg = _mau.BuiltIn ? $"Đưa mẫu “{_mau.Name}” về như mặc định (bỏ phần đã sửa)?" : $"Xóa mẫu “{_mau.Name}”? Các trang đã tạo từ mẫu không bị ảnh hưởng.";
        if (!await Ui.Confirm(this, msg, _mau.BuiltIn ? "Về mặc định" : "Xóa mẫu", _mau.BuiltIn ? "Về mặc định" : "Xóa")) return;
        var id = _mau.Id; var builtIn = _mau.BuiltIn;
        if (!await Ui.Run(this, () => Svc.DeleteTemplate(id))) return;
        _mauDirty = false; _mau = null;
        ReloadMau(builtIn ? id : null);
    }

    /// <summary>Tạo trang mới theo mẫu đang chọn rồi mở ở tab Bảng nhập liệu.</summary>
    private async Task MauUse()
    {
        if (_mau is null || _mau.Id.Length == 0) return;
        if (_mauDirty && Svc.CanEdit)
        {
            if (!await Ui.Confirm(this, "Mẫu đang sửa chưa lưu. Lưu mẫu rồi tạo trang?", "Tạo trang", "Lưu và tạo")) return;
            await MauSave();
            if (_mauDirty) return;
        }
        var tpl = _mau;
        var name = await Ui.Prompt(this, "Tên trang mới:", "Tạo trang từ mẫu", tpl.Name + " " + DateTime.Now.ToString("dd/MM/yyyy"));
        if (name is null) return;
        if (_dirty && !await SaveAsync(quiet: true)) return;
        EntryService.PageInfo? p = null;
        if (!await Ui.Run(this, () => p = Svc.Create(name, tpl))) return;
        if (TrashToggle.IsChecked == true) TrashToggle.IsChecked = false;
        ReloadList(p!.Id);
        Open(p);
        Tabs.SelectedIndex = 0;
        Dispatcher.UIThread.Post(() => _grid.Focus());
    }
}

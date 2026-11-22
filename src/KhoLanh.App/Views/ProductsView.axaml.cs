using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public partial class ProductsView : UserControl, IPage
{
    public string Title => "Danh mục mã hàng";
    public string Subtitle => "Mã hàng, đơn vị, kg chuẩn, size và mức tồn tối thiểu.";
    private Product _editing = new();
    private List<Product> _data = new();

    public ProductsView()
    {
        InitializeComponent();
        var canEdit = Roles.CanManage(App.Services.Session.Role);
        SaveButton.IsVisible = DeleteButton.IsVisible = NewButton.IsVisible = ImportButton.IsVisible = canEdit;
        ReadOnlyText.IsVisible = !canEdit;
        SearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Load(); };
        Load();
        Edit(new Product());
    }

    private void Load()
    {
        _data = App.Services.Catalog.ListProducts(SearchBox.Text, InactiveBox.IsChecked == true);
        Grid.ItemsSource = _data;
        SummaryText.Text = $"{_data.Count} mã hàng";
    }

    private void Edit(Product p)
    {
        _editing = p;
        FormTitle.Text = p.Id == 0 ? "Mã hàng mới" : $"Sửa mã {p.Code}";
        CodeBox.Text = p.Code; NameBox.Text = p.Name; CategoryBox.Text = p.Category; UnitBox.Text = p.Unit;
        KgBox.Text = p.KgPerUnit == 0 ? "" : Ui.Num(p.KgPerUnit);
        SizesBox.Text = p.Sizes;
        MinBox.Text = p.MinStock == 0 ? "" : Ui.Num(p.MinStock);
        NoteBox.Text = p.Note; ActiveBox.IsChecked = p.IsActive;
        DeleteButton.IsEnabled = p.Id != 0;
    }

    private void OnSelect(object? sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is Product p)
            Edit(new Product
            {
                Id = p.Id, Code = p.Code, Name = p.Name, Category = p.Category, Unit = p.Unit, KgPerUnit = p.KgPerUnit,
                Sizes = p.Sizes, MinStock = p.MinStock, Note = p.Note, IsActive = p.IsActive,
            });
    }

    private void OnSearch(object? sender, RoutedEventArgs e) => Load();
    private void OnNew(object? sender, RoutedEventArgs e) { Grid.SelectedItem = null; Edit(new Product()); CodeBox.Focus(); }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var ok = await Ui.Run(this, () =>
        {
            _editing.Code = CodeBox.Text ?? ""; _editing.Name = NameBox.Text ?? ""; _editing.Category = (CategoryBox.Text ?? "").Trim();
            _editing.Unit = string.IsNullOrWhiteSpace(UnitBox.Text) ? "thùng" : UnitBox.Text.Trim();
            _editing.KgPerUnit = Ui.ParseNumber(KgBox.Text, "Kg / đơn vị");
            _editing.Sizes = SizesBox.Text ?? "";
            _editing.MinStock = Ui.ParseNumber(MinBox.Text, "Tồn tối thiểu");
            _editing.Note = (NoteBox.Text ?? "").Trim(); _editing.IsActive = ActiveBox.IsChecked == true;
            App.Services.Catalog.SaveProduct(_editing);
        });
        if (!ok) return;
        var id = _editing.Id;
        Load();
        Grid.SelectedItem = _data.FirstOrDefault(x => x.Id == id);
        MainWindow.Of(this)?.SetSubtitle($"Đã lưu mã {_editing.Code}.");
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_editing.Id == 0) return;
        if (!await Ui.Confirm(this, $"Xóa mã hàng {_editing.Code}? Nếu mã đã có phiếu, phần mềm chỉ chuyển sang ngừng dùng.", "Xóa mã hàng", "Xóa")) return;
        string msg = "";
        if (await Ui.Run(this, () => msg = App.Services.Catalog.DeleteProduct(_editing.Id)))
        {
            Load(); Edit(new Product());
            await Ui.Message(this, msg);
        }
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        await Ui.Message(this, "Chọn file Excel (.xlsx). Dòng 1 là tiêu đề; các cột theo thứ tự:\n\nMã hàng | Tên hàng | Nhóm | Đơn vị | Kg/đơn vị | Size | Tồn tối thiểu | Ghi chú\n\nMã đã có sẽ được cập nhật, mã mới sẽ được thêm.", "Nạp mã hàng từ Excel");
        var path = await Ui.OpenFile(this, "Chọn file Excel mã hàng", "xlsx", "Excel");
        if (path is null) return;
        (int added, int updated, List<string> errors) r = (0, 0, new List<string>());
        if (!await Ui.Run(this, () => r = App.Services.Catalog.ImportProducts(path))) return;
        Load();
        var msg = $"Thêm mới {r.added} mã, cập nhật {r.updated} mã.";
        if (r.errors.Count > 0) msg += $"\n\n{r.errors.Count} dòng lỗi:\n" + string.Join("\n", r.errors.Take(15)) + (r.errors.Count > 15 ? "\n..." : "");
        await Ui.Message(this, msg, "Kết quả nạp");
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var path = await Ui.SaveFile(this, "Xuất danh mục mã hàng", "DanhMucMaHang.xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => App.Services.Export.ExportTable(path, "Mã hàng", "DANH MỤC MÃ HÀNG", _data, new ExportColumn<Product>[]
            {
                new("Mã hàng", x => x.Code), new("Tên hàng", x => x.Name), new("Nhóm", x => x.Category), new("Đơn vị", x => x.Unit),
                new("Kg/đơn vị", x => x.KgPerUnit, ExportService.Qty), new("Size", x => x.Sizes),
                new("Tồn tối thiểu", x => x.MinStock, ExportService.Qty), new("Ghi chú", x => x.Note),
                new("Đang dùng", x => x.IsActive ? "Có" : "Không"),
            })))
            Ui.OpenExternal(path);
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public partial class StockView : UserControl, IPage
{
    public string Title => "Tồn kho";
    public string Subtitle => "Số tồn hiện tại, tính từ mọi phiếu nhập / xuất còn hiệu lực.";
    private List<StockItem> _detail = new();
    private List<ProductStock> _total = new();

    public StockView()
    {
        InitializeComponent();
        var owners = new List<Owner> { new() { Id = 0, Code = "*", Name = "Tất cả" } };
        owners.AddRange(App.Services.Catalog.ListOwners(includeInactive: true));
        OwnerBox.ItemsSource = owners;
        OwnerBox.SelectedIndex = 0;
        SearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Load(); };
        LocationBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Load(); };
        Tabs.SelectionChanged += (_, _) => UpdateSummary();
        Load();
    }

    private long? OwnerId => (OwnerBox.SelectedItem as Owner)?.Id is long id && id != 0 ? id : null;

    private void Load()
    {
        var s = App.Services.Stock;
        _detail = s.Stock(OwnerId, SearchBox.Text, LocationBox.Text);
        DetailGrid.ItemsSource = _detail;
        _total = s.ProductTotals(SearchBox.Text);
        TotalGrid.ItemsSource = _total;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        SummaryText.Text = Tabs.SelectedIndex == 0
            ? $"{_detail.Count} dòng tồn · {Ui.Num(_detail.Sum(x => x.Quantity))} đơn vị · {Ui.Num(_detail.Sum(x => x.Weight))} kg"
            : $"{_total.Count} mã hàng · {_total.Count(x => x.IsLow)} mã dưới mức tối thiểu (tổng mọi chủ hàng)";
    }

    private void OnFilter(object? sender, RoutedEventArgs e) => Load();

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var path = await Ui.SaveFile(this, "Xuất tồn kho", $"TonKho_{DateTime.Today:yyyyMMdd}.xlsx", "xlsx", "Excel");
        if (path is null) return;
        var exp = App.Services.Export;
        var ok = await Ui.Run(this, () =>
        {
            if (Tabs.SelectedIndex == 0)
                exp.ExportTable(path, "Tồn chi tiết", $"TỒN KHO NGÀY {DateTime.Today:dd/MM/yyyy}", _detail, new ExportColumn<StockItem>[]
                {
                    new("Chủ hàng", x => x.OwnerName), new("Mã hàng", x => x.ProductCode), new("Tên hàng", x => x.ProductName),
                    new("Size", x => x.Size), new("Lô", x => x.Lot), new("Vị trí", x => x.Location), new("ĐVT", x => x.Unit),
                    new("Tồn", x => x.Quantity, ExportService.Qty), new("Số kg", x => x.Weight, ExportService.Qty),
                    new("Nhập lần đầu", x => x.FirstIn), new("Số ngày lưu", x => x.DaysInStock),
                });
            else
                exp.ExportTable(path, "Tồn theo mã", $"TỒN THEO MÃ HÀNG NGÀY {DateTime.Today:dd/MM/yyyy}", _total, new ExportColumn<ProductStock>[]
                {
                    new("Mã hàng", x => x.ProductCode), new("Tên hàng", x => x.ProductName), new("ĐVT", x => x.Unit),
                    new("Tồn", x => x.Quantity, ExportService.Qty), new("Số kg", x => x.Weight, ExportService.Qty),
                    new("Tồn tối thiểu", x => x.MinStock, ExportService.Qty), new("Dưới mức", x => x.IsLow ? "Có" : ""),
                });
        });
        if (ok) Ui.OpenExternal(path);
    }
}

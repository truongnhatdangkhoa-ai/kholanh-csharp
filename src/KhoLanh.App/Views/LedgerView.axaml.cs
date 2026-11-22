using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public partial class LedgerView : UserControl, IPage
{
    public string Title => "Sổ kho";
    public string Subtitle => "Từng lần nhập / xuất theo thời gian, kèm tồn lũy kế. Chọn một mã hàng để xem thẻ kho của mã đó.";
    private List<LedgerEntry> _rows = new();
    private double _openQ, _openK;

    public LedgerView()
    {
        InitializeComponent();
        var s = App.Services;
        ProductBox.ItemsSource = s.Catalog.ListProducts(includeInactive: true);
        ProductBox.ValueMemberBinding = new Binding(nameof(Product.Display));
        ProductBox.ItemFilter = (search, item) => item is Product p && (string.IsNullOrEmpty(search)
            || p.Code.Contains(search, StringComparison.OrdinalIgnoreCase) || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        var owners = new List<Owner> { new() { Id = 0, Code = "*", Name = "Tất cả" } };
        owners.AddRange(s.Catalog.ListOwners(includeInactive: true));
        OwnerBox.ItemsSource = owners;
        OwnerBox.SelectedIndex = 0;
        FromBox.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToBox.SelectedDate = DateTime.Today;
        Load();
    }

    private void Load()
    {
        var product = ProductBox.SelectedItem as Product;
        if (string.IsNullOrWhiteSpace(ProductBox.Text)) product = null;
        var owner = (OwnerBox.SelectedItem as Owner)?.Id is long id && id != 0 ? id : (long?)null;
        var (oq, ok, rows) = App.Services.Stock.Ledger(FromBox.SelectedDate ?? DateTime.Today, ToBox.SelectedDate ?? DateTime.Today,
            product?.Id, owner, LotBox.Text);
        _openQ = oq; _openK = ok; _rows = rows;
        Grid.ItemsSource = rows;
        OpenText.Text = $"Tồn đầu kỳ ({FromBox.SelectedDate:dd/MM/yyyy}): {Ui.Num(oq)} đơn vị · {Ui.Num(ok)} kg"
                        + (product is null ? "   (đang xem tất cả mã hàng)" : $"   ·  {product.Display}");
        var last = rows.LastOrDefault();
        SummaryText.Text = $"{rows.Count} dòng · Nhập {Ui.Num(rows.Sum(r => r.InQuantity))} · Xuất {Ui.Num(rows.Sum(r => r.OutQuantity))}"
                           + $" · Tồn cuối kỳ {Ui.Num(last?.BalanceQuantity ?? oq)} đơn vị / {Ui.Num(last?.BalanceWeight ?? ok)} kg";
    }

    private void OnFilter(object? sender, RoutedEventArgs e) => Load();

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var path = await Ui.SaveFile(this, "Xuất sổ kho", $"SoKho_{DateTime.Today:yyyyMMdd}.xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => App.Services.Export.ExportTable(path, "Sổ kho", "SỔ KHO", _rows, new ExportColumn<LedgerEntry>[]
            {
                new("Ngày", x => x.Date), new("Số phiếu", x => x.VoucherNo), new("Loại", x => x.TypeLabel),
                new("Mã hàng", x => x.ProductCode), new("Tên hàng", x => x.ProductName), new("Size", x => x.Size),
                new("Lô", x => x.Lot), new("Vị trí", x => x.Location), new("Chủ hàng", x => x.OwnerName), new("Giao / nhận", x => x.Partner),
                new("Nhập", x => x.InQuantity, ExportService.Qty), new("Xuất", x => x.OutQuantity, ExportService.Qty),
                new("Nhập kg", x => x.InWeight, ExportService.Qty), new("Xuất kg", x => x.OutWeight, ExportService.Qty),
                new("Tồn", x => x.BalanceQuantity, ExportService.Qty), new("Tồn kg", x => x.BalanceWeight, ExportService.Qty),
            }, $"{OpenText.Text}")))
            Ui.OpenExternal(path);
    }
}

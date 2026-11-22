using Avalonia.Controls;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public partial class ReportView : UserControl, IPage
{
    public string Title => "Báo cáo nhập xuất tồn";
    public string Subtitle => "Tồn đầu, nhập, xuất, tồn cuối theo mã hàng và size. Chọn cùng một ngày để có báo cáo hằng ngày.";
    private List<NxtRow> _rows = new();

    public ReportView()
    {
        InitializeComponent();
        var owners = new List<Owner> { new() { Id = 0, Code = "*", Name = "Tất cả" } };
        owners.AddRange(App.Services.Catalog.ListOwners(includeInactive: true));
        OwnerBox.ItemsSource = owners;
        OwnerBox.SelectedIndex = 0;
        FromBox.SelectedDate = DateTime.Today;
        ToBox.SelectedDate = DateTime.Today;
        Load();
    }

    private void Load()
    {
        var owner = (OwnerBox.SelectedItem as Owner)?.Id is long id && id != 0 ? id : (long?)null;
        _rows = App.Services.Stock.Nxt(FromBox.SelectedDate ?? DateTime.Today, ToBox.SelectedDate ?? DateTime.Today, owner);
        Grid.ItemsSource = _rows;
        SummaryText.Text = $"{_rows.Count} dòng · Tồn đầu {Ui.Num(_rows.Sum(r => r.OpenQuantity))} · Nhập {Ui.Num(_rows.Sum(r => r.InQuantity))}"
                           + $" · Xuất {Ui.Num(_rows.Sum(r => r.OutQuantity))} · Tồn cuối {Ui.Num(_rows.Sum(r => r.CloseQuantity))} đơn vị"
                           + $" ({Ui.Num(_rows.Sum(r => r.CloseWeight))} kg)";
    }

    private void OnFilter(object? sender, RoutedEventArgs e) => Load();
    private void OnToday(object? sender, RoutedEventArgs e) { FromBox.SelectedDate = ToBox.SelectedDate = DateTime.Today; Load(); }
    internal void OnMonth(object? sender, RoutedEventArgs e)
    {
        FromBox.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToBox.SelectedDate = DateTime.Today;
        Load();
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var from = FromBox.SelectedDate ?? DateTime.Today; var to = ToBox.SelectedDate ?? DateTime.Today;
        var path = await Ui.SaveFile(this, "Xuất báo cáo", $"BaoCaoNXT_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx", "xlsx", "Excel");
        if (path is null) return;
        var company = App.Services.Settings.Get(SettingsService.CompanyName);
        if (await Ui.Run(this, () => App.Services.Export.ExportTable(path, "NXT", "BÁO CÁO NHẬP XUẤT TỒN", _rows, new ExportColumn<NxtRow>[]
            {
                new("Mã hàng", x => x.ProductCode), new("Tên hàng", x => x.ProductName), new("Size", x => x.Size), new("ĐVT", x => x.Unit),
                new("Tồn đầu", x => x.OpenQuantity, ExportService.Qty), new("Nhập", x => x.InQuantity, ExportService.Qty),
                new("Xuất", x => x.OutQuantity, ExportService.Qty), new("Tồn cuối", x => x.CloseQuantity, ExportService.Qty),
                new("Kg đầu", x => x.OpenWeight, ExportService.Qty), new("Kg nhập", x => x.InWeight, ExportService.Qty),
                new("Kg xuất", x => x.OutWeight, ExportService.Qty), new("Kg cuối", x => x.CloseWeight, ExportService.Qty),
            }, $"{company}  ·  Từ {from:dd/MM/yyyy} đến {to:dd/MM/yyyy}  ·  Chủ hàng: {(OwnerBox.SelectedItem as Owner)?.Name}")))
            Ui.OpenExternal(path);
    }
}

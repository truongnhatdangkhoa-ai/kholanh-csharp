using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public partial class VoucherListView : UserControl, IPage
{
    public string Title => "Danh sách phiếu";
    public string Subtitle => "Nhấp đúp một phiếu để xem chi tiết, in lại hoặc hủy.";
    private List<VoucherSummary> _data = new();

    public VoucherListView()
    {
        InitializeComponent();
        FromBox.SelectedDate = DateTime.Today.AddDays(-30);
        ToBox.SelectedDate = DateTime.Today;
        var owners = new List<Owner> { new() { Id = 0, Code = "*", Name = "Tất cả" } };
        owners.AddRange(App.Services.Catalog.ListOwners(includeInactive: true));
        OwnerBox.ItemsSource = owners;
        OwnerBox.SelectedIndex = 0;
        SearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Load(); };
        Load();
    }

    private void Load()
    {
        var type = TypeBox.SelectedIndex switch { 1 => VoucherTypes.In, 2 => VoucherTypes.Out, _ => null };
        var owner = (OwnerBox.SelectedItem as Owner)?.Id is long id && id != 0 ? id : (long?)null;
        _data = App.Services.Vouchers.List(FromBox.SelectedDate ?? DateTime.Today.AddYears(-10), ToBox.SelectedDate ?? DateTime.Today,
            type, owner, SearchBox.Text, CancelledBox.IsChecked == true);
        Grid.ItemsSource = _data;
        var live = _data.Where(x => !x.Cancelled).ToList();
        SummaryText.Text = $"{_data.Count} phiếu · Nhập {Ui.Num(live.Where(x => x.Type == VoucherTypes.In).Sum(x => x.TotalQuantity))} đơn vị"
                           + $" · Xuất {Ui.Num(live.Where(x => x.Type == VoucherTypes.Out).Sum(x => x.TotalQuantity))} đơn vị (không tính phiếu hủy)";
    }

    private void OnFilter(object? sender, RoutedEventArgs e) => Load();

    private async void OnOpen(object? sender, TappedEventArgs e)
    {
        if (Grid.SelectedItem is VoucherSummary v && await VoucherDetailWindow.Open(this, v.Id)) Load();
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var path = await Ui.SaveFile(this, "Xuất danh sách phiếu", $"DanhSachPhieu_{DateTime.Today:yyyyMMdd}.xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => App.Services.Export.ExportTable(path, "Phiếu", "DANH SÁCH PHIẾU NHẬP XUẤT", _data,
                new ExportColumn<VoucherSummary>[]
                {
                    new("Số phiếu", x => x.No), new("Loại", x => x.TypeLabel), new("Ngày", x => x.Date),
                    new("Chủ hàng", x => x.OwnerName), new("Người giao / nhận", x => x.Partner), new("Ghi chú", x => x.Note),
                    new("Số dòng", x => x.LineCount), new("Số lượng", x => x.TotalQuantity, ExportService.Qty),
                    new("Số kg", x => x.TotalWeight, ExportService.Qty), new("Người lập", x => x.CreatedBy), new("Trạng thái", x => x.StatusLabel),
                }, $"Từ {FromBox.SelectedDate:dd/MM/yyyy} đến {ToBox.SelectedDate:dd/MM/yyyy}")))
            Ui.OpenExternal(path);
    }
}

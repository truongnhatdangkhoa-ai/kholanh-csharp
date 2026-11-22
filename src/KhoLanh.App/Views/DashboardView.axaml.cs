using Avalonia.Controls;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Views;

public partial class DashboardView : UserControl, IPage
{
    public string Title => "Tổng quan";
    public string Subtitle => $"Hôm nay {DateTime.Today:dddd, dd/MM/yyyy}";

    public DashboardView()
    {
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        var d = App.Services.Stock.Dashboard();
        TotalQty.Text = Ui.Num(d.TotalQuantity);
        TotalKg.Text = $"{Ui.Num(d.TotalWeight)} kg";
        InToday.Text = $"{d.TodayInCount} phiếu";
        InTodayQty.Text = $"{Ui.Num(d.TodayInQuantity)} đơn vị";
        OutToday.Text = $"{d.TodayOutCount} phiếu";
        OutTodayQty.Text = $"{Ui.Num(d.TodayOutQuantity)} đơn vị";
        Products.Text = $"{d.ProductCount} mã hàng";
        Owners.Text = $"{d.OwnerCount} chủ hàng";
        RecentGrid.ItemsSource = d.Recent;
        LowGrid.ItemsSource = d.LowStock;
        LowGrid.IsVisible = d.LowStock.Count > 0;
        LowEmpty.IsVisible = d.LowStock.Count == 0;
    }

    private void OnNewIn(object? s, RoutedEventArgs e) => MainWindow.Of(this)?.Navigate("in");
    private void OnNewOut(object? s, RoutedEventArgs e) => MainWindow.Of(this)?.Navigate("out");
    private void OnStock(object? s, RoutedEventArgs e) => MainWindow.Of(this)?.Navigate("stock");
    private void OnReport(object? s, RoutedEventArgs e) => MainWindow.Of(this)?.Navigate("nxt");
    private void OnRefresh(object? s, RoutedEventArgs e) => Load();

    private async void OnRecentOpen(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (RecentGrid.SelectedItem is VoucherSummary v) await VoucherDetailWindow.Open(this, v.Id);
    }
}

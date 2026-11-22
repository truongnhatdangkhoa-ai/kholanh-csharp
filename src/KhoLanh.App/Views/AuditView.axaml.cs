using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace KhoLanh.App.Views;

public partial class AuditView : UserControl, IPage
{
    public string Title => "Nhật ký thao tác";
    public string Subtitle => "500 thao tác gần nhất: đăng nhập, tài khoản, phân quyền, sao lưu, cập nhật...";

    public AuditView()
    {
        InitializeComponent();
        SearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Load(); };
        Load();
    }

    private void Load() => Grid.ItemsSource = App.Services.Audit.Recent(500, SearchBox.Text);
    private void OnSearch(object? sender, RoutedEventArgs e) => Load();
}

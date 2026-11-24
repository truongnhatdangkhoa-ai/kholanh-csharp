using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace KhoLanh.App.Views;

public partial class AuditView : UserControl, IPage
{
    public string Title => "Nhật ký thao tác";
    public string Subtitle => "500 thao tác gần nhất trên mọi module.";

    public AuditView()
    {
        InitializeComponent();
        SearchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Load(); };
        Load();
    }

    private void Load()
    {
        var l = App.Services.Audit.Recent(500, SearchBox.Text);
        Grid.ItemsSource = l;
        CountText.Text = $"{l.Count():#,0} thao tác" + (string.IsNullOrWhiteSpace(SearchBox.Text) ? " gần nhất" : " khớp");
    }
    private void OnSearch(object? sender, RoutedEventArgs e) => Load();
}

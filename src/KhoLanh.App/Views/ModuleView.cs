using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Web;

namespace KhoLanh.App.Views;

/// <summary>Màn hình chứa 1 module gốc (Tồn kho An An / Tồn kho gửi) chạy trong WebView2. Giữ sống suốt phiên để không mất trạng thái khi chuyển menu.</summary>
public class ModuleView : Grid, IPage
{
    private readonly WebModuleHost? _host;
    private readonly TextBlock _status;

    public string Title { get; }
    public string Subtitle { get; }
    public bool FullBleed => true;

    public ModuleView(string page, string title, string subtitle)
    {
        Title = title; Subtitle = subtitle;
        _status = new TextBlock
        {
            Text = "Đang mở module…", Foreground = new SolidColorBrush(Color.Parse("#5B6B7B")), FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 640, TextAlignment = TextAlignment.Center,
        };
        Children.Add(_status);
        if (!OperatingSystem.IsWindows())
        {
            _status.Text = "Module này chạy bằng Microsoft Edge WebView2, chỉ có trên Windows.";
            return;
        }
        _host = new WebModuleHost(page);
        _host.Status += msg => Avalonia.Threading.Dispatcher.UIThread.Post(() => { _status.Text = msg; _status.IsVisible = true; _host.IsVisible = false; ModuleLoaded?.Invoke(false, msg); });
        _host.Ready += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => { _status.IsVisible = false; ModuleLoaded?.Invoke(true, ""); });
        Children.Add(_host);
    }

    public WebModuleHost? Host => _host;
    public event Action<bool, string>? ModuleLoaded;

    public void SetActive(bool active)
    {
        IsVisible = active;
        _host?.SetActive(active);
    }
}

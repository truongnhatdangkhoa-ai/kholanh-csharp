using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using KhoLanh.Core.Data;

namespace KhoLanh.App;

/// <summary>
/// Máy mới cài lần đầu: chọn nơi lưu database trước khi đăng nhập.
/// Gợi ý sẵn thư mục mặc định; chọn thư mục đã có khodata.db thì dùng luôn dữ liệu đó.
/// </summary>
public sealed class DataFolderWindow : Window
{
    private readonly DataLocation _loc;
    private readonly TextBox _path = new() { IsReadOnly = true };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#C62828")) };

    /// <summary>Thư mục đã chọn (null = đóng cửa sổ, không chọn).</summary>
    public string? Chosen { get; private set; }
    public event Action<string>? Done;

    public DataFolderWindow(DataLocation loc)
    {
        _loc = loc;
        Title = "Kho Lạnh - Chọn nơi lưu dữ liệu"; Width = 820; Height = 520; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://KhoLanh/Assets/app.ico"))); } catch { }

        var brand = new Border
        {
            Background = Application.Current?.FindResource("BrandDark") as IBrush ?? new SolidColorBrush(Color.Parse("#0B2A45")),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36), Spacing = 14,
                Children =
                {
                    Logo(),
                    new TextBlock { Text = "KHO LẠNH", FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                    new TextBlock { Text = "Cài đặt lần đầu trên máy này.", Foreground = new SolidColorBrush(Color.Parse("#B9D3E8")), FontSize = 14, TextWrapping = TextWrapping.Wrap },
                },
            },
        };

        _path.Text = loc.DefaultFolder;
        var pick = new Button { Content = "Chọn thư mục khác…" };
        pick.Click += async (_, _) =>
        {
            var d = await Ui.OpenFolder(this, "Chọn thư mục lưu dữ liệu Kho Lạnh");
            if (d is not null) { _path.Text = d; Refresh(); }
        };
        var reset = new Button { Content = "Dùng thư mục mặc định" };
        reset.Click += (_, _) => { _path.Text = loc.DefaultFolder; Refresh(); };
        var ok = new Button { Content = "Dùng thư mục này và tiếp tục", Classes = { "primary" }, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        ok.Click += (_, _) =>
        {
            try { Chosen = _loc.Save(_path.Text ?? ""); _error.Text = ""; Done?.Invoke(Chosen); }
            catch (Exception ex) { _error.Text = ex.Message; }
        };

        var form = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Chọn nơi lưu dữ liệu", Classes = { "title" } },
                new TextBlock
                {
                    Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
                    Text = "Database (khodata.db) và các bản sao lưu tự động (thư mục SaoLuu) sẽ nằm trong thư mục này. " +
                           "Nên chọn ổ dữ liệu, ví dụ D:\\KhoLanhData, để không mất khi cài lại Windows. Đổi lại được sau ở Hệ thống › Sao lưu và cài đặt.",
                },
                new TextBlock { Text = "Thư mục dữ liệu", Classes = { "label" } },
                _path,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { pick, reset } },
                _note, _error, ok,
            },
        };
        Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("300,*"),
            Children = { brand, new Border { [Grid.ColumnProperty] = 1, Background = Brushes.White, Padding = new Thickness(44, 0), Child = form } },
        };
        Refresh();
    }

    private static Control Logo()
    {
        try { return new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://KhoLanh/Assets/logo.png"))), Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left }; }
        catch { return new Panel(); }
    }

    private void Refresh()
    {
        var p = _path.Text ?? "";
        _error.Text = "";
        _note.Text = p.Length > 0 && DataLocation.HasDb(p)
            ? "Thư mục này đã có dữ liệu Kho Lạnh — app sẽ dùng luôn dữ liệu đó (tài khoản, tồn kho, phiếu…)."
            : "Thư mục chưa có dữ liệu — app sẽ tạo database mới, sau đó tạo tài khoản Quản trị (hoặc dùng chung tài khoản với máy khác).";
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using KhoLanh.Core.Services;

namespace KhoLanh.App;

public partial class LoginWindow : Window
{
    private bool _firstRun;
    public event Action? LoggedIn;
    public bool Succeeded { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        var s = App.Services;
        _firstRun = !s.Auth.HasAnyUser();
        VersionText.Text = $"Phiên bản {typeof(LoginWindow).Assembly.GetName().Version?.ToString(3)}";
        DataPathText.Text = "Dữ liệu: " + s.Db.FilePath;
        if (_firstRun)
        {
            Heading.Text = "Thiết lập lần đầu";
            SubHeading.Text = "Chưa có tài khoản nào. Tạo tài khoản Quản trị để bắt đầu dùng phần mềm.";
            FullNamePanel.IsVisible = true;
            ConfirmPanel.IsVisible = true;
            LoginButton.Content = "Tạo tài khoản và vào phần mềm";
            UserBox.Text = "admin";
            JoinButton.IsVisible = true;
        }
        else if (s.Accounts.Enabled)
            SubHeading.Text = "Tài khoản dùng chung cho mọi máy (Google Sheet). Nhập tài khoản được cấp để vào phần mềm.";
        Opened += (_, _) => (_firstRun ? FullNameBox : UserBox).Focus();
    }

    private async void OnLogin(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        var s = App.Services;
        if (!_firstRun && s.Accounts.Enabled)
        {
            // Lấy danh sách tài khoản + quyền mới nhất; mất mạng thì dùng bản lưu trong máy.
            LoginButton.IsEnabled = false;
            LoginButton.Content = "Đang lấy danh sách tài khoản…";
            try { await s.Accounts.PullAsync(); }
            catch (BusinessException ex) { DataPathText.Text = "Không lấy được danh sách mới (" + ex.Message.TrimEnd('.') + ") — dùng bản lưu trong máy."; }
            catch (Exception ex) { DataPathText.Text = "Không lấy được danh sách mới (" + ex.Message + ") — dùng bản lưu trong máy."; }
            finally { LoginButton.IsEnabled = true; LoginButton.Content = "Đăng nhập"; }
        }
        try
        {
            var user = UserBox.Text ?? "";
            var pass = PassBox.Text ?? "";
            if (_firstRun)
            {
                if (pass != (Pass2Box.Text ?? "")) throw new BusinessException("Hai lần nhập mật khẩu không khớp.");
                s.Auth.CreateFirstAdmin(user, FullNameBox.Text ?? "", pass);
            }
            s.Auth.Login(user, pass);
            Succeeded = true;
            LoggedIn?.Invoke();
        }
        catch (BusinessException ex)
        {
            ErrorText.Text = ex.Message;
            PassBox.Text = "";
            PassBox.Focus();
        }
    }

    /// <summary>Máy mới: dán mã kết nối từ máy quản trị → nhận danh sách tài khoản từ Google Sheet, khỏi tạo Quản trị riêng.</summary>
    private async void OnJoin(object? sender, RoutedEventArgs e)
    {
        var code = await Ui.Prompt(this, "Dán mã kết nối (bắt đầu bằng KLTK1-) chép từ máy quản trị › Sao lưu và cài đặt › Tài khoản dùng chung:", "Dùng chung tài khoản");
        if (string.IsNullOrWhiteSpace(code)) return;
        ErrorText.Text = "";
        JoinButton.IsEnabled = false;
        try
        {
            var r = await App.Services.Accounts.ConnectAsync(code);
            _firstRun = false;
            Heading.Text = "Đăng nhập";
            SubHeading.Text = $"Đã nhận {r.Users} tài khoản từ Google Sheet. Nhập tài khoản được cấp để vào phần mềm.";
            FullNamePanel.IsVisible = ConfirmPanel.IsVisible = JoinButton.IsVisible = false;
            LoginButton.Content = "Đăng nhập";
            UserBox.Text = ""; PassBox.Text = "";
            UserBox.Focus();
        }
        catch (BusinessException ex) { ErrorText.Text = ex.Message; }
        finally { JoinButton.IsEnabled = true; }
    }
}

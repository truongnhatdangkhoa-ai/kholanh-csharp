using Avalonia.Controls;
using Avalonia.Interactivity;
using KhoLanh.Core.Services;

namespace KhoLanh.App;

public partial class LoginWindow : Window
{
    private readonly bool _firstRun;
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
        }
        Opened += (_, _) => (_firstRun ? FullNameBox : UserBox).Focus();
    }

    private void OnLogin(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        var s = App.Services;
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
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KhoLanh.Core.Services;

namespace KhoLanh.App;

public partial class LoginWindow : Window
{
    private bool _firstRun;
    public event Action? LoggedIn;
    public bool Succeeded { get; private set; }
    /// <summary>Mở lại sau khi bấm Đăng xuất: không tự đăng nhập, không hỏi cập nhật.</summary>
    public bool AfterLogout { get; init; }
    private TaskCompletionSource? _skip;

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
        else
        {
            if (s.Accounts.Enabled)
                SubHeading.Text = "Tài khoản dùng chung cho mọi máy (Google Sheet). Nhập tài khoản được cấp để vào phần mềm.";
            var m = s.Remember;
            UserBox.Text = m.Username;
            if (m.Remember) { PassBox.Text = m.Password; RememberBox.IsChecked = true; AutoBox.IsChecked = m.Auto; }
            Opened += async (_, _) => await StartupAsync();
        }
        Opened += (_, _) => (_firstRun ? FullNameBox : (UserBox.Text ?? "").Length > 0 ? PassBox : UserBox).Focus();
        // Bộ cài cho máy mới (KhoLanh.exe + ket-noi.txt): tự nhận tài khoản từ Google Sheet, chỉ cần đăng nhập.
        if (_firstRun && AccountSyncService.BundledCode() is { } code)
        {
            Heading.Text = "Đăng nhập";
            SubHeading.Text = "Đang nhận danh sách tài khoản từ Google Sheet…";
            FullNamePanel.IsVisible = ConfirmPanel.IsVisible = JoinButton.IsVisible = false;
            LoginButton.IsEnabled = false;
            LoginButton.Content = "Đăng nhập";
            UserBox.Text = "";
            Opened += async (_, _) => await AutoJoin(code);
        }
    }

    // ---------------- mở app: kiểm tra bản mới, rồi tự đăng nhập ----------------

    private async Task StartupAsync()
    {
        if (AfterLogout) return;
        if (await UpdateBeforeLogin()) return;
        var m = App.Services.Remember;
        if (m.Auto && (UserBox.Text ?? "").Length > 0 && (PassBox.Text ?? "").Length > 0)
            await DoLogin(auto: true);
    }

    /// <summary>
    /// Hỏi GitHub có bản mới không (chờ tối đa 10 giây; mất mạng / chưa có mã thì vào luôn). Có bản mới → tải (hiện %),
    /// xong thì mở bản mới. Người dùng bấm "Bỏ qua" thì vào làm việc, bản mới tiếp tục tải ngầm. True = đang mở bản mới.
    /// </summary>
    private async Task<bool> UpdateBeforeLogin()
    {
        if (AppUpdater.ExePath is null || !OperatingSystem.IsWindows() || !App.Services.Updates.Auto) return false;
        UpdatePanel.IsVisible = true;
        UpdText.Text = "Đang kiểm tra bản cập nhật…";
        LoginButton.IsEnabled = false;
        _skip = new TaskCompletionSource();
        var downloading = false;
        void OnProg(string msg) => Dispatcher.UIThread.Post(() =>
        {
            UpdText.Text = msg;
            var i = msg.LastIndexOf(": ", StringComparison.Ordinal);
            if (msg.StartsWith("Đang tải") && i > 0 && int.TryParse(msg[(i + 2)..].TrimEnd('%'), out var pct))
            {
                downloading = true; SkipButton.IsVisible = true;
                UpdBar.IsIndeterminate = false; UpdBar.Value = pct;
            }
        });
        AppUpdater.Progress += OnProg;
        var restarting = false;
        try
        {
            var task = AppUpdater.CheckAndStageAsync(quiet: true);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && !_skip.Task.IsCompleted && (downloading || sw.Elapsed < TimeSpan.FromSeconds(10)))
                await Task.WhenAny(task, _skip.Task, Task.Delay(200));
            if (!task.IsCompleted)
            {
                _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default); // tải tiếp ngầm; xong thì nút Cập nhật hiện trên menu
                return false;
            }
            Version? v;
            try { v = await task; } catch { v = null; }
            if (v is null) return false;
            UpdText.Text = $"Đã tải xong bản {v} — đang mở bản mới…";
            await Task.Delay(300);
            try { restarting = AppUpdater.ApplyAndRestart(); } catch { restarting = false; }
            if (restarting)
            {
                Succeeded = true; // không coi là đóng cửa sổ đăng nhập bình thường
                AppUpdater.ExitForUpdate();
            }
            return restarting;
        }
        finally
        {
            AppUpdater.Progress -= OnProg;
            if (!restarting) { UpdatePanel.IsVisible = false; LoginButton.IsEnabled = true; }
        }
    }

    private void OnSkipUpdate(object? sender, RoutedEventArgs e) => _skip?.TrySetResult();

    private void OnRememberChanged(object? sender, RoutedEventArgs e) { if (RememberBox.IsChecked != true) AutoBox.IsChecked = false; }
    private void OnAutoChanged(object? sender, RoutedEventArgs e) { if (AutoBox.IsChecked == true) RememberBox.IsChecked = true; }

    private async void OnLogin(object? sender, RoutedEventArgs e) => await DoLogin(auto: false);

    private async Task DoLogin(bool auto)
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
            s.Remember.Save(user, pass, RememberBox.IsChecked == true, AutoBox.IsChecked == true);
            Succeeded = true;
            LoggedIn?.Invoke();
        }
        catch (BusinessException ex)
        {
            ErrorText.Text = auto ? "Không tự đăng nhập được: " + ex.Message + " Đã bỏ mật khẩu đã nhớ — nhập lại." : ex.Message;
            if (auto) { s.Remember.Forget(); RememberBox.IsChecked = AutoBox.IsChecked = false; }
            PassBox.Text = "";
            PassBox.Focus();
        }
    }

    private async Task AutoJoin(string code)
    {
        try
        {
            var r = await App.Services.Accounts.ConnectAsync(code);
            _firstRun = false;
            Heading.Text = "Đăng nhập";
            FullNamePanel.IsVisible = ConfirmPanel.IsVisible = false;
            LoginButton.Content = "Đăng nhập";
            SubHeading.Text = $"Máy này dùng chung {r.Users} tài khoản với các máy khác. Nhập tài khoản được cấp để vào phần mềm.";
            UserBox.Focus();
        }
        catch (BusinessException ex)
        {
            // Không nhận được (mất mạng…): cho thử lại, hoặc thiết lập tay như máy đầu tiên.
            Heading.Text = "Thiết lập lần đầu";
            SubHeading.Text = "Chưa nhận được tài khoản từ Google Sheet: " + ex.Message + " Kiểm tra Internet rồi bấm nút dưới để thử lại.";
            JoinButton.IsVisible = true;
            JoinButton.Content = "Thử nhận tài khoản lại";
            _retryCode = code;
        }
        finally { LoginButton.IsEnabled = true; if (_firstRun) { FullNamePanel.IsVisible = ConfirmPanel.IsVisible = true; LoginButton.Content = "Tạo tài khoản và vào phần mềm"; } }
    }

    private string? _retryCode;

    /// <summary>Máy mới: dán mã kết nối từ máy quản trị → nhận danh sách tài khoản từ Google Sheet, khỏi tạo Quản trị riêng.</summary>
    private async void OnJoin(object? sender, RoutedEventArgs e)
    {
        if (_retryCode is { } rc) { JoinButton.IsEnabled = false; await AutoJoin(rc); JoinButton.IsEnabled = true; if (!_firstRun) { _retryCode = null; Heading.Text = "Đăng nhập"; JoinButton.IsVisible = false; } return; }
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

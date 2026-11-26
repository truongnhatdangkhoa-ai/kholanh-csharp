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
    /// <summary>Kết quả đăng nhập (kèm lượt tải danh sách còn chạy ngầm, nếu có) — App kiểm lại tài khoản khi lượt đó xong.</summary>
    public LoginFlow.Result? Result { get; private set; }
    private TaskCompletionSource? _skip;

    public LoginWindow()
    {
        InitializeComponent();
        var s = App.Services;
        _firstRun = !s.Auth.HasAnyUser();
        VersionText.Text = $"Phiên bản {typeof(LoginWindow).Assembly.GetName().Version?.ToString(3)}";
        // Ghi chú chỉ hiện khi có nội dung cần báo (lần đầu, lỗi, cảnh báo đồng bộ tài khoản) — bản gọn 1.46
        foreach (var tb in new[] { SubHeading, ErrorText, DataPathText })
            tb.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) tb.IsVisible = !string.IsNullOrWhiteSpace(tb.Text); };
        ToolTip.SetTip(VersionText, "Dữ liệu: " + s.Db.FilePath);
        if (_firstRun) ShowNewMachine();
        else
        {
            // Tải danh sách tài khoản NGẦM ngay khi mở cửa sổ (song song kiểm tra cập nhật / gõ mật khẩu) — 1.59.
            if (s.Accounts.Enabled) _ = s.Accounts.PrefetchAsync();
            var m = s.Remember;
            UserBox.Text = m.Username;
            if (m.Remember) { PassBox.Text = m.Password; RememberBox.IsChecked = true; AutoBox.IsChecked = m.Auto; }
            Opened += async (_, _) => await StartupAsync();
            GoogleButton.IsVisible = s.Google.Ready;   // máy đã cài mã OAuth (Hệ thống › Tài khoản › Đăng nhập Google)
        }
        Opened += (_, _) => (_firstRun && _setupAdmin ? FullNameBox : (UserBox.Text ?? "").Length > 0 ? PassBox : UserBox).Focus();
        // Bộ cài cho máy mới (KhoLanh.exe + ket-noi.txt): tự nhận tài khoản từ Google Sheet, chỉ cần đăng nhập.
        if (_firstRun && AccountSyncService.BundledCode() is { } code)
        {
            Heading.Text = "Đăng nhập";
            SubHeading.Text = "Đang nhận danh sách tài khoản từ Google Sheet…";
            FullNamePanel.IsVisible = ConfirmPanel.IsVisible = JoinButton.IsVisible = SetupButton.IsVisible = false;
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
        // Vừa cập nhật xong (bản mới tự mở lại): khỏi hỏi GitHub lần nữa trước khi đăng nhập — hỏi ngầm sau 20 giây như thường.
        if (Array.IndexOf(Program.Args, "--da-cap-nhat") >= 0) return false;
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

    /// <summary>1.70: đăng nhập bằng tài khoản Google đang đăng nhập trên trình duyệt → email đã xác minh → tài khoản được gán email đó.</summary>
    private async void OnGoogle(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        var s = App.Services;
        GoogleButton.IsEnabled = LoginButton.IsEnabled = false;
        GoogleButton.Content = "Đang chờ chọn tài khoản Google trên trình duyệt…";
        try
        {
            var r = await s.Google.SignInAsync(Ui.OpenExternal);
            Activate();
            if (s.Accounts.Enabled)
            {
                try { await s.Accounts.PullAsync(); }
                catch (Exception ex) { DataPathText.Text = "Không lấy được danh sách mới (" + ex.Message.TrimEnd('.') + ") — dùng bản lưu trong máy."; }
            }
            var u = s.Auth.LoginGoogle(r.Email);
            try { App.LanNote = KhoLanh.Core.Lan.LanPlanSync.ApplyAtLogin(s) ?? ""; } catch (Exception ex) { App.LanNote = "Không tự đổi vai LAN được: " + ex.Message; }
            UserBox.Text = u.Username;
            Succeeded = true;
            LoggedIn?.Invoke();
        }
        catch (BusinessException ex) { Activate(); ErrorText.Text = ex.Message; }
        catch (Exception ex) { Activate(); ErrorText.Text = "Đăng nhập Google lỗi: " + ex.Message; }
        finally { GoogleButton.IsEnabled = LoginButton.IsEnabled = true; GoogleButton.Content = "Đăng nhập bằng Google"; }
    }

    private async Task DoLogin(bool auto)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();   // 1.100: thời gian đăng nhập (bấm → cửa sổ chính hiện)
        ErrorText.Text = "";
        var s = App.Services;
        try
        {
            var user = UserBox.Text ?? "";
            var pass = PassBox.Text ?? "";
            if (_firstRun && !_setupAdmin)
            {
                // 1.106: máy mới — tên + mật khẩu được cấp → Google Sheet chung kiểm mật khẩu, trả mã để máy tự kết nối (không dán mã).
                LoginButton.IsEnabled = false;
                LoginButton.Content = "Đang kết nối Google Sheet chung…";
                try { await s.Accounts.JoinByPasswordAsync(user, pass); }
                finally { LoginButton.IsEnabled = true; LoginButton.Content = "Đăng nhập"; }
                _firstRun = false;
                JoinButton.IsVisible = SetupButton.IsVisible = false;
                SubHeading.Text = "";
                Result = await LoginFlow.LoginAsync(s.Auth, s.Accounts, user, pass);
            }
            else if (_firstRun)
            {
                if (pass != (Pass2Box.Text ?? "")) throw new BusinessException("Hai lần nhập mật khẩu không khớp.");
                s.Auth.CreateFirstAdmin(user, FullNameBox.Text ?? "", pass);
                s.Auth.Login(user, pass);
            }
            else
            {
                // Danh sách tài khoản đã tải ngầm từ lúc mở cửa sổ; chưa xong thì chỉ chờ thêm ~1,5 giây rồi dùng bản lưu trong máy.
                LoginButton.IsEnabled = false;
                LoginButton.Content = "Đang đăng nhập…";
                try { Result = await LoginFlow.LoginAsync(s.Auth, s.Accounts, user, pass); }
                finally { LoginButton.IsEnabled = true; LoginButton.Content = "Đăng nhập"; }
                if (Result.Note.Length > 0) DataPathText.Text = Result.Note;
            }
            try { App.LanNote = KhoLanh.Core.Lan.LanPlanSync.ApplyAtLogin(s) ?? ""; } catch (Exception ex) { App.LanNote = "Không tự đổi vai LAN được: " + ex.Message; }
            s.Remember.Save(user, pass, RememberBox.IsChecked == true, AutoBox.IsChecked == true);
            Succeeded = true;
            LoggedIn?.Invoke();
            KhoLanh.Core.TroLy.PerfMonitor.Mark(KhoLanh.Core.TroLy.PerfMonitor.MarkDangNhap, sw.ElapsedMilliseconds);
        }
        catch (BusinessException ex)
        {
            ErrorText.Text = auto ? "Không tự đăng nhập được: " + ex.Message + " Đã bỏ mật khẩu đã nhớ — nhập lại." : ex.Message;
            if (auto) { s.Remember.Forget(); RememberBox.IsChecked = AutoBox.IsChecked = false; }
            PassBox.Text = "";
            PassBox.Focus();
        }
    }

    /// <summary>true = đang ở màn "Máy đầu tiên: tạo Quản trị"; false = máy mới đăng nhập bằng tài khoản được cấp (1.106).</summary>
    private bool _setupAdmin;

    /// <summary>1.106: máy mới mở app = màn Đăng nhập thường; tên + mật khẩu được cấp là đủ (app tự kết nối Google Sheet chung).</summary>
    private void ShowNewMachine()
    {
        _setupAdmin = false;
        Heading.Text = "Đăng nhập";
        SubHeading.Text = "Máy mới: nhập tên đăng nhập và mật khẩu đã được cấp. App tự kết nối Google Sheet chung, không cần mã.";
        FullNamePanel.IsVisible = ConfirmPanel.IsVisible = false;
        LoginButton.Content = "Đăng nhập";
        UserBox.Text = "";
        JoinButton.Content = "Có mã kết nối KLKN1-? Dán mã…";
        JoinButton.IsVisible = SetupButton.IsVisible = true;
        SetupButton.Content = "Máy đầu tiên của công ty: tạo tài khoản Quản trị";
    }

    private void OnSetup(object? sender, RoutedEventArgs e)
    {
        if (_setupAdmin) { ShowNewMachine(); ErrorText.Text = ""; UserBox.Focus(); return; }
        _setupAdmin = true;
        ErrorText.Text = "";
        Heading.Text = "Thiết lập lần đầu";
        SubHeading.Text = "Chỉ dùng cho MÁY ĐẦU TIÊN của công ty: tạo tài khoản Quản trị để bắt đầu dùng phần mềm.";
        FullNamePanel.IsVisible = ConfirmPanel.IsVisible = true;
        LoginButton.Content = "Tạo tài khoản và vào phần mềm";
        UserBox.Text = "admin";
        JoinButton.IsVisible = false;
        SetupButton.Content = "← Quay lại: máy mới đăng nhập bằng tài khoản được cấp";
        FullNameBox.Focus();
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
            Heading.Text = "Đăng nhập";
            SubHeading.Text = "Chưa nhận được tài khoản từ Google Sheet: " + ex.Message + " Kiểm tra Internet rồi bấm nút dưới để thử lại, hoặc đăng nhập bằng tài khoản được cấp.";
            JoinButton.IsVisible = true;
            JoinButton.Content = "Thử nhận tài khoản lại";
            _retryCode = code;
        }
        finally { LoginButton.IsEnabled = true; if (_firstRun) SetupButton.IsVisible = true; }
    }

    private string? _retryCode;

    /// <summary>Máy mới: dán mã kết nối từ máy quản trị → nhận danh sách tài khoản từ Google Sheet, khỏi tạo Quản trị riêng.</summary>
    private async void OnJoin(object? sender, RoutedEventArgs e)
    {
        if (_retryCode is { } rc) { JoinButton.IsEnabled = false; await AutoJoin(rc); JoinButton.IsEnabled = true; if (!_firstRun) { _retryCode = null; Heading.Text = "Đăng nhập"; JoinButton.IsVisible = false; } return; }
        var code = await Ui.Prompt(this, "Dán mã kết nối (bắt đầu bằng KLKN1-) chép từ máy quản trị › Hệ thống › Kết nối › ⋯ Thêm › Chép mã kết nối máy:", "Dùng chung tài khoản");
        if (string.IsNullOrWhiteSpace(code)) return;
        ErrorText.Text = "";
        JoinButton.IsEnabled = false;
        try
        {
            var r = await App.Services.Accounts.ConnectAsync(code);
            _firstRun = false;
            Heading.Text = "Đăng nhập";
            SubHeading.Text = $"Đã nhận {r.Users} tài khoản từ Google Sheet. Nhập tài khoản được cấp để vào phần mềm.";
            FullNamePanel.IsVisible = ConfirmPanel.IsVisible = JoinButton.IsVisible = SetupButton.IsVisible = false;
            LoginButton.Content = "Đăng nhập";
            UserBox.Text = ""; PassBox.Text = "";
            UserBox.Focus();
        }
        catch (BusinessException ex) { ErrorText.Text = ex.Message; }
        finally { JoinButton.IsEnabled = true; }
    }
}

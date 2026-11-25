using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KhoLanh.Core.Lan;

namespace KhoLanh.App.Views;

/// <summary>Hệ thống › Mạng LAN: chọn vai của máy, xem các máy đang nối, xử lý trùng sửa sau khi mất máy chủ.</summary>
public partial class LanView : UserControl, IPage
{
    public string Title => "Mạng LAN";
    public string Subtitle => "Nhiều máy trong mạng nội bộ dùng chung dữ liệu module; máy chủ tắt vẫn làm việc được.";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _loading;

    public record MachineRow(string Name, string RoleLabel, string Addr, DateTime At, int Pending);
    public record ConflictRow(LanService.Conflict C)
    {
        public string At => C.At; public string Machine => C.Machine; public string Username => C.Username;
        public string ModuleLabel => C.ModuleLabel; public string Sheet => C.Sheet; public string StatusLabel => C.StatusLabel;
        public string Note => C.Note;
        public string Rows => $"{C.ClientRows} / {C.ServerRows}" + (C.Status is "gop" ? $" / {C.ResultRows}" : "");
    }

    public LanView()
    {
        InitializeComponent();
        var lan = App.Services.Lan;
        _loading = true;
        (lan.Role switch
        {
            LanService.RoleServer => RoleServer, LanService.RoleStandby => RoleStandby, LanService.RoleClient => RoleClient, _ => RoleSingle,
        }).IsChecked = true;
        PortBox.Text = lan.Port.ToString();
        _loading = false;
        OnRoleChanged(null, new RoutedEventArgs());
        _subscribed = lan;
        lan.Changed += OnLanChanged;
        LoadPlan();
        _timer.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => { _timer.Start(); Refresh(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        Refresh();
    }

    private void OnLanChanged() => Dispatcher.UIThread.Post(Refresh);
    private LanService _subscribed = null!;

    /// <summary>Phần LAN vừa được tạo lại (đổi vai) → nghe sự kiện của phần mới.</summary>
    private void Resubscribe()
    {
        _subscribed.Changed -= OnLanChanged;
        _subscribed = App.Services.Lan;
        _subscribed.Changed += OnLanChanged;
        Refresh();
    }

    // ----------------------------------------------------------------- phân vai theo tài khoản

    private readonly List<(string User, ComboBox Box)> _autoBoxes = new();
    private static readonly (string Role, string Label)[] AutoChoices =
    {
        (LanService.RoleClient, "Máy con"), (LanService.RoleServer, "Máy chủ chính"), (LanService.RoleStandby, "Máy dự phòng"),
    };

    private void LoadPlan()
    {
        var s = App.Services;
        var plan = s.Accounts.Plan ?? new LanPlan();
        AutoRows.Children.Clear(); _autoBoxes.Clear();
        if (!s.Accounts.Enabled)
        {
            AutoOn.IsEnabled = AutoSaveButton.IsEnabled = ClaimButton.IsEnabled = DropStandbyButton.IsEnabled = StandbyButton.IsEnabled = false;
            AutoMsg.Text = "Cần bật tài khoản dùng chung (Google Sheet) trước: Hệ thống › Sao lưu và cài đặt.";
            AutoSlots.Text = "";
            ShowManual(false);
            return;
        }
        // 1.106: vai theo TÀI KHOẢN — không cài tay IP / mã LAN.
        AutoRows.Children.Add(new TextBlock
        {
            Text = "• Quản trị: bật / tắt máy chủ trên máy giữ dữ liệu chính (nên là máy luôn bật).\n"
                   + "• Tài khoản có quyền \"Máy dự phòng LAN\" (Hệ thống › Tài khoản › phân quyền): máy đăng nhập là máy dự phòng.\n"
                   + "• Mọi tài khoản khác: máy con — đăng nhập là tự nối máy chủ trong cùng mạng.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12.5, Tag = "keep",
        });
        var canWrite = s.Accounts.CanWrite;
        var isServer = plan.Chu?.Id == s.Lan.MachineId && s.Lan.Role == LanService.RoleServer;
        var admin = s.Session.Role == KhoLanh.Core.Models.Roles.Admin;
        ClaimButton.IsVisible = admin && !isServer;
        ClaimButton.Content = plan.Chu is null ? "Bật máy chủ trên máy này" : "Chuyển máy chủ về máy này";
        ClaimButton.IsEnabled = canWrite;
        StandbyButton.IsVisible = admin && isServer;
        StandbyButton.IsEnabled = canWrite;
        DropStandbyButton.IsEnabled = canWrite && plan.DuPhong is not null;
        if (!canWrite) AutoMsg.Text = "Chỉ tài khoản Quản trị bật / tắt được máy chủ.";
        ShowPlanSlots(plan);
        ShowManual(true);
    }

    private void ShowManual(bool planOn)
    {
        ManualCard.IsVisible = ShareCard.IsVisible = !planOn;   // 1.106: có tài khoản dùng chung thì không cài tay IP / mã LAN
        ManualNote.IsVisible = planOn;
        foreach (var r in new[] { RoleSingle, RoleServer, RoleStandby, RoleClient }) r.IsEnabled = !planOn;
        CodePanel.IsEnabled = PortPanel.IsEnabled = ManualSave.IsEnabled = !planOn;
    }

    private void ShowPlanSlots(LanPlan plan)
    {
        var me = App.Services.Lan.MachineId;
        string S(LanPlan.Slot? x) => x is null ? "chưa có" : $"{x.Name}{(x.Id == me ? " (máy này)" : "")} — {string.Join(", ", x.Addrs)}";
        AutoSlots.Text = $"Máy chủ chính: {S(plan.Chu)}\nMáy dự phòng: {S(plan.DuPhong)}" + (App.LanPublishNote.Length > 0 ? "\n" + App.LanPublishNote : "");
    }

    private async void OnAutoSave(object? sender, RoutedEventArgs e)
    {
        var s = App.Services;
        var on = AutoOn.IsChecked == true;
        var noServer = on && s.Accounts.Plan?.Chu is null;
        if (noServer && !await Ui.Confirm(this, "Chưa có máy chủ chính. Đặt MÁY NÀY làm máy chủ chính (giữ dữ liệu chính cho mọi máy)?\n\nMáy chủ nên là máy luôn bật. Muốn máy khác làm máy chủ: bấm Đóng, ra máy đó bật ở đây.", "Bật mạng LAN", "Đặt máy này")) return;
        LanPlan? plan = null;
        if (!await Ui.RunAsync(this, async () => plan = await s.Accounts.UpdatePlanAsync(p => { p.On = on; return true; }))) return;
        s.Audit.Log(s.Session.Username, "Mạng LAN tự nối", on ? "bật" : "tắt");
        if (noServer)
        {
            string m = "";
            if (!await Ui.RunAsync(this, async () => m = await KhoLanh.Core.Lan.LanPlanSync.ClaimServerAsync(s))) { LoadPlan(); return; }
            Resubscribe(); App.PublishLan(); AutoMsg.Text = m; LoadPlan(); return;
        }
        // áp ngay cho máy này (như vừa đăng nhập)
        string? msg = null;
        try { msg = KhoLanh.Core.Lan.LanPlanSync.ApplyAtLogin(s); } catch (Exception ex) { msg = "Không tự đổi vai LAN được: " + ex.Message; }
        Resubscribe();
        App.PublishLan();
        AutoMsg.Text = "Đã lưu lên Google Sheet. Các máy khác tự nối ở lần đăng nhập tới." + (msg is null ? "" : "\n" + msg);
        LoadPlan();
    }

    private async void OnClaim(object? sender, RoutedEventArgs e)
    {
        var plan = App.Services.Accounts.Plan;
        var old = plan?.Chu is { } c && c.Id != App.Services.Lan.MachineId ? c.Name : null;
        if (!await Ui.Confirm(this, "Máy này sẽ làm Máy chủ chính (giữ database chính cho mọi máy)."
                + (old is null ? "" : $"\n\nMáy chủ cũ ({old}) trả vai sau khi máy này chép đủ dữ liệu, rồi thành máy con. Không mất dữ liệu.")
                + "\n\nMáy chủ nên là máy luôn bật.", "Chuyển máy chủ", "Chuyển")) return;
        string msg = "";
        if (!await Ui.RunAsync(this, async () => msg = await KhoLanh.Core.Lan.LanPlanSync.ClaimServerAsync(App.Services))) return;
        Resubscribe();
        App.PublishLan();
        AutoMsg.Text = msg;
        LoadPlan();
    }

    private async void OnRelease(object? sender, RoutedEventArgs e)
    {
        var d = App.Services.Accounts.Plan?.DuPhong;
        if (!await Ui.Confirm(this, "Tắt máy chủ trên máy này?" + (d is null
                ? "\n\nChưa có máy dự phòng: các máy con làm tiếp trên dữ liệu trong máy, bật máy chủ lại (ở máy này hoặc máy khác) thì tự gộp."
                : $"\n\nMáy dự phòng {d.Name} sẽ tự làm máy chủ."), "Tắt máy chủ", "Tắt")) return;
        string msg = "";
        if (!await Ui.RunAsync(this, async () => msg = await KhoLanh.Core.Lan.LanPlanSync.ReleaseServerAsync(App.Services))) return;
        Resubscribe();
        AutoMsg.Text = msg;
        LoadPlan();
    }

    private async void OnDropStandby(object? sender, RoutedEventArgs e)
    {
        if (App.Services.Accounts.Plan?.DuPhong is not { } d) return;
        if (!await Ui.Confirm(this, $"Bỏ ghi nhận máy dự phòng {d.Name}? Lần đăng nhập tới máy đó thành máy con.", "Bỏ máy dự phòng", "Bỏ")) return;
        if (!await Ui.RunAsync(this, async () => await App.Services.Accounts.UpdatePlanAsync(p => { p.DuPhong = null; return true; }))) return;
        AutoMsg.Text = "Đã bỏ máy dự phòng.";
        LoadPlan();
    }

    private string SelectedRole =>
        RoleServer.IsChecked == true ? LanService.RoleServer :
        RoleStandby.IsChecked == true ? LanService.RoleStandby :
        RoleClient.IsChecked == true ? LanService.RoleClient : LanService.RoleSingle;

    private void OnRoleChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var r = SelectedRole;
        CodePanel.IsVisible = r is LanService.RoleStandby or LanService.RoleClient;
        PortPanel.IsVisible = r != LanService.RoleSingle;
    }

    private void Refresh()
    {
        var lan = App.Services.Lan;
        if (!lan.Enabled)
        {
            StatusText.Text = "Máy đơn — dữ liệu chỉ nằm trong máy này.";
            DetailText.Text = "Chọn vai bên dưới để dùng chung dữ liệu với các máy khác trong mạng nội bộ.";
            SyncText.Text = "";
        }
        else
        {
            StatusText.Text = lan.Short;
            var pend = lan.PendingCount();
            DetailText.Text = $"Vai: {LanService.RoleLabel(lan.Role)} · Máy: {lan.MachineName} · Cổng {lan.Port} · Nhiệm kỳ {lan.Epoch}"
                              + (lan.Serving ? " · ĐANG LÀM MÁY CHỦ" : lan.Online ? $" · máy chủ: {lan.ServerName}" : " · chưa nối được máy chủ")
                              + (pend > 0 ? $" · {pend} bảng trong sổ chờ" : "")
                              + (lan.ListenError.Length > 0 ? "\n" + lan.ListenError : "");
            SyncText.Text = lan.LastSync.Length > 0 ? "Lần gửi sổ chờ gần nhất: " + lan.LastSync : "";
        }
        ShareCard.IsVisible = lan.Enabled && lan.Role == LanService.RoleServer && !App.Services.Accounts.Enabled;   // 1.106: dùng chung tài khoản thì máy tự nối, không cần mã LAN
        if (App.Services.Accounts.Plan is { } plan) ShowPlanSlots(plan);
        MachinesGrid.ItemsSource = lan.Machines().Select(m => new MachineRow(m.Name, LanService.RoleLabel(m.Role), m.Addr, m.At, m.Pending)).ToList();
        var sel = (ConflictsGrid.SelectedItem as ConflictRow)?.C.Id;
        var rows = lan.Conflicts(AllBox.IsChecked == true).Select(c => new ConflictRow(c)).ToList();
        ConflictsGrid.ItemsSource = rows;
        if (sel is { } id) ConflictsGrid.SelectedItem = rows.FirstOrDefault(r => r.C.Id == id);
        OnSelection(null, null);
    }

    private void OnAllChanged(object? sender, RoutedEventArgs e) => Refresh();

    private void OnSelection(object? sender, SelectionChangedEventArgs? e)
    {
        var c = (ConflictsGrid.SelectedItem as ConflictRow)?.C;
        var canAct = c is not null && (!App.Services.Lan.Enabled || App.Services.Lan.Serving);
        UseClientButton.IsEnabled = canAct && c!.Status is "cho" or "gop";
        KeepServerButton.IsEnabled = canAct && c!.Status == "cho";
        UndoButton.IsEnabled = canAct && c!.Status == "gop";
        SeenButton.IsEnabled = canAct && c!.Waiting;
    }

    private async void Act(string action, string question)
    {
        if ((ConflictsGrid.SelectedItem as ConflictRow)?.C is not { } c) return;
        if (!await Ui.Confirm(this, $"{question}\n\n{c.ModuleLabel} › bảng {c.Sheet} (máy {c.Machine}, {c.Username}, {c.At}).", "Trùng sửa", "Đồng ý")) return;
        if (await Ui.Run(this, () => App.Services.Lan.Resolve(c.Id, action))) Refresh();
    }

    private void OnUseClient(object? sender, RoutedEventArgs e) => Act("maycon", "Thay bảng trên máy chủ bằng bản của máy con? Những gì máy chủ sửa trong bảng này lúc mất kết nối sẽ mất (vẫn còn trong sao lưu).");
    private void OnKeepServer(object? sender, RoutedEventArgs e) => Act("maychu", "Giữ nguyên bản máy chủ, bỏ bản của máy con?");
    private void OnUndo(object? sender, RoutedEventArgs e) => Act("hoantac", "Bỏ kết quả tự gộp, đưa bảng về bản máy chủ trước khi gộp?");
    private void OnSeen(object? sender, RoutedEventArgs e) => Act("daxem", "Đánh dấu đã xem (giữ kết quả hiện tại)?");

    private async void OnCopyCode(object? sender, RoutedEventArgs e)
    {
        var code = App.Services.Lan.ConnectionCode();
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(code);
        ShareMsg.Text = "Đã chép mã LAN. Địa chỉ trong mã: máy này. Nếu đổi địa chỉ IP của máy chủ, chép lại mã cho các máy.";
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var role = SelectedRole;
        int? port = int.TryParse(PortBox.Text?.Trim(), out var p) && p is > 0 and < 65536 ? p : null;
        if (role != LanService.RoleSingle && port is null) { await Ui.Message(this, "Cổng không hợp lệ (1–65535)."); return; }
        var msg = role switch
        {
            LanService.RoleSingle => "Chuyển máy này về Máy đơn: thôi dùng chung dữ liệu, giữ dữ liệu đang có trong máy.",
            LanService.RoleServer => "Máy này làm Máy chủ chính: giữ database chính cho mọi máy. Sau khi mở lại, chép mã LAN cho các máy khác.",
            _ => "Máy này sẽ tải lại toàn bộ dữ liệu module từ máy chủ (dữ liệu module đang có trong máy được thay). Nếu máy này có dữ liệu riêng cần giữ, hãy sao lưu trước.",
        };
        if (!await Ui.Confirm(this, msg + "\n\nPhần mềm sẽ tự mở lại.", "Đổi vai máy", "Lưu và mở lại")) return;
        if (!await Ui.Run(this, () => LanService.Configure(App.Services.Settings, App.Services.Session, role, CodeBox.Text, port))) return;
        App.Services.Audit.Log(App.Services.Session.Username, "Đổi vai LAN", LanService.RoleLabel(role));
        Restart();
    }

    private static void Restart()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && !exe.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) && !exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        }
        catch { }
        App.Services.Lan.Dispose();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }
}

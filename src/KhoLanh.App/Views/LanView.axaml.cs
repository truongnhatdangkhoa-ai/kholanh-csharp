using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KhoLanh.Core.Lan;

namespace KhoLanh.App.Views;

/// <summary>Hệ thống › Mạng LAN (vai theo tài khoản, 1.106; bỏ cài vai tay theo máy 1.108): xem các máy đang nối, xử lý trùng sửa sau khi mất máy chủ.</summary>
public partial class LanView : UserControl, IPage
{
    public string Title => "Mạng LAN";
    public string Subtitle => "Nhiều máy trong mạng nội bộ dùng chung dữ liệu module; máy chủ tắt vẫn làm việc được.";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    public record MachineRow(string Name, string User, string RoleLabel, string Addr, DateTime At, int Pending);
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

    private void LoadPlan()
    {
        var s = App.Services;
        var plan = s.Accounts.Plan ?? new LanPlan();
        AutoRows.Children.Clear();
        if (!s.Accounts.Enabled)
        {
            ClaimButton.IsEnabled = DropStandbyButton.IsEnabled = StandbyButton.IsEnabled = false;
            AutoMsg.Text = "Mạng LAN chạy theo tài khoản dùng chung: cần kết nối Google Sheet chung trước (Hệ thống › Kết nối).";
            AutoSlots.Text = "";
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
    }

    private void ShowPlanSlots(LanPlan plan)
    {
        var me = App.Services.Lan.MachineId;
        string S(LanPlan.Slot? x) => x is null ? "chưa có" : $"{x.Name}{(x.Id == me ? " (máy này)" : "")} — {string.Join(", ", x.Addrs)}";
        AutoSlots.Text = $"Máy chủ chính: {S(plan.Chu)}\nMáy dự phòng: {S(plan.DuPhong)}" + (App.LanPublishNote.Length > 0 ? "\n" + App.LanPublishNote : "");
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

    private void Refresh()
    {
        var lan = App.Services.Lan;
        RefreshRemote();
        if (!lan.Enabled)
        {
            StatusText.Text = "Máy đơn — dữ liệu chỉ nằm trong máy này.";
            DetailText.Text = "Quản trị bấm \"Bật máy chủ trên máy này\" ở tab Máy chủ LAN; các máy khác đăng nhập là tự nối.";
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
        if (App.Services.Accounts.Plan is { } plan) ShowPlanSlots(plan);
        // 1.118: 2 loại — nối trong mạng LAN / nối qua Internet (đường hầm)
        var (lanM, netM) = lan.MachinesByWay();
        MachineRow Row(LanService.Seen m, string addr) => new(m.Name, m.User, LanService.RoleLabel(m.Role), addr, m.At, m.Pending);
        MachinesGrid.ItemsSource = lanM.Select(m => Row(m, m.Addr)).ToList();
        NetMachinesGrid.ItemsSource = netM.Select(m => Row(m, m.NetIp.Length > 0 ? m.NetIp : "(không rõ)")).ToList();
        LanMachinesTitle.Text = $"Máy nối trong mạng LAN · {lanM.Count} máy (5 phút gần nhất)";
        NetMachinesTitle.Text = $"Máy nối qua Internet · {netM.Count} máy (5 phút gần nhất)";
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

    // ------------------------------------------------------------------ 1.104: máy chủ từ xa (Cloudflare Tunnel)

    private void RefreshRemote()
    {
        var lan = App.Services.Lan;
        RemoteCard.IsVisible = lan.Role is LanService.RoleServer or LanService.RoleStandby;
        if (!RemoteCard.IsVisible) return;
        if (!RemoteUrlBox.IsFocused && (RemoteUrlBox.Text ?? "").Length == 0) RemoteUrlBox.Text = lan.PublicUrl;
        var last = lan.TunnelLastOk;
        RemoteInfo.Text = $"Địa chỉ trong máy cho Cloudflare Tunnel (Service HTTP): localhost:{lan.HttpPort}"
            + (lan.HttpError.Length > 0 ? "\n⚠ " + lan.HttpError : "")
            + (lan.PublicUrl.Length == 0 ? "\nChưa có địa chỉ từ xa — chỉ máy trong cùng mạng nối được."
                : "\nĐường hầm: " + (last == DateTime.MinValue ? "chưa có máy nào nối qua Internet từ lúc mở app." : $"máy ở xa nối lần cuối {last:HH:mm:ss dd/MM}."));
    }

    private async void OnRemoteSave(object? sender, RoutedEventArgs e)
    {
        if (!await Ui.Run(this, () => LanService.SetPublicUrl(App.Services.Settings, RemoteUrlBox.Text))) return;
        var u = App.Services.Lan.PublicUrl;
        RemoteUrlBox.Text = u;
        App.Services.Audit.Log(App.Services.Session.Username, "Địa chỉ máy chủ từ xa", u.Length > 0 ? u : "(bỏ)");
        RemoteMsg.Foreground = AppTheme.Brush("#1A2433");
        // ghi địa chỉ mới lên Google Sheet (kế hoạch LAN theo tài khoản) → máy ở xa đăng nhập là tự biết, không cần mã
        var err = await KhoLanh.Core.Lan.LanPlanSync.PublishAsync(App.Services);
        RemoteMsg.Text = err ?? (u.Length > 0 ? "Đã lưu và ghi lên Google Sheet chung — máy ở xa chỉ cần đăng nhập là tự nối qua địa chỉ này. Bấm «Kiểm tra nối từ xa» để thử." : "Đã bỏ địa chỉ từ xa.");
        RefreshRemote();
    }

    private async void OnRemoteTest(object? sender, RoutedEventArgs e)
    {
        RemoteTestButton.IsEnabled = false;
        RemoteMsg.Foreground = AppTheme.Brush("#5D6B78"); RemoteMsg.Text = "Đang thử nối qua Internet…";
        try
        {
            var r = await App.Services.Lan.TestPublicAsync(RemoteUrlBox.Text);
            RemoteMsg.Foreground = AppTheme.Brush("#1E7B4A"); RemoteMsg.Text = "✓ " + r;
        }
        catch (Exception ex)
        {
            RemoteMsg.Foreground = AppTheme.Brush("#B42318");
            RemoteMsg.Text = "✖ " + ex.Message + "\nKiểm tra: cloudflared đang chạy, Public hostname trỏ HTTP localhost:" + App.Services.Lan.HttpPort + ", DNS tên miền đã chuyển sang Cloudflare.";
        }
        finally { RemoteTestButton.IsEnabled = true; RefreshRemote(); }
    }
}

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
        lan.Changed += OnLanChanged;
        _timer.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => { _timer.Start(); Refresh(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        Refresh();
    }

    private void OnLanChanged() => Dispatcher.UIThread.Post(Refresh);

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
        ShareCard.IsVisible = lan.Enabled && lan.Role == LanService.RoleServer;
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

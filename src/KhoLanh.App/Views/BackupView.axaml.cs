using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public partial class BackupView : UserControl, IPage
{
    public string Title => "Cài đặt máy";
    public string Subtitle => "Tên kho, nơi lưu dữ liệu, khởi động cùng Windows, cập nhật phần mềm. Kết nối (tài khoản, điện thoại, Gmail): Hệ thống › Kết nối. Đồng bộ / sao lưu: Hệ thống › Đồng bộ & sao lưu.";

    public BackupView()
    {
        InitializeComponent();
        var s = App.Services;
        WarehouseBox.Text = s.Settings.Get(SettingsService.WarehouseName);
        PathText.Text = s.Db.FilePath; ToolTip.SetTip(PathText, s.Db.FilePath);
        var admin = Roles.IsAdmin(s.Session.Role);
        // Cập nhật online
        var u = s.Updates;
        UpdVersionText.Text = $"Đang dùng bản {UpdateService.Current}" + (AppUpdater.StagedVersion is { } sv ? $" · đã tải sẵn bản {sv}, chờ áp dụng" : "")
                              + (u.LastCheck.Length > 0 ? $" · lần hỏi gần nhất {u.LastCheck}" : "");
        _loading = true; UpdAutoBox.IsChecked = u.Auto; _loading = false;
        UpdAutoBox.IsEnabled = admin;
        UpdRollbackButton.IsVisible = admin && AppUpdater.HasPrevious;
        BuildBoot();
        AppUpdater.Progress += OnUpdProgress;
        DetachedFromVisualTree += (_, _) => AppUpdater.Progress -= OnUpdProgress;
    }

    private bool _loading;
    private void OnGoKetNoi(object? sender, RoutedEventArgs e) => MainWindow.Of(this)?.Navigate("ketnoi");
    private void OnGoDongBo(object? sender, RoutedEventArgs e) => MainWindow.Of(this)?.Navigate("dongbo");

    private async void OnUpdAuto(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var u = App.Services.Updates;
        if (await Ui.Run(this, () => u.SaveConfig(u.Repo, null, UpdAutoBox.IsChecked == true)))
            UpdStatus.Text = UpdAutoBox.IsChecked == true ? "Đã bật tự cập nhật." : "Đã tắt tự cập nhật.";
    }

    private void OnUpdProgress(string msg) => Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdStatus.Text = msg);

    private async void OnUpdCheck(object? sender, RoutedEventArgs e)
    {
        Version? v = null;
        if (!await Ui.RunAsync(this, async () => v = await AppUpdater.CheckAndStageAsync(quiet: false))) return;
        if (v is not null && await Ui.Confirm(this, $"Đã tải xong bản {v}. Cập nhật ngay (phần mềm tự mở lại, dữ liệu giữ nguyên)?", "Cập nhật phần mềm", "Cập nhật ngay"))
        {
            if (await Ui.Run(this, () => { if (!AppUpdater.ApplyAndRestart()) throw new BusinessException("Không áp dụng được bản mới — thử lại sau khi mở lại phần mềm."); }))
                AppUpdater.ExitForUpdate();
        }
    }

    private async void OnUpdRollback(object? sender, RoutedEventArgs e)
    {
        if (!await Ui.Confirm(this, "Quay lại bản phần mềm trước lần cập nhật gần nhất? Phần mềm sẽ tự mở lại. Dữ liệu giữ nguyên.", "Quay lại bản trước", "Quay lại")) return;
        if (await Ui.Run(this, () => { if (!AppUpdater.RollbackAndRestart()) throw new BusinessException("Không còn bản trước để quay lại."); }))
            AppUpdater.ExitForUpdate();
    }


    private async void OnSaveSettings(object? sender, RoutedEventArgs e)
    {
        var s = App.Services;
        if (await Ui.Run(this, () =>
            {
                s.Session.Require("backup");
                s.Settings.Set(SettingsService.WarehouseName, (WarehouseBox.Text ?? "").Trim());
            }))
            MainWindow.Of(this)?.SetSubtitle("Đã lưu tên kho — mở lại phần mềm để thấy trên thanh menu.");
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e) => Ui.OpenExternal(App.DataFolder);

    /// <summary>Đổi thư mục lưu database: chép dữ liệu hiện tại sang (hoặc dùng dữ liệu có sẵn ở đó), app đóng để mở lại.</summary>
    private async void OnChangeFolder(object? sender, RoutedEventArgs e)
    {
        var s = App.Services;
        if (KhoLanh.Core.Data.DataLocation.EnvFolder is not null)
        {
            await Ui.Message(this, "Máy này đang đặt thư mục dữ liệu bằng biến môi trường KHOLANH_DATA — đổi ở đó.");
            return;
        }
        if (!await Ui.Run(this, () => s.Session.Require("backup"))) return;
        var target = await Ui.OpenFolder(this, "Chọn thư mục lưu dữ liệu mới");
        if (target is null) return;
        if (KhoLanh.Core.Data.DataLocation.Same(target, App.DataFolder)) { await Ui.Message(this, "Đây là thư mục đang dùng."); return; }
        string full = "";
        if (!await Ui.Run(this, () => full = KhoLanh.Core.Data.DataLocation.Validate(target))) return;
        var has = KhoLanh.Core.Data.DataLocation.HasDb(full);
        var text = has
            ? $"Thư mục\n{full}\nđã có dữ liệu Kho Lạnh.\n\nChuyển sang DÙNG dữ liệu đó? Dữ liệu ở thư mục hiện tại vẫn giữ nguyên, không bị xóa."
            : $"Chép toàn bộ dữ liệu hiện tại sang\n{full}\nvà dùng thư mục này từ lần mở sau?\n\nThư mục cũ giữ nguyên làm bản dự phòng.";
        if (!await Ui.Confirm(this, text, "Đổi nơi lưu dữ liệu", has ? "Dùng dữ liệu đó" : "Chép và đổi")) return;
        if (!await Ui.Run(this, () =>
            {
                if (!has) s.Backup.ManualBackup(Path.Combine(full, KhoLanh.Core.Data.DataLocation.DbFileName));
                new KhoLanh.Core.Data.DataLocation().Save(full);
                s.Audit.Log(s.Session.Username, "Đổi nơi lưu dữ liệu", $"{App.DataFolder} → {full}");
            })) return;
        await Ui.Message(this, $"Đã đổi nơi lưu dữ liệu sang:\n{full}\n\nApp sẽ đóng lại — hãy mở lại Kho Lạnh để dùng thư mục mới.");
        AppUpdater.ExitForUpdate();
    }

    // ---------------- Khởi động cùng Windows + tự đăng nhập (mọi tài khoản, cài theo máy) ----------------
    private void BuildBoot() => BootPanel.Children.Add(BootSettings.Build(this, compact: true));
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public partial class BackupView : UserControl, IPage
{
    public string Title => "Sao lưu và cài đặt";
    public string Subtitle => "Thông tin công ty in trên phiếu, cập nhật phần mềm, sao lưu và khôi phục dữ liệu.";

    private record BackupRow(string Name, string FullName, DateTime LastWriteTime, long Kb);

    public BackupView()
    {
        InitializeComponent();
        var s = App.Services;
        CompanyBox.Text = s.Settings.Get(SettingsService.CompanyName);
        AddressBox.Text = s.Settings.Get(SettingsService.CompanyAddress);
        WarehouseBox.Text = s.Settings.Get(SettingsService.WarehouseName);
        PathText.Text = "File dữ liệu: " + s.Db.FilePath;
        var admin = Roles.IsAdmin(s.Session.Role);
        RestoreFileButton.IsVisible = RestoreSelButton.IsVisible = admin;
        LoadBackups();
        // Cập nhật online
        var u = s.Updates;
        UpdVersionText.Text = $"Đang dùng bản {UpdateService.Current}" + (AppUpdater.StagedVersion is { } sv ? $" · đã tải sẵn bản {sv}, chờ áp dụng" : "")
                              + (u.LastCheck.Length > 0 ? $" · lần hỏi gần nhất {u.LastCheck}" : "");
        UpdRepoBox.Text = u.Repo;
        UpdAutoBox.IsChecked = u.Auto;
        UpdTokenBox.Watermark = u.HasToken ? "Đã có mã — để trống = giữ nguyên" : "Chưa có mã — dán token vào đây";
        UpdConfigPanel.IsVisible = UpdSaveButton.IsVisible = admin;
        UpdRollbackButton.IsVisible = admin && AppUpdater.HasPrevious;
        AppUpdater.Progress += OnUpdProgress;
        DetachedFromVisualTree += (_, _) => AppUpdater.Progress -= OnUpdProgress;
    }

    private void OnUpdProgress(string msg) => Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdStatus.Text = msg);

    private async void OnUpdSave(object? sender, RoutedEventArgs e)
    {
        var token = string.IsNullOrWhiteSpace(UpdTokenBox.Text) ? null : UpdTokenBox.Text;
        if (await Ui.Run(this, () => App.Services.Updates.SaveConfig(UpdRepoBox.Text ?? "", token, UpdAutoBox.IsChecked == true)))
        {
            UpdTokenBox.Text = "";
            UpdTokenBox.Watermark = App.Services.Updates.HasToken ? "Đã có mã — để trống = giữ nguyên" : "Chưa có mã — dán token vào đây";
            UpdStatus.Text = "Đã lưu cài đặt cập nhật.";
        }
    }

    private async void OnUpdCheck(object? sender, RoutedEventArgs e)
    {
        Version? v = null;
        if (!await Ui.RunAsync(this, async () => v = await AppUpdater.CheckAndStageAsync(quiet: false))) return;
        if (v is not null && await Ui.Confirm(this, $"Đã tải xong bản {v}. Cập nhật ngay (phần mềm tự mở lại, dữ liệu giữ nguyên)?", "Cập nhật phần mềm", "Cập nhật ngay"))
        {
            if (await Ui.Run(this, () => { if (!AppUpdater.ApplyAndRestart()) throw new BusinessException("Không áp dụng được bản mới — thử lại sau khi mở lại phần mềm."); }))
                (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
    }

    private async void OnUpdRollback(object? sender, RoutedEventArgs e)
    {
        if (!await Ui.Confirm(this, "Quay lại bản phần mềm trước lần cập nhật gần nhất? Phần mềm sẽ tự mở lại. Dữ liệu giữ nguyên.", "Quay lại bản trước", "Quay lại")) return;
        if (await Ui.Run(this, () => { if (!AppUpdater.RollbackAndRestart()) throw new BusinessException("Không còn bản trước để quay lại."); }))
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void LoadBackups() =>
        Grid.ItemsSource = App.Services.Backup.ListAutoBackups()
            .Select(f => new BackupRow(f.Name, f.FullName, f.LastWriteTime, f.Length / 1024)).ToList();

    private async void OnSaveSettings(object? sender, RoutedEventArgs e)
    {
        var s = App.Services;
        if (await Ui.Run(this, () =>
            {
                s.Session.Require("backup");
                s.Settings.Set(SettingsService.CompanyName, (CompanyBox.Text ?? "").Trim());
                s.Settings.Set(SettingsService.CompanyAddress, (AddressBox.Text ?? "").Trim());
                s.Settings.Set(SettingsService.WarehouseName, (WarehouseBox.Text ?? "").Trim());
            }))
            MainWindow.Of(this)?.SetSubtitle("Đã lưu thông tin công ty.");
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e) => Ui.OpenExternal(App.DataFolder);

    private async void OnImportLegacy(object? sender, RoutedEventArgs e)
    {
        var path = await Ui.OpenFile(this, "Chọn database app cũ (khokhanh.db)", "db", "Database Kho Lạnh cũ");
        if (path is null) return;
        if (!await Ui.Confirm(this,
                "Chép toàn bộ dữ liệu Tồn kho An An và Tồn kho gửi từ file này vào app mới?\n\n" +
                "Dữ liệu 2 module đang có trong app mới sẽ được thay. App cũ không bị thay đổi.\n" +
                "Nên đóng app cũ trước khi chép để lấy bản mới nhất.", "Chép dữ liệu từ app cũ", "Chép")) return;
        KhoLanh.Core.Modules.LegacyImporter.Result? r = null;
        if (await Ui.Run(this, () => r = App.Services.Legacy.Import(path)) && r is not null)
            await Ui.Message(this, $"Đã chép {r.Sheets} bảng, {r.Rows:#,0} dòng.\n\nMở lại Tồn kho An An / Tồn kho gửi để thấy dữ liệu.");
    }

    private async void OnBackup(object? sender, RoutedEventArgs e)
    {
        var path = await Ui.SaveFile(this, "Sao lưu dữ liệu", $"KhoLanh_SaoLuu_{DateTime.Now:yyyyMMdd_HHmm}.db", "db", "File sao lưu Kho Lạnh");
        if (path is null) return;
        if (await Ui.Run(this, () => App.Services.Backup.ManualBackup(path)))
            await Ui.Message(this, $"Đã sao lưu ra:\n{path}");
    }

    private async void OnRestoreFile(object? sender, RoutedEventArgs e)
    {
        var path = await Ui.OpenFile(this, "Chọn file sao lưu", "db", "File sao lưu Kho Lạnh");
        if (path is not null) await Restore(path);
    }

    private async void OnRestoreSelected(object? sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is BackupRow r) await Restore(r.FullName);
        else await Ui.Message(this, "Chọn một bản sao lưu trong danh sách trước.");
    }

    private async Task Restore(string path)
    {
        if (!await Ui.Confirm(this,
                $"Khôi phục dữ liệu từ:\n{path}\n\nToàn bộ dữ liệu hiện tại sẽ được thay bằng bản sao lưu này. Phần mềm sẽ tự sao lưu bản hiện tại trước khi khôi phục, rồi yêu cầu đăng nhập lại.",
                "Khôi phục dữ liệu", "Khôi phục")) return;
        string safety = "";
        if (!await Ui.Run(this, () => safety = App.Services.Backup.Restore(path))) return;
        var main = MainWindow.Of(this);
        await Ui.Message(this, $"Đã khôi phục xong. Bản dữ liệu trước khi khôi phục được giữ ở:\n{safety}\n\nVui lòng đăng nhập lại.");
        main?.Logout();
    }
}

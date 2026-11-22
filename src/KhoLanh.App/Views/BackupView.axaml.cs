using Avalonia.Controls;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public partial class BackupView : UserControl, IPage
{
    public string Title => "Sao lưu và cài đặt";
    public string Subtitle => "Thông tin công ty in trên phiếu, sao lưu và khôi phục dữ liệu.";

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
    }

    private void LoadBackups() =>
        Grid.ItemsSource = App.Services.Backup.ListAutoBackups()
            .Select(f => new BackupRow(f.Name, f.FullName, f.LastWriteTime, f.Length / 1024)).ToList();

    private async void OnSaveSettings(object? sender, RoutedEventArgs e)
    {
        var s = App.Services;
        if (await Ui.Run(this, () =>
            {
                s.Session.RequireManager();
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

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public partial class BackupView : UserControl, IPage
{
    public string Title => "Sao lưu và cài đặt";
    public string Subtitle => "Thông tin công ty, cập nhật phần mềm, tài khoản dùng chung, sao lưu và khôi phục dữ liệu.";

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
        ShowAccounts();
        AppUpdater.Progress += OnUpdProgress;
        DetachedFromVisualTree += (_, _) => AppUpdater.Progress -= OnUpdProgress;
    }

    // ---------------- tài khoản dùng chung (Google Sheet) ----------------

    private void ShowAccounts()
    {
        var s = App.Services; var a = s.Accounts;
        var admin = Roles.IsAdmin(s.Session.Role);
        AcctStatus.Text = a.Enabled
            ? $"Đang dùng chung: mọi máy lấy tài khoản và phân quyền từ Google Sheet lúc đăng nhập (mất mạng thì dùng bản lưu trong máy). "
              + (a.CanWrite ? "Đã mở khóa ghi: tài khoản Quản trị được thêm / sửa / phân quyền tài khoản ở bất kỳ máy nào." : "Chưa mở khóa ghi: đăng nhập bằng tài khoản Quản trị để thêm / sửa tài khoản (khóa tự mở bằng mật khẩu Quản trị).")
              + (a.LastPull.Length > 0 ? $" Lần đồng bộ gần nhất: {a.LastPull}." : "")
            : "Chưa bật: mỗi máy có danh sách tài khoản riêng. Bật để mọi máy dùng chung tài khoản và phân quyền.";
        AcctSetupPanel.IsVisible = admin && !a.Enabled;
        AcctOnPanel.IsVisible = admin && a.Enabled;
        AcctCodeAdminButton.IsVisible = a.CanWrite;
        AcctScriptButton.Content = a.HasPendingScript ? "Tạo lại mã Apps Script (mã cũ sẽ hết dùng)" : "Tạo mã Apps Script (tự chép vào bộ nhớ)";
    }

    private async Task Copy(string text)
    {
        var cb = TopLevel.GetTopLevel(this)?.Clipboard;
        if (cb is not null) await cb.SetTextAsync(text);
    }

    private async void OnAcctScript(object? sender, RoutedEventArgs e)
    {
        string? script = null;
        if (!await Ui.Run(this, () => script = App.Services.Accounts.BuildScript())) return;
        var file = Path.Combine(App.DataFolder, "TaiKhoan-AppsScript.gs");
        try { File.WriteAllText(file, script!); } catch { }
        await Copy(script!);
        AcctMsg.Text = $"Đã chép mã Apps Script vào bộ nhớ (cũng lưu ở {file}). Dán vào Apps Script của Google Sheet, triển khai Ứng dụng web, rồi dán đường dẫn /exec vào ô trên.";
        ShowAccounts();
    }

    private async void OnAcctConnect(object? sender, RoutedEventArgs e)
    {
        var input = AcctInput.Text ?? "";
        AcctMsg.Text = "Đang kết nối Google Sheet…";
        AccountSyncService.ConnectResult? r = null;
        if (!await Ui.RunAsync(this, async () => r = await App.Services.Accounts.ConnectAsync(input))) { AcctMsg.Text = ""; return; }
        AcctInput.Text = "";
        AcctMsg.Text = r!.Uploaded
            ? $"Đã kết nối và đưa {r.Users} tài khoản của máy này lên Google Sheet. Bấm \"Chép mã cho máy nhân viên\" rồi gửi mã cho các máy khác."
            : $"Đã kết nối, nhận {r.Users} tài khoản từ Google Sheet" + (r.Admin ? " (máy quản trị)." : ".");
        ShowAccounts();
    }

    private async void OnAcctPull(object? sender, RoutedEventArgs e)
    {
        int n = 0;
        if (await Ui.RunAsync(this, async () => n = await App.Services.Accounts.PullAsync(TimeSpan.FromSeconds(30))))
            AcctMsg.Text = $"Đã đồng bộ {n} tài khoản từ Google Sheet.";
        ShowAccounts();
    }

    /// <summary>Thư mục chép sang máy mới: KhoLanh.exe + ket-noi.txt (mã máy nhân viên + mã GitHub). Máy mới mở lên chỉ cần đăng nhập.</summary>
    private async void OnAcctBundle(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var picked = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "Chọn nơi tạo bộ cài (VD: USB hoặc Desktop)" });
        var root = picked.FirstOrDefault()?.TryGetLocalPath();
        if (root is null) return;
        string? dir = null;
        if (!await Ui.RunAsync(this, () => Task.Run(() =>
        {
            var text = App.Services.Accounts.BundleText();
            if (App.Services.Lan.BundleLine() is { } lan)
                text += "# Mã LAN: máy mới tự làm máy con, dùng chung dữ liệu với máy chủ trong mạng nội bộ.\r\n" + lan + "\r\n";
            dir = Path.Combine(root, "KhoLanh-cai-may-moi");
            Directory.CreateDirectory(dir);
            if (AppUpdater.ExePath is { } exe) File.Copy(exe, Path.Combine(dir, "KhoLanh.exe"), true);
            File.WriteAllText(Path.Combine(dir, AccountSyncService.BundleFile), text);
            File.WriteAllText(Path.Combine(dir, "DOC-TRUOC.txt"),
                "Cài Kho Lạnh cho máy mới:\r\n" +
                "1. Chép CẢ thư mục này sang máy mới (VD vào Desktop). Giữ ket-noi.txt cạnh KhoLanh.exe.\r\n" +
                "2. Mở KhoLanh.exe (máy cần Internet lần đầu). Phần mềm tự nhận tài khoản và phân quyền từ Google Sheet.\r\n" +
                "3. Đăng nhập bằng tài khoản được cấp. Phần mềm tự cập nhật bản mới, không cần cài lại.\r\n" +
                "Nếu Windows hỏi (SmartScreen): bấm Thông tin thêm > Vẫn chạy.\r\n");
        }))) return;
        AcctMsg.Text = $"Đã tạo bộ cài ở {dir}. Chép cả thư mục sang máy mới, mở KhoLanh.exe là chỉ cần đăng nhập."
                       + (AppUpdater.ExePath is null ? " (Chưa chép được KhoLanh.exe — hãy tự chép file exe vào thư mục đó.)" : "")
                       + (App.Services.Updates.HasToken ? "" : " Lưu ý: chưa có mã GitHub nên máy mới chưa tự cập nhật được.");
        Ui.OpenExternal(dir!);
    }

    private async void OnAcctCodeStaff(object? sender, RoutedEventArgs e)
    {
        string? code = null;
        if (!await Ui.Run(this, () => code = App.Services.Accounts.ConnectionCode(forAdmin: false))) return;
        await Copy(code!);
        AcctMsg.Text = "Đã chép mã cho máy nhân viên (chỉ đọc tài khoản). Gửi mã này sang máy kia: lúc mở phần mềm lần đầu bấm \"Dùng chung tài khoản với máy khác…\" và dán vào.";
    }

    private async void OnAcctCodeAdmin(object? sender, RoutedEventArgs e)
    {
        if (!await Ui.Confirm(this, "Mã cho máy quản trị cho phép máy đó thêm / sửa / phân quyền mọi tài khoản. Chỉ gửi cho máy của người quản trị. Tiếp tục?", "Mã máy quản trị", "Chép mã")) return;
        string? code = null;
        if (!await Ui.Run(this, () => code = App.Services.Accounts.ConnectionCode(forAdmin: true))) return;
        await Copy(code!);
        AcctMsg.Text = "Đã chép mã cho máy quản trị.";
    }

    private async void OnAcctDisconnect(object? sender, RoutedEventArgs e)
    {
        if (!await Ui.Confirm(this, "Ngắt kết nối: máy này thôi lấy tài khoản từ Google Sheet, giữ nguyên danh sách tài khoản hiện có trong máy. Tiếp tục?", "Ngắt kết nối", "Ngắt")) return;
        if (await Ui.Run(this, () => App.Services.Accounts.Disconnect())) AcctMsg.Text = "Đã ngắt. Máy này dùng danh sách tài khoản riêng.";
        ShowAccounts();
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
                AppUpdater.ExitForUpdate();
        }
    }

    private async void OnUpdRollback(object? sender, RoutedEventArgs e)
    {
        if (!await Ui.Confirm(this, "Quay lại bản phần mềm trước lần cập nhật gần nhất? Phần mềm sẽ tự mở lại. Dữ liệu giữ nguyên.", "Quay lại bản trước", "Quay lại")) return;
        if (await Ui.Run(this, () => { if (!AppUpdater.RollbackAndRestart()) throw new BusinessException("Không còn bản trước để quay lại."); }))
            AppUpdater.ExitForUpdate();
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

    private async void OnImportLegacy(object? sender, RoutedEventArgs e)
    {
        var path = await Ui.OpenFile(this, "Chọn database app cũ (khokhanh.db)", "db", "Database Kho Lạnh cũ");
        if (path is null) return;
        if (!await Ui.Confirm(this,
                "Chép dữ liệu các module dùng chung (Tồn kho An An, Tồn kho gửi, Tổng hợp, Rã đông, Tồn kho theo vị trí, Sơ đồ kho, Kiểm kê, Kết quả kháng sinh) từ file này vào app mới?\n\n" +
                "Các bảng đó trong app mới sẽ được thay bằng bản chép. App cũ không bị thay đổi.\n" +
                "Nên đóng app cũ trước khi chép để lấy bản mới nhất.", "Chép dữ liệu từ app cũ", "Chép")) return;
        KhoLanh.Core.Modules.LegacyImporter.Result? r = null;
        if (await Ui.Run(this, () => r = App.Services.Legacy.Import(path)) && r is not null)
            await Ui.Message(this, $"Đã chép {r.Sheets} bảng, {r.Rows:#,0} dòng.\n\nMở lại các module để thấy dữ liệu.");
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

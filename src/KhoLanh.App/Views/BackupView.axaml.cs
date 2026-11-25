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
        LegacyCard.IsVisible = admin;   // 1.98: chép dữ liệu app cũ chỉ Quản trị
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

    /// <summary>
    /// Chép dữ liệu app cũ (1.79: MỌI module có trong khokhanh.db, chọn từng module): xem trước số bảng / dòng từng module,
    /// tích chọn (mặc định tất cả), module được chọn ở app mới được thay bằng bản chép. Chỉ đọc file cũ.
    /// </summary>
    private async void OnImportLegacy(object? sender, RoutedEventArgs e)
    {
        if (!Roles.IsAdmin(App.Services.Session.Role)) { await Ui.Message(this, "Chỉ tài khoản Quản trị được chép dữ liệu từ app cũ."); return; }
        var path = await Ui.OpenFile(this, "Chọn database app cũ (khokhanh.db)", "db", "Database Kho Lạnh cũ");
        if (path is null) return;
        List<KhoLanh.Core.Modules.LegacyImporter.Group> groups = new();
        if (!await Ui.Run(this, () => groups = App.Services.Legacy.Scan(path))) return;
        if (groups.Count == 0) { await Ui.Message(this, "Database app cũ chưa có dữ liệu module nào."); return; }
        var boxes = groups.Select(g => new CheckBox { IsChecked = true, Content = $"{g.Label} — {g.Sheets} bảng, {g.Rows:#,0} dòng", Tag = g.Db }).ToList();
        var all = new CheckBox { Content = "Chọn tất cả", IsChecked = true, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        all.IsCheckedChanged += (_, _) => { foreach (var b in boxes) b.IsChecked = all.IsChecked == true; };
        var list = new StackPanel { Spacing = 4 }; foreach (var b in boxes) list.Children.Add(b);
        var ok = new Button { Content = "Chép", Classes = { "primary" }, IsDefault = true }; var cancel = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = "Chọn module cần chép từ app cũ. Bảng của module được chọn trong app mới sẽ được THAY bằng bản chép (app cũ không bị thay đổi). Nên đóng app cũ trước để lấy bản mới nhất; nếu app mới đã nhập dữ liệu mới thì sao lưu trước (Hệ thống › Đồng bộ & sao lưu › Sao lưu).", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12.5, Tag = "keep" });
        body.Children.Add(all); body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360 });
        body.Children.Add(Ui.Buttons(cancel, ok));
        var w = Ui.Dialog("Chép dữ liệu từ app cũ", body, 520); var go = false;
        ok.Click += (_, _) => { go = true; w.Close(); }; cancel.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        var pick = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!).ToList();
        if (!go || pick.Count == 0) return;
        KhoLanh.Core.Modules.LegacyImporter.Result? r = null;
        if (await Ui.Run(this, () => r = App.Services.Legacy.Import(path, pick)) && r is not null)
            await Ui.Message(this, $"Đã chép {pick.Count} module · {r.Sheets} bảng · {r.Rows:#,0} dòng.\n\nMở lại các module để thấy dữ liệu (Lịch xuất ở Tổng quan).");
    }

    // ---------------- Khởi động cùng Windows + tự đăng nhập (mọi tài khoản, cài theo máy) ----------------
    private void BuildBoot() => BootPanel.Children.Add(BootSettings.Build(this, compact: true));
}

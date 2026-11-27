using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.Core.CongCu;
using KhoLanh.Core.Grid;

namespace KhoLanh.App.Controls;

/// <summary>
/// 1.154 — dùng chung cho nhóm menu Công cụ (Scan tài liệu, Container 3D, Xử lý PDF &amp; Excel, So sánh &amp; Tra cứu bảng):
/// <see cref="Exported"/> thay <c>Ui.Exported</c> (ghi nhớ file đã xuất + tự chép vào Lưu trữ cá nhân khi ô tích đang bật),
/// <see cref="Tab"/> = tab "Đã lưu" (danh sách file, mở, mở thư mục, xóa, ô tích tự lưu vào Lưu trữ cá nhân).
/// </summary>
public static class CongCuFiles
{
    private static CongCuFileService Svc => App.Services.CongCuFiles;
    public static event Action? Changed;

    /// <summary>Xuất xong: thông báo như cũ + ghi vào "Đã lưu" + (nếu đã tích) lưu vào Lưu trữ cá nhân ở nền, không chặn giao diện.</summary>
    public static void Exported(Visual? v, string tool, string path)
    {
        Ui.Exported(v, path);
        CcFile e;
        try { e = Svc.Record(tool, path); } catch { return; }
        Changed?.Invoke();
        if (!Svc.SyncOn) return;
        _ = SyncBg(v, e.Id, tool);
    }

    private static async Task SyncBg(Visual? v, string id, string tool)
    {
        var r = await Svc.SyncAsync(id);
        Dispatcher.UIThread.Post(() =>
        {
            Changed?.Invoke();
            Ui.Notify(v, r.Sync == "err" ? "Chưa lưu được vào Lưu trữ cá nhân" : "Lưu trữ cá nhân", r.Note,
                r.Sync == "ok" ? Avalonia.Controls.Notifications.NotificationType.Success : Avalonia.Controls.Notifications.NotificationType.Warning, null, 12);
        });
    }

    public static TabItem Tab(string tool, string header = "Đã lưu") => new() { Header = header, Content = Panel(tool) };

    public static Control Panel(string tool)
    {
        var model = new GridModel { ReadOnly = true };
        var grid = new SheetGrid(model) { SpareRows = 0, RowChecks = true };
        var list = new List<CcFile>();
        var status = new TextBlock { Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
        var syncBox = new CheckBox { Content = "Tự lưu file vào Lưu trữ cá nhân", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(syncBox, "Khi tích: mỗi file xuất ra từ các module Công cụ được chép thêm vào ngăn “Công cụ” của Lưu trữ cá nhân và đẩy lên Google Drive (nếu tài khoản đã đăng nhập Google). Nhớ riêng theo tài khoản.");
        var loading = false;

        string Sz(long b) => b >= 1048576 ? (b / 1048576.0).ToString("0.0", Ui.Vi) + " MB" : Math.Max(1, b / 1024) + " KB";
        string SyncText(CcFile f) => f.Sync switch { "ok" => "✔ Đã lưu + Drive", "local" => "Đã lưu trên máy", "err" => "✖ Lỗi", _ => "" };

        void Refresh()
        {
            loading = true;
            try { syncBox.IsChecked = Svc.SyncOn; } catch { syncBox.IsChecked = false; }
            loading = false;
            try { list = Svc.List(tool); } catch { list = new(); }
            var cols = new[]
            {
                new GridColumn("n", "Tên file", 260) { ReadOnly = true }, new GridColumn("t", "Lúc xuất", 130) { ReadOnly = true },
                new GridColumn("k", "Loại", 80) { ReadOnly = true }, new GridColumn("s", "Dung lượng", 90, "number") { ReadOnly = true },
                new GridColumn("l", "Lưu trữ cá nhân", 140) { ReadOnly = true }, new GridColumn("p", "Nơi lưu trên máy", 420) { ReadOnly = true },
            };
            model.Load(cols, list.Select(f => new[] { f.Name + (f.Exists ? "" : "  (không còn)"), f.At.ToString("HH:mm dd/MM/yyyy"), f.IsDir ? "Thư mục" : "File", Sz(f.Bytes), SyncText(f), f.Path }));
            status.Text = list.Count == 0
                ? "Chưa có file nào. File xuất ra từ module này nằm đúng ở nơi bạn chọn trong hộp “Lưu thành…”; từ nay danh sách ở đây nhớ đường dẫn để mở lại."
                : $"{list.Count} mục. Bấm đúp một dòng để mở. File luôn nằm ở cột “Nơi lưu trên máy”.";
        }

        CcFile? Sel()
        {
            var ch = grid.CheckedRows;
            var r = ch.Count > 0 ? ch[0] : grid.Selection.R1;
            return r >= 0 && r < list.Count ? list[r] : null;
        }
        List<CcFile> Picked()
        {
            var ch = grid.CheckedRows;
            if (ch.Count > 0) return ch.Where(i => i < list.Count).Select(i => list[i]).ToList();
            return Sel() is { } s ? new() { s } : new();
        }

        void Open() { if (Sel() is { } f) { if (!f.Exists) status.Text = "Không còn " + f.Path + " (đã bị xóa / di chuyển)."; else Ui.OpenExternal(f.Path); } }
        void Folder() { if (Sel() is { } f) { if (!f.Exists && !Directory.Exists(Path.GetDirectoryName(f.Path) ?? "")) status.Text = "Không còn thư mục chứa file."; else Ui.ShowInFolder(f.Path); } }

        async Task SyncSel()
        {
            var ps = Picked();
            if (ps.Count == 0) { status.Text = "Chọn (hoặc tích) ít nhất 1 dòng."; return; }
            status.Text = "Đang lưu vào Lưu trữ cá nhân…";
            string last = "";
            foreach (var p in ps) { var r = await Svc.SyncAsync(p.Id); last = r.Note; }
            Refresh(); status.Text = ps.Count > 1 ? $"Đã xử lý {ps.Count} mục. {last}" : last;
        }

        async Task Remove(bool withFile)
        {
            var ps = Picked();
            if (ps.Count == 0) { status.Text = "Chọn (hoặc tích) ít nhất 1 dòng."; return; }
            var msg = withFile ? $"Xóa {ps.Count} mục khỏi danh sách VÀ xóa file / thư mục trên máy? Không hoàn tác được." : $"Xóa {ps.Count} mục khỏi danh sách? (file trên máy vẫn giữ nguyên)";
            if (!await Ui.Confirm(grid, msg, yes: "Xóa")) return;
            await Ui.Run(grid, () => { foreach (var p in ps) Svc.Remove(p.Id, withFile); });
            Refresh();
        }

        syncBox.IsCheckedChanged += (_, _) =>
        {
            if (loading) return;
            try { Svc.SetSync(syncBox.IsChecked == true); } catch { return; }
            if (syncBox.IsChecked == true)
            {
                string hint;
                try
                {
                    hint = !App.Services.LuuTru.CanUse ? "Tài khoản này chưa có quyền “Lưu trữ cá nhân” nên sẽ chưa lưu được — nhờ Quản trị cấp quyền."
                        : App.Services.LuuTru.Config.Linked ? "Đã bật: từ giờ file xuất ra sẽ tự vào Lưu trữ cá nhân và lên Google Drive."
                        : "Đã bật: file xuất ra sẽ vào Lưu trữ cá nhân trên máy này. Chưa đăng nhập Google nên chưa lên Drive — đăng nhập ở Hệ thống › Kết nối (dùng chung với Sao lưu Drive).";
                }
                catch (Exception ex) { hint = ex.Message; }
                status.Text = hint;
            }
            else status.Text = "Đã tắt tự lưu vào Lưu trữ cá nhân.";
        };

        var open = Icon.Button("Mở file", "doc", "primary"); open.Click += (_, _) => Open();
        var folder = Icon.Button("Mở thư mục", "folder"); folder.Click += (_, _) => Folder();
        var save = Icon.Button("Lưu vào Lưu trữ cá nhân", "cloud"); save.Click += async (_, _) => await SyncSel();
        var del = Icon.Button("Xóa khỏi danh sách", "trash", "danger"); del.Click += async (_, _) => await Remove(false);
        var delFile = Icon.Button("Xóa cả file trên máy", "trash", "danger"); delFile.Click += async (_, _) => await Remove(true);
        var refresh = Icon.Button("Làm mới", "refresh"); refresh.Click += (_, _) => Refresh();

        var bar = new ToolRow();
        bar.Children.Add(syncBox); bar.Children.Add(ToolRow.Gap());
        bar.Children.Add(open); bar.Children.Add(folder); bar.Children.Add(save); bar.Children.Add(refresh);
        bar.Children.Add(ToolRow.More(del)); bar.Children.Add(ToolRow.More(delFile));

        grid.DoubleTapped += (_, _) => Open();
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        DockPanel.SetDock(status, Dock.Bottom); status.Margin = new Thickness(0, 6, 0, 0); root.Children.Add(status);
        root.Children.Add(grid);

        Action onChanged = () => Dispatcher.UIThread.Post(Refresh);
        root.AttachedToVisualTree += (_, _) => { Changed += onChanged; Refresh(); };
        root.DetachedFromVisualTree += (_, _) => Changed -= onChanged;
        return root;
    }
}

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

    /// <summary>Danh sách "Đã lưu" thay đổi từ nơi khác (vd Scan vừa 💾 Lưu tài liệu) → mọi tab Đã lưu đang mở vẽ lại.</summary>
    public static void Notify() => Changed?.Invoke();

    /// <summary>1.158 — mục riêng của module hiện chung trong tab Đã lưu (Scan: tài liệu lưu để sửa tiếp — trước nằm ở hộp "Tài liệu đã lưu").</summary>
    public sealed record Item(string Id, string Name, DateTime At, string Kind, long Bytes, string Where);
    public sealed class Extra
    {
        public required Func<List<Item>> List { get; init; }
        public required Func<Item, Task> Open { get; init; }
        public required Func<Item, Task<bool>> Delete { get; init; }
        /// <summary>Chữ nút "Mở" khi dòng đang chọn là mục riêng (vd "Mở để sửa tiếp").</summary>
        public string OpenLabel { get; init; } = "Mở";
    }

    public static TabItem Tab(string tool, string header = "Đã lưu") => new() { Header = header, Content = Panel(tool) };

    public static Control Panel(string tool, Extra? extra = null)
    {
        var model = new GridModel { ReadOnly = true };
        var grid = new SheetGrid(model) { SpareRows = 0, RowChecks = true };
        var list = new List<CcFile>();
        var items = new List<Item>();   // mục riêng (đứng trước file trong bảng)
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
            try { items = extra?.List() ?? new(); } catch { items = new(); }
            var cols = new[]
            {
                new GridColumn("n", "Tên file", 260) { ReadOnly = true }, new GridColumn("t", "Lúc xuất", 130) { ReadOnly = true },
                new GridColumn("k", "Loại", extra is null ? 80 : 170) { ReadOnly = true }, new GridColumn("s", "Dung lượng", 90, "number") { ReadOnly = true },
                new GridColumn("l", "Lưu trữ cá nhân", 140) { ReadOnly = true }, new GridColumn("p", "Nơi lưu trên máy", 420) { ReadOnly = true },
            };
            model.Load(cols, items.Select(i => new[] { i.Name, i.At.ToString("HH:mm dd/MM/yyyy"), i.Kind, Sz(i.Bytes), "", i.Where })
                .Concat(list.Select(f => new[] { f.Name + (f.Exists ? "" : "  (không còn)"), f.At.ToString("HH:mm dd/MM/yyyy"), f.IsDir ? "Thư mục" : "File", Sz(f.Bytes), SyncText(f), f.Path })));
            status.Text = items.Count + list.Count == 0
                ? "Chưa có file nào. File xuất ra từ module này nằm đúng ở nơi bạn chọn trong hộp “Lưu thành…”; từ nay danh sách ở đây nhớ đường dẫn để mở lại."
                : (items.Count > 0 ? $"{items.Count} tài liệu lưu để sửa tiếp · " : "") + $"{list.Count} file đã xuất. Bấm đúp một dòng để mở. File luôn nằm ở cột “Nơi lưu trên máy”.";
        }

        int Row() { var ch = grid.CheckedRows; return ch.Count > 0 ? ch[0] : grid.Selection.R1; }
        Item? SelItem() { var r = Row(); return r >= 0 && r < items.Count ? items[r] : null; }
        CcFile? Sel() { var r = Row() - items.Count; return r >= 0 && r < list.Count ? list[r] : null; }
        List<int> PickedRows()
        {
            var ch = grid.CheckedRows;
            if (ch.Count > 0) return ch.ToList();
            var r = Row(); return r >= 0 ? new() { r } : new();
        }
        List<CcFile> Picked() => PickedRows().Select(r => r - items.Count).Where(i => i >= 0 && i < list.Count).Select(i => list[i]).ToList();
        List<Item> PickedItems() => PickedRows().Where(r => r < items.Count).Select(r => items[r]).ToList();

        async void Open()
        {
            if (SelItem() is { } it && extra is not null) { try { await extra.Open(it); } catch (Exception ex) { status.Text = ex.Message; } return; }
            if (Sel() is { } f) { if (!f.Exists) status.Text = "Không còn " + f.Path + " (đã bị xóa / di chuyển)."; else Ui.OpenExternal(f.Path); }
        }
        void Folder()
        {
            if (SelItem() is { } it) { status.Text = $"“{it.Name}” lưu trong bộ nhớ của app ({it.Where}) — bấm “{extra?.OpenLabel ?? "Mở"}” để sửa tiếp, hoặc xuất PDF / ảnh để có file."; return; }
            if (Sel() is { } f) { if (!f.Exists && !Directory.Exists(Path.GetDirectoryName(f.Path) ?? "")) status.Text = "Không còn thư mục chứa file."; else Ui.ShowInFolder(f.Path); }
        }

        async Task SyncSel()
        {
            var ps = Picked();
            if (ps.Count == 0) { status.Text = PickedItems().Count > 0 ? "Tài liệu lưu để sửa tiếp chưa phải là file — xuất PDF / ảnh trước (file xuất ra sẽ có ở danh sách này và lưu được vào Lưu trữ cá nhân)." : "Chọn (hoặc tích) ít nhất 1 dòng."; return; }
            status.Text = "Đang lưu vào Lưu trữ cá nhân…";
            string last = "";
            foreach (var p in ps) { var r = await Svc.SyncAsync(p.Id); last = r.Note; }
            Refresh(); status.Text = ps.Count > 1 ? $"Đã xử lý {ps.Count} mục. {last}" : last;
        }

        async Task Remove(bool withFile)
        {
            var ps = Picked(); var its = extra is null ? new() : PickedItems();
            if (ps.Count + its.Count == 0) { status.Text = "Chọn (hoặc tích) ít nhất 1 dòng."; return; }
            if (its.Count > 0)
            {
                // tài liệu lưu để sửa tiếp không có "file ngoài" — xóa khỏi danh sách = xóa hẳn tài liệu (như nút Xóa ở hộp cũ)
                var names = string.Join(", ", its.Take(3).Select(i => "“" + i.Name + "”")) + (its.Count > 3 ? $" và {its.Count - 3} tài liệu khác" : "");
                if (!await Ui.Confirm(grid, $"Xóa hẳn tài liệu {names}? Không hoàn tác được.", yes: "Xóa")) return;
                foreach (var i in its) { try { await extra!.Delete(i); } catch (Exception ex) { status.Text = ex.Message; } }
                if (ps.Count == 0) { Refresh(); return; }
            }
            var msg = withFile ? $"Xóa {ps.Count} mục khỏi danh sách VÀ xóa file / thư mục trên máy? Không hoàn tác được." : $"Xóa {ps.Count} mục khỏi danh sách? (file trên máy vẫn giữ nguyên)";
            if (!await Ui.Confirm(grid, msg, yes: "Xóa")) { Refresh(); return; }
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
        if (extra is not null) ToolTip.SetTip(open, $"Tài liệu lưu để sửa tiếp: {extra.OpenLabel.ToLowerInvariant()}. File đã xuất: mở bằng chương trình mặc định.");
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

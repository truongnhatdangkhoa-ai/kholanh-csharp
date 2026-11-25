using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.LichXuat;
using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

/// <summary>
/// Tab LIST SẢN XUẤT của Phiếu xuất kho (theo tab "List sản xuất" của app cũ): kho List theo LSX (nạp từ PDF / Excel của khách hoặc tự tạo từ tồn kho),
/// mỗi List có trạng thái theo tồn Thành Phẩm Chính · số phiếu xuất kho · lịch xuất. Xem chi tiết ngày × size, gán Tên / Mã / Lô cho từng size,
/// tạo Phiếu xuất kho từ List, quét tồn kho để tự tạo List cho LSX còn tồn mà chưa có List.
/// </summary>
public sealed class ListSxView : UserControl
{
    private static PhieuKhoService Pnx => App.Services.PhieuKho;
    private static LichXuatService Lx => App.Services.LichXuat;
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45");
    private static readonly Dictionary<string, (string Bg, string Fg)> Mau = new()
    {
        ["ok"] = ("#E6F4EA", "#1E7B34"), ["wait"] = ("#E3F2FB", "#155E8C"), ["info"] = ("#EEF1F5", "#4B5565"), ["warn"] = ("#FFF4CC", "#8A5A00"), ["bad"] = ("#FDE7E7", "#B42318"),
    };

    private readonly Action<string> _openPhieu;
    private string? _sel;
    private List<LichXuatService.ListInfo> _all = new(), _shown = new();
    private readonly GridModel _lm = new() { ReadOnly = true };
    private readonly SheetGrid _lg;
    private readonly TextBox _q = new() { Watermark = "Tìm LSX, khách…", Width = 220 };
    private readonly CheckBox _hideDone = new() { Content = "Ẩn List đã hoàn tất > 30 ngày", IsChecked = true, FontSize = 12.5 };
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _detail = new() { Spacing = 8 };
    private readonly bool _edit;

    public ListSxView(string? openLsx, Action<string> openPhieu)
    {
        _sel = openLsx; _openPhieu = openPhieu; _edit = Pnx.CanEdit;
        _lg = new SheetGrid(_lm) { SpareRows = 0, CellBackground = (r, c) => c == 7 && r < _shown.Count ? Brush(Mau[_shown[r].Mau].Bg) : null };
        _lg.SelectionChanged += () => { if (_lg.Row >= 0 && _lg.Row < _shown.Count) { _sel = _shown[_lg.Row].Rec.Lsx; ShowDetail(); } };
        var deb = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        deb.Tick += (_, _) => { deb.Stop(); Render(); };
        _q.TextChanged += (_, _) => { deb.Stop(); deb.Start(); };
        _hideDone.IsCheckedChanged += (_, _) => Render();

        var bar = new ToolRow { };
        if (_edit)
        {
            bar.Children.Add(Btn("📂 Nạp file List (PDF / Excel)…", async () => await Import(), primary: true));
            bar.Children.Add(Btn("⟳ Quét tồn kho", async () => await ScanStock(), "LSX còn tồn trong Thành Phẩm Chính (Tồn kho An An) mà chưa có List → tự tạo List từ tồn kho"));
        }
        bar.Children.Add(Btn("Tải lại", async () => await LoadAsync()));
        bar.Children.Add(_q); bar.Children.Add(_hideDone);

        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 6) };
        top.Children.Add(bar); top.Children.Add(_status);
        // 1.57: danh sách LSX / chi tiết List thành 2 tab con (bấm đúp LSX → mở chi tiết); trước 2 cột, màn hình nhỏ mỗi bên chật.
        var right = new ScrollViewer { Content = new Border { Padding = new Thickness(0, 0, 4, 8), Child = _detail }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _split = Ui.MasterDetail("Danh sách LSX", new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = _lg }, _lg, "Chi tiết List", right);
        var split = _split;
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(split);
        Content = root;
        AttachedToVisualTree += async (_, _) => await LoadAsync();
    }

    /// <summary>Chọn List của LSX (mở từ tab "⚡ Tự động đã tạo").</summary>
    public void SelectLsx(string lsx) { _sel = lsx; _q.Text = ""; _ = LoadAsync(); if (_split is not null) _split.SelectedIndex = 1; }
    private TabControl? _split;

    private static IBrush Brush(string c) => AppTheme.Brush(c);
    private static Button Btn(string text, Func<Task> click, string? tip = null, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += async (_, _) => await click();
        return b;
    }
    private static string F(double d) => d.ToString("#,0.###", Ui.Vi);

    private async Task LoadAsync()
    {
        _status.Text = "Đang tải…";
        try
        {
            await Task.Run(() =>
            {
                Pnx.Load();
                if (Lx.CanView) Lx.Load(); else Lx.LoadPnx();
                _all = Lx.ListInfos();
            });
        }
        catch (Exception e) { _status.Text = ""; await Ui.Message(this, e is BusinessException ? e.Message : Ui.Describe(e)); return; }
        Render();
    }

    private void Render()
    {
        var q = Core.ChungTu.ChungTuService.StripAccents(_q.Text ?? "");
        _shown = _all.Where(i => (q.Length == 0 || Core.ChungTu.ChungTuService.StripAccents(i.Rec.Lsx + " " + i.Rec.Khach + " " + i.Rec.File).Contains(q))
                                 && !(_hideDone.IsChecked == true && i.Mau == "ok" && DateTime.TryParse(i.Rec.CapNhat, out var d) && (DateTime.Now - d).TotalDays > 30))
            .OrderBy(i => i.Mau switch { "bad" => 0, "warn" => 1, "wait" => 2, "info" => 3, _ => 4 }).ThenByDescending(i => i.Rec.CapNhat, StringComparer.Ordinal).ToList();
        _lm.Load(new[]
        {
            new GridColumn("lsx", "LSX", 110), new GridColumn("khach", "Khách", 110), new GridColumn("nguon", "Nguồn", 120), new GridColumn("ngay", "Ngày × size", 85),
            new GridColumn("ton", "Tồn (kiện)", 80, "number"), new GridColumn("phieu", "Phiếu XK", 65, "number"), new GridColumn("lich", "Lịch xuất", 85), new GridColumn("tt", "Tình trạng", 250),
        }, _shown.Select(i => new[]
        {
            i.Rec.Lsx, i.Rec.Khach, (PhieuKhoService.TagOf(i.Rec.Data) is not null ? "⚡ " : "") + (i.Rec.File == "(tồn kho)" ? "Tồn kho (tự tạo)" : i.Rec.File), $"{i.Rec.Data.Rows.Count} × {i.Rec.Data.Sizes.Count}",
            i.Ton is null ? "—" : i.Ton > 0 ? F(i.Ton.Value) : "Hết", i.Phieu.ToString(), i.LichN == 0 ? "chưa có" : $"{i.LichOk}/{i.LichN} đã xuất", (i.CanhBao ? "⚠ " : "") + i.Nhan,
        }));
        _lg.Refresh();
        var warn = _all.Count(i => i.CanhBao);
        _status.Text = $"{_all.Count} List trong kho · đang hiện {_shown.Count}" + (warn > 0 ? $" · ⚠ {warn} List cần xem" : "") +
            (Lx.PnxError.Length > 0 ? " · " + Lx.PnxError : "") + ". Tồn lấy từ Thành Phẩm Chính của Tồn kho An An (cột Hợp đồng).";
        var idx = _sel is null ? -1 : _shown.FindIndex(i => PhieuKhoCore.LsxNorm(i.Rec.Lsx) == PhieuKhoCore.LsxNorm(_sel));
        if (idx >= 0) _lg.Select(idx, 0);
        ShowDetail();
    }

    private void ShowDetail()
    {
        _detail.Children.Clear();
        var info = _sel is null ? null : _all.FirstOrDefault(i => PhieuKhoCore.LsxNorm(i.Rec.Lsx) == PhieuKhoCore.LsxNorm(_sel));
        if (info is null) { _detail.Children.Add(new TextBlock { Text = _all.Count == 0 ? "Kho List đang trống. Nạp file List của khách, hoặc bấm ⟳ Quét tồn kho để tự tạo List cho LSX còn tồn." : "Chọn một List bên trái để xem.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap }); return; }
        var d = info.Rec.Data;
        _detail.Children.Add(new TextBlock { Text = "LSX " + info.Rec.Lsx, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Dark });
        var (bg, fg) = Mau[info.Mau];
        _detail.Children.Add(new Border { Background = Brush(bg), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = (info.CanhBao ? "⚠ " : "") + info.Nhan, Foreground = Brush(fg), FontWeight = FontWeight.SemiBold } });
        _detail.Children.Add(new TextBlock
        {
            Text = $"Khách: {(info.Rec.Khach.Length > 0 ? info.Rec.Khach : "—")} · Nguồn: {(info.Rec.File == "(tồn kho)" ? "tự tạo từ tồn kho" : info.Rec.File)} · cập nhật {(DateTime.TryParse(info.Rec.CapNhat, out var cn) ? cn.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : info.Rec.CapNhat)}\n" +
                   $"Tồn kho: {(info.Ton is null ? "chưa theo dõi được (Tồn kho An An chưa có dữ liệu)" : info.Ton > 0 ? F(info.Ton.Value) + " kiện" : "đã hết")} · Phiếu xuất kho: {(info.Phieu > 0 ? info.Phieu + " phiếu" : "chưa có")} · Lịch xuất: {(info.LichN == 0 ? "chưa có" : $"{info.LichOk}/{info.LichN} đã xuất")}",
            FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
        });
        var acts = new ToolRow { };
        var ph = Pnx.List.FirstOrDefault(p => p.Loai == "xuat_khau" && PhieuKhoCore.LsxList(p.Lsx).Any(c => LichXuatService.LsxKeyOf(c) == LichXuatService.LsxKeyOf(info.Rec.Lsx)));
        if (ph is not null) acts.Children.Add(Btn("📄 Mở phiếu " + (ph.SoPhieu.Length > 0 ? ph.SoPhieu : ""), () => { _openPhieu(ph.Id); return Task.CompletedTask; }));
        else if (_edit) acts.Children.Add(Btn("➕ Tạo phiếu xuất kho từ List", async () => await MakePhieu(info), primary: true));
        if (_edit) acts.Children.Add(Btn("Xóa List", async () => await DeleteList(info)));
        _detail.Children.Add(acts);
        if (d.Warnings.Count > 0)
            _detail.Children.Add(new Border { Background = Brush("#FFF4CC"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Child = new TextBlock { Text = string.Join("\n", d.Warnings.Select(w => "⚠ " + w)), FontSize = 12, TextWrapping = TextWrapping.Wrap } });

        // size: Tên / Mã / Lô gán sẵn
        _detail.Children.Add(new TextBlock { Text = "Hàng theo size (gán Tên hàng / Mã hàng / Mã lô — phiếu tạo từ List dùng các ô này)", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        var sm = new GridModel { ReadOnly = !_edit };
        sm.Load(new[] { new GridColumn("size", "Size", 90) { ReadOnly = true }, new GridColumn("ten", "Tên hàng", 200), new GridColumn("ma", "Mã hàng", 180), new GridColumn("lo", "Mã lô", 80), new GridColumn("tong", "Tổng kiện", 75, "number") { ReadOnly = true } },
            d.Sizes.Select(z => new[] { z.Size, z.Ten, z.Ma, z.Lo, F(d.Rows.Sum(r => r.Qty.GetValueOrDefault(z.Size))) }));
        // 1.52: cao theo số dòng thật (tối thiểu 3 dòng, kèm thanh cuộn ngang) — trước đây chỉ thấy 1 dòng; ⤢ ở góc bảng mở cửa sổ lớn
        _detail.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Height = SubH(d.Sizes.Count, 320), Child = new SheetGrid(sm) { SpareRows = 0, LargeTitle = $"LSX {info.Rec.Lsx} — Hàng theo size" } });
        if (_edit) _detail.Children.Add(Btn("💾 Lưu Tên / Mã / Lô", async () =>
        {
            for (var i = 0; i < d.Sizes.Count; i++) { d.Sizes[i].Ten = sm.Get(i, 1).Trim(); d.Sizes[i].Ma = sm.Get(i, 2).Trim(); d.Sizes[i].Lo = sm.Get(i, 3).Trim(); }
            if (await Ui.Run(this, () => Pnx.SaveListData(d, info.Rec.File))) await LoadAsync();
        }));

        // bảng ngày × size
        _detail.Children.Add(new TextBlock { Text = $"Ngày sản xuất × size ({d.Rows.Count} dòng · tổng {F(d.Rows.Sum(r => r.Qty.Values.Sum()))} {(d.DonVi.Length > 0 ? d.DonVi : "kiện")})", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        var cols = new List<GridColumn> { new("date", "Ngày SX", 90), new("exp", "Hạn dùng", 85) };
        cols.AddRange(d.Sizes.Select((z, i) => new GridColumn("s" + i, z.Size, 75, "number")));
        cols.Add(new GridColumn("tot", "Tổng", 70, "number")); cols.Add(new GridColumn("mks", "MKS", 90));
        var rm = new GridModel { ReadOnly = true };
        rm.Load(cols, d.Rows.Select(r =>
        {
            var a = new List<string> { r.Date.Length > 0 ? r.Date : "(chưa rõ ngày)", r.Exp };
            a.AddRange(d.Sizes.Select(z => r.Qty.TryGetValue(z.Size, out var q) ? F(q) : ""));
            a.Add(F(r.Qty.Values.Sum()));
            a.Add(r.Mks ? (r.MksQty is { Count: > 0 } mq ? string.Join(", ", mq.Select(x => x.Key + ":" + F(x.Value))) : r.MksSizes is { Count: > 0 } ms ? string.Join(", ", ms) : "MKS") : "");
            return a.ToArray();
        }));
        _detail.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Height = SubH(d.Rows.Count, 440), Child = new SheetGrid(rm) { SpareRows = 0, LargeTitle = $"LSX {info.Rec.Lsx} — Ngày sản xuất × size" } });
    }

    /// <summary>Chiều cao bảng con: tiêu đề + số dòng (≥ 3) + thanh cuộn ngang, tối đa <paramref name="max"/>.</summary>
    private static double SubH(int rows, double max) => SheetGrid.FitHeight(Math.Max(3, rows), max);

    private async Task Import()
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Chọn List sản xuất của khách (PDF hoặc Excel)", AllowMultiple = true,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("List sản xuất (PDF, Excel)") { Patterns = new[] { "*.pdf", "*.xlsx", "*.xlsm" } } },
        });
        var msgs = new List<string>();
        foreach (var f in files)
        {
            if (Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(f) is not { } path) continue;
            var name = Path.GetFileName(path);
            ListData d;
            try { d = await Task.Run(() => Pnx.ReadList(path)); }
            catch (Exception e) { msgs.Add("❌ " + name + ": " + (e is BusinessException ? e.Message : Ui.Describe(e))); continue; }
            if (!d.Ok) { msgs.Add("❌ " + name + ": " + (d.Warnings.FirstOrDefault() ?? "không đọc được List sản xuất.")); continue; }
            if (d.Lsx.Trim().Length == 0)
            {
                var lsx = await Ui.Prompt(this, $"File {name} không ghi số LSX. Nhập số LSX cho List này:", "Số LSX");
                if (string.IsNullOrWhiteSpace(lsx)) { msgs.Add("↩ " + name + ": bỏ qua (không có số LSX)."); continue; }
                d.Lsx = lsx.Trim().Replace("LSX", "", StringComparison.OrdinalIgnoreCase).Trim();
            }
            if (Pnx.FindList(d.Lsx) is { } old)
            {
                if (!await Ui.Confirm(this, $"LSX {d.Lsx} đã có trong kho ({old.Data.Rows.Count} ngày, nguồn {old.File}). Thay bằng file {name}?\nTên hàng / Mã hàng / Mã lô đã gán cho size trùng tên được giữ lại.", "List đã có", "Thay"))
                { msgs.Add("↩ " + name + ": giữ nguyên LSX " + d.Lsx + " đã lưu."); continue; }
                foreach (var z in d.Sizes) if (old.Data.Sizes.FirstOrDefault(o => o.Size == z.Size) is { } o) { if (z.Ten.Length == 0) z.Ten = o.Ten; if (z.Ma.Length == 0) z.Ma = o.Ma; if (z.Lo.Length == 0) z.Lo = o.Lo; }
            }
            try { Pnx.SaveListData(d, name); msgs.Add($"✅ {name}: đã lưu LSX {d.Lsx} — {d.Rows.Count} ngày × {d.Sizes.Count} size" + (d.Warnings.Count > 0 ? $" · ⚠ {d.Warnings.Count} cảnh báo" : "") + "."); _sel = d.Lsx; }
            catch (Exception e) { msgs.Add("❌ " + name + ": " + e.Message); }
        }
        if (msgs.Count > 0) { await LoadAsync(); await Ui.Message(this, string.Join("\n", msgs), "Nạp List sản xuất"); }
    }

    private async Task ScanStock()
    {
        _status.Text = "Đang quét Tồn kho An An…";
        (int, List<string>, string) r = default;
        if (!await Ui.RunAsync(this, async () => r = await Task.Run(Lx.ScanStockLists))) return;
        await LoadAsync();
        await Ui.Message(this, r.Item3, "Quét tồn kho");
    }

    private async Task MakePhieu(LichXuatService.ListInfo info)
    {
        Phieu? saved = null; ListSxApply.ApplyResult? a = null;
        if (!await Ui.Run(this, () =>
        {
            Pnx.Load();
            var p = Pnx.NewPhieu();
            p.Lsx = info.Rec.Lsx; p.Khach = info.Rec.Khach;
            var lc = Lx.Rows.Where(r => LichXuatCore.Keys(r.Lsx).Any(c => LichXuatService.LsxKeyOf(c) == LichXuatService.LsxKeyOf(info.Rec.Lsx)) && r.TrangThai != "ok").OrderBy(r => r.Ngay).FirstOrDefault();
            if (lc is not null) { p.Ngay = lc.Ngay; if (lc.ContSo.Length > 0) p.Xe.Cont = lc.ContSo; }
            a = Pnx.ApplyList(p, info.Rec.Data, false);
            PhieuKhoCore.RefreshNumbers(Pnx.List, p);
            saved = Pnx.Save(p);
        }) || saved is null) return;
        await Ui.Message(this, $"Đã tạo phiếu {saved.SoPhieu} từ List LSX {info.Rec.Lsx}." + (a?.Notes.Count > 0 ? "\n" + string.Join("\n", a.Notes) : ""), "Tạo phiếu xuất kho");
        _openPhieu(saved.Id);
    }

    private async Task DeleteList(LichXuatService.ListInfo info)
    {
        if (!await Ui.Confirm(this, $"Xóa List LSX {info.Rec.Lsx} khỏi kho?\n\nPhiếu xuất kho đã tạo không bị ảnh hưởng.", "Xóa List", "Xóa")) return;
        if (await Ui.Run(this, () => Pnx.DeleteList(info.Rec.Lsx))) { _sel = null; await LoadAsync(); }
    }
}

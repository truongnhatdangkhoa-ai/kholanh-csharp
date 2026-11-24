using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.Services;
using KhoLanh.Core.Modules;
using static KhoLanh.Core.PhieuKho.PhieuKhoCore;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Phiếu xuất kho (C#, theo "Phiếu nhập/xuất kho" của app cũ — không dùng WebView2). Giai đoạn 1: Phiếu XUẤT KHẨU.
/// Soạn phiếu (mặt hàng × dòng DATE · size · kiện · MKS), dán email "Thông tin xe", nội dung email gửi kèm, xuất Excel điền thẳng vào file mẫu .xlsm.
/// </summary>
public sealed class PhieuKhoView : UserControl, IPage
{
    public string Title => "Phiếu xuất kho";
    public string Subtitle => "Phiếu xuất khẩu theo LSX (List sản xuất, email thông tin xe, xuất .xlsm) và Phiếu xuất nội địa (Cargo list) cho Thuận Phước · Gia công · Gửi kho (tra kháng sinh, phiếu theo thị trường, xuất Excel).";

    private static PhieuKhoService Svc => App.Services.PhieuKho;
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45");
    private static readonly IBrush Warn = AppTheme.Brush("#B23B00");
    private static readonly IBrush CardBg = AppTheme.Brush("#F6F9FC");

    private readonly bool _edit, _export;
    private bool _loading, _dirty;
    private Phieu? _cur;
    private List<Phieu> _shown = new();
    private readonly ListBox _list = new() { MinWidth = 280 };
    private readonly TextBox _search = new() { Watermark = "Tìm số phiếu, LSX, khách…" };
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _err = new() { FontSize = 12.5, Foreground = Warn, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _totals = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Dark, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _editor = new() { Spacing = 8 };
    private readonly TextBox _mail = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"), FontSize = 12.5, TextWrapping = TextWrapping.NoWrap };
    private readonly TabControl _tabs;
    private TextBox? _soBox, _o40Box;
    private readonly List<(MatHang M, GridModel Gm)> _blocks = new();
    private readonly Button _save, _del, _exp, _copy;

    private readonly string? _openId;
    private readonly ListSxView _lsxView;
    private readonly PnxAutoView _autoView;
    private readonly TabItem _autoTab = new() { Header = "⚡ Tự động đã tạo" };
    private readonly TabControl _outer;

    /// <summary>Mở module: <paramref name="tab"/> 0 = Phiếu xuất khẩu · 1 = List sản xuất · 2 = Cargo list; <paramref name="openPhieuId"/> / <paramref name="openListLsx"/> = mở thẳng phiếu / List đó.</summary>
    public PhieuKhoView(int tab = 0, string? openPhieuId = null, string? openListLsx = null)
    {
        _openId = openPhieuId;
        _edit = Svc.CanEdit; _export = Svc.CanExport;
        _list.SelectionChanged += async (_, _) => await OnPick();
        var deb = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        deb.Tick += (_, _) => { deb.Stop(); RenderList(); };
        _search.TextChanged += (_, _) => { deb.Stop(); deb.Start(); };

        _save = Btn("💾 Lưu phiếu", async () => await DoSave(), primary: true); _save.IsEnabled = _edit;
        _del = Btn("Xóa phiếu", async () => await DoDelete(), danger: true); _del.IsEnabled = _edit;
        _exp = Btn("📤 Xuất Excel (.xlsm)", async () => await DoExport()); _exp.IsEnabled = _export;
        _copy = Btn("📋 Copy email", async () => await CopyMail());
        var bar = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        bar.Children.Add(_save); bar.Children.Add(_exp); bar.Children.Add(_copy); bar.Children.Add(_del);
        bar.Children.Add(Btn("📩 Dán email thông tin xe…", async () => await PasteXe()));
        if (_edit) bar.Children.Add(Btn("📄 Nạp List sản xuất…", async () => await ListDialog()));

        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Soạn phiếu", Content = new ScrollViewer { Content = new Border { Padding = new Thickness(0, 6, 12, 12), Child = _editor }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } },
                new TabItem { Header = "Nội dung email", Content = new Border { Padding = new Thickness(0, 6, 0, 0), Child = _mail } },
            },
        };
        _tabs.SelectionChanged += (_, _) => RefreshMail();

        var leftTop = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        if (_edit) leftTop.Children.Add(Btn("➕ Phiếu xuất khẩu mới", async () => await NewPhieu(), primary: true));
        if (_export) leftTop.Children.Add(Btn("📊 Thống kê xuất khẩu theo tháng…", async () => await DoThongKe()));
        leftTop.Children.Add(_search);
        leftTop.Children.Add(new TextBlock { Text = "Phiếu xuất khẩu theo LSX. Thuận Phước · Gia công · Gửi kho làm ở tab “Phiếu xuất nội địa”.", Classes = { "muted" }, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        var left = new DockPanel { Width = 300 };
        DockPanel.SetDock(leftTop, Dock.Top); left.Children.Add(leftTop); left.Children.Add(Framed(_list));

        var head = new StackPanel { Spacing = 4 };
        head.Children.Add(bar); head.Children.Add(_err); head.Children.Add(_totals); head.Children.Add(_status);
        var right = new DockPanel { Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(head, Dock.Top); right.Children.Add(head); right.Children.Add(_tabs);
        var root = new DockPanel();
        DockPanel.SetDock(left, Dock.Left); root.Children.Add(left); root.Children.Add(right);
        // 2 phần của module: phiếu xuất khẩu theo LSX (List sản xuất) và Cargo list (hàng tạm: Thuận Phước · Gia công · Gửi kho)
        _lsxView = new ListSxView(openListLsx, OpenFromList);
        _autoView = new PnxAutoView(OpenFromList, lsx => { _outer!.SelectedIndex = 1; _lsxView.SelectLsx(lsx); });
        _autoView.UnseenChanged += n => _autoTab.Header = n > 0 ? $"⚡ Tự động đã tạo ({n} mới)" : "⚡ Tự động đã tạo";
        _autoTab.Content = _autoView;
        _outer = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Phiếu xuất khẩu (theo LSX)", Content = root },
                new TabItem { Header = "List sản xuất", Content = _lsxView },
                new TabItem { Header = "Phiếu xuất nội địa", Content = new CargoView() },
                _autoTab,
            },
            SelectedIndex = Math.Clamp(tab, 0, 3),
        };
        Content = _outer;
        ShowEditor();
        AttachedToVisualTree += async (_, _) => await LoadAsync();
    }

    // ------------------------------------------------------------------ chung

    private static Button Btn(string text, Func<Task> click, bool primary = false, bool danger = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (danger) b.Classes.Add("danger");
        b.Click += (_, _) => { _ = click(); };
        return b;
    }
    private static Border Framed(Control c) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = c };
    private static StackPanel Labeled(string label, Control c)
    {
        var s = new StackPanel { Spacing = 1 };
        s.Children.Add(new TextBlock { Text = label, Classes = { "muted" }, FontSize = 11.5 });
        s.Children.Add(c);
        return s;
    }
    private void Dirty() { if (_loading) return; _dirty = true; RefreshAll(); }

    private TextBox Tb(string value, Action<string> set, double w = 150, string? watermark = null)
    {
        var tb = new TextBox { Text = value, Width = w, FontSize = 12.5, IsReadOnly = !_edit, Watermark = watermark };
        var last = value;   // Avalonia bắn TextChanged trễ kể cả cho giá trị khởi tạo → chỉ tính khi chữ thật sự đổi
        tb.TextChanged += (_, _) => { var t = tb.Text ?? ""; if (_loading || t == last) return; last = t; set(t); Dirty(); };
        return tb;
    }
    private ComboBox Cb(IEnumerable<string> items, string value, Action<string> set, double w = 140)
    {
        var list = items.ToList();
        var cb = new ComboBox { ItemsSource = list, SelectedIndex = Math.Max(0, list.IndexOf(value)), Width = w, FontSize = 12.5, IsEnabled = _edit };
        var cur = value;
        cb.SelectionChanged += (_, _) => { if (_loading || cb.SelectedItem is not string s || s == cur) return; cur = s; set(s); Dirty(); };   // bỏ qua sự kiện lúc khởi tạo (không đổi giá trị)
        return cb;
    }

    private async Task LoadAsync()
    {
        _status.Text = "Đang tải…";
        try { await Task.Run(() => { Svc.Load(); try { Svc.LoadAutoLog(); } catch { } }); }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        _status.Text = "";
        RenderList();
        if (_openId is not null && _shown.FindIndex(p => p.Id == _openId) is var i && i >= 0) _list.SelectedIndex = i;
    }

    /// <summary>List sản xuất → phiếu vừa tạo / phiếu đã có: chuyển sang tab phiếu và mở phiếu đó.</summary>
    private async void OpenFromList(string phieuId)
    {
        await LoadAsync();
        _outer.SelectedIndex = 0;
        if (_shown.FindIndex(p => p.Id == phieuId) is var i && i >= 0) _list.SelectedIndex = i;
    }

    private void RenderList(string? keepId = null)
    {
        keepId ??= _cur?.Id;
        var q = StripLower(_search.Text ?? "");
        _shown = Svc.List.Where(p => p.Loai == "xuat_khau" && (q.Length == 0 || StripLower(p.SoPhieu + " " + p.Lsx + " " + p.Khach + " " + p.Ngay).Contains(q))).ToList();
        _loading = true;
        _list.ItemsSource = _shown.Select(p => $"{(PhieuKhoService.TagOf(p) is null ? "" : Svc.IsNew(p) ? "⚡ MỚI · " : "⚡ ")}{(p.SoPhieu.Length > 0 ? p.SoPhieu : "(chưa có số)")}  ·  {Dm(p.Ngay)}{(p.Lsx.Length > 0 ? "  ·  " + p.Lsx : "")}{(p.Khach.Length > 0 ? " " + p.Khach : "")}{(p.XuatLuc.Length > 0 ? "  ✓ đã xuất" : "")}").ToList();
        var i = keepId is null ? -1 : _shown.FindIndex(p => p.Id == keepId);
        _list.SelectedIndex = i;
        _loading = false;
    }
    private static string StripLower(string s) => KhoLanh.Core.ChungTu.ChungTuService.StripAccents(s);

    private async Task OnPick()
    {
        if (_loading) return;
        var i = _list.SelectedIndex;
        if (i < 0 || i >= _shown.Count) return;
        if (_dirty && _cur is not null && _cur.Id != _shown[i].Id && !await Ui.Confirm(this, "Phiếu đang soạn chưa lưu. Bỏ thay đổi và mở phiếu khác?", "Chưa lưu", "Bỏ thay đổi"))
        { RenderList(_cur.Id); return; }
        _cur = Clone(_shown[i]); _dirty = false;
        ShowEditor();
        if (PhieuKhoService.TagOf(_shown[i]) is { } tag)
        {
            var ng = tag.NgayLich.Length >= 10 ? $" từ lịch xuất ngày {tag.NgayLich[8..10]}/{tag.NgayLich[5..7]}/{tag.NgayLich[..4]}" : "";
            _status.Text = "⚡ Phiếu do hệ thống tự tạo" + ng + " — kiểm tra lại Tên hàng, Mã hàng, số kiện, Theo hợp đồng… rồi mới xuất Excel.";
            if (Svc.IsNew(_shown[i])) { var id = _shown[i].Id; _ = Task.Run(() => { try { Svc.MarkSeen('p', id); } catch { } }); }
        }
    }

    private async Task NewPhieu()
    {
        if (_dirty && !await Ui.Confirm(this, "Phiếu đang soạn chưa lưu. Bỏ thay đổi và tạo phiếu mới?", "Chưa lưu", "Bỏ thay đổi")) return;
        _cur = Svc.NewPhieu(); _dirty = true;
        _loading = true; _list.SelectedIndex = -1; _loading = false;
        ShowEditor();
    }

    // ------------------------------------------------------------------ form soạn phiếu

    private void ShowEditor()
    {
        _editor.Children.Clear(); _blocks.Clear(); _soBox = null; _o40Box = null;
        _del.IsEnabled = _edit && _cur is { Id.Length: > 0 };
        if (_cur is null)
        {
            _editor.Children.Add(new TextBlock { Text = _edit ? "Chọn một phiếu ở danh sách bên trái, hoặc bấm “Phiếu xuất khẩu mới”." : "Chọn một phiếu ở danh sách bên trái.", Classes = { "muted" } });
            RefreshAll(); return;
        }
        _loading = true;
        var p = _cur;
        // --- thông tin chung
        var g1 = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        var date = new CalendarDatePicker { Width = 140, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy", IsEnabled = _edit };
        if (DateTime.TryParse(p.Ngay, out var dt)) date.SelectedDate = dt;
        date.SelectedDateChanged += (_, _) => { if (_loading || date.SelectedDate is not { } d || d.ToString("yyyy-MM-dd") == p.Ngay) return; p.Ngay = d.ToString("yyyy-MM-dd"); AutoNumber(); Dirty(); };
        g1.Children.Add(Labeled("Ngày xuất", date));
        _soBox = Tb(p.SoPhieu, v => p.SoPhieu = v, 150);
        var so = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom };
        so.Children.Add(_soBox);
        if (_edit) { var re = new Button { Content = "↻", Padding = new Thickness(8, 3) }; ToolTip.SetTip(re, "Cấp lại số phiếu tự động theo tháng / hàng"); re.Click += (_, _) => { p.SoPhieuTuCap = ""; p.SoPhieu = NextNumbers(Svc.List, p).SoPhieu; _soBox.Text = p.SoPhieu; Dirty(); }; so.Children.Add(re); }
        g1.Children.Add(Labeled("Số phiếu", so));
        g1.Children.Add(Labeled("Kho xuất", Cb(new[] { "An An", "Thuận Phước" }, p.Kho, v => p.Kho = v, 130)));
        g1.Children.Add(Labeled("LSX (nhiều LSX: cách bằng dấu +)", Tb(p.Lsx, v => { p.Lsx = v; }, 230)));
        g1.Children.Add(Labeled("Khách hàng", Tb(p.Khach, v => p.Khach = v, 170)));
        g1.Children.Add(Labeled("Theo hợp đồng số", Tb(p.HopDong, v => p.HopDong = v, 170)));
        g1.Children.Add(Labeled("Kiểu xuất", Tb(p.KieuXuat, v => p.KieuXuat = v, 160)));
        g1.Children.Add(Labeled("Loại cont", Cb(new[] { "40RF", "20RF", "Xe" }, p.LoaiCont, v => p.LoaiCont = v, 90)));
        _o40Box = Tb(p.O40, v => p.O40 = v, 120);
        g1.Children.Add(Labeled("Mã sau nhãn 40RF", _o40Box));
        g1.Children.Add(Labeled("Đơn vị", Cb(new[] { "kiện", "bdl" }, p.DonVi, v => p.DonVi = v, 80)));
        _editor.Children.Add(Section("Thông tin phiếu", g1));
        var ly = new WrapPanel { ItemSpacing = 8 };
        var lyBox = Tb(LyDoOf(p), v => { if (p.LyDoTay) p.LyDo = v; }, 520);
        lyBox.IsEnabled = _edit && p.LyDoTay;
        var lyTay = new CheckBox { Content = "Gõ tay lý do", IsChecked = p.LyDoTay, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, IsEnabled = _edit };
        lyTay.IsCheckedChanged += (_, _) => { if (_loading) return; p.LyDoTay = lyTay.IsChecked == true; if (p.LyDoTay && p.LyDo.Length == 0) p.LyDo = LyDoAuto(p); lyBox.IsEnabled = _edit && p.LyDoTay; _loading = true; lyBox.Text = LyDoOf(p); _loading = false; Dirty(); };
        ly.Children.Add(Labeled("Lý do xuất (tự ghép từ LSX + khách; tích “Gõ tay” để sửa)", lyBox)); ly.Children.Add(lyTay);
        _editor.Children.Add(ly);
        // --- thông tin xe
        var gx = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        foreach (var (k, _, _, lab) in XeFields) gx.Children.Add(Labeled(lab, Tb(p.Xe.Get(k), v => p.Xe.Set(k, v), k is "taiXe" ? 170 : 140)));
        _editor.Children.Add(Section("Thông tin xe (có thể dán nguyên email ở nút “Dán email thông tin xe”)", gx));
        // --- mặt hàng
        for (var i = 0; i < p.Mh.Count; i++) _editor.Children.Add(BuildBlock(p, i));
        if (_edit && p.Mh.Count < XuatKhauLayout.NB)
        {
            var add = Btn("➕ Thêm mặt hàng", async () => { p.Mh.Add(new MatHang()); _dirty = true; ShowEditor(); await Task.CompletedTask; });
            _editor.Children.Add(add);
        }
        // --- cuối phiếu
        var gc = p.Gc;
        var g3 = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        g3.Children.Add(Labeled("Kiểm tra hình ảnh", Tb(gc.HinhAnh, v => gc.HinhAnh = v, 330)));
        g3.Children.Add(Labeled("Nhiệt độ bắt đầu xuất", Tb(gc.NdBatDau, v => gc.NdBatDau = v, 110)));
        g3.Children.Add(Labeled("Nhiệt độ khi rời nhà máy", Tb(gc.NdRoi, v => gc.NdRoi = v, 110)));
        g3.Children.Add(Labeled("Đóng sole", Cb(new[] { "", "co", "khong" }, gc.Sole, v => gc.Sole = v, 90)));
        g3.Children.Add(Labeled("Nhiệt kế", Cb(new[] { "", "co", "khong" }, gc.NhietKe, v => gc.NhietKe = v, 90)));
        g3.Children.Add(Labeled("Mã số nhiệt kế", Tb(gc.MaNhietKe, v => gc.MaNhietKe = v, 120)));
        g3.Children.Add(Labeled("Cont đến", Tb(gc.ContDen, v => gc.ContDen = v, 120)));
        g3.Children.Add(Labeled("Cont đi", Tb(gc.ContDi, v => gc.ContDi = v, 120)));
        g3.Children.Add(Labeled("Cửa XH", Tb(gc.CuaXH, v => gc.CuaXH = v, 110)));
        g3.Children.Add(Labeled("Người báo", Tb(gc.NguoiBao, v => gc.NguoiBao = v, 140)));
        _editor.Children.Add(Section("Cuối phiếu (nhiệt độ, sole, nhiệt kế, người báo)", g3));
        _loading = false;
        RefreshAll();
    }

    private static Border Section(string title, Control body)
    {
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = Dark });
        s.Children.Add(body);
        return new Border { Background = CardBg, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = s };
    }

    private Control BuildBlock(Phieu p, int bi)
    {
        var m = p.Mh[bi];
        var head = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        head.Children.Add(Labeled("Tên hàng", Tb(m.Ten, v => m.Ten = v, 300)));
        head.Children.Add(Labeled("Mã hàng (1 mã cho cả mặt hàng)", Tb(m.Ma, v => m.Ma = v, 220)));
        head.Children.Add(Labeled("Mã số lô", Tb(m.Lo, v => m.Lo = v, 120)));
        head.Children.Add(Labeled("LSX của mặt hàng", Tb(m.Lsx, v => m.Lsx = v, 120)));
        head.Children.Add(Labeled("Sản xuất", Cb(new[] { "Tự theo mã", "AA", "TP" }, m.Sx is "AA" or "TP" ? m.Sx : "Tự theo mã", v => m.Sx = v == "Tự theo mã" ? "" : v, 120)));
        if (_edit)
        {
            var rm = new Button { Content = "🗑 Xóa mặt hàng", Padding = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Bottom }; rm.Classes.Add("danger");
            rm.Click += (_, _) => { p.Mh.Remove(m); if (p.Mh.Count == 0) p.Mh.Add(new MatHang()); _dirty = true; ShowEditor(); };
            head.Children.Add(rm);
        }

        var gm = new GridModel { ReadOnly = !_edit };
        gm.Load(new[]
        {
            new GridColumn("date", "DATE (dd.mm.yyyy)", 118), new GridColumn("size", "Size", 70), new GridColumn("ma", "Mã riêng (nếu nhiều mã)", 120),
            new GridColumn("packing", "Packing (1.0 kg x 5 x 2)", 150), new GridColumn("kien", "Số kiện", 75, "number"), new GridColumn("mks", "MKS (x / số)", 90),
            new GridColumn("nettay", "NET sửa tay", 85, "number"), new GridColumn("net", "NET / kiện", 85, "number") { ReadOnly = true }, new GridColumn("kg", "Thành kg", 130, "number") { ReadOnly = true },
        }, m.DongList.Select(d => new[] { d.Date, d.Size, d.Ma, d.Packing, d.Kien, d.Mks, d.NetTay, "", "" }));
        var grid = new SheetGrid(gm) { SpareRows = 2 };
        grid.Display = (r, c, raw) =>
        {
            if (c is not (7 or 8)) return raw;
            var d = RowOf(gm, r);
            if (!UsedRow(d)) return "";
            var n = RowNet(d);
            if (n is null) return c == 7 ? "?" : "";
            return c == 7 ? Fmt(n.Value) : Fmt(n.Value * N0(d.Kien));
        };
        void FixHeight() => grid.Height = Math.Min(520, 24 * (Math.Max(gm.RowCount, 1) + grid.SpareRows) + 34);
        FixHeight();
        gm.Changed += () =>
        {
            if (_loading) return;
            SyncRows(m, gm); FixHeight(); Dirty();
        };
        _blocks.Add((m, gm));
        var total = new TextBlock { FontSize = 12, Classes = { "muted" } };
        total.Text = "Mã hàng: nhập 1 mã ở trên cho cả mặt hàng, hoặc điền “Mã riêng” từng dòng khi mặt hàng có nhiều mã. Dán nhiều dòng từ Excel được (Ctrl+V).";
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(head); body.Children.Add(Framed(grid)); body.Children.Add(total);
        return Section($"Mặt hàng {bi + 1}", body);
    }

    private static string Fmt(double d) => d.ToString("#,0.###", Ui.Vi);
    private static double N0(string? s) => Num(s) is { } d && !double.IsNaN(d) ? d : 0;
    private static Dong RowOf(GridModel gm, int r) => new()
    {
        Date = gm.Get(r, 0), Size = gm.Get(r, 1), Ma = gm.Get(r, 2), Packing = gm.Get(r, 3), Kien = gm.Get(r, 4), Mks = gm.Get(r, 5), NetTay = gm.Get(r, 6),
    };
    /// <summary>Chép lưới vào mặt hàng; giữ NET từ tồn kho (netTon / netPk) và trường lạ của dòng cũ theo vị trí dòng.</summary>
    private static void SyncRows(MatHang m, GridModel gm)
    {
        var old = m.DongList; var res = new List<Dong>();
        for (var r = 0; r < gm.RowCount; r++)
        {
            var d = RowOf(gm, r);
            if (!UsedRow(d) && d.Ma.Trim().Length == 0 && d.NetTay.Trim().Length == 0) continue;
            if (r < old.Count) { d.NetTon = old[r].NetTon; d.NetPk = old[r].NetPk; d.NoSize = old[r].NoSize; d.Extra = old[r].Extra; }
            res.Add(d);
        }
        m.DongList = res.Count > 0 ? res : new List<Dong> { new() };
    }

    // ------------------------------------------------------------------ cập nhật số / email

    private void AutoNumber()
    {
        if (_cur is null || _soBox is null) return;
        if (RefreshNumbers(Svc.List, _cur)) { _loading = true; _soBox.Text = _cur.SoPhieu; if (_o40Box is not null) _o40Box.Text = _cur.O40; _loading = false; }
    }

    private void RefreshAll()
    {
        if (_cur is null) { _totals.Text = ""; _err.IsVisible = false; _mail.Text = ""; _save.IsEnabled = false; _exp.IsEnabled = false; _copy.IsEnabled = false; return; }
        _save.IsEnabled = _edit; _exp.IsEnabled = _export; _copy.IsEnabled = true;
        var t = TotalsOf(_cur); var unit = _cur.DonVi == "bdl" ? "bdl" : "kiện";
        _totals.Text = $"Tổng: {t.Kien:#,0.##} {unit} (An An {t.Aa:#,0.##} · Thuận Phước {t.Tp:#,0.##}) · {t.Kg:#,0.###} kg" + (t.Kien > 0 ? "  —  " + DocSo(t.Kien) + " " + unit : "");
        var errs = Validate(Clone(_cur));
        _err.IsVisible = errs.Count > 0;
        _err.Text = errs.Count == 0 ? "" : "Cần sửa trước khi xuất file: " + string.Join(" ", errs.Take(3)) + (errs.Count > 3 ? $" (+{errs.Count - 3} lỗi nữa)" : "");
        _status.Text = _dirty ? "● Có thay đổi chưa lưu" : (_cur.Id.Length > 0 ? "Đã lưu" : "");
        RefreshMail();
    }

    private void RefreshMail() { if (_tabs?.SelectedIndex == 1 && _cur is not null) _mail.Text = EmailText(_cur); }

    // ------------------------------------------------------------------ thao tác

    private async Task DoSave()
    {
        if (_cur is null) return;
        try
        {
            if (_cur.SoPhieu.Trim().Length == 0) { await Ui.Message(this, "Chưa nhập Số phiếu."); return; }
            var saved = await Task.Run(() => Svc.Save(_cur));
            _cur = Clone(saved); _dirty = false;
            RenderList(saved.Id);
            ShowEditor();
            _status.Text = "Đã lưu phiếu " + saved.SoPhieu + " (" + DateTime.Now.ToString("HH:mm:ss") + ")";
        }
        catch (SheetConflictException)
        {
            await Ui.Message(this, "Dữ liệu vừa được máy khác sửa nên phiếu CHƯA được lưu (để không ghi đè). Bấm Đóng để tải bản mới nhất rồi lưu lại.", "Máy khác vừa sửa");
            await LoadAsync();
        }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); }
    }

    private async Task DoDelete()
    {
        if (_cur is null || _cur.Id.Length == 0) return;
        if (!await Ui.Confirm(this, $"Xóa phiếu {_cur.SoPhieu}? Không khôi phục được.", "Xóa phiếu", "Xóa")) return;
        var id = _cur.Id;
        if (await Ui.Run(this, () => Svc.Delete(id))) { _cur = null; _dirty = false; RenderList(""); ShowEditor(); _status.Text = "Đã xóa phiếu"; }
    }

    private async Task DoExport()
    {
        if (_cur is null) return;
        var errs = Validate(Clone(_cur));
        if (errs.Count > 0) { await Ui.Message(this, string.Join("\n", errs.Take(6)) + (errs.Count > 6 ? $"\n(+{errs.Count - 6} lỗi nữa)" : ""), "Phiếu chưa đủ để xuất"); return; }
        if (_cur.Xe.Cont.Trim().Length == 0 && !await Ui.Confirm(this, "Phiếu chưa có SỐ CONT.\n\nXuất file vẫn được; bấm “Vẫn xuất” để tiếp tục, “Thôi” để quay lại nhập.", "Thiếu số cont", "Vẫn xuất")) return;
        var cur = Clone(_cur);
        var path = await Ui.SaveFile(this, "Xuất Phiếu xuất kho", FileName(cur), "xlsm", "Excel có macro (.xlsm)");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.Export(cur, path)))
        {
            _status.Text = "Đã xuất " + Path.GetFileName(path);
            if (cur.Id.Length > 0) RenderList(cur.Id);
            Ui.OpenExternal(path);
        }
    }

    // ------------------------------------------------------------------ List sản xuất (PDF / Excel / kho theo LSX)

    private async Task ListDialog()
    {
        if (_cur is null || !_edit) return;
        var p = _cur;
        ListData? data = null; var fileName = "";
        var info = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, Foreground = Dark };
        var warn = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Warn, MaxHeight = 130 };
        var gm = new GridModel();
        var grid = new SheetGrid(gm) { SpareRows = 0, Height = 230 };
        var chkAppend = new CheckBox { Content = "THÊM vào phiếu (giữ mặt hàng của LSX khác — cont ghép nhiều LSX); bỏ tích = thay toàn bộ mặt hàng", FontSize = 12.5, IsChecked = p.Mh.Any(UsedBlock) };
        var chkSave = new CheckBox { Content = "Lưu List vào kho theo số LSX (lần sau gõ LSX là điền lại, khỏi nạp PDF)", FontSize = 12.5, IsChecked = true };
        var go = new Button { Content = "Đưa vào phiếu", Classes = { "primary" }, IsEnabled = false };
        var close = new Button { Content = "Đóng", IsCancel = true };
        var savedBox = new ComboBox { Width = 380, FontSize = 12.5, ItemsSource = Svc.Lists.Select(x => $"LSX {x.Lsx} · {x.Khach} · {x.Data.Rows.Count} ngày × {x.Data.Sizes.Count} size").ToList(), PlaceholderText = Svc.Lists.Count == 0 ? "(kho List đang trống)" : "Dùng List đã lưu…" };

        void Show(ListData d, string src, ListData? prefill)
        {
            data = d; fileName = src;
            if (prefill is not null)   // giữ Tên hàng / Mã hàng / Mã lô đã gán ở bản lưu cũ cho size trùng tên
                foreach (var z in d.Sizes) if (prefill.Sizes.FirstOrDefault(o => o.Size == z.Size) is { } o) { if (z.Ten.Length == 0) z.Ten = o.Ten; if (z.Ma.Length == 0) z.Ma = o.Ma; if (z.Lo.Length == 0) z.Lo = o.Lo; }
            info.Text = d.Ok
                ? $"LSX {(d.Lsx.Length > 0 ? d.Lsx : "(không thấy)")} · khách {(d.Khach.Length > 0 ? d.Khach : "(không thấy)")} · {d.Rows.Count} ngày × {d.Sizes.Count} size · tổng {d.Rows.Sum(r => r.Qty.Values.Sum()):#,0.##} {(d.DonVi.Length > 0 ? d.DonVi : "kiện")}" + (d.Layout == "product" ? " · bố cục sản phẩm theo cột" : " · bố cục bảng ngày × size")
                : "Chưa đọc được List.";
            warn.Text = d.Warnings.Count > 0 ? string.Join("\n", d.Warnings.Select(w => "⚠ " + w)) : "";
            gm.Load(new[]
            {
                new GridColumn("size", "Size", 90) { ReadOnly = true }, new GridColumn("ten", "Tên hàng (gom các size cùng tên thành 1 mặt hàng)", 290), new GridColumn("ma", "Mã hàng", 190), new GridColumn("lo", "Mã số lô", 100),
                new GridColumn("kien", "Tổng kiện", 80, "number") { ReadOnly = true }, new GridColumn("pack", "Packing (từ List)", 130) { ReadOnly = true }, new GridColumn("code", "Code mã hoá của khách", 170) { ReadOnly = true },
            }, d.Sizes.Select(z => new[] { z.Size, z.Ten, z.Ma, z.Lo, d.Rows.Sum(r => r.Qty.GetValueOrDefault(z.Size)).ToString("0.###", Ui.Vi), d.PackingBy.GetValueOrDefault(z.Size) ?? d.Packing, z.CodeMaHoa }));
            grid.Refresh();
            go.IsEnabled = d.Ok;
        }
        async Task PickFile()
        {
            var top = TopLevel.GetTopLevel(this); if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Chọn List sản xuất của khách (PDF hoặc Excel)", AllowMultiple = false,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("List sản xuất (PDF, Excel)") { Patterns = new[] { "*.pdf", "*.xlsx", "*.xlsm" } } },
            });
            if (files.Count == 0 || Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]) is not { } path) return;
            info.Text = "Đang đọc…";
            ListData? d = null;
            try { d = await Task.Run(() => Svc.ReadList(path)); }
            catch (BusinessException e) { info.Text = ""; warn.Text = e.Message; return; }
            catch (Exception e) { info.Text = ""; warn.Text = Ui.Describe(e); return; }
            Show(d, Path.GetFileName(path), Svc.FindList(d.Lsx)?.Data);
        }
        savedBox.SelectionChanged += (_, _) =>
        {
            if (savedBox.SelectedIndex < 0 || savedBox.SelectedIndex >= Svc.Lists.Count) return;
            var rec = Svc.Lists[savedBox.SelectedIndex];
            Show(ListSx.FromJson(ListSx.ToJson(rec.Data))!, rec.File, null);
            chkSave.IsChecked = false;
        };
        var pick = new Button { Content = "📂 Chọn file PDF / Excel…", Classes = { "primary" } }; pick.Click += async (_, _) => await PickFile();
        var top1 = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 }; top1.Children.Add(pick); top1.Children.Add(new TextBlock { Text = "hoặc", VerticalAlignment = VerticalAlignment.Center }); top1.Children.Add(savedBox);
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { Text = "Đọc List sản xuất của khách → các dòng DATE × size × số kiện của phiếu. Cột size được nhận theo vị trí trong file; điều gì không chắc thì báo cảnh báo, không đoán. Điền Tên hàng / Mã hàng / Mã lô cho từng size (Packing tự lấy từ List hoặc từ mã hàng 21 ký tự).", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        body.Children.Add(top1); body.Children.Add(info); body.Children.Add(warn); body.Children.Add(Framed(grid));
        body.Children.Add(chkAppend); body.Children.Add(chkSave); body.Children.Add(Ui.Buttons(close, go));
        var w = Ui.Dialog("Nạp List sản xuất vào phiếu", body, 980);
        var doIt = false;
        go.Click += (_, _) => { doIt = true; w.Close(); };
        close.Click += (_, _) => w.Close();
        if (Svc.FindList(p.Lsx) is { } pre) { var ix = Svc.Lists.IndexOf(pre); savedBox.SelectedIndex = ix; }
        await Ui.Show(this, w);
        if (!doIt || data is null) return;
        for (var i = 0; i < data.Sizes.Count; i++) { data.Sizes[i].Ten = gm.Get(i, 1).Trim(); data.Sizes[i].Ma = gm.Get(i, 2).Trim(); data.Sizes[i].Lo = gm.Get(i, 3).Trim(); }
        var d2 = data; var app = chkAppend.IsChecked == true; var save = chkSave.IsChecked == true; var fn = fileName;
        ListSxApply.ApplyResult res = new(false, new());
        try
        {
            res = await Task.Run(() => Svc.ApplyList(p, d2, app));
            if (res.Ok && save)
            {
                try { await Task.Run(() => Svc.SaveListData(d2, fn)); }
                catch (BusinessException e) { res.Notes.Add("⚠ Chưa lưu List vào kho: " + e.Message); }
            }
        }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Không thực hiện được"); return; }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); return; }
        if (!res.Ok) { await Ui.Message(this, string.Join("\n", res.Notes), "Chưa đưa được List vào phiếu"); return; }
        RefreshNumbers(Svc.List, p);
        _dirty = true; ShowEditor();
        _status.Text = $"Đã đưa List vào phiếu: {res.SoMatHang} mặt hàng, {res.SoDong} dòng DATE, {res.TongKien:#,0.##} {(p.DonVi == "bdl" ? "bdl" : "kiện")}.";
        if (res.Notes.Count > 0) await Ui.Message(this, string.Join("\n\n", res.Notes), "Lưu ý khi nạp List");
    }

    private async Task DoThongKe()
    {
        var def = _cur is { Ngay.Length: >= 7 } ? _cur.Ngay[..7] : DateTime.Today.ToString("yyyy-MM");
        var ans = await Ui.Prompt(this, "Tháng cần thống kê (yyyy-mm). Tính theo ngày xuất của các phiếu xuất khẩu ĐÃ LƯU:", "Thống kê xuất khẩu theo tháng", def);
        if (ans is null) return;
        var mk = ans.Trim();
        var path = await Ui.SaveFile(this, "Xuất thống kê tháng", ThongKeThang.FileName(System.Text.RegularExpressions.Regex.IsMatch(mk, @"^\d{4}-\d{2}$") ? mk : def), "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.ExportThongKe(mk, path)))
        {
            var d = Svc.ThongKe(mk);
            _status.Text = $"Đã xuất {Path.GetFileName(path)} — {d.Groups.Count} phiếu, {d.TongThang:#,0.##} kiện.";
            Ui.OpenExternal(path);
        }
    }

    private async Task CopyMail()
    {
        if (_cur is null) return;
        var text = EmailText(_cur);
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { await cb.SetTextAsync(text); _status.Text = "Đã copy nội dung email — dán vào thư (Ctrl+V)."; }
    }

    private async Task PasteXe()
    {
        if (_cur is null) return;
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 240, Watermark = "Dán email có các dòng: Họ tên tài xế: … / Số điện thoại: … / Số xe: … / Số cont: … / Số seal: … / Số booking: … / Nhiệt độ cài đặt cont: …" };
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { try { var t = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(cb); if (!string.IsNullOrWhiteSpace(t) && t.Contains(':')) box.Text = t; } catch { } }
        var msg = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        var ok = new Button { Content = "Nhận thông tin xe", Classes = { "primary" }, IsDefault = false };
        var close = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "Dán nội dung email “Thông tin xe” — app tách 7 mục (tài xế, SĐT, số xe, cont, seal, booking, nhiệt độ). Mục nào không thấy nhãn thì báo THIẾU (không đoán).", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        body.Children.Add(box); body.Children.Add(msg); body.Children.Add(Ui.Buttons(close, ok));
        var w = Ui.Dialog("Dán email thông tin xe", body, 640);
        var applied = false;
        ok.Click += (_, _) =>
        {
            var r = ApplyXeEmail(_cur, box.Text);
            msg.Text = r.Found.Count > 0 ? $"Đã nhận {r.Found.Count}/7 mục" + (r.Missing.Count > 0 ? " — thiếu: " + string.Join(", ", r.Missing) : " ✓") : "Không thấy mục nào — kiểm tra nội dung email.";
            if (r.Found.Count > 0) applied = true;
        };
        close.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (applied) { _dirty = true; ShowEditor(); }
    }
}

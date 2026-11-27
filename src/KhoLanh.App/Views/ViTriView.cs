using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.ViTri;
using static KhoLanh.Core.ViTri.ViTriService;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Tồn kho theo vị trí (C#, nghiệp vụ theo "Tồn Vị Trí An An" của app cũ — không dùng WebView2; dùng chung dữ liệu với app cũ).
/// Giao diện mới: lưới tồn có cột "SL xuất" gõ / dán thẳng ("x" = lấy hết), khung "Phiếu đang soạn" bên phải luôn thấy trước khi lưu.
/// </summary>
public sealed class ViTriView : UserControl, IPage
{
    public string Title => "Tồn kho theo vị trí";
    public string Subtitle => "Lập phiếu xuất: chọn hàng từ tồn theo ô vị trí, sửa SL ngay trên phiếu (trừ tồn khi lưu), gộp theo Size, lưu trữ theo ngày — dùng chung dữ liệu với app cũ.";

    private static ViTriService Svc => App.Services.ViTri;
    private static readonly IBrush PickBg = AppTheme.Brush("#FFF1C9");
    private static readonly IBrush OverBg = AppTheme.Brush("#F8CBAD");
    private static readonly IBrush ZeroBg = AppTheme.Brush("#EDEDED");
    private static readonly IBrush DoneBg = AppTheme.Brush("#E2EFDA");
    private static readonly IBrush LockBg = AppTheme.Brush("#EDEDED");
    private static readonly IBrush OffBg = AppTheme.Brush("#FBE3D6");
    private static readonly IBrush KhacBg = AppTheme.Brush("#F8CBAD");
    private static readonly IBrush ChuaBg = AppTheme.Brush("#FFE699");
    private static readonly IBrush KhopBg = AppTheme.Brush("#E2EFDA");
    private static readonly IBrush Line = AppTheme.Brush("#D0D7E2");

    private readonly bool _edit, _export;
    private bool _loading;
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _fileInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TabControl _tabs;

    // ---------- tab Tồn theo vị trí
    private readonly TextBox _q = new() { Watermark = "Tìm: BTP 50 MKS Nau, mã, vị trí, lô… (gõ “BTP 50” chỉ ra đúng size BTP 50)", Width = 330 };
    private readonly ComboBox _zone = new() { MinWidth = 110 };
    private readonly CheckBox _onlyStock = new() { Content = "Chỉ ô còn tồn", IsChecked = true, FontSize = 12.5 };
    private readonly CheckBox _onlyPick = new() { Content = "Chỉ dòng đang xuất", FontSize = 12.5 };
    private readonly CheckBox _onlyPos = new()
    {
        Content = "Lọc vị trí đang chọn", FontSize = 12.5,
        [ToolTip.TipProperty] = "Hiện mọi mã hàng còn tồn ở các vị trí đã có dòng đang xuất (kể cả mã chưa chọn) — bỏ qua ô tìm và Khu.",
    };
    private readonly TextBlock _gridInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly GridModel _gm = new();
    private readonly SheetGrid _grid;
    private List<int> _rowIdx = new();          // dòng lưới → chỉ số dòng tồn
    private List<string[]> _snap = new();
    private Dictionary<int, double> _done = new();
    private const int ColX = 8;                 // cột SL xuất
    private static readonly GridColumn[] StockCols =
    {
        // 1.95: thêm Tên hàng (sau Mã hàng) + Trọng lượng theo mã hàng (sau Size)
        new("vt", "Vị trí", 90) { ReadOnly = true }, new("ma", "Mã hàng", 190) { ReadOnly = true }, new("ten", "Tên hàng", 230) { ReadOnly = true }, new("size", "Size", 60) { ReadOnly = true },
        new("tl", "Trọng lượng", 80, "number") { ReadOnly = true }, new("dt", "Đặc tính", 110) { ReadOnly = true }, new("hd", "Hợp đồng", 100) { ReadOnly = true },
        new("sl", "SL tồn", 70, "number") { ReadOnly = true }, new("x", "SL xuất ✎", 80, "number"), new("done", "Đã làm phiếu", 85, "number") { ReadOnly = true },
        new("net", "Net tồn (kg)", 90, "number") { ReadOnly = true },
        new("lo", "Lô", 90) { ReadOnly = true },   // 1.92: Lô xuống cột cuối
    };

    // ---------- phiếu đang soạn
    private readonly Dictionary<int, double> _draft = new();
    private EditState? _editing;
    private readonly TextBlock _draftHead = new() { FontSize = 15, FontWeight = FontWeight.SemiBold };
    private readonly ComboBox _loai = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CalendarDatePicker _ngay = new() { HorizontalAlignment = HorizontalAlignment.Stretch, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy" };
    private readonly TextBox _title = new() { Watermark = TitleDefault };
    private readonly CheckBox _gop = new() { Content = "Gộp theo Size (cùng vị trí + cùng size → 1 dòng)", FontSize = 12.5 };
    private readonly GridModel _pvm = new() { ReadOnly = true };
    private readonly SheetGrid _pv;
    private readonly TextBlock _draftSum = new() { FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _draftWarn = new() { FontSize = 12, Foreground = AppTheme.Brush("#B23B00"), TextWrapping = TextWrapping.Wrap };
    private readonly Button _save, _cancelEdit;
    // 1.54: bảng phiếu đang soạn (mặc định của tab) + cửa sổ "Chọn hàng từ tồn" chứa bảng tồn
    private readonly GridModel _dm = new();
    private readonly SheetGrid _dg;
    private List<int> _dRows = new();           // dòng bảng phiếu → chỉ số dòng tồn
    private List<string[]> _dSnap = new();
    private bool _dLoading;
    private Control? _stockPanel;
    private const int DColX = 8;                // cột SL xuất của bảng phiếu
    private static readonly GridColumn[] DraftCols =
    {
        new("vt", "Vị trí", 90) { ReadOnly = true }, new("ma", "Mã hàng", 200) { ReadOnly = true }, new("ten", "Tên hàng", 230) { ReadOnly = true }, new("size", "Size", 60) { ReadOnly = true },
        new("tl", "Trọng lượng", 80, "number") { ReadOnly = true }, new("dt", "Đặc tính", 110) { ReadOnly = true }, new("hd", "Hợp đồng", 110) { ReadOnly = true },
        new("sl", "SL tồn", 70, "number") { ReadOnly = true }, new("x", "SL xuất ✎", 85, "number"),
        new("net", "Net xuất (kg)", 95, "number") { ReadOnly = true },
        new("lo", "Lô", 90) { ReadOnly = true },
    };

    // ---------- tab Phiếu xuất
    private readonly GridModel _sm = new() { ReadOnly = true };
    private readonly SheetGrid _sg;
    private List<Slip> _slipRows = new();
    private readonly GridModel _sdm = new() { ReadOnly = true };
    private readonly SheetGrid _sd;
    private readonly TextBlock _slipInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };

    // ---------- tab Lưu trữ theo ngày
    private readonly GridModel _am = new() { ReadOnly = true };
    private readonly SheetGrid _ag;
    private List<ArcGroup> _arcRows = new();
    private readonly GridModel _adm = new() { ReadOnly = true };
    private readonly TextBlock _arcInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };

    // ---------- tab Thống kê Size
    private readonly ComboBox _range = new() { MinWidth = 140 };
    private readonly GridModel _stm = new() { ReadOnly = true };
    private readonly TextBlock _statInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private static readonly (string Key, string Text)[] Ranges = { ("today", "Hôm nay"), ("week", "7 ngày qua"), ("month", "Tháng này"), ("all", "Tất cả") };

    // ---------- tab Nạp file
    private readonly StackPanel _filePanel = new() { Spacing = 10, Margin = new Thickness(0, 8, 0, 8) };

    public ViTriView()
    {
        _edit = Svc.CanEdit; _export = Svc.CanExport;
        _grid = new SheetGrid(_gm) { SpareRows = 0, CellBackground = StockBg, CheckCol = ColX };
        _grid.CheckFill = r =>   // 1.50: tích ô đầu dòng = lấy hết phần chưa làm phiếu (như gõ "x")
        {
            if (!Ui.TryParseNumber(_gm.Get(r, ColX - 1), out var sl)) return "x";
            var done = Ui.TryParseNumber(_gm.Get(r, ColX + 1), out var d) ? d : 0;
            var left = sl - done;
            return left > 0 ? left.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "x";
        };
        _gm.Changed += () => { if (!_loading) OnGridChanged(); };
        _pv = new SheetGrid(_pvm) { SpareRows = 0 };
        _dg = new SheetGrid(_dm) { SpareRows = 0, CellBackground = DraftBg, EmptyText = "Phiếu chưa có dòng nào — bấm “Chọn hàng từ tồn…” để chọn mã hàng cần xuất.", LargeTitle = "Phiếu đang soạn" };
        _dm.Changed += () => { if (!_dLoading) OnDraftChanged(); };
        _sg = new SheetGrid(_sm) { SpareRows = 0, CellBackground = SlipBg };
        _sg.SelectionChanged += ShowSlipDetail;
        _sd = new SheetGrid(_sdm) { SpareRows = 0 };
        _ag = new SheetGrid(_am) { SpareRows = 0 };
        _ag.SelectionChanged += ShowArcDetail;
        _save = Btn("💾 Lưu phiếu (trừ tồn)", async () => await SaveDraft(), primary: true);
        _cancelEdit = Btn("Hủy sửa", CancelEdit);

        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = _edit ? "Lập phiếu xuất" : "Tồn theo vị trí", Content = BuildStock() },
                new TabItem { Header = "Phiếu xuất", Content = BuildSlips() },
                new TabItem { Header = "Lưu trữ theo ngày", Content = BuildArchive() },
                new TabItem { Header = "Thống kê Size", Content = BuildStats() },
                new TabItem { Header = "Nạp file", Content = new ScrollViewer { Content = _filePanel } },
                new TabItem { Header = "Tra cứu đã xuất", Content = BuildDaXuat() },   // 1.132: thêm cuối — giữ số thứ tự tab cũ
            },
        };
        _tabs.SelectionChanged += (_, _) => { if (!_loading && Svc.Loaded) RenderTab(); };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(_status, Dock.Right);
        top.Children.Add(_status);
        top.Children.Add(_fileInfo);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(_tabs);
        Content = root;
        // 1.135 (hiệu năng): quay lại trang đã giữ sẵn mà dữ liệu chưa đổi → giữ nguyên (cả phiếu đang soạn), không đọc lại toàn bộ
        // (đo: mở lại 0,9 s — chậm hơn lần đầu — và trước đây còn xóa phiếu đang soạn dở mỗi lần quay lại)
        AttachedToVisualTree += (_, _) => { var keep = Ui.SkipReload(this); if (_loadedSeq < 0 || (!keep && _loadedSeq != DataSeq())) _ = Safe(LoadAsync); };   // 1.136: + dữ liệu trang đọc chưa đổi (MainWindow) → giữ
    }

    // =====================================================================================
    // Chung
    // =====================================================================================

    /// <summary>1.90: nút chạy việc async — lỗi luôn được bắt, ghi logs/loi.log và báo người dùng (không thành lỗi chưa bắt / tác vụ nền).</summary>
    private Button Btn(string text, Func<Task> click, bool primary = false, bool danger = false, string? tip = null) =>
        Btn(text, (Action)(() => { _ = Safe(click); }), primary, danger, tip);
    private async Task Safe(Func<Task> work)
    {
        try { await work(); }
        catch (Exception e)
        {
            try { if (App.DataFolder.Length > 0) KhoLanh.Core.TroLy.TroLyService.LogUnhandled(App.DataFolder, e, "Tồn theo vị trí"); } catch { }
            try { await Ui.Message(this, Ui.Describe(e), "Lỗi"); } catch { }
        }
    }
    private static Button Btn(string text, Action click, bool primary = false, bool danger = false, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (danger) b.Classes.Add("danger");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
    private static TextBlock Label(string t, bool bold = false, double size = 12.5) => new() { Text = t, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private static string Q(double v) => v.ToString("#,0.###", Ui.Vi);
    private static string Kg(double v) => v.ToString("#,0.00", Ui.Vi);
    private static string Raw(double v) => Math.Round(v, 6).ToString(CultureInfo.InvariantCulture);
    private static Border Framed(Control c) => new() { BorderBrush = Line, BorderThickness = new Thickness(1), Child = c };

    private long _loadedSeq = -1;
    private static long DataSeq() { try { return App.Services.Modules.CurrentSeq(); } catch { return -2; } }

    private async Task LoadAsync()
    {
        _status.Text = "Đang tải…";
        var seq = DataSeq();
        // 1.90: Svc.Load thay Svc.Rows ở luồng nền — trong lúc đó không vẽ (chỉ số trong _draft / _rowIdx thuộc bản cũ)
        _reloading++;
        try { await Task.Run(Svc.Load); }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        finally { _reloading--; }
        _loadedSeq = seq;
        _status.Text = "";
        _draft.Clear(); _editing = null;
        _loading = true;
        if (_loai.ItemsSource is null) _loai.ItemsSource = new[] { "— Chưa chọn loại —" }.Concat(ViTriService.Loai.Select(l => l.Label)).ToList();
        _loai.SelectedIndex = 0; _ngay.SelectedDate = DateTime.Today; _title.Text = ""; _gop.IsChecked = false;
        _loading = false;
        RenderAll();
        if (Svc.Rows.Count == 0) _tabs.SelectedIndex = 4;
        if (OpenSlipId is { } id)
        {
            OpenSlipId = null; _tabs.SelectedIndex = 1;
            await Task.Delay(50);
            var i = _slipRows.FindIndex(x => x.Id == id);
            if (i >= 0) _sg.Select(i, 0); else _status.Text = $"Phiếu {id} không còn trong danh sách phiếu đang mở (xem tab Lưu trữ theo ngày).";
        }
    }

    /// <summary>Mở thẳng 1 phiếu (từ Tìm nhanh).</summary>
    public string? OpenSlipId { get; set; }

    /// <summary>Chạy thao tác ghi; máy khác vừa sửa → tải lại.</summary>
    private async Task<bool> Act(Action a, string? ok = null)
    {
        try
        {
            a();
            if (ok is not null) _status.Text = ok + " (" + DateTime.Now.ToString("HH:mm:ss") + ")";
            RenderAll();
            return true;
        }
        catch (SheetConflictException)
        {
            await Ui.Message(this, "Dữ liệu Tồn theo vị trí vừa được máy khác sửa nên thao tác này CHƯA được lưu (để không ghi đè). Bấm Đóng để tải bản mới nhất rồi làm lại.", "Máy khác vừa sửa");
            await LoadAsync();
        }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); }
        RenderAll();
        return false;
    }

    /// <summary>&gt; 0: đang tải lại dữ liệu ở luồng nền → bỏ qua mọi lượt vẽ (vẽ lại khi tải xong).</summary>
    private int _reloading;

    private void RenderAll()
    {
        if (_reloading > 0) return;
        Svc.DropStale(_draft);
        var i = Svc.Info;
        _fileInfo.Text = Svc.Rows.Count == 0
            ? "Chưa có dữ liệu vị trí — mở tab “Nạp file” để nạp file Tồn kho theo vị trí."
            : $"File: {i.FileName} · Ngày dữ liệu: {(i.DataDate.Length > 0 ? i.DataDate : "—")} · {Svc.Rows.Count:#,0} dòng · nạp lúc {Nap(i.ImportedAt)} · {Svc.Slips.Count} phiếu";
        RenderStock(); RenderDraft(); RenderTab();
    }
    private void RenderTab()
    {
        switch (_tabs.SelectedIndex)
        {
            case 1: RenderSlips(); break;
            case 2: RenderArchive(); break;
            case 3: RenderStats(); break;
            case 4: RenderFile(); break;
            case 5: _ = RenderDaXuatAsync(); break;
        }
    }
    // =====================================================================================
    // 1.132 Tab Tra cứu đã xuất (người dùng: "thêm tìm mã hàng, size, vị trí đã xuất, để rà soát lại các mã hàng đã xuất")
    // =====================================================================================

    private readonly GridModel _xm = new() { ReadOnly = true };
    internal SheetGrid? _xg;
    internal readonly TextBox _xq = new() { Watermark = "Tìm mã / tên / size / vị trí / lô / số phiếu… (nhiều từ cách nhau dấu cách)", Width = 420 };
    private readonly CheckBox _xTon = new() { Content = "Chỉ dòng file tồn còn hàng ở vị trí", FontSize = 12.5 };
    private readonly ComboBox _xNguon = new() { ItemsSource = new[] { "Mọi nguồn", "Phiếu app tạo", "Excel kho gốc" }, SelectedIndex = 0, MinWidth = 130 };
    private readonly CheckBox _xChua = new() { Content = "Chỉ dòng chưa đối chiếu được", FontSize = 12.5 };
    private const int XPage = 5000;
    private int _xLimit = XPage, _xAll;
    private readonly Button _xMore = new() { Content = "Hiện thêm", IsVisible = false, Padding = new Thickness(10, 4), FontSize = 12.5 };
    private readonly Avalonia.Threading.DispatcherTimer _xDebounce = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly TextBlock _xInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    internal List<ViTriService.DaXuat> _xRows = new();
    private const int XTonCol = 9;

    private Control BuildDaXuat()
    {
        _xg = new SheetGrid(_xm) { SpareRows = 0, LargeTitle = "Tra cứu hàng đã xuất" };
        _xg.CellTip = (r, c) => r < _xRows.Count && c == XTonCol ? "Tồn trong file đang nạp ở đúng Mã · Lô · Vị trí của dòng (trống = ô đã hết mã này)" : null;
        _xg.DoubleTapped += async (_, _) => await OpenXuat();
        void Re() { _xLimit = XPage; RenderDaXuat(); }
        _xDebounce.Tick += (_, _) => { _xDebounce.Stop(); Re(); };   // gõ liên tục chỉ lọc 1 lần khi dừng tay
        _xq.TextChanged += (_, _) => { if (_xAll < 3000) { Re(); return; } _xDebounce.Stop(); _xDebounce.Start(); };   // ít dòng: lọc ngay; nhiều dòng: chờ dừng tay
        _xTon.IsCheckedChanged += (_, _) => Re();
        _xChua.IsCheckedChanged += (_, _) => Re();
        _xNguon.SelectionChanged += (_, _) => Re();
        _xMore.Click += (_, _) => { _xLimit += XPage; RenderDaXuat(); };
        var bar = new ToolRow { Margin = new Thickness(0, 8, 0, 6) };
        bar.Children.Add(_xq); bar.Children.Add(_xNguon); bar.Children.Add(_xTon); bar.Children.Add(_xChua); bar.Children.Add(_xInfo); bar.Children.Add(_xMore);
        bar.Children.Add(ToolRow.Gap());
        bar.Children.Add(Btn("📂 Nạp Excel kho gốc…", async () => await ImportXuatGoc(), primary: true, tip: "Báo cáo “Kho - Nhập xuất chi tiết” của phần mềm kho gốc. Lưu riêng, nạp nhiều lần thì cộng dồn (không nhân đôi)."));
        bar.Children.Add(ToolRow.More(Btn("Xóa dữ liệu Excel đã nạp", async () => await ClearXuatGoc(), danger: true, tip: "Chỉ xóa phần nạp từ Excel; phiếu do app tạo giữ nguyên")));
        bar.Children.Add(Btn("Mở phiếu của dòng đang chọn", async () => await OpenXuat(), tip: "Hoặc bấm đúp vào dòng"));
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);
        dock.Children.Add(Framed(_xg));
        return dock;
    }

    /// <summary>Lần đầu (hoặc sau khi dữ liệu đổi) dựng danh sách ~33.000 dòng ở luồng nền để giao diện không đứng.</summary>
    private async Task RenderDaXuatAsync()
    {
        if (!Svc.DaXuatReady) { _xInfo.Text = "Đang dựng danh sách đã xuất…"; try { await Task.Run(() => Svc.DaXuatCached()); } catch (Exception e) { _xInfo.Text = Ui.Describe(e); return; } }
        RenderDaXuat();
    }

    private void RenderDaXuat()
    {
        if (_xg is null) return;
        var all = Svc.DaXuatCached(); _xAll = all.Count;   // 1.163: dựng sẵn, gõ tìm chỉ lọc (file Excel kho gốc cả năm ~33.000 dòng)
        var nguon = _xNguon.SelectedIndex;   // 0 mọi nguồn · 1 app · 2 Excel
        var toks = GridFilter.Fold(_xq.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matched = all.Where(x => (nguon == 0 || (nguon == 1) == (x.Nguon == "app"))
            && (_xTon.IsChecked != true || x.TonVt > 0)
            && (_xChua.IsChecked != true || x.DoiChieu is "Chưa thấy ở Excel" or "Chỉ có ở Excel")
            && toks.All(t => x.Hay.Contains(t, StringComparison.Ordinal))).ToList();
        _xRows = matched.Count > _xLimit ? matched.Take(_xLimit).ToList() : matched;   // bảng chỉ nạp phần đầu cho nhẹ; còn lại bấm "Hiện thêm" hoặc gõ thêm để lọc
        _xm.Load(new[]
        {
            new GridColumn("ngay", "Ngày xuất", 90, "date"), new GridColumn("phieu", "Số phiếu", 120), new GridColumn("loai", "Loại", 120),
            new GridColumn("ma", "Mã hàng", 190), new GridColumn("ten", "Tên hàng", 230), new GridColumn("size", "Size", 70), new GridColumn("lo", "Lô", 110),
            new GridColumn("vt", "Vị trí", 100), new GridColumn("sl", "SL xuất", 80, "number"), new GridColumn("ton", "Tồn hiện tại ở vị trí", 130, "number"),
            new GridColumn("tl", "Trọng lượng", 90, "number"), new GridColumn("net", "Net (kg)", 95, "number"), new GridColumn("tt", "Trạng thái", 110),
            new GridColumn("nguoi", "Người lập", 110),
            new GridColumn("nguon", "Nguồn", 110), new GridColumn("doi", "Đối chiếu", 140), new GridColumn("dt", "Đặc tính", 100), new GridColumn("hd", "Hợp đồng", 100),
        }, _xRows.Select(x => new[]
        {
            Dmy(x.Ngay), x.MaPhieu, x.Loai, x.Ma.Length > 0 ? x.Ma : x.MaGoc, x.Ten, x.Size, x.Lo, x.Vt, Raw(x.Sl), x.TonVt > 0 ? Raw(x.TonVt) : "", Raw(x.Tl), Raw(x.Sl * x.Tl),
            x.Nguon == "excel" ? "Excel kho gốc" : (x.DaTru ? "Đã trừ tồn" : "Đang mở") + (x.DienThoai ? " · ĐT" : ""), x.NguoiTao,
            x.Nguon == "excel" ? "Excel kho gốc" : "App tạo", x.DoiChieu, x.Dt, x.Hd,
        }).ToList());
        _xg.Refresh();
        var xi = Svc.XuatGocInfo;
        _xMore.IsVisible = matched.Count > _xRows.Count; _xMore.Content = $"Hiện thêm {Math.Min(XPage, matched.Count - _xRows.Count):#,0} dòng (còn {matched.Count - _xRows.Count:#,0})";
        _xInfo.Text = $"{(matched.Count > _xRows.Count ? $"{_xRows.Count:#,0} trong {matched.Count:#,0}" : $"{_xRows.Count:#,0}")} / {all.Count:#,0} dòng · {_xRows.Where(x => x.Nguon == "app").Select(x => x.SlipId).Distinct().Count()} phiếu app"
            + (Svc.XuatGoc.Count > 0 ? $" · Excel: {Svc.XuatGoc.Count:#,0} dòng {ViTriService.Dmy(xi.From)}–{ViTriService.Dmy(xi.To)}" : " · chưa nạp Excel kho gốc");
    }

    private async Task ImportXuatGoc()
    {
        var path = await Ui.OpenFile(this, "Chọn file Excel Nhập xuất chi tiết (phần mềm kho gốc)", "xlsx", "Excel");
        if (path is null) return;
        ViTriService.XuatGocFile? f = null;
        _status.Text = "Đang đọc file…";
        try { var (done, rd) = await Ui.Busy(this, $"Đang đọc file “{System.IO.Path.GetFileName(path)}”…", () => Svc.ReadXuatGoc(path), cancellable: true, op: "Đọc file Excel", detail: "Tra cứu đã xuất", rows: x => x.Rows.Count); if (!done) { _status.Text = "Đã hủy đọc file."; return; } f = rd; }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex), "Không đọc được file"); return; }
        _status.Text = "";
        var from = f!.Rows.Min(x => x.Ngay); var to = f.Rows.Max(x => x.Ngay);
        if (!await Ui.Confirm(this, $"Nạp {f.Rows.Count:#,0} dòng xuất ({ViTriService.Dmy(from)} – {ViTriService.Dmy(to)}) từ “{f.FileName}”?\n\nDữ liệu này lưu RIÊNG với phiếu do app tạo, cộng dồn vào lần nạp trước (dòng đã có không bị nhân đôi) và KHÔNG làm thay đổi tồn theo vị trí.", "Nạp Excel kho gốc")) return;
        ViTriService.XuatGocResult? r = null;
        try { var (_, rr) = await Ui.Busy(this, "Đang lưu…", () => Svc.ImportXuatGoc(f), op: "Nạp file Excel", detail: "Tra cứu đã xuất"); r = rr; }
        catch (Exception ex) { await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex), "Không nạp được"); return; }
        await RenderDaXuatAsync();
        _status.Text = $"Đã nạp Excel kho gốc: +{r!.Added:#,0} dòng ({DateTime.Now:HH:mm:ss})";
        await Ui.Message(this, $"Thêm mới {r.Added:#,0} dòng · {r.Duplicate:#,0} dòng đã có từ trước" + (f.Skipped > 0 ? $" · bỏ qua {f.Skipped} dòng không đọc được" : "") + $".\nTổng dữ liệu Excel: {r.Total:#,0} dòng ({ViTriService.Dmy(r.From)} – {ViTriService.Dmy(r.To)}).\n\nCột “Đối chiếu” cho biết dòng nào của app khớp với Excel, dòng nào chỉ có một bên.", "Nạp Excel kho gốc xong");
    }

    private async Task ClearXuatGoc()
    {
        if (Svc.XuatGoc.Count == 0) { _status.Text = "Chưa có dữ liệu Excel kho gốc."; return; }
        if (!await Ui.Confirm(this, $"Xóa {Svc.XuatGoc.Count:#,0} dòng đã nạp từ Excel kho gốc? Phiếu do app tạo không bị ảnh hưởng.", "Xóa dữ liệu Excel")) return;
        Svc.ClearXuatGoc(); await RenderDaXuatAsync();
    }

    /// <summary>Mở phiếu của dòng đang chọn: phiếu đang mở → tab Phiếu xuất; đã lưu trữ → tab Lưu trữ theo ngày (đúng ngày + loại).</summary>
    private async Task OpenXuat()
    {
        if (_xg is null || _xg.Row < 0 || _xg.Row >= _xRows.Count) return;
        var id = _xRows[_xg.Row].SlipId;
        if (id.Length == 0) { _status.Text = "Dòng này từ Excel kho gốc — không có phiếu trong app."; return; }
        _tabs.SelectedIndex = 1; await Task.Delay(50);
        var i = _slipRows.FindIndex(x => x.Id == id);
        if (i >= 0) { _sg.Select(i, 0); return; }
        _tabs.SelectedIndex = 2; await Task.Delay(50);
        var g = _arcRows.FindIndex(a => a.Slips.Any(x => x.Id == id));
        if (g >= 0) _ag.Select(g, 0); else _status.Text = $"Không thấy phiếu {id} trong danh sách phiếu / lưu trữ.";
    }

    private static string Nap(double? ms) => ms is > 0 ? DateTime.UnixEpoch.AddMilliseconds(ms.Value).ToLocalTime().ToString("dd/MM/yyyy HH:mm", Ui.Vi) : "—";

    // =====================================================================================
    // Tab Tồn theo vị trí + Phiếu đang soạn
    // =====================================================================================

    private Control BuildStock()
    {
        var bar = new ToolRow { Margin = new Thickness(0, 8, 0, 6) };
        bar.Children.Add(_q); bar.Children.Add(Label("Khu:")); bar.Children.Add(_zone);
        bar.Children.Add(_onlyStock); bar.Children.Add(_onlyPick); bar.Children.Add(_onlyPos); bar.Children.Add(_gridInfo);
        _q.TextChanged += (_, _) => { if (!_loading) RenderStock(); };
        _zone.SelectionChanged += (_, _) => { if (!_loading) RenderStock(); };
        _onlyStock.IsCheckedChanged += (_, _) => { if (!_loading) RenderStock(); };
        _onlyPick.IsCheckedChanged += (_, _) => { if (_loading) return; if (_onlyPick.IsChecked == true) { _loading = true; _onlyPos.IsChecked = false; _loading = false; } RenderStock(); };
        _onlyPos.IsCheckedChanged += (_, _) => { if (_loading) return; if (_onlyPos.IsChecked == true) { _loading = true; _onlyPick.IsChecked = false; _loading = false; } RenderStock(); };
        var hint = new TextBlock
        {
            Text = _edit
                ? "Tích ô đầu dòng (lấy hết) hoặc gõ số vào cột “SL xuất ✎” (x = lấy hết), dán cả cột từ Excel cũng được. Dòng đang xuất tô vàng, vượt tồn tô đỏ. Bấm “Xong” để về phiếu."
                : "Chỉ xem: tài khoản không có quyền lập phiếu xuất.",
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        };
        var head = new StackPanel(); head.Children.Add(bar); head.Children.Add(hint);
        var left = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        left.Children.Add(head); left.Children.Add(Framed(_grid));
        if (!_edit) return left;
        _stockPanel = left;   // tài khoản lập phiếu: bảng tồn nằm trong cửa sổ "Chọn hàng từ tồn…"
        return BuildDraftPanel();
    }

    /// <summary>Cửa sổ lớn chứa bảng tồn: lọc / tìm, tích ô đầu dòng hoặc gõ SL xuất → dòng vào phiếu đang soạn ngay.</summary>
    private async Task OpenPicker()
    {
        if (_stockPanel is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        RenderStock();
        var info = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        void Info() => info.Text = _draft.Count == 0 ? "Chưa chọn dòng nào." : $"Đang chọn {_draft.Count} ô · SL {Q(_draft.Values.Sum())} · Net ≈ {Kg(_draft.Sum(kv => kv.Value * Svc.Rows[kv.Key].Tl))} kg";
        var done = new Button { Content = "Xong — đưa vào phiếu", Classes = { "primary" }, IsDefault = false, MinWidth = 170, HorizontalContentAlignment = HorizontalAlignment.Center };
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(done, Dock.Right); bar.Children.Add(done); bar.Children.Add(info);
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom); root.Children.Add(bar); root.Children.Add(_stockPanel);
        var scr = owner.Screens.ScreenFromWindow(owner)?.WorkingArea; var sc = owner.RenderScaling <= 0 ? 1 : owner.RenderScaling;
        var w = new Window
        {
            Title = "Chọn hàng từ tồn — tích ô đầu dòng (lấy hết) hoặc gõ SL xuất",
            Width = Math.Max(900, (scr?.Width ?? 1400) / sc * 0.92), Height = Math.Max(560, (scr?.Height ?? 860) / sc * 0.88),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = AppTheme.Card, MinWidth = 700, MinHeight = 420,
            Content = new Border { Padding = new Thickness(14, 12), Child = root },
        };
        PickerWindow = w;
        void Changed() => Info();
        _draftChanged += Changed; Info();
        done.Click += (_, _) => w.Close();
        w.Closed += (_, _) => { _draftChanged -= Changed; root.Children.Remove(_stockPanel); PickerWindow = null; };
        w.Opened += (_, _) => _grid.Focus();
        await Ui.Show(this, w);
        RenderDraft();
    }
    /// <summary>Cửa sổ chọn hàng đang mở (công cụ chụp màn hình dùng).</summary>
    internal Window? PickerWindow { get; private set; }
    internal Task OpenPickerForTest() => OpenPicker();
    private event Action? _draftChanged;

    private Control BuildDraftPanel()
    {
        _loai.SelectionChanged += (_, _) => { if (!_loading) RenderDraft(); };
        _ngay.SelectedDateChanged += (_, _) => { if (!_loading) RenderDraft(); };
        _gop.IsCheckedChanged += (_, _) => { if (!_loading) RenderDraft(); };
        _pvm.Load(new[] { new GridColumn("vt", "Vị trí", 62), new GridColumn("ma", "Mã hàng", 162), new GridColumn("x", "SL", 50, "number"), new GridColumn("dt", "Đặc tính", 80) }, Array.Empty<string[]>());
        _loai.MinWidth = 210; _loai.HorizontalAlignment = HorizontalAlignment.Left;
        _ngay.Width = 150; _ngay.HorizontalAlignment = HorizontalAlignment.Left;
        _title.Width = 260;

        // hàng 1: đầu phiếu
        var form = new WrapPanel { ItemSpacing = 10, LineSpacing = 6 };
        StackPanel Field(string label, Control c) { var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; sp.Children.Add(Label(label)); sp.Children.Add(c); return sp; }
        _draftHead.VerticalAlignment = VerticalAlignment.Center;
        form.Children.Add(_draftHead);
        form.Children.Add(Field("Loại phiếu", _loai)); form.Children.Add(Field("Ngày", _ngay)); form.Children.Add(Field("Tiêu đề", _title));
        _gop.VerticalAlignment = VerticalAlignment.Center; form.Children.Add(_gop);

        // hàng 2: nút
        var btns = new ToolRow { Margin = new Thickness(0, 8, 0, 0) };
        btns.Children.Add(Btn("➕ Chọn hàng từ tồn…", async () => await OpenPicker(), primary: true, tip: "Mở bảng tồn theo vị trí (cửa sổ lớn): lọc / tìm, tích ô đầu dòng hoặc gõ SL xuất."));
        _save.Classes.Remove("primary");
        btns.Children.Add(_save);
        if (_export)
        {
            btns.Children.Add(Btn("🖨 In…", async () => { if (_draft.Count == 0) { await Ui.Message(this, "Chưa có dòng nào trong phiếu đang soạn."); return; } await PrintItems(DraftView(), DraftTitle, Ngay); }, tip: "Mở trang in A4 trong trình duyệt (In hoặc Lưu PDF)."));
            btns.Children.Add(Btn("Xuất Excel…", async () => await ExportDraft(), tip: "Xuất phiếu đang soạn ra Excel theo mẫu app cũ (không lưu phiếu)."));
            btns.Children.Add(Btn("Ảnh PNG…", async () => await ImageDraft()));
        }
        btns.Children.Add(Btn("Bỏ chọn hết", ClearDraft));
        btns.Children.Add(_cancelEdit);

        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 6) };
        top.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(12, 10), Child = new StackPanel { Children = { form, btns } } });
        _draftSum.Margin = new Thickness(2, 4, 0, 0);
        top.Children.Add(_draftSum); top.Children.Add(_draftWarn);
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(Framed(_dg));
        return dock;
    }

    private IBrush? DraftBg(int r, int c)
    {
        if (r >= _dRows.Count) return null;
        var i = _dRows[r];
        return _draft.TryGetValue(i, out var x) && x > Avail(i) + 1e-9 ? OverBg : c == DColX ? PickBg : null;
    }

    /// <summary>Bảng phiếu đang soạn: mỗi ô vị trí đã chọn 1 dòng (thứ tự vị trí), sửa được cột SL xuất (xóa = bỏ dòng).</summary>
    private void RenderDraftGrid()
    {
        _dLoading = true;
        try
        {
            _dRows = _draft.Keys.Where(i => i >= 0 && i < Svc.Rows.Count)
                .OrderBy(i => Svc.Rows[i], Comparer<Row>.Create((a, b) => CmpViTri(a.Vt, b.Vt) is var c && c != 0 ? c : string.Compare(a.Ma, b.Ma, StringComparison.Ordinal))).ToList();
            _dm.Load(DraftCols.Select(c => new GridColumn(c.Key, c.Header, c.Width, c.Type) { ReadOnly = c.ReadOnly }), _dRows.Select(i =>
            {
                var r = Svc.Rows[i]; var x = _draft[i];
                return new[] { r.Vt.Length > 0 ? r.Vt : NoPos, r.Ma, TenOf(r), Svc.SizeOf(r), Raw(TlOf(r)), r.Dt, r.Hd, Raw(Avail(i)), Raw(x), Raw(Round(x * r.Tl)), r.Lo };
            }).ToList());
            _dSnap = _dm.Snapshot();
        }
        finally { _dLoading = false; }
        _dg.Refresh();
    }

    /// <summary>Sửa SL xuất ngay trên bảng phiếu (gõ "x" = lấy hết; xóa / 0 = bỏ dòng khỏi phiếu).</summary>
    private void OnDraftChanged()
    {
        var now = _dm.Snapshot(); var changed = false;
        for (var r = 0; r < Math.Min(now.Count, _dRows.Count); r++)
        {
            var before = r < _dSnap.Count ? _dSnap[r][DColX] : ""; var t = (now[r][DColX] ?? "").Trim();
            if (before == t) continue;
            var i = _dRows[r]; changed = true;
            if (t.Length == 0 || t == "0") { _draft.Remove(i); continue; }
            if (t is "x" or "X" or "*") { _draft[i] = Round(Avail(i)); continue; }
            if (Ui.TryParseNumber(t, out var v) && v > 0) _draft[i] = Round(v);
            else _status.Text = $"Bỏ qua ô không phải số: “{t}”";
        }
        if (!changed) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { RenderStock(); RenderDraft(); });
    }

    private double Avail(int i) => Svc.Rows[i].Sl + (_editing?.Bonus.GetValueOrDefault(i) ?? 0);

    private void RenderStock()
    {
        if (_reloading > 0) return;
        Svc.DropStale(_draft);
        _loading = true;
        try
        {
            var zones = Svc.Rows.Select(r => ZoneOf(r.Vt)).Where(z => z.Length > 0).Distinct().OrderBy(z => z, Comparer<string>.Create(CmpViTri)).ToList();
            var items = new List<string> { "Tất cả" }.Concat(zones).ToList();
            var cur = _zone.SelectedItem as string;
            if (_zone.ItemsSource is not List<string> old || !old.SequenceEqual(items)) { _zone.ItemsSource = items; _zone.SelectedIndex = Math.Max(0, cur is null ? 0 : items.IndexOf(cur)); }
            var zone = _zone.SelectedIndex > 0 ? _zone.SelectedItem as string : null;
            var hit = Svc.Matcher(_q.Text);
            var pos = _onlyPos.IsChecked == true ? _draft.Keys.Select(i => Svc.Rows[i].Vt).Where(v => v.Length > 0).ToHashSet() : null;
            _done = Svc.DoneMap();
            _rowIdx = Enumerable.Range(0, Svc.Rows.Count).Where(i =>
            {
                var r = Svc.Rows[i];
                if (_onlyPick.IsChecked == true) return _draft.ContainsKey(i);
                if (pos is not null)
                {
                    if (!pos.Contains(r.Vt)) return false;
                }
                else
                {
                    if (zone is not null && ZoneOf(r.Vt) != zone) return false;
                    if (hit is not null && !hit(r)) return false;
                }
                if (_draft.ContainsKey(i)) return true;
                return _onlyStock.IsChecked != true || r.Sl > 1e-9 || (_editing?.Bonus.ContainsKey(i) ?? false);
            }).OrderBy(i => Svc.Rows[i], Comparer<Row>.Create((a, b) => CmpViTri(a.Vt, b.Vt) is var c && c != 0 ? c : string.Compare(a.Ma, b.Ma, StringComparison.Ordinal))).ToList();
            var rows = _rowIdx.Select(i =>
            {
                var r = Svc.Rows[i];
                return new[]
                {
                    r.Vt.Length > 0 ? r.Vt : NoPos, r.Ma, TenOf(r), Svc.SizeOf(r), Raw(TlOf(r)), r.Dt, r.Hd, Raw(r.Sl),
                    _draft.TryGetValue(i, out var x) ? Raw(x) : "", _done.TryGetValue(i, out var d) ? Raw(d) : "", Raw(Round(r.Sl * r.Tl)), r.Lo,
                };
            }).ToList();
            var cols = StockCols.Select(c => new GridColumn(c.Key, c.Header, c.Width, c.Type) { ReadOnly = c.ReadOnly || (c.Key == "x" && !_edit) }).ToList();
            _gm.Load(cols, rows);
            _snap = _gm.Snapshot();
            var tong = _rowIdx.Sum(i => Svc.Rows[i].Sl);
            _gridInfo.Text = $"{_rowIdx.Count:#,0} dòng · tồn {Q(tong)}";
        }
        finally { _loading = false; }
        _grid.Refresh();
    }

    // 1.95: Tên hàng = tên trong file bỏ đuôi " - Đặc tính - Hợp đồng"; Trọng lượng = trọng lượng của dòng trong file tồn (Net / phiếu dùng số này),
    // file để trống / 0 thì tra theo MÃ HÀNG (Danh sách mã hàng, rồi Tồn kho An An)
    private static string TenOf(Row r)
    {
        var p = r.Ten.Split(" - ");
        return p.Length >= 3 ? string.Join(" - ", p[..^2]).Trim() : r.Ten;
    }
    private static Dictionary<string, double>? _tlMa;
    private static DateTime _tlAt;
    private double TlOf(Row r)
    {
        if (r.Tl > 0) return r.Tl;
        if (_tlMa is null || (DateTime.Now - _tlAt).TotalMinutes > 5)
        {
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var dc = App.Services.DoiChieu; dc.LoadDictionaries();
                foreach (var ma in Svc.Rows.Select(x => x.Ma).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (dc.WeightOfMa(ma) is { } w and > 0) map[ma] = w;
            }
            catch { /* không tra được thì dùng trọng lượng trong file */ }
            _tlMa = map; _tlAt = DateTime.Now;
        }
        return _tlMa.TryGetValue(r.Ma, out var t) ? t : r.Tl;
    }

    private IBrush? StockBg(int r, int c)
    {
        if (r >= _rowIdx.Count) return null;
        var i = _rowIdx[r];
        if (_draft.TryGetValue(i, out var x)) return x > Avail(i) + 1e-9 ? OverBg : PickBg;
        if (c == ColX + 1 && _done.ContainsKey(i)) return DoneBg;
        if (Svc.Rows[i].Sl <= 1e-9) return ZeroBg;
        return null;
    }

    /// <summary>Sửa cột SL xuất (gõ / dán / xóa) → cập nhật phiếu đang soạn. Sửa tại chỗ (không tải lại bảng → không nhảy vị trí cuộn).</summary>
    private void OnGridChanged()
    {
        var now = _gm.Snapshot();
        var bad = new List<string>(); var fix = new List<(int r, int c, string v)>();
        for (var r = 0; r < Math.Min(now.Count, _rowIdx.Count); r++)
        {
            var before = r < _snap.Count ? _snap[r][ColX] : ""; var after = now[r][ColX] ?? "";
            if (before == after) continue;
            var i = _rowIdx[r]; var t = after.Trim();
            if (t.Length == 0 || t == "0") { _draft.Remove(i); if (after.Length > 0) fix.Add((r, ColX, "")); continue; }
            double v;
            if (t is "x" or "X" or "*") v = Round(Avail(i));
            else if (!Ui.TryParseNumber(t, out v) || v < 0) { bad.Add($"{Svc.Rows[i].Ma} @ {Svc.Rows[i].Vt}: “{t}”"); fix.Add((r, ColX, before)); continue; }
            if (v > 1e-9) { _draft[i] = Round(v); if (Raw(v) != after) fix.Add((r, ColX, Raw(v))); }
            else { _draft.Remove(i); fix.Add((r, ColX, "")); }
        }
        _status.Text = bad.Count > 0 ? "Bỏ qua ô không phải số: " + string.Join("; ", bad.Take(3)) + (bad.Count > 3 ? " …" : "") : "";
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _loading = true;
            try { if (fix.Count > 0) _gm.SetCells(fix); _snap = _gm.Snapshot(); }
            finally { _loading = false; }
            if (_onlyPick.IsChecked == true) RenderStock(); else _grid.Refresh();
            RenderDraft();
        });
    }

    private List<Pick> Picks() => _draft.Select(kv => new Pick(kv.Key, kv.Value)).ToList();
    private string LoaiKey => _loai.SelectedIndex > 0 ? ViTriService.Loai[_loai.SelectedIndex - 1].Key : "";
    private DateTime Ngay => _ngay.SelectedDate?.Date ?? DateTime.Today;
    private string DraftTitle => CleanTitle(_title.Text);

    private List<ViewItem> DraftView() => Svc.BuildView(Svc.Items(Picks()), _gop.IsChecked == true);

    private void RenderDraft()
    {
        if (!_edit || _reloading > 0) return;
        Svc.DropStale(_draft);   // 1.90: chỉ số dòng cũ (vd dòng tạm của phiếu đang sửa vừa bị dọn khi lưu) → trước đây Index out of range
        _draftHead.Text = _editing is null ? "Phiếu đang soạn" : $"Đang sửa phiếu {_editing.Id}";
        _cancelEdit.IsVisible = _editing is not null;
        _save.Content = _editing is null ? "💾 Lưu phiếu (trừ tồn)" : "💾 Lưu thay đổi phiếu";
        var view = DraftView();
        RenderDraftGrid();
        _draftChanged?.Invoke();
        var over = _draft.Where(kv => kv.Value > Avail(kv.Key) + 1e-9).ToList();
        _draftSum.Text = _draft.Count == 0
            ? "Phiếu chưa có dòng nào. Bấm “Chọn hàng từ tồn…” để chọn mã hàng cần xuất."
            : $"{_draft.Count} ô · {view.Count} dòng phiếu · Tổng SL {Q(_draft.Values.Sum())} · Net ≈ {Kg(_draft.Sum(kv => kv.Value * Svc.Rows[kv.Key].Tl))} kg";
        _draftWarn.Text = over.Count > 0
            ? $"⚠ {over.Count} ô vượt tồn (tô đỏ): " + string.Join("; ", over.Take(3).Select(kv => $"{Svc.Rows[kv.Key].Ma} @ {Svc.Rows[kv.Key].Vt} còn {Q(Avail(kv.Key))}"))
            : LoaiKey.Length == 0 && _draft.Count > 0 ? "Chưa chọn loại phiếu (thay thùng / xuất xả) — vẫn lưu được, nhưng phải chọn loại thì phiếu mới vào Lưu trữ theo ngày." : "";
        _save.IsEnabled = _draft.Count > 0 && over.Count == 0;
    }

    private void ClearDraft() { _draft.Clear(); RenderStock(); RenderDraft(); }
    private void CancelEdit()
    {
        _editing = null; _draft.Clear(); Svc.PruneGhosts();
        _loading = true; _loai.SelectedIndex = 0; _ngay.SelectedDate = DateTime.Today; _title.Text = ""; _gop.IsChecked = false; _loading = false;
        RenderAll();
    }

    private async Task SaveDraft()
    {
        if (_draft.Count == 0) return;
        var warns = Svc.Warnings(Picks(), Ngay, _editing?.Id);
        if (warns.Count > 0 && !await Ui.Confirm(this, "Kiểm tra lại trước khi lưu:\n\n• " + string.Join("\n• ", warns) + "\n\nVẫn lưu phiếu?", "Cảnh báo", "Vẫn lưu")) return;
        Slip? s = null;
        var editing = _editing;
        // 1.90: SaveEdit dọn dòng tạm (PruneGhosts → Svc.Rows ngắn lại) nên phải bỏ phiếu đang soạn NGAY sau khi lưu,
        // trước khi Act vẽ lại — trước đây RenderAll đọc Svc.Rows[chỉ số dòng tạm] → ArgumentOutOfRangeException.
        if (!await Act(() =>
            {
                s = editing is null
                    ? Svc.CreateSlip(Picks(), _gop.IsChecked == true, LoaiKey, Ngay, _title.Text ?? "")
                    : Svc.SaveEdit(editing, Picks(), _gop.IsChecked == true, LoaiKey, Ngay, _title.Text ?? "");
                _editing = null; _draft.Clear();
            })) return;
        _loading = true; _title.Text = ""; _loading = false;
        _status.Text = $"Đã lưu phiếu {s!.Id}: {s.Lines.Count} dòng, SL {Q(s.TongSl)} — đã trừ tồn.";
        RenderAll();
    }

    private async Task ExportDraft()
    {
        if (_draft.Count == 0) { await Ui.Message(this, "Chưa có dòng nào trong phiếu đang soạn."); return; }
        await ExportItems(DraftView(), DraftTitle, Ngay, LoaiKey);
    }
    private async Task ImageDraft()
    {
        if (_draft.Count == 0) { await Ui.Message(this, "Chưa có dòng nào trong phiếu đang soạn."); return; }
        await ShowImage(DraftView(), DraftTitle, Ngay, LoaiKey);
    }

    private async Task ExportItems(List<ViewItem> items, string title, DateTime day, string loai)
    {
        var path = await Ui.SaveFile(this, "Xuất phiếu ra Excel", Ui.SafeFileName(FileBase(loai, day)) + ".xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.ExportExcel(path, items, title, day))) Ui.Exported(this, path);
    }

    /// <summary>In trực tiếp: dùng lại đúng hàm xuất Excel rồi mở trang in A4 trong trình duyệt.</summary>
    private async Task PrintItems(List<ViewItem> items, string title, DateTime day)
    {
        if (items.Count == 0) { await Ui.Message(this, "Phiếu chưa có dòng nào để in."); return; }
        await Ui.PrintVia(this, CleanTitle(title) + " " + day.ToString("dd/MM/yyyy"), tmp => Svc.ExportExcel(tmp, items, title, day));
    }

    private async Task ShowImage(List<ViewItem> items, string title, DateTime day, string loai)
    {
        var bmp = SlipImageRenderer.Render(items, $"{CleanTitle(title)} : {day:dd/MM/yyyy}");
        var save = new Button { Content = "Lưu ảnh PNG…", Classes = { "primary" }, IsDefault = true };
        var close = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Ui.ImagePreview(bmp, 900));
        body.Children.Add(Ui.Buttons(close, save));
        var w = Ui.Dialog("Ảnh phiếu xuất", body, Ui.ImageDialogWidth(bmp, 900));
        close.Click += (_, _) => w.Close();
        save.Click += async (_, _) =>
        {
            var path = await Ui.SaveFile(w, "Lưu ảnh", Ui.SafeFileName(FileBase(loai, day)) + ".png", "png", "Ảnh PNG");
            if (path is null) return;
            if (await Ui.Run(w, () => bmp.Save(path))) { w.Close(); Ui.Exported(this, path); }
        };
        await Ui.Show(this, w);
    }

    // =====================================================================================
    // Tab Phiếu xuất
    // =====================================================================================

    private Control BuildSlips()
    {
        var act = new ToolRow { Margin = new Thickness(0, 8, 0, 6) };
        if (_edit)
        {
            act.Children.Add(Btn("✎ Sửa phiếu", async () => await EditSlip(), primary: true, tip: "Mở phiếu ở tab Tồn theo vị trí để sửa số lượng / thêm bớt dòng."));
            act.Children.Add(Btn("Hủy trừ / Trừ lại", async () => await ToggleSlip(), tip: "Hủy trừ = cộng trả lại tồn (phiếu vẫn giữ); Trừ lại = trừ tồn lần nữa."));
            act.Children.Add(Btn("Mở khóa", async () => await UnlockSlip(), tip: "Phiếu của lần nạp file trước (đã khóa) nhưng thực tế chưa xuất → gắn vào số liệu hiện tại và trừ tồn."));
            act.Children.Add(Btn("Gộp phiếu", async () => await MergeSlips(), tip: "Bôi đen từ 2 phiếu cùng loại (liền nhau) rồi bấm."));
            act.Children.Add(Btn("Tách phiếu gộp", async () => await SplitBack()));
            act.Children.Add(Btn("Tách mã ra phiếu mới…", async () => await SplitLines()));
            act.Children.Add(Btn("Đổi loại…", async () => await ChangeLoai()));
            act.Children.Add(Btn("Đổi ngày…", async () => await ChangeDay()));
            act.Children.Add(Btn("🗑 Xóa phiếu", async () => await DeleteSlip(), danger: true));
        }
        if (_export)
        {
            act.Children.Add(Btn("🖨 In…", async () => { if (CurSlip() is { } s) await PrintItems(Svc.ViewOfSlip(s), s.TieuDe ?? TitleDefault, Local(s.CreatedAt) ?? DateTime.Today); }));
            act.Children.Add(Btn("Xuất Excel…", async () => { if (CurSlip() is { } s) await ExportItems(Svc.ViewOfSlip(s), s.TieuDe ?? TitleDefault, Local(s.CreatedAt) ?? DateTime.Today, s.Loai); }));
            act.Children.Add(Btn("Ảnh PNG…", async () => { if (CurSlip() is { } s) await ShowImage(Svc.ViewOfSlip(s), s.TieuDe ?? TitleDefault, Local(s.CreatedAt) ?? DateTime.Today, s.Loai); }));
        }
        var hint = new TextBlock
        {
            Text = "Phiếu đã qua ngày và đã chọn loại tự chuyển sang tab “Lưu trữ theo ngày”. Phiếu của lần nạp file trước bị khóa (xám) — số liệu file mới đã gồm phiếu đó.",
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        };
        var head = new StackPanel(); head.Children.Add(act); head.Children.Add(hint);
        var split = Ui.MasterDetail("Danh sách phiếu", Framed(_sg), _sg, "Chi tiết phiếu", Framed(_sd), _slipInfo);
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head); dock.Children.Add(split);
        return dock;
    }

    private static string State(Slip s) => !Svc.Active(s) ? "Đã khóa (lần nạp trước)" : s.Deducted ? "Đang trừ tồn" : "Đã hủy trừ";

    private void RenderSlips()
    {
        _slipRows = Svc.OpenSlips();
        _sm.Load(new[]
        {
            new GridColumn("id", "Mã phiếu", 120), new GridColumn("ngay", "Ngày", 85), new GridColumn("loai", "Loại", 120), new GridColumn("td", "Tiêu đề", 150),
            new GridColumn("n", "Số dòng", 60, "number"), new GridColumn("sl", "Tổng SL", 70, "number"), new GridColumn("tt", "Trạng thái", 150),
            new GridColumn("nguoi", "Người tạo", 110), new GridColumn("gc", "Ghi chú", 220),
        }, _slipRows.Select(s => new[]
        {
            s.Id + (s.FromPhone ? " 📱" : ""), Dmy(DayKey(s)), LoaiLabel(s.Loai) is { Length: > 0 } l ? l : "— chưa chọn —", s.TieuDe ?? "",
            s.Lines.Count.ToString(CultureInfo.InvariantCulture), Raw(s.TongSl), State(s), s.NguoiTao, s.GhiChu,
        }).ToList());
        _sg.Refresh();
        ShowSlipDetail();
    }

    private IBrush? SlipBg(int r, int c)
    {
        if (r >= _slipRows.Count) return null;
        var s = _slipRows[r];
        if (!Svc.Active(s)) return LockBg;
        if (!s.Deducted) return OffBg;
        if (c == 2 && s.Loai.Length == 0) return ChuaBg;
        return null;
    }

    private Slip? CurSlip() => _sg.Row >= 0 && _sg.Row < _slipRows.Count ? _slipRows[_sg.Row] : null;
    private List<Slip> SelSlips() { var s = _sg.Selection; return Enumerable.Range(s.R1, s.Rows).Where(r => r >= 0 && r < _slipRows.Count).Select(r => _slipRows[r]).ToList(); }

    private void ShowSlipDetail()
    {
        var s = CurSlip();
        var view = s is null ? new List<ViewItem>() : Svc.ViewOfSlip(s);
        _sdm.Load(ItemCols(), view.Select(ItemRow).ToList());
        _sd.Refresh();
        _slipInfo.Text = s is null
            ? (_slipRows.Count == 0 ? "Chưa có phiếu nào (ngoài các phiếu đã vào Lưu trữ theo ngày)." : "Chọn một phiếu để xem chi tiết.")
            : $"{s.Id} · {LoaiLabel(s.Loai)} · {Dmy(DayKey(s))} · {s.Lines.Count} dòng · SL {Q(s.TongSl)} · {State(s)}" + (s.MergedFrom is { Count: > 0 } m ? $" · gộp từ {m.Count} phiếu" : "") + (s.View?.Gop == true ? " · bản gộp theo Size" : "");
    }
    private static GridColumn[] ItemCols() => new[]
    {
        new GridColumn("vt", "Vị trí", 90), new GridColumn("ma", "Mã hàng", 200), new GridColumn("x", "SL xuất", 70, "number"), new GridColumn("dt", "Đặc tính", 130),
        new GridColumn("hd", "Hợp đồng", 120), new GridColumn("ma21", "Mã gốc (khi gộp)", 260),
    };
    private static string[] ItemRow(ViewItem v) => new[] { v.Vt, v.Ma, Raw(v.X), v.Dt, v.Hd, v.Ma21.Replace("\n", ", ") };

    private async Task EditSlip()
    {
        if (CurSlip() is not { } s) { await Ui.Message(this, "Chọn một phiếu trước."); return; }
        if (_draft.Count > 0 && _editing is null && !await Ui.Confirm(this, "Đang có phiếu soạn dở — bỏ phiếu đó để sửa phiếu " + s.Id + "?", "Sửa phiếu", "Bỏ và sửa")) return;
        EditState? e = null;
        if (!await Act(() => e = Svc.BeginEdit(s.Id))) return;
        _editing = e; _draft.Clear();
        foreach (var (i, q) in e!.Sel) _draft[i] = q;
        _loading = true;
        _loai.SelectedIndex = Array.FindIndex(ViTriService.Loai, l => l.Key == e.Loai) + 1;
        _ngay.SelectedDate = e.Ngay; _title.Text = e.Title; _gop.IsChecked = e.Gop; _onlyPick.IsChecked = true;
        _loading = false;
        _tabs.SelectedIndex = 0;
        RenderAll();
    }
    private async Task ToggleSlip()
    {
        if (CurSlip() is not { } s) return;
        var to = !s.Deducted;
        if (!await Ui.Confirm(this, to ? $"Trừ lại tồn cho phiếu {s.Id}?" : $"Hủy trừ phiếu {s.Id}? Tồn được cộng trả lại; phiếu vẫn giữ để trừ lại sau.", "Phiếu " + s.Id, to ? "Trừ lại" : "Hủy trừ")) return;
        var miss = 0;
        await Act(() => miss = Svc.SetDeducted(s.Id, to), to ? "Đã trừ lại tồn " + s.Id : "Đã hủy trừ " + s.Id);
        if (miss > 0) await Ui.Message(this, $"{miss} dòng của phiếu không còn trong dữ liệu vị trí hiện tại nên không cộng / trừ được.");
    }
    private async Task UnlockSlip()
    {
        if (CurSlip() is not { } s) return;
        if (Svc.Active(s)) { await Ui.Message(this, "Phiếu này chưa bị khóa."); return; }
        if (!await Ui.Confirm(this, $"Mở khóa phiếu {s.Id}?\n\nChỉ làm khi hàng của phiếu THỰC TẾ CHƯA XUẤT (file mới chưa trừ). Phiếu sẽ trừ vào tồn hiện tại.", "Mở khóa phiếu", "Mở khóa")) return;
        (int Missing, int Negative) r = default;
        if (await Act(() => r = Svc.Unlock(s.Id), "Đã mở khóa " + s.Id) && (r.Missing > 0 || r.Negative > 0))
            await Ui.Message(this, $"Đã mở khóa. {(r.Missing > 0 ? r.Missing + " dòng không còn trong file. " : "")}{(r.Negative > 0 ? r.Negative + " ô bị âm tồn — kiểm tra lại." : "")}");
    }
    private async Task MergeSlips()
    {
        var list = SelSlips();
        if (list.Count < 2) { await Ui.Message(this, "Bôi đen từ 2 phiếu trở lên (cùng loại, đang trừ tồn) rồi bấm Gộp phiếu."); return; }
        if (!await Ui.Confirm(this, $"Gộp {list.Count} phiếu: {string.Join(", ", list.Select(s => s.Id))} thành 1 phiếu (gộp theo Size)? Tồn không đổi; tách lại được.", "Gộp phiếu", "Gộp")) return;
        Slip? m = null;
        await Act(() => m = Svc.Merge(list.Select(s => s.Id).ToList()), "Đã gộp phiếu");
    }
    private async Task SplitBack()
    {
        if (CurSlip() is not { } s) return;
        if (s.MergedFrom is not { Count: > 0 }) { await Ui.Message(this, "Phiếu này không phải phiếu gộp."); return; }
        if (!await Ui.Confirm(this, $"Tách phiếu {s.Id} trở lại {s.MergedFrom.Count} phiếu gốc?", "Tách phiếu gộp", "Tách")) return;
        await Act(() => Svc.SplitBack(s.Id), "Đã tách phiếu " + s.Id);
    }
    private async Task ChangeLoai()
    {
        if (CurSlip() is not { } s) return;
        var r = await Ui.Choose(this, $"Loại của phiếu {s.Id}:", "Đổi loại phiếu", ViTriService.Loai.Select(l => l.Label).ToList(), Math.Max(0, Array.FindIndex(ViTriService.Loai, l => l.Key == s.Loai)));
        if (r is null) return;
        await Act(() => Svc.SetLoai(s.Id, ViTriService.Loai[r.Value.Index].Key), "Đã đổi loại " + s.Id);
    }
    private async Task ChangeDay()
    {
        if (CurSlip() is not { } s) return;
        var d = await AskDate($"Ngày của phiếu {s.Id}:", Local(s.CreatedAt) ?? DateTime.Today);
        if (d is null) return;
        await Act(() => Svc.SetDay(s.Id, d.Value), "Đã đổi ngày " + s.Id);
    }
    private async Task<DateTime?> AskDate(string text, DateTime initial)
    {
        var t = await Ui.Prompt(this, text + " (dd/mm/yyyy)", "Chọn ngày", initial.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        if (t is null) return null;
        if (DateTime.TryParseExact(t.Trim(), new[] { "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "d.M.yyyy", "d-M-yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d.Date;
        await Ui.Message(this, "Ngày không hợp lệ: " + t);
        return null;
    }
    private async Task DeleteSlip()
    {
        var list = SelSlips();
        if (list.Count == 0) return;
        var restore = list.Count(s => s.Deducted && Svc.Active(s));
        if (!await Ui.Confirm(this, $"Xóa {list.Count} phiếu: {string.Join(", ", list.Select(s => s.Id))}?" + (restore > 0 ? $"\n\n{restore} phiếu đang trừ tồn sẽ được cộng trả lại tồn." : ""), "Xóa phiếu", "Xóa")) return;
        await Act(() => { foreach (var s in list) Svc.Delete(s.Id); }, $"Đã xóa {list.Count} phiếu");
    }

    /// <summary>Tách một số mã (và SL) của các phiếu đang chọn ra phiếu mới có loại + ngày riêng.</summary>
    private async Task SplitLines()
    {
        var list = SelSlips();
        if (list.Count == 0) { await Ui.Message(this, "Chọn (bôi đen) phiếu cần tách mã."); return; }
        var lines = list.SelectMany(s => s.Lines.Select((l, i) => (s, l, i))).ToList();
        var m = new GridModel();
        m.Load(new[]
        {
            new GridColumn("p", "Phiếu", 110) { ReadOnly = true }, new GridColumn("ma", "Mã hàng", 190) { ReadOnly = true }, new GridColumn("vt", "Vị trí", 80) { ReadOnly = true },
            new GridColumn("sl", "SL phiếu", 70, "number") { ReadOnly = true }, new GridColumn("t", "SL tách ✎", 80, "number"),
        }, lines.Select(x => new[] { x.s.Id, ShortCode(x.l.MaGoc), x.l.Vt, Raw(x.l.Qty), "" }).ToList());
        var g = new SheetGrid(m) { SpareRows = 0, Height = 300 };
        var loai = new ComboBox { ItemsSource = ViTriService.Loai.Select(l => l.Label).ToList(), SelectedIndex = list[0].Loai == "thaythung" ? 1 : 0, MinWidth = 160 };
        var day = new CalendarDatePicker { SelectedDate = Local(list[0].CreatedAt)?.Date ?? DateTime.Today, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy" };
        var note = new TextBox { Watermark = "Ghi chú (không bắt buộc)" };
        var ok = new Button { Content = "Tách ra phiếu mới", Classes = { "primary" } };
        var cancel = new Button { Content = "Đóng", IsCancel = true };
        var opts = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        opts.Children.Add(Label("Loại phiếu mới:")); opts.Children.Add(loai); opts.Children.Add(Label("Ngày:")); opts.Children.Add(day);
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Label("Gõ SL tách vào cột “SL tách ✎” (gõ x = tách hết dòng đó). Tồn không đổi — chỉ chia lại phiếu."));
        body.Children.Add(Framed(g)); body.Children.Add(opts); body.Children.Add(note); body.Children.Add(Ui.Buttons(cancel, ok));
        var w = Ui.Dialog("Tách mã ra phiếu mới", body, 640);
        cancel.Click += (_, _) => w.Close();
        ok.Click += async (_, _) =>
        {
            var picks = new List<SplitPick>();
            for (var r = 0; r < lines.Count; r++)
            {
                var t = m.Get(r, 4).Trim(); if (t.Length == 0) continue;
                var q = t is "x" or "X" ? lines[r].l.Qty : Ui.TryParseNumber(t, out var v) ? v : double.NaN;
                if (double.IsNaN(q)) { await Ui.Message(w, $"SL tách dòng {r + 1} không phải số: {t}"); return; }
                picks.Add(new SplitPick(lines[r].s.Id, lines[r].i, q));
            }
            Slip? ns = null;
            try { ns = Svc.SplitLines(picks, ViTriService.Loai[Math.Max(0, loai.SelectedIndex)].Key, day.SelectedDate?.Date ?? DateTime.Today, note.Text ?? ""); }
            catch (SheetConflictException) { w.Close(); await Ui.Message(this, "Máy khác vừa sửa dữ liệu — tải lại rồi làm lại."); await LoadAsync(); return; }
            catch (BusinessException e) { await Ui.Message(w, e.Message, "Không tách được"); return; }
            w.Close();
            _status.Text = $"Đã tách ra phiếu {ns!.Id}: {ns.Lines.Count} dòng, SL {Q(ns.TongSl)}.";
            RenderAll();
        };
        await Ui.Show(this, w);
    }

    // =====================================================================================
    // Tab Lưu trữ theo ngày
    // =====================================================================================

    private Control BuildArchive()
    {
        var act = new ToolRow { Margin = new Thickness(0, 8, 0, 6) };
        if (_export)
        {
            act.Children.Add(Btn("🖨 In ngày…", async () => { if (CurArc() is { } g) await PrintItems(Svc.ArchiveItems(g), ArcTitle(g), ParseDay(g.Day)); }));
            act.Children.Add(Btn("Xuất Excel ngày…", async () => { if (CurArc() is { } g) await ExportItems(Svc.ArchiveItems(g), ArcTitle(g), ParseDay(g.Day), g.Loai); }, primary: true));
            act.Children.Add(Btn("Ảnh PNG…", async () => { if (CurArc() is { } g) await ShowImage(Svc.ArchiveItems(g), ArcTitle(g), ParseDay(g.Day), g.Loai); }));
        }
        act.Children.Add(Label("Mỗi ngày + mỗi loại phiếu tự gộp thành 1 phiếu (gộp theo Size).", size: 12));
        var split = Ui.MasterDetail("Danh sách theo ngày", Framed(_ag), _ag, "Chi tiết ngày", Framed(new SheetGrid(_adm) { SpareRows = 0 }), _arcInfo);
        var dock = new DockPanel();
        DockPanel.SetDock(act, Dock.Top);
        dock.Children.Add(act); dock.Children.Add(split);
        return dock;
    }
    private static DateTime ParseDay(string ymd) => DateTime.TryParseExact(ymd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Today;
    private static string ArcTitle(ArcGroup g) => ViTriService.Loai.FirstOrDefault(l => l.Key == g.Loai).Title ?? TitleDefault;
    private ArcGroup? CurArc() => _ag.Row >= 0 && _ag.Row < _arcRows.Count ? _arcRows[_ag.Row] : null;

    private Dictionary<string, int> _arcMissing = new();
    private void RenderArchive()
    {
        (_arcRows, _arcMissing) = Svc.Archive();
        _am.Load(new[]
        {
            new GridColumn("ngay", "Ngày", 90), new GridColumn("loai", "Loại", 140), new GridColumn("n", "Số phiếu", 70, "number"),
            new GridColumn("d", "Số dòng", 70, "number"), new GridColumn("sl", "Tổng SL", 80, "number"), new GridColumn("ids", "Các phiếu", 380),
        }, _arcRows.Select(g => new[] { Dmy(g.Day), LoaiLabel(g.Loai), g.Slips.Count.ToString(CultureInfo.InvariantCulture), g.NLines.ToString(CultureInfo.InvariantCulture), Raw(g.Tong), string.Join(", ", g.Slips.Select(s => s.Id)) }).ToList());
        _ag.Refresh();
        ShowArcDetail();
    }
    private void ShowArcDetail()
    {
        var g = CurArc();
        var items = g is null ? new List<ViewItem>() : Svc.ArchiveItems(g);
        _adm.Load(ItemCols(), items.Select(ItemRow).ToList());
        var miss = _arcMissing.Count == 0 ? "" : "  ⚠ Phiếu chưa chọn loại (chưa vào lưu trữ, xem ở tab Phiếu xuất): " + string.Join(", ", _arcMissing.OrderByDescending(k => k.Key).Select(k => $"{Dmy(k.Key)}: {k.Value}"));
        _arcInfo.Text = (g is null ? (_arcRows.Count == 0 ? "Chưa có ngày nào (phiếu hôm nay vẫn ở tab Phiếu xuất)." : "Chọn một ngày để xem phiếu gộp.")
            : $"{ArcTitle(g)} : {Dmy(g.Day)} · {items.Count} dòng · SL {Q(items.Sum(i => i.X))}") + miss;
    }

    // =====================================================================================
    // Tab Thống kê Size
    // =====================================================================================

    private Control BuildStats()
    {
        var bar = new ToolRow { Margin = new Thickness(0, 8, 0, 6) };
        bar.Children.Add(Label("Khoảng:")); bar.Children.Add(_range);
        _range.ItemsSource = Ranges.Select(r => r.Text).ToList(); _range.SelectedIndex = 0;
        _range.SelectionChanged += (_, _) => { if (!_loading) RenderStats(); };
        bar.Children.Add(_statInfo);
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar); dock.Children.Add(Framed(new SheetGrid(_stm) { SpareRows = 0 }));
        return dock;
    }
    private void RenderStats()
    {
        var key = Ranges[Math.Max(0, _range.SelectedIndex)].Key;
        var st = Svc.SizeStats(key, key == "today" ? Picks() : null);
        _stm.Load(new[]
        {
            new GridColumn("size", "Size (gộp)", 160), new GridColumn("dt", "Đặc tính", 220), new GridColumn("done", "Đã làm phiếu", 95, "number"),
            new GridColumn("draft", "Đang soạn", 85, "number"), new GridColumn("tong", "Tổng kiện", 85, "number"), new GridColumn("net", "Net (kg)", 100, "number"),
        }, st.Select(s => new[] { s.Label, string.Join(", ", s.Dts.OrderBy(x => x)), s.Done > 0 ? Raw(s.Done) : "", s.Draft > 0 ? Raw(s.Draft) : "", Raw(s.Done + s.Draft), Raw(Math.Round(s.Net, 2)) }).ToList());
        _statInfo.Text = $"{st.Count} size · {Q(st.Sum(s => s.Done))} kiện đã làm phiếu" + (st.Sum(s => s.Draft) > 0 ? $" + {Q(st.Sum(s => s.Draft))} đang soạn" : "") + $" · Net {Kg(st.Sum(s => s.Net))} kg";
    }

    // =====================================================================================
    // Tab Nạp file
    // =====================================================================================

    private void RenderFile()
    {
        _filePanel.Children.Clear();
        var i = Svc.Info;
        var card = new StackPanel { Spacing = 6 };
        card.Children.Add(Label("Số liệu đang dùng", bold: true, size: 15));
        card.Children.Add(Label(Svc.Rows.Count == 0 ? "Chưa nạp file nào."
            : $"File: {i.FileName}\nSheet: {i.Sheet} (tiêu đề ở dòng {i.HeaderRowNumber})\nNgày dữ liệu: {(i.DataDate.Length > 0 ? i.DataDate : "—")}\nSố dòng: {Svc.Rows.Count:#,0} · Tổng tồn: {Q(Svc.Rows.Sum(r => r.Sl))} · Net: {Kg(Svc.Rows.Sum(r => r.Sl * r.Tl))} kg\nNạp lúc: {Nap(i.ImportedAt)}"));
        var btns = new ToolRow { Margin = new Thickness(0, 6, 0, 0) };
        if (_edit)
        {
            btns.Children.Add(Btn("📂 Nạp file Tồn kho theo vị trí…", async () => await ImportFile(), primary: true));
            if (Svc.Rows.Count > 0) btns.Children.Add(Btn("Sửa ngày dữ liệu…", async () => await EditDataDate()));
        }
        btns.Children.Add(Btn("Tải file mẫu…", async () =>
        {
            var p = await Ui.SaveFile(this, "Lưu file mẫu", "Mau_Ton_Theo_Vi_Tri.xlsx", "xlsx", "Excel");
            if (p is not null && await Ui.Run(this, () => SaveTemplate(p))) Ui.Exported(this, p);
        }));
        if (_edit && Svc.Rows.Count > 0) btns.Children.Add(Btn("Xóa dữ liệu vị trí", async () => await ClearData(), danger: true));
        card.Children.Add(btns);
        _filePanel.Children.Add(new Border { Classes = { "card" }, Child = card });
        _filePanel.Children.Add(new Border
        {
            Classes = { "card" },
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12.5,
                Text = "Khi nạp file mới:\n" +
                       "• Phiếu đã trừ của lần nạp trước được ĐỐI CHIẾU với file mới: khớp (file đã giảm đúng), chưa giảm, hay lệch khác — hiện bảng kết quả ngay sau khi nạp.\n" +
                       "• Phiếu HÔM NAY đang trừ được trừ lại vào file mới (ô nào file đã giảm đúng số phiếu thì không trừ lại).\n" +
                       "• Phiếu các ngày trước bị khóa (số liệu file mới đã gồm phiếu đó). Nếu thực tế chưa xuất → “Mở khóa” ở tab Phiếu xuất.\n" +
                       "• Hàng đổi vị trí giữa 2 lần nạp (cùng mã + lô, ô cũ mất, ô mới xuất hiện) cũng được liệt kê.\n" +
                       "File cần các cột: Mã hàng, Vị trí, SL Tồn, T.Lượng (thêm Tên hàng, ĐVT, Lô nếu có). Dòng tiêu đề tự tìm trong 30 dòng đầu.",
            },
        });
        if (Svc.LastRecon is { } rc) _filePanel.Children.Add(new Border { Classes = { "card" }, Child = ReconView(rc) });
    }

    private async Task ImportFile()
    {
        var path = await Ui.OpenFile(this, "Chọn file Tồn kho theo vị trí", "xlsx", "Excel");
        if (path is null) return;
        if (!await Ui.ConfirmChangedFile(this, "vitri", path)) return;   // 1.72: đúng file đã nạp, chưa đổi → hỏi trước
        FileData? f = null;
        _status.Text = "Đang đọc file…";
        try { var (done, rd) = await Ui.Busy(this, $"Đang đọc file “{System.IO.Path.GetFileName(path)}”…", () => { var x = Svc.ReadFile(path); x.Sha = KhoLanh.Core.TonKho.NapExcel.Sha256(System.IO.File.ReadAllBytes(path)); return x; }, cancellable: true, op: "Đọc file Excel", detail: "Tồn theo vị trí", rows: x => x.Rows.Count); if (!done) { _status.Text = "Đã hủy đọc file."; return; } f = rd; }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex), "Không đọc được file"); return; }
        _status.Text = "";
        var today = Svc.Slips.Count(s => s.Deducted && IsToday(s));
        var text = $"Nạp {f!.Rows.Count:#,0} dòng từ “{f.FileName}” (sheet {f.Sheet}) thay cho số liệu hiện tại?" +
                   (today > 0 ? $"\n\n{today} phiếu HÔM NAY đang trừ sẽ được trừ lại vào file mới (ô file đã giảm đúng thì bỏ qua)." : "") +
                   (_editing is not null || _draft.Count > 0 ? "\n\nPhiếu đang soạn / đang sửa sẽ bị bỏ." : "");
        if (!await ConfirmImport(f, text)) return;
        ImportResult? r = null;
        _editing = null; _draft.Clear();
        try { var (_, rr) = await Ui.Busy(this, $"Đang nạp {f.Rows.Count:#,0} dòng…", () => Svc.Import(f), op: "Nạp file Excel", detail: "Tồn theo vị trí"); r = rr; }
        catch (Exception ex) { await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex), "Không nạp được"); return; }
        Ui.RememberFile("vitri", path);
        _status.Text = $"Đã nạp {f.Rows.Count:#,0} dòng ({DateTime.Now:HH:mm:ss})"; RenderAll();
        _tabs.SelectedIndex = 4;
        RenderFile();
        var msg = $"Đã nạp {r!.Rows:#,0} dòng." + (r.KeepSlips > 0 ? $"\nTrừ lại {r.KeepSlips} phiếu hôm nay" + (r.KeepSkipped > 0 ? $" ({r.KeepSkipped} ô file đã giảm sẵn — không trừ lại)" : "") + (r.KeepMissing > 0 ? $", {r.KeepMissing} dòng không còn trong file mới" : "") + "." : "");
        if (r.Recon is { } rc) msg += $"\n\nĐối chiếu phiếu với file mới: {rc.Khop} khớp · {rc.ChuaGiam} chưa giảm · {rc.Khac} lệch khác" + (rc.Moves.Count > 0 ? $" · {rc.Moves.Count} mã đổi vị trí" : "") + " — xem bảng bên dưới.";
        if (Svc.Info.DataDate.Length == 0) msg += "\n\nKhông đoán được ngày dữ liệu từ tên file — bấm “Sửa ngày dữ liệu…” để nhập.";
        await Ui.Message(this, msg, "Nạp file xong");
    }

    /// <summary>1.73 (N3): hộp xác nhận nạp + so cấu trúc file với mẫu lần nạp trước; lỗi đỏ → nút "Vẫn nạp" và hỏi lại.</summary>
    private async Task<bool> ConfirmImport(FileData f, string text)
    {
        var (truoc, mau) = MauFileBox.So(ViTriService.Db, KhoLanh.Core.TonKho.MauFile.Of(f), KhoLanh.Core.TonKho.MauFile.BatBuocViTri, Svc.Info.DataDate);
        var loi = KhoLanh.Core.TonKho.MauFile.CoLoi(mau);
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5 });
        if (MauFileBox.Build(truoc, mau) is { } mb) body.Children.Add(mb);
        var ok = false;
        var cancel = new Button { Content = "Đóng", IsCancel = true };
        var go = new Button { Content = loi ? "Vẫn nạp" : "Nạp", Classes = { "primary" }, IsDefault = !loi };
        body.Children.Add(Ui.Buttons(cancel, go));
        var w = Ui.Dialog("Nạp file vị trí", new ScrollViewer { Content = body, MaxHeight = 600 }, 600);
        cancel.Click += (_, _) => w.Close();
        go.Click += async (_, _) =>
        {
            if (loi && !await Ui.Confirm(w, MauFileBox.ConfirmText(mau), "File khác mẫu lần trước", "Vẫn nạp")) return;
            ok = true; w.Close();
        };
        await Ui.Show(this, w);
        return ok;
    }

    private Control ReconView(Recon rc)
    {
        var p = new StackPanel { Spacing = 6 };
        p.Children.Add(Label($"Đối chiếu phiếu đã trừ: “{rc.OldFile}” → “{rc.NewFile}”", bold: true, size: 14));
        p.Children.Add(Label($"{rc.Khop} khớp (file đã giảm đúng) · {rc.ChuaGiam} chưa giảm (file chưa trừ phiếu) · {rc.Khac} lệch khác"));
        if (rc.Items.Count > 0)
        {
            var m = new GridModel { ReadOnly = true };
            m.Load(new[]
            {
                new GridColumn("tt", "Kết quả", 95), new GridColumn("ma", "Mã hàng", 190), new GridColumn("lo", "Lô", 80), new GridColumn("vt", "Vị trí", 80),
                new GridColumn("p", "Phiếu", 150), new GridColumn("q", "SL phiếu", 70, "number"), new GridColumn("o", "Tồn cũ (đã trừ)", 100, "number"),
                new GridColumn("n", "Tồn file mới", 90, "number"), new GridColumn("l", "Lệch", 70, "number"),
            }, rc.Items.Select(i => new[]
            {
                i.Status switch { "khop" => "Khớp", "chuagiam" => "Chưa giảm", _ => "Lệch khác" }, ShortCode(i.Ma), i.Lo, i.Vt, string.Join(", ", i.Phieu), Raw(i.SlipQty),
                i.OldNet is { } o ? Raw(o) : "", i.InNew ? Raw(i.NewQty) : "(không có)", Raw(i.Lech),
            }).ToList());
            var items = rc.Items;
            var g = new SheetGrid(m) { SpareRows = 0, Height = Math.Min(360, 30 + items.Count * 26), CellBackground = (r, _) => r < items.Count ? items[r].Status switch { "khop" => KhopBg, "chuagiam" => ChuaBg, _ => KhacBg } : null };
            p.Children.Add(Framed(g));
        }
        if (rc.Moves.Count > 0)
        {
            p.Children.Add(Label($"Hàng đổi vị trí giữa 2 lần nạp ({rc.Moves.Count} mã):", bold: true));
            p.Children.Add(new TextBlock
            {
                FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                Text = string.Join("\n", rc.Moves.Take(40).Select(mv => $"• {ShortCode(mv.Ma)}{(mv.Lo.Length > 0 ? " (lô " + mv.Lo + ")" : "")}: {string.Join(", ", mv.Tu)} → {string.Join(", ", mv.Den)} · SL {Q(mv.SlCu)} → {Q(mv.SlMoi)}")) + (rc.Moves.Count > 40 ? "\n…" : ""),
            });
        }
        return p;
    }

    private async Task EditDataDate()
    {
        var t = await Ui.Prompt(this, "Ngày của số liệu vị trí (dd/mm/yyyy):", "Ngày dữ liệu", Svc.Info.DataDate);
        if (t is null) return;
        await Act(() => Svc.SetDataDate(t), "Đã lưu ngày dữ liệu");
    }
    private async Task ClearData()
    {
        if (!await Ui.Confirm(this, "Xóa toàn bộ số liệu vị trí đang dùng? Phiếu xuất vẫn giữ (sẽ bị khóa khi nạp file mới).", "Xóa dữ liệu vị trí", "Xóa")) return;
        _editing = null; _draft.Clear();
        await Act(Svc.Clear, "Đã xóa dữ liệu vị trí");
    }
}

/// <summary>Ảnh phiếu xuất (PNG, độ nét ×2) giống bản Excel: tiêu đề "TIÊU ĐỀ : dd/mm/yyyy", cột Mã hàng | Vị trí | SL xuất | Đặc tính | Hợp đồng, dòng Tổng.</summary>
public static class SlipImageRenderer
{
    private const double F = 24, FT = 28, PAD = 16, HT = 46, HH = 40, HR = 36, M = 2;
    private static readonly Typeface Reg = new("Times New Roman, Liberation Serif, Times, serif");
    private static readonly Typeface Bold = new(new FontFamily("Times New Roman, Liberation Serif, Times, serif"), FontStyle.Normal, FontWeight.Bold);
    private static FormattedText Text(string s, bool bold, double px) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? Bold : Reg, px, Brushes.Black);
    private static double W(string s, bool bold, double px) => s.Length == 0 ? 0 : Text(s, bold, px).Width;
    private static string N(double v) => v.ToString("#,0.##", KhoLanh.Core.Grid.NumFmt.Culture);

    private sealed class Surface : Control
    {
        public string Title = ""; public double[] Cols = Array.Empty<double>(); public List<string[]> Rows = new(); public string[] Total = Array.Empty<string>();
        public override void Render(DrawingContext dc)
        {
            dc.FillRectangle(Brushes.White, new Rect(Bounds.Size));
            var pen = new Pen(Brushes.Black, 1);
            var x = new double[Cols.Length + 1]; x[0] = M; for (var i = 0; i < Cols.Length; i++) x[i + 1] = x[i] + Cols[i];
            void Put(string s, bool bold, double px, char al, double x0, double x1, double yy, double h)
            {
                if (s.Length == 0) return;
                var t = Text(s, bold, px);
                var tx = al == 'c' ? (x0 + x1 - t.Width) / 2 : al == 'l' ? x0 + PAD : x1 - PAD - t.Width;
                dc.DrawText(t, new Point(tx, yy + (h - t.Height) / 2 + 1));
            }
            var y = (double)M;
            Put(Title, true, FT, 'l', x[0] - PAD + 4, x[^1], y, HT); y += HT;
            var heads = new[] { "Mã hàng", "Vị trí", "SL xuất", "Đặc tính", "Hợp đồng" };
            var y1 = y;
            for (var i = 0; i < 5; i++) Put(heads[i], true, F, 'c', x[i], x[i + 1], y, HH);
            y += HH;
            foreach (var r in Rows) { for (var i = 0; i < 5; i++) Put(r[i], false, F, i == 2 ? 'r' : i == 1 ? 'c' : 'l', x[i], x[i + 1], y, HR); y += HR; }
            Put(Total[0], true, F, 'l', x[0], x[2], y, HR); Put(Total[2], true, F, 'r', x[2], x[3], y, HR);
            var yt = y; y += HR;
            void H(double yy) => dc.DrawLine(pen, new Point(x[0], yy), new Point(x[^1], yy));
            H(y1); H(y1 + HH); H(yt); H(y);
            for (var k = 1; k < Rows.Count; k++) H(y1 + HH + k * HR);
            for (var k = 0; k < x.Length; k++) dc.DrawLine(pen, new Point(x[k], y1), new Point(x[k], k == 1 ? yt : y));   // ô "Tổng" gộp 2 cột đầu
        }
    }

    public static RenderTargetBitmap Render(List<ViTriService.ViewItem> items, string title)
    {
        var rows = items.Select(v => new[] { v.Ma, v.Vt, N(v.X), v.Dt, v.Hd }).ToList();
        var total = new[] { $"Tổng ({items.Count} dòng)", "", N(items.Sum(i => i.X)), "", "" };
        var heads = new[] { "Mã hàng", "Vị trí", "SL xuất", "Đặc tính", "Hợp đồng" };
        var cols = Enumerable.Range(0, 5).Select(i => Math.Ceiling(Math.Max(W(heads[i], true, F), rows.Select(r => W(r[i], false, F)).DefaultIfEmpty(0).Max()) + PAD * 2)).ToArray();
        cols[2] = Math.Max(cols[2], W(total[2], true, F) + PAD * 2);
        if (cols[0] + cols[1] < W(total[0], true, F) + PAD * 2) cols[0] = W(total[0], true, F) + PAD * 2 - cols[1];
        var w = Math.Max(cols.Sum(), W(title, true, FT) + 8) + M * 2;
        if (cols.Sum() < w - M * 2) cols[4] += w - M * 2 - cols.Sum();
        var h = HT + HH + rows.Count * HR + HR + M * 2;
        var s = new Surface { Title = title, Cols = cols, Rows = rows, Total = total, Width = w, Height = h };
        s.Measure(new Size(w, h)); s.Arrange(new Rect(0, 0, w, h));
        var bmp = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(w * 2), (int)Math.Ceiling(h * 2)), new Vector(192, 192));
        bmp.Render(s);
        return bmp;
    }
}

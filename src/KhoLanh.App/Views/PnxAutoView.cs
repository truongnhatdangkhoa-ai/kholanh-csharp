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
/// Tab "⚡ Tự động đã tạo" của Phiếu xuất kho (theo app cũ PC 2.90): mọi List sản xuất / Phiếu xuất kho do hệ thống tự tạo từ Lịch xuất —
/// lọc Phiếu / List, chỉ mục chưa xem, đánh dấu đã xem, mở từng mục. Kèm tuỳ chọn tự tạo: chỉ List (mặc định) · List + Phiếu · tắt.
/// </summary>
public sealed class PnxAutoView : UserControl
{
    private static PhieuKhoService Pnx => App.Services.PhieuKho;
    private static LichXuatService Lx => App.Services.LichXuat;
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush NewBg = AppTheme.Brush("#FFF4CC");

    private readonly Action<string> _openPhieu, _openList;
    private readonly GridModel _m = new() { ReadOnly = true };
    private readonly SheetGrid _g;
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "Tất cả", "Phiếu xuất kho", "List sản xuất" }, SelectedIndex = 0, FontSize = 12.5, MinWidth = 150 };
    private readonly CheckBox _onlyNew = new() { Content = "Chỉ hiện mục chưa xem", FontSize = 12.5 };
    private readonly TextBlock _sum = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button _seenAll;
    private List<PhieuKhoService.AutoItem> _all = new(), _shown = new();

    /// <summary>Số mục chưa xem thay đổi (để PhieuKhoView ghi số lên tên tab).</summary>
    public event Action<int>? UnseenChanged;

    public PnxAutoView(Action<string> openPhieu, Action<string> openList)
    {
        _openPhieu = openPhieu; _openList = openList;
        _g = new SheetGrid(_m) { SpareRows = 0, CellBackground = (r, _) => r < _shown.Count && _shown[r].IsNew ? NewBg : null };
        _g.DoubleTapped += (_, _) => Open();
        _kind.SelectionChanged += (_, _) => Render();
        _onlyNew.IsCheckedChanged += (_, _) => Render();
        _seenAll = Btn("✓ Đánh dấu đã xem tất cả", async () =>
        {
            if (await Ui.Run(this, () => { Pnx.LoadAutoLog(); Pnx.MarkSeen(); })) await LoadAsync();
        });

        var mode = new ComboBox { ItemsSource = new[] { "Chỉ tạo List sản xuất (như app cũ)", "Tạo List + Phiếu xuất kho", "Tắt tự tạo" }, FontSize = 12.5, MinWidth = 240 };
        mode.SelectedIndex = Lx.AutoMode switch { "phieu" => 1, "off" => 2, _ => 0 };
        mode.IsEnabled = Pnx.CanEdit;
        mode.SelectionChanged += (_, _) => { Lx.AutoMode = mode.SelectedIndex switch { 1 => "phieu", 2 => "off", _ => "list" }; };
        ToolTip.SetTip(mode, "Khi thêm lịch XUẤT (ngày ≥ hôm nay) hoặc đổi LSX / loại lịch — từ mọi đường: hộp Thêm, Thêm nhanh, Trợ lý, email — hệ thống tự tạo List cho từng LSX "
            + "(từ tồn kho; chưa có hàng thì List TRỐNG). Mỗi LSX chỉ tự tạo 1 lần; xóa lịch không xóa List / Phiếu.");

        var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        bar.Children.Add(_sum);
        bar.Children.Add(_kind); bar.Children.Add(_onlyNew);
        bar.Children.Add(Btn("📂 Mở mục đã chọn", () => { Open(); return Task.CompletedTask; }, primary: true));
        bar.Children.Add(_seenAll);
        bar.Children.Add(Btn("Tải lại", LoadAsync));
        if (Pnx.CanEdit) bar.Children.Add(Btn("⚡ Rà soát lịch 14 ngày tới", async () =>
        {
            List<string> r = new();
            if (await Ui.Run(this, () => { Lx.Load(); r = Lx.Reconcile(force: true); })) { await LoadAsync(); _status.Text = r.Count == 0 ? "Lịch xuất 14 ngày tới đều đã có List." : string.Join("\n", r); }
        }, "Lịch XUẤT trong 14 ngày tới có LSX chưa tự tạo List → tạo bù ngay"));
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        modeRow.Children.Add(new TextBlock { Text = "Tự tạo theo Lịch xuất:", VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
        modeRow.Children.Add(mode);

        var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 6) };
        top.Children.Add(bar); top.Children.Add(modeRow); top.Children.Add(_status);
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        root.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = _g });
        Content = root;
        AttachedToVisualTree += async (_, _) => { if (!Ui.SkipReload(this)) await LoadAsync(); };   // 1.136: quay lại trang dữ liệu chưa đổi → không đọc lại
    }

    private static Button Btn(string text, Func<Task> click, string? tip = null, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += async (_, _) => await click();
        return b;
    }

    public async Task LoadAsync()
    {
        try { await Task.Run(() => { Pnx.Load(); Pnx.LoadAutoLog(); _all = Pnx.AutoItems(); }); }
        catch (Exception e) { _status.Text = e is BusinessException ? e.Message : Ui.Describe(e); return; }
        Render();
    }

    private void Render()
    {
        int cP = _all.Count(i => i.Kind == 'p'), cL = _all.Count - cP, cN = _all.Count(i => i.IsNew);
        _sum.Text = $"⚡ {cP} phiếu xuất kho · {cL} List sản xuất · {cN} chưa xem";
        _seenAll.IsEnabled = cN > 0;
        var k = _kind.SelectedIndex;
        _shown = _all.Where(i => (k == 0 || (k == 1 ? i.Kind == 'p' : i.Kind == 'l')) && (_onlyNew.IsChecked != true || i.IsNew)).ToList();
        _m.Load(new[]
        {
            new GridColumn("loai", "Loại", 120), new GridColumn("ten", "Số phiếu / LSX", 150), new GridColumn("lsx", "LSX", 130), new GridColumn("khach", "Khách hàng", 140),
            new GridColumn("ngay", "Từ lịch ngày", 90), new GridColumn("at", "Tạo lúc", 120), new GridColumn("moi", "", 50), new GridColumn("note", "Ghi chú", 260),
        }, _shown.Select(i => new[] { i.Loai, i.Ten, i.Kind == 'p' ? i.Lsx : "", i.Khach, Dmy(i.NgayLich), DmyHm(i.At), i.IsNew ? "MỚI" : "", i.Note }));
        _g.Refresh();
        if (_shown.Count == 0)
            _status.Text = _all.Count == 0 ? "Chưa có Phiếu / List nào do hệ thống tự tạo. Thêm lịch XUẤT có LSX ở Lịch xuất / Trang Tổng quan → List tự có ở đây." : "Không có mục nào khớp bộ lọc.";
        else if (_status.Text is null || _status.Text.Length == 0 || _status.Text.StartsWith("Chưa có") || _status.Text.StartsWith("Không có"))
            _status.Text = "Mục MỚI tô vàng — mở từng mục để kiểm tra (Tên hàng, Mã hàng, số kiện…) rồi mới xuất Excel. Bấm đúp một dòng để mở.";
        UnseenChanged?.Invoke(cN);
    }

    private void Open()
    {
        if (_g.Row < 0 || _g.Row >= _shown.Count) { _status.Text = "Chọn 1 dòng trước."; return; }
        var it = _shown[_g.Row];
        try { Pnx.LoadAutoLog(); Pnx.MarkSeen(it.Kind, it.Id); } catch { /* đánh dấu lỗi không chặn mở */ }
        if (it.Kind == 'p') _openPhieu(it.Id); else _openList(it.Lsx);
        _ = LoadAsync();
    }

    private static string Dmy(string v) => v.Length >= 10 && v[4] == '-' ? $"{v[8..10]}/{v[5..7]}/{v[..4]}" : "";
    private static string DmyHm(string v) => DateTime.TryParse(v, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "";
}

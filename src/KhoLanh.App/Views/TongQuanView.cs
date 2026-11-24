using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.Core.LichXuat;
using KhoLanh.Core.Models;
using static KhoLanh.Core.LichXuat.LichXuatCore;

namespace KhoLanh.App.Views;

/// <summary>
/// TRANG TỔNG QUAN (theo Trang Chủ + Lịch xuất khẩu của app cũ — cùng cấu trúc chức năng, giao diện riêng cho bản C#):
/// • 4 thẻ số liệu: Tổng tồn · Tồn kho An An · Tồn kho gửi · Container xuất tháng này (đã xuất / dự kiến).
/// • Lịch xuất trong ngày (lùi / tới ngày): mỗi container 1 thẻ — LSX, khách, công ty, việc cần làm, số cont, số xe, tấn, trạng thái;
///   nút Tạo List / Tạo phiếu xuất (hoặc "Đã có" → mở thẳng), ✉ Cont (dán email Thông tin xe), Sửa. Bấm thẻ → chi tiết lịch.
/// • + Thêm lịch · ⚡ Nhập nhanh (dán kế hoạch "Ngày dd/mm: … LSX …") · ✉ Email (duyệt thư cập nhật lịch, tự quét Gmail).
/// • Lịch xuất gần đây / dự kiến (lọc An An / Thuận Phước, tìm), Việc cần làm (việc đến hạn, chuẩn bị còn thiếu, việc cho LSX chưa có lịch),
///   Thao tác nhanh (tùy chọn theo tài khoản). Tab Lịch tháng (danh sách theo ngày) và Thống kê (theo tháng trong năm).
/// </summary>
public sealed partial class TongQuanView : UserControl, IPage
{
    public string Title => "Tổng quan";
    public string Subtitle => _sub;
    private string _sub = "";

    internal static LichXuatService Svc => App.Services.LichXuat;
    internal static readonly IBrush Line_ = B("#D9E2EA"), Dark = B("#0B2A45"), Muted = B("#5D6B78");
    internal static IBrush B(string c) => AppTheme.Brush(c);
    internal static readonly Dictionary<string, (string Bg, string Fg, string Bar)> StStyle = new()
    {
        ["ok"] = ("#E6F4EA", "#1E7B34", "#3FB950"), ["run"] = ("#FFF1E0", "#B45309", "#F59E0B"), ["plan"] = ("#E3F2FB", "#155E8C", "#1F8FD1"), ["wait"] = ("#EEF1F5", "#4B5565", "#94A3B8"),
    };
    internal static readonly string[] Wd = { "Chủ nhật", "Thứ hai", "Thứ ba", "Thứ tư", "Thứ năm", "Thứ sáu", "Thứ bảy" };

    private int _dayOff;
    private string _rTab = "recent", _rCo = "all";
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private int _year = DateTime.Today.Year;
    private bool _loaded;
    private (LichXuatService.StockKpi AnAn, LichXuatService.StockKpi Gui)? _stock;

    private readonly Avalonia.Controls.Primitives.UniformGrid _kpi = new() { Columns = 4 };
    private readonly TextBlock _dayLb = new() { FontSize = 13, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _dayCnt = new() { FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _day = new() { Spacing = 6 };
    private readonly StackPanel _recent = new() { Spacing = 0 };
    private readonly TextBlock _recentNote = new() { FontSize = 11.5, Foreground = Muted };
    private readonly TextBox _rq = new() { Watermark = "Tìm LSX, khách, cont…", Width = 190, FontSize = 12.5 };
    private readonly WrapPanel _rSeg = new() { ItemSpacing = 4 }, _rCoBar = new() { ItemSpacing = 4 };
    private readonly StackPanel _todo = new() { Spacing = 6 };
    private readonly WrapPanel _quick = new() { ItemSpacing = 8, LineSpacing = 8 };
    private readonly Button _email;
    private readonly CalendarDatePicker _pick = new() { Width = 120, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy" };
    private readonly StackPanel _monthBody = new() { Spacing = 4 };
    private readonly TextBlock _monthLb = new() { FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center, MinWidth = 150, TextAlignment = TextAlignment.Center };
    private readonly StackPanel _stats = new() { Spacing = 8 };
    private readonly TextBlock _warn = new() { FontSize = 12, Foreground = B("#B45309"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private bool _picking;

    public TongQuanView()
    {
        _email = Btn("✉ Email", async () => await EmailDialog(), "Thư Gmail cập nhật lịch: chờ duyệt / đã xử lý / cài đặt");
        _email.IsVisible = Svc.CanMail;
        _pick.SelectedDateChanged += (_, _) => { if (_picking || _pick.SelectedDate is not { } d) return; _dayOff = (int)Math.Round((d.Date - DateTime.Today).TotalDays); PaintDay(); };
        var deb = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        deb.Tick += (_, _) => { deb.Stop(); PaintRecent(); };
        _rq.TextChanged += (_, _) => { deb.Stop(); deb.Start(); };

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Tổng quan", Content = Scroll(HomeTab()) });
        tabs.Items.Add(new TabItem { Header = "Lịch xuất theo tháng", Content = Scroll(MonthTab()) });
        tabs.Items.Add(new TabItem { Header = "Thống kê", Content = Scroll(StatsTab()) });
        tabs.SelectionChanged += (_, _) => { if (tabs.SelectedIndex == 1) PaintMonth(); else if (tabs.SelectedIndex == 2) PaintStats(); };
        Content = tabs;

        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) => { if (_dayOff == 0) PaintDay(); };
        Svc.Changed += OnChanged; Svc.EmailScanned += OnEmail;
        AttachedToVisualTree += async (_, _) => { timer.Start(); await LoadAsync(); };
        DetachedFromVisualTree += (_, _) => { timer.Stop(); Svc.Changed -= OnChanged; Svc.EmailScanned -= OnEmail; };
    }

    private void OnChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(PaintAll);
    private void OnEmail(LichXuatService.ScanResult r) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { PaintEmailBadge(); if (r.Auto + r.ContSet > 0) _ = LoadAsync(); });

    // ------------------------------------------------------------------ khung

    internal static Button Btn(string text, Func<Task> click, string? tip = null, bool primary = false, double fs = 12.5)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = fs };
        if (primary) b.Classes.Add("primary");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += async (_, _) => await click();
        return b;
    }
    private static ScrollViewer Scroll(Control c) => new() { Content = new Border { Padding = new Thickness(0, 8, 8, 16), Child = c }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    internal static Border Card(Control body, double pad = 14) => new() { Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(pad), Child = body };
    internal static TextBlock H2(string t) => new() { Text = t, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center };
    internal static Border Pill(string text, string bg, string fg, double fs = 11.5) => new()
    {
        Background = B(bg), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = fs, FontWeight = FontWeight.SemiBold, Foreground = B(fg) },
    };
    internal static string Tan(double kg) => (kg / 1000).ToString("#,0.00", CultureInfo.InvariantCulture);
    internal static string DayLabel(string iso) => IsIso(iso) ? Wd[(int)DateOf(iso).DayOfWeek] + ", " + Vn(iso) : iso;
    internal static Border CoBadge(string co) => co == "tp" ? Pill("THUẬN PHƯỚC", "#F3E8FF", "#6B21A8", 10.5) : Pill("AN AN", "#E0F2FE", "#075985", 10.5);
    internal static Border StPill(string st) { var s = StStyle.GetValueOrDefault(st, StStyle["plan"]); return Pill(StLabel.GetValueOrDefault(st, st), s.Bg, s.Fg); }

    private Control HomeTab()
    {
        // tiêu đề + nút xếp dòng tự do: màn hình hẹp (thanh bên mở) thì nút xuống dòng, không đè lên chữ
        var dayHead = new WrapPanel { ItemSpacing = 16, LineSpacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        var acts = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (Svc.CanEdit)
        {
            acts.Children.Add(Btn("+ Thêm lịch", async () => await EditDialog(null, DayKey()), primary: true));
            acts.Children.Add(Btn("⚡ Nhập nhanh", async () => await QuickDialog(), "Dán kế hoạch dạng \"Ngày 24/11: Xuất LSX 26.07.051 + 26.08.033; LSX 26.10.054\""));
        }
        acts.Children.Add(_email);
        acts.Children.Add(Btn("‹", () => { _dayOff--; PaintDay(); return Task.CompletedTask; }));
        acts.Children.Add(Btn("Hôm nay", () => { _dayOff = 0; PaintDay(); return Task.CompletedTask; }));
        acts.Children.Add(Btn("›", () => { _dayOff++; PaintDay(); return Task.CompletedTask; }));
        acts.Children.Add(_pick);
        var tl = new StackPanel { Spacing = 2 }; tl.Children.Add(H2("Lịch xuất trong ngày"));
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; sub.Children.Add(_dayLb); sub.Children.Add(_dayCnt); tl.Children.Add(sub);
        dayHead.Children.Add(tl); dayHead.Children.Add(acts);
        var dayBox = new StackPanel(); dayBox.Children.Add(dayHead); dayBox.Children.Add(_day);
        if (Svc.CanEdit) dayBox.Children.Add(new Button { Content = "＋ Thêm lịch ngày này", Margin = new Thickness(0, 6, 0, 0), FontSize = 12.5, Command = null }.Also(b => b.Click += async (_, _) => await EditDialog(null, DayKey())));

        foreach (var (k, t) in new[] { ("recent", "Lịch xuất gần đây"), ("up", "Lịch xuất dự kiến") })
            _rSeg.Children.Add(new ToggleButton { Content = t, Tag = k, FontSize = 12.5, Padding = new Thickness(10, 3) }.Also(b => b.Click += (_, _) => { _rTab = k; PaintRecent(); }));
        foreach (var (k, t) in new[] { ("all", "Tất cả"), ("an", "An An"), ("tp", "Thuận Phước") })
            _rCoBar.Children.Add(new ToggleButton { Content = t, Tag = k, FontSize = 12, Padding = new Thickness(8, 2) }.Also(b => b.Click += (_, _) => { _rCo = k; PaintRecent(); }));
        var rHead = new WrapPanel { ItemSpacing = 10, LineSpacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        rHead.Children.Add(_rSeg); rHead.Children.Add(_rCoBar); rHead.Children.Add(_rq);
        var recentBox = new StackPanel(); recentBox.Children.Add(rHead); recentBox.Children.Add(_recentNote); recentBox.Children.Add(_recent);

        var left = new StackPanel { Spacing = 12 };
        left.Children.Add(Card(dayBox)); left.Children.Add(Card(recentBox));

        var qHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var qEdit = Btn("✎", async () => await QuickEdit(), "Chọn các ô thao tác nhanh của tài khoản này"); DockPanel.SetDock(qEdit, Dock.Right); qHead.Children.Add(qEdit);
        qHead.Children.Add(H2("Thao tác nhanh"));
        var qBox = new StackPanel(); qBox.Children.Add(qHead); qBox.Children.Add(_quick);
        var tdBox = new StackPanel(); tdBox.Children.Add(H2("Việc cần làm").Also(t => t.Margin = new Thickness(0, 0, 0, 8))); tdBox.Children.Add(_todo);
        var right = new StackPanel { Spacing = 12 };
        right.Children.Add(Card(qBox)); right.Children.Add(Card(tdBox));

        var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,360") };
        cols.Children.Add(left); Grid.SetColumn(right, 2); cols.Children.Add(right);
        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(_kpi); root.Children.Add(_warn); root.Children.Add(cols);
        return root;
    }

    private string DayKey() => Iso(DateTime.Today.AddDays(_dayOff));

    // ------------------------------------------------------------------ dữ liệu

    private async Task LoadAsync()
    {
        try
        {
            await Task.Run(() => { Svc.Load(); _stock = Svc.Stock(); });
            _loaded = true;
        }
        catch (Exception e) { _warn.Text = "Không đọc được lịch xuất: " + e.Message; _warn.IsVisible = true; return; }
        _warn.Text = Svc.PnxError.Length > 0 ? "Phiếu xuất kho: " + Svc.PnxError : ""; _warn.IsVisible = _warn.Text.Length > 0;
        PaintAll();
    }

    private void PaintAll()
    {
        if (!_loaded) return;
        _sub = "Cập nhật " + DateTime.Now.ToString("HH:mm dd/MM/yyyy") + " · lịch xuất container dùng chung với app cũ";
        if (MainWindow.Of(this) is { } mw) mw.SetSubtitle(_sub);
        PaintKpi(); PaintDay(); PaintRecent(); PaintTodo(); PaintQuick(); PaintEmailBadge();
    }

    private void PaintKpi()
    {
        _kpi.Children.Clear();
        var (a, g) = _stock ?? (new(0, 0, "", false), new(0, 0, "", false));
        Border K(string title, string value, string unit, Control? sub, string accent, Func<Task>? click = null, double w = 250)
        {
            var s = new StackPanel { Spacing = 2 };
            s.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Muted });
            var v = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            v.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeight.SemiBold, Foreground = Dark });   // Băng sạch: số màu chữ, màu chỉ dùng cho trạng thái
            v.Children.Add(new TextBlock { Text = unit, FontSize = 12.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) });
            s.Children.Add(v);
            if (sub is not null) s.Children.Add(sub);
            var card = new Border { Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), Margin = new Thickness(0, 0, 10, 0), Child = s };
            if (click is not null) { card.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand); card.PointerPressed += async (_, _) => await click(); }
            return card;
        }
        Control Fresh(LichXuatService.StockKpi k)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            p.Children.Add(new TextBlock { Text = $"{k.Kg:#,0} kg · {k.Ma:#,0} mã", FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
            var iso = KhoLanh.Core.TroLy.TroLyNghiepVu.IsoDate(k.Date);
            if (iso.Length > 0)
            {
                var days = (int)Math.Round((DateTime.Today - DateOf(iso)).TotalDays);
                p.Children.Add(days >= 2 ? Pill($"{Dm(iso)} · {days} ngày trước", "#FFF4CC", "#8A5A00", 10.5) : Pill($"{Dm(iso)} · {(days <= 0 ? "hôm nay" : "hôm qua")}", "#E6F4EA", "#1E7B34", 10.5));
            }
            return p;
        }
        var nav = MainWindow.Of(this);
        _kpi.Children.Add(K("Tổng tồn", a.Has || g.Has ? Tan(a.Kg + g.Kg) : "—", "tấn", new TextBlock { Text = a.Has && g.Has ? "Tồn An An + tồn gửi kho" : a.Has ? "Chỉ có số liệu An An" : g.Has ? "Chỉ có số liệu kho gửi" : "Chưa nạp file tồn kho", FontSize = 12, Foreground = Muted }, "#0B2A45"));
        _kpi.Children.Add(K("Tồn kho An An", a.Has ? Tan(a.Kg) : "—", "tấn", a.Has ? Fresh(a) : null, "#1F8FD1", () => { nav?.Navigate("m1"); return Task.CompletedTask; }));
        _kpi.Children.Add(K("Tồn kho gửi", g.Has ? Tan(g.Kg) : "—", "tấn", g.Has ? Fresh(g) : null, "#0F766E", () => { nav?.Navigate("m2"); return Task.CompletedTask; }));
        var m = DateTime.Today.ToString("yyyy-MM"); var (done, plan, _, _) = MonthCont(Svc.Rows, m);
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions($"{Math.Max(done, 0.001)}*,{Math.Max(plan, 0.001)}*"), Height = 6, Margin = new Thickness(0, 4, 0, 2) };
        bar.Children.Add(new Border { Background = B("#3FB950"), CornerRadius = new CornerRadius(3, 0, 0, 3) });
        var b2 = new Border { Background = B("#BFDDF2"), CornerRadius = new CornerRadius(0, 3, 3, 0) }; Grid.SetColumn(b2, 1); bar.Children.Add(b2);
        var cs = new StackPanel(); cs.Children.Add(bar);
        cs.Children.Add(new TextBlock { Text = $"Đã xuất {done} cont · dự kiến {plan} cont", FontSize = 12, Foreground = Muted });
        _kpi.Children.Add(K($"Container xuất tháng {DateTime.Today:MM/yyyy}", (done + plan).ToString(), "cont", cs, "#B45309"));
    }

    // ------------------------------------------------------------------ lịch trong ngày

    private void PaintDay()
    {
        if (!_loaded) return;
        var k = DayKey();
        _dayLb.Text = DayLabel(k) + (_dayOff == 1 ? " · ngày mai" : _dayOff == -1 ? " · hôm qua" : _dayOff == 0 ? " · hôm nay" : "");
        _picking = true; _pick.SelectedDate = DateOf(k); _picking = false;
        var h = Svc.BuildHome(k);
        _dayCnt.Text = $"Đã xuất {h.Today.Count(x => x.St == "ok")} · Đang đóng {h.Today.Count(x => x.St == "run")} · Dự kiến {h.Today.Count(x => x.St == "plan")}";
        _day.Children.Clear();
        if (h.Today.Count == 0) { _day.Children.Add(new TextBlock { Text = "Không có lịch xuất ngày này.", Foreground = Muted, Margin = new Thickness(4, 8) }); return; }
        var i = 0;
        foreach (var x in h.Today) _day.Children.Add(DayCard(x, ++i));
    }

    private Control DayCard(ContRow x, int n)
    {
        var st = StStyle.GetValueOrDefault(x.St, StStyle["plan"]);
        // cột nút (cuối) co giãn: màn hình hẹp thì nút tự xuống dòng, không bị cắt
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("28,4,3*,100,100,80,110,2*"), Margin = new Thickness(0) };
        g.Children.Add(new TextBlock { Text = n.ToString(), FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        var bar = new Border { Background = B(st.Bar), CornerRadius = new CornerRadius(2) }; Grid.SetColumn(bar, 1); g.Children.Add(bar);
        var who = new StackPanel { Spacing = 2, Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var l1 = new WrapPanel { ItemSpacing = 6 };
        l1.Children.Add(new TextBlock { Text = x.Lsx, FontSize = 14.5, FontWeight = FontWeight.Bold, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center });
        l1.Children.Add(CoBadge(x.Co));
        if (x.Gio.Length > 0) l1.Children.Add(Pill("⏱ " + GioVn(x.Gio), "#EEF1F5", "#334155", 10.5));
        who.Children.Add(l1);
        if (x.Khach.Length > 0) who.Children.Add(new TextBlock { Text = x.Khach, FontSize = 12.5, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis });
        if (x.Viec is { } v) who.Children.Add(ViecTag(v));
        Grid.SetColumn(who, 2); g.Children.Add(who);
        Control Cell(string lab, string val, int col)
        {
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(new TextBlock { Text = lab, FontSize = 10.5, Foreground = Muted });
            s.Children.Add(new TextBlock { Text = val.Length > 0 ? val : "—", FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(s, col); return s;
        }
        g.Children.Add(Cell("Cont", x.Cont, 3)); g.Children.Add(Cell("Xe", x.Xe, 4)); g.Children.Add(Cell("Khối lượng", x.Kg > 0 ? Tan(x.Kg) + " tấn" : "", 5));
        var sp = StPill(x.St); sp.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(sp, 6); g.Children.Add(sp);
        var ac = new WrapPanel { ItemSpacing = 4, LineSpacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var px = Svc.CanEdit && Svc.CanPnx;
        if (x.HasList) ac.Children.Add(Btn("✔ Đã có List", () => OpenPnx(1, null, x.Codes.FirstOrDefault()), "Mở List sản xuất của LSX này", fs: 12));
        else if (x.St != "ok" && px) ac.Children.Add(Btn("Tạo List", async () => await RunPnx(x, false), "Tạo List sản xuất từ tồn kho (Thành Phẩm Chính)", fs: 12));
        if (x.HasPhieu) ac.Children.Add(Btn("✔ Đã có phiếu", () => OpenPnx(0, Svc.PhieuOf(Svc.Group(x.Ids))?.Id, null), "Mở Phiếu xuất kho của LSX này", fs: 12));
        else if (x.St != "ok" && px) ac.Children.Add(Btn("Tạo phiếu xuất", async () => await RunPnx(x, true), "Tạo Phiếu xuất kho từ List (chưa có List thì tạo từ tồn kho)", fs: 12));
        if (Svc.CanEdit) { ac.Children.Add(Btn("✉ Cont", async () => await ContMailDialog(x.Ids), "Dán email «Thông tin xe» để cập nhật số cont", fs: 12)); ac.Children.Add(Btn("✎", async () => await EditDialog(x.Ids[0], null), "Sửa lịch", fs: 12)); }
        Grid.SetColumn(ac, 7); g.Children.Add(ac);
        var card = new Border { Background = B(x.St == "ok" ? "#FAFCFB" : "#FFFFFF"), BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 8), Child = g, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        who.PointerPressed += async (_, _) => await DetailDialog(x.Ids);
        ToolTip.SetTip(who, "Bấm để xem chi tiết lịch, việc cần xử lý, hàng theo tồn kho");
        return card;
    }

    internal static Border ViecTag(ViecBadge v)
    {
        var (bg, fg, ic) = v.Level switch { "red" => ("#FDE7E7", "#B42318", "⚠ "), "amber" => ("#FFF4CC", "#8A5A00", "⏰ "), _ => ("#EEF1F5", "#4B5565", "○ ") };
        var b = Pill(ic + v.Text, bg, fg, 11); b.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(b, (v.Level == "red" ? "Cần xử lý ngay: " : v.Level == "amber" ? "Đến hạn hôm nay: " : "Còn việc, chưa đến hạn: ") + v.All);
        return b;
    }

    private Task OpenPnx(int tab, string? phieuId, string? lsx)
    {
        if (MainWindow.Of(this) is not { } mw) return Task.CompletedTask;
        if (!App.Services.Session.Can("m13")) return Ui.Message(this, "Tài khoản không có quyền xem Phiếu xuất kho.");
        mw.Show("m13", new PhieuKhoView(tab, phieuId, lsx));
        return Task.CompletedTask;
    }

    private async Task RunPnx(ContRow x, bool phieu)
    {
        List<string> notes = new(); string? id = null;
        if (!await Ui.RunAsync(this, async () => await Task.Run(() =>
        {
            if (phieu) { var r = Svc.CreatePhieu(x.Ids); notes = r.Notes; id = r.Phieu.Id; }
            else notes = Svc.CreateLists(x.Ids);
        }))) return;
        PaintDay();
        var open = await Ui.Confirm(this, string.Join("\n", notes), phieu ? "Tạo phiếu xuất kho" : "Tạo List sản xuất", phieu ? "Mở phiếu" : "Mở List");
        if (open) await OpenPnx(phieu ? 0 : 1, id, x.Codes.FirstOrDefault());
    }

    // ------------------------------------------------------------------ gần đây / dự kiến

    private void PaintRecent()
    {
        if (!_loaded) return;
        foreach (var c in _rSeg.Children.OfType<ToggleButton>()) c.IsChecked = (string)c.Tag! == _rTab;
        foreach (var c in _rCoBar.Children.OfType<ToggleButton>()) c.IsChecked = (string)c.Tag! == _rCo;
        var h = Svc.BuildHome(Iso(DateTime.Today));
        ((ToggleButton)_rSeg.Children[0]).Content = $"Lịch xuất gần đây ({h.Recent.Count})"; ((ToggleButton)_rSeg.Children[1]).Content = $"Lịch xuất dự kiến ({h.Upcoming.Count})";
        var q = Core.ChungTu.ChungTuService.StripAccents(_rq.Text ?? "");
        var list = (_rTab == "up" ? h.Upcoming : h.Recent).Where(x => (_rCo == "all" || x.Co == _rCo) && (q.Length == 0 || Core.ChungTu.ChungTuService.StripAccents(x.Lsx + " " + x.Khach + " " + x.Cont).Contains(q))).ToList();
        var shown = list.Take(12).ToList();
        _recentNote.Text = list.Count > 0 ? $"Hiện {shown.Count} / {list.Count} · " + (_rTab == "up" ? "chưa xuất, ngày gần nhất trước" : "đã xuất / đang đóng hàng, mới nhất trước") : "";
        _recent.Children.Clear();
        var head = Row(new[] { "#", "Ngày", "Số LSX", "Kho", "Khách hàng", "Tấn", "Cont", "Trạng thái" }, true, null);
        _recent.Children.Add(head);
        if (shown.Count == 0) { _recent.Children.Add(new TextBlock { Text = _rTab == "up" ? "Không có lịch xuất dự kiến." : "Chưa có lịch xuất nào.", Foreground = Muted, Margin = new Thickness(6, 8) }); return; }
        string? prev = null; var band = -1; var i = 0;
        foreach (var x in shown)
        {
            var first = x.Ngay != prev; if (first) { band++; prev = x.Ngay; }
            var r = Row(new[] { (++i).ToString(), first ? Dm(x.Ngay) : "″", x.Lsx, "", x.Khach, x.Kg > 0 ? Tan(x.Kg) : "—", x.Cont.Length > 0 ? x.Cont : "—", "" }, false, band % 2 == 1 ? "#F7FAFC" : null);
            var g = (Grid)r.Child!;
            var co = CoBadge(x.Co); co.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(co, 3); g.Children.Add(co);
            var sp = StPill(x.St); sp.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(sp, 7); g.Children.Add(sp);
            var ids = x.Ids; r.PointerPressed += async (_, _) => await DetailDialog(ids); r.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            _recent.Children.Add(r);
        }
    }

    private static Border Row(string[] cells, bool head, string? bg)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("30,60,150,110,*,70,120,120") };
        for (var c = 0; c < cells.Length; c++)
        {
            if (cells[c].Length == 0 && !head) continue;
            var t = new TextBlock { Text = cells[c], FontSize = head ? 11 : 12.5, FontWeight = head ? FontWeight.SemiBold : c == 2 ? FontWeight.SemiBold : FontWeight.Normal, Foreground = head ? Muted : c == 2 ? Dark : AppTheme.Text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0) };
            Grid.SetColumn(t, c); g.Children.Add(t);
        }
        return new Border { Child = g, Padding = new Thickness(2, head ? 4 : 6), Background = bg is null ? Brushes.Transparent : B(bg), BorderBrush = Line_, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    // ------------------------------------------------------------------ việc cần làm

    private void PaintTodo()
    {
        _todo.Children.Clear();
        var now = DateTime.Now; var t = Iso(now);
        // 1) việc của lịch đến hạn / quá hạn
        var hot = Svc.Rows.Select(r => (r, s: ViecState(r, now))).Where(x => x.s.Level is "red" or "amber").OrderBy(x => x.s.Level == "red" ? 0 : 1).ThenBy(x => x.r.Ngay).ToList();
        // 2) chuẩn bị còn thiếu (List / Phiếu / Cont) cho cont xuất hôm nay + ngày mai
        var prep = Svc.BuildHome(t).Upcoming.Concat(Svc.BuildHome(t).Today.Where(x => x.St != "ok")).Where(x => string.CompareOrdinal(x.Ngay, Iso(now.AddDays(1))) <= 0)
            .GroupBy(x => string.Join(",", x.Ids)).Select(g => g.First())
            .Select(x => (x, thieu: new[] { x.HasList ? "" : "List", x.HasPhieu ? "" : "Phiếu xuất kho", x.Cont.Length > 0 && !x.Cont.StartsWith("ghép") ? "" : "Số cont" }.Where(s => s.Length > 0).ToList()))
            .Where(x => x.thieu.Count > 0).ToList();
        if (hot.Count == 0 && prep.Count == 0 && Svc.Pending.Count == 0)
            _todo.Children.Add(new TextBlock { Text = "✓ Không có việc đến hạn.", Foreground = B("#1E7B34"), FontSize = 12.5 });
        foreach (var (r, s) in hot)
        {
            var b = TodoRow(s.Level == "red" ? "⚠" : "⏰", s.Level == "red" ? "#B42318" : "#8A5A00", $"{Label(r.Lsx)} · xuất {Dm(r.Ngay)}", string.Join("; ", s.Open.Select(v => v.T)));
            var id = r.Id; b.PointerPressed += async (_, _) => await DetailDialog(new List<string> { id });
            _todo.Children.Add(b);
        }
        foreach (var (x, thieu) in prep)
        {
            var b = TodoRow(x.Ngay == t ? "⚠" : "○", x.Ngay == t ? "#B42318" : "#155E8C", $"{x.Lsx} · xuất {(x.Ngay == t ? "hôm nay" : "ngày mai")}", "Chuẩn bị còn thiếu: " + string.Join(", ", thieu));
            var ids = x.Ids; b.PointerPressed += async (_, _) => await DetailDialog(ids);
            _todo.Children.Add(b);
        }
        // 3) việc cho LSX chưa có lịch
        if (Svc.Pending.Count > 0 || Svc.CanEdit)
        {
            _todo.Children.Add(new TextBlock { Text = $"LSX chưa có lịch ({Svc.Pending.Select(p => Sq(p.Lsx)).Distinct().Count()})", FontWeight = FontWeight.SemiBold, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0) });
            foreach (var p in Svc.Pending)
            {
                var row = new DockPanel();
                if (Svc.CanEdit) { var del = new Button { Content = "×", Padding = new Thickness(6, 0), FontSize = 12 }; ToolTip.SetTip(del, "Xóa việc này"); var pid = p.Id; del.Click += async (_, _) => { if (await Ui.Run(this, () => Svc.PendDelete(pid))) PaintTodo(); }; DockPanel.SetDock(del, Dock.Right); row.Children.Add(del); }
                var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                s.Children.Add(new TextBlock { Text = Label(p.Lsx), FontWeight = FontWeight.SemiBold, FontSize = 12.5 });
                s.Children.Add(new TextBlock { Text = p.T, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 210 });
                row.Children.Add(s); _todo.Children.Add(row);
            }
            if (Svc.CanEdit)
            {
                var lsx = new TextBox { Watermark = "Số LSX", Width = 100, FontSize = 12.5 };
                var tx = new TextBox { Watermark = "Việc cần xử lý (Enter)", FontSize = 12.5 };
                async Task Add()
                {
                    string? m = null;
                    if (await Ui.Run(this, () => m = Svc.PendAdd(lsx.Text ?? "", tx.Text ?? ""))) { tx.Text = ""; PaintAll(); if (m is not null && !m.Contains("chưa có lịch")) await Ui.Message(this, m); }
                }
                tx.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await Add(); };
                var add = Btn("+", Add, "Thêm việc");
                var f = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
                DockPanel.SetDock(lsx, Dock.Left); DockPanel.SetDock(add, Dock.Right); f.Children.Add(lsx); f.Children.Add(add); f.Children.Add(tx);
                _todo.Children.Add(f);
                _todo.Children.Add(new TextBlock { Text = "Khi thêm lịch xuất đúng số LSX, việc tự chuyển vào lịch và tính hạn (mặc định trước ngày xuất 1 ngày).", FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
            }
        }
    }

    private static Border TodoRow(string ic, string color, string title, string detail)
    {
        var g = new DockPanel();
        var i = new TextBlock { Text = ic, Foreground = B(color), FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(i, Dock.Left); g.Children.Add(i);
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 12.5 });
        s.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        g.Children.Add(s);
        return new Border { Child = g, Padding = new Thickness(6, 5), CornerRadius = new CornerRadius(6), Background = B("#F8FAFC"), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
    }

    // ------------------------------------------------------------------ thao tác nhanh

    private sealed record Quick(string Key, string Icon, string Label, string Perm, int Tab);
    private static readonly Quick[] Quicks =
    {
        new("anan", "📦", "Tồn kho An An", "m1", -1), new("gui", "🏬", "Tồn kho gửi", "m2", -1), new("xk", "🚢", "Phiếu xuất khẩu", "m13", 0),
        new("lsx", "📋", "List sản xuất", "m13", 1), new("cargo", "🚚", "Cargo list (TP · GC · GK)", "m13", 2), new("vt", "📍", "Tồn kho theo vị trí", "m7", -1),
        new("sodo", "🗺", "Sơ đồ kho", "m8", -1), new("dc", "⚖", "Đối chiếu hàng ngày", "m5", -1), new("kk", "🧮", "Đối chiếu kiểm kê", "m9", -1),
        new("ks", "🧪", "Kết quả kháng sinh", "m10", -1), new("nl", "✏", "Nhập liệu", "m6", -1), new("c3d", "🧊", "Container 3D", "m14", -1), new("scan", "📷", "Scan tài liệu", "m15", -1),
    };
    private static readonly string[] QuickDefault = { "anan", "gui", "xk", "lsx", "cargo", "vt" };
    private static string QuickKey => "home_quick|" + App.Services.Session.Username;
    private static List<string> QuickChosen()
    {
        var s = App.Services.Settings.Get(QuickKey);
        return s.Length == 0 ? QuickDefault.ToList() : s.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private void PaintQuick()
    {
        _quick.Children.Clear();
        var nav = MainWindow.Of(this);
        foreach (var q in QuickChosen().Select(k => Quicks.FirstOrDefault(x => x.Key == k)).Where(x => x is not null && App.Services.Session.Can(x.Perm)))
        {
            var s = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            s.Children.Add(new TextBlock { Text = q!.Icon, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center });
            s.Children.Add(new TextBlock { Text = q.Label, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 92 });
            var b = new Button { Content = s, Width = 104, Height = 74, Padding = new Thickness(4), HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Click += (_, _) => { if (nav is null) return; if (q.Tab >= 0) nav.Show("m13", new PhieuKhoView(q.Tab)); else nav.Navigate(q.Perm); };
            _quick.Children.Add(b);
        }
        if (_quick.Children.Count == 0) _quick.Children.Add(new TextBlock { Text = "Chưa chọn ô nào — bấm ✎.", Foreground = Muted, FontSize = 12.5 });
    }

    private async Task QuickEdit()
    {
        var chosen = QuickChosen().ToHashSet();
        var boxes = Quicks.Where(q => App.Services.Session.Can(q.Perm)).Select(q => new CheckBox { Content = q.Icon + "  " + q.Label, IsChecked = chosen.Contains(q.Key), Tag = q.Key }).ToList();
        var ok = new Button { Content = "Lưu", Classes = { "primary" }, IsDefault = true }; var reset = new Button { Content = "Mặc định" }; var no = new Button { Content = "Thôi", IsCancel = true };
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = "Chọn các ô hiện ở «Thao tác nhanh» (lưu riêng cho tài khoản này):", TextWrapping = TextWrapping.Wrap });
        foreach (var b in boxes) body.Children.Add(b);
        body.Children.Add(Ui.Buttons(reset, no, ok));
        var w = Ui.Dialog("Thao tác nhanh", body, 380);
        ok.Click += (_, _) => { App.Services.Settings.Set(QuickKey, string.Join(",", boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!))); w.Close(); };
        reset.Click += (_, _) => { App.Services.Settings.Set(QuickKey, ""); w.Close(); };
        no.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        PaintQuick();
    }

    // ------------------------------------------------------------------ lịch theo tháng

    private Control MonthTab()
    {
        var head = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(Btn("‹", () => { _month = _month.AddMonths(-1); PaintMonth(); return Task.CompletedTask; }));
        head.Children.Add(_monthLb);
        head.Children.Add(Btn("›", () => { _month = _month.AddMonths(1); PaintMonth(); return Task.CompletedTask; }));
        head.Children.Add(Btn("Tháng này", () => { _month = new(DateTime.Today.Year, DateTime.Today.Month, 1); PaintMonth(); return Task.CompletedTask; }));
        if (Svc.CanEdit)
        {
            head.Children.Add(Btn("+ Thêm lịch", async () => await EditDialog(null, Iso(DateTime.Today)), primary: true));
            head.Children.Add(Btn("⟳ Dò lại tồn (lịch chưa xuất)", async () =>
            {
                int n = 0; if (await Ui.RunAsync(this, async () => n = await Task.Run(() => Svc.Rescan()))) await Ui.Message(this, $"Đã dò lại tồn cho {n} lịch theo Load Data hiện tại.");
            }, "Nạp Load Data mới → số kiện / tấn trên lịch cập nhật theo"));
        }
        var s = new StackPanel(); s.Children.Add(head); s.Children.Add(_monthBody);
        return Card(s);
    }

    private void PaintMonth()
    {
        if (!_loaded) return;
        _monthLb.Text = "Tháng " + _month.ToString("MM/yyyy");
        _monthBody.Children.Clear();
        var m = _month.ToString("yyyy-MM"); var t = Iso(DateTime.Today); var tm = Iso(DateTime.Today.AddDays(1));
        var all = Svc.Rows.Where(r => MonthOf(r.Ngay) == m).ToList();
        var (done, plan, kd, kp) = MonthCont(Svc.Rows, m);
        _monthBody.Children.Add(new TextBlock { Text = $"{all.Count} lịch · {done + plan} container (đã xuất {done} · dự kiến {plan}) · {Tan(kd + kp)} tấn (đã xuất {Tan(kd)})", Foreground = Muted, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6) });
        if (all.Count == 0) { _monthBody.Children.Add(new TextBlock { Text = "Tháng này chưa có lịch xuất.", Foreground = Muted }); return; }
        foreach (var day in all.GroupBy(r => r.Ngay).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var (bg, tag) = day.Key == t ? ("#E3F2FB", " · hôm nay") : day.Key == tm ? ("#E6F4EA", " · ngày mai") : string.CompareOrdinal(day.Key, t) < 0 ? ("#F1F5F9", "") : ("#FFF8E1", "");
            var hd = new DockPanel();
            var hv = new TextBlock { Text = DayLabel(day.Key) + tag, FontWeight = FontWeight.Bold, Foreground = Dark, FontSize = 13 };
            if (Svc.CanEdit) { var add = Btn("+", async () => await EditDialog(null, day.Key), "Thêm lịch ngày này", fs: 11.5); DockPanel.SetDock(add, Dock.Right); hd.Children.Add(add); }
            var cont = day.GroupBy(ContKey).Count();
            hd.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { hv, new TextBlock { Text = $"{cont} cont · {Tan(day.Sum(r => r.Kg))} tấn", Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } } });
            _monthBody.Children.Add(new Border { Background = B(bg), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5), Margin = new Thickness(0, 6, 0, 2), Child = hd });
            var n = 0;
            foreach (var g in day.OrderBy(r => r.Id, StringComparer.Ordinal).GroupBy(ContKey))
            {
                var x = Mk(g.ToList(), DateTime.Now, Svc.StateOf);
                var r = Row(new[] { (++n).ToString(), x.Gio.Length > 0 ? GioVn(x.Gio) : "", x.Lsx, "", x.Khach, x.Kg > 0 ? Tan(x.Kg) : "—", x.Cont.Length > 0 ? x.Cont : "—", "" }, false, null);
                var gr = (Grid)r.Child!;
                var co = CoBadge(x.Co); co.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(co, 3); gr.Children.Add(co);
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }; sp.Children.Add(StPill(x.St)); if (x.Viec is { } v) sp.Children.Add(new TextBlock { Text = v.Level == "red" ? "⚠" : v.Level == "amber" ? "⏰" : "○", VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(sp, 7); gr.Children.Add(sp);
                var ids = x.Ids; r.PointerPressed += async (_, _) => await DetailDialog(ids); r.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                _monthBody.Children.Add(r);
            }
        }
    }

    // ------------------------------------------------------------------ thống kê

    private readonly TextBlock _yearLb = new() { FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center };
    private Control StatsTab()
    {
        var head = new WrapPanel { ItemSpacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(Btn("‹", () => { _year--; PaintStats(); return Task.CompletedTask; })); head.Children.Add(_yearLb);
        head.Children.Add(Btn("›", () => { _year++; PaintStats(); return Task.CompletedTask; }));
        var s = new StackPanel(); s.Children.Add(head); s.Children.Add(_stats);
        return Card(s);
    }

    private void PaintStats()
    {
        if (!_loaded) return;
        _yearLb.Text = "Năm " + _year; _stats.Children.Clear();
        var rows = Svc.Rows.Where(r => r.Ngay.StartsWith(_year + "-", StringComparison.Ordinal)).ToList();
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("90,110,110,110,120,120,*") };
        string[] hs = { "Tháng", "Cont đã xuất", "Cont dự kiến", "Tấn đã xuất", "An An (cont)", "Thuận Phước (cont)", "" };
        var maxKg = Enumerable.Range(1, 12).Select(mm => MonthCont(rows, $"{_year}-{mm:00}").KgDone).DefaultIfEmpty(0).Max();
        _stats.Children.Add(StatRow(hs, true, 0, maxKg));
        int td = 0, tp = 0; double tk = 0;
        for (var mm = 1; mm <= 12; mm++)
        {
            var m = $"{_year}-{mm:00}"; var (d, p, kd, _) = MonthCont(rows, m);
            var byCo = rows.Where(r => MonthOf(r.Ngay) == m).GroupBy(ContKey).Where(g => g.All(z => z.TrangThai == "ok")).GroupBy(g => CoOf(g.First())).ToDictionary(g => g.Key, g => g.Count());
            td += d; tp += p; tk += kd;
            _stats.Children.Add(StatRow(new[] { $"{mm:00}/{_year}", d.ToString(), p.ToString(), Tan(kd), byCo.GetValueOrDefault("an").ToString(), byCo.GetValueOrDefault("tp").ToString(), "" }, false, kd, maxKg));
        }
        _stats.Children.Add(StatRow(new[] { "Cả năm", td.ToString(), tp.ToString(), Tan(tk), "", "", "" }, true, 0, maxKg));
        var top = rows.Where(r => r.TrangThai == "ok" && r.Khach.Length > 0).GroupBy(r => r.Khach.Split(" · ")[0]).Select(g => (g.Key, Kg: g.Sum(r => r.Kg), N: g.GroupBy(ContKey).Count())).OrderByDescending(x => x.Kg).Take(8).ToList();
        if (top.Count > 0)
        {
            _stats.Children.Add(H2("Khách hàng xuất nhiều nhất năm " + _year).Also(t => t.Margin = new Thickness(0, 14, 0, 4)));
            foreach (var x in top) _stats.Children.Add(new TextBlock { Text = $"{x.Key} — {Tan(x.Kg)} tấn · {x.N} cont", FontSize = 12.5 });
        }
    }

    private static Control StatRow(string[] c, bool head, double kg, double max)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("90,110,110,110,120,120,*") };
        for (var i = 0; i < 6; i++) { var t = new TextBlock { Text = c[i], FontSize = head ? 11.5 : 12.5, FontWeight = head ? FontWeight.SemiBold : FontWeight.Normal, Foreground = head ? Muted : AppTheme.Text, Margin = new Thickness(4, 0) }; Grid.SetColumn(t, i); g.Children.Add(t); }
        if (!head && max > 0) { var b = new Border { Background = B("#3FB950"), Height = 10, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(0, 260 * kg / max), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(b, 6); g.Children.Add(b); }
        return new Border { Child = g, Padding = new Thickness(0, 5), BorderBrush = Line_, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private void PaintEmailBadge()
    {
        if (!Svc.CanMail) return;
        int n = 0; try { n = Svc.EmailRows().Count(r => r.TrangThai.Length == 0); } catch { }
        _email.Content = n > 0 ? $"✉ Email ({n})" : "✉ Email";
    }
}

internal static class UiExt
{
    public static T Also<T>(this T x, Action<T> f) { f(x); return x; }
}

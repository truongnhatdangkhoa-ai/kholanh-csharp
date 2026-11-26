using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.TroLy;

namespace KhoLanh.App.Views;

/// <summary>
/// Giai đoạn 5: tab «Các máy» của Trợ lý hệ thống (chỉ Quản trị). Tab con «Tình trạng các máy» = bảng mọi máy đã ghi ảnh chụp tình trạng vào
/// database dùng chung (bản app, lần cập nhật cuối / mất liên lạc &gt; 1 giờ, Nặng / Cần xem, CPU / RAM / ổ trống, trung bình 24 giờ) + khung phải
/// liệt kê cảnh báo của máy đang chọn. Tab con «Lịch sử» = số Nặng / Cần xem theo ngày của máy này (7 · 30 ngày).
/// </summary>
public sealed class TroLyMayView : UserControl
{
    private static TroLyService Svc => App.Services.TroLy;
    /// <summary>Nguồn dữ liệu (công cụ chụp thay bằng dữ liệu mẫu).</summary>
    internal Func<List<MayTinhTrang>> LoadMachines = () => Svc.Machines();
    internal Func<int, List<TroLyLichSu.Day?>> LoadHistory = d => Svc.History(d);
    internal Func<string> MyId = () => App.Services.Lan.MachineId;
    internal Func<DateTime> Now = () => DateTime.Now;

    private readonly GridModel _m = new() { ReadOnly = true };
    private readonly SheetGrid _g;
    private readonly StackPanel _detail = new() { Spacing = 6, Margin = new Thickness(14, 10, 16, 12) };
    private readonly TextBlock _info = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Tag = "keep", TextWrapping = TextWrapping.Wrap };
    private readonly Seg _range = new("7 ngày", "30 ngày");
    private readonly DayChart _chart = new() { Height = 280 };
    private readonly TextBlock _hInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Tag = "keep", TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _hSum = new() { Spacing = 4 };
    private List<MayTinhTrang> _list = new();
    private bool _loaded;

    /// <summary>Tab con đang mở (công cụ chụp đổi được).</summary>
    internal TabControl Tabs { get; }

    public TroLyMayView()
    {
        _g = new SheetGrid(_m) { SpareRows = 0, RowChecks = false };
        _g.SelectionChanged += PaintDetail;
        _g.CellBackground = (r, c) =>
        {
            if (r < 0 || r >= _list.Count) return null;
            var t = _list[r];
            if (c == 4 && TroLyMay.MatLienLac(t, Now())) return AppTheme.Brush("#FDE8E6");
            if (c == 5 && t.Do > 0) return AppTheme.Brush("#FDE8E6");
            if (c == 6 && t.Vang > 0) return AppTheme.Brush("#FFF2D6");
            if (c == 7 && t.Cpu is double cpu && PerfLevel.Cpu(cpu) > 0) return AppTheme.Brush(PerfLevel.Cpu(cpu) == 2 ? "#FDE8E6" : "#FFF2D6");
            if (c == 8 && t.Ram is double ram && PerfLevel.Ram(ram) > 0) return AppTheme.Brush(PerfLevel.Ram(ram) == 2 ? "#FDE8E6" : "#FFF2D6");
            if (c == 9 && t.DiskFree is double f && t.DiskTotal is double tt && PerfLevel.Disk(f, tt) > 0) return AppTheme.Brush(PerfLevel.Disk(f, tt) == 2 ? "#FDE8E6" : "#FFF2D6");
            return null;
        };

        // ---- tab con 1: tình trạng các máy
        var refresh = new Button { Content = Icon.Make("refresh", 15), Padding = new Thickness(8, 4) }; ToolTip.SetTip(refresh, "Đọc lại tình trạng các máy");
        refresh.Click += (_, _) => Refresh();
        var publish = new Button { Content = "Cập nhật máy này", Padding = new Thickness(10, 4), FontSize = 12.5 };
        ToolTip.SetTip(publish, "Ghi ngay tình trạng của máy này lên database dùng chung (bình thường tự ghi sau mỗi lần quét + mỗi 15 phút)");
        publish.Click += async (_, _) => { await Ui.Run(this, () => Svc.PublishStatus()); Refresh(); };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var tl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tl.Children.Add(refresh); tl.Children.Add(publish);
        DockPanel.SetDock(tl, Dock.Left); top.Children.Add(tl);
        _info.Margin = new Thickness(12, 0, 0, 0); top.Children.Add(_info);

        var left = new Border { BorderBrush = AppTheme.Brush("#D0D7E2"), BorderThickness = new Thickness(1), Child = _g, MinWidth = 420 };
        var right = new Border
        {
            BorderBrush = AppTheme.Brush("#D0D7E2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = AppTheme.Card, MinWidth = 320,
            Child = new ScrollViewer { Content = _detail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,10,2*"), MinHeight = 340 };
        Grid.SetColumn(right, 2); split.Children.Add(left); split.Children.Add(right);
        var p1 = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); p1.Children.Add(top); p1.Children.Add(split);

        // ---- tab con 2: lịch sử
        _range.Changed += _ => PaintHistory();
        var htop = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 10) };
        htop.Children.Add(_range); htop.Children.Add(_hInfo);
        var hcard = new Border { Classes = { "card" }, Padding = new Thickness(16, 14) };
        var hp = new StackPanel { Spacing = 10 };
        hp.Children.Add(new TextBlock { Text = "Số cảnh báo theo ngày (máy này — cao nhất trong ngày)", FontSize = 15, FontWeight = FontWeight.SemiBold });
        hp.Children.Add(_chart); hp.Children.Add(_hSum);
        hcard.Child = hp;
        var p2 = new StackPanel(); p2.Children.Add(htop); p2.Children.Add(hcard);
        var p2s = new ScrollViewer { Content = p2, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        Tabs = Ui.SubTabs(("Tình trạng các máy", p1), ("Lịch sử 7 / 30 ngày", p2s));
        // Ui.SubTabs đánh dấu SelectionChanged đã xử lý (không nổi lên tab cha) → nghe thẳng thuộc tính SelectedIndex
        Tabs.PropertyChanged += (_, e) => { if (e.Property == SelectingItemsControl.SelectedIndexProperty && Tabs.SelectedIndex == 1) PaintHistory(); };
        Content = Tabs;
        AttachedToVisualTree += (_, _) => { if (!_loaded) Refresh(); };
    }

    private static string T(DateTime? d) => d is { } x && x != default ? x.ToString("HH:mm dd/MM", CultureInfo.InvariantCulture) : "";
    private static string P(double? v) => v is double d ? d.ToString("0", CultureInfo.InvariantCulture) + "%" : "";
    private static string Ago(TimeSpan s) => s.TotalMinutes < 1 ? "vừa xong" : s.TotalMinutes < 60 ? $"{s.TotalMinutes:0} phút trước" : s.TotalHours < 48 ? $"{s.TotalHours:0} giờ trước" : $"{s.TotalDays:0} ngày trước";

    public void Refresh()
    {
        _loaded = true;
        try { _list = LoadMachines(); }
        catch (Exception e) { _list = new(); _info.Text = "Không đọc được tình trạng các máy: " + e.Message; }
        var now = Now(); var me = MyId();
        _m.Load(new[]
        {
            new GridColumn("may", "Máy", 136), new GridColumn("ver", "Bản app", 78), new GridColumn("vai", "Vai", 112), new GridColumn("at", "Cập nhật", 86),
            new GridColumn("tt", "Liên lạc", 140), new GridColumn("do", "Nặng", 62, "number"), new GridColumn("vang", "Cần xem", 82, "number"),
            new GridColumn("cpu", "CPU", 54), new GridColumn("ram", "RAM", 54), new GridColumn("o", "Ổ trống", 76), new GridColumn("tb24", "TB 24h CPU / RAM", 124),
        }, _list.Select(t => new[]
        {
            t.Name + (t.Id == me ? " (máy này)" : ""), t.Ver, t.Role, T(t.At),
            TroLyMay.MatLienLac(t, now) ? "Mất liên lạc · " + Ago(now - t.At).Replace(" trước", "") : Ago(now - t.At),
            t.Do.ToString(CultureInfo.InvariantCulture), t.Vang.ToString(CultureInfo.InvariantCulture), P(t.Cpu), P(t.Ram),
            t.DiskFree is double f ? f.ToString("#,0.#", CultureInfo.InvariantCulture) + " GB" : "",
            t.Cpu24 is null && t.Ram24 is null ? "" : P(t.Cpu24) + " / " + P(t.Ram24),
        }));
        _g.Refresh();
        if (_list.Count > 0)
        {
            var lost = _list.Count(t => TroLyMay.MatLienLac(t, now));
            _info.Text = $"{_list.Count} máy · " + (lost > 0 ? $"{lost} mất liên lạc (> 1 giờ không có tin) · " : "") +
                "mỗi máy tự ghi sau mỗi lần quét và mỗi 15 phút (qua LAN / Internet).";
            var sel = _list.FindIndex(t => t.Id == me);
            _g.Select(sel >= 0 ? sel : 0, 0);
        }
        else if (_info.Text is not { Length: > 0 } || !_info.Text.StartsWith("Không đọc", StringComparison.Ordinal))
            _info.Text = "Chưa có máy nào ghi tình trạng — bấm «Cập nhật máy này», hoặc đợi các máy quét / tự ghi (mỗi 15 phút).";
        PaintDetail();
        if (Tabs.SelectedIndex == 1) PaintHistory();
    }

    private static readonly Dictionary<string, (string Bg, string Fg, string Text)> Sev = new()
    {
        ["do"] = ("#FDE8E6", "#B42318", "NẶNG"), ["vang"] = ("#FFF2D6", "#9A5B00", "CẦN XEM"),
    };

    private static Border Pill(string bg, string fg, string text) => new()
    {
        Background = AppTheme.Brush(bg), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = AppTheme.Brush(fg) },
    };

    private void PaintDetail()
    {
        _detail.Children.Clear();
        var r = _g.Row;
        if (r < 0 || r >= _list.Count)
        {
            _detail.Children.Add(new TextBlock { Text = "Chọn một máy ở bảng bên trái để xem cảnh báo.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            return;
        }
        var t = _list[r]; var now = Now();
        _detail.Children.Add(new TextBlock { Text = t.Name, FontSize = 16, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        var pills = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        if (TroLyMay.MatLienLac(t, now)) pills.Children.Add(Pill("#FDE8E6", "#B42318", "MẤT LIÊN LẠC"));
        pills.Children.Add(Pill("#FDE8E6", "#B42318", $"{t.Do} NẶNG"));
        pills.Children.Add(Pill("#FFF2D6", "#9A5B00", $"{t.Vang} CẦN XEM"));
        pills.Children.Add(Pill("#E3F4EA", "#1E7B4A", $"{t.Xanh} ỔN"));
        _detail.Children.Add(pills);
        var sub = $"Bản {t.Ver} · {t.Role} · cập nhật {T(t.At)} ({Ago(now - t.At)})" + (t.ScanAt is { } sa ? $" · quét lúc {T(sa)}" : " · chưa quét");
        _detail.Children.Add(new TextBlock { Text = sub, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        if (t.Cpu is not null || t.Cpu24 is not null)
        {
            var perf = $"CPU {P(t.Cpu)} · RAM {P(t.Ram)}" + (t.DiskFree is double f ? $" · ổ trống {f:#,0.#} / {t.DiskTotal:#,0} GB" : "") +
                (t.Cpu24 is not null ? $" · trung bình 24h: CPU {P(t.Cpu24)}, RAM {P(t.Ram24)}" : "") +
                (t.LanMs is double l ? $" · LAN {l:0} ms" : "") + (t.GoogleMs is double g ? $" · Google {g:0} ms" : "");
            _detail.Children.Add(new TextBlock { Text = perf, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }
        _detail.Children.Add(new Border { Height = 1, Background = AppTheme.Brush("#E3E8EF"), Margin = new Thickness(0, 4) });
        if (t.Canh.Count == 0)
        {
            _detail.Children.Add(new TextBlock { Text = t.ScanAt is null ? "Máy này chưa quét Trợ lý lần nào (chỉ quét khi Quản trị đăng nhập)." : "Không có cảnh báo Nặng / Cần xem.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            return;
        }
        _detail.Children.Add(new TextBlock { Text = $"Cảnh báo ({t.Canh.Count}" + (t.Do + t.Vang > t.Canh.Count ? $" / {t.Do + t.Vang}" : "") + ")", FontWeight = FontWeight.SemiBold });
        foreach (var c in t.Canh)
        {
            var (bg, fg, text) = Sev.GetValueOrDefault(c.Sev, Sev["vang"]);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            row.Children.Add(Pill(bg, fg, text));
            var s = new StackPanel();
            s.Children.Add(new TextBlock { Text = c.Title, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            s.Children.Add(new TextBlock { Text = c.Mod, FontSize = 11.5, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(s, 1); row.Children.Add(s);
            _detail.Children.Add(row);
        }
    }

    internal void PaintHistory()
    {
        var days = _range.Selected == 1 ? 30 : 7;
        List<TroLyLichSu.Day?> h;
        try { h = LoadHistory(days); }
        catch (Exception e) { h = new(); _hInfo.Text = "Không đọc được lịch sử: " + e.Message; }
        _chart.Set(h, Now().Date, days);
        var had = h.Where(d => d is not null).Select(d => d!).ToList();
        _hInfo.Text = had.Count == 0 ? "Chưa có lần quét nào trong khoảng này (lịch sử lưu riêng máy này, giữ 30 ngày)."
            : $"{had.Sum(d => d.Scans)} lần quét trong {had.Count} ngày · lưu riêng máy này, giữ {TroLyLichSu.KeepDays} ngày";
        _hSum.Children.Clear();
        if (had.Count > 0)
        {
            var line = $"Trong {days} ngày: {had.Sum(d => d.Moi)} cảnh báo mới · {had.Sum(d => d.TaiPhat)} tái phát · {had.Sum(d => d.Dong)} đã hết · " +
                       $"Nặng cao nhất {had.Max(d => d.Do)} (ngày {had.OrderByDescending(d => d.Do).First().Date:dd/MM})";
            _hSum.Children.Add(new TextBlock { Text = line, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        }
    }

    /// <summary>Biểu đồ cột đôi theo ngày: Nặng (đỏ) · Cần xem (vàng). Ngày không quét để trống.</summary>
    private sealed class DayChart : Control
    {
        private List<TroLyLichSu.Day?> _days = new();
        private DateTime _today = DateTime.Today; private int _n = 7;
        public void Set(List<TroLyLichSu.Day?> days, DateTime today, int n) { _days = days; _today = today; _n = Math.Max(1, n); InvalidateVisual(); }

        private static readonly Typeface Face = new(FontFamily.Default);
        private static FormattedText Tx(string s, double size, IBrush b) => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, b);

        public override void Render(DrawingContext dc)
        {
            try { Draw(dc); } catch { /* lỗi vẽ không được làm hỏng cả khung vẽ của cửa sổ */ }
        }
        private void Draw(DrawingContext dc)
        {
            var b = Bounds; if (b.Width < 80 || b.Height < 60) return;
            var muted = AppTheme.Brush("#5D6B78"); var grid = new Pen(AppTheme.Brush("#E3E8EF"), 1); var axis = new Pen(AppTheme.Brush("#B9C3D0"), 1);
            var red = AppTheme.Brush("#D64545"); var amber = AppTheme.Brush("#E0A030");
            const double L = 36, R = 10, Top = 26, Bot = 24;
            var w = b.Width - L - R; var h = b.Height - Top - Bot;
            dc.FillRectangle(red, new Rect(L, 8, 14, 8)); var f1 = Tx("Nặng", 12, AppTheme.Brush("#1A2433")); dc.DrawText(f1, new Point(L + 18, 4));
            var lx = L + 18 + f1.Width + 18;
            dc.FillRectangle(amber, new Rect(lx, 8, 14, 8)); dc.DrawText(Tx("Cần xem", 12, AppTheme.Brush("#1A2433")), new Point(lx + 18, 4));
            var max = Math.Max(4, _days.Where(d => d is not null).Select(d => Math.Max(d!.Do, d.Vang)).DefaultIfEmpty(0).Max());
            var step = max <= 8 ? 2 : max <= 20 ? 5 : max <= 50 ? 10 : 25;
            var top = (int)Math.Ceiling(max / (double)step) * step;
            for (var v = 0; v <= top; v += step)
            {
                var y = Top + h - h * v / top;
                dc.DrawLine(v == 0 ? axis : grid, new Point(L, y), new Point(L + w, y));
                var ft = Tx(v.ToString(CultureInfo.InvariantCulture), 11, muted); dc.DrawText(ft, new Point(L - ft.Width - 6, y - ft.Height / 2));
            }
            var slot = w / _n; var bw = Math.Max(2, Math.Min(18, slot * 0.36));
            var every = _n <= 7 ? 1 : _n <= 14 ? 2 : 5;
            for (var i = 0; i < _n; i++)
            {
                var x0 = L + slot * i + slot / 2;
                var d = i < _days.Count ? _days[i] : null;
                if (d is not null)
                {
                    var hd = h * d.Do / top; var hv = h * d.Vang / top;
                    if (d.Do > 0) dc.FillRectangle(red, new Rect(x0 - bw - 1, Top + h - hd, bw, hd));
                    if (d.Vang > 0) dc.FillRectangle(amber, new Rect(x0 + 1, Top + h - hv, bw, hv));
                    if (d.Do == 0 && d.Vang == 0) dc.DrawEllipse(AppTheme.Brush("#1E7B4A"), null, new Point(x0, Top + h - 4), 3, 3);   // quét mà không có cảnh báo
                }
                if ((_n - 1 - i) % every == 0)
                {
                    var ft = Tx(_today.AddDays(-(_n - 1 - i)).ToString("dd/MM", CultureInfo.InvariantCulture), 11, muted);
                    dc.DrawText(ft, new Point(Math.Clamp(x0 - ft.Width / 2, L - 6, L + w - ft.Width), Top + h + 5));
                }
            }
            if (_days.All(d => d is null))
            {
                var ft = Tx("Chưa có lần quét nào trong khoảng này.", 13, muted); dc.DrawText(ft, new Point(L + (w - ft.Width) / 2, Top + h / 2 - ft.Height / 2));
            }
        }
    }
}

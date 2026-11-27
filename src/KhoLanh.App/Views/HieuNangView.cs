using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.TroLy;

namespace KhoLanh.App.Views;

/// <summary>
/// 1.100 (giai đoạn 2, phương án A): tab "Hiệu năng" của Trợ lý hệ thống — 1 trang tổng hợp: 4 thẻ số CPU / RAM / Ổ đĩa / Mạng
/// (vàng / đỏ theo <see cref="PerfLevel"/>), biểu đồ đường CPU / RAM (1 giờ · 24 giờ · 7 ngày · 30 ngày), Hiệu suất app (thao tác chậm
/// nhất dạng thanh, thời gian mở app / đăng nhập / đồng bộ) và Dữ liệu (dung lượng database, bảng lớn nhất).
/// Số liệu do <see cref="PerfMonitor"/> ghi mỗi phút vào hieu-nang.db (riêng máy này).
/// </summary>
public sealed class HieuNangView : UserControl
{
    private static readonly (string Label, TimeSpan Span)[] Ranges =
        { ("1 giờ", TimeSpan.FromHours(1)), ("24 giờ", TimeSpan.FromHours(24)), ("7 ngày", TimeSpan.FromDays(7)), ("30 ngày", TimeSpan.FromDays(30)) };
    private readonly Seg _range = new(Ranges.Select(r => r.Label).ToArray());
    private readonly WrapPanel _cards = new() { ItemSpacing = 12, LineSpacing = 12 };
    private readonly PerfChart _chart = new() { Height = 260 };
    private readonly StackPanel _app = new() { Spacing = 6 }, _data = new() { Spacing = 6 };
    private readonly TextBlock _info = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Tag = "keep" };
    private PerfStore? _store;
    /// <summary>Giờ hiện tại (công cụ chụp đặt cố định).</summary>
    internal Func<DateTime> Now = () => DateTime.Now;

    public HieuNangView()
    {
        _range.Selected = 1;   // mặc định 24 giờ
        _range.Changed += _ => Refresh();
        var refresh = new Button { Content = Icon.Make("refresh", 15), Padding = new Thickness(8, 4) }; ToolTip.SetTip(refresh, "Đọc lại số liệu");
        refresh.Click += (_, _) => Refresh();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var claude = new Button { Content = "📋 Sao chép báo cáo cho Claude", Padding = new Thickness(10, 4) };
        ToolTip.SetTip(claude, "Soạn sẵn prompt + số liệu hiệu năng THẬT của máy này (CPU / RAM / mạng, mốc mở app · đăng nhập · đồng bộ, thao tác chậm, database, cảnh báo) — dán vào Claude để kiểm tra app. Không có tên khách / người dùng.");
        claude.Click += async (_, _) => await CopyReport();
        left.Children.Add(_range); left.Children.Add(refresh); left.Children.Add(claude); left.Children.Add(_info);
        top.Children.Add(left);

        var chartCard = Card("CPU / RAM theo thời gian", _chart);
        var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12, Margin = new Thickness(0, 12, 0, 0) };
        var a = Card("Hiệu suất app (7 ngày)", _app); var d = Card("Dữ liệu", _data);
        cols.Children.Add(a); Grid.SetColumn(d, 1); cols.Children.Add(d);
        var body = new StackPanel { Margin = new Thickness(0, 4, 6, 8) };
        body.Children.Add(top); body.Children.Add(_cards); body.Children.Add(new Border { Height = 12 }); body.Children.Add(chartCard); body.Children.Add(cols);
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        AttachedToVisualTree += (_, _) => Refresh();
    }

    private PerfStore? Store
    {
        get
        {
            if (App.Perf?.Store is { } s) return s;
            try { return _store ??= new PerfStore(Path.Combine(App.DataFolder, PerfStore.FileName)); } catch { return null; }
        }
    }

    private static Border Card(string title, Control body)
    {
        var p = new StackPanel { Spacing = 10 };
        p.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
        p.Children.Add(body);
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), Child = p };
    }

    // ------------------------------------------------------------------ vẽ

    public void Refresh()
    {
        var st = Store; var now = Now();
        var span = Ranges[Math.Clamp(_range.Selected, 0, Ranges.Length - 1)].Span;
        PerfStore.Sample? last = null; List<PerfStore.Sample> pts = new(); List<PerfStore.Mark> marks = new();
        PerfStore.Sample? lastNet = null;
        try
        {
            if (st is not null)
            {
                last = st.Latest();
                pts = st.Range(now - span, now, 300);
                marks = st.Marks(null, now.AddDays(-7));
                lastNet = st.Range(now.AddHours(-1), now, 120).LastOrDefault(s => s.GoogleMs is not null || s.LanMs is not null);
            }
        }
        catch { /* file số liệu hỏng: vẽ trống */ }
        _info.Text = last is null ? "Chưa có số liệu (bộ đo ghi 1 mẫu mỗi phút khi app đang mở)." : $"Đo lúc {last.At:HH:mm dd/MM} · {pts.Count} điểm trên biểu đồ · giữ {PerfStore.KeepDays} ngày";
        PaintCards(last, lastNet);
        _chart.Set(new[]
        {
            new PerfChart.Series("CPU cả máy", "#1F6FD6", pts.Select(p => (p.At, p.CpuSys)).ToList()),
            new PerfChart.Series("CPU KhoLanh", "#5B4BC4", pts.Select(p => (p.At, p.CpuApp)).ToList()),
            new PerfChart.Series("RAM cả máy", "#1E7B4A", pts.Select(p => (p.At, p.RamSys)).ToList()),
        }, now - span, now);
        PaintApp(now, marks);
        PaintData();
    }

    /// <summary>1.172: soạn prompt + số liệu hiệu năng cho Claude (hỏi thêm mô tả hiện tượng — tùy chọn) rồi chép vào bộ nhớ tạm.</summary>
    private async Task CopyReport()
    {
        var note = await Ui.Prompt(this, "Mô tả ngắn hiện tượng người dùng thấy (tùy chọn — vd: “chuyển tab Tồn kho An An giật 3 giây”, “mở app chậm”). Để trống nếu không có.", "Báo cáo hiệu năng cho Claude", "");
        if (note is null) return;
        var st = Store; var now = Now();
        var rg = Ranges[Math.Clamp(_range.Selected, 0, Ranges.Length - 1)];
        var inp = new TroLyHieuNangBaoCao.Input
        {
            Version = KhoLanh.Core.Services.UpdateService.Current.ToString(), At = now, RangeLabel = rg.Label, Note = note, Cores = Environment.ProcessorCount,
            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, LanExpected = App.Services?.Lan.ServerAddress is not null, SlowMs = Perf.SlowMs,
        };
        string? text = null;
        var ok = await Ui.Run(this, () =>
        {
            try
            {
                if (st is not null) { inp.Last = st.Latest(); inp.Points = st.Range(now - rg.Span, now, 600); inp.Marks = st.Marks(null, now.AddDays(-7)); }
            }
            catch { /* file số liệu hỏng: báo cáo trống phần máy */ }
            try { inp.Slow = Perf.Read(App.DataFolder, now, 7); } catch { inp.Slow = new(); }
            try { inp.Db = DbStats.Read(App.Services.Db); } catch { inp.Db = null; }
            try { inp.Findings = App.Services.TroLy.Last?.Findings.ToList() ?? new(); } catch { inp.Findings = new(); }
            text = TroLyHieuNangBaoCao.Build(inp);
        });
        if (!ok || text is null) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) await cb.SetTextAsync(text);
        _info.Text = $"Đã sao chép báo cáo hiệu năng ({text.Length:#,0} ký tự) — dán vào Claude.";
    }

    private static readonly string[] LvBg = { "#FFFFFF", "#FFF2D6", "#FDE8E6" }, LvFg = { "#1A2433", "#9A5B00", "#B42318" };
    private static Border Kpi(string title, string value, string sub, int level, string icon)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(Icon.Make(icon, 15, AppTheme.Brush("#5D6B78")));
        head.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = AppTheme.Brush("#5D6B78") });
        var p = new StackPanel { Spacing = 4 };
        p.Children.Add(head);
        p.Children.Add(new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = AppTheme.Brush(LvFg[level]) });
        p.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = AppTheme.Brush("#5D6B78"), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Width = 250, Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = AppTheme.Brush(level == 0 ? "#E3E8EF" : LvFg[level]), Background = AppTheme.Brush(LvBg[level]), Child = p,
        };
    }

    private void PaintCards(PerfStore.Sample? s, PerfStore.Sample? net)
    {
        _cards.Children.Clear();
        if (s is null)
        {
            foreach (var (t, i) in new[] { ("CPU", "chart"), ("RAM", "layers"), ("Ổ đĩa", "save"), ("Mạng", "network") }) _cards.Children.Add(Kpi(t, "—", "chưa có số liệu", 0, i));
            return;
        }
        _cards.Children.Add(Kpi("CPU", $"{s.CpuSys:0}%", $"KhoLanh dùng {s.CpuApp:0.#}% · cả máy {s.CpuSys:0.#}%", PerfLevel.Cpu(s.CpuSys), "chart"));
        _cards.Children.Add(Kpi("RAM", $"{s.RamSys:0}%", $"KhoLanh dùng {s.RamAppMb:#,0} MB", PerfLevel.Ram(s.RamSys), "layers"));
        var pct = s.DiskTotalGb > 0 ? 100 * s.DiskFreeGb / s.DiskTotalGb : 0;
        _cards.Children.Add(Kpi("Ổ đĩa (ổ chứa dữ liệu)", $"{s.DiskFreeGb:#,0.#} GB trống", $"{pct:0}% của {s.DiskTotalGb:#,0} GB", PerfLevel.Disk(s.DiskFreeGb, s.DiskTotalGb), "save"));
        var lanExpected = App.Services?.Lan.ServerAddress is not null;
        var lanTxt = net?.LanMs is double l ? $"LAN {l:0} ms" : lanExpected ? "LAN: không nối được" : "LAN: máy này làm chủ / không dùng";
        var ggTxt = net?.GoogleMs is double g ? $"Google {g:0} ms" : "Google: không nối được";
        var lv = net is null ? 0 : Math.Max(PerfLevel.Net(net.LanMs, true, lanExpected), PerfLevel.Net(net.GoogleMs, false));
        var main = net?.GoogleMs is double gg ? $"{gg:0} ms" : net is null ? "—" : "mất mạng";
        _cards.Children.Add(Kpi("Mạng", main, net is null ? "đo mỗi 5 phút" : lanTxt + " · " + ggTxt, lv, "network"));
    }

    /// <summary>Thanh ngang: nhãn · thanh tỉ lệ · số.</summary>
    private static Control Bar(string label, double frac, string value, string color, string? tip = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*,Auto"), ColumnSpacing = 10 };
        var lb = new TextBlock { Text = label, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (tip is not null) ToolTip.SetTip(lb, tip);
        var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = AppTheme.Brush("#EEF1F5"), VerticalAlignment = VerticalAlignment.Center };
        var fill = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = AppTheme.Brush(color), HorizontalAlignment = HorizontalAlignment.Left };
        track.Child = fill;
        track.SizeChanged += (_, e) => fill.Width = Math.Max(4, e.NewSize.Width * Math.Clamp(frac, 0, 1));
        var v = new TextBlock { Text = value, FontSize = 12, Foreground = AppTheme.Brush("#5D6B78"), VerticalAlignment = VerticalAlignment.Center, MinWidth = 120, TextAlignment = TextAlignment.Right };
        g.Children.Add(lb); Grid.SetColumn(track, 1); g.Children.Add(track); Grid.SetColumn(v, 2); g.Children.Add(v);
        return g;
    }
    private static string Sec(double ms) => ms >= 60000 ? $"{ms / 60000:0.#} phút" : $"{ms / 1000:0.0} giây";
    private static TextBlock Sub(string t) => new() { Text = t, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = AppTheme.Brush("#5D6B78"), Margin = new Thickness(0, 6, 0, 0) };

    private void PaintApp(DateTime now, List<PerfStore.Mark> marks)
    {
        _app.Children.Clear();
        _app.Children.Add(Sub("Thời gian mở / đăng nhập / đồng bộ (trung vị · lần cuối)"));
        var any = false;
        foreach (var k in new[] { PerfMonitor.MarkMoApp, PerfMonitor.MarkDangNhap, PerfMonitor.MarkDongBoLan, PerfMonitor.MarkDongBoDrive, PerfMonitor.MarkDongBoDt })
        {
            if (PerfStore.Stat(marks, k) is not { } st) continue;
            any = true;
            _app.Children.Add(Bar(PerfMonitor.MarkLabel(k), st.Median / Math.Max(1, Math.Max(10000, st.Max)), $"{Sec(st.Median)} · cuối {Sec(st.Last)}",
                st.Median > 10000 ? "#B42318" : st.Median > 4000 ? "#C98A00" : "#1F6FD6", $"{st.Count} lần · chậm nhất {Sec(st.Max)}"));
        }
        if (!any) _app.Children.Add(new TextBlock { Text = "Chưa có mốc nào (ghi từ bản 1.100 khi mở app, đăng nhập, đồng bộ).", Classes = { "muted" }, FontSize = 12 });
        _app.Children.Add(Sub($"Thao tác chậm (> {Perf.SlowMs / 1000} giây, theo logs/cham.log) — trung bình · lớn nhất"));
        List<Perf.Entry> slow;
        try { slow = Perf.Read(App.DataFolder, now, 7); } catch { slow = new(); }
        var groups = slow.GroupBy(e => e.Op + (e.Detail.Length > 0 ? " · " + e.Detail : ""))
            .Select(g => (Name: g.Key, N: g.Count(), Avg: g.Average(x => (double)x.Ms), Max: g.Max(x => (double)x.Ms))).OrderByDescending(g => g.Avg).Take(8).ToList();
        if (groups.Count == 0) { _app.Children.Add(new TextBlock { Text = "Không có thao tác nào chậm trong 7 ngày.", Foreground = AppTheme.Brush("#1E7B4A"), FontSize = 12.5 }); return; }
        var top = groups.Max(g => g.Avg);
        foreach (var g in groups)
            _app.Children.Add(Bar(g.Name, g.Avg / top, $"{Sec(g.Avg)} · {Sec(g.Max)} · {g.N} lần", g.Avg > 10000 ? "#B42318" : g.Avg > 5000 ? "#C98A00" : "#5B4BC4", g.Name));
    }

    private void PaintData()
    {
        _data.Children.Clear();
        DbStats.Result r;
        try { r = DbStats.Read(App.Services.Db); }
        catch (Exception e) { _data.Children.Add(new TextBlock { Text = "Không đọc được database: " + e.Message, FontSize = 12.5 }); return; }
        var sum = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        sum.Inlines!.Add(new Avalonia.Controls.Documents.Run(DbStats.Human(r.FileBytes)) { FontWeight = FontWeight.Bold, FontSize = 22 });
        sum.Inlines.Add(new Avalonia.Controls.Documents.Run("  database" + (r.WalBytes > 0 ? $" (+ {DbStats.Human(r.WalBytes)} đang ghi)" : "")));
        _data.Children.Add(sum);
        _data.Children.Add(new TextBlock
        {
            Text = $"Dữ liệu module {DbStats.Human(r.DataBytes)} · lịch sử phiên bản bảng {DbStats.Human(r.HistoryBytes)} ({r.HistoryRows:#,0} bản) · nhật ký thao tác {r.AuditRows:#,0} dòng",
            FontSize = 12.5, Foreground = AppTheme.Brush("#5D6B78"), TextWrapping = TextWrapping.Wrap,
        });
        _data.Children.Add(Sub("Theo module"));
        var mx = Math.Max(1, r.Modules.FirstOrDefault()?.Bytes ?? 1);
        foreach (var m in r.Modules.Take(6))
            _data.Children.Add(Bar(KhoLanh.Core.Modules.LegacyImporter.LabelOf(m.Module), (double)m.Bytes / mx, $"{DbStats.Human(m.Bytes)} · {m.Tables} bảng", "#1F6FD6", m.Module));
        _data.Children.Add(Sub("Bảng lớn nhất"));
        var tx = Math.Max(1, r.Tables.FirstOrDefault()?.Bytes ?? 1);
        foreach (var t in r.Tables.Take(6))
            _data.Children.Add(Bar(KhoLanh.Core.Modules.LegacyImporter.LabelOf(t.Module) + " › " + t.Sheet, (double)t.Bytes / tx, $"{DbStats.Human(t.Bytes)} · {t.Rows:#,0} dòng", "#5B4BC4", t.Module + " / " + t.Sheet));
    }
}

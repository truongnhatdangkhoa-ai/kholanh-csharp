using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace KhoLanh.App.Controls;

/// <summary>
/// 1.100: biểu đồ đường tự vẽ cho tab Hiệu năng — nhiều đường % (0–100) theo thời gian, lưới 25 %, nhãn giờ / ngày, chú giải;
/// màu theo bộ Băng sạch qua <see cref="AppTheme.Brush"/> (tự đổi Sáng / Tối). Điểm cách nhau quá xa (máy tắt) thì đứt nét.
/// </summary>
public sealed class PerfChart : Control
{
    public sealed record Series(string Label, string Color, IReadOnlyList<(DateTime At, double V)> Points);

    private IReadOnlyList<Series> _series = Array.Empty<Series>();
    private DateTime _from, _to;
    private string _empty = "Chưa có số liệu — bộ đo ghi 1 mẫu mỗi phút khi app đang mở.";

    public void Set(IReadOnlyList<Series> series, DateTime from, DateTime to, string? empty = null)
    {
        _series = series; _from = from; _to = to; if (empty is not null) _empty = empty;
        InvalidateVisual();
    }

    private static readonly Typeface Face = new(FontFamily.Default);
    private static FormattedText T(string s, double size, IBrush b) => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, b);

    public override void Render(DrawingContext dc)
    {
        var b = Bounds; if (b.Width < 80 || b.Height < 60) return;
        var muted = AppTheme.Brush("#5D6B78"); var grid = new Pen(AppTheme.Brush("#E3E8EF"), 1); var axis = new Pen(AppTheme.Brush("#B9C3D0"), 1);
        const double L = 40, R = 12, Top = 26, Bot = 24;
        var w = b.Width - L - R; var h = b.Height - Top - Bot;
        // chú giải
        double lx = L;
        foreach (var s in _series)
        {
            var br = AppTheme.Brush(s.Color);
            dc.FillRectangle(br, new Rect(lx, 8, 14, 3));
            var ft = T(s.Label, 12, AppTheme.Brush("#1A2433")); dc.DrawText(ft, new Point(lx + 18, 2)); lx += 18 + ft.Width + 18;
        }
        // lưới %
        for (var p = 0; p <= 100; p += 25)
        {
            var y = Top + h - h * p / 100.0;
            dc.DrawLine(p == 0 ? axis : grid, new Point(L, y), new Point(L + w, y));
            var ft = T(p + "%", 11, muted); dc.DrawText(ft, new Point(L - ft.Width - 6, y - ft.Height / 2));
        }
        var span = (_to - _from).TotalSeconds; if (span <= 0) return;
        // nhãn thời gian (5 mốc)
        var fmt = span <= 2 * 86400 ? "HH:mm" : "dd/MM";
        for (var i = 0; i <= 4; i++)
        {
            var t = _from.AddSeconds(span * i / 4); var x = L + w * i / 4;
            var ft = T(t.ToString(fmt, CultureInfo.InvariantCulture), 11, muted);
            dc.DrawText(ft, new Point(Math.Clamp(x - ft.Width / 2, L - 10, L + w - ft.Width), Top + h + 5));
        }
        if (_series.All(s => s.Points.Count == 0))
        {
            var ft = T(_empty, 13, muted); dc.DrawText(ft, new Point(L + (w - ft.Width) / 2, Top + h / 2 - ft.Height / 2)); return;
        }
        // đường: đứt nét khi 2 điểm cách nhau > 3 lần bước trung bình (máy / app tắt)
        foreach (var s in _series)
        {
            if (s.Points.Count == 0) continue;
            var pen = new Pen(AppTheme.Brush(s.Color), 2, lineJoin: PenLineJoin.Round);
            var gap = s.Points.Count > 1 ? 3 * (s.Points[^1].At - s.Points[0].At).TotalSeconds / (s.Points.Count - 1) : double.MaxValue;
            Point? prev = null; DateTime prevAt = default;
            foreach (var (at, v) in s.Points)
            {
                var pt = new Point(L + w * Math.Clamp((at - _from).TotalSeconds / span, 0, 1), Top + h - h * Math.Clamp(v, 0, 100) / 100);
                if (prev is { } q && (at - prevAt).TotalSeconds <= Math.Max(gap, 180)) dc.DrawLine(pen, q, pt);
                else if (s.Points.Count == 1 || prev is not null) dc.DrawEllipse(AppTheme.Brush(s.Color), null, pt, 1.8, 1.8);
                prev = pt; prevAt = at;
            }
        }
    }
}

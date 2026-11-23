using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KhoLanh.Core.Scan;

namespace KhoLanh.App.Controls;

/// <summary>Khung chỉnh 4 góc cắt trang: hiện ảnh gốc vừa khung, kéo 4 chấm tròn (hoặc cả khung) để đặt lại vùng giấy.</summary>
public sealed class ScanCropCanvas : Control
{
    public Bitmap? Source { get; set; }
    public List<ScanPoint> Quad { get; set; } = ScanCore.DefaultQuad();
    public event Action? QuadChanged;
    private int _drag = -1; private Point _last;

    public ScanCropCanvas() { ClipToBounds = true; }

    private Rect ImgRect()
    {
        if (Source is null) return default;
        var s = Source.PixelSize; var b = Bounds.Size; var k = Math.Min((b.Width - 24) / s.Width, (b.Height - 24) / s.Height);
        if (k <= 0) k = 0.01;
        double w = s.Width * k, h = s.Height * k;
        return new Rect((b.Width - w) / 2, (b.Height - h) / 2, w, h);
    }

    private Point ToScreen(ScanPoint p, Rect r) => new(r.X + p.X * r.Width, r.Y + p.Y * r.Height);

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(new SolidColorBrush(Color.Parse("#2B3340")), new Rect(Bounds.Size));
        if (Source is null) return;
        var r = ImgRect(); dc.DrawImage(Source, new Rect(Source.Size), r);
        var pts = Quad.Select(q => ToScreen(q, r)).ToArray();
        // tối phần ngoài khung
        var outer = new StreamGeometry();
        using (var ctx = outer.Open())
        {
            ctx.SetFillRule(FillRule.EvenOdd);
            ctx.BeginFigure(r.TopLeft, true); ctx.LineTo(r.TopRight); ctx.LineTo(r.BottomRight); ctx.LineTo(r.BottomLeft); ctx.EndFigure(true);
            ctx.BeginFigure(pts[0], true); for (var i = 1; i < 4; i++) ctx.LineTo(pts[i]); ctx.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), null, outer);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#22C55E")), 2);
        for (var i = 0; i < 4; i++) dc.DrawLine(pen, pts[i], pts[(i + 1) % 4]);
        foreach (var p in pts)
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 34, 197, 94)), new Pen(Brushes.White, 2), p, 11, 11);
            dc.DrawEllipse(Brushes.White, null, p, 3, 3);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (Source is null) return;
        var r = ImgRect(); var pt = e.GetPosition(this); _last = pt;
        _drag = -1; double best = 22;
        for (var i = 0; i < 4; i++) { var s = ToScreen(Quad[i], r); var d = Math.Sqrt((s.X - pt.X) * (s.X - pt.X) + (s.Y - pt.Y) * (s.Y - pt.Y)); if (d < best) { best = d; _drag = i; } }
        if (_drag < 0 && InsideQuad(pt, r)) _drag = 4; // kéo cả khung
        if (_drag >= 0) e.Pointer.Capture(this);
    }

    private bool InsideQuad(Point p, Rect r)
    {
        var poly = Quad.Select(q => ToScreen(q, r)).ToArray(); var inside = false;
        for (int i = 0, j = 3; i < 4; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
        return inside;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); if (_drag < 0 || Source is null) return;
        var r = ImgRect(); var pt = e.GetPosition(this);
        static double C(double v) => Math.Max(0, Math.Min(1, v));
        if (_drag < 4) Quad[_drag] = new ScanPoint(C((pt.X - r.X) / r.Width), C((pt.Y - r.Y) / r.Height));
        else
        {
            double dx = (pt.X - _last.X) / r.Width, dy = (pt.Y - _last.Y) / r.Height;
            dx = Math.Max(-Quad.Min(q => q.X), Math.Min(1 - Quad.Max(q => q.X), dx)); dy = Math.Max(-Quad.Min(q => q.Y), Math.Min(1 - Quad.Max(q => q.Y), dy));
            Quad = Quad.Select(q => new ScanPoint(q.X + dx, q.Y + dy)).ToList();
        }
        _last = pt; InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag >= 0) { _drag = -1; e.Pointer.Capture(null); QuadChanged?.Invoke(); }
    }
}

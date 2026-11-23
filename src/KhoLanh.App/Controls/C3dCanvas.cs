using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using KhoLanh.Core.Container3D;

namespace KhoLanh.App.Controls;

/// <summary>
/// Khung 3D của Container 3D — vẽ bằng phép chiếu phối cảnh (không cần WebGL / GPU riêng):
/// kéo chuột trái = xoay, chuột phải / giữa = di chuyển, lăn chuột = phóng to; bấm 1 thùng = chọn; 4 góc nhìn có sẵn.
/// Mặt thùng bị thùng bên cạnh che kín được bỏ qua → vẽ mượt cả vài nghìn thùng. Hệ toạ độ cont: x dọc (0 = vách đầu), y ngang (0 = vách phải), z cao.
/// </summary>
public sealed class C3dCanvas : Control
{
    public C3dPlan? Plan { get; private set; }
    public List<Color> GroupColors { get; private set; } = new();
    public HashSet<int>? Tiers, Groups, Rows;     // null = hiện hết
    public int? SeqMax;
    public int? Selected;
    public int? SelRow;
    public bool WallRight = true, WallLeft, WallCeil, WallFront = true, RedLine = true, Dims = true, RowLabels, GroupLabels, Usable = true;

    public event Action<int?>? Picked;
    public event Action<int?, Point>? Hovered;

    // camera: góc quay quanh trục đứng (yaw), góc ngẩng (pitch), khoảng cách, điểm nhìn (mm, hệ cont)
    private double _yaw = -2.35, _pitch = 0.5, _dist = 16000;
    private Point3 _target;
    private Point? _drag; private bool _pan; private Point _down; private DateTime _downAt;
    private int? _hover;
    private readonly List<(Point[] poly, int box)> _drawn = new();
    private List<int> _visible = new();
    private readonly List<(int box, int face)> _faces = new();
    private bool _facesDirty = true;

    private readonly record struct Point3(double X, double Y, double Z)
    {
        public static Point3 operator -(Point3 a, Point3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Point3 operator +(Point3 a, Point3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Point3 operator *(Point3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
        public double Dot(Point3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public Point3 Cross(Point3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public Point3 Norm() { var l = Math.Sqrt(Dot(this)); return l > 0 ? this * (1 / l) : this; }
    }

    public C3dCanvas()
    {
        ClipToBounds = true; Focusable = true;
        Background = new SolidColorBrush(Color.Parse("#EEF2F7"));
    }

    public IBrush Background { get; set; }

    public void SetPlan(C3dPlan? plan, List<Color> colors)
    {
        var first = Plan is null || plan is null || Plan.Container.L != plan.Container.L || Plan.Container.W != plan.Container.W;
        Plan = plan; GroupColors = colors; Selected = null; SelRow = null; Tiers = Groups = Rows = null; SeqMax = null; _facesDirty = true;
        if (first) SetPreset("iso");
        InvalidateVisual();
    }

    public void SetColors(List<Color> colors) { GroupColors = colors; InvalidateVisual(); }
    public void Refilter() { _facesDirty = true; InvalidateVisual(); }

    /// <summary>Góc nhìn có sẵn: iso (tổng thể), top (từ trên), side (nhìn ngang), door (từ cửa).</summary>
    public void SetPreset(string v)
    {
        var c = Plan?.Container; double L = c?.L ?? 12000, W = c?.W ?? 2300, H = c?.H ?? 2500;
        _target = new Point3(L / 2, W / 2, H / 2.4);
        var span = Math.Max(L, Math.Max(W, H));
        switch (v)
        {
            case "top": _yaw = -Math.PI / 2; _pitch = 1.553; _dist = span * 0.95; break;
            case "side": _yaw = -Math.PI / 2; _pitch = 0.06; _dist = span * 0.95; break;
            case "door": _yaw = 0; _pitch = 0.16; _dist = Math.Max(W, H) * 3.2; _target = new Point3(L, W / 2, H / 2); break;
            default: _yaw = -2.35 + Math.PI; _pitch = 0.5; _dist = span * 1.08; break;
        }
        InvalidateVisual();
    }

    // ------------------------------------------------------------ chiếu
    private (Point3 eye, Point3 f, Point3 r, Point3 u) Cam()
    {
        var dir = new Point3(Math.Cos(_pitch) * Math.Cos(_yaw), Math.Cos(_pitch) * Math.Sin(_yaw), Math.Sin(_pitch));
        var eye = _target + dir * _dist; var f = (_target - eye).Norm();
        var up = new Point3(0, 0, 1); var r = f.Cross(up).Norm(); if (double.IsNaN(r.X) || r.Dot(r) < 1e-9) r = new Point3(1, 0, 0);
        var u = r.Cross(f).Norm();
        return (eye, f, r, u);
    }

    private Point? Project(Point3 p, (Point3 eye, Point3 f, Point3 r, Point3 u) cam, double focal, Size sz)
    {
        var d = p - cam.eye; var z = d.Dot(cam.f); if (z < 10) return null;
        // y cont tăng sang TRÁI khi đứng ở cửa nhìn vào → hệ toạ độ tay trái: đảo dấu trục ngang màn hình
        return new Point(sz.Width / 2 - d.Dot(cam.r) * focal / z, sz.Height / 2 - d.Dot(cam.u) * focal / z);
    }

    private static readonly Point3[] FaceNormals = { new(-1, 0, 0), new(1, 0, 0), new(0, -1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1) };

    private static Point3[] FaceCorners(C3dBox b, int f)
    {
        double x0 = b.X, x1 = b.X + b.Dx, y0 = b.Y, y1 = b.Y + b.Dy, z0 = b.Z, z1 = b.Z + b.Dz;
        return f switch
        {
            0 => new Point3[] { new(x0, y0, z0), new(x0, y1, z0), new(x0, y1, z1), new(x0, y0, z1) },
            1 => new Point3[] { new(x1, y0, z0), new(x1, y0, z1), new(x1, y1, z1), new(x1, y1, z0) },
            2 => new Point3[] { new(x0, y0, z0), new(x0, y0, z1), new(x1, y0, z1), new(x1, y0, z0) },
            3 => new Point3[] { new(x0, y1, z0), new(x1, y1, z0), new(x1, y1, z1), new(x0, y1, z1) },
            4 => new Point3[] { new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0) },
            _ => new Point3[] { new(x0, y0, z1), new(x0, y1, z1), new(x1, y1, z1), new(x1, y0, z1) },
        };
    }

    private bool IsVisible(C3dBox b, int i)
    {
        if (Tiers is not null && !Tiers.Contains(b.Tier)) return false;
        if (Groups is not null && !Groups.Contains(b.Group)) return false;
        if (Rows is not null && !Rows.Contains(b.Row)) return false;
        if (SeqMax is { } s && b.Seq > s) return false;
        return true;
    }

    // Mặt thùng bị thùng hiển thị bên cạnh che kín (cùng mặt phẳng, phủ trọn) → không cần vẽ.
    private void BuildFaces()
    {
        _faces.Clear(); _visible.Clear(); var p = Plan; if (p is null) return;
        for (var i = 0; i < p.Boxes.Count; i++) if (IsVisible(p.Boxes[i], i)) _visible.Add(i);
        var planes = new Dictionary<(int axis, long pos, int side), List<(double a0, double a1, double b0, double b1)>>();
        static long K(double v) => (long)Math.Round(v * 10);
        foreach (var i in _visible)
        {
            var b = p.Boxes[i];
            Add(0, b.X, 0, b.Y, b.Y + b.Dy, b.Z, b.Z + b.Dz); Add(0, b.X + b.Dx, 1, b.Y, b.Y + b.Dy, b.Z, b.Z + b.Dz);
            Add(1, b.Y, 0, b.X, b.X + b.Dx, b.Z, b.Z + b.Dz); Add(1, b.Y + b.Dy, 1, b.X, b.X + b.Dx, b.Z, b.Z + b.Dz);
            Add(2, b.Z, 0, b.X, b.X + b.Dx, b.Y, b.Y + b.Dy); Add(2, b.Z + b.Dz, 1, b.X, b.X + b.Dx, b.Y, b.Y + b.Dy);
        }
        void Add(int axis, double pos, int side, double a0, double a1, double b0, double b1)
        {
            var key = (axis, K(pos), side); if (!planes.TryGetValue(key, out var l)) planes[key] = l = new(); l.Add((a0, a1, b0, b1));
        }
        bool Covered(int axis, double pos, int side, double a0, double a1, double b0, double b1)
        {
            if (!planes.TryGetValue((axis, K(pos), 1 - side), out var l)) return false;
            foreach (var r in l) if (r.a0 <= a0 + 0.5 && r.a1 >= a1 - 0.5 && r.b0 <= b0 + 0.5 && r.b1 >= b1 - 0.5) return true;
            return false;
        }
        foreach (var i in _visible)
        {
            var b = p.Boxes[i];
            if (!Covered(0, b.X, 0, b.Y, b.Y + b.Dy, b.Z, b.Z + b.Dz)) _faces.Add((i, 0));
            if (!Covered(0, b.X + b.Dx, 1, b.Y, b.Y + b.Dy, b.Z, b.Z + b.Dz)) _faces.Add((i, 1));
            if (!Covered(1, b.Y, 0, b.X, b.X + b.Dx, b.Z, b.Z + b.Dz)) _faces.Add((i, 2));
            if (!Covered(1, b.Y + b.Dy, 1, b.X, b.X + b.Dx, b.Z, b.Z + b.Dz)) _faces.Add((i, 3));
            if (b.Z > 0.5 && !Covered(2, b.Z, 0, b.X, b.X + b.Dx, b.Y, b.Y + b.Dy)) _faces.Add((i, 4));
            if (!Covered(2, b.Z + b.Dz, 1, b.X, b.X + b.Dx, b.Y, b.Y + b.Dy)) _faces.Add((i, 5));
        }
        _facesDirty = false;
    }

    private static Color Mix(Color c, double k) => k >= 1 ? Color.FromRgb((byte)Math.Min(255, c.R + (255 - c.R) * (k - 1)), (byte)Math.Min(255, c.G + (255 - c.G) * (k - 1)), (byte)Math.Min(255, c.B + (255 - c.B) * (k - 1)))
        : Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));

    private static StreamGeometry Poly(IList<Point> pts)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open()) { ctx.BeginFigure(pts[0], true); for (var i = 1; i < pts.Count; i++) ctx.LineTo(pts[i]); ctx.EndFigure(true); }
        return g;
    }

    public override void Render(DrawingContext dc)
    {
        var sz = Bounds.Size; dc.FillRectangle(Background, new Rect(sz));
        _drawn.Clear();
        var p = Plan;
        if (p is null || !(p.Container.L > 0 && p.Container.W > 0 && p.Container.H > 0))
        {
            dc.DrawText(new FormattedText("Chưa có container / hàng để mô phỏng.", Ui.Vi, FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.Gray), new Point(16, 16));
            return;
        }
        if (_facesDirty) BuildFaces();
        var cam = Cam(); var focal = Math.Min(sz.Width, sz.Height) / (2 * Math.Tan(18 * Math.PI / 180));
        Point? P(double x, double y, double z) => Project(new Point3(x, y, z), cam, focal, sz);
        var c = p.Container; var sp = p.Space;
        var lineCol = new SolidColorBrush(Color.Parse("#5B6B82"));
        var thin = new Pen(lineCol, 1);
        // sàn
        DrawPoly(dc, new[] { P(0, 0, 0), P(c.L, 0, 0), P(c.L, c.W, 0), P(0, c.W, 0) }, new SolidColorBrush(Color.Parse("#C9D2DE")), thin);
        if (Usable)
        {
            var dash = new Pen(new SolidColorBrush(Color.Parse("#2E7D32")), 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            DrawPoly(dc, new[] { P(sp.X0, sp.Y0, 0.5), P(sp.X1, sp.Y0, 0.5), P(sp.X1, sp.Y1, 0.5), P(sp.X0, sp.Y1, 0.5) }, null, dash);
        }
        // vách (mờ) — vẽ trước thùng
        var wallFill = new SolidColorBrush(Color.FromArgb(40, 90, 120, 170));
        if (WallRight) DrawPoly(dc, new[] { P(0, 0, 0), P(c.L, 0, 0), P(c.L, 0, c.H), P(0, 0, c.H) }, wallFill, null);
        if (WallLeft) DrawPoly(dc, new[] { P(0, c.W, 0), P(c.L, c.W, 0), P(c.L, c.W, c.H), P(0, c.W, c.H) }, wallFill, null);
        if (WallFront) DrawPoly(dc, new[] { P(0, 0, 0), P(0, c.W, 0), P(0, c.W, c.H), P(0, 0, c.H) }, wallFill, null);
        if (WallCeil) DrawPoly(dc, new[] { P(0, 0, c.H), P(c.L, 0, c.H), P(c.L, c.W, c.H), P(0, c.W, c.H) }, wallFill, null);
        // thùng: vẽ từ xa tới gần (theo tâm thùng dọc hướng nhìn), mặt quay về phía camera
        var order = new List<(double d, int box, int face)>(_faces.Count);
        foreach (var (bi, f) in _faces)
        {
            var b = p.Boxes[bi]; var n = FaceNormals[f];
            var fc = new Point3(b.X + b.Dx / 2 + n.X * b.Dx / 2, b.Y + b.Dy / 2 + n.Y * b.Dy / 2, b.Z + b.Dz / 2 + n.Z * b.Dz / 2);
            if ((cam.eye - fc).Dot(n) <= 0) continue;
            var center = new Point3(b.X + b.Dx / 2, b.Y + b.Dy / 2, b.Z + b.Dz / 2);
            order.Add(((center - cam.eye).Dot(cam.f) * 1000 - (fc - cam.eye).Dot(cam.f) * 0.001, bi, f));
        }
        order.Sort((a, b) => b.d.CompareTo(a.d));
        var edge = new Pen(new SolidColorBrush(Color.FromArgb(150, 20, 30, 45)), 0.6);
        var selPen = new Pen(new SolidColorBrush(Color.Parse("#FF1744")), 2.2);
        var brushCache = new Dictionary<(int g, int f, bool s, bool h), IBrush>();
        foreach (var (_, bi, f) in order)
        {
            var b = p.Boxes[bi]; var pts = FaceCorners(b, f).Select(q => Project(q, cam, focal, sz)).ToArray();
            if (pts.Any(x => x is null)) continue;
            var poly = pts.Select(x => x!.Value).ToArray();
            var rowSel = SelRow is { } sr && b.Row == sr;
            var key = (b.Group, f, b.Sample, rowSel || _hover == bi);
            if (!brushCache.TryGetValue(key, out var br))
            {
                var col = b.Group < GroupColors.Count ? GroupColors[b.Group] : Color.Parse("#4A90D9");
                if (b.Sample) col = Color.Parse("#F5C400");
                var k = f == 5 ? 1.18 : f is 0 or 1 ? 0.86 : f == 4 ? 0.6 : 0.72;
                if (key.Item4) k = Math.Min(1.45, k + 0.3);
                brushCache[key] = br = new SolidColorBrush(Mix(col, k));
            }
            dc.DrawGeometry(br, Selected == bi ? selPen : edge, Poly(poly));
            _drawn.Add((poly, bi));
        }
        // khung cont
        void Ln(Point3 a, Point3 b2, IPen pen) { var A = Project(a, cam, focal, sz); var B = Project(b2, cam, focal, sz); if (A is { } x && B is { } y) dc.DrawLine(pen, x, y); }
        var frame = new Pen(lineCol, 1.3);
        var cs = new[] { new Point3(0, 0, 0), new Point3(c.L, 0, 0), new Point3(c.L, c.W, 0), new Point3(0, c.W, 0), new Point3(0, 0, c.H), new Point3(c.L, 0, c.H), new Point3(c.L, c.W, c.H), new Point3(0, c.W, c.H) };
        foreach (var (a, b2) in new[] { (0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6), (6, 7), (7, 4), (0, 4), (1, 5), (2, 6), (3, 7) }) Ln(cs[a], cs[b2], frame);
        // red line
        if (RedLine && sp.Red > 0 && sp.Red <= c.H + 1)
        {
            var red = new Pen(new SolidColorBrush(Color.Parse("#E53935")), 1.6) { DashStyle = new DashStyle(new double[] { 6, 3 }, 0) };
            var r = sp.Red;
            Ln(new(0, 0, r), new(c.L, 0, r), red); Ln(new(0, c.W, r), new(c.L, c.W, r), red); Ln(new(0, 0, r), new(0, c.W, r), red); Ln(new(c.L, 0, r), new(c.L, c.W, r), red);
            if (P(0, 0, r) is { } rp) Text(dc, "red line " + Ui.Num(r) + " mm", rp + new Point(4, -16), "#C62828", 11);
        }
        // nhãn
        if (Dims)
        {
            if (P(c.L / 2, 0, 0) is { } a) Text(dc, "Dài " + Ui.Num(c.L) + " mm", a + new Point(-40, 4), "#334155", 11);
            if (P(c.L, c.W / 2, 0) is { } bb) Text(dc, "Rộng " + Ui.Num(c.W), bb + new Point(4, 2), "#334155", 11);
            if (P(c.L, 0, c.H / 2) is { } hh) Text(dc, "Cao " + Ui.Num(c.H), hh + new Point(4, 0), "#334155", 11);
            if (P(0, c.W / 2, c.H) is { } fr) Text(dc, "VÁCH ĐẦU", fr + new Point(-28, -18), "#475569", 10.5);
            if (P(c.L, c.W / 2, c.H) is { } dr) Text(dc, "CỬA", dr + new Point(-12, -18), "#475569", 10.5);
        }
        if (RowLabels)
            foreach (var r in p.Rows) if (P((r.X0 + r.X1) / 2, c.W / 2, r.Top + 60) is { } q) Text(dc, r.Row.ToString(), q + new Point(-5, -14), "#0B2A45", 11, true);
        if (GroupLabels)
            foreach (var g in p.Groups.Where(g => g.Loaded > 0 && g.XStart is not null))
            {
                var top = p.Boxes.Where(b => b.Group == g.Index).Select(b => b.Z + b.Dz).DefaultIfEmpty(0).Max();
                if (P((g.XStart!.Value + g.XEnd!.Value) / 2, c.W / 2, top + 150) is { } q) Text(dc, (g.Sku.Code.Length > 0 ? g.Sku.Code : "#" + (g.Index + 1)) + (g.Sku.Size.Length > 0 ? " " + g.Sku.Size : ""), q + new Point(-30, -14), "#111827", 11, true);
            }
    }

    private static void DrawPoly(DrawingContext dc, Point?[] pts, IBrush? fill, IPen? pen)
    {
        if (pts.Any(p => p is null)) return;
        dc.DrawGeometry(fill, pen, Poly(pts.Select(p => p!.Value).ToArray()));
    }

    private static void Text(DrawingContext dc, string t, Point at, string color, double size, bool bold = false)
    {
        var ft = new FormattedText(t, Ui.Vi, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal), size, new SolidColorBrush(Color.Parse(color)));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), null, new Rect(at.X - 2, at.Y - 1, ft.Width + 4, ft.Height + 2), 3, 3);
        dc.DrawText(ft, at);
    }

    // ------------------------------------------------------------ chuột
    private int? HitTest(Point pt)
    {
        for (var i = _drawn.Count - 1; i >= 0; i--) if (Inside(_drawn[i].poly, pt)) return _drawn[i].box;
        return null;
    }

    private static bool Inside(Point[] poly, Point p)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
        return inside;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus();
        var pp = e.GetCurrentPoint(this);
        _drag = pp.Position; _down = pp.Position; _downAt = DateTime.Now; _pan = !pp.Properties.IsLeftButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.ClickCount == 2) { SetPreset("iso"); _drag = null; }
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        if (_drag is { } d)
        {
            var dx = pt.X - d.X; var dy = pt.Y - d.Y; _drag = pt;
            if (_pan)
            {
                var cam = Cam(); var k = _dist / Math.Max(200, Math.Min(Bounds.Width, Bounds.Height)) * 0.9;
                _target = _target + cam.r * (dx * k) + cam.u * (dy * k);
            }
            else { _yaw -= dx * 0.008; _pitch = Math.Clamp(_pitch + dy * 0.006, -0.05, 1.555); }
            InvalidateVisual();
            return;
        }
        var h = HitTest(pt);
        if (h != _hover) { _hover = h; InvalidateVisual(); }
        Hovered?.Invoke(h, pt);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pt = e.GetPosition(this);
        if (_drag is not null && Math.Abs(pt.X - _down.X) < 4 && Math.Abs(pt.Y - _down.Y) < 4 && (DateTime.Now - _downAt).TotalMilliseconds < 500 && e.InitialPressMouseButton == MouseButton.Left)
        {
            var hit = HitTest(pt); Selected = hit; InvalidateVisual(); Picked?.Invoke(hit);
        }
        _drag = null; e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hover is not null) { _hover = null; InvalidateVisual(); } Hovered?.Invoke(null, default); }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _dist = Math.Clamp(_dist * (e.Delta.Y > 0 ? 1 / 1.12 : 1.12), 800, 120000); InvalidateVisual(); e.Handled = true;
    }
}

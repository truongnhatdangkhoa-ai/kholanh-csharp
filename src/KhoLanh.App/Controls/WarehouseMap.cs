using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using static KhoLanh.Core.SoDo.SoDoCore;

namespace KhoLanh.App.Controls;

/// <summary>
/// Sơ đồ kho vẽ tay: mỗi dãy là 1 khối (tầng × khoang), tầng cao ở trên như kệ thật.
/// Ô tô màu theo trạng thái; cột nhỏ bên phải mỗi tầng là mức đầy pallet của tầng. Bấm ô → <see cref="Picked"/>.
/// </summary>
public sealed class WarehouseMap : Control
{
    public sealed record Block(string Khu, int D, int Nt, int Nk, bool Locked, Dictionary<(int T, int K), Pos> Cells);
    public sealed record Section(string Title, List<Block> Blocks);

    public List<Section> Sections { get; private set; } = new();
    public Func<Pos, string>? CellText { get; set; }
    public Func<Pos, bool>? Hit { get; set; }                     // null = không tìm
    public Func<string, (int Pct, string Lv, int Used, int Nk)>? TangInfo { get; set; }
    public Func<Pos, bool>? IsOverPos { get; set; }
    public Pos? Selected { get; private set; }
    public event Action<Pos>? Picked;

    private const double CW = 56, CH = 24, LW = 24, HH = 22, BarW = 7, GapX = 16, GapY = 14, SecH = 30, Pad = 6;
    private readonly List<(Rect R, Pos P)> _cells = new();
    private readonly List<(Rect R, Block B)> _blocks = new();
    private readonly List<(Point At, string Text)> _titles = new();
    private double _lastWidth = -1;

    public static readonly IBrush EmptyBg = Brushes.White;
    public static readonly IBrush OneBg = new SolidColorBrush(Color.Parse("#CDEBD3"));
    public static readonly IBrush ManyBg = new SolidColorBrush(Color.Parse("#FFE2A0"));
    public static readonly IBrush OverBg = new SolidColorBrush(Color.Parse("#F6B5A8"));
    private static readonly IBrush Grid = new SolidColorBrush(Color.Parse("#C9D3DE"));
    private static readonly IBrush Head = new SolidColorBrush(Color.Parse("#0B2A45"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#5D6B78"));
    private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));
    private static readonly IBrush BlockBg = new SolidColorBrush(Color.Parse("#F5F8FB"));
    private static readonly IBrush LockBg = new SolidColorBrush(Color.Parse("#EEF0F3"));
    private static readonly IPen SelPen = new Pen(new SolidColorBrush(Color.Parse("#0B5394")), 3);
    private static readonly IPen HitPen = new Pen(new SolidColorBrush(Color.Parse("#1F8FD1")), 2.2);
    private static readonly IPen OverPen = new Pen(new SolidColorBrush(Color.Parse("#C0392B")), 1.6);
    private static readonly IPen CellPen = new Pen(Grid, 1);
    private static readonly IPen BlockPen = new Pen(new SolidColorBrush(Color.Parse("#AAB8C6")), 1);
    public static IBrush LevelBrush(string lv) => lv switch
    {
        "over" => new SolidColorBrush(Color.Parse("#C0392B")),
        "full" => new SolidColorBrush(Color.Parse("#E67E22")),
        "near" => new SolidColorBrush(Color.Parse("#F1C40F")),
        "ok" => new SolidColorBrush(Color.Parse("#4CAF50")),
        _ => new SolidColorBrush(Color.Parse("#DDE3EA")),
    };

    public WarehouseMap() { Cursor = new Cursor(StandardCursorType.Hand); ClipToBounds = true; }

    public void SetData(List<Section> sections, Pos? selected)
    {
        Sections = sections; Selected = selected; _lastWidth = -1;
        InvalidateMeasure(); InvalidateVisual();
    }
    public void Select(Pos? p) { Selected = p; InvalidateVisual(); }
    public Rect? CellRect(Pos p) => _cells.FirstOrDefault(c => ReferenceEquals(c.P, p)) is { P: not null } x ? x.R : null;

    private static double BlockW(Block b) => LW + b.Nk * CW + 4 + BarW + 2 * Pad;
    private static double BlockH(Block b) => HH + b.Nt * CH + 2 * Pad;

    private double Layout(double width)
    {
        _cells.Clear(); _blocks.Clear(); _titles.Clear();
        if (double.IsInfinity(width) || width <= 0) width = 1200;
        double y = 4;
        foreach (var s in Sections)
        {
            _titles.Add((new Point(4, y), s.Title)); y += SecH;
            double x = 4, rowH = 0;
            foreach (var b in s.Blocks)
            {
                double w = BlockW(b), h = BlockH(b);
                if (x > 4 && x + w > width - 4) { x = 4; y += rowH + GapY; rowH = 0; }
                var br = new Rect(x, y, w, h); _blocks.Add((br, b));
                for (var t = 1; t <= b.Nt; t++)
                    for (var k = 1; k <= b.Nk; k++)
                        if (b.Cells.TryGetValue((t, k), out var p))
                            _cells.Add((new Rect(x + Pad + LW + (k - 1) * CW, y + Pad + HH + (b.Nt - t) * CH, CW, CH), p));
                x += w + GapX; rowH = Math.Max(rowH, h);
            }
            y += rowH + GapY + 6;
        }
        return y;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 1200 : availableSize.Width;
        var h = Layout(w); _lastWidth = w;
        return new Size(w, Math.Max(h, 40));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Math.Abs(finalSize.Width - _lastWidth) > 0.5) { Layout(finalSize.Width); _lastWidth = finalSize.Width; }
        return finalSize;
    }

    private static FormattedText Txt(string s, double size, IBrush brush, bool bold = false, double max = 0)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, brush);
        if (max > 0) { ft.MaxTextWidth = max; ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis; }
        return ft;
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (Sections.Count == 0)
        {
            dc.DrawText(Txt("Không có vị trí nào để vẽ.", 13, Muted), new Point(8, 8));
            return;
        }
        foreach (var (at, text) in _titles) dc.DrawText(Txt(text, 15, Head, true), at);
        foreach (var (r, b) in _blocks)
        {
            dc.DrawRectangle(b.Locked ? LockBg : BlockBg, BlockPen, r, 6, 6);
            dc.DrawText(Txt($"Dãy {Two(b.D)}" + (b.Locked ? "  · đang khóa" : ""), 12, Head, true, r.Width - 2 * Pad), new Point(r.X + Pad, r.Y + Pad + 2));
            for (var t = 1; t <= b.Nt; t++)
            {
                var ry = r.Y + Pad + HH + (b.Nt - t) * CH;
                var key = $"{b.Cells.Values.First().Kho}.{b.Khu}.{Two(b.D)}.{t}";
                var info = TangInfo?.Invoke(key) ?? (0, "", 0, b.Nk);
                dc.DrawText(Txt("T" + t, 11, info.Lv == "over" ? LevelBrush("over") : Muted, info.Lv == "over"), new Point(r.X + Pad + 2, ry + 5));
                // cột mức đầy của tầng
                var bar = new Rect(r.X + Pad + LW + b.Nk * CW + 4, ry + 2, BarW, CH - 4);
                dc.FillRectangle(LevelBrush(""), bar);
                if (info.Nk > 0 && info.Used > 0)
                {
                    var f = Math.Min(1, info.Used / (double)info.Nk);
                    dc.FillRectangle(LevelBrush(info.Lv), new Rect(bar.X, bar.Bottom - bar.Height * f, bar.Width, bar.Height * f));
                }
            }
        }
        foreach (var (r, p) in _cells)
        {
            var st = Status(p); var over = IsOverPos?.Invoke(p) ?? false;
            var bg = st == "e" ? EmptyBg : over ? OverBg : st == "mm" ? ManyBg : OneBg;
            dc.DrawRectangle(bg, CellPen, r);
            var text = CellText?.Invoke(p) ?? "";
            if (text.Length > 0) dc.DrawText(Txt(text, 10.5, Head, false, CW - 6), new Point(r.X + 3, r.Y + 5));
            if (Hit is not null && !Hit(p)) dc.FillRectangle(Dim, r);
        }
        // viền tầng vượt mức
        foreach (var (r, b) in _blocks)
            for (var t = 1; t <= b.Nt; t++)
            {
                var key = $"{b.Cells.Values.First().Kho}.{b.Khu}.{Two(b.D)}.{t}";
                if ((TangInfo?.Invoke(key).Lv ?? "") != "over") continue;
                var ry = r.Y + Pad + HH + (b.Nt - t) * CH;
                dc.DrawRectangle(null, OverPen, new Rect(r.X + Pad + LW - 1, ry, b.Nk * CW + 2, CH));
            }
        if (Hit is not null) foreach (var (r, p) in _cells) if (Hit(p)) dc.DrawRectangle(null, HitPen, r.Deflate(1.5));
        if (Selected is not null && _cells.FirstOrDefault(c => ReferenceEquals(c.P, Selected)) is { P: not null } s) dc.DrawRectangle(null, SelPen, s.R.Deflate(1));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        foreach (var (r, p) in _cells)
            if (r.Contains(pt)) { Selected = p; InvalidateVisual(); Picked?.Invoke(p); e.Handled = true; return; }
    }
}

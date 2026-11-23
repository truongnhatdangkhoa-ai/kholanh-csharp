using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace KhoLanh.Core.KhangSinh;

/// <summary>
/// Đọc chữ + toạ độ trong file PDF (thư viện PdfPig, viết thuần C#, không cần cài gì thêm).
/// App cũ dùng pdf.js (page.getTextContent): mỗi "mảnh chữ" là một đoạn chữ liền nhau trên cùng dòng.
/// Lớp này dựng lại đúng các mảnh đó từ từng ký tự theo cùng quy tắc của pdf.js 3.11 (khoảng cách ký tự,
/// thêm dấu cách, cắt mảnh khi xuống dòng / nhảy xa) để bộ đọc phiếu cho ra kết quả giống app cũ.
/// </summary>
public static class PdfText
{
    /// <summary>Một mảnh chữ: x, y = điểm đầu đường chân chữ (toạ độ PDF, gốc dưới-trái), w = bề rộng, h = cỡ chữ.</summary>
    public sealed class Tok
    {
        public string Text = "";
        public double X, Y, W, H;
        public Tok Clone() => new() { Text = Text, X = X, Y = Y, W = W, H = H };
        public override string ToString() => $"{Text} @({X:0.##},{Y:0.##}) w{W:0.##}";
    }

    public sealed class Page
    {
        public int No;
        public double Left, Bottom, Width, Height;
        /// <summary>Các mảnh chữ thô theo đúng thứ tự trong file (như content.items của pdf.js).</summary>
        public List<Tok> Items = new();
    }

    public static List<Page> Read(byte[] pdf)
    {
        var pages = new List<Page>();
        using var doc = PdfDocument.Open(pdf);
        foreach (var p in doc.GetPages())
        {
            var box = p.CropBox.Bounds;
            var pg = new Page { No = p.Number, Left = box.Left, Bottom = box.Bottom, Width = box.Width, Height = box.Height };
            pg.Items = BuildItems(p.Letters, FlushPoints(p), box.Left, box.Bottom, box.Right, box.Top);
            pages.Add(pg);
        }
        return pages;
    }

    /// <summary>pdf.js cắt mảnh chữ khi gặp lệnh đổi phông (Tf) khác phông đang dùng — kể cả khi phông "đang dùng" vừa bị
    /// trả về trạng thái cũ bởi Q — và khi vẽ XObject (Do). Trả về số thứ tự lệnh in chữ (TextSequence) ngay sau các chỗ đó.</summary>
    internal static HashSet<int> FlushPoints(UglyToad.PdfPig.Content.Page p)
    {
        var res = new HashSet<int>();
        var stack = new Stack<(string? Font, double Size)>();
        (string? Font, double Size) cur = (null, 0);
        int seq = 0;
        foreach (var op in p.Operations)
        {
            switch (op)
            {
                case UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState.Push: stack.Push(cur); break;
                case UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState.Pop: if (stack.Count > 0) cur = stack.Pop(); break;
                case UglyToad.PdfPig.Graphics.Operations.TextState.SetFontAndSize tf:
                    {
                        var name = tf.Font.Data; var size = (double)tf.Size;
                        if (!(cur.Font is not null && cur.Font == name && cur.Size == size)) res.Add(seq + 1);
                        cur = (name, size);
                        break;
                    }
                case UglyToad.PdfPig.Graphics.Operations.InvokeNamedXObject: res.Add(seq + 1); break;
                case UglyToad.PdfPig.Graphics.Operations.TextShowing.ShowText:
                case UglyToad.PdfPig.Graphics.Operations.TextShowing.ShowTextsWithPositioning:
                case UglyToad.PdfPig.Graphics.Operations.TextShowing.MoveToNextLineShowText:
                case UglyToad.PdfPig.Graphics.Operations.TextShowing.MoveToNextLineShowTextWithSpacing:
                    seq++; break;
            }
        }
        return res;
    }

    private static bool IsWs(string v)
    {
        foreach (var ch in v) if (!char.IsWhiteSpace(ch) && ch != ' ') return false;
        return true;
    }

    /// <summary>Gom ký tự thành mảnh chữ theo getTextContent của pdf.js 3.11 (compareWithLastPosition / addFakeSpaces).</summary>
    internal static List<Tok> BuildItems(IReadOnlyList<Letter> letters, HashSet<int> flushAt, double vx0, double vy0, double vx1, double vy1)
    {
        var items = new List<Tok>();
        var sb = new System.Text.StringBuilder();
        bool init = false;
        double sx = 0, sy = 0, width = 0, height = 0;
        // ngưỡng của mảnh hiện tại (giữ lại sau khi cắt mảnh, như pdf.js)
        double tracking = double.NegativeInfinity, notASpace = double.NegativeInfinity, negMax = double.NegativeInfinity, inMin = double.NegativeInfinity, inMax = double.NegativeInfinity;
        bool havePrev = false; double px = 0, py = 0;
        int lastSeq = int.MinValue;
        var two = new[] { ' ', ' ' }; int twoPos = 0;
        bool SaveLast(char c) { var next = (twoPos + 1) % 2; var ret = two[twoPos] != ' ' && two[next] == ' '; two[twoPos] = c; twoPos = next; return ret; }
        bool ShouldWs() => two[twoPos] != ' ' && two[(twoPos + 1) % 2] == ' ';
        void Reset() { two[0] = two[1] = ' '; twoPos = 0; }
        void Flush()
        {
            if (!init) return;
            if (sb.Length > 0) items.Add(new Tok { Text = sb.ToString(), X = sx, Y = sy, W = Math.Abs(width), H = height });
            init = false; sb.Clear();
        }

        foreach (var l in letters)
        {
            var v = l.Value ?? "";
            if (v.Length == 0) continue;
            if (l.TextSequence != lastSeq) { if (flushAt.Contains(l.TextSequence)) Flush(); lastSeq = l.TextSequence; }
            if (IsWs(v)) { SaveLast(' '); continue; }
            double x = l.StartBaseLine.X, y = l.StartBaseLine.Y;
            double gw = l.EndBaseLine.X - l.StartBaseLine.X;
            if (x + gw < vx0 || x > vx1 || y < vy0 || y > vy1) continue;
            var size = l.PointSize > 0 ? l.PointSize : l.FontSize;
            if (havePrev && !(Math.Abs(px - x) < 1e-7 && Math.Abs(py - y) < 1e-7))
            {
                double advX = x - px, advY = y - py;
                if (advX < negMax) { Reset(); Flush(); }
                else if (Math.Abs(advY) > height) { Reset(); Flush(); }
                else
                {
                    if (advX <= notASpace) Reset();
                    if (advX <= tracking)
                    {
                        if (ShouldWs()) { Reset(); Flush(); }
                        else width += advX;
                    }
                    else if (inMin <= advX && advX <= inMax)
                    {
                        if (init) { Reset(); sb.Append(' '); }
                        if (init) width += advX; else Reset();
                    }
                    else { Flush(); Reset(); }
                    if (Math.Abs(advY) > height * 0.25) Flush();
                }
            }
            if (!init)
            {
                init = true; sx = x; sy = y; width = 0; height = size;
                tracking = size * 0.102; notASpace = size * 0.03; negMax = size * -0.2; inMin = size * 0.102; inMax = size * 0.6;
            }
            width += gw;
            if (gw != 0) { havePrev = true; px = l.EndBaseLine.X; py = l.EndBaseLine.Y; }
            var first = v[0];
            if (SaveLast(first)) sb.Append(' ');
            sb.Append(v);
        }
        Flush();
        return items;
    }

    // ------------------------------------------------------------------ extractPdfPages (bộ đọc phiếu)

    /// <summary>Trang đã gộp mảnh như extractPdfPages của app cũ: bỏ mảnh rỗng, xếp trên→dưới, trái→phải,
    /// nối mảnh sát nhau (≤ 2.2) trên cùng dòng; Raw = chữ theo thứ tự gốc trong file (cách đọc thứ 2).</summary>
    public sealed class PageToks
    {
        public int PageNo;
        public List<Tok> Toks = new();
        public List<string> Raw = new();
    }

    public static List<PageToks> Pages(IReadOnlyList<Page> pages)
    {
        var res = new List<PageToks>();
        foreach (var p in pages)
        {
            var items = p.Items.Where(it => it.Text.Trim().Length > 0)
                .Select(it => new Tok { Text = it.Text, X = it.X, Y = it.Y, W = it.W, H = Math.Abs(it.H) > 0 ? Math.Abs(it.H) : 7 }).ToList();
            items = Js.Sort(items, (a, b) => Js.Cmp(b.Y - a.Y) is var c && c != 0 ? c : Js.Cmp(a.X - b.X));
            var outl = new List<Tok>();
            foreach (var it in items)
            {
                var prev = outl.Count > 0 ? outl[^1] : null;
                if (prev is not null && Math.Abs(prev.Y - it.Y) <= 1 && it.X >= prev.X + prev.W - 1 && (it.X - (prev.X + prev.W)) < 2.2)
                { prev.Text += it.Text; prev.W = (it.X + it.W) - prev.X; }
                else outl.Add(it.Clone());
            }
            res.Add(new PageToks { PageNo = p.No, Toks = outl, Raw = p.Items.Select(it => it.Text.Trim()).Where(s => s.Length > 0).ToList() });
        }
        return res;
    }

    /// <summary>extractPdfLines của app cũ (bảng chung): dòng theo y (sai số 2.5), trong dòng nối mảnh sát nhau.</summary>
    public static List<List<Tok>> Lines(IReadOnlyList<Page> pages)
    {
        var all = new List<List<Tok>>();
        foreach (var p in pages)
        {
            var items = p.Items.Where(it => it.Text.Trim().Length > 0).Select(it => new Tok { Text = it.Text, X = it.X, Y = it.Y, W = it.W }).ToList();
            if (items.Count == 0) continue;
            items = Js.Sort(items, (a, b) => Js.Cmp(b.Y - a.Y) is var c && c != 0 ? c : Js.Cmp(a.X - b.X));
            var lines = new List<(double Y, List<Tok> Items)>();
            foreach (var it in items)
            {
                var idx = lines.FindIndex(l => Math.Abs(l.Y - it.Y) <= 2.5);
                if (idx < 0) { lines.Add((it.Y, new List<Tok>())); idx = lines.Count - 1; }
                lines[idx].Items.Add(it);
            }
            lines = Js.Sort(lines, (a, b) => Js.Cmp(b.Y - a.Y));
            foreach (var line in lines)
            {
                var sorted = Js.Sort(line.Items, (a, b) => Js.Cmp(a.X - b.X));
                var merged = new List<Tok>();
                foreach (var it in sorted)
                {
                    var prev = merged.Count > 0 ? merged[^1] : null;
                    if (prev is not null && (it.X - (prev.X + prev.W)) < 2.2) { prev.Text += it.Text; prev.W = (it.X + it.W) - prev.X; }
                    else merged.Add(new Tok { Text = it.Text, X = it.X, W = it.W, Y = it.Y });
                }
                all.Add(merged);
            }
        }
        return all;
    }
}

/// <summary>Vài hàm giúp chép thuật toán JS cho đúng (sắp xếp ổn định, so sánh số, làm tròn kiểu Math.round).</summary>
internal static class Js
{
    public static int Cmp(double d) => d < 0 ? -1 : d > 0 ? 1 : 0;
    /// <summary>Array.prototype.sort của JS là sắp xếp ổn định; List.Sort của .NET thì không.</summary>
    public static List<T> Sort<T>(IEnumerable<T> src, Comparison<T> cmp)
    {
        var arr = src.Select((v, i) => (v, i)).ToList();
        arr.Sort((a, b) => { var c = cmp(a.v, b.v); return c != 0 ? c : a.i.CompareTo(b.i); });
        return arr.Select(a => a.v).ToList();
    }
    public static double Round(double v) => Math.Floor(v + 0.5);
    public static string Num(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace KhoLanh.App.Controls;

/// <summary>
/// 1.135 — bôi đen + chép chữ như trang web (người dùng: "phần copy đã làm trước đó nhưng không tốt, mình muốn bôi đen rồi copy như trên 1 trang website,
/// có thể bôi đen copy bất kỳ chữ nào"). KHÔNG thay TextBlock bằng SelectableTextBlock (chữ đang cập nhật / nút sẽ hỏng) — lớp dùng chung cho MỌI
/// TextBlock thường (<see cref="CopyText.Eligible"/>: không thuộc nút / menu / tab / ô chọn / ô nhập):
///  • kéo chuột trên chữ = bôi đen (qua nhiều dòng / nhiều khung theo thứ tự trên màn hình, như web), bấm đúp = 1 từ, bấm 3 lần = cả dòng chữ;
///  • Ctrl+C = chép phần đã bôi đen (ưu tiên hơn ô bảng đang chọn vì vừa bôi), chuột phải → "Chép đoạn đã bôi đen";
///  • bấm chỗ khác / chuyển trang = bỏ chọn. Vùng chọn vẽ bằng lớp phủ (adorner) trên chính ô chữ, không đổi bố cục.
/// </summary>
public static class TextSelect
{
    private sealed record Pos(TextBlock Tb, int Index);

    private static Pos? _anchor, _focus;
    private static List<TextBlock> _order = new();
    private static TopLevel? _top;
    private static bool _down, _dragging;
    private static Point _downAt;
    private static readonly List<(AdornerLayer Layer, Control Overlay)> _overlays = new();

    public static bool HasSelection => SelectedText().Length > 0;

    public static void Install()
    {
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((t, e) => OnPressed(t, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((t, e) => OnMoved(t, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, _) => { _down = false; }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((t, e) => OnKey(t, e), RoutingStrategies.Tunnel);
        Control.LoadedEvent.AddClassHandler<TextBlock>((tb, _) =>
        {
            if (tb is not SelectableTextBlock && tb.Cursor is null && Selectable(tb) && !tb.GetVisualAncestors().OfType<Control>().Any(a => a.Cursor is not null))
                tb.Cursor = new Cursor(StandardCursorType.Ibeam);
        });
    }

    /// <summary>Ô chữ bôi đen được: TextBlock thường (không phải SelectableTextBlock — tự có bôi đen), chữ thường trên màn hình, không trong bảng DataGrid.</summary>
    internal static bool Selectable(TextBlock tb) =>
        tb is not SelectableTextBlock && tb.IsEffectivelyVisible && CopyText.Eligible(tb) && !tb.GetVisualAncestors().Any(a => a is DataGrid);

    /// <summary>Chữ đang hiện của ô (Text, hoặc các Run khi ô dùng Inlines) — trùng chỉ số ký tự của TextLayout.</summary>
    internal static string TextOf(TextBlock tb)
    {
        if (tb.Text is { } t) return t;
        if (tb.Inlines is not { Count: > 0 } il) return "";
        return string.Concat(il.Select(i => i switch { Run r => r.Text ?? "", LineBreak => "\n", _ => "" }));
    }

    // ------------------------------------------------------------------ chuột

    private static void OnPressed(TopLevel top, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(top);
        if (!pt.Properties.IsLeftButtonPressed) return;   // chuột phải giữ vùng chọn để "Chép đoạn đã bôi đen"
        Clear();
        var hit = HitText(top, e.GetPosition(top));
        if (hit is null) return;
        _top = top; _order.Clear();   // thứ tự các ô chữ chỉ tính khi kéo sang ô khác (EnsureOrder) — bấm / kéo trong 1 dòng không quét cả trang
        if (e.ClickCount >= 2)
        {
            var text = TextOf(hit.Tb);
            if (e.ClickCount >= 3) { _anchor = new(hit.Tb, 0); _focus = new(hit.Tb, text.Length); }
            else { var (a, b) = Word(text, hit.Index); _anchor = new(hit.Tb, a); _focus = new(hit.Tb, b); }
            Paint(); return;
        }
        _anchor = _focus = hit; _down = true; _dragging = false; _downAt = e.GetPosition(top);
    }

    private static void OnMoved(TopLevel top, PointerEventArgs e)
    {
        if (!_down || _anchor is null) return;
        var p = e.GetPosition(top);
        // 1.184: ngưỡng kéo 4 → 1,5 px — ký tự rộng ~7 px, kéo nửa ký tự (~3,5 px) mà ngưỡng 4 thì không bôi được 1 chữ, phải kéo thêm 3–4 chữ mới ăn
        if (!_dragging && Math.Abs(p.X - _downAt.X) + Math.Abs(p.Y - _downAt.Y) < 1.5) return;
        _dragging = true;
        var hit = HitText(top, p);
        if (hit is null || hit.Tb != _anchor.Tb) { EnsureOrder(); hit = HitText(top, p, nearest: true); }
        if (hit is null || hit == _focus) return;
        if (hit.Tb != _anchor.Tb && !_order.Contains(hit.Tb)) return;   // ra ngoài vùng chữ của trang (thanh menu, hộp khác…) → giữ chỗ cũ
        _focus = hit; Paint();
    }

    private static void OnKey(TopLevel top, KeyEventArgs e)
    {
        if (e.Key != Key.C || (e.KeyModifiers & KeyModifiers.Control) == 0 || (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Shift)) != 0) return;
        if (top.FocusManager?.GetFocusedElement() is TextBox { SelectionStart: var s, SelectionEnd: var en } && s != en) return;   // ô nhập đang bôi đen → để ô tự chép
        var text = SelectedText();
        if (text.Length == 0) return;
        _ = top.Clipboard?.SetTextAsync(text);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ chọn / chép

    /// <summary>Chữ đang bôi đen (nhiều ô: mỗi ô 1 dòng, như chép trên trang web).</summary>
    public static string SelectedText()
    {
        if (_anchor is null || _focus is null) return "";
        var (s, f) = Ordered();
        if (s is null || f is null) return "";
        if (s.Tb == f.Tb) { var t = TextOf(s.Tb); var a = Math.Clamp(s.Index, 0, t.Length); var b = Math.Clamp(f.Index, a, t.Length); return t[a..b]; }
        EnsureOrder();
        var i0 = _order.IndexOf(s.Tb); var i1 = _order.IndexOf(f.Tb);
        var parts = new List<string>();
        for (var i = i0; i <= i1; i++)
        {
            var t = TextOf(_order[i]);
            if (i == i0) parts.Add(t[Math.Clamp(s.Index, 0, t.Length)..]);
            else if (i == i1) parts.Add(t[..Math.Clamp(f.Index, 0, t.Length)]);
            else parts.Add(t);
        }
        return string.Join("\n", parts.Where(p => p.Length > 0)).Trim();
    }

    /// <summary>Bỏ chọn (bấm chỗ khác / chuyển trang).</summary>
    public static void Clear()
    {
        foreach (var (layer, ov) in _overlays) layer.Children.Remove(ov);
        _overlays.Clear();
        _anchor = _focus = null; _down = false; _dragging = false; _order.Clear();
    }

    /// <summary>Công cụ chụp / test: chọn từ ký tự <paramref name="a"/> của ô <paramref name="ta"/> tới ký tự <paramref name="b"/> của ô <paramref name="tb"/>.</summary>
    internal static void SelectForTest(TopLevel top, TextBlock ta, int a, TextBlock tb, int b)
    {
        Clear(); _top = top; _anchor = new(ta, a); _focus = new(tb, b); EnsureOrder(); Paint();
    }

    private static void EnsureOrder() { if (_order.Count == 0 && _top is not null && _anchor is not null) _order = Order(_top, _anchor.Tb); }

    private static (Pos? S, Pos? F) Ordered()
    {
        if (_anchor is null || _focus is null) return (null, null);
        if (_anchor.Tb == _focus.Tb) return _anchor.Index <= _focus.Index ? (_anchor, _focus) : (_focus, _anchor);
        EnsureOrder();
        var ia = _order.IndexOf(_anchor.Tb); var ifo = _order.IndexOf(_focus.Tb);
        if (ia < 0 || ifo < 0) return (null, null);
        return ia <= ifo ? (_anchor, _focus) : (_focus, _anchor);
    }

    private static (int, int) Word(string t, int i)
    {
        static bool W(char c) => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '/' or ',';
        if (t.Length == 0) return (0, 0);
        i = Math.Clamp(i, 0, t.Length - 1);
        if (!W(t[i]) && i > 0 && W(t[i - 1])) i--;
        if (!W(t[i])) return (i, i + 1);
        int a = i, b = i;
        while (a > 0 && W(t[a - 1])) a--;
        while (b < t.Length && W(t[b])) b++;
        return (a, b);
    }

    // ------------------------------------------------------------------ tìm ô chữ dưới chuột

    /// <summary>Các ô chữ bôi đen được trong cùng vùng (trang / hộp thoại) theo thứ tự trên màn hình — để kéo qua nhiều ô.</summary>
    private static List<TextBlock> Order(TopLevel top, TextBlock start)
    {
        Visual root = top;
        foreach (var a in start.GetVisualAncestors())
            if (a is ScrollViewer or Window or PopupRoot) { root = a; break; }
        return root.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(TextOf(t)) && Selectable(t)).ToList();
    }

    private static Pos? HitText(TopLevel top, Point p, bool nearest = false)
    {
        TextBlock? best = null;
        var start = top.InputHitTest(p) as Visual;
        if (start is TextBlock t0 && Selectable(t0)) best = t0;
        else if (start is not null)
        {
            foreach (var t in start.GetVisualDescendants().OfType<TextBlock>())
                if (Inside(top, t, p) && Selectable(t)) best = t;
            if (best is null) foreach (var a in start.GetVisualAncestors()) if (a is TextBlock ta && Selectable(ta)) { best = ta; break; }
        }
        if (best is null && nearest && _order.Count > 0)
        {
            // kéo vào khoảng trống giữa các dòng → ô gần nhất theo chiều dọc
            double bestD = double.MaxValue;
            foreach (var t in _order)
            {
                var r = Rect(top, t); if (r is null) continue;
                var d = p.Y < r.Value.Top ? r.Value.Top - p.Y : p.Y > r.Value.Bottom ? p.Y - r.Value.Bottom : 0;
                d += p.X < r.Value.Left ? (r.Value.Left - p.X) * 0.1 : p.X > r.Value.Right ? (p.X - r.Value.Right) * 0.1 : 0;
                if (d < bestD) { bestD = d; best = t; }
            }
        }
        if (best is null || string.IsNullOrEmpty(TextOf(best))) return null;
        var local = top.TranslatePoint(p, best) ?? default;
        var pad = best.Padding;
        var res = best.TextLayout.HitTestPoint(new Point(local.X - pad.Left, local.Y - pad.Top));
        return new Pos(best, Math.Clamp(res.TextPosition, 0, TextOf(best).Length));
    }

    private static bool Inside(TopLevel top, Visual v, Point p) => Rect(top, v) is { } r && r.Contains(p);
    private static Rect? Rect(TopLevel top, Visual v) => v.TranslatePoint(new Point(0, 0), top) is { } o ? new Rect(o, v.Bounds.Size) : null;

    // ------------------------------------------------------------------ vẽ vùng chọn

    private static void Paint()
    {
        foreach (var (layer, ov) in _overlays) layer.Children.Remove(ov);
        _overlays.Clear();
        var (s, f) = Ordered();
        if (s is null || f is null) return;
        var i0 = _order.IndexOf(s.Tb); var i1 = _order.IndexOf(f.Tb);
        if (s.Tb == f.Tb) { i0 = i1 = Math.Max(0, _order.IndexOf(s.Tb)); }
        for (var i = i0; i <= i1 && i < _order.Count; i++)
        {
            var tb = s.Tb == f.Tb ? s.Tb : _order[i];
            var len = TextOf(tb).Length;
            var a = tb == s.Tb ? s.Index : 0; var b = tb == f.Tb ? f.Index : len;
            if (b <= a) continue;
            if (AdornerLayer.GetAdornerLayer(tb) is not { } layer) continue;
            var ov = new Overlay(tb, a, b - a) { IsHitTestVisible = false };
            AdornerLayer.SetAdornedElement(ov, tb);
            layer.Children.Add(ov);
            _overlays.Add((layer, ov));
            if (s.Tb == f.Tb) break;
        }
    }

    private sealed class Overlay : Control
    {
        private readonly TextBlock _tb; private readonly int _start, _len;
        private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x66, 0x3D, 0x86, 0xE6));
        public Overlay(TextBlock tb, int start, int len) { _tb = tb; _start = start; _len = len; }
        public override void Render(DrawingContext ctx)
        {
            var pad = _tb.Padding;
            foreach (var r in _tb.TextLayout.HitTestTextRange(_start, _len))
                ctx.FillRectangle(Fill, new Rect(r.X + pad.Left, r.Y + pad.Top, Math.Max(r.Width, 2), r.Height));
        }
    }
}

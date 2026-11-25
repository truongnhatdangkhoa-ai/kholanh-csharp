using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace KhoLanh.App.Controls;

/// <summary>
/// 1.83: vùng trang "lấp đầy hoặc cuộn". Đủ chỗ thì trang lấp đúng chiều cao cửa sổ như trước (dòng <c>*</c> giãn, bảng tự cuộn bên trong);
/// bảng nào được chia chỗ thấp hơn tối thiểu (<see cref="SheetGrid.MinRows"/> = 8 dòng) — vd 2 bảng xếp dọc ở màn hình 1366×768 — thì trang
/// cao thêm đúng phần thiếu và cuộn dọc, thay vì ép bảng còn 2–3 dòng / đè lên chữ phía trên. Ngang luôn theo bề rộng cửa sổ.
/// Cách tính: sau mỗi lượt bố cục, lấy chỗ cha đã chia cho từng bảng đang hiện (<see cref="LayoutInformation.GetPreviousMeasureConstraint"/>)
/// so với MinHeight; các bảng chồng dọc (cùng khoảng ngang) cộng dồn, cạnh nhau lấy lớn nhất → cộng vào chiều cao trang rồi bố cục lại.
/// Đổi cỡ cửa sổ / đổi trang / đổi tab thì tính lại từ đầu.
/// </summary>
public sealed class FillScroll : ScrollViewer
{
    private readonly FillBox _box;
    private double _viewH = double.NaN, _extra, _lastNeed;
    private int _tries;
    protected override Type StyleKeyOverride => typeof(ScrollViewer);   // dùng giao diện (template) của ScrollViewer

    public FillScroll(Control child)
    {
        _box = new FillBox(this) { Child = child };
        Content = _box;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        LayoutUpdated += (_, _) => Check();
        AddHandler(SelectingItemsControl.SelectionChangedEvent, (_, e) => { if (e.Source is TabControl) Reset(); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        if (child is ContentControl cc) cc.PropertyChanged += (_, e) => { if (e.Property == ContentControl.ContentProperty) { Reset(); Offset = default; } };
    }

    /// <summary>Tính lại từ đầu (đổi trang / tab / cỡ cửa sổ).</summary>
    public void Reset()
    {
        if (_extra == 0 && _tries == 0) return;
        _extra = 0; _tries = 0; _lastNeed = 0;
        _box.InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Height) && Math.Abs(availableSize.Height - (double.IsNaN(_viewH) ? -1 : _viewH)) > 0.5)
        {
            _viewH = availableSize.Height;
            _extra = 0; _tries = 0; _lastNeed = 0;
            _box.InvalidateMeasure();
        }
        return base.MeasureOverride(availableSize);
    }

    /// <summary>Phần chiều cao còn thiếu để mọi bảng đang hiện đủ tối thiểu.</summary>
    internal double Deficit()
    {
        var list = new List<(double L, double R, double D)>();
        foreach (var g in _box.GetVisualDescendants().OfType<SheetGrid>())
        {
            if (!g.IsEffectivelyVisible || g.MinHeight <= 0) continue;
            var p = g.TranslatePoint(new Point(0, 0), _box);
            if (p is not { } pt) continue;
            var d = 0.0;
            // 1) chỗ cha chia khi đo / khi xếp thấp hơn MinHeight (bảng bị đẩy đè lên chữ phía trên)
            if (LayoutInformation.GetPreviousMeasureConstraint(g) is { } cs && !double.IsInfinity(cs.Height))
                d = Math.Max(d, g.MinHeight - (cs.Height - g.Margin.Top - g.Margin.Bottom));
            if (LayoutInformation.GetPreviousArrangeBounds(g) is { } ab)
                d = Math.Max(d, g.MinHeight - (ab.Height - g.Margin.Top - g.Margin.Bottom));
            // 2) khung chứa (StackPanel / Border…) bị xếp thấp hơn nội dung → bảng tràn ra ngoài khung, đè lên phần dưới
            var bottom = pt.Y + g.Bounds.Height;
            for (var a = g.GetVisualParent(); a is not null && a != _box; a = a.GetVisualParent())
            {
                if (a is ScrollViewer or Avalonia.Controls.Presenters.ScrollContentPresenter) break;   // trang có khung cuộn riêng: tràn là bình thường
                if (a.TranslatePoint(new Point(0, 0), _box) is { } ap) d = Math.Max(d, bottom - (ap.Y + a.Bounds.Height));
            }
            if (d <= 0.5) continue;
            list.Add((pt.X, pt.X + g.Bounds.Width, d));
        }
        if (list.Count == 0) return 0;
        // bảng chồng dọc (khoảng ngang giao nhau) cộng dồn; bảng cạnh nhau lấy lớn nhất
        return list.Max(a => list.Where(b => b.L < a.R - 1 && a.L < b.R - 1).Sum(b => b.D));
    }

    private void Check()
    {
        if (double.IsNaN(_viewH)) return;
        var need = Deficit();
        if (need <= 0.5) { _tries = 0; _lastNeed = 0; return; }
        // không giảm được sau lần thêm trước (bảng nằm trong khung cố định chiều cao) → thôi, tránh cao mãi
        if (_tries > 0 && need >= _lastNeed - 0.5) return;
        if (_tries >= 12 || _extra > 6000) return;
        _tries++; _lastNeed = need;
        _extra += Math.Ceiling(need);
        _box.InvalidateMeasure();
    }

    /// <summary>Đo nội dung với chiều cao khung nhìn + phần thêm (không vô hạn như ScrollViewer thường) → dòng <c>*</c> vẫn giãn.</summary>
    private sealed class FillBox : Decorator
    {
        private readonly FillScroll _o;
        public FillBox(FillScroll owner) { _o = owner; }

        protected override Size MeasureOverride(Size availableSize)
        {
            var h = (double.IsNaN(_o._viewH) ? (double.IsInfinity(availableSize.Height) ? 600 : availableSize.Height) : _o._viewH) + _o._extra;
            if (Child is null) return new Size(0, h);
            Child.Measure(new Size(availableSize.Width, h));
            return new Size(Child.DesiredSize.Width, h);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}

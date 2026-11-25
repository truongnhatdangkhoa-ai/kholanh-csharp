using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.KiemKe;
using KhoLanh.Core.Services;
using static KhoLanh.Core.SoDo.SoDoCore;

namespace KhoLanh.App.Views;

/// <summary>
/// 1.72 · Sơ đồ kho › chế độ "Kết quả kiểm kê": chọn 1 trang Nhập liệu kiểm kê → mỗi ô tô theo trạng thái
/// khớp · lệch · thiếu · thừa · chưa đếm (lõi <see cref="KiemKeCore.MapPage"/>), chú giải + thẻ số; bấm ô xem sổ / đếm,
/// bấm đúp (hoặc nút) mở Nhập liệu đúng trang, đúng dòng. Từ Nhập liệu: chuột phải › "Xem vị trí … trên sơ đồ kho".
/// </summary>
public sealed partial class SoDoView
{
    private string? _kkPageId, _kkWantCode;
    private string _kkPageName = "";
    private KiemKeCore.KkMap? _kkMap;
    private readonly WrapPanel _legendStd = new() { ItemSpacing = 8, LineSpacing = 6 };
    private readonly WrapPanel _legendKk = new() { ItemSpacing = 8, LineSpacing = 6, IsVisible = false };
    private Button? _kkPick, _kkOff;

    /// <summary>Mở Sơ đồ kho ở chế độ kết quả kiểm kê của trang <paramref name="kkPageId"/>, chọn sẵn ô <paramref name="code"/> (nếu có).</summary>
    public SoDoView(string kkPageId, string? code) : this() { _kkPageId = kkPageId; _kkWantCode = code; }

    private static readonly IBrush KkGray = AppTheme.Brush("#EEF1F5");
    private static IBrush KkBrush(string st) => st switch
    {
        KiemKeCore.KkKhop => AppTheme.Brush("#E3F4EA"),
        KiemKeCore.KkLech => AppTheme.Brush("#FFF2D6"),
        KiemKeCore.KkThieu or KiemKeCore.KkThua => AppTheme.Brush("#FDE8E6"),
        _ => KkGray,
    };
    private static IBrush KkFg(string st) => st switch
    {
        KiemKeCore.KkKhop => AppTheme.Brush("#1E7B4A"),
        KiemKeCore.KkLech => AppTheme.Brush("#9A5B00"),
        KiemKeCore.KkThieu or KiemKeCore.KkThua => AppTheme.Brush("#B42318"),
        _ => AppTheme.Brush("#5D6B78"),
    };

    private IBrush? KkFill(Pos p) => _kkMap is { } m && m.St.TryGetValue(p.Code, out var st) ? KkBrush(st) : _kkMap is null ? null : KkGray;

    private string? KkCellText(Pos p)
    {
        if (_kkMap is not { } m) return null;
        if (!m.St.TryGetValue(p.Code, out var st) || !m.Cells.TryGetValue(p.Code, out var c)) return "";
        return st switch
        {
            KiemKeCore.KkLech => Math.Abs(c.Cnt - c.Exp) <= KiemKeCore.VtTol ? "sai lô" : (c.Cnt - c.Exp > 0 ? "+" : "") + Q(c.Cnt - c.Exp),
            KiemKeCore.KkThieu => "−" + Q(c.Exp),
            KiemKeCore.KkThua => "+" + Q(c.Cnt),
            KiemKeCore.KkKhop => c.NExp == 0 ? "" : "✓",
            _ => "",
        };
    }

    private Control KkBar()
    {
        _kkPick = Btn("Xem kết quả kiểm kê…", async () => await KkChoose(), tip: "Chọn 1 trang Nhập liệu kiểm kê (có cột Vị trí): tô từng ô khớp / lệch / thiếu / thừa / chưa đếm so với sổ theo vị trí");
        _kkOff = Btn("Thoát kết quả kiểm kê", () => KkSet(null, null), tip: "Về sơ đồ tồn thường");
        _kkOff.IsVisible = false;
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        p.Children.Add(_kkPick); p.Children.Add(_kkOff);
        return p;
    }

    private void KkLegend()
    {
        _legendKk.Children.Clear();
        _legendKk.IsVisible = _kkMap is not null; _legendStd.IsVisible = _kkMap is null;
        _map.ShowTangBar = _kkMap is null;   // chế độ kiểm kê: bỏ cột mức đầy / viền tầng vượt mức cho dễ nhìn màu trạng thái
        if (_kkOff is not null) _kkOff.IsVisible = _kkMap is not null;
        if (_kkPick is not null) _kkPick.Content = _kkMap is null ? "Xem kết quả kiểm kê…" : "Đổi trang kiểm kê…";
        if (_kkMap is null) return;
        _legendKk.Children.Add(Label("Trang: " + _kkPageName, true, 12));
        foreach (var st in KiemKeCore.KkStates)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            s.Children.Add(new Border { Width = 16, Height = 12, Background = KkBrush(st), BorderBrush = Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center });
            s.Children.Add(Label(KiemKeCore.KkText(st), size: 12));
            _legendKk.Children.Add(s);
        }
    }

    /// <summary>Thẻ số theo kho / khu đang xem (chế độ kiểm kê). Trả false khi không ở chế độ này.</summary>
    private bool KkRenderKpi()
    {
        if (_kkMap is not { } m) return false;
        var n = KiemKeCore.KkStates.ToDictionary(x => x, _ => 0);
        foreach (var p in ScopeList()) if (!p.Z && m.St.TryGetValue(p.Code, out var st)) n[st]++;
        foreach (var st in KiemKeCore.KkStates)
            _kpi.Children.Add(Kpi(KiemKeCore.KkText(st), n[st].ToString("#,0", Ui.Vi), KkFg(st), st == KiemKeCore.KkKhop ? $"Gồm {m.Empty:#,0} ô trống đúng (cả trang)" : null));
        if (m.Bad.Count > 0) _kpi.Children.Add(Kpi("dòng không đặt lên sơ đồ được", m.Bad.Count.ToString("#,0", Ui.Vi), Warn, string.Join("\n", m.Bad.Take(12).Select(b => $"Dòng {b.R.Row}: {b.Why}"))));
        return true;
    }

    private void KkDetail(StackPanel host, Pos p)
    {
        if (_kkMap is not { } m || p.Z) return;
        var st = m.St.GetValueOrDefault(p.Code, KiemKeCore.KkChuaDem);
        var box = new StackPanel { Spacing = 4 };
        var head = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        head.Children.Add(new Border { Background = KkBrush(st), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2), Child = new TextBlock { Text = "Kiểm kê: " + KiemKeCore.KkText(st), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = KkFg(st) } });
        box.Children.Add(head);
        if (m.Cells.TryGetValue(p.Code, out var c) && c.Items.Count > 0)
        {
            foreach (var it in c.Items)
                box.Children.Add(Label($"{Ma(it.Ma, "")}{(it.Lo.Length > 0 ? " · lô " + it.Lo : "")}: sổ {Q(it.Exp)} · đếm {Q(it.Cnt)}" + (Math.Abs(it.Diff) > 1e-9 ? $" · chênh {(it.Diff > 0 ? "+" : "")}{Q(it.Diff)}" : ""), size: 12,
                    fg: Math.Abs(it.Diff) > 1e-9 ? KkFg(KiemKeCore.KkLech) : null));
        }
        else if (st == KiemKeCore.KkChuaDem) box.Children.Add(Muted("Dãy này không có trong trang kiểm kê đang xem."));
        if (m.RowOf.TryGetValue(p.Code, out var row))
        {
            var b = Btn($"Mở dòng {row + 1} ở Nhập liệu", async () => await KkOpenEntry(p), tip: "Mở trang kiểm kê ở module Nhập liệu, chọn đúng dòng của vị trí này (hoặc bấm đúp ô trên sơ đồ)");
            box.Children.Add(b);
        }
        host.Children.Add(new Border { BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Child = box });
    }

    private async Task KkChoose()
    {
        List<KhoLanh.Core.Entry.EntryService.PageInfo> pages;
        try { pages = Svc.KkPages(); }
        catch (BusinessException e) { await Ui.Message(this, e.Message); return; }
        if (pages.Count == 0) { await Ui.Message(this, "Chưa có trang Nhập liệu kiểm kê nào có cột “Vị trí”. Ở tab Phiếu In vị trí, bấm “Chuyển sang Nhập liệu” để tạo trang kiểm kê theo dãy.", "Chưa có trang kiểm kê"); return; }
        var cur = Math.Max(0, pages.FindIndex(p => p.Id == _kkPageId));
        var pick = await Ui.Choose(this, "Chọn trang Nhập liệu kiểm kê để xem trên sơ đồ (so với sổ theo vị trí đang nạp):", "Xem kết quả kiểm kê",
            pages.Select(p => $"{p.Name}  ·  {p.Rows:#,0} dòng").ToList(), cur);
        if (pick is not { } k || k.Index < 0) return;
        KkSet(pages[k.Index].Id, null);
    }

    /// <summary>Bật (pageId ≠ null) / tắt chế độ kiểm kê; chọn kho / khu theo trang, chọn sẵn ô <paramref name="code"/>.</summary>
    private void KkSet(string? pageId, string? code)
    {
        _kkPageId = pageId; _kkWantCode = code; _kkMap = null; _kkPageName = "";
        if (pageId is null) { KkLegend(); RenderAll(); return; }
        if (!KkCompute()) { KkLegend(); RenderAll(); return; }
        var m = _kkMap!;
        Pos? want = code is not null ? Svc.B.ByCode.GetValueOrDefault(code) : null;
        if (want is not null) { _kho = want.Kho; _khu = m.OneKhu && m.Khu == want.Khu ? want.Khu : "ALL"; _sel = want; }
        else if (m.Kho.Length > 0) { _kho = m.Kho; _khu = m.OneKhu ? m.Khu : "ALL"; _sel = null; }
        _kkWantCode = null;
        _tabs.SelectedIndex = 0;
        KkLegend(); RenderAll();
        if (want is not null) Pick(want);
    }

    private bool KkCompute()
    {
        if (_kkPageId is null) return false;
        try
        {
            _kkMap = Svc.KkMapOf(_kkPageId);
            _kkPageName = Svc.KkPages().FirstOrDefault(p => p.Id == _kkPageId)?.Name ?? "";
            return true;
        }
        catch (BusinessException e) { _kkMap = null; _kkPageId = null; _status.Text = e.Message; return false; }
    }

    /// <summary>Sau mỗi lần tải sơ đồ: tính lại kết quả kiểm kê (dữ liệu mới), lần đầu thì áp yêu cầu mở từ Nhập liệu.</summary>
    private void KkAfterLoad()
    {
        if (_kkPageId is null) return;
        if (_kkWantCode is not null || _kkMap is null) { var id = _kkPageId; var code = _kkWantCode; Avalonia.Threading.Dispatcher.UIThread.Post(() => KkSet(id, code)); return; }
        KkCompute(); KkLegend();
    }

    private async Task KkOpenEntry(Pos p)
    {
        if (_kkMap is not { } m || _kkPageId is null) return;
        if (!App.Services.Session.Can("m6")) { await Ui.Message(this, "Tài khoản không có quyền xem module Nhập liệu."); return; }
        var row = m.RowOf.GetValueOrDefault(p.Code, 0);
        var ev = new EntryView();
        ev.OpenAt(_kkPageId, row);
        MainWindow.Of(this)?.Show("m6", ev);
    }

    /// <summary>Công cụ chụp ảnh: bật chế độ kiểm kê với trang cho sẵn.</summary>
    internal void KkForDemo(string pageId, string? code) => KkSet(pageId, code);
}

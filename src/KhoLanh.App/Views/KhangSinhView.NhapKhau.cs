using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.KhangSinh;
using KhoLanh.Core.Services;
using static KhoLanh.Core.KhangSinh.KhangSinhCore;
using Nk = KhoLanh.Core.KhangSinh.KhangSinhNhapKhau;

namespace KhoLanh.App.Views;

/// <summary>
/// 1.175 — Tab "Tôm nhập khẩu": kết quả kháng sinh của cả cont hàng nhập (không có lô) — tra cứu theo TP / INV / Contract + Size,
/// thẻ kết quả (đạt / nhiễm), danh sách theo Size, các mẫu của dòng đang chọn. Nạp PDF phiếu tôm nhập khẩu ở tab "Nạp PDF phiếu".
/// </summary>
public sealed partial class KhangSinhView
{
    private readonly GridModel _nm = new() { ReadOnly = true };
    private readonly GridModel _nsm = new() { ReadOnly = true };
    private SheetGrid _ng = null!, _nsg = null!;
    private readonly TextBox _nq = new() { Watermark = "Tìm — VD: TP143 size 31/40 · INV MNSF-155 · TR3007-05 · Ấn Độ", Width = 470 };
    private readonly CheckBox _nOld = new() { Content = "Cả bản đã bị phiếu cập nhật thay", FontSize = 12.5 };
    private readonly StackPanel _nCards = new() { Spacing = 6 };
    private readonly TextBlock _nInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private List<Nk.Nhom> _nShown = new();
    private List<Nk.Mau> _nMau = new();
    private static readonly IBrush NkGreen = AppTheme.Brush("#E3F4EA"), NkAmber = AppTheme.Brush("#FFF2D6");

    private static readonly string[] NkHeads = { "Ngày phân tích", "Số phiếu", "Nước", "INV", "Contract", "TP", "Hàng", "Size", "Ngày nhập", "Số mẫu", "Số kiện", "Kết quả", "Mẫu nhiễm", "Ghi chú" };
    private static readonly int[] NkW = { 110, 80, 80, 150, 150, 50, 80, 64, 100, 80, 80, 300, 280, 220 };

    private Control BuildNk()
    {
        _ng = new SheetGrid(_nm) { SpareRows = 0, CellBackground = NkBg };
        _nsg = new SheetGrid(_nsm) { SpareRows = 0, CellBackground = NkMauBg };
        _ng.SelectionChanged += () => { if (_ng.Row >= 0 && _ng.Row < _nShown.Count) NkShowMau(_nShown[_ng.Row]); };
        var t = new System.Timers.Timer(220) { AutoReset = false };
        t.Elapsed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(NkRefresh);
        _nq.TextChanged += (_, _) => { t.Stop(); t.Start(); };
        _nOld.IsCheckedChanged += (_, _) => NkRefresh();

        var acts = new ToolRow { };
        acts.Children.Add(_nq); acts.Children.Add(Btn("Xóa bộ lọc", () => { _nq.Text = ""; NkRefresh(); }));
        if (_edit)
        {
            acts.Children.Add(Btn("Nạp PDF phiếu tôm nhập khẩu…", async () => { _tabs.SelectedIndex = 4; await PickPdf(); }, primary: true));
            acts.Children.Add(ToolRow.More(Btn("Xóa dòng đang chọn…", async () => await NkDelete(), danger: true)));
        }
        acts.Children.Add(ToolRow.More(_nOld));
        var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 6) };
        head.Children.Add(acts);
        head.Children.Add(_nCards);
        head.Children.Add(_nInfo);
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,6,*") };
        var up = Framed(_ng);
        var sp = new GridSplitter { Background = Line_, ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(sp, 1);
        var down = new DockPanel();
        var dl = Label("Các mẫu của dòng đang chọn (mẫu nhiễm tô vàng)", bold: true); dl.Margin = new Thickness(0, 4, 0, 4);
        DockPanel.SetDock(dl, Dock.Top); down.Children.Add(dl); down.Children.Add(Framed(_nsg)); Grid.SetRow(down, 2);
        grid.Children.Add(up); grid.Children.Add(sp); grid.Children.Add(down);
        var root = new DockPanel(); DockPanel.SetDock(head, Dock.Top); root.Children.Add(head); root.Children.Add(grid);
        return root;
    }

    private static readonly string[] NkMauHeads = { "Mẫu", "Tên mẫu", "Kiện", "Ngày nhập", "Enro", "Cipro", "Oxy", "Doxy", "Sulfo", "AOZ", "CAP", "Kết quả", "Đặc tính", "Ghi chú" };

    private void NkRefresh()
    {
        if (!Svc.Loaded) return;
        var q = Nk.ParseQuery(_nq.Text);
        var all = Svc.NhapKhau.Concat(_nOld.IsChecked == true ? Svc.NhapKhauCu : new List<Nk.Nhom>());
        _nShown = all.Where(n => Nk.Matches(n, q)).OrderByDescending(n => n.Date, StringComparer.Ordinal).ThenBy(n => n.Invoice, StringComparer.Ordinal)
            .ThenBy(n => n.Size, StringComparer.Ordinal).ToList();
        var cols = NkHeads.Select((h, i) => new GridColumn("n" + i, h, NkW[i]) { ReadOnly = true }).ToList();
        var rows = _nShown.Select(n =>
        {
            var s = Nk.Summarize(n);
            return (IEnumerable<string?>)new string?[]
            {
                Dmy(n.Date), n.So, n.Country, n.Invoice, n.Contract, n.Tp, n.Hang, n.Size, Dmy(n.NgayNhap), s.Total.ToString(), s.Kien.ToString("#,0"),
                s.Text, s.NhiemText, n.IsHistory ? $"Đã thay bởi phiếu {n.ReplacedBy}" : "",
            };
        });
        _nm.Load(cols, rows); _ng.Refresh();
        _nInfo.Text = Svc.NhapKhau.Count == 0
            ? "Chưa có phiếu tôm nhập khẩu. Bấm \"Nạp PDF phiếu tôm nhập khẩu…\" để nạp (nhiều file một lần được)."
            : $"{_nShown.Count}/{Svc.NhapKhau.Count} dòng (mỗi dòng = 1 Size của 1 phiếu) · gõ TP / INV / Contract / Size / nước / số mẫu để lọc";
        NkCards(q);
        if (_nShown.Count > 0) { _ng.Select(0, 0); NkShowMau(_nShown[0]); } else NkShowMau(null);
    }

    /// <summary>Thẻ kết quả cho từ khóa đang gõ (vd "TP143 size 31/40"): đạt / nhiễm, chỉ tiêu nhiễm, đặc tính.</summary>
    private void NkCards(Nk.Query q)
    {
        _nCards.Children.Clear();
        if (q.Empty) return;
        var hits = _nShown.Where(n => !n.IsHistory).ToList();
        if (hits.Count == 0)
        {
            var other = q.Size is not null && q.Tp is not null ? Svc.NhapKhau.Where(n => n.Tp.TrimStart('0') == q.Tp).Select(n => n.Size).Distinct().ToList() : new();
            _nCards.Children.Add(Card(null, "Không có kết quả khớp.", other.Count > 0 ? $"TP-{q.Tp} chỉ có Size {string.Join(", ", other)}." : "Kiểm tra lại TP / INV / Size hoặc nạp phiếu PDF."));
            return;
        }
        foreach (var n in hits.Take(6))
        {
            var s = Nk.Summarize(n); var bad = s.Nhiem > 0 || !s.AllSame;
            var l1 = $"{n.Label} · {n.Country} · phiếu {n.So} · phân tích {Dmy(n.Date)}";
            var l2 = s.Text + (s.Nhiem > 0 ? "" : "");
            var l3 = (s.Nhiem > 0 ? $"Mẫu nhiễm: {s.NhiemText} → không đi MKS (xem đặc tính từng mẫu)\n" : "") + $"Nhập {Dmy(n.NgayNhap)} · tổng {s.Kien:#,0} kiện · Contract {n.Contract}";
            _nCards.Children.Add(Card(bad ? NkAmber : NkGreen, l1, l2, l3));
        }
        if (hits.Count > 6) _nCards.Children.Add(Muted($"… còn {hits.Count - 6} dòng nữa, xem bảng bên dưới."));
    }
    private static Border Card(IBrush? bg, params string[] lines)
    {
        var sp = new StackPanel { Spacing = 2 };
        for (int i = 0; i < lines.Length; i++) if (lines[i].Length > 0) sp.Children.Add(new TextBlock { Text = lines[i], FontSize = i == 1 ? 14 : 12.5, FontWeight = i <= 1 ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap });
        return new Border { Background = bg ?? AppTheme.Brush("#F3F6FA"), BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8), Child = sp };
    }

    private void NkShowMau(Nk.Nhom? n)
    {
        _nMau = n?.Samples ?? new();
        var cols = NkMauHeads.Select((h, i) => new GridColumn("m" + i, h, i == 1 ? 190 : i == 0 ? 50 : i == 2 ? 50 : i == 3 ? 90 : i < 11 ? 58 : i == 11 ? 200 : i == 12 ? 120 : 140) { ReadOnly = true }).ToList();
        _nsm.Load(cols, _nMau.Select(m => (IEnumerable<string?>)new string?[] { m.Stt.ToString(), m.Ten, m.Kien.ToString(), Dmy(m.NgayNhap) }
            .Concat(FieldKeys.Select(k => (string?)(m.Inputs.GetValueOrDefault(k) ?? ""))).Concat(new string?[] { m.Kq, m.DatTinh, m.GhiChu }).ToArray()));
        _nsg.Refresh();
    }

    private IBrush? NkBg(int r, int c)
    {
        if (r < 0 || r >= _nShown.Count) return null;
        if (c == 11 || c == 12) { var s = Nk.Summarize(_nShown[r]); return s.Nhiem > 0 || !s.AllSame ? NkAmber : c == 11 ? NkGreen : null; }
        return null;
    }
    private IBrush? NkMauBg(int r, int c)
    {
        if (r < 0 || r >= _nMau.Count) return null;
        var m = _nMau[r];
        if (c >= 4 && c < 11) return m.Nhiem.Any(x => x.Key == FieldKeys[c - 4]) ? NkAmber : null;
        if (c >= 11 && m.IsNhiem) return NkAmber;
        return null;
    }

    private async Task NkDelete()
    {
        if (_ng.Row < 0 || _ng.Row >= _nShown.Count) { await Ui.Message(this, "Chọn một dòng trong bảng trước."); return; }
        var n = _nShown[_ng.Row];
        if (!await Ui.Confirm(this, $"Xóa {n.Label} (phiếu {n.So}, {n.Samples.Count} mẫu)?", "Xóa tôm nhập khẩu", "Xóa")) return;
        await Act(() => Svc.DeleteNhapKhau(new[] { n }));
        NkRefresh();
    }

    /// <summary>Nạp phiếu tôm nhập khẩu đã đọc: tóm tắt từng Size, cảnh báo đọc, phiếu cập nhật thay bản cũ.</summary>
    private async Task NkImport(List<Nk.Phieu> list)
    {
        if (!await NkConfirm(list)) return;
        Nk.Phieu? first = list.FirstOrDefault();
        KhangSinhService.NkResult? res = null;
        try { await Task.Run(() => res = Svc.ImportNhapKhau(list)); }
        catch (Exception ex) { await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        RenderInfo(); NkRefresh();
        _tabs.SelectedIndex = 6;
        _nq.Text = list.Count == 1 && first is not null ? $"INV {first.Invoice}" : "";
        _pInfo.Text = "";
        await Ui.Message(this, $"Đã nạp: {res!.Added} dòng mới · {res.Replaced} dòng thay bản cũ · {res.Same} dòng đã có (giữ nguyên)." + (res.Notes.Count > 0 ? "\n" + string.Join("\n", res.Notes) : ""));
    }

    /// <summary>
    /// 1.182: hộp xác nhận nạp — trước là 1 khối chữ (mỗi file / Size vài dòng) nên nạp nhiều file thì hộp cao hơn màn hình, khuất nút Nạp.
    /// Nay: dòng tóm tắt · bảng mỗi Size 1 dòng (mẫu nhiễm tô vàng, cột Nạp = Mới / Thay phiếu cũ) · khung cảnh báo đọc có thanh cuộn ·
    /// nút luôn ở đáy, cửa sổ kéo giãn được.
    /// </summary>
    internal async Task<bool> NkConfirm(List<Nk.Phieu> list)
    {
        var conf = Svc.NkConflicts(list);
        var cols = new List<GridColumn>
        {
            new("file", "File", 170), new("so", "Phiếu", 78), new("d", "Ngày PT", 84), new("c", "Nước", 70), new("inv", "INVOICE", 140),
            new("ct", "Contract", 150), new("h", "Hàng", 100), new("sz", "Size", 60), new("mau", "Mẫu · đặc tính", 220), new("kien", "Kiện", 64),
            new("nhiem", "Mẫu nhiễm", 200), new("nap", "Nạp", 150),
        };
        var rows = new List<string?[]>(); var nhiemRow = new HashSet<int>(); var thayRow = new HashSet<int>();
        int mau = 0;
        foreach (var p in list)
            foreach (var (h, sz, ms) in p.BySize())
            {
                var n = Nk.Nhom.Parse("x", Nk.ToRecords(p, "").First(r => KhangSinhCore.Str(r["size"]) == sz).ToJsonString())!;
                var t = Nk.Summarize(n); mau += t.Total;
                var old = conf.FirstOrDefault(c => c.NewSo == p.So && c.Size == sz);
                if (t.Nhiem > 0) nhiemRow.Add(rows.Count);
                if (old.Old is not null) thayRow.Add(rows.Count);
                rows.Add(new string?[] { p.File, p.So, Dmy(p.Date), p.Country, p.Invoice, p.Contract + (p.Tp.Length > 0 ? $" (TP {p.Tp})" : ""), h, sz, t.Text,
                    t.Kien.ToString("#,0", Ui.Vi), t.Nhiem > 0 ? t.NhiemText : "", old.Old is not null ? $"Thay phiếu {old.Old.So}" : "Mới" });
            }
        var model = new GridModel { ReadOnly = true };
        model.Load(cols, rows);
        var grid = new SheetGrid(model)
        {
            SpareRows = 0, MinRows = 3,
            CellBackground = (r, c) => nhiemRow.Contains(r) && c >= 8 ? NkAmber : thayRow.Contains(r) && c == 11 ? AppTheme.Brush("#EEEBFC") : null,
        };
        int loi = list.Sum(p => p.Issues.Count(i => i.StartsWith("•"))), ghi = list.Sum(p => p.Issues.Count(i => !i.StartsWith("•")));
        var warn = new System.Text.StringBuilder();
        foreach (var p in list.Where(p => p.Issues.Count > 0))
        {
            warn.AppendLine("■ " + p.File);
            foreach (var i in p.Issues) warn.AppendLine("   " + i);
        }
        var head = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Margin = new Thickness(0, 0, 0, 8),
            Text = $"{list.Count} file · {rows.Count} dòng (mỗi Size 1 dòng) · {mau:#,0} mẫu"
                + (nhiemRow.Count > 0 ? $" · {nhiemRow.Count} dòng có mẫu nhiễm (tô vàng)" : " · không có mẫu nhiễm")
                + (thayRow.Count > 0 ? $" · {thayRow.Count} dòng thay bản cũ (bản cũ giữ trong lịch sử)" : "")
                + (loi > 0 ? $" · {loi} cảnh báo đọc — xem khung dưới" : ""),
        };
        var warnBox = new Border
        {
            BorderBrush = AppTheme.Brush(loi > 0 ? "#E0B25C" : "#D8DEE6"), Background = AppTheme.Brush(loi > 0 ? "#FFF2D6" : "#F6F8FA"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), Margin = new Thickness(0, 8, 0, 0),
            Child = new ScrollViewer
            {
                MaxHeight = 150, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new SelectableTextBlock
                {
                    TextWrapping = TextWrapping.Wrap, FontSize = 12.5,
                    Text = warn.Length == 0 ? "Đọc đủ thông tin, không có cảnh báo." : (loi > 0 ? $"{loi} cảnh báo cần xem" : "Không có cảnh báo") + (ghi > 0 ? $" · {ghi} ghi chú" : "") + ":\n" + warn.ToString().TrimEnd(),
                },
            },
        };
        var ok = new Button { Content = $"Nạp {rows.Count} dòng", IsDefault = true, Classes = { "primary" } };
        var no = new Button { Content = "Đóng", IsCancel = true };
        var body = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); body.Children.Add(head);
        var btns = Ui.Buttons(no, ok); DockPanel.SetDock(btns, Dock.Bottom); body.Children.Add(btns);
        DockPanel.SetDock(warnBox, Dock.Bottom); body.Children.Add(warnBox);
        body.Children.Add(grid);
        var w = new Window
        {
            Title = "Nạp phiếu tôm nhập khẩu", Width = 1180, Height = 600, MinWidth = 640, MinHeight = 360, CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = AppTheme.Card,
            Content = new Border { Padding = new Thickness(18, 14), Child = body },
        };
        if (Ui.Owner(this) is { } owner)
        {
            var sc = owner.Screens.ScreenFromVisual(owner)?.WorkingArea;
            if (sc is { } a) { var k = owner.RenderScaling; w.Width = Math.Min(w.Width, a.Width / k - 40); w.Height = Math.Min(w.Height, a.Height / k - 60); }
        }
        var result = false;
        ok.Click += (_, _) => { result = true; w.Close(); };
        no.Click += (_, _) => w.Close();
        LastNkDialog = w;
        await Ui.Show(this, w);
        return result;
    }
    /// <summary>Công cụ chụp: hộp xác nhận nạp tôm nhập khẩu vừa mở.</summary>
    internal static Window? LastNkDialog;
}

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
        var sb = new System.Text.StringBuilder();
        foreach (var p in list)
        {
            sb.AppendLine($"■ {p.File}\n   Phiếu {p.So} · phân tích {Dmy(p.Date)} · {p.Country} · INV {p.Invoice} · Contract {p.Contract}" + (p.Tp.Length > 0 ? $" (TP {p.Tp})" : ""));
            foreach (var (h, sz, mau) in p.BySize())
            {
                var n = Nk.Nhom.Parse("x", Nk.ToRecords(p, "").First(r => KhangSinhCore.Str(r["size"]) == sz).ToJsonString())!;
                var s = Nk.Summarize(n);
                sb.AppendLine($"   {h} {sz}: {s.Text} · {s.Kien:#,0} kiện" + (s.Nhiem > 0 ? $"\n      ⚠ mẫu nhiễm: {s.NhiemText}" : ""));
            }
            foreach (var i in p.Issues.Take(8)) sb.AppendLine("   " + i);
            if (p.Issues.Count > 8) sb.AppendLine($"   … còn {p.Issues.Count - 8} cảnh báo.");
        }
        var conf = Svc.NkConflicts(list);
        if (conf.Count > 0)
        {
            sb.AppendLine("\nĐã có phiếu cùng INV + Contract + Size — bản mới sẽ THAY bản cũ (bản cũ giữ trong lịch sử):");
            foreach (var (old, neu, sz) in conf.Take(10)) sb.AppendLine($"   • {old.Label}: phiếu {old.So} → phiếu {neu}");
        }
        if (!await Ui.Confirm(this, sb.ToString().TrimEnd(), "Nạp phiếu tôm nhập khẩu", "Nạp")) return;
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
}

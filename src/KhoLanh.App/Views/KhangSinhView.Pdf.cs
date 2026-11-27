using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.KhangSinh;
using KhoLanh.Core.Services;
using P = KhoLanh.Core.KhangSinh.KhangSinhPdf;

namespace KhoLanh.App.Views;

/// <summary>
/// Tab "Nạp PDF phiếu": đọc phiếu kết quả lần 1 (Kho) / kiểm lại (MKS chờ) như app cũ — 2 cách đọc độc lập, đối soát,
/// bảng sửa tay, ảnh trang PDF gốc bên cạnh có khung đúng hàng mẫu đang chọn; mục cảnh báo phải tick "Đã so" mới nhập được.
/// PDF không phải phiếu (bảng chung) → chuyển sang khớp cột ở tab Nhập từ Excel.
/// </summary>
public sealed partial class KhangSinhView
{
    private static readonly IBrush Amber = AppTheme.Brush("#9C6500");
    private static readonly IBrush Green = AppTheme.Brush("#2E7D32");
    private static readonly IBrush SelBox = AppTheme.Brush("#1E6FD9");

    private List<P.Phieu> _pf = new();
    private List<P.Sample> _pRows = new();
    private readonly GridModel _qm = new();
    private SheetGrid _qg = null!;
    private bool _qLoading;
    private readonly TextBlock _pInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pBanner = new() { FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _pFileIss = new() { Spacing = 4 };
    private readonly TextBlock _pSum = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _pOnlyBad = new() { Content = "Chỉ hiện mẫu có cảnh báo", FontSize = 12.5 };
    private readonly CheckBox _pKeep = new() { Content = "Lưu cả mẫu KHÔNG ghi số lô thành dòng riêng", IsChecked = true, FontSize = 12.5 };
    private readonly Button _pRun = new() { Padding = new Thickness(12, 5), FontSize = 12.5, Classes = { "primary" } };
    private readonly StackPanel _pDet = new() { Spacing = 4 };
    private readonly StackPanel _pBody = new() { Spacing = 8, IsVisible = false };
    // ảnh trang PDF
    private readonly Image _pImg = new() { Stretch = Stretch.Fill };
    private readonly Rectangle _pBox = new() { StrokeThickness = 2.5, Stroke = SelBox, Fill = new SolidColorBrush(Color.FromArgb(28, 30, 111, 217)), IsVisible = false, IsHitTestVisible = false };
    private readonly Canvas _pCanvas = new() { Background = AppTheme.Card };
    private readonly ScrollViewer _pScroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly TextBlock _pPageLbl = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, MinWidth = 120 };
    private readonly TextBlock _pZoomLbl = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 40, TextAlignment = TextAlignment.Center };
    private int _pFi, _pPage = 1, _pRenderSeq;
    private double _pZoom = 1, _pScale = 1;
    private P.Sample? _pSel;

    private bool K1 => _pf.Count > 0 && _pf[0].Kind == P.KindLan1;

    private Control BuildPdf()
    {
        _qg = new SheetGrid(_qm) { SpareRows = 0, CellBackground = QBg };
        _qm.Changed += () => { if (!_qLoading) PdfEdited(); };
        _qg.SelectionChanged += () => { if (!_qLoading && _qg.Row >= 0 && _qg.Row < _pRows.Count) PdfSelect(_pRows[_qg.Row], fromGrid: true); };
        _pOnlyBad.IsCheckedChanged += (_, _) => PdfFill(keepSel: true);
        _pRun.Click += async (_, _) => await PdfImport();
        _pCanvas.Children.Add(_pImg); _pCanvas.Children.Add(_pBox);
        _pCanvas.PointerPressed += (_, e) => PdfClickPage(e.GetPosition(_pCanvas));
        _pScroll.Content = _pCanvas;

        var p = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var top = new StackPanel { Spacing = 8 };
        top.Children.Add(Muted("Chọn 1 hoặc nhiều file PDF phiếu kết quả: phiếu lần 1 \"Kết quả kiểm kháng sinh nguyên liệu (Kho)\" hoặc phiếu kiểm lại (2 cột Kết quả lần 1 / lần 2). "
            + "App đọc mỗi phiếu bằng 2 cách độc lập (theo cột và theo thứ tự chữ trong file) rồi đối soát Lô · Mẫu · Đặt tính; mục lệch phải xem ảnh phiếu gốc bên phải và tick \"Đã so\" mới nhập được. "
            + "Phiếu lần 1: lô (Ngày nhập + Số lô) đã có thì cập nhật kết quả lần 1. Phiếu kiểm lại: mẫu ghi \"Lô: N (ngày)\" được gắn vào lô đó. PDF dạng bảng khác được chuyển sang khớp cột như Excel. Phiếu TÔM NHẬP KHẨU (không có lô: INV · Contract · TP · Size) được nhận ra tự động và nạp sang tab \"Tôm nhập khẩu\"."));
        top.Children.Add(Wrap(Btn("Chọn file PDF…", async () => await PickPdf(), primary: true), Btn("Chọn lại", PdfReset), _pInfo));
        DockPanel.SetDock(top, Dock.Top); p.Children.Add(top);

        _pBody.Children.Add(_pBanner);
        _pBody.Children.Add(_pFileIss);
        var bar = Wrap(_pSum, _pOnlyBad, _pKeep);
        if (_edit) bar.Children.Add(_pRun);
        _pBody.Children.Add(bar);
        DockPanel.SetDock(_pBody, Dock.Top); p.Children.Add(_pBody);

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("11*,6,8*"), Margin = new Thickness(0, 6, 0, 0), IsVisible = false };
        var left = new DockPanel();
        var det = new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Padding = new Thickness(10, 6), Margin = new Thickness(0, 6, 0, 0), MaxHeight = 210, Child = new ScrollViewer { Content = _pDet } };
        DockPanel.SetDock(det, Dock.Bottom); left.Children.Add(det);
        left.Children.Add(Framed(_qg));
        split.Children.Add(left);
        var gs = new GridSplitter { Background = Line_, ResizeDirection = GridResizeDirection.Columns }; Grid.SetColumn(gs, 1); split.Children.Add(gs);
        var right = new DockPanel();
        var pbar = Wrap(Btn("‹", () => PdfStepPage(-1), tip: "Trang trước"), _pPageLbl, Btn("›", () => PdfStepPage(1), tip: "Trang sau"),
            Btn("−", () => PdfZoom(_pZoom / 1.25), tip: "Thu nhỏ"), _pZoomLbl, Btn("+", () => PdfZoom(_pZoom * 1.25), tip: "Phóng to"), Btn("Vừa khung", () => PdfZoom(1)));
        DockPanel.SetDock(pbar, Dock.Top); right.Children.Add(pbar);
        right.Children.Add(Framed(_pScroll));
        Grid.SetColumn(right, 2); split.Children.Add(right);
        _pBody.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) split.IsVisible = _pBody.IsVisible; };
        _pScroll.SizeChanged += (_, e) => { if (_pf.Count > 0 && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 30) _ = PdfShowPage(_pFi, _pPage); };
        p.Children.Add(split);
        return p;
    }

    private void PdfReset()
    {
        _pf = new(); _pRows = new(); _pSel = null; _pBody.IsVisible = false; _pInfo.Text = "";
        _pImg.Source = null; _pBox.IsVisible = false;
    }

    private async Task PickPdf()
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Chọn file PDF phiếu kết quả kháng sinh", AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } },
        });
        var paths = files.Select(f => f.TryGetLocalPath()).Where(x => x is not null).Cast<string>().ToList();
        if (paths.Count > 0) await LoadPdfFiles(paths);
    }

    /// <summary>Đọc các file PDF: cùng loại phiếu → đối soát; không phải phiếu → bảng chung sang tab Nhập từ Excel.</summary>
    internal async Task LoadPdfFiles(IReadOnlyList<string> paths)
    {
        _pInfo.Text = "Đang đọc file PDF…";
        List<P.Phieu>? found = null; List<P.PdfTable>? tables = null; var failed = new List<string>(); List<KhangSinhNhapKhau.Phieu>? nk = null;
        try
        {
            await Task.Run(() =>
            {
                // 1.175: phiếu tôm nhập khẩu (không có lô) — mọi file đều là loại này thì đi luồng riêng
                var nkl = new List<KhangSinhNhapKhau.Phieu>();
                foreach (var path in paths)
                {
                    KhangSinhNhapKhau.Phieu? x = null;
                    try { x = KhangSinhNhapKhau.Read(File.ReadAllBytes(path), System.IO.Path.GetFileName(path)); } catch { x = null; }
                    if (x is null) { nkl.Clear(); break; }
                    nkl.Add(x);
                }
                if (nkl.Count > 0) { nk = nkl; return; }
                var list = new List<P.Phieu>();
                foreach (var path in paths)
                {
                    P.Phieu? r = null;
                    try { r = P.Read(File.ReadAllBytes(path), System.IO.Path.GetFileName(path)); } catch { r = null; }
                    if (r is null || (list.Count > 0 && list[0].Kind != r.Kind)) { list.Clear(); break; }
                    list.Add(r);
                }
                if (list.Count > 0)
                {
                    for (int fi = 0; fi < list.Count; fi++) foreach (var s in list[fi].Samples) s.Fi = fi;
                    Svc.AttachSavedLan1(list);
                    foreach (var r in list) P.CheckAll(r);
                    found = list; return;
                }
                tables = new();
                foreach (var path in paths)
                {
                    try { var t = P.Table(File.ReadAllBytes(path)); if (t.Headers.Count > 0 && t.Rows.Count > 0) tables.Add(t); else failed.Add(System.IO.Path.GetFileName(path)); }
                    catch { failed.Add(System.IO.Path.GetFileName(path)); }
                }
            });
        }
        catch (Exception ex) { _pInfo.Text = ""; await Ui.Message(this, Ui.Describe(ex), "Không đọc được PDF"); return; }

        if (nk is not null) { _pInfo.Text = ""; await NkImport(nk); return; }
        if (found is not null)
        {
            _pf = found; _pSel = null; _pZoom = 1; _pImg.Source = null; _pBox.IsVisible = false; _pRenderSeq++;
            _pBody.IsVisible = true;
            _pKeep.IsVisible = !K1;
            _pInfo.Text = $"Đã đọc {_pf.Sum(r => r.Samples.Count)} {(K1 ? "lô" : "mẫu")} từ {_pf.Count} file — đối soát bên dưới.";
            PdfFill(keepSel: false);
            var first = _pRows.FirstOrDefault();
            if (first is not null) { _qg.Select(0, 2); PdfSelect(first, fromGrid: false); } else await PdfShowPage(0, 1);
            return;
        }
        PdfReset();
        if (tables is null || tables.Count == 0)
        {
            await Ui.Message(this, "Không thấy phiếu kết quả kháng sinh hay dữ liệu dạng bảng trong file PDF đã chọn (PDF phải có chữ, không phải ảnh chụp). Hãy thử xuất lại dưới dạng Excel.", "Nạp PDF");
            return;
        }
        var m = P.Merge(tables);
        var sheet = new KhangSinhService.SheetIn { Name = tables.Count == 1 ? "PDF" : $"PDF ({tables.Count} file)", Aoa = new List<string[]> { m.Headers.ToArray() }.Concat(m.Rows).ToList(), HeaderIdx = 0 };
        LoadSheets(new List<KhangSinhService.SheetIn> { sheet }, $"{tables.Count}/{paths.Count} file PDF (bảng)");
        _tabs.SelectedIndex = 3;
        await Ui.Message(this, $"File PDF không phải phiếu kết quả lần 1 / kiểm lại nên được đọc như bảng: {m.Rows.Count} dòng từ {tables.Count}/{paths.Count} file (gộp theo tên cột)."
            + (failed.Count > 0 ? $"\nKhông đọc được: {string.Join(", ", failed)}." : "")
            + "\n\nĐã chuyển sang tab Nhập từ Excel — kiểm tra kỹ phần khớp cột và xem trước, vì dò cột từ PDF có thể không chính xác 100%.", "Nạp PDF");
    }

    // ------------------------------------------------------------------ bảng đối soát

    private string[] QHeads() => K1
        ? new[] { "", "STT", "Code / Mã", "Lô · Ngày nhập" }.Concat(P.AB.Select(a => a.Label)).Concat(new[] { "Kết quả", "Đặt tính", "Ghi chú" }).ToArray()
        : new[] { "", "Mẫu", "Tên mẫu", "Ghép vào lô" }.Concat(P.AB.Select(a => a.Label)).Concat(new[] { "KQ lần 1", "KQ lần 2", "Đặt tính", "Ghi chú" }).ToArray();
    private int QEdit0 => 4;                                    // cột Enro
    private int QKq2 => K1 ? 11 : 12;
    private static string Mark(P.Sample s) => !P.Flag(s) ? "✓" : s.Ok ? "✓ đã so" : "!";
    private string RefTxt(P.Sample s)
    {
        var rf = P.LotRef(s);
        if (K1) return rf is null ? "Lô ?" : $"Lô {rf.Batch} · {(rf.Date.Length > 0 ? P.Dmy(rf.Date) : "?")}";
        return rf is null ? "(không ghi lô — dòng riêng)" : $"Lô {string.Join(" + ", rf.Batches ?? new List<string> { rf.Batch })} · {P.Dmy(rf.Date)}";
    }
    private IEnumerable<string?> QRow(P.Sample s)
    {
        var o = new List<string?> { Mark(s), P.N(s.Stt) + (_pf.Count > 1 ? $" · F{s.Fi + 1}" : "") + $" · tr.{s.Page}", K1 ? s.Code : string.Join(" / ", s.Lines), RefTxt(s) };
        o.AddRange(P.AB.Select(a => s.Raw.GetValueOrDefault(a.Key) ?? ""));
        if (!K1) o.Add(s.Kq1 + (s.Lan1Saved is { } l ? $" (đã lưu: {l})" : ""));
        o.Add(s.Kq2); o.Add(s.DatTinh); o.Add(s.GhiChu);
        return o;
    }

    private void PdfFill(bool keepSel)
    {
        var all = _pf.SelectMany(r => r.Samples).ToList();
        IEnumerable<P.Sample> rows = _pOnlyBad.IsChecked == true ? all.Where(P.Flag) : all;
        _pRows = rows.OrderByDescending(P.Flag).ThenBy(s => s.Fi).ThenBy(s => s.Ix).ToList();
        var heads = QHeads();
        var widths = K1 ? new[] { 62, 80, 150, 150 } : new[] { 62, 92, 300, 170 };
        var cols = heads.Select((h, i) =>
        {
            var w = i < 4 ? widths[i] : i < 11 ? 58 : h.StartsWith("KQ lần 1") ? 170 : h == "Ghi chú" ? 150 : h == "Đặt tính" ? 100 : 170;
            var ro = i < 4 || (!K1 && i == 11);
            return new GridColumn("c" + i, h, w) { ReadOnly = ro };
        }).ToList();
        _qLoading = true;
        _qm.Load(cols, _pRows.Select(QRow));
        _qLoading = false;
        _qg.Refresh();
        RenderPdfHeader();
        if (keepSel && _pSel is not null) { var i = _pRows.IndexOf(_pSel); if (i >= 0) { _qLoading = true; _qg.Select(i, Math.Max(_qg.Col, 0)); _qLoading = false; } }
    }

    private void RenderPdfHeader()
    {
        var all = _pf.SelectMany(r => r.Samples).ToList();
        var groups = all.GroupBy(s => s.Group.Length > 0 ? s.Group : "MKS chờ").Select(g => $"{g.Key} ({g.Count()})");
        _pBanner.Text = (K1 ? "Phiếu KQ lần 1 (Kho)" : "Phiếu KQ lần 1 + lần 2") + " · " + string.Join(" | ", _pf.Select(r => (K1 ? "Ngày kiểm " : "Ngày phân tích ") + (r.Date.Length > 0 ? P.Dmy(r.Date) : "?") + (r.So.Length > 0 ? " · Số " + r.So : "") + $" · {r.Samples.Count} mẫu · {r.PageCount} trang"))
            + "\n" + string.Join(" · ", groups)
            + (K1 ? " · lô đã có (cùng Ngày nhập + Số lô) → cập nhật kết quả lần 1, giữ kết quả kiểm lại nếu có"
                  : $" · {all.Count(s => P.LotRef(s) is not null)} mẫu có \"Lô: N (ngày)\" → thêm kết quả kiểm lại vào lô đó");
        _pFileIss.Children.Clear();
        foreach (var r in _pf.Where(r => r.FileIssues.Count > 0))
        {
            var box = new StackPanel { Spacing = 2 };
            box.Children.Add(Label("⚠ " + r.File, bold: true));
            foreach (var i in r.FileIssues) box.Children.Add(IssueLine(i));
            if (P.FileFlag(r))
            {
                var cb = new CheckBox { Content = "Tôi đã so với PDF gốc, phiếu đúng là như vậy", IsChecked = r.FileOk, FontSize = 12.5 };
                var rr = r; cb.IsCheckedChanged += (_, _) => { rr.FileOk = cb.IsChecked == true; RenderPdfHeader(); };
                box.Children.Add(cb);
            }
            _pFileIss.Children.Add(new Border { Background = AmberBg, Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(6), Child = box });
        }
        var flagged = all.Count(P.Flag); var need = all.Count(P.Need); var notes = all.Count(s => s.Issues.Any(i => i.Lv == "note"));
        var fileNeed = _pf.Count(r => P.FileFlag(r) && !r.FileOk);
        _pSum.Text = $"{all.Count} {(K1 ? "lô" : "mẫu")} · " + (flagged == 0 ? "Khớp hết — Lô · Mẫu · Đặt tính" + (notes > 0 ? $" · {notes} mẫu có lưu ý KQ lần 1/2 (không chặn)" : "") : $"{flagged} có cảnh báo · còn {need} chưa kiểm");
        _pSum.Foreground = need > 0 ? Red : Green;
        var blocked = need + fileNeed;
        _pRun.IsEnabled = blocked == 0;
        _pRun.Content = blocked > 0 ? $"Còn {blocked} mục cần kiểm trước khi nhập" : K1 ? $"Nhập kết quả lần 1 ({all.Count} lô)" : $"Nhập kết quả kiểm lại ({all.Count} mẫu)";
    }

    private static TextBlock IssueLine(P.Issue i) => new()
    {
        Text = (i.Lv switch { "err" => "✗ ", "warn" => "⚠ ", "note" => "• ", _ => "ⓘ " }) + i.Msg, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        Foreground = i.Lv switch { "err" => Red, "warn" => Amber, "note" => Amber, _ => AppTheme.Muted },
    };

    private IBrush? QBg(int r, int c)
    {
        if (r < 0 || r >= _pRows.Count) return null;
        var s = _pRows[r];
        if (c == 0) return !P.Flag(s) ? GreenBg : s.Ok ? AmberBg : RedBg;
        if (c >= QEdit0 && c < QEdit0 + 7) { var v = s.Raw.GetValueOrDefault(P.AB[c - QEdit0].Key) ?? ""; if (P.NumVal.IsMatch(v)) return AmberBg; }
        if (P.Need(s) && c >= 1) return AppTheme.Brush("#FDF0EC");
        return null;
    }

    private void PdfEdited()
    {
        bool any = false;
        for (int i = 0; i < _pRows.Count && i < _qm.RowCount; i++)
        {
            var s = _pRows[i]; bool ch = false;
            for (int k = 0; k < 7; k++)
            {
                var key = P.AB[k].Key; var v = System.Text.RegularExpressions.Regex.Replace(_qm.Get(i, QEdit0 + k), @"\s+", " ").Trim();
                if (v != (s.Raw.GetValueOrDefault(key) ?? "")) { s.Raw[key] = v; s.Inputs[key] = v == "***" ? "" : v; ch = true; }
            }
            string G(int c) => System.Text.RegularExpressions.Regex.Replace(_qm.Get(i, c), @"\s+", " ").Trim();
            if (G(QKq2) != s.Kq2) { s.Kq2 = G(QKq2); ch = true; }
            if (G(QKq2 + 1) != s.DatTinh) { s.DatTinh = G(QKq2 + 1); ch = true; }
            if (G(QKq2 + 2) != s.GhiChu) { s.GhiChu = G(QKq2 + 2); ch = true; }
            if (ch) { s.Edited = true; s.Ok = false; P.CheckSample(s, _pf[s.Fi]); any = true; _qLoading = true; _qm.SetCells(new[] { (i, 0, Mark(s)) }); _qLoading = false; }
        }
        if (!any) return;
        _qg.Refresh(); RenderPdfHeader();
        if (_pSel is not null) RenderPdfDetail(_pSel);
    }

    private void RenderPdfDetail(P.Sample s)
    {
        _pDet.Children.Clear();
        var rf = P.LotRef(s);
        _pDet.Children.Add(Label((K1 ? $"STT {P.N(s.Stt)} · " : $"Mẫu {P.N(s.Stt)} · ") + (K1 ? s.Code : string.Join(" / ", s.Lines)) + $" · trang {s.Page}" + (_pf.Count > 1 ? $" · {_pf[s.Fi].File}" : ""), bold: true));
        if (s.Group.Length > 0) _pDet.Children.Add(Muted(s.Group));
        _pDet.Children.Add(Label(K1
            ? (rf is null ? "Không đọc được Số lô" : $"→ Lô {rf.Batch} · nhập {(rf.Date.Length > 0 ? P.Dmy(rf.Date) : "?")} (lô đã có thì cập nhật kết quả lần 1)")
            : rf is not null ? $"→ cập nhật kết quả kiểm lại của Lô {string.Join(" + ", rf.Batches ?? new List<string> { rf.Batch })} · {P.Dmy(rf.Date)}" + (s.Lan1Saved is { } l ? $" (kết quả lần 1 đã lưu: {l})" : " (chưa có kết quả lần 1 — sẽ tạo lô mới)")
            : "Không ghi lô — lưu thành dòng riêng (nếu bật \"Lưu cả mẫu không ghi số lô\")"));
        var a = s.Alt;
        _pDet.Children.Add(Muted(a is null ? "Cách đọc 2: không đọc được."
            : "Cách đọc 2: " + (a.Lot is not null ? $"Lô {a.Lot.Batch} · " : "") + (a.Code.Length > 0 ? a.Code + " · " : "") + (a.Kq1 is { Length: > 0 } k1 ? k1 + " → " : "") + (a.Kq2.Length > 0 ? a.Kq2 : "—") + " · " + (a.DatTinh.Length > 0 ? a.DatTinh : "—")));
        foreach (var i in s.Issues) _pDet.Children.Add(IssueLine(i));
        if (s.Issues.Count == 0) _pDet.Children.Add(Label("Khớp cả 2 cách đọc.", fg: Green));
        if (P.Flag(s))
        {
            var cb = new CheckBox { Content = "Đã so với PDF gốc — đúng như phiếu" + (s.Edited ? " (đã sửa tay)" : ""), IsChecked = s.Ok, FontSize = 12.5, FontWeight = FontWeight.SemiBold };
            cb.IsCheckedChanged += (_, _) =>
            {
                s.Ok = cb.IsChecked == true;
                var i = _pRows.IndexOf(s); if (i >= 0) { _qLoading = true; _qm.SetCells(new[] { (i, 0, Mark(s)) }); _qLoading = false; _qg.Refresh(); }
                RenderPdfHeader(); PdfDrawBox();
            };
            _pDet.Children.Add(cb);
        }
    }

    private void PdfSelect(P.Sample s, bool fromGrid)
    {
        _pSel = s;
        RenderPdfDetail(s);
        if (!fromGrid) { var i = _pRows.IndexOf(s); if (i >= 0) { _qLoading = true; _qg.Select(i, Math.Max(2, _qg.Col)); _qLoading = false; } }
        if (s.Fi != _pFi || s.Page != _pPage || _pImg.Source is null) _ = PdfShowPage(s.Fi, s.Page); else PdfDrawBox();
    }

    // ------------------------------------------------------------------ ảnh trang PDF gốc + khung mẫu

    private async Task PdfShowPage(int fi, int page)
    {
        if (fi < 0 || fi >= _pf.Count) return;
        var r = _pf[fi]; page = Math.Clamp(page, 1, Math.Max(1, r.PageCount));
        _pFi = fi; _pPage = page;
        var seq = ++_pRenderSeq;
        _pPageLbl.Text = (_pf.Count > 1 ? $"File {fi + 1} · " : "") + $"Trang {page}/{r.PageCount}";
        _pZoomLbl.Text = $"{Math.Round(_pZoom * 100)}%";
        var pg = r.Pages[page - 1];
        var fit = Math.Max(320, (_pScroll.Bounds.Width > 0 ? _pScroll.Bounds.Width : 700) - 20);
        var w = fit * _pZoom;
        WriteableBitmap? bmp = null; string? err = null;
        try { await Task.Run(() => bmp = Controls.PdfRender.Render(r.Bytes, page - 1, (int)Math.Round(w * 2))); }
        catch (Exception e) { err = e is DllNotFoundException ? "thiếu thư viện vẽ PDF" : e.Message; }
        if (seq != _pRenderSeq) return;
        if (bmp is null) { _pPageLbl.Text += $" — không vẽ được ảnh trang ({err})"; return; }
        _pScale = w / pg.Width;
        _pImg.Source = bmp; _pImg.Width = w; _pImg.Height = w * pg.Height / pg.Width;
        _pCanvas.Width = _pImg.Width; _pCanvas.Height = _pImg.Height;
        PdfDrawBox(scroll: true);
    }

    private void PdfDrawBox(bool scroll = true)
    {
        var s = _pSel;
        if (s?.Box is null || s.Fi != _pFi || s.Page != _pPage || _pf.Count == 0) { _pBox.IsVisible = false; return; }
        var pg = _pf[_pFi].Pages[_pPage - 1];
        double top = pg.Bottom + pg.Height;
        double x = (s.Box.X1 - pg.Left) * _pScale, y = (top - s.Box.Y2) * _pScale;
        _pBox.Width = (s.Box.X2 - s.Box.X1) * _pScale; _pBox.Height = (s.Box.Y2 - s.Box.Y1) * _pScale;
        Canvas.SetLeft(_pBox, x); Canvas.SetTop(_pBox, y);
        _pBox.Stroke = P.Need(s) ? Red : SelBox;
        _pBox.IsVisible = true;
        if (!scroll) return;
        var vp = _pScroll.Viewport; var off = _pScroll.Offset;
        double ny = off.Y, nx = off.X;
        if (y - 60 < off.Y || y > off.Y + vp.Height - 120) ny = Math.Max(0, y - 60);
        if (x - 20 < off.X || x > off.X + vp.Width - 200) nx = Math.Max(0, x - 20);
        _pScroll.Offset = new Vector(nx, ny);
    }

    private void PdfStepPage(int d)
    {
        if (_pf.Count == 0) return;
        var r = _pf[_pFi];
        if (d < 0) { if (_pPage > 1) _ = PdfShowPage(_pFi, _pPage - 1); else if (_pFi > 0) _ = PdfShowPage(_pFi - 1, _pf[_pFi - 1].PageCount); }
        else { if (_pPage < r.PageCount) _ = PdfShowPage(_pFi, _pPage + 1); else if (_pFi < _pf.Count - 1) _ = PdfShowPage(_pFi + 1, 1); }
    }
    private void PdfZoom(double z) { _pZoom = Math.Clamp(z, 0.5, 4); if (_pf.Count > 0) _ = PdfShowPage(_pFi, _pPage); }

    /// <summary>Bấm vào 1 hàng trên ảnh phiếu → chọn mẫu ở hàng đó.</summary>
    private void PdfClickPage(Point pos)
    {
        if (_pf.Count == 0 || _pScale <= 0) return;
        var pg = _pf[_pFi].Pages[_pPage - 1];
        var py = pg.Bottom + pg.Height - pos.Y / _pScale;
        P.Sample? best = null; double bd = 1e9;
        foreach (var s in _pf[_pFi].Samples.Where(s => s.Page == _pPage && s.Box is not null))
        {
            var d = py >= s.Box!.Y1 && py <= s.Box.Y2 ? 0 : Math.Min(Math.Abs(py - s.Box.Y1), Math.Abs(py - s.Box.Y2));
            if (d < bd) { bd = d; best = s; }
        }
        if (best is not null && bd < 40) PdfSelect(best, fromGrid: false);
    }

    private async Task PdfImport()
    {
        if (_pf.Count == 0) return;
        var n = _pf.Sum(r => r.Samples.Count);
        if (!await Ui.Confirm(this, K1 ? $"Nhập kết quả lần 1 của {n} lô vào Dữ liệu?\nLô đã có (cùng Ngày nhập + Số lô) sẽ được cập nhật kết quả lần 1." : $"Nhập kết quả kiểm lại của {n} mẫu vào Dữ liệu?", "Nạp PDF phiếu", "Nhập")) return;
        string? msg = null; var keep = _pKeep.IsChecked == true; var list = _pf;
        if (!await Act(() => msg = Svc.ImportPdf(list, keep)) || msg is null) return;
        _pInfo.Text = msg;
        await Ui.Message(this, msg, "Nạp PDF phiếu");
    }
}

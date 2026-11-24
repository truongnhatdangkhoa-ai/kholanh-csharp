using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using KhoLanh.App.Controls;
using KhoLanh.Core.Bang;
using KhoLanh.Core.Scan;
using SkiaSharp;

namespace KhoLanh.App.Views;

/// <summary>
/// Công cụ "Xử lý PDF &amp; Excel" (M21 của app cũ, C#): PDF — gộp, tách / lấy trang, xoay / xóa trang, đánh số / chữ chìm / hình đóng dấu,
/// xóa trang trắng, giảm dung lượng, ảnh → PDF, PDF → ảnh (PDFium gói sẵn); Excel — gộp nhiều file / sheet, tách theo cột / số dòng, làm sạch.
/// File gốc không bao giờ bị ghi đè: kết quả luôn là file mới.
/// </summary>
public sealed class XuLyTepView : UserControl, IPage
{
    public string Title => "Xử lý PDF & Excel";
    public string Subtitle => "Gộp, tách, xoay, đóng dấu, nén PDF; ảnh ↔ PDF; gộp / tách / làm sạch Excel — chỉ tạo file mới, file gốc giữ nguyên.";

    private static IBrush B(string c) => AppTheme.Brush(c);
    private static readonly IBrush Dark = B("#0B2A45"), Line_ = B("#D0D7E2");
    private readonly TextBlock _status = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ContentControl _pdfBody = new(), _xlsBody = new();

    private static readonly (string k, string t)[] PdfModes = { ("merge", "Gộp nhiều PDF"), ("split", "Tách / lấy trang"), ("rot", "Xoay / xóa trang"), ("stamp", "Đánh số · chữ chìm · con dấu"), ("blank", "Xóa trang trắng"), ("shrink", "Giảm dung lượng"), ("img2pdf", "Ảnh → PDF"), ("pdf2img", "PDF → ảnh") };
    private static readonly (string k, string t)[] XlsModes = { ("merge", "Gộp nhiều file / sheet"), ("split", "Tách bảng"), ("clean", "Làm sạch dữ liệu") };

    public XuLyTepView()
    {
        var tabs = new TabControl { ItemsSource = new[] { new TabItem { Header = "PDF", Content = Section(PdfModes, _pdfBody, BuildPdf) }, new TabItem { Header = "Excel", Content = Section(XlsModes, _xlsBody, BuildXls) } } };
        var root = new DockPanel(); DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status); root.Children.Add(tabs);
        Content = root;
    }

    private Control Section((string k, string t)[] modes, ContentControl body, Func<string, Control> build)
    {
        var list = new ListBox { ItemsSource = modes.Select(m => m.t).ToList(), SelectedIndex = 0, Width = 230 };
        list.SelectionChanged += (_, _) => { if (list.SelectedIndex >= 0) { body.Content = build(modes[list.SelectedIndex].k); _status.Text = ""; } };
        body.Content = build(modes[0].k);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        g.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 4, 8, 0), Child = list });
        var sv = new ScrollViewer { Content = body, Padding = new Thickness(14, 4, 8, 4) }; Grid.SetColumn(sv, 1); g.Children.Add(sv);
        return g;
    }

    // ------------------------------------------------------------ tiện ích
    private static TextBlock H(string t) => new() { Text = t, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Dark, Margin = new Thickness(0, 0, 0, 4) };
    private static TextBlock Muted(string t) => new() { Text = t, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Lb(string t) => new() { Text = t, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private static Button Btn(string t, Func<Task> click, bool primary = false) { var b = new Button { Content = t, Padding = new Thickness(10, 4) }; if (primary) b.Classes.Add("primary"); b.Click += async (_, _) => await click(); return b; }
    private static StackPanel Row(params Control[] c) { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var x in c) s.Children.Add(x); return s; }
    private static ComboBox Combo(params string[] items) => new() { ItemsSource = items, SelectedIndex = 0, MinWidth = 220 };
    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
    private static string Base(string path) => Path.GetFileNameWithoutExtension(path);
    private static string Size(long b) => b >= 1048576 ? (b / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : Math.Max(1, b / 1024) + " KB";

    private async Task<List<string>> PickFiles(string title, string[] ext, bool many)
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return new();
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = many, FileTypeFilter = new[] { new FilePickerFileType(string.Join(", ", ext)) { Patterns = ext.Select(e => "*." + e).ToArray() } } });
        return files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
    }

    private async Task Do(string busy, Func<Task<string>> work)
    {
        _status.Foreground = Dark; _status.Text = busy;
        try { _status.Text = await work(); _status.Foreground = B("#1B5E20"); }
        catch (Exception e) { _status.Text = "⛔ " + e.Message; _status.Foreground = B("#B71C1C"); }
    }

    private async Task<string> SaveBytes(byte[] data, string suggested, string ext, string type)
    {
        var path = await Ui.SaveFile(this, "Lưu file", suggested, ext, type); if (path is null) return "Đã hủy.";
        await File.WriteAllBytesAsync(path, data); Ui.OpenExternal(Path.GetDirectoryName(path)!);
        return $"✔ Đã lưu {Path.GetFileName(path)} ({Size(data.Length)}).";
    }

    /// <summary>Ô chọn 1 file PDF; trả hàm lấy (bytes, tên, số trang).</summary>
    private (Control ui, Func<(byte[] pdf, string name, int pages)?> get) OnePdf()
    {
        (byte[] pdf, string name, int pages)? cur = null;
        var info = Muted("Chưa chọn file.");
        var pick = new Button { Content = "Chọn file PDF…", Padding = new Thickness(10, 4) };
        var thumbs = new WrapPanel();
        var strip = new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6), IsVisible = false, Child = new ScrollViewer { MaxHeight = 250, Content = thumbs } };
        var seq = 0;
        async Task Load(string f)
        {
            var my = ++seq; thumbs.Children.Clear(); strip.IsVisible = false;
            try { var b = await File.ReadAllBytesAsync(f); var n = await Task.Run(() => PdfRender.Count(b, Path.GetFileName(f))); cur = (b, f, n); info.Text = $"{Path.GetFileName(f)} — {n} trang · {Size(b.Length)}"; }
            catch (Exception e) { cur = null; info.Text = e.Message; return; }
            // xem trước: tối đa 60 trang đầu, vẽ nền lần lượt
            var (pdf, _, pages) = cur.Value; strip.IsVisible = true;
            for (var i = 0; i < Math.Min(pages, MaxThumbs); i++)
            {
                if (my != seq) return;
                Avalonia.Media.Imaging.WriteableBitmap? bmp = null; var idx = i;
                try { bmp = await Task.Run(() => PdfRender.Render(pdf, idx, 150)); } catch { /* trang lỗi: bỏ ảnh */ }
                if (my != seq) return;
                var cell = new StackPanel { Margin = new Thickness(4), Width = 104 };
                cell.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Background = Avalonia.Media.Brushes.White, Child = bmp is null ? new TextBlock { Text = "?", HorizontalAlignment = HorizontalAlignment.Center } : new Image { Source = bmp, Width = 100, Stretch = Stretch.Uniform } });
                cell.Children.Add(new TextBlock { Text = "Trang " + (i + 1), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
                thumbs.Children.Add(cell);
            }
            if (pages > MaxThumbs) thumbs.Children.Add(Muted($"… còn {pages - MaxThumbs} trang (chỉ xem trước {MaxThumbs} trang đầu)."));
        }
        pick.Click += async (_, _) => { var f = (await PickFiles("Chọn file PDF", new[] { "pdf" }, false)).FirstOrDefault(); if (f is not null) await Load(f); };
        _loadPdf = Load;
        var box = new StackPanel { Spacing = 6 }; box.Children.Add(Row(pick, info)); box.Children.Add(strip);
        return (box, () => cur);
    }
    private const int MaxThumbs = 60;
    private Func<string, Task>? _loadPdf;
    /// <summary>Nạp file PDF vào ô chọn file của chức năng đang mở (công cụ chụp màn hình dùng).</summary>
    public Task LoadPdfForTest(string path) => _loadPdf?.Invoke(path) ?? Task.CompletedTask;

    // ============================================================ PDF
    private Control BuildPdf(string k)
    {
        var s = new StackPanel { Spacing = 10, MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        switch (k)
        {
            case "merge":
            {
                s.Children.Add(H("Gộp nhiều PDF thành 1 file")); s.Children.Add(Muted("Chọn các file theo thứ tự; dùng ▲ ▼ để đổi thứ tự."));
                var files = new List<string>(); var list = new StackPanel { Spacing = 2 };
                void Paint()
                {
                    list.Children.Clear();
                    for (var i = 0; i < files.Count; i++)
                    {
                        var idx = i; var up = new Button { Content = "▲", IsEnabled = i > 0 }; var dn = new Button { Content = "▼", IsEnabled = i < files.Count - 1 }; var rm = new Button { Content = "✕" };
                        up.Click += (_, _) => { (files[idx], files[idx - 1]) = (files[idx - 1], files[idx]); Paint(); }; dn.Click += (_, _) => { (files[idx], files[idx + 1]) = (files[idx + 1], files[idx]); Paint(); }; rm.Click += (_, _) => { files.RemoveAt(idx); Paint(); };
                        list.Children.Add(Row(Lb($"{i + 1}. {Path.GetFileName(files[i])}"), up, dn, rm));
                    }
                }
                s.Children.Add(Row(Btn("+ Thêm file PDF…", async () => { files.AddRange(await PickFiles("Chọn file PDF", new[] { "pdf" }, true)); Paint(); }), Btn("Xóa danh sách", () => { files.Clear(); Paint(); return Task.CompletedTask; })));
                s.Children.Add(list);
                s.Children.Add(Btn("Gộp & lưu", () => Do("Đang gộp…", async () =>
                {
                    if (files.Count < 2) throw new InvalidOperationException("Cần ít nhất 2 file.");
                    var data = await Task.Run(() => PdfRender.Merge(files.Select(f => (File.ReadAllBytes(f), Path.GetFileName(f))).ToList()));
                    return await SaveBytes(data, $"gop-{Stamp()}.pdf", "pdf", "PDF");
                }), true));
                break;
            }
            case "split":
            {
                s.Children.Add(H("Tách / lấy trang"));
                var (ui, get) = OnePdf(); s.Children.Add(ui);
                var how = Combo("Lấy một số trang thành 1 file mới", "Tách MỖI trang thành 1 file", "Tách mỗi N trang thành 1 file");
                var range = new TextBox { Watermark = "VD: 1-3, 5, 8-", Width = 220 }; var n = new TextBox { Text = "2", Width = 60 };
                s.Children.Add(how); s.Children.Add(Row(Lb("Trang"), range, Lb("N ="), n));
                s.Children.Add(Btn("Tách & lưu", () => Do("Đang tách…", async () =>
                {
                    var src = get() ?? throw new InvalidOperationException("Chưa chọn file PDF.");
                    if (how.SelectedIndex == 0)
                    {
                        var (p, err) = CcBang.ParsePages(range.Text, src.pages); if (err.Length > 0) throw new InvalidOperationException(err);
                        return await SaveBytes(await Task.Run(() => PdfRender.Extract(src.pdf, src.name, p)), $"{Base(src.name)}-tach-{Stamp()}.pdf", "pdf", "PDF");
                    }
                    var step = how.SelectedIndex == 1 ? 1 : Math.Max(1, int.TryParse(n.Text, out var x) ? x : 1);
                    var dir = await Ui.OpenFolder(this, "Chọn thư mục lưu các file tách"); if (dir is null) return "Đã hủy.";
                    var k2 = 0; var pad = ((src.pages + step - 1) / step).ToString().Length;
                    await Task.Run(() => { for (var i = 0; i < src.pages; i += step) { k2++; File.WriteAllBytes(Path.Combine(dir, $"{Base(src.name)}_{k2.ToString().PadLeft(pad, '0')}.pdf"), PdfRender.Extract(src.pdf, src.name, Enumerable.Range(i, Math.Min(step, src.pages - i)).ToList())); } });
                    Ui.OpenExternal(dir); return $"✔ Đã tách thành {k2} file trong {dir}.";
                }), true));
                break;
            }
            case "rot":
            {
                s.Children.Add(H("Xoay / xóa trang"));
                var (ui, get) = OnePdf(); s.Children.Add(ui);
                var pages = new TextBox { Watermark = "VD: 2, 4-6 (để trống khi xoay = mọi trang)", Width = 320 };
                var deg = Combo("Phải 90°", "Trái 90°", "180°");
                s.Children.Add(Row(Lb("Trang"), pages)); s.Children.Add(Row(Lb("Xoay"), deg));
                s.Children.Add(Row(
                    Btn("Xoay & lưu", () => Do("Đang xoay…", async () =>
                    {
                        var src = get() ?? throw new InvalidOperationException("Chưa chọn file PDF.");
                        var p = (pages.Text ?? "").Trim().Length == 0 ? Enumerable.Range(0, src.pages).ToList() : CcBang.ParsePages(pages.Text, src.pages) is var r && r.Error.Length == 0 ? r.Pages : throw new InvalidOperationException(r.Error);
                        var d = new[] { 90, 270, 180 }[Math.Max(0, deg.SelectedIndex)];
                        return await SaveBytes(await Task.Run(() => PdfRender.Rotate(src.pdf, src.name, p, d)), $"{Base(src.name)}-xoay-{Stamp()}.pdf", "pdf", "PDF");
                    }), true),
                    Btn("Xóa các trang & lưu", () => Do("Đang xóa trang…", async () =>
                    {
                        var src = get() ?? throw new InvalidOperationException("Chưa chọn file PDF.");
                        var (p, err) = CcBang.ParsePages(pages.Text, src.pages); if (err.Length > 0) throw new InvalidOperationException(err == "Chưa nhập trang." ? "Nhập các trang cần xóa (ví dụ 2, 4-6)." : err);
                        if (p.Count >= src.pages) throw new InvalidOperationException("Không thể xóa hết các trang.");
                        return await SaveBytes(await Task.Run(() => PdfRender.Delete(src.pdf, src.name, p)), $"{Base(src.name)}-xoa-trang-{Stamp()}.pdf", "pdf", "PDF");
                    }))));
                break;
            }
            case "stamp":
            {
                s.Children.Add(H("Đánh số trang · chữ chìm · hình đóng dấu"));
                var (ui, get) = OnePdf(); s.Children.Add(ui);
                var num = new CheckBox { Content = "Đánh số trang", IsChecked = true }; var fmt = new TextBox { Text = "Trang {n}/{N}", Width = 160 }; var pos = Combo("Dưới giữa", "Dưới phải", "Dưới trái", "Trên giữa", "Trên phải", "Trên trái"); var start = new TextBox { Text = "1", Width = 60 };
                var wm = new CheckBox { Content = "Chữ chìm chéo trang" }; var wmt = new TextBox { Text = "BẢN SAO", Width = 220 }; var wms = new TextBox { Text = "72", Width = 60 }; var wmo = new TextBox { Text = "20", Width = 60 };
                var im = new CheckBox { Content = "Hình đóng dấu (con dấu / chữ ký)" }; string? imgPath = null; var imgInfo = Muted("Chưa chọn hình.");
                var ipick = new Button { Content = "Chọn hình…" }; ipick.Click += async (_, _) => { imgPath = (await PickFiles("Chọn hình con dấu / chữ ký", new[] { "png", "jpg", "jpeg", "bmp", "webp" }, false)).FirstOrDefault(); imgInfo.Text = imgPath is null ? "Chưa chọn hình." : Path.GetFileName(imgPath); };
                var ipos = Combo("Dưới phải", "Dưới trái", "Dưới giữa", "Giữa trang", "Trên phải", "Trên trái"); var ipages = Combo("Tất cả trang", "Chỉ trang đầu", "Chỉ trang cuối"); var iw = new TextBox { Text = "22", Width = 60 }; var key = new CheckBox { Content = "Bỏ nền trắng của hình", IsChecked = true };
                s.Children.Add(num); s.Children.Add(Row(Lb("Mẫu ({n} = số trang, {N} = tổng)"), fmt, Lb("Vị trí"), pos, Lb("Bắt đầu từ"), start));
                s.Children.Add(wm); s.Children.Add(Row(Lb("Chữ"), wmt, Lb("Cỡ"), wms, Lb("Độ đậm %"), wmo));
                s.Children.Add(im); s.Children.Add(Row(ipick, imgInfo)); s.Children.Add(Row(Lb("Vị trí"), ipos, Lb("Trang"), ipages, Lb("Rộng % trang"), iw)); s.Children.Add(key);
                s.Children.Add(Btn("Đóng dấu & lưu", () => Do("Đang xử lý…", async () =>
                {
                    var src = get() ?? throw new InvalidOperationException("Chưa chọn file PDF.");
                    if (num.IsChecked != true && wm.IsChecked != true && im.IsChecked != true) throw new InvalidOperationException("Chọn ít nhất một mục: đánh số trang, chữ chìm hoặc hình đóng dấu.");
                    var o = new PdfRender.StampOpts
                    {
                        Number = num.IsChecked == true, NumFmt = fmt.Text ?? "{n}", NumPos = new[] { "bc", "br", "bl", "tc", "tr", "tl" }[Math.Max(0, pos.SelectedIndex)], NumStart = int.TryParse(start.Text, out var st) ? st : 1,
                        Watermark = wm.IsChecked == true, WmText = wmt.Text ?? "", WmSize = Ui.TryParseNumber(wms.Text, out var ws) ? ws : 72, WmOpacity = (Ui.TryParseNumber(wmo.Text, out var wo) ? wo : 20) / 100,
                        ImgPos = new[] { "br", "bl", "bc", "c", "tr", "tl" }[Math.Max(0, ipos.SelectedIndex)], ImgPages = new[] { "all", "first", "last" }[Math.Max(0, ipages.SelectedIndex)], ImgWidthPct = Ui.TryParseNumber(iw.Text, out var iwv) ? iwv : 22,
                    };
                    if (im.IsChecked == true) { if (imgPath is null) throw new InvalidOperationException("Chưa chọn hình con dấu / chữ ký."); o.Image = LoadStampImage(imgPath, key.IsChecked == true); }
                    return await SaveBytes(await Task.Run(() => PdfRender.Stamp(src.pdf, src.name, o)), $"{Base(src.name)}-dong-dau-{Stamp()}.pdf", "pdf", "PDF");
                }), true));
                break;
            }
            case "blank":
            {
                s.Children.Add(H("Xóa trang trắng")); s.Children.Add(Muted("Hay gặp khi scan 2 mặt: các mặt sau trắng sẽ bị bỏ."));
                var (ui, get) = OnePdf(); s.Children.Add(ui);
                var sens = Combo("Chỉ bỏ trang thật sự trắng", "Bỏ cả trang có vài chấm bẩn do scan", "Mạnh tay (có thể bỏ trang chỉ có vài chữ nhỏ)"); sens.SelectedIndex = 1;
                s.Children.Add(Row(Lb("Độ nhạy"), sens));
                s.Children.Add(Btn("Tìm & xóa trang trắng", () => Do("Đang kiểm tra từng trang…", async () =>
                {
                    var src = get() ?? throw new InvalidOperationException("Chưa chọn file PDF.");
                    var th = new[] { 0.0005, 0.003, 0.01 }[Math.Max(0, sens.SelectedIndex)];
                    var blanks = await Task.Run(() => PdfRender.BlankPages(src.pdf, src.name, th));
                    if (blanks.Count == 0) return "Không có trang trắng nào — file giữ nguyên.";
                    if (blanks.Count >= src.pages) throw new InvalidOperationException("Tất cả các trang đều bị nhận là trắng — không xóa. Thử giảm độ nhạy.");
                    var r = await SaveBytes(await Task.Run(() => PdfRender.Delete(src.pdf, src.name, blanks)), $"{Base(src.name)}-bo-trang-trang-{Stamp()}.pdf", "pdf", "PDF");
                    return r + $" Đã bỏ {blanks.Count} trang: {string.Join(", ", blanks.Select(b => b + 1))}.";
                }), true));
                break;
            }
            case "shrink":
            {
                s.Children.Add(H("Giảm dung lượng")); s.Children.Add(Muted("Vẽ lại từng trang thành ảnh nén (chữ thành ảnh — không chọn / tìm chữ được nữa). Hợp để gửi mail / Zalo."));
                var (ui, get) = OnePdf(); s.Children.Add(ui);
                var q = Combo("Nhẹ — chữ vẫn rõ (150 dpi)", "Vừa — gửi mail / Zalo (110 dpi)", "Mạnh — nhỏ nhất (80 dpi)"); q.SelectedIndex = 1;
                s.Children.Add(Row(Lb("Mức nén"), q));
                s.Children.Add(Btn("Nén & lưu", () => Do("Đang nén…", async () =>
                {
                    var src = get() ?? throw new InvalidOperationException("Chưa chọn file PDF.");
                    var (dpi, qual) = new[] { (150, 75), (110, 60), (80, 50) }[Math.Max(0, q.SelectedIndex)];
                    var data = await Task.Run(() =>
                    {
                        var pages = new List<JpegPdf.Page>();
                        for (var i = 0; i < src.pages; i++)
                        {
                            var (w, h, rgba) = PdfRender.RenderRgbaDpi(src.pdf, i, dpi); var (pw, ph) = PdfRender.PageSize(src.pdf, i);
                            pages.Add(new JpegPdf.Page(ScanImaging.EncodeJpeg(new RgbaImage(w, h, rgba), qual), w, h, false, pw, ph));
                        }
                        return JpegPdf.Build(pages, false, Base(src.name));
                    });
                    var r = await SaveBytes(data, $"{Base(src.name)}-nen-{Stamp()}.pdf", "pdf", "PDF");
                    return r + $" (gốc {Size(src.pdf.Length)} → {Size(data.Length)})";
                }), true));
                break;
            }
            case "img2pdf":
            {
                s.Children.Add(H("Ảnh → PDF")); s.Children.Add(Muted("Mỗi ảnh 1 trang, theo thứ tự chọn. Muốn làm sạch ảnh chụp tài liệu (cắt trang, khử bóng) thì dùng Scan tài liệu."));
                var files = new List<string>(); var info = Muted("Chưa chọn ảnh.");
                var size = Combo("A4 — ảnh nằm vừa trong trang", "Trang vừa khít ảnh");
                s.Children.Add(Row(Btn("Chọn ảnh…", async () => { files = await PickFiles("Chọn ảnh", ScanImaging.ImageExt, true); info.Text = files.Count == 0 ? "Chưa chọn ảnh." : $"{files.Count} ảnh: " + string.Join(", ", files.Take(5).Select(Path.GetFileName)) + (files.Count > 5 ? "…" : ""); }), info));
                s.Children.Add(Row(Lb("Khổ trang"), size));
                s.Children.Add(Btn("Tạo PDF & lưu", () => Do("Đang tạo PDF…", async () =>
                {
                    if (files.Count == 0) throw new InvalidOperationException("Chưa chọn ảnh.");
                    var data = await Task.Run(() => JpegPdf.Build(files.Select(f => { var img = ScanImaging.Decode(File.ReadAllBytes(f)); return new JpegPdf.Page(ScanImaging.EncodeJpeg(img, 88), img.W, img.H, false); }).ToList(), size.SelectedIndex == 0, "Ảnh"));
                    return await SaveBytes(data, $"anh-{Stamp()}.pdf", "pdf", "PDF");
                }), true));
                break;
            }
            case "pdf2img":
            {
                s.Children.Add(H("PDF → ảnh"));
                var (ui, get) = OnePdf(); s.Children.Add(ui);
                var fmt = Combo("JPG (nhẹ)", "PNG (nét, nặng hơn)"); var dpi = Combo("100 dpi", "150 dpi", "200 dpi"); dpi.SelectedIndex = 1;
                s.Children.Add(Row(Lb("Định dạng"), fmt, Lb("Độ phân giải"), dpi));
                s.Children.Add(Btn("Xuất ảnh vào thư mục…", () => Do("Đang xuất ảnh…", async () =>
                {
                    var src = get() ?? throw new InvalidOperationException("Chưa chọn file PDF.");
                    var dir = await Ui.OpenFolder(this, "Chọn thư mục lưu ảnh"); if (dir is null) return "Đã hủy.";
                    var d = new[] { 100, 150, 200 }[Math.Max(0, dpi.SelectedIndex)]; var png = fmt.SelectedIndex == 1; var pad = src.pages.ToString().Length;
                    await Task.Run(() =>
                    {
                        for (var i = 0; i < src.pages; i++)
                        {
                            var (w, h, rgba) = PdfRender.RenderRgbaDpi(src.pdf, i, d); var img = new RgbaImage(w, h, rgba);
                            var name = Path.Combine(dir, $"{Base(src.name)}_trang{(i + 1).ToString().PadLeft(pad, '0')}.{(png ? "png" : "jpg")}");
                            File.WriteAllBytes(name, png ? EncodePng(img) : ScanImaging.EncodeJpeg(img, 88));
                        }
                    });
                    Ui.OpenExternal(dir); return $"✔ Đã xuất {src.pages} ảnh vào {dir}.";
                }), true));
                break;
            }
        }
        return s;
    }

    private static byte[] EncodePng(RgbaImage img)
    {
        using var bmp = new SKBitmap(new SKImageInfo(img.W, img.H, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(img.Px, 0, bmp.GetPixels(), img.Px.Length);
        using var im = SKImage.FromBitmap(bmp); using var d = im.Encode(SKEncodedImageFormat.Png, 90); return d.ToArray();
    }

    /// <summary>Hình đóng dấu → BGRA (tối đa 1200 px); "bỏ nền trắng" = điểm gần trắng thành trong suốt.</summary>
    private static (int W, int H, byte[] Bgra) LoadStampImage(string path, bool keyOut)
    {
        var img = ScanImaging.Decode(File.ReadAllBytes(path), 1200); var bgra = new byte[img.Px.Length];
        for (var p = 0; p < img.Px.Length; p += 4)
        {
            byte r = img.Px[p], g = img.Px[p + 1], b = img.Px[p + 2]; var a = 255;
            if (keyOut) { var lo = Math.Min(r, Math.Min(g, b)); if (lo >= 235) a = 0; else if (lo > 190) a = (int)Math.Round(255 * (235 - lo) / 45.0); }
            bgra[p] = b; bgra[p + 1] = g; bgra[p + 2] = r; bgra[p + 3] = (byte)a;
        }
        return (img.W, img.H, bgra);
    }

    // ============================================================ EXCEL
    private Control BuildXls(string k)
    {
        var s = new StackPanel { Spacing = 10, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
        switch (k)
        {
            case "merge":
            {
                s.Children.Add(H("Gộp nhiều file / sheet Excel"));
                var files = new List<string>(); var info = Muted("Chưa chọn file.");
                var allSheets = new CheckBox { Content = "Lấy mọi sheet của mỗi file (bỏ chọn = chỉ sheet đầu)", IsChecked = false };
                var how = Combo("Xếp chồng thành 1 bảng — ghép cột theo TÊN cột, thêm cột \"Nguồn\"", "Mỗi file / sheet thành 1 sheet riêng trong 1 file");
                s.Children.Add(Row(Btn("Chọn file Excel…", async () => { files = await PickFiles("Chọn file Excel", new[] { "xlsx", "xlsm", "xls", "csv" }, true); info.Text = files.Count == 0 ? "Chưa chọn file." : $"{files.Count} file: " + string.Join(", ", files.Take(4).Select(Path.GetFileName)) + (files.Count > 4 ? "…" : ""); }), info));
                s.Children.Add(allSheets); s.Children.Add(how);
                s.Children.Add(Btn("Gộp & lưu", () => Do("Đang gộp…", async () =>
                {
                    if (files.Count == 0) throw new InvalidOperationException("Chưa chọn file.");
                    var items = await Task.Run(() => files.SelectMany(f => { var sh = ExcelOps.Read(f); var pick = allSheets.IsChecked == true ? sh : sh.Take(1).ToList(); return pick.Select(x => (Name: pick.Count > 1 || allSheets.IsChecked == true ? $"{Path.GetFileName(f)} › {x.Name}" : Path.GetFileName(f), Tbl: ExcelOps.ToTable(x))); }).ToList());
                    var path = await Ui.SaveFile(this, "Lưu file gộp", $"gop-{Stamp()}.xlsx", "xlsx", "Excel"); if (path is null) return "Đã hủy.";
                    var notes = new List<string>();
                    if (how.SelectedIndex == 0) { var (t, n) = CcBang.MergeStack(items); notes = n; ExcelOps.Write(path, new[] { ("Gộp", t) }); }
                    else ExcelOps.Write(path, items.Select(x => (x.Name.Replace(" › ", "-"), x.Tbl)));
                    Ui.OpenExternal(path);
                    return $"✔ Đã gộp {items.Count} bảng → {Path.GetFileName(path)}." + (notes.Count > 0 ? " Lưu ý: " + string.Join("; ", notes) : "");
                }), true));
                break;
            }
            case "split":
            {
                s.Children.Add(H("Tách bảng"));
                var (pickUi, getTbl, colBox) = OneTable(); s.Children.Add(pickUi);
                var how = Combo("Theo giá trị của một cột", "Mỗi N dòng một phần"); var n = new TextBox { Text = "1000", Width = 80 };
                var outp = Combo("1 file, mỗi phần 1 sheet", "Mỗi phần 1 file riêng (chọn thư mục)");
                s.Children.Add(Row(Lb("Tách"), how, Lb("Cột"), colBox, Lb("N ="), n)); s.Children.Add(Row(Lb("Kết quả"), outp));
                s.Children.Add(Btn("Tách & lưu", () => Do("Đang tách…", async () =>
                {
                    var (t, name) = getTbl() ?? throw new InvalidOperationException("Chưa chọn file / sheet.");
                    var parts = how.SelectedIndex == 0 ? CcBang.SplitByColumn(t, Math.Max(0, colBox.SelectedIndex)).Select(x => (x.Name, x.Tbl)).ToList() : CcBang.SplitByRows(t, int.TryParse(n.Text, out var nn) ? nn : 1000);
                    if (outp.SelectedIndex == 0)
                    {
                        var path = await Ui.SaveFile(this, "Lưu file tách", $"{Base(name)}-tach-{Stamp()}.xlsx", "xlsx", "Excel"); if (path is null) return "Đã hủy.";
                        await Task.Run(() => ExcelOps.Write(path, parts)); Ui.OpenExternal(path); return $"✔ Đã tách thành {parts.Count} sheet.";
                    }
                    var dir = await Ui.OpenFolder(this, "Chọn thư mục lưu các file"); if (dir is null) return "Đã hủy.";
                    await Task.Run(() => { foreach (var (pn, pt) in parts) ExcelOps.Write(Path.Combine(dir, $"{Base(name)}_{CcBang.SafeName(pn)}.xlsx"), new[] { (pn, pt) }); });
                    Ui.OpenExternal(dir); return $"✔ Đã tách thành {parts.Count} file trong {dir}.";
                }), true));
                break;
            }
            case "clean":
            {
                s.Children.Add(H("Làm sạch dữ liệu"));
                var (pickUi, getTbl, colBox) = OneTable(); s.Children.Add(pickUi);
                var trim = new CheckBox { Content = "Cắt khoảng trắng thừa (đầu / cuối / giữa, ký tự trắng lạ)", IsChecked = true };
                var blank = new CheckBox { Content = "Bỏ dòng trống", IsChecked = true };
                var dedupe = new CheckBox { Content = "Bỏ dòng trùng (so các cột được đánh dấu \"so trùng\" bên dưới; không đánh dấu cột nào = so mọi cột)", IsChecked = false };
                var num = new CheckBox { Content = "Đổi số dạng chữ thành số ở MỌI cột (\"1,234\", \"12,5\", \"(100)\")", IsChecked = true };
                var date = new CheckBox { Content = "Đưa ngày dạng chữ về dd/mm/yyyy ở mọi cột (khi không đổi số mọi cột)", IsChecked = false };
                foreach (var c in new[] { trim, blank, dedupe, num, date }) s.Children.Add(c);
                // tùy chọn riêng từng cột
                var colOpts = new List<(ComboBox Kind, CheckBox Dup)>(); var colGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), Margin = new Thickness(0, 2, 0, 0) };
                var colWrap = new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), IsVisible = false, Child = new ScrollViewer { MaxHeight = 260, Content = colGrid } };
                string[] kinds = { "Giữ nguyên (theo tùy chọn chung)", "Đổi thành số", "Đổi thành ngày dd/mm/yyyy", "CHỮ HOA", "chữ thường", "Viết Hoa Đầu Từ" };
                void BuildCols()
                {
                    colGrid.Children.Clear(); colGrid.RowDefinitions.Clear(); colOpts.Clear();
                    var heads = (colBox.ItemsSource as IEnumerable<string>)?.ToList() ?? new();
                    colWrap.IsVisible = heads.Count > 0;
                    for (var i = 0; i < heads.Count; i++)
                    {
                        colGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                        var lb = Lb(heads[i]); lb.Margin = new Thickness(0, 2, 12, 2); lb.MaxWidth = 300; lb.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetRow(lb, i); colGrid.Children.Add(lb);
                        var k = new ComboBox { ItemsSource = kinds, SelectedIndex = 0, MinWidth = 230, FontSize = 12.5, Margin = new Thickness(0, 2) }; Grid.SetRow(k, i); Grid.SetColumn(k, 1); colGrid.Children.Add(k);
                        var d = new CheckBox { Content = "so trùng", FontSize = 12.5, Margin = new Thickness(12, 0, 0, 0) }; Grid.SetRow(d, i); Grid.SetColumn(d, 2); colGrid.Children.Add(d);
                        colOpts.Add((k, d));
                    }
                }
                colBox.SelectionChanged += (_, _) => { if (colBox.SelectedIndex == 0 || colBox.ItemsSource is null) BuildCols(); };
                s.Children.Add(H("Tùy chọn theo từng cột")); s.Children.Add(Muted("Chọn cách đổi riêng cho cột nào cần (ưu tiên hơn tùy chọn chung). Đánh dấu \"so trùng\" để chỉ so các cột đó khi bỏ dòng trùng."));
                s.Children.Add(colWrap);
                s.Children.Add(Btn("Làm sạch & lưu", () => Do("Đang làm sạch…", async () =>
                {
                    var (t, name) = getTbl() ?? throw new InvalidOperationException("Chưa chọn file / sheet.");
                    // tùy chọn chung → đưa về từng cột "Giữ nguyên" để lựa chọn riêng của cột luôn được ưu tiên
                    bool numAll = num.IsChecked == true, dateAll = date.IsChecked == true && !numAll;
                    var cols = new Dictionary<int, CcBang.ColOpt>();
                    for (var i = 0; i < Math.Max(colOpts.Count, t.Head.Count); i++)
                    {
                        var k = i < colOpts.Count ? colOpts[i].Kind.SelectedIndex : 0;
                        cols[i] = k <= 0 ? new CcBang.ColOpt { Num = numAll, Date = dateAll } : new CcBang.ColOpt { Num = k == 1, Date = k == 2, Upper = k == 3, Lower = k == 4, Title = k == 5 };
                    }
                    var dupCols = colOpts.Select((o, i) => (o, i)).Where(x => x.o.Dup.IsChecked == true).Select(x => x.i).ToList();
                    var r = CcBang.CleanTable(t, new CcBang.CleanOpts { Trim = trim.IsChecked == true, BlankRows = blank.IsChecked == true, Dedupe = dedupe.IsChecked == true ? dupCols : null, NumAll = false, DateAll = false, Cols = cols });
                    var path = await Ui.SaveFile(this, "Lưu file đã làm sạch", $"{Base(name)}-sach-{Stamp()}.xlsx", "xlsx", "Excel"); if (path is null) return "Đã hủy.";
                    var log = new CcTable(new[] { "Dòng (trong bảng)", "Cột", "Trước", "Sau", "Thay đổi" }, r.Log.Select(l => new object?[] { (double)(l.R + 1), r.Table.Head.ElementAtOrDefault(l.C) ?? CcBang.ColName(l.C), l.From, l.To, l.Why }));
                    var iss = new CcTable(new[] { "Dòng (trong bảng)", "Cột", "Giá trị", "Vấn đề" }, r.Issues.Select(l => new object?[] { (double)(l.R + 1), r.Table.Head.ElementAtOrDefault(l.C) ?? CcBang.ColName(l.C), l.V, l.Why }));
                    await Task.Run(() => ExcelOps.Write(path, new[] { ("Dữ liệu sạch", r.Table), ("Đã sửa", log), ("Ô cần xem lại", iss) }));
                    Ui.OpenExternal(path);
                    return $"✔ {r.Table.Rows.Count} dòng · sửa {r.Log.Count} ô · bỏ {r.BlankRows} dòng trống · {r.Dups} dòng trùng · {r.Issues.Count} ô cần xem lại.";
                }), true));
                break;
            }
        }
        return s;
    }

    /// <summary>Chọn file Excel + sheet → bảng; kèm ô chọn cột (dùng cho tách theo cột).</summary>
    private (Control ui, Func<(CcTable t, string name)?> get, ComboBox cols) OneTable()
    {
        List<ExcelOps.Sheet>? sheets = null; string? file = null;
        var info = Muted("Chưa chọn file."); var sheet = new ComboBox { MinWidth = 180 }; var cols = new ComboBox { MinWidth = 180 };
        CcTable? Cur() => sheets is null || sheet.SelectedIndex < 0 ? null : ExcelOps.ToTable(sheets[sheet.SelectedIndex]);
        sheet.SelectionChanged += (_, _) => { var t = Cur(); cols.ItemsSource = t?.Head.Select((h, i) => $"{CcBang.ColName(i)}: {h}").ToList() ?? new List<string>(); cols.SelectedIndex = t is { Head.Count: > 0 } ? 0 : -1; if (t is not null) info.Text = $"{Path.GetFileName(file)} · {t.Rows.Count} dòng · {t.Head.Count} cột"; };
        var pick = new Button { Content = "Chọn file Excel…" };
        pick.Click += async (_, _) =>
        {
            file = (await PickFiles("Chọn file Excel", new[] { "xlsx", "xlsm", "xls", "csv" }, false)).FirstOrDefault(); if (file is null) return;
            try { sheets = await Task.Run(() => ExcelOps.Read(file)); sheet.ItemsSource = sheets.Select(x => x.Name).ToList(); sheet.SelectedIndex = 0; }
            catch (Exception e) { info.Text = e.Message; sheets = null; }
        };
        return (Row(pick, Lb("Sheet"), sheet, info), () => Cur() is { } t ? (t, file!) : null, cols);
    }
}

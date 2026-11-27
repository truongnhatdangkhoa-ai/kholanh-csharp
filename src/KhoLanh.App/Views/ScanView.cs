using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.Scan;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Scan tài liệu (M15 của app cũ): chọn / kéo thả ảnh chụp hoặc PDF → tự tìm và cắt trang giấy, nắn thẳng, khử bóng,
/// làm trắng nền, chữ đen rõ (5 chế độ) → xuất PDF (A4 hoặc vừa ảnh) / ảnh JPG. Mọi xử lý chạy trên máy, không gửi ảnh đi đâu.
/// Tài liệu lưu RIÊNG từng tài khoản trên máy; trang đang làm tự lưu nháp.
/// </summary>
public sealed class ScanView : UserControl, IPage
{
    public string Title => "Scan tài liệu";
    public string Subtitle => "Ảnh chụp / PDF → tự cắt trang, khử bóng, nền trắng chữ đen → xuất PDF để in / gửi. Xử lý hoàn toàn trên máy.";

    private static ScanStore Store => App.Services.Scan;
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45");
    private const int PreviewSide = 1700, ThumbSide = 240;

    private ScanDoc _doc = new() { Id = ScanStore.NewId(), Name = DefaultName() };
    private int _cur = -1;
    private bool _dirty, _cropMode, _painting;
    private readonly Dictionary<ScanPage, RgbaImage> _src = new();          // ảnh gốc đã giải mã (giữ vài trang gần nhất)
    private readonly List<ScanPage> _srcOrder = new();
    private readonly Dictionary<ScanPage, Bitmap> _thumbs = new();
    private int _ver;

    private readonly StackPanel _strip = new() { Spacing = 6 };
    private readonly Image _img = new() { Stretch = Stretch.Uniform };
    private readonly ScrollViewer _imgScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ScanCropCanvas _crop = new() { IsVisible = false };
    private readonly Border _empty = new();
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _name = new() { Width = 240 };
    private readonly ComboBox _fmt = new() { ItemsSource = new[] { "PDF khổ A4", "PDF vừa khít ảnh" }, SelectedIndex = 0, Width = 150 };
    private readonly StackPanel _panel = new() { Spacing = 8 };
    private readonly Slider _zoom = new() { Minimum = 0.25, Maximum = 3, Value = 1, Width = 140 };
    private readonly TextBlock _busy = new() { Foreground = AppTheme.Brush("#1D4ED8"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _draftT = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _prevT = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private Bitmap? _curBmp;

    private static string DefaultName() => "Scan_" + DateTime.Now.ToString("dd-MM-yyyy_HHmm", CultureInfo.InvariantCulture);

    public ScanView()
    {
        _draftT.Tick += (_, _) => { _draftT.Stop(); SaveDraft(); };
        _prevT.Tick += (_, _) => { _prevT.Stop(); _ = Preview(); };
        _crop.QuadChanged += () => { if (Cur is { } p) { p.Quad = _crop.Quad.ToList(); p.CropOn = true; Dirty(); RefreshThumbSoon(p); } };
        _name.Text = _doc.Name; _name.TextChanged += (_, _) => { _doc.Name = _name.Text ?? ""; Dirty(false); };
        _fmt.SelectedIndex = Store.GetSetting("format") == "fit" ? 1 : 0;
        _fmt.SelectionChanged += (_, _) => Store.SetSetting("format", _fmt.SelectedIndex == 1 ? "fit" : "a4");
        _zoom.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) ApplyZoom(); };

        var bar = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        bar.Children.Add(Btn("＋ Thêm ảnh / PDF", AddFilesDialog, "Chọn ảnh chụp (JPG, PNG…) hoặc file PDF — có thể chọn nhiều file, hoặc kéo thả vào trang", true));
        bar.Children.Add(Btn("Mới", NewDoc));
        bar.Children.Add(Btn("Tài liệu đã lưu", OpenSaved));
        bar.Children.Add(new TextBlock { Text = "Tên:", VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
        bar.Children.Add(_name);
        bar.Children.Add(Btn("💾 Lưu", SaveDoc, "Cất tài liệu vào bộ nhớ của tài khoản trên máy này để mở / chỉnh lại sau"));
        bar.Children.Add(_fmt);
        bar.Children.Add(Btn("⬇ Xuất PDF", ExportPdf, null, true));
        bar.Children.Add(Btn("⬇ Xuất ảnh JPG", ExportJpg, "Trang đang xem → 1 file JPG; chọn “tất cả” để xuất mọi trang vào 1 thư mục"));
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(bar);
        var st = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; st.Children.Add(_busy); st.Children.Add(_status); top.Children.Add(st);

        _imgScroll.Content = _img;
        var es = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 8, MaxWidth = 520 };
        es.Children.Add(new TextBlock { Text = "Kéo thả ảnh / PDF vào đây", FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Dark, HorizontalAlignment = HorizontalAlignment.Center });
        es.Children.Add(new TextBlock { Text = "hoặc bấm “＋ Thêm ảnh / PDF”. Phần mềm tự tìm tờ giấy trong ảnh, nắn thẳng, khử bóng, làm trắng nền để in / photo rõ như máy scan. Ảnh không gửi đi đâu.", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Classes = { "muted" } });
        var addBig = Btn("＋ Thêm ảnh / PDF", AddFilesDialog, null, true); addBig.HorizontalAlignment = HorizontalAlignment.Center; es.Children.Add(addBig);
        _empty.Child = es;
        var stage = new Grid { Background = AppTheme.Brush("#E5E9F0") };
        stage.Children.Add(_imgScroll); stage.Children.Add(_crop); stage.Children.Add(_empty);
        var zoomBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        zoomBar.Children.Add(new TextBlock { Text = "Thu phóng", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        zoomBar.Children.Add(_zoom);
        zoomBar.Children.Add(Btn("Vừa khung", () => _zoom.Value = 1));
        var center = new DockPanel(); DockPanel.SetDock(zoomBar, Dock.Bottom); center.Children.Add(zoomBar);
        center.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = stage });

        var left = new ScrollViewer { Content = _strip, Width = 150, Padding = new Thickness(0, 0, 8, 0) };
        var right = new ScrollViewer { Content = _panel, Width = 300, Padding = new Thickness(10, 0, 6, 0) };
        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        main.Children.Add(left); Grid.SetColumn(center, 1); main.Children.Add(center); Grid.SetColumn(right, 2); main.Children.Add(right);
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(main);
        Content = Ui.SubTabs(("Làm việc", root), ("Đã lưu", CongCuFiles.Panel("Scan tài liệu")));

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; });
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
            if (files is { Count: > 0 }) await AddFiles(files);
        });
        AttachedToVisualTree += (_, _) => Boot();
        DetachedFromVisualTree += (_, _) => { if (_draftT.IsEnabled) { _draftT.Stop(); SaveDraft(); } };
    }

    private ScanPage? Cur => _cur >= 0 && _cur < _doc.Pages.Count ? _doc.Pages[_cur] : null;

    private static Button Btn(string text, Action click, string? tip = null, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
    private static Button Btn(string text, Func<Task> click, string? tip = null, bool primary = false) => Btn(text, () => { _ = click(); }, tip, primary);
    private static TextBlock Muted(string t) => new() { Text = t, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private static TextBlock H(string t) => new() { Text = t, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Dark, Margin = new Thickness(0, 6, 0, 0) };

    private void Boot()
    {
        if (!Store.CanUse) { Content = Muted("Tài khoản chưa được cấp quyền dùng Scan tài liệu."); return; }
        try
        {
            if (_doc.Pages.Count == 0 && Store.Load(ScanStore.DraftId) is { Pages.Count: > 0 } d)
            {
                _doc = d; _doc.Id = d.Name.Length > 0 && Store.GetSetting("draftOf") is { Length: > 0 } of ? of : ScanStore.NewId();
                _name.Text = _doc.Name; _cur = 0; _dirty = true;
                _status.Text = $"Đã mở lại {_doc.Pages.Count} trang đang làm dở (nháp tự lưu).";
            }
        }
        catch { /* nháp hỏng: bỏ qua */ }
        RenderAll();
    }

    private void Dirty(bool repaint = true)
    {
        _dirty = true; _draftT.Stop(); _draftT.Start();
        if (repaint) { _prevT.Stop(); _prevT.Start(); }
    }

    private void SaveDraft()
    {
        try
        {
            if (_doc.Pages.Count == 0) { Store.Delete(ScanStore.DraftId); return; }
            var d = new ScanDoc { Id = ScanStore.DraftId, Name = _doc.Name, Created = _doc.Created, Pages = _doc.Pages };
            Store.Save(d); Store.SetSetting("draftOf", _doc.Id);
        }
        catch { /* đĩa đầy / bị chặn: bỏ qua */ }
    }

    // ------------------------------------------------------------ nạp file
    private async Task AddFilesDialog()
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Chọn ảnh chụp tài liệu hoặc file PDF", AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Ảnh / PDF") { Patterns = ScanImaging.ImageExt.Select(e => "*." + e).Append("*.pdf").ToArray() } },
        });
        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
        if (paths.Count > 0) await AddFiles(paths);
    }

    public async Task AddFiles(List<string> paths)
    {
        var skipped = new List<string>(); var added = 0; var first = _doc.Pages.Count;
        var mode = Store.GetSetting("mode") ?? ScanCore.DefaultMode;
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var n = await Task.Run(() => PdfRender.PageCount(bytes));
                    var take = Math.Min(n, ScanCore.PdfMaxPages);
                    for (var i = 0; i < take; i++)
                    {
                        _busy.Text = $"Đang đọc PDF {name} trang {i + 1}/{take}…";
                        var ii = i;
                        var pg = await Task.Run(() =>
                        {
                            var (w, h, rgba) = PdfRender.RenderRgba(bytes, ii, ScanCore.MaxSide);
                            var img = new RgbaImage(w, h, rgba);
                            return MakePage(img, false, mode);   // trang PDF đã thẳng: không tự cắt
                        });
                        _doc.Pages.Add(pg.page); Remember(pg.page, pg.img); added++;
                    }
                    if (n > take) skipped.Add($"{name}: chỉ lấy {take} trang đầu / {n} trang");
                }
                else
                {
                    _busy.Text = $"Đang xử lý {name}…";
                    var pg = await Task.Run(() => MakePage(ScanImaging.Decode(bytes), true, mode));
                    _doc.Pages.Add(pg.page); Remember(pg.page, pg.img); added++;
                }
            }
            catch (Exception e) { skipped.Add(name + ": " + e.Message); }
        }
        _busy.Text = "";
        if (added > 0) { _cur = first; Dirty(false); RenderAll(); }
        var msg = added > 0 ? $"Đã thêm {added} trang." + (_doc.Pages.Skip(first).Count(p => p.CropOn) is var c && c > 0 ? $" Tự tìm thấy tờ giấy ở {c} ảnh (bấm “Chỉnh khung cắt” nếu chưa đúng)." : "") : "Không thêm được trang nào.";
        _status.Text = msg;
        if (skipped.Count > 0) await Ui.Message(this, msg + "\n\nBỏ qua:\n" + string.Join("\n", skipped));
    }

    private static (ScanPage page, RgbaImage img) MakePage(RgbaImage img, bool detect, string mode)
    {
        var pg = new ScanPage { Source = ScanImaging.EncodeJpeg(img, 92), Mode = mode };
        if (detect) { var q = ScanCore.DetectQuad(img); if (q is not null) { pg.Quad = q; pg.CropOn = true; } }
        return (pg, img);
    }

    private void Remember(ScanPage p, RgbaImage img)
    {
        _src[p] = img; _srcOrder.Remove(p); _srcOrder.Add(p);
        while (_srcOrder.Count > 4) { _src.Remove(_srcOrder[0]); _srcOrder.RemoveAt(0); }
    }

    private RgbaImage SourceOf(ScanPage p)
    {
        if (_src.TryGetValue(p, out var img)) return img;
        img = ScanImaging.Decode(p.Source); Remember(p, img); return img;
    }

    // ------------------------------------------------------------ vẽ
    private void RenderAll()
    {
        _empty.IsVisible = _doc.Pages.Count == 0;
        if (_cur >= _doc.Pages.Count) _cur = _doc.Pages.Count - 1;
        if (_cur < 0 && _doc.Pages.Count > 0) _cur = 0;
        RenderStrip(); RenderPanel();
        if (_doc.Pages.Count == 0) { _img.Source = null; _crop.IsVisible = false; _imgScroll.IsVisible = false; return; }
        _imgScroll.IsVisible = !_cropMode; _crop.IsVisible = _cropMode;
        if (_cropMode) ShowCrop(); else _ = Preview();
        foreach (var p in _doc.Pages.Where(p => !_thumbs.ContainsKey(p)).ToList()) _ = MakeThumb(p);
    }

    private void RenderStrip()
    {
        _strip.Children.Clear();
        for (var i = 0; i < _doc.Pages.Count; i++)
        {
            var p = _doc.Pages[i]; var idx = i;
            var sp = new StackPanel { Spacing = 2 };
            sp.Children.Add(new Image { Source = _thumbs.TryGetValue(p, out var t) ? t : null, Height = 150, Stretch = Stretch.Uniform });
            sp.Children.Add(new TextBlock { Text = $"Trang {i + 1}" + (p.CropOn ? " · đã cắt" : ""), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center });
            var b = new Button { Content = sp, Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Stretch, Background = i == _cur ? AppTheme.Brush("#DBEAFE") : Brushes.White, BorderBrush = i == _cur ? AppTheme.Brush("#2563EB") : Line_, BorderThickness = new Thickness(i == _cur ? 2 : 1) };
            b.Click += (_, _) => { _cur = idx; _cropMode = false; RenderAll(); };
            _strip.Children.Add(b);
        }
    }

    private async Task MakeThumb(ScanPage p)
    {
        try
        {
            var bmp = await Task.Run(() =>
            {
                var src = SourceOf(p);
                return ScanImaging.ToBitmap(ScanCore.Process(src, p.CropOn ? p.Quad : null, p.Rot, p.Mode, p.P, ThumbSide));
            });
            _thumbs[p] = bmp; RenderStrip();
        }
        catch { /* bỏ qua */ }
    }

    private void RefreshThumbSoon(ScanPage p) { _thumbs.Remove(p); _ = MakeThumb(p); }

    private async Task Preview()
    {
        var p = Cur; if (p is null || _cropMode) return;
        var ver = ++_ver; _busy.Text = "Đang xử lý…";
        try
        {
            var bmp = await Task.Run(() => ScanImaging.ToBitmap(ScanCore.Process(SourceOf(p), p.CropOn ? p.Quad : null, p.Rot, p.Mode, p.P, PreviewSide)));
            if (ver != _ver) return;
            _curBmp = bmp; _img.Source = bmp; ApplyZoom();
        }
        catch (Exception e) { _status.Text = "Không vẽ được trang: " + e.Message; }
        finally { if (ver == _ver) _busy.Text = ""; }
    }

    private void ApplyZoom()
    {
        if (_curBmp is null) return;
        var vw = Math.Max(100, _imgScroll.Bounds.Width - 20); var vh = Math.Max(100, _imgScroll.Bounds.Height - 20);
        var s = _curBmp.Size; var fit = Math.Min(vw / s.Width, vh / s.Height);
        _img.Width = s.Width * fit * _zoom.Value; _img.Height = s.Height * fit * _zoom.Value;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e) { base.OnSizeChanged(e); ApplyZoom(); }

    private void ShowCrop()
    {
        var p = Cur; if (p is null) return;
        try { _crop.Source = ScanImaging.ToBitmap(ScanCore.Rotate(ScanCore.Scale(SourceOf(p), 1600), 0)); }
        catch (Exception e) { _status.Text = e.Message; return; }
        _crop.Quad = p.Quad.ToList(); _crop.InvalidateVisual();
    }

    private Slider SliderRow(string label, double value, double min, double max, Action<double> set, string? tip = null)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = (max - min) / 20, Margin = new Thickness(0, -10, 0, -6) };
        var lab = new TextBlock { Text = $"{label}: {Math.Round(value, 2)}", FontSize = 12 };
        if (tip is not null) ToolTip.SetTip(s, tip);
        s.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty && !_painting) { lab.Text = $"{label}: {Math.Round(s.Value, 2)}"; set(s.Value); } };
        _panel.Children.Add(lab); _panel.Children.Add(s);
        return s;
    }

    private void RenderPanel()
    {
        _painting = true;
        _panel.Children.Clear();
        var p = Cur;
        if (p is null) { _panel.Children.Add(Muted("Chưa có trang nào.")); _painting = false; return; }
        _panel.Children.Add(new TextBlock { Text = $"Trang {_cur + 1} / {_doc.Pages.Count}", FontWeight = FontWeight.Bold, Foreground = Dark, FontSize = 14 });
        _panel.Children.Add(H("Chế độ"));
        var modes = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        foreach (var m in ScanCore.Modes)
        {
            var b = new ToggleButton { Content = m.Ten, IsChecked = p.Mode == m.Key, FontSize = 12.5, Padding = new Thickness(8, 3) };
            ToolTip.SetTip(b, m.Full + " — " + m.Tip);
            b.Click += (_, _) => { p.Mode = m.Key; p.P = m.Key == "tuy" ? (Store.GetSetting("pTuy") is { } js ? ParseP(js) : null) : null; Dirty(); RefreshThumbSoon(p); RenderPanel(); };
            modes.Children.Add(b);
        }
        _panel.Children.Add(modes);
        _panel.Children.Add(Muted(ScanCore.ModeOf(p.Mode).Tip));
        var pr = ScanCore.Resolve(p.Mode, p.P);
        if (p.Mode == "tuy")
        {
            var kieu = new ComboBox { ItemsSource = ScanCore.Kieu.Values.ToList(), SelectedIndex = Math.Max(0, ScanCore.Kieu.Keys.ToList().IndexOf(pr.Kieu)), Width = 180 };
            kieu.SelectionChanged += (_, _) => { if (_painting) return; p.P = ScanCore.Resolve(p.Mode, p.P) with { Kieu = ScanCore.Kieu.Keys.ElementAt(Math.Max(0, kieu.SelectedIndex)) }; Dirty(); RefreshThumbSoon(p); };
            var ks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; ks.Children.Add(new TextBlock { Text = "Kiểu:", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }); ks.Children.Add(kieu);
            _panel.Children.Add(ks);
        }
        void Set(Func<ScanParams, ScanParams> f) { p.P = f(ScanCore.Resolve(p.Mode, p.P)); Dirty(); RefreshThumbSoon(p); }
        SliderRow("Khử bóng", pr.Bong, 0, 100, v => Set(q => q with { Bong = v }), "Làm đều độ sáng nền giấy (bóng tay, bóng đèn, giấy ngả vàng)");
        SliderRow("Độ trắng nền", pr.Trang, 0, 100, v => Set(q => q with { Trang = v }));
        SliderRow("Độ đậm chữ", pr.Dam, 0, 100, v => Set(q => q with { Dam = v }));
        SliderRow("Làm nhạt dòng kẻ", pr.Nhat, 0, 100, v => Set(q => q with { Nhat = v }), "Dòng kẻ / ô li thành xám nhạt, nét chữ đè lên dòng vẫn đen đậm");
        SliderRow("Độ đậm vùng chữ mờ", pr.Gam, 0.4, 1.6, v => Set(q => q with { Gam = v }), "< 1 = nét mờ đậm hơn");
        var li = new CheckBox { Content = "Xóa hẳn ô li / dòng kẻ (giữ nét chữ cắt ngang)", IsChecked = pr.Li, FontSize = 12 };
        li.IsCheckedChanged += (_, _) => { if (!_painting) Set(q => q with { Li = li.IsChecked == true }); };
        _panel.Children.Add(li);
        var defs = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        defs.Children.Add(Btn("Áp dụng cho mọi trang", () => { foreach (var x in _doc.Pages) { x.Mode = p.Mode; x.P = p.P; } _thumbs.Clear(); Dirty(); RenderAll(); _status.Text = "Đã áp dụng chế độ cho mọi trang."; }));
        defs.Children.Add(Btn("Đặt làm mặc định", () => { Store.SetSetting("mode", p.Mode); if (p.Mode == "tuy" && p.P is { } q) Store.SetSetting("pTuy", FormatP(q)); _status.Text = "Trang mới sẽ dùng chế độ " + ScanCore.ModeOf(p.Mode).Ten + "."; }));
        defs.Children.Add(Btn("Về mặc định của chế độ", () => { p.P = null; Dirty(); RefreshThumbSoon(p); RenderPanel(); }));
        _panel.Children.Add(defs);

        _panel.Children.Add(H("Cắt trang"));
        var cropOn = new CheckBox { Content = "Cắt theo khung (nắn thẳng tờ giấy)", IsChecked = p.CropOn, FontSize = 12 };
        cropOn.IsCheckedChanged += (_, _) => { if (_painting) return; p.CropOn = cropOn.IsChecked == true; Dirty(); RefreshThumbSoon(p); };
        _panel.Children.Add(cropOn);
        var cw = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        cw.Children.Add(Btn(_cropMode ? "✔ Xong chỉnh khung" : "Chỉnh khung cắt", () => { _cropMode = !_cropMode; if (!_cropMode) RefreshThumbSoon(p); RenderAll(); }, null, _cropMode));
        cw.Children.Add(Btn("Tự tìm lại", async () =>
        {
            var q = await Task.Run(() => ScanCore.DetectQuad(SourceOf(p)));
            if (q is null) { await Ui.Message(this, "Không thấy rìa tờ giấy (ảnh đã là trang giấy, hoặc bàn cùng màu giấy). Bấm “Chỉnh khung cắt” để kéo 4 góc bằng tay."); return; }
            p.Quad = q; p.CropOn = true; Dirty(); RefreshThumbSoon(p); RenderAll();
        }));
        cw.Children.Add(Btn("Lấy cả ảnh", () => { p.CropOn = false; p.Quad = ScanCore.DefaultQuad(); Dirty(); RefreshThumbSoon(p); RenderAll(); }));
        _panel.Children.Add(cw);
        if (_cropMode) _panel.Children.Add(Muted("Kéo 4 chấm tròn tới 4 góc tờ giấy (hoặc kéo cả khung). Bấm “Xong chỉnh khung” để xem kết quả."));

        _panel.Children.Add(H("Trang"));
        var pw = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        pw.Children.Add(Btn("⟲ Xoay trái", () => { p.Rot = (p.Rot + 270) % 360; Dirty(); RefreshThumbSoon(p); }));
        pw.Children.Add(Btn("⟳ Xoay phải", () => { p.Rot = (p.Rot + 90) % 360; Dirty(); RefreshThumbSoon(p); }));
        pw.Children.Add(Btn("◀ Dời trước", () => Move(-1)));
        pw.Children.Add(Btn("Dời sau ▶", () => Move(1)));
        pw.Children.Add(Btn("🗑 Xóa trang", async () => { if (!await Ui.Confirm(this, $"Xóa trang {_cur + 1}?", yes: "Xóa")) return; _doc.Pages.RemoveAt(_cur); _thumbs.Remove(p); _src.Remove(p); _cropMode = false; Dirty(false); RenderAll(); }));
        _panel.Children.Add(pw);
        _painting = false;
    }

    private void Move(int d)
    {
        var j = _cur + d; if (_cur < 0 || j < 0 || j >= _doc.Pages.Count) return;
        (_doc.Pages[_cur], _doc.Pages[j]) = (_doc.Pages[j], _doc.Pages[_cur]); _cur = j; Dirty(false); RenderAll();
    }

    private static string FormatP(ScanParams q) => string.Join(";", q.Kieu, q.Bong.ToString(CultureInfo.InvariantCulture), q.Trang.ToString(CultureInfo.InvariantCulture), q.Dam.ToString(CultureInfo.InvariantCulture), q.Li ? "1" : "0", q.Gam.ToString(CultureInfo.InvariantCulture), q.Nhat.ToString(CultureInfo.InvariantCulture));
    private static ScanParams? ParseP(string s)
    {
        var a = s.Split(';'); if (a.Length < 7) return null;
        double D(string x) => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        return new ScanParams(a[0], D(a[1]), D(a[2]), D(a[3]), a[4] == "1", D(a[5]), D(a[6]));
    }

    // ------------------------------------------------------------ lưu / mở / xuất
    private async Task NewDoc()
    {
        if (_doc.Pages.Count > 0 && _dirty && !await Ui.Confirm(this, "Bỏ tài liệu đang làm (chưa lưu) để bắt đầu tài liệu mới?", yes: "Bắt đầu mới")) return;
        _doc = new ScanDoc { Id = ScanStore.NewId(), Name = DefaultName() }; _name.Text = _doc.Name; _cur = -1; _cropMode = false; _dirty = false;
        _thumbs.Clear(); _src.Clear(); _srcOrder.Clear(); _curBmp = null;
        try { Store.Delete(ScanStore.DraftId); } catch { }
        RenderAll(); _status.Text = "";
    }

    private async Task SaveDoc()
    {
        if (_doc.Pages.Count == 0) { await Ui.Message(this, "Chưa có trang nào để lưu."); return; }
        if ((_doc.Name ?? "").Trim().Length == 0) _doc.Name = DefaultName();
        _busy.Text = "Đang lưu…";
        var ok = await Ui.RunAsync(this, () => Task.Run(() => Store.Save(_doc)));
        _busy.Text = "";
        if (ok) { _dirty = false; try { Store.Delete(ScanStore.DraftId); } catch { } _status.Text = $"Đã lưu “{_doc.Name}” ({_doc.Pages.Count} trang) vào bộ nhớ của tài khoản trên máy này."; }
    }

    private async Task OpenSaved()
    {
        var list = new StackPanel { Spacing = 4 };
        var w = Ui.Dialog("Tài liệu đã lưu", new StackPanel(), 640);
        void Paint()
        {
            list.Children.Clear();
            var docs = Store.List();
            if (docs.Count == 0) { list.Children.Add(Muted("Chưa có tài liệu nào. Bấm “💾 Lưu” để cất tài liệu đang làm.")); return; }
            foreach (var d in docs)
            {
                var row = new DockPanel();
                var bs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                bs.Children.Add(Btn("Mở", async () =>
                {
                    if (_doc.Pages.Count > 0 && _dirty && !await Ui.Confirm(this, "Tài liệu đang làm chưa lưu — vẫn mở tài liệu khác?", yes: "Mở")) return;
                    var doc = Store.Load(d.Id); if (doc is null) { await Ui.Message(this, "Không mở được tài liệu này."); return; }
                    _doc = doc; _name.Text = doc.Name; _cur = 0; _cropMode = false; _dirty = false; _thumbs.Clear(); _src.Clear(); _srcOrder.Clear();
                    w.Close(); RenderAll(); _status.Text = $"Đã mở “{doc.Name}” ({doc.Pages.Count} trang).";
                }));
                bs.Children.Add(Btn("Xóa", async () => { if (!await Ui.Confirm(this, $"Xóa tài liệu “{d.Name}”?", yes: "Xóa")) return; if (await Ui.Run(this, () => Store.Delete(d.Id))) Paint(); }));
                DockPanel.SetDock(bs, Dock.Right); row.Children.Add(bs);
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = d.Name, FontWeight = FontWeight.SemiBold });
                t.Children.Add(Muted($"{d.Pages} trang · {(d.Bytes >= 1048576 ? (d.Bytes / 1048576.0).ToString("0.0", Ui.Vi) + " MB" : Math.Max(1, d.Bytes / 1024) + " KB")} · {d.Updated:HH:mm dd/MM/yyyy}"));
                row.Children.Add(t);
                list.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 4), Child = row });
            }
        }
        var close = new Button { Content = "Đóng", IsCancel = true }; close.Click += (_, _) => w.Close();
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Muted("Tài liệu lưu riêng cho tài khoản đang đăng nhập, trên máy này."));
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 460 }); body.Children.Add(Ui.Buttons(close));
        ((Border)w.Content!).Child = body;
        Paint();
        await Ui.Show(this, w);
    }

    private static string Clean(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\.(pdf|jpe?g)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[\\/:*?""<>|]+", "-").Trim();
        return s.Length > 0 ? s : DefaultName();
    }

    private async Task ExportPdf()
    {
        if (_doc.Pages.Count == 0) { await Ui.Message(this, "Chưa có trang nào."); return; }
        var path = await Ui.SaveFile(this, "Xuất PDF", Clean(_doc.Name) + ".pdf", "pdf", "PDF");
        if (path is null) return;
        var a4 = _fmt.SelectedIndex != 1; var pages = _doc.Pages.ToList(); var title = _doc.Name;
        var ok = await Ui.RunAsync(this, async () =>
        {
            var outp = new List<JpegPdf.Page>();
            for (var i = 0; i < pages.Count; i++)
            {
                _busy.Text = $"Đang tạo PDF trang {i + 1}/{pages.Count}…";
                var p = pages[i];
                outp.Add(await Task.Run(() =>
                {
                    var img = ScanCore.Process(ScanImaging.Decode(p.Source), p.CropOn ? p.Quad : null, p.Rot, p.Mode, p.P);
                    var kieu = ScanCore.Resolve(p.Mode, p.P).Kieu; var gray = kieu is "bw" or "xam";
                    return new JpegPdf.Page(ScanImaging.EncodeJpeg(img, kieu == "bw" ? 80 : 85, gray), img.W, img.H, gray);
                }));
            }
            var bytes = JpegPdf.Build(outp, a4, title);
            await File.WriteAllBytesAsync(path, bytes);
            _status.Text = $"Đã tạo PDF {pages.Count} trang ({(bytes.Length >= 1048576 ? (bytes.Length / 1048576.0).ToString("0.0", Ui.Vi) + " MB" : Math.Max(1, bytes.Length / 1024) + " KB")}): {path}";
        });
        _busy.Text = "";
        if (ok) CongCuFiles.Exported(this, "Scan tài liệu", path);
    }

    private async Task ExportJpg()
    {
        if (_doc.Pages.Count == 0) { await Ui.Message(this, "Chưa có trang nào."); return; }
        var choice = _doc.Pages.Count == 1 ? 0 : (await Ui.Choose(this, "Xuất ảnh JPG:", "Xuất ảnh", new[] { $"Trang đang xem (trang {_cur + 1})", $"Tất cả {_doc.Pages.Count} trang (vào 1 thư mục)" }))?.Index;
        if (choice is null) return;
        RgbaImage Render(ScanPage p) => ScanCore.Process(ScanImaging.Decode(p.Source), p.CropOn ? p.Quad : null, p.Rot, p.Mode, p.P);
        if (choice == 0)
        {
            var p = Cur!; var path = await Ui.SaveFile(this, "Lưu ảnh JPG", Clean(_doc.Name) + (_doc.Pages.Count > 1 ? $"_trang{_cur + 1}" : "") + ".jpg", "jpg", "Ảnh JPG");
            if (path is null) return;
            if (await Ui.RunAsync(this, async () => await File.WriteAllBytesAsync(path, await Task.Run(() => ScanImaging.EncodeJpeg(Render(p), 92))))) { _status.Text = "Đã lưu " + path; CongCuFiles.Exported(this, "Scan tài liệu", path); }
            return;
        }
        var dir = await Ui.OpenFolder(this, "Chọn thư mục lưu ảnh"); if (dir is null) return;
        var pages = _doc.Pages.ToList(); var baseName = Clean(_doc.Name);
        var ok = await Ui.RunAsync(this, async () =>
        {
            for (var i = 0; i < pages.Count; i++)
            {
                _busy.Text = $"Đang xuất ảnh {i + 1}/{pages.Count}…"; var p = pages[i];
                var bytes = await Task.Run(() => ScanImaging.EncodeJpeg(Render(p), 92));
                await File.WriteAllBytesAsync(Path.Combine(dir, $"{baseName}_trang{i + 1:00}.jpg"), bytes);
            }
        });
        _busy.Text = "";
        if (ok) { _status.Text = $"Đã xuất {pages.Count} ảnh vào {dir}"; CongCuFiles.Exported(this, "Scan tài liệu", dir); }
    }
}

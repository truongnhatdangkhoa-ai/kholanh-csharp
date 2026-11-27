using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.LuuTru;

namespace KhoLanh.App.Views;

/// <summary>
/// Lưu trữ cá nhân (M19 app cũ, làm lại): bảng các mục của riêng tài khoản (ghi chú / liên kết / ảnh / tệp) chia NGĂN bên trái,
/// tìm không dấu + lọc loại / thẻ, khung sửa bên phải; tab "Sao lưu &amp; Google": đăng nhập Google cá nhân, lịch tự sao lưu Drive,
/// khôi phục, .zip, nạp dữ liệu app cũ. Dữ liệu chỉ nằm trên máy + Drive của chính người dùng (không qua LAN).
/// </summary>
public sealed class LuuTruView : UserControl, IPage
{
    public string Title => "Lưu trữ cá nhân";
    public string Subtitle => "Ghi chú, liên kết, ảnh, tệp của riêng bạn — chia ngăn, gắn thẻ, tìm nhanh; sao lưu Google Drive cá nhân ngay trong trang.";

    private static LuuTruService Svc => App.Services.LuuTru;
    private const string AllNgan = "", TrashNgan = "#trash";
    private static readonly string[] KindKeys = { "all", "note", "link", "image", "file", "pin" };
    private static readonly string[] KindLabels = { "Mọi loại", "Ghi chú", "Liên kết", "Ảnh", "Tệp", "Đã ghim" };
    private static readonly Dictionary<string, string> KindName = new() { ["note"] = "Ghi chú", ["link"] = "Liên kết", ["image"] = "Ảnh", ["file"] = "Tệp" };

    private string _ngan = AllNgan;
    private List<LtRec> _rows = new();
    private LtRec? _cur;
    private bool _newMode, _loading;

    // ---- tab Kho
    private readonly WrapPanel _kpi = new() { ItemSpacing = 8, LineSpacing = 8 };
    private readonly TextBox _q = new() { Watermark = "Tìm tiêu đề, nội dung, tệp, thẻ…", Width = 280 };
    private readonly ComboBox _kind = new() { ItemsSource = KindLabels, SelectedIndex = 0, Width = 120 };
    private readonly ComboBox _tag = new() { Width = 150, PlaceholderText = "Mọi thẻ" };
    private readonly StackPanel _nganList = new() { Spacing = 2 };
    private readonly GridModel _gm = new() { ReadOnly = true };
    private readonly SheetGrid _g;
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
    private readonly Button _bMove, _bDel;

    // ---- khung chi tiết
    private readonly StackPanel _det = new() { Spacing = 8 };
    private readonly TextBox _dTitle = new() { Watermark = "Tiêu đề" };
    private readonly TextBox _dText = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, MinHeight = 150, MaxHeight = 320, Watermark = "Nội dung ghi chú, số liệu, liên kết…" };
    private readonly ComboBox _dNgan = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _dTags = new() { Watermark = "Thẻ, cách nhau dấu phẩy" };
    private readonly CheckBox _dPin = new() { Content = "Ghim lên đầu" };
    private List<LtNgan> _ngans = new();

    // ---- tab Sao lưu & Google
    private readonly StackPanel _gAcc = new() { Spacing = 8 };
    private readonly StackPanel _gBk = new() { Spacing = 8 };
    private readonly StackPanel _gHist = new() { Spacing = 4 };
    private readonly TextBox _cid = new() { Watermark = "….apps.googleusercontent.com" };
    private readonly TextBox _csec = new() { PasswordChar = '•', Watermark = "Client secret" };
    private readonly Expander _oauth = new() { Header = "Cài đặt kết nối Google (OAuth — làm 1 lần cho mỗi máy)", HorizontalAlignment = HorizontalAlignment.Stretch };
    private CancellationTokenSource? _signCts;
    private string _quota = "";

    public LuuTruView()
    {
        // ======== tab Kho
        _g = new SheetGrid(_gm) { SpareRows = 0, EmptyText = "Chưa có mục nào — bấm “Ghi chú mới” hoặc kéo thả tệp vào đây.", LargeTitle = "Lưu trữ cá nhân" };
        _g.SelectionChanged += () => { if (!_loading) ShowDetail(_g.Row >= 0 && _g.Row < _rows.Count ? _rows[_g.Row] : null); };
        _g.DoubleTapped += (_, _) => { if (_cur is { IsFile: true } r) Ui.OpenExternal(Svc.Store.AbsPath(r)); else if (_cur?.IsLink == true) OpenFirstLink(_cur); };
        _q.TextChanged += (_, _) => Reload();
        _kind.SelectionChanged += (_, _) => Reload();
        _tag.SelectionChanged += (_, _) => { if (!_loading) Reload(); };

        var add = Icon.Button("Ghi chú mới", "plus", "primary");
        add.Click += (_, _) => NewNote();
        var addF = Icon.Button("Thêm tệp / ảnh", "upload");
        ToolTip.SetTip(addF, "Chọn 1 hoặc nhiều tệp (PDF, Excel, ảnh…) — hoặc kéo thả tệp vào trang. Tệp được chép vào kho riêng của bạn.");
        addF.Click += async (_, _) => await PickFiles();
        _bMove = Icon.Button("Chuyển ngăn", "folder");
        _bMove.Click += async (_, _) => await MoveSelected();
        _bDel = Icon.Button("Xóa", "trash", "danger");
        _bDel.Click += async (_, _) => await DeleteSelected();
        var bar = new ToolRow { Margin = new Thickness(0, 8, 0, 8) };
        bar.Children.Add(_q); bar.Children.Add(_kind); bar.Children.Add(_tag);
        bar.Children.Add(new Border { Width = 10 });
        bar.Children.Add(add); bar.Children.Add(addF); bar.Children.Add(_bMove); bar.Children.Add(_bDel);

        var nganHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var addN = new Button { Content = Icon.Make("plus", 14), Classes = { "ghost" }, Padding = new Thickness(6, 2), MinHeight = 26 };
        ToolTip.SetTip(addN, "Thêm ngăn");
        addN.Click += async (_, _) => await AddNgan();
        DockPanel.SetDock(addN, Dock.Right); nganHead.Children.Add(addN);
        nganHead.Children.Add(new TextBlock { Text = "NGĂN", FontSize = 11.5, FontWeight = FontWeight.Bold, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        var left = new Border
        {
            Classes = { "card" }, Padding = new Thickness(8), Width = 190, Margin = new Thickness(0, 0, 8, 0),
            Child = new DockPanel { Children = { Docked(nganHead, Avalonia.Controls.Dock.Top), new ScrollViewer { Content = _nganList } } },
        };
        var right = new Border { Classes = { "card" }, Padding = new Thickness(12), Width = 330, Margin = new Thickness(8, 0, 0, 0), Child = new ScrollViewer { Content = _det } };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(left, 0); Grid.SetColumn(_g, 1); Grid.SetColumn(right, 2);
        body.Children.Add(left); body.Children.Add(_g); body.Children.Add(right);

        var top = new StackPanel();
        top.Children.Add(_kpi); top.Children.Add(bar);
        var kho = new DockPanel();
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Top); kho.Children.Add(top);
        DockPanel.SetDock(_status, Avalonia.Controls.Dock.Bottom); _status.Margin = new Thickness(0, 6, 0, 0); kho.Children.Add(_status);
        kho.Children.Add(body);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; });
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p is not null && File.Exists(p)).Cast<string>().ToList();
            if (files is { Count: > 0 }) await AddFiles(files);
        });
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { SaveDetail(); e.Handled = true; }
            else if (e.Key == Key.N && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { NewNote(); e.Handled = true; }
            else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _q.Focus(); e.Handled = true; }
        };

        // ======== tab Sao lưu & Google
        var sync = new StackPanel { Spacing = 12, MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 12) };
        sync.Children.Add(Card("Tài khoản Google cá nhân", _gAcc));
        sync.Children.Add(Card("Sao lưu Google Drive", _gBk));
        sync.Children.Add(Card("Trên máy này", BuildLocal()));
        sync.Children.Add(Card("Lịch sử sao lưu", _gHist));

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Kho của tôi", Content = new Border { Padding = new Thickness(0, 6, 0, 0), Child = kho } });
        tabs.Items.Add(new TabItem { Header = "Sao lưu & Google", Content = new ScrollViewer { Content = sync } });
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs && tabs.SelectedIndex == 1) RenderSync(); };
        Content = tabs;

        if (!Svc.CanUse) { Content = new TextBlock { Text = "Tài khoản chưa được cấp quyền Lưu trữ cá nhân.", Classes = { "muted" } }; return; }
        AttachedToVisualTree += (_, _) => { Svc.StatusChanged -= OnStatus; Svc.StatusChanged += OnStatus; };
        DetachedFromVisualTree += (_, _) => { Svc.StatusChanged -= OnStatus; _signCts?.Cancel(); };
        Reload();
        RenderSync();
    }

    private void OnStatus() => Dispatcher.UIThread.Post(() => { RenderKpi(); RenderSync(); });

    private static Control Docked(Control c, Avalonia.Controls.Dock d) { DockPanel.SetDock(c, d); return c; }

    private static Border Card(string title, Control body)
    {
        var s = new StackPanel { Spacing = 8 };
        s.Children.Add(new TextBlock { Text = title, Classes = { "section" }, Margin = new Thickness(0) });
        s.Children.Add(body);
        return new Border { Classes = { "card" }, Child = s };
    }

    private static Border Pill(string text, string kind) => new() { Classes = { "pill", kind }, HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = text } };

    private static Border Kpi(string label, string value, string? sub = null)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = value, FontSize = 17, FontWeight = FontWeight.Bold });
        s.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Classes = { "muted" } });
        if (sub is not null) s.Children.Add(new TextBlock { Text = sub, FontSize = 11, Classes = { "muted" } });
        return new Border { Classes = { "card" }, Padding = new Thickness(12, 6), MinWidth = 120, Child = s };
    }

    private static string N(int n) => n.ToString("#,0", Ui.Vi);
    private static string When(long ms) => ms <= 1 ? "" : DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "vừa xong" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} phút trước" : d.TotalDays < 1 ? $"{(int)d.TotalHours} giờ trước" : t.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    // ================================================================== Kho

    private void Reload(string? keepId = null)
    {
        LtStore st;
        try { st = Svc.Store; } catch (Exception e) { _status.Text = Ui.Describe(e); return; }
        _loading = true;
        try
        {
            keepId ??= _cur?.Id;
            var tags = st.Tags();
            var curTag = _tag.SelectedItem as string;
            _tag.ItemsSource = new[] { "Mọi thẻ" }.Concat(tags).ToList();
            _tag.SelectedIndex = curTag is not null && tags.Contains(curTag) ? tags.IndexOf(curTag) + 1 : 0;
            var tag = _tag.SelectedIndex > 0 ? _tag.SelectedItem as string ?? "" : "";
            var kind = _ngan == TrashNgan ? "trash" : KindKeys[Math.Max(0, _kind.SelectedIndex)];
            _rows = st.Query(_ngan == TrashNgan ? "" : _ngan, kind, _q.Text ?? "", tag);
            var cfg = Svc.Config;
            _gm.Load(new[]
            {
                new GridColumn("pn", "Ghim", 42), new GridColumn("loai", "Loại", 70), new GridColumn("tieude", "Tiêu đề / nội dung", 290),
                new GridColumn("ngan", "Ngăn", 120), new GridColumn("the", "Thẻ", 110), new GridColumn("mb", "MB", 64, "number"),
                new GridColumn("sua", "Sửa lúc", 112), new GridColumn("drive", "Drive", 54), new GridColumn("tao", "Ngày tạo", 112),
            }, _rows.Select(r => new[]
            {
                r.Pn == 1 ? "★" : "", KindName.GetValueOrDefault(r.Kind, ""), r.Title, st.NganName(r.Ng), string.Join(", ", r.Tg),
                r.Fs > 0 ? (r.Fs / 1048576.0).ToString("0.###", Ui.Vi) : "", When(r.Up),
                !cfg.Linked ? "" : r.Sq > cfg.LastSeq || (r.IsFile && r.Fid.Length == 0) ? "chờ" : "✓", When(r.Ts),
            }));
            _g.Refresh();
            RenderNgans(st);
            RenderKpi();
            var trash = _ngan == TrashNgan;
            _bMove.IsVisible = !trash;
            _bDel.Content = Icon.Label(trash ? "Xóa hẳn" : "Xóa", "trash");
            var idx = keepId is null ? -1 : _rows.FindIndex(r => r.Id == keepId);
            if (_newMode) { }
            else if (idx >= 0) { _g.Select(idx, 2); ShowDetail(_rows[idx]); }
            else if (_rows.Count > 0) { _g.Select(0, 2); ShowDetail(_rows[0]); }
            else ShowDetail(null);
            _status.Text = $"{N(_rows.Count)} mục" + ((_q.Text ?? "").Trim().Length > 0 ? " khớp tìm kiếm" : "") + " · thư mục dữ liệu: " + st.Dir;
        }
        finally { _loading = false; }
    }

    private void RenderKpi()
    {
        LtStore st; LtConfig cfg;
        try { st = Svc.Store; cfg = Svc.Config; } catch { return; }
        var s = st.Stats(cfg.LastSeq);
        _kpi.Children.Clear();
        _kpi.Children.Add(Kpi("Mục đang lưu", N(s.Items), s.Pinned > 0 ? $"{N(s.Pinned)} mục ghim" : null));
        _kpi.Children.Add(Kpi("Ghi chú · liên kết", $"{N(s.Notes)} · {N(s.Links)}"));
        _kpi.Children.Add(Kpi("Ảnh · tệp", $"{N(s.Images)} · {N(s.Files)}"));
        _kpi.Children.Add(Kpi("Dung lượng", LuuTruService.Size(s.Bytes)));
        var drive = new StackPanel { Spacing = 3 };
        drive.Children.Add(new TextBlock { Text = "Sao lưu Drive", FontSize = 11.5, Classes = { "muted" } });
        if (!cfg.Linked) drive.Children.Add(Pill("Chưa đăng nhập Google", "warn"));
        else if (cfg.NeedRelink) drive.Children.Add(Pill("Cần đăng nhập lại", "bad"));
        else if (cfg.LastErr.Length > 0) drive.Children.Add(Pill("Lỗi lần trước", "bad"));
        else if (Svc.Dirty(st, cfg)) drive.Children.Add(Pill("Có thay đổi chưa sao lưu", "info"));
        else drive.Children.Add(Pill(cfg.LastOk is { } t ? "Đã sao lưu " + Ago(t) : "Đã sao lưu", "ok"));
        var b = new Border { Classes = { "card" }, Padding = new Thickness(12, 6), MinWidth = 150, Child = drive, Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(b, "Bấm để mở tab Sao lưu & Google");
        b.PointerPressed += (_, _) => { if (Content is TabControl t) t.SelectedIndex = 1; };
        _kpi.Children.Add(b);
        if (s.Trash > 0) _kpi.Children.Add(Kpi("Thùng rác", N(s.Trash)));
    }

    private void RenderNgans(LtStore st)
    {
        _ngans = st.Ngans();
        _nganList.Children.Clear();
        Button Row(string id, string name, string count, string icon)
        {
            var g = new DockPanel();
            var c = new TextBlock { Text = count, FontSize = 12, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(c, Avalonia.Controls.Dock.Right); g.Children.Add(c);
            var ic = Icon.Make(icon, 14); ic.Margin = new Thickness(0, 0, 7, 0);
            DockPanel.SetDock(ic, Avalonia.Controls.Dock.Left); g.Children.Add(ic);
            g.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = g, Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 4), MinHeight = 30 };
            ToolTip.SetTip(b, name);
            if (id == _ngan) { b.Background = AppTheme.Brush("#E7F0FC"); b.FontWeight = FontWeight.Bold; }
            b.Click += (_, _) => { _ngan = id; _newMode = false; Reload(); };
            if (id != AllNgan && id != TrashNgan)
            {
                var ren = new MenuItem { Header = "Đổi tên ngăn…" }; ren.Click += async (_, _) => await RenameNgan(id);
                var del = new MenuItem { Header = "Xóa ngăn (mục chuyển về Chung)…", IsEnabled = id != LtStore.DefaultNgan }; del.Click += async (_, _) => await DeleteNgan(id);
                b.ContextMenu = new ContextMenu { Items = { ren, del } };
                ToolTip.SetTip(b, "Chuột phải để đổi tên / xóa ngăn");
            }
            return b;
        }
        _nganList.Children.Add(Row(AllNgan, "Tất cả", N(_ngans.Sum(n => n.Count)), "layers"));
        foreach (var n in _ngans) _nganList.Children.Add(Row(n.Id, n.Name, N(n.Count), "folder"));
        _nganList.Children.Add(new Border { Height = 1, Background = AppTheme.Brush("#DDE3EA"), Margin = new Thickness(0, 6) });
        _nganList.Children.Add(Row(TrashNgan, "Thùng rác", N(st.Stats().Trash), "trash"));
        if (_ngan != AllNgan && _ngan != TrashNgan && _ngans.All(n => n.Id != _ngan)) _ngan = AllNgan;
    }

    private string TargetNgan => _ngan is AllNgan or TrashNgan ? LtStore.DefaultNgan : _ngan;

    private void NewNote()
    {
        if (_ngan == TrashNgan) { _ngan = AllNgan; Reload(); }
        _newMode = true; _cur = null;
        _g.Select(-1, 0);
        BuildEditor(null);
        _dTitle.Focus();
    }

    private void ShowDetail(LtRec? r)
    {
        if (_newMode && r is null) return;
        _newMode = false;
        _cur = r;
        BuildEditor(r);
    }

    private void BuildEditor(LtRec? r)
    {
        _det.Children.Clear();
        var st = Svc.Store;
        if (r is null && !_newMode)
        {
            _det.Children.Add(new TextBlock { Text = _rows.Count == 0 && _ngan != TrashNgan ? "Kho còn trống" : "Chọn 1 mục để xem / sửa", FontSize = 15, FontWeight = FontWeight.SemiBold });
            _det.Children.Add(new TextBlock { Text = "• Ghi chú mới (Ctrl + N): số liệu, số tài khoản, việc cần nhớ…\n• Kéo thả tệp / ảnh vào trang để cất vào kho.\n• Ngăn bên trái để chia nhóm; thẻ để lọc nhanh.\n• Đăng nhập Google ở tab Sao lưu & Google để kho tự sao lưu lên Drive của bạn.", TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, Tag = "keep" });
            return;
        }
        var trash = r?.Dl == 1;
        var head = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        head.Children.Add(Pill(r is null ? "Ghi chú mới" : KindName.GetValueOrDefault(r.Kind, "Mục"), r is null ? "accent" : "info"));
        if (trash) head.Children.Add(Pill("Trong thùng rác", "bad"));
        if (r is not null && Svc.Config.Linked) head.Children.Add(r.Sq > Svc.Config.LastSeq || (r.IsFile && r.Fid.Length == 0) ? Pill("Chờ sao lưu", "warn") : Pill("Đã sao lưu Drive", "ok"));
        _det.Children.Add(head);
        if (r is not null)
            _det.Children.Add(new TextBlock { Text = $"Tạo {When(r.Ts)} · sửa {When(r.Up)}" + (r.Src == "dt" ? " · từ điện thoại" : ""), FontSize = 11.5, Classes = { "muted" }, Tag = "keep" });

        _dTitle.Text = r?.Ti ?? "";
        _dTitle.Watermark = r?.IsFile == true ? "Tiêu đề (để trống = tên tệp)" : "Tiêu đề";
        _dText.Text = r?.Tx ?? "";
        _dText.Watermark = r?.IsFile == true ? "Ghi chú kèm tệp…" : "Nội dung ghi chú, số liệu, liên kết…";
        _dTags.Text = r is null ? "" : string.Join(", ", r.Tg);
        _dPin.IsChecked = r?.Pn == 1;
        _dNgan.ItemsSource = _ngans.Select(n => n.Name).ToList();
        var ng = r?.Ng ?? TargetNgan;
        _dNgan.SelectedIndex = Math.Max(0, _ngans.FindIndex(n => n.Id == ng));
        foreach (var c in new Control[] { _dTitle, _dText, _dTags, _dPin, _dNgan }) c.IsEnabled = !trash;

        if (r?.IsFile == true)
        {
            var abs = st.AbsPath(r);
            var has = st.HasLocal(r);
            _det.Children.Add(new TextBlock { Text = r.Fn, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            _det.Children.Add(new TextBlock { Text = LuuTruService.Size(r.Fs) + (has ? "" : " · chưa có trên máy này (khôi phục từ Drive để tải về)"), FontSize = 12, Classes = { "muted" }, Tag = "keep" });
            if (has && r.T == "image")
            {
                try
                {
                    using var fs = File.OpenRead(abs);
                    _det.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = new Image { Source = Bitmap.DecodeToWidth(fs, 640), MaxHeight = 260, Stretch = Stretch.Uniform } });
                }
                catch { /* ảnh hỏng: bỏ xem trước */ }
            }
            if (has && !trash)
            {
                var fb = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
                var open = Icon.Button("Mở", "eye"); open.Click += (_, _) => Ui.OpenExternal(abs);
                var dir = Icon.Button("Thư mục", "folder"); dir.Click += (_, _) => Ui.OpenExternal(Path.GetDirectoryName(abs)!);
                var save = Icon.Button("Lưu bản sao…", "download"); save.Click += async (_, _) => await SaveCopy(r);
                fb.Children.Add(open); fb.Children.Add(dir); fb.Children.Add(save);
                _det.Children.Add(fb);
            }
        }
        _det.Children.Add(Field("Tiêu đề", _dTitle));
        _det.Children.Add(Field(r?.IsFile == true ? "Ghi chú" : "Nội dung", _dText));
        if (r?.IsLink == true)
        {
            var lb = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            foreach (var m in LtStore.UrlRe.Matches(r.Tx).Select(m => m.Value).Distinct().Take(3))
            {
                var b = Icon.Button(m.Length > 34 ? m[..34] + "…" : m, "play", "ghost"); ToolTip.SetTip(b, m);
                b.Click += (_, _) => Ui.OpenExternal(m); lb.Children.Add(b);
            }
            _det.Children.Add(lb);
        }
        var two = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        var f1 = Field("Ngăn", _dNgan); var f2 = Field("Thẻ", _dTags);
        Grid.SetColumn(f2, 2); two.Children.Add(f1); two.Children.Add(f2);
        _det.Children.Add(two);
        _det.Children.Add(_dPin);

        var acts = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        if (trash && r is not null)
        {
            var back = Icon.Button("Khôi phục", "undo"); back.Click += (_, _) => Ui.Run(this, () => { Svc.Store.SetDeleted(new[] { r.Id }, 0); Reload(); });
            var kill = Icon.Button("Xóa hẳn", "trash", "danger"); kill.Click += async (_, _) => await DeleteSelected(r);
            acts.Children.Add(back); acts.Children.Add(kill);
        }
        else
        {
            var save = Icon.Button(r is null ? "Lưu ghi chú" : "Lưu thay đổi", "save"); ToolTip.SetTip(save, "Ctrl + S");
            save.Click += (_, _) => SaveDetail();
            acts.Children.Add(save);
            if (r is not null)
            {
                var copy = Icon.Button("Chép", "copy"); ToolTip.SetTip(copy, "Chép tiêu đề + nội dung");
                copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { await cb.SetTextAsync((r.Ti.Length > 0 ? r.Ti + "\n" : "") + r.Tx); _status.Text = "Đã chép."; } };
                var del = Icon.Button("Xóa", "trash", "danger"); del.Click += async (_, _) => await DeleteSelected(r);
                acts.Children.Add(copy); acts.Children.Add(del);
            }
            else
            {
                var cancel = new Button { Content = "Đóng", Classes = { "ghost" } };
                cancel.Click += (_, _) => { _newMode = false; Reload(); };
                acts.Children.Add(cancel);
            }
        }
        _det.Children.Add(acts);
    }

    private static Control Field(string label, Control c)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = label, Classes = { "label" } });
        if (c.Parent is Panel p) p.Children.Remove(c);
        s.Children.Add(c);
        return s;
    }

    private void SaveDetail()
    {
        if (_cur?.Dl == 1) return;
        Ui.Run(this, () =>
        {
            var st = Svc.Store;
            var ng = _dNgan.SelectedIndex >= 0 && _dNgan.SelectedIndex < _ngans.Count ? _ngans[_dNgan.SelectedIndex].Id : TargetNgan;
            var tags = LtStore.ParseTags(_dTags.Text ?? "");
            LtRec r;
            if (_cur is null)
            {
                r = st.AddNote(ng, _dTitle.Text ?? "", _dText.Text ?? "", tags);
                if (_dPin.IsChecked == true) st.Edit(r.Id, new LtStore.Patch(Pinned: true));
                _status.Text = "Đã lưu ghi chú mới.";
            }
            else
            {
                r = st.Edit(_cur.Id, new LtStore.Patch(_dTitle.Text ?? "", _dText.Text ?? "", ng, tags, _dPin.IsChecked == true));
                _status.Text = "Đã lưu.";
            }
            _newMode = false;
            if (_ngan != AllNgan && _ngan != r.Ng) _ngan = r.Ng;
            Reload(r.Id);
        });
    }

    private List<LtRec> SelectedRecs()
    {
        var sel = _g.Selection;
        var shown = new HashSet<int>(_g.ShownRows);
        var list = Enumerable.Range(sel.R1, Math.Max(0, sel.R2 - sel.R1 + 1)).Where(i => i >= 0 && i < _rows.Count && shown.Contains(i)).Select(i => _rows[i]).ToList();
        if (list.Count == 0 && _cur is not null) list.Add(_cur);
        return list;
    }

    private async Task DeleteSelected(LtRec? one = null)
    {
        var recs = one is not null ? new List<LtRec> { one } : SelectedRecs();
        if (recs.Count == 0) return;
        var hard = recs.All(r => r.Dl == 1);
        var what = recs.Count == 1 ? $"“{recs[0].Title}”" : $"{recs.Count} mục đã chọn";
        if (hard && !await Ui.Confirm(this, $"Xóa hẳn {what}? Tệp trên máy bị xóa ngay, bản trên Google Drive xóa ở lượt sao lưu sau. Không hoàn tác được.", "Xóa hẳn", "Xóa hẳn")) return;
        Ui.Run(this, () =>
        {
            var n = Svc.Store.SetDeleted(recs.Select(r => r.Id).ToList(), hard ? 2 : 1);
            _cur = null;
            Reload();
            _status.Text = hard ? $"Đã xóa hẳn {n} mục." : $"Đã chuyển {n} mục vào thùng rác (khôi phục được ở ngăn Thùng rác).";
        });
    }

    private async Task MoveSelected()
    {
        var recs = SelectedRecs().Where(r => r.Dl == 0).ToList();
        if (recs.Count == 0) { _status.Text = "Chọn mục cần chuyển (kéo chọn nhiều dòng trong bảng)."; return; }
        var names = _ngans.Select(n => n.Name).ToList();
        var pick = await Ui.Choose(this, $"Chuyển {recs.Count} mục sang ngăn:", "Chuyển ngăn", names, Math.Max(0, _ngans.FindIndex(n => n.Id == recs[0].Ng)));
        if (pick is null) return;
        Ui.Run(this, () => { var n = Svc.Store.Move(recs.Select(r => r.Id), _ngans[pick.Value.Index].Id); Reload(); _status.Text = $"Đã chuyển {n} mục sang ngăn {names[pick.Value.Index]}."; });
    }

    private async Task AddNgan()
    {
        var name = await Ui.Prompt(this, "Tên ngăn mới (vd: Hợp đồng, Số liệu tháng, Cá nhân):", "Thêm ngăn");
        if (string.IsNullOrWhiteSpace(name)) return;
        Ui.Run(this, () => { var n = Svc.Store.AddNgan(name); _ngan = n.Id; Reload(); });
    }

    private async Task RenameNgan(string id)
    {
        var name = await Ui.Prompt(this, "Tên mới của ngăn:", "Đổi tên ngăn", Svc.Store.NganName(id));
        if (string.IsNullOrWhiteSpace(name)) return;
        Ui.Run(this, () => { Svc.Store.Edit(id, new LtStore.Patch(Title: name)); Reload(); });
    }

    private async Task DeleteNgan(string id)
    {
        var n = _ngans.FirstOrDefault(x => x.Id == id);
        if (n is null) return;
        if (!await Ui.Confirm(this, $"Xóa ngăn “{n.Name}”? {n.Count} mục trong ngăn được chuyển về ngăn Chung (không mất mục nào).", "Xóa ngăn", "Xóa ngăn")) return;
        Ui.Run(this, () => { Svc.Store.DeleteNgan(id); if (_ngan == id) _ngan = AllNgan; Reload(); });
    }

    private async Task PickFiles()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Chọn tệp / ảnh cất vào Lưu trữ cá nhân", AllowMultiple = true });
        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
        if (paths.Count > 0) await AddFiles(paths);
    }

    private async Task AddFiles(List<string> paths)
    {
        var ng = TargetNgan;
        _status.Text = $"Đang chép {paths.Count} tệp…";
        var errs = new List<string>();
        string? last = null;
        await Task.Run(() =>
        {
            foreach (var p in paths)
            {
                try { last = Svc.Store.AddFile(ng, p).Id; }
                catch (Exception e) { errs.Add(Path.GetFileName(p) + ": " + e.Message); }
            }
        });
        if (_ngan == TrashNgan) _ngan = AllNgan;
        _newMode = false;
        Reload(last);
        _status.Text = $"Đã cất {paths.Count - errs.Count} tệp vào ngăn {Svc.Store.NganName(ng)}." + (errs.Count > 0 ? " Lỗi: " + string.Join("; ", errs) : "");
    }

    private async Task SaveCopy(LtRec r)
    {
        var ext = Path.GetExtension(r.Fn).TrimStart('.');
        var p = await Ui.SaveFile(this, "Lưu bản sao", r.Fn, ext.Length > 0 ? ext : "bin", ext.Length > 0 ? ext.ToUpperInvariant() : "Tệp");
        if (p is null) return;
        Ui.Run(this, () => { File.Copy(Svc.Store.AbsPath(r), p, true); _status.Text = "Đã lưu bản sao: " + p; });
    }

    private void OpenFirstLink(LtRec r)
    {
        var m = LtStore.UrlRe.Match(r.Tx);
        if (m.Success) Ui.OpenExternal(m.Value);
    }

    // ================================================================== Sao lưu & Google

    private void RenderSync()
    {
        LtConfig cfg; LtStore st;
        try { st = Svc.Store; cfg = Svc.Config; } catch { return; }
        var client = Svc.Client;

        // ---- tài khoản Google
        _gAcc.Children.Clear();
        var who = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        if (!cfg.Linked) who.Children.Add(Pill("Chưa đăng nhập Google", "warn"));
        else
        {
            who.Children.Add(Pill(cfg.NeedRelink ? "Cần đăng nhập lại" : "Đã đăng nhập", cfg.NeedRelink ? "bad" : "ok"));
            who.Children.Add(new TextBlock { Text = cfg.Email.Length > 0 ? cfg.Email : "(chưa rõ Gmail)", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        }
        who.Children.Add(new TextBlock { Text = "· tài khoản phần mềm: " + App.Services.Session.Username, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
        _gAcc.Children.Add(who);
        _gAcc.Children.Add(new TextBlock
        {
            Text = "Đăng nhập Google 1 lần là dùng chung với Sao lưu (Hệ thống › Đồng bộ & sao lưu); mỗi tài khoản phần mềm có đăng nhập RIÊNG. Phần mềm chỉ thấy thư mục do nó tạo trên Drive (" + LuuTruService.DriveRoot + "), không đọc được tệp khác của bạn. Mã đăng nhập cất mã hóa theo tài khoản Windows của máy này.",
            TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, Tag = "keep",
        });
        var ab = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        if (_signCts is not null)
        {
            ab.Children.Add(new TextBlock { Text = "Đang chờ đăng nhập trên trình duyệt…", VerticalAlignment = VerticalAlignment.Center, Foreground = AppTheme.Brush("#1F6FD6") });
            var cancel = new Button { Content = "Hủy" }; cancel.Click += (_, _) => _signCts?.Cancel();
            ab.Children.Add(cancel);
        }
        else
        {
            var sign = Icon.Button(!cfg.Linked ? "Đăng nhập Google" : cfg.NeedRelink ? "Đăng nhập lại" : "Đổi tài khoản Google", "user", !cfg.Linked || cfg.NeedRelink ? "primary" : "");
            sign.IsEnabled = client.Ok;
            if (!client.Ok) ToolTip.SetTip(sign, "Nhập Client ID ở mục Cài đặt kết nối Google bên dưới trước.");
            sign.Click += async (_, _) => await SignIn();
            ab.Children.Add(sign);
            if (cfg.Linked)
            {
                var outB = Icon.Button("Đăng xuất Google", "logout", "ghost");
                outB.Click += async (_, _) =>
                {
                    if (!await Ui.Confirm(this, "Đăng xuất Google khỏi Lưu trữ cá nhân? Bản sao lưu đã có trên Drive giữ nguyên; kho trên máy không đổi.", "Đăng xuất Google", "Đăng xuất")) return;
                    await Ui.RunAsync(this, () => Svc.SignOutAsync());
                };
                ab.Children.Add(outB);
            }
        }
        _gAcc.Children.Add(ab);

        _cid.Text = client.Id; _csec.Text = client.Secret;
        var oauth = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        oauth.Children.Add(new TextBlock
        {
            Text = "Google cần 1 “OAuth client” loại Ứng dụng máy tính (Desktop app). Tạo 1 lần (miễn phí) ở console.cloud.google.com: tạo dự án › bật Google Drive API › " +
                   "Màn hình xin quyền (External, thêm Gmail của bạn vào Test users hoặc Publish) › Credentials › Create credentials › OAuth client ID › Desktop app › chép Client ID + Client secret dán vào đây. " +
                   "Máy đã dùng Drive cá nhân của app cũ: bấm “Lấy từ app cũ”.",
            TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, Tag = "keep",
        });
        oauth.Children.Add(Field("Client ID", _cid));
        oauth.Children.Add(Field("Client secret", _csec));
        var ob = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        var saveC = Icon.Button("Lưu", "save"); saveC.Click += (_, _) => Ui.Run(this, () => { Svc.SetClient(_cid.Text ?? "", _csec.Text ?? ""); RenderSync(); });
        var old = Icon.Button("Lấy từ app cũ…", "download"); old.Click += async (_, _) => await ClientFromOld();
        var guide = Icon.Button("Mở Google Cloud Console", "play", "ghost"); guide.Click += (_, _) => Ui.OpenExternal("https://console.cloud.google.com/apis/credentials");
        ob.Children.Add(saveC); ob.Children.Add(old); ob.Children.Add(guide);
        oauth.Children.Add(ob);
        _oauth.Content = oauth;
        if (!client.Ok) _oauth.IsExpanded = true;
        if (_oauth.Parent is Panel pp) pp.Children.Remove(_oauth);
        _gAcc.Children.Add(_oauth);

        // ---- sao lưu
        _gBk.Children.Clear();
        var state = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        var dirty = Svc.Dirty(st, cfg);
        if (Svc.Busy) state.Children.Add(Pill("Đang chạy…", "info"));
        else if (!cfg.Linked) state.Children.Add(Pill("Chưa đăng nhập Google — chưa sao lưu", "warn"));
        else if (cfg.LastErr.Length > 0) state.Children.Add(Pill("Lỗi: " + cfg.LastErr, "bad"));
        else if (dirty) state.Children.Add(Pill("Có thay đổi chưa sao lưu", "info"));
        else state.Children.Add(Pill("Đã sao lưu đầy đủ", "ok"));
        state.Children.Add(new TextBlock { Text = cfg.LastOk is { } lo ? "Lần cuối: " + lo.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) + " (" + Ago(lo) + ")" : "Chưa sao lưu lần nào", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
        if (_quota.Length > 0) state.Children.Add(new TextBlock { Text = "· " + _quota, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
        _gBk.Children.Add(state);

        var auto = new ComboBox { ItemsSource = LtConfig.AutoOptions.Select(o => o.Label).ToList(), Width = 260 };
        auto.SelectedIndex = Math.Max(0, Array.FindIndex(LtConfig.AutoOptions, o => o.Key == cfg.Auto));
        var hour = new ComboBox { ItemsSource = Enumerable.Range(0, 24).Select(h => $"lúc {h:00}:00").ToList(), SelectedIndex = Math.Clamp(cfg.DailyHour, 0, 23), Width = 110 };
        var hourField = Field("Giờ", hour); hourField.IsVisible = cfg.Auto == LtConfig.AutoDaily;
        var keep = new ComboBox { ItemsSource = LtConfig.KeepOptions.Select(d => $"{d} ngày").ToList(), Width = 110 };
        keep.SelectedIndex = Math.Max(0, Array.IndexOf(LtConfig.KeepOptions, cfg.KeepDays));
        void SaveCfg()
        {
            var c = Svc.Config;
            c.Auto = LtConfig.AutoOptions[Math.Max(0, auto.SelectedIndex)].Key;
            c.DailyHour = Math.Max(0, hour.SelectedIndex);
            c.KeepDays = LtConfig.KeepOptions[Math.Max(0, keep.SelectedIndex)];
            Svc.SaveConfig(c);
            hourField.IsVisible = c.Auto == LtConfig.AutoDaily;
        }
        auto.SelectionChanged += (_, _) => SaveCfg();
        hour.SelectionChanged += (_, _) => SaveCfg();
        keep.SelectionChanged += (_, _) => SaveCfg();
        var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        row.Children.Add(Field("Tự sao lưu", auto));
        row.Children.Add(hourField);
        row.Children.Add(Field("Giữ bản theo ngày", keep));
        _gBk.Children.Add(row);
        _gBk.Children.Add(new TextBlock
        {
            Text = "Mỗi tệp chỉ tải lên 1 lần; mỗi lượt cập nhật danh sách mục (data.json) + 1 bản theo ngày để quay lại ngày trước. Tệp xóa hẳn trong kho cũng được xóa trên Drive. Tự sao lưu chạy ngầm khi phần mềm đang mở.",
            TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, Tag = "keep",
        });
        var bb = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        var now = Icon.Button("Sao lưu ngay", "cloud", cfg.Linked && !cfg.NeedRelink ? "primary" : "");
        now.IsEnabled = cfg.Linked && !Svc.Busy;
        now.Click += async (_, _) => await BackupNow();
        var rest = Icon.Button("Khôi phục từ Drive…", "history");
        rest.IsEnabled = cfg.Linked && !Svc.Busy;
        rest.Click += async (_, _) => await Restore();
        bb.Children.Add(now); bb.Children.Add(rest);
        _gBk.Children.Add(bb);

        // ---- lịch sử
        _gHist.Children.Clear();
        if (cfg.History.Count == 0) _gHist.Children.Add(new TextBlock { Text = "Chưa có lượt nào.", Classes = { "muted" } });
        foreach (var h in Enumerable.Reverse(cfg.History).Take(15))
        {
            var line = new DockPanel();
            var t = new TextBlock { Text = h.At.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture), Width = 90, Classes = { "muted" }, FontSize = 12 };
            line.Children.Add(t);
            var p = Pill(h.Ok ? h.Kind : h.Kind + " lỗi", h.Ok ? "ok" : "bad"); p.Margin = new Thickness(0, 0, 8, 0); p.Width = 110;
            line.Children.Add(p);
            line.Children.Add(new TextBlock { Text = h.Msg, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            _gHist.Children.Add(line);
        }
    }

    private Control BuildLocal()
    {
        var s = new StackPanel { Spacing = 8 };
        s.Children.Add(new TextBlock
        {
            Text = "Bản sao .zip chứa toàn bộ ghi chú + tệp — cất ra USB / ổ khác, hoặc chuyển kho sang máy khác. Nạp .zip / dữ liệu app cũ chỉ THÊM mục còn thiếu, không xóa mục đang có.",
            TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, Tag = "keep",
        });
        var w = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        var exp = Icon.Button("Xuất bản sao .zip…", "download");
        exp.Click += async (_, _) =>
        {
            var p = await Ui.SaveFile(this, "Xuất bản sao Lưu trữ cá nhân", $"LuuTruCaNhan_{Svc.Store.Account}_{DateTime.Now:yyyy-MM-dd}.zip", "zip", "Tệp nén");
            if (p is null) return;
            await Ui.RunAsync(this, async () => { var n = await Task.Run(() => Svc.ExportZip(p)); await Ui.Message(this, $"Đã xuất {N(Svc.Store.Stats().Items)} mục ({n} tệp) ra:\n{p}"); });
        };
        var imp = Icon.Button("Nạp từ .zip…", "upload");
        imp.Click += async (_, _) =>
        {
            var p = await Ui.OpenFile(this, "Chọn bản sao .zip của Lưu trữ cá nhân", "zip", "Tệp nén");
            if (p is null) return;
            await Ui.RunAsync(this, async () => { var r = await Task.Run(() => Svc.ImportZip(p)); Reload(); await Ui.Message(this, $"Đã nạp: {r.Items} mục mới / đổi, {r.Files} tệp." + (r.MissingFiles > 0 ? $" {r.MissingFiles} tệp không có trong .zip." : "")); });
        };
        var old = Icon.Button("Nạp từ app cũ…", "history");
        ToolTip.SetTip(old, "Chép Lưu trữ cá nhân (M19) của app cũ — chọn thư mục app cũ (vd Desktop\\PM-Kho-Lanh). Chỉ đọc, không đổi gì ở app cũ.");
        old.Click += async (_, _) => await ImportOld();
        var dir = Icon.Button("Mở thư mục dữ liệu", "folder", "ghost");
        dir.Click += (_, _) => Ui.OpenExternal(Svc.Store.Dir);
        w.Children.Add(exp); w.Children.Add(imp); w.Children.Add(old); w.Children.Add(dir);
        s.Children.Add(w);
        return s;
    }

    private static string? GuessOldFolder()
    {
        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (var p in new[] { Path.Combine(desk, "PM-Kho-Lanh"), @"C:\PM-Kho-Lanh", Path.Combine(desk, "KhoLanh") })
            if (Directory.Exists(p)) return p;
        return null;
    }

    private async Task ImportOld()
    {
        var folder = GuessOldFolder();
        var found = folder is null ? new() : LuuTruService.FindLegacy(folder);
        if (found.Count == 0)
        {
            folder = await Ui.OpenFolder(this, "Chọn thư mục app cũ (PM-Kho-Lanh) hoặc thư mục config\\luu-tru");
            if (folder is null) return;
            found = LuuTruService.FindLegacy(folder);
        }
        if (found.Count == 0) { await Ui.Message(this, "Không thấy dữ liệu Lưu trữ cá nhân của app cũ trong thư mục này (cần có config\\luu-tru\\<tài khoản>\\data.json)."); return; }
        var me = Svc.Store.Account;
        var items = found.Select(f => $"{f.Account} — {f.Items} mục" + (f.Account == me ? " (đúng tài khoản đang đăng nhập)" : "")).ToList();
        var pick = await Ui.Choose(this, "Chọn kho của app cũ để nạp vào kho của tài khoản " + App.Services.Session.Username + ":", "Nạp từ app cũ", items, Math.Max(0, found.FindIndex(f => f.Account == me)), yes: "Nạp");
        if (pick is null) return;
        var dirSel = found[pick.Value.Index].Dir;
        await Ui.RunAsync(this, async () =>
        {
            var r = await Task.Run(() => Svc.ImportLegacy(dirSel));
            Reload();
            await Ui.Message(this, $"Đã nạp {r.Items} mục, chép {r.Files} tệp từ app cũ." + (r.MissingFiles > 0 ? $" {r.MissingFiles} tệp không còn trên máy (app cũ chưa tải về)." : "") + "\nApp cũ giữ nguyên.");
        });
    }

    private async Task ClientFromOld()
    {
        var folder = GuessOldFolder() ?? await Ui.OpenFolder(this, "Chọn thư mục app cũ (PM-Kho-Lanh)");
        if (folder is null) return;
        var c = LuuTruService.ReadLegacyClient(folder);
        if (c is null && GuessOldFolder() == folder)
        {
            folder = await Ui.OpenFolder(this, "Chọn thư mục app cũ (PM-Kho-Lanh)");
            if (folder is null) return;
            c = LuuTruService.ReadLegacyClient(folder);
        }
        if (c is null) { await Ui.Message(this, "Không thấy cài đặt Drive cá nhân (config\\drive-ca-nhan.json) trong thư mục app cũ."); return; }
        _cid.Text = c.Id;
        if (c.Secret.Length > 0) _csec.Text = c.Secret;
        Ui.Run(this, () => Svc.SetClient(c.Id, c.Secret.Length > 0 ? c.Secret : _csec.Text ?? ""));
        RenderSync();
        if (c.Secret.Length == 0)
            await Ui.Message(this, "Đã lấy Client ID của app cũ. Client secret app cũ cất mã hóa nên không đọc được — mở Google Cloud Console › Credentials, chép Client secret dán vào ô bên dưới rồi bấm Lưu.");
    }

    private async Task SignIn()
    {
        _signCts = new CancellationTokenSource();
        RenderSync();
        try
        {
            var mail = await Svc.SignInAsync(Ui.OpenExternal, _signCts.Token);
            _status.Text = "Đã đăng nhập Google: " + mail;
            _ = LoadQuota();
            var cfg = Svc.Config;
            if (Svc.Store.Stats().Items == 0
                && await Ui.Confirm(this, "Kho trên máy này đang trống. Tải về bản sao lưu có sẵn trên Drive của " + mail + " (nếu có)?", "Khôi phục", "Khôi phục"))
                await RunRestore(null);
            else if (cfg.Auto != LtConfig.AutoOff && Svc.Store.Stats().Items > 0) await BackupNow(quiet: true);
        }
        catch (Exception e) when (e is not OperationCanceledException) { await Ui.Message(this, Ui.Describe(e), "Đăng nhập Google"); }
        catch (OperationCanceledException) { }
        finally { _signCts?.Dispose(); _signCts = null; RenderSync(); RenderKpi(); }
    }

    private async Task LoadQuota()
    {
        try
        {
            var cfg = Svc.Config;
            if (!cfg.Linked) return;
            var a = await Svc.Google.AccessAsync(Svc.Client, KhoLanh.Core.Services.Secret.Unprotect(cfg.Refresh));
            if (await Svc.Google.QuotaAsync(a) is { } q)
                _quota = "Drive đã dùng " + LuuTruService.Size(q.Used) + (q.Limit > 0 ? " / " + LuuTruService.Size(q.Limit) : "");
            Dispatcher.UIThread.Post(RenderSync);
        }
        catch { /* chỉ để xem */ }
    }

    private async Task BackupNow(bool quiet = false)
    {
        RenderSync();
        try
        {
            var r = await Svc.BackupAsync();
            _status.Text = r.Message;
            if (!quiet) _ = LoadQuota();
        }
        catch (Exception e) { if (!quiet) await Ui.Message(this, Ui.Describe(e), "Sao lưu Google Drive"); }
        finally { Reload(); RenderSync(); }
    }

    private async Task Restore()
    {
        List<LtGoogle.DFile> snaps;
        try { snaps = await Svc.SnapshotsAsync(); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Khôi phục"); return; }
        if (snaps.Count == 0) { await Ui.Message(this, "Drive chưa có bản sao lưu nào của tài khoản này."); return; }
        var items = snaps.Select(s => s.Name == "data.json" ? $"Bản mới nhất ({s.Modified:dd/MM/yyyy HH:mm})" : "Bản ngày " + LuuTruService.DayOf(s.Name)!.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)).ToList();
        var pick = await Ui.Choose(this, "Khôi phục sẽ GỘP bản trên Drive vào kho trên máy: thêm mục còn thiếu, mục sửa sau thắng, tải tệp còn thiếu về. Không xóa mục đang có.", "Khôi phục từ Drive", items, 0, yes: "Khôi phục");
        if (pick is null) return;
        await RunRestore(snaps[pick.Value.Index].Id);
    }

    private async Task RunRestore(string? id)
    {
        _status.Text = "Đang khôi phục từ Drive…";
        RenderSync();
        try
        {
            var prog = new Progress<string>(m => _status.Text = m);
            var r = await Svc.RestoreAsync(id, prog);
            Reload();
            await Ui.Message(this, $"Đã khôi phục: {r.Items} mục mới / đổi, tải {r.Files} tệp." + (r.MissingFiles > 0 ? $" {r.MissingFiles} tệp không còn trên Drive." : ""), "Khôi phục");
        }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Khôi phục"); }
        finally { RenderSync(); }
    }
}

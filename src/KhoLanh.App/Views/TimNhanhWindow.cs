using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.TimNhanh;

namespace KhoLanh.App.Views;

/// <summary>
/// TÌM NHANH TOÀN APP (Ctrl + K, theo global-search.js của app cũ): gõ tên module → nhảy tới; gõ mã hàng / tên / size (BTP 50) /
/// vị trí / lô / hợp đồng → bảng tồn hiện tại ở mọi module (Tồn kho An An, Tồn vị trí, Tồn bột, Tồn kho gửi, NXT Bột/Sốt).
/// Chọn 1 dòng → chi tiết mã đó (theo nguồn, các vị trí, hợp đồng) + nút mở module. Dữ liệu đọc lại mỗi lần mở.
/// </summary>
public sealed class TimNhanhWindow : Window
{
    public sealed record ModuleLink(string Key, string Label, string Group);

    private const int Page = 400;
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45"), Line_ = AppTheme.Brush("#D0D7E2"), Hit = AppTheme.Brush("#FFF2B3");
    private static readonly Dictionary<string, string> SrcColor = new() { ["M01"] = "#1F8FD1", ["M01VT"] = "#7C3AED", ["M01VTB"] = "#C2410C", ["M02"] = "#1E8E3E", ["M03"] = "#B45309" };

    private readonly IReadOnlyList<ModuleLink> _mods;
    private readonly Action<string> _go;
    private readonly Action<string, Control>? _show;
    private readonly WrapPanel _docPanel = new();
    private List<GsDoc> _docs = new();
    // Lọc theo cột (lọc phân tầng: giá trị của 1 cột lấy từ các dòng khớp ô tìm + bộ lọc các cột khác)
    private static readonly (string Key, string Label)[] FilterCols = { ("src", "Nguồn"), ("vt", "Khu vị trí"), ("size", "Size"), ("hd", "Hợp đồng"), ("dt", "Đặc tính") };
    private readonly Dictionary<string, ComboBox> _cf = new();
    private readonly Button _cfClear = new() { Content = "Bỏ lọc cột", IsVisible = false, Padding = new Thickness(8, 3), FontSize = 12.5 };
    private readonly StackPanel _cfPanel = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private bool _cfBusy;
    private List<GsRow> _base = new();
    private readonly TextBox _q = new() { Watermark = "Tìm mã hàng, size (BTP 50), vị trí, hợp đồng, lô, đặc tính hoặc tên module…", FontSize = 15, Padding = new Thickness(10, 7) };
    private readonly TextBlock _status = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private readonly WrapPanel _modPanel = new();
    private readonly GridModel _m = new() { ReadOnly = true };
    private readonly SheetGrid _g;
    private readonly TextBlock _foot = new() { FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _more = new() { Content = "Hiện thêm", IsVisible = false };
    private readonly StackPanel _detail = new() { Spacing = 8, Margin = new Thickness(0, 0, 14, 0) };
    private readonly Border _detailBox;

    private Gs.Data? _data; private bool _loading;
    private List<GsRow> _list = new(); private List<string> _terms = new(); private int _limit = Page;
    private string? _detailKey;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };

    public TimNhanhWindow(IReadOnlyList<ModuleLink> modules, Action<string> go, string initial = "", Action<string, Control>? show = null)
    {
        _mods = modules; _go = go; _show = show;
        _cfPanel.Children.Add(new TextBlock { Text = "Lọc cột:", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        foreach (var (k, lb) in FilterCols)
        {
            var cb = new ComboBox { MinWidth = 130, MaxWidth = 220, FontSize = 12.5, PlaceholderText = lb };
            ToolTip.SetTip(cb, "Lọc theo " + lb);
            cb.SelectionChanged += (_, _) => { if (!_cfBusy) { _limit = Page; Render(); } };
            _cf[k] = cb; _cfPanel.Children.Add(cb);
        }
        _cfClear.Click += (_, _) => { _cfBusy = true; foreach (var c in _cf.Values) c.SelectedIndex = -1; _cfBusy = false; _limit = Page; Render(); };
        _cfPanel.Children.Add(_cfClear);
        Title = "Tìm nhanh toàn app"; Width = 1240; Height = 760; MinWidth = 900; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = AppTheme.Brush("#F3F6F9", "#181A1E");

        _g = new SheetGrid(_m) { SpareRows = 0, CellBackground = CellBg };
        _g.SelectionChanged += () => { if (_g.Row >= 0 && _g.Row < Math.Min(_list.Count, _limit)) ShowDetail(_list[_g.Row].Key); };
        _more.Click += (_, _) => { _limit += Page; Render(); };

        _q.Text = initial;
        _q.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _limit = Page; Render(); };
        _q.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { _debounce.Stop(); if (_modPanel.Children.OfType<Button>().FirstOrDefault() is { Tag: string k } && _list.Count == 0) Go(k); else Render(); e.Handled = true; }
            else if (e.Key == Key.Down && _list.Count > 0) { _g.Focus(); _g.Select(0, 2); e.Handled = true; }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { if (_detailKey is not null) CloseDetail(); else Close(); e.Handled = true; }
            else if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _q.Focus(); _q.SelectAll(); e.Handled = true; }
        };

        _detailBox = new Border { Width = 360, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(14, 12), Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), IsVisible = false, Child = new ScrollViewer { Content = _detail } };

        var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(_q); top.Children.Add(_status); top.Children.Add(_modPanel); top.Children.Add(_docPanel); top.Children.Add(_cfPanel);
        var tbl = new DockPanel();
        var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; foot.Children.Add(_foot); foot.Children.Add(_more); _more.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(foot, Dock.Bottom); tbl.Children.Add(foot);
        tbl.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = _g });
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; body.Children.Add(tbl); Grid.SetColumn(_detailBox, 1); body.Children.Add(_detailBox);
        var root = new DockPanel { Margin = new Thickness(16) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(body);
        Content = root;

        Opened += async (_, _) => { _q.Focus(); _q.CaretIndex = _q.Text?.Length ?? 0; await LoadAsync(); };
        Render();
    }

    /// <summary>Đọc lại tồn hiện tại của các module (chạy nền).</summary>
    public async Task LoadAsync()
    {
        _loading = true; RenderStatus();
        try { (_data, _docs) = await Task.Run(() => (new GsService(App.Services).Build(), GsDocs.Build(App.Services))); }
        catch (Exception e) { _data = Gs.Prepare(Array.Empty<GsRow>(), new[] { e.Message }); }
        _loading = false; Render();
    }
    /// <summary>Dùng cho công cụ chụp màn hình: nạp đồng bộ.</summary>
    public void LoadNow() { _data = new GsService(App.Services).Build(); _docs = GsDocs.Build(App.Services); Render(); }
    public void SetQuery(string q) { _q.Text = q; _debounce.Stop(); _limit = Page; Render(); }
    public void SelectRow(int r) { if (r < _list.Count) { _g.Select(r, 2); ShowDetail(_list[r].Key); } }

    private void Go(string key) { Close(); _go(key); }

    private static string FN(double? v) => v is null ? "—" : v.Value.ToString("#,0.##", CultureInfo.InvariantCulture);
    private static string FK(double? v) => v is null ? "—" : v.Value.ToString("#,0.00", CultureInfo.InvariantCulture);

    private void RenderStatus()
    {
        if (_loading) _status.Text = _data is null ? "Đang đọc dữ liệu tồn kho…" : "Đang cập nhật dữ liệu mới nhất…";
        else if (_data is { Errors.Count: > 0 }) _status.Text = "Một phần dữ liệu không đọc được — " + string.Join(" · ", _data.Errors);
        else if (_data is not null) _status.Text = $"Đang có {_data.Rows.Count:#,0} dòng tồn kho (Tồn kho An An, Tồn vị trí, Tồn bột, Tồn kho gửi, NXT Bột/Sốt — theo quyền của tài khoản). Esc để đóng.";
    }

    private void Render()
    {
        RenderStatus();
        var q = _q.Text ?? "";
        // module khớp tên
        _modPanel.Children.Clear();
        var nq = Gs.Norm(q).Trim();
        if (nq.Length > 0)
        {
            var ws = nq.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var m in _mods.Where(m => ws.All(w => Gs.Norm(m.Label + " " + m.Group).Contains(w))).Take(8))
            {
                var b = new Button { Content = $"↪ {m.Label}", Tag = m.Key, Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 6, 0), FontSize = 13 };
                ToolTip.SetTip(b, $"Mở {m.Group} › {m.Label}");
                b.Click += (_, _) => Go(m.Key); _modPanel.Children.Add(b);
            }
        }
        // phiếu / LSX / số cont (từ 2 ký tự)
        _docPanel.Children.Clear();
        var docs = q.Trim().Length >= 2 ? GsDocs.Search(_docs, q) : new();
        if (docs.Count > 0)
        {
            _docPanel.Children.Add(new TextBlock { Text = "Phiếu · LSX · Số cont:", FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            foreach (var d in docs)
            {
                var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = d.Title, FontWeight = FontWeight.SemiBold, FontSize = 12.5 }); sp.Children.Add(new TextBlock { Text = d.Sub, FontSize = 11, Classes = { "muted" } });
                var b = new Button { Content = sp, Padding = new Thickness(8, 3), Margin = new Thickness(0, 0, 6, 4), Tag = d };
                b.Click += (_, _) => OpenDoc(d); _docPanel.Children.Add(b);
            }
        }
        if (_data is null || nq.Length == 0)
        {
            _list = new(); _base = new(); _terms = new(); FillFilters(); LoadGrid(); CloseDetail();
            _foot.Text = nq.Length == 0 ? "Gõ để tìm. Ví dụ: BTP 50 · VRHLBTN · A.01 · HD-2614 · bột chiên." : ""; _more.IsVisible = false; return;
        }
        _base = Gs.Search(_data, q, out _terms);
        _list = _base.Where(r => PassFilters(r, null)).ToList();
        FillFilters();
        LoadGrid();
        var tot = (ton: _list.Sum(r => r.Ton ?? 0), kg: _list.Sum(r => r.Kg ?? 0));
        _foot.Text = _list.Count == 0 ? $"Không có kết quả cho “{q.Trim()}”." : $"Tổng: {_list.Count:#,0} dòng · {_list.Select(r => r.Key).Distinct().Count():#,0} mã · tồn {FN(tot.ton)} · {FK(tot.kg)} kg";
        _more.IsVisible = _list.Count > _limit; _more.Content = $"Hiện thêm {Math.Min(Page, _list.Count - _limit):#,0} dòng (còn {_list.Count - _limit:#,0})";
        if (_detailKey is not null) ShowDetail(_detailKey);
    }

    private static string ColVal(GsRow r, string k)
    {
        var v = k switch { "src" => Gs.SrcName(r.Src), "vt" => Zone(r.Vt), "size" => r.Size, "hd" => r.Hd, "dt" => r.Dt, _ => "" };
        return string.IsNullOrWhiteSpace(v) ? "(Trống)" : v.Trim();
    }
    /// <summary>Khu của vị trí: phần đầu trước dấu chấm (bỏ "TG1.") — "A.01.1.1" → "A".</summary>
    private static string Zone(string vt) { var s = Gs.ShortPlace(vt); var i = s.IndexOf('.'); return i > 0 ? s[..i] : s; }
    private string? Picked(string k) => _cf[k].SelectedIndex > 0 ? _cf[k].SelectedItem as string : null;
    private bool PassFilters(GsRow r, string? except) => FilterCols.All(c => c.Key == except || Picked(c.Key) is not { } v || ColVal(r, c.Key) == v);

    private void FillFilters()
    {
        _cfBusy = true;
        foreach (var (k, lb) in FilterCols)
        {
            var cur = Picked(k);
            var vals = _base.Where(r => PassFilters(r, k)).Select(r => ColVal(r, k)).Distinct().OrderBy(x => x == "(Trống)" ? "\uffff" : x, StringComparer.CurrentCulture).ToList();
            var items = new List<string> { $"{lb}: tất cả" }; items.AddRange(vals);
            _cf[k].ItemsSource = items; _cf[k].SelectedIndex = cur is null ? 0 : Math.Max(0, items.IndexOf(cur));
            _cf[k].IsEnabled = vals.Count > 0;
        }
        _cfPanel.IsVisible = _base.Count > 0;
        _cfClear.IsVisible = FilterCols.Any(c => Picked(c.Key) is not null);
        _cfBusy = false;
    }

    private void OpenDoc(GsDoc d)
    {
        if (!_mods.Any(m => m.Key == d.Menu)) return;
        Close();
        if (_show is null) { _go(d.Menu); return; }
        if (d.Kind == "pnx") _show(d.Menu, new PhieuKhoView(0, d.Id));
        else _show(d.Menu, new ViTriView { OpenSlipId = d.Id });
    }

    private void LoadGrid()
    {
        GridColumn C(string k, string h, double w, string t = "text") => new(k, h, w, t) { ReadOnly = true };
        _m.Load(new[] { C("src", "Nguồn", 110), C("vt", "Vị trí", 110), C("ma", "Mã hàng", 205), C("size", "Size", 85), C("hd", "Hợp đồng", 120), C("dt", "Đặc tính", 120), C("ton", "Tồn cuối", 85, "number"), C("kg", "Tổng KL (kg)", 105, "number"), C("lo", "Lô / ghi chú", 170) },
            _list.Take(_limit).Select(r => new[] { Gs.SrcName(r.Src), Gs.ShortPlace(r.Vt), r.Ma, r.Size, r.Hd, r.Dt, FN(r.Ton), FK(r.Kg), string.Join(" · ", new[] { r.Lo, r.Extra }.Where(x => x.Length > 0)) }));
        _g.Refresh();
    }

    private static readonly int[] TextCols = { 1, 2, 3, 4, 5, 8 };
    private IBrush? CellBg(int r, int c)
    {
        if (r >= _list.Count || r >= _limit) return null;
        if (c == 0) return AppTheme.Brush("#2E" + SrcColor.GetValueOrDefault(_list[r].Src, "#5D6B78")[1..]);
        if (_terms.Count > 0 && TextCols.Contains(c) && Gs.Highlight(_m.Get(r, c), _terms).Any(b => b)) return Hit;
        return null;
    }

    // ------------------------------------------------------------ chi tiết 1 mã
    private void CloseDetail() { _detailKey = null; _detailBox.IsVisible = false; }

    private void ShowDetail(string key)
    {
        var rows = _list.Where(r => r.Key == key).ToList();
        if (rows.Count == 0) { CloseDetail(); return; }
        _detailKey = key; _detailBox.IsVisible = true; _detail.Children.Clear();
        var r0 = rows[0];
        var head = new DockPanel();
        var x = new Button { Content = "✕", Padding = new Thickness(8, 2) }; x.Click += (_, _) => CloseDetail(); DockPanel.SetDock(x, Dock.Right); head.Children.Add(x);
        head.Children.Add(new SelectableTextBlock { Text = r0.Ma, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Dark, TextWrapping = TextWrapping.Wrap });
        _detail.Children.Add(head);
        var ten = rows.Select(r => r.Ten).FirstOrDefault(t => t.Length > 0); if (ten is not null) _detail.Children.Add(new TextBlock { Text = ten, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        _detail.Children.Add(new TextBlock { Text = $"Chỉ các dòng khớp “{(_q.Text ?? "").Trim()}”" + (FilterCols.Any(c => Picked(c.Key) is not null) ? " và bộ lọc cột" : ""), Classes = { "muted" }, FontSize = 12 });
        var tags = new WrapPanel();
        foreach (var t in rows.Select(r => r.Tag).Where(t => t.Length > 0).Distinct().Take(1).Concat(rows.Select(r => r.Dt).Where(t => t.Length > 0).Distinct()))
            tags.Children.Add(new Border { Background = AppTheme.Brush("#E3F2FB"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 5, 4), Child = new TextBlock { Text = t, FontSize = 12 } });
        if (tags.Children.Count > 0) _detail.Children.Add(tags);

        var kpi = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Control K(string lb, string v) { var s = new StackPanel(); s.Children.Add(new TextBlock { Text = lb, Classes = { "muted" }, FontSize = 11.5 }); s.Children.Add(new TextBlock { Text = v, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Dark }); return s; }
        kpi.Children.Add(K("Tổng tồn cuối", FN(rows.Sum(r => r.Ton ?? 0)))); var k2 = K("Tổng KL (kg)", FK(rows.Sum(r => r.Kg ?? 0))); Grid.SetColumn(k2, 1); kpi.Children.Add(k2);
        _detail.Children.Add(kpi);

        _detail.Children.Add(Sec("Theo nguồn"));
        var by = Gs.Sources.Select(s => (s, xs: rows.Where(r => r.Src == s.Src).ToList())).Where(b => b.xs.Count > 0).ToList();
        var max = Math.Max(1, by.Max(b => b.xs.Sum(r => r.Ton ?? 0)));
        foreach (var (s, xs) in by)
        {
            var ton = xs.Sum(r => r.Ton ?? 0); var kg = xs.Sum(r => r.Kg ?? 0); var col = SrcColor.GetValueOrDefault(s.Src, "#5D6B78");
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("92,74,*"), Margin = new Thickness(0, 0, 0, 2) };
            g.Children.Add(new TextBlock { Text = s.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = AppTheme.Brush(col), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, 66 * ton / max), Margin = new Thickness(2, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bar, 1); g.Children.Add(bar);
            var v = new TextBlock { Text = FN(ton) + (kg > 0 ? $" · {FK(kg)} kg" : ""), FontSize = 12, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(v, 2); g.Children.Add(v);
            _detail.Children.Add(g);
        }

        _detail.Children.Add(Sec($"Vị trí ({rows.Count})"));
        foreach (var r in rows.OrderBy(r => Gs.SrcOrder(r.Src)).ThenByDescending(r => r.Ton ?? 0).Take(60))
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
            var l = new StackPanel();
            l.Children.Add(new TextBlock { Text = r.Vt.Length > 0 ? Gs.ShortPlace(r.Vt) : "Chưa có vị trí", FontWeight = FontWeight.SemiBold, FontSize = 12.5 });
            l.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { Gs.SrcName(r.Src), r.Lo, r.Hd, r.Dt, r.Extra }.Where(t => t.Length > 0)), Classes = { "muted" }, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            g.Children.Add(l);
            var rr = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            rr.Children.Add(new TextBlock { Text = FN(r.Ton), FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Right });
            if (r.Kg is not null) rr.Children.Add(new TextBlock { Text = FK(r.Kg) + " kg", Classes = { "muted" }, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right });
            Grid.SetColumn(rr, 1); g.Children.Add(rr);
            _detail.Children.Add(g);
        }
        if (_data is not null && Gs.Decode(_data, r0.Ma) is { Count: > 0 } dec)
        {
            _detail.Children.Add(Sec("Giải mã (Tổng hợp)"));
            var dg = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
            for (var i = 0; i < dec.Count; i++)
            {
                dg.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var (lb, code, name) = dec[i];
                var a = new TextBlock { Text = lb, FontSize = 11.5, Classes = { "muted" }, Margin = new Thickness(0, 1, 8, 1) }; Grid.SetRow(a, i); dg.Children.Add(a);
                var c = new TextBlock { Text = code, FontSize = 12, FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 1, 8, 1) }; Grid.SetRow(c, i); Grid.SetColumn(c, 1); dg.Children.Add(c);
                var n = new TextBlock { Text = name ?? "Chưa khai báo", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1) }; if (name is null) n.Classes.Add("muted"); Grid.SetRow(n, i); Grid.SetColumn(n, 2); dg.Children.Add(n);
            }
            _detail.Children.Add(dg);
        }
        var hds = rows.Select(r => r.Hd).Where(h => h.Length > 0).Distinct().ToList();
        if (hds.Count > 0) { _detail.Children.Add(Sec("Hợp đồng")); _detail.Children.Add(new TextBlock { Text = string.Join(" · ", hds), TextWrapping = TextWrapping.Wrap, FontSize = 12.5 }); }

        var btns = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var s in by.Select(b => b.s).Where(s => _mods.Any(m => m.Key == s.Menu)).DistinctBy(s => s.Menu))
        {
            var b = new Button { Content = "Mở " + _mods.First(m => m.Key == s.Menu).Label, Classes = { "primary" }, Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 6, 6) };
            ToolTip.SetTip(b, "Mã đã được sao chép — dán (Ctrl + V) vào ô tìm của module");
            b.Click += async (_, _) => { await Copy(r0.Ma); Go(s.Menu); };
            btns.Children.Add(b);
        }
        var cp = new Button { Content = "Sao chép mã", Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 6, 6) };
        cp.Click += async (_, _) => { await Copy(r0.Ma); _status.Text = "Đã sao chép " + r0.Ma; };
        btns.Children.Add(cp);
        _detail.Children.Add(btns);
    }

    private static TextBlock Sec(string t) => new() { Text = t, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = Dark, Margin = new Thickness(0, 6, 0, 0) };
    private async Task Copy(string s) { try { if (Clipboard is { } c) await c.SetTextAsync(s); } catch { /* không có clipboard */ } }
}

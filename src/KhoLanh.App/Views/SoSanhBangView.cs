using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using KhoLanh.App.Controls;
using KhoLanh.Core.Bang;
using KhoLanh.Core.Grid;

namespace KhoLanh.App.Views;

/// <summary>
/// Công cụ "So sánh &amp; Tra cứu bảng" (M22 của app cũ, C#): nạp 2 bảng Excel / CSV, chọn cột khóa →
/// (1) So sánh: giống / khác số liệu theo cột / chỉ có ở A / chỉ có ở B / mã trùng — xem 2 bảng song song;
/// (2) Tra cứu kiểu VLOOKUP: kéo cột của B sang A. Xuất Excel. Lõi: <see cref="CcBang"/> (khớp cc-bang-engine.js).
/// </summary>
public sealed class SoSanhBangView : UserControl, IPage
{
    public string Title => "So sánh & Tra cứu bảng";
    public string Subtitle => "Nạp 2 bảng Excel / CSV, chọn cột khóa → so sánh khác biệt hoặc kéo cột từ bảng B sang bảng A (kiểu VLOOKUP).";

    private static IBrush B(string c) => new SolidColorBrush(Color.Parse(c));
    private static readonly IBrush Dark = B("#0B2A45"), Line_ = B("#D0D7E2"), Diff = B("#FFE9A8"), Miss = B("#ECEFF3"), OkBg = B("#E8F5E9"), DiffBg = B("#FFF6D6"), OnlyBg = B("#FDE7E7"), PullBg = B("#E3F0FF");

    private sealed class Side
    {
        public string Label = ""; public string? File; public List<ExcelOps.Sheet>? Sheets; public CcTable? Tbl; public int Hr;
        public readonly ComboBox Sheet = new() { MinWidth = 150 };
        public readonly TextBox HeadRow = new() { Width = 50, Text = "1" };
        public readonly TextBlock Info = new() { FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        public readonly WrapPanel Keys = new();
    }
    private readonly Side _a = new() { Label = "A" }, _b = new() { Label = "B" };
    private readonly CheckBox _case = new() { Content = "Không phân biệt HOA / thường", IsChecked = true }, _dia = new() { Content = "Bỏ qua dấu tiếng Việt" };
    private readonly TextBox _tol = new() { Text = "0", Width = 70 };
    private readonly TextBlock _msg = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _pairs = new(), _pull = new();
    private readonly ComboBox _dup = new() { ItemsSource = new[] { "Lấy dòng đầu tiên", "Lấy dòng cuối cùng", "Cộng các số", "Nối các giá trị (; )", "Đếm số dòng" }, SelectedIndex = 0, MinWidth = 180 };
    private readonly Button _btnCmp = new() { Content = "So sánh", Classes = { "primary" }, IsEnabled = false }, _btnLkp = new() { Content = "Tra cứu", Classes = { "primary" }, IsEnabled = false };
    private readonly Button _cmpDl = new() { Content = "Xuất Excel…", IsEnabled = false }, _lkpDl = new() { Content = "Xuất Excel…", IsEnabled = false };
    private readonly TextBlock _cmpStats = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap }, _lkpStats = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _views = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly GridModel _cm = new() { ReadOnly = true }, _lm = new() { ReadOnly = true };
    private readonly SheetGrid _cg, _lg;

    private List<CcBang.Pair> _pairList = new(), _cmpSel = new();
    private CcBang.CompareResult? _cmp; private CcBang.LookupResult? _lkp;
    private List<Sbs> _sbs = new(); private List<Sbs> _shown = new(); private string _view = "all";
    private int _lkpBase;
    private sealed record Sbs(string St, int RowA, int RowB, HashSet<int> DA, HashSet<int> DB);

    public SoSanhBangView()
    {
        _cg = new SheetGrid(_cm) { SpareRows = 0, CellBackground = CmpBg };
        _lg = new SheetGrid(_lm) { SpareRows = 0, CellBackground = (r, c) => _lkp is not null && c >= _lkpBase ? PullBg : null };

        var sides = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        sides.Children.Add(SidePanel(_a)); var pb = SidePanel(_b); Grid.SetColumn(pb, 2); sides.Children.Add(pb);

        var opts = Row(_case, _dia, Lb("Dung sai số"), _tol);

        _btnCmp.Click += (_, _) => RunCompare(); _btnLkp.Click += (_, _) => RunLookup();
        _cmpDl.Click += async (_, _) => await ExportCmp(); _lkpDl.Click += async (_, _) => await ExportLkp();

        var cmpTop = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 6) };
        cmpTop.Children.Add(Lb("Cột so số liệu (các cột trùng tên ở 2 bảng):")); cmpTop.Children.Add(_pairs);
        cmpTop.Children.Add(Row(_btnCmp, _cmpDl, _cmpStats)); cmpTop.Children.Add(_views);
        var cmpTab = new DockPanel(); DockPanel.SetDock(cmpTop, Dock.Top); cmpTab.Children.Add(cmpTop); cmpTab.Children.Add(Framed(_cg));

        var lkpTop = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 6) };
        lkpTop.Children.Add(Lb("Cột của bảng B cần kéo sang bảng A:")); lkpTop.Children.Add(_pull);
        lkpTop.Children.Add(Row(Lb("Mã có nhiều dòng ở B"), _dup, _btnLkp, _lkpDl, _lkpStats));
        var lkpTab = new DockPanel(); DockPanel.SetDock(lkpTop, Dock.Top); lkpTab.Children.Add(lkpTop); lkpTab.Children.Add(Framed(_lg));

        var tabs = new TabControl { ItemsSource = new[] { new TabItem { Header = "So sánh 2 bảng", Content = cmpTab }, new TabItem { Header = "Tra cứu (kéo cột B → A)", Content = lkpTab } } };

        var top = new StackPanel { Spacing = 8 }; top.Children.Add(sides); top.Children.Add(opts); top.Children.Add(_msg);
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(tabs);
        Content = root;
        Refresh();
    }

    // ------------------------------------------------------------ dựng giao diện
    private static TextBlock Lb(string t) => new() { Text = t, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private static StackPanel Row(params Control[] c) { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var x in c) { x.VerticalAlignment = VerticalAlignment.Center; s.Children.Add(x); } return s; }
    private static Border Framed(Control c) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = c };
    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);

    private Control SidePanel(Side s)
    {
        var pick = new Button { Content = $"Chọn bảng {s.Label}…" };
        pick.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this); if (top is null) return;
            var f = (await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = $"Chọn bảng {s.Label}", FileTypeFilter = new[] { new FilePickerFileType("Excel / CSV") { Patterns = new[] { "*.xlsx", "*.xlsm", "*.xls", "*.csv" } } } })).FirstOrDefault()?.TryGetLocalPath();
            if (f is null) return;
            try { var sh = await Task.Run(() => ExcelOps.Read(f)); SetSide(s == _a ? 'A' : 'B', f, sh); }
            catch (Exception e) { s.Info.Text = e.Message; s.Tbl = null; Refresh(); }
        };
        s.Sheet.SelectionChanged += (_, _) => { if (s.Sheets is not null && s.Sheet.SelectedIndex >= 0) { s.HeadRow.Text = (CcBang.GuessHeader(s.Sheets[s.Sheet.SelectedIndex].Aoa) + 1).ToString(); Build(s); AutoKeys(); } };
        s.HeadRow.LostFocus += (_, _) => { if (s.Sheets is not null) { Build(s); AutoKeys(); } };
        var st = new StackPanel { Spacing = 6 };
        st.Children.Add(new TextBlock { Text = "Bảng " + s.Label, FontWeight = FontWeight.Bold, Foreground = Dark, FontSize = 14 });
        st.Children.Add(Row(pick, Lb("Sheet"), s.Sheet, Lb("Dòng tiêu đề"), s.HeadRow));
        st.Children.Add(s.Info);
        st.Children.Add(Lb("Cột khóa (mã để ghép dòng):"));
        st.Children.Add(new ScrollViewer { MaxHeight = 74, Content = s.Keys });
        return new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8), Child = st };
    }

    /// <summary>Nạp bảng cho một phía (dùng khi chọn file, và cho công cụ chụp màn hình).</summary>
    public void SetSide(char side, string file, List<ExcelOps.Sheet> sheets)
    {
        var s = side == 'A' ? _a : _b; s.File = file; s.Sheets = sheets;
        s.Sheet.ItemsSource = sheets.Select(x => x.Name).ToList(); s.Sheet.SelectedIndex = -1; s.Sheet.SelectedIndex = 0;
    }

    private void Build(Side s)
    {
        if (s.Sheets is null || s.Sheet.SelectedIndex < 0) return;
        s.Hr = Math.Max(0, (int.TryParse(s.HeadRow.Text, out var h) ? h : 1) - 1);
        s.Tbl = ExcelOps.ToTable(s.Sheets[s.Sheet.SelectedIndex], s.Hr);
        s.Info.Text = $"{Path.GetFileName(s.File)} · {s.Tbl.Rows.Count} dòng, {s.Tbl.Head.Count} cột";
        Checks(s.Keys, s.Tbl.Head, _ => false, Refresh);
    }

    private static void Checks(WrapPanel p, IEnumerable<string> names, Func<int, bool> on, Action? changed)
    {
        p.Children.Clear(); var i = 0;
        foreach (var n in names) { var cb = new CheckBox { Content = n, Tag = i, IsChecked = on(i), Margin = new Thickness(0, 0, 12, 0), FontSize = 12.5 }; if (changed is not null) cb.IsCheckedChanged += (_, _) => changed(); p.Children.Add(cb); i++; }
    }
    private static List<int> Picked(WrapPanel p) => p.Children.OfType<CheckBox>().Where(c => c.IsChecked == true && c.IsEnabled).Select(c => (int)c.Tag!).ToList();

    private void AutoKeys()
    {
        if (_a.Tbl is null || _b.Tbl is null || Picked(_a.Keys).Count > 0 || Picked(_b.Keys).Count > 0) { Refresh(); return; }
        var lb = _b.Tbl.Head.Select(h => h.ToLowerInvariant()).ToList();
        var i = _a.Tbl.Head.FindIndex(h => lb.Contains(h.ToLowerInvariant()));
        if (i >= 0) { var j = lb.IndexOf(_a.Tbl.Head[i].ToLowerInvariant()); ((CheckBox)_a.Keys.Children[i]).IsChecked = true; ((CheckBox)_b.Keys.Children[j]).IsChecked = true; }
        Refresh();
    }

    private bool _refreshing;
    private void Refresh()
    {
        if (_refreshing) return; _refreshing = true;
        try
        {
            if (_a.Tbl is null || _b.Tbl is null) { _btnCmp.IsEnabled = _btnLkp.IsEnabled = false; _msg.Text = "Chọn file cho cả bảng A và bảng B."; _msg.Foreground = Dark; return; }
            var kA = Picked(_a.Keys); var kB = Picked(_b.Keys);
            var lb = _b.Tbl.Head.Select(h => h.ToLowerInvariant()).ToList();
            _pairList = new();
            for (var i = 0; i < _a.Tbl.Head.Count; i++)
            {
                var j = lb.IndexOf(_a.Tbl.Head[i].ToLowerInvariant());
                if (j >= 0 && !kA.Contains(i) && !kB.Contains(j) && _pairList.All(p => p.B != j)) _pairList.Add(new CcBang.Pair(i, j, _a.Tbl.Head[i]));
            }
            Checks(_pairs, _pairList.Select(p => p.Name), _ => true, null);
            if (_pairList.Count == 0) _pairs.Children.Add(new TextBlock { Text = "Hai bảng không có cột nào trùng tên (ngoài cột khóa) — chỉ so được việc có / không có mã.", Classes = { "muted" }, FontSize = 12 });
            Checks(_pull, _b.Tbl.Head.Select((h, j) => h + (kB.Contains(j) ? " (khóa)" : "")), _ => false, null);
            foreach (var cb in _pull.Children.OfType<CheckBox>()) if (kB.Contains((int)cb.Tag!)) cb.IsEnabled = false;
            var ok = kA.Count > 0 && kA.Count == kB.Count; _btnCmp.IsEnabled = _btnLkp.IsEnabled = ok;
            _msg.Foreground = B("#8A5300");
            _msg.Text = kA.Count == 0 || kB.Count == 0 ? "Chọn cột khóa ở cả hai bảng." : kA.Count != kB.Count ? $"Số cột khóa của A ({kA.Count}) và B ({kB.Count}) phải bằng nhau." : "";
        }
        finally { _refreshing = false; }
    }

    private CcBang.MatchOpts Opts() => new() { IgnoreCase = _case.IsChecked == true, IgnoreDiacritics = _dia.IsChecked == true, Tol = Ui.TryParseNumber(_tol.Text, out var t) ? Math.Abs(t) : 0 };
    private int RowNo(Side s, int i) => s.Hr + 2 + i;
    private static string Txt(object? v) => CcBang.Str(v);

    // ------------------------------------------------------------ SO SÁNH
    public void RunCompare()
    {
        if (_a.Tbl is null || _b.Tbl is null) return;
        var kA = Picked(_a.Keys); var kB = Picked(_b.Keys);
        _cmpSel = Picked(_pairs).Where(i => i < _pairList.Count).Select(i => _pairList[i]).ToList();
        var r = _cmp = CcBang.CompareTables(_a.Tbl, _b.Tbl, kA, kB, _cmpSel, Opts());
        _sbs = BuildSbs(r);
        var nDup = r.DupA.Count + r.DupB.Count;
        _cmpStats.Text = $"✔ {r.Same.Count} giống nhau · ≠ {r.Diff.Count} mã có số liệu khác · {r.OnlyA.Count} chỉ có ở A · {r.OnlyB.Count} chỉ có ở B" + (nDup > 0 ? $" · {nDup} mã trùng" : "");
        _views.Children.Clear();
        var views = new (string k, string t)[] { ("all", $"Tất cả ({_sbs.Count})"), ("same", $"Giống nhau ({r.Same.Count})"), ("diff", $"Khác nhau ({r.Diff.Count})"), ("onlyA", $"Chỉ có ở A ({r.OnlyA.Count})"), ("onlyB", $"Chỉ có ở B ({r.OnlyB.Count})"), ("dup", $"Mã trùng ({nDup})") };
        foreach (var (k, t) in views) { var b = new ToggleButton2(t, k); b.Click += (_, _) => ShowCmp(k); _views.Children.Add(b); }
        ShowCmp(r.Diff.Count > 0 ? "diff" : r.OnlyA.Count > 0 ? "onlyA" : r.OnlyB.Count > 0 ? "onlyB" : "all");
        _msg.Foreground = r.Diff.Count == 0 && r.OnlyA.Count == 0 && r.OnlyB.Count == 0 ? B("#1B5E20") : Dark;
        _msg.Text = r.Diff.Count == 0 && r.OnlyA.Count == 0 && r.OnlyB.Count == 0 ? "Hai bảng KHỚP nhau theo các cột đã chọn." + (nDup > 0 ? " Lưu ý có mã trùng." : "") : "";
        if (r.EmptyKeyA.Count + r.EmptyKeyB.Count > 0) { _msg.Foreground = B("#8A5300"); _msg.Text = $"Bỏ qua dòng có khóa trống: A {r.EmptyKeyA.Count} dòng, B {r.EmptyKeyB.Count} dòng."; }
        _cmpDl.IsEnabled = true;
    }

    private sealed class ToggleButton2 : Button
    {
        protected override Type StyleKeyOverride => typeof(Button);
        public string Key { get; }
        public ToggleButton2(string text, string key) { Content = text; Key = key; Padding = new Thickness(10, 3); FontSize = 12.5; }
    }

    /// <summary>2 bảng song song, dòng căn theo khóa (giống buildSbs của app cũ): dòng chỉ có ở B chèn sau dòng khớp gần nhất theo thứ tự B.</summary>
    private List<Sbs> BuildSbs(CcBang.CompareResult r)
    {
        var ent = new SortedDictionary<int, Sbs>(); var none = new HashSet<int>();
        foreach (var (ra, rb) in r.Same) ent[ra] = new Sbs("same", ra, rb, none, none);
        foreach (var d in r.Diff)
        {
            var sel = d.Cells.Select(c => _cmpSel.FirstOrDefault(p => p.Name == c.Name)).Where(p => p is not null).ToList();
            ent[d.RowA] = new Sbs("diff", d.RowA, d.RowB, sel.Select(p => p!.A).ToHashSet(), sel.Select(p => p!.B).ToHashSet());
        }
        foreach (var i in r.OnlyA) ent[i] = new Sbs("onlyA", i, -1, none, none);
        var outp = ent.Values.ToList();
        foreach (var j in r.OnlyB.OrderBy(x => x))
        {
            int pos = -1, best = -1;
            for (var k = 0; k < outp.Count; k++) if (outp[k].RowB >= 0 && outp[k].St != "onlyB" && outp[k].RowB < j && outp[k].RowB > best) { best = outp[k].RowB; pos = k; }
            var at = pos + 1; while (at < outp.Count && outp[at].St == "onlyB" && outp[at].RowB < j) at++;
            outp.Insert(at, new Sbs("onlyB", -1, j, none, none));
        }
        return outp;
    }

    private static readonly Dictionary<string, string> StName = new() { ["same"] = "Giống", ["diff"] = "Khác", ["onlyA"] = "Chỉ A", ["onlyB"] = "Chỉ B" };
    public void ShowCmp(string v)
    {
        _view = v;
        foreach (var b in _views.Children.OfType<ToggleButton2>()) { if (b.Key == v) b.Classes.Add("primary"); else b.Classes.Remove("primary"); }
        if (_cmp is null || _a.Tbl is null || _b.Tbl is null) return;
        if (v == "dup")
        {
            var d = DupTable(); _shown = new();
            _cm.Load(d.Head.Select((h, i) => new GridColumn("c" + i, h, i == 3 ? 320 : 130, "text") { ReadOnly = true }), d.Rows.Select(r => r.Select(Txt)));
            _cg.Refresh(); return;
        }
        _shown = v == "all" ? _sbs : _sbs.Where(x => x.St == v).ToList();
        var A = _a.Tbl; var Bt = _b.Tbl;
        var cols = new List<GridColumn> { new("st", "", 58) { ReadOnly = true }, new("ra", "Dòng A", 60, "number") { ReadOnly = true } };
        cols.AddRange(A.Head.Select((h, i) => new GridColumn("a" + i, "A · " + h, 120) { ReadOnly = true }));
        cols.Add(new GridColumn("rb", "Dòng B", 60, "number") { ReadOnly = true });
        cols.AddRange(Bt.Head.Select((h, i) => new GridColumn("b" + i, "B · " + h, 120) { ReadOnly = true }));
        _cm.Load(cols, _shown.Select(x =>
        {
            var row = new List<string> { StName[x.St], x.RowA >= 0 ? RowNo(_a, x.RowA).ToString() : "" };
            row.AddRange(Enumerable.Range(0, A.Head.Count).Select(c => x.RowA >= 0 && c < A.Rows[x.RowA].Length ? Txt(A.Rows[x.RowA][c]) : ""));
            row.Add(x.RowB >= 0 ? RowNo(_b, x.RowB).ToString() : "");
            row.AddRange(Enumerable.Range(0, Bt.Head.Count).Select(c => x.RowB >= 0 && c < Bt.Rows[x.RowB].Length ? Txt(Bt.Rows[x.RowB][c]) : ""));
            return row;
        }));
        _cg.Refresh();
    }

    private IBrush? CmpBg(int r, int c)
    {
        if (_view == "dup" || r >= _shown.Count || _a.Tbl is null) return null;
        var x = _shown[r]; var na = _a.Tbl.Head.Count;
        if (c == 0) return x.St switch { "same" => OkBg, "diff" => DiffBg, _ => OnlyBg };
        var isA = c >= 1 && c <= na + 1; var isB = c > na + 1;
        if (x.St == "onlyA" && isB || x.St == "onlyB" && isA) return Miss;
        if (x.St == "diff" && (isA && c >= 2 && x.DA.Contains(c - 2) || isB && c >= na + 3 && x.DB.Contains(c - na - 3))) return Diff;
        return null;
    }

    private CcTable DupTable()
    {
        var r = _cmp!; var rows = new List<object?[]>();
        rows.AddRange(r.DupA.Select(d => new object?[] { "A", d.Key, (double)d.Rows.Count, string.Join(", ", d.Rows.Select(i => RowNo(_a, i))) }));
        rows.AddRange(r.DupB.Select(d => new object?[] { "B", d.Key, (double)d.Rows.Count, string.Join(", ", d.Rows.Select(i => RowNo(_b, i))) }));
        return new CcTable(new[] { "Bảng", "Mã (khóa)", "Số dòng", "Các dòng" }, rows);
    }

    private async Task ExportCmp()
    {
        if (_cmp is null || _a.Tbl is null || _b.Tbl is null) return; var r = _cmp; var A = _a.Tbl; var Bt = _b.Tbl; var kA = Picked(_a.Keys);
        var path = await Ui.SaveFile(this, "Lưu kết quả so sánh", $"so-sanh-bang-{Stamp()}.xlsx", "xlsx", "Excel"); if (path is null) return;
        var sum = new CcTable(new[] { "Mục", "Số lượng" }, new[]
        {
            new object?[] { "Bảng A", Path.GetFileName(_a.File) }, new object?[] { "Bảng B", Path.GetFileName(_b.File) },
            new object?[] { "Giống nhau", (double)r.Same.Count }, new object?[] { "Mã có số liệu khác", (double)r.Diff.Count },
            new object?[] { "Chỉ có ở A", (double)r.OnlyA.Count }, new object?[] { "Chỉ có ở B", (double)r.OnlyB.Count },
            new object?[] { "Mã trùng ở A", (double)r.DupA.Count }, new object?[] { "Mã trùng ở B", (double)r.DupB.Count },
        });
        var diff = new CcTable(new[] { "Mã (khóa)", "Cột", "Giá trị A", "Giá trị B", "Dòng A", "Dòng B" }, r.Diff.SelectMany(d => d.Cells.Select(c => new object?[] { d.Key, c.Name, c.A, c.B, (double)RowNo(_a, d.RowA), (double)RowNo(_b, d.RowB) })));
        var onlyA = new CcTable(new[] { "Dòng A" }.Concat(A.Head), r.OnlyA.Select(i => new object?[] { (double)RowNo(_a, i) }.Concat(A.Rows[i]).ToArray()));
        var onlyB = new CcTable(new[] { "Dòng B" }.Concat(Bt.Head), r.OnlyB.Select(i => new object?[] { (double)RowNo(_b, i) }.Concat(Bt.Rows[i]).ToArray()));
        var same = new CcTable(new[] { "Mã (khóa)", "Dòng A", "Dòng B" }, r.Same.Select(s => new object?[] { string.Join(" | ", kA.Select(c => Txt(c < A.Rows[s.RowA].Length ? A.Rows[s.RowA][c] : null))), (double)RowNo(_a, s.RowA), (double)RowNo(_b, s.RowB) }));
        try
        {
            await Task.Run(() => ExcelOps.Write(path, new[] { ("Tóm tắt", sum), ("Khác nhau", diff), ("Chỉ có ở A", onlyA), ("Chỉ có ở B", onlyB), ("Mã trùng", DupTable()), ("Giống nhau", same) }));
            Ui.OpenExternal(path);
        }
        catch (Exception e) { await Ui.Message(this, e.Message, "Không lưu được"); }
    }

    // ------------------------------------------------------------ TRA CỨU
    private static readonly string[] DupKeys = { "first", "last", "sum", "join", "count" };
    public void RunLookup(IList<int>? pull = null)
    {
        if (_a.Tbl is null || _b.Tbl is null) return;
        pull ??= Picked(_pull);
        if (pull.Count == 0) { _msg.Foreground = B("#8A5300"); _msg.Text = "Chọn ít nhất một cột của bảng B cần kéo sang."; return; }
        var r = _lkp = CcBang.LookupTables(_a.Tbl, _b.Tbl, Picked(_a.Keys), Picked(_b.Keys), pull, DupKeys[Math.Max(0, _dup.SelectedIndex)], Opts());
        _lkpBase = _a.Tbl.Head.Count;
        _lkpStats.Text = $"✔ {r.Found} dòng A tìm thấy ở B · {r.NotFound} không tìm thấy" + (r.Multi > 0 ? $" · {r.Multi} mã có nhiều dòng ở B" : "") + (r.EmptyKey > 0 ? $" · {r.EmptyKey} dòng khóa trống" : "");
        _lm.Load(r.Table.Head.Select((h, i) => new GridColumn("c" + i, h, 120) { ReadOnly = true }), r.Table.Rows.Select(x => x.Select(Txt)));
        _lg.Refresh();
        _msg.Foreground = Dark; _msg.Text = r.Multi > 0 ? $"Có {r.Multi} mã xuất hiện nhiều dòng ở bảng B — đã xử lý theo lựa chọn \"{_dup.SelectedItem}\"." : "";
        _lkpDl.IsEnabled = true;
    }

    private async Task ExportLkp()
    {
        if (_lkp is null || _a.Tbl is null) return; var r = _lkp; var A = _a.Tbl;
        var path = await Ui.SaveFile(this, "Lưu kết quả tra cứu", $"tra-cuu-bang-{Stamp()}.xlsx", "xlsx", "Excel"); if (path is null) return;
        var sheets = new List<(string, CcTable)> { ("Kết quả tra cứu", r.Table) };
        if (r.Miss.Count > 0) sheets.Add(("Không tìm thấy", new CcTable(new[] { "Dòng A" }.Concat(A.Head), r.Miss.Select(i => new object?[] { (double)RowNo(_a, i) }.Concat(A.Rows[i]).ToArray()))));
        try { await Task.Run(() => ExcelOps.Write(path, sheets)); Ui.OpenExternal(path); }
        catch (Exception e) { await Ui.Message(this, e.Message, "Không lưu được"); }
    }
}

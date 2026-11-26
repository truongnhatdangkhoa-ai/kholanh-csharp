using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Modules;
using KhoLanh.Core.RaDong;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;

namespace KhoLanh.App.Views;

/// <summary>Module Báo cáo rã đông (C#): nạp file báo cáo kho → Bảng 2 gộp theo Ngày + Tên sản phẩm + Size (sửa tay được) → Bảng 3 lưu theo ngày → Excel năm / ảnh ngày.</summary>
public sealed class RaDongView : UserControl, IPage
{
    public string Title => "Báo cáo rã đông";
    public string Subtitle => "Nạp file Nhập xuất trong ngày, gộp theo Ngày + Tên sản phẩm + Size, lưu số liệu theo ngày, xuất Excel năm và ảnh từng ngày.";

    private static RaDongService Svc => App.Services.RaDong;
    private static readonly IBrush EditedBg = AppTheme.Brush("#FFF1C9");
    private static readonly IBrush AutoBg = AppTheme.Brush("#E3F0FF");
    private static readonly IBrush TotalBg = AppTheme.Brush("#EEF2F7");
    private static readonly IBrush DayBg = AppTheme.Brush("#DCEBDD");
    private static readonly IBrush HandBg = AppTheme.Brush("#FCE8E6");

    private readonly bool _edit;
    private bool _loading, _ready;
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

    // ---------- Bảng 2
    private readonly GridModel _gm = new(), _rm = new();
    private readonly SheetGrid _gGrid, _rGrid;
    private readonly TextBlock _b2Info = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _dayFilter = new() { MinWidth = 150 };
    private readonly ToggleButton _rawToggle = new() { Content = "Bảng chưa gộp (dòng gốc)", Padding = new Thickness(10, 4), FontSize = 12.5 };
    private readonly Button _undoBtn;
    private readonly Panel _gridHost = new Panel();
    private RaDongService.Groups _groups = new();
    private List<string?> _gRowKey = new();           // dòng lưới → khóa nhóm (null = dòng tổng)
    private HashSet<int> _gTotalRows = new(), _gDayRows = new();
    private List<string> _gCols = new();              // cột lưới → "__d","__p","__s","__n","__w", tên cột số
    private List<string[]> _gSnap = new();
    private List<Dictionary<string, JsonNode?>> _rRows = new();
    private List<string> _rCols = new();
    private HashSet<string> _rNum = new();
    private List<string[]> _rSnap = new();

    // ---------- Bảng 3
    private readonly GridModel _sm = new();
    private readonly SheetGrid _sGrid;
    private readonly ComboBox _year = new() { MinWidth = 90 }, _month = new() { MinWidth = 110 };
    private readonly TextBox _q = new() { Watermark = "Tìm mã hàng / size / ngày…", Width = 220 };
    private readonly TextBlock _sInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _undoUpd, _delMonth, _exportYear;
    private readonly Border _addBox = new() { IsVisible = false };
    private readonly TextBox _addDate = new() { Watermark = "dd/mm/yyyy", Width = 110 }, _addSize = new() { Watermark = "51/60", Width = 90 },
        _addSl = new() { Watermark = "0", Width = 90 }, _addNet = new() { Watermark = "trống = tự tính", Width = 130 };
    private readonly AutoCompleteBox _addMa = new() { Watermark = "TÔM GIA CÔNG", Width = 200, FilterMode = AutoCompleteFilterMode.ContainsOrdinal };
    private List<RaDongService.Saved?> _sRowItem = new(); // dòng lưới → dòng đã lưu (null = dòng tiêu đề ngày)
    private List<string> _sRowDay = new();
    private List<string[]> _sSnap = new();
    private string _selYear = "", _selMonth = "0";
    // 1.92: ngày thu gọn / mở ở tab Số liệu đã lưu (bấm dòng tổng ngày). Mặc định chỉ ngày mới nhất mở; đang tìm / lọc thì mở hết.
    private readonly Dictionary<string, bool> _dayOpen = new();
    private string _newestDay = "";
    private bool _openAll, _qOpen;
    private bool DayOpen(string d) => _openAll || _qOpen || (_dayOpen.TryGetValue(d, out var o) ? o : d == _newestDay);
    private bool IsDayHead(int r) => r >= 0 && r < _sRowItem.Count && _sRowItem[r] is null;
    private readonly HashSet<string> _imgSel = new();   // ngày đã tích để gộp ảnh (như app cũ: ô vuông đầu dòng ngày)
    private readonly Button _imgBtn, _imgClear;
    private const int PickCol = 6;                       // cột ẩn rộng 0 gắn ô tích đầu dòng

    public RaDongView()
    {
        _edit = Svc.CanEdit;
        _gGrid = new SheetGrid(_gm) { SpareRows = 0, Display = NumDisplay, CellBackground = GroupBg };
        _rGrid = new SheetGrid(_rm) { SpareRows = 0, Display = NumDisplay };
        _sGrid = new SheetGrid(_sm) { SpareRows = 0, Display = NumDisplay, CellBackground = SavedBg, CheckCol = PickCol, CheckFill = _ => "1" };
        _gm.Changed += () => { if (!_loading) OnGroupGridChanged(); };
        _rm.Changed += () => { if (!_loading) OnRawGridChanged(); };
        _sm.Changed += () => { if (!_loading) OnSavedGridChanged(); };
        _sGrid.Sortable = false;   // giữ thứ tự ngày (mới → cũ) + dòng tổng ngày đứng trên
        _sGrid.TotalsKeepRow = r => IsDayHead(r) && !DayOpen(_sRowDay[r]);   // ngày đang thu: cộng số của dòng tổng ngày
        _sGrid.RowClicked = r => { if (IsDayHead(r)) { var d = _sRowDay[r]; _dayOpen[d] = !DayOpen(d); ShowBang3(); } };
        _sGrid.FilterChanged += () => { if (_openAll == _sGrid.IsFiltered) return; _openAll = _sGrid.IsFiltered; ShowBang3(); };
        _undoBtn = Btn("↶ Hoàn tác", async () => await Act(() => { var l = Svc.UndoBang2(); if (l is not null) _status.Text = "Đã hoàn tác: " + l; }, b2: true));
        _undoUpd = Btn("↶ Hoàn tác cập nhật", async () => await UndoUpdate());
        _delMonth = Btn("Xóa tháng", async () => await DeleteMonth());
        _exportYear = Btn("Xuất Excel năm…", async () => await ExportYear(), primary: true);
        _imgBtn = Btn("🖼 Ảnh ngày đã chọn…", async () => await DayImage());
        _imgClear = Btn("Bỏ tích", () => { _imgSel.Clear(); ShowBang3(); });

        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Số liệu nạp (gộp theo ngày)", Content = BuildBang2() },
                new TabItem { Header = "Số liệu đã lưu theo ngày", Content = BuildBang3() },
            },
        };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_status, Dock.Right);
        top.Children.Add(_status);
        top.Children.Add(new TextBlock
        {
            Text = _edit ? "Mỗi thao tác lưu ngay. Tên sản phẩm lấy từ Mã hóa sản phẩm + Ánh xạ tên hàng của module Tổng hợp." : "Chỉ xem (tài khoản không có quyền sửa Báo cáo rã đông).",
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        });
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(tabs);
        Content = root;
        AttachedToVisualTree += async (_, _) => { if (!Ui.SkipReload(this)) await LoadAsync(); };   // 1.136: quay lại trang dữ liệu chưa đổi → không đọc lại
    }

    private static Button Btn(string text, Action click, bool primary = false, bool danger = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (danger) b.Classes.Add("danger");
        b.Click += (_, _) => click();
        return b;
    }

    private static string NumDisplay(int r, int c, string raw) =>
        raw.Length is > 0 and < 20 && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && raw.IndexOfAny(new[] { '/', ':' }) < 0 ? d.ToString("#,0.##", Ui.Vi) : raw;
    private static string Inv(double d) => Math.Round(d, 6).ToString(CultureInfo.InvariantCulture);

    private async Task LoadAsync()
    {
        _status.Text = "Đang đọc dữ liệu…";
        _ready = false;
        try { await Task.Run(Svc.Load); }
        catch (Exception ex)
        {
            _status.Text = "Không đọc được dữ liệu — tạm khóa việc ghi.";
            await Ui.Message(this, (ex is BusinessException ? ex.Message : Ui.Describe(ex)) + "\n\nTạm khóa việc ghi để không mất số liệu cũ. Bấm \"Tải lại\" ở tab Số liệu đã lưu để thử lại.");
            return;
        }
        _ready = true;
        _status.Text = "";
        ShowBang2();
        ShowBang3();
    }

    /// <summary>Chạy thao tác ghi; máy khác vừa sửa → tải lại; xong vẽ lại bảng.</summary>
    private async Task<bool> Act(Action a, bool b2 = false, bool b3 = false)
    {
        try
        {
            a();
            if (b2) ShowBang2();
            if (b3) ShowBang3();
            return true;
        }
        catch (SheetConflictException)
        {
            await Ui.Message(this, "Dữ liệu Báo cáo rã đông vừa được máy khác sửa nên thao tác này CHƯA được lưu (để không ghi đè). Bấm Đóng để tải bản mới nhất rồi làm lại.", "Máy khác vừa sửa");
            await LoadAsync();
        }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); }
        if (b2) ShowBang2();
        if (b3) ShowBang3();
        return false;
    }

    // =====================================================================================
    // Bảng 2
    // =====================================================================================

    private Control BuildBang2()
    {
        var bar = new ToolRow { Margin = new Thickness(0, 0, 0, 6) };
        if (_edit)
        {
            bar.Children.Add(Btn("⬆ Nạp file Excel…", async () => await ImportFile(update: false)));
            bar.Children.Add(ToolRow.More(Btn("Xóa dữ liệu đã nhập", async () =>
            {
                if (Svc.Bang1.Rows.Count == 0) return;
                if (!await Ui.Confirm(this, "Xóa toàn bộ dữ liệu đã nạp và bảng gộp? (Số liệu đã lưu theo ngày KHÔNG bị xóa.) Có thể bấm \"Hoàn tác\".", "Xóa dữ liệu", "Xóa")) return;
                await Act(Svc.ClearBang1, b2: true);
            })));
        }
        bar.Children.Add(ToolRow.More(Btn("Làm mới ánh xạ", async () => { await Act(Svc.LoadMapping, b2: true); _status.Text = "Đã đọc lại Mã hóa sản phẩm + Ánh xạ tên hàng."; })));
        bar.Children.Add(new TextBlock { Text = "Ngày", VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Margin = new Thickness(6, 0, 0, 0) });
        bar.Children.Add(_dayFilter);
        _dayFilter.SelectionChanged += (_, _) => { if (!_loading) RenderGroups(); };
        // "Bảng chưa gộp (dòng gốc)" bật / tắt → nút gạt Đã gộp | Dòng gốc
        var rawSeg = new Seg("Đã gộp", "Dòng gốc");
        ToolTip.SetTip(rawSeg, "Đã gộp: bảng gộp theo ngày (sửa tên / size / số). Dòng gốc: sửa / xóa từng dòng của file đã nạp.");
        rawSeg.Changed += i => _rawToggle.IsChecked = i == 1;
        bar.Children.Add(rawSeg);
        _rawToggle.IsCheckedChanged += (_, _) => { rawSeg.Selected = _rawToggle.IsChecked == true ? 1 : 0; ShowBang2(); };
        bar.Children.Add(ToolRow.Gap());
        if (_edit)
        {
            bar.Children.Add(Btn("🗑 Xóa dòng đã chọn", async () => await DeleteSelected(), danger: true));
            bar.Children.Add(ToolRow.More(Btn("↺ Hoàn lại số gốc", async () =>
            {
                var keys = SelectedGroupKeys();
                if (keys.Count == 0) { await Ui.Message(this, "Chọn (bôi đen) các dòng nhóm có ô vàng (đã sửa tay) rồi bấm lại."); return; }
                await Act(() => Svc.RevertGroups(keys), b2: true);
            })));
            bar.Children.Add(_undoBtn);
            bar.Children.Add(Btn("Lưu vào Số liệu đã lưu…", async () => await SaveToBang3(), primary: true));
        }
        var hint = new TextBlock
        {
            Text = "Bảng gộp: sửa thẳng ô Tên sản phẩm / Size / Trọng lượng / số (ô vàng = đã sửa tay, ô xanh = Net app tự tính = Số lượng × Trọng lượng). Bảng chưa gộp: sửa / xóa từng dòng gốc, Bảng gộp tự tính lại.",
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        };
        var head = new StackPanel();
        head.Children.Add(bar); head.Children.Add(_b2Info); head.Children.Add(hint);
        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        root.Children.Add(_gridHost);
        return root;
    }

    private void ShowBang2()
    {
        if (!_ready) return;
        var raw = _rawToggle.IsChecked == true;
        _gridHost.Children.Clear();
        _gridHost.Children.Add(raw ? _rGrid : _gGrid);
        _groups = Svc.Compute();
        var b = Svc.Bang1;
        _b2Info.Text = b.Rows.Count == 0
            ? "Chưa có dữ liệu. Bấm \"Nạp file Excel…\" và chọn file báo cáo kho (Nhập xuất trong ngày). App tự tìm dòng tiêu đề có cột \"Mã hàng\" và bỏ tiền tố \"TPC-\"."
            : "";
        _b2Info.IsVisible = b.Rows.Count == 0;
        _gGrid.Status = _rGrid.Status = b.Rows.Count == 0 ? "" :
            $"{(b.FileName.Length > 0 ? b.FileName + " · " : "")}{b.Rows.Count:#,0} dòng gốc · {_groups.Order.Count:#,0} nhóm" + (b.Edits.Count > 0 ? $" · {b.Edits.Count} nhóm sửa tay" : "");
        _undoBtn.IsEnabled = Svc.UndoLabel is not null;
        // bộ lọc ngày (mới nhất trước)
        _loading = true;
        var prev = _dayFilter.SelectedItem as string;
        var dates = _groups.Order.Select(k => _groups.ByKey[k].Date).Distinct().Reverse().ToList();
        var items = new List<string> { "Tất cả ngày" };
        items.AddRange(dates.Where(d => d.Length > 0));
        _dayFilter.ItemsSource = items;
        _dayFilter.SelectedIndex = prev is not null && items.Contains(prev) ? items.IndexOf(prev) : 0;
        _dayFilter.IsVisible = _groups.HasDate;
        _loading = false;
        if (raw) RenderRaw(); else RenderGroups();
    }

    private void RenderGroups()
    {
        var g = _groups;
        var day = _dayFilter.SelectedIndex > 0 ? _dayFilter.SelectedItem as string : null;
        var cols = new List<GridColumn>(); _gCols = new();
        void Col(string id, GridColumn c) { cols.Add(c); _gCols.Add(id); }
        if (g.HasDate) Col("__d", new GridColumn("d", "Ngày", 100) { ReadOnly = true });
        Col("__p", new GridColumn("p", "Tên sản phẩm", 220) { ReadOnly = !_edit });
        Col("__s", new GridColumn("s", "Size", 90) { ReadOnly = !_edit });
        Col("__n", new GridColumn("n", "Số dòng", 70, "number") { ReadOnly = true });
        if (g.WeightCol.Length > 0) Col("__w", new GridColumn("w", "Trọng lượng", 130) { ReadOnly = !_edit });
        foreach (var c in g.NumericCols) Col(c, new GridColumn("x" + _gCols.Count, "Tổng " + c, 120, "number") { ReadOnly = !_edit });
        var rows = new List<string[]>(); _gRowKey = new(); _gTotalRows = new(); _gDayRows = new();
        string[] Total(string label, string date, IEnumerable<RaDongService.Group> list)
        {
            var l = list.ToList();
            return _gCols.Select(id => id switch
            {
                "__d" => date, "__p" => label, "__s" or "__w" => "", "__n" => l.Sum(x => x.Count).ToString(),
                _ => Inv(l.Sum(x => x.Sums[id])),
            }).ToArray();
        }
        var byDate = g.Order.Select(k => g.ByKey[k]).GroupBy(x => x.Date).ToList();
        // ngày mới nhất ở trên cùng; dòng không có ngày xếp cuối
        var ordered = byDate.Where(d => d.Key.Length > 0).Reverse().Concat(byDate.Where(d => d.Key.Length == 0)).ToList();
        foreach (var dg in ordered)
        {
            if (day is not null && dg.Key != day) continue;
            foreach (var x in dg)
            {
                rows.Add(_gCols.Select(id => id switch
                {
                    "__d" => x.Date, "__p" => x.Product, "__s" => x.SizeText(Svc), "__n" => x.Count.ToString(),
                    "__w" => string.Join(" • ", x.WeightValues), _ => Inv(x.Sums[id]),
                }).ToArray());
                _gRowKey.Add(x.Key);
            }
            if (g.HasDate) { _gDayRows.Add(rows.Count); rows.Add(Total("Tổng ngày " + dg.Key, dg.Key, dg)); _gRowKey.Add(null); }
        }
        if (g.Order.Count > 0 && day is null)
        {
            _gTotalRows.Add(rows.Count);
            rows.Add(Total("Tổng cộng", "", g.Order.Select(k => g.ByKey[k])));
            _gRowKey.Add(null);
        }
        _loading = true;
        _gm.ReadOnly = !_edit;
        _gm.Load(cols, rows);
        _gSnap = _gm.Snapshot();
        _loading = false;
        _gGrid.Refresh();
    }

    private IBrush? GroupBg(int r, int c)
    {
        if (r >= _gRowKey.Count || c >= _gCols.Count) return null;
        if (_gTotalRows.Contains(r)) return TotalBg;
        if (_gDayRows.Contains(r)) return DayBg;
        var k = _gRowKey[r];
        if (k is null) return null;
        var id = _gCols[c];
        if (Svc.Bang1.Edits.TryGetValue(k, out var e))
        {
            if (id == "__p" && e.P is not null || id == "__s" && e.S is not null || id == "__w" && e.W is not null || e.V?.ContainsKey(id) == true) return EditedBg;
        }
        if (id == _groups.NetCol && _groups.ByKey.TryGetValue(k, out var gg) && gg.AutoNet > 0) return AutoBg;
        return null;
    }

    private async void OnGroupGridChanged()
    {
        var now = _gm.Snapshot();
        var changes = new List<(string key, string col, string val)>();
        var bad = false;
        for (var r = 0; r < now.Count; r++)
            for (var c = 0; c < now[r].Length; c++)
            {
                var before = r < _gSnap.Count && c < _gSnap[r].Length ? _gSnap[r][c] : "";
                if (now[r][c] == before) continue;
                var key = r < _gRowKey.Count ? _gRowKey[r] : null;
                if (key is null || _gCols[c] is "__d" or "__n") { bad = true; continue; }
                changes.Add((key, _gCols[c], now[r][c]));
            }
        if (changes.Count == 0) { if (bad) RenderGroups(); return; }
        await Act(() => { foreach (var (k, col, v) in changes) Svc.EditGroup(k, col, v); }, b2: true);
        if (bad) _status.Text = "Dòng tổng / cột Ngày, Số dòng không sửa được — đã bỏ qua.";
    }

    private List<string> SelectedGroupKeys()
    {
        var s = _gGrid.Selection;
        var keys = new List<string>();
        for (var r = s.R1; r <= s.R2 && r < _gRowKey.Count; r++) if (_gRowKey[r] is { } k && !keys.Contains(k)) keys.Add(k);
        return keys;
    }

    private async Task DeleteSelected()
    {
        if (_rawToggle.IsChecked == true)
        {
            var s = _rGrid.Selection;
            var rows = Enumerable.Range(s.R1, s.Rows).Where(r => r < _rRows.Count).Select(r => _rRows[r]).ToList();
            if (rows.Count == 0) return;
            if (!await Ui.Confirm(this, $"Xóa {rows.Count} dòng gốc đã chọn? Bảng gộp sẽ tự tính lại. Có thể bấm \"Hoàn tác\".", "Xóa dòng gốc", "Xóa")) return;
            await Act(() => Svc.DeleteRawRows(rows), b2: true);
            return;
        }
        var keys = SelectedGroupKeys();
        if (keys.Count == 0) { await Ui.Message(this, "Chọn (bôi đen) các dòng nhóm cần xóa rồi bấm lại."); return; }
        if (!await Ui.Confirm(this, $"Xóa {keys.Count} nhóm đã chọn? Xóa luôn các dòng gốc thuộc các nhóm này. Có thể bấm \"Hoàn tác\".", "Xóa nhóm", "Xóa")) return;
        await Act(() => Svc.DeleteGroups(keys), b2: true);
    }

    private void RenderRaw()
    {
        var b = Svc.Bang1;
        var H = b.Headers;
        var dc = TongHopService.FindDateColumn(H);
        // file nhiều cột → chỉ hiện các cột cần cho báo cáo
        if (H.Count <= 12) _rCols = H.ToList();
        else
        {
            _rCols = new();
            void Add(string h) { if (h.Length > 0 && H.Contains(h) && !_rCols.Contains(h)) _rCols.Add(h); }
            Add(dc); Add(b.CodeCol);
            foreach (var h in H) { var n = TongHopService.NormSearch(h); if (n is "ten hang" or "ten san pham" or "loai chung tu") Add(h); }
            Add(_groups.WeightCol); foreach (var c in _groups.NumericCols) Add(c);
        }
        _rNum = _groups.NumericCols.Concat(new[] { _groups.WeightCol }).Where(x => x.Length > 0).ToHashSet();
        int DKey(Dictionary<string, JsonNode?> r)
        {
            var m = System.Text.RegularExpressions.Regex.Match(TongHopService.FormatDate(r.GetValueOrDefault(dc)), @"^(\d{2})/(\d{2})/(\d{4})$");
            return m.Success ? int.Parse(m.Groups[3].Value) * 10000 + int.Parse(m.Groups[2].Value) * 100 + int.Parse(m.Groups[1].Value) : -1;
        }
        _rRows = b.Rows.Select((r, i) => (r, i, k: dc.Length > 0 ? DKey(r) : 0)).OrderByDescending(x => x.k).ThenBy(x => x.i).Select(x => x.r).ToList();
        var cols = _rCols.Select((h, i) => new GridColumn("c" + i, h, h == b.CodeCol ? 210 : FindTen(h) ? 240 : Math.Clamp(h.Length * 8 + 40, 80, 260), _rNum.Contains(h) ? "number" : "text") { ReadOnly = !_edit }).ToList();
        cols.Add(new GridColumn("p", "→ Tên sản phẩm", 200) { ReadOnly = true });
        cols.Add(new GridColumn("s", "→ Size", 80) { ReadOnly = true });
        var rows = _rRows.Select(r =>
        {
            var code = RaDongService.S(r.GetValueOrDefault("__code"));
            return _rCols.Select(h => h == dc ? TongHopService.FormatDate(r.GetValueOrDefault(h)) : r.GetValueOrDefault(h) is JsonValue jv && jv.TryGetValue<double>(out var d) ? Inv(d) : RaDongService.S(r.GetValueOrDefault(h)))
                .Concat(new[] { Svc.ProductLabel(code), Svc.FormatSize(RaDongService.SegmentRaw(code, 8, "—")) }).ToArray();
        }).ToList();
        _loading = true;
        _rm.ReadOnly = !_edit;
        _rm.Load(cols, rows);
        _rSnap = _rm.Snapshot();
        _loading = false;
        _rGrid.Refresh();
    }

    private static bool FindTen(string h) => TongHopService.NormSearch(h).Contains("ten hang");

    private async void OnRawGridChanged()
    {
        var now = _rm.Snapshot();
        var changes = new List<(Dictionary<string, JsonNode?> row, string col, string val, bool num)>();
        for (var r = 0; r < Math.Min(now.Count, _rRows.Count); r++)
            for (var c = 0; c < _rCols.Count; c++)
            {
                var before = r < _rSnap.Count ? _rSnap[r][c] : "";
                if (now[r][c] != before) changes.Add((_rRows[r], _rCols[c], now[r][c], _rNum.Contains(_rCols[c])));
            }
        if (changes.Count == 0) { RenderRaw(); return; }
        if (await Act(() => { foreach (var x in changes) Svc.EditRaw(x.row, x.col, x.val, x.num); }, b2: true))
            _status.Text = "Đã sửa — Bảng gộp tự tính lại.";
    }

    // ---------- nạp file

    /// <summary>File "Xuất rã đông" đã liên kết ở Đối chiếu hàng ngày (máy này, tài khoản này) — null nếu chưa liên kết.</summary>
    private static string? LinkedRaDongFile()
    {
        try { return App.Services.DoiChieu.LinkedPath("kho2"); } catch { return null; }
    }

    /// <summary>Cập nhật Bảng 3 từ file Xuất rã đông đang liên kết ở Đối chiếu hàng ngày (không phải chọn file).</summary>
    private async Task FromDoiChieu()
    {
        var path = LinkedRaDongFile();
        if (path is null) { await Ui.Message(this, "Chưa liên kết file “Xuất rã đông” ở module Đối chiếu hàng ngày (trên máy này, với tài khoản này). Liên kết ở đó trước, hoặc bấm “Nạp file cập nhật…” để chọn file."); return; }
        await ImportFile(update: true, path);
    }

    /// <summary>So các ngày trong file Xuất rã đông với Bảng 3: ngày chưa lưu, ngày Net khác nhau.</summary>
    private async Task CheckMissingDays()
    {
        if (!_ready) { await Ui.Message(this, "Chưa đọc được dữ liệu đã lưu. Bấm \"Tải lại\" rồi thử lại."); return; }
        var path = LinkedRaDongFile() ?? await Ui.OpenFile(this, "Chọn file báo cáo kho để kiểm", "xlsx", "Excel");
        if (path is null) return;
        SortedDictionary<string, List<RaDongService.DayRow>>? days = null;
        if (!await Ui.RunAsync(this, () => Task.Run(() => days = Svc.DaysFrom(RaDongService.ReadExcel(path))))) return;
        var missing = new List<string>(); var diff = new List<string>();
        foreach (var (d, rows) in days!)
        {
            if (!RaDongService.IsoOk(d)) continue;
            var saved = Svc.SavedRows.Where(r => r.Date == d).ToList();
            var fileNet = Math.Round(rows.Sum(r => r.Net), 2);
            if (saved.Count == 0) missing.Add(RaDongService.Dmy(d));
            else if (Math.Abs(RaDongService.DaySum(saved) - fileNet) > 0.5) diff.Add($"{RaDongService.Dmy(d)}: đã lưu {RaDongService.DaySum(saved):#,0.00} kg, file {fileNet:#,0.00} kg");
        }
        var msg = $"File: {Path.GetFileName(path)} ({days!.Count} ngày)\n\n" +
            (missing.Count == 0 ? "✔ Không thiếu ngày nào." : $"Chưa lưu {missing.Count} ngày: " + string.Join(", ", missing)) + "\n" +
            (diff.Count == 0 ? "✔ Các ngày đã lưu khớp Net với file." : $"Khác Net {diff.Count} ngày:\n• " + string.Join("\n• ", diff.Take(30))) +
            (missing.Count + diff.Count > 0 ? "\n\nBấm “⤓ Lấy từ Đối chiếu hàng ngày” (hoặc “Nạp file cập nhật…”) để cập nhật." : "");
        await Ui.Message(this, msg, "Kiểm ngày thiếu");
    }

    private async Task ImportFile(bool update, string? given = null)
    {
        if (!_ready) { await Ui.Message(this, "Chưa đọc được dữ liệu đã lưu. Bấm \"Tải lại\" ở tab Số liệu đã lưu rồi thử lại."); return; }
        var path = given ?? await Ui.OpenFile(this, update ? "Chọn file cập nhật (cùng mẫu báo cáo kho)" : "Chọn file báo cáo kho", "xlsx", "Excel");
        if (path is null) return;
        RaDongService.Data? d = null;
        if (!await Ui.RunAsync(this, () => Task.Run(() => d = RaDongService.ReadExcel(path)))) return;
        if (update)
        {
            // chỉ cập nhật Bảng 3, KHÔNG đụng Bảng 1 / Bảng 2
            Svc.LearnSizeLabels(d!);
            await OpenPlan(d!, "update");
            return;
        }
        if (Svc.Bang1.Rows.Count > 0 && !await Ui.Confirm(this, $"Thay dữ liệu đang có ({Svc.Bang1.Rows.Count:#,0} dòng) bằng {d!.Rows.Count:#,0} dòng của file {d.FileName}? Số sửa tay ở bảng gộp cũ sẽ bỏ.", "Nạp file", "Thay")) return;
        if (!await Act(() => Svc.SetBang1(d!), b2: true)) return;
        await OpenPlan(Svc.Bang1, "replace");
    }

    private async Task SaveToBang3()
    {
        if (Svc.Bang1.Rows.Count == 0) { await Ui.Message(this, "Chưa có số liệu nạp. Bấm \"Nạp file Excel…\" trước."); return; }
        await OpenPlan(Svc.Bang1, "replace");
    }

    /// <summary>Hộp thoại lưu / cập nhật Bảng 3 theo từng ngày (giống "Lưu số liệu đã nạp" + "Cập nhật số liệu từ file" của app cũ).</summary>
    private async Task OpenPlan(RaDongService.Data data, string defaultMode)
    {
        SortedDictionary<string, List<RaDongService.DayRow>> days;
        try { days = ReferenceEquals(data, Svc.Bang1) ? Svc.DaysFrom() : Svc.DaysFrom(data); }
        catch (BusinessException e) { await Ui.Message(this, e.Message); return; }
        var plan = days.Select(p => new RaDongService.DayPlan { Date = p.Key, Rows = p.Value }).ToList();
        var dlg = new PlanDialog(Svc, plan, data.FileName.Length > 0 ? data.FileName : "File đã nạp", data.FileHash, defaultMode, update: defaultMode == "update");
        await Ui.Show(this, dlg);
        if (dlg.Result is null) return;
        var (fileName, hash) = (dlg.FileName, data.FileHash);
        RaDongService.ApplyResult? res = null;
        if (!await Act(() => res = Svc.Apply(plan, fileName, hash, data.FileSize, defaultMode), b3: true)) return;
        if (res!.NoChange) _status.Text = "Không có thay đổi nào — số liệu đã lưu đã khớp với file.";
        else
        {
            _status.Text = $"Đã lưu {res.Days} ngày vào Số liệu đã lưu: +{res.NewRows} dòng mới · ~{res.Changed} dòng đổi số.";
            var first = plan.Where(p => RaDongService.IsoOk(p.Date)).Select(p => p.Date).DefaultIfEmpty("").Max()!;
            if (first.Length > 0) { _selYear = first[..4]; ShowBang3(); }
        }
    }

    // =====================================================================================
    // Bảng 3
    // =====================================================================================

    private Control BuildBang3()
    {
        var bar = new ToolRow { Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "Năm", VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
        bar.Children.Add(_year);
        bar.Children.Add(new TextBlock { Text = "Tháng", VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
        bar.Children.Add(_month);
        bar.Children.Add(_q);
        _year.SelectionChanged += (_, _) => { if (!_loading && _year.SelectedItem is string y) { _selYear = y; ShowBang3(); } };
        _month.SelectionChanged += (_, _) => { if (!_loading) { _selMonth = Math.Max(0, _month.SelectedIndex).ToString(); ShowBang3(); } };
        _q.TextChanged += (_, _) => { if (!_loading) ShowBang3(); };
        var reload = Btn("⟳", async () => await LoadAsync()); ToolTip.SetTip(reload, "Tải lại");
        bar.Children.Add(reload);
        bar.Children.Add(ToolRow.Gap());
        if (_edit)
        {
            bar.Children.Add(Btn("⤓ Lấy từ Đối chiếu hàng ngày", async () => await FromDoiChieu()));
            bar.Children.Add(Btn("＋ Thêm dòng", () => { _addBox.IsVisible = !_addBox.IsVisible; if (_addBox.IsVisible && string.IsNullOrEmpty(_addDate.Text)) _addDate.Text = DateTime.Today.ToString("dd/MM/yyyy"); }));
            bar.Children.Add(ToolRow.More(Btn("⬆ Nạp file cập nhật…", async () => await ImportFile(update: true))));
            bar.Children.Add(ToolRow.More(_undoUpd));
            bar.Children.Add(ToolRow.More(Btn("Xóa dòng đã chọn", async () => await DeleteSavedRows())));
            bar.Children.Add(ToolRow.More(Btn("Xóa ngày đã chọn", async () => await DeleteSavedDays())));
            bar.Children.Add(ToolRow.More(_delMonth));
        }
        bar.Children.Add(ToolRow.More(Btn("Kiểm ngày thiếu", async () => await CheckMissingDays())));
        bar.Children.Add(_imgBtn); bar.Children.Add(ToolRow.More(_imgClear));
        if (Svc.CanExport) bar.Children.Add(_exportYear);

        var add = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (label, box) in new (string, Control)[] { ("Ngày", _addDate), ("Mã hàng", _addMa), ("Size", _addSize), ("Số lượng", _addSl), ("Net (kg)", _addNet) })
        {
            add.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
            add.Children.Add(box);
        }
        add.Children.Add(Btn("Thêm", async () => await AddRow(), primary: true));
        _addBox.Child = add; _addBox.Classes.Add("card"); _addBox.Padding = new Thickness(8); _addBox.Margin = new Thickness(0, 0, 0, 6);
        ToolTip.SetTip(_addNet, "Bỏ trống Net nếu đã nhập Số lượng: app tự tính theo kg/đơn vị của Mã hàng + Size đã lưu");

        var hint = new TextBlock
        {
            Text = "Sửa thẳng ô Mã hàng / Size / Số lượng / Net (ô hồng = đã sửa tay hoặc nhập tay). Sửa Số lượng mà để nguyên Net → Net tự tính lại theo kg/đơn vị. Bôi đen nhiều dòng để xóa. Tích ô vuông đầu dòng các ngày cần gộp thành 1 ảnh (ngày cũ ở trên) rồi bấm “Gộp ảnh”; không tích thì xuất ảnh ngày đang chọn.",
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        };
        var head = new StackPanel();
        head.Children.Add(bar); head.Children.Add(hint); head.Children.Add(_addBox);
        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        root.Children.Add(_sGrid);
        return root;
    }

    private void ShowBang3()
    {
        if (!_ready) return;
        _loading = true;
        var years = Svc.Years();
        if (_selYear.Length == 0 || !years.Contains(_selYear))
        {
            var withData = years.Where(y => Svc.SavedRows.Any(r => r.Date.StartsWith(y))).ToList();
            _selYear = withData.Count > 0 ? withData[^1] : years[^1];
        }
        _year.ItemsSource = years; _year.SelectedItem = _selYear;
        var months = new List<string> { "Tất cả" }; months.AddRange(Enumerable.Range(1, 12).Select(m => "Tháng " + m));
        _month.ItemsSource = months; _month.SelectedIndex = int.Parse(_selMonth);
        _loading = false;
        _exportYear.Content = $"Xuất Excel năm {_selYear}…";
        _delMonth.IsEnabled = _selMonth != "0";
        _delMonth.Content = _selMonth != "0" ? $"Xóa tháng {_selMonth}/{_selYear}" : "Xóa tháng";
        _undoUpd.IsVisible = Svc.Undo is not null;
        if (Svc.Undo is { } u) ToolTip.SetTip(_undoUpd, $"Đưa {u.Days.Count} ngày về trước lần cập nhật lúc {At(u.At)} (file {u.File})");
        _addMa.ItemsSource = Svc.SavedRows.Select(r => r.Ma).Distinct().OrderBy(x => x).ToList();

        var q = TongHopService.NormSearch(_q.Text);
        var list = Svc.SavedRows.Where(r => r.Date.StartsWith(_selYear) && (_selMonth == "0" || int.Parse(r.Date.Substring(5, 2)) == int.Parse(_selMonth))).ToList();
        if (q.Length > 0) list = list.Where(r => TongHopService.NormSearch($"{RaDongService.Dmy(r.Date)} {r.Ma} {r.Size}").Contains(q)).ToList();
        var days = list.GroupBy(r => r.Date).OrderByDescending(g => g.Key, StringComparer.Ordinal).ToList();
        _sGrid.Status = _sInfo.Text = $"{days.Count} ngày · {list.Count:#,0} dòng · {list.Sum(r => r.Net).ToString("#,0.00", Ui.Vi)} kg";
        var cols = new List<GridColumn>
        {
            new("d", "Ngày", 130) { ReadOnly = true }, new("m", "Mã hàng", 220) { ReadOnly = !_edit }, new("s", "Size", 100) { ReadOnly = !_edit },
            new("sl", "Số lượng", 100, "number") { ReadOnly = !_edit }, new("n", "Net (kg)", 130, "number") { ReadOnly = !_edit },
            new("src", "Nạp từ / ghi chú", 320) { ReadOnly = true },
            new("pk", "", 0),   // ô tích gộp ảnh (không lưu, chỉ trong phiên)
        };
        var rows = new List<string[]>(); _sRowItem = new(); _sRowDay = new();
        _newestDay = days.Count > 0 ? days[0].Key : "";
        _qOpen = q.Length > 0;
        foreach (var d in days)
        {
            var rs = d.ToList();
            var open = DayOpen(d.Key);
            var last = rs.OrderByDescending(r => r.At, StringComparer.Ordinal).First();
            var sl = RaDongService.SlSum(rs);
            var pk = _imgSel.Contains(d.Key) ? "1" : "";
            rows.Add(new[] { $"{(open ? "▾" : "▸")} {RaDongService.Dmy(d.Key)}", $"{rs.Count} dòng", "", sl is null ? "" : Inv(sl.Value), Inv(RaDongService.DaySum(rs)), last.Src + (last.At.Length > 0 ? " · " + At(last.At) : ""), pk });
            _sRowItem.Add(null); _sRowDay.Add(d.Key);
            if (!open) continue;
            foreach (var r in rs)
            {
                rows.Add(new[] { RaDongService.Dmy(r.Date), r.Ma, r.Size, r.Sl is null ? "" : Inv(r.Sl.Value), Inv(r.Net), r.Edited ? (r.Src == "Nhập tay" ? "nhập tay" : "đã sửa tay " + At(r.EditAt)) : "", pk });
                _sRowItem.Add(r); _sRowDay.Add(d.Key);
            }
        }
        _loading = true;
        _sm.ReadOnly = false;   // tài khoản chỉ xem vẫn tích ngày để gộp ảnh; các cột số liệu khóa theo quyền
        _sm.Load(cols, rows);
        _sSnap = _sm.Snapshot();
        _loading = false;
        _sGrid.Refresh();
        PaintImgBtn();
    }

    private void PaintImgBtn()
    {
        _imgSel.RemoveWhere(d => !Svc.SavedRows.Any(r => r.Date == d));
        var n = _imgSel.Count;
        _imgBtn.Content = n >= 2 ? $"🖼 Gộp ảnh {n} ngày đã tích…" : n == 1 ? "🖼 Ảnh ngày đã tích…" : "🖼 Ảnh ngày đã chọn…";
        _imgClear.IsVisible = n > 0;
    }

    private static string At(string iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime().ToString("dd/MM HH:mm") : "";

    private IBrush? SavedBg(int r, int c)
    {
        if (r >= _sRowItem.Count) return null;
        var it = _sRowItem[r];
        if (it is null) return DayBg;
        return it.Edited && c is >= 1 and <= 4 ? HandBg : null;
    }

    private async void OnSavedGridChanged()
    {
        var now = _sm.Snapshot();
        var edits = new List<(string id, string[] row)>();
        var bad = false;
        for (var r = 0; r < now.Count; r++)
        {
            var before = r < _sSnap.Count ? _sSnap[r] : new string[now[r].Length];
            if (now[r].SequenceEqual(before)) continue;
            string Pk(string[] x) => x.Length > PickCol ? (x[PickCol] ?? "").Trim() : "";
            if (r < _sRowDay.Count && Pk(now[r]) != Pk(before))
            {   // tích / bỏ tích ngày để gộp ảnh — cả ngày đổi theo, không ghi dữ liệu
                if (Pk(now[r]).Length > 0) _imgSel.Add(_sRowDay[r]); else _imgSel.Remove(_sRowDay[r]);
            }
            if (now[r].Take(PickCol).SequenceEqual(before.Take(PickCol))) continue;
            if (r >= _sRowItem.Count || _sRowItem[r] is null) { bad = true; continue; }
            edits.Add((_sRowItem[r]!.Id, now[r]));
        }
        if (edits.Count == 0 || !_edit) { ShowBang3(); return; }
        await Act(() => { foreach (var (id, row) in edits) Svc.EditRow(id, row[1], row[2], row[3], row[4]); }, b3: true);
        if (bad) _status.Text = "Dòng tiêu đề ngày không sửa được — đã bỏ qua.";
    }

    private async Task AddRow()
    {
        var iso = RaDongService.DmyToIso((_addDate.Text ?? "").Trim());
        if (iso.Length == 0) { await Ui.Message(this, "Ngày phải dạng dd/mm/yyyy."); return; }
        RaDongService.Saved? r = null;
        if (!await Act(() => r = Svc.AddRow(iso, _addMa.Text ?? "", _addSize.Text ?? "", _addSl.Text ?? "", _addNet.Text ?? ""))) return;
        _addMa.Text = ""; _addSize.Text = ""; _addSl.Text = ""; _addNet.Text = "";
        _selYear = iso[..4];
        ShowBang3();
        _status.Text = $"Đã thêm {r!.Ma} · {r.Size} · {r.Net.ToString("#,0.00", Ui.Vi)} kg ngày {RaDongService.Dmy(iso)}.";
    }

    private (List<RaDongService.Saved> rows, List<string> days) SavedSelection()
    {
        var s = _sGrid.Selection;
        var rows = new List<RaDongService.Saved>(); var days = new List<string>();
        for (var r = s.R1; r <= s.R2 && r < _sRowItem.Count; r++)
        {
            if (_sRowItem[r] is { } it) rows.Add(it);
            if (!days.Contains(_sRowDay[r])) days.Add(_sRowDay[r]);
        }
        return (rows, days);
    }

    private async Task DeleteSavedRows()
    {
        var (rows, _) = SavedSelection();
        if (rows.Count == 0) { await Ui.Message(this, "Chọn (bôi đen) các dòng cần xóa rồi bấm lại."); return; }
        var txt = rows.Count == 1 ? $"Xóa dòng {rows[0].Ma} · {rows[0].Size} · {rows[0].Net.ToString("#,0.00", Ui.Vi)} kg của ngày {RaDongService.Dmy(rows[0].Date)}?" : $"Xóa {rows.Count} dòng đã chọn?";
        if (!await Ui.Confirm(this, txt, "Xóa dòng", "Xóa")) return;
        await Act(() => Svc.DeleteRows(rows.Select(r => r.Id)), b3: true);
    }

    private async Task DeleteSavedDays()
    {
        var (_, days) = SavedSelection();
        if (days.Count == 0) { await Ui.Message(this, "Chọn (bôi đen) một dòng bất kỳ của ngày cần xóa rồi bấm lại."); return; }
        var n = Svc.SavedRows.Count(r => days.Contains(r.Date));
        if (!await Ui.Confirm(this, $"Xóa toàn bộ {n} dòng của {(days.Count == 1 ? "ngày " + RaDongService.Dmy(days[0]) : days.Count + " ngày: " + string.Join(", ", days.Select(RaDongService.Dmy)))}? Hành động này không thể hoàn tác.", "Xóa ngày", "Xóa")) return;
        await Act(() => Svc.DeleteDays(days), b3: true);
    }

    private async Task DeleteMonth()
    {
        if (_selMonth == "0") return;
        int y = int.Parse(_selYear), m = int.Parse(_selMonth);
        var list = Svc.SavedRows.Where(r => r.Date.StartsWith(_selYear) && int.Parse(r.Date.Substring(5, 2)) == m).ToList();
        if (list.Count == 0) { await Ui.Message(this, $"Tháng {m}/{y} chưa có dữ liệu đã lưu để xóa."); return; }
        var days = list.Select(r => r.Date).Distinct().Count(); var hand = list.Count(r => r.Edited);
        if (!await Ui.Confirm(this, $"Xóa TOÀN BỘ dữ liệu đã lưu của tháng {m}/{y}?\n• {days} ngày · {list.Count} dòng · {list.Sum(r => r.Net).ToString("#,0.00", Ui.Vi)} kg" +
                (hand > 0 ? $"\n• Trong đó {hand} dòng đã sửa tay / nhập tay (cũng bị xóa)" : "") + "\nHành động này không thể hoàn tác.", "Xóa tháng", "Xóa")) return;
        if (await Act(() => Svc.DeleteMonth(y, m), b3: true)) _status.Text = $"Đã xóa tháng {m}/{y} ({days} ngày, {list.Count} dòng).";
    }

    private async Task UndoUpdate()
    {
        if (Svc.Undo is not { } u) return;
        if (!await Ui.Confirm(this, $"Hoàn tác lần cập nhật lúc {At(u.At)} (file {u.File}, {u.Days.Count} ngày)?\nCác ngày đó trở về số liệu TRƯỚC khi cập nhật — mọi chỉnh sửa ở các ngày này SAU lần cập nhật cũng bị bỏ.", "Hoàn tác cập nhật", "Hoàn tác")) return;
        if (await Act(Svc.UndoUpdate, b3: true)) _status.Text = $"Đã hoàn tác lần cập nhật từ \"{u.File}\" ({u.Days.Count} ngày).";
    }

    private async Task ExportYear()
    {
        if (!int.TryParse(_selYear, out var y)) return;
        if (!Svc.SavedRows.Any(r => r.Date.StartsWith(_selYear))) { await Ui.Message(this, $"Chưa có dữ liệu năm {y} để xuất."); return; }
        var path = await Ui.SaveFile(this, "Xuất Excel năm", $"bao-cao-ra-dong-{y}.xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.RunAsync(this, () => Task.Run(() => Svc.ExportYear(y, path)))) Ui.Exported(this, path);
    }

    private async Task DayImage()
    {
        var (_, days) = SavedSelection();
        if (_imgSel.Count > 0) days = _imgSel.ToList();   // đã tích ngày → gộp đúng các ngày đã tích (như app cũ)
        days = days.Where(d => Svc.SavedRows.Any(r => r.Date == d)).OrderBy(d => d, StringComparer.Ordinal).ToList();
        if (days.Count == 0) { await Ui.Message(this, "Tích ô vuông đầu dòng các ngày cần gộp ảnh, hoặc chọn một dòng của ngày cần xuất ảnh, rồi bấm lại."); return; }
        var parts = days.Select(d => (d, Svc.SavedRows.Where(r => r.Date == d).ToList())).ToList();
        var bmp = DayImageRenderer.Render(parts);
        string Dash(string d) => $"{d[8..10]}-{d[5..7]}-{d[..4]}";
        var name = days.Count == 1 ? $"bao-cao-ra-dong_{Dash(days[0])}.png"
            : $"bao-cao-ra-dong_gop_{(days.Count <= 3 ? string.Join("_", days.Select(Dash)) : Dash(days[0]) + "_den_" + Dash(days[^1]))}.png";
        var title = days.Count == 1 ? $"Ảnh bảng chi tiết xuất · {RaDongService.Dmy(days[0])}" : $"Ảnh gộp {days.Count} ngày · {string.Join(", ", days.Select(RaDongService.Dmy))}";
        var save = new Button { Content = "Lưu ảnh PNG…", Classes = { "primary" }, IsDefault = true };
        var close = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Ui.ImagePreview(bmp));
        body.Children.Add(Ui.Buttons(close, save));
        var w = Ui.Dialog(title, body, Ui.ImageDialogWidth(bmp));
        close.Click += (_, _) => w.Close();
        save.Click += async (_, _) =>
        {
            var path = await Ui.SaveFile(w, "Lưu ảnh", name, "png", "Ảnh PNG");
            if (path is null) return;
            if (await Ui.Run(w, () => bmp.Save(path))) { w.Close(); Ui.Exported(this, path); }
        };
        await Ui.Show(this, w);
    }

    // =====================================================================================
    // Hộp thoại lưu / cập nhật theo ngày
    // =====================================================================================

    private sealed class PlanDialog : Window
    {
        public object? Result { get; private set; }
        public string FileName { get; }
        private readonly RaDongService _svc;
        private readonly List<RaDongService.DayPlan> _plan;
        private readonly string _defaultMode;
        private readonly RaDongService.FileSeen? _dup;
        private readonly StackPanel _blocks = new() { Spacing = 10 };
        private readonly TextBlock _total = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
        private readonly CheckBox _showSame = new() { Content = "Hiện cả dòng giống", FontSize = 12.5 };

        public PlanDialog(RaDongService svc, List<RaDongService.DayPlan> plan, string fileName, string hash, string defaultMode, bool update)
        {
            _svc = svc; _plan = plan; FileName = fileName; _defaultMode = defaultMode;
            _dup = hash.Length > 0 ? svc.SeenBefore(hash) : null;
            Title = update ? "Cập nhật số liệu từ file" : "Lưu số liệu đã nạp theo ngày";
            Width = 860; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = AppTheme.Card;
            var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12.5,
                Text = $"File: {fileName} · {plan.Count} ngày. " + (update
                    ? "Mặc định \"Cập nhật\": giữ mọi dòng cũ, chỉ thêm dòng mới và sửa dòng khác số; dòng bạn đã sửa tay được giữ. Bảng gộp ở tab Số liệu nạp không bị đổi."
                    : "Ngày đã có số liệu cũ: mặc định THAY THẾ (nạp lại cùng một ngày thường là số đã chỉnh, cộng dồn sẽ bị tính đúp). Có thể chọn Cập nhật để giữ dòng cũ và dòng đã sửa tay."),
            });
            if (_dup is not null)
                head.Children.Add(new Border
                {
                    Background = AppTheme.Brush("#FFF1C9"), Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(4),
                    Child = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Text = $"⚠ File này (cùng nội dung) đã nạp lúc {At(_dup.At)} ({_dup.Name}{(_dup.Days > 0 ? " · " + _dup.Days + " ngày" : "")}). Kiểu Cập nhật / Thay thế vẫn an toàn; đừng chọn \"Cộng dồn\"." },
                });
            head.Children.Add(_showSame);
            _showSame.IsCheckedChanged += (_, _) => Paint();
            var ok = new Button { Content = update ? "Cập nhật" : "Lưu", Classes = { "primary" }, IsDefault = true };
            var cancel = new Button { Content = "Hủy (không đổi gì)", IsCancel = true };
            ok.Click += async (_, _) => await Ok();
            cancel.Click += (_, _) => Close();
            var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var btns = Ui.Buttons(cancel, ok); btns.Margin = new Thickness(0);
            DockPanel.SetDock(btns, Dock.Right);
            foot.Children.Add(btns); foot.Children.Add(_total);
            var root = new DockPanel { Margin = new Thickness(18, 14) };
            DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(head); root.Children.Add(foot);
            root.Children.Add(new ScrollViewer { Content = _blocks });
            Content = root;
            Paint();
        }

        private static readonly Dictionary<string, string> ModeText = new()
        {
            ["update"] = "Cập nhật (giữ dòng cũ)", ["replace"] = "Thay thế cả ngày", ["add"] = "Cộng dồn", ["skip"] = "Bỏ qua", ["new"] = "Thêm mới",
        };
        private static readonly Dictionary<RaDongService.Kind, string> KindText = new()
        {
            [RaDongService.Kind.New] = "dòng mới", [RaDongService.Kind.Chg] = "khác số", [RaDongService.Kind.Sl] = "bổ sung / sửa số lượng",
            [RaDongService.Kind.Manual] = "file khác số bạn đã sửa", [RaDongService.Kind.Old] = "không có trong file", [RaDongService.Kind.Same] = "giống",
        };
        private static string F(double d) => d.ToString("#,0.00", Ui.Vi);
        private static TextBlock T(string s, bool bold = false, IBrush? fg = null) => new() { Text = s, FontSize = 12.5, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, Foreground = fg ?? Brushes.Black, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

        private void Paint()
        {
            _blocks.Children.Clear();
            double tb = 0, ta = 0;
            foreach (var p in _plan)
            {
                var c = _svc.Calc(p, _defaultMode);
                tb += c.Has ? c.Before : 0; ta += c.After;
                var box = new StackPanel { Spacing = 6 };
                var h = new WrapPanel { ItemSpacing = 10, LineSpacing = 4 };
                if (p.Date.Length > 0 && RaDongService.IsoOk(p.Date) && !p.Date.StartsWith("0000")) h.Children.Add(T(RaDongService.Dmy(p.Date), bold: true));
                else
                {
                    var dt = new TextBox { Watermark = "Ngày dd/mm/yyyy", Width = 140, Text = "" };
                    dt.LostFocus += (_, _) => { p.Date = RaDongService.DmyToIso(dt.Text ?? ""); p.Mode = ""; p.Take.Clear(); Paint(); };
                    h.Children.Add(T("File không có cột Ngày:")); h.Children.Add(dt);
                }
                var modes = c.Has ? new[] { "update", "replace", "add", "skip" } : new[] { "new", "skip" };
                var cb = new ComboBox { ItemsSource = modes.Select(m => ModeText[m]).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(modes, c.Mode)), MinWidth = 190 };
                cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) { p.Mode = modes[cb.SelectedIndex]; Paint(); } };
                h.Children.Add(cb);
                int N(params RaDongService.Kind[] k) => c.Items.Count(x => k.Contains(x.Kind));
                h.Children.Add(T(c.Has
                    ? $"+{N(RaDongService.Kind.New)} mới · ~{N(RaDongService.Kind.Chg, RaDongService.Kind.Manual)} khác số" + (N(RaDongService.Kind.Sl) > 0 ? $" · {N(RaDongService.Kind.Sl)} bổ sung SL" : "") + $" · ={N(RaDongService.Kind.Same)} giống · {N(RaDongService.Kind.Old)} cũ không có trong file"
                    : $"{p.Rows.Count} dòng mới", fg: AppTheme.Muted));
                var diff = RaDongService.Round(c.After - (c.Has ? c.Before : 0));
                h.Children.Add(T((c.Has ? $"Tổng: {F(c.Before)} → " : "Tổng: ") + F(c.After) + $"  ({(diff >= 0 ? "+" : "")}{F(diff)})", bold: true,
                    fg: diff >= 0 ? AppTheme.Brush("#1B7F3B") : AppTheme.Brush("#B42318")));
                box.Children.Add(h);
                var hand = c.Existing.Count(r => r.Edited);
                if (c.Has && c.Mode == "update")
                {
                    var list = c.Items.Where(x => _showSame.IsChecked == true || x.Kind != RaDongService.Kind.Same).ToList();
                    if (list.Count == 0) box.Children.Add(T("Không có dòng nào khác nhau.", fg: AppTheme.Muted));
                    else
                    {
                        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,*,2*,1.6*"), Margin = new Thickness(12, 0, 0, 0) };
                        void Cell(int r, int col, Control ctl) { Grid.SetRow(ctl, r); Grid.SetColumn(ctl, col); ctl.Margin = new Thickness(0, 1, 8, 1); g.Children.Add(ctl); }
                        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                        var heads = new[] { "Mã hàng", "Size", "Đang lưu", "Trong file", "Tình trạng", "Xử lý" };
                        for (var i = 0; i < heads.Length; i++) Cell(0, i, T(heads[i], bold: true));
                        var row = 1;
                        foreach (var x in list)
                        {
                            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                            Cell(row, 0, T(x.Ma)); Cell(row, 1, T(x.Size));
                            Cell(row, 2, T(x.List is null ? "—" : F(x.Cur) + (x.Kind == RaDongService.Kind.Manual ? " (sửa tay)" : "")));
                            Cell(row, 3, T(x.X is null ? "—" : F(x.X.Net)));
                            var extra = x.Kind is RaDongService.Kind.Chg or RaDongService.Kind.Manual ? $" ({(x.X!.Net - x.Cur >= 0 ? "+" : "")}{F(x.X.Net - x.Cur)})"
                                : x.Kind == RaDongService.Kind.Sl ? $" (SL: {(RaDongService.FmtSlVn(x.CurSl) is { Length: > 0 } a ? a : "trống")} → {RaDongService.FmtSlVn(x.X!.Sl)})" : "";
                            Cell(row, 4, T(KindText[x.Kind] + extra, fg: x.Kind == RaDongService.Kind.Manual ? AppTheme.Brush("#B42318") : null));
                            string[]? opt = x.Kind switch
                            {
                                RaDongService.Kind.New => new[] { "Thêm", "Bỏ qua" },
                                RaDongService.Kind.Chg => new[] { "Lấy số mới", "Giữ số cũ" },
                                RaDongService.Kind.Sl => new[] { "Cập nhật số lượng", "Bỏ qua" },
                                RaDongService.Kind.Manual => new[] { "Lấy số file", "Giữ số đã sửa" },
                                _ => null,
                            };
                            if (opt is null) Cell(row, 5, T(x.Kind == RaDongService.Kind.Old ? "giữ nguyên" : "—", fg: AppTheme.Muted));
                            else
                            {
                                var take = p.Take.TryGetValue(x.Key, out var t) ? t : x.DefaultTake;
                                var pick = new ComboBox { ItemsSource = opt, SelectedIndex = take ? 0 : 1, FontSize = 12, MinWidth = 140 };
                                var key = x.Key;
                                pick.SelectionChanged += (_, _) => { p.Take[key] = pick.SelectedIndex == 0; Paint(); };
                                Cell(row, 5, pick);
                            }
                            row++;
                        }
                        box.Children.Add(g);
                    }
                }
                else if (c.Has && c.Mode == "replace") box.Children.Add(T($"Thay thế cả ngày: xóa {c.Existing.Count} dòng đang lưu" + (hand > 0 ? $" ({hand} dòng đã sửa tay sẽ mất)" : "") + $" và ghi {p.Rows.Count} dòng của file.", fg: hand > 0 ? AppTheme.Brush("#B42318") : AppTheme.Muted));
                else if (c.Has && c.Mode == "add") box.Children.Add(T($"Cộng dồn: cộng Net của {p.Rows.Count} dòng trong file vào các dòng trùng Mã hàng + Size, dòng chưa có thì thêm." + (_dup is not null ? " File này đã nạp trước đó — cộng dồn sẽ bị tính ĐÚP." : ""), fg: AppTheme.Muted));
                else if (c.Mode == "skip") box.Children.Add(T("Bỏ qua ngày này — không đổi gì.", fg: AppTheme.Muted));
                _blocks.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(10, 8), Child = box });
            }
            _total.Text = $"Tổng các ngày: {F(tb)} → {F(ta)}";
        }

        private async Task Ok()
        {
            var cs = _plan.Select(p => (p, c: _svc.Calc(p, _defaultMode))).ToList();
            if (cs.Any(x => !RaDongService.IsoOk(x.p.Date) && x.c.Mode != "skip")) { await Ui.Message(this, "Chọn ngày cho dữ liệu chưa có cột Ngày (hoặc chọn Bỏ qua)."); return; }
            if (_dup is not null && cs.Any(x => x.c.Mode == "add") && !await Ui.Confirm(this, "File này đã nạp trước đó. Cộng dồn sẽ cộng số liệu LẦN NỮA (tính đúp). Vẫn tiếp tục?", "Cộng dồn", "Tiếp tục")) return;
            // chốt chế độ đang hiện để Apply dùng đúng (ngày đã có dữ liệu mà chưa chọn → chế độ mặc định)
            foreach (var (p, c) in cs) p.Mode = c.Mode;
            Result = true;
            Close();
        }
    }
}

/// <summary>Vẽ ảnh "bảng chi tiết xuất" theo ngày (giống rd-xuat.js): NGÀY : dd.mm.yyyy · MÃ HÀNG | SIZE | Số lượng | Net · Tổng cộng, chữ Times New Roman, nền trắng. Nhiều ngày xếp dọc.</summary>
public static class DayImageRenderer
{
    private const double F = 26, FT = 30, PAD = 20, HT = 48, HH = 42, HR = 38, HS = 44, M = 2, GAP = 48;
    private static readonly Typeface Reg = new("Times New Roman, Liberation Serif, Times, serif");
    private static readonly Typeface Bold = new(new FontFamily("Times New Roman, Liberation Serif, Times, serif"), FontStyle.Normal, FontWeight.Bold);

    private static FormattedText Text(string s, bool bold, double px) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? Bold : Reg, px, Brushes.Black);
    private static double W(string s, bool bold, double px) => s.Length == 0 ? 0 : Text(s, bold, px).Width;

    private sealed record Layout(double Width, double Height, double[] Cols, string Title, List<string[]> Rows, string Total, string TotalSl);

    private static Layout Measure(string iso, List<RaDongService.Saved> rows)
    {
        var title = $"NGÀY : {iso[8..10]}.{iso[5..7]}.{iso[..4]}";
        var data = rows.Select(r => new[] { r.Ma, r.Size, RaDongService.FmtSlVn(r.Sl), RaDongService.FmtNetVn(r.Net) }).ToList();
        var total = RaDongService.FmtNetVn(RaDongService.DaySum(rows));
        var totalSl = RaDongService.FmtSlVn(RaDongService.SlSum(rows));
        double Mx(string head, int i, double extra = 0) => Math.Max(Math.Max(W(head, true, F), data.Select(d => W(d[i], false, F)).DefaultIfEmpty(0).Max()), extra) + PAD * 2;
        var w = new[] { Mx("MÃ HÀNG", 0), Mx("SIZE", 1), Mx("Số lượng", 2, W(totalSl, true, F)), Mx("Net", 3, W(total, true, F)) };
        var needTong = W("Tổng cộng", true, F) + PAD * 2; if (w[0] + w[1] < needTong) w[0] = needTong - w[1];
        var needTitle = W(title, true, FT) + PAD * 2; var cur = w.Sum();
        if (cur < needTitle) { var k = needTitle / cur; w = w.Select(x => x * k).ToArray(); }
        w = w.Select(Math.Ceiling).ToArray();
        return new Layout(w.Sum(), HT + HH + data.Count * HR + HS, w, title, data, total, totalSl);
    }

    private sealed class Surface : Control
    {
        private readonly List<Layout> _parts;
        public Surface(List<Layout> parts) { _parts = parts; }
        public override void Render(DrawingContext dc)
        {
            dc.FillRectangle(Brushes.White, new Rect(Bounds.Size));
            var pen = new Pen(Brushes.Black, 1);
            var y0 = M;
            foreach (var L in _parts)
            {
                var x = new[] { M, M + L.Cols[0], M + L.Cols[0] + L.Cols[1], M + L.Cols[0] + L.Cols[1] + L.Cols[2], M + L.Width };
                void Put(string s, bool bold, double px, string align, double x0, double x1, double yy, double h)
                {
                    if (s.Length == 0) return;
                    var t = Text(s, bold, px);
                    var tx = align == "c" ? (x0 + x1 - t.Width) / 2 : align == "l" ? x0 + PAD : x1 - PAD - t.Width;
                    dc.DrawText(t, new Point(tx, yy + (h - t.Height) / 2 + 1));
                }
                var y = y0;
                Put(L.Title, true, FT, "c", x[0], x[4], y, HT); y += HT;
                var heads = new[] { "MÃ HÀNG", "SIZE", "Số lượng", "Net" };
                for (var i = 0; i < 4; i++) Put(heads[i], true, F, "c", x[i], x[i + 1], y, HH);
                y += HH; var yHead = y;
                foreach (var r in L.Rows) { Put(r[0], false, F, "l", x[0], x[1], y, HR); Put(r[1], false, F, "c", x[1], x[2], y, HR); Put(r[2], false, F, "r", x[2], x[3], y, HR); Put(r[3], false, F, "r", x[3], x[4], y, HR); y += HR; }
                Put("Tổng cộng", true, F, "c", x[0], x[2], y, HS); Put(L.TotalSl, true, F, "r", x[2], x[3], y, HS); Put(L.Total, true, F, "r", x[3], x[4], y, HS);
                var yTot = y; y += HS;
                void H(double yy) => dc.DrawLine(pen, new Point(x[0], yy), new Point(x[4], yy));
                void V(double xx, double a, double b) => dc.DrawLine(pen, new Point(xx, a), new Point(xx, b));
                H(y0); H(y0 + HT); H(yHead); H(yTot); H(y);
                for (var k = 1; k < L.Rows.Count; k++) H(yHead + k * HR);
                V(x[0], y0, y); V(x[4], y0, y);
                V(x[1], y0 + HT, yTot); V(x[2], y0 + HT, y); V(x[3], y0 + HT, y);
                y0 = y + GAP;
            }
        }
    }

    /// <summary>Ảnh độ nét ×2 (giống app cũ).</summary>
    public static RenderTargetBitmap Render(List<(string iso, List<RaDongService.Saved> rows)> days)
    {
        var parts = days.Select(d => Measure(d.iso, d.rows)).ToList();
        var w = parts.Max(p => p.Width) + M * 2;
        var h = parts.Sum(p => p.Height) + GAP * (parts.Count - 1) + M * 2;
        var s = new Surface(parts) { Width = w, Height = h };
        s.Measure(new Size(w, h)); s.Arrange(new Rect(0, 0, w, h));
        var bmp = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(w * 2), (int)Math.Ceiling(h * 2)), new Vector(192, 192));
        bmp.Render(s);
        return bmp;
    }
}

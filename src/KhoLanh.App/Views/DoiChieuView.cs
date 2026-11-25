using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.DoiChieu;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Services;
using static KhoLanh.Core.DoiChieu.DcxEngine;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Đối chiếu hàng ngày (C#, viết lại theo M16 của app cũ — không dùng WebView2).
/// Liên kết 3 file Excel trên máy (Xuất thay thùng, Xuất rã đông = ghi thẳng; Xưởng = chỉ đọc) → so theo (Ngày, Mã)
/// → sửa số / chuyển mã giữa 2 file Kho / thêm dòng / nạp file gốc — ghi thẳng vào file Excel Kho, giữ nguyên mẫu, có sao lưu và hoàn tác.
/// </summary>
public sealed partial class DoiChieuView : UserControl, IPage
{
    public string Title => "Đối chiếu hàng ngày";
    public string Subtitle => "So file Xuất của Kho với file Xưởng theo Ngày + Mã hàng; sửa / chuyển / thêm dòng ghi thẳng vào file Excel Kho (giữ nguyên mẫu, có sao lưu và hoàn tác).";

    private static DoiChieuService Svc => App.Services.DoiChieu;
    private static readonly IBrush DayBg = AppTheme.Brush("#DCEBDD");
    private static readonly IBrush WarnDayBg = AppTheme.Brush("#FBE3D6");
    private static readonly IBrush EditedBg = AppTheme.Brush("#FFF1C9");
    private static readonly IBrush LechBg = AppTheme.Brush("#FFE699");
    private static readonly IBrush ChiKhoBg = AppTheme.Brush("#F8CBAD");
    private static readonly IBrush ChiXuongBg = AppTheme.Brush("#BDD7EE");
    private static readonly IBrush KhopBg = AppTheme.Brush("#E2EFDA");
    private static readonly IBrush ZeroBg = AppTheme.Brush("#EDEDED");
    private static readonly IBrush MoveBg = AppTheme.Brush("#D9E7FF");

    private readonly bool _edit;
    private bool _loading, _busy;
    private int _pid = 1;
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };

    // ---------- tab Đối chiếu
    private readonly RadioButton _p1 = new() { Content = "Xuất thay thùng", GroupName = "dcxpair", IsChecked = true }, _p2 = new() { Content = "Xuất rã đông", GroupName = "dcxpair" };
    private readonly ComboBox _day = new() { MinWidth = 150 }, _filter = new() { MinWidth = 150 };
    private readonly TextBox _q = new() { Watermark = "Tìm mã / tên hàng…", Width = 200 };
    private readonly CheckBox _hideZero = new() { Content = "Ẩn dòng 0", FontSize = 12.5 };
    private readonly TextBlock _summary = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _note = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly GridModel _gm = new();
    private readonly SheetGrid _grid;
    private List<Item?> _rowItem = new();              // dòng lưới → mục (null = dòng tổng ngày)
    private List<Day?> _rowDay = new();
    private List<string[]> _snap = new();
    private static readonly string[] Cols = { "ngay", "ma", "ten", "ksl", "knet", "xsl", "xnet", "dsl", "dnet", "tt", "gc" };
    private static readonly (string Key, string Text)[] Filters =
    {
        ("can", "Cần xem (chưa khớp)"), ("all", "Tất cả"), ("lech", "Lệch"), ("chi_kho", "Chỉ có ở Kho"), ("chi_xuong", "Chỉ có ở Xưởng"), ("zero", "Ghi 0"), ("khop", "Khớp"),
    };

    // ---------- tab Liên kết file
    private readonly StackPanel _linkPanel = new() { Spacing = 10, Margin = new Thickness(0, 8, 0, 8) };
    // ---------- tab Lịch sử
    private readonly StackPanel _histPanel = new() { Spacing = 6, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TabControl _tabs;

    public DoiChieuView()
    {
        _edit = Svc.CanEdit;
        _grid = new SheetGrid(_gm) { SpareRows = 0, Display = Disp, CellBackground = Bg };
        _gm.Changed += () => { if (!_loading) OnGridChanged(); };
        _grid.SelectionChanged += ShowRowNote;
        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Đối chiếu", Content = BuildCompare() },
                new TabItem { Header = "Xuất kiểm", Content = BuildXuatKiem() },
                new TabItem { Header = "Liên kết file", Content = new ScrollViewer { Content = _linkPanel } },
                new TabItem { Header = "Lịch sử ghi file", Content = new ScrollViewer { Content = _histPanel } },
            },
        };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_status, Dock.Right);
        top.Children.Add(_status);
        top.Children.Add(new TextBlock
        {
            Text = _edit ? "Liên kết file trên máy này (lưu theo tài khoản). File Xưởng chỉ đọc; sửa ở bảng ghi thẳng vào file Excel Kho — mỗi ngày sao lưu 1 bản, giữ 7 ngày."
                         : "Chỉ xem: tài khoản không có quyền ghi vào file Excel Kho (vẫn liên kết, so và sửa tay số Xưởng để đối chiếu được).",
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        });
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(_tabs);
        Content = root;
        _timer.Tick += (_, _) => AutoSync();
        AttachedToVisualTree += async (_, _) => { await LoadAsync(); _timer.Start(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private static Button Btn(string text, Action click, bool primary = false, bool danger = false, string? tip = null)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (danger) b.Classes.Add("danger");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
    private static TextBlock Label(string t, bool bold = false, double size = 12.5) => new() { Text = t, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private static string N(double? d) => d is null ? "" : d.Value.ToString("#,0.###", Ui.Vi);
    private static string Kg(double? d) => d is null ? "" : d.Value.ToString("#,0.00", Ui.Vi);
    private static string SgnN(double d) => d == 0 ? "0" : (d > 0 ? "+" : "") + d.ToString("#,0.###", Ui.Vi);
    private static string SgnKg(double d) => Math.Abs(d) < 0.005 ? "0,00" : (d > 0 ? "+" : "") + d.ToString("#,0.00", Ui.Vi);
    private static string Inv(double? d) => d is null ? "" : Math.Round(d.Value, 6).ToString(CultureInfo.InvariantCulture);

    private async Task LoadAsync()
    {
        _status.Text = "Đang đọc các file đã liên kết…";
        _busy = true;
        try { await Task.Run(Svc.Load); }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); }
        finally { _busy = false; }
        _status.Text = "";
        _loading = true; _hideZero.IsChecked = Svc.St.HideZero; _loading = false;
        RenderAll();
        if (Svc.St.Links.Count == 0) _tabs.SelectedIndex = 1;
        await AskNeeds();
    }

    private void RenderAll() { RenderCompare(); RenderLinks(); RenderHistory(); }

    /// <summary>File Excel đã đổi bên ngoài (Excel vừa lưu) → tự đọc lại.</summary>
    private void AutoSync()
    {
        if (_busy || !Svc.Loaded || Svc.St.Links.Count == 0) return;
        try
        {
            var ch = Svc.SyncLinks();
            if (ch.Count == 0) return;
            _status.Text = "Đã đọc lại file vừa thay đổi: " + string.Join(", ", ch.Select(DoiChieuService.SlotLabel)) + " (" + DateTime.Now.ToString("HH:mm:ss") + ")";
            RenderAll();
        }
        catch { /* thử lại lần sau */ }
    }

    /// <summary>Chạy thao tác (có thể ghi file) → vẽ lại.</summary>
    private async Task<bool> Act(Func<string?> a)
    {
        _busy = true;
        try
        {
            string? msg = null;
            await Task.Run(() => msg = a());
            if (!string.IsNullOrEmpty(msg)) _status.Text = msg;
            return true;
        }
        catch (DoiChieuService.CommitException e) { await Ui.Message(this, e.Message, e.Code == "LOCKED" ? "File đang mở" : "Chưa ghi được"); }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); }
        finally { _busy = false; RenderAll(); }
        return false;
    }

    // =====================================================================================
    // Tab Đối chiếu
    // =====================================================================================

    private Control BuildCompare()
    {
        var bar = new ToolRow { Margin = new Thickness(0, 8, 0, 6) };
        bar.Children.Add(_p1); bar.Children.Add(_p2);
        _p1.IsCheckedChanged += (_, _) => { if (_p1.IsChecked == true && _pid != 1) { _pid = 1; RenderCompare(); } };
        _p2.IsCheckedChanged += (_, _) => { if (_p2.IsChecked == true && _pid != 2) { _pid = 2; RenderCompare(); } };
        bar.Children.Add(new Border { Width = 8 });
        bar.Children.Add(Label("Ngày:")); bar.Children.Add(_day);
        bar.Children.Add(Label("Hiện:")); bar.Children.Add(_filter);
        _filter.ItemsSource = Filters.Select(f => f.Text).ToList(); _filter.SelectedIndex = 0;
        bar.Children.Add(_q); bar.Children.Add(_hideZero);
        _day.SelectionChanged += (_, _) => { if (!_loading) RenderGrid(); };
        _filter.SelectionChanged += (_, _) => { if (!_loading) RenderGrid(); };
        _q.TextChanged += (_, _) => { if (!_loading) RenderGrid(); };
        _hideZero.IsCheckedChanged += (_, _) => { if (_loading) return; Svc.St.HideZero = _hideZero.IsChecked == true; try { Svc.Persist(); } catch { } RenderGrid(); };

        var act = new ToolRow { Margin = new Thickness(0, 0, 0, 6) };
        if (_edit)
        {
            act.Children.Add(Btn("⇄ Chuyển sang file kia", async () => await MoveSelected(), primary: true,
                tip: "Chuyển số lượng Kho của các dòng đã chọn sang file Kho còn lại (dòng có gợi ý “⇄” chuyển đúng số gợi ý)."));
            act.Children.Add(Btn("＋ Thêm dòng vào file Kho…", async () => await AddRow()));
            act.Children.Add(Btn("✎ Sửa mã / ngày…", async () => await Retag()));
            act.Children.Add(Btn("🗑 Xóa khỏi file Kho", async () => await DeleteSelected(), danger: true));
            act.Children.Add(Btn("🔠 Thêm cột Size…", async () => await AddSizeCol(), tip: "Thêm cột Size (theo từ điển Mã hóa sản phẩm) vào file Kho để in chứng từ"));
            act.Children.Add(Btn("↶ Hoàn tác lần ghi cuối", async () => await UndoLast()));
        }
        act.Children.Add(Btn("⟳ Đọc lại file", async () => await Act(() => { Svc.SyncLinks(true); return "Đã đọc lại các file đã liên kết."; })));
        if (Svc.CanExport) act.Children.Add(Btn("Xuất Excel kết quả…", async () => await Export()));

        var hint = new TextBlock
        {
            Text = "Sửa thẳng ô SL Kho (ghi vào file Excel Kho) hoặc SL / Net Xưởng (sửa tay chỉ để đối chiếu, ô vàng — file Xưởng không bị đụng). Dán nhiều ô một lúc như Excel. Chọn dòng để xem ghi chú bên dưới.",
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        };
        var head = new StackPanel();
        head.Children.Add(bar); head.Children.Add(_summary); head.Children.Add(act); head.Children.Add(hint);
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(_note, Dock.Bottom);
        dock.Children.Add(head); dock.Children.Add(_note);
        dock.Children.Add(new Border { BorderBrush = AppTheme.Brush("#D0D7E2"), BorderThickness = new Thickness(1), Child = _grid });
        return dock;
    }

    private DoiChieuService.Pair P => Svc.PairOf(_pid);

    private void RenderCompare()
    {
        var p = P;
        _p1.Content = "Xuất thay thùng" + Badge(Svc.P1); _p2.Content = "Xuất rã đông" + Badge(Svc.P2);
        var r = p.Result;
        if (r is null)
        {
            var why = !Svc.St.Links.ContainsKey(DoiChieuService.SlotOf(_pid)) ? $"Chưa liên kết file {DoiChieuService.KN(_pid)}."
                : p.LinkErr.Length > 0 ? p.LinkErr
                : Svc.Xuong is null ? (Svc.XErr.Length > 0 ? Svc.XErr : "Chưa liên kết file Xưởng.")
                : p.XuongSheet.Length == 0 ? $"Chưa chọn sheet Xưởng cho {DoiChieuService.KN(_pid)}." : "Chưa đọc được dữ liệu (chưa chọn đủ cột).";
            _summary.Text = why + " → mở tab “Liên kết file”.";
        }
        else
        {
            var s = r.Summary;
            _summary.Text = $"{p.Kho!.File} ↔ Xưởng “{p.XuongSheet}” · {s.NNgay} ngày ({s.NNgayLech} ngày có lệch) · {s.NMuc} mục: " +
                $"{s.NKhop} khớp · {s.NLech} lệch · {s.NChiKho} chỉ có ở Kho · {s.NChiXuong} chỉ có ở Xưởng" + (s.NZero > 0 ? $" · {s.NZero} ghi 0" : "") +
                $"  |  Kho {N(s.KSl)} · {Kg(s.KNet)} kg — Xưởng {N(s.XSl)} · {Kg(s.XNet)} kg — Δ {SgnN(s.DSl)} · {SgnKg(s.DNet)} kg" +
                (r.NNhanh > 0 ? $"  |  {r.NNhanh} mục BTP / NL so nhanh theo Size + kg/thùng" : "");
        }
        _loading = true;
        var keep = _day.SelectedItem as string;
        var days = new List<string> { "Toàn file" };
        if (r is not null) days.AddRange(r.Days.Select(d => Dmy(d.Ngay) + " " + DoiChieuService.Weekday(d.Ngay) + (d.Attn ? " ⚠" : "")));
        _day.ItemsSource = days;
        _day.SelectedIndex = keep is not null && days.IndexOf(keep) is var i and >= 0 ? i : 0;
        _loading = false;
        RenderGrid();
    }
    private static string Badge(DoiChieuService.Pair p) => p.Result is { } r && (r.Summary.NLech + r.Summary.NChiKho + r.Summary.NChiXuong) > 0 ? $" ({r.Summary.NLech + r.Summary.NChiKho + r.Summary.NChiXuong})" : "";

    private bool Pass(Item it)
    {
        var f = Filters[Math.Max(0, _filter.SelectedIndex)].Key;
        if (f == "can" && it.Status == "khop") return false;
        if (f is not ("can" or "all") && it.Status != f) return false;
        if (_hideZero.IsChecked == true && (it.KSl ?? 0) == 0 && (it.XSl ?? 0) == 0 && (it.KNet ?? 0) == 0 && (it.XNet ?? 0) == 0) return false;
        var q = (_q.Text ?? "").Trim();
        if (q.Length > 0 && it.Ma.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && it.Ten.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    private void RenderGrid()
    {
        var p = P; var r = p.Result;
        var cols = new List<GridColumn>
        {
            new("ngay", "Ngày", 92) { ReadOnly = true }, new("ma", "Mã hàng", 200) { ReadOnly = true }, new("ten", "Tên hàng", 200) { ReadOnly = true },
            new("ksl", "SL Kho", 80, "number") { ReadOnly = !_edit && !(p.Result is not null && !Svc.IsLinked(p)) }, new("knet", "Net Kho", 95, "number") { ReadOnly = Svc.IsLinked(p) },
            new("xsl", "SL Xưởng", 80, "number"), new("xnet", "Net Xưởng", 95, "number"),
            new("dsl", "Δ SL", 70, "number") { ReadOnly = true }, new("dnet", "Δ Net", 85, "number") { ReadOnly = true },
            new("tt", "Trạng thái", 115) { ReadOnly = true }, new("gc", "Ghi chú", 330) { ReadOnly = true },
        };
        var rows = new List<string[]>(); _rowItem = new(); _rowDay = new();
        if (r is not null)
        {
            var sel = _day.SelectedIndex > 0 && _day.SelectedIndex - 1 < r.Days.Count ? r.Days[_day.SelectedIndex - 1] : null;
            foreach (var d in sel is not null ? new List<Day> { sel } : r.Days)
            {
                var items = d.Items.Where(Pass).OrderBy(i => StatusRank(i.Status)).ThenBy(i => i.Ma, StringComparer.Ordinal).ToList();
                if (items.Count == 0 && sel is null) continue;
                var warn = new List<string>();
                if (d.CheckKho is { Khop: false } ck) warn.Add($"dòng tổng Kho ghi {N(ck.Sl)} · {Kg(ck.Net)} kg ≠ cộng chi tiết");
                if (d.CheckXuong is { Khop: false } cx) warn.Add($"dòng tổng Xưởng ghi {N(cx.Sl)} · {Kg(cx.Net)} kg ≠ cộng chi tiết");
                if (d.NcKho is { } nk) warn.Add($"Kho có {nk.N} dòng chưa mã ({N(nk.Sl)})");
                if (d.NcXuong is { } nx) warn.Add($"Xưởng có {nx.N} dòng chưa mã ({N(nx.Sl)})");
                rows.Add(new[] { Dmy(d.Ngay), "TỔNG NGÀY " + DoiChieuService.Weekday(d.Ngay), $"{d.Items.Count} mục · {d.NLech} lệch · {d.NChiKho + d.NChiXuong} chỉ 1 bên",
                    Inv(d.Kho.Sl), Inv(d.Kho.Net), Inv(d.Xuong.Sl), Inv(d.Xuong.Net), Inv(d.DSl), Inv(d.DNet), d.CoLech ? "Có lệch" : "Khớp", warn.Count > 0 ? "⚠ " + string.Join(" · ", warn) : "" });
                _rowItem.Add(null); _rowDay.Add(d);
                foreach (var it in items)
                {
                    var note = Svc.NoteOf(_pid, it);
                    rows.Add(new[] { Dmy(it.Ngay), it.Ma, it.TheoSize ? (it.Ten.Length > 0 ? it.Ten : it.Size) : it.Ten, Inv(it.KSl), Inv(it.KNet), Inv(it.XSl), Inv(it.XNet),
                        it.Status == "khop" ? "" : Inv(it.DSl), it.Status == "khop" ? "" : Inv(it.DNet), StatusText.GetValueOrDefault(it.Status, it.Status), note.Line });
                    _rowItem.Add(it); _rowDay.Add(d);
                }
            }
        }
        _loading = true;
        _gm.ReadOnly = r is null;
        _gm.Load(cols, rows);
        _snap = _gm.Snapshot();
        _loading = false;
        _grid.Refresh();
        ShowRowNote();
    }

    private string Disp(int r, int c, string raw)
    {
        if (raw.Length == 0 || c < 3 || c > 8) return raw;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return raw;
        return c is 7 or 8 ? (c == 7 ? SgnN(d) : SgnKg(d)) : c is 4 or 6 ? Kg(d) : N(d);
    }

    private IBrush? Bg(int r, int c)
    {
        if (r >= _rowItem.Count) return null;
        var it = _rowItem[r];
        if (it is null) return _rowDay[r] is { } d && (d.HasWarn || d.CoLech) ? WarnDayBg : DayBg;
        if (c is 5 or 6 && Svc.EditOf(_pid, "xuong", it.Ngay, it.Ma, c == 5 ? "sl" : "net") is not null) return EditedBg;
        if (c is 3 or 4 && Svc.EditOf(_pid, "kho", it.Ngay, it.Ma, c == 3 ? "sl" : "net") is not null) return EditedBg;
        if (c == 9) return it.Status switch { "lech" => LechBg, "chi_kho" => ChiKhoBg, "chi_xuong" => ChiXuongBg, "khop" => KhopBg, "zero" => ZeroBg, _ => null };
        if (c == 10 && _gm.Get(r, 10).StartsWith('⇄')) return MoveBg;
        if (c is 7 or 8 && it.Status == "lech") return LechBg;
        return null;
    }

    private void ShowRowNote()
    {
        var r = _grid.Row;
        if (r < 0 || r >= _rowItem.Count || _rowItem[r] is not { } it) { _note.Text = ""; return; }
        var n = Svc.NoteOf(_pid, it);
        var rows = new List<string>();
        if (it.KRows.Count > 0) rows.Add("Kho: dòng " + string.Join(", ", it.KRows.Take(12)));
        if (it.XRows.Count > 0) rows.Add("Xưởng: dòng " + string.Join(", ", it.XRows.Take(12)));
        _note.Text = $"{it.Ma} · {Dmy(it.Ngay)} — " + (n.Tip.Length > 0 ? n.Tip : StatusText.GetValueOrDefault(it.Status, "")) + (rows.Count > 0 ? "   (" + string.Join(" · ", rows) + ")" : "");
    }

    private List<Item> SelectedItems()
    {
        var s = _grid.Selection; var l = new List<Item>();
        for (var r = s.R1; r <= s.R2 && r < _rowItem.Count; r++) if (_rowItem[r] is { } it && !l.Contains(it)) l.Add(it);
        return l;
    }

    private async void OnGridChanged()
    {
        var now = _gm.Snapshot();
        var ops = new List<Op>(); var edits = new List<(string side, Item it, string field, double v)>(); var bad = 0;
        for (var r = 0; r < Math.Min(now.Count, _rowItem.Count); r++)
            for (var c = 0; c < now[r].Length; c++)
            {
                var before = r < _snap.Count && c < _snap[r].Length ? _snap[r][c] : "";
                if (now[r][c] == before) continue;
                var it = _rowItem[r];
                if (it is null || it.TheoSize || c is not (3 or 4 or 5 or 6)) { bad++; continue; }
                if (!Ui.TryParseNumber(now[r][c], out var v) || v < 0) { bad++; continue; }
                v = R6(v);
                var linked = Svc.IsLinked(P);
                if (c == 3 && linked)
                {
                    if (!_edit) { bad++; continue; }
                    var cur = Svc.Movable(P, it.Ngay, it.Ma);
                    if (Math.Abs(cur - v) < 1e-9) continue;
                    if (cur <= 0)
                    {
                        var lk = Svc.LookupMa(_pid, it.Ma);
                        var tl = lk.Tl ?? (it.XSl is > 0 && it.XNet is > 0 ? R6(it.XNet!.Value / it.XSl!.Value) : null);
                        ops.Add(new Op { Type = "add", Slot = _pid, Ngay = it.Ngay, Ma = it.Ma, Ten = lk.Ten.Length > 0 ? lk.Ten : it.Ten, Sl = v, Tl = tl });
                    }
                    else if (v == 0) ops.Add(new Op { Type = "delete", Slot = _pid, Ngay = it.Ngay, Ma = it.Ma });
                    else ops.Add(new Op { Type = "setsl", Slot = _pid, Ngay = it.Ngay, Ma = it.Ma, Target = v });
                }
                else if (c == 4 && linked) bad++;
                else edits.Add((c <= 4 ? "kho" : "xuong", it, c is 3 or 5 ? "sl" : "net", v));
            }
        if (ops.Count == 0 && edits.Count == 0) { RenderGrid(); if (bad > 0) _status.Text = "Ô này không sửa được (dòng tổng, cột tính sẵn, Net Kho của file đã liên kết, hoặc không phải số) — đã bỏ qua."; return; }
        if (ops.Count > 0)
        {
            var desc = ops.Count == 1 ? Describe(ops[0]) : $"Sửa số lượng {ops.Count} dòng ở {DoiChieuService.KN(_pid)}";
            if (!await Ui.Confirm(this, desc + "\n\nGhi thẳng vào file Excel " + DoiChieuService.KN(_pid) + " (đã sao lưu, hoàn tác được ở tab Lịch sử).", "Ghi vào file Excel", "Ghi"))
            { RenderGrid(); return; }
            await Act(() => Svc.ApplyOps(ops, desc).Message);
        }
        if (edits.Count > 0) await Act(() => string.Join("  ", edits.Select(e => Svc.ApplyEdit(_pid, e.side, e.it.Ngay, e.it.Ma, e.field, e.v)).Where(m => m.Length > 0)));
        if (bad > 0) _status.Text += "  (Có ô không sửa được — đã bỏ qua.)";
    }

    private static string Describe(Op o) => o.Type switch
    {
        "setsl" => $"Đổi SL {o.Ma} ngày {Dmy(o.Ngay)} ở {DoiChieuService.KN(o.Slot)} thành {N(o.Target)}",
        "delete" => $"Xóa {o.Ma} ngày {Dmy(o.Ngay)} khỏi {DoiChieuService.KN(o.Slot)}" + (o.Sl is not null ? $" ({N(o.Sl)})" : ""),
        "add" => $"Thêm {o.Ma} ngày {Dmy(o.Ngay)} vào {DoiChieuService.KN(o.Slot)}: SL {N(o.Sl)}" + (o.Tl is > 0 ? $" × {N(o.Tl)} kg" : ""),
        "move" => $"Chuyển {N(o.Sl)} {o.Ma} ngày {Dmy(o.Ngay)} từ {DoiChieuService.KN(o.From)} sang {DoiChieuService.KN(o.To)}",
        "retag" => $"Sửa {o.Ma} ngày {Dmy(o.Ngay)} ở {DoiChieuService.KN(o.Slot)} → " + string.Join(", ", new[] { o.NewMa is not null ? "mã " + o.NewMa : null, o.NewNgay is not null ? "ngày " + Dmy(o.NewNgay) : null }.Where(x => x is not null)),
        _ => o.Type,
    };

    private bool NeedLinked(out string why)
    {
        why = !Svc.IsLinked(P) ? $"Cần liên kết file Excel {DoiChieuService.KN(_pid)} (đọc được đủ cột) mới ghi được." : "";
        return why.Length == 0;
    }

    private async Task AddSizeCol()
    {
        string msg;
        try { msg = Svc.AddSizeColumn(_pid, false, true).Message; } catch (Exception e) { await Ui.Message(this, e.Message); return; }
        if (!await Ui.Confirm(this, DoiChieuService.KN(_pid) + ": " + msg + "\n\nGhi vào file Excel đã liên kết? (file gốc được sao lưu trước khi ghi; cột chỉ chứa chữ, không đổi công thức.)", "Thêm cột Size", "Ghi file")) return;
        await Act(() => Svc.AddSizeColumn(_pid, true).Message);
    }
    private async Task MoveSelected()
    {
        var items = SelectedItems().Where(i => !i.TheoSize && Svc.Movable(P, i.Ngay, i.Ma) > 0).ToList();
        if (items.Count == 0) { await Ui.Message(this, "Chọn (bôi đen) các dòng có số lượng ở file Kho đang xem rồi bấm lại."); return; }
        var to = DoiChieuService.OtherId(_pid);
        if (!Svc.IsLinked(P) || !Svc.IsLinked(Svc.PairOf(to))) { await Ui.Message(this, "Cần liên kết đủ 2 file Kho (Xuất thay thùng và Xuất rã đông) mới chuyển được."); return; }
        var ops = items.Select(it =>
        {
            var vt = Svc.ViTri(_pid, it);
            var sl = vt is { Kind: "chuyen" } && vt.To == to && vt.Qty > 0 ? vt.Qty : Svc.Movable(P, it.Ngay, it.Ma);
            return new Op { Type = "move", From = _pid, To = to, Ngay = it.Ngay, Ma = it.Ma, Sl = sl };
        }).ToList();
        var txt = string.Join("\n", ops.Take(15).Select(Describe)) + (ops.Count > 15 ? $"\n… và {ops.Count - 15} dòng nữa" : "");
        if (!await Ui.Confirm(this, txt + "\n\nGhi vào cả 2 file Excel (ghi cả hai hoặc không file nào).", "Chuyển mã", "Chuyển")) return;
        await Act(() => Svc.ApplyOps(ops, ops.Count == 1 ? Describe(ops[0]) : $"Chuyển {ops.Count} mã từ {DoiChieuService.KN(_pid)} sang {DoiChieuService.KN(to)}").Message);
    }

    private async Task DeleteSelected()
    {
        if (!NeedLinked(out var why)) { await Ui.Message(this, why); return; }
        var items = SelectedItems().Where(i => !i.TheoSize && Svc.Movable(P, i.Ngay, i.Ma) > 0).ToList();
        if (items.Count == 0) { await Ui.Message(this, "Chọn (bôi đen) các dòng có số lượng ở file Kho rồi bấm lại."); return; }
        var ops = items.Select(i => new Op { Type = "delete", Slot = _pid, Ngay = i.Ngay, Ma = i.Ma }).ToList();
        if (!await Ui.Confirm(this, string.Join("\n", ops.Take(15).Select(Describe)) + "\n\nXóa các dòng này khỏi file Excel " + DoiChieuService.KN(_pid) + "? (Hoàn tác được.)", "Xóa khỏi file Kho", "Xóa")) return;
        await Act(() => Svc.ApplyOps(ops, ops.Count == 1 ? Describe(ops[0]) : $"Xóa {ops.Count} mã khỏi {DoiChieuService.KN(_pid)}").Message);
    }

    private async Task Retag()
    {
        if (!NeedLinked(out var why)) { await Ui.Message(this, why); return; }
        var it = SelectedItems().FirstOrDefault(i => !i.TheoSize && Svc.Movable(P, i.Ngay, i.Ma) > 0);
        if (it is null) { await Ui.Message(this, "Chọn 1 dòng có số lượng ở file Kho (mã sai / ngày sai) rồi bấm lại."); return; }
        var guess = it.GoiY is { Count: > 0 } ? it.GoiY[0].Ma : it.Ma;
        var ma = await Ui.Prompt(this, $"Mã đúng cho {it.Ma} (ngày {Dmy(it.Ngay)}, SL {N(Svc.Movable(P, it.Ngay, it.Ma))}):", "Sửa mã / ngày", guess);
        if (ma is null) return;
        var ngayTxt = await Ui.Prompt(this, "Ngày đúng (dd/mm/yyyy):", "Sửa mã / ngày", Dmy(it.Ngay));
        if (ngayTxt is null) return;
        var ngay = ParseDate(ngayTxt.Trim());
        if (ngay is null) { await Ui.Message(this, $"“{ngayTxt}” không phải ngày hợp lệ."); return; }
        var newMa = NormMa(ma) == it.Ma ? null : ma.Trim();
        var newNgay = ngay == it.Ngay ? null : ngay;
        if (newMa is null && newNgay is null) return;
        var op = new Op { Type = "retag", Slot = _pid, Ngay = it.Ngay, Ma = it.Ma, NewMa = newMa, NewNgay = newNgay };
        await Act(() => Svc.ApplyOps(new() { op }, Describe(op)).Message);
    }

    private async Task AddRow()
    {
        if (!NeedLinked(out var why)) { await Ui.Message(this, why); return; }
        var src = SelectedItems().FirstOrDefault();
        var dlg = new AddRowDialog(_pid, src);
        await Ui.Show(this, dlg);
        if (dlg.Result is not { } op) return;
        await Act(() => Svc.ApplyOps(new() { op }, Describe(op)).Message);
    }

    private async Task UndoLast()
    {
        var e = Svc.St.Log.LastOrDefault();
        if (e is null) { await Ui.Message(this, "Chưa có lần ghi file nào để hoàn tác."); return; }
        if (!await Ui.Confirm(this, $"Hoàn tác: {e.Desc}\n({e.At} · {e.By})", "Hoàn tác", "Hoàn tác")) return;
        await Act(() => Svc.UndoLog(e.Id).Message);
    }

    private async Task Export()
    {
        var path = await Ui.SaveFile(this, "Xuất Excel kết quả đối chiếu", $"Doi chieu hang ngay {DateTime.Now:yyyy-MM-dd}.xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Act(() => { Svc.Export(path, new[] { 1, 2 }); return "Đã xuất " + Path.GetFileName(path); })) Ui.OpenExternal(path);
    }

    // =====================================================================================
    // Tab Liên kết file
    // =====================================================================================

    private void RenderLinks()
    {
        _linkPanel.Children.Clear();
        if (Svc.Needs.Count > 0)
        {
            var warn = new StackPanel { Spacing = 4 };
            warn.Children.Add(Label("⚠ Có file chưa nhận ra cột — chọn cột để app đọc được:", true));
            foreach (var n in Svc.Needs.ToList())
            {
                var row = new WrapPanel { ItemSpacing = 6 };
                row.Children.Add(Label("• " + n.Label + ": " + n.Map.Reason));
                row.Children.Add(Btn("Chọn cột…", async () => await AskNeed(n), primary: true));
                warn.Children.Add(row);
            }
            _linkPanel.Children.Add(Card(warn, "#FFF4E5"));
        }
        foreach (var slot in new[] { "kho1", "kho2", "xuong" }) _linkPanel.Children.Add(LinkCard(slot));

        var bk = new StackPanel { Spacing = 6 };
        bk.Children.Add(Label("Sao lưu trước khi ghi", true, 13.5));
        bk.Children.Add(Label($"Mỗi ngày, lần ghi đầu tiên vào mỗi file Kho được sao lưu 1 bản (giữ {DoiChieuService.KeepDays} ngày) tại: {Svc.BackupDir}"));
        var bb = new WrapPanel { ItemSpacing = 6 };
        bb.Children.Add(Btn("Mở thư mục sao lưu", () => { try { Directory.CreateDirectory(Svc.BackupDir); } catch { } Ui.OpenExternal(Svc.BackupDir); }));
        bb.Children.Add(Btn("Đổi thư mục…", async () =>
        {
            var top = TopLevel.GetTopLevel(this); if (top is null) return;
            var f = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Chọn thư mục sao lưu", AllowMultiple = false });
            var p = f.Count > 0 ? f[0].TryGetLocalPath() : null;
            if (p is not null) await Act(() => { Svc.SetBackupDir(p); return "Đã đổi thư mục sao lưu."; });
        }));
        bk.Children.Add(bb);
        _linkPanel.Children.Add(Card(bk));

        if (_edit)
        {
            var g = new StackPanel { Spacing = 6 };
            g.Children.Add(Label("Nạp file gốc của phần mềm kho", true, 13.5));
            g.Children.Add(Label("Chọn MỘT file xuất của phần mềm kho (có cột Loại chứng từ: “Xuất thay bao bì” / “Xuất xả”). App tách theo loại, so từng ngày với hai file đã liên kết rồi thêm ngày mới hoặc thay ngày khác số — ghi vào cả 2 file."));
            g.Children.Add(new WrapPanel { Children = { Btn("Nạp file gốc…", async () => await LoadGoc(), primary: true) } });
            _linkPanel.Children.Add(Card(g));
        }
        if (Svc.St.Links.Count > 0)
            _linkPanel.Children.Add(new WrapPanel
            {
                Children =
                {
                    Btn("Ngắt liên kết tất cả", async () =>
                    {
                        if (!await Ui.Confirm(this, "Ngắt liên kết cả 3 file, bỏ cột đã chọn, số sửa tay và lịch sử ghi file trên máy này? (Các file Excel KHÔNG bị xóa.)", "Ngắt liên kết", "Ngắt")) return;
                        await Act(() => { Svc.UnlinkAll(); return "Đã ngắt liên kết tất cả."; });
                    }, danger: true),
                },
            });
    }

    private static Border Card(Control child, string bg = "#FFFFFF") => new()
    {
        Background = AppTheme.Brush(bg), BorderBrush = AppTheme.Brush("#D0D7E2"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10), Child = child,
    };

    private Control LinkCard(string slot)
    {
        var st = new StackPanel { Spacing = 6 };
        var lk = Svc.LinkOf(slot);
        st.Children.Add(Label(DoiChieuService.SlotLabel(slot) + (slot == "xuong" ? "  (chỉ đọc)" : "  (app ghi thẳng vào file này)"), true, 13.5));
        if (lk is null) st.Children.Add(Label("Chưa liên kết."));
        else
        {
            st.Children.Add(Label($"{lk.Name}  —  {lk.Path}  (liên kết {lk.At})"));
            string? err = slot == "xuong" ? Svc.XErr : Svc.PairOf(slot == "kho2" ? 2 : 1).LinkErr;
            if (!string.IsNullOrEmpty(err)) st.Children.Add(new TextBlock { Text = "⚠ " + err, Foreground = AppTheme.Danger, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            else if (slot != "xuong" && Svc.PairOf(slot == "kho2" ? 2 : 1) is { Kho: { } k } kp)
            {
                var res = k.Res;
                st.Children.Add(Label($"Sheet “{k.Sheet}”: {res.Rows.Count:#,0} dòng có mã · {res.Totals.Count} dòng tổng ngày · đọc lúc {k.LoadedAt}" + (Svc.IsLinked(kp) ? "" : " · CHƯA ghi được")));
                if (kp.KhoSheets.Count > 1)
                {
                    var cb = new ComboBox { ItemsSource = kp.KhoSheets, SelectedItem = k.Sheet, MinWidth = 180 };
                    cb.SelectionChanged += async (_, _) => { if (cb.SelectedItem is string s && s != k.Sheet) await Act(() => { Svc.St.KhoSheet[slot] = s; Svc.Reload(slot); return ""; }); };
                    st.Children.Add(new WrapPanel { ItemSpacing = 6, Children = { Label("Sheet dữ liệu:"), cb } });
                }
                Issues(st, res, "Kho");
            }
            else if (slot == "xuong" && Svc.Xuong is { } x)
            {
                st.Children.Add(Label($"{x.Names.Count} sheet: {string.Join(", ", x.Names)}"));
                foreach (var p in Svc.Pairs)
                {
                    var items = new List<string> { "(chưa chọn)" };
                    items.AddRange(x.Names.Select(n => { var s = p.Sug.FirstOrDefault(o => o.Sheet == n); return s.Sheet is null ? n : $"{n}  — trùng {s.Chung} mã"; }));
                    var cb = new ComboBox { ItemsSource = items, MinWidth = 260, SelectedIndex = p.XuongSheet.Length == 0 ? 0 : x.Names.IndexOf(p.XuongSheet) + 1 };
                    var pid = p.Id;
                    cb.SelectionChanged += async (_, _) =>
                    {
                        if (_loading) return;
                        var name = cb.SelectedIndex <= 0 ? "" : x.Names[cb.SelectedIndex - 1];
                        if (name == Svc.PairOf(pid).XuongSheet) return;
                        await Act(() => { Svc.SetXuongSheet(pid, name); return ""; });
                        await AskNeeds();
                    };
                    st.Children.Add(new WrapPanel { ItemSpacing = 6, Children = { Label($"Sheet đối chiếu với {DoiChieuService.KN(pid)}:"), cb } });
                    if (p.Xuong is { } xs) Issues(st, xs.Res, "Xưởng “" + xs.Sheet + "”");
                }
            }
        }
        var bar = new ToolRow { };
        bar.Children.Add(Btn(lk is null ? "Chọn file…" : "Đổi file…", async () => await PickLink(slot), primary: lk is null));
        if (lk is not null)
        {
            bar.Children.Add(Btn("Nạp lại", async () => { await Act(() => { Svc.Reload(slot); return "Đã đọc lại " + lk.Name; }); await AskNeeds(); }));
            bar.Children.Add(Btn("Mở file", () => Ui.OpenExternal(lk.Path)));
            bar.Children.Add(Btn("Ngắt", async () => await Act(() => { Svc.Unlink(slot); return "Đã ngắt liên kết " + DoiChieuService.SlotLabel(slot); })));
        }
        st.Children.Add(bar);
        return Card(st);
    }

    private static void Issues(StackPanel st, ParseResult res, string who)
    {
        var lines = new List<string>();
        foreach (var e in res.Errors.Take(8)) lines.Add($"Dòng {e.Row} ({e.Ma}): {e.Why}");
        if (res.Errors.Count > 8) lines.Add($"… và {res.Errors.Count - 8} dòng lỗi nữa");
        foreach (var c in res.Conflicts.Take(5)) lines.Add($"Dòng {c.Row}: cột Mã ghi {c.Ma} nhưng cột Size ghi {c.MaSize}");
        if (res.NoCode.Count > 0) lines.Add($"{res.NoCode.Count} dòng có số nhưng chưa có mã (SL {N(res.NoCode.Sum(x => x.Sl))})");
        if (lines.Count == 0) return;
        st.Children.Add(new TextBlock { Text = $"Cần xem ở {who}:\n• " + string.Join("\n• ", lines), FontSize = 12, Foreground = AppTheme.Brush("#8A4B00"), TextWrapping = TextWrapping.Wrap });
    }

    private async Task PickLink(string slot)
    {
        var path = await Ui.OpenFile(this, "Chọn file Excel " + DoiChieuService.SlotLabel(slot), "xlsx", "Excel");
        if (path is null) return;
        string warn = "";
        if (await Act(() => { warn = Svc.LinkFile(slot, path); return "Đã liên kết " + Path.GetFileName(path); }))
        {
            if (warn.Length > 0) await Ui.Message(this, warn, "Lưu ý");
            await AskNeeds();
        }
    }

    private async Task AskNeeds()
    {
        foreach (var n in Svc.Needs.ToList())
            if (!await AskNeed(n)) break;
    }

    /// <summary>Hộp "Chọn cột": file có cột trùng / thiếu tên → người dùng chỉ cột (hoặc dòng tiêu đề).</summary>
    private async Task<bool> AskNeed(DoiChieuService.Need need)
    {
        var dlg = new MapDialog(need);
        await Ui.Show(this, dlg);
        if (dlg.Map is null && dlg.HeaderRow is null) { RenderLinks(); return false; }
        await Act(() => { Svc.AnswerNeed(need, dlg.Map ?? new(), dlg.HeaderRow); return "Đã chọn cột cho " + need.Label; });
        return true;
    }

    private async Task LoadGoc()
    {
        var path = await Ui.OpenFile(this, "Chọn file gốc của phần mềm kho (xlsx / csv)", "xlsx;csv", "Excel / CSV");
        if (path is null) return;
        DoiChieuService.Goc? g = null;
        if (!await Ui.RunAsync(this, () => Task.Run(() => g = Svc.ReadGoc(path)))) return;
        var dlg = new GocDialog(g!);
        await Ui.Show(this, dlg);
        if (dlg.Replace is not { } replace) return;
        await Act(() => Svc.ApplyGoc(g!, replace).Message);
    }

    // =====================================================================================
    // Tab Lịch sử
    // =====================================================================================

    private void RenderHistory()
    {
        _histPanel.Children.Clear();
        _histPanel.Children.Add(Label("Các lần ghi vào file Excel Kho (mới nhất trên cùng). Hoàn tác = ghi thao tác ngược lại vào file.", true));
        if (Svc.St.Log.Count == 0) _histPanel.Children.Add(Label("Chưa có lần ghi nào."));
        foreach (var e in Enumerable.Reverse(Svc.St.Log).Take(100).ToList())
        {
            var row = new DockPanel();
            var b = Btn("↶ Hoàn tác", async () =>
            {
                if (!await Ui.Confirm(this, "Hoàn tác: " + e.Desc + "?", "Hoàn tác", "Hoàn tác")) return;
                await Act(() => Svc.UndoLog(e.Id).Message);
            });
            b.IsEnabled = _edit;
            DockPanel.SetDock(b, Dock.Right);
            row.Children.Add(b);
            row.Children.Add(Label($"{e.At} · {e.By} — {e.Desc}"));
            _histPanel.Children.Add(Card(row));
        }
        _histPanel.Children.Add(new Border { Height = 8 });
        _histPanel.Children.Add(Label("Số đã sửa tay (chỉ để đối chiếu, file Excel không bị đổi)", true));
        if (Svc.St.Edits.Count == 0) _histPanel.Children.Add(Label("Không có."));
        foreach (var e in Svc.St.Edits.OrderByDescending(x => x.At).ToList())
        {
            var row = new DockPanel();
            var b = Btn("↶ Bỏ sửa tay", async () => await Act(() => { Svc.UndoEdit(e.Id); return "Đã bỏ số sửa tay."; }));
            DockPanel.SetDock(b, Dock.Right);
            row.Children.Add(b);
            row.Children.Add(Label($"{DoiChieuService.KN(e.Pair)} · {(e.Side == "kho" ? "Kho" : "Xưởng")} · {e.Ma} {Dmy(e.Ngay)}: {(e.Field == "sl" ? "SL" : "Net")} {N(e.Before)} → {N(e.After)}  ({e.By})"));
            _histPanel.Children.Add(Card(row));
        }
    }

    // =====================================================================================
    // Hộp thoại
    // =====================================================================================

    private sealed class MapDialog : Window
    {
        public Dictionary<string, int>? Map;
        public int? HeaderRow;
        public MapDialog(DoiChieuService.Need need)
        {
            Title = "Chọn cột — " + need.Label; Width = 560; SizeToContent = SizeToContent.Height; CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = AppTheme.Card;
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock { Text = need.Map.Reason, TextWrapping = TextWrapping.Wrap });
            var ok = new Button { Content = "Đồng ý", IsDefault = true, Classes = { "primary" } };
            var no = new Button { Content = "Để sau", IsCancel = true };
            no.Click += (_, _) => Close();
            if (need.Map.HeaderRow is null)
            {
                var opts = need.Aoa.Take(30).Select((r, i) => $"Dòng {i + 1}: " + string.Join(" | ", (r ?? Array.Empty<object?>()).Select(Str).Where(s => s.Trim().Length > 0).Take(8))).ToList();
                var cb = new ComboBox { ItemsSource = opts, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                body.Children.Add(new TextBlock { Text = "Dòng tiêu đề (dòng có tên các cột):", FontWeight = FontWeight.SemiBold });
                body.Children.Add(cb);
                ok.Click += (_, _) => { HeaderRow = cb.SelectedIndex; Close(); };
            }
            else
            {
                var boxes = new Dictionary<string, ComboBox>();
                var heads = need.Map.Headers;
                foreach (var f in need.Map.Fields)
                {
                    var choices = need.Map.Choices.GetValueOrDefault(f);
                    var list = heads.Select(h => $"{ColName(h.J)} — {h.Name}" + (choices?.Contains(h.J) == true ? "  ★" : "")).ToList();
                    var cb = new ComboBox { ItemsSource = list, MinWidth = 300, SelectedIndex = choices is { Count: > 0 } ? heads.FindIndex(h => h.J == choices[0]) : -1 };
                    boxes[f] = cb;
                    body.Children.Add(new WrapPanel { ItemSpacing = 8, Children = { new TextBlock { Text = DcxEngine.Label.GetValueOrDefault(f, f) + ":", Width = 110, VerticalAlignment = VerticalAlignment.Center }, cb } });
                }
                var err = new TextBlock { Foreground = AppTheme.Danger, TextWrapping = TextWrapping.Wrap };
                body.Children.Add(err);
                ok.Click += (_, _) =>
                {
                    if (boxes.Values.Any(b => b.SelectedIndex < 0)) { err.Text = "Còn cột chưa chọn."; return; }
                    Map = boxes.ToDictionary(kv => kv.Key, kv => heads[kv.Value.SelectedIndex].J);
                    Close();
                };
            }
            body.Children.Add(Ui.Buttons(no, ok));
            Content = new Border { Padding = new Thickness(22, 18), Child = body };
        }
        private static string ColName(int j) => DcxXlsx.ColName(j + 1);
    }

    private sealed class AddRowDialog : Window
    {
        public Op? Result;
        public AddRowDialog(int pid, Item? src)
        {
            Title = "Thêm dòng vào " + DoiChieuService.KN(pid); Width = 520; SizeToContent = SizeToContent.Height; CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = AppTheme.Card;
            var ngay = new TextBox { Text = src is not null ? Dmy(src.Ngay) : DateTime.Today.ToString("dd/MM/yyyy"), Width = 120 };
            var ma = new AutoCompleteBox { Text = src is { TheoSize: false } ? src.Ma : "", Width = 300, FilterMode = AutoCompleteFilterMode.ContainsOrdinal, ItemsSource = Svc.CodeCandidates(), MinimumPrefixLength = 2 };
            var ten = new TextBox { Width = 300 };
            var sl = new TextBox { Text = src?.XSl is > 0 && src.Status == "chi_xuong" ? Inv(src.XSl) : "", Width = 120 };
            var tl = new TextBox { Width = 120, Watermark = "kg / đơn vị" };
            var info = new TextBlock { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            var err = new TextBlock { Foreground = AppTheme.Danger, TextWrapping = TextWrapping.Wrap };
            void Lookup()
            {
                var m = (ma.Text ?? "").Trim(); if (m.Length == 0) { info.Text = ""; return; }
                var lk = Svc.LookupMa(pid, m);
                if (string.IsNullOrWhiteSpace(ten.Text) && lk.Ten.Length > 0) ten.Text = lk.Ten;
                if (string.IsNullOrWhiteSpace(tl.Text) && lk.Tl is > 0) tl.Text = Inv(lk.Tl);
                info.Text = (lk.InList ? "Mã có trong Danh sách mã." : "⚠ Mã chưa có trong Danh sách mã (kiểm tra lại cho đúng).")
                    + (lk.SrcTen.Length > 0 ? $" Tên lấy từ {lk.SrcTen}." : "") + (lk.SrcTl.Length > 0 ? $" Trọng lượng lấy từ {lk.SrcTl}." : " Chưa có trọng lượng — Net để trống.");
            }
            ma.LostFocus += (_, _) => Lookup();
            ma.SelectionChanged += (_, _) => Lookup();
            if (src is { TheoSize: false }) Lookup();
            if (string.IsNullOrWhiteSpace(tl.Text) && src?.XSl is > 0 && src.XNet is > 0) tl.Text = Inv(R6(src.XNet!.Value / src.XSl!.Value));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), RowSpacing = 6 };
            void Row(int r, string l, Control c) { var t = new TextBlock { Text = l, VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(t, r); Grid.SetRow(c, r); Grid.SetColumn(c, 1); grid.Children.Add(t); grid.Children.Add(c); }
            Row(0, "Ngày", ngay); Row(1, "Mã hàng", ma); Row(2, "Tên hàng", ten); Row(3, "Số lượng", sl); Row(4, "Trọng lượng", tl);
            var ok = new Button { Content = "Ghi vào file", IsDefault = true, Classes = { "primary" } };
            var no = new Button { Content = "Đóng", IsCancel = true };
            no.Click += (_, _) => Close();
            ok.Click += (_, _) =>
            {
                var d = ParseDate((ngay.Text ?? "").Trim());
                if (d is null) { err.Text = "Ngày không hợp lệ (dd/mm/yyyy)."; return; }
                var m = (ma.Text ?? "").Trim(); if (NormMa(m).Length == 0) { err.Text = "Chưa nhập mã hàng."; return; }
                if (!Ui.TryParseNumber(sl.Text, out var s) || !(s > 0)) { err.Text = "Số lượng phải lớn hơn 0."; return; }
                if (!Ui.TryParseNumber(tl.Text, out var w) || w < 0) { err.Text = "Trọng lượng không phải số."; return; }
                Result = new Op { Type = "add", Slot = pid, Ngay = d, Ma = CleanMa(m), Ten = (ten.Text ?? "").Trim(), Sl = R6(s), Tl = w > 0 ? R6(w) : null };
                Close();
            };
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock { Text = "Dòng mới được thêm vào đúng khối ngày (chưa có ngày đó → tạo khối mới + dòng tổng ngày). Net = Số lượng × Trọng lượng.", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
            body.Children.Add(grid); body.Children.Add(info); body.Children.Add(err);
            body.Children.Add(Ui.Buttons(no, ok));
            Content = new Border { Padding = new Thickness(22, 18), Child = body };
        }
    }

    private sealed class GocDialog : Window
    {
        public bool? Replace;
        public GocDialog(DoiChieuService.Goc g)
        {
            Title = "Nạp file gốc — " + g.FileName; Width = 720; Height = 560; CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Background = AppTheme.Card;
            var p = g.Plan;
            var head = new StackPanel { Spacing = 4 };
            head.Children.Add(new TextBlock { Text = $"{g.NRows:#,0} dòng · Xuất thay bao bì {g.Split.Thay.Count:#,0} dòng · Xuất xả {g.Split.RaDong.Count:#,0} dòng" + (g.Split.KhongLoai > 0 ? $" · {g.Split.KhongLoai} dòng không rõ loại (bỏ qua)" : ""), TextWrapping = TextWrapping.Wrap });
            head.Children.Add(new TextBlock { Text = $"{p.NMoi} ngày mới · {p.NGiong} ngày giống hệt · {p.NKhac} ngày khác số" + (p.NSai > 0 ? $" · {p.NSai} ngày sai tháng" : ""), FontWeight = FontWeight.SemiBold });
            if (g.Wrong.Count > 0) head.Children.Add(new TextBlock { Text = $"⚠ Bỏ qua {g.Wrong.Count} ngày không thuộc {MonthLabel(g.LinkedMonth)}: " + string.Join(", ", g.Wrong.Select(Dmy)), Foreground = AppTheme.Danger, TextWrapping = TextWrapping.Wrap });
            var list = new StackPanel { Spacing = 2 };
            foreach (var d in p.Days)
            {
                var st = d.State switch { "moi" => "MỚI", "giong" => "giống", "khac" => "KHÁC", _ => "sai tháng" };
                var t = $"{Dmy(d.Ngay)}  [{st}]  Thay thùng {d.Thay.N} mã · {N(d.Thay.Sl)} · {Kg(d.Thay.Net)} kg  |  Rã đông {d.RaDong.N} mã · {N(d.RaDong.Sl)} · {Kg(d.RaDong.Net)} kg";
                if (d.State == "khac" && d.Diffs.Count > 0) t += "\n      " + string.Join("; ", d.Diffs.Take(6).Select(x => $"{x.Ma}: {N(x.CurSl)} → {N(x.NewSl)}")) + (d.Diffs.Count > 6 ? $"; … +{d.Diffs.Count - 6}" : "");
                list.Children.Add(new TextBlock { Text = t, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = d.State switch { "moi" => Brushes.DarkGreen, "khac" => AppTheme.Brush("#9C5700"), "giong" => AppTheme.Muted, _ => AppTheme.Danger } });
            }
            var add = new Button { Content = $"Thêm {p.NMoi} ngày mới", Classes = { "primary" }, IsEnabled = p.NMoi > 0 };
            var rep = new Button { Content = $"Thay {p.NKhac} ngày khác số", IsEnabled = p.NKhac > 0 };
            var no = new Button { Content = "Đóng", IsCancel = true };
            add.Click += (_, _) => { Replace = false; Close(); };
            rep.Click += (_, _) => { Replace = true; Close(); };
            no.Click += (_, _) => Close();
            var dock = new DockPanel();
            var btns = Ui.Buttons(no, rep, add);
            DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(btns, Dock.Bottom);
            dock.Children.Add(head); dock.Children.Add(btns);
            dock.Children.Add(new ScrollViewer { Content = list, Margin = new Thickness(0, 8, 0, 0) });
            Content = new Border { Padding = new Thickness(22, 18), Child = dock };
        }
    }
}

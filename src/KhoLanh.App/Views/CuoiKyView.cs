using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.CuoiKy;
using KhoLanh.Core.Grid;
using static KhoLanh.Core.CuoiKy.Ck;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Số liệu cuối kỳ (M07 "Đối chiếu kế toán - kho" của app cũ, viết bằng C#): đối chiếu TỒN CUỐI KỲ theo mã (kg) —
/// Tồn kho = Tồn An An + Tồn kho gửi; Chênh lệch = Tồn kho − Kế toán. Mỗi mã lệch có nguyên nhân (xác định / nghi ngờ + độ tin cậy),
/// gợi ý mã nhầm, chứng từ nghi ngờ; người dùng xác nhận, sửa mã / điều chỉnh, hoàn tác. Dữ liệu dùng chung database "so-lieu-cuoi-ky".
/// </summary>
public sealed class CuoiKyView : UserControl, IPage
{
    public string Title => "Số liệu cuối kỳ";
    public string Subtitle => "Đối chiếu tồn cuối kỳ kho (An An + Kho gửi) với kế toán theo mã, kg — tìm nguyên nhân lệch, gợi ý mã nhầm, chứng từ nghi ngờ.";

    private static CkService Svc => App.Services.CuoiKy;
    private static IBrush B(string c) => AppTheme.Brush(c);
    private static readonly IBrush Line_ = B("#D0D7E2"), Dark = B("#0B2A45"), Red = B("#B71C1C"), Green = B("#1B5E20"), Amber = B("#8A5A00");
    private static readonly IBrush BgDiff = B("#FDECEA"), BgDone = B("#E8F5E9"), BgGroup = B("#FFF8E1");

    private readonly CalendarDatePicker _from = DatePick(), _to = DatePick();
    private readonly StackPanel _srcPanel = new() { Spacing = 8 };
    private readonly WrapPanel _kpi = new() { ItemSpacing = 8, LineSpacing = 6 };
    private readonly WrapPanel _chips = new() { ItemSpacing = 4, LineSpacing = 4 };
    private readonly TextBox _q = new() { Watermark = "Tìm mã / tên hàng…", Width = 220, VerticalAlignment = VerticalAlignment.Top };
    private readonly GridModel _m = new() { ReadOnly = true };
    private readonly SheetGrid _g;
    private readonly StackPanel _detail = new() { Spacing = 6 };
    private readonly StackPanel _checks = new() { Spacing = 2 };
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TabControl _tabs = new();
    private readonly StackPanel _hist = new() { Spacing = 2 };
    private readonly DispatcherTimer _sync = new() { Interval = TimeSpan.FromSeconds(8) };
    private List<CkItem> _view = new();
    private string _filter = "lech", _sel = "";
    private bool _loading;

    private static readonly (string k, string t)[] Filters =
    {
        ("all", "Tất cả"), ("lech", "Đang lệch"), ("khop", "Khớp"), ("thieu", "Thiếu"), ("thua", "Thừa"), ("saima", "Nghi sai mã"), ("quycach", "Quy cách"),
        ("ct", "Có chứng từ nghi ngờ"), ("chua", "Chưa xác định"), ("xong", "Đã xử lý"), ("conlai", "Chưa xử lý"),
    };

    public CuoiKyView()
    {
        _g = new SheetGrid(_m) { SpareRows = 0, CellBackground = (r, _) => r < _view.Count ? (_view[r].StatusUi == "xong" ? BgDone : _view[r].Status == "diff" ? (_view[r].Group.Length > 0 ? BgGroup : BgDiff) : null) : null };
        _g.SelectionChanged += () => { if (_g.Row >= 0 && _g.Row < _view.Count) { _sel = _view[_g.Row].Ma; RenderDetail(); } };
        var deb = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; deb.Tick += (_, _) => { deb.Stop(); RenderList(); };
        _q.TextChanged += (_, _) => { deb.Stop(); deb.Start(); };
        _from.SelectedDateChanged += (_, _) => PeriodChanged(); _to.SelectedDateChanged += (_, _) => PeriodChanged();
        _sync.Tick += (_, _) => SyncTick();

        var bar = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        bar.Children.Add(Lb("Kỳ đối chiếu từ")); bar.Children.Add(_from); bar.Children.Add(Lb("đến")); bar.Children.Add(_to);
        bar.Children.Add(Btn("▶ Đối chiếu", Run, "Tính lại đối chiếu với các file đã nạp", true));
        bar.Children.Add(Btn("⟳ Tải lại", Reload, "Đọc lại dữ liệu kỳ trên database dùng chung (máy khác vừa sửa)"));
        if (Svc.CanExport) bar.Children.Add(Btn("⬇ Xuất Excel (10 sheet)", Export));
        if (Svc.CanEdit)
        {
            bar.Children.Add(Btn("Nạp file điều chỉnh…", ImportAdj, "File Excel: Mã hàng · Điều chỉnh (kg) · Lý do · Sửa ở (Kho / Kế toán)"));
            bar.Children.Add(Btn("Thiết lập…", Settings));
            bar.Children.Add(Btn("🗑 Xóa dữ liệu kỳ", ClearAll));
        }
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6) }; top.Children.Add(bar); top.Children.Add(_status);

        var left = new ScrollViewer { Content = _srcPanel, Width = 290, Padding = new Thickness(0, 0, 8, 0) };
        _srcPanel.Children.Add(new TextBlock());
        var filterRow = new DockPanel(); DockPanel.SetDock(_q, Dock.Right); filterRow.Children.Add(_q); filterRow.Children.Add(_chips);
        var listTop = new StackPanel { Spacing = 6 }; listTop.Children.Add(_kpi); listTop.Children.Add(filterRow);
        var listPane = new DockPanel(); DockPanel.SetDock(listTop, Dock.Top); listPane.Children.Add(listTop); listPane.Children.Add(Framed(_g, new Thickness(0, 6, 0, 0)));
        var right = new ScrollViewer { Content = _detail, Padding = new Thickness(10, 0, 6, 0) };
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,3*,6,2*") };
        split.Children.Add(left); Grid.SetColumn(listPane, 1); split.Children.Add(listPane);
        var gs = new GridSplitter { Background = Brushes.Transparent }; Grid.SetColumn(gs, 2); split.Children.Add(gs);
        Grid.SetColumn(right, 3); split.Children.Add(right);

        _tabs.ItemsSource = new[]
        {
            new TabItem { Header = "Đối chiếu", Content = split },
            new TabItem { Header = "Lịch sử xử lý", Content = new ScrollViewer { Content = _hist } },
            new TabItem { Header = "Kiểm tra dữ liệu", Content = new ScrollViewer { Content = _checks } },
        };
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(_tabs);
        Content = root;
        AttachedToVisualTree += (_, _) => { Boot(); _sync.Start(); };
        DetachedFromVisualTree += (_, _) => _sync.Stop();
    }

    // ------------------------------------------------------------ tiện ích giao diện
    private static CalendarDatePicker DatePick() => new() { Width = 130, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy", Watermark = "dd/mm/yyyy" };
    private static TextBlock Lb(string t) => new() { Text = t, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock Muted(string t) => new() { Text = t, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private static TextBlock H(string t) => new() { Text = t, FontSize = 13.5, FontWeight = FontWeight.Bold, Foreground = Dark, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private static Border Framed(Control c, Thickness? m = null) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = c, Margin = m ?? default };
    private static Border Card(Control c, IBrush? bg = null) => new() { Background = bg ?? AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), Child = c };
    private static Button Btn(string text, Action click, string? tip = null, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
    private static Button Btn(string text, Func<Task> click, string? tip = null, bool primary = false) => Btn(text, () => { _ = click(); }, tip, primary);
    private static string F(double? n) => n is { } v ? Vn(v) : "";
    private static DateTime? D(string? iso) => DateTime.TryParseExact(iso ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    private static string Iso(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    // ------------------------------------------------------------ nạp / đồng bộ
    private void Boot()
    {
        if (!Svc.CanView) { Content = Muted("Tài khoản chưa được cấp quyền xem Số liệu cuối kỳ."); return; }
        try { Svc.Load(); } catch (Exception e) { _status.Text = "Không đọc được dữ liệu kỳ: " + e.Message; }
        _loading = true; _from.SelectedDate = D(Svc.Period?.From); _to.SelectedDate = D(Svc.Period?.To); _loading = false;
        RenderSources();
        if (Svc.Ready) RunSilent(); else RenderAll();
        if (Svc.SavedAt.Length > 0) _status.Text = $"Dữ liệu kỳ trên database: lưu lần cuối {When(Svc.SavedAt)}" + (Svc.SavedBy.Length > 0 ? " bởi " + Svc.SavedBy : "") + ". Máy khác sửa sẽ tự cập nhật.";
    }

    private static string When(string iso) => DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime().ToString("HH:mm dd/MM/yyyy") : iso;

    private void SyncTick()
    {
        try
        {
            var p = Svc.Peek();
            if (p.Rev == Svc.Rev || p.Rev == 0) return;
            if (p.Deleted) { Svc.Load(); RenderSources(); RenderAll(); _status.Text = $"Dữ liệu kỳ vừa bị xóa trên database ({p.By})."; return; }
            if (p.Stamp == Svc.CurrentStamp) { Svc.ReloadState(); Svc.Result?.Overlay(Svc.State); RenderAll(); }
            else { Svc.Load(); RenderSources(); if (Svc.Ready) RunSilent(); }
            _status.Text = $"Đã cập nhật thay đổi của {(p.By.Length > 0 ? p.By : "máy khác")} lúc {When(p.At)}.";
        }
        catch { /* mất kết nối tạm thời: bỏ qua lần này */ }
    }

    private async void Reload() { if (await Ui.Run(this, () => Svc.Load())) { RenderSources(); if (Svc.Ready) RunSilent(); else RenderAll(); _status.Text = "Đã tải lại dữ liệu kỳ."; } }

    private async void PeriodChanged()
    {
        if (_loading || _from.SelectedDate is null || _to.SelectedDate is null) return;
        await Ui.Run(this, () => Svc.SetPeriod(Iso(_from.SelectedDate), Iso(_to.SelectedDate)));
        RenderSources(); if (Svc.Ready) RunSilent();
    }

    private async void Run() { if (await Ui.Run(this, () => Svc.Run())) { RenderAll(); _status.Text = $"Đã đối chiếu {Svc.Result!.Kpi.Tong} mã."; } }
    private void RunSilent() { try { Svc.Run(); } catch (Exception e) { _status.Text = e.Message; } RenderAll(); }

    // ------------------------------------------------------------ cột nguồn
    private void RenderSources()
    {
        _srcPanel.Children.Clear();
        _srcPanel.Children.Add(H("Dữ liệu nguồn"));
        _srcPanel.Children.Add(Muted("Nạp file Excel báo cáo; file gốc chỉ đọc, không bao giờ bị ghi. Mọi máy dùng chung dữ liệu kỳ này."));
        foreach (var k in CkConfig.SourceOrder)
        {
            var spec = CkConfig.Sources[k]; var f = Svc.Files.GetValueOrDefault(k);
            var s = new StackPanel { Spacing = 3 };
            s.Children.Add(new TextBlock { Text = spec.Title + (k == "gui" ? " (không bắt buộc)" : ""), FontWeight = FontWeight.Bold, Foreground = Dark });
            if (f is null) s.Children.Add(Muted(spec.Hint.Length > 0 ? spec.Hint : "Chưa nạp."));
            else
            {
                s.Children.Add(new TextBlock { Text = "📄 " + f.FileName, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
                s.Children.Add(Muted($"Sheet {f.Sheet} · {f.Rows.Count:#,0} dòng · {Vn(f.Rows.Sum(x => x.Kg))} kg" + (f.LoadedAt.Length > 0 ? " · nạp " + When(f.LoadedAt) : "")));
                var pc = CkImport.CheckPeriod(f.Period ?? f.PeriodGuess, Svc.Period, k);
                s.Children.Add(new TextBlock { Text = (pc.State == "ok" ? "✔ " : pc.State == "khac" ? "⚠ " : "• ") + pc.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = pc.State == "ok" ? Green : pc.State == "khac" ? Amber : AppTheme.Muted });
                if (k == "ketoan") s.Children.Add(Muted(f.Ct is { } ct ? $"Chứng từ: {ct.Vouchers.Count:#,0} dòng (sheet {ct.Sheet})" : "Không có sheet chứng từ" + (f.CtError.Length > 0 ? ": " + f.CtError : "")));
                if (k == "ketoan" && f.DanhMuc is { } dm) s.Children.Add(Muted($"Danh mục thành phẩm: {dm.Count:#,0} mã"));
                if (f.Issues.Count > 0)
                {
                    var exp = new Expander { Header = $"{f.Stats.Issues} vấn đề khi đọc", FontSize = 12, Padding = new Thickness(4) };
                    var iss = new StackPanel(); foreach (var i in f.Issues.Take(60)) iss.Children.Add(new TextBlock { Text = (i.Row > 0 ? $"Dòng {i.Row}: " : "") + i.Msg, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = i.Sev == "loi" ? Red : AppTheme.Text });
                    exp.Content = iss; s.Children.Add(exp);
                }
            }
            if (Svc.CanEdit)
            {
                var bs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                var kk = k; bs.Children.Add(Btn(f is null ? "Chọn file…" : "Thay file…", async () => await LoadFile(kk)));
                if (f is not null) bs.Children.Add(Btn("Bỏ", async () => { if (await Ui.Confirm(this, $"Bỏ file {spec.Title} khỏi kỳ đối chiếu?", yes: "Bỏ")) { await Ui.Run(this, () => Svc.RemoveFile(kk)); RenderSources(); if (Svc.Ready) RunSilent(); else RenderAll(); } }));
                s.Children.Add(bs);
            }
            _srcPanel.Children.Add(Card(s));
        }
    }

    private async Task LoadFile(string kind, Dictionary<string, int>? manual = null, string? path = null, string? sheet = null)
    {
        path ??= await Ui.OpenFile(this, "Chọn file " + CkConfig.Sources[kind].Title, "xlsx;xlsm;xls;csv", "Excel");
        if (path is null) return;
        CkSource? res = null;
        _status.Text = "Đang đọc " + Path.GetFileName(path) + "…";
        if (!await Ui.RunAsync(this, async () => res = await Task.Run(() => Svc.LoadFile(kind, path, manual, sheet)))) { _status.Text = ""; return; }
        if (res is { Ok: false, NeedMap: true }) { var m = await MapDialog(kind, res); if (m is not null) await LoadFile(kind, m, path, res.Sheet.Length > 0 ? res.Sheet : null); return; }
        if (res is { Ok: false }) { await Ui.Message(this, res.Error, "Không đọc được file"); _status.Text = ""; return; }
        _status.Text = $"Đã nạp {res!.FileName}: {res.Rows.Count:#,0} dòng" + (res.Ct is { } ct ? $" + {ct.Vouchers.Count:#,0} chứng từ" : "") + ".";
        _loading = true; _from.SelectedDate = D(Svc.Period?.From); _to.SelectedDate = D(Svc.Period?.To); _loading = false;
        RenderSources();
        if (Svc.Ready) RunSilent();
    }

    /// <summary>Ánh xạ cột thủ công khi không nhận diện chắc chắn.</summary>
    private async Task<Dictionary<string, int>?> MapDialog(string kind, CkSource res)
    {
        var spec = CkConfig.Sources[kind]; Dictionary<string, int>? result = null;
        var opts = new List<string> { "(không có)" }; opts.AddRange(res.Labels.Select((l, i) => $"{XlsxColName(i)}: {(l.Length > 0 ? l : "(trống)")}"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), RowDefinitions = new RowDefinitions(string.Join(",", spec.Fields.Select(_ => "Auto"))) };
        var boxes = new Dictionary<string, ComboBox>();
        for (var i = 0; i < spec.Fields.Length; i++)
        {
            var f = spec.Fields[i];
            var lab = new TextBlock { Text = f.Label + (spec.Must.Contains(f.Key) ? " *" : "") + (res.Missing.Contains(f.Key) ? " (thiếu)" : res.Ambiguous.ContainsKey(f.Key) ? " (nhiều cột khớp)" : ""), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2), Foreground = res.Missing.Contains(f.Key) || res.Ambiguous.ContainsKey(f.Key) ? Red : AppTheme.Text };
            var cb = new ComboBox { ItemsSource = opts, SelectedIndex = res.Mapping.TryGetValue(f.Key, out var c) ? c + 1 : res.Ambiguous.TryGetValue(f.Key, out var a) ? a[0] + 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2) };
            Grid.SetRow(lab, i); Grid.SetRow(cb, i); Grid.SetColumn(cb, 1); grid.Children.Add(lab); grid.Children.Add(cb); boxes[f.Key] = cb;
        }
        var ok = new Button { Content = "Đọc lại với ánh xạ này", Classes = { "primary" }, IsDefault = true }; var no = new Button { Content = "Thôi", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = res.Error, TextWrapping = TextWrapping.Wrap, Foreground = Amber });
        body.Children.Add(Muted($"Dòng tiêu đề: {res.HeaderRow}. Chọn cột tương ứng cho từng trường (* = bắt buộc). Lần sau phần mềm tự nhớ theo tên cột."));
        body.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 460 }); body.Children.Add(Ui.Buttons(no, ok));
        var w = Ui.Dialog("Ánh xạ cột — " + spec.Title, body, 640);
        ok.Click += (_, _) => { result = boxes.Where(kv => kv.Value.SelectedIndex > 0).ToDictionary(kv => kv.Key, kv => kv.Value.SelectedIndex - 1); w.Close(); };
        no.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        return result;
    }
    private static string XlsxColName(int c) { var s = ""; c++; while (c > 0) { var m = (c - 1) % 26; s = (char)('A' + m) + s; c = (c - 1) / 26; } return s; }

    // ------------------------------------------------------------ bảng kết quả
    private void RenderAll()
    {
        RenderKpi(); RenderChips(); RenderList(); RenderDetail(); RenderHistory(); RenderChecks();
    }

    private void RenderKpi()
    {
        _kpi.Children.Clear(); var R = Svc.Result;
        if (R is null) { _kpi.Children.Add(Muted(Svc.Ready ? "Bấm ▶ Đối chiếu." : "Nạp file Tồn kho An An và Kế toán (bên trái) để bắt đầu.")); return; }
        var K = R.Kpi;
        Border Kp(string label, string value, IBrush? fg = null, string? sub = null)
        {
            var s = new StackPanel();
            s.Children.Add(new TextBlock { Text = value, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = fg ?? Dark });
            s.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Classes = { "muted" } });
            if (sub is not null) s.Children.Add(new TextBlock { Text = sub, FontSize = 11, Classes = { "muted" } });
            return new Border { Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3), MinWidth = 100, Child = s };
        }
        _kpi.Children.Add(Kp("Mã đối chiếu", $"{K.Tong:#,0}", null, $"khớp {K.Khop:#,0}"));
        _kpi.Children.Add(Kp("Đang lệch", $"{K.Lech:#,0}", K.Lech > 0 ? Red : Green, $"thiếu {K.Thieu} · thừa {K.Thua}"));
        _kpi.Children.Add(Kp("Chưa xác định nguyên nhân", $"{K.ChuaXacDinh:#,0}", K.ChuaXacDinh > 0 ? Amber : Green));
        _kpi.Children.Add(Kp("Đã xử lý", $"{K.DaXuLy:#,0}", Green, $"tiến độ {K.TienDo}%"));
        _kpi.Children.Add(Kp("Tồn kho (An An + gửi)", Vn(K.TongKho) + " kg", null, $"An An {Vn(K.TongAA)} · gửi {Vn(K.TongGui)}"));
        _kpi.Children.Add(Kp("Tồn kế toán", Vn(K.TongKt) + " kg"));
        _kpi.Children.Add(Kp("Chênh lệch ròng / tuyệt đối", $"{Sg(K.LechRong)} / {Vn(K.LechTuyetDoi)}", K.LechTuyetDoi > 0 ? Red : Green, $"sau điều chỉnh {Sg(K.LechRongSau)} / {Vn(K.LechTuyetDoiSau)}"));
        if (K.Bubu) _kpi.Children.Add(Kp("Lệch bù trừ", "⚠", Amber, "tổng ≈ 0 nhưng từng mã lệch"));
    }

    private int Count(string k) => Svc.Result?.Items.Count(i => Match(i, k)) ?? 0;
    private static bool Match(CkItem i, string k) => k switch
    {
        "all" => true, "lech" => i.Status == "diff", "khop" => i.Status == "match", "thieu" => i.TagThieu, "thua" => i.TagThua, "saima" => i.TagSaiMa, "quycach" => i.TagQuyCach,
        "ct" => i.TagCt, "chua" => i.Status == "diff" && !i.RootKnown && i.StatusUi != "xong", "xong" => i.StatusUi == "xong", "conlai" => i.Status == "diff" && i.StatusUi != "xong", _ => true,
    };

    private void RenderChips()
    {
        _chips.Children.Clear(); if (Svc.Result is null) return;
        foreach (var (k, t) in Filters)
        {
            var b = new Button { Content = $"{t} ({Count(k)})", FontSize = 12, Padding = new Thickness(8, 2) };
            if (k == _filter) b.Classes.Add("primary");
            var kk = k; b.Click += (_, _) => { _filter = kk; RenderChips(); RenderList(); };
            _chips.Children.Add(b);
        }
    }

    private void RenderList()
    {
        var R = Svc.Result; var q = Fold(_q.Text ?? "");
        _view = R is null ? new() : R.Items.Where(i => Match(i, _filter) && (q.Length == 0 || Fold(i.Ma + " " + i.Ten).Contains(q))).ToList();
        // xếp các mã cùng nhóm nghi ngờ cạnh nhau
        if (_view.Any(i => i.Group.Length > 0))
        {
            var firstPos = new Dictionary<string, int>(); for (var i = 0; i < _view.Count; i++) { var g = _view[i].Group.Length > 0 ? _view[i].Group : _view[i].Ma; if (!firstPos.ContainsKey(g)) firstPos[g] = i; }
            _view = _view.Select((x, i) => (x, i)).OrderBy(t => firstPos[t.x.Group.Length > 0 ? t.x.Group : t.x.Ma]).ThenBy(t => t.i).Select(t => t.x).ToList();
        }
        _m.Load(new[]
        {
            new GridColumn("ma", "Mã hàng", 195), new GridColumn("ten", "Tên hàng", 200), new GridColumn("aa", "Tồn An An", 90, "number"), new GridColumn("gui", "Tồn kho gửi", 90, "number"),
            new GridColumn("kho", "Tổng tồn kho", 95, "number"), new GridColumn("kt", "Tồn kế toán", 95, "number"), new GridColumn("d", "Chênh lệch", 90, "number"), new GridColumn("da", "Sau điều chỉnh", 95, "number"),
            new GridColumn("c", "Nguyên nhân chính", 260), new GridColumn("tc", "Tin cậy", 80), new GridColumn("st", "Trạng thái", 140), new GridColumn("g", "Nhóm", 60),
        }, _view.Select(i => new[]
        {
            i.Ma, i.Ten, F(i.Aa), F(i.Gui), Vn(i.Kho), F(i.Kt), i.Status == "match" ? "0" : Sg(i.Diff), i.Adj != 0 ? Sg(i.DiffAfter) : "",
            i.Cause is { } c ? c.Label : "", i.Cause is { } c2 ? (c2.Basis == "xac-dinh" ? "Xác định" : c2.ConfLabel) : "", CkConfig.StatusLabel(i.StatusUi) + (i.Stale ? " ⚠ số liệu đổi" : ""), i.Group.Length > 0 ? "●" : "",
        }));
        _g.Refresh();
        var idx = _view.FindIndex(i => i.Ma == _sel);
        if (idx >= 0) _g.Select(idx, 0); else if (_view.Count > 0 && _sel.Length == 0) { _sel = _view[0].Ma; _g.Select(0, 0); }
    }

    // ------------------------------------------------------------ chi tiết 1 mã
    private void RenderDetail()
    {
        _detail.Children.Clear(); var R = Svc.Result;
        var it = R is not null && _sel.Length > 0 ? R.ByMa.GetValueOrDefault(_sel) : null;
        if (it is null) { _detail.Children.Add(Muted("Chọn 1 mã ở bảng để xem nguyên nhân, gợi ý mã và chứng từ.")); return; }
        _detail.Children.Add(new TextBlock { Text = it.Ma, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Dark });
        if (it.Ten.Length > 0) _detail.Children.Add(Muted(it.Ten));
        var nums = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        void Num(int c, string l, string v, IBrush? fg = null) { var s = new StackPanel(); s.Children.Add(new TextBlock { Text = v, FontWeight = FontWeight.Bold, Foreground = fg ?? Dark }); s.Children.Add(new TextBlock { Text = l, FontSize = 11, Classes = { "muted" } }); Grid.SetColumn(s, c); nums.Children.Add(s); }
        Num(0, "An An + Kho gửi", Vn(it.Kho) + " kg"); Num(1, "Kế toán", F(it.Kt) + " kg"); Num(2, "Chênh lệch", Sg(it.Diff) + " kg", it.Status == "diff" ? Red : Green); Num(3, "Sau điều chỉnh", Sg(it.DiffAfter) + " kg", it.StatusAfter == "diff" ? Red : Green);
        _detail.Children.Add(Card(nums));
        var tham = $"An An {F(it.Aa)} · kho gửi {F(it.Gui)}";
        if (it.Decomp is { } dc) tham += $"\nĐầu kỳ: kho {Vn(dc.Dau.Kho)} / KT {Vn(dc.Dau.Kt)} · Nhập: {Vn(dc.Nhap.Kho)} / {Vn(dc.Nhap.Kt)} · Xuất: {Vn(dc.Xuat.Kho)} / {Vn(dc.Xuat.Kt)}";
        _detail.Children.Add(Muted(tham));
        if (it.Stale) _detail.Children.Add(new TextBlock { Text = "⚠ Số liệu đã đổi sau khi xác nhận xử lý — kiểm tra lại.", Foreground = Amber, TextWrapping = TextWrapping.Wrap });

        // nguyên nhân
        if (it.Causes.Count > 0)
        {
            _detail.Children.Add(H("Nguyên nhân (#1 = chính)"));
            for (var k = 0; k < it.Causes.Count; k++)
            {
                var c = it.Causes[k]; var s = new StackPanel { Spacing = 2 };
                s.Children.Add(new TextBlock { Text = $"{k + 1}. {c.Label}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                s.Children.Add(new TextBlock { Text = (c.Basis == "xac-dinh" ? "Xác định" : c.Basis == "nghi-ngo" ? "Nghi ngờ" : "Chưa xác định") + (c.ConfLabel.Length > 0 ? " · tin cậy " + c.ConfLabel : ""), FontSize = 11.5, Foreground = c.Basis == "xac-dinh" ? Green : Amber });
                foreach (var e in c.Evidence) s.Children.Add(new TextBlock { Text = "• " + e, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                if (c.Action.Length > 0) s.Children.Add(new TextBlock { Text = "→ " + c.Action, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Dark });
                if (Svc.CanEdit && it.Status == "diff") { var cc = c; s.Children.Add(Btn("Xác nhận nguyên nhân này…", async () => await ConfirmDlg(it, cc.Code))); }
                _detail.Children.Add(Card(s, k == 0 ? B("#F4F7FB") : null));
            }
        }
        // gợi ý mã
        if (it.Cands.Count > 0 || it.Combos.Count > 0)
        {
            _detail.Children.Add(H("Mã có khả năng bị ghi nhầm"));
            foreach (var c in it.Cands)
            {
                var s = new StackPanel { Spacing = 2 };
                s.Children.Add(new TextBlock { Text = $"{c.Ma}  ({Sg(c.Diff)} kg) · {c.Score} điểm · tin cậy {CkEngine.ConfLabel.GetValueOrDefault(c.Conf, "")}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                foreach (var r in c.Reasons) s.Children.Add(new TextBlock { Text = "• " + r, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                if (Svc.CanEdit)
                {
                    var bs = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
                    var linked = it.Decision?.Linked.Contains(c.Ma) == true; var cc = c;
                    bs.Children.Add(Btn("Sửa mã…", async () => await TransferDlg(it, cc), "Ghi nhận nhầm mã: chuyển số lượng từ mã thừa sang mã thiếu (ở kho hoặc ở kế toán)"));
                    bs.Children.Add(Btn(linked ? "Gỡ mã liên quan" : "Gắn mã liên quan", async () => { await Ui.Run(this, () => { if (linked) Svc.Unlink(it.Ma, cc.Ma); else Svc.Link(it.Ma, cc.Ma); }); RenderAll(); }));
                    bs.Children.Add(Btn("Xem mã này", () => { _sel = cc.Ma; _filter = "all"; RenderChips(); RenderList(); RenderDetail(); }));
                    s.Children.Add(bs);
                }
                _detail.Children.Add(Card(s));
            }
            foreach (var g in it.Combos)
            {
                var s = new StackPanel { Spacing = 2 };
                s.Children.Add(new TextBlock { Text = $"Chia sang {g.Codes.Count} mã: {string.Join(" + ", g.Codes)} (tin cậy {CkEngine.ConfLabel.GetValueOrDefault(g.Conf, "")})", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                foreach (var r in g.Reasons) s.Children.Add(new TextBlock { Text = "• " + r, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                _detail.Children.Add(Card(s));
            }
        }
        // chứng từ
        if (it.Ct.Lines.Count > 0)
        {
            _detail.Children.Add(H($"Chứng từ ({it.Ct.Lines.Count}, {it.Ct.NFlag} có dấu hiệu)"));
            foreach (var l in it.Ct.Lines.Take(80))
            {
                var flagged = it.Decision?.Flagged.Contains(l.V.Id) == true;
                var s = new DockPanel();
                if (Svc.CanEdit) { var ll = l; var fb = Btn(flagged ? "★" : "☆", async () => { await Ui.Run(this, () => Svc.FlagVoucher(it.Ma, ll.V.Id, !flagged)); RenderAll(); }, flagged ? "Bỏ đánh dấu" : "Đánh dấu chứng từ nghi ngờ"); DockPanel.SetDock(fb, Dock.Right); s.Children.Add(fb); }
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = $"{l.V.Phieu} · {Dmy(l.V.Ngay)} · {(l.V.Dir == "N" ? "Nhập" : "Xuất")} {Vn(l.V.Kg)} kg" + (l.V.Lsx.Length > 0 ? " · " + l.V.Lsx : "") + $" ({(l.V.Src == "ketoan" ? "kế toán" : "kho")}, dòng {l.V.Row})", FontSize = 12, TextWrapping = TextWrapping.Wrap });
                foreach (var f in l.Flags) t.Children.Add(new TextBlock { Text = "⚑ " + CkTrace.FlagLabel.GetValueOrDefault(f.Code, f.Code) + ": " + f.Text, FontSize = 11.5, Foreground = Amber, TextWrapping = TextWrapping.Wrap });
                s.Children.Add(t);
                _detail.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 3), Child = s });
            }
        }
        // xử lý
        if (Svc.CanEdit)
        {
            _detail.Children.Add(H("Xử lý"));
            var st = new ComboBox { ItemsSource = CkConfig.Status.Select(x => x.label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(CkConfig.Status, x => x.v == it.StatusUi)), Width = 200 };
            st.SelectionChanged += async (_, _) => { var v = CkConfig.Status[Math.Max(0, st.SelectedIndex)].v; if (v == it.StatusUi) return; if (v == "xong") { await ConfirmDlg(it, it.Cause?.Code ?? ""); return; } await Ui.Run(this, () => Svc.SetStatus(it.Ma, v)); RenderAll(); };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; row.Children.Add(Lb("Trạng thái")); row.Children.Add(st);
            _detail.Children.Add(row);
            var note = new TextBox { Text = it.Decision?.Note ?? "", Watermark = "Ghi chú cho mã này…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 50 };
            _detail.Children.Add(note);
            var acts = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
            acts.Children.Add(Btn("Lưu ghi chú", async () => { await Ui.Run(this, () => Svc.SetNote(it.Ma, note.Text ?? "")); RenderAll(); }));
            acts.Children.Add(Btn("Điều chỉnh số lượng…", async () => await AdjustDlg(it)));
            acts.Children.Add(Btn("Xác nhận đã xử lý…", async () => await ConfirmDlg(it, it.Cause?.Code ?? ""), null, true));
            _detail.Children.Add(acts);
        }
        var adjs = Svc.State.Adjustments.Where(a => a.Ma == it.Ma).ToList();
        if (adjs.Count > 0)
        {
            _detail.Children.Add(H("Điều chỉnh"));
            foreach (var a in adjs)
            {
                var d = new DockPanel();
                if (Svc.CanEdit && !a.Cancelled) { var aa = a; var cb = Btn("Hủy", async () => { await Ui.Run(this, () => Svc.CancelAdjustment(aa.Id)); RenderAll(); }); DockPanel.SetDock(cb, Dock.Right); d.Children.Add(cb); }
                d.Children.Add(new TextBlock { Text = $"{Sg(a.Qty)} kg ở {(a.Side == "kt" ? "kế toán" : "kho")} · {a.Reason} · {a.By} {When(a.At)}" + (a.Cancelled ? " (đã hủy)" : ""), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = a.Cancelled ? AppTheme.Muted : AppTheme.Text });
                _detail.Children.Add(d);
            }
        }
        var mine = Svc.State.History.Where(e => e.Mas.Contains(it.Ma)).Reverse().Take(15).ToList();
        if (mine.Count > 0)
        {
            _detail.Children.Add(H("Lịch sử của mã"));
            foreach (var e in mine) _detail.Children.Add(HistRow(e));
        }
    }

    private Control HistRow(CkEntry e)
    {
        var d = new DockPanel();
        if (Svc.CanEdit && !e.Undone && e.Action != "hoan-tac") { var ee = e; var ub = Btn("↶ Hoàn tác", async () => { await Ui.Run(this, () => Svc.Undo(ee.Id)); RenderAll(); }); DockPanel.SetDock(ub, Dock.Right); d.Children.Add(ub); }
        d.Children.Add(new TextBlock { Text = $"{When(e.At)} · {e.By} · {e.Summary}" + (e.Reason.Length > 0 ? " — " + e.Reason : "") + (e.Undone ? " (đã hoàn tác)" : ""), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = e.Undone ? AppTheme.Muted : AppTheme.Text });
        return new Border { BorderBrush = Line_, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 3), Child = d };
    }

    private void RenderHistory()
    {
        _hist.Children.Clear();
        if (Svc.State.History.Count == 0) { _hist.Children.Add(Muted("Chưa có thao tác nào.")); return; }
        foreach (var e in Enumerable.Reverse(Svc.State.History)) _hist.Children.Add(HistRow(e));
    }

    private void RenderChecks()
    {
        _checks.Children.Clear(); var R = Svc.Result;
        if (R is null) { _checks.Children.Add(Muted("Chưa đối chiếu.")); return; }
        foreach (var c in R.Validation) _checks.Children.Add(new TextBlock { Text = (c.Level switch { "ok" => "✔ ", "warn" => "⚠ ", "err" => "⛔ ", _ => "ℹ " }) + c.Text, Foreground = c.Level switch { "ok" => Green, "warn" => Amber, "err" => Red, _ => AppTheme.Text }, TextWrapping = TextWrapping.Wrap });
        if (R.Offsets.Count > 0)
        {
            _checks.Children.Add(H("Nhóm mã lệch bù trừ (chỉ khác 1 đoạn mã, tổng ≈ 0)"));
            foreach (var o in R.Offsets) _checks.Children.Add(new TextBlock { Text = $"Khác ở {o.Label}: {string.Join(", ", o.Codes)} (tổng {Sg(o.Sum)} kg)", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
    }

    // ------------------------------------------------------------ hộp thoại xử lý
    private async Task TransferDlg(CkItem it, CkCand c)
    {
        var (over, under) = it.Diff > 0 ? (it.Ma, c.Ma) : (c.Ma, it.Ma);
        var qty = new TextBox { Text = Vn(Math.Min(Math.Abs(it.Diff), Math.Abs(c.Diff))).Replace(",", ""), Width = 120 };
        var side = new ComboBox { ItemsSource = new[] { CkHistory.SideText["kho"], CkHistory.SideText["kt"] }, SelectedIndex = 0, Width = 260 };
        var reason = new TextBox { Watermark = "Lý do (bắt buộc), VD: phiếu XK… ghi nhầm mã" };
        var ok = new Button { Content = "Ghi nhận sửa mã", Classes = { "primary" }, IsDefault = true }; var no = new Button { Content = "Thôi", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = $"Chuyển số lượng từ mã THỪA {over} sang mã THIẾU {under}.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Muted("Chỉ ghi điều chỉnh trong phiên đối chiếu (file gốc / phần mềm kho, kế toán không bị đổi) — hoàn tác được."));
        var r1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; r1.Children.Add(Lb("Số kg")); r1.Children.Add(qty); r1.Children.Add(Lb("Sửa ở")); r1.Children.Add(side);
        body.Children.Add(r1); body.Children.Add(reason); body.Children.Add(Ui.Buttons(no, ok));
        var w = Ui.Dialog("Sửa mã hàng", body, 560); var go = false;
        ok.Click += (_, _) => { go = true; w.Close(); }; no.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (!go) return;
        if (await Ui.Run(this, () => Svc.Transfer(over, under, Ui.ParseNumber(qty.Text, "Số kg"), reason.Text ?? "", side.SelectedIndex == 1 ? "kt" : "kho"))) { RenderAll(); _status.Text = $"Đã ghi nhận sửa mã {over} → {under}."; }
    }

    private async Task AdjustDlg(CkItem it)
    {
        var qty = new TextBox { Text = Vn(-it.DiffAfter).Replace(",", ""), Width = 120 };
        var side = new ComboBox { ItemsSource = new[] { "Kho (cộng / trừ vào tồn kho)", "Kế toán (cộng / trừ vào tồn kế toán)" }, SelectedIndex = 0, Width = 280 };
        var reason = new TextBox { Watermark = "Lý do (bắt buộc)" };
        var ok = new Button { Content = "Ghi điều chỉnh", Classes = { "primary" }, IsDefault = true }; var no = new Button { Content = "Thôi", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Muted($"Chênh lệch hiện tại {Sg(it.DiffAfter)} kg. kg dương = cộng vào bên được sửa, âm = trừ đi."));
        var r1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; r1.Children.Add(Lb("Số kg")); r1.Children.Add(qty); r1.Children.Add(side);
        body.Children.Add(r1); body.Children.Add(reason); body.Children.Add(Ui.Buttons(no, ok));
        var w = Ui.Dialog("Điều chỉnh — " + it.Ma, body, 600); var go = false;
        ok.Click += (_, _) => { go = true; w.Close(); }; no.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (!go) return;
        if (await Ui.Run(this, () => Svc.Adjust(it.Ma, Ui.ParseNumber(qty.Text, "Số kg"), side.SelectedIndex == 1 ? "kt" : "kho", reason.Text ?? ""))) RenderAll();
    }

    private async Task ConfirmDlg(CkItem it, string cause)
    {
        var causes = CkConfig.CauseList.ToList();
        var cb = new ComboBox { ItemsSource = causes.Select(c => c.label).ToList(), SelectedIndex = Math.Max(0, causes.FindIndex(c => c.code == cause)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var note = new TextBox { Watermark = "Diễn giải nguyên nhân (không bắt buộc)" };
        var reason = new TextBox { Watermark = "Lý do / cách đã xử lý (bắt buộc)" };
        var adj = new CheckBox { Content = $"Kèm điều chỉnh kho {Sg(-it.DiffAfter)} kg để khớp", IsChecked = false, IsVisible = Math.Abs(it.DiffAfter) > Svc.Options.Tol };
        var ok = new Button { Content = "Xác nhận đã xử lý", Classes = { "primary" }, IsDefault = true }; var no = new Button { Content = "Thôi", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Lb("Nguyên nhân")); body.Children.Add(cb); body.Children.Add(note); body.Children.Add(reason); body.Children.Add(adj); body.Children.Add(Ui.Buttons(no, ok));
        var w = Ui.Dialog("Xác nhận xử lý — " + it.Ma, body, 560); var go = false;
        ok.Click += (_, _) => { go = true; w.Close(); }; no.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (!go) { RenderDetail(); return; }
        if (await Ui.Run(this, () => Svc.Confirm(it.Ma, causes[Math.Max(0, cb.SelectedIndex)].code, note.Text ?? "", adj.IsChecked == true ? R6(-it.DiffAfter) : 0, reason.Text ?? "", null))) RenderAll();
        else RenderDetail();
    }

    // ------------------------------------------------------------ khác
    private async Task Export()
    {
        if (Svc.Result is null) { await Ui.Message(this, "Chưa đối chiếu."); return; }
        var p = Svc.Period;
        var path = await Ui.SaveFile(this, "Xuất báo cáo đối chiếu cuối kỳ", $"DoiChieuCuoiKy_{p?.To ?? DateTime.Now.ToString("yyyy-MM-dd")}.xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => Svc.BuildExcel().Save(path))) Ui.OpenExternal(path);
    }

    private async Task ImportAdj()
    {
        var path = await Ui.OpenFile(this, "Chọn file điều chỉnh", "xlsx;xls;csv", "Excel"); if (path is null) return;
        (int N, List<(int Row, string Msg)> Errors) r = default;
        if (await Ui.Run(this, () => r = Svc.ImportAdjustments(path)))
        {
            RenderAll();
            await Ui.Message(this, $"Đã ghi {r.N} điều chỉnh." + (r.Errors.Count > 0 ? "\n\nBỏ qua:\n" + string.Join("\n", r.Errors.Take(20).Select(e => $"Dòng {e.Row}: {e.Msg}")) : ""));
        }
    }

    private async Task Settings()
    {
        var o = Svc.Options;
        var tol = new TextBox { Text = Vn(o.Tol), Width = 90 }; var pt = new TextBox { Text = Vn(o.PairTol), Width = 90 };
        var bot = new CheckBox { Content = "Ẩn hàng Bột (không cần đối chiếu)", IsChecked = o.HideBot }; var zero = new CheckBox { Content = "Hiện cả mã tồn = 0 ở mọi nguồn", IsChecked = o.ShowZero };
        var ok = new Button { Content = "Lưu", Classes = { "primary" }, IsDefault = true }; var no = new Button { Content = "Thôi", IsCancel = true };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        var l1 = Lb("Ngưỡng khớp (kg)"); var l2 = Lb("Ngưỡng đối ứng 2 mã (kg)"); Grid.SetRow(l2, 1); Grid.SetColumn(tol, 1); Grid.SetColumn(pt, 1); Grid.SetRow(pt, 1);
        g.Children.Add(l1); g.Children.Add(tol); g.Children.Add(l2); g.Children.Add(pt);
        var body = new StackPanel { Spacing = 8 }; body.Children.Add(g); body.Children.Add(bot); body.Children.Add(zero); body.Children.Add(Ui.Buttons(no, ok));
        var w = Ui.Dialog("Thiết lập đối chiếu", body, 460); var go = false;
        ok.Click += (_, _) => { go = true; w.Close(); }; no.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (!go) return;
        if (await Ui.Run(this, () => { o.Tol = Math.Max(0, Ui.ParseNumber(tol.Text, "Ngưỡng khớp")); o.PairTol = Math.Max(0, Ui.ParseNumber(pt.Text, "Ngưỡng đối ứng")); o.HideBot = bot.IsChecked == true; o.ShowZero = zero.IsChecked == true; Svc.SaveAll(); }) && Svc.Ready) RunSilent();
    }

    private async Task ClearAll()
    {
        if (!await Ui.Confirm(this, "Xóa toàn bộ dữ liệu kỳ (file đã nạp, quyết định, điều chỉnh, lịch sử) trên database dùng chung? Mọi máy sẽ thấy trống. Nên Xuất Excel trước.", "Xóa dữ liệu kỳ", "Xóa")) return;
        if (await Ui.Run(this, Svc.Clear)) { _sel = ""; RenderSources(); RenderAll(); _status.Text = "Đã xóa dữ liệu kỳ."; }
    }
}

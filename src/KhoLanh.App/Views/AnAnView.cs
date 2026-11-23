using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Tồn kho An An viết lại bằng C# (thay bản WebView2) — cùng nghiệp vụ, cùng số liệu, dùng chung database "ton-kho-an-an" với app cũ.
/// Tab: Load Data · Phân Loại (8 bảng + điều kiện lọc) · Báo Cáo · Theo Dõi Tồn · Đặc Tính · Tổng Hợp (Xuất Hàng, Báo cáo ngoài sổ sách).
/// </summary>
public sealed class AnAnView : UserControl, IPage
{
    public string Title => "Tồn kho An An";
    public string Subtitle => "Nạp file tồn gốc, phân loại 8 nhóm, báo cáo Tổng Tồn / HLSO, theo dõi tồn, đặc tính, phiếu xuất — dùng chung dữ liệu với app cũ.";

    private static AnAnService Svc => App.Services.AnAn;
    private static AnAnData D => Svc.D;
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#D0D7E2"));
    private static readonly IBrush TitleBg = new SolidColorBrush(Color.Parse("#DDEBF7"));
    private static readonly IBrush TotalBg = new SolidColorBrush(Color.Parse("#FFF2CC"));
    private static readonly IBrush ManualBg = new SolidColorBrush(Color.Parse("#E2EFDA"));
    private static readonly IBrush WarnBg = new SolidColorBrush(Color.Parse("#FCE4D6"));
    private static readonly IBrush LowBg = new SolidColorBrush(Color.Parse("#FFE699"));
    private static readonly IBrush OutBg = new SolidColorBrush(Color.Parse("#F8CBAD"));
    private static readonly IBrush PickBg = new SolidColorBrush(Color.Parse("#FFF1C9"));

    private readonly bool _edit, _export;
    private bool _loading;
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
    private readonly TextBlock _fileInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TabControl _tabs;

    public AnAnView()
    {
        _edit = Svc.CanEdit; _export = Svc.CanExport;
        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Load Data", Content = BuildLoad() },
                new TabItem { Header = "Phân Loại", Content = BuildPl() },
                new TabItem { Header = "Báo Cáo", Content = BuildBc() },
                new TabItem { Header = "Theo Dõi Tồn", Content = BuildTd() },
                new TabItem { Header = "Đặc Tính", Content = BuildDt() },
                new TabItem { Header = "Tổng Hợp", Content = BuildTh() },
            },
        };
        _tabs.SelectionChanged += (_, _) => { if (!_loading && Svc.Loaded) RenderTab(); };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(_status, Dock.Right);
        top.Children.Add(_status); top.Children.Add(_fileInfo);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top); root.Children.Add(_tabs);
        Content = root;
        AttachedToVisualTree += async (_, _) => await LoadAsync();
    }

    // =====================================================================================
    // Chung
    // =====================================================================================

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
    private static TextBlock Muted(string t) => new() { Text = t, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private static Border Framed(Control c) => new() { BorderBrush = Line, BorderThickness = new Thickness(1), Child = c };
    private static WrapPanel Bar() => new() { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 8, 0, 6) };
    /// <summary>Số hiện như bản cũ (toLocaleString en-US).</summary>
    private static string N(double? v, int min = 0, int max = 2) => v is double x && !double.IsNaN(x) ? JsText.En(x, min, max) : "";
    private static string Kg(double? v) => N(v, 2, 2);
    private static string Raw(double v) => JsText.JsStr(Math.Round(v, 6));
    private static string Nap(double? ms) => ms is > 0 ? DateTime.UnixEpoch.AddMilliseconds(ms.Value).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "—";
    private static string Today => DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static DockPanel Page(Control head, Control body)
    {
        var p = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        p.Children.Add(head); p.Children.Add(body);
        return p;
    }

    private async Task LoadAsync()
    {
        _status.Text = "Đang tải…";
        try { await Task.Run(Svc.Load); }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        _status.Text = "";
        RenderAll();
        if (!D.HasData) _tabs.SelectedIndex = 0;
        // Kho Gửi tự cập nhật từ Tồn kho gửi (như vào trang Báo Cáo của app cũ)
        try
        {
            var r = await Task.Run(() => Svc.SyncKhoGui(false));
            if (r is { Changed: true } x) { _status.Text = $"Kho Gửi tự cập nhật từ Tồn kho gửi: {Kg(x.Info.Kg)} kg" + (x.Info.DataDate.Length > 0 ? $" (số liệu ngày {x.Info.DataDate})" : "") + (x.Old is double o ? $" · trước đó {Kg(o)}" : "") + "."; RenderTab(); }
        }
        catch { /* Tồn kho gửi chưa có dữ liệu / lỗi đọc: giữ số cũ */ }
    }

    /// <summary>Chạy thao tác ghi; máy khác vừa sửa → tải lại.</summary>
    private async Task<bool> Act(Action a, string? ok = null)
    {
        try
        {
            a();
            if (ok is not null) _status.Text = ok + " (" + DateTime.Now.ToString("HH:mm:ss") + ")";
            RenderAll();
            return true;
        }
        catch (SheetConflictException)
        {
            await Ui.Message(this, "Dữ liệu Tồn kho An An vừa được máy khác sửa nên thao tác này CHƯA được lưu (để không ghi đè). Bấm Đóng để tải bản mới nhất rồi làm lại.", "Máy khác vừa sửa");
            await LoadAsync();
        }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); }
        RenderAll();
        return false;
    }

    private async Task SaveXlsx(XlsxOut? x, string fileName)
    {
        if (x is null) { await Ui.Message(this, "Không có gì để xuất."); return; }
        var path = await Ui.SaveFile(this, "Xuất Excel", fileName, "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => x.Save(path))) Ui.OpenExternal(path);
    }

    private void RenderAll()
    {
        _fileInfo.Text = !D.HasData
            ? "Chưa có dữ liệu — bấm “Nạp file Excel” ở tab Load Data."
            : $"File: {(D.FileName.Length > 0 ? D.FileName : "dữ liệu đã lưu")} · Số liệu ngày {(D.DataDate.Length > 0 ? D.DataDate : "— (chưa nhập)")} · {N(D.Rows.Count)} dòng · nạp lúc {Nap(D.ImportedAt)}";
        RenderTab();
    }

    private void RenderTab()
    {
        _loading = true;
        try
        {
            switch (_tabs.SelectedIndex)
            {
                case 0: RenderLoad(); break;
                case 1: RenderPl(); break;
                case 2: RenderBc(); break;
                case 3: RenderTd(); break;
                case 4: RenderDt(); break;
                case 5: RenderTh(); break;
            }
        }
        finally { _loading = false; }
    }

    // =====================================================================================
    // Load Data
    // =====================================================================================

    private readonly TextBox _ldQ = new() { Watermark = "Tìm trong mọi cột…", Width = 260 };
    private readonly TextBox _ldDate = new() { Watermark = "dd/mm/yyyy", Width = 110 };
    private readonly TextBlock _ldStats = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly WrapPanel _ldSheets = new() { ItemSpacing = 4, LineSpacing = 4 };
    private readonly GridModel _ldm = new() { ReadOnly = true };
    private SheetGrid _ldg = null!;

    private Control BuildLoad()
    {
        _ldg = new SheetGrid(_ldm) { SpareRows = 0 };
        var bar = Bar();
        if (_edit)
        {
            bar.Children.Add(Btn("📂 Nạp file Excel…", async () => await PickAndImport(), primary: true, tip: "Chọn file → kiểm tra → xem trước → nạp. Dữ liệu cũ được chụp lại để hoàn tác."));
        }
        bar.Children.Add(Btn("⟲ Lịch sử nạp", async () => await ShowHistory(), tip: "Các đợt nạp (ai · máy nào · file nào); hoàn tác đợt mới nhất."));
        bar.Children.Add(Btn("⇄ So sánh 2 lần nạp", async () => await ShowCompare(), tip: "So dữ liệu hiện tại với lần nạp trước: mã mới / đã hết / tăng / giảm."));
        bar.Children.Add(Label("Ngày số liệu"));
        bar.Children.Add(_ldDate);
        if (_edit) bar.Children.Add(Btn("Lưu ngày", async () => await Act(() => Svc.SetDataDate(_ldDate.Text ?? ""), "Đã lưu ngày số liệu")));
        else _ldDate.IsReadOnly = true;
        if (_export) bar.Children.Add(Btn("⬇ Xuất Excel", async () => await SaveXlsx(AnAnExport.DuLieu(D, LdRows()), $"DuLieu_{AnAnExport.Stamp(DateTime.Now)}.xlsx")));
        if (_edit) bar.Children.Add(Btn("Xóa dữ liệu", async () =>
        {
            if (!await Ui.Confirm(this, "Xóa toàn bộ dữ liệu Load Data đang lưu?\n\nĐặc Tính và Báo cáo ngoài sổ sách vẫn giữ nguyên.", "Xóa dữ liệu", "Xóa")) return;
            await Act(Svc.ClearLoadData, "Đã xóa dữ liệu Load Data");
        }, danger: true));
        _ldDate.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter && _edit) await Act(() => Svc.SetDataDate(_ldDate.Text ?? ""), "Đã lưu ngày số liệu"); };
        _ldQ.TextChanged += (_, _) => { if (!_loading) RenderLoad(); };
        var bar2 = Bar(); bar2.Children.Add(_ldQ); bar2.Children.Add(_ldSheets);
        var head = new StackPanel(); head.Children.Add(bar); head.Children.Add(bar2); head.Children.Add(_ldStats);
        return Page(head, Framed(_ldg));
    }

    private List<int> LdRows()
    {
        var q = JsText.Fuzzy(_ldQ.Text);
        var all = Enumerable.Range(0, D.Rows.Count);
        return q.Length == 0 ? all.ToList() : all.Where(i => JsText.Fuzzy(string.Join(" ", D.Rows[i])).Contains(q, StringComparison.Ordinal)).ToList();
    }

    private void RenderLoad()
    {
        _ldDate.Text = D.DataDate;
        _ldSheets.Children.Clear();
        if (Svc.Workbook is { SheetNames.Count: > 1 } wb && _edit)
        {
            _ldSheets.Children.Add(Label("Sheet:"));
            foreach (var s in wb.SheetNames)
            {
                var name = s;
                var b = Btn(name + (name == D.CurrentSheet ? " ✓" : ""), async () =>
                {
                    if (name == D.CurrentSheet) return;
                    if (!await Ui.Confirm(this, $"Nạp sheet “{name}” thay cho dữ liệu đang có?", "Đổi sheet", "Nạp sheet này")) return;
                    await Act(() => Svc.SwitchSheet(name), "Đã nạp sheet " + name);
                });
                _ldSheets.Children.Add(b);
            }
        }
        if (!D.HasData) { _ldStats.Text = ""; _ldm.Load(new[] { new GridColumn("x", "Chưa có dữ liệu", 300) }, Array.Empty<string[]>()); _ldg.Refresh(); return; }
        var rows = LdRows();
        var matched = AnAnData.RequiredColumns.Count(c => D.Col(c) != -1);
        var maCi = D.Col("Mã hàng");
        var items = maCi == -1 ? (int?)null : D.Rows.Select(r => JsText.JsTrim(maCi < r.Length ? r[maCi] : "")).Where(x => x.Length > 0).Distinct().Count();
        _ldStats.Text = $"Dòng: {N(D.Rows.Count)}" + (items is int it ? $" · Mã hàng: {N(it)}" : "") + (D.SumAll("Tồn cuối") is double tc ? $" · Tổng tồn cuối: {N(tc)}" : "")
            + (D.TotalWeightAll() is double tw ? $" · Tổng trọng lượng: {Kg(tw)} kg" : "") + $" · Cột: {D.Headers.Count} · Khớp mẫu: {matched}/{AnAnData.RequiredColumns.Length}"
            + (rows.Count != D.Rows.Count ? $" · đang lọc {N(rows.Count)} dòng" : "")
            + (D.HeaderRowInfo?["rowNumber"] is { } rn ? $" · tiêu đề ở dòng {AnAnStore.S(rn)}" : "");
        var cols = D.Headers.Select((h, i) => new GridColumn("c" + i, h, AnAnData.IsNumericCol(h) ? 90 : Math.Clamp(h.Length * 9 + 30, 70, 260), AnAnData.IsNumericCol(h) ? "number" : "text") { ReadOnly = true }).ToList();
        _ldm.Load(cols, rows.Select(i => D.Rows[i].Concat(Enumerable.Repeat("", Math.Max(0, D.Headers.Count - D.Rows[i].Length))).Take(D.Headers.Count)));
        _ldg.Refresh();
    }

    // ---------- nạp file

    private async Task PickAndImport()
    {
        var path = await Ui.OpenFile(this, "Chọn file tồn kho An An", "xlsx;xls;csv", "Excel");
        if (path is null) return;
        AnAnService.NapPreview? pv = null;
        _status.Text = "Đang đọc file…";
        try { pv = await Task.Run(() => Svc.Preview(path)); }
        catch (BusinessException e) { _status.Text = ""; await Ui.Message(this, e.Message, "Không đọc được file"); return; }
        catch (Exception e) { _status.Text = ""; await Ui.Message(this, "Không đọc được file. Vui lòng kiểm tra định dạng Excel (.xlsx, .xls, .csv).\n\n" + Ui.Describe(e), "Không đọc được file"); return; }
        _status.Text = "";
        if (!await ReviewDialog(pv)) return;
        _status.Text = "Đang nạp…";
        try { await Task.Run(() => Svc.Commit(pv)); }
        catch (BusinessException e) when (e.Message.StartsWith("Không lưu được bản hoàn tác"))
        {
            _status.Text = "";
            if (!await Ui.Confirm(this, e.Message + "\n\nVẫn nạp dữ liệu? (đợt nạp này sẽ KHÔNG hoàn tác được)", "Không có bản hoàn tác", "Vẫn nạp")) return;
            if (!await Act(() => Svc.Commit(pv, keepUndo: false))) return;
        }
        catch (Exception e) { _status.Text = ""; await Ui.Message(this, e is BusinessException ? e.Message : Ui.Describe(e), "Không nạp được"); return; }
        _status.Text = $"Đã nạp {pv.FileName}: {N(D.Rows.Count)} dòng ({DateTime.Now:HH:mm:ss})";
        RenderAll();
    }

    /// <summary>Hộp "Xem trước khi nạp": 3 mức hợp lệ / cần kiểm tra / lỗi, so với dữ liệu đang có, file đã nạp chưa.</summary>
    private async Task<bool> ReviewDialog(AnAnService.NapPreview pv)
    {
        var C = pv.Check; var ok = false;
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Label($"{pv.FileName} · {(pv.Size / 1048576.0 < 0.1 ? Math.Max(1, Math.Round(pv.Size / 1024.0)) + " KB" : Math.Round(pv.Size / 104857.6) / 10 + " MB")} · {pv.Book.SheetNames.Count} sheet (đang nạp “{pv.Sheet}”)"
            + (pv.DataDate.Length > 0 ? $" · ngày số liệu {pv.DataDate}" : " · chưa có ngày số liệu"), bold: true));
        var pills = new WrapPanel { ItemSpacing = 8 };
        Border Pill(string n, string l, IBrush bg) => new() { Background = bg, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4), Child = new StackPanel { Children = { Label(n, true, 16), Muted(l) } } };
        pills.Children.Add(Pill(N(C.Ok), "Dòng hợp lệ", ManualBg)); pills.Children.Add(Pill(N(C.Warn), "Dòng cần kiểm tra", LowBg)); pills.Children.Add(Pill(N(C.Err), "Dòng lỗi", OutBg));
        body.Children.Add(pills);
        if (C.Infos.Count > 0) body.Children.Add(Muted(string.Join("\n", C.Infos)));
        if (C.Errors.Count > 0) body.Children.Add(new Border { Background = OutBg, Padding = new Thickness(8), Child = Label("Lỗi nặng — không nạp được:\n• " + string.Join("\n• ", C.Errors)) });
        if (pv.Warns.Count > 0) body.Children.Add(new Border { Background = LowBg, Padding = new Thickness(8), Child = Label($"Cần lưu ý ({pv.Warns.Count}):\n• " + string.Join("\n• ", pv.Warns.Take(8))) });
        if (pv.CmpLines.Count > 0)
        {
            var g = new GridModel { ReadOnly = true };
            g.Load(new[] { new GridColumn("a", "So với dữ liệu đang có", 170), new GridColumn("b", "Đang có", 110, "number"), new GridColumn("c", "File mới", 110, "number"), new GridColumn("d", "Chênh", 70) },
                pv.CmpLines.Select(l => new[] { l.Label, N(l.Cur, 0, 0), N(l.Next, 0, 0), l.Pct is double p ? (p > 0 ? "+" : "") + JsText.JsStr(p) + "%" : "" }));
            body.Children.Add(Framed(new SheetGrid(g) { SpareRows = 0, Height = 110 }));
        }
        if (C.Problems.Count > 0)
        {
            var g = new GridModel { ReadOnly = true };
            g.Load(new[] { new GridColumn("r", "Dòng", 60, "number"), new GridColumn("l", "Mức", 80), new GridColumn("m", "Nội dung", 420) },
                C.Problems.Take(200).Select(p => new[] { p.Row.ToString(CultureInfo.InvariantCulture), p.Level == "err" ? "Lỗi" : "Kiểm tra", p.Msg }));
            body.Children.Add(Label($"Các dòng cần xem ({N(C.Problems.Count)})" + (C.Problems.Count > 200 ? " — hiện 200 dòng đầu" : ""), bold: true));
            body.Children.Add(Framed(new SheetGrid(g) { SpareRows = 0, Height = 180 }));
        }
        var ack = new CheckBox { Content = $"Tôi đã xem {N(C.Err)} dòng lỗi ở trên và vẫn muốn nạp.", IsVisible = C.Err > 0 && !pv.Blocking };
        body.Children.Add(ack);
        body.Children.Add(Muted($"Dữ liệu cũ được chụp lại trước khi nạp — hoàn tác được trong {Svc.KeepDays} ngày (nút “Lịch sử nạp”), trên máy này."));
        var cancel = new Button { Content = "Hủy — giữ dữ liệu cũ", IsCancel = true };
        var go = new Button { Content = pv.Blocking ? "Không nạp được" : C.Err > 0 || pv.Warns.Count > 0 || C.Warn > 0 ? "Vẫn nạp" : "Nạp dữ liệu", Classes = { "primary" }, IsEnabled = !pv.Blocking && C.Err == 0 };
        ack.IsCheckedChanged += (_, _) => go.IsEnabled = !pv.Blocking && ack.IsChecked == true;
        body.Children.Add(Ui.Buttons(cancel, go));
        var w = Ui.Dialog("Xem trước khi nạp · Tồn kho An An", new ScrollViewer { Content = body, MaxHeight = 640 }, 720);
        cancel.Click += (_, _) => w.Close();
        go.Click += (_, _) => { ok = true; w.Close(); };
        await Ui.Show(this, w);
        return ok;
    }

    private async Task ShowHistory()
    {
        List<JsonObject> log;
        try { log = await Task.Run(Svc.History); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e)); return; }
        var g = new GridModel { ReadOnly = true };
        static string S(JsonNode? n) => NapExcel.S(n);
        string St(JsonObject x) => S(x["status"]) switch { "applied" => "Đang dùng", "undone" => "Đã hoàn tác", _ => "Đã bị thay" };
        g.Load(new[]
        {
            new GridColumn("t", "Lúc nạp", 120), new GridColumn("f", "File", 220), new GridColumn("s", "Sheet", 70), new GridColumn("d", "Ngày SL", 85), new GridColumn("r", "Dòng", 65, "number"),
            new GridColumn("k", "Tổng (kg)", 100, "number"), new GridColumn("u", "Người · máy", 170), new GridColumn("st", "Trạng thái", 95),
        }, log.Take(60).Select(x => new[]
        {
            NapExcel.When(S(x["time"])), S(x["fileName"]), S(x["sheet"]), S(x["dataDate"]), N(AnAnService.Num(x["rows"]), 0, 0), N(AnAnService.Num(x["kg"]), 0, 0),
            S(x["user"]) + (S(x["host"]).Length > 0 ? " · " + S(x["host"]) : ""), St(x),
        }));
        var grid = new SheetGrid(g) { SpareRows = 0, Height = 340 };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(log.Count == 0 ? Muted("Chưa có đợt nạp nào được ghi.") : Framed(grid));
        var la = NapExcel.LastApplied(log);
        var info = Muted(la is null ? "" : Svc.CanUndo(la, log)
            ? $"Đợt nạp đang dùng: {S(la["fileName"])} lúc {NapExcel.When(S(la["time"]))} — hoàn tác được trên máy này."
            : $"Đợt nạp đang dùng: {S(la["fileName"])} — " + (AnAnService.Num(la["hasUndo"]) == 1 ? $"bản hoàn tác nằm ở máy {(S(la["host"]).Length > 0 ? S(la["host"]) : "?")} (hoặc đã quá hạn)." : "không có bản hoàn tác."));
        body.Children.Add(info);
        body.Children.Add(Muted("Chỉ hoàn tác được đợt nạp mới nhất đang dùng, trên đúng máy đã nạp. Hoàn tác đưa dữ liệu về như TRƯỚC khi nạp file đó."));
        var days = new ComboBox { ItemsSource = new[] { "7 ngày", "15 ngày", "30 ngày" }, SelectedIndex = Svc.KeepDays switch { 7 => 0, 15 => 1, _ => 2 } };
        days.SelectionChanged += (_, _) => { Svc.KeepDays = days.SelectedIndex switch { 0 => 7, 1 => 15, _ => 30 }; Svc.Undo.Purge(Svc.KeepDays); };
        var keep = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Label("Giữ bản hoàn tác:"), days } };
        body.Children.Add(keep);
        var close = new Button { Content = "Đóng", IsCancel = true };
        var undo = new Button { Content = "Hoàn tác đợt đang dùng", Classes = { "danger" }, IsEnabled = la is not null && Svc.CanUndo(la, log) };
        var open = new Button { Content = "Mở thư mục" };
        open.Click += (_, _) => { try { Directory.CreateDirectory(Svc.Undo.Root); Ui.OpenExternal(Svc.Undo.Root); } catch { } };
        body.Children.Add(Ui.Buttons(open, undo, close));
        var w = Ui.Dialog("Lịch sử nạp Excel · Tồn kho An An", body, 1000);
        close.Click += (_, _) => w.Close();
        undo.Click += async (_, _) =>
        {
            if (la is null) return;
            if (!await Ui.Confirm(w, $"HOÀN TÁC đợt nạp lúc {NapExcel.When(S(la["time"]))}?\n\nFile: {S(la["fileName"])}\nDữ liệu sẽ trở về như TRƯỚC khi nạp file này.", "Hoàn tác", "Hoàn tác")) return;
            w.Close();
            await Act(() => Svc.UndoNap(S(la["id"])), "Đã hoàn tác đợt nạp " + S(la["fileName"]));
        };
        await Ui.Show(this, w);
    }

    private async Task ShowCompare()
    {
        var r = Svc.CompareWithPrevious();
        if (r is null) { await Ui.Message(this, "Chưa có lần nạp trước để so sánh.\nSau khi nạp file Tồn kho An An MỚI, app sẽ tự giữ lại bản trước đó cho lần so sánh."); return; }
        var (prev, R) = r.Value; var S = R.Sum;
        string Sg(double v, int d = 0) => (v > 0 ? "+" : "") + N(v, 0, d);
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Muted($"Trước: {(prev.FileName.Length > 0 ? prev.FileName : "—")}{(prev.DataDate.Length > 0 ? " (" + prev.DataDate + ")" : "")} → Sau: {(D.FileName.Length > 0 ? D.FileName : "—")}{(D.DataDate.Length > 0 ? " (" + D.DataDate + ")" : "")}"));
        body.Children.Add(Label($"Mới xuất hiện {N(S.Moi)} · Đã hết {N(S.Het)} · Giảm {N(S.Giam)} · Tăng {N(S.Tang)} · Không đổi {N(S.Doi0)} · Tổng kg {N(S.KgBefore, 0, 0)} → {N(S.KgAfter, 0, 0)} ({Sg(S.KgDiff)})", bold: true));
        GridModel G(string head, List<SoSanhNap.Grp> gs)
        {
            var m = new GridModel { ReadOnly = true };
            m.Load(new[] { new GridColumn("n", head, 170), new GridColumn("c", "Số mã", 60, "number"), new GridColumn("b", "Tồn trước", 85, "number"), new GridColumn("a", "Tồn sau", 85, "number"), new GridColumn("d", "Chênh", 80, "number"), new GridColumn("kb", "kg trước", 95, "number"), new GridColumn("ka", "kg sau", 95, "number"), new GridColumn("kd", "Chênh kg", 95, "number") },
                gs.Select(g => new[] { g.Name, N(g.N), N(g.Before, 0, 1), N(g.After, 0, 1), Sg(g.Diff, 1), N(g.KgBefore, 0, 0), N(g.KgAfter, 0, 0), Sg(g.KgDiff) }));
            return m;
        }
        body.Children.Add(Label("Theo nhóm đặc tính", bold: true)); body.Children.Add(Framed(new SheetGrid(G("Nhóm", R.ByDt)) { SpareRows = 0, Height = 150 }));
        body.Children.Add(Label("Theo Size", bold: true)); body.Children.Add(Framed(new SheetGrid(G("Size", R.BySize)) { SpareRows = 0, Height = 150 }));
        var shown = R.Rows.Where(x => x.Status != "doi0").ToList();
        var mm = new GridModel { ReadOnly = true };
        mm.Load(new[] { new GridColumn("m", "Mã hàng", 190), new GridColumn("g", "Nhóm", 110), new GridColumn("s", "Size", 70), new GridColumn("t", "Tình trạng", 95), new GridColumn("b", "Tồn trước", 85, "number"), new GridColumn("a", "Tồn sau", 85, "number"), new GridColumn("d", "Chênh", 80, "number"), new GridColumn("k", "Chênh kg", 95, "number") },
            shown.Select(x => new[] { x.Ma, x.Dt, x.Size, SoSanhNap.StatusLabel(x.Status), N(x.Before, 0, 1), N(x.After, 0, 1), Sg(x.Diff, 1), Sg(x.KgDiff) }));
        body.Children.Add(Label($"Theo mã hàng ({N(shown.Count)}, bỏ mã không đổi)", bold: true)); body.Children.Add(Framed(new SheetGrid(mm) { SpareRows = 0, Height = 220 }));
        var close = new Button { Content = "Đóng", IsCancel = true, Classes = { "primary" } };
        var xls = new Button { Content = "Xuất Excel", IsVisible = _export };
        body.Children.Add(Ui.Buttons(xls, close));
        var w = Ui.Dialog("⇄ So sánh 2 lần nạp — Tồn kho An An", new ScrollViewer { Content = body, MaxHeight = 720 }, 1000);
        close.Click += (_, _) => w.Close();
        xls.Click += async (_, _) => await SaveXlsx(SoSanhNap.Export(R), "so_sanh_2_lan_nap.xlsx");
        await Ui.Show(this, w);
    }

    // =====================================================================================
    // Phân Loại
    // =====================================================================================

    private readonly ComboBox _plMod = new() { MinWidth = 200 };
    private readonly TextBox _plQ = new() { Watermark = "Tìm mã, tên, size, hợp đồng…", Width = 240 };
    private readonly TextBlock _plStats = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _plTh = new() { Width = 60 };
    private readonly StackPanel _plThBox = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly GridModel _plm = new() { ReadOnly = true };
    private SheetGrid _plg = null!;
    private List<int> _plRows = new();
    private readonly StackPanel _plRules = new() { Spacing = 6 };
    private string PlId => AnAnData.FilterModules[Math.Max(0, _plMod.SelectedIndex)].Id;

    private Control BuildPl()
    {
        _plg = new SheetGrid(_plm) { SpareRows = 0, CellBackground = PlBg };
        _plMod.SelectionChanged += (_, _) => { if (!_loading) RenderPl(); };
        _plQ.TextChanged += (_, _) => { if (!_loading) RenderPlGrid(); };
        var bar = Bar();
        bar.Children.Add(_plMod); bar.Children.Add(_plQ);
        _plThBox.Children.Add(Label("Ngưỡng Sắp hết ≤")); _plThBox.Children.Add(_plTh);
        _plThBox.Children.Add(Btn("Áp dụng", () => { Svc.SetTpcThreshold(Ui.TryParseNumber(_plTh.Text, out var v) ? v : AnAnData.TpcThresholdDefault); RenderPl(); }));
        bar.Children.Add(_plThBox);
        if (_export) bar.Children.Add(Btn("⬇ Xuất Excel", async () => await SaveXlsx(AnAnExport.Module(D, PlId, _plRows), AnAnExport.ModuleFileName(PlId, DateTime.Now))));
        if (_edit)
        {
            bar.Children.Add(Btn("Chuyển dòng chọn sang…", async () => await MoveSelected(), tip: "Bôi đen các dòng rồi chọn bảng đích — dòng chỉ còn thuộc bảng đó."));
            bar.Children.Add(Btn("Trả về tự động", async () =>
            {
                var rows = SelectedPlRows();
                if (rows.Count == 0) { await Ui.Message(this, "Bôi đen các dòng cần trả về tự động."); return; }
                await Act(() => Svc.RevertRows(rows), $"Đã trả {rows.Count} dòng về tự động");
            }));
        }
        var head = new StackPanel(); head.Children.Add(bar); head.Children.Add(_plStats);
        var exp = new Expander { Header = "Điều kiện lọc của bảng này", Content = new ScrollViewer { Content = _plRules, MaxHeight = 320 }, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 0) };
        var p = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(exp, Dock.Bottom);
        p.Children.Add(head); p.Children.Add(exp); p.Children.Add(Framed(_plg));
        return p;
    }

    private IBrush? PlBg(int r, int c)
    {
        if (r >= _plRows.Count) return null;
        var i = _plRows[r];
        if (D.ManualAssign.ContainsKey(i)) return ManualBg;
        if (PlId == "tpc" && D.TpcStatus(i) is { } st) return st == "Hết hàng" ? OutBg : st == "Sắp hết" ? LowBg : null;
        return null;
    }

    private List<int> SelectedPlRows()
    {
        var s = _plg.Selection;
        return Enumerable.Range(s.R1, s.Rows).Where(r => r >= 0 && r < _plRows.Count).Select(r => _plRows[r]).ToList();
    }

    private async Task MoveSelected()
    {
        var rows = SelectedPlRows();
        if (rows.Count == 0) { await Ui.Message(this, "Bôi đen các dòng cần chuyển trước."); return; }
        var mods = AnAnData.FilterModules.Where(m => m.Id != PlId).ToList();
        var r = await Ui.Choose(this, $"Chuyển {rows.Count} dòng sang bảng:", "Chuyển dòng", mods.Select(m => m.Label).ToList());
        if (r is null) return;
        await Act(() => Svc.MoveRows(rows, mods[r.Value.Index].Id), $"Đã chuyển {rows.Count} dòng sang {mods[r.Value.Index].Label}");
    }

    private void RenderPl()
    {
        if (_plMod.ItemsSource is null) { _plMod.ItemsSource = AnAnData.FilterModules.Select(m => m.Label).ToList(); _plMod.SelectedIndex = 0; }
        _plThBox.IsVisible = PlId == "tpc";
        _plTh.Text = JsText.JsStr(D.TpcThreshold);
        RenderPlGrid();
        RenderRules();
    }

    private void RenderPlGrid()
    {
        var id = PlId;
        if (!D.HasData) { _plRows = new(); _plStats.Text = "Chưa có dữ liệu."; _plm.Load(new[] { new GridColumn("x", "", 200) }, Array.Empty<string[]>()); _plg.Refresh(); return; }
        var q = JsText.Fuzzy(_plQ.Text);
        var cols = D.FilterCols();
        var basis = D.WeightBasis();
        var all = D.BaseRows(id);
        _plRows = q.Length == 0 ? all : all.Where(i => JsText.Fuzzy(string.Join(" ", cols.Select(c => D.Cell(i, c.Ci)))).Contains(q, StringComparison.Ordinal)).ToList();
        var gc = cols.Select(c => new GridColumn("c" + c.Ci, c.Label, AnAnData.IsNumericCol(c.Label) ? 85 : c.Label is "Mã hàng" ? 190 : c.Label is "Tên hàng" ? 260 : 100, AnAnData.IsNumericCol(c.Label) ? "number" : "text")).ToList();
        if (basis is not null) gc.Add(new GridColumn("kg", "Tổng KL (kg)", 105, "number"));
        if (id == "tpc") gc.Add(new GridColumn("st", "Trạng thái", 90));
        gc.Add(new GridColumn("mv", "Chuyển tay", 80));
        _plm.Load(gc, _plRows.Select(i =>
        {
            var row = cols.Select(c => D.Cell(i, c.Ci)).ToList();
            if (basis is { } b) row.Add(Kg(D.RowWeight(i, b)));
            if (id == "tpc") row.Add(D.TpcStatus(i) ?? "");
            row.Add(D.ManualAssign.ContainsKey(i) ? "✓" : "");
            return row;
        }));
        _plg.Refresh();
        var tc = cols.FirstOrDefault(c => c.Label == "Tồn cuối");
        var maC = cols.FirstOrDefault(c => c.Label == "Mã hàng");
        _plStats.Text = $"{AnAnData.LabelOf(id)}: {N(all.Count)} dòng" + (maC is not null ? $" · {N(all.Select(i => JsText.JsTrim(D.Cell(i, maC.Ci))).Where(x => x.Length > 0).Distinct().Count())} mã hàng" : "")
            + (tc is not null && D.SumCol(all, tc.Ci) is double t ? $" · Tồn cuối {N(t)}" : "") + (D.ModuleWeight(id) is double w ? $" · Tổng KL {Kg(w)} kg" : "")
            + (_plRows.Count != all.Count ? $" · đang lọc {N(_plRows.Count)} dòng" : "")
            + (D.ManualAssign.Count > 0 ? " · nền xanh = dòng chuyển tay" : "") + (id == "tpc" ? " · nền vàng = Sắp hết, cam = Hết hàng" : "");
    }

    private readonly ComboBox _cfCol = new() { MinWidth = 140 };
    private readonly ComboBox _cfOp = new() { MinWidth = 170 };
    private readonly TextBox _cfVal = new() { Width = 200, Watermark = "giá trị (danh sách: cách nhau bằng ;)" };
    private readonly TextBox _cfFrom = new() { Width = 50, Watermark = "từ" };
    private readonly TextBox _cfTo = new() { Width = 50, Watermark = "đến" };

    private void RenderRules()
    {
        var id = PlId;
        _plRules.Children.Clear();
        if (AnAnData.RuleText.TryGetValue(id, out var rules)) foreach (var r in rules) _plRules.Children.Add(Muted("• " + r));
        if (D.HasData)
        {
            var fm = new GridModel { ReadOnly = true };
            fm.Load(new[] { new GridColumn("s", "Bước lọc", 420), new GridColumn("l", "Còn lại", 80, "number"), new GridColumn("r", "Bị loại", 80, "number") }, D.Funnel(id).Select(f => new[] { f.Step, N(f.Left), N(f.Removed) }));
            _plRules.Children.Add(Framed(new SheetGrid(fm) { SpareRows = 0, Height = 150 }));
        }
        var custom = D.Custom.GetValueOrDefault(id) ?? new();
        _plRules.Children.Add(Label($"Điều kiện tự thêm ({custom.Count})", bold: true));
        for (var k = 0; k < custom.Count; k++)
        {
            var idx = k;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(Label(AnAnData.CustomDesc(custom[k])));
            if (_edit) row.Children.Add(Btn("Xóa", async () => await Act(() => Svc.RemoveCustomFilter(id, idx), "Đã xóa điều kiện"), danger: true));
            _plRules.Children.Add(row);
        }
        if (!_edit) return;
        if (_cfOp.ItemsSource is null)
        {
            _cfOp.ItemsSource = AnAnData.CustomOps.Select(o => o.Label).ToList(); _cfOp.SelectedIndex = 0;
            _cfOp.SelectionChanged += (_, _) => { var op = AnAnData.CustomOps[Math.Max(0, _cfOp.SelectedIndex)].Op; _cfFrom.IsVisible = _cfTo.IsVisible = op is "pos_in" or "pos_notin"; _cfVal.IsVisible = op is not ("empty" or "notempty"); };
            _cfFrom.IsVisible = _cfTo.IsVisible = false;
        }
        _cfCol.ItemsSource = D.Headers.Count > 0 ? D.Headers.ToList() : AnAnData.FilterColumns.ToList();
        if (_cfCol.SelectedIndex < 0) _cfCol.SelectedIndex = 0;
        var add = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        add.Children.Add(Label("Thêm:")); add.Children.Add(_cfCol); add.Children.Add(_cfOp); add.Children.Add(_cfVal); add.Children.Add(_cfFrom); add.Children.Add(_cfTo);
        add.Children.Add(Btn("＋ Thêm điều kiện", async () => await AddCf(id), primary: true));
        _plRules.Children.Add(add);
    }

    private async Task AddCf(string id)
    {
        var op = AnAnData.CustomOps[Math.Max(0, _cfOp.SelectedIndex)].Op;
        var col = _cfCol.SelectedItem as string ?? "";
        var val = (_cfVal.Text ?? "").Trim();
        if (col.Length == 0) { await Ui.Message(this, "Chọn cột cần lọc"); return; }
        if (op is not ("empty" or "notempty") && val.Length == 0) { await Ui.Message(this, "Nhập giá trị cần so khớp"); return; }
        if (op is "gt" or "lt" or "gte" or "lte" && double.IsNaN(JsText.LooseNum(val))) { await Ui.Message(this, "Giá trị so sánh phải là số"); return; }
        var cf = new JsonObject { ["id"] = AnAnService.NewCfId(), ["colLabel"] = col, ["op"] = op, ["value"] = val };
        if (op is "pos_in" or "pos_notin")
        {
            if (!int.TryParse(_cfFrom.Text, out var f) || !int.TryParse(_cfTo.Text, out var t) || f < 1 || t < f) { await Ui.Message(this, "Vị trí ký tự không hợp lệ (từ ≥ 1, đến ≥ từ)"); return; }
            if (AnAnData.CfList(val).Count == 0) { await Ui.Message(this, "Nhập ít nhất 1 giá trị"); return; }
            cf["from"] = f; cf["to"] = t;
        }
        if (op is "in" or "notin" && AnAnData.CfList(val).Count == 0) { await Ui.Message(this, "Nhập ít nhất 1 giá trị"); return; }
        if (await Act(() => Svc.AddCustomFilter(id, cf), "Đã thêm điều kiện")) _cfVal.Text = "";
    }

    // =====================================================================================
    // Báo Cáo
    // =====================================================================================

    private readonly GridModel _bcm = new() { ReadOnly = true };
    private readonly GridModel _bdm = new() { ReadOnly = true };
    private readonly GridModel _bhm = new() { ReadOnly = true };
    private SheetGrid _bcg = null!, _bhg = null!;
    private readonly TextBlock _bcKhoGui = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _bcRecon = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private List<AnAnData.HlsoRow> _bhRows = new();

    private Control BuildBc()
    {
        _bcg = new SheetGrid(_bcm) { SpareRows = 0, CellBackground = (r, _) => r == _bcm.RowCount - 1 ? TotalBg : r == 0 ? ManualBg : null };
        _bhg = new SheetGrid(_bhm) { SpareRows = 0, CellBackground = (r, _) => r < _bhRows.Count && _bhRows[r].Kind == "group-title" ? TitleBg : null };
        var bar = Bar();
        if (_export)
        {
            bar.Children.Add(Btn("⬇ Xuất excel tổng tồn", async () => { var d = await AskReportDate(); if (d is not null) await SaveXlsx(AnAnExport.BaoCaoAll(D, d), $"BaoCao_TongTon_7Module_{AnAnExport.Stamp(DateTime.Now)}.xlsx"); }, tip: "Một file Excel chung: tổng tồn + Kho Gửi"));
            bar.Children.Add(Btn("⬇ Xuất excel HLSO", async () => await ExportHlso(), tip: "Cùng cấu trúc file, chỉ gồm Tôm HLSO, Tôm Gia Công, Tôm Nhập Khẩu, Tôm Nguyên Liệu, Tôm Giám Sát"));
            bar.Children.Add(Btn("⬇ Xuất Bảng Tổng Hợp HLSO", async () => await SaveXlsx(AnAnExport.HlsoSummary(D, D.DataDate.Length > 0 ? D.DataDate : Today), $"BangTongHopHLSO_{AnAnExport.Stamp(DateTime.Now)}.xlsx")));
        }
        if (_edit) bar.Children.Add(Btn("Lấy Kho Gửi từ Tồn kho gửi", async () => await PullKhoGui()));
        var left = new StackPanel { Spacing = 6, Width = 440 };
        left.Children.Add(Label("Báo Cáo Tổng Tồn", true, 14));
        left.Children.Add(Framed(new Border { Height = 300, Child = _bcg }));
        left.Children.Add(_bcKhoGui);
        var right = new StackPanel { Spacing = 6, Margin = new Thickness(14, 0, 0, 0) };
        right.Children.Add(Label("Đối Soát: 8 Module vs Load Data", true, 14));
        right.Children.Add(Framed(new Border { Height = 150, Child = new SheetGrid(_bdm) { SpareRows = 0, CellBackground = (r, _) => r is 2 or 4 && _bdm.Get(r, 1) is { Length: > 0 } s && s != "0.00" && s != "-0.00" ? WarnBg : null } }));
        right.Children.Add(_bcRecon);
        var row = new DockPanel();
        DockPanel.SetDock(left, Dock.Left); row.Children.Add(left); row.Children.Add(right);
        var head = new StackPanel { Spacing = 6 };
        head.Children.Add(bar); head.Children.Add(row);
        head.Children.Add(Label("Bảng Tổng Hợp HLSO", true, 14));
        return Page(head, Framed(_bhg));
    }

    private async Task<string?> AskReportDate()
    {
        var t = await Ui.Prompt(this, "Ngày báo cáo (dd/mm/yyyy):", "Ngày báo cáo", D.DataDate.Length > 0 ? D.DataDate : Today);
        if (t is null) return null;
        var v = DataDate.Parse(t);
        if (v.Length == 0) { await Ui.Message(this, "Ngày không hợp lệ — gõ dạng dd/mm/yyyy."); return null; }
        return v;
    }

    private async Task ExportHlso()
    {
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(Label("Chọn sheet cần xuất:"));
        var sum = new CheckBox { Content = "Tổng Tồn", IsChecked = true };
        body.Children.Add(sum);
        var boxes = AnAnExport.HlsoExportIds.Select(id => new CheckBox { Content = AnAnData.LabelOf(id), IsChecked = true, Tag = id }).ToList();
        foreach (var b in boxes) body.Children.Add(b);
        var ok = false;
        var y = new Button { Content = "Xuất", IsDefault = true, Classes = { "primary" } }; var n = new Button { Content = "Thôi", IsCancel = true };
        body.Children.Add(Ui.Buttons(n, y));
        var w = Ui.Dialog("Xuất excel HLSO", body, 360);
        y.Click += (_, _) => { ok = true; w.Close(); }; n.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (!ok) return;
        var pick = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!).ToHashSet();
        var date = "";
        if (sum.IsChecked == true) { var d = await AskReportDate(); if (d is null) return; date = d; }
        await SaveXlsx(AnAnExport.BaoCaoAll(D, date, AnAnExport.HlsoExportIds.ToHashSet(), pick, sum.IsChecked == true), $"BaoCao_TongTon_HLSO_{AnAnExport.Stamp(DateTime.Now)}.xlsx");
    }

    private async Task PullKhoGui()
    {
        AnAnService.KhoGuiInfo info;
        try { info = await Task.Run(Svc.ReadKhoGui); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e)); return; }
        if (info.Rows == 0) { await Ui.Message(this, "Tồn Kho Gửi chưa có dữ liệu — hãy nạp dữ liệu bên Tồn kho gửi trước."); return; }
        var old = D.KhoGui; var dateTxt = info.DataDate.Length > 0 ? $" (số liệu ngày {info.DataDate})" : "";
        if (old is double o && Math.Abs(o - info.Kg) >= 0.005 && !await Ui.Confirm(this, $"Hiện tại: {Kg(o)} kg → Tồn kho gửi{dateTxt}: {Kg(info.Kg)} kg ({N(info.Rows)} dòng). Thay bằng số của Tồn kho gửi?", "Thay số Kho Gửi?", "Thay")) return;
        await Act(() => Svc.SyncKhoGui(true), $"Đã nạp Kho Gửi: {Kg(info.Kg)} kg từ Tồn kho gửi{dateTxt}");
    }

    private void RenderBc()
    {
        var rows = D.TongTonRows();
        double? total = null; foreach (var r in rows) if (r.Weight is double w && !double.IsNaN(w)) total = (total ?? 0) + w;
        _bcm.Load(new[] { new GridColumn("l", "Tên hàng", 200), new GridColumn("w", "Trọng lượng (kg)", 170, "number") },
            rows.Select(r => new[] { r.Label, Kg(r.Weight) }).Append(new[] { "TỔNG CỘNG", Kg(total) }));
        _bcg.Refresh();
        _bcKhoGui.Text = $"Báo Cáo Kho Gửi: {(D.KhoGui is double kg ? Kg(kg) + " kg" : "—")} · “Bột + Sốt + Bếp” sửa ở tab Tổng Hợp › Báo cáo ngoài sổ sách.";
        var all = D.TotalWeightAll(); var (hl, hasHl) = D.HlsoGrandTotal();
        double? Diff(double? a, double? b) => a is double x && b is double y ? x - y : null;
        _bdm.Load(new[] { new GridColumn("l", "", 270), new GridColumn("v", "Trọng lượng (kg)", 140, "number") }, new[]
        {
            new[] { "Tổng toàn bộ dữ liệu Load Data", Kg(all) }, new[] { "Tổng cộng 8 module (bảng bên trái)", Kg(total) }, new[] { "Chênh lệch", Kg(Diff(total, all)) },
            new[] { "Tổng Bảng Tổng Hợp HLSO", hasHl ? Kg(hl) : "—" }, new[] { "Chênh lệch (HLSO vs 8 module)", hasHl ? Kg(Diff(hl, total)) : "—" },
        });
        var rc = D.Reconcile();
        _bcRecon.Text = rc is null ? "" : $"Dòng thuộc từ 2 bảng trở lên: {N(rc.Overlap.Count)} (đếm dư {Kg(rc.OverlapExcess)} kg) · Dòng không thuộc bảng nào: {N(rc.Orphan.Count)} ({Kg(rc.OrphanWeight)} kg)"
            + (D.HlsoGap() is { } gap && gap.Rows.Count > 0 ? $" · Dòng HLSO chưa gộp đặc tính: {N(gap.Rows.Count)} ({Kg(gap.Weight)} kg)" : "")
            + string.Concat(rc.ColumnWarnings.Select(w => $"\n⚠ {w.Label}: thiếu cột {string.Join(", ", w.Missing)}"))
            + (rc.Orphan.Count > 0 ? "\nDòng không thuộc bảng nào: " + string.Join(", ", rc.Orphan.Take(8).Select(o => o.Ma)) + (rc.Orphan.Count > 8 ? " …" : "") : "");
        // Bảng Tổng Hợp HLSO: chỉ các cột có số
        _bhRows = D.HlsoRows();
        var withData = D.HlsoColumnsWithData();
        var cols = AnAnData.HlsoColumns.Select((g, k) => (g, k)).Where(x => withData[x.k]).ToList();
        var gc = new List<GridColumn> { new("t", "Tiêu Đề", 200) };
        gc.AddRange(cols.Select(x => new GridColumn("g" + x.k, x.g, 95, "number")));
        gc.Add(new GridColumn("tot", "Tổng", 110, "number"));
        _bhm.Load(gc, _bhRows.Select(r => new[] { r.Display }.Concat(cols.Select(x => Kg(D.HlsoCell(r, x.g)))).Append(Kg(D.HlsoTotal(r)))));
        _bhg.Refresh();
    }

    // =====================================================================================
    // Theo Dõi Tồn
    // =====================================================================================

    private readonly ComboBox _tdF = new() { MinWidth = 160 };
    private readonly TextBox _tdQ = new() { Watermark = "Tìm mã, tên, hợp đồng, ghi chú…", Width = 240 };
    private readonly TextBox _tdMonths = new() { Width = 50 };
    private readonly TextBlock _tdStats = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly GridModel _tdm = new();
    private SheetGrid _tdg = null!;
    private TheoDoiTon.ListResult? _tdRes;
    private List<TheoDoiTon.Item> _tdItems = new();
    private List<string[]> _tdSnap = new();
    private const int TdNoteCol = 9;
    private static readonly (string Key, string Text)[] TdFilters = { ("all", "Tất cả"), ("low", "Gần hết"), ("old", "Tồn lâu"), ("both", "Gần hết + Tồn lâu"), ("nonote", "Chưa có ghi chú"), ("note", "Đã có ghi chú") };

    private Control BuildTd()
    {
        _tdg = new SheetGrid(_tdm) { SpareRows = 0, CellBackground = (r, c) => r < _tdItems.Count && c == 5 && _tdItems[r].IsLow ? LowBg : r < _tdItems.Count && c == 7 && _tdItems[r].IsOld ? WarnBg : c == TdNoteCol && _edit ? PickBg : null };
        _tdF.ItemsSource = TdFilters.Select(f => f.Text).ToList(); _tdF.SelectedIndex = 0;
        _tdF.SelectionChanged += (_, _) => { if (!_loading) RenderTd(); };
        _tdQ.TextChanged += (_, _) => { if (!_loading) RenderTd(); };
        _tdm.Changed += () => { if (!_loading) OnTdEdited(); };
        var bar = Bar();
        bar.Children.Add(_tdF); bar.Children.Add(_tdQ);
        bar.Children.Add(Label("Tồn lâu ≥")); bar.Children.Add(_tdMonths); bar.Children.Add(Label("tháng"));
        bar.Children.Add(Btn("Áp dụng", async () =>
        {
            if (!int.TryParse(_tdMonths.Text, out var m) || m < 1) { await Ui.Message(this, "Số tháng phải ≥ 1."); return; }
            Svc.Tdt.Months = m;
            await Act(Svc.SaveTdt, "Đã lưu ngưỡng tháng");
        }));
        if (_edit) bar.Children.Add(Btn("✓ Xác nhận ghi chú dòng chọn", async () =>
        {
            var s = _tdg.Selection; var keys = Enumerable.Range(s.R1, s.Rows).Where(r => r >= 0 && r < _tdItems.Count).Select(r => _tdItems[r].G.Key).ToList();
            var n = keys.Count(k => Svc.Tdt.Confirm(k));
            if (n == 0) { await Ui.Message(this, "Bôi đen các dòng đã có ghi chú cần xác nhận (ghi chú vẫn đúng ở lần nạp này)."); return; }
            await Act(Svc.SaveTdt, $"Đã xác nhận {n} ghi chú");
        }));
        if (_export) bar.Children.Add(Btn("⬇ Xuất Excel", async () => { if (_tdRes is not null) await SaveXlsx(TheoDoiTon.Export(_tdRes, _tdItems), TheoDoiTon.FileName(D)); }));
        var head = new StackPanel(); head.Children.Add(bar); head.Children.Add(_tdStats);
        head.Children.Add(Muted(_edit ? "Gõ lý do còn tồn vào cột “Lý do còn tồn / ghi chú ✎” (Enter để lưu; xóa trắng = bỏ ghi chú). Ghi chú giữ qua các lần nạp." : "Chỉ xem."));
        return Page(head, Framed(_tdg));
    }

    private void RenderTd()
    {
        _tdMonths.Text = Svc.Tdt.Months.ToString(CultureInfo.InvariantCulture);
        _tdRes = Svc.Tdt.Compute(D, out var changed);
        if (changed) { try { Svc.SaveTdt(); } catch { } }
        if (_tdRes is null) { _tdItems = new(); _tdStats.Text = "Chưa có dữ liệu Thành Phẩm Chính."; _tdm.Load(new[] { new GridColumn("x", "", 200) }, Array.Empty<string[]>()); _tdg.Refresh(); return; }
        _tdItems = TheoDoiTon.Visible(_tdRes, TdFilters[Math.Max(0, _tdF.SelectedIndex)].Key, _tdQ.Text ?? "");
        _tdStats.Text = $"Tháng {TheoDoiTon.YmLabel(_tdRes.CurYm)} · Gần hết (≤ {N(_tdRes.Th)}): {N(_tdRes.Items.Count(i => i.IsLow))} · Tồn lâu (≥ {Svc.Tdt.Months} tháng): {N(_tdRes.Items.Count(i => i.IsOld))} · đang hiện {N(_tdItems.Count)} · đã hết (có ghi chú cũ): {N(_tdRes.Done.Count)}";
        var cols = new[]
        {
            new GridColumn("ma", "Mã hàng", 190) { ReadOnly = true }, new GridColumn("ten", "Tên hàng", 220) { ReadOnly = true }, new GridColumn("size", "Size", 60) { ReadOnly = true },
            new GridColumn("dt", "Đặc tính", 100) { ReadOnly = true }, new GridColumn("hd", "Hợp đồng", 120) { ReadOnly = true }, new GridColumn("ton", "Tồn cuối", 75, "number") { ReadOnly = true },
            new GridColumn("loai", "Loại", 115) { ReadOnly = true }, new GridColumn("thang", "Số tháng", 70, "number") { ReadOnly = true }, new GridColumn("tu", "Từ tháng", 75) { ReadOnly = true },
            new GridColumn("note", _edit ? "Lý do còn tồn / ghi chú ✎" : "Lý do còn tồn / ghi chú", 300) { ReadOnly = !_edit }, new GridColumn("when", "Ngày ghi", 140) { ReadOnly = true },
            new GridColumn("dvt", "ĐVT", 50) { ReadOnly = true },
        };
        _tdm.ReadOnly = !_edit;
        _tdm.Load(cols, _tdItems.Select(it => new[]
        {
            it.G.Ma, it.G.Ten, it.G.Size, it.Dt, it.G.Hd, N(it.G.Qty),
            string.Join(" + ", new[] { it.IsLow ? "Gần hết" : "", it.IsOld ? "Tồn lâu" : "" }.Where(x => x.Length > 0)),
            (it.Age.Estimated ? "~" : "") + it.Age.Months, TheoDoiTon.YmLabel(it.Age.StartYm), it.Note is null ? "" : AnAnStore.S(it.Note["text"]), TheoDoiTon.WhenText(it.Note), it.G.Dvt,
        }));
        _tdSnap = _tdm.Snapshot();
        _tdg.Refresh();
    }

    private void OnTdEdited()
    {
        var now = _tdm.Snapshot(); var changed = false;
        for (var r = 0; r < Math.Min(now.Count, _tdItems.Count); r++)
        {
            var before = r < _tdSnap.Count ? _tdSnap[r][TdNoteCol] : ""; var after = now[r][TdNoteCol] ?? "";
            if (before == after) continue;
            changed |= Svc.Tdt.SaveNote(_tdItems[r].G.Key, after, _tdItems[r].G);
        }
        if (!changed) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(async () => await Act(Svc.SaveTdt, "Đã lưu ghi chú"));
    }

    // =====================================================================================
    // Đặc Tính
    // =====================================================================================

    private readonly GridModel _dtm = new() { ReadOnly = true };
    private readonly GridModel _dgm = new() { ReadOnly = true };
    private SheetGrid _dtg = null!;
    private List<AnAnData.DtRow> _dtRows = new();
    private readonly TextBlock _dtStats = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };

    private Control BuildDt()
    {
        _dtg = new SheetGrid(_dtm) { SpareRows = 0, CellBackground = (r, _) => r < _dtRows.Count && !_dtRows[r].Present ? WarnBg : r < _dtRows.Count && D.GroupOf(_dtRows[r].Label) is not null ? ManualBg : null };
        var bar = Bar();
        if (_edit)
        {
            bar.Children.Add(Btn("Gộp dòng chọn vào nhóm…", async () =>
            {
                var labels = SelectedDt();
                if (labels.Count == 0) { await Ui.Message(this, "Bôi đen các đặc tính cần gộp."); return; }
                var r = await Ui.Choose(this, $"Gộp {labels.Count} đặc tính vào nhóm:", "Gộp đặc tính", AnAnData.HlsoColumns.ToList());
                if (r is null) return;
                await Act(() => Svc.AssignGroup(labels, AnAnData.HlsoColumns[r.Value.Index]), $"Đã gộp {labels.Count} đặc tính vào {AnAnData.HlsoColumns[r.Value.Index]}");
            }, primary: true));
            bar.Children.Add(Btn("Bỏ gộp dòng chọn", async () =>
            {
                var labels = SelectedDt();
                if (labels.Count == 0) { await Ui.Message(this, "Bôi đen các đặc tính cần bỏ gộp."); return; }
                await Act(() => { foreach (var l in labels) Svc.Ungroup(AnAnData.LabelKey(l)); }, $"Đã bỏ gộp {labels.Count} đặc tính");
            }));
            bar.Children.Add(Btn("Xóa danh sách đặc tính", async () =>
            {
                if (!await Ui.Confirm(this, "Xóa toàn bộ danh sách đặc tính đã ghi nhận và các nhóm đã gộp? Đặc tính đang có trong dữ liệu sẽ được ghi nhận lại.", "Xóa Đặc Tính", "Xóa")) return;
                await Act(Svc.ClearDacTinh, "Đã xóa danh sách đặc tính");
            }, danger: true));
        }
        if (_export)
        {
            bar.Children.Add(Btn("⬇ Xuất Đặc Tính", async () => await SaveXlsx(AnAnExport.DacTinh(_dtRows), $"DacTinh_{AnAnExport.Stamp(DateTime.Now)}.xlsx")));
            bar.Children.Add(Btn("⬇ Xuất Gộp Đặc Tính", async () => await SaveXlsx(AnAnExport.GopDacTinh(D), $"GopDacTinh_{AnAnExport.Stamp(DateTime.Now)}.xlsx")));
        }
        var head = new StackPanel(); head.Children.Add(bar); head.Children.Add(_dtStats);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,420") };
        body.Children.Add(Framed(_dtg));
        var right = new DockPanel();
        var rl = Label("Gộp Đặc Tính (nhóm của Bảng Tổng Hợp HLSO)", bold: true); DockPanel.SetDock(rl, Dock.Top);
        right.Children.Add(rl); right.Children.Add(Framed(new SheetGrid(_dgm) { SpareRows = 0 }));
        Grid.SetColumn(right, 2); body.Children.Add(right);
        return Page(head, body);
    }

    private List<string> SelectedDt()
    {
        var s = _dtg.Selection;
        return Enumerable.Range(s.R1, s.Rows).Where(r => r >= 0 && r < _dtRows.Count).Select(r => _dtRows[r].Label).ToList();
    }

    private void RenderDt()
    {
        _dtRows = D.DacTinhBreakdown(out var known);
        if (known && _edit) { try { Svc.SaveDacTinh(); } catch { } }
        _dtm.Load(new[] { new GridColumn("l", "Đặc Tính", 220), new GridColumn("n", "Số dòng", 75, "number"), new GridColumn("w", "Trọng lượng (kg)", 130, "number"), new GridColumn("g", "Nhóm đã gộp", 200) },
            _dtRows.Select(r => new[] { r.Label, r.Present ? N(r.Count) : "", r.HasWeight ? Kg(r.Weight) : "", D.GroupOf(r.Label) ?? "" }));
        _dtg.Refresh();
        _dtStats.Text = $"{N(_dtRows.Count)} đặc tính · {N(_dtRows.Count(r => D.GroupOf(r.Label) is not null))} đã gộp · nền cam = không còn trong dữ liệu hiện tại, xanh = đã gộp";
        var rows = new List<string[]>();
        foreach (var g in AnAnData.HlsoColumns)
        {
            var m = D.GroupMembers(g);
            if (m.Count == 0) rows.Add(new[] { g, "" }); else rows.AddRange(m.Select(x => new[] { g, x.Label }));
        }
        _dgm.Load(new[] { new GridColumn("g", "Nhóm", 170), new GridColumn("d", "Đặc Tính", 200) }, rows);
    }

    // =====================================================================================
    // Tổng Hợp: Xuất Hàng + Báo cáo ngoài sổ sách
    // =====================================================================================

    private readonly ComboBox _xhMod = new() { MinWidth = 200 };
    private readonly TextBox _xhQ = new() { Watermark = "Tìm mã, tên, size…", Width = 220 };
    private readonly GridModel _xhm = new();
    private SheetGrid _xhg = null!;
    private List<int> _xhRows = new();
    private List<string[]> _xhSnap = new();
    private readonly Dictionary<int, double> _xhSel = new();
    private int _xhQtyCol;
    private readonly TextBlock _xhInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly GridModel _xsm = new() { ReadOnly = true };
    private SheetGrid _xsg = null!;
    private readonly TextBox _ngssManual = new() { Width = 160 };
    private readonly TextBox _ngssKhoGui = new() { Width = 160 };

    private static readonly string[] XhSources = { "tpc", "tptam", "bm", "hlso", "tomgc", "tomnk", "tomnl", "tomgs" };

    private Control BuildTh()
    {
        _xhg = new SheetGrid(_xhm) { SpareRows = 0, CellBackground = (r, c) => c == _xhQtyCol && _edit ? PickBg : r < _xhRows.Count && _xhSel.ContainsKey(_xhRows[r]) ? LowBg : null };
        _xsg = new SheetGrid(_xsm) { SpareRows = 0, CellBackground = (r, c) => c == 5 && r < Svc.Slips.Count ? (AnAnService.SlipDeducted(Svc.Slips[r]) ? ManualBg : LowBg) : null };
        _xhMod.ItemsSource = XhSources.Select(AnAnData.LabelOf).ToList(); _xhMod.SelectedIndex = 0;
        _xhMod.SelectionChanged += (_, _) => { if (!_loading) { _xhSel.Clear(); RenderXh(); } };
        _xhQ.TextChanged += (_, _) => { if (!_loading) RenderXh(); };
        _xhm.Changed += () => { if (!_loading) OnXhEdited(); };

        var bar = Bar();
        bar.Children.Add(Label("Module nguồn:")); bar.Children.Add(_xhMod); bar.Children.Add(_xhQ);
        if (_edit)
        {
            bar.Children.Add(Btn("Tạo phiếu xuất", async () =>
            {
                if (_xhSel.Count == 0) { await Ui.Message(this, "Gõ số lượng xuất vào cột “SL xuất ✎” trước."); return; }
                var id = XhSources[Math.Max(0, _xhMod.SelectedIndex)];
                JsonObject? s = null;
                if (await Act(() => s = Svc.CreateSlip(id, _xhSel)) && s is not null) { _xhSel.Clear(); _status.Text = $"Đã tạo phiếu {AnAnService.SlipId(s)} — chưa trừ tồn (bấm “Trừ tồn kho” ở danh sách phiếu)."; RenderTh(); }
            }, primary: true));
            bar.Children.Add(Btn("Bỏ chọn", () => { _xhSel.Clear(); RenderXh(); }));
        }
        var top = new DockPanel();
        var th = new StackPanel(); th.Children.Add(bar); th.Children.Add(_xhInfo);
        DockPanel.SetDock(th, Dock.Top); top.Children.Add(th); top.Children.Add(Framed(_xhg));

        var sbar = Bar();
        sbar.Children.Add(Label("Phiếu xuất đã tạo", bold: true));
        if (_edit)
        {
            sbar.Children.Add(Btn("Trừ tồn kho", async () => await SlipAct(true), primary: true));
            sbar.Children.Add(Btn("Hủy trừ tồn kho", async () => await SlipAct(false)));
        }
        var bottom = new DockPanel();
        DockPanel.SetDock(sbar, Dock.Top); bottom.Children.Add(sbar); bottom.Children.Add(Framed(_xsg));
        var xh = new Grid { RowDefinitions = new RowDefinitions("3*,8,2*") };
        xh.Children.Add(top); Grid.SetRow(bottom, 2); xh.Children.Add(bottom);

        var ngss = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        ngss.Children.Add(Muted("2 số này không có trong file tồn kho: nhập tay (Kho Gửi tự lấy từ Tồn kho gửi khi dữ liệu bên đó đổi). Dùng cho Báo Cáo Tổng Tồn và file báo cáo."));
        StackPanel Row(string l, TextBox b, string key)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            p.Children.Add(new TextBlock { Text = l, Width = 170, VerticalAlignment = VerticalAlignment.Center }); p.Children.Add(b); p.Children.Add(Label("kg"));
            b.IsReadOnly = !_edit;
            if (_edit) p.Children.Add(Btn("Lưu", async () =>
            {
                var t = (b.Text ?? "").Trim();
                double? v = null;
                if (t.Length > 0) { var n = JsText.LooseNum(t); if (double.IsNaN(n)) { await Ui.Message(this, "Giá trị phải là số."); return; } v = n; }
                await Act(() => Svc.SetManualValue(key, v), "Đã lưu " + key);
            }));
            return p;
        }
        ngss.Children.Add(Row(AnAnData.ManualLabel, _ngssManual, AnAnData.ManualLabel));
        ngss.Children.Add(Row(AnAnData.KhoGuiLabel, _ngssKhoGui, AnAnData.KhoGuiLabel));
        if (_edit) ngss.Children.Add(Btn("Lấy Kho Gửi từ Tồn kho gửi", async () => await PullKhoGui()));

        return new TabControl
        {
            Items =
            {
                new TabItem { Header = "Xuất Hàng", Content = xh },
                new TabItem { Header = "Báo cáo ngoài sổ sách", Content = ngss },
            },
        };
    }

    private async Task SlipAct(bool deduct)
    {
        var r = _xsg.Row;
        if (r < 0 || r >= Svc.Slips.Count) { await Ui.Message(this, "Chọn một phiếu trước."); return; }
        var s = Svc.Slips[r]; var id = AnAnService.SlipId(s); var label = AnAnStore.S(s["moduleLabel"]);
        if (deduct == AnAnService.SlipDeducted(s)) { await Ui.Message(this, deduct ? "Phiếu này đã trừ tồn." : "Phiếu này chưa trừ tồn."); return; }
        var ok = deduct
            ? await Ui.Confirm(this, $"Xác nhận trừ tồn kho cho phiếu {id} (module \"{label}\")?\nSố lượng tương ứng sẽ bị trừ khỏi cột \"Tồn cuối\" của module nguồn.", "Trừ tồn kho", "Trừ tồn")
            : await Ui.Confirm(this, $"Xác nhận hủy trừ tồn kho cho phiếu {id} (module \"{label}\")?\nSố lượng đã trừ trước đó sẽ được cộng lại vào cột \"Tồn cuối\".", "Hủy trừ tồn kho", "Hủy trừ");
        if (!ok) return;
        await Act(() => { if (deduct) Svc.DeductSlip(id); else Svc.UndoSlip(id); }, (deduct ? "Đã trừ tồn phiếu " : "Đã hủy trừ phiếu ") + id);
    }

    private void RenderTh()
    {
        RenderXh();
        _xsm.Load(new[] { new GridColumn("id", "Mã phiếu", 100), new GridColumn("m", "Module nguồn", 150), new GridColumn("d", "Ngày tạo", 95), new GridColumn("n", "Số dòng", 70, "number"), new GridColumn("q", "Tổng SL", 85, "number"), new GridColumn("st", "Trạng thái", 110) },
            Svc.Slips.Select(s => new[]
            {
                AnAnStore.S(s["tam"]) == "true" ? "Chờ mã máy chủ" : AnAnService.SlipId(s), AnAnStore.S(s["moduleLabel"]),
                DateTimeOffset.TryParse(AnAnStore.S(s["createdAt"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime().ToString("d/M/yyyy", CultureInfo.InvariantCulture) : "",
                N(AnAnService.SlipLines(s).Count), N(AnAnService.SlipTotal(s)), AnAnService.SlipDeducted(s) ? "Đã trừ tồn" : "Chưa trừ tồn",
            }));
        _xsg.Refresh();
        _ngssManual.Text = D.ManualValues.GetValueOrDefault(AnAnData.LabelKey(AnAnData.ManualLabel)) is double m ? JsText.JsStr(m) : "";
        _ngssKhoGui.Text = D.KhoGui is double k ? JsText.JsStr(k) : "";
    }

    private void RenderXh()
    {
        var id = XhSources[Math.Max(0, _xhMod.SelectedIndex)];
        if (!D.HasData) { _xhRows = new(); _xhInfo.Text = "Chưa có dữ liệu."; _xhm.Load(new[] { new GridColumn("x", "", 200) }, Array.Empty<string[]>()); _xhg.Refresh(); return; }
        var cols = D.FilterCols().Where(c => c.Label is "Mã hàng" or "Tên hàng" or "Size" or "Đặc tính" or "Hợp đồng" or "Tồn cuối" or "Trọng lượng").ToList();
        var q = JsText.Fuzzy(_xhQ.Text);
        var all = D.BaseRows(id);
        _xhRows = q.Length == 0 ? all : all.Where(i => _xhSel.ContainsKey(i) || JsText.Fuzzy(string.Join(" ", cols.Select(c => D.Cell(i, c.Ci)))).Contains(q, StringComparison.Ordinal)).ToList();
        var gc = cols.Select(c => new GridColumn("c" + c.Ci, c.Label, AnAnData.IsNumericCol(c.Label) ? 85 : c.Label is "Mã hàng" ? 190 : c.Label is "Tên hàng" ? 260 : 100, AnAnData.IsNumericCol(c.Label) ? "number" : "text") { ReadOnly = true }).ToList();
        _xhQtyCol = gc.Count;
        gc.Add(new GridColumn("x", _edit ? "SL xuất ✎" : "SL xuất", 85, "number") { ReadOnly = !_edit });
        _xhm.ReadOnly = !_edit;
        _xhm.Load(gc, _xhRows.Select(i => cols.Select(c => D.Cell(i, c.Ci)).Append(_xhSel.TryGetValue(i, out var v) ? Raw(v) : "")));
        _xhSnap = _xhm.Snapshot();
        _xhg.Refresh();
        _xhInfo.Text = $"{AnAnData.LabelOf(id)}: {N(all.Count)} dòng · đang chọn {N(_xhSel.Count)} dòng, tổng SL {N(_xhSel.Values.Sum())}. Phiếu chỉ GHI NHẬN — trừ tồn khi bấm “Trừ tồn kho”.";
    }

    private void OnXhEdited()
    {
        var now = _xhm.Snapshot();
        for (var r = 0; r < Math.Min(now.Count, _xhRows.Count); r++)
        {
            var before = r < _xhSnap.Count ? _xhSnap[r][_xhQtyCol] : ""; var after = (now[r][_xhQtyCol] ?? "").Trim();
            if (before == after) continue;
            var i = _xhRows[r];
            if (after.Length == 0 || !Ui.TryParseNumber(after, out var v) || v <= 0) _xhSel.Remove(i); else _xhSel[i] = v;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { _loading = true; try { RenderXh(); } finally { _loading = false; } });
    }
}

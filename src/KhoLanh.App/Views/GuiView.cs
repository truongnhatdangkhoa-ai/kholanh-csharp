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
using KhoLanh.Core.TonKhoGui;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Tồn kho gửi viết lại bằng C# (thay bản WebView2 ton-kho-gui.html) — cùng nghiệp vụ, dùng chung database "ton-kho-gui" với app cũ.
/// Tab: Load Data (nạp file + bảng dữ liệu đang lưu, như Tồn kho An An) · Phiếu Xuất · Phân Loại (6 nhóm + Điều kiện lọc) · Đặc Tính · Báo Cáo · Đối Soát.
/// </summary>
public sealed class GuiView : UserControl, IPage
{
    public string Title => "Tồn kho gửi";
    public string Subtitle => "Nạp file tồn kho gửi, phân loại 6 nhóm, đặc tính, Bảng Tổng Hợp, đối soát, phiếu xuất — dùng chung dữ liệu với app cũ.";

    private static GuiService Svc => App.Services.Gui;
    private static GuiData D => Svc.D;
    private static readonly IBrush Line = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush TitleBg = AppTheme.Brush("#DDEBF7");
    private static readonly IBrush GroupBg = AppTheme.Brush("#D6EAD3");
    private static readonly IBrush WarnBg = AppTheme.Brush("#FCE4D6");
    private static readonly IBrush LowBg = AppTheme.Brush("#FFE699");
    private static readonly IBrush OutBg = AppTheme.Brush("#F8CBAD");
    private static readonly IBrush OkBg = AppTheme.Brush("#E2EFDA");
    private static readonly IBrush PickBg = AppTheme.Brush("#FFF1C9");

    private readonly bool _edit, _export;
    private bool _loading;
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
    private readonly TextBlock _fileInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TabControl _tabs;

    public GuiView()
    {
        _edit = Svc.CanEdit; _export = Svc.CanExport;
        _dl = new RowsPanel(this, () => D.Rows, "Du lieu", "du-lieu.xlsx", true, BuildLoadBar());
        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Load Data", Content = _dl.Root },   // 1.62: bảng dữ liệu nằm ngay trong Load Data (bỏ tab Dữ Liệu riêng)
                new TabItem { Header = "Phiếu Xuất", Content = BuildPx() },
                new TabItem { Header = "Phân Loại", Content = BuildPl() },
                new TabItem { Header = "Đặc Tính", Content = BuildDt() },
                new TabItem { Header = "Báo Cáo", Content = BuildBc() },
                new TabItem { Header = "Đối Soát", Content = BuildDs() },
            },
        };
        _tabs.SelectionChanged += (_, _) => { if (!_loading && Svc.Loaded) RenderTab(); };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(_status, Dock.Right);
        top.Children.Add(_status); top.Children.Add(_fileInfo);
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top);
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
        if (primary) b.Classes.Add("primary"); if (danger) b.Classes.Add("danger");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
    private static TextBlock Label(string t, bool bold = false, double size = 12.5) => new() { Text = t, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Muted(string t) => new() { Text = t, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private static Border Framed(Control c) => new() { BorderBrush = Line, BorderThickness = new Thickness(1), Child = c };
    private static WrapPanel Bar() => new() { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 8, 0, 6) };
    private static string N(double v, int min = 0, int max = 2) => JsText.En(v, min, max);
    private static string Kg(double v) => JsText.En(v, 2, 2);
    private static string Raw(double v) => JsText.JsStr(Math.Round(v, 6));
    private static string Nap(double ms) => ms > 0 ? DateTime.UnixEpoch.AddMilliseconds(ms).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "—";
    private static DockPanel Page(Control head, Control body) { var p = new DockPanel(); DockPanel.SetDock(head, Dock.Top); p.Children.Add(head); p.Children.Add(body); return p; }

    private async Task LoadAsync()
    {
        _status.Text = "Đang tải…";
        try { await Task.Run(Svc.Load); }
        catch (Exception ex) { _status.Text = ""; await Ui.Message(this, ex is BusinessException ? ex.Message : Ui.Describe(ex)); return; }
        _status.Text = ""; RenderAll();
        if (!D.HasData) _tabs.SelectedIndex = 0;
    }

    private async Task<bool> Act(Action a, string? ok = null)
    {
        try { a(); if (ok is not null) _status.Text = ok + " (" + DateTime.Now.ToString("HH:mm:ss") + ")"; RenderAll(); return true; }
        catch (SheetConflictException)
        {
            await Ui.Message(this, "Dữ liệu Tồn kho gửi vừa được máy khác sửa nên thao tác này CHƯA được lưu (để không ghi đè). Bấm Đóng để tải bản mới nhất rồi làm lại.", "Máy khác vừa sửa");
            await LoadAsync();
        }
        catch (BusinessException e) { await Ui.Message(this, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); }
        RenderAll(); return false;
    }

    private async Task SaveXlsx(XlsxOut? x, string fileName)
    {
        if (x is null) { await Ui.Message(this, "Không có gì để xuất."); return; }
        var path = await Ui.SaveFile(this, "Xuất Excel", fileName, "xlsx", "Excel"); if (path is null) return;
        if (await Ui.Run(this, () => x.Save(path))) Ui.OpenExternal(path);
    }

    private void RenderAll()
    {
        _fileInfo.Text = !D.HasData
            ? "Chưa có dữ liệu — bấm “Nạp file Excel” ở tab Load Data."
            : $"Số liệu ngày {(D.DataDate.Length > 0 ? D.DataDate : "— (chưa nhập)")} · {N(D.Rows.Count)} dòng · tổng {Kg(D.Rows.Sum(r => r.TrongLuong))} kg · nạp lúc {Nap(D.ImportedAt)}";
        RenderTab();
    }

    private void RenderTab()
    {
        _loading = true;
        try
        {
            switch (_tabs.SelectedIndex)
            {
                case 0: RenderLoad(); _dl.Render(); break; case 1: RenderPx(); break; case 2: RenderPl(); break;
                case 3: RenderDt(); break; case 4: RenderBc(); break; case 5: RenderDs(); break;
            }
        }
        finally { _loading = false; }
    }

    // =====================================================================================
    // Bảng dòng tồn dùng chung (Load Data + 6 nhóm Phân Loại)
    // =====================================================================================
    private sealed class RowsPanel
    {
        public readonly Control Root; private readonly GuiView _v; private readonly Func<List<GuiRow>> _src; private readonly string _sheet, _file;
        private readonly GridModel _m = new() { ReadOnly = true }; private readonly SheetGrid _g; private readonly TextBox _q = new() { Watermark = "Tìm trong mọi cột…", Width = 260 };
        private readonly TextBlock _info = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        private List<GuiRow> _cur = new();
        public RowsPanel(GuiView v, Func<List<GuiRow>> src, string sheet, string file, bool clearable, Control? top = null)
        {
            _v = v; _src = src; _sheet = sheet; _file = file;
            _g = new SheetGrid(_m) { SpareRows = 0, CellBackground = (r, _) => r < _cur.Count && _cur[r].PxDaXuat ? WarnBg : null };
            var bar = Bar(); bar.Children.Add(_q);
            if (v._export) bar.Children.Add(Btn("⬇ Xuất Excel", async () => await v.SaveXlsx(GuiExport.Rows(D, _cur, sheet), file)));
            if (clearable && v._edit) bar.Children.Add(Btn("Xóa dữ liệu đã lưu", async () =>
            {
                if (!await Ui.Confirm(v, "Xóa toàn bộ dữ liệu đã nạp ở Load Data?\n\nPhiếu xuất, Đặc Tính và điều kiện lọc vẫn giữ nguyên.", "Xóa dữ liệu", "Xóa")) return;
                await v.Act(Svc.ClearRows, "Đã xóa dữ liệu");
            }, danger: true));
            _q.TextChanged += (_, _) => { if (!v._loading) Render(); };
            var head = new StackPanel(); if (top is not null) head.Children.Add(top); head.Children.Add(bar); head.Children.Add(_info);
            Root = Page(head, new DetailPane(Framed(_g), _g, "Chi tiết dòng"));
        }
        public void Render()
        {
            var all = _src(); var q = JsText.Fuzzy(_q.Text);
            _cur = q.Length == 0 ? all : all.Where(r => JsText.Fuzzy(string.Join(" ", GuiData.TargetCols.Select(c => c.Key == "dacTinh" ? D.DeriveDacTinh(r.TenHang, r.SoLo) : GuiData.Get(r, c.Key)))).Contains(q, StringComparison.Ordinal)).ToList();
            _info.Text = $"{N(_cur.Count)} / {N(all.Count)} dòng · Số kiện {N(_cur.Sum(r => r.SoKien))} · Trọng lượng {Kg(_cur.Sum(r => r.TrongLuong))} kg" + (_cur.Any(r => r.PxDaXuat) ? " · dòng tô đỏ = đã trừ theo phiếu xuất" : "");
            var cols = GuiData.TargetCols.Select(c => new GridColumn(c.Key, c.Label, c.Key switch { "maHang" => 190, "tenHang" => 300, "dacTinh" => 110, "soLo" => 140, "phieuNhap" => 140, "trongLuong" => 110, "soKien" => 80, _ => 100 }, c.Key is "soKien" or "trongLuong" ? "number" : "text") { ReadOnly = true }).ToList();
            _m.Load(cols, _cur.Select(r => new[] { r.MaHang, r.TenHang, D.DeriveDacTinh(r.TenHang, r.SoLo), r.SoLo, r.Size, r.QuyCach, r.NgayNhapKho, r.PhieuNhap, N(r.SoKien), Kg(r.TrongLuong) }));
            _g.Refresh();
        }
    }
    private readonly RowsPanel _dl;

    // =====================================================================================
    // Load Data
    // =====================================================================================
    private readonly TextBox _ldDate = new() { Watermark = "dd/mm/yyyy", Width = 110 };
    private readonly TextBlock _ldStats = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };

    private Control BuildLoadBar()
    {
        var bar = Bar();
        if (_edit) bar.Children.Add(Btn("📂 Nạp file Excel…", async () => await PickAndImport(), primary: true, tip: "Chọn file → kiểm tra → xem trước → nạp. Dữ liệu cũ được chụp lại để hoàn tác. File báo cáo NXT của kho: dòng tiêu đề có “Mã hàng”, “Tên hàng”, “Số lô”, “Size”, “Quy cách”; lấy nhóm Số kiện / Trọng lượng TỒN CUỐI KỲ (hoặc cặp cuối cùng)."));
        bar.Children.Add(Btn("⟲ Lịch sử nạp", async () => await ShowHistory(), tip: "Các đợt nạp (ai · máy nào · file nào); hoàn tác đợt mới nhất."));
        bar.Children.Add(Label("Ngày số liệu")); bar.Children.Add(_ldDate);
        if (_edit) bar.Children.Add(Btn("Lưu ngày", async () => await Act(() => Svc.SetDataDate(_ldDate.Text ?? ""), "Đã lưu ngày số liệu"))); else _ldDate.IsReadOnly = true;
        _ldDate.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter && _edit) await Act(() => Svc.SetDataDate(_ldDate.Text ?? ""), "Đã lưu ngày số liệu"); };
        return bar;
    }

    private void RenderLoad()
    {
        _ldDate.Text = D.DataDate;
        if (!D.HasData) { _ldStats.Text = "Chưa có dữ liệu."; return; }
        var items = D.Rows.Select(r => JsText.JsTrim(r.MaHang)).Where(x => x.Length > 0).Distinct().Count();
        _ldStats.Text = $"Dữ liệu đang lưu: {N(D.Rows.Count)} dòng · {N(items)} mã hàng · Số kiện {N(D.Rows.Sum(r => r.SoKien))} · Tổng trọng lượng {Kg(D.Rows.Sum(r => r.TrongLuong))} kg · nạp lúc {Nap(D.ImportedAt)}";
    }

    private async Task PickAndImport()
    {
        var path = await Ui.OpenFile(this, "Chọn file tồn kho gửi", "xlsx;xls;csv", "Excel");
        if (path is null) return;
        GuiService.NapPreview? pv = null;
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

    private async Task<bool> ReviewDialog(GuiService.NapPreview pv)
    {
        var C = pv.Check; var ok = false;
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Label($"{pv.FileName} · {(pv.Size / 1048576.0 < 0.1 ? Math.Max(1, Math.Round(pv.Size / 1024.0)) + " KB" : Math.Round(pv.Size / 104857.6) / 10 + " MB")} · {pv.Book.SheetNames.Count} sheet (đang xem “{pv.Sheet}”)"
            + (pv.DataDate.Length > 0 ? $" · ngày số liệu {pv.DataDate}" : " · chưa có ngày số liệu"), bold: true));
        var pills = new WrapPanel { ItemSpacing = 8 };
        Border Pill(string n, string l, IBrush bg) => new() { Background = bg, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4), Child = new StackPanel { Children = { Label(n, true, 16), Muted(l) } } };
        pills.Children.Add(Pill(N(C.Ok), "Dòng hợp lệ", OkBg)); pills.Children.Add(Pill(N(C.Warn), "Dòng cần kiểm tra", LowBg)); pills.Children.Add(Pill(N(C.Err), "Dòng lỗi", OutBg));
        body.Children.Add(pills);
        if (C.Infos.Count > 0) body.Children.Add(Muted(string.Join("\n", C.Infos)));
        if (C.Errors.Count > 0) body.Children.Add(new Border { Background = OutBg, Padding = new Thickness(8), Child = Label("Lỗi nặng — không nạp được:\n• " + string.Join("\n• ", C.Errors)) });
        if (pv.Warns.Count > 0) body.Children.Add(new Border { Background = LowBg, Padding = new Thickness(8), Child = Label($"Cần lưu ý ({pv.Warns.Count}):\n• " + string.Join("\n• ", pv.Warns.Take(8))) });
        if (pv.CmpLines.Count > 0)
        {
            var g = new GridModel { ReadOnly = true };
            g.Load(new[] { new GridColumn("a", "So với dữ liệu đang có", 170), new GridColumn("b", "Đang có", 110, "number"), new GridColumn("c", "File mới", 110, "number"), new GridColumn("d", "Chênh", 70) },
                pv.CmpLines.Select(l => new[] { l.Label, JsText.En(l.Cur, 0, 0), JsText.En(l.Next, 0, 0), l.Pct is double p ? (p > 0 ? "+" : "") + JsText.JsStr(p) + "%" : "" }));
            body.Children.Add(Framed(new SheetGrid(g) { SpareRows = 0, Height = 110 }));
        }
        if (C.Problems.Count > 0)
        {
            var g = new GridModel { ReadOnly = true };
            g.Load(new[] { new GridColumn("r", "Dòng", 60, "number"), new GridColumn("l", "Mức", 80), new GridColumn("m", "Nội dung", 420) }, C.Problems.Take(200).Select(p => new[] { p.Row.ToString(CultureInfo.InvariantCulture), p.Level == "err" ? "Lỗi" : "Kiểm tra", p.Msg }));
            body.Children.Add(Label($"Các dòng cần xem ({N(C.Problems.Count)})" + (C.Problems.Count > 200 ? " — hiện 200 dòng đầu" : ""), bold: true));
            body.Children.Add(Framed(new SheetGrid(g) { SpareRows = 0, Height = 180 }));
        }
        var ack = new CheckBox { Content = $"Tôi đã xem {N(C.Err)} dòng lỗi ở trên và vẫn muốn nạp.", IsVisible = C.Err > 0 && !pv.Blocking };
        body.Children.Add(ack);
        body.Children.Add(Muted($"Dữ liệu cũ được chụp lại trước khi nạp — hoàn tác được trong {Svc.KeepDays} ngày (nút “Lịch sử nạp”), trên máy này."));
        var cancel = new Button { Content = "Hủy — giữ dữ liệu cũ", IsCancel = true };
        var go = new Button { Content = pv.Blocking ? "Không nạp được" : C.Err > 0 || pv.Warns.Count > 0 ? "Vẫn nạp" : "Nạp dữ liệu", Classes = { "primary" }, IsEnabled = !pv.Blocking && C.Err == 0 };
        ack.IsCheckedChanged += (_, _) => go.IsEnabled = !pv.Blocking && ack.IsChecked == true;
        body.Children.Add(Ui.Buttons(cancel, go));
        var w = Ui.Dialog("Xem trước khi nạp · Tồn kho gửi", new ScrollViewer { Content = body, MaxHeight = 640 }, 720);
        cancel.Click += (_, _) => w.Close(); go.Click += (_, _) => { ok = true; w.Close(); };
        await Ui.Show(this, w);
        return ok;
    }

    private async Task ShowHistory()
    {
        List<JsonObject> log;
        try { log = await Task.Run(Svc.History); } catch (Exception e) { await Ui.Message(this, Ui.Describe(e)); return; }
        var g = new GridModel { ReadOnly = true };
        static string S(JsonNode? n) => NapExcel.S(n);
        string St(JsonObject x) => S(x["status"]) switch { "applied" => "Đang dùng", "undone" => "Đã hoàn tác", _ => "Đã bị thay" };
        g.Load(new[]
        {
            new GridColumn("t", "Lúc nạp", 120), new GridColumn("f", "File", 220), new GridColumn("s", "Sheet", 70), new GridColumn("d", "Ngày SL", 85), new GridColumn("r", "Dòng", 65, "number"),
            new GridColumn("k", "Tổng (kg)", 100, "number"), new GridColumn("u", "Người · máy", 170), new GridColumn("st", "Trạng thái", 95),
        }, log.Take(60).Select(x => new[] { NapExcel.When(S(x["time"])), S(x["fileName"]), S(x["sheet"]), S(x["dataDate"]), N(GuiStore.Num(x["rows"]), 0, 0), N(GuiStore.Num(x["kg"]), 0, 0), S(x["user"]) + (S(x["host"]).Length > 0 ? " · " + S(x["host"]) : ""), St(x) }));
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(log.Count == 0 ? Muted("Chưa có đợt nạp nào được ghi.") : Framed(new SheetGrid(g) { SpareRows = 0, Height = 340 }));
        var la = NapExcel.LastApplied(log);
        body.Children.Add(Muted(la is null ? "" : Svc.CanUndo(la, log) ? $"Đợt nạp đang dùng: {S(la["fileName"])} lúc {NapExcel.When(S(la["time"]))} — hoàn tác được trên máy này." : $"Đợt nạp đang dùng: {S(la["fileName"])} — bản hoàn tác nằm ở máy khác hoặc đã quá hạn."));
        body.Children.Add(Muted("Chỉ hoàn tác được đợt nạp mới nhất đang dùng, trên đúng máy đã nạp. Hoàn tác đưa dữ liệu về như TRƯỚC khi nạp file đó."));
        var close = new Button { Content = "Đóng", IsCancel = true };
        var undo = new Button { Content = "Hoàn tác đợt đang dùng", Classes = { "danger" }, IsEnabled = la is not null && Svc.CanUndo(la, log) };
        body.Children.Add(Ui.Buttons(undo, close));
        var w = Ui.Dialog("Lịch sử nạp Excel · Tồn kho gửi", body, 1000);
        close.Click += (_, _) => w.Close();
        undo.Click += async (_, _) =>
        {
            if (la is null) return;
            if (!await Ui.Confirm(w, $"HOÀN TÁC đợt nạp lúc {NapExcel.When(S(la["time"]))}?\n\nFile: {S(la["fileName"])}\nDữ liệu sẽ trở về như TRƯỚC khi nạp file này.", "Hoàn tác", "Hoàn tác")) return;
            w.Close(); await Act(() => Svc.UndoNap(S(la["id"])), "Đã hoàn tác đợt nạp " + S(la["fileName"]));
        };
        await Ui.Show(this, w);
    }

    // =====================================================================================
    // Phiếu Xuất
    // =====================================================================================
    private readonly ComboBox _pxGrp = new() { MinWidth = 190 };
    private readonly TextBox _pxQ = new() { Watermark = "Tìm mã, tên, lô, size…", Width = 240 }, _pxMa = new() { Watermark = "Mã phiếu (để trống = tự cấp PX0001…)", Width = 260 };
    private readonly TextBlock _pxInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly GridModel _pxm = new(), _plm = new() { ReadOnly = true };
    private SheetGrid _pxg = null!, _plg = null!;
    private List<GuiRow> _pxRows = new(); private readonly Dictionary<GuiRow, double> _pxSel = new(); private List<string[]> _pxSnap = new(); private int _pxQtyCol;
    private List<GuiStore.Slip> _plRows = new();
    private TabControl _pxTabs = null!;

    private Control BuildPx()
    {
        _pxg = new SheetGrid(_pxm) { SpareRows = 0, CellBackground = (r, c) => r < _pxRows.Count && _pxSel.ContainsKey(_pxRows[r]) ? PickBg : null };
        _plg = new SheetGrid(_plm) { SpareRows = 0, CellBackground = (r, c) => r < _plRows.Count && !Svc.IsActive(_plRows[r]) ? OutBg : null };
        _pxGrp.ItemsSource = new[] { "Tất cả" }.Concat(GuiData.Groups.Select(g => g.Label)).ToList(); _pxGrp.SelectedIndex = 0;
        _pxGrp.SelectionChanged += (_, _) => { if (!_loading) RenderPx(); };
        _pxQ.TextChanged += (_, _) => { if (!_loading) RenderPx(); };
        _pxm.Changed += () => { if (!_loading) OnPxEdited(); };
        var bar = Bar(); bar.Children.Add(Label("Nhóm:")); bar.Children.Add(_pxGrp); bar.Children.Add(_pxQ);
        if (_edit)
        {
            bar.Children.Add(_pxMa);
            bar.Children.Add(Btn("Tạo phiếu xuất", async () => await CreateSlip(), primary: true));
            bar.Children.Add(Btn("Bỏ chọn", () => { _pxSel.Clear(); RenderPx(); }));
        }
        var create = Page(new StackPanel { Children = { bar, _pxInfo } }, Framed(_pxg));
        var lbar = Bar();
        lbar.Children.Add(Btn("👁 Xem / Sửa phiếu", async () => await OpenSlip(), primary: true));
        if (_export) lbar.Children.Add(Btn("⬇ Xuất Excel phiếu", async () => await ExportSlip()));
        if (_edit) lbar.Children.Add(Btn("🗑 Xóa phiếu", async () => await DeleteSlip(), danger: true));
        var list = Page(new StackPanel { Children = { lbar, Muted("Phiếu tô đỏ = thuộc đợt số liệu cũ (đã khóa: chỉ xem / xuất Excel / xóa, không hoàn tồn).") }, Margin = new Thickness(0) }, Framed(_plg));
        _pxTabs = new TabControl { Items = { new TabItem { Header = "Tạo phiếu xuất", Content = create }, new TabItem { Header = "Danh sách phiếu xuất", Content = list } } };
        _pxTabs.SelectionChanged += (_, _) => { if (!_loading) RenderPx(); };
        return _pxTabs;
    }

    private List<GuiRow> PxSource()
    {
        IEnumerable<GuiRow> src = D.Rows.Where(GuiService.InStock);
        var gi = _pxGrp.SelectedIndex;
        if (gi > 0) { var set = D.GroupRows(GuiData.Groups[gi - 1].Id).ToHashSet(); src = src.Where(set.Contains); }
        var q = JsText.Fuzzy(_pxQ.Text);
        if (q.Length > 0) src = src.Where(r => _pxSel.ContainsKey(r) || JsText.Fuzzy(string.Join(" ", r.MaHang, r.TenHang, r.SoLo, r.Size, r.PhieuNhap)).Contains(q, StringComparison.Ordinal));
        return src.ToList();
    }

    private void RenderPx()
    {
        if (_pxTabs.SelectedIndex == 1) { RenderSlipList(); return; }
        if (!D.HasData) { _pxRows = new(); _pxInfo.Text = "Chưa có dữ liệu."; _pxm.Load(new[] { new GridColumn("x", "", 200) }, Array.Empty<string[]>()); _pxg.Refresh(); return; }
        _pxRows = PxSource();
        _pxQtyCol = 8;
        var cols = new List<GridColumn>
        {
            new("maHang", "Mã hàng", 190) { ReadOnly = true }, new("tenHang", "Tên hàng", 280) { ReadOnly = true }, new("soLo", "Số lô", 140) { ReadOnly = true }, new("size", "Size", 90) { ReadOnly = true },
            new("phieuNhap", "Phiếu nhập", 130) { ReadOnly = true }, new("dacTinh", "Đặc tính", 100) { ReadOnly = true }, new("sk", "Số kiện", 80, "number") { ReadOnly = true }, new("tl", "Trọng lượng", 100, "number") { ReadOnly = true },
            new("x", _edit ? "SL xuất ✎" : "SL xuất", 90, "number") { ReadOnly = !_edit },
        };
        _pxm.ReadOnly = !_edit;
        _pxm.Load(cols, _pxRows.Select(r => new[] { r.MaHang, r.TenHang, r.SoLo, r.Size, r.PhieuNhap, D.DeriveDacTinh(r.TenHang, r.SoLo), N(r.SoKien), Kg(r.TrongLuong), _pxSel.TryGetValue(r, out var v) ? Raw(v) : "" }));
        _pxg.CheckCol = _pxQtyCol;   // 1.50: tích ô đầu dòng = xuất hết (số kiện; dòng chỉ có kg thì số kg)
        _pxg.CheckFill = r => r < _pxRows.Count ? (_pxRows[r].SoKien > 0 ? Raw(_pxRows[r].SoKien) : Raw(_pxRows[r].TrongLuong)) : "";
        _pxSnap = _pxm.Snapshot(); _pxg.Refresh();
        _pxInfo.Text = $"{N(_pxRows.Count)} dòng còn tồn · đang chọn {N(_pxSel.Count)} dòng, tổng SL {N(_pxSel.Sum(kv => kv.Value))}. Tích ô đầu dòng = lấy hết (hoặc gõ SL). Dòng chỉ có kg (0 kiện) thì SL xuất là SỐ KG. Phiếu trừ tồn NGAY khi tạo.";
    }

    private void OnPxEdited()
    {
        var now = _pxm.Snapshot();
        for (var r = 0; r < Math.Min(now.Count, _pxRows.Count); r++)
        {
            var before = r < _pxSnap.Count ? _pxSnap[r][_pxQtyCol] : ""; var after = (now[r][_pxQtyCol] ?? "").Trim();
            if (before == after) continue;
            if (after.Length == 0 || !Ui.TryParseNumber(after, out var v) || v <= 0) _pxSel.Remove(_pxRows[r]); else _pxSel[_pxRows[r]] = v;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { _loading = true; try { RenderPx(); } finally { _loading = false; } });
    }

    private async Task CreateSlip()
    {
        if (_pxSel.Count == 0) { await Ui.Message(this, "Tích chọn dòng (ô đầu dòng) hoặc gõ số lượng vào cột “SL xuất ✎” trước."); return; }
        if (!await Ui.Confirm(this, $"Tạo phiếu xuất mới với {_pxSel.Count} dòng hàng đã chọn?\n\nSố kiện / Trọng lượng sẽ được TRỪ ngay khỏi dữ liệu Load Data.", "Xác nhận tạo phiếu xuất", "Tạo phiếu")) return;
        var picks = _pxSel.Select(kv => (D.Rows.IndexOf(kv.Key), kv.Value)).Where(p => p.Item1 >= 0).ToList();
        GuiStore.Slip? s = null;
        if (await Act(() => s = Svc.CreateSlip(picks, _pxMa.Text), null) && s is not null)
        {
            _pxSel.Clear(); _pxMa.Text = ""; _status.Text = $"Đã tạo phiếu {s.MaPhieu} — đã trừ tồn.";
            _pxTabs.SelectedIndex = 1; RenderAll();
        }
    }

    private void RenderSlipList()
    {
        _plRows = Svc.Slips.ToList();
        _plm.Load(new[]
        {
            new GridColumn("ma", "Mã phiếu", 130), new GridColumn("ngay", "Ngày tạo", 140), new GridColumn("n", "Số dòng", 70, "number"), new GridColumn("sl", "Tổng SL", 90, "number"),
            new GridColumn("tt", "Trạng thái", 120), new GridColumn("nd", "Ngày số liệu", 110), new GridColumn("u", "Người tạo", 160),
        }, _plRows.Select(p => new[]
        {
            p.MaPhieu, DateTime.TryParse(p.NgayTao, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : p.NgayTao,
            N(p.Items.Count), N(p.Items.Sum(i => i.SoLuongXuat)), Svc.IsActive(p) ? "Đang trừ tồn" : "Đã khóa", p.LoadDate, p.NguoiTao + (p.Nguon.Length > 0 ? " · " + p.Nguon : ""),
        }));
        _plg.Refresh();
    }

    private GuiStore.Slip? CurSlip() => _plg.Row >= 0 && _plg.Row < _plRows.Count ? _plRows[_plg.Row] : null;
    private async Task DeleteSlip()
    {
        if (CurSlip() is not { } p) { await Ui.Message(this, "Chọn một phiếu trước."); return; }
        var act = Svc.IsActive(p);
        if (!await Ui.Confirm(this, act ? $"Xóa phiếu xuất “{p.MaPhieu}”? Số kiện / Trọng lượng đã trừ sẽ được HOÀN LẠI vào dữ liệu Load Data. Không thể hoàn tác." : $"Xóa phiếu xuất “{p.MaPhieu}” khỏi danh sách?\nPhiếu này đã bị khóa nên KHÔNG hoàn lại tồn.", "Xóa phiếu xuất", "Xóa phiếu")) return;
        await Act(() => Svc.DeleteSlip(p.Id), "Đã xóa phiếu " + p.MaPhieu);
    }
    private async Task ExportSlip()
    {
        if (CurSlip() is not { } p) { await Ui.Message(this, "Chọn một phiếu trước."); return; }
        var path = await Ui.SaveFile(this, "Xuất phiếu yêu cầu xuất hàng", GuiExport.SlipFileName(p), "xlsx", "Excel"); if (path is null) return;
        if (await Ui.Run(this, () => File.WriteAllBytes(path, GuiExport.SlipWorkbook(p)))) Ui.OpenExternal(path);
    }

    private async Task OpenSlip()
    {
        if (CurSlip() is not { } p) { await Ui.Message(this, "Chọn một phiếu trước."); return; }
        var active = Svc.IsActive(p); var canEdit = _edit && active;
        var m = new GridModel { ReadOnly = !canEdit };
        m.Load(new[]
        {
            new GridColumn("a", "Mã hàng", 180) { ReadOnly = true }, new GridColumn("b", "Tên hàng", 260) { ReadOnly = true }, new GridColumn("c", "Số lô", 120) { ReadOnly = true }, new GridColumn("d", "Size", 80) { ReadOnly = true },
            new GridColumn("e", "Phiếu nhập", 110) { ReadOnly = true }, new GridColumn("f", "Đơn vị", 60) { ReadOnly = true }, new GridColumn("q", canEdit ? "SL xuất ✎" : "SL xuất", 90, "number") { ReadOnly = !canEdit },
        }, p.Items.Select(i => new[] { i.MaHang, i.TenHang, i.SoLo, i.Size, i.PhieuNhap, i.XuatKg is not null ? "kg" : "kiện", Raw(i.XuatKg ?? i.SoLuongXuat) }));
        var maBox = new TextBox { Text = p.MaPhieu, Width = 240, IsReadOnly = !canEdit };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Label($"Phiếu {p.MaPhieu} · {(active ? "đang trừ tồn" : "ĐÃ KHÓA (số liệu cũ" + (p.LoadDate.Length > 0 ? " ngày " + p.LoadDate : "") + ")")} · {p.Items.Count} dòng", bold: true));
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Label("Mã phiếu:"), maBox } });
        body.Children.Add(Framed(new SheetGrid(m) { SpareRows = 0, Height = 320 }));
        if (canEdit) body.Children.Add(Muted("Sửa số lượng rồi bấm Lưu: hệ thống hoàn lại số cũ rồi trừ theo số mới; không được trống, ≤ 0 hoặc vượt tồn."));
        var close = new Button { Content = "Đóng", IsCancel = true }; var save = new Button { Content = "Lưu thay đổi", Classes = { "primary" }, IsVisible = canEdit };
        body.Children.Add(Ui.Buttons(save, close));
        var w = Ui.Dialog("Phiếu xuất · " + p.MaPhieu, body, 1000);
        close.Click += (_, _) => w.Close();
        save.Click += async (_, _) =>
        {
            var qty = new List<double>();
            for (var r = 0; r < p.Items.Count; r++) qty.Add(Ui.TryParseNumber(m.Get(r, 6), out var v) ? v : double.NaN);
            if (qty.Any(double.IsNaN)) { await Ui.Message(w, "Có dòng SL xuất không phải số."); return; }
            try { Svc.UpdateSlip(p.Id, maBox.Text ?? "", qty); } catch (BusinessException e) { await Ui.Message(w, e.Message, "Không lưu được"); return; }
            w.Close(); _status.Text = "Đã lưu phiếu " + p.MaPhieu; RenderAll();
        };
        await Ui.Show(this, w);
    }

    // =====================================================================================
    // Phân Loại
    // =====================================================================================
    private readonly List<RowsPanel> _pl = new(); private TabControl _plTabs = null!;
    private readonly ComboBox _dkMod = new() { MinWidth = 220 }, _cfCol = new() { MinWidth = 160 }, _cfOp = new() { MinWidth = 190 };
    private readonly TextBox _cfVal = new() { Watermark = "Giá trị (nhiều giá trị ngăn bằng ;)", Width = 260 }, _cfFrom = new() { Text = "2", Width = 50 }, _cfTo = new() { Text = "4", Width = 50 };
    private readonly GridModel _dkm = new() { ReadOnly = true }; private SheetGrid _dkg = null!;
    private readonly StackPanel _dkCustom = new() { Spacing = 4 }; private readonly TextBlock _dkInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };

    private Control BuildPl()
    {
        _plTabs = new TabControl();
        foreach (var g in GuiData.Groups)
        {
            var id = g.Id; var panel = new RowsPanel(this, () => D.GroupRows(id), GuiExport.GroupSheetName(id), GuiExport.GroupFileName(id), false); _pl.Add(panel);
            _plTabs.Items.Add(new TabItem { Header = g.Label.Replace("Tôm ", ""), Content = panel.Root });
        }
        _plTabs.Items.Add(new TabItem { Header = "Điều kiện lọc", Content = BuildDk() });
        _plTabs.SelectionChanged += (_, _) => { if (!_loading) RenderPl(); };
        return _plTabs;
    }
    private void RenderPl() { var i = _plTabs.SelectedIndex; if (i >= 0 && i < _pl.Count) _pl[i].Render(); else if (i == _pl.Count) RenderDk(); }

    private Control BuildDk()
    {
        _dkg = new SheetGrid(_dkm) { SpareRows = 0, Height = 230 };
        _dkMod.ItemsSource = GuiData.Groups.Select(g => g.Label).ToList(); _dkMod.SelectedIndex = 0;
        _dkMod.SelectionChanged += (_, _) => { if (!_loading) RenderDk(); };
        _cfCol.ItemsSource = GuiData.TargetCols.Select(c => c.Label).ToList(); _cfCol.SelectedIndex = 3;
        _cfOp.ItemsSource = GuiData.CustomOps.Select(o => o.Label).ToList(); _cfOp.SelectedIndex = 0;
        var bar = Bar(); bar.Children.Add(Label("Nhóm:")); bar.Children.Add(_dkMod);
        var add = Bar(); add.Children.Add(Label("＋ Thêm điều kiện: cột")); add.Children.Add(_cfCol); add.Children.Add(_cfOp); add.Children.Add(_cfVal);
        add.Children.Add(Label("từ ký tự")); add.Children.Add(_cfFrom); add.Children.Add(Label("đến")); add.Children.Add(_cfTo);
        if (_edit) add.Children.Add(Btn("＋ Thêm", async () => await AddCf(), primary: true));
        var s = new StackPanel { Spacing = 6 }; s.Children.Add(bar); s.Children.Add(_dkInfo); s.Children.Add(Framed(_dkg));
        s.Children.Add(Label("Điều kiện tự thêm của nhóm này (áp dụng cộng dồn với điều kiện gốc)", true)); s.Children.Add(_dkCustom); s.Children.Add(add);
        s.Children.Add(Muted("Điều kiện gốc (Chính / Phụ) cố định theo Số lô / Size; điều kiện tự thêm lưu dùng chung, áp cho tab Phân Loại, Phiếu Xuất, Báo Cáo, Đối Soát."));
        return new ScrollViewer { Content = s };
    }
    private string DkId => GuiData.Groups[Math.Max(0, _dkMod.SelectedIndex)].Id;
    private void RenderDk()
    {
        var f = D.FunnelOf(DkId);
        _dkInfo.Text = $"Từ {N(f.Total)} dòng đã nạp → còn {N(f.Final)} dòng sau các bước lọc.";
        _dkm.Load(new[] { new GridColumn("k", "Loại", 80), new GridColumn("l", "Bước lọc", 420), new GridColumn("c", "Cột", 90), new GridColumn("r", "Còn lại", 90, "number"), new GridColumn("x", "Bị loại", 90, "number") },
            f.Steps.Select(s => new[] { s.Kind switch { "main" => "Chính", "extra" => "Phụ", _ => "Tự thêm" }, s.Label + (s.Warn ? "  ⚠ không thấy cột — bỏ qua" : ""), s.Col, N(s.Remaining), N(s.Removed) }));
        _dkg.Refresh();
        _dkCustom.Children.Clear();
        var list = D.CustomFor(DkId);
        if (list.Count == 0) _dkCustom.Children.Add(Muted("Chưa có điều kiện tự thêm."));
        for (var i = 0; i < list.Count; i++)
        {
            var idx = i; var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(Label("• " + GuiData.CustomDesc(list[i])));
            if (_edit) row.Children.Add(Btn("Xóa", async () => await Act(() => Svc.RemoveCustomFilter(DkId, idx), "Đã xóa điều kiện"), danger: true));
            _dkCustom.Children.Add(row);
        }
    }
    private async Task AddCf()
    {
        var op = GuiData.CustomOps[Math.Max(0, _cfOp.SelectedIndex)].Op; var col = _cfCol.SelectedItem as string ?? ""; var val = (_cfVal.Text ?? "").Trim();
        if (col.Length == 0) { await Ui.Message(this, "Chọn cột cần lọc"); return; }
        if (op is not ("empty" or "notempty") && val.Length == 0) { await Ui.Message(this, "Nhập giá trị cần so khớp"); return; }
        if (op is "gt" or "lt" or "gte" or "lte" && double.IsNaN(JsText.LooseNum(val))) { await Ui.Message(this, "Giá trị so sánh phải là số"); return; }
        var cf = new CustomFilter { Id = GuiService.NewCfId(), ColLabel = col, Op = op, Value = val };
        if (op is "pos_in" or "pos_notin")
        {
            if (!int.TryParse(_cfFrom.Text, out var f) || !int.TryParse(_cfTo.Text, out var t) || f < 1 || t < f) { await Ui.Message(this, "Vị trí ký tự không hợp lệ (từ ≥ 1, đến ≥ từ)"); return; }
            cf.From = f; cf.To = t;
        }
        if (await Act(() => Svc.AddCustomFilter(DkId, cf), "Đã thêm điều kiện")) _cfVal.Text = "";
    }

    // =====================================================================================
    // Đặc Tính
    // =====================================================================================
    private readonly TextBox _dtPaste = new() { AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap, Watermark = "Dán 2 cột từ Excel: Diễn giải đặc tính ⇥ Đặc tính (VD “Tôm EU Nhật” ⇥ “83.EU-H-M”)" };
    private readonly GridModel _dtm = new() { ReadOnly = true }, _dgm = new() { ReadOnly = true }; private SheetGrid _dtg = null!, _dgg = null!;
    private readonly TextBox _dtQ = new() { Watermark = "Tìm đặc tính…", Width = 220 }; private readonly ComboBox _dtFilter = new() { MinWidth = 140 }, _dtGroup = new() { MinWidth = 200 };
    private readonly TextBlock _dtInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private List<string> _dtVals = new(); private TabControl _dtTabs = null!;
    private readonly GridModel _dmm = new() { ReadOnly = true }; private SheetGrid _dmg = null!;

    private Control BuildDt()
    {
        // --- nhập tay
        _dmg = new SheetGrid(_dmm) { SpareRows = 0 };
        var mbar = Bar();
        if (_edit)
        {
            mbar.Children.Add(Btn("Đọc dữ liệu dán", async () =>
            {
                var rows = GuiData.ParsePasted(_dtPaste.Text ?? ""); if (rows.Count == 0) { await Ui.Message(this, "Không có dòng nào đọc được (cần cột Đặc tính)."); return; }
                var n = 0; if (await Act(() => n = Svc.AddManual(rows), null)) { _dtPaste.Text = ""; _status.Text = $"Đã thêm {n} dòng đặc tính nhập tay ({rows.Count - n} dòng trùng bị bỏ)."; }
            }, primary: true));
            mbar.Children.Add(Btn("📂 Nạp từ file Excel…", async () => await LoadManualFile()));
            mbar.Children.Add(Btn("Xóa danh sách nhập tay", async () =>
            {
                if (!await Ui.Confirm(this, "Xóa toàn bộ danh sách đặc tính nhập tay?", "Xóa", "Xóa")) return;
                await Act(() => Svc.SaveManual(Array.Empty<(string, string)>()), "Đã xóa danh sách nhập tay");
            }, danger: true));
        }
        if (_export) mbar.Children.Add(Btn("⬇ Xuất Excel", async () => await SaveXlsx(GuiExport.DacTinhManual(D), "dac-tinh-nhap-tay.xlsx")));
        var manual = Page(new StackPanel { Children = { mbar, _dtPaste, Muted("Bảng này dùng để đổi mã đặc tính trong Tên hàng: “0083” → “83.EU-H-M” (khớp phần trước dấu chấm, bỏ số 0 đầu). Không có trong bảng thì giữ nguyên.") } }, Framed(_dmg));
        // --- gộp
        _dtg = new SheetGrid(_dtm) { SpareRows = 0, CellBackground = (r, c) => c == 2 && r < _dtVals.Count && D.DtGroupMap.ContainsKey(_dtVals[r]) ? OkBg : null };
        _dgg = new SheetGrid(_dgm) { SpareRows = 0 };
        _dtFilter.ItemsSource = new[] { "Tất cả", "Chưa gộp", "Đã gộp" }; _dtFilter.SelectedIndex = 0; _dtGroup.ItemsSource = GuiData.DtGroups.ToList(); _dtGroup.SelectedIndex = 0;
        _dtFilter.SelectionChanged += (_, _) => { if (!_loading) RenderDt(); }; _dtQ.TextChanged += (_, _) => { if (!_loading) RenderDt(); };
        var gbar = Bar(); gbar.Children.Add(_dtQ); gbar.Children.Add(_dtFilter);
        if (_edit)
        {
            gbar.Children.Add(Label("Gộp vào nhóm:")); gbar.Children.Add(_dtGroup);
            gbar.Children.Add(Btn("Gộp dòng chọn", async () =>
            {
                var s = _dtg.Selection; var vs = Enumerable.Range(s.R1, s.Rows).Where(r => r >= 0 && r < _dtVals.Count).Select(r => _dtVals[r]).ToList();
                if (vs.Count == 0) { await Ui.Message(this, "Bôi đen các dòng đặc tính cần gộp."); return; }
                await Act(() => Svc.MergeGroup(vs, GuiData.DtGroups[Math.Max(0, _dtGroup.SelectedIndex)]), "Đã gộp đặc tính");
            }, primary: true, tip: "Bôi đen các dòng chưa gộp rồi chọn nhóm"));
            gbar.Children.Add(Btn("Bỏ gộp dòng chọn", async () =>
            {
                var s = _dtg.Selection; var vs = Enumerable.Range(s.R1, s.Rows).Where(r => r >= 0 && r < _dtVals.Count).Select(r => _dtVals[r]).ToList();
                await Act(() => { foreach (var v in vs) Svc.Unmerge(v); }, "Đã bỏ gộp");
            }));
            gbar.Children.Add(Btn("⇄ Chuyển Số lô (CKQ) → Đặc tính…", async () => await MapLotDialog(), tip: "Đặc tính CKQ lấy Số lô làm đặc tính — chuyển từng Số lô sang đặc tính thật"));
            gbar.Children.Add(Btn("Xóa toàn bộ gộp", async () =>
            {
                if (!await Ui.Confirm(this, "Xóa toàn bộ dữ liệu gộp đặc tính đã lưu? Không thể hoàn tác.", "Xóa toàn bộ", "Xóa toàn bộ")) return;
                await Act(Svc.ClearGroups, "Đã xóa toàn bộ gộp");
            }, danger: true));
        }
        if (_export) { gbar.Children.Add(Btn("⬇ Xuất đặc tính", async () => await SaveXlsx(GuiExport.DacTinhList(D), "dac-tinh.xlsx"))); gbar.Children.Add(Btn("⬇ Xuất gộp", async () => await SaveXlsx(GuiExport.DacTinhGroups(D), "gop-dac-tinh.xlsx"))); }
        var left = Page(new StackPanel { Children = { gbar, _dtInfo } }, Framed(_dtg));
        // 1.57: bảng danh sách nhóm (trước nằm bên phải) thành tab riêng — màn hình nhỏ bảng gộp rộng hết trang
        _dtTabs = new TabControl { Items = { new TabItem { Header = "Gộp đặc tính", Content = left }, new TabItem { Header = "Nhóm đặc tính", Content = new Border { Padding = new Thickness(0, 8, 0, 0), Child = Framed(_dgg) } }, new TabItem { Header = "Dữ liệu đặc tính nhập tay", Content = manual } } };
        _dtTabs.SelectionChanged += (_, _) => { if (!_loading) RenderDt(); };
        return _dtTabs;
    }

    private void RenderDt()
    {
        if (_dtTabs.SelectedIndex == 2)
        {
            _dmm.Load(new[] { new GridColumn("a", "Diễn giải đặc tính", 360), new GridColumn("b", "Đặc tính", 200) }, D.DtManual.Select(r => new[] { r.MaHang, r.DacTinh })); _dmg.Refresh(); return;
        }
        var (all, newSeen) = D.UniqueDacTinh(); if (newSeen && _edit) Svc.SaveSeen();
        var q = JsText.Fuzzy(_dtQ.Text); var f = _dtFilter.SelectedIndex;
        _dtVals = all.Where(v => (f == 0 || (f == 1) == !D.DtGroupMap.ContainsKey(v)) && (q.Length == 0 || JsText.Fuzzy(v + " " + D.DienGiai(v)).Contains(q, StringComparison.Ordinal))).ToList();
        _dtInfo.Text = $"{N(all.Count)} đặc tính · đã gộp {N(all.Count(D.DtGroupMap.ContainsKey))} · chưa gộp {N(all.Count(v => !D.DtGroupMap.ContainsKey(v)))}. Đặc tính chưa gộp KHÔNG cộng vào Bảng Tổng Hợp.";
        _dtm.Load(new[] { new GridColumn("v", "Đặc tính", 220), new GridColumn("d", "Diễn giải đặc tính", 280), new GridColumn("g", "Nhóm đã gộp", 220) }, _dtVals.Select(v => new[] { v, D.DienGiai(v), D.DtGroupMap.GetValueOrDefault(v, "") }));
        _dtg.Refresh();
        _dgm.Load(new[] { new GridColumn("g", "Nhóm", 200), new GridColumn("n", "Đặc tính đã gộp", 300) }, GuiData.DtGroups.Select(g => new[] { g, string.Join(", ", D.DtGroupMap.Where(kv => kv.Value == g).Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal)) }));
        _dgg.Refresh();
    }

    private async Task LoadManualFile()
    {
        var path = await Ui.OpenFile(this, "Chọn file đặc tính nhập tay (2 cột: Diễn giải đặc tính, Đặc tính)", "xlsx;xls;csv", "Excel");
        if (path is null) return;
        try
        {
            var book = await Task.Run(() => SheetText.Read(path));
            var sh = book.Sheets[book.SheetNames[0]];
            var text = string.Join("\n", sh.Select(r => string.Join("\t", r)));
            var rows = GuiData.ParsePasted(text); var n = 0;
            if (rows.Count == 0) { await Ui.Message(this, "Không đọc được dòng đặc tính nào trong file."); return; }
            if (await Act(() => n = Svc.AddManual(rows), null)) _status.Text = $"Đã thêm {n} dòng từ {Path.GetFileName(path)} ({rows.Count - n} dòng trùng bị bỏ).";
        }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Không đọc được file"); }
    }

    private async Task MapLotDialog()
    {
        var mapped = D.DtLoMap.Values.ToList(); var ckq = D.CkqLotSet().Where(l => !D.DtLoMap.ContainsKey(GuiData.LoKey(l))).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var lots = new ComboBox { ItemsSource = ckq, SelectedIndex = ckq.Count > 0 ? 0 : -1, MinWidth = 320 };
        var existing = D.UniqueDacTinh().Values.Where(v => !D.CkqLotSet().Contains(v)).ToList();
        var dt = new AutoCompleteBox { ItemsSource = existing, MinWidth = 320, Watermark = "VD 23.MKS" };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Muted($"Có {ckq.Count} Số lô đang là CKQ (lấy Số lô làm đặc tính). Chọn 1 Số lô và đặc tính thật để chuyển."));
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Label("Số lô:"), lots } });
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Label("Đặc tính:"), dt } });
        if (mapped.Count > 0) body.Children.Add(Muted("Đã chuyển: " + string.Join("; ", mapped.Select(m => $"{m.Lo} → {m.DacTinh}"))));
        var close = new Button { Content = "Đóng", IsCancel = true }; var go = new Button { Content = "Chuyển", Classes = { "primary" } };
        var undo = new Button { Content = "Hoàn tác các lô đã chuyển", IsEnabled = mapped.Count > 0 };
        body.Children.Add(Ui.Buttons(undo, go, close));
        var w = Ui.Dialog("Chuyển Số lô → Đặc tính", body, 560);
        close.Click += (_, _) => w.Close();
        go.Click += async (_, _) =>
        {
            if (lots.SelectedItem is not string lo) { await Ui.Message(w, "Không còn Số lô CKQ nào."); return; }
            var target = D.ResolveTyped(dt.Text, existing); if (target.Length == 0) { await Ui.Message(w, "Nhập đặc tính đích."); return; }
            w.Close(); await Act(() => Svc.MapLot(lo, target), $"Đã chuyển {lo} → {target}");
        };
        undo.Click += async (_, _) => { w.Close(); await Act(() => { foreach (var m in mapped) Svc.UnmapLot(m.Lo); }, "Đã hoàn tác các lô đã chuyển"); };
        await Ui.Show(this, w);
    }

    // =====================================================================================
    // Báo Cáo (Bảng Tổng Hợp)
    // =====================================================================================
    private readonly GridModel _bcm = new() { ReadOnly = true }; private SheetGrid _bcg = null!;
    private readonly TextBlock _bcInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap }; private bool _bcHide = true; private GuiData.Pivot? _pivot; private List<(string Kind, int Dummy)> _bcKinds = new();

    private Control BuildBc()
    {
        _bcg = new SheetGrid(_bcm) { SpareRows = 0, CellBackground = (r, c) => r < _bcKinds.Count ? _bcKinds[r].Kind switch { "total" => TitleBg, "group" => GroupBg, _ => null } : null };
        var bar = Bar();
        bar.Children.Add(Btn("Ẩn / hiện cột trống", () => { _bcHide = !_bcHide; RenderBc(); }));
        if (_export) bar.Children.Add(Btn("⬇ Xuất Excel Bảng Tổng Hợp", async () => await ExportBc(), primary: true));
        var main = Page(new StackPanel { Children = { bar, _bcInfo } }, Framed(_bcg));
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(main); var cs = BuildCs(); Grid.SetRow(cs, 1); grid.Children.Add(cs);
        return grid;
    }

    // ---------- Bảng Tổng Hợp Tùy Chọn (lọc lại Size × Đặc Tính từ Bảng Tổng Hợp)
    private const string CsKey = "m2_custom_summary";
    private CustomSummary.State _cs = new(); private CustomSummary.Data? _csData; private CustomSummary.Result? _csRes;
    private readonly GridModel _csm = new() { ReadOnly = true }; private SheetGrid _csg = null!;
    private readonly TextBlock _csNote = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _csViews = new() { MinWidth = 170, PlaceholderText = "— Mẫu đã lưu —" };
    private Button _csRowsBtn = null!, _csColsBtn = null!, _csHideBtn = null!;
    private Expander _csExp = null!;

    private Control BuildCs()
    {
        _cs = CustomSummary.State.From(App.Services.Settings.Get(CsKey));
        _csg = new SheetGrid(_csm) { SpareRows = 0, Height = 300, CellBackground = (r, c) => _csRes is { } x && r < x.Shown.Count && x.Shown[r].Title ? TitleBg : _csRes is { } y && r == y.Shown.Count ? LowBg : null };
        _csRowsBtn = Btn("Chọn Size", async () => await CsPick(true)); _csColsBtn = Btn("Chọn Đặc Tính", async () => await CsPick(false));
        _csHideBtn = Btn("", () => { _cs.HideEmpty = !_cs.HideEmpty; CsSave(); RenderCs(); });
        _csViews.SelectionChanged += (_, _) =>
        {
            if (_loading || _csViews.SelectedItem is not string name || !_cs.Views.TryGetValue(name, out var v) || _csData is null) return;
            _cs.Sizes = v.Sizes; _cs.Cols = v.Cols; CsSave(); RenderCs();
        };
        var bar = Bar();
        bar.Children.Add(_csRowsBtn); bar.Children.Add(_csColsBtn); bar.Children.Add(_csHideBtn); bar.Children.Add(_csViews);
        bar.Children.Add(Btn("Lưu mẫu", async () => await CsSaveView()));
        bar.Children.Add(Btn("Xóa mẫu", async () =>
        {
            if (_csViews.SelectedItem is not string name) { await Ui.Message(this, "Chọn mẫu cần xóa ở ô “Mẫu đã lưu” trước."); return; }
            if (!await Ui.Confirm(this, $"Xóa mẫu “{name}”?", "Xóa mẫu", "Xóa")) return;
            _cs.Views.Remove(name); CsSave(); RenderCs();
        }));
        if (_export) bar.Children.Add(Btn("⬇ Xuất Excel", async () =>
        {
            if (_csData is null || _csRes is null || _csRes.Shown.Count == 0 || _csRes.ColIdx.Count == 0) { await Ui.Message(this, "Chưa có gì để xuất — hãy chọn Size và Đặc Tính trước."); return; }
            await SaveXlsx(CustomSummary.Export(_csData, _csRes), $"TongHopTuyChon_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }));
        bar.Children.Add(Btn("Chọn lại tất cả", () => { _cs.Sizes = null; _cs.Cols = null; CsSave(); RenderCs(); }));
        var body = new StackPanel { Spacing = 4, Children = { bar, Framed(_csg), _csNote } };
        _csExp = new Expander { Header = "Bảng Tổng Hợp Tùy Chọn · lọc Size và Đặc Tính đã gộp từ bảng có sẵn", Content = body, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0) };
        _csExp.Expanded += (_, _) => RenderCs();
        return _csExp;
    }
    private void CsSave() { try { App.Services.Settings.Set(CsKey, _cs.ToJson().ToJsonString()); } catch { } }
    private HashSet<string> CsSel(bool rows)
    {
        if (_csData is null) return new();
        var all = rows ? _csData.Rows.Select(r => r.Key).ToList() : _csData.Cols;
        var saved = rows ? _cs.Sizes : _cs.Cols;
        return (saved is null ? all : saved.Where(all.Contains)).ToHashSet();
    }
    private void CsStore(bool rows, HashSet<string> sel)
    {
        var all = rows ? _csData!.Rows.Select(r => r.Key).ToList() : _csData!.Cols;
        var v = sel.Count == all.Count ? null : all.Where(sel.Contains).ToList();
        if (rows) _cs.Sizes = v; else _cs.Cols = v;
        CsSave();
    }
    private void RenderCs()
    {
        if (_csExp is null || !_csExp.IsExpanded) return;
        _csData = _pivot is null ? null : CustomSummary.FromPivot(_pivot);
        _loading = true; try { _csViews.ItemsSource = _cs.Views.Keys.ToList(); } finally { _loading = false; }
        _csHideBtn.Content = _cs.HideEmpty ? "Đang ẩn cột trống" : "Hiện cả cột trống";
        if (_csData is null) { _csRes = null; _csm.Load(new[] { new GridColumn("x", "Chưa có dữ liệu", 300) }, Array.Empty<string[]>()); _csg.Refresh(); _csNote.Text = ""; return; }
        var sr = CsSel(true); var sc = CsSel(false);
        _csRowsBtn.Content = $"Chọn Size ({sr.Count}/{_csData.Rows.Count}) ▾"; _csColsBtn.Content = $"Chọn Đặc Tính ({sc.Count}/{_csData.Cols.Count}) ▾";
        var r = _csRes = CustomSummary.Compute(_csData, sr, sc, _cs.HideEmpty);
        if (r.Shown.Count == 0 || r.ColIdx.Count == 0)
        {
            _csm.Load(new[] { new GridColumn("x", r.Shown.Count == 0 ? "Chưa chọn Size nào — bấm “Chọn Size”." : "Chưa chọn Đặc Tính nào — bấm “Chọn Đặc Tính”.", 420) }, Array.Empty<string[]>()); _csg.Refresh(); _csNote.Text = ""; return;
        }
        string C(double? v) => v is double d ? Kg(d) : "-";
        var cols = new List<GridColumn> { new("s", "Size", 160) }; cols.AddRange(r.ColIdx.Select(i => new GridColumn("c" + i, _csData.Cols[i], 130, "number"))); cols.Add(new GridColumn("t", "Tổng chọn", 120, "number"));
        var rows = r.Shown.Select((x, j) => new[] { x.Label }.Concat(r.ColIdx.Select(i => C(x.Cells[i]))).Concat(new[] { C(r.RowTotals[j]) }).ToArray()).ToList();
        rows.Add(new[] { "TỔNG CHỌN" }.Concat(r.ColIdx.Select((i, k) => r.HasSum[k] ? Kg(r.Sums[k]) : "-")).Concat(new[] { r.GrandHas ? Kg(r.Grand) : "-" }).ToArray());
        _csm.Load(cols, rows); _csg.Refresh();
        _csNote.Text = "Số liệu lấy thẳng từ Bảng Tổng Hợp; “Tổng chọn” = cộng các cột đang chọn. Dòng tiêu đề nhóm (nền xanh) không cộng vào TỔNG CHỌN."
            + (Math.Abs(r.Diff) > 0.005 ? $" Lưu ý: cột “Tổng” của bảng gốc lớn hơn tổng các cột đặc tính {Kg(r.Diff)} kg (hàng chưa gán nhóm đặc tính)." : "");
    }
    private async Task CsPick(bool rows)
    {
        if (_csData is null) { await Ui.Message(this, "Chưa có dữ liệu Bảng Tổng Hợp."); return; }
        var items = rows ? _csData.Rows.Select(r => (Key: r.Key, Label: r.Title ? r.Label : "      " + r.Label, Title: r.Title)).ToList() : _csData.Cols.Select(c => (Key: c, Label: c, Title: false)).ToList();
        var sel = CsSel(rows);
        var boxes = items.Select(it => new CheckBox { Content = it.Label, IsChecked = sel.Contains(it.Key), FontWeight = it.Title ? FontWeight.Bold : FontWeight.Normal }).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            if (!rows || !items[i].Title) continue;
            var start = i; boxes[i].IsCheckedChanged += (_, _) =>   // tick tiêu đề nhóm = tick cả nhóm bên dưới
            {
                for (var j = start + 1; j < items.Count && !items[j].Title; j++) boxes[j].IsChecked = boxes[start].IsChecked;
            };
        }
        var q = new TextBox { Watermark = "Tìm…" };
        var list = new StackPanel { Spacing = 2 }; foreach (var b in boxes) list.Children.Add(b);
        q.TextChanged += (_, _) => { var t = JsText.Fuzzy(q.Text); for (var i = 0; i < items.Count; i++) boxes[i].IsVisible = t.Length == 0 || JsText.Fuzzy(items[i].Label).Contains(t, StringComparison.Ordinal); };
        var all = new Button { Content = "Chọn tất cả" }; var none = new Button { Content = "Bỏ hết" };
        all.Click += (_, _) => { foreach (var b in boxes.Where(b => b.IsVisible)) b.IsChecked = true; };
        none.Click += (_, _) => { foreach (var b in boxes.Where(b => b.IsVisible)) b.IsChecked = false; };
        var ok = new Button { Content = "Áp dụng", Classes = { "primary" } }; var cancel = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 8, Children = { q, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { all, none } }, new ScrollViewer { Content = list, MaxHeight = 420 }, Ui.Buttons(cancel, ok) } };
        var w = Ui.Dialog(rows ? "Chọn Size" : "Chọn Đặc Tính", body, 420);
        cancel.Click += (_, _) => w.Close();
        ok.Click += (_, _) => { CsStore(rows, items.Where((it, i) => boxes[i].IsChecked == true).Select(it => it.Key).ToHashSet()); w.Close(); RenderCs(); };
        await Ui.Show(this, w);
    }
    private async Task CsSaveView()
    {
        if (_csData is null) return;
        var box = new TextBox { Watermark = "VD: TGC xuất Nhật", Width = 280 }; string? name = null;
        var ok = new Button { Content = "Lưu", Classes = { "primary" } }; var cancel = new Button { Content = "Đóng", IsCancel = true };
        var w = Ui.Dialog("Đặt tên mẫu", new StackPanel { Spacing = 8, Children = { box, Ui.Buttons(cancel, ok) } }, 360);
        cancel.Click += (_, _) => w.Close(); ok.Click += (_, _) => { name = (box.Text ?? "").Trim(); w.Close(); };
        await Ui.Show(this, w);
        if (string.IsNullOrEmpty(name)) return;
        if (_cs.Views.ContainsKey(name) && !await Ui.Confirm(this, $"Mẫu “{name}” đã có — ghi đè?", "Lưu mẫu", "Ghi đè")) return;
        _cs.Views[name] = (_cs.Sizes?.ToList(), _cs.Cols?.ToList()); CsSave(); RenderCs();
        _loading = true; try { _csViews.SelectedItem = name; } finally { _loading = false; }
    }

    private void RenderBc()
    {
        if (!D.HasData) { _pivot = null; _bcInfo.Text = "Chưa có dữ liệu — hãy nạp file ở tab Load Data trước."; _bcm.Load(new[] { new GridColumn("x", "", 200) }, Array.Empty<string[]>()); _bcg.Refresh(); return; }
        _pivot = D.ComputePivot(); var p = _pivot;
        var has = GuiData.DtGroups.ToDictionary(g => g, g => (p.Grand.ByGroup.TryGetValue(g, out var v) && v != 0) || p.Cats.Any(c => (c.B.ByGroup.TryGetValue(g, out var x) && x != 0) || c.Sizes.Any(s => s.B.ByGroup.TryGetValue(g, out var y) && y != 0)));
        var vis = _bcHide ? GuiData.DtGroups.Where(g => has[g]).ToList() : GuiData.DtGroups.ToList();
        string Cell(double? v) => v is double d && d != 0 ? Kg(d) : "-";
        string[] Row(string label, GuiData.Bucket b) => new[] { label }.Concat(vis.Select(g => Cell(b.ByGroup.TryGetValue(g, out var v) ? v : 0))).Concat(new[] { Cell(b.Tong) }).ToArray();
        var rows = new List<string[]>(); _bcKinds = new();
        rows.Add(Row("TỔNG TỒN", p.Grand)); _bcKinds.Add(("total", 0));
        foreach (var c in p.Cats) { rows.Add(Row(c.Label, c.B)); _bcKinds.Add(("group", 0)); foreach (var s in c.Sizes) { rows.Add(Row(s.Size, s.B)); _bcKinds.Add(("size", 0)); } }
        rows.Add(Row("TỔNG TỒN", p.Grand)); _bcKinds.Add(("total", 0));
        var cols = new List<GridColumn> { new("s", "SIZE", 150) { ReadOnly = true } };
        cols.AddRange(vis.Select(g => new GridColumn("g" + g, g, 130, "number") { ReadOnly = true })); cols.Add(new GridColumn("t", "TỔNG", 120, "number") { ReadOnly = true });
        _bcm.Load(cols, rows); _bcg.Refresh();
        var sizes = p.Cats.Sum(c => c.Sizes.Count);
        _bcInfo.Text = $"{N(p.Cats.Count)} nhóm · {N(sizes)} dòng size · Tổng KG {Kg(p.Grand.Tong)} — chỉ tính dòng có đặc tính ĐÃ GỘP nhóm (xem tab Đối Soát nếu thấy lệch)." + (_bcHide ? $" · đã ẩn {GuiData.DtGroups.Count(g => !has[g])} cột trống" : "");
    }

    private async Task ExportBc()
    {
        if (_pivot is null) return;
        var date = await AskDate(D.DataDate); if (date is null) return;
        await SaveXlsx(GuiExport.BangTongHop(_pivot, date), GuiExport.BangTongHopFileName(DateTime.Now));
    }
    private async Task<string?> AskDate(string initial)
    {
        var box = new TextBox { Text = initial.Length > 0 ? initial : DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Width = 160 };
        string? res = null; var msg = Muted("");
        var body = new StackPanel { Spacing = 8, Children = { Muted("Nhập NGÀY BÁO CÁO ghi trên file (dd/mm/yyyy). Mặc định = Ngày số liệu."), box, msg } };
        var cancel = new Button { Content = "Đóng", IsCancel = true }; var ok = new Button { Content = "Xuất Excel", Classes = { "primary" } };
        body.Children.Add(Ui.Buttons(cancel, ok));
        var w = Ui.Dialog("Ngày báo cáo", body, 380); cancel.Click += (_, _) => w.Close();
        ok.Click += (_, _) => { var d = DataDate.Parse(box.Text ?? ""); if (d.Length == 0) { msg.Text = "Ngày không hợp lệ — gõ dạng dd/mm/yyyy."; return; } res = d; w.Close(); };
        await Ui.Show(this, w); return res;
    }

    // =====================================================================================
    // Đối Soát
    // =====================================================================================
    private readonly GridModel _dsm = new() { ReadOnly = true }; private SheetGrid _dsg = null!; private GuiData.DoiSoat? _ds; private (double Tot, int N, List<(GuiStore.Slip Slip, GuiStore.SlipItem Item)> List) _dsPx;
    private readonly List<string> _dsKeys = new();

    private Control BuildDs()
    {
        _dsg = new SheetGrid(_dsm) { SpareRows = 0, CellBackground = (r, c) => c == 2 && r < _dsKeys.Count && _dsKeys[r].StartsWith("!") ? LowBg : null };
        var bar = Bar(); bar.Children.Add(Btn("🔍 Xem chi tiết dòng chọn", async () => await DsDetail(), primary: true));
        return Page(new StackPanel { Children = { bar, Muted("Đối soát tổng trọng lượng đã nạp với 6 nhóm Phân Loại và với Bảng Tổng Hợp: chênh lệch = dòng bị đếm trùng, không thuộc nhóm nào, thiếu đặc tính, hoặc đặc tính chưa gộp.") } }, Framed(_dsg));
    }

    private void RenderDs()
    {
        _dsKeys.Clear();
        if (!D.HasData) { _ds = null; _dsm.Load(new[] { new GridColumn("x", "Chưa có dữ liệu — hãy nạp file ở tab Load Data trước.", 500) }, Array.Empty<string[]>()); _dsg.Refresh(); return; }
        var ds = _ds = D.ComputeDoiSoat(); var px = Svc.PxDeducted(); _dsPx = (px.TotalQty, px.Count, px.List); var pivot = D.ComputePivot();
        var rows = new List<string[]>();
        void Add(string key, string label, string val, string note) { _dsKeys.Add(key); rows.Add(new[] { label, key.StartsWith("!") ? "chi tiết ▸" : "", val, note }); }
        Add("t", "Tổng trọng lượng Load Data", Kg(ds.LoadTotal) + " kg", $"{N(D.Rows.Count)} dòng");
        Add("m", "Tổng 6 nhóm Phân Loại (cộng lại)", Kg(ds.ModuleTotal) + " kg", "Bằng tổng Load Data + phần đếm trùng");
        Add("!dup", "Đếm trùng ở nhiều nhóm", Kg(ds.DupSum) + " kg", $"{N(ds.DupCount)} dòng");
        Add("!ndt", "Không suy ra được đặc tính", Kg(ds.NoDtSum) + " kg", $"{N(ds.NoDtCount)} dòng — không vào Bảng Tổng Hợp");
        Add("!ung", "Có đặc tính nhưng CHƯA GỘP nhóm", Kg(ds.UngroupedSum) + " kg", $"{N(ds.UngroupedCount)} dòng — không vào Bảng Tổng Hợp");
        Add("b", "Tổng trong Bảng Tổng Hợp", Kg(pivot.Grand.Tong) + " kg", "Chỉ dòng có đặc tính đã gộp");
        var diff = ds.ModuleTotal - ds.DupSum - ds.NoDtSum - ds.UngroupedSum - pivot.Grand.Tong;
        Add("c", "Chênh lệch chưa giải thích được", Kg(diff) + " kg", Math.Abs(diff) < 0.005 ? "✓ Khớp" : "⚠ Kiểm tra lại");
        Add("!px", "Đã trừ theo phiếu xuất (đang trừ)", N(px.TotalQty) + " kiện", $"{N(px.Count)} dòng phiếu");
        _dsm.Load(new[] { new GridColumn("l", "Nội dung", 360), new GridColumn("d", "", 90), new GridColumn("v", "Số liệu", 170, "number"), new GridColumn("n", "Ghi chú", 360) }, rows); _dsg.Refresh();
    }

    private async Task DsDetail()
    {
        if (_ds is null || _dsg.Row < 0 || _dsg.Row >= _dsKeys.Count) { await Ui.Message(this, "Chọn một dòng có “chi tiết ▸”."); return; }
        var key = _dsKeys[_dsg.Row];
        if (!key.StartsWith("!")) { await Ui.Message(this, "Dòng này không có chi tiết."); return; }
        var m = new GridModel { ReadOnly = true }; string title;
        if (key == "!px")
        {
            title = "Đã trừ theo phiếu xuất";
            m.Load(new[] { new GridColumn("p", "Phiếu", 100), new GridColumn("a", "Mã hàng", 190), new GridColumn("b", "Tên hàng", 280), new GridColumn("c", "Số lô", 130), new GridColumn("d", "Size", 80), new GridColumn("q", "SL xuất", 90, "number") },
                _dsPx.List.Take(300).Select(x => new[] { x.Slip.MaPhieu, x.Item.MaHang, x.Item.TenHang, x.Item.SoLo, x.Item.Size, N(x.Item.SoLuongXuat) }));
        }
        else
        {
            var list = key switch { "!dup" => _ds.DupList, "!ndt" => _ds.NoDtList, _ => _ds.UngroupedList }; title = key switch { "!dup" => "Dòng bị đếm trùng", "!ndt" => "Dòng không có đặc tính", _ => "Đặc tính chưa gộp" };
            m.Load(new[] { new GridColumn("a", "Mã hàng", 190), new GridColumn("b", "Tên hàng", 280), new GridColumn("c", "Số lô", 130), new GridColumn("d", "Size", 80), new GridColumn("w", "Trọng lượng (kg)", 120, "number"), new GridColumn("e", key == "!dup" ? "Thuộc nhóm" : "Đặc tính", 180) },
                list.Take(300).Select(x => new[] { x.MaHang, x.TenHang, x.SoLo, x.Size, Kg(x.TrongLuong), x.Extra }));
        }
        var close = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 8, Children = { Framed(new SheetGrid(m) { SpareRows = 0, Height = 380 }), Muted("Hiện tối đa 300 dòng lớn nhất."), Ui.Buttons(close) } };
        var w = Ui.Dialog(title, body, 1000); close.Click += (_, _) => w.Close(); await Ui.Show(this, w);
    }
}

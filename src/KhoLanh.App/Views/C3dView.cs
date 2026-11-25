using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.Container3D;

namespace KhoLanh.App.Views;

/// <summary>
/// Module Container 3D (M14 của app cũ, chuyển sang C#): mô phỏng xếp hàng vào container lạnh 20' / 40'.
/// 3 bảng: Hàng hóa · Loại container · Mô phỏng 3D &amp; Báo cáo; lưu phương án, danh mục loại thùng, Tính CBM / Pallet, Xuất Excel, Lưu ảnh 3D.
/// Dữ liệu dùng chung database "container-3d" với app cũ.
/// </summary>
public sealed class C3dView : UserControl, IPage
{
    public string Title => "Container 3D";
    public string Subtitle => "Mô phỏng xếp hàng vào container lạnh: tự sắp thứ tự đóng, hướng đặt, hàng mẫu hải quan, kiểm tra độc lập từng thùng.";

    private static C3dService Svc => App.Services.C3d;
    private static C3dService.State St => Svc.St;
    private static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2");
    private static readonly IBrush Dark = AppTheme.Brush("#0B2A45");
    private static readonly IBrush Bad = AppTheme.Brush("#B71C1C");
    private static readonly IBrush Good = AppTheme.Brush("#1B5E20");
    private static readonly IBrush WarnFg = AppTheme.Brush("#8A5A00");

    private readonly TabControl _tabs = new();
    private readonly StackPanel _skuList = new() { Spacing = 8 };
    private readonly StackPanel _contPanel = new() { Spacing = 8 };
    private readonly C3dCanvas _cv = new();
    private readonly StackPanel _report = new() { Spacing = 6 };
    private readonly StackPanel _layers = new() { Spacing = 2 };
    private readonly Slider _seq = new() { Minimum = 0, Maximum = 1, Width = 260, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _seqLab = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 80 };
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
    private readonly Border _sel = new() { Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), IsVisible = false, MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8) };
    private readonly Border _tip = new() { Background = new SolidColorBrush(Color.FromArgb(235, 11, 42, 69)), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4), IsVisible = false, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _ctx = new() { FontSize = 12, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private C3dPlan? _plan;
    private List<string> _gcolors = new();
    private readonly HashSet<int> _hidT = new(), _hidG = new(), _hidR = new();
    private int? _selRow;
    private int _calcVer;
    private long _ms;
    private readonly DispatcherTimer _calcT = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _saveT = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _play = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private string? _openedAuto;
    private bool _busySeq;
    private C3dCbm.Pallet _pal = new();

    private double F => St.Unit == "cm" ? 10 : 1;
    private string ToU(double mm) => Math.Round(mm / F, 2).ToString("0.##", CultureInfo.InvariantCulture);
    private double FromU(string? s) => C3dJson.Num(s) * F;
    private static string Fmt(double n, int d = 0) => n.ToString(d == 0 ? "#,0" : "#,0." + new string('0', d), CultureInfo.InvariantCulture);

    public C3dView()
    {
        _calcT.Tick += (_, _) => { _calcT.Stop(); Calc(); };
        _saveT.Tick += (_, _) => { _saveT.Stop(); try { Svc.SaveState(); } catch { /* tự lưu: bỏ qua */ } };
        _play.Tick += (_, _) => PlayTick();
        _cv.Picked += OnPick;
        _cv.Hovered += OnHover;
        _seq.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty && !_busySeq) SetSeq((int)Math.Round(_seq.Value), false); };

        var tools = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        tools.Children.Add(Btn("Tính CBM / Pallet", OpenCbm, "Tính CBM từng mặt hàng, số thùng trên 1 pallet, số pallet cần và số pallet cont chứa được"));
        tools.Children.Add(Btn("Tính toán xếp hàng", () => { Calc(); _tabs.SelectedIndex = 2; }, "Tính lại phương án (cũng tự tính khi bạn sửa số liệu)", true));
        if (Svc.CanEdit) tools.Children.Add(Btn("Lưu phương án", SavePlanDialog, "Lưu container + danh sách hàng + tuỳ chọn thành 1 phương án để mở lại sau"));
        tools.Children.Add(Btn("Phương án đã lưu", OpenPlans));
        if (Svc.CanExport) { tools.Children.Add(Btn("Xuất Excel", ExportExcel)); tools.Children.Add(Btn("Lưu ảnh 3D", ExportPng)); }
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(tools, Dock.Right); head.Children.Add(tools);
        var hl = new StackPanel { Spacing = 2 }; hl.Children.Add(_ctx); hl.Children.Add(_status); head.Children.Add(hl);

        _tabs.ItemsSource = new[]
        {
            new TabItem { Header = "1  Hàng hóa", Content = BuildGoods() },
            new TabItem { Header = "2  Loại container", Content = new ScrollViewer { Content = _contPanel, Padding = new Thickness(0, 4, 12, 4) } },
            new TabItem { Header = "3  Mô phỏng 3D & Báo cáo", Content = BuildSim() },
        };
        var root = new DockPanel(); DockPanel.SetDock(head, Dock.Top); root.Children.Add(head); root.Children.Add(_tabs);
        Content = root;
        AttachedToVisualTree += (_, _) => Boot();
        DetachedFromVisualTree += (_, _) => { _play.Stop(); if (_saveT.IsEnabled) { _saveT.Stop(); try { Svc.SaveState(); } catch { } } };
    }

    private static Button Btn(string text, Action click, string? tip = null, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
    private static Button Btn(string text, Func<Task> click, string? tip = null) => Btn(text, () => { _ = click(); }, tip);
    private static TextBlock Muted(string t) => new() { Text = t, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private static TextBlock H(string t) => new() { Text = t, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Dark, Margin = new Thickness(0, 4, 0, 2) };
    private static Border Card(Control c) => new() { Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10), Child = c };

    private void Boot()
    {
        if (!Svc.CanView) { Content = Muted("Tài khoản chưa được cấp quyền xem Container 3D."); return; }
        try { Svc.Load(); } catch (Exception e) { _status.Text = "Không đọc được dữ liệu: " + e.Message; }
        RenderGoods(); RenderCont(); UpdCtx();
        Calc();
    }

    private void Changed(bool recalc = true, int delay = 450)
    {
        if (Svc.CanEdit) { _saveT.Stop(); _saveT.Start(); }
        if (recalc) { _calcT.Stop(); _calcT.Interval = TimeSpan.FromMilliseconds(Math.Max(1, delay)); _calcT.Start(); }
        UpdCtx();
    }

    private void UpdCtx()
    {
        var q = St.Skus.Sum(k => k.Qty);
        _ctx.Text = $"{St.Cont?.Name ?? "Container"} · {St.Skus.Count} mặt hàng · {Fmt(q)} thùng";
    }

    // ================================================================== BẢNG 1 — HÀNG HÓA
    private Control BuildGoods()
    {
        var bar = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 4, 0, 8) };
        bar.Children.Add(Btn("+ Thêm mặt hàng", () => { St.Skus.Add(C3dService.NewSku()); RenderGoods(); Changed(delay: 1); }, null, true));
        bar.Children.Add(Btn("Dán từ Excel", OpenPaste, "Copy các ô trong Excel rồi dán: Mã · Size · Date · SL · Dài · Rộng · Cao · kg · Xoay · Nằm · Đứng · Sole · Tầng tối đa"));
        bar.Children.Add(Btn("Lấy từ danh mục", OpenCat, "Lấy loại thùng đã lưu (kích thước, kg, cách xếp) — chỉ cần nhập số lượng"));
        bar.Children.Add(Btn("Ví dụ mẫu", async () => { if (St.Skus.Count > 0 && !await Ui.Confirm(this, "Thay danh sách hàng hiện tại bằng ví dụ mẫu?")) return; St.Skus = C3dService.DemoSkus(); RenderGoods(); Changed(delay: 1); }));
        bar.Children.Add(Btn("Xóa hết", async () => { if (St.Skus.Count > 0 && !await Ui.Confirm(this, "Xóa toàn bộ danh sách hàng hóa?", yes: "Xóa")) return; St.Skus = new(); RenderGoods(); Changed(delay: 1); }));
        var note = Muted("Hệ thống tự sắp thứ tự đóng: mặt hàng → size → ngày; nhóm đầu vào SÂU NHẤT (sát vách đầu), nhóm cuối sát cửa. Mỗi dòng (mã · size · ngày) là 1 nhóm. Hàng mẫu hải quan tự chia, không cần nhập.");
        var top = new StackPanel(); top.Children.Add(bar); top.Children.Add(note);
        var dp = new DockPanel(); DockPanel.SetDock(top, Dock.Top); dp.Children.Add(top);
        dp.Children.Add(new ScrollViewer { Content = _skuList, Padding = new Thickness(0, 6, 12, 6) });
        return dp;
    }

    private void RenderGoods()
    {
        _skuList.Children.Clear();
        if (St.Skus.Count == 0) { _skuList.Children.Add(Muted("Chưa có mặt hàng. Bấm “+ Thêm mặt hàng”, “Dán từ Excel” hoặc “Ví dụ mẫu”.")); return; }
        for (var i = 0; i < St.Skus.Count; i++) _skuList.Children.Add(SkuCard(St.Skus[i], i));
    }

    private TextBox Field(WrapPanel host, string label, string value, Action<string> set, double width = 90, string? tip = null)
    {
        var sp = new StackPanel { Spacing = 1 };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Classes = { "muted" } });
        var tb = new TextBox { Text = value, Width = width, FontSize = 12.5, Padding = new Thickness(6, 3) };
        if (tip is not null) ToolTip.SetTip(tb, tip);
        tb.TextChanged += (_, _) => set(tb.Text ?? "");
        sp.Children.Add(tb); host.Children.Add(sp);
        return tb;
    }

    private static CheckBox Chk(string text, bool value, Action<bool> set, string? tip = null)
    {
        var c = new CheckBox { Content = text, IsChecked = value, FontSize = 12.5 };
        if (tip is not null) ToolTip.SetTip(c, tip);
        c.IsCheckedChanged += (_, _) => set(c.IsChecked == true);
        return c;
    }

    private static readonly string[] OName = { "Dài", "Rộng", "Cao" };
    private static string OriLabel(string p, C3dSku k)
    {
        double D(char c) => c == 'L' ? k.L : c == 'W' ? k.W : k.H;
        string N(char c) => c == 'L' ? "Dài" : c == 'W' ? "Rộng" : "Cao";
        return C3dEngine.VName(D(p[2]), k).ToUpperInvariant() + $" · {N(p[0])} dọc cont · {N(p[1])} ngang · {N(p[2])} đứng (cao {Math.Round(D(p[2]))})";
    }

    private Control SkuCard(C3dSku k, int i)
    {
        var col = AppTheme.Color(Svc.ColorFor(k.Code));
        var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(col), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(swatch, "Bấm để đổi màu của mã hàng này");
        swatch.PointerPressed += async (_, _) =>
        {
            var pick = await Ui.Choose(this, "Màu hiển thị cho mã hàng \"" + (k.Code.Length > 0 ? k.Code : "(trống)") + "\":", "Đổi màu", C3dService.Palette.Select((p, j) => $"Màu {j + 1} ({p})").ToList(), Math.Max(0, Array.IndexOf(C3dService.Palette, Svc.ColorFor(k.Code))));
            if (pick is null) return; St.Colors[k.Code.Trim().Length > 0 ? k.Code.Trim() : "(trống)"] = C3dService.Palette[pick.Value.Index];
            RenderGoods(); ApplyColors(); Changed(false);
        };
        var title = new TextBlock { Text = $"#{i + 1}  " + (k.Code.Length > 0 ? k.Code : "Chưa nhập mã") + (k.Size.Length > 0 ? " · " + k.Size : "") + (k.Date.Length > 0 ? " · " + k.Date : ""), FontWeight = FontWeight.Bold, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center };
        var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        acts.Children.Add(Btn("Nhân đôi", () => { var c = k.Clone(); c.Id = C3dService.NewSku().Id; St.Skus.Insert(St.Skus.IndexOf(k) + 1, c); RenderGoods(); Changed(delay: 1); }));
        if (Svc.CanEdit) acts.Children.Add(Btn("Lưu danh mục", async () => { if (await Ui.Run(this, () => _status.Text = Svc.SaveToCat(k))) RenderGoods(); }, "Lưu kích thước + kg + cách xếp của loại thùng này để dùng cho các cont sau"));
        acts.Children.Add(Btn("Xóa", () => { St.Skus.Remove(k); RenderGoods(); Changed(delay: 1); }));
        var hdr = new DockPanel(); DockPanel.SetDock(acts, Dock.Right); hdr.Children.Add(acts);
        var hl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; hl.Children.Add(swatch); hl.Children.Add(title); hdr.Children.Add(hl);

        var info = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        var vol = Muted(VolText(k));
        void Upd(bool titleToo = false) { vol.Text = VolText(k); if (titleToo) title.Text = $"#{St.Skus.IndexOf(k) + 1}  " + (k.Code.Length > 0 ? k.Code : "Chưa nhập mã") + (k.Size.Length > 0 ? " · " + k.Size : "") + (k.Date.Length > 0 ? " · " + k.Date : ""); Changed(); }
        Field(info, "Mã hàng", k.Code, v => { k.Code = v.Trim(); swatch.Background = AppTheme.Brush(Svc.ColorFor(k.Code)); Upd(true); }, 120);
        Field(info, "Size", k.Size, v => { k.Size = v.Trim(); Upd(true); }, 70);
        Field(info, "Date (ngày SX)", k.Date, v => { k.Date = v.Trim(); Upd(true); }, 95, "dd/mm/yyyy");
        Field(info, "SL thùng", k.Qty.ToString(CultureInfo.InvariantCulture), v => { k.Qty = (int)Math.Max(0, Math.Floor(C3dJson.Num(v))); Upd(); }, 70);
        Field(info, "kg / thùng", C3dEngine.Js(k.Kg), v => { k.Kg = Math.Max(0, C3dJson.Num(v)); Upd(); }, 70);
        Field(info, $"Dài ({St.Unit})", ToU(k.L), v => { k.L = FromU(v); Upd(); }, 70);
        Field(info, $"Rộng ({St.Unit})", ToU(k.W), v => { k.W = FromU(v); Upd(); }, 70);
        Field(info, $"Cao ({St.Unit})", ToU(k.H), v => { k.H = FromU(v); Upd(); }, 70);
        Field(info, "Tên hàng", k.Name, v => { k.Name = v; Changed(false); }, 150);
        Field(info, "Ghi chú khi đóng hàng", k.Note, v => { k.Note = v; Changed(false); }, 220, "VD: không chồng quá 5 tầng, hàng dễ vỡ…");

        // cách đặt & xếp
        var ori = new ComboBox { FontSize = 12.5, MinWidth = 330 };
        var oriItems = new List<string> { "Tự động (hệ thống chọn hướng tốt nhất)" }; oriItems.AddRange(C3dEngine.Perms.Select(p => OriLabel(p, k)));
        ori.ItemsSource = oriItems; ori.SelectedIndex = k.Orient.Length > 0 ? Array.IndexOf(C3dEngine.Perms, k.Orient) + 1 : 0;
        ori.SelectionChanged += (_, _) => { k.Orient = ori.SelectedIndex > 0 ? C3dEngine.Perms[ori.SelectedIndex - 1] : ""; Changed(delay: 1); };
        var pack = new WrapPanel { ItemSpacing = 10, LineSpacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        var ol = new StackPanel { Spacing = 1 }; ol.Children.Add(new TextBlock { Text = "Hướng đặt thùng", FontSize = 11.5, Classes = { "muted" } }); ol.Children.Add(ori); pack.Children.Add(ol);
        var tplBox = Field(pack, "Dãy mẫu (VD: 6 nằm 2 đứng · trống = tự động)", k.RowPlan, v => { k.RowPlan = v.Trim(); Changed(); }, 210, "Cách xếp trong 1 LỚP của 1 dãy; các dãy còn lại lặp lại mẫu này. Ghi số tầng từng nhóm: 6 nằm x10 2 đứng x8");
        var tplBtn = new Button { Content = "Sắp dãy mẫu…", FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom };
        tplBtn.Click += async (_, _) => { var r = await OpenTpl(k); if (r is not null) tplBox.Text = r; };
        pack.Children.Add(tplBtn);
        Field(pack, "Chồng tối đa (tầng, 0 = không giới hạn)", k.MaxLayers.ToString(CultureInfo.InvariantCulture), v => { k.MaxLayers = (int)Math.Max(0, Math.Floor(C3dJson.Num(v))); Changed(); }, 80);
        var flags = new WrapPanel { ItemSpacing = 12, Margin = new Thickness(0, 6, 0, 0) };
        flags.Children.Add(Chk("Cho xoay", k.AllowRotate, v => { k.AllowRotate = v; Changed(delay: 1); }, "Xoay thùng 90° trên mặt sàn (đổi dài ↔ rộng)"));
        flags.Children.Add(Chk("Giữ cao gốc", k.AllowStand, v => { k.AllowStand = v; Changed(delay: 1); }, "Cho đặt đúng chiều CAO bạn nhập thẳng đứng"));
        flags.Children.Add(Chk("Cho đặt nằm", k.AllowLay, v => { k.AllowLay = v; Changed(delay: 1); }, "Cho lật thùng (chiều cao khác chiều gốc)"));
        flags.Children.Add(Chk("Xếp sole", k.Sole, v => { k.Sole = v; Changed(delay: 1); }, "So le trái / phải từng tầng để khí lạnh lưu thông"));
        if (k.RowSet.Count > 0) flags.Children.Add(Btn($"Bỏ chỉnh riêng {k.RowSet.Count} dãy", () => { k.RowSet = new(); RenderGoods(); Changed(delay: 1); }));
        var catM = Svc.CatMatch(k);
        if (catM is not null && (Math.Abs(catM.V.L - k.L) > 0.5 || Math.Abs(catM.V.W - k.W) > 0.5 || Math.Abs(catM.V.H - k.H) > 0.5 || Math.Abs(catM.V.Kg - k.Kg) > 0.01))
            flags.Children.Add(Btn($"Dùng kích thước danh mục ({Math.Round(catM.V.L)}×{Math.Round(catM.V.W)}×{Math.Round(catM.V.H)})", () => { C3dService.ApplyCat(k, catM.V); RenderGoods(); Changed(delay: 1); }));

        var body = new StackPanel { Spacing = 2 };
        body.Children.Add(hdr); body.Children.Add(info); body.Children.Add(vol);
        var exp = new Expander { Header = "Cách đặt & xếp", IsExpanded = k.Orient.Length > 0 || k.RowPlan.Length > 0 || k.Sole || k.AllowLay || k.MaxLayers > 0, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        var eb = new StackPanel(); eb.Children.Add(pack); eb.Children.Add(flags); exp.Content = eb;
        body.Children.Add(exp);
        var card = Card(body); card.BorderBrush = new SolidColorBrush(col); card.BorderThickness = new Thickness(4, 1, 1, 1);
        return card;
    }

    private static string VolText(C3dSku k) => $"Khối 1 thùng: {Fmt(k.L * k.W * k.H / 1e9, 4)} m³ · tổng {Fmt(k.Qty)} thùng: {Fmt(k.L * k.W * k.H * k.Qty / 1e9, 2)} m³ · {Fmt(k.Kg * k.Qty, 1)} kg";

    private async Task OpenPaste()
    {
        var ta = new TextBox { AcceptsReturn = true, Height = 220, TextWrapping = TextWrapping.NoWrap, Watermark = "VRHLCK\t31/40\t15/09/2026\t420\t520\t280\t190\t10", FontFamily = new FontFamily("Consolas, monospace") };
        var ok = new Button { Content = "Thêm vào danh sách", Classes = { "primary" }, IsDefault = true }; var no = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Muted($"Copy các ô trong Excel rồi dán vào đây. Thứ tự cột: Mã hàng · Size · Date · Số lượng thùng · Dài · Rộng · Cao ({St.Unit}) · kg/thùng · Cho xoay · Cho nằm · Giữ cao gốc · Sole · Tầng tối đa (5 cột cuối không bắt buộc; x / 1 / có = bật)."));
        body.Children.Add(ta); body.Children.Add(Ui.Buttons(no, ok));
        var w = Ui.Dialog("Dán danh sách hàng từ Excel", body, 720);
        List<C3dSku>? got = null;
        ok.Click += (_, _) => { got = C3dService.ParsePaste(ta.Text ?? "", F); w.Close(); };
        no.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (got is null) return;
        if (got.Count == 0) { await Ui.Message(this, "Không đọc được dòng nào (cần Dài / Rộng / Cao / Số lượng là số > 0)."); return; }
        St.Skus.AddRange(got); RenderGoods(); Changed(delay: 1); _status.Text = $"Đã thêm {got.Count} dòng hàng.";
    }

    private async Task OpenCat()
    {
        if (Svc.Cat.Count == 0) { await Ui.Message(this, "Danh mục loại thùng còn trống. Nhập 1 mặt hàng rồi bấm “Lưu danh mục” để dùng lại cho các cont sau."); return; }
        var q = new TextBox { Watermark = "Tìm mã / size / tên…", Width = 300 };
        var list = new StackPanel { Spacing = 4 };
        var w = Ui.Dialog("Danh mục loại thùng", new StackPanel(), 720);
        void Paint()
        {
            list.Children.Clear();
            var key = C3dEngine.StripVn(q.Text);
            foreach (var c in Svc.Cat.Where(c => key.Length == 0 || C3dEngine.StripVn(c.Name + " " + c.V.Note).Contains(key)).OrderBy(c => c.Name, StringComparer.Create(Ui.Vi, true)))
            {
                var row = new DockPanel();
                var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                btns.Children.Add(Btn("+ Thêm vào hàng", async () =>
                {
                    var n = C3dService.NewSku(); n.Code = c.V.Code; n.Size = c.V.Size; C3dService.ApplyCat(n, c.V);
                    var qty = await Ui.Prompt(this, $"Số lượng thùng cho “{C3dService.CatLabel(c.V)}”:", "Số lượng", "100");
                    if (qty is null) return; n.Qty = (int)Math.Max(0, Math.Floor(C3dJson.Num(qty)));
                    St.Skus.Add(n); RenderGoods(); Changed(delay: 1); _status.Text = "Đã thêm " + C3dService.CatLabel(c.V);
                }));
                if (Svc.CanEdit) btns.Children.Add(Btn("Xóa", async () => { if (!await Ui.Confirm(this, $"Xóa “{c.Name}” khỏi danh mục?", yes: "Xóa")) return; if (await Ui.Run(this, () => Svc.DeleteCat(c.Id))) Paint(); }));
                DockPanel.SetDock(btns, Dock.Right); row.Children.Add(btns);
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = C3dService.CatLabel(c.V), FontWeight = FontWeight.SemiBold });
                t.Children.Add(Muted($"{Math.Round(c.V.L)}×{Math.Round(c.V.W)}×{Math.Round(c.V.H)} mm · {Fmt(c.V.Kg, 2)} kg/thùng · {Fmt(c.V.L * c.V.W * c.V.H / 1e9, 4)} m³" + (c.V.Note.Length > 0 ? " · " + c.V.Note : "")));
                row.Children.Add(t);
                list.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 4), Child = row });
            }
            if (list.Children.Count == 0) list.Children.Add(Muted("Không có loại thùng phù hợp."));
        }
        q.TextChanged += (_, _) => Paint();
        var close = new Button { Content = "Đóng", IsCancel = true }; close.Click += (_, _) => w.Close();
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(q); body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 460 }); body.Children.Add(Ui.Buttons(close));
        ((Border)w.Content!).Child = body;
        Paint();
        await Ui.Show(this, w);
    }

    // Bảng SẮP DÃY MẪU: số cột NẰM / ĐỨNG / DỰNG CAO trong 1 lớp của 1 dãy, xem trước mặt cắt nhìn từ cửa.
    private async Task<string?> OpenTpl(C3dSku k)
    {
        var specs = C3dEngine.ParseColPlan(k.RowPlan).Select(s => (tok: s.Tok, n: s.N, t: s.T)).ToList();
        if (specs.Count == 0) specs = new() { ("nam", 6, 0), ("dung", 2, 0) };
        var rows = new StackPanel { Spacing = 4 };
        var prev = new Canvas { Width = 470, Height = 280, Background = AppTheme.Brush("#F7F9FC") };
        var stats = Muted("");
        string Str() => string.Join(" ", specs.Where(q => q.n > 0).Select(q => q.n + " " + C3dEngine.TokName(q.tok) + (q.t > 0 ? " x" + q.t : "")));
        var dims = new[] { k.L, k.W, k.H }.OrderBy(x => x).ToArray();
        var toks = new[] { ("nam", $"NẰM · cao {Math.Round(dims[0])}"), ("dung", $"ĐỨNG · cao {Math.Round(dims[1])}"), ("cao", $"DỰNG CAO · cao {Math.Round(dims[2])}") };
        void Preview()
        {
            prev.Children.Clear();
            var pv = C3dEngine.PreviewTpl(k, St.Cont!, Str()); var c = St.Cont!; double W = c.W, Hh = c.H, sc = Math.Min(440 / W, 250 / Hh), pad = 14;
            prev.Children.Add(Rect(pad, pad, W * sc, Hh * sc, "#FFFFFF", "#5B6B82"));
            var x = c.ClearSide;
            var colMap = new Dictionary<string, string> { ["nằm"] = "#3b82f6", ["đứng"] = "#f59e0b", ["dựng cao"] = "#22c55e" };
            foreach (var q in pv.Cols) { for (var t = 0; t < q.T; t++) prev.Children.Add(Rect(pad + x * sc, pad + (Hh - (t + 1) * q.Dz) * sc, q.Dy * sc, q.Dz * sc, colMap[q.Name], "#1F2937")); x += q.Dy; }
            var ry = pad + (Hh - pv.Red) * sc;
            prev.Children.Add(new Avalonia.Controls.Shapes.Line { StartPoint = new Point(pad, ry), EndPoint = new Point(pad + W * sc, ry), Stroke = Brushes.Red, StrokeThickness = 1.5, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 2 } });
            var warn = new List<string>();
            foreach (var d in pv.Detail) { if (!d.Ok) warn.Add($"“{d.N} {C3dEngine.TokName(d.Tok)}” không vừa cont (cao / ngang)."); else if (d.Used < d.N) warn.Add($"“{d.N} {C3dEngine.TokName(d.Tok)}” chỉ xếp được {d.Used} cột (hết bề ngang)."); }
            stats.Text = $"{Fmt(pv.Cap)} thùng / dãy · bề ngang dùng {Fmt(pv.Width)} / {Fmt(pv.Wu)} mm (còn trống {Fmt(pv.Wu - pv.Width)}) · cột cao nhất {Fmt(pv.Top)} mm (giới hạn {Fmt(pv.EffH)}) · sâu {Fmt(pv.Depth)} mm" + (warn.Count > 0 ? "\n⚠ " + string.Join(" ", warn) : "");
        }
        void Paint()
        {
            rows.Children.Clear();
            for (var i = 0; i < specs.Count; i++)
            {
                var idx = i; var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var n = new TextBox { Text = specs[i].n.ToString(CultureInfo.InvariantCulture), Width = 50 };
                var cb = new ComboBox { ItemsSource = toks.Select(t => t.Item2).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(toks, t => t.Item1 == specs[idx].tok)), Width = 190 };
                var t = new TextBox { Text = specs[i].t > 0 ? specs[i].t.ToString(CultureInfo.InvariantCulture) : "", Width = 50, Watermark = "tự" };
                n.TextChanged += (_, _) => { specs[idx] = (specs[idx].tok, (int)Math.Max(0, Math.Floor(C3dJson.Num(n.Text))), specs[idx].t); Preview(); };
                cb.SelectionChanged += (_, _) => { specs[idx] = (toks[Math.Max(0, cb.SelectedIndex)].Item1, specs[idx].n, specs[idx].t); Preview(); };
                t.TextChanged += (_, _) => { specs[idx] = (specs[idx].tok, specs[idx].n, (int)Math.Max(0, Math.Floor(C3dJson.Num(t.Text)))); Preview(); };
                var del = new Button { Content = "✕" }; del.Click += (_, _) => { specs.RemoveAt(idx); Paint(); };
                r.Children.Add(n); r.Children.Add(new TextBlock { Text = "cột", VerticalAlignment = VerticalAlignment.Center }); r.Children.Add(cb);
                r.Children.Add(new TextBlock { Text = "số tầng", VerticalAlignment = VerticalAlignment.Center }); r.Children.Add(t); r.Children.Add(del);
                rows.Children.Add(r);
            }
            Preview();
        }
        string? result = null;
        var add = new Button { Content = "+ Thêm nhóm cột" }; add.Click += (_, _) => { specs.Add(("nam", 1, 0)); Paint(); };
        var ok = new Button { Content = "Áp dụng", Classes = { "primary" }, IsDefault = true }; var clr = new Button { Content = "Bỏ dãy mẫu (tự động)" }; var no = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Muted("Trong 1 lớp của 1 dãy xếp cạnh nhau (trái → phải, nhìn từ cửa). Mỗi cột chồng tới red line (hoặc số tầng ghi riêng). Các dãy của mặt hàng lặp lại mẫu này."));
        body.Children.Add(rows); body.Children.Add(add); body.Children.Add(prev); body.Children.Add(stats); body.Children.Add(Ui.Buttons(no, clr, ok));
        var w = Ui.Dialog("Sắp dãy mẫu — " + (k.Code.Length > 0 ? k.Code : "mặt hàng") + " " + k.Size, body, 560);
        ok.Click += (_, _) => { result = Str(); w.Close(); };
        clr.Click += (_, _) => { result = ""; w.Close(); };
        no.Click += (_, _) => w.Close();
        Paint();
        await Ui.Show(this, w);
        if (result is not null) { k.RowPlan = result; Changed(delay: 1); }
        return result;
    }

    private static Avalonia.Controls.Shapes.Rectangle Rect(double x, double y, double w, double h, string fill, string stroke)
    {
        var r = new Avalonia.Controls.Shapes.Rectangle { Width = Math.Max(0.5, w), Height = Math.Max(0.5, h), Fill = AppTheme.Brush(fill), Stroke = AppTheme.Brush(stroke), StrokeThickness = 0.6 };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y); return r;
    }

    // ================================================================== BẢNG 2 — LOẠI CONTAINER + CÁCH XẾP
    private void RenderCont()
    {
        _contPanel.Children.Clear();
        var c = St.Cont!; var lst = Svc.ContList();
        var sel = new ComboBox { MinWidth = 300, ItemsSource = lst.Select(x => (x.Builtin ? "" : "★ ") + x.C.Name + (x.Edited ? " (đã sửa)" : "")).ToList(), SelectedIndex = Math.Max(0, lst.FindIndex(x => x.Id == St.Cid)) };
        sel.SelectionChanged += (_, _) => { if (sel.SelectedIndex < 0) return; Svc.PickContainer(lst[sel.SelectedIndex].Id); RenderCont(); RenderGoods(); Changed(delay: 1); };
        var unit = new ComboBox { ItemsSource = new[] { "mm", "cm" }, SelectedIndex = St.Unit == "cm" ? 1 : 0, Width = 80 };
        unit.SelectionChanged += (_, _) => { St.Unit = unit.SelectedIndex == 1 ? "cm" : "mm"; RenderCont(); RenderGoods(); Changed(false); };
        var r1 = new WrapPanel { ItemSpacing = 10, LineSpacing = 6 };
        var s1 = new StackPanel(); s1.Children.Add(Muted("Chọn container")); s1.Children.Add(sel); r1.Children.Add(s1);
        var s2 = new StackPanel(); s2.Children.Add(Muted("Đơn vị dài")); s2.Children.Add(unit); r1.Children.Add(s2);

        var ro = Muted("");
        void UpdRo()
        {
            var red = c.RedLine > 0 ? c.RedLine : c.H; var eff = Math.Min(c.H, Math.Min(red, c.DoorH > 0 ? c.DoorH : c.H));
            var vol = c.L * c.W * c.H / 1e9; var lu = c.L - c.ClearFront - c.ClearRear; var wu = c.W - 2 * c.ClearSide;
            ro.Text = $"Thể tích trong: {Fmt(vol, 2)} m³ · Chứa hàng được (tới giới hạn xếp, trừ khe): {Fmt(lu * wu * eff / 1e9, 2)} m³ · Cao xếp tối đa: {Fmt(eff)} mm · Dài chứa hàng: {Fmt(lu)} mm · Rộng chứa hàng: {Fmt(wu)} mm";
        }
        var g = new WrapPanel { ItemSpacing = 10, LineSpacing = 6 };
        Field(g, "Tên container", c.Name, v => { c.Name = v; UpdCtx(); Changed(false); }, 220);
        var type = new ComboBox { ItemsSource = C3dService.Types.Select(t => t.name).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(C3dService.Types, t => t.id == c.Type)), Width = 120 };
        type.SelectionChanged += (_, _) => { c.Type = C3dService.Types[Math.Max(0, type.SelectedIndex)].id; Changed(false); };
        var ts = new StackPanel(); ts.Children.Add(Muted("Loại")); ts.Children.Add(type); g.Children.Add(ts);
        var u = St.Unit;
        void Len(string label, Func<double> get, Action<double> set, string? tip = null) => Field(g, $"{label} ({u})", ToU(get()), v => { set(FromU(v)); UpdRo(); Changed(); }, 95, tip);
        Len("Dài trong", () => c.L, v => c.L = v); Len("Rộng trong", () => c.W, v => c.W = v); Len("Cao trong", () => c.H, v => c.H = v);
        Len("Rộng cửa", () => c.DoorW, v => c.DoorW = v); Len("Cao cửa", () => c.DoorH, v => c.DoorH = v);
        Field(g, "Tải trọng cho phép (kg)", C3dEngine.Js(c.MaxPayload), v => { c.MaxPayload = C3dJson.Num(v); Changed(); }, 110);
        Len("Red line — cao tối đa xếp", () => c.RedLine, v => c.RedLine = v, "Vạch đỏ trên vách cont lạnh: hàng KHÔNG được cao quá vạch này để khí lạnh lưu thông phía trên.");
        Len("Khe vách đầu", () => c.ClearFront, v => c.ClearFront = v, "Khe vách đầu (có cửa gió lạnh) / khe cửa / hai bên: để trống khoảng này khi xếp.");
        Len("Khe cửa", () => c.ClearRear, v => c.ClearRear = v); Len("Khe mỗi bên", () => c.ClearSide, v => c.ClearSide = v);
        var btns = new WrapPanel { ItemSpacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        btns.Children.Add(Btn("Về mặc định 40' HC RF", () => { Svc.PickContainer("rf40hc"); RenderCont(); Changed(delay: 1); }));
        if (Svc.CanEdit)
        {
            btns.Children.Add(Btn("Lưu thay đổi", async () => { if (await Ui.Run(this, () => _status.Text = Svc.SaveContainer(false))) RenderCont(); }));
            btns.Children.Add(Btn("Lưu thành cont mới", async () => { if (await Ui.Run(this, () => _status.Text = Svc.SaveContainer(true))) RenderCont(); }));
            var cur = lst.FirstOrDefault(x => x.Id == St.Cid);
            if (cur is { Builtin: false } || cur is { Edited: true })
                btns.Children.Add(Btn(cur.Builtin ? "Khôi phục mẫu gốc" : "Xóa container", async () =>
                {
                    if (!await Ui.Confirm(this, cur.Builtin ? $"Khôi phục \"{cur.C.Name}\" về thông số mẫu gốc?" : $"Xóa container \"{cur.C.Name}\"?", yes: cur.Builtin ? "Khôi phục" : "Xóa")) return;
                    if (await Ui.Run(this, () => _status.Text = Svc.DeleteContainer())) { RenderCont(); Changed(delay: 1); }
                }));
        }
        var cc = new StackPanel { Spacing = 6 };
        cc.Children.Add(H("Thông số container")); cc.Children.Add(Muted("Mặc định: cont lạnh 40' HC RF — sửa tay theo cont thực tế. " + (c.Note.Length > 0 ? c.Note : "")));
        cc.Children.Add(r1); cc.Children.Add(g); cc.Children.Add(ro); cc.Children.Add(btns);
        UpdRo();
        _contPanel.Children.Add(Card(cc));

        // cách xếp hàng
        var o = St.Opts; var oc = new StackPanel { Spacing = 6 };
        oc.Children.Add(H("Cách xếp hàng"));
        var rbSep = new RadioButton { GroupName = "c3dmrg", IsChecked = !o.MergeTail, Content = "Tách riêng từng mặt hàng — mỗi mặt hàng bắt đầu ở 1 vách mới, ranh giới rõ ràng" };
        var rbMrg = new RadioButton { GroupName = "c3dmrg", IsChecked = o.MergeTail, Content = "Ghép lấp chỗ trống cạnh vách cuối (khuyên dùng — không để trống giữa các mặt hàng)" };
        rbMrg.IsCheckedChanged += (_, _) => { o.MergeTail = rbMrg.IsChecked == true; Changed(delay: 1); };
        oc.Children.Add(rbSep); oc.Children.Add(rbMrg);
        oc.Children.Add(Chk("Tự đóng so le khi trống một bên (khe dư bề ngang dồn về 1 vách)", o.AutoLayout, v => { o.AutoLayout = v; Changed(delay: 1); }, "Bật: tự đóng so le trái/phải từng tầng (không mất chỗ); cont còn trống nhiều thì ưu tiên dựng đứng + thoáng khí"));
        var spPct = new TextBox { Text = C3dEngine.Js(o.SpreadPct), Width = 60, IsEnabled = o.Spread };
        spPct.TextChanged += (_, _) => { o.SpreadPct = Math.Min(100, Math.Max(20, C3dJson.Num(spPct.Text, 70))); Changed(); };
        var spr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        spr.Children.Add(Chk("Hàng ít: trải đều thành nhiều dãy thấp tới", o.Spread, v => { o.Spread = v; spPct.IsEnabled = v; Changed(delay: 1); }, "Không đóng đặc ít dãy cao rồi để trống phía sau (dễ đổ): hạ số tầng, trải thành nhiều dãy thấp"));
        spr.Children.Add(spPct); spr.Children.Add(new TextBlock { Text = "% chiều dài cont", VerticalAlignment = VerticalAlignment.Center });
        oc.Children.Add(spr);
        oc.Children.Add(Chk("Ưu tiên chồng mặt hàng sau lên khoảng trống phía trên vách cuối của mặt hàng trước", o.StackTop, v => { o.StackTop = v; Changed(delay: 1); }));
        oc.Children.Add(Chk("Quy tắc cao → thấp (trong → cửa): dãy phía trong không thấp hơn dãy phía ngoài", o.HeightRule, v => { o.HeightRule = v; Changed(delay: 1); }));
        oc.Children.Add(Chk("Hàng mẫu hải quan: tự dành các dãy cuối (sát cửa) trộn đủ mọi mặt hàng / date (≥ 10 thùng / dòng)", o.SampleOn, v => { o.SampleOn = v; Changed(delay: 1); }));
        var sortRow = new WrapPanel { ItemSpacing = 10 };
        var sort = new ComboBox { ItemsSource = new[] { "Mặt hàng › Size › Ngày (tự động)", "Đúng thứ tự nhập (thủ công)" }, SelectedIndex = o.SortMode == "manual" ? 1 : 0, Width = 260 };
        sort.SelectionChanged += (_, _) => { o.SortMode = sort.SelectedIndex == 1 ? "manual" : "auto"; Changed(delay: 1); };
        var dd = new ComboBox { ItemsSource = new[] { "Ngày cũ vào trong trước", "Ngày mới vào trong trước" }, SelectedIndex = o.DateDir == "desc" ? 1 : 0, Width = 220 };
        dd.SelectionChanged += (_, _) => { o.DateDir = dd.SelectedIndex == 1 ? "desc" : "asc"; Changed(delay: 1); };
        var a1 = new StackPanel(); a1.Children.Add(Muted("Thứ tự đóng")); a1.Children.Add(sort); sortRow.Children.Add(a1);
        var a2 = new StackPanel(); a2.Children.Add(Muted("Ngày (trong cùng size)")); a2.Children.Add(dd); sortRow.Children.Add(a2);
        oc.Children.Add(sortRow);
        _contPanel.Children.Add(Card(oc));
    }

    // ================================================================== BẢNG 3 — MÔ PHỎNG 3D & BÁO CÁO
    private Control BuildSim()
    {
        var views = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (k, t) in new[] { ("iso", "Tổng thể"), ("top", "Từ trên"), ("side", "Nhìn ngang"), ("door", "Từ cửa") }) views.Children.Add(Btn(t, () => _cv.SetPreset(k)));
        var tg = new WrapPanel { ItemSpacing = 10 };
        tg.Children.Add(new TextBlock { Text = "Vách:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        tg.Children.Add(Chk("Phải", _cv.WallRight, v => { _cv.WallRight = v; _cv.InvalidateVisual(); }));
        tg.Children.Add(Chk("Trái", _cv.WallLeft, v => { _cv.WallLeft = v; _cv.InvalidateVisual(); }));
        tg.Children.Add(Chk("Trần", _cv.WallCeil, v => { _cv.WallCeil = v; _cv.InvalidateVisual(); }));
        tg.Children.Add(Chk("Đầu", _cv.WallFront, v => { _cv.WallFront = v; _cv.InvalidateVisual(); }));
        tg.Children.Add(Chk("Red line", _cv.RedLine, v => { _cv.RedLine = v; _cv.InvalidateVisual(); }));
        tg.Children.Add(Chk("Kích thước", _cv.Dims, v => { _cv.Dims = v; _cv.InvalidateVisual(); }));
        tg.Children.Add(Chk("Nhãn hàng", _cv.GroupLabels, v => { _cv.GroupLabels = v; _cv.InvalidateVisual(); }));
        tg.Children.Add(Chk("Số dãy", _cv.RowLabels, v => { _cv.RowLabels = v; _cv.InvalidateVisual(); }));
        tg.Children.Add(Chk("Vùng chứa hàng", _cv.Usable, v => { _cv.Usable = v; _cv.InvalidateVisual(); }));
        var vbar = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(views, Dock.Left); vbar.Children.Add(views);
        var tgb = new Border { Padding = new Thickness(12, 0, 0, 0), Child = tg }; vbar.Children.Add(tgb);

        var seqBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        seqBar.Children.Add(new TextBlock { Text = "Thứ tự đóng (trong → ngoài)", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var bPlay = new Button { Content = "▶", Padding = new Thickness(8, 2) };
        seqBar.Children.Add(Btn("⏮", () => { _play.Stop(); bPlay.Content = "▶"; SetSeq(0); }));
        seqBar.Children.Add(Btn("◀", () => { _play.Stop(); bPlay.Content = "▶"; SetSeq(CurSeq() - 1); }));
        bPlay.Click += (_, _) => { if (_play.IsEnabled) { _play.Stop(); bPlay.Content = "▶"; return; } if (CurSeq() >= (_plan?.Boxes.Count ?? 0)) SetSeq(0); bPlay.Content = "⏸"; _play.Start(); };
        _playBtn = bPlay;
        seqBar.Children.Add(bPlay);
        seqBar.Children.Add(Btn("▶|", () => { _play.Stop(); bPlay.Content = "▶"; SetSeq(CurSeq() + 1); }));
        seqBar.Children.Add(Btn("⏭", () => { _play.Stop(); bPlay.Content = "▶"; SetSeq(int.MaxValue); }));
        seqBar.Children.Add(_seq); seqBar.Children.Add(_seqLab);

        var stage = new Grid();
        stage.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = _cv });
        stage.Children.Add(_sel); stage.Children.Add(_tip);
        var vp = new DockPanel(); DockPanel.SetDock(vbar, Dock.Top); vp.Children.Add(vbar); DockPanel.SetDock(seqBar, Dock.Bottom); vp.Children.Add(seqBar); vp.Children.Add(stage);

        var side = new ScrollViewer { Content = _layers, Width = 250, Padding = new Thickness(8, 0, 4, 0) };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 470 };
        top.Children.Add(vp); Grid.SetColumn(side, 1); top.Children.Add(side);

        var all = new StackPanel { Spacing = 8 };
        all.Children.Add(top); all.Children.Add(_report);
        return new ScrollViewer { Content = all, Padding = new Thickness(0, 0, 12, 0) };
    }

    private Button? _playBtn;

    private int CurSeq() => _cv.SeqMax ?? (_plan?.Boxes.Count ?? 0);

    private void SetSeq(int v, bool moveSlider = true)
    {
        var n = _plan?.Boxes.Count ?? 0; v = Math.Max(0, Math.Min(n, v));
        _cv.SeqMax = v >= n ? null : v; _cv.Refilter();
        if (moveSlider) { _busySeq = true; _seq.Value = v; _busySeq = false; }
        _seqLab.Text = $"{v} / {n}";
    }

    private void PlayTick()
    {
        var n = _plan?.Boxes.Count ?? 0; var c = CurSeq();
        if (c >= n) { _play.Stop(); if (_playBtn is not null) _playBtn.Content = "▶"; return; }
        SetSeq(c + Math.Max(1, (int)Math.Round(n / 160.0)));
    }

    // ------------------------------------------------------------ tính toán (chạy nền, kết quả cũ bị bỏ)
    private async void Calc()
    {
        _calcT.Stop();
        var ver = ++_calcVer;
        var cont = St.Cont!.Clone(); var skus = St.Skus.Select(k => k.Clone()).ToList(); var opts = St.Opts.Clone();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        C3dPlan plan;
        try { plan = await Task.Run(() => C3dEngine.Solve(cont, skus, opts)); }
        catch (Exception e) { if (ver == _calcVer) _status.Text = "Lỗi tính toán: " + e.Message; return; }
        if (ver != _calcVer) return;
        _ms = sw.ElapsedMilliseconds;
        _plan = plan; _hidT.Clear(); _hidG.Clear(); _hidR.Clear(); _selRow = null; _sel.IsVisible = false;
        _play.Stop(); if (_playBtn is not null) _playBtn.Content = "▶";
        _gcolors = Svc.GroupColors(plan);
        _cv.SetPlan(plan, _gcolors.Select(Color.Parse).ToList());
        _busySeq = true; _seq.Maximum = Math.Max(1, plan.Boxes.Count); _seq.Value = plan.Boxes.Count; _busySeq = false; _seqLab.Text = $"{plan.Boxes.Count} / {plan.Boxes.Count}";
        RenderReport(); RenderLayers(); UpdCtx();
    }

    private void ApplyColors()
    {
        if (_plan is null) return; _gcolors = Svc.GroupColors(_plan); _cv.SetColors(_gcolors.Select(Color.Parse).ToList()); RenderLayers(); RenderReport();
    }

    private void ApplyFilter()
    {
        var p = _plan; if (p is null) return;
        _cv.Tiers = _hidT.Count > 0 ? Enumerable.Range(1, p.MaxTier).Where(t => !_hidT.Contains(t)).ToHashSet() : null;
        _cv.Groups = _hidG.Count > 0 ? Enumerable.Range(0, p.Groups.Count).Where(g => !_hidG.Contains(g)).ToHashSet() : null;
        _cv.Rows = _hidR.Count > 0 ? p.Rows.Select(r => r.Row).Where(r => !_hidR.Contains(r)).ToHashSet() : null;
        _cv.SelRow = _selRow;
        _cv.Refilter();
    }

    // ------------------------------------------------------------ lớp / mặt hàng / dãy
    private void RenderLayers()
    {
        _layers.Children.Clear(); var p = _plan; if (p is null || p.Boxes.Count == 0) { _layers.Children.Add(Muted("Chưa có thùng nào được xếp.")); return; }
        StackPanel Head(string t, Action all, Action none)
        {
            var h = new DockPanel { Margin = new Thickness(0, 6, 0, 2) };
            var b = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            b.Children.Add(new Button { Content = "Tất cả", FontSize = 11, Padding = new Thickness(5, 1) }); ((Button)b.Children[0]).Click += (_, _) => { all(); RenderLayers(); ApplyFilter(); };
            b.Children.Add(new Button { Content = "Ẩn hết", FontSize = 11, Padding = new Thickness(5, 1) }); ((Button)b.Children[1]).Click += (_, _) => { none(); RenderLayers(); ApplyFilter(); };
            DockPanel.SetDock(b, Dock.Right); h.Children.Add(b); h.Children.Add(new TextBlock { Text = t, FontWeight = FontWeight.Bold, FontSize = 12.5, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center });
            var s = new StackPanel(); s.Children.Add(h); return s;
        }
        var gh = Head("Mặt hàng", () => _hidG.Clear(), () => { foreach (var g in p.Groups) _hidG.Add(g.Index); });
        foreach (var g in p.Groups.Where(g => g.Loaded > 0))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var cb = new CheckBox { IsChecked = !_hidG.Contains(g.Index), FontSize = 12 };
            var gi = g.Index; cb.IsCheckedChanged += (_, _) => { if (cb.IsChecked == true) _hidG.Remove(gi); else _hidG.Add(gi); ApplyFilter(); };
            row.Children.Add(cb);
            row.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = AppTheme.Brush(gi < _gcolors.Count ? _gcolors[gi] : "#888"), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = (g.Sku.Code.Length > 0 ? g.Sku.Code : "#" + (gi + 1)) + " " + g.Sku.Size + (g.Sku.Date.Length > 0 ? " · " + g.Sku.Date : "") + $" ({Fmt(g.Loaded)})", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190 });
            gh.Children.Add(row);
        }
        _layers.Children.Add(gh);
        var th = Head("Lớp hàng (tầng)", () => _hidT.Clear(), () => { for (var t = 1; t <= p.MaxTier; t++) _hidT.Add(t); });
        var cnt = p.Boxes.GroupBy(b => b.Tier).ToDictionary(x => x.Key, x => x.Count());
        var tw = new WrapPanel();
        for (var t = 1; t <= p.MaxTier; t++)
        {
            var tt = t; var cb = new CheckBox { Content = $"Lớp {t} ({(cnt.TryGetValue(t, out var n) ? n : 0)})", IsChecked = !_hidT.Contains(t), FontSize = 12, Width = 115 };
            cb.IsCheckedChanged += (_, _) => { if (cb.IsChecked == true) _hidT.Remove(tt); else _hidT.Add(tt); ApplyFilter(); };
            tw.Children.Add(cb);
        }
        th.Children.Add(tw); _layers.Children.Add(th);
        var rh = Head("Dãy (vách → cửa)", () => _hidR.Clear(), () => { foreach (var r in p.Rows) _hidR.Add(r.Row); });
        rh.Children.Add(RowInspector(p));
        foreach (var r in p.Rows)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var rr = r.Row; var cb = new CheckBox { IsChecked = !_hidR.Contains(rr) };
            cb.IsCheckedChanged += (_, _) => { if (cb.IsChecked == true) _hidR.Remove(rr); else _hidR.Add(rr); ApplyFilter(); };
            var lb = new Button { Content = $"Dãy {r.Row}", FontSize = 11.5, Padding = new Thickness(5, 1), FontWeight = _selRow == rr ? FontWeight.Bold : FontWeight.Normal };
            lb.Click += (_, _) => { _selRow = _selRow == rr ? null : rr; RenderLayers(); ApplyFilter(); };
            row.Children.Add(cb); row.Children.Add(lb);
            row.Children.Add(new TextBlock { Text = $"{Fmt(r.N)} thùng" + (r.Samples > 0 ? $" · {r.Samples} mẫu HQ" : "") + $" · cao {Fmt(r.Top)}", FontSize = 11.5, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
            rh.Children.Add(row);
        }
        _layers.Children.Add(rh);
    }

    // Bảng chỉnh HƯỚNG cho 1 dãy
    private Control RowInspector(C3dPlan p)
    {
        var r = _selRow is { } sr ? p.Rows.FirstOrDefault(x => x.Row == sr) : null;
        if (r is null) return Muted("Bấm “Dãy n” (hoặc 1 thùng trong hình) để chọn dãy: xem khoảng trống còn lại và chỉnh hướng riêng cho dãy đó.");
        var box = new StackPanel { Spacing = 4, Margin = new Thickness(0, 2, 0, 6) };
        box.Children.Add(new TextBlock { Text = $"Dãy {r.Row}", FontWeight = FontWeight.Bold });
        box.Children.Add(Muted($"Còn trống: trần {Fmt(r.Gaps.Ceiling)} · red line {Fmt(r.Gaps.Red)} · vách phải {Fmt(r.Gaps.Right)} · vách trái {Fmt(r.Gaps.Left)} mm"));
        if (r.Wall < 0 || r.Group < 0) { box.Children.Add(Muted("Dãy này gồm nhiều mặt hàng ghép / hàng mẫu — không chỉnh hướng riêng được.")); return new Border { Background = AppTheme.Brush("#F4F7FB"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Child = box }; }
        var sk = St.Skus.FirstOrDefault(x => x.Id == p.Groups[r.Group].Sku.Id) ?? p.Groups[r.Group].Sku;
        var cur = sk.RowSet.TryGetValue(r.Wall.ToString(CultureInfo.InvariantCulture), out var v) ? v : "";
        var opts = new List<(string v, string t)> { ("", "Theo dãy mẫu / tự động"), ("nam", "Cả dãy NẰM"), ("dung", "Cả dãy ĐỨNG"), ("cao", "Cả dãy DỰNG CAO") };
        opts.AddRange(C3dEngine.Perms.Select(q => (q, OriLabel(q, sk))));
        box.Children.Add(Muted($"{sk.Code} {sk.Size} · dãy thứ {r.Wall + 1} của mặt hàng"));
        var cb = new ComboBox { ItemsSource = opts.Select(o => o.t).ToList(), SelectedIndex = Math.Max(0, opts.FindIndex(o => o.v == cur)), Width = 230, FontSize = 12 };
        var tx = new TextBox { Text = System.Text.RegularExpressions.Regex.IsMatch(cur, @"\d") ? cur : "", Watermark = "hoặc mẫu riêng: 6 nằm 2 đứng", Width = 230, FontSize = 12 };
        box.Children.Add(cb); box.Children.Add(tx);
        var bs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        bs.Children.Add(Btn("Áp dụng", () =>
        {
            var val = (tx.Text ?? "").Trim(); if (val.Length == 0) val = opts[Math.Max(0, cb.SelectedIndex)].v;
            var key = r.Wall.ToString(CultureInfo.InvariantCulture);
            if (val.Length > 0) sk.RowSet[key] = val; else sk.RowSet.Remove(key);
            RenderGoods(); Changed(delay: 1); _status.Text = val.Length > 0 ? $"Đã đặt dãy {r.Row}: {val} — các dãy khác tự sắp lại." : $"Dãy {r.Row} về tự động.";
        }));
        bs.Children.Add(Btn("Về tự động", () => { sk.RowSet.Remove(r.Wall.ToString(CultureInfo.InvariantCulture)); RenderGoods(); Changed(delay: 1); }));
        box.Children.Add(bs);
        return new Border { Background = AppTheme.Brush("#F4F7FB"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Child = box };
    }

    // ------------------------------------------------------------ rê chuột / chọn thùng
    private string BoxText(int bi, bool full)
    {
        var p = _plan!; var b = p.Boxes[bi]; var g = p.Groups[b.Group]; var sk = g.Sku;
        var head = $"#{b.Seq} · {(sk.Code.Length > 0 ? sk.Code : "Mặt hàng")} {sk.Size}" + (sk.Date.Length > 0 ? " · " + sk.Date : "") + $" · {g.Loaded} thùng";
        var dims = $"{Math.Round(b.Dx)}×{Math.Round(b.Dy)}×{Math.Round(b.Dz)} mm (dài×ngang×cao khi đặt)";
        if (!full) return head + $"\nDãy {b.Row} · Lớp {b.Tier} · {dims}";
        return head + $"\nDãy {b.Row}" + (b.Sample ? "\n🛃 Thùng mẫu hải quan" : "") + (b.OnPrev ? "\n↥ chồng lên hàng trước" : "") + $"\nLớp (tầng) {b.Tier} · {C3dEngine.VName(b.Dz, sk)}" + (b.Rot != 0 ? " · xoay 90°" : "")
            + $"\n{dims}\nCách vách đầu {Fmt(b.X - p.Space.X0)} · vách phải {Fmt(b.Y - p.Space.Y0)} · cao từ sàn {Fmt(b.Z)} mm";
    }

    private void OnHover(int? bi, Point pt)
    {
        if (bi is null || _plan is null) { _tip.IsVisible = false; return; }
        _tip.Child = new TextBlock { Text = BoxText(bi.Value, false), Foreground = Brushes.White, FontSize = 12 };
        _tip.Margin = new Thickness(Math.Min(pt.X + 14, Math.Max(0, _cv.Bounds.Width - 320)), Math.Min(pt.Y + 14, Math.Max(0, _cv.Bounds.Height - 60)), 0, 0);
        _tip.IsVisible = true;
    }

    private void OnPick(int? bi)
    {
        if (bi is null || _plan is null) { _sel.IsVisible = false; return; }
        var b = _plan.Boxes[bi.Value]; var sk0 = _plan.Groups[b.Group].Sku; var sk = St.Skus.FirstOrDefault(x => x.Id == sk0.Id);
        _selRow = b.Row; RenderLayers(); _cv.SelRow = b.Row; _cv.InvalidateVisual();
        var s = new StackPanel { Spacing = 4 };
        var x = new Button { Content = "✕", Padding = new Thickness(6, 0), HorizontalAlignment = HorizontalAlignment.Right }; x.Click += (_, _) => { _sel.IsVisible = false; _cv.Selected = null; _cv.InvalidateVisual(); };
        s.Children.Add(x);
        s.Children.Add(new TextBlock { Text = BoxText(bi.Value, true), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        if (sk is not null)
        {
            s.Children.Add(Muted($"Xoay cả nhóm “{sk.Code} {sk.Size}”:"));
            var cur = sk.Orient.Length > 0 ? sk.Orient : C3dEngine.Perms.FirstOrDefault(pm => { double D(char c) => c == 'L' ? sk.L : c == 'W' ? sk.W : sk.H; return Math.Abs(D(pm[0]) - b.Dx) < 0.01 && Math.Abs(D(pm[1]) - b.Dy) < 0.01 && Math.Abs(D(pm[2]) - b.Dz) < 0.01; }) ?? "";
            var items = new List<string> { "Tự động" }; items.AddRange(C3dEngine.Perms.Select(pm => OriLabel(pm, sk)));
            var cb = new ComboBox { ItemsSource = items, SelectedIndex = sk.Orient.Length > 0 ? Array.IndexOf(C3dEngine.Perms, sk.Orient) + 1 : 0, Width = 300, FontSize = 12 };
            cb.SelectionChanged += (_, _) => { sk.Orient = cb.SelectedIndex > 0 ? C3dEngine.Perms[cb.SelectedIndex - 1] : ""; RenderGoods(); Changed(delay: 1); };
            s.Children.Add(cb);
            s.Children.Add(Btn("Xoay 90° trên sàn", () => { var p0 = cur.Length == 3 ? cur : "LWH"; sk.Orient = $"{p0[1]}{p0[0]}{p0[2]}"; RenderGoods(); Changed(delay: 1); }));
        }
        _sel.Child = s; _sel.IsVisible = true;
    }

    // ------------------------------------------------------------ báo cáo
    private void RenderReport()
    {
        _report.Children.Clear(); var p = _plan; if (p is null) return; var t = p.Totals;
        if (p.Groups.Count == 0 || t.Requested == 0) { _report.Children.Add(Card(Muted("Chưa có hàng để xếp. Thêm mặt hàng ở bảng “1 Hàng hóa” (hoặc bấm “Ví dụ mẫu”)."))); return; }
        Border Kpi(string k, string v, string s, IBrush? fg = null, double? pct = null)
        {
            var sp = new StackPanel { Spacing = 1 };
            sp.Children.Add(new TextBlock { Text = k, FontSize = 11.5, Classes = { "muted" } });
            sp.Children.Add(new TextBlock { Text = v, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = fg ?? Dark });
            if (s.Length > 0) sp.Children.Add(new TextBlock { Text = s, FontSize = 11.5, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
            if (pct is { } pc) sp.Children.Add(new ProgressBar { Value = Math.Min(100, pc), Maximum = 100, Height = 6, MinHeight = 6, Margin = new Thickness(0, 3, 0, 0), Foreground = pc > 100 ? Bad : AppTheme.Brush("#2563EB") });
            return new Border { Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6), Width = 230, Child = sp };
        }
        var pay = t.Payload > 0 ? t.Kg / t.Payload * 100 : 0;
        var kp = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        kp.Children.Add(Kpi("Đã xếp / yêu cầu", $"{Fmt(t.Loaded)} / {Fmt(t.Requested)}", t.Left > 0 ? $"rớt kho {Fmt(t.Left)} thùng" : "đủ hàng", t.Left > 0 ? Bad : Good));
        kp.Children.Add(Kpi("Khối lượng hàng", Fmt(t.Kg, 2) + " kg", t.Payload > 0 ? $"{Fmt(pay, 1)}% tải trọng ({Fmt(t.Payload)} kg)" : "chưa nhập tải trọng", null, t.Payload > 0 ? pay : null));
        kp.Children.Add(Kpi("Khối hàng đã xếp", Fmt(t.VolBox / 1e9, 2) + " m³", $"lấp {Fmt(t.FillUsablePct, 1)}% vùng chứa hàng", null, t.FillUsablePct));
        kp.Children.Add(Kpi("Chiều dài đã dùng", Fmt(t.UsedLength / 1000, 2) + " m", $"còn trống {Fmt(t.FreeLength / 1000, 2)} m trước cửa"));
        kp.Children.Add(Kpi("Số dãy · tầng cao nhất", $"{t.Rows} dãy · {p.MaxTier} tầng", t.Sample > 0 ? $"{Fmt(t.Sample)} thùng mẫu hải quan" : "không đánh dấu mẫu"));
        var head = new TextBlock { Text = $"Báo cáo · {p.Container.Name} · tính trong {_ms} ms", FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Dark };
        _report.Children.Add(head); _report.Children.Add(kp);
        void Msg(string text, string bg, IBrush fg) => _report.Children.Add(new Border { Background = AppTheme.Brush(bg), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), Child = new TextBlock { Text = text, Foreground = fg, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 } });
        foreach (var m in p.Errors) Msg("⛔ " + m, "#FDECEA", Bad);
        if (t.Left > 0) Msg($"📦 Rớt lại kho {Fmt(t.Left)} thùng — " + string.Join(" · ", p.Groups.Where(g => g.Left > 0).Select(g => (g.Sku.Code.Length > 0 ? g.Sku.Code : "Mặt hàng") + (g.Sku.Size.Length > 0 ? " " + g.Sku.Size : "") + (g.Sku.Date.Length > 0 ? $" ({g.Sku.Date})" : "") + $": {Fmt(g.Left)} thùng")) + ".", "#FDECEA", Bad);
        if (!p.Verify.Ok) Msg("Chi tiết lỗi kiểm tra:\n" + string.Join("\n", p.Verify.Violations.Take(5).Select(v => v.Msg)), "#FDECEA", Bad);
        foreach (var m in p.Warnings) Msg("⚠ " + m, "#FFF4CC", WarnFg);
        for (var i = 0; i < Math.Min(3, p.Hints.Count); i++)
        {
            var h = p.Hints[i];
            var row = new DockPanel();
            var ap = Btn("Áp dụng", () => ApplyHint(h)); DockPanel.SetDock(ap, Dock.Right); row.Children.Add(ap);
            row.Children.Add(new TextBlock { Text = "💡 " + h.Text + "\n→ " + (h.Gain > 0 ? $"đóng thêm được +{Fmt(h.Gain)} thùng ({Fmt(h.Loaded)} / {Fmt(t.Requested)}, còn rớt {Fmt(h.Left)})" : h.Note), TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
            _report.Children.Add(new Border { Background = AppTheme.Brush("#E8F1FB"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), Child = row });
        }
        if (p.Verify.Ok && t.Loaded > 0) Msg($"✔ Kiểm tra độc lập ĐẠT: không chồng lấn · nằm trong cont, dưới red line · mỗi thùng được đỡ ≥ {Math.Round(p.Options.MinSupport * 100)}% diện tích bên dưới · thùng đỡ đóng trước · không thùng nào bị chắn đường từ cửa.", "#E8F5E9", Good);
        if (p.SpreadNote.Length > 0) Msg("🧱 " + p.SpreadNote, "#F3F4F6", Dark);
        if (p.LayoutNote.Length > 0) Msg("🌬 " + p.LayoutNote, "#F3F4F6", Dark);
        if (p.Compare is { Ok: true } cmp)
        {
            var other = cmp.MergeTail ? "“Ghép lấp chỗ trống cạnh vách cuối”" : "“Tách riêng từng mặt hàng”";
            if (cmp.Delta > 0) Msg($"💡 Nếu chọn {other} sẽ xếp được {Fmt(cmp.Loaded)} thùng (+{Fmt(cmp.Delta)}).", "#F3F4F6", Dark);
            else if (cmp.Delta == 0 && t.Left == 0 && cmp.UsedLength < t.UsedLength - 100) Msg($"💡 Chọn {other} gọn hơn {Fmt((t.UsedLength - cmp.UsedLength) / 1000, 2)} m chiều dài.", "#F3F4F6", Dark);
        }
        if (t.Gaps is { } g0) _report.Children.Add(Muted($"Khoảng trống còn lại (mm): → vách đầu {Fmt(g0.Front)} · → cửa {Fmt(g0.Door)} · → vách phải {Fmt(g0.Right)} · → vách trái {Fmt(g0.Left)} · → trần {Fmt(g0.Ceiling)} · → red line {Fmt(g0.Red)}"
            + (t.Cog is { } cg ? $" · trọng tâm dọc {Math.Round(cg.XPct)}% / ngang {Math.Round(cg.YPct)}%" : "")));
        // bảng theo mặt hàng
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("40,2*,70,80,70,3*,80,2*"), Margin = new Thickness(0, 6, 0, 0) };
        var r = 0;
        void Cell(int row, int col, string text, bool bold = false, IBrush? fg = null, bool right = false)
        {
            var tb = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, Margin = new Thickness(4, 3), TextAlignment = right ? TextAlignment.Right : TextAlignment.Left };
            if (fg is not null) tb.Foreground = fg;
            var bd = new Border { BorderBrush = Line_, BorderThickness = new Thickness(0, 0, 0, 1), Child = tb, Background = row == 0 ? AppTheme.Brush("#EEF2F7") : null };
            Grid.SetRow(bd, row); Grid.SetColumn(bd, col); grid.Children.Add(bd);
        }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] hd = { "STT", "Mặt hàng · size · date", "Yêu cầu", "Đã xếp", "Rớt kho", "Hướng đặt / kiểu xếp", "KG", "Ghi chú / lý do" };
        for (var c = 0; c < hd.Length; c++) Cell(0, c, hd[c], true, null, c is 2 or 3 or 4 or 6);
        foreach (var g in p.Groups)
        {
            r++; grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); var sk = g.Sku;
            var tiers = g.Loaded > 0 ? p.Boxes.Where(b => b.Group == g.Index).Max(b => b.Tier) : 0;
            Cell(r, 0, (g.Index + 1).ToString(CultureInfo.InvariantCulture));
            Cell(r, 1, (sk.Code.Length > 0 ? sk.Code : "(chưa có mã)") + (sk.Name.Length > 0 ? " — " + sk.Name : "") + "\n" + string.Join(" · ", new[] { sk.Size, sk.Date }.Where(x => x.Length > 0)) + (g.XStart is { } xs ? $"\n{Fmt((xs - p.Space.X0) / 1000, 2)} → {Fmt((g.XEnd!.Value - p.Space.X0) / 1000, 2)} m" : ""), true, AppTheme.Brush(g.Index < _gcolors.Count ? _gcolors[g.Index] : "#333"));
            Cell(r, 2, Fmt(g.Requested), false, null, true);
            Cell(r, 3, Fmt(g.Loaded) + (g.Sample > 0 ? $"\ngồm {Fmt(g.Sample)} mẫu HQ" : ""), false, null, true);
            Cell(r, 4, Fmt(g.Left), g.Left > 0, g.Left > 0 ? Bad : null, true);
            Cell(r, 5, string.Join("\n", g.Patterns.Select(x => x.Text)) + (tiers > 0 ? $"\ntối đa {tiers} tầng" : ""));
            Cell(r, 6, Fmt(g.Kg, 2), false, null, true);
            Cell(r, 7, (sk.Note.Length > 0 ? "📝 " + sk.Note + "\n" : "") + string.Join("\n", g.Reasons), false, g.Left > 0 ? Bad : null);
        }
        r++; grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell(r, 1, "Tổng cộng", true); Cell(r, 2, Fmt(t.Requested), true, null, true); Cell(r, 3, Fmt(t.Loaded), true, null, true); Cell(r, 4, Fmt(t.Left), true, t.Left > 0 ? Bad : null, true); Cell(r, 6, Fmt(t.Kg, 2), true, null, true);
        _report.Children.Add(Card(grid));
    }

    private void ApplyHint(C3dHint h)
    {
        foreach (var id in h.SkuIds) if (St.Skus.FirstOrDefault(k => k.Id == id) is { } k) C3dEngine.ApplyLayPatch(k);
        if (h.GapZero) { St.Cont!.ClearFront = 0; St.Cont.ClearRear = 0; }
        if (h.OptSpread is { } s) St.Opts.Spread = s;
        if (h.OptHeightRule is { } hr) St.Opts.HeightRule = hr;
        RenderGoods(); RenderCont(); Changed(delay: 1); _status.Text = "Đã áp dụng gợi ý: " + h.Text;
    }

    // ------------------------------------------------------------ phương án / xuất file
    private async Task SavePlanDialog()
    {
        if (_plan is null || _plan.Totals.Requested == 0) { await Ui.Message(this, "Chưa có phương án để lưu."); return; }
        var auto = _openedAuto is null ? null : Svc.Plans.FirstOrDefault(x => x.Id == _openedAuto && x.IsAuto);
        var def = auto?.Name ?? $"{St.Cont!.Name} {DateTime.Now:dd/MM HH:mm}";
        var name = new TextBox { Text = def }; var over = new CheckBox { Content = "Ghi đè phương án tự động đang mở và khóa lại (lịch xuất không tính đè nữa)", IsChecked = true, IsVisible = auto is not null };
        var ok = new Button { Content = "Lưu", Classes = { "primary" }, IsDefault = true }; var no = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = "Tên phương án" }); body.Children.Add(name); body.Children.Add(over);
        body.Children.Add(Muted("Lưu container, danh sách hàng và tuỳ chọn; kết quả được tính lại khi mở (cùng dữ liệu → cùng phương án)."));
        body.Children.Add(Ui.Buttons(no, ok));
        var w = Ui.Dialog("Lưu phương án xếp hàng", body, 520);
        var go = false; ok.Click += (_, _) => { go = true; w.Close(); }; no.Click += (_, _) => w.Close();
        w.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        await Ui.Show(this, w);
        if (!go) return;
        var nm = (name.Text ?? "").Trim(); if (nm.Length == 0) nm = def;
        await Ui.Run(this, () => { var p = Svc.SavePlan(nm, _plan, auto is not null && over.IsChecked == true ? auto.Id : null); _status.Text = $"Đã lưu phương án “{p.Name}”."; });
    }

    private async Task OpenPlans()
    {
        var list = new StackPanel { Spacing = 4 };
        var w = Ui.Dialog("Phương án đã lưu", new StackPanel(), 760);
        void Paint()
        {
            list.Children.Clear();
            if (Svc.Plans.Count == 0) { list.Children.Add(Muted("Chưa có phương án nào. Tính xong bấm “Lưu phương án”.")); return; }
            foreach (var pl in Svc.Plans)
            {
                var row = new DockPanel();
                var bs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                bs.Children.Add(Btn("Mở", () => { Svc.OpenPlan(pl); _openedAuto = pl.IsAuto ? pl.Id : null; w.Close(); RenderGoods(); RenderCont(); Calc(); _tabs.SelectedIndex = 2; _status.Text = $"Đã mở phương án “{pl.Name}”."; }));
                if (Svc.CanEdit && pl.IsAuto) bs.Children.Add(Btn(pl.Locked ? "Mở khóa" : "Khóa", async () => { if (await Ui.Run(this, () => Svc.ToggleLock(pl.Id))) Paint(); }));
                if (Svc.CanEdit) bs.Children.Add(Btn("Xóa", async () => { if (!await Ui.Confirm(this, $"Xóa phương án “{pl.Name}”?", yes: "Xóa")) return; if (await Ui.Run(this, () => Svc.DeletePlan(pl.Id))) Paint(); }));
                DockPanel.SetDock(bs, Dock.Right); row.Children.Add(bs);
                var t = new StackPanel();
                t.Children.Add(new TextBlock { Text = (pl.IsAuto ? (pl.Locked ? "🔒 " : "⚙ ") : "") + pl.Name, FontWeight = FontWeight.SemiBold });
                var when = DateTime.TryParse(pl.CapNhat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : pl.CapNhat;
                t.Children.Add(Muted($"{pl.ContName} · {Fmt(pl.Loaded)} / {Fmt(pl.Requested)} thùng · {when}"));
                row.Children.Add(t);
                list.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 4), Child = row });
            }
        }
        var close = new Button { Content = "Đóng", IsCancel = true }; close.Click += (_, _) => w.Close();
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 480 }); body.Children.Add(Ui.Buttons(close));
        ((Border)w.Content!).Child = body;
        Paint();
        await Ui.Show(this, w);
    }

    private async Task ExportExcel()
    {
        if (_plan is null || _plan.Totals.Requested == 0) { await Ui.Message(this, "Chưa có phương án để xuất."); return; }
        var path = await Ui.SaveFile(this, "Xuất Excel phương án xếp hàng", $"XepContainer_{C3dService.SafeName(St.Cont!.Name)}_{DateTime.Now:yyyy-MM-dd}.xlsx", "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => C3dService.BuildExcel(_plan).Save(path))) Ui.Exported(this, path);
    }

    private async Task ExportPng()
    {
        if (_plan is null) return;
        var path = await Ui.SaveFile(this, "Lưu ảnh 3D", $"Container3D_{C3dService.SafeName(St.Cont!.Name)}_{DateTime.Now:yyyy-MM-dd}.png", "png", "Ảnh PNG");
        if (path is null) return;
        await Ui.Run(this, () =>
        {
            var sz = _cv.Bounds.Size; var bmp = new RenderTargetBitmap(new PixelSize((int)Math.Max(10, sz.Width * 2), (int)Math.Max(10, sz.Height * 2)), new Vector(192, 192));
            bmp.Render(_cv); bmp.Save(path); _status.Text = "Đã lưu ảnh 3D: " + path;
        });
    }

    // ------------------------------------------------------------ Tính CBM / Pallet
    private async Task OpenCbm()
    {
        var body = new StackPanel { Spacing = 8 };
        var res = new StackPanel { Spacing = 6 };
        var inputs = new WrapPanel { ItemSpacing = 10, LineSpacing = 6 };
        void Paint()
        {
            res.Children.Clear();
            var s = C3dCbm.Summarize(St.Skus, St.Cont!.Normalized(), _pal);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,60,110,80,80,80,100,80,70,70") };
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            void Cell(int r, int c, string t, bool bold = false)
            {
                var tb = new TextBlock { Text = t, FontSize = 12, Margin = new Thickness(3, 2), FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, TextAlignment = c == 0 ? TextAlignment.Left : TextAlignment.Right };
                Grid.SetRow(tb, r); Grid.SetColumn(tb, c); g.Children.Add(tb);
            }
            string[] hd = { "Mặt hàng", "SL", $"Thùng ({St.Unit})", "CBM/thùng", "Tổng CBM", "Tổng kg", "Thùng/lớp × lớp", "Thùng/pallet", "Số pallet", $"Cao ({St.Unit})" };
            for (var c = 0; c < hd.Length; c++) Cell(0, c, hd[c], true);
            var r = 0;
            foreach (var (k, pl) in s.Items)
            {
                r++; g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Cell(r, 0, (k.Code.Length > 0 ? k.Code : "—") + (k.Size.Length > 0 ? " " + k.Size : ""), true); Cell(r, 1, Fmt(k.Qty)); Cell(r, 2, $"{ToU(k.L)}×{ToU(k.W)}×{ToU(k.H)}");
                Cell(r, 3, Fmt(C3dCbm.CbmBox(k), 4)); Cell(r, 4, Fmt(C3dCbm.CbmBox(k) * k.Qty, 3)); Cell(r, 5, Fmt(k.Kg * k.Qty, 1));
                if (pl.Ok) { Cell(r, 6, $"{pl.PerLayer} × {pl.Layers}"); Cell(r, 7, Fmt(pl.PerPallet) + (pl.Limited == "kg" ? " (tải)" : ""), true); Cell(r, 8, Fmt(pl.Pallets), true); Cell(r, 9, ToU(pl.Height)); }
                else Cell(r, 6, pl.Why);
            }
            res.Children.Add(new ScrollViewer { Content = g, MaxHeight = 280, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            var f = s.Floor; var ok = s.PalletsFit && s.Bad == 0;
            res.Children.Add(new TextBlock { Text = $"{Fmt(s.CbmTotal, 2)} m³ hàng / {Fmt(s.ContVol, 1)} m³ cont (tới red line) = {Fmt(s.FillPct, 1)}%   ·   {Fmt(s.Kg)} kg" + (St.Cont!.MaxPayload > 0 ? (s.KgOver ? $" — ⚠ VƯỢT tải trọng {Fmt(St.Cont.MaxPayload)} kg" : $" / tải trọng {Fmt(St.Cont.MaxPayload)} kg") : ""), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            res.Children.Add(new TextBlock { Text = $"Cần {Fmt(s.TotalPallets)} pallet · cont chứa được {f.FloorCount} pallet trên sàn × {f.Tiers} tầng = {f.Total} pallet → " + (ok ? "VỪA ✔" : "KHÔNG VỪA ✖"), Foreground = ok ? Good : Bad, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
            res.Children.Add(Muted("Số pallet trên sàn là mức tối đa lý thuyết khi xếp pallet thẳng sát nhau; xếp thực tế (thùng lẻ, cửa, khe thông gió) có thể ít hơn. Kết quả để ước tính — dùng “Mô phỏng 3D” để chốt phương án."));
        }
        void Num(string label, double v, Action<double> set) => Field(inputs, label, C3dEngine.Js(v), t => { set(Math.Max(0, C3dJson.Num(t))); Paint(); }, 100);
        var pre = new ComboBox { ItemsSource = new[] { "Mẫu pallet…", "1200 × 1000 (ISO)", "1200 × 800 (EUR)", "1100 × 1100", "1219 × 1016 (Mỹ)" }, SelectedIndex = 0, Width = 170 };
        Num("Pallet dài (mm)", _pal.L, v => _pal.L = v > 0 ? v : 1200); Num("Pallet rộng (mm)", _pal.W, v => _pal.W = v > 0 ? v : 1000);
        Num("Cao pallet (mm)", _pal.Base, v => _pal.Base = v); Num("Cao tối đa gồm pallet (mm)", _pal.MaxH, v => _pal.MaxH = v > 0 ? v : 1800); Num("Tải pallet (kg)", _pal.MaxKg, v => _pal.MaxKg = v);
        var ps = new StackPanel(); ps.Children.Add(Muted("Mẫu")); ps.Children.Add(pre); inputs.Children.Add(ps);
        body.Children.Add(Muted($"Lấy từ danh sách hàng ở bảng 1 và loại container đang chọn ({St.Cont!.Name}). Mỗi mặt hàng xếp pallet riêng, không trộn."));
        body.Children.Add(inputs); body.Children.Add(res);
        var close = new Button { Content = "Đóng", IsCancel = true, IsDefault = true };
        body.Children.Add(Ui.Buttons(close));
        var w = Ui.Dialog("Tính CBM / Pallet", body, 900);
        close.Click += (_, _) => w.Close();
        pre.SelectionChanged += (_, _) =>
        {
            var dims = new[] { (0.0, 0.0), (1200, 1000), (1200, 800), (1100, 1100), (1219, 1016) }[Math.Max(0, pre.SelectedIndex)];
            if (dims.Item1 <= 0) return; _pal.L = dims.Item1; _pal.W = dims.Item2;
            ((TextBox)((StackPanel)inputs.Children[0]).Children[1]).Text = C3dEngine.Js(_pal.L); ((TextBox)((StackPanel)inputs.Children[1]).Children[1]).Text = C3dEngine.Js(_pal.W);
        };
        Paint();
        await Ui.Show(this, w);
    }
}

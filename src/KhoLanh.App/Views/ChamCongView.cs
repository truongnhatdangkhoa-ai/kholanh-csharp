using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.ChamCong;
using KhoLanh.Core.Grid;

namespace KhoLanh.App.Views;

/// <summary>
/// CHẤM CÔNG (1.129, quyền m24cc, Chức năng): nạp file "Chi tiết chấm công" của máy chấm công → tính giờ theo quy tắc kho
/// (làm tròn giờ vào / ra, nghỉ trưa theo bộ phận, V.Sớm, nghỉ tối khi làm ≥ 15 tiếng) → bảng công tháng; sửa tay từng ngày,
/// người không có mã máy chép giờ người khác; xuất Excel (Bảng công + Chi tiết).
/// Tab: Bảng công · Chi tiết ngày · Nhân viên · Cấu hình.
/// </summary>
public sealed class ChamCongView : UserControl, IPage
{
    public string Title => "Chấm công";
    public string Subtitle => "Nạp file máy chấm công, tự tính giờ theo quy tắc kho (làm tròn, nghỉ trưa theo bộ phận), sửa tay từng ngày, bảng công tháng + xuất Excel.";

    private static ChamCongService Svc => App.Services.ChamCong;
    private static readonly IBrush WarnBg = AppTheme.Brush("#FFF2D6"), EditBg = AppTheme.Brush("#EEEBFC"), SunBg = AppTheme.Brush("#F3F5F8"),
        EditCol = AppTheme.Brush("#FBFCFD"), Sun = AppTheme.Brush("#B42318"), NhanBg = AppTheme.Brush("#E3F4EA"), ChoBg = AppTheme.Brush("#E6F0FC");

    private readonly ComboBox _thang = new() { MinWidth = 150 };
    private readonly TextBlock _st = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    internal readonly TabControl _tabs = new();
    private ChamCongService.Thang? _t;
    private List<string> _thangs = new();
    private bool _loading;

    // Bảng công
    private readonly GridModel _bm = new() { ReadOnly = true };
    private readonly SheetGrid _bg;
    private readonly ComboBox _bView = new() { ItemsSource = new[] { "Giờ sau sang công", "Giờ chốt (trước sang công)" }, SelectedIndex = 0, MinWidth = 210 };
    private readonly CheckBox _buTruoc = new() { VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private bool SauSangView => _bView.SelectedIndex != 1;
    // Chi tiết
    internal readonly GridModel _cm = new();
    internal readonly SheetGrid _cg;
    private readonly ComboBox _cNv = new() { MinWidth = 220 };
    private readonly CheckBox _cOnly = new() { Content = "Chỉ ngày cần xem / đã sửa", VerticalAlignment = VerticalAlignment.Center };
    private List<CcDong> _cRows = new();
    private readonly DispatcherTimer _cSave = new() { Interval = TimeSpan.FromMilliseconds(700) };
    // Nhân viên
    private readonly GridModel _nm = new();
    private readonly SheetGrid _ng;
    // Cấu hình
    private readonly TextBox _pb = new() { Width = 260 }, _vTron = Num(), _vNua = Num(), _rTron = Num(), _rNua = Num(), _nua = Num(), _toiTu = Num(), _toi = Num(), _som = Num(), _chuan = Num();
    private readonly CheckBox _sang = new() { Content = "Tự sang công: giờ trên 1 công bù cho ngày thiếu trong tháng (ngày đi làm thiếu trước, rồi ngày nghỉ; ngày gần nhau nhất trước)" };
    private readonly GridModel _tm = new(), _lm = new();
    private readonly SheetGrid _tg, _lg;
    private readonly Dictionary<string, CheckBox> _duCong = CcCore.Loais.ToDictionary(x => x.Ma, x => new CheckBox { Content = x.Ten });
    private static readonly IBrush LeBg = AppTheme.Brush("#FCEFE6"), CtBg = AppTheme.Brush("#E6F4F1");

    // cột Chi tiết
    private const int CId = 0, CMa = 1, CTen = 2, CNhom = 3, CNgay = 4, CThu = 5, CVaoM = 6, CRaM = 7, CVao = 8, CRa = 9, CVs = 10, CVaoT = 11, CRaT = 12, CNghi = 13, CCong = 14, CChot = 15, CSau = 16, CPhep = 17, CSang = 18, CNote = 19, CWarn = 20;

    public ChamCongView()
    {
        var edit = Svc.CanEdit;
        _bg = new SheetGrid(_bm) { SpareRows = 0, CellBackground = BBg, CellForeground = BFg, CellTip = BTip, LargeTitle = "Bảng công tháng" };
        _cg = new SheetGrid(_cm) { SpareRows = 0, CellBackground = CBg, CellTip = CTip, LargeTitle = "Chấm công — chi tiết ngày" };
        _ng = new SheetGrid(_nm) { SpareRows = edit ? 10 : 0 };
        _tg = new SheetGrid(_tm) { SpareRows = edit ? 4 : 0, MinRows = 5, ShowTotals = false };
        _lg = new SheetGrid(_lm) { SpareRows = edit ? 6 : 0, MinRows = 6, ShowTotals = false };
        _cm.ReadOnly = !edit; _nm.ReadOnly = !edit; _tm.ReadOnly = !edit; _lm.ReadOnly = !edit;
        _cg.Suggest = (c, text) => c == CPhep ? CcCore.Loais.Where(x => CcCoreText.Fold(x.Ma + " " + x.Ten).Contains(CcCoreText.Fold(text))).Select(x => new SheetGrid.GridSuggestion(x.Ma, $"{x.Ma} — {x.Ten}", "")).ToList() : Array.Empty<SheetGrid.GridSuggestion>();
        _lg.Suggest = (c, text) => c == 1 ? new[] { ("L", "Nghỉ lễ"), ("T", "Nghỉ Tết") }.Select(x => new SheetGrid.GridSuggestion(x.Item1, $"{x.Item1} — {x.Item2}", "")).ToList() : Array.Empty<SheetGrid.GridSuggestion>();
        _cm.Changed += () => { if (!_loading && edit) { _cSave.Stop(); _cSave.Start(); _st.Text = "Chưa lưu…"; } };
        _cSave.Tick += (_, _) => { _cSave.Stop(); _ = SaveChiTiet(); };
        _thang.SelectionChanged += (_, _) => { if (!_loading) Compute(); };
        _cNv.SelectionChanged += (_, _) => { if (!_loading) PaintChiTiet(); };
        _cOnly.IsCheckedChanged += (_, _) => { if (!_loading) PaintChiTiet(); };
        _bView.SelectionChanged += (_, _) => { if (!_loading) PaintBang(); };
        _buTruoc.IsCheckedChanged += async (_, _) =>
        {
            if (_loading || CurKy is not { } ky) return;
            if (await Ui.Run(this, () => Svc.SetBuThangTruoc(ky, _buTruoc.IsChecked == true))) Compute();
        };

        var bar = new ToolRow { Margin = new Thickness(0, 4, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "Tháng", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        bar.Children.Add(_thang);
        bar.Children.Add(ToolRow.Gap());
        if (Svc.CanExport) bar.Children.Add(Btn("Xuất Excel", Export, "Bảng công (nhân viên × ngày, tổng giờ) + Chi tiết (cùng cột bảng Excel làm tay)"));
        if (edit) bar.Children.Add(Btn("Nạp file máy chấm công…", Import, "File \"Chi tiết chấm công\" xuất từ phần mềm máy chấm công (.xlsx / .xls / .csv)", primary: true));

        _tabs.Items.Add(new TabItem { Header = "Bảng công", Content = Pad(BuildBang()) });
        _tabs.Items.Add(new TabItem { Header = "Chi tiết ngày", Content = Pad(BuildChiTiet()) });
        _tabs.Items.Add(new TabItem { Header = "Nhân viên", Content = Pad(BuildNhanVien()) });
        _tabs.Items.Add(new TabItem { Header = "Cấu hình", Content = Pad(BuildCauHinh()) });
        var dock = new DockPanel();
        // thanh Tháng + nút được MainWindow kéo lên hàng tên trang (chỗ hẹp, 1 dòng) → dòng tóm tắt _st để riêng dưới đáy trang
        _st.TextTrimming = TextTrimming.CharacterEllipsis; _st.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);
        DockPanel.SetDock(_st, Dock.Bottom); dock.Children.Add(_st); dock.Children.Add(_tabs);
        Content = dock;
        AttachedToVisualTree += (_, _) => { if (!Ui.SkipReload(this)) Reload(); };   // 1.136: quay lại trang dữ liệu chưa đổi → không đọc lại
    }

    private static Control Pad(Control c) => new Border { Padding = new Thickness(0, 6, 0, 0), Child = c };
    private static TextBox Num() => new() { Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
    private static Button Btn(string t, Func<Task> click, string? tip = null, bool primary = false, bool danger = false)
    {
        var b = new Button { Content = t };
        if (primary) b.Classes.Add("primary"); if (danger) b.Classes.Add("danger");
        if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += async (_, _) => await click();
        return b;
    }
    private static TextBlock Muted(string t) => new() { Text = t, Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Tag = "keep" };

    // ================================================================== nạp / tính

    internal void Reload(string? chon = null)
    {
        Svc.Load();
        _loading = true;
        var cur = chon ?? _thang.SelectedItem as string;
        _thangs = Svc.Thangs().Select(Label).ToList();
        _thang.ItemsSource = _thangs;
        _thang.SelectedIndex = _thangs.Count == 0 ? -1 : Math.Max(0, cur is null ? 0 : _thangs.IndexOf(cur));
        _loading = false;
        PaintNhanVien(); PaintCauHinh();
        Compute();
    }

    private static string Label(string ky) => $"{ky[5..]}/{ky[..4]}";
    private static string Ky(string label) => $"{label[3..]}-{label[..2]}";
    private string? CurKy => _thang.SelectedItem is string s ? Ky(s) : null;

    private void Compute()
    {
        if (CurKy is not { } ky)
        {
            // 26/11: nạp bảng trống KHÔNG được coi là người dùng sửa (trước: báo "Chưa lưu…" + hẹn lưu); ô "Bù giờ dư" (chưa có chữ) ẩn
            _t = null; _st.Text = Svc.CanEdit ? "Chưa có dữ liệu — bấm “Nạp file máy chấm công…”." : "Chưa có dữ liệu chấm công.";
            _loading = true;
            try
            {
                _buTruoc.IsVisible = false;
                _bm.Load(new[] { new GridColumn("x", "Chưa có dữ liệu", 300) }, Array.Empty<string[]>()); _bg.Refresh();
                _cm.Load(new[] { new GridColumn("x", "Chưa có dữ liệu", 300) }, Array.Empty<string[]>()); _cg.Refresh();
            }
            finally { _loading = false; }
            _cSave.Stop();
            return;
        }
        _t = Svc.Tinh(ky);
        var canh = _t.Rows.Count(r => r.CanhBao.Length > 0); var sua = _t.Rows.Count(r => r.DaSua);
        _st.Text = $"{_t.Nvs.Count} nhân viên · {_t.Nvs.Sum(_t.TongGio):#,0.##} giờ" + (_t.SangList.Count > 0 ? $" · sang công {_t.SangList.Sum(x => x.Lan.Gio):#,0.##} giờ" : "") + (canh > 0 ? $" · {canh} ngày cần xem" : "") + (sua > 0 ? $" · {sua} ngày đã sửa tay" : "");
        _loading = true;
        var du = Svc.CauHinh.SangCong ? Svc.DuThangTruoc(ky) : null;
        _buTruoc.IsVisible = du is not null;
        _buTruoc.IsEnabled = Svc.CanEdit;
        _buTruoc.Content = $"Bù giờ dư tháng {Label(ChamCongService.KyTruoc(ky))} (còn {du ?? 0:#,0.##} giờ)";
        _buTruoc.IsChecked = Svc.CauHinh.BuThangTruoc.Contains(ky);
        _loading = false;
        PaintBang(); FillNvFilter(); PaintChiTiet();
    }

    private async Task Import()
    {
        var path = await Ui.OpenFile(this, "Chọn file chi tiết chấm công (máy chấm công)", "xlsx;xls;csv", "Excel / CSV");
        if (path is null) return;
        var (ok, f) = await Ui.Busy(this, "Đang đọc file chấm công…", () => ChamCongService.ReadFile(path), op: "Chấm công: đọc file");
        if (!ok || f is null) return;
        // chọn phòng ban
        var want = Svc.CauHinh.PhongBanList().Select(CcCoreText.Fold).ToHashSet();
        var boxes = f.PhongBan.OrderByDescending(p => p.Value).Select(p => new CheckBox
        {
            Content = $"{(p.Key.Length > 0 ? p.Key : "(không ghi phòng ban)")} — {p.Value:#,0} dòng", Tag = p.Key, IsChecked = want.Contains(CcCoreText.Fold(p.Key)),
        }).ToList();
        if (!boxes.Any(b => b.IsChecked == true) && boxes.Count == 1) boxes[0].IsChecked = true;
        var list = new StackPanel { Spacing = 2 };
        foreach (var b in boxes) list.Children.Add(b);
        var co = Svc.NhanVien.Count(n => n.Ma.Length > 0);
        var body = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = $"Tháng {Label(f.Thang)} · {f.Rows.Count:#,0} dòng · {f.Rows.Select(r => r.Ma).Distinct().Count():#,0} người trong file.", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "Lấy phòng ban:", Margin = new Thickness(0, 4, 0, 0) },
            new ScrollViewer { Content = list, MaxHeight = 300 },
            Muted((co > 0 ? $"Nhân viên đã có trong danh sách ({co} người) luôn được lấy dù khác phòng ban. " : "")
                + "Nạp lại cùng tháng sẽ THAY giờ chấm của tháng đó; các ô đã sửa tay giữ nguyên." + (f.CoVSom ? " File có cột V.Sớm (bảng đã làm tay) → ghi vào V.Sớm từng ngày." : "")),
        } };
        var w = Ui.Dialog("Nạp file máy chấm công", body, 520);
        var yes = new Button { Content = "Nạp", Classes = { "primary" }, IsDefault = true }; var no = new Button { Content = "Đóng", IsCancel = true };
        var go = false;
        yes.Click += (_, _) => { go = true; w.Close(); }; no.Click += (_, _) => w.Close();
        body.Children.Add(Ui.Buttons(no, yes));
        await Ui.Show(this, w);
        if (!go) return;
        var pbs = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!).ToList();
        if (pbs.Count == 0 && co == 0) { await Ui.Message(this, "Chưa chọn phòng ban nào."); return; }
        ChamCongService.KetQuaNap? kq = null;
        if (!await Ui.Run(this, () =>
        {
            kq = Svc.Commit(f, pbs);
            var pbText = string.Join(", ", pbs);
            if (pbs.Count > 0 && !string.Equals(pbText, Svc.CauHinh.PhongBan, StringComparison.OrdinalIgnoreCase)) { var c = Svc.CauHinh; c.PhongBan = pbText; Svc.SaveCauHinh(c); }
        })) return;
        Reload(Label(kq!.Thang));
        var thieu = Svc.NhanVien.Count(n => n.DangLam && n.Nhom.Length == 0);
        await Ui.Message(this, $"Đã nạp tháng {Label(kq.Thang)}: {kq.Dong:#,0} dòng" + (kq.NvMoi > 0 ? $", thêm {kq.NvMoi} nhân viên mới" : "") + (kq.VSom > 0 ? $", {kq.VSom} ngày có V.Sớm" : "") + "."
            + (thieu > 0 ? $"\n\n{thieu} nhân viên chưa có Bộ phận (Xuất / Nhập / VP) nên chưa trừ nghỉ trưa — khai ở tab Nhân viên." : ""), "Chấm công");
        if (thieu > 0) _tabs.SelectedIndex = 2;
    }

    private async Task Export()
    {
        if (_t is null) { await Ui.Message(this, "Chưa có dữ liệu tháng nào."); return; }
        var path = await Ui.SaveFile(this, "Xuất bảng chấm công", $"ChamCong_{_t.Ky}.xlsx", "xlsx", "Excel");
        if (path is null) return;
        var t = _t;
        if (await Ui.Run(this, () => ChamCongService.Export(t, path))) Ui.Exported(this, path);
    }

    // ================================================================== Bảng công

    private Control BuildBang()
    {
        var hint = Muted("Mỗi ô = giờ của ngày (mặc định SAU sang công); P = nghỉ có phép, L = nghỉ lễ, T = nghỉ Tết, CT = công tác (L / T / CT không đi làm tính đủ 1 công — đổi ở Cấu hình; các ngày này không bù / không cho giờ sang công). Nền cam = lễ / Tết, xanh ngọc = công tác, xanh lá = nhận giờ sang công, xanh dương = cho giờ dư đi, vàng = cần xem (chỉ 1 lần chấm, giờ vào quá sớm…), tím = đã sửa tay, chữ đỏ = Chủ nhật. Rê chuột vào ô để xem giờ vào / ra và sang công. Sửa ở tab Chi tiết ngày.");
        var btn = Btn("Xem chi tiết người đang chọn", () => { OpenChiTiet(_bg.Row); return Task.CompletedTask; });
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6), Children = { new ToolRow { Children = { new TextBlock { Text = "Hiện", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, _bView, _buTruoc, ToolRow.Gap(), btn } }, hint } };
        var d = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); d.Children.Add(top); d.Children.Add(Framed(_bg));
        _bg.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { OpenChiTiet(_bg.Row); e.Handled = true; } };
        _bg.DoubleTapped += (_, _) => OpenChiTiet(_bg.Row);
        return d;
    }

    private static Border Framed(Control c) => new() { BorderBrush = AppTheme.Brush("#B9C3D0"), BorderThickness = new Thickness(1), Child = c };

    private const int BDay0 = 3;
    private void PaintBang()
    {
        if (_t is null) return;
        var cols = new List<GridColumn> { new("ma", "Mã NV", 70) { ReadOnly = true }, new("ten", "Họ tên", 190) { ReadOnly = true }, new("nhom", "Bộ phận", 70) { ReadOnly = true } };
        cols.AddRange(_t.Ngays.Select(d => new GridColumn("d" + d.Day, $"{d.Day} {CcCore.Thu(d).Replace(".", "")}", 60, "number") { ReadOnly = true }));
        var sau = SauSangView;
        cols.Add(new GridColumn("so", sau ? "Ngày đủ công" : "Số ngày làm", 90, "number") { ReadOnly = true });
        cols.Add(new GridColumn("p", "Nghỉ phép", 76, "number") { ReadOnly = true });
        cols.Add(new GridColumn("le", "Lễ / Tết", 66, "number") { ReadOnly = true });
        cols.Add(new GridColumn("ct", "Công tác", 70, "number") { ReadOnly = true });
        cols.Add(new GridColumn("gl", "Giờ làm lễ / Tết", 108, "number") { ReadOnly = true });
        cols.Add(new GridColumn("tong", "Tổng giờ", 78, "number") { ReadOnly = true });
        cols.Add(new GridColumn("du", "Dư còn (tăng ca)", 110, "number") { ReadOnly = true });
        cols.Add(new GridColumn("cx", "Cần xem", 66, "number") { ReadOnly = true });
        var rows = _t.Nvs.Select(n =>
        {
            var r = new List<string> { n.Ma, n.Ten, n.Nhom };
            r.AddRange(_t.Ngays.Select(d => { var dd = _t.Dong[(n.Id, d)]; var x = sau ? dd.SauSang : dd.Chot; return dd.Loai.Length > 0 && (dd.DuCong || x == 0) ? CcCore.Nhan(dd.Loai) : x == 0 ? "" : Ui.Num(x); }));
            r.Add(sau ? _t.NgayDu(n).ToString() : _t.SoNgay(n).ToString());
            string Cnt(double k) => k > 0 ? Ui.Num(k) : "";
            r.Add(Cnt(_t.NgayPhep(n))); r.Add(Cnt(_t.NgayLoai(n, "L", "T"))); r.Add(Cnt(_t.NgayLoai(n, "CT")));
            var gl = _t.GioLe(n); r.Add(gl > 0 ? Ui.Num(gl) : ""); r.Add(Ui.Num(sau ? _t.TongSau(n) : _t.TongGio(n)));
            var du = _t.DuCon.GetValueOrDefault(n.Id); r.Add(du > 0 ? Ui.Num(du) : "");
            var cx = _t.CanhBao(n); r.Add(cx > 0 ? cx.ToString() : "");
            return (IEnumerable<string?>)r;
        }).ToList();
        _bm.Load(cols, rows); _bg.Refresh();
    }

    private CcDong? BCell(int r, int c)
    {
        if (_t is null || r < 0 || r >= _t.Nvs.Count || c < BDay0 || c >= BDay0 + _t.Ngays.Count) return null;
        return _t.Dong[(_t.Nvs[r].Id, _t.Ngays[c - BDay0])];
    }
    private IBrush? BBg(int r, int c) => BCell(r, c) is { } d
        ? d.CanhBao.Length > 0 ? WarnBg : d.Loai is "L" or "T" ? LeBg : d.Loai is "CT" ? CtBg : d.DaSua ? EditBg : SauSangView && d.Nhan.Count > 0 ? NhanBg : SauSangView && d.Cho.Count > 0 ? ChoBg : d.Ngay.DayOfWeek == DayOfWeek.Sunday ? SunBg : null
        : null;
    private IBrush? BFg(int r, int c) => BCell(r, c) is { } d && d.Ngay.DayOfWeek == DayOfWeek.Sunday ? Sun : null;
    private string? BTip(int r, int c) => BCell(r, c) is { } d
        ? $"{d.Ngay:dd/MM} {CcCore.Thu(d.Ngay)} · vào {Or(d.Vao)} · ra {Or(d.Ra)} · nghỉ {CcCore.HmShort(d.Ket.Nghi)} · chốt {Ui.Num(d.Chot)} giờ" + (d.SauSang != d.Chot || d.SuaSang ? $" → sau sang công {Ui.Num(d.SauSang)} giờ" : "")
          + (d.Loai.Length > 0 ? $"\n{CcCore.TenLoai(d.Loai)}{(d.TenLe.Length > 0 ? " " + d.TenLe : "")}{(d.DuCong ? " — tính đủ 1 công" : "")}{(d.GioLe > 0 ? $" — đi làm {Ui.Num(d.GioLe)} giờ (tính riêng)" : "")}" : "")
          + (ChamCongService.SangText(d) is { Length: > 0 } st ? "\n" + st : "") + (d.CanhBao.Length > 0 ? "\n⚠ " + d.CanhBao : "") + (d.ChepTu.Length > 0 ? "\nGiờ của " + d.ChepTu : "") + (d.GhiChu.Length > 0 ? "\n" + d.GhiChu : "")
        : null;
    private static string Or(string s) => s.Length > 0 ? s : "—";

    private void OpenChiTiet(int bangRow)
    {
        if (_t is null || bangRow < 0 || bangRow >= _t.Nvs.Count) return;
        _cNv.SelectedIndex = bangRow + 1;
        _tabs.SelectedIndex = 1;
    }

    // ================================================================== Chi tiết ngày

    private Control BuildChiTiet()
    {
        var bar = new ToolRow();
        bar.Children.Add(new TextBlock { Text = "Nhân viên", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        bar.Children.Add(_cNv); bar.Children.Add(_cOnly);
        if (Svc.CanEdit)
        {
            bar.Children.Add(ToolRow.Gap());
            bar.Children.Add(Btn("Chép giờ người khác…", ChepGio, "Người không có mã máy chấm công (làm thay): lấy giờ vào / ra của một người khác cho các ngày đang chọn"));
            bar.Children.Add(Btn("Loại ngày…", PhepSel, "Đánh dấu các ngày đang chọn: nghỉ có phép / nghỉ lễ / nghỉ Tết / công tác / ngày thường — các ngày này không bù giờ sang công"));
            bar.Children.Add(Btn("Trả về giờ máy", ResetSel, "Bỏ mọi sửa tay (giờ, V.Sớm, chốt, sau sang, nghỉ phép, chép giờ, ghi chú) của các ngày đang chọn"));
        }
        var hint = Muted("Sửa trực tiếp các cột có ✎: Giờ vào / Giờ ra (vd 07:30), V.Sớm (vào sớm / đi sớm — số giờ cộng thêm, vd 1), Chốt (ghi đè giờ chốt), Sau sang (ghi đè giờ sau sang công — ngày đó không cho / nhận sang công tự động; xóa ô = về tự động), Loại ngày (P nghỉ có phép · L lễ · T Tết · CT công tác — không bù giờ; ngày lễ chung khai ở Cấu hình), Ghi chú. Rời ô là tự lưu. Xóa ô giờ = để trống (0 giờ); muốn quay về giờ máy bấm “Trả về giờ máy”.");
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6), Children = { bar, hint } };
        var d = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); d.Children.Add(top); d.Children.Add(Framed(_cg));
        return d;
    }

    private void FillNvFilter()
    {
        if (_t is null) return;
        _loading = true;
        var cur = _cNv.SelectedIndex;
        _cNv.ItemsSource = new[] { "Tất cả" }.Concat(_t.Nvs.Select(n => (n.Ma.Length > 0 ? n.Ma + " · " : "") + n.Ten)).ToList();
        _cNv.SelectedIndex = Math.Clamp(cur, 0, _t.Nvs.Count);
        _loading = false;
    }

    private void PaintChiTiet()
    {
        if (_t is null) return;
        var nv = _cNv.SelectedIndex > 0 && _cNv.SelectedIndex <= _t.Nvs.Count ? _t.Nvs[_cNv.SelectedIndex - 1] : null;
        var only = _cOnly.IsChecked == true;
        _cRows = _t.Rows.Where(r => (nv is null || r.Nv.Id == nv.Id) && (!only || r.CanhBao.Length > 0 || r.DaSua)).ToList();
        var cols = new List<GridColumn>
        {
            new("id", "", 0) { ReadOnly = true }, new("ma", "Mã NV", 64) { ReadOnly = true }, new("ten", "Họ tên", 170) { ReadOnly = true }, new("nhom", "Bộ phận", 64) { ReadOnly = true },
            new("ngay", "Ngày", 82) { ReadOnly = true }, new("thu", "Thứ", 42) { ReadOnly = true },
            new("vm", "Vào (máy)", 80) { ReadOnly = true }, new("rm", "Ra (máy)", 76) { ReadOnly = true },
            new("vao", "Giờ vào ✎", 88), new("ra", "Giờ ra ✎", 84), new("vs", "V.Sớm ✎", 74, "number"),
            new("vt", "Vào", 52) { ReadOnly = true }, new("rt", "Ra", 52) { ReadOnly = true }, new("nghi", "Nghỉ", 52) { ReadOnly = true }, new("cong", "Công", 56) { ReadOnly = true },
            new("chot", "Chốt ✎", 72, "number"), new("sau", "Sau sang ✎", 88, "number"), new("phep", "Loại ngày ✎", 90), new("sang", "Sang công", 200) { ReadOnly = true },
            new("note", "Ghi chú ✎", 200), new("warn", "Cần xem", 260) { ReadOnly = true },
        };
        var rows = _cRows.Select(r => (IEnumerable<string?>)new[]
        {
            r.Nv.Id + "|" + r.Ngay.ToString("yyyy-MM-dd"), r.Nv.Ma, r.Nv.Ten, r.Nv.Nhom, r.Ngay.ToString("dd/MM/yyyy"), CcCore.Thu(r.Ngay),
            r.VaoGoc, r.RaGoc, r.Vao, r.Ra, r.VSom != 0 ? Ui.Num(r.VSom) : "",
            CcCore.HmShort(r.Ket.VaoTron), CcCore.HmShort(r.Ket.RaTron), r.Ket.Nghi > 0 ? CcCore.HmShort(r.Ket.Nghi) : "", r.Ket.Cong > 0 ? CcCore.HmShort(r.Ket.Cong) : "",
            r.Chot != 0 ? Ui.Num(r.Chot) : "", r.SauSang != 0 ? Ui.Num(r.SauSang) : "", CcCore.Nhan(r.Loai), ChamCongService.SangText(r), r.GhiChu,
            string.Join(" · ", new[] { r.CanhBao, r.ChepTu.Length > 0 ? "giờ của " + r.ChepTu : "", r.SuaChot ? "chốt sửa tay" : "", r.SuaSang ? "sang công sửa tay" : "" }.Where(x => x.Length > 0)),
        }).ToList();
        _loading = true;
        _cm.Load(cols, rows); _cg.Refresh();
        _loading = false;
    }

    private CcDong? CRow(int r) => r >= 0 && r < _cRows.Count ? _cRows[r] : null;
    private IBrush? CBg(int r, int c)
    {
        if (CRow(r) is not { } d) return null;
        if (c == CWarn && d.CanhBao.Length > 0) return WarnBg;
        if ((c == CVao && d.SuaVao) || (c == CRa && d.SuaRa) || (c == CVs && d.SuaVSom) || (c == CChot && d.SuaChot) || (c == CSau && d.SuaSang) || ((c is CVao or CRa) && d.ChepTu.Length > 0)) return EditBg;
        if (c == CPhep && d.Loai.Length > 0) return d.SuaLoai ? EditBg : LeBg;
        if (c is CSau or CSang && d.Nhan.Count > 0) return NhanBg;
        if (c is CSau or CSang && d.Cho.Count > 0) return ChoBg;
        if (d.CanhBao.Length > 0 && c is CVaoM or CRaM) return WarnBg;
        return null;
    }
    private string? CTip(int r, int c) => CRow(r) is { } d && c == CNghi
        ? $"Nghỉ trưa {d.Nv.Nhom} {CcCore.HmShort(Svc.CauHinh.NghiTruaOf(d.Nv.Nhom))}" + $"; có mặt ≤ {Svc.CauHinh.NuaNgayToiDa / 60.0:0.#} tiếng không trừ; ≥ {Svc.CauHinh.NghiToiTu / 60.0:0.#} tiếng trừ thêm {CcCore.HmShort(Svc.CauHinh.NghiToiPhut)}"
        : CRow(r) is { } v && c == CVs ? "V.Sớm = vào sớm / đi sớm: số giờ cộng thêm vào công ngày này" : null;

    /// <summary>So lưới với kết quả đang có → các sửa tay cần ghi.</summary>
    private List<CcSua> Diff()
    {
        var ky = CurKy!;
        var old = Svc.ReadSua(ky).ToDictionary(s => (s.Nv, s.Ngay));
        var res = new List<CcSua>();
        for (var i = 0; i < _cRows.Count && i < _cm.RowCount; i++)
        {
            var d = _cRows[i];
            string vao = _cm.Get(i, CVao).Trim(), ra = _cm.Get(i, CRa).Trim(), vs = _cm.Get(i, CVs).Trim(), chot = _cm.Get(i, CChot).Trim(), sauS = _cm.Get(i, CSau).Trim(), note = _cm.Get(i, CNote).Trim();
            var loai = CcCore.NormLoai(_cm.Get(i, CPhep));
            if (loai == "?") throw new KhoLanh.Core.Services.BusinessException($"Loại ngày \"{_cm.Get(i, CPhep).Trim()}\" không hiểu — dùng P (nghỉ có phép), L (lễ), T (Tết), CT (công tác) hoặc để trống.");
            var s = old.TryGetValue((d.Nv.Id, d.Ngay), out var o) ? o : new CcSua { Nv = d.Nv.Id, Ngay = d.Ngay };
            var before = (s.Vao, s.Ra, s.VSom, s.Chot, s.SauSang, s.Loai, s.GhiChu);
            if (loai != d.Loai)
            {
                var chung = Svc.CauHinh.NgayLe.TryGetValue(d.Ngay, out var nl) ? nl.Loai : "";
                s.Loai = loai == chung ? null : loai;   // trùng ngày lễ chung → theo lịch chung; khác → đánh dấu riêng ("" = ngày thường)
            }
            string Norm(string t) => CcCore.ParseTime(t) is int m ? CcCore.Hm(m) : t;
            // giờ: trùng giờ đang tính từ máy / chép → không ghi đè; khác → ghi đè (trống = không có giờ)
            var vaoNguon = s.ChepTu is { Length: > 0 } ? d.Vao : d.VaoGoc;
            var raNguon = s.ChepTu is { Length: > 0 } ? d.Ra : d.RaGoc;
            if (Norm(vao) != d.Vao) s.Vao = Norm(vao) == vaoNguon && s.Vao is null ? null : vao;
            if (Norm(ra) != d.Ra) s.Ra = Norm(ra) == raNguon && s.Ra is null ? null : ra;
            if (vs != (d.VSom != 0 ? Ui.Num(d.VSom) : ""))
                s.VSom = vs.Length == 0 ? null : Ui.TryParseNumber(vs, out var v) ? (v == 0 ? null : v) : throw new KhoLanh.Core.Services.BusinessException($"V.Sớm \"{vs}\" không phải số.");
            if (chot != (d.Chot != 0 ? Ui.Num(d.Chot) : ""))
            {
                if (chot.Length == 0) s.Chot = d.SuaChot ? null : 0;
                else if (Ui.TryParseNumber(chot, out var cv)) s.Chot = Math.Abs(cv - d.Ket.Gio) < 1e-9 && !d.SuaChot ? null : cv;
                else throw new KhoLanh.Core.Services.BusinessException($"Chốt \"{chot}\" không phải số.");
            }
            if (sauS != (d.SauSang != 0 ? Ui.Num(d.SauSang) : ""))
            {
                if (sauS.Length == 0) s.SauSang = d.SuaSang ? null : 0;
                else if (Ui.TryParseNumber(sauS, out var sv)) s.SauSang = sv;
                else throw new KhoLanh.Core.Services.BusinessException($"Sau sang \"{sauS}\" không phải số.");
            }
            s.GhiChu = note;
            if (before != (s.Vao, s.Ra, s.VSom, s.Chot, s.SauSang, s.Loai, s.GhiChu)) res.Add(s);
        }
        return res;
    }

    internal async Task SaveChiTiet()
    {
        if (CurKy is not { } ky || _t is null) return;
        List<CcSua> ch = new();
        if (!await Ui.Run(this, () => { ch = Diff(); if (ch.Count > 0) Svc.SaveSua(ky, ch); }))
        { PaintChiTiet(); return; }
        var sel = (_cg.Row, _cg.Selection.C1);
        Compute();
        _st.Text = ch.Count > 0 ? $"Đã lưu {ch.Count} ngày · " + _st.Text : _st.Text;
        if (sel.Row >= 0) _cg.Select(Math.Min(sel.Row, Math.Max(0, _cm.RowCount - 1)), sel.C1);
    }

    private List<CcDong> SelRows()
    {
        var checkedRows = _cg.CheckedRows;
        var idx = checkedRows.Count > 0 ? checkedRows : Enumerable.Range(_cg.Selection.R1, _cg.Selection.R2 - _cg.Selection.R1 + 1).ToList();
        return idx.Where(i => i >= 0 && i < _cRows.Count).Select(i => _cRows[i]).ToList();
    }

    private async Task ChepGio()
    {
        if (_t is null || CurKy is not { } ky) return;
        var rows = SelRows();
        if (rows.Count == 0) { await Ui.Message(this, "Chọn (hoặc tích) các ngày cần lấy giờ."); return; }
        var nvs = rows.Select(r => r.Nv.Id).Distinct().ToList();
        if (nvs.Count > 1) { await Ui.Message(this, "Các ngày đang chọn thuộc nhiều người — chọn ngày của MỘT người (lọc theo ô Nhân viên)."); return; }
        var others = _t.Nvs.Where(n => n.Id != nvs[0] && n.Ma.Length > 0).ToList();
        var pick = await Ui.Choose(this, $"Lấy giờ vào / ra cho {rows[0].Nv.Ten} ({rows.Count} ngày) theo giờ của:", "Chép giờ người khác", others.Select(n => $"{n.Ma} · {n.Ten}").ToList());
        if (pick is null) return;
        var src = others[pick.Value.Index];
        var old = Svc.ReadSua(ky).ToDictionary(s => (s.Nv, s.Ngay));
        var ch = rows.Select(r => { var s = old.TryGetValue((r.Nv.Id, r.Ngay), out var o) ? o : new CcSua { Nv = r.Nv.Id, Ngay = r.Ngay }; s.ChepTu = src.Id; s.Vao = null; s.Ra = null; return s; }).ToList();
        if (await Ui.Run(this, () => Svc.SaveSua(ky, ch))) Compute();
    }

    private async Task PhepSel()
    {
        if (CurKy is not { } ky) return;
        var rows = SelRows();
        if (rows.Count == 0) { await Ui.Message(this, "Chọn (hoặc tích) các ngày cần đánh dấu."); return; }
        var opts = CcCore.Loais.Select(x => $"{x.Ma} — {x.Ten}").Append("Ngày thường (bỏ đánh dấu)").ToList();
        var pick = await Ui.Choose(this, $"Đánh dấu {rows.Count} ngày đang chọn là:", "Loại ngày", opts);
        if (pick is null) return;
        var loai = pick.Value.Index < CcCore.Loais.Length ? CcCore.Loais[pick.Value.Index].Ma : "";
        var old = Svc.ReadSua(ky).ToDictionary(s => (s.Nv, s.Ngay));
        var ch = rows.Select(r =>
        {
            var s = old.TryGetValue((r.Nv.Id, r.Ngay), out var o) ? o : new CcSua { Nv = r.Nv.Id, Ngay = r.Ngay };
            var chung = Svc.CauHinh.NgayLe.TryGetValue(r.Ngay, out var nl) ? nl.Loai : "";
            s.Loai = loai == chung ? null : loai;
            return s;
        }).ToList();
        if (await Ui.Run(this, () => Svc.SaveSua(ky, ch))) { Compute(); _st.Text = $"Đã đánh dấu {ch.Count} ngày: {(loai.Length > 0 ? CcCore.TenLoai(loai) : "ngày thường")} · " + _st.Text; }
    }

    private async Task ResetSel()
    {
        if (CurKy is not { } ky) return;
        var rows = SelRows().Where(r => r.DaSua || r.GhiChu.Length > 0).ToList();
        if (rows.Count == 0) { await Ui.Message(this, "Các ngày đang chọn chưa sửa tay."); return; }
        if (!await Ui.Confirm(this, $"Bỏ sửa tay của {rows.Count} ngày, quay về giờ máy chấm công?", "Trả về giờ máy", "Trả về")) return;
        if (await Ui.Run(this, () => Svc.SaveSua(ky, rows.Select(r => new CcSua { Nv = r.Nv.Id, Ngay = r.Ngay })))) Compute();
    }

    // ================================================================== Nhân viên

    private Control BuildNhanVien()
    {
        var bar = new ToolRow();
        if (Svc.CanEdit) bar.Children.Add(Btn("Lưu danh sách", SaveNhanVien, primary: true));
        var hint = Muted("Mã máy = mã nhân viên trên máy chấm công (để trống = người không chấm máy, nhập giờ tay / chép giờ ở tab Chi tiết). Bộ phận quyết định giờ nghỉ trưa (Xuất / Nhập / VP — xem tab Cấu hình). Đang làm: x = hiện trên bảng công mọi tháng; trống = chỉ hiện tháng có dữ liệu. Thứ tự dòng = thứ tự trên bảng công.");
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6), Children = { bar, hint } };
        var d = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); d.Children.Add(top); d.Children.Add(Framed(_ng));
        _ng.Suggest = (c, text) => c == 3 ? new[] { "XUẤT", "NHẬP", "VP" }.Concat(Svc.CauHinh.NghiTrua.Keys).Distinct().Where(x => x.Contains(text.Trim().ToUpperInvariant())).Select(x => new SheetGrid.GridSuggestion(x, x, "")).ToList() : Array.Empty<SheetGrid.GridSuggestion>();
        return d;
    }

    private void PaintNhanVien()
    {
        var cols = new[]
        {
            new GridColumn("id", "", 0) { ReadOnly = true }, new GridColumn("ma", "Mã máy", 80), new GridColumn("ten", "Họ tên", 220), new GridColumn("nhom", "Bộ phận", 90),
            new GridColumn("pb", "Phòng ban", 120), new GridColumn("lam", "Đang làm", 86), new GridColumn("note", "Ghi chú", 260),
        };
        _nm.Load(cols, Svc.NhanVien.Select(n => (IEnumerable<string?>)new[] { n.Id, n.Ma, n.Ten, n.Nhom, n.PhongBan, n.DangLam ? "x" : "", n.GhiChu }));
        _ng.Refresh();
    }

    private async Task SaveNhanVien()
    {
        var list = new List<CcNhanVien>();
        for (var i = 0; i < _nm.RowCount; i++)
        {
            string G(int c) => _nm.Get(i, c).Trim();
            if (G(1).Length == 0 && G(2).Length == 0) continue;
            list.Add(new CcNhanVien { Id = G(0), Ma = G(1), Ten = G(2), Nhom = G(3), PhongBan = G(4), DangLam = G(5).Length > 0, GhiChu = G(6) });
        }
        var mat = Svc.NhanVien.Where(n => list.All(x => x.Id != n.Id)).ToList();
        if (mat.Count > 0 && !await Ui.Confirm(this, $"Xóa {mat.Count} nhân viên khỏi danh sách ({string.Join(", ", mat.Take(5).Select(n => n.Ten))}{(mat.Count > 5 ? "…" : "")})? Sửa tay của họ vẫn giữ trong dữ liệu tháng nhưng không còn hiện.", "Lưu danh sách", "Lưu")) return;
        if (await Ui.Run(this, () => Svc.SaveNhanVien(list))) { Reload(); _st.Text = $"Đã lưu {list.Count} nhân viên · " + _st.Text; }
    }

    // ================================================================== Cấu hình

    private Control BuildCauHinh()
    {
        StackPanel Row(string label, params Control[] kids)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            p.Children.Add(new TextBlock { Text = label, Width = 250, VerticalAlignment = VerticalAlignment.Center });
            foreach (var k in kids) p.Children.Add(k);
            return p;
        }
        TextBlock T(string s) => new() { Text = s, VerticalAlignment = VerticalAlignment.Center };
        var left = new StackPanel { Spacing = 10, Children =
        {
            new TextBlock { Text = "Quy tắc tính giờ", Classes = { "section" } },
            Row("Phòng ban lấy từ file máy", _pb),
            Row("Làm tròn giờ VÀO", T("phút <"), _vTron, T("→ tròn giờ; <"), _vNua, T("→ :30; còn lại → giờ sau")),
            Row("Làm tròn giờ RA", T("phút <"), _rTron, T("→ tròn giờ; <"), _rNua, T("→ :30; còn lại → giờ sau")),
            Row("Nửa ngày (không trừ nghỉ trưa)", T("có mặt ≤"), _nua, T("phút")),
            Row("Nghỉ tối", T("có mặt ≥"), _toiTu, T("phút → trừ thêm"), _toi, T("phút")),
            Row("Cảnh báo giờ vào quá sớm", T("trước"), _som, T("phút tính từ 0:00 (240 = 4:00)")),
            Row("Giờ 1 công", _chuan, T("phút (480 = 8 tiếng)")),
            _sang,
            Row("Không đi làm vẫn tính đủ 1 công", _duCong["L"], _duCong["T"], _duCong["CT"], _duCong["P"]),
            Muted("Nghỉ trưa theo bộ phận (phút) ở bảng bên phải; bộ phận không có trong bảng thì không trừ nghỉ. V.Sớm (vào sớm / đi sớm, giờ) nhập từng ngày ở tab Chi tiết — cộng thẳng vào giờ công. Bù giờ dư tháng trước: bật từng tháng ở tab Bảng công."),
        } };
        if (Svc.CanEdit) left.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Btn("Lưu cấu hình", SaveCauHinh, primary: true), Btn("Về mặc định", async () => { if (await Ui.Confirm(this, "Đưa quy tắc tính giờ về mặc định (theo bảng chấm công kho tháng 10/2026)?", "Về mặc định", "Đồng ý") && await Ui.Run(this, () => Svc.SaveCauHinh(new CcCauHinh()))) { PaintCauHinh(); Compute(); } }) } });
        var right = new StackPanel { Width = 420, Spacing = 6 };
        right.Children.Add(new TextBlock { Text = "Nghỉ trưa theo bộ phận", Classes = { "section" } });
        right.Children.Add(new Border { Height = 230, Child = Framed(_tg) });
        right.Children.Add(new TextBlock { Text = "Ngày lễ / Tết (cả kho)", Classes = { "section" }, Margin = new Thickness(0, 10, 0, 0) });
        right.Children.Add(Muted("Ngày (dd/MM/yyyy) · Loại L = lễ, T = Tết · Tên. Áp cho mọi nhân viên; người nào đi làm / không nghỉ thì sửa riêng ở cột Loại ngày tab Chi tiết. Lưu cùng nút Lưu cấu hình."));
        right.Children.Add(new Border { Height = 280, Child = Framed(_lg) });
        left.MaxWidth = 900; left.HorizontalAlignment = HorizontalAlignment.Left;   // ghi chú dài xuống dòng, không đẩy khung phải ra ngoài
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,Auto"), VerticalAlignment = VerticalAlignment.Top };
        g.Children.Add(left); Grid.SetColumn(right, 2); g.Children.Add(right);
        return new ScrollViewer { Content = g };
    }

    private void PaintCauHinh()
    {
        var c = Svc.CauHinh;
        _pb.Text = c.PhongBan; _vTron.Text = c.VaoTronGio.ToString(); _vNua.Text = c.VaoNuaGio.ToString(); _rTron.Text = c.RaTronGio.ToString(); _rNua.Text = c.RaNuaGio.ToString();
        _nua.Text = c.NuaNgayToiDa.ToString(); _toiTu.Text = c.NghiToiTu.ToString(); _toi.Text = c.NghiToiPhut.ToString(); _som.Text = c.VaoSomNhat.ToString();
        _chuan.Text = c.GioChuan.ToString(); _sang.IsChecked = c.SangCong; _sang.IsEnabled = Svc.CanEdit;
        foreach (var b in new[] { _pb, _vTron, _vNua, _rTron, _rNua, _nua, _toiTu, _toi, _som, _chuan }) b.IsReadOnly = !Svc.CanEdit;
        _tm.Load(new[] { new GridColumn("bp", "Bộ phận", 130), new GridColumn("ph", "Nghỉ trưa (phút)", 130, "number") }, c.NghiTrua.Select(p => (IEnumerable<string?>)new[] { p.Key, p.Value.ToString() }));
        _tg.Refresh();
        foreach (var (k, cb) in _duCong) { cb.IsChecked = c.DuCongKhiNghi.Contains(k); cb.IsEnabled = Svc.CanEdit; }
        _lm.Load(new[] { new GridColumn("ngay", "Ngày", 100), new GridColumn("loai", "Loại", 60), new GridColumn("ten", "Tên", 190) },
            c.NgayLe.Select(p => (IEnumerable<string?>)new[] { p.Key.ToString("dd/MM/yyyy"), p.Value.Loai, p.Value.Ten }));
        _lg.Refresh();
    }

    private async Task SaveCauHinh()
    {
        int I(TextBox t, string name) => int.TryParse((t.Text ?? "").Trim(), out var v) && v >= 0 && v <= 1440 ? v : throw new KhoLanh.Core.Services.BusinessException($"Ô \"{name}\" phải là số phút (0–1440).");
        await Ui.Run(this, () =>
        {
            var c = new CcCauHinh
            {
                PhongBan = (_pb.Text ?? "").Trim(), VaoTronGio = I(_vTron, "vào tròn giờ"), VaoNuaGio = I(_vNua, "vào :30"), RaTronGio = I(_rTron, "ra tròn giờ"), RaNuaGio = I(_rNua, "ra :30"),
                NuaNgayToiDa = I(_nua, "nửa ngày"), NghiToiTu = I(_toiTu, "nghỉ tối từ"), NghiToiPhut = I(_toi, "nghỉ tối"), VaoSomNhat = I(_som, "giờ vào quá sớm"),
                GioChuan = I(_chuan, "giờ 1 công"), SangCong = _sang.IsChecked == true, BuThangTruoc = Svc.CauHinh.BuThangTruoc,
            };
            if (c.GioChuan < 60) throw new KhoLanh.Core.Services.BusinessException("Giờ 1 công phải từ 60 phút trở lên.");
            if (c.VaoTronGio > c.VaoNuaGio || c.RaTronGio > c.RaNuaGio || c.VaoNuaGio > 60 || c.RaNuaGio > 60) throw new KhoLanh.Core.Services.BusinessException("Mốc làm tròn phải tăng dần và không quá 60 phút.");
            c.NghiTrua.Clear();
            for (var i = 0; i < _tm.RowCount; i++)
            {
                var k = CcCore.NormNhom(_tm.Get(i, 0)); var v = _tm.Get(i, 1).Trim();
                if (k.Length == 0) continue;
                if (!int.TryParse(v, out var ph) || ph < 0 || ph > 600) throw new KhoLanh.Core.Services.BusinessException($"Nghỉ trưa của {k} phải là số phút.");
                c.NghiTrua[k] = ph;
            }
            foreach (var (k, cb) in _duCong) if (cb.IsChecked == true) c.DuCongKhiNghi.Add(k); else c.DuCongKhiNghi.Remove(k);
            for (var i = 0; i < _lm.RowCount; i++)
            {
                string ng = _lm.Get(i, 0).Trim(), lo = _lm.Get(i, 1).Trim(), ten = _lm.Get(i, 2).Trim();
                if (ng.Length == 0 && ten.Length == 0) continue;
                var d = CcCore.ParseDate(ng) ?? throw new KhoLanh.Core.Services.BusinessException($"Ngày lễ \"{ng}\" không đúng dạng dd/MM/yyyy.");
                var l = CcCore.NormLoai(lo.Length == 0 ? "L" : lo);
                if (l is not ("L" or "T")) throw new KhoLanh.Core.Services.BusinessException($"Loại ngày lễ \"{lo}\" — chỉ L (lễ) hoặc T (Tết).");
                c.NgayLe[d] = (l, ten);
            }
            Svc.SaveCauHinh(c);
        });
        PaintCauHinh(); Compute();
    }
}

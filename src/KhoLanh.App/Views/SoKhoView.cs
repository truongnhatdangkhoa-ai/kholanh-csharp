using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Services;
using KhoLanh.Core.SoKho;
using static KhoLanh.Core.SoKho.SoKhoCore;

namespace KhoLanh.App.Views;

/// <summary>
/// NXT BỘT/SỐT — SỔ KHO (theo M03 của app cũ, dùng chung database nxt-bot-sot). Mọi nghiệp vụ là dòng trong sổ cái; tồn tính theo lô
/// (Vị trí · Mã hàng · Size · Hạn dùng · Ngày nhập), xuất không chỉ định lô thì trừ FIFO. Tab: Phiếu nhập · Phiếu xuất · Chuyển vị trí ·
/// Tồn kho (theo mã / vị trí / lô, NXT trong kỳ, sắp hết hạn, tồn âm, chốt tồn) · Kiểm kê · Định mức · Sổ cái & lịch sử (hoàn tác) · Cấu hình.
/// </summary>
public sealed class SoKhoView : UserControl, IPage
{
    public string Title => "NXT Bột/Sốt";
    public string Subtitle => "Sổ kho bột / sốt theo lô (vị trí · mã · size · hạn dùng · ngày nhập) — xuất không chỉ định lô thì trừ FIFO. Dùng chung dữ liệu với app cũ.";

    internal static SoKhoService Svc => App.Services.SoKho;
    internal static readonly IBrush Line_ = AppTheme.Brush("#D0D7E2"), Red = AppTheme.Brush("#FDE7E7"), Yellow = AppTheme.Brush("#FFF4CC"), Green = AppTheme.Brush("#E6F4EA");
    internal static readonly CultureInfo Ic = CultureInfo.InvariantCulture;
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly List<Action> _refresh = new();

    public SoKhoView()
    {
        var tabs = new TabControl();
        void Tab(string h, Control c, Action refresh) { tabs.Items.Add(new TabItem { Header = h, Content = c }); _refresh.Add(refresh); }
        var pn = new PhieuPanel("nhap"); Tab("Phiếu nhập", pn, pn.Reload);
        var px = new PhieuPanel("xuat"); Tab("Phiếu xuất", px, px.Reload);
        var pc = new PhieuPanel("chuyen"); Tab("Chuyển vị trí", pc, pc.Reload);
        var ton = new TonPanel(); Tab("Tồn kho", ton, ton.Paint);
        var kk = new KiemKePanel(); Tab("Kiểm kê", kk, kk.Paint);
        var dm = new DinhMucPanel(); Tab("Định mức", dm, dm.Paint);
        var sc = new SoCaiPanel(); Tab("Sổ cái & lịch sử", sc, sc.Paint);
        var cf = new CauHinhPanel(); Tab("Cấu hình", cf, cf.Paint);
        tabs.SelectionChanged += (_, e) => { if (!ReferenceEquals(e.Source, tabs)) return;   /* 1.172: danh sách / ô chọn bên trong nổi lên đây → không phải đổi tab */ if (tabs.SelectedIndex >= 0 && tabs.SelectedIndex < _refresh.Count && _loaded) _refresh[tabs.SelectedIndex](); };
        var root = new DockPanel(); DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status); root.Children.Add(tabs);
        Content = root;
        AttachedToVisualTree += async (_, _) =>
        {
            if (Ui.SkipReload(this) && _loaded) return;   // 1.136: quay lại trang dữ liệu chưa đổi → không đọc lại
            if (!await Ui.RunAsync(this, () => Task.Run(Svc.Load))) return;
            _loaded = true; Status();
            foreach (var r in _refresh) r();
        };
        Changed += () => { Status(); foreach (var r in _refresh) r(); };
    }
    private bool _loaded;

    /// <summary>Báo các tab vẽ lại sau khi sổ đổi.</summary>
    private static TextBlock Muted(string t) => new() { Text = t, FontSize = 12, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
    internal static event Action? Changed;
    internal static void RaiseChanged() => Changed?.Invoke();

    private void Status()
    {
        var neg = Negatives(Svc.Lines).Count; var exp = Expiring(Svc.Lines, Iso(DateTime.Today), Svc.HsdDays).Count; var low = Svc.BelowMin().Count;
        _status.Text = $"Sổ cái {Svc.Lines.Count:#,0} dòng" + (Svc.DroppedDup > 0 ? $" · ⚠ bỏ {Svc.DroppedDup} dòng trùng mã dòng" : "")
            + (neg > 0 ? $" · ⚠ {neg} lô tồn âm" : "") + (exp > 0 ? $" · ⏰ {exp} lô hết hạn trong {Svc.HsdDays} ngày" : "") + (low > 0 ? $" · {low} mã dưới tồn tối thiểu" : "");
    }

    // ------------------------------------------------------------------ tiện ích dùng chung

    internal static Button Btn(string t, Func<Task> click, string? tip = null, bool primary = false)
    {
        var b = new Button { Content = t, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary"); if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += async (_, _) => await click(); return b;
    }
    internal static Button Btn(string t, Action click, string? tip = null, bool primary = false) => Btn(t, () => { click(); return Task.CompletedTask; }, tip, primary);
    internal static CalendarDatePicker Date(DateTime? d = null) => new() { Width = 135, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy", SelectedDate = d ?? DateTime.Today };
    internal static string IsoOf(CalendarDatePicker p) => p.SelectedDate is { } d ? Iso(d) : Iso(DateTime.Today);
    internal static Border Framed(Control c) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = c };
    internal static TextBlock Lb(string t) => new() { Text = t, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    internal static string F(double v, int d = 2) => v.ToString("#,0." + new string('#', d), Ic);
    internal static string F(double? v, int d = 2) => v is { } x ? F(x, d) : "";
    internal static async Task<string?> SaveXlsx(Control c, string name) => await Ui.SaveFile(c, "Xuất Excel", name + ".xlsx", "xlsx", "Excel");

    // ================================================================== PHIẾU (nhập / xuất / chuyển)

    private sealed class PhieuPanel : UserControl
    {
        private readonly string _loai;
        private readonly ListBox _list = new();
        private readonly TextBox _q = new() { Watermark = "Tìm số phiếu, LSX, mã…" };
        private readonly CalendarDatePicker _ngay = Date();
        private readonly TextBox _ct = new() { Width = 150 }, _hd = new() { Width = 170, Watermark = "LSX / hợp đồng (cả phiếu)" };
        private readonly GridModel _m = new(); private readonly SheetGrid _g;
        private readonly TextBlock _msg = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _head = new() { FontSize = 15, FontWeight = FontWeight.Bold };
        private List<PGroup> _groups = new(); private List<string> _ids = new(); private PGroup? _cur; private List<SkLot> _lots = new();
        private string[] _keys = Array.Empty<string>();
        private Dictionary<string, (string H, double W, string T)> _def = new(ColDef);
        private string _tplSig = "";
        /// <summary>Cột của lưới phiếu theo mẫu cột (SoKhoMau) của loại phiếu: thứ tự, tên, ẩn, cột phụ x_….</summary>
        private void ApplyMau()
        {
            var t = Svc.TemplateFor(_loai);
            var cols = t.Columns.Where(c => !c.Hidden || c.Key == "maHang").ToList();
            if (_loai == "chuyen") cols = cols.Where(c => c.Key is not ("netDv" or "hopDong")).ToList();
            var keys = cols.Select(c => c.Key).ToList();
            if (_loai == "chuyen") keys.Insert(Math.Max(0, keys.IndexOf("viTri") + 1), "viTriDen");
            _def = new(ColDef);
            foreach (var c in cols) _def[c.Key] = (c.Key == "soLuong" && !c.Label.Contains('(') ? c.Label + " (bao)" : c.Label, ColDef.TryGetValue(c.Key, out var cd) ? Math.Max(cd.W, Math.Min(c.Label.Length * 7.5 + 16, 200)) : Math.Min(Math.Max(c.W * 0.8, 90), 220), c.Type == "number" ? "number" : "text");
            _keys = keys.ToArray();
            _tplSig = string.Join("|", _keys.Select(k => k + "=" + _def[k].H));
        }

        public PhieuPanel(string loai)
        {
            _loai = loai;
            _keys = loai switch
            {
                "chuyen" => new[] { "viTri", "viTriDen", "maHang", "size", "soLuong", "net", "hanDung", "ngayNhap", "ghiChu" },
                "xuat" => new[] { "viTri", "maHang", "size", "soLuong", "netDv", "net", "hopDong", "hanDung", "ngayNhap", "ghiChu" },
                _ => new[] { "viTri", "maHang", "size", "soLuong", "netDv", "net", "hopDong", "hanDung", "ngayNhap", "ghiChu" },
            };
            ApplyMau();
            _g = new SheetGrid(_m) { SpareRows = 12 };
            _list.SelectionChanged += (_, _) => { if (_list.SelectedIndex >= 0 && _list.SelectedIndex < Shown.Count) Open(Shown[_list.SelectedIndex]); };
            _q.TextChanged += (_, _) => FillList();
            _ngay.SelectedDateChanged += (_, _) => { if (_cur is null) _ct.Text = NextSo(Svc.Lines, IsoOf(_ngay), _loai == "nhap" ? "NK" : _loai == "chuyen" ? "CV" : "BS"); };
            var left = new DockPanel { Width = 290 };
            var lt = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
            if (Svc.CanEdit) lt.Children.Add(Btn("➕ Phiếu " + Ten + " mới", New, primary: true));
            lt.Children.Add(_q);
            DockPanel.SetDock(lt, Dock.Top); left.Children.Add(lt); left.Children.Add(Framed(_list));
            // 1.84 (mẫu chuẩn): ô đầu phiếu + nút chung 1 hàng, nút chính cuối, nút ít dùng vào ⋯ Thêm.
            var bar = new ToolRow();
            bar.Children.Add(Lb("Ngày")); bar.Children.Add(_ngay); bar.Children.Add(Lb("Số chứng từ")); bar.Children.Add(_ct);
            if (loai == "xuat") { bar.Children.Add(Lb("LSX")); bar.Children.Add(_hd); }
            bar.Children.Add(ToolRow.Gap());
            bar.Children.Add(Btn("Kiểm tra", () => { Check(); }, "Kiểm tra lỗi từng dòng, định mức LSX, lô chỉ định"));
            bar.Children.Add(Btn("🖨 In phiếu", Print, "Mở phiếu A4 trong trình duyệt để In / Lưu PDF"));
            if (Svc.CanEdit) { bar.Children.Add(ToolRow.More(Btn("Xóa phiếu", Delete))); bar.Children.Add(Btn("💾 Lưu phiếu", Save, primary: true)); }
            var top = new StackPanel { Spacing = 6 }; top.Children.Add(_head); top.Children.Add(bar);
            // 1.57: phiếu và bảng tồn theo lô thành 2 tab con (màn hình nhỏ không phải xem 2 bảng chồng nhau).
            var editor = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            editor.Children.Add(top);
            var gf = Framed(_g);
            var ms = new ScrollViewer { Content = _msg, MaxHeight = 120 }; Grid.SetRow(ms, 2); editor.Children.Add(ms);
            Control body = gf;
            if (loai != "nhap" && Svc.CanEdit)
            {
                // 1.174: chọn hàng từ tồn theo lô trong cửa sổ lớn (tích ô đầu dòng = lấy hết lô, hoặc gõ SL) như Tồn theo vị trí.
                var pick = Btn("➕ Chọn hàng từ tồn…", async () => await OpenPicker(), "Mở bảng tồn theo lô: lọc / tìm, tích ô đầu dòng (lấy hết) hoặc gõ SL " + Ten + ", rồi đưa vào phiếu", primary: true);
                var th = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                DockPanel.SetDock(pick, Dock.Left); th.Children.Add(pick);
                th.Children.Add(new TextBlock { Text = loai == "xuat" ? "  Để trống Hạn dùng / Ngày nhập ở dòng phiếu = trừ FIFO; chọn từ tồn để " + Ten + " đúng lô." : "", Tag = "keep", Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                var wrap = new DockPanel(); DockPanel.SetDock(th, Dock.Top); wrap.Children.Add(th); wrap.Children.Add(gf);
                body = wrap;
            }
            body.Margin = new Thickness(0, 6); Grid.SetRow(body, 1); editor.Children.Add(body);
            var root = new DockPanel();
            DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
            root.Children.Add(new Border { Padding = new Thickness(10, 0, 0, 0), Child = editor });
            Content = root;
        }
        private string Ten => _loai switch { "nhap" => "nhập", "xuat" => "xuất", _ => "chuyển" };
        private List<PGroup> Shown = new();

        private static readonly Dictionary<string, (string H, double W, string T)> ColDef = new()
        {
            ["viTri"] = ("Vị trí", 80, "text"), ["viTriDen"] = ("Vị trí đến", 85, "text"), ["maHang"] = ("Mã hàng", 150, "text"), ["size"] = ("Size", 70, "text"),
            ["soLuong"] = ("Số lượng (bao)", 95, "number"), ["netDv"] = ("Net (kg/bao)", 85, "number"), ["net"] = ("Tổng (kg)", 85, "number"), ["hopDong"] = ("LSX / Hợp đồng", 120, "text"),
            ["hanDung"] = ("Hạn dùng", 90, "text"), ["ngayNhap"] = ("Ngày nhập", 90, "text"), ["ghiChu"] = ("Ghi chú", 180, "text"),
        };

        public void Reload()
        {
            var sig = _tplSig; ApplyMau(); var tplChanged = sig != _tplSig;
            _groups = Groups(Svc.Lines, _loai);
            FillList();
            _lots = Compute(Svc.Lines).Lots.Where(l => l.Sl > Eps || l.Net > Eps).OrderBy(l => l.MaHang).ThenBy(l => l.ViTri).ThenBy(l => l.NgayNhap).ToList();
            if (_cur is not null && _groups.FirstOrDefault(g => g.Key == _cur.Key) is { } again) Open(again); else if (_cur is null && (_m.ColCount == 0 || tplChanged)) New();
        }
        private void FillList()
        {
            var q = Core.ChungTu.ChungTuService.StripAccents(_q.Text ?? "");
            Shown = _groups.Where(g => q.Length == 0 || Core.ChungTu.ChungTuService.StripAccents(g.Ct + " " + g.HopDong + " " + string.Join(" ", g.Lines.Select(l => l.MaHang))).Contains(q)).ToList();
            _list.ItemsSource = Shown.Select(g => $"{(g.Ct.Length > 0 ? g.Ct : "(không số)")} · {Dmy(g.Ngay)}\n{g.Dong} dòng · {F(g.Net)} kg{(g.HopDong.Length > 0 ? " · " + g.HopDong : "")}{(g.Nguon == "dt" ? " · 📱" : "")}").ToList();
        }
        private void Load(IEnumerable<SkLine> ls)
        {
            string V(SkLine l, string k) => k switch
            {
                "viTri" => l.ViTri, "viTriDen" => l.ViTriDen, "maHang" => l.MaHang, "size" => l.Size, "soLuong" => F(l.SoLuong, 4), "netDv" => F(l.NetDv, 4), "net" => F(l.Net, 4),
                "hopDong" => l.HopDong, "hanDung" => Dmy(l.HanDung), "ngayNhap" => _loai == "nhap" && l.NgayNhap == l.Ngay ? "" : Dmy(l.NgayNhap), "ghiChu" => l.GhiChu,
                _ => k.StartsWith("x_", StringComparison.Ordinal) && l.Extra[k] is { } xv ? (xv is JsonValue jv && jv.TryGetValue<double>(out var xd) ? F(xd, 4) : xv.ToString()) : "",
            };
            var list = ls.ToList(); _ids = list.Select(l => l.Id).ToList();
            _m.Load(_keys.Select(k => new GridColumn(k, _def[k].H, _def[k].W, _def[k].T)), list.Select(l => _keys.Select(k => V(l, k))));
            _g.Refresh(); _msg.Text = "";
        }
        private void New()
        {
            _cur = null; _list.SelectedIndex = -1; _ngay.SelectedDate = DateTime.Today; _hd.Text = "";
            _ct.Text = NextSo(Svc.Lines, Iso(DateTime.Today), _loai == "nhap" ? "NK" : _loai == "chuyen" ? "CV" : "BS");
            _head.Text = "Phiếu " + Ten + " mới"; Load(Array.Empty<SkLine>());
        }
        private void Open(PGroup g)
        {
            _cur = g; _ngay.SelectedDate = DateTime.ParseExact(g.Ngay.Length == 10 ? g.Ngay : Iso(DateTime.Today), "yyyy-MM-dd", Ic); _ct.Text = g.Ct;
            _hd.Text = g.Lines.Select(l => l.HopDong).Distinct().Count() == 1 ? g.Lines[0].HopDong : "";
            _head.Text = $"Phiếu {Ten} {g.Ct} · {Dmy(g.Ngay)}" + (g.Nguon == "dt" ? " · từ điện thoại" : "");
            Load(g.Lines);
        }
        private List<SkLine> Draft()
        {
            var o = new List<SkLine>(); var ngay = IsoOf(_ngay);
            for (var r = 0; r < _m.RowCount; r++)
            {
                if (_keys.All(k => _m.Get(r, Array.IndexOf(_keys, k)).Trim().Length == 0)) continue;
                var keep = r < _ids.Count ? _cur?.Lines.FirstOrDefault(l => l.Id == _ids[r]) : null;
                // dòng đang sửa: giữ các ô mẫu đang ẩn (không có trên lưới); Tổng (kg) ẩn thì tính lại từ Số lượng × Net
                var raw = keep is null ? new JsonObject() : ToStore(keep);
                if (keep is not null && !_keys.Contains("net")) raw.Remove("net");
                raw["loai"] = _loai; raw["ngay"] = ngay; raw["chungTu"] = (_ct.Text ?? "").Trim();
                var extra = keep is null ? new JsonObject() : (JsonObject)keep.Extra.DeepClone(); raw.Remove("extraJson");
                for (var c = 0; c < _keys.Length; c++)
                {
                    var v = _m.Get(r, c).Trim();
                    if (_keys[c].StartsWith("x_", StringComparison.Ordinal)) { if (v.Length > 0) extra[_keys[c]] = _def[_keys[c]].T == "number" && SoKhoCore.ParseNum(v) is { } xn && double.IsFinite(xn) ? xn : v; else extra.Remove(_keys[c]); }
                    else raw[_keys[c]] = v;
                }
                // công thức cột (trừ Tổng kg — sổ tự tính Số lượng × Net): ô để trống thì tự tính từ các cột khác (2 lượt cho công thức dùng cột có công thức)
                double? Get(string k) => k.StartsWith("x_", StringComparison.Ordinal) ? (extra[k] is JsonValue ev && ev.TryGetValue<double>(out var ed) ? ed : SoKhoCore.ParseNum(extra[k]?.ToString() ?? "")) : SoKhoCore.ParseNum(raw[k]?.ToString() ?? "");
                for (var pass = 0; pass < 2; pass++)
                    foreach (var fc in Svc.TemplateFor(_loai).Columns.Where(c => c.Formula is not null && c.Key != "net"))
                    {
                        var isX = fc.Key.StartsWith("x_", StringComparison.Ordinal);
                        var curV = isX ? extra[fc.Key]?.ToString() ?? "" : raw[fc.Key]?.ToString() ?? "";
                        if (curV.Trim().Length > 0) continue;
                        if (SkTemplate.Eval(fc.Formula, Get) is { } fv) { if (isX) extra[fc.Key] = fv; else raw[fc.Key] = fv.ToString(CultureInfo.InvariantCulture); }
                    }
                raw["extra"] = extra;
                if (_loai == "xuat" && S(raw["hopDong"]).Length == 0) raw["hopDong"] = (_hd.Text ?? "").Trim();
                if (r < _ids.Count) raw["id"] = _ids[r];
                if (keep is not null) { raw["nguon"] = keep.Nguon; raw["nguoi"] = keep.Nguoi; raw["luc"] = keep.Luc; raw["phieuId"] = keep.PhieuId; }
                o.Add(NormLine(raw));
            }
            return o;
        }
        private bool Check()
        {
            var d = Draft();
            if (d.Count == 0) { _msg.Text = "Phiếu chưa có dòng nào."; _msg.Foreground = AppTheme.Danger; return false; }
            var chk = Svc.CheckDraft(d, _cur?.Lines.Select(l => l.Id));
            var lines = chk.Rows.Where(r => r.Check.Level != "ok").Select(r => $"{(r.Check.Level == "err" ? "✖" : "⚠")} Dòng {r.Row + 1} ({d[r.Row].MaHang}): {string.Join("; ", r.Check.Msgs)}").ToList();
            lines.AddRange(chk.DinhMuc.Where(x => x.St != "ok").Select(x => x.St == "thieu" ? $"ⓘ {x.HopDong} · {x.MaHang}: {x.Why} — chưa kiểm được định mức." :
                $"{(x.St == "vuot" ? "✖" : "⚠")} Định mức {x.HopDong} · {x.MaHang}: dự kiến {F(x.Du)} {(x.DimKind == "net" ? "kg" : "bao")}, đã xuất {F(x.Da)}, phiếu này {F(x.Nay)} → {F(x.Sau)} ({StatusTen.GetValueOrDefault(x.St, x.St)}{(x.Pct is { } p ? $", {(p > 0 ? "+" : "")}{F(p)}%" : "")})"));
            lines.AddRange(chk.Spills.Select(s => $"⚠ {s.Line.MaHang} ({s.Line.ViTri}): lô hạn {Dmy(s.Spill.HanDung)} {(s.Spill.LoFound ? $"chỉ có {F(s.Spill.ExSl)} bao" : "không tìm thấy")} — {F(s.Spill.Sl)} bao lấy sang lô khác (FIFO)."));
            var tot = $"Phiếu {d.Count} dòng · {F(d.Sum(l => l.SoLuong ?? 0))} bao · {F(d.Sum(l => l.Net ?? 0))} kg.";
            _msg.Text = lines.Count == 0 ? "✓ " + tot + " Không có lỗi." : tot + "\n" + string.Join("\n", lines);
            _msg.Foreground = chk.HasError ? AppTheme.Danger : lines.Count > 0 ? AppTheme.Brush("#8A5A00") : AppTheme.Brush("#1E7B34");
            return !chk.HasError;
        }
        private async Task Save()
        {
            if (!Check()) return;
            if (_msg.Text?.Contains('⚠') == true || _msg.Text?.Contains("Định mức") == true)
                if (!await Ui.Confirm(this, "Phiếu có cảnh báo (xem bên dưới bảng). Vẫn lưu?", "Cảnh báo", "Vẫn lưu")) return;
            var d = Draft(); var ct = (_ct.Text ?? "").Trim(); var rep = _cur?.Lines.Select(l => l.Id).ToList();
            if (await Ui.RunAsync(this, () => Task.Run(() => Svc.SavePhieu(d, rep, _loai, ct))))
            {
                var key = ct.Length > 0 ? ct : "#" + IsoOf(_ngay);
                _cur = Groups(Svc.Lines, _loai).FirstOrDefault(g => g.Key == key);
                RaiseChanged();
                _msg.Text = "✓ Đã lưu phiếu " + ct + "."; _msg.Foreground = AppTheme.Brush("#1E7B34");
            }
        }
        private async Task Delete()
        {
            if (_cur is null) { New(); return; }
            if (!await Ui.Confirm(this, $"Xóa phiếu {_cur.Ct} ({_cur.Dong} dòng)? Hoàn tác được ở tab Sổ cái & lịch sử.", "Xóa phiếu", "Xóa")) return;
            var ids = _cur.Lines.Select(l => l.Id).ToList(); var ct = _cur.Ct;
            if (await Ui.RunAsync(this, () => Task.Run(() => Svc.DeleteLines(ids, "Xóa phiếu " + ct)))) { _cur = null; New(); RaiseChanged(); }
        }
        private Task Print()
        {
            var d = _cur?.Lines ?? Draft();
            if (d.Count == 0) return Ui.Message(this, "Phiếu chưa có dòng nào.");
            var path = Path.Combine(Path.GetTempPath(), $"phieu-{Ui.SafeFileName((_ct.Text ?? "phieu").Trim())}-{DateTime.Now:HHmmss}.html");
            File.WriteAllText(path, SoKhoService.PhieuHtml(d, (_ct.Text ?? "").Trim(), App.Services.Session.Username));
            Ui.OpenExternal(path);
            return Task.CompletedTask;
        }
        /// <summary>Thêm vào phiếu các lô đã chọn (SL > 0) vào dòng trống; trả về số dòng đã thêm.</summary>
        private int AddLots(IEnumerable<(SkLot L, double Sl)> picks)
        {
            var n = 0;
            foreach (var (l, sl) in picks)
            {
                var row = 0; while (row < _m.RowCount && _keys.Any(k => _m.Get(row, Array.IndexOf(_keys, k)).Trim().Length > 0)) row++;
                if (row >= _m.RowCount) _m.InsertRows(_m.RowCount, 1);
                var cells = new List<(int, int, string)>();
                void Set(string k, string v) { var c = Array.IndexOf(_keys, k); if (c >= 0) cells.Add((row, c, v)); }
                Set("viTri", l.ViTri); Set("maHang", l.MaHang); Set("size", l.Size); Set("hanDung", Dmy(l.HanDung)); Set("ngayNhap", Dmy(l.NgayNhap)); Set("soLuong", F(sl, 4));
                if (_loai == "xuat" && l.Sl > Eps && l.Net > Eps) Set("netDv", F(l.Net / l.Sl, 4));
                _m.SetCells(cells); n++;
            }
            _g.Refresh();
            return n;
        }

        private async Task OpenPicker()
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            Reload();
            var lots = _lots; // chụp tồn hiện tại
            var pm = new GridModel();
            var pg = new SheetGrid(pm) { SpareRows = 0, CheckCol = 8, CheckFill = r => r >= 0 && r < lots.Count ? F(lots[r].Sl, 4) : "" };
            pm.Load(new[] { new GridColumn("vt", "Vị trí", 70) { ReadOnly = true }, new GridColumn("ma", "Mã hàng", 150) { ReadOnly = true }, new GridColumn("sz", "Size", 60) { ReadOnly = true },
                new GridColumn("hd", "Hạn dùng", 85) { ReadOnly = true }, new GridColumn("nn", "Ngày nhập", 85) { ReadOnly = true }, new GridColumn("sl", "Tồn (bao)", 80, "number") { ReadOnly = true },
                new GridColumn("net", "Tồn (kg)", 85, "number") { ReadOnly = true }, new GridColumn("lsx", "Hợp đồng nhập", 120) { ReadOnly = true }, new GridColumn("pick", "SL " + Ten + " (bao)", 110, "number") },
                lots.Select(l => new[] { l.ViTri, l.MaHang, l.Size, Dmy(l.HanDung), Dmy(l.NgayNhap), F(l.Sl), F(l.Net), l.HopDong, "" }));
            pg.Refresh();
            var info = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            List<(SkLot, double)> Picks()
            {
                var o = new List<(SkLot, double)>();
                for (var r = 0; r < lots.Count && r < pm.RowCount; r++) if (SoKhoCore.ParseNum(pm.Get(r, 8)) is { } v && v > Eps) o.Add((lots[r], v));
                return o;
            }
            void Info() { var p = Picks(); info.Text = p.Count == 0 ? "Chưa chọn lô nào." : $"Đã chọn {p.Count} lô · {F(p.Sum(x => x.Item2))} bao · ≈ {F(p.Sum(x => x.Item1.Sl > Eps ? x.Item2 * x.Item1.Net / x.Item1.Sl : 0))} kg"; }
            pm.Changed += Info; Info();
            var done = new Button { Content = "Xong — đưa vào phiếu", Classes = { "primary" }, MinWidth = 170, HorizontalContentAlignment = HorizontalAlignment.Center };
            var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(done, Dock.Right); bar.Children.Add(done); bar.Children.Add(info);
            var hint = new TextBlock { Text = lots.Count == 0 ? "Sổ kho chưa có lô tồn nào để " + Ten + "." : "Tích ô đầu dòng để lấy hết tồn của lô, hoặc gõ SL ở cột cuối. Lọc bằng dòng tìm kiếm / nút ▾ ở tiêu đề cột.", Classes = { "muted" }, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
            var root = new DockPanel();
            DockPanel.SetDock(bar, Dock.Bottom); root.Children.Add(bar); DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint); root.Children.Add(Framed(pg));
            var scr = owner.Screens.ScreenFromWindow(owner)?.WorkingArea; var sc = owner.RenderScaling <= 0 ? 1 : owner.RenderScaling;
            var w = new Window
            {
                Title = "Chọn hàng từ tồn — tích ô đầu dòng (lấy hết) hoặc gõ SL " + Ten,
                Width = Math.Max(900, (scr?.Width ?? 1400) / sc * 0.92), Height = Math.Max(560, (scr?.Height ?? 860) / sc * 0.88),
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = AppTheme.Card, MinWidth = 700, MinHeight = 420,
                Content = new Border { Padding = new Thickness(14, 12), Child = root },
            };
            PickerWindow = w; PickerGrid = pm;
            done.Click += (_, _) => w.Close();
            w.Closed += (_, _) => { pm.Changed -= Info; PickerWindow = null; };
            await Ui.Show(this, w);
            var picks = Picks();
            if (picks.Count > 0) { var n = AddLots(picks); _msg.Text = $"✓ Đã đưa {n} lô vào phiếu — bấm Kiểm tra rồi Lưu phiếu."; _msg.Foreground = AppTheme.Brush("#1E7B34"); }
        }
        internal Window? PickerWindow { get; private set; }
        internal GridModel? PickerGrid { get; private set; }
        internal Task OpenPickerForTest() => OpenPicker();
    }

    // ================================================================== TỒN KHO

    private sealed class TonPanel : UserControl
    {
        private readonly ComboBox _mode = new() { MinWidth = 230, ItemsSource = new[] { "Tồn theo mã + size", "Tồn theo vị trí + mã", "Tồn theo lô (đầy đủ)", "NXT trong kỳ (mã + size)", "Sắp / đã hết hạn dùng", "Lô tồn âm" }, SelectedIndex = 0 };
        private readonly CalendarDatePicker _from = Date(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)), _to = Date();
        private readonly TextBox _q = new() { Watermark = "Lọc mã, vị trí, size…", Width = 200 };
        private readonly GridModel _m = new() { ReadOnly = true }; private readonly SheetGrid _g;
        private readonly TextBlock _sum = new() { FontSize = 12.5, FontWeight = FontWeight.SemiBold };
        private List<string[]> _rows = new(); private string[] _head = Array.Empty<string>(); private List<string> _bg = new();

        private readonly TextBlock _fromLb = Lb("Từ"), _toLb = Lb("Đến ngày");
        public TonPanel()
        {
            _g = new SheetGrid(_m) { SpareRows = 0, CellBackground = (r, _) => r < _bg.Count ? _bg[r] switch { "neg" => Red, "exp" => Yellow, "low" => Yellow, _ => null } : null };
            _mode.SelectionChanged += (_, _) => Paint(); _from.SelectedDateChanged += (_, _) => Paint(); _to.SelectedDateChanged += (_, _) => Paint(); _q.TextChanged += (_, _) => Paint();
            var bar = new ToolRow { };
            bar.Children.Add(_mode); bar.Children.Add(_fromLb); bar.Children.Add(_from); bar.Children.Add(_toLb); bar.Children.Add(_to); bar.Children.Add(_q);
            if (Svc.CanExport) bar.Children.Add(Btn("Xuất Excel", async () => { var p = await SaveXlsx(this, "TonKho_BotSot_" + IsoOf(_to)); if (p is not null) await Ui.Run(this, () => SoKhoService.ExportTable("Ton kho", _head, _rows.Select(r => r.Select(x => (object?)(double.TryParse(x.Replace(",", ""), NumberStyles.Float, Ic, out var d) && x.Length > 0 && char.IsDigit(x[^1]) && !x.Contains('/') ? d : x)).ToArray()), p)); }));
            if (Svc.CanEdit) bar.Children.Add(Btn("Chốt tồn đầu kỳ…", Chot, "Tồn cuối ngày trước → dòng Tồn đầu của ngày chọn; các báo cáo sau đó tính từ mốc này"));
            if (Svc.CanEdit) bar.Children.Add(Btn("📄 Nạp Excel Vị Trí Bột…", async () => { if (await VtbExcelUi.Load(this)) Paint(); }, "Nạp file Excel (Vị trí · Mã bột · Số lượng) vào ngăn Excel của Vị Trí Bột — nạp lại thì THAY ngăn Excel cũ, không cộng dồn; điện thoại xem ở Vị Trí Bột"));
            if (Svc.CanEdit) bar.Children.Add(Btn("📱 Đưa lên Vị Trí Bột…", PushVtb, "Thay dữ liệu Vị Trí Bột (điện thoại xem / xuất) bằng tồn theo vị trí đến ngày đang chọn"));
            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6) }; top.Children.Add(bar); top.Children.Add(_sum);
            var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(new DetailPane(Framed(_g), _g, "Chi tiết dòng"));   // 1.53
            Content = root;
        }

        private async Task PushVtb()
        {
            var to = IsoOf(_to); var rows = KhoLanh.Core.SoKho.VtbService.FromLedger(Svc.Lines, to);
            if (rows.Count == 0) { await Ui.Message(this, "Không có dòng tồn cuối > 0 để đẩy (kiểm tra lại ngày)."); return; }
            var v = App.Services.Vtb; try { v.Load(); } catch { }
            var msg = $"Đưa {rows.Count} dòng tồn theo vị trí (tồn cuối đến ngày {Dmy(to)} · SL {F(rows.Sum(r => r.Sl))} · {F(rows.Sum(r => r.Net))} kg) lên Vị Trí Bột?"
                + (v.Rows.Count > 0 ? $"\n\nNgăn NXT hiện có ({v.Rows.Count(r => !r.IsExcel)} dòng) sẽ bị THAY bằng số liệu này; các phiếu xuất đã tạo trên ngăn NXT sẽ bị khóa. Ngăn Excel ({v.Rows.Count(r => r.IsExcel)} dòng) giữ nguyên." : "");
            if (!await Ui.Confirm(this, msg, "Đưa lên Vị Trí Bột", "Đưa lên")) return;
            if (!await Ui.Run(this, () => v.Push(rows, to))) return;
            // như app cũ (pushViTriBot): đã kết nối điện thoại thì đẩy luôn tồn mới lên điện thoại
            var ph = App.Services.Phone; var extra = "";
            if (ph.Enabled)
            {
                try { var r = await v.SyncPhoneAsync(ph, pull: false); extra = $"\n\nĐã đẩy {r.Pushed} dòng tồn lên điện thoại."; }
                catch (Exception e) { extra = "\n\nChưa đẩy được lên điện thoại: " + e.Message + " — vào Vị Trí Bột › 📱 Đồng bộ điện thoại để thử lại."; }
            }
            await Ui.Message(this, $"Đã đưa {rows.Count} dòng tồn theo vị trí lên Vị Trí Bột (Tồn kho › Vị Trí Bột)." + extra);
        }

        public void Paint()
        {
            var L = Svc.Lines; var to = IsoOf(_to); var q = Core.ChungTu.ChungTuService.StripAccents(_q.Text ?? ""); _bg = new();
            bool Hit(params string[] s) => q.Length == 0 || Core.ChungTu.ChungTuService.StripAccents(string.Join(" ", s)).Contains(q);
            var mode = _mode.SelectedIndex; var cols = new List<GridColumn>();
            _from.IsVisible = _fromLb.IsVisible = mode == 3; _toLb.Text = mode == 3 ? "đến" : "Đến ngày";
            if (mode is 0 or 1)
            {
                var dims = mode == 0 ? new[] { "maHang", "size" } : new[] { "viTri", "maHang", "size" };
                var rows = Stock(L, to, dims).Where(r => Hit(dims.Select(d => r[d]).ToArray())).ToList();
                _head = dims.Select(d => DimLabel[d]).Concat(new[] { "Tồn (bao)", "Tồn (kg)", "Số lô" }).ToArray();
                _rows = rows.Select(r => dims.Select(d => r[d]).Concat(new[] { F(r.Sl), F(r.Net), r.Lots.ToString() }).ToArray()).ToList();
                _bg = rows.Select(r => r.Sl < -Eps || r.Net < -Eps ? "neg" : mode == 0 && Svc.MinStock.TryGetValue(r["maHang"], out var mn) && r.Sl < mn ? "low" : "").ToList();
                _sum.Text = $"Tồn đến hết {Dmy(to)}: {rows.Count} dòng · {F(rows.Sum(r => r.Sl))} bao · {F(rows.Sum(r => r.Net))} kg";
            }
            else if (mode is 2 or 4 or 5)
            {
                var lots = Compute(L, to).Lots.Where(l => Hit(l.ViTri, l.MaHang, l.Size, l.HopDong));
                if (mode == 4) lots = lots.Where(l => (l.Sl > Eps || l.Net > Eps) && l.HanDung.Length > 0 && string.CompareOrdinal(l.HanDung, AddDays(Iso(DateTime.Today), Svc.HsdDays)) <= 0).OrderBy(l => l.HanDung);
                if (mode == 5) lots = lots.Where(l => l.Sl < -Eps || l.Net < -Eps);
                var ls = lots.ToList();
                _head = new[] { "Vị trí", "Mã hàng", "Size", "Hạn dùng", "Ngày nhập", "Hợp đồng nhập", "Chứng từ nhập", "Tồn (bao)", "Tồn (kg)" };
                _rows = ls.Select(l => new[] { l.ViTri, l.MaHang, l.Size, Dmy(l.HanDung), Dmy(l.NgayNhap), l.HopDong, l.ChungTu, F(l.Sl), F(l.Net) }).ToList();
                var today = Iso(DateTime.Today);
                _bg = ls.Select(l => l.Sl < -Eps || l.Net < -Eps ? "neg" : l.HanDung.Length > 0 && string.CompareOrdinal(l.HanDung, AddDays(today, Svc.HsdDays)) <= 0 ? "exp" : "").ToList();
                _sum.Text = (mode == 4 ? $"Lô hết hạn trong {Svc.HsdDays} ngày tới (đỏ nhạt = tồn âm, vàng = sắp / đã hết hạn): " : mode == 5 ? "Lô tồn âm (xuất nhiều hơn đã nhập — thiếu dòng nhập / tồn đầu, xuất nhầm mã…): " : "Tồn theo lô: ")
                    + $"{ls.Count} lô · {F(ls.Sum(l => l.Sl))} bao · {F(ls.Sum(l => l.Net))} kg";
            }
            else
            {
                var from = IsoOf(_from); var rows = Nxt(L, from, to, "maHang", "size").Where(r => Hit(r["maHang"], r["size"])).ToList(); var t = NxtTotal(rows);
                _head = new[] { "Mã hàng", "Size", "Đầu kỳ (bao)", "Đầu kỳ (kg)", "Nhập (bao)", "Nhập (kg)", "Xuất (bao)", "Xuất (kg)", "Điều chỉnh (bao)", "Điều chỉnh (kg)", "Cuối kỳ (bao)", "Cuối kỳ (kg)" };
                _rows = rows.Select(r => new[] { r["maHang"], r["size"], F(r.DauSl), F(r.DauNet), F(r.NhapSl), F(r.NhapNet), F(r.XuatSl), F(r.XuatNet), F(r.DcSl), F(r.DcNet), F(r.CuoiSl), F(r.CuoiNet) }).ToList();
                _rows.Add(new[] { "TỔNG", "", F(t.DauSl), F(t.DauNet), F(t.NhapSl), F(t.NhapNet), F(t.XuatSl), F(t.XuatNet), F(t.DcSl), F(t.DcNet), F(t.CuoiSl), F(t.CuoiNet) });
                _bg = rows.Select(r => r.CuoiSl < -Eps || r.CuoiNet < -Eps ? "neg" : "").Append("").ToList();
                _sum.Text = $"NXT từ {Dmy(from)} đến {Dmy(to)}: Đầu kỳ + Nhập − Xuất ± Điều chỉnh (chuyển vị trí, kiểm kê, chốt tồn) = Cuối kỳ.";
            }
            _m.Load(_head.Select((h, i) => new GridColumn("c" + i, h, i < 3 && mode != 3 ? 120 : 95, h.Contains("(bao)") || h.Contains("(kg)") || h == "Số lô" ? "number" : "text")), _rows);
            _g.Refresh();
        }

        private async Task Chot()
        {
            var s = await Ui.Prompt(this, "Chốt tồn cuối kỳ: tồn cuối ngày TRƯỚC ngày này thành dòng Tồn đầu của ngày này (dd/mm/yyyy):", "Chốt tồn", Dmy(Iso(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1))));
            if (s is null) return; var d = ParseDate(s);
            if (d.Length == 0) { await Ui.Message(this, "Ngày không hợp lệ."); return; }
            (SoKhoService.CommitResult?, int, bool) r = default;
            if (!await Ui.RunAsync(this, () => Task.Run(() => r = Svc.Snapshot(d)))) return;
            await Ui.Message(this, r.Item3 ? $"Ngày {Dmy(d)} đã có dòng Tồn đầu — không tạo thêm (tránh nhân đôi tồn)." : $"Đã chốt {r.Item1?.Added ?? 0} lô thành Tồn đầu ngày {Dmy(d)}." + (r.Item2 > 0 ? $"\n⚠ {r.Item2} lô đang ÂM không chuyển sang kỳ sau — kiểm tra lại các lô đó." : ""));
            RaiseChanged();
        }
    }

    // ================================================================== KIỂM KÊ

    private sealed class KiemKePanel : UserControl
    {
        private readonly CalendarDatePicker _ngay = Date();
        private readonly GridModel _m = new(); private readonly SheetGrid _g;
        private readonly GridModel _rm = new() { ReadOnly = true }; private readonly SheetGrid _rg;
        private readonly TextBlock _msg = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        private List<SkLine> _adj = new(); private List<KkRow> _res = new();
        private readonly TabControl _sub;

        public KiemKePanel()
        {
            _g = new SheetGrid(_m) { SpareRows = 10 };
            _rg = new SheetGrid(_rm) { SpareRows = 0, CellBackground = (r, _) => r < _res.Count ? _res[r].Kind switch { "khop" => Green, "thieu" => Red, _ => Yellow } : null };
            _ngay.SelectedDateChanged += (_, _) => Paint();
            var bar = new ToolRow { };
            bar.Children.Add(Lb("Ngày kiểm kê")); bar.Children.Add(_ngay);
            bar.Children.Add(Btn("Tính chênh lệch", Calc, primary: true));
            if (Svc.CanEdit) bar.Children.Add(Btn("Ghi điều chỉnh vào sổ", Apply, "Mỗi dòng lệch thành 1 dòng Điều chỉnh (dương = thêm, âm = bớt) ngày kiểm kê"));
            bar.Children.Add(Btn("Tải lại tồn sổ", Paint));
            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6) }; top.Children.Add(bar);
            top.Children.Add(new TextBlock { Text = "Gõ / dán SỐ ĐẾM thực tế vào cột cuối (để trống = chưa đếm, bỏ qua). Thêm dòng cho hàng có ngoài sổ. Net điều chỉnh tính theo tỷ lệ kg / bao của tồn sổ.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
            grid.Children.Add(top);
            Grid.SetRow(_msg, 1); _msg.Margin = new Thickness(0, 0, 0, 6); grid.Children.Add(_msg);
            _sub = Ui.SubTabs(("Số đếm thực tế", Framed(_g)), ("Kết quả chênh lệch", Framed(_rg)));
            Grid.SetRow(_sub, 2); grid.Children.Add(_sub);
            Content = grid;
        }
        public void Paint()
        {
            var st = Stock(Svc.Lines, IsoOf(_ngay), "maHang", "size", "viTri").Where(r => Math.Abs(r.Sl) > Eps || Math.Abs(r.Net) > Eps).ToList();
            _m.Load(new[] { new GridColumn("ma", "Mã hàng", 150), new GridColumn("sz", "Size", 70), new GridColumn("vt", "Vị trí", 80), new GridColumn("sl", "Tồn sổ (bao)", 95, "number") { ReadOnly = true },
                new GridColumn("kg", "Tồn sổ (kg)", 95, "number") { ReadOnly = true }, new GridColumn("dem", "SỐ ĐẾM (bao)", 110, "number") },
                st.Select(r => new[] { r["maHang"], r["size"], r["viTri"], F(r.Sl), F(r.Net), "" }));
            _g.Refresh(); _res = new(); _adj = new(); _rm.Load(Array.Empty<GridColumn>(), Array.Empty<string[]>()); _rg.Refresh(); _msg.Text = "";
        }
        private void Calc()
        {
            var counts = new List<KkCount>();
            for (var r = 0; r < _m.RowCount; r++) if (_m.Get(r, 0).Trim().Length > 0 && ParseNum(_m.Get(r, 5)) is { } d && !double.IsNaN(d)) counts.Add(new KkCount(_m.Get(r, 0), _m.Get(r, 1), _m.Get(r, 2), d));
            var k = KiemKePlan(Svc.Lines, IsoOf(_ngay), counts, App.Services.Session.Username);
            _res = k.Rows; _adj = k.Adj;
            _rm.Load(new[] { new GridColumn("ma", "Mã hàng", 150), new GridColumn("sz", "Size", 70), new GridColumn("vt", "Vị trí", 80), new GridColumn("so", "Sổ", 80, "number"), new GridColumn("dem", "Đếm", 80, "number"),
                new GridColumn("ch", "Chênh (bao)", 90, "number"), new GridColumn("net", "Chênh (kg)", 90, "number"), new GridColumn("kq", "Kết quả", 160) },
                _res.Select(r => new[] { r.MaHang, r.Size, r.ViTri, F(r.So), F(r.Dem), F(r.Chenh), F(r.Net), r.Kind switch { "khop" => "Khớp", "thua" => "Thừa", "moi" => r.ThieuNet ? "Có ngoài sổ — thiếu Net" : "Có ngoài sổ", _ => "Thiếu" } }));
            _rg.Refresh();
            if (counts.Count > 0) _sub.SelectedIndex = 1;
            _msg.Text = counts.Count == 0 ? "Chưa có số đếm nào." : $"{k.NKhop} khớp · {k.NLech} lệch (thừa {F(k.Thua)} bao, thiếu {F(-k.Thieu)} bao). " + (k.NLech > 0 ? "Bấm «Ghi điều chỉnh vào sổ» để cập nhật tồn." : "");
        }
        private async Task Apply()
        {
            if (_adj.Count == 0) { Calc(); if (_adj.Count == 0) { await Ui.Message(this, "Không có dòng lệch để ghi."); return; } }
            if (!await Ui.Confirm(this, $"Ghi {_adj.Count} dòng Điều chỉnh ngày {Dmy(IsoOf(_ngay))} vào sổ cái?", "Kiểm kê", "Ghi")) return;
            var adj = _adj.ToList(); var d = IsoOf(_ngay);
            if (await Ui.RunAsync(this, () => Task.Run(() => Svc.ApplyKiemKe(adj, d)))) { RaiseChanged(); _msg.Text = $"✓ Đã ghi {adj.Count} dòng điều chỉnh."; }
        }
    }

    // ================================================================== ĐỊNH MỨC

    private sealed class DinhMucPanel : UserControl
    {
        private readonly GridModel _nm = new(), _om = new(), _rm = new() { ReadOnly = true }, _mm = new() { ReadOnly = true };
        private readonly SheetGrid _ng, _og, _rg, _mg;
        private readonly CalendarDatePicker _from = Date(new DateTime(DateTime.Today.Year, 1, 1)), _to = Date();
        private List<DmRow> _rows = new();
        public DinhMucPanel()
        {
            _ng = new SheetGrid(_nm) { SpareRows = 8 }; _og = new SheetGrid(_om) { SpareRows = 8 }; _mg = new SheetGrid(_mm) { SpareRows = 0 };
            _rg = new SheetGrid(_rm) { SpareRows = 0, CellBackground = (r, _) => r < _rows.Count ? _rows[r].Status switch { "vuot" => Red, "chuY" => Yellow, "dat" or "tietkiem" => Green, _ => null } : null };
            _from.SelectedDateChanged += (_, _) => Report(); _to.SelectedDateChanged += (_, _) => Report();
            // 1.57: 4 bảng thành 4 tab con (trước xếp chồng 2 × 2 — màn hình nhỏ mỗi bảng chỉ vài dòng).
            Control Page(string note, Control grid, Control? act)
            {
                var dp = new DockPanel();
                var head = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 0, 0, 6) };
                if (act is not null) head.Children.Add(act);
                head.Children.Add(new TextBlock { Text = note, Tag = "keep", Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                DockPanel.SetDock(head, Dock.Top); dp.Children.Add(head); dp.Children.Add(Framed(grid));
                return dp;
            }
            var rbar = new ToolRow { };
            rbar.Children.Add(Lb("Từ")); rbar.Children.Add(_from); rbar.Children.Add(Lb("đến")); rbar.Children.Add(_to);
            Content = Ui.SubTabs(
                ("Báo cáo định mức", Page("Thực xuất bột / sốt theo LSX so với dự kiến", _rg, rbar)),
                ("Định mức", Page("LSX để trống = áp cho mọi LSX. Đơn vị: " + string.Join(" · ", DmUnits.Select(u => u.Key)), _ng, Svc.CanEdit ? Btn("💾 Lưu định mức", SaveNorms, primary: true) : null)),
                ("Sản lượng theo LSX", Page("Sản lượng thành phẩm theo LSX (kg)", _og, Svc.CanEdit ? Btn("💾 Lưu sản lượng", SaveOutputs, primary: true) : null)),
                ("LSX chưa có định mức", Page("LSX có xuất nhưng chưa có định mức cho mã đó", _mg, null)));
        }
        public void Paint()
        {
            _nm.Load(new[] { new GridColumn("hd", "LSX / Hợp đồng", 120), new GridColumn("ma", "Mã bột / sốt", 130), new GridColumn("sz", "Size", 60), new GridColumn("dm", "Định mức", 75, "number"), new GridColumn("dv", "Đơn vị", 85), new GridColumn("gc", "Ghi chú", 140) },
                Svc.Norms.Select(n => new[] { n.HopDong, n.MaHang, n.Size, F(n.DinhMuc, 4), n.DonVi, n.GhiChu }));
            _om.Load(new[] { new GridColumn("hd", "LSX / Hợp đồng", 130), new GridColumn("tp", "Thành phẩm (kg)", 110, "number"), new GridColumn("kh", "Khách", 120), new GridColumn("gc", "Ghi chú", 140) },
                Svc.Outputs.Select(o => new[] { o.HopDong, F(o.ThanhPham), o.Khach, o.GhiChu }));
            _ng.Refresh(); _og.Refresh(); Report();
        }
        private void Report()
        {
            var (rows, miss) = DinhMuc(Svc.Lines, Svc.Norms, Svc.Outputs, Svc.Tol, IsoOf(_from), IsoOf(_to)); _rows = rows;
            _rm.Load(new[] { new GridColumn("hd", "LSX", 110), new GridColumn("kh", "Khách", 100), new GridColumn("tp", "Thành phẩm (kg)", 100, "number"), new GridColumn("ma", "Mã", 110), new GridColumn("dm", "Định mức", 110),
                new GridColumn("du", "Dự kiến", 80, "number"), new GridColumn("tx", "Thực xuất", 80, "number"), new GridColumn("ch", "Chênh", 75, "number"), new GridColumn("pct", "%", 60, "number"), new GridColumn("st", $"Đánh giá (dung sai {F(Svc.Tol)}%)", 150) },
                rows.Select(r => new[] { r.HopDong, r.Khach, F(r.ThanhPham), r.MaHang + (r.Size.Length > 0 ? " " + r.Size : ""), F(r.DinhMuc, 4) + " " + r.DonVi, F(r.DuKien), F(r.ThucXuat), F(r.Chenh), F(r.Pct), StatusTen.GetValueOrDefault(r.Status, r.Status) }));
            _mm.Load(new[] { new GridColumn("hd", "LSX", 120), new GridColumn("kh", "Khách", 110), new GridColumn("tp", "Thành phẩm (kg)", 110, "number"), new GridColumn("ma", "Mã", 120), new GridColumn("sz", "Size", 60), new GridColumn("sl", "Đã xuất (bao)", 95, "number"), new GridColumn("kg", "Đã xuất (kg)", 95, "number") },
                miss.Select(m => new[] { m.HopDong, m.Khach, F(m.ThanhPham), m.MaHang, m.Size, F(m.ThucXuatSl), F(m.ThucXuatNet) }));
            _rg.Refresh(); _mg.Refresh();
        }
        private async Task SaveNorms()
        {
            var rows = new List<Norm>();
            for (var r = 0; r < _nm.RowCount; r++) if (_nm.Get(r, 1).Trim().Length > 0) rows.Add(new Norm(r < Svc.Norms.Count ? Svc.Norms[r].Id : "", _nm.Get(r, 0), _nm.Get(r, 1), _nm.Get(r, 2), ParseNum(_nm.Get(r, 3)) is { } d && !double.IsNaN(d) ? d : null, _nm.Get(r, 4), _nm.Get(r, 5)));
            if (await Ui.Run(this, () => Svc.SaveNorms(rows))) Paint();
        }
        private async Task SaveOutputs()
        {
            var rows = new List<Output>();
            for (var r = 0; r < _om.RowCount; r++) if (_om.Get(r, 0).Trim().Length > 0) rows.Add(new Output(r < Svc.Outputs.Count ? Svc.Outputs[r].Id : "", _om.Get(r, 0), ParseNum(_om.Get(r, 1)) is { } d && !double.IsNaN(d) ? d : null, _om.Get(r, 2), _om.Get(r, 3)));
            if (await Ui.Run(this, () => Svc.SaveOutputs(rows))) Paint();
        }
    }

    // ================================================================== SỔ CÁI & LỊCH SỬ

    private sealed class SoCaiPanel : UserControl
    {
        private readonly CalendarDatePicker _from = Date(DateTime.Today.AddDays(-30)), _to = Date();
        private readonly ComboBox _loai = new() { ItemsSource = new[] { "Mọi loại" }.Concat(LoaiTen.Values).ToList(), SelectedIndex = 0, MinWidth = 130 };
        private readonly TextBox _q = new() { Watermark = "Tìm mã, chứng từ, LSX, ghi chú…", Width = 210 };
        private readonly GridModel _m = new() { ReadOnly = true }, _hm = new() { ReadOnly = true }; private readonly SheetGrid _g, _hg;
        private List<SkLine> _shown = new(); private List<Op> _ops = new();
        public SoCaiPanel()
        {
            _g = new SheetGrid(_m) { SpareRows = 0 }; _hg = new SheetGrid(_hm) { SpareRows = 0 };
            _from.SelectedDateChanged += (_, _) => Paint(); _to.SelectedDateChanged += (_, _) => Paint(); _loai.SelectionChanged += (_, _) => Paint(); _q.TextChanged += (_, _) => Paint();
            var bar = new ToolRow { };
            bar.Children.Add(Lb("Từ")); bar.Children.Add(_from); bar.Children.Add(Lb("đến")); bar.Children.Add(_to); bar.Children.Add(_loai); bar.Children.Add(_q);
            if (Svc.CanEdit) { bar.Children.Add(Btn("Xóa dòng đang chọn", DelSel)); bar.Children.Add(Btn("📥 Nạp từ Excel…", Import, "File có dòng tiêu đề: Ngày · Loại · Vị trí · Mã hàng · Size · Số lượng · Net · Hợp đồng · Hạn dùng · Ngày nhập · Ghi chú")); }
            if (Svc.CanExport) bar.Children.Add(Btn("Xuất Excel", async () => { var p = await SaveXlsx(this, $"SoCai_BotSot_{IsoOf(_from)}_{IsoOf(_to)}"); if (p is not null) await Ui.Run(this, () => Svc.ExportLedger(_shown, p)); }));
            var hb = new WrapPanel { ItemSpacing = 8, Margin = new Thickness(0, 8, 0, 4) };
            hb.Children.Add(new TextBlock { Text = "Hoàn tác chỉ khi các dòng chưa bị sửa tiếp", Tag = "keep", Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            if (Svc.CanEdit) hb.Children.Add(Btn("↶ Hoàn tác thao tác đang chọn", Undo));
            // 1.57: sổ cái và lịch sử thao tác thành 2 tab con.
            var ledger = new DockPanel(); bar.Margin = new Thickness(0, 0, 0, 6); DockPanel.SetDock(bar, Dock.Top);
            ledger.Children.Add(bar); ledger.Children.Add(new DetailPane(Framed(_g), _g, "Chi tiết dòng sổ"));   // 1.53
            var hist = new DockPanel(); hb.Margin = new Thickness(0, 0, 0, 6); DockPanel.SetDock(hb, Dock.Top);
            hist.Children.Add(hb); hist.Children.Add(Framed(_hg));
            Content = Ui.SubTabs(("Sổ cái", ledger), ($"Lịch sử thao tác ({HistDays} ngày)", hist));
        }
        public void Paint()
        {
            var loai = _loai.SelectedIndex > 0 ? LoaiTen.Keys.ElementAt(_loai.SelectedIndex - 1) : null;
            var q = Core.ChungTu.ChungTuService.StripAccents(_q.Text ?? "");
            _shown = SortLines(Svc.Lines.Where(l => string.CompareOrdinal(l.Ngay, IsoOf(_from)) >= 0 && string.CompareOrdinal(l.Ngay, IsoOf(_to)) <= 0 && (loai is null || l.Loai == loai)
                && (q.Length == 0 || Core.ChungTu.ChungTuService.StripAccents($"{l.MaHang} {l.ChungTu} {l.HopDong} {l.GhiChu} {l.ViTri}").Contains(q)))).ToList();
            // cột phụ (x_…) của mọi mẫu cột — như sổ chi tiết của app cũ
            var extraCols = Svc.Templates.SelectMany(t => t.Columns).Where(c => !c.Builtin).GroupBy(c => c.Key).Select(g => g.First()).ToList();
            static string Ex(SkLine l, string k) => l.Extra[k] is { } xv ? (xv is JsonValue jv && jv.TryGetValue<double>(out var xd) ? F(xd) : xv.ToString()) : "";
            _m.Load(new[] { new GridColumn("ngay", "Ngày", 80), new GridColumn("loai", "Loại", 90), new GridColumn("ct", "Chứng từ", 100), new GridColumn("vt", "Vị trí", 70), new GridColumn("vd", "→ Vị trí", 65),
                new GridColumn("ma", "Mã hàng", 130), new GridColumn("sz", "Size", 60), new GridColumn("sl", "SL (bao)", 70, "number"), new GridColumn("net", "Tổng (kg)", 80, "number"), new GridColumn("hd", "LSX / HĐ", 110),
                new GridColumn("han", "Hạn dùng", 80), new GridColumn("nn", "Ngày nhập", 80), new GridColumn("gc", "Ghi chú", 180), new GridColumn("ng", "Nguồn · người", 110) }
                .Concat(extraCols.Select(c => new GridColumn(c.Key, c.Label, Math.Min(Math.Max(c.W * 0.8, 70), 200), c.Type == "number" ? "number" : "text"))),
                _shown.Select(l => new[] { Dmy(l.Ngay), LoaiTen.GetValueOrDefault(l.Loai, l.Loai), l.ChungTu, l.ViTri, l.ViTriDen, l.MaHang, l.Size, F(l.SoLuong), F(l.Net), l.HopDong, Dmy(l.HanDung), Dmy(l.NgayNhap), l.GhiChu, l.Nguon + (l.Nguoi.Length > 0 ? " · " + l.Nguoi : "") }
                    .Concat(extraCols.Select(c => Ex(l, c.Key))).ToArray()));
            _ops = HistOps(Svc.Hist);
            _hm.Load(new[] { new GridColumn("luc", "Lúc", 120), new GridColumn("ng", "Người", 90), new GridColumn("mt", "Thao tác", 360), new GridColumn("t", "Thêm", 55, "number"), new GridColumn("s", "Sửa", 50, "number"), new GridColumn("x", "Xóa", 50, "number"), new GridColumn("u", "Trạng thái", 110) },
                _ops.Select(o => new[] { DateTime.TryParse(o.Luc, Ic, DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("dd/MM HH:mm:ss") : o.Luc, o.Nguoi, o.MoTa, o.Them.ToString(), o.Sua.ToString(), o.Xoa.ToString(), o.Undone ? "Đã hoàn tác" : "" }));
            _g.Refresh(); _hg.Refresh();
        }
        private async Task DelSel()
        {
            var sel = _g.Selection; var ids = Enumerable.Range(sel.R1, sel.Rows).Where(r => r >= 0 && r < _shown.Count).Select(r => _shown[r].Id).ToList();
            if (ids.Count == 0) { await Ui.Message(this, "Bôi chọn các dòng cần xóa trong bảng sổ cái."); return; }
            if (!await Ui.Confirm(this, $"Xóa {ids.Count} dòng sổ? Hoàn tác được trong 30 ngày.", "Xóa dòng", "Xóa")) return;
            if (await Ui.RunAsync(this, () => Task.Run(() => Svc.DeleteLines(ids, $"Xóa {ids.Count} dòng ở sổ cái")))) RaiseChanged();
        }
        private async Task Import()
        {
            var path = await Ui.OpenFile(this, "Chọn file Excel sổ kho", "xlsx", "Excel"); if (path is null) return;
            var ch = await Ui.Choose(this, "Dòng nào không có cột Loại thì coi là:", "Nạp Excel", LoaiTen.Values.ToList(), 1);
            if (ch is null) return; var loai = LoaiTen.Keys.ElementAt(ch.Value.Index);
            SoKhoService.CommitResult? r = null;
            if (await Ui.RunAsync(this, () => Task.Run(() => r = Svc.ImportExcel(path, loai)))) { await Ui.Message(this, $"Đã nạp {r!.Added} dòng vào sổ cái. Hoàn tác được ở bảng lịch sử."); RaiseChanged(); }
        }
        private async Task Undo()
        {
            var r = _hg.Row; if (r < 0 || r >= _ops.Count) { await Ui.Message(this, "Chọn một thao tác trong bảng lịch sử."); return; }
            var op = _ops[r]; if (!await Ui.Confirm(this, $"Hoàn tác «{op.MoTa}»?", "Hoàn tác", "Hoàn tác")) return;
            (int, List<(string, string)>) res = default;
            if (!await Ui.RunAsync(this, () => Task.Run(() => res = Svc.Undo(op.OpId)))) return;
            await Ui.Message(this, $"Đã hoàn tác {res.Item1} dòng." + (res.Item2.Count > 0 ? $"\n{res.Item2.Count} dòng giữ nguyên vì: " + string.Join("; ", res.Item2.Take(5).Select(c => c.Item2)) : ""));
            RaiseChanged();
        }
    }

    // ================================================================== CẤU HÌNH

    private sealed class CauHinhPanel : UserControl
    {
        private readonly NumericUpDown _tol = new() { Minimum = 0, Maximum = 50, Increment = 0.5m, Width = 140, FormatString = "0.##" }, _hsd = new() { Minimum = 0, Maximum = 365, Width = 140, FormatString = "0" };
        private readonly GridModel _mm = new(); private readonly SheetGrid _mg;
        public CauHinhPanel()
        {
            _mg = new SheetGrid(_mm) { SpareRows = 10, Height = 320 };
            var s = new StackPanel { Spacing = 6, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8) };
            s.Children.Add(Lb("Dung sai định mức (%) — chênh trong khoảng này coi là Đạt")); s.Children.Add(_tol);
            s.Children.Add(Lb("Báo hạn dùng trước (ngày)")); s.Children.Add(_hsd);
            s.Children.Add(Lb("Tồn tối thiểu theo mã (bao) — dưới mức hiện vàng ở Tồn kho")); s.Children.Add(Framed(_mg));
            if (Svc.CanEdit) s.Children.Add(Btn("💾 Lưu cấu hình", Save, primary: true));
            // ---- mẫu cột phiếu (SoKhoMau)
            _tg = new SheetGrid(_tm) { SpareRows = 0, Height = 330, CellBackground = (r, _) => r < _tplCols.Count && _tplCols[r].Hidden ? HiddenBg : null };
            s.Children.Add(new TextBlock { Text = "Mẫu cột phiếu", FontSize = 15, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 14, 0, 0) });
            s.Children.Add(Muted("Đổi tên / ẩn / sắp xếp cột của lưới phiếu nhập · xuất · chuyển, thêm cột phụ (ghi vào sổ cái, xem ở tab Sổ cái). Mã hàng luôn hiện. Dùng chung sheet SoKhoMau với app cũ."));
            _tplLoai.SelectionChanged += (_, _) => { if (!_painting) PaintTpl(); };
            var row1 = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 }; row1.Children.Add(Lb("Áp dụng cho")); row1.Children.Add(_tplLoai); row1.Children.Add(_tplInfo);
            s.Children.Add(row1); s.Children.Add(Framed(_tg));
            if (Svc.CanEdit)
            {
                var acts = new ToolRow { };
                acts.Children.Add(Btn("▲ Lên", () => Move(-1))); acts.Children.Add(Btn("▼ Xuống", () => Move(1)));
                acts.Children.Add(Btn("+ Thêm cột phụ…", AddExtra)); acts.Children.Add(Btn("Xóa cột đang chọn", DelCol));
                acts.Children.Add(Btn("Về mẫu cơ bản", () => { _tplCols = SkTemplate.Default().Columns; LoadTplGrid(); }));
                acts.Children.Add(Btn("💾 Lưu mẫu cột", SaveTpl, primary: true));
                s.Children.Add(acts);
            }
            Content = new ScrollViewer { Content = s };
        }

        private static readonly IBrush HiddenBg = AppTheme.Brush("#EEF1F5");
        private static readonly (string Loai, string Ten)[] TplLoais = { ("", "Mẫu chung (mọi loại phiếu)"), ("nhap", "Phiếu nhập"), ("xuat", "Phiếu xuất"), ("chuyen", "Chuyển vị trí") };
        private readonly ComboBox _tplLoai = new() { ItemsSource = TplLoais.Select(x => x.Ten).ToList(), SelectedIndex = 0, MinWidth = 220 };
        private readonly TextBlock _tplInfo = new() { FontSize = 12, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
        private readonly GridModel _tm = new(); private readonly SheetGrid _tg;
        private List<SkCol> _tplCols = new(); private List<string> _tplRemoved = new();
        private bool _painting;
        private string TplLoai => TplLoais[Math.Max(0, _tplLoai.SelectedIndex)].Loai;
        private SkTemplate? Existing() => TplLoai.Length == 0 ? Svc.Templates.FirstOrDefault(t => t.MacDinh) ?? Svc.Templates.FirstOrDefault(t => t.Loai.Length == 0) : Svc.Templates.FirstOrDefault(t => t.Loai == TplLoai);

        private void PaintTpl()
        {
            var ex = Existing(); var src = ex ?? Svc.TemplateFor(TplLoai);
            _tplCols = src.Columns.Select(c => c.Clone()).ToList(); _tplRemoved = src.Removed.ToList();
            _tplInfo.Text = ex is not null ? $"Đang dùng mẫu \"{ex.Ten}\"." : $"Chưa có mẫu riêng — đang dùng \"{src.Ten}\"; lưu để tạo mẫu riêng cho {TplLoais[_tplLoai.SelectedIndex].Ten.ToLowerInvariant()}.";
            LoadTplGrid();
        }
        private void LoadTplGrid()
        {
            var ro = !Svc.CanEdit;
            _tm.Load(new[] { new GridColumn("key", "Khóa", 110) { ReadOnly = true }, new GridColumn("label", "Tên cột", 200) { ReadOnly = ro }, new GridColumn("hidden", "Ẩn (gõ x)", 80) { ReadOnly = ro },
                new GridColumn("type", "Kiểu (cột phụ: chữ / số / ngày)", 190) { ReadOnly = ro }, new GridColumn("w", "Rộng", 70, "number") { ReadOnly = ro } },
                _tplCols.Select(c => new[] { c.Key, c.Label, c.Hidden ? "x" : "", c.Builtin ? (c.Type == "number" ? "số (dựng sẵn)" : c.Type == "date" ? "ngày (dựng sẵn)" : "chữ (dựng sẵn)") : c.Type == "number" ? "số" : c.Type == "date" ? "ngày" : "chữ", c.W.ToString("0", CultureInfo.InvariantCulture) }));
            _tg.Refresh();
        }
        /// <summary>Đọc lại lưới vào danh sách cột (tên, ẩn, kiểu, rộng).</summary>
        private void ReadTplGrid()
        {
            for (var r = 0; r < _tplCols.Count && r < _tm.RowCount; r++)
            {
                var c = _tplCols[r]; var lb = _tm.Get(r, 1).Trim(); if (lb.Length > 0) c.Label = lb;
                c.Hidden = c.Key != "maHang" && _tm.Get(r, 2).Trim().Length > 0;
                if (!c.Builtin) { var t = KhoLanh.Core.TimNhanh.Gs.Norm(_tm.Get(r, 3)); c.Type = t.StartsWith("so") || t.StartsWith("number") ? "number" : t.StartsWith("ngay") || t.StartsWith("date") ? "date" : "text"; }
                if (ParseNum(_tm.Get(r, 4)) is { } w && w >= 40 && w <= 600) c.W = w;
            }
        }
        private void Move(int d)
        {
            ReadTplGrid(); var r = _tg.Row; var n = r + d; if (r < 0 || r >= _tplCols.Count || n < 0 || n >= _tplCols.Count) return;
            (_tplCols[r], _tplCols[n]) = (_tplCols[n], _tplCols[r]); LoadTplGrid(); _tg.Select(n, 1);
        }
        private async Task AddExtra()
        {
            ReadTplGrid();
            var name = await Ui.Prompt(this, "Tên cột phụ (VD: Số xe, Người giao):", "Thêm cột phụ"); if (string.IsNullOrWhiteSpace(name)) return;
            _tplCols.Add(new SkCol { Key = SkTemplate.NewExtraKey(name, _tplCols.Select(c => c.Key).Concat(Svc.Templates.SelectMany(t => t.Columns).Select(c => c.Key))), Label = name.Trim(), Type = "text", W = 140 });
            LoadTplGrid(); _tg.Select(_tplCols.Count - 1, 1);
        }
        private async Task DelCol()
        {
            ReadTplGrid(); var r = _tg.Row; if (r < 0 || r >= _tplCols.Count) return; var c = _tplCols[r];
            if (c.Key == "maHang") { await Ui.Message(this, "Không xóa được cột Mã hàng."); return; }
            if (!await Ui.Confirm(this, c.Builtin ? $"Xóa cột dựng sẵn \"{c.Label}\" khỏi mẫu? (Có thể thêm lại bằng \"Về mẫu cơ bản\".)" : $"Xóa cột phụ \"{c.Label}\"? Số liệu đã ghi trong sổ cái vẫn giữ.", yes: "Xóa")) return;
            _tplCols.RemoveAt(r); if (c.Builtin && !_tplRemoved.Contains(c.Key)) _tplRemoved.Add(c.Key); LoadTplGrid();
        }
        private async Task SaveTpl()
        {
            ReadTplGrid();
            var ex = Existing(); var list = Svc.Templates.Select(t => t).ToList();
            var cols = _tplCols.Select(c => { var o = new System.Text.Json.Nodes.JsonObject { ["key"] = c.Key, ["label"] = c.Label, ["type"] = c.Type, ["hidden"] = c.Hidden, ["w"] = c.W }; if (c.Type == "number") o["formula"] = c.Formula?.DeepClone(); return o; })
                .Concat(_tplRemoved.Where(k => _tplCols.All(c => c.Key != k)).Select(k => new System.Text.Json.Nodes.JsonObject { ["key"] = k, ["removed"] = true })).ToList();
            var t = SkTemplate.Norm(ex?.Id ?? "", ex?.Ten ?? (TplLoai.Length == 0 ? "Mẫu cơ bản" : "Mẫu " + TplLoais[_tplLoai.SelectedIndex].Ten.ToLowerInvariant()), TplLoai, ex?.MacDinh ?? TplLoai.Length == 0, cols);
            if (ex is not null) list[list.IndexOf(ex)] = t; else list.Add(t);
            if (await Ui.Run(this, () => Svc.SaveTemplates(list))) { PaintTpl(); RaiseChanged(); await Ui.Message(this, "Đã lưu mẫu cột — lưới phiếu dùng mẫu mới."); }
        }
        public void Paint()
        {
            _painting = true; PaintTpl(); _painting = false;
            _tol.Value = (decimal)Svc.Tol; _hsd.Value = Svc.HsdDays;
            _mm.Load(new[] { new GridColumn("ma", "Mã hàng", 180), new GridColumn("min", "Tồn tối thiểu (bao)", 140, "number") }, Svc.MinStock.Select(m => new[] { m.Key, F(m.Value) }));
            _mg.Refresh();
        }
        private async Task Save()
        {
            var min = new Dictionary<string, double>();
            for (var r = 0; r < _mm.RowCount; r++) if (_mm.Get(r, 0).Trim().Length > 0 && ParseNum(_mm.Get(r, 1)) is { } d && !double.IsNaN(d)) min[_mm.Get(r, 0).Trim()] = d;
            if (await Ui.Run(this, () => Svc.SaveConfig((double)(_tol.Value ?? 3), (int)(_hsd.Value ?? 30), min))) { await Ui.Message(this, "Đã lưu cấu hình."); RaiseChanged(); }
        }
    }
}

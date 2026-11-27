using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.SoKho;
using KhoLanh.Core;

namespace KhoLanh.App.Views;

/// <summary>
/// Vị Trí Bột (M01VTB của app cũ, C#): tồn bột / sốt theo vị trí — đưa lên từ NXT Bột/Sốt › Tồn kho › "Đưa lên Vị Trí Bột".
/// Bảng tồn (bỏ dòng SL = 0, tìm chung + lọc), gõ SL xuất ngay trên lưới → "Tạo phiếu xuất" (lưu và trừ ngay cột SL);
/// danh sách phiếu (PC / điện thoại): hủy trừ / trừ lại / xóa. Dữ liệu dùng chung database "ton-kho-an-an" (ViTriBot*).
/// </summary>
public sealed class VtbView : UserControl, IPage
{
    public string Title => "Vị Trí Bột";
    public string Subtitle => "Tồn bột / sốt theo vị trí (đưa lên từ NXT Bột/Sốt) — lập phiếu xuất trừ tồn, hủy trừ / trừ lại / xóa phiếu PC và điện thoại.";

    private static VtbService Svc => App.Services.Vtb;
    private static IBrush B(string c) => AppTheme.Brush(c);
    private static readonly IBrush Line_ = B("#D0D7E2"), Dark = B("#0B2A45"), Pick = B("#FFF4CC");
    private readonly GridModel _m = new();
    private readonly SheetGrid _g;
    private readonly TextBox _q = new() { Watermark = "Tìm vị trí, mã hàng, ghi chú… (không dấu cũng được)", Width = 320 };
    private readonly TextBlock _info = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
    private readonly TextBlock _pickInfo = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _note = new() { Watermark = "Ghi chú phiếu (không bắt buộc)", Width = 260 };
    private readonly StackPanel _slips = new() { Spacing = 4 };
    private List<int> _view = new();             // chỉ số dòng trong Svc.Rows đang hiện
    private readonly Dictionary<int, double> _picks = new();
    private bool _painting;

    public VtbView()
    {
        _g = new SheetGrid(_m) { SpareRows = 0, CellBackground = (r, _) => r < _view.Count && _picks.ContainsKey(_view[r]) ? Pick : null,
            CheckCol = 4, CheckFill = r => _m.Get(r, 3) };   // 1.50: tích ô đầu dòng = xuất hết SL
        _m.Changed += OnEdit;
        _cbNxt.IsCheckedChanged += (_, _) => _ = TogglePush(); _cbXl.IsCheckedChanged += (_, _) => _ = TogglePush();
        var deb = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; deb.Tick += (_, _) => { deb.Stop(); Paint(); };
        _q.TextChanged += (_, _) => { deb.Stop(); deb.Start(); };
        var bar = new ToolRow { };
        bar.Children.Add(_q);
        bar.Children.Add(Btn("⟳ Tải lại", Reload));
        if (Svc.CanEdit || App.Services.Session.Can(KhoLanh.Core.SoKho.SoKhoService.PermKey, KhoLanh.Core.Models.Perm.Edit))
        {
            bar.Children.Add(Btn("📱 Đồng bộ điện thoại", SyncPhone, true));
            bar.Children.Add(Btn("Kết nối điện thoại…", PhoneSettings));
        }
        if (Svc.CanEdit) { bar.Children.Add(Btn("📄 Nạp Excel…", async () => { if (await VtbExcelUi.Load(this)) { _picks.Clear(); Paint(); PaintSlips(); } })); bar.Children.Add(Btn("Tải file mẫu…", SaveTemplate)); }
        bar.Children.Add(Btn("Đối chiếu 2 ngăn…", () => VtbExcelUi.Compare(this)));
        bar.Children.Add(_phone);
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6) }; top.Children.Add(bar); top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { _cbNxt, _cbXl } }); top.Children.Add(_info);
        var pickBar = new ToolRow { Margin = new Thickness(0, 6, 0, 0) };
        pickBar.Children.Add(_pickInfo);
        if (Svc.CanEdit) { pickBar.Children.Add(_note); pickBar.Children.Add(Btn("📝 Tạo phiếu xuất (trừ ngay)", CreateSlip, true)); pickBar.Children.Add(Btn("Bỏ chọn", () => { _picks.Clear(); Paint(); })); }
        var left = new DockPanel(); DockPanel.SetDock(pickBar, Dock.Bottom); left.Children.Add(pickBar); left.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = _g });
        var right = new DockPanel();
        var rh = new TextBlock { Text = "Phiếu xuất Vị Trí Bột", FontWeight = FontWeight.Bold, Foreground = Dark, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(rh, Dock.Top); right.Children.Add(rh); right.Children.Add(new ScrollViewer { Content = _slips });
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,6,2*") };
        split.Children.Add(left); var gs = new GridSplitter { Background = Brushes.Transparent }; Grid.SetColumn(gs, 1); split.Children.Add(gs);
        var rb = new Border { Padding = new Thickness(8, 0, 0, 0), Child = right }; Grid.SetColumn(rb, 2); split.Children.Add(rb);
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(split);
        Content = root;
        AttachedToVisualTree += (_, _) => { if (!Ui.SkipReload(this)) Reload(); };   // 1.136: quay lại trang dữ liệu chưa đổi → không đọc lại
    }

    private static Button Btn(string text, Action click, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary");
        b.Click += (_, _) => click();
        return b;
    }
    private static Button Btn(string text, Func<Task> click, bool primary = false) => Btn(text, () => { _ = click(); }, primary);
    private static string F(double v) => v.ToString("#,0.###", CultureInfo.InvariantCulture);
    private static string When(string iso) => DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime().ToString("HH:mm dd/MM/yyyy") : iso;
    private static string Fold(string s) => KhoLanh.Core.CuoiKy.Ck.Fold(s).Replace(" ", "");

    private readonly TextBlock _phone = new() { FontSize = 12, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private static KhoLanh.Core.Services.PhoneWebApp Phone => App.Services.Phone;

    private readonly CheckBox _cbNxt = new() { Content = "Điện thoại thấy: ngăn NXT", FontSize = 12.5 }, _cbXl = new() { Content = "ngăn Excel", FontSize = 12.5 };
    private async Task TogglePush()
    {
        if (_painting) return;
        if (!await Ui.Run(this, () => Svc.SetPush(_cbNxt.IsChecked == true, _cbXl.IsChecked == true))) { Paint(); return; }
        var ph = Phone;
        if (ph.Enabled && await Ui.Confirm(this, "Đẩy ngay thay đổi này lên điện thoại?", "Điện thoại", "Đẩy lên")) await SyncPhone(); else Paint();
    }

    private async Task SaveTemplate()
    {
        var path = await Ui.SaveFile(this, "Lưu file mẫu Vị trí Bột", "mau-vi-tri-bot.xlsx", "xlsx", "Excel"); if (path is null) return;
        await Ui.Run(this, () =>
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(); var ws = wb.AddWorksheet("Vị trí Bột");
            ws.Cell(1, 1).Value = "NGÀY:"; ws.Cell(1, 2).Value = DateTime.Today; ws.Cell(1, 2).Style.DateFormat.Format = "dd/MM/yyyy";
            var cols = KhoLanh.Core.SoKho.VtbService.TemplateColumns;
            for (var c = 0; c < cols.Length; c++) { var cell = ws.Cell(2, c + 1); cell.Value = cols[c]; cell.Style.Font.Bold = true; cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#D9E1F2"); }
            ws.Columns(1, cols.Length).Width = 14; wb.SaveInPlace(path);
        });
        Ui.Exported(this, Path.GetDirectoryName(path)!);
    }

    /// <summary>Nhận phiếu xuất Vị Trí Bột do điện thoại tạo (trừ tồn) rồi đẩy tồn mới lên điện thoại.</summary>
    private async Task SyncPhone()
    {
        if (!Phone.Enabled) { await PhoneSettings(); if (!Phone.Enabled) return; }
        _phone.Text = "Đang đồng bộ điện thoại…";
        KhoLanh.Core.SoKho.VtbService.SyncResult? r = null;
        if (!await Ui.RunAsync(this, async () => r = await Svc.SyncPhoneAsync(Phone))) { _phone.Text = "Đồng bộ điện thoại chưa xong."; Reload(); return; }
        _phone.Text = $"Đồng bộ lúc {DateTime.Now:HH:mm}: nhận {r!.NewPhieu} phiếu · đẩy {r.Pushed} dòng tồn" + (r.ErrPhieu > 0 ? $" · {r.ErrPhieu} phiếu lỗi (không có dòng hàng)" : "");
        _picks.Clear(); Paint(); PaintSlips();
        if (r.Errors.Count > 0) await Ui.Message(this, "Có phiếu điện thoại chưa nhận được:\n• " + string.Join("\n• ", r.Errors) + "\n\nPhiếu này giữ \"Chưa xử lý\" trên điện thoại — kiểm tra lại rồi đồng bộ lần sau.", "Phiếu điện thoại");
    }

    private async Task PhoneSettings() { await PhoneConnect.ShowAsync(this); _phone.Text = Phone.Enabled ? "Đã kết nối điện thoại." : "Chưa kết nối điện thoại."; }

    private async void Reload()
    {
        if (!Svc.CanView) { Content = new TextBlock { Text = "Tài khoản chưa được cấp quyền xem Vị Trí Bột.", Classes = { "muted" } }; return; }
        if (!await Ui.Run(this, Svc.Load)) return;
        _picks.Clear(); Paint(); PaintSlips();
    }

    private void Paint()
    {
        _painting = true;
        var q = Fold(_q.Text ?? "");
        _view = Enumerable.Range(0, Svc.Rows.Count).Where(i => Svc.Rows[i].Sl > 1e-9 && (q.Length == 0 || Fold(string.Join(" ", Svc.Rows[i].Vt, Svc.Rows[i].Ma, Svc.Rows[i].VtCu, Svc.Rows[i].VtMoi, Svc.Rows[i].GhiChu, Svc.Rows[i].Nsx, Svc.Rows[i].Hsd)).Contains(q))).ToList();
        var ro = (string k, string h, double w, string t) => new GridColumn(k, h, w, t) { ReadOnly = true };
        _m.Load(new[]
        {
            ro("vt", "VT", 90, "text"), ro("ma", "Mã hàng", 170, "text"), ro("ng", "Nguồn", 66, "text"), ro("sl", "SL", 80, "number"), new GridColumn("x", "SL XUẤT", 90, "number") { ReadOnly = !Svc.CanEdit },
            ro("nsx", "NSX", 85, "text"), ro("hsd", "HSD", 85, "text"), ro("nhap", "Nhập", 85, "text"), ro("gc", "Ghi chú", 200, "text"),
        }, _view.Select(i => { var r = Svc.Rows[i]; return new[] { r.Vt, r.Ma, r.IsExcel ? "Excel" : "NXT", F(r.Sl), _picks.TryGetValue(i, out var p) ? F(p) : "", r.Nsx, r.Hsd, r.Nhap, r.GhiChu }; }));
        _g.Refresh();
        _cbNxt.IsChecked = Svc.PushNxt; _cbXl.IsChecked = Svc.PushExcel;   // trong lúc _painting: không kích hoạt TogglePush
        _painting = false;
        var total = Svc.Rows.Where(r => r.Sl > 1e-9).ToList();
        var nx = total.Count(r => !r.IsExcel); var xl = total.Count - nx;
        var layers = $"Ngăn NXT: {(Svc.FileName.Length > 0 ? Svc.FileName : "chưa có")} · {nx} dòng còn tồn.  Ngăn Excel: {(Svc.ExcelFile.Length > 0 ? Svc.ExcelFile + (Svc.ExcelDate.Length > 0 ? " (ngày " + Svc.ExcelDate + ")" : "") : "chưa nạp")} · {xl} dòng còn tồn. Nạp Excel mới thì THAY ngăn Excel cũ; ngăn NXT cộng dồn theo sổ kho.";
        _info.Text = Svc.Rows.Count == 0 ? "Chưa có dữ liệu. Nạp file Excel, hoặc vào NXT Bột/Sốt › Tồn kho › “📱 Đưa lên Vị Trí Bột…” để đưa tồn theo vị trí lên."
            : $"{layers}\n{total.Count} dòng còn tồn (đã ẩn dòng SL = 0) · tổng SL {F(total.Sum(r => r.Sl))}" + (_view.Count != total.Count ? $" · đang lọc {_view.Count} dòng" : "") + ". Gõ số vào cột SL XUẤT (“x” = lấy hết) rồi bấm Tạo phiếu xuất.";
        _pickInfo.Text = _picks.Count == 0 ? "Chưa chọn dòng xuất." : $"Đang chọn {_picks.Count} dòng · SL xuất {F(_picks.Values.Sum())}";
    }

    private void OnEdit()
    {
        if (_painting) return;
        for (var r = 0; r < _m.RowCount && r < _view.Count; r++)
        {
            var i = _view[r]; var t = _m.Get(r, 4).Trim();
            if (t.Length == 0) { _picks.Remove(i); continue; }
            double v;
            if (t.Equals("x", StringComparison.OrdinalIgnoreCase)) v = Svc.Rows[i].Sl;
            else if (!Ui.TryParseNumber(t, out v) || v <= 0) { _picks.Remove(i); continue; }
            _picks[i] = Math.Min(v, Svc.Rows[i].Sl);
        }
        Dispatcher.UIThread.Post(Paint);
    }

    private async Task CreateSlip()
    {
        if (_picks.Count == 0) { await Ui.Message(this, "Gõ số lượng vào cột SL XUẤT (hoặc “x” = lấy hết) cho các dòng cần xuất."); return; }
        if (!await Ui.Confirm(this, $"Tạo phiếu xuất {_picks.Count} dòng (SL {F(_picks.Values.Sum())}) và TRỪ NGAY cột SL?", "Tạo phiếu xuất", "Tạo phiếu")) return;
        var picks = _picks.Select(kv => new VtbService.Pick(kv.Key, kv.Value)).ToList();
        if (await Ui.Run(this, () => Svc.CreateSlip(picks, _note.Text ?? ""))) { _picks.Clear(); _note.Text = ""; Paint(); PaintSlips(); }
    }

    private void PaintSlips()
    {
        _slips.Children.Clear();
        if (Svc.Slips.Count == 0) { _slips.Children.Add(new TextBlock { Text = "Chưa có phiếu nào.", Classes = { "muted" } }); return; }
        foreach (var s in Svc.Slips)
        {
            var locked = Svc.Locked(s);
            var st = new StackPanel { Spacing = 2 };
            st.Children.Add(new TextBlock { Text = $"{(s.IsPhone ? "📱" : "🖥")} {s.Id} · {(s.MaPhieu.Length > 0 ? s.MaPhieu : "")}", FontWeight = FontWeight.SemiBold });
            st.Children.Add(new TextBlock { Text = $"{When(s.CreatedAt)} · {s.NguoiTao} · {s.Lines.Count} dòng · SL {F(s.Lines.Sum(l => l.Qty))}" + (s.GhiChu.Length > 0 ? " · " + s.GhiChu : ""), FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
            st.Children.Add(new TextBlock { Text = locked ? "🔒 Đợt dữ liệu cũ (đã đưa tồn mới) — khóa" : s.Deducted ? "✔ Đang trừ tồn" : "○ Đã hủy trừ", FontSize = 12, Foreground = locked ? AppTheme.Muted : s.Deducted ? B("#1B5E20") : B("#8A5A00") });
            var exp = new Expander { Header = "Chi tiết", FontSize = 12, Padding = new Thickness(4) };
            var ls = new StackPanel(); foreach (var l in s.Lines) ls.Children.Add(new TextBlock { Text = $"{l.Vt} · {l.Ma} · SL {F(l.Qty)}" + (l.Hsd.Length > 0 ? " · HSD " + l.Hsd : ""), FontSize = 12, TextWrapping = TextWrapping.Wrap });
            exp.Content = ls; st.Children.Add(exp);
            if (Svc.CanEdit)
            {
                var bs = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 }; var id = s.Id;
                if (!locked) bs.Children.Add(Btn(s.Deducted ? "Hủy trừ" : "Trừ lại", async () => { var on = !s.Deducted; var miss = 0; if (await Ui.Run(this, () => miss = Svc.SetDeducted(id, on))) { Paint(); PaintSlips(); if (miss > 0) await Ui.Message(this, $"{miss} dòng của phiếu không còn trong dữ liệu hiện tại — bỏ qua."); } }));
                bs.Children.Add(Btn("Xóa", async () => { if (!await Ui.Confirm(this, $"Xóa phiếu {id}?" + (s.Deducted && !locked ? " Số lượng sẽ được hoàn lại." : ""), "Xóa phiếu", "Xóa")) return; if (await Ui.Run(this, () => Svc.DeleteSlip(id))) { Paint(); PaintSlips(); } }));
                st.Children.Add(bs);
            }
            _slips.Children.Add(new Border { Background = AppTheme.Card, BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Child = st });
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.DanhMuc;
using KhoLanh.Core.Grid;

namespace KhoLanh.App.Views;

/// <summary>
/// DANH MỤC MÃ HÀNG DÙNG CHUNG (theo danh-muc-ma-hang.js của app cũ): gốc = Danh sách mã hàng của Tổng hợp. Lưới kiểu Excel — gõ / dán thẳng,
/// rời tay là tự lưu; chỉ hiện tối đa 2.000 dòng khớp ô tìm / bộ lọc. Cột Size / Trọng lượng / Đang dùng ở / Kiểm tra tự tính từ mã.
/// Tab "Cần xử lý": mã đang dùng chưa có trong danh mục (thêm hàng loạt), mã trùng, mã nghi gõ sai, mã có đoạn chưa khai báo.
/// </summary>
public sealed class DanhMucMaView : UserControl, IPage
{
    public string Title => "Danh mục mã hàng";
    public string Subtitle => "Danh sách mã hàng dùng chung (Tổng hợp › Danh sách mã hàng): đọc mã 21 ký tự theo 10 đoạn, báo trùng / nghi gõ sai / đoạn chưa khai báo, nơi đang dùng mã.";

    private static DanhMucService Svc => App.Services.DanhMuc;
    private const int CId = 6, Cap = 2000;
    private static readonly IBrush Err = AppTheme.Brush("#FDE7E7"), Warn = AppTheme.Brush("#FFF4CC"), Line_ = AppTheme.Brush("#D0D7E2");

    private readonly GridModel _m = new(); private readonly SheetGrid _g;
    private readonly TextBox _q = new() { Watermark = "Tìm mã hoặc diễn giải…", Width = 260 };
    private readonly ComboBox _f = new() { ItemsSource = new[] { "Tất cả", "Trùng mã", "Nghi gõ sai", "Đoạn chưa khai báo" }, SelectedIndex = 0, MinWidth = 160 };
    private readonly TextBlock _cnt = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
    private readonly TextBlock _st = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TabControl _tabs = new();
    private readonly TabItem _xlTab = new() { Header = "Cần xử lý" };
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _find = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private List<string> _shownIds = new();
    private bool _loading, _warnedEmpty;

    // tab Cần xử lý
    private readonly GridModel _mm = new() { ReadOnly = true }, _nm = new() { ReadOnly = true }, _dm = new() { ReadOnly = true };
    private readonly SheetGrid _mg, _ng, _dg;
    private readonly TextBlock _mh = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, _nh = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
        _dh = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, _sh = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private DanhMucMa.Xl _xl = new(new(), new(), new(), new());

    public DanhMucMaView()
    {
        _g = new SheetGrid(_m) { SpareRows = Svc.CanEdit ? 30 : 0, Display = Show, CellBackground = Bg };
        _m.ReadOnly = !Svc.CanEdit;
        _m.Changed += () => { if (!_loading && Svc.CanEdit) { _save.Stop(); _save.Start(); Pill("Chưa lưu…", "#8A5A00"); } };
        _save.Tick += (_, _) => { _save.Stop(); Save(); };
        _find.Tick += (_, _) => { _find.Stop(); Draw(); };
        _q.TextChanged += (_, _) => { _find.Stop(); _find.Start(); };
        _f.SelectionChanged += (_, _) => Draw();

        var bar = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 6) };
        bar.Children.Add(_q); bar.Children.Add(_f);
        if (Svc.CanEdit) bar.Children.Add(Btn("Xóa dòng đã chọn", DeleteSel, "Xóa hẳn các mã đang chọn khỏi danh mục (không xóa mã trong tồn kho / phiếu)"));
        bar.Children.Add(Btn("⟳ Nạp lại", () => Reload(), "Đọc lại danh mục, Mã hóa sản phẩm và nơi đang dùng mã"));
        if (Svc.CanExport) bar.Children.Add(Btn("Xuất Excel", Export));
        bar.Children.Add(_cnt); bar.Children.Add(_st);
        var help = new TextBlock
        {
            Text = Svc.CanEdit ? "Gõ / dán thẳng vào cột Mã hàng, Diễn giải (dán nhiều dòng từ Excel được; thêm mã mới ở các dòng trống cuối bảng — diễn giải để trống thì tự dựng từ Mã hóa sản phẩm). Tự lưu sau khi gõ. Đổi mã ở đây KHÔNG sửa mã trong tồn kho / phiếu cũ."
                : "Chỉ xem (tài khoản không có quyền sửa Danh mục mã hàng).",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Classes = { "muted" },
        };
        var top = new StackPanel(); top.Children.Add(bar); top.Children.Add(help);
        var p1 = new DockPanel(); DockPanel.SetDock(top, Dock.Top); p1.Children.Add(top);
        p1.Children.Add(new DetailPane(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = _g }, _g, "Chi tiết mã") { Margin = new Thickness(0, 6, 0, 0) });   // 1.53
        _tabs.Items.Add(new TabItem { Header = "Mã hàng", Content = p1 });

        _mg = new SheetGrid(_mm) { SpareRows = 0 }; _ng = new SheetGrid(_nm) { SpareRows = 0 }; _dg = new SheetGrid(_dm) { SpareRows = 0 };
        _xlTab.Content = BuildXl(); _tabs.Items.Add(_xlTab);
        Content = _tabs;
        AttachedToVisualTree += async (_, _) => await Reload();
        DetachedFromVisualTree += (_, _) => { if (_save.IsEnabled) { _save.Stop(); Save(); } };
    }

    private static Button Btn(string t, Func<Task> click, string? tip = null, bool primary = false)
    {
        var b = new Button { Content = t, Padding = new Thickness(10, 4), FontSize = 12.5 };
        if (primary) b.Classes.Add("primary"); if (tip is not null) ToolTip.SetTip(b, tip);
        b.Click += async (_, _) => await click(); return b;
    }
    private static Button Btn(string t, Action click, string? tip = null, bool primary = false) => Btn(t, () => { click(); return Task.CompletedTask; }, tip, primary);
    private void Pill(string t, string color) { _st.Text = t; _st.Foreground = AppTheme.Brush(color); }

    private async Task Reload()
    {
        if (_save.IsEnabled) { _save.Stop(); Save(); }
        if (!await Ui.RunAsync(this, () => Task.Run(Svc.Load))) return;
        Draw(); PaintXl();
        if (Svc.Errors.Count > 0) Pill("⚠ " + string.Join("; ", Svc.Errors.Take(2)), "#B42318");
    }

    // ------------------------------------------------------------------ lưới mã hàng

    private string Filter => _f.SelectedIndex switch { 1 => "trung", 2 => "sai", 3 => "doan", _ => "all" };

    private void Draw(int keepRow = -1, int keepCol = 0)
    {
        if (_save.IsEnabled) { _save.Stop(); Save(); }
        var list = Svc.Visible(_q.Text ?? "", Filter, Cap); var total = Svc.Rows.Count;
        _shownIds = list.Select(r => r.Id).ToList();
        _loading = true;
        _m.Load(new[]
        {
            new GridColumn("code", "Mã hàng (21 ký tự)", 200), new GridColumn("note", "Diễn giải", 400),
            new GridColumn("size", "Size (đoạn 8)", 105) { ReadOnly = true }, new GridColumn("tl", "Trọng lượng (đoạn 9)", 145) { ReadOnly = true },
            new GridColumn("dung", "Đang dùng ở", 170) { ReadOnly = true }, new GridColumn("kt", "Kiểm tra", 420) { ReadOnly = true },
            new GridColumn("_id", "", 0) { ReadOnly = true },
        }, list.Select(r => new[] { r.Code, r.Note, "", "", "", "", r.Id }));
        _loading = false;
        _g.Refresh();
        if (keepRow >= 0) _g.Select(Math.Min(keepRow, Math.Max(_m.RowCount - 1, 0)), keepCol);
        _cnt.Text = $"{total:#,0} mã" + (list.Count < total ? $" · đang hiện {list.Count:#,0}" + (list.Count >= Cap ? $" (tối đa {Cap:#,0} — gõ ô Tìm để thu hẹp)" : "") : "");
        _xlTab.Header = _xl.Count > 0 ? $"Cần xử lý ({_xl.Count})" : "Cần xử lý";
        MainWindow.Of(this)?.SetBadge(DanhMucService.PermKey, _xl.Count);   // 1.72: số trên thanh menu theo đúng màn hình đang xem
    }

    private string Show(int r, int c, string raw)
    {
        if (c < 2 || c > 5) return raw;
        var code = DanhMucMa.NormCode(_m.Get(r, 0)); if (code.Length == 0) return "";
        switch (c)
        {
            case 2 or 3:
                if (code.Length != DanhMucMa.Len) return "";
                return Svc.DecodeOf(code)[c == 2 ? 7 : 8].Name ?? "";
            case 4: return Svc.UseOf(code);
            default: var s = Svc.StatusFor(code); return s is null ? "✓" : string.Join("; ", s.Msgs);
        }
    }
    private IBrush? Bg(int r, int c)
    {
        if (r >= _m.RowCount || c == CId) return null;
        var code = DanhMucMa.NormCode(_m.Get(r, 0)); if (code.Length == 0 && _m.Get(r, 1).Length == 0) return null;
        var s = Svc.StatusFor(code); return s is null ? null : s.Level == "err" ? Err : Warn;
    }

    /// <summary>Gom thay đổi trên lưới → lưu. Dòng mới / dòng bị xóa → vẽ lại lưới để gán mã dòng; sửa dòng cũ thì giữ nguyên lưới (giữ hoàn tác).</summary>
    private void Save()
    {
        if (!Svc.CanEdit) return;
        var edits = new List<(string?, string, string, string?)>(); var seen = new HashSet<string>(); string? prev = null; var added = 0; var empty = 0;
        for (var r = 0; r < _m.RowCount; r++)
        {
            var id = _m.Get(r, CId); var code = _m.Get(r, 0).Trim(); var note = _m.Get(r, 1);
            var mine = id.Length > 0 && seen.Add(id) ? id : null;
            if (code.Length == 0) { if (mine is not null) { empty++; prev = mine; } continue; }
            if (mine is null) added++;
            edits.Add((mine, code, note, prev));
            if (mine is not null) prev = mine;
        }
        var del = _shownIds.Where(id => !seen.Contains(id)).ToList();
        if (empty > 0 && !_warnedEmpty) { _warnedEmpty = true; _ = Ui.Message(this, "Dòng đã xóa trắng chưa bị xóa khỏi danh mục — muốn xóa hẳn thì chọn dòng rồi bấm «Xóa dòng đã chọn»."); }
        if (added == 0 && del.Count == 0 && !edits.Any(e => Changed(e.Item1!, e.Item2, e.Item3))) { Pill("", "#1E7B34"); return; }
        try
        {
            var t0 = DateTime.Now;
            var n = Svc.Apply(edits, del, added > 0 ? $"Thêm {added} mã" : del.Count > 0 ? $"Xóa {del.Count} mã" : "Sửa mã");
            Pill($"Đã lưu · {n} dòng · {(DateTime.Now - t0).TotalMilliseconds:0} ms", "#1E7B34");
            if (added > 0 || del.Count > 0) Draw(_g.Row, _g.Col); else { _g.Refresh(); }
            PaintXl();
        }
        catch (Exception e) { Pill("Lưu lỗi — thử lại", "#B42318"); _ = Ui.Message(this, "Không lưu được: " + e.Message); }
    }
    private static bool Changed(string id, string code, string note) => Svc.Rows.FirstOrDefault(r => r.Id == id) is not { } r || r.Code != DanhMucMa.NormCode(code) || r.Note != note;

    private async Task DeleteSel()
    {
        if (_save.IsEnabled) { _save.Stop(); Save(); }
        var sel = _g.Selection; var ids = Enumerable.Range(sel.R1, sel.Rows).Where(r => r < _m.RowCount).Select(r => _m.Get(r, CId)).Where(x => x.Length > 0).Distinct().ToList();
        if (ids.Count == 0) { await Ui.Message(this, "Chọn ô / số dòng của các mã cần xóa trước."); return; }
        if (!await Ui.Confirm(this, $"Xóa {ids.Count} mã khỏi danh mục? (Không xóa mã trong tồn kho / phiếu.)", "Xóa mã", "Xóa")) return;
        if (await Ui.Run(this, () => Svc.Apply(Array.Empty<(string?, string, string, string?)>(), ids, $"Xóa {ids.Count} mã"))) { Draw(sel.R1); PaintXl(); }
    }

    private async Task Export()
    {
        if (_save.IsEnabled) { _save.Stop(); Save(); }
        var p = await Ui.SaveFile(this, "Xuất Excel", "Danh-muc-ma-hang.xlsx", "xlsx", "Excel");
        if (p is not null && await Ui.Run(this, () => Svc.Export(p))) Ui.OpenExternal(p);
    }

    /// <summary>Mở 1 mã trên lưới (từ tab Cần xử lý).</summary>
    private void Go(string code) { _f.SelectedIndex = 0; _q.Text = code; _find.Stop(); Draw(0); _tabs.SelectedIndex = 0; }

    // ------------------------------------------------------------------ Cần xử lý

    private Control BuildXl()
    {
        // 1.57: 3 bảng xếp chồng (cuộn cả trang) → 3 tab con, mỗi bảng cao hết phần còn lại.
        static Control Pane(Control head, Control grid)
        {
            var d = new DockPanel(); var h = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 6) }; h.Children.Add(head);
            DockPanel.SetDock(h, Dock.Top); d.Children.Add(h); d.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), Child = grid }); return d;
        }
        var mb = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        if (Svc.CanEdit)
        {
            mb.Children.Add(Btn("Thêm tất cả vào danh mục", () => AddMissing(_xl.Missing), primary: true));
            mb.Children.Add(Btn("Thêm các dòng đang chọn", () => { var sel = _mg.Selection; return AddMissing(Enumerable.Range(sel.R1, sel.Rows).Where(r => r < _xl.Missing.Count).Select(r => _xl.Missing[r]).ToList()); }));
        }
        mb.Children.Add(new TextBlock { Text = "Diễn giải tự dựng từ Mã hóa sản phẩm (sửa lại được trên lưới).", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
        var mh = new StackPanel { Spacing = 6 }; mh.Children.Add(_mh); mh.Children.Add(mb);
        var db = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 }; db.Children.Add(_dh); db.Children.Add(Btn("Mở mã đang chọn trên lưới", () => Pick(_dg, _xl.Dups.Select(d => d.Code).ToList())));
        var nb = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 }; nb.Children.Add(_nh); nb.Children.Add(Btn("Mở mã đang chọn trên lưới", () => Pick(_ng, _xl.Near.Select(d => d.Code).ToList())));
        _dg.DoubleTapped += (_, _) => Pick(_dg, _xl.Dups.Select(d => d.Code).ToList());
        _ng.DoubleTapped += (_, _) => Pick(_ng, _xl.Near.Select(d => d.Code).ToList());
        var sb = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 8, 0, 0) }; sb.Children.Add(_sh); sb.Children.Add(Btn("Lọc trên lưới", () => { _q.Text = ""; _f.SelectedIndex = 3; Draw(); _tabs.SelectedIndex = 0; }));
        _xlSub = Ui.SubTabs(("Chưa có trong danh mục", Pane(mh, _mg)), ("Mã trùng", Pane(db, _dg)), ("Nghi gõ sai", Pane(nb, _ng)));
        var root = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(sb, Dock.Bottom); root.Children.Add(sb); root.Children.Add(_xlSub);
        return root;
    }
    private TabControl? _xlSub;
    private void XlHead(int i, string t, int n) { if (_xlSub?.Items[i] is TabItem ti) ti.Header = n > 0 ? $"{t} ({n:#,0})" : t; }
    private static Border Box(Control c, double h) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), Height = h, Child = c };
    private void Pick(SheetGrid g, List<string> codes) { var r = g.Row; if (r >= 0 && r < codes.Count) Go(codes[r]); }

    private void PaintXl()
    {
        _xl = Svc.XlNow();
        _mh.Text = _xl.Missing.Count == 0 ? "✓ Mọi mã đang có trong Tồn kho An An / Tồn kho gửi / Sổ kho đều đã có trong danh mục."
            : $"⚠ {_xl.Missing.Count} mã đang có trong Tồn kho An An / Tồn kho gửi / Sổ kho nhưng chưa có trong danh mục.";
        _mm.Load(new[] { new GridColumn("ma", "Mã hàng", 200), new GridColumn("dg", "Diễn giải dựng từ mã", 520), new GridColumn("dung", "Đang dùng ở", 200) },
            _xl.Missing.Take(3000).Select(c => new[] { c, Svc.NoteFor(c), Svc.UseOf(c) }));
        _dh.Text = _xl.Dups.Count == 0 ? "✓ Không có mã trùng trong danh mục." : $"⚠ {_xl.Dups.Count} mã bị trùng trong danh mục.";
        _dm.Load(new[] { new GridColumn("ma", "Mã hàng", 200), new GridColumn("n", "Số dòng", 80, "number"), new GridColumn("dung", "Đang dùng ở", 200) },
            _xl.Dups.Take(300).Select(d => new[] { d.Code, d.N.ToString(), Svc.UseOf(d.Code) }));
        _nh.Text = _xl.Near.Count == 0 ? "✓ Không có mã nghi gõ sai." : $"⚠ {_xl.Near.Count} mã có thể gõ sai: khác đúng 1 ký tự so với 1 mã khác, và ký tự đó làm đoạn mã chưa có trong Mã hóa sản phẩm.";
        _nm.Load(new[] { new GridColumn("ma", "Mã nghi gõ sai", 200), new GridColumn("d1", "Đang dùng ở", 170), new GridColumn("ok", "Mã gần giống (hợp lệ)", 200), new GridColumn("d2", "Đang dùng ở", 170) },
            _xl.Near.Take(2000).Select(p => new[] { p.Code, Svc.UseOf(p.Code), p.Other, Svc.UseOf(p.Other) }));
        _sh.Text = $"{_xl.Miss.Count} mã có đoạn chưa khai báo trong Mã hóa sản phẩm (khai báo ở module Tổng hợp › Mã hóa sản phẩm).";
        _mg.Refresh(); _dg.Refresh(); _ng.Refresh();
        XlHead(0, "Chưa có trong danh mục", _xl.Missing.Count); XlHead(1, "Mã trùng", _xl.Dups.Count); XlHead(2, "Nghi gõ sai", _xl.Near.Count);
        _xlTab.Header = _xl.Count > 0 ? $"Cần xử lý ({_xl.Count})" : "Cần xử lý";
        MainWindow.Of(this)?.SetBadge(DanhMucService.PermKey, _xl.Count);   // 1.72: số trên thanh menu theo đúng màn hình đang xem
    }

    private async Task AddMissing(List<string> codes)
    {
        if (codes.Count == 0) { await Ui.Message(this, "Chưa chọn mã nào."); return; }
        if (_save.IsEnabled) { _save.Stop(); Save(); }
        var n = 0;
        if (await Ui.Run(this, () => n = Svc.AddMissing(codes))) { Draw(); PaintXl(); await Ui.Message(this, $"Đã thêm {n} mã vào danh mục."); }
    }
}

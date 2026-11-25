using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.Core.LichXuat;
using static KhoLanh.Core.LichXuat.LichXuatCore;

namespace KhoLanh.App.Views;

public sealed partial class TongQuanView
{
    private static Window Win(string title, Control body, double w, double h) => new()
    {
        Title = title, Width = w, Height = h, MinWidth = 480, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = AppTheme.Card,
        Content = new Border { Padding = new Thickness(18, 14), Child = body },
    };
    private static TextBlock Lb(string t) => new() { Text = t, FontSize = 12, Foreground = Muted, Margin = new Thickness(0, 6, 0, 2) };

    // ------------------------------------------------------------------ thêm / sửa lịch

    private async Task EditDialog(string? id, string? day)
    {
        var old = id is null ? null : Svc.Rows.FirstOrDefault(r => r.Id == id);
        if (id is not null && old is null) return;
        var ngay = new CalendarDatePicker { SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy", Width = 150, SelectedDate = IsIso(old?.Ngay ?? day) ? DateOf(old?.Ngay ?? day!) : DateTime.Today };
        var gio = new TextBox { Text = old?.Gio ?? "", Watermark = "08:30", Width = 90 };
        var st = new ComboBox { ItemsSource = StLabel.Values.ToList(), SelectedIndex = Math.Max(0, StLabel.Keys.ToList().IndexOf(old?.TrangThai ?? "plan")), Width = 170 };
        var lsx = new TextBox { Text = old?.Lsx ?? "", Watermark = "26.05.024 (nhiều LSX chung cont: 26.05.024 + 26.05.025)" };
        var look = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Muted };
        var cont = new TextBox { Text = old?.ContSo ?? "", Watermark = "ABCD1234567", Width = 170 };
        var note = new TextBox { Text = old?.GhiChu ?? "", Watermark = "Ghi chú" };
        var chung = new ComboBox { Width = 330 };
        var err = new TextBlock { Foreground = B("#B42318"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        List<Lich> mates = new();
        void FillMates()
        {
            var d = ngay.SelectedDate is { } x ? Iso(x) : "";
            mates = Svc.Rows.Where(r => r.Ngay == d && r.Id != id).OrderBy(r => r.Id).ToList();
            chung.ItemsSource = new[] { "— Riêng 1 container —" }.Concat(mates.Select(r => Label(r.Lsx) + (r.Khach.Length > 0 ? " · " + r.Khach : ""))).ToList();
            var cur = old is { Cont.Length: > 0 } ? mates.FindIndex(r => r.Cont == old.Cont) : -1;
            chung.SelectedIndex = cur + 1;
        }
        ngay.SelectedDateChanged += (_, _) => FillMates(); FillMates();
        async Task Probe()
        {
            look.Text = "Đang dò Thành Phẩm Chính…";
            var q = lsx.Text ?? ""; LichXuatService.LookupResult? r = null;
            await Task.Run(() => r = Svc.Lookup(LichXuatService.NormLsxInput(q)));
            look.Text = r!.Found ? $"✔ {r.SoDong} dòng · khách {r.Khach} · {r.MatHang} · size {r.Size} · tồn {r.TonKien:#,0.##} kiện · {Tan(r.Kg)} tấn (Load Data {r.DataDate})" : "✖ " + r.Message + " — vẫn lưu được lịch.";
        }
        var probe = Btn("Dò", Probe, "Tìm LSX trong Thành Phẩm Chính của Tồn kho An An (cột Hợp đồng)");
        var save = new Button { Content = id is null ? "Thêm lịch" : "Lưu", Classes = { "primary" }, IsDefault = true };
        var cancel = new Button { Content = "Đóng", IsCancel = true };
        var del = new Button { Content = "Xóa lịch", IsVisible = id is not null, Foreground = B("#B42318") };
        var body = new StackPanel { Spacing = 2 };
        var r1 = new WrapPanel { ItemSpacing = 10 };
        r1.Children.Add(new StackPanel { Children = { Lb("Ngày xuất"), ngay } }); r1.Children.Add(new StackPanel { Children = { Lb("Giờ kéo đóng"), gio } }); r1.Children.Add(new StackPanel { Children = { Lb("Trạng thái"), st } });
        body.Children.Add(r1);
        body.Children.Add(Lb("Số LSX"));
        var lr = new DockPanel(); DockPanel.SetDock(probe, Dock.Right); lr.Children.Add(probe); probe.Margin = new Thickness(6, 0, 0, 0); lr.Children.Add(lsx); body.Children.Add(lr);
        body.Children.Add(look);
        var r2 = new WrapPanel { ItemSpacing = 10 };
        r2.Children.Add(new StackPanel { Children = { Lb("Chung container với (cùng ngày)"), chung } }); r2.Children.Add(new StackPanel { Children = { Lb("Số cont"), cont } });
        body.Children.Add(r2);
        body.Children.Add(Lb("Ghi chú")); body.Children.Add(note);
        body.Children.Add(err);
        var btns = Ui.Buttons(del, cancel, save); body.Children.Add(btns);
        var w = Ui.Dialog(id is null ? "Thêm lịch xuất" : "Sửa lịch xuất", body, 620);
        save.Click += async (_, _) =>
        {
            err.Text = "";
            var inp = new LichXuatService.Input
            {
                Ngay = ngay.SelectedDate is { } d ? Iso(d) : "", Gio = gio.Text ?? "", TrangThai = StLabel.Keys.ElementAt(Math.Max(0, st.SelectedIndex)), Lsx = lsx.Text ?? "",
                ContSo = cont.Text ?? "", GhiChu = note.Text ?? "", ChungVoi = chung.SelectedIndex > 0 ? mates[chung.SelectedIndex - 1].Id : "",
            };
            save.IsEnabled = false;
            try { await Task.Run(() => { if (id is null) Svc.Add(inp); else Svc.Update(id, inp); }); w.Close(); }
            catch (Exception e) { err.Text = e is KhoLanh.Core.Services.BusinessException ? e.Message : Ui.Describe(e); save.IsEnabled = true; }
        };
        cancel.Click += (_, _) => w.Close();
        del.Click += async (_, _) => { if (await Ui.Confirm(w, $"Xóa lịch {old!.Lsx} ngày {Vn(old.Ngay)}?", "Xóa lịch", "Xóa") && await Ui.Run(w, () => Svc.Delete(new[] { id! }))) w.Close(); };
        w.Opened += (_, _) => lsx.Focus();
        await Ui.Show(this, w);
        PaintAll(); PaintMonth();
    }

    // ------------------------------------------------------------------ chi tiết lịch

    private async Task DetailDialog(List<string> ids)
    {
        var body = new StackPanel { Spacing = 8 };
        var w = Win("Chi tiết lịch xuất", new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, 860, 720);
        void Paint()
        {
            body.Children.Clear();
            var g = Svc.Group(ids);
            if (g.Count == 0) { body.Children.Add(new TextBlock { Text = "Lịch này không còn (đã xóa hoặc chuyển)." }); return; }
            var x = Mk(g, DateTime.Now, Svc.StateOf);
            var head = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
            head.Children.Add(new TextBlock { Text = x.Lsx, FontSize = 19, FontWeight = FontWeight.Bold, Foreground = Dark });
            head.Children.Add(CoBadge(x.Co)); head.Children.Add(StPill(x.St));
            body.Children.Add(head);
            body.Children.Add(new TextBlock { Text = $"{DayLabel(x.Ngay)}{(x.Gio.Length > 0 ? " · kéo đóng " + GioVn(x.Gio) : "")} · {(g.Count > 1 ? $"chung 1 container ({g.Count} LSX)" : "1 container")} · cont {(x.Cont.Length > 0 ? x.Cont : "—")} · xe {(x.Xe.Length > 0 ? x.Xe : "—")}", FontSize = 13 });
            // chuẩn bị
            var prep = new WrapPanel { ItemSpacing = 6 };
            prep.Children.Add(new TextBlock { Text = "Chuẩn bị:", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            foreach (var (t, ok) in new[] { ("List", x.HasList), ("Phiếu xuất kho", x.HasPhieu), ("Số cont", x.Cont.Length > 0 && !x.Cont.StartsWith("ghép")) })
                prep.Children.Add(ok ? Pill("✔ " + t, "#E6F4EA", "#1E7B34") : Pill("✖ " + t, "#FDE7E7", "#B42318"));
            body.Children.Add(prep);
            // thao tác
            var acts = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            if (Svc.CanEdit)
            {
                acts.Children.Add(Btn("✎ Sửa", async () => { await EditDialog(g[0].Id, null); Paint(); }));
                acts.Children.Add(Btn("📅 Chuyển ngày…", async () =>
                {
                    var s = await Ui.Prompt(w, "Chuyển sang ngày (dd/mm hoặc dd/mm/yyyy):", "Chuyển lịch", Vn(x.Ngay));
                    if (s is null) return; var iso = ParseNgay(s, DateTime.Today);
                    if (iso.Length == 0) { await Ui.Message(w, "Ngày không hợp lệ."); return; }
                    if (await Ui.Run(w, () => Svc.Move(ids, iso))) Paint();
                }));
                if (x.St != "ok") acts.Children.Add(Btn("✔ Đã xuất", async () => { if (await Ui.Run(w, () => Svc.SetStatus(ids, "ok"))) Paint(); }));
                if (x.St == "plan") acts.Children.Add(Btn("Đang đóng hàng", async () => { if (await Ui.Run(w, () => Svc.SetStatus(ids, "run"))) Paint(); }));
                acts.Children.Add(Btn("✉ Số cont từ email", async () => { await ContMailDialog(ids); Paint(); }));
                acts.Children.Add(Btn("⟳ Dò lại tồn", async () => { if (await Ui.RunAsync(w, async () => await Task.Run(() => Svc.Rescan(ids)))) Paint(); }));
            }
            if (Svc.CanEdit && Svc.CanPnx)
            {
                if (!x.HasList) acts.Children.Add(Btn("Tạo List", async () => { List<string> n = new(); if (await Ui.RunAsync(w, async () => n = await Task.Run(() => Svc.CreateLists(ids)))) { await Ui.Message(w, string.Join("\n", n)); Paint(); } }));
                if (!x.HasPhieu) acts.Children.Add(Btn("Tạo phiếu xuất", async () => { List<string> n = new(); if (await Ui.RunAsync(w, async () => n = await Task.Run(() => Svc.CreatePhieu(ids).Notes))) { await Ui.Message(w, string.Join("\n", n)); Paint(); } }, primary: true));
            }
            if (x.HasPhieu) acts.Children.Add(Btn("📄 Mở phiếu xuất kho", async () => { w.Close(); await OpenPnx(0, Svc.PhieuOf(g)?.Id, null); }));
            if (x.HasList) acts.Children.Add(Btn("📋 Mở List", async () => { w.Close(); await OpenPnx(1, null, x.Codes.FirstOrDefault()); }));
            if (Svc.CanEdit) acts.Children.Add(Btn("Xóa", async () => { if (await Ui.Confirm(w, $"Xóa {g.Count} lịch {x.Lsx} ngày {Vn(x.Ngay)}?", "Xóa lịch", "Xóa") && await Ui.Run(w, () => Svc.Delete(ids))) w.Close(); }));
            body.Children.Add(acts);

            foreach (var r in g)
            {
                var card = new StackPanel { Spacing = 4 };
                card.Children.Add(new TextBlock { Text = Label(r.Lsx) + (r.Khach.Length > 0 ? " — " + r.Khach : "") + (r.ThiTruong.Length > 0 ? " · " + r.ThiTruong : ""), FontWeight = FontWeight.Bold, FontSize = 14, Foreground = Dark, TextWrapping = TextWrapping.Wrap });
                card.Children.Add(new TextBlock
                {
                    Text = r.SoDong > 0 ? $"Theo tồn kho: {r.TonKien:#,0.##} kiện · {Tan(r.Kg)} tấn · {r.SoDong} dòng Thành Phẩm Chính (Load Data {r.DataDate}, dò lúc {r.DoLuc})\nHợp đồng gốc: {r.NguonHd}" : "Chưa dò được LSX này trong Thành Phẩm Chính (bấm ⟳ Dò lại tồn sau khi nạp Load Data mới).",
                    FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
                });
                if (r.GhiChu.Length > 0) card.Children.Add(new TextBlock { Text = "Ghi chú: " + r.GhiChu, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
                var lines = Lines(r).GroupBy(l => (l.Ten.Split(" (")[0].Split(',')[0].Trim(), l.Size)).Select(gg => (gg.Key.Item1, gg.Key.Size, Ton: gg.Sum(z => z.Ton), Kg: gg.Sum(z => z.Kg))).ToList();
                if (lines.Count > 0)
                {
                    var tg = new Grid { ColumnDefinitions = new ColumnDefinitions("*,100,90,90") };
                    void Add(int row, string[] c, bool h) { for (var i = 0; i < 4; i++) { var t = new TextBlock { Text = c[i], FontSize = h ? 11 : 12.5, FontWeight = h ? FontWeight.SemiBold : FontWeight.Normal, Foreground = h ? Muted : AppTheme.Text, Margin = new Thickness(4, 2) }; Grid.SetRow(t, row); Grid.SetColumn(t, i); tg.Children.Add(t); } }
                    tg.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); Add(0, new[] { "Hàng", "Size", "Tồn (kiện)", "Tấn" }, true);
                    for (var i = 0; i < lines.Count; i++) { tg.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); Add(i + 1, new[] { lines[i].Item1, lines[i].Size, lines[i].Ton.ToString("#,0.##"), Tan(lines[i].Kg) }, false); }
                    card.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = tg });
                }
                card.Children.Add(ViecBox(r, Paint, w));
                body.Children.Add(Card(card, 10));
            }
        }
        Paint();
        await Ui.Show(this, w);
        PaintAll(); PaintMonth();
    }

    private Control ViecBox(Lich r, Action repaint, Window w)
    {
        var s = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
        var def = IsIso(r.Ngay) ? Vn(Iso(DateOf(r.Ngay).AddDays(-1))) : "";
        s.Children.Add(new TextBlock { Text = $"Việc cần xử lý · hạn mặc định trước ngày xuất 1 ngày ({def}); chưa tick đến ngày xuất sẽ báo ĐỎ", FontSize = 12, FontWeight = FontWeight.SemiBold });
        var now = DateTime.Now; var shipped = r.TrangThai == "ok" || string.CompareOrdinal(r.Ngay, Iso(now)) < 0;
        foreach (var v in ViecOf(r))
        {
            var lv = v.D == 1 ? "ok" : shipped ? "idle" : ViecLv(r, v, now);
            var row = new DockPanel();
            var cb = new CheckBox { IsChecked = v.D == 1, IsEnabled = Svc.CanEdit, VerticalAlignment = VerticalAlignment.Center };
            var vid = v.I; cb.IsCheckedChanged += async (_, _) => { if (await Ui.Run(w, () => Svc.ViecTick(r.Id, vid, cb.IsChecked == true))) repaint(); };
            DockPanel.SetDock(cb, Dock.Left); row.Children.Add(cb);
            if (Svc.CanEdit) { var x = new Button { Content = "×", Padding = new Thickness(6, 0) }; x.Click += async (_, _) => { if (await Ui.Run(w, () => Svc.ViecDelete(r.Id, vid))) repaint(); }; DockPanel.SetDock(x, Dock.Right); row.Children.Add(x); }
            var badge = v.D == 1 ? "Xong" + (IsIso(v.Dt) ? " " + Vn(v.Dt) : "") : lv == "red" ? (string.CompareOrdinal(Iso(now), r.Ngay) >= 0 ? "Hôm nay xuất — chưa xong" : "Quá hạn") : lv == "amber" ? "Đến hạn hôm nay" : "Hạn " + Vn(ViecHan(r, v));
            var (bg, fg) = lv switch { "ok" => ("#E6F4EA", "#1E7B34"), "red" => ("#FDE7E7", "#B42318"), "amber" => ("#FFF4CC", "#8A5A00"), _ => ("#EEF1F5", "#4B5565") };
            var p = Pill(badge, bg, fg, 11); DockPanel.SetDock(p, Dock.Right); p.Margin = new Thickness(6, 0); row.Children.Add(p);
            row.Children.Add(new TextBlock { Text = v.T, VerticalAlignment = VerticalAlignment.Center, TextDecorations = v.D == 1 ? TextDecorations.Strikethrough : null, TextWrapping = TextWrapping.Wrap });
            s.Children.Add(row);
        }
        if (Svc.CanEdit)
        {
            var box = new TextBox { Watermark = "+ Thêm việc cần xử lý (gõ rồi Enter), ví dụ: Chưa dán băng keo", MaxLength = 120 };
            async Task Add(string t) { if (t.Trim().Length == 0) return; if (await Ui.Run(w, () => Svc.ViecAdd(r.Id, t))) repaint(); }
            box.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await Add(box.Text ?? ""); };
            s.Children.Add(box);
            var sg = Svc.ViecSugs().Where(t => !ViecOf(r).Any(v => v.D == 0 && v.T == t)).ToList();
            if (sg.Count > 0)
            {
                var wp = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 }; wp.Children.Add(new TextBlock { Text = "Đã dùng:", FontSize = 11.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
                foreach (var t in sg) wp.Children.Add(Btn(t, () => Add(t), fs: 11.5));
                s.Children.Add(wp);
            }
        }
        return s;
    }

    // ------------------------------------------------------------------ nhập nhanh

    private async Task QuickDialog()
    {
        const string Sample = "Cập nhật kế hoạch:\nNgày 24/11:\n1. Xuất LSX 26.07.051 + 26.08.033 Mitsubishi\n2. Xuất LSX 26.10.054\nNgày 25/11: Xuất LSX 26.11.002";
        var text = new TextBox { AcceptsReturn = true, Height = 170, TextWrapping = TextWrapping.Wrap, Watermark = Sample, FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"), FontSize = 12.5 };
        var prev = new StackPanel { Spacing = 2 };
        var notes = new TextBlock { FontSize = 12, Foreground = B("#8A5A00"), TextWrapping = TextWrapping.Wrap };
        var go = new Button { Content = "Thêm vào lịch", Classes = { "primary" }, IsEnabled = false }; var close = new Button { Content = "Đóng", IsCancel = true };
        List<QuickRow> rows = new();
        void Parse()
        {
            (rows, var n) = QuickParse(text.Text ?? "", DateTime.Now);
            prev.Children.Clear();
            foreach (var g in rows.GroupBy(r => r.Ngay))
            {
                prev.Children.Add(new TextBlock { Text = DayLabel(g.Key), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
                foreach (var r in g)
                {
                    var dup = SameDayDup(Svc.Rows, r.Lsx, r.Ngay) is not null; var other = Svc.Rows.Where(x => x.Ngay != r.Ngay && Sq(x.Lsx) == Sq(r.Lsx)).Select(x => Dm(x.Ngay)).ToList();
                    prev.Children.Add(new TextBlock
                    {
                        Text = $"   {r.Lsx}{(r.Grp.Length > 0 ? "  (chung cont " + r.Grp + ")" : "")} · {StLabel[r.TrangThai]}" + (dup ? "  — đã có, bỏ qua" : other.Count > 0 ? "  — ⚠ đã có lịch ngày " + string.Join(", ", other) : ""),
                        FontSize = 12.5, Foreground = dup ? Muted : AppTheme.Text,
                    });
                }
            }
            notes.Text = string.Join("\n", n.Select(x => "• " + x));
            go.IsEnabled = rows.Count > 0;
        }
        var deb = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; deb.Tick += (_, _) => { deb.Stop(); Parse(); };
        text.TextChanged += (_, _) => { deb.Stop(); deb.Start(); };
        var body = new DockPanel();
        var top = new StackPanel { Spacing = 6 };
        top.Children.Add(new TextBlock { Text = "Dán kế hoạch xuất hàng (tin nhắn / email). Mỗi \"Ngày dd/mm\" là 1 ngày; các LSX nối \"+\" trong 1 dòng = chung 1 container; dòng \"Nhập\" bỏ qua. Không ghi năm → năm gần hôm nay nhất. App tự dò Thành Phẩm Chính như khi thêm tay.", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        top.Children.Add(text); top.Children.Add(notes);
        DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        var btns = Ui.Buttons(close, go); DockPanel.SetDock(btns, Dock.Bottom); body.Children.Add(btns);
        body.Children.Add(new Border { BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 0), Child = new ScrollViewer { Content = prev } });
        var w = Win("⚡ Nhập nhanh lịch xuất", body, 720, 640);
        close.Click += (_, _) => w.Close();
        go.Click += async (_, _) =>
        {
            go.IsEnabled = false; LichXuatService.QuickResult? r = null; var list = rows.ToList();
            if (!await Ui.RunAsync(w, async () => r = await Task.Run(() => Svc.AddQuick(list)))) { go.IsEnabled = true; return; }
            var msg = new List<string>();
            if (r!.Added.Count > 0) msg.Add($"✅ Đã thêm {r.Added.Count} lịch:\n  " + string.Join("\n  ", r.Added));
            if (r.Dup.Count > 0) msg.Add("↩ Đã có sẵn (bỏ qua): " + string.Join(", ", r.Dup));
            if (r.NotFound.Count > 0) msg.Add("⚠ Chưa dò được (vẫn lưu lịch):\n  " + string.Join("\n  ", r.NotFound));
            if (r.OtherDay.Count > 0) msg.Add("⚠ Kế hoạch có thể đã đổi:\n  " + string.Join("\n  ", r.OtherDay));
            await Ui.Message(w, string.Join("\n\n", msg), "Nhập nhanh");
            w.Close();
        };
        w.Opened += (_, _) => text.Focus();
        await Ui.Show(this, w);
        PaintAll();
    }

    // ------------------------------------------------------------------ số cont từ email

    private async Task ContMailDialog(List<string> ids)
    {
        var g = Svc.Group(ids); if (g.Count == 0) return;
        var now = g.Select(z => z.ContSo).FirstOrDefault(c => c.Length > 0) ?? "";
        var text = new TextBox { AcceptsReturn = true, Height = 170, TextWrapping = TextWrapping.Wrap, Watermark = "Họ tên tài xế: …\nSố điện thoại: …\nSố xe: …\nSố cont: ABCD1234567\nSố seal: …" };
        var view = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Content = "Cập nhật số cont", Classes = { "primary" }, IsEnabled = false }; var close = new Button { Content = "Đóng", IsCancel = true };
        string cont = "";
        text.TextChanged += (_, _) =>
        {
            var r = LichXuatService.ReadXeMail(text.Text ?? ""); ok.IsEnabled = r.Ok; cont = r.Cont;
            view.Foreground = r.Ok ? AppTheme.Text : B("#B42318");
            view.Text = (text.Text ?? "").Trim().Length == 0 ? "" : r.Ok ? $"Số cont: {r.Cont}{(now.Length > 0 && now != r.Cont ? " (thay cho " + now + ")" : "")}\n" + string.Join(" · ", new[] { r.Xe.SoXe.Length > 0 ? "Xe " + r.Xe.SoXe : "", r.Xe.Seal.Length > 0 ? "Seal " + r.Xe.Seal : "", r.Xe.TaiXe.Length > 0 ? "Tài xế " + r.Xe.TaiXe : "" }.Where(s => s.Length > 0)) : r.Error;
        };
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { Text = $"Dán nội dung email «Thông tin xe» cho {string.Join(", ", g.Select(z => Label(z.Lsx)).Distinct())} (không cần tạo List / phiếu trước{(now.Length > 0 ? " · đang ghi cont " + now : "")}).", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(text); body.Children.Add(view); body.Children.Add(Ui.Buttons(close, ok));
        var w = Ui.Dialog("Số cont từ email", body, 560);
        close.Click += (_, _) => w.Close();
        ok.Click += async (_, _) =>
        {
            if (!await Ui.Run(w, () => Svc.SetContSo(ids, cont))) return;
            var ph = Svc.StateOf(g).Cont;
            if (ph.Length > 0 && ph != cont) await Ui.Message(w, $"Đã ghi cont {cont} vào lịch, nhưng Phiếu xuất kho của LSX này đang ghi cont {ph} nên lịch vẫn hiện số của phiếu — sửa số cont trong phiếu để đồng bộ.");
            w.Close();
        };
        w.Opened += (_, _) => text.Focus();
        await Ui.Show(this, w);
        PaintAll();
    }

    // ------------------------------------------------------------------ ✉ Email

    /// <summary>Hộp Email đang / vừa mở (công cụ chụp màn hình kiểm tra).</summary>
    internal Window? EmailWindow;
    internal Task OpenEmailForTest() => EmailDialog();

    private async Task EmailDialog()
    {
        var waitBox = new StackPanel { Spacing = 6 }; var histBox = new StackPanel { Spacing = 4 };
        var status = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Muted };
        var scan = new Button { Content = "⟳ Quét hộp thư ngay", Classes = { "primary" } };
        // 1.77: trạng thái kết nối Gmail luôn hiện (✓ đã kết nối … / ✖ lỗi …) — trước đây không biết đã kết nối được chưa
        var conn = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
        void PaintConn()
        {
            var st = Svc.MailStatus;
            conn.Text = "Kết nối Gmail: " + (!Svc.MailCfg().Ready ? "chưa khai báo (tab Cài đặt Gmail)" : st.Length > 0 ? st : "chưa kiểm tra — bấm «Lưu & kiểm tra kết nối» ở tab Cài đặt Gmail");
            conn.Foreground = st.StartsWith("✓") ? B("#1E7B4A") : st.StartsWith("✖") || !Svc.MailCfg().Ready ? B("#B42318") : Muted;
        }
        void Prog(string m) => Avalonia.Threading.Dispatcher.UIThread.Post(() => status.Text = m);
        Window? w = null;
        void Paint()
        {
            var rows = Svc.EmailRows();
            var (trust, only) = Svc.TrustCfg();
            var wait = rows.Where(r => r.TrangThai.Length == 0).Where(r => !only || trust.Length == 0 || LichXuatEmail.Trusted(r.NguoiGui, trust)).OrderBy(r => r.NhanLuc).ToList();
            var hidden = rows.Count(r => r.TrangThai.Length == 0) - wait.Count;
            waitBox.Children.Clear();
            if (wait.Count == 0) waitBox.Children.Add(new TextBlock { Text = "Không có thư chờ duyệt." + (hidden > 0 ? $" ({hidden} thư của người không tin cậy đang ẩn — bỏ chọn «Chỉ hiện thư người tin cậy» ở Cài đặt để xem.)" : ""), Foreground = Muted });
            foreach (var r in wait)
            {
                var c = new StackPanel { Spacing = 3 };
                var h = new WrapPanel { ItemSpacing = 6 };
                h.Children.Add(Pill(r.HanhDongTen, r.HanhDong == "huy" ? "#FDE7E7" : r.HanhDong == "doi" ? "#FFF4CC" : "#E3F2FB", r.HanhDong == "huy" ? "#B42318" : r.HanhDong == "doi" ? "#8A5A00" : "#155E8C"));
                h.Children.Add(new TextBlock { Text = "LSX " + r.Lsx, FontWeight = FontWeight.Bold, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center });
                h.Children.Add(new TextBlock { Text = r.NguoiGui + " · " + r.NhanLuc.ToString("dd/MM HH:mm"), FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
                c.Children.Add(h);
                c.Children.Add(new TextBlock { Text = r.TieuDe, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
                if (r.CanCu.Length > 0) c.Children.Add(new TextBlock { Text = "Căn cứ: " + r.CanCu, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
                else if (r.GhiChu.Length > 0) c.Children.Add(new TextBlock { Text = r.GhiChu, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, MaxHeight = 60 });
                if (r.KetQua.Length > 0) c.Children.Add(new TextBlock { Text = r.KetQua, FontSize = 12, Foreground = B("#B42318"), TextWrapping = TextWrapping.Wrap });
                var why = LichXuatEmail.Guard(r, Svc.Rows, DateTime.Now);
                if (why.Length > 0) c.Children.Add(new TextBlock { Text = "Chưa tự xử lý vì: " + why, FontSize = 11.5, Foreground = Muted });
                var act = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
                var d = new CalendarDatePicker { SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd/MM/yyyy", Width = 130, SelectedDate = IsIso(r.Ngay) ? DateOf(r.Ngay) : null, Watermark = "chọn ngày" };
                var gio = new TextBox { Text = r.Gio, Watermark = "giờ", Width = 70 };
                act.Children.Add(new TextBlock { Text = "Ngày:", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }); act.Children.Add(d); act.Children.Add(gio);
                var id = r.Id;
                act.Children.Add(Btn("✔ Duyệt", async () =>
                {
                    string m = ""; var nd = d.SelectedDate is { } dd ? Iso(dd) : ""; var g2 = gio.Text ?? "";
                    if (await Ui.RunAsync(w!, async () => m = await Task.Run(() => Svc.Decide(id, true, nd, g2)))) { status.Text = "✓ " + m; Paint(); }
                }, primary: true));
                act.Children.Add(Btn("Bỏ qua", async () => { if (await Ui.Run(w!, () => Svc.Decide(id, false))) Paint(); }));
                c.Children.Add(act);
                waitBox.Children.Add(Card(c, 10));
            }
            histBox.Children.Clear();
            foreach (var r in rows.Where(r => r.TrangThai.Length > 0).OrderByDescending(r => r.XuLyLuc ?? r.NhanLuc).Take(150))
            {
                var row = new DockPanel();
                if (r.TrangThai == "Đã duyệt" && r.HanhDong != "xe" || r.TrangThai == "Bỏ qua")
                { var id = r.Id; var u = Btn(r.TrangThai == "Bỏ qua" ? "Đưa về chờ duyệt" : "↶ Hoàn tác", async () => { string m = ""; if (await Ui.Run(w!, () => m = Svc.Undo(id))) { status.Text = m; Paint(); } }, fs: 11.5); DockPanel.SetDock(u, Dock.Right); row.Children.Add(u); }
                var s = new StackPanel();
                s.Children.Add(new TextBlock { Text = $"{(r.XuLyLuc ?? r.NhanLuc):dd/MM HH:mm} · {r.TrangThai}{(r.TuDong ? " (tự động)" : "")} · {r.HanhDongTen} · LSX {r.Lsx}", FontSize = 12.5, FontWeight = FontWeight.SemiBold });
                s.Children.Add(new TextBlock { Text = (r.KetQua.Length > 0 ? r.KetQua : r.TieuDe) + " — " + r.NguoiGui, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
                row.Children.Add(s);
                histBox.Children.Add(new Border { Child = row, Padding = new Thickness(4, 4), BorderBrush = Line_, BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            var last = Svc.LastScan;
            // 1.74: TextBlock.Text mặc định null → trước đây lỗi NullReference ngay khi mở hộp Email (không vào được Cài đặt Gmail)
            status.Text = ((status.Text ?? "").StartsWith("✓") ? status.Text + "\n" : "") + (last is { } l ? "Lần quét gần nhất: " + l.ToLocalTime().ToString("HH:mm dd/MM") : "Chưa quét lần nào.") + (Svc.MailCfg().Ready ? (Svc.MailCfg().Auto ? $" · tự quét mỗi {Svc.MailCfg().IntervalMin} phút" : " · tự quét đang tắt") : " · chưa khai báo Gmail (tab Cài đặt)");
        }
        scan.Click += async (_, _) =>
        {
            scan.IsEnabled = false; status.Text = "Đang đọc hộp thư…";
            LichXuatService.ScanResult? r = null;
            try { r = await Svc.ScanEmailAsync(default, Prog); status.Text = $"✓ {r.New} thư mới · tự xử lý {r.Auto} · số cont {r.ContSet} lịch · thông tin xe {r.XeFilled} phiếu · {r.Waiting} thư chờ duyệt." + (r.Notes.Count > 0 ? "\n" + string.Join("\n", r.Notes) : ""); }
            catch (Exception e) { status.Text = "✖ " + (e is KhoLanh.Core.Services.BusinessException ? e.Message : e.Message); }
            scan.IsEnabled = true; var keep = status.Text; Paint(); status.Text = keep; PaintConn();
        };

        // cài đặt
        var cfg = Svc.MailCfg(); var (tr, onlyT) = Svc.TrustCfg();
        var checkRes = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6), Tag = "keep" };
        var email = new TextBox { Text = cfg.Email, Watermark = "ten@gmail.com" };
        var pass = new TextBox { PasswordChar = '•', Watermark = cfg.PassEnc.Length > 0 ? "(đã lưu — để trống nếu không đổi)" : "Mật khẩu ứng dụng 16 ký tự" };
        var kw = new TextBox { Text = cfg.Keywords, Watermark = string.Join(", ", LichXuatEmail.DefaultKeywords) };
        var allow = new TextBox { Text = cfg.Allow, Watermark = "(trống = mọi người gửi) ví dụ: @anan.com.vn, logistics@" };
        var trust = new TextBox { Text = tr, Watermark = "địa chỉ / tên miền người gửi được tự xử lý, cách nhau dấu phẩy" };
        var onlyTr = new CheckBox { Content = "Chỉ hiện thư của người tin cậy ở mục chờ duyệt", IsChecked = onlyT };
        var auto = new CheckBox { Content = "Tự quét hộp thư khi mở phần mềm và tự xử lý thư của người tin cậy", IsChecked = cfg.Auto };
        var every = new NumericUpDown { Value = cfg.IntervalMin, Minimum = 2, Maximum = 240, Width = 120, FormatString = "0" };
        var days = new NumericUpDown { Value = cfg.Days, Minimum = 1, Maximum = 60, Width = 120, FormatString = "0" };
        var folder = new TextBox { Text = cfg.Folder };
        var saveCfg = Btn("💾 Lưu cài đặt", async () =>
        {
            var c = Svc.MailCfg(); c.Email = email.Text ?? ""; c.Keywords = kw.Text ?? ""; c.Allow = allow.Text ?? ""; c.Auto = auto.IsChecked == true;
            c.IntervalMin = (int)(every.Value ?? 10); c.Days = (int)(days.Value ?? 7); c.Folder = folder.Text ?? "INBOX";
            if (await Ui.Run(w!, () => { Svc.SaveMailCfg(c, pass.Text); Svc.SaveTrust(trust.Text ?? "", onlyTr.IsChecked == true); }))
            {
                pass.Text = ""; Svc.StartAutoEmail(); status.Text = "✓ Đã lưu cài đặt. Đang kiểm tra kết nối…"; Paint(); status.Text = "Đang kiểm tra kết nối…";
                try { checkRes.Text = await Svc.CheckMailAsync(default, Prog); checkRes.Foreground = B("#1E7B4A"); }
                catch (Exception e) { checkRes.Text = "✖ " + e.Message; checkRes.Foreground = B("#B42318"); }
                status.Text = checkRes.Text; PaintConn();
            }
        }, primary: true);
        saveCfg.Content = "💾 Lưu & kiểm tra kết nối";
        var set = new StackPanel { Spacing = 2 };
        set.Children.Add(checkRes);
        set.Children.Add(new TextBlock { Text = "Kết nối Gmail bằng IMAP (chỉ ĐỌC thư, không gửi / xóa). Cần bật Xác minh 2 bước rồi tạo «Mật khẩu ứng dụng» ở tài khoản Google — KHÔNG dùng mật khẩu Gmail. Mật khẩu được mã hóa trên máy này.", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        set.Children.Add(Lb("Địa chỉ Gmail")); set.Children.Add(email); set.Children.Add(Lb("Mật khẩu ứng dụng")); set.Children.Add(pass);
        set.Children.Add(Lb("Thư mục (Gmail: [Gmail]/All Mail để thấy cả thư đã gửi trong chuỗi)")); set.Children.Add(folder);
        set.Children.Add(Lb("Từ khóa trên tiêu đề (trống = mặc định)")); set.Children.Add(kw);
        set.Children.Add(Lb("Chỉ nhận thư từ")); set.Children.Add(allow);
        set.Children.Add(Lb("Người gửi tin cậy (tự áp vào lịch nếu qua lưới an toàn: ngày rõ ràng, trong 60 ngày tới, LSX khớp đúng 1 lịch…)")); set.Children.Add(trust); set.Children.Add(onlyTr);
        var nums = new WrapPanel { ItemSpacing = 14 }; nums.Children.Add(new StackPanel { Children = { Lb("Quét mỗi (phút)"), every } }); nums.Children.Add(new StackPanel { Children = { Lb("Đọc thư trong (ngày)"), days } });
        set.Children.Add(nums); set.Children.Add(auto); set.Children.Add(saveCfg.Also(b => b.Margin = new Thickness(0, 8, 0, 0)));

        // thử đọc thư
        var tSub = new TextBox { Watermark = "Tiêu đề thư (nếu có)" }; var tBody = new TextBox { AcceptsReturn = true, Height = 150, TextWrapping = TextWrapping.Wrap, Watermark = "Dán nội dung thư…" };
        var tRes = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        void Try()
        {
            if ((tSub.Text ?? "").Trim().Length == 0 && (tBody.Text ?? "").Trim().Length == 0) { tRes.Text = ""; return; }
            var k = LichXuatEmail.ThuDoc(tBody.Text ?? "", tSub.Text ?? "", DateTime.Now);
            tRes.Text = k.HanhDong == "khac" ? (k.Loai == "list" ? "Thư thông báo gửi List — chỉ để xem, không đổi lịch." : "Thư chỉ để xem — không đổi lịch.") :
                $"→ {LichXuatEmail.Act[k.HanhDong]}" + (k.Ngay.Length > 0 ? " · ngày " + Vn(k.Ngay) : k.HanhDong is "doi" ? " · chưa rõ ngày (người duyệt chọn)" : "") + (k.Gio.Length > 0 ? " · giờ " + k.Gio : "") + (k.CanCu.Length > 0 ? "\nCăn cứ: " + k.CanCu : "") + (k.HanhDong == "xe" ? "\nSố cont: " + LichXuatService.ReadXeMail(k.GhiChu).Cont : "");
        }
        tSub.TextChanged += (_, _) => Try(); tBody.TextChanged += (_, _) => Try();
        var tryBox = new StackPanel { Spacing = 6 };
        tryBox.Children.Add(new TextBlock { Text = "Dán một thư để xem app SẼ hiểu thế nào (không ghi gì):", FontSize = 12.5 });
        tryBox.Children.Add(tSub); tryBox.Children.Add(tBody); tryBox.Children.Add(tRes);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Chờ duyệt", Content = new ScrollViewer { Content = waitBox } });
        tabs.Items.Add(new TabItem { Header = "Đã xử lý", Content = new ScrollViewer { Content = histBox } });
        tabs.Items.Add(new TabItem { Header = "Cài đặt Gmail", Content = new ScrollViewer { Content = set } });
        tabs.Items.Add(new TabItem { Header = "Thử đọc thư", Content = tryBox });
        var root = new DockPanel();
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        var tb = new WrapPanel { ItemSpacing = 8 }; tb.Children.Add(scan); tb.Children.Add(new TextBlock { Text = "Thư gốc có LSX trên tiêu đề = tạo lịch (ngày ETD) · thư trả lời = đổi / hủy lịch · thư «Thông tin xe» tự ghi số cont, không cần duyệt.", FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 });
        top.Children.Add(tb); top.Children.Add(conn); top.Children.Add(status); PaintConn();
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); root.Children.Add(tabs);
        w = Win("✉ Email cập nhật lịch xuất", root, 900, 720);
        EmailWindow = w;
        Paint();
        if (!Svc.MailCfg().Ready) tabs.SelectedIndex = 2;
        await Ui.Show(this, w);
        PaintAll();
    }
}

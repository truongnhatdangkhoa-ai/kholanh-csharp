using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.KiemKe;
using KhoLanh.Core.Services;
using static KhoLanh.Core.KiemKe.KiemKeCore;

namespace KhoLanh.App.Views;

/// <summary>
/// Tab "⚡ Kiểm kê nhanh" (theo kk-nhanh.js của app cũ, PC 3.32): người dùng chỉ làm 3 việc — ① chọn / thả file ② xem "Việc cần làm" ③ bấm Chốt.
/// File thả vào được nhận loại theo tiêu đề cột (<see cref="KkNhanh.DetectKind"/>) rồi nạp bằng ĐÚNG hàm của module gốc (Tồn kho An An / Tồn kho gửi:
/// Preview + Commit có hoàn tác; Kế toán: lưu số kế toán của Đối chiếu kiểm kê). Không nhận ra thì hỏi, không đoán.
/// Không sửa số liệu âm thầm: sửa mã gõ sai, chốt kỳ… đều đi qua hàm của module (có xác nhận + nhật ký).
/// </summary>
public sealed partial class KiemKeView
{
    private readonly StackPanel _nSide = new() { Spacing = 8, Margin = new Thickness(0, 8, 10, 8) };
    private readonly WrapPanel _nChips = new() { ItemSpacing = 6, LineSpacing = 6 };
    private readonly TextBox _nQ = new() { Watermark = "Tìm mã, tên…", Width = 200 };
    private readonly TextBlock _nInfo = new() { Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly GridModel _nm = new() { ReadOnly = true };
    private SheetGrid _ng = null!;
    private List<Row> _nRows = new();
    private string _nFilt = "LECH";
    private readonly List<(string Msg, string Tone)> _nLog = new();
    private bool _nBusy;

    private Control BuildNhanh()
    {
        _ng = new SheetGrid(_nm) { SpareRows = 0, CellBackground = NBg };
        _ng.DoubleTapped += (_, _) => OpenInAn();
        var deb = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        deb.Tick += (_, _) => { deb.Stop(); RenderNGrid(); };
        _nQ.TextChanged += (_, _) => { deb.Stop(); deb.Start(); };

        var bar = new ToolRow { Margin = new Thickness(0, 8, 0, 6) };
        bar.Children.Add(_nChips); bar.Children.Add(_nQ);
        bar.Children.Add(Btn("⧉ Sao chép", async () => await CopyView(), tip: "Sao chép bảng đang xem để dán vào Excel"));
        if (_export) bar.Children.Add(Btn("⬇ Excel", async () => await ExportView(), tip: "Xuất bảng đang xem ra Excel"));
        if (_edit) bar.Children.Add(Btn("Sửa mã dòng đang chọn", async () => await FixOne(), tip: "Chỉ khi app nghi mã kiểm kê gõ sai và đã tìm được mã An An tương ứng"));
        bar.Children.Add(_nInfo);
        var main = new DockPanel(); DockPanel.SetDock(bar, Dock.Top); main.Children.Add(bar);
        var hint = Muted("Đúp chuột 1 dòng để mở ở tab Đối chiếu An An (chi tiết, nguyên nhân, cách xử lý).");
        DockPanel.SetDock(hint, Dock.Bottom); main.Children.Add(hint);
        main.Children.Add(Framed(_ng));

        var side = new ScrollViewer { Content = _nSide, Width = 430 };
        var root = new DockPanel(); DockPanel.SetDock(side, Dock.Left); root.Children.Add(side); root.Children.Add(main);
        DragDrop.SetAllowDrop(root, true);
        root.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; });
        root.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p is not null && Regex.IsMatch(p!, @"\.(xlsx|xlsm|xls|csv)$", RegexOptions.IgnoreCase)).Select(p => p!).ToList() ?? new();
            if (files.Count == 0) { await Ui.Message(this, "Chỉ nhận file Excel (.xlsx, .xls) hoặc .csv."); return; }
            await LoadFiles(files);
        });
        return root;
    }

    private KkNhanh.Ctx NCtx() => new(Svc.Lines.Count > 0, Svc.Stock.Count > 0, Svc.Kt.Map.Count > 0, 0, Svc.Locked, Svc.Chosen.Count);

    private void RenderNhanh()
    {
        var R = KkNhanh.Rows(Svc.T.Rows); var S = KkNhanh.Summarize(R); var C = NCtx(); var has = C.HasKK && C.HasAn;
        _nSide.Children.Clear();
        var drop = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 10), IsEnabled = !_nBusy,
            Content = new StackPanel { Children = { Label("⚡ Chọn / thả file vào đây — app tự nhận & đối chiếu", true, 13.5), Muted("1 hoặc nhiều file Excel: Tồn kho An An · Kế toán · Tồn kho gửi. App nhận loại theo tiêu đề cột rồi nạp bằng đúng chức năng của module đó (có hoàn tác).") } },
        };
        drop.Click += async (_, _) => await PickFiles();
        _nSide.Children.Add(drop);

        var srcs = new StackPanel { Spacing = 3 };
        void Src(string name, bool ok, string sub) => srcs.Children.Add(new TextBlock { Text = (ok ? "✓ " : "○ ") + name + " — " + sub, FontSize = 12.5, Foreground = ok ? AppTheme.Brush("#1E7B34") : AppTheme.Muted, TextWrapping = TextWrapping.Wrap });
        Src("Kiểm kê", C.HasKK, C.HasKK ? $"{C.NPages} trang · {Svc.Lines.Count:#,0} dòng" : "chọn ở tab Số liệu kiểm kê");
        Src("Tồn kho An An", C.HasAn, C.HasAn ? (Svc.StockSrc == "snap" ? "bản chụp tồn " + Svc.SnapInfo : $"{Svc.StockFile} · ngày {Svc.StockDate}") : "chưa nạp");
        Src("Kế toán", C.HasKT, C.HasKT ? $"{Svc.Kt.Map.Count:#,0} mã" : "chưa nạp (không bắt buộc)");
        Src("Tồn kho gửi", Svc.GuiOn && Svc.Gui.Map.Count > 0, Svc.GuiOn ? (Svc.GuiInfo.Length > 0 ? Svc.GuiInfo : "đã bật") : "chưa cộng vào đối chiếu (không bắt buộc)");
        _nSide.Children.Add(Card(srcs));

        if (_nLog.Count > 0)
        {
            var lg = new StackPanel { Spacing = 2 };
            foreach (var (m, t) in _nLog.TakeLast(5)) lg.Children.Add(new TextBlock { Text = m, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = t == "bad" ? Warn : t == "ok" ? AppTheme.Brush("#1E7B34") : AppTheme.Muted });
            _nSide.Children.Add(lg);
        }
        if (_nBusy) _nSide.Children.Add(Label("Đang nạp và đối chiếu…", true, fg: Accent));

        if (has)
        {
            var k = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            k.Children.Add(Kpi("Đã khớp", S.Khop.ToString("#,0", Ui.Vi), AppTheme.Brush("#2E7D32"), () => SetNFilt(Khop), _nFilt == Khop));
            k.Children.Add(Kpi("Lệch số lượng / kg", (S.Thua + S.Thieu + S.LechKg).ToString("#,0", Ui.Vi), S.Thua + S.Thieu + S.LechKg > 0 ? Warn : null, () => SetNFilt("LECHSL"), _nFilt == "LECHSL"));
            k.Children.Add(Kpi("Chỉ có ở kiểm kê", S.ChiKK.ToString("#,0", Ui.Vi), S.ChiKK > 0 ? Warn : null, () => SetNFilt(ChiKK), _nFilt == ChiKK));
            k.Children.Add(Kpi("Chưa kiểm kê", S.ChuaKK.ToString("#,0", Ui.Vi), S.ChuaKK > 0 ? Warn : null, () => SetNFilt(ChuaKK), _nFilt == ChuaKK));
            var pct = S.Total > 0 ? (int)Math.Round(S.Khop * 100.0 / S.Total) : 0;
            k.Children.Add(Kpi($"đã khớp · lệch {S.KgLech.ToString("#,0.#", Ui.Vi)} kg", pct + "%", null));
            _nSide.Children.Add(k);
        }

        var T = KkNhanh.TodoList(S, C);
        var td = new StackPanel { Spacing = 6 };
        td.Children.Add(Label(T.Count > 0 ? "Việc cần làm" : has ? "✅ Không còn việc cần làm" : "Việc cần làm", true, 14));
        if (T.Count == 0 && has) td.Children.Add(Muted("Tồn kho và kiểm kê đã khớp hoàn toàn. Có thể chốt kỳ."));
        foreach (var t in T)
        {
            var row = new DockPanel();
            var label = t.Id == "ten" ? "Mở Danh mục mã hàng" : t.Label;
            var b = Btn(label, async () => await NAct(t.Act), primary: t.Tone == "ok");
            b.IsEnabled = t.Act switch { "fix" => _edit && !Svc.Locked, "pick" => !_nBusy, _ => true };
            DockPanel.SetDock(b, Dock.Right); row.Children.Add(b);
            row.Children.Add(new TextBlock { Text = t.Icon + " " + t.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                Foreground = t.Tone == "bad" ? Warn : t.Tone == "warn" ? AppTheme.Brush("#8A5A00") : AppTheme.Text });
            td.Children.Add(row);
        }
        if (!has) td.Children.Add(Muted("Chọn file tồn kho ở ô trên để bắt đầu. Số liệu kiểm kê lấy từ các trang Nhập liệu đã chọn ở tab Số liệu kiểm kê."));
        _nSide.Children.Add(Card(td));

        if (has)
        {
            var rd = new StackPanel { Spacing = 3 };
            rd.Children.Add(Label("Sẵn sàng chốt?", true, 14));
            foreach (var c in Svc.Readiness())
                rd.Children.Add(new TextBlock { Text = (c.Ok == "ok" ? "✓ " : c.Ok == "warn" ? "! " : "✕ ") + c.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                    Foreground = c.Ok == "ok" ? AppTheme.Brush("#1E7B34") : c.Ok == "warn" ? AppTheme.Brush("#8A5A00") : Warn });
            var acts = new ToolRow { Margin = new Thickness(0, 6, 0, 0) };
            if (_export)
            {
                acts.Children.Add(Btn("⬇ Báo cáo chênh lệch", async () => await NExport(true), tip: "File Excel chỉ gồm các mã đang lệch"));
                acts.Children.Add(Btn("⬇ File tổng đối chiếu", async () => await NExport(false), tip: "File Excel đầy đủ: Đối chiếu An An, kế toán, chi tiết kiểm kê…"));
            }
            if (_edit)
            {
                var block = Svc.Readiness().Any(c => c.Ok == "block");
                var bc = Btn("🔒 Chốt kỳ kiểm kê…", async () => await DoChot(), primary: true); bc.IsEnabled = !block;
                if (block) ToolTip.SetTip(bc, "Còn mục ✕ ở danh sách trên — xử lý trước khi chốt");
                acts.Children.Add(bc);
            }
            rd.Children.Add(acts);
            _nSide.Children.Add(Card(rd));
        }
        var adv = Btn("Chế độ nâng cao ▸", () => _tabs.SelectedIndex = 1, tip: "Đối chiếu An An · Số liệu kiểm kê · Kế toán · Tồn kho gửi · Theo vị trí · Lệnh đếm · Chốt kỳ — đầy đủ cột, nhật ký");
        _nSide.Children.Add(adv);

        _nChips.Children.Clear();
        void Chip(string key, string text, int? n) { var b = Btn(text + (n is { } x ? $" ({x:#,0})" : ""), () => SetNFilt(key)); if (_nFilt == key) b.Classes.Add("primary"); _nChips.Children.Add(b); }
        Chip("LECH", "Cần xử lý", S.Open); Chip(Thua, "Thừa", S.Thua); Chip(Thieu, "Thiếu", S.Thieu); Chip(LechKg, "Lệch kg", S.LechKg);
        Chip(ChiKK, "Chỉ có ở kiểm kê", S.ChiKK); Chip(ChuaKK, "Chưa kiểm kê", S.ChuaKK); Chip(Khop, "Khớp", S.Khop); Chip("ALL", "Tất cả", S.Total);
        RenderNGrid();
    }
    private static Border Card(Control c) => new() { BorderBrush = Line_, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8), Child = c };
    private void SetNFilt(string f) { _nFilt = f; RenderNhanh(); }

    private bool HasKtCols => Svc.T.HasKT;
    private void RenderNGrid()
    {
        _nRows = KkNhanh.FilterRows(KkNhanh.Rows(Svc.T.Rows), _nFilt, _nQ.Text);
        var cols = new List<GridColumn>
        {
            new("ma", "Mã hàng", 230), new("kkk", "Kiểm kê (kiện)", 90, "number"), new("kkg", "Kiểm kê (kg)", 95, "number"), new("tk", "Tồn An An (kiện)", 100, "number"), new("tkg", "Tồn An An (kg)", 100, "number"),
            new("dk", "Chênh (kiện)", 85, "number"), new("dkg", "Chênh (kg)", 90, "number"),
        };
        if (HasKtCols) { cols.Add(new("kt", "Kế toán (kg)", 95, "number")); cols.Add(new("kkt", "Tổng − KT (kg)", 100, "number")); }
        cols.Add(new("st", "Kết quả", 115)); cols.Add(new("nn", "Nguyên nhân (gợi ý)", 220));
        _nm.Load(cols, _nRows.Select(r =>
        {
            var v = new List<string> { r.Ma, Q(r.KkKien), Kg(r.KkKg), Q(r.TonKien), Kg(r.TonKg), D(r.DKien), D(r.DKg, true) };
            if (HasKtCols) { v.Add(Kg(r.KtKg)); v.Add(D(r.TongKt, true)); }
            v.Add(r.Status); v.Add(r.Nn is null ? "" : (r.Nn.Level == "xd" ? "● " : r.Nn.Level == "ng" ? "◐ " : "○ ") + r.Nn.Label + (r.Nn.FixTo is { } f && r.Nn.Side == "kk" ? " → " + f : ""));
            return v;
        }));
        _ng.Refresh();
        _nInfo.Text = $"{_nRows.Count:#,0} mã";
    }
    private IBrush? NBg(int r, int c)
    {
        if (r < 0 || r >= _nRows.Count) return null;
        var x = _nRows[r]; var st = HasKtCols ? 9 : 7;
        if (c == st) return StBrush(x.Status);
        if ((c == 5 && Math.Abs(x.DKien ?? 0) > 0.0005) || (c == 6 && Math.Abs(x.DKg ?? 0) > KgTol)) return BadBg;
        if (HasKtCols && c == 8 && Math.Abs(x.TongKt ?? 0) > KgTol) return WarnBg;
        return null;
    }

    private void OpenInAn()
    {
        var r = _ng.Row; if (r < 0 || r >= _nRows.Count) return;
        var ma = _nRows[r].Ma;
        _tabs.SelectedIndex = 1; _q.Text = ma; SetFlt("ALL");
    }

    // ------------------------------------------------------------------ thao tác

    private void Say(string m, string t = "i") { _nLog.Add((m, t)); if (_nLog.Count > 20) _nLog.RemoveAt(0); }

    private async Task NAct(string act)
    {
        if (act.StartsWith("filter:")) { SetNFilt(act[7..]); return; }
        switch (act)
        {
            case "pick": await PickFiles(); break;
            case "goto-tong": _tabs.SelectedIndex = 2; break;
            case "fix": await FixAll(); break;
            case "ten": (TopLevel.GetTopLevel(this) as MainWindow)?.Navigate("m3dm"); break;
        }
    }

    private async Task PickFiles()
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Chọn file tồn kho / kế toán", AllowMultiple = true,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Excel / CSV") { Patterns = new[] { "*.xlsx", "*.xlsm", "*.xls", "*.csv" } } },
        });
        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
        if (paths.Count > 0) await LoadFiles(paths);
    }

    /// <summary>Nạp từng file: nhận loại theo tiêu đề (không rõ → hỏi), rồi nạp bằng chức năng của module đó; xong thì đối chiếu lại.</summary>
    private async Task LoadFiles(List<string> paths)
    {
        if (_nBusy) return;
        if (!_edit) { await Ui.Message(this, "Tài khoản không có quyền Sửa của Đối chiếu kiểm kê."); return; }
        _nBusy = true; RenderNhanh();
        var s = App.Services;
        try
        {
            foreach (var path in paths)
            {
                var name = Path.GetFileName(path); KkNhanh.Detect d;
                try
                {
                    var book = await Task.Run(() => Core.TonKho.SheetText.Read(path));
                    d = KkNhanh.DetectBook(book.SheetNames.Select(n => (IReadOnlyList<IReadOnlyList<string?>>)book.Sheets[n].Take(80).Select(r => (IReadOnlyList<string?>)r).ToList()));
                }
                catch (Exception e) { Say($"✕ Không đọc được {name}: {Ui.Describe(e)}", "bad"); continue; }
                var kind = d.Kind;
                if (kind.Length == 0)
                {
                    var ch = await Ui.Choose(this, $"App chưa chắc “{name}” là loại file nào ({d.Why}). Đây là file gì?", "Chọn loại file", KkNhanh.Kinds.Values.ToList());
                    if (ch is null) { Say("Bỏ qua " + name); continue; }
                    kind = KkNhanh.Kinds.Keys.ElementAt(ch.Value.Index);
                }
                Say($"Nhận ra “{name}” là {KkNhanh.Kinds[kind]} — đang nạp…"); RenderNhanh();
                try
                {
                    switch (kind)
                    {
                        case KkNhanh.An:
                        {
                            if (!s.AnAn.CanEdit) throw new BusinessException("Cần quyền Sửa của module Tồn kho An An để nạp file tồn.");
                            await Task.Run(() => { if (!s.AnAn.Loaded) s.AnAn.Load(); });
                            var pv = await Task.Run(() => s.AnAn.Preview(path));
                            if (pv.Blocking) throw new BusinessException("File có lỗi nặng: " + string.Join("; ", pv.Check.Errors.Take(3)));
                            if (pv.Warns.Count > 0 && !await Ui.Confirm(this, $"“{name}” (Tồn kho An An) có cảnh báo:\n\n• " + string.Join("\n• ", pv.Warns.Take(6)) + "\n\nVẫn nạp?", "Nạp Tồn kho An An", "Vẫn nạp")) { Say("Bỏ qua " + name); continue; }
                            await Task.Run(() => s.AnAn.Commit(pv));
                            break;
                        }
                        case KkNhanh.Gui:
                        {
                            if (!s.Gui.CanEdit) throw new BusinessException("Cần quyền Sửa của module Tồn kho gửi để nạp file.");
                            await Task.Run(() => { if (!s.Gui.Loaded) s.Gui.Load(); });
                            var pv = await Task.Run(() => s.Gui.Preview(path));
                            if (pv.Blocking) throw new BusinessException("File có lỗi nặng — mở module Tồn kho gửi để xem chi tiết.");
                            await Task.Run(() => s.Gui.Commit(pv));
                            if (!Svc.GuiOn && !Svc.Locked) await Task.Run(() => Svc.SetGuiOn(true));
                            break;
                        }
                        default:
                        {
                            var rows = await Task.Run(() => KiemKeService.ReadKeToanFile(path));
                            await Task.Run(() => Svc.SaveKeToan(rows, "File " + name));
                            break;
                        }
                    }
                    Say($"✓ Đã nạp {KkNhanh.Kinds[kind]} từ {name}", "ok");
                }
                catch (BusinessException e) { Say($"✕ Không nạp được {name}: {e.Message}", "bad"); }
                catch (Exception e) { Say($"✕ Không nạp được {name}: {Ui.Describe(e)}", "bad"); }
            }
        }
        finally
        {
            _nBusy = false;
            try { await Task.Run(Svc.Load); } catch (Exception e) { Say("✕ Không đối chiếu lại được: " + Ui.Describe(e), "bad"); }
            RenderAll();
        }
    }

    private async Task FixOne()
    {
        var r = _ng.Row; if (r < 0 || r >= _nRows.Count) { await Ui.Message(this, "Chọn 1 dòng trong bảng."); return; }
        var x = _nRows[r];
        if (x.Nn is not { Code: "sai-ma", Side: "kk", FixTo: { } to }) { await Ui.Message(this, "Dòng này không có đề xuất sửa mã (chỉ mã kiểm kê nghi gõ sai mới sửa được ở đây)."); return; }
        if (!await Ui.Confirm(this, $"Đổi mã kiểm kê {x.Ma} → {to} ở trang Nhập liệu nguồn?" + (x.Nn.Strong ? "" : "\n\nLưu ý: số kg hai bên KHÔNG bằng nhau — hãy chắc chắn trước khi đổi."), "Sửa mã", "Đổi mã")) return;
        await Act(() => Svc.FixMa(new[] { (x.Key, to) }), $"Đã đổi {x.Ma} → {to}");
    }

    private (string[] Head, List<object?[]> Rows) ViewTable()
    {
        var LV = new Dictionary<string, string> { ["xd"] = "Xác định", ["ng"] = "Nghi ngờ", ["cx"] = "Chưa xác định" };
        var head = new[] { "STT", "Mã hàng", "Kiểm kê (kiện)", "Kiểm kê (kg)", "Tồn An An (kiện)", "Tồn An An (kg)", "Chênh (kiện)", "Chênh (kg)", "Kết quả", "Mức chắc chắn", "Nguyên nhân" };
        return (head, _nRows.Select((r, i) => new object?[] { i + 1, r.Ma, r.KkKien, r.KkKg, r.TonKien, r.TonKg, r.DKien, r.DKg, r.Status, r.Nn is null ? "" : LV.GetValueOrDefault(r.Nn.Level, ""), r.Nn?.Label ?? "" }).ToList());
    }
    private async Task CopyView()
    {
        var (head, rows) = ViewTable(); if (rows.Count == 0) { await Ui.Message(this, "Không có dòng nào để sao chép."); return; }
        string C(object? v) => v switch { null => "", double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => Regex.Replace(v.ToString() ?? "", @"[\t\r\n]+", " ") };
        var txt = string.Join("\n", new[] { string.Join("\t", head) }.Concat(rows.Select(r => string.Join("\t", r.Select(C)))));
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { await cb.SetTextAsync(txt); _status.Text = $"Đã sao chép {rows.Count} dòng — dán vào Excel (Ctrl+V)."; }
    }
    private async Task ExportView()
    {
        var (head, rows) = ViewTable(); if (rows.Count == 0) { await Ui.Message(this, "Không có dòng nào để xuất."); return; }
        var path = await Ui.SaveFile(this, "Xuất bảng Kiểm kê nhanh", $"KiemKeNhanh_{DateTime.Today:yyyy-MM-dd}.xlsx", "xlsx", "Excel"); if (path is null) return;
        if (await Ui.Run(this, () => Core.SoKho.SoKhoService.ExportTable("Kiem ke nhanh", head, rows, path))) Ui.Exported(this, path);
    }
    private async Task NExport(bool onlyLech)
    {
        var path = await Ui.SaveFile(this, onlyLech ? "Báo cáo chênh lệch kiểm kê" : "File tổng đối chiếu kiểm kê",
            Ui.SafeFileName((onlyLech ? "Chenh lech kiem ke " : "Doi chieu kiem ke ") + DateTime.Today.ToString("dd.MM.yyyy")) + ".xlsx", "xlsx", "Excel");
        if (path is null) return;
        var rows = onlyLech ? KkNhanh.FilterRows(KkNhanh.Rows(Svc.T.Rows), "LECH", null) : null;
        if (await Ui.Run(this, () => Svc.ExportReport(path, rows))) Ui.Exported(this, path);
    }
}

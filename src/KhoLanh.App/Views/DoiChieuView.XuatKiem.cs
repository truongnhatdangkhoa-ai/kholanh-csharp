using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.App.Controls;
using KhoLanh.Core.ChungTu;
using KhoLanh.Core.DoiChieu;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

/// <summary>
/// Tab "Xuất kiểm" của Đối chiếu hàng ngày (theo dcx-xuatkiem.js của app cũ): chọn file GỐC của phần mềm kho → tách riêng loại "Xuất kiểm hàng", gộp theo (Ngày, Mã hàng)
/// thành danh sách rút gọn 8 cột → xuất file Excel mới. "Cập nhật tên hàng": tra tên chuẩn theo MÃ (Danh sách mã hàng, rồi Mã hóa sản phẩm của Tổng hợp), xem trước và chọn từng dòng.
/// KHÔNG ghi vào file gốc và KHÔNG ghi vào file Xuất thay thùng / Xuất rã đông đã liên kết; dữ liệu chỉ nằm trong bộ nhớ của tab cho tới khi bấm "Xuất file rút gọn".
/// </summary>
public sealed partial class DoiChieuView
{
    private XuatKiem.Result? _xk;
    private string _xkFile = "";
    private Dictionary<string, string>? _xkUndo;
    private readonly HashSet<string> _xkRenamed = new();
    private readonly GridModel _xkm = new() { ReadOnly = true };
    private SheetGrid? _xkg;
    private readonly TextBlock _xkInfo = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _xkWarn = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Brush("#B23B00") };
    private Button? _xkTen, _xkUndoBtn, _xkSave;

    private Control BuildXuatKiem()
    {
        _xkg = new SheetGrid(_xkm) { SpareRows = 0 };
        _xkg.CellBackground = (r, c) =>
        {
            if (_xk is null || r >= _xk.Groups.Count) return null;
            var g = _xk.Groups[r];
            if (c == 3 && _xkRenamed.Contains(g.Ngay + "|" + g.Ma)) return AppTheme.Brush("#E2EFDA");
            return g.NetLech ? AppTheme.Brush("#FFE699") : null;
        };
        var pick = Btn("📂 Chọn file gốc…", async () => await XkPick(), primary: true);
        _xkTen = Btn("Cập nhật tên hàng…", async () => await XkTen(), tip: "Tra tên chuẩn theo mã hàng để thay tên bị cắt trong file gốc — xem trước rồi mới áp"); _xkTen.IsEnabled = false;
        _xkUndoBtn = Btn("↶ Hoàn tác tên hàng", () => XkUndo(), tip: "Trả lại tên hàng như trong file gốc"); _xkUndoBtn.IsVisible = false;
        _xkSave = Btn("📤 Xuất file rút gọn (.xlsx)", async () => await XkSave()); _xkSave.IsEnabled = false;
        var bar = new WrapPanel { ItemSpacing = 6, LineSpacing = 4, Margin = new Thickness(0, 8, 0, 6) };
        bar.Children.Add(pick); bar.Children.Add(_xkTen); bar.Children.Add(_xkUndoBtn); bar.Children.Add(_xkSave);
        var hint = new TextBlock
        {
            Text = "Chọn file GỐC của phần mềm kho (báo cáo “Nhập xuất chi tiết”, .xlsx / .xlsm). App lấy riêng các dòng Xuất kiểm hàng, gộp theo Ngày + Mã hàng thành danh sách rút gọn (Ngày · Mã hàng · Tên hàng · TLg Net · Số lượng tồn · Net · SIZE · Hợp đồng). Không ghi vào file gốc hay hai file Xuất thay thùng / Xuất rã đông.",
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        };
        var top = new StackPanel { Spacing = 4 }; top.Children.Add(hint); top.Children.Add(bar); top.Children.Add(_xkInfo); top.Children.Add(_xkWarn);
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        root.Children.Add(new Border { BorderBrush = AppTheme.Brush("#D0D7E2"), BorderThickness = new Thickness(1), Child = _xkg });
        XkRender();
        return root;
    }

    private async Task XkPick()
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Chọn file gốc của phần mềm kho", AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Excel (.xlsx, .xlsm)") { Patterns = new[] { "*.xlsx", "*.xlsm" } } },
        });
        if (files.Count == 0 || Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]) is not { } path) return;
        XuatKiem.Result? res = null;
        _status.Text = "Đang đọc file gốc…";
        try { res = await Task.Run(() => XuatKiem.BuildFromBook(XuatKiem.ReadBook(path))); }
        catch (BusinessException e) { _status.Text = ""; await Ui.Message(this, e.Message, "Không đọc được file"); return; }
        catch (Exception e) { _status.Text = ""; await Ui.Message(this, Ui.Describe(e), "Lỗi"); return; }
        if (!res.Ok) { _status.Text = ""; await Ui.Message(this, res.Err.Length > 0 ? res.Err : "Không có dữ liệu.", "Không nhận ra file gốc"); return; }
        _xk = res; _xkFile = Path.GetFileName(path); _xkUndo = null; _xkRenamed.Clear();
        XkRender();
        _status.Text = res.Groups.Count > 0 ? $"Đã tách {res.Groups.Count:#,0} dòng Xuất kiểm hàng." : "File không có dòng Xuất kiểm hàng.";
    }

    private void XkRender()
    {
        var r = _xk;
        _xkTen!.IsEnabled = r is { Groups.Count: > 0 }; _xkSave!.IsEnabled = r is { Groups.Count: > 0 } && Svc.CanExport; _xkUndoBtn!.IsVisible = _xkUndo is not null;
        if (r is null)
        {
            _xkInfo.Text = "Chưa chọn file gốc."; _xkWarn.Text = "";
            _xkm.Load(Array.Empty<GridColumn>(), Array.Empty<string[]>()); _xkg!.Refresh(); return;
        }
        var other = string.Join(", ", r.Khac.Select(x => $"{x.Loai} {x.N:#,0} dòng"));
        _xkInfo.Text = $"File: {_xkFile} — {r.NRows:#,0} dòng · {r.NKiem:#,0} dòng Xuất kiểm hàng → gộp còn {r.Groups.Count:#,0} dòng" + (r.NGop > 0 ? $" ({r.NGop:#,0} dòng cùng ngày + mã được cộng dồn)" : "")
            + $" · Tổng SL {r.TotSl:#,0} · Tổng Net {r.TotNet:#,0.####} kg"
            + (other.Length > 0 ? $"\nKhông lấy: {other}" + (r.BoQua > 0 ? $" · bỏ {r.BoQua:#,0} dòng thiếu mã / ngày (dòng tổng)" : "") : r.BoQua > 0 ? $"\nBỏ {r.BoQua:#,0} dòng thiếu mã / ngày (dòng tổng)." : "");
        _xkWarn.Text = r.Warn.Count == 0 ? "" : $"⚠ {r.Warn.Count} cảnh báo:\n" + string.Join("\n", r.Warn.Take(6)) + (r.Warn.Count > 6 ? $"\n… và {r.Warn.Count - 6} cảnh báo nữa." : "");
        var cols = new[]
        {
            new GridColumn("stt", "STT", 45, "number"), new GridColumn("ngay", "Ngày chứng từ", 105), new GridColumn("ma", "Mã hàng", 190), new GridColumn("ten", "Tên hàng", 430),
            new GridColumn("tlg", "TLg Net", 75, "number"), new GridColumn("sl", "Số lượng tồn", 100, "number"), new GridColumn("net", "Net", 90, "number"), new GridColumn("size", "SIZE", 75), new GridColumn("hd", "Hợp đồng", 110),
        };
        var rows = r.Groups.Select((g, i) => new[]
        {
            (i + 1).ToString(), XuatKiem.Dmy(g.Ngay), g.Ma, g.Ten, N(g.Tlg), N(g.Sl) + (g.NDong > 1 ? $" ({g.NDong})" : ""), N(g.Net), g.Size, g.Hopdong,
        }).ToList();
        if (r.Groups.Count > 0) rows.Add(new[] { "", "Tổng", "", "", "", N(r.TotSl), N(r.TotNet), "", "" });
        _xkm.Load(cols, rows); _xkg!.Refresh();
    }

    /// <summary>Tra tên chuẩn theo mã: Danh sách mã hàng (Tổng hợp), rồi ghép từ Mã hóa sản phẩm.</summary>
    private Func<string, (string Ten, string Src)?> XkLookup()
    {
        var th = App.Services.TongHop;
        Dictionary<int, Dictionary<string, string>> seg = new();
        try
        {
            th.Load();
            seg = th.SegmentData.ToDictionary(p => p.Key, p => p.Value.GroupBy(e => e.Code.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Name));
        }
        catch { /* tra được tới đâu hay tới đó */ }
        string? Name(int s, string code) => seg.TryGetValue(s, out var d) && d.TryGetValue(code.ToUpperInvariant(), out var n) ? n : null;
        return ma =>
        {
            if (Svc.CodeList.TryGetValue(DcxEngine.NormMa(ma), out var l) && l.Ten.Trim().Length > 0) return (l.Ten.Trim(), "Danh sách mã hàng");
            var c = ChungTuService.Compose(ma, Name);
            return c.Ok ? (c.Ten, "Mã hóa sản phẩm") : null;
        };
    }

    private async Task XkTen()
    {
        var r = _xk; if (r is null || r.Groups.Count == 0) return;
        XuatKiem.TenPlan plan;
        try { plan = await Task.Run(() => { Svc.LoadDictionaries(); return XuatKiem.PlanTen(r.Groups, XkLookup()); }); }
        catch (Exception e) { await Ui.Message(this, Ui.Describe(e), "Lỗi"); return; }
        if (plan.List.Count == 0)
        {
            await Ui.Message(this, $"Không có dòng nào cần cập nhật: {plan.Same:#,0} dòng đã đúng tên" + (plan.NoName > 0 ? $", {plan.NoName:#,0} dòng không tra được tên theo mã (giữ nguyên)." : ".")
                + (plan.NoName == r.Groups.Count ? "\n\nKhông tra được tên của mã nào — kiểm tra Tổng hợp › Danh sách mã hàng và Mã hóa sản phẩm đã có dữ liệu chưa." : ""), "Tên hàng đã khớp");
            return;
        }
        const int Max = 300; var shown = plan.List.Take(Max).ToList();
        var gm = new GridModel();
        gm.Load(new[]
        {
            new GridColumn("do", "Đổi (x)", 60), new GridColumn("ngay", "Ngày", 95) { ReadOnly = true }, new GridColumn("ma", "Mã hàng", 190) { ReadOnly = true },
            new GridColumn("cu", "Tên hiện có", 330) { ReadOnly = true }, new GridColumn("moi", "Tên mới", 330) { ReadOnly = true }, new GridColumn("src", "Nguồn", 130) { ReadOnly = true },
        }, shown.Select(x => new[] { "x", XuatKiem.Dmy(x.Ngay), x.Ma, x.Cu.Length > 0 ? x.Cu : "(trống)", x.Moi, x.Src }));
        var grid = new SheetGrid(gm) { SpareRows = 0, Height = 340 };
        var ok = new Button { Content = "Áp dụng", Classes = { "primary" } }; var close = new Button { Content = "Hủy", IsCancel = true };
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock
        {
            Text = $"{plan.List.Count:#,0} dòng khác tên" + (plan.Same > 0 ? $" · {plan.Same:#,0} dòng đã đúng" : "") + (plan.NoName > 0 ? $" · {plan.NoName:#,0} dòng không tra được tên (giữ nguyên)" : "")
                + ". Tra tên theo MÃ HÀNG từ Danh sách mã hàng rồi Mã hóa sản phẩm; tên mới KHÔNG có phần đuôi “ - đặc tính - hợp đồng-lô”. Xóa chữ x ở cột “Đổi” của dòng nào không muốn đổi.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5,
        });
        body.Children.Add(new Border { BorderBrush = AppTheme.Brush("#D0D7E2"), BorderThickness = new Thickness(1), Child = grid });
        if (plan.List.Count > Max) body.Children.Add(new TextBlock { Text = $"Chỉ hiện {Max} dòng đầu; {plan.List.Count - Max:#,0} dòng còn lại không đổi (bấm lại sau khi áp để làm tiếp).", Classes = { "muted" }, FontSize = 12 });
        body.Children.Add(new TextBlock { Text = "Chỉ đổi tên trong bảng này (chưa ghi file nào). Tên mới có trong file khi bấm “Xuất file rút gọn”.", Classes = { "muted" }, FontSize = 12 });
        body.Children.Add(Ui.Buttons(close, ok));
        var w = Ui.Dialog("Cập nhật tên hàng — Xuất kiểm", body, 1180); var apply = false;
        ok.Click += (_, _) => { apply = true; w.Close(); }; close.Click += (_, _) => w.Close();
        await Ui.Show(this, w);
        if (!apply) return;
        var pickKeys = shown.Where((_, i) => gm.Get(i, 0).Trim().Length > 0).ToDictionary(x => x.Key);
        if (pickKeys.Count == 0) { _status.Text = "Chưa chọn dòng nào."; return; }
        _xkUndo ??= new();
        foreach (var g in r.Groups)
        {
            var k = g.Ngay + "|" + g.Ma;
            if (!pickKeys.TryGetValue(k, out var x)) continue;
            if (!_xkUndo.ContainsKey(k)) _xkUndo[k] = g.Ten;
            g.Ten = x.Moi; _xkRenamed.Add(k);
        }
        XkRender(); _status.Text = $"Đã cập nhật tên hàng {pickKeys.Count:#,0} dòng.";
    }

    private void XkUndo()
    {
        if (_xkUndo is null || _xk is null) return;
        foreach (var g in _xk.Groups) if (_xkUndo.TryGetValue(g.Ngay + "|" + g.Ma, out var old)) g.Ten = old;
        _xkUndo = null; _xkRenamed.Clear(); XkRender(); _status.Text = "Đã trả lại tên hàng như trong file gốc.";
    }

    private async Task XkSave()
    {
        var r = _xk; if (r is null || r.Groups.Count == 0) return;
        var name = Path.GetFileNameWithoutExtension(_xkFile.Length > 0 ? _xkFile : "XuatKiem") + "_rut_gon.xlsx";
        var path = await Ui.SaveFile(this, "Xuất file Xuất kiểm rút gọn", name, "xlsx", "Excel");
        if (path is null) return;
        if (await Ui.Run(this, () => XuatKiem.Export(r, path))) { _status.Text = $"Đã xuất {r.Groups.Count:#,0} dòng → {Path.GetFileName(path)}"; Ui.OpenExternal(path); }
    }
}

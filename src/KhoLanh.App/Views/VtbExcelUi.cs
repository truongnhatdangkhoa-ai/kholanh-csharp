using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.SoKho;

namespace KhoLanh.App.Views;

/// <summary>1.177: nạp Excel vào NGĂN EXCEL của Vị Trí Bột + đối chiếu 2 ngăn — dùng chung cho NXT Bột/Sốt › Tồn theo vị trí và trang Vị Trí Bột.</summary>
public static class VtbExcelUi
{
    private static VtbService Svc => App.Services.Vtb;
    private static string F(double v) => v.ToString("#,0.###", CultureInfo.InvariantCulture);

    /// <summary>Chọn file → nhận diện cột (VỊ TRÍ, MÃ BỘT, SỐ LƯỢNG; cột khác bỏ qua) → xác nhận → THAY ngăn Excel → đẩy điện thoại. Trả true nếu đã nạp.</summary>
    public static async Task<bool> Load(Visual v)
    {
        var path = await Ui.OpenFile(v, "Chọn file Excel Vị Trí Bột", "xlsx;xlsm;xls;csv", "Excel"); if (path is null) return false;
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<object?>>>? book = null; IList<string>? names = null; VtbService.FileParse? p = null;
        if (!await Ui.RunAsync(v, async () => await Task.Run(() =>
        {
            var (b, n) = KhoLanh.Core.CuoiKy.CkService.ReadExcel(path, keepDates: true); book = b; names = n;
            foreach (var nm in n) { var t = VtbService.ParseSheet(b[nm], nm, true); p ??= t; if (t.Ok) { p = t; break; } }
        }))) return false;
        if (p is null) { await Ui.Message(v, "File không có sheet nào.", "Không đọc được file"); return false; }
        if (!p.Ok && book is not null && names is not null) p = await PickColumns(v, book[names[0]], names[0], p) ?? p;
        if (!p.Ok) { await Ui.Message(v, p.Error + "\n\nCần tối thiểu 3 cột: VỊ TRÍ, MÃ BỘT (mã hàng), SỐ LƯỢNG. Các cột khác (NSX, HSD, NHẬP, GHI CHÚ…) có thì lấy, không có thì bỏ qua.", "Không đọc được file"); return false; }
        Svc.Load();
        var old = Svc.Rows.Count(r => r.IsExcel);
        var ov = p.Rows.Count(r => Svc.Rows.Any(n => !n.IsExcel && n.Sl > 1e-9 && n.Vt.Trim().Equals(r.Vt.Trim(), StringComparison.OrdinalIgnoreCase) && VtbService.SameMa(n.Ma, r.Ma)));
        var cols = new[] { "vt", "ma", "sl", "nsx", "hsd", "nhap", "ghiChu" }.Where(p.Map.ContainsKey).Select(k => k switch { "vt" => "Vị trí", "ma" => "Mã hàng", "sl" => "SL", "nsx" => "NSX", "hsd" => "HSD", "nhap" => "Nhập", _ => "Ghi chú" });
        var msg = $"Nạp {p.Rows.Count} dòng (tổng SL {F(p.Rows.Sum(r => r.Sl))}) từ {Path.GetFileName(path)} · sheet {p.Sheet}" + (p.DataDate.Length > 0 ? " · ngày " + p.DataDate : "") + "."
            + $"\n\nCột nhận diện: {string.Join(", ", cols)}. Cột khác bỏ qua."
            + $"\nBỏ qua {p.SkippedZero} dòng SL = 0" + (p.Skipped > 0 ? $" và {p.Skipped} dòng thiếu Vị trí / Mã hàng" : "") + "."
            + (old > 0 ? $"\n\nNgăn Excel cũ ({old} dòng · {Svc.ExcelFile}) sẽ bị THAY hoàn toàn, không cộng dồn; phiếu xuất đã tạo trên dòng Excel cũ sẽ bị khóa." : "")
            + "\nNgăn NXT (nhập trực tiếp) giữ nguyên."
            + (ov > 0 ? $"\n\n⚠ {ov} dòng trùng Mã hàng + Vị trí với ngăn NXT — kiểm tra ở “Đối chiếu 2 ngăn” kẻo tính trùng." : "");
        if (!await Ui.Confirm(v, msg, "Nạp Excel Vị Trí Bột", "Nạp")) return false;
        if (!await Ui.Run(v, () => Svc.LoadExcel(p, Path.GetFileName(path)))) return false;
        var extra = "";
        var ph = App.Services.Phone;
        if (ph.Enabled && Svc.PushExcel)
        {
            try { var r = await Svc.SyncPhoneAsync(ph, pull: false); extra = $"\n\nĐã đẩy {r.Pushed} dòng tồn lên điện thoại."; }
            catch (Exception e) { extra = "\n\nChưa đẩy được lên điện thoại: " + e.Message + " — vào Vị Trí Bột › 📱 Đồng bộ điện thoại để thử lại."; }
        }
        else if (!Svc.PushExcel) extra = "\n\nLưu ý: công tắc “Excel” đang TẮT nên ngăn này chưa lên điện thoại (bật ở trang Vị Trí Bột).";
        await Ui.Message(v, $"Đã nạp {p.Rows.Count} dòng vào ngăn Excel của Vị Trí Bột." + extra);
        return true;
    }

    /// <summary>Không nhận diện được cột → cho người dùng chọn tay 3 cột bắt buộc (Vị trí, Mã hàng, SL).</summary>
    private static async Task<VtbService.FileParse?> PickColumns(Visual v, IReadOnlyList<IReadOnlyList<object?>> aoa, string sheet, VtbService.FileParse failed)
    {
        var hr = VtbService.GuessHeaderRow(aoa); if (hr < 0) return null;
        var heads = aoa[hr].Select((x, i) => (i, t: Convert.ToString(x, CultureInfo.InvariantCulture)?.Trim() ?? "")).Where(x => x.t.Length > 0).ToList();
        if (heads.Count < 3) return null;
        var opts = heads.Select(h => $"Cột {ColName(h.i)}: {h.t}").ToList();
        if (!await Ui.Confirm(v, failed.Error + "\n\nChọn tay 3 cột bắt buộc dựa trên dòng " + (hr + 1) + " của sheet " + sheet + "?", "Không nhận diện được cột", "Chọn cột")) return null;
        var map = new Dictionary<string, int>();
        foreach (var (f, label) in new[] { ("vt", "VỊ TRÍ"), ("ma", "MÃ HÀNG / MÃ BỘT"), ("sl", "SỐ LƯỢNG") })
        {
            var k = await Ui.Pick(v, $"Cột nào là {label}?", "Chọn cột " + label, opts); if (k < 0) return null; map[f] = heads[k].i;
        }
        return VtbService.ParseSheet(aoa, sheet, true, map, hr);
    }
    private static string ColName(int i) { var s = ""; for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s; return s; }

    /// <summary>Bảng Mã hàng + Vị trí có tồn ở CẢ hai ngăn (nghi tính trùng).</summary>
    public static async Task Compare(Visual v)
    {
        Svc.Load(); var list = Svc.Overlaps();
        if (list.Count == 0) { await Ui.Message(v, "Không có Mã hàng + Vị trí nào nằm ở cả 2 ngăn (Excel và NXT) — số liệu không trùng.", "Đối chiếu 2 ngăn"); return; }
        var m = new GridModel();
        m.Load(new[] { new GridColumn("vt", "Vị trí", 110, "text") { ReadOnly = true }, new GridColumn("ma", "Mã hàng", 220, "text") { ReadOnly = true },
            new GridColumn("n", "SL ngăn NXT", 110, "number") { ReadOnly = true }, new GridColumn("x", "SL ngăn Excel", 110, "number") { ReadOnly = true }, new GridColumn("t", "Tổng nếu cộng", 110, "number") { ReadOnly = true } },
            list.Select(o => new[] { o.Vt, o.Ma, F(o.SlNxt), F(o.SlExcel), F(o.SlNxt + o.SlExcel) }));
        var g = new SheetGrid(m) { SpareRows = 0 }; g.Refresh();
        var body = new DockPanel { MinHeight = 320 };
        var tip = new TextBlock { Text = $"{list.Count} dòng có tồn ở cả 2 ngăn. Nếu cùng một hàng thì điện thoại sẽ thấy tồn gấp đôi — tắt công tắc ngăn không đúng, hoặc sửa nguồn (nạp lại Excel / đưa lại NXT).", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(tip, Dock.Top); body.Children.Add(tip); body.Children.Add(g);
        await Ui.Show(v, Ui.Dialog("Đối chiếu 2 ngăn · Vị Trí Bột", body, 760));
    }
}

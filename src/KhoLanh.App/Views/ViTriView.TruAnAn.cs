using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using KhoLanh.App.Controls;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;
using KhoLanh.Core.ViTri;
using static KhoLanh.Core.ViTri.ViTriService;

namespace KhoLanh.App.Views;

/// <summary>
/// 1.165: "Trừ nhanh tồn An An" — chọn phiếu vị trí → xem trước (ghép theo mã · đặc tính · hợp đồng) → tạo phiếu xuất An An và trừ ngay,
/// để xuất file báo cáo An An nhanh. An An không có cột Lô / Vị trí nên chỉ trừ TỔNG; dùng thỉnh thoảng, không liên động hai chiều.
/// </summary>
public sealed partial class ViTriView
{
    private async Task TruAnAn()
    {
        var an = App.Services.AnAn;
        if (!an.CanEdit) { await Ui.Message(this, "Tài khoản không có quyền sửa Tồn kho An An."); return; }
        if (!an.Loaded) an.Load();
        if (an.D.Rows.Count == 0) { await Ui.Message(this, "Tồn kho An An chưa có dữ liệu Load Data — nạp file An An trước."); return; }
        var slips = Svc.OpenSlips();
        if (slips.Count == 0) { await Ui.Message(this, "Chưa có phiếu vị trí nào để trừ."); return; }

        var today = DateTime.Today;
        var checks = new List<(Slip S, CheckBox Cb)>();
        var list = new StackPanel { Spacing = 2 };
        foreach (var s in slips.Take(60))
        {
            var done = AnAnSlipOf(s);
            var cb = new CheckBox
            {
                Content = $"{(s.MaPhieu.Length > 0 ? s.MaPhieu : s.Id)} · {Dmy(DayKey(s))} · {LoaiLabel(s.Loai)} · {s.Lines.Count} dòng · SL {s.TongSl:#,0.##}" + (done.Length > 0 ? $"  ✔ đã trừ An An ({done})" : ""),
                IsChecked = done.Length == 0 && s.Deducted && DayKey(s) == today.ToString("yyyy-MM-dd"), FontSize = 12.5,
            };
            checks.Add((s, cb)); list.Children.Add(cb);
        }
        var gm = new GridModel { ReadOnly = true };
        var grid = new SheetGrid(gm) { SpareRows = 0, Height = 300, LargeTitle = "Trừ nhanh tồn An An", Filterable = false };
        var info = new TextBlock { Classes = { "muted" }, FontSize = 12.5, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        List<AnAnService.TruRow> prev = new();
        void Refresh()
        {
            var chosen = checks.Where(c => c.Cb.IsChecked == true).Select(c => c.S).ToList();
            prev = chosen.Count == 0 ? new() : an.PreviewTruNhanh(ToAnAnLines(chosen));
            GridColumn C(string k, string h, double w, string t = "text") => new(k, h, w, t) { ReadOnly = true };
            gm.Load(new[] { C("ma", "Mã hàng", 190), C("dt", "Đặc tính", 100), C("hd", "Hợp đồng", 100), C("sl", "SL xuất", 80, "number"), C("ton", "Tồn An An", 90, "number"), C("con", "Còn lại", 80, "number"), C("tt", "Kết quả", 210) },
                prev.Select(r => new[] { r.Src.Ma, r.Src.Dt, r.Src.Hd, Raw(r.Src.Qty), r.Status == "khong-ghep" ? "" : Raw(r.Ton), r.Ok ? Raw(r.Con) : "",
                    r.Ok ? "Sẽ trừ" : r.Status == "khong-ghep" ? "Không thấy dòng An An cùng mã · đặc tính · hợp đồng" : $"Vượt tồn (An An chỉ còn {Raw(r.Ton)})" }).ToList());
            grid.CellBackground = (row, c) => row < prev.Count && !prev[row].Ok ? AppTheme.Brush("#FDE8E6") : null;
            grid.Refresh();
            var ok = prev.Count(r => r.Ok); var bad = prev.Count - ok;
            info.Text = chosen.Count == 0 ? "Tích phiếu cần trừ." : $"{chosen.Count} phiếu · {prev.Count} nhóm mã: {ok} sẽ trừ" + (bad > 0 ? $", {bad} bỏ qua (dòng đỏ — không trừ, tự xử lý)" : "") + ". Chỉ trừ TỔNG theo mã · đặc tính · hợp đồng (An An không có lô / vị trí).";
        }
        foreach (var (_, cb) in checks) cb.IsCheckedChanged += (_, _) => Refresh();
        Refresh();

        var go = new Button { Content = "Trừ tồn An An", Classes = { "primary" } };
        var close = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = "Trừ nhanh tồn kho An An theo phiếu vị trí", FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 15 });
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 150, BorderThickness = new Thickness(1), BorderBrush = AppTheme.Brush("#D0D7E2"), Padding = new Thickness(6) });
        body.Children.Add(new Border { BorderThickness = new Thickness(1), BorderBrush = AppTheme.Brush("#D0D7E2"), Child = grid });
        body.Children.Add(info);
        body.Children.Add(Ui.Buttons(go, close));
        var w = Ui.Dialog("Trừ nhanh tồn An An", body, 980);
        close.Click += (_, _) => w.Close();
        go.Click += async (_, _) =>
        {
            var chosen = checks.Where(c => c.Cb.IsChecked == true).Select(c => c.S).ToList();
            if (chosen.Count == 0 || !prev.Any(r => r.Ok)) { await Ui.Message(w, "Không có dòng nào trừ được."); return; }
            var again = chosen.Where(s => AnAnSlipOf(s).Length > 0).ToList();
            if (again.Count > 0 && !await Ui.Confirm(w, $"{again.Count} phiếu ({string.Join(", ", again.Select(s => s.Id))}) ĐÃ trừ vào An An trước đó. Trừ nữa sẽ trừ TRÙNG. Vẫn trừ?", "Trừ trùng?", "Vẫn trừ")) return;
            var ok = prev.Count(r => r.Ok); var bad = prev.Count - ok;
            if (!await Ui.Confirm(w, $"Trừ {ok} nhóm mã khỏi Tồn kho An An" + (bad > 0 ? $" ({bad} nhóm không trừ được sẽ bỏ qua)" : "") + "?\n\nTạo phiếu xuất An An và trừ ngay. Muốn quay lại: Tồn kho An An › Phiếu xuất đã tạo › Hủy trừ.", "Trừ nhanh tồn An An", "Trừ")) return;
            try
            {
                var r = an.TruNhanh(ToAnAnLines(chosen), chosen.Select(s => s.Id));
                if (r.Slip is null) { await Ui.Message(w, "Không có dòng nào trừ được."); return; }
                Svc.MarkAnAn(chosen.Select(s => s.Id), AnAnService.SlipId(r.Slip));
                _status.Text = $"Đã trừ An An: phiếu {AnAnService.SlipId(r.Slip)} ({r.Done} nhóm mã" + (r.Skipped > 0 ? $", bỏ qua {r.Skipped}" : "") + ")";
                RenderAll();
                await Ui.Message(w, $"Đã tạo phiếu {AnAnService.SlipId(r.Slip)} và trừ {r.Done} nhóm mã khỏi Tồn kho An An." + (r.Skipped > 0 ? $"\n{r.Skipped} nhóm không trừ được (không ghép được hoặc vượt tồn) — xem dòng đỏ." : "") + "\n\nBáo cáo / xuất file An An dùng số mới ngay. Nạp lại file An An mới thì số được thay mới.", "Đã trừ");
                w.Close();
            }
            catch (Exception ex) { await Ui.Message(w, ex is BusinessException ? ex.Message : Ui.Describe(ex), "Không trừ được"); }
        };
        await Ui.Show(this, w);
    }
}

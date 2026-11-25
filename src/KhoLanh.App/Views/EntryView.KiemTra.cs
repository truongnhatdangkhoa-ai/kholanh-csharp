using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.Core.KiemKe;
using KhoLanh.Core.SoDo;

namespace KhoLanh.App.Views;

/// <summary>
/// 1.73 (K2) — báo trùng / thiếu khi nhập trang KIỂM KÊ: tô ô (vàng = trùng Vị trí + Mã hàng / vị trí lạ / mã lạ, đỏ = số lỗi),
/// chú thích khi rê chuột + ở dòng trạng thái, nút tóm tắt "N dòng trùng · N vị trí lạ · N mã lạ · N số lỗi" dưới lưới (bấm → ô kế tiếp).
/// Chỉ báo, không chặn lưu; bấm Lưu (Ctrl + S) khi còn lỗi đỏ thì hỏi lại (tự lưu ngầm không hỏi).
/// Tính lại sau khi ngừng gõ 300 ms (gộp mọi thay đổi trong lúc gõ / dán) bằng hàm thuần O(số dòng) <see cref="KkKiemTra.Check"/>.
/// </summary>
public partial class EntryView
{
    private KkKiemTra.KetQua _kk = KkKiemTra.KetQua.Rong;
    private int _kkSeq;                              // mỗi thay đổi tăng 1; chỉ lần chờ mới nhất được tính (gộp khi gõ / dán liên tục)
    private static HashSet<string>? _vtCodes;      // mã vị trí theo Sơ đồ kho (dùng chung các lần mở trang, làm mới sau 5 phút)
    private static DateTime _vtAt;
    private static readonly IBrush KkWarnBg = AppTheme.Brush("#FFF2D6"), KkBadBg = AppTheme.Brush("#FDE8E6");

    private void InitKkCheck()
    {
        _grid.CellTip = (r, c) => IsKk ? _kk.Tip(r, c) : null;
        _model.Changed += () =>
        {
            if (_loading || !IsKk) return;
            var seq = ++_kkSeq;
            _ = Task.Delay(300).ContinueWith(_ => Dispatcher.UIThread.Post(() => { if (seq == _kkSeq) RecheckKk(); }), TaskScheduler.Default);
        };
        AttachedToVisualTree += (_, _) => LoadViTriCodes();
    }

    /// <summary>Nạp ngầm danh sách vị trí của Sơ đồ kho (không xét quyền Sơ đồ kho).</summary>
    private void LoadViTriCodes()
    {
        if (_vtCodes is not null && (DateTime.Now - _vtAt).TotalMinutes < 5) return;
        _vtAt = DateTime.Now;
        var s = App.Services;
        Task.Run(() =>
        {
            try
            {
                var codes = new SoDoService(() => s.Lan, s.Session, s.Audit, s.Entry).AllCodes();
                Dispatcher.UIThread.Post(() => { _vtCodes = codes; RecheckKk(); });
            }
            catch { /* không đọc được sơ đồ → bỏ qua kiểm tra vị trí */ }
        });
    }

    /// <summary>Tính lại toàn trang (gọi sau khi ngừng gõ / mở trang / nạp xong danh sách mã, vị trí).</summary>
    private void RecheckKk()
    {
        _kkSeq++;
        var cot = _page is not null && IsKk ? KkKiemTra.Cols(PageCols.Select(c => c.Header)) : null;
        if (cot is null) { _kk = KkKiemTra.KetQua.Rong; KkCheckButton.IsVisible = false; return; }
        Func<string, bool>? vtCo = _vtCodes is { Count: > 0 } vs ? vs.Contains : null;
        Func<string, bool>? maCo = Sugs is null ? null : ma => Known.Contains(KhoLanh.Core.TimNhanh.Gs.MaKey(ma));
        _kk = KkKiemTra.Check(_model.RowCount, _model.Get, cot, vtCo, maCo);
        KkCheckButton.IsVisible = true;
        if (_kk.Empty)
        {
            KkCheckButton.Content = "✓ Không thấy trùng / lỗi";
            KkCheckButton.Classes.Set("ghost", true);
            KkCheckButton.Foreground = AppTheme.Brush("#1E7B4A"); KkCheckButton.Background = AppTheme.Brush("#E3F4EA");
        }
        else
        {
            KkCheckButton.Content = "⚠ " + _kk.TomTat();
            KkCheckButton.Classes.Set("ghost", false);
            KkCheckButton.Foreground = AppTheme.Brush(_kk.CoDo ? "#B42318" : "#9A5B00");
            KkCheckButton.Background = AppTheme.Brush(_kk.CoDo ? "#FDE8E6" : "#FFF2D6");
        }
        _grid.Refresh();
        UpdateStatus();
    }

    /// <summary>Nền ô theo kết quả kiểm tra (null = không có vấn đề).</summary>
    private IBrush? KkBg(int r, int c) => _kk.Muc(r, c) switch { true => KkBadBg, false => KkWarnBg, _ => null };

    private void OnKkNext(object? sender, RoutedEventArgs e)
    {
        if (_kk.Empty) return;
        var sel = _grid.Selection;
        if (_kk.Next(sel.R1, sel.C1) is { } v) { _grid.Select(v.Row, v.Col); _grid.Focus(); UpdateStatus(); }
    }

    /// <summary>Lưu tay khi trang còn lỗi đỏ → hỏi lại (không chặn).</summary>
    private async Task<bool> KkConfirmSave()
    {
        if (!IsKk || !_dirty) return true;
        RecheckKk();
        if (!_kk.CoDo) return true;
        var first = _kk.Items.First(x => x.Do);
        return await Ui.Confirm(this, $"Trang kiểm kê còn {_kk.SoLoi:#,0} ô số lỗi (Số lượng / SL xuất không phải số, âm, hoặc SL xuất lớn hơn Số lượng).\n\nVd dòng {first.Row + 1}: {first.Text}\n\nVẫn lưu?", "Còn số lỗi", "Vẫn lưu");
    }

    /// <summary>Chú thích của ô đang chọn (cho dòng trạng thái).</summary>
    private string? KkSelTip()
    {
        if (!IsKk || _kk.Empty) return null;
        var sel = _grid.Selection;
        return sel.Single ? _kk.Tip(sel.R1, sel.C1)?.Replace("\n", " · ") : null;
    }
}

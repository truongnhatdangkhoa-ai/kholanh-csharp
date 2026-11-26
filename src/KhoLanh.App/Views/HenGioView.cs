using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KhoLanh.App.Controls;
using KhoLanh.Core.HenGio;
using KhoLanh.Core.Lan;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

/// <summary>
/// Hẹn giờ tắt máy (1.139): tắt máy tính này sau N phút hoặc vào giờ đồng hồ (vd 22:30) bằng lệnh <c>shutdown</c> của hệ điều hành —
/// lệnh vẫn có hiệu lực khi đóng app. Chỉ Quản trị (quyền "hengio"). Máy chủ LAN → cảnh báo các máy con sẽ mất kết nối.
/// </summary>
public sealed class HenGioView : UserControl, IPage
{
    public string Title => "Hẹn giờ tắt máy";
    public string Subtitle => "Tắt máy tính này sau số phút tùy chọn hoặc vào giờ đồng hồ. Lệnh do Windows giữ nên vẫn chạy kể cả khi đóng app; hủy được bất cứ lúc nào tại đây.";

    private static HenGioService Svc => App.Services.HenGio;
    private static readonly int[] Quick = { 15, 30, 60, 120 };

    private readonly RadioButton _byMin = new() { Content = "Sau số phút", GroupName = "hg-mode", IsChecked = true };
    private readonly RadioButton _byClock = new() { Content = "Vào giờ đồng hồ", GroupName = "hg-mode" };
    private readonly TextBox _min = new() { Text = "60", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Right };
    private readonly TextBox _clock = new() { Text = "22:30", Width = 80, Watermark = "HH:mm" };
    private readonly TextBlock _unit = new() { Text = "phút", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _set, _cancel;
    private readonly TextBlock _big = new() { FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sub = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly Border _pill = new() { Classes = { "pill", "info" }, Child = new TextBlock { Text = "Không có hẹn giờ" } };
    private readonly TextBlock _preview = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
    private readonly TextBlock _msg = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Tag = "keep" };
    private readonly Border _lanWarn;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public HenGioView()
    {
        _set = Icon.Button("Hẹn giờ tắt máy", "history", "primary");
        _set.Click += async (_, _) => await OnSet();
        _cancel = Icon.Button("Hủy hẹn giờ", "x", "danger");
        _cancel.Click += async (_, _) => await OnCancel();

        // ---- trạng thái hiện tại
        var statusBody = new StackPanel { Spacing = 4 };
        statusBody.Children.Add(_pill); statusBody.Children.Add(_big); statusBody.Children.Add(_sub);
        var status = new Border { Classes = { "card" }, Child = statusBody };

        // ---- đặt hẹn giờ (1 hàng ToolRow)
        var row = new ToolRow();
        row.Children.Add(_byMin); row.Children.Add(_min); row.Children.Add(_unit);
        foreach (var q in Quick)
        {
            var b = new Button { Content = q >= 60 ? $"{q / 60} giờ" : $"{q} phút", Padding = new Thickness(10, 4), FontSize = 12.5, Tag = q };
            b.Click += (_, _) => { _byMin.IsChecked = true; _min.Text = q.ToString(); Refresh(); };
            row.Children.Add(b);
        }
        row.Children.Add(_byClock); row.Children.Add(_clock);
        row.Children.Add(ToolRow.Gap()); row.Children.Add(_cancel); row.Children.Add(ToolRow.Pin(_set));
        var setBody = new StackPanel { Spacing = 8 };
        setBody.Children.Add(new TextBlock { Text = "Đặt hẹn giờ", Classes = { "section" } });
        setBody.Children.Add(row);
        setBody.Children.Add(_preview);
        setBody.Children.Add(_msg);
        var set = new Border { Classes = { "card" }, Child = setBody };

        // ---- cảnh báo máy chủ LAN
        _lanWarn = new Border { Classes = { "card" }, IsVisible = false, Child = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 } };

        var note = new TextBlock
        {
            Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "keep",
            Text = "Lưu ý: Windows báo trước ở góc màn hình khi gần tới giờ; phần mềm đang mở sẽ bị đóng khi máy tắt — hãy lưu việc đang làm. Hẹn lại sẽ thay hẹn cũ. Tối đa 7 ngày.",
        };

        var root = new StackPanel { Spacing = 10 };
        root.Children.Add(status); root.Children.Add(set); root.Children.Add(_lanWarn); root.Children.Add(note);
        Content = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = root };

        _byMin.IsCheckedChanged += (_, _) => { SyncMode(); Refresh(); };
        _min.TextChanged += (_, _) => Refresh();
        _clock.TextChanged += (_, _) => Refresh();
        _tick.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => { SyncMode(); Refresh(); _tick.Start(); };
        DetachedFromVisualTree += (_, _) => _tick.Stop();
        SyncMode(); Refresh();
    }

    private void Kind(string k)
    {
        foreach (var c in new[] { "ok", "warn", "bad", "info" }) _pill.Classes.Remove(c);
        _pill.Classes.Add(k);
    }

    private void SyncMode()
    {
        var m = _byMin.IsChecked == true;
        _min.IsEnabled = m; _clock.IsEnabled = !m;
    }

    private static string LanText()
    {
        var lan = App.Services.Lan;
        if (!lan.Enabled || lan.Role == LanService.RoleRemote) return "";
        var n = lan.Machines().Count;
        if (lan.Serving || lan.Role == LanService.RoleServer)
            return "⚠ Máy này là MÁY CHỦ mạng LAN" + (n > 0 ? $" — hiện có {n} máy con đang nối" : "") +
                   ". Khi máy tắt, mọi máy con sẽ MẤT KẾT NỐI: tự làm việc riêng (lưu sổ chờ) rồi gộp lại khi máy chủ mở lại. Chỉ hẹn giờ khi đã báo mọi người.";
        return "Máy này đang là máy con của mạng LAN — khi tắt máy, dữ liệu chưa gửi được giữ trong sổ chờ và gửi lại ở lần mở sau.";
    }

    private void Refresh()
    {
        var now = DateTime.Now;
        var can = Svc.CanUse;
        var at = can ? Svc.DangHen() : null;
        if (at is { } t)
        {
            ((TextBlock)_pill.Child!).Text = "Đang hẹn"; Kind("warn");
            _big.Text = "Máy tắt lúc " + HenGioCore.NhanLuc(t, now);
            _sub.Text = "Còn " + HenGioCore.DemNguoc(t - now) + "  ·  " + t.ToString("dd/MM/yyyy HH:mm:ss");
        }
        else
        {
            ((TextBlock)_pill.Child!).Text = can ? "Không có hẹn giờ" : "Không có quyền"; Kind(can ? "info" : "bad");
            _big.Text = can ? "Chưa hẹn giờ tắt máy" : "Chỉ Quản trị được hẹn giờ tắt máy";
            _sub.Text = can ? "Chọn số phút hoặc giờ đồng hồ bên dưới rồi bấm “Hẹn giờ tắt máy”." : "Nhờ Quản trị cấp quyền “Hẹn giờ tắt máy” ở Hệ thống › Tài khoản.";
        }
        _set.IsEnabled = can; _cancel.IsEnabled = can && at is not null;

        // xem trước: máy sẽ tắt lúc nào nếu bấm nút
        if (_byMin.IsChecked == true)
        {
            _preview.Text = int.TryParse(_min.Text?.Trim(), out var p) && HenGioCore.TheoPhut(now, p) is { } r
                ? $"Nếu hẹn bây giờ: máy tắt lúc {r.Luc:HH:mm:ss} ({HenGioCore.NhanLuc(r.Luc, now)})."
                : $"Nhập số phút từ 1 đến {HenGioCore.MaxPhut}.";
        }
        else
        {
            _preview.Text = HenGioCore.TryParseGio(_clock.Text, out var h, out var m) && HenGioCore.TheoGio(now, h, m) is { } r
                ? $"Nếu hẹn bây giờ: máy tắt lúc {r.Luc:HH:mm} ({HenGioCore.NhanLuc(r.Luc, now)}), còn {HenGioCore.DemNguoc(r.Luc - now)}."
                : "Nhập giờ dạng 22:30 (giờ 0–23, phút 0–59).";
        }
        var lw = LanText();
        _lanWarn.IsVisible = can && lw.Length > 0;
        ((TextBlock)_lanWarn.Child!).Text = lw;
        _lanWarn.Background = AppTheme.Brush(lw.StartsWith("⚠") ? "#FFF2D6" : "#EEEBFC");
    }

    private async Task OnSet()
    {
        _msg.Text = "";
        try
        {
            var now = DateTime.Now;
            string what; bool byMin = _byMin.IsChecked == true;
            if (byMin)
            {
                if (!int.TryParse(_min.Text?.Trim(), out var p) || HenGioCore.TheoPhut(now, p) is not { } r) { _msg.Text = $"Số phút phải từ 1 đến {HenGioCore.MaxPhut}."; return; }
                what = $"Tắt máy tính này sau {p} phút (lúc {r.Luc:HH:mm}, {HenGioCore.NhanLuc(r.Luc, now)})?";
            }
            else
            {
                if (!HenGioCore.TryParseGio(_clock.Text, out var h, out var m)) { _msg.Text = "Giờ không đúng — nhập dạng 22:30."; return; }
                var r = HenGioCore.TheoGio(now, h, m)!.Value;
                what = $"Tắt máy tính này vào lúc {HenGioCore.NhanLuc(r.Luc, now)}?";
            }
            var lw = LanText();
            var text = what + (Svc.DangHen() is { } old ? $"\n\nĐang có hẹn giờ lúc {HenGioCore.NhanLuc(old, now)} — sẽ được thay bằng hẹn giờ mới." : "")
                       + (lw.Length > 0 ? "\n\n" + lw : "") + "\n\nPhần mềm đang mở sẽ bị đóng khi máy tắt.";
            if (!await Ui.Confirm(this, text, "Hẹn giờ tắt máy", "Hẹn giờ")) return;
            var res = byMin ? Svc.HenPhut(int.Parse(_min.Text!.Trim())) : Svc.HenGio(_clock.Text ?? "");
            _msg.Text = res.Text;
            _msg.Foreground = AppTheme.Brush(res.Ok ? "#1E7B4A" : "#B42318");
        }
        catch (BusinessException ex) { _msg.Text = ex.Message; _msg.Foreground = AppTheme.Brush("#B42318"); }
        Refresh();
    }

    private async Task OnCancel()
    {
        _msg.Text = "";
        try
        {
            if (!await Ui.Confirm(this, "Hủy hẹn giờ tắt máy đang đặt?", "Hủy hẹn giờ", "Hủy hẹn giờ")) return;
            var res = Svc.Huy();
            _msg.Text = res.Text;
            _msg.Foreground = AppTheme.Brush(res.Ok ? "#1E7B4A" : "#B42318");
        }
        catch (BusinessException ex) { _msg.Text = ex.Message; _msg.Foreground = AppTheme.Brush("#B42318"); }
        Refresh();
    }
}

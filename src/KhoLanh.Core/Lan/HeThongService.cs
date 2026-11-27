using System.Globalization;

namespace KhoLanh.Core.Lan;

/// <summary>
/// 1.179 — Mục Hệ thống chỉ MÁY CHỦ được sửa; máy khác chỉ xem tình trạng.
///  • <see cref="ChiXem"/>: máy đang nối LAN / Internet nhưng KHÔNG phải máy chủ (máy con, máy ngoại tuyến, máy xem từ xa). Dựa vào VAI MÁY, không dựa vào tài khoản —
///    Quản trị đăng nhập ở máy khác vẫn chỉ xem. Máy đơn (chưa bật LAN) vẫn là máy chủ của chính nó, không khóa.
///  • Máy chủ ghi bản chụp <see cref="HtTrangThai"/> (không chứa token / mật khẩu) mỗi 1–5 phút để máy khác xem; hẹn giờ tắt máy → ghi dấu "tắt theo lịch".
/// Việc GÁN máy làm máy chủ vẫn do Quản trị bấm tay ở chính máy đó (không có cơ chế tự gán).
/// </summary>
public sealed class HeThongService
{
    private readonly AppServices _s;
    private readonly HeThongTrangThaiStore _store;
    public HeThongService(AppServices s) { _s = s; _store = new HeThongTrangThaiStore(() => s.Lan); }

    /// <summary>Máy này đang nối máy chủ nhưng không phải máy chủ → mục Hệ thống chỉ xem.</summary>
    public bool ChiXem => ChiXemOf(_s.Lan);
    public static bool ChiXemOf(LanService lan) => lan.Enabled && !lan.Serving;
    /// <summary>Tên máy chủ để ghi ở nhãn ("KHO-PC"); rỗng = chưa biết.</summary>
    public string TenMayChu => _s.Lan.ServerName;

    public HtTrangThai? Read() => _store.Read();

    /// <summary>Nhãn ngắn cho người dùng: "Chỉ xem — chỉnh ở máy chủ KHO-PC".</summary>
    public string Nhan()
    {
        var n = TenMayChu;
        return "Chỉ xem — chỉ chỉnh được khi đăng nhập trên máy chủ" + (n.Length > 0 ? $" ({n})" : "") + ".";
    }

    // ------------------------------------------------------------------ bản chụp

    public HtTrangThai Build(string tat = "")
    {
        var t = new HtTrangThai { Id = _s.Lan.MachineId, Ten = _s.Lan.MachineName, At = HtTrangThai.Iso(DateTime.UtcNow), Tat = tat, Ban = typeof(HeThongService).Assembly.GetName().Version?.ToString(3) ?? "" };
        try { if (_s.HenGio.DangHen() is { } h) t.HenTat = HtTrangThai.Iso(h.ToUniversalTime()); } catch { }
        try
        {
            var ph = _s.Phone;
            t.DtNoi = ph.Enabled; t.DtLoi = ph.Enabled ? ph.LastError?.Msg ?? "" : "";
            var instant = _s.PhoneSync.InstantOn;
            var m = int.TryParse(_s.Settings.Get("phone_sync_interval", "0"), out var mm) ? Math.Max(0, mm) : 0;
            t.DtAuto = instant ? "Ngay khi có thay đổi" : m <= 0 ? "Tắt" : $"Mỗi {m} phút";
            t.DtBang = _s.PhoneSync.Pick.Count;
            t.DtLan = _s.PhoneSync.LastSlipPush;
        }
        catch { }
        try
        {
            var c = _s.DriveCaNhan.Config;
            t.DriveDn = c.Linked; t.DriveMail = Mask(c.Email); t.DriveGio = c.Every; t.DriveLoi = c.LastErr;
            t.DriveLan = c.Last?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? "";
        }
        catch { }
        try { t.SheetChung = _s.Accounts.Enabled; } catch { }
        try { var cfg = _s.LichXuat.MailCfg(); t.MailLx = cfg.Ready; t.MailLxTt = _s.LichXuat.MailStatus; } catch { }
        try { t.GitHub = _s.Updates.HasToken; } catch { }
        return t;
    }

    /// <summary>Máy chủ ghi bản chụp. Máy khác / máy đơn không ghi. Không bao giờ ném lỗi.</summary>
    public bool Publish(string tat = "")
    {
        try
        {
            if (!_s.Lan.Serving) return false;
            return _store.Write(Build(tat));
        }
        catch { return false; }
    }

    private DateTime? Hen() { try { return _s.HenGio.DangHen()?.ToUniversalTime(); } catch { return null; } }

    /// <summary>Gọi khi app đóng: có hẹn giờ tắt máy → "lich", không thì "thoat".</summary>
    public void PublishOnExit() => Publish(Hen() is not null ? "lich" : "thoat");

    private CancellationTokenSource? _cts;

    /// <summary>Ghi nền: sau <paramref name="first"/> rồi mỗi phút (chỉ ghi khi nội dung đổi hoặc quá 5 phút; gần giờ hẹn tắt thì ghi dấu "lich").</summary>
    public void Start(TimeSpan first)
    {
        Stop();
        var cts = _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(first, cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var soonOff = Hen() is { } h && h <= DateTime.UtcNow.AddMinutes(10);
                    Publish(soonOff ? "lich" : "");
                    await Task.Delay(TimeSpan.FromMinutes(1), cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    public void Stop() { _cts?.Cancel(); _cts = null; }

    /// <summary>Địa chỉ Gmail che bớt: abc***@gmail.com.</summary>
    public static string Mask(string mail)
    {
        var at = mail.IndexOf('@');
        if (at <= 0) return mail.Length > 0 ? "***" : "";
        return mail[..Math.Min(3, at)] + "***" + mail[at..];
    }

    // ------------------------------------------------------------------ chữ hiển thị

    /// <summary>Dòng trạng thái máy chủ cho máy xem: (mức ok/warn/bad/info, chữ).</summary>
    public static (string Kind, string Text) MayChuText(HtTrangThai? t, DateTime nowUtc, Func<DateTime, string>? fmt = null)
    {
        fmt ??= d => d.ToLocalTime().ToString("HH:mm dd/MM");
        if (t is null) return ("warn", "Máy chủ chưa báo tình trạng (máy chủ cần bản 1.179 trở lên và đang mở).");
        var ten = t.Ten.Length > 0 ? t.Ten : "Máy chủ";
        var at = t.AtUtc;
        switch (t.Phan(nowUtc))
        {
            case HtTrangThai.May.Chay: return ("ok", $"{ten} đang chạy · báo lúc {fmt(at!.Value)}" + (t.HenTatUtc is { } h ? $" · hẹn tắt máy lúc {fmt(h)}" : ""));
            case HtTrangThai.May.TatTheoLich: return ("info", $"🌙 {ten} đang tắt theo lịch" + (t.HenTatUtc is { } h2 ? $" (hẹn tắt {fmt(h2)})" : "") + $" · đang hiện tình trạng lần báo cuối {fmt(at!.Value)}");
            case HtTrangThai.May.DaThoat: return ("info", $"{ten} đã thoát app lúc {fmt(at!.Value)} · đang hiện tình trạng lần báo cuối");
            default: return ("warn", $"{ten} chưa báo từ {fmt(at!.Value)} — có thể đang tắt ngoài lịch · đang hiện tình trạng lần báo cuối");
        }
    }
}

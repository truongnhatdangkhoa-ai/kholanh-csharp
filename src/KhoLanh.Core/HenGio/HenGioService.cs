using System.Globalization;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.HenGio;

/// <summary>
/// Hẹn giờ tắt máy: gọi lệnh <c>shutdown</c> của hệ điều hành (lệnh vẫn chạy kể cả khi đóng app) và nhớ thời điểm tắt ở settings
/// <c>hengio_tat</c> (ISO, giờ máy) để lần mở app sau còn hiện đếm ngược. Chỉ tài khoản có quyền "hengio" (mặc định Quản trị).
/// </summary>
public sealed class HenGioService
{
    public const string Key = "hengio_tat", PermKey = "hengio";
    private readonly SettingsService _set;
    private readonly Session _ses;
    private readonly AuditService _audit;
    private readonly Func<DateTime> _now;
    private readonly Func<HgLenh, (int Code, string Out)> _run;
    private readonly HgOs _os;

    public HenGioService(SettingsService set, Session ses, AuditService audit, Func<DateTime>? now = null, Func<HgLenh, (int Code, string Out)>? run = null, HgOs? os = null)
    {
        _set = set; _ses = ses; _audit = audit;
        _now = now ?? (() => DateTime.Now);
        _run = run ?? Run;
        _os = os ?? HenGioCore.ThisOs();
    }

    public bool CanUse => _ses.Can(PermKey, Perm.Edit);

    /// <summary>Thời điểm đang hẹn tắt máy (null = không có / đã qua).</summary>
    public DateTime? DangHen()
    {
        var s = _set.Get(Key);
        if (s.Length == 0) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) && t > _now()) return t;
        _set.Set(Key, "");
        return null;
    }

    public HgKetQua HenPhut(int phut)
    {
        _ses.Require(PermKey);
        var r = HenGioCore.TheoPhut(_now(), phut);
        return r is null
            ? new(false, null, $"Số phút phải từ 1 đến {HenGioCore.MaxPhut} (7 ngày).")
            : Dat(r.Value.Luc, r.Value.Giay, $"sau {phut} phút");
    }

    public HgKetQua HenGio(string text)
    {
        _ses.Require(PermKey);
        if (!HenGioCore.TryParseGio(text, out var h, out var m)) return new(false, null, "Giờ không đúng — nhập dạng 22:30 (giờ 0–23, phút 0–59).");
        var r = HenGioCore.TheoGio(_now(), h, m)!.Value;
        return Dat(r.Luc, r.Giay, "lúc " + HenGioCore.NhanLuc(r.Luc, _now()));
    }

    private HgKetQua Dat(DateTime luc, int giay, string mota)
    {
        if (DangHen() is not null) Huy0();                       // hẹn mới thay hẹn cũ
        var (code, outp) = _run(HenGioCore.Dat(_os, giay));
        if (code != 0) return new(false, null, "Không đặt được hẹn giờ tắt máy: " + (outp.Length > 0 ? outp : $"mã lỗi {code}"));
        _set.Set(Key, luc.ToString("o", CultureInfo.InvariantCulture));
        _audit.Log(_ses.Username, "Hẹn giờ tắt máy", $"Đặt tắt máy {mota} (lúc {luc:dd/MM/yyyy HH:mm})");
        return new(true, luc, $"Máy sẽ tắt {mota} — {HenGioCore.NhanLuc(luc, _now())}.");
    }

    public HgKetQua Huy()
    {
        _ses.Require(PermKey);
        var had = DangHen() is not null;
        var (code, outp) = _run(HenGioCore.Huy(_os));
        _set.Set(Key, "");
        if (code != 0 && had) return new(false, null, "Không hủy được bằng lệnh hệ thống (có thể máy đã được hủy từ nơi khác): " + outp);
        _audit.Log(_ses.Username, "Hẹn giờ tắt máy", "Hủy hẹn giờ tắt máy");
        return new(true, null, had ? "Đã hủy hẹn giờ tắt máy." : "Hiện không có hẹn giờ nào.");
    }

    private void Huy0() { _run(HenGioCore.Huy(_os)); _set.Set(Key, ""); }

    private static (int, string) Run(HgLenh l)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(l.File, l.Args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p is null) return (-1, "không chạy được lệnh");
            var o = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, o.Trim());
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}

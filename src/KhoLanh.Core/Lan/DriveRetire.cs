using KhoLanh.Core.Services;

namespace KhoLanh.Core.Lan;

/// <summary>
/// 1.121: BỎ "Thư mục / Google Drive dùng chung" (người dùng duyệt mẫu tab Máy chủ — máy ở xa nay nối máy chủ qua Internet / đường hầm).
/// Gọi MỘT lần lúc mở app (<see cref="AppServices.RetireDriveSync"/>, trước khi bật LAN) — máy cũ còn cài Drive dùng chung tự chuyển:
///  • Máy chính (drive_mode "chinh"): chạy 1 lượt cuối (nhận nốt thay đổi máy xa đã gửi vào thư mục, đưa bản cuối lên, nhả khóa máy chính) rồi tắt.
///    KHÔNG xóa tệp nào trên Drive.
///  • Máy xem từ xa (drive_mode "xa" / lan_role "xa"): chụp dữ liệu hiện tại vào SaoLuu (<c>truoc-bo-drive_yyyy-MM-dd_HHmmss.db</c>), giữ nguyên
///    dữ liệu + sổ chờ trên máy (không gửi qua Drive nữa — tránh gửi 2 lần), rồi tắt; lần đăng nhập tới máy tự nối máy chủ theo kế hoạch LAN
///    của tài khoản dùng chung (<see cref="LanPlanSync.ApplyAtLogin"/>), sổ chờ gửi lên máy chủ như máy con LAN bình thường.
/// Sau khi chuyển: không còn máy nào có drive_mode → <see cref="DriveSyncService"/> không tự chạy nền nữa (Start bỏ qua khi chưa bật).
/// </summary>
public static class DriveRetire
{
    /// <summary>Settings: lúc đã bỏ đồng bộ Drive dùng chung trên máy này (ISO) — chỉ để ghi nhận / hiện lại.</summary>
    public const string DoneKey = "drive_bo";
    public const string AuditAction = "Bỏ đồng bộ Drive dùng chung";

    /// <summary>Kết quả chuyển: vai Drive cũ ("chinh" / "xa"), đường dẫn bản chụp trước khi chuyển (máy xa, "" nếu không chụp), câu báo.</summary>
    public sealed record Result(string OldMode, string Snapshot, string Message);

    /// <summary>Máy này còn cài Drive dùng chung (máy chính / máy xem từ xa)?</summary>
    public static bool Needed(SettingsService st) =>
        st.Get("drive_mode") is DriveSyncService.ModeMain or DriveSyncService.ModeRemote || st.Get("lan_role") == LanService.RoleRemote;

    /// <summary>Chuyển máy cũ (không làm gì nếu máy không dùng Drive dùng chung). Trả null khi không có gì đổi.</summary>
    public static Result? Run(AppServices s, Func<DateTime>? now = null)
    {
        var st = s.Settings;
        if (!Needed(st)) return null;
        var at = (now ?? (() => DateTime.Now))();
        var mode = st.Get("drive_mode");
        var remote = mode == DriveSyncService.ModeRemote || st.Get("lan_role") == LanService.RoleRemote;
        var drive = s.Drive;
        try { drive.Stop(); } catch { }
        string snap = "", msg;
        if (!remote)
        {
            // máy chính: lượt cuối — áp nốt tệp thay đổi máy xa đã gửi, đưa bản cuối lên, nhả khóa (ReleaseOnExit tự bắt lỗi, Drive chưa chạy thì bỏ qua)
            try { drive.ReleaseOnExit(); } catch { }
            msg = "Máy này từng là máy chính của Thư mục / Drive dùng chung: đã chạy lượt đồng bộ cuối và tắt (tệp trên Drive giữ nguyên). "
                  + "Máy ở xa nay nối máy chủ qua Internet (Đồng bộ & sao lưu › Máy chủ).";
        }
        else
        {
            // máy xem từ xa: chụp dữ liệu trước khi chuyển; dữ liệu + sổ chờ giữ nguyên trên máy (gửi lên máy chủ khi nối LAN / Internet)
            try
            {
                snap = Path.Combine(s.Backup.AutoBackupFolder, $"truoc-bo-drive_{at:yyyy-MM-dd_HHmmss}.db");
                s.Backup.BackupTo(snap);
            }
            catch { snap = ""; }
            var pend = 0;
            try { pend = s.Lan.PendingCount(); } catch { }
            msg = "Máy này từng là máy xem từ xa qua Thư mục / Drive dùng chung: đã tắt Drive dùng chung, dữ liệu trên máy giữ nguyên"
                  + (snap.Length > 0 ? $" (đã chụp vào SaoLuu: {Path.GetFileName(snap)})" : "")
                  + (pend > 0 ? $", {pend} bảng thay đổi chờ gửi sẽ gửi lên máy chủ khi nối được" : "")
                  + ". Đăng nhập tài khoản dùng chung là máy tự nối máy chủ.";
        }
        st.Set("drive_mode", DriveSyncService.ModeOff);
        st.Set("drive_seq", "0");
        if (st.Get("lan_role") == LanService.RoleRemote) st.Set("lan_role", LanService.RoleSingle);
        st.Set(DoneKey, at.ToString("s"));
        try { s.Audit.Log(s.Session.Username, AuditAction, msg); } catch { }
        // phần LAN đang chạy theo vai cũ ("xa") → đọc lại vai (chưa bật; App.StartLan / đăng nhập bật sau)
        if (remote) { try { s.ReloadLan(start: false); } catch { } }
        return new Result(remote ? DriveSyncService.ModeRemote : DriveSyncService.ModeMain, snap, msg);
    }
}

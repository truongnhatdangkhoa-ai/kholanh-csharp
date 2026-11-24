namespace KhoLanh.Core.Lan;

/// <summary>Tự nhận vai Đồng bộ Google Drive theo TÀI KHOẢN đăng nhập (xem <see cref="DrivePlan"/>).</summary>
public static class DrivePlanSync
{
    /// <summary>
    /// Gọi sau khi đăng nhập. Kế hoạch Drive chưa bật → không làm gì. Tài khoản máy chính đăng nhập mà chưa có máy chính khác đang hoạt động
    /// → máy này thành MÁY CHÍNH; còn lại (tài khoản khác, hoặc đã có máy chính ở nơi khác) → MÁY XEM TỪ XA.
    /// Trả câu báo (null nếu không có gì đổi / không áp dụng).
    /// </summary>
    public static string? ApplyAtLogin(AppServices s)
    {
        var dp = s.Accounts.Plan?.Drive;
        if (dp is null || !dp.On) return null;
        var lan = s.Lan; var drive = s.Drive; var user = s.Session.Username;
        if (lan.Enabled && lan.Role != LanService.RoleRemote) return null;           // máy đang dùng LAN: không đụng
        if (drive.Dir.Length == 0 || !Directory.Exists(drive.Dir))
            return "Đồng bộ Drive: máy này chưa chọn thư mục Google Drive (Hệ thống › Dữ liệu & sao lưu › Đồng bộ › Thư mục / Google Drive dùng chung › Chọn thư mục).";
        drive.SetKey(dp.Key);
        drive.CurrentUser = user;
        var wantMain = dp.IsMain(user) && !drive.IsHome;   // máy ở nhà luôn là máy xem từ xa
        string msg;
        if (wantMain)
        {
            if (drive.Mode == DriveSyncService.ModeMain) { drive.Start(); return null; }
            if (drive.HeldByOther(lan) is { } o)
            {
                if (drive.Mode != DriveSyncService.ModeRemote) drive.BecomeRemote(s);
                msg = $"Máy “{o.Name}” (tài khoản {o.User}) đang là máy chính nên máy này là máy xem từ xa. Máy kia đăng xuất / thoát thì đăng nhập lại để làm máy chính.";
                s.Audit.Log(user, "Tự nhận vai Drive", msg);
                return msg;
            }
            msg = "Máy này là MÁY CHÍNH (tài khoản " + user + "): " + drive.BecomeMain(s, user) + ".";
        }
        else
        {
            if (drive.Mode == DriveSyncService.ModeRemote) { drive.Start(); return null; }
            if (drive.Mode == DriveSyncService.ModeMain) drive.ReleaseOnExit();       // đưa bản cuối lên Drive, nhả khóa
            drive.BecomeRemote(s);
            msg = $"Máy này là máy xem từ xa (tài khoản {user} không phải tài khoản máy chính).";
        }
        s.Audit.Log(user, "Tự nhận vai Drive", msg);
        return msg;
    }
}

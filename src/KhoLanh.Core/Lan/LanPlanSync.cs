using KhoLanh.Core.Services;

namespace KhoLanh.Core.Lan;

/// <summary>Áp kế hoạch phân vai LAN lúc đăng nhập + ghi địa chỉ / khóa của máy chủ lên Google Sheet.</summary>
public static class LanPlanSync
{
    /// <summary>
    /// Gọi ngay sau khi đăng nhập (trước khi mở cửa sổ chính). Kế hoạch chưa bật → không làm gì.
    /// Vai đổi → lưu cài đặt và khởi động lại phần LAN. Trả câu báo (null nếu không đổi gì).
    /// </summary>
    public static string? ApplyAtLogin(AppServices s)
    {
        var plan = s.Accounts.Plan;
        if (plan is null) return null;
        var lan = s.Lan;
        if (lan.Role == LanService.RoleRemote) return null;   // máy xem từ xa (Google Drive): không tự nhận vai LAN
        var d = plan.Decide(s.Session.Username, lan.MachineId, lan.Role, LanPlan.CanStandby(s.Session.CurrentUser));
        if (d is null) return null;
        var oldRole = lan.Role;
        if (!LanService.ApplyPlan(s.Settings, plan, d.Role)) return null;
        // Sắp thay dữ liệu module bằng dữ liệu máy chủ → sao lưu trước (phòng máy đơn có dữ liệu riêng).
        if (oldRole is LanService.RoleSingle or LanService.RoleServer && d.Role is LanService.RoleStandby or LanService.RoleClient)
            try { s.Backup.BackupTo(Path.Combine(s.Backup.AutoBackupFolder, $"truoc-doi-vai-{DateTime.Now:yyyyMMdd-HHmmss}.db")); } catch { }
        s.ReloadLan();
        var msg = oldRole == d.Role
            ? $"Cập nhật địa chỉ máy chủ LAN ({d.Why})."
            : $"Máy này chuyển từ {LanService.RoleLabel(oldRole)} sang {LanService.RoleLabel(d.Role)}: {d.Why}.";
        s.Audit.Log(s.Session.Username, "Tự nhận vai LAN", msg);
        return oldRole == d.Role ? null : msg;
    }

    /// <summary>
    /// Máy chủ chính / dự phòng (có mã quản trị) ghi tên, địa chỉ, khóa LAN của mình lên Google Sheet khi khác bản đang có.
    /// Trả câu báo lỗi (null = ổn hoặc không cần làm gì).
    /// </summary>
    public static async Task<string?> PublishAsync(AppServices s, CancellationToken ct = default)
    {
        var acc = s.Accounts; var lan = s.Lan;
        var plan = acc.Plan;
        if (!acc.Enabled || !lan.Enabled || !lan.Capable) return null;
        plan ??= new LanPlan();
        var addrs = lan.Addresses();
        var standbys = lan.Machines().Where(m => m.Role == LanService.RoleStandby && m.Addr.Length > 0)
            .Select(m => (m.Id, m.Name, m.Addr.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(), m.User)).ToList();
        var user = s.Session.Username;
        var users = s.Auth.ListUsers();
        bool Standby(string u) => LanPlan.CanStandby(users.FirstOrDefault(x => string.Equals(x.Username, u, StringComparison.OrdinalIgnoreCase)));
        bool Fix(LanPlan p) { var c = p.Fix(lan.MachineId, lan.MachineName, lan.Role, user, addrs, lan.Key, lan.Port, standbys, Standby); if (c || !p.On) p.On = true; return c; }
        if (!Fix(plan.Clone())) return null;
        if (!acc.CanWrite) return "Máy này chưa có mã quản trị nên chưa ghi được địa chỉ máy chủ lên Google Sheet — các máy khác chưa tự tìm thấy máy này.";
        try { await acc.UpdatePlanAsync(Fix, ct); return null; }
        catch (BusinessException ex) { return "Chưa ghi được địa chỉ máy chủ lên Google Sheet: " + ex.Message; }
    }

    /// <summary>
    /// Quản trị bấm "Chuyển máy chủ về máy này": ghi máy này là máy chủ chính trên Google Sheet, đổi vai máy này thành máy chủ;
    /// máy chủ cũ (nếu đang chạy) được báo nhường vai khi máy này đã chép đủ dữ liệu, lần mở sau là máy con.
    /// </summary>
    public static async Task<string> ClaimServerAsync(AppServices s, CancellationToken ct = default)
    {
        s.Session.RequireAdmin();
        var lan = s.Lan;
        var plan = s.Accounts.Plan ?? new LanPlan();
        if (!s.Accounts.Enabled) throw new BusinessException("Chưa kết nối Google Sheet chung (Hệ thống › Kết nối).");
        if (plan.Chu?.Id == lan.MachineId && lan.Role == LanService.RoleServer) return "Máy này đã là máy chủ chính.";
        if (lan.Enabled && lan.Role != LanService.RoleServer && !lan.Online && plan.Chu is not null)
            throw new BusinessException("Máy này đang không nối được máy chủ hiện tại nên chưa có đủ dữ liệu mới nhất. Nối lại rồi chuyển, hoặc chuyển ở chính máy chủ cũ khi nó tắt hẳn.");
        var oldName = plan.Chu?.Name ?? "";
        var hadOther = plan.Chu is not null && plan.Chu.Id != lan.MachineId;
        var key = lan.Key.Length > 0 ? lan.Key : plan.Key;
        var addrs = lan.Addresses();
        var user = s.Session.Username;
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        plan = await s.Accounts.UpdatePlanAsync(p =>
        {
            p.On = true;
            p.Chu = new LanPlan.Slot(lan.MachineId, lan.MachineName, addrs, now, user);
            if (p.DuPhong?.Id == lan.MachineId) p.DuPhong = null;
            if (key.Length > 0) p.Key = key;
            if (p.Port == 0) p.Port = lan.Port;
            return true;
        }, ct);
        LanService.ApplyPlan(s.Settings, plan, LanService.RoleServer);
        if (hadOther) LanService.MarkClaim(s.Settings);
        s.ReloadLan();
        var msg = hadOther ? $"Máy này nhận vai máy chủ chính thay {oldName}. Máy cũ trả vai sau vài giây (khi máy này chép đủ dữ liệu) và thành máy con." : "Máy này là máy chủ chính.";
        s.Audit.Log(user, "Chuyển máy chủ LAN", msg);
        return msg;
    }

    /// <summary>
    /// 1.106: Quản trị TẮT máy chủ trên máy này: bỏ ghi nhận máy chủ trên Google Sheet, máy này thành máy đơn. Có máy dự phòng thì máy đó
    /// tự nhận làm máy chủ (như khi máy chủ tắt); không có thì các máy con làm tiếp trên dữ liệu trong máy, bật máy chủ lại thì tự gộp.
    /// </summary>
    public static async Task<string> ReleaseServerAsync(AppServices s, CancellationToken ct = default)
    {
        s.Session.RequireAdmin();
        var lan = s.Lan;
        var plan = s.Accounts.Plan;
        if (plan?.Chu?.Id != lan.MachineId && lan.Role != LanService.RoleServer) return "Máy này không phải máy chủ.";
        await s.Accounts.UpdatePlanAsync(p => { if (p.Chu?.Id != lan.MachineId) return false; p.Chu = null; return true; }, ct);
        s.Settings.Set("lan_role", LanService.RoleSingle);
        s.ReloadLan();
        var msg = "Đã tắt máy chủ trên máy này." + (plan?.DuPhong is { } d ? $" Máy dự phòng {d.Name} tự làm máy chủ." : " Các máy con làm tiếp trên dữ liệu trong máy, bật máy chủ lại thì tự gộp.");
        s.Audit.Log(s.Session.Username, "Tắt máy chủ LAN", msg);
        return msg;
    }
}

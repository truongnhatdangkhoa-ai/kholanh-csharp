namespace KhoLanh.Core.Models;

public static class Roles
{
    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string Staff = "staff";

    public static readonly string[] All = { Admin, Manager, Staff };

    public static string Label(string role) => role switch
    {
        Admin => "Quản trị",
        Manager => "Quản lý",
        Staff => "Nhân viên",
        _ => role,
    };

    /// <summary>Quản lý danh mục, hủy phiếu, xem báo cáo.</summary>
    public static bool CanManage(string role) => role is Admin or Manager;
    /// <summary>Tài khoản, khôi phục dữ liệu, cài đặt.</summary>
    public static bool IsAdmin(string role) => role == Admin;
}

public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Role { get; set; } = Roles.Staff;
    public bool IsActive { get; set; } = true;
    /// <summary>Quyền chỉnh riêng (JSON {mục: mức}); rỗng = theo vai trò.</summary>
    public string Permissions { get; set; } = "";
    public string RoleLabel => Roles.Label(Role) + (Role != Roles.Admin && Permissions.Length > 0 ? " (riêng)" : "");
    public string StatusLabel => IsActive ? "Đang dùng" : "Đã khóa";
}

public class AuditEntry
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public string Username { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

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
    public string RoleLabel => Roles.Label(Role);
    public string StatusLabel => IsActive ? "Đang dùng" : "Đã khóa";
}

public class Product
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "thùng";
    /// <summary>Khối lượng chuẩn của 1 đơn vị (kg), dùng để gợi ý số kg khi nhập.</summary>
    public double KgPerUnit { get; set; }
    /// <summary>Các size thường dùng, cách nhau bằng dấu phẩy (gợi ý khi lập phiếu).</summary>
    public string Sizes { get; set; } = "";
    public double MinStock { get; set; }
    public string Note { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public string Display => $"{Code} - {Name}";
    public IEnumerable<string> SizeList =>
        Sizes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class Owner
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string Display => $"{Code} - {Name}";
}

public static class VoucherTypes
{
    public const string In = "N";
    public const string Out = "X";
    public static string Label(string t) => t == In ? "Nhập" : "Xuất";
}

public class Voucher
{
    public long Id { get; set; }
    public string No { get; set; } = "";
    public string Type { get; set; } = VoucherTypes.In;
    public DateTime Date { get; set; } = DateTime.Today;
    public long OwnerId { get; set; }
    public string Partner { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string Note { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool Cancelled { get; set; }
    public string CancelReason { get; set; } = "";
    public List<VoucherLine> Lines { get; set; } = new();
}

public class VoucherLine
{
    public long Id { get; set; }
    public long VoucherId { get; set; }
    public long ProductId { get; set; }
    public string Size { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Location { get; set; } = "";
    public double Quantity { get; set; }
    public double Weight { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>Một dòng trong danh sách phiếu (đã kèm tên chủ hàng và tổng).</summary>
public class VoucherSummary
{
    public long Id { get; set; }
    public string No { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime Date { get; set; }
    public string OwnerName { get; set; } = "";
    public string Partner { get; set; } = "";
    public string Note { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public bool Cancelled { get; set; }
    public int LineCount { get; set; }
    public double TotalQuantity { get; set; }
    public double TotalWeight { get; set; }
    public string TypeLabel => VoucherTypes.Label(Type);
    public string StatusLabel => Cancelled ? "Đã hủy" : "Hiệu lực";
}

/// <summary>Chi tiết dòng phiếu kèm thông tin mã hàng, dùng cho in và hiển thị.</summary>
public class VoucherLineView
{
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Size { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Location { get; set; } = "";
    public double Quantity { get; set; }
    public double Weight { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>Tồn theo khóa chủ hàng + mã hàng + size + lô + vị trí.</summary>
public class StockItem
{
    public long OwnerId { get; set; }
    public string OwnerName { get; set; } = "";
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Size { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Location { get; set; } = "";
    public double Quantity { get; set; }
    public double Weight { get; set; }
    public DateTime? FirstIn { get; set; }
    public int DaysInStock => FirstIn is null ? 0 : (int)(DateTime.Today - FirstIn.Value.Date).TotalDays;
}

/// <summary>Tồn tổng theo mã hàng, kèm ngưỡng cảnh báo.</summary>
public class ProductStock
{
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Quantity { get; set; }
    public double Weight { get; set; }
    public double MinStock { get; set; }
    public bool IsLow => MinStock > 0 && Quantity < MinStock;
}

/// <summary>Một dòng sổ kho: một dòng phiếu đã ghi, kèm tồn lũy kế.</summary>
public class LedgerEntry
{
    public DateTime Date { get; set; }
    public string VoucherNo { get; set; } = "";
    public string Type { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Size { get; set; } = "";
    public string Lot { get; set; } = "";
    public string Location { get; set; } = "";
    public string Partner { get; set; } = "";
    public double InQuantity { get; set; }
    public double OutQuantity { get; set; }
    public double InWeight { get; set; }
    public double OutWeight { get; set; }
    public double BalanceQuantity { get; set; }
    public double BalanceWeight { get; set; }
    public string TypeLabel => VoucherTypes.Label(Type);
}

/// <summary>Báo cáo nhập xuất tồn theo mã hàng + size trong một khoảng ngày.</summary>
public class NxtRow
{
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Size { get; set; } = "";
    public string Unit { get; set; } = "";
    public double OpenQuantity { get; set; }
    public double OpenWeight { get; set; }
    public double InQuantity { get; set; }
    public double InWeight { get; set; }
    public double OutQuantity { get; set; }
    public double OutWeight { get; set; }
    public double CloseQuantity => OpenQuantity + InQuantity - OutQuantity;
    public double CloseWeight => OpenWeight + InWeight - OutWeight;
}

public class AuditEntry
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public string Username { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

public class DashboardInfo
{
    public int ProductCount { get; set; }
    public int OwnerCount { get; set; }
    public double TotalQuantity { get; set; }
    public double TotalWeight { get; set; }
    public int TodayInCount { get; set; }
    public int TodayOutCount { get; set; }
    public double TodayInQuantity { get; set; }
    public double TodayOutQuantity { get; set; }
    public List<ProductStock> LowStock { get; set; } = new();
    public List<VoucherSummary> Recent { get; set; } = new();
}

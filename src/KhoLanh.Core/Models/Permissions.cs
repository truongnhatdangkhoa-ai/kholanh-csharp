using System.Text.Json;

namespace KhoLanh.Core.Models;

/// <summary>
/// Phân quyền tới từng màn hình. Mỗi tài khoản có một mức cho từng mục: Không / Xem / Xem + xuất file / Sửa.
/// Quản trị luôn có toàn quyền. Tài khoản chưa chỉnh riêng thì dùng mức mặc định theo vai trò.
/// </summary>
public static class Perm
{
    public const string None = "", View = "xem", Export = "xuat", Edit = "sua";

    public record Item(string Key, string Label, string Group, string[] Levels, string Hint);

    public static readonly Item[] Items =
    {
        new("m1", "Tồn kho An An", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = nạp file, Đặc tính, chuyển mã"),
        new("m2", "Tồn kho gửi", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = nạp file, phiếu xuất, Đặc tính"),
        new("m7", "Tồn kho theo vị trí", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = nạp file, lập / sửa / xóa phiếu xuất"),
        new("m8", "Sơ đồ kho", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = cấu hình kho, quy cách, khóa kiểm kê"),
        new("m3", "Tổng hợp", "Báo cáo", new[] { View, Export, Edit }, "Sửa = nạp file, sửa danh mục mã hóa, ánh xạ tên"),
        new("m4", "Báo cáo rã đông", "Báo cáo", new[] { View, Export, Edit }, "Sửa = nạp file, lưu số liệu theo ngày"),
        new("m5", "Đối chiếu hàng ngày (báo cáo hàng ngày)", "Báo cáo", new[] { View, Export, Edit }, "Sửa = ghi vào file Excel Kho đã liên kết"),
        new("m9", "Đối chiếu kiểm kê", "Báo cáo", new[] { View, Export, Edit }, "Sửa = chọn trang kiểm kê, số kế toán, chụp tồn, sửa mã, chốt kỳ"),
        new("m10", "Kết quả kháng sinh", "Báo cáo", new[] { View, Export, Edit }, "Sửa = nhập kết quả (theo lô, Excel), xóa dữ liệu, sửa quy định kháng sinh"),
        new("m11", "Phiếu xuất hệ thống", "Phiếu & chứng từ", new[] { View, Export }, "Chỉ xem phiếu xuất của mọi module; Xem + xuất file = xuất Excel danh sách"),
        new("m6", "Nhập liệu", "Nhập liệu", new[] { View, Export, Edit }, "Sửa = tạo / sửa / xóa trang, lưu mẫu"),
        new("backup", "Sao lưu và cài đặt", "Hệ thống", new[] { Edit }, "Sao lưu, khôi phục, chép dữ liệu app cũ"),
        new("audit", "Nhật ký thao tác", "Hệ thống", new[] { View }, ""),
    };

    public static int Rank(string? level) => level switch { View => 1, Export => 2, Edit => 3, _ => 0 };

    public static string LevelLabel(string level) => level switch
    {
        View => "Xem", Export => "Xem + xuất file", Edit => "Sửa / toàn quyền", _ => "Không",
    };

    public static Item? Find(string key) => Items.FirstOrDefault(i => i.Key == key);

    /// <summary>Mức mặc định theo vai trò (dùng khi tài khoản chưa chỉnh riêng).</summary>
    public static Dictionary<string, string> Defaults(string role)
    {
        var d = new Dictionary<string, string>();
        foreach (var i in Items) d[i.Key] = i.Levels[^1];
        if (role == Roles.Staff)
        {
            d["backup"] = None; d["audit"] = None;
            d["m1"] = Export; d["m2"] = Export; d["m7"] = Export; d["m8"] = Export;
            d["m3"] = Export; d["m4"] = Export; d["m5"] = Export; d["m9"] = Export; d["m10"] = Export; d["m11"] = Export; // nhân viên: nhập liệu được sửa (m6 mặc định Sửa)
        }
        return d;
    }

    public static Dictionary<string, string> Parse(string? json)
    {
        var d = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json)) return d;
        try
        {
            foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new())
                if (Find(k) is { } item && (v == None || item.Levels.Contains(v))) d[k] = v;
        }
        catch { /* hỏng thì coi như chưa chỉnh */ }
        return d;
    }

    public static string Serialize(IDictionary<string, string> map) =>
        JsonSerializer.Serialize(Items.Where(i => map.ContainsKey(i.Key)).ToDictionary(i => i.Key, i => map[i.Key]));

    /// <summary>Bảng quyền đầy đủ của 1 tài khoản (đã gộp mặc định theo vai trò).</summary>
    public static Dictionary<string, string> Effective(User? u)
    {
        if (u is null) return Items.ToDictionary(i => i.Key, _ => None);
        if (u.Role == Roles.Admin) return Items.ToDictionary(i => i.Key, i => i.Levels[^1]);
        var d = Defaults(u.Role);
        if (!string.IsNullOrWhiteSpace(u.Permissions))
        {
            var custom = Parse(u.Permissions);
            foreach (var i in Items) d[i.Key] = custom.TryGetValue(i.Key, out var v) ? v : None;
        }
        return d;
    }

    public static string Level(User? u, string key) =>
        u?.Role == Roles.Admin ? (Find(key)?.Levels[^1] ?? Edit) : Effective(u).GetValueOrDefault(key, None);

    public static bool Has(User? u, string key, string level = View) => u is not null && Rank(Level(u, key)) >= Rank(level);
}

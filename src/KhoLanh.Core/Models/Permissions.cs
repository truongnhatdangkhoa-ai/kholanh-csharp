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
        new("dash", "Tổng quan", "Chung", new[] { View }, "Trang tổng quan"),
        new("in", "Phiếu nhập", "Nghiệp vụ", new[] { Edit }, "Lập phiếu nhập"),
        new("out", "Phiếu xuất", "Nghiệp vụ", new[] { Edit }, "Lập phiếu xuất"),
        new("list", "Danh sách phiếu", "Nghiệp vụ", new[] { View }, "Xem, in lại phiếu"),
        new("cancel", "Hủy phiếu", "Nghiệp vụ", new[] { Edit }, "Hủy phiếu đã lưu"),
        new("stock", "Tồn kho", "Kho", new[] { View }, ""),
        new("ledger", "Sổ kho", "Kho", new[] { View }, ""),
        new("nxt", "Báo cáo NXT", "Kho", new[] { View }, ""),
        new("m1", "Tồn kho An An", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = nạp file, Đặc tính, chuyển mã"),
        new("m2", "Tồn kho gửi", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = nạp file, phiếu xuất, Đặc tính"),
        new("products", "Mã hàng", "Danh mục", new[] { View, Edit }, ""),
        new("owners", "Chủ hàng", "Danh mục", new[] { View, Edit }, ""),
        new("backup", "Sao lưu và cài đặt", "Hệ thống", new[] { Edit }, "Sao lưu, chép dữ liệu app cũ, thông tin phiếu"),
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
            d["cancel"] = None; d["backup"] = None; d["audit"] = None;
            d["products"] = View; d["owners"] = View;
            d["m1"] = Export; d["m2"] = Export;
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

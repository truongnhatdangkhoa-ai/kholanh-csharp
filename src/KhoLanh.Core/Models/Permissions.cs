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
        new("lx", "Tổng quan · Lịch xuất", "Tổng quan", new[] { View, Edit }, "Sửa = thêm / sửa / xóa / chuyển lịch xuất, việc cần làm, số cont"),
        new("m6", "Nhập liệu", "Nhập liệu", new[] { View, Export, Edit }, "Sửa = tạo / sửa / xóa trang, lưu mẫu"),
        new("m1", "Tồn kho An An", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = nạp file, Đặc tính, chuyển mã"),
        new("m2", "Tồn kho gửi", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = nạp file, phiếu xuất, Đặc tính"),
        new("m7", "Tồn kho theo vị trí", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = nạp file, lập / sửa / xóa phiếu xuất"),
        new("m1vtb", "Vị Trí Bột", "Tồn kho gốc", new[] { View, Edit }, "Xem tồn bột / sốt theo vị trí (đẩy từ NXT Bột/Sốt); Sửa = lập / hủy trừ / trừ lại / xóa phiếu xuất"),
        new("m3sk", "NXT Bột/Sốt (Sổ kho)", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = lập phiếu nhập / xuất / chuyển, chốt tồn, kiểm kê, định mức, nạp Excel, hoàn tác"),
        new("m8", "Sơ đồ kho", "Tồn kho gốc", new[] { View, Export, Edit }, "Sửa = cấu hình kho, quy cách, khóa kiểm kê"),
        new("m13", "Phiếu xuất kho", "Tồn kho gốc", new[] { View, Export, Edit }, "Xem + xuất file = xuất Excel phiếu; Sửa = lập / sửa / xóa phiếu"),
        new("m4", "Báo cáo rã đông", "Chức năng", new[] { View, Export, Edit }, "Sửa = nạp file, lưu số liệu theo ngày"),
        new("m5", "Đối chiếu hàng ngày (báo cáo hàng ngày)", "Chức năng", new[] { View, Export, Edit }, "Sửa = ghi vào file Excel Kho đã liên kết"),
        new("m9", "Đối chiếu kiểm kê", "Chức năng", new[] { View, Export, Edit }, "Sửa = chọn trang kiểm kê, số kế toán, chụp tồn, sửa mã, chốt kỳ"),
        new("m7ck", "Số liệu cuối kỳ (đối chiếu kế toán - kho)", "Chức năng", new[] { View, Export, Edit }, "Sửa = nạp file, xác nhận nguyên nhân, sửa mã / điều chỉnh, hoàn tác, xóa dữ liệu kỳ"),
        new("m12", "In chứng từ kho", "Chức năng", new[] { View, Export, Edit }, "Xem + xuất file = in / xuất Excel phiếu; Sửa = nạp file, gộp / tách phiếu, người ký, bổ sung tên hàng"),
        new("m10", "Kết quả kháng sinh", "Chức năng", new[] { View, Export, Edit }, "Sửa = nhập kết quả (theo lô, Excel), xóa dữ liệu, sửa quy định kháng sinh"),
        new("m24cc", "Chấm công", "Chức năng", new[] { View, Export, Edit }, "Nạp file máy chấm công, sửa giờ / nhân viên / quy tắc tính giờ (Sửa); xem / xuất Excel bảng công tháng"),
        new("m3", "Tổng hợp", "Chức năng", new[] { View, Export, Edit }, "Sửa = nạp file, sửa danh mục mã hóa, ánh xạ tên"),
        new("m3dm", "Danh mục mã hàng", "Chức năng", new[] { View, Export, Edit }, "Sửa = thêm / sửa / xóa mã trong Danh sách mã hàng dùng chung (Tổng hợp), thêm mã đang dùng chưa có"),
        new("m14", "Container 3D", "Công cụ", new[] { View, Export, Edit }, "Xem + xuất file = Excel / ảnh 3D; Sửa = lưu container, phương án, danh mục thùng"),
        new("m21", "Xử lý PDF & Excel", "Công cụ", new[] { Edit }, "Gộp / tách / xoay / đóng dấu PDF, ảnh ↔ PDF; gộp / tách / làm sạch Excel (chỉ tạo file mới)"),
        new("m22", "So sánh & Tra cứu bảng", "Công cụ", new[] { Edit }, "So 2 bảng Excel / CSV theo cột khóa (giống / khác / chỉ có ở A / B); kéo cột từ bảng B sang A kiểu VLOOKUP"),
        new("m15", "Scan tài liệu", "Công cụ", new[] { Edit }, "Quét ảnh / PDF thành bản in sạch; tài liệu lưu riêng từng tài khoản"),
        new("m19", "Lưu trữ cá nhân", "Công cụ", new[] { Edit }, "Ghi chú, liên kết, ảnh, tệp riêng từng tài khoản; đăng nhập Google cá nhân để sao lưu Drive"),
        // 1.139: hẹn giờ tắt máy tính — chỉ Quản trị (hoặc tài khoản Quản trị tích riêng).
        new("hengio", "Hẹn giờ tắt máy", "Công cụ", new[] { Edit }, "Hẹn giờ tắt máy tính này theo số phút hoặc giờ đồng hồ, hủy hẹn giờ. Mặc định chỉ Quản trị"),
        new("backup", "Sao lưu và cài đặt", "Hệ thống", new[] { Edit }, "Sao lưu, khôi phục, chép dữ liệu app cũ"),
        // 1.114 (người dùng: "máy ngoài LAN khôi phục bình thường, chỉ cần tài khoản được phân quyền"): máy đang nối máy chủ LAN thì vẫn khóa.
        new("khoiphuc", "Khôi phục dữ liệu", "Hệ thống", new[] { Edit }, "Khôi phục bản sao lưu (trên máy / Google Drive) ở máy KHÔNG nối máy chủ LAN — máy nối mạng LAN mà đang ngoài mạng thì tách ra dùng riêng, không ảnh hưởng máy chủ / máy trong LAN"),
        new("audit", "Nhật ký thao tác", "Hệ thống", new[] { View }, ""),
        // 1.110 (người dùng: "tài khoản được phân quyền tạo tài khoản là đồng bộ tài khoản lên Sheet được, app cũ làm được"): quyền theo tài khoản, máy nào cũng vậy.
        new("taikhoan", "Tạo / sửa tài khoản", "Hệ thống", new[] { Edit }, "Tạo, sửa, khóa, đặt lại mật khẩu tài khoản Nhân viên / Quản lý (tự đồng bộ lên Google Sheet chung). Không tạo / sửa được tài khoản Quản trị"),
        new("phone", "Đồng bộ điện thoại", "Hệ thống", new[] { Edit }, "Kết nối Web App, đẩy tồn các module lên điện thoại, nhận phiếu xuất điện thoại tạo (mỗi bảng chỉ khi được xem module đó)"),
        new("m11", "Phiếu xuất hệ thống", "Hệ thống", new[] { View, Export }, "Chỉ xem phiếu xuất của mọi module; Xem + xuất file = xuất Excel danh sách"),
        // 1.121: bỏ "drivepush" (Gửi dữ liệu từ máy xem từ xa qua Drive) — bỏ hẳn Thư mục / Drive dùng chung; quyền đã lưu chứa khóa này bị bỏ qua.
    };

    /// <summary>
    /// Mục quyền thêm sau: tài khoản đã chỉnh quyền riêng từ trước (chưa có mục này trong bảng quyền) vẫn được mức này, để nâng cấp app
    /// không tự tắt Trang Tổng quan / không chặn máy ở nhà. Quản trị đổi lại được bất cứ lúc nào.
    /// </summary>
    public static readonly Dictionary<string, string> MissingDefault = new() { ["lx"] = View, ["m1vtb"] = View, ["m21"] = Edit, ["m22"] = Edit, ["m3dm"] = View, ["m19"] = Edit };

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
        d["khoiphuc"] = None;     // 1.114: khôi phục chỉ khi Quản trị tích riêng (Quản trị luôn có)
        d["hengio"] = None;       // 1.139: tắt máy là việc nguy hiểm với máy chủ LAN — chỉ Quản trị (hoặc tích riêng)
        d["taikhoan"] = None;     // 1.110: tạo / sửa tài khoản chỉ khi Quản trị tích riêng (Quản trị luôn có)
        if (role == Roles.Staff)
        {
            d["backup"] = None; d["audit"] = None; d["phone"] = None;
            d["m24cc"] = None;   // 1.129: chấm công là dữ liệu nhân sự — nhân viên mặc định không xem
            d["m1"] = Export; d["m2"] = Export; d["m7"] = Export; d["m8"] = Export;
            d["m3"] = Export; d["m3dm"] = Export; d["m4"] = Export; d["m5"] = Export; d["m9"] = Export; d["m7ck"] = Export; d["m10"] = Export; d["m11"] = Export; d["m12"] = Export; d["m13"] = Export; // nhân viên: nhập liệu được sửa (m6 mặc định Sửa)
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
            foreach (var i in Items) d[i.Key] = custom.TryGetValue(i.Key, out var v) ? v : MissingDefault.GetValueOrDefault(i.Key, None);
        }
        return d;
    }

    public static string Level(User? u, string key) =>
        u?.Role == Roles.Admin ? (Find(key)?.Levels[^1] ?? Edit) : Effective(u).GetValueOrDefault(key, None);

    public static bool Has(User? u, string key, string level = View) => u is not null && Rank(Level(u, key)) >= Rank(level);
}

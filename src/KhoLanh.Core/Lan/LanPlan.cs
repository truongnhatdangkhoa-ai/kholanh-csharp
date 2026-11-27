using KhoLanh.Core.Models;
using System.Text.Json.Nodes;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.Lan;

/// <summary>
/// PHÂN VAI MẠNG LAN THEO TÀI KHOẢN — Quản trị gán vai cho tài khoản, máy tự nhận vai lúc đăng nhập, người dùng không phải cài gì.
/// Lưu trên Google Sheet tài khoản dùng chung, ở một dòng riêng của tab TaiKhoan (username "#mang-lan", vai_tro "mang-lan"):
/// bản Apps Script đang dùng KHÔNG phải dán lại; các bản phần mềm cũ thấy vai_tro lạ nên bỏ qua dòng này.
///  - Vai: tên đăng nhập → "chu" (máy chủ chính) / "duphong" (máy dự phòng); không gán = máy con.
///  - Chu / DuPhong: máy đang giữ vai (mã máy, tên, địa chỉ) — do chính máy đó (hoặc máy chủ) tự ghi.
///  - Key / Port: khóa + cổng LAN của máy chủ, để máy con tự nối mà không cần chép mã LAN.
/// Vai GẮN THEO MÁY: máy đã giữ vai máy chủ / dự phòng thì giữ, ai đăng nhập cũng vậy; chỉ có một máy chủ.
/// </summary>
public sealed class LanPlan
{
    public const string RowName = "#mang-lan", RowRole = "mang-lan";

    public sealed record Slot(string Id, string Name, List<string> Addrs, string At, string User = "");

    public bool On;
    public Dictionary<string, string> Vai = new(StringComparer.OrdinalIgnoreCase);
    public Slot? Chu, DuPhong;
    public string Key = "";
    public int Port;
    /// <summary>Phân vai Đồng bộ Google Drive theo tài khoản (cùng dòng "#mang-lan", khóa "drive").</summary>
    public DrivePlan Drive = new();

    /// <summary>1.106: quyền "Máy dự phòng LAN" (Hệ thống › Tài khoản › phân quyền) — chỉ tính khi được tích RIÊNG (Quản trị không tự có).</summary>

    public string RoleOf(string username) => Vai.TryGetValue(username, out var r) && r is LanService.RoleServer or LanService.RoleStandby ? r : LanService.RoleClient;

    public LanPlan Clone() => Parse(ToJson())!;

    public string ToJson()
    {
        static JsonNode? S(Slot? s) => s is null ? null : new JsonObject
        {
            ["id"] = s.Id, ["ten"] = s.Name, ["dc"] = new JsonArray(s.Addrs.Select(a => (JsonNode)a).ToArray()), ["luc"] = s.At, ["tk"] = s.User,
        };
        return new JsonObject
        {
            ["v"] = 1, ["bat"] = On,
            ["vai"] = new JsonObject(Vai.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
            ["chu"] = S(Chu), ["duphong"] = S(DuPhong), ["k"] = Key, ["cong"] = Port,
            ["drive"] = Drive.ToJson(),
        }.ToJsonString();
    }

    public static LanPlan? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return null;
            static Slot? S(JsonNode? n) => n is JsonObject x && (x["id"]?.ToString() ?? "").Length > 0
                ? new Slot(x["id"]!.ToString(), x["ten"]?.ToString() ?? "", (x["dc"] as JsonArray ?? new()).Select(a => a?.ToString() ?? "").Where(a => a.Length > 0).ToList(), x["luc"]?.ToString() ?? "", x["tk"]?.ToString() ?? "")
                : null;
            var p = new LanPlan
            {
                On = o["bat"]?.GetValue<bool>() == true, Chu = S(o["chu"]), DuPhong = S(o["duphong"]),
                Key = o["k"]?.ToString() ?? "", Port = o["cong"] is JsonValue c && c.TryGetValue<int>(out var port) ? port : 0,
            };
            p.Drive = DrivePlan.From(o["drive"]);
            if (o["vai"] is JsonObject v)
                foreach (var (k, val) in v)
                    if (val?.ToString() is LanService.RoleServer or LanService.RoleStandby) p.Vai[k] = val.ToString();
            return p;
        }
        catch { return null; }
    }

    /// <summary>Kết quả quyết định vai lúc đăng nhập.</summary>
    public sealed record Decision(string Role, bool ClaimServer, bool ClaimStandby, string Why);

    /// <summary>
    /// Vai của máy này khi <paramref name="username"/> đăng nhập (null = kế hoạch chưa bật, giữ cài đặt tay).
    /// Thứ tự: máy đang được ghi nhận làm chủ / dự phòng → giữ; tài khoản được gán vai mà vai đó còn trống → nhận;
    /// máy đã làm máy chủ từ trước (cài tay) mà chưa có máy chủ ghi nhận → giữ và ghi nhận; còn lại → máy con
    /// (chưa có máy chủ nào ghi khóa LAN → máy đơn).
    /// </summary>
    /// <summary>
    /// 1.106 — VAI THEO TÀI KHOẢN (người dùng: "xóa chế độ vai trò theo máy, chỉ áp dụng theo vai trò tài khoản"): luôn bật khi có tài khoản dùng chung,
    /// không cài tay IP / mã LAN. Máy chủ chính = máy mà Quản trị bấm "Bật máy chủ trên máy này" (giữ vai dù ai đăng nhập, Quản trị tắt được);
    /// mọi tài khoản khác (Quản lý, Nhân viên…) = máy con, tự nối máy chủ theo địa chỉ ghi trên Google Sheet. <see cref="On"/> / <see cref="Vai"/> cũ bỏ qua.
    /// </summary>
    public Decision? Decide(string username, string machineId, string currentRole, string machineName = "")
    {
        // 1.131 (người dùng: "bỏ máy dự phòng, chỉ dùng 1 máy chính; máy chính tắt thì máy con tự làm riêng, máy chủ mở lại thì ghép lại"):
        // không còn vai dự phòng — máy từng làm dự phòng thành máy con (phần đã sửa khi nó tạm làm chủ được gửi lên máy chủ để gộp).
        if (Chu?.Id == machineId) return new(LanService.RoleServer, false, false, "máy này là máy chủ chính");
        // 1.144: mã máy đổi (cài đặt bị xóa ngoài app → mã máy sinh lại) nhưng Sheet vẫn ghi máy chủ cùng TÊN máy → vẫn là máy chủ này, đừng thành máy con của chính mình
        if (SameMachineByName(machineName)) return new(LanService.RoleServer, true, false, "máy này trùng tên với máy chủ ghi trên Sheet (mã máy đã đổi)");
        var haveServer = Chu is not null && Key.Length > 0;
        if (currentRole == LanService.RoleServer && Chu is null)
            return new(LanService.RoleServer, true, false, "máy này đang làm máy chủ, chưa có máy chủ ghi nhận");
        if (haveServer || currentRole is LanService.RoleClient) return new(LanService.RoleClient, false, false, haveServer ? $"máy chủ chính là {Chu!.Name}" : "chưa có máy chủ chính");
        return new(LanService.RoleSingle, false, false, "chưa bật máy chủ nào");
    }

    /// <summary>1.144: máy chủ ghi trên Sheet mang mã khác nhưng cùng tên máy (so không phân biệt hoa thường).</summary>
    public bool SameMachineByName(string machineName) =>
        machineName.Length > 0 && Chu is { Name.Length: > 0 } c && string.Equals(c.Name, machineName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sửa kế hoạch cho khớp với máy này: máy chủ chính ghi tên / địa chỉ / khóa / cổng của mình (1.131: bỏ máy dự phòng — ô dự phòng cũ bị xóa).
    /// Không giành vai đã ghi cho máy khác. Trả true nếu có sửa.
    /// </summary>
    public bool Fix(string machineId, string machineName, string role, string user, List<string> addrs, string key, int port)
    {
        var changed = false;
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        bool Same(Slot? s, string id, string name, List<string> a) => s is not null && s.Id == id && s.Name == name && s.Addrs.SequenceEqual(a);
        if (role == LanService.RoleServer && (Chu is null || Chu.Id == machineId || SameMachineByName(machineName)))
        {
            if (!Same(Chu, machineId, machineName, addrs)) { Chu = new Slot(machineId, machineName, addrs, now, Chu?.User is { Length: > 0 } u0 ? u0 : user); changed = true; }
            if (Key != key) { Key = key; changed = true; }
            if (Port != port) { Port = port; changed = true; }
            if (DuPhong is not null) { DuPhong = null; changed = true; }
        }
        return changed;
    }

    /// <summary>Đọc dòng "#mang-lan" trong danh sách tài khoản tải từ Sheet (null nếu không có).</summary>
    internal static LanPlan? FromRows(JsonArray rows)
    {
        foreach (var n in rows)
            if (n is JsonObject o && string.Equals(Cell(o, "username").Trim(), RowName, StringComparison.OrdinalIgnoreCase))
                return Parse(Cell(o, "quyen"));
        return null;
    }

    private static string Cell(JsonObject o, string k) { var v = o[k]?.ToString() ?? ""; return v.StartsWith('\'') ? v[1..] : v; }

    internal JsonObject ToRow() => new()
    {
        ["username"] = RowName,
        ["ho_ten"] = "Phân vai mạng LAN (phần mềm tự ghi, đừng sửa tay)",
        ["vai_tro"] = RowRole,
        ["dang_dung"] = "0",
        ["quyen"] = ToJson(),
        ["mat_khau_bam"] = "", ["muoi"] = "",
        ["cap_nhat_luc"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        ["cap_nhat_boi"] = "",
    };

    public static void CheckRole(string r)
    {
        if (r is not (LanService.RoleServer or LanService.RoleStandby or LanService.RoleClient)) throw new BusinessException("Vai không hợp lệ.");
    }
}

/// <summary>
/// PHÂN VAI ĐỒNG BỘ GOOGLE DRIVE THEO TÀI KHOẢN: tài khoản trong danh sách "máy chính" đăng nhập ở máy nào thì máy đó là MÁY CHÍNH
/// (nếu chưa có máy chính khác đang hoạt động — xem khóa main.lock trong thư mục Drive); các tài khoản khác, và máy đăng nhập sau khi
/// đã có máy chính, là MÁY XEM TỪ XA. Khóa mã hóa (mã đồng bộ) nằm cùng kế hoạch nên máy không phải dán mã.
/// </summary>
public sealed class DrivePlan
{
    public bool On;
    public HashSet<string> Main = new(StringComparer.OrdinalIgnoreCase);
    public string Key = "";
    public bool IsMain(string user) => Main.Contains(user);
    internal JsonNode ToJson() => new JsonObject
    {
        ["bat"] = On, ["chinh"] = new JsonArray(Main.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => (JsonNode)x).ToArray()), ["k"] = Key,
    };
    internal static DrivePlan From(JsonNode? n)
    {
        var d = new DrivePlan();
        if (n is not JsonObject o) return d;
        d.On = o["bat"]?.GetValue<bool>() == true; d.Key = o["k"]?.ToString() ?? "";
        if (o["chinh"] is JsonArray a) foreach (var x in a) if (x?.ToString() is { Length: > 0 } u) d.Main.Add(u);
        return d;
    }
}

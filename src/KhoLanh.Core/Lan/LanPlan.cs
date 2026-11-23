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
    public Decision? Decide(string username, string machineId, string currentRole)
    {
        if (!On) return null;
        if (Chu?.Id == machineId) return new(LanService.RoleServer, false, false, "máy này được ghi nhận là máy chủ chính");
        if (DuPhong?.Id == machineId) return new(LanService.RoleStandby, false, false, "máy này được ghi nhận là máy dự phòng");
        var assigned = RoleOf(username);
        var haveServer = Chu is not null && Key.Length > 0;
        if (assigned == LanService.RoleServer && Chu is null)
            return new(LanService.RoleServer, true, false, $"tài khoản {username} được gán máy chủ, chưa có máy chủ");
        if (assigned == LanService.RoleStandby && DuPhong is null && haveServer)
            return new(LanService.RoleStandby, false, true, $"tài khoản {username} được gán máy dự phòng, chưa có máy dự phòng");
        if (currentRole == LanService.RoleServer && Chu is null)
            return new(LanService.RoleServer, true, false, "máy này đang làm máy chủ (cài tay), chưa có máy chủ ghi nhận");
        if (haveServer || currentRole == LanService.RoleClient) return new(LanService.RoleClient, false, false,
            assigned == LanService.RoleServer ? $"đã có máy chủ là {Chu!.Name}" : assigned == LanService.RoleStandby ? $"đã có máy dự phòng là {DuPhong?.Name}" : "tài khoản không được gán vai");
        return new(LanService.RoleSingle, false, false, "chưa có máy chủ nào");
    }

    /// <summary>
    /// Sửa kế hoạch cho khớp với máy này: máy chủ chính ghi tên / địa chỉ / khóa / cổng của mình và máy dự phòng nó thấy;
    /// máy dự phòng ghi tên / địa chỉ của mình. Không giành vai đã ghi cho máy khác. Máy dự phòng chưa ghi nhận chỉ được ghi
    /// khi người đang đăng nhập trên đó được gán "máy dự phòng" (Quản trị bỏ máy dự phòng thì nó không tự quay lại). Trả true nếu có sửa.
    /// </summary>
    public bool Fix(string machineId, string machineName, string role, string user, List<string> addrs, string key, int port,
        IEnumerable<(string Id, string Name, List<string> Addrs, string User)> standbys)
    {
        if (!On) return false;
        var changed = false;
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        bool Same(Slot? s, string id, string name, List<string> a) => s is not null && s.Id == id && s.Name == name && s.Addrs.SequenceEqual(a);
        if (role == LanService.RoleServer && (Chu is null || Chu.Id == machineId))
        {
            if (!Same(Chu, machineId, machineName, addrs)) { Chu = new Slot(machineId, machineName, addrs, now, Chu?.User is { Length: > 0 } u0 ? u0 : user); changed = true; }
            if (Key != key) { Key = key; changed = true; }
            if (Port != port) { Port = port; changed = true; }
            if (DuPhong?.Id == machineId) { DuPhong = null; changed = true; }
            foreach (var sb in standbys)
            {
                if (sb.Id == machineId || sb.Addrs.Count == 0) continue;
                if (DuPhong is null && RoleOf(sb.User) == LanService.RoleStandby)
                { DuPhong = new Slot(sb.Id, sb.Name, sb.Addrs, now, sb.User); changed = true; }
                else if (DuPhong is not null && DuPhong.Id == sb.Id && !Same(DuPhong, sb.Id, sb.Name, sb.Addrs))
                { DuPhong = DuPhong with { Name = sb.Name, Addrs = sb.Addrs, At = now }; changed = true; }
            }
        }
        else if (role == LanService.RoleStandby && Chu?.Id != machineId)
        {
            if (DuPhong is null && RoleOf(user) == LanService.RoleStandby) { DuPhong = new Slot(machineId, machineName, addrs, now, user); changed = true; }
            else if (DuPhong?.Id == machineId && !Same(DuPhong, machineId, machineName, addrs)) { DuPhong = DuPhong with { Name = machineName, Addrs = addrs, At = now }; changed = true; }
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

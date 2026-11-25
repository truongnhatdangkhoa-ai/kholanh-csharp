using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using KhoLanh.Core.Modules;

namespace KhoLanh.Core.PhoneSync;

/// <summary>
/// Tài khoản điện thoại dùng chung tài khoản PC (1.76): CÙNG tên đăng nhập + mật khẩu, nhưng QUYỀN ĐIỆN THOẠI RIÊNG
/// (bật / tắt, vai trò Chỉ xem · Tạo phiếu · Quản lý · Quản trị, kho được dùng) — không suy từ quyền PC.
/// <list type="bullet">
/// <item>Quyền điện thoại lưu ở db <see cref="Db"/> sheet <see cref="Sheet"/> (qua LAN / Drive: mọi máy PC thấy chung).</item>
/// <item>App điện thoại (Code.gs <c>resolveUser_</c>) so <c>sha256(mật khẩu)</c> với cột <c>tokenHash</c> tab PhanQuyen. PC chỉ cất PBKDF2 nên
///   KHÔNG tính lại được từ mật khẩu đã lưu → ghi mã SHA-256 lúc người đó đăng nhập PC / đổi mật khẩu / được Quản trị đặt mật khẩu.
///   Ai chưa đăng nhập PC từ bản này thì chưa vào được điện thoại (giao diện báo "cần đăng nhập PC 1 lần").</item>
/// <item>Đẩy lên tab PhanQuyen (Code.gs tự sao lưu bản cũ trước khi ghi đè): dòng của tài khoản PC do PC quyết định;
///   dòng tạo riêng trên điện thoại (tên không trùng tài khoản PC) giữ nguyên.</item>
/// </list>
/// </summary>
public sealed class PhoneAccounts
{
    public const string Db = "dien-thoai", Sheet = "TaiKhoanDT";
    /// <summary>Cột tab PhanQuyen — PHẢI khớp <c>PQ_COLS</c> trong Code.gs của app điện thoại mới.</summary>
    public static readonly string[] PqCols = { "tokenHash", "ten", "tenHienThi", "quyen", "kho", "trangThai", "capNhat", "maTruyCap", "pcModules", "email" };
    public const string PqTab = "PhanQuyen";

    /// <summary>Vai trò điện thoại (giá trị cột quyen) — giống Code.gs.</summary>
    public static readonly (string Key, string Label)[] Roles =
    {
        ("xem", "Chỉ xem (cấp 1)"), ("xuat", "Tạo phiếu (cấp 2)"), ("quanly", "Quản lý"), ("quantri", "Quản trị"),
    };
    /// <summary>"Kho" (chức năng) trên điện thoại — mã giống <c>ALL_KHO</c> trong Code.gs.</summary>
    public static readonly (string Key, string Label)[] Kho =
    {
        ("M01", "Tồn kho An An"), ("M01VT", "Tồn theo vị trí"), ("M01VTB", "Vị Trí Bột"), ("M02", "Tồn kho gửi"), ("M03", "NXT Bột/Sốt"),
        ("M08", "Tổng hợp · Kháng sinh"), ("BC01", "Báo cáo An An"), ("BC02", "Báo cáo kho gửi"), ("LX", "Lịch xuất"),
        ("C3D", "Container 3D"), ("SCAN", "Scan tài liệu"), ("QR", "Tạo mã QR"),
    };

    public sealed class Entry
    {
        public string Ten = "";          // tên đăng nhập (chữ thường)
        public bool Bat;                 // dùng trên điện thoại
        public string VaiTro = "xem";
        public List<string> Kho = new();
        public string Hash = "";         // sha256 hex của mật khẩu (rỗng = chưa có — cần đăng nhập PC 1 lần)
        public string HashLuc = "";      // ISO: lần ghi mã gần nhất

        public JsonObject ToJson() => new()
        {
            ["ten"] = Ten, ["bat"] = Bat ? "1" : "0", ["vaiTro"] = VaiTro, ["kho"] = string.Join(",", Kho), ["hash"] = Hash, ["hashLuc"] = HashLuc,
        };
        public static Entry From(JsonObject o) => new()
        {
            Ten = Str(o["ten"]).ToLowerInvariant(), Bat = Str(o["bat"]) == "1", VaiTro = NormRole(Str(o["vaiTro"])),
            Kho = NormKho(Str(o["kho"]).Split(',')), Hash = Str(o["hash"]).ToLowerInvariant(), HashLuc = Str(o["hashLuc"]),
        };
    }

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    public PhoneAccounts(Func<ISheetSource> src, Session session) { _src = src; _session = session; }

    private static string Str(JsonNode? n) => n is null ? "" : (n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n.ToJsonString().Trim('"')).Trim();
    public static string NormRole(string r) => Roles.Any(x => x.Key == r) ? r : "xem";
    public static List<string> NormKho(IEnumerable<string> k) => k.Select(x => x.Trim()).Where(x => Kho.Any(y => y.Key == x)).Distinct().ToList();

    /// <summary>sha256 hex chữ thường của chuỗi UTF-8 — GIỐNG <c>sha256Hex_</c> trong Code.gs.</summary>
    public static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""))).ToLowerInvariant();

    public List<Entry> List()
    {
        try
        {
            var arr = JsonNode.Parse(_src().Read(Db, Sheet)) as JsonArray ?? new JsonArray();
            return arr.OfType<JsonObject>().Select(Entry.From).Where(e => e.Ten.Length > 0).GroupBy(e => e.Ten).Select(g => g.Last()).ToList();
        }
        catch { return new(); }
    }

    private void Save(List<Entry> list) =>
        _src().Write(Db, new Dictionary<string, JsonArray?> { [Sheet] = new JsonArray(list.OrderBy(e => e.Ten, StringComparer.Ordinal).Select(e => (JsonNode)e.ToJson()).ToArray()) });

    /// <summary>Ghi mã điện thoại khi biết mật khẩu thật (đăng nhập PC thành công / đổi / đặt mật khẩu). Không đổi gì nếu mã đã đúng.</summary>
    public void RememberPassword(string username, string password)
    {
        var ten = (username ?? "").Trim().ToLowerInvariant();
        if (ten.Length == 0 || string.IsNullOrEmpty(password)) return;
        var h = Sha256Hex(password);
        var list = List();
        var e = list.FirstOrDefault(x => x.Ten == ten);
        if (e is not null && e.Hash == h) return;
        if (e is null) { e = new Entry { Ten = ten }; list.Add(e); }
        e.Hash = h; e.HashLuc = Now().ToString("yyyy-MM-ddTHH:mm:ss");
        Save(list);
    }

    /// <summary>Quản trị lưu quyền điện thoại (bật / vai trò / kho) cho các tài khoản — mã mật khẩu giữ nguyên.</summary>
    public void SaveRights(IEnumerable<Entry> rights)
    {
        _session.RequireAdmin();
        var list = List();
        foreach (var r in rights)
        {
            var ten = r.Ten.Trim().ToLowerInvariant(); if (ten.Length == 0) continue;
            var e = list.FirstOrDefault(x => x.Ten == ten);
            if (e is null) { e = new Entry { Ten = ten }; list.Add(e); }
            e.Bat = r.Bat; e.VaiTro = NormRole(r.VaiTro); e.Kho = NormKho(r.Kho);
            if (e.Bat && e.VaiTro == "quantri" && e.Kho.Count == 0) e.Kho = Kho.Select(k => k.Key).ToList();   // như Code.gs: Quản trị chưa chọn kho = đủ kho
        }
        Save(list);
    }

    public sealed record PlanRow(string Ten, string HienThi, bool FromPc, string Note);
    public sealed record Plan(List<Dictionary<string, JsonNode?>> Rows, List<PlanRow> Info, List<string> Warnings);

    /// <summary>
    /// Dựng nội dung tab PhanQuyen mới từ tab hiện có (<paramref name="current"/>) + tài khoản PC + quyền điện thoại.
    /// Tài khoản PC: dòng do PC quyết định (đang tắt điện thoại / bị khóa ở PC → "Tắt"); dòng chỉ có trên điện thoại giữ nguyên.
    /// Cảnh báo: bật mà chưa có mã (cần đăng nhập PC 1 lần), 2 tài khoản trùng mật khẩu (app điện thoại chỉ nhận được 1 người).
    /// </summary>
    public static Plan BuildPlan(IReadOnlyList<IDictionary<string, JsonNode?>> current, IReadOnlyList<User> users, IReadOnlyList<Entry> rights, DateTime now)
    {
        var rows = new List<Dictionary<string, JsonNode?>>(); var info = new List<PlanRow>(); var warn = new List<string>();
        var byTen = users.GroupBy(u => u.Username.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var rightOf = rights.ToDictionary(r => r.Ten, r => r);
        var cur = current.Select(r => PqCols.ToDictionary(c => c, c => (JsonNode?)JsonValue.Create(Str(r.TryGetValue(c, out var v) ? v : null)))).ToList();
        string T(Dictionary<string, JsonNode?> r, string c) => Str(r.GetValueOrDefault(c));
        var nowS = now.ToString("yyyy-MM-ddTHH:mm:ss");
        var done = new HashSet<string>();
        // 1) giữ thứ tự cũ: dòng trùng tài khoản PC → PC ghi lại; dòng riêng điện thoại → giữ nguyên
        foreach (var r in cur)
        {
            var ten = T(r, "ten").ToLowerInvariant();
            if (ten.Length == 0 || done.Contains(ten)) continue;
            if (byTen.TryGetValue(ten, out var u)) { done.Add(ten); var x = PcRow(u, rightOf.GetValueOrDefault(ten), r, nowS, info, warn); if (x is not null) rows.Add(x); }
            else { done.Add(ten); rows.Add(r); info.Add(new PlanRow(T(r, "ten"), T(r, "tenHienThi"), false, "tạo trên điện thoại — giữ nguyên")); }
        }
        // 2) tài khoản PC mới bật điện thoại
        foreach (var (ten, u) in byTen.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (done.Contains(ten)) continue;
            var x = PcRow(u, rightOf.GetValueOrDefault(ten), null, nowS, info, warn); if (x is not null) rows.Add(x);
        }
        // 3) trùng mật khẩu giữa các dòng đang bật — Code.gs dừng ở dòng đầu cùng mã băm nên người sau không vào được
        foreach (var g in rows.Where(r => T(r, "trangThai") == "Bật" && T(r, "tokenHash").Length > 0).GroupBy(r => T(r, "tokenHash").ToLowerInvariant()).Where(g => g.Count() > 1))
            warn.Add("Trùng mật khẩu: " + string.Join(", ", g.Select(r => T(r, "ten"))) + " — điện thoại chỉ nhận người đầu; đổi mật khẩu cho khác nhau.");
        return new Plan(rows, info, warn);
    }

    private static Dictionary<string, JsonNode?>? PcRow(User u, Entry? e, Dictionary<string, JsonNode?>? old, string nowS, List<PlanRow> info, List<string> warn)
    {
        var ten = u.Username.Trim();
        var on = e is { Bat: true } && u.IsActive;
        string O(string c) => old is null ? "" : Str(old.GetValueOrDefault(c));
        if (!on && old is null) return null;   // chưa từng lên điện thoại và không bật → không tạo dòng
        var hash = e?.Hash ?? "";
        if (on && hash.Length == 0) { hash = O("tokenHash"); warn.Add($"{ten}: chưa có mật khẩu điện thoại — người này cần đăng nhập PC 1 lần (hoặc Quản trị đặt lại mật khẩu)."); }
        var vt = on ? e!.VaiTro : (O("quyen").Length > 0 ? O("quyen") : "xem");
        var kho = on ? string.Join(",", e!.Kho) : O("kho");
        info.Add(new PlanRow(ten, u.FullName, true, on ? (hash.Length > 0 ? "dùng mật khẩu PC" : "chưa có mật khẩu") : (u.IsActive ? "đã tắt điện thoại" : "tài khoản PC bị khóa")));
        var row = new Dictionary<string, JsonNode?>
        {
            ["tokenHash"] = hash, ["ten"] = ten, ["tenHienThi"] = u.FullName.Trim().Length > 0 ? u.FullName.Trim() : ten, ["quyen"] = vt, ["kho"] = kho,
            ["trangThai"] = on ? "Bật" : "Tắt", ["capNhat"] = nowS,
            ["maTruyCap"] = "",                 // mật khẩu PC — không lưu chữ lên Sheet
            ["pcModules"] = O("pcModules"),      // module PC hiện trên điện thoại: giữ như cũ (phân quyền PC làm ở PC)
            ["email"] = (u.GoogleEmail ?? "").Trim().Length > 0 ? u.GoogleEmail!.Trim().ToLowerInvariant() : O("email"),
        };
        if (old is not null && PqCols.Where(c => c != "capNhat").All(c => Str(row[c]) == O(c))) row["capNhat"] = O("capNhat");   // không đổi → giữ mốc cũ
        return row;
    }

    /// <summary>Nội dung 2 tab PhanQuyen như nhau (bỏ qua cột phụ / số dòng) → không cần ghi lại.</summary>
    public static bool SameTable(IReadOnlyList<IDictionary<string, JsonNode?>> a, IReadOnlyList<IDictionary<string, JsonNode?>> b) =>
        a.Count == b.Count && a.Zip(b).All(p => PqCols.All(c => Str(p.First.TryGetValue(c, out var x) ? x : null) == Str(p.Second.TryGetValue(c, out var y) ? y : null)));
}

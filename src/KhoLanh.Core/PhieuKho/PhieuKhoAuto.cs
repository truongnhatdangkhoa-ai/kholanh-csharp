using System.Text.Json;
using System.Text.Json.Nodes;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.PhieuKho;

/// <summary>
/// List / Phiếu TỰ ĐỘNG tạo từ Lịch xuất (chép phieu-nhap-xuat-ui.js › PC 8.28 / 2.90 của app cũ). Nhật ký dùng chung sheet <c>PNX_AutoLog</c>:
/// mỗi (LSX + loại phiếu) chỉ tự tạo 1 lần (khóa <c>lsxNorm|loaiPhieu</c>, List dùng loại <c>__list</c>) — xóa lịch, xóa phiếu rồi thêm lại lịch cũng không tạo trùng;
/// xóa HẾT List + Phiếu của LSX thì quên nhật ký (thêm lịch lại sẽ được tạo lại). Mục do máy tạo mang nhãn <c>tuDong</c> và cờ "chưa xem" tới khi người dùng mở.
/// </summary>
public sealed partial class PhieuKhoService
{
    public const string SheetAutoLog = "PNX_AutoLog", LoaiList = "__list";
    public static readonly string[] LogCols = { "key", "lsx", "loaiPhieu", "loaiLich", "phieuId", "listKey", "listNew", "phieuNew", "listSeen", "phieuSeen", "lichId", "ngay", "tao", "ghiChu" };

    /// <summary>1 dòng nhật ký (giữ đúng tên cột của app cũ).</summary>
    public sealed class AutoLog
    {
        public Dictionary<string, string> V = LogCols.ToDictionary(c => c, _ => "");
        public string this[string k] { get => V.TryGetValue(k, out var s) ? s : ""; set => V[k] = value ?? ""; }
        public JsonObject ToJson() { var o = new JsonObject(); foreach (var c in LogCols) o[c] = this[c]; return o; }
    }

    /// <summary>Nhãn tuDong của List / Phiếu: { at, ngayLich, loaiLich, lichIds }.</summary>
    public sealed record AutoTag(string At, string NgayLich, string LoaiLich, List<string> LichIds);

    public static string LogKey(string code, string loaiPhieu) => PhieuKhoCore.LsxNorm(code) + "|" + loaiPhieu;
    public static string ListKey(string? v) => PhieuKhoCore.LsxNorm(v);

    public List<AutoLog> AutoLogs { get; private set; } = new();

    public List<AutoLog> LoadAutoLog()
    {
        JsonArray a;
        try { a = JsonNode.Parse(_src().Read(Db, SheetAutoLog)) as JsonArray ?? new JsonArray(); } catch { a = new JsonArray(); }
        var old = AutoLogs.ToDictionary(l => l["key"] + "|" + l["phieuId"] + "|" + l["tao"], l => l, StringComparer.Ordinal);
        AutoLogs = a.OfType<JsonObject>().Select(o =>
        {
            var l = new AutoLog(); foreach (var c in LogCols) l[c] = o[c] is JsonValue v ? v.ToString() : "";
            if (old.TryGetValue(l["key"] + "|" + l["phieuId"] + "|" + l["tao"], out var m)) { if (m["listSeen"].Length > 0) l["listSeen"] = "1"; if (m["phieuSeen"].Length > 0) l["phieuSeen"] = "1"; }
            return l;
        }).Where(l => l["key"].Length > 0).ToList();
        return AutoLogs;
    }

    public void SaveAutoLog()
    {
        var keep = AutoLogs.Count > 2000 ? AutoLogs.Skip(AutoLogs.Count - 2000).ToList() : AutoLogs;
        AutoLogs = keep;
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetAutoLog] = new JsonArray(keep.Select(l => (JsonNode)l.ToJson()).ToArray()) });
    }

    public void LogAdd(string code, string loaiPhieu, AutoTag g, string phieuId = "", bool listNew = false, bool phieuNew = false, string ghiChu = "")
    {
        var l = new AutoLog();
        l["key"] = LogKey(code, loaiPhieu); l["lsx"] = code; l["loaiPhieu"] = loaiPhieu; l["loaiLich"] = g.LoaiLich; l["phieuId"] = phieuId; l["listKey"] = ListKey(code);
        l["listNew"] = listNew ? "1" : ""; l["phieuNew"] = phieuNew ? "1" : ""; l["lichId"] = string.Join(",", g.LichIds); l["ngay"] = g.NgayLich;
        l["tao"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"); l["ghiChu"] = ghiChu.Length > 200 ? ghiChu[..200] : ghiChu;
        AutoLogs.Add(l);
    }

    /// <summary>Đã ghi nhật ký tự tạo cho (LSX + loại phiếu) — cùng ngày lịch (dòng cũ không có ngày vẫn chặn, PC 5.11).</summary>
    public bool Logged(string code, string loaiPhieu, string ngay = "")
    {
        var k = LogKey(code, loaiPhieu); var d = IsoD(ngay);
        return AutoLogs.Any(l => l["key"] == k && (loaiPhieu == LoaiList || IsoD(l["ngay"]).Length == 0 || d.Length == 0 || IsoD(l["ngay"]) == d));
    }
    private static string IsoD(string v) => v.Length >= 10 && char.IsDigit(v[0]) && v[4] == '-' && v[7] == '-' ? v[..10] : "";

    // ---- nhãn tuDong ----
    private static readonly JsonSerializerOptions TagJs = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static JsonElement TagEl(AutoTag t) => JsonSerializer.SerializeToElement(new { at = t.At, ngayLich = t.NgayLich, loaiLich = t.LoaiLich, lichIds = t.LichIds }, TagJs);
    private static AutoTag? TagOf(Dictionary<string, JsonElement>? ex)
    {
        if (ex is null || !ex.TryGetValue("tuDong", out var e) || e.ValueKind != JsonValueKind.Object) return null;
        string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var ids = e.TryGetProperty("lichIds", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.ToString()).ToList() : new List<string>();
        return new AutoTag(S("at"), S("ngayLich"), S("loaiLich"), ids);
    }
    public static void SetTag(ListData d, AutoTag t) { d.Extra ??= new(); d.Extra["tuDong"] = TagEl(t); }
    public static void SetTag(Phieu p, AutoTag t) { p.Extra ??= new(); p.Extra["tuDong"] = TagEl(t); }
    public static AutoTag? TagOf(ListData d) => TagOf(d.Extra);
    public static AutoTag? TagOf(Phieu p) => TagOf(p.Extra);
    private static bool XeTag(Phieu p) => p.Extra is { } ex && ex.TryGetValue("tuDong", out var e) && e.ValueKind == JsonValueKind.Object && e.TryGetProperty("xe", out var x) && x.ValueKind is not (JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined);

    // ---- chưa xem ----
    public bool IsNew(Phieu p) => TagOf(p) is not null && AutoLogs.Any(l => l["phieuId"] == p.Id && l["phieuNew"] == "1" && l["phieuSeen"].Length == 0);
    public bool IsNew(ListRec x) { var k = ListKey(x.Lsx); return TagOf(x.Data) is not null && AutoLogs.Any(l => l["listKey"] == k && l["listNew"] == "1" && l["listSeen"].Length == 0); }
    public (int Phieu, int List) Unseen() => (List.Count(IsNew), Lists.Count(IsNew));

    /// <summary>Đánh dấu đã xem: kind 'p' = phiếu (id), 'l' = List (khóa LSX), null = tất cả. Ghi nhật ký ngay.</summary>
    public bool MarkSeen(char? kind = null, string id = "")
    {
        var hit = false;
        foreach (var l in AutoLogs)
        {
            if (kind is null or 'p' && (kind is null || l["phieuId"] == id) && l["phieuNew"] == "1" && l["phieuSeen"].Length == 0) { l["phieuSeen"] = "1"; hit = true; }
            if (kind is null or 'l' && (kind is null || l["listKey"] == id) && l["listNew"] == "1" && l["listSeen"].Length == 0) { l["listSeen"] = "1"; hit = true; }
        }
        if (hit) SaveAutoLog();
        return hit;
    }

    public sealed record AutoItem(char Kind, string Id, string Ten, string Loai, string Lsx, string Khach, string NgayLich, string At, bool IsNew, string Note);

    /// <summary>Danh sách tab "⚡ Tự động đã tạo" (mới nhất trước).</summary>
    public List<AutoItem> AutoItems()
    {
        var it = new List<AutoItem>();
        foreach (var p in List)
        {
            if (TagOf(p) is not { } t) continue;
            var notes = new List<string>();
            if (p.XuatLuc.Length > 0) notes.Add("đã xuất Excel");
            if (XeTag(p)) notes.Add("có thông tin xe");
            it.Add(new('p', p.Id, p.SoPhieu.Length > 0 ? p.SoPhieu : "(chưa có số)", PhieuKhoCore.LoaiTen(p.Loai), p.Lsx, p.Khach, t.NgayLich, t.At.Length > 0 ? t.At : p.CapNhat, IsNew(p), string.Join(" · ", notes)));
        }
        foreach (var x in Lists)
        {
            if (TagOf(x.Data) is not { } t) continue;
            it.Add(new('l', ListKey(x.Lsx), "LSX " + x.Lsx, "List sản xuất", x.Lsx, x.Khach, t.NgayLich, t.At.Length > 0 ? t.At : x.CapNhat, IsNew(x),
                x.Data.Rows.Count == 0 ? "List trống — bạn tự cập nhật" : "lấy từ tồn kho"));
        }
        return it.OrderByDescending(i => i.At, StringComparer.Ordinal).ToList();
    }

    /// <summary>Lưu List TRỐNG đặt tên theo LSX (khi tồn kho chưa có hàng của LSX) — người dùng tự cập nhật sau.</summary>
    public ListRec SaveEmptyList(string code, string khach, AutoTag tag)
    {
        var d = new ListData { Ok = true, Lsx = code, Khach = khach };
        SetTag(d, tag);
        d.Extra!["nguon"] = JsonSerializer.SerializeToElement("tay");
        d.Extra["at"] = JsonSerializer.SerializeToElement(tag.At);
        return SaveListData(d, "(tự tạo từ lịch xuất)");
    }

    /// <summary>Xóa hết Phiếu + List của LSX → quên nhật ký để thêm lịch lại thì được tự tạo lại (PC 8.29 A).</summary>
    private void ForgetLog(IEnumerable<string> codes)
    {
        try
        {
            LoadAutoLog(); var hit = false;
            foreach (var c in codes)
            {
                if (FindList(c) is not null || List.Any(p => PhieuKhoCore.LsxList(p.Lsx).Any(x => PhieuKhoCore.LsxNorm(x) == PhieuKhoCore.LsxNorm(c)))) continue;
                var k = ListKey(c); if (AutoLogs.RemoveAll(l => l["listKey"] == k) > 0) hit = true;
            }
            if (hit) SaveAutoLog();
        }
        catch { /* bỏ qua: lần sau xóa lại */ }
    }
}

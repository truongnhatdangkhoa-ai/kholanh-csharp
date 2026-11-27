using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.SoKho;

namespace KhoLanh.Core.PhoneSync;

/// <summary>1 phiếu xuất do điện thoại tạo (tab PhieuXuat + PhieuXuatItems).</summary>
public sealed record PhoneItem(string MaHang, string TenHang, string PhieuNhap, string SoLo, string Size, double SoKien, double SoLuongXuat, string ItemKey, JsonNode? RowIndex, string HopDong, string ViTri);
public sealed record PhoneSlip(string Id, string MaPhieu, string NgayTao, string NguoiTao, string GhiChu, string Module, string LsxXuat, List<PhoneItem> Items);
/// <summary>Kết quả nhập phiếu vào 1 module: phiếu đã nhận (kể cả đã có từ trước) + số phiếu mới + lỗi (phiếu để lại "Chưa xử lý").</summary>
public sealed record ApplyResult(List<string> Imported, int NewCount, List<string> Errors);

/// <summary>
/// ĐỒNG BỘ ĐIỆN THOẠI (Google Sheet qua Apps Script Web App) cho MỌI module — chép main.js của app cũ (pushTonKhoLenSheet / pullPhieuXuatVeLocal +
/// applyPhonePhieu…DirectToExcel + core/phieu-pc-push.js + core/vt-push-extra.js + so-kho-core.js › pushRows / applyPhonePhieu), cùng tên tab, cột, khóa dòng
/// và Meta nên app điện thoại hiện có (và bản mobile sau này) dùng được ngay. Đọc / ghi THẲNG các sheet dùng chung (cùng định dạng app cũ) qua
/// <see cref="ISheetSource"/>; sau khi nhận phiếu, các service đang nạp được đọc lại. Giao thức mô tả ở docs/DONG-BO-DIEN-THOAI.md.
/// </summary>
public sealed partial class PhoneSyncService
{
    // ---------------------------------------------------------------- tab (SH_TAB của app cũ)
    public static class Tab
    {
        public const string M01 = "TonKho_M01", M01VT = "TonKho_M01_ViTri", M01VTB = "TonKho_M01_ViTriBot", M02 = "TonKho_M02", M03 = "TonKho_M03", M03Lsx = "DanhMuc_M03_LSX";
        public const string M08MaHoa = "M08_MaHoa", M08MaDaTao = "M08_MaDaTao", M08TongHop = "M08_TongHop", M08Ks = "M08_KhangSinh", M08AnhXa = "M08_AnhXa";
        public const string Lx = "LichXuat_Cont", LxThang = "LichXuat_Thang", C3d = "C3D_Data", Meta = "Meta", Px = "PhieuXuat", Pxi = "PhieuXuatItems", PxPc = "PhieuXuat_PC", PxPci = "PhieuXuatItems_PC";
    }
    /// <summary>Bảng đẩy lên điện thoại (mã dùng chung với cột "module" của phiếu điện thoại).</summary>
    public static readonly (string Code, string Name, string Perm)[] Tables =
    {
        ("M01", "Tồn kho An An", "m1"), ("M01VT", "Tồn theo vị trí", "m7"), ("M01VTB", "Vị Trí Bột", "m1vtb"), ("M02", "Tồn kho gửi", "m2"),
        ("M03", "NXT Bột/Sốt", "m3sk"), ("M08", "Tổng hợp (mã hóa, danh sách mã, ánh xạ, kháng sinh)", "m3"), ("LX", "Lịch xuất nhập hàng", "lx"), ("C3D", "Container 3D", "m14"),
    };
    public static readonly string[] SyncTables = Tables.Select(t => t.Code).ToArray();
    public const string DaXuLy = "Đã xử lý", LoiThieuDong = "Lỗi - không có dòng hàng";
    public const string KeyPick = "phone_sync_pick", KeyStatus = "phone_sync_status";

    private readonly AppServices _s;
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;
    private readonly PhoneWebApp? _w;
    public PhoneSyncService(AppServices s, PhoneWebApp? web = null) { _s = s; _w = web; }
    private ISheetSource Src => _s.Lan;
    private PhoneWebApp W => _w ?? _s.Phone;

    // ---------------------------------------------------------------- tiện ích đọc / ghi sheet thô
    private JsonArray Read(string db, string sheet) { try { return JsonNode.Parse(Src.Read(db, sheet)) as JsonArray ?? new(); } catch { return new(); } }
    private List<JsonObject> Rows(string db, string sheet) => Read(db, sheet).OfType<JsonObject>().ToList();
    private JsonObject? JsonCell(string db, string sheet)
    {
        var a = Read(db, sheet); var c = a.FirstOrDefault()?["data"] is JsonValue v && v.TryGetValue<string>(out var t) ? t : "";
        if (!c.StartsWith("__JSON__", StringComparison.Ordinal)) return null;
        try { return JsonNode.Parse(c[8..]) as JsonObject; } catch { return null; }
    }
    private static JsonArray Cell(JsonObject o) => new(new JsonObject { ["data"] = "__JSON__" + o.ToJsonString(J) });
    private static readonly JsonSerializerOptions J = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string S(JsonNode? n) => n is null ? "" : n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : n is JsonValue v2 && v2.TryGetValue<double>(out var d) ? JsNum(d) : n.ToJsonString().Trim('"').Trim();
    public static string JsNum(double d) => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);
    private static double Loose(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : TonKho.JsText.LooseNum(S(n));
    private static double R4(double x) => Math.Round(x * 10000, MidpointRounding.AwayFromZero) / 10000;
    private static double R2(double x) => Math.Round(x * 100, MidpointRounding.AwayFromZero) / 100;
    private string NowIso() => Now().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static string MsIso(double ms) => ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) : "";
    /// <summary>m1FuzzyNorm: bỏ dấu, đ → d, thường, dấu câu → khoảng trắng.</summary>
    public static string Fuzzy(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD); var sb = new StringBuilder();
        foreach (var ch in d) if (ch < '̀' || ch > 'ͯ') sb.Append(ch);
        var t = sb.ToString().Replace('đ', 'd').Replace('Đ', 'd').ToLowerInvariant();
        return Regex.Replace(Regex.Replace(t, @"[.,()/\\\-_:;]", " "), @"\s+", " ").Trim();
    }
    /// <summary>encodeURIComponent (giữ A-Z a-z 0-9 - _ . ! ~ * ' ( )).</summary>
    public static string EncodeUri(string s)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            var c = (char)b;
            if (b < 128 && (char.IsAsciiLetterOrDigit(c) || "-_.!~*'()".Contains(c))) sb.Append(c); else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- cấu hình chọn bảng + trạng thái
    public HashSet<string> Pick
    {
        get { var v = _s.Settings.Get(KeyPick, ""); return v.Length == 0 ? new() : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(SyncTables.Contains).ToHashSet(); }
        set => _s.Settings.Set(KeyPick, string.Join(",", SyncTables.Where(value.Contains)));
    }
    public sealed record TableStatus(string At, bool Changed, string ChangedAt, string Error, string Sig, int Rows);
    public Dictionary<string, TableStatus> Status
    {
        get { try { return JsonSerializer.Deserialize<Dictionary<string, TableStatus>>(_s.Settings.Get(KeyStatus, "{}")) ?? new(); } catch { return new(); } }
        private set => _s.Settings.Set(KeyStatus, JsonSerializer.Serialize(value));
    }
    /// <summary>Bảng tài khoản này được đẩy (có quyền xem module đó).</summary>
    public bool CanTable(string code) { var p = Tables.First(t => t.Code == code).Perm; return _s.Session.Can(p, Perm.View) || (code == "M01VTB" && _s.Session.Can(SoKhoService.PermKey, Perm.View)); }
    private static string Sig(object o) { var t = JsonSerializer.Serialize(o, J); uint h = 2166136261; foreach (var ch in t) { h ^= ch; h *= 16777619; } return h.ToString("x") + ":" + t.Length; }

    // ================================================================ ĐẨY (push)
    public sealed record Built(string Tab, List<string> Columns, List<Dictionary<string, JsonNode?>> Rows);
    public sealed record PushResult(List<string> Tables, Dictionary<string, int> Rows, List<string> Errors);

    /// <summary>Dựng dữ liệu 1 bảng (tab + dòng) và ghi Meta — không gọi mạng (test được).</summary>
    public List<Built> Build(string code, Dictionary<string, JsonNode?> meta)
    {
        var now = NowIso(); var outp = new List<Built>();
        switch (code)
        {
            case "M01": outp.Add(BuildM01(meta)); meta["lastPushAt_M01"] = now; break;
            case "M01VT": outp.Add(BuildM01Vt(meta)); meta["lastPushAt_M01VT"] = now; break;
            case "M01VTB":
            {
                var v = _s.Vtb; v.Load(); var rows = v.PushRows();
                outp.Add(new Built(Tab.M01VTB, rows.Count > 0 ? VtbService.PushColumns.ToList() : new(), rows));
                meta["lastPushAt_M01VTB"] = now; meta["m01ViTriBotRows"] = rows.Count; meta["m01ViTriBotDataDate"] = v.DataDate; meta["m01ViTriBotImportedAt"] = MsIso(v.LastLoadAt);
                break;
            }
            case "M02": outp.Add(BuildM02(meta)); meta["lastPushAt_M02"] = now; break;
            case "M03": outp.AddRange(BuildM03(meta)); meta["lastPushAt_M03"] = now; break;
            case "M08": outp.AddRange(BuildM08(meta)); meta["lastPushAt_M08"] = now; break;
            case "LX": outp.AddRange(BuildLx(meta)); meta["lastPushAt_LX"] = now; break;
            case "C3D": outp.Add(BuildC3d(meta)); meta["lastPushAt_C3D"] = now; break;
        }
        return outp;
    }

    // ---- M01 · Load Data (cột thật từ LoadDataMeta; "_key" = id:<Item ID> | r:<thứ tự>)
    public const string Tpc = "TPC-";
    private Built BuildM01(Dictionary<string, JsonNode?> meta)
    {
        var m = JsonCell("ton-kho-an-an", "LoadDataMeta") ?? new();
        var headers = (m["headers"] as JsonArray ?? new()).Select(h => S(h)).ToList();
        var used = new HashSet<string>(); var cols = new List<string>();
        for (var i = 0; i < headers.Count; i++)
        {
            var name = headers[i].Length > 0 ? headers[i] : $"Cột {i + 1}";
            if (used.Contains(name)) { var n = 2; while (used.Contains($"{name} ({n})")) n++; name = $"{name} ({n})"; }
            used.Add(name); cols.Add(name);
        }
        var itemIdIdx = headers.FindIndex(h => Fuzzy(h) == "item id"); var maIdx = headers.FindIndex(h => Fuzzy(h) == "ma hang");
        var rows = Rows("ton-kho-an-an", "LoadData").Select((o, i) =>
        {
            var r = new Dictionary<string, JsonNode?>();
            for (var j = 0; j < cols.Count; j++)
            {
                var v = o["c" + j]?.DeepClone() ?? JsonValue.Create("");
                if (j == maIdx && v is JsonValue sv && sv.TryGetValue<string>(out var ms) && Regex.IsMatch(ms, @"^\s*TPC-", RegexOptions.IgnoreCase)) v = Regex.Replace(ms, @"^\s*TPC-", "", RegexOptions.IgnoreCase);
                r[cols[j]] = v;
            }
            var itemId = itemIdIdx >= 0 ? S(o["c" + itemIdIdx]) : "";
            r["_key"] = itemId.Length > 0 ? "id:" + itemId : "r:" + i; r["_row"] = i;
            return r;
        }).ToList();
        meta["m01FileName"] = S(m["fileName"]); meta["m01Rows"] = rows.Count; meta["m01ImportedAt"] = m["importedAt"] is { } ia && Loose(ia) > 0 ? MsIso(Loose(ia)) : ""; meta["m01DataDate"] = S(m["dataDate"]);
        return new Built(Tab.M01, cols.Concat(new[] { "_key", "_row" }).ToList(), rows);
    }

    // ---- M01VT · Tồn theo vị trí (khóa Mã|Lô|Vị trí#n + cột phụ Đã xuất / Xuất hôm nay / Nhóm đặc tính)
    private static readonly (string F, string Col)[] VtLbl = { ("ma", "Mã hàng"), ("ten", "Tên hàng"), ("dvt", "ĐVT"), ("lo", "Lô (Batch)"), ("vt", "Vị trí"), ("sl", "SL Tồn"), ("tl", "T.Lượng") };
    public static List<string> VtKeys(IEnumerable<JsonObject> rows)
    {
        var seen = new Dictionary<string, int>();
        return rows.Select(r =>
        {
            var ma = S(r["Mã hàng"]); if (ma.Length == 0) return "";
            var b = string.Join("|", ma, S(r["Lô (Batch)"]), S(r["Vị trí"])); var n = seen.GetValueOrDefault(b); seen[b] = n + 1; return b + "#" + n;
        }).ToList();
    }
    private static bool SlipActive(JsonObject sl, double cur)
    {
        double b;
        if (sl["loadAt"] is { } la && la is not null && !(la is JsonValue lv && lv.TryGetValue<string>(out _) )) b = Loose(la);
        else if (cur == 0) b = 0;
        else { var ok = DateTime.TryParse(S(sl["createdAt"]), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t); var tt = ok ? (t - DateTime.UnixEpoch).TotalMilliseconds : double.NaN; b = S(sl["phoneId"]).Length > 0 || double.IsNaN(tt) ? cur : (tt >= cur ? cur : 1); }
        return b == cur;
    }
    private Built BuildM01Vt(Dictionary<string, JsonNode?> meta)
    {
        var raw = Rows("ton-vi-tri-an-an", "ViTri"); var keys = VtKeys(raw);
        var vm = JsonCell("ton-vi-tri-an-an", "ViTriMeta") ?? new(); var impAt = Loose(vm["importedAt"]); if (double.IsNaN(impAt)) impAt = 0;
        var slips = (JsonCell("ton-vi-tri-an-an", "ViTriPhieuDienThoai")?["slips"] as JsonArray ?? new()).OfType<JsonObject>().ToList();
        var idx = new Dictionary<string, int>(); for (var i = 0; i < keys.Count; i++) if (keys[i].Length > 0) idx[keys[i]] = i;
        var daXuat = new double[keys.Count]; var homNay = new double[keys.Count]; var today = Now().Date;
        foreach (var sl in slips)
        {
            if (!(sl["deducted"] is JsonValue dv && dv.TryGetValue<bool>(out var dd) && dd) || !SlipActive(sl, impAt)) continue;
            var isToday = DateTime.TryParse(S(sl["createdAt"]), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ct) && ct.ToLocalTime().Date == today;
            foreach (var l in (sl["lines"] as JsonArray ?? new()).OfType<JsonObject>())
            {
                if (!idx.TryGetValue(S(l["key"]), out var i)) continue; var q = Loose(l["qty"]); if (double.IsNaN(q)) q = 0;
                daXuat[i] += q; if (isToday) homNay[i] += q;
            }
        }
        var gmap = new Dictionary<string, string>();
        foreach (var r in Rows("ton-kho-an-an", "DacTinh")) { var l = S(r["Đặc Tính"]); var g = S(r["Nhóm đã gộp"]); if (l.Length > 0 && g.Length > 0) gmap[l.ToLowerInvariant()] = g; }
        string Nhom(string ten) { var p = ten.Split(" - "); var dt = p.Length >= 3 ? p[^2].Trim() : ""; return dt.Length == 0 ? "" : gmap.GetValueOrDefault(dt.ToLowerInvariant(), dt); }
        var rows = new List<Dictionary<string, JsonNode?>>();
        for (var i = 0; i < raw.Count; i++)
        {
            if (keys[i].Length == 0) continue;
            var o = raw[i].ToDictionary(kv => kv.Key, kv => kv.Value?.DeepClone());
            o["Đã xuất"] = daXuat[i] != 0 ? daXuat[i] : ""; o["Xuất hôm nay"] = homNay[i] != 0 ? homNay[i] : ""; o["Nhóm đặc tính"] = Nhom(S(raw[i]["Tên hàng"])); o["_key"] = keys[i];
            rows.Add(o);
        }
        meta["m01ViTriRows"] = rows.Count; meta["m01ViTriDataDate"] = S(vm["dataDate"]); meta["m01ViTriImportedAt"] = MsIso(impAt);
        return new Built(Tab.M01VT, rows.Count > 0 ? VtLbl.Select(x => x.Col).Concat(new[] { "Đã xuất", "Xuất hôm nay", "Nhóm đặc tính", "_key" }).ToList() : new(), rows);
    }

    // ---- M02 · Tồn kho gửi (Đặc tính suy ra như màn hình PC)
    public static readonly string[] M2Push = { "Mã hàng", "Tên hàng", "Đặc tính", "Số lô", "Size", "Quy cách", "Ngày nhập kho", "Phiếu nhập/Voucher", "Số kiện", "Trọng Lượng (KG)" };
    private List<JsonObject> M2WithDerivedDacTinh(List<JsonObject> rows)
    {
        var manual = Rows("ton-kho-gui", "DacTinh_NhapTay").Select(r => S(r["Đặc tính"])).Where(x => x.Length > 0)
            .Select(dt => Regex.Match(dt, @"^([^.]+)\.") is { Success: true } m ? (Prefix: m.Groups[1].Value.Trim(), Dt: dt) : (Prefix: "", Dt: "")).Where(x => x.Dt.Length > 0).ToList();
        var loMap = new Dictionary<string, string>();
        foreach (var r in Rows("ton-kho-gui", "DacTinh_ChuyenLo")) { var lo = S(r["Số lô"]).ToLowerInvariant(); var dt = S(r["Đặc tính"]); if (lo.Length > 0 && dt.Length > 0) loMap[lo] = dt; }
        static string LastSeg(string t) { t = t.Trim(); if (t.Length == 0) return ""; if (!t.Contains(',')) return t; return t.Split(',')[^1].Trim(); }
        string? Match(string v)
        {
            var vt = v.Trim(); if (vt.Length == 0) return null;
            string? vn = Regex.IsMatch(vt, @"^\d+$") ? long.Parse(vt).ToString(CultureInfo.InvariantCulture) : null;
            foreach (var r in manual)
            {
                if (r.Prefix == vt) return r.Dt;
                if (vn is not null && Regex.IsMatch(r.Prefix, @"^\d+$") && long.Parse(r.Prefix).ToString(CultureInfo.InvariantCulture) == vn) return r.Dt;
            }
            return null;
        }
        return rows.Select(r =>
        {
            var seg = LastSeg(S(r["Tên hàng"])); if (seg.Length == 0) return r;
            var dt = Match(seg) ?? seg;
            if (dt == "CKQ") { var lo = S(r["Số lô"]); dt = lo.Length > 0 && loMap.TryGetValue(lo.ToLowerInvariant(), out var x) ? x : lo; }
            if (dt.Length == 0) return r;
            var c = (JsonObject)r.DeepClone(); c["Đặc tính"] = dt; return c;
        }).ToList();
    }
    private Built BuildM02(Dictionary<string, JsonNode?> meta)
    {
        var rows = M2WithDerivedDacTinh(Rows("ton-kho-gui", "DuLieuChinh")).Select(o => M2Push.ToDictionary(c => c, c => o[c]?.DeepClone() ?? JsonValue.Create(""))).ToList();
        var m = JsonCell("ton-kho-gui", "DuLieuMeta") ?? new();
        meta["m02Rows"] = rows.Count; meta["m02DataDate"] = S(m["dataDate"]); meta["m02ImportedAt"] = Loose(m["importedAt"]) is var ia && ia > 0 ? MsIso(ia) : "";
        return new Built(Tab.M02, rows.Count > 0 ? M2Push.ToList() : new(), rows);
    }

    // ---- M03 · NXT Bột/Sốt: mỗi LÔ còn tồn = 1 dòng (giữ tên cột cũ M3COL) + danh mục LSX
    public static readonly (string F, string Col)[] M3Col = { ("maPhieu", "Mã phiếu"), ("ngay", "Ngày nhập"), ("viTri", "Vị trí"), ("loaiHang", "Loại Hàng"), ("maHang", "Mã Hàng"), ("lsx", "LSX / Hợp đồng"), ("khach", "Khách hàng"), ("net", "Trọng lượng (kg)"), ("kien", "Kiện/Bao"), ("tong", "Tổng (kg)"), ("ghiChu", "Ghi chú") };
    public static string LotKey(SkLot l) => string.Join("|", new[] { l.ViTri, l.MaHang, l.Size, l.HanDung, l.NgayNhap }.Select(p => EncodeUri(p ?? "")));
    private List<SkLine> SkLines() => Rows(SoKhoCore.Db, SoKhoCore.SLedger).Select(SoKhoCore.NormLine).ToList();
    private List<Built> BuildM03(Dictionary<string, JsonNode?> meta)
    {
        const double Eps = 1e-9;
        var lines = SkLines(); var lots = SoKhoCore.Compute(lines).Lots.Where(l => l.Net > Eps || l.Sl > Eps).ToList();
        var lsx = new Dictionary<string, Dictionary<string, JsonNode?>>();
        foreach (var o in Rows(SoKhoCore.Db, SoKhoCore.SOutputs)) { var h = S(o["hopDong"]); if (h.Length > 0) lsx[h] = new() { ["id"] = h, ["code"] = h, ["name"] = h, ["khachHang"] = S(o["khach"]) }; }
        foreach (var l in lines) if (l.HopDong.Length > 0 && !lsx.ContainsKey(l.HopDong)) lsx[l.HopDong] = new() { ["id"] = l.HopDong, ["code"] = l.HopDong, ["name"] = l.HopDong, ["khachHang"] = "" };
        var rows = lots.Select(l =>
        {
            var hasNet = l.Net > Eps; var perBag = hasNet && l.Sl > Eps ? l.Net / l.Sl : 0;
            return new Dictionary<string, JsonNode?>
            {
                ["Mã phiếu"] = l.ChungTu, ["Ngày nhập"] = l.NgayNhap, ["Vị trí"] = l.ViTri, ["Loại Hàng"] = l.MaHang, ["Mã Hàng"] = l.Size, ["LSX / Hợp đồng"] = l.HopDong, ["Khách hàng"] = "",
                ["Trọng lượng (kg)"] = perBag > 0 ? R4(perBag) : "", ["Kiện/Bao"] = l.Sl > Eps ? l.Sl : "", ["Tổng (kg)"] = hasNet ? R4(l.Net) : R4(l.Sl), ["Ghi chú"] = "", ["_key"] = LotKey(l),
            };
        }).ToList();
        meta["m03Rows"] = rows.Count;
        return new() { new Built(Tab.M03, rows.Count > 0 ? M3Col.Select(c => c.Col).Append("_key").ToList() : new(), rows), new Built(Tab.M03Lsx, new() { "id", "code", "name", "khachHang" }, lsx.Values.ToList()) };
    }

    // ---- M08 · Tổng hợp (chỉ xem trên điện thoại)
    private List<Built> BuildM08(Dictionary<string, JsonNode?> meta)
    {
        var th = new TongHop.TongHopService(() => Src, _s.Session, _s.Audit, _s.Settings); th.Load();
        var maHoa = th.SegmentData.OrderBy(k => k.Key).SelectMany(k => k.Value.Select(e => new Dictionary<string, JsonNode?> { ["seg"] = k.Key, ["code"] = e.Code, ["name"] = e.Name })).ToList();
        var maDaTao = th.Results.Select((r, i) => new Dictionary<string, JsonNode?> { ["stt"] = r.Stt > 0 ? r.Stt : i + 1, ["code"] = r.Code, ["note"] = r.Note }).ToList();
        static JsonNode? Clean(JsonNode? v) => v is null ? "" : v is JsonValue ? v.DeepClone() : v.ToJsonString(J);
        var tCols = th.Tab2.Headers.ToList();
        var tRows = th.Tab2.Rows.Select(r => tCols.ToDictionary(c => c, c => Clean(r.GetValueOrDefault(c)))).ToList();
        var ax = th.AnhXa.ToPhoneTable().Select(x => new Dictionary<string, JsonNode?> { ["pv"] = x.Pv, ["ma"] = x.Ma, ["full"] = x.Full, ["short"] = x.Short }).ToList();
        meta["m08Tab2CodeCol"] = th.Tab2.CodeCol; meta["m08Tab2DateCol"] = th.Tab2.DateCol;
        return new() { new Built(Tab.M08MaHoa, new() { "seg", "code", "name" }, maHoa), new Built(Tab.M08MaDaTao, new() { "stt", "code", "note" }, maDaTao), new Built(Tab.M08TongHop, tCols, tRows), new Built(Tab.M08AnhXa, new() { "pv", "ma", "full", "short" }, ax) };
    }

    // ---- LX · Lịch xuất nhập hàng (từ đầu tháng hiện tại − 2) + tổng kết mọi tháng
    public static readonly string[] LxCols = { "id", "loai", "ngay", "trangThai", "lsx", "khach", "thiTruong", "matHang", "size", "tonKien", "kg", "chiTiet", "canXuat", "doLuc", "cont" };
    public static readonly string[] TkCols = { "thang", "soLich", "daXuat", "kg", "kgDaXuat", "soNhap", "daNhap", "kgNhap", "kgDaNhap", "capNhat" };
    private List<Built> BuildLx(Dictionary<string, JsonNode?> meta)
    {
        var d0 = new DateTime(Now().Year, Now().Month, 1).AddMonths(-2); var from = d0.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lx = Rows("lichxuat", "LichXuatCont").Where(r => Regex.IsMatch(S(r["ngay"]), @"^\d{4}-\d{2}-\d{2}") && string.CompareOrdinal(S(r["ngay"])[..10], from) >= 0).Select(r =>
        {
            var o = LxCols.ToDictionary(c => c, c => r[c]?.DeepClone() ?? JsonValue.Create(""));
            o["loai"] = S(r["loai"]) == "nhap" ? "nhap" : "xuat"; o["ngay"] = S(r["ngay"])[..10];
            if (S(o["chiTiet"]).Length > 40000) o["chiTiet"] = "";
            return o;
        }).ToList();
        var tk = Rows("lichxuat", "TongKetThang").Where(r => Regex.IsMatch(S(r["thang"]), @"^\d{4}-\d{2}$")).Select(r => TkCols.ToDictionary(c => c, c => r[c]?.DeepClone() ?? JsonValue.Create(""))).ToList();
        meta["lxRows"] = lx.Count;
        return new() { new Built(Tab.Lx, lx.Count > 0 ? LxCols.ToList() : new(), lx), new Built(Tab.LxThang, tk.Count > 0 ? TkCols.ToList() : new(), tk) };
    }

    // ---- C3D · Container 3D (cont đã sửa + phương án + danh mục thùng — điện thoại chỉ xem)
    public static readonly string[] C3dCols = { "kind", "id", "name", "capNhat", "contName", "loaded", "requested", "data" };
    private Built BuildC3d(Dictionary<string, JsonNode?> meta)
    {
        var rows = new List<Dictionary<string, JsonNode?>>();
        void Add(string kind, JsonObject r, bool plan)
        {
            if (S(r["id"]).Length == 0 || S(r["data"]).Length > 40000) return;
            rows.Add(new() { ["kind"] = kind, ["id"] = r["id"]?.DeepClone(), ["name"] = S(r["name"]), ["capNhat"] = kind == "cont" ? "" : S(r["capNhat"]), ["contName"] = plan ? S(r["contName"]) : "", ["loaded"] = plan ? r["loaded"]?.DeepClone() ?? "" : "", ["requested"] = plan ? r["requested"]?.DeepClone() ?? "" : "", ["data"] = r["data"]?.DeepClone() });
        }
        foreach (var r in Rows("container-3d", "C3D_Container")) Add("cont", r, false);
        foreach (var r in Rows("container-3d", "C3D_Plan")) Add("plan", r, true);
        foreach (var r in Rows("container-3d", "C3D_Thung")) Add("thung", r, false);
        meta["c3dRows"] = rows.Count;
        return new Built(Tab.C3d, rows.Count > 0 ? C3dCols.ToList() : new(), rows);
    }

    // ---- Phiếu xuất tạo trên PC → PhieuXuat_PC / PhieuXuatItems_PC (điện thoại chỉ xem) — chép core/phieu-pc-push.js
    public static readonly string[] PcHeaders = { "id", "maPhieu", "ngayTao", "trangThai", "nguoiTao", "ghiChu", "xuLyLuc", "module", "lsxXuat", "nguoiDung", "nguon" };
    public static readonly string[] PcItemHeaders = { "phieuId", "maHang", "tenHang", "phieuNhap", "soLo", "size", "soKien", "soLuongXuat", "itemKey", "rowIndex", "hopDong", "viTri", "tl" };
    public const string TtPc = "Tạo trên PC", TtPcHuy = "Tạo trên PC - Đã hủy trừ tồn";
    private sealed record PcSlip(Dictionary<string, JsonNode?> P, List<Dictionary<string, JsonNode?>> Items);
    private static string PcToIso(JsonNode? v)
    {
        if (v is null) return "";
        if (v is JsonValue nv && nv.TryGetValue<double>(out var ms)) return MsIso(ms);
        var s = S(v); if (s.Length == 0) return "";
        if (Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$")) return s + "T00:00:00";
        var m = Regex.Match(s, @"^(\d{1,2})/(\d{1,2})/(\d{4})$"); if (m.Success) return $"{m.Groups[3]}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[1].Value.PadLeft(2, '0')}T00:00:00";
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) : s;
    }
    private static string PcId(string src, string raw) => Regex.Replace("pc_" + src + "_" + raw, @"[^A-Za-z0-9_|.-]+", "-") is var t && t.Length > 90 ? t[..90] : t;
    private static Dictionary<string, JsonNode?> PcItem(params (string K, JsonNode? V)[] kv)
    {
        var o = new Dictionary<string, JsonNode?> { ["maHang"] = "", ["tenHang"] = "", ["phieuNhap"] = "", ["soLo"] = "", ["size"] = "", ["soKien"] = "", ["soLuongXuat"] = 0, ["itemKey"] = "", ["rowIndex"] = "", ["hopDong"] = "", ["viTri"] = "", ["tl"] = "" };
        foreach (var (k, v) in kv) o[k] = v; return o;
    }
    private static Dictionary<string, JsonNode?> PcPhieu(params (string K, JsonNode? V)[] kv)
    {
        var o = new Dictionary<string, JsonNode?> { ["trangThai"] = TtPc, ["nguoiTao"] = "PC", ["ghiChu"] = "", ["lsxXuat"] = "", ["nguoiDung"] = "" };
        foreach (var (k, v) in kv) o[k] = v; return o;
    }
    private static double N0(JsonNode? v) { var s = S(v).Replace(",", ""); return s.Length == 0 ? 0 : double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : 0; }
    private static bool Off(JsonObject s) => s["deducted"] is JsonValue v && v.TryGetValue<bool>(out var b) && !b;
    // ---- 1.72 (B6): phiếu PC lưu / sửa / hủy → đẩy riêng 2 bảng phiếu PC lên điện thoại ngay (không chờ lượt đồng bộ định kỳ)
    /// <summary>Sheet chứa phiếu xuất PC (nguồn của <see cref="CollectPcSlips"/>).</summary>
    public static readonly (string Db, string Sheet)[] PcSlipSheets =
        { ("ton-kho-an-an", "PhieuXuat"), ("ton-vi-tri-an-an", "ViTriPhieuDienThoai"), ("ton-kho-an-an", "ViTriBotPhieuDienThoai"), ("ton-kho-gui", "PhieuXuat"), ("ton-kho-gui", "PhieuXuatItems") };
    public static bool IsPcSlipWrite(string db, IEnumerable<string> sheets) => sheets.Any(sh => PcSlipSheets.Any(x => x.Db == db && x.Sheet == sh));
    public string LastSlipPush { get; private set; } = "";
    /// <summary>Đẩy riêng PhieuXuat_PC / PhieuXuatItems_PC. Lỗi (mất mạng…) → ghi lại, lượt đồng bộ định kỳ sẽ đẩy lại cả bảng.
    /// 1.90: mỗi bảng chỉ ghi khi nội dung khác lần ghi thành công trước (<see cref="_pcSig"/>) — lưu phiếu thường ghi cả sheet
    /// tồn vị trí / sửa SL… mà danh sách phiếu PC không đổi; mỗi lần ghi là 1 lượt Google Apps Script ~3 giây.</summary>
    public async Task<int> PushPcSlipsAsync(CancellationToken ct = default)
    {
        using var perf = KhoLanh.Core.TroLy.Perf.Measure("Đẩy phiếu PC lên điện thoại");
        var (ph, it, _) = CollectPcSlips();
        var n = await WritePcSlipTablesAsync(ph, it, force: false, ct);
        perf.Rows = ph.Count;
        perf.Detail = n == 0 ? "không đổi, bỏ qua" : $"chạy nền · ghi {n} bảng lên Google Sheet";
        LastSlipPush = n == 0 ? $"Phiếu PC không đổi — không cần đẩy ({DateTime.Now:HH:mm:ss})" : $"Đã đẩy {ph.Count} phiếu PC lên điện thoại lúc {DateTime.Now:HH:mm:ss}";
        return ph.Count;
    }
    /// <summary>Chữ ký nội dung lần ghi thành công gần nhất của 2 bảng phiếu PC (theo tab).</summary>
    private readonly Dictionary<string, string> _pcSig = new();
    /// <summary>Số lượt ghi bảng phiếu PC đã gửi (thử nghiệm).</summary>
    internal int PcTableWrites;
    private async Task<int> WritePcSlipTablesAsync(List<Dictionary<string, JsonNode?>> ph, List<Dictionary<string, JsonNode?>> it, bool force, CancellationToken ct)
    {
        var n = 0;
        foreach (var (tab, cols, rows) in new[] { (Tab.PxPc, PcHeaders, ph), (Tab.PxPci, PcItemHeaders, it) })
        {
            var sig = Sig(rows);
            lock (_pcSig) if (!force && _pcSig.TryGetValue(tab, out var old) && old == sig) continue;
            lock (_pcSig) _pcSig.Remove(tab);   // đang ghi: lỗi giữa chừng thì lần sau ghi lại
            await W.WriteTableAsync(tab, rows.Count > 0 ? cols : Array.Empty<string>(), rows.Select(r => (IDictionary<string, JsonNode?>)r), ct);
            lock (_pcSig) _pcSig[tab] = sig;
            Interlocked.Increment(ref PcTableWrites); n++;
        }
        return n;
    }
    private CancellationTokenSource? _slipCts;
    private bool _slipHooked;
    /// <summary>Bật đẩy phiếu ngay: nghe ghi CSDL module, gom 3 giây rồi đẩy (chỉ khi đã kết nối điện thoại và tài khoản được xem các bảng tồn).</summary>
    public void StartInstantSlipPush()
    {
        if (_slipHooked) return; _slipHooked = true;
        _s.Modules.Written += (db, sheets) =>
        {
            if (!W.Enabled || !IsPcSlipWrite(db, sheets) || !Pick.Any(t => t is "M01" or "M01VT" or "M01VTB" or "M02" or "M03")) return;
            _slipCts?.Cancel();
            var cts = _slipCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(3000, cts.Token); await PushPcSlipsAsync(cts.Token); }
                catch (OperationCanceledException) { }
                catch (Exception e) { LastSlipPush = $"Chưa đẩy được phiếu lúc {DateTime.Now:HH:mm:ss} ({e.Message}) — sẽ đẩy lại ở lượt đồng bộ sau"; }
            });
        };
    }

    // ---------------------------------------------------------------- 1.145: đẩy NGAY khi có thay đổi (gom, chạy nền, chỉ bảng đổi)
    /// <summary>Chỉ 1 lần đồng bộ / đẩy điện thoại chạy cùng lúc (đồng bộ định kỳ, nút bấm, đẩy ngay dùng chung).</summary>
    public static readonly SemaphoreSlim Gate = new(1, 1);
    /// <summary>Thời gian gom thay đổi: đẩy sau lần sửa cuối chừng này (mỗi lần ghi 1 bảng lên Apps Script ~3 giây nên phải gom).</summary>
    public TimeSpan InstantDelay { get; set; } = TimeSpan.FromSeconds(25);
    /// <summary>Bảng điện thoại bị ảnh hưởng khi ghi sheet của 1 database module (rỗng = không liên quan điện thoại).</summary>
    public static IReadOnlyList<string> TablesOfWrite(string db, IEnumerable<string> sheets)
    {
        var l = new List<string>();
        switch (db)
        {
            case "ton-kho-an-an": l.Add("M01"); l.Add("M01VTB"); break;
            case "ton-vi-tri-an-an": l.Add("M01VT"); break;
            case "ton-kho-gui": l.Add("M02"); break;
            case "tong-hop": case "ket-qua-khang-sinh": case "danh-muc": l.Add("M08"); break;
            case "lichxuat": l.Add("LX"); break;
            case "container-3d": l.Add("C3D"); break;
            default: if (db == SoKhoCore.Db) l.Add("M03"); break;
        }
        return l;
    }
    /// <summary>Chế độ "Đẩy ngay khi có thay đổi" đang bật (settings phone_sync_instant = 1).</summary>
    public bool InstantOn { get => _s.Settings.Get(KeyInstant, "0") == "1"; set => _s.Settings.Set(KeyInstant, value ? "1" : "0"); }
    public const string KeyInstant = "phone_sync_instant";
    /// <summary>Đang trong lần đồng bộ / đẩy của chính dịch vụ này: các ghi do nó gây ra (vd "DacTinh_DaGhiNhan", phiếu nhận từ điện thoại) không được tính là thay đổi mới — tránh vòng đẩy lặp.</summary>
    private static readonly AsyncLocal<bool> InSync = new();
    private readonly HashSet<string> _dirty = new();
    private CancellationTokenSource? _instCts;
    private bool _instHooked;
    public string LastInstant { get; private set; } = "";
    /// <summary>Số lần đẩy ngay đã chạy (thử nghiệm).</summary>
    internal int InstantRuns;
    /// <summary>Nghe ghi CSDL module: bảng nào đổi thì đánh dấu, sau <see cref="InstantDelay"/> yên lặng mới đẩy RIÊNG các bảng đó (nền, không chồng lần đang chạy).
    /// Máy con LAN không đẩy (dữ liệu về máy con do kéo từ máy chủ; máy chủ / máy đơn đẩy).</summary>
    public void StartInstantPush()
    {
        if (_instHooked) return; _instHooked = true;
        _s.Modules.Written += (db, sheets) =>
        {
            try
            {
                if (InSync.Value || !InstantOn || !W.Enabled || _s.Lan.Role == KhoLanh.Core.Lan.LanService.RoleClient) return;
                var t = TablesOfWrite(db, sheets).Where(Pick.Contains).ToList();
                if (t.Count == 0) return;
                lock (_dirty) foreach (var x in t) _dirty.Add(x);
                ScheduleInstant(InstantDelay);
            }
            catch { /* người nghe lỗi không ảnh hưởng ghi */ }
        };
    }
    private void ScheduleInstant(TimeSpan delay)
    {
        _instCts?.Cancel();
        var cts = _instCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(delay, cts.Token); await FlushInstantAsync(cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { LastInstant = $"Chưa đẩy ngay được lúc {Now():HH:mm:ss} ({e.Message}) — lượt đồng bộ định kỳ sẽ đẩy lại"; }
        });
    }
    /// <summary>Đẩy các bảng đang đánh dấu đổi. Đang có lần đồng bộ khác chạy → hoãn thêm, không chạy chồng.</summary>
    public async Task<int> FlushInstantAsync(CancellationToken ct = default)
    {
        if (!await Gate.WaitAsync(0, ct)) { ScheduleInstant(InstantDelay); return -1; }
        List<string> todo;
        lock (_dirty) { todo = _dirty.ToList(); _dirty.Clear(); }
        try
        {
            if (todo.Count == 0 || !InstantOn || !W.Enabled) return 0;
            Interlocked.Increment(ref InstantRuns);
            var res = await PushAsync(todo, ct);
            LastInstant = res.Errors.Count == 0 ? $"Đã đẩy ngay {string.Join(", ", res.Tables)} lúc {Now():HH:mm:ss}" : $"Đẩy ngay có lỗi lúc {Now():HH:mm:ss}: {res.Errors[0]}";
            if (res.Errors.Count > 0) lock (_dirty) foreach (var x in todo) _dirty.Add(x);   // lần sau thử lại
            return res.Tables.Count;
        }
        catch { lock (_dirty) foreach (var x in todo) _dirty.Add(x); throw; }
        finally { Gate.Release(); }
    }

    public (List<Dictionary<string, JsonNode?>> Phieu, List<Dictionary<string, JsonNode?>> Items, List<string> Errors) CollectPcSlips()
    {
        var sources = new List<(string K, Func<List<PcSlip>> F)>
        {
            ("m1xh", () => ((JsonCell("ton-kho-an-an", "PhieuXuat")?["slips"] as JsonArray) ?? new()).OfType<JsonObject>().Where(s => !(S(s["phoneId"]).Length > 0 || S(s["moduleId"]) == "dienthoai")).Select(s =>
                new PcSlip(PcPhieu(("id", PcId("m1xh", S(s["id"]))), ("maPhieu", S(s["id"])), ("ngayTao", PcToIso(s["createdAt"])), ("module", "M01"), ("nguoiTao", S(s["nguoiTao"]).Length > 0 ? S(s["nguoiTao"]) : "PC"), ("ghiChu", S(s["ghiChu"])), ("trangThai", Off(s) ? TtPcHuy : TtPc)),
                    (s["lines"] as JsonArray ?? new()).OfType<JsonObject>().Select(l => PcItem(("maHang", S(l["maHang"])), ("tenHang", S(l["tenHang"])), ("size", S(l["size"])), ("hopDong", S(l["hopDong"])), ("soLuongXuat", N0(l["qty"])))).ToList())).ToList()),
            ("m1vt", () => ((JsonCell("ton-vi-tri-an-an", "ViTriPhieuDienThoai")?["slips"] as JsonArray) ?? new()).OfType<JsonObject>().Where(s => S(s["source"]) == "pc").Select(s =>
            {
                var view = (s["view"]?["items"] as JsonArray)?.OfType<JsonObject>().ToList();
                var items = view is { Count: > 0 }
                    ? view.Select(l => PcItem(("maHang", string.Join(", ", (S(l["ma21"]).Length > 0 ? S(l["ma21"]) : S(l["ma"])).Split('\n'))), ("tenHang", S(l["ma"])), ("viTri", S(l["vt"])), ("hopDong", S(l["hd"])), ("soLo", S(l["dt"])), ("soLuongXuat", N0(l["x"])), ("tl", N0(l["tl"]) is var tl && tl != 0 ? tl : ""), ("size", S(l["ma21"]).Length > 0 && S(l["ma"]) != S(l["ma21"]) ? S(l["ma"]) : ""))).ToList()
                    : (s["lines"] as JsonArray ?? new()).OfType<JsonObject>().Select(l => PcItem(("maHang", S(l["maGoc"])), ("tenHang", S(l["ten"])), ("soLo", S(l["lo"])), ("viTri", S(l["vt"])), ("soLuongXuat", N0(l["qty"])), ("tl", N0(l["tl"]) is var tl && tl != 0 ? tl : ""))).ToList();
                return new PcSlip(PcPhieu(("id", PcId("m1vt", S(s["id"]))), ("maPhieu", S(s["maPhieu"]).Length > 0 ? S(s["maPhieu"]) : S(s["id"])), ("ngayTao", PcToIso(s["createdAt"])), ("module", "M01VT"), ("ghiChu", S(s["ghiChu"])), ("trangThai", Off(s) ? TtPcHuy : TtPc)), items);
            }).ToList()),
            ("m1vtb", () => ((JsonCell("ton-kho-an-an", "ViTriBotPhieuDienThoai")?["slips"] as JsonArray) ?? new()).OfType<JsonObject>().Where(s => S(s["source"]) == "pc" || S(s["nguon"]) == "pc").Select(s =>
                new PcSlip(PcPhieu(("id", PcId("m1vtb", S(s["id"]))), ("maPhieu", S(s["maPhieu"]).Length > 0 ? S(s["maPhieu"]) : S(s["id"])), ("ngayTao", PcToIso(s["createdAt"])), ("module", "M01VTB"), ("ghiChu", S(s["ghiChu"])), ("trangThai", Off(s) ? TtPcHuy : TtPc)),
                    (s["lines"] as JsonArray ?? new()).OfType<JsonObject>().Select(l => PcItem(("maHang", S(l["ma"])), ("viTri", S(l["vt"])), ("soLo", S(l["nsx"])), ("tenHang", S(l["ghiChu"])), ("soLuongXuat", N0(l["qty"])))).ToList())).ToList()),
            ("m2", () =>
            {
                var by = new Dictionary<string, List<Dictionary<string, JsonNode?>>>();
                foreach (var r in Rows("ton-kho-gui", "PhieuXuatItems")) { var k = S(r["phieuId"]); if (!by.TryGetValue(k, out var l)) by[k] = l = new(); l.Add(PcItem(("maHang", S(r["maHang"])), ("tenHang", S(r["tenHang"])), ("phieuNhap", S(r["phieuNhap"])), ("soLo", S(r["soLo"])), ("size", S(r["size"])), ("soKien", N0(r["soKien"])), ("soLuongXuat", N0(r["soLuongXuat"])))); }
                return Rows("ton-kho-gui", "PhieuXuat").Where(r => (S(r["maPhieu"]).Length > 0 || S(r["id"]).Length > 0) && !Regex.IsMatch(S(r["nguon"]), "điện thoại|dien thoai|dienthoai", RegexOptions.IgnoreCase)).Select(r =>
                    new PcSlip(PcPhieu(("id", PcId("m2", S(r["id"]).Length > 0 ? S(r["id"]) : S(r["maPhieu"]))), ("maPhieu", S(r["maPhieu"]).Length > 0 ? S(r["maPhieu"]) : S(r["id"])), ("ngayTao", PcToIso(r["ngayTao"])), ("module", "M02"), ("nguoiTao", S(r["nguoiTao"]).Length > 0 ? S(r["nguoiTao"]) : "PC"), ("ghiChu", S(r["ghiChu"]))), by.GetValueOrDefault(S(r["id"])) ?? new())).ToList();
            }),
            ("m3", () =>
            {
                var groups = new Dictionary<string, PcSlip>(); var outp = new List<PcSlip>();
                foreach (var l in Rows(SoKhoCore.Db, SoKhoCore.SLedger).Where(l => S(l["loai"]) == "xuat" && S(l["nguon"]) != "dt"))
                {
                    var gk = S(l["phieuId"]).Length > 0 ? "ph|" + S(l["phieuId"]) : "ct|" + (S(l["chungTu"]).Length > 0 ? S(l["chungTu"]) : S(l["ngay"]));
                    if (!groups.TryGetValue(gk, out var g))
                    {
                        g = new PcSlip(PcPhieu(("id", PcId("m3", gk)), ("maPhieu", S(l["chungTu"]).Length > 0 ? S(l["chungTu"]) : "XK-" + S(l["ngay"]).Replace("-", "")), ("ngayTao", PcToIso(S(l["luc"]).Length > 0 ? l["luc"] : l["ngay"])), ("module", "M03"), ("nguoiTao", S(l["nguoi"]).Length > 0 ? S(l["nguoi"]) : "PC"), ("ghiChu", S(l["ghiChu"]))), new());
                        groups[gk] = g; outp.Add(g);
                    }
                    g.Items.Add(PcItem(("maHang", S(l["maHang"])), ("size", S(l["size"])), ("viTri", S(l["viTri"])), ("soLo", S(l["hopDong"])), ("hopDong", S(l["hopDong"])), ("soLuongXuat", N0(l["soLuong"]))));
                }
                return outp;
            }),
        };
        var all = new List<PcSlip>(); var errors = new List<string>();
        foreach (var (k, f) in sources)
        {
            List<PcSlip> list; try { list = f(); } catch (Exception e) { errors.Add(k + ": " + e.Message); list = new(); }
            list = list.Where(x => x.Items.Any(i => N0(i["soLuongXuat"]) > 0 || S(i["maHang"]).Length > 0)).OrderByDescending(x => S(x.P["ngayTao"]), StringComparer.Ordinal).Take(200).ToList();
            all.AddRange(list);
        }
        all = all.OrderByDescending(x => S(x.P["ngayTao"]), StringComparer.Ordinal).ToList();
        var seen = new HashSet<string>(); var ph = new List<Dictionary<string, JsonNode?>>(); var it = new List<Dictionary<string, JsonNode?>>();
        foreach (var x in all)
        {
            var id = S(x.P["id"]); if (seen.Contains(id)) continue; if (it.Count + x.Items.Count > 12000) break;
            seen.Add(id); var p = new Dictionary<string, JsonNode?> { ["nguon"] = "PC" }; foreach (var (k, v) in x.P) p[k] = v; ph.Add(p);
            foreach (var i in x.Items) { var c = new Dictionary<string, JsonNode?>(i) { ["phieuId"] = id }; it.Add(c); }
        }
        return (ph, it, errors);
    }

    /// <summary>Đẩy các bảng chọn (mặc định: bảng đã tick) + phiếu PC + Meta (ghi SAU CÙNG).</summary>
    public async Task<PushResult> PushAsync(IEnumerable<string>? tables = null, CancellationToken ct = default)
    {
        InSync.Value = true;
        using var _perf = KhoLanh.Core.TroLy.Perf.Measure("Đẩy lên điện thoại", tables is null ? "mọi bảng đã chọn" : string.Join(", ", tables));
        var pick = (tables ?? Pick).Where(SyncTables.Contains).Where(CanTable).Distinct().ToList();
        var res = new PushResult(pick, new(), new());
        if (pick.Count == 0) return res;
        var tabs = new Dictionary<string, string[]?> { [Tab.Meta] = null, [Tab.Px] = PhoneWebApp.PhieuHeaders, [Tab.Pxi] = PhoneWebApp.PhieuItemHeaders };
        var tabOf = new Dictionary<string, string[]> { ["M01"] = new[] { Tab.M01 }, ["M01VT"] = new[] { Tab.M01VT }, ["M01VTB"] = new[] { Tab.M01VTB }, ["M02"] = new[] { Tab.M02 }, ["M03"] = new[] { Tab.M03, Tab.M03Lsx }, ["M08"] = new[] { Tab.M08MaHoa, Tab.M08MaDaTao, Tab.M08TongHop, Tab.M08Ks }, ["LX"] = new[] { Tab.Lx, Tab.LxThang }, ["C3D"] = new[] { Tab.C3d } };
        foreach (var t in pick) foreach (var n in tabOf[t]) tabs[n] = null;
        await W.EnsureTabsAsync(tabs, ct);
        var meta = await ReadMetaAsync(ct); var oldLast = meta.GetValueOrDefault("lastPushAt") is { } lp ? S(lp) : "";
        var status = Status; var nowS = NowIso();
        foreach (var t in pick)
        {
            try
            {
                var built = Build(t, meta); var n = 0;
                foreach (var b in built)
                {
                    try { await W.WriteTableAsync(b.Tab, b.Columns, b.Rows.Select(r => (IDictionary<string, JsonNode?>)r), ct); }
                    catch (BusinessException e) when (b.Tab == Tab.M08AnhXa) { res.Errors.Add("Ánh xạ tên hàng: " + e.Message); continue; }   // Code.gs bản cũ chưa biết tab này
                    n += b.Rows.Count;
                }
                res.Rows[t] = n;
                var sig = Sig(built.Select(b => b.Rows).ToList()); var prev = status.GetValueOrDefault(t);
                var changed = prev?.Sig != sig;
                status[t] = new TableStatus(nowS, changed, changed ? nowS : prev?.ChangedAt ?? "", "", sig, n);
            }
            catch (Exception e) { res.Errors.Add($"{t}: {e.Message}"); status[t] = (status.GetValueOrDefault(t) ?? new TableStatus("", false, "", "", "", 0)) with { At = nowS, Error = e.Message }; }
        }
        // 1.55: báo cáo Excel (+ Phân loại) cho điện thoại tải về; kháng sinh "chỉ phần mới" — lỗi không làm hỏng lần đẩy tồn
        if (pick.Contains("M01")) await PushReportAsync("M01", meta, res.Errors, ct);
        if (pick.Contains("M02")) await PushReportAsync("M02", meta, res.Errors, ct);
        if (pick.Contains("M08") && _s.Session.Can(KhangSinh.KhangSinhService.PermKey, Perm.View))
        {
            try { var k = await PushKhangSinhAsync(ct); res.Rows["KS"] = k.Sent; }
            catch (Exception e) { res.Errors.Add("Kháng sinh: " + e.Message); }
        }
        // phiếu PC cho điện thoại xem — lỗi không làm hỏng lần đẩy tồn
        if (pick.Any(t => t is "M01" or "M01VT" or "M01VTB" or "M02" or "M03"))
        {
            try
            {
                var (ph, it, errs) = CollectPcSlips();
                await WritePcSlipTablesAsync(ph, it, force: true, ct);   // lượt định kỳ luôn ghi lại (phòng Sheet bị sửa tay)
                meta["pxPcAt"] = nowS; meta["pxPcRows"] = ph.Count; meta["pxPcLoi"] = string.Join(" | ", errs);
            }
            catch (Exception e) { meta["pxPcLoi"] = e.Message.Length > 300 ? e.Message[..300] : e.Message; }
        }
        // 1.76: tài khoản PC dùng trên điện thoại — lỗi không làm hỏng lần đẩy tồn
        try { var pa = await PushAccountsAsync(ct); res.Errors.AddRange(pa.Warnings.Select(w => "Tài khoản điện thoại: " + w)); }
        catch (Exception e) { res.Errors.Add("Tài khoản điện thoại: " + e.Message); }
        var stamps = SyncTables.Select(t => meta.GetValueOrDefault("lastPushAt_" + t) is { } v ? S(v) : "").Where(x => x.Length > 0).OrderBy(x => x, StringComparer.Ordinal).ToList();
        meta["lastPushAt"] = stamps.Count == SyncTables.Length ? stamps[0] : oldLast;
        await WriteMetaAsync(meta, ct);
        Status = status;
        _s.Audit.Log(_s.Session.Username, "Đồng bộ điện thoại: Đẩy lên", string.Join(", ", res.Rows.Select(kv => $"{kv.Key} {kv.Value} dòng")) + (res.Errors.Count > 0 ? " · lỗi: " + string.Join("; ", res.Errors) : ""));
        return res;
    }

    /// <summary>
    /// Đẩy tài khoản PC + quyền điện thoại lên tab PhanQuyen (1.76) — gộp với tài khoản tạo trên điện thoại; không đổi thì không ghi.
    /// Gọi trong mỗi lần đẩy và từ Hệ thống › Tài khoản › Điện thoại.
    /// </summary>
    public async Task<PhoneAccounts.Plan> PushAccountsAsync(CancellationToken ct = default)
    {
        await W.EnsureTabsAsync(new Dictionary<string, string[]?> { [PhoneAccounts.PqTab] = PhoneAccounts.PqCols }, ct);
        var cur = (await W.ReadTableAsync(PhoneAccounts.PqTab, ct)).Rows.Select(r => (IDictionary<string, JsonNode?>)r).ToList();
        var plan = PhoneAccounts.BuildPlan(cur, _s.Auth.UsersForPhone(), _s.PhoneAccounts.List(), Now());
        if (!PhoneAccounts.SameTable(cur, plan.Rows.Select(r => (IDictionary<string, JsonNode?>)r).ToList()))
        {
            await W.WriteTableAsync(PhoneAccounts.PqTab, PhoneAccounts.PqCols, plan.Rows.Select(r => (IDictionary<string, JsonNode?>)r), ct);
            _s.Audit.Log(_s.Session.Username, "Điện thoại: cập nhật tài khoản", $"{plan.Info.Count(i => i.FromPc)} tài khoản PC, {plan.Info.Count(i => !i.FromPc)} tài khoản riêng điện thoại");
        }
        return plan;
    }

    private async Task<Dictionary<string, JsonNode?>> ReadMetaAsync(CancellationToken ct)
    {
        var meta = new Dictionary<string, JsonNode?>();
        try { foreach (var r in (await W.ReadTableAsync(Tab.Meta, ct)).Rows) { var k = PhoneWebApp.Str(r.GetValueOrDefault("key")); if (k.Length > 0) meta[k] = r.GetValueOrDefault("value")?.DeepClone(); } } catch { /* Meta chưa có — tạo mới */ }
        return meta;
    }
    private Task WriteMetaAsync(Dictionary<string, JsonNode?> meta, CancellationToken ct) =>
        W.WriteTableAsync(Tab.Meta, new[] { "key", "value" }, meta.Select(kv => (IDictionary<string, JsonNode?>)new Dictionary<string, JsonNode?> { ["key"] = kv.Key, ["value"] = kv.Value?.DeepClone() }), ct);

    // ================================================================ KÉO PHIẾU (pull)
    public sealed record PullResult(int NewPhieu, int ErrPhieu, Dictionary<string, int> ByModule, List<string> Errors, HashSet<string> Modules);

    public static readonly string[] PullModules = { "M01", "M01VT", "M01VTB", "M02", "M03" };
    /// <summary>Kéo phiếu "Chưa xử lý" ở PhieuXuat / PhieuXuatItems → nhập vào đúng module → đánh dấu "Đã xử lý"; mỗi module nhập riêng, module lỗi không chặn module khác.</summary>
    public async Task<PullResult> PullAsync(CancellationToken ct = default)
    {
        await W.EnsureTabsAsync(new Dictionary<string, string[]?> { [Tab.Meta] = null, [Tab.Px] = PhoneWebApp.PhieuHeaders, [Tab.Pxi] = PhoneWebApp.PhieuItemHeaders }, ct);
        var px = await W.ReadTableAsync(Tab.Px, ct);
        var pending = px.Rows.Where(h => { var t = PhoneWebApp.Str(h.GetValueOrDefault("trangThai")).Trim(); return PhoneWebApp.Str(h.GetValueOrDefault("id")).Trim().Length > 0 && t != DaXuLy && !t.StartsWith("Lỗi", StringComparison.Ordinal); }).ToList();
        var res = new PullResult(0, 0, new(), new(), new());
        if (pending.Count == 0) return res;
        var pxi = await W.ReadTableAsync(Tab.Pxi, ct);
        var itemsBy = new Dictionary<string, List<PhoneItem>>();
        foreach (var it in pxi.Rows)
        {
            var pid = PhoneWebApp.Str(it.GetValueOrDefault("phieuId")).Trim(); if (pid.Length == 0) continue;
            string G(string k) => PhoneWebApp.Str(it.GetValueOrDefault(k));
            if (!itemsBy.TryGetValue(pid, out var l)) itemsBy[pid] = l = new();
            l.Add(new PhoneItem(G("maHang"), G("tenHang"), G("phieuNhap"), G("soLo"), G("size"), PhoneWebApp.Num(it.GetValueOrDefault("soKien")), PhoneWebApp.Num(it.GetValueOrDefault("soLuongXuat")), G("itemKey"), it.GetValueOrDefault("rowIndex")?.DeepClone(), G("hopDong"), G("viTri")));
        }
        var nowUtc = Now().ToUniversalTime(); var stamp = NowIso();
        var list = new List<(PhoneSlip P, JsonNode? Row)>(); var loi = new List<(JsonNode?, string, IDictionary<string, string>)>();
        foreach (var h in pending)
        {
            var id = PhoneWebApp.Str(h["id"]).Trim(); var ngay = PhoneWebApp.Iso(h.GetValueOrDefault("ngayTao"), nowUtc);
            var items = (itemsBy.GetValueOrDefault(id) ?? new()).Where(i => i.SoLuongXuat > 0).ToList();
            if (items.Count == 0)
            {
                if (DateTime.TryParse(ngay, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t0) && (nowUtc - t0).TotalMinutes > 10)
                    loi.Add((h.GetValueOrDefault("_rowNumber"), id, new Dictionary<string, string> { ["trangThai"] = LoiThieuDong, ["xuLyLuc"] = stamp }));
                continue;
            }
            var mod = PhoneWebApp.Str(h.GetValueOrDefault("module")).Trim(); if (!PullModules.Contains(mod)) mod = "M02";
            list.Add((new PhoneSlip(id, PhoneWebApp.Str(h.GetValueOrDefault("maPhieu")).Trim(), ngay, PhoneWebApp.Str(h.GetValueOrDefault("nguoiTao")), PhoneWebApp.Str(h.GetValueOrDefault("ghiChu")), mod, PhoneWebApp.Str(h.GetValueOrDefault("lsxXuat")).Trim(), items), h.GetValueOrDefault("_rowNumber")));
        }
        var done = new HashSet<string>(); int nNew = 0;
        foreach (var mod in PullModules)
        {
            var part = list.Where(x => x.P.Module == mod).Select(x => x.P).ToList(); if (part.Count == 0) continue;
            try
            {
                var r = mod switch { "M02" => ApplyM02(part), "M01" => ApplyM01(part), "M01VT" => ApplyM01Vt(part), "M01VTB" => ApplyM01Vtb(part), _ => ApplyM03(part) };
                nNew += r.NewCount; res.ByModule[mod] = r.NewCount; res.Errors.AddRange(r.Errors.Select(e => $"{mod}: {e}"));
                foreach (var x in r.Imported) done.Add(x);
                if (r.NewCount > 0 || r.Imported.Count > 0) res.Modules.Add(mod);
            }
            catch (Exception e) { res.Errors.Add($"{mod}: {e.Message}"); }
        }
        await W.MarkRowsAsync(Tab.Px, list.Where(x => done.Contains(x.P.Id)).Select(x => (x.Row, x.P.Id, (IDictionary<string, string>)new Dictionary<string, string> { ["trangThai"] = DaXuLy, ["xuLyLuc"] = stamp })), ct);
        if (loi.Count > 0) await W.MarkRowsAsync(Tab.Px, loi, ct);
        if (nNew > 0) { ReloadServices(res.Modules); _s.Audit.Log(_s.Session.Username, "Đồng bộ điện thoại: Nhận phiếu", string.Join(", ", res.ByModule.Select(kv => $"{kv.Key} {kv.Value} phiếu"))); }
        return res with { NewPhieu = nNew, ErrPhieu = loi.Count };
    }

    /// <summary>Kéo phiếu rồi đẩy lại bảng đã tick + bảng vừa nhận phiếu (điện thoại thấy tồn mới ngay).</summary>
    public async Task<(PullResult Pull, PushResult Push)> SyncAsync(bool pull = true, CancellationToken ct = default)
    {
        InSync.Value = true;
        using var _perf = KhoLanh.Core.TroLy.Perf.Measure("Đồng bộ điện thoại", pull ? "kéo + đẩy" : "đẩy");
        var p = pull ? await PullAsync(ct) : new PullResult(0, 0, new(), new(), new());
        if (pull)
        {
            // 1.55: hộp thư yêu cầu lịch xuất từ điện thoại + lưu trữ phiếu cũ (lỗi không chặn phần còn lại)
            try { var lx = await PullLichXuatRequestsAsync(ct); if (lx.Err > 0) p.Errors.AddRange(lx.Notes.Select(n => "Yêu cầu lịch xuất: " + n)); }
            catch (Exception e) { p.Errors.Add("Yêu cầu lịch xuất: " + e.Message); }
            await MaybeArchiveAsync(ct);
        }
        var push = await PushAsync(Pick.Union(p.Modules), ct);
        return (p, push);
    }

    /// <summary>Sau khi ghi thẳng sheet: cho các service đang nạp đọc lại (màn hình mở lại sẽ thấy số mới).</summary>
    public event Action<IReadOnlyCollection<string>>? DataChanged;
    private void ReloadServices(HashSet<string> mods)
    {
        void Try(Action a) { try { a(); } catch { } }
        if (mods.Contains("M01") && _s.AnAn.Loaded) Try(_s.AnAn.Load);
        if (mods.Contains("M01VT") && _s.ViTri.Loaded) Try(_s.ViTri.Load);
        if (mods.Contains("M01VTB")) Try(_s.Vtb.Load);
        if (mods.Contains("M02") && _s.Gui.Loaded) Try(_s.Gui.Load);
        if (mods.Contains("M03")) Try(_s.SoKho.Load);
        DataChanged?.Invoke(mods);
    }

    // ---- M02 · Tồn kho gửi: trừ Số kiện + Trọng lượng theo tỉ lệ, đánh dấu "Đã trừ theo phiếu xuất", lưu PhieuXuat / PhieuXuatItems
    public static string M2Key(params string?[] v) => string.Join("|", v.Select(x => (x ?? "").Trim()));
    public ApplyResult ApplyM02(List<PhoneSlip> list)
    {
        var du = Rows("ton-kho-gui", "DuLieuChinh"); var headers = Rows("ton-kho-gui", "PhieuXuat"); var items = Rows("ton-kho-gui", "PhieuXuatItems");
        var existing = headers.Select(h => S(h["id"])).ToHashSet(); var usedMa = headers.Select(h => S(h["maPhieu"])).ToHashSet();
        var mm = JsonCell("ton-kho-gui", "DuLieuMeta") ?? new(); var loadAt = Loose(mm["importedAt"]); if (double.IsNaN(loadAt)) loadAt = 0; var loadDate = S(mm["dataDate"]);
        var idx = new Dictionary<string, JsonObject>(); foreach (var r in du) idx[M2Key(S(r["Mã hàng"]), S(r["Số lô"]), S(r["Size"]), S(r["Phiếu nhập/Voucher"]))] = r;
        var imported = new List<string>(); var n = 0;
        foreach (var ph in list)
        {
            if (existing.Contains(ph.Id)) { imported.Add(ph.Id); continue; }
            var ma = ph.MaPhieu.Length > 0 ? ph.MaPhieu : ph.Id;
            if (usedMa.Contains(ma)) { var k = 2; while (usedMa.Contains($"{ma}-{k}")) k++; ma = $"{ma}-{k}"; }
            usedMa.Add(ma);
            foreach (var it in ph.Items)
            {
                var qty = it.SoLuongXuat; double tlTru = 0; JsonNode? skLuc = it.SoKien;
                if (idx.TryGetValue(M2Key(it.MaHang, it.SoLo, it.Size, it.PhieuNhap), out var r))
                {
                    var skGoc = PhoneWebApp.Num(r["Số kiện"]); var tlGoc = PhoneWebApp.Num(r["Trọng Lượng (KG)"]); skLuc = skGoc;
                    tlTru = skGoc > 0 ? tlGoc * (qty / skGoc) : 0;
                    r["Số kiện"] = skGoc - qty; r["Trọng Lượng (KG)"] = tlGoc - tlTru; if (qty != 0) r["Đã trừ theo phiếu xuất"] = 1;
                }
                items.Add(new JsonObject { ["phieuId"] = ph.Id, ["maHang"] = it.MaHang, ["tenHang"] = it.TenHang, ["phieuNhap"] = it.PhieuNhap, ["soLo"] = it.SoLo, ["size"] = it.Size, ["soKien"] = skLuc?.DeepClone(), ["soLuongXuat"] = qty, ["trongLuongTru"] = tlTru });
            }
            headers.Insert(0, new JsonObject { ["id"] = ph.Id, ["maPhieu"] = ma, ["ngayTao"] = ph.NgayTao, ["nguon"] = "Điện thoại", ["nguoiTao"] = ph.NguoiTao, ["ghiChu"] = ph.GhiChu, ["loadAt"] = loadAt, ["loadDate"] = loadDate });
            existing.Add(ph.Id); imported.Add(ph.Id); n++;
        }
        if (n > 0) Src.Write("ton-kho-gui", new Dictionary<string, JsonArray?> { ["DuLieuChinh"] = Arr(du), ["PhieuXuat"] = Arr(headers), ["PhieuXuatItems"] = Arr(items) });
        return new ApplyResult(imported, n, new());
    }
    private static JsonArray Arr(IEnumerable<JsonObject> rows) => new(rows.Select(r => (JsonNode)(r.Parent is null ? r : (JsonObject)r.DeepClone())).ToArray());

    // ---- M01 · Tồn kho An An: phiếu vào "Xuất Hàng" (sheet PhieuXuat 1 ô JSON), trừ ngay "Tồn cuối" của đúng dòng Load Data
    public ApplyResult ApplyM01(List<PhoneSlip> list)
    {
        var m = JsonCell("ton-kho-an-an", "LoadDataMeta"); if (m is null) return new ApplyResult(new(), 0, new() { "Chưa có dữ liệu Tồn kho An An." });
        var headers = (m["headers"] as JsonArray ?? new()).Select(h => S(h)).ToList();
        var tonIdx = headers.FindIndex(h => Fuzzy(h) == Fuzzy("Tồn cuối"));
        if (tonIdx < 0) return new ApplyResult(new(), 0, new() { "Tồn kho An An không có cột \"Tồn cuối\" — không nhập được phiếu." });
        var itemIdIdx = headers.FindIndex(h => Fuzzy(h) == "item id"); var maIdx = headers.FindIndex(h => Fuzzy(h) == Fuzzy("Mã hàng"));
        var rows = Rows("ton-kho-an-an", "LoadData"); var tk = "c" + tonIdx;
        var payload = JsonCell("ton-kho-an-an", "PhieuXuat") ?? new JsonObject();
        var slips = payload["slips"] as JsonArray ?? new JsonArray(); payload["slips"] = slips;
        var seq = payload["seq"] is { } sq && Loose(sq) is var sv && double.IsFinite(sv) ? (int)sv : 1;
        int Find(PhoneItem it)
        {
            var key = it.ItemKey;
            if (key.StartsWith("id:") && itemIdIdx >= 0) { var want = key[3..]; return rows.FindIndex(r => S(r["c" + itemIdIdx]) == want); }
            var iText = key.StartsWith("r:") ? key[2..] : S(it.RowIndex);
            if (!int.TryParse(iText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) || i < 0 || i >= rows.Count) return -1;
            if (maIdx >= 0 && it.MaHang.Length > 0) { var cm = Regex.Replace(S(rows[i]["c" + maIdx]), @"^\s*TPC-", "", RegexOptions.IgnoreCase); if (S(rows[i]["c" + maIdx]) != it.MaHang.Trim() && cm != it.MaHang.Trim()) return -1; }
            return i;
        }
        var imported = new List<string>(); var errors = new List<string>(); var n = 0; var now = NowIso();
        foreach (var ph in list)
        {
            if (slips.OfType<JsonObject>().Any(s => S(s["phoneId"]) == ph.Id)) { imported.Add(ph.Id); continue; }
            var lines = new List<(int I, PhoneItem It)>(); var missing = new List<string>();
            foreach (var it in ph.Items) { var i = Find(it); if (i < 0) missing.Add(it.MaHang); else lines.Add((i, it)); }
            if (missing.Count > 0) { errors.Add($"Phiếu {(ph.MaPhieu.Length > 0 ? ph.MaPhieu : ph.Id)}: không khớp dòng Load Data ({string.Join(", ", missing)})"); continue; }
            foreach (var (i, it) in lines) { var cur = Loose(rows[i][tk]); rows[i][tk] = (double.IsNaN(cur) ? 0 : cur) - it.SoLuongXuat; }
            slips.Insert(0, new JsonObject
            {
                ["id"] = "PX-" + seq++.ToString("0000", CultureInfo.InvariantCulture), ["moduleId"] = "dienthoai",
                ["moduleLabel"] = "📱 Điện thoại" + (ph.NguoiTao.Length > 0 ? " — " + ph.NguoiTao : "") + (ph.MaPhieu.Length > 0 ? $" ({ph.MaPhieu})" : ""),
                ["createdAt"] = ph.NgayTao.Length > 0 ? ph.NgayTao : now,
                ["lines"] = new JsonArray(lines.Select(l => (JsonNode)new JsonObject { ["i"] = l.I, ["maHang"] = l.It.MaHang, ["tenHang"] = l.It.TenHang, ["size"] = l.It.Size, ["hopDong"] = l.It.HopDong, ["qty"] = l.It.SoLuongXuat }).ToArray()),
                ["deducted"] = true, ["deductedAt"] = now, ["phoneId"] = ph.Id, ["phoneMaPhieu"] = ph.MaPhieu, ["nguoiTao"] = ph.NguoiTao, ["ghiChu"] = ph.GhiChu,
            });
            imported.Add(ph.Id); n++;
        }
        payload["seq"] = seq;
        if (n > 0) Src.Write("ton-kho-an-an", new Dictionary<string, JsonArray?> { ["LoadData"] = Arr(rows), ["PhieuXuat"] = Cell(payload) });
        return new ApplyResult(imported, n, errors);
    }

    // ---- M01VT · Tồn theo vị trí: trừ "SL Tồn" đúng dòng (khóa Mã|Lô|Vị trí#n), phiếu vào ViTriPhieuDienThoai
    public ApplyResult ApplyM01Vt(List<PhoneSlip> list)
    {
        var rows = Rows("ton-vi-tri-an-an", "ViTri"); if (rows.Count == 0) return new ApplyResult(new(), 0, new());
        var keys = VtKeys(rows); var idx = new Dictionary<string, int>(); for (var i = 0; i < keys.Count; i++) if (keys[i].Length > 0) idx.TryAdd(keys[i], i);
        var payload = JsonCell("ton-vi-tri-an-an", "ViTriPhieuDienThoai") ?? new JsonObject();
        var slips = payload["slips"] as JsonArray ?? new JsonArray(); payload["slips"] = slips;
        var seq = payload["seq"] is { } sq && Loose(sq) is var sv && double.IsFinite(sv) ? (int)sv : 1;
        var imported = new List<string>(); var errors = new List<string>(); var n = 0; var now = NowIso();
        foreach (var ph in list)
        {
            if (slips.OfType<JsonObject>().Any(s => S(s["phoneId"]) == ph.Id)) { imported.Add(ph.Id); continue; }
            var ix = ph.Items.Select(it => idx.TryGetValue(it.ItemKey.Trim(), out var i) ? i : -1).ToList();
            if (ix.Any(i => i < 0)) { errors.Add($"Phiếu {(ph.MaPhieu.Length > 0 ? ph.MaPhieu : ph.Id)}: có dòng không còn khớp tồn theo vị trí"); continue; }
            var lines = ph.Items.Select((it, k) =>
            {
                var r = rows[ix[k]]; var cur = Loose(r["SL Tồn"]); r["SL Tồn"] = (double.IsNaN(cur) ? 0 : cur) - it.SoLuongXuat;
                return (JsonNode)new JsonObject { ["key"] = it.ItemKey, ["maGoc"] = r["Mã hàng"]?.DeepClone(), ["ten"] = r["Tên hàng"]?.DeepClone(), ["dvt"] = r["ĐVT"]?.DeepClone(), ["lo"] = r["Lô (Batch)"]?.DeepClone(), ["vt"] = r["Vị trí"]?.DeepClone(), ["tl"] = r["T.Lượng"]?.DeepClone(), ["qty"] = it.SoLuongXuat };
            }).ToArray();
            slips.Insert(0, new JsonObject { ["id"] = "VT-" + seq++.ToString("0000", CultureInfo.InvariantCulture), ["phoneId"] = ph.Id, ["maPhieu"] = ph.MaPhieu, ["nguoiTao"] = ph.NguoiTao, ["ghiChu"] = ph.GhiChu, ["createdAt"] = ph.NgayTao.Length > 0 ? ph.NgayTao : now, ["lines"] = new JsonArray(lines), ["deducted"] = true, ["deductedAt"] = now });
            imported.Add(ph.Id); n++;
        }
        payload["seq"] = seq;
        if (n > 0) Src.Write("ton-vi-tri-an-an", new Dictionary<string, JsonArray?> { ["ViTri"] = Arr(rows), ["ViTriPhieuDienThoai"] = Cell(payload) });
        return new ApplyResult(imported, n, errors);
    }

    // ---- M01VTB · Vị Trí Bột (VtbService)
    public ApplyResult ApplyM01Vtb(List<PhoneSlip> list)
    {
        var v = _s.Vtb; v.Load();
        var (imp, n, errs) = v.ApplyPhone(list.Select(p => new VtbService.PhonePhieu(p.Id, p.MaPhieu, p.NgayTao, p.NguoiTao, p.GhiChu, p.Items.Select(i => new VtbService.PhoneItem(i.ItemKey, i.SoLuongXuat)).ToList())));
        return new ApplyResult(imp, n, errs);
    }

    // ---- M03 · NXT Bột/Sốt: phiếu điện thoại → DÒNG XUẤT trong sổ cái (nguon "dt", phieuId = id phiếu — không trừ 2 lần)
    public ApplyResult ApplyM03(List<PhoneSlip> list)
    {
        const double Eps = 1e-9;
        var lines = SkLines(); var r = ApplyPhonePhieu("M03", lines, list, Now);
        if (r.NewLines.Count > 0)
        {
            var sk = _s.SoKho; sk.Load();
            sk.Commit(r.NewLines, Array.Empty<string>(), Array.Empty<SkLine>(), "xuat", $"Phiếu xuất từ điện thoại: {r.NewCount} phiếu");
        }
        _ = Eps;
        return new ApplyResult(r.Imported, r.NewCount, r.Errors);
    }
    public sealed record SoKhoPhone(List<SkLine> NewLines, List<string> Imported, int NewCount, List<string> Errors);
    /// <summary>applyPhonePhieu của so-kho-core.js: phiếu vượt tồn / mất lô → bỏ qua (ghi lỗi), phiếu đã có (theo phieuId) → chỉ ghi nhận.</summary>
    public static SoKhoPhone ApplyPhonePhieu(string mod, List<SkLine> lines, IEnumerable<PhoneSlip> list, Func<DateTime> now)
    {
        const double Eps = 1e-9;
        var have = lines.Where(l => l.Nguon == "dt" && l.PhieuId.Length > 0).Select(l => l.PhieuId).ToHashSet();
        var work = lines.ToList(); var newLines = new List<SkLine>(); var imported = new List<string>(); var errors = new List<string>(); var n = 0;
        foreach (var ph in list)
        {
            var pid = ph.Id.Trim();
            if (pid.Length == 0) { errors.Add($"Phiếu {(ph.MaPhieu.Length > 0 ? ph.MaPhieu : "(không mã)")}: thiếu mã nội bộ (id) — bỏ qua, để phiếu ở lại \"Chưa xử lý\""); continue; }
            if (have.Contains(pid)) { imported.Add(ph.Id); continue; }
            var byKey = new Dictionary<string, SkLot>(); foreach (var l in SoKhoCore.Compute(work).Lots) byKey[LotKey(l)] = l;
            var want = new Dictionary<string, double>(); var order = new List<string>(); var bad = "";
            foreach (var it in ph.Items)
            {
                if (!byKey.ContainsKey(it.ItemKey)) { if (bad.Length == 0) bad = "không còn lô " + it.ItemKey; continue; }
                var q = it.SoLuongXuat; if (!(q > 0)) { if (bad.Length == 0) bad = "số lượng xuất không hợp lệ"; continue; }
                if (!want.ContainsKey(it.ItemKey)) { want[it.ItemKey] = 0; order.Add(it.ItemKey); } want[it.ItemKey] += q;
            }
            foreach (var k in order) { var l = byKey[k]; var avail = l.Net > Eps ? l.Net : l.Sl; if (want[k] > avail + 1e-6 && bad.Length == 0) bad = $"vượt tồn lô {l.MaHang} {l.Size} ({l.ViTri})"; }
            if (bad.Length > 0) { errors.Add($"Phiếu {(ph.MaPhieu.Length > 0 ? ph.MaPhieu : ph.Id)}: {bad}"); continue; }
            var lucOk = DateTime.TryParse(ph.NgayTao, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lucUtc);
            var local = lucOk ? lucUtc.ToLocalTime() : now();
            var ngay = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); var luc = ph.NgayTao.Length > 0 ? ph.NgayTao : now().ToUniversalTime().ToString("o");
            var note = "Xuất từ điện thoại · " + string.Join(" · ", new[] { ph.NguoiTao.Trim(), lucOk ? local.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture) : "" }.Where(x => x.Length > 0)) + (ph.MaPhieu.Length > 0 ? " · " + ph.MaPhieu : "") + (ph.GhiChu.Trim().Length > 0 ? " | " + ph.GhiChu.Trim() : "");
            foreach (var k in order)
            {
                var l = byKey[k]; var q = want[k]; JsonNode? sl, nt;
                if (l.Net > Eps) { nt = q; sl = l.Sl > Eps ? R2(q * l.Sl / l.Net) : ""; } else { sl = q; nt = ""; }
                var L = SoKhoCore.NormLine(new JsonObject
                {
                    ["loai"] = "xuat", ["ngay"] = ngay, ["chungTu"] = ph.MaPhieu, ["phieuId"] = ph.Id, ["viTri"] = l.ViTri, ["maHang"] = l.MaHang, ["size"] = l.Size, ["soLuong"] = sl, ["net"] = nt,
                    ["netDv"] = l.Sl > Eps && l.Net > Eps ? R4(l.Net / l.Sl) : "", ["hopDong"] = mod == "M03" ? ph.LsxXuat : "", ["hanDung"] = l.HanDung, ["ngayNhap"] = l.NgayNhap, ["ghiChu"] = note, ["nguon"] = "dt", ["nguoi"] = ph.NguoiTao.Trim(), ["luc"] = luc,
                });
                newLines.Add(L); work.Add(L);
            }
            have.Add(pid); imported.Add(ph.Id); n++;
        }
        return new SoKhoPhone(newLines, imported, n, errors);
    }
}

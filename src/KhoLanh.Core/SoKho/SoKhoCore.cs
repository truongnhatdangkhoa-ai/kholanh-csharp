using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.SoKho;

/// <summary>
/// Một dòng SỔ CÁI của NXT Bột/Sốt (sheet SoCai, database nxt-bot-sot — dùng chung với app cũ).
/// Số = double? (null = trống), ngày = yyyy-mm-dd hoặc "".
/// </summary>
public sealed class SkLine
{
    public string Id = "", Loai = "nhap", Ngay = "", ChungTu = "", PhieuId = "", ViTri = "", ViTriDen = "", MaHang = "", Size = "";
    public double? SoLuong, Net, NetDv;
    public string HopDong = "", HanDung = "", NgayNhap = "", GhiChu = "", Nguon = "pc", Nguoi = "", Luc = "";
    public JsonObject Extra = new();
    public List<string> Bad = new();
    public SkLine Clone() { var c = (SkLine)MemberwiseClone(); c.Extra = (JsonObject)Extra.DeepClone(); c.Bad = new(Bad); return c; }
}

/// <summary>Một lô còn tồn: Vị trí · Mã hàng · Size · Hạn dùng · Ngày nhập (sl / net có thể âm).</summary>
public sealed record SkLot(string ViTri, string MaHang, string Size, string HanDung, string NgayNhap, string HopDong, string ChungTu, double Sl, double Net);

/// <summary>Mỗi lần một dòng làm đổi tồn (kind: tondau · nhap · xuat · chuyen_di · chuyen_den · dieuchinh).</summary>
public sealed record SkEffect(SkLine Line, string Kind, string ViTri, string MaHang, string Size, string HanDung, string NgayNhap, double DSl, double DNet, bool Neg = false)
{
    public string HopDong => Line.HopDong;
}

/// <summary>
/// LÕI SỔ KHO — NXT Bột/Sốt (viết lại theo js/so-kho-core.js của app cũ). HÀM THUẦN, không đọc / ghi đĩa.
/// MÔ HÌNH: mọi nhập – xuất – chuyển – điều chỉnh – tồn đầu là DÒNG trong 1 sổ cái; mọi báo cáo TÍNH từ sổ cái nên không lệch nhau.
/// LÔ = (Vị trí · Mã hàng · Size · Hạn dùng · Ngày nhập). Xuất không ghi hạn dùng / ngày nhập → trừ FIFO (ngày nhập cũ trước, rồi hạn dùng gần trước).
/// Tồn được âm (cảnh báo). Mốc TỒN ĐẦU (tondau) muộn nhất ≤ ngày tính: dòng cũ hơn mốc bị bỏ qua.
/// </summary>
public static class SoKhoCore
{
    public const string Db = "nxt-bot-sot", SLedger = "SoCai", STemplates = "SoKhoMau", SNorms = "SoKhoDinhMuc", SOutputs = "SoKhoSanLuong", SConfig = "SoKhoCauHinh", SHist = "_SoKhoLichSu";
    public static readonly Dictionary<string, string> LoaiTen = new() { ["tondau"] = "Tồn đầu", ["nhap"] = "Nhập kho", ["xuat"] = "Xuất kho", ["chuyen"] = "Chuyển vị trí", ["dieuchinh"] = "Điều chỉnh" };
    public static readonly string[] LineFields = { "id", "loai", "ngay", "chungTu", "phieuId", "viTri", "viTriDen", "maHang", "size", "soLuong", "net", "netDv", "hopDong", "hanDung", "ngayNhap", "ghiChu", "nguon", "nguoi", "luc" };
    public const double Eps = 1e-9;
    public const int HistDays = 30;

    // ------------------------------------------------------------------ đọc số / ngày (KLNum / KLDate của app cũ)

    /// <summary>Số: null = ô trống, NaN = không đọc được. Chuẩn app: phẩy nghìn, chấm thập phân; kiểu Việt khi không mơ hồ; "(100)" = −100.</summary>
    public static double? ParseNum(JsonNode? n)
    {
        if (n is null) return null;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return double.IsFinite(d) ? d : double.NaN;
            if (v.TryGetValue<bool>(out _)) return double.NaN;
        }
        return ParseNum(n is JsonValue sv && sv.TryGetValue<string>(out var s0) ? s0 : n.ToJsonString());
    }
    public static double? ParseNum(string? v)
    {
        if (v is null) return null;
        var s = Regex.Replace(v, @"[\s   ]+", "");
        if (s is "" or "-" or "—" or "–") return null;
        s = s.Replace('−', '-');
        var neg = false;
        if (s.Length >= 2 && s[0] == '(' && s[^1] == ')') { neg = true; s = s[1..^1]; }
        var sign = "";
        if (s.Length > 0 && (s[0] == '-' || s[0] == '+')) { sign = s[..1]; s = s[1..]; }
        if (!Regex.IsMatch(s, @"^[\d.,]+$") || !Regex.IsMatch(s, @"\d") || Regex.IsMatch(s, @"^[.,]|[.,]$")) return double.NaN;
        int nC = s.Count(c => c == ','), nD = s.Count(c => c == '.');
        if (nC > 0 && nD > 0)
        {
            int c = s.LastIndexOf(','), d = s.LastIndexOf('.');
            if (c > d) { if (nC > 1) return double.NaN; s = s.Replace(".", "").Replace(',', '.'); }
            else { if (nD > 1) return double.NaN; s = s.Replace(",", ""); }
        }
        else if (nC > 0)
        {
            if (Regex.IsMatch(s, @"^\d{1,3}(,\d{3})+$")) s = s.Replace(",", "");
            else if (nC == 1) s = s.Replace(',', '.');
            else return double.NaN;
        }
        else if (nD > 1)
        {
            if (Regex.IsMatch(s, @"^\d{1,3}(\.\d{3})+$")) s = s.Replace(".", "");
            else return double.NaN;
        }
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.IsFinite(x)) return double.NaN;
        if (sign == "-") x = -x;
        return neg ? -x : x;
    }
    private static double? Num(JsonNode? n) { var x = ParseNum(n); return x is { } d && double.IsNaN(d) ? null : x; }

    /// <summary>Ngày → yyyy-mm-dd ("" nếu không đọc được): dd/mm/yyyy (năm 2 số = 20yy, kèm giờ được), yyyy-mm-dd, số ngày Excel.</summary>
    public static string ParseDate(JsonNode? n)
    {
        if (n is null) return "";
        if (n is JsonValue v && v.TryGetValue<double>(out var d))
        {
            if (!double.IsFinite(d) || d < 20000 || d > 80000) return "";
            var t = DateTime.UnixEpoch.AddMilliseconds(Math.Round((d - 25569) * 86400000));
            return Iso(t);
        }
        return ParseDate(S(n));
    }
    public static string ParseDate(string? s0)
    {
        var s = (s0 ?? "").Trim(); if (s.Length == 0) return "";
        var m = Regex.Match(s, @"^(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{4}|\d{2})(?:[ T]+\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AaPp][Mm])?)?$");
        if (m.Success) { var y = int.Parse(m.Groups[3].Value); if (m.Groups[3].Value.Length == 2) y += 2000; return Ymd(y, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)); }
        m = Regex.Match(s, @"^(\d{4})[\/\-.](\d{1,2})[\/\-.](\d{1,2})(?:[ T].*)?$");
        return m.Success ? Ymd(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : "";
    }
    private static string Ymd(int y, int m, int d) { try { return Iso(new DateTime(y, m, d)); } catch { return ""; } }
    public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string AddDays(string iso, int n) => Iso(DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(n));
    public static string Dmy(string iso) { var p = (iso ?? "").Split('-'); return p.Length == 3 ? $"{p[2]}/{p[1]}/{p[0]}" : iso ?? ""; }
    public static double R4(double n) => Math.Floor(n * 10000 + 0.5) / 10000;   // = Math.round của JS
    public static double R2(double n) => Math.Floor(n * 100 + 0.5) / 100;

    public static string S(JsonNode? n)
    {
        if (n is null) return "";
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s.Trim();
            if (v.TryGetValue<double>(out var d)) return d.ToString("0.############", CultureInfo.InvariantCulture);
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        }
        return n.ToJsonString().Trim();
    }

    /// <summary>"6 THÁNG" / "1 NĂM" / "90 NGÀY" / "2 TUẦN" + NSX → hạn dùng (tháng ngắn lấy ngày cuối tháng). Không khớp → "".</summary>
    public static string HsdFromText(string text, string nsx)
    {
        var t = TroLy.TroLyMaHang.Fold(text).Trim();
        var m = Regex.Match(t, @"^(\d{1,4})\s*(thang|nam|ngay|tuan|month|months|year|years|day|days|week|weeks)$");
        if (!m.Success) return "";
        var iso = ParseDate(nsx); if (iso.Length == 0) return "";
        var n = int.Parse(m.Groups[1].Value); var u = m.Groups[2].Value; if (n <= 0) return "";
        var d0 = DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (u.StartsWith("thang") || u.StartsWith("month") || u.StartsWith("nam") || u.StartsWith("year"))
        {
            var add = u.StartsWith("nam") || u.StartsWith("year") ? n * 12 : n;
            var first = new DateTime(d0.Year, d0.Month, 1).AddMonths(add);
            return Iso(new DateTime(first.Year, first.Month, Math.Min(d0.Day, DateTime.DaysInMonth(first.Year, first.Month))));
        }
        return Iso(d0.AddDays(u.StartsWith("tuan") || u.StartsWith("week") ? n * 7 : n));
    }

    private static int _seq;
    public static string NewId() => LichXuat.LichXuatCore.B36(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) + LichXuat.LichXuatCore.B36(Interlocked.Increment(ref _seq) % 1679616) + Guid.NewGuid().ToString("N")[..4];

    // ------------------------------------------------------------------ dòng sổ

    /// <summary>Chuẩn hóa 1 dòng (nhập tay / Excel / đọc từ sheet). Ô có nội dung mà không đọc được → ghi vào Bad để báo lỗi.</summary>
    public static SkLine NormLine(JsonObject raw)
    {
        var bad = new List<string>();
        double? ChkNum(JsonNode? v, string ten) { var x = ParseNum(v); if (x is { } d && double.IsNaN(d)) { var s = S(v); if (s is not ("" or "-" or "—")) bad.Add(ten + " không đọc được"); return null; } return x; }
        string ChkDate(JsonNode? v, string ten) { var d = ParseDate(v); if (d.Length == 0 && S(v).Length > 0) bad.Add($"{ten} \"{(S(v).Length > 30 ? S(v)[..30] : S(v))}\" không hợp lệ"); return d; }
        var L = new SkLine
        {
            Id = S(raw["id"]) is { Length: > 0 } id ? id : NewId(), Loai = S(raw["loai"]).ToLowerInvariant(), Ngay = ParseDate(raw["ngay"]),
            ChungTu = S(raw["chungTu"]), PhieuId = S(raw["phieuId"]), ViTri = S(raw["viTri"]), ViTriDen = S(raw["viTriDen"]), MaHang = S(raw["maHang"]), Size = S(raw["size"]),
            SoLuong = ChkNum(raw["soLuong"], "Số lượng"), Net = ChkNum(raw["net"], "Tổng (kg)"), NetDv = ChkNum(raw["netDv"], "Net/đơn vị"),
            HopDong = S(raw["hopDong"]), HanDung = ChkDate(raw["hanDung"], "Hạn dùng"), NgayNhap = ChkDate(raw["ngayNhap"], "Ngày nhập"), GhiChu = S(raw["ghiChu"]),
            Nguon = S(raw["nguon"]) is { Length: > 0 } ng ? ng : "pc", Nguoi = S(raw["nguoi"]), Luc = S(raw["luc"]), Bad = bad,
        };
        if (!LoaiTen.ContainsKey(L.Loai)) L.Loai = "nhap";
        if (L.Net is null && L.SoLuong is { } q && L.NetDv is { } nd) L.Net = R4(q * nd);
        JsonObject? ex = raw["extra"] as JsonObject;
        if (ex is null && S(raw["extraJson"]) is { Length: > 0 } ej) { try { ex = JsonNode.Parse(ej) as JsonObject; } catch { } }
        if (ex is not null) foreach (var (k, v) in ex) if (k.StartsWith("x_", StringComparison.Ordinal)) L.Extra[k] = v?.DeepClone();
        if (L.Loai is "nhap" or "tondau" && L.NgayNhap.Length == 0) L.NgayNhap = L.Ngay;
        return L;
    }

    public static JsonObject ToStore(SkLine L)
    {
        static JsonNode? N(double? d) => d is { } x ? JsonValue.Create(x) : JsonValue.Create("");
        return new JsonObject
        {
            ["id"] = L.Id, ["loai"] = L.Loai, ["ngay"] = L.Ngay, ["chungTu"] = L.ChungTu, ["phieuId"] = L.PhieuId, ["viTri"] = L.ViTri, ["viTriDen"] = L.ViTriDen,
            ["maHang"] = L.MaHang, ["size"] = L.Size, ["soLuong"] = N(L.SoLuong), ["net"] = N(L.Net), ["netDv"] = N(L.NetDv), ["hopDong"] = L.HopDong,
            ["hanDung"] = L.HanDung, ["ngayNhap"] = L.NgayNhap, ["ghiChu"] = L.GhiChu, ["nguon"] = L.Nguon, ["nguoi"] = L.Nguoi, ["luc"] = L.Luc,
            ["extraJson"] = L.Extra.Count > 0 ? L.Extra.ToJsonString() : "",
        };
    }

    /// <summary>Dòng trùng id (sửa tay Excel chép nguyên dòng): chỉ giữ dòng đầu.</summary>
    public static (List<SkLine> Lines, List<SkLine> Dropped) DedupeById(IEnumerable<SkLine> lines)
    {
        var seen = new HashSet<string>(); var o = new List<SkLine>(); var dr = new List<SkLine>();
        foreach (var l in lines) { if (l.Id.Length > 0 && !seen.Add(l.Id)) { dr.Add(l); continue; } o.Add(l); }
        return (o, dr);
    }

    /// <summary>Sắp: ngày → Tồn đầu trước các dòng cùng ngày → giờ ghi (luc) → thứ tự gốc.</summary>
    public static List<SkLine> SortLines(IEnumerable<SkLine> lines) =>
        lines.Select((l, i) => (l, i)).OrderBy(x => x.l.Ngay, StringComparer.Ordinal).ThenBy(x => x.l.Loai == "tondau" ? 0 : 1)
            .ThenBy(x => x.l.Luc, StringComparer.Ordinal).ThenBy(x => x.i).Select(x => x.l).ToList();

    public sealed record Check(string Level, List<string> Errs, List<string> Warns)
    {
        public IEnumerable<string> Msgs => Errs.Concat(Warns);
    }

    public static (HashSet<string> Ma, HashSet<string> Vt) KnownSets(IEnumerable<SkLine> lines)
    {
        var ma = new HashSet<string>(); var vt = new HashSet<string>();
        foreach (var l in lines) { if (l.MaHang.Length > 0) ma.Add(l.MaHang); if (l.ViTri.Length > 0) vt.Add(l.ViTri); if (l.ViTriDen.Length > 0) vt.Add(l.ViTriDen); }
        return (ma, vt);
    }

    /// <summary>Kiểm tra 1 dòng: chỉ BẮT BUỘC mã hàng + ngày; thiếu số lượng / Net chỉ nhắc.</summary>
    public static Check Validate(SkLine L, (HashSet<string> Ma, HashSet<string> Vt)? known = null)
    {
        var e = new List<string>(); var w = new List<string>();
        if (L.MaHang.Length == 0) e.Add("Thiếu mã hàng");
        if (L.Ngay.Length == 0) e.Add("Ngày không hợp lệ");
        e.AddRange(L.Bad);
        if (L.SoLuong is null && L.Net is null) w.Add("Chưa có số lượng / Net — cập nhật sau");
        else if (L.Loai is "nhap" or "tondau" && L.Net is null) w.Add("Chưa có Net (kg) — cập nhật sau");
        else if (L.Loai is "nhap" or "tondau" && L.SoLuong is null) w.Add("Chưa có số lượng — cập nhật sau");
        if (L.Loai == "chuyen")
        {
            if (L.ViTri.Length == 0) e.Add("Thiếu vị trí đi"); if (L.ViTriDen.Length == 0) e.Add("Thiếu vị trí đến");
            if (L.ViTri.Length > 0 && L.ViTri == L.ViTriDen) e.Add("Vị trí đi và đến giống nhau");
        }
        else if (L.ViTri.Length == 0) w.Add("Chưa ghi vị trí");
        if (L.Loai != "dieuchinh")
        {
            if (L.SoLuong is < 0) e.Add("Số lượng âm (dùng Điều chỉnh)");
            if (L.Net is < 0) e.Add("Net âm (dùng Điều chỉnh)");
        }
        else if (L.GhiChu.Length == 0) w.Add("Nên ghi lý do điều chỉnh");
        if (L.SoLuong is { } q && L.NetDv is { } nd && L.Net is { } nt && Math.Abs(nt - q * nd) > Math.Max(0.01, Math.Abs(nt) * 0.001)) w.Add("Tổng khác Số lượng × Net");
        if (L.HanDung.Length > 0 && L.NgayNhap.Length > 0 && string.CompareOrdinal(L.HanDung, L.NgayNhap) < 0) w.Add("Hạn dùng trước ngày nhập");
        if (known is { } k)
        {
            if (k.Ma.Count > 0 && L.MaHang.Length > 0 && !k.Ma.Contains(L.MaHang)) w.Add("Mã hàng mới");
            if (k.Vt.Count > 0 && L.ViTri.Length > 0 && !k.Vt.Contains(L.ViTri)) w.Add("Vị trí mới");
        }
        return new Check(e.Count > 0 ? "err" : w.Count > 0 ? "warn" : "ok", e, w);
    }

    // ------------------------------------------------------------------ TÍNH LÔ

    public sealed record Spill(double Sl, double Net, double ExSl, bool LoFound, string HanDung, string NgayNhap);
    public sealed record Result(List<SkLot> Lots, List<SkEffect> Effects, string Base, List<(SkLine Line, Spill Spill)> Spills);

    public static string SnapBase(IEnumerable<SkLine> lines, string? upTo)
    {
        var b = "";
        foreach (var l in lines) if (l.Loai == "tondau" && l.Ngay.Length > 0 && (string.IsNullOrEmpty(upTo) || string.CompareOrdinal(l.Ngay, upTo) <= 0) && string.CompareOrdinal(l.Ngay, b) > 0) b = l.Ngay;
        return b;
    }

    private sealed class Lot { public string HanDung = "", NgayNhap = "", HopDong = "", ChungTu = ""; public double Sl, Net; public int Ord; }
    private sealed class Grp { public string ViTri = "", MaHang = "", Size = ""; public List<Lot> Lots = new(); }
    private sealed record Part(string HanDung, string NgayNhap, string HopDong, string ChungTu, double Sl, double Net, bool FromEx = false, bool Neg = false);

    /// <summary>Tính tồn từng lô đến hết ngày <paramref name="upTo"/> (null = tất cả), kèm danh sách tác động từng dòng.</summary>
    public static Result Compute(IEnumerable<SkLine> all, string? upTo = null)
    {
        var lines = all as IList<SkLine> ?? all.ToList();
        var spills = new List<(SkLine, Spill)>();
        var bs = SnapBase(lines, upTo);
        var use = SortLines(lines.Where(l => l.Ngay.Length > 0 && (string.IsNullOrEmpty(upTo) || string.CompareOrdinal(l.Ngay, upTo) <= 0)
            && (bs.Length == 0 || string.CompareOrdinal(l.Ngay, bs) >= 0) && !(bs.Length > 0 && l.Loai == "tondau" && l.Ngay != bs)));
        var groups = new Dictionary<string, Grp>(); var order = new List<Grp>(); var effects = new List<SkEffect>(); var ord = 0;
        Grp G(string vt, string ma, string sz)
        {
            var k = vt + "\u0001" + ma + "\u0001" + sz;
            if (!groups.TryGetValue(k, out var g)) { g = new Grp { ViTri = vt, MaHang = ma, Size = sz }; groups[k] = g; order.Add(g); }
            return g;
        }
        Lot? LotOf(Grp g, string hd, string nn, bool create)
        {
            foreach (var l in g.Lots) if (l.HanDung == hd && l.NgayNhap == nn) return l;
            if (!create) return null;
            var lot = new Lot { HanDung = hd, NgayNhap = nn, Ord = ++ord }; g.Lots.Add(lot); return lot;
        }
        void Add(SkLine L, string vt, string hd, string nn, double dSl, double dNet, string kind, string hopDong, string chungTu)
        {
            var g = G(vt, L.MaHang, L.Size); var lot = LotOf(g, hd, nn, true)!;
            lot.Sl = R4(lot.Sl + dSl); lot.Net = R4(lot.Net + dNet);
            if (hopDong.Length > 0 && lot.HopDong.Length == 0) lot.HopDong = hopDong; if (chungTu.Length > 0 && lot.ChungTu.Length == 0) lot.ChungTu = chungTu;
            effects.Add(new SkEffect(L, kind, vt, L.MaHang, L.Size, hd, nn, dSl, dNet));
        }
        IEnumerable<Lot> Fifo(Grp g) => g.Lots.OrderBy(a => a.NgayNhap, StringComparer.Ordinal).ThenBy(a => a.HanDung, StringComparer.Ordinal).ThenBy(a => a.Ord);
        (List<Part> Parts, Spill? Spill) Take(Grp g, double? qSl, double? qNet, string hd, string nn)
        {
            if (qSl is { } s0 && s0 <= Eps && qNet is > Eps) qSl = null;
            var outp = new List<Part>(); double remSl = qSl ?? 0, remNet = qNet ?? 0; bool hasSl = qSl is not null, hasNet = qNet is not null;
            var seq = new List<Lot>(); Lot? ex = null;
            if (hd.Length > 0 || nn.Length > 0) { ex = LotOf(g, hd, nn, false); if (ex is not null) seq.Add(ex); }
            foreach (var l in Fifo(g)) if (!seq.Contains(l)) seq.Add(l);
            var totalSl = qSl ?? 0;
            for (var i = 0; i < seq.Count && ((hasSl && remSl > Eps) || (!hasSl && hasNet && remNet > Eps)); i++)
            {
                var lot = seq[i]; double tSl, tNet;
                if (hasSl)
                {
                    if (lot.Sl <= Eps) continue;
                    tSl = Math.Min(lot.Sl, remSl);
                    tNet = hasNet ? (totalSl > 0 ? qNet!.Value * (tSl / totalSl) : 0) : (lot.Sl > Eps ? lot.Net * (tSl / lot.Sl) : 0);
                    remSl = R4(remSl - tSl);
                }
                else
                {
                    if (lot.Net <= Eps) continue;
                    tNet = Math.Min(lot.Net, remNet); tSl = lot.Sl > Eps ? lot.Sl * (tNet / lot.Net) : 0; remNet = R4(remNet - tNet);
                }
                outp.Add(new Part(lot.HanDung, lot.NgayNhap, lot.HopDong, lot.ChungTu, R4(tSl), R4(tNet), ex is not null && lot == ex));
                lot.Sl = R4(lot.Sl - tSl); lot.Net = R4(lot.Net - tNet);
            }
            var restSl = hasSl ? Math.Max(0, remSl) : 0;
            var restNet = hasNet ? (hasSl ? Math.Max(0, qNet!.Value - outp.Sum(p => p.Net)) : Math.Max(0, remNet)) : 0;
            if (restSl > Eps || restNet > Eps || outp.Count == 0)
            {
                var neg = LotOf(g, "", "", true)!;
                neg.Sl = R4(neg.Sl - restSl); neg.Net = R4(neg.Net - restNet);
                outp.Add(new Part("", "", "", "", R4(restSl), R4(restNet), Neg: true));
            }
            Spill? sp = null;
            if (hd.Length > 0 || nn.Length > 0)
            {
                double spSl = 0, spNet = 0, exSl = 0;
                foreach (var p in outp) { if (p.FromEx) exSl += p.Sl; else if (!p.Neg) { spSl += p.Sl; spNet += p.Net; } }
                if (spSl > Eps || spNet > Eps) sp = new Spill(R4(spSl), R4(spNet), R4(exSl), ex is not null, hd, nn);
            }
            return (outp, sp);
        }
        foreach (var L in use)
        {
            var sl = L.SoLuong; var nt = L.Net;
            switch (L.Loai)
            {
                case "tondau" or "nhap": Add(L, L.ViTri, L.HanDung, L.NgayNhap, sl ?? 0, nt ?? 0, L.Loai, L.HopDong, L.ChungTu); break;
                case "xuat":
                {
                    var (parts, sp) = Take(G(L.ViTri, L.MaHang, L.Size), sl, nt, L.HanDung, L.NgayNhap);
                    foreach (var p in parts) effects.Add(new SkEffect(L, "xuat", L.ViTri, L.MaHang, L.Size, p.HanDung, p.NgayNhap, -p.Sl, -p.Net, p.Neg));
                    if (sp is not null) spills.Add((L, sp));
                    break;
                }
                case "chuyen":
                {
                    var (parts, _) = Take(G(L.ViTri, L.MaHang, L.Size), sl, nt, L.HanDung, L.NgayNhap);
                    foreach (var p in parts)
                    {
                        effects.Add(new SkEffect(L, "chuyen_di", L.ViTri, L.MaHang, L.Size, p.HanDung, p.NgayNhap, -p.Sl, -p.Net));
                        var lot = LotOf(G(L.ViTriDen, L.MaHang, L.Size), p.HanDung, p.NgayNhap, true)!;
                        lot.Sl = R4(lot.Sl + p.Sl); lot.Net = R4(lot.Net + p.Net);
                        if (p.HopDong.Length > 0 && lot.HopDong.Length == 0) lot.HopDong = p.HopDong; if (p.ChungTu.Length > 0 && lot.ChungTu.Length == 0) lot.ChungTu = p.ChungTu;
                        effects.Add(new SkEffect(L, "chuyen_den", L.ViTriDen, L.MaHang, L.Size, p.HanDung, p.NgayNhap, p.Sl, p.Net));
                    }
                    break;
                }
                case "dieuchinh":
                {
                    var g = G(L.ViTri, L.MaHang, L.Size); double dS = sl ?? 0, dN = nt ?? 0;
                    void Out(List<Part> ps) { foreach (var p in ps) effects.Add(new SkEffect(L, "dieuchinh", L.ViTri, L.MaHang, L.Size, p.HanDung, p.NgayNhap, -p.Sl, -p.Net)); }
                    if (dS >= 0 && dN >= 0) Add(L, L.ViTri, L.HanDung, L.NgayNhap, dS, dN, "dieuchinh", L.HopDong, L.ChungTu);
                    else if ((dS < 0 && dN > 0) || (dS > 0 && dN < 0))
                    {
                        if (dS > 0 || dN > 0) Add(L, L.ViTri, L.HanDung, L.NgayNhap, Math.Max(0, dS), Math.Max(0, dN), "dieuchinh", L.HopDong, L.ChungTu);
                        Out(Take(g, Math.Max(0, -dS), Math.Max(0, -dN), L.HanDung, L.NgayNhap).Parts);
                    }
                    else Out(Take(g, sl is null ? null : Math.Abs(sl.Value), nt is null ? null : Math.Abs(nt.Value), L.HanDung, L.NgayNhap).Parts);
                    break;
                }
            }
        }
        var lots = new List<SkLot>();
        foreach (var g in order) foreach (var l in g.Lots)
            if (Math.Abs(l.Sl) > Eps || Math.Abs(l.Net) > Eps) lots.Add(new SkLot(g.ViTri, g.MaHang, g.Size, l.HanDung, l.NgayNhap, l.HopDong, l.ChungTu, l.Sl, l.Net));
        return new Result(lots, effects, bs, spills);
    }

    /// <summary>Phát sinh của mọi dòng đến hết <paramref name="to"/>, KỂ CẢ dòng cũ hơn mốc tồn đầu (mỗi đoạn giữa 2 mốc tính bằng quy tắc tồn của đoạn đó).</summary>
    public static List<SkEffect> EffectsAll(IList<SkLine> lines, string? to)
    {
        var bases = lines.Where(l => l.Loai == "tondau" && l.Ngay.Length > 0 && (string.IsNullOrEmpty(to) || string.CompareOrdinal(l.Ngay, to) <= 0)).Select(l => l.Ngay).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var ups = bases.Select(b => AddDays(b, -1)).ToList(); ups.Add(to ?? "");
        var o = new List<SkEffect>();
        foreach (var u in ups) o.AddRange(Compute(lines, u.Length > 0 ? u : null).Effects);
        return o;
    }

    // ------------------------------------------------------------------ báo cáo

    public static readonly Dictionary<string, string> DimLabel = new() { ["viTri"] = "Vị trí", ["maHang"] = "Mã hàng", ["size"] = "Size", ["hanDung"] = "Hạn dùng", ["ngayNhap"] = "Ngày nhập", ["hopDong"] = "Hợp đồng" };
    private static string Dim(SkLot l, string d) => d switch { "viTri" => l.ViTri, "maHang" => l.MaHang, "size" => l.Size, "hanDung" => l.HanDung, "ngayNhap" => l.NgayNhap, "hopDong" => l.HopDong, _ => "" };
    private static string Dim(SkEffect e, string d) => d switch { "viTri" => e.ViTri, "maHang" => e.MaHang, "size" => e.Size, "hanDung" => e.HanDung, "ngayNhap" => e.NgayNhap, "hopDong" => e.HopDong, _ => "" };

    public sealed class StockRow { public Dictionary<string, string> Keys = new(); public double Sl, Net; public int Lots; public string this[string k] => Keys.GetValueOrDefault(k, ""); }

    /// <summary>Tồn tại 1 ngày, gom theo các chiều (mặc định Mã hàng + Size).</summary>
    public static List<StockRow> Stock(IList<SkLine> lines, string? upTo, params string[] dims)
    {
        if (dims.Length == 0) dims = new[] { "maHang", "size" };
        var m = new Dictionary<string, StockRow>();
        foreach (var l in Compute(lines, upTo).Lots)
        {
            var k = string.Join("\u0001", dims.Select(d => Dim(l, d)));
            if (!m.TryGetValue(k, out var r)) { r = new StockRow(); foreach (var d in dims) r.Keys[d] = Dim(l, d); m[k] = r; }
            r.Sl = R4(r.Sl + l.Sl); r.Net = R4(r.Net + l.Net); r.Lots++;
        }
        return Order(m.Values, dims, r => r.Keys);
    }
    private static List<T> Order<T>(IEnumerable<T> rows, string[] dims, Func<T, Dictionary<string, string>> keys)
    {
        var l = rows.ToList();
        l.Sort((a, b) => { foreach (var d in dims) { var c = string.CompareOrdinal(keys(a).GetValueOrDefault(d, ""), keys(b).GetValueOrDefault(d, "")); if (c != 0) return c; } return 0; });
        return l;
    }

    public sealed class NxtRow
    {
        public Dictionary<string, string> Keys = new();
        public double DauSl, DauNet, NhapSl, NhapNet, XuatSl, XuatNet, DcSl, DcNet, CuoiSl, CuoiNet;
        public string this[string k] => Keys.GetValueOrDefault(k, "");
    }

    /// <summary>NXT trong kỳ [from, to]: Tồn đầu + Nhập − Xuất ± Điều chỉnh = Tồn cuối (cột Điều chỉnh gồm cả chuyển vị trí / chốt tồn trong kỳ).</summary>
    public static List<NxtRow> Nxt(IList<SkLine> lines, string from, string to, params string[] dims)
    {
        if (dims.Length == 0) dims = new[] { "maHang", "size" };
        var m = new Dictionary<string, NxtRow>();
        NxtRow Row(Func<string, string> get) { var k = string.Join("\u0001", dims.Select(get)); if (!m.TryGetValue(k, out var r)) { r = new NxtRow(); foreach (var d in dims) r.Keys[d] = get(d); m[k] = r; } return r; }
        foreach (var o in Stock(lines, AddDays(from, -1), dims)) { var r = Row(o.Keys.GetValueOrDefault!); r.DauSl = o.Sl; r.DauNet = o.Net; }
        foreach (var o in Stock(lines, to, dims)) { var r = Row(o.Keys.GetValueOrDefault!); r.CuoiSl = o.Sl; r.CuoiNet = o.Net; }
        foreach (var e in EffectsAll(lines, to))
        {
            if (string.CompareOrdinal(e.Line.Ngay, from) < 0 || string.CompareOrdinal(e.Line.Ngay, to) > 0) continue;
            if (e.Kind is not ("nhap" or "xuat")) continue;
            var r = Row(d => Dim(e, d));
            if (e.Kind == "nhap") { r.NhapSl = R4(r.NhapSl + e.DSl); r.NhapNet = R4(r.NhapNet + e.DNet); }
            else { r.XuatSl = R4(r.XuatSl - e.DSl); r.XuatNet = R4(r.XuatNet - e.DNet); }
        }
        var o2 = new List<NxtRow>();
        foreach (var r in m.Values)
        {
            r.DcSl = R4(r.CuoiSl - r.DauSl - r.NhapSl + r.XuatSl); r.DcNet = R4(r.CuoiNet - r.DauNet - r.NhapNet + r.XuatNet);
            if (new[] { r.DauSl, r.DauNet, r.NhapSl, r.NhapNet, r.XuatSl, r.XuatNet, r.DcSl, r.DcNet, r.CuoiSl, r.CuoiNet }.Any(x => Math.Abs(x) > Eps)) o2.Add(r);
        }
        return Order(o2, dims, r => r.Keys);
    }

    public static NxtRow NxtTotal(IEnumerable<NxtRow> rows)
    {
        var t = new NxtRow();
        foreach (var r in rows)
        {
            t.DauSl = R4(t.DauSl + r.DauSl); t.DauNet = R4(t.DauNet + r.DauNet); t.NhapSl = R4(t.NhapSl + r.NhapSl); t.NhapNet = R4(t.NhapNet + r.NhapNet);
            t.XuatSl = R4(t.XuatSl + r.XuatSl); t.XuatNet = R4(t.XuatNet + r.XuatNet); t.DcSl = R4(t.DcSl + r.DcSl); t.DcNet = R4(t.DcNet + r.DcNet);
            t.CuoiSl = R4(t.CuoiSl + r.CuoiSl); t.CuoiNet = R4(t.CuoiNet + r.CuoiNet);
        }
        return t;
    }

    /// <summary>Dòng phát sinh (chi tiết) trong kỳ, không gồm Tồn đầu.</summary>
    public static List<SkLine> Detail(IEnumerable<SkLine> lines, string from, string to, string? loai = null) =>
        SortLines(lines.Where(l => string.CompareOrdinal(l.Ngay, from) >= 0 && string.CompareOrdinal(l.Ngay, to) <= 0 && l.Loai != "tondau" && (string.IsNullOrEmpty(loai) || loai == "all" || l.Loai == loai)));

    public static List<SkLot> Negatives(IList<SkLine> lines, string? upTo = null) => Compute(lines, upTo).Lots.Where(l => l.Sl < -Eps || l.Net < -Eps).ToList();

    /// <summary>Lô sắp / đã hết hạn dùng (còn tồn dương): hạn ≤ hôm nay + <paramref name="days"/> ngày.</summary>
    public static List<SkLot> Expiring(IList<SkLine> lines, string today, int days) =>
        Compute(lines).Lots.Where(l => (l.Sl > Eps || l.Net > Eps) && l.HanDung.Length > 0 && string.CompareOrdinal(l.HanDung, AddDays(today, days)) <= 0)
            .OrderBy(l => l.HanDung, StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------------ chốt tồn

    /// <summary>Chốt tồn: tồn cuối ngày trước <paramref name="date"/> → các dòng Tồn đầu ngày đó (lô âm không chuyển sang, báo số lô bỏ). Đã có Tồn đầu ngày đó → Exists.</summary>
    public static (List<SkLine> Lines, int Dropped, bool Exists) Snapshot(IList<SkLine> lines, string date, string nguoi, string? ghiChu = null)
    {
        if (lines.Any(l => l.Loai == "tondau" && l.Ngay == date)) return (new(), 0, true);
        var t = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"); var dropped = 0; var o = new List<SkLine>();
        foreach (var l in Compute(lines, AddDays(date, -1)).Lots)
        {
            if (l.Sl < -Eps || l.Net < -Eps) dropped++;
            if (l.Sl <= Eps && l.Net <= Eps) continue;
            o.Add(NormLine(new JsonObject
            {
                ["loai"] = "tondau", ["ngay"] = date, ["viTri"] = l.ViTri, ["maHang"] = l.MaHang, ["size"] = l.Size,
                ["soLuong"] = l.Sl > Eps ? l.Sl : "", ["net"] = l.Net > Eps ? l.Net : "", ["hopDong"] = l.HopDong, ["hanDung"] = l.HanDung, ["ngayNhap"] = l.NgayNhap,
                ["chungTu"] = l.ChungTu, ["ghiChu"] = ghiChu ?? "Chốt tồn cuối kỳ trước", ["nguon"] = "chot", ["nguoi"] = nguoi, ["luc"] = t,
            }));
        }
        return (o, dropped, false);
    }

    // ------------------------------------------------------------------ kiểm kê thực tế

    public sealed record KkCount(string MaHang, string Size, string ViTri, double? Dem);
    public sealed record KkRow(string MaHang, string Size, string ViTri, double So, double Dem, double Chenh, string Kind, double Net, bool ThieuNet);

    /// <summary>So SỐ ĐẾM với TỒN SỔ (theo Mã hàng + Size + Vị trí) → dòng ĐIỀU CHỈNH (dương = thêm, âm = bớt). Net điều chỉnh = chênh × (Net ÷ SL của dòng sổ).</summary>
    public static (List<KkRow> Rows, List<SkLine> Adj, int NKhop, int NLech, double Thua, double Thieu) KiemKePlan(IList<SkLine> lines, string ngay, IEnumerable<KkCount> counts, string nguoi, string? chungTu = null)
    {
        var by = Stock(lines, ngay, "maHang", "size", "viTri").ToDictionary(r => r["maHang"] + "\u0001" + r["size"] + "\u0001" + r["viTri"]);
        var rows = new List<KkRow>(); var adj = new List<SkLine>(); var seen = new HashSet<string>(); int nKhop = 0, nLech = 0; double thua = 0, thieu = 0;
        foreach (var c in counts)
        {
            if (c.Dem is not { } dem || double.IsNaN(dem)) continue;
            var k = c.MaHang.Trim() + "\u0001" + c.Size.Trim() + "\u0001" + c.ViTri.Trim(); if (!seen.Add(k)) continue;
            by.TryGetValue(k, out var s); var so = s?.Sl ?? 0; var net = s?.Net ?? 0; var chenh = R4(dem - so);
            var kind = Math.Abs(chenh) <= Eps ? "khop" : chenh > 0 ? (s is not null && so > Eps ? "thua" : "moi") : "thieu";
            double? perUnit = so > Eps ? net / so : null; var dNet = perUnit is { } pu ? R4(chenh * pu) : 0;
            rows.Add(new KkRow(c.MaHang.Trim(), c.Size.Trim(), c.ViTri.Trim(), so, dem, chenh, kind, dNet, chenh > Eps && perUnit is null));
            if (kind == "khop") { nKhop++; continue; }
            nLech++; if (chenh > 0) thua = R4(thua + chenh); else thieu = R4(thieu + chenh);
            adj.Add(NormLine(new JsonObject
            {
                ["loai"] = "dieuchinh", ["ngay"] = ngay, ["viTri"] = c.ViTri.Trim(), ["maHang"] = c.MaHang.Trim(), ["size"] = c.Size.Trim(), ["soLuong"] = chenh, ["net"] = dNet,
                ["chungTu"] = chungTu ?? "KK-" + ngay.Replace("-", ""), ["ghiChu"] = $"Kiểm kê {Dmy(ngay)}: sổ {so.ToString(CultureInfo.InvariantCulture)}, đếm {dem.ToString(CultureInfo.InvariantCulture)}", ["nguon"] = "kiem-ke", ["nguoi"] = nguoi,
            }));
        }
        return (rows, adj, nKhop, nLech, thua, thieu);
    }

    /// <summary>Dòng xuất chỉ định lô (có hạn dùng / ngày nhập) mà lô đó không đủ → phần thiếu lấy sang lô khác (FIFO). Chỉ cảnh báo.</summary>
    public static List<(SkLine Line, Spill Spill)> XuatLoCheck(IList<SkLine> lines, IList<SkLine> draft)
    {
        var ids = draft.Where(l => l.Loai == "xuat" && (l.HanDung.Length > 0 || l.NgayNhap.Length > 0)).Select(l => l.Id).ToHashSet();
        if (ids.Count == 0) return new();
        return Compute(lines.Concat(draft).ToList()).Spills.Where(s => ids.Contains(s.Line.Id)).ToList();
    }

    // ------------------------------------------------------------------ định mức

    public sealed record DmUnit(string Key, string Ten, string DimKind, Func<double, double, double> F);
    public static readonly DmUnit[] DmUnits =
    {
        new("kg/100kg", "kg nguyên liệu / 100 kg thành phẩm", "net", (tp, dm) => tp * dm / 100),
        new("kg/kg", "kg nguyên liệu / 1 kg thành phẩm", "net", (tp, dm) => tp * dm),
        new("bao/100kg", "bao / 100 kg thành phẩm", "sl", (tp, dm) => tp * dm / 100),
        new("bao/tan", "bao / tấn thành phẩm", "sl", (tp, dm) => tp * dm / 1000),
    };
    public static DmUnit Unit(string k) => DmUnits.FirstOrDefault(u => u.Key == k) ?? DmUnits[0];
    public static string StatusOf(double? pct, double tol) => pct is not { } p ? "chua" : Math.Abs(p) <= tol + Eps ? "dat" : p < 0 ? "tietkiem" : p <= tol * 2 + Eps ? "chuY" : "vuot";
    public static readonly Dictionary<string, string> StatusTen = new() { ["chua"] = "Chưa đủ số liệu", ["dat"] = "Đạt", ["tietkiem"] = "Tiết kiệm", ["chuY"] = "Vượt nhẹ", ["vuot"] = "Vượt", ["gan"] = "Gần định mức", ["ok"] = "Trong định mức", ["thieu"] = "Chưa kiểm được" };

    public sealed record Norm(string Id, string HopDong, string MaHang, string Size, double? DinhMuc, string DonVi, string GhiChu);
    public sealed record Output(string Id, string HopDong, double? ThanhPham, string Khach, string GhiChu);
    public sealed record DmRow(string HopDong, string Khach, double ThanhPham, string MaHang, string Size, double DinhMuc, string DonVi, string DimKind, double DuKien, double ThucXuat, double Chenh, double? Pct, string Status, string NormId);
    public sealed record DmMissing(string HopDong, string Khach, double? ThanhPham, string MaHang, string Size, double ThucXuatSl, double ThucXuatNet);

    /// <summary>Định mức theo LSX: (thành phẩm × định mức) so với thực xuất bột / sốt cho LSX đó. missing = LSX có xuất nhưng chưa có định mức cho mã đó.</summary>
    public static (List<DmRow> Rows, List<DmMissing> Missing) DinhMuc(IList<SkLine> lines, IEnumerable<Norm> norms, IEnumerable<Output> outputs, double tol = 3, string from = "", string to = "9999-12-31")
    {
        var used = new Dictionary<string, (string Hd, string Ma, string Sz, double Sl, double Net)>();
        foreach (var e in EffectsAll(lines, to))
        {
            if (e.Kind != "xuat" || e.Line.HopDong.Length == 0) continue;
            if (from.Length > 0 && string.CompareOrdinal(e.Line.Ngay, from) < 0) continue; if (string.CompareOrdinal(e.Line.Ngay, to) > 0) continue;
            var k = e.Line.HopDong + "\u0001" + e.MaHang + "\u0001" + e.Size;
            var u = used.GetValueOrDefault(k, (e.Line.HopDong, e.MaHang, e.Size, 0, 0));
            used[k] = (u.Hd, u.Ma, u.Sz, R4(u.Sl - e.DSl), R4(u.Net - e.DNet));
        }
        var outBy = new Dictionary<string, Output>(); foreach (var o in outputs) if (o.HopDong.Trim().Length > 0) outBy[o.HopDong.Trim()] = o;
        var covered = new HashSet<string>(); var rows = new List<DmRow>();
        foreach (var n in norms)
        {
            var ma = n.MaHang.Trim(); var sz = n.Size.Trim(); if (ma.Length == 0 || n.DinhMuc is not { } dm) continue; var un = Unit(n.DonVi.Trim());
            var targets = n.HopDong.Trim().Length > 0 ? new List<string> { n.HopDong.Trim() } : outBy.Keys.ToList();
            foreach (var hd in targets)
            {
                if (!outBy.TryGetValue(hd, out var o) || o.ThanhPham is not { } tp) continue;
                double sSl = 0, sNet = 0;
                foreach (var (k, u) in used) if (u.Hd == hd && u.Ma == ma && (sz.Length == 0 || u.Sz == sz)) { sSl = R4(sSl + u.Sl); sNet = R4(sNet + u.Net); covered.Add(k); }
                var du = R2(un.F(tp, dm)); var real = un.DimKind == "net" ? sNet : sSl; var ch = R2(real - du); double? pct = du > 0 ? R2(ch / du * 100) : null;
                rows.Add(new DmRow(hd, o.Khach.Trim(), tp, ma, sz, dm, un.Key, un.DimKind, du, R2(real), ch, pct, StatusOf(pct, tol), n.Id));
            }
        }
        var missing = used.Where(x => !covered.Contains(x.Key)).Select(x => { outBy.TryGetValue(x.Value.Hd, out var o); return new DmMissing(x.Value.Hd, o?.Khach.Trim() ?? "", o?.ThanhPham, x.Value.Ma, x.Value.Sz, x.Value.Sl, x.Value.Net); }).ToList();
        rows.Sort((a, b) => string.CompareOrdinal(a.HopDong, b.HopDong) is var c && c != 0 ? c : string.CompareOrdinal(a.MaHang, b.MaHang));
        return (rows, missing);
    }

    public sealed record DmCheckRow(string HopDong, string MaHang, string Size, string DimKind, double Du, double Da, double Nay, double Sau, double? Pct, string St, string Why);

    /// <summary>Kiểm tra định mức lúc lưu phiếu xuất: mỗi (LSX × mã) trong phiếu → ok · gan (≥ 90%) · chuY · vuot · thieu (thiếu định mức / sản lượng). Chỉ cảnh báo.</summary>
    public static (List<DmCheckRow> Rows, int NBad, int NWarn) DinhMucCheck(IList<SkLine> lines, IList<SkLine> draft, IList<Norm> norms, IList<Output> outputs, double tol = 3)
    {
        var xs = draft.Where(l => l.Loai == "xuat" && l.HopDong.Length > 0).ToList(); var o = new List<DmCheckRow>(); int bad = 0, warn = 0;
        if (xs.Count == 0) return (o, 0, 0);
        var b = DinhMuc(lines, norms, outputs, tol).Rows; var a = DinhMuc(lines.Concat(draft).ToList(), norms, outputs, tol).Rows;
        foreach (var k in xs.Select(l => (Hd: l.HopDong, Ma: l.MaHang)).Distinct())
        {
            var ar = a.Where(r => r.HopDong == k.Hd && r.MaHang == k.Ma).ToList();
            if (ar.Count > 0)
                foreach (var r in ar)
                {
                    var before = b.FirstOrDefault(x => x.NormId == r.NormId && x.HopDong == r.HopDong)?.ThucXuat ?? 0;
                    double? ratio = r.DuKien > 0 ? r.ThucXuat / r.DuKien : null;
                    var st = r.Status is "vuot" or "chuY" ? r.Status : ratio is >= 0.9 - Eps ? "gan" : "ok";
                    o.Add(new DmCheckRow(r.HopDong, r.MaHang, r.Size, r.DimKind, r.DuKien, before, R2(r.ThucXuat - before), r.ThucXuat, r.Pct, st, ""));
                    if (st == "vuot") bad++; else if (st is "chuY" or "gan") warn++;
                }
            else
            {
                var hasNorm = norms.Any(n => n.MaHang.Trim() == k.Ma && (n.HopDong.Trim().Length == 0 || n.HopDong.Trim() == k.Hd) && n.DinhMuc is not null);
                o.Add(new DmCheckRow(k.Hd, k.Ma, "", "", 0, 0, 0, 0, null, "thieu", hasNorm ? "Chưa có sản lượng thành phẩm của LSX" : "Chưa có định mức cho mã này"));
            }
        }
        return (o, bad, warn);
    }

    // ------------------------------------------------------------------ lịch sử & hoàn tác (sheet _SoKhoLichSu)

    public sealed record HistRow(string OpId, string Luc, string Nguoi, string Loai, string MoTa, string Kind, string LineId, string Before, string After, string Undone);
    public sealed record Op(string OpId, string Luc, string Nguoi, string Loai, string MoTa, int Them, int Xoa, int Sua, bool Undone);

    public static string RowSig(JsonObject? r) =>
        string.Join("\u0001", LineFields.Append("extraJson").Select(f => r?[f] is { } v ? (v is JsonValue jv && jv.TryGetValue<double>(out var d) ? d.ToString(CultureInfo.InvariantCulture) : S(v)) : ""));

    /// <summary>1 thao tác ghi sổ → các dòng lịch sử (ảnh trước / sau của từng dòng sổ).</summary>
    public static List<HistRow> HistRows(string opId, string nguoi, string loai, string moTa, IEnumerable<JsonObject> added, IEnumerable<JsonObject> removed, IEnumerable<(JsonObject After, JsonObject Before)> updated, string nowIso)
    {
        var o = new List<HistRow>(); var mt = moTa.Length > 300 ? moTa[..300] : moTa;
        foreach (var r in added) o.Add(new HistRow(opId, nowIso, nguoi, loai, mt, "them", S(r["id"]), "", r.ToJsonString(), ""));
        foreach (var r in removed) o.Add(new HistRow(opId, nowIso, nguoi, loai, mt, "xoa", S(r["id"]), r.ToJsonString(), "", ""));
        foreach (var (a, b) in updated) o.Add(new HistRow(opId, nowIso, nguoi, loai, mt, "sua", S(a["id"]), b.ToJsonString(), a.ToJsonString(), ""));
        return o;
    }

    /// <summary>Các thao tác, mới nhất trước (cùng giờ → thao tác ghi sau đứng trước).</summary>
    public static List<Op> HistOps(IEnumerable<HistRow> rows) =>
        rows.Select((r, i) => (r, i)).GroupBy(x => x.r.OpId).Select(g => (Op: new Op(g.Key, g.First().r.Luc, g.First().r.Nguoi, g.First().r.Loai, g.First().r.MoTa,
            g.Count(x => x.r.Kind == "them"), g.Count(x => x.r.Kind == "xoa"), g.Count(x => x.r.Kind == "sua"), g.All(x => x.r.Undone.Length > 0)), At: g.Min(x => x.i)))
            .OrderByDescending(x => x.Op.Luc, StringComparer.Ordinal).ThenByDescending(x => x.At).Select(x => x.Op).ToList();

    public sealed record UndoPlanR(List<string> Remove, List<JsonObject> Restore, List<JsonObject> Update, List<(string Id, string Why)> Conflicts, int Skipped);

    /// <summary>Hoàn tác: đưa các dòng về ảnh trước — chỉ khi dòng chưa bị sửa tiếp (khác ảnh sau → xung đột, không ghi đè).</summary>
    public static UndoPlanR UndoPlan(IEnumerable<HistRow> opRows, IEnumerable<JsonObject> curStore)
    {
        var cur = new Dictionary<string, JsonObject>(); foreach (var r in curStore) if (S(r["id"]) is { Length: > 0 } id) cur[id] = r;
        var p = new UndoPlanR(new(), new(), new(), new(), 0); var skipped = 0;
        JsonObject? J(string s) => s.Length > 0 ? JsonNode.Parse(s) as JsonObject : null;
        foreach (var h in opRows)
        {
            cur.TryGetValue(h.LineId, out var now); var before = J(h.Before); var after = J(h.After);
            switch (h.Kind)
            {
                case "them":
                    if (now is null) { skipped++; break; }
                    if (RowSig(now) != RowSig(after)) { p.Conflicts.Add((h.LineId, "Dòng này đã được sửa sau đó")); break; }
                    p.Remove.Add(h.LineId); break;
                case "xoa":
                    if (now is not null) { if (RowSig(now) == RowSig(before)) skipped++; else p.Conflicts.Add((h.LineId, "Đã có dòng khác dùng cùng mã dòng này")); break; }
                    p.Restore.Add(before!); break;
                case "sua":
                    if (now is null) { p.Conflicts.Add((h.LineId, "Dòng này đã bị xóa sau đó")); break; }
                    if (RowSig(now) == RowSig(before)) { skipped++; break; }
                    if (RowSig(now) != RowSig(after)) { p.Conflicts.Add((h.LineId, "Dòng này đã được sửa tiếp sau đó")); break; }
                    p.Update.Add(before!); break;
            }
        }
        return p with { Skipped = skipped };
    }

    public static JsonObject HistToStore(HistRow h) => new()
    {
        ["opId"] = h.OpId, ["luc"] = h.Luc, ["nguoi"] = h.Nguoi, ["loai"] = h.Loai, ["mota"] = h.MoTa, ["kind"] = h.Kind, ["lineId"] = h.LineId, ["before"] = h.Before, ["after"] = h.After, ["undone"] = h.Undone,
    };
    public static HistRow HistFromStore(JsonObject o) => new(S(o["opId"]), S(o["luc"]), S(o["nguoi"]), S(o["loai"]), S(o["mota"]), S(o["kind"]), S(o["lineId"]), S(o["before"]), S(o["after"]), S(o["undone"]));

    // ------------------------------------------------------------------ phiếu nhập / xuất (gom sổ cái)

    public sealed record PnMonth(string Thang, int Phieu, int Dong, double Sl, double Net);
    public static List<PnMonth> PnThang(IEnumerable<SkLine> lines) =>
        lines.Where(l => l.Loai == "nhap" && l.Ngay.Length > 0).GroupBy(l => l.Ngay[..7]).OrderByDescending(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PnMonth(g.Key, g.Select(l => l.ChungTu.Length > 0 ? l.ChungTu : "#" + l.Ngay).Distinct().Count(), g.Count(), R4(g.Sum(l => l.SoLuong ?? 0)), R4(g.Sum(l => l.Net ?? 0)))).ToList();

    public sealed record PGroup(string Key, string Ct, string Ngay, string HopDong, int Dong, double Sl, double Net, string Nguon, List<SkLine> Lines);
    /// <summary>Phiếu = các dòng cùng loại gom theo số chứng từ (trống → theo ngày). Mới nhất trước.</summary>
    public static List<PGroup> Groups(IEnumerable<SkLine> lines, string loai) =>
        lines.Where(l => l.Loai == loai).GroupBy(l => l.ChungTu.Length > 0 ? l.ChungTu : "#" + l.Ngay)
            .Select(g => new PGroup(g.Key, g.First().ChungTu, g.Max(l => l.Ngay) ?? "", string.Join(", ", g.Select(l => l.HopDong).Where(x => x.Length > 0).Distinct()),
                g.Count(), R4(g.Sum(l => l.SoLuong ?? 0)), R4(g.Sum(l => l.Net ?? 0)), g.First().Nguon, g.ToList()))
            .OrderByDescending(p => p.Ngay, StringComparer.Ordinal).ThenByDescending(p => p.Ct, StringComparer.Ordinal).ToList();

    /// <summary>Số phiếu tự đặt: &lt;tiền tố&gt;-ddmm-NN (NN tăng dần trong ngày). Xuất: BS, nhập: NK.</summary>
    public static string NextSo(IEnumerable<SkLine> lines, string ngay, string prefix = "BS")
    {
        var pre = $"{prefix}-{ngay[8..10]}{ngay[5..7]}-"; var mx = 0;
        foreach (var l in lines) if (l.ChungTu.StartsWith(pre, StringComparison.Ordinal) && int.TryParse(l.ChungTu[pre.Length..], out var n) && n > mx) mx = n;
        return pre + (mx + 1).ToString("00");
    }
}

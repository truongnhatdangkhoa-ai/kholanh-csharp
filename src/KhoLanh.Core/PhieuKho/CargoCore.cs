using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KhoLanh.Core.KhangSinh;

namespace KhoLanh.Core.PhieuKho;

public sealed class CargoLine
{
    public string Loai { get; set; } = "";       // BTP / NL … (khối kháng sinh)
    public string Sn { get; set; } = "";         // size-quy cách "0040-6"
    public string Size { get; set; } = "";       // khối "khác": size gõ tay
    public string Pack { get; set; } = "";       // khối "khác": quy cách gõ tay ("10.0 kg x 1")
    public string Lo { get; set; } = "";         // lô nguyên liệu "22.05.26 L16"
    public string KsTay { get; set; } = "";      // số kết quả kháng sinh gõ tay (1–83)
    /// <summary>Số kiện THỰC TẾ từng dãy: khóa "1".."40" → số kiện.</summary>
    public Dictionary<string, string> Dai { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class CargoBlock
{
    public string Id { get; set; } = "";
    public string Loai { get; set; } = "ks";
    public string Ten { get; set; } = "";
    public string Ma { get; set; } = "";
    public string Tag { get; set; } = "";
    public List<CargoLine> Lines { get; set; } = new() { new CargoLine() };
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>1 cont = 1 Cargo list + 1 phiếu xuất kho (hàng tạm, không LSX).</summary>
public sealed class CargoRec
{
    public string Id { get; set; } = "";
    public string Loai { get; set; } = "xuat_thuan_phuoc";
    public string Ngay { get; set; } = "";
    public string SoPhieu { get; set; } = "";
    public string SoPhieuTuCap { get; set; } = "";
    public string Kho { get; set; } = "An An + Long An";
    public string NguoiNhan { get; set; } = "";
    public string ContXe { get; set; } = "";
    public string Seal { get; set; } = "";
    public string KhoNhan { get; set; } = "";
    public string ContSo { get; set; } = "";
    public string ContSoTuCap { get; set; } = "";
    public string CapNhat { get; set; } = "";
    public List<CargoBlock> Blocks { get; set; } = new() { new CargoBlock { Loai = "ks" } };
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class CargoLoai
{
    public string Code { get; set; } = "";
    public string Ten { get; set; } = "";
}
public sealed class CargoQc
{
    public string D { get; set; } = "";
    public string Label { get; set; } = "";
    public string Pack { get; set; } = "";
    public Dictionary<string, string> Pre { get; set; } = new();
}
/// <summary>Danh mục "Loại hàng" + "Size-quy cách" (sheet PNX_CargoCfg). Mã hàng = tiền tố 13 + size 4 + mã gói 4 + số kết quả KS 4 = 25 ký tự.</summary>
public sealed class CargoCfg
{
    public List<CargoLoai> Loai { get; set; } = new();
    public List<CargoQc> Qc { get; set; } = new();
}

/// <summary>Hồ sơ loại Cargo: Thuận Phước (XTP), Gia công (XGC), Gửi kho (XGK).</summary>
public sealed record CargoProfile(string Key, string Sheet, string Code, string Loai, string Ten, string Khach, string LyDo, string Mau, bool Gk, string KhoNhan, string KieuXuat, string SoEg);

/// <summary>
/// LÕI TÍNH TOÁN "CARGO LIST" (port từ pnx-cargo-core.js): mỗi dòng gõ Loại + Size-quy cách (0040-6) + Lô (22.05.26 L16) + số kiện từng dãy → app tự ra mã hàng 25 ký tự,
/// nhãn size, quy cách, TRA kết quả kháng sinh (Kết quả kháng sinh) theo (ngày, lô) rồi gom thành nhóm thị trường của phiếu xuất kho. Không phụ thuộc giao diện.
/// </summary>
public static class CargoCore
{
    public const int NDay = 40, Half = 20;
    public static readonly CargoProfile[] Profiles =
    {
        new("cargo", "PNX_Cargo", "XTP", "xuat_thuan_phuoc", "Cargo xuất Thuận Phước", "THUẬN PHƯỚC", "XUẤT HÀNG VỀ THUẬN PHƯỚC", "xtp", false, "", "Xuất nội địa", "05.10 XTP 46"),
        new("cargogc", "PNX_CargoGC", "XGC", "xuat_gia_cong", "Cargo xuất Gia Công", "GIA CÔNG", "XUẤT HÀNG GIA CÔNG", "xtp", false, "", "Xuất nội địa", "05.10 XGC 01"),
        new("cargogk", "PNX_CargoGK", "XGK", "xuat_gui_kho", "Cargo xuất gửi kho", "", "", "xgk", true, "Transimex Long An", "Xuất nội địa - Gửi kho", "09.05XGK 29"),
    };

    /// <summary>23 nhóm thị trường của phiếu (đúng thứ tự cột E..AA của mẫu; cách viết có dấu phẩy như mẫu).</summary>
    public static readonly string[] Markets =
    {
        "MKS", "NHẬT, ASC, HQ, MỸ", "NHẬT, ASC-A,B, HQ, MỸ", "NHẬT, ASC-B, HQ, MỸ", "NHẬT, ASC, HQ", "NHẬT, EU, HQ", "NHẬT, ASC-A,B, HQ", "NHẬT, ASC-B, HQ", "NHẬT, ASC", "NHẬT, HQ", "NHẬT, EU",
        "ASC, HQ, MỸ", "ASC-A, B, HQ, MỸ", "ASC-B, HQ, MỸ", "ASC, HQ", "ASC-A,B, HQ", "ASC-B, HQ", "ASC", "EU, HQ, MỸ", "EU, HQ", "EU", "HQ", "KHÔNG ĐẠT",
    };

    public static double R6(double v) => Math.Round(v * 1e6) / 1e6;
    private static string Pad2(int n) => n.ToString("00");
    private static double Num(string? v) => double.TryParse((v ?? "").Replace(",", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : 0;
    private static string Dbl(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ nhóm thị trường

    private static List<string> TokensOf(string? s)
    {
        var t = Regex.Replace((s ?? "").ToUpperInvariant(), @"^\s*ĐẠT\b", "");
        return Regex.Matches(t, @"NHẬT|MKS|EU|HQ|MỸ|ASC-A\s*,\s*B|ASC-B|ASC").Select(m => Regex.Replace(m.Value, @"\s+", "")).ToList();
    }
    private static string SetKey(string? s) => string.Join("|", TokensOf(s).OrderBy(x => x, StringComparer.Ordinal));
    private static readonly Dictionary<string, string> MKey = Markets.Where(m => m != "KHÔNG ĐẠT").ToDictionary(SetKey, m => m);
    public static string MarketFromDat(string? dat)
    {
        var s = (dat ?? "").Trim(); if (s.Length == 0) return "";
        if (Regex.IsMatch(s, @"KH[ÔO]NG\s*ĐẠT", RegexOptions.IgnoreCase)) return "KHÔNG ĐẠT";
        return MKey.GetValueOrDefault(SetKey(s), "");
    }

    private static Dictionary<int, (string Ky, string Dat)>? _rules;
    private static (string Ky, string Dat)? RuleOf(int stt)
    {
        _rules ??= KhangSinhCore.DefaultRules().ToDictionary(r => r.Stt, r => (r.KyHieu, r.Dat));
        return _rules.TryGetValue(stt, out var r) ? r : null;
    }

    // ------------------------------------------------------------------ lô nguyên liệu + tra kháng sinh

    public sealed record LoParse(bool Ok, string Date, string Alt, string Batch, string Text, string Err, bool Guessed);

    /// <summary>"22.05.26 L16" / "L16 22.05.26". Không gõ năm → ưu tiên NĂM HIỆN TẠI (nếu ngày đó còn ở tương lai thì lấy năm trước); năm còn lại làm phương án tra dự phòng.</summary>
    public static LoParse ParseLo(string? text, DateTime? today = null)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0) return new(false, "", "", "", s, "Chưa nhập lô.", false);
        var b = Regex.Match(s, @"L\s*0*(\d+)", RegexOptions.IgnoreCase);
        var d = Regex.Match(s, @"(\d{1,2})\s*[./\-]\s*(\d{1,2})(?:\s*[./\-]\s*(\d{4}|\d{2}))?(?!\d)");
        if (!b.Success) return new(false, "", "", "", s, "Thiếu số lô (dạng L16).", false);
        if (!d.Success) return new(false, "", "", "", s, "Thiếu ngày nguyên liệu (dạng 22.05.26 hoặc 22.05).", false);
        int dd = int.Parse(d.Groups[1].Value), mm = int.Parse(d.Groups[2].Value); var now = today ?? DateTime.Now; var cy = now.Year;
        if (mm < 1 || mm > 12 || dd < 1 || dd > 31) return new(false, "", "", "", s, "Ngày nguyên liệu không hợp lệ.", false);
        int y; var alt = ""; var guessed = false;
        if (d.Groups[3].Success) { y = int.Parse(d.Groups[3].Value); if (y < 100) y += 2000; }
        else
        {
            guessed = true;
            DateTime cur; try { cur = new DateTime(cy, mm, dd); } catch { cur = new DateTime(cy, mm, 1).AddDays(dd - 1); }
            var tmr = now.Date.AddDays(1);
            y = cur >= tmr ? cy - 1 : cy; alt = $"{(y == cy ? cy - 1 : cy)}-{Pad2(mm)}-{Pad2(dd)}";
        }
        var batch = int.Parse(b.Groups[1].Value).ToString();
        return new(true, $"{y}-{Pad2(mm)}-{Pad2(dd)}", alt, batch, $"{Pad2(dd)}.{Pad2(mm)}.{y.ToString()[2..]} L{Regex.Replace(b.Groups[1].Value, @"^0+(?=\d)", "")}", "", guessed);
    }

    public static string IsoDate(string? t)
    {
        t = (t ?? "").Trim();
        var m = Regex.Match(t, @"^(\d{4})-(\d{1,2})-(\d{1,2})$"); if (m.Success) return $"{m.Groups[1].Value}-{Pad2(int.Parse(m.Groups[2].Value))}-{Pad2(int.Parse(m.Groups[3].Value))}";
        m = Regex.Match(t, @"^(\d{1,2})\s*[./\-]\s*(\d{1,2})\s*[./\-]\s*(\d{4}|\d{2})$"); if (!m.Success) return "";
        var y = int.Parse(m.Groups[3].Value); if (y < 100) y += 2000;
        return $"{y}-{Pad2(int.Parse(m.Groups[2].Value))}-{Pad2(int.Parse(m.Groups[1].Value))}";
    }

    public sealed record KsInfo(int Stt, string KyHieu, string Dat, string Market, bool Lan2, bool Tay = false);

    /// <summary>Chỉ mục kết quả kháng sinh theo (ngày, lô): lô "07" và "7" là một; nhiều bản ghi thì lấy bản lưu mới nhất.</summary>
    public sealed class KsIndex
    {
        internal readonly Dictionary<string, KhangSinhCore.Rec> Map = new();
        public int Size => Map.Count;
        public static KsIndex From(IEnumerable<KhangSinhCore.Rec> recs)
        {
            var ix = new KsIndex();
            foreach (var r in recs)
            {
                var d = IsoDate(r.Date); if (d.Length == 0) continue;
                var bt = r.Batch.Trim(); var k = d + "|" + (Regex.IsMatch(bt, @"^\d+$") ? long.Parse(bt).ToString() : bt);
                if (!ix.Map.TryGetValue(k, out var old) || string.CompareOrdinal(r.SavedAt, old.SavedAt) >= 0) ix.Map[k] = r;
            }
            return ix;
        }
    }

    private static int? ParseIntLead(string? s) { var m = Regex.Match(s ?? "", @"^\s*(\d+)"); return m.Success ? int.Parse(m.Groups[1].Value) : null; }

    /// <summary>Kết quả của bản ghi; lần 2 (nếu có và đọc được số) được ưu tiên.</summary>
    public static KsInfo? KsOfRecord(KhangSinhCore.Rec rec)
    {
        var j = rec.J;
        System.Text.Json.Nodes.JsonNode? l2n = null;
        if (j["lan2All"] is System.Text.Json.Nodes.JsonArray arr && arr.Count > 0) l2n = arr[^1]; else l2n = j["lan2"];
        KsInfo? Pick(string? kyhieu, string? sttRaw, string? dat, bool lan2)
        {
            var stt = ParseIntLead(kyhieu) ?? ParseIntLead(sttRaw) ?? 0;
            if (stt == 0) return null;
            var r = RuleOf(stt); var ky = r?.Ky ?? (kyhieu ?? ""); var dt = !string.IsNullOrEmpty(dat) ? dat : r?.Dat ?? "";
            var mk = MarketFromDat(r?.Dat ?? dt); if (mk.Length == 0) mk = MarketFromDat(dt);
            return new KsInfo(stt, ky, dt, mk, lan2);
        }
        string S(System.Text.Json.Nodes.JsonNode? n) => n is null ? "" : n is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s) ? s : n.ToJsonString();
        if (l2n is System.Text.Json.Nodes.JsonObject l2) { var a = Pick(S(l2["kyhieu"]), null, "", true); if (a is not null) return a; }
        return Pick(S(j["kyhieu"]), S(j["stt"]), S(j["dat"]), false);
    }

    public static KsInfo? LookupKs(KsIndex? ix, LoParse lo)
    {
        if (ix is null || !lo.Ok) return null;
        var rec = ix.Map.GetValueOrDefault(lo.Date + "|" + lo.Batch) ?? (lo.Alt.Length > 0 ? ix.Map.GetValueOrDefault(lo.Alt + "|" + lo.Batch) : null);
        return rec is null ? null : KsOfRecord(rec);
    }
    public static string KsLabel(KsInfo? k) => k is null ? "" : (k.KyHieu.Length > 0 ? k.KyHieu : k.Stt.ToString()) + (k.Lan2 ? " - L2" : "");

    // ------------------------------------------------------------------ danh mục loại hàng / quy cách

    public static CargoCfg DefaultCfg() => new()
    {
        Loai = { new() { Code = "BTP", Ten = "HLSO" }, new() { Code = "NL", Ten = "HOSO" } },
        Qc =
        {
            new() { D = "4", Label = "1.8Kg x 4", Pack = "IMC1", Pre = { ["BTP"] = "VRHLBTNXXXR00" } },
            new() { D = "5", Label = "1.8Kg x 5", Pack = "IMD1", Pre = { ["BTP"] = "VRHLBTNXXXR00" } },
            new() { D = "6", Label = "1.8Kg x 6", Pack = "IME1", Pre = { ["BTP"] = "VRHLBTNXXXR00", ["NL"] = "VRHOBTNXXXR00" } },
            new() { D = "7", Label = "1.8Kg x 7", Pack = "IMF1", Pre = { ["BTP"] = "VRHLBTNXXXR00", ["NL"] = "VRHOBTNXXXR00" } },
            new() { D = "8", Label = "8Kg x 1", Pack = "JPA1", Pre = { ["NL"] = "VRHOITNXXXR00" } },     // 8Kg x 1 luôn là hàng NL (HOSO)
            new() { D = "9", Label = "9Kg x 1", Pack = "JRA1", Pre = { ["BTP"] = "VRHLITNXXXR00" } },    // 9Kg x 1 luôn là hàng BTP (HLSO)
        },
    };

    public static CargoCfg NormCfg(CargoCfg? c)
    {
        var o = new CargoCfg(); var seenL = new HashSet<string>(); var seenD = new HashSet<string>();
        foreach (var x in c?.Loai ?? new())
        {
            var code = Regex.Replace((x.Code ?? "").Trim().ToUpperInvariant(), "[^A-Z0-9]", ""); if (code.Length > 8) code = code[..8];
            if (code.Length == 0 || !seenL.Add(code)) continue;
            o.Loai.Add(new CargoLoai { Code = code, Ten = (x.Ten ?? "").Trim() });
        }
        if (o.Loai.Count == 0) o.Loai = DefaultCfg().Loai;
        foreach (var x in c?.Qc ?? new())
        {
            var d = (x.D ?? "").Trim(); if (d.Length > 1) d = d[..1];
            if (!Regex.IsMatch(d, @"^\d$") || !seenD.Add(d)) continue;
            var pre = new Dictionary<string, string>();
            foreach (var l in o.Loai) { var v = (x.Pre.GetValueOrDefault(l.Code) ?? "").Trim(); if (v.Length > 0) pre[l.Code] = v; }
            o.Qc.Add(new CargoQc { D = d, Label = (x.Label ?? "").Trim(), Pack = (x.Pack ?? "").Trim().ToUpperInvariant(), Pre = pre });
        }
        o.Qc = o.Qc.OrderBy(q => q.D, StringComparer.Ordinal).ToList();
        return o;
    }

    // ------------------------------------------------------------------ size-quy cách → mã hàng

    public sealed record SnParse(bool Ok, string Loai, string Size4, string Digit, string Err);

    /// <summary>"0040-6" · "40-6" · "BTP40-6" · "NL130 8".</summary>
    public static SnParse ParseSn(string? text, string loaiMacDinh, CargoCfg cfg)
    {
        var s = Regex.Replace((text ?? "").Trim().ToUpperInvariant(), @"\s+", " ");
        if (s.Length == 0) return new(false, loaiMacDinh, "", "", "Chưa nhập size-quy cách.");
        var alt = string.Join("|", cfg.Loai.Select(l => l.Code).OrderByDescending(c => c.Length).Select(Regex.Escape));
        var m = Regex.Match(s, "^(" + alt + @")?\s*0*(\d{1,4})\s*[-–./ ]\s*(\d)$");
        if (!m.Success) return new(false, loaiMacDinh, "", "", "Gõ dạng 0040-6 (size 4 số - số quy cách).");
        return new(true, m.Groups[1].Success ? m.Groups[1].Value : loaiMacDinh, m.Groups[2].Value.PadLeft(4, '0'), m.Groups[3].Value, "");
    }
    public static CargoQc? QcOf(CargoCfg cfg, string digit) => cfg.Qc.FirstOrDefault(q => q.D == digit);
    /// <summary>Số quy cách chỉ dùng được cho ĐÚNG 1 loại thì Loại bị buộc theo nó (mặc định: 8 → NL, 9 → BTP).</summary>
    public static string LoaiTheoQuyCach(CargoCfg cfg, string digit) { var q = QcOf(cfg, digit); var ks = q?.Pre.Where(p => p.Value.Length > 0).Select(p => p.Key).ToList() ?? new(); return ks.Count == 1 ? ks[0] : ""; }
    public static string PackLabel(CargoCfg cfg, string digit) => QcOf(cfg, digit)?.Label ?? "";
    /// <summary>Mã 25 ký tự = tiền tố 13 + size 4 + mã gói 4 + số kết quả 4.</summary>
    public static string MaHang(CargoCfg cfg, string loai, string size4, string digit, int ksStt)
    {
        var q = QcOf(cfg, digit);
        if (q is null || !q.Pre.TryGetValue(loai, out var pre) || pre.Length == 0 || q.Pack.Length == 0) return "";
        return pre + size4 + q.Pack + (ksStt > 0 ? ksStt.ToString().PadLeft(4, '0') : "");
    }
    /// <summary>Nhãn size suy từ mã hàng (công thức cột C của file mẫu).</summary>
    public static string SizeLabel(string? ma)
    {
        var z = ma ?? ""; if (z.Length == 0) return "";
        string Mid(int a, int n) => a - 1 >= z.Length ? "" : z.Substring(a - 1, Math.Min(n, z.Length - a + 1));
        if (Mid(1, 4) == "VRHO") return Mid(15, 1) != "0" ? "NL" + Mid(15, 3) : "NL" + Mid(16, 2);
        if (Mid(1, 4) == "VRHL")
        {
            if (Mid(14, 4) == "0914") return "91/120";
            if (Mid(16, 2) == "XX") return Mid(14, 4);
            if (Mid(14, 2) == "00") return "BTP" + Mid(16, 2);
            if (string.CompareOrdinal(Mid(15, 1), "1") >= 0 && string.CompareOrdinal(Mid(14, 1), "1") >= 0) return Mid(14, 2) + "/" + Mid(16, 2);
            if (Mid(15, 1) == "1") return "BTP" + Mid(15, 3);
            if (Mid(14, 1) == "0" && Mid(15, 1) == "8") return Mid(14, 2) + "/" + Mid(16, 2);
        }
        return "";
    }
    /// <summary>kg / thùng từ chữ quy cách: "1.8Kg x 6" → 10.8 · "9Kg x 1" → 9 · "100G x 60" → 6.</summary>
    public static double? KgPerCarton(string? pack)
    {
        var m = Regex.Match(pack ?? "", @"^\s*([\d.,]+)\s*(kg|g)\s*[x×*]\s*(\d+)\s*$", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) return null;
        if (m.Groups[2].Value.Equals("g", StringComparison.OrdinalIgnoreCase)) a /= 1000;
        return R6(a * double.Parse(m.Groups[3].Value));
    }

    // ------------------------------------------------------------------ dòng Cargo → thông tin đầy đủ

    public static double DayQty(CargoLine ln, int d) => Num(ln.Dai.GetValueOrDefault(d.ToString()));
    public static double LineTotal(CargoLine ln) { double s = 0; for (var d = 1; d <= NDay; d++) s += DayQty(ln, d); return s; }

    public sealed class Resolved
    {
        public double Kien;
        public List<string> Errors = new();
        public string Size = "", Pack = "", Ma = "", Market = "", KsText = "", LoText = "", Loai = "";
        public KsInfo? Ks;
        public bool ChuaCoKQ, Other;
        public SnParse? Sn;
    }

    public static Resolved ResolveLine(CargoCfg cfg, CargoBlock block, CargoLine ln, KsIndex? ks)
    {
        var r = new Resolved { Kien = LineTotal(ln), LoText = ln.Lo.Trim(), Other = block.Loai == "khac", Loai = ln.Loai };
        if (r.Other)
        {
            r.Size = ln.Size.Trim(); r.Pack = ln.Pack.Trim();
            if (r.Kien > 0 && KgPerCarton(r.Pack) is null) r.Errors.Add($"Quy cách \"{r.Pack}\" chưa tính được kg/thùng (gõ dạng 10.0 kg x 1 hoặc 100G x 60).");
            return r;
        }
        var first = cfg.Loai[0].Code;
        var sn = ParseSn(ln.Sn, ln.Loai, cfg); r.Loai = sn.Loai.Length > 0 ? sn.Loai : first;
        if (!sn.Ok) { if (r.Kien > 0 || ln.Sn.Trim().Length > 0) r.Errors.Add(sn.Err); return r; }
        var must = LoaiTheoQuyCach(cfg, sn.Digit); var mism = must.Length > 0 && sn.Loai != must;
        if (mism) r.Errors.Add($"Quy cách {PackLabel(cfg, sn.Digit)} là hàng {must} nhưng Loại đang là {(sn.Loai.Length > 0 ? sn.Loai : first)} — đổi Loại sang {must} hoặc sửa quy cách.");
        r.Pack = PackLabel(cfg, sn.Digit);
        if (r.Pack.Length == 0) r.Errors.Add($"Số quy cách {sn.Digit} chưa có trong danh mục (thêm ở tab “Danh mục” → Size-quy cách).");
        var lo = ParseLo(ln.Lo); KsInfo? k = null;
        if (ln.KsTay.Trim().Length > 0)
        {
            var st = ParseIntLead(ln.KsTay) ?? 0; var ru = st > 0 ? RuleOf(st) : null;
            if (ru is { } rr) k = new KsInfo(st, rr.Ky, rr.Dat, MarketFromDat(rr.Dat), false, true);
            else r.Errors.Add($"Số kết quả \"{ln.KsTay}\" không có trong bảng 1–83.");
        }
        if (k is null && lo.Ok) k = LookupKs(ks, lo);
        if (k is null) { r.ChuaCoKQ = true; r.Errors.Add(lo.Ok ? $"Chưa có kết quả kháng sinh cho {lo.Text} (Kết quả kháng sinh)." : lo.Err.Length > 0 ? lo.Err : "Chưa nhập lô."); }
        else { r.Ks = k; r.Market = k.Market; r.KsText = KsLabel(k); if (k.Market.Length == 0) r.Errors.Add($"Kết quả {k.KyHieu} chưa ra được nhóm thị trường."); }
        r.Ma = MaHang(cfg, r.Loai, sn.Size4, sn.Digit, k?.Stt ?? 0);
        if (r.Ma.Length == 0 && !mism) r.Errors.Add($"Chưa ghép được mã hàng (loại {r.Loai}, quy cách {sn.Digit}).");
        var sl = SizeLabel(r.Ma); r.Size = sl.Length > 0 ? sl : r.Loai + long.Parse(sn.Size4);
        r.Sn = sn; if (lo.Ok) r.LoText = lo.Text;
        return r;
    }

    // ------------------------------------------------------------------ gộp thành PHIẾU XUẤT KHO

    public sealed class PhieuRow { public string Size = "", Pack = ""; public Dictionary<string, double> Cols = new(); public double Other, Kien, Kg, PerCarton, Chua; public bool Ks; }
    public sealed class PhieuBlock { public string Title = ""; public List<PhieuRow> Rows = new(); public double Kien, Kg; public bool Ks; internal Dictionary<string, PhieuRow> Map = new(); }
    public sealed record Problem(int Bi, int Li, string Msg);
    public sealed class PhieuData
    {
        public List<PhieuBlock> Blocks = new(); public List<string> Cols = new(); public Dictionary<string, double> Per = new();
        public double Other, Kien, Kg; public int SoDay; public List<Problem> Problems = new();
    }

    public static int DayCount(CargoRec c) { var n = 0; for (var d = 1; d <= NDay; d++) { double s = 0; foreach (var b in c.Blocks) foreach (var l in b.Lines) s += DayQty(l, d); if (s > 0) n++; } return n; }
    public static string PhieuTitle(CargoBlock b) => b.Ten.Trim() + (b.Ma.Trim().Length > 0 ? " ( " + b.Ma.Trim() + " )" : "") + (b.Tag.Trim().Length > 0 ? " - " + b.Tag.Trim() : "");
    private static bool HasSz(Resolved r) => r.Size.Length > 0 || r.Pack.Length > 0;
    private static (int, double) SizeSortKey(CargoCfg cfg, string size)
    {
        var m = Regex.Match(size ?? "", @"^([A-Z]+)\s*(\d+)", RegexOptions.IgnoreCase); if (!m.Success) return (98, 0);
        var i = cfg.Loai.FindIndex(l => l.Code == m.Groups[1].Value.ToUpperInvariant());
        return (i < 0 ? 97 : i, double.Parse(m.Groups[2].Value));
    }
    private static (int, double) PackSortKey(string pack) { var m = Regex.Match(pack ?? "", @"^1\.8Kg x (\d+)$", RegexOptions.IgnoreCase); return m.Success ? (0, double.Parse(m.Groups[1].Value)) : (1, KgPerCarton(pack) ?? 0); }

    /// <summary>Phiếu theo nhóm thị trường: các khối CÙNG tên gộp 1; mỗi dòng gộp theo (size, quy cách). Dòng chưa có kết quả kháng sinh chưa tính vào cột nào — phiếu bị chặn xuất tới khi có.</summary>
    public static PhieuData BuildPhieu(CargoCfg cfg, CargoRec cargo, KsIndex? ks, bool preview = false)
    {
        var res = new PhieuData(); var usedCols = new HashSet<string>(); foreach (var m in Markets) res.Per[m] = 0;
        var merge = new Dictionary<string, PhieuBlock>();
        for (var bi = 0; bi < cargo.Blocks.Count; bi++)
        {
            var b = cargo.Blocks[bi]; var title = PhieuTitle(b);
            if (title.Length == 0 && !b.Lines.Any(l => LineTotal(l) > 0 || (preview && HasSz(ResolveLine(cfg, b, l, ks))))) continue;
            if (!merge.TryGetValue(title.ToLowerInvariant(), out var pb)) { pb = new PhieuBlock { Title = title, Ks = b.Loai != "khac" }; merge[title.ToLowerInvariant()] = pb; res.Blocks.Add(pb); }
            for (var li = 0; li < b.Lines.Count; li++)
            {
                var r = ResolveLine(cfg, b, b.Lines[li], ks);
                if (r.Kien <= 0 && !(preview && HasSz(r))) continue;
                if (r.Kien > 0) foreach (var e in r.Errors) res.Problems.Add(new Problem(bi, li, e));
                var k = (r.Size + "|" + r.Pack).ToLowerInvariant();
                if (!pb.Map.TryGetValue(k, out var row)) { row = new PhieuRow { Size = r.Size, Pack = r.Pack, PerCarton = KgPerCarton(r.Pack) ?? 0, Ks = !r.Other }; pb.Map[k] = row; pb.Rows.Add(row); }
                if (r.Other) row.Other += r.Kien;
                else if (r.Market.Length > 0) { row.Cols[r.Market] = row.Cols.GetValueOrDefault(r.Market) + r.Kien; usedCols.Add(r.Market); }
                else row.Chua += r.Kien;
                row.Kien += r.Kien;
            }
        }
        foreach (var pb in res.Blocks)
        {
            if (pb.Ks) pb.Rows = pb.Rows.OrderBy(r => SizeSortKey(cfg, r.Size)).ThenBy(r => PackSortKey(r.Pack)).ToList();
            foreach (var row in pb.Rows)
            {
                row.Kg = R6(row.Kien * row.PerCarton); pb.Kien += row.Kien; pb.Kg += row.Kg;
                foreach (var (m, q) in row.Cols) res.Per[m] += q;
                res.Other += row.Other;
            }
            res.Kien += pb.Kien; res.Kg += pb.Kg;
        }
        res.Cols = Markets.Where(usedCols.Contains).ToList(); res.Blocks = res.Blocks.Where(b => b.Rows.Count > 0).ToList(); res.Kg = R6(res.Kg); res.SoDay = DayCount(cargo);
        return res;
    }

    // ------------------------------------------------------------------ xuất gửi kho: mỗi dòng 1 hàng, mã rút gọn 19 ký tự

    public static string MaRutGon(string? ma) { var z = ma ?? ""; return z.Length == 25 ? z[..7] + z[13..] : z; }
    private static string FormOf(string ma) { var c = ma.Length > 4 ? ma[4] : ' '; return c == 'B' ? "BLOCK" : c == 'I' ? "IQF" : ""; }
    private static string PadLo(string? t) => Regex.Replace(t ?? "", @"\bL(\d)$", "L0$1");
    private static string Ks4(Resolved r) => r.Ks is null ? "" : r.Ks.Stt.ToString().PadLeft(4, '0');
    private static string TenHangGk(Resolved r, CargoBlock block)
    {
        if (r.Other) return string.Join(", ", new[] { block.Ten.Trim(), r.Size, r.Pack }.Where(x => x.Length > 0));
        var nguyenCon = r.Ma.StartsWith("VRHO") || (!r.Ma.StartsWith("VRH") && r.Loai == "NL");
        return string.Join(", ", new[] { "TÔM TCT " + (nguyenCon ? "NGUYÊN CON " : "") + r.Size, FormOf(r.Ma), r.Pack, Ks4(r) }.Where(x => x.Length > 0));
    }

    public sealed class GkRow { public int Stt; public string Ma = "", Ma15 = "", Ten = "", Lo = "", Size = "", Pack = "", Ks = ""; public double Kien, Kg, Per; public double[] Dai = new double[NDay]; public bool Other; public int Bi, Li; }
    public sealed class GkData { public List<GkRow> Rows = new(); public double Kien, Kg; public int SoDay; public List<Problem> Problems = new(); }

    public static GkData BuildGk(CargoCfg cfg, CargoRec cargo, KsIndex? ks, bool preview = false)
    {
        var g = new GkData(); double kg = 0;
        for (var bi = 0; bi < cargo.Blocks.Count; bi++)
            for (var li = 0; li < cargo.Blocks[bi].Lines.Count; li++)
            {
                var b = cargo.Blocks[bi]; var ln = b.Lines[li]; var r = ResolveLine(cfg, b, ln, ks);
                if (r.Kien <= 0 && !(preview && HasSz(r))) continue;
                if (r.Kien > 0) foreach (var e in r.Errors) g.Problems.Add(new Problem(bi, li, e));
                var per = KgPerCarton(r.Pack) ?? 0; var k = R6(r.Kien * per); var ma = r.Other ? "" : MaRutGon(r.Ma);
                var row = new GkRow { Stt = g.Rows.Count + 1, Ma = ma, Ma15 = ma.Length > 15 ? ma[..15] : ma, Ten = TenHangGk(r, b), Lo = PadLo(r.LoText), Size = r.Size, Pack = r.Pack, Ks = Ks4(r), Kien = r.Kien, Kg = k, Per = per, Other = r.Other, Bi = bi, Li = li };
                for (var d = 1; d <= NDay; d++) row.Dai[d - 1] = DayQty(ln, d);
                g.Rows.Add(row); g.Kien += r.Kien; kg += k;
            }
        g.Kg = R6(kg); g.SoDay = DayCount(cargo);
        return g;
    }

    // ------------------------------------------------------------------ số phiếu / tên file / thông tin xe

    private static string CodeOf(string? code) { var c = Regex.Replace((code ?? "XTP").ToUpperInvariant(), "[^A-Z0-9]", ""); return c.Length == 0 ? "XTP" : c; }
    private static string SepOf(string? code) => CodeOf(code) == "XGK" ? "" : " ";   // Xuất gửi kho ghi liền "09.05XGK 29" (đúng file thật)
    public static (int Nn, int Mm, int Kk)? ParseSo(string? s, string code)
    {
        var m = Regex.Match(s ?? "", @"(\d+)\s*\.\s*(\d{1,2})\s*" + CodeOf(code) + @"\s*(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
    }
    /// <summary>"NN.MM XTP KK" — NN = phiếu thứ mấy trong THÁNG xuất, KK = phiếu thứ mấy trong NĂM (mỗi loại đếm riêng).</summary>
    public static string NextSo(IEnumerable<CargoRec> list, string ngay, string selfId, string code)
    {
        var m = Regex.Match(ngay ?? "", @"^(\d{4})-(\d{2})-\d{2}$"); var y = m.Success ? m.Groups[1].Value : DateTime.Today.Year.ToString(); var mm = m.Success ? int.Parse(m.Groups[2].Value) : DateTime.Today.Month;
        int maxNn = 0, maxKk = 0;
        foreach (var x in list)
        {
            if (x.Id == selfId) continue; var q = ParseSo(x.SoPhieu, code); if (q is null) continue;
            var xy = x.Ngay.Length >= 4 ? x.Ngay[..4] : ""; if (Regex.IsMatch(xy, @"^\d{4}$") && xy != y) continue;
            if (q.Value.Mm == mm && q.Value.Nn > maxNn) maxNn = q.Value.Nn;
            if (q.Value.Kk > maxKk) maxKk = q.Value.Kk;
        }
        return $"{Pad2(maxNn + 1)}.{Pad2(mm)}{SepOf(code)}{CodeOf(code)} {Pad2(maxKk + 1)}";
    }
    public static string FileName(CargoRec c, string code)
    {
        var cd = CodeOf(code); var so = ParseSo(c.SoPhieu, cd); var m = Regex.Match(c.Ngay, @"^\d{4}-(\d{2})-(\d{2})$");
        var head = so is { } s ? $"{Pad2(s.Nn)}_{Pad2(s.Mm)}{SepOf(cd)}{cd} {Pad2(s.Kk)}" : Regex.Replace(c.SoPhieu.Length > 0 ? c.SoPhieu : cd, @"[\\/:*?""<>|]", "_");
        return (head + (m.Success ? $" ({m.Groups[2].Value}_{m.Groups[1].Value})" : "") + " " + Regex.Replace(c.Kho, @"\s*\+\s*", "+")).Trim() + ".xlsx";
    }
    /// <summary>"Cont gửi kho số: 29/2026" = KK của số phiếu "NN.MM XGK KK" + năm ngày xuất.</summary>
    public static string ContSoOf(CargoRec c, string code = "XGK")
    {
        var so = ParseSo(c.SoPhieu, code); if (so is null) return "";
        var y = Regex.Match(c.Ngay, @"^(\d{4})-"); return so.Value.Kk + "/" + (y.Success ? y.Groups[1].Value : DateTime.Today.Year.ToString());
    }

    /// <summary>"Thông tin xe" dán từ email / Excel: tài xế, SĐT, số xe, cont, seal, ngày.</summary>
    public sealed record XeInfo(string TaiXe, string Sdt, string SoXe, string Cont, string Seal, string Ngay);
    public static XeInfo ParseXe(string? text)
    {
        var src = (text ?? "").Replace(' ', ' ').Replace("\r", ""); string taiXe = "", sdt = "", soXe = "", cont = "", seal = "", ngay = "";
        string NoAccent(string s) => KhoLanh.Core.ChungTu.ChungTuService.StripAccents(s);
        foreach (var ln in src.Split('\n'))
        {
            var m = Regex.Match(ln, @"^\s*[-•*\d.)]*\s*([^:：]{1,40})[:：]\s*(.+?)\s*$"); if (!m.Success) continue;
            var lab = Regex.Replace(NoAccent(m.Groups[1].Value), @"\s+", " ").Trim(); var v = m.Groups[2].Value.Trim(); if (v.Length == 0) continue;
            if (Regex.IsMatch(lab, @"^(ho ten )?(tai xe|tx)$|^ten (tai xe|tx)$|^tai xe") && taiXe.Length == 0) taiXe = v;
            else if (Regex.IsMatch(lab, @"^(sdt|dt|so dien thoai|dien thoai|so dt)") && sdt.Length == 0) sdt = Regex.Replace(v, @"[^\d+]", "");
            else if (Regex.IsMatch(lab, @"^(so xe|bien so|bks|xe)$|^so xe") && soXe.Length == 0) soXe = v;
            else if (Regex.IsMatch(lab, "^cont") && cont.Length == 0) cont = v;
            else if (lab.Contains("seal") && seal.Length == 0) seal = v;
        }
        if (cont.Length == 0) { var c = Regex.Match(src, @"\b([A-Z]{4})\s?(\d{6,7})\b"); if (c.Success) cont = c.Groups[1].Value + " " + c.Groups[2].Value; }
        if (sdt.Length == 0) { var p = Regex.Match(src, @"\b(0\d{9,10})\b"); if (p.Success) sdt = p.Groups[1].Value; }
        if (soXe.Length == 0) { var x = Regex.Match(src, @"\b(\d{2}[A-Z]{1,2})[-\s.]?(\d{3,5}(?:\.\d{2})?)\b"); if (x.Success) soXe = x.Groups[1].Value + " " + x.Groups[2].Value; }
        var dt = Regex.Match(src, @"(\d{1,2})\s*[/.\-]\s*(\d{1,2})\s*[/.\-]\s*(\d{4})"); if (dt.Success) ngay = $"{dt.Groups[3].Value}-{Pad2(int.Parse(dt.Groups[2].Value))}-{Pad2(int.Parse(dt.Groups[1].Value))}";
        return new XeInfo(taiXe, sdt, soXe, cont, seal, ngay);
    }
    public static string NguoiNhanOf(XeInfo x) => x.TaiXe + (x.Sdt.Length > 0 ? (x.TaiXe.Length > 0 ? " ( " : "") + x.Sdt + (x.TaiXe.Length > 0 ? " )" : "") : "");
    public static string ContXeOf(XeInfo x) => string.Join(" / ", new[] { x.SoXe, x.Cont }.Where(s => s.Length > 0));

    /// <summary>"ĐỔ ĐẦY TỪNG DÃY": gõ TỔNG kiện của 1 dòng → xếp lần lượt từ dãy 1, mỗi dãy chỉ nhận phần còn trống = sức chứa dãy − hàng của các dòng khác. Dãy chưa có sức chứa = không nhận.</summary>
    public static (double Placed, double Left, int NDays, bool NoCap) RaiTong(CargoRec cargo, int bi, int li, double total, IReadOnlyDictionary<int, int> caps)
    {
        var ln = cargo.Blocks[bi].Lines[li]; var nDay = Enumerable.Range(1, NDay).Count(d => caps.GetValueOrDefault(d) > 0);
        if (nDay == 0) return (0, 0, 0, true);
        var left = Math.Max(0, Math.Floor(total)); var want = left; ln.Dai = new();
        for (var d = 1; d <= NDay && left > 0; d++)
        {
            var cap = caps.GetValueOrDefault(d); if (cap <= 0) continue;
            double used = 0; foreach (var b in cargo.Blocks) foreach (var l in b.Lines) if (!ReferenceEquals(l, ln)) used += DayQty(l, d);
            var put = Math.Min(Math.Max(0, cap - used), left);
            if (put > 0) { ln.Dai[d.ToString()] = Dbl(put); left -= put; }
        }
        return (want - left, left, nDay, false);
    }

    // ------------------------------------------------------------------ JSON

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowReadingFromString, Converters = { new StrConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNameCaseInsensitive = true,
    };
    public static string ToJson(CargoRec c) => JsonSerializer.Serialize(c, Json);
    public static CargoRec? FromJson(string json)
    {
        try
        {
            var c = JsonSerializer.Deserialize<CargoRec>(json, Json); if (c is null) return null;
            if (c.Blocks.Count == 0) c.Blocks.Add(new CargoBlock { Loai = "ks" });
            foreach (var b in c.Blocks) if (b.Lines.Count == 0) b.Lines.Add(new CargoLine());
            return c;
        }
        catch { return null; }
    }
    public static CargoRec Clone(CargoRec c) => FromJson(ToJson(c))!;
    public static string CfgToJson(CargoCfg c) => JsonSerializer.Serialize(c, Json);
    public static CargoCfg? CfgFromJson(string json) { try { return JsonSerializer.Deserialize<CargoCfg>(json, Json); } catch { return null; } }
}

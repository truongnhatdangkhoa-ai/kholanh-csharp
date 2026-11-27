using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.LichXuat;

/// <summary>
/// Một lịch xuất container (1 dòng sheet LichXuatCont của database "lichxuat" — dùng CHUNG với app cũ, js/lich-xuat-cont.js).
/// Giữ nguyên các cột lạ (Raw) để ghi lại không làm mất dữ liệu app cũ / điện thoại thêm vào.
/// </summary>
public sealed class Lich
{
    public string Id = "", Loai = "xuat", LoaiLich = "", Ngay = "", TrangThai = "plan", Lsx = "", Khach = "", ThiTruong = "", MatHang = "", Size = "";
    public double TonKien, Kg;
    public string ChiTiet = "", DoLuc = "", CanXuat = "", LsxDo = "", NguonHd = "", DataDate = "", Cont = "", ContSo = "", Gio = "", Viec = "", GhiChu = "";
    public int SoDong;
    public JsonObject Raw = new();

    public bool IsNhap => Loai == "nhap" || LoaiLich.StartsWith("nhap", StringComparison.Ordinal);

    private static string S(JsonNode? n) => LichXuatCore.S(n);
    private static double N(JsonNode? n) => double.TryParse(S(n).Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : 0;

    public static Lich From(JsonObject o)
    {
        var st = S(o["trangThai"]);
        return new Lich
        {
            Raw = (JsonObject)o.DeepClone(),
            Id = S(o["id"]), Loai = S(o["loai"]) == "nhap" ? "nhap" : "xuat", LoaiLich = S(o["loaiLich"]),
            Ngay = S(o["ngay"]) is { Length: >= 10 } ng ? ng[..10] : S(o["ngay"]),
            TrangThai = LichXuatCore.StLabel.ContainsKey(st) ? st : "wait",
            Lsx = S(o["lsx"]), Khach = S(o["khach"]), ThiTruong = S(o["thiTruong"]), MatHang = S(o["matHang"]), Size = S(o["size"]),
            TonKien = N(o["tonKien"]), Kg = N(o["kg"]), ChiTiet = S(o["chiTiet"]), DoLuc = S(o["doLuc"]), CanXuat = S(o["canXuat"]),
            LsxDo = S(o["lsxDo"]), NguonHd = S(o["nguonHd"]), SoDong = (int)N(o["soDong"]), DataDate = S(o["dataDate"]),
            Cont = S(o["cont"]), ContSo = S(o["contSo"]), Gio = S(o["gio"]), Viec = S(o["viec"]), GhiChu = S(o["ghiChu"]),
        };
    }

    public JsonObject ToJson()
    {
        var o = (JsonObject)Raw.DeepClone();
        void Set(string k, JsonNode? v) => o[k] = v;
        Set("id", Id); Set("loai", Loai); Set("loaiLich", LoaiLich); Set("ngay", Ngay); Set("trangThai", TrangThai); Set("lsx", Lsx);
        Set("khach", Khach); Set("thiTruong", ThiTruong); Set("matHang", MatHang); Set("size", Size); Set("tonKien", TonKien); Set("kg", Kg);
        Set("chiTiet", ChiTiet); Set("doLuc", DoLuc); Set("canXuat", CanXuat); Set("lsxDo", LsxDo); Set("nguonHd", NguonHd); Set("soDong", SoDong);
        Set("dataDate", DataDate); Set("cont", Cont); Set("contSo", ContSo); Set("gio", Gio); Set("viec", Viec); Set("ghiChu", GhiChu);
        return o;
    }

    public Lich Clone() { var x = From(ToJson()); return x; }
}

/// <summary>Một việc cần xử lý của lịch (cột viec — JSON [{i, t, h, d, dt}]). Hạn mặc định = trước ngày xuất 1 ngày.</summary>
public sealed class Viec
{
    public string I { get; set; } = "";
    public string T { get; set; } = "";
    public string H { get; set; } = "";
    public int D { get; set; }
    public string Dt { get; set; } = "";
}

/// <summary>Một nhóm container (các LSX chung 1 cont gộp 1 dòng) để hiện ở Trang Tổng quan.</summary>
public sealed record ContRow(List<string> Ids, string Ngay, string Gio, string Khach, string Lsx, List<string> Codes, string Cont, string Xe, double Kg,
    string St, string Co, ViecBadge? Viec, bool HasList, bool HasPhieu, string PhieuCont);

public sealed record ViecBadge(string Level, string Text, string All);

/// <summary>Trạng thái chuẩn bị của 1 nhóm LSX lấy từ Phiếu xuất kho (List / phiếu / cont / xe).</summary>
public sealed record PnxState(bool List, bool Phieu, string Cont, string Xe)
{
    public static readonly PnxState None = new(false, false, "", "");
}

/// <summary>
/// LÕI LỊCH XUẤT (viết lại theo js/lich-xuat-cont.js + js/trang-chu.js của app cũ). HÀM THUẦN, không đọc / ghi đĩa.
/// </summary>
public static class LichXuatCore
{
    public const string Db = "lichxuat", Sheet = "LichXuatCont", SheetTk = "TongKetThang", SheetPend = "LX_ViecChuaLich", SheetTrust = "LX_TuDong", SheetEmail = "LX_EmailXuLy", SheetDupOk = "LX_TrungHopLe";

    public static readonly Dictionary<string, string> StLabel = new() { ["ok"] = "Đã xuất", ["run"] = "Đang đóng hàng", ["plan"] = "Dự kiến", ["wait"] = "Chờ xác nhận" };
    public static readonly string[] DropLoaiLich = { "xuat_thuan_phuoc", "xuat_gia_cong", "xuat_gui_kho", "xuat_khac" };

    /// <summary>App cũ (PC 4.65) chỉ còn theo dõi lịch xuất cont XUẤT KHẨU — lịch nhập / loại xuất khác không hiện.</summary>
    public static bool Keep(Lich r) => !r.IsNhap && !DropLoaiLich.Contains(r.LoaiLich);

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

    public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateTime DateOf(string iso) => DateTime.ParseExact(iso[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static bool IsIso(string? s) => s is not null && Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$");
    public static string Vn(string iso) => Regex.Match(iso ?? "", @"^(\d{4})-(\d{2})-(\d{2})") is { Success: true } m ? $"{m.Groups[3]}/{m.Groups[2]}/{m.Groups[1]}" : iso ?? "";
    public static string Dm(string iso) => Regex.Match(iso ?? "", @"^\d{4}-(\d{2})-(\d{2})") is { Success: true } m ? $"{m.Groups[2]}/{m.Groups[1]}" : iso ?? "";
    public static string MonthOf(string iso) => iso.Length >= 7 ? iso[..7] : "";
    public static string GioVn(string g) => (g ?? "").Replace(':', 'h');

    // ------------------------------------------------------------------ LSX

    /// <summary>Mã LSX: 26.05.024 · 26.08.04A · 26.09.02 A · hậu tố -17/09 hoặc -B-02.</summary>
    public static readonly Regex LsxRe = new(@"\d{2}\.\d{2}\.\d{2,3}(?:[A-Za-z]+|\s[A-Z](?![A-Za-z]))?(?:-\d{1,2}\/\d{1,2}|(?:-[A-Za-z0-9]{1,3})+)?", RegexOptions.Compiled);
    /// <summary>So khớp / chống trùng: bỏ chữ LSX, khoảng trắng, hoa thường.</summary>
    public static string Sq(string? v) => Regex.Replace(Regex.Replace(v ?? "", @"\s+", "").ToUpperInvariant(), "^LSX[:\\-]?", "");
    public static string WithLsx(string c) => "LSX " + c.Trim().ToUpperInvariant();
    public static string Label(string? v) => Regex.IsMatch(v ?? "", @"^\s*LSX\b", RegexOptions.IgnoreCase) ? (v ?? "").Trim() : "LSX " + (v ?? "").Trim();
    /// <summary>Các số LSX trong 1 ô (cont ghép "26.12.050 + 26.12.051").</summary>
    public static List<string> Keys(string? v)
    {
        var a = LsxRe.Matches(v ?? "").Select(m => Sq(m.Value)).Where(x => x.Length > 0).Distinct().ToList();
        var w = Sq(v); if (w.Length > 0 && !a.Contains(w) && a.Count == 0) a.Add(w);
        return a;
    }
    /// <summary>Mã LSX trần (dùng tìm List / phiếu / Hợp đồng).</summary>
    public static List<string> Codes(string? v) => TroLy.TroLyChuY.LsxCodes(Regex.Replace(v ?? "", @"^\s*LSX\s*[:\-]?\s*", "", RegexOptions.IgnoreCase));

    // ------------------------------------------------------------------ ngày

    /// <summary>"07/09" · "07/09/2026" · "2026-09-07" → yyyy-mm-dd. Thiếu năm → chọn năm gần hôm nay nhất.</summary>
    public static string ParseNgay(string? v, DateTime today)
    {
        var s = (v ?? "").Trim(); int y, mo, d;
        var m = Regex.Match(s, @"^(\d{4})-(\d{1,2})-(\d{1,2})$");
        if (m.Success) { y = int.Parse(m.Groups[1].Value); mo = int.Parse(m.Groups[2].Value); d = int.Parse(m.Groups[3].Value); }
        else
        {
            m = Regex.Match(s, @"(\d{1,2})\s*[\/.\-]\s*(\d{1,2})(?:\s*[\/.\-]\s*(\d{2,4}))?");
            if (!m.Success) return "";
            d = int.Parse(m.Groups[1].Value); mo = int.Parse(m.Groups[2].Value); y = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
            if (y is > 0 and < 100) y += 2000;
            if (y == 0)
            {
                var best = 0; var bestDiff = double.MaxValue;
                foreach (var yy in new[] { today.Year - 1, today.Year, today.Year + 1 })
                    if (Ok(yy, mo, d) is { } dt && Math.Abs((dt - today.Date).TotalDays) < bestDiff) { bestDiff = Math.Abs((dt - today.Date).TotalDays); best = yy; }
                y = best;
            }
        }
        return Ok(y, mo, d) is { } r ? Iso(r) : "";
        static DateTime? Ok(int y, int mo, int d) { try { return y > 0 ? new DateTime(y, mo, d) : null; } catch { return null; } }
    }

    // ------------------------------------------------------------------ nhóm container

    /// <summary>Các LSX chung container: cùng ngày + cùng mã `cont`; trống = mỗi dòng 1 container.</summary>
    public static string ContKey(Lich x) => x.Ngay + "|" + (!x.IsNhap && x.Cont.Length > 0 ? x.Cont : x.Id);
    public static int ById(Lich a, Lich b) => string.CompareOrdinal(a.Id, b.Id);

    public static string CoOf(Lich x) =>
        x.LoaiLich == "xuat_khau_tp" || Regex.IsMatch(x.LoaiLich + " " + x.Khach, @"thuận\s*phước|thuan\s*phuoc", RegexOptions.IgnoreCase) || FirstCode(x).StartsWith('P') ? "tp" : "an";
    private static string FirstCode(Lich x)
    {
        try { return JsonNode.Parse(x.ChiTiet.Length > 0 ? x.ChiTiet : "[]") is JsonArray a && a.FirstOrDefault() is JsonArray l && l.Count > 2 ? S(l[2]).ToUpperInvariant() : ""; }
        catch { return ""; }
    }

    /// <summary>Tồn kho → chi tiết [tên hàng, size, mã hàng, tồn, kg, NSX].</summary>
    public static List<(string Ten, string Size, string Ma, double Ton, double Kg)> Lines(Lich x)
    {
        var o = new List<(string, string, string, double, double)>();
        try
        {
            if (JsonNode.Parse(x.ChiTiet.Length > 0 ? x.ChiTiet : "[]") is JsonArray a)
                foreach (var l in a.OfType<JsonArray>())
                    o.Add((S(l.ElementAtOrDefault(0)), S(l.ElementAtOrDefault(1)), S(l.ElementAtOrDefault(2)), Num(l.ElementAtOrDefault(3)), Num(l.ElementAtOrDefault(4))));
        }
        catch { }
        return o;
        static double Num(JsonNode? n) => double.TryParse(S(n), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    public sealed record Home(string Day, List<ContRow> Today, List<ContRow> Recent, List<ContRow> Upcoming);

    /// <summary>Số liệu Trang Tổng quan: lịch trong ngày, gần đây (đã xuất / đang đóng, mới → cũ), dự kiến (chưa xuất, gần → xa).</summary>
    public static Home BuildHome(IEnumerable<Lich> rows, string day, DateTime now, Func<List<Lich>, PnxState>? pnx = null)
    {
        var t = Iso(now);
        var groups = rows.Where(Keep).OrderBy(x => x.Id, StringComparer.Ordinal).GroupBy(ContKey).Select(g => g.ToList()).ToList();
        var all = groups.Select(g => Mk(g, now, pnx)).ToList();
        int ByGio(ContRow a, ContRow b) => string.CompareOrdinal(a.Gio.Length > 0 ? a.Gio : "99:99", b.Gio.Length > 0 ? b.Gio : "99:99");
        var today = all.Where(x => x.Ngay == day).ToList(); today.Sort(ByGio);
        var m0 = MonthOf(t) + "-01"; // 1.146: gần đây = TOÀN BỘ lịch từ đầu tháng tới hôm nay (giao diện phân trang)
        var recent = all.Where(x => string.CompareOrdinal(x.Ngay, t) <= 0 && string.CompareOrdinal(x.Ngay, m0) >= 0 && x.St != "plan").ToList();
        recent.Sort((a, b) => string.CompareOrdinal(b.Ngay, a.Ngay) is var c && c != 0 ? c : ByGio(b, a));
        var up = all.Where(x => string.CompareOrdinal(x.Ngay, t) >= 0 && x.St == "plan").ToList();
        up.Sort((a, b) => string.CompareOrdinal(a.Ngay, b.Ngay) is var c && c != 0 ? c : ByGio(a, b));
        return new Home(day, today, recent, up);
    }

    /// <summary>1.146: số trang (≥ 1) của <paramref name="count"/> dòng, mỗi trang <paramref name="size"/> dòng.</summary>
    public static int PageCount(int count, int size) => Math.Max(1, (count + size - 1) / size);

    /// <summary>1.146: số thứ tự đếm từ DƯỚI lên trên toàn danh sách — dòng đầu (mới nhất) = <paramref name="count"/>, dòng cuối = 1; không đổi khi sang trang.</summary>
    public static int Stt(int count, int index) => count - index;

    public static ContRow Mk(List<Lich> g, DateTime now, Func<List<Lich>, PnxState>? pnx)
    {
        var f = g[0]; var st = pnx?.Invoke(g) ?? PnxState.None;
        static List<string> Uq(IEnumerable<string> a) => a.Select(z => z.Trim()).Where(z => z.Length > 0).Distinct().ToList();
        var contSo = Uq(g.Select(z => z.ContSo));
        var cont = st.Cont.Length > 0 ? st.Cont : contSo.Count > 0 ? string.Join(", ", contSo) : g.Count > 1 ? $"ghép {g.Count} LSX" : "";
        var stt = g.All(z => z.TrangThai == "ok") ? "ok" : g.Any(z => z.TrangThai == "run") ? "run" : "plan";
        ViecBadge? vb = null; var rk = new Dictionary<string, int> { ["red"] = 3, ["amber"] = 2, ["idle"] = 1 };
        foreach (var z in g)
        {
            var (s, open) = ViecState(z, now);
            if (s.Length > 0 && (vb is null || rk[s] > rk[vb.Level])) vb = new ViecBadge(s, open[0].T + (open.Count > 1 ? " +" + (open.Count - 1) : ""), string.Join("; ", open.Select(v => v.T)));
        }
        return new ContRow(g.Select(z => z.Id).ToList(), f.Ngay, f.Gio, string.Join(" · ", Uq(g.Select(z => z.Khach))), string.Join(", ", Uq(g.Select(z => Label(z.Lsx)))),
            g.SelectMany(z => Codes(z.Lsx)).Distinct().ToList(), cont, st.Xe, g.Sum(z => z.Kg), stt, CoOf(f), vb, st.List, st.Phieu, st.Cont);
    }

    /// <summary>Cont đã xuất / dự kiến của 1 tháng (cont ghép chỉ "đã xuất" khi mọi LSX đã xuất).</summary>
    public static (int Done, int Plan, double KgDone, double KgPlan) MonthCont(IEnumerable<Lich> rows, string month)
    {
        int done = 0, plan = 0; double kd = 0, kp = 0;
        foreach (var g in rows.Where(r => Keep(r) && MonthOf(r.Ngay) == month).GroupBy(ContKey))
        {
            if (g.All(z => z.TrangThai == "ok")) { done++; kd += g.Sum(z => z.Kg); } else { plan++; kp += g.Sum(z => z.Kg); }
        }
        return (done, plan, kd, kp);
    }

    /// <summary>Lịch của ngày đã qua tự chuyển "Đã xuất" (như app cũ mỗi lần ghi).</summary>
    public static int AutoDone(IEnumerable<Lich> rows, DateTime now)
    {
        var t = Iso(now); var n = 0;
        foreach (var r in rows) if (r.Ngay.Length == 10 && string.CompareOrdinal(r.Ngay, t) < 0 && r.TrangThai != "ok") { r.TrangThai = "ok"; n++; }
        return n;
    }

    public static Lich? SameDayDup(IEnumerable<Lich> rows, string lsx, string ngay, string? exceptId = null)
    {
        var k = Sq(lsx); if (k.Length == 0) return null;
        return rows.FirstOrDefault(x => x.Id != exceptId && !x.IsNhap && x.Ngay == ngay && Sq(x.Lsx) == k);
    }

    // ------------------------------------------------------------------ 1.90: LSX đã có lịch ngày khác / LSX trùng lịch

    /// <summary>Lịch xuất có chung ít nhất 1 số LSX với <paramref name="lsx"/> (cont ghép "A + B" cũng khớp A).</summary>
    public static bool SharesLsx(Lich x, IReadOnlyCollection<string> keys) => !x.IsNhap && keys.Count > 0 && Keys(x.Lsx).Any(keys.Contains);

    /// <summary>
    /// Lịch CHƯA XUẤT (khác "Đã xuất") của cùng LSX ở NGÀY KHÁC — thêm lịch / nhập nhanh / email cho LSX này thì có thể là kế hoạch đổi ngày
    /// (người dùng đã sửa tay ngày / giờ của lịch cũ) → hỏi chuyển lịch cũ thay vì tạo thêm lịch trùng.
    /// </summary>
    public static List<Lich> OpenOther(IEnumerable<Lich> rows, string lsx, string ngay, string? exceptId = null)
    {
        var k = Keys(lsx);
        return rows.Where(x => x.Id != exceptId && x.TrangThai != "ok" && x.Ngay != ngay && SharesLsx(x, k))
            .OrderBy(x => x.Ngay, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Một nhóm lịch chưa xuất cùng số LSX. <see cref="Sig"/> = LSX + mã các lịch (đánh dấu "trùng hợp lệ" theo đúng bộ lịch này).</summary>
    public sealed record DupGroup(string Lsx, List<Lich> Rows)
    {
        public string Sig => Lsx + "|" + string.Join(",", Rows.Select(r => r.Id).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>
    /// LSX đang có từ 2 lịch CHƯA XUẤT trở lên (thường do lịch sửa tay rồi email / nhập nhanh tạo thêm). Bỏ nhóm đã đánh dấu hợp lệ
    /// (<paramref name="okSigs"/> — vd cùng LSX nhiều cont, nhiều ngày). Nhóm có cùng bộ lịch (cont ghép A + B) chỉ báo 1 lần.
    /// </summary>
    public static List<DupGroup> DupLsx(IEnumerable<Lich> rows, ISet<string>? okSigs = null)
    {
        var open = rows.Where(x => !x.IsNhap && x.TrangThai != "ok").ToList();
        var seen = new HashSet<string>(); var o = new List<DupGroup>();
        foreach (var g in open.SelectMany(x => Keys(x.Lsx).Select(k => (k, x))).GroupBy(p => p.k).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var list = g.Select(p => p.x).DistinctBy(x => x.Id).OrderBy(x => x.Ngay, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            if (list.Count < 2) continue;
            var ids = string.Join(",", list.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));
            if (!seen.Add(ids)) continue;
            var d = new DupGroup(g.Key, list);
            if (okSigs is not null && okSigs.Contains(d.Sig)) continue;
            o.Add(d);
        }
        return o;
    }

    /// <summary>Số → chữ hệ 36 (như Number.toString(36) của app cũ, để mã lịch cùng dạng).</summary>
    public static string B36(long v)
    {
        const string D = "0123456789abcdefghijklmnopqrstuvwxyz"; if (v <= 0) return "0";
        var sb = new System.Text.StringBuilder(); while (v > 0) { sb.Insert(0, D[(int)(v % 36)]); v /= 36; }
        return sb.ToString();
    }

    public static string NewId(int i = 0) =>
        "lx" + B36(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + i) + Guid.NewGuid().ToString("N")[..3];

    // ------------------------------------------------------------------ việc cần xử lý

    private static readonly JsonSerializerOptions JsonOpt = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static List<Viec> ViecOf(Lich x)
    {
        if (x.Viec.Trim().Length == 0) return new();
        try
        {
            var a = JsonNode.Parse(x.Viec) as JsonArray ?? new JsonArray();
            return a.OfType<JsonObject>().Select(o => new Viec { I = S(o["i"]), T = S(o["t"]), H = S(o["h"]), D = S(o["d"]) is "1" or "true" ? 1 : 0, Dt = S(o["dt"]) }).Where(v => v.T.Length > 0).ToList();
        }
        catch { return new(); }
    }
    public static void SetViec(Lich x, List<Viec> a) => x.Viec = a.Count == 0 ? "" : JsonSerializer.Serialize(a, JsonOpt);
    public static string ViecHan(Lich x, Viec v) => IsIso(v.H) ? v.H : (IsIso(x.Ngay) ? Iso(DateOf(x.Ngay).AddDays(-1)) : "");
    /// <summary>red = đến ngày xuất (hoặc quá hạn) chưa xong · amber = hôm nay là hạn · idle = còn việc, chưa đến hạn.</summary>
    public static string ViecLv(Lich x, Viec v, DateTime now)
    {
        var t = Iso(now); var h = ViecHan(x, v);
        return string.CompareOrdinal(t, x.Ngay) >= 0 || string.CompareOrdinal(t, h) > 0 ? "red" : t == h ? "amber" : "idle";
    }
    public static (string Level, List<Viec> Open) ViecState(Lich x, DateTime now)
    {
        var open = ViecOf(x).Where(v => v.D == 0).ToList();
        if (x.IsNhap || open.Count == 0 || x.TrangThai == "ok" || string.CompareOrdinal(x.Ngay, Iso(now)) < 0) return ("", open);
        var s = "idle";
        foreach (var v in open) { var lv = ViecLv(x, v, now); if (lv == "red") s = "red"; else if (lv == "amber" && s != "red") s = "amber"; }
        return (s, open);
    }
    public static Viec NewViec(string t) => new() { I = "v" + B36(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) + Guid.NewGuid().ToString("N")[..3], T = t.Trim() };
    public static bool ViecPush(Lich r, string t)
    {
        var a = ViecOf(r);
        if (a.Any(v => v.D == 0 && string.Equals(v.T.Trim(), t.Trim(), StringComparison.OrdinalIgnoreCase))) return false;
        a.Add(NewViec(t)); SetViec(r, a); return true;
    }

    /// <summary>Việc cho LSX chưa có lịch (sheet LX_ViecChuaLich).</summary>
    public sealed record Pend(string Id, string Lsx, string T, string Tao);

    /// <summary>Lịch nhận việc của 1 LSX: lịch XUẤT gần nhất chưa xuất, ngày chưa qua, có số LSX đó trong ô.</summary>
    public static Lich? PickRow(IEnumerable<Lich> list, string lsx, DateTime now)
    {
        var pk = Keys(lsx); var t = Iso(now); if (pk.Count == 0) return null;
        return list.Where(r => !r.IsNhap && r.TrangThai != "ok" && string.CompareOrdinal(r.Ngay, t) >= 0 && Keys(r.Lsx).Any(pk.Contains))
            .OrderBy(r => r.Ngay, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>Gắn việc chờ vào lịch vừa có; trả việc còn lại (chưa có lịch).</summary>
    public static List<Pend> AttachPend(List<Lich> rows, List<Pend> pend, DateTime now)
    {
        var rest = new List<Pend>();
        foreach (var p in pend) { var r = PickRow(rows, p.Lsx, now); if (r is null) rest.Add(p); else ViecPush(r, p.T); }
        return rest;
    }

    // ------------------------------------------------------------------ nhập nhanh (văn bản "Ngày dd/mm: … LSX …")

    public sealed record QuickRow(string Ngay, string Lsx, string TrangThai, string Grp);

    private const string LqDate = @"(\d{1,2}\s*[\/.\-]\s*\d{1,2}(?:\s*[\/.\-]\s*\d{2,4})?)";
    private static readonly Regex LqKw = new(@"^(xuất|nhập)(?:\s+(?:hàng|kho))?(?![A-Za-zÀ-ỹ])[\s:\-–]*", RegexOptions.IgnoreCase);

    /// <summary>
    /// Phân tích văn bản kế hoạch ("Ngày 07/09: Xuất LSX 26.07.051 + 26.08.033; LSX 26.10.054") → các lịch xuất.
    /// Nhiều LSX nối "+" trong 1 đoạn = chung 1 container. Dòng "Nhập" bị bỏ qua (app không còn theo dõi lịch nhập).
    /// </summary>
    public static (List<QuickRow> Rows, List<string> Notes) QuickParse(string text, DateTime now)
    {
        var today = Iso(now); var o = new List<QuickRow>(); var notes = new List<string>();
        var t = (text ?? "").Replace("\r", "");
        var ms = Regex.Matches(t, @"Ngày\s*" + LqDate + @"\s*[:\-–]?", RegexOptions.IgnoreCase).ToList();
        if (ms.Count == 0) ms = Regex.Matches(t, @"^[ \t]*" + LqDate + @"[ \t]*[:\-–]", RegexOptions.Multiline).ToList();
        var days = ms.Count == 0 ? new List<(string Date, string Body)> { ("", t) }
            : ms.Select((m, i) => (Date: m.Groups[1].Value, Body: t[(m.Index + m.Length)..(i + 1 < ms.Count ? ms[i + 1].Index : t.Length)])).ToList();
        var fb = today;
        if (days.Count == 1 && days[0].Date.Length == 0 && days[0].Body.Trim().Length > 0)
        {
            if (Regex.IsMatch(t, @"ngày\s*mai", RegexOptions.IgnoreCase)) fb = Iso(now.AddDays(1));
            if (!Regex.IsMatch(t, @"hôm\s*nay|ngày\s*mai", RegexOptions.IgnoreCase)) notes.Add("Không thấy \"Ngày dd/mm\" trong nội dung → tạm ghi vào ngày hôm nay, sửa ngày ở bảng nếu cần.");
        }
        var gi = 0;
        foreach (var (date, body0) in days)
        {
            var ngay = date.Length > 0 ? ParseNgay(date, now) : fb; var lab = date.Length > 0 ? "Ngày " + Regex.Replace(date, @"\s+", "") : "Nội dung";
            if (date.Length > 0 && ngay.Length == 0) { notes.Add($"Không đọc được ngày \"{date}\"."); continue; }
            var body = body0.Trim(); if (body.Length == 0) continue;
            var st = string.CompareOrdinal(ngay, today) < 0 ? "ok" : "plan";
            var lines = body.Split('\n').Select(x => Regex.Replace(x.Trim(), @"^(?:\d{1,2}\s*[.)]\s+|[-•*]\s*)", "").Trim()).Where(x => x.Length > 0).ToList();
            var anyTag = lines.Any(l => LqKw.IsMatch(l));
            var nx = 0;
            foreach (var l in lines)
            {
                var kw = LqKw.Match(l); var hasCode = LsxRe.IsMatch(l);
                var loai = kw.Success ? (kw.Groups[1].Value.StartsWith('X') || kw.Groups[1].Value.StartsWith('x') ? "xuat" : "nhap") : (anyTag ? (hasCode ? "xuat" : "") : "xuat");
                if (loai.Length == 0) continue;
                if (loai == "nhap") { notes.Add(lab + ": bỏ qua dòng NHẬP — lịch nhập hàng không còn được theo dõi."); continue; }
                var rest = kw.Success ? l[kw.Length..] : l;
                foreach (var seg in Regex.Split(rest, "[;,]+"))
                {
                    var codes = LsxRe.Matches(seg).Select(m => WithLsx(Regex.Replace(m.Value, @"\s+", ""))).ToList();
                    if (codes.Count == 0) continue;
                    var grp = codes.Count > 1 ? ((char)('A' + gi++ % 26)).ToString() : "";
                    foreach (var c in codes) { nx++; o.Add(new QuickRow(ngay, c, st, grp)); }
                }
            }
            var more = Regex.Match(body, @"thêm\s*(\d+)\s*load", RegexOptions.IgnoreCase);
            if (more.Success) notes.Add($"{lab}: \"có thêm {more.Groups[1].Value} Load\" chưa có mã LSX → chưa tạo lịch.");
            else if (nx == 0) notes.Add(lab + ": không có lịch nào" + (body.Contains("không", StringComparison.OrdinalIgnoreCase) ? " (ghi \"không có Load\")." : "."));
        }
        return (o, notes);
    }

    // ------------------------------------------------------------------ tổng kết tháng (sheet TongKetThang — điện thoại đọc)

    public static JsonArray BuildTk(JsonArray oldTk, List<Lich> rows, DateTime now)
    {
        var m = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var o in oldTk.OfType<JsonObject>()) { var k = S(o["thang"]); if (Regex.IsMatch(k, @"^\d{4}-\d{2}$")) m[k] = (JsonObject)o.DeepClone(); }
        foreach (var g in rows.GroupBy(r => MonthOf(r.Ngay)).Where(g => g.Key.Length == 7))
        {
            var xs = g.Where(x => !x.IsNhap).ToList();
            double R2(double v) => Math.Round(v * 100) / 100;
            int n = xs.Count, dn = xs.Count(x => x.TrangThai == "ok"); double kg = R2(xs.Sum(x => x.Kg)), kd = R2(xs.Where(x => x.TrangThai == "ok").Sum(x => x.Kg));
            if (m.TryGetValue(g.Key, out var o) && S(o["soLich"]) == n.ToString() && S(o["daXuat"]) == dn.ToString() && S(o["kg"]) == kg.ToString("0.##", CultureInfo.InvariantCulture) && S(o["kgDaXuat"]) == kd.ToString("0.##", CultureInfo.InvariantCulture)) continue;
            m[g.Key] = new JsonObject { ["thang"] = g.Key, ["soLich"] = n, ["daXuat"] = dn, ["kg"] = kg, ["kgDaXuat"] = kd, ["soNhap"] = 0, ["daNhap"] = 0, ["kgNhap"] = 0, ["kgDaNhap"] = 0, ["capNhat"] = now.ToString("d/M/yyyy, HH:mm:ss", CultureInfo.InvariantCulture) };
        }
        return new JsonArray(m.Values.Reverse().Select(x => (JsonNode)x).ToArray());
    }
}

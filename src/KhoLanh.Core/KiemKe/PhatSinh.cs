using System.Globalization;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.KiemKe;

/// <summary>
/// PHÁT SINH NHẬP / XUẤT SAU NGÀY ĐẾM → quy SỐ ĐẾM về NGÀY CHỐT (port buildAdjust của dc-gop-engine.js).
/// Tồn An An (file đã up) ĐÃ gồm mọi nhập / xuất đến ngày chốt nên không điều chỉnh phía hệ thống; chỉ đưa số đếm về ngày chốt.
/// Ngày cùng hoặc sau ngày kiểm kê → trừ (xuất) / cộng (nhập); trống ngày / trước ngày kiểm kê → KHÔNG tính + báo rà; sau ngày chốt → chỉ ghi nhận.
/// </summary>
public static class PhatSinh
{
    public sealed class Mv
    {
        public string Type = "", PageId = "", Page = "", PageNgay = "", Ma = "", Key = "", Ten = "", Vt = "", Hd = "", Gc = "", NgayRaw = "", Ngay = "";
        public int Dong; public bool DateBlank, DateBad; public double Kien; public double? Kg;
    }
    public sealed class Item
    {
        public Mv M = null!; public string State = "", Note = "", RefNgay = ""; public double AdjKien, AdjKg; public bool Review, Loose, KgEst;
        public string Label => Labels.GetValueOrDefault(State, State);
    }
    public sealed class Acc { public double NKien, NKg, XKien, XKg; public int N; public HashSet<string> Flags = new(); }
    public sealed class Adj
    {
        public List<Item> List = new(); public Dictionary<string, Acc> ByKey = new(); public string Chot = "";
        public int NTru, NCong, NReview, NSauChot;
        public IEnumerable<Item> Of(string key) => List.Where(x => x.M.Key == key);
    }
    public static readonly Dictionary<string, string> Labels = new()
    {
        ["tru"] = "Trừ vào số đếm", ["cong"] = "Cộng vào số đếm", ["truoc"] = "Ngày trước ngày kiểm kê — KHÔNG tính (kiểm tra lại ngày)",
        ["thieu_ngay"] = "Để trống ngày — KHÔNG tính (kiểm tra)", ["loi_ngay"] = "Ngày không đọc được — KHÔNG tính (kiểm tra)",
        ["sau_chot"] = "Sau ngày chốt — chỉ ghi nhận, chờ cập nhật phần mềm chính thức",
    };

    public static bool IsoOk(string? s) => Regex.IsMatch(s ?? "", @"^\d{4}-\d{2}-\d{2}$");
    private static string Ok(int y, int m, int d) { try { var dt = new DateTime(y, m, d); return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } catch { return ""; } }

    /// <summary>25/12 · 25/12/2026 · 25-12-26 · 2026-12-25 · số ngày Excel. Thiếu năm + biết ngày chốt → chọn năm gần ngày chốt nhất (±6 tháng).</summary>
    public static (string Iso, bool Blank, bool Bad) ParseNgay(string? raw, int year, string? near = null)
    {
        var s = (raw ?? "").Trim(); if (s.Length == 0) return ("", true, false);
        var m = Regex.Match(s, @"^(\d{4})-(\d{1,2})-(\d{1,2})");
        if (m.Success) { var iso = Ok(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)); return (iso, false, iso.Length == 0); }
        m = Regex.Match(s, @"^(\d{1,2})[\/\-.](\d{1,2})(?:[\/\-.](\d{2,4}))?$");
        if (m.Success)
        {
            int d = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value);
            var y = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : year; if (y < 100) y += 2000;
            if (!m.Groups[3].Success && IsoOk(near))
            {
                var nt = DateTime.ParseExact(near!, "yyyy-MM-dd", CultureInfo.InvariantCulture); string best = ""; var bd = double.MaxValue;
                foreach (var yy in new[] { nt.Year - 1, nt.Year, nt.Year + 1 }) { var iso = Ok(yy, mo, d); if (iso.Length == 0) continue; var dist = Math.Abs((DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture) - nt).TotalDays); if (dist < bd) { bd = dist; best = iso; } }
                if (best.Length > 0 && bd <= 183) return (best, false, false);
            }
            var r = Ok(y, mo, d); return (r, false, r.Length == 0);
        }
        if (Regex.IsMatch(s, @"^\d{5}(\.\d+)?$") && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var ser)) { var r = DateTime.FromOADate(Math.Floor(ser)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); return (r, false, false); }
        return ("", false, true);
    }

    private static string VtNorm(string? s) => Regex.Replace((s ?? "").ToUpperInvariant(), @"\s+", "");
    private static string VtLoose(string e) { var p = e.Split('.'); return p.Length >= 5 ? string.Join(".", p.Take(4)) : e; }
    private static double R4(double v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
    private sealed class Agg { public double Kien, Kg; public int KgN; public string Latest = ""; }

    public static Adj Build(IReadOnlyList<Mv> mvs, IEnumerable<KiemKeCore.Line> rows, string? chotDate)
    {
        var chot = IsoOk(chotDate) ? chotDate! : "9999-12-31";
        var byVt = new Dictionary<string, Agg>(); var byLoose = new Dictionary<string, Agg>(); var mk = new Dictionary<string, Agg>();
        static Agg Slot(Dictionary<string, Agg> d, string k) { if (!d.TryGetValue(k, out var a)) d[k] = a = new Agg(); return a; }
        foreach (var r in rows)
        {
            var e = VtNorm(r.Vt); var a = Slot(mk, r.Key); a.Kien += r.Kien; if (r.Kg is { } kg) { a.Kg += kg; a.KgN++; }
            if (e.Length > 0)
                foreach (var (d, k) in new[] { (byVt, e + "|" + r.Key), (byLoose, VtLoose(e) + "|" + r.Key) })
                { var s = Slot(d, k); s.Kien += r.Kien; if (r.Kg is { } kg2) { s.Kg += kg2; s.KgN++; } if (r.Ngay.Length > 0 && string.CompareOrdinal(r.Ngay, s.Latest) > 0) s.Latest = r.Ngay; }
        }
        var o = new Adj { Chot = chot }; var avail = new Dictionary<string, double>();
        Acc Get(string key) { if (!o.ByKey.TryGetValue(key, out var a)) o.ByKey[key] = a = new Acc(); return a; }
        double? UnitKg(string key, Agg? agg) { if (agg is { KgN: > 0, Kien: not 0 }) return agg.Kg / agg.Kien; return mk.TryGetValue(key, out var m) && m.KgN > 0 && m.Kien != 0 ? m.Kg / m.Kien : null; }
        foreach (var m in mvs.OrderBy(x => x.Ngay.Length > 0 ? x.Ngay : "9", StringComparer.Ordinal))
        {
            var it = new Item { M = m }; var sign = m.Type == "x" ? -1 : 1; var e = VtNorm(m.Vt);
            Agg? agg = null; var ak = "";
            if (e.Length > 0)
            {
                ak = e + "|" + m.Key; byVt.TryGetValue(ak, out agg);
                if (agg is null) { ak = VtLoose(e) + "|" + m.Key; byLoose.TryGetValue(ak, out agg); if (agg is not null) { it.Loose = true; it.Note += "Khớp gần đúng vị trí (bỏ khoang). "; } }
            }
            var rf = agg is { Latest.Length: > 0 } ? agg.Latest : m.PageNgay; it.RefNgay = rf;
            var what = m.Type == "x" ? "xuất" : "nhập";
            if (m.DateBad) { it.State = "loi_ngay"; it.Review = true; Get(m.Key).Flags.Add("loi_ngay"); }
            else if (m.DateBlank || m.Ngay.Length == 0) { it.State = "thieu_ngay"; it.Review = true; Get(m.Key).Flags.Add("thieu_ngay"); it.Note += $"Chưa ghi ngày {what}. "; }
            else if (string.CompareOrdinal(m.Ngay, chot) > 0) { it.State = "sau_chot"; it.Note += "Ghi nhận để cập nhật phần mềm chính thức — không cộng / trừ vào số đếm. "; Get(m.Key).Flags.Add("sau_chot"); }
            else if (rf.Length > 0 && string.CompareOrdinal(m.Ngay, rf) < 0) { it.State = "truoc"; it.Review = true; Get(m.Key).Flags.Add("truoc"); it.Note += $"Ngày {what} trước ngày kiểm kê ({rf}) — nhập sai ngày? "; }
            else
            {
                it.State = m.Type == "x" ? "tru" : "cong"; it.AdjKien = sign * m.Kien;
                var kg = m.Kg;
                if (kg is null) { var u = UnitKg(m.Key, agg); if (u is { } uu) { kg = m.Kien * uu; it.KgEst = true; it.Note += "Kg ước tính theo trọng lượng của số đếm. "; } }
                if (kg is null) { it.Note += "Thiếu trọng lượng — chưa tính kg. "; it.Review = true; kg = 0; }
                it.AdjKg = sign * kg.Value;
                var a = Get(m.Key); a.N++; if (m.Type == "x") { a.XKien += m.Kien; a.XKg += kg.Value; } else { a.NKien += m.Kien; a.NKg += kg.Value; }
                var k = ak.Length > 0 ? ak : "|" + m.Key; var cur = avail.TryGetValue(k, out var av) ? av : (agg?.Kien ?? 0); var nxt = cur + sign * m.Kien; avail[k] = nxt;
                if (m.Type == "x" && nxt < -1e-9) { it.Review = true; it.Note += $"Xuất lớn hơn số đang có tại vị trí này (còn {Math.Round(nxt, 2)}). "; Get(m.Key).Flags.Add("xuat_lon"); }
            }
            o.List.Add(it);
        }
        o.List = o.List.OrderBy(x => x.M.Ngay, StringComparer.Ordinal).ThenBy(x => x.M.Ma, StringComparer.Ordinal).ToList();
        foreach (var a in o.ByKey.Values) { a.NKien = R4(a.NKien); a.XKien = R4(a.XKien); a.NKg = R4(a.NKg); a.XKg = R4(a.XKg); }
        foreach (var x in o.List) { if (x.State == "tru") o.NTru++; else if (x.State == "cong") o.NCong++; else if (x.State == "sau_chot") o.NSauChot++; if (x.Review) o.NReview++; }
        return o;
    }
}

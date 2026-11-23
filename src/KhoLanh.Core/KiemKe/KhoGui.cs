using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.KiemKe;

/// <summary>
/// TỒN KHO GỬI cho Đối chiếu kiểm kê (port DCEngine.autoMa21 / applyMap + DCGop.makeGui của app cũ).
/// Mã kho gửi 18–19 ký tự → mã 21 ký tự: bảng chuyển đổi mã do người dùng lập (ưu tiên), không có thì tự chuyển theo Mã hoá sản phẩm:
/// 15 ký tự đầu = [1–7] + [14–21]; 6 ký tự giữa (vị trí 8–13) suy từ Số lô + Size: TGS → XXXTGS · TGS+TGC → XTCTGS · AD → XAIR00 ·
/// Ecuador → XECR00 · TGC → XTCR00 · còn lại → XXXR00. Tổ hợp khác (VD TGS + AD) → không đoán, giữ mã gửi (không tính vào tổng).
/// Tồn kho gửi chỉ dùng cho đối chiếu KẾ TOÁN: tổng = tồn chốt (số đếm) + kho gửi.
/// </summary>
public static class KhoGui
{
    public sealed class Line { public string Ma = "", MaGui = "", Ten = "", Lo = "", Size = "", Phieu = "", Ngay = "", Via = "", Rule = "", Why = ""; public double Kg; public double? Kien; }
    public sealed class Agg { public string Ma = "", Ten = ""; public double Kg; public double? Kien; public int Rows; }
    public sealed class Result
    {
        public Dictionary<string, Agg> Map = new(); public List<Line> Detail = new(), Bad = new();
        public double KgAll, KgBad; public int N, NTay, NAuto;
    }

    public static string NormMa(string? v) => Regex.Replace((v ?? "").Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"\s+", "") is var s && s.StartsWith("TPC-", StringComparison.Ordinal) ? s[4..] : Regex.Replace((v ?? "").Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"\s+", "");
    private static string Fold(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD); var sb = new StringBuilder();
        foreach (var c in d) if (c < '̀' || c > 'ͯ') sb.Append(c == 'đ' ? 'd' : c == 'Đ' ? 'D' : c);
        return Regex.Replace(Regex.Replace(sb.ToString().ToLowerInvariant(), @"[._\-/\\():#*]+", " "), @"\s+", " ").Trim();
    }
    private static readonly Dictionary<string, string> MaHoa = new() { ["BTP"] = "XXXR00", ["TGS"] = "XXXTGS", ["TGS_TGC"] = "XTCTGS", ["AD"] = "XAIR00", ["ECUADOR"] = "XECR00", ["TGC"] = "XTCR00" };
    private static readonly Dictionary<string, string> RuleLabel = new() { ["BTP"] = "BTP thường", ["TGS"] = "Tôm Giám Sát", ["TGS_TGC"] = "Giám Sát + Gia Công", ["AD"] = "Tôm Ấn Độ", ["ECUADOR"] = "Tôm Ecuador", ["TGC"] = "Tôm Gia Công" };

    /// <summary>null = đã là mã 21; Ma rỗng = không chuyển được (Why).</summary>
    public static (string Ma, string Rule, string Why)? AutoMa21(string ma, string lo, string size)
    {
        var code = NormMa(ma);
        if (code.Length == 21) return null;
        if (code.Length is not (18 or 19)) return ("", "", $"Mã kho gửi {code.Length} ký tự (cần 18–19)");
        var text = Fold(lo + " " + size); var tokens = text.Split(' ');
        var hits = new List<string>();
        if (text.Contains("tgs")) hits.Add("TGS");
        if (tokens.Any(t => t == "ad")) hits.Add("AD");
        if (text.Contains("ecuador")) hits.Add("ECUADOR");
        if (text.Contains("tgc")) hits.Add("TGC");
        var rule = hits.FirstOrDefault() ?? "BTP";
        if (hits.Count == 2 && hits.Contains("TGS") && hits.Contains("TGC")) rule = "TGS_TGC";
        else if (hits.Count > 1) return ("", "", "Xung đột quy tắc: " + string.Join(" + ", hits));
        return (code[..7] + MaHoa[rule] + code.Substring(7, 8), RuleLabel[rule], "");
    }

    /// <summary>applyMap + makeGui: bảng chuyển đổi (gui → ma 21) ưu tiên, không có thì tự chuyển; mã không đủ 21 ký tự KHÔNG tính vào tổng.</summary>
    public static Result Build(IEnumerable<Line> rows, IEnumerable<(string Gui, string Ma)> map)
    {
        var m = new Dictionary<string, string>();
        foreach (var (g, t) in map) { var a = NormMa(g); var b = NormMa(t); if (a.Length > 0 && b.Length > 0) m[a] = b; }
        var o = new Result();
        foreach (var r0 in rows)
        {
            var r = new Line { Ma = NormMa(r0.Ma), MaGui = NormMa(r0.Ma), Ten = r0.Ten, Lo = r0.Lo, Size = r0.Size, Phieu = r0.Phieu, Ngay = r0.Ngay, Kg = r0.Kg, Kien = r0.Kien };
            if (m.TryGetValue(r.Ma, out var t)) { r.Ma = t; r.Via = "tay"; o.NTay++; }
            else if (AutoMa21(r.Ma, r.Lo, r.Size) is { } a) { if (a.Ma.Length > 0) { r.Ma = a.Ma; r.Via = "auto"; r.Rule = a.Rule; o.NAuto++; } else r.Why = a.Why; }
            o.N++;
            if (r.Ma.Length != 21) { if (r.Why.Length == 0) r.Why = $"Mã dài {r.Ma.Length} ký tự — chưa có trong bảng chuyển đổi"; o.Bad.Add(r); o.Detail.Add(r); o.KgBad += r.Kg; continue; }
            o.Detail.Add(r); o.KgAll += r.Kg;
            var key = KiemKeCore.NormKey(r.Ma);
            if (!o.Map.TryGetValue(key, out var e)) o.Map[key] = e = new Agg { Ma = r.Ma };
            e.Kg += r.Kg; e.Rows++; if (r.Kien is double k) e.Kien = (e.Kien ?? 0) + k; if (e.Ten.Length == 0 && r.Ten.Length > 0) e.Ten = r.Ten;
        }
        foreach (var e in o.Map.Values) { e.Kg = Math.Round(e.Kg, 4); if (e.Kien is double k) e.Kien = Math.Round(k, 4); }
        o.KgAll = Math.Round(o.KgAll, 4); o.KgBad = Math.Round(o.KgBad, 4);
        return o;
    }
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static KhoLanh.Core.TroLy.TroLyChecks;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// KIỂM TRA MÃ HÀNG &amp; TÊN HÀNG SAI QUY CÁCH (theo core/tro-ly/ma-hang.js của app cũ). HÀM THUẦN, chỉ đọc.
/// Chuẩn: mã hàng 21 ký tự theo 10 đoạn của "Mã hóa sản phẩm" (Tổng hợp); tên chuẩn = Tổng hợp › Danh sách mã hàng (MaDaTao, cột Ghi chú).
/// Luật 21 ký tự chỉ áp cho Tồn kho An An và Tồn theo vị trí; Tồn kho gửi (mã 18–19 ký tự) chỉ soi tên (font, khoảng trắng, nhiều tên).
/// </summary>
public static class TroLyMaHang
{
    public static readonly (int Seg, int Len, string Label)[] Segs =
    {
        (1, 4, "Tên sản phẩm"), (2, 1, "Cấp đông"), (3, 1, "Đóng gói"), (4, 1, "Hóa chất / phụ gia"), (5, 1, "Thị trường"),
        (6, 2, "Khách hàng"), (7, 3, "Mạ băng"), (8, 4, "Size"), (9, 2, "Trọng lượng"), (10, 2, "Quy cách thùng"),
    };
    public const int Len = 21;
    private const int MaxEv = 8;
    public static readonly Dictionary<string, string> SrcLabel = new() { ["M01"] = "Tồn An An", ["M01VT"] = "Tồn theo vị trí", ["M02"] = "Tồn kho gửi" };
    private static readonly HashSet<string> Len21Src = new() { "M01", "M01VT" };
    private static readonly HashSet<string> VtSrc = new() { "M01VT" };
    private static string Sl(string src) => SrcLabel.GetValueOrDefault(src, src);

    public sealed record Row(string Src, string Ma, string Ten);
    public sealed record Master(string Ma, string Ten);
    public sealed record SegCode(int Seg, string Code);

    /// <summary>Tên của Tồn theo vị trí luôn dạng «tên (quy cách) - Đặc tính - Hợp đồng» → bỏ 2 phần cuối trước khi so với tên chuẩn.</summary>
    public static string StripVT(string t)
    {
        var p = (t ?? "").Split(" - ");
        return p.Length >= 3 ? string.Join(" - ", p[..^2]).Trim() : (t ?? "").Trim();
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsOpt = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    /// <summary>Chuỗi dạng JSON (thấy rõ dấu cách, \n, \t…).</summary>
    private static string Js(string s) => System.Text.Json.JsonSerializer.Serialize(s, JsOpt);

    public static string Fold(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d) if (ch < '̀' || ch > 'ͯ') sb.Append(ch);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();
    }
    public static string Key(string? s) => Regex.Replace(Fold(s), "[^a-z0-9]+", "");
    public static string NormCode(string? c)
    {
        var t = Regex.Replace(c ?? "", @"\s+", "").ToUpperInvariant();
        return t.StartsWith("TPC-", StringComparison.Ordinal) ? t[4..] : t;
    }

    public static int Lev(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1]; cur[0] = i; var rowMin = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                cur[j] = v; if (v < rowMin) rowMin = v;
            }
            if (rowMin > max) return max + 1;
            prev = cur;
        }
        return prev[b.Length];
    }

    /// <summary>Phân loại tên trong file so với tên chuẩn: dung · thua (ghép thêm chữ) · thieu (mất chữ) · sai (lệch vài ký tự) · khac (khác hẳn).</summary>
    public static string CmpName(string ten, string chuan)
    {
        var a = Key(ten); var b = Key(chuan);
        if (a.Length == 0 || b.Length == 0 || a == b) return "dung";
        if (a.Contains(b, StringComparison.Ordinal)) return "thua";
        if (b.Contains(a, StringComparison.Ordinal)) return "thieu";
        var max = Math.Max(3, (int)Math.Round(b.Length * 0.12, MidpointRounding.AwayFromZero));
        return Lev(a, b, max) <= max ? "sai" : "khac";
    }

    /// <summary>Phần chữ ghép thừa (đứng trước / sau tên chuẩn).</summary>
    public static string ExtraPart(string ten, string chuan)
    {
        var t = (ten ?? "").Trim(); var f = Fold(t); var c = Fold(chuan).Trim(); var i = f.IndexOf(c, StringComparison.Ordinal);
        if (i < 0 || c.Length == 0 || f.Length != t.Length) return "";
        var pre = t[..i].Trim(); var post = t[(i + c.Length)..].Trim();
        return string.Join(", ", new[] { pre.Length > 0 ? "«" + pre + "» đứng trước" : "", post.Length > 0 ? "«" + post + "» đứng sau" : "" }.Where(x => x.Length > 0));
    }

    // Vỡ font: ký tự �, "Ã¡"-kiểu (sau Ã / Â / Ä là ký tự Latin-1 lạ), "á»", "Æ°", dấu ? kẹp giữa 2 chữ, ký tự điều khiển.
    private static readonly Regex[] FontRe =
    {
        new("�"), new("Ã[\u0080-¿]"), new("Â[\u0080-¿]"), new("á»"), new("Æ°"), new("Ä[\u0090-¿]"),
        new(@"[A-Za-zÀ-ỹ]\?[A-Za-zÀ-ỹ]"), new("[\u0000-\u0008\u000B\u000C\u000E-\u001F]"),
    };
    public static string FontHit(string s)
    {
        foreach (var re in FontRe)
        {
            var m = re.Match(s); if (!m.Success) continue;
            var i = m.Index;
            var cp = string.Join(" ", m.Value.Select(c => "U+" + ((int)c).ToString("X4")));
            return "…" + s[Math.Max(0, i - 12)..i] + "【" + m.Value + "】" + s[(i + m.Length)..Math.Min(s.Length, i + m.Length + 12)] + $"… (ký tự {cp}, vị trí {i + 1}/{s.Length})";
        }
        return "";
    }

    public static List<Finding> CheckMaTen(List<Row> input, List<Master> masterList, List<SegCode> maHoa)
    {
        var o = new List<Finding>();
        var rows = new List<Row>(); var seenRow = new HashSet<string>();
        foreach (var r in input) { if (r.Ma.Length == 0 || !seenRow.Add(r.Src + "|" + r.Ma + "|" + r.Ten)) continue; rows.Add(r); }
        var master = new Dictionary<string, string>();
        foreach (var r in masterList) { var c = NormCode(r.Ma); if (c.Length > 0) master.TryAdd(c, r.Ten.Trim()); }
        var seg = Segs.ToDictionary(s => s.Seg, _ => new HashSet<string>());
        foreach (var x in maHoa) if (seg.TryGetValue(x.Seg, out var set) && x.Code.Trim().Length > 0) set.Add(x.Code.Trim().ToLowerInvariant());
        if (rows.Count == 0) return new() { Mk("ma-hang", "xanh", "Mã hàng & tên hàng", "Chưa có dữ liệu để kiểm tra mã / tên hàng", "Các bảng tồn đang trống.", id: "ma-hang-on") };

        // gợi ý "có phải mã …?" — mã chuẩn khác đúng 1 ký tự
        Dictionary<string, List<string>>? bucket = null;
        string Near(string c)
        {
            if (c.Length != Len) return "";
            if (bucket is null)
            {
                bucket = new();
                foreach (var m in master.Keys)
                {
                    if (m.Length != Len) continue;
                    for (var i = 0; i < Len; i++) { var k = i + ":" + m[..i] + m[(i + 1)..]; if (!bucket.TryGetValue(k, out var l)) bucket[k] = l = new(); l.Add(m); }
                }
            }
            var hit = new SortedSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < Len; i++) foreach (var m in bucket.GetValueOrDefault(i + ":" + c[..i] + c[(i + 1)..]) ?? new()) if (m != c) hit.Add(m);
            return hit.Count == 1 ? hit.First() : hit.Count > 1 ? string.Join(" / ", hit.Take(2)) : "";
        }

        // ================= MÃ HÀNG =================
        var lenBad = new List<string>(); var charBad = new List<string>(); var segBad = new List<string>(); var notInList = new List<string>();
        var fixes = new List<MaFix>(); var seen = new HashSet<string>();
        foreach (var r in rows)
        {
            var c = NormCode(r.Ma);
            if (!seen.Add(r.Src + "|" + c)) continue;
            var lbl = "[" + Sl(r.Src) + "] ";
            if (!Len21Src.Contains(r.Src)) continue;
            var rawNoPrefix = Regex.Replace(r.Ma, "^TPC-", "", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(rawNoPrefix, @"[^A-Za-z0-9\s]") || Regex.IsMatch(r.Ma, "[\u0000-\u001F ​ ]"))
                charBad.Add(lbl + Short(Js(r.Ma), 60) + " → có ký tự lạ / khoảng trắng ẩn");
            if (c.Length != Len) { lenBad.Add(lbl + c + $" ({c.Length} ký tự" + (c.Length < Len ? $", thiếu {Len - c.Length}" : $", thừa {c.Length - Len}") + ")"); continue; }
            var bad = new List<string>(); var pos = 0;
            foreach (var (n, len, label) in Segs)
            {
                var piece = c.Substring(pos, len); pos += len;
                if (seg[n].Count == 0) continue;
                if (n >= 2 && n <= 8 && Regex.IsMatch(piece, "^X+$")) continue;
                if (!seg[n].Contains(piece.ToLowerInvariant())) bad.Add($"đoạn {n} {label} «{piece}»");
            }
            if (bad.Count > 0) { var nr = Near(c); segBad.Add(lbl + c + " → " + string.Join("; ", bad) + " chưa có trong Mã hóa sản phẩm" + (nr.Length > 0 ? " · có phải " + nr + "?" : "")); }
            else if (master.Count > 0 && !master.ContainsKey(c))
            {
                var nr = Near(c);
                notInList.Add(lbl + c + (nr.Length > 0 ? " · gần giống " + nr + " (khác 1 ký tự — nghi gõ sai)" : ""));
                fixes.Add(new MaFix(c, VtSrc.Contains(r.Src) ? StripVT(r.Ten) : r.Ten.Trim(), nr, Sl(r.Src)));
            }
        }
        if (lenBad.Count > 0) o.Add(Mk("ma-hang", "do", "Mã hàng", $"Mã hàng sai độ dài (phải 21 ký tự): {lenBad.Count} mã", "Mã công ty luôn 21 ký tự (Tồn An An / Tồn theo vị trí). Thường do cột Excel bị cắt, ghép thêm chữ hoặc mất ký tự khi nạp.", Cut(lenBad, MaxEv)));
        if (charBad.Count > 0) o.Add(Mk("ma-hang", "do", "Mã hàng", $"Mã hàng chứa ký tự lạ / khoảng trắng ẩn: {charBad.Count} mã", "Mã chỉ gồm chữ và số. Dấu cách, xuống dòng, ký tự ẩn làm mã không khớp khi tra cứu / đối chiếu.", Cut(charBad, MaxEv)));
        if (segBad.Count > 0) o.Add(Mk("ma-hang", "vang", "Mã hàng", $"Mã có đoạn chưa khai báo trong Mã hóa sản phẩm: {segBad.Count} mã", "Đủ 21 ký tự nhưng có đoạn không tìm thấy trong bảng Mã hóa (Tổng hợp) — thường gõ sai chữ, hoặc cần bổ sung vào Mã hóa.", Cut(segBad, MaxEv)));
        if (notInList.Count > 0)
        {
            var f = Mk("ma-hang", "vang", "Mã hàng", $"Mã hợp lệ nhưng chưa có trong Danh sách mã hàng: {notInList.Count} mã",
                "Mã đúng quy cách nhưng Tổng hợp › Danh sách mã hàng chưa có → không có tên chuẩn để đối chiếu. Mã có \"gần giống\" là dấu hiệu gõ sai 1 ký tự.",
                Cut(notInList.OrderByDescending(x => x.Contains("gần giống") ? 1 : 0), MaxEv));
            f.Fixes = fixes.Take(500).ToList();
            o.Add(f);
        }

        // ================= TÊN HÀNG =================
        var cnt = new Dictionary<string, int> { ["thua"] = 0, ["thieu"] = 0, ["sai"] = 0, ["khac"] = 0 };
        var ev = cnt.Keys.ToDictionary(k => k, _ => new List<string>());
        var font = new List<string>(); var space = new List<string>(); var bySrc = new Dictionary<string, int>();
        var seenN = new HashSet<string>(); var multi = new Dictionary<string, Dictionary<string, string>>();
        foreach (var r in rows)
        {
            var c = NormCode(r.Ma); var ten = r.Ten; if (ten.Trim().Length == 0) continue;
            var fh = FontHit(ten);
            if (fh.Length > 0) font.Add("[" + Sl(r.Src) + "] " + c + " · lỗi tại " + fh);
            else if (Regex.IsMatch(ten, @"^\s|\s$|\s{2,}|[\r\n\t]")) space.Add("[" + Sl(r.Src) + "] " + c + " · " + Short(Js(ten), 70));
            var tenC = VtSrc.Contains(r.Src) ? StripVT(ten) : ten;
            var grp = r.Src + "|" + c;
            if (!multi.TryGetValue(grp, out var mm)) multi[grp] = mm = new();
            var kn = Key(tenC); if (kn.Length > 0) mm.TryAdd(kn, tenC.Trim());
            if (!master.TryGetValue(c, out var chuan)) continue;
            if (!seenN.Add(r.Src + "|" + c + "|" + kn)) continue;
            var k = CmpName(tenC, chuan); if (k == "dung") continue;
            cnt[k]++; bySrc[r.Src] = bySrc.GetValueOrDefault(r.Src) + 1;
            var lbl = "[" + Sl(r.Src) + "] " + c + " — file: «" + tenC.Trim() + "» · chuẩn: «" + chuan + "»";
            var ex = k == "thua" ? ExtraPart(tenC, chuan) : "";
            ev[k].Add(ex.Length > 0 ? lbl + " · thừa: " + ex : lbl);
        }
        var srcTxt = string.Join(", ", bySrc.Select(x => Sl(x.Key) + " " + x.Value));
        if (cnt["thieu"] > 0) o.Add(Mk("ma-hang", "do", "Tên hàng", $"Tên hàng bị THIẾU chữ so với Danh sách mã hàng: {cnt["thieu"]} dòng", $"Tên trong file ngắn hơn tên chuẩn của cùng mã (mất đoạn cuối / bị cắt khi nạp). Nguồn: {srcTxt}.", Cut(ev["thieu"], MaxEv)));
        if (cnt["sai"] > 0) o.Add(Mk("ma-hang", "vang", "Tên hàng", $"Tên hàng sai / thiếu vài ký tự: {cnt["sai"]} dòng", "Tên gần giống tên chuẩn nhưng lệch vài ký tự (gõ sai, mất dấu, mất chữ).", Cut(ev["sai"], MaxEv)));
        if (cnt["thua"] > 0) o.Add(Mk("ma-hang", "vang", "Tên hàng", $"Tên hàng bị GHÉP THÊM chữ so với Danh sách mã hàng: {cnt["thua"]} dòng", $"Tên chứa đúng tên chuẩn nhưng có thêm chữ (thường là nhóm, ghi chú, hợp đồng… từ cột khác dính vào). Nguồn: {srcTxt}.", Cut(ev["thua"], MaxEv)));
        if (cnt["khac"] > 0) o.Add(Mk("ma-hang", "vang", "Tên hàng", $"Tên hàng khác hẳn tên chuẩn của mã: {cnt["khac"]} dòng", "Tên trong file không giống tên chuẩn của mã — có thể sai mã, lệch cột khi nạp, hoặc tên cũ đã đổi.", Cut(ev["khac"], MaxEv)));
        if (font.Count > 0)
        {
            var f = Mk("ma-hang", "do", "Tên hàng", $"Tên hàng lỗi font / ký tự hỏng: {font.Count} dòng", "Có ký tự �, chữ vỡ dạng \"Ã¡\", dấu \"?\" thay chữ có dấu hoặc ký tự điều khiển — file Excel / CSV nạp sai bảng mã.", Cut(font, MaxEv));
            f.Nguon = true; o.Add(f);
        }
        if (space.Count > 0) o.Add(Mk("ma-hang", "vang", "Tên hàng", $"Tên hàng thừa khoảng trắng / xuống dòng: {space.Count} dòng", "Có dấu cách đầu / cuối, 2 dấu cách liền nhau, tab hoặc xuống dòng trong tên — dễ gây lệch khi tra cứu và so sánh.", Cut(space, MaxEv)));
        var dupName = multi.Where(x => x.Value.Count > 1).Select(x =>
        {
            var p = x.Key.Split('|');
            return "[" + Sl(p[0]) + "] " + p[1] + " → " + string.Join(" ≠ ", x.Value.Values.Take(3).Select(v => "«" + v + "»"));
        }).ToList();
        if (dupName.Count > 0) o.Add(Mk("ma-hang", "vang", "Tên hàng", $"Một mã có nhiều tên khác nhau trong cùng bảng: {dupName.Count} mã", "Cùng mã hàng, cùng bảng nhưng tên ghi khác nhau (khác chữ, không chỉ khác dấu cách / hoa thường) — nghi nạp từ nhiều file không đồng nhất.", Cut(dupName, MaxEv)));
        if (o.Count == 0) o.Add(Mk("ma-hang", "xanh", "Mã hàng & tên hàng", "Mã hàng và tên hàng đúng quy cách",
            $"Đã soi {seen.Count} mã (Tồn An An / Tồn theo vị trí đủ 21 ký tự, đoạn đã khai báo, có trong Danh sách mã hàng) và tên so với tên chuẩn: không thấy thiếu / thừa / lỗi font.", id: "ma-hang-on"));
        return o;
    }
}

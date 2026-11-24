using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.DanhMuc;

/// <summary>
/// DANH MỤC MÃ HÀNG — lõi (chép danh-muc-ma-hang.js của app cũ, PC 1.70): đọc mã 21 ký tự theo 10 đoạn của "Mã hóa sản phẩm",
/// kiểm tra toàn danh mục (trùng mã, nghi gõ sai = khác đúng 1 ký tự mà ký tự đó làm đoạn chưa khai báo), đếm nơi đang dùng mã
/// (Tồn kho An An / Tồn kho gửi / Sổ kho), mục "Cần xử lý".
/// </summary>
public static class DanhMucMa
{
    public const int Len = 21;
    /// <summary>10 đoạn: tổ hợp · độ dài · tên (thứ tự = vị trí trong mã).</summary>
    public static readonly (int Seg, int Len, string Label)[] Segs =
    {
        (1, 4, "Tên sản phẩm"), (2, 1, "Cấp đông"), (3, 1, "Đóng gói"), (4, 1, "Hóa chất / phụ gia"), (5, 1, "Thị trường"),
        (6, 2, "Khách hàng"), (7, 3, "Mạ băng"), (8, 4, "Size"), (9, 2, "Trọng lượng"), (10, 2, "Quy cách thùng"),
    };
    private static readonly int[] SegAt = Segs.SelectMany((d, j) => Enumerable.Repeat(j, d.Len)).ToArray();

    /// <summary>Tổ hợp → (mã không phân biệt hoa thường → tên).</summary>
    public sealed class SegMap
    {
        public readonly Dictionary<int, Dictionary<string, string>> Map = Segs.ToDictionary(s => s.Seg, _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        /// <summary>Danh sách cố định đoạn 1 (m8-seg1-fixed.js) rồi sheet MaHoa (Tổ hợp | Mã | Tên) — dòng sau đè dòng trước như app cũ.</summary>
        public static SegMap Build(IEnumerable<(string Code, string Name)> seg1Fixed, JsonArray? maHoa)
        {
            var m = new SegMap();
            foreach (var (c, n) in seg1Fixed) m.Map[1][c] = n;
            foreach (var r in maHoa?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                if (!double.TryParse(Str(r["Tổ hợp"]), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) || !m.Map.TryGetValue((int)s, out var t) || s != Math.Floor(s)) continue;
                if (r["Mã"] is null) continue; var code = Str(r["Mã"]).Trim(); if (code.Length == 0) continue;
                t[code] = Str(r["Tên"]);
            }
            return m;
        }
    }

    public sealed record Piece(int Seg, string Label, string Code, string? Name, bool Ok);
    public sealed record Status(string Level, List<string> Msgs);
    public sealed record Use(int M01, int M02, int SK);

    public static string Str(JsonNode? n) => n switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        _ => n.ToJsonString(),
    };
    public static string NormCode(string? c) => Regex.Replace(c ?? "", @"\s+", "").ToUpperInvariant();

    public static List<Piece> Decode(SegMap seg, string code)
    {
        code = NormCode(code); var o = new List<Piece>(10); var pos = 0;
        foreach (var d in Segs)
        {
            var piece = pos >= code.Length ? "" : code.Substring(pos, Math.Min(d.Len, code.Length - pos)); pos += d.Len;
            string? name = seg.Map[d.Seg].TryGetValue(piece, out var n) ? n : null;
            var none = d.Seg is >= 2 and <= 8 && Regex.IsMatch(piece, "^X+$");
            o.Add(new Piece(d.Seg, d.Label, piece, name, name is not null || none || piece.Length == 0));
        }
        return o;
    }
    /// <summary>Diễn giải tự dựng: tên các đoạn đã khai báo, ghép bằng " • " (mã không đủ 21 ký tự → rỗng).</summary>
    public static string NoteOf(SegMap seg, string code) =>
        NormCode(code).Length != Len ? "" : string.Join(" • ", Decode(seg, code).Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)));

    /// <summary>Kết quả kiểm tra toàn danh mục.</summary>
    public sealed class Checks
    {
        public readonly Dictionary<string, int> Dup = new();
        public readonly Dictionary<string, List<string>> Near = new();
        public int DupOf(string c) => Dup.GetValueOrDefault(c);
    }
    public static Checks Check(SegMap seg, IEnumerable<string> codes)
    {
        var r = new Checks(); var bucket = new Dictionary<string, List<string>>();
        foreach (var x in codes) { var c = NormCode(x); if (c.Length == 0) continue; r.Dup[c] = r.Dup.GetValueOrDefault(c) + 1; }
        foreach (var c in r.Dup.Keys)
        {
            if (c.Length != Len) continue;
            for (var i = 0; i < Len; i++) { var k = i + ":" + c[..i] + c[(i + 1)..]; if (bucket.TryGetValue(k, out var b)) b.Add(c); else bucket[k] = new() { c }; }
        }
        var cache = new Dictionary<string, List<Piece>>();
        List<Piece> D(string c) => cache.TryGetValue(c, out var v) ? v : cache[c] = Decode(seg, c);
        foreach (var (key, list) in bucket)
        {
            if (list.Count < 2) continue;
            var sj = SegAt[int.Parse(key[..key.IndexOf(':')])];
            foreach (var a in list)
            {
                var okA = D(a)[sj].Ok;
                foreach (var b in list)
                {
                    if (a == b || okA || !D(b)[sj].Ok) continue;
                    if (!r.Near.TryGetValue(a, out var s)) r.Near[a] = s = new();
                    if (!s.Contains(b)) s.Add(b);
                }
            }
        }
        return r;
    }

    public static Status? StatusOf(SegMap seg, Checks chk, string code)
    {
        var c = NormCode(code); var errs = new List<string>(); var warns = new List<string>();
        if (c.Length == 0) return new Status("err", new() { "Thiếu mã hàng" });
        if (c.Length != Len) errs.Add($"Mã phải đủ {Len} ký tự (đang {c.Length})");
        if (chk.DupOf(c) > 1) errs.Add($"Trùng mã ({chk.DupOf(c)} dòng)");
        if (c.Length == Len) { var miss = Decode(seg, c).Where(x => !x.Ok).ToList(); if (miss.Count > 0) warns.Add("Đoạn chưa khai báo: " + string.Join(", ", miss.Select(x => x.Seg + "." + x.Code))); }
        if (chk.Near.TryGetValue(c, out var nr) && nr.Count > 0) warns.Add("Có thể gõ sai — gần giống " + string.Join(", ", nr.Take(2)));
        if (errs.Count == 0 && warns.Count == 0) return null;
        return new Status(errs.Count > 0 ? "err" : "warn", errs.Concat(warns).ToList());
    }

    /// <summary>Nguồn đếm "Đang dùng ở": database · sheet · nhóm (M01 = Tồn An An, M02 = Tồn gửi, SK = Sổ kho).</summary>
    public static readonly (string Db, string Sheet, string Tag)[] UseSources =
    {
        ("ton-kho-an-an", "LoadData", "M01"), ("ton-vi-tri-an-an", "ViTri", "M01"), ("ton-kho-an-an", "ViTriBot", "M01"),
        ("ton-kho-gui", "DuLieuChinh", "M02"), ("nxt-bot-sot", "SoCai", "SK"),
    };
    private static readonly Regex UseRe = new(@"^(?:TPC-)?([A-Z0-9]{21})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Đếm số dòng có mã (mỗi dòng tính 1 lần / mã) trong mọi ô chuỗi của các bảng nguồn.</summary>
    public static Dictionary<string, Use> CountUse(IEnumerable<(string Tag, JsonArray? Rows)> sources)
    {
        var d = new Dictionary<string, (int a, int b, int c)>();
        foreach (var (tag, rows) in sources)
            foreach (var row in rows?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                var seen = new HashSet<string>();
                foreach (var (_, v) in row)
                {
                    if (v is not JsonValue jv || !jv.TryGetValue<string>(out var s)) continue;
                    var m = UseRe.Match(s.Trim()); if (!m.Success) continue;
                    var c = m.Groups[1].Value.ToUpperInvariant(); if (!seen.Add(c)) continue;
                    var e = d.GetValueOrDefault(c);
                    d[c] = tag switch { "M01" => (e.a + 1, e.b, e.c), "M02" => (e.a, e.b + 1, e.c), _ => (e.a, e.b, e.c + 1) };
                }
            }
        return d.ToDictionary(x => x.Key, x => new Use(x.Value.a, x.Value.b, x.Value.c));
    }
    public static string UseText(Dictionary<string, Use> use, string code)
    {
        if (!use.TryGetValue(NormCode(code), out var u)) return "";
        var p = new List<string>();
        if (u.M01 > 0) p.Add("Tồn An An " + u.M01); if (u.M02 > 0) p.Add("Tồn gửi " + u.M02); if (u.SK > 0) p.Add("Sổ kho " + u.SK);
        return string.Join(" · ", p);
    }

    /// <summary>"Cần xử lý": mã đang dùng chưa có trong danh mục, mã trùng, cặp nghi gõ sai, mã có đoạn chưa khai báo.</summary>
    public sealed record Xl(List<string> Missing, List<(string Code, int N)> Dups, List<(string Code, string Other)> Near, List<string> Miss)
    {
        public int Count => Missing.Count + Dups.Count + Near.Count;
    }
    public static Xl XlData(SegMap seg, Checks chk, Dictionary<string, Use> use, IReadOnlyList<string> codes)
    {
        var have = new HashSet<string>(codes);
        var missing = use.Keys.Where(c => !have.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var dups = chk.Dup.Where(x => x.Value > 1).Select(x => (x.Key, x.Value)).ToList();
        var near = chk.Near.SelectMany(x => x.Value.Select(o => (x.Key, o))).ToList();
        var miss = codes.Where(c => c.Length == Len && Decode(seg, c).Any(x => !x.Ok)).ToList();
        return new Xl(missing, dups, near, miss);
    }
}

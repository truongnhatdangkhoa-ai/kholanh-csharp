using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.KhangSinh;

/// <summary>
/// Nghiệp vụ "Tra cứu kháng sinh" của app cũ (html/tra-cuu-khang-sinh.html), chép sang C#:
/// 7 chỉ tiêu, bảng quy định 83 trường hợp, chọn trường hợp phù hợp nhất (ksPickBest), bản ghi lần 1 / kiểm lại,
/// tìm nhanh (L1 3/5/2026 · MKS2 9.9.26 …), dò dòng tiêu đề + khớp cột khi nhập Excel.
/// Bản ghi giữ NGUYÊN JSON gốc (lan1Src, lan2, lan2All…) để không mất gì khi ghi lại.
/// </summary>
public static class KhangSinhCore
{
    public static readonly (string Key, string Label)[] Fields =
    {
        ("enro", "Enro"), ("cipro", "Cipro"), ("oxy", "Oxy"), ("doxy", "Doxy"), ("sulfo", "Sulfo"), ("aoz", "AOZ"), ("cap", "CAP"),
    };
    public static readonly string[] FieldKeys = Fields.Select(f => f.Key).ToArray();

    // ================================================================== quy định

    public sealed class Rule
    {
        public int Stt;
        public Dictionary<string, string> C = FieldKeys.ToDictionary(k => k, _ => "ND");
        public string Dat = "", KyHieu = "", MoTa = "";
        public bool IsFail => Dat.ToUpperInvariant().Contains("KHÔNG ĐẠT");
        public string this[string key] => C.GetValueOrDefault(key, "ND");

        public JsonObject ToJson()
        {
            var o = new JsonObject { ["stt"] = Stt };
            foreach (var k in FieldKeys) o[k] = this[k];
            o["dat"] = Dat; o["kyhieu"] = KyHieu; o["mota"] = MoTa;
            return o;
        }
        public static Rule FromJson(JsonObject o)
        {
            var r = new Rule { Stt = (int)Math.Round(NumOf(o["stt"]) ?? 0), Dat = Str(o["dat"]), KyHieu = Str(o["kyhieu"]), MoTa = Str(o["mota"]) };
            foreach (var k in FieldKeys) r.C[k] = o[k] is JsonValue v && v.TryGetValue<double>(out var d) ? "<" + d.ToString(CultureInfo.InvariantCulture) : Str(o[k]);
            return r;
        }
    }

    public static List<Rule> DefaultRules()
    {
        using var s = typeof(KhangSinhCore).Assembly.GetManifestResourceStream("KhoLanh.Core.KhangSinh.quy-dinh-mac-dinh.json")!;
        return (JsonNode.Parse(s) as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(Rule.FromJson).ToList();
    }

    public readonly record struct Cond(char Type, double Val);   // Type: 'N' = ND, '<', '>'
    public static Cond ParseCond(string? cond)
    {
        var c = (cond ?? "").Trim();
        if (c.Equals("ND", StringComparison.OrdinalIgnoreCase)) return new('N', 0);
        var m = Regex.Match(c, @"^([<>])\s*([\d.,]+)");
        if (m.Success && double.TryParse(m.Groups[2].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return new(m.Groups[1].Value[0], v);
        // số trơn = "<số" (app cũ: typeof cond === 'number')
        if (double.TryParse(c.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return new('<', n);
        return new('N', 0);
    }
    public static bool IsBlank(string? raw) { var t = (raw ?? "").Trim(); return t.Length == 0 || t.Equals("ND", StringComparison.OrdinalIgnoreCase); }
    private static double ParseNum(string? raw) =>
        double.TryParse(LeadingNumber((raw ?? "").Trim().Replace(',', '.')), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    /// <summary>Như parseFloat của JS: lấy phần số ở đầu chuỗi ("8.5 ppb" → 8.5).</summary>
    private static string LeadingNumber(string s) { var m = Regex.Match(s, @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?"); return m.Success ? m.Value : ""; }

    /// <summary>
    /// Chọn trường hợp "phù hợp nhất" — chép nguyên ksPickBest (PC 7.91):
    /// 1) KHÔNG ĐẠT xét trước (AOZ/CAP rồi chỉ tiêu số đo cao nhất; trường hợp KĐ ít điều kiện nhất);
    /// 2) ngưỡng lý tưởng mỗi chỉ tiêu = ngưỡng "&lt;X" nhỏ nhất mà số đo vẫn đạt;
    /// 3) trường hợp ĐẠT khớp đủ: tổng bậc nới → bậc từng chỉ tiêu → ít ô ngầm hiểu → số thứ tự nhỏ.
    /// </summary>
    public static Rule? PickBest(IReadOnlyList<Rule> rules, IReadOnlyDictionary<string, string> inputs)
    {
        string In(string k) => inputs.GetValueOrDefault(k) ?? "";
        var meas = FieldKeys.Where(k => !IsBlank(In(k)) && !double.IsNaN(ParseNum(In(k))))
            .Select(k => new M { Key = k, V = ParseNum(In(k)), Top = k is "aoz" or "cap" ? 1 : 0 })
            .OrderByDescending(m => m.Top).ThenByDescending(m => m.V).ToList();
        foreach (var m in meas)
        {
            Rule? best = null; var bestN = int.MaxValue;
            foreach (var row in rules)
            {
                if (!row.IsFail) continue;
                var c = ParseCond(row[m.Key]);
                if (c.Type != '>' || !(m.V > c.Val)) continue;
                var n = FieldKeys.Count(k => ParseCond(row[k]).Type == '>');
                if (n < bestN || (n == bestN && row.Stt < best!.Stt)) { best = row; bestN = n; }
            }
            if (best is not null) return best;
        }
        var levels = new Dictionary<string, List<double>>();
        foreach (var m in meas)
        {
            levels[m.Key] = rules.Select(r => ParseCond(r[m.Key])).Where(c => c.Type == '<' && double.IsFinite(c.Val)).Select(c => c.Val).Distinct().OrderBy(x => x).ToList();
            m.Ideal = levels[m.Key].FindIndex(x => m.V < x);
        }
        Rule? pick = null; List<double>? pickKey = null;
        foreach (var row in rules)
        {
            if (row.IsFail) continue;
            var ok = true; var imp = 0;
            foreach (var k in FieldKeys)
            {
                var c = ParseCond(row[k]); var raw = In(k);
                if (c.Type == 'N') { if (!IsBlank(raw)) { ok = false; break; } continue; }
                if (IsBlank(raw)) { if (c.Type == '<') { imp++; continue; } ok = false; break; }
                var v = ParseNum(raw);
                if (double.IsNaN(v) || !(c.Type == '<' ? v < c.Val : v > c.Val)) { ok = false; break; }
            }
            if (!ok) continue;
            var steps = meas.Select(m => { var c = ParseCond(row[m.Key]); var i = levels[m.Key].IndexOf(c.Val); return (double)(i < 0 || m.Ideal < 0 ? 99 : i - m.Ideal); }).ToList();
            var key = new List<double> { steps.Sum() }; key.AddRange(steps); key.Add(imp); key.Add(row.Stt);
            if (pickKey is null || Less(key, pickKey)) { pick = row; pickKey = key; }
        }
        return pick;
    }
    private sealed class M { public string Key = ""; public double V; public int Top, Ideal; }
    private static bool Less(List<double> a, List<double> b) { for (var i = 0; i < a.Count; i++) if (a[i] != b[i]) return a[i] < b[i]; return false; }

    public static List<string> AutoPassNotes(Rule row, IReadOnlyDictionary<string, string> inputs) =>
        Fields.Where(f => ParseCond(row[f.Key]).Type == '<' && IsBlank(inputs.GetValueOrDefault(f.Key)))
              .Select(f => $"{f.Label}: chưa phát hiện (ND) — mặc nhiên đạt ngưỡng {row[f.Key]}").ToList();
    public static string Summary(IReadOnlyDictionary<string, string>? inputs) =>
        string.Join("  ·  ", Fields.Select(f => $"{f.Label}: {(IsBlank(inputs?.GetValueOrDefault(f.Key)) ? "ND" : inputs![f.Key].Trim())}"));
    /// <summary>Màu kết quả: AOZ có giá trị → đỏ; tất cả ND → xanh; còn lại → hổ phách.</summary>
    public static string Tone(IReadOnlyDictionary<string, string>? inputs) =>
        !IsBlank(inputs?.GetValueOrDefault("aoz")) ? "red" : FieldKeys.All(k => IsBlank(inputs?.GetValueOrDefault(k))) ? "green" : "amber";

    // ================================================================== bản ghi

    public static string Str(JsonNode? n) => n switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        _ => n.ToJsonString(),
    };
    public static double? NumOf(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d
        : double.TryParse(Str(n), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : null;
    public static Dictionary<string, string> InputsOf(JsonNode? n)
    {
        var o = n as JsonObject; return FieldKeys.ToDictionary(k => k, k => o is null ? "" : Str(o[k]));
    }
    public static JsonObject InputsJson(IReadOnlyDictionary<string, string> inputs)
    {
        var o = new JsonObject(); foreach (var k in FieldKeys) o[k] = (inputs.GetValueOrDefault(k) ?? "").Trim(); return o;
    }

    /// <summary>Một bản ghi (1 lô): khóa lưu "batch:&lt;ngày&gt;:&lt;lô&gt;:&lt;id&gt;" + JSON gốc.</summary>
    public sealed class Rec
    {
        public string Key = "";
        public JsonObject J = new();
        public string Date => Str(J["date"]);
        public string Batch => Str(J["batch"]);
        public string Note => Str(J["note"]);
        public string Dat => Str(J["dat"]);
        public string KyHieu => Str(J["kyhieu"]);
        public string MoTa => Str(J["mota"]);
        public string Source => Str(J["source"]);
        public string SavedAt => Str(J["savedAt"]);
        public Dictionary<string, string> Inputs => InputsOf(J["inputs"]);
        public JsonObject? Lan1Src => J["lan1Src"] as JsonObject;
        public List<JsonObject> Lan2 => Lan2ListOf(J);
        public bool HasLan1 => Lan1Src is not null || (Source != "lan2-import" && (Dat.Length > 0 || KyHieu.Length > 0 || Lan2.Count == 0));
        public string GroupDateL1 => Lan1Src is { } s && Str(s["ngayKiem"]).Length > 0 ? Str(s["ngayKiem"]) : Date;
        public JsonObject? Lan2Latest => Lan2.OrderBy(l => Str(l["date"]), StringComparer.Ordinal).LastOrDefault();
        public int RoundOf(JsonObject l)
        {
            var ds = Lan2.Select(x => Str(x["date"])).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            return ds.IndexOf(Str(l["date"])) + 2;
        }
    }
    public static List<JsonObject> Lan2ListOf(JsonObject r) =>
        r["lan2All"] is JsonArray a && a.Count > 0 ? a.OfType<JsonObject>().ToList() : r["lan2"] is JsonObject l ? new List<JsonObject> { l } : new List<JsonObject>();
    public static string Lan2Id(JsonObject l) => $"{Str(l["so"])}|{Str(l["date"])}|{Str(l["stt"])}|{Str(l["file"])}";
    public static string PhieuKeyL2(JsonObject l) => $"L2|{Str(l["so"])}|{Str(l["date"])}|{Str(l["file"])}";
    public static string PhieuKeyL1(JsonObject src) => $"L1|{Str(src["file"])}|{Str(src["ngayKiem"])}";

    /// <summary>lan2Normalize: bỏ trùng phiếu+ngày+mẫu, xếp theo ngày phân tích; lan2 = lần mới nhất.</summary>
    public static void Lan2Normalize(JsonObject rec, IEnumerable<JsonObject>? list = null)
    {
        var src = (list ?? Lan2ListOf(rec)).ToList();
        var outList = new List<JsonObject>();
        foreach (var l in src)
        {
            var i = outList.FindIndex(x => Lan2Id(x) == Lan2Id(l));
            if (i >= 0) outList[i] = l; else outList.Add(l);
        }
        outList = outList.OrderBy(l => Str(l["date"]), StringComparer.Ordinal).ThenBy(l => Str(l["so"]), StringComparer.Ordinal).ThenBy(l => NumOf(l["stt"]) ?? 0).ToList();
        rec.Remove("lan2"); rec.Remove("lan2All");
        if (outList.Count == 0) return;
        rec["lan2All"] = new JsonArray(outList.Select(l => (JsonNode)l.DeepClone()).ToArray());
        rec["lan2"] = outList[^1].DeepClone();
    }

    /// <summary>Dòng hiển thị: chế độ Lần 1 = bản ghi; Kiểm lại = mỗi lần kiểm lại 1 dòng (trỏ về bản ghi gốc).</summary>
    public sealed class Row
    {
        public Rec R = null!;
        public JsonObject? Kl;
        public int LanNo;
        public string Date = "", Batch = "", Note = "", Dat = "", KyHieu = "", SavedAt = "";
        public Dictionary<string, string> Inputs = new();
        public string GroupDate = "";
        public string ParentBatch => R.Batch;
        public string ParentDate => R.Date;
        public List<JsonObject> Samples => Kl is not null ? new List<JsonObject> { Kl } : R.Lan2;
    }
    public const string ModeL1 = "l1", ModeKl = "kl";
    private static readonly Regex Digits = new(@"^\d+$");
    public static List<Row> BaseRows(IEnumerable<Rec> recs, string mode)
    {
        var o = new List<Row>();
        foreach (var r in recs)
        {
            if (mode == ModeL1)
            {
                if (!r.HasLan1) continue;
                o.Add(new Row { R = r, Date = r.Date, Batch = r.Batch, Note = r.Note, Dat = r.Dat, KyHieu = r.KyHieu, SavedAt = r.SavedAt, Inputs = r.Inputs, GroupDate = r.GroupDateL1 });
                continue;
            }
            foreach (var l in r.Lan2)
            {
                var numLo = Digits.IsMatch(r.Batch);
                o.Add(new Row
                {
                    R = r, Kl = l, LanNo = r.RoundOf(l), Date = Str(l["date"]), GroupDate = Str(l["date"]),
                    Batch = Str(l["mau"]) is { Length: > 0 } mau ? mau : r.Batch,
                    Note = numLo ? $"→ Lô {r.Batch} · nhập {Dmy(r.Date)}" + (r.Note.Length > 0 && r.HasLan1 ? " · " + r.Note : "") : Str(l["group"]),
                    Dat = Str(l["dat"]), KyHieu = Str(l["kyhieu"]), Inputs = InputsOf(l["inputs"]),
                    SavedAt = Str(l["savedAt"]) is { Length: > 0 } sa ? sa : r.SavedAt,
                });
            }
        }
        return o;
    }
    /// <summary>Ô "KQ kiểm lại" dạng chữ (để lọc / xuất Excel).</summary>
    public static string Lan2Text(Row row)
    {
        if (row.Kl is { } kl) return $"Lần {row.LanNo} · phiếu {Str(kl["so"])} · mẫu {Str(kl["stt"])}";
        var l = row.R.Lan2Latest; if (l is null) return "";
        return string.Join(" · ", new[] { "Lần " + row.R.RoundOf(l), Str(l["dat"]), Str(l["kyhieu"]) }.Where(x => x.Length > 0))
            + (Str(l["date"]).Length > 0 ? " · kiểm " + Dmy(Str(l["date"])) : "");
    }
    /// <summary>Xếp như app cũ: ngày mới → cũ, trong ngày theo số lô tăng dần (kiểm lại: theo phiếu + số mẫu).</summary>
    public static List<Row> Sort(IEnumerable<Row> rows, string mode)
    {
        static long Lk(string b) { var d = Regex.Replace(b, @"\D+", ""); return d.Length > 0 && d.Length < 15 ? long.Parse(d) : 1_000_000_000; }
        var q = rows.OrderByDescending(r => r.GroupDate, StringComparer.Ordinal);
        return mode == ModeKl
            ? q.ThenBy(r => Str(r.Kl!["so"]), StringComparer.Ordinal).ThenBy(r => NumOf(r.Kl!["stt"]) ?? 0).ToList()
            : q.ThenByDescending(r => r.Date, StringComparer.Ordinal).ThenBy(r => Lk(r.Batch)).ThenBy(r => r.Batch, StringComparer.Ordinal).ToList();
    }

    // ================================================================== tìm nhanh (ksHistMatcher)

    public static string NormText(string? s)
    {
        var t = (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Replace('đ', 'd').Trim();
    }
    public static (int y, int m, int d)? ParseDay(string? v)
    {
        var t = (v ?? "").Trim();
        var m = Regex.Match(t, @"^(\d{4})-(\d{1,2})-(\d{1,2})");
        if (m.Success) return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        m = Regex.Match(t, @"^(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2,4})");
        if (m.Success) { var y = int.Parse(m.Groups[3].Value); return (y < 100 ? 2000 + y : y, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)); }
        return null;
    }
    public static string LoKey(string? v)
    {
        var k = Regex.Replace(Regex.Replace(NormText(v), @"\s+", ""), @"^(lo|l)(?=\w)[.\-:]?", "");
        return Regex.IsMatch(k, @"^\d+$") ? long.Parse(k.Length > 15 ? k[..15] : k).ToString(CultureInfo.InvariantCulture) : k;
    }
    // 1.162: + MCKSS; "MAU" / "M" (= mẫu) chỉ tính là mẫu chờ khi ĐI KÈM SỐ (M2, MAU2, M 2) — "M" trơn không phải mẫu (lần 1 tên lô là L2, không có M2)
    private const string MkInner = "mcsks|mckss|mcks|mkscho|mksc|mcs|mks|mc";
    private const string MkNumInner = MkInner + "|mau|m";
    private static readonly Regex MkOnly = new("^(?:" + MkInner + ")$");
    private static readonly Regex MkNum = new(@"^(?:" + MkNumInner + @")[.\-_:#]*(\d+)$");
    private static readonly Regex MkPhrase = new(@"(?<![a-z0-9])(?:mau|m)\s*(?:khang\s*sinh|ks)\s*(cho)?(?![a-z])");
    private static readonly Regex MauCho = new(@"(?<![a-z0-9])mau\s*cho(?![a-z])");
    /// <summary>Gõ đủ chữ ("mẫu kháng sinh chờ 2", "maukhangsinhcho2", "mẫu chờ2") → dạng gọn "mks cho 2" cho các mẫu so khớp bên dưới.</summary>
    public static string MauNorm(string? normText)
    {
        var t = MkPhrase.Replace(normText ?? "", m => m.Groups[1].Success ? "mks cho " : "mks ");
        return MauCho.Replace(t, "mks cho ");
    }
    private static readonly Regex Mau1 = new(@"(?:^|[\s(\[])(?:" + MkInner + @"|mau)\s*cho\s*(?:so|mau|no|#)?\s*[.\-:]?\s*(\d+)");
    private static readonly Regex Mau2 = new(@"^(?:" + MkNumInner + @")\s*[.\-_:#]?\s*(?:so|mau|no)?\s*[.\-_:#]?\s*(\d+)(?!\d|[./\-]\d)");
    private static readonly Regex Mau3 = new(@"(?:^|[\s(\[])(?:" + MkInner + @"|mau)\s*cho(?![a-z])");
    /// <summary>Tên mẫu → (mẫu chờ?, số mẫu).</summary>
    public static (bool Cho, int? No) MauInfo(string? name)
    {
        var t = Regex.Replace(MauNorm(NormText(name)), @"\s+", " ").Trim();
        if (t.Length == 0) return (false, null);
        var m = Mau1.Match(t); if (m.Success) return (true, int.Parse(m.Groups[1].Value));
        m = Mau2.Match(t); if (m.Success) return (true, int.Parse(m.Groups[1].Value));
        return Mau3.IsMatch(t) ? (true, null) : (false, null);
    }
    private static bool LanIsMks(Row r, JsonObject l, int? no)
    {
        var i = Str(l["mau"]).Trim().Length > 0 ? MauInfo(Str(l["mau"])) : MauInfo(r.ParentBatch);
        if (!i.Cho) return false;
        if (no is null) return true;
        var stt = Str(l["stt"]).Trim();
        var n = i.No ?? (Regex.IsMatch(stt, @"^\d+$") ? int.Parse(stt) : null);
        return n == no;
    }

    public sealed class Matcher
    {
        public Func<Row, bool> Test = _ => true;
        public bool MksMode;
    }
    public static Matcher Match(string? q)
    {
        var toks = MauNorm(NormText(q)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var conds = new List<Func<Row, bool>>(); var dates = new List<(int d, int m, int? y)>();
        int? mks = null; var mksAny = false; var lotGiven = false;
        static string Cmp(string v) => Regex.Replace(v, @"\s+", "");
        static bool WordHit(string s, string w) => s.Contains(w) || Cmp(s).Contains(w);
        for (var i = 0; i < toks.Length; i++)
        {
            var t = toks[i];
            var mm = MkNum.Match(t);
            if (mm.Success) { mks = int.Parse(mm.Groups[1].Value); continue; }
            bool isMk = MkOnly.IsMatch(t) || (t is "m" or "mau" && i + 1 < toks.Length && Regex.IsMatch(toks[i + 1], @"^\d+$")), isMau = t == "mau" && i + 1 < toks.Length && toks[i + 1] == "cho";
            if (isMk || isMau)
            {
                int j = i + 1; var sawCho = isMau;
                if (isMau) j = i + 2; else if (j < toks.Length && toks[j] == "cho") { sawCho = true; j++; }
                if (j < toks.Length && toks[j] is "so" or "no") j++;
                if (j < toks.Length && Regex.IsMatch(toks[j], @"^\d+$")) { mks = int.Parse(toks[j]); i = j; continue; }
                if (sawCho) { mksAny = true; i = j - 1; continue; }
            }
            if (t is "lo" or "l" && i + 1 < toks.Length) t += toks[++i];
            var dm = Regex.Match(t, @"^(\d{1,2})[/.\-](\d{1,2})(?:[/.\-](\d{2,4}))?$");
            if (dm.Success)
            {
                int? y = dm.Groups[3].Success ? int.Parse(dm.Groups[3].Value) : null; if (y < 100) y += 2000;
                dates.Add((int.Parse(dm.Groups[1].Value), int.Parse(dm.Groups[2].Value), y)); continue;
            }
            if (Regex.IsMatch(t, @"^(lo|l)[.\-:]?[a-z0-9]") && !Regex.IsMatch(Regex.Replace(t, "^lo", "l"), @"^lo?[a-z]{2,}"))
            {
                var k = LoKey(t); lotGiven = true;
                conds.Add(r => LoKey(r.Kl is not null ? r.ParentBatch : r.Batch) == k);
                continue;
            }
            var w = t;
            conds.Add(r => WordHit(NormText(string.Join(" ", new[] { r.Batch, r.KyHieu, r.Dat, r.Note, Dmy(r.Date) }
                .Concat(r.Samples.Select(l => string.Join(" ", Str(l["mau"]), Str(l["dat"]), Str(l["kyhieu"]), Str(l["ghichu"]), "lan 2", "kiem lai"))))), w));
        }
        bool DOk(string iso) { var p = ParseDay(iso); return p is { } x && dates.All(d => x.d == d.d && x.m == d.m && (d.y is null || x.y == d.y)); }
        var mksMode = mks is not null || mksAny;
        bool LanOk(Row r, JsonObject l) => LanIsMks(r, l, mks) && (dates.Count == 0 || DOk(Str(l["date"])));
        return new Matcher
        {
            MksMode = mksMode,
            Test = r =>
            {
                if (!conds.All(c => c(r))) return false;
                if (mksMode) return r.Samples.Any(l => LanOk(r, l));
                if (dates.Count == 0) return true;
                if (lotGiven) return DOk(r.Kl is not null ? r.ParentDate : r.Date);
                return DOk(r.Date) || r.Samples.Any(l => DOk(Str(l["date"]))) || (r.Kl is null && r.R.Lan1Src is { } s && DOk(Str(s["ngayKiem"])));
            },
        };
    }

    // ================================================================== ngày

    public static string Dmy(string? iso)
    {
        var s = (iso ?? "").Trim(); if (s.Length == 0) return "";
        var m = Regex.Match(s, @"^(\d{4})-(\d{2})-(\d{2})$"); if (m.Success) return $"{m.Groups[3].Value}/{m.Groups[2].Value}/{m.Groups[1].Value}";
        m = Regex.Match(s, @"^(\d{1,2})[/\-](\d{1,2})[/\-](\d{4})$"); if (m.Success) return $"{m.Groups[1].Value.PadLeft(2, '0')}/{m.Groups[2].Value.PadLeft(2, '0')}/{m.Groups[3].Value}";
        return s;
    }
    /// <summary>excelValueToISODate: yyyy-mm-dd, d/m/yyyy, d.m.yy … → yyyy-MM-dd (không đọc được thì giữ nguyên).</summary>
    public static string ToIso(string? v)
    {
        var s = (v ?? "").Trim(); if (s.Length == 0) return "";
        var m = Regex.Match(s, @"^(\d{4})-(\d{2})-(\d{2})"); if (m.Success) return $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}";
        m = Regex.Match(s, @"^(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{4}|\d{2})$");
        if (m.Success) { var y = m.Groups[3].Value.Length == 2 ? "20" + m.Groups[3].Value : m.Groups[3].Value; return $"{y}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[1].Value.PadLeft(2, '0')}"; }
        return s;
    }
    public static bool IsIso(string? s) => Regex.IsMatch(s ?? "", @"^\d{4}-\d{2}-\d{2}$");
    public static string GuessDateFromSheetName(string? name, int? year = null)
    {
        var s = (name ?? "").Trim();
        var m = Regex.Match(s, @"(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})"); if (m.Success) return $"{m.Groups[1].Value}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[3].Value.PadLeft(2, '0')}";
        m = Regex.Match(s, @"(\d{1,2})[-/.](\d{1,2})[-/.](\d{4})"); if (m.Success) return $"{m.Groups[3].Value}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[1].Value.PadLeft(2, '0')}";
        m = Regex.Match(s, @"(?:^|\D)(\d{1,2})[-/.](\d{1,2})(?:\D|$)"); if (m.Success) return $"{year ?? DateTime.Today.Year}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[1].Value.PadLeft(2, '0')}";
        return "";
    }

    // ================================================================== nhập Excel

    public sealed record Target(string Key, string Label, bool Required, string[] Aliases);
    public static readonly Target[] ImportTargets =
    {
        new("date", "Ngày", true, new[] { "ngay", "ngày", "date", "ngay lay mau", "ngày lấy mẫu", "ngay xet nghiem", "ngày xét nghiệm", "ngay tra ket qua" }),
        new("batch", "Mã / số lô hàng", true, new[] { "lo hang", "lô hàng", "ma lo", "mã lô", "so lo", "số lô", "batch", "ma lo hang", "mã lô hàng" }),
        new("code", "Code / Mã NL (ghi kèm vào Ghi chú)", false, new[] { "code/ma", "code ma", "code", "ma nl", "mã nl", "nha cung cap", "nhà cung cấp" }),
        new("note", "Ghi chú", false, new[] { "ghi chu", "ghi chú", "note", "ao", "khach hang", "khách hàng" }),
        new("enro", "Enro", false, new[] { "enro" }),
        new("cipro", "Cipro", false, new[] { "cipro" }),
        new("oxy", "Oxy", false, new[] { "oxy", "oxytetracyclin" }),
        new("doxy", "Doxy", false, new[] { "doxy", "doxycyclin" }),
        new("sulfo", "Sulfo", false, new[] { "sulfo", "sulfa" }),
        new("aoz", "AOZ", false, new[] { "aoz" }),
        new("cap", "CAP", false, new[] { "cap", "chloramphenicol" }),
        new("dat", "Kết quả đạt (nếu file đã có)", false, new[] { "dat theo thi truong", "đạt theo thị trường", "ket qua", "kết quả", "dat", "đạt" }),
        new("kyhieu", "Ký hiệu (nếu file đã có)", false, new[] { "ky hieu", "ký hiệu", "symbol", "dac tinh", "đặc tính", "dat tinh", "đặt tính" }),
    };

    public static int DetectHeaderRow(IReadOnlyList<string[]> rows)
    {
        var pool = ImportTargets.SelectMany(t => t.Aliases).Concat(new[] { "stt", "so thu tu" }).Select(NormText).ToList();
        var maxScan = Math.Min(rows.Count, 25); int bestIdx = -1, bestScore = -1;
        for (var i = 0; i < maxScan; i++)
        {
            var row = rows[i]; var nonEmpty = row.Count(c => (c ?? "").Trim().Length > 0);
            if (nonEmpty < 2) continue;
            var match = row.Select(NormText).Count(nc => nc.Length > 0 && pool.Any(a => a.Length > 0 && (nc == a || nc.Contains(a) || a.Contains(nc))));
            if (match < 2) continue;
            var score = match * 10 + nonEmpty;
            if (score > bestScore) { bestScore = score; bestIdx = i; }
        }
        if (bestIdx >= 0) return bestIdx;
        for (var i = 0; i < maxScan; i++) if (rows[i].Count(c => (c ?? "").Trim().Length > 0) >= 3) return i;
        for (var i = 0; i < rows.Count; i++) if (rows[i].Any(c => (c ?? "").Trim().Length > 0)) return i;
        return 0;
    }
    public static int GuessColumn(IReadOnlyList<string> headers, IEnumerable<string> aliases)
    {
        var hs = headers.Select(NormText).ToList(); var al = aliases.Select(NormText).ToList();
        foreach (var a in al) { var i = hs.IndexOf(a); if (i >= 0) return i; }
        foreach (var a in al) { var i = hs.FindIndex(h => h.Length >= 2 && (h.Contains(a) || a.StartsWith(h, StringComparison.Ordinal))); if (i >= 0) return i; }
        return -1;
    }
    public static Dictionary<string, int> GuessMapping(IReadOnlyList<string> headers) =>
        ImportTargets.ToDictionary(t => t.Key, t => GuessColumn(headers, t.Aliases));
}

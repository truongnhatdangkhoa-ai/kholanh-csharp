using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.TongHop;

/// <summary>
/// ÁNH XẠ TÊN HÀNG — chép đúng logic js/m8-anhxa.js của app cũ (dữ liệu dùng chung với Tồn kho An An / Báo cáo rã đông):
///  - Loại quy tắc (đoạn ký tự của mã 21 ký tự): SP (1–4, "P" coi như "V"), TT (8–10), MB (11–13) + loại tự thêm X1, X2…;
///  - Quy tắc đơn {pv, ma, full, short}; quy tắc kết hợp {when, full, short} thắng quy tắc đơn (nhiều điều kiện hơn thắng, bằng thì đứng trên thắng);
///  - Tuỳ chọn tách theo nơi: ratd (Báo cáo rã đông, dùng tên đầy đủ) và tk (Tồn kho / tìm kiếm, dùng tên viết tắt).
/// Lưu ở sheet AnhXaTen (Phạm vi | Mã | Tên đầy đủ | Tên viết tắt; quy tắc kết hợp: Phạm vi = CB) + AnhXaCauHinh (1 ô JSON {opts, segs}).
/// </summary>
public sealed class AnhXa
{
    public const int MaxCode = 21;
    private static readonly HashSet<string> Reserved = new() { "CB", "CFG", "SEG" };

    public sealed record Seg(string Id, string Label, int From, int To) { public int Len => To - From + 1; public string Text => $"{Label} (ký tự {From}{(To > From ? "–" + To : "")})"; }
    public sealed record Rule(string Pv, string Ma, string Full, string Short);
    public sealed record Combo(Dictionary<string, string> When, string Full, string Short);

    public static readonly Seg[] DefaultSegs = { new("SP", "Tên sản phẩm", 1, 4), new("TT", "Thị trường + Khách hàng", 8, 10), new("MB", "Mạ băng", 11, 13) };
    public static readonly Rule[] DefaultRows =
    {
        new("SP", "VRHL", "BTP", "BTP"), new("SP", "VRHO", "NL", "NL"), new("SP", "VRPD", "RAW PD", ""),
        new("TT", "XEC", "ECUADOR", "Ecuador"), new("TT", "XTC", "TÔM GIA CÔNG", "TGC"), new("TT", "XAI", "ẤN ĐỘ", "AĐ"),
        new("MB", "TGS", "TGS", "TGS"),
    };

    public List<Seg> Segs { get; }
    public List<Rule> Rows { get; }
    public List<Combo> Combos { get; }
    /// <summary>opts["ratd"]["tachTT"] …</summary>
    public Dictionary<string, Dictionary<string, bool>> Opts { get; }

    private readonly Dictionary<string, Rule> _idx;

    public AnhXa(IEnumerable<Seg>? segs, IEnumerable<Rule>? rows, IEnumerable<Combo>? combos, Dictionary<string, Dictionary<string, bool>>? opts)
    {
        Segs = CleanSegs(segs);
        Rows = CleanRows(rows ?? DefaultRows, Segs);
        Combos = CleanCombos(combos, Segs);
        Opts = CleanOpts(opts, Segs);
        _idx = Rows.ToDictionary(r => r.Pv + "|" + r.Ma);
    }

    public static AnhXa Defaults() => new(DefaultSegs, DefaultRows, null, null);

    private static string Up(string? s) => Regex.Replace((s ?? "").Trim().ToUpperInvariant(), @"\s+", "");
    public static string NormMa(string id, string? v) { var u = Up(v); return id == "SP" && u.StartsWith('P') ? "V" + u[1..] : u; }

    public static List<Seg> CleanSegs(IEnumerable<Seg>? list)
    {
        var seen = new HashSet<string>(); var outp = new List<Seg>();
        foreach (var s in list ?? Enumerable.Empty<Seg>())
        {
            if (s is null) continue;
            var id = new string((s.Id ?? "").ToUpperInvariant().Where(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9').ToArray());
            if (id.Length > 6) id = id[..6];
            if (id.Length == 0 || seen.Contains(id) || Reserved.Contains(id) || s.From < 1 || s.To < s.From || s.To > MaxCode) continue;
            var def = DefaultSegs.FirstOrDefault(d => d.Id == id);
            seen.Add(id);
            outp.Add(new Seg(id, (s.Label ?? "").Trim() is { Length: > 0 } l ? l : def?.Label ?? id, s.From, s.To));
        }
        if (outp.Count == 0) return DefaultSegs.ToList();
        var spIdx = outp.FindIndex(x => x.Id == "SP");
        Seg sp;
        if (spIdx >= 0) { sp = outp[spIdx]; outp.RemoveAt(spIdx); } else sp = DefaultSegs[0];
        var missing = DefaultSegs.Skip(1).Where(d => !seen.Contains(d.Id));
        return new[] { sp }.Concat(missing).Concat(outp).ToList();
    }

    public static List<Rule> CleanRows(IEnumerable<Rule> rows, List<Seg> segs)
    {
        var S = segs.ToDictionary(s => s.Id);
        var m = new Dictionary<string, Rule>();
        foreach (var r in rows)
        {
            if (r is null) continue;
            var pv = (r.Pv ?? "").ToUpperInvariant();
            if (!S.TryGetValue(pv, out var seg)) continue;
            var ma = NormMa(pv, r.Ma);
            if (ma.Length != seg.Len) continue;
            m[pv + "|" + ma] = new Rule(pv, ma, (r.Full ?? "").Trim(), (r.Short ?? "").Trim());
        }
        return m.Values.ToList();
    }

    public static string CondKey(Dictionary<string, string> when, List<Seg> segs) =>
        string.Join("&", segs.Where(s => when.ContainsKey(s.Id) && when[s.Id].Length > 0).Select(s => s.Id + "=" + when[s.Id]));

    public static Dictionary<string, string> StrToCond(string? s)
    {
        var o = new Dictionary<string, string>();
        foreach (var p in (s ?? "").Split('&'))
        {
            var i = p.IndexOf('=');
            if (i > 0) o[p[..i].Trim().ToUpperInvariant()] = p[(i + 1)..];
        }
        return o;
    }

    public static List<Combo> CleanCombos(IEnumerable<Combo>? list, List<Seg> segs)
    {
        var S = segs.ToDictionary(s => s.Id);
        var seen = new HashSet<string>(); var outp = new List<Combo>();
        foreach (var c in list ?? Enumerable.Empty<Combo>())
        {
            if (c?.When is null) continue;
            var when = new Dictionary<string, string>(); var ok = true; var n = 0;
            foreach (var (k, v) in c.When)
            {
                var id = k.ToUpperInvariant();
                if (string.IsNullOrEmpty(v)) continue;
                if (!S.TryGetValue(id, out var seg)) { ok = false; continue; }
                var ma = NormMa(id, v);
                if (ma.Length != seg.Len) { ok = false; continue; }
                when[id] = ma; n++;
            }
            if (!ok || n < 1) continue;
            var key = CondKey(when, segs);
            if (!seen.Add(key)) continue;
            outp.Add(new Combo(when, (c.Full ?? "").Trim(), (c.Short ?? "").Trim()));
        }
        return outp;
    }

    public static Dictionary<string, Dictionary<string, bool>> CleanOpts(Dictionary<string, Dictionary<string, bool>>? o, List<Seg> segs)
    {
        var d = new Dictionary<string, Dictionary<string, bool>> { ["ratd"] = new(), ["tk"] = new() };
        foreach (var s in segs.Where(s => s.Id != "SP")) { d["ratd"]["tach" + s.Id] = true; d["tk"]["tach" + s.Id] = true; }
        if (o is not null)
            foreach (var k in new[] { "ratd", "tk" })
                if (o.TryGetValue(k, out var src))
                    foreach (var f in d[k].Keys.ToList())
                        if (src.TryGetValue(f, out var b)) d[k][f] = b;
        return d;
    }

    // ------------------------------------------------------------------ tra cứu

    private Rule? Get(string pv, string ma) => _idx.GetValueOrDefault(pv + "|" + NormMa(pv, ma));
    private static string FullOf(Rule? r) => r is null ? "" : r.Full.Length > 0 ? r.Full : r.Short;
    private static string Val(string code, Seg seg) => code.Length < seg.To ? "" : code.Substring(seg.From - 1, seg.Len);
    private bool On(bool full, Seg seg) => seg.Id == "SP" || Opts[full ? "ratd" : "tk"].GetValueOrDefault("tach" + seg.Id, true);
    private static string NameOf(bool full, Rule? r) => full ? FullOf(r) : r?.Short ?? "";

    public sealed record Hit(int Index, Combo Combo, string Name, int Conditions);

    public List<Hit> ComboHits(string code, bool full)
    {
        var hits = new List<Hit>();
        var S = Segs.ToDictionary(s => s.Id);
        for (var i = 0; i < Combos.Count; i++)
        {
            var c = Combos[i];
            var name = full ? (c.Full.Length > 0 ? c.Full : c.Short) : c.Short;
            if (name.Length == 0) continue;
            var ok = true;
            foreach (var (id, want) in c.When)
            {
                if (!S.TryGetValue(id, out var seg) || !On(full, seg) || NormMa(id, Val(code, seg)) != want) { ok = false; break; }
            }
            if (ok) hits.Add(new Hit(i, c, name, c.When.Count));
        }
        return hits.OrderByDescending(h => h.Conditions).ThenBy(h => h.Index).ToList();
    }

    public sealed record Step(string Id, string Ma, string Name);
    public sealed record Resolution(string Label, string Via, Combo? Combo, int ComboIndex, List<Step> Steps, List<int> Conflicts);

    private (string label, List<Step> steps) Chain(string code, bool full, string? @base)
    {
        var steps = new List<Step>();
        var sp = Segs[0];
        var v0 = NormMa("SP", Val(code, sp));
        var label = NameOf(full, Get("SP", v0));
        if (label.Length > 0) steps.Add(new Step("SP", v0, label));
        else if (!full) return ("", steps);
        else label = @base ?? "";
        foreach (var seg in Segs.Skip(1))
        {
            if (!On(full, seg)) continue;
            var v = NormMa(seg.Id, Val(code, seg));
            var n = NameOf(full, Get(seg.Id, v));
            if (n.Length > 0) { label = n; steps.Add(new Step(seg.Id, v, n)); }
        }
        return (label, steps);
    }

    public Resolution Resolve(string? code, bool full, string? @base = null)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        var hits = ComboHits(code, full);
        var ch = Chain(code, full, @base);
        if (hits.Count > 0)
        {
            var top = hits[0];
            return new Resolution(top.Name, "combo", top.Combo, top.Index, ch.steps,
                hits.Where(h => h != top && h.Conditions == top.Conditions && h.Name != top.Name).Select(h => h.Index).ToList());
        }
        return new Resolution(ch.label, ch.steps.Count > 0 ? "rules" : full ? "base" : "none", null, -1, ch.steps, new());
    }

    /// <summary>Tên đầy đủ (Báo cáo rã đông). base = nhãn gốc khi SP chưa có tên.</summary>
    public string FullLabel(string code, string? @base = null) => Resolve(code, true, @base).Label;
    /// <summary>Tiền tố nhãn viết tắt (Tồn kho An An / tìm kiếm); "" = không đổi tên.</summary>
    public string ShortPrefix(string code) => Resolve(Up(code), false).Label;
    public string SpFull(string c4) => FullOf(Get("SP", c4));

    /// <summary>Cặp quy tắc kết hợp cùng số điều kiện có thể cùng khớp mà cho tên khác nhau.</summary>
    public List<(int, int)> Overlaps()
    {
        var outp = new List<(int, int)>();
        for (var a = 0; a < Combos.Count; a++)
            for (var b = a + 1; b < Combos.Count; b++)
            {
                var A = Combos[a]; var B = Combos[b];
                if (A.When.Count != B.When.Count) continue;
                if (A.When.Any(k => B.When.TryGetValue(k.Key, out var bv) && bv != k.Value)) continue;
                if (A.Full == B.Full && A.Short == B.Short) continue;
                outp.Add((a, b));
            }
        return outp;
    }

    // ------------------------------------------------------------------ sheet

    public Dictionary<string, JsonArray?> ToSheets()
    {
        var ten = new JsonArray();
        foreach (var r in Rows) ten.Add(new JsonObject { ["Phạm vi"] = r.Pv, ["Mã"] = r.Ma, ["Tên đầy đủ"] = r.Full, ["Tên viết tắt"] = r.Short });
        foreach (var c in Combos) ten.Add(new JsonObject { ["Phạm vi"] = "CB", ["Mã"] = CondKey(c.When, Segs), ["Tên đầy đủ"] = c.Full, ["Tên viết tắt"] = c.Short });
        var opts = new JsonObject();
        foreach (var (k, v) in Opts) opts[k] = new JsonObject(v.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value))));
        var cfg = new JsonObject
        {
            ["opts"] = opts,
            ["segs"] = new JsonArray(Segs.Select(s => (JsonNode)new JsonObject { ["id"] = s.Id, ["label"] = s.Label, ["from"] = s.From, ["to"] = s.To }).ToArray()),
        };
        return new() { ["AnhXaTen"] = ten, ["AnhXaCauHinh"] = new JsonArray(new JsonObject { ["data"] = "__JSON__" + cfg.ToJsonString() }) };
    }

    private static string S(JsonNode? n) => n switch { null => "", JsonValue v when v.TryGetValue<string>(out var s) => s, _ => n.ToString() };
    private static int I(JsonNode? n) => int.TryParse(S(n), out var i) ? i : 0;

    /// <summary>Đọc từ 2 sheet; chưa có cấu hình → null (dùng mặc định).</summary>
    public static AnhXa? FromSheets(JsonArray? rows, JsonArray? cfgSheet)
    {
        var c = cfgSheet?.FirstOrDefault()?["data"] is JsonNode d ? S(d) : "";
        if (!c.StartsWith("__JSON__")) return null;
        JsonObject cfg;
        try { cfg = JsonNode.Parse(c[8..]) as JsonObject ?? throw new Exception(); } catch { return null; }
        var segs = CleanSegs((cfg["segs"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(o => new Seg(S(o["id"]), S(o["label"]), I(o["from"]), I(o["to"]))));
        var opts = new Dictionary<string, Dictionary<string, bool>>();
        if (cfg["opts"] is JsonObject oo)
            foreach (var (k, v) in oo)
                if (v is JsonObject vo)
                    opts[k] = vo.Where(p => p.Value is JsonValue jv && jv.TryGetValue<bool>(out _)).ToDictionary(p => p.Key, p => p.Value!.GetValue<bool>());
        var rules = new List<Rule>(); var combos = new List<Combo>();
        foreach (var r in (rows ?? new JsonArray()).OfType<JsonObject>())
        {
            var pv = S(r["Phạm vi"]).ToUpperInvariant();
            if (pv == "CB") combos.Add(new Combo(StrToCond(S(r["Mã"])), S(r["Tên đầy đủ"]), S(r["Tên viết tắt"])));
            else rules.Add(new Rule(pv, S(r["Mã"]), S(r["Tên đầy đủ"]), S(r["Tên viết tắt"])));
        }
        return new AnhXa(segs, rules, combos, opts);
    }
}

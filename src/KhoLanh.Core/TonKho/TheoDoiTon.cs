using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static KhoLanh.Core.TonKho.JsText;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// Tab "Theo Dõi Tồn" của Tồn kho An An (port theo-doi-ton.js): mã Thành Phẩm Chính GẦN HẾT (tồn gộp Mã hàng + Hợp đồng ≤ ngưỡng)
/// hoặc TỒN LÂU (số tháng liên tiếp còn tồn ≥ ngưỡng tháng), kèm ghi chú lý do giữ qua các lần nạp.
/// Lưu sheet "TheoDoiTon": JSON {v, notes, snaps, months, lastSig} cắt 30.000 ký tự / dòng như app cũ.
/// </summary>
public sealed class TheoDoiTon
{
    public const int MaxSnapMonths = 24, Chunk = 30000, DefaultMonths = 2;

    /// <summary>Ghi chú theo khóa "Mã|Hợp đồng" (giữ nguyên mọi trường: ma, hd, ten, size, text, at, confirmedAt, lastQty).</summary>
    public List<(string Key, JsonObject Note)> Notes = new();
    /// <summary>Ảnh chụp tồn theo tháng "yyyy-MM" → [[khóa, số lượng]], theo thứ tự thêm.</summary>
    public List<(string Ym, List<(string Key, double Qty)> Items)> Snaps = new();
    public int Months = DefaultMonths;
    public string LastSig = "";

    public JsonObject? NoteOf(string key) => Notes.FirstOrDefault(n => n.Key == key).Note;

    // ------------------------------------------------------------------ lưu / đọc

    public static TheoDoiTon FromRows(JsonArray rows)
    {
        var t = new TheoDoiTon();
        var s = string.Concat(rows.Select(r => AnAnStore.S((r as JsonObject)?["data"])));
        if (!s.StartsWith(AnAnStore.JsonTag)) return t;
        JsonObject? d;
        try { d = JsonNode.Parse(s[8..]) as JsonObject; } catch { return t; }
        if (d is null) return t;
        if (d["notes"] is JsonObject no) foreach (var (k, v) in no) if (v is JsonObject o) t.Notes.Add((k, (JsonObject)o.DeepClone()));
        if (d["snaps"] is JsonObject so)
            foreach (var (k, v) in so)
                t.Snaps.Add((k, (v as JsonArray ?? new()).OfType<JsonArray>().Select(a => (AnAnStore.S(a.Count > 0 ? a[0] : null), a.Count > 1 && a[1] is JsonValue q && q.TryGetValue<double>(out var x) ? x : 0)).ToList()));
        var m = d["months"] is JsonValue mv && mv.TryGetValue<double>(out var mm) ? mm : double.NaN;
        t.Months = double.IsFinite(m) && m >= 1 ? (int)Math.Floor(m) : DefaultMonths;
        t.LastSig = d["lastSig"] is JsonValue lv && lv.TryGetValue<string>(out var ls) ? ls : "";
        return t;
    }

    public JsonArray ToRows()
    {
        var notes = new JsonObject(); foreach (var (k, n) in Notes) notes[k] = n.DeepClone();
        var snaps = new JsonObject();
        foreach (var (ym, items) in Snaps) snaps[ym] = new JsonArray(items.Select(i => (JsonNode)new JsonArray(i.Key, JsonValue.Create(i.Qty))).ToArray());
        var json = new JsonObject { ["v"] = 1, ["notes"] = notes, ["snaps"] = snaps, ["months"] = Months, ["lastSig"] = LastSig }.ToJsonString(AnAnStore.Opt);
        var rows = new JsonArray();
        for (var i = 0; i < json.Length; i += Chunk) rows.Add(new JsonObject { ["data"] = (i == 0 ? AnAnStore.JsonTag : "") + json.Substring(i, Math.Min(Chunk, json.Length - i)) });
        return rows;
    }

    // ------------------------------------------------------------------ tính

    public sealed class Group { public string Key = "", Ma = "", Hd = "", Ten = "", Size = "", Dvt = ""; public List<string> Dts = new(); public double Qty; }
    public sealed record Age(int Months, string StartYm, bool Estimated);
    public sealed class Item { public Group G = null!; public bool IsLow, IsOld; public Age Age = null!; public JsonObject? Note; public string Dt = ""; }
    public sealed class ListResult { public List<Item> Items = new(); public List<(string Key, JsonObject Note)> Done = new(); public double Th; public string CurYm = ""; }

    private static int? YmIdx(string? ym) { var m = Regex.Match(ym ?? "", @"^(\d{4})-(\d{2})$"); return m.Success ? int.Parse(m.Groups[1].Value) * 12 + int.Parse(m.Groups[2].Value) - 1 : null; }
    private static string IdxToYm(int i) => $"{i / 12}-{(i % 12 + 1):00}";
    public static string YmLabel(string ym) { var m = Regex.Match(ym ?? "", @"^(\d{4})-(\d{2})$"); return m.Success ? $"{m.Groups[2].Value}/{m.Groups[1].Value}" : "—"; }

    public static string CurrentYm(AnAnData d)
    {
        var m = Regex.Match(JsTrim(d.DataDate), @"^(\d{1,2})/(\d{1,2})/(\d{4})$");
        if (m.Success) return $"{m.Groups[3].Value}-{int.Parse(m.Groups[2].Value):00}";
        var t = d.ImportedAt is double ms ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).ToLocalTime().DateTime : DateTime.Now;
        return $"{t.Year}-{t.Month:00}";
    }

    private static string? ContractYm(string hd)
    {
        var m = Regex.Match(hd ?? "", @"^\s*(\d{2})\.(\d{2})\.");
        if (!m.Success) return null;
        var mo = int.Parse(m.Groups[2].Value); if (mo < 1 || mo > 12) return null;
        return $"20{m.Groups[1].Value}-{m.Groups[2].Value}";
    }

    public static Dictionary<string, Group>? BuildGroups(AnAnData d, out List<string> order)
    {
        order = new();
        if (!d.HasData) return null;
        var cols = d.FilterCols();
        int? Ci(string l) => cols.FirstOrDefault(c => c.Label == l)?.Ci;
        var cMa = Ci("Mã hàng"); var cHd = Ci("Hợp đồng"); var cTon = Ci("Tồn cuối");
        if (cMa is null || cHd is null || cTon is null) return null;
        var cTen = Ci("Tên hàng"); var cSize = Ci("Size"); var cDvt = Ci("ĐVT"); var cDt = Ci("Đặc tính");
        var map = new Dictionary<string, Group>();
        foreach (var i in d.BaseRows("tpc"))
        {
            var ma = JsTrim(d.Cell(i, cMa.Value)); if (ma.Length == 0) continue;
            var hd = JsTrim(d.Cell(i, cHd.Value));
            var k = ma + "|" + hd;
            if (!map.TryGetValue(k, out var g)) { g = new Group { Key = k, Ma = ma, Hd = hd }; map[k] = g; order.Add(k); }
            var q = JsNumber(d.Cell(i, cTon.Value)); g.Qty += double.IsNaN(q) ? 0 : q;
            if (g.Ten.Length == 0 && cTen is int a) g.Ten = JsTrim(d.Cell(i, a));
            if (g.Size.Length == 0 && cSize is int b) g.Size = JsTrim(d.Cell(i, b));
            if (g.Dvt.Length == 0 && cDvt is int c) g.Dvt = JsTrim(d.Cell(i, c));
            if (cDt is int e) { var x = JsTrim(d.Cell(i, e)); if (x.Length > 0 && !g.Dts.Contains(x)) g.Dts.Add(x); }
        }
        return map;
    }

    /// <summary>Chụp tồn tháng hiện tại khi dữ liệu Load Data đổi (ensureSnapshot). Trả true nếu có thay đổi cần lưu.</summary>
    public bool EnsureSnapshot(AnAnData d)
    {
        var groups = BuildGroups(d, out var order);
        if (groups is null) return false;
        double tot = 0; foreach (var k in order) tot += groups[k].Qty;
        var sig = string.Join("|", d.FileName, d.ImportedAt is double ia ? JsStr(ia) : "", d.DataDate, order.Count.ToString(CultureInfo.InvariantCulture), JsStr(JsRound(tot * 100)));
        if (sig == LastSig) return false;
        var ym = CurrentYm(d);
        var arr = order.Select(k => groups[k]).Where(g => g.Qty > 0).Select(g => (g.Key, JsRound(g.Qty * 1000) / 1000)).ToList();
        var at = Snaps.FindIndex(s => s.Ym == ym);
        if (at >= 0) Snaps[at] = (ym, arr); else Snaps.Add((ym, arr));
        var yms = Snaps.Select(s => s.Ym).OrderBy(x => x, StringComparer.Ordinal).ToList();
        while (yms.Count > MaxSnapMonths) { var y = yms[0]; yms.RemoveAt(0); Snaps.RemoveAll(s => s.Ym == y); }
        LastSig = sig;
        return true;
    }

    private static Age AgeOf(string key, string curYm, Dictionary<string, HashSet<string>> sets)
    {
        var cur = YmIdx(curYm) ?? 0;
        var yms = sets.Keys.Where(y => YmIdx(y) <= cur).OrderBy(y => y, StringComparer.Ordinal).ToList();
        int? start = null;
        for (var i = yms.Count - 1; i >= 0; i--) { if (sets[yms[i]].Contains(key)) start = YmIdx(yms[i]); else break; }
        var earliest = yms.Count > 0 ? YmIdx(yms[0]) ?? cur : cur;
        var est = false;
        start ??= cur;
        if (start == earliest)
        {
            var parts = key.Split('|');
            var hdIdx = YmIdx(ContractYm(parts.Length > 1 ? parts[1] : ""));
            if (hdIdx is int h && h < start && h <= cur) { start = h; est = true; }
        }
        return new Age(cur - start.Value + (est ? 0 : 1), IdxToYm(start.Value), est);
    }

    public ListResult? Compute(AnAnData d, out bool snapChanged)
    {
        snapChanged = false;
        var groups = BuildGroups(d, out var order);
        if (groups is null) return null;
        snapChanged = EnsureSnapshot(d);
        var cur = CurrentYm(d);
        var sets = Snaps.ToDictionary(s => s.Ym, s => s.Items.Select(x => x.Key).ToHashSet());
        sets[cur] = order.Where(k => groups[k].Qty > 0).ToHashSet();
        var th = double.IsFinite(d.TpcThreshold) && d.TpcThreshold >= 0 ? d.TpcThreshold : 8;
        var res = new ListResult { Th = th, CurYm = cur };
        foreach (var k in order)
        {
            var g = groups[k]; if (g.Qty <= 0) continue;
            var low = g.Qty <= th; var age = AgeOf(k, cur, sets); var old = age.Months >= Months;
            if (!low && !old) continue;
            res.Items.Add(new Item { G = g, IsLow = low, IsOld = old, Age = age, Note = NoteOf(k), Dt = string.Join(" / ", g.Dts) });
        }
        foreach (var (k, n) in Notes) if (!groups.TryGetValue(k, out var gr) || gr.Qty <= 0) res.Done.Add((k, n));
        res.Items = AnAnData.StableSort(res.Items, (a, b) =>
        {
            var c = b.Age.Months - a.Age.Months; if (c != 0) return c;
            c = string.Compare(a.G.Ma, b.G.Ma, StringComparison.InvariantCulture); if (c != 0) return c;
            return string.Compare(a.G.Hd, b.G.Hd, StringComparison.InvariantCulture);
        });
        return res;
    }

    public static bool Match(Item it, string filter) => filter switch
    {
        "low" => it.IsLow, "old" => it.IsOld, "both" => it.IsLow && it.IsOld,
        "nonote" => !HasText(it.Note), "note" => HasText(it.Note), _ => true,
    };

    public static bool HasText(JsonObject? n) => n is not null && AnAnStore.S(n["text"]).Length > 0;

    public static List<Item> Visible(ListResult l, string filter, string q)
    {
        var fq = Fuzzy(q);
        return l.Items.Where(it => Match(it, filter) && (fq.Length == 0 || Fuzzy(string.Join(" ", it.G.Ma, it.G.Ten, it.G.Size, it.G.Hd, it.Dt, it.Note is null ? "" : AnAnStore.S(it.Note["text"]))).Contains(fq))).ToList();
    }

    public static string WhenText(JsonObject? n)
    {
        if (!HasText(n)) return "";
        var conf = n!["confirmedAt"] is JsonValue cv && cv.TryGetValue<string>(out var c) && c.Length > 0 ? c : null;
        var a = FmtDT(conf ?? AnAnStore.S(n["at"]));
        return (conf is not null ? "Xác nhận " : "Ghi ") + a;
    }

    private static string FmtDT(string iso)
    {
        if (string.IsNullOrEmpty(iso) || !DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) return "";
        var t = d.ToLocalTime();
        return $"{t.Day:00}/{t.Month:00}/{t.Year}";
    }

    private static string NowIso() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>Ghi / xóa ghi chú (saveNote). Trả true nếu có thay đổi.</summary>
    public bool SaveNote(string key, string text, Group? g)
    {
        text = JsTrim(text ?? "");
        var at = Notes.FindIndex(n => n.Key == key);
        var old = at >= 0 ? Notes[at].Note : null;
        if (text.Length == 0) { if (old is null) return false; Notes.RemoveAt(at); return true; }
        if (old is not null && AnAnStore.S(old["text"]) == text) return false;
        var n = old is null ? new JsonObject() : (JsonObject)old.DeepClone();
        var parts = key.Split('|');
        n["ma"] = g?.Ma ?? parts[0]; n["hd"] = g?.Hd ?? (parts.Length > 1 ? parts[1] : "");
        n["ten"] = g?.Ten ?? (old is null ? "" : AnAnStore.S(old["ten"])); n["size"] = g?.Size ?? (old is null ? "" : AnAnStore.S(old["size"]));
        n["text"] = text; n["at"] = NowIso(); n["confirmedAt"] = null;
        n["lastQty"] = g?.Qty ?? (old?["lastQty"] is JsonValue lq && lq.TryGetValue<double>(out var x) ? x : 0);
        if (at >= 0) Notes[at] = (key, n); else Notes.Add((key, n));
        return true;
    }

    public bool Confirm(string key) { var n = NoteOf(key); if (n is null) return false; n["confirmedAt"] = NowIso(); return true; }
    public bool DeleteNote(string key) => Notes.RemoveAll(n => n.Key == key) > 0;

    // ------------------------------------------------------------------ xuất Excel

    /// <summary>TheoDoiTon_TPC_{dd-mm-yyyy}.xlsx — sheet "Theo dõi tồn" (các dòng đang hiện) + "Đã hết".</summary>
    public static XlsxOut Export(ListResult l, List<Item> items)
    {
        var head = new object?[] { "STT", "Mã hàng", "Tên hàng", "Size", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn cuối", "Loại", "Số tháng tồn", "Từ tháng", "Lý do còn tồn / ghi chú", "Ngày ghi" };
        var a1 = new List<object?[]> { head };
        a1.AddRange(items.Select((it, i) => new object?[]
        {
            (double)(i + 1), it.G.Ma, it.G.Ten, it.G.Size, it.Dt, it.G.Hd, it.G.Dvt, it.G.Qty,
            string.Join(" + ", new[] { it.IsLow ? "Gần hết" : "", it.IsOld ? "Tồn lâu" : "" }.Where(x => x.Length > 0)),
            (it.Age.Estimated ? "~" : "") + it.Age.Months, YmLabel(it.Age.StartYm), it.Note is null ? "" : AnAnStore.S(it.Note["text"]), WhenText(it.Note),
        }));
        var a2 = new List<object?[]> { new object?[] { "STT", "Mã hàng", "Tên hàng", "Size", "Hợp đồng", "Lý do (ghi chú cũ)", "Ngày ghi" } };
        a2.AddRange(l.Done.Select((x, i) =>
        {
            var p = x.Key.Split('|');
            string F(string k, string dflt) { var v = AnAnStore.S(x.Note[k]); return v.Length > 0 ? v : dflt; }
            return new object?[] { (double)(i + 1), F("ma", p[0]), F("ten", ""), F("size", ""), F("hd", p.Length > 1 ? p[1] : ""), F("text", ""), WhenText(x.Note) };
        }));
        return new XlsxOut
        {
            Sheets =
            {
                new XlsxOut.Sheet { Name = "Theo dõi tồn", Aoa = a1, Widths = new double[] { 6, 26, 48, 9, 14, 18, 7, 11, 18, 12, 10, 46, 20 } },
                new XlsxOut.Sheet { Name = "Đã hết", Aoa = a2, Widths = new double[] { 6, 26, 48, 9, 18, 46, 20 } },
            },
        };
    }

    public static string FileName(AnAnData d) => $"TheoDoiTon_TPC_{(d.DataDate.Replace('/', '-') is { Length: > 0 } x ? x : "ngay")}.xlsx";
}

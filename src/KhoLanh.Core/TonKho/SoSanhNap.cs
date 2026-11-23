using System.Text.Json.Nodes;
using static KhoLanh.Core.TonKho.JsText;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// So sánh 2 lần nạp Tồn kho An An (port so-sanh-nap.js + m1SnapNow / m1SaveSnap): ảnh chụp gộp theo Mã hàng
/// {ma, ten, dt, size, ton, kg}, lưu sheet "LanNapTruoc" mỗi khi nạp file MỚI (giữ số liệu TRƯỚC khi thay).
/// </summary>
public static class SoSanhNap
{
    public const double Eps = 0.0005;

    public sealed class Item
    {
        public string Ma = "", Ten = "", Dt = "", Size = "";
        public double Ton, Kg;
    }

    public sealed record Row(string Ma, string Ten, string Dt, string Size, double Before, double After, double KgBefore, double KgAfter, string Status)
    {
        public double Diff => After - Before;
        public double KgDiff => KgAfter - KgBefore;
    }

    public sealed class Grp { public string Name = ""; public int N; public double Before, After, KgBefore, KgAfter; public double Diff => After - Before; public double KgDiff => KgAfter - KgBefore; }

    public sealed class Sum { public int Moi, Het, Tang, Giam, Doi0; public double KgBefore, KgAfter; public double KgDiff => KgAfter - KgBefore; }

    public sealed class Result { public List<Row> Rows = new(); public Sum Sum = new(); public List<Grp> ByDt = new(), BySize = new(); }

    public static readonly (string Key, string Label)[] Status = { ("moi", "Mới xuất hiện"), ("het", "Đã hết"), ("giam", "Giảm"), ("tang", "Tăng"), ("doi0", "Không đổi") };
    public static string StatusLabel(string k) => Status.FirstOrDefault(s => s.Key == k).Label ?? k;

    /// <summary>m1SnapNow: mỗi dòng Load Data → {mã, tên, nhóm đặc tính (đã gộp) hoặc đặc tính, size, tồn, kg = tồn × trọng lượng}.</summary>
    public static List<Item> SnapNow(AnAnData d)
    {
        var H = d.Headers;
        int Ci(string l) => FindCol(H, l);
        int iMa = Ci("Mã hàng"), iTen = Ci("Tên hàng"), iDt = Ci("Đặc tính"), iSize = Ci("Size");
        var b = d.WeightBasisAll();
        if (iMa == -1 || d.Rows.Count == 0) return new();
        var iTon = b?.Qty ?? -1;
        var res = new List<Item>(d.Rows.Count);
        for (var r = 0; r < d.Rows.Count; r++)
        {
            var ton = iTon == -1 ? double.NaN : LooseNum(d.Cell(r, iTon));
            var w = b is { } bb ? LooseNum(d.Cell(r, bb.W)) : double.NaN;
            var dtRaw = iDt == -1 ? "" : JsTrim(d.Cell(r, iDt));
            res.Add(new Item
            {
                Ma = JsTrim(d.Cell(r, iMa)), Ten = iTen == -1 ? "" : JsTrim(d.Cell(r, iTen)),
                Dt = (dtRaw.Length > 0 ? d.GroupOf(dtRaw) : null) ?? dtRaw,
                Size = iSize == -1 ? "" : JsTrim(d.Cell(r, iSize)),
                Ton = double.IsNaN(ton) ? 0 : ton, Kg = double.IsNaN(ton) || double.IsNaN(w) ? 0 : ton * w,
            });
        }
        return res;
    }

    /// <summary>build: gộp theo Mã hàng (giữ thứ tự xuất hiện), cộng tồn + kg, lấy tên / đặc tính / size đầu tiên khác rỗng.</summary>
    public static List<Item> Build(IEnumerable<Item> items)
    {
        var m = new Dictionary<string, Item>(); var order = new List<Item>();
        foreach (var it in items)
        {
            var ma = JsTrim(it.Ma ?? ""); if (ma.Length == 0) continue;
            if (!m.TryGetValue(ma, out var e)) { e = new Item { Ma = ma, Ten = JsTrim(it.Ten ?? ""), Dt = JsTrim(it.Dt ?? ""), Size = JsTrim(it.Size ?? "") }; m[ma] = e; order.Add(e); }
            e.Ton += Fin(it.Ton); e.Kg += Fin(it.Kg);
            if (e.Ten.Length == 0) e.Ten = JsTrim(it.Ten ?? "");
            if (e.Dt.Length == 0) e.Dt = JsTrim(it.Dt ?? "");
            if (e.Size.Length == 0) e.Size = JsTrim(it.Size ?? "");
        }
        return order;
    }

    private static double Fin(double v) => double.IsFinite(v) ? v : 0;

    public static Result Compare(List<Item> prev, List<Item> next)
    {
        var P = Build(prev).ToDictionary(x => x.Ma); var N = Build(next).ToDictionary(x => x.Ma);
        var keys = new List<string>(); var seen = new HashSet<string>();
        foreach (var k in Build(prev).Select(x => x.Ma).Concat(Build(next).Select(x => x.Ma))) if (seen.Add(k)) keys.Add(k);
        var res = new Result();
        foreach (var ma in keys)
        {
            P.TryGetValue(ma, out var a); N.TryGetValue(ma, out var b);
            double ta = a?.Ton ?? 0, tb = b?.Ton ?? 0, ka = a?.Kg ?? 0, kb = b?.Kg ?? 0;
            var rf = b ?? a!;
            string st;
            if (a is null || ta <= Eps) st = tb > Eps ? "moi" : "khong";
            else if (b is null || tb <= Eps) st = "het";
            else if (Math.Abs(tb - ta) <= Eps) st = "doi0";
            else st = tb > ta ? "tang" : "giam";
            if (st == "khong") continue;
            res.Rows.Add(new Row(ma, rf.Ten, rf.Dt.Length > 0 ? rf.Dt : "(chưa có đặc tính)", rf.Size.Length > 0 ? rf.Size : "(không Size)", ta, tb, ka, kb, st));
        }
        List<Grp> G(Func<Row, string> f)
        {
            var g = new Dictionary<string, Grp>(); var ord = new List<Grp>();
            foreach (var r in res.Rows)
            {
                var k = f(r);
                if (!g.TryGetValue(k, out var e)) { e = new Grp { Name = k }; g[k] = e; ord.Add(e); }
                e.N++; e.Before += r.Before; e.After += r.After; e.KgBefore += r.KgBefore; e.KgAfter += r.KgAfter;
            }
            return AnAnData.StableSort(ord, (x, y) => Math.Abs(y.KgDiff).CompareTo(Math.Abs(x.KgDiff)));
        }
        foreach (var r in res.Rows)
        {
            switch (r.Status) { case "moi": res.Sum.Moi++; break; case "het": res.Sum.Het++; break; case "tang": res.Sum.Tang++; break; case "giam": res.Sum.Giam++; break; default: res.Sum.Doi0++; break; }
            res.Sum.KgBefore += r.KgBefore; res.Sum.KgAfter += r.KgAfter;
        }
        var byDt = G(r => r.Dt); var bySize = G(r => r.Size);
        var ordS = new Dictionary<string, int> { ["moi"] = 0, ["het"] = 1, ["giam"] = 2, ["tang"] = 3, ["doi0"] = 4 };
        res.Rows = AnAnData.StableSort(res.Rows, (x, y) => { var c = ordS[x.Status] - ordS[y.Status]; return c != 0 ? c : Math.Abs(y.KgDiff).CompareTo(Math.Abs(x.KgDiff)); });
        res.ByDt = byDt; res.BySize = bySize;
        return res;
    }

    // ------------------------------------------------------------------ lưu sheet LanNapTruoc

    public sealed class Saved { public string FileName = "", DataDate = ""; public double ImportedAt, SavedAt; public List<Item> Snap = new(); }

    public static JsonArray? SnapSheet(AnAnData d, double nowMs)
    {
        if (d.Rows.Count == 0) return null;
        var snap = Build(SnapNow(d));
        if (snap.Count == 0) return null;
        var meta = new JsonObject { ["fileName"] = d.FileName, ["dataDate"] = d.DataDate, ["importedAt"] = d.ImportedAt is double t ? JsonValue.Create((long)t) : 0, ["savedAt"] = (long)nowMs };
        var arr = new JsonArray(snap.Select(x => (JsonNode)new JsonObject { ["ma"] = x.Ma, ["ten"] = x.Ten, ["dt"] = x.Dt, ["size"] = x.Size, ["ton"] = Num(x.Ton), ["kg"] = Num(x.Kg) }).ToArray());
        return AnAnStore.JsonCell(new JsonObject { ["meta"] = meta, ["snap"] = arr });
    }

    private static JsonNode Num(double v) => v == Math.Floor(v) && Math.Abs(v) < 9e15 ? JsonValue.Create((long)v) : JsonValue.Create(v);

    public static Saved? ReadSaved(JsonNode? node)
    {
        if (node is not JsonObject o || o["snap"] is not JsonArray a || a.Count == 0) return null;
        var m = o["meta"] as JsonObject;
        double D(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var x) ? x : n is JsonValue s && s.TryGetValue<string>(out var str) ? JsNumber(str) : 0;
        return new Saved
        {
            FileName = AnAnStore.S(m?["fileName"]), DataDate = AnAnStore.S(m?["dataDate"]), ImportedAt = D(m?["importedAt"]), SavedAt = D(m?["savedAt"]),
            Snap = a.OfType<JsonObject>().Select(x => new Item { Ma = AnAnStore.S(x["ma"]), Ten = AnAnStore.S(x["ten"]), Dt = AnAnStore.S(x["dt"]), Size = AnAnStore.S(x["size"]), Ton = D(x["ton"]), Kg = D(x["kg"]) }).ToList(),
        };
    }

    /// <summary>so_sanh_2_lan_nap.xlsx: TheoMa · TheoNhom · TheoSize (aoa_to_sheet thường, cố định dòng đầu).</summary>
    public static XlsxOut Export(Result R)
    {
        var a1 = new List<object?[]> { new object?[] { "Mã hàng", "Tên hàng", "Nhóm đặc tính", "Size", "Tình trạng", "Tồn trước", "Tồn sau", "Chênh", "kg trước", "kg sau", "Chênh kg" } };
        a1.AddRange(R.Rows.Select(r => new object?[] { r.Ma, r.Ten, r.Dt, r.Size, StatusLabel(r.Status), r.Before, r.After, r.Diff, r.KgBefore, r.KgAfter, r.KgDiff }));
        List<object?[]> Gs(string head, List<Grp> gs)
        {
            var a = new List<object?[]> { new object?[] { head, "Số mã", "Tồn trước", "Tồn sau", "Chênh", "kg trước", "kg sau", "Chênh kg" } };
            a.AddRange(gs.Select(g => new object?[] { g.Name, (double)g.N, g.Before, g.After, g.Diff, g.KgBefore, g.KgAfter, g.KgDiff }));
            return a;
        }
        return new XlsxOut
        {
            Sheets =
            {
                new XlsxOut.Sheet { Name = "TheoMa", Aoa = a1, Widths = Array.Empty<double>() },
                new XlsxOut.Sheet { Name = "TheoNhom", Aoa = Gs("Nhóm đặc tính", R.ByDt), Widths = Array.Empty<double>() },
                new XlsxOut.Sheet { Name = "TheoSize", Aoa = Gs("Size", R.BySize), Widths = Array.Empty<double>() },
            },
        };
    }
}

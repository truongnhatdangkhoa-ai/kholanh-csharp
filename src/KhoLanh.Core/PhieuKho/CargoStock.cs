using System.Text.Json.Nodes;
using KhoLanh.Core.KiemKe;

namespace KhoLanh.Core.PhieuKho;

/// <summary>
/// Dò TỒN KHO AN AN theo MÃ HÀNG cho Cargo list (port stockCheck / maSums / similarMa của pnx-cargo-core.js):
/// so tổng kiện của cả cont theo từng mã hàng với tồn gốc ở module Tồn kho An An (không trừ các cont đã lưu).
/// </summary>
public static class CargoStock
{
    public sealed record Item(string Ma, double Ton, string Ten, string Dt);
    public sealed record Similar(string Ma, double Ton, string Ten, string Dt, List<string> Diff);
    /// <summary>St: ok | over (cần &gt; tồn) | zero (tồn 0 / âm) | none (không có mã trong tồn).</summary>
    public sealed record Result(string Ma, string St, double Ton, double Need, List<Similar> Similar)
    {
        public string Text => St switch
        {
            "ok" => $"Đủ tồn ({Need:#,0.##} / {Ton:#,0.##} kiện)",
            "over" => $"Vượt tồn: cần {Need:#,0.##} kiện, tồn An An chỉ còn {Ton:#,0.##}",
            "zero" => $"Tồn An An của mã này = {Ton:#,0.##} (hết hàng)",
            _ => "Mã không có trong Tồn kho An An",
        } + (Similar.Count > 0 ? " — gần giống: " + string.Join("; ", Similar.Select(s => $"{s.Ma} (tồn {s.Ton:#,0.##}, khác {string.Join("/", s.Diff)})")) : "");
    }

    private static string Key(string? ma) => KiemKeCore.NormKey(ma);

    /// <summary>Gộp tồn theo mã (cộng các dòng cùng mã).</summary>
    public static List<Item> Aggregate(IEnumerable<KiemKeCore.Stock> stock) =>
        stock.GroupBy(s => s.Key).Select(g => new Item(g.First().Ma.ToUpperInvariant(), g.Sum(x => x.Ton), g.Select(x => x.Ten).FirstOrDefault(t => t.Length > 0) ?? "", g.Select(x => x.Dt).FirstOrDefault(t => t.Length > 0) ?? "")).ToList();

    /// <summary>Tổng kiện của cả cont theo từng mã hàng (mọi dòng, mọi mục, trừ mục “Khác”).</summary>
    public static Dictionary<string, double> MaSums(CargoCfg cfg, CargoRec rec, CargoCore.KsIndex? ks)
    {
        var sum = new Dictionary<string, double>();
        foreach (var b in rec.Blocks)
        {
            if (b.Loai == "khac") continue;
            foreach (var ln in b.Lines) { var r = CargoCore.ResolveLine(cfg, b, ln, ks); if (r.Ma.Length > 0 && r.Kien > 0) sum[Key(r.Ma)] = sum.GetValueOrDefault(Key(r.Ma)) + r.Kien; }
        }
        return sum;
    }

    private static string[] Parts(string ma) => new[] { Cut(ma, 0, 13), Cut(ma, 13, 4), Cut(ma, 17, 4), ma.Length > 21 ? ma[21..] : "" };
    private static string Cut(string s, int i, int n) => i >= s.Length ? "" : s.Substring(i, Math.Min(n, s.Length - i));
    private static readonly string[] DiffName = { "Loại hàng", "Size", "Net / quy cách", "Đặc tính (KQKS)" };

    /// <summary>Mã trong tồn chỉ khác đúng 1 phần (không có thì 2 phần) — gợi ý chỗ nhập sai.</summary>
    public static List<Similar> SimilarMa(string ma, IReadOnlyList<Item> items, int max = 3)
    {
        var a = Parts(ma); var o = new List<Similar>();
        for (var pass = 1; pass <= 2 && o.Count == 0; pass++)
            foreach (var it in items)
            {
                if (it.Ma.Length != ma.Length || !(it.Ton > 0)) continue;
                var b = Parts(it.Ma); var diff = Enumerable.Range(0, 4).Where(i => a[i] != b[i]).ToList();
                if (diff.Count == pass) o.Add(new Similar(it.Ma, it.Ton, it.Ten, it.Dt, diff.Select(i => DiffName[i]).ToList()));
            }
        return o.OrderByDescending(x => x.Ton).Take(max).ToList();
    }

    public static Result? Check(IReadOnlyList<Item> stock, IReadOnlyDictionary<string, double> sums, string ma)
    {
        var m = Key(ma); if (m.Length == 0) return null;
        var need = sums.GetValueOrDefault(m); var it = stock.FirstOrDefault(x => Key(x.Ma) == m);
        var sim = () => SimilarMa(ma.ToUpperInvariant(), stock);
        if (it is null) return new Result(ma, "none", 0, need, sim());
        if (!(it.Ton > 0)) return new Result(ma, "zero", it.Ton, need, sim());
        if (need > it.Ton + 1e-9) return new Result(ma, "over", it.Ton, need, new());
        return new Result(ma, "ok", it.Ton, need, new());
    }

    /// <summary>Kết quả dò cho mọi mã trong cont (đã có mã hàng đủ), sắp: lỗi trước.</summary>
    public static List<Result> CheckRec(CargoCfg cfg, CargoRec rec, CargoCore.KsIndex? ks, IReadOnlyList<Item> stock)
    {
        var sums = MaSums(cfg, rec, ks); var o = new List<Result>();
        foreach (var (k, need) in sums)
        {
            var ma = rec.Blocks.Where(b => b.Loai != "khac").SelectMany(b => b.Lines.Select(l => CargoCore.ResolveLine(cfg, b, l, ks).Ma)).First(x => Key(x) == k);
            if (Check(stock, sums, ma) is { } r) o.Add(r);
        }
        return o.OrderBy(r => r.St == "ok" ? 1 : 0).ThenBy(r => r.Ma, StringComparer.Ordinal).ToList();
    }
}

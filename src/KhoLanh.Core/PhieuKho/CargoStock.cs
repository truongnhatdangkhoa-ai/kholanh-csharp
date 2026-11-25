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

    // ------------------------------------------------------------------ 1.72: "Chọn từ tồn" — gộp tồn theo SIZE + ĐẶC TÍNH (+ quy cách), không tách lô

    /// <summary>
    /// 1 nhóm tồn: Size (nhãn như cột Size của Cargo) · Đặc tính (KQ kháng sinh — 4 số cuối mã 25 ký tự, hoặc cột Đặc tính) · Quy cách (mã gói 4 ký tự 18–21 → danh mục Size-quy cách).
    /// Loai / Sn suy được khi mã gói + tiền tố có trong danh mục; KsStt &gt; 0 khi đặc tính là số kết quả 1–83.
    /// </summary>
    public sealed record SizeRow(string Key, string Size, string Dt, string Pack, string Loai, string Sn, int KsStt, double Ton, double Have, double? Tl, int NMa, string MaMau)
    {
        public double Left => Math.Max(0, Math.Round(Ton - Have, 6));
    }

    private static string Norm(string? ma) => System.Text.RegularExpressions.Regex.Replace((ma ?? "").ToUpperInvariant(), @"\s+", "").Replace("TPC-", "");
    private static int Stt4(string s) => s.Length == 4 && int.TryParse(s, out var n) ? n : 0;

    /// <summary>Phần mã 25 ký tự: tiền tố 13 · size 4 · mã gói 4 · KQ 4 (mã 21 ký tự: không có KQ).</summary>
    public static (string Pre, string Size4, string Pack4, int Ks) Split(string? ma)
    {
        var m = Norm(ma);
        return (Cut(m, 0, 13), Cut(m, 13, 4), Cut(m, 17, 4), Stt4(Cut(m, 21, 4)));
    }

    /// <summary>Khóa gộp (size | quy cách | KQ hoặc chữ đặc tính) — dùng chung cho tồn và cho dòng đã có trong cont.</summary>
    public static string GroupKey(string size, string pack, int ks, string dt) =>
        (size.Trim() + "|" + pack.Trim() + "|" + (ks > 0 ? "#" + ks : dt.Trim())).ToUpperInvariant();

    /// <summary>Nhóm của 1 dòng tồn (null = không phải mã hàng dạng 21 / 25 ký tự).</summary>
    public static (string Size, string Pack, string Loai, string Sn, int Ks, string Dt)? Describe(CargoCfg cfg, string ma, string dtCol)
    {
        var m = Norm(ma); if (m.Length < 21) return null;
        var (pre, size4, pack4, ks) = Split(m);
        var qc = cfg.Qc.FirstOrDefault(q => q.Pack.Length > 0 && string.Equals(q.Pack, pack4, StringComparison.OrdinalIgnoreCase));
        var loai = qc?.Pre.FirstOrDefault(p => string.Equals(p.Value, pre, StringComparison.OrdinalIgnoreCase)).Key ?? "";
        if (loai.Length == 0) loai = pre.StartsWith("VRHO") ? (cfg.Loai.Any(l => l.Code == "NL") ? "NL" : "") : pre.StartsWith("VRHL") ? (cfg.Loai.Any(l => l.Code == "BTP") ? "BTP" : "") : "";
        var sl = CargoCore.SizeLabel(m);
        var size = sl.Length > 0 ? sl : loai.Length > 0 && long.TryParse(size4, out var n) ? loai + n : size4;
        var sn = qc is not null && loai.Length > 0 && qc.Pre.ContainsKey(loai) ? size4 + "-" + qc.D : "";
        var dt = dtCol.Trim().Length > 0 ? dtCol.Trim() : ks > 0 ? CargoCore.KsLabel(CargoCore.KsRule(ks)) is { Length: > 0 } k ? k : ks.ToString() : "";
        return (size, qc?.Label ?? "", loai, sn, ks, dt);
    }

    /// <summary>Kiện đã có trong cont theo khóa gộp (mọi mục hàng, cả mục "khác" — khớp theo Size + Quy cách gõ tay).</summary>
    public static Dictionary<string, double> HaveByGroup(CargoCfg cfg, CargoRec rec, CargoCore.KsIndex? ks)
    {
        var o = new Dictionary<string, double>();
        foreach (var b in rec.Blocks)
            foreach (var ln in b.Lines)
            {
                var r = CargoCore.ResolveLine(cfg, b, ln, ks); if (r.Kien <= 0) continue;
                var k = r.Other ? GroupKey(r.Size, r.Pack, 0, "") : GroupKey(r.Size, r.Pack, r.Ks?.Stt ?? 0, "");
                o[k] = o.GetValueOrDefault(k) + r.Kien;
            }
        return o;
    }

    /// <summary>
    /// Tồn Tồn kho An An gộp theo Size + Đặc tính + Quy cách (KHÔNG tách lô / hợp đồng), kèm số kiện đã có trong cont đang soạn.
    /// Chỉ các nhóm còn tồn &gt; 0. Sắp theo Size rồi Quy cách, Đặc tính.
    /// </summary>
    public static List<SizeRow> BySize(IEnumerable<KiemKeCore.Stock> stock, CargoCfg cfg, CargoRec? rec, CargoCore.KsIndex? ks)
    {
        var have = rec is null ? new() : HaveByGroup(cfg, rec, ks);
        var g = new Dictionary<string, (string Size, string Pack, string Loai, string Sn, int Ks, string Dt, double Ton, double? Tl, HashSet<string> Ma, string Mau)>();
        foreach (var s in stock)
        {
            if (!(s.Ton > 0) || Describe(cfg, s.Ma, s.Dt) is not { } d) continue;
            var k = GroupKey(d.Size, d.Pack, d.Ks, d.Dt);
            if (!g.TryGetValue(k, out var e)) e = (d.Size, d.Pack, d.Loai, d.Sn, d.Ks, d.Dt, 0, s.Tl, new HashSet<string>(), Norm(s.Ma));
            e.Ton += s.Ton; e.Ma.Add(Norm(s.Ma)); e.Tl ??= s.Tl;
            g[k] = e;
        }
        return g.Select(kv =>
        {
            var e = kv.Value;
            // dòng trong cont chưa ra KQ kháng sinh (chưa gõ lô) được đếm vào nhóm KQ = 0 → cộng vào nhóm cùng size + quy cách khi nhóm KQ đó có
            var h = have.GetValueOrDefault(kv.Key);
            return new SizeRow(kv.Key, e.Size, e.Dt, e.Pack, e.Loai, e.Sn, e.Ks, Math.Round(e.Ton, 6), Math.Round(h, 6), e.Tl, e.Ma.Count, e.Mau);
        }).OrderBy(r => SizeOrder(r.Size)).ThenBy(r => r.Size, StringComparer.Ordinal).ThenBy(r => r.Pack, StringComparer.Ordinal).ThenBy(r => r.Dt, StringComparer.Ordinal).ToList();
    }
    private static (int, double) SizeOrder(string size)
    {
        var m = System.Text.RegularExpressions.Regex.Match(size, @"^([A-Z]*)\s*(\d+)");
        return m.Success ? (m.Groups[1].Value switch { "BTP" => 0, "" => 1, "NL" => 2, _ => 3 }, double.Parse(m.Groups[2].Value)) : (9, 0);
    }

    /// <summary>Dòng Cargo mới từ 1 nhóm tồn. Mục kháng sinh: Loại + Size-quy cách (+ KQ KS tay = đặc tính của nhóm), Lô để trống;
    /// mục "khác": Size + Quy cách (không có trong danh mục → "x kg x 1" theo trọng lượng). Ok = false khi mục kháng sinh chưa suy được Size-quy cách.</summary>
    public static (CargoLine Line, bool Ok) LineFor(SizeRow r, bool ksBlock)
    {
        if (!ksBlock)
        {
            var pack = r.Pack.Length > 0 ? r.Pack : r.Tl is > 0 ? r.Tl.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " kg x 1" : "";
            return (new CargoLine { Size = r.Size, Pack = pack }, true);
        }
        return (new CargoLine { Loai = r.Loai, Sn = r.Sn, KsTay = r.KsStt > 0 ? r.KsStt.ToString() : "" }, r.Sn.Length > 0);
    }
}

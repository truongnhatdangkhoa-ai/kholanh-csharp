using System.Text.Json.Nodes;
using KhoLanh.Core.KiemKe;
using static KhoLanh.Core.PhieuKho.CargoCore;

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

    // ------------------------------------------------------------------ 1.88: cảnh báo từng dòng (chỉ cảnh báo, không chặn lưu / xuất)

    /// <summary>Cảnh báo của 1 dòng mục hàng kháng sinh. Bad = Size-quy cách sai (đỏ), còn lại = tồn kho (vàng).</summary>
    public sealed record LineWarn(int Block, int Line, bool Bad, string Text);

    private static string K21(string ma) { var k = Key(ma); return k.Length >= 21 ? k[..21] : ""; }

    /// <summary>
    /// Dò từng dòng đã gõ Size-quy cách (mục kháng sinh) với Tồn kho An An:
    /// Size-quy cách sai / chưa ghép được mã (dòng chưa có kiện — dòng có kiện đã vào "Cần xử lý trước khi xuất") ·
    /// mã (size + quy cách) không có trong tồn · có mã nhưng tồn = 0 · size còn hàng nhưng đặc tính (KQ kháng sinh) đó không còn ·
    /// vượt tồn (cộng mọi dòng cùng mã của cả cont). Không có tồn (chưa nạp) → chỉ cảnh báo Size-quy cách.
    /// </summary>
    public static List<LineWarn> LineWarnings(CargoCfg cfg, CargoRec rec, CargoCore.KsIndex? ks, IReadOnlyList<Item> stock)
    {
        var o = new List<LineWarn>();
        var res = new List<(int Bi, int Li, CargoCore.Resolved R)>();
        for (var bi = 0; bi < rec.Blocks.Count; bi++)
        {
            var b = rec.Blocks[bi]; if (b.Loai == "khac") continue;
            for (var li = 0; li < b.Lines.Count; li++)
            {
                var ln = b.Lines[li];
                if (ln.Sn.Trim().Length == 0 && CargoCore.LineTotal(ln) <= 0) continue;
                res.Add((bi, li, CargoCore.ResolveLine(cfg, b, ln, ks)));
            }
        }
        foreach (var (bi, li, r) in res)
        {
            if (r.Kien > 0) continue;   // dòng có kiện: lỗi đã nằm trong Problems của CheckRec
            if (r.SizeErrs > 0) o.Add(new LineWarn(bi, li, true, string.Join(" ", r.Errors.Take(r.SizeErrs))));
            else if (r.Sn is not null && r.Ma.Length == 0) o.Add(new LineWarn(bi, li, true, $"Chưa ghép được mã hàng (loại {r.Loai}, quy cách {r.Sn.Digit}) — Size-quy cách / Loại không có trong danh mục."));
        }
        if (stock.Count == 0) return o;

        var by21 = new Dictionary<string, List<Item>>();
        foreach (var it in stock) { var k = K21(it.Ma); if (k.Length == 0) continue; if (!by21.TryGetValue(k, out var l)) by21[k] = l = new(); l.Add(it); }
        var needMa = new Dictionary<string, double>(); var need21 = new Dictionary<string, double>();
        foreach (var (_, _, r) in res)
        {
            if (r.Kien <= 0 || r.Ma.Length < 21) continue;
            need21[K21(r.Ma)] = need21.GetValueOrDefault(K21(r.Ma)) + r.Kien;
            if (r.Ma.Length > 21) needMa[Key(r.Ma)] = needMa.GetValueOrDefault(Key(r.Ma)) + r.Kien;
        }
        string N(double v) => v.ToString("#,0.##", System.Globalization.CultureInfo.CurrentCulture);
        string DtOf(Item x)
        {
            if (x.Dt.Trim().Length > 0) return x.Dt.Trim();
            var st = Split(x.Ma).Ks; return st > 0 ? (CargoCore.KsLabel(CargoCore.KsRule(st)) is { Length: > 0 } l ? l : "KQ " + st) : "(không đặc tính)";
        }
        string Lbl(string ma21)
        {
            var (_, _, pack4, _) = Split(ma21);
            var qc = cfg.Qc.FirstOrDefault(q => q.Pack.Length > 0 && string.Equals(q.Pack, pack4, StringComparison.OrdinalIgnoreCase));
            return (CargoCore.SizeLabel(ma21) is { Length: > 0 } sl ? sl : ma21.Substring(13, 4)) + (qc is not null && qc.Label.Length > 0 ? " " + qc.Label : " gói " + pack4);
        }
        string Near(string k21)
        {
            // còn hàng, chỉ khác 1 phần (loại hàng / size / quy cách) → gợi ý chỗ gõ sai
            var a = new[] { k21[..13], k21.Substring(13, 4), k21.Substring(17, 4) };
            var sim = by21.Where(kv => kv.Value.Sum(x => Math.Max(0, x.Ton)) > 0)
                .Select(kv => (kv.Key, Ton: kv.Value.Sum(x => Math.Max(0, x.Ton)), D: new[] { kv.Key[..13], kv.Key.Substring(13, 4), kv.Key.Substring(17, 4) }.Where((p, i) => p != a[i]).Count()))
                .Where(x => x.D == 1).OrderByDescending(x => x.Ton).Take(3).ToList();
            return sim.Count == 0 ? "" : " — tồn còn: " + string.Join("; ", sim.Select(x => $"{Lbl(x.Key)} ({N(x.Ton)} kiện)"));
        }
        foreach (var (bi, li, r) in res)
        {
            if (r.SizeErrs > 0 || r.Ma.Length < 21) continue;
            var k21 = K21(r.Ma); var lbl = r.Size + (r.Pack.Length > 0 ? " " + r.Pack : "");
            if (!by21.TryGetValue(k21, out var grp)) { o.Add(new LineWarn(bi, li, false, $"{lbl}: mã {k21} không có trong Tồn kho An An" + Near(k21) + ".")); continue; }
            var ton21 = grp.Sum(x => Math.Max(0, x.Ton));
            if (ton21 <= 0) { o.Add(new LineWarn(bi, li, false, $"{lbl}: Tồn kho An An đã hết hàng (tồn 0)" + Near(k21) + ".")); continue; }
            var kqStock = grp.Where(x => Key(x.Ma).Length > 21).ToList();
            if (r.Ks is not null && r.Ma.Length > 21 && kqStock.Count > 0)
            {
                var full = Key(r.Ma); var it = kqStock.FirstOrDefault(x => Key(x.Ma) == full);
                if (it is null || !(it.Ton > 0))
                {
                    var con = kqStock.Where(x => x.Ton > 0).OrderByDescending(x => x.Ton).Take(4).Select(x => $"{DtOf(x)} ({N(x.Ton)})");
                    o.Add(new LineWarn(bi, li, false, $"{lbl}: đặc tính {r.KsText} không còn hàng — tồn còn: {string.Join("; ", con)}."));
                    continue;
                }
                var need = needMa.GetValueOrDefault(full);
                if (need > it.Ton + 1e-9) o.Add(new LineWarn(bi, li, false, $"{lbl} · {r.KsText}: vượt tồn — cả cont cần {N(need)} kiện, tồn An An còn {N(it.Ton)}."));
                continue;
            }
            var n21 = need21.GetValueOrDefault(k21);
            if (n21 > ton21 + 1e-9) o.Add(new LineWarn(bi, li, false, $"{lbl}: vượt tồn — cả cont cần {N(n21)} kiện, tồn An An còn {N(ton21)}."));
        }
        return o.OrderBy(w => w.Block).ThenBy(w => w.Line).ToList();
    }

    // ------------------------------------------------------------------ 1.72: "Chọn từ tồn" — gộp tồn theo SIZE + ĐẶC TÍNH (+ quy cách), không tách lô

    /// <summary>
    /// 1 nhóm tồn: Size (nhãn như cột Size của Cargo) · Đặc tính (KQ kháng sinh — 4 số cuối mã 25 ký tự, hoặc cột Đặc tính) · Quy cách (mã gói 4 ký tự 18–21 → danh mục Size-quy cách).
    /// Loai / Sn suy được khi mã gói + tiền tố có trong danh mục; KsStt &gt; 0 khi đặc tính là số kết quả 1–83.
    /// </summary>
    public sealed record SizeRow(string Key, string Size, string Dt, string Pack, string Loai, string Sn, int KsStt, double Ton, double Have, double? Tl, int NMa, string MaMau, string Nhom = "")
    {
        /// <summary>1.128: tên phân loại Tồn kho An An của nhóm ("Chưa phân loại" khi trống).</summary>
        public string NhomLabel => PhanLoaiM1.LabelOf(Nhom);
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
    /// Tồn Tồn kho An An gộp theo Size + Đặc tính + Quy cách (KHÔNG tách lô / hợp đồng) + phân loại An An (1.128), kèm số kiện đã có trong cont đang soạn.
    /// Chỉ các nhóm còn tồn &gt; 0. Sắp theo Size rồi Quy cách, Đặc tính.
    /// Kiện đã có trong cont: tính theo Size + Quy cách + KQ; cùng nhóm có ở nhiều phân loại thì ghi vào phân loại của Loại hàng dòng đó (không có thì phân loại đầu).
    /// </summary>
    public static List<SizeRow> BySize(IEnumerable<KiemKeCore.Stock> stock, CargoCfg cfg, CargoRec? rec, CargoCore.KsIndex? ks)
    {
        var g = new Dictionary<string, (string Base, string Size, string Pack, string Loai, string Sn, int Ks, string Dt, double Ton, double? Tl, HashSet<string> Ma, string Mau, string Nhom)>();
        var order = new List<string>();
        foreach (var s in stock)
        {
            if (!(s.Ton > 0) || Describe(cfg, s.Ma, s.Dt) is not { } d) continue;
            // 1.128: hàng thuộc phân loại của 1 Loại khác mà Loại đó dùng CÙNG tiền tố cho quy cách này → lấy Loại theo phân loại (vd TGC cùng tiền tố BTP)
            if (s.Nhom.Length > 0 && cfg.Loai.FirstOrDefault(l => NhomOf(cfg, l.Code) == s.Nhom)?.Code is { } ln && ln != d.Loai)
            {
                var (pre, _, pack4, _) = Split(s.Ma);
                if (cfg.Qc.FirstOrDefault(q => q.Pack.Length > 0 && string.Equals(q.Pack, pack4, StringComparison.OrdinalIgnoreCase)) is { } qc && string.Equals(qc.Pre.GetValueOrDefault(ln), pre, StringComparison.OrdinalIgnoreCase))
                    d = d with { Loai = ln };
            }
            var bk = GroupKey(d.Size, d.Pack, d.Ks, d.Dt); var k = bk + "|" + s.Nhom;
            if (!g.TryGetValue(k, out var e)) { e = (bk, d.Size, d.Pack, d.Loai, d.Sn, d.Ks, d.Dt, 0, s.Tl, new HashSet<string>(), Norm(s.Ma), s.Nhom); order.Add(k); }
            e.Ton += s.Ton; e.Ma.Add(Norm(s.Ma)); e.Tl ??= s.Tl;
            g[k] = e;
        }
        var have = new Dictionary<string, double>();
        if (rec is not null)
            foreach (var b in rec.Blocks)
                foreach (var ln in b.Lines)
                {
                    var r = CargoCore.ResolveLine(cfg, b, ln, ks); if (r.Kien <= 0) continue;
                    var bk = r.Other ? GroupKey(r.Size, r.Pack, 0, "") : GroupKey(r.Size, r.Pack, r.Ks?.Stt ?? 0, "");
                    var cands = order.Where(k => g[k].Base == bk).ToList(); if (cands.Count == 0) continue;
                    var nh = r.Other ? "" : NhomOf(cfg, r.Loai);
                    var tk = cands.FirstOrDefault(k => g[k].Nhom == nh) ?? cands[0];
                    have[tk] = have.GetValueOrDefault(tk) + r.Kien;
                }
        return order.Select(k =>
        {
            var e = g[k]; var h = have.GetValueOrDefault(k);
            return new SizeRow(k, e.Size, e.Dt, e.Pack, e.Loai, e.Sn, e.Ks, Math.Round(e.Ton, 6), Math.Round(h, 6), e.Tl, e.Ma.Count, e.Mau, e.Nhom);
        }).OrderBy(r => SizeOrder(r.Size)).ThenBy(r => r.Size, StringComparer.Ordinal).ThenBy(r => r.Pack, StringComparer.Ordinal).ThenBy(r => r.Dt, StringComparer.Ordinal)
          .ThenBy(r => Array.IndexOf(PhanLoaiM1.Order, r.Nhom)).ToList();
    }

    /// <summary>1.128: 1 gợi ý ô Size-quy cách — gộp các nhóm tồn cùng Loại + Size-quy cách (mọi đặc tính) trong phân loại của Loại.</summary>
    public sealed record SnHint(string Sn, string Loai, string Size, string Pack, double Left, List<string> Dts, string NhomLabel);

    /// <summary>
    /// 1.128: gợi ý khi gõ ô Size-quy cách của phiếu nội địa: chỉ hàng còn tồn (sau khi trừ cont đang soạn) thuộc phân loại An An của <paramref name="loai"/>
    /// (Loại trống → mọi Loại có gắn phân loại). Chữ gõ: "40" (size) · "0040-6" / "40-6" · "BTP40" · "NL130" — khớp đầu size / Size-quy cách / nhãn size.
    /// </summary>
    public static List<SnHint> SuggestSn(IEnumerable<SizeRow> rows, CargoCfg cfg, string? loai, string? text, int max = 12)
    {
        var lo = (loai ?? "").Trim().ToUpperInvariant();
        var t = System.Text.RegularExpressions.Regex.Replace((text ?? "").Trim().ToUpperInvariant(), @"\s+", "");
        // gõ loại ngay đầu ô ("NL130") → dùng loại đó
        var lm = System.Text.RegularExpressions.Regex.Match(t, "^(" + string.Join("|", cfg.Loai.Select(l => l.Code).OrderByDescending(c => c.Length).Select(System.Text.RegularExpressions.Regex.Escape)) + ")(?=\\d|$)");
        if (cfg.Loai.Count > 0 && lm.Success) { lo = lm.Groups[1].Value; t = t[lm.Length..]; }
        var nm = System.Text.RegularExpressions.Regex.Match(t, @"^0*(\d{1,4})(?:[-–./](\d?))?$");
        if (t.Length > 0 && !nm.Success) return new();
        var num = nm.Success ? nm.Groups[1].Value : ""; var dig = nm.Success ? nm.Groups[2].Value : ""; var hasDash = nm.Success && t.IndexOfAny(new[] { '-', '–', '.', '/' }) >= 0;
        bool Hit(SizeRow r)
        {
            if (r.Sn.Length == 0 || !(r.Left > 0)) return false;
            // Loại có phân loại → chỉ hàng thuộc phân loại đó (Loại của gợi ý = Loại ra đúng mã tồn); Loại không gắn phân loại → theo Loại; Loại trống → mọi hàng
            var nh = lo.Length > 0 ? NhomOf(cfg, lo) : "";
            if (nh.Length > 0 ? r.Nhom != nh : lo.Length > 0 && r.Loai != lo) return false;
            if (num.Length == 0) return true;
            var size = r.Sn.Split('-')[0].TrimStart('0'); var d = r.Sn.Split('-').Last();
            if (hasDash) return size == num && (dig.Length == 0 || d == dig);
            return size.StartsWith(num, StringComparison.Ordinal);
        }
        return rows.Where(Hit).GroupBy(r => r.Loai + "|" + r.Sn).Select(gr =>
        {
            var f = gr.First();
            return new SnHint(f.Sn, f.Loai, f.Size, f.Pack, Math.Round(gr.Sum(x => x.Left), 6), gr.Select(x => x.Dt).Where(x => x.Length > 0).Distinct().ToList(), f.NhomLabel);
        }).OrderBy(h => SizeOrder(h.Size)).ThenBy(h => h.Sn, StringComparer.Ordinal).Take(max).ToList();
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

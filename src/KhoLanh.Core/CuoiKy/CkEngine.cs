using System.Text.RegularExpressions;
using static KhoLanh.Core.CuoiKy.Ck;

namespace KhoLanh.Core.CuoiKy;

public sealed class CkCause
{
    public string Code = "", Label = "", Basis = "", Conf = "", ConfLabel = "", Action = "";
    public List<string> Evidence = new();
    public List<string> RefCodes = new(), RefVouchers = new();
}

public sealed record CkDecomp((double Kho, double Kt, double D) Dau, (double Kho, double Kt, double D) Nhap, (double Kho, double Kt, double D) Xuat, double Resid);

/// <summary>Tồn kho gửi gộp theo mã 21 ký tự.</summary>
public sealed class CkGuiAgg
{
    public string Ma = "", Ten = "";
    public double Kg, Kien, DauKg, NhapKg, XuatKg, SelfLech;
    public bool HasMove;
    public int NDoi;
    public List<CkRow> Lots = new();
}

public sealed class CkItem
{
    public string Ma = "", Ten = "";
    public double? Aa, Gui, Kt, Kk;
    public double Kho, Diff;
    public bool PAa, PGui, PKt, PKtKnown, PKk;
    public CkRow? AaRow, KtRow, KkRow;
    public CkGuiAgg? GuiRow;
    public double? AaSL, AaW, GuiKien;
    public (double? Aa, double? Gui, double? Kt) Dau, Nhap, Xuat;
    public double? TinhAa, TinhGui, TinhKt;
    public bool Active;
    public string Status = "";
    public CkItemCt Ct = new();
    public List<CkCand> Cands = new();
    public List<CkCombo> Combos = new();
    public string Group = "";
    public CkDecomp? Decomp;
    public List<CkCause> Causes = new();
    public CkCause? Cause => Causes.Count > 0 ? Causes[0] : null;
    public bool RootKnown = true;
    public (double Nhap, double Xuat)? CtLech;
    // sau xử lý (overlay)
    public CkDecision? Decision;
    public double Adj, AdjKho, AdjKt, KhoAfter, KtAfter, DiffAfter;
    public string StatusAfter = "", StatusUi = "";
    public bool Stale, AutoDone;
    public bool TagThieu, TagThua, TagQuyCach, TagSaiMa, TagCt, TagXong;
}

public sealed class CkKpi
{
    public int Tong, Khop, Lech, ChuaXacDinh, DaXuLy, TienDo, Thieu, Thua, QuyCach, SaiMa, ChungTu, BoQuaBot, BoQuaZero;
    public double TongAA, TongGui, TongKho, TongKt, LechRong, LechTuyetDoi, LechRongSau, LechTuyetDoiSau;
    public double? TongKk;
    public bool Bubu;
}

public sealed record CkCheck(string Level, string Text);

public sealed class CkInput
{
    public CkSource? AnAn, KeToan, Gui, KiemKe, AnAnNhap, AnAnXuat;
    public List<CkVoucher> Ct = new();
    public List<string>? DanhMuc;
    public CkPeriod? Period;
    public CkOptions Options = new();
}

public sealed class CkResult
{
    public List<CkItem> Items = new();
    public Dictionary<string, CkItem> ByMa = new();
    public Dictionary<string, List<string>> Groups = new();
    public CkKpi Kpi = new();
    public List<CkOffset> Offsets = new();
    public List<CkCheck> Validation = new();
    public CkPeriod? Period;
    public CkOptions Options = new();
    public CkTrace.Result Trace = new();
    public bool HasCtKt;
    public int DanhMucSize;
    internal int HiddenBot, HiddenZero;

    /// <summary>Áp quyết định + điều chỉnh của người dùng (tính lại tức thì, không đối chiếu lại từ đầu).</summary>
    public CkKpi Overlay(CkState? st) { Kpi = CkEngine.Overlay(this, st); return Kpi; }
}

/// <summary>BỘ MÁY ĐỐI CHIẾU CUỐI KỲ (giống dc2-engine.js).</summary>
public static class CkEngine
{
    private static readonly Dictionary<string, int> Conf = new() { ["cao"] = 3, ["trung binh"] = 2, ["thap"] = 1 };
    public static readonly Dictionary<string, string> ConfLabel = new() { ["cao"] = "Cao", ["trung binh"] = "Trung bình", ["thap"] = "Thấp" };
    private static readonly string[] Pairish = { "nhan-ma", "quy-cach", "chia-ma" };

    public static (Dictionary<string, CkGuiAgg> Map, List<string> Order) FoldGui(IEnumerable<CkRow>? rows)
    {
        var m = new Dictionary<string, CkGuiAgg>(); var order = new List<string>();
        foreach (var r in rows ?? Array.Empty<CkRow>())
        {
            if (!m.TryGetValue(r.Ma, out var g)) { g = new CkGuiAgg { Ma = r.Ma, Ten = r.Ten }; m[r.Ma] = g; order.Add(r.Ma); }
            g.Kg = R6(g.Kg + r.Kg); g.Kien += r.CuoiKien ?? 0; g.Lots.Add(r);
            if (r.DauKg is { } d) { g.DauKg = R6(g.DauKg + d); g.HasMove = true; }
            if (r.NhapKg is { } n) g.NhapKg = R6(g.NhapKg + n);
            if (r.XuatKg is { } x) g.XuatKg = R6(g.XuatKg + x);
            if (r.SelfLech is { } s && s != 0) g.SelfLech = R6(g.SelfLech + s);
            if (r.Via == "khong") g.NDoi++;
        }
        return (m, order);
    }

    private static CkItem BuildItem(string ma, Dictionary<string, CkRow> aa, Dictionary<string, CkRow> kt, Dictionary<string, CkGuiAgg> gui, Dictionary<string, CkRow> kk, HashSet<string> ktKnown)
    {
        aa.TryGetValue(ma, out var a); kt.TryGetValue(ma, out var k); gui.TryGetValue(ma, out var g); kk.TryGetValue(ma, out var q);
        double? kgA = a?.Kg, kgG = g?.Kg, kgK = k?.Kg, kgQ = q?.Kg;
        var kho = R6((kgA ?? 0) + (kgG ?? 0)); var diff = R6(kho - (kgK ?? 0));
        string Pick(params string?[] xs) => xs.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";
        var it = new CkItem
        {
            Ma = ma, Ten = Pick(a?.Ten, k?.Ten, g?.Ten, q?.Ten), Aa = kgA, Gui = kgG, Kt = kgK, Kk = kgQ, Kho = kho, Diff = diff,
            PAa = a is not null, PGui = g is not null, PKt = k is not null, PKtKnown = ktKnown.Contains(ma), PKk = q is not null,
            AaRow = a, KtRow = k, GuiRow = g, KkRow = q, AaSL = a?.Sl, AaW = a?.W, GuiKien = g?.Kien,
            Dau = (a?.DauKg, g is { HasMove: true } ? g.DauKg : null, k?.DauKg),
            Nhap = (a?.NhapKg, g is { HasMove: true } ? g.NhapKg : null, k?.NhapKg),
            Xuat = (a?.XuatKg, g is { HasMove: true } ? g.XuatKg : null, k?.XuatKg),
        };
        double? T(double? d, double? n, double? x) => d is { } dd && n is { } nn && x is { } xx ? R6(dd + nn - xx) : null;
        it.TinhAa = T(it.Dau.Aa, it.Nhap.Aa, it.Xuat.Aa); it.TinhGui = T(it.Dau.Gui, it.Nhap.Gui, it.Xuat.Gui); it.TinhKt = T(it.Dau.Kt, it.Nhap.Kt, it.Xuat.Kt);
        return it;
    }

    private static CkDecomp? Decompose(CkItem it, CkInput inp)
    {
        if (inp.AnAn is null || inp.KeToan is null) return null;
        double? Side(Func<CkItem, (double? Aa, double? Gui, double? Kt)> f)
        {
            double sum = 0;
            foreach (var (loaded, has, v) in new[] { (inp.AnAn is not null, it.PAa, f(it).Aa), (inp.Gui is not null, it.PGui, f(it).Gui) })
            {
                if (!loaded || !has) continue;
                if (v is not { } x) return null; sum += x;
            }
            return R6(sum);
        }
        double? Kt(Func<CkItem, (double? Aa, double? Gui, double? Kt)> f) => it.PKt ? f(it).Kt : 0;
        var kd = Side(x => x.Dau); var kn = Side(x => x.Nhap); var kx = Side(x => x.Xuat);
        var td = Kt(x => x.Dau); var tn = Kt(x => x.Nhap); var tx = Kt(x => x.Xuat);
        if (kd is null || kn is null || kx is null || td is null || tn is null || tx is null) return null;
        var dau = (kd.Value, td.Value, R6(kd.Value - td.Value)); var nhap = (kn.Value, tn.Value, R6(kn.Value - tn.Value)); var xuat = (kx.Value, tx.Value, R6(kx.Value - tx.Value));
        return new CkDecomp(dau, nhap, xuat, R6(it.Diff - (dau.Item3 + nhap.Item3 - xuat.Item3)));
    }

    private static CkCause Mk(string code, string basis, string conf, IEnumerable<string> evidence, string action, List<string>? codes = null, List<string>? vouchers = null) => new()
    {
        Code = code, Label = CkConfig.Cause[code], Basis = basis, Conf = conf, ConfLabel = ConfLabel.GetValueOrDefault(conf, ""), Evidence = evidence.ToList(), Action = action, RefCodes = codes ?? new(), RefVouchers = vouchers ?? new(),
    };

    public static double RankOf(CkCause c) => (c.Basis == "xac-dinh" ? 10 : 0) + Conf.GetValueOrDefault(c.Conf) * 3 + (Pairish.Contains(c.Code) && c.Conf == "cao" ? 12 : 0) + (30 - CkConfig.CauseOrder.IndexOf(c.Code)) / 100.0;

    private sealed record Ctx(CkOptions Opts, HashSet<string>? DanhMuc, int DanhMucSize, bool HasKk, bool HasCtKt, bool GuiPeriodBad, CkPeriod? GuiPeriod, CkPeriod? Period);

    private static List<CkCause> CausesFor(CkItem it, Ctx X)
    {
        var tol = X.Opts.Tol; var pt = X.Opts.PairTol; var outp = new List<CkCause>();
        var d = it.Diff; var abs = Math.Abs(d);
        var side = d > 0 ? "Tồn kho (An An + gửi) CAO hơn kế toán " : "Tồn kho (An An + gửi) THẤP hơn kế toán ";
        // 1. Mã chỉ có ở một nguồn
        var khoHas = it.PAa || it.PGui;
        if (khoHas && !it.PKt && Math.Abs(it.Kho) > tol)
        {
            if (!it.PKtKnown) outp.Add(Mk("chi-co-1-ben", "xac-dinh", "cao", new[] { $"Mã có tồn {Vn(it.Kho)} kg ở kho nhưng KHÔNG có trong báo cáo NXT kế toán." }, "Kiểm tra kế toán đã tạo / ghi nhận mã này chưa, hoặc mã đang bị ghi sang một mã khác (xem gợi ý mã)."));
            else outp.Add(Mk("chi-co-1-ben", "xac-dinh", "cao", new[] { $"Kế toán có mã này nhưng tồn cuối = 0 / không phát sinh; kho còn {Vn(it.Kho)} kg." }, "Kiểm tra phiếu nhập chưa vào kế toán, hoặc mã bị ghi sang mã khác."));
        }
        else if (!khoHas && it.PKt && Math.Abs(it.Kt ?? 0) > tol)
            outp.Add(Mk("chi-co-1-ben", "xac-dinh", "cao", new[] { $"Kế toán còn tồn {Vn(it.Kt ?? 0)} kg nhưng mã này KHÔNG có ở Tồn kho An An / Tồn kho gửi." }, "Kiểm tra phiếu xuất chưa vào kế toán, hoặc mã bị ghi sang mã khác (xem gợi ý mã)."));
        // 2. Mã không hợp lệ
        if (X.DanhMuc is not null && it.Ma.Length == 21 && !X.DanhMuc.Contains(it.Ma))
            outp.Add(Mk("ngoai-danh-muc", "xac-dinh", "cao", new[] { $"Mã không có trong danh mục thành phẩm của kế toán ({Vn(X.DanhMucSize)} mã)." }, "Mã này có thể gõ sai — đối chiếu với danh mục / mã gợi ý."));
        // 3. Gợi ý mã
        if (it.Cands.Count > 0)
        {
            var top = it.Cands[0];
            var ev = new List<string> { $"Mã gợi ý {top.Ma} đang lệch ngược {Sg(top.Diff)} kg." }; ev.AddRange(top.Reasons.Take(3));
            if (X.DanhMuc is not null && it.Ma.Length == 21 && top.Ma.Length == 21)
            {
                bool mine = X.DanhMuc.Contains(it.Ma), theirs = X.DanhMuc.Contains(top.Ma);
                if (mine && !theirs) ev.Add("Mã gợi ý KHÔNG có trong danh mục thành phẩm, mã này có → nhiều khả năng mã gợi ý mới là mã nhập sai.");
                else if (!mine && theirs) ev.Add("Mã này KHÔNG có trong danh mục thành phẩm, mã gợi ý có → nhiều khả năng mã này là mã nhập sai.");
            }
            outp.Add(Mk(top.Kind == "quy-cach" ? "quy-cach" : "nhan-ma", "nghi-ngo", top.Conf, ev, $"Hai mã có thể bị ghi nhầm cho nhau {Vn(Math.Min(abs, Math.Abs(top.Diff)))} kg — đối chiếu phiếu của cả hai mã; nếu đúng, dùng \"Gắn mã hàng liên quan\" để ghi nhận chuyển nhầm.", new() { top.Ma }));
        }
        if (it.Combos.Count > 0) { var g = it.Combos[0]; outp.Add(Mk("chia-ma", "nghi-ngo", g.Conf, g.Reasons, $"Nghi {Vn(abs)} kg của mã này bị ghi chia sang {g.Codes.Count} mã gần giống — kiểm tra phiếu của các mã: {string.Join(", ", g.Codes)}.", g.Codes.ToList())); }
        // 3b. Phân rã
        if (it.Decomp is { } D)
        {
            double dd = D.Dau.D, dn = D.Nhap.D, dx = D.Xuat.D;
            if (Math.Abs(dd) > tol || Math.Abs(dn) > tol || Math.Abs(dx) > tol)
            {
                var line = $"Phân rã: chênh lệch cuối kỳ {Sg(d)} kg = tồn đầu lệch {Sg(dd)} + nhập lệch {Sg(dn)} − xuất lệch {Sg(dx)}" + (Math.Abs(D.Resid) > tol ? $" (còn {Sg(D.Resid)} kg chưa giải thích — một nguồn không tự khớp)" : "") + ".";
                if (Math.Abs(dd - d) <= tol && Math.Abs(dn) <= tol && Math.Abs(dx) <= tol) outp.Add(Mk("lech-dau-ky", "xac-dinh", "cao", new[] { $"Tồn đầu kỳ đã lệch sẵn: kho {Vn(D.Dau.Kho)} kg, kế toán {Vn(D.Dau.Kt)} kg; nhập và xuất trong kỳ của hai bên khớp nhau → toàn bộ chênh lệch cuối kỳ có từ TRƯỚC kỳ này.", line }, "Truy lại số cuối kỳ trước của mã này — kho và kế toán đã chốt khác nhau."));
                else if (Math.Abs(dd) <= tol) outp.Add(Mk("lech-phat-sinh", "xac-dinh", Math.Abs(D.Resid) <= tol ? "cao" : "trung binh", new[] { "Tồn đầu kỳ hai bên khớp; chênh lệch phát sinh TRONG kỳ.", line }, "Rà phiếu nhập / xuất của mã trong kỳ giữa kho và kế toán."));
                else outp.Add(Mk("lech-dau-ky", "nghi-ngo", Math.Abs(D.Resid) <= tol ? "trung binh" : "thap", new[] { $"Tồn đầu kỳ lệch {Sg(dd)} kg (kho {Vn(D.Dau.Kho)}, kế toán {Vn(D.Dau.Kt)}) và phát sinh trong kỳ cũng lệch.", line, "Lưu ý: số đầu kỳ / nhập / xuất của kho và kế toán có thể ghi nhận khác nhau (chuyển đổi nội bộ) — chỉ Tồn cuối kỳ là số so sánh chính." }, "Rà tồn đầu kỳ trước, sau đó rà phát sinh trong kỳ."));
            }
        }
        // 4. Chứng từ kế toán ↔ NXT kế toán
        var ct = it.Ct;
        if (X.HasCtKt && it.KtRow is { } kr)
        {
            var lechN = R6((kr.NhapKg ?? 0) - ct.Ketoan.Nhap); var lechX = R6((kr.XuatKg ?? 0) - ct.Ketoan.Xuat);
            it.CtLech = (lechN, lechX);
            if (Math.Abs(lechN) > tol || Math.Abs(lechX) > tol)
            {
                var diffAdj = R6(d + R6(lechN - lechX)); var ev = new List<string>();
                if (Math.Abs(lechN) > tol) ev.Add($"Báo cáo NXT kế toán ghi nhập {Vn(kr.NhapKg ?? 0)} kg nhưng chứng từ chi tiết cộng {Vn(ct.Ketoan.Nhap)} kg (NXT {(lechN > 0 ? "cao" : "thấp")} hơn {Vn(Math.Abs(lechN))} kg).");
                if (Math.Abs(lechX) > tol) ev.Add($"Báo cáo NXT kế toán ghi xuất {Vn(kr.XuatKg ?? 0)} kg nhưng chứng từ chi tiết cộng {Vn(ct.Ketoan.Xuat)} kg (NXT {(lechX > 0 ? "cao" : "thấp")} hơn {Vn(Math.Abs(lechX))} kg).");
                if (Math.Abs(diffAdj) <= tol) { ev.Add("Nếu tồn kế toán tính theo đúng chứng từ chi tiết thì chênh lệch = 0 → số lệch này được giải thích đầy đủ."); outp.Add(Mk("ct-lech-nxt", "xac-dinh", "cao", ev, "Kế toán rà lại số liệu NXT so với chứng từ chi tiết của mã này.")); }
                else if (Math.Abs(diffAdj) < abs - tol) { ev.Add($"Theo chứng từ chi tiết, chênh lệch còn {Sg(diffAdj)} kg (giảm so với {Sg(d)} kg) → giải thích MỘT PHẦN."); outp.Add(Mk("ct-lech-nxt", "nghi-ngo", "trung binh", ev, "Kế toán rà lại số liệu NXT so với chứng từ chi tiết của mã này.")); }
                else { ev.Add("Số lệch này không giải thích được chênh lệch hiện tại."); outp.Add(Mk("ct-lech-nxt", "nghi-ngo", "thap", ev, "Kế toán rà lại số liệu NXT so với chứng từ chi tiết.")); }
            }
        }
        // 5. Chứng từ nghi ngờ
        var L = ct.Lines;
        List<CkLine> WithFlag(string code) => L.Where(l => l.Flags.Any(f => f.Code == code)).ToList();
        List<string> Refs(List<CkLine> a) => a.Select(l => l.V.Id).ToList();
        string FlagText(CkLine l, string code) => l.Flags.First(f => f.Code == code).Text;
        var eq = WithFlag("bang-chenh");
        if (eq.Count > 0) outp.Add(Mk("ct-bang-chenh", "nghi-ngo", eq.Count == 1 ? "trung binh" : "thap", eq.Take(3).Select(l => $"Chứng từ {l.V.Phieu} ({(l.V.Dir == "N" ? "nhập" : "xuất")}, {Dmy(l.V.Ngay)}) {Vn(l.V.Kg)} kg = đúng mức chênh lệch {Vn(abs)} kg.").Concat(eq.Count > 3 ? new[] { $"… và {eq.Count - 3} chứng từ khác." } : Array.Empty<string>()),
            "Kiểm tra chứng từ này có được ghi ở cả kho và kế toán, đúng mã, đúng ngày chưa.", null, Refs(eq)));
        var dup = WithFlag("trung");
        if (dup.Count > 0) { var same = dup.Any(l => Math.Abs(l.V.Kg - abs) <= pt && abs >= 1); outp.Add(Mk("ct-trung", "nghi-ngo", same ? "cao" : "thap", dup.Take(3).Select(l => FlagText(l, "trung") + $" (dòng {l.V.Row})"), "Kiểm tra chứng từ có bị nhập trùng không.", null, Refs(dup))); }
        var out2 = L.Where(l => l.Flags.Any(f => f.Code is "ngoai-ky" or "sau-chot" or "sau-kiem-ke")).ToList();
        if (out2.Count > 0)
        {
            var sum = R6(out2.Sum(l => l.V.Dir == "N" ? l.V.Kg : -l.V.Kg)); var match = Math.Abs(Math.Abs(sum) - abs) <= pt && abs >= 1;
            outp.Add(Mk("ct-ngoai-ky", "nghi-ngo", match ? "trung binh" : "thap", out2.Take(3).Select(l => $"Chứng từ {l.V.Phieu} ({Dmy(l.V.Ngay)}): " + l.Flags.First(f => f.Code is "ngoai-ky" or "sau-chot" or "sau-kiem-ke").Text).Concat(match ? new[] { $"Tổng chứng từ ngoài kỳ {Sg(sum)} kg ≈ mức chênh lệch." } : Array.Empty<string>()), "Kiểm tra ngày ghi nhận của các chứng từ này.", null, Refs(out2)));
        }
        var noMatch = WithFlag("khong-doi-ung");
        if (noMatch.Count > 0) outp.Add(Mk("ct-khong-doi-ung", "nghi-ngo", noMatch.Any(l => Math.Abs(l.V.Kg - abs) <= pt && abs >= 1) ? "trung binh" : "thap", noMatch.Take(3).Select(l => FlagText(l, "khong-doi-ung") + $" [{l.V.Phieu}]"), "Đối chiếu từng chứng từ giữa kho và kế toán.", null, Refs(noMatch)));
        var lechSo = WithFlag("lech-so");
        if (lechSo.Count > 0) outp.Add(Mk("ct-lech-so", "nghi-ngo", "trung binh", lechSo.Take(3).Select(l => FlagText(l, "lech-so")), "Hai bên ghi số lượng khác nhau cho cùng chứng từ — xác định số đúng.", null, Refs(lechSo)));
        // 6. Nguồn tự không khớp / trọng lượng / đơn vị
        var selfEv = new List<(string s, double v, string u)>();
        if (it.AaRow?.SelfLech is { } sa && sa != 0) selfEv.Add(("An An", sa, it.AaRow.SelfUnit ?? "kg"));
        if (it.KtRow?.SelfLech is { } sk && sk != 0) selfEv.Add(("Kế toán", sk, "kg"));
        if (it.GuiRow is { SelfLech: not 0 } gr) selfEv.Add(("Kho gửi", gr.SelfLech, "kg"));
        if (selfEv.Count > 0) outp.Add(Mk("tu-khong-khop", "nghi-ngo", selfEv.Any(x => x.u == "kg" && Math.Abs(Math.Abs(x.v) - abs) <= pt) ? "trung binh" : "thap", selfEv.Select(x => $"{x.s}: Tồn đầu + Nhập − Xuất lệch {Sg(x.v)} {x.u} so với Tồn cuối (nghi tồn đầu kỳ hoặc phát sinh ghi sai)."), "Rà lại tồn đầu kỳ và phát sinh của nguồn này."));
        if (it.AaRow?.NhetLech is { } nl && nl != 0)
            outp.Add(Mk("sai-trong-luong", "nghi-ngo", Math.Abs(Math.Abs(nl) - abs) <= pt ? "cao" : "thap", new[] { $"An An: Tồn cuối (NET) {Vn(it.AaRow.Kg)} kg ≠ {Vn(it.AaRow.Sl ?? 0)} × {Vn(it.AaRow.W ?? 0)} kg = {Vn(R6((it.AaRow.Sl ?? 0) * (it.AaRow.W ?? 0)))} kg (lệch {Sg(nl)})." }, "Kiểm tra trọng lượng 1 đơn vị / NET của mã này trong kho An An."));
        var netL = L.Where(l => l.Flags.Any(f => f.Code == "net-lech")).ToList();
        if (netL.Count > 0) outp.Add(Mk("sai-trong-luong", "nghi-ngo", "thap", netL.Take(2).Select(l => $"Chứng từ {l.V.Phieu}: " + FlagText(l, "net-lech")), "Kiểm tra Net / số kiện của chứng từ.", null, Refs(netL)));
        if (it.AaRow is not null && it.AaSL is { } asl && it.PKt && Math.Abs(asl - it.AaRow.Kg) > tol && it.Kt is { } ktv && Math.Abs(ktv - asl) <= tol)
            outp.Add(Mk("don-vi", "nghi-ngo", "cao", new[] { $"Kế toán ghi {Vn(ktv)} kg đúng bằng SỐ THÙNG của An An ({Vn(asl)}), trong khi số kg của An An là {Vn(it.AaRow.Kg)}." }, "Kiểm tra kế toán có nhập số thùng vào cột kg không."));
        // 7. Kho gửi sai kỳ
        if (X.GuiPeriodBad && it.GuiRow is { } gg)
        {
            var late = gg.Lots.Where(l => !string.IsNullOrEmpty(l.NgayNhap) && X.Period is { To.Length: > 0 } p && string.CompareOrdinal(l.NgayNhap, p.To) > 0).ToList();
            var mv = R6(gg.NhapKg - gg.XuatKg);
            if (late.Count > 0 || Math.Abs(mv) > tol)
            {
                var ev = new List<string>();
                if (late.Count > 0) ev.Add($"{late.Count} lô nhập kho gửi sau cuối kỳ ({string.Join(", ", late.Take(2).Select(l => Dmy(l.NgayNhap)))}) — nằm trong tồn kho gửi nhưng ngoài kỳ.");
                if (Math.Abs(mv) > tol) ev.Add($"Báo cáo kho gửi kỳ {Dmy(X.GuiPeriod?.From)} – {Dmy(X.GuiPeriod?.To)} có phát sinh ròng {Sg(mv)} kg.");
                outp.Add(Mk("gui-ngoai-ky", "nghi-ngo", Math.Abs(Math.Abs(mv) - abs) <= pt && abs >= 1 ? "trung binh" : "thap", ev, "Lấy lại báo cáo kho gửi đúng kỳ đối chiếu."));
            }
        }
        // 8. Kiểm kê thực tế
        if (X.HasKk && it.Kk is { } kkv)
        {
            var kt0 = it.Kt ?? 0;
            if (Math.Abs(kkv - kt0) <= tol && Math.Abs(kkv - it.Kho) > tol) outp.Add(Mk("kiem-ke", "nghi-ngo", "cao", new[] { $"Kiểm kê thực tế {Vn(kkv)} kg KHỚP số kế toán ({Vn(kt0)} kg), khác số kho {Vn(it.Kho)} kg." }, "Số liệu của kho (An An / gửi) có khả năng sai — rà phiếu nhập xuất của kho."));
            else if (Math.Abs(kkv - it.Kho) <= tol && Math.Abs(kkv - kt0) > tol) outp.Add(Mk("kiem-ke", "nghi-ngo", "cao", new[] { $"Kiểm kê thực tế {Vn(kkv)} kg KHỚP số kho ({Vn(it.Kho)} kg), khác số kế toán {Vn(kt0)} kg." }, "Số liệu kế toán có khả năng thiếu / thừa chứng từ — rà chứng từ của kế toán."));
            else if (Math.Abs(kkv - kt0) > tol && Math.Abs(kkv - it.Kho) > tol) outp.Add(Mk("kiem-ke", "nghi-ngo", "thap", new[] { $"Kiểm kê {Vn(kkv)} kg không khớp cả kho ({Vn(it.Kho)}) lẫn kế toán ({Vn(kt0)})." }, "Kiểm kê lại mã này."));
        }
        // 9. Tồn âm
        if (it.Kho < -tol || (it.Kt is { } kt1 && kt1 < -tol))
            outp.Add(Mk("ton-am", "xac-dinh", "cao", new[] { (it.Kho < -tol ? $"Tồn kho âm {Vn(it.Kho)} kg. " : "") + (it.Kt is { } kt2 && kt2 < -tol ? $"Tồn kế toán âm {Vn(kt2)} kg." : "") }, "Tồn âm là dấu hiệu thiếu phiếu nhập hoặc thừa phiếu xuất — rà chứng từ."));
        // 10. Không có bằng chứng
        outp = outp.OrderByDescending(RankOf).ToList();
        if (outp.Count == 0) outp.Add(Mk("chua-xac-dinh", "chua", "thap", new[] { "Không tìm thấy dấu hiệu rõ ràng trong dữ liệu đã nạp. " + side + Vn(abs) + " kg." }, "Đối chiếu chứng từ gốc của mã này."));
        return outp;
    }

    // ---------- ĐỐI CHIẾU ----------
    public static CkResult Reconcile(CkInput input)
    {
        var opts = input.Options; var period = input.Period;
        Dictionary<string, CkRow> Idx(CkSource? s, List<string> order)
        {
            var m = new Dictionary<string, CkRow>();
            foreach (var r in s?.Rows ?? new()) { if (!m.ContainsKey(r.Ma)) order.Add(r.Ma); m[r.Ma] = r; }
            return m;
        }
        List<string> oA = new(), oK = new(), oQ = new();
        var aa = Idx(input.AnAn, oA); var kt = Idx(input.KeToan, oK); var kk = Idx(input.KiemKe, oQ);
        var (gui, oG) = FoldGui(input.Gui?.Rows);
        var ktKnown = new HashSet<string>(input.KeToan?.KnownCodes ?? new());
        foreach (var r in input.KeToan?.Rows ?? new()) ktKnown.Add(r.Ma);
        var danhMuc = input.DanhMuc is { Count: > 0 } ? new HashSet<string>(input.DanhMuc) : null;
        var vouchers = input.Ct;
        var kiemKeNgay = opts.KiemKeNgay.Length > 0 ? opts.KiemKeNgay : input.KiemKe?.Period?.To ?? "";
        var tr = CkTrace.Analyze(vouchers, period, kiemKeNgay, opts.Tol);
        var hasCtKt = vouchers.Any(v => v.Src == "ketoan");
        var guiPeriod = input.Gui?.Period;
        var guiPeriodBad = guiPeriod is not null && period is { From.Length: > 0, To.Length: > 0 } && guiPeriod.To != period.To;
        var universe = new List<string>(); var seen = new HashSet<string>();
        foreach (var m in oA.Concat(oK).Concat(oG).Concat(oQ)) if (seen.Add(m)) universe.Add(m);

        var R = new CkResult { Period = period, Options = opts, Trace = tr, HasCtKt = hasCtKt, DanhMucSize = danhMuc?.Count ?? 0 };
        var all = universe.Select(ma => BuildItem(ma, aa, kt, gui, kk, ktKnown)).ToList();
        var items = new List<CkItem>();
        foreach (var it in all)
        {
            var any = Math.Abs(it.Kho) > opts.Tol || (it.Kt is { } k && Math.Abs(k) > opts.Tol) || (it.Kk is { } q && Math.Abs(q) > opts.Tol);
            if (!any && !opts.ShowZero) { R.HiddenZero++; continue; }
            if (opts.HideBot && IsBot(it.Ten)) { R.HiddenBot++; continue; }
            it.Active = any; it.Status = Math.Abs(it.Diff) <= opts.Tol ? "match" : "diff";
            items.Add(it);
        }
        foreach (var it in items) it.Ct = CkTrace.ForItem(tr, it.Ma, it.Diff, opts.PairTol);
        var diffItems = items.Where(i => i.Status == "diff").ToList();
        var pool = diffItems.Select(i => new CkPoolItem(i.Ma, i.Ten, i.Diff)).ToList();
        foreach (var it in diffItems) { var (c, g) = CkSuggest.SuggestFor(new CkPoolItem(it.Ma, it.Ten, it.Diff), pool, opts); it.Cands = c; it.Combos = g; }
        // nhóm mã nghi ngờ (union-find)
        var uf = new Dictionary<string, string>();
        string Find(string x) { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; }
        foreach (var it in diffItems) uf[it.Ma] = it.Ma;
        void Uni(string a, string b) { if (!uf.ContainsKey(a) || !uf.ContainsKey(b)) return; var ra = Find(a); var rb = Find(b); if (ra != rb) uf[ra] = rb; }
        foreach (var it in diffItems) { foreach (var c in it.Cands) if (c.Conf != "thap") Uni(it.Ma, c.Ma); foreach (var g in it.Combos) if (g.Conf != "thap") foreach (var m in g.Codes) Uni(it.Ma, m); }
        var groups = new Dictionary<string, List<string>>(); var gOrder = new List<string>();
        foreach (var it in diffItems) { var r = Find(it.Ma); if (!groups.TryGetValue(r, out var g)) { groups[r] = g = new(); gOrder.Add(r); } g.Add(it.Ma); }
        foreach (var it in items) { var g = it.Status == "diff" ? groups[Find(it.Ma)] : null; it.Group = g is { Count: > 1 } ? Find(it.Ma) : ""; }
        foreach (var k in gOrder.Where(k => groups[k].Count < 2).ToList()) groups.Remove(k);
        R.Groups = groups;
        var X = new Ctx(opts, danhMuc, danhMuc?.Count ?? 0, input.KiemKe is not null, hasCtKt, false, guiPeriod, period);
        foreach (var it in diffItems) it.Decomp = Decompose(it, input);
        bool IsLocator(CkCause c) => CkConfig.Locator.Contains(c.Code) || (c.Code == "lech-dau-ky" && c.Basis != "xac-dinh");
        foreach (var it in diffItems)
        {
            it.Causes = CausesFor(it, X);
            it.RootKnown = it.Causes.Any(c => c.Code != "chua-xac-dinh" && !IsLocator(c) && (c.Basis == "xac-dinh" || Conf.GetValueOrDefault(c.Conf) >= 2));
        }
        items = items.OrderByDescending(i => Math.Abs(i.Diff)).ThenBy(i => i.Ma, StringComparer.Ordinal).ToList();
        R.Items = items; R.ByMa = items.ToDictionary(i => i.Ma);
        R.Offsets = CkSuggest.OffsettingGroups(pool, opts);
        R.Kpi = Overlay(R, null);
        R.Validation = Validate(input, R, tr);
        return R;
    }

    public static CkKpi Overlay(CkResult R, CkState? st)
    {
        var opts = R.Options; var items = R.Items;
        var decisions = st?.Decisions ?? new(); var adjSum = new Dictionary<string, double>(); var adjK = new Dictionary<string, double>(); var adjT = new Dictionary<string, double>();
        foreach (var a in st?.Adjustments ?? new())
        {
            if (a.Cancelled) continue;
            if (a.Side == "kt") { adjT[a.Ma] = R6(adjT.GetValueOrDefault(a.Ma) + a.Qty); adjSum[a.Ma] = R6(adjSum.GetValueOrDefault(a.Ma) - a.Qty); }
            else { adjK[a.Ma] = R6(adjK.GetValueOrDefault(a.Ma) + a.Qty); adjSum[a.Ma] = R6(adjSum.GetValueOrDefault(a.Ma) + a.Qty); }
        }
        foreach (var it in items)
        {
            var dec = decisions.GetValueOrDefault(it.Ma); it.Decision = dec; it.Adj = adjSum.GetValueOrDefault(it.Ma);
            it.AdjKho = adjK.GetValueOrDefault(it.Ma); it.AdjKt = adjT.GetValueOrDefault(it.Ma); it.KhoAfter = R6(it.Kho + it.AdjKho); it.KtAfter = R6((it.Kt ?? 0) + it.AdjKt);
            it.DiffAfter = R6(it.KhoAfter - it.KtAfter); it.StatusAfter = Math.Abs(it.DiffAfter) <= opts.Tol ? "match" : "diff";
            it.Stale = dec is { Status: "xong", DiffAtDecision: { } dad } && Math.Abs(dad - it.Diff) > opts.Tol;
            var s = dec?.Status ?? "";
            if (s.Length == 0)
            {
                var c = it.Cause?.Code;
                s = it.Cause is null ? "" : (!it.RootKnown && it.Cands.Count == 0 && it.Combos.Count == 0 && it.Ct.NFlag == 0) ? "chua"
                    : (it.Cands.Count > 0 || it.Combos.Count > 0) && Pairish.Contains(c) ? "nghi-ma" : c is not null && c.StartsWith("ct-", StringComparison.Ordinal) ? "nghi-phieu" : it.RootKnown ? "dang-tim" : "chua";
            }
            it.AutoDone = it.Status == "diff" && it.Adj != 0 && it.StatusAfter == "match" && s != "xong";
            it.StatusUi = it.Status == "match" ? "" : it.AutoDone ? "xong" : s;
            it.TagThieu = it.Status == "diff" && it.Diff < 0; it.TagThua = it.Status == "diff" && it.Diff > 0;
            it.TagQuyCach = it.Causes.Any(c => c.Code == "quy-cach");
            it.TagSaiMa = it.Causes.Any(c => c.Code is "nhan-ma" or "quy-cach" or "chia-ma" or "ngoai-danh-muc" or "ma-sai-mahoa");
            it.TagCt = it.Ct.NFlag > 0 && it.Status == "diff"; it.TagXong = it.StatusUi == "xong";
        }
        var lech = items.Where(i => i.Status == "diff").ToList();
        double Sum(IEnumerable<CkItem> a, Func<CkItem, double> f) => R6(a.Sum(f));
        return new CkKpi
        {
            Tong = items.Count, Khop = items.Count - lech.Count, Lech = lech.Count,
            ChuaXacDinh = lech.Count(i => !i.RootKnown && i.StatusUi != "xong"), DaXuLy = lech.Count(i => i.StatusUi == "xong"),
            TienDo = lech.Count > 0 ? (int)Rnd(lech.Count(i => i.StatusUi == "xong") * 100.0 / lech.Count) : 100,
            TongAA = Sum(items, i => i.Aa ?? 0), TongGui = Sum(items, i => i.Gui ?? 0), TongKho = Sum(items, i => i.Kho), TongKt = Sum(items, i => i.Kt ?? 0),
            TongKk = items.Any(i => i.Kk is not null) ? Sum(items, i => i.Kk ?? 0) : null,
            LechRong = Sum(lech, i => i.Diff), LechTuyetDoi = Sum(lech, i => Math.Abs(i.Diff)), LechRongSau = Sum(items, i => i.DiffAfter), LechTuyetDoiSau = Sum(items, i => Math.Abs(i.DiffAfter)),
            Thieu = lech.Count(i => i.TagThieu), Thua = lech.Count(i => i.TagThua), QuyCach = lech.Count(i => i.TagQuyCach), SaiMa = lech.Count(i => i.TagSaiMa), ChungTu = lech.Count(i => i.TagCt),
            BoQuaBot = R.HiddenBot, BoQuaZero = R.HiddenZero,
            Bubu = lech.Count > 0 && Math.Abs(Sum(lech, i => i.Diff)) <= opts.PairTol * 2 && Sum(lech, i => Math.Abs(i.Diff)) > opts.Tol,
        };
    }

    private static List<CkCheck> Validate(CkInput input, CkResult R, CkTrace.Result tr)
    {
        var outp = new List<CkCheck>();
        var need = new List<string>(); if (input.AnAn is null) need.Add("anan"); if (input.KeToan is null) need.Add("ketoan");
        if (need.Count > 0) outp.Add(new("err", "Chưa nạp: " + string.Join(", ", need.Select(k => CkConfig.SourceTitle[k]))));
        else outp.Add(new("ok", "Đọc dữ liệu thành công"));
        var srcs = new (string k, CkSource? s)[] { ("anan", input.AnAn), ("ketoan", input.KeToan), ("gui", input.Gui), ("kiemke", input.KiemKe), ("anan_nhap", input.AnAnNhap), ("anan_xuat", input.AnAnXuat) };
        var unrec = srcs.Take(4).Where(x => x.s is not null && x.s.Mapping.Count == 0).ToList();
        if (unrec.Count == 0 && need.Count == 0) outp.Add(new("ok", "Nhận diện cột và định dạng hợp lệ"));
        outp.Add(new(R.Items.Count > 0 ? "ok" : "warn", $"Mã hàng: {Vn(R.Items.Count)} mã" + (R.Kpi.BoQuaBot > 0 ? $" (ẩn {R.Kpi.BoQuaBot} mã Bột)" : "")));
        foreach (var (k, s) in srcs)
        {
            if (s is null) continue; var title = CkConfig.SourceTitle[k];
            if (s.PeriodCheck is { State: "khac" } pc) outp.Add(new("info", title + ": " + pc.Text));
            var cn = s.Stats.Counts; int C(string key) => cn.GetValueOrDefault(key);
            if (C("so-loi") > 0) outp.Add(new("err", $"{title}: {C("so-loi")} ô số không đọc được (dòng bị bỏ, không tính là 0)"));
            if (C("thieu-so") > 0) outp.Add(new("warn", $"{title}: {C("thieu-so")} dòng thiếu số lượng"));
            if (C("ma-trung") > 0) outp.Add(new("warn", $"{title}: {C("ma-trung")} dòng trùng mã (đã cộng dồn)"));
            if (C("ton-am") > 0) outp.Add(new("warn", $"{title}: {C("ton-am")} dòng tồn âm"));
            if (C("tu-khong-khop") > 0) outp.Add(new("warn", $"{title}: {C("tu-khong-khop")} dòng đầu + nhập − xuất ≠ cuối"));
            if (C("trong-luong") > 0) outp.Add(new("warn", $"{title}: {C("trong-luong")} dòng sai trọng lượng / Net"));
            if (C("ma-khong-chuyen") > 0) outp.Add(new("warn", $"{title}: {C("ma-khong-chuyen")} mã chưa chuyển được sang 21 ký tự"));
            if (C("don-vi") > 0) outp.Add(new("warn", $"{title}: có dòng đơn vị khác kg"));
            if (C("dvt-la") > 0) outp.Add(new("info", "An An: cột ĐVT chứa mã hàng — bỏ qua, đối chiếu theo kg"));
            if (C("ngay-loi") > 0) outp.Add(new("err", $"{title}: {C("ngay-loi")} ngày chứng từ không đọc được"));
        }
        if (input.KeToan?.Ct is { } ct) outp.Add(new("ok", $"Chứng từ kế toán: {Vn(ct.Stats.NRows)} dòng ({Dmy(ct.Stats.From)} – {Dmy(ct.Stats.To)})"));
        else if (input.KeToan is not null) outp.Add(new("info", "Kế toán: không có sheet chứng từ — bỏ qua phân tích chứng từ kế toán"));
        if (tr.NgoaiKy > 0) outp.Add(new("warn", $"Có {tr.NgoaiKy} chứng từ nằm ngoài kỳ đối chiếu"));
        var nDupAll = (input.AnAn?.Stats.Counts.GetValueOrDefault("ma-trung") ?? 0) + (input.KeToan?.Stats.Counts.GetValueOrDefault("ma-trung") ?? 0);
        if (nDupAll == 0 && need.Count == 0) outp.Add(new("ok", "Không phát hiện dòng trùng mã"));
        if (R.Kpi.Bubu) outp.Add(new("warn", $"Tổng chênh lệch ròng ≈ 0 nhưng từng mã vẫn lệch ({Vn(R.Kpi.LechTuyetDoi)} kg tuyệt đối) — lệch bù trừ giữa các mã"));
        return outp;
    }
}

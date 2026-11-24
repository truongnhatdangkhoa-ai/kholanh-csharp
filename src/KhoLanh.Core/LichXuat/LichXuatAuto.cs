using KhoLanh.Core.PhieuKho;
using static KhoLanh.Core.LichXuat.LichXuatCore;

namespace KhoLanh.Core.LichXuat;

/// <summary>
/// TỰ TẠO LIST / PHIẾU THEO LỊCH XUẤT (chép lich-xuat-cont.js › pnxAfterWrite / pnxReconcile + phieu-nhap-xuat-ui.js › PC 8.28 của app cũ).
/// Mỗi lịch XUẤT (ngày ≥ hôm nay, chưa "Đã xuất") thêm mới / đổi LSX / đổi loại lịch — từ mọi đường ghi (hộp Thêm-Sửa, Thêm nhanh, Trợ lý, email) vì đều qua
/// <see cref="Mutate{T}"/> — tự có List sản xuất cho từng LSX: lấy từ tồn kho, không có hàng thì List TRỐNG theo tên LSX. Mở Lịch xuất / Trang Tổng quan:
/// rà bù các lịch 14 ngày tới chưa có trong nhật ký. Tuỳ chọn "List + Phiếu": tạo thêm Phiếu xuất kho (LSX chung cont vào chung phiếu).
/// Chống trùng bằng nhật ký PNX_AutoLog (dùng chung app cũ). XÓA LỊCH KHÔNG XÓA List / Phiếu.
/// </summary>
public sealed partial class LichXuatService
{
    public const string AutoKey = "lx_auto_mode";
    /// <summary>Bật ở app (tests mặc định tắt để không tạo List ngoài ý muốn).</summary>
    public bool Auto { get; set; }
    /// <summary>"list" (mặc định, như app cũ PC 2.89) · "phieu" = List + Phiếu xuất kho · "off".</summary>
    public string AutoMode
    {
        get { var v = _settings.Get(AutoKey); return v is "phieu" or "off" ? v : "list"; }
        set => _settings.Set(AutoKey, value is "phieu" or "off" ? value : "list");
    }
    private bool AutoOn => Auto && AutoMode != "off" && CanPnx;

    /// <summary>Kết quả lần tự tạo gần nhất (giao diện hiện thông báo).</summary>
    public event Action<List<string>>? AutoMade;
    private DateTime _reconAt = DateTime.MinValue;

    private static string LoaiPhieuOf(Lich x) => x.LoaiLich is "" or "xuat_khau_tp" or "xuat_khau" ? "xuat_khau" : x.LoaiLich;
    private static string CodeSig(Lich x) => string.Join("+", Codes(x.Lsx));

    private static Dictionary<string, (string Codes, string Loai)> Snap(List<Lich> all) =>
        all.Where(x => x.Id.Length > 0).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => (CodeSig(g.First()), LoaiPhieuOf(g.First())));

    private bool Upcoming(Lich x, string today) => Keep(x) && string.CompareOrdinal(x.Ngay, today) >= 0 && x.TrangThai != "ok" && Codes(x.Lsx).Count > 0;

    /// <summary>Lịch XUẤT vừa thêm / đổi LSX / đổi loại lịch → tự tạo.</summary>
    private void AfterWrite(Dictionary<string, (string Codes, string Loai)> snap, List<Lich> all, DateTime now)
    {
        if (!AutoOn) return;
        var t = Iso(now);
        var chg = all.Where(x => Upcoming(x, t) && (!snap.TryGetValue(x.Id, out var o) || o.Codes != CodeSig(x) || o.Loai != LoaiPhieuOf(x))).ToList();
        if (chg.Count > 0) RunAuto(chg);
    }

    /// <summary>Rà bù: lịch XUẤT trong 14 ngày tới có LSX chưa có khóa '__list' trong nhật ký (tối đa 1 lần / 2 phút).</summary>
    public List<string> Reconcile(bool force = false)
    {
        if (!AutoOn || (!force && (DateTime.Now - _reconAt).TotalMinutes < 2)) return new();
        _reconAt = DateTime.Now;
        try
        {
            var now = Now(); string t = Iso(now), t2 = Iso(now.AddDays(14));
            var cand = Rows.Where(x => Upcoming(x, t) && string.CompareOrdinal(x.Ngay, t2) <= 0).ToList();
            if (cand.Count == 0) return new();
            var pnx = _pnx(); pnx.Load(); pnx.LoadAutoLog();
            var need = cand.Where(x => Codes(x.Lsx).Any(c => !pnx.Logged(c, PhieuKhoService.LoaiList))).ToList();
            return need.Count > 0 ? RunAuto(need) : new();
        }
        catch (Exception e) { return new() { "Chưa tự tạo được List: " + e.Message }; }
    }

    private List<string> RunAuto(List<Lich> chg)
    {
        var msgs = new List<string>();
        try
        {
            msgs.AddRange(ListsFor(chg, true, "tự tạo theo lịch xuất"));
            if (AutoMode == "phieu")
            {
                LoadPnx();
                var pnx = _pnx(); pnx.Load(); pnx.LoadAutoLog();
                // nhóm chung cont: cùng ngày + cùng mã cont (không có mã cont → mỗi lịch 1 nhóm)
                foreach (var g in chg.GroupBy(x => x.Cont.Length > 0 ? x.Ngay + "|" + x.Cont : "#" + x.Id))
                {
                    var l = g.ToList();
                    if (Codes(string.Join(" + ", l.Select(z => z.Lsx))).All(c => pnx.Logged(c, LoaiPhieuOf(l[0]), l[0].Ngay))) continue;
                    if (LoaiPhieuOf(l[0]) != "xuat_khau") continue; // loại phiếu khác chưa có mẫu tự điền → chỉ tạo List (như app cũ)
                    try
                    {
                        var (p, created, notes) = CreatePhieu(l.Select(z => z.Id), true);
                        msgs.Add(created ? "Đã tự tạo phiếu " + p.SoPhieu + " cho " + string.Join(", ", l.Select(z => Label(z.Lsx))) + "." : notes.FirstOrDefault() ?? "");
                        if (!created) { pnx.LoadAutoLog(); foreach (var c in l.SelectMany(z => Codes(z.Lsx)).Distinct()) pnx.LogAdd(c, "xuat_khau", TagOf(l), p.Id, ghiChu: "đã có phiếu"); pnx.SaveAutoLog(); }
                    }
                    catch (Exception e) { msgs.Add("Chưa tự tạo được phiếu: " + e.Message); }
                }
            }
        }
        catch (Exception e) { msgs.Add("Chưa tự tạo được List / Phiếu: " + e.Message + " — sẽ thử lại khi mở lại Lịch xuất."); }
        LoadPnx();
        msgs.RemoveAll(m => m.Length == 0);
        if (msgs.Count > 0) AutoMade?.Invoke(msgs);
        return msgs;
    }

    private PhieuKhoService.AutoTag TagOf(List<Lich> g) =>
        new(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"), g.Select(z => z.Ngay).Where(IsIso).DefaultIfEmpty("").Min() ?? "", g[0].LoaiLich, g.Select(z => z.Id).ToList());

    /// <summary>
    /// List cho mọi LSX của các lịch: đã có List → giữ (ghi nhật ký); chưa có → từ tồn kho; <paramref name="auto"/> và tồn chưa có hàng → List trống.
    /// Tự động: bỏ qua LSX đã có khóa '__list' trong nhật ký (đã tự tạo 1 lần — kể cả khi người dùng đã xóa List).
    /// </summary>
    private List<string> ListsFor(List<Lich> g, bool auto, string ghiChu)
    {
        var pnx = _pnx(); pnx.Load(); pnx.LoadAutoLog();
        var msgs = new List<string>(); var d = AnAn(); var logged = false;
        foreach (var code in g.SelectMany(z => Codes(z.Lsx)).Distinct())
        {
            var mine = g.Where(z => Codes(z.Lsx).Contains(code)).ToList();
            var tag = TagOf(mine);
            if (auto && pnx.Logged(code, PhieuKhoService.LoaiList)) continue;
            if (pnx.FindList(code) is { } have)
            {
                if (!auto) msgs.Add($"LSX {code}: đã có List ({have.Data.Rows.Count} ngày) — giữ nguyên.");
                if (!pnx.Logged(code, PhieuKhoService.LoaiList)) { pnx.LogAdd(code, PhieuKhoService.LoaiList, tag, ghiChu: "đã có List"); logged = true; }
                continue;
            }
            var (lines, msg) = TpcLines(d, code);
            var ld = lines.Count > 0 ? ListFromStock(lines, code, Now()) : null;
            if (ld is not { Ok: true })
            {
                if (!auto) { msgs.Add($"LSX {code}: " + (lines.Count == 0 ? msg : "không còn hàng tồn để tạo List.")); continue; }
                var khach = mine.Select(z => z.Khach.Split(" · ")[0].Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
                pnx.SaveEmptyList(code, khach, tag);
                pnx.LogAdd(code, PhieuKhoService.LoaiList, tag, listNew: true, ghiChu: ghiChu + " (List trống)"); logged = true;
                msgs.Add($"LSX {code}: tồn kho chưa có hàng — đã tạo List TRỐNG theo tên LSX, bạn tự cập nhật.");
                continue;
            }
            PhieuKhoService.SetTag(ld, tag);
            pnx.SaveListData(ld, "(tồn kho)");
            pnx.LogAdd(code, PhieuKhoService.LoaiList, tag, listNew: auto, ghiChu: ghiChu); logged = true;
            msgs.Add($"LSX {code}: đã {(auto ? "tự " : "")}tạo List từ tồn kho — {ld.Rows.Count} ngày × {ld.Sizes.Count} size" + (ld.Warnings.Count > 0 ? " · ⚠ " + string.Join(" ", ld.Warnings) : "") + ".");
        }
        if (logged) { try { pnx.SaveAutoLog(); } catch { /* nhật ký lỗi không chặn List */ } }
        return msgs;
    }
}

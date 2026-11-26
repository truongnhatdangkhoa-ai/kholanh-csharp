using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;
using KhoLanh.Core.TonKhoGui;
using static KhoLanh.Core.LichXuat.LichXuatCore;

namespace KhoLanh.Core.LichXuat;

/// <summary>
/// LỊCH XUẤT CONTAINER + TRANG TỔNG QUAN (viết lại theo js/lich-xuat-cont.js + js/trang-chu.js của app cũ). Dùng CHUNG database "lichxuat" với app cũ
/// (LichXuatCont · TongKetThang · LX_ViecChuaLich · LX_TuDong) nên app cũ / điện thoại thấy cùng lịch.
/// Mỗi lần ghi đọc lại bảng mới nhất trước (không đè thay đổi của máy khác), tự chuyển lịch ngày đã qua sang "Đã xuất", gắn việc chờ của LSX vừa có lịch,
/// cập nhật Tổng kết tháng. Dò hàng theo LSX trong Thành Phẩm Chính của Tồn kho An An (cột Hợp đồng).
/// </summary>
public sealed partial class LichXuatService
{
    public const string PermKey = "lx", PermMail = "lxmail";
    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly SettingsService _settings;
    private readonly Func<PhieuKhoService> _pnx;
    private readonly object _lock = new();

    public Func<DateTime> Now { get; set; } = () => DateTime.Now;
    public IMailSource Mail { get; set; } = new ImapMailSource();

    public LichXuatService(Func<ISheetSource> src, Session session, AuditService audit, SettingsService settings, Func<PhieuKhoService> pnx)
    { _src = src; _session = session; _audit = audit; _settings = settings; _pnx = pnx; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    /// <summary>
    /// Mục Email (kết nối Gmail + mật khẩu ứng dụng lưu trên máy, người gửi tin cậy, duyệt / bỏ qua / hoàn tác thư, quét tay):
    /// CHỈ tài khoản Quản trị (1.75 — trước đây theo quyền "lxmail", Quản lý mặc định có). Quản lý / Nhân viên không thấy nút Email.
    /// </summary>
    public bool CanMail => Roles.IsAdmin(_session.Role) && CanEdit;
    /// <summary>
    /// Tự quét nền trên máy đã được Quản trị khai báo Gmail: chạy với mọi tài khoản đang đăng nhập có quyền sửa Lịch xuất
    /// (thư của người tin cậy vẫn tự lên lịch dù người ngồi máy không phải Quản trị); không mở được cài đặt / duyệt thư.
    /// </summary>
    public bool CanAutoMail => CanEdit;
    public bool CanPnx => _session.Can(PhieuKhoService.PermKey, Perm.Edit);
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản chỉ có quyền xem Lịch xuất — không sửa được."); }

    public List<Lich> Rows { get; private set; } = new();
    public List<Pend> Pending { get; private set; } = new();
    /// <summary>Phiếu xuất kho + kho List (để biết lịch đã có List / phiếu / cont / xe chưa). Nạp lại ở Load().</summary>
    public List<Phieu> Phieus { get; private set; } = new();
    public List<PhieuKhoService.ListRec> Lists { get; private set; } = new();
    /// <summary>Lỗi khi nạp Phiếu xuất kho (không chặn Lịch xuất).</summary>
    public string PnxError { get; private set; } = "";
    public event Action? Changed;

    // ------------------------------------------------------------------ đọc / ghi

    private JsonArray Read(string sheet)
    {
        try { return JsonNode.Parse(_src().Read(Db, sheet)) as JsonArray ?? new JsonArray(); } catch { return new JsonArray(); }
    }

    private List<Lich> ReadAll() => Read(Sheet).OfType<JsonObject>().Select(Lich.From).Where(r => r.Id.Length > 0 || r.Ngay.Length > 0).ToList();
    private List<Pend> ReadPend() => Read(SheetPend).OfType<JsonObject>().Select(o => new Pend(S(o["id"]), S(o["lsx"]), S(o["t"]), S(o["tao"])))
        .Where(p => p.Id.Length > 0 && p.Lsx.Length > 0 && p.T.Length > 0).ToList();

    public void Load()
    {
        if (!CanView) throw new BusinessException("Tài khoản không có quyền xem Lịch xuất.");
        Rows = ReadAll().Where(Keep).ToList();
        Pending = ReadPend();
        LoadPnx();
        Reconcile();
    }

    public void LoadPnx()
    {
        PnxError = "";
        try
        {
            var p = _pnx();
            if (!p.CanView) { Phieus = new(); Lists = new(); return; }
            p.Load(); Phieus = p.List.Where(x => x.Loai == "xuat_khau").ToList(); Lists = p.Lists.ToList();
        }
        catch (Exception e) { PnxError = e.Message; }
    }

    /// <summary>Đọc-sửa-ghi: luôn đọc bản mới nhất, chạy <paramref name="fn"/> trên TẤT CẢ dòng (kể cả dòng app cũ đang ẩn), rồi ghi lịch + tổng kết tháng + việc chờ.</summary>
    public T Mutate<T>(Func<List<Lich>, T> fn, string? log = null)
    {
        RequireEdit();
        lock (_lock)
        {
            var all = ReadAll(); var pend = ReadPend(); var now = Now();
            var snap = Snap(all);
            var r = fn(all);
            AutoDone(all, now);
            var rest = AttachPend(all, pend, now);
            var sheets = new Dictionary<string, JsonArray?>
            {
                [Sheet] = new JsonArray(all.OrderBy(x => x.Ngay, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).Select(x => (JsonNode)x.ToJson()).ToArray()),
                [SheetTk] = BuildTk(Read(SheetTk), all, now),
            };
            if (rest.Count != pend.Count) sheets[SheetPend] = PendSheet(rest);
            _src().Write(Db, sheets);
            Rows = all.Where(Keep).ToList(); Pending = rest;
            if (log is not null) _audit.Log(_session.Username, "Lịch xuất", log);
            AfterWrite(snap, all, now);
            Changed?.Invoke();
            return r;
        }
    }
    public void Mutate(Action<List<Lich>> fn, string? log = null) => Mutate(l => { fn(l); return 0; }, log);

    private static JsonArray PendSheet(IEnumerable<Pend> l) => new(l.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["lsx"] = p.Lsx, ["t"] = p.T, ["tao"] = p.Tao }).ToArray());

    // ------------------------------------------------------------------ số liệu Trang Tổng quan

    public Home BuildHome(string day) => LichXuatCore.BuildHome(Rows, day, Now(), StateOf);

    /// <summary>List / phiếu xuất kho / số cont / số xe của nhóm LSX (đọc từ module Phiếu xuất kho).</summary>
    public PnxState StateOf(List<Lich> g)
    {
        var codes = g.SelectMany(z => Codes(z.Lsx)).Select(PhieuKhoCore.LsxNorm).Distinct().ToList();
        if (codes.Count == 0) return PnxState.None;
        var cmp = codes.Select(TroLy.TroLyChuY.LsxCmp).ToHashSet();
        bool Hit(string lsx) => PhieuKhoCore.LsxList(lsx).Any(c => codes.Contains(PhieuKhoCore.LsxNorm(c)) || cmp.Contains(TroLy.TroLyChuY.LsxCmp(c)));
        var ngay = g[0].Ngay;
        var ph = Phieus.Where(p => Hit(p.Lsx)).OrderBy(p => Math.Abs(DayDiff(p.Ngay, ngay))).ToList();
        var list = Lists.Any(l => Hit(l.Lsx));
        var p0 = ph.FirstOrDefault();
        return new PnxState(list, p0 is not null, p0?.Xe.Cont.Trim().ToUpperInvariant() ?? "", p0?.Xe.SoXe.Trim() ?? "");
    }
    private static double DayDiff(string a, string b) => IsIso(a) && IsIso(b) ? (DateOf(a) - DateOf(b)).TotalDays : 999;

    public Phieu? PhieuOf(List<Lich> g)
    {
        var codes = g.SelectMany(z => Codes(z.Lsx)).Select(PhieuKhoCore.LsxNorm).ToHashSet();
        return Phieus.Where(p => PhieuKhoCore.LsxList(p.Lsx).Any(c => codes.Contains(PhieuKhoCore.LsxNorm(c)))).OrderBy(p => Math.Abs(DayDiff(p.Ngay, g[0].Ngay))).FirstOrDefault();
    }

    public List<Lich> Group(IEnumerable<string> ids) { var s = ids.ToHashSet(); return Rows.Where(r => s.Contains(r.Id)).OrderBy(r => r.Id, StringComparer.Ordinal).ToList(); }

    /// <summary>KPI tồn kho: tổng kg / số mã / ngày dữ liệu của Tồn kho An An và Tồn kho gửi.</summary>
    public sealed record StockKpi(double Kg, int Ma, string Date, bool Has);
    public (StockKpi AnAn, StockKpi Gui) Stock()
    {
        var src = _src();
        StockKpi a = new(0, 0, "", false), g = new(0, 0, "", false);
        try
        {
            var d = AnAnStore.Load(src);
            if (d.HasData)
            {
                int mi = d.Col("Mã hàng"), ti = d.Col("Tồn cuối"), wi = d.Col("Trọng lượng"), ni = d.ColExact("Tồn cuối (NET)");
                double kg = 0; var ma = new HashSet<string>();
                foreach (var r in d.Rows)
                {
                    var q = Num(ti >= 0 ? r[ti] : ""); var w = Num(wi >= 0 ? r[wi] : ""); var net = Num(ni >= 0 ? r[ni] : "");
                    kg += w > 0 ? q * w : net;
                    if (mi >= 0 && q != 0 && r[mi].Trim().Length > 0) ma.Add(r[mi].Trim().ToUpperInvariant());
                }
                a = new(kg, ma.Count, d.DataDate, true);
            }
        }
        catch { }
        try
        {
            var gd = GuiStore.Load(src);
            if (gd.Rows.Count > 0) g = new(gd.Rows.Sum(r => r.TrongLuong), gd.Rows.Select(r => r.MaHang.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().Count(), gd.DataDate, true);
        }
        catch { }
        return (a, g);
    }
    public static double Num(string? s) => double.TryParse((s ?? "").Replace(",", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : 0;

    // ------------------------------------------------------------------ thêm / sửa / xóa / chuyển

    public sealed class Input
    {
        public string Ngay = "", Gio = "", TrangThai = "plan", Lsx = "", GhiChu = "", ContSo = "";
        /// <summary>Id lịch cùng ngày muốn ghép chung container (rỗng = riêng).</summary>
        public string ChungVoi = "";
    }

    // ------------------------------------------------------------------ 1.90: LSX trùng lịch

    /// <summary>Nhóm đã đánh dấu "trùng hợp lệ" (sheet LX_TrungHopLe trong database lichxuat — mọi máy thấy chung).</summary>
    public HashSet<string> DupOkSigs() => Read(SheetDupOk).OfType<JsonObject>().Select(o => S(o["sig"])).Where(x => x.Length > 0).ToHashSet();

    /// <summary>LSX đang có từ 2 lịch chưa xuất trở lên (trừ nhóm đã đánh dấu hợp lệ).</summary>
    public List<DupGroup> DupGroups() => DupLsx(Rows, DupOkSigs());

    /// <summary>Đánh dấu nhóm trùng là hợp lệ (vd cùng LSX nhiều cont / nhiều ngày) — không báo nữa cho đúng bộ lịch này.</summary>
    public void MarkDupOk(DupGroup g)
    {
        RequireEdit();
        lock (_lock)
        {
            var cur = Read(SheetDupOk).OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).Where(o => S(o["sig"]) != g.Sig).ToList();
            var live = Rows.Select(r => r.Id).ToHashSet();
            // bỏ dấu cũ của bộ lịch đã không còn (giữ bảng gọn)
            cur = cur.Where(o => S(o["sig"]).Split('|').Last().Split(',').Any(live.Contains)).ToList();
            cur.Add(new JsonObject { ["sig"] = g.Sig, ["lsx"] = g.Lsx, ["by"] = _session.Username, ["at"] = Now().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) });
            _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetDupOk] = new JsonArray(cur.Select(o => (JsonNode)o).ToArray()) });
        }
        _audit.Log(_session.Username, "Lịch xuất", $"Đánh dấu LSX {g.Lsx} trùng lịch hợp lệ ({string.Join(", ", g.Rows.Select(r => Vn(r.Ngay)))})");
        Changed?.Invoke();
    }

    /// <summary>
    /// Gộp lịch trùng: giữ lịch <paramref name="keepId"/>, xóa các lịch <paramref name="dropIds"/>; giờ / số cont còn trống lấy từ lịch bị xóa,
    /// ghi chú và việc cần xử lý được gộp lại (không mất dữ liệu đã sửa tay).
    /// </summary>
    public string ResolveDup(string keepId, IEnumerable<string> dropIds)
    {
        var drop = dropIds.Where(x => x != keepId).ToHashSet();
        return Mutate(l =>
        {
            var k = l.FirstOrDefault(x => x.Id == keepId) ?? throw new BusinessException("Không còn lịch này (máy khác vừa sửa?).");
            var gone = l.Where(x => drop.Contains(x.Id)).ToList();
            foreach (var d in gone)
            {
                if (k.Gio.Length == 0) k.Gio = d.Gio;
                if (k.ContSo.Length == 0) k.ContSo = d.ContSo;
                if (d.GhiChu.Trim().Length > 0 && !k.GhiChu.Contains(d.GhiChu.Trim())) k.GhiChu = (k.GhiChu.Trim() + " · " + d.GhiChu.Trim()).Trim(' ', '·');
                var a = ViecOf(k);
                foreach (var v in ViecOf(d)) if (!a.Any(x => string.Equals(x.T.Trim(), v.T.Trim(), StringComparison.OrdinalIgnoreCase))) a.Add(v);
                SetViec(k, a);
            }
            l.RemoveAll(x => drop.Contains(x.Id));
            return $"Giữ lịch {Label(k.Lsx)} ngày {Vn(k.Ngay)}, xóa {gone.Count} lịch trùng ({string.Join(", ", gone.Select(x => Vn(x.Ngay)))}).";
        }, $"Gộp lịch trùng LSX → giữ {keepId}, xóa {string.Join(", ", drop)}");
    }

    /// <summary>Lịch chưa xuất của LSX ở ngày khác (đọc bản mới nhất) — hộp Thêm lịch hỏi "chuyển lịch cũ hay thêm mới" (1.90).</summary>
    public List<Lich> OtherDays(string lsx, string ngay, string? exceptId = null) => OpenOther(ReadAll().Where(Keep), NormLsxInput(lsx), ngay, exceptId);

    /// <summary>
    /// Thêm lịch NHƯNG là chuyển lịch cũ <paramref name="moveId"/> của cùng LSX sang ngày mới (kế hoạch đổi ngày, không tạo trùng):
    /// giữ việc / dò tồn / số LSX của lịch cũ; giờ, ghi chú, số cont, trạng thái lấy theo ô vừa nhập nếu có.
    /// </summary>
    public Lich MoveInto(string moveId, Input inp)
    {
        RequireEdit();
        if (!IsIso(inp.Ngay)) throw new BusinessException("Chọn ngày xuất.");
        return Mutate(l =>
        {
            var r = l.FirstOrDefault(x => x.Id == moveId) ?? throw new BusinessException("Không còn lịch cũ này (máy khác vừa xóa?).");
            if (SameDayDup(l, r.Lsx, inp.Ngay, r.Id) is { } d) throw new BusinessException($"{Label(d.Lsx)} đã có lịch ngày {Vn(d.Ngay)}.");
            var from = r.Ngay;
            MoveRec(r, inp.Ngay, Now());
            if (NormGio(inp.Gio) is { Length: > 0 } g) r.Gio = g;
            if (inp.GhiChu.Trim() is { Length: > 0 } gc) r.GhiChu = gc;
            if (inp.ContSo.Trim() is { Length: > 0 } cs) r.ContSo = cs.ToUpperInvariant();
            if (StLabel.ContainsKey(inp.TrangThai) && inp.TrangThai != "plan") r.TrangThai = inp.TrangThai;
            if (inp.ChungVoi.Length > 0 && l.FirstOrDefault(x => x.Id == inp.ChungVoi && x.Ngay == r.Ngay) is { } mate)
            {
                if (mate.Cont.Length == 0) mate.Cont = "c" + B36(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                r.Cont = mate.Cont;
            }
            _audit.Log(_session.Username, "Lịch xuất", $"Chuyển lịch {Label(r.Lsx)} ngày {Vn(from)} → {Vn(inp.Ngay)} (thay vì thêm lịch trùng)");
            return r;
        });
    }

    /// <summary>Thêm 1 lịch (tự dò Thành Phẩm Chính). Trả về lịch đã lưu.</summary>
    public Lich Add(Input inp)
    {
        RequireEdit();
        var lsx = NormLsxInput(inp.Lsx);
        if (!IsIso(inp.Ngay)) throw new BusinessException("Chọn ngày xuất.");
        if (lsx.Length == 0) throw new BusinessException("Nhập số LSX.");
        var r = new Lich { Id = NewId(), Ngay = inp.Ngay, Gio = NormGio(inp.Gio), TrangThai = StLabel.ContainsKey(inp.TrangThai) ? inp.TrangThai : "plan", Lsx = lsx, GhiChu = inp.GhiChu.Trim(), ContSo = inp.ContSo.Trim().ToUpperInvariant() };
        var look = Lookup(lsx);
        if (look.Found) Merge(r, look);
        return Mutate(l =>
        {
            if (SameDayDup(l, r.Lsx, r.Ngay) is { } d) throw new BusinessException($"{Label(d.Lsx)} đã có lịch ngày {Vn(d.Ngay)} — không thêm trùng.");
            if (inp.ChungVoi.Length > 0 && l.FirstOrDefault(x => x.Id == inp.ChungVoi) is { } mate)
            {
                if (mate.Cont.Length == 0) mate.Cont = "c" + B36(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                r.Cont = mate.Cont;
            }
            l.Add(r); return r;
        }, $"Thêm lịch {r.Lsx} ngày {Vn(r.Ngay)}");
    }

    public void Update(string id, Input inp)
    {
        RequireEdit();
        var lsx = NormLsxInput(inp.Lsx);
        if (!IsIso(inp.Ngay)) throw new BusinessException("Chọn ngày xuất.");
        if (lsx.Length == 0) throw new BusinessException("Nhập số LSX.");
        LookupResult? look = null;
        var old = Rows.FirstOrDefault(x => x.Id == id);
        if (old is not null && Sq(old.Lsx) != Sq(lsx)) look = Lookup(lsx);
        Mutate(l =>
        {
            var r = l.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không còn lịch này (máy khác vừa xóa?).");
            if (SameDayDup(l, lsx, inp.Ngay, id) is { } d) throw new BusinessException($"{Label(d.Lsx)} đã có lịch ngày {Vn(d.Ngay)}.");
            if (r.Ngay != inp.Ngay) r.Cont = "";
            r.Ngay = inp.Ngay; r.Gio = NormGio(inp.Gio); r.TrangThai = StLabel.ContainsKey(inp.TrangThai) ? inp.TrangThai : r.TrangThai;
            r.Lsx = lsx; r.GhiChu = inp.GhiChu.Trim(); r.ContSo = inp.ContSo.Trim().ToUpperInvariant();
            if (look is { Found: true }) Merge(r, look);
            if (inp.ChungVoi.Length > 0 && inp.ChungVoi != id && l.FirstOrDefault(x => x.Id == inp.ChungVoi && x.Ngay == r.Ngay) is { } mate)
            {
                if (mate.Cont.Length == 0) mate.Cont = "c" + B36(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                r.Cont = mate.Cont;
            }
            else if (inp.ChungVoi.Length == 0) r.Cont = "";
        }, $"Sửa lịch {lsx} ngày {Vn(inp.Ngay)}");
    }

    public void Delete(IEnumerable<string> ids)
    {
        var s = ids.ToHashSet();
        var del = Rows.Where(r => s.Contains(r.Id)).Select(r => Label(r.Lsx) + " " + Vn(r.Ngay)).ToList();
        Mutate(l => { l.RemoveAll(r => s.Contains(r.Id)); }, "Xóa lịch " + string.Join(", ", del));
    }

    /// <summary>Chuyển lịch sang ngày khác (ngày tương lai → Dự kiến; ngày đã qua → Đã xuất).</summary>
    public void Move(IEnumerable<string> ids, string ngay)
    {
        if (!IsIso(ngay)) throw new BusinessException("Ngày không hợp lệ.");
        var s = ids.ToHashSet();
        Mutate(l =>
        {
            foreach (var r in l.Where(r => s.Contains(r.Id)))
            {
                if (SameDayDup(l, r.Lsx, ngay, r.Id) is not null) throw new BusinessException($"{Label(r.Lsx)} đã có lịch ngày {Vn(ngay)}.");
                MoveRec(r, ngay, Now());
            }
        }, "Chuyển lịch sang ngày " + Vn(ngay));
    }
    private static void MoveRec(Lich r, string ngay, DateTime now)
    {
        r.Ngay = ngay; r.Cont = "";
        if (string.CompareOrdinal(ngay, Iso(now)) >= 0 && r.TrangThai == "ok") r.TrangThai = "plan";
    }

    public void SetStatus(IEnumerable<string> ids, string st)
    {
        if (!StLabel.ContainsKey(st)) return;
        var s = ids.ToHashSet();
        Mutate(l => { foreach (var r in l.Where(r => s.Contains(r.Id))) r.TrangThai = st; }, "Đổi trạng thái → " + StLabel[st]);
    }

    public void SetContSo(IEnumerable<string> ids, string cont)
    {
        var s = ids.ToHashSet(); var c = Regex.Replace(cont ?? "", @"\s+", "").ToUpperInvariant();
        Mutate(l => { foreach (var r in l.Where(r => s.Contains(r.Id))) r.ContSo = c; }, "Số cont " + c);
    }

    /// <summary>Đọc email «Thông tin xe» dán vào → số cont (+ xe / seal / tài xế để xem).</summary>
    public static (bool Ok, string Cont, Xe Xe, List<string> Found, string Error) ReadXeMail(string text)
    {
        var p = new Phieu(); var r = PhieuKhoCore.ApplyXeEmail(p, text);
        var cont = Regex.Replace(p.Xe.Cont, @"\s+", "").ToUpperInvariant();
        if (r.Found.Count == 0) return (false, "", p.Xe, r.Found, "Không thấy mục nào của email «Thông tin xe» (Họ tên tài xế, Số điện thoại, Số xe, Số cont, Số seal…).");
        if (cont.Length == 0) return (false, "", p.Xe, r.Found, "Email không có mục «Số cont». Đã đọc: " + string.Join(", ", r.Found) + ".");
        return (true, cont, p.Xe, r.Found, "");
    }

    private static string NormGio(string g)
    {
        var m = Regex.Match(g ?? "", @"(\d{1,2})\s*[:hH]\s*(\d{2})?");
        return m.Success && int.Parse(m.Groups[1].Value) < 24 ? int.Parse(m.Groups[1].Value).ToString("00") + ":" + (m.Groups[2].Success ? m.Groups[2].Value : "00") : "";
    }
    /// <summary>Ô LSX: "26.05.024" / "lsx 26.05.024" / "LPF LSX 26.07.019" → "LSX 26.05.024"; nhiều mã giữ "+".</summary>
    public static string NormLsxInput(string? v)
    {
        var t = (v ?? "").Trim(); if (t.Length == 0) return "";
        var codes = LsxRe.Matches(t).Select(m => Regex.Replace(m.Value, @"\s+", "").ToUpperInvariant()).Distinct().ToList();
        if (codes.Count == 0) return Regex.Replace(t, @"^.*\bLSX\s*[:\-]?\s*", "", RegexOptions.IgnoreCase).Trim() is { Length: > 0 } x ? WithLsx(x) : t;
        return "LSX " + string.Join(" + ", codes);
    }

    // ------------------------------------------------------------------ việc cần xử lý

    public void ViecAdd(string id, string t)
    {
        t = (t ?? "").Trim(); if (t.Length == 0) return; if (t.Length > 120) t = t[..120];
        Mutate(l => { if (l.FirstOrDefault(x => x.Id == id) is { } r) ViecPush(r, t); });
    }
    public void ViecSet(string id, string viecId, Action<Viec> fn)
        => Mutate(l => { if (l.FirstOrDefault(x => x.Id == id) is { } r) { var a = ViecOf(r); if (a.FirstOrDefault(v => v.I == viecId) is { } v) { fn(v); SetViec(r, a); } } });
    public void ViecTick(string id, string viecId, bool done) => ViecSet(id, viecId, v => { v.D = done ? 1 : 0; v.Dt = done ? Iso(Now()) : ""; });
    public void ViecDelete(string id, string viecId)
        => Mutate(l => { if (l.FirstOrDefault(x => x.Id == id) is { } r) { var a = ViecOf(r); a.RemoveAll(v => v.I == viecId); SetViec(r, a); } });
    /// <summary>Việc hay dùng (để gõ nhanh).</summary>
    public List<string> ViecSugs() => Rows.SelectMany(ViecOf).GroupBy(v => v.T.Trim()).OrderByDescending(g => g.Count()).Take(6).Select(g => g.Key).ToList();

    /// <summary>Việc cho LSX: đã có lịch (chưa xuất, ngày chưa qua) → gắn vào lịch; chưa có → chờ, tự chuyển vào khi thêm lịch đúng LSX.</summary>
    public string PendAdd(string lsx, string t)
    {
        RequireEdit();
        lsx = (lsx ?? "").Trim(); t = (t ?? "").Trim();
        if (Sq(lsx).Length == 0) throw new BusinessException("Nhập số LSX.");
        if (t.Length == 0) throw new BusinessException("Nhập việc cần xử lý.");
        if (PickRow(Rows, lsx, Now()) is { } tgt)
        {
            ViecAdd(tgt.Id, t);
            return $"{Label(tgt.Lsx)} đã có lịch ngày {Vn(tgt.Ngay)} — đã thêm việc vào lịch đó.";
        }
        lock (_lock)
        {
            var cur = ReadPend();
            if (!cur.Any(p => Sq(p.Lsx) == Sq(lsx) && string.Equals(p.T, t, StringComparison.OrdinalIgnoreCase)))
                cur.Add(new Pend("p" + B36(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) + Guid.NewGuid().ToString("N")[..3], lsx, t, Iso(Now())));
            _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetPend] = PendSheet(cur) });
            Pending = cur;
        }
        Changed?.Invoke();
        return "Đã ghi việc cho " + Label(lsx) + " (chưa có lịch) — khi thêm lịch đúng LSX này, việc tự chuyển vào lịch.";
    }
    public void PendDelete(string pid)
    {
        RequireEdit();
        lock (_lock)
        {
            var cur = ReadPend(); cur.RemoveAll(p => p.Id == pid);
            _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetPend] = PendSheet(cur) });
            Pending = cur;
        }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ dò Thành Phẩm Chính (Tồn kho An An › cột Hợp đồng)

    public sealed class LookupResult
    {
        public bool Found; public string Message = "";
        public string Khach = "", ThiTruong = "", MatHang = "", Size = "", ChiTiet = "", DataDate = "", NguonHd = "";
        public double TonKien, Kg; public int SoDong;
        public List<Dictionary<string, string>> Lines = new();
    }

    private AnAnData? _an; private DateTime _anAt;
    private AnAnData AnAn()
    {
        if (_an is null || (DateTime.Now - _anAt).TotalSeconds > 20) { _an = AnAnStore.Load(_src()); _anAt = DateTime.Now; }
        return _an;
    }
    public void ResetStockCache() => _an = null;

    private static readonly string[] LineCols = { "Mã hàng", "Tên hàng", "Size", "Đặc tính", "Hợp đồng", "Khách hàng", "Thị trường", "ĐVT", "Tồn cuối", "Trọng lượng", "Tồn cuối (NET)", "NSX", "Ngày SX" };
    private static string Sqs(string? v) => Regex.Replace(v ?? "", @"\s+", "").ToUpperInvariant();
    public static string LsxKeyOf(string? raw) => TroLy.TroLyChuY.LsxCmp(TroLy.TroLyChuY.LsxCodes(Regex.Replace(raw ?? "", @"^\s*LSX\s*[:\-]?\s*", "", RegexOptions.IgnoreCase)).FirstOrDefault() ?? Sqs(raw));

    /// <summary>Các dòng Thành Phẩm Chính có Hợp đồng = LSX (đúng chuỗi → cùng mã chuẩn hóa "26.9.028A" → chứa chuỗi).</summary>
    public static (List<Dictionary<string, string>> Lines, string Message) TpcLines(AnAnData d, string lsx)
    {
        if (!d.HasData) return (new(), "Tồn kho An An chưa có dữ liệu (chưa nạp Load Data).");
        var hd = d.Col("Hợp đồng"); if (hd < 0) return (new(), "Không thấy cột \"Hợp đồng\" trong Load Data.");
        var bare = Regex.Replace(lsx ?? "", @"^\s*LSX\s*[:\-]?\s*", "", RegexOptions.IgnoreCase).Trim();
        var baseRows = d.BaseRows("tpc");
        List<int> Find(string q)
        {
            var sq = Sqs(q); if (sq.Length == 0) return new();
            var h = baseRows.Where(i => Sqs(d.Rows[i][hd]) == sq).ToList();
            var qk = LsxKeyOf(q);
            if (h.Count == 0 && qk.Length > 0) h = baseRows.Where(i => LsxKeyOf(d.Rows[i][hd]) == qk).ToList();
            if (h.Count == 0) h = baseRows.Where(i => Sqs(d.Rows[i][hd]).Contains(sq)).ToList();
            return h;
        }
        var hits = Find(lsx ?? ""); if (hits.Count == 0 && bare.Length > 0) hits = Find(bare);
        if (hits.Count == 0)
        {
            var other = d.Rows.Count(r => Sqs(r[hd]) == Sqs(bare));
            return (new(), other > 0 ? $"Có {other} dòng Hợp đồng \"{bare}\" nhưng không thuộc bảng Thành Phẩm Chính." : $"Không thấy Hợp đồng \"{bare}\" trong bảng Thành Phẩm Chính.");
        }
        var cols = LineCols.Select(c => (c, i: c == "Tồn cuối (NET)" ? d.ColExact(c) : d.Col(c))).ToList();
        return (hits.Select(i => cols.ToDictionary(x => x.c, x => x.i >= 0 ? d.Rows[i][x.i].Trim() : "")).ToList(), "");
    }

    public LookupResult Lookup(string lsx)
    {
        var res = new LookupResult();
        AnAnData d;
        try { d = AnAn(); } catch (Exception e) { res.Message = "Không đọc được Tồn kho An An: " + e.Message; return res; }
        var (lines, msg) = TpcLines(d, lsx);
        if (lines.Count == 0) { res.Message = msg; return res; }
        static double KgOf(Dictionary<string, string> l) { var q = Num(l["Tồn cuối"]); var w = Num(l["Trọng lượng"]); return w > 0 ? q * w : Num(l["Tồn cuối (NET)"]); }
        string Uq(string c) => string.Join(" · ", lines.Select(l => l[c]).Where(x => x.Length > 0).Distinct());
        res.Found = true; res.Lines = lines;
        res.Khach = Uq("Khách hàng"); res.ThiTruong = Uq("Thị trường"); res.MatHang = Uq("Tên hàng"); res.Size = Uq("Size");
        res.TonKien = lines.Sum(l => Num(l["Tồn cuối"])); res.Kg = lines.Sum(KgOf); res.SoDong = lines.Count; res.DataDate = d.DataDate;
        res.NguonHd = string.Join(" | ", lines.Select(l => l["Hợp đồng"]).Distinct().Take(6)); if (res.NguonHd.Length > 300) res.NguonHd = res.NguonHd[..300];
        res.ChiTiet = new JsonArray(lines.Take(60).Select(l => (JsonNode)new JsonArray(l["Tên hàng"], l["Size"], l["Mã hàng"], Num(l["Tồn cuối"]), Math.Round(KgOf(l) * 100) / 100, l["NSX"].Length > 0 ? l["NSX"] : l["Ngày SX"])).ToArray()).ToJsonString(AnAnStore.Opt);
        return res;
    }

    private void Merge(Lich r, LookupResult p)
    {
        r.Khach = p.Khach; r.ThiTruong = p.ThiTruong; r.MatHang = p.MatHang; r.Size = p.Size; r.TonKien = p.TonKien; r.Kg = Math.Round(p.Kg * 1000) / 1000;
        r.ChiTiet = p.ChiTiet; r.DoLuc = Now().ToString("HH:mm:ss d/M/yyyy", CultureInfo.InvariantCulture); r.NguonHd = p.NguonHd; r.SoDong = p.SoDong; r.DataDate = p.DataDate;
    }

    /// <summary>Dò lại tồn cho các lịch (chưa xuất) — nạp Load Data mới thì số kiện / tấn trên lịch cập nhật theo.</summary>
    public int Rescan(IEnumerable<string>? ids = null)
    {
        ResetStockCache();
        var want = ids?.ToHashSet();
        var looks = Rows.Where(r => (want is null ? r.TrangThai != "ok" : want.Contains(r.Id))).Select(r => (r.Id, L: Lookup(r.LsxDo.Length > 0 ? r.LsxDo : r.Lsx))).Where(x => x.L.Found).ToList();
        if (looks.Count == 0) return 0;
        return Mutate(l => { var n = 0; foreach (var (id, lk) in looks) if (l.FirstOrDefault(x => x.Id == id) is { } r) { Merge(r, lk); n++; } return n; }, $"Dò lại tồn {looks.Count} lịch");
    }

    // ------------------------------------------------------------------ nhập nhanh

    public sealed record QuickResult(List<string> Added, List<string> Dup, List<string> NotFound, List<string> OtherDay)
    {
        /// <summary>1.90: lịch cũ của LSX đã chuyển sang ngày mới (thay vì thêm lịch trùng).</summary>
        public List<string> Moved { get; init; } = new();
    }

    /// <summary>Dòng nhập nhanh mà LSX đã có lịch chưa xuất ở ngày khác (hộp Nhập nhanh hỏi trước khi thêm).</summary>
    public List<(QuickRow Row, List<Lich> Other)> QuickConflicts(IEnumerable<QuickRow> list)
    {
        var cur = ReadAll().Where(Keep).ToList();
        return list.Where(q => IsIso(q.Ngay) && SameDayDup(cur, NormLsxInput(q.Lsx), q.Ngay) is null)
            .Select(q => (q, OpenOther(cur, NormLsxInput(q.Lsx), q.Ngay))).Where(x => x.Item2.Count > 0).ToList();
    }

    /// <summary>
    /// Nhập nhanh. <paramref name="moveOther"/> (1.90): LSX đã có ĐÚNG 1 lịch chưa xuất ở ngày khác → chuyển lịch đó sang ngày mới thay vì thêm lịch trùng
    /// (nhiều lịch → vẫn thêm và báo kiểm tra).
    /// </summary>
    public QuickResult AddQuick(IEnumerable<QuickRow> list, bool moveOther = false)
    {
        RequireEdit();
        var baseMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var want = list.Where(q => IsIso(q.Ngay) && Sq(q.Lsx).Length > 0).GroupBy(q => q.Ngay + "|" + Sq(q.Lsx)).Select(g => g.First()).ToList();
        var fresh = new List<Lich>(); var notFound = new List<string>(); var i = 0;
        foreach (var w in want)
        {
            var r = new Lich { Id = NewId(i++), Ngay = w.Ngay, TrangThai = w.TrangThai, Lsx = NormLsxInput(w.Lsx), Cont = w.Grp.Length > 0 ? "q" + B36(baseMs) + w.Grp : "" };
            var lk = Lookup(r.Lsx); if (lk.Found) Merge(r, lk); else notFound.Add($"{Vn(r.Ngay)} · {r.Lsx}: {lk.Message}");
            fresh.Add(r);
        }
        var dup = new List<string>(); var other = new List<string>(); var added = new List<string>(); var moved = new List<string>();
        Mutate(l =>
        {
            var newIds = fresh.Select(x => x.Id).ToHashSet(); var used = new HashSet<string>();
            foreach (var r in fresh)
            {
                if (SameDayDup(l, r.Lsx, r.Ngay) is not null) { dup.Add(Vn(r.Ngay) + " · " + r.Lsx); continue; }
                if (moveOther && OpenOther(l.Where(Keep), r.Lsx, r.Ngay).Where(x => !newIds.Contains(x.Id) && !used.Contains(x.Id)).ToList() is { Count: 1 } one)
                {
                    var o1 = one[0]; var from = o1.Ngay; used.Add(o1.Id);
                    MoveRec(o1, r.Ngay, Now());
                    if (r.Cont.Length > 0) o1.Cont = r.Cont;
                    if (r.TrangThai != "plan") o1.TrangThai = r.TrangThai;
                    moved.Add($"{Label(o1.Lsx)}: {Vn(from)} → {Vn(r.Ngay)}");
                    continue;
                }
                var o = OpenOther(l.Where(Keep), r.Lsx, r.Ngay).Where(x => !newIds.Contains(x.Id)).Select(x => Vn(x.Ngay)).ToList();
                if (o.Count > 0) other.Add($"{r.Lsx} (thêm ngày {Vn(r.Ngay)}) đã có lịch ngày {string.Join(", ", o)} — kế hoạch đổi? Kiểm tra / chuyển lịch cũ.");
                l.Add(r); added.Add(Vn(r.Ngay) + " · " + r.Lsx + (r.Khach.Length > 0 ? " · " + r.Khach : ""));
            }
        }, $"Nhập nhanh {fresh.Count} lịch" + (moved.Count > 0 ? $" (chuyển {moved.Count} lịch cũ)" : ""));
        return new QuickResult(added, dup, notFound, other) { Moved = moved };
    }

    // ------------------------------------------------------------------ tạo List sản xuất / Phiếu xuất kho từ lịch

    /// <summary>
    /// Tạo List sản xuất từ TỒN KHO cho các LSX của lịch chưa có List (theo app cũ › buildFromStock): mỗi ngày sản xuất (dd/mm trong cột Hợp đồng "26.09.038-17/09")
    /// × mỗi size (cùng size khác mã hàng → cột riêng). Trả về thông báo.
    /// </summary>
    public List<string> CreateLists(IEnumerable<string> ids)
    {
        if (!CanPnx) throw new BusinessException("Tài khoản không có quyền sửa Phiếu xuất kho — không tạo được List.");
        var g = Group(ids); if (g.Count == 0) throw new BusinessException("Không còn lịch này.");
        var msgs = ListsFor(g, false, "tạo list nhanh");
        LoadPnx(); Changed?.Invoke();
        return msgs;
    }

    /// <summary>
    /// Tạo Phiếu xuất kho (xuất khẩu) cho nhóm LSX của lịch: điền LSX, ngày, khách, số cont; áp các List đã có (chưa có List thì tạo từ tồn kho trước).
    /// Đã có phiếu → không tạo lại, trả phiếu cũ.
    /// </summary>
    public (Phieu Phieu, bool Created, List<string> Notes) CreatePhieu(IEnumerable<string> ids) => CreatePhieu(ids, false);
    private (Phieu Phieu, bool Created, List<string> Notes) CreatePhieu(IEnumerable<string> ids, bool auto)
    {
        if (!CanPnx) throw new BusinessException("Tài khoản không có quyền sửa Phiếu xuất kho.");
        var g = Group(ids); if (g.Count == 0) throw new BusinessException("Không còn lịch này.");
        LoadPnx();
        if (PhieuOf(g) is { } old) return (old, false, new() { "Đã có phiếu " + (old.SoPhieu.Length > 0 ? old.SoPhieu : "(chưa có số)") + " cho LSX này." });
        var notes = CreateLists(g.Select(z => z.Id));
        var pnx = _pnx(); pnx.Load();
        var codes = g.SelectMany(z => Codes(z.Lsx)).Distinct().ToList();
        var p = pnx.NewPhieu();
        p.Ngay = g[0].Ngay; p.Lsx = PhieuKhoCore.LsxJoin(codes); p.Khach = g.Select(z => z.Khach.Split(" · ")[0].Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
        var cont = g.Select(z => z.ContSo).FirstOrDefault(x => x.Length > 0); if (cont is not null) p.Xe.Cont = cont;
        var first = true;
        foreach (var c in codes)
        {
            if (pnx.FindList(c) is not { } rec) { notes.Add($"LSX {c}: chưa có List — phiếu để trống phần hàng của LSX này."); continue; }
            var a = pnx.ApplyList(p, rec.Data, !first); first = false;
            notes.AddRange(a.Notes.Select(n => $"LSX {c}: {n}"));
        }
        PhieuKhoCore.RefreshNumbers(pnx.List, p);
        var tag = TagOf(g);
        PhieuKhoService.SetTag(p, tag);
        var saved = pnx.Save(p);
        try { pnx.LoadAutoLog(); foreach (var c in codes) pnx.LogAdd(c, "xuat_khau", tag, saved.Id, phieuNew: auto, ghiChu: auto ? "tự tạo theo lịch xuất" : "tạo phiếu nhanh"); pnx.SaveAutoLog(); } catch { /* nhật ký lỗi không chặn phiếu */ }
        // 1.122: thư «Thông tin xe» đến TRƯỚC khi có phiếu → điền luôn vào phiếu vừa tạo (trước chỉ chép số cont của lịch)
        if (CanPnx) notes.AddRange(ReplayXe(saved.Id));
        LoadPnx(); Changed?.Invoke();
        notes.Add("Đã tạo phiếu " + (saved.SoPhieu.Length > 0 ? saved.SoPhieu : "") + " — mở Phiếu xuất kho để kiểm tra rồi xuất Excel.");
        return (saved, true, notes);
    }

    // ---- List từ tồn kho (port phieu-nhap-xuat.js › hdParse / resolveYear / buildFromStock) ----

    public static (string Lsx, string Ddmm, string Ht)? HdParse(string? hd)
    {
        var t = hd ?? "";
        var m = Regex.Match(t, @"(\d{2}\.\d{1,2}\.\d{2,3}[A-Za-z]{0,2})\s*[-–—]\s*(\d{1,2})\s*[\/.]\s*(\d{1,2})([A-Za-z]{0,2})");
        if (m.Success) return (m.Groups[1].Value, m.Groups[2].Value.PadLeft(2, '0') + m.Groups[3].Value.PadLeft(2, '0'), m.Groups[4].Value.ToUpperInvariant());
        var n = Regex.Match(t, @"(?:^|[^A-Za-z0-9.])([A-Z][A-Z0-9]{1,11})\s*[-–—]\s*(\d{1,2})\s*[\/.]\s*(\d{1,2})([A-Za-z]{0,2})");
        return n.Success && n.Groups[1].Value is not ("OK" or "NA" or "TBD" or "XK" or "NK" or "LSX" or "CONT" or "HUY" or "HD")
            ? (n.Groups[1].Value, n.Groups[2].Value.PadLeft(2, '0') + n.Groups[3].Value.PadLeft(2, '0'), n.Groups[4].Value.ToUpperInvariant()) : null;
    }

    public static ListData ListFromStock(List<Dictionary<string, string>> lines, string lsx, DateTime refDt)
    {
        var want = Regex.Replace(lsx ?? "", @"\s+", "");
        var warn = new List<string>(); var cols = new List<(string Ck, string Base, bool NoSize, string Ma, string Ten)>();
        var perDate = new Dictionary<string, Dictionary<string, double>>(); var mksPer = new Dictionary<string, Dictionary<string, double>>(); var undated = new Dictionary<string, double>();
        var dvt = new HashSet<string>(); var khach = new List<string>();
        static string Loose(string s) => Regex.Replace(s.ToUpperInvariant(), "[^A-Z0-9]", "");
        static string ShortTen(string raw) { var s = Regex.Replace(raw ?? "", @"\s+", " ").Trim(); var i = Regex.Match(s, @"\s\(|,"); return (i.Success && i.Index > 0 ? s[..i.Index] : s).Trim(); }
        foreach (var l in lines)
        {
            var hp = HdParse(l.GetValueOrDefault("Hợp đồng"));
            if (hp is { } h && want.Length > 0 && TroLy.TroLyChuY.LsxCmp(h.Lsx) != TroLy.TroLyChuY.LsxCmp(Regex.Replace(want, "^LSX", "", RegexOptions.IgnoreCase))) continue;
            if (l.GetValueOrDefault("ĐVT") is { Length: > 0 } dv) dvt.Add(dv);
            var q = Num(l.GetValueOrDefault("Tồn cuối")); if (q == 0) continue;
            if (q < 0) { warn.Add($"Tồn kho có số ÂM ({q}) ở {l.GetValueOrDefault("Size")} — bỏ qua dòng này."); continue; }
            var size = l.GetValueOrDefault("Size") ?? ""; var ma = Regex.Replace(l.GetValueOrDefault("Mã hàng") ?? "", "^TPC-", "", RegexOptions.IgnoreCase).Trim();
            if (size.Length == 0 && ma.Length == 0) { warn.Add("Có dòng tồn kho không có Size lẫn Mã hàng — bỏ qua."); continue; }
            var ck = Loose(size) + "|" + ma.ToUpperInvariant();
            if (!cols.Any(c => c.Ck == ck)) cols.Add((ck, size.Length > 0 ? size : ma, size.Length == 0, ma, ShortTen(l.GetValueOrDefault("Tên hàng") ?? "")));
            if (l.GetValueOrDefault("Khách hàng") is { Length: > 0 } kh && !khach.Contains(kh)) khach.Add(kh);
            var mks = Regex.IsMatch(l.GetValueOrDefault("Đặc tính") ?? "", "MKS", RegexOptions.IgnoreCase);
            if (hp is { } h2 && DdmmOk(h2.Ddmm))
            {
                var k = h2.Ddmm + h2.Ht;
                (perDate.TryGetValue(k, out var o) ? o : perDate[k] = new())[ck] = (perDate[k].GetValueOrDefault(ck)) + q;
                if (mks) (mksPer.TryGetValue(k, out var mo) ? mo : mksPer[k] = new())[ck] = mksPer[k].GetValueOrDefault(ck) + q;
            }
            else undated[ck] = undated.GetValueOrDefault(ck) + q;
        }
        var cnt = cols.GroupBy(c => Loose(c.Base)).ToDictionary(g => g.Key, g => g.Count());
        string LabelOf((string Ck, string Base, bool NoSize, string Ma, string Ten) c) => cnt[Loose(c.Base)] > 1 && c.Ma.Length > 0 ? c.Base + " · " + c.Ma : c.Base;
        foreach (var (k, n) in cnt.Where(x => x.Value > 1)) warn.Add($"Size {cols.First(c => Loose(c.Base) == k).Base} có {n} mã hàng khác nhau — tách thành các cột riêng.");
        cols.Sort((a, b) => PhieuKhoCore.NaturalCmp(a.Base, b.Base) is var r && r != 0 ? r : string.CompareOrdinal(LabelOf(a), LabelOf(b)));
        var res = new ListData { Ok = false, Lsx = Regex.Replace(lsx ?? "", @"^\s*LSX\s*[:\-]?\s*", "", RegexOptions.IgnoreCase).Trim(), Khach = khach.FirstOrDefault() ?? "", Warnings = warn };
        res.Sizes = cols.Select(c => new ListSize { Size = LabelOf(c), Ten = c.Ten, Ma = c.Ma, NoSize = c.NoSize ? true : null }).ToList();
        foreach (var (k, per) in perDate)
        {
            var dd = int.Parse(k[..2]); var mm = int.Parse(k[2..4]);
            var y = refDt.Year; var cand = new DateTime(y, mm, Math.Min(dd, DateTime.DaysInMonth(y, mm)));
            if ((cand - refDt.Date).TotalDays > 31) y--;
            if (dd > DateTime.DaysInMonth(y, mm)) { foreach (var (ck, q) in per) undated[ck] = undated.GetValueOrDefault(ck) + q; warn.Add($"Ngày {k[..2]}/{k[2..4]} không có thật — chuyển vào dòng chưa rõ ngày."); continue; }
            var qty = new Dictionary<string, double>(); foreach (var c in cols) if (per.GetValueOrDefault(c.Ck) > 0) qty[LabelOf(c)] = per[c.Ck];
            var row = new ListRow { Date = $"{dd:00}.{mm:00}.{y}", Iso = $"{y}-{mm:00}-{dd:00}", Qty = qty, Total = qty.Values.Sum() };
            if (mksPer.TryGetValue(k, out var mq))
            {
                var m2 = new Dictionary<string, double>(); foreach (var c in cols) if (mq.GetValueOrDefault(c.Ck) > 0 && qty.ContainsKey(LabelOf(c))) m2[LabelOf(c)] = Math.Min(mq[c.Ck], qty[LabelOf(c)]);
                if (m2.Count > 0) { row.Mks = true; row.MksQty = m2; }
            }
            res.Rows.Add(row);
        }
        res.Rows.Sort((a, b) => string.CompareOrdinal(a.Iso, b.Iso));
        foreach (var c in cols) if (undated.GetValueOrDefault(c.Ck) > 0) res.Rows.Add(new ListRow { Qty = new() { [LabelOf(c)] = undated[c.Ck] }, Total = undated[c.Ck] });
        if (undated.Count > 0) warn.Add("Có hàng tồn không ghi ngày sản xuất trong cột Hợp đồng — để ở dòng chưa rõ ngày, điền ngày trong phiếu.");
        var kien = dvt.Count > 0 && dvt.All(x => Regex.IsMatch(x, "ki[eệ]n", RegexOptions.IgnoreCase));
        if (dvt.Count > 0 && !kien) warn.Add($"ĐVT tồn kho là “{string.Join(", ", dvt)}” (không phải kiện) — kiểm tra đơn vị trước khi xuất.");
        res.DonVi = kien ? "kiện" : "";
        res.Ok = res.Rows.Count > 0;
        return res;
        static bool DdmmOk(string ddmm) { var d = int.Parse(ddmm[..2]); var m = int.Parse(ddmm[2..4]); return m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(2024, m); }
    }

    // ------------------------------------------------------------------ List sản xuất: trạng thái (port phieu-nhap-xuat.js › lsxTrangThai)

    public sealed record ListInfo(PhieuKhoService.ListRec Rec, double? Ton, int Phieu, int LichN, int LichOk, string Mau, string Nhan, bool CanhBao);

    /// <summary>Trạng thái từng List: tồn còn (kiện) trong Thành Phẩm Chính · số phiếu xuất kho · lịch xuất đã xuất hết chưa.</summary>
    public List<ListInfo> ListInfos()
    {
        AnAnData? d = null; try { d = AnAn(); } catch { }
        Dictionary<string, double>? ton = null;
        if (d is { HasData: true } && d.Col("Hợp đồng") is var hd && hd >= 0)
        {
            ton = new(); var tc = d.Col("Tồn cuối"); var dv = d.Col("ĐVT");
            foreach (var i in d.BaseRows("tpc"))
            {
                var k = LsxKeyOf(d.Rows[i][hd]); if (k.Length == 0) continue;
                var unit = dv >= 0 ? d.Rows[i][dv] : "";
                if (unit.Length == 0 || Regex.IsMatch(unit, "ki[eệ]n|th[uù]ng|carton|ctn", RegexOptions.IgnoreCase)) ton[k] = ton.GetValueOrDefault(k) + Num(tc >= 0 ? d.Rows[i][tc] : "");
            }
        }
        var o = new List<ListInfo>();
        foreach (var rec in Lists)
        {
            var k = LsxKeyOf(rec.Lsx);
            double? t = ton is null ? null : ton.GetValueOrDefault(k);
            var np = Phieus.Count(p => PhieuKhoCore.LsxList(p.Lsx).Any(c => LsxKeyOf(c) == k));
            var lc = Rows.Where(r => Keys(r.Lsx).Any(c => LsxKeyOf(c) == k)).ToList();
            var dx = lc.Count > 0 && lc.All(r => r.TrangThai == "ok");
            var (mau, nhan) = TrangThai(t, np, dx);
            o.Add(new ListInfo(rec, t, np, lc.Count, lc.Count(r => r.TrangThai == "ok"), mau, nhan, mau is "bad" or "warn"));
        }
        return o;
    }

    public static (string Mau, string Nhan) TrangThai(double? ton, int np, bool dx)
    {
        string F(double n) => n.ToString("#,0.###", CultureInfo.InvariantCulture);
        if (ton is > 0 && dx) return ("bad", $"Đã xuất nhưng tồn vẫn còn {F(ton.Value)} kiện");
        if (ton is { } t && t <= 0)
            return np > 0 && dx ? ("ok", "Hoàn tất") : np > 0 ? ("warn", "Hết tồn, lịch chưa xác nhận đã xuất") : dx ? ("warn", "Hết tồn, chưa có phiếu xuất kho") : ("warn", "Hết tồn, chưa có phiếu / lịch xuất");
        if (ton is not null) return np > 0 ? ("wait", "Chờ xuất") : ("info", "Chưa có phiếu xuất kho");
        return ("info", dx ? (np > 0 ? "Đã xuất (chưa theo dõi tồn)" : "Đã xuất, chưa có phiếu") : (np > 0 ? "Đã có phiếu xuất kho" : "Chưa có phiếu xuất kho"));
    }

    /// <summary>
    /// QUÉT TỒN KHO cho List sản xuất: LSX còn tồn trong Thành Phẩm Chính mà CHƯA có List → tự tạo List từ tồn kho. Trả số List đã tạo + danh sách.
    /// </summary>
    public (int Created, List<string> Codes, string Message) ScanStockLists()
    {
        if (!CanPnx) throw new BusinessException("Tài khoản không có quyền sửa Phiếu xuất kho.");
        ResetStockCache(); var d = AnAn();
        if (!d.HasData) return (0, new(), "Tồn kho An An chưa có dữ liệu — bỏ qua lần quét này, giữ nguyên số liệu cũ.");
        var hd = d.Col("Hợp đồng"); if (hd < 0) return (0, new(), "Không thấy cột \"Hợp đồng\" trong Load Data.");
        var pnx = _pnx(); pnx.Load();
        var have = pnx.Lists.Select(l => LsxKeyOf(l.Lsx)).ToHashSet();
        var tc = d.Col("Tồn cuối");
        var codes = d.BaseRows("tpc").Select(i => (Raw: d.Rows[i][hd], Q: Num(tc >= 0 ? d.Rows[i][tc] : "")))
            .Select(x => (Code: TroLy.TroLyChuY.LsxCodes(x.Raw).FirstOrDefault() ?? "", x.Q)).Where(x => x.Code.Length > 0)
            .GroupBy(x => TroLy.TroLyChuY.LsxCmp(x.Code)).Where(g => g.Sum(x => x.Q) > 0 && !have.Contains(g.Key)).Select(g => g.First().Code).ToList();
        var made = new List<string>();
        foreach (var c in codes)
        {
            var (lines, _) = TpcLines(d, c); var ld = ListFromStock(lines, c, Now());
            if (!ld.Ok) continue;
            pnx.SaveListData(ld, "(tồn kho)"); made.Add(c);
        }
        LoadPnx(); Changed?.Invoke();
        return (made.Count, made, made.Count > 0 ? $"Đã tạo {made.Count} List từ tồn kho: {string.Join(", ", made.Take(12))}{(made.Count > 12 ? " …" : "")}." : "Mọi LSX còn tồn đều đã có List.");
    }

    // ------------------------------------------------------------------ EMAIL → LỊCH

    public MailConfig MailCfg() => MailConfig.Load(_settings);
    public void SaveMailCfg(MailConfig c, string? newPass)
    {
        if (!CanMail) throw new BusinessException("Chỉ tài khoản Quản trị mới đổi được tài khoản Gmail.");
        if (newPass is { Length: > 0 }) c.PassEnc = Secret.Protect(Regex.Replace(newPass, @"\s+", ""));
        c.Save(_settings);
        _audit.Log(_session.Username, "Lịch xuất", "Đổi cài đặt email: " + c.Email);
    }

    /// <summary>Người gửi tin cậy (sheet LX_TuDong, dùng chung app cũ): thư của họ được tự xử lý; OnlyTrusted = chỉ hiện thư của họ.</summary>
    public (string Trust, bool OnlyTrusted) TrustCfg()
    {
        var a = Read(SheetTrust).OfType<JsonObject>().FirstOrDefault();
        return (S(a?["nguoi"]), S(a?["chiTin"]) != "0");
    }
    public void SaveTrust(string trust, bool only)
    {
        if (!CanMail) throw new BusinessException("Mục Email chỉ dành cho tài khoản Quản trị.");
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetTrust] = new JsonArray(new JsonObject { ["nguoi"] = trust.Trim(), ["chiTin"] = only ? "1" : "0" }) });
    }

    private static readonly JsonSerializerOptions EmJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    /// <summary>Thư đã nhận diện (chờ duyệt + đã xử lý) — sheet LX_EmailXuLy.</summary>
    public List<LichXuatEmail.Row> EmailRows()
    {
        var o = new List<LichXuatEmail.Row>();
        foreach (var r in Read(SheetEmail).OfType<JsonObject>())
        {
            try { if (JsonSerializer.Deserialize<LichXuatEmail.Row>(r.ToJsonString(), EmJson) is { Id.Length: > 0 } x) o.Add(x); } catch { }
        }
        return o;
    }
    private void SaveEmailRows(List<LichXuatEmail.Row> rows)
    {
        var now = Now();
        var keep = rows.Where(r => r.TrangThai.Length == 0 || (r.XuLyLuc ?? r.NhanLuc) > now.AddDays(-60)).OrderByDescending(r => r.NhanLuc).Take(800).ToList();
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetEmail] = new JsonArray(keep.Select(r => JsonNode.Parse(JsonSerializer.Serialize(r, EmJson))).ToArray()) });
    }

    public sealed record ScanResult(int New, int Auto, int ContSet, int XeFilled, int Waiting, List<string> Notes)
    {
        /// <summary>Số thư nhận diện được (trước khi bỏ thư đã có) — 1.77.</summary>
        public int Recognized { get; init; }
    }

    /// <summary>
    /// Quét hộp thư: thư mới → đề xuất chờ duyệt. Thư «Thông tin xe» (không cần duyệt): ghi số cont vào lịch cùng LSX + điền thông tin xe vào phiếu xuất kho
    /// đã có (phiếu đã có thông tin xe thì không đè). Thư của người tin cậy, qua lưới an toàn (Guard) → tự áp vào lịch (hoàn tác được).
    /// Thư xe của người tin cậy nhận HÔM NAY mà lịch hôm nay chưa có LSX → tự tạo lịch hôm nay.
    /// </summary>
    public async Task<ScanResult> ScanEmailAsync(CancellationToken ct = default, Action<string>? progress = null)
    {
        if (!CanMail) throw new BusinessException("Mục Email chỉ dành cho tài khoản Quản trị.");
        // 1.77: giới hạn 3 phút — trước đây hộp thư lớn khiến nút quét đứng ở "Đang đọc hộp thư…" không báo gì
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ScanTimeout);
        try { return await ScanCoreAsync(cts.Token, progress); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw SetMailStatus(new BusinessException($"Quá {ScanTimeout.TotalMinutes:0} phút chưa đọc xong hộp thư — điền ô «Chỉ nhận thư từ» (lọc ngay trên Gmail) hoặc giảm số ngày đọc, rồi quét lại.")); }
    }
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Trạng thái kết nối Gmail gần nhất (✓ / ✖ + giờ) — hiện ở đầu hộp Email (settings lx_mail_status).</summary>
    public string MailStatus => _settings.Get("lx_mail_status");
    private T SetMailStatus<T>(T e) where T : Exception { _settings.Set("lx_mail_status", $"✖ {Now():HH:mm dd/MM} · {e.Message}"); return e; }

    /// <summary>Nút "Kiểm tra kết nối": đăng nhập Gmail, mở thư mục, đếm thư — không tải nội dung, không ghi lịch.</summary>
    public async Task<string> CheckMailAsync(CancellationToken ct = default, Action<string>? progress = null)
    {
        if (!CanMail) throw new BusinessException("Mục Email chỉ dành cho tài khoản Quản trị.");
        if (Mail is not ImapMailSource imap) return "✓ (nguồn thư thử nghiệm)";
        imap.Progress = progress;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromMinutes(1));
        try
        {
            var msg = await imap.CheckAsync(MailCfg(), cts.Token);
            _settings.Set("lx_mail_status", $"{msg.Split(" · ")[0]} lúc {Now():HH:mm dd/MM}");
            return msg;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw SetMailStatus(new BusinessException("Quá 1 phút chưa kết nối được Gmail.")); }
        catch (BusinessException e) { throw SetMailStatus(e); }
        finally { imap.Progress = null; }
    }

    /// <summary>Quét thật (dùng chung cho quét tay của Quản trị và quét nền <see cref="StartAutoEmail"/>).</summary>
    private async Task<ScanResult> ScanCoreAsync(CancellationToken ct, Action<string>? progress = null)
    {
        if (!CanAutoMail) throw new BusinessException("Tài khoản không có quyền sửa Lịch xuất.");
        var cfg = MailCfg();
        List<LichXuatEmail.Msg> msgs;
        if (Mail is ImapMailSource imap) imap.Progress = progress;
        try { msgs = await Mail.FetchAsync(cfg, ct); }
        catch (BusinessException e) { throw SetMailStatus(e); }
        finally { if (Mail is ImapMailSource im) im.Progress = null; }
        var r = ProcessMessages(msgs, cfg);
        if (Mail is ImapMailSource st)
        {
            // số liệu từng bước — để biết thư bị loại ở đâu khi "quét không thấy thư"
            var (folder, found, down) = st.LastStats;
            r.Notes.Insert(0, $"Đọc «{folder}»: {found} thư trong {Math.Max(cfg.Days, 1)} ngày (+30 ngày lấy thư gốc)" + (cfg.AllowList.Count > 0 ? " từ người ở ô «Chỉ nhận thư từ»" : "") +
                $" · {down} thư thuộc chuỗi có LSX trên tiêu đề (dạng 26.11.020) · {r.Recognized} thư nhận diện được (tạo / đổi / hủy lịch / thông tin xe)." +
                (found > 0 && down == 0 ? " → Không thư nào có số LSX trên tiêu đề, nên không có gì để cập nhật lịch." : "") +
                (down > 0 && r.Recognized == 0 ? " → Có chuỗi thư có LSX nhưng tiêu đề thiếu từ khóa (xem ô «Từ khóa») hoặc nội dung không nói về kéo đóng / xuất hàng." : ""));
            _settings.Set("lx_mail_status", $"✓ Đã kết nối {cfg.Email} lúc {Now():HH:mm dd/MM} · {found} thư");
        }
        return r;
    }

    /// <summary>Xử lý thư đã đọc (tách khỏi IMAP để test).</summary>
    public ScanResult ProcessMessages(List<LichXuatEmail.Msg> msgs, MailConfig cfg)
    {
        var now = Now();
        // 1.90: quét nền chạy lâu — lịch trong bộ nhớ có thể cũ (sửa tay ở máy khác / điện thoại) → đọc lại trước khi khớp LSX
        lock (_lock) Rows = ReadAll().Where(Keep).ToList();
        var built = LichXuatEmail.BuildRows(msgs, cfg.KeywordList, cfg.AllowList, cfg.Days, now);
        var store = EmailRows(); var known = store.Select(r => r.Id).ToHashSet();
        var fresh = built.Where(r => !known.Contains(r.Id)).ToList();
        var notes = new List<string>(); int auto = 0, contN = 0, xeN = 0;
        var (trust, _) = TrustCfg();

        // 1) Thông tin xe → số cont + phiếu
        foreach (var x in fresh.Where(r => r.HanhDong == "xe"))
        {
            var rd = ReadXeMail(x.GhiChu); var codes = LichXuatEmail.CodesOf(x.Lsx);
            if (rd.Ok && codes.Count > 0)
            {
                if (trust.Length > 0 && LichXuatEmail.Trusted(x.NguoiGui, trust) && x.NhanLuc.Date == now.Date && CanEdit)
                {
                    var miss = codes.Where(c => !Rows.Any(r => SharesLsx(r, new[] { Sq(c) }))).ToList();
                    if (miss.Count > 0)
                    {
                        AddQuick(miss.Select(c => new QuickRow(Iso(now), WithLsx(c), "run", miss.Count > 1 ? "M" : "")));
                        notes.Add($"Thư «Thông tin xe» của người tin cậy: lịch hôm nay chưa có {string.Join(" + ", miss)} — đã tự tạo lịch hôm nay.");
                    }
                }
                if (CanEdit) contN += Mutate(l =>
                {
                    var n = 0;
                    foreach (var r in l.Where(r => !r.IsNhap && Keys(r.Lsx).Any(k => codes.Any(c => Sq(c) == k))))
                        if (r.ContSo != rd.Cont) { r.ContSo = rd.Cont; n++; }
                    return n;
                });
            }
            // 1.122 (như app cũ autoXeNow): thư xe MỚI → điền vào phiếu xuất khẩu mới nhất của LSX, thư sau cùng thắng (chỉ ô thư có ghi).
            // Chưa có phiếu → vẫn ghi nhận thư; khi phiếu được tạo (tự / tay) sẽ tự áp lại (ReplayXe).
            var fx = CanPnx ? XeToPhieu(codes, x.GhiChu, null) : (Status: "no_pnx", SoPhieu: "");
            if (fx.Status == "ok") xeN++;
            x.TrangThai = "Đã duyệt"; x.XuLyLuc = now; x.TuDong = true;
            x.KetQua = (rd.Ok ? $"Số cont {rd.Cont} → lịch {x.Lsx}" : "Thư xe không có số cont") + fx.Status switch
            {
                "ok" => $" · đã điền thông tin xe vào phiếu {fx.SoPhieu}",
                "same" => $" · phiếu {fx.SoPhieu} đã có đúng thông tin này",
                "no_phieu" => " · chưa có phiếu xuất khẩu — tự điền khi có phiếu",
                _ => "",
            };
        }
        // 1.122: thư người tin cậy CÒN CHỜ (lượt trước chưa qua lưới an toàn, vd LSX chưa có lịch) cũng thử lại mỗi lượt quét như app cũ (emAuto chạy trên mọi thư chờ)
        var cutoffAuto = now.AddDays(-Math.Max(cfg.Days, 1));
        var pendTrusted = store.Where(r => r.TrangThai.Length == 0 && r.NhanLuc >= cutoffAuto).ToList();
        // 2) Thư của người tin cậy qua lưới an toàn → tự áp
        if (cfg.Auto && trust.Length > 0 && CanEdit)
            foreach (var r in fresh.Concat(pendTrusted).Where(r => r.HanhDong is "tao" or "doi" or "huy" && LichXuatEmail.Trusted(r.NguoiGui, trust)).OrderBy(r => r.NhanLuc))
            {
                if (LichXuatEmail.Guard(r, Rows, now).Length > 0) continue;
                try { var msg = ApplyEmail(r, r.Ngay, r.Gio); r.TrangThai = "Đã duyệt"; r.XuLyLuc = now; r.TuDong = true; r.KetQua = "[Tự động · người tin cậy] " + msg; auto++; notes.Add($"{r.HanhDongTen} · {r.Lsx}: {msg}"); }
                catch (Exception e) { r.KetQua = "Chưa tự xử lý được: " + e.Message; break; }
            }
        // 3) thư xe đã nhận trước khi có phiếu → áp vào phiếu xuất khẩu chưa có thông tin xe (phiếu tạo tay / tạo lại sau khi thư đến)
        if (CanPnx) foreach (var n in ReplayXe(null, store.Concat(fresh))) { notes.Add(n); xeN++; }
        store.AddRange(fresh);
        SaveEmailRows(store);
        _settings.Set("lx_mail_last", now.ToString("o", CultureInfo.InvariantCulture));
        var waiting = store.Count(r => r.TrangThai.Length == 0);
        return new ScanResult(fresh.Count, auto, contN, xeN, waiting, notes) { Recognized = built.Count };
    }

    public DateTime? LastScan => DateTime.TryParse(_settings.Get("lx_mail_last"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    /// <summary>5 ô thông tin xe (không tính số cont — phiếu tạo từ lịch đã có cont của lịch) còn trống hết → phiếu chưa nhận thông tin xe.</summary>
    private static bool XeTrong(Phieu p) => new[] { p.Xe.TaiXe, p.Xe.Sdt, p.Xe.SoXe, p.Xe.Seal, p.Xe.Booking }.All(v => string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// Áp 1 thư «Thông tin xe» vào phiếu xuất khẩu của LSX (như app cũ autoXeNow): <paramref name="targetId"/> = đúng phiếu đó, không thì phiếu ngày muộn nhất chứa LSX.
    /// Chỉ ghi các ô thư có ghi (thư sau thắng). Status: ok · same (không có gì mới) · empty (thư không đọc được mục nào) · no_phieu · error.
    /// </summary>
    private (string Status, string SoPhieu) XeToPhieu(List<string> codes, string text, string? targetId)
    {
        try
        {
            var pnx = _pnx(); pnx.Load(); var want = codes.Select(PhieuKhoCore.LsxNorm).ToHashSet();
            bool Mine(Phieu p) => p.Loai == "xuat_khau" && PhieuKhoCore.LsxList(p.Lsx).Any(c => want.Contains(PhieuKhoCore.LsxNorm(c)));
            var h = targetId is not null ? pnx.List.FirstOrDefault(p => p.Id == targetId && Mine(p)) : null;
            h ??= pnx.List.Where(Mine).OrderByDescending(p => p.Ngay, StringComparer.Ordinal).ThenByDescending(p => p.CapNhat, StringComparer.Ordinal).FirstOrDefault();
            if (h is null) return ("no_phieu", "");
            var c = PhieuKhoCore.Clone(h); var r = PhieuKhoCore.ApplyXeEmail(c, text);
            if (r.Found.Count == 0) return ("empty", h.SoPhieu);
            if (r.Xe.All(kv => h.Xe.Get(kv.Key) == kv.Value)) return ("same", h.SoPhieu);
            pnx.Save(c, tay: false); LoadPnx();
            return ("ok", h.SoPhieu);
        }
        catch { return ("error", ""); }
    }

    /// <summary>
    /// 1.122 (như app cũ emXeReplay): thư «Thông tin xe» đã nhận (14 ngày) mà lúc đó chưa có phiếu → áp vào phiếu xuất khẩu chưa có thông tin xe của đúng LSX,
    /// theo thứ tự cũ → mới (thư mới nhất thắng). <paramref name="targetId"/> = chỉ phiếu đó (vừa tạo). Trả ghi chú từng phiếu đã điền.
    /// </summary>
    private List<string> ReplayXe(string? targetId, IEnumerable<LichXuatEmail.Row>? rows = null)
    {
        var notes = new List<string>();
        try
        {
            var cut = Now().AddDays(-14);
            var xe = (rows ?? EmailRows()).Where(r => r.HanhDong == "xe" && r.TrangThai == "Đã duyệt" && r.NhanLuc >= cut).GroupBy(r => r.Id).Select(g => g.First()).OrderBy(r => r.NhanLuc).ToList();
            if (xe.Count == 0) return notes;
            var pnx = _pnx(); pnx.Load();
            var targets = pnx.List.Where(p => p.Loai == "xuat_khau" && (targetId is null ? XeTrong(p) : p.Id == targetId)).ToList();
            foreach (var p in targets)
            {
                var mine = PhieuKhoCore.LsxList(p.Lsx).Select(PhieuKhoCore.LsxNorm).ToHashSet();
                // quét định kỳ (không chỉ định phiếu): chỉ thư nhận quanh ngày xuất của phiếu (± 3 ngày) — tránh điền xe của lần xuất này vào phiếu cũ cùng LSX
                bool Near(LichXuatEmail.Row x) => targetId is not null || (IsIso(p.Ngay) && Math.Abs((x.NhanLuc.Date - DateOf(p.Ngay)).TotalDays) <= 3);
                var its = xe.Where(x => Near(x) && LichXuatEmail.CodesOf(x.Lsx).Any(c => mine.Contains(PhieuKhoCore.LsxNorm(c)))).ToList();
                if (its.Count == 0) continue;
                var c = PhieuKhoCore.Clone(p); var n = 0;
                foreach (var x in its) n += PhieuKhoCore.ApplyXeEmail(c, x.GhiChu).Found.Count;
                if (n == 0 || new[] { "taiXe", "sdt", "soXe", "cont", "seal", "booking", "nhietDo" }.All(k => p.Xe.Get(k) == c.Xe.Get(k))) continue;
                pnx.Save(c, tay: false);
                notes.Add($"Đã điền thông tin xe vào phiếu {p.SoPhieu} ({p.Lsx}) từ email «Thông tin xe» đã nhận trước đó.");
            }
            if (notes.Count > 0) LoadPnx();
        }
        catch { /* không chặn quét / tạo phiếu */ }
        return notes;
    }

    /// <summary>Lựa chọn khi thư không chắc (LSX đã có lịch ngày khác / nhiều lịch): <see cref="ChoiceKeep"/> = thêm lịch mới, giữ cả hai;
    /// mã 1 lịch = chuyển lịch đó sang ngày thư.</summary>
    public const string ChoiceKeep = "keep";

    /// <summary>Kế hoạch áp thư (đọc bản lịch mới nhất) — hộp duyệt dùng để hỏi khi có mục <see cref="LichXuatEmail.PlanKind.Choose"/>.</summary>
    public List<LichXuatEmail.PlanItem> EmailPlan(LichXuatEmail.Row r, string ngay) => LichXuatEmail.Plan(r, ngay, ReadAll().Where(Keep));

    /// <summary>
    /// Áp 1 thư vào lịch, KHỚP THEO SỐ LSX (1.90 — lịch đã sửa tay ngày / giờ vẫn được nhận ra, không tạo trùng):
    /// tạo (LSX chưa có lịch) · đã có lịch đúng ngày → chỉ cập nhật giờ · đổi → chuyển lịch chưa xuất duy nhất của LSX (giữ giờ, ghi chú, việc, số cont sửa tay) ·
    /// không chắc (<see cref="LichXuatEmail.PlanKind.Choose"/>) → cần <paramref name="choice"/>: <see cref="ChoiceKeep"/> hoặc mã lịch muốn chuyển · hủy (xóa lịch ngày đó).
    /// </summary>
    public string ApplyEmail(LichXuatEmail.Row r, string ngay, string gio, string? choice = null)
    {
        var codes = LichXuatEmail.CodesOf(r.Lsx);
        if (codes.Count == 0) throw new BusinessException("Không đọc được LSX từ tiêu đề thư.");
        if (!IsIso(ngay)) throw new BusinessException("Chọn ngày trước khi duyệt.");
        gio = NormGio(gio);
        var keys = codes.Select(Sq).ToList();
        bool Mine(Lich x) => SharesLsx(x, keys);
        r.Undo = JsonSerializer.Serialize(ReadAll().Where(Mine).Select(x => x.ToJson().ToJsonString()).ToList());
        if (r.HanhDong == "huy")
        {
            var other = new List<string>(); var ghep = new List<string>();
            var del = Mutate(l =>
            {
                // chỉ xóa lịch mà MỌI số LSX trong ô đều thuộc thư hủy — lịch cont ghép với LSX khác giữ lại, báo kiểm tay
                var hit = l.Where(x => Mine(x) && x.Ngay == ngay).ToList();
                foreach (var x in hit.Where(x => !Keys(x.Lsx).All(keys.Contains))) ghep.Add(Label(x.Lsx));
                var n = l.RemoveAll(x => hit.Contains(x) && Keys(x.Lsx).All(keys.Contains));
                other.AddRange(l.Where(x => Mine(x) && x.Ngay != ngay).Select(x => Vn(x.Ngay)));
                return n;
            }, $"Email: hủy {r.Lsx} ngày {Vn(ngay)}");
            var msg = del > 0 ? $"Đã hủy {del} lịch ngày {Vn(ngay)}" : other.Count > 0 ? $"Không có lịch ngày {Vn(ngay)} (LSX đang ở ngày {string.Join(", ", other.Distinct())}) — giữ nguyên" : ghep.Count == 0 ? "Không có lịch nào để hủy" : "";
            if (ghep.Count > 0) msg += (msg.Length > 0 ? " · " : "") + $"{string.Join(", ", ghep)} ngày {Vn(ngay)} đang chung cont với LSX khác — giữ nguyên, kiểm tra tay";
            return msg;
        }
        // dựng sẵn lịch mới (dò Thành Phẩm Chính) cho mọi số LSX — chỉ dùng cho số nào cần thêm
        var baseMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var i = 0;
        var st = string.CompareOrdinal(ngay, Iso(Now())) < 0 ? "ok" : "plan";
        var made = codes.ToDictionary(c => c, c =>
        {
            var x = new Lich { Id = NewId(i++), Ngay = ngay, Gio = gio, TrangThai = st, Lsx = WithLsx(c) };
            var lk = Lookup(x.Lsx); if (lk.Found) Merge(x, lk);
            return x;
        });
        var added = new List<string>(); var moved = new List<string>(); var same = new List<string>();
        Mutate(l =>
        {
            var plan = LichXuatEmail.Plan(r, ngay, l.Where(Keep));
            if (choice is null && plan.FirstOrDefault(p => p.Kind == LichXuatEmail.PlanKind.Choose) is { } q)
                throw new BusinessException($"LSX {q.Code} đã có lịch ngày {string.Join(", ", q.Cands.Select(x => Vn(x.Ngay)))} — chọn chuyển lịch cũ hay giữ cả hai.");
            var fresh = new List<Lich>(); var done = new HashSet<string>();
            foreach (var p in plan)
            {
                var kind = p.Kind; var tgt = p.Target;
                if (kind == LichXuatEmail.PlanKind.Choose)
                {
                    if (choice == ChoiceKeep) kind = LichXuatEmail.PlanKind.Add;
                    else { tgt = p.Cands.FirstOrDefault(x => x.Id == choice) ?? l.FirstOrDefault(x => x.Id == choice && Mine(x)) ?? throw new BusinessException("Lịch đã chọn không còn (máy khác vừa sửa?) — mở lại thư."); kind = LichXuatEmail.PlanKind.Move; }
                }
                switch (kind)
                {
                    case LichXuatEmail.PlanKind.SameDay: same.Add(p.Code); break;
                    case LichXuatEmail.PlanKind.Add: fresh.Add(made[p.Code]); added.Add(p.Code); break;
                    case LichXuatEmail.PlanKind.Move when tgt is not null:
                        if (!done.Add(tgt.Id)) { same.Add(p.Code); break; }   // cont ghép: 1 lịch chứa nhiều số LSX của thư → chuyển 1 lần
                        var from = tgt.Ngay;
                        if (SameDayDup(l, tgt.Lsx, ngay, tgt.Id) is not null) { moved.Add($"{p.Code}: đã có lịch ngày {Vn(ngay)}, giữ lịch ngày {Vn(from)}"); break; }
                        MoveRec(tgt, ngay, Now()); moved.Add($"{p.Code}: {Vn(from)} → {Vn(ngay)}");
                        break;
                }
            }
            if (fresh.Count > 1) { var g = "q" + B36(baseMs) + "M"; foreach (var x in fresh) x.Cont = g; }
            l.AddRange(fresh);
            if (gio.Length > 0) foreach (var x in l.Where(x => x.Ngay == ngay && Mine(x))) x.Gio = gio;
        }, $"Email: {r.HanhDongTen} {r.Lsx} → {Vn(ngay)}");
        return string.Join(" · ", new[]
        {
            added.Count > 0 ? $"Đã thêm {string.Join(", ", added)} ngày {Vn(ngay)}" + (choice == ChoiceKeep ? " (giữ cả lịch cũ)" : "") : "",
            string.Join("; ", moved),
            same.Count > 0 ? $"{string.Join(", ", same)}: giữ ngày {Vn(ngay)}" + (gio.Length > 0 ? ", giờ " + GioVn(gio) : "") + " — không thêm trùng" : "",
        }.Where(x => x.Length > 0));
    }

    /// <summary>Duyệt (áp vào lịch) hoặc bỏ qua 1 thư chờ duyệt. <paramref name="choice"/>: xem <see cref="ApplyEmail"/>.</summary>
    public string Decide(string id, bool approve, string? ngay = null, string? gio = null, string? choice = null)
    {
        if (!CanMail) throw new BusinessException("Mục Email chỉ dành cho tài khoản Quản trị.");
        var store = EmailRows(); var r = store.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không còn thư này.");
        if (r.TrangThai.Length > 0) throw new BusinessException("Thư này đã xử lý (" + r.TrangThai + ").");
        string msg;
        if (approve) { msg = ApplyEmail(r, ngay ?? r.Ngay, gio ?? r.Gio, choice); r.Ngay = ngay ?? r.Ngay; r.Gio = gio ?? r.Gio; r.TrangThai = "Đã duyệt"; }
        else { msg = "Đã bỏ qua thư " + r.Lsx; r.TrangThai = "Bỏ qua"; }
        r.XuLyLuc = Now(); r.KetQua = msg;
        SaveEmailRows(store);
        return msg;
    }

    /// <summary>Hoàn tác 1 thư đã duyệt: bỏ lịch do lần duyệt tạo mới, trả lịch đã đổi về như cũ, thêm lại lịch đã hủy; thư về "Chờ duyệt".</summary>
    public string Undo(string id)
    {
        if (!CanMail) throw new BusinessException("Mục Email chỉ dành cho tài khoản Quản trị.");
        var store = EmailRows(); var r = store.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không còn thư này.");
        var n = 0;
        if (r.TrangThai == "Đã duyệt" && r.HanhDong != "xe" && r.Undo.Length > 0)
        {
            var before = (JsonSerializer.Deserialize<List<string>>(r.Undo) ?? new()).Select(s => Lich.From(JsonNode.Parse(s)!.AsObject())).ToList();
            var ids = before.Select(b => b.Id).ToHashSet(); var keys = LichXuatEmail.CodesOf(r.Lsx).Select(Sq).ToList();
            n = Mutate(l =>
            {
                var k = l.RemoveAll(x => !ids.Contains(x.Id) && x.Ngay == r.Ngay && SharesLsx(x, keys));
                foreach (var b in before)
                {
                    var z = l.FirstOrDefault(x => x.Id == b.Id);
                    if (z is null) { l.Add(b); k++; }
                    else if (z.Ngay != b.Ngay || z.Gio != b.Gio || z.TrangThai != b.TrangThai) { z.Ngay = b.Ngay; z.Gio = b.Gio; z.TrangThai = b.TrangThai; z.Cont = b.Cont; k++; }
                }
                return k;
            }, "Email: hoàn tác " + r.Lsx);
        }
        r.TrangThai = ""; r.XuLyLuc = null; r.KetQua = ""; r.Undo = ""; r.TuDong = false;
        SaveEmailRows(store);
        return n > 0 ? $"Đã hoàn tác {n} lịch — thư về mục chờ duyệt." : "Thư đã về mục chờ duyệt.";
    }

    // ---- quét email tự động ----
    private CancellationTokenSource? _auto;
    public event Action<ScanResult>? EmailScanned;
    public event Action<string>? EmailError;
    public void StartAutoEmail()
    {
        StopAutoEmail();
        var cfg = MailCfg(); if (!cfg.Ready || !cfg.Auto || !CanAutoMail) return;
        var cts = _auto = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    try { var r = await ScanCoreAsync(cts.Token); EmailScanned?.Invoke(r); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { EmailError?.Invoke(e.Message); }
                    await Task.Delay(TimeSpan.FromMinutes(Math.Max(2, MailCfg().IntervalMin)), cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    public void StopAutoEmail() { _auto?.Cancel(); _auto = null; }
}

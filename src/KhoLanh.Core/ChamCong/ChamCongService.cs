using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;

namespace KhoLanh.Core.ChamCong;

/// <summary>
/// Dữ liệu Chấm công (db "cham-cong", đọc / ghi qua ISheetSource → LAN / Drive thấy chung):
///  • NhanVien — danh sách nhân viên (thứ tự = thứ tự trên bảng công).
///  • CauHinh — 1 ô JSON quy tắc tính giờ (<see cref="CcCauHinh"/>).
///  • Goc_yyyy-MM — dòng file máy chấm công của tháng (chỉ phòng ban đã chọn + nhân viên có trong danh sách).
///  • Sua_yyyy-MM — sửa tay theo ngày (giờ vào / ra, V.Sớm, giờ chốt, chép giờ người khác, ghi chú).
/// </summary>
public sealed class ChamCongService
{
    public const string Db = "cham-cong", PermKey = "m24cc";
    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService? _audit;

    public List<CcNhanVien> NhanVien { get; private set; } = new();
    public CcCauHinh CauHinh { get; private set; } = new();

    public ChamCongService(Func<ISheetSource> src, Session session, AuditService? audit)
    {
        _src = src; _session = session; _audit = audit;
    }

    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản này chỉ được xem Chấm công."); }

    private JsonArray Read(string sheet)
    {
        try { return JsonNode.Parse(_src().Read(Db, sheet)) as JsonArray ?? new JsonArray(); }
        catch { return new JsonArray(); }
    }
    private void Write(Dictionary<string, JsonArray?> sheets) => _src().Write(Db, sheets);
    private static string S(JsonNode? n) => n?.ToString() ?? "";

    public static string GocSheet(string thang) => "Goc_" + thang;
    public static string SuaSheet(string thang) => "Sua_" + thang;

    public void Load()
    {
        NhanVien = Read("NhanVien").OfType<JsonObject>().Select(o => new CcNhanVien
        {
            Id = S(o["id"]), Ma = S(o["ma"]), Ten = S(o["ten"]), Nhom = CcCore.NormNhom(S(o["nhom"])), PhongBan = S(o["pb"]),
            GhiChu = S(o["ghiChu"]), DangLam = o["nghi"]?.ToString() != "1",
        }).Where(n => n.Id.Length > 0).ToList();
        var cfg = Read("CauHinh").OfType<JsonObject>().FirstOrDefault();
        var data = S(cfg?["data"]);
        CauHinh = CcCauHinh.From(data.StartsWith("__JSON__") ? JsonNode.Parse(data[8..]) as JsonObject : null);
    }

    /// <summary>Các tháng đã nạp (yyyy-MM), mới nhất trước.</summary>
    public List<string> Thangs() =>
        _src().List(Db).Where(s => s.StartsWith("Goc_") || s.StartsWith("Sua_")).Select(s => s[4..]).Distinct().OrderByDescending(s => s, StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------------ nhân viên / cấu hình

    private static JsonObject NvJson(CcNhanVien n) => new()
    {
        ["id"] = n.Id, ["ma"] = n.Ma, ["ten"] = n.Ten, ["nhom"] = n.Nhom, ["pb"] = n.PhongBan, ["ghiChu"] = n.GhiChu, ["nghi"] = n.DangLam ? "" : "1",
    };

    public static string NewId(CcNhanVien n, IEnumerable<CcNhanVien> co)
    {
        var used = co.Select(x => x.Id).ToHashSet();
        var baseId = n.Ma.Length > 0 ? "m:" + n.Ma : "t:" + CcCoreText.Key(n.Ten);
        var id = baseId; var i = 2;
        while (used.Contains(id)) id = baseId + "#" + i++;
        return id;
    }

    public void SaveNhanVien(List<CcNhanVien> list)
    {
        RequireEdit();
        foreach (var n in list)
        {
            n.Ma = n.Ma.Trim(); n.Ten = n.Ten.Trim(); n.Nhom = CcCore.NormNhom(n.Nhom);
            if (n.Ten.Length == 0 && n.Ma.Length == 0) throw new BusinessException("Có dòng nhân viên thiếu cả mã lẫn tên.");
        }
        var dup = list.Where(n => n.Ma.Length > 0).GroupBy(n => n.Ma).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new BusinessException($"Mã máy chấm công {dup.Key} bị trùng ở {dup.Count()} nhân viên.");
        foreach (var n in list.Where(n => n.Id.Length == 0)) n.Id = NewId(n, list);
        Write(new() { ["NhanVien"] = new JsonArray(list.Select(n => (JsonNode)NvJson(n)).ToArray()) });
        NhanVien = list;
        _audit?.Log(_session.Username, "Chấm công: lưu nhân viên", $"{list.Count} người");
    }

    public void SaveCauHinh(CcCauHinh c)
    {
        RequireEdit();
        Write(new() { ["CauHinh"] = new JsonArray(new JsonObject { ["data"] = "__JSON__" + c.ToJson().ToJsonString() }) });
        CauHinh = c;
        _audit?.Log(_session.Username, "Chấm công: lưu cấu hình", c.ToJson().ToJsonString());
    }

    // ------------------------------------------------------------------ nạp file máy chấm công

    public sealed class FileNap
    {
        public string Path = "", Thang = "";
        public List<CcGoc> Rows = new();
        /// <summary>Số dòng theo phòng ban (để chọn).</summary>
        public Dictionary<string, int> PhongBan = new();
        public bool CoVSom;
        public Dictionary<(string Ma, DateOnly Ngay), double> VSom = new();
    }

    private static readonly Dictionary<string, string[]> Alias = new()
    {
        ["ma"] = new[] { "mã nhân viên", "mã nv", "ma nhan vien", "mã n.viên", "mã số", "ac-no.", "ac no", "id", "mã chấm công" },
        ["ten"] = new[] { "tên nhân viên", "họ tên", "họ và tên", "tên nv", "tên n.viên", "name" },
        ["pb"] = new[] { "phòng ban", "bộ phận", "department" },
        ["cv"] = new[] { "chức vụ" },
        ["ngay"] = new[] { "ngày", "date", "ngày chấm" },
        ["vao"] = new[] { "giờ vào", "vào 1", "vao", "check in", "time in" },
        ["ra"] = new[] { "giờ ra", "ra 1", "check out", "time out" },
        ["ca"] = new[] { "tên ca", "ca" },
        ["vsom"] = new[] { "v.sớm", "v. sớm", "vào sớm" },
    };

    /// <summary>Đọc bảng "Chi tiết chấm công" (dòng tiêu đề có Mã nhân viên + Ngày + Giờ vào + Giờ ra, ở bất kỳ dòng nào trong 20 dòng đầu).</summary>
    public static FileNap Parse(SheetText.Book book, string path = "")
    {
        foreach (var name in book.SheetNames)
        {
            var rows = book.Sheets[name];
            for (var h = 0; h < Math.Min(20, rows.Count); h++)
            {
                var head = rows[h].Select(x => CcCoreText.Fold(x)).ToArray();
                int Col(string k) { var al = Alias[k].Select(CcCoreText.Fold).ToArray(); for (var c = 0; c < head.Length; c++) if (al.Contains(head[c])) return c; return -1; }
                int ma = Col("ma"), ngay = Col("ngay"), vao = Col("vao"), ra = Col("ra");
                if (ma < 0 || ngay < 0 || vao < 0 || ra < 0) continue;
                int ten = Col("ten"), pb = Col("pb"), cv = Col("cv"), ca = Col("ca"), vs = Col("vsom");
                var f = new FileNap { Path = path, CoVSom = vs >= 0 };
                string G(string[] r, int c) => c >= 0 && c < r.Length ? (r[c] ?? "").Trim() : "";
                for (var i = h + 1; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var m = G(r, ma); var d = CcCore.ParseDate(G(r, ngay));
                    if (m.Length == 0 || d is null) continue;
                    string T(int c) => CcCore.ParseTime(G(r, c)) is int t ? CcCore.Hm(t) : "";
                    var g = new CcGoc(m, G(r, ten), G(r, pb), G(r, cv), d.Value, T(vao), T(ra), G(r, ca));
                    f.Rows.Add(g);
                    f.PhongBan[g.PhongBan] = f.PhongBan.GetValueOrDefault(g.PhongBan) + 1;
                    if (vs >= 0 && double.TryParse(G(r, vs).Replace(',', '.'), System.Globalization.NumberStyles.Float, CcCore.Inv, out var v) && v != 0) f.VSom[(m, d.Value)] = v;
                }
                if (f.Rows.Count == 0) continue;
                f.Thang = f.Rows.GroupBy(x => x.Ngay.ToString("yyyy-MM")).OrderByDescending(gr => gr.Count()).First().Key;
                return f;
            }
        }
        throw new BusinessException("Không thấy bảng chấm công trong file (cần dòng tiêu đề có Mã nhân viên, Ngày, Giờ vào, Giờ ra).");
    }

    /// <summary>
    /// Đọc file máy chấm công. xlsx đọc SỐ GỐC (ngày = số ngày Excel): phần mềm máy chấm công định dạng ngày kiểu Mỹ "10/1/26"
    /// (tháng/ngày/năm 2 số) — đọc chữ đang hiện dễ nhầm ngày ↔ tháng.
    /// </summary>
    public static FileNap ReadFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".xlsx" or ".xlsm")
            try { return Parse(SheetText.ReadXlsxFast(path, rawNumbers: true), path); }
            catch (BusinessException) { throw; }
            catch (Exception e) when (e is not OutOfMemoryException) { /* file lạ → cách đọc chung */ }
        return Parse(SheetText.Read(path), path);
    }

    public sealed record KetQuaNap(int Dong, int NvMoi, string Thang, int VSom);

    /// <summary>
    /// Lưu tháng của file: thay Goc_thang (chỉ dòng thuộc phòng ban đã chọn hoặc nhân viên đã có trong danh sách); thêm nhân viên mới của
    /// phòng ban đã chọn vào cuối danh sách (bộ phận lấy từ cột Chức vụ nếu có); file có cột V.Sớm (bảng đã làm tay) → ghi vào sửa tay.
    /// </summary>
    public KetQuaNap Commit(FileNap f, IEnumerable<string> phongBan)
    {
        RequireEdit();
        var pbs = phongBan.Select(CcCoreText.Fold).ToHashSet();
        var byMa = NhanVien.Where(n => n.Ma.Length > 0).ToDictionary(n => n.Ma);
        var keep = f.Rows.Where(r => r.Ngay.ToString("yyyy-MM") == f.Thang && (pbs.Contains(CcCoreText.Fold(r.PhongBan)) || byMa.ContainsKey(r.Ma))).ToList();
        var list = NhanVien.ToList(); var moi = 0;
        foreach (var g in keep.GroupBy(r => r.Ma))
        {
            var first = g.First();
            var nhom = g.Select(x => CcCore.NormNhom(x.ChucVu)).FirstOrDefault(x => x.Length > 0) ?? "";
            if (byMa.TryGetValue(g.Key, out var co))
            {
                if (co.Nhom.Length == 0 && nhom.Length > 0) co.Nhom = nhom;
                if (co.Ten.Length == 0) co.Ten = first.Ten;
                continue;
            }
            var n = new CcNhanVien { Ma = g.Key, Ten = first.Ten, PhongBan = first.PhongBan, Nhom = nhom };
            n.Id = NewId(n, list); list.Add(n); byMa[n.Ma] = n; moi++;
        }
        var sheets = new Dictionary<string, JsonArray?>
        {
            [GocSheet(f.Thang)] = new JsonArray(keep.OrderBy(r => r.Ma, StringComparer.Ordinal).ThenBy(r => r.Ngay).Select(r => (JsonNode)new JsonObject
            {
                ["ma"] = r.Ma, ["ten"] = r.Ten, ["pb"] = r.PhongBan, ["ngay"] = r.Ngay.ToString("yyyy-MM-dd"), ["vao"] = r.Vao, ["ra"] = r.Ra, ["ca"] = r.Ca,
            }).ToArray()),
            ["NhanVien"] = new JsonArray(list.Select(n => (JsonNode)NvJson(n)).ToArray()),
        };
        var nVs = 0;
        if (f.VSom.Count > 0)
        {
            var sua = ReadSua(f.Thang);
            foreach (var ((ma, ngay), v) in f.VSom)
            {
                if (!byMa.TryGetValue(ma, out var nv) || ngay.ToString("yyyy-MM") != f.Thang) continue;
                var s = sua.FirstOrDefault(x => x.Nv == nv.Id && x.Ngay == ngay);
                if (s is null) { s = new CcSua { Nv = nv.Id, Ngay = ngay }; sua.Add(s); }
                s.VSom = v; nVs++;
            }
            sheets[SuaSheet(f.Thang)] = SuaJson(sua);
        }
        Write(sheets);
        NhanVien = list;
        _audit?.Log(_session.Username, "Chấm công: nạp file", $"{Path.GetFileName(f.Path)} · tháng {f.Thang} · {keep.Count} dòng · {moi} nhân viên mới");
        return new(keep.Count, moi, f.Thang, nVs);
    }

    // ------------------------------------------------------------------ sửa tay

    public List<CcSua> ReadSua(string thang) => Read(SuaSheet(thang)).OfType<JsonObject>().Select(o =>
    {
        double? D(string k) => double.TryParse(S(o[k]), System.Globalization.NumberStyles.Float, CcCore.Inv, out var v) ? v : null;
        string? T(string k) => o[k] is null ? null : S(o[k]);
        return new CcSua
        {
            Nv = S(o["nv"]), Ngay = CcCore.ParseDate(S(o["ngay"])) ?? default, Vao = T("vao"), Ra = T("ra"), ChepTu = T("chepTu"),
            VSom = D("vsom"), Chot = D("chot"), SauSang = D("sauSang"), GhiChu = S(o["ghiChu"]),
        };
    }).Where(s => s.Nv.Length > 0 && s.Ngay != default).ToList();

    private static JsonArray SuaJson(IEnumerable<CcSua> list) => new(list.Where(s => !s.Empty).OrderBy(s => s.Nv, StringComparer.Ordinal).ThenBy(s => s.Ngay).Select(s =>
    {
        var o = new JsonObject { ["nv"] = s.Nv, ["ngay"] = s.Ngay.ToString("yyyy-MM-dd") };
        if (s.Vao is not null) o["vao"] = s.Vao;
        if (s.Ra is not null) o["ra"] = s.Ra;
        if (s.ChepTu is not null) o["chepTu"] = s.ChepTu;
        if (s.VSom is double v) o["vsom"] = v.ToString(CcCore.Inv);
        if (s.Chot is double c) o["chot"] = c.ToString(CcCore.Inv);
        if (s.SauSang is double ss) o["sauSang"] = ss.ToString(CcCore.Inv);
        if (s.GhiChu.Length > 0) o["ghiChu"] = s.GhiChu;
        return (JsonNode)o;
    }).ToArray());

    /// <summary>Ghi các sửa tay (thay bản cũ cùng nhân viên + ngày; sửa rỗng = xóa).</summary>
    public void SaveSua(string thang, IEnumerable<CcSua> changes)
    {
        RequireEdit();
        var all = ReadSua(thang).ToDictionary(s => (s.Nv, s.Ngay));
        var n = 0;
        foreach (var c in changes)
        {
            if (c.Vao is not null && c.Vao.Length > 0 && CcCore.ParseTime(c.Vao) is null) throw new BusinessException($"Giờ vào \"{c.Vao}\" không đúng dạng giờ (vd 07:30).");
            if (c.Ra is not null && c.Ra.Length > 0 && CcCore.ParseTime(c.Ra) is null) throw new BusinessException($"Giờ ra \"{c.Ra}\" không đúng dạng giờ (vd 17:00).");
            if (c.Vao is { Length: > 0 }) c.Vao = CcCore.Hm(CcCore.ParseTime(c.Vao));
            if (c.Ra is { Length: > 0 }) c.Ra = CcCore.Hm(CcCore.ParseTime(c.Ra));
            if (c.Empty) all.Remove((c.Nv, c.Ngay)); else all[(c.Nv, c.Ngay)] = c;
            n++;
        }
        Write(new() { [SuaSheet(thang)] = SuaJson(all.Values) });
        _audit?.Log(_session.Username, "Chấm công: sửa tay", $"tháng {thang} · {n} ô");
    }

    // ------------------------------------------------------------------ tính tháng

    public sealed class Thang
    {
        public string Ky = "";
        public List<DateOnly> Ngays = new();
        public List<CcNhanVien> Nvs = new();
        public Dictionary<(string Nv, DateOnly Ngay), CcDong> Dong = new();
        public List<CcDong> Rows => Nvs.SelectMany(n => Ngays.Select(d => Dong[(n.Id, d)])).ToList();
        public double TongGio(CcNhanVien n) => Ngays.Sum(d => Dong[(n.Id, d)].Chot);
        public int SoNgay(CcNhanVien n) => Ngays.Count(d => Dong[(n.Id, d)].CoMat);
        /// <summary>Tổng giờ sau sang công (không gồm giờ dư còn lại).</summary>
        public double TongSau(CcNhanVien n) => Math.Round(Ngays.Sum(d => Dong[(n.Id, d)].SauSang), 2);
        /// <summary>Số ngày đủ công (sau sang ≥ giờ chuẩn).</summary>
        public int NgayDu(CcNhanVien n) => Ngays.Count(d => Dong[(n.Id, d)].SauSang >= GioChuan - 1e-9);
        public double GioChuan = 8;
        /// <summary>Giờ dư tháng trước đã đưa vào bù / giờ dư còn lại cuối tháng (tăng ca chưa dùng), theo nhân viên.</summary>
        public Dictionary<string, double> DuTruoc = new(), DuCon = new();
        public List<(CcNhanVien Nv, CcCore.Sang Lan)> SangList = new();
        public int CanhBao(CcNhanVien n) => Ngays.Count(d => Dong[(n.Id, d)].CanhBao.Length > 0);
    }

    public Thang Tinh(string ky) => Tinh(ky, 0);

    private Thang Tinh(string ky, int depth)
    {
        Dictionary<string, double>? du = null;
        var truoc = KyTruoc(ky);
        if (CauHinh.SangCong && CauHinh.BuThangTruoc.Contains(ky) && depth < 12 && Thangs().Contains(truoc)) du = Tinh(truoc, depth + 1).DuCon;
        return Tinh(ky, Read(GocSheet(ky)).OfType<JsonObject>().Select(o =>
            new CcGoc(S(o["ma"]), S(o["ten"]), S(o["pb"]), "", CcCore.ParseDate(S(o["ngay"])) ?? default, S(o["vao"]), S(o["ra"]), S(o["ca"]))).ToList(), ReadSua(ky), NhanVien, CauHinh, du);
    }

    public static string KyTruoc(string ky) { var d = new DateOnly(int.Parse(ky[..4]), int.Parse(ky[5..7]), 1).AddMonths(-1); return d.ToString("yyyy-MM"); }

    /// <summary>Giờ dư còn lại tháng trước (để hiện ở ô "Bù giờ dư tháng trước"), null = tháng trước chưa có dữ liệu.</summary>
    public double? DuThangTruoc(string ky)
    {
        var truoc = KyTruoc(ky);
        return Thangs().Contains(truoc) ? Tinh(truoc).DuCon.Values.Sum() : null;
    }

    public void SetBuThangTruoc(string ky, bool on)
    {
        RequireEdit();
        var c = CauHinh;
        if (on) c.BuThangTruoc.Add(ky); else c.BuThangTruoc.Remove(ky);
        SaveCauHinh(c);
    }

    /// <summary>Tính bảng công tháng (hàm thuần — test gọi thẳng).</summary>
    public static Thang Tinh(string ky, List<CcGoc> goc, List<CcSua> sua, List<CcNhanVien> nvs, CcCauHinh c, Dictionary<string, double>? duTruoc = null)
    {
        var y = int.Parse(ky[..4]); var m = int.Parse(ky[5..7]);
        var t = new Thang { Ky = ky, Ngays = Enumerable.Range(1, DateTime.DaysInMonth(y, m)).Select(d => new DateOnly(y, m, d)).ToList() };
        var gocMap = new Dictionary<(string, DateOnly), CcGoc>();
        foreach (var g in goc) gocMap[(g.Ma, g.Ngay)] = g;
        var suaMap = sua.GroupBy(s => (s.Nv, s.Ngay)).ToDictionary(g => g.Key, g => g.Last());
        var coDuLieu = goc.Select(g => g.Ma).ToHashSet();
        var coSua = sua.Select(s => s.Nv).ToHashSet();
        t.Nvs = nvs.Where(n => n.DangLam || (n.Ma.Length > 0 && coDuLieu.Contains(n.Ma)) || coSua.Contains(n.Id)).ToList();
        var byId = nvs.ToDictionary(n => n.Id);
        foreach (var n in t.Nvs)
            foreach (var d in t.Ngays)
            {
                var g = n.Ma.Length > 0 ? gocMap.GetValueOrDefault((n.Ma, d)) : null;
                var s = suaMap.GetValueOrDefault((n.Id, d));
                var r = new CcDong { Nv = n, Ngay = d, VaoGoc = g?.Vao ?? "", RaGoc = g?.Ra ?? "", GhiChu = s?.GhiChu ?? "" };
                string vao = r.VaoGoc, ra = r.RaGoc;
                if (s?.ChepTu is { Length: > 0 } src && byId.TryGetValue(src, out var nguon))
                {
                    var g2 = nguon.Ma.Length > 0 ? gocMap.GetValueOrDefault((nguon.Ma, d)) : null;
                    var s2 = suaMap.GetValueOrDefault((nguon.Id, d));
                    vao = s2?.Vao ?? g2?.Vao ?? ""; ra = s2?.Ra ?? g2?.Ra ?? "";
                    r.ChepTu = nguon.Ten.Length > 0 ? nguon.Ten : nguon.Ma;
                }
                if (s?.Vao is not null) { vao = s.Vao; r.SuaVao = true; }
                if (s?.Ra is not null) { ra = s.Ra; r.SuaRa = true; }
                r.Vao = vao; r.Ra = ra;
                r.VSom = s?.VSom ?? 0; r.SuaVSom = s?.VSom is not null;
                r.Ket = CcCore.Tinh(CcCore.ParseTime(vao), CcCore.ParseTime(ra), n.Nhom, r.VSom, c);
                r.Chot = s?.Chot ?? r.Ket.Gio; r.SuaChot = s?.Chot is not null;
                r.CanhBao = r.SuaChot ? "" : r.Ket.CanhBao;
                r.SauSang = s?.SauSang ?? r.Chot; r.SuaSang = s?.SauSang is not null;
                t.Dong[(n.Id, d)] = r;
            }
        // sang công (người dùng 26/11): giờ dư bù ngày thiếu trong tháng (+ giờ dư tháng trước nếu bật)
        t.GioChuan = c.GioChuan / 60.0;
        foreach (var n in t.Nvs)
        {
            var rows = t.Ngays.Select(d => t.Dong[(n.Id, d)]).ToList();
            var truoc = duTruoc?.GetValueOrDefault(n.Id) ?? 0;
            if (!c.SangCong) { t.DuCon[n.Id] = Math.Round(rows.Where(r => !r.SuaSang).Sum(r => Math.Max(0, r.Chot - t.GioChuan)), 2); continue; }
            var (sau, lan, con) = CcCore.SangCong(rows.Select(r => (r.Ngay, r.SuaSang ? r.SauSang : r.Chot, r.SuaSang)).ToList(), t.GioChuan, truoc);
            for (var i = 0; i < rows.Count; i++) if (!rows[i].SuaSang) rows[i].SauSang = sau[i];
            foreach (var l in lan)
            {
                t.Dong[(n.Id, l.Den)].Nhan.Add(l);
                if (l.Tu is DateOnly tu) t.Dong[(n.Id, tu)].Cho.Add(l);
                t.SangList.Add((n, l));
            }
            if (truoc > 0) t.DuTruoc[n.Id] = truoc;
            t.DuCon[n.Id] = con;
        }
        return t;
    }

    // ------------------------------------------------------------------ xuất Excel

    /// <summary>Xuất 2 sheet: "Bảng công" (nhân viên × ngày, giờ chốt + tổng) và "Chi tiết" (cùng cột bảng Excel người dùng làm tay).</summary>
    public static void Export(Thang t, string path)
    {
        using var wb = new XLWorkbook();
        var bc = wb.AddWorksheet("Bảng công");
        bc.Cell(1, 1).Value = $"BẢNG CHẤM CÔNG THÁNG {t.Ky[5..]}/{t.Ky[..4]}";
        bc.Cell(1, 1).Style.Font.Bold = true; bc.Cell(1, 1).Style.Font.FontSize = 14;
        var head = new List<string> { "STT", "Mã NV", "Họ tên", "Bộ phận" };
        head.AddRange(t.Ngays.Select(d => d.Day.ToString()));
        head.AddRange(new[] { "Ngày đủ công", "Tổng giờ", "Giờ dư còn (tăng ca)" });
        for (var c = 0; c < head.Count; c++) bc.Cell(3, c + 1).Value = head[c];
        for (var i = 0; i < t.Ngays.Count; i++) bc.Cell(2, 5 + i).Value = CcCore.Thu(t.Ngays[i]);
        var row = 4;
        foreach (var n in t.Nvs)
        {
            bc.Cell(row, 1).Value = row - 3; bc.Cell(row, 2).Value = n.Ma; bc.Cell(row, 3).Value = n.Ten; bc.Cell(row, 4).Value = n.Nhom;
            for (var i = 0; i < t.Ngays.Count; i++)
            {
                var d = t.Dong[(n.Id, t.Ngays[i])];
                if (d.SauSang != 0) bc.Cell(row, 5 + i).Value = d.SauSang;
                if (d.CanhBao.Length > 0) bc.Cell(row, 5 + i).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2D6");
                else if (d.Nhan.Count > 0) bc.Cell(row, 5 + i).Style.Fill.BackgroundColor = XLColor.FromHtml("#E3F4EA");
                if (t.Ngays[i].DayOfWeek == DayOfWeek.Sunday) bc.Cell(row, 5 + i).Style.Font.FontColor = XLColor.FromHtml("#B42318");
            }
            bc.Cell(row, 5 + t.Ngays.Count).Value = t.NgayDu(n);
            bc.Cell(row, 6 + t.Ngays.Count).Value = t.TongSau(n);
            bc.Cell(row, 7 + t.Ngays.Count).Value = t.DuCon.GetValueOrDefault(n.Id);
            row++;
        }
        var hr = bc.Range(2, 1, 3, head.Count); hr.Style.Font.Bold = true; hr.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDF1F6");
        bc.Range(3, 1, row - 1, head.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        bc.Range(3, 1, row - 1, head.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        bc.Column(3).Width = 26; for (var i = 0; i < t.Ngays.Count; i++) bc.Column(5 + i).Width = 5;
        bc.SheetView.FreezeRows(3); bc.SheetView.FreezeColumns(4);

        var ct = wb.AddWorksheet("Chi tiết");
        ct.Cell(1, 1).Value = "CHI TIẾT CHẤM CÔNG";
        ct.Cell(2, 1).Value = $"Từ ngày {t.Ngays[0]:dd/MM/yyyy} đến ngày {t.Ngays[^1]:dd/MM/yyyy}";
        var h2 = new[] { "Mã nhân viên", "Tên nhân viên", "Phòng ban", "Chức vụ", "Ngày", "Thứ", "Giờ vào", "Giờ ra", "Vào", "Ra", "Nghỉ", "V.Sớm", "Công", "Chốt", "Sau sang công", "Sang công", "Ghi chú" };
        for (var c = 0; c < h2.Length; c++) ct.Cell(3, c + 1).Value = h2[c];
        ct.Range(3, 1, 3, h2.Length).Style.Font.Bold = true;
        row = 4;
        foreach (var d in t.Rows)
        {
            ct.Cell(row, 1).Value = d.Nv.Ma; ct.Cell(row, 2).Value = d.Nv.Ten; ct.Cell(row, 3).Value = d.Nv.PhongBan; ct.Cell(row, 4).Value = d.Nv.Nhom;
            ct.Cell(row, 5).Value = d.Ngay.ToDateTime(TimeOnly.MinValue); ct.Cell(row, 5).Style.DateFormat.Format = "dd/MM/yyyy";
            ct.Cell(row, 6).Value = CcCore.Thu(d.Ngay); ct.Cell(row, 7).Value = d.Vao; ct.Cell(row, 8).Value = d.Ra;
            ct.Cell(row, 9).Value = CcCore.HmShort(d.Ket.VaoTron); ct.Cell(row, 10).Value = CcCore.HmShort(d.Ket.RaTron);
            if (d.Ket.Nghi > 0) ct.Cell(row, 11).Value = CcCore.HmShort(d.Ket.Nghi);
            if (d.VSom != 0) ct.Cell(row, 12).Value = d.VSom;
            if (d.Ket.Cong > 0) ct.Cell(row, 13).Value = CcCore.HmShort(d.Ket.Cong);
            ct.Cell(row, 14).Value = d.Chot;
            ct.Cell(row, 15).Value = d.SauSang;
            if (SangText(d) is { Length: > 0 } st) ct.Cell(row, 16).Value = st;
            var note = string.Join(" · ", new[] { d.GhiChu, d.ChepTu.Length > 0 ? "giờ của " + d.ChepTu : "", d.SuaChot ? "chốt sửa tay" : "", d.SuaSang ? "sang công sửa tay" : "", d.CanhBao }.Where(x => x.Length > 0));
            if (note.Length > 0) ct.Cell(row, 17).Value = note;
            if (d.DaSua) ct.Range(row, 1, row, 17).Style.Fill.BackgroundColor = XLColor.FromHtml("#EEEBFC");
            row++;
        }
        ct.Column(2).Width = 26; ct.Column(16).Width = 30; ct.Column(17).Width = 40;
        ct.SheetView.FreezeRows(3);

        var sc = wb.AddWorksheet("Sang công");
        var h3 = new[] { "Mã nhân viên", "Tên nhân viên", "Từ ngày", "Sang ngày", "Số giờ" };
        for (var c = 0; c < h3.Length; c++) sc.Cell(1, c + 1).Value = h3[c];
        sc.Range(1, 1, 1, h3.Length).Style.Font.Bold = true;
        row = 2;
        foreach (var (nv, l) in t.SangList)
        {
            sc.Cell(row, 1).Value = nv.Ma; sc.Cell(row, 2).Value = nv.Ten;
            sc.Cell(row, 3).Value = l.Tu is DateOnly tu ? tu.ToString("dd/MM/yyyy") : "tháng trước";
            sc.Cell(row, 4).Value = l.Den.ToString("dd/MM/yyyy"); sc.Cell(row, 5).Value = l.Gio;
            row++;
        }
        sc.Column(2).Width = 26;
        wb.SaveAs(path);
    }

    /// <summary>"nhận 1 giờ từ 05/10 · cho 0,5 giờ sang 07/10" của 1 ngày.</summary>
    public static string SangText(CcDong d) => string.Join(" · ",
        d.Nhan.Select(l => $"nhận {l.Gio.ToString("0.##", CcCore.Inv)} giờ từ {(l.Tu is DateOnly tu ? tu.ToString("dd/MM") : "tháng trước")}")
        .Concat(d.Cho.Select(l => $"cho {l.Gio.ToString("0.##", CcCore.Inv)} giờ sang {l.Den:dd/MM}")));
}

/// <summary>Chuẩn hóa chữ để so tiêu đề / phòng ban (bỏ dấu, viết thường, gộp khoảng trắng).</summary>
public static class CcCoreText
{
    public static string Fold(string? s)
    {
        var t = (s ?? "").Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in t)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch == 'đ' ? 'd' : ch);
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ");
    }
    public static string Key(string? s) => Fold(s).Replace(' ', '-');
}

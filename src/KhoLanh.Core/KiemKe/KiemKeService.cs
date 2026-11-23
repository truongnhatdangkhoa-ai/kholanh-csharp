using System.Globalization;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.Entry;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.SoDo;
using KhoLanh.Core.ViTri;
using static KhoLanh.Core.KiemKe.KiemKeCore;

namespace KhoLanh.Core.KiemKe;

/// <summary>
/// Module Đối chiếu kiểm kê (C#, theo "Số liệu kiểm kê" của app cũ). Database "so-lieu-kiem-ke".
/// CHỈ ĐỌC: Nhập liệu (trang kiểm kê), Tồn kho An An (LoadData), Tồn theo vị trí + Sơ đồ kho (qua SoDoService), Tổng hợp (tên hàng).
/// GHI (sheet riêng của app mới, tiền tố CS_): trang đã chọn, số kế toán, lịch sử + bản chốt; bản chụp tồn TonChot / TonChotMeta cùng dạng app cũ.
/// Sửa mã gõ sai ghi thẳng vào trang Nhập liệu nguồn (cần quyền Sửa Nhập liệu).
/// </summary>
public sealed class KiemKeService
{
    public const string Db = "so-lieu-kiem-ke", PermKey = "m9";
    public const string SheetState = "CS_TrangThai", SheetKt = "CS_KeToan", SheetKtMeta = "CS_KeToanMeta", SheetChot = "CS_ChotLichSu", SheetChotBan = "CS_ChotBan",
        SheetSnap = "TonChot", SheetSnapMeta = "TonChotMeta";
    private const string DbAn = "ton-kho-an-an";
    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly EntryService _entry;
    private readonly SoDoService _sodo;

    public KiemKeService(Func<ISheetSource> source, Session session, AuditService audit, EntryService entry, SoDoService sodo)
    { _src = source; _session = session; _audit = audit; _entry = entry; _sodo = sodo; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void RequireView() { if (!CanView) throw new BusinessException("Tài khoản không có quyền xem Đối chiếu kiểm kê."); }
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa Đối chiếu kiểm kê (chọn trang, số kế toán, chốt)."); }
    private void RequireExport() { if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file Đối chiếu kiểm kê."); }
    private string UserName => _session.CurrentUser is { } u ? (u.FullName.Length > 0 ? u.FullName : u.Username) : "";
    private void Log(string action, string detail) => _audit.Log(_session.Username, "Đối chiếu kiểm kê: " + action, detail);

    // ------------------------------------------------------------------ trạng thái

    public List<Page> Pages { get; private set; } = new();            // mọi trang Nhập liệu có dạng kiểm kê
    public HashSet<string> Chosen { get; private set; } = new();
    public bool LatestOnly { get; private set; }
    public string StockSrc { get; private set; } = "live";             // live = Tồn kho An An hiện tại · snap = bản chụp tồn
    public List<string> People { get; private set; } = new();
    public Extracted Ex { get; private set; } = new();
    public string ChotDate { get; private set; } = "";
    /// <summary>Tính tồn kho gửi (module Tồn kho gửi, đổi mã 19 → 21) vào đối chiếu Kế toán.</summary>
    public bool GuiOn { get; private set; }
    public List<KhoGui.Line> GuiRows { get; private set; } = new();
    public List<(string Gui, string Ma)> GuiMap { get; private set; } = new();
    public KhoGui.Result Gui { get; private set; } = new();
    public string GuiInfo { get; private set; } = "";
    public const string DbCuoiKy = "so-lieu-cuoi-ky", SheetGuiMap = "DC_ChuyenDoi";
    public PhatSinh.Adj Adj { get; private set; } = new();
    public List<Line> Lines { get; private set; } = new();              // dòng kiểm kê dùng để đối chiếu (đã bỏ lần đếm cũ nếu chọn)
    public List<Line> Dropped { get; private set; } = new();
    public List<Recount> Recounts { get; private set; } = new();
    public List<Stock> StockLive { get; private set; } = new();
    public List<Stock> StockSnap { get; private set; } = new();
    public string StockFile { get; private set; } = "";
    public string StockDate { get; private set; } = "";
    public string SnapInfo { get; private set; } = "";
    public List<KtRow> KtRows { get; private set; } = new();
    public KtSet Kt { get; private set; } = new();
    public string KtInfo { get; private set; } = "";
    public Totals T { get; private set; } = new();
    public Sum Summary { get; private set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    public VtCmp? Vt { get; private set; }
    public List<Chot> Chots { get; private set; } = new();
    public bool Loaded { get; private set; }
    public List<Stock> Stock => StockSrc == "snap" && StockSnap.Count > 0 ? StockSnap : StockLive;
    private Dictionary<string, string> _tenMap = new();

    public sealed class Chot { public string Id = "", Kind = "", At = "", By = "", LyDo = ""; public int Ver, NMa, NChenh, KtLech; public double KgChenh; }
    public Chot? CurrentChot { get { Chot? cur = null; foreach (var c in Chots.OrderBy(c => c.At, StringComparer.Ordinal)) cur = c.Kind == "chot" ? c : null; return cur; } }
    public bool Locked => CurrentChot is not null;

    private JsonArray Read(string db, string sheet)
    {
        try { return JsonNode.Parse(_src().Read(db, sheet)) as JsonArray ?? new JsonArray(); } catch { return new JsonArray(); }
    }
    private static string S(JsonNode? n) => ViTriService.S(n);
    private static JsonNode? JsonOf(string? s)
    {
        var t = (s ?? "").Trim(); if (t.StartsWith("__JSON__", StringComparison.Ordinal)) t = t[8..];
        try { return JsonNode.Parse(t); } catch { return null; }
    }
    private static double Num0(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : (Num(S(n)) is { } x && !double.IsNaN(x) ? x : 0);
    private static string Iso() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public void Load()
    {
        RequireView();
        // trang Nhập liệu dạng kiểm kê
        Pages = _entry.PagesForRead().Where(p => p.Template is not ("nhap-kho" or "xuat-kho" or "trong") && IsKiemKe(p.Columns.Select(c => c.Header)))
            .Select(p => new Page { Id = p.Id, Name = p.Name, Template = p.Template, UpdatedAt = p.UpdatedAt, Ngay = NgayOf(p.Name, p.CreatedAt), Columns = p.Columns.Select(c => c.Header).ToList(), Rows = _entry.LoadForRead(p) })
            .OrderByDescending(p => p.Ngay, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.CurrentCulture).ToList();
        var st = Read(Db, SheetState); var sj = st.Count > 0 && st[0] is JsonObject so ? JsonOf(S(so["data"])) as JsonObject : null;
        Chosen = (sj?["pages"] as JsonArray ?? new JsonArray()).Select(S).Where(x => x.Length > 0).ToHashSet();
        LatestOnly = S(sj?["latest"]) is "true" or "1";
        ChotDate = PhatSinh.IsoOk(S(sj?["chotDate"])) ? S(sj?["chotDate"]) : "";
        GuiOn = S(sj?["gui"]) is "true" or "1";
        LoadGui();
        StockSrc = S(sj?["stock"]) == "snap" ? "snap" : "live";
        People = (sj?["people"] as JsonArray ?? new JsonArray()).Select(S).Where(x => x.Length > 0).ToList();
        // Tồn kho An An (module Tồn kho An An)
        var meta = Read(DbAn, "LoadDataMeta"); var mj = meta.Count > 0 && meta[0] is JsonObject mo ? JsonOf(S(mo["data"])) as JsonObject : null;
        var headers = (mj?["headers"] as JsonArray ?? new JsonArray()).Select(S).ToList();
        StockFile = S(mj?["fileName"]); StockDate = S(mj?["dataDate"]);
        if (headers.Count == 0) StockLive = new();
        else
        {
            var data = Read(DbAn, "LoadData").OfType<JsonObject>().Select(o => Enumerable.Range(0, headers.Count).Select(i => S(o["c" + i])).ToArray()).ToList();
            string[] grp;
            try { grp = PhanLoaiM1.Classify(headers, data, ManualAssign(mj), CustomFilters()).Primary; } catch { grp = Array.Empty<string>(); }
            StockLive = MakeStock(headers, data, grp);
        }
        // bản chụp tồn
        StockSnap = Read(Db, SheetSnap).OfType<JsonObject>().Select(o => new Stock
        {
            Ma = S(o["ma"]), Key = NormKey(S(o["ma"])), Ten = S(o["ten"]), Hd = S(o["hd"]), Dt = S(o["dt"]), Nhom = S(o["nhom"]), Ton = Num0(o["ton"]),
            Tl = o["tl"] is null || S(o["tl"]).Length == 0 ? null : Num0(o["tl"]),
        }).Where(s => s.Ma.Length > 0).ToList();
        var sm = Read(Db, SheetSnapMeta); var smj = sm.Count > 0 && sm[0] is JsonObject smo ? JsonOf(S(smo["data"])) as JsonObject : null;
        SnapInfo = smj is null ? "" : $"{S(smj["file"])} (dữ liệu ngày {S(smj["date"])}) · chụp {AtText(S(smj["at"]))}{(S(smj["by"]).Length > 0 ? " · " + S(smj["by"]) : "")}";
        // kế toán
        KtRows = Read(Db, SheetKt).OfType<JsonObject>().Select(o => new KtRow(S(o["ma"]), S(o["ten"]), S(o["tl"]), S(o["sl"]), S(o["net"]))).ToList();
        var km = Read(Db, SheetKtMeta); var kmj = km.Count > 0 && km[0] is JsonObject kmo ? JsonOf(S(kmo["data"])) as JsonObject : null;
        KtInfo = kmj is null ? "" : $"{S(kmj["src"])} · {AtText(S(kmj["at"]))}{(S(kmj["by"]).Length > 0 ? " · " + S(kmj["by"]) : "")}";
        // chốt
        Chots = Read(Db, SheetChot).OfType<JsonObject>().Select(o => new Chot
        {
            Id = S(o["id"]), Kind = S(o["kind"]), At = S(o["at"]), By = S(o["by"]), LyDo = S(o["lyDo"]), Ver = (int)Num0(o["ver"]), NMa = (int)Num0(o["nMa"]),
            NChenh = (int)Num0(o["nChenh"]), KgChenh = Num0(o["kgChenh"]), KtLech = (int)Num0(o["ktLech"]),
        }).Where(c => c.Kind.Length > 0).ToList();
        // tên hàng từ Tổng hợp › Danh sách mã hàng (Ghi chú = tên)
        _tenMap = new();
        foreach (var o in Read("tong-hop", "MaDaTao").OfType<JsonObject>())
        {
            var code = NormKey(S(o["Mã"])); var ten = S(o["Ghi chú"]).Trim();
            if (code.Length > 0 && ten.Length > 0) _tenMap.TryAdd(code, ten);
        }
        try { _sodo.LoadForRead(); } catch { /* chưa có dữ liệu vị trí → bỏ phần theo vị trí */ }
        Recompute();
        Loaded = true;
    }
    private static Dictionary<int, string> ManualAssign(JsonObject? meta)
    {
        var d = new Dictionary<int, string>();
        if (meta?["manualAssign"] is JsonObject mo) foreach (var kv in mo) if (int.TryParse(kv.Key, out var i) && S(kv.Value).Length > 0) d[i] = S(kv.Value);
        return d;
    }
    private Dictionary<string, List<PhanLoaiM1.CustomFilter>> CustomFilters()
    {
        var d = new Dictionary<string, List<PhanLoaiM1.CustomFilter>>();
        var rs = Read(DbAn, "DieuKienTuThem"); if (rs.Count == 0 || rs[0] is not JsonObject ro) return d;
        if (JsonOf(S(ro["data"])) is not JsonObject cj) return d;
        foreach (var kv in cj)
        {
            if (kv.Value is not JsonArray arr) continue;
            d[kv.Key] = arr.OfType<JsonObject>().Select(o => new PhanLoaiM1.CustomFilter { ColLabel = S(o["colLabel"]), Op = S(o["op"]), Value = S(o["value"]), From = (int)Num0(o["from"]), To = (int)Num0(o["to"]) }).ToList();
        }
        return d;
    }
    public static string AtText(string iso) => DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
        ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : iso;

    /// <summary>Tính lại toàn bộ từ dữ liệu đã đọc (đổi trang chọn, lần đếm mới nhất, nguồn tồn…).</summary>
    public void Recompute()
    {
        var chosen = Pages.Where(p => Chosen.Contains(p.Id)).ToList();
        Ex = ExtractRows(chosen, ChotDate);
        Recounts = FindRecount(Ex.Rows);
        if (LatestOnly) { Lines = KiemKeCore.LatestOnly(Ex.Rows, out var d); Dropped = d; } else { Lines = Ex.Rows; Dropped = new(); }
        Kt = MakeKeToan(KtRows);
        Adj = PhatSinh.Build(Ex.Mv, Lines, ChotDate);
        Gui = KhoGui.Build(GuiRows, GuiMap);
        T = BuildTotals(Lines, Stock, Kt.Map.Count > 0 ? Kt : null, Adj, GuiOn && Gui.Map.Count > 0 ? Gui.Map : null);
        AddLsx(T.Rows, Lines, Stock);
        foreach (var r in T.Rows) if (r.Ten.Length == 0 && _tenMap.TryGetValue(r.Key, out var t)) r.Ten = t;
        Analyze(T.Rows, Recounts.SelectMany(a => a.Items.Select(x => x.Key)).ToHashSet());
        Summary = Summarize(T.Rows);
        Vt = _sodo.Loaded ? CompareVt(_sodo.B, Lines, _sodo.QcOf) : null;
    }

    // ------------------------------------------------------------------ ghi: lựa chọn / kế toán / bản chụp

    private void SaveState()
    {
        var o = new JsonObject
        {
            ["pages"] = new JsonArray(Chosen.OrderBy(x => x, StringComparer.Ordinal).Select(x => (JsonNode)x).ToArray()), ["latest"] = LatestOnly, ["stock"] = StockSrc, ["chotDate"] = ChotDate, ["gui"] = GuiOn,
            ["people"] = new JsonArray(People.Select(x => (JsonNode)x).ToArray()), ["at"] = Iso(), ["by"] = UserName,
        };
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetState] = new JsonArray(new JsonObject { ["id"] = "cfg", ["data"] = "__JSON__" + o.ToJsonString() }) });
    }
    private void RequireOpen() { if (Locked) throw new BusinessException($"Kỳ kiểm kê đã chốt (bản v{CurrentChot!.Ver}) — hủy chốt trước khi đổi số liệu dùng để đối chiếu."); }

    public void SetChosen(IEnumerable<string> ids, bool latestOnly)
    {
        RequireEdit(); RequireOpen();
        Chosen = ids.Where(id => Pages.Any(p => p.Id == id)).ToHashSet(); LatestOnly = latestOnly;
        SaveState(); Recompute();
        Log("chọn trang kiểm kê", $"{Chosen.Count} trang" + (latestOnly ? ", chỉ lấy lần đếm mới nhất" : ""));
    }
    /// <summary>Đọc tồn kho gửi (module Tồn kho gửi › Dữ Liệu) + bảng chuyển đổi mã (Số liệu cuối kỳ › DC_ChuyenDoi, dùng chung với app cũ).</summary>
    private void LoadGui()
    {
        GuiRows = Read(TonKhoGui.GuiStore.Db, TonKhoGui.GuiStore.SRows).OfType<JsonObject>().Select(TonKhoGui.GuiStore.RowFromDb)
            .Where(r => r.MaHang.Trim().Length > 0)
            .Select(r => new KhoGui.Line { Ma = r.MaHang, Ten = r.TenHang, Lo = r.SoLo, Size = r.Size, Phieu = r.PhieuNhap, Ngay = r.NgayNhapKho, Kg = r.TrongLuong, Kien = r.SoKien }).ToList();
        GuiMap = Read(DbCuoiKy, SheetGuiMap).OfType<JsonObject>().Select(o => (KhoGui.NormMa(S(o["gui"])), KhoGui.NormMa(S(o["ma"])))).Where(x => x.Item1.Length > 0 && x.Item2.Length > 0).ToList();
        var meta = Read(TonKhoGui.GuiStore.Db, TonKhoGui.GuiStore.SMeta); var mj = meta.Count > 0 && meta[0] is JsonObject mo ? JsonOf(S(mo["data"])) as JsonObject : null;
        GuiInfo = GuiRows.Count == 0 ? "Module Tồn kho gửi chưa có dữ liệu." : $"Tồn kho gửi: {GuiRows.Count:#,0} dòng" + (S(mj?["dataDate"]).Length > 0 ? $" · số liệu ngày {S(mj?["dataDate"])}" : "");
    }
    public void SetGuiOn(bool on)
    {
        RequireEdit(); RequireOpen();
        LoadGui(); GuiOn = on; SaveState(); Recompute(); Log(on ? "bật tồn kho gửi" : "tắt tồn kho gửi", GuiInfo);
    }
    public void ReloadGui() { LoadGui(); Recompute(); }
    /// <summary>Lưu bảng chuyển đổi mã kho gửi → mã 21 (ghi đè sheet DC_ChuyenDoi).</summary>
    public void SaveGuiMap(IEnumerable<(string Gui, string Ma)> rows)
    {
        RequireEdit(); RequireOpen();
        var list = new List<(string, string)>(); var seen = new HashSet<string>();
        foreach (var (g0, m0) in rows)
        {
            var g = KhoGui.NormMa(g0); var m = KhoGui.NormMa(m0);
            if (g.Length == 0 && m.Length == 0) continue;
            if (g.Length == 0 || m.Length == 0) throw new BusinessException($"Dòng chuyển đổi “{g0} → {m0}” thiếu mã.");
            if (m.Length != 21) throw new BusinessException($"Mã đích “{m}” phải đủ 21 ký tự.");
            if (!seen.Add(g)) throw new BusinessException($"Mã kho gửi “{g}” bị trùng trong bảng chuyển đổi.");
            list.Add((g, m));
        }
        _src().Write(DbCuoiKy, new Dictionary<string, JsonArray?> { [SheetGuiMap] = new JsonArray(list.Select(x => (JsonNode)new JsonObject { ["gui"] = x.Item1, ["ma"] = x.Item2 }).ToArray()) });
        GuiMap = list; Recompute(); Log("lưu bảng chuyển đổi mã kho gửi", $"{list.Count} dòng");
    }

    public void SetChotDate(string iso)
    {
        RequireEdit(); RequireOpen();
        var t = (iso ?? "").Trim(); if (t.Length > 0 && !PhatSinh.IsoOk(t)) throw new BusinessException("Ngày chốt tồn phải có dạng năm-tháng-ngày (VD 2026-12-31).");
        ChotDate = t; SaveState(); Recompute(); Log("đặt ngày chốt tồn", t.Length > 0 ? t : "(bỏ)");
    }
    public void SetStockSrc(string src)
    {
        RequireEdit(); RequireOpen();
        if (src == "snap" && StockSnap.Count == 0) throw new BusinessException("Chưa có bản chụp tồn — bấm “Chụp tồn kho” trước.");
        StockSrc = src == "snap" ? "snap" : "live"; SaveState(); Recompute();
    }
    public void SetPeople(IEnumerable<string> people)
    {
        RequireEdit();
        People = people.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList(); SaveState();
    }
    /// <summary>Chụp Tồn kho An An hiện tại làm bản cố định để đối chiếu (cùng dạng TonChot của app cũ).</summary>
    public int SnapStock()
    {
        RequireEdit(); RequireOpen();
        if (StockLive.Count == 0) throw new BusinessException("Tồn kho An An chưa có dữ liệu — nạp file ở module Tồn kho An An trước.");
        var rows = new JsonArray(StockLive.Select(s => (JsonNode)new JsonObject { ["ma"] = s.Ma, ["ten"] = s.Ten, ["hd"] = s.Hd, ["dt"] = s.Dt, ["nhom"] = s.Nhom, ["ton"] = s.Ton, ["tl"] = s.Tl is { } t ? t : "" }).ToArray());
        var meta = new JsonObject { ["at"] = Iso(), ["file"] = StockFile, ["date"] = StockDate, ["n"] = StockLive.Count, ["origin"] = new JsonObject { ["type"] = "module", ["label"] = "module Tồn kho An An" }, ["by"] = UserName };
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetSnap] = rows, [SheetSnapMeta] = new JsonArray(new JsonObject { ["data"] = "__JSON__" + meta.ToJsonString() }) });
        StockSnap = StockLive.Select(s => new Stock { Ma = s.Ma, Key = s.Key, Ten = s.Ten, Hd = s.Hd, Dt = s.Dt, Nhom = s.Nhom, Ton = s.Ton, Tl = s.Tl }).ToList();
        SnapInfo = $"{StockFile} (dữ liệu ngày {StockDate}) · chụp {AtText(S(meta["at"]))} · {UserName}";
        StockSrc = "snap"; SaveState(); Recompute();
        Log("chụp tồn kho An An", $"{StockLive.Count} dòng, file {StockFile}");
        return StockLive.Count;
    }

    public void SaveKeToan(IEnumerable<KtRow> rows, string src)
    {
        RequireEdit(); RequireOpen();
        var list = rows.Where(r => (r.Ma + r.Tl + r.Sl + r.Net).Trim().Length > 0).ToList();
        var arr = new JsonArray(list.Select((r, i) => (JsonNode)new JsonObject { ["id"] = "k" + i, ["ma"] = r.Ma.Trim(), ["ten"] = r.Ten.Trim(), ["tl"] = r.Tl.Trim(), ["sl"] = r.Sl.Trim(), ["net"] = r.Net.Trim() }).ToArray());
        var meta = new JsonObject { ["src"] = src, ["at"] = Iso(), ["by"] = UserName, ["n"] = list.Count };
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetKt] = arr, [SheetKtMeta] = list.Count == 0 ? new JsonArray() : new JsonArray(new JsonObject { ["data"] = "__JSON__" + meta.ToJsonString() }) });
        KtRows = list; KtInfo = list.Count == 0 ? "" : $"{src} · {AtText(S(meta["at"]))} · {UserName}";
        Recompute();
        Log("lưu số kế toán", $"{list.Count} dòng ({src})");
    }

    /// <summary>Đọc file Excel kế toán: tìm dòng tiêu đề có “Mã hàng”, lấy Tên hàng, Trọng lượng, Số lượng, Net (hoặc cột kg).</summary>
    public static List<KtRow> ReadKeToanFile(string path)
    {
        using var wb = new XLWorkbook(path);
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed(); if (used is null) continue;
            int last = used.LastRow().RowNumber(), lastC = used.LastColumn().ColumnNumber();
            for (var h = 1; h <= Math.Min(last, 40); h++)
            {
                var heads = Enumerable.Range(1, lastC).Select(c => Hk(ws.Cell(h, c).GetFormattedString())).ToList();
                var cMa = heads.FindIndex(x => x is "mahang" or "mahh" or "mavattu" or "ma");
                if (cMa < 0) continue;
                int F(params string[] ks) { foreach (var k in ks) { var i = heads.FindIndex(x => x == k); if (i >= 0) return i; } foreach (var k in ks) { var i = heads.FindIndex(x => x.StartsWith(k, StringComparison.Ordinal)); if (i >= 0) return i; } return -1; }
                int cTen = F("tenhang", "ten"), cTl = F("trongluong", "tl"), cSl = F("soluong", "sokien", "sl"), cNet = F("net", "netkg", "kg", "tongkg", "khoiluong");
                if (cNet < 0 && cSl < 0) continue;
                var o = new List<KtRow>();
                string G(int r, int c) => c < 0 ? "" : ws.Cell(r, c + 1).GetFormattedString().Trim();
                string V(int r, int c) { if (c < 0) return ""; var cell = ws.Cell(r, c + 1); return cell.DataType == XLDataType.Number ? cell.GetDouble().ToString("R", CultureInfo.InvariantCulture) : cell.GetFormattedString().Trim(); }
                for (var r = h + 1; r <= last; r++)
                {
                    var ma = G(r, cMa); if (ma.Length == 0 && V(r, cNet).Length == 0) continue;
                    if (Hk(ma) is "tong" or "tongcong" or "cong") continue;
                    o.Add(new KtRow(ma, G(r, cTen), V(r, cTl), V(r, cSl), V(r, cNet)));
                }
                return o;
            }
        }
        throw new BusinessException($"File “{Path.GetFileName(path)}” không có dòng tiêu đề chứa cột “Mã hàng” và “Net” (hoặc Số lượng).");
    }

    // ------------------------------------------------------------------ sửa mã gõ sai (ghi vào trang Nhập liệu nguồn)

    /// <summary>Đổi mã của mọi dòng kiểm kê mang khóa <paramref name="key"/> thành <paramref name="to"/> ở trang Nhập liệu nguồn. Trả số dòng đã đổi.</summary>
    public int FixMa(IEnumerable<(string Key, string To)> fixes)
    {
        RequireEdit(); RequireOpen();
        if (!_entry.CanEdit) throw new BusinessException("Sửa mã ghi vào trang Nhập liệu nên cần thêm quyền Sửa của module Nhập liệu.");
        var map = fixes.GroupBy(f => f.Key).ToDictionary(g => g.Key, g => g.First().To);
        var n = 0; var pages = new List<string>();
        foreach (var pid in Lines.Where(l => map.ContainsKey(l.Key)).Select(l => l.PageId).Distinct().ToList())
        {
            var info = _entry.PagesForRead().FirstOrDefault(p => p.Id == pid); if (info is null) continue;
            var rows = _entry.LoadForRead(info);
            var cMa = PageCols(info.Columns.Select(c => c.Header)).GetValueOrDefault("ma", -1); if (cMa < 0) continue;
            var changed = 0;
            foreach (var row in rows) if (cMa < row.Length && map.TryGetValue(NormKey(row[cMa]), out var to) && row[cMa].Trim() != to) { row[cMa] = to; changed++; }
            if (changed == 0) continue;
            _entry.Save(info, info.Columns, rows.Select(r => (IReadOnlyList<string>)r).ToList());
            n += changed; pages.Add(info.Name);
        }
        Log("sửa mã kiểm kê theo An An", $"{n} dòng: " + string.Join(", ", map.Select(m => m.Key + " → " + m.Value)));
        Load();
        return n;
    }

    // ------------------------------------------------------------------ chốt kỳ

    public sealed record Check(string Text, string Ok);   // Ok: "ok" · "warn" · "block"
    public List<Check> Readiness()
    {
        var o = new List<Check>
        {
            new(Lines.Count > 0 ? $"Có số liệu kiểm kê ({Chosen.Count} trang, {Lines.Count:#,0} dòng)" : "Chưa chọn trang kiểm kê nào", Lines.Count > 0 ? "ok" : "block"),
            new(Stock.Count > 0 ? (StockSrc == "snap" ? "Đối chiếu với bản chụp tồn: " + SnapInfo : $"Đối chiếu với Tồn kho An An hiện tại ({StockFile}, ngày {StockDate})") : "Chưa có Tồn kho An An", Stock.Count > 0 ? "ok" : "block"),
            new(Kt.Map.Count > 0 ? $"Có số Kế toán ({Kt.Map.Count:#,0} mã)" : "Chưa có số Kế toán (không bắt buộc)", Kt.Map.Count > 0 ? "ok" : "warn"),
            new(Summary.Open == 0 ? "Không còn mã lệch tồn kho" : $"Còn {Summary.Open} mã lệch ({Summary.KgLech:#,0.##} kg) — vẫn chốt được, bản chốt ghi “chốt còn chênh” kèm lý do", Summary.Open == 0 ? "ok" : "warn"),
            new(Ex.KhongMa + Ex.SoLoi == 0 ? "Dữ liệu kiểm kê không có dòng lỗi" : $"{Ex.KhongMa} dòng có số nhưng thiếu mã, {Ex.SoLoi} dòng số không đọc được", Ex.KhongMa + Ex.SoLoi == 0 ? "ok" : "warn"),
            new(Recounts.Count == 0 ? "Không có vị trí bị đếm ở 2 ngày" : $"{Recounts.Count} nhóm vị trí · mã bị đếm ở ≥ 2 ngày" + (LatestOnly ? " (đang chỉ lấy lần mới nhất)" : " — cân nhắc “Chỉ lấy lần đếm mới nhất”"), Recounts.Count == 0 || LatestOnly ? "ok" : "warn"),
            new(Locked ? $"Kỳ đã chốt (bản v{CurrentChot!.Ver})" : "Kỳ chưa chốt", Locked ? "block" : "ok"),
        };
        return o;
    }
    public Chot DoChot(string lyDo)
    {
        RequireEdit(); RequireOpen();
        if (lyDo.Trim().Length == 0) throw new BusinessException("Nhập lý do chốt.");
        if (Lines.Count == 0) throw new BusinessException("Chưa có số liệu kiểm kê để chốt.");
        var ver = Chots.Select(c => c.Ver).DefaultIfEmpty(0).Max() + 1;
        var c = new Chot { Id = "c" + ver, Kind = "chot", Ver = ver, At = Iso(), By = UserName, LyDo = lyDo.Trim(), NMa = T.Rows.Count, NChenh = Summary.Open, KgChenh = Summary.KgLech, KtLech = Summary.KtLech };
        var ban = Read(Db, SheetChotBan);
        var i = 0;
        foreach (var r in T.Rows)
            ban.Add(new JsonObject
            {
                ["id"] = $"v{ver}#{i++}", ["ver"] = ver, ["ma"] = r.Ma, ["ten"] = r.Ten, ["kkKien"] = r.KkKien ?? 0, ["kkKg"] = r.KkKg ?? 0,
                ["tonKien"] = r.TonKien is { } tk ? tk : "", ["tonKg"] = r.TonKg is { } tg ? tg : "", ["ktKg"] = r.KtKg is { } kt ? kt : "", ["status"] = r.Status, ["ktStatus"] = r.KtStatus,
            });
        var hist = Read(Db, SheetChot); hist.Add(ChotJson(c));
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetChot] = hist, [SheetChotBan] = ban });
        Chots.Add(c);
        Log("chốt kỳ kiểm kê", $"v{ver}, {c.NMa} mã, còn {c.NChenh} mã lệch — lý do: {c.LyDo}");
        return c;
    }
    public void MoChot(string lyDo)
    {
        RequireEdit();
        var cur = CurrentChot ?? throw new BusinessException("Kỳ chưa chốt.");
        if (lyDo.Trim().Length == 0) throw new BusinessException("Nhập lý do hủy chốt.");
        var c = new Chot { Id = "m" + cur.Ver + "-" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture), Kind = "mo", Ver = cur.Ver, At = Iso(), By = UserName, LyDo = lyDo.Trim() };
        var hist = Read(Db, SheetChot); hist.Add(ChotJson(c));
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetChot] = hist });
        Chots.Add(c);
        Log("hủy chốt kỳ kiểm kê", $"v{cur.Ver} — lý do: {c.LyDo}");
    }
    private static JsonObject ChotJson(Chot c) => new()
    {
        ["id"] = c.Id, ["kind"] = c.Kind, ["ver"] = c.Ver, ["at"] = c.At, ["by"] = c.By, ["lyDo"] = c.LyDo, ["nMa"] = c.NMa, ["nChenh"] = c.NChenh, ["kgChenh"] = c.KgChenh, ["ktLech"] = c.KtLech,
    };
    public sealed record BanRow(string Ma, string Ten, double KkKien, double KkKg, double? TonKien, double? TonKg, double? KtKg, string Status, string KtStatus);
    public List<BanRow> ChotBan(int ver) => Read(Db, SheetChotBan).OfType<JsonObject>().Where(o => (int)Num0(o["ver"]) == ver)
        .Select(o => new BanRow(S(o["ma"]), S(o["ten"]), Num0(o["kkKien"]), Num0(o["kkKg"]), S(o["tonKien"]).Length > 0 ? Num0(o["tonKien"]) : null, S(o["tonKg"]).Length > 0 ? Num0(o["tonKg"]) : null,
            S(o["ktKg"]).Length > 0 ? Num0(o["ktKg"]) : null, S(o["status"]), S(o["ktStatus"]))).ToList();

    // ------------------------------------------------------------------ xuất Excel

    private static void Table(IXLWorksheet ws, int top, string[] head, IEnumerable<object?[]> rows, string title)
    {
        ws.Style.Font.FontName = "Times New Roman"; ws.Style.Font.FontSize = 12;
        ws.Cell(1, 1).Value = title; ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 14;
        for (var c = 0; c < head.Length; c++) { var h = ws.Cell(top, c + 1); h.Value = head[c]; h.Style.Font.Bold = true; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8E8E8"); h.Style.Alignment.WrapText = true; h.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; }
        var r = top + 1;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c]) { case null: break; case double d: cell.Value = d; cell.Style.NumberFormat.Format = "#,##0.##"; break; case int n: cell.Value = n; break; default: cell.Value = row[c]!.ToString(); break; }
            }
            r++;
        }
        var rng = ws.Range(top, 1, Math.Max(top, r - 1), head.Length);
        rng.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; rng.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.SheetView.FreezeRows(top); ws.Columns(1, head.Length).AdjustToContents(top, Math.Max(top, r - 1));
        foreach (var col in ws.Columns(1, head.Length)) col.Width = Math.Min(Math.Max(col.Width, 9), 60);
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper; ws.PageSetup.PageOrientation = XLPageOrientation.Landscape; ws.PageSetup.FitToPages(1, 0); ws.PageSetup.SetRowsToRepeatAtTop(top, top);
    }
    /// <summary>Báo cáo Excel: đối chiếu An An, kế toán (nếu có), chi tiết kiểm kê, theo vị trí (nếu có).</summary>
    public void ExportReport(string path, IEnumerable<Row>? onlyRows = null)
    {
        RequireExport();
        using var wb = new XLWorkbook();
        var rows = (onlyRows ?? T.Rows.Where(r => r.Status is not (ChiKT or ChiGui))).ToList();
        var src = StockSrc == "snap" ? "bản chụp tồn " + SnapInfo : $"Tồn kho An An {StockFile} (ngày {StockDate})";
        var hasMv = Adj.List.Count > 0;
        var anHead = new List<string> { "Mã hàng", "Tên hàng", "Kiểm kê (kiện)" };
        if (hasMv) anHead.AddRange(new[] { "Nhập sau ngày đếm (kiện)", "Xuất sau ngày đếm (kiện)", "Nhập (kg)", "Xuất (kg)" });
        anHead.AddRange(new[] { "Tồn kho (kiện)", "Chênh (kiện)", "Kiểm kê (kg)", "Tồn kho (kg)", "Chênh (kg)", "Kết quả", "Nguyên nhân (gợi ý)", "Ghi chú" });
        Table(wb.AddWorksheet("Doi chieu An An"), 3, anHead.ToArray(),
            rows.Select(r =>
            {
                var a = new List<object?> { r.Ma, r.Ten, r.KkKien };
                if (hasMv) a.AddRange(new object?[] { r.NhapKien, r.XuatKien, r.NhapKg, r.XuatKg });
                a.AddRange(new object?[] { r.TonKien, r.DKien, r.KkKg, r.TonKg, r.DKg, r.Status, r.Nn?.Label, string.Join("; ", r.Notes) });
                return a.ToArray();
            }),
            $"ĐỐI CHIẾU KIỂM KÊ — TỒN KHO AN AN · {src}" + (hasMv ? $" · số kiểm kê đã quy về ngày chốt {ChotDate}" : "") + $" · xuất {DateTime.Now:dd/MM/yyyy HH:mm}");
        if (T.HasKT)
            Table(wb.AddWorksheet("Doi chieu ke toan"), 3, new[] { "Mã hàng", "Tên hàng", "Kiểm kê (kg)", "Kho gửi (kg)", "Tổng (kg)", "Kế toán (Net kg)", "Tổng − Kế toán (kg)", "Kết quả", "Ghi chú" },
                T.Rows.Where(r => r.KtStatus.Length > 0 || r.KtKg is not null).Select(r => new object?[] { r.Ma, r.Ten, r.ChotKg, r.GuiKg, r.TongKg, r.KtKg, r.TongKt, r.KtStatus, string.Join("; ", r.Notes) }),
                $"ĐỐI CHIẾU KIỂM KÊ — KẾ TOÁN · {KtInfo}" + (GuiOn ? " · Tổng = kiểm kê + tồn kho gửi" : ""));
        if (GuiOn && Gui.Detail.Count > 0)
            Table(wb.AddWorksheet("Kho gui"), 3, new[] { "Mã kho gửi", "Mã 21 ký tự", "Cách đổi", "Tên hàng", "Số lô", "Size", "Số kiện", "Trọng lượng (kg)", "Ghi chú" },
                Gui.Detail.Select(x => new object?[] { x.MaGui, x.Ma.Length == 21 ? x.Ma : "", x.Via == "tay" ? "Bảng chuyển đổi" : x.Via == "auto" ? "Tự động · " + x.Rule : "Không đổi được", x.Ten, x.Lo, x.Size, x.Kien, x.Kg, x.Why }),
                $"TỒN KHO GỬI (đổi mã 19 → 21 ký tự) · {GuiInfo} · không đổi được: {Gui.Bad.Count} dòng / {Gui.KgBad:#,0.##} kg (không tính vào tổng)");
        if (hasMv)
            Table(wb.AddWorksheet("Phat sinh"), 3, new[] { "Ngày", "Loại", "Mã hàng", "Vị trí", "Số kiện", "Trang", "Dòng", "Xử lý", "Ghi chú" },
                Adj.List.Select(x => new object?[] { x.M.Ngay.Length > 0 ? x.M.Ngay : x.M.NgayRaw, x.M.Type == "x" ? "Xuất" : "Nhập", x.M.Ma, x.M.Vt, x.M.Kien, x.M.Page, x.M.Dong, x.Label, x.Note.Trim() }),
                $"PHÁT SINH NHẬP / XUẤT SAU NGÀY ĐẾM · ngày chốt {(PhatSinh.IsoOk(ChotDate) ? ChotDate : "(chưa đặt)")}");
        Table(wb.AddWorksheet("Chi tiet kiem ke"), 3, new[] { "Ngày", "Trang", "Dòng", "Vị trí", "Mã hàng", "Tên hàng", "Hợp đồng", "Lô", "Số kiện", "Kg" },
            Lines.Select(x => new object?[] { x.Ngay, x.Page, x.Row, x.Vt, x.Ma, x.Ten, x.Hd, x.Lo, x.Kien, x.Kg }), $"CHI TIẾT SỐ LIỆU KIỂM KÊ · {Chosen.Count} trang");
        if (Vt is not null)
            Table(wb.AddWorksheet("Theo vi tri"), 3, new[] { "Vị trí", "Kết quả", "Mã hàng", "Lô", "Sổ sách (kiện)", "Kiểm kê (kiện)", "Chênh", "Ghi chú" },
                Vt.Cells.Values.Where(c => c.St is VtLech or VtThua or VtChua).OrderBy(c => c.P.Code, StringComparer.Ordinal)
                    .SelectMany(c => c.Items.Count == 0 ? new[] { new object?[] { c.P.Code, VtText(c.St), "", "", c.Exp, c.Cnt, R2(c.Cnt - c.Exp), "" } }
                        : c.Items.Where(i => c.St != VtLech || Math.Abs(i.Diff) > VtTol).Select(i => new object?[] { c.P.Code, VtText(c.St), i.Ma, i.Lo, i.Exp, i.Cnt, i.Diff, i.Note })),
                "ĐỐI CHIẾU THEO VỊ TRÍ — vị trí chưa đếm / lệch / thừa");
        wb.SaveAs(path);
        Log("xuất báo cáo Excel", Path.GetFileName(path));
    }
    /// <summary>Phiếu đếm Excel: mỗi lệnh (dãy) 1 sheet — vị trí · mã · lô · sổ sách · cột trống để ghi số đếm.</summary>
    public void ExportOrders(string path, List<Order> orders, bool withBook)
    {
        RequireExport();
        if (!_sodo.Loaded) throw new BusinessException("Chưa đọc được Sơ đồ kho / Tồn theo vị trí.");
        using var wb = new XLWorkbook(); var used = new HashSet<string>();
        foreach (var o in orders)
        {
            var nm = $"{SoDoCore.KhoLabel(o.Kho).Replace("Kho ", "K")} {o.Khu}{SoDoCore.Two(o.D)}"; var name = nm; var i = 2; while (!used.Add(name.ToLowerInvariant())) name = nm + " " + i++;
            var ws = wb.AddWorksheet(name);
            var rows = SoDoCore.KkRows(_sodo.B.Pos, o.Kho, o.Khu, o.D);
            Table(ws, 3, withBook ? new[] { "Vị trí", "Mã hàng", "Lô", "Sổ sách", "Số đếm", "Ghi chú" } : new[] { "Vị trí", "Mã hàng", "Lô", "Số đếm", "Ghi chú" },
                rows.Select(r => withBook
                    ? new object?[] { r.Code, r.Known ? _sodo.ShowMa(r.Ma, r.Ten, true) : "", r.Lo, r.Known ? r.Sl0 : null, null, null }
                    : new object?[] { r.Code, r.Known ? _sodo.ShowMa(r.Ma, r.Ten, true) : "", r.Lo, null, null }),
                $"PHIẾU ĐẾM — {SoDoCore.KhoLabel(o.Kho)} · Khu {o.Khu} · Dãy {SoDoCore.Two(o.D)}" + (o.Nguoi.Length > 0 ? " · Người đếm: " + o.Nguoi : ""));
            ws.Cell(2, 1).Value = $"Ngày đếm: ……/……/………  ·  {o.NExp}/{o.NPos} vị trí có hàng theo sổ sách"; ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Style.Font.FontSize = 13;
            for (var r = 4; r < 4 + rows.Count; r++) ws.Row(r).Height = 22;
            ws.Column(withBook ? 5 : 4).Width = 14; ws.Column(withBook ? 6 : 5).Width = 22;
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        }
        if (orders.Count == 0) wb.AddWorksheet("Trong");
        wb.SaveAs(path);
        Log("xuất phiếu đếm", $"{orders.Count} lệnh — {Path.GetFileName(path)}");
    }
    public SoDoService SoDo => _sodo;
}

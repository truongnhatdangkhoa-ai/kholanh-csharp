using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KhoLanh.Core.Entry;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;
using KhoLanh.Core.ViTri;
using static KhoLanh.Core.SoDo.SoDoCore;

namespace KhoLanh.Core.SoDo;

/// <summary>
/// Module Sơ đồ kho (C#, theo "Sơ đồ kho" — so-do-vi-tri.js của app cũ). Dùng chung dữ liệu với app cũ:
///   • Tồn: database "ton-vi-tri-an-an" (ViTri + ViTriMeta) — CHỈ ĐỌC.
///   • Ghi: database "so-do-vi-tri": CauHinh (dãy / khoang / tầng từng khu), QuyCach (đơn vị kiện / gói, gói/kiện theo mã 21 ký tự),
///     KhoaKK + KhoaKK_D (khóa kiểm kê: số liệu lúc khóa của từng kho / khu / dãy).
/// </summary>
public sealed class SoDoService
{
    public const string Db = "so-do-vi-tri", SheetCfg = "CauHinh", SheetQc = "QuyCach", SheetLock = "KhoaKK", SheetLockD = "KhoaKK_D", PermKey = "m8";

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly EntryService _entry;

    public SoDoService(Func<ISheetSource> source, Session session, AuditService audit, EntryService entry) { _src = source; _session = session; _audit = audit; _entry = entry; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void RequireView() { if (!CanView) throw new BusinessException("Tài khoản không có quyền xem Sơ đồ kho."); }
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa Sơ đồ kho (cấu hình kho, quy cách, khóa kiểm kê)."); }
    private void RequireExport() { if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file Sơ đồ kho."); }
    private string UserName => _session.CurrentUser is { } u ? (u.FullName.Length > 0 ? u.FullName : u.Username) : "";
    private void Log(string action, string detail) => _audit.Log(_session.Username, "Sơ đồ kho: " + action, detail);

    // ------------------------------------------------------------------ trạng thái

    public List<StockRow> Live { get; private set; } = new();         // tồn hiện tại (Tồn theo vị trí)
    public List<StockRow> Rows { get; private set; } = new();         // dùng cho sơ đồ (đã áp khóa kiểm kê)
    public Cfg Config { get; private set; } = DefaultCfg();
    public Dictionary<string, Qc> QcMap { get; private set; } = new();
    public List<Lock> Locks { get; private set; } = new();
    public Dictionary<string, List<StockRow>> Snaps { get; private set; } = new();
    public List<Diff> LockDiff { get; private set; } = new();
    public Built B { get; private set; } = new();
    public string DataDate { get; private set; } = "";
    public string FileName { get; private set; } = "";
    public bool Loaded { get; private set; }
    private Dictionary<string, string> _size = new();
    private AnhXa _ax = AnhXa.Defaults();
    private readonly Dictionary<string, string> _labelCache = new();

    // ------------------------------------------------------------------ đọc / lưu

    private JsonArray Read(string db, string sheet)
    {
        try { return JsonNode.Parse(_src().Read(db, sheet)) as JsonArray ?? new JsonArray(); } catch { return new JsonArray(); }
    }
    private static string S(JsonNode? n) => ViTriService.S(n);
    private static double Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? (double.IsFinite(d) ? d : 0) : (DoiChieu.DcxEngine.ParseNum(S(n)) ?? 0);
    private static JsonNode? JsonOf(string? s)
    {
        var t = (s ?? "").Trim(); if (t.StartsWith("__JSON__", StringComparison.Ordinal)) t = t[8..];
        try { return JsonNode.Parse(t); } catch { return null; }
    }

    public void Load()
    {
        RequireView();
        LoadForRead();
    }
    /// <summary>Đọc dữ liệu không xét quyền Sơ đồ kho — cho Đối chiếu kiểm kê (chỉ đọc).</summary>
    internal void LoadForRead()
    {
        Live = Read(ViTriService.Db, ViTriService.SheetRows).OfType<JsonObject>()
            .Select(o => new StockRow { Ma = S(o["Mã hàng"]), Ten = S(o["Tên hàng"]), Lo = S(o["Lô (Batch)"]).Trim(), Vt = S(o["Vị trí"]), Sl = Num(o["SL Tồn"]), Tl = Num(o["T.Lượng"]) })
            .Where(r => r.Ma.Trim().Length > 0).ToList();
        var meta = Read(ViTriService.Db, ViTriService.SheetMeta);
        var mj = meta.Count > 0 && meta[0] is JsonObject mo ? JsonOf(S(mo["data"])) as JsonObject : null;
        DataDate = S(mj?["dataDate"]); FileName = S(mj?["fileName"]);
        var cf = Read(Db, SheetCfg);
        Config = cf.Count > 0 && cf[0] is JsonObject co && JsonOf(S(co["json"])) is JsonObject cj ? CleanCfg(cj) : DefaultCfg();
        QcMap = new();
        foreach (var r in Read(Db, SheetQc).OfType<JsonObject>())
        {
            var id = S(r["id"]).Trim().ToUpperInvariant(); if (id.Length == 0) continue;
            QcMap[id] = new Qc { Dv = S(r["dv"]) == "gói" ? "gói" : "kiện", Gk = (int)Num(r["gk"]), Saved = true, Ts = S(r["ts"]) };
        }
        Locks = Read(Db, SheetLock).OfType<JsonObject>()
            .Select(r => new Lock { Id = S(r["id"]), Kho = S(r["kho"]).ToUpperInvariant(), Khu = S(r["khu"]).ToUpperInvariant(), D = (int)Num(r["d"]), Ts = S(r["ts"]), By = S(r["by"]), Note = S(r["note"]) })
            .Where(l => l.Id.Length > 0 && l.Kho.Length > 0 && l.Khu.Length > 0).ToList();
        Snaps = Locks.ToDictionary(l => l.Id, _ => new List<StockRow>());
        foreach (var r in Read(Db, SheetLockD).OfType<JsonObject>())
            if (Snaps.TryGetValue(S(r["lk"]), out var a))
                a.Add(new StockRow { Ma = S(r["ma"]), Ten = S(r["ten"]), Lo = S(r["lo"]), Vt = S(r["vt"]), Sl = Num(r["sl"]), Tl = Num(r["tl"]) });
        LoadDictionaries();
        Rebuild();
        Loaded = true;
    }

    /// <summary>Từ điển Size (Mã hóa › 8. Size) + Ánh xạ tên hàng (tiền tố) của Tổng hợp — để ra mã rút gọn và nhóm pallet.</summary>
    private void LoadDictionaries()
    {
        var dict = new Dictionary<string, string>();
        foreach (var e in TongHopService.FixedCatalog().GetValueOrDefault(8) ?? new()) dict[e.Code.Trim().ToUpperInvariant()] = e.Name.Trim();
        foreach (var r in Read(TongHopService.Db, "MaHoa").OfType<JsonObject>())
            if (S(r["Tổ hợp"]) == "8") { var c = S(r["Mã"]).Trim().ToUpperInvariant(); if (c.Length > 0) dict[c] = S(r["Tên"]).Trim(); }
        _size = dict;
        AnhXa? ax; try { ax = AnhXa.FromSheets(Read(TongHopService.Db, "AnhXaTen"), Read(TongHopService.Db, "AnhXaCauHinh")); } catch { ax = null; }
        _ax = ax ?? AnhXa.Defaults();
        _labelCache.Clear();
    }

    /// <summary>1.73 (K2): mã mọi vị trí theo Sơ đồ kho (cấu hình + vị trí có trong dữ liệu) — không xét quyền, cho Nhập liệu báo "vị trí lạ".
    /// Rỗng khi chưa có cấu hình lẫn dữ liệu vị trí.</summary>
    public HashSet<string> AllCodes()
    {
        if (!Loaded) LoadForRead();
        return B.Pos.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
    }

    public void Rebuild()
    {
        (Rows, LockDiff) = ApplyLocks(Live, Locks, Snaps);
        B = Build(Rows, Config, QcOf, GrpOf);
    }

    public Qc? QcOf(string ma) => QcMap.GetValueOrDefault(KeyMa(ma));
    public string DvOf(string ma) => QcOf(ma)?.Dv ?? "kiện";

    /// <summary>(tiền tố, size, nhãn) theo quy tắc size-label.js: mã phải đủ 21 ký tự, size từ ký tự 14–17 (hoặc NN/NNN trong tên).</summary>
    public (string Pre, string Size, string Tag) SizeParts(string ma, string ten)
    {
        var m = KeyMa(ma); if (m.Length != 21) return ("", "", "");
        var code = m.Substring(13, 4); string size;
        if (code == "XXXX") size = "";
        else if (_size.TryGetValue(code, out var nm)) size = Regex.Replace(nm.Trim(), @"^size\s+", "", RegexOptions.IgnoreCase).Trim();
        else { var mm = Regex.Match(ten ?? "", @"\d{1,3}\s*\/\s*\d{1,3}"); size = mm.Success ? Regex.Replace(mm.Value, @"\s+", "") : ""; }
        if (size.Length == 0) return ("", "", "");
        var pre = ""; try { pre = _ax.ShortPrefix(m); } catch { pre = ""; }
        return (pre, size, pre.Length > 0 ? pre + " " + size : size);
    }
    /// <summary>Mã rút gọn: "BTP 31/40 (1.8kg x6)"; không quy ra được → "".</summary>
    public string Label(string ma, string ten)
    {
        if (_labelCache.TryGetValue(ma, out var c)) return c;
        var p = SizeParts(ma, ten); var g = GoiY(ten);
        var s = p.Tag.Length > 0 ? p.Tag + (g is { } gy ? " (" + gy.Text + ")" : "") : "";
        _labelCache[ma] = s; return s;
    }
    /// <summary>Nhóm dùng chung pallet = cùng loại hàng + cùng size; không xác định → "" (mỗi mã 1 nhóm).</summary>
    public string GrpOf(string ma, string ten) { var p = SizeParts(ma, ten); return p.Size.Length > 0 ? (p.Pre.Length > 0 ? p.Pre : "?") + "|" + p.Size : ""; }
    public static string GrpName(string g) => g.Replace('|', ' ');

    public List<string> KhuList(string kho) => (Config.GetValueOrDefault(kho) ?? new()).Select(e => e.Khu).ToList();
    public List<string> KhoList() => B.Cfg.Keys.ToList();
    public bool HasZ(string kho) => B.Pos.Any(p => p.Kho == kho && p.Z);
    public List<Pos> Scope(string kho, string khu) => B.Pos.Where(p => p.Kho == kho && (khu == "ALL" ? !p.Z : p.Khu == khu)).ToList();
    public Lock? PosLock(Pos p) => Locks.FirstOrDefault(x => x.Kho == p.Kho && x.Khu == (p.Z ? "Z" : p.Khu) && (x.D == 0 || x.D == p.D));
    public bool IsLocked(string kho, string? khu = null) => Locks.Any(x => x.Kho == kho && (khu is null || x.Khu == khu));
    public static string LockName(Lock x) => KhoLabel(x.Kho) + " · " + (x.Khu == "Z" ? "Chưa có vị trí" : "Khu " + x.Khu) + (x.D > 0 ? " · Dãy " + x.D : "");

    // ------------------------------------------------------------------ ghi: cấu hình / quy cách / khóa

    public void SaveConfig(Cfg c)
    {
        RequireEdit();
        var clean = CleanCfg(CfgJson(c));
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetCfg] = new JsonArray(new JsonObject { ["id"] = "cfg", ["json"] = "__JSON__" + CfgJson(clean).ToJsonString() }) });
        Config = clean; Rebuild();
        Log("lưu cấu hình kho", string.Join("; ", clean.Select(k => k.Key + ": " + string.Join(",", k.Value.Select(e => $"{e.Khu} {e.D}x{e.K}")))));
    }

    /// <summary>Lưu quy cách cho nhiều mã (đơn vị kiện / gói, gói/kiện). Mã gói cần gói/kiện ≥ 1.</summary>
    public int SaveQc(IEnumerable<(string Ma, string Dv, int Gk)> items)
    {
        RequireEdit();
        var next = QcMap.ToDictionary(k => k.Key, k => k.Value); var n = 0; var ts = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        foreach (var (ma, dv, gk) in items)
        {
            var key = KeyMa(ma); if (key.Length == 0) continue;
            var d = dv == "gói" ? "gói" : "kiện";
            if (d == "gói" && gk < 1) throw new BusinessException($"Mã {key}: tính theo gói thì phải nhập số gói / kiện.");
            next[key] = new Qc { Dv = d, Gk = Math.Max(0, gk), Saved = true, Ts = ts }; n++;
        }
        if (n == 0) return 0;
        var arr = new JsonArray(next.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => (JsonNode)new JsonObject
        {
            ["id"] = k.Key, ["ma"] = k.Key, ["dv"] = k.Value.Dv, ["gk"] = k.Value.Gk > 0 ? k.Value.Gk : "", ["ts"] = k.Value.Ts,
        }).ToArray());
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetQc] = arr });
        QcMap = next; Rebuild();
        Log("lưu quy cách", $"{n} mã");
        return n;
    }

    private void SaveLocks(List<Lock> locks, Dictionary<string, List<StockRow>> snaps)
    {
        var head = new JsonArray(locks.Select(x => (JsonNode)new JsonObject
        {
            ["id"] = x.Id, ["kho"] = x.Kho, ["khu"] = x.Khu, ["d"] = x.D, ["ts"] = x.Ts, ["by"] = x.By, ["note"] = x.Note, ["n"] = snaps.GetValueOrDefault(x.Id)?.Count ?? 0,
        }).ToArray());
        var det = new JsonArray();
        foreach (var x in locks)
        {
            var i = 0;
            foreach (var r in snaps.GetValueOrDefault(x.Id) ?? new())
                det.Add(new JsonObject { ["id"] = x.Id + "#" + i++, ["lk"] = x.Id, ["ma"] = r.Ma, ["ten"] = r.Ten, ["lo"] = r.Lo, ["vt"] = r.Vt, ["sl"] = r.Sl, ["tl"] = r.Tl });
        }
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetLock] = head, [SheetLockD] = det });
    }
    /// <summary>Khóa kiểm kê các phần (kho, khu, dãy — dãy 0 = cả khu): chụp số liệu hiện tại, từ đó phần này giữ nguyên tới khi mở khóa.</summary>
    public List<Lock> AddLocks(IEnumerable<(string Kho, string Khu, int D)> parts, string note)
    {
        RequireEdit();
        var list = parts.Distinct().ToList();
        list = list.Where(p => p.D == 0 || !list.Contains((p.Kho, p.Khu, 0))).ToList();   // đã khóa cả khu thì bỏ dãy lẻ
        list = list.Where(p => !Locks.Any(x => x.Kho == p.Kho && x.Khu == p.Khu && (x.D == 0 || x.D == p.D))).ToList();
        if (list.Count == 0) throw new BusinessException("Chưa chọn phần nào để khóa (hoặc đã khóa rồi).");
        var ts = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture); var bas = DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture);
        var added = new List<Lock>(); var snaps = new Dictionary<string, List<StockRow>>(Snaps);
        var i = 0;
        foreach (var p in list.OrderBy(p => p.Kho).ThenBy(p => p.Khu).ThenBy(p => p.D))
        {
            var l = new Lock { Id = "k" + bas + i++, Kho = p.Kho, Khu = p.Khu, D = p.D, Ts = ts, By = UserName, Note = note.Trim() };
            added.Add(l); snaps[l.Id] = RowsOf(Live, p.Kho, p.Khu, p.D).Select(r => r.Clone()).ToList();
        }
        var locks = Locks.Concat(added).ToList();
        SaveLocks(locks, snaps);
        Locks = locks; Snaps = snaps; Rebuild();
        Log("khóa kiểm kê", string.Join(", ", added.Select(LockName)));
        return added;
    }
    public void Unlock(string id)
    {
        RequireEdit();
        var l = Locks.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không thấy khóa này (vừa mở ở máy khác?).");
        var locks = Locks.Where(x => x.Id != id).ToList(); var snaps = new Dictionary<string, List<StockRow>>(Snaps); snaps.Remove(id);
        SaveLocks(locks, snaps);
        Locks = locks; Snaps = snaps; Rebuild();
        Log("mở khóa kiểm kê", LockName(l));
    }

    // ------------------------------------------------------------------ xuất Excel danh sách vị trí

    public void ExportList(string path, List<Pos> list)
    {
        RequireExport();
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Vi tri kho");
        var head = new[] { "Vị trí", "Kho", "Khu", "Dãy", "Tầng", "Khoang", "Trạng thái", "Mã hàng", "Mã rút gọn", "Tên hàng", "Lô", "Số lượng", "Đơn vị", "Net (kg)", "Tổng SL vị trí", "Pallet vị trí", "Pallet tầng", "Số khoang", "Vượt mức" };
        for (var c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
        var r = 2;
        foreach (var p in list)
        {
            B.Tang.TryGetValue(p.TangKey, out var m);
            void Base()
            {
                ws.Cell(r, 1).Value = p.Code; ws.Cell(r, 2).Value = KhoLabel(p.Kho); ws.Cell(r, 3).Value = p.Khu; ws.Cell(r, 4).Value = p.D; ws.Cell(r, 5).Value = p.T; ws.Cell(r, 6).Value = p.K;
                ws.Cell(r, 7).Value = StatusLabel(Status(p));
                ws.Cell(r, 15).Value = string.Join(" + ", p.Tot.Select(t => t.Sl.ToString(CultureInfo.InvariantCulture) + " " + t.Dv));
                if (!p.Z) ws.Cell(r, 16).Value = p.Pal;
                if (m is not null) { ws.Cell(r, 17).Value = m.Used; ws.Cell(r, 18).Value = m.Nk; }
                ws.Cell(r, 19).Value = IsOver(p, B) ? "Có" : "";
            }
            if (p.It.Count == 0) { Base(); r++; }
            else foreach (var x in p.It)
            {
                Base();
                ws.Cell(r, 8).Value = x.Ma; ws.Cell(r, 9).Value = Label(x.Ma, x.Ten); ws.Cell(r, 10).Value = x.Ten; ws.Cell(r, 11).Value = x.Lo;
                ws.Cell(r, 12).Value = x.Sl; ws.Cell(r, 13).Value = x.Dv; ws.Cell(r, 14).Value = x.Net; r++;
            }
        }
        var rng = ws.Range(1, 1, Math.Max(1, r - 1), head.Length);
        rng.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; rng.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Row(1).Style.Font.Bold = true; ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8E8E8");
        ws.SheetView.FreezeRows(1); ws.Columns().AdjustToContents(1, Math.Min(r, 400));
        wb.SaveInPlace(path);
        Log("xuất Excel danh sách vị trí", Path.GetFileName(path));
    }

    // ------------------------------------------------------------------ Phiếu In vị trí (kiểm kê)

    public sealed class KkLine
    {
        public string Code = "", Pos = "", G = "", Lb = "", Mv = "", M0 = "", T0 = "", Ma = "", Tl = "", Sl = "", Ghi = "", Lo = "";
        public bool First, Known;
        public KkLine Clone() => (KkLine)MemberwiseClone();
    }
    public sealed class KkPage { public int N; public List<KkLine> Rows = new(); }
    public sealed class KkSheet
    {
        public string Kho = "", Khu = "", Mode = "so"; public int D;   // D = 0: tất cả dãy
        public List<KkPage> Pages = new(); public bool Dirty, Out;
        private readonly Stack<(List<KkPage> p, bool d)> _hist = new();
        public int History => _hist.Count;
        public void Snap() { Out = false; _hist.Push((Copy(Pages), Dirty)); }
        public bool Undo() { if (_hist.Count == 0) return false; var (p, d) = _hist.Pop(); Pages = p; Dirty = d; return true; }
        public void DropSnap() { if (_hist.Count > 0) _hist.Pop(); }
        private static List<KkPage> Copy(List<KkPage> pg) => pg.Select(p => new KkPage { N = p.N, Rows = p.Rows.Select(r => r.Clone()).ToList() }).ToList();
        public IEnumerable<(int Pi, int Ri, KkLine L)> All() { for (var i = 0; i < Pages.Count; i++) for (var j = 0; j < Pages[i].Rows.Count; j++) yield return (i, j, Pages[i].Rows[j]); }
        public int MoveCount => Pages.Sum(p => p.Rows.Count(r => r.Mv.Length > 0));
        /// <summary>Phiếu "Vị trí có hàng" (so) không hiện / in dòng vị trí trống — các dòng đó vẫn giữ trong phiếu làm đích cho "Chuyển" (chuyển vào thì hiện).</summary>
        public bool Shown(KkLine l) => !(Mode == "so" && l.Ma == "trống");
        public IEnumerable<(int Pi, int Ri, KkLine L)> Visible() => All().Where(x => Shown(x.L));
        /// <summary>Phiếu để ghi tay: "trong" = mọi vị trí, "rong" = chỉ vị trí trống (không có hàng) theo sơ đồ.</summary>
        public bool Blank => Mode is "trong" or "rong";
    }
    public List<int> DayList(string kho, string khu) { var e = (Config.GetValueOrDefault(kho) ?? new()).FirstOrDefault(x => x.Khu == khu); return e is null ? new() : Enumerable.Range(1, e.D).ToList(); }
    private static string Nf(double v, int d = 2) => v.ToString(d == 3 ? "#,0.###" : "#,0.##", CultureInfo.InvariantCulture);
    public string ShowMa(string ma, string ten, bool rut) => rut && Label(ma, ten) is { Length: > 0 } l ? l : ma;

    /// <summary>Dựng phiếu: theo sổ sách (mỗi mã 1 dòng, ô trống 1 dòng "trống", kèm đề xuất chuyển hàng lẫn size) hoặc phiếu trống (mỗi ô 1 dòng).</summary>
    public KkSheet BuildKk(string kho, string khu, int d, string mode, bool rut = true)
    {
        var k = new KkSheet { Kho = kho, Khu = khu, D = d, Mode = mode };
        var days = d == 0 ? DayList(kho, khu) : new List<int> { d };
        var taken = new HashSet<string>(); var sugs = new Dictionary<string, Dictionary<string, string>>();
        var e = (Config.GetValueOrDefault(kho) ?? new()).FirstOrDefault(x => x.Khu == khu);
        foreach (var n in days)
        {
            var pg = new KkPage { N = n };
            if (mode == "rong")
            {
                foreach (var r in KkRows(B.Pos, kho, khu, n)) if (!r.Known) pg.Rows.Add(new KkLine { Code = r.Code, Pos = r.Code, First = true });
            }
            else if (mode == "trong")
            {
                if (e is not null) for (int t = 1, nt = TangOf(e, n); t <= nt; t++) for (var kk = 1; kk <= e.K; kk++) { var c = CodeOf(kho, khu, n, t, kk); pg.Rows.Add(new KkLine { Code = c, Pos = c, First = true }); }   // 1.71: ghi sẵn mã vị trí như phiếu "vị trí trống"
            }
            else
                foreach (var r in KkRows(B.Pos, kho, khu, n))
                {
                    var q = r.Known ? QcOf(r.Ma) : null; var g = r.Known ? (GrpOf(r.Ma, r.Ten) is { Length: > 0 } gg ? gg : KeyMa(r.Ma)) : "";
                    if (r.Known && !sugs.ContainsKey(r.Code)) sugs[r.Code] = MoveSug(B.ByCode.GetValueOrDefault(r.Code), B.Pos, taken, GrpOf);
                    pg.Rows.Add(new KkLine
                    {
                        Code = r.Code, Pos = r.Code, G = g, Lb = r.Known ? Label(r.Ma, r.Ten) : "", Mv = r.Known ? sugs[r.Code].GetValueOrDefault(g) ?? "" : "", First = r.First, Known = r.Known,
                        M0 = r.Known ? r.Ma : "", T0 = r.Known ? r.Ten : "", Lo = r.Lo, Ma = r.Known ? ShowMa(r.Ma, r.Ten, rut) : "trống",
                        Tl = r.Known && r.Tl != 0 ? Nf(r.Tl, 3) + " kg" : "", Sl = r.Known ? Nf(r.Sl0) + " " + (q?.Dv ?? "kiện") : "",
                    });
                }
            k.Pages.Add(pg);
        }
        return k;
    }
    private static (double N, string U)? SlParse(string? v)
    {
        var m = Regex.Match(v ?? "", @"^\s*(-?[\d.,]+)\s*(.*?)\s*$"); if (!m.Success) return null;
        return double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? (n, m.Groups[2].Value) : null;
    }
    /// <summary>Trong 1 vị trí: các dòng cùng nhãn size + cùng trọng lượng + cùng đơn vị → số dòng có thể gộp.</summary>
    public static int MergeCount(KkSheet k) => MergeGroups(k).Sum(g => g.Count - 1);
    private static List<List<KkLine>> MergeGroups(KkSheet k)
    {
        var o = new List<List<KkLine>>();
        foreach (var pg in k.Pages)
        {
            var map = new Dictionary<string, List<KkLine>>(); var order = new List<string>();
            foreach (var r in pg.Rows)
            {
                if (r.Ma.Length == 0 || r.Ma == "trống" || r.Code.Length == 0 || SlParse(r.Sl) is not { } q) continue;
                var key = r.Code + "|" + (r.Lb.Length > 0 ? r.Lb : r.Ma) + "|" + r.Tl + "|" + q.U;
                if (!map.TryGetValue(key, out var l)) { map[key] = l = new(); order.Add(key); }
                l.Add(r);
            }
            foreach (var key in order) if (map[key].Count > 1) o.Add(map[key]);
        }
        return o;
    }
    public static int Merge(KkSheet k)
    {
        var n = 0;
        foreach (var g in MergeGroups(k))
        {
            var pg = k.Pages.First(p => p.Rows.Contains(g[0])); var first = g[0]; double tot = 0; var u = ""; var notes = new List<string>();
            foreach (var r in g) { var q = SlParse(r.Sl)!.Value; tot += q.N; u = q.U; if (r.Ghi.Length > 0 && !notes.Contains(r.Ghi)) notes.Add(r.Ghi); }
            first.Sl = Nf(tot) + (u.Length > 0 ? " " + u : ""); first.Ghi = string.Join(" · ", notes);
            for (var j = 1; j < g.Count; j++) if (pg.Rows.Remove(g[j])) n++;
        }
        if (n > 0) k.Dirty = true;
        return n;
    }
    /// <summary>Chuyển nhóm hàng (cùng vị trí gốc + cùng nhóm) của dòng sang dòng "trống" đề xuất — chỉ trên phiếu.</summary>
    public static bool Move(KkSheet k, KkLine src)
    {
        var to = src.Mv; if (to.Length == 0) return false;
        var pg = k.Pages.FirstOrDefault(p => p.Rows.Contains(src)); if (pg is null) return false;
        var rows = pg.Rows.Where(r => r.Pos == src.Pos && r.G == src.G && r.Mv == to).ToList();
        var tp = k.Pages.FirstOrDefault(p => p.Rows.Any(r => r.Code == to && r.Ma == "trống")); if (tp is null) return false;
        foreach (var r in rows) pg.Rows.Remove(r);
        foreach (var r in rows) { r.Ghi = (r.Ghi.Length > 0 ? r.Ghi + " · " : "") + "chuyển từ " + r.Pos; r.Code = to; r.Pos = to; r.Mv = ""; }
        var at = tp.Rows.FindIndex(r => r.Code == to && r.Ma == "trống");
        tp.Rows.RemoveAt(at); tp.Rows.InsertRange(at, rows);
        k.Dirty = true; return true;
    }
    public static int MoveAll(KkSheet k)
    {
        var n = 0;
        for (var guard = 0; guard < 500; guard++)
        {
            var f = k.All().FirstOrDefault(x => x.L.Mv.Length > 0).L; if (f is null || !Move(k, f)) break; n++;
        }
        return n;
    }

    /// <summary>Excel phiếu in vị trí: mỗi dãy 1 sheet, Times New Roman 15, viền sẵn, A4 đứng vừa 1 trang ngang.</summary>
    public void ExportKk(string path, KkSheet k)
    {
        RequireExport();
        using var wb = new XLWorkbook();
        var used = new HashSet<string>();
        var ngay = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        foreach (var pg in k.Pages)
        {
            var lines = pg.Rows.Where(k.Shown).ToList();
            if (lines.Count == 0 && k.Pages.Count > 1) continue;
            var name = "Day " + Two(pg.N); var nm = name; var i = 2; while (!used.Add(nm.ToLowerInvariant())) nm = name + " " + i++;
            var ws = wb.AddWorksheet(nm);
            ws.Style.Font.FontName = "Times New Roman"; ws.Style.Font.FontSize = 15;
            var title = $"PHIẾU IN VỊ TRÍ — {KhoLabel(k.Kho)} · Khu {k.Khu} · Dãy {Two(pg.N)}";
            var sub = "Ngày: " + ngay + " · " + (k.Mode == "rong" ? "Vị trí trống — ghi tay" : k.Mode == "trong" ? "Phiếu trống" : "Theo sổ sách" + (DataDate.Length > 0 ? " (dữ liệu ngày " + DataDate + ")" : ""));
            ws.Cell(1, 1).Value = title; ws.Range(1, 1, 1, 5).Merge(); ws.Cell(1, 1).Style.Font.Bold = true; ws.Row(1).Height = 24;
            ws.Cell(2, 1).Value = sub; ws.Range(2, 1, 2, 5).Merge(); ws.Row(2).Height = 21;
            var head = new[] { "Vị trí", "Mã hàng", "Trọng lượng", "Số lượng", "Ghi chú" };
            for (var c = 0; c < 5; c++) { var h = ws.Cell(3, c + 1); h.Value = head[c]; h.Style.Font.Bold = true; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8E8E8"); h.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; }
            ws.Row(3).Height = 24;
            var r = 4;
            foreach (var x in lines)
            {
                ws.Cell(r, 1).SetValue(x.Code); ws.Cell(r, 2).SetValue(x.Ma == "trống" ? "" : x.Ma); ws.Cell(r, 3).SetValue(x.Tl); ws.Cell(r, 4).SetValue(x.Sl); ws.Cell(r, 5).SetValue(x.Ghi);
                ws.Row(r).Height = k.Blank ? 30 : 21; r++;
            }
            var last = Math.Max(3, r - 1);
            var tbl = ws.Range(3, 1, last, 5);
            tbl.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; tbl.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range(4, 3, last, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Range(4, 1, last, 5).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            if (k.Blank) { ws.Column(1).Width = 20; for (var c = 2; c <= 5; c++) ws.Column(c).Width = 19; }
            else { ws.Columns(1, 5).AdjustToContents(3, last); foreach (var col in ws.Columns(1, 5)) col.Width = Math.Max(col.Width * 1.1, 10); ws.Column(5).Width = Math.Max(ws.Column(5).Width, 16); }
            ws.SheetView.FreezeRows(3);
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper; ws.PageSetup.PageOrientation = XLPageOrientation.Portrait; ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.Margins.Left = 0.4; ws.PageSetup.Margins.Right = 0.4; ws.PageSetup.Margins.Top = 0.5; ws.PageSetup.Margins.Bottom = 0.6;
            ws.PageSetup.Footer.Center.AddText("Trang ").SetFontName("Times New Roman"); ws.PageSetup.Footer.Center.AddText(XLHFPredefinedText.PageNumber); ws.PageSetup.Footer.Center.AddText(" / "); ws.PageSetup.Footer.Center.AddText(XLHFPredefinedText.NumberOfPages);
            ws.PageSetup.SetRowsToRepeatAtTop(3, 3);
        }
        if (wb.Worksheets.Count == 0) wb.AddWorksheet("Trong");
        wb.SaveInPlace(path);
        k.Out = true;
        Log("xuất phiếu in vị trí", Path.GetFileName(path));
    }

    // ------------------------------------------------------------------ Nhập liệu kiểm kê + rà soát chênh lệch

    public static string NlName(string kho, string khu, int n) => $"{KhoLabel(kho)} {khu} dãy {Two(n)}";
    private static readonly Regex ReLo = new(@"(?:^|·|\s)lô\s+(\S+)", RegexOptions.IgnoreCase);

    /// <summary>Tạo trang Nhập liệu (mẫu Kiểm kê) cho 1 dãy: số lượng điền sẵn theo sổ sách để nhập số thực.</summary>
    public EntryService.PageInfo ToNhapLieu(string kho, string khu, int n)
    {
        RequireView();
        var rows = KkRows(B.Pos, kho, khu, n);
        if (rows.Count == 0) throw new BusinessException("Dãy này không có vị trí.");
        var tpl = EntryService.BuiltIn.First(t => t.Id == "kiem-ke");
        var name = NlName(kho, khu, n) + " " + DateTime.Today.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var page = _entry.Create(name, tpl);
        // 1.62: điền theo TÊN cột (mẫu Kiểm kê mới: Vị trí, Mã hàng, Size, Trọng lượng, Số lượng, Net(kg), Ghi chú, Ngày nhập; mẫu cũ: Tên hàng, Số thùng, Kg)
        var hs = page.Columns.Select(c => KhoLanh.Core.Grid.GridFilter.Fold(c.Header).Trim()).ToList();
        int H(params string[] names) => hs.FindIndex(h => names.Contains(h));
        int cVt = H("vi tri"), cMa = H("ma hang"), cTen = H("ten hang"), cSz = H("size"), cSl = H("so luong", "so thung"), cTl = H("trong luong", "kg"), cGc = H("ghi chu"), cNg = H("ngay nhap");
        var today = EntryRules.TodayText(DateTime.Today);
        var data = rows.Select(r =>
        {
            var a = new string[page.Columns.Count]; Array.Fill(a, "");
            void Put(int c, string v) { if (c >= 0) a[c] = v; }
            Put(cVt, r.Code);
            if (r.Known)
            {
                Put(cMa, r.Ma); Put(cTen, r.Ten); Put(cSz, SizeParts(r.Ma, r.Ten).Size);
                Put(cSl, r.Sl0.ToString("R", CultureInfo.InvariantCulture));
                if (r.Tl != 0) Put(cTl, r.Tl.ToString("R", CultureInfo.InvariantCulture));
                if (r.Lo.Length > 0) Put(cGc, "lô " + r.Lo);
            }
            Put(cNg, today);
            return (IReadOnlyList<string>)a;
        }).ToList();
        page = _entry.Save(page, page.Columns, data);
        Log("tạo trang Nhập liệu kiểm kê", name);
        return page;
    }

    public sealed record ReviewResult(string PageName, int PageRows, List<KkRow> Rows, Review R);
    /// <summary>Đọc trang Nhập liệu mới nhất của dãy (tên bắt đầu "Kho 1 A dãy 03") → so với sổ sách.</summary>
    public ReviewResult ReviewDay(string kho, string khu, int n)
    {
        RequireView();
        var pre = NlName(kho, khu, n);
        var page = _entry.Pages().Where(p => p.Name.StartsWith(pre, StringComparison.Ordinal)).OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).FirstOrDefault()
            ?? throw new BusinessException($"Chưa có trang Nhập liệu tên bắt đầu bằng “{pre}”. Bấm “Chuyển sang Nhập liệu” trước, nhập số thực rồi quay lại.");
        var cols = page.Columns.Select(c => c.Header).ToList();
        int Col(string h) => cols.FindIndex(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase));
        int cVt = Col("Vị trí"), cMa = Col("Mã hàng"), cSl = Col("Số lượng còn lại") is >= 0 and var sc ? sc : Col("Số lượng") is >= 0 and var sl ? sl : Col("Số thùng"), cKg = Col("Trọng lượng") is >= 0 and var tl ? tl : Col("Kg"), cGc = Col("Ghi chú");
        if (cVt < 0 || cMa < 0 || cSl < 0) throw new BusinessException($"Trang “{page.Name}” không có đủ cột Vị trí, Mã hàng, Số lượng.");
        var dat = _entry.Load(page);
        var bas = KkRows(B.Pos, kho, khu, n); var idx = new Dictionary<string, KkRow>();
        foreach (var r in bas) { r.Sl = null; idx.TryAdd(r.Code + "|" + (r.Known ? KeyMa(r.Ma) : "") + "|" + (r.Known ? r.Lo : ""), r); }
        var extra = new List<KkRow>();
        string At(string[] row, int c) => c >= 0 && c < row.Length ? row[c].Trim() : "";
        foreach (var o in dat)
        {
            var code = Parse(At(o, cVt))?.Code ?? At(o, cVt); if (code.Length == 0) continue;
            var ma = At(o, cMa); var lo = ReLo.Match(At(o, cGc)) is { Success: true } lm ? lm.Groups[1].Value : "";
            double? sk = EntryService.TryNumber(At(o, cSl), out var v) ? v : null;
            if (ma.Length > 0 && (idx.GetValueOrDefault(code + "|" + KeyMa(ma) + "|" + lo) ?? bas.FirstOrDefault(x => x.Known && x.Code == code && KeyMa(x.Ma) == KeyMa(ma) && x.Sl is null)) is { } r) r.Sl = sk;
            else if (ma.Length == 0) { var e = bas.FirstOrDefault(x => x.Code == code && !x.Known); if (e is not null && sk is > 0) e.Sl = sk; }
            else if (sk is not null) extra.Add(new KkRow { Code = code, Ma = ma, Lo = lo, Sl = sk, Tl = EntryService.TryNumber(At(o, cKg), out var kg) ? kg : 0 });
        }
        var all = bas.Concat(extra).ToList();
        var rv = ReviewOf(all, key => B.Tang.TryGetValue(key, out var m) ? m.Nk : 99, GrpOf);
        return new ReviewResult(page.Name, dat.Count, all, rv);
    }
    // ------------------------------------------------------------------ 1.72: kết quả 1 trang kiểm kê trên sơ đồ

    /// <summary>Các trang Nhập liệu dạng kiểm kê có cột Vị trí (mới sửa trước).</summary>
    public List<EntryService.PageInfo> KkPages()
    {
        RequireView();
        return _entry.PagesForRead().Where(p => p.Template is not ("nhap-kho" or "xuat-kho" or "trong"))
            .Where(p => { var h = p.Columns.Select(c => c.Header).ToList(); return KiemKe.KiemKeCore.IsKiemKe(h) && KiemKe.KiemKeCore.PageCols(h).ContainsKey("vt"); })
            .OrderByDescending(p => p.UpdatedAt, StringComparer.Ordinal).ToList();
    }

    /// <summary>So 1 trang kiểm kê với sổ theo vị trí (sơ đồ đang nạp, đã áp khóa kiểm kê). Số lượng trên trang tính bằng KIỆN, sổ sách mã đơn vị gói đổi ra kiện theo Quy cách — như Đối chiếu kiểm kê › Theo vị trí.</summary>
    public KiemKe.KiemKeCore.KkMap KkMapOf(string pageId)
    {
        RequireView();
        var p = _entry.PagesForRead().FirstOrDefault(x => x.Id == pageId) ?? throw new BusinessException("Không tìm thấy trang Nhập liệu này (đã xóa?).");
        var page = new KiemKe.KiemKeCore.Page { Id = p.Id, Name = p.Name, Template = p.Template, UpdatedAt = p.UpdatedAt, Columns = p.Columns.Select(c => c.Header).ToList(), Rows = _entry.LoadForRead(p) };
        return KiemKe.KiemKeCore.MapPage(B, page, QcOf);
    }

    public void ExportReview(string path, ReviewResult res)
    {
        RequireExport();
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Chenh lech");
        var head = new[] { "Vị trí", "Mã hàng", "Lô", "SL sổ sách", "SL thực", "Chênh lệch", "Trọng lượng", "Chênh lệch Net (kg)", "Ghi chú" };
        for (var c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
        var r = 2;
        foreach (var x in res.Rows)
        {
            var eff = x.Sl ?? (x.Known ? x.Sl0 : 0); var d = eff - x.Sl0;
            if (Math.Abs(d) < 1e-9 && !(x.Known && eff == 0)) continue;
            ws.Cell(r, 1).Value = x.Code; ws.Cell(r, 2).Value = x.Ma; ws.Cell(r, 3).Value = x.Lo; ws.Cell(r, 4).Value = x.Sl0; ws.Cell(r, 5).Value = eff; ws.Cell(r, 6).Value = d;
            if (x.Tl != 0) ws.Cell(r, 7).Value = x.Tl; ws.Cell(r, 8).Value = R2(d * x.Tl);
            ws.Cell(r, 9).Value = x.Known ? (eff == 0 ? "Thực = 0, sổ có hàng" : "") : "Hàng ngoài sổ" + (x.Ma.Length > 0 ? "" : " — chưa nhập mã"); r++;
        }
        var rng = ws.Range(1, 1, Math.Max(1, r - 1), head.Length);
        rng.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; rng.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Row(1).Style.Font.Bold = true; ws.SheetView.FreezeRows(1); ws.Columns().AdjustToContents();
        wb.SaveInPlace(path);
    }
}

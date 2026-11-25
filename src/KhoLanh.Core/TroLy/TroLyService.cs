using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Lan;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.PhieuXuat;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;
using KhoLanh.Core.TonKho;
using Microsoft.Data.Sqlite;
using static KhoLanh.Core.TroLy.TroLyChecks;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// TRỢ LÝ HỆ THỐNG (viết lại theo core/tro-ly của app cũ). Thu thập dữ liệu CHỈ ĐỌC, chạy các kiểm tra (TroLyChecks / TroLyNghiepVu / TroLyMaHang),
/// giữ hàng đợi đề xuất (TroLyQueue) và soạn báo cáo cho Claude (TroLyReport). Chỉ Quản trị dùng. Không sửa dữ liệu — trừ một thao tác
/// do Quản trị bấm: "Thêm mã vào Danh sách mã hàng" (chỉ THÊM dòng mới, có ghi nhật ký).
/// Trạng thái hàng đợi lưu riêng ở &lt;thư mục dữ liệu&gt;/tro-ly-state.json (theo máy).
/// </summary>
public sealed partial class TroLyService
{
    private readonly Database _db;
    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly BackupService _backup;
    private readonly Func<LanService> _lan;
    private readonly DriveSyncService? _drive;
    private readonly string _dataDir;
    private readonly object _lock = new();
    private bool _running;

    public Func<DateTime> Now { get; set; } = () => DateTime.Now;
    /// <summary>Cài đặt của máy — để kiểm tra cấu hình kết nối (null = bỏ bước đó).</summary>
    public SettingsService? Settings { get; set; }
    public string Version { get; set; } = UpdateService.Current.ToString(3);

    public TroLyService(Database db, Func<ISheetSource> src, Session session, AuditService audit, BackupService backup, Func<LanService> lan, DriveSyncService? drive, string dataDir)
    {
        _db = db; _src = src; _session = session; _audit = audit; _backup = backup; _lan = lan; _drive = drive; _dataDir = dataDir;
    }

    public string StateFile => Path.Combine(_dataDir, "tro-ly-state.json");
    public string ErrorLogFile => Path.Combine(_dataDir, "logs", "loi.log");
    public bool CanUse => Roles.IsAdmin(_session.Role);
    private void Require() { if (!CanUse) throw new BusinessException("Trợ lý hệ thống chỉ dành cho Quản trị."); }

    // ------------------------------------------------------------------ trạng thái

    public sealed class State
    {
        public DateTime? LastScanAt { get; set; }
        public Dictionary<string, TroLyQueue.Item> Items { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public State LoadState()
    {
        try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile), Json) ?? new() : new(); }
        catch { return new(); }
    }
    private void SaveState(State s)
    {
        Directory.CreateDirectory(_dataDir);
        var tmp = StateFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
        File.Move(tmp, StateFile, true);
    }

    // ------------------------------------------------------------------ kết quả quét

    public sealed record Step(string Name, long Ms, string Error);
    public sealed class ScanResult
    {
        public DateTime At;
        public long Ms;
        public bool Deep;
        public List<Step> Steps = new();
        public List<Finding> Findings = new();
        public TroLyQueue.Changes Changes = new(0, 0, 0);
        public int Do, Vang, Xanh, BoQua;
    }

    public ScanResult? Last { get; private set; }
    /// <summary>Báo khi một lần quét xong (kể cả quét tự động) — giao diện cập nhật huy hiệu.</summary>
    public event Action<ScanResult>? Scanned;

    public ScanResult Scan(bool deep)
    {
        Require();
        lock (_lock) { if (_running) throw new BusinessException("Đang quét — đợi xong rồi quét lại."); _running = true; }
        try { return ScanCore(deep); }
        finally { lock (_lock) _running = false; }
    }

    private ScanResult ScanCore(bool deep)
    {
        var sw = Stopwatch.StartNew(); var now = Now();
        var res = new ScanResult { At = now, Deep = deep };
        var f = new List<Finding>();
        var stepFailed = false;
        long liveRows = 0;
        void StepRun(string name, Action fn)
        {
            var t = Stopwatch.StartNew(); var err = "";
            try { fn(); }
            catch (Exception e)
            {
                stepFailed = true; err = e.Message;
                f.Add(Mk("loi-app", "vang", "Trợ lý hệ thống", $"Bước \"{name}\" lỗi: " + Short(e.Message, 120), e.GetType().Name + ": " + e.Message, new[] { Short(e.StackTrace ?? "", 400) }, Hash("troly-step|" + name)));
            }
            res.Steps.Add(new Step(name, t.ElapsedMilliseconds, err));
        }

        StepRun("Lỗi phần mềm", () => f.AddRange(CheckErrors(ReadErrors(now))));
        StepRun("Cơ sở dữ liệu", () => { var info = CollectDb(deep); liveRows = info.SheetRows; f.AddRange(CheckDb(info)); });
        PhieuCol? phieu = null;
        StepRun("Ghi phiếu", () => { phieu = CollectPhieu(); f.AddRange(CheckPhieu(phieu, "Phiếu xuất (5 nguồn)")); });
        StepRun("Số liệu nghiệp vụ", () => f.AddRange(TroLyNghiepVu.CheckNghiepVu(CollectSoKho(), phieu, now)));
        StepRun("Dòng trùng khóa", () => f.AddRange(CollectDupKeys()));
        StepRun("Mã hàng & tên hàng", () => { var (rows, master, maHoa) = CollectMaTen(); f.AddRange(TroLyMaHang.CheckMaTen(rows, master, maHoa)); });
        StepRun("Kiểm tra chéo", () => { var (a, v, g) = CollectTon(); f.AddRange(TroLyNghiepVu.CheckCheo(a, v, g)); });
        StepRun("Sao lưu & phục hồi", () =>
        {
            var files = _backup.ListAutoBackups().Select(x => new BackupFile(x.Name, x.LastWriteTime, x.Length)).ToList();
            RestoreProbe? probe = null;
            // thử mở ĐÚNG bản mà CheckBackup đánh giá (trước đây lấy bản đầu theo tên → có thể khác bản "mới nhất" ghi trong báo cáo)
            if (deep && PickLatest(files) is { } newest) probe = Probe(Path.Combine(_backup.AutoBackupFolder, newest.Name));
            f.AddRange(CheckBackup(files, liveRows, probe, now));
        });
        StepRun("Đồng bộ", () => f.AddRange(CheckSync(CollectSync(now))));
        StepRun("Cấu hình kết nối", () => f.AddRange(CheckConfig(CollectConfig())));
        StepRun("Thao tác chậm", () => f.AddRange(CheckSlow(Perf.Read(_dataDir, now))));
        StepRun("Hiệu năng", () => f.AddRange(CheckPerf(now)));   // 1.101: CPU / RAM cao kéo dài, ổ sắp đầy, chậm dần sau cập nhật

        var st = LoadState();
        var fin = Sort(f);
        TroLyFix.Assign(fin);   // 1.102: nút "Sửa nhanh" cho lỗi đã biết
        var (items, ch) = TroLyQueue.Update(st.Items, fin, now, deep, !stepFailed, Version);
        st.Items = items; st.LastScanAt = now;
        try { SaveState(st); } catch { /* không lưu được trạng thái: vẫn trả kết quả */ }
        Annotate(fin, st);
        res.Findings = fin; res.Changes = ch; res.Ms = sw.ElapsedMilliseconds;
        Count(res);
        Last = res;
        AfterScan(res);   // giai đoạn 5: lịch sử quét (hieu-nang.db) + ảnh chụp tình trạng máy (database dùng chung) — TroLyService.May.cs
        Scanned?.Invoke(res);
        return res;
    }

    private static void Count(ScanResult r)
    {
        var live = r.Findings.Where(x => x.Q is not ("boQua" or "loiNguon")).ToList();
        r.Do = live.Count(x => x.Sev == "do"); r.Vang = live.Count(x => x.Sev == "vang"); r.Xanh = live.Count(x => x.Sev == "xanh");
        r.BoQua = r.Findings.Count - live.Count;
    }

    private static void Annotate(List<Finding> fin, State st)
    {
        foreach (var x in fin)
        {
            if (st.Items.TryGetValue(x.Id, out var q)) { x.Q = q.St; x.Recur = q.Recur; x.QNote = q.Note; x.QFirstAt = q.FirstAt; }
            else { x.Q = ""; x.Recur = 0; x.QNote = ""; x.QFirstAt = null; }
        }
    }

    /// <summary>Gắn lại trạng thái hàng đợi mới nhất vào kết quả quét gần nhất (sau khi Quản trị đổi trạng thái).</summary>
    public void Refresh()
    {
        if (Last is null) return;
        Annotate(Last.Findings, LoadState());
        Count(Last);
    }

    // ------------------------------------------------------------------ thao tác của Quản trị

    public List<(string Id, TroLyQueue.Item Item)> Queue() { Require(); return TroLyQueue.List(LoadState().Items); }

    public int Mark(IEnumerable<string> ids, string st)
    {
        Require();
        var s = LoadState();
        var (items, n) = TroLyQueue.Mark(s.Items, ids, st, Now(), Version);
        if (n == 0) throw new BusinessException("Không đổi được trạng thái (cảnh báo chưa có trong hàng đợi — bấm Quét ngay trước).");
        s.Items = items; SaveState(s); Refresh();
        return n;
    }

    public int Purge(IEnumerable<string> ids)
    {
        Require();
        var s = LoadState();
        var (items, n) = TroLyQueue.Purge(s.Items, ids);
        if (n == 0) throw new BusinessException("Không có mục đã đóng / đã bỏ qua nào để xóa.");
        s.Items = items; SaveState(s); Refresh();
        return n;
    }

    /// <summary>Soạn báo cáo cho các cảnh báo đã chọn; cảnh báo Mới / Tái phát chuyển sang "Đã gửi Claude".</summary>
    public string Report(IEnumerable<string> ids, bool anDanh = true)
    {
        Require();
        var want = ids.ToHashSet();
        var pick = (Last?.Findings ?? new()).Where(x => want.Contains(x.Id)).ToList();
        if (pick.Count == 0) throw new BusinessException("Chưa chọn cảnh báo nào (hoặc chưa quét).");
        var s = LoadState();
        var send = pick.Where(x => s.Items.TryGetValue(x.Id, out var q) && q.St is "moi" or "taiPhat").Select(x => x.Id).ToList();
        if (send.Count > 0) { s.Items = TroLyQueue.Mark(s.Items, send, "daGui", Now(), Version).Items; SaveState(s); Refresh(); }
        return TroLyReport.Build(pick, Version, Now(), anDanh, ReportExtra(pick));
    }

    public sealed record AddResult(int Added, int SkippedNear);

    /// <summary>
    /// «Thêm mã vào Danh sách mã hàng»: chỉ thêm mã KHÔNG có mã gần giống (gần giống = nghi gõ sai, để người dùng tự xem).
    /// Chỉ THÊM dòng mới vào Tổng hợp › MaDaTao, không sửa / xóa dòng đang có.
    /// </summary>
    public AddResult AddMissingCodes(string findingId)
    {
        Require();
        var f = (Last?.Findings ?? new()).FirstOrDefault(x => x.Id == findingId);
        if (f?.Fixes is not { Count: > 0 } fixes) throw new BusinessException("Không còn cảnh báo này — bấm Quét ngay rồi thử lại.");
        var cur = JsonNode.Parse(_src().Read(TongHopService.Db, "MaDaTao")) as JsonArray ?? new JsonArray();
        static string N(string? c) => Regex.Replace(c ?? "", @"\s+", "").ToUpperInvariant();
        var have = cur.OfType<JsonObject>().Select(o => N(S(o["Mã"]))).ToHashSet();
        var add = new List<JsonObject>(); var skip = 0;
        foreach (var x in fixes)
        {
            var c = N(x.Ma);
            if (c.Length == 0 || have.Contains(c)) continue;
            if (x.Near.Length > 0) { skip++; continue; }
            have.Add(c);
            add.Add(new JsonObject { ["STT"] = cur.Count + add.Count + 1, ["Mã"] = c, ["Ghi chú"] = x.Ten, ["Đủ mã"] = c.Length == 21 ? 1 : 0 });
        }
        if (add.Count == 0) return new(0, skip);
        foreach (var a in add) cur.Add(a);
        _src().Write(TongHopService.Db, new Dictionary<string, JsonArray?> { ["MaDaTao"] = cur });
        _audit.Log(_session.Username, "Trợ lý: thêm mã vào Danh sách mã hàng", $"{add.Count} mã: " + string.Join(", ", add.Take(20).Select(a => S(a["Mã"]))) + (add.Count > 20 ? " …" : ""));
        return new(add.Count, skip);
    }

    // ------------------------------------------------------------------ thu thập (CHỈ ĐỌC)

    private JsonArray Read(string db, string sheet)
    {
        try { return JsonNode.Parse(_src().Read(db, sheet)) as JsonArray ?? new JsonArray(); }
        catch { return new JsonArray(); }
    }
    private static JsonObject? Cell(JsonArray rows)
    {
        if (rows.Count == 0 || rows[0] is not JsonObject o) return null;
        var c = S(o["data"]);
        if (!c.StartsWith("__JSON__", StringComparison.Ordinal)) return null;
        try { return JsonNode.Parse(c[8..]) as JsonObject; } catch { return null; }
    }

    internal List<AppError> ReadErrors(DateTime now)
    {
        if (!File.Exists(ErrorLogFile)) return new();
        string text;
        using (var fs = new FileStream(ErrorLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            const long Tail = 2 * 1024 * 1024;
            if (fs.Length > Tail) fs.Seek(-Tail, SeekOrigin.End);
            using var sr = new StreamReader(fs);
            text = sr.ReadToEnd();
        }
        return ParseErrorLog(text, now);
    }

    /// <summary>Số lỗi phần mềm ghi trong loi.log từ đầu ngày hôm nay.</summary>
    public int ErrorsToday()
    {
        try { var now = Now(); return ReadErrors(now).Where(e => e.Last.Date == now.Date).Sum(e => e.Count); } catch { return 0; }
    }

    internal DbInfo CollectDb(bool deep)
    {
        var info = new DbInfo();
        try
        {
            using var c = _db.Open();
            info.Integrity = string.Join("; ", c.Query<string>(deep ? "PRAGMA integrity_check" : "PRAGMA quick_check").Take(5));
            info.PageCount = c.ExecuteScalar<long>("PRAGMA page_count");
            info.FreePages = c.ExecuteScalar<long>("PRAGMA freelist_count");
            info.AuditRows = c.ExecuteScalar<long>("SELECT COUNT(*) FROM audit_log");
            info.HistoryRows = c.ExecuteScalar<long>("SELECT COUNT(*) FROM sheet_history");
            info.Sheets = c.ExecuteScalar<long>("SELECT COUNT(*) FROM module_sheets");
            info.SheetRows = c.ExecuteScalar<long?>("SELECT SUM(row_count) FROM module_sheets") ?? 0;
            info.Conflicts = (int)c.ExecuteScalar<long>("SELECT COUNT(*) FROM lan_conflicts WHERE status IN ('cho','gop')");
        }
        catch (Exception e) { info.Failed = e.Message; info.Integrity = ""; }
        try { info.FileBytes = new FileInfo(_db.FilePath).Length; } catch { }
        try { var w = _db.FilePath + "-wal"; if (File.Exists(w)) info.WalBytes = new FileInfo(w).Length; } catch { }
        return info;
    }

    /// <summary>Phiếu xuất của các module (qua Phiếu xuất hệ thống) về dạng chung.</summary>
    internal PhieuCol CollectPhieu()
    {
        var px = new PxhtService(_src, _session);
        var slips = new List<PxhtService.Slip>();
        slips.AddRange(px.LoadM1()); slips.AddRange(px.LoadM2()); slips.AddRange(px.LoadSoKho("m3", "nxt-bot-sot"));
        var phieu = new List<Phieu>(); var items = new List<PhieuItem>();
        foreach (var s in slips)
        {
            var ded = s.Meta.FirstOrDefault(m => m.K == "Trừ tồn kho").V;
            phieu.Add(new Phieu(s.Key, s.MaPhieu, PxhtService.SrcLabel(s.Src) + " · " + PxhtService.Src(s.Src).Sub, s.DayIso, false, ded is null ? null : ded == "Đã trừ"));
            foreach (var it in s.Items)
            {
                string G(params string[] keys) => keys.Select(k => it.GetValueOrDefault(k) ?? "").FirstOrDefault(v => v.Length > 0) ?? "";
                items.Add(new PhieuItem(s.Key, G("maHang", "maGoc", "ma"), G("qty", "soLuong"), G("vt", "viTri")));
            }
        }
        // Phiếu xuất hệ thống chỉ gắn dòng hàng vào phiếu có thật → đọc thẳng PhieuXuatItems của Tồn kho gửi để thấy dòng mồ côi
        var gIds = Read("ton-kho-gui", "PhieuXuat").OfType<JsonObject>().Select(r => S(r["id"])).ToHashSet();
        foreach (var r in Read("ton-kho-gui", "PhieuXuatItems").OfType<JsonObject>())
            if (!gIds.Contains(S(r["phieuId"]))) items.Add(new PhieuItem("m2|" + S(r["phieuId"]), S(r["maHang"]), S(r["soLuongXuat"])));
        return new PhieuCol(phieu, items);
    }

    internal List<(string, List<TroLyNghiepVu.SoKhoLine>)> CollectSoKho()
    {
        var res = new List<(string, List<TroLyNghiepVu.SoKhoLine>)>();
        var rows = Read("nxt-bot-sot", "SoCai");
        if (rows.Count == 0) return res;
        var seen = new HashSet<string>(); var lines = new List<TroLyNghiepVu.SoKhoLine>();
        foreach (var o in rows.OfType<JsonObject>().Take(200_000))
        {
            var id = S(o["id"]);
            if (id.Length > 0 && !seen.Add(id)) continue; // dòng trùng id: chỉ giữ dòng đầu (giống so-kho-core dedupeById)
            var loai = S(o["loai"]).ToLowerInvariant();
            if (loai is not ("tondau" or "nhap" or "xuat" or "chuyen" or "dieuchinh")) loai = "nhap";
            lines.Add(new(loai, TroLyNghiepVu.IsoDate(S(o["ngay"])), S(o["maHang"]), S(o["size"]), S(o["viTri"]), Num(S(o["soLuong"])), Num(S(o["net"])), S(o["chungTu"])));
        }
        res.Add(("Sổ kho M03 · NXT Bột/Sốt", lines));
        return res;
    }

    public static readonly Dictionary<string, string> DbLabels = new()
    {
        ["ton-kho-an-an"] = "Tồn kho An An", ["ton-kho-gui"] = "Tồn kho gửi", ["ton-vi-tri-an-an"] = "Tồn kho theo vị trí", ["so-do-vi-tri"] = "Sơ đồ kho",
        ["tong-hop"] = "Tổng hợp", ["bao-cao-ra-dong"] = "Báo cáo rã đông", ["so-lieu-kiem-ke"] = "Đối chiếu kiểm kê", ["ket-qua-khang-sinh"] = "Kết quả kháng sinh",
        ["phieu-nhap-xuat"] = "Phiếu xuất kho", ["in-chung-tu-kho"] = "In chứng từ kho", ["nxt-bot-sot"] = "NXT Bột/Sốt", ["nhap-lieu"] = "Nhập liệu",
        ["container-3d"] = "Container 3D", ["lichxuat"] = "Lịch xuất",
    };
    public static string DbLabel(string k) => DbLabels.GetValueOrDefault(k, k);

    private static readonly Regex SkipSheet = new(@"^(_|meta$|naplog$|log$|settings$|cfg$|config$)|meta$|_meta$|lichsu|history", RegexOptions.IgnoreCase);
    private const int MaxSheetRows = 60_000;

    internal List<Finding> CollectDupKeys()
    {
        var o = new List<Finding>();
        List<(string db_key, string sheet, long row_count)> sheets;
        using (var c = _db.Open())
            sheets = c.Query<(string, string, long)>("SELECT db_key, sheet, row_count FROM module_sheets ORDER BY db_key, sheet").ToList();
        foreach (var (db, sheet, n) in sheets)
        {
            if (n < 2 || n > MaxSheetRows || SkipSheet.IsMatch(sheet)) continue;
            var rows = Read(db, sheet);
            if (rows.Count < 2 || rows.Count > MaxSheetRows) continue;
            o.AddRange(FindDupKeys(DbLabel(db), sheet, rows));
        }
        return o;
    }

    internal (List<TroLyMaHang.Row>, List<TroLyMaHang.Master>, List<TroLyMaHang.SegCode>) CollectMaTen()
    {
        var rows = new List<TroLyMaHang.Row>();
        var an = AnAnStore.Load(_src());
        if (an.HasData)
        {
            int mi = an.Col("Mã hàng"), ti = an.Col("Tên hàng");
            if (mi >= 0) foreach (var r in an.Rows) rows.Add(new("M01", r[mi], ti >= 0 ? r[ti] : ""));
        }
        foreach (var r in Read("ton-vi-tri-an-an", "ViTri").OfType<JsonObject>()) rows.Add(new("M01VT", S(r["Mã hàng"]), RawStr(r["Tên hàng"])));
        foreach (var r in Read("ton-kho-gui", "DuLieuChinh").OfType<JsonObject>()) rows.Add(new("M02", S(r["Mã hàng"]), RawStr(r["Tên hàng"])));
        var master = Read(TongHopService.Db, "MaDaTao").OfType<JsonObject>().Select(o => new TroLyMaHang.Master(S(o["Mã"]), S(o["Ghi chú"]))).Where(m => m.Ma.Length > 0).ToList();
        var maHoa = new List<TroLyMaHang.SegCode>();
        foreach (var r in Read(TongHopService.Db, "MaHoa").OfType<JsonObject>())
            if (int.TryParse(S(r["Tổ hợp"]), out var k)) maHoa.Add(new(k, S(r["Mã"])));
        foreach (var (k, list) in TongHopService.FixedCatalog()) maHoa.AddRange(list.Select(e => new TroLyMaHang.SegCode(k, e.Code)));
        return (rows, master, maHoa);
    }

    /// <summary>Chuỗi gốc (không cắt khoảng trắng) — để soi khoảng trắng thừa trong tên.</summary>
    private static string RawStr(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : S(n);

    internal (TroLyNghiepVu.TonTable?, TroLyNghiepVu.TonTable?, TroLyNghiepVu.TonTable?) CollectTon()
    {
        TroLyNghiepVu.TonTable? a = null, v = null, g = null;
        var an = AnAnStore.Load(_src());
        if (an.HasData)
        {
            int mi = an.Col("Mã hàng"), ti = an.Col("Tên hàng"), si = an.Col("Tồn cuối"), ki = an.ColExact("Tồn cuối (NET)");
            if (mi >= 0)
                a = new("Tồn kho An An", TroLyNghiepVu.IsoDate(an.DataDate), an.Rows.Select(r => new TroLyNghiepVu.TonRow(r[mi], ti >= 0 ? r[ti] : "",
                    si >= 0 ? Z(Num(r[si])) : 0, ki >= 0 ? Z(Num(r[ki])) : 0)).ToList());
        }
        var vmeta = Cell(Read("ton-vi-tri-an-an", "ViTriMeta"));
        var vrows = Read("ton-vi-tri-an-an", "ViTri").OfType<JsonObject>().Select(r => new TroLyNghiepVu.TonRow(S(r["Mã hàng"]), S(r["Tên hàng"]),
            Z(Num(S(r["SL Tồn"]))), Z(Num(S(r["SL Tồn"]))) * Z(Num(S(r["T.Lượng"]))), S(r["Vị trí"]) + (S(r["Lô (Batch)"]) is { Length: > 0 } lo ? " · lô " + lo : ""))).ToList();
        if (vrows.Count > 0) v = new("Tồn kho theo vị trí", TroLyNghiepVu.IsoDate(S(vmeta?["dataDate"])), vrows);
        var gmeta = Cell(Read("ton-kho-gui", "DuLieuMeta"));
        var grows = Read("ton-kho-gui", "DuLieuChinh").OfType<JsonObject>().Select(r => new TroLyNghiepVu.TonRow(S(r["Mã hàng"]), S(r["Tên hàng"]),
            Z(Num(S(r["Số kiện"]))), Z(Num(S(r["Trọng Lượng (KG)"]))), S(r["Số lô"]))).ToList();
        if (grows.Count > 0) g = new("Tồn kho gửi", TroLyNghiepVu.IsoDate(S(gmeta?["dataDate"])), grows);
        return (a, v, g);
    }
    private static double Z(double d) => double.IsFinite(d) ? d : 0;

    internal static RestoreProbe Probe(string path)
    {
        try
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            c.Open();
            var ic = c.ExecuteScalar<string>("PRAGMA integrity_check");
            if (ic != "ok") return new(false, "integrity_check: " + ic, 0, 0);
            var sheets = (int)c.ExecuteScalar<long>("SELECT COUNT(*) FROM module_sheets");
            var rows = c.ExecuteScalar<long?>("SELECT SUM(row_count) FROM module_sheets") ?? 0;
            return new(true, "", sheets, rows);
        }
        catch (Exception e) { return new(false, e.Message, 0, 0); }
    }

    private static readonly Regex TmpRe = new(@"\.(tmp|tam|tai-ve|partial|crdownload)$", RegexOptions.IgnoreCase);

    internal SyncInfo CollectSync(DateTime now)
    {
        var info = new SyncInfo();
        void Walk(string dir, int depth)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var p in Directory.EnumerateFiles(dir))
                if (TmpRe.IsMatch(p) && !p.EndsWith("tro-ly-state.json.tmp", StringComparison.Ordinal))
                    try { info.TmpFiles.Add((Path.GetFileName(p), (now - File.GetLastWriteTime(p)).TotalMinutes)); } catch { }
            if (depth < 1) foreach (var d in Directory.EnumerateDirectories(dir)) if (!Path.GetFileName(d).StartsWith('_')) Walk(d, depth + 1);
        }
        try { Walk(_dataDir, 0); } catch { }
        var lan = _lan();
        info.LanEnabled = lan.Enabled; info.LanRole = lan.Role; info.LanHealth = lan.Health; info.LanListenError = lan.ListenError;
        try
        {
            using var c = _db.Open();
            info.Pending = (int)c.ExecuteScalar<long>("SELECT COUNT(*) FROM lan_pending");
            var oldest = c.ExecuteScalar<string?>("SELECT MIN(created_at) FROM lan_pending");
            if (oldest is not null && DateTime.TryParse(oldest, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var o))
                info.PendingAgeMin = (now.ToUniversalTime() - o).TotalMinutes;
        }
        catch { }
        if (_drive is { Enabled: true } dr)
        {
            info.DriveEnabled = true; info.DriveStatus = dr.Status;
            if (dr.LastOk != default) info.DriveOkAgeMin = (now - dr.LastOk).TotalMinutes;
        }
        return info;
    }

    /// <summary>1.101: cảnh báo hiệu năng từ hieu-nang.db (bộ đo ngầm) + logs/cham.log 30 ngày. Chưa có số liệu thì không cảnh báo.</summary>
    /// <summary>Khóa "trang sửa" đặc biệt: chuyển sang tab Hiệu năng của chính Trợ lý (không phải khóa menu).</summary>
    public const string GoHieuNang = "#hieunang";
    internal List<Finding> CheckPerf(DateTime now)
    {
        var res = new List<Finding>();
        var path = Path.Combine(_dataDir, PerfStore.FileName);
        var st = PerfMonitor.Current?.Store ?? (File.Exists(path) ? new PerfStore(path) : null);
        if (st is not null)
        {
            res.AddRange(PerfChecks.CheckMachine(st.Range(now.AddHours(-1), now, 120), st.Latest(), now));
            res.AddRange(PerfChecks.CheckStartupTrend(st.Marks(null, now.AddDays(-30)), now));
        }
        res.AddRange(PerfChecks.CheckSlowAfterUpdate(Perf.Read(_dataDir, now, 30)));
        foreach (var x in res) if (x.Go.Length == 0) { x.Go = GoHieuNang; x.GoLabel = "tab Hiệu năng"; }
        return res;
    }

    /// <summary>Cấu hình kết nối của máy: Tài khoản dùng chung, Web App điện thoại, Gmail lịch xuất, thư mục Drive dùng chung (CHỈ ĐỌC).</summary>
    internal ConfigInfo? CollectConfig()
    {
        if (Settings is not { } st) return null;
        var info = new ConfigInfo
        {
            AcctEnabled = st.Get("acct_url").Length > 0 && st.Get("acct_token").Length > 0,
            AcctError = ConnErrorLog.Read(st, ConnErrorLog.Acct),
        };
        var phone = new PhoneWebApp(st);
        info.PhoneEnabled = phone.Enabled; info.PhoneError = phone.LastError;
        var mail = KhoLanh.Core.LichXuat.MailConfig.Load(st);
        info.MailReady = mail.Ready; info.MailEmail = mail.Email; info.MailStatus = st.Get("lx_mail_status");
        if (_drive is { Enabled: true } dr)
        {
            info.DriveEnabled = true; info.DriveDir = dr.Dir;
            try { info.DriveDirExists = Directory.Exists(dr.Dir); } catch { info.DriveDirExists = false; }
        }
        return info;
    }

    // ------------------------------------------------------------------ Việc cần chú ý hôm nay

    public List<TroLyChuY.Viec> ChuY()
    {
        Require();
        var now = Now();
        var inp = new TroLyChuY.Input { Today = now.Date };
        foreach (var r in Read("lichxuat", "LichXuatCont").OfType<JsonObject>())
            inp.Lich.Add(new(S(r["id"]), TroLyNghiepVu.IsoDate(S(r["ngay"])), S(r["loai"]), S(r["trangThai"]), S(r["cont"]), S(r["contSo"]), S(r["lsx"])));
        inp.ListLsx = Read("phieu-nhap-xuat", "PNX_ListSX").OfType<JsonObject>().Select(r => S(r["lsx"])).Where(x => x.Length > 0).ToList();
        foreach (var r in Read("phieu-nhap-xuat", "PNX_Phieu").OfType<JsonObject>())
        {
            var raw = S(r["data"]);
            JsonObject? d = null;
            try { d = JsonNode.Parse(raw.StartsWith("__JSON__", StringComparison.Ordinal) ? raw[8..] : raw) as JsonObject; } catch { }
            if (d is null || S(d["loai"]) != "xuat_khau") continue;
            var ngay = S(d["tuDong"]?["ngayLich"]) is { Length: > 0 } nl ? nl : S(d["ngay"]);
            inp.Phieu.Add(new("xuat_khau", S(d["lsx"]), TroLyNghiepVu.IsoDate(ngay) is { Length: > 0 } iso ? iso : (ngay.Length >= 10 ? ngay[..10] : ngay), S(d["xe"]?["cont"])));
        }
        foreach (var s in (Cell(Read("ton-vi-tri-an-an", "ViTriPhieuDienThoai"))?["slips"] as JsonArray ?? new()).OfType<JsonObject>())
        {
            DateTime? at = DateTimeOffset.TryParse(S(s["createdAt"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.LocalDateTime : null;
            inp.VtSlips.Add(new(S(s["id"]), S(s["maPhieu"]), S(s["loai"]), at, S(s["deducted"]) == "true"));
        }
        var lan = _lan();
        inp.LanEnabled = lan.Enabled; inp.LanHealth = lan.Health;
        var sync = CollectSync(now); inp.LanPending = sync.Pending; inp.LanPendingAgeMin = sync.PendingAgeMin;
        try { using var c = _db.Open(); inp.LanConflicts = (int)c.ExecuteScalar<long>("SELECT COUNT(*) FROM lan_conflicts WHERE status IN ('cho','gop')"); } catch { }
        inp.HasBackupToday = _backup.ListAutoBackups().Any(b => b.LastWriteTime.Date == now.Date);
        var (a, v, g) = CollectTon();
        if (a is not null) inp.Data.Add(new(a.Label, a.DataDate, a.Rows.Count, "m1"));
        if (v is not null) inp.Data.Add(new(v.Label, v.DataDate, v.Rows.Count, "m7"));
        if (g is not null) inp.Data.Add(new(g.Label, g.DataDate, g.Rows.Count, "m2"));
        inp.ErrorsToday = ErrorsToday();
        return TroLyChuY.Build(inp);
    }

    // ------------------------------------------------------------------ quét tự động

    private CancellationTokenSource? _auto;

    /// <summary>Quét nhanh tự động: <paramref name="first"/> sau khi đăng nhập Quản trị, rồi mỗi <paramref name="every"/>.</summary>
    public void StartAuto(TimeSpan first, TimeSpan every)
    {
        StopAuto();
        var cts = _auto = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(first, cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    try { if (CanUse) Scan(false); } catch { /* đang quét tay / lỗi: lượt sau quét lại */ }
                    await Task.Delay(every, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    public void StopAuto() { _auto?.Cancel(); _auto = null; }

    // ------------------------------------------------------------------ ghi lỗi chưa bắt

    /// <summary>Ghi lỗi chưa được bắt (UnhandledException / UnobservedTaskException) vào logs/loi.log theo cùng dạng Ui.Describe.</summary>
    public static void LogUnhandled(string dataDir, Exception e, string where)
    {
        try
        {
            var dir = Path.Combine(dataDir, "logs"); Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "loi.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} bản {UpdateService.Current} [{where}]\n{e}\n\n");
        }
        catch { /* ghi log lỗi không bao giờ được làm hỏng app */ }
    }
}

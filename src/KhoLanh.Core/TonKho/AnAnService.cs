using System.Globalization;
using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// Module Tồn kho An An (C#, thay bản WebView2 ton-kho-an-an.html) — dùng CHUNG database "ton-kho-an-an" với app cũ.
///   • Nạp file Excel an toàn: đọc + kiểm tra → xem trước → nạp (chụp bản hoàn tác trên máy này, ghi nhật ký NapLog, giữ ảnh chụp lần trước để so sánh).
///   • Ngày số liệu, chuyển dòng thủ công, điều kiện lọc tự thêm, Đặc Tính (gộp nhóm), Báo cáo ngoài sổ sách (Kho Gửi tự lấy từ Tồn kho gửi).
///   • Xuất Hàng: tạo phiếu từ dòng của 1 bảng phân loại, trừ / hủy trừ vào cột "Tồn cuối".
///   • Theo Dõi Tồn: ảnh chụp tồn theo tháng + ghi chú.
/// Quyền: m1 (Xem / Xuất / Sửa) — Sửa = nạp file, Đặc tính, chuyển mã, phiếu (như "Quản lý" của app cũ).
/// </summary>
public sealed class AnAnService
{
    public const string PermKey = "m1";
    public const string KeyTpcThreshold = "m1_tpc_threshold", SetKhoGuiSig = "m1_khogui_sig", SetNapDays = "nap_keep_days";

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly SettingsService? _settings;
    public NapUndoStore Undo { get; }

    public AnAnData D { get; private set; } = new();
    public List<JsonObject> Slips { get; private set; } = new();
    public double SlipSeq { get; private set; } = 1;
    public TheoDoiTon Tdt { get; private set; } = new();
    public bool Loaded { get; private set; }
    /// <summary>File Excel vừa nạp trong phiên này (để bấm đổi sang sheet khác như app cũ).</summary>
    public SheetText.Book? Workbook { get; private set; }
    private string? _openNapId;

    public AnAnService(Func<ISheetSource> source, Session session, AuditService audit, SettingsService? settings = null, NapUndoStore? undo = null)
    { _src = source; _session = session; _audit = audit; _settings = settings; Undo = undo ?? NapUndoStore.Default(); }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    private void RequireEdit() => _session.Require(PermKey, Perm.Edit);
    private string UserName => _session.CurrentUser is { } u ? (u.FullName.Length > 0 ? u.FullName : u.Username) : "";
    private void Log(string action, string detail) { try { _audit.Log(_session.Username, "Tồn kho An An: " + action, detail); } catch { /* nhật ký lỗi không chặn việc chính */ } }
    private static double NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // ------------------------------------------------------------------ đọc

    public void Load()
    {
        var src = _src();
        D = AnAnStore.Load(src);
        if (_settings is not null && double.TryParse(_settings.Get(KeyTpcThreshold), NumberStyles.Float, CultureInfo.InvariantCulture, out var th) && th >= 0) D.TpcThreshold = th;
        var px = AnAnStore.ReadJsonCell(src, AnAnStore.SPhieu) as JsonObject;
        Slips = (px?["slips"] as JsonArray ?? new()).OfType<JsonObject>().Select(x => (JsonObject)x.DeepClone()).ToList();
        var seq = Num(px?["seq"]); SlipSeq = double.IsFinite(seq) ? seq : 1;
        Tdt = TheoDoiTon.FromRows(AnAnStore.ReadArr(src, AnAnStore.STheoDoi));
        Loaded = true;
        // Đặc tính mới xuất hiện trong dữ liệu → ghi vào sổ Đặc Tính (như renderDacTinh của app cũ, chỉ người được sửa)
        D.DacTinhBreakdown(out var known);
        if (known && CanEdit) TryWrite(new() { [AnAnStore.SDacTinh] = AnAnStore.DacTinhSheet(D) });
        EnsureTdtSnapshot();
    }

    /// <summary>Theo Dõi Tồn: chụp ảnh tồn tháng hiện tại khi dữ liệu đổi (app cũ chạy sau mỗi lần vẽ trang).</summary>
    public void EnsureTdtSnapshot()
    {
        if (Tdt.EnsureSnapshot(D)) TryWrite(new() { [AnAnStore.STheoDoi] = Tdt.ToRows() });
    }

    private void TryWrite(Dictionary<string, JsonArray?> sheets) { try { _src().Write(AnAnStore.Db, sheets); } catch { /* máy chỉ xem / mất mạng: để lần sau */ } }
    private void Write(Dictionary<string, JsonArray?> sheets) => _src().Write(AnAnStore.Db, sheets);

    public void SaveLoadData() => Write(AnAnStore.LoadDataSheets(D));

    // ------------------------------------------------------------------ nạp file Excel

    public sealed class NapPreview
    {
        public string Path = "", FileName = "", Sha = "", DataDate = "";
        public long Size;
        public SheetText.Book Book = null!;
        public string Sheet = "";
        public AnAnData.Prepared Prepared = null!;
        public NapExcel.Check Check = null!;
        public List<NapExcel.CmpLine> CmpLines = new();
        public List<string> Warns = new();
        public bool Blocking => Check.Errors.Count > 0;
        public bool HasErrRows => Check.Err > 0;
    }

    public NapExcel.Snapshot? CurrentSnapshot()
    {
        if (D.Rows.Count == 0) return null;
        var iMa = D.Headers.FindIndex(h => JsText.Fuzzy(h) == JsText.Fuzzy("Mã hàng")); var iNet = D.Headers.FindIndex(h => JsText.Fuzzy(h) == JsText.Fuzzy("Tồn cuối (NET)"));
        var items = new HashSet<string>(); double kg = 0;
        for (var r = 0; r < D.Rows.Count; r++)
        {
            if (iMa >= 0) { var m = JsText.JsTrim(D.Cell(r, iMa)); if (m.Length > 0) items.Add(m); }
            if (iNet >= 0 && NapExcel.Num(D.Cell(r, iNet)) is double n && !double.IsNaN(n)) kg += n;
        }
        return new NapExcel.Snapshot(D.Rows.Count, items.Count, kg, D.DataDate);
    }

    /// <summary>Bước 1–2: đọc + kiểm tra file (CHƯA đổi gì).</summary>
    public NapPreview Preview(string path)
    {
        RequireEdit();
        var bytes = File.ReadAllBytes(path);
        var book = SheetText.Read(path);
        if (book.SheetNames.Count == 0) throw new BusinessException("File không có sheet nào.");
        var name = System.IO.Path.GetFileName(path);
        var sheet = book.SheetNames[0];
        var P = AnAnData.Prepare(book.Sheets[sheet]);
        var chk = NapExcel.CheckM1(P.Headers, P.Rows, P.HeaderRowIdx + 1, P.Dropped, P.RowNums);
        if (book.SheetNames.Count > 1) chk.Infos.Add($"File có {book.SheetNames.Count} sheet — đang nạp sheet đầu tiên “{sheet}”; bấm tên sheet khác ở Load Data sẽ THAY dữ liệu ngay.");
        chk.Infos.Add("Số thứ tự dòng bên dưới tính từ dòng dữ liệu đầu tiên sau dòng tiêu đề.");
        var dd = DataDate.Guess(name);
        if (dd.Length == 0) chk.Warns.Add("Chưa đoán được ngày số liệu từ tên file — nhớ nhập ở ô “Ngày số liệu” sau khi nạp.");
        var pv = new NapPreview { Path = path, FileName = name, Size = bytes.LongLength, Sha = NapExcel.Sha256(bytes), DataDate = dd, Book = book, Sheet = sheet, Prepared = P, Check = chk };
        var log = NapExcel.ReadLog(_src(), AnAnStore.Db);
        var (same, day) = NapExcel.FindDuplicate(log, pv.Sha, dd);
        var cur = CurrentSnapshot();
        var (lines, cw) = NapExcel.Compare(cur is null ? null : cur with { Date = DataDate.Iso(cur.Date) }, new NapExcel.Snapshot(chk.Total, chk.Items, chk.Kg, DataDate.Iso(dd)));
        pv.CmpLines = lines;
        pv.Warns.AddRange(chk.Warns); pv.Warns.AddRange(cw);
        if (same is not null) pv.Warns.Insert(0, $"File này ĐÃ ĐƯỢC NẠP lúc {NapExcel.When(NapExcel.S(same["time"]))} ({Or(NapExcel.S(same["user"]), "?")}{(NapExcel.S(same["host"]).Length > 0 ? " · máy " + NapExcel.S(same["host"]) : "")}). Nạp lại sẽ thay thế đúng số liệu đó — chắc chắn chưa?");
        else if (day is not null) pv.Warns.Insert(0, $"Ngày số liệu {dd} đã được nạp lúc {NapExcel.When(NapExcel.S(day["time"]))} bằng file khác (\"{Or(NapExcel.S(day["fileName"]), "?")}\").");
        return pv;
    }

    private static string Or(string s, string d) => s.Length > 0 ? s : d;

    public int KeepDays
    {
        get { var n = int.TryParse(_settings?.Get(SetNapDays), out var x) ? x : 30; return n is 7 or 15 or 30 ? n : 30; }
        set => _settings?.Set(SetNapDays, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Bước 3–4: người dùng bấm Nạp → chụp bản hoàn tác + ảnh chụp lần trước, thay dữ liệu, ghi nhật ký.</summary>
    public void Commit(NapPreview pv, bool keepUndo = true)
    {
        RequireEdit();
        if (pv.Blocking) throw new BusinessException("File có lỗi nặng — không nạp được.");
        var src = _src();
        var id = NapExcel.NewId();
        var hasUndo = false;
        if (keepUndo)
        {
            var before = new Dictionary<string, string> { [AnAnStore.SLoadData] = src.Read(AnAnStore.Db, AnAnStore.SLoadData), [AnAnStore.SMeta] = src.Read(AnAnStore.Db, AnAnStore.SMeta) };
            try { Undo.Save(AnAnStore.Db, id, before, File.Exists(pv.Path) ? File.ReadAllBytes(pv.Path) : null, pv.FileName); hasUndo = true; Undo.Purge(KeepDays); }
            catch (Exception e) { throw new BusinessException("Không lưu được bản hoàn tác: " + e.Message); }
        }
        var t0 = NapExcel.IsoNow();
        // giữ ảnh chụp lần nạp TRƯỚC để so sánh
        try { if (SoSanhNap.SnapSheet(D, NowMs()) is { } snap) Write(new() { [AnAnStore.SSnap] = snap }); } catch { /* không chặn nạp */ }
        Workbook = pv.Book;
        D.Sheets = pv.Book.SheetNames.ToList();
        D.FileName = pv.FileName;
        D.ImportedAt = NowMs();
        D.DataDate = pv.DataDate;
        D.NapId = id;
        ApplySheet(pv.Sheet, pv.Prepared);
        SaveLoadData();
        MauFile.Luu(src, AnAnStore.Db, MauFile.Of(pv, UserName));   // 1.73: nhớ mẫu file
        _openNapId = id;
        var info = CurrentSnapshot() ?? new NapExcel.Snapshot(0, 0, 0, "");
        var log = NapExcel.ReadLog(src, AnAnStore.Db);
        NapExcel.AddEntry(log, new JsonObject
        {
            ["id"] = id, ["time"] = t0, ["user"] = UserName, ["host"] = Environment.MachineName, ["fileName"] = pv.FileName, ["size"] = pv.Size, ["sha"] = pv.Sha,
            ["rows"] = info.Rows, ["items"] = info.Items, ["kg"] = Math.Round(info.Kg * 100) / 100, ["dataDate"] = pv.DataDate, ["sheet"] = pv.Sheet,
            ["status"] = "applied", ["hasUndo"] = hasUndo ? 1 : 0, ["warn"] = pv.Check.Warn, ["err"] = pv.Check.Err,
        });
        TryWrite(new() { [NapExcel.LogSheet] = NapExcel.LogSheetRows(log) });
        Log("Nạp file", $"{pv.FileName} · {info.Rows} dòng · ngày số liệu {Or(pv.DataDate, "(chưa có)")}");
        AfterDataChanged();
    }

    private void ApplySheet(string name, AnAnData.Prepared P)
    {
        D.CurrentSheet = name;
        D.Headers = P.Headers; D.Rows = P.Rows; D.NumCells.Clear(); D.ManualAssign.Clear();
        D.HeaderRowInfo = new JsonObject
        {
            ["rowNumber"] = P.HeaderRowIdx + 1, ["skippedRows"] = P.HeaderRowIdx, ["matchScore"] = P.Score,
            ["rawPreview"] = new JsonArray(P.Filtered.Take(6).Select(r => (JsonNode)new JsonArray(r.Select(c => (JsonNode)(c ?? "")).ToArray())).ToArray()),
        };
        D.Invalidate();
    }

    /// <summary>Đổi sang sheet khác của file vừa nạp (THAY dữ liệu ngay như app cũ).</summary>
    public void SwitchSheet(string name)
    {
        RequireEdit();
        if (Workbook is null || !Workbook.Sheets.TryGetValue(name, out var raw)) throw new BusinessException("Chỉ đổi sheet được với file vừa nạp trong lần mở này.");
        ApplySheet(name, AnAnData.Prepare(raw));
        SaveLoadData();
        if (_openNapId is { } id)
        {
            var log = NapExcel.ReadLog(_src(), AnAnStore.Db); var x = log.FirstOrDefault(r => NapExcel.S(r["id"]) == id);
            if (x is not null) { var info = CurrentSnapshot(); x["sheet"] = name; x["rows"] = info?.Rows ?? 0; x["items"] = info?.Items ?? 0; x["kg"] = Math.Round((info?.Kg ?? 0) * 100) / 100; TryWrite(new() { [NapExcel.LogSheet] = NapExcel.LogSheetRows(log) }); }
        }
        AfterDataChanged();
    }

    private void AfterDataChanged()
    {
        D.DacTinhBreakdown(out var known);
        if (known) TryWrite(new() { [AnAnStore.SDacTinh] = AnAnStore.DacTinhSheet(D) });
        EnsureTdtSnapshot();
    }

    // ------------------------------------------------------------------ lịch sử nạp + hoàn tác

    public List<JsonObject> History() => AnAnData.StableSort(NapExcel.ReadLog(_src(), AnAnStore.Db), (a, b) => string.CompareOrdinal(NapExcel.S(b["time"]), NapExcel.S(a["time"])));

    /// <summary>Chỉ hoàn tác được đợt nạp MỚI NHẤT đang dùng, trên đúng máy đã nạp (còn bản hoàn tác).</summary>
    public bool CanUndo(JsonObject entry, List<JsonObject> log) =>
        CanEdit && NapExcel.LastApplied(log) is { } la && NapExcel.S(la["id"]) == NapExcel.S(entry["id"]) && Undo.Has(AnAnStore.Db, NapExcel.S(entry["id"]));

    public void UndoNap(string id)
    {
        RequireEdit();
        var log = NapExcel.ReadLog(_src(), AnAnStore.Db);
        var x = log.FirstOrDefault(r => NapExcel.S(r["id"]) == id) ?? throw new BusinessException("Không thấy đợt nạp này.");
        if (NapExcel.LastApplied(log) is not { } la || NapExcel.S(la["id"]) != id) throw new BusinessException("Chỉ hoàn tác được đợt nạp mới nhất đang dùng.");
        var data = Undo.Before(AnAnStore.Db, id);
        if (data.Count == 0) throw new BusinessException("Bản hoàn tác trống.");
        Write(data);
        NapExcel.MarkUndone(log, id, UserName);
        TryWrite(new() { [NapExcel.LogSheet] = NapExcel.LogSheetRows(log) });
        Log("Hoàn tác nạp", NapExcel.S(x["fileName"]));
        Workbook = null; _openNapId = null;
        Load();
    }

    // ------------------------------------------------------------------ so sánh 2 lần nạp

    public (SoSanhNap.Saved Prev, SoSanhNap.Result R)? CompareWithPrevious()
    {
        var prev = SoSanhNap.ReadSaved(AnAnStore.ReadJsonCell(_src(), AnAnStore.SSnap));
        if (prev is null) return null;
        return (prev, SoSanhNap.Compare(prev.Snap, SoSanhNap.SnapNow(D)));
    }

    // ------------------------------------------------------------------ sửa nhỏ

    public void SetDataDate(string raw)
    {
        RequireEdit();
        var v = DataDate.Parse(raw);
        if (JsText.JsTrim(raw).Length > 0 && v.Length == 0) throw new BusinessException("Ngày không hợp lệ — gõ dạng dd/mm/yyyy.");
        if (v == D.DataDate) return;
        D.DataDate = v;
        Write(new() { [AnAnStore.SMeta] = AnAnStore.JsonCell(AnAnStore.MetaJson(D)) });
        EnsureTdtSnapshot();
    }

    public void SetTpcThreshold(double n)
    {
        D.TpcThreshold = double.IsFinite(n) && n >= 0 ? n : AnAnData.TpcThresholdDefault;
        D.Invalidate();
        _settings?.Set(KeyTpcThreshold, D.TpcThreshold.ToString(CultureInfo.InvariantCulture));
    }

    public void MoveRows(IEnumerable<int> rows, string modId)
    {
        RequireEdit();
        if (!AnAnData.FilterOrder.Contains(modId)) return;
        foreach (var i in rows) D.ManualAssign[i] = modId;
        D.Invalidate(); SaveLoadData();
    }

    public void RevertRows(IEnumerable<int> rows)
    {
        RequireEdit();
        foreach (var i in rows) D.ManualAssign.Remove(i);
        D.Invalidate(); SaveLoadData();
    }

    public static string NewCfId() => "cf_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + Guid.NewGuid().ToString("N")[..5];

    public void AddCustomFilter(string modId, JsonObject cf)
    {
        RequireEdit();
        var raw = D.CustomRaw.ToDictionary(k => k.Key, k => k.Value.ToList());
        if (!raw.TryGetValue(modId, out var list)) raw[modId] = list = new();
        list.Add(cf);
        D.SetCustom(raw);
        Write(new() { [AnAnStore.SCustom] = AnAnStore.CustomSheet(D) });
    }

    public void RemoveCustomFilter(string modId, int index)
    {
        RequireEdit();
        var raw = D.CustomRaw.ToDictionary(k => k.Key, k => k.Value.ToList());
        if (raw.TryGetValue(modId, out var list) && index >= 0 && index < list.Count) list.RemoveAt(index);
        D.SetCustom(raw);
        Write(new() { [AnAnStore.SCustom] = AnAnStore.CustomSheet(D) });
    }

    public void SaveDacTinh() { RequireEdit(); Write(new() { [AnAnStore.SDacTinh] = AnAnStore.DacTinhSheet(D) }); }

    public void AssignGroup(IEnumerable<string> labels, string group) { RequireEdit(); D.AssignGroup(labels, group); SaveDacTinh(); }
    public void Ungroup(string key) { RequireEdit(); D.Ungroup(key); SaveDacTinh(); }
    public void ClearDacTinh() { RequireEdit(); D.ClearDacTinh(); D.DacTinhBreakdown(out _); SaveDacTinh(); }

    public void SetManualValue(string label, double? kg)
    {
        RequireEdit();
        D.ManualValues[AnAnData.LabelKey(label)] = kg;
        D.Invalidate();
        Write(new() { [AnAnStore.SNgss] = AnAnStore.NgssSheet(D) });
    }

    // ------------------------------------------------------------------ Kho Gửi (Tồn kho gửi)

    public sealed record KhoGuiInfo(double Kg, int Rows, string DataDate, string ImportedAt) { public string Sig => $"{JsText.JsStr(Kg)}|{Rows}|{DataDate}|{ImportedAt}"; }

    public KhoGuiInfo ReadKhoGui()
    {
        var src = _src();
        JsonArray rows;
        try { rows = JsonNode.Parse(src.Read("ton-kho-gui", "DuLieuChinh")) as JsonArray ?? new(); } catch { rows = new(); }
        double kg = 0;
        foreach (var r in rows.OfType<JsonObject>()) { var w = JsText.LooseNum(AnAnStore.S(r["Trọng Lượng (KG)"])); if (!double.IsNaN(w)) kg += w; }
        string dd = "", ia = "";
        try
        {
            var a = JsonNode.Parse(src.Read("ton-kho-gui", "DuLieuMeta")) as JsonArray;
            if (a is { Count: > 0 } && a[0]?["data"] is JsonValue v && v.TryGetValue<string>(out var cell) && cell.StartsWith(AnAnStore.JsonTag) && JsonNode.Parse(cell[AnAnStore.JsonTag.Length..]) is JsonObject m)
            { dd = AnAnStore.S(m["dataDate"]); ia = AnAnStore.S(m["importedAt"]); }
        }
        catch { /* thiếu meta */ }
        return new KhoGuiInfo(JsText.JsRound(kg * 100) / 100, rows.Count, dd, ia);
    }

    /// <summary>autoSyncKhoGui: Tồn kho gửi đổi (chữ ký khác lần trước) → cập nhật số "Báo Cáo Kho Gửi". Trả về số cũ nếu đã đổi, null nếu không.</summary>
    public (bool Changed, double? Old, KhoGuiInfo Info)? SyncKhoGui(bool force)
    {
        var info = ReadKhoGui();
        if (info.Rows == 0) return null;
        var last = _settings?.Get(SetKhoGuiSig) ?? "";
        if (!force && info.Sig == last) return (false, D.KhoGui, info);
        if (!CanEdit) return (false, D.KhoGui, info);
        var old = D.KhoGui;
        D.ManualValues[AnAnData.LabelKey(AnAnData.KhoGuiLabel)] = info.Kg;
        D.Invalidate();
        Write(new() { [AnAnStore.SNgss] = AnAnStore.NgssSheet(D) });
        _settings?.Set(SetKhoGuiSig, info.Sig);
        return (old is not double o || Math.Abs(o - info.Kg) >= 0.005, old, info);
    }

    // ------------------------------------------------------------------ Xuất Hàng

    public static string SlipId(JsonObject s) => AnAnStore.S(s["id"]);
    public static bool SlipDeducted(JsonObject s) => s["deducted"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    public static List<JsonObject> SlipLines(JsonObject s) => (s["lines"] as JsonArray ?? new()).OfType<JsonObject>().ToList();
    /// <summary>Number(x) của 1 ô JSON (số, chữ số, bool) — đọc được cả nút tạo trong bộ nhớ (int / long / double).</summary>
    public static double Num(JsonNode? n)
    {
        if (n is not JsonValue v) return double.NaN;
        if (v.TryGetValue<string>(out var s)) return JsText.JsNumber(s);
        if (v.TryGetValue<bool>(out var b)) return b ? 1 : 0;
        return double.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }
    private static double Qty(JsonObject l) { var q = Num(l["qty"]); return double.IsNaN(q) ? 0 : q; }
    public static double SlipTotal(JsonObject s) => SlipLines(s).Sum(l => { var q = Qty(l); return double.IsFinite(q) ? q : 0; });

    private void SaveSlips() => Write(new() { [AnAnStore.SPhieu] = AnAnStore.JsonCell(new JsonObject { ["slips"] = new JsonArray(Slips.Select(x => (JsonNode)x.DeepClone()).ToArray()), ["seq"] = SlipSeq }) });

    /// <summary>Tạo phiếu từ các dòng chọn (chỉ ghi nhận, CHƯA trừ tồn).</summary>
    public JsonObject? CreateSlip(string modId, IReadOnlyDictionary<int, double> qtyByRow)
    {
        RequireEdit();
        var cols = D.FilterCols();
        int? Ci(string l) => cols.FirstOrDefault(c => c.Label == l)?.Ci;
        int? cMa = Ci("Mã hàng"), cTen = Ci("Tên hàng"), cSize = Ci("Size"), cHd = Ci("Hợp đồng");
        var lines = new JsonArray();
        foreach (var (i, q) in qtyByRow.OrderBy(k => k.Key))
        {
            if (!(q > 0)) continue;
            string V(int? c) => c is int x ? D.Cell(i, x) : "";
            lines.Add(new JsonObject { ["i"] = i, ["maHang"] = V(cMa), ["tenHang"] = V(cTen), ["size"] = V(cSize), ["hopDong"] = V(cHd), ["qty"] = q });
        }
        if (lines.Count == 0) return null;
        var label = AnAnData.FilterModules.FirstOrDefault(m => m.Id == modId)?.Label ?? modId;
        var slip = new JsonObject
        {
            ["id"] = "PX-" + ((long)SlipSeq).ToString(CultureInfo.InvariantCulture).PadLeft(4, '0'), ["moduleId"] = modId, ["moduleLabel"] = label,
            ["createdAt"] = NapExcel.IsoNow(), ["lines"] = lines, ["deducted"] = false, ["deductedAt"] = null,
        };
        SlipSeq++;
        Slips.Insert(0, slip);
        SaveSlips();
        Log("Tạo phiếu xuất", $"{SlipId(slip)} · {label} · {lines.Count} dòng");
        return slip;
    }

    private void ApplySlip(JsonObject slip, int sign)
    {
        var ci = D.Headers.FindIndex(h => JsText.Fuzzy(h) == JsText.Fuzzy("Tồn cuối"));
        if (ci == -1) throw new BusinessException("Không tìm thấy cột \"Tồn cuối\" trong dữ liệu Load Data — không thể " + (sign < 0 ? "trừ" : "hủy trừ") + " tồn kho.");
        foreach (var l in SlipLines(slip))
        {
            var di = Num(l["i"]); var i = double.IsFinite(di) ? (int)di : -1;
            if (i < 0 || i >= D.Rows.Count) continue;
            var cur = JsText.LooseNum(D.Cell(i, ci));
            var row = D.Rows[i];
            if (ci >= row.Length) { Array.Resize(ref row, D.Headers.Count); D.Rows[i] = row; }
            row[ci] = JsText.JsStr((double.IsNaN(cur) ? 0 : cur) + sign * Qty(l));
            D.NumCells.Add(AnAnData.CellKey(i, ci));
        }
        D.Invalidate();
    }

    public void DeductSlip(string id)
    {
        RequireEdit();
        var s = Slips.FirstOrDefault(x => SlipId(x) == id) ?? throw new BusinessException("Không thấy phiếu " + id);
        if (SlipDeducted(s)) return;
        ApplySlip(s, -1);
        s["deducted"] = true; s["deductedAt"] = NapExcel.IsoNow();
        SaveLoadData(); SaveSlips();
        Log("Trừ tồn kho", id);
        EnsureTdtSnapshot();
    }

    public void UndoSlip(string id)
    {
        RequireEdit();
        var s = Slips.FirstOrDefault(x => SlipId(x) == id) ?? throw new BusinessException("Không thấy phiếu " + id);
        if (!SlipDeducted(s)) return;
        ApplySlip(s, +1);
        s["deducted"] = false; s["deductedAt"] = null;
        SaveLoadData(); SaveSlips();
        Log("Hủy trừ tồn kho", id);
        EnsureTdtSnapshot();
    }

    // ------------------------------------------------------------------ Theo Dõi Tồn

    public void SaveTdt() => Write(new() { [AnAnStore.STheoDoi] = Tdt.ToRows() });

    /// <summary>Xóa sạch dữ liệu Load Data (giữ Đặc Tính, Báo cáo ngoài sổ sách) — như nút "Xóa dữ liệu" của app cũ.</summary>
    public void ClearLoadData()
    {
        RequireEdit();
        D.Headers = new(); D.Rows = new(); D.Sheets = new(); D.CurrentSheet = null; D.FileName = ""; D.ImportedAt = null; D.DataDate = ""; D.NumCells.Clear(); D.ManualAssign.Clear(); D.HeaderRowInfo = null;
        D.Invalidate(); Workbook = null;
        SaveLoadData();
        Log("Xóa dữ liệu Load Data", "");
    }
}

using System.Globalization;
using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;

namespace KhoLanh.Core.TonKhoGui;

/// <summary>
/// Module Tồn kho gửi (C#, thay bản WebView2 ton-kho-gui.html) — dùng CHUNG database "ton-kho-gui" với app cũ. Quyền: m2 (Xem / Xuất / Sửa).
/// Nạp file an toàn (xem trước → nạp, hoàn tác, nhật ký), Dữ Liệu, 6 nhóm Phân Loại + điều kiện tự thêm, Đặc Tính, Báo Cáo (Bảng Tổng Hợp),
/// Đối Soát, Phiếu Xuất (trừ / hoàn Số kiện + Trọng lượng).
/// </summary>
public sealed class GuiService
{
    public const string PermKey = "m2";
    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly SettingsService? _settings;
    public NapUndoStore Undo { get; }

    public GuiData D { get; private set; } = new();
    public List<GuiStore.Slip> Slips { get; private set; } = new();
    public bool Loaded { get; private set; }
    public SheetText.Book? Workbook { get; private set; }
    public double SlipCounter { get; private set; }

    public GuiService(Func<ISheetSource> source, Session session, AuditService audit, SettingsService? settings = null, NapUndoStore? undo = null)
    { _src = source; _session = session; _audit = audit; _settings = settings; Undo = undo ?? NapUndoStore.Default(); }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    private void RequireEdit() => _session.Require(PermKey, Perm.Edit);
    private string UserName => _session.CurrentUser is { } u ? (u.FullName.Length > 0 ? u.FullName : u.Username) : "";
    private void Log(string action, string detail) { try { _audit.Log(_session.Username, "Tồn kho gửi: " + action, detail); } catch { } }
    private static double NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private void Write(Dictionary<string, JsonArray?> sheets) => _src().Write(GuiStore.Db, sheets);
    private void TryWrite(Dictionary<string, JsonArray?> sheets) { try { Write(sheets); } catch { } }
    public int KeepDays { get { var n = int.TryParse(_settings?.Get("nap_keep_days"), out var x) ? x : 30; return n is 7 or 15 or 30 ? n : 30; } set => _settings?.Set("nap_keep_days", value.ToString(CultureInfo.InvariantCulture)); }

    // ------------------------------------------------------------------ đọc
    public void Load()
    {
        var src = _src();
        D = GuiStore.Load(src);
        Slips = GuiStore.LoadSlips(src);
        RecomputeCounter(); MigrateLegacyBatch(); RecomputeFlags();
        Loaded = true;
        // ghi nhận đặc tính mới thấy (như getUniqueDacTinhValues) — chỉ người được sửa
        var (_, newSeen) = D.UniqueDacTinh();
        if (newSeen && CanEdit) TryWrite(new() { [GuiStore.SSeen] = GuiStore.SeenSheet(D) });
    }

    // ------------------------------------------------------------------ nạp file Excel
    public sealed class NapPreview
    {
        public string Path = "", FileName = "", Sha = "", DataDate = "", Sheet = "", ReadInfo = "";
        public long Size;
        public SheetText.Book Book = null!;
        public List<object?[]> Raw = new();
        public GuiData.Extracted? Extracted;
        public List<string> RawDacTinh = new();
        public NapExcel.Check Check = new();
        public List<NapExcel.CmpLine> CmpLines = new();
        public List<string> Warns = new();
        public bool Blocking => Check.Errors.Count > 0;
    }

    public NapExcel.Snapshot? CurrentSnapshot()
    {
        if (D.Rows.Count == 0) return null;
        var items = new HashSet<string>(); double kg = 0;
        foreach (var r in D.Rows) { var m = JsText.JsTrim(r.MaHang); if (m.Length > 0) items.Add(m); kg += r.TrongLuong; }
        return new NapExcel.Snapshot(D.Rows.Count, items.Count, kg, D.DataDate);
    }

    public NapPreview Preview(string path, string? sheet = null)
    {
        RequireEdit();
        var bytes = File.ReadAllBytes(path);
        var book = SheetText.Read(path);
        if (book.SheetNames.Count == 0) throw new BusinessException("File không có sheet nào.");
        var name = System.IO.Path.GetFileName(path);
        var pv = new NapPreview { Path = path, FileName = name, Size = bytes.LongLength, Sha = NapExcel.Sha256(bytes), Book = book };
        FillPreview(pv, sheet ?? book.SheetNames[0], path);
        if (sheet is null && pv.Extracted is null)   // sheet đầu không khớp mẫu → thử các sheet còn lại, lấy sheet đầu tiên khớp
            foreach (var other in book.SheetNames.Skip(1)) { var t = new NapPreview { Path = path, FileName = name, Size = pv.Size, Sha = pv.Sha, Book = book }; FillPreview(t, other, path); if (t.Extracted is not null) return t; }
        return pv;
    }

    /// <summary>Đọc sheet (giá trị gốc: số / ngày / chữ) và kiểm tra.</summary>
    public void FillPreview(NapPreview pv, string sheet, string? path = null)
    {
        pv.Sheet = sheet;
        pv.Raw = GuiRaw.ReadRaw(path ?? pv.Path, sheet, pv.Book);
        pv.Extracted = GuiData.Extract(pv.Raw);
        var text = pv.Raw.Select(r => r.Select(c => c is double d ? JsText.JsStr(d) : Convert.ToString(c, CultureInfo.InvariantCulture) ?? "").ToArray()).ToList();
        pv.RawDacTinh = pv.Extracted is null ? GuiData.ExtractDacTinhRaw(text) : new();
        pv.DataDate = DataDate.Guess(pv.FileName);
        var rows = pv.Extracted?.Rows ?? new();
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        pv.Check = NapExcel.CheckM2(rows.Select(r => (r.MaHang, r.SoLo, r.PhieuNhap, r.Size, r.QuyCach, r.NgayNhapKho, r.SoKienRaw, r.TrongLuongRaw)).ToList(), today);
        if (pv.Extracted is null) { pv.Check.Errors.Clear(); pv.Check.Errors.Add("Không tìm thấy đủ cột theo mẫu (Mã hàng / Tên hàng / Số lô / Size / Quy cách / Ngày nhập kho / Phiếu nhập-Voucher / Tồn cuối kỳ) trong sheet \"" + sheet + "\"."); }
        else if (pv.Extracted.ReadInfo.Length > 0) pv.Check.Infos.Add(pv.Extracted.ReadInfo);
        if (pv.Book.SheetNames.Count > 1) pv.Check.Infos.Add($"File có {pv.Book.SheetNames.Count} sheet — đang xem sheet “{sheet}”.");
        var active = Slips.Count(IsActive);
        if (active > 0) pv.Check.Infos.Add($"{active} phiếu xuất đang trừ tồn theo số liệu cũ sẽ bị KHÓA (chỉ còn xem / xuất Excel / xóa), không trừ vào số liệu mới.");
        if (pv.DataDate.Length == 0) pv.Check.Warns.Add("Chưa có “Ngày số liệu” cho file này — nhập ở ô Ngày số liệu để báo cáo / điện thoại hiện đúng ngày.");
        pv.Check.Infos.Add("Thay thế toàn bộ menu “Dữ Liệu”; mọi tab khác cập nhật theo.");
        var log = NapExcel.ReadLog(_src(), GuiStore.Db);
        var (same, day) = NapExcel.FindDuplicate(log, pv.Sha, pv.DataDate);
        var cur = CurrentSnapshot();
        var (lines, cw) = NapExcel.Compare(cur is null ? null : cur with { Date = DataDate.Iso(cur.Date) }, new NapExcel.Snapshot(pv.Check.Total, pv.Check.Items, rows.Sum(r => r.TrongLuong), DataDate.Iso(pv.DataDate)));
        pv.CmpLines = lines; pv.Warns.Clear(); pv.Warns.AddRange(pv.Check.Warns); pv.Warns.AddRange(cw);
        if (same is not null) pv.Warns.Insert(0, $"File này ĐÃ ĐƯỢC NẠP lúc {NapExcel.When(NapExcel.S(same["time"]))} ({Or(NapExcel.S(same["user"]), "?")}).");
        else if (day is not null) pv.Warns.Insert(0, $"Ngày số liệu {pv.DataDate} đã được nạp lúc {NapExcel.When(NapExcel.S(day["time"]))} bằng file khác (\"{Or(NapExcel.S(day["fileName"]), "?")}\").");
    }
    private static string Or(string s, string d) => s.Length > 0 ? s : d;

    public void Commit(NapPreview pv, bool keepUndo = true)
    {
        RequireEdit();
        if (pv.Blocking || pv.Extracted is null) throw new BusinessException("File có lỗi nặng — không nạp được.");
        var src = _src(); var id = NapExcel.NewId(); var hasUndo = false;
        if (keepUndo)
        {
            var before = new Dictionary<string, string> { [GuiStore.SRows] = src.Read(GuiStore.Db, GuiStore.SRows), [GuiStore.SMeta] = src.Read(GuiStore.Db, GuiStore.SMeta) };
            try { Undo.Save(GuiStore.Db, id, before, File.Exists(pv.Path) ? File.ReadAllBytes(pv.Path) : null, pv.FileName); hasUndo = true; Undo.Purge(KeepDays); }
            catch (Exception e) { throw new BusinessException("Không lưu được bản hoàn tác: " + e.Message); }
        }
        var t0 = NapExcel.IsoNow();
        Workbook = pv.Book;
        D.Rows = pv.Extracted.Rows.Select(r => r.Clone()).ToList();
        D.RawDacTinhValues = new();
        D.ImportedAt = NowMs(); D.DataDate = pv.DataDate; D.NapId = id;
        RecomputeFlags();
        var sheets = new Dictionary<string, JsonArray?> { [GuiStore.SRows] = GuiStore.RowsSheet(D.Rows), [GuiStore.SMeta] = GuiStore.MetaSheet(D) };
        sheets[GuiStore.SFile] = FileNapRows(pv); sheets[GuiStore.SFileMeta] = AnAnStore.JsonCell(new JsonObject { ["fileName"] = pv.FileName, ["size"] = pv.Size, ["sheetCount"] = pv.Book.SheetNames.Count, ["sheet"] = pv.Sheet, ["cols"] = pv.Raw.Count == 0 ? 0 : pv.Raw.Max(r => r.Length), ["savedAt"] = t0 });
        Write(sheets);
        MauFile.Luu(src, GuiStore.Db, MauFile.Of(pv, UserName));   // 1.73: nhớ mẫu file
        var info = CurrentSnapshot() ?? new NapExcel.Snapshot(0, 0, 0, "");
        var log = NapExcel.ReadLog(src, GuiStore.Db);
        NapExcel.AddEntry(log, new JsonObject
        {
            ["id"] = id, ["time"] = t0, ["user"] = UserName, ["host"] = Environment.MachineName, ["fileName"] = pv.FileName, ["size"] = pv.Size, ["sha"] = pv.Sha,
            ["rows"] = info.Rows, ["items"] = info.Items, ["kg"] = Math.Round(info.Kg * 100) / 100, ["dataDate"] = pv.DataDate, ["sheet"] = pv.Sheet,
            ["status"] = "applied", ["hasUndo"] = hasUndo ? 1 : 0, ["warn"] = pv.Check.Warn, ["err"] = pv.Check.Err,
        });
        TryWrite(new() { [NapExcel.LogSheet] = NapExcel.LogSheetRows(log) });
        Log("Nạp file", $"{pv.FileName} · {info.Rows} dòng · ngày số liệu {Or(pv.DataDate, "(chưa có)")}");
        // phiếu của đợt cũ bị khóa (loadAt khác importedAt) → lưu lại cờ
        var (_, newSeen) = D.UniqueDacTinh(); if (newSeen) TryWrite(new() { [GuiStore.SSeen] = GuiStore.SeenSheet(D) });
    }
    private static JsonArray FileNapRows(NapPreview pv)
    {
        var w = pv.Raw.Count == 0 ? 0 : pv.Raw.Max(r => r.Length);
        return new(pv.Raw.Select(r => { var o = new JsonObject(); for (var i = 0; i < w; i++) o["c" + i] = i < r.Length ? ObjStr(r[i]) : ""; return (JsonNode)o; }).ToArray());
    }
    private static string ObjStr(object? v) => v switch { null => "", double d => JsText.JsStr(d), DateTime t => t.ToOADate().ToString(CultureInfo.InvariantCulture), _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" };

    public List<JsonObject> History() => AnAnData.StableSort(NapExcel.ReadLog(_src(), GuiStore.Db), (a, b) => string.CompareOrdinal(NapExcel.S(b["time"]), NapExcel.S(a["time"])));
    public bool CanUndo(JsonObject entry, List<JsonObject> log) =>
        CanEdit && NapExcel.LastApplied(log) is { } la && NapExcel.S(la["id"]) == NapExcel.S(entry["id"]) && Undo.Has(GuiStore.Db, NapExcel.S(entry["id"]));
    public void UndoNap(string id)
    {
        RequireEdit();
        var log = NapExcel.ReadLog(_src(), GuiStore.Db);
        var x = log.FirstOrDefault(r => NapExcel.S(r["id"]) == id) ?? throw new BusinessException("Không thấy đợt nạp này.");
        if (NapExcel.LastApplied(log) is not { } la || NapExcel.S(la["id"]) != id) throw new BusinessException("Chỉ hoàn tác được đợt nạp mới nhất đang dùng.");
        var data = Undo.Before(GuiStore.Db, id); if (data.Count == 0) throw new BusinessException("Bản hoàn tác trống.");
        Write(data);
        NapExcel.MarkUndone(log, id, UserName); TryWrite(new() { [NapExcel.LogSheet] = NapExcel.LogSheetRows(log) });
        Log("Hoàn tác nạp", NapExcel.S(x["fileName"])); Workbook = null; Load();
    }

    public void SetDataDate(string raw)
    {
        RequireEdit();
        var v = DataDate.Parse(raw);
        if (JsText.JsTrim(raw).Length > 0 && v.Length == 0) throw new BusinessException("Ngày không hợp lệ — gõ dạng dd/mm/yyyy.");
        if (v == D.DataDate) return;
        D.DataDate = v; Write(new() { [GuiStore.SMeta] = GuiStore.MetaSheet(D) });
    }

    /// <summary>Nút "Xóa" của tab Dữ Liệu: làm rỗng "Dữ Liệu" (phiếu xuất vẫn giữ).</summary>
    public void ClearRows()
    {
        RequireEdit();
        Write(new() { [GuiStore.SRows] = new JsonArray() }); D.Rows = new(); Log("Xóa dữ liệu đã lưu", "");
    }

    // ------------------------------------------------------------------ điều kiện tự thêm
    public static string NewCfId() => "cf_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + Guid.NewGuid().ToString("N")[..5];
    public void AddCustomFilter(string tab, CustomFilter cf) { RequireEdit(); if (cf.Id.Length == 0) cf.Id = NewCfId(); D.CustomFor(tab).Add(cf); Write(new() { [GuiStore.SCustom] = GuiStore.CustomSheet(D) }); }
    public void RemoveCustomFilter(string tab, int index) { RequireEdit(); var l = D.CustomFor(tab); if (index >= 0 && index < l.Count) l.RemoveAt(index); Write(new() { [GuiStore.SCustom] = GuiStore.CustomSheet(D) }); }

    // ------------------------------------------------------------------ Đặc tính
    public void SaveManual(IEnumerable<(string MaHang, string DacTinh)> rows) { RequireEdit(); D.DtManual = rows.Where(r => r.DacTinh.Length > 0).ToList(); Write(new() { [GuiStore.SManual] = GuiStore.ManualSheet(D) }); }
    /// <summary>addManualRows: thêm dòng nhập tay (bỏ dòng trùng); trả số dòng thêm được.</summary>
    public int AddManual(IEnumerable<(string MaHang, string DacTinh)> rows)
    {
        RequireEdit(); var seen = D.DtManual.Select(r => GuiData.Nz(r.MaHang) + "|" + GuiData.Nz(r.DacTinh)).ToHashSet(); var added = 0;
        foreach (var r in rows) { if (r.DacTinh.Length == 0) continue; if (!seen.Add(GuiData.Nz(r.MaHang) + "|" + GuiData.Nz(r.DacTinh))) continue; D.DtManual.Add(r); added++; }
        if (added > 0) Write(new() { [GuiStore.SManual] = GuiStore.ManualSheet(D) });
        return added;
    }
    public void MergeGroup(IEnumerable<string> values, string group)
    {
        RequireEdit();
        if (!GuiData.DtGroups.Contains(group)) throw new BusinessException("Nhóm không hợp lệ.");
        var vs = values.Where(v => !D.DtGroupMap.ContainsKey(v)).ToList(); if (vs.Count == 0) throw new BusinessException("Chọn ít nhất 1 đặc tính chưa được gộp.");
        foreach (var v in vs) D.DtGroupMap[v] = group;
        Write(new() { [GuiStore.SGroup] = GuiStore.GroupSheet(D) });
    }
    public void Unmerge(string value) { RequireEdit(); D.DtGroupMap.Remove(value); Write(new() { [GuiStore.SGroup] = GuiStore.GroupSheet(D) }); }
    public void ClearGroups() { RequireEdit(); D.DtGroupMap = new(); Write(new() { [GuiStore.SGroup] = GuiStore.GroupSheet(D) }); }
    /// <summary>Chuyển tay Số lô (đặc tính CKQ) sang đặc tính thật.</summary>
    public void MapLot(string lo, string dacTinh)
    {
        RequireEdit(); var l = JsText.JsTrim(lo); var dt = JsText.JsTrim(dacTinh);
        if (l.Length == 0 || dt.Length == 0) throw new BusinessException("Chọn Số lô và đặc tính đích.");
        D.DtLoMap[GuiData.LoKey(l)] = (l, dt); Write(new() { [GuiStore.SLoMap] = GuiStore.LoMapSheet(D) });
    }
    public void UnmapLot(string lo) { RequireEdit(); D.DtLoMap.Remove(GuiData.LoKey(lo)); Write(new() { [GuiStore.SLoMap] = GuiStore.LoMapSheet(D) }); }
    public void SaveSeen() => TryWrite(new() { [GuiStore.SSeen] = GuiStore.SeenSheet(D), [GuiStore.SGroup] = GuiStore.GroupSheet(D) });

    // ------------------------------------------------------------------ Phiếu xuất
    public double LoadStamp => D.ImportedAt;
    public bool IsActive(GuiStore.Slip p) => p.LoadAt is null || p.LoadAt.Value == LoadStamp;
    public static bool KgOnly(GuiRow r) => r.SoKien <= 0 && r.TrongLuong > 0;
    public static bool InStock(GuiRow r) => Math.Abs(r.SoKien) >= 0.005 || Math.Abs(r.TrongLuong) >= 0.005;
    public static double RowKg(GuiRow r, double qty) => r.SoKien <= 0 ? (r.TrongLuong > 0 ? qty : 0) : r.TrongLuong * (qty / r.SoKien);
    public static double DefQty(GuiRow r) => KgOnly(r) ? r.TrongLuong : r.SoKien;
    private static string RowKey(string ma, string lo, string size, string phieu) => string.Join("|", new[] { ma, lo, size, phieu }.Select(v => JsText.JsTrim(v ?? "")));
    private Dictionary<string, List<GuiRow>> BuildIndex() { var m = new Dictionary<string, List<GuiRow>>(); foreach (var r in D.Rows) { var k = RowKey(r.MaHang, r.SoLo, r.Size, r.PhieuNhap); if (!m.TryGetValue(k, out var l)) m[k] = l = new(); l.Add(r); } return m; }
    private static GuiRow? Pick(Dictionary<string, List<GuiRow>> idx, GuiStore.SlipItem it)
    {
        if (!idx.TryGetValue(RowKey(it.MaHang, it.SoLo, it.Size, it.PhieuNhap), out var arr) || arr.Count == 0) return null;
        return it.Occ is int o && o >= 0 && o < arr.Count ? arr[o] : arr[^1];
    }
    private static int Occ(Dictionary<string, List<GuiRow>> idx, GuiRow r) { var i = idx.TryGetValue(RowKey(r.MaHang, r.SoLo, r.Size, r.PhieuNhap), out var a) ? a.IndexOf(r) : -1; return i < 0 ? 0 : i; }
    private void ApplyDelta(IEnumerable<GuiStore.SlipItem> items, int sign)
    {
        var idx = BuildIndex();
        foreach (var it in items) { var r = Pick(idx, it); if (r is null) continue; r.SoKien += sign * it.SoLuongXuat; r.TrongLuong += sign * it.TrongLuongTru; }
    }
    private void RecomputeFlags()
    {
        foreach (var r in D.Rows) r.PxDaXuat = false;
        var idx = BuildIndex();
        foreach (var p in Slips.Where(IsActive))
            foreach (var it in p.Items) { if (it.SoLuongXuat == 0 && (it.XuatKg ?? 0) == 0) continue; var r = Pick(idx, it); if (r is not null) r.PxDaXuat = true; }
    }
    private void RecomputeCounter() { double max = 0; foreach (var p in Slips) { var m = System.Text.RegularExpressions.Regex.Match(p.MaPhieu, @"^PX(\d+)$"); if (m.Success) max = Math.Max(max, double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)); } SlipCounter = max; }
    /// <summary>Phiếu cũ chưa gắn đợt số liệu: gắn theo ngày tạo (như pxMigrateLegacyBatch).</summary>
    private void MigrateLegacyBatch()
    {
        var cur = LoadStamp; var changed = false;
        foreach (var p in Slips)
        {
            if (p.LoadAt is not null) continue;
            var ok = DateTimeOffset.TryParse(p.NgayTao, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t);
            if (cur == 0) p.LoadAt = 0; else if (p.Nguon.Length > 0 || !ok) p.LoadAt = cur; else p.LoadAt = t.ToUnixTimeMilliseconds() >= cur ? cur : 1;
            p.LoadDate = p.LoadAt == cur ? D.DataDate : ""; changed = true;
        }
        if (changed && CanEdit) TryWrite(GuiStore.SlipSheets(Slips));
    }
    private void SaveSlipsAndRows() => Write(new Dictionary<string, JsonArray?>(GuiStore.SlipSheets(Slips)) { [GuiStore.SRows] = GuiStore.RowsSheet(D.Rows) });

    /// <summary>pxQtyProblems: SL xuất trống / ≤ 0 / vượt tồn → chuỗi báo lỗi ("" nếu hợp lệ).</summary>
    public static string QtyProblems(IEnumerable<(string Label, string Raw, double? Avail)> list)
    {
        var bad = new List<string>();
        foreach (var (label, raw, avail) in list)
        {
            var q = GuiData.ToNum(raw); var empty = JsText.JsTrim(raw ?? "").Length == 0;
            if (empty || !double.IsFinite(q)) bad.Add($"• {label} — SL xuất đang TRỐNG / không phải số");
            else if (q <= 0) bad.Add($"• {label} — SL xuất = {GuiData.FormatNumber(q)} (phải > 0)");
            else if (avail is double a && q > a + 1e-9) bad.Add($"• {label} — SL xuất {GuiData.FormatNumber(q)} VƯỢT tồn ({GuiData.FormatNumber(a)})");
        }
        if (bad.Count == 0) return "";
        return $"Có {bad.Count} dòng SL xuất chưa hợp lệ:\n\n" + string.Join("\n", bad.Take(12)) + (bad.Count > 12 ? $"\n… và {bad.Count - 12} dòng khác" : "") + "\n\nHãy sửa rồi thử lại.";
    }
    private static string Label(GuiRow r, bool kg) => $"{r.MaHang}{(r.SoLo.Length > 0 ? " · Lô " + r.SoLo : "")}{(r.Size.Length > 0 ? " · " + r.Size : "")}".Trim() + (kg ? " (xuất theo kg)" : "");

    /// <summary>Tạo phiếu từ các dòng (chỉ số trong D.Rows) + số lượng xuất; trừ ngay Số kiện / Trọng lượng.</summary>
    public GuiStore.Slip CreateSlip(IReadOnlyList<(int Row, double Qty)> picks, string? maPhieu = null)
    {
        RequireEdit();
        if (picks.Count == 0) throw new BusinessException("Chưa chọn dòng nào.");
        var msg = QtyProblems(picks.Select(p => { var r = D.Rows[p.Row]; var kg = KgOnly(r); return (Label(r, kg), JsText.JsStr(p.Qty), (double?)(kg ? r.TrongLuong : r.SoKien)); }));
        if (msg.Length > 0) throw new BusinessException(msg);
        var idx = BuildIndex();
        var items = picks.OrderBy(p => p.Row).Select(p =>
        {
            var r = D.Rows[p.Row]; var kg = KgOnly(r);
            var it = new GuiStore.SlipItem
            {
                MaHang = r.MaHang, TenHang = r.TenHang, PhieuNhap = r.PhieuNhap, SoLo = r.SoLo, Size = r.Size, SoKien = JsText.JsStr(r.SoKien), SoLuongXuat = kg ? 0 : p.Qty,
                TrongLuongTru = kg ? p.Qty : (r.SoKien > 0 ? r.TrongLuong * (p.Qty / r.SoKien) : 0), Occ = Occ(idx, r),
            };
            if (kg) it.XuatKg = p.Qty;
            return it;
        }).ToList();
        var ma = JsText.JsTrim(maPhieu ?? "");
        if (ma.Length == 0) { SlipCounter++; ma = "PX" + ((long)SlipCounter).ToString("0000", CultureInfo.InvariantCulture); }
        else { var b = ma; var n = 1; while (Slips.Any(p => p.MaPhieu == ma)) { n++; ma = b + "-" + n; } }
        var slip = new GuiStore.Slip { Id = "px_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + Guid.NewGuid().ToString("N")[..5], MaPhieu = ma, NgayTao = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture), NguoiTao = UserName, LoadAt = LoadStamp, LoadDate = D.DataDate, Items = items };
        Slips.Insert(0, slip);
        ApplyDelta(items, -1); RecomputeFlags();
        SaveSlipsAndRows();
        Log("Tạo phiếu xuất", $"{slip.MaPhieu} · {items.Count} dòng");
        return slip;
    }

    public void DeleteSlip(string id)
    {
        RequireEdit();
        var p = Slips.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không thấy phiếu.");
        var active = IsActive(p);
        Slips.Remove(p);
        if (active) { ApplyDelta(p.Items, +1); RecomputeFlags(); }
        SaveSlipsAndRows();
        Log("Xóa phiếu xuất", p.MaPhieu + (active ? " (đã hoàn tồn)" : " (phiếu khóa, không hoàn tồn)"));
    }

    /// <summary>Sửa phiếu: đổi mã phiếu + số lượng từng dòng (hoàn theo số lượng cũ rồi trừ theo số lượng mới).</summary>
    public void UpdateSlip(string id, string newMa, IReadOnlyList<double> newQty)
    {
        RequireEdit();
        var p = Slips.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không thấy phiếu.");
        if (!IsActive(p)) throw new BusinessException("Phiếu này đã bị khóa vì số liệu tồn đã được cập nhật lại" + (p.LoadDate.Length > 0 ? " (phiếu tính theo số liệu ngày " + p.LoadDate + ")" : "") + ". Không sửa được — chỉ xem / xuất Excel / xóa.");
        if (newQty.Count != p.Items.Count) throw new BusinessException("Số dòng không khớp.");
        newMa = JsText.JsTrim(newMa); if (newMa.Length == 0) newMa = p.MaPhieu;
        if (newMa != p.MaPhieu && Slips.Any(x => x.Id != p.Id && x.MaPhieu == newMa)) throw new BusinessException("Mã phiếu này đã tồn tại, vui lòng chọn mã khác.");
        var idx = BuildIndex(); var old = new Dictionary<GuiRow, double>();
        foreach (var o in p.Items) { var rr = Pick(idx, o); if (rr is not null) old[rr] = old.GetValueOrDefault(rr) + (o.XuatKg ?? o.SoLuongXuat); }
        var msg = QtyProblems(p.Items.Select((it, i) => { var r = Pick(idx, it); var kg = it.XuatKg is not null; return (Label(new GuiRow { MaHang = it.MaHang, SoLo = it.SoLo, Size = it.Size }, kg), JsText.JsStr(newQty[i]), r is null ? (double?)null : (kg ? r.TrongLuong : r.SoKien) + old.GetValueOrDefault(r)); }));
        if (msg.Length > 0) throw new BusinessException(msg);
        ApplyDelta(p.Items, +1);
        var idx2 = BuildIndex();
        for (var i = 0; i < p.Items.Count; i++)
        {
            var it = p.Items[i]; var r = Pick(idx2, it);
            if (it.XuatKg is not null) { it.XuatKg = newQty[i]; it.SoLuongXuat = 0; } else it.SoLuongXuat = newQty[i];
            if (r is not null) { it.Occ ??= Occ(idx2, r); it.TrongLuongTru = it.XuatKg is double xk ? xk : (r.SoKien > 0 ? r.TrongLuong * (it.SoLuongXuat / r.SoKien) : 0); } else it.TrongLuongTru = 0;
        }
        ApplyDelta(p.Items, -1); RecomputeFlags();
        p.MaPhieu = newMa;
        SaveSlipsAndRows();
        Log("Sửa phiếu xuất", p.MaPhieu);
    }

    public (double TotalQty, int Count, List<(GuiStore.Slip Slip, GuiStore.SlipItem Item)> List) PxDeducted()
    {
        double tot = 0; var list = new List<(GuiStore.Slip, GuiStore.SlipItem)>();
        foreach (var p in Slips.Where(IsActive)) foreach (var it in p.Items) { tot += it.SoLuongXuat; list.Add((p, it)); }
        list = AnAnData.StableSort(list, (a, b) => Math.Abs(b.Item2.SoLuongXuat).CompareTo(Math.Abs(a.Item2.SoLuongXuat)));
        return (tot, list.Count, list);
    }
}

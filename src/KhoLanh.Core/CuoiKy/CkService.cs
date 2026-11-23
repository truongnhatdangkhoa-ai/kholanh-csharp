using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;
using static KhoLanh.Core.CuoiKy.Ck;

namespace KhoLanh.Core.CuoiKy;

/// <summary>
/// Số liệu cuối kỳ — phiên đối chiếu lưu ở database dùng chung "so-lieu-cuoi-ky" (sheet DC2_*, y định dạng app cũ):
/// file đã nạp + kỳ + thiết lập (DC2_Phien, DC2_Nguon_*, DC2_ChungTu, DC2_MaKeToan, DC2_DanhMuc) và thao tác người dùng
/// (DC2_QuyetDinh, DC2_DieuChinh, DC2_LichSu, DC2_ChinhSua) + DC2_Rev. Số liệu nguồn không bao giờ bị sửa.
/// </summary>
public sealed class CkService
{
    public const string PermKey = "m7ck", Db = "so-lieu-cuoi-ky";
    private const string SPhien = "DC2_Phien", SRev = "DC2_Rev", SCt = "DC2_ChungTu", SMaKt = "DC2_MaKeToan", SDm = "DC2_DanhMuc", SQd = "DC2_QuyetDinh", SDc = "DC2_DieuChinh", SLs = "DC2_LichSu", SEd = "DC2_ChinhSua";
    private static string SrcSheet(string k) => "DC2_Nguon_" + k;
    private static readonly string[] SrcKinds = { "anan", "ketoan", "gui", "kiemke", "anan_nhap", "anan_xuat" };
    private const int Chunk = 26000;
    private static readonly JsonSerializerOptions J = CkHistory.Json;

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly SettingsService? _settings;
    public CkService(Func<ISheetSource> src, Session session, SettingsService? settings = null) { _src = src; _session = session; _settings = settings; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);

    // ------------------------------------------------------------ phiên
    public CkPeriod? Period { get; set; }
    public CkOptions Options { get; set; } = new();
    public Dictionary<string, CkSource> Files { get; } = new();
    public CkState State { get; private set; } = new();
    public CkResult? Result { get; private set; }
    public int Rev { get; private set; }
    public string SavedBy { get; private set; } = "";
    public string SavedAt { get; private set; } = "";

    public bool Ready => Files.ContainsKey("anan") && Files.ContainsKey("ketoan");

    // ------------------------------------------------------------ chia khúc JSON (< 32.767 ký tự / ô)
    private static JsonArray Chunks<T>(IEnumerable<T> arr)
    {
        var outp = new JsonArray(); var cur = new List<string>(); var len = 2; var i = 0;
        foreach (var x in arr)
        {
            var s = JsonSerializer.Serialize(x, J);
            if (cur.Count > 0 && len + s.Length + 1 > Chunk) { outp.Add(new JsonObject { ["i"] = i++, ["j"] = "[" + string.Join(",", cur) + "]" }); cur.Clear(); len = 2; }
            cur.Add(s); len += s.Length + 1;
        }
        outp.Add(new JsonObject { ["i"] = i, ["j"] = "[" + string.Join(",", cur) + "]" });
        return outp;
    }
    private static List<T> Unchunk<T>(JsonArray rows)
    {
        var outp = new List<T>();
        foreach (var r in rows.OfType<JsonObject>().OrderBy(r => C3dI(r["i"])))
            try { if (JsonSerializer.Deserialize<List<T>>(AnAnStore.S(r["j"]), J) is { } l) outp.AddRange(l); } catch { /* bỏ khúc hỏng */ }
        return outp;
    }
    private static double C3dI(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : double.TryParse(AnAnStore.S(n), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
    private static JsonArray TextChunks(JsonNode? obj)
    {
        var s = obj?.ToJsonString(J) ?? "null"; var outp = new JsonArray();
        for (int i = 0, k = 0; i < s.Length; i += Chunk, k++) outp.Add(new JsonObject { ["i"] = k, ["j"] = s.Substring(i, Math.Min(Chunk, s.Length - i)) });
        return outp;
    }
    private static JsonNode? Untext(JsonArray rows)
    {
        try { var s = string.Concat(rows.OfType<JsonObject>().OrderBy(r => C3dI(r["i"])).Select(r => AnAnStore.S(r["j"]))); return s.Length == 0 ? null : JsonNode.Parse(s); } catch { return null; }
    }
    private JsonArray Read(string sheet) { try { return JsonNode.Parse(_src().Read(Db, sheet)) as JsonArray ?? new(); } catch { return new(); } }

    // ------------------------------------------------------------ nguồn ↔ JSON meta (giống app cũ: bỏ rows / knownCodes / extra)
    private static JsonObject MetaOf(CkSource s)
    {
        var o = new JsonObject
        {
            ["ok"] = true, ["kind"] = s.Kind, ["fileName"] = s.FileName, ["sheet"] = s.Sheet, ["sheets"] = new JsonArray(s.Sheets.Select(x => (JsonNode)x).ToArray()), ["headerRow"] = s.HeaderRow,
            ["loadedAt"] = s.LoadedAt, ["mapping"] = JsonSerializer.SerializeToNode(s.Mapping, J), ["mappingLabels"] = JsonSerializer.SerializeToNode(s.MappingLabels, J),
            ["labels"] = new JsonArray(s.Labels.Select(x => (JsonNode)x).ToArray()),
            ["stats"] = new JsonObject
            {
                ["nRows"] = s.Stats.NRows, ["nCodes"] = s.Stats.NCodes, ["nZero"] = s.Stats.NZero, ["totalKg"] = s.Stats.TotalKg, ["totalDau"] = s.Stats.TotalDau, ["totalNhap"] = s.Stats.TotalNhap, ["totalXuat"] = s.Stats.TotalXuat,
                ["skipped"] = s.Stats.Skipped, ["totals"] = s.Stats.Totals, ["issues"] = s.Stats.Issues, ["counts"] = JsonSerializer.SerializeToNode(s.Stats.Counts, J),
            },
            ["issues"] = new JsonArray(s.Issues.Take(200).Select(i => (JsonNode)new JsonObject { ["type"] = i.Type, ["sev"] = i.Sev, ["row"] = i.Row, ["ma"] = i.Ma, ["msg"] = i.Msg }).ToArray()),
        };
        if (s.Period is { } p) o["period"] = new JsonObject { ["from"] = p.From, ["to"] = p.To, ["source"] = p.Source };
        if (s.PeriodGuess is { } g) o["periodGuess"] = new JsonObject { ["from"] = g.From, ["to"] = g.To, ["source"] = g.Source };
        if (s.DanhMuc is { } dm) o["dmInfo"] = new JsonObject { ["sheet"] = s.DanhMucSheet, ["nRows"] = dm.Count };
        return o;
    }
    private static CkPeriod? PeriodOf(JsonNode? n) => n is JsonObject o && (AnAnStore.S(o["from"]).Length > 0 || AnAnStore.S(o["to"]).Length > 0) ? new CkPeriod(AnAnStore.S(o["from"]), AnAnStore.S(o["to"]), AnAnStore.S(o["source"])) : null;
    private static CkSource FromMeta(string kind, JsonObject o, List<CkRow> rows)
    {
        var s = new CkSource { Ok = true, Kind = kind, FileName = AnAnStore.S(o["fileName"]), Sheet = AnAnStore.S(o["sheet"]), LoadedAt = AnAnStore.S(o["loadedAt"]), Rows = rows, Period = PeriodOf(o["period"]), PeriodGuess = PeriodOf(o["periodGuess"]) };
        try { s.HeaderRow = (int)C3dI(o["headerRow"]); s.Mapping = o["mapping"]?.Deserialize<Dictionary<string, int>>(J) ?? new(); s.MappingLabels = o["mappingLabels"]?.Deserialize<Dictionary<string, string>>(J) ?? new(); } catch { }
        if (o["labels"] is JsonArray la) s.Labels = la.Select(AnAnStore.S).ToList();
        if (o["sheets"] is JsonArray sh) s.Sheets = sh.Select(AnAnStore.S).ToList();
        if (o["stats"] is JsonObject st)
        {
            s.Stats.NRows = (int)C3dI(st["nRows"]); s.Stats.NCodes = (int)C3dI(st["nCodes"]); s.Stats.TotalKg = C3dI(st["totalKg"]); s.Stats.Skipped = (int)C3dI(st["skipped"]); s.Stats.Issues = (int)C3dI(st["issues"]);
            try { s.Stats.Counts = st["counts"]?.Deserialize<Dictionary<string, int>>(J) ?? new(); } catch { }
        }
        if (o["issues"] is JsonArray iss) s.Issues = iss.OfType<JsonObject>().Select(i => new CkIssue(AnAnStore.S(i["type"]), AnAnStore.S(i["sev"]), (int)C3dI(i["row"]), AnAnStore.S(i["ma"]), AnAnStore.S(i["msg"]))).ToList();
        if (o["dmInfo"] is JsonObject dmi) s.DanhMucSheet = AnAnStore.S(dmi["sheet"]);
        return s;
    }

    private JsonObject OptionsJson() => new() { ["tol"] = Options.Tol, ["pairTol"] = Options.PairTol, ["hideBot"] = Options.HideBot, ["showZero"] = Options.ShowZero, ["minScore"] = Options.MinScore, ["maxCand"] = Options.MaxCand, ["kiemKeNgay"] = Options.KiemKeNgay };
    private static CkOptions OptionsOf(JsonNode? n)
    {
        var o = new CkOptions(); if (n is not JsonObject j) return o;
        if (j["tol"] is not null) o.Tol = C3dI(j["tol"]); if (j["pairTol"] is not null) o.PairTol = C3dI(j["pairTol"]); if (j["minScore"] is not null) o.MinScore = C3dI(j["minScore"]);
        if (j["maxCand"] is not null) o.MaxCand = (int)C3dI(j["maxCand"]); o.HideBot = j["hideBot"] is null || AnAnStore.S(j["hideBot"]) == "true"; o.ShowZero = AnAnStore.S(j["showZero"]) == "true"; o.KiemKeNgay = AnAnStore.S(j["kiemKeNgay"]);
        return o;
    }

    private Dictionary<string, JsonArray?> StateSheets() => new()
    {
        [SQd] = TextChunks(JsonSerializer.SerializeToNode(State.Decisions, J)), [SDc] = Chunks(State.Adjustments), [SLs] = Chunks(State.History), [SEd] = Chunks(State.Edits),
    };
    private Dictionary<string, JsonArray?> SourceSheets()
    {
        var sources = new JsonObject(); foreach (var (k, s) in Files) sources[k] = MetaOf(s);
        var meta = new JsonObject { ["v"] = 2, ["period"] = Period is null ? null : new JsonObject { ["from"] = Period.From, ["to"] = Period.To }, ["options"] = OptionsJson(), ["sources"] = sources };
        var kt = Files.GetValueOrDefault("ketoan");
        var sh = new Dictionary<string, JsonArray?>
        {
            [SPhien] = TextChunks(meta), [SCt] = Chunks(kt?.Ct?.Vouchers ?? new()), [SMaKt] = Chunks(kt?.KnownCodes ?? new()), [SDm] = Chunks(kt?.DanhMuc ?? new()),
            [SPhien + "_ct"] = TextChunks(kt?.Ct is { } ct ? new JsonObject { ["ok"] = true, ["kind"] = "ct", ["sheet"] = ct.Sheet, ["stats"] = new JsonObject { ["nRows"] = ct.Stats.NRows, ["from"] = ct.Stats.From, ["to"] = ct.Stats.To } } : null),
        };
        foreach (var k in SrcKinds) sh[SrcSheet(k)] = Files.TryGetValue(k, out var s) ? Chunks(s.Rows) : new JsonArray();
        return sh;
    }
    private string Stamp() => string.Join("|", Files.OrderBy(k => k.Key, StringComparer.Ordinal).Select(kv => kv.Key + ":" + kv.Value.FileName + ":" + kv.Value.LoadedAt + ":" + kv.Value.Rows.Count)) + "|" + (Files.GetValueOrDefault("ketoan")?.Ct?.Vouchers.Count ?? 0);

    private JsonArray RevSheet(bool deleted = false)
    {
        var cur = Untext(Read(SRev)) as JsonObject; var rev = Math.Max(Rev, (int)C3dI(cur?["rev"])) + 1; Rev = rev;
        SavedBy = _session.Username; SavedAt = CkHistory.Now();
        return TextChunks(new JsonObject { ["rev"] = rev, ["by"] = SavedBy, ["client"] = Environment.MachineName, ["at"] = SavedAt, ["stamp"] = deleted ? "" : Stamp(), ["deleted"] = deleted });
    }

    private void Write(Dictionary<string, JsonArray?> sheets) => _src().Write(Db, sheets);

    /// <summary>Ghi thao tác người dùng; máy khác vừa ghi → đọc bản của họ, GỘP rồi ghi lại.</summary>
    public bool SaveState()
    {
        var merged = false;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try { var sh = StateSheets(); sh[SRev] = RevSheet(); Write(sh); return merged; }
            catch (SheetConflictException) { CkHistory.Merge(State, ReadState()); merged = true; }
        }
        throw new BusinessException("Nhiều máy cùng ghi, chưa lưu được — thử lại sau ít giây.");
    }

    public void SaveAll()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try { var sh = SourceSheets(); foreach (var kv in StateSheets()) sh[kv.Key] = kv.Value; sh[SRev] = RevSheet(); Write(sh); return; }
            catch (SheetConflictException) { CkHistory.Merge(State, ReadState()); }
        }
        throw new BusinessException("Nhiều máy cùng ghi, chưa lưu được — thử lại sau ít giây.");
    }

    private CkState ReadState() => new()
    {
        Decisions = (Untext(Read(SQd)) as JsonObject)?.Deserialize<Dictionary<string, CkDecision>>(J) ?? new(),
        Adjustments = Unchunk<CkAdjustment>(Read(SDc)), History = Unchunk<CkEntry>(Read(SLs)), Edits = Unchunk<CkEdit2>(Read(SEd)),
    };

    /// <summary>Mở lại phiên đã lưu trên database (mọi máy thấy chung).</summary>
    public bool Load()
    {
        Files.Clear(); State = new(); Result = null;
        var meta = Untext(Read(SPhien)) as JsonObject; var rv = Untext(Read(SRev)) as JsonObject;
        Rev = (int)C3dI(rv?["rev"]); SavedBy = AnAnStore.S(rv?["by"]); SavedAt = AnAnStore.S(rv?["at"]);
        if (meta is null) return false;
        Period = PeriodOf(meta["period"]); Options = OptionsOf(meta["options"]);
        if (meta["sources"] is JsonObject srcs)
            foreach (var (k, v) in srcs) if (v is JsonObject o && SrcKinds.Contains(k)) Files[k] = FromMeta(k, o, Unchunk<CkRow>(Read(SrcSheet(k))));
        if (Files.TryGetValue("ketoan", out var kt))
        {
            kt.KnownCodes = Unchunk<string>(Read(SMaKt));
            var ct = Unchunk<CkVoucher>(Read(SCt));
            if (ct.Count > 0) { var ci = Untext(Read(SPhien + "_ct")) as JsonObject; kt.Ct = new CkSource { Ok = true, Kind = "ct", Vouchers = ct, Sheet = AnAnStore.S(ci?["sheet"]) }; kt.Ct.Stats.NRows = ct.Count; var ds = ct.Select(v => v.Ngay).Where(x => x.Length > 0).OrderBy(x => x, StringComparer.Ordinal).ToList(); kt.Ct.Stats.From = ds.FirstOrDefault() ?? ""; kt.Ct.Stats.To = ds.LastOrDefault() ?? ""; }
            var dm = Unchunk<string>(Read(SDm)); if (dm.Count > 0) kt.DanhMuc = dm;
        }
        State = ReadState();
        return Files.Count > 0;
    }

    /// <summary>Số phiên bản hiện tại trên database (máy khác vừa ghi → khác Rev đang giữ).</summary>
    public (int Rev, string By, string At, bool Deleted, string Stamp) Peek()
    {
        var rv = Untext(Read(SRev)) as JsonObject;
        return ((int)C3dI(rv?["rev"]), AnAnStore.S(rv?["by"]), AnAnStore.S(rv?["at"]), AnAnStore.S(rv?["deleted"]) == "true", AnAnStore.S(rv?["stamp"]));
    }
    public string CurrentStamp => Stamp();
    public void ReloadState() => State = ReadState();

    /// <summary>Xóa dữ liệu kỳ khỏi database (mọi máy sẽ thấy).</summary>
    public void Clear()
    {
        if (!CanEdit) throw new BusinessException("Bạn chỉ có quyền xem.");
        var sh = new Dictionary<string, JsonArray?>();
        foreach (var n in new[] { SPhien, SCt, SMaKt, SDm, SPhien + "_ct", SQd, SDc, SLs, SEd }.Concat(SrcKinds.Select(SrcSheet))) sh[n] = new JsonArray();
        sh[SRev] = RevSheet(true);
        Write(sh);
        Files.Clear(); State = new(); Result = null; Period = null;
    }

    // ------------------------------------------------------------ đọc Excel (số giữ nguyên là số, ngày → số ngày Excel — giống SheetJS raw)
    public static (Dictionary<string, IReadOnlyList<IReadOnlyList<object?>>> Book, List<string> Names) ReadExcel(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant(); var book = new Dictionary<string, IReadOnlyList<IReadOnlyList<object?>>>(); var names = new List<string>();
        if (ext is ".xls" or ".csv")
        {
            var b = SheetText.Read(path);
            foreach (var n in b.SheetNames) { names.Add(n); book[n] = b.Sheets[n].Select(r => (IReadOnlyList<object?>)r.Select(c => (object?)c).ToList()).ToList(); }
            return (book, names);
        }
        using var wb = new XLWorkbook(path);
        foreach (var ws in wb.Worksheets)
        {
            names.Add(ws.Name); var rows = new List<IReadOnlyList<object?>>();
            var used = ws.RangeUsed(XLCellsUsedOptions.Contents);
            if (used is not null)
            {
                int r2 = used.LastRow().RowNumber(), c2 = used.LastColumn().ColumnNumber();
                for (var r = 1; r <= r2; r++)
                {
                    var arr = new object?[c2];
                    for (var c = 1; c <= c2; c++)
                    {
                        var cell = ws.Cell(r, c); XLCellValue v;
                        try { v = cell.HasFormula ? cell.CachedValue : cell.Value; } catch { arr[c - 1] = cell.GetFormattedString(); continue; }
                        arr[c - 1] = v.Type switch
                        {
                            XLDataType.Number => v.GetNumber(), XLDataType.DateTime => v.GetDateTime().ToOADate(), XLDataType.Boolean => v.GetBoolean() ? "TRUE" : "FALSE",
                            XLDataType.Text => v.GetText(), XLDataType.TimeSpan => v.GetTimeSpan().TotalDays, XLDataType.Blank => "", _ => cell.GetFormattedString(),
                        };
                    }
                    rows.Add(arr);
                }
            }
            book[ws.Name] = rows;
        }
        return (book, names);
    }

    // ------------------------------------------------------------ ghi nhớ ánh xạ cột (theo tên tiêu đề)
    private Dictionary<string, string>? Prior(string kind)
    {
        try { var o = JsonNode.Parse(_settings?.Get("ck_mapping", "{}") ?? "{}") as JsonObject; return o?[kind]?["mapping"]?.Deserialize<Dictionary<string, string>>(J); } catch { return null; }
    }
    private void Remember(string kind, CkSource s)
    {
        if (_settings is null) return;
        try { var o = JsonNode.Parse(_settings.Get("ck_mapping", "{}")) as JsonObject ?? new(); o[kind] = new JsonObject { ["mapping"] = JsonSerializer.SerializeToNode(s.MappingLabels, J), ["sheet"] = s.Sheet, ["at"] = CkHistory.Now() }; _settings.Set("ck_mapping", o.ToJsonString(J)); } catch { }
    }

    /// <summary>Nạp 1 file nguồn. Trả kết quả (có thể NeedMap → giao diện cho ánh xạ cột tay rồi gọi lại với manual).</summary>
    public CkSource LoadFile(string kind, string path, Dictionary<string, int>? manual = null, string? sheet = null)
    {
        if (!CanEdit) throw new BusinessException("Bạn chỉ có quyền xem.");
        var (book, names) = ReadExcel(path);
        var res = CkImport.ReadWorkbook(book, names, kind, Path.GetFileName(path), new CkImport.ReadOpts { Manual = manual, Prior = Prior(kind) }, sheet);
        if (!res.Ok) return res;
        res.LoadedAt = CkHistory.Now();
        Files[kind] = res; Remember(kind, res);
        if (Period is null && kind is "anan" or "ketoan" && res.Period is { From.Length: > 0, To.Length: > 0 } p) Period = new CkPeriod(p.From, p.To);
        SaveAll();
        return res;
    }

    public void RemoveFile(string kind) { if (!CanEdit) throw new BusinessException("Bạn chỉ có quyền xem."); Files.Remove(kind); Result = null; SaveAll(); }

    public void SetPeriod(string from, string to) { Period = new CkPeriod(from, to); if (CanEdit) SaveAll(); }

    // ------------------------------------------------------------ đối chiếu
    private CkSource? Edited(string kind)
    {
        if (!Files.TryGetValue(kind, out var s)) return null;
        var (rows, _, n) = CkHistory.ApplyEdits(kind, s.Rows, State.Edits);
        if (n == 0) return s;
        var c = new CkSource { Ok = true, Kind = kind, Rows = MergeSameMa(rows), KnownCodes = s.KnownCodes, Period = s.Period, Stats = s.Stats, Mapping = s.Mapping, Ct = s.Ct, DanhMuc = s.DanhMuc, FileName = s.FileName };
        return c;
    }
    private static List<CkRow> MergeSameMa(List<CkRow> rows)
    {
        var by = new Dictionary<string, CkRow>(); var outp = new List<CkRow>();
        foreach (var r in rows)
        {
            if (!by.TryGetValue(r.Ma, out var p)) { var c = r.Clone(); by[r.Ma] = c; outp.Add(c); continue; }
            p.Kg = R6(p.Kg + r.Kg); if (r.Sl is { } s) p.Sl = R6((p.Sl ?? 0) + s);
            if (r.DauKg is { } d) p.DauKg = R6((p.DauKg ?? 0) + d); if (r.NhapKg is { } n) p.NhapKg = R6((p.NhapKg ?? 0) + n); if (r.XuatKg is { } x) p.XuatKg = R6((p.XuatKg ?? 0) + x);
            p.Dup = (p.Dup ?? 1) + 1;
        }
        return outp;
    }

    public CkResult Run()
    {
        if (!Ready) throw new BusinessException("Cần nạp đủ Tồn kho An An và Kế toán.");
        var an = Edited("anan"); var kt = Edited("ketoan"); var gui = Files.ContainsKey("gui") ? Edited("gui") : null;
        foreach (var (k, s) in Files) s.PeriodCheck = CkImport.CheckPeriod(s.Period ?? s.PeriodGuess, Period, k);
        if (an is not null) an.PeriodCheck = Files["anan"].PeriodCheck; if (kt is not null) kt.PeriodCheck = Files["ketoan"].PeriodCheck; if (gui is not null) gui.PeriodCheck = Files["gui"].PeriodCheck;
        var inp = new CkInput { AnAn = an, KeToan = kt, Gui = gui, Ct = Files["ketoan"].Ct?.Vouchers ?? new(), DanhMuc = Files["ketoan"].DanhMuc, Period = Period, Options = Options };
        Result = CkEngine.Reconcile(inp);
        Result.Overlay(State);
        return Result;
    }

    // ------------------------------------------------------------ thao tác người dùng (ghi ngay vào database dùng chung)
    private CkMeta Meta(string reason = "") => new(_session.Username, "", reason);
    private void Apply((bool Ok, string Error, CkEntry? Entry) r)
    {
        if (!r.Ok) throw new BusinessException(r.Error);
        if (SaveState() && Result is not null) Result.Overlay(State); else Result?.Overlay(State);
    }
    private void Need() { if (!CanEdit) throw new BusinessException("Bạn chỉ có quyền xem."); }

    public void Transfer(string over, string under, double qty, string reason, string side) { Need(); Apply(CkHistory.Transfer(State, over, under, qty, Meta(reason), side)); }
    public void Confirm(string ma, string cause, string causeNote, double adjQty, string reason, string? note) { Need(); var it = Result?.ByMa.GetValueOrDefault(ma); Apply(CkHistory.Confirm(State, ma, cause, causeNote, it?.Diff, adjQty, note, Meta(reason))); }
    public void SetStatus(string ma, string status) { Need(); Apply(CkHistory.SetStatus(State, ma, status, Meta())); }
    public void SetNote(string ma, string note) { Need(); Apply(CkHistory.SetNote(State, ma, note, Meta())); }
    public void Link(string ma, string other) { Need(); Apply(CkHistory.Link(State, ma, other, Meta())); }
    public void Unlink(string ma, string other) { Need(); Apply(CkHistory.Unlink(State, ma, other, Meta())); }
    public void FlagVoucher(string ma, string id, bool on) { Need(); Apply(CkHistory.FlagVoucher(State, ma, id, on, Meta())); }
    public void CancelAdjustment(string id) { Need(); Apply(CkHistory.CancelAdjustment(State, id, Meta())); }
    public void Undo(string entryId) { Need(); Apply(CkHistory.Undo(State, entryId, Meta())); }
    public void Adjust(string ma, double qty, string side, string reason)
    {
        Need(); var p = new CkHistory.Patch(); p.Adjust.Add((ma, qty, "", side));
        Apply(CkHistory.Commit(State, ma, p, Meta(reason) with { Action = "dieu-chinh", Summary = $"Điều chỉnh {(qty > 0 ? "+" : "")}{Vn(qty)} kg ({(side == "kt" ? "kế toán" : "kho")})" }));
    }

    /// <summary>Nạp file điều chỉnh (Mã hàng · Điều chỉnh (kg) · Lý do · Sửa ở): mỗi dòng 1 điều chỉnh.</summary>
    public (int N, List<(int Row, string Msg)> Errors) ImportAdjustments(string path)
    {
        Need();
        var (book, names) = ReadExcel(path); var r = CkImport.ReadAdjustments(book[names[0]]);
        if (!r.Ok) throw new BusinessException(r.Error);
        var n = 0;
        foreach (var a in r.Rows)
        {
            var p = new CkHistory.Patch(); p.Adjust.Add((a.Ma, a.Qty, "", a.Side));
            var res = CkHistory.Commit(State, a.Ma, p, Meta(a.Reason.Length > 0 ? a.Reason : "Nạp file điều chỉnh " + Path.GetFileName(path)) with { Action = "dieu-chinh", Summary = $"Điều chỉnh từ file {(a.Qty > 0 ? "+" : "")}{Vn(a.Qty)} kg" });
            if (res.Ok) n++;
        }
        if (n > 0) { SaveState(); Result?.Overlay(State); }
        return (n, r.Errors);
    }

    // ------------------------------------------------------------ XUẤT BÁO CÁO EXCEL (10 sheet, giống dc2-export.js)
    public XlsxOut BuildExcel()
    {
        var R = Result ?? throw new BusinessException("Chưa đối chiếu.");
        var items = R.Items; var K = R.Kpi; var p = R.Period; var lech = items.Where(i => i.Status == "diff").ToList(); var khop = items.Where(i => i.Status == "match").ToList();
        var at = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture); var user = _session.Username;
        object? V(double? n) => n is null ? "" : n.Value;
        double Sum(IEnumerable<CkItem> a, Func<CkItem, double?> f) => R6(a.Sum(x => f(x) ?? 0));
        string Basis(string b) => b switch { "xac-dinh" => "Xác định", "nghi-ngo" => "Nghi ngờ", "chua" => "Chưa xác định", _ => "" };
        string ConfL(string c) => CkEngine.ConfLabel.GetValueOrDefault(c, "");
        string Kind(CkItem i) => i.Diff < 0 ? "Thiếu (kho < kế toán)" : "Thừa (kho > kế toán)";
        string DecL(CkItem i) => i.Decision is { Status.Length: > 0 } d ? CkConfig.StatusLabel(d.Status) : CkConfig.StatusLabel(i.StatusUi);
        var x = new XlsxOut();
        var head = new XlsxOut.St(Bold: true, Fill: "D9E1F2", Font: "Calibri", Size: 11, Wrap: true);
        var body = new XlsxOut.St(Font: "Calibri", Size: 11);
        var tot = new XlsxOut.St(Bold: true, Fill: "FFF2CC", Font: "Calibri", Size: 11);
        var title = new XlsxOut.St(Bold: true, Font: "Calibri", Size: 13, Border: false);
        var sub = new XlsxOut.St(Font: "Calibri", Size: 10, Color: "555555", Border: false);
        void Sheet(string name, string ttl, (string h, double w)[] cols, List<object?[]> rows, object?[]? total = null)
        {
            var aoa = new List<object?[]> { new object?[] { ttl }, new object?[] { $"Kỳ đối chiếu: {Dmy(p?.From)} – {Dmy(p?.To)}   ·   Xuất lúc: {at}" + (user.Length > 0 ? "   ·   Người xuất: " + user : "") }, new object?[] { "Đơn vị: kg   ·   Chênh lệch = Tồn kho (An An + Kho gửi) − Kế toán" }, cols.Select(c => (object?)c.h).ToArray() };
            aoa.AddRange(rows); if (total is not null) aoa.Add(total);
            var sh = new XlsxOut.Sheet { Name = name, Aoa = aoa, Widths = cols.Select(c => c.w).ToArray(), FreezeRows = 4 };
            sh.Set(0, 0, title); sh.Set(1, 0, sub); sh.Set(2, 0, sub);
            for (var c = 0; c < cols.Length; c++) sh.Set(3, c, head);
            for (var r = 4; r < aoa.Count; r++) for (var c = 0; c < cols.Length; c++) sh.Set(r, c, total is not null && r == aoa.Count - 1 ? tot : body);
            sh.AutoFilter = (3, 0, cols.Length - 1);
            x.Sheets.Add(sh);
        }
        // 1. Tổng quan
        var ov = new List<object?[]>
        {
            new object?[] { "Kỳ đối chiếu", $"{Dmy(p?.From)} – {Dmy(p?.To)}", "" }, new object?[] { "Thời gian xuất báo cáo", at, user },
            new object?[] { "Tổng số mã đối chiếu", K.Tong, "" }, new object?[] { "Số mã khớp", K.Khop, "" }, new object?[] { "Số mã đang lệch", K.Lech, $"Thiếu {K.Thieu} · Thừa {K.Thua}" },
            new object?[] { "Số mã chưa xác định nguyên nhân gốc", K.ChuaXacDinh, "" }, new object?[] { "Số mã đã xử lý", K.DaXuLy, $"Tiến độ {K.TienDo}%" },
            new object?[] { "Tồn kho An An (kg)", K.TongAA, "" }, new object?[] { "Tồn kho gửi (kg)", K.TongGui, "" }, new object?[] { "Tổng tồn kho (kg)", K.TongKho, "An An + Kho gửi" }, new object?[] { "Tồn kế toán (kg)", K.TongKt, "" },
            new object?[] { "Chênh lệch ròng (kg)", K.LechRong, "Kho − Kế toán" }, new object?[] { "Chênh lệch tuyệt đối (kg)", K.LechTuyetDoi, "Tổng |lệch| của các mã lệch" },
            new object?[] { "Chênh lệch ròng sau điều chỉnh (kg)", K.LechRongSau, "" }, new object?[] { "Chênh lệch tuyệt đối sau điều chỉnh (kg)", K.LechTuyetDoiSau, "" },
        };
        if (K.Bubu) ov.Add(new object?[] { "Lưu ý", "Lệch bù trừ", "Tổng ròng ≈ 0 nhưng từng mã vẫn lệch" });
        foreach (var (k, f) in Files) ov.Add(new object?[] { "Nguồn: " + CkConfig.SourceTitle.GetValueOrDefault(k, k), f.FileName, $"Sheet {f.Sheet} · {f.Rows.Count} dòng · kỳ file " + (f.Period is { } fp ? $"{Dmy(fp.From)} – {Dmy(fp.To)}" : "không rõ") + (f.PeriodCheck is { } pc ? " · " + pc.Text : "") });
        foreach (var v in R.Validation) ov.Add(new object?[] { $"Kiểm tra dữ liệu ({v.Level switch { "ok" => "đạt", "warn" => "cảnh báo", "err" => "lỗi", _ => "ghi chú" }})", v.Text, "" });
        Sheet("Tổng quan", "BÁO CÁO ĐỐI CHIẾU TỒN KHO CUỐI KỲ — TỔNG QUAN", new[] { ("Chỉ tiêu", 44.0), ("Giá trị", 40.0), ("Ghi chú", 70.0) }, ov);
        // 2–3. Khớp / chênh lệch
        object?[] Money(CkItem i) => new object?[] { i.Ma, i.Ten, V(i.Aa), V(i.Gui), i.Kho, V(i.Kt), V(i.Kk), i.Diff };
        var baseCols = new[] { ("Mã hàng", 24.0), ("Tên hàng", 46.0), ("Tồn An An", 14.0), ("Tồn kho gửi", 14.0), ("Tổng tồn kho", 15.0), ("Tồn kế toán", 14.0), ("Kiểm kê", 12.0), ("Chênh lệch", 14.0) };
        object?[] Totals(List<CkItem> a) => new object?[] { $"TỔNG CỘNG ({a.Count} mã)", "", Sum(a, i => i.Aa), Sum(a, i => i.Gui), Sum(a, i => i.Kho), Sum(a, i => i.Kt), Sum(a, i => i.Kk), Sum(a, i => i.Diff) };
        Sheet("Mã hàng khớp", "DANH SÁCH MÃ HÀNG KHỚP", baseCols, khop.Select(Money).ToList(), Totals(khop));
        Sheet("Mã hàng chênh lệch", "DANH SÁCH MÃ HÀNG CHÊNH LỆCH", baseCols.Concat(new[] { ("Loại lệch", 22.0), ("Nguyên nhân chính", 44.0), ("Cơ sở", 14.0), ("Tin cậy", 12.0), ("Trạng thái", 22.0), ("Đã điều chỉnh", 14.0), ("Lệch sau điều chỉnh", 16.0), ("Ghi chú", 36.0) }).ToArray(),
            lech.Select(i => Money(i).Concat(new object?[] { Kind(i), i.Cause?.Label ?? "", i.Cause is { } c ? Basis(c.Basis) : "", i.Cause is { } c2 ? ConfL(c2.Conf) : "", DecL(i), i.Adj, i.DiffAfter, i.Decision?.Note ?? "" }).ToArray()).ToList(),
            Totals(lech).Concat(new object?[] { "", "", "", "", "", Sum(lech, i => i.Adj), Sum(lech, i => i.DiffAfter), "" }).ToArray());
        // 4. Mã nghi sai
        var sug = new List<object?[]>();
        foreach (var i in lech)
        {
            foreach (var c in i.Cands) sug.Add(new object?[] { i.Ma, i.Ten, i.Diff, c.Ma, c.Ten, c.Diff, c.Score, ConfL(c.Conf), c.Kind switch { "quy-cach" => "Sai quy cách / size / trọng lượng", "dac-tinh" => "Khác đặc tính", "san-pham" => "Khác sản phẩm", _ => "Khác mã" }, c.Exact ? "Có" : "Không", Pct(c.CodeSim), c.NameSim is { } ns ? Pct(ns) : "", string.Join("; ", c.DiffSegs.Select(d => $"{d.Label} {d.A}↔{d.B}")), string.Join(" | ", c.Reasons) });
            foreach (var g in i.Combos) sug.Add(new object?[] { i.Ma, i.Ten, i.Diff, string.Join(" + ", g.Codes), string.Join(" + ", g.Names), g.Sum, "", ConfL(g.Conf), "Một mã chia thành nhiều mã", "Có", Pct(g.AvgSim), "", "", string.Join(" | ", g.Reasons) });
        }
        Sheet("Mã hàng nghi sai", "MÃ HÀNG NGHI NGỜ SAI (chỉ gợi ý — không tự gộp / sửa mã)", new[] { ("Mã đang nghi ngờ", 24.0), ("Tên hàng", 38.0), ("Chênh lệch (kg)", 14.0), ("Mã hàng có khả năng đúng", 28.0), ("Tên hàng gợi ý", 38.0), ("Chênh lệch mã gợi ý (kg)", 16.0), ("Điểm (0–100)", 11.0), ("Độ tin cậy", 12.0), ("Kiểu sai", 28.0), ("Đối ứng đúng số lượng", 12.0), ("Tương đồng mã", 12.0), ("Tương đồng tên", 12.0), ("Khác ở đoạn mã", 40.0), ("Lý do", 90.0) }, sug);
        // 5. Chứng từ nghi ngờ
        var ctr = new List<object?[]>();
        foreach (var i in lech) foreach (var l in i.Ct.Lines.Where(l => l.Flags.Count > 0))
            ctr.Add(new object?[] { i.Ma, l.V.Src == "ketoan" ? "Kế toán" : "Kho An An", l.V.Phieu, Dmy(l.V.Ngay), l.V.Loai, l.V.Dir == "N" ? "Nhập" : "Xuất", l.V.Ten.Length > 0 ? l.V.Ten : i.Ten, l.V.Kg, l.V.Kho, l.V.Lsx, string.Join(" | ", l.Flags.Select(f => CkTrace.FlagLabel.GetValueOrDefault(f.Code, f.Code) + ": " + f.Text)), i.Decision?.Flagged.Contains(l.V.Id) == true ? "Có" : "", l.V.Row });
        Sheet("Chứng từ nghi ngờ", "CHỨNG TỪ NGHI NGỜ (dấu hiệu từ dữ liệu — chưa khẳng định là nguyên nhân)", new[] { ("Mã hàng", 24.0), ("Nguồn", 12.0), ("Số chứng từ", 16.0), ("Ngày", 12.0), ("Loại nghiệp vụ", 14.0), ("Chiều", 8.0), ("Tên hàng", 40.0), ("Số lượng (kg)", 14.0), ("Kho", 10.0), ("LSX", 14.0), ("Dấu hiệu", 90.0), ("Đã đánh dấu", 12.0), ("Dòng trong file", 10.0) }, ctr);
        // 6. Phân tích nguyên nhân
        var an = new List<object?[]>();
        foreach (var i in lech) for (var k = 0; k < i.Causes.Count; k++) { var c = i.Causes[k]; an.Add(new object?[] { i.Ma, i.Ten, i.Diff, k + 1, c.Label, Basis(c.Basis), ConfL(c.Conf), string.Join(" | ", c.Evidence), c.Action, i.Decision?.Cause == c.Code ? "Đã xác nhận" : "" }); }
        Sheet("Phân tích nguyên nhân", "PHÂN TÍCH NGUYÊN NHÂN (nguyên nhân #1 = nguyên nhân chính)", new[] { ("Mã hàng", 24.0), ("Tên hàng", 38.0), ("Chênh lệch (kg)", 14.0), ("#", 5.0), ("Nguyên nhân", 46.0), ("Cơ sở", 14.0), ("Tin cậy", 12.0), ("Bằng chứng", 110.0), ("Hướng xử lý đề xuất", 70.0), ("Xác nhận", 14.0) }, an);
        // 7. Gợi ý mã (cặp)
        var pairs = new List<object?[]>(); var seen = new HashSet<string>();
        foreach (var i in lech)
        {
            if (i.Cands.Count == 0) continue; var c = i.Cands[0]; var key = string.Join("|", new[] { i.Ma, c.Ma }.OrderBy(z => z, StringComparer.Ordinal)); if (!seen.Add(key)) continue;
            pairs.Add(new object?[] { i.Ma, c.Ma, Math.Min(Math.Abs(i.Diff), Math.Abs(c.Diff)), c.Exact ? "Đối ứng đúng số" : "Lệch ngược, số khác nhau", ConfL(c.Conf), string.Join("; ", c.DiffSegs.Select(d => $"{d.Label} {d.A}↔{d.B}")), State.Decisions.GetValueOrDefault(i.Ma)?.Linked.Contains(c.Ma) == true ? "Đã gắn" : "" });
        }
        Sheet("Gợi ý mã hàng", "GỢI Ý MÃ HÀNG — CẶP MÃ CÓ KHẢ NĂNG NHẦM CHO NHAU", new[] { ("Mã A", 24.0), ("Mã B", 24.0), ("Số lượng có thể nhầm (kg)", 16.0), ("Quan hệ số lượng", 24.0), ("Độ tin cậy", 12.0), ("Khác ở đoạn mã", 60.0), ("Người dùng", 12.0) }, pairs);
        // 8. Lịch sử xử lý
        string When(string iso) => DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) : iso;
        Sheet("Lịch sử xử lý", "LỊCH SỬ XỬ LÝ", new[] { ("Thời gian", 19.0), ("Người thao tác", 18.0), ("Mã hàng", 24.0), ("Nội dung thay đổi", 60.0), ("Lý do xử lý", 40.0), ("Ghi chú", 36.0), ("Trạng thái", 14.0) },
            State.History.Select(e => new object?[] { When(e.At), e.By, e.Ma, e.Action == "hoan-tac" ? "Hoàn tác" : e.Summary, e.Reason, e.Note, e.Undone ? "Đã hoàn tác" : "" }).ToList());
        // 9. Số liệu sau đối chiếu
        string FixTxt(CkItem i) => i.AdjKho != 0 && i.AdjKt != 0 ? "Cả kho và kế toán" : i.AdjKt != 0 ? "Kế toán sửa theo mã kho" : i.AdjKho != 0 ? "Kho sửa theo mã kế toán" : "";
        Sheet("Số liệu sau đối chiếu", "SỐ LIỆU SAU ĐỐI CHIẾU (đã tính điều chỉnh của người dùng — số liệu nguồn không bị sửa)",
            new[] { ("Mã hàng", 24.0), ("Tên hàng", 46.0), ("Tồn kho gốc", 14.0), ("Điều chỉnh kho", 13.0), ("Tồn kho sau", 14.0), ("Tồn kế toán gốc", 14.0), ("Điều chỉnh kế toán", 14.0), ("Tồn kế toán sau", 14.0), ("Chênh lệch gốc", 14.0), ("Chênh lệch sau", 14.0), ("Sửa ở", 26.0), ("Trạng thái", 30.0) },
            items.Select(i => new object?[] { i.Ma, i.Ten, i.Kho, i.AdjKho, i.KhoAfter, V(i.Kt), i.AdjKt, i.KtAfter, i.Diff, i.DiffAfter, FixTxt(i), i.Status == "match" ? "Khớp" : (DecL(i) is { Length: > 0 } dl ? dl : "Đang lệch") + (i.StatusAfter == "match" ? " → khớp sau điều chỉnh" : "") }).ToList(),
            new object?[] { $"TỔNG CỘNG ({items.Count} mã)", "", Sum(items, i => i.Kho), Sum(items, i => i.AdjKho), Sum(items, i => i.KhoAfter), Sum(items, i => i.Kt), Sum(items, i => i.AdjKt), Sum(items, i => i.KtAfter), Sum(items, i => i.Diff), Sum(items, i => i.DiffAfter), "", "" });
        // 10. Dữ liệu điều chỉnh
        Sheet("Dữ liệu điều chỉnh", "DỮ LIỆU ĐIỀU CHỈNH (kg dương = cộng vào số liệu của bên được sửa, âm = trừ đi)",
            new[] { ("Thời gian", 19.0), ("Người", 18.0), ("Mã hàng", 24.0), ("Tên hàng", 40.0), ("Điều chỉnh (kg)", 15.0), ("Sửa ở", 26.0), ("Lý do", 40.0), ("Loại", 30.0), ("Trạng thái", 12.0) },
            State.Adjustments.Select(a => new object?[] { When(a.At), a.By, a.Ma, R.ByMa.GetValueOrDefault(a.Ma)?.Ten ?? "", a.Qty, a.Side == "kt" ? "Kế toán (sửa theo mã kho)" : "Kho (sửa theo mã kế toán)", a.Reason, a.PairId.Length > 0 ? $"Sửa mã / chuyển nhầm (cặp {a.PairId[^Math.Min(5, a.PairId.Length)..]})" : "Điều chỉnh số lượng", a.Cancelled ? "Đã hủy" : "Hiệu lực" }).ToList());
        return x;
    }
}

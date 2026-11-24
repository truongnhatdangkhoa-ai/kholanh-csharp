using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static KhoLanh.Core.CuoiKy.Ck;

namespace KhoLanh.Core.CuoiKy;

/// <summary>Quyết định của người dùng cho 1 mã (định dạng JSON giống app cũ).</summary>
public sealed class CkDecision
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("cause")] public string Cause { get; set; } = "";
    [JsonPropertyName("causeNote")] public string CauseNote { get; set; } = "";
    [JsonPropertyName("linked")] public List<string> Linked { get; set; } = new();
    [JsonPropertyName("flagged")] public List<string> Flagged { get; set; } = new();
    [JsonPropertyName("note")] public string Note { get; set; } = "";
    [JsonPropertyName("confirmedAt")] public string ConfirmedAt { get; set; } = "";
    [JsonPropertyName("diffAtDecision")] public double? DiffAtDecision { get; set; }
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = "";
    [JsonPropertyName("updatedBy")] public string UpdatedBy { get; set; } = "";
    public CkDecision Clone() { var d = (CkDecision)MemberwiseClone(); d.Linked = Linked.ToList(); d.Flagged = Flagged.ToList(); return d; }
}

public sealed class CkAdjustment
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("ma")] public string Ma { get; set; } = "";
    [JsonPropertyName("qty")] public double Qty { get; set; }
    [JsonPropertyName("side")] public string Side { get; set; } = "kho";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";
    [JsonPropertyName("by")] public string By { get; set; } = "";
    [JsonPropertyName("pairId")] public string PairId { get; set; } = "";
    [JsonPropertyName("cancelled")] public bool Cancelled { get; set; }
    [JsonPropertyName("cancelledAt")] public string CancelledAt { get; set; } = "";
    [JsonPropertyName("undoneBy")] public string? UndoneBy { get; set; }
    [JsonPropertyName("entryId")] public string EntryId { get; set; } = "";
}

public sealed class CkEdit2
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("field")] public string Field { get; set; } = "ma";
    [JsonPropertyName("before")] public JsonNode? Before { get; set; }
    [JsonPropertyName("after")] public JsonNode? After { get; set; }
    [JsonPropertyName("at")] public string At { get; set; } = "";
    [JsonPropertyName("by")] public string By { get; set; } = "";
    [JsonPropertyName("cancelled")] public bool Cancelled { get; set; }
    [JsonPropertyName("cancelledAt")] public string CancelledAt { get; set; } = "";
}

public sealed class CkOp
{
    [JsonPropertyName("t")] public string T { get; set; } = "";
    [JsonPropertyName("ma")] public string? Ma { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("before")] public CkDecision? Before { get; set; }
    [JsonPropertyName("after")] public CkDecision? After { get; set; }
}

public sealed class CkEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";
    [JsonPropertyName("by")] public string By { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("ma")] public string Ma { get; set; } = "";
    [JsonPropertyName("mas")] public List<string> Mas { get; set; } = new();
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
    [JsonPropertyName("values")] public JsonObject Values { get; set; } = new();
    [JsonPropertyName("ops")] public List<CkOp> Ops { get; set; } = new();
    [JsonPropertyName("undone")] public bool Undone { get; set; }
    [JsonPropertyName("undoneAt")] public string? UndoneAt { get; set; }
    [JsonPropertyName("undoOf")] public string? UndoOf { get; set; }
}

/// <summary>Vùng dữ liệu riêng của người dùng: quyết định + điều chỉnh + lịch sử + sửa dữ liệu (số liệu nguồn không bao giờ bị sửa).</summary>
public sealed class CkState
{
    public Dictionary<string, CkDecision> Decisions = new();
    public List<CkAdjustment> Adjustments = new();
    public List<CkEntry> History = new();
    public List<CkEdit2> Edits = new();
}

public sealed record CkMeta(string By, string At = "", string Reason = "", string Summary = "", string Action = "");

/// <summary>XỬ LÝ, LỊCH SỬ, HOÀN TÁC, GỘP 2 MÁY (giống dc2-history.js).</summary>
public static class CkHistory
{
    public static readonly Dictionary<string, string> SideText = new() { ["kho"] = "Kho sửa theo mã kế toán", ["kt"] = "Kế toán sửa theo mã kho" };
    public static Func<string> Now = () => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
    private static string Uid(string p) => p + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x") + Guid.NewGuid().ToString("N")[..5];

    public sealed class Patch
    {
        public string? Status, Cause, CauseNote, Note;
        public double? DiffAtDecision;
        public List<string> LinkAdd = new(), LinkRemove = new(), FlagAdd = new(), FlagRemove = new(), CancelAdj = new();
        public List<(string? Ma, double Qty, string PairId, string Side)> Adjust = new();
    }

    public static double AdjustmentEffect(CkState st, string ma) => R6(st.Adjustments.Where(a => a.Ma == ma && !a.Cancelled).Sum(a => a.Side == "kt" ? -a.Qty : a.Qty));

    public static (bool Ok, string Error, CkEntry? Entry) Commit(CkState state, string ma, Patch p, CkMeta m)
    {
        var at = m.At.Length > 0 ? m.At : Now();
        if (ma.Length == 0) return (false, "Thiếu mã hàng", null);
        if (p.Adjust.Count > 0 && m.Reason.Trim().Length == 0) return (false, "Cần nhập lý do khi điều chỉnh số lượng", null);
        if (p.Status == "xong" && m.Reason.Trim().Length == 0) return (false, "Cần chọn nguyên nhân hoặc nhập lý do khi xác nhận đã xử lý", null);
        var ops = new List<CkOp>(); var touched = new List<string> { ma };
        var entry = new CkEntry { Id = Uid("h"), At = at, By = m.By, Action = m.Action.Length > 0 ? m.Action : "xu-ly", Ma = ma, Summary = m.Summary, Reason = m.Reason, Note = p.Note ?? "", Ops = ops };
        void SetDec(string ma2, Action<CkDecision> fn)
        {
            var before = state.Decisions.TryGetValue(ma2, out var cur) ? cur.Clone() : null;
            var d = before?.Clone() ?? new CkDecision();
            fn(d); d.UpdatedAt = at; d.UpdatedBy = m.By;
            state.Decisions[ma2] = d; if (!touched.Contains(ma2)) touched.Add(ma2);
            ops.Add(new CkOp { T = "dec", Ma = ma2, Before = before, After = d.Clone() });
        }
        SetDec(ma, d =>
        {
            if (p.Status is not null) { d.Status = p.Status; if (p.Status == "xong") { d.ConfirmedAt = at; if (p.DiffAtDecision is { } x) d.DiffAtDecision = x; } else d.ConfirmedAt = ""; }
            if (p.Cause is not null) d.Cause = p.Cause;
            if (p.CauseNote is not null) d.CauseNote = p.CauseNote;
            if (p.Note is not null) d.Note = p.Note;
            foreach (var x in p.LinkAdd) if (x.Length > 0 && x != ma && !d.Linked.Contains(x)) d.Linked.Add(x);
            foreach (var x in p.LinkRemove) d.Linked.Remove(x);
            foreach (var x in p.FlagAdd) if (!d.Flagged.Contains(x)) d.Flagged.Add(x);
            foreach (var x in p.FlagRemove) d.Flagged.Remove(x);
        });
        foreach (var o in p.LinkAdd) if (o.Length > 0 && o != ma) SetDec(o, d => { if (!d.Linked.Contains(ma)) d.Linked.Add(ma); });
        foreach (var o in p.LinkRemove) if (state.Decisions.ContainsKey(o)) SetDec(o, d => d.Linked.Remove(ma));
        foreach (var a in p.Adjust)
        {
            if (!double.IsFinite(a.Qty) || a.Qty == 0) continue;
            var adj = new CkAdjustment { Id = Uid("a"), Ma = a.Ma ?? ma, Qty = R6(a.Qty), Side = a.Side == "kt" ? "kt" : "kho", Reason = m.Reason, At = at, By = m.By, PairId = a.PairId, EntryId = entry.Id };
            state.Adjustments.Add(adj); if (!touched.Contains(adj.Ma)) touched.Add(adj.Ma); ops.Add(new CkOp { T = "adj+", Id = adj.Id });
        }
        foreach (var id in p.CancelAdj) { var a = state.Adjustments.FirstOrDefault(x => x.Id == id); if (a is { Cancelled: false }) { a.Cancelled = true; a.CancelledAt = at; if (!touched.Contains(a.Ma)) touched.Add(a.Ma); ops.Add(new CkOp { T = "adj-", Id = id }); } }
        entry.Mas = touched;
        state.History.Add(entry);
        return (true, "", entry);
    }

    public sealed record CellChange(string Field, JsonNode? Before, JsonNode? After);
    public static bool NodeEquals(JsonNode? a, JsonNode? b) => NodeText(a) == NodeText(b);
    private static string NodeText(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? JsNum(d) : n?.ToString() ?? "";

    /// <summary>Sửa ô dữ liệu nguồn (mã / số lượng kg) của 1 dòng — dữ liệu đọc từ file không đổi, phần sửa được áp khi đối chiếu (H.editCells).</summary>
    public static (bool Ok, string Error, CkEntry? Entry) EditCells(CkState state, string kind, string key, IEnumerable<CellChange> changes, CkMeta m, string ma = "", string sourceLabel = "", string rowLabel = "")
    {
        var at = m.At.Length > 0 ? m.At : Now();
        var list = changes.Where(c => c.Field is "ma" or "kg" && NodeText(c.Before) != NodeText(c.After)).ToList();
        if (list.Count == 0) return (false, "Không có gì thay đổi", null);
        var ops = new List<CkOp>();
        foreach (var c in list)
        {
            var rec = new CkEdit2 { Id = Uid("e"), Kind = kind, Key = key, Field = c.Field, Before = c.Before?.DeepClone(), After = c.After?.DeepClone(), At = at, By = m.By };
            state.Edits.Add(rec); ops.Add(new CkOp { T = "ed+", Id = rec.Id });
        }
        var mas = list.SelectMany(c => c.Field == "ma" ? new[] { NodeText(c.Before), NodeText(c.After) } : new[] { ma }).Where(x => x.Length > 0).Distinct().ToList();
        var entry = new CkEntry
        {
            Id = Uid("h"), At = at, By = m.By, Action = "sua-du-lieu", Ma = mas.LastOrDefault() ?? "", Mas = mas,
            Summary = $"Sửa dữ liệu {(sourceLabel.Length > 0 ? sourceLabel : kind)} (dòng {(rowLabel.Length > 0 ? rowLabel : key)}): " + string.Join("; ", list.Select(c => (c.Field == "ma" ? "mã " : "số lượng ") + NodeText(c.Before) + " → " + NodeText(c.After))),
            Reason = m.Reason, Ops = ops,
        };
        state.History.Add(entry);
        return (true, "", entry);
    }

    /// <summary>Trả lại giá trị gốc 1 ô (hủy mọi lần sửa ô đó) — 1 thao tác, hoàn tác được (H.revertCell).</summary>
    public static (bool Ok, string Error, CkEntry? Entry) RevertCell(CkState state, string kind, string key, string field, CkMeta m, string ma = "", string sourceLabel = "", string rowLabel = "")
    {
        var at = m.At.Length > 0 ? m.At : Now();
        var act = state.Edits.Where(e => e.Kind == kind && e.Key == key && e.Field == field && !e.Cancelled).ToList();
        if (act.Count == 0) return (false, "Ô này chưa bị sửa", null);
        var ops = new List<CkOp>();
        foreach (var e in act) { e.Cancelled = true; e.CancelledAt = at; ops.Add(new CkOp { T = "ed-", Id = e.Id }); }
        var entry = new CkEntry
        {
            Id = Uid("h"), At = at, By = m.By, Action = "huy-sua-du-lieu", Ma = ma, Mas = ma.Length > 0 ? new() { ma } : new(),
            Summary = $"Trả lại giá trị gốc {(field == "ma" ? "mã" : "số lượng")} ({(sourceLabel.Length > 0 ? sourceLabel : kind)}, dòng {(rowLabel.Length > 0 ? rowLabel : key)})", Ops = ops,
        };
        state.History.Add(entry);
        return (true, "", entry);
    }

    public static (bool Ok, string Error, CkEntry? Entry) SetStatus(CkState s, string ma, string status, CkMeta m) => Commit(s, ma, new Patch { Status = status }, m with { Action = "doi-trang-thai", Summary = "Đổi trạng thái → " + (CkConfig.StatusLabel(status) is { Length: > 0 } l ? l : status) });
    public static (bool Ok, string Error, CkEntry? Entry) Confirm(CkState s, string ma, string cause, string causeNote, double? diffAtDecision, double adjQty, string? note, CkMeta m)
    {
        var p = new Patch { Status = "xong", Cause = cause, CauseNote = causeNote, DiffAtDecision = diffAtDecision, Note = note };
        if (adjQty != 0) p.Adjust.Add((ma, adjQty, "", "kho"));
        return Commit(s, ma, p, m with { Action = "xac-nhan", Summary = "Xác nhận xử lý" + (cause.Length > 0 ? ": " + CkConfig.Cause.GetValueOrDefault(cause, cause) : "") + (adjQty != 0 ? $" · điều chỉnh {(adjQty > 0 ? "+" : "")}{Vn(adjQty)} kg" : "") });
    }
    public static (bool Ok, string Error, CkEntry? Entry) Link(CkState s, string ma, string other, CkMeta m) => Commit(s, ma, new Patch { LinkAdd = { other } }, m with { Action = "gan-ma", Summary = "Gắn mã liên quan: " + other });
    public static (bool Ok, string Error, CkEntry? Entry) Unlink(CkState s, string ma, string other, CkMeta m) => Commit(s, ma, new Patch { LinkRemove = { other } }, m with { Action = "go-ma", Summary = "Gỡ mã liên quan: " + other });
    public static (bool Ok, string Error, CkEntry? Entry) FlagVoucher(CkState s, string ma, string id, bool on, CkMeta m) => Commit(s, ma, on ? new Patch { FlagAdd = { id } } : new Patch { FlagRemove = { id } }, m with { Action = "danh-dau-phieu", Summary = (on ? "Đánh dấu" : "Bỏ đánh dấu") + " chứng từ " + id });
    public static (bool Ok, string Error, CkEntry? Entry) SetNote(CkState s, string ma, string text, CkMeta m) => Commit(s, ma, new Patch { Note = text }, m with { Action = "ghi-chu", Summary = "Ghi chú" });

    /// <summary>Sửa mã (ghi nhầm): chuyển qty kg từ mã thừa sang mã thiếu, ở kho (mặc định) hoặc kế toán.</summary>
    public static (bool Ok, string Error, CkEntry? Entry) Transfer(CkState s, string over, string under, double qty, CkMeta m, string side = "kho")
    {
        var q = Math.Abs(qty); if (!double.IsFinite(q) || q == 0) return (false, "Số lượng chuyển không hợp lệ", null);
        var sd = side == "kt" ? "kt" : "kho"; var pairId = Uid("p");
        var p = new Patch { LinkAdd = { under } };
        if (sd == "kt") { p.Adjust.Add((over, q, pairId, "kt")); p.Adjust.Add((under, -q, pairId, "kt")); }
        else { p.Adjust.Add((over, -q, pairId, "kho")); p.Adjust.Add((under, q, pairId, "kho")); }
        return Commit(s, over, p, m with { Action = "chuyen-ma", Summary = $"Sửa mã ({SideText[sd]}): chuyển {Vn(q)} kg {over} → {under}" });
    }

    public static (bool Ok, string Error, CkEntry? Entry) CancelAdjustment(CkState s, string id, CkMeta m)
    {
        var a = s.Adjustments.FirstOrDefault(x => x.Id == id); if (a is null) return (false, "Không thấy điều chỉnh", null);
        var ids = a.PairId.Length > 0 ? s.Adjustments.Where(x => x.PairId == a.PairId && !x.Cancelled).Select(x => x.Id).ToList() : new List<string> { id };
        var p = new Patch(); p.CancelAdj.AddRange(ids);
        return Commit(s, a.Ma, p, m with { Action = "huy-dieu-chinh", Summary = $"Hủy điều chỉnh {(a.Qty > 0 ? "+" : "")}{Vn(a.Qty)} kg" });
    }

    public static (bool Ok, string Error, CkEntry? Entry) Undo(CkState state, string entryId, CkMeta m)
    {
        var e = state.History.FirstOrDefault(x => x.Id == entryId);
        if (e is null) return (false, "Không thấy thao tác", null);
        if (e.Undone) return (false, "Thao tác này đã được hoàn tác", null);
        if (e.Action == "hoan-tac") return (false, "Không hoàn tác một thao tác hoàn tác — hãy thực hiện lại thao tác gốc", null);
        var idx = state.History.IndexOf(e);
        var newer = state.History.Skip(idx + 1).FirstOrDefault(x => !x.Undone && x.Action != "hoan-tac" && x.Mas.Any(mm => e.Mas.Contains(mm)));
        if (newer is not null) return (false, $"Có thao tác mới hơn trên cùng mã ({(newer.Summary.Length > 0 ? newer.Summary : newer.Action)}) — hãy hoàn tác thao tác đó trước", null);
        var at = m.At.Length > 0 ? m.At : Now(); var ops = new List<CkOp>();
        foreach (var op in Enumerable.Reverse(e.Ops))
        {
            switch (op.T)
            {
                case "dec":
                {
                    var cur = state.Decisions.TryGetValue(op.Ma!, out var c) ? c.Clone() : null;
                    var back = op.Before is null ? new CkDecision { UpdatedAt = at, UpdatedBy = m.By } : op.Before.Clone();
                    state.Decisions[op.Ma!] = back; ops.Add(new CkOp { T = "dec", Ma = op.Ma, Before = cur, After = back.Clone() });
                    break;
                }
                case "ed+": { var x = state.Edits.FirstOrDefault(z => z.Id == op.Id); if (x is { Cancelled: false }) { x.Cancelled = true; x.CancelledAt = at; ops.Add(new CkOp { T = "ed-", Id = op.Id }); } break; }
                case "ed-": { var x = state.Edits.FirstOrDefault(z => z.Id == op.Id); if (x is { Cancelled: true }) { x.Cancelled = false; x.CancelledAt = ""; ops.Add(new CkOp { T = "ed+", Id = op.Id }); } break; }
                case "adj+": { var a = state.Adjustments.FirstOrDefault(z => z.Id == op.Id); if (a is { Cancelled: false }) { a.Cancelled = true; a.CancelledAt = at; a.UndoneBy = e.Id; ops.Add(new CkOp { T = "adj-", Id = op.Id }); } break; }
                case "adj-": { var a = state.Adjustments.FirstOrDefault(z => z.Id == op.Id); if (a is { Cancelled: true }) { a.Cancelled = false; a.CancelledAt = ""; ops.Add(new CkOp { T = "adj+", Id = op.Id }); } break; }
            }
        }
        e.Undone = true; e.UndoneAt = at;
        var bk = new CkEntry { Id = Uid("h"), At = at, By = m.By, Action = "hoan-tac", Ma = e.Ma, Mas = e.Mas.ToList(), Summary = "Hoàn tác: " + (e.Summary.Length > 0 ? e.Summary : e.Action), Reason = m.Reason, Ops = ops, UndoOf = e.Id };
        state.History.Add(bk);
        return (true, "", bk);
    }

    public static bool CanUndo(CkState s, string? ma) => s.History.Any(e => !e.Undone && e.Action != "hoan-tac" && (ma is null || e.Mas.Contains(ma)));

    /// <summary>Gộp trạng thái của máy khác: hợp theo id; quyết định lấy bản mới hơn; cờ hủy / hoàn tác: bên nào đã hủy thì hủy.</summary>
    public static CkState Merge(CkState local, CkState r)
    {
        foreach (var (ma, b) in r.Decisions) if (!local.Decisions.TryGetValue(ma, out var a) || string.CompareOrdinal(b.UpdatedAt, a.UpdatedAt) > 0) local.Decisions[ma] = b;
        void Uni<T>(List<T> la, List<T> lb, Func<T, string> id, Action<T, T> fix)
        {
            var map = la.ToDictionary(id); foreach (var x in lb) { if (!map.TryGetValue(id(x), out var y)) { la.Add(x); map[id(x)] = x; } else fix(y, x); }
        }
        Uni(local.Adjustments, r.Adjustments, x => x.Id, (y, x) => { if (x.Cancelled && !y.Cancelled) { y.Cancelled = true; y.CancelledAt = x.CancelledAt; y.UndoneBy = x.UndoneBy; } });
        Uni(local.Edits, r.Edits, x => x.Id, (y, x) => { if (x.Cancelled && !y.Cancelled) { y.Cancelled = true; y.CancelledAt = x.CancelledAt; } });
        Uni(local.History, r.History, x => x.Id, (y, x) => { if (x.Undone && !y.Undone) { y.Undone = true; y.UndoneAt = x.UndoneAt; } });
        local.History = local.History.OrderBy(x => x.At, StringComparer.Ordinal).ToList();
        local.Adjustments = local.Adjustments.OrderBy(x => x.At, StringComparer.Ordinal).ToList();
        local.Edits = local.Edits.OrderBy(x => x.At, StringComparer.Ordinal).ToList();
        return local;
    }

    /// <summary>Áp các ô đã sửa trực tiếp (mã / kg) lên dòng nguồn; ô có giá trị gốc đã đổi (file mới) thì bỏ qua (stale).</summary>
    public static (List<CkRow> Rows, int Stale, int N) ApplyEdits(string kind, List<CkRow> rows, List<CkEdit2> edits)
    {
        var mine = edits.Where(e => e.Kind == kind && !e.Cancelled).ToList(); if (mine.Count == 0) return (rows, 0, 0);
        var byCell = mine.GroupBy(e => e.Key + "|" + e.Field).ToDictionary(g => g.Key, g => g.OrderBy(x => x.At, StringComparer.Ordinal).ToList());
        int stale = 0, n = 0; var outp = new List<CkRow>();
        foreach (var r in rows)
        {
            var key = kind is "gui" or "ct" or "anan_nhap" or "anan_xuat" ? r.Id ?? "" : r.Row.ToString(System.Globalization.CultureInfo.InvariantCulture);
            byCell.TryGetValue(key + "|ma", out var em); byCell.TryGetValue(key + "|kg", out var ek);
            if (em is null && ek is null) { outp.Add(r); continue; }
            var o = r.Clone(); var changed = false;
            if (em is not null) { if (C3(em[0].Before) == r.Ma) { o.Ma = C3(em[^1].After); n++; changed = true; } else stale++; }
            if (ek is not null) { if (Math.Abs(D(ek[0].Before) - r.Kg) < 1e-6) { o.Kg = D(ek[^1].After); o.SelfLech = null; o.NhetLech = null; n++; changed = true; } else stale++; }
            outp.Add(changed ? o : r);
        }
        return (outp, stale, n);
        static string C3(JsonNode? x) => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : x?.ToJsonString() ?? "";
        static double D(JsonNode? x) => x is JsonValue v && v.TryGetValue<double>(out var d) ? d : Num(C3(x)) is { } dd && !double.IsNaN(dd) ? dd : double.NaN;
    }

    // ---------------- chuẩn hoá khi nạp lại ----------------
    public static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, NumberHandling = JsonNumberHandling.AllowReadingFromString };
}

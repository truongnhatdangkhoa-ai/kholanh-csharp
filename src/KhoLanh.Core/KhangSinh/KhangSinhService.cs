using System.Globalization;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using static KhoLanh.Core.KhangSinh.KhangSinhCore;

namespace KhoLanh.Core.KhangSinh;

/// <summary>
/// Module Kết quả kháng sinh (C#, theo "Tra cứu kháng sinh" của app cũ). Database "ket-qua-khang-sinh", sheet "KhangSinh":
/// mỗi dòng {key, value} y hệt sheet tong-hop/KhangSinh của app cũ — key "s:batch:&lt;ngày&gt;:&lt;lô&gt;:&lt;id&gt;" (value = JSON bản ghi)
/// và "s:ksinhRulesData" (bảng quy định đã sửa). Nút "Chép dữ liệu từ app cũ" chép thẳng sheet đó sang đây.
/// </summary>
public sealed class KhangSinhService
{
    public const string Db = "ket-qua-khang-sinh", Sheet = "KhangSinh", PermKey = "m10", LegacyTab = "tong-hop/KhangSinh";
    private const string RulesKey = "s:ksinhRulesData", BatchPrefix = "s:batch:";
    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;

    public KhangSinhService(Func<ISheetSource> source, Session session, AuditService audit) { _src = source; _session = session; _audit = audit; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void RequireView() { if (!CanView) throw new BusinessException("Tài khoản không có quyền xem Kết quả kháng sinh."); }
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa Kết quả kháng sinh (nhập, xóa, sửa quy định)."); }
    private void RequireExport() { if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file Kết quả kháng sinh."); }
    private void Log(string action, string detail) => _audit.Log(_session.Username, "Kết quả kháng sinh: " + action, detail);

    private readonly List<(string Key, string Value)> _rows = new();   // giữ nguyên thứ tự + mọi khóa khác (l:…)
    public List<Rec> Records { get; private set; } = new();
    /// <summary>1.175: kết quả tôm nhập khẩu (mỗi Size của một phiếu một bản ghi) + lịch sử bản bị phiếu cập nhật thay.</summary>
    public List<KhangSinhNhapKhau.Nhom> NhapKhau { get; private set; } = new();
    public List<KhangSinhNhapKhau.Nhom> NhapKhauCu { get; private set; } = new();
    public List<Rule> Rules { get; private set; } = DefaultRules();
    public bool RulesCustom { get; private set; }
    public bool Loaded { get; private set; }

    public void Load()
    {
        RequireView();
        JsonArray arr;
        try { arr = JsonNode.Parse(_src().Read(Db, Sheet)) as JsonArray ?? new JsonArray(); } catch { arr = new JsonArray(); }
        _rows.Clear();
        foreach (var o in arr.OfType<JsonObject>())
        {
            var k = Str(o["key"]); if (k.Length == 0) continue;
            _rows.Add((k, Str(o["value"])));
        }
        Rebuild();
        Loaded = true;
    }

    private void Rebuild()
    {
        var recs = new List<Rec>();
        foreach (var (k, v) in _rows)
        {
            if (!k.StartsWith(BatchPrefix, StringComparison.Ordinal)) continue;
            try { if (JsonNode.Parse(v) is JsonObject j) recs.Add(new Rec { Key = k, J = j }); } catch { /* bỏ bản ghi hỏng như app cũ */ }
        }
        Records = recs.OrderByDescending(r => r.SavedAt, StringComparer.Ordinal).ToList();
        NhapKhau = KhangSinhNhapKhau.ParseRows(_rows);
        NhapKhauCu = KhangSinhNhapKhau.ParseRows(_rows, history: true);
        Rules = DefaultRules(); RulesCustom = false;
        var rv = _rows.FirstOrDefault(r => r.Key == RulesKey).Value;
        if (!string.IsNullOrEmpty(rv))
            try
            {
                var list = (JsonNode.Parse(rv) as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(Rule.FromJson).ToList();
                if (list.Count > 0) { Rules = list; RulesCustom = true; }
            }
            catch { }
    }

    private void Save()
    {
        var arr = new JsonArray(_rows.Select(r => (JsonNode)new JsonObject { ["key"] = r.Key, ["value"] = r.Value }).ToArray());
        _src().Write(Db, new Dictionary<string, JsonArray?> { [Sheet] = arr });
        Rebuild();
    }
    private void Set(string key, JsonNode value)
    {
        var s = value is JsonValue v && v.TryGetValue<string>(out var str) ? str : value.ToJsonString();
        var i = _rows.FindIndex(r => r.Key == key);
        if (i >= 0) _rows[i] = (key, s); else _rows.Add((key, s));
    }
    private void Del(string key) => _rows.RemoveAll(r => r.Key == key);
    private static string Iso() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static string NewId()
    {
        const string a = "0123456789abcdefghijklmnopqrstuvwxyz";
        static string B36(long n) { var s = ""; do { s = a[(int)(n % 36)] + s; n /= 36; } while (n > 0); return s; }
        return B36(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) + "_" + new string(Enumerable.Range(0, 6).Select(_ => a[Random.Shared.Next(36)]).ToArray());
    }
    private static string KeyFor(JsonObject rec) => $"{BatchPrefix}{Str(rec["date"])}:{Str(rec["batch"])}:{NewId()}";

    // ------------------------------------------------------------------ tra cứu / ghi bản ghi

    public Rule? Pick(IReadOnlyDictionary<string, string> inputs) => PickBest(Rules, inputs);

    /// <summary>Một dòng nhập theo lô (đã tính kết quả).</summary>
    public sealed record Entry(string Date, string Batch, string Note, Dictionary<string, string> Inputs, Rule? Rule)
    {
        public string Dat => Rule?.Dat ?? "Không xác định";
    }
    public Entry MakeEntry(string date, string batch, string note, Dictionary<string, string> inputs) => new(ToIso(date), batch.Trim(), note.Trim(), inputs, Pick(inputs));
    public bool Exists(string isoDate, string batch) =>
        Records.Any(r => r.Date == isoDate && r.Batch.Trim().Equals(batch.Trim(), StringComparison.OrdinalIgnoreCase));

    private static JsonObject RecordOf(string date, string batch, string note, IReadOnlyDictionary<string, string> inputs, Rule? row, string? source, string? dat = null, string? kyhieu = null, string? mota = null)
    {
        var o = new JsonObject
        {
            ["date"] = date, ["batch"] = batch, ["note"] = note, ["inputs"] = InputsJson(inputs),
            ["stt"] = dat is null && row is not null ? row.Stt : null,
            ["dat"] = dat ?? row?.Dat ?? "Không xác định", ["kyhieu"] = kyhieu ?? row?.KyHieu ?? "", ["mota"] = mota ?? row?.MoTa ?? "",
            ["savedAt"] = Iso(),
        };
        if (source is not null) o["source"] = source;
        return o;
    }

    /// <summary>Lưu các dòng nhập theo lô vào Dữ liệu (Ngày + lô bắt buộc).</summary>
    public int SaveEntries(IEnumerable<Entry> entries)
    {
        RequireEdit();
        var list = entries.ToList();
        foreach (var e in list)
        {
            if (!IsIso(e.Date)) throw new BusinessException($"Lô \"{e.Batch}\": ngày \"{e.Date}\" không hợp lệ (dd/mm/yyyy).");
            if (e.Batch.Length == 0) throw new BusinessException("Có dòng chưa ghi Mã / số lô hàng.");
        }
        foreach (var e in list) { var o = RecordOf(e.Date, e.Batch, e.Note, e.Inputs, e.Rule, null); Set(KeyFor(o), o); }
        Save();
        Log("Nhập theo lô", $"{list.Count} lô");
        return list.Count;
    }

    // ------------------------------------------------------------------ xóa

    /// <summary>Xóa 1 dòng: chế độ Lần 1 = cả bản ghi; Kiểm lại = chỉ lần kiểm lại đó (lần 1 giữ nguyên).</summary>
    public void DeleteRow(Row row)
    {
        RequireEdit();
        if (row.Kl is { } kl) { var id = Lan2Id(kl); RemoveLan2(row.R, l => Lan2Id(l) == id); }
        else Del(row.R.Key);
        Save();
        Log("Xóa dòng", $"{Dmy(row.Date)} · {row.Batch}" + (row.Kl is not null ? $" (kiểm lại lần {row.LanNo})" : ""));
    }
    private void RemoveLan2(Rec rec, Func<JsonObject, bool> pred)
    {
        var r = (JsonObject)rec.J.DeepClone();
        Lan2Normalize(r, Lan2ListOf(r).Where(l => !pred(l)));
        var tmp = new Rec { Key = rec.Key, J = r };
        if (tmp.Lan2.Count == 0 && !tmp.HasLan1) Del(rec.Key); else Set(rec.Key, r);
    }
    private void DropLan1(Rec rec)
    {
        if (rec.Lan2.Count == 0) { Del(rec.Key); return; }
        var r = (JsonObject)rec.J.DeepClone();
        r["dat"] = ""; r["kyhieu"] = ""; r["inputs"] = new JsonObject(); r["mota"] = "(đã xoá kết quả lần 1 — còn kết quả kiểm lại)"; r["source"] = "lan2-import";
        r.Remove("lan1Src");
        Set(rec.Key, r);
    }

    /// <summary>Xóa dữ liệu 1 ngày: Lần 1 = mọi lô có ngày kiểm = ngày đó (lô còn kiểm lại thì giữ phần kiểm lại); Kiểm lại = các lần kiểm lại ngày đó.</summary>
    public int DeleteDay(string mode, string day)
    {
        RequireEdit();
        int n = 0;
        if (mode == ModeKl)
            foreach (var r in Records.Where(r => r.Lan2.Any(l => Str(l["date"]) == day)).ToList()) { RemoveLan2(r, l => Str(l["date"]) == day); n++; }
        else
            foreach (var r in Records.Where(r => r.HasLan1 && r.GroupDateL1 == day).ToList()) { DropLan1(r); n++; }
        Save();
        Log("Xóa theo ngày", $"{(mode == ModeKl ? "kiểm lại" : "lần 1")} {Dmy(day)}: {n} dòng");
        return n;
    }

    public sealed class Phieu { public string Key = "", Label = ""; public int Kind; public List<Rec> Recs = new(); public HashSet<string> Samples = new(); }
    public List<Phieu> CollectPhieu()
    {
        var map = new Dictionary<string, Phieu>();
        foreach (var rec in Records)
        {
            foreach (var l in rec.Lan2)
            {
                var k = PhieuKeyL2(l);
                if (!map.TryGetValue(k, out var e)) map[k] = e = new Phieu { Key = k, Kind = 2, Label = $"Lần 2 · Số {(Str(l["so"]) is { Length: > 0 } so ? so : "?")} · phân tích {(Str(l["date"]) is { Length: > 0 } d ? Dmy(d) : "?")} · {Str(l["file"])}" };
                if (!e.Recs.Contains(rec)) e.Recs.Add(rec); e.Samples.Add(Str(l["stt"]));
            }
            if (rec.Lan1Src is { } src)
            {
                var k = PhieuKeyL1(src);
                if (!map.TryGetValue(k, out var e)) map[k] = e = new Phieu { Key = k, Kind = 1, Label = $"Lần 1 (Kho) · kiểm {(Str(src["ngayKiem"]) is { Length: > 0 } d ? Dmy(d) : "?")} · {Str(src["file"])}" };
                if (!e.Recs.Contains(rec)) e.Recs.Add(rec); e.Samples.Add(Str(src["stt"]));
            }
        }
        return map.Values.OrderBy(p => p.Label, StringComparer.Ordinal).ToList();
    }
    /// <summary>Xóa đúng phần dữ liệu của 1 phiếu PDF đã nhập (phiếu lần 1 hoặc lần 2), không đụng phiếu kia.</summary>
    public (int Del, int Upd) DeletePhieu(Phieu p)
    {
        RequireEdit();
        int del = 0, upd = 0;
        foreach (var rec in p.Recs)
        {
            if (p.Kind == 2)
            {
                var rest = rec.Lan2.Where(l => PhieuKeyL2(l) != p.Key).ToList();
                var hasL1 = rec.Lan1Src is not null || (rec.Source != "lan2-import" && (rec.Dat.Length > 0 || rec.KyHieu.Length > 0));
                if (rest.Count == 0 && !hasL1) { Del(rec.Key); del++; continue; }
                var r = (JsonObject)rec.J.DeepClone(); Lan2Normalize(r, rest); Set(rec.Key, r); upd++;
            }
            else
            {
                if (rec.Lan2.Count == 0) { Del(rec.Key); del++; continue; }
                var r = (JsonObject)rec.J.DeepClone();
                r["dat"] = ""; r["kyhieu"] = ""; r["inputs"] = new JsonObject(); r["mota"] = "(đã xoá kết quả lần 1 — còn kết quả lần 2)"; r.Remove("lan1Src");
                Set(rec.Key, r); upd++;
            }
        }
        Save();
        Log("Xóa theo phiếu", $"{p.Label}: xóa {del}, giữ {upd}");
        return (del, upd);
    }

    public static string MonthOf(string? d) { var s = (d ?? "").Length >= 7 ? d![..7] : ""; return System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{4}-\d{2}$") ? s : ""; }
    public static string MonthVn(string m) => $"Tháng {m[5..]}/{m[..4]}";
    public List<(string Month, int N, int Recs)> Months(string kind)
    {
        var map = new Dictionary<string, (int n, HashSet<string> recs)>();
        void Add(string m, Rec r) { if (!map.TryGetValue(m, out var e)) map[m] = e = (0, new()); e.recs.Add(r.Key); map[m] = (e.n + 1, e.recs); }
        foreach (var r in Records)
        {
            if (kind == ModeL1) { if (r.HasLan1 && MonthOf(r.GroupDateL1) is { Length: > 0 } m) Add(m, r); }
            else foreach (var l in r.Lan2) if (MonthOf(Str(l["date"])) is { Length: > 0 } m) Add(m, r);
        }
        return map.OrderByDescending(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value.n, p.Value.recs.Count)).ToList();
    }
    public int DeleteMonth(string kind, string month)
    {
        RequireEdit();
        int n = 0;
        foreach (var r in Records.ToList())
        {
            if (kind == ModeKl) { if (r.Lan2.Any(l => MonthOf(Str(l["date"])) == month)) { RemoveLan2(r, l => MonthOf(Str(l["date"])) == month); n++; } }
            else if (r.HasLan1 && MonthOf(r.GroupDateL1) == month) { DropLan1(r); n++; }
        }
        Save();
        Log("Xóa theo tháng", $"{(kind == ModeKl ? "kiểm lại" : "lần 1")} {MonthVn(month)}: {n} dòng");
        return n;
    }
    public int CountBadDates() => Records.Count(r => !IsIso(r.Date));
    public int DeleteBadDates()
    {
        RequireEdit();
        var bad = Records.Where(r => !IsIso(r.Date)).ToList();
        foreach (var r in bad) Del(r.Key);
        if (bad.Count > 0) { Save(); Log("Xóa dòng lỗi ngày", bad.Count + " dòng"); }
        return bad.Count;
    }
    public int ClearAll()
    {
        RequireEdit();
        var n = Records.Count;
        _rows.RemoveAll(r => r.Key.StartsWith(BatchPrefix, StringComparison.Ordinal) || r.Key.StartsWith(KhangSinhNhapKhau.Prefix, StringComparison.Ordinal) || r.Key.StartsWith(KhangSinhNhapKhau.HistPrefix, StringComparison.Ordinal));
        Save();
        Log("Xóa toàn bộ dữ liệu", n + " bản ghi");
        return n;
    }

    // ------------------------------------------------------------------ tôm nhập khẩu (1.175)

    public sealed record NkResult(int Added, int Replaced, int Same, List<string> Notes);

    /// <summary>Phiếu nào đã có (cùng INV + Contract + Size) → sẽ bị thay: [(bản cũ, bản mới)].</summary>
    public List<(KhangSinhNhapKhau.Nhom Old, string NewSo, string Size)> NkConflicts(IEnumerable<KhangSinhNhapKhau.Phieu> list)
    {
        var o = new List<(KhangSinhNhapKhau.Nhom, string, string)>();
        foreach (var p in list)
            foreach (var r in KhangSinhNhapKhau.ToRecords(p, ""))
                foreach (var old in NhapKhau.Where(n => KhangSinhNhapKhau.Replaces(n, r))) o.Add((old, p.So, Str(r["size"])));
        return o;
    }

    /// <summary>Nạp phiếu tôm nhập khẩu. Cùng INV + Contract + Size: bản mới thay bản cũ, bản cũ giữ ở lịch sử (phiếu "Cập nhật").</summary>
    public NkResult ImportNhapKhau(IEnumerable<KhangSinhNhapKhau.Phieu> list)
    {
        RequireEdit();
        int add = 0, rep = 0, same = 0; var notes = new List<string>(); var now = Iso();
        foreach (var p in list)
            foreach (var r in KhangSinhNhapKhau.ToRecords(p, now))
            {
                var olds = NhapKhau.Where(n => KhangSinhNhapKhau.Replaces(n, r)).ToList();
                if (olds.Any(o => o.So == Str(r["so"]) && o.Date == Str(r["date"]) && o.J["mau"]?.ToJsonString() == r["mau"]?.ToJsonString()))
                { same++; continue; }
                foreach (var old in olds)
                {
                    var h = (JsonObject)old.J.DeepClone(); h["replacedBy"] = Str(r["so"]); h["replacedAt"] = now;
                    Del(old.Key); Set(KhangSinhNhapKhau.HistPrefix + old.Key[KhangSinhNhapKhau.Prefix.Length..], h);
                    notes.Add($"INV {old.Invoice} · {old.Hang} {old.Size}: phiếu {Str(r["so"])} thay phiếu {old.So}");
                }
                Set(KhangSinhNhapKhau.Prefix + NewId(), r);
                if (olds.Count > 0) rep++; else add++;
            }
        if (add + rep > 0) { Save(); Log("Nạp phiếu tôm nhập khẩu", $"{add} mới · {rep} thay thế"); }
        return new NkResult(add, rep, same, notes);
    }

    public int DeleteNhapKhau(IEnumerable<KhangSinhNhapKhau.Nhom> list)
    {
        RequireEdit();
        var n = 0; foreach (var x in list) { Del(x.Key); n++; }
        if (n > 0) { Save(); Log("Xóa tôm nhập khẩu", n + " dòng"); }
        return n;
    }

    // ------------------------------------------------------------------ quy định

    public void SaveRules(IEnumerable<Rule> rules)
    {
        RequireEdit();
        var list = rules.Where(r => r.Dat.Trim().Length > 0 || r.KyHieu.Trim().Length > 0).ToList();
        if (list.Count == 0) throw new BusinessException("Bảng quy định trống — phải có ít nhất 1 trường hợp.");
        for (var i = 0; i < list.Count; i++)
        {
            list[i].Stt = i + 1;
            foreach (var k in FieldKeys)
            {
                var c = (list[i].C.GetValueOrDefault(k) ?? "").Trim();
                if (c.Length == 0) c = "ND";
                if (!c.Equals("ND", StringComparison.OrdinalIgnoreCase) && !System.Text.RegularExpressions.Regex.IsMatch(c, @"^[<>]\s*[\d.,]+"))
                    throw new BusinessException($"Trường hợp {i + 1}, cột {Fields.First(f => f.Key == k).Label}: \"{c}\" không đúng dạng (ND, <10, >0.3…).");
                list[i].C[k] = c.Equals("ND", StringComparison.OrdinalIgnoreCase) ? "ND" : c;
            }
        }
        Set(RulesKey, JsonValue.Create(new JsonArray(list.Select(r => (JsonNode)r.ToJson()).ToArray()).ToJsonString())!);
        Save();
        Log("Sửa quy định", list.Count + " trường hợp");
    }
    public void ResetRules()
    {
        RequireEdit();
        Del(RulesKey); Save();
        Log("Khôi phục quy định gốc", "83 trường hợp");
    }

    // ------------------------------------------------------------------ nhập Excel

    public sealed class SheetIn
    {
        public string Name = "";
        public List<string[]> Aoa = new();
        public int HeaderIdx;
        public string Date = "";
        public List<string> Headers => HeaderIdx < Aoa.Count ? Aoa[HeaderIdx].Select(h => (h ?? "").Trim()).ToList() : new();
        public List<string[]> Rows => Aoa.Skip(HeaderIdx + 1).Where(r => r.Any(c => (c ?? "").Trim().Length > 0)).ToList();
    }
    /// <summary>Đọc file Excel: mỗi sheet = 1 ngày; tự dò dòng tiêu đề và đoán ngày từ tên sheet.</summary>
    public static List<SheetIn> ReadExcel(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(fs);
        var o = new List<SheetIn>();
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed(); var aoa = new List<string[]>();
            if (used is not null)
            {
                int r1 = 1, r2 = used.LastRow().RowNumber(), c2 = used.LastColumn().ColumnNumber();
                for (var r = r1; r <= r2; r++)
                {
                    var row = new string[c2];
                    for (var c = 1; c <= c2; c++) row[c - 1] = CellText(ws.Cell(r, c));
                    aoa.Add(row);
                }
            }
            var s = new SheetIn { Name = ws.Name, Aoa = aoa, Date = GuessDateFromSheetName(ws.Name) };
            s.HeaderIdx = aoa.Count == 0 ? 0 : DetectHeaderRow(aoa);
            o.Add(s);
        }
        return o;
    }
    private static string CellText(IXLCell c)
    {
        try
        {
            if (c.DataType == XLDataType.DateTime) return c.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (c.DataType == XLDataType.Number) return c.GetDouble().ToString("G15", CultureInfo.InvariantCulture);
            return c.GetFormattedString().Trim();
        }
        catch { return c.Value.ToString() ?? ""; }
    }

    public sealed record ImportResult(int Ok, int Total, int Dup, int Skip, List<string> SkippedSheets, List<string> Details);
    /// <summary>Nhập các sheet đã khớp cột. map: khóa mục (date, batch, enro…) → chỉ số cột, -1 = không có.</summary>
    public ImportResult ImportSheets(IEnumerable<SheetIn> sheets, IReadOnlyDictionary<string, int> map, bool skipDup)
    {
        RequireEdit();
        int M(string k) => map.TryGetValue(k, out var v) ? v : -1;
        if (M("batch") < 0) throw new BusinessException("Chưa khớp cột Mã lô hàng.");
        string G(string[] r, int i) => i >= 0 && i < r.Length ? (r[i] ?? "").Trim() : "";
        var useRowDate = M("date") >= 0;
        var existing = new HashSet<string>(skipDup ? Records.Select(r => r.Date + "|" + r.Batch) : Array.Empty<string>());
        int ok = 0, dup = 0, skip = 0, total = 0;
        var skippedSheets = new List<string>(); var details = new List<string>();
        foreach (var sh in sheets)
        {
            var rows = sh.Rows; if (rows.Count == 0) continue;
            total += rows.Count;
            if (!useRowDate && !IsIso(sh.Date)) { skippedSheets.Add(sh.Name); continue; }
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var date = useRowDate && ToIso(G(r, M("date"))) is { Length: > 0 } d ? d : sh.Date;
                if (!IsIso(date)) { skip++; details.Add($"Sheet \"{sh.Name}\" dòng {i + 1}: thiếu / sai Ngày"); continue; }
                var batch = G(r, M("batch"));
                if (batch.Length == 0) { skip++; details.Add($"Sheet \"{sh.Name}\" ({Dmy(date)}) dòng {i + 1}: thiếu Mã lô hàng"); continue; }
                if (skipDup && existing.Contains(date + "|" + batch)) { dup++; continue; }
                var inputs = FieldKeys.ToDictionary(k => k, k => G(r, M(k)));
                var note = string.Join(" · ", new[] { G(r, M("code")), G(r, M("note")) }.Where(x => x.Length > 0));
                var fileDat = G(r, M("dat"));
                var o = fileDat.Length > 0
                    ? RecordOf(date, batch, note, inputs, null, "excel-import-sheet:" + sh.Name, fileDat, G(r, M("kyhieu")), "(kết quả lấy từ file Excel)")
                    : RecordOf(date, batch, note, inputs, Pick(inputs), "excel-import-sheet:" + sh.Name);
                Set(KeyFor(o), o); existing.Add(date + "|" + batch); ok++;
            }
        }
        if (ok > 0) Save();
        Log("Nhập Excel", $"{ok}/{total} dòng, bỏ {dup} trùng, {skip} lỗi");
        return new ImportResult(ok, total, dup, skip, skippedSheets, details);
    }

    // ------------------------------------------------------------------ nạp PDF phiếu kết quả (lần 1 / kiểm lại)

    /// <summary>Các khóa bản ghi của 1 lô (batch:&lt;ngày&gt;:&lt;lô&gt;:…) — như storage.list của app cũ.</summary>
    public List<string> KeysOf(string date, string batch)
    {
        var pre = $"{BatchPrefix}{date}:{batch}:";
        return _rows.Where(r => r.Key.StartsWith(pre, StringComparison.Ordinal)).Select(r => r.Key).ToList();
    }
    private JsonObject? RecOf(string key)
    {
        var v = _rows.FirstOrDefault(r => r.Key == key).Value;
        try { return string.IsNullOrEmpty(v) ? null : JsonNode.Parse(v) as JsonObject; } catch { return null; }
    }

    /// <summary>Ghi kèm kết quả lần 1 đã lưu của lô được ghép vào từng mẫu (để so với cột KQ lần 1 của phiếu kiểm lại).</summary>
    public void AttachSavedLan1(IEnumerable<KhangSinhPdf.Phieu> list)
    {
        foreach (var r in list)
            foreach (var s in r.Samples)
            {
                var rf = KhangSinhPdf.LotRef(s); if (rf is null) continue;
                foreach (var k in KeysOf(rf.Date, (rf.Batches ?? new List<string> { rf.Batch })[0]))
                {
                    var rec = RecOf(k); if (rec is null) continue;
                    var dat = Str(rec["dat"]); var ky = Str(rec["kyhieu"]);
                    if (dat.Length > 0 || ky.Length > 0) { s.Lan1Saved = ky.Length > 0 ? ky : dat; break; }
                }
            }
    }

    private static JsonObject Lan2Payload(KhangSinhPdf.Sample s, KhangSinhPdf.Phieu r) => new()
    {
        ["dat"] = s.Kq2, ["kyhieu"] = s.DatTinh, ["kq1"] = s.Kq1, ["inputs"] = InputsJson(s.Inputs), ["ghichu"] = s.GhiChu,
        ["mau"] = string.Join(" · ", s.Lines), ["group"] = s.Group, ["date"] = r.Date, ["so"] = r.So, ["file"] = r.File, ["stt"] = s.Stt, ["trang"] = s.Page,
        ["canhBao"] = new JsonArray(s.Issues.Where(i => i.Lv != "info").Select(i => (JsonNode)JsonValue.Create(i.Msg)!).ToArray()),
        ["xacNhanTay"] = KhangSinhPdf.Flag(s) && s.Ok, ["suaTay"] = s.Edited, ["savedAt"] = Iso(),
    };

    /// <summary>Nhập các phiếu PDF đã đối soát. Phiếu lần 1: khóa Ngày nhập + Số lô (lô đã có → cập nhật kết quả lần 1, giữ kiểm lại).
    /// Phiếu kiểm lại: mẫu có "Lô: N (ngày)" → thêm vào lô đó; không ghi lô → lưu dòng riêng (nếu keepOthers).</summary>
    public string ImportPdf(IReadOnlyList<KhangSinhPdf.Phieu> list, bool keepOthers)
    {
        RequireEdit();
        if (list.Count == 0) return "";
        if (list.SelectMany(r => r.Samples).Any(KhangSinhPdf.Need) || list.Any(r => KhangSinhPdf.FileFlag(r) && !r.FileOk))
            throw new BusinessException("Còn mục cảnh báo chưa kiểm — hãy so với PDF gốc rồi tick \"Đã so\" trước khi nhập.");
        string msg;
        if (list[0].Kind == KhangSinhPdf.KindLan1)
        {
            int add = 0, upd = 0, err = 0;
            foreach (var r in list)
                foreach (var s in r.Samples)
                {
                    var lot = s.Lot; if (lot is null || lot.Date.Length == 0) { err++; continue; }
                    var src = new JsonObject
                    {
                        ["file"] = r.File, ["ngayKiem"] = r.Date, ["stt"] = s.Stt, ["trang"] = s.Page,
                        ["canhBao"] = new JsonArray(s.Issues.Where(i => i.Lv != "info").Select(i => (JsonNode)JsonValue.Create(i.Msg)!).ToArray()),
                        ["xacNhanTay"] = KhangSinhPdf.Flag(s) && s.Ok, ["suaTay"] = s.Edited, ["savedAt"] = Iso(),
                    };
                    JsonObject Fields() => new() { ["dat"] = s.Kq2, ["kyhieu"] = s.DatTinh, ["inputs"] = InputsJson(s.Inputs), ["note"] = s.Code, ["mota"] = s.GhiChu, ["lan1Src"] = src.DeepClone() };
                    var keys = KeysOf(lot.Date, lot.Batch);
                    if (keys.Count > 0)
                    {
                        foreach (var k in keys) { var rec = RecOf(k); if (rec is null) continue; foreach (var kv in Fields()) rec[kv.Key] = kv.Value?.DeepClone(); Set(k, rec); }
                        upd++;
                    }
                    else
                    {
                        var o = new JsonObject { ["date"] = lot.Date, ["batch"] = lot.Batch, ["stt"] = null, ["savedAt"] = Iso(), ["source"] = "lan1-import" };
                        foreach (var kv in Fields()) o[kv.Key] = kv.Value?.DeepClone();
                        Set(KeyFor(o), o); add++;
                    }
                }
            msg = $"Hoàn tất: {add} lô mới" + (upd > 0 ? $", {upd} lô đã có được cập nhật kết quả lần 1" : "") + (err > 0 ? $", lỗi {err}" : "") + ". Xem tại tab \"Dữ liệu\".";
        }
        else
        {
            int upd = 0, newLot = 0, sep = 0, sepUpd = 0, skip = 0;
            void AddLan2(string k, JsonObject lan2)
            {
                var rec = RecOf(k); if (rec is null) return;
                var all = Lan2ListOf(rec).Select(x => (JsonObject)x.DeepClone()).ToList(); all.Add((JsonObject)lan2.DeepClone());
                Lan2Normalize(rec, all); Set(k, rec);
            }
            foreach (var r in list)
                foreach (var s in r.Samples)
                {
                    var lan2 = Lan2Payload(s, r);
                    var rf = KhangSinhPdf.LotRef(s);
                    if (rf is not null)
                    {
                        foreach (var bt in rf.Batches ?? new List<string> { rf.Batch })
                        {
                            var keys = KeysOf(rf.Date, bt);
                            if (keys.Count > 0) { foreach (var k in keys) AddLan2(k, lan2); upd++; }
                            else
                            {
                                var o = new JsonObject
                                {
                                    ["date"] = rf.Date, ["batch"] = bt, ["note"] = Str(lan2["mau"]), ["inputs"] = new JsonObject(), ["stt"] = null, ["dat"] = "", ["kyhieu"] = "",
                                    ["mota"] = "(chưa có kết quả lần 1 — tạo từ phiếu lần 2)", ["lan2"] = lan2.DeepClone(), ["lan2All"] = new JsonArray(lan2.DeepClone()),
                                    ["savedAt"] = Iso(), ["source"] = "lan2-import",
                                };
                                Set(KeyFor(o), o); newLot++;
                            }
                        }
                        continue;
                    }
                    if (!keepOthers || r.Date.Length == 0) { skip++; continue; }
                    var batch = (s.Lines.Count > 0 ? string.Join(" · ", s.Lines) : "Mẫu " + KhangSinhPdf.N(s.Stt)).Replace(":", "：");
                    var ks = KeysOf(r.Date, batch);
                    if (ks.Count > 0) { foreach (var k in ks) AddLan2(k, lan2); sepUpd++; }
                    else
                    {
                        var o = new JsonObject
                        {
                            ["date"] = r.Date, ["batch"] = batch, ["note"] = s.Group, ["inputs"] = InputsJson(s.Inputs), ["stt"] = null, ["dat"] = s.Kq2, ["kyhieu"] = s.DatTinh,
                            ["mota"] = "(kết quả lấy từ phiếu lần 2)", ["lan2"] = lan2.DeepClone(), ["savedAt"] = Iso(), ["source"] = "lan2-import",
                        };
                        Set(KeyFor(o), o); sep++;
                    }
                }
            msg = $"Hoàn tất: {upd} lô đã cập nhật KQ lần 2" + (newLot > 0 ? $", {newLot} lô chưa có lần 1 (đã tạo mới)" : "")
                + (sep > 0 || sepUpd > 0 ? $", {sep} mẫu lưu dòng riêng" + (sepUpd > 0 ? $" (+{sepUpd} cập nhật)" : "") : "") + (skip > 0 ? $", bỏ qua {skip}" : "") + ". Xem tại tab \"Dữ liệu\".";
        }
        Save();
        Log("Nạp PDF phiếu " + (list[0].Kind == KhangSinhPdf.KindLan1 ? "lần 1" : "kiểm lại"), string.Join(", ", list.Select(r => r.File)) + " — " + msg);
        return msg;
    }

    // ------------------------------------------------------------------ xuất Excel

    public void Export(string path, IReadOnlyList<Row> rows, string mode)
    {
        RequireExport();
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(mode == ModeKl ? "Kiem lai" : "Lan 1");
        var heads = new List<string> { "Ngày", "Lô hàng", "Ghi chú", "Kết quả", "Ký hiệu", "KQ kiểm lại" };
        heads.AddRange(Fields.Select(f => f.Label)); heads.Add("Lưu lúc");
        for (var c = 0; c < heads.Count; c++) ws.Cell(1, c + 1).Value = heads[c];
        var hr = ws.Range(1, 1, 1, heads.Count); hr.Style.Font.Bold = true; hr.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
        var y = 2;
        foreach (var r in rows)
        {
            ws.Cell(y, 1).Value = Dmy(r.Date); ws.Cell(y, 2).Value = r.Batch; ws.Cell(y, 3).Value = r.Note;
            ws.Cell(y, 4).Value = r.Dat; ws.Cell(y, 5).Value = r.KyHieu; ws.Cell(y, 6).Value = Lan2Text(r);
            for (var i = 0; i < FieldKeys.Length; i++) ws.Cell(y, 7 + i).Value = IsBlank(r.Inputs.GetValueOrDefault(FieldKeys[i])) ? "ND" : r.Inputs[FieldKeys[i]];
            ws.Cell(y, 7 + FieldKeys.Length).Value = SavedText(r.SavedAt);
            if (r.Dat.ToUpperInvariant().Contains("KHÔNG ĐẠT")) ws.Range(y, 1, y, heads.Count).Style.Font.FontColor = XLColor.FromHtml("#C0392B");
            y++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(8, 60);
        wb.SaveInPlace(path);
        Log("Xuất Excel", $"{rows.Count} dòng → {Path.GetFileName(path)}");
    }
    public static string SavedText(string iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) : "";
}

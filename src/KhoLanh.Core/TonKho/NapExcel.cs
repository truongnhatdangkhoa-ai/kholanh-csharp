using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.TonKho;

/// <summary>"Ngày số liệu" dùng chung Tồn kho An An + Tồn kho gửi (port data-date.js) — lưu dạng dd/mm/yyyy.</summary>
public static class DataDate
{
    private static bool Valid(int d, int m, int y) => y >= 1 && y <= 9999 && m >= 1 && m <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m);
    private static string Fmt(int d, int m, int y) => $"{d:00}/{m:00}/{y}";

    /// <summary>Năm thiếu → năm nay; ra ngày tương lai quá 7 ngày thì hiểu là năm trước.</summary>
    private static int Year(int d, int m, string? yRaw, DateTime now)
    {
        if (!string.IsNullOrEmpty(yRaw)) { var y = int.Parse(yRaw, CultureInfo.InvariantCulture); return y < 100 ? 2000 + y : y; }
        var yy = now.Year;
        if (m < 1 || m > 12) return yy;
        var dt = new DateTime(yy, m, 1).AddDays(d - 1); // giống new Date(y, m-1, d) (tràn ngày thì sang tháng sau)
        return (dt - now).TotalMilliseconds > 7 * 86400000.0 ? yy - 1 : yy;
    }

    /// <summary>"23/9", "23.09.2026", "23-9-26", "23092026" → "23/09/2026"; sai → "".</summary>
    public static string Parse(string? s, DateTime? now = null)
    {
        var t = JsText.JsTrim(s ?? ""); if (t.Length == 0) return "";
        var m = Regex.Match(t, @"^(\d{1,2})\s*[./\-\s]\s*(\d{1,2})(?:\s*[./\-\s]\s*(\d{2}|\d{4}))?$");
        if (!m.Success) m = Regex.Match(t, @"^(\d{2})(\d{2})(\d{4})$");
        if (!m.Success) return "";
        var d = int.Parse(m.Groups[1].Value); var mo = int.Parse(m.Groups[2].Value);
        var y = Year(d, mo, m.Groups[3].Success ? m.Groups[3].Value : null, now ?? DateTime.Now);
        return Valid(d, mo, y) ? Fmt(d, mo, y) : "";
    }

    /// <summary>Đoán từ tên file: "TON 23.9.xlsx", "Ton_23-09-2026", "tonkho 23.9.26 (2).xlsx", "TON 23092026".</summary>
    public static string Guess(string? name, DateTime? now = null)
    {
        var bas = Regex.Replace(name ?? "", @"\.[^.]+$", "");
        foreach (Match m in Regex.Matches(bas, @"(?:^|\D)(\d{1,2})[._\-/ ](\d{1,2})(?:[._\-/ ](\d{4}|\d{2}))?(?!\d)"))
        {
            var d = int.Parse(m.Groups[1].Value); var mo = int.Parse(m.Groups[2].Value);
            if (d < 1 || d > 31 || mo < 1 || mo > 12) continue;
            var y = Year(d, mo, m.Groups[3].Success ? m.Groups[3].Value : null, now ?? DateTime.Now);
            if (Valid(d, mo, y)) return Fmt(d, mo, y);
        }
        var m8 = Regex.Match(bas, @"(?:^|\D)(\d{2})(\d{2})(\d{4})(?!\d)");
        if (m8.Success)
        {
            int d = int.Parse(m8.Groups[1].Value), mo = int.Parse(m8.Groups[2].Value), y = int.Parse(m8.Groups[3].Value);
            if (Valid(d, mo, y)) return Fmt(d, mo, y);
        }
        return "";
    }

    /// <summary>dd/mm/yyyy → yyyy-mm-dd (ngayIso của nap-excel.js).</summary>
    public static string Iso(string? s)
    {
        var t = JsText.JsTrim(s ?? "");
        var m = Regex.Match(t, @"^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$");
        if (m.Success) return $"{m.Groups[3].Value}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[1].Value.PadLeft(2, '0')}";
        m = Regex.Match(t, @"^(\d{4})-(\d{2})-(\d{2})");
        return m.Success ? $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}" : "";
    }
}

/// <summary>
/// Kiểm tra file Excel trước khi nạp + nhật ký nạp (port nap-excel-core.js + nap-excel.js).
/// Nhật ký ghi ở sheet "NapLog" của chính database module (mọi máy cùng thấy); bản hoàn tác (dữ liệu TRƯỚC khi nạp) lưu trên máy đã nạp.
/// </summary>
public static class NapExcel
{
    public const string LogSheet = "NapLog";
    public const int MaxLog = 300;

    public sealed record Problem(int Row, string Level, string Msg);

    public sealed class Check
    {
        public List<string> Errors = new(), Warns = new(), Infos = new();
        public List<Problem> Problems = new();
        public int Total, Ok, Warn, Err, Items;
        public double Kg, Kien;
    }

    private static string NoAcc(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d) { if (c >= '\u0300' && c <= '\u036f') continue; sb.Append(c == 'đ' ? 'd' : c == 'Đ' ? 'D' : c); }
        return JsText.JsTrim(Regex.Replace(sb.ToString().ToLowerInvariant(), @"\s+", " "));
    }

    /// <summary>num: "" / "." / "-" → null; "(100)" = −100; dấu phẩy = nghìn; "30." = 30; không phải số → NaN.</summary>
    public static double? Num(string? v)
    {
        var s = JsText.JsTrim(v ?? "");
        if (s is "" or "." or "-") return null;
        var neg = false;
        if (Regex.IsMatch(s, @"^\(.*\)$")) { neg = true; s = s[1..^1]; }
        s = Regex.Replace(s, @"\s", "").Replace(",", "");
        if (Regex.IsMatch(s, @"\d\.$")) s = s[..^1];
        if (!Regex.IsMatch(s, @"^[-+]?\d*\.?\d+(e[-+]?\d+)?$", RegexOptions.IgnoreCase)) return double.NaN;
        var n = JsText.JsNumber(s);
        return neg ? -n : n;
    }

    public static string Fmt(double? n, int? d = null) => n is not double x || double.IsNaN(x) ? "" : JsText.En(x, d ?? 0, d ?? 2);

    private static int ColIdx(IReadOnlyList<string> headers, string name) { var t = NoAcc(name); for (var i = 0; i < headers.Count; i++) if (NoAcc(headers[i]) == t) return i; return -1; }

    private static void Level(Dictionary<int, string> map, int row, string lv) { if (!map.TryGetValue(row, out var cur) || (lv == "err" && cur != "err")) map[row] = lv; }

    private static Check Finish(Check R, Dictionary<int, string> levels)
    {
        R.Err = levels.Values.Count(x => x == "err"); R.Warn = levels.Values.Count(x => x == "warn");
        R.Ok = Math.Max(0, R.Total - R.Warn - R.Err);
        R.Problems = AnAnData.StableSort(R.Problems, (a, b) => a.Level == b.Level ? a.Row - b.Row : a.Level == "err" ? -1 : 1);
        return R;
    }

    /// <summary>checkM1 — Tồn kho An An: tiêu đề + dòng (đã bỏ dòng Tồn cuối trống).</summary>
    public static Check CheckM1(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, int headerRowNumber, int dropped, IReadOnlyList<int>? rowNums)
    {
        var R = new Check(); var lv = new Dictionary<int, string>(); var req = AnAnData.RequiredColumns;
        var found = req.Count(c => ColIdx(headers, c) >= 0); R.Total = rows.Count;
        int iMa = ColIdx(headers, "Mã hàng"), iTen = ColIdx(headers, "Tên hàng"), iTc = ColIdx(headers, "Tồn cuối"), iNet = ColIdx(headers, "Tồn cuối (NET)"), iHd = ColIdx(headers, "Hợp đồng"), iDt = ColIdx(headers, "Đặc tính");
        R.Infos.Add($"Dòng tiêu đề nằm ở dòng {headerRowNumber} · khớp {found}/{req.Length} cột chuẩn.");
        if (rows.Count == 0) R.Errors.Add("Không có dòng dữ liệu nào (sau khi bỏ các dòng \"Tồn cuối\" trống). Kiểm tra lại file / sheet.");
        if (found < Math.Min(6, req.Length)) R.Errors.Add($"File không giống báo cáo tồn kho (chỉ khớp {found}/{req.Length} cột chuẩn) — có thể chọn nhầm file hoặc sai sheet.");
        foreach (var c in new[] { "Mã hàng", "Tồn cuối" }) if (ColIdx(headers, c) < 0) R.Errors.Add($"Thiếu cột bắt buộc \"{c}\".");
        if (iTen < 0) R.Warns.Add("Thiếu cột \"Tên hàng\".");
        if (iNet < 0) R.Warns.Add("Thiếu cột \"Tồn cuối (NET)\" — tổng kg sẽ không tính được.");
        if (dropped > 0) R.Infos.Add($"Đã bỏ {Fmt(dropped)} dòng hết tồn (cột \"Tồn cuối\" trống).");
        var seen = new Dictionary<string, int>(); double kg = 0; var items = new HashSet<string>();
        string C(string[] r, int i) => i >= 0 && i < r.Length ? r[i] ?? "" : "";
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var n = rowNums is not null && i < rowNums.Count ? rowNums[i] : 1 + i;
            var ma = iMa >= 0 ? JsText.JsTrim(C(r, iMa)) : "";
            if (iMa >= 0) { if (ma.Length == 0) { R.Problems.Add(new(n, "warn", "Mã hàng để trống")); Level(lv, n, "warn"); } else items.Add(ma); }
            if (iTc >= 0)
            {
                var t = Num(C(r, iTc));
                if (t is double x && double.IsNaN(x)) { R.Problems.Add(new(n, "err", $"Tồn cuối không phải số: \"{C(r, iTc)}\"")); Level(lv, n, "err"); }
                else if (t is double y && y < 0) { R.Problems.Add(new(n, "warn", "Tồn cuối âm: " + JsText.JsStr(y))); Level(lv, n, "warn"); }
            }
            if (iNet >= 0)
            {
                var t = Num(C(r, iNet));
                if (t is double x && double.IsNaN(x)) { R.Problems.Add(new(n, "err", $"Tồn cuối (NET) không phải số: \"{C(r, iNet)}\"")); Level(lv, n, "err"); }
                else if (t is double y) kg += y;
            }
            var key = string.Join("|", ma, iHd >= 0 ? C(r, iHd) : "", iDt >= 0 ? C(r, iDt) : "").ToLowerInvariant();
            if (ma.Length > 0) { if (seen.TryGetValue(key, out var first)) { R.Problems.Add(new(n, "warn", "Trùng mã hàng + hợp đồng + đặc tính với dòng " + first)); Level(lv, n, "warn"); } else seen[key] = n; }
        }
        R.Kg = kg; R.Items = items.Count;
        return Finish(R, lv);
    }

    /// <summary>checkM2 — Tồn kho gửi: kiểm tra từng dòng đã lọc (số kiện / trọng lượng là số, ngày nhập kho không ở tương lai, trùng khóa).</summary>
    public static Check CheckM2(IReadOnlyList<(string MaHang, string SoLo, string PhieuNhap, string Size, string QuyCach, string Ngay, string? SoKien, string? TrongLuong)> rows, string? todayIso)
    {
        var R = new Check { Total = rows.Count }; var lv = new Dictionary<int, string>();
        if (rows.Count == 0) R.Errors.Add("Không có dòng nào để nạp (chưa có \"Dữ liệu đã lọc\" khớp mẫu).");
        var seen = new Dictionary<string, int>(); var items = new HashSet<string>(); double kg = 0, kien = 0; var zero = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var n = i + 1; var ma = JsText.JsTrim(r.MaHang);
            if (ma.Length == 0) { R.Problems.Add(new(n, "warn", "Mã hàng để trống")); Level(lv, n, "warn"); } else items.Add(ma);
            var sk = Num(r.SoKien); var tl = Num(r.TrongLuong);
            if (sk is double a && double.IsNaN(a)) { R.Problems.Add(new(n, "err", $"Số kiện không phải số: \"{r.SoKien}\"")); Level(lv, n, "err"); }
            else if (sk is double b && b < 0) { R.Problems.Add(new(n, "warn", "Số kiện âm: " + JsText.JsStr(b))); Level(lv, n, "warn"); }
            else if (sk is double c) kien += c;
            if (tl is double a2 && double.IsNaN(a2)) { R.Problems.Add(new(n, "err", $"Trọng lượng không phải số: \"{r.TrongLuong}\"")); Level(lv, n, "err"); }
            else if (tl is double t2) { kg += t2; if (t2 < 0) { R.Problems.Add(new(n, "warn", "Trọng lượng âm: " + JsText.JsStr(t2))); Level(lv, n, "warn"); } }
            if ((sk is null or 0) && (tl is null or 0)) zero++;
            var d = JsText.JsTrim(r.Ngay);
            if (d.Length > 0 && !string.IsNullOrEmpty(todayIso))
            {
                string? iso = null; var m = Regex.Match(d, @"^(\d{4})-(\d{2})-(\d{2})");
                if (m.Success) iso = $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}";
                else if ((m = Regex.Match(d, @"^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})")).Success) iso = $"{m.Groups[3].Value}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[1].Value.PadLeft(2, '0')}";
                if (iso is not null && string.CompareOrdinal(iso, todayIso) > 0) { R.Problems.Add(new(n, "warn", "Ngày nhập kho ở tương lai: " + d)); Level(lv, n, "warn"); }
            }
            var key = string.Join("|", new[] { ma, r.SoLo, r.PhieuNhap, r.Size, r.QuyCach, r.Ngay }.Select(x => (x ?? "").ToLowerInvariant()));
            if (ma.Length > 0) { if (seen.TryGetValue(key, out var first)) { R.Problems.Add(new(n, "warn", $"Trùng mã hàng + số lô + phiếu nhập với dòng {first}")); Level(lv, n, "warn"); } else seen[key] = n; }
        }
        if (zero > 0) R.Infos.Add(Fmt(zero) + " dòng đã hết tồn cuối kỳ (0 kiện / 0 kg) — bình thường, vẫn được nạp.");
        R.Kg = kg; R.Kien = kien; R.Items = items.Count; return Finish(R, lv);
    }

    public sealed record Snapshot(int Rows, int Items, double Kg, string Date);
    public sealed record CmpLine(string Label, double Cur, double Next, double? Pct);

    /// <summary>compare: cảnh báo số dòng giảm ≥ 40%, tổng kg đổi ≥ 50%, trùng ngày số liệu.</summary>
    public static (List<CmpLine> Lines, List<string> Warns) Compare(Snapshot? cur, Snapshot next)
    {
        var lines = new List<CmpLine>(); var warns = new List<string>();
        if (cur is null || cur.Rows <= 0) return (lines, warns);
        static double? Pct(double a, double b) => a > 0 ? JsText.JsRound((b - a) / a * 1000) / 10 : null;
        var dRows = Pct(cur.Rows, next.Rows); var dKg = Pct(cur.Kg, next.Kg);
        lines.Add(new("Số dòng", cur.Rows, next.Rows, dRows)); lines.Add(new("Số mã hàng", cur.Items, next.Items, Pct(cur.Items, next.Items))); lines.Add(new("Tổng số liệu (kg)", cur.Kg, next.Kg, dKg));
        if (dRows is double dr && dr <= -40) warns.Add($"Số dòng GIẢM {JsText.JsStr(Math.Abs(dr))}% so với dữ liệu đang có ({Fmt(cur.Rows)} → {Fmt(next.Rows)}) — kiểm tra có chọn nhầm file / sheet không.");
        if (dKg is double dk && Math.Abs(dk) >= 50) warns.Add($"Tổng số liệu {(dk > 0 ? "TĂNG " : "GIẢM ")}{JsText.JsStr(Math.Abs(dk))}% so với dữ liệu đang có ({Fmt(cur.Kg, 0)} → {Fmt(next.Kg, 0)} kg) — bất thường, nên kiểm tra trước khi nạp.");
        if (cur.Date.Length > 0 && next.Date.Length > 0 && cur.Date == next.Date) warns.Add($"Ngày số liệu trùng với dữ liệu đang có ({next.Date}) — nạp file này sẽ THAY THẾ số liệu cùng ngày.");
        return (lines, warns);
    }

    // ------------------------------------------------------------------ nhật ký

    public static string S(JsonNode? n) => AnAnStore.S(n);

    public static List<JsonObject> ReadLog(Modules.ISheetSource src, string db)
    {
        try { return (JsonNode.Parse(src.Read(db, LogSheet)) as JsonArray ?? new()).OfType<JsonObject>().Where(x => S(x["id"]).Length > 0).Select(x => (JsonObject)x.DeepClone()).ToList(); }
        catch { return new(); }
    }

    public static JsonArray LogSheetRows(List<JsonObject> log) => new(log.Skip(Math.Max(0, log.Count - MaxLog)).Select(x => (JsonNode)x.DeepClone()).ToArray());

    private static int CmpTime(JsonObject a, JsonObject b) => string.CompareOrdinal(S(b["time"]), S(a["time"]));

    /// <summary>findDuplicate: lần nạp (chưa hoàn tác) cùng dấu vân tay file, hoặc cùng ngày số liệu bằng file khác.</summary>
    public static (JsonObject? SameFile, JsonObject? SameDate) FindDuplicate(List<JsonObject> log, string sha, string dataDateIso)
    {
        var live = AnAnData.StableSort(log.Where(x => S(x["status"]) != "undone").ToList(), CmpTime);
        var same = sha.Length > 0 ? live.FirstOrDefault(x => S(x["sha"]) == sha) : null;
        var day = dataDateIso.Length > 0 ? live.FirstOrDefault(x => S(x["dataDate"]).Length > 0 && S(x["dataDate"]) == dataDateIso && (same is null || S(x["id"]) != S(same["id"]))) : null;
        return (same, day);
    }

    public static string NewId(DateTime? now = null)
    {
        var d = now ?? DateTime.Now;
        const string abc = "abcdefghijklmnopqrstuvwxyz0123456789";
        var r = string.Concat(Enumerable.Range(0, 3).Select(_ => abc[RandomNumberGenerator.GetInt32(abc.Length)]));
        return "n" + d.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + r;
    }

    public static string Sha256(byte[] buf) => Convert.ToHexString(SHA256.HashData(buf)).ToLowerInvariant();

    public static string When(string? iso)
    {
        if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)) return "";
        return t.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    public static string IsoNow() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Ghi đợt nạp mới: các đợt "applied" cũ → "replaced", thêm đợt mới "applied".</summary>
    public static List<JsonObject> AddEntry(List<JsonObject> log, JsonObject entry)
    {
        foreach (var x in log) if (S(x["status"]) == "applied") x["status"] = "replaced";
        log.Add(entry); return log;
    }

    /// <summary>Hoàn tác: đợt đó → "undone", đợt "replaced" ngay trước nó trở lại "applied".</summary>
    public static void MarkUndone(List<JsonObject> log, string id, string user)
    {
        var cur = log.FirstOrDefault(q => S(q["id"]) == id);
        if (cur is not null) { cur["status"] = "undone"; cur["undoneAt"] = IsoNow(); cur["undoneBy"] = user; }
        var t = S(cur?["time"]);
        var prev = AnAnData.StableSort(log.Where(q => S(q["status"]) == "replaced" && string.CompareOrdinal(S(q["time"]), t) < 0).ToList(), CmpTime).FirstOrDefault();
        if (prev is not null) prev["status"] = "applied";
    }

    public static JsonObject? LastApplied(List<JsonObject> log) => AnAnData.StableSort(log.Where(x => S(x["status"]) == "applied").ToList(), CmpTime).FirstOrDefault();
}

/// <summary>
/// Bản hoàn tác của 1 đợt nạp, lưu TRÊN MÁY ĐÃ NẠP (giống napAPI của app cũ): thư mục …/KhoLanhCS/nap/{module}/{id}/
/// before.json (dữ liệu các sheet bị thay, trước khi nạp) + bản gốc file Excel. Tự dọn bản quá hạn (mặc định 30 ngày).
/// </summary>
public sealed class NapUndoStore
{
    private readonly string _root;
    public NapUndoStore(string root) { _root = root; }
    public static NapUndoStore Default() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KhoLanhCS", "nap"));

    private string Dir(string module, string id) => Path.Combine(_root, Safe(module), Safe(id));
    private static string Safe(string s) => Regex.Replace(s ?? "", @"[^A-Za-z0-9_.-]", "_");

    public void Save(string module, string id, IDictionary<string, string> beforeSheets, byte[]? file, string fileName)
    {
        var d = Dir(module, id); Directory.CreateDirectory(d);
        var o = new JsonObject();
        foreach (var (k, v) in beforeSheets) { JsonNode? n; try { n = JsonNode.Parse(v); } catch { n = new JsonArray(); } o[k] = n; }
        File.WriteAllText(Path.Combine(d, "before.json"), o.ToJsonString(AnAnStore.Opt));
        if (file is not null) File.WriteAllBytes(Path.Combine(d, "goc_" + Regex.Replace(fileName, @"[\\/:*?""<>|]", "_")), file);
    }

    public bool Has(string module, string id) => File.Exists(Path.Combine(Dir(module, id), "before.json"));

    public Dictionary<string, JsonArray?> Before(string module, string id)
    {
        var p = Path.Combine(Dir(module, id), "before.json");
        if (!File.Exists(p)) throw new Services.BusinessException("Không còn bản hoàn tác của đợt nạp này trên máy này.");
        var o = JsonNode.Parse(File.ReadAllText(p)) as JsonObject ?? new();
        return o.ToDictionary(kv => kv.Key, kv => kv.Value?.DeepClone() as JsonArray ?? new JsonArray());
    }

    public void Purge(int days)
    {
        try
        {
            if (!Directory.Exists(_root)) return;
            var lim = DateTime.Now.AddDays(-days);
            foreach (var m in Directory.GetDirectories(_root))
                foreach (var d in Directory.GetDirectories(m))
                    if (Directory.GetLastWriteTime(d) < lim) Directory.Delete(d, true);
        }
        catch { /* dọn không được thì để lần sau */ }
    }

    public string Root => _root;
}

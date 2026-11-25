using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.TongHop;

/// <summary>
/// Module TỔNG HỢP (viết lại bằng C# theo tong-hop.js của app cũ). Dữ liệu giữ ĐÚNG định dạng sheet của app cũ
/// (database "tong-hop": M8_Meta, MaHoa, MaDaTao, TongHop, TongHop_Meta, AnhXaTen, AnhXaCauHinh) vì hai module
/// Tồn kho An An / Tồn kho gửi (WebView2) vẫn đọc các sheet này.
///  - Mã hóa sản phẩm: mã hàng 21 ký tự = 10 tổ hợp; mỗi tổ hợp có danh sách Mã → Tên.
///  - Danh sách mã hàng: mã đã tạo + diễn giải (ghép tự động từ tên các tổ hợp) + đủ mã.
///  - Dữ liệu tổng hợp: Bảng 1 nạp từ Excel, Bảng 2 gộp theo Ngày + Tên sản phẩm + Size, cộng các cột số.
///  - Ánh xạ tên hàng: <see cref="AnhXa"/>.
/// </summary>
public sealed class TongHopService
{
    public const string Db = "tong-hop", PermKey = "m3";
    public const string SeedVersion = "2026-10-23-grd-catelogy";

    public sealed record SegDef(int Seg, int Length, string Range, string Name);
    public static readonly SegDef[] Segs =
    {
        new(1, 4, "Ký tự 1–4", "Tên sản phẩm"), new(2, 1, "Ký tự 5", "Cấp đông"), new(3, 1, "Ký tự 6", "Đóng gói"),
        new(4, 1, "Ký tự 7", "Hóa chất/Phụ gia"), new(5, 1, "Ký tự 8", "Thị trường"), new(6, 2, "Ký tự 9–10", "Khách hàng"),
        new(7, 3, "Ký tự 11–13", "Mạ băng"), new(8, 4, "Ký tự 14–17", "Size"), new(9, 2, "Ký tự 18–19", "Trọng lượng"),
        new(10, 2, "Ký tự 20–21", "Quy cách đóng thùng"),
    };
    public const int CodeLength = 21;
    public static int Offset(int seg) => Segs.TakeWhile(s => s.Seg != seg).Sum(s => s.Length);

    public sealed class Entry { public string Code = ""; public string Name = ""; public Entry() { } public Entry(string c, string n) { Code = c; Name = n; } }
    public sealed class Result { public int Stt; public string Code = ""; public string Note = ""; public bool Complete; }

    /// <summary>Bảng 1 (Dữ liệu tổng hợp): tiêu đề + dòng (giữ giá trị JSON gốc: số / chuỗi).</summary>
    public sealed class Table
    {
        public List<string> Headers = new();
        public string CodeCol = "", DateCol = "";
        public List<Dictionary<string, JsonNode?>> Rows = new();
    }

    public Dictionary<int, List<Entry>> SegmentData { get; private set; } = Segs.ToDictionary(s => s.Seg, _ => new List<Entry>());
    public List<Result> Results { get; set; } = new();
    // 1.95: danh sách mã hàng (MaDaTao) nay sửa ở Danh mục mã hàng — Tổng hợp chỉ ghi MaDaTao khi chính nó đổi danh sách
    // (trước luôn ghi lại bản trong bộ nhớ → lưu Mã hóa sản phẩm có thể xóa mất mã vừa thêm ở Danh mục / máy khác).
    private string _resSig = "";
    private static string SigOf(List<Result> rs) => string.Join("\n", rs.Select(r => r.Code + "\t" + r.Note));
    /// <summary>Danh mục mã hàng vừa ghi MaDaTao → nhận bản mới làm bản đã lưu (không ghi lại).</summary>
    public void AcceptResults(List<Result> rs) { Results = rs; _resSig = SigOf(rs); }
    public Table Tab2 { get; set; } = new();
    public AnhXa AnhXa { get; set; } = AnhXa.Defaults();

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly SettingsService _settings;

    public TongHopService(Func<ISheetSource> source, Session session, AuditService audit, SettingsService settings)
    { _src = source; _session = session; _audit = audit; _settings = settings; }

    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void Require(string level)
    {
        if (!_session.Can(PermKey, level)) throw new BusinessException(level == Perm.Edit ? "Tài khoản không có quyền sửa Tổng hợp." : "Tài khoản không có quyền xem Tổng hợp.");
    }

    public static string Norm(string? code) => (code ?? "").Trim().ToLowerInvariant();
    private static string S(JsonNode? n) => n switch { null => "", JsonValue v when v.TryGetValue<string>(out var s) => s, JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture), JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false", _ => n.ToString() };
    private const string JsonTag = "__JSON__";
    private static JsonObject? MetaRow(JsonArray rows)
    {
        var cell = rows.FirstOrDefault()?["data"] is JsonNode d ? S(d) : "";
        if (!cell.StartsWith(JsonTag)) return null;
        try { return JsonNode.Parse(cell[JsonTag.Length..]) as JsonObject; } catch { return null; }
    }
    private static JsonArray MakeMeta(JsonObject o) => new(new JsonObject { ["data"] = JsonTag + o.ToJsonString() });
    private JsonArray Read(string sheet) => JsonNode.Parse(_src().Read(Db, sheet)) as JsonArray ?? new JsonArray();

    // ------------------------------------------------------------------ đọc / ghi

    /// <summary>Đọc toàn bộ dữ liệu; lần đầu theo phiên bản danh sách chuẩn → gộp danh sách chuẩn + mã hàng mặc định rồi lưu.</summary>
    public void Load()
    {
        Require(Perm.View);
        var seg = Segs.ToDictionary(s => s.Seg, _ => new List<Entry>());
        foreach (var r in Read("MaHoa").OfType<JsonObject>())
            if (int.TryParse(S(r["Tổ hợp"]), out var k) && seg.ContainsKey(k)) seg[k].Add(new Entry(S(r["Mã"]), S(r["Tên"])));
        SegmentData = seg;
        Results = Read("MaDaTao").OfType<JsonObject>().Select((r, i) => new Result
        {
            Stt = int.TryParse(S(r["STT"]), out var st) ? st : i + 1, Code = S(r["Mã"]), Note = S(r["Ghi chú"]),
            Complete = S(r["Đủ mã"]) is "1" or "true",
        }).ToList();
        _resSig = SigOf(Results);
        var thMeta = MetaRow(Read("TongHop_Meta")) ?? new JsonObject();
        var t = new Table
        {
            Headers = (thMeta["headers"] as JsonArray ?? new JsonArray()).Select(S).ToList(),
            CodeCol = S(thMeta["codeCol"]), DateCol = S(thMeta["dateCol"]),
        };
        var extra = (thMeta["extra"] as JsonArray ?? new JsonArray()).Select(S).ToList();
        foreach (var r in Read("TongHop").OfType<JsonObject>())
        {
            var o = new Dictionary<string, JsonNode?>();
            for (var i = 0; i < t.Headers.Count; i++) o[t.Headers[i]] = r["c" + i]?.DeepClone();
            for (var i = 0; i < extra.Count; i++) o[extra[i]] = r["e" + i]?.DeepClone();
            t.Rows.Add(o);
        }
        Tab2 = t;
        var ax = AnhXa.FromSheets(Read("AnhXaTen"), Read("AnhXaCauHinh"));
        var seedAx = ax is null;
        AnhXa = ax ?? AnhXa.Defaults();

        if (!CanEdit) { RefreshNotes(); return; }
        var changed = seedAx;
        var overwrite = _settings.Get("th_seed_ver") != SeedVersion;
        foreach (var (k, list) in FixedCatalog()) changed |= ApplyFixed(k, list, overwrite);
        if (overwrite) _settings.Set("th_seed_ver", SeedVersion);
        var def = DefaultCodes();
        if (_settings.Get("th_mahang_ver") != def.Version) { changed |= AddDefaultCodes() > 0; _settings.Set("th_mahang_ver", def.Version); }
        RefreshNotes();
        if (changed) Save();
    }

    /// <summary>Ghi mọi sheet (giống saveState(true) của app cũ).</summary>
    public void Save()
    {
        Require(Perm.Edit);
        var maHoa = new JsonArray();
        foreach (var s in Segs)
            foreach (var e in SegmentData[s.Seg]) maHoa.Add(new JsonObject { ["Tổ hợp"] = s.Seg, ["Mã"] = e.Code, ["Tên"] = e.Name });
        var maDaTao = new JsonArray(Results.Select((r, i) => (JsonNode)new JsonObject
        {
            ["STT"] = r.Stt > 0 ? r.Stt : i + 1, ["Mã"] = r.Code, ["Ghi chú"] = r.Note, ["Đủ mã"] = r.Complete ? 1 : 0,
        }).ToArray());
        var sheets = new Dictionary<string, JsonArray?>
        {
            ["M8_Meta"] = MakeMeta(new JsonObject { ["savedAt"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(), ["version"] = 2 }),
            ["MaHoa"] = maHoa,
        };
        if (SigOf(Results) != _resSig) { sheets["MaDaTao"] = maDaTao; _resSig = SigOf(Results); }
        foreach (var (k, v) in EncodeTab2()) sheets[k] = v;
        foreach (var (k, v) in AnhXa.ToSheets()) sheets[k] = v;
        _src().Write(Db, sheets);
    }

    private Dictionary<string, JsonArray?> EncodeTab2()
    {
        var t = Tab2;
        var inHead = new HashSet<string>(t.Headers);
        var extra = new List<string>();
        foreach (var r in t.Rows) foreach (var k in r.Keys) if (!inHead.Contains(k) && !extra.Contains(k)) extra.Add(k);
        var rows = new JsonArray();
        foreach (var r in t.Rows)
        {
            var o = new JsonObject();
            for (var i = 0; i < t.Headers.Count; i++) o["c" + i] = r.TryGetValue(t.Headers[i], out var v) && v is not null ? v.DeepClone() : "";
            for (var i = 0; i < extra.Count; i++) o["e" + i] = r.TryGetValue(extra[i], out var v) && v is not null ? v.DeepClone() : "";
            rows.Add(o);
        }
        var meta = new JsonObject
        {
            ["headers"] = new JsonArray(t.Headers.Select(h => (JsonNode)h).ToArray()), ["extra"] = new JsonArray(extra.Select(h => (JsonNode)h).ToArray()),
            ["codeCol"] = t.CodeCol, ["dateCol"] = t.DateCol,
        };
        return new() { ["TongHop"] = rows, ["TongHop_Meta"] = MakeMeta(meta) };
    }

    // ------------------------------------------------------------------ danh sách chuẩn

    private static Dictionary<int, List<Entry>>? _fixed;
    public static Dictionary<int, List<Entry>> FixedCatalog()
    {
        if (_fixed is not null) return _fixed;
        using var s = typeof(TongHopService).Assembly.GetManifestResourceStream("KhoLanh.Core.TongHop.ma-hoa-chuan.json")!;
        var o = JsonNode.Parse(s)!.AsObject();
        return _fixed = o.ToDictionary(p => int.Parse(p.Key), p => p.Value!.AsArray().Select(x => new Entry(S(x![0]), S(x[1]))).ToList());
    }

    public record DefaultList(string Version, List<(string Code, string Note)> Rows);
    private static DefaultList? _def;
    public static DefaultList DefaultCodes()
    {
        if (_def is not null) return _def;
        using var s = typeof(TongHopService).Assembly.GetManifestResourceStream("KhoLanh.Core.TongHop.ma-hang-mac-dinh.json.gz")!;
        using var gz = new GZipStream(s, CompressionMode.Decompress);
        var o = JsonNode.Parse(gz)!.AsObject();
        return _def = new DefaultList(S(o["version"]), o["rows"]!.AsArray().Select(x => (S(x![0]), S(x[1]))).ToList());
    }

    /// <summary>Gộp danh sách chuẩn vào tổ hợp: thêm mã thiếu; <paramref name="overwrite"/> = sửa tên theo chuẩn. Mã người dùng tự thêm giữ nguyên.</summary>
    public bool ApplyFixed(int seg, List<Entry> fixedList, bool overwrite)
    {
        var list = SegmentData[seg];
        var changed = false;
        if (seg == 1)
        {
            var stale = list.FindIndex(e => Norm(e.Code) == "vrpd" && e.Name == "PD");
            if (stale >= 0) { list.RemoveAt(stale); changed = true; }
        }
        var byCode = new Dictionary<string, int>();
        for (var i = 0; i < list.Count; i++) byCode.TryAdd(Norm(list[i].Code), i);
        foreach (var f in fixedList)
        {
            var n = Norm(f.Code);
            if (byCode.TryGetValue(n, out var i))
            {
                if (overwrite && list[i].Name != f.Name) { list[i] = new Entry(list[i].Code, f.Name); changed = true; }
            }
            else { list.Add(new Entry(f.Code, f.Name)); byCode[n] = list.Count - 1; changed = true; }
        }
        return changed;
    }

    public int AddDefaultCodes()
    {
        var have = new HashSet<string>(Results.Select(r => Norm(r.Code)));
        var added = 0;
        foreach (var (code, note) in DefaultCodes().Rows)
        {
            if (!have.Add(Norm(code))) continue;
            Results.Add(new Result { Stt = Results.Count + 1, Code = code, Note = note, Complete = code.Length == CodeLength });
            added++;
        }
        return added;
    }

    // ------------------------------------------------------------------ mã hàng

    public Entry? Find(int seg, string piece) { var n = Norm(piece); return SegmentData[seg].FirstOrDefault(e => Norm(e.Code) == n); }

    /// <summary>Tách mã 21 ký tự thành 10 đoạn (thiếu ký tự → đoạn rỗng).</summary>
    public static string[] Split(string code)
    {
        var parts = new string[Segs.Length]; var pos = 0;
        for (var i = 0; i < Segs.Length; i++)
        {
            var len = Segs[i].Length;
            parts[i] = code.Length >= pos + len ? code.Substring(pos, len) : "";
            pos += len;
        }
        return parts;
    }

    /// <summary>Diễn giải ghép từ tên các tổ hợp (giống buildNoteFromCode).</summary>
    public string BuildNote(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != CodeLength) return "";
        var parts = new List<string>(); var pos = 0;
        foreach (var d in Segs)
        {
            var m = Find(d.Seg, code.Substring(pos, d.Length));
            pos += d.Length;
            if (m is not null && m.Name.Length > 0) parts.Add(m.Name);
        }
        return string.Join(" • ", parts);
    }

    /// <summary>Điền diễn giải cho dòng chưa có. True nếu có đổi.</summary>
    public bool RefreshNotes()
    {
        var changed = false;
        foreach (var r in Results)
        {
            if (!string.IsNullOrWhiteSpace(r.Note)) continue;
            var n = BuildNote(r.Code);
            if (n.Length > 0) { r.Note = n; changed = true; }
        }
        return changed;
    }

    /// <summary>Đoạn nào trong mã chưa khai báo ở Mã hóa sản phẩm (để tô màu / báo lỗi).</summary>
    public List<int> UnknownSegments(string code)
    {
        var bad = new List<int>();
        var parts = Split(code);
        for (var i = 0; i < Segs.Length; i++) if (parts[i].Length == 0 || Find(Segs[i].Seg, parts[i]) is null) bad.Add(Segs[i].Seg);
        return bad;
    }

    public static string CleanNote(string? s)
    {
        s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        s = Regex.Replace(s, @"^,\s*", "");
        s = Regex.Replace(s, @",(\s*,)+", ",");
        s = Regex.Replace(s, @",\s*(\([^()]*\))$", " $1");
        s = Regex.Replace(s, @",\s*$", "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    public void Renumber() { for (var i = 0; i < Results.Count; i++) Results[i].Stt = i + 1; }

    /// <summary>Xóa mã trùng (giữ dòng đầu). Trả về số dòng đã xóa.</summary>
    public int RemoveDuplicates()
    {
        var seen = new HashSet<string>(); var before = Results.Count;
        Results = Results.Where(r => r.Code.Trim().Length == 0 || seen.Add(Norm(r.Code))).ToList();
        Renumber();
        return before - Results.Count;
    }

    // ------------------------------------------------------------------ Dữ liệu tổng hợp (Bảng 1 → Bảng 2)

    public static string NormSearch(string? s)
    {
        var t = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in t) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant().Trim();
    }

    public static string FindCodeColumn(List<string> headers)
    {
        var i = headers.FindIndex(h => { var n = NormSearch(h); return n is "ma hang" or "mahang" or "code" || n.Contains("ma hang"); });
        return i >= 0 ? headers[i] : headers.FirstOrDefault() ?? "";
    }

    public static string FindDateColumn(List<string> headers)
    {
        var i = headers.FindIndex(h => { var n = NormSearch(h); return n == "ngay" || n.Contains("ngay") || n == "date" || n.Contains("date"); });
        return i >= 0 ? headers[i] : "";
    }

    /// <summary>Số kiểu Việt Nam / quốc tế (giống parseVNNumber).</summary>
    public static double? ParseNumber(JsonNode? v)
    {
        if (v is null) return null;
        if (v is JsonValue jv && jv.TryGetValue<double>(out var d)) return double.IsNaN(d) ? null : d;
        var s = Regex.Replace(S(v).Trim(), @"\s", "");
        if (s.Length == 0) return null;
        int lc = s.LastIndexOf(','), ld = s.LastIndexOf('.');
        if (lc >= 0 && ld >= 0) s = lc > ld ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (lc >= 0) { var dec = s.Length - lc - 1; s = dec is > 0 and <= 2 ? s.Replace(',', '.') : s.Replace(",", ""); }
        var m = Regex.Match(s, @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null;
    }

    /// <summary>Ngày → dd/MM/yyyy (số serial Excel, ISO, hoặc giữ nguyên chuỗi).</summary>
    public static string FormatDate(JsonNode? v)
    {
        if (v is null) return "";
        if (v is JsonValue jv && jv.TryGetValue<double>(out var serial)) return FromSerial(serial);
        var s = S(v).Trim();
        if (s.Length == 0) return "";
        var iso = Regex.Match(s, @"^(\d{4})-(\d{2})-(\d{2})");
        if (iso.Success) return $"{iso.Groups[3].Value}/{iso.Groups[2].Value}/{iso.Groups[1].Value}";
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return FromSerial(n);
        return s;
    }

    private static string FromSerial(double serial)
    {
        try { return DateTime.FromOADate(Math.Floor(serial)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture); }
        catch { return serial.ToString(CultureInfo.InvariantCulture); }
    }

    public string SegmentLabel(string code, int seg, string missing)
    {
        var off = Offset(seg); var len = Segs[seg - 1].Length;
        if (code.Length < off + len) return missing;
        var piece = code.Substring(off, len);
        var m = Find(seg, piece);
        return m is not null && m.Name.Length > 0 ? m.Name : $"{piece} (chưa khai báo trong Menu {Segs[seg - 1].Name})";
    }

    public sealed class Group
    {
        public string Date = "", Product = "", Size = "";
        public int Count;
        public Dictionary<string, double> Sums = new();
    }

    /// <summary>Gộp Bảng 1 theo Ngày (nếu có) + Tên sản phẩm + Size, cộng các cột số (giống computeTab2Groups).</summary>
    public (List<string> numericCols, List<Group> groups, bool hasDate) ComputeGroups()
    {
        var t = Tab2;
        var hasDate = t.DateCol.Length > 0;
        var numeric = t.Headers.Where(h => h != t.CodeCol && h != t.DateCol && t.Rows.Any(r => ParseNumber(r.GetValueOrDefault(h)) is not null)).ToList();
        var groups = new Dictionary<string, Group>(); var order = new List<Group>();
        foreach (var r in t.Rows)
        {
            var code = S(r.GetValueOrDefault(t.CodeCol)).Trim();
            var date = hasDate ? (FormatDate(r.GetValueOrDefault(t.DateCol)) is { Length: > 0 } dd ? dd : "(Không có ngày)") : "";
            var product = SegmentLabel(code, 1, "(Mã hàng thiếu ký tự Tên sản phẩm)");
            var size = SegmentLabel(code, 8, "(Mã hàng thiếu ký tự Size)");
            var key = date + "\u0001" + product + "\u0001" + size;
            if (!groups.TryGetValue(key, out var g))
            {
                g = new Group { Date = date, Product = product, Size = size };
                foreach (var c in numeric) g.Sums[c] = 0;
                groups[key] = g; order.Add(g);
            }
            g.Count++;
            foreach (var c in numeric) if (ParseNumber(r.GetValueOrDefault(c)) is { } v) g.Sums[c] += v;
        }
        if (hasDate)
        {
            // gom theo ngày, giữ thứ tự xuất hiện của ngày
            var dates = order.Select(g => g.Date).Distinct().ToList();
            order = dates.SelectMany(d => order.Where(g => g.Date == d)).ToList();
        }
        return (numeric, order, hasDate);
    }

    /// <summary>Nạp Excel vào Bảng 1 (thay toàn bộ).</summary>
    public static Table ReadTable(string path, string? sheetName = null)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(fs);
            var ws = sheetName is not null && wb.Worksheets.TryGetWorksheet(sheetName, out var w0) ? w0 : wb.Worksheets.First();
            var used = ws.RangeUsed();
            var t = new Table();
            if (used is null) return t;
            int r1 = used.FirstRow().RowNumber(), r2 = used.LastRow().RowNumber(), c2 = used.LastColumn().ColumnNumber();
            var seen = new Dictionary<string, int>();
            for (var c = 1; c <= c2; c++)
            {
                var h = ws.Cell(r1, c).GetFormattedString().Trim();
                if (h.Length == 0) h = "Cột " + c;
                if (seen.TryGetValue(h, out var n)) { seen[h] = n + 1; h = h + " (" + (n + 1) + ")"; } else seen[h] = 1;
                t.Headers.Add(h);
            }
            for (var r = r1 + 1; r <= r2; r++)
            {
                var o = new Dictionary<string, JsonNode?>(); var any = false;
                for (var c = 1; c <= c2; c++)
                {
                    var cell = ws.Cell(r, c);
                    JsonNode? v = cell.IsEmpty() ? "" : cell.DataType switch
                    {
                        XLDataType.Number => JsonValue.Create(cell.GetDouble()),
                        XLDataType.DateTime => JsonValue.Create(cell.GetDateTime().ToOADate()),
                        XLDataType.Boolean => cell.GetBoolean() ? "TRUE" : "FALSE",
                        _ => cell.GetFormattedString(),
                    };
                    if (!cell.IsEmpty()) any = true;
                    o[t.Headers[c - 1]] = v;
                }
                if (any) t.Rows.Add(o);
            }
            t.CodeCol = FindCodeColumn(t.Headers);
            t.DateCol = FindDateColumn(t.Headers);
            return t;
        }
        catch (Exception e) { throw new BusinessException("Không đọc được file Excel: " + e.Message); }
    }

    public static string Cell(Table t, Dictionary<string, JsonNode?> r, string h)
    {
        var v = r.GetValueOrDefault(h);
        if (h == t.DateCol) return FormatDate(v);
        if (v is JsonValue jv && jv.TryGetValue<double>(out var d)) return Math.Round(d, 6).ToString("0.######", CultureInfo.InvariantCulture);
        return S(v);
    }

    public void ExportGroups(string path)
    {
        Require(Perm.Export);
        var (numeric, groups, hasDate) = ComputeGroups();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Bảng 2");
        var head = new List<string> { "STT" };
        if (hasDate) head.Add("Ngày");
        head.AddRange(new[] { "Tên sản phẩm", "Size", "Số dòng" });
        head.AddRange(numeric.Select(c => "Tổng " + c));
        for (var i = 0; i < head.Count; i++) { ws.Cell(1, i + 1).Value = head[i]; ws.Cell(1, i + 1).Style.Font.Bold = true; ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F1"); }
        var row = 2; var stt = 0;
        void Write(Group g, bool total)
        {
            var c = 1;
            ws.Cell(row, c++).Value = total ? "" : (++stt).ToString();
            if (hasDate) ws.Cell(row, c++).Value = g.Date;
            ws.Cell(row, c++).Value = total ? "Tổng ngày " + g.Date : g.Product;
            ws.Cell(row, c++).Value = total ? "" : g.Size;
            ws.Cell(row, c++).Value = g.Count;
            foreach (var n in numeric) ws.Cell(row, c++).Value = Math.Round(g.Sums[n], 6);
            if (total) { ws.Row(row).Style.Font.Bold = true; ws.Range(row, 1, row, head.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF4CC"); }
            row++;
        }
        foreach (var dg in groups.GroupBy(g => g.Date))
        {
            foreach (var g in dg) Write(g, false);
            if (hasDate)
            {
                var t = new Group { Date = dg.Key, Count = dg.Sum(x => x.Count) };
                foreach (var n in numeric) t.Sums[n] = dg.Sum(x => x.Sums[n]);
                Write(t, true);
            }
        }
        ws.Columns().AdjustToContents(8, 60);
        ws.SheetView.FreezeRows(1);
        wb.SaveAs(path);
        _audit.Log(_session.Username, "Tổng hợp: xuất Excel Bảng 2", path);
    }

    /// <summary>Xuất danh sách mã hàng / một tổ hợp ra Excel.</summary>
    public void ExportList(string path, string title, string[] headers, IEnumerable<string[]> rows)
    {
        Require(Perm.Export);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(title.Length > 31 ? title[..31] : title);
        for (var i = 0; i < headers.Length; i++) { ws.Cell(1, i + 1).Value = headers[i]; ws.Cell(1, i + 1).Style.Font.Bold = true; }
        var r = 2;
        foreach (var row in rows) { for (var i = 0; i < row.Length; i++) ws.Cell(r, i + 1).Value = row[i]; r++; }
        ws.Columns().AdjustToContents(8, 80);
        ws.SheetView.FreezeRows(1);
        wb.SaveAs(path);
        _audit.Log(_session.Username, "Tổng hợp: xuất Excel", $"{title} → {path}");
    }

    public void Log(string action, string detail) => _audit.Log(_session.Username, "Tổng hợp: " + action, detail);
}

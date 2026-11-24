using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;

namespace KhoLanh.Core.RaDong;

/// <summary>
/// Module BÁO CÁO RÃ ĐÔNG (viết lại bằng C# theo bao-cao-ra-dong.js + rd-bang2.js + rd-xuat.js của app cũ).
/// Database "bao-cao-ra-dong", giữ ĐÚNG định dạng sheet của app cũ:
///  - RaDong + RaDong_Meta: Bảng 1 (dòng gốc của file vừa nạp, lưu theo vị trí cột c0.., e0..) + số sửa tay của Bảng 2 (meta.edits).
///  - RaDong_DaLuu: Bảng 3 (số liệu đã lưu theo ngày: Ngày · Mã hàng · Size · Số lượng · Net).
///  - RaDong_FileDaNap (vân tay file đã nạp), RaDong_HoanTac (hoàn tác lần cập nhật gần nhất), Tab3_SizeLabel_DaGhiNhan (nhãn Size tự học).
/// Mã hóa sản phẩm + Ánh xạ tên hàng: CHỈ ĐỌC từ database "tong-hop".
/// </summary>
public sealed class RaDongService
{
    public const string Db = "bao-cao-ra-dong", PermKey = "m4", CodePrefix = "TPC-";
    public const string SheetRows = "RaDong", SheetMeta = "RaDong_Meta", SheetSaved = "RaDong_DaLuu", SheetFiles = "RaDong_FileDaNap",
        SheetUndo = "RaDong_HoanTac", SheetSize = "Tab3_SizeLabel_DaGhiNhan";
    private const string JsonTag = "__JSON__";
    private static readonly Dictionary<string, string> SizeDisplayMap = new() { ["1003"] = "100/200" };

    // ------------------------------------------------------------------ kiểu dữ liệu

    public sealed class Edit
    {
        public Dictionary<string, double>? V;
        public string? W, P, S;
        public bool Empty => (V is null || V.Count == 0) && W is null && P is null && S is null;
    }

    /// <summary>Bảng 1: dòng gốc của file đã nạp. Mỗi dòng là {tên cột: giá trị} + "__code" (mã 21 ký tự) + "__ed" (ô đã sửa tay).</summary>
    public sealed class Data
    {
        public List<string> Headers = new();
        public string CodeCol = "";
        public List<Dictionary<string, JsonNode?>> Rows = new();
        public Dictionary<string, Edit> Edits = new();
        public string FileName = "", FileHash = "";
        public long FileSize;
    }

    public sealed class Group
    {
        public string Key = "", Date = "", Product = "", Size = "";
        public string? SizeFixed;
        public int Count, AutoNet;
        public bool Edited;
        public Dictionary<string, double> Sums = new();
        public List<string> WeightValues = new();
        public string SizeText(RaDongService s) => SizeFixed ?? s.FormatSize(Size);
    }

    public sealed class Groups
    {
        public List<string> NumericCols = new();
        public List<string> Order = new();
        public Dictionary<string, Group> ByKey = new();
        public bool HasDate;
        public string DateCol = "", WeightCol = "", NetCol = "";
    }

    public sealed class Saved
    {
        public string Id = NewId(), Date = "", Ma = "", Size = "", Src = "", At = "", EditAt = "";
        public double? Sl;
        public double Net;
        public bool Edited;
        public Saved Clone() => (Saved)MemberwiseClone();
    }

    public sealed record DayRow(string Ma, string Size, double? Sl, double Net);
    public sealed record FileSeen(string Hash, string Name, long Size, string At, int Days);
    public sealed class UndoInfo { public string At = "", File = ""; public List<string> Days = new(); public List<Saved> Before = new(); }

    // ------------------------------------------------------------------ trạng thái

    public Data Bang1 { get; private set; } = new();
    public List<Saved> SavedRows { get; private set; } = new();
    public List<FileSeen> Files { get; private set; } = new();
    public UndoInfo? Undo { get; private set; }
    public Dictionary<string, string> SizeLabels { get; private set; } = new();
    public bool Loaded { get; private set; }

    private Dictionary<string, string> _seg1 = new();
    private AnhXa _ax = AnhXa.Defaults();

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;

    public RaDongService(Func<ISheetSource> source, Session session, AuditService audit) { _src = source; _session = session; _audit = audit; }

    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void Require(string level)
    {
        if (!_session.Can(PermKey, level)) throw new BusinessException(level == Perm.Edit ? "Tài khoản không có quyền sửa Báo cáo rã đông." : "Tài khoản không có quyền xem Báo cáo rã đông.");
    }
    private void RequireWrite()
    {
        Require(Perm.Edit);
        if (!Loaded) throw new BusinessException("Chưa đọc được dữ liệu đã lưu nên tạm KHÓA việc ghi (tránh ghi đè mất số liệu cũ). Bấm \"Tải lại\".");
    }

    // ------------------------------------------------------------------ tiện ích

    public static string NewId() => "t" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString("x") + Guid.NewGuid().ToString("N")[..4];
    public static string S(JsonNode? n) => n switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        _ => n.ToJsonString(),
    };
    public static double Round(double x) => Math.Round(x * 1e6, MidpointRounding.AwayFromZero) / 1e6;
    public static double? Num(JsonNode? v) => TongHopService.ParseNumber(v);
    public static double? Num(string? s) => string.IsNullOrWhiteSpace(s) ? null : TongHopService.ParseNumber(JsonValue.Create(s));
    private static string Norm(string? s) => TongHopService.NormSearch(s);
    public static bool IsoOk(string? s) => s is not null && Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$");
    public static string Dmy(string iso) => IsoOk(iso) ? $"{iso[8..10]}/{iso[5..7]}/{iso[..4]}" : iso;
    public static string DmyToIso(string dmy) { var m = Regex.Match((dmy ?? "").Trim(), @"^(\d{2})/(\d{2})/(\d{4})$"); return m.Success ? $"{m.Groups[3]}-{m.Groups[2]}-{m.Groups[1]}" : ""; }
    /// <summary>1479.6 → "1,479.60" (giống toLocaleString en-US 2 số lẻ của app cũ).</summary>
    public static string Fmt2(double n) => n.ToString("#,##0.00", CultureInfo.InvariantCulture);
    public static string FmtNum(double n) => Math.Round(n, 2).ToString("#,##0.##", CultureInfo.InvariantCulture);
    /// <summary>Số cho ảnh / hiển thị: 1479.6 → "1,479.60" (1.71: 1 kiểu duy nhất cho toàn app — trước là kiểu Việt "1.479,60").</summary>
    public static string FmtNetVn(double n) => Math.Round(n, 2).ToString("#,##0.00", CultureInfo.InvariantCulture);
    public static string FmtSlVn(double? n) => n is null ? "" : Math.Round(n.Value, 2).ToString("#,##0.##", CultureInfo.InvariantCulture);

    private static readonly CompareInfo Vi = CultureInfo.GetCultureInfo("vi-VN").CompareInfo;
    /// <summary>So sánh kiểu Intl.Collator('vi', {numeric:true, sensitivity:'base'}).</summary>
    public static int Natural(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0'); var nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                var c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                int si = i, sj = j;
                while (i < a.Length && !char.IsDigit(a[i])) i++;
                while (j < b.Length && !char.IsDigit(b[j])) j++;
                var c = Vi.Compare(a[si..i], b[sj..j], CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
                if (c != 0) return c;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    private JsonArray Read(string db, string sheet) => JsonNode.Parse(_src().Read(db, sheet)) as JsonArray ?? new JsonArray();
    private static JsonObject? MetaRow(JsonArray rows)
    {
        var cell = rows.FirstOrDefault() is JsonObject o ? S(o["data"]) : "";
        if (!cell.StartsWith(JsonTag)) return null;
        try { return JsonNode.Parse(cell[JsonTag.Length..]) as JsonObject; } catch { return null; }
    }
    private static JsonArray MakeMeta(JsonObject o) => new(new JsonObject { ["data"] = JsonTag + o.ToJsonString() });
    private static JsonNode? EncVal(JsonNode? v) => v switch
    {
        null => "",
        JsonObject or JsonArray => JsonTag + v.ToJsonString(),
        _ => v.DeepClone(),
    };
    private static JsonNode? DecVal(JsonNode? v)
    {
        if (v is JsonValue jv && jv.TryGetValue<string>(out var s) && s.StartsWith(JsonTag))
            try { return JsonNode.Parse(s[JsonTag.Length..]); } catch { return v.DeepClone(); }
        return v?.DeepClone();
    }

    // ------------------------------------------------------------------ đọc / ghi

    /// <summary>Đọc Bảng 1, Bảng 3, nhãn Size, file đã nạp, hoàn tác và ánh xạ (từ Tổng hợp). Lỗi đọc → ném lỗi, KHÔNG cho ghi.</summary>
    public void Load()
    {
        Require(Perm.View);
        Loaded = false;
        // Bảng 1
        var meta = MetaRow(Read(Db, SheetMeta));
        var d = new Data();
        if (meta is not null)
        {
            d.Headers = (meta["headers"] as JsonArray ?? new()).Select(S).ToList();
            d.CodeCol = S(meta["codeCol"]);
            var extra = (meta["extra"] as JsonArray ?? new()).Select(S).ToList();
            foreach (var r in Read(Db, SheetRows).OfType<JsonObject>())
            {
                var o = new Dictionary<string, JsonNode?>();
                for (var i = 0; i < d.Headers.Count; i++) o[d.Headers[i]] = DecVal(r["c" + i]) ?? "";
                for (var i = 0; i < extra.Count; i++) o[extra[i]] = DecVal(r["e" + i]) ?? "";
                if (!o.ContainsKey("__code") && d.CodeCol.Length > 0) o["__code"] = ExtractCode(S(o.GetValueOrDefault(d.CodeCol)));
                d.Rows.Add(o);
            }
            if (meta["edits"] is JsonObject eds) d.Edits = DecodeEdits(eds);
        }
        Bang1 = d;
        // Bảng 3
        SavedRows = Read(Db, SheetSaved).OfType<JsonObject>().Select(x => new Saved
        {
            Id = S(x["id"]) is { Length: > 0 } id ? id : NewId(),
            Date = S(x["Ngày"]) is var dt && dt.Length >= 10 ? dt[..10] : S(x["Ngày"]),
            Ma = S(x["Mã hàng"]), Size = S(x["Size"]),
            Net = Num(x["Net"]) ?? 0, Sl = Num(x["Số lượng"]),
            Src = S(x["Nguồn"]), At = S(x["Nạp lúc"]), Edited = S(x["Sửa tay"]) == "1", EditAt = S(x["Sửa lúc"]),
        }).Where(r => IsoOk(r.Date)).ToList();
        // phụ: lỗi đọc không khóa ghi
        try
        {
            Files = Read(Db, SheetFiles).OfType<JsonObject>().Select(x => new FileSeen(S(x["hash"]), S(x["name"]), (long)(Num(x["size"]) ?? 0), S(x["at"]), (int)(Num(x["days"]) ?? 0)))
                .Where(f => f.Hash.Length > 0).ToList();
        }
        catch { Files = new(); }
        try
        {
            var str = string.Concat(Read(Db, SheetUndo).OfType<JsonObject>().OrderBy(x => Num(x["part"]) ?? 0).Select(x => S(x["data"])));
            Undo = str.Length > 0 ? DecodeUndo(JsonNode.Parse(str) as JsonObject) : null;
        }
        catch { Undo = null; }
        try
        {
            SizeLabels = Read(Db, SheetSize).OfType<JsonObject>()
                .Select(x => (k: S(x["Mã Size"]).Trim(), v: S(x["Nhãn hiển thị"]).Trim())).Where(x => x.k.Length > 0 && x.v.Length > 0)
                .GroupBy(x => x.k).ToDictionary(g => g.Key, g => g.Last().v);
        }
        catch { SizeLabels = new(); }
        LoadMapping();
        Loaded = true;
    }

    /// <summary>Đọc lại Mã hóa sản phẩm ("1. Tên sản phẩm" + danh sách chuẩn) và Ánh xạ tên hàng từ Tổng hợp (chỉ đọc).</summary>
    public void LoadMapping()
    {
        var seg1 = new Dictionary<string, string>();
        JsonArray maHoa, axRows, axCfg;
        try { maHoa = Read(TongHopService.Db, "MaHoa"); axRows = Read(TongHopService.Db, "AnhXaTen"); axCfg = Read(TongHopService.Db, "AnhXaCauHinh"); }
        catch { maHoa = new(); axRows = new(); axCfg = new(); }
        foreach (var r in maHoa.OfType<JsonObject>())
            if (S(r["Tổ hợp"]) == "1") { var k = TongHopService.Norm(S(r["Mã"])); if (k.Length > 0 && !(k == "vrpd" && S(r["Tên"]) == "PD")) seg1.TryAdd(k, S(r["Tên"])); }
        foreach (var e in TongHopService.FixedCatalog().GetValueOrDefault(1) ?? new()) seg1[TongHopService.Norm(e.Code)] = e.Name;
        _seg1 = seg1;
        AnhXa? ax = null;
        try { ax = AnhXa.FromSheets(axRows, axCfg); } catch { ax = null; }
        _ax = ax ?? AnhXa.Defaults();
    }

    private static Dictionary<string, Edit> DecodeEdits(JsonObject o)
    {
        var res = new Dictionary<string, Edit>();
        foreach (var (k, v) in o)
        {
            if (v is not JsonObject e) continue;
            var ed = new Edit
            {
                W = e["w"] is null ? null : S(e["w"]), P = e["p"] is null || S(e["p"]).Length == 0 ? null : S(e["p"]), S = e["s"] is null || S(e["s"]).Length == 0 ? null : S(e["s"]),
            };
            if (e["v"] is JsonObject vv) ed.V = vv.Where(p => Num(p.Value) is not null).ToDictionary(p => p.Key, p => Num(p.Value)!.Value);
            if (!ed.Empty) res[k] = ed;
        }
        return res;
    }
    private static JsonObject EncodeEdits(Dictionary<string, Edit> eds)
    {
        var o = new JsonObject();
        foreach (var (k, e) in eds)
        {
            if (e.Empty) continue;
            var x = new JsonObject();
            if (e.V is { Count: > 0 }) x["v"] = new JsonObject(e.V.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value)));
            if (e.W is not null) x["w"] = e.W;
            if (e.P is not null) x["p"] = e.P;
            if (e.S is not null) x["s"] = e.S;
            o[k] = x;
        }
        return o;
    }

    private Dictionary<string, JsonArray?> EncodeBang1()
    {
        var t = Bang1;
        var inHead = new HashSet<string>(t.Headers);
        var extra = new List<string>();
        foreach (var r in t.Rows) foreach (var k in r.Keys) if (!inHead.Contains(k) && !extra.Contains(k)) extra.Add(k);
        var rows = new JsonArray();
        foreach (var r in t.Rows)
        {
            var o = new JsonObject();
            for (var i = 0; i < t.Headers.Count; i++) o["c" + i] = EncVal(r.GetValueOrDefault(t.Headers[i]));
            for (var i = 0; i < extra.Count; i++) o["e" + i] = EncVal(r.GetValueOrDefault(extra[i]));
            rows.Add(o);
        }
        var meta = new JsonObject
        {
            ["headers"] = new JsonArray(t.Headers.Select(h => (JsonNode)h).ToArray()),
            ["extra"] = new JsonArray(extra.Select(h => (JsonNode)h).ToArray()),
            ["codeCol"] = t.CodeCol,
        };
        if (t.Edits.Count > 0) meta["edits"] = EncodeEdits(t.Edits);
        return new() { [SheetRows] = rows, [SheetMeta] = MakeMeta(meta) };
    }

    private static JsonObject EncodeSaved(Saved r) => new()
    {
        ["id"] = r.Id, ["Ngày"] = r.Date, ["Mã hàng"] = r.Ma, ["Size"] = r.Size, ["Số lượng"] = r.Sl is { } sl ? sl : "",
        ["Net"] = r.Net, ["Nguồn"] = r.Src, ["Nạp lúc"] = r.At, ["Sửa tay"] = r.Edited ? "1" : "", ["Sửa lúc"] = r.EditAt,
    };
    private static JsonObject UndoRow(Saved r) => new()
    {
        ["id"] = r.Id, ["date"] = r.Date, ["ma"] = r.Ma, ["size"] = r.Size, ["sl"] = r.Sl is { } sl ? sl : null, ["net"] = r.Net,
        ["src"] = r.Src, ["at"] = r.At, ["edited"] = r.Edited, ["editAt"] = r.EditAt,
    };
    private static UndoInfo? DecodeUndo(JsonObject? o)
    {
        if (o is null || o["days"] is not JsonArray days || o["before"] is not JsonArray before) return null;
        return new UndoInfo
        {
            At = S(o["at"]), File = S(o["file"]), Days = days.Select(S).ToList(),
            Before = before.OfType<JsonObject>().Select(x => new Saved
            {
                Id = S(x["id"]), Date = S(x["date"]), Ma = S(x["ma"]), Size = S(x["size"]), Sl = Num(x["sl"]), Net = Num(x["net"]) ?? 0,
                Src = S(x["src"]), At = S(x["at"]), Edited = x["edited"] is JsonValue ev && ev.TryGetValue<bool>(out var b) && b, EditAt = S(x["editAt"]),
            }).ToList(),
        };
    }

    /// <summary>Lưu Bảng 1 + số sửa tay của Bảng 2.</summary>
    public void SaveBang1() { RequireWrite(); _src().Write(Db, EncodeBang1()); }

    /// <summary>Lưu Bảng 3.</summary>
    public void SaveSaved()
    {
        RequireWrite();
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetSaved] = new JsonArray(SavedRows.Select(r => (JsonNode)EncodeSaved(r)).ToArray()) });
    }

    private void SaveExtra()
    {
        var files = new JsonArray(Files.Take(200).Select(f => (JsonNode)new JsonObject { ["hash"] = f.Hash, ["name"] = f.Name, ["size"] = f.Size, ["at"] = f.At, ["days"] = f.Days }).ToArray());
        var parts = new JsonArray();
        if (Undo is not null)
        {
            var str = new JsonObject
            {
                ["at"] = Undo.At, ["file"] = Undo.File, ["days"] = new JsonArray(Undo.Days.Select(x => (JsonNode)x).ToArray()),
                ["before"] = new JsonArray(Undo.Before.Select(x => (JsonNode)UndoRow(x)).ToArray()),
            }.ToJsonString();
            for (var i = 0; i < str.Length; i += 30000) parts.Add(new JsonObject { ["part"] = parts.Count, ["data"] = str.Substring(i, Math.Min(30000, str.Length - i)) });
        }
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetFiles] = files, [SheetUndo] = parts });
    }

    private void SaveSizeLabels()
    {
        _src().Write(Db, new Dictionary<string, JsonArray?>
        {
            [SheetSize] = new JsonArray(SizeLabels.Select(p => (JsonNode)new JsonObject { ["Mã Size"] = p.Key, ["Nhãn hiển thị"] = p.Value }).ToArray()),
        });
    }

    public void Log(string action, string detail) => _audit.Log(_session.Username, "Báo cáo rã đông: " + action, detail);

    // ------------------------------------------------------------------ nạp Excel (Bảng 1)

    public static string ExtractCode(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase)) s = s[CodePrefix.Length..];
        return s.Trim();
    }

    private static bool IsCodeHeader(string? h) { var n = Norm(h); return n is "ma hang" or "mahang" || n.Contains("ma hang"); }

    public static string FindTenHangColumn(List<string> headers) => headers.FirstOrDefault(h => { var n = Norm(h); return n == "ten hang" || n.Contains("ten hang"); }) ?? "";

    /// <summary>FNV-1a 32 bit (giống t3sFingerprint) → nhận ra file đã nạp, kể cả file nạp ở app cũ.</summary>
    public static string Fingerprint(byte[] u8)
    {
        uint h = 2166136261;
        foreach (var b in u8) { h ^= b; h *= 16777619; }
        return ToBase36(h) + ":" + u8.Length;
    }
    private static string ToBase36(uint v)
    {
        const string d = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (v == 0) return "0";
        var sb = new StringBuilder();
        while (v > 0) { sb.Insert(0, d[(int)(v % 36)]); v /= 36; }
        return sb.ToString();
    }

    /// <summary>Đọc file Excel báo cáo kho (sheet đầu): tự tìm dòng tiêu đề có cột "Mã hàng", bỏ tiền tố "TPC-", chỉ giữ dòng có mã đúng 21 ký tự.</summary>
    public static Data ReadExcel(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception e) { throw new BusinessException("Không đọc được file: " + e.Message); }
        List<List<JsonNode?>> rows2D;
        try
        {
            using var ms = new MemoryStream(bytes);
            using var wb = new XLWorkbook(ms);
            var ws = wb.Worksheets.First();
            var used = ws.RangeUsed();
            rows2D = new();
            if (used is not null)
            {
                int r1 = used.FirstRow().RowNumber(), r2 = used.LastRow().RowNumber(), c1 = used.FirstColumn().ColumnNumber(), c2 = used.LastColumn().ColumnNumber();
                for (var r = r1; r <= r2; r++)
                {
                    var row = new List<JsonNode?>();
                    for (var c = c1; c <= c2; c++)
                    {
                        var cell = ws.Cell(r, c);
                        row.Add(cell.IsEmpty() ? null : cell.DataType switch
                        {
                            XLDataType.Number => JsonValue.Create(cell.GetDouble()),
                            XLDataType.DateTime => JsonValue.Create(cell.GetDateTime().ToOADate()),
                            XLDataType.TimeSpan => JsonValue.Create(cell.GetTimeSpan().TotalDays),
                            XLDataType.Boolean => cell.GetBoolean() ? "TRUE" : "FALSE",
                            _ => cell.GetFormattedString(),
                        });
                    }
                    rows2D.Add(row);
                }
            }
        }
        catch (Exception e) { throw new BusinessException("Không đọc được file Excel. Vui lòng kiểm tra lại định dạng file. (" + e.Message + ")"); }
        if (rows2D.Count == 0) throw new BusinessException("File Excel không có dữ liệu.");
        var headerIdx = 0;
        for (var i = 0; i < Math.Min(rows2D.Count, 20); i++)
            if (rows2D[i].Any(c => IsCodeHeader(S(c)))) { headerIdx = i; break; }
        var headers = rows2D[headerIdx].Select((h, i) => S(h).Trim() is { Length: > 0 } t ? t : $"Cột {i + 1}").ToList();
        var codeCol = TongHopService.FindCodeColumn(headers);
        var d = new Data { Headers = headers, CodeCol = codeCol, FileName = Path.GetFileName(path), FileHash = Fingerprint(bytes), FileSize = bytes.Length };
        foreach (var r in rows2D.Skip(headerIdx + 1))
        {
            if (!r.Any(c => S(c).Trim().Length > 0)) continue;
            var o = new Dictionary<string, JsonNode?>();
            for (var i = 0; i < headers.Count; i++) o[headers[i]] = i < r.Count && r[i] is not null ? r[i]!.DeepClone() : "";
            var code = ExtractCode(S(o.GetValueOrDefault(codeCol)));
            if (code.Length != TongHopService.CodeLength) continue;
            o["__code"] = code;
            d.Rows.Add(o);
        }
        if (d.Rows.Count == 0)
            throw new BusinessException($"Không tìm thấy dòng nào có Mã hàng hợp lệ (đúng {TongHopService.CodeLength} ký tự sau khi bỏ tiền tố \"{CodePrefix}\") trong file Excel.");
        return d;
    }

    /// <summary>Nạp file vào Bảng 1 (thay dữ liệu cũ, bỏ số sửa tay cũ) + học nhãn Size + lưu.</summary>
    public void SetBang1(Data d)
    {
        RequireWrite();
        LearnSizeLabels(d);
        Bang1 = d;
        _undo2.Clear();
        SaveBang1();
        Log("Nạp file", $"{d.FileName}: {d.Rows.Count} dòng");
    }

    public void ClearBang1()
    {
        RequireWrite();
        PushUndo("Xóa dữ liệu đã nhập");
        Bang1 = new Data();
        SaveBang1();
    }

    // ------------------------------------------------------------------ Size

    public static string SizeFromName(string? tenHang)
    {
        var m = Regex.Match(tenHang ?? "", @"\d{1,3}\s*/\s*\d{1,3}");
        return m.Success ? Regex.Replace(m.Value, @"\s+", "") : "";
    }

    /// <summary>Học nhãn Size "x/y" từ cột "Tên hàng" (dữ liệu mới nhất thắng). True nếu có đổi (đã lưu).</summary>
    public bool LearnSizeLabels(Data d)
    {
        var col = FindTenHangColumn(d.Headers);
        if (col.Length == 0) return false;
        var changed = false;
        foreach (var r in d.Rows)
        {
            var label = SizeFromName(S(r.GetValueOrDefault(col)));
            if (label.Length == 0) continue;
            var code = SegmentRaw(S(r.GetValueOrDefault("__code")), 8, "");
            if (code.Length != 4) continue;
            if (SizeLabels.GetValueOrDefault(code) != label) { SizeLabels[code] = label; changed = true; }
        }
        if (changed && CanEdit) { try { SaveSizeLabels(); } catch { /* chỉ mất bản lưu nhãn, không ảnh hưởng số liệu */ } }
        return changed;
    }

    public string FormatSize(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (SizeLabels.TryGetValue(t, out var l)) return l;
        if (SizeDisplayMap.TryGetValue(t, out var m)) return m;
        if (Regex.IsMatch(t, "^[0-9]{4}$") && t[0] != '0') return t[..2] + "/" + t[2..];
        if (Regex.IsMatch(t, "^[0-9]+$")) return long.TryParse(t, out var n) ? n.ToString(CultureInfo.InvariantCulture) : t;
        return t;
    }

    // ------------------------------------------------------------------ gộp (Bảng 2)

    public static string SegmentRaw(string code, int seg, string missing)
    {
        var off = TongHopService.Offset(seg); var len = TongHopService.Segs[seg - 1].Length;
        return string.IsNullOrEmpty(code) || code.Length < off + len ? missing : code.Substring(off, len);
    }

    /// <summary>Tên sản phẩm đầy đủ (sản phẩm → thị trường + khách hàng → mạ băng, theo Ánh xạ tên hàng).</summary>
    public string ProductLabel(string code)
    {
        var raw = SegmentRaw(code, 1, "(Mã hàng thiếu ký tự Tên sản phẩm)");
        var name = _seg1.GetValueOrDefault(TongHopService.Norm(raw)) ?? "";
        return _ax.FullLabel(code, name.Length > 0 ? name : raw);
    }

    public string GroupKey(Dictionary<string, JsonNode?> r, bool hasDate, string dateCol)
    {
        var code = S(r.GetValueOrDefault("__code"));
        var date = hasDate ? (TongHopService.FormatDate(r.GetValueOrDefault(dateCol)) is { Length: > 0 } dd ? dd : "(Không có ngày)") : "";
        return date + "\u0001" + ProductLabel(code) + "\u0001" + SegmentRaw(code, 8, "(Mã hàng thiếu ký tự Size)");
    }

    private static int DateKey(string d)
    {
        var m = Regex.Match(d ?? "", @"^(\d{2})/(\d{2})/(\d{4})$");
        return m.Success ? int.Parse(m.Groups[3].Value) * 10000 + int.Parse(m.Groups[2].Value) * 100 + int.Parse(m.Groups[1].Value) : int.MaxValue;
    }

    private static readonly string[] SlNames = { "so luong", "soluong", "so kien", "sl" };

    /// <summary>Gộp theo Ngày (nếu có) + Tên sản phẩm + Size, cộng các cột số; Net thiếu → Số lượng × Trọng lượng; áp số sửa tay. Giống computeTab3Groups.</summary>
    public Groups Compute(Data? data = null, bool noEdits = false)
    {
        var src = data ?? Bang1;
        var headers = src.Headers;
        var res = new Groups { DateCol = TongHopService.FindDateColumn(headers) };
        res.WeightCol = headers.FirstOrDefault(h => Norm(h).Contains("trong luong")) ?? "";
        var caCol = headers.FirstOrDefault(h => Norm(h) == "ca") ?? "";
        res.HasDate = res.DateCol.Length > 0;
        var excluded = new HashSet<string>(new[] { src.CodeCol, res.DateCol, res.WeightCol, caCol }.Where(x => x.Length > 0));
        var numeric = headers.Where(h => !excluded.Contains(h) && src.Rows.Any(r => Num(r.GetValueOrDefault(h)) is not null)).Distinct().ToList();
        var slCol = headers.FirstOrDefault(h => !excluded.Contains(h) && SlNames.Contains(Norm(h)))
                    ?? headers.FirstOrDefault(h => !excluded.Contains(h) && Regex.IsMatch(Norm(h), "so luong|so kien")) ?? "";
        var netCol = headers.FirstOrDefault(h => !excluded.Contains(h) && Norm(h) == "net") ?? "";
        if (netCol.Length == 0 && slCol.Length > 0 && res.WeightCol.Length > 0) netCol = headers.Contains("Net") ? "Net (tự tính)" : "Net";
        var canAuto = netCol.Length > 0 && slCol.Length > 0 && res.WeightCol.Length > 0;
        if (canAuto && !numeric.Contains(netCol)) numeric.Add(netCol);
        res.NumericCols = numeric; res.NetCol = netCol;

        foreach (var r in src.Rows)
        {
            var code = S(r.GetValueOrDefault("__code"));
            var product = ProductLabel(code);
            var size = SegmentRaw(code, 8, "(Mã hàng thiếu ký tự Size)");
            var date = res.HasDate ? (TongHopService.FormatDate(r.GetValueOrDefault(res.DateCol)) is { Length: > 0 } dd ? dd : "(Không có ngày)") : "";
            var key = date + "\u0001" + product + "\u0001" + size;
            if (!res.ByKey.TryGetValue(key, out var g))
            {
                g = new Group { Key = key, Date = date, Product = product, Size = size };
                foreach (var c in numeric) g.Sums[c] = 0;
                res.ByKey[key] = g; res.Order.Add(key);
            }
            g.Count++;
            foreach (var c in numeric)
            {
                if (c == netCol)
                {
                    var raw = Num(r.GetValueOrDefault(netCol));
                    if (canAuto && (raw is null || raw == 0))
                    {
                        var sl = Num(r.GetValueOrDefault(slCol)); var tl = Num(r.GetValueOrDefault(res.WeightCol));
                        if (sl is > 0 && tl is > 0) { g.Sums[c] += Round(sl.Value * tl.Value); g.AutoNet++; continue; }
                    }
                    if (raw is not null) g.Sums[c] += raw.Value;
                    continue;
                }
                if (Num(r.GetValueOrDefault(c)) is { } v) g.Sums[c] += v;
            }
            if (res.WeightCol.Length > 0)
            {
                var wv = r.GetValueOrDefault(res.WeightCol);
                var p = Num(wv);
                var w = p is not null ? Round(p.Value).ToString(CultureInfo.InvariantCulture) : S(wv).Trim();
                if (w.Length > 0 && !g.WeightValues.Contains(w)) g.WeightValues.Add(w);
            }
        }
        if (data is null && !noEdits)
            foreach (var k in res.Order)
            {
                if (!src.Edits.TryGetValue(k, out var ed)) continue;
                var g = res.ByKey[k]; g.Edited = true;
                if (ed.V is not null) foreach (var (c, v) in ed.V) if (g.Sums.ContainsKey(c)) g.Sums[c] = v;
                if (!string.IsNullOrEmpty(ed.W)) g.WeightValues = Regex.Split(ed.W, @"\s*[•;|]\s*").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (!string.IsNullOrEmpty(ed.P)) g.Product = ed.P;
                if (!string.IsNullOrEmpty(ed.S)) { g.Size = ed.S; g.SizeFixed = ed.S; }
            }
        // Ngày tăng dần → Tên sản phẩm → Size (sắp ổn định)
        res.Order = res.Order.Select((k, i) => (k, i)).OrderBy(x => x, Comparer<(string k, int i)>.Create((a, b) =>
        {
            var ga = res.ByKey[a.k]; var gb = res.ByKey[b.k];
            var c = DateKey(ga.Date).CompareTo(DateKey(gb.Date));
            if (c == 0) c = Natural(ga.Product, gb.Product);
            if (c == 0) c = Natural(FormatSize(ga.Size), FormatSize(gb.Size));
            return c != 0 ? c : a.i.CompareTo(b.i);
        })).Select(x => x.k).ToList();
        return res;
    }

    // ---------- sửa Bảng 2 (số sửa tay), xóa nhóm / dòng gốc, hoàn tác trong phiên

    private readonly List<(string label, List<Dictionary<string, JsonNode?>> rows, Dictionary<string, Edit> edits)> _undo2 = new();
    public string? UndoLabel => _undo2.Count > 0 ? _undo2[^1].label : null;
    private static Dictionary<string, Edit> CloneEdits(Dictionary<string, Edit> e) => e.ToDictionary(p => p.Key, p => new Edit { V = p.Value.V is null ? null : new(p.Value.V), W = p.Value.W, P = p.Value.P, S = p.Value.S });
    private static Dictionary<string, JsonNode?> CloneRow(Dictionary<string, JsonNode?> r) => r.ToDictionary(p => p.Key, p => p.Value?.DeepClone());
    private void PushUndo(string label)
    {
        _undo2.Add((label, Bang1.Rows.Select(CloneRow).ToList(), CloneEdits(Bang1.Edits)));
        if (_undo2.Count > 20) _undo2.RemoveAt(0);
    }
    public string? UndoBang2()
    {
        RequireWrite();
        if (_undo2.Count == 0) return null;
        var u = _undo2[^1]; _undo2.RemoveAt(_undo2.Count - 1);
        Bang1.Rows = u.rows; Bang1.Edits = u.edits;
        SaveBang1();
        return u.label;
    }

    /// <summary>Sửa 1 ô của Bảng gộp. col = "__p" (tên SP), "__s" (size), "__w" (trọng lượng) hoặc tên cột số. Trùng số gốc → bỏ sửa tay.</summary>
    public void EditGroup(string key, string col, string value)
    {
        RequireWrite();
        var g0 = Compute(noEdits: true).ByKey.GetValueOrDefault(key) ?? throw new BusinessException("Không tìm thấy nhóm để sửa.");
        value = value.Trim();
        double? n = null;
        if (col is not ("__p" or "__s" or "__w"))
        {
            n = Num(value);
            if (n is null) throw new BusinessException($"\"{value}\" không phải số.");
        }
        PushUndo("Sửa số liệu bảng gộp");
        var e = Bang1.Edits.TryGetValue(key, out var ex) ? ex : Bang1.Edits[key] = new Edit();
        switch (col)
        {
            case "__p": e.P = value == g0.Product || value.Length == 0 ? null : value; break;
            case "__s": e.S = value == FormatSize(g0.Size) || value == g0.Size || value.Length == 0 ? null : value; break;
            case "__w": e.W = value == string.Join(" • ", g0.WeightValues) ? null : value; break;
            default:
                e.V ??= new();
                if (Math.Abs(n!.Value - g0.Sums.GetValueOrDefault(col)) < 1e-9) e.V.Remove(col); else e.V[col] = n.Value;
                if (e.V.Count == 0) e.V = null;
                break;
        }
        if (e.Empty) Bang1.Edits.Remove(key);
        SaveBang1();
    }

    /// <summary>Bỏ số sửa tay (col null = cả nhóm).</summary>
    public void RevertGroups(IEnumerable<string> keys, string? col = null)
    {
        RequireWrite();
        var ks = keys.Where(Bang1.Edits.ContainsKey).ToList();
        if (ks.Count == 0) return;
        PushUndo("Hoàn lại số gốc");
        foreach (var k in ks)
        {
            if (col is null) { Bang1.Edits.Remove(k); continue; }
            var e = Bang1.Edits[k];
            if (col == "__p") e.P = null; else if (col == "__s") e.S = null; else if (col == "__w") e.W = null; else { e.V?.Remove(col); if (e.V is { Count: 0 }) e.V = null; }
            if (e.Empty) Bang1.Edits.Remove(k);
        }
        SaveBang1();
    }

    /// <summary>Xóa nhóm = xóa luôn các dòng gốc thuộc nhóm. Trả về số dòng gốc đã xóa.</summary>
    public int DeleteGroups(IEnumerable<string> keys)
    {
        RequireWrite();
        var set = keys.ToHashSet();
        if (set.Count == 0) return 0;
        var dc = TongHopService.FindDateColumn(Bang1.Headers);
        PushUndo($"Xóa {set.Count} nhóm");
        var before = Bang1.Rows.Count;
        Bang1.Rows = Bang1.Rows.Where(r => !set.Contains(GroupKey(r, dc.Length > 0, dc))).ToList();
        foreach (var k in set) Bang1.Edits.Remove(k);
        SaveBang1();
        return before - Bang1.Rows.Count;
    }

    public void DeleteRawRows(IEnumerable<Dictionary<string, JsonNode?>> rows)
    {
        RequireWrite();
        var set = rows.ToHashSet();
        if (set.Count == 0) return;
        PushUndo($"Xóa {set.Count} dòng gốc");
        Bang1.Rows = Bang1.Rows.Where(r => !set.Contains(r)).ToList();
        SaveBang1();
    }

    /// <summary>Sửa 1 ô dòng gốc (Bảng chưa gộp): cột số phải là số, cột Ngày phải đọc được ngày.</summary>
    public void EditRaw(Dictionary<string, JsonNode?> row, string col, string value, bool numeric)
    {
        RequireWrite();
        JsonNode? v = value;
        if (numeric && value.Trim().Length > 0)
        {
            v = Num(value) is { } n ? JsonValue.Create(n) : throw new BusinessException($"\"{value}\" không phải số.");
        }
        else if (col == TongHopService.FindDateColumn(Bang1.Headers))
        {
            if (!Regex.IsMatch(TongHopService.FormatDate(JsonValue.Create(value)), @"^\d{2}/\d{2}/\d{4}$")) throw new BusinessException("Ngày phải dạng dd/mm/yyyy.");
        }
        PushUndo("Sửa ô dòng gốc");
        row[col] = v;
        if (col == Bang1.CodeCol) row["__code"] = ExtractCode(value);
        var ed = row.GetValueOrDefault("__ed") as JsonObject ?? new JsonObject();
        ed[col] = true; row["__ed"] = ed;
        SaveBang1();
    }

    /// <summary>Bỏ sửa tay không còn khớp nhóm nào (sau khi nạp lại / xóa dòng).</summary>
    public void PruneEdits()
    {
        if (Bang1.Edits.Count == 0) return;
        var ks = Compute(noEdits: true).ByKey.Keys.ToHashSet();
        foreach (var k in Bang1.Edits.Keys.Where(k => !ks.Contains(k)).ToList()) Bang1.Edits.Remove(k);
    }

    // ------------------------------------------------------------------ Bảng 3: lưu theo ngày

    public string SizeExportValue(string? raw) => FormatSize(raw);

    private static string SlColOf(List<string> numeric, string netCol) =>
        numeric.FirstOrDefault(c => c != netCol && SlNames.Contains(Norm(c))) ?? numeric.FirstOrDefault(c => c != netCol && Regex.IsMatch(Norm(c), "so luong|so kien")) ?? "";

    /// <summary>Gom Bảng 2 (của Bảng 1 hiện tại, hoặc của file <paramref name="data"/>) theo ngày ISO ("" = file không có cột Ngày).</summary>
    public SortedDictionary<string, List<DayRow>> DaysFrom(Data? data = null)
    {
        var g = Compute(data);
        var netCol = g.NetCol.Length > 0 ? g.NetCol : g.NumericCols.FirstOrDefault(c => Norm(c) == "net") ?? g.NumericCols.FirstOrDefault() ?? "";
        if (netCol.Length == 0) throw new BusinessException("Không tìm thấy cột \"Net\" (hoặc cột số nào) trong dữ liệu đã nạp.");
        var slCol = SlColOf(g.NumericCols, netCol);
        var days = new SortedDictionary<string, List<DayRow>>(StringComparer.Ordinal);
        foreach (var k in g.Order)
        {
            var x = g.ByKey[k];
            var iso = g.HasDate ? DmyToIso(x.Date) : "";
            if (!days.TryGetValue(iso, out var list)) days[iso] = list = new();
            list.Add(new DayRow(x.Product, SizeExportValue(x.Size), slCol.Length > 0 ? Round(x.Sums[slCol]) : null, Round(x.Sums[netCol])));
        }
        return days;
    }

    public static double DaySum(IEnumerable<Saved> rows) => Round(rows.Sum(r => r.Net));
    public static double DaySum(IEnumerable<DayRow> rows) => Round(rows.Sum(r => r.Net));
    public static double? SlSum(IEnumerable<Saved> rows) { var l = rows.ToList(); return l.Any(r => r.Sl is not null) ? Round(l.Sum(r => r.Sl ?? 0)) : null; }
    private static string KeyOf(string ma, string size) => ma + "\u0001" + size.Trim();

    public enum Kind { Manual = 0, Chg = 1, Sl = 2, New = 3, Old = 4, Same = 5 }
    public sealed class DiffItem
    {
        public Kind Kind; public string Key = ""; public DayRow? X; public List<Saved>? List; public double Cur; public double? CurSl;
        public string Ma => X?.Ma ?? List![0].Ma;
        public string Size => X?.Size ?? List![0].Size;
        /// <summary>Mặc định: dòng file khác số đã sửa tay → giữ số đã sửa; còn lại → lấy theo file.</summary>
        public bool DefaultTake => Kind != Kind.Manual;
    }

    /// <summary>So 1 ngày: file ↔ đã lưu (giống t3sDiffDay).</summary>
    public (List<Saved> existing, List<DiffItem> items, double before) DiffDay(string date, List<DayRow> fileRows)
    {
        var ex = SavedRows.Where(r => r.Date == date).ToList();
        var by = new Dictionary<string, List<Saved>>();
        foreach (var r in ex) { var k = KeyOf(r.Ma, r.Size); if (!by.TryGetValue(k, out var l)) by[k] = l = new(); l.Add(r); }
        var items = new List<DiffItem>(); var seen = new HashSet<string>();
        foreach (var x in fileRows)
        {
            var k = KeyOf(x.Ma, x.Size); seen.Add(k);
            if (!by.TryGetValue(k, out var list)) { items.Add(new DiffItem { Kind = Kind.New, Key = k, X = x }); continue; }
            var cur = DaySum(list); var curSl = SlSum(list);
            var slDiff = x.Sl is not null && (curSl is null || Math.Abs(curSl.Value - x.Sl.Value) > 0.005);
            items.Add(new DiffItem
            {
                Kind = Math.Abs(cur - x.Net) <= 0.005 ? (slDiff ? Kind.Sl : Kind.Same) : list.Any(r => r.Edited) ? Kind.Manual : Kind.Chg,
                Key = k, X = x, List = list, Cur = cur, CurSl = curSl,
            });
        }
        foreach (var (k, list) in by) if (!seen.Contains(k)) items.Add(new DiffItem { Kind = Kind.Old, Key = k, List = list, Cur = DaySum(list) });
        items = items.OrderBy(i => (int)i.Kind).ToList();
        return (ex, items, DaySum(ex));
    }

    /// <summary>Kế hoạch lưu 1 ngày. Mode: "new" | "update" | "replace" | "add" | "skip". Take[key] = lấy theo file (chỉ dùng cho "update").</summary>
    public sealed class DayPlan
    {
        public string Date = "";
        public List<DayRow> Rows = new();
        public string Mode = "";
        public Dictionary<string, bool> Take = new();
    }

    public sealed record PlanCalc(bool Has, string Mode, List<DiffItem> Items, List<Saved> Existing, double Before, double After);

    /// <summary>Tính trước kết quả 1 ngày (cho hộp thoại). defaultMode = chế độ khi ngày đã có số liệu.</summary>
    public PlanCalc Calc(DayPlan p, string defaultMode = "update")
    {
        var (ex, items, before) = DiffDay(p.Date, p.Rows);
        var has = ex.Count > 0;
        var mode = p.Mode;
        if (!has) mode = mode == "skip" ? "skip" : "new";
        else if (mode is "" or "new") mode = defaultMode;
        double after;
        if (mode == "skip") after = before;
        else if (mode is "replace" or "new") after = DaySum(p.Rows);
        else if (mode == "add") after = Round(before + DaySum(p.Rows));
        else
        {
            after = before;
            foreach (var x in items)
            {
                if (!(p.Take.TryGetValue(x.Key, out var t) ? t : x.DefaultTake)) continue;
                if (x.Kind == Kind.New) after += x.X!.Net;
                else if (x.Kind is Kind.Chg or Kind.Manual) after += x.X!.Net - x.Cur;
            }
            after = Round(after);
        }
        return new PlanCalc(has, mode, items, ex, before, after);
    }

    public sealed record ApplyResult(int Days, int NewRows, int Changed, bool NoChange);

    /// <summary>Ghi kế hoạch vào Bảng 3 (giống nút "Lưu" / "Cập nhật" của app cũ) + ghi nhớ file + lưu bản hoàn tác.</summary>
    public ApplyResult Apply(List<DayPlan> plan, string fileName, string fileHash, long fileSize, string defaultMode = "update")
    {
        RequireWrite();
        var cs = plan.Select(p => (p, c: Calc(p, defaultMode))).ToList();
        if (cs.Any(x => !IsoOk(x.p.Date) && x.c.Mode != "skip")) throw new BusinessException("Chọn ngày cho dữ liệu chưa có cột Ngày (hoặc chọn Bỏ qua).");
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var affected = cs.Where(x => x.c.Mode != "skip").Select(x => x.p.Date).Distinct().ToList();
        if (affected.Count == 0) return new ApplyResult(0, 0, 0, true);
        var before = SavedRows.Where(r => affected.Contains(r.Date)).Select(r => r.Clone()).ToList();
        var snapshot = SavedRows.Select(r => r.Clone()).ToList();
        int nNew = 0, nChg = 0, nDays = 0;
        Saved Fresh(DayRow x, string date) => new() { Date = date, Ma = x.Ma, Size = x.Size, Sl = x.Sl, Net = x.Net, Src = fileName, At = now };
        foreach (var (p, c) in cs)
        {
            if (c.Mode == "skip") continue;
            nDays++;
            if (c.Mode == "replace") { SavedRows.RemoveAll(r => r.Date == p.Date); SavedRows.AddRange(p.Rows.Select(x => Fresh(x, p.Date))); nNew += p.Rows.Count; }
            else if (c.Mode == "new") { SavedRows.AddRange(p.Rows.Select(x => Fresh(x, p.Date))); nNew += p.Rows.Count; }
            else if (c.Mode == "add")
                foreach (var x in p.Rows)
                {
                    var hit = SavedRows.FirstOrDefault(r => r.Date == p.Date && KeyOf(r.Ma, r.Size) == KeyOf(x.Ma, x.Size));
                    if (hit is not null)
                    {
                        hit.Net = Round(hit.Net + x.Net);
                        if (x.Sl is not null) hit.Sl = Round((hit.Sl ?? 0) + x.Sl.Value);
                        hit.Src = fileName; hit.At = now; nChg++;
                    }
                    else { SavedRows.Add(Fresh(x, p.Date)); nNew++; }
                }
            else
                foreach (var x in c.Items)
                {
                    if (!(p.Take.TryGetValue(x.Key, out var t) ? t : x.DefaultTake)) continue;
                    if (x.Kind == Kind.New) { SavedRows.Add(Fresh(x.X!, p.Date)); nNew++; }
                    else if (x.Kind == Kind.Sl)
                    {
                        var first = x.List![0];
                        if (x.List.Count > 1) first.Net = DaySum(x.List);
                        first.Sl = x.X!.Sl; first.Src = fileName; first.At = now;
                        foreach (var r in x.List.Skip(1)) SavedRows.Remove(r);
                        nChg++;
                    }
                    else if (x.Kind is Kind.Chg or Kind.Manual)
                    {
                        var first = x.List![0];
                        first.Net = x.X!.Net; first.Sl = x.X.Sl ?? SlSum(x.List); first.Src = fileName; first.At = now; first.Edited = false; first.EditAt = "";
                        foreach (var r in x.List.Skip(1)) SavedRows.Remove(r);
                        nChg++;
                    }
                }
        }
        if (nNew + nChg == 0 && cs.All(x => x.c.Mode is "update" or "skip"))
        {
            SavedRows = snapshot;
            return new ApplyResult(nDays, 0, 0, true);
        }
        try { SaveSaved(); }
        catch { SavedRows = snapshot; throw; }
        Undo = new UndoInfo { At = now, File = fileName, Days = affected, Before = before };
        if (fileHash.Length > 0)
        {
            Files.RemoveAll(f => f.Hash == fileHash);
            Files.Insert(0, new FileSeen(fileHash, fileName, fileSize, now, nDays));
        }
        try { SaveExtra(); } catch { /* chỉ mất cảnh báo nạp trùng / nút hoàn tác */ }
        Log("Lưu số liệu theo ngày", $"{fileName}: {nDays} ngày, +{nNew} dòng, ~{nChg} dòng đổi số");
        return new ApplyResult(nDays, nNew, nChg, false);
    }

    public FileSeen? SeenBefore(string hash) => Files.FirstOrDefault(f => f.Hash == hash);

    public void UndoUpdate()
    {
        RequireWrite();
        var u = Undo ?? throw new BusinessException("Không có lần cập nhật nào để hoàn tác.");
        var snapshot = SavedRows;
        SavedRows = SavedRows.Where(r => !u.Days.Contains(r.Date)).Concat(u.Before.Select(r => r.Clone())).ToList();
        try { SaveSaved(); } catch { SavedRows = snapshot; throw; }
        Undo = null;
        try { SaveExtra(); } catch { }
        Log("Hoàn tác cập nhật", $"{u.File}: {u.Days.Count} ngày");
    }

    // ---------- sửa tay Bảng 3

    private static double? Ratio(Saved r) => r.Sl is > 0 && r.Net > 0 ? r.Net / r.Sl.Value : null;
    private double? RatioByKey(string ma, string size) =>
        SavedRows.Where(r => r.Ma == ma && r.Size == size && Ratio(r) is not null).OrderByDescending(r => r.At, StringComparer.Ordinal).Select(Ratio).FirstOrDefault();

    private static string NowIso() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    /// <summary>Thêm dòng tay. Bỏ trống Net + có Số lượng → Net = Số lượng × kg/đơn vị của dòng đã lưu cùng Mã + Size.</summary>
    public Saved AddRow(string date, string ma, string size, string slRaw, string netRaw)
    {
        RequireWrite();
        ma = ma.Trim(); size = size.Trim();
        double? sl = slRaw.Trim().Length == 0 ? null : Num(slRaw) ?? throw new BusinessException("Số lượng phải là số (hoặc để trống).");
        var net = Num(netRaw);
        if (netRaw.Trim().Length == 0 && sl is > 0 && RatioByKey(ma, size) is { } k) net = Round(sl.Value * k);
        if (!IsoOk(date) || ma.Length == 0 || size.Length == 0 || net is null)
            throw new BusinessException("Nhập đủ Ngày, Mã hàng, Size và Net (số). Có thể bỏ trống Net nếu đã nhập Số lượng và Mã hàng + Size này đã có dòng lưu trước đó (app tự tính Net).");
        var now = NowIso();
        var r = new Saved { Date = date, Ma = ma, Size = size, Sl = sl is null ? null : Round(sl.Value), Net = Round(net.Value), Src = "Nhập tay", At = now, Edited = true, EditAt = now };
        SavedRows.Add(r);
        try { SaveSaved(); } catch { SavedRows.Remove(r); throw; }
        return r;
    }

    /// <summary>Sửa 1 dòng đã lưu. Sửa Số lượng mà không đụng Net (hoặc xóa trống Net) → Net tự tính lại theo kg/đơn vị.</summary>
    public void EditRow(string id, string ma, string size, string slRaw, string netRaw)
    {
        RequireWrite();
        var r = SavedRows.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không tìm thấy dòng.");
        ma = ma.Trim(); size = size.Trim();
        double? sl = slRaw.Trim().Length == 0 ? null : Num(slRaw) ?? throw new BusinessException("Số lượng phải là số (hoặc để trống).");
        var net = Num(netRaw);
        if (sl is > 0)
        {
            var k = Ratio(r) ?? RatioByKey(ma, size);
            var netUntouched = net is not null && Round(net.Value) == r.Net;
            var slChanged = r.Sl is null || Round(sl.Value) != r.Sl;
            if (k is not null && (netRaw.Trim().Length == 0 || (netUntouched && slChanged))) net = Round(sl.Value * k.Value);
        }
        if (ma.Length == 0 || size.Length == 0 || net is null)
            throw new BusinessException("Mã hàng, Size không được trống và Net phải là số (hoặc bỏ trống Net để app tự tính từ Số lượng).");
        var slNew = sl is null ? (double?)null : Round(sl.Value);
        if (ma == r.Ma && size == r.Size && Round(net.Value) == r.Net && slNew == r.Sl) return;
        var old = r.Clone();
        r.Ma = ma; r.Size = size; r.Net = Round(net.Value); r.Sl = slNew; r.Edited = true; r.EditAt = NowIso();
        try { SaveSaved(); }
        catch { SavedRows[SavedRows.IndexOf(r)] = old; throw; }
    }

    private void RemoveWhere(Predicate<Saved> match, string action, string detail)
    {
        RequireWrite();
        var snapshot = SavedRows.ToList();
        if (SavedRows.RemoveAll(match) == 0) return;
        try { SaveSaved(); } catch { SavedRows = snapshot; throw; }
        Log(action, detail);
    }
    public void DeleteRows(IEnumerable<string> ids) { var s = ids.ToHashSet(); RemoveWhere(r => s.Contains(r.Id), "Xóa dòng đã lưu", s.Count + " dòng"); }
    public void DeleteDays(IEnumerable<string> isos) { var s = isos.ToHashSet(); RemoveWhere(r => s.Contains(r.Date), "Xóa ngày", string.Join(", ", s.Select(Dmy))); }
    public void DeleteMonth(int year, int month) =>
        RemoveWhere(r => r.Date.StartsWith(year.ToString("0000")) && int.Parse(r.Date.Substring(5, 2)) == month, "Xóa tháng", $"{month}/{year}");

    public List<string> Years()
    {
        var s = SavedRows.Select(r => r.Date[..4]).ToHashSet();
        s.Add(DateTime.Now.Year.ToString());
        return s.OrderBy(x => x).ToList();
    }

    // ------------------------------------------------------------------ Excel năm

    /// <summary>Thứ tự sheet tháng: tháng hiện tại → các tháng cũ (mới → cũ) → tháng chưa tới (giống RDXuat.monthOrder).</summary>
    public static List<int> MonthOrder(IEnumerable<int> months, int year, DateTime now)
    {
        var cur = year == now.Year ? now.Month : year < now.Year ? 13 : 0;
        var list = months.Distinct().Where(m => m is >= 1 and <= 12).ToList();
        var res = list.Where(m => m == cur).ToList();
        res.AddRange(list.Where(m => m < cur).OrderByDescending(m => m));
        if (cur != 0) res.AddRange(list.Where(m => m > cur).OrderBy(m => m));
        else res.AddRange(list.OrderByDescending(m => m));
        return res;
    }

    private const string NetFmt = "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)";

    /// <summary>Xuất file Excel năm theo mẫu "BÁO CÁO RÃ ĐÔNG &lt;năm&gt;.xlsx": sheet "Tổng &lt;năm&gt;" (31 ngày × 12 tháng) + sheet "tháng N" có dữ liệu.</summary>
    public void ExportYear(int year, string path, DateTime? today = null)
    {
        Require(Perm.Export);
        var now = today ?? DateTime.Now;
        var ys = year.ToString("0000");
        var rows = SavedRows.Where(r => r.Date.StartsWith(ys)).ToList();
        if (rows.Count == 0) throw new BusinessException($"Chưa có dữ liệu năm {year} để xuất.");
        const string FN = "Times New Roman";
        using var wb = new XLWorkbook();
        var wsY = wb.Worksheets.Add("Tổng " + year);
        var byM = rows.GroupBy(r => int.Parse(r.Date.Substring(5, 2))).ToDictionary(g => g.Key, g => g.GroupBy(r => r.Date).ToDictionary(x => x.Key, x => x.ToList()));
        var info = new Dictionary<int, Dictionary<int, (double total, string reff)>>();
        void Border(IXLCell c) { c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; }
        foreach (var m in MonthOrder(byM.Keys, year, now))
        {
            var ws = wb.Worksheets.Add("tháng " + m);
            info[m] = new();
            var days = byM[m].Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var wid = new Dictionary<int, double[]> { [1] = new[] { 29.9, 11.3, 13, 20.4 }, [6] = new[] { 29.9, 11.3, 13, 20.4 } };
            var dataRows = new HashSet<int>(); var usedRows = new HashSet<int>();
            var top = 1; (int top, string active)? lastView = null;
            for (var i = 0; i < days.Count; i += 2)
            {
                var bandEnd = top;
                foreach (var (iso, c0, colL) in new[] { (days[i], 1, "D"), (i + 1 < days.Count ? days[i + 1] : null, 6, "I") })
                {
                    if (iso is null) continue;
                    var list = byM[m][iso];
                    var r = top; var minW = new double[4];
                    for (var c = 0; c < 4; c++) { var x = ws.Cell(r, c0 + c); Border(x); x.Style.Font.SetFontName(FN).Font.SetFontSize(20).Font.SetBold(); x.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center); }
                    ws.Cell(r, c0).Value = $"NGÀY : {iso[8..10]}.{iso[5..7]}.{iso[..4]}";
                    ws.Range(r, c0, r, c0 + 3).Merge(); r++;
                    var heads = new[] { "MÃ HÀNG", "SIZE", "Số lượng", "Net" };
                    for (var c = 0; c < 4; c++) { var x = ws.Cell(r, c0 + c); x.Value = heads[c]; Border(x); x.Style.Font.SetFontName(FN).Font.SetFontSize(20).Font.SetBold(); x.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center); }
                    r++;
                    minW[2] = "Số lượng".Length * 1.8 + 2;
                    var first = r; double sum = 0;
                    foreach (var x in list)
                    {
                        var c1 = ws.Cell(r, c0); var c2 = ws.Cell(r, c0 + 1); var c3 = ws.Cell(r, c0 + 2); var c4 = ws.Cell(r, c0 + 3);
                        c1.Value = x.Ma;
                        if (Regex.IsMatch(x.Size, @"^\d+$") && double.TryParse(x.Size, out var sz)) c2.Value = sz; else c2.Value = x.Size;
                        c4.Value = x.Net; c4.Style.NumberFormat.Format = NetFmt; sum += x.Net;
                        if (x.Sl is { } sl) { c3.Value = sl; c3.Style.NumberFormat.Format = sl == Math.Floor(sl) ? "#,##0" : "#,##0.00"; minW[2] = Math.Max(minW[2], FmtSlVn(sl).Length * 1.8 + 2); }
                        foreach (var c in new[] { c1, c2, c3, c4 }) { Border(c); c.Style.Font.SetFontName(FN).Font.SetFontSize(20); }
                        c2.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                        minW[0] = Math.Max(minW[0], x.Ma.Length * 2.2 + 2); minW[1] = Math.Max(minW[1], x.Size.Length * 1.8 + 2); minW[3] = Math.Max(minW[3], Fmt2(x.Net).Length * 1.8 + 2);
                        r++;
                    }
                    sum = Round(sum); minW[3] = Math.Max(minW[3], Fmt2(sum).Length * 1.8 + 2);
                    var slSum = SlSum(list);
                    if (slSum is not null) minW[2] = Math.Max(minW[2], FmtSlVn(slSum).Length * 1.8 + 2);
                    for (var c = 0; c < 4; c++) { var x = ws.Cell(r, c0 + c); Border(x); x.Style.Font.SetFontName(FN).Font.SetFontSize(20).Font.SetBold(); }
                    ws.Cell(r, c0).Value = "Tổng cộng"; ws.Cell(r, c0).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    ws.Range(r, c0, r, c0 + 1).Merge();
                    if (slSum is not null)
                    {
                        var cs = ws.Cell(r, c0 + 2);
                        cs.FormulaA1 = $"SUM({ws.Cell(first, c0 + 2).Address.ToStringRelative()}:{ws.Cell(Math.Max(first, r - 1), c0 + 2).Address.ToStringRelative()})";
                        cs.Style.NumberFormat.Format = slSum == Math.Floor(slSum.Value) ? "#,##0" : "#,##0.00";
                    }
                    var ct = ws.Cell(r, c0 + 3);
                    ct.FormulaA1 = $"SUM({ws.Cell(first, c0 + 3).Address.ToStringRelative()}:{ws.Cell(Math.Max(first, r - 1), c0 + 3).Address.ToStringRelative()})";
                    ct.Style.NumberFormat.Format = NetFmt;
                    for (var rr = top; rr <= r; rr++) usedRows.Add(rr);
                    for (var rr = first; rr < r; rr++) dataRows.Add(rr);
                    for (var k = 0; k < 4; k++) wid[c0][k] = Math.Max(wid[c0][k], minW[k]);
                    info[m][int.Parse(iso[8..10])] = (sum, $"{colL}{r}");
                    if (iso == days[^1]) lastView = (Math.Max(top, r - 22), $"{colL}{r}");
                    bandEnd = Math.Max(bandEnd, r);
                }
                top = bandEnd + 2;
            }
            foreach (var rr in usedRows) ws.Row(rr).Height = dataRows.Contains(rr) ? 26.25 : 25.5;
            foreach (var c0 in new[] { 1, 6 }) for (var k = 0; k < 4; k++) ws.Column(c0 + k).Width = wid[c0][k];
            if (lastView is { } lv)
            {
                try { ws.Cell(lv.active).SetActive(); ws.SheetView.TopLeftCellAddress = ws.Cell(lv.top, 1).Address; } catch { /* chỉ mất vị trí cuộn */ }
            }
        }
        // sheet Tổng năm
        void Paint(IXLCell c, XLColor? fill)
        {
            Border(c); c.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            if (fill is not null) c.Style.Fill.BackgroundColor = fill;
        }
        for (var r = 1; r <= 35; r++) wsY.Row(r).Height = 30;
        wsY.SheetView.FreezeRows(3); wsY.SheetView.FreezeColumns(1);
        wsY.Cell("A1").Value = $"BÁO CÁO RÃ ĐÔNG : {year}";
        for (var c = 1; c <= 13; c++) { var x = wsY.Cell(1, c); Border(x); x.Style.Font.SetFontName(FN).Font.SetFontSize(15).Font.SetBold(); x.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left).Alignment.SetVertical(XLAlignmentVerticalValues.Center); }
        wsY.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFF00");
        wsY.Range("A1:D1").Merge();
        var note = wsY.Cell("E1"); note.Value = "X : Là ngày không xả hàng"; note.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        note.Style.Fill.BackgroundColor = XLColor.FromHtml("#E6B8B7");
        wsY.Range("E1:F1").Merge();
        var a2 = wsY.Cell("A2"); a2.Value = "NGÀY"; a2.Style.Font.SetFontName(FN).Font.SetFontSize(15).Font.SetBold(); Paint(a2, XLColor.FromHtml("#92D050"));
        for (var m = 1; m <= 12; m++) { var c = wsY.Cell(2, m + 1); c.Value = $"THÁNG {m}"; c.Style.Font.SetFontName(FN).Font.SetFontSize(15).Font.SetBold(); Paint(c, XLColor.FromHtml("#DAEEF3")); }
        for (var c = 1; c <= 13; c++) { var x = wsY.Cell(3, c); if (c > 1) x.Value = "NET (kg)"; x.Style.Font.SetFontName(FN).Font.SetFontSize(15).Font.SetBold(); Paint(x, XLColor.FromHtml("#FDE9D9")); }
        var todayD = now.Date;
        for (var d = 1; d <= 31; d++)
        {
            var r = d + 3; var a = wsY.Cell(r, 1); a.Value = d; a.Style.Font.SetFontName(FN).Font.SetFontSize(15).Font.SetBold(); Paint(a, XLColor.FromHtml("#92D050"));
            for (var m = 1; m <= 12; m++)
            {
                var c = wsY.Cell(r, m + 1); c.Style.Font.SetFontName(FN).Font.SetFontSize(15); Paint(c, null);
                var has = info.TryGetValue(m, out var im);
                var di = has && im!.TryGetValue(d, out var v) ? v : ((double, string)?)null;
                if (d > DateTime.DaysInMonth(year, m) || (has && di is null && new DateTime(year, m, d) < todayD))
                {
                    c.Value = "X"; c.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFC7CE"); c.Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#9C0006"));
                    continue;
                }
                if (di is { } dv) { c.FormulaA1 = $"'tháng {m}'!{dv.Item2}"; c.Style.NumberFormat.Format = NetFmt; }
            }
        }
        var tt = wsY.Cell("A35"); tt.Value = $"TỔNG {year}"; tt.Style.Font.SetFontName(FN).Font.SetFontSize(15).Font.SetBold(); Paint(tt, null);
        for (var m = 1; m <= 12; m++)
        {
            var c = wsY.Cell(35, m + 1); var L = XLHelper.GetColumnLetterFromNumber(m + 1);
            c.FormulaA1 = $"SUM({L}4:{L}34)"; c.Style.NumberFormat.Format = NetFmt; c.Style.Font.SetFontName(FN).Font.SetFontSize(15).Font.SetBold(); Paint(c, null);
        }
        wsY.Column(1).Width = 19.1; for (var c = 2; c <= 13; c++) wsY.Column(c).Width = 19.9;
        wsY.SetTabActive(); wsY.Select();
        try { wb.SaveAs(path); }
        catch (IOException e) { throw new BusinessException("Không ghi được file (file đang mở trong Excel?): " + e.Message); }
        Log("Xuất Excel năm", $"{year}: {rows.Count} dòng");
    }
}

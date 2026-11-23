using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.Modules;

/// <summary>
/// Kho dữ liệu cho các module gốc chạy trong WebView (Tồn kho An An, Tồn kho gửi).
/// Mỗi (dbKey, sheet) là một mảng dòng JSON — y như window.dbAPI của app cũ (main.js: db:read-sheet / db:write-sheet / db:write-sheets).
/// </summary>
public class ModuleStore
{
    private readonly Database _db;
    private readonly Session _session;
    private static readonly Regex SafeName = new(@"^[\p{L}\p{N} _.\-()]{1,80}$", RegexOptions.Compiled);

    public ModuleStore(Database db, Session session) { _db = db; _session = session; }

    public static void CheckName(string what, string? s)
    {
        if (string.IsNullOrEmpty(s) || !SafeName.IsMatch(s)) throw new BusinessException($"Tên {what} không hợp lệ: \"{s}\".");
    }

    /// <summary>Trả về JSON mảng dòng (chuỗi gốc đã lưu) — sheet chưa có thì "[]".</summary>
    public string ReadSheetJson(string dbKey, string sheet)
    {
        CheckName("database", dbKey); CheckName("sheet", sheet);
        using var c = _db.Open();
        return c.ExecuteScalar<string?>("SELECT data FROM module_sheets WHERE db_key=@dbKey AND sheet=@sheet", new { dbKey, sheet }) ?? "[]";
    }

    public JsonArray ReadSheet(string dbKey, string sheet) =>
        JsonNode.Parse(ReadSheetJson(dbKey, sheet)) as JsonArray ?? new JsonArray();

    public List<string> ListSheets(string dbKey)
    {
        CheckName("database", dbKey);
        using var c = _db.Open();
        return c.Query<string>("SELECT sheet FROM module_sheets WHERE db_key=@dbKey ORDER BY sheet", new { dbKey }).ToList();
    }

    // ---------------- số phiên bản (mạng LAN) ----------------
    // Mỗi lần ghi một bảng, bảng nhận số phiên bản = số thứ tự toàn cục tăng dần (lan_meta.seq). Bảng bị xóa để lại "bia mộ"
    // (sheet_tombstones) mang số phiên bản để máy khác biết mà xóa theo. Bản cũ được giữ trong sheet_history (3 bản gần nhất)
    // để gộp 3 chiều khi hai nơi cùng sửa.

    public record SheetRow(string DbKey, string Sheet, long Version, string? Data);

    /// <summary>Nội dung + số phiên bản hiện tại (bảng chưa có / đã xóa: "[]" và số của bia mộ hoặc 0).</summary>
    public (string Json, long Version) ReadVersioned(string dbKey, string sheet)
    {
        CheckName("database", dbKey); CheckName("sheet", sheet);
        using var c = _db.Open();
        var r = c.QuerySingleOrDefault<(string data, long version)?>("SELECT data, version FROM module_sheets WHERE db_key=@dbKey AND sheet=@sheet", new { dbKey, sheet });
        if (r is { } x) return (x.data, x.version);
        var t = c.ExecuteScalar<long?>("SELECT version FROM sheet_tombstones WHERE db_key=@dbKey AND sheet=@sheet", new { dbKey, sheet }) ?? 0;
        return ("[]", t);
    }

    public long CurrentSeq()
    {
        using var c = _db.Open();
        return Seq(c, null);
    }

    private static long Seq(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction? tx) =>
        Math.Max(long.TryParse(c.ExecuteScalar<string?>("SELECT v FROM lan_meta WHERE k='seq'", transaction: tx), out var m) ? m : 0,
            Math.Max(c.ExecuteScalar<long?>("SELECT MAX(version) FROM module_sheets", transaction: tx) ?? 0,
                     c.ExecuteScalar<long?>("SELECT MAX(version) FROM sheet_tombstones", transaction: tx) ?? 0));

    private static void SetSeq(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx, long v) =>
        c.Execute("INSERT INTO lan_meta(k, v) VALUES('seq', @v) ON CONFLICT(k) DO UPDATE SET v=excluded.v", new { v = v.ToString() }, tx);

    /// <summary>Ghi nhiều sheet trong 1 giao dịch. Giá trị null = xóa sheet (giống db:write-sheets cũ).
    /// <paramref name="expected"/>: số phiên bản lúc đọc — bảng đã bị nơi khác sửa thì từ chối cả lượt (không ghi đè).</summary>
    public Dictionary<string, long> WriteSheets(string dbKey, IDictionary<string, JsonArray?> sheets, IDictionary<string, long>? expected = null, string? by = null)
    {
        CheckName("database", dbKey);
        foreach (var name in sheets.Keys) CheckName("sheet", name);
        var map = sheets.ToDictionary(p => p.Key, p => p.Value is null ? null : Clean(p.Value));
        return WriteRaw(dbKey, map, expected, by);
    }

    /// <summary>Như WriteSheets nhưng dữ liệu đã là chuỗi JSON mảng dòng (dùng cho đồng bộ LAN).</summary>
    public Dictionary<string, long> WriteRaw(string dbKey, IDictionary<string, string?> sheets, IDictionary<string, long>? expected = null, string? by = null)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        if (expected is not null)
            foreach (var (name, exp) in expected)
            {
                if (!sheets.ContainsKey(name)) continue;
                var cur = c.ExecuteScalar<long?>("SELECT version FROM module_sheets WHERE db_key=@dbKey AND sheet=@name", new { dbKey, name }, tx)
                          ?? c.ExecuteScalar<long?>("SELECT version FROM sheet_tombstones WHERE db_key=@dbKey AND sheet=@name", new { dbKey, name }, tx) ?? 0;
                if (cur != exp)
                    throw new SheetConflictException(dbKey, name);
            }
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var seq = Seq(c, tx);
        var result = new Dictionary<string, long>();
        foreach (var (name, data) in sheets)
        {
            var v = ++seq;
            Put(c, tx, dbKey, name, data, v, now, by ?? _session.Username);
            result[name] = v;
        }
        SetSeq(c, tx, seq);
        tx.Commit();
        return result;
    }

    /// <summary>Máy bản sao: đặt đúng nội dung + số phiên bản nhận từ máy chủ.</summary>
    public void SetExact(string dbKey, string sheet, string? data, long version)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        Put(c, tx, dbKey, sheet, data, version, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "");
        SetSeq(c, tx, Math.Max(Seq(c, tx), version));
        tx.Commit();
    }

    private static void Put(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx, string dbKey, string name, string? data, long v, string now, string by)
    {
        var old = c.QuerySingleOrDefault<(string data, long version)?>("SELECT data, version FROM module_sheets WHERE db_key=@dbKey AND sheet=@name", new { dbKey, name }, tx);
        if (old is { } o && o.version > 0)
        {
            c.Execute("INSERT INTO sheet_history(db_key, sheet, version, data, saved_at) VALUES(@dbKey, @name, @v, @d, @now)",
                new { dbKey, name, v = o.version, d = o.data, now }, tx);
            c.Execute("""
                DELETE FROM sheet_history WHERE db_key=@dbKey AND sheet=@name AND id NOT IN
                  (SELECT id FROM sheet_history WHERE db_key=@dbKey AND sheet=@name ORDER BY version DESC LIMIT 3)
                """, new { dbKey, name }, tx);
        }
        if (data is null)
        {
            c.Execute("DELETE FROM module_sheets WHERE db_key=@dbKey AND sheet=@name", new { dbKey, name }, tx);
            c.Execute("INSERT INTO sheet_tombstones(db_key, sheet, version) VALUES(@dbKey, @name, @v) ON CONFLICT(db_key, sheet) DO UPDATE SET version=excluded.version",
                new { dbKey, name, v }, tx);
            return;
        }
        var n = JsonNode.Parse(data) is JsonArray a ? a.Count : 0;
        c.Execute(
            """
            INSERT INTO module_sheets(db_key, sheet, data, row_count, updated_at, updated_by, version)
            VALUES(@dbKey, @name, @data, @n, @now, @by, @v)
            ON CONFLICT(db_key, sheet) DO UPDATE SET data=excluded.data, row_count=excluded.row_count,
                updated_at=excluded.updated_at, updated_by=excluded.updated_by, version=excluded.version
            """,
            new { dbKey, name, data, n, now, by, v }, tx);
        c.Execute("DELETE FROM sheet_tombstones WHERE db_key=@dbKey AND sheet=@name", new { dbKey, name }, tx);
    }

    /// <summary>Các bảng (kể cả bảng đã xóa) có số phiên bản lớn hơn <paramref name="since"/>.</summary>
    public List<SheetRow> ChangesSince(long since)
    {
        using var c = _db.Open();
        var list = c.Query<(string db_key, string sheet, long version, string data)>(
            "SELECT db_key, sheet, version, data FROM module_sheets WHERE version > @since ORDER BY version", new { since })
            .Select(r => new SheetRow(r.db_key, r.sheet, r.version, r.data)).ToList();
        list.AddRange(c.Query<(string db_key, string sheet, long version)>(
            "SELECT db_key, sheet, version FROM sheet_tombstones WHERE version > @since", new { since })
            .Select(r => new SheetRow(r.db_key, r.sheet, r.version, null)));
        return list.OrderBy(r => r.Version).ToList();
    }

    /// <summary>Bản hiện tại + các bản cũ còn giữ của một bảng (mới trước). Data null = bảng đã xóa.</summary>
    public List<SheetRow> History(string dbKey, string sheet)
    {
        using var c = _db.Open();
        var list = new List<SheetRow>();
        var cur = c.QuerySingleOrDefault<(string data, long version)?>("SELECT data, version FROM module_sheets WHERE db_key=@dbKey AND sheet=@sheet", new { dbKey, sheet });
        if (cur is { } x) list.Add(new SheetRow(dbKey, sheet, x.version, x.data));
        else if (c.ExecuteScalar<long?>("SELECT version FROM sheet_tombstones WHERE db_key=@dbKey AND sheet=@sheet", new { dbKey, sheet }) is { } tv)
            list.Add(new SheetRow(dbKey, sheet, tv, null));
        list.AddRange(c.Query<(long version, string data)>("SELECT version, data FROM sheet_history WHERE db_key=@dbKey AND sheet=@sheet ORDER BY version DESC", new { dbKey, sheet })
            .Select(h => new SheetRow(dbKey, sheet, h.version, h.data)));
        return list;
    }

    /// <summary>Chuẩn hóa mảng dòng như khi ghi.</summary>
    public static string CleanRows(JsonArray rows) => Clean(rows);

    /// <summary>Mọi bảng đang có (để đồng bộ toàn bộ).</summary>
    public List<(string DbKey, string Sheet, long Version)> AllVersions()
    {
        using var c = _db.Open();
        return c.Query<(string, string, long)>("SELECT db_key, sheet, version FROM module_sheets").ToList();
    }

    /// <summary>Nội dung bảng ở phiên bản lớn nhất ≤ <paramref name="v"/> (hiện tại hoặc trong lịch sử); null = khi đó chưa có bảng.</summary>
    public string? DataAtOrBefore(string dbKey, string sheet, long v)
    {
        using var c = _db.Open();
        var cur = c.QuerySingleOrDefault<(string data, long version)?>("SELECT data, version FROM module_sheets WHERE db_key=@dbKey AND sheet=@sheet", new { dbKey, sheet });
        if (cur is { } x && x.version <= v) return x.data;
        return c.ExecuteScalar<string?>("SELECT data FROM sheet_history WHERE db_key=@dbKey AND sheet=@sheet AND version <= @v ORDER BY version DESC LIMIT 1", new { dbKey, sheet, v });
    }

    /// <summary>
    /// Giống clean() trong core/db-engine.js của app cũ: bỏ ô undefined (JSON không có), số không hữu hạn → "".
    /// System.Text.Json không chứa NaN/Infinity nên chỉ cần chuẩn hóa: dòng không phải object → {}.
    /// </summary>
    private static string Clean(JsonArray rows)
    {
        var outArr = new JsonArray();
        foreach (var r in rows)
            outArr.Add(r is JsonObject o ? o.DeepClone() : new JsonObject());
        return outArr.ToJsonString(Json.Options);
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };
}

/// <summary>
/// Cầu nối giữa trang module (JS) và C#: nhận {op, args} dạng JSON, trả kết quả dạng JSON
/// đúng hình dạng mà window.dbAPI / window.authAPI của app cũ trả về.
/// </summary>
public class ModuleBridge
{
    private readonly Session _session;
    private readonly string _logFolder;
    /// <summary>Nơi đọc / ghi bảng (máy đơn hoặc mạng LAN) — đổi được khi bật LAN.</summary>
    public ISheetSource Source { get; set; }

    public ModuleBridge(ISheetSource source, Session session, string logFolder)
    {
        Source = source; _session = session; _logFolder = logFolder;
    }

    /// <summary>Mục phân quyền tương ứng database / trang của module.</summary>
    public static string? PermKey(string dbKeyOrPage) => dbKeyOrPage switch
    {
        var x when x.Contains("ton-kho-an-an") => "m1",
        var x when x.Contains("ton-kho-gui") => "m2",
        "ton-vi-tri-an-an" => "m7",
        "so-do-vi-tri" => "m8",
        "tong-hop" => "m3",
        "bao-cao-ra-dong" => "m4",
        "bao-cao-hang-ngay" => "m5",
        "nhap-lieu" => "m6",
        _ => null,
    };

    /// <summary>Mức quyền app mới → vai trò app cũ (kla-role.js): sửa = quanly (Quản trị = quantri), xem + xuất = xuat, xem = xem.</summary>
    public static string OldRole(string role, string level) =>
        role == Roles.Admin ? "quantri" : level switch { Perm.Edit => "quanly", Perm.Export => "xuat", _ => "xem" };

    /// <summary>Ghi vào database của module cần quyền Sửa của đúng module đó; database khác chỉ Quản trị.</summary>
    public bool CanWrite(string dbKey) =>
        _session.CurrentUser is not null &&
        (PermKey(dbKey) is { } k ? _session.Can(k, Perm.Edit) : Roles.IsAdmin(_session.Role));

    private bool CanRead => _session.Can("m1") || _session.Can("m2");

    /// <summary>Xử lý một yêu cầu: chuỗi JSON {op, args:[...]} → chuỗi JSON kết quả. Không bao giờ ném lỗi ra ngoài.</summary>
    public string Handle(string requestJson)
    {
        try
        {
            var req = JsonNode.Parse(requestJson) as JsonObject ?? throw new BusinessException("Yêu cầu không hợp lệ.");
            var op = req["op"]?.GetValue<string>() ?? "";
            var args = req["args"] as JsonArray ?? new JsonArray();
            string S(int i) => args.Count > i && args[i] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
            switch (op)
            {
                case "readSheet":
                {
                    if (!CanRead) throw new BusinessException("Bạn chưa được cấp quyền xem module này.");
                    var key = S(0); var sheet = S(1);
                    var json = Source.Read(key, sheet);
                    if (key == "ton-kho-an-an" && sheet == "LoadData") json = StripTpcRows(json);
                    return "{\"ok\":true,\"data\":" + json + "}";
                }
                case "writeSheet":
                {
                    RequireWrite(S(0));
                    var rows = args.Count > 2 ? args[2] as JsonArray : null;
                    Source.Write(S(0), new Dictionary<string, JsonArray?> { [S(1)] = rows?.DeepClone().AsArray() ?? new JsonArray() });
                    return "{\"ok\":true,\"ignored\":0}";
                }
                case "writeSheets":
                {
                    RequireWrite(S(0));
                    var map = new Dictionary<string, JsonArray?>();
                    if (args.Count > 1 && args[1] is JsonObject o)
                        foreach (var (name, node) in o)
                            map[name] = node is null ? null : node as JsonArray ?? new JsonArray();
                    Source.Write(S(0), map);
                    return "{\"ok\":true,\"ignored\":0}";
                }
                case "listSheets":
                {
                    var arr = new JsonArray(Source.List(S(0)).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
                    return new JsonObject { ["ok"] = true, ["sheets"] = arr }.ToJsonString(Json.Options);
                }
                case "logError":
                {
                    WriteLog(args.Count > 0 ? args[0]?.ToJsonString(Json.Options) ?? "" : "");
                    return "{\"ok\":true}";
                }
                case "authState":
                {
                    var u = _session.CurrentUser;
                    if (u is null) return "{\"ok\":true,\"user\":null}";
                    var level = PermKey(S(0)) is { } pk ? Perm.Level(u, pk) : Perm.View;
                    var user = new JsonObject
                    {
                        ["ten"] = u.Username,
                        ["tenHienThi"] = string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName,
                        ["vaiTro"] = OldRole(u.Role, level),
                        ["canWrite"] = level == Perm.Edit,
                        ["pc"] = null,
                    };
                    return new JsonObject { ["ok"] = true, ["user"] = user }.ToJsonString(Json.Options);
                }
                default:
                    return Error($"Không hỗ trợ lệnh \"{op}\".");
            }
        }
        catch (BusinessException e) { return Error(e.Message); }
        catch (Exception e) { WriteLog("bridge: " + e); return Error(e.Message); }
    }

    private void RequireWrite(string dbKey)
    {
        if (!CanWrite(dbKey)) throw new BusinessException("Bạn không được cấp quyền sửa dữ liệu module này (cần mức \"Sửa\" trong phân quyền tài khoản).");
    }

    private static string Error(string msg) =>
        new JsonObject { ["ok"] = false, ["error"] = msg }.ToJsonString(Json.Options);

    private void WriteLog(string text)
    {
        try
        {
            Directory.CreateDirectory(_logFolder);
            File.AppendAllText(Path.Combine(_logFolder, "loi-module.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { /* không ghi được nhật ký thì bỏ qua */ }
    }

    /// <summary>
    /// Giống m1StripTpcRows() trong main.js của app cũ: khi đọc LoadData của Tồn kho An An, bỏ tiền tố "TPC-" ở cột Mã hàng
    /// (cột tìm theo headers trong LoadDataMeta, so khớp m1FuzzyNorm = "ma hang").
    /// </summary>
    private string StripTpcRows(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonArray rows || rows.Count == 0) return json;
            var meta = JsonNode.Parse(Source.Read("ton-kho-an-an", "LoadDataMeta")) as JsonArray ?? new JsonArray();
            var cell = meta.Count > 0 ? (meta[0] as JsonObject)?["data"] : null;
            if (cell is not JsonValue cv || !cv.TryGetValue<string>(out var s) || !s.StartsWith("__JSON__")) return json;
            var hs = (JsonNode.Parse(s["__JSON__".Length..]) as JsonObject)?["headers"] as JsonArray;
            if (hs is null) return json;
            var j = -1;
            for (var i = 0; i < hs.Count; i++)
                if (FuzzyNorm(hs[i] is JsonValue hv && hv.TryGetValue<string>(out var h) ? h : hs[i]?.ToString() ?? "") == "ma hang") { j = i; break; }
            if (j < 0) return json;
            var k = "c" + j;
            var re = new Regex(@"^\s*TPC-", RegexOptions.IgnoreCase);
            var changed = false;
            foreach (var r in rows)
                if (r is JsonObject o && o[k] is JsonValue v && v.TryGetValue<string>(out var str) && re.IsMatch(str))
                {
                    o[k] = re.Replace(str, "", 1); changed = true;
                }
            return changed ? rows.ToJsonString(Json.Options) : json;
        }
        catch { return json; }
    }

    /// <summary>m1FuzzyNorm của main.js: NFD, bỏ dấu, đ→d, chữ thường, dấu câu → khoảng trắng, gộp khoảng trắng.</summary>
    internal static string FuzzyNorm(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (ch >= '\u0300' && ch <= '\u036f') continue;
            sb.Append(ch is 'đ' or 'Đ' ? 'd' : ch);
        }
        var t = sb.ToString().ToLowerInvariant();
        t = Regex.Replace(t, @"[.,()/\\\-_:;]", " ");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }
}

/// <summary>Bảng vừa bị máy khác (hoặc trang khác) sửa sau lúc mình đọc: không ghi đè, báo người dùng làm mới.</summary>
public class SheetConflictException : BusinessException
{
    public string DbKey { get; }
    public string Sheet { get; }
    public SheetConflictException(string dbKey, string sheet)
        : base($"Bảng \"{sheet}\" vừa được máy khác sửa. Để không ghi đè dữ liệu của họ, thao tác này chưa được lưu — bấm Làm mới (F5) để tải dữ liệu mới rồi làm lại.")
    { DbKey = dbKey; Sheet = sheet; }
}

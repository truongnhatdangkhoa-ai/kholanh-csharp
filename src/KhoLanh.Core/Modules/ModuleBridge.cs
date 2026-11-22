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

    /// <summary>Ghi nhiều sheet trong 1 giao dịch. Giá trị null = xóa sheet (giống db:write-sheets cũ).</summary>
    public void WriteSheets(string dbKey, IDictionary<string, JsonArray?> sheets)
    {
        CheckName("database", dbKey);
        foreach (var name in sheets.Keys) CheckName("sheet", name);
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var (name, rows) in sheets)
        {
            if (rows is null)
            {
                c.Execute("DELETE FROM module_sheets WHERE db_key=@dbKey AND sheet=@name", new { dbKey, name }, tx);
                continue;
            }
            var clean = Clean(rows);
            c.Execute(
                """
                INSERT INTO module_sheets(db_key, sheet, data, row_count, updated_at, updated_by)
                VALUES(@dbKey, @name, @data, @n, @now, @user)
                ON CONFLICT(db_key, sheet) DO UPDATE SET data=excluded.data, row_count=excluded.row_count,
                    updated_at=excluded.updated_at, updated_by=excluded.updated_by
                """,
                new { dbKey, name, data = clean, n = rows.Count, now, user = _session.Username }, tx);
        }
        tx.Commit();
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
    private readonly ModuleStore _store;
    private readonly Session _session;
    private readonly string _logFolder;

    public ModuleBridge(ModuleStore store, Session session, string logFolder)
    {
        _store = store; _session = session; _logFolder = logFolder;
    }

    /// <summary>Mục phân quyền tương ứng database / trang của module.</summary>
    public static string? PermKey(string dbKeyOrPage) => dbKeyOrPage switch
    {
        var x when x.Contains("ton-kho-an-an") => "m1",
        var x when x.Contains("ton-kho-gui") => "m2",
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
                    var json = _store.ReadSheetJson(key, sheet);
                    if (key == "ton-kho-an-an" && sheet == "LoadData") json = StripTpcRows(json);
                    return "{\"ok\":true,\"data\":" + json + "}";
                }
                case "writeSheet":
                {
                    RequireWrite(S(0));
                    var rows = args.Count > 2 ? args[2] as JsonArray : null;
                    _store.WriteSheets(S(0), new Dictionary<string, JsonArray?> { [S(1)] = rows?.DeepClone().AsArray() ?? new JsonArray() });
                    return "{\"ok\":true,\"ignored\":0}";
                }
                case "writeSheets":
                {
                    RequireWrite(S(0));
                    var map = new Dictionary<string, JsonArray?>();
                    if (args.Count > 1 && args[1] is JsonObject o)
                        foreach (var (name, node) in o)
                            map[name] = node is null ? null : node as JsonArray ?? new JsonArray();
                    _store.WriteSheets(S(0), map);
                    return "{\"ok\":true,\"ignored\":0}";
                }
                case "listSheets":
                {
                    var arr = new JsonArray(_store.ListSheets(S(0)).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
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
            var meta = _store.ReadSheet("ton-kho-an-an", "LoadDataMeta");
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

using System.Text.Json.Nodes;
using Dapper;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;

namespace KhoLanh.Core.Modules;

/// <summary>
/// Chép dữ liệu 2 module (Tồn kho An An, Tồn kho gửi) từ database SQLite của app cũ (database\engine\khokhanh.db)
/// sang app mới. CHỈ ĐỌC file cũ (mở chế độ read-only) — không ghi gì vào app cũ.
/// Cách dựng dòng y hệt readRows() trong core/db-engine.js: cột theo bảng cols (ord, key), ô thiếu = "", rồi các ô thừa;
/// thứ tự dòng ORDER BY born, id; bỏ dòng đã xóa (del = 1).
/// </summary>
public class LegacyImporter
{
    private readonly ModuleStore _store;
    private readonly Session _session;

    public LegacyImporter(ModuleStore store, Session session) { _store = store; _session = session; }

    /// <summary>Các bảng được chép: toàn bộ 2 module + bảng mã hóa / ánh xạ tên của Tổng hợp (ô tìm chung, nhãn size dùng).</summary>
    public static bool Wanted(string tab) =>
        tab.StartsWith("ton-kho-an-an/") || tab.StartsWith("ton-kho-gui/") ||
        tab is "tong-hop/MaHoa" or "tong-hop/AnhXaCauHinh" or "tong-hop/AnhXaTen" or "tong-hop/M8_Meta";

    public record Result(int Sheets, int Rows, List<string> Names);

    public Result Import(string oldDbPath)
    {
        _session.RequireManager();
        if (!File.Exists(oldDbPath)) throw new BusinessException("Không tìm thấy file database của app cũ.");
        var cs = new SqliteConnectionStringBuilder { DataSource = oldDbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        var byKey = new Dictionary<string, Dictionary<string, JsonArray?>>();
        int total = 0;
        var names = new List<string>();
        using (var c = new SqliteConnection(cs))
        {
            c.Open();
            var hasTables = c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('rows','cols','sheets')");
            if (hasTables < 3) throw new BusinessException("File này không phải database của app Kho Lạnh cũ (thiếu bảng rows / cols / sheets).");
            var tabs = c.Query<string>("SELECT tab FROM sheets ORDER BY tab").Where(Wanted).ToList();
            foreach (var tab in tabs)
            {
                var keys = c.Query<string>("SELECT key FROM cols WHERE tab=@tab ORDER BY ord, key", new { tab }).ToList();
                var arr = new JsonArray();
                foreach (var cells in c.Query<string>("SELECT cells FROM rows WHERE tab=@tab AND del=0 ORDER BY born, id", new { tab }))
                    arr.Add(BuildRow(keys, cells));
                var slash = tab.IndexOf('/');
                var dbKey = tab[..slash]; var sheet = tab[(slash + 1)..];
                if (!byKey.TryGetValue(dbKey, out var m)) byKey[dbKey] = m = new Dictionary<string, JsonArray?>();
                m[sheet] = arr;
                total += arr.Count;
                names.Add($"{tab} ({arr.Count} dòng)");
            }
        }
        if (names.Count == 0) throw new BusinessException("Database app cũ chưa có dữ liệu Tồn kho An An / Tồn kho gửi.");
        foreach (var (dbKey, sheets) in byKey) _store.WriteSheets(dbKey, sheets);
        return new Result(names.Count, total, names);
    }

    /// <summary>Dựng 1 dòng như JS: { ...cột theo cols (thiếu → "") , ...ô còn lại }, rồi sắp khóa theo quy tắc object của JS.</summary>
    internal static JsonObject BuildRow(List<string> keys, string cellsJson)
    {
        var cells = JsonNode.Parse(cellsJson) as JsonObject ?? new JsonObject();
        var pairs = new List<KeyValuePair<string, JsonNode?>>();
        var seen = new HashSet<string>();
        foreach (var k in keys)
        {
            seen.Add(k);
            var v = cells.TryGetPropertyValue(k, out var n) ? n : null;
            pairs.Add(new(k, v is null ? JsonValue.Create("") : v.DeepClone()));
        }
        foreach (var (k, v) in JsOrder(cells.Select(p => p.Key).ToList()).Select(k => new KeyValuePair<string, JsonNode?>(k, cells[k])))
            if (seen.Add(k)) pairs.Add(new(k, v?.DeepClone()));
        var o = new JsonObject();
        foreach (var k in JsOrder(pairs.Select(p => p.Key).ToList()))
            o[k] = pairs.First(p => p.Key == k).Value;
        return o;
    }

    /// <summary>Thứ tự khóa của object JS: khóa là chỉ số mảng ("0", "12"…) tăng dần trước, rồi các khóa khác theo thứ tự chèn.</summary>
    internal static IEnumerable<string> JsOrder(List<string> keys)
    {
        static bool IsIndex(string k, out uint n) =>
            uint.TryParse(k, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n)
            && n != uint.MaxValue && n.ToString(System.Globalization.CultureInfo.InvariantCulture) == k;
        var idx = keys.Where(k => IsIndex(k, out _)).OrderBy(k => { IsIndex(k, out var n); return n; });
        return idx.Concat(keys.Where(k => !IsIndex(k, out _)));
    }
}

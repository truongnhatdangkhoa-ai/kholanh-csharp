using System.Text.Json.Nodes;
using Dapper;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;

namespace KhoLanh.Core.Modules;

/// <summary>
/// Chép dữ liệu MỌI module (1.79) từ database SQLite của app cũ (database\engine\khokhanh.db)
/// sang app mới. CHỈ ĐỌC file cũ (mở chế độ read-only) — không ghi gì vào app cũ.
/// Cách dựng dòng y hệt readRows() trong core/db-engine.js: cột theo bảng cols (ord, key), ô thiếu = "", rồi các ô thừa;
/// thứ tự dòng ORDER BY born, id; bỏ dòng đã xóa (del = 1).
/// </summary>
public class LegacyImporter
{
    private readonly ModuleStore _store;
    private readonly Session _session;

    public LegacyImporter(ModuleStore store, Session session) { _store = store; _session = session; }
    /// <summary>Nơi ghi (LAN: ghi lên máy chủ); null = ghi thẳng database của máy.</summary>
    public ISheetSource? Target { get; set; }

    /// <summary>
    /// Module của app cũ (khóa database = tên module, giống core/module-registry.js) — 1.79: chép TẤT CẢ module có trong khokhanh.db
    /// (trước chỉ Tồn An An / gửi / Tổng hợp / Rã đông / vị trí / sơ đồ / kiểm kê / kháng sinh nên Lịch xuất, NXT Bột/Sốt, Phiếu xuất kho… không sang).
    /// </summary>
    public static readonly (string Db, string Label)[] Modules =
    {
        ("ton-kho-an-an", "Tồn kho An An"), ("ton-kho-gui", "Tồn kho gửi"), ("ton-vi-tri-an-an", "Tồn kho theo vị trí"), ("so-do-vi-tri", "Sơ đồ kho"),
        ("nxt-bot-sot", "NXT Bột/Sốt · Vị Trí Bột"), ("lichxuat", "Lịch xuất nhập (Tổng quan, email, việc cần làm)"), ("phieu-nhap-xuat", "Phiếu xuất kho · List sản xuất"),
        ("nhap-lieu", "Nhập liệu"), ("so-lieu-kiem-ke", "Đối chiếu kiểm kê"), ("so-lieu-cuoi-ky", "Số liệu cuối kỳ"), ("in-chung-tu-kho", "In chứng từ kho"),
        ("tong-hop", "Tổng hợp · Danh mục mã hàng"), ("ket-qua-khang-sinh", "Kết quả kháng sinh"), ("bao-cao-ra-dong", "Báo cáo rã đông"),
        ("container-3d", "Container 3D"), ("doi-chieu-hang-ngay", "Đối chiếu hàng ngày"),
    };
    /// <summary>Không chép: cài đặt máy của app cũ (có mã / token riêng của app cũ) và module M04 đã bỏ.</summary>
    public static readonly string[] Skip = { "settings", "nxt-tnk-tgc" };

    public static string LabelOf(string db) => Modules.FirstOrDefault(m => m.Db == db).Label ?? db;

    /// <summary>Bảng cũ "khóa/sheet" → (database app mới, sheet); null = không chép. Kết quả kháng sinh: app cũ để trong Tổng hợp, app mới tách db riêng.</summary>
    public static (string Db, string Sheet)? MapTab(string tab)
    {
        var slash = tab.IndexOf('/');
        if (slash <= 0 || slash == tab.Length - 1) return null;
        var db = tab[..slash]; var sheet = tab[(slash + 1)..];
        if (Skip.Contains(db)) return null;
        if (tab == KhoLanh.Core.KhangSinh.KhangSinhService.LegacyTab) return (KhoLanh.Core.KhangSinh.KhangSinhService.Db, sheet);
        return (db, sheet);
    }

    public record Group(string Db, string Label, int Sheets, int Rows);
    public record Result(int Sheets, int Rows, List<string> Names);

    private static SqliteConnection Open(string oldDbPath)
    {
        if (!File.Exists(oldDbPath)) throw new BusinessException("Không tìm thấy file database của app cũ.");
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = oldDbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('rows','cols','sheets')") < 3)
        { c.Dispose(); throw new BusinessException("File này không phải database của app Kho Lạnh cũ (thiếu bảng rows / cols / sheets)."); }
        return c;
    }

    /// <summary>Xem trước: các module có trong database cũ (số bảng, số dòng) để chọn chép.</summary>
    public List<Group> Scan(string oldDbPath)
    {
        _session.Require("backup");
        using var c = Open(oldDbPath);
        var counts = c.Query<(string Tab, long N)>("SELECT s.tab, (SELECT COUNT(*) FROM rows r WHERE r.tab = s.tab AND r.del = 0) FROM sheets s").ToList();
        return counts.Select(x => (T: MapTab(x.Tab), x.N)).Where(x => x.T is not null).GroupBy(x => x.T!.Value.Db)
            .Select(g => new Group(g.Key, LabelOf(g.Key), g.Count(), (int)g.Sum(x => x.N)))
            .OrderBy(g => Array.FindIndex(Modules, m => m.Db == g.Db) is var i && i >= 0 ? i : 999).ThenBy(g => g.Db).ToList();
    }

    /// <summary>Chép các module đã chọn (<paramref name="only"/> = khóa database app mới; null = tất cả). Bảng của module đó ở app mới được thay bằng bản chép.</summary>
    public Result Import(string oldDbPath, IEnumerable<string>? only = null)
    {
        _session.Require("backup");
        var pick = only?.ToHashSet();
        var byKey = new Dictionary<string, Dictionary<string, JsonArray?>>();
        int total = 0;
        var names = new List<string>();
        using (var c = Open(oldDbPath))
        {
            foreach (var tab in c.Query<string>("SELECT tab FROM sheets ORDER BY tab"))
            {
                if (MapTab(tab) is not { } t || (pick is not null && !pick.Contains(t.Db))) continue;
                var keys = c.Query<string>("SELECT key FROM cols WHERE tab=@tab ORDER BY ord, key", new { tab }).ToList();
                var arr = new JsonArray();
                foreach (var cells in c.Query<string>("SELECT cells FROM rows WHERE tab=@tab AND del=0 ORDER BY born, id", new { tab }))
                    arr.Add(BuildRow(keys, cells));
                if (!byKey.TryGetValue(t.Db, out var m)) byKey[t.Db] = m = new Dictionary<string, JsonArray?>();
                m[t.Sheet] = arr;
                total += arr.Count;
                names.Add($"{tab} ({arr.Count} dòng)");
            }
        }
        if (names.Count == 0) throw new BusinessException("Database app cũ chưa có dữ liệu của các module đã chọn.");
        foreach (var (dbKey, sheets) in byKey) (Target ?? new LocalSheetSource(_store)).Write(dbKey, sheets);
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

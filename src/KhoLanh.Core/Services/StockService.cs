using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Models;

namespace KhoLanh.Core.Services;

/// <summary>Tồn kho, sổ kho, báo cáo nhập xuất tồn và số liệu trang tổng quan.</summary>
public class StockService
{
    private readonly Database _db;
    public StockService(Database db) { _db = db; }

    private const string SignedQty = "CASE v.type WHEN 'N' THEN l.quantity ELSE -l.quantity END";
    private const string SignedKg = "CASE v.type WHEN 'N' THEN l.weight ELSE -l.weight END";

    /// <summary>Tồn chi tiết theo chủ hàng + mã + size + lô + vị trí (chỉ các dòng còn hàng).</summary>
    public List<StockItem> Stock(long? ownerId = null, string? search = null, string? location = null)
    {
        using var c = _db.Open();
        var like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        var loc = string.IsNullOrWhiteSpace(location) ? null : $"{location.Trim()}%";
        return c.Query<StockItem>(
            $"""
            SELECT v.owner_id, o.name AS owner_name, l.product_id, p.code AS product_code, p.name AS product_name, p.unit,
                   l.size, l.lot, l.location,
                   SUM({SignedQty}) AS quantity, SUM({SignedKg}) AS weight,
                   MIN(CASE v.type WHEN 'N' THEN v.date END) AS first_in
            FROM voucher_lines l
            JOIN vouchers v ON v.id = l.voucher_id
            JOIN products p ON p.id = l.product_id
            JOIN owners o ON o.id = v.owner_id
            WHERE v.cancelled = 0
              AND (@ownerId IS NULL OR v.owner_id = @ownerId)
              AND (@like IS NULL OR p.code LIKE @like OR p.name LIKE @like OR l.lot LIKE @like OR l.size LIKE @like)
              AND (@loc IS NULL OR l.location LIKE @loc)
            GROUP BY v.owner_id, l.product_id, l.size, l.lot, l.location
            HAVING ABS(SUM({SignedQty})) > 1e-9 OR ABS(SUM({SignedKg})) > 1e-6
            ORDER BY p.code, l.size, l.lot, l.location
            """, new { ownerId, like, loc }).ToList();
    }

    /// <summary>Tồn tổng theo mã hàng (mọi chủ hàng), gồm cả mã đang dùng nhưng hết hàng.</summary>
    public List<ProductStock> ProductTotals(string? search = null)
    {
        using var c = _db.Open();
        var like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        return c.Query<ProductStock>(
            $"""
            SELECT p.id AS product_id, p.code AS product_code, p.name AS product_name, p.unit, p.min_stock,
                   COALESCE(SUM(CASE WHEN v.id IS NULL THEN 0.0 ELSE {SignedQty} END), 0.0) AS quantity,
                   COALESCE(SUM(CASE WHEN v.id IS NULL THEN 0.0 ELSE {SignedKg} END), 0.0) AS weight
            FROM products p
            LEFT JOIN voucher_lines l ON l.product_id = p.id
            LEFT JOIN vouchers v ON v.id = l.voucher_id AND v.cancelled = 0
            WHERE p.is_active = 1 AND (@like IS NULL OR p.code LIKE @like OR p.name LIKE @like)
            GROUP BY p.id
            ORDER BY p.code
            """, new { like }).ToList();
    }

    /// <summary>
    /// Sổ kho: từng dòng phiếu trong khoảng ngày, kèm tồn lũy kế (tính từ tồn đầu kỳ của cùng bộ lọc).
    /// </summary>
    public (double openQty, double openKg, List<LedgerEntry> entries) Ledger(DateTime from, DateTime to,
        long? productId = null, long? ownerId = null, string? lot = null)
    {
        using var c = _db.Open();
        var lotLike = string.IsNullOrWhiteSpace(lot) ? null : $"%{lot.Trim()}%";
        var args = new { from = Database.D(from), to = Database.D(to), productId, ownerId, lotLike };
        const string filter = """
            v.cancelled = 0
              AND (@productId IS NULL OR l.product_id = @productId)
              AND (@ownerId IS NULL OR v.owner_id = @ownerId)
              AND (@lotLike IS NULL OR l.lot LIKE @lotLike)
            """;
        var open = c.QuerySingle<(double, double)>(
            $"""
            SELECT COALESCE(SUM({SignedQty}),0.0), COALESCE(SUM({SignedKg}),0.0)
            FROM voucher_lines l JOIN vouchers v ON v.id=l.voucher_id
            WHERE {filter} AND v.date < @from
            """, args);
        var rows = c.Query<LedgerEntry>(
            $"""
            SELECT v.date, v.no AS voucher_no, v.type, o.name AS owner_name, p.code AS product_code, p.name AS product_name,
                   l.size, l.lot, l.location, v.partner,
                   CASE v.type WHEN 'N' THEN l.quantity ELSE 0.0 END AS in_quantity,
                   CASE v.type WHEN 'X' THEN l.quantity ELSE 0.0 END AS out_quantity,
                   CASE v.type WHEN 'N' THEN l.weight ELSE 0.0 END AS in_weight,
                   CASE v.type WHEN 'X' THEN l.weight ELSE 0.0 END AS out_weight
            FROM voucher_lines l
            JOIN vouchers v ON v.id = l.voucher_id
            JOIN products p ON p.id = l.product_id
            JOIN owners o ON o.id = v.owner_id
            WHERE {filter} AND v.date BETWEEN @from AND @to
            ORDER BY v.date, v.type, v.no, l.id
            """, args).ToList();
        double q = open.Item1, k = open.Item2;
        foreach (var r in rows)
        {
            q += r.InQuantity - r.OutQuantity;
            k += r.InWeight - r.OutWeight;
            r.BalanceQuantity = q;
            r.BalanceWeight = k;
        }
        return (open.Item1, open.Item2, rows);
    }

    /// <summary>Báo cáo nhập – xuất – tồn theo mã hàng + size trong khoảng ngày.</summary>
    public List<NxtRow> Nxt(DateTime from, DateTime to, long? ownerId = null, bool hideEmpty = true)
    {
        using var c = _db.Open();
        var rows = c.Query<NxtRow>(
            $"""
            SELECT p.code AS product_code, p.name AS product_name, l.size, p.unit,
                SUM(CASE WHEN v.date < @from THEN {SignedQty} ELSE 0.0 END) AS open_quantity,
                SUM(CASE WHEN v.date < @from THEN {SignedKg} ELSE 0.0 END) AS open_weight,
                SUM(CASE WHEN v.date >= @from AND v.type='N' THEN l.quantity ELSE 0.0 END) AS in_quantity,
                SUM(CASE WHEN v.date >= @from AND v.type='N' THEN l.weight ELSE 0.0 END) AS in_weight,
                SUM(CASE WHEN v.date >= @from AND v.type='X' THEN l.quantity ELSE 0.0 END) AS out_quantity,
                SUM(CASE WHEN v.date >= @from AND v.type='X' THEN l.weight ELSE 0.0 END) AS out_weight
            FROM voucher_lines l
            JOIN vouchers v ON v.id = l.voucher_id
            JOIN products p ON p.id = l.product_id
            WHERE v.cancelled = 0 AND v.date <= @to AND (@ownerId IS NULL OR v.owner_id = @ownerId)
            GROUP BY l.product_id, l.size
            ORDER BY p.code, l.size
            """, new { from = Database.D(from), to = Database.D(to), ownerId }).ToList();
        if (hideEmpty)
            rows = rows.Where(r => Math.Abs(r.OpenQuantity) > 1e-9 || r.InQuantity > 0 || r.OutQuantity > 0).ToList();
        return rows;
    }

    public DashboardInfo Dashboard()
    {
        using var c = _db.Open();
        var today = Database.D(DateTime.Today);
        var info = new DashboardInfo
        {
            ProductCount = c.ExecuteScalar<int>("SELECT COUNT(*) FROM products WHERE is_active=1"),
            OwnerCount = c.ExecuteScalar<int>("SELECT COUNT(*) FROM owners WHERE is_active=1"),
        };
        var tot = c.QuerySingle<(double, double)>(
            $"SELECT COALESCE(SUM({SignedQty}),0.0), COALESCE(SUM({SignedKg}),0.0) FROM voucher_lines l JOIN vouchers v ON v.id=l.voucher_id WHERE v.cancelled=0");
        info.TotalQuantity = tot.Item1; info.TotalWeight = tot.Item2;
        var td = c.QuerySingle<(long, long, double, double)>(
            """
            SELECT
              (SELECT COUNT(*) FROM vouchers WHERE cancelled=0 AND date=@today AND type='N'),
              (SELECT COUNT(*) FROM vouchers WHERE cancelled=0 AND date=@today AND type='X'),
              (SELECT COALESCE(SUM(l.quantity),0.0) FROM voucher_lines l JOIN vouchers v ON v.id=l.voucher_id WHERE v.cancelled=0 AND v.date=@today AND v.type='N'),
              (SELECT COALESCE(SUM(l.quantity),0.0) FROM voucher_lines l JOIN vouchers v ON v.id=l.voucher_id WHERE v.cancelled=0 AND v.date=@today AND v.type='X')
            """, new { today });
        info.TodayInCount = (int)td.Item1; info.TodayOutCount = (int)td.Item2;
        info.TodayInQuantity = td.Item3; info.TodayOutQuantity = td.Item4;
        info.LowStock = ProductTotals().Where(p => p.IsLow).ToList();
        info.Recent = c.Query<VoucherSummary>(
            """
            SELECT v.id, v.no, v.type, v.date, o.name AS owner_name, v.partner, v.note, v.created_by, v.cancelled,
                   COUNT(l.id) AS line_count, COALESCE(SUM(l.quantity),0.0) AS total_quantity, COALESCE(SUM(l.weight),0.0) AS total_weight
            FROM vouchers v JOIN owners o ON o.id=v.owner_id LEFT JOIN voucher_lines l ON l.voucher_id=v.id
            GROUP BY v.id ORDER BY v.id DESC LIMIT 10
            """).ToList();
        return info;
    }

    /// <summary>Gợi ý vị trí / lô đã từng dùng.</summary>
    public List<string> DistinctLocations()
    {
        using var c = _db.Open();
        return c.Query<string>("SELECT DISTINCT location FROM voucher_lines WHERE location<>'' ORDER BY location").ToList();
    }
}

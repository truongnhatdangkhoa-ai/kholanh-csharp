using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Models;
using Microsoft.Data.Sqlite;

namespace KhoLanh.Core.Services;

/// <summary>Phiếu nhập / phiếu xuất. Phiếu đã lưu không sửa; sai thì hủy và lập lại (có ghi nhật ký).</summary>
public class VoucherService
{
    private readonly Database _db;
    private readonly Session _session;
    private readonly AuditService _audit;

    public VoucherService(Database db, Session session, AuditService audit)
    {
        _db = db; _session = session; _audit = audit;
    }

    public const double Epsilon = 1e-9;

    public static string Prefix(string type) => type == VoucherTypes.In ? "PN" : "PX";

    public string PreviewNumber(string type, DateTime date)
    {
        using var c = _db.Open();
        return NextNumber(c, null, type, date);
    }

    private static string NextNumber(SqliteConnection c, SqliteTransaction? tx, string type, DateTime date)
    {
        var head = $"{Prefix(type)}{date:yyMMdd}-";
        var last = c.ExecuteScalar<string?>(
            "SELECT no FROM vouchers WHERE no LIKE @p ORDER BY no DESC LIMIT 1", new { p = head + "%" }, tx);
        var seq = 1;
        if (last is not null && int.TryParse(last[head.Length..], out var n)) seq = n + 1;
        return $"{head}{seq:000}";
    }

    public Voucher Create(Voucher v)
    {
        _session.RequireLogin();
        if (v.Type is not (VoucherTypes.In or VoucherTypes.Out)) throw new BusinessException("Loại phiếu không hợp lệ.");
        if (v.OwnerId == 0) throw new BusinessException("Chưa chọn chủ hàng.");
        var lines = v.Lines.Where(l => l.ProductId != 0 || l.Quantity != 0).ToList();
        if (lines.Count == 0) throw new BusinessException("Phiếu chưa có dòng hàng nào.");
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            l.Size = l.Size.Trim(); l.Lot = l.Lot.Trim(); l.Location = l.Location.Trim().ToUpperInvariant(); l.Note = l.Note.Trim();
            if (l.ProductId == 0) throw new BusinessException($"Dòng {i + 1}: chưa chọn mã hàng.");
            if (l.Quantity <= 0) throw new BusinessException($"Dòng {i + 1}: số lượng phải lớn hơn 0.");
            if (l.Weight < 0) throw new BusinessException($"Dòng {i + 1}: số kg không được âm.");
        }

        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM owners WHERE id=@OwnerId", v, tx) == 0)
            throw new BusinessException("Chủ hàng không tồn tại.");

        if (v.Type == VoucherTypes.Out)
        {
            // Gộp các dòng cùng khóa tồn rồi so với tồn hiện có.
            foreach (var g in lines.GroupBy(l => (l.ProductId, l.Size, l.Lot, l.Location)))
            {
                var (qty, kg) = Balance(c, tx, v.OwnerId, g.Key.ProductId, g.Key.Size, g.Key.Lot, g.Key.Location);
                var needQty = g.Sum(x => x.Quantity);
                var needKg = g.Sum(x => x.Weight);
                if (needQty > qty + Epsilon || needKg > kg + 0.001)
                {
                    var code = c.ExecuteScalar<string>("SELECT code FROM products WHERE id=@id", new { id = g.Key.ProductId }, tx);
                    throw new BusinessException(
                        $"Không đủ tồn để xuất {code} (size \"{g.Key.Size}\", lô \"{g.Key.Lot}\", vị trí \"{g.Key.Location}\"): " +
                        $"tồn {qty:#,0.##} / {kg:#,0.##} kg, cần xuất {needQty:#,0.##} / {needKg:#,0.##} kg.");
                }
            }
        }

        v.No = NextNumber(c, tx, v.Type, v.Date);
        v.CreatedBy = _session.Username;
        v.CreatedAt = DateTime.Now;
        v.Cancelled = false;
        v.Id = c.ExecuteScalar<long>(
            """
            INSERT INTO vouchers(no, type, date, owner_id, partner, vehicle, note, created_by, created_at)
            VALUES(@No, @Type, @DateS, @OwnerId, @Partner, @Vehicle, @Note, @CreatedBy, @CreatedAtS);
            SELECT last_insert_rowid();
            """,
            new
            {
                v.No, v.Type, DateS = Database.D(v.Date), v.OwnerId, Partner = v.Partner.Trim(), Vehicle = v.Vehicle.Trim(),
                Note = v.Note.Trim(), v.CreatedBy, CreatedAtS = v.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            }, tx);
        foreach (var l in lines)
        {
            l.VoucherId = v.Id;
            l.Id = c.ExecuteScalar<long>(
                """
                INSERT INTO voucher_lines(voucher_id, product_id, size, lot, location, quantity, weight, note)
                VALUES(@VoucherId, @ProductId, @Size, @Lot, @Location, @Quantity, @Weight, @Note);
                SELECT last_insert_rowid();
                """, l, tx);
        }
        v.Lines = lines;
        _audit.Log(c, tx, _session.Username, $"Lập phiếu {VoucherTypes.Label(v.Type).ToLower()}",
            $"{v.No}: {lines.Count} dòng, {lines.Sum(l => l.Quantity):#,0.##} đơn vị");
        tx.Commit();
        return v;
    }

    public void Cancel(long id, string reason)
    {
        _session.RequireManager();
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessException("Cần ghi lý do hủy phiếu.");
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        var v = c.QuerySingleOrDefault<Voucher>("SELECT * FROM vouchers WHERE id=@id", new { id }, tx)
                ?? throw new BusinessException("Không tìm thấy phiếu.");
        if (v.Cancelled) throw new BusinessException($"Phiếu {v.No} đã hủy trước đó.");

        if (v.Type == VoucherTypes.In)
        {
            // Hủy phiếu nhập không được làm tồn âm (hàng đã xuất bớt).
            var lines = c.Query<VoucherLine>("SELECT * FROM voucher_lines WHERE voucher_id=@id", new { id }, tx);
            foreach (var g in lines.GroupBy(l => (l.ProductId, l.Size, l.Lot, l.Location)))
            {
                var (qty, kg) = Balance(c, tx, v.OwnerId, g.Key.ProductId, g.Key.Size, g.Key.Lot, g.Key.Location);
                if (qty - g.Sum(x => x.Quantity) < -Epsilon || kg - g.Sum(x => x.Weight) < -0.001)
                {
                    var code = c.ExecuteScalar<string>("SELECT code FROM products WHERE id=@id", new { id = g.Key.ProductId }, tx);
                    throw new BusinessException(
                        $"Không thể hủy {v.No}: hàng {code} (lô \"{g.Key.Lot}\", vị trí \"{g.Key.Location}\") đã được xuất bớt. Hủy các phiếu xuất liên quan trước.");
                }
            }
        }
        c.Execute("UPDATE vouchers SET cancelled=1, cancel_reason=@reason WHERE id=@id", new { id, reason = reason.Trim() }, tx);
        _audit.Log(c, tx, _session.Username, "Hủy phiếu", $"{v.No}: {reason.Trim()}");
        tx.Commit();
    }

    internal static (double qty, double kg) Balance(SqliteConnection c, SqliteTransaction? tx,
        long ownerId, long productId, string size, string lot, string location)
    {
        var r = c.QuerySingle<(double, double)>(
            """
            SELECT COALESCE(SUM(CASE v.type WHEN 'N' THEN l.quantity ELSE -l.quantity END), 0.0),
                   COALESCE(SUM(CASE v.type WHEN 'N' THEN l.weight ELSE -l.weight END), 0.0)
            FROM voucher_lines l JOIN vouchers v ON v.id = l.voucher_id
            WHERE v.cancelled = 0 AND v.owner_id=@ownerId AND l.product_id=@productId
              AND l.size=@size AND l.lot=@lot AND l.location=@location
            """, new { ownerId, productId, size, lot, location }, tx);
        return r;
    }

    public List<VoucherSummary> List(DateTime from, DateTime to, string? type = null, long? ownerId = null,
        string? search = null, bool includeCancelled = true)
    {
        using var c = _db.Open();
        var like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        return c.Query<VoucherSummary>(
            """
            SELECT v.id, v.no, v.type, v.date, o.name AS owner_name, v.partner, v.note, v.created_by, v.cancelled,
                   COUNT(l.id) AS line_count, COALESCE(SUM(l.quantity),0.0) AS total_quantity, COALESCE(SUM(l.weight),0.0) AS total_weight
            FROM vouchers v
            JOIN owners o ON o.id = v.owner_id
            LEFT JOIN voucher_lines l ON l.voucher_id = v.id
            WHERE v.date BETWEEN @from AND @to
              AND (@type IS NULL OR v.type = @type)
              AND (@ownerId IS NULL OR v.owner_id = @ownerId)
              AND (@inc = 1 OR v.cancelled = 0)
              AND (@like IS NULL OR v.no LIKE @like OR v.partner LIKE @like OR v.note LIKE @like OR v.vehicle LIKE @like
                   OR EXISTS (SELECT 1 FROM voucher_lines x JOIN products p ON p.id=x.product_id
                              WHERE x.voucher_id=v.id AND (p.code LIKE @like OR p.name LIKE @like OR x.lot LIKE @like)))
            GROUP BY v.id
            ORDER BY v.date DESC, v.no DESC
            """,
            new { from = Database.D(from), to = Database.D(to), type, ownerId, like, inc = includeCancelled ? 1 : 0 }).ToList();
    }

    public (Voucher voucher, Owner owner, List<VoucherLineView> lines) Get(long id)
    {
        using var c = _db.Open();
        var v = c.QuerySingleOrDefault<Voucher>("SELECT * FROM vouchers WHERE id=@id", new { id })
                ?? throw new BusinessException("Không tìm thấy phiếu.");
        var o = c.QuerySingle<Owner>("SELECT * FROM owners WHERE id=@OwnerId", v);
        var lines = c.Query<VoucherLineView>(
            """
            SELECT l.product_id, p.code AS product_code, p.name AS product_name, p.unit, l.size, l.lot, l.location,
                   l.quantity, l.weight, l.note
            FROM voucher_lines l JOIN products p ON p.id = l.product_id
            WHERE l.voucher_id=@id ORDER BY l.id
            """, new { id }).ToList();
        return (v, o, lines);
    }
}

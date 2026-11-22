using ClosedXML.Excel;
using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Models;

namespace KhoLanh.Core.Services;

/// <summary>Danh mục mã hàng và chủ hàng.</summary>
public class CatalogService
{
    private readonly Database _db;
    private readonly Session _session;
    private readonly AuditService _audit;

    public CatalogService(Database db, Session session, AuditService audit)
    {
        _db = db; _session = session; _audit = audit;
    }

    /// <summary>Tạo chủ hàng mặc định "Hàng của kho" nếu chưa có chủ hàng nào.</summary>
    public void EnsureDefaults()
    {
        using var c = _db.Open();
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM owners") == 0)
            c.Execute("INSERT INTO owners(code, name, note) VALUES('KHO', 'Hàng của kho', 'Chủ hàng mặc định')");
    }

    // ---------------- Mã hàng ----------------

    public List<Product> ListProducts(string? search = null, bool includeInactive = false)
    {
        using var c = _db.Open();
        var like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        return c.Query<Product>(
            """
            SELECT * FROM products
            WHERE (@all = 1 OR is_active = 1)
              AND (@like IS NULL OR code LIKE @like OR name LIKE @like OR category LIKE @like)
            ORDER BY code
            """, new { like, all = includeInactive ? 1 : 0 }).ToList();
    }

    public Product SaveProduct(Product p)
    {
        _session.RequireManager();
        p.Code = p.Code.Trim().ToUpperInvariant();
        p.Name = p.Name.Trim();
        if (p.Code.Length == 0) throw new BusinessException("Chưa nhập mã hàng.");
        if (p.Name.Length == 0) throw new BusinessException("Chưa nhập tên hàng.");
        if (p.KgPerUnit < 0 || p.MinStock < 0) throw new BusinessException("Kg/đơn vị và tồn tối thiểu không được âm.");
        p.Sizes = string.Join(", ", p.SizeList);
        using var c = _db.Open();
        var dup = c.ExecuteScalar<long>("SELECT COUNT(*) FROM products WHERE code=@Code AND id<>@Id", p);
        if (dup > 0) throw new BusinessException($"Mã hàng \"{p.Code}\" đã tồn tại.");
        if (p.Id == 0)
        {
            p.Id = c.ExecuteScalar<long>(
                """
                INSERT INTO products(code, name, category, unit, kg_per_unit, sizes, min_stock, note, is_active)
                VALUES(@Code, @Name, @Category, @Unit, @KgPerUnit, @Sizes, @MinStock, @Note, @IsActive);
                SELECT last_insert_rowid();
                """, p);
            _audit.Log(c, null, _session.Username, "Thêm mã hàng", p.Display);
        }
        else
        {
            c.Execute(
                """
                UPDATE products SET code=@Code, name=@Name, category=@Category, unit=@Unit, kg_per_unit=@KgPerUnit,
                    sizes=@Sizes, min_stock=@MinStock, note=@Note, is_active=@IsActive WHERE id=@Id
                """, p);
            _audit.Log(c, null, _session.Username, "Sửa mã hàng", p.Display);
        }
        return p;
    }

    /// <summary>Xóa mã hàng chưa phát sinh phiếu; đã có phiếu thì chỉ ngừng dùng.</summary>
    public string DeleteProduct(long id)
    {
        _session.RequireManager();
        using var c = _db.Open();
        var p = c.QuerySingleOrDefault<Product>("SELECT * FROM products WHERE id=@id", new { id })
                ?? throw new BusinessException("Không tìm thấy mã hàng.");
        var used = c.ExecuteScalar<long>("SELECT COUNT(*) FROM voucher_lines WHERE product_id=@id", new { id }) > 0;
        if (used)
        {
            c.Execute("UPDATE products SET is_active=0 WHERE id=@id", new { id });
            _audit.Log(c, null, _session.Username, "Ngừng dùng mã hàng", p.Display);
            return $"Mã {p.Code} đã có phiếu nên chỉ chuyển sang ngừng dùng (dữ liệu cũ vẫn giữ).";
        }
        c.Execute("DELETE FROM products WHERE id=@id", new { id });
        _audit.Log(c, null, _session.Username, "Xóa mã hàng", p.Display);
        return $"Đã xóa mã {p.Code}.";
    }

    /// <summary>
    /// Nạp mã hàng từ Excel. Dòng 1 là tiêu đề; cột theo thứ tự:
    /// Mã hàng | Tên hàng | Nhóm | Đơn vị | Kg/đơn vị | Size | Tồn tối thiểu | Ghi chú.
    /// Mã đã có thì cập nhật, mã mới thì thêm.
    /// </summary>
    public (int added, int updated, List<string> errors) ImportProducts(string xlsxPath)
    {
        _session.RequireManager();
        var errors = new List<string>();
        int added = 0, updated = 0;
        using var wb = new XLWorkbook(xlsxPath);
        var ws = wb.Worksheets.First();
        var existing = ListProducts(includeInactive: true).ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        // Tìm dòng tiêu đề (ô đầu bắt đầu bằng "Mã") trong 10 dòng đầu; không thấy thì coi dòng 1 là tiêu đề.
        // Nhờ vậy file "Xuất Excel" của chính phần mềm cũng nạp lại được.
        var headerRow = ws.RowsUsed().Take(10)
            .FirstOrDefault(r => r.Cell(1).GetString().Trim().StartsWith("Mã", StringComparison.OrdinalIgnoreCase))?.RowNumber() ?? 1;
        foreach (var row in ws.RowsUsed().Where(r => r.RowNumber() > headerRow))
        {
            var code = row.Cell(1).GetString().Trim();
            if (code.Length == 0) continue;
            try
            {
                var p = existing.TryGetValue(code, out var ex) ? ex : new Product { Code = code };
                p.Name = row.Cell(2).GetString().Trim();
                if (!row.Cell(3).IsEmpty()) p.Category = row.Cell(3).GetString().Trim();
                if (!row.Cell(4).IsEmpty()) p.Unit = row.Cell(4).GetString().Trim();
                if (!row.Cell(5).IsEmpty()) p.KgPerUnit = ReadNumber(row.Cell(5));
                if (!row.Cell(6).IsEmpty()) p.Sizes = row.Cell(6).GetString();
                if (!row.Cell(7).IsEmpty()) p.MinStock = ReadNumber(row.Cell(7));
                if (!row.Cell(8).IsEmpty()) p.Note = row.Cell(8).GetString().Trim();
                p.IsActive = true;
                var isNew = p.Id == 0;
                SaveProduct(p);
                existing[p.Code] = p;
                if (isNew) added++; else updated++;
            }
            catch (Exception e)
            {
                errors.Add($"Dòng {row.RowNumber()} ({code}): {e.Message}");
            }
        }
        return (added, updated, errors);
    }

    private static double ReadNumber(IXLCell cell)
    {
        if (cell.TryGetValue<double>(out var d)) return d;
        var s = cell.GetString().Trim().Replace(",", ".");
        return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d)
            ? d : throw new BusinessException($"\"{cell.GetString()}\" không phải số.");
    }

    // ---------------- Chủ hàng ----------------

    public List<Owner> ListOwners(string? search = null, bool includeInactive = false)
    {
        using var c = _db.Open();
        var like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        return c.Query<Owner>(
            """
            SELECT * FROM owners
            WHERE (@all = 1 OR is_active = 1)
              AND (@like IS NULL OR code LIKE @like OR name LIKE @like OR phone LIKE @like)
            ORDER BY code
            """, new { like, all = includeInactive ? 1 : 0 }).ToList();
    }

    public Owner SaveOwner(Owner o)
    {
        _session.RequireManager();
        o.Code = o.Code.Trim().ToUpperInvariant();
        o.Name = o.Name.Trim();
        if (o.Code.Length == 0) throw new BusinessException("Chưa nhập mã chủ hàng.");
        if (o.Name.Length == 0) throw new BusinessException("Chưa nhập tên chủ hàng.");
        using var c = _db.Open();
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM owners WHERE code=@Code AND id<>@Id", o) > 0)
            throw new BusinessException($"Mã chủ hàng \"{o.Code}\" đã tồn tại.");
        if (o.Id == 0)
        {
            o.Id = c.ExecuteScalar<long>(
                "INSERT INTO owners(code, name, phone, address, note, is_active) VALUES(@Code, @Name, @Phone, @Address, @Note, @IsActive); SELECT last_insert_rowid();", o);
            _audit.Log(c, null, _session.Username, "Thêm chủ hàng", o.Display);
        }
        else
        {
            c.Execute("UPDATE owners SET code=@Code, name=@Name, phone=@Phone, address=@Address, note=@Note, is_active=@IsActive WHERE id=@Id", o);
            _audit.Log(c, null, _session.Username, "Sửa chủ hàng", o.Display);
        }
        return o;
    }

    public string DeleteOwner(long id)
    {
        _session.RequireManager();
        using var c = _db.Open();
        var o = c.QuerySingleOrDefault<Owner>("SELECT * FROM owners WHERE id=@id", new { id })
                ?? throw new BusinessException("Không tìm thấy chủ hàng.");
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM vouchers WHERE owner_id=@id", new { id }) > 0)
        {
            c.Execute("UPDATE owners SET is_active=0 WHERE id=@id", new { id });
            _audit.Log(c, null, _session.Username, "Ngừng dùng chủ hàng", o.Display);
            return $"Chủ hàng {o.Code} đã có phiếu nên chỉ chuyển sang ngừng dùng.";
        }
        c.Execute("DELETE FROM owners WHERE id=@id", new { id });
        _audit.Log(c, null, _session.Username, "Xóa chủ hàng", o.Display);
        return $"Đã xóa chủ hàng {o.Code}.";
    }
}

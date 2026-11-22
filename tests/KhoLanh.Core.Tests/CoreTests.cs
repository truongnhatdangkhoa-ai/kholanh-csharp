using KhoLanh.Core;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Xunit;

namespace KhoLanh.Core.Tests;

public class CoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-test-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private readonly long _owner;
    private readonly Product _ca;

    public CoreTests()
    {
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
        _owner = _s.Catalog.ListOwners().Single().Id;
        _ca = _s.Catalog.SaveProduct(new Product { Code = "ca-basa", Name = "Cá basa phi lê", KgPerUnit = 10, Sizes = "120-170, 170-220", MinStock = 5 });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private Voucher In(double qty, double kg, string lot = "L1", string loc = "A1", DateTime? date = null) =>
        _s.Vouchers.Create(new Voucher
        {
            Type = VoucherTypes.In, Date = date ?? DateTime.Today, OwnerId = _owner, Partner = "Xưởng",
            Lines = { new VoucherLine { ProductId = _ca.Id, Size = "120-170", Lot = lot, Location = loc, Quantity = qty, Weight = kg } },
        });

    private Voucher Out(double qty, double kg, string lot = "L1", string loc = "A1", DateTime? date = null) =>
        _s.Vouchers.Create(new Voucher
        {
            Type = VoucherTypes.Out, Date = date ?? DateTime.Today, OwnerId = _owner, Partner = "Khách",
            Lines = { new VoucherLine { ProductId = _ca.Id, Size = "120-170", Lot = lot, Location = loc, Quantity = qty, Weight = kg } },
        });

    [Fact]
    public void Login_rejects_wrong_password()
    {
        _s.Auth.Logout();
        var e = Assert.Throws<BusinessException>(() => _s.Auth.Login("admin", "sai"));
        Assert.Contains("Sai", e.Message);
        Assert.Null(_s.Session.CurrentUser);
    }

    [Fact]
    public void First_admin_only_once()
    {
        Assert.Throws<BusinessException>(() => _s.Auth.CreateFirstAdmin("admin2", "", "matkhau2"));
    }

    [Fact]
    public void Product_code_is_normalised_and_unique()
    {
        Assert.Equal("CA-BASA", _ca.Code);
        Assert.Equal("120-170, 170-220", _ca.Sizes);
        Assert.Throws<BusinessException>(() => _s.Catalog.SaveProduct(new Product { Code = "Ca-Basa", Name = "x" }));
    }

    [Fact]
    public void Voucher_numbers_increase_per_day()
    {
        var a = In(10, 100);
        var b = In(5, 50);
        Assert.Equal($"PN{DateTime.Today:yyMMdd}-001", a.No);
        Assert.Equal($"PN{DateTime.Today:yyMMdd}-002", b.No);
        Assert.Equal($"PX{DateTime.Today:yyMMdd}-001", Out(1, 10).No);
    }

    [Fact]
    public void Stock_is_in_minus_out_per_lot_and_location()
    {
        In(10, 100, "L1", "a1");
        In(4, 40, "L2", "B2");
        Out(3, 30, "L1", "A1");
        var stock = _s.Stock.Stock();
        Assert.Equal(2, stock.Count);
        var l1 = stock.Single(x => x.Lot == "L1");
        Assert.Equal("A1", l1.Location); // vị trí được viết hoa
        Assert.Equal(7, l1.Quantity);
        Assert.Equal(70, l1.Weight);
        Assert.Equal(11, _s.Stock.ProductTotals().Single().Quantity);
    }

    [Fact]
    public void Cannot_export_more_than_stock()
    {
        In(5, 50);
        var e = Assert.Throws<BusinessException>(() => Out(6, 60));
        Assert.Contains("Không đủ tồn", e.Message);
        Assert.Throws<BusinessException>(() => Out(1, 10, lot: "KHAC"));
        Out(5, 50);
        Assert.Empty(_s.Stock.Stock());
    }

    [Fact]
    public void Cancelled_voucher_no_longer_counts()
    {
        var v = In(10, 100);
        _s.Vouchers.Cancel(v.Id, "Nhập nhầm");
        Assert.Empty(_s.Stock.Stock());
        Assert.Throws<BusinessException>(() => _s.Vouchers.Cancel(v.Id, "lại"));
    }

    [Fact]
    public void Cannot_cancel_import_already_exported()
    {
        var v = In(10, 100);
        var x = Out(8, 80);
        var e = Assert.Throws<BusinessException>(() => _s.Vouchers.Cancel(v.Id, "thử"));
        Assert.Contains("đã được xuất", e.Message);
        _s.Vouchers.Cancel(x.Id, "xuất nhầm");
        _s.Vouchers.Cancel(v.Id, "nhập nhầm");
    }

    [Fact]
    public void Staff_cannot_cancel_or_edit_catalog()
    {
        _s.Auth.CreateUser("nv1", "Nhân viên", "matkhau1", Roles.Staff);
        var v = In(1, 10);
        _s.Auth.Login("nv1", "matkhau1");
        Assert.Throws<BusinessException>(() => _s.Vouchers.Cancel(v.Id, "x"));
        Assert.Throws<BusinessException>(() => _s.Catalog.SaveProduct(new Product { Code = "Z", Name = "Z" }));
        Out(1, 10); // nhân viên vẫn lập phiếu được
    }

    [Fact]
    public void Last_admin_cannot_be_demoted()
    {
        var me = _s.Session.CurrentUser!;
        Assert.Throws<BusinessException>(() => _s.Auth.UpdateUser(me.Id, "x", Roles.Staff, true));
    }

    [Fact]
    public void Nxt_report_splits_opening_in_out()
    {
        var d0 = DateTime.Today.AddDays(-5);
        In(10, 100, date: d0);
        In(6, 60, date: DateTime.Today);
        Out(4, 40, date: DateTime.Today);
        var r = _s.Stock.Nxt(DateTime.Today, DateTime.Today).Single();
        Assert.Equal(10, r.OpenQuantity);
        Assert.Equal(6, r.InQuantity);
        Assert.Equal(4, r.OutQuantity);
        Assert.Equal(12, r.CloseQuantity);
        Assert.Equal(120, r.CloseWeight);
    }

    [Fact]
    public void Ledger_has_running_balance()
    {
        In(10, 100, date: DateTime.Today.AddDays(-2));
        In(5, 50);
        Out(3, 30);
        var (openQ, _, rows) = _s.Stock.Ledger(DateTime.Today, DateTime.Today, _ca.Id);
        Assert.Equal(10, openQ);
        Assert.Equal(new double[] { 15, 12 }, rows.Select(r => r.BalanceQuantity));
    }

    [Fact]
    public void Dashboard_counts_today_and_low_stock()
    {
        In(3, 30);
        var d = _s.Stock.Dashboard();
        Assert.Equal(1, d.TodayInCount);
        Assert.Equal(3, d.TotalQuantity);
        Assert.Single(d.LowStock); // tồn 3 < tối thiểu 5
        Assert.Single(d.Recent);
    }

    [Fact]
    public void Backup_and_restore_round_trip()
    {
        In(10, 100);
        var bak = Path.Combine(_dir, "bk.db");
        _s.Backup.ManualBackup(bak);
        Out(10, 100);
        Assert.Empty(_s.Stock.Stock());
        _s.Backup.Restore(bak);
        Assert.Equal(10, _s.Stock.Stock().Single().Quantity);
    }

    [Fact]
    public void Excel_export_and_product_import()
    {
        var path = Path.Combine(_dir, "ma.xlsx");
        _s.Export.ExportTable(path, "Mã hàng", "Danh mục", new[] { _ca },
            new ExportColumn<Product>[]
            {
                new("Mã", p => p.Code), new("Tên", p => p.Name), new("Nhóm", p => p.Category), new("ĐVT", p => p.Unit),
                new("Kg", p => p.KgPerUnit), new("Size", p => p.Sizes), new("Min", p => p.MinStock),
            });
        Assert.True(File.Exists(path));
        // File xuất có 3 dòng đầu (tiêu đề + trống); tạo file nạp đúng mẫu riêng.
        var imp = Path.Combine(_dir, "nap.xlsx");
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("MaHang");
            ws.Cell(1, 1).Value = "Mã hàng"; ws.Cell(1, 2).Value = "Tên hàng";
            ws.Cell(2, 1).Value = "TOM-SU"; ws.Cell(2, 2).Value = "Tôm sú"; ws.Cell(2, 5).Value = 12;
            ws.Cell(3, 1).Value = "CA-BASA"; ws.Cell(3, 2).Value = "Cá basa (sửa)";
            ws.Cell(4, 1).Value = "LOI"; ws.Cell(4, 2).Value = "";
            wb.SaveAs(imp);
        }
        var (added, updated, errors) = _s.Catalog.ImportProducts(imp);
        Assert.Equal(1, added);
        Assert.Equal(1, updated);
        Assert.Single(errors);
        Assert.Equal(12, _s.Catalog.ListProducts("TOM").Single().KgPerUnit);
    }

    [Fact]
    public void Voucher_html_contains_lines()
    {
        var v = In(10, 100);
        var (vv, o, lines) = _s.Vouchers.Get(v.Id);
        var html = _s.Export.BuildVoucherHtml(vv, o, lines, "Cty A", "Địa chỉ", "Kho 1");
        Assert.Contains("PHIẾU NHẬP KHO", html);
        Assert.Contains("CA-BASA", html);
    }
}

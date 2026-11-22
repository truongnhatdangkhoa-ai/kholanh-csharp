using KhoLanh.Core.Data;
using KhoLanh.Core.Services;

namespace KhoLanh.Core;

/// <summary>Gom toàn bộ dịch vụ dùng chung một CSDL và một phiên đăng nhập.</summary>
public class AppServices
{
    public Database Db { get; }
    public Session Session { get; } = new();
    public AuditService Audit { get; }
    public SettingsService Settings { get; }
    public AuthService Auth { get; }
    public CatalogService Catalog { get; }
    public VoucherService Vouchers { get; }
    public StockService Stock { get; }
    public BackupService Backup { get; }
    public ExportService Export { get; } = new();

    public AppServices(string dbPath)
    {
        Db = new Database(dbPath);
        Db.Initialize();
        Audit = new AuditService(Db);
        Settings = new SettingsService(Db);
        Auth = new AuthService(Db, Session, Audit);
        Catalog = new CatalogService(Db, Session, Audit);
        Vouchers = new VoucherService(Db, Session, Audit);
        Stock = new StockService(Db);
        Backup = new BackupService(Db, Session, Audit);
        Catalog.EnsureDefaults();
    }
}

using KhoLanh.Core.Data;
using KhoLanh.Core.Modules;
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
    public BackupService Backup { get; }
    public ModuleStore Modules { get; }
    public ModuleBridge Bridge { get; }
    public LegacyImporter Legacy { get; }
    public UpdateService Updates { get; }
    public AccountSyncService Accounts { get; internal set; }
    public LoginMemory Remember { get; }

    public AppServices(string dbPath)
    {
        Db = new Database(dbPath);
        Db.Initialize();
        Audit = new AuditService(Db);
        Settings = new SettingsService(Db);
        Auth = new AuthService(Db, Session, Audit);
        Backup = new BackupService(Db, Session, Audit);
        Modules = new ModuleStore(Db, Session);
        Legacy = new LegacyImporter(Modules, Session);
        Updates = new UpdateService(Settings, Session);
        Accounts = new AccountSyncService(Db, Settings, Session, Audit);
        Auth.Sync = Accounts;
        Accounts.Updates = Updates;
        Remember = new LoginMemory(Settings);
        Bridge = new ModuleBridge(Modules, Session, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "logs"));
    }
}

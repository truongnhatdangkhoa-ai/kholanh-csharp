using KhoLanh.Core.Data;
using KhoLanh.Core.Entry;
using KhoLanh.Core.TongHop;
using KhoLanh.Core.RaDong;
using KhoLanh.Core.DoiChieu;
using KhoLanh.Core.ViTri;
using KhoLanh.Core.TonKho;
using KhoLanh.Core.SoDo;
using KhoLanh.Core.KiemKe;
using KhoLanh.Core.KhangSinh;
using KhoLanh.Core.Lan;
using KhoLanh.Core.ChungTu;
using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.PhieuXuat;
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
    public LegacyImporter Legacy { get; }
    public UpdateService Updates { get; }
    public AccountSyncService Accounts { get; internal set; }
    public LoginMemory Remember { get; }
    public LanService Lan { get; private set; }
    public DriveSyncService Drive { get; }
    public EntryService Entry { get; }
    public TongHopService TongHop { get; }
    public RaDongService RaDong { get; }
    public DoiChieuService DoiChieu { get; }
    public ViTriService ViTri { get; }
    public AnAnService AnAn { get; }
    public KhoLanh.Core.TonKhoGui.GuiService Gui { get; }
    public KhoLanh.Core.Container3D.C3dService C3d { get; }
    public KhoLanh.Core.Scan.ScanStore Scan { get; }
    public SoDoService SoDo { get; }
    public KiemKeService KiemKe { get; }
    public KhangSinhService KhangSinh { get; }
    public PxhtService Pxht { get; }
    public ChungTuService ChungTu { get; }
    public PhieuKhoService PhieuKho { get; }
    public CargoService Cargo { get; }

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
        Lan = new LanService(Db, Modules, Settings, Session, Audit);
        Legacy.Target = Lan;
        Drive = new DriveSyncService(() => Lan, Settings);
        Backup.Guard = () => Lan.Enabled ? "Máy này đang dùng chung dữ liệu qua mạng LAN. Chuyển về \"Máy đơn\" ở Mạng LAN rồi mở lại phần mềm trước khi khôi phục." : null;
        Entry = new EntryService(() => Lan, Session, Audit);
        TongHop = new TongHopService(() => Lan, Session, Audit, Settings);
        RaDong = new RaDongService(() => Lan, Session, Audit);
        ViTri = new ViTriService(() => Lan, Session, Audit);
        Gui = new KhoLanh.Core.TonKhoGui.GuiService(() => Lan, Session, Audit, Settings, new NapUndoStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "nap")));
        AnAn = new AnAnService(() => Lan, Session, Audit, Settings, new NapUndoStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "nap")));
        SoDo = new SoDoService(() => Lan, Session, Audit, Entry);
        KiemKe = new KiemKeService(() => Lan, Session, Audit, Entry, new SoDoService(() => Lan, Session, Audit, Entry));
        KhangSinh = new KhangSinhService(() => Lan, Session, Audit);
        Pxht = new PxhtService(() => Lan, Session);
        C3d = new KhoLanh.Core.Container3D.C3dService(() => Lan, Session);
        Scan = new KhoLanh.Core.Scan.ScanStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "scan"), Session);
        PhieuKho = new PhieuKhoService(() => Lan, Session, Audit, new TongHopService(() => Lan, Session, Audit, Settings));
        Cargo = new CargoService(() => Lan, Session, Audit);
        ChungTu = new ChungTuService(() => Lan, Session, Audit, new TongHopService(() => Lan, Session, Audit, Settings));
        DoiChieu = new DoiChieuService(() => Lan, Session, Audit, Settings, Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".");
    }

    /// <summary>Đọc lại vai LAN vừa đổi (phân vai theo tài khoản lúc đăng nhập): dừng phần LAN cũ, chạy phần LAN mới.</summary>
    public void ReloadLan(bool start = true)
    {
        var old = Lan;
        int tick = old.TickMs; double speed = old.Speed;
        old.Dispose();
        Lan = new LanService(Db, Modules, Settings, Session, Audit) { TickMs = tick, Speed = speed };
        Legacy.Target = Lan;
        if (start) Lan.Start();
    }
}

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
    /// <summary>Lưu trữ cá nhân (ghi chú / tệp riêng từng tài khoản + sao lưu Google Drive cá nhân).</summary>
    public KhoLanh.Core.LuuTru.LuuTruService LuuTru { get; }
    /// <summary>Sao lưu database lên Google Drive cá nhân (đăng nhập Gmail) — Dữ liệu &amp; sao lưu › Sao lưu.</summary>
    public KhoLanh.Core.SaoLuu.DriveCaNhanService DriveCaNhan { get; }
    public SoDoService SoDo { get; }
    public KiemKeService KiemKe { get; }
    public KhangSinhService KhangSinh { get; }
    public PxhtService Pxht { get; }
    public ChungTuService ChungTu { get; }
    public PhieuKhoService PhieuKho { get; }
    public CargoService Cargo { get; }
    public KhoLanh.Core.TroLy.TroLyService TroLy { get; }
    public KhoLanh.Core.LichXuat.LichXuatService LichXuat { get; }
    public KhoLanh.Core.SoKho.SoKhoService SoKho { get; }
    public KhoLanh.Core.DanhMuc.DanhMucService DanhMuc { get; }
    public KhoLanh.Core.CuoiKy.CkService CuoiKy { get; }
    public KhoLanh.Core.SoKho.VtbService Vtb { get; }
    /// <summary>Kết nối điện thoại (Google Sheet qua Apps Script Web App của app điện thoại).</summary>
    public PhoneWebApp Phone { get; }
    public GoogleSignIn Google { get; }
    /// <summary>Đồng bộ điện thoại cho mọi module (đẩy tồn + nhận phiếu).</summary>
    public PhoneSync.PhoneSyncService PhoneSync { get; }
    /// <summary>Tài khoản PC dùng trên điện thoại + quyền điện thoại riêng (1.76).</summary>
    public PhoneSync.PhoneAccounts PhoneAccounts { get; }

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
        Backup.IsRemote = () => Lan.Role == LanService.RoleRemote;
        Drive.MakeBackup = p => Backup.BackupTo(p);
        Backup.Guard = () => RestoreLock(Lan);
        Backup.AfterRestore = () => ReloadLan();   // máy vừa tách khỏi LAN: dừng phần LAN cũ (không nối lại máy chủ, không gửi sổ chờ)
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
        LuuTru = new KhoLanh.Core.LuuTru.LuuTruService(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".", "luu-tru"), Session, Settings);
        DriveCaNhan = new KhoLanh.Core.SaoLuu.DriveCaNhanService(Settings, Session, Backup, () => Lan);
        PhieuKho = new PhieuKhoService(() => Lan, Session, Audit, new TongHopService(() => Lan, Session, Audit, Settings));
        Cargo = new CargoService(() => Lan, Session, Audit);
        ChungTu = new ChungTuService(() => Lan, Session, Audit, new TongHopService(() => Lan, Session, Audit, Settings));
        DoiChieu = new DoiChieuService(() => Lan, Session, Audit, Settings, Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".");
        SoKho = new KhoLanh.Core.SoKho.SoKhoService(() => Lan, Session, Audit);
        DanhMuc = new KhoLanh.Core.DanhMuc.DanhMucService(() => Lan, Session, Audit, () => TongHop);
        CuoiKy = new KhoLanh.Core.CuoiKy.CkService(() => Lan, Session, Settings);
        Vtb = new KhoLanh.Core.SoKho.VtbService(() => Lan, Session, Audit);
        Phone = new PhoneWebApp(Settings);
        Accounts.Phone = Phone;   // 1.105: mã kết nối máy mang theo PC_TOKEN
        // 1.112: Client OAuth Google dùng chung qua Sheet chung — máy thiếu secret tự lấy về trước khi đăng nhập Gmail.
        DriveCaNhan.EnsureClient = ct => Accounts.SyncGoogleClientAsync(ct);
        // 1.121: máy đơn (không nối được máy chủ, không có thay đổi chờ gửi) tự dùng bản sao lưu Drive cá nhân của máy chủ nếu mới hơn
        Lan.OfflineSnapshot = (server, since, ct) => DriveCaNhan.NewerServerBackupAsync(server, since, ct);
        Lan.BeforeOfflineSnapshot = () =>
        {
            try { Backup.BackupTo(Path.Combine(Backup.AutoBackupFolder, $"truoc-tai-drive_{DateTime.Now:yyyy-MM-dd_HHmmss}.db")); } catch { }
        };
        LuuTru.EnsureClient = ct => Accounts.SyncGoogleClientAsync(ct);
        Google = new GoogleSignIn(Settings);
        PhoneSync = new PhoneSync.PhoneSyncService(this);
        PhoneAccounts = new PhoneSync.PhoneAccounts(() => Lan, Session);
        Auth.PasswordSeen = PhoneAccounts.RememberPassword;   // 1.76: tài khoản PC dùng chung trên điện thoại
        LichXuat = new KhoLanh.Core.LichXuat.LichXuatService(() => Lan, Session, Audit, Settings, () => new PhieuKhoService(() => Lan, Session, Audit, new TongHopService(() => Lan, Session, Audit, Settings)));
        // 1.121: đệm thư lịch xuất đã đọc cạnh database (lượt quét sau chỉ tải thư mới)
        if (LichXuat.Mail is KhoLanh.Core.LichXuat.ImapMailSource ims) ims.CachePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "email-lich-xuat-dem.json");
        TroLy = new KhoLanh.Core.TroLy.TroLyService(Db, () => Lan, Session, Audit, Backup, () => Lan, Drive, Path.GetDirectoryName(Path.GetFullPath(dbPath)) ?? ".") { Settings = Settings };
    }

    /// <summary>Đọc lại vai LAN vừa đổi (phân vai theo tài khoản lúc đăng nhập): dừng phần LAN cũ, chạy phần LAN mới.</summary>
    /// <summary>
    /// 1.114: khóa khôi phục khi máy ĐANG nối máy chủ LAN (máy chủ đang làm chủ / máy con, dự phòng đang nối) — khôi phục lúc đó ảnh hưởng dữ liệu chung.
    /// Máy ngoài LAN (máy đơn, máy LAN đang không nối được máy chủ, máy xem từ xa qua Drive) → được, sau khi khôi phục tách ra dùng riêng.
    /// </summary>
    public static string? RestoreLock(LanService lan) =>
        lan.Enabled && lan.Role != LanService.RoleRemote && (lan.Serving || lan.Online || lan.Role == LanService.RoleServer)
            ? (lan.Serving || lan.Role == LanService.RoleServer
                ? "Máy này là MÁY CHỦ mạng LAN — khôi phục sẽ thay dữ liệu chung của mọi máy, nên bị khóa (muốn khôi phục: Quản trị «Tắt máy chủ trên máy này» trước)."
                : "Máy này đang nối máy chủ qua mạng LAN — khóa khôi phục để không ảnh hưởng dữ liệu chung.")
              + " Khôi phục được ở máy ngoài mạng LAN (máy đó tự tách ra dùng dữ liệu riêng)."
            : null;

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

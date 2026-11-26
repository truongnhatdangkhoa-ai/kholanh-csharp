using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.Lan;

/// <summary>
/// Nhiều máy trong mạng nội bộ dùng chung dữ liệu module.
///  - MÁY CHỦ CHÍNH ("chu") giữ database chính, mở cổng TCP (mặc định 47820) cho các máy khác.
///  - MÁY DỰ PHÒNG ("duphong") chép liên tục mọi thay đổi của máy chủ (mỗi giây). Máy chủ mất ≥ 10 giây và quá nửa số máy con
///    cũng báo mất máy chủ → tự lên làm máy chủ (nhiệm kỳ +1). Máy chủ chính bật lại → chép phần thiếu rồi nhận lại vai.
///  - MÁY CON ("con") đọc / ghi thẳng vào máy đang làm chủ; giữ bản sao trong máy.
///  - Không nối được máy nào làm chủ → làm tiếp trên database riêng (bản sao), mỗi lần ghi vào SỔ CHỜ (lan_pending, kèm bản gốc).
///    Nối lại → gửi sổ chờ; máy chủ gộp: chỉ một bên đổi → lấy luôn; khác dòng → tự gộp; trùng dòng → giữ bản chính, lưu
///    bản máy con vào lan_conflicts để Quản trị chọn. Không mất dữ liệu.
///  - Hai máy cùng làm chủ (đứt mạng giữa hai nhóm) → khi thấy nhau, máy nhiệm kỳ thấp hơn nhường và gửi phần đã sửa lên để gộp.
/// Mỗi bảng có số phiên bản; ghi kèm số phiên bản lúc đọc → bảng đã bị nơi khác sửa thì từ chối (không ghi đè).
/// </summary>
public sealed partial class LanService : ISheetSource, IDisposable
{
    public const string RoleSingle = "", RoleServer = "chu", RoleStandby = "duphong", RoleClient = "con", RoleRemote = "xa";
    public const int DefaultPort = 47820;
    public const string CodePrefix = "KLLAN1-";
    private const int PromoteAfterSeconds = 10;

    private readonly Database _db;
    private readonly ModuleStore _store;
    private readonly SettingsService _settings;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly object _state = new();
    private readonly object _dataLock = new();
    private readonly ConcurrentDictionary<string, long> _readVersions = new();
    // Bảng vừa được ghi vào database riêng từ bản mới của máy chủ (ghi / đọc / gửi sổ chờ) → số thứ tự lần ghi. Pull dùng để
    // không áp dữ liệu cũ hơn: phản hồi "changes" được máy chủ tính TRƯỚC lần ghi đó, áp vào sẽ xóa / lùi bản vừa ghi
    // (rồi khi mất máy chủ, sổ chờ lấy nhầm bản gốc rỗng → gộp 3 bên nhân đôi dòng).
    private readonly ConcurrentDictionary<string, long> _touched = new();
    private long _touchSeq;
    private void Touch(string db, string sheet) => _touched[K(db, sheet)] = Interlocked.Increment(ref _touchSeq);
    /// <summary>Kiểm thử: chạy sau khi Pull nhận phản hồi, trước khi áp (để chen một lần ghi vào giữa).</summary>
    internal Func<Task>? TestAfterPullResponse;
    /// <summary>Test: chạy trước mỗi lần gọi máy khác (giả lập mạng chập chờn / máy chủ trả lời chậm).</summary>
    internal Func<JsonObject, Task>? TestBeforeCall;
    /// <summary>Test: số lượt tìm máy chủ đã chạy.</summary>
    internal int FindRounds;
    private int _pullsApplied;
    internal int PullsApplied => Volatile.Read(ref _pullsApplied);
    private readonly ConcurrentDictionary<string, ILanConn> _conns = new();
    /// <summary>Địa chỉ gọi được gần nhất của từng máy (thử trước — máy ở xa khỏi chờ hết các địa chỉ trong nhà).</summary>
    private readonly ConcurrentDictionary<string, string> _goodEp = new();
    private LanHttpServer? _http;
    private DateTime _lastListen = DateTime.MinValue;
    private readonly ConcurrentDictionary<string, Seen> _seen = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public string Role { get; }
    public string MachineId { get; }
    public string MachineName { get; }
    public int Port { get; }
    private readonly string _key;

    // ----- trạng thái -----
    public bool Serving { get; private set; }
    public long Epoch { get; private set; }
    private long _epochStart;
    private bool _frozen; private DateTime _frozenAt;
    private DateTime _lastWriteAt = DateTime.MinValue;
    private Node? _server;            // máy đang làm chủ (khi mình không làm chủ)
    private long _serverEpoch;
    private long _maxSeenEpoch;
    public bool Online { get; private set; }
    private DateTime _failSince = DateTime.MinValue;
    /// <summary>1.129: số lượt tìm máy chủ hụt LIÊN TIẾP — máy dự phòng chỉ lên làm chủ khi đủ <see cref="PromoteAfterMisses"/> lượt
    /// (trước chỉ xét thời gian: 1 lần hỏi chậm (máy bận) quá ngưỡng là lên làm chủ dù máy chủ chính còn sống → 2 máy chủ).</summary>
    private int _findMisses;
    private const int PromoteAfterMisses = 3;
    private DateTime _lastPull = DateTime.MinValue, _lastBeat = DateTime.MinValue, _lastPeerCheck = DateTime.MinValue, _lastTry = DateTime.MinValue, _lastLanProbe = DateTime.MinValue;
    private readonly DateTime _startedAt = DateTime.Now;
    public string LastSync { get; private set; } = "";
    public event Action? Changed;

    public record Node(string Id, string Name, string Role, List<string> Addrs);
    /// <summary>1.100: "IP:cổng" của máy chủ đang nối (máy con) — để đo độ trễ LAN; null khi chính máy này làm chủ / chưa nối.</summary>
    /// <summary>Địa chỉ đang dùng để nối máy chủ (địa chỉ gọi được gần nhất; qua đường hầm thì là https://…).</summary>
    public string? ServerAddress => _server is { } sv ? (_goodEp.TryGetValue(string.Join(";", sv.Addrs), out var g) ? g : sv.Addrs.FirstOrDefault()) : null;
    /// <summary>Máy vừa gọi tới. <paramref name="ViaNet"/> = nối qua Internet (đường hầm Cloudflare, 1.118) — <paramref name="NetIp"/> là IP công khai
    /// của nó (có thể trống); false = nối trong mạng LAN.</summary>
    public record Seen(string Id, string Name, string Role, string Addr, DateTime At, bool ServerDown, int Pending, string User = "", bool ViaNet = false, string NetIp = "");

    public LanService(Database db, ModuleStore store, SettingsService settings, Session session, AuditService audit)
    {
        _db = db; _store = store; _settings = settings; _session = session; _audit = audit;
        Role = settings.Get("lan_role");
        Port = int.TryParse(settings.Get("lan_port"), out var p) && p > 0 ? p : DefaultPort;
        _key = Secret.Unprotect(settings.Get("lan_key"));
        var id = settings.Get("lan_machine_id");
        if (id.Length == 0) { id = Guid.NewGuid().ToString("N"); settings.Set("lan_machine_id", id); }
        MachineId = id;
        var nm = settings.Get("lan_name");
        MachineName = nm.Length > 0 ? nm : Environment.MachineName;
        Epoch = long.TryParse(settings.Get("lan_epoch"), out var e) ? e : 0;
        _epochStart = long.TryParse(settings.Get("lan_epoch_start"), out var es) ? es : 0;
    }

    public bool Enabled => (Role is RoleServer or RoleStandby or RoleClient && _key.Length > 0) || Role == RoleRemote;
    public bool Capable => Role is RoleServer or RoleStandby;
    public static string RoleLabel(string r) => r switch
    {
        RoleServer => "Máy chủ chính", RoleStandby => "Máy dự phòng", RoleClient => "Máy con", RoleRemote => "Máy xem từ xa (qua Google Drive)", _ => "Máy đơn (không dùng LAN)",
    };

    // =====================================================================================
    // Cấu hình
    // =====================================================================================

    /// <summary>Mã LAN để dán vào máy khác: địa chỉ máy chủ chính + cổng + khóa.</summary>
    public string ConnectionCode()
    {
        var o = new JsonObject { ["h"] = new JsonArray(MyAddrs().Select(a => (JsonNode)a).ToArray()), ["k"] = _key };
        return CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(o.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static (List<string> addrs, string key) ParseCode(string code)
    {
        var t = new string(code.Trim().Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        if (!t.StartsWith(CodePrefix)) throw new BusinessException("Mã LAN phải bắt đầu bằng " + CodePrefix);
        try
        {
            var b = t[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
            b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
            var o = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b)))!.AsObject();
            return (o["h"]!.AsArray().Select(x => x!.GetValue<string>()).ToList(), o["k"]!.GetValue<string>());
        }
        catch { throw new BusinessException("Mã LAN bị thiếu hoặc sai — chép lại đủ cả dòng."); }
    }

    /// <summary>
    /// Cấu hình cho máy chủ chạy KHÔNG GIAO DIỆN (KhoLanh.Server, không có người đăng nhập Quản trị). Vai "chu": tạo khóa nếu chưa có;
    /// "duphong": cần mã LAN của máy chủ chính. publicAddrs = địa chỉ công khai (IP:cổng) máy con dùng để nối (trên cloud, IP trong máy ≠ IP công khai).
    /// </summary>
    public static void ConfigureHeadless(SettingsService settings, string role, string? code, int? port, string? publicAddrs, bool requireEnc, string? name)
    {
        if (role is not (RoleServer or RoleStandby)) throw new BusinessException("Máy chủ không giao diện chỉ làm “chu” (chính) hoặc “duphong” (dự phòng).");
        if (role == RoleStandby)
        {
            if (string.IsNullOrWhiteSpace(code) && Secret.Unprotect(settings.Get("lan_key")).Length == 0) throw new BusinessException("Máy dự phòng cần mã LAN của máy chủ chính (--code).");
            if (!string.IsNullOrWhiteSpace(code))
            {
                var (addrs, key) = ParseCode(code);
                settings.Set("lan_key", Secret.Protect(key));
                settings.Set("lan_nodes", new JsonArray(new JsonObject { ["id"] = "", ["name"] = "Máy chủ chính", ["role"] = RoleServer, ["addrs"] = new JsonArray(addrs.Select(a => (JsonNode)a).ToArray()) }).ToJsonString());
                if (settings.Get("lan_role") != RoleStandby) settings.Set("lan_last_seq", "0");
            }
        }
        else if (Secret.Unprotect(settings.Get("lan_key")).Length == 0)
            settings.Set("lan_key", Secret.Protect(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()));
        if (port is > 0) settings.Set("lan_port", port.Value.ToString());
        if (publicAddrs is not null) settings.Set("lan_addrs", publicAddrs);
        if (!string.IsNullOrWhiteSpace(name)) settings.Set("lan_name", name);
        settings.Set("lan_enc", requireEnc ? "1" : "");
        settings.Set("lan_role", role);
    }

    /// <summary>Lưu vai của máy (có hiệu lực sau khi mở lại phần mềm). Máy chủ chính: tạo khóa mới nếu chưa có.</summary>
    public static void Configure(SettingsService settings, Session session, string role, string? code, int? port = null)
    {
        session.RequireAdmin();
        if (role is not (RoleSingle or RoleServer or RoleStandby or RoleClient)) throw new BusinessException("Vai không hợp lệ.");
        if (role is RoleStandby or RoleClient)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new BusinessException("Dán mã LAN chép từ máy chủ chính.");
            var (addrs, key) = ParseCode(code);
            settings.Set("lan_key", Secret.Protect(key));
            settings.Set("lan_nodes", new JsonArray(new JsonObject { ["id"] = "", ["name"] = "Máy chủ chính", ["role"] = RoleServer, ["addrs"] = new JsonArray(addrs.Select(a => (JsonNode)a).ToArray()) }).ToJsonString());
            settings.Set("lan_last_seq", "0"); // tải lại toàn bộ dữ liệu từ máy chủ
        }
        else if (role == RoleServer && Secret.Unprotect(settings.Get("lan_key")).Length == 0)
            settings.Set("lan_key", Secret.Protect(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()));
        if (port is > 0) settings.Set("lan_port", port.Value.ToString());
        settings.Set("lan_role", role);
        settings.Set("lan_serving", "");
    }

    /// <summary>
    /// Đặt vai theo kế hoạch phân vai theo tài khoản (không cần quyền Quản trị — vai do Quản trị gán sẵn trên Google Sheet).
    /// Máy chủ giữ khóa của mình (chưa có thì lấy khóa trong kế hoạch / tạo mới); máy dự phòng / máy con lấy khóa + địa chỉ
    /// máy chủ trong kế hoạch. Trả true nếu có gì thay đổi (cần khởi động lại phần LAN).
    /// </summary>
    public static bool ApplyPlan(SettingsService settings, LanPlan plan, string role)
    {
        var before = string.Join("|", settings.Get("lan_role"), settings.Get("lan_key"), settings.Get("lan_port"), settings.Get("lan_nodes"));
        var oldRole = settings.Get("lan_role");
        var myKey = Secret.Unprotect(settings.Get("lan_key"));
        if (plan.Port > 0 && settings.Get("lan_port").Length == 0) settings.Set("lan_port", plan.Port.ToString());
        if (role == RoleServer)
        {
            if (myKey.Length == 0)
                settings.Set("lan_key", Secret.Protect(plan.Key.Length > 0 ? plan.Key : Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()));
        }
        else if (role is RoleStandby or RoleClient)
        {
            if (plan.Key.Length > 0 && plan.Key != myKey) settings.Set("lan_key", Secret.Protect(plan.Key));
            List<JsonObject> nodes;
            try { nodes = (JsonNode.Parse(settings.Get("lan_nodes", "[]")) as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToList(); }
            catch { nodes = new(); }
            var myId = settings.Get("lan_machine_id");
            foreach (var (slot, r) in new[] { (plan.Chu, RoleServer), (plan.DuPhong, RoleStandby) })
            {
                if (slot is null || slot.Id == myId || slot.Addrs.Count == 0) continue;
                nodes.RemoveAll(o => (o["id"]?.ToString() ?? "") == slot.Id || (o["id"]?.ToString() ?? "") == ""
                    || (r == RoleServer && o["role"]?.ToString() == RoleServer));
                nodes.Add(new JsonObject { ["id"] = slot.Id, ["name"] = slot.Name, ["role"] = r, ["addrs"] = new JsonArray(slot.Addrs.Select(a => (JsonNode)a).ToArray()) });
            }
            var json = new JsonArray(nodes.Select(n => (JsonNode)n).ToArray()).ToJsonString();
            if (json != settings.Get("lan_nodes", "[]")) settings.Set("lan_nodes", json);
            if (oldRole is not (RoleStandby or RoleClient)) settings.Set("lan_last_seq", "0"); // tải lại toàn bộ từ máy chủ
        }
        if (oldRole != role) { settings.Set("lan_role", role); if (role != RoleServer) settings.Set("lan_claim", ""); }
        var after = string.Join("|", settings.Get("lan_role"), settings.Get("lan_key"), settings.Get("lan_port"), settings.Get("lan_nodes"));
        return before != after;
    }

    /// <summary>Đánh dấu "chuyển máy chủ về máy này": máy chủ đang chạy được báo nhường vai khi máy này đã chép đủ.</summary>
    public static void MarkClaim(SettingsService settings) => settings.Set("lan_claim", "1");

    /// <summary>Địa chỉ (IP:cổng) của máy này trong mạng nội bộ.</summary>
    public List<string> Addresses() => MyAddrs();
    internal string Key => _key;
    /// <summary>Máy chủ đã được báo nhường vai cho máy chủ chính mới.</summary>
    public bool Yielding => _yield;
    private volatile bool _yield;

    /// <summary>Dòng mã LAN cho file ket-noi.txt của bộ cài máy mới (máy mới thành máy con).</summary>
    public string? BundleLine() => Enabled && Role == RoleServer ? ConnectionCode() : null;

    /// <summary>Máy mới có ket-noi.txt chứa mã LAN → tự làm máy con.</summary>
    public static void ImportBundle(SettingsService settings, string code)
    {
        var (addrs, key) = ParseCode(code);
        settings.Set("lan_key", Secret.Protect(key));
        settings.Set("lan_nodes", new JsonArray(new JsonObject { ["id"] = "", ["name"] = "Máy chủ chính", ["role"] = RoleServer, ["addrs"] = new JsonArray(addrs.Select(a => (JsonNode)a).ToArray()) }).ToJsonString());
        settings.Set("lan_role", RoleClient);
        settings.Set("lan_last_seq", "0");
    }

    private List<string> MyAddrs()
    {
        var pub = PublicUrl;   // 1.104: địa chỉ từ xa (đường hầm) đứng CUỐI — máy trong nhà vẫn nối thẳng, máy ở xa thử hết rồi nhớ địa chỉ này
        var over = _settings.Get("lan_addrs"); // máy chủ không giao diện (--public) / kiểm thử
        if (over.Length > 0) { var o = over.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(); if (pub.Length > 0 && !o.Contains(pub)) o.Add(pub); return o; }
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) list.Add($"{ua.Address}:{Port}");
            }
        }
        catch { }
        if (list.Count == 0) list.Add($"127.0.0.1:{Port}");
        if (pub.Length > 0) list.Add(pub);
        return list;
    }

    private List<Node> KnownNodes()
    {
        try
        {
            return (JsonNode.Parse(_settings.Get("lan_nodes", "[]")) as JsonArray ?? new JsonArray())
                .OfType<JsonObject>()
                .Select(o => new Node(o["id"]?.ToString() ?? "", o["name"]?.ToString() ?? "", o["role"]?.ToString() ?? "",
                    (o["addrs"] as JsonArray ?? new JsonArray()).Select(x => x!.ToString()).ToList()))
                .Where(n => n.Id != MachineId).ToList();
        }
        catch { return new(); }
    }

    private void Learn(JsonArray? nodes)
    {
        if (nodes is null) return;
        var known = KnownNodes();
        var changed = false;
        foreach (var o in nodes.OfType<JsonObject>())
        {
            var n = new Node(o["id"]?.ToString() ?? "", o["name"]?.ToString() ?? "", o["role"]?.ToString() ?? "",
                (o["addrs"] as JsonArray ?? new JsonArray()).Select(x => x!.ToString()).ToList());
            if (n.Id == MachineId || n.Id.Length == 0 || n.Addrs.Count == 0) continue;
            // nút không id (lấy từ mã LAN) trùng địa chỉ → thay bằng nút đầy đủ
            var same = known.FirstOrDefault(k => k.Id == n.Id);
            if (same is not null && same.Name == n.Name && same.Role == n.Role && same.Addrs.SequenceEqual(n.Addrs)) continue;
            known.RemoveAll(k => k.Id == n.Id || (k.Id.Length == 0 && k.Addrs.Intersect(n.Addrs).Any()));
            known.Add(n); changed = true;
        }
        if (changed)
            _settings.Set("lan_nodes", new JsonArray(known.Select(k => (JsonNode)new JsonObject
            {
                ["id"] = k.Id, ["name"] = k.Name, ["role"] = k.Role, ["addrs"] = new JsonArray(k.Addrs.Select(a => (JsonNode)a).ToArray()),
            }).ToArray()).ToJsonString());
    }

    private JsonArray NodesInfo()
    {
        var arr = new JsonArray { MyNodeJson() };
        foreach (var s in _seen.Values.Where(s => s.Role is RoleServer or RoleStandby && (DateTime.Now - s.At).TotalMinutes < 10))
            arr.Add(new JsonObject { ["id"] = s.Id, ["name"] = s.Name, ["role"] = s.Role, ["addrs"] = new JsonArray(s.Addr.Split(';').Select(a => (JsonNode)a).ToArray()) });
        return arr;
    }

    private JsonObject MyNodeJson() => new()
    {
        ["id"] = MachineId, ["name"] = MachineName, ["role"] = Role, ["addrs"] = new JsonArray(MyAddrs().Select(a => (JsonNode)a).ToArray()),
    };

    // =====================================================================================
    // Chạy / dừng
    // =====================================================================================

    public void Start()
    {
        if (!Enabled || _cts is not null || Role == RoleRemote) return;   // máy xem từ xa: không mở / không nối mạng LAN
        _cts = new CancellationTokenSource();
        if (Capable) Listen();
        _loop = Task.Run(() => Loop(_cts.Token));
    }

    /// <summary>Lỗi mở cổng (VD cổng đang bị chiếm, bản cũ chưa thoát hẳn) — tự thử lại mỗi 3 giây.</summary>
    public string ListenError { get; private set; } = "";

    private void Listen()
    {
        if (_cts is null) return;
        if (_listener is not null) { ListenHttp(); return; }
        try
        {
            var l = new TcpListener(IPAddress.Any, Port);
            l.Start();
            _listener = l; ListenError = "";
            _ = AcceptLoop(l, _cts.Token);
        }
        catch (SocketException e) { ListenError = PortBusyText(Port, e, OtherHolders()); }
        ListenHttp();
    }

    /// <summary>
    /// 1.117 (Trợ lý: "Máy chủ LAN không mở được cổng 47820: Only one usage of each socket address…"): câu báo nói rõ AI đang giữ cổng để người dùng tự xử lý.
    /// <paramref name="holders"/> = chương trình KhoLanh khác đang chạy trên máy (cửa sổ KhoLanh thứ hai, KhoLanh.Server, bản cũ chưa thoát hẳn sau cập nhật).
    /// </summary>
    public static string PortBusyText(int port, SocketException e, IReadOnlyList<string> holders)
    {
        if (e.SocketErrorCode != SocketError.AddressAlreadyInUse) return $"Không mở được cổng {port}: {e.Message}";
        if (holders.Count > 0)
            return $"Không mở được cổng {port}: {string.Join(", ", holders)} khác trên máy này đang giữ cổng (mỗi máy chỉ 1 chương trình làm máy chủ). "
                 + "Đóng bớt cửa sổ KhoLanh / tắt KhoLanh.Server rồi chờ vài giây — app tự thử lại mỗi 3 giây. Vừa cập nhật xong thì bản cũ có thể chưa thoát hẳn, chờ 1 phút.";
        return $"Không mở được cổng {port}: một chương trình khác (không phải KhoLanh) đang dùng cổng này. Tắt chương trình đó hoặc khởi động lại máy — "
             + "app tự thử lại mỗi 3 giây (xem chương trình giữ cổng: cmd › netstat -ano | findstr :" + port + ").";
    }

    /// <summary>Chương trình KhoLanh khác (không phải chính tiến trình này) đang chạy trên máy.</summary>
    private static IReadOnlyList<string> OtherHolders()
    {
        var res = new List<string>();
        try
        {
            var me = Environment.ProcessId;
            if (System.Diagnostics.Process.GetProcessesByName("KhoLanh").Any(p => p.Id != me)) res.Add("Một cửa sổ KhoLanh");
            if (System.Diagnostics.Process.GetProcessesByName("KhoLanh.Server").Any(p => p.Id != me)) res.Add("KhoLanh.Server");
        }
        catch { /* không đọc được danh sách tiến trình: báo chung */ }
        return res;
    }

    // ------------------------------------------------------------------ 1.104: máy chủ từ xa qua Cloudflare Tunnel

    /// <summary>Cổng HTTP chỉ trong máy (127.0.0.1) cho cloudflared chuyển vào — mặc định cổng LAN + 1.</summary>
    public int HttpPort => int.TryParse(_settings.Get("lan_http_port"), out var p) && p > 0 ? p : Port + 1;
    /// <summary>Địa chỉ từ xa (đường hầm), vd https://kho.kcenter.xyz — đưa vào mã LAN để máy ở xa nối qua Internet.</summary>
    public string PublicUrl => LanHttp.NormUrl(_settings.Get("lan_public")) ?? "";
    public static void SetPublicUrl(SettingsService st, string? url)
    {
        var u = LanHttp.NormUrl(url);
        if (!string.IsNullOrWhiteSpace(url) && u is null) throw new BusinessException("Địa chỉ từ xa không hợp lệ — ví dụ: kho.kcenter.xyz");
        st.Set("lan_public", u ?? "");
    }
    public string HttpError { get; private set; } = "";
    /// <summary>Lần cuối có máy nối vào qua đường hầm.</summary>
    public DateTime TunnelLastOk => _http?.LastOk ?? DateTime.MinValue;

    private void ListenHttp()
    {
        if (_http is not null || _cts is null) return;
        try { _http = new LanHttpServer(HttpPort, () => _key, (req, ip) => Handle(req, "", netIp: ip ?? "")); HttpError = ""; }
        catch (SocketException e) { HttpError = $"Không mở được cổng {HttpPort} cho đường hầm: {e.Message}"; }
    }

    /// <summary>Kiểm tra đường hầm từ chính máy này: gọi "hello" qua địa chỉ từ xa (đi ra Internet rồi vòng về).</summary>
    public async Task<string> TestPublicAsync(string? url = null)
    {
        var u = LanHttp.NormUrl(url ?? PublicUrl) ?? throw new BusinessException("Chưa nhập địa chỉ từ xa.");
        if (_key.Length == 0) throw new BusinessException("Máy này chưa bật mạng LAN.");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var c = new HttpLanConnection(u, _key);
        JsonObject h;
        try { h = await c.CallAsync(Req("hello"), TimeSpan.FromSeconds(15)).ConfigureAwait(false); }
        catch (LanUnreachableException e) { throw new BusinessException("Chưa nối được: " + e.Message.Replace($"Không nối được {u}: ", "")); }
        if (h["ok"]?.GetValue<bool>() != true) throw new BusinessException(h["error"]?.ToString() ?? "Máy chủ trả lời lỗi.");
        var name = h["name"]?.ToString() ?? "";
        return $"Nối được qua {u} trong {sw.ElapsedMilliseconds:#,0} ms — máy trả lời: {name}" + (h["serving"]?.GetValue<bool>() == true ? " (đang làm máy chủ)." : " (CHƯA làm máy chủ).");
    }

    /// <summary>Mã LAN trong ket-noi.txt cạnh exe (bộ cài máy mới), nếu có.</summary>
    public static string? BundledCode(string? dir = null)
    {
        try
        {
            dir ??= Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
            var f = Path.Combine(dir, AccountSyncService.BundleFile);
            return File.Exists(f) ? File.ReadAllLines(f).Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith(CodePrefix)) : null;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _http?.Dispose(); } catch { }
        foreach (var c in _conns.Values) c.Dispose();
        _conns.Clear();
        try { _loop?.Wait(2000); } catch { }
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Tick(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { /* vòng sau thử lại */ }
            try { await Task.Delay(TickMs, ct); } catch { break; }
        }
    }

    /// <summary>Nhịp vòng lặp (kiểm thử đặt nhỏ hơn).</summary>
    public int TickMs { get; set; } = 1000;
    public double Speed { get; set; } = 1; // kiểm thử: <1 = rút ngắn mọi thời gian chờ

    private TimeSpan T(double seconds) => TimeSpan.FromSeconds(seconds * Speed);

    private async Task Tick(CancellationToken ct)
    {
        var now = DateTime.Now;
        if (Capable && (_listener is null || _http is null) && now - _lastListen > T(3)) { _lastListen = now; Listen(); }
        if (Serving)
        {
            if (_frozen && now - _frozenAt > T(10)) { _frozen = false; Raise(); }
            if (now - _lastPeerCheck > T(3)) { _lastPeerCheck = now; await CheckPeers(ct); }
            return;
        }
        if (!Online || _server is null)
        {
            if (now - _lastTry > T(3))
            {
                _lastTry = now;
                if (await FindServer(ct)) { await Flush(ct); await Pull(ct, full: false); }
            }
            if (Capable && !Online) await MaybePromote(ct);
            if (!Online && !Serving) await MaybeOfflineSnapshot(now, ct);
        }
        else
        {
            if (Role == RoleServer && _settings.Get("lan_claim") == "1" && _server is { } cur && cur.Id != MachineId)
            {
                try
                {
                    var r = await CallNode(cur, Req("nhuong"), TimeSpan.FromSeconds(5), ct);
                    if (r["ok"]?.GetValue<bool>() == true) _settings.Set("lan_claim", "");
                }
                catch (LanUnreachableException) { }
            }
            // 1.121: đang đi Internet (đường hầm) mà máy đã về mạng kho → tự chuyển sang LAN (nhanh hơn ~100 lần mỗi lượt gọi)
            if (Via == "net" && now - _lastLanProbe > T(60)) { _lastLanProbe = now; await ProbeLan(ct); }
            var every = Capable ? T(1) : T(15);
            if (now - _lastPull > every) await Pull(ct, full: false);
            if (Online && PendingCount() > 0) await Flush(ct);
        }
        if (Role == RoleClient && now - _lastBeat > T(5)) { _lastBeat = now; await Beat(ct); }
    }

    // =====================================================================================
    // Phía máy làm chủ: nhận yêu cầu
    // =====================================================================================

    private async Task AcceptLoop(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(() => Serve(c, ct));
        }
    }

    private async Task Serve(TcpClient c, CancellationToken ct)
    {
        using (c)
        {
            c.NoDelay = true;
            var s = c.GetStream();
            var remote = (c.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
            ILanChannel chan;
            try
            {
                var head = new byte[4]; var got = 0;
                using (var hc = CancellationTokenSource.CreateLinkedTokenSource(ct)) { hc.CancelAfter(TimeSpan.FromSeconds(10)); while (got < 4) { var r = await s.ReadAsync(head.AsMemory(got), hc.Token); if (r == 0) return; got += r; } }
                if (head.AsSpan().SequenceEqual(SecureChannel.Magic)) chan = await SecureChannel.ServerAsync(s, _key, ct);
                else if (RequireEnc) return;                       // bắt buộc mã hóa: bỏ qua kênh thường
                else chan = new PlainChannel(s, head);
            }
            catch { return; }
            while (!ct.IsCancellationRequested)
            {
                string? text;
                try { text = await chan.ReadAsync(ct); } catch { break; }
                if (text is null) break;
                JsonObject res;
                try { res = Handle(JsonNode.Parse(text) as JsonObject ?? new JsonObject(), remote); }
                catch (BusinessException e) { res = Err(e.Message); }
                catch (Exception e) { res = Err("Máy chủ lỗi: " + e.Message); }
                try { await chan.WriteAsync(res.ToJsonString(), ct); } catch { break; }
            }
        }
    }

    private static JsonObject Err(string msg) => new() { ["ok"] = false, ["error"] = msg };

    /// <summary>Xử lý một yêu cầu (công khai để kiểm thử).</summary>
    /// <param name="netIp">Khác null = yêu cầu đến qua đường hầm Internet (giá trị = IP công khai của máy con, có thể trống).</param>
    public JsonObject Handle(JsonObject req, string remote = "", string? netIp = null)
    {
        if (req["k"]?.ToString() != _key) return Err("Sai mã LAN.");
        var op = req["op"]?.ToString() ?? "";
        var from = req["m"]?.ToString() ?? "";
        var fromRole = req["r"]?.ToString() ?? "";
        if (req["ep"]?.GetValue<long>() is long seenEp && seenEp > _maxSeenEpoch) _maxSeenEpoch = seenEp;
        if (from.Length > 0)
        {
            var addrs = (req["a"] as JsonArray)?.Select(x => x!.ToString()).ToList() ?? new();
            if (addrs.Count == 0 && remote.Length > 0) addrs.Add($"{remote}:{Port}");
            // máy có thể làm chủ gọi tới → nhớ lại (mở máy lần sau biết mà tìm nó trước khi tự làm chủ)
            if (fromRole is RoleServer or RoleStandby && req["a"] is JsonArray)
                Learn(new JsonArray(new JsonObject { ["id"] = from, ["name"] = req["mn"]?.ToString() ?? "", ["role"] = fromRole, ["addrs"] = new JsonArray(addrs.Select(x => (JsonNode)x).ToArray()) }));
            var prev = _seen.GetValueOrDefault(from);
            _seen[from] = new Seen(from, req["mn"]?.ToString() ?? "", fromRole, string.Join(";", addrs), DateTime.Now,
                op == "beat" ? req["down"]?.GetValue<bool>() ?? false : prev?.ServerDown ?? false,
                req["pend"]?.GetValue<int>() ?? prev?.Pending ?? 0, req["u"]?.ToString() ?? "", netIp is not null, netIp ?? "");
        }
        if (op == "hello") return Hello();
        if (op == "beat") return new JsonObject { ["ok"] = true };
        if (op == "nhuong")
        {
            // Quản trị bấm "Chuyển máy chủ về máy này" ở máy khác: máy này thôi làm máy chủ chính. Đang làm chủ thì trả vai
            // khi máy kia đã chép đủ (như máy dự phòng trả vai), lần mở sau là máy con.
            if (fromRole != RoleServer || from.Length == 0 || from == MachineId) return Err("Yêu cầu nhường vai không hợp lệ.");
            _settings.Set("lan_role", RoleClient);
            _yield = true;
            Log("Nhường vai máy chủ chính", $"cho {req["mn"]} (Quản trị chuyển máy chủ)");
            Raise();
            return new JsonObject { ["ok"] = true };
        }
        if (!Serving || (_frozen && op is "write" or "sync"))
            return new JsonObject { ["ok"] = false, ["notServing"] = true, ["error"] = "Máy này không làm máy chủ lúc này." };
        var res = HandleData(op, req, from, fromRole);
        // Lệnh dữ liệu của máy con (read / list / write / sync) kèm danh sách máy có thể làm chủ: máy con chỉ ghi, chưa tới lượt
        // "changes" vẫn biết máy dự phòng (1.50 — trước đây máy chủ tắt ngay sau khi máy dự phòng vừa nối thì máy con
        // không tìm được máy chủ mới; CI đỏ ở Standby_takes_over…).
        if (op is "read" or "list" or "write" or "sync" && res["ok"]?.GetValue<bool>() == true && res["nodes"] is null) res["nodes"] = NodesInfo();
        return res;
    }

    private JsonObject HandleData(string op, JsonObject req, string from, string fromRole)
    {
        var user = req["u"]?.ToString() ?? "";
        switch (op)
        {
            case "read":
            {
                var db = req["db"]!.ToString(); var sheet = req["sheet"]!.ToString();
                RequireRemote(user, db, write: false);
                var (json, v) = _store.ReadVersioned(db, sheet);
                return new JsonObject { ["ok"] = true, ["data"] = json, ["v"] = v };
            }
            case "list":
            {
                var db = req["db"]!.ToString();
                RequireRemote(user, db, write: false);
                return new JsonObject { ["ok"] = true, ["sheets"] = new JsonArray(_store.ListSheets(db).Select(x => (JsonNode)x).ToArray()) };
            }
            case "write":
            {
                var db = req["db"]!.ToString();
                RequireRemote(user, db, write: true);
                var sheets = (req["sheets"] as JsonObject ?? new JsonObject()).ToDictionary(p => p.Key, p => p.Value is null ? null : p.Value.ToString());
                var exp = (req["exp"] as JsonObject)?.ToDictionary(p => p.Key, p => p.Value!.GetValue<long>());
                var opId = req["opId"]?.ToString() ?? "";
                lock (_dataLock)
                {
                    if (opId.Length > 0 && Applied(opId)) return Versions(db, sheets.Keys);
                    try
                    {
                        var vers = _store.WriteRaw(db, sheets, exp, user);
                        MarkApplied(opId);
                        _lastWriteAt = DateTime.Now;
                        return new JsonObject { ["ok"] = true, ["vers"] = new JsonObject(vers.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))) };
                    }
                    catch (SheetConflictException e) { return new JsonObject { ["ok"] = false, ["conflict"] = true, ["sheet"] = e.Sheet, ["error"] = e.Message }; }
                }
            }
            case "changes":
            {
                var since = req["since"]?.GetValue<long>() ?? 0;
                // Đọc số thứ tự TRƯỚC rồi mới lấy danh sách thay đổi: nếu có lần ghi chen giữa thì nó nằm trong danh sách (lượt sau đọc lại cũng vô hại).
                // Làm ngược lại (lỗi trước 1.46) thì máy nhận ghi "đã đọc tới seq mới" mà thiếu đúng lần ghi đó → bỏ lỡ vĩnh viễn
                // (test Standby_takes_over… thỉnh thoảng đỏ: máy dự phòng không chép lại sau khi trả vai).
                var seq = _store.CurrentSeq();
                var items = _store.ChangesSince(since);
                var res = new JsonObject
                {
                    ["ok"] = true, ["seq"] = seq, ["epoch"] = Epoch, ["epochStart"] = _epochStart,
                    ["items"] = new JsonArray(items.Select(r => (JsonNode)new JsonObject { ["db"] = r.DbKey, ["sheet"] = r.Sheet, ["v"] = r.Version, ["data"] = r.Data }).ToArray()),
                    ["nodes"] = NodesInfo(),
                };
                // Trả vai cho máy chủ chính khi nó đã chép đủ và không ai đang ghi.
                if (fromRole == RoleServer && (Role != RoleServer || _yield) && since == seq && items.Count == 0
                    && (req["pend"]?.GetValue<int>() ?? 1) == 0 && DateTime.Now - _lastWriteAt > T(3))
                {
                    _frozen = true; _frozenAt = DateTime.Now;
                    res["handover"] = true;
                    Raise();
                }
                return res;
            }
            case "hist":
            {
                var db = req["db"]!.ToString(); var sheet = req["sheet"]!.ToString();
                ModuleStore.CheckName("database", db); ModuleStore.CheckName("sheet", sheet);
                return new JsonObject
                {
                    ["ok"] = true,
                    ["items"] = new JsonArray(_store.History(db, sheet).Select(h => (JsonNode)new JsonObject { ["v"] = h.Version, ["data"] = h.Data }).ToArray()),
                };
            }
            case "sync":
            {
                var results = new JsonArray();
                foreach (var it in (req["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                    results.Add(ApplySyncItem(it, it["u"]?.ToString() ?? user, req["mn"]?.ToString() ?? from, fromRole is RoleServer or RoleStandby));
                return new JsonObject { ["ok"] = true, ["results"] = results };
            }
            default:
                return Err($"Không hỗ trợ lệnh {op}.");
        }
    }

    private JsonObject Hello() => new()
    {
        ["ok"] = true, ["serving"] = Serving && !_frozen, ["epoch"] = Epoch, ["epochStart"] = _epochStart, ["id"] = MachineId,
        ["name"] = MachineName, ["role"] = Role, ["seq"] = _store.CurrentSeq(), ["nodes"] = NodesInfo(),
    };

    private JsonObject Versions(string db, IEnumerable<string> sheets) =>
        new() { ["ok"] = true, ["vers"] = new JsonObject(sheets.Select(s => KeyValuePair.Create(s, (JsonNode?)_store.ReadVersioned(db, s).Version))) };

    private void RequireRemote(string username, string db, bool write)
    {
        User? u;
        using (var c = _db.Open())
            u = c.QuerySingleOrDefault<User>("SELECT * FROM users WHERE username=@username", new { username });
        if (u is null || !u.IsActive)
            throw new BusinessException($"Tài khoản \"{username}\" không có (hoặc đã khóa) trên máy chủ. Hãy bật Tài khoản dùng chung (Google Sheet) cho mọi máy.");
        var key = ModuleBridge.PermKey(db);
        var ok = write
            ? (key is not null ? Perm.Has(u, key, Perm.Edit) : Roles.IsAdmin(u.Role))
            : (key is not null && Perm.Has(u, key)) || Perm.Has(u, "m1") || Perm.Has(u, "m2")
              // Mã hóa sản phẩm / Ánh xạ tên hàng (tong-hop) là danh mục chung: các báo cáo đọc được
              || (db == "tong-hop" && (Perm.Has(u, "m4") || Perm.Has(u, "m5") || Perm.Has(u, "m7")))
              // Tồn theo vị trí đọc nhóm Đặc tính của Tồn kho An An
              || (db.Contains("ton-kho-an-an") && Perm.Has(u, "m7"))
              // Sơ đồ kho đọc tồn theo vị trí, danh mục mã hàng và trang Kiểm kê của Nhập liệu
              || ((db == "ton-vi-tri-an-an" || db == "tong-hop" || db == "nhap-lieu") && Perm.Has(u, "m8"))
              // Đối chiếu kiểm kê đọc trang Nhập liệu, Tồn kho An An, Tồn theo vị trí, Sơ đồ kho, Tổng hợp
              || ((db is "nhap-lieu" or "ton-vi-tri-an-an" or "so-do-vi-tri" or "tong-hop" || db.Contains("ton-kho-an-an")) && Perm.Has(u, "m9"));
        if (!ok) throw new BusinessException($"Tài khoản \"{username}\" không có quyền {(write ? "sửa" : "xem")} dữ liệu này.");
    }

    private bool Applied(string opId)
    {
        using var c = _db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM lan_applied WHERE op_id=@opId", new { opId }) > 0;
    }

    private void MarkApplied(string opId)
    {
        if (opId.Length == 0) return;
        using var c = _db.Open();
        c.Execute("INSERT OR IGNORE INTO lan_applied(op_id, at) VALUES(@opId, @at)", new { opId, at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
        c.Execute("DELETE FROM lan_applied WHERE at < @old", new { old = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss") });
    }

    /// <summary>Gộp một mục trong sổ chờ của máy con vào database chính.</summary>
    private JsonObject ApplySyncItem(JsonObject it, string user, string machine, bool fromCapable)
    {
        var opId = it["op"]?.ToString() ?? "";
        var db = it["db"]?.ToString() ?? ""; var sheet = it["sheet"]?.ToString() ?? "";
        var bv = it["bv"]?.GetValue<long>() ?? -1;
        var bd = it["bd"]?.ToString(); var d = it["d"]?.ToString();
        JsonObject R(string st, string note = "") => new() { ["op"] = opId, ["db"] = db, ["sheet"] = sheet, ["st"] = st, ["note"] = note };
        try
        {
            ModuleStore.CheckName("database", db); ModuleStore.CheckName("sheet", sheet);
            // bảng do máy chủ cũ gửi khi nhường vai (không rõ người sửa) → không kiểm quyền từng người
            if (!(fromCapable && user.Length == 0)) RequireRemote(user, db, write: true);
        }
        catch (BusinessException e) { return R("loi", e.Message); }
        lock (_dataLock)
        {
            if (opId.Length > 0 && Applied(opId)) return R("trung");
            // Lần gửi trước của chính mục này đã được áp (máy con không nhận được trả lời) → bản gốc đúng là nội dung lần đó.
            if (it["prev"] is JsonArray prev)
                foreach (var po in prev.OfType<JsonObject>().Reverse())
                    if (po["op"]?.ToString() is { Length: > 0 } pid && Applied(pid)) { bd = po["d"]?.ToString(); bv = -1; break; }
            var (curJson, curV) = _store.ReadVersioned(db, sheet);
            string? cur = Exists(db, sheet) ? curJson : null;
            JsonObject res;
            if (bv == -2 && !SameJson(d, cur))
            {
                LogConflict(machine, user, db, sheet, "cho", bd, d, cur, null, "hai máy cùng làm máy chủ, không tìm được bản chung để gộp");
                res = R("xungdot", "không có bản chung");
            }
            else if (bv == -2) res = R("ok", "hai bên giống nhau");
            else if (bv >= 0 && curV == bv)
            {
                _store.WriteRaw(db, new Dictionary<string, string?> { [sheet] = d }, null, user);
                res = R("ok", "không ai sửa trong lúc mất máy chủ");
            }
            else
            {
                var m = SheetMerge.ThreeWay(bd, d, cur);
                if (m.Clean)
                {
                    var changed = !SameJson(m.Merged, cur);
                    if (changed) _store.WriteRaw(db, new Dictionary<string, string?> { [sheet] = m.Merged }, null, user);
                    var bothChanged = !SameJson(cur, bd) && !SameJson(d, bd) && !SameJson(d, cur);
                    if (bothChanged) LogConflict(machine, user, db, sheet, "gop", bd, d, cur, m.Merged, m.Note);
                    res = R(bothChanged ? "gop" : "ok", m.Note);
                }
                else
                {
                    LogConflict(machine, user, db, sheet, "cho", bd, d, cur, null, m.Note);
                    res = R("xungdot", m.Note);
                }
            }
            MarkApplied(opId);
            _lastWriteAt = DateTime.Now;
            return res;
        }
    }

    private static bool SameJson(string? a, string? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a == b) return true;
        try { return JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b)); } catch { return false; }
    }

    private bool Exists(string db, string sheet)
    {
        using var c = _db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM module_sheets WHERE db_key=@db AND sheet=@sheet", new { db, sheet }) > 0;
    }

    private void LogConflict(string machine, string user, string db, string sheet, string status, string? b, string? client, string? server, string? result, string note)
    {
        using var c = _db.Open();
        c.Execute("""
            INSERT INTO lan_conflicts(at, machine, username, db_key, sheet, status, base_data, client_data, server_data, result_data, note)
            VALUES(@at, @machine, @user, @db, @sheet, @status, @b, @client, @server, @result, @note)
            """, new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), machine, user, db, sheet, status, b, client, server, result, note });
    }

    // =====================================================================================
    // Phía máy không làm chủ: tìm máy chủ, đọc / ghi, sổ chờ
    // =====================================================================================

    private JsonObject Req(string op)
    {
        return new JsonObject
        {
            ["k"] = _key, ["op"] = op, ["m"] = MachineId, ["mn"] = MachineName, ["r"] = Role, ["u"] = _session.Username,
            ["a"] = Capable ? new JsonArray(MyAddrs().Select(a => (JsonNode)a).ToArray()) : null, ["pend"] = PendingCount(),
            ["ep"] = Math.Max(Epoch, _maxSeenEpoch),
        };
    }

    /// <summary>Bắt buộc mã hóa (không nhận / không dùng kênh thường). Đặt lan_enc = 1 (máy chủ trên Internet nên bật).</summary>
    public bool RequireEnc => _settings.Get("lan_enc") == "1";
    private ILanConn Conn(string ep) => _conns.GetOrAdd(ep, e => LanHttp.IsUrl(e) ? new HttpLanConnection(e, _key) : new LanConnection(e, _key, RequireEnc));

    private async Task<JsonObject> CallNode(Node n, JsonObject req, TimeSpan timeout, CancellationToken ct)
    {
        if (TestBeforeCall is { } before) await before(req);
        LanUnreachableException? last = null;
        var key = string.Join(";", n.Addrs);
        var order = _goodEp.TryGetValue(key, out var good) && n.Addrs.Contains(good) ? n.Addrs.OrderBy(a => a == good ? 0 : 1).ToList() : n.Addrs;
        foreach (var ep in order)
        {
            try { var r = await Conn(ep).CallAsync(req, timeout, ct); _goodEp[key] = ep; return r; }
            catch (LanUnreachableException e) { last = e; }
        }
        throw last ?? new LanUnreachableException("Không có địa chỉ.");
    }

    /// <summary>Gọi máy đang làm chủ; mất kết nối → chuyển sang làm riêng.</summary>
    private JsonObject CallServer(JsonObject req, TimeSpan? timeout = null)
    {
        var n = _server ?? throw new LanUnreachableException("Chưa nối máy chủ.");
        try
        {
            var r = Task.Run(() => CallNode(n, req, timeout ?? TimeSpan.FromSeconds(30), CancellationToken.None)).GetAwaiter().GetResult();
            if (r["notServing"]?.GetValue<bool>() == true) { GoOffline(); throw new LanUnreachableException("Máy chủ vừa đổi."); }
            Learn(r["nodes"] as JsonArray);
            return r;
        }
        catch (LanUnreachableException) { GoOffline(); throw; }
    }

    /// <summary>1.121: đang nối qua Internet → thử các địa chỉ trong kho của máy chủ (chờ ngắn); được thì ghi nhớ địa chỉ LAN để mọi lượt sau đi LAN.</summary>
    internal async Task<bool> ProbeLan(CancellationToken ct)
    {
        if (_server is not { } sv) return false;
        var fresh = KnownNodes().FirstOrDefault(k => k.Id == sv.Id && k.Id.Length > 0)?.Addrs ?? sv.Addrs;
        var addrs = fresh.Concat(sv.Addrs).Distinct().ToList();
        foreach (var ep in addrs.Where(a => !LanHttp.IsUrl(a)))
        {
            try
            {
                var h = await Conn(ep).CallAsync(Req("hello"), TimeSpan.FromSeconds(1.5), ct);
                if (h["ok"]?.GetValue<bool>() != true || h["serving"]?.GetValue<bool>() != true || h["id"]?.ToString() != sv.Id) continue;
                lock (_state)
                {
                    if (_server?.Id != sv.Id) return false;
                    var n = new Node(sv.Id, sv.Name, sv.Role, addrs);
                    _server = n;
                    _goodEp[string.Join(";", n.Addrs)] = ep;
                }
                Raise();
                return true;
            }
            catch (LanUnreachableException) { }
        }
        return false;
    }

    // ------------------------------------------------------------------ 1.121: máy đơn dùng bản sao lưu Drive cá nhân của máy chủ

    /// <summary>
    /// Máy đơn (không nối được máy chủ): hỏi xem có bản sao lưu của máy chủ MỚI HƠN lần đồng bộ cuối của máy này không
    /// (tham số: tên máy chủ, lần đồng bộ cuối). Có → tải về file .db tạm và trả (đường dẫn, mô tả, giờ sao lưu); không → null.
    /// AppServices nối với Drive cá nhân (cùng Gmail đã đăng nhập ở tab Sao lưu).
    /// </summary>
    public Func<string, DateTime?, CancellationToken, Task<(string Path, string Label, DateTime At)?>>? OfflineSnapshot { get; set; }
    /// <summary>Gọi ngay trước khi thay dữ liệu bằng bản Drive (AppServices: chụp bản hiện tại vào Sao lưu trên máy).</summary>
    public Action? BeforeOfflineSnapshot { get; set; }
    private DateTime _lastSnapCheck = DateTime.MinValue;
    private int _snapBusy;

    private async Task MaybeOfflineSnapshot(DateTime now, CancellationToken ct)
    {
        if (OfflineSnapshot is not { } get || Role != RoleClient) return;
        DateTime fail; lock (_state) fail = _failSince;
        if (fail == DateTime.MinValue || now - fail < T(60) || now - _lastSnapCheck < T(1800)) return;
        if (PendingCount() > 0) return;   // có thay đổi chờ gửi → giữ dữ liệu của máy (không mất phần đang sửa)
        var server = _settings.Get("lan_server_name");
        if (server.Length == 0 || Interlocked.Exchange(ref _snapBusy, 1) == 1) return;
        _lastSnapCheck = now;
        try
        {
            var since = LastSyncedAt;
            if (_settings.Get("lan_offline_at") is { Length: > 0 } oa && DateTime.TryParse(oa, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var o) && (since is null || o > since)) since = o;
            var snap = await get(server, since, ct);
            if (snap is { } x)
                try { LoadOfflineSnapshot(x.Path, x.Label, x.At); }
                finally { try { File.Delete(x.Path); } catch { } }
        }
        catch (Exception e) { Log("Máy đơn: không tải được bản Drive", e.Message); }
        finally { Interlocked.Exchange(ref _snapBusy, 0); }
    }

    /// <summary>
    /// Thay dữ liệu module của máy này bằng bản sao lưu của máy chủ (chỉ khi đang là máy đơn và không có thay đổi chờ gửi).
    /// Không tách khỏi LAN: nối lại máy chủ thì tải lại toàn bộ dữ liệu thật; phần sửa sau khi nạp gửi lên theo sổ chờ như thường.
    /// </summary>
    internal bool LoadOfflineSnapshot(string dbPath, string label, DateTime at)
    {
        var rows = new List<(string db, string sheet, string data, long version)>();
        using (var src = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            src.Open();
            rows = src.Query<(string, string, string, long)>("SELECT db_key, sheet, data, version FROM module_sheets").ToList();
        }
        if (rows.Count == 0) return false;
        lock (_dataLock)
        {
            if (Online || Serving || PendingCount() > 0) return false;
            BeforeOfflineSnapshot?.Invoke();
            var have = rows.Select(r => r.db + "/" + r.sheet).ToHashSet();
            foreach (var (db, sheet, data, version) in rows) _store.SetExact(db, sheet, data, version);
            foreach (var (db, sheet, _) in _store.AllVersions())
                if (!have.Contains(db + "/" + sheet)) _store.SetExact(db, sheet, null, 0);
            _settings.Set("lan_offline_src", label);
            _settings.Set("lan_offline_at", at.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            _settings.Set("lan_sync_epoch", ""); _settings.Set("lan_last_seq", "0");   // nối lại máy chủ → tải lại toàn bộ
        }
        Log("Máy đơn: dùng bản Drive của máy chủ", label);
        Raise();
        return true;
    }

    /// <summary>1.121: nút "Nối lại ngay" (máy đơn) — vòng sau thử tìm máy chủ ngay, không chờ nhịp 15 giây.</summary>
    public void RetryNow() { _lastTry = DateTime.MinValue; _lastLanProbe = DateTime.MinValue; }

    /// <summary>1.121: các thay đổi đang chờ gửi lên máy chủ (để giao diện liệt kê): khóa db, bảng, lúc, người sửa.</summary>
    public List<(string DbKey, string Sheet, string At, string User)> PendingList()
    {
        using var c = _db.Open();
        return c.Query<(string, string, string, string)>("SELECT db_key, sheet, created_at, username FROM lan_pending ORDER BY created_at DESC").ToList();
    }

    private void GoOffline()
    {
        lock (_state)
        {
            if (Online) _failSince = DateTime.Now;
            Online = false; _server = null;
        }
        Raise();
    }

    private async Task<bool> FindServer(CancellationToken ct)
    {
        Node? best = null; long bestEpoch = -1;
        Interlocked.Increment(ref FindRounds);
        foreach (var n in KnownNodes())
        {
            try
            {
                var h = await CallNode(n, Req("hello"), TimeSpan.FromSeconds(2), ct);
                if (h["ok"]?.GetValue<bool>() != true) continue;
                Learn(h["nodes"] as JsonArray);
                var full = new Node(h["id"]!.ToString(), h["name"]!.ToString(), h["role"]!.ToString(), n.Addrs);
                if (h["serving"]?.GetValue<bool>() == true)
                {
                    var ep = h["epoch"]!.GetValue<long>();
                    if (ep > bestEpoch) { best = full; bestEpoch = ep; }
                }
            }
            catch (LanUnreachableException) { }
        }
        if (best is null)
        {
            lock (_state) { if (_failSince == DateTime.MinValue) _failSince = DateTime.Now; _findMisses++; }
            return false;
        }
        lock (_state) _findMisses = 0;
        if (_settings.Get("lan_serving") == "1" && !Serving)
        {
            // Mình làm chủ trước khi tắt máy mà giờ đã có máy khác làm chủ: gửi phần mình đã sửa lên để gộp.
            var h = await CallNode(best, Req("hello"), TimeSpan.FromSeconds(2), ct);
            await Demote(best, bestEpoch, h["epochStart"]?.GetValue<long>() ?? 0, ct);
            return true;
        }
        lock (_state) { _server = best; _serverEpoch = bestEpoch; Online = true; _failSince = DateTime.MinValue; if (bestEpoch > Epoch && !Serving) SetEpoch(bestEpoch, _epochStart); }
        if (_settings.Get("lan_server_name") != best.Name) _settings.Set("lan_server_name", best.Name);   // 1.121: máy đơn tìm bản Drive theo tên máy chủ
        Raise();
        return true;
    }

    private async Task Pull(CancellationToken ct, bool full)
    {
        using var _perf = KhoLanh.Core.TroLy.Perf.Measure("Đồng bộ LAN", full ? "tải lại toàn bộ" : "nhận thay đổi");
        if (_server is not { } n) return;
        _lastPull = DateTime.Now;
        var since = full ? 0 : long.TryParse(_settings.Get("lan_last_seq"), out var s) ? s : 0;
        if (_settings.Get("lan_sync_epoch") != _serverEpoch.ToString()) since = 0; // máy chủ khác / nhiệm kỳ khác: tải lại toàn bộ
        var req = Req("changes"); req["since"] = since; req["seq"] = _store.CurrentSeq();
        var stamp = Interlocked.Read(ref _touchSeq);
        JsonObject r;
        try { r = await CallNode(n, req, TimeSpan.FromSeconds(60), ct); }
        catch (LanUnreachableException) { GoOffline(); return; }
        if (r["ok"]?.GetValue<bool>() != true) { if (r["notServing"]?.GetValue<bool>() == true) GoOffline(); return; }
        Learn(r["nodes"] as JsonArray);
        if (TestAfterPullResponse is { } hook) await hook();
        var seq = r["seq"]!.GetValue<long>();
        lock (_dataLock)
        {
            var pending = PendingKeys();
            // bảng đang chờ gửi, hoặc vừa ghi bản mới hơn phản hồi này (ghi / đọc trong lúc chờ phản hồi): giữ bản của mình
            bool Keep(string k) => pending.Contains(k) || (_touched.TryGetValue(k, out var t) && t > stamp);
            var got = new HashSet<string>();
            foreach (var it in (r["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                var db = it["db"]!.ToString(); var sheet = it["sheet"]!.ToString();
                got.Add(db + "/" + sheet);
                if (Keep(db + "/" + sheet)) continue;
                _store.SetExact(db, sheet, it["data"]?.ToString(), it["v"]!.GetValue<long>());
            }
            // Tải lại toàn bộ: bảng cục bộ không có trên máy chủ thì xóa — TRỪ bảng có phiên bản mới hơn mốc máy chủ vừa báo
            // (vừa ghi xong sau khi máy chủ lập câu trả lời; xóa nhầm thì lần sửa kế tiếp lúc mất máy chủ mất bản gốc → gộp nhân đôi dòng).
            if (since == 0)
                foreach (var (db, sheet, ver) in _store.AllVersions())
                    if (!got.Contains(db + "/" + sheet) && !Keep(db + "/" + sheet) && ver <= seq)
                        _store.SetExact(db, sheet, null, 0);
        }
        Interlocked.Increment(ref _pullsApplied);
        var ep = r["epoch"]?.GetValue<long>() ?? 0;
        if (ep != _serverEpoch) { _serverEpoch = ep; _settings.Set("lan_sync_epoch", ""); return; } // đổi nhiệm kỳ giữa chừng: lượt sau tải lại
        _settings.Set("lan_last_seq", seq.ToString());
        _settings.Set("lan_sync_epoch", ep.ToString());
        _settings.Set("lan_synced_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));   // 1.121
        if (_settings.Get("lan_offline_src").Length > 0) _settings.Set("lan_offline_src", "");   // 1.121: đã nhận dữ liệu thật của máy chủ
        Raise();
        if (ep > Epoch && !Serving) SetEpoch(ep, r["epochStart"]?.GetValue<long>() ?? 0);
        if (r["handover"]?.GetValue<bool>() == true && Role == RoleServer && !Serving)
            Promote("máy chủ chính nhận lại vai", ep + 1);
    }

    private async Task Beat(CancellationToken ct)
    {
        // Máy con báo cho các máy có thể làm chủ (trừ máy đang nối): còn sống + có thấy máy chủ không. Dùng để máy dự phòng
        // chỉ lên làm chủ khi quá nửa số máy con cũng mất máy chủ (tránh hai máy chủ khi chỉ đứt mạng một đoạn).
        foreach (var n in KnownNodes().Where(n => n.Id != _server?.Id))
        {
            var req = Req("beat"); req["down"] = !Online;
            try { await CallNode(n, req, TimeSpan.FromSeconds(2), ct); } catch (LanUnreachableException) { }
        }
    }

    private async Task MaybePromote(CancellationToken ct)
    {
        // 1.131: máy đã trả vai (cài đặt không còn là vai đang chạy — vd máy chủ cũ sau khi máy khác nhận vai) thì không tự lên làm chủ
        if (_settings.Get("lan_role") is { Length: > 0 } saved && saved != Role) return;
        // Máy chủ chính mới mở mà không thấy ai làm chủ → làm chủ luôn.
        if (Role == RoleServer && DateTime.Now - _startedAt < T(PromoteAfterSeconds + 5))
        {
            // chờ vài giây để nghe nhịp của các máy con (biết nhiệm kỳ mới nhất)
            if (KnownNodes().Count == 0 || _failSince != DateTime.MinValue && DateTime.Now - _failSince > T(6))
            { Promote("máy chủ chính mở máy", Math.Max(Epoch, _maxSeenEpoch) + 1); return; }
        }
        if (_failSince == DateTime.MinValue || DateTime.Now - _failSince < T(PromoteAfterSeconds)) return;
        if (_findMisses < PromoteAfterMisses) return;   // 1.129: thêm điều kiện số lượt hỏi hụt liên tiếp, không chỉ thời gian
        // Đếm phiếu của máy con.
        var active = _seen.Values.Where(s => s.Role == RoleClient && DateTime.Now - s.At < T(60)).ToList();
        var down = active.Count(s => s.ServerDown && DateTime.Now - s.At < T(15));
        if (active.Count > 0 && down * 2 < active.Count) return;
        await Task.CompletedTask;
        Promote("không thấy máy chủ quá " + PromoteAfterSeconds + " giây", Math.Max(Epoch, _maxSeenEpoch) + 1);
    }

    private void SetEpoch(long epoch, long start)
    {
        Epoch = epoch; _epochStart = start;
        _settings.Set("lan_epoch", epoch.ToString());
        _settings.Set("lan_epoch_start", start.ToString());
    }

    /// <summary>Lên làm máy chủ: áp sổ chờ của chính mình vào database, tăng nhiệm kỳ.</summary>
    private void Promote(string why, long epoch)
    {
        lock (_dataLock)
        {
            var start = _store.CurrentSeq();
            foreach (var p in PendingRows())
                _store.WriteRaw(p.db_key, new Dictionary<string, string?> { [p.sheet] = p.data }, null, p.username);
            using (var c = _db.Open()) c.Execute("DELETE FROM lan_pending");
            SetEpoch(epoch, start);
            Serving = true; _frozen = false; Online = true; _server = null;
            _settings.Set("lan_serving", "1");
        }
        Log("Lên làm máy chủ", $"nhiệm kỳ {epoch}: {why}");
        Raise();
    }

    private async Task CheckPeers(CancellationToken ct)
    {
        // các máy có thể làm chủ đã biết + các máy có thể làm chủ vừa gọi tới mình (để hai máy chủ luôn thấy nhau)
        var nodes = KnownNodes().Where(n => n.Role is RoleServer or RoleStandby || n.Id.Length == 0).ToList();
        foreach (var sn in _seen.Values.Where(x => x.Role is RoleServer or RoleStandby && x.Addr.Length > 0 && DateTime.Now - x.At < TimeSpan.FromMinutes(10)))
            if (!nodes.Any(n => n.Id == sn.Id)) nodes.Add(new Node(sn.Id, sn.Name, sn.Role, sn.Addr.Split(';').ToList()));
        foreach (var n in nodes)
        {
            JsonObject h;
            try { h = await CallNode(n, Req("hello"), TimeSpan.FromSeconds(2), ct); }
            catch (LanUnreachableException) { continue; }
            if (h["ok"]?.GetValue<bool>() != true || h["serving"]?.GetValue<bool>() != true) continue;
            var ep = h["epoch"]!.GetValue<long>();
            var otherId = h["id"]!.ToString();
            var otherWins = ep > Epoch || (ep == Epoch && string.CompareOrdinal(otherId, MachineId) > 0);
            if (otherWins)
            {
                await Demote(new Node(otherId, h["name"]!.ToString(), h["role"]!.ToString(), n.Addrs), ep, h["epochStart"]?.GetValue<long>() ?? 0, ct);
                return;
            }
        }
    }

    /// <summary>
    /// Nhường vai cho máy làm chủ khác. Bảng nào mình sửa trong nhiệm kỳ của mình mà khác bản bên kia → đưa vào sổ chờ để
    /// gộp vào máy chủ mới. Bản gốc = bản gần nhất mà cả hai bên cùng có (cùng số phiên bản và cùng nội dung); không có thì
    /// để Quản trị chọn. Rồi tải lại toàn bộ.
    /// </summary>
    private async Task Demote(Node winner, long epoch, long winnerStart, CancellationToken ct)
    {
        long myStart;
        lock (_dataLock) { Serving = false; _frozen = false; myStart = _epochStart; }
        Raise();
        var queue = new List<(string db, string sheet, string? data, long bv, string? bd)>();
        foreach (var r in _store.ChangesSince(myStart).GroupBy(r => (r.DbKey, r.Sheet)).Select(g => g.Last()))
        {
            var rq = Req("hist"); rq["db"] = r.DbKey; rq["sheet"] = r.Sheet;
            var res = await CallNode(winner, rq, TimeSpan.FromSeconds(30), ct);
            var theirs = (res["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(o => (v: o["v"]!.GetValue<long>(), data: o["data"]?.ToString())).ToList();
            var mine = _store.History(r.DbKey, r.Sheet);
            var cur = mine.FirstOrDefault();
            if (cur is null) continue;
            if (theirs.Count > 0 && SameJson(theirs[0].data, cur.Data)) continue; // bên kia đã có đúng bản này
            var common = mine.FirstOrDefault(m => theirs.Any(t => t.v == m.Version && SameJson(t.data, m.Data)));
            if (common is null && theirs.Count == 0)
                queue.Add((r.DbKey, r.Sheet, cur.Data, -1, null)); // bên kia chưa từng có bảng này
            else if (common is null)
                queue.Add((r.DbKey, r.Sheet, cur.Data, -2, null));
            else
                queue.Add((r.DbKey, r.Sheet, cur.Data, -1, common.Data));
        }
        lock (_dataLock)
        {
            _settings.Set("lan_serving", "");
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            using var c = _db.Open();
            foreach (var q in queue)
                c.Execute("""
                    INSERT INTO lan_pending(db_key, sheet, op_id, data, base_version, base_data, username, created_at)
                    VALUES(@db, @sheet, @op, @data, @bv, @bd, @user, @now)
                    ON CONFLICT(db_key, sheet) DO UPDATE SET op_id=excluded.op_id, data=excluded.data, sent=0,
                        prev_ops=CASE WHEN lan_pending.sent=1 THEN json_insert(lan_pending.prev_ops, '$[#]', json_object('op', lan_pending.op_id, 'd', lan_pending.data)) ELSE lan_pending.prev_ops END
                    """, new { q.db, q.sheet, op = Guid.NewGuid().ToString("N"), q.data, q.bv, q.bd, user = "", now });
            SetEpoch(epoch, winnerStart);
            _settings.Set("lan_last_seq", "0");
            _settings.Set("lan_sync_epoch", "");
            _server = winner; _serverEpoch = epoch; Online = true; _failSince = DateTime.MinValue; _findMisses = 0;
        }
        Log("Nhường vai máy chủ", $"cho {winner.Name} (nhiệm kỳ {epoch}), {queue.Count} bảng gửi lên gộp");
        Raise();
    }

    // ----- sổ chờ -----

    public int PendingCount()
    {
        using var c = _db.Open();
        return (int)c.ExecuteScalar<long>("SELECT COUNT(*) FROM lan_pending");
    }

    private HashSet<string> PendingKeys()
    {
        using var c = _db.Open();
        return c.Query<(string, string)>("SELECT db_key, sheet FROM lan_pending").Select(x => x.Item1 + "/" + x.Item2).ToHashSet();
    }

    private bool HasPending(string db, string sheet)
    {
        using var c = _db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM lan_pending WHERE db_key=@db AND sheet=@sheet", new { db, sheet }) > 0;
    }

    private List<(string db_key, string sheet, string op_id, string? data, long base_version, string? base_data, string username, string prev_ops)> PendingRows()
    {
        using var c = _db.Open();
        return c.Query<(string, string, string, string?, long, string?, string, string)>(
            "SELECT db_key, sheet, op_id, data, base_version, base_data, username, prev_ops FROM lan_pending ORDER BY created_at").ToList();
    }

    /// <summary>Đánh dấu các mục sổ chờ sắp gửi đi (máy chủ có thể áp xong mà câu trả lời không về được).</summary>
    private void MarkSent(IEnumerable<string> opIds)
    {
        lock (_dataLock)
        {
            using var c = _db.Open();
            foreach (var op in opIds) c.Execute("UPDATE lan_pending SET sent=1 WHERE op_id=@op", new { op });
        }
    }

    /// <summary>1 mục sổ chờ dạng gửi lên máy chủ / máy chính (kèm các lần gửi trước chưa được xác nhận).</summary>
    /// <summary>
    /// Gửi sổ chờ theo từng đợt ≤ <see cref="SyncBatchChars"/> ký tự (theo thứ tự ghi, ít nhất 1 mục) — mất kết nối lâu, sửa nhiều bảng lớn thì
    /// không dồn thành 1 yêu cầu khổng lồ (đường hầm Cloudflare giới hạn 100 MB / yêu cầu); phần còn lại gửi ở lượt kế tiếp.
    /// </summary>
    public static long SyncBatchChars { get; set; } = 40_000_000;
    internal static List<T> Batch<T>(List<T> rows) where T : struct, System.Runtime.CompilerServices.ITuple
    {
        var res = new List<T>(); long sum = 0;
        foreach (var r in rows)
        {
            long n = 0; for (var i = 0; i < r.Length; i++) if (r[i] is string str) n += str.Length;
            if (res.Count > 0 && sum + n > SyncBatchChars) break;
            res.Add(r); sum += n;
        }
        return res;
    }

    private static JsonObject ItemOf((string db_key, string sheet, string op_id, string? data, long base_version, string? base_data, string username, string prev_ops) p)
    {
        var o = new JsonObject { ["op"] = p.op_id, ["db"] = p.db_key, ["sheet"] = p.sheet, ["bv"] = p.base_version, ["bd"] = p.base_data, ["d"] = p.data, ["u"] = p.username };
        try { if (JsonNode.Parse(p.prev_ops) is JsonArray pa && pa.Count > 0) o["prev"] = pa; } catch { /* hỏng thì bỏ */ }
        return o;
    }

    private void WritePending(string db, IDictionary<string, string?> sheets)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        lock (_dataLock)
        {
            using var c = _db.Open();
            foreach (var (sheet, data) in sheets)
            {
                var (curJson, curV) = _store.ReadVersioned(db, sheet);
                var exists = Exists(db, sheet);
                c.Execute("""
                    INSERT INTO lan_pending(db_key, sheet, op_id, data, base_version, base_data, username, created_at)
                    VALUES(@db, @sheet, @op, @data, @bv, @bd, @user, @now)
                    ON CONFLICT(db_key, sheet) DO UPDATE SET op_id=excluded.op_id, data=excluded.data, sent=0,
                        prev_ops=CASE WHEN lan_pending.sent=1 THEN json_insert(lan_pending.prev_ops, '$[#]', json_object('op', lan_pending.op_id, 'd', lan_pending.data)) ELSE lan_pending.prev_ops END
                    """, new { db, sheet, op = Guid.NewGuid().ToString("N"), data, bv = curV, bd = exists ? curJson : null, user = _session.Username, now });
                // database riêng của máy: ghi thẳng, giữ số phiên bản cũ (= bản gốc)
                _store.SetExact(db, sheet, data, curV);
            }
        }
        Raise();
    }

    /// <summary>Gửi sổ chờ lên máy chủ để gộp.</summary>
    private async Task Flush(CancellationToken ct)
    {
        if (_server is not { } n) return;
        var rows = Batch(PendingRows());
        if (rows.Count == 0) return;
        var req = Req("sync");
        req["items"] = new JsonArray(rows.Select(p => (JsonNode)ItemOf(p)).ToArray());
        MarkSent(rows.Select(p => p.op_id));
        JsonObject r;
        try { r = await CallNode(n, req, TimeSpan.FromSeconds(120), ct); }
        catch (LanUnreachableException) { GoOffline(); return; }
        if (r["ok"]?.GetValue<bool>() != true) { if (r["notServing"]?.GetValue<bool>() == true) GoOffline(); else LastSync = "Gửi sổ chờ lỗi: " + r["error"]; Raise(); return; }
        Learn(r["nodes"] as JsonArray);
        int ok = 0, merged = 0, conflict = 0, err = 0;
        var errors = new List<string>();
        foreach (var it in (r["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var st = it["st"]?.ToString();
            if (st == "loi") { err++; errors.Add($"{it["sheet"]}: {it["note"]}"); continue; } // giữ trong sổ chờ
            if (st == "gop") merged++; else if (st == "xungdot") conflict++; else ok++;
            using (var c = _db.Open()) c.Execute("DELETE FROM lan_pending WHERE op_id=@op", new { op = it["op"]!.ToString() });
            // tải bản trên máy chủ về (đã gộp / bản chính khi xung đột) — trừ khi người dùng vừa sửa tiếp bảng này (còn mục chờ mới hơn)
            if (HasPending(it["db"]!.ToString(), it["sheet"]!.ToString())) continue;
            try
            {
                var rq = Req("read"); rq["db"] = it["db"]!.ToString(); rq["sheet"] = it["sheet"]!.ToString();
                var rr = await CallNode(n, rq, TimeSpan.FromSeconds(60), ct);
                if (rr["ok"]?.GetValue<bool>() == true)
                    lock (_dataLock)
                    {
                        var (fdb, fsh) = (it["db"]!.ToString(), it["sheet"]!.ToString());
                        if (HasPending(fdb, fsh)) continue; // vừa sửa tiếp khi đang gửi: giữ bản mới, lượt sau gửi tiếp
                        _store.SetExact(fdb, fsh, rr["data"]!.ToString() == "[]" && rr["v"]!.GetValue<long>() == 0 ? null : rr["data"]!.ToString(), rr["v"]!.GetValue<long>());
                        Touch(fdb, fsh);
                    }
            }
            catch (LanUnreachableException) { GoOffline(); }
        }
        LastSync = $"{DateTime.Now:HH:mm} đã gửi {ok + merged + conflict} bảng chờ: {ok} lấy thẳng, {merged} tự gộp, {conflict} trùng sửa (Quản trị chọn ở máy chủ)"
                   + (err > 0 ? $", {err} lỗi: {string.Join("; ", errors)}" : "");
        Log("Đồng bộ sổ chờ", LastSync);
        Raise();
    }

    // =====================================================================================
    // ISheetSource — module đọc / ghi
    // =====================================================================================

    private static string K(string db, string sheet) => db + "/" + sheet;

    public string Read(string dbKey, string sheet)
    {
        ModuleStore.CheckName("database", dbKey); ModuleStore.CheckName("sheet", sheet);
        ReadTrack.Note(dbKey);
        if (Serving || !Enabled || HasPending(dbKey, sheet) || !Online)
        {
            var (json, v) = _store.ReadVersioned(dbKey, sheet);
            _readVersions[K(dbKey, sheet)] = v;
            return json;
        }
        try
        {
            var req = Req("read"); req["db"] = dbKey; req["sheet"] = sheet;
            var r = CallServer(req);
            if (r["ok"]?.GetValue<bool>() != true) throw new BusinessException(r["error"]?.ToString() ?? "Máy chủ báo lỗi.");
            var data = r["data"]!.ToString(); var v = r["v"]!.GetValue<long>();
            lock (_dataLock)
                if (!HasPending(dbKey, sheet)) { _store.SetExact(dbKey, sheet, v == 0 && data == "[]" && !Exists(dbKey, sheet) ? null : data, v); Touch(dbKey, sheet); }
            _readVersions[K(dbKey, sheet)] = v;
            return data;
        }
        catch (LanUnreachableException)
        {
            var (json, v) = _store.ReadVersioned(dbKey, sheet);
            _readVersions[K(dbKey, sheet)] = v;
            return json;
        }
    }

    public List<string> List(string dbKey)
    {
        ReadTrack.Note(dbKey);
        if (Serving || !Enabled || !Online) return _store.ListSheets(dbKey);
        try
        {
            var req = Req("list"); req["db"] = dbKey;
            var r = CallServer(req);
            if (r["ok"]?.GetValue<bool>() != true) throw new BusinessException(r["error"]?.ToString() ?? "Máy chủ báo lỗi.");
            var remote = (r["sheets"] as JsonArray ?? new JsonArray()).Select(x => x!.ToString()).ToList();
            var pend = PendingKeys().Where(k => k.StartsWith(dbKey + "/")).Select(k => k[(dbKey.Length + 1)..]);
            return remote.Union(pend).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        catch (LanUnreachableException) { return _store.ListSheets(dbKey); }
    }

    public void Write(string dbKey, IDictionary<string, JsonArray?> sheets)
    {
        ModuleStore.CheckName("database", dbKey);
        foreach (var n in sheets.Keys) ModuleStore.CheckName("sheet", n);
        if (Role == RoleRemote && _session.CurrentUser is not null && !_session.Can("drivepush", Models.Perm.Edit))
            throw new BusinessException("Tài khoản này chỉ được XEM ở máy ngoài công ty (chưa được cấp quyền «Gửi dữ liệu từ máy xem từ xa») — liên hệ Quản trị.");
        var raw = sheets.ToDictionary(p => p.Key, p => p.Value is null ? null : ModuleStore.CleanRows(p.Value));
        Dictionary<string, long> Exp() => raw.Keys.Where(s => _readVersions.ContainsKey(K(dbKey, s))).ToDictionary(s => s, s => _readVersions[K(dbKey, s)]);
        if (!Enabled)
        {
            _store.WriteSheets(dbKey, sheets);
            return;
        }
        if (Serving)
        {
            if (_frozen) throw new BusinessException("Đang chuyển máy chủ (vài giây) — bấm lưu lại sau giây lát.");
            lock (_dataLock)
            {
                var vers = _store.WriteSheets(dbKey, sheets, Exp());
                foreach (var (s, v) in vers) _readVersions[K(dbKey, s)] = v;
                _lastWriteAt = DateTime.Now;
            }
            return;
        }
        var anyPending = raw.Keys.Any(s => HasPending(dbKey, s));
        if (Online && !anyPending)
        {
            try
            {
                var req = Req("write"); req["db"] = dbKey; req["opId"] = Guid.NewGuid().ToString("N");
                req["sheets"] = new JsonObject(raw.Select(p => KeyValuePair.Create(p.Key, p.Value is null ? null : JsonNode.Parse(p.Value))));
                req["exp"] = new JsonObject(Exp().Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value)));
                var r = CallServer(req);
                if (r["ok"]?.GetValue<bool>() == true)
                {
                    lock (_dataLock)
                        foreach (var (s, node) in r["vers"]!.AsObject())
                        {
                            var v = node!.GetValue<long>();
                            _store.SetExact(dbKey, s, raw[s], v);
                            Touch(dbKey, s);
                            _readVersions[K(dbKey, s)] = v;
                        }
                    return;
                }
                if (r["conflict"]?.GetValue<bool>() == true) throw new SheetConflictException(dbKey, r["sheet"]?.ToString() ?? "");
                throw new BusinessException(r["error"]?.ToString() ?? "Máy chủ báo lỗi.");
            }
            catch (LanUnreachableException) { /* mất máy chủ giữa chừng → ghi vào database riêng + sổ chờ */ }
        }
        WritePending(dbKey, raw);
    }

    // =====================================================================================
    // Xung đột (trên máy đang làm chủ)
    // =====================================================================================

    public record Conflict(long Id, string At, string Machine, string Username, string DbKey, string Sheet, string Status, string Note,
        int ClientRows, int ServerRows, int ResultRows)
    {
        public string StatusLabel => Status switch
        {
            "cho" => "Chờ Quản trị chọn", "gop" => "Đã tự gộp (xem lại)", "maycon" => "Đã dùng bản máy con",
            "maychu" => "Đã giữ bản máy chủ", "hoantac" => "Đã hoàn tác gộp", "daxem" => "Đã xem", _ => Status,
        };
        public string ModuleLabel => DbKey switch { "ton-kho-an-an" => "Tồn kho An An", "ton-kho-gui" => "Tồn kho gửi", _ => DbKey };
        public bool Waiting => Status is "cho" or "gop";
    }

    public List<Conflict> Conflicts(bool all = false)
    {
        using var c = _db.Open();
        return c.Query<(long id, string at, string machine, string username, string db_key, string sheet, string status, string note, string? client_data, string? server_data, string? result_data)>(
            $"SELECT id, at, machine, username, db_key, sheet, status, note, client_data, server_data, result_data FROM lan_conflicts {(all ? "" : "WHERE status IN ('cho','gop')")} ORDER BY id DESC LIMIT 300")
            .Select(x => new Conflict(x.id, x.at, x.machine, x.username, x.db_key, x.sheet, x.status, x.note, Count(x.client_data), Count(x.server_data), Count(x.result_data)))
            .ToList();
    }

    private static int Count(string? json)
    {
        if (json is null) return 0;
        try { return JsonNode.Parse(json) is JsonArray a ? a.Count : 0; } catch { return 0; }
    }

    /// <summary>Quản trị chọn: "maycon" = dùng bản máy con · "maychu" = giữ bản máy chủ · "hoantac" = bỏ kết quả tự gộp · "daxem".</summary>
    public void Resolve(long id, string action)
    {
        _session.RequireAdmin();
        if (Enabled && !Serving) throw new BusinessException("Chỉ xử lý được trên máy đang làm máy chủ.");
        lock (_dataLock)
        {
            using var c = _db.Open();
            var x = c.QuerySingleOrDefault<(string db_key, string sheet, string status, string? client_data, string? server_data)?>(
                "SELECT db_key, sheet, status, client_data, server_data FROM lan_conflicts WHERE id=@id", new { id })
                ?? throw new BusinessException("Không thấy mục này.");
            switch (action)
            {
                case "maycon":
                    _store.WriteRaw(x.db_key, new Dictionary<string, string?> { [x.sheet] = x.client_data }, null, _session.Username); break;
                case "hoantac":
                    if (x.status != "gop") throw new BusinessException("Chỉ hoàn tác được mục đã tự gộp.");
                    _store.WriteRaw(x.db_key, new Dictionary<string, string?> { [x.sheet] = x.server_data }, null, _session.Username); break;
                case "maychu": case "daxem": break;
                default: throw new BusinessException("Lựa chọn không hợp lệ.");
            }
            c.Execute("UPDATE lan_conflicts SET status=@action WHERE id=@id", new { id, action });
            _lastWriteAt = DateTime.Now;
        }
        Log("Xử lý trùng sửa LAN", $"#{id}: {action}");
        Raise();
    }

    // =====================================================================================
    // Trạng thái hiển thị
    // =====================================================================================

    public List<Seen> Machines() => _seen.Values.Where(s => s.Id != MachineId && DateTime.Now - s.At < TimeSpan.FromMinutes(5)).OrderBy(s => s.Name).ToList();   // 1.118: bỏ chính máy này (Kiểm tra nối từ xa tự gọi qua đường hầm)
    /// <summary>1.118: máy đang nối chia 2 loại — trong mạng LAN / qua Internet (đường hầm).</summary>
    public (List<Seen> Lan, List<Seen> Net) MachinesByWay()
    {
        var all = Machines();
        return (all.Where(m => !m.ViaNet).ToList(), all.Where(m => m.ViaNet).ToList());
    }

    public string ServerName => Serving ? MachineName : _server?.Name ?? "";

    /// <summary>1.121: máy con đang nối máy chủ bằng đường nào — "lan" (trong kho), "net" (Internet / đường hầm), "" (không nối / đang làm máy chủ).
    /// App tự chọn: LAN trước, không được thì Internet, đang đi Internet thì cứ 60 giây dò lại LAN.</summary>
    public string Via => Serving || !Online || ServerAddress is not { } a ? "" : LanHttp.IsUrl(a) ? "net" : "lan";

    /// <summary>1.121: lần cuối nhận dữ liệu từ máy chủ (máy con) — settings <c>lan_synced_at</c>.</summary>
    public DateTime? LastSyncedAt => DateTime.TryParse(_settings.Get("lan_synced_at"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    /// <summary>1.121: máy đơn đang dùng bản sao lưu Drive cá nhân của máy chủ — chữ mô tả ("KHOLANH-PC · sao lưu 26/11 22:00"), "" = dữ liệu của chính máy.
    /// Xóa khi nối lại máy chủ.</summary>
    public string OfflineSource => _settings.Get("lan_offline_src");

    /// <summary>Một dòng ngắn cho thanh menu.</summary>
    public string Short
    {
        get
        {
            if (!Enabled) return "";
            if (Role == RoleRemote) { var pe = PendingCount(); return "Drive: " + (RemoteStatus.Length > 0 ? RemoteStatus : "chưa đồng bộ") + (pe > 0 ? $" · {pe} chờ gửi" : ""); }
            if (Serving)
            {
                if (_frozen) return "LAN: đang chuyển máy chủ…";
                var (l, n) = MachinesByWay();
                return $"LAN: máy chủ · {l.Count} máy đang nối" + (n.Count > 0 ? $" · {n.Count} máy qua Internet" : "");
            }
            var pend = PendingCount();
            if (Online) return $"LAN: nối {ServerName}" + (pend > 0 ? $" · {pend} chờ gửi" : "");
            return $"LAN: mất máy chủ — làm riêng" + (pend > 0 ? $" · {pend} chờ gộp" : "");
        }
    }

    /// <summary>0 = máy đơn, 1 = tốt, 2 = cảnh báo (làm riêng / chuyển máy chủ).</summary>
    public int Health => !Enabled ? 0 : Role == RoleRemote ? 1 : Serving ? (_frozen ? 2 : 1) : Online ? 1 : 2;

    private void Raise() { try { Changed?.Invoke(); } catch { } }

    private void Log(string action, string detail) { try { _audit.Log(_session.Username, action, detail); } catch { } }
}

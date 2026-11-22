using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Models;

namespace KhoLanh.Core.Services;

/// <summary>
/// Tài khoản + phân quyền DÙNG CHUNG cho mọi máy qua một Google Sheet (Apps Script Web App, giống cách app cũ nối Google Sheet:
/// không cần Google Cloud / service account). Google Sheet là bản gốc; bảng users trong SQLite là bản lưu đệm để đăng nhập khi mất mạng.
///  - Mọi máy có "mã đọc": lúc đăng nhập tải danh sách về (8 giây; lỗi mạng → dùng bản lưu đệm).
///  - Máy quản trị có thêm "mã quản trị": thêm / sửa / phân quyền tài khoản → ghi lên Sheet TRƯỚC, thành công mới ghi vào máy.
///  - Mật khẩu chỉ đi dưới dạng băm PBKDF2-SHA256 + muối (như trong máy), không bao giờ ở dạng chữ.
/// </summary>
public class AccountSyncService
{
    private const string KeyUrl = "acct_url", KeyToken = "acct_token", KeyAdmin = "acct_admin_key", KeyLast = "acct_last_pull",
        KeyPendToken = "acct_pending_token", KeyPendAdmin = "acct_pending_admin";
    public const string CodePrefix = "KLTK1-";

    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly HttpMessageHandler? _handler;

    public AccountSyncService(Database db, SettingsService settings, Session session, AuditService audit, HttpMessageHandler? handler = null)
    {
        _db = db; _settings = settings; _session = session; _audit = audit; _handler = handler;
    }

    public string Url => _settings.Get(KeyUrl);
    private string Token => Secret.Unprotect(_settings.Get(KeyToken));
    private string AdminKey => Secret.Unprotect(_settings.Get(KeyAdmin));
    /// <summary>Đã bật dùng chung tài khoản qua Google Sheet.</summary>
    public bool Enabled => Url.Length > 0 && Token.Length > 0;
    /// <summary>Máy này được sửa tài khoản (có mã quản trị).</summary>
    public bool CanWrite => Enabled && AdminKey.Length > 0;
    public string LastPull => _settings.Get(KeyLast);
    public bool HasPendingScript => Secret.Unprotect(_settings.Get(KeyPendToken)).Length > 0;

    // ---------------- mã Apps Script + mã kết nối ----------------

    /// <summary>Tạo 2 mã mới và trả về nội dung Apps Script (đã điền mã) để dán vào Google Sheet. Chỉ Quản trị.</summary>
    public string BuildScript()
    {
        _session.RequireAdmin();
        var tok = NewCode(); var adm = NewCode();
        _settings.Set(KeyPendToken, Secret.Protect(tok));
        _settings.Set(KeyPendAdmin, Secret.Protect(adm));
        return ScriptTemplate().Replace("__MA_DOC__", tok).Replace("__MA_QUAN_TRI__", adm);
    }

    internal static string ScriptTemplate()
    {
        using var s = typeof(AccountSyncService).Assembly.GetManifestResourceStream("KhoLanh.Core.Accounts.TaiKhoan.gs")
            ?? throw new InvalidOperationException("Thiếu mẫu Apps Script.");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static string NewCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>Mã kết nối để dán vào máy khác: máy nhân viên (chỉ đọc) hoặc máy quản trị (kèm mã quản trị).</summary>
    public string ConnectionCode(bool forAdmin)
    {
        _session.RequireAdmin();
        if (!Enabled) throw new BusinessException("Chưa kết nối Google Sheet.");
        if (forAdmin && !CanWrite) throw new BusinessException("Máy này không có mã quản trị.");
        var o = new JsonObject { ["u"] = Url, ["t"] = Token };
        if (forAdmin) o["a"] = AdminKey;
        return CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(o.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static (string url, string token, string admin) ParseCode(string code)
    {
        var t = new string(code.Trim().Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        if (!t.StartsWith(CodePrefix)) throw new BusinessException("Mã kết nối không đúng (phải bắt đầu bằng " + CodePrefix + ").");
        try
        {
            var b = t[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
            b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
            var o = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b)))!.AsObject();
            return (o["u"]!.GetValue<string>(), o["t"]!.GetValue<string>(), o["a"]?.GetValue<string>() ?? "");
        }
        catch { throw new BusinessException("Mã kết nối bị thiếu hoặc sai — chép lại đủ cả dòng mã."); }
    }

    internal static string CheckUrl(string url)
    {
        var u = url.Trim();
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "script.google.com" || !uri.AbsolutePath.EndsWith("/exec"))
            throw new BusinessException("Đường dẫn Web App phải có dạng https://script.google.com/macros/s/…/exec (chép ở bước Triển khai).");
        return u;
    }

    // ---------------- kết nối / ngắt ----------------

    public record ConnectResult(bool Uploaded, int Users, bool Admin);

    /// <summary>
    /// Kết nối máy này: <paramref name="input"/> là đường dẫn Web App (máy quản trị vừa tạo mã Apps Script)
    /// hoặc mã kết nối KLTK1-… chép từ máy đã kết nối. Sheet còn trống + máy có mã quản trị → đưa tài khoản máy này lên;
    /// ngược lại tải danh sách từ Sheet về (thay danh sách trong máy).
    /// Máy chưa có tài khoản nào (lần đầu) được kết nối bằng mã mà không cần đăng nhập.
    /// </summary>
    public async Task<ConnectResult> ConnectAsync(string input, CancellationToken ct = default)
    {
        if (HasAnyUser()) _session.RequireAdmin();
        string url, token, admin;
        if (input.Trim().StartsWith(CodePrefix)) (url, token, admin) = ParseCode(input);
        else
        {
            token = Secret.Unprotect(_settings.Get(KeyPendToken));
            admin = Secret.Unprotect(_settings.Get(KeyPendAdmin));
            if (token.Length == 0) throw new BusinessException("Dán mã kết nối KLTK1-… (chép từ máy đã kết nối), hoặc bấm \"Tạo mã Apps Script\" trước nếu đây là lần thiết lập đầu tiên.");
            url = input;
        }
        url = CheckUrl(url);
        var list = await CallAsync(url, token, "", "list", null, ct);
        var users = list["users"]?.AsArray() ?? new JsonArray();
        bool uploaded = false;
        int count;
        if (users.Count == 0)
        {
            if (admin.Length == 0) throw new BusinessException("Google Sheet chưa có tài khoản nào. Hãy kết nối máy quản trị trước.");
            var local = LocalUsers();
            if (local.Count == 0) throw new BusinessException("Máy này chưa có tài khoản nào để đưa lên Google Sheet.");
            // Kiểm tra mã quản trị bằng chính lệnh ghi.
            await CallAsync(url, token, admin, "replaceAll", new JsonObject { ["users"] = new JsonArray(local.Select(ToRow).ToArray<JsonNode?>()) }, ct);
            uploaded = true; count = local.Count;
        }
        else
        {
            if (admin.Length > 0) await CallAsync(url, token, admin, "save", new JsonObject { ["user"] = users[0]!.DeepClone() }, ct); // thử mã quản trị
            count = Apply(users);
        }
        _settings.Set(KeyUrl, url);
        _settings.Set(KeyToken, Secret.Protect(token));
        _settings.Set(KeyAdmin, admin.Length > 0 ? Secret.Protect(admin) : "");
        _settings.Set(KeyPendToken, ""); _settings.Set(KeyPendAdmin, "");
        _settings.Set(KeyLast, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        _audit.Log(_session.Username, "Kết nối tài khoản dùng chung", $"{(uploaded ? "đưa lên" : "tải về")} {count} tài khoản{(admin.Length > 0 ? ", máy quản trị" : "")}");
        return new ConnectResult(uploaded, count, admin.Length > 0);
    }

    public void Disconnect()
    {
        _session.RequireAdmin();
        foreach (var k in new[] { KeyUrl, KeyToken, KeyAdmin, KeyPendToken, KeyPendAdmin }) _settings.Set(k, "");
        _audit.Log(_session.Username, "Ngắt tài khoản dùng chung", "mỗi máy dùng tài khoản riêng");
    }

    // ---------------- tải về ----------------

    /// <summary>Tải danh sách tài khoản từ Sheet về máy. Trả số tài khoản; lỗi mạng ném BusinessException (nơi gọi quyết định dùng bản lưu đệm).</summary>
    public async Task<int> PullAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!Enabled) return 0;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
        JsonObject res;
        try { res = await CallAsync(Url, Token, "", "list", null, cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new BusinessException("Google Sheet trả lời quá lâu."); }
        var users = res["users"]?.AsArray() ?? new JsonArray();
        if (users.Count == 0) throw new BusinessException("Google Sheet không có tài khoản nào — giữ danh sách trong máy.");
        var n = Apply(users);
        _settings.Set(KeyLast, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return n;
    }

    /// <summary>Ghi danh sách từ Sheet vào bảng users: thêm / cập nhật theo tên đăng nhập; tài khoản không còn trên Sheet bị khóa.</summary>
    internal int Apply(JsonArray users)
    {
        var parsed = new List<User>();
        foreach (var n in users)
        {
            if (n is not JsonObject o) continue;
            var u = FromRow(o);
            if (u is not null && !parsed.Any(p => string.Equals(p.Username, u.Username, StringComparison.OrdinalIgnoreCase))) parsed.Add(u);
        }
        if (parsed.Count == 0) throw new BusinessException("Danh sách tài khoản trên Google Sheet không đọc được.");
        if (!parsed.Any(u => u.IsActive && u.Role == Roles.Admin))
            throw new BusinessException("Danh sách trên Google Sheet không còn Quản trị nào đang dùng — giữ danh sách trong máy.");
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        var names = new List<string>();
        foreach (var u in parsed)
        {
            names.Add(u.Username.ToLowerInvariant());
            var n = c.Execute("""
                UPDATE users SET full_name=@FullName, password_hash=@PasswordHash, salt=@Salt, role=@Role, is_active=@IsActive, permissions=@Permissions
                WHERE username=@Username COLLATE NOCASE
                """, u, tx);
            if (n == 0)
                c.Execute("INSERT INTO users(username, full_name, password_hash, salt, role, is_active, permissions) VALUES(@Username, @FullName, @PasswordHash, @Salt, @Role, @IsActive, @Permissions)", u, tx);
        }
        foreach (var local in c.Query<string>("SELECT username FROM users", transaction: tx).ToList())
            if (!names.Contains(local.ToLowerInvariant()))
                c.Execute("UPDATE users SET is_active=0 WHERE username=@local", new { local }, tx);
        tx.Commit();
        if (_session.CurrentUser is { } cur)
        {
            using var c2 = _db.Open();
            var fresh = c2.QuerySingleOrDefault<User>("SELECT * FROM users WHERE id=@Id", cur);
            if (fresh is not null) { cur.FullName = fresh.FullName; cur.Role = fresh.Role; cur.Permissions = fresh.Permissions; cur.IsActive = fresh.IsActive; }
        }
        return parsed.Count;
    }

    // ---------------- ghi lên ----------------

    /// <summary>
    /// Gọi trước mỗi thay đổi tài khoản: chưa bật dùng chung → không làm gì; bật rồi → ghi lên Sheet (máy phải có mã quản trị).
    /// Lỗi → ném BusinessException và máy KHÔNG đổi gì, để hai bên luôn khớp.
    /// </summary>
    public void BeforeSave(User u)
    {
        if (!Enabled) return;
        if (!CanWrite)
            throw new BusinessException("Tài khoản đang dùng chung qua Google Sheet và máy này chỉ được đọc. Hãy sửa tài khoản ở máy quản trị (hoặc dán \"mã cho máy quản trị\" vào máy này).");
        var url = Url; var token = Token; var admin = AdminKey;
        var row = ToRow(u);
        try { Task.Run(() => CallAsync(url, token, admin, "save", new JsonObject { ["user"] = row })).GetAwaiter().GetResult(); }
        catch (BusinessException ex) { throw new BusinessException("Chưa lưu được lên Google Sheet nên chưa đổi gì: " + ex.Message); }
    }

    // ---------------- dòng Sheet <-> User ----------------

    internal static JsonObject ToRow(User u) => new()
    {
        ["username"] = u.Username,
        ["ho_ten"] = u.FullName,
        ["vai_tro"] = u.Role,
        ["dang_dung"] = u.IsActive ? "1" : "0",
        ["quyen"] = u.Permissions,
        // Băm / muối gửi dạng "h:" + hex để Google Sheet không hiểu nhầm thành số hay công thức.
        ["mat_khau_bam"] = "h:" + Convert.ToHexString(Convert.FromBase64String(u.PasswordHash)).ToLowerInvariant(),
        ["muoi"] = "h:" + Convert.ToHexString(Convert.FromBase64String(u.Salt)).ToLowerInvariant(),
        ["cap_nhat_luc"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        ["cap_nhat_boi"] = "",
    };

    internal static User? FromRow(JsonObject o)
    {
        string S(string k) { var v = o[k]?.ToString() ?? ""; return v.StartsWith('\'') ? v[1..] : v; }
        static string? Bytes(string v)
        {
            try
            {
                if (v.StartsWith("h:")) return Convert.ToBase64String(Convert.FromHexString(v[2..]));
                return v.Length > 0 ? Convert.ToBase64String(Convert.FromBase64String(v)) : null;
            }
            catch { return null; }
        }
        var name = S("username").Trim();
        var role = S("vai_tro").Trim();
        var hash = Bytes(S("mat_khau_bam").Trim()); var salt = Bytes(S("muoi").Trim());
        if (name.Length == 0 || !Roles.All.Contains(role) || hash is null || salt is null) return null;
        var perms = S("quyen").Trim();
        try { perms = perms.Length == 0 ? "" : Perm.Serialize(Perm.Parse(perms)); } catch { perms = ""; }
        var act = S("dang_dung").Trim().ToLowerInvariant();
        return new User
        {
            Username = name, FullName = S("ho_ten").Trim(), Role = role, PasswordHash = hash, Salt = salt,
            IsActive = act is "1" or "true" or "có", Permissions = role == Roles.Admin ? "" : perms,
        };
    }

    private List<User> LocalUsers()
    {
        using var c = _db.Open();
        return c.Query<User>("SELECT * FROM users ORDER BY username").ToList();
    }

    private bool HasAnyUser()
    {
        using var c = _db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM users") > 0;
    }

    // ---------------- gọi Web App ----------------

    private async Task<JsonObject> CallAsync(string url, string token, string admin, string action, JsonObject? payload, CancellationToken ct = default)
    {
        var body = payload ?? new JsonObject();
        body["token"] = token; body["action"] = action;
        if (admin.Length > 0) body["adminKey"] = admin;
        using var h = _handler is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) : new HttpClient(_handler, false);
        h.Timeout = TimeSpan.FromSeconds(60);
        HttpResponseMessage res;
        try
        {
            // text/plain: Apps Script đọc thân qua e.postData.contents; Google trả 302 sang googleusercontent.com, HttpClient tự đi theo bằng GET.
            res = await h.PostAsync(url, new StringContent(body.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("text/plain")), ct);
        }
        catch (HttpRequestException) { throw new BusinessException("Không kết nối được Google Sheet — kiểm tra Internet."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new BusinessException("Google Sheet trả lời quá lâu."); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            JsonObject? o = null;
            try { o = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }
            if (o is null)
                throw new BusinessException($"Web App trả về trang lạ (HTTP {(int)res.StatusCode}). Kiểm tra: đường dẫn kết thúc bằng /exec và \"Ai có quyền truy cập\" là \"Bất kỳ ai\".");
            if (o["ok"]?.GetValue<bool>() != true) throw new BusinessException(o["error"]?.ToString() ?? "Web App báo lỗi không rõ.");
            return o;
        }
    }
}

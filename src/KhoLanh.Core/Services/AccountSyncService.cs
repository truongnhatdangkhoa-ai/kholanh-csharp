using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Lan;
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
        KeyPendToken = "acct_pending_token", KeyPendAdmin = "acct_pending_admin", KeyPlan = "lan_plan", KeyBlobs = "acct_keyblobs";
    public const string KeyRowName = "#khoa-quan-tri", KeyRowRole = "khoa-qt";
    public const string CodePrefix = "KLTK1-";
    /// <summary>1.105: MÃ KẾT NỐI MÁY (1 mã cho máy mới) = mã tài khoản dùng chung + mã PC_TOKEN điện thoại (+ mã GitHub). Cùng dạng KLTK1, máy cũ vẫn dán được KLTK1.</summary>
    public const string CodePrefix2 = "KLKN1-";
    /// <summary>Chuỗi là mã kết nối (KLKN1- hoặc KLTK1- cũ).</summary>
    public static bool IsCode(string? s) { var t = (s ?? "").Trim(); return t.StartsWith(CodePrefix) || t.StartsWith(CodePrefix2); }

    private readonly Database _db;
    private readonly SettingsService _settings;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly HttpMessageHandler? _handler;
    /// <summary>Để mã kết nối mang theo mã GitHub (máy mới tự cập nhật được ngay).</summary>
    public UpdateService? Updates { get; set; }
    /// <summary>1.105: để mã kết nối mang theo mã PC_TOKEN điện thoại (máy mới khỏi nhập riêng).</summary>
    public PhoneWebApp? Phone { get; set; }
    public const string BundleFile = "ket-noi.txt";

    public AccountSyncService(Database db, SettingsService settings, Session session, AuditService audit, HttpMessageHandler? handler = null)
    {
        _db = db; _settings = settings; _session = session; _audit = audit; _handler = handler;
    }

    /// <summary>Mã kết nối đóng sẵn trong file ket-noi.txt cạnh KhoLanh.exe (bộ cài cho máy mới), hoặc null.</summary>
    public static string? BundledCode(string? dir = null)
    {
        try
        {
            dir ??= Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
            var f = Path.Combine(dir, BundleFile);
            if (!File.Exists(f)) return null;
            return File.ReadAllLines(f).Select(x => x.Trim()).FirstOrDefault(IsCode);
        }
        catch { return null; }
    }

    /// <summary>Nội dung file ket-noi.txt của bộ cài cho máy mới (mã máy nhân viên, chỉ đọc).</summary>
    public string BundleText() =>
        "# Kho Lạnh — mã kết nối tài khoản dùng chung (Google Sheet) + mã cập nhật phần mềm.\r\n" +
        "# Để file này CẠNH KhoLanh.exe: lần mở đầu tiên máy tự nhận tài khoản, chỉ cần đăng nhập.\r\n" +
        "# Chỉ dùng cho máy của công ty, không gửi cho người ngoài.\r\n" +
        ConnectionCode(forAdmin: false) + "\r\n";

    public string Url => _settings.Get(KeyUrl);
    private string Token => Secret.Unprotect(_settings.Get(KeyToken));
    /// <summary>Mã quản trị đóng sẵn trong máy (máy đã tạo Apps Script hoặc dán mã quản trị).</summary>
    private string StoredAdminKey => Secret.Unprotect(_settings.Get(KeyAdmin));
    /// <summary>Mã quản trị mở khóa bằng mật khẩu Quản trị lúc đăng nhập — chỉ giữ trong bộ nhớ, đăng xuất là mất.</summary>
    private string _sessionKey = "";
    private string AdminKey => StoredAdminKey.Length > 0 ? StoredAdminKey : _sessionKey;
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

    /// <summary>Nội dung Apps Script BẢN MỚI nhưng GIỮ 2 mã đang dùng (1.70: thêm cột email_google) — dán đè vào Apps Script rồi
    /// Triển khai › Quản lý triển khai › Phiên bản mới; đường dẫn và các máy giữ nguyên. Chỉ máy quản trị đã mở khóa ghi.</summary>
    public string UpgradeScript()
    {
        _session.RequireAdmin();
        if (!CanWrite) throw new BusinessException("Máy này chưa mở khóa ghi tài khoản dùng chung (cần mã quản trị) nên không tạo được mã cập nhật.");
        return ScriptTemplate().Replace("__MA_DOC__", Token).Replace("__MA_QUAN_TRI__", AdminKey);
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
        if (Updates?.ShareToken() is { Length: > 0 } g) o["g"] = g;
        if (Phone is { Token.Length: > 0 } ph)
        {
            o["p"] = ph.Token;
            if (ph.CustomUrl) o["pu"] = ph.Url;
        }
        return CodePrefix2 + Convert.ToBase64String(Encoding.UTF8.GetBytes(o.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static (string url, string token, string admin) ParseCode(string code) { var r = ParseFull(code); return (r.url, r.token, r.admin); }

    internal static (string url, string token, string admin, string github) ParseFull(string code)
    {
        var c = ParseAny(code);
        return (c.Url, c.Token, c.Admin, c.Github);
    }

    /// <summary>Nội dung mã kết nối: tài khoản (u, t, a) + GitHub (g) + điện thoại (p = PC_TOKEN, pu = đường dẫn Web App riêng nếu có).</summary>
    public sealed record CodeInfo(string Url, string Token, string Admin, string Github, string PhoneToken, string PhoneUrl);

    public static CodeInfo ParseAny(string code)
    {
        var t = new string(code.Trim().Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        var pre = t.StartsWith(CodePrefix2) ? CodePrefix2 : t.StartsWith(CodePrefix) ? CodePrefix : null;
        if (pre is null) throw new BusinessException("Mã kết nối không đúng (phải bắt đầu bằng " + CodePrefix2 + ").");
        try
        {
            var b = t[pre.Length..].Replace('-', '+').Replace('_', '/');
            b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
            var o = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b)))!.AsObject();
            return new CodeInfo(o["u"]!.GetValue<string>(), o["t"]!.GetValue<string>(), o["a"]?.GetValue<string>() ?? "", o["g"]?.GetValue<string>() ?? "",
                o["p"]?.GetValue<string>() ?? "", o["pu"]?.GetValue<string>() ?? "");
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

    // ---------------- 1.104: dùng chung Sheet điện thoại (1 Google Sheet + 1 Apps Script) ----------------
    // Trước 1.104 tài khoản dùng chung cần 1 Google Sheet + 1 Apps Script RIÊNG (TaiKhoan.gs) bên cạnh Sheet điện thoại → dễ dán nhầm mã vào nhau.
    // Code.gs của app điện thoại (kholanh-csharp-mobile ≥ 25/11/2026) có sẵn phần tài khoản: tab TaiKhoan, lệnh list / save / replaceAll, 2 mã ở
    // Thuộc tính tập lệnh (TK_MA_DOC / TK_MA_QUAN_TRI) do PC đặt bằng lệnh pc.tkCaiMa (PC_TOKEN). Các máy khác tự theo nhờ dòng "#chuyen-sheet"
    // ghi ở Sheet cũ (cùng mã đọc / mã quản trị nên mã kết nối KLTK1 cũ vẫn đúng, chỉ đổi đường dẫn).

    public const string MoveRowName = "#chuyen-sheet", MoveRowRole = "chuyen";

    /// <summary>Đường dẫn Web App điện thoại máy này đang dùng (chưa đặt riêng → đường dẫn cài sẵn).</summary>
    public string PhoneUrl => _settings.Get(PhoneWebApp.KeyUrl) is { Length: > 0 } u ? u : PhoneWebApp.DefaultUrl;
    /// <summary>Tài khoản đang dùng chung qua chính Sheet điện thoại (1 Sheet cho cả hai).</summary>
    public bool OnPhoneSheet => Enabled && string.Equals(Url.Trim(), PhoneUrl.Trim(), StringComparison.Ordinal);

    internal const string OldPhoneScriptMsg =
        "Apps Script của Google Sheet điện thoại chưa phải bản mới (chưa có phần tài khoản dùng chung). Mở Sheet điện thoại › Tiện ích mở rộng › Apps Script, "
        + "xóa hết, dán Code.gs mới, Lưu, rồi Triển khai › Quản lý triển khai › ✏ › Phiên bản: Phiên bản mới › Triển khai (KHÔNG tạo triển khai mới để giữ đường dẫn).";

    /// <summary>Đường dẫn Sheet mới ghi ở dòng "#chuyen-sheet" (khác <paramref name="current"/>), hoặc null.</summary>
    internal static string? MovedTo(JsonArray rows, string current)
    {
        foreach (var n in rows)
        {
            if (n is not JsonObject o) continue;
            var name = (o["username"]?.ToString() ?? "").TrimStart('\'').Trim();
            if (!string.Equals(name, MoveRowName, StringComparison.OrdinalIgnoreCase)) continue;
            var url = (o["quyen"]?.ToString() ?? "").TrimStart('\'').Trim();
            try { url = CheckUrl(url); } catch { return null; }
            return string.Equals(url, current.Trim(), StringComparison.Ordinal) ? null : url;
        }
        return null;
    }

    private static JsonObject MoveRow(string url) => new()
    {
        ["username"] = MoveRowName,
        ["ho_ten"] = "Tài khoản đã chuyển sang Google Sheet điện thoại (phần mềm tự ghi, đừng sửa tay)",
        ["vai_tro"] = MoveRowRole,
        ["dang_dung"] = "0",
        ["quyen"] = url,
        ["mat_khau_bam"] = "", ["muoi"] = "",
        ["cap_nhat_luc"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        ["cap_nhat_boi"] = "",
    };

    public record MoveResult(int Users, bool Uploaded, bool OldMarked);

    /// <summary>
    /// Chuyển tài khoản dùng chung sang chính Google Sheet điện thoại (Quản trị). Máy đang dùng Sheet tài khoản riêng → chép nguyên danh sách
    /// (kể cả dòng khóa ghi / phân vai LAN), giữ 2 mã, ghi dòng "#chuyen-sheet" ở Sheet cũ để máy khác tự theo. Máy chưa dùng chung → tạo 2 mã mới,
    /// đưa tài khoản trong máy lên. Sheet điện thoại đã có tài khoản (máy này chuyển dở lần trước) → tải về, không ghi đè.
    /// </summary>
    public async Task<MoveResult> MoveToPhoneSheetAsync(PhoneWebApp phone, CancellationToken ct = default)
    {
        _session.RequireAdmin();
        if (phone.Token.Length == 0)
            throw new BusinessException("Máy này chưa kết nối đồng bộ điện thoại (chưa có mã PC_TOKEN). Nhập mã ở Hệ thống › Kết nối trước, rồi bấm lại.");
        var url = CheckUrl(phone.Url);
        if (OnPhoneSheet) throw new BusinessException("Tài khoản đã dùng chung Google Sheet điện thoại rồi.");
        string token, admin;
        JsonArray rows;
        var oldUrl = Enabled ? Url : "";
        if (Enabled)
        {
            if (!CanWrite) throw new BusinessException("Máy này chưa mở khóa ghi tài khoản dùng chung. Đăng nhập bằng tài khoản Quản trị (khóa tự mở) hoặc dán mã máy quản trị, rồi bấm lại.");
            token = Token; admin = AdminKey;
            var old = await CallAsync(oldUrl, token, "", "list", null, ct);
            rows = new JsonArray((old["users"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
                .Where(o => !string.Equals((o["username"]?.ToString() ?? "").TrimStart('\'').Trim(), MoveRowName, StringComparison.OrdinalIgnoreCase))
                .Select(o => (JsonNode?)o.DeepClone()).ToArray());
        }
        else
        {
            token = NewCode(); admin = NewCode();
            rows = new JsonArray(LocalUsers().Select(u => (JsonNode?)ToRow(u)).ToArray());
        }
        if (!rows.OfType<JsonObject>().Any(o => FromRow(o) is { IsActive: true, Role: Roles.Admin }))
            throw new BusinessException("Danh sách tài khoản chưa có Quản trị nào đang dùng nên chưa chuyển được.");
        try { await phone.CallAsync("pc.tkCaiMa", new JsonObject { ["maDoc"] = token, ["maQuanTri"] = admin }, 60, ct); }
        catch (BusinessException e) when (e.Message.Contains("Không có chức năng")) { throw new BusinessException(OldPhoneScriptMsg); }
        catch (BusinessException e) when (e.Message.Contains("từ một máy khác")) { throw new BusinessException(e.Message.TrimEnd() + " " + ResetHint); }
        var cur = (await CallAsync(url, token, "", "list", null, ct))["users"]?.AsArray() ?? new JsonArray();
        bool uploaded = false;
        if (!cur.OfType<JsonObject>().Any(o => FromRow(o) is not null))
        {
            await CallAsync(url, token, admin, "replaceAll", new JsonObject { ["users"] = rows.DeepClone() }, ct);
            cur = (await CallAsync(url, token, "", "list", null, ct))["users"]?.AsArray() ?? new JsonArray();
            uploaded = true;
        }
        bool marked = false;
        if (oldUrl.Length > 0)
        {
            try { await CallAsync(oldUrl, token, admin, "save", new JsonObject { ["user"] = MoveRow(url) }, ct); marked = true; }
            catch (BusinessException) { /* Sheet cũ lỗi: máy khác cập nhật mã kết nối bằng tay */ }
        }
        _settings.Set(KeyUrl, url);
        _settings.Set(KeyToken, Secret.Protect(token));
        _settings.Set(KeyAdmin, Secret.Protect(admin));
        _settings.Set(KeyPendToken, ""); _settings.Set(KeyPendAdmin, "");
        CachePlan(cur);
        var n = Apply(cur);
        _settings.Set(KeyLast, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        ConnErrorLog.Clear(_settings, ConnErrorLog.Acct);
        _audit.Log(_session.Username, "Tài khoản dùng chung sang Sheet điện thoại", $"{n} tài khoản{(uploaded ? ", đã đưa lên" : ", Sheet đã có sẵn")}{(marked ? ", Sheet cũ đã ghi dòng chuyển" : "")}");
        return new MoveResult(n, uploaded, marked);
    }

    // ---------------- 1.109: đặt lại mã tài khoản dùng chung ----------------

    /// <summary>Câu gợi ý khi Sheet chung đã có mã của máy khác (pc.tkCaiMa từ chối).</summary>
    public const string ResetHint =
        "Không máy nào còn mã đó (máy cũ đã cài lại / mất mã) → máy Quản trị: Hệ thống › Kết nối › ⋯ Thêm › \"Đặt lại mã tài khoản dùng chung…\".";

    internal const string OldResetScriptMsg =
        "Apps Script của Google Sheet chung chưa có lệnh đặt lại mã (cần Code.gs mới ở repo kholanh-csharp-mobile › apps-script/Code.gs). "
        + "Mở Sheet › Tiện ích mở rộng › Apps Script, xóa hết, dán Code.gs mới, Lưu, rồi Triển khai › Quản lý triển khai › ✏ › Phiên bản: Phiên bản mới › Triển khai. "
        + "Hoặc làm tay: Apps Script › ⚙ Cài đặt dự án › Thuộc tính tập lệnh: xóa TK_MA_DOC và TK_MA_QUAN_TRI rồi bấm \"Bật tài khoản dùng chung\".";

    public record ResetResult(int Users, bool Uploaded);

    /// <summary>
    /// Quản trị đặt lại 2 mã tài khoản dùng chung trên Google Sheet chung (lệnh <c>pc.tkDatLaiMa</c>, cần PC_TOKEN) — dùng khi Sheet báo
    /// "đã bật từ một máy khác" mà không máy nào còn mã đó, hoặc mọi máy báo "Sai mã kết nối". Mã mới được máy này giữ; máy khác đăng nhập
    /// lại là tự nối (pcDangNhap). <paramref name="keepSheet"/>: true = giữ danh sách đang có trên Sheet (tải về máy này; Sheet trống thì đưa
    /// danh sách máy này lên), false = thay danh sách trên Sheet bằng danh sách máy này.
    /// </summary>
    public async Task<ResetResult> ResetCodesAsync(PhoneWebApp phone, bool keepSheet, CancellationToken ct = default)
    {
        _session.RequireAdmin();
        if (phone.Token.Length == 0)
            throw new BusinessException("Máy này chưa có mã điện thoại (PC_TOKEN) nên chưa đặt lại mã được. Nhập mã ở Hệ thống › Kết nối trước.");
        var url = CheckUrl(phone.Url);
        var local = new JsonArray(LocalUsers().Select(u => (JsonNode?)ToRow(u)).ToArray());
        bool LocalHasAdmin() => local.OfType<JsonObject>().Any(o => FromRow(o) is { IsActive: true, Role: Roles.Admin });
        if (!keepSheet && !LocalHasAdmin()) throw new BusinessException("Máy này chưa có Quản trị nào đang dùng nên không đưa danh sách lên được.");
        string token = NewCode(), admin = NewCode();
        try { await phone.CallAsync("pc.tkDatLaiMa", new JsonObject { ["maDoc"] = token, ["maQuanTri"] = admin }, 60, ct); }
        catch (BusinessException e) when (e.Message.Contains("Không có chức năng")) { throw new BusinessException(OldResetScriptMsg); }
        var cur = (await CallAsync(url, token, "", "list", null, ct))["users"]?.AsArray() ?? new JsonArray();
        var sheetHas = cur.OfType<JsonObject>().Any(o => FromRow(o) is not null);
        bool uploaded = false;
        if (keepSheet && sheetHas)
        {
            var me = _session.CurrentUser?.Username ?? "";
            if (!cur.OfType<JsonObject>().Any(o => FromRow(o) is { IsActive: true, Role: Roles.Admin } u && string.Equals(u.Username, me, StringComparison.OrdinalIgnoreCase)))
                throw new BusinessException($"Mã mới đã đặt, nhưng danh sách trên Google Sheet không có tài khoản Quản trị \"{me}\" đang dùng — chưa tải về để máy này không bị khóa. "
                    + "Chọn lại \"Đặt lại mã…\" với cách \"Thay danh sách trên Sheet bằng danh sách máy này\".");
        }
        else
        {
            if (!LocalHasAdmin()) throw new BusinessException("Google Sheet chưa có tài khoản và máy này cũng chưa có Quản trị nào để đưa lên.");
            await CallAsync(url, token, admin, "replaceAll", new JsonObject { ["users"] = local.DeepClone() }, ct);
            cur = (await CallAsync(url, token, "", "list", null, ct))["users"]?.AsArray() ?? new JsonArray();
            uploaded = true;
        }
        _settings.Set(KeyUrl, url);
        _settings.Set(KeyToken, Secret.Protect(token));
        _settings.Set(KeyAdmin, Secret.Protect(admin));
        _settings.Set(KeyPendToken, ""); _settings.Set(KeyPendAdmin, "");
        CachePlan(cur);
        var n = Apply(cur);
        _settings.Set(KeyLast, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        ConnErrorLog.Clear(_settings, ConnErrorLog.Acct);
        ConnErrorLog.Clear(_settings, ConnErrorLog.Phone);
        _audit.Log(_session.Username, "Đặt lại mã tài khoản dùng chung", $"{n} tài khoản{(uploaded ? ", đưa danh sách máy này lên" : ", giữ danh sách trên Sheet")}");
        return new ResetResult(n, uploaded);
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
        string url, token, admin, github = "", phoneTok = "", phoneUrl = "";
        if (IsCode(input)) { var c = ParseAny(input); (url, token, admin, github, phoneTok, phoneUrl) = (c.Url, c.Token, c.Admin, c.Github, c.PhoneToken, c.PhoneUrl); }
        else
        {
            token = Secret.Unprotect(_settings.Get(KeyPendToken));
            admin = Secret.Unprotect(_settings.Get(KeyPendAdmin));
            if (token.Length == 0) throw new BusinessException("Dán mã kết nối KLKN1-… (chép từ máy đã kết nối), hoặc bấm \"Tạo mã Apps Script\" trước nếu đây là lần thiết lập đầu tiên.");
            url = input;
        }
        url = CheckUrl(url);
        var list = await CallAsync(url, token, "", "list", null, ct);
        var users = list["users"]?.AsArray() ?? new JsonArray();
        if (MovedTo(users, url) is { } moved) { url = moved; users = (await CallAsync(url, token, "", "list", null, ct))["users"]?.AsArray() ?? new JsonArray(); }
        CachePlan(users);
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
        if (github.Length > 0) Updates?.ImportToken(github);
        if (phoneTok.Length > 0)
        {
            // 1.105: mã kết nối mang theo PC_TOKEN điện thoại → máy mới đồng bộ điện thoại được ngay, khỏi nhập riêng.
            if (Phone is not null) Phone.Save(phoneUrl, phoneTok);
            else { _settings.Set(PhoneWebApp.KeyUrl, phoneUrl); _settings.Set(PhoneWebApp.KeyToken, Secret.Protect(phoneTok)); }
        }
        _settings.Set(KeyLast, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        ConnErrorLog.Clear(_settings, ConnErrorLog.Acct);
        _audit.Log(_session.Username, "Kết nối tài khoản dùng chung", $"{(uploaded ? "đưa lên" : "tải về")} {count} tài khoản{(admin.Length > 0 ? ", máy quản trị" : "")}");
        return new ConnectResult(uploaded, count, admin.Length > 0);
    }

    // ---------------- 1.106: máy mới chỉ cần đăng nhập ----------------

    /// <summary>Câu báo khi Code.gs trên Google chưa có lệnh đăng nhập máy mới (pcDangNhap).</summary>
    /// <summary>1.107: Sheet chung chưa bật tài khoản dùng chung (máy chính chưa đưa tài khoản lên Sheet).</summary>
    public const string NotEnabledMsg =
        "Google Sheet chung chưa có danh sách tài khoản — máy đã tạo tài khoản (Quản trị) chưa đưa tài khoản lên Sheet. Ở máy đó: Hệ thống › Kết nối, "
        + "thẻ \"Google Sheet chung\" phải hiện \"Đã kết nối\" (chưa thì bấm \"Bật tài khoản dùng chung\" / \"Kết nối…\").";

    /// <summary>1.107: tài khoản có ở máy khác nhưng chưa lên Sheet chung.</summary>
    public static string NotOnSheetMsg(string user) =>
        $"Tài khoản \"{user}\" chưa có trên Google Sheet chung. Ở máy đã tạo tài khoản này (Quản trị): Hệ thống › Kết nối › thẻ \"Google Sheet chung\" "
        + "phải hiện \"Đã kết nối\", rồi bấm ⋯ Thêm › \"Đồng bộ tài khoản ngay\" (hoặc mở Hệ thống › Tài khoản, sửa tài khoản rồi Lưu) — xong đăng nhập lại ở máy này.";

    public const string OldJoinScriptMsg =
        "Google Sheet chung chưa cập nhật Code.gs mới (cần cho máy mới đăng nhập). Ở máy Quản trị: dán Code.gs mới vào Apps Script rồi Triển khai phiên bản mới; "
        + "trong lúc chờ, dán mã KLKN1-… (máy Quản trị: Hệ thống › Kết nối › ⋯ Thêm › Chép mã kết nối máy).";

    /// <summary>
    /// 1.106: MÁY MỚI chỉ cần tên đăng nhập + mật khẩu. Gửi tới Web App Sheet chung cài sẵn (lệnh <c>pcDangNhap</c> của Code.gs): Web App kiểm mật khẩu bằng
    /// chính mã băm PBKDF2 ở tab TaiKhoan, đúng thì trả mã đọc tài khoản + PC_TOKEN (+ mã GitHub) → máy tự kết nối như khi dán mã KLKN1. Sai nhiều lần
    /// Web App tạm khóa (chống dò mật khẩu). Chỉ dùng khi máy CHƯA có tài khoản nào (máy đang dùng riêng thì kết nối ở Hệ thống › Kết nối).
    /// </summary>
    public async Task<int> JoinByPasswordAsync(string user, string pass, CancellationToken ct = default, bool allowExisting = false)
    {
        if (!allowExisting && HasAnyUser()) throw new BusinessException("Máy này đã có tài khoản riêng — kết nối ở Hệ thống › Kết nối.");
        if (user.Trim().Length == 0 || pass.Length == 0) throw new BusinessException("Nhập tên đăng nhập và mật khẩu.");
        var url = PhoneUrl;
        var r = await PostRawAsync(url, new JsonObject { ["action"] = "pcDangNhap", ["ten"] = user.Trim(), ["matKhau"] = pass }, ct);
        if (r["ok"]?.GetValue<bool>() != true)
        {
            var err = r["error"]?.ToString() ?? "";
            if (LooksLikePhoneScript(r) || err.Contains("Không có chức năng")) throw new JoinException(JoinException.OldScript, OldJoinScriptMsg);
            if (r["chuaBat"]?.GetValue<bool>() == true) throw new JoinException(JoinException.NotEnabled, NotEnabledMsg);
            if (r["chuaCo"]?.GetValue<bool>() == true) throw new JoinException(JoinException.NoAccount, NotOnSheetMsg(user.Trim()));
            if (r["khoa"]?.GetValue<bool>() == true) throw new JoinException(JoinException.Locked, "Google Sheet chung: " + err);
            if (err.StartsWith("Sai tên đăng nhập")) throw new JoinException(JoinException.WrongPassword, "Google Sheet chung báo: " + err);
            throw new JoinException(JoinException.Other, err.Length > 0 ? "Google Sheet chung báo lỗi: " + err : "Web App báo lỗi không rõ.");
        }
        string token = r["maDoc"]?.ToString() ?? "", pcTok = r["pcToken"]?.ToString() ?? "", gh = r["gh"]?.ToString() ?? "";
        if (token.Length == 0) throw new BusinessException("Web App không trả mã tài khoản — kiểm tra Code.gs.");
        var users = (await CallAsync(url, token, "", "list", null, ct))["users"]?.AsArray() ?? new JsonArray();
        if (MovedTo(users, url) is { } moved) { url = moved; users = (await CallAsync(url, token, "", "list", null, ct))["users"]?.AsArray() ?? new JsonArray(); }
        if (users.Count == 0) throw new BusinessException("Google Sheet chung chưa có tài khoản nào.");
        CachePlan(users);
        var count = Apply(users);
        _settings.Set(KeyUrl, url);
        _settings.Set(KeyToken, Secret.Protect(token));
        _settings.Set(KeyAdmin, "");
        _settings.Set(KeyPendToken, ""); _settings.Set(KeyPendAdmin, "");
        if (gh.Length > 0) Updates?.ImportToken(gh);
        if (pcTok.Length > 0)
        {
            var pu = url == PhoneWebApp.DefaultUrl ? "" : PhoneUrl;
            if (Phone is not null) Phone.Save(pu, pcTok);
            else { _settings.Set(PhoneWebApp.KeyUrl, pu.Length > 0 ? pu : PhoneWebApp.DefaultUrl); _settings.Set(PhoneWebApp.KeyToken, Secret.Protect(pcTok)); }
        }
        _settings.Set(KeyLast, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        ConnErrorLog.Clear(_settings, ConnErrorLog.Acct);
        _audit.Log(user.Trim(), "Kết nối tài khoản dùng chung", $"máy mới đăng nhập bằng mật khẩu, tải về {count} tài khoản");
        return count;
    }

    /// <summary>
    /// 1.106: máy Quản trị đẩy mã cập nhật phần mềm (GitHub) lên Sheet chung (lệnh <c>pc.ghMayMoi</c>, cần PC_TOKEN) để máy mới đăng nhập xong tự cập nhật được.
    /// Chỉ gửi khi mã đổi (so dấu trong cài đặt <c>gh_maymoi_sig</c>).
    /// </summary>
    public async Task PushGithubForNewMachinesAsync(CancellationToken ct = default)
    {
        if (Phone is not { Enabled: true } || Updates is null) return;
        var tok = Updates.ShareToken();
        if (tok.Length == 0) return;
        var sig = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Phone.Url + "|" + tok)))[..16];
        if (_settings.Get(KeyGhSig) == sig) return;
        await Phone.CallAsync("pc.ghMayMoi", new JsonObject { ["gh"] = tok }, 30, ct);
        _settings.Set(KeyGhSig, sig);
    }
    private const string KeyGhSig = "gh_maymoi_sig";
    /// <summary>Lượt đẩy mã GitHub ngầm gần nhất (để kiểm thử chờ).</summary>
    internal Task GhTask { get; private set; } = Task.CompletedTask;

    private async Task<JsonObject> PostRawAsync(string url, JsonObject body, CancellationToken ct)
    {
        using var own = _handler is null ? null : new HttpClient(_handler, false) { Timeout = TimeSpan.FromSeconds(60) };
        var h = own ?? Shared;
        HttpResponseMessage res;
        try { res = await h.PostAsync(url, new StringContent(body.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("text/plain")), ct); }
        catch (HttpRequestException) { throw new BusinessException("Không kết nối được Google Sheet chung — kiểm tra Internet."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new BusinessException("Google Sheet chung trả lời quá lâu — thử lại."); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            JsonObject? o = null;
            try { o = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }
            return o ?? throw new BusinessException($"Web App trả về trang lạ (HTTP {(int)res.StatusCode}).");
        }
    }

    public void Disconnect()
    {
        _session.RequireAdmin();
        foreach (var k in new[] { KeyUrl, KeyToken, KeyAdmin, KeyPendToken, KeyPendAdmin, KeyBlobs }) _settings.Set(k, "");
        ConnErrorLog.Clear(_settings, ConnErrorLog.Acct);
        _sessionKey = "";
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
        // Trợ lý: ghi lỗi tải gần nhất (settings acct_last_error), tải được thì xóa — xem ConnErrorLog / TroLyChecks.CheckConfig
        try { res = await CallAsync(Url, Token, "", "list", null, cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { NoteError("Google Sheet trả lời quá lâu."); throw new BusinessException("Google Sheet trả lời quá lâu."); }
        catch (Exception e) when (!ct.IsCancellationRequested) { NoteError(e.Message); throw; }
        var users = res["users"]?.AsArray() ?? new JsonArray();
        if (MovedTo(users, Url) is { } moved)
        {
            // 1.104: máy Quản trị đã chuyển tài khoản sang Sheet điện thoại (dòng "#chuyen-sheet" ở Sheet cũ) → máy này tự theo, cùng mã.
            try { res = await CallAsync(moved, Token, "", "list", null, cts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { NoteError("Google Sheet trả lời quá lâu."); throw new BusinessException("Google Sheet trả lời quá lâu."); }
            catch (Exception e) when (!ct.IsCancellationRequested) { NoteError(e.Message); throw; }
            users = res["users"]?.AsArray() ?? new JsonArray();
            if (users.Count > 0)
            {
                _settings.Set(KeyUrl, moved);
                _audit.Log(_session.Username, "Tài khoản dùng chung theo Sheet mới", "Sheet điện thoại (máy Quản trị đã chuyển)");
            }
        }
        if (users.Count == 0) { NoteError("Google Sheet không có tài khoản nào."); throw new BusinessException("Google Sheet không có tài khoản nào — giữ danh sách trong máy."); }
        ConnErrorLog.Clear(_settings, ConnErrorLog.Acct);
        CachePlan(users);
        var n = Apply(users);
        _settings.Set(KeyLast, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return n;
    }

    /// <summary>Lỗi tải danh sách tài khoản gần nhất (null = lần gần nhất tải được / chưa tải lỗi).</summary>
    public ConnErrorLog.Entry? LastError => ConnErrorLog.Read(_settings, ConnErrorLog.Acct);
    private void NoteError(string msg) =>
        ConnErrorLog.Note(_settings, ConnErrorLog.Acct, msg, msg == PhoneScriptMsg || msg == OldPhoneScriptMsg ? ConnErrorLog.KindPhoneScript : "");

    private readonly object _pfLock = new();
    private Task<int>? _prefetch;

    /// <summary>
    /// Tải danh sách ngầm (1 lượt dùng chung): cửa sổ đăng nhập gọi ngay lúc mở — song song với kiểm tra bản cập nhật và lúc người dùng gõ
    /// mật khẩu — nên khi bấm Đăng nhập thường đã có danh sách mới. Đang tải, hoặc vừa tải xong trong 2 phút, thì trả lại đúng lượt đó;
    /// lượt trước lỗi / đã cũ thì tải lượt mới.
    /// </summary>
    public Task<int> PrefetchAsync(TimeSpan? timeout = null)
    {
        lock (_pfLock)
        {
            if (_prefetch is { IsCompleted: false }) return _prefetch;
            if (_prefetch is { IsCompletedSuccessfully: true } && DateTime.UtcNow - _prefetchAt < TimeSpan.FromMinutes(2)) return _prefetch;
            if (!Enabled) return Task.FromResult(0);
            var t = timeout ?? TimeSpan.FromSeconds(20);
            _prefetchAt = DateTime.UtcNow;
            _prefetch = Task.Run(() => PullAsync(t));
            // 1.99 (báo cáo Trợ lý "Tác vụ nền lỗi — A Task's exception(s) were not observed"): cửa sổ đăng nhập gọi tải ngầm kiểu
            // "_ = PrefetchAsync()" — lượt tải lỗi (vd đường dẫn trỏ nhầm Web App điện thoại) mà không ai chờ thì thành lỗi chưa bắt.
            // Lỗi đã được báo ở chỗ chờ (LoginFlow.NoteOf / Trợ lý) → ở đây chỉ đánh dấu đã xem.
            _prefetch.ContinueWith(x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            return _prefetch;
        }
    }
    private DateTime _prefetchAt;

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
                UPDATE users SET full_name=@FullName, password_hash=@PasswordHash, salt=@Salt, role=@Role, is_active=@IsActive, permissions=@Permissions,
                    google_email=COALESCE(@GoogleEmail, google_email)
                WHERE username=@Username COLLATE NOCASE
                """, u, tx);
            if (n == 0)
                c.Execute("INSERT INTO users(username, full_name, password_hash, salt, role, is_active, permissions, google_email) VALUES(@Username, @FullName, @PasswordHash, @Salt, @Role, @IsActive, @Permissions, COALESCE(@GoogleEmail, ''))", u, tx);
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

    // ---------------- phân vai mạng LAN theo tài khoản (dòng "#mang-lan") ----------------

    /// <summary>Kế hoạch phân vai LAN lấy từ Sheet ở lần tải gần nhất (null = chưa có).</summary>
    public LanPlan? Plan => Enabled ? LanPlan.Parse(_settings.Get(KeyPlan)) : null;

    private void CachePlan(JsonArray users) { _settings.Set(KeyPlan, LanPlan.FromRows(users)?.ToJson() ?? ""); CacheBlobs(users); }

    /// <summary>
    /// Đọc lại kế hoạch mới nhất trên Sheet, cho <paramref name="change"/> sửa; có đổi thì ghi lại dòng "#mang-lan".
    /// Cần mã quản trị. Trả kế hoạch sau khi sửa.
    /// </summary>
    public async Task<LanPlan> UpdatePlanAsync(Func<LanPlan, bool> change, CancellationToken ct = default)
    {
        if (!Enabled) throw new BusinessException("Chưa bật tài khoản dùng chung (Google Sheet) — bật ở Cài đặt máy trước.");
        if (!CanWrite) throw new BusinessException("Máy này không có mã quản trị nên không ghi được phân vai LAN lên Google Sheet.");
        var res = await CallAsync(Url, Token, "", "list", null, ct);
        var users = res["users"]?.AsArray() ?? new JsonArray();
        var plan = LanPlan.FromRows(users) ?? new LanPlan();
        if (change(plan))
            await CallAsync(Url, Token, AdminKey, "save", new JsonObject { ["user"] = plan.ToRow() }, ct);
        _settings.Set(KeyPlan, plan.ToJson());
        return plan;
    }

    // ---------------- khóa ghi đi theo mật khẩu Quản trị (không cần "máy quản trị") ----------------
    // Mã quản trị được mã hóa AES-GCM bằng khóa suy ra từ MẬT KHẨU của từng tài khoản Quản trị (PBKDF2-SHA256, 100.000 vòng) rồi lưu ở dòng
    // riêng "#khoa-quan-tri" của tab TaiKhoan (cột quyen = JSON {tên đăng nhập: gói mã hóa}) — không phải dán lại Apps Script.
    // Quản trị đăng nhập ở BẤT KỲ máy nào đã nối Google Sheet → máy tự giải mã, giữ trong bộ nhớ tới khi đăng xuất. Nhân viên không có
    // mật khẩu Quản trị nên không mở được khóa dù đọc được gói mã hóa.

    internal static string Seal(string password, string key)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12);
        var k = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var plain = Encoding.UTF8.GetBytes(key); var cipher = new byte[plain.Length]; var tag = new byte[16];
        using (var aes = new AesGcm(k, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        return "v1:" + Convert.ToBase64String(salt.Concat(nonce).Concat(tag).Concat(cipher).ToArray());
    }

    /// <summary>Giải mã gói; sai mật khẩu / gói hỏng → chuỗi rỗng.</summary>
    internal static string Unseal(string password, string blob)
    {
        try
        {
            if (!blob.StartsWith("v1:")) return "";
            var b = Convert.FromBase64String(blob[3..]);
            if (b.Length < 45) return "";
            var k = Rfc2898DeriveBytes.Pbkdf2(password, b[..16], 100_000, HashAlgorithmName.SHA256, 32);
            var cipher = b[44..]; var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(k, 16)) aes.Decrypt(b[16..28], cipher, b[28..44], plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return ""; }
    }

    private static Dictionary<string, string> BlobsFromRows(JsonArray rows)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in rows)
        {
            if (n is not JsonObject o) continue;
            var name = o["username"]?.ToString() ?? "";
            if (!string.Equals(name.TrimStart('\''), KeyRowName, StringComparison.OrdinalIgnoreCase)) continue;
            var q = o["quyen"]?.ToString() ?? ""; if (q.StartsWith('\'')) q = q[1..];
            try { foreach (var p in JsonNode.Parse(q)!.AsObject()) if (p.Value?.ToString() is { Length: > 0 } v) d[p.Key] = v; } catch { }
        }
        return d;
    }

    private void CacheBlobs(JsonArray users) =>
        _settings.Set(KeyBlobs, JsonSerializer.Serialize(BlobsFromRows(users)));

    private Dictionary<string, string> CachedBlobs()
    {
        try { return new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(_settings.Get(KeyBlobs)) ?? new(), StringComparer.OrdinalIgnoreCase); }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static JsonObject BlobRow(Dictionary<string, string> d) => new()
    {
        ["username"] = KeyRowName,
        ["ho_ten"] = "Khóa ghi Google Sheet, mã hóa bằng mật khẩu Quản trị (phần mềm tự ghi, đừng sửa tay)",
        ["vai_tro"] = KeyRowRole,
        ["dang_dung"] = "0",
        ["quyen"] = JsonSerializer.Serialize(d),
        ["mat_khau_bam"] = "", ["muoi"] = "",
        ["cap_nhat_luc"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        ["cap_nhat_boi"] = "",
    };

    /// <summary>Gọi sau khi đăng nhập thành công (biết mật khẩu): Quản trị ở máy chưa có mã → tự mở khóa; máy đã có mã → bảo đảm gói của tài khoản này trên Sheet đúng.</summary>
    public void AfterLogin(User u, string password)
    {
        _sessionKey = "";
        if (!Enabled || u.Role != Roles.Admin) return;
        GhTask = Task.Run(async () => { try { await PushGithubForNewMachinesAsync(); } catch { /* lần đăng nhập sau thử lại */ } });
        try
        {
            CachedBlobs().TryGetValue(u.Username, out var mine);
            var stored = StoredAdminKey;
            if (stored.Length == 0)
            {
                if (!string.IsNullOrEmpty(mine)) _sessionKey = Unseal(password, mine);
                return;
            }
            if (string.IsNullOrEmpty(mine) || Unseal(password, mine) != stored)
            {
                // Ghi gói khóa lên Sheet chạy NGẦM (trước 1.59 chặn màn hình đăng nhập tới 15 giây).
                var name = u.Username;
                PublishTask = Task.Run(() => { try { Publish(name, password); } catch { /* lần đăng nhập sau thử lại */ } });
            }
        }
        catch { /* mất mạng / Sheet lỗi: đăng nhập vẫn bình thường, lần sau thử lại */ }
    }

    /// <summary>Lượt ghi gói khóa ngầm gần nhất (để kiểm thử chờ).</summary>
    internal Task PublishTask { get; private set; } = Task.CompletedTask;

    public void AfterLogout() => _sessionKey = "";

    /// <summary>
    /// Mở khóa ghi tài khoản ngay trên màn hình Tài khoản (máy chưa có mã quản trị, Quản trị đang đăng nhập):
    /// <paramref name="input"/> là mật khẩu Quản trị (tải gói khóa mới nhất trên Sheet rồi giải mã) hoặc "mã cho máy quản trị" KLTK1-…
    /// (lưu luôn vào máy). Không mở được → BusinessException kèm hướng dẫn.
    /// </summary>
    public async Task UnlockAsync(string input, CancellationToken ct = default)
    {
        _session.RequireAdmin();
        if (!Enabled) throw new BusinessException("Máy này chưa dùng chung tài khoản qua Google Sheet.");
        var text = input.Trim();
        if (IsCode(text))
        {
            var (url, token, admin, _) = ParseFull(text);
            if (admin.Length == 0) throw new BusinessException("Đây là mã cho máy NHÂN VIÊN (chỉ đọc). Cần \"mã cho máy quản trị\" — chép ở máy đã thiết lập Google Sheet, mục Hệ thống › Kết nối › Google Sheet chung › ⋯ Thêm › Chép mã cho máy quản trị.");
            if (!string.Equals(url, Url, StringComparison.Ordinal) || token != Token) throw new BusinessException("Mã này của một Google Sheet khác với Sheet máy này đang dùng.");
            var list = await CallAsync(url, token, "", "list", null, ct);
            var users = list["users"]?.AsArray() ?? new JsonArray();
            var probe = users.OfType<JsonObject>().FirstOrDefault(o => !(o["username"]?.ToString() ?? "").TrimStart('\'').StartsWith('#'));
            if (probe is not null) await CallAsync(url, token, admin, "save", new JsonObject { ["user"] = probe.DeepClone() }, ct); // thử mã quản trị
            _settings.Set(KeyAdmin, Secret.Protect(admin));
            _audit.Log(_session.Username, "Mở khóa ghi tài khoản", "dán mã cho máy quản trị");
            return;
        }
        var me = _session.CurrentUser!;
        using (var c = _db.Open())
        {
            var u = c.QuerySingleOrDefault<User>("SELECT * FROM users WHERE id=@id", new { id = me.Id });
            if (u is null || !AuthService.Verify(text, u.Salt, u.PasswordHash)) throw new BusinessException("Mật khẩu Quản trị không đúng.");
        }
        var res = await CallAsync(Url, Token, "", "list", null, ct);
        var rows = res["users"]?.AsArray() ?? new JsonArray();
        CacheBlobs(rows);
        var key = BlobsFromRows(rows).TryGetValue(me.Username, out var blob) ? Unseal(text, blob) : "";
        if (key.Length == 0)
            throw new BusinessException($"Trên Google Sheet chưa có khóa ghi cho tài khoản {me.Username}. Làm MỘT trong hai cách: (1) ở máy đã thiết lập Google Sheet (máy tạo mã Apps Script), đăng nhập {me.Username} một lần rồi quay lại đây bấm lại; hoặc (2) ở máy đó vào Hệ thống › Kết nối › ⋯ Thêm › \"Chép mã cho máy quản trị\" rồi dán mã vào ô này.");
        _sessionKey = key;
    }

    /// <summary>Mật khẩu Quản trị vừa đổi / tạo mới: ghi lại gói khóa bằng mật khẩu mới (cần đang giữ mã quản trị). Lỗi chỉ ghi nhật ký.</summary>
    public void Reseal(string username, string password)
    {
        if (!Enabled || AdminKey.Length == 0) return;
        try { Publish(username, password); }
        catch (Exception e) { _audit.Log(_session.Username, "Cập nhật khóa ghi Quản trị thất bại", username + ": " + e.Message); }
    }

    private void Publish(string username, string password)
    {
        var url = Url; var token = Token; var admin = AdminKey;
        if (admin.Length == 0) return;
        var blob = Seal(password, admin);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var all = Task.Run(async () =>
        {
            var res = await CallAsync(url, token, "", "list", null, cts.Token);
            var d = BlobsFromRows(res["users"]?.AsArray() ?? new JsonArray());
            d[username] = blob;
            await CallAsync(url, token, admin, "save", new JsonObject { ["user"] = BlobRow(d) }, cts.Token);
            return d;
        }, cts.Token).GetAwaiter().GetResult();
        _settings.Set(KeyBlobs, JsonSerializer.Serialize(all));
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
            throw new BusinessException("Tài khoản đang dùng chung qua Google Sheet nhưng máy này chưa mở được khóa ghi. Ở màn hình Tài khoản, bấm Lưu và nhập lại mật khẩu Quản trị (hoặc dán \"mã cho máy quản trị\") để mở khóa.");
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
        ["email_google"] = u.GoogleEmail ?? "",
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
            // Sheet có cột email_google (TaiKhoan.gs 1.70+) → lấy theo Sheet; Sheet cũ chưa có cột → null = giữ email trong máy.
            GoogleEmail = o.ContainsKey("email_google") ? GoogleSignIn.NormEmail(S("email_google")) : null,
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

    private static readonly HttpClient Shared = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(10),
    }) { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// 1.98: đường dẫn "Tài khoản dùng chung" trỏ nhầm Web App của app ĐIỆN THOẠI (Code.gs của kholanh-csharp-mobile trả {ok:false, code:"AUTH",
    /// "… Liên hệ người quản lý app PC."}) — hoặc Sheet tài khoản bị dán đè mã điện thoại. Trước đây báo nguyên câu đó → người dùng không hiểu vì sao không tạo được tài khoản.
    /// </summary>
    internal static bool LooksLikePhoneScript(JsonObject o) =>
        o["code"]?.ToString() is "AUTH" or "NO_USER" || (o["error"]?.ToString() ?? "").Contains("người quản lý app PC", StringComparison.OrdinalIgnoreCase);
    internal const string PhoneScriptMsg =
        "Đường dẫn tài khoản dùng chung (Hệ thống › Kết nối) đang trỏ tới Web App của APP ĐIỆN THOẠI, không phải Web App tài khoản "
        + "(hoặc Google Sheet tài khoản đã bị dán đè mã của app điện thoại). Cách sửa: mở Google Sheet tài khoản › Tiện ích mở rộng › Apps Script, "
        + "dán lại mã từ nút \"Cập nhật mã Apps Script\" rồi Triển khai › Quản lý triển khai › Phiên bản mới; hoặc dán đúng đường dẫn …/exec của Sheet tài khoản "
        + "rồi bấm Kết nối. Gọn nhất (1.104): bấm \"Dùng chung Sheet điện thoại\" ở khung đó — 1 Google Sheet cho cả tài khoản lẫn điện thoại. "
        + "Muốn tạo tài khoản ngay chỉ trên máy này: bấm \"Ngắt kết nối\" ở khung đó.";

    private async Task<JsonObject> CallAsync(string url, string token, string admin, string action, JsonObject? payload, CancellationToken ct = default)
    {
        var body = payload ?? new JsonObject();
        body["token"] = token; body["action"] = action;
        if (admin.Length > 0) body["adminKey"] = admin;
        // Dùng chung 1 HttpClient (giữ sẵn kết nối TLS tới Google giữa các lần gọi — lần tải ngầm lúc mở cửa sổ làm "nóng" cho lần sau).
        using var own = _handler is null ? null : new HttpClient(_handler, false) { Timeout = TimeSpan.FromSeconds(60) };
        var h = own ?? Shared;
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
            if (o["ok"]?.GetValue<bool>() != true)
            {
                if (LooksLikePhoneScript(o))
                {
                    // 1.104: đường dẫn chính là Sheet điện thoại (đã gộp) mà vẫn trả AUTH → Code.gs trên Google còn bản cũ, chưa có phần tài khoản.
                    var msg = string.Equals(url.Trim(), PhoneUrl.Trim(), StringComparison.Ordinal) ? OldPhoneScriptMsg : PhoneScriptMsg;
                    NoteError(msg); throw new BusinessException(msg);
                }
                throw new BusinessException(o["error"]?.ToString() ?? "Web App báo lỗi không rõ.");
            }
            ConnErrorLog.Clear(_settings, ConnErrorLog.Acct);   // Web App tài khoản trả lời đúng → hết lỗi cấu hình
            return o;
        }
    }
}

/// <summary>1.107: lý do máy không đăng nhập được qua Google Sheet chung (để màn đăng nhập báo đúng, không chỉ "sai mật khẩu").</summary>
public sealed class JoinException(string kind, string message) : BusinessException(message)
{
    public const string OldScript = "old", NotEnabled = "off", NoAccount = "missing", WrongPassword = "wrong", Locked = "locked", Other = "other";
    public string Kind { get; } = kind;
}

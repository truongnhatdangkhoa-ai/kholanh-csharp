using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace KhoLanh.Core.Services;

/// <summary>
/// Đăng nhập bằng tài khoản Google đang đăng nhập trên trình duyệt (1.59) — OAuth 2.0 cho ứng dụng máy tính, cùng cách Drive cá nhân
/// của app cũ (core/drive-ca-nhan.js): mở trình duyệt mặc định, Google gọi lại http://127.0.0.1:&lt;cổng ngẫu nhiên&gt;/, PKCE (S256),
/// kiểm tra state. Chỉ xin quyền <c>openid email profile</c> — chỉ biết email + tên, không đụng Drive / Gmail.
/// Mã OAuth: dùng lại mã Desktop của dự án Google Cloud "kholanhcloud" (app cũ đã tạo cho Drive cá nhân); Client secret do quản trị
/// nhập ở máy (Hệ thống › Tài khoản › Đăng nhập Google), cất bằng <see cref="Secret"/>.
/// </summary>
public sealed class GoogleSignIn
{
    public const string KeyClientId = "google_client_id", KeyClientSecret = "google_client_secret";
    /// <summary>Mã Desktop "kholanhanan" của dự án kholanhcloud (Client ID không phải mật khẩu).</summary>
    public const string DefaultClientId = "688518280070-s9dd35p3ouff2nqpscs5stt76gmis8m3.apps.googleusercontent.com";
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth", TokenEndpoint = "https://oauth2.googleapis.com/token";

    private readonly SettingsService _settings;
    private readonly HttpMessageHandler? _handler;
    public GoogleSignIn(SettingsService settings, HttpMessageHandler? handler = null) { _settings = settings; _handler = handler; }

    public string ClientId => _settings.Get(KeyClientId) is { Length: > 0 } v ? v : DefaultClientId;
    public string ClientSecret => Secret.Unprotect(_settings.Get(KeyClientSecret));
    public bool Ready => ClientId.Length > 0 && ClientSecret.Length > 0;

    public void SaveConfig(string clientId, string clientSecret)
    {
        clientId = clientId.Trim();
        if (clientId.Length > 0 && !clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Client ID phải có dạng ….apps.googleusercontent.com");
        _settings.Set(KeyClientId, clientId == DefaultClientId ? "" : clientId);
        if (clientSecret.Trim().Length > 0) _settings.Set(KeyClientSecret, Secret.Protect(clientSecret.Trim())); // để trống = giữ secret cũ
    }
    public void ClearSecret() => _settings.Set(KeyClientSecret, "");

    public sealed record Result(string Email, string Name);

    /// <summary>Mở trình duyệt cho người dùng chọn tài khoản Google, chờ Google gọi lại, đổi mã lấy id_token → email đã xác minh.</summary>
    public async Task<Result> SignInAsync(Action<string> openBrowser, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!Ready) throw new BusinessException("Máy này chưa cài Đăng nhập Google: Quản trị vào Hệ thống › Tài khoản › Đăng nhập Google, dán Client secret.");
        var verifier = B64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = B64Url(RandomNumberGenerator.GetBytes(18));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            var url = AuthEndpoint + "?" + Form(new()
            {
                ["client_id"] = ClientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = "openid email profile",
                ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state, ["prompt"] = "select_account",
            });
            openBrowser(url);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(3));
            string? code = null;
            while (code is null)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(cts.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new BusinessException("Hết thời gian chờ đăng nhập Google (3 phút). Bấm lại nút Đăng nhập bằng Google."); }
                using (client)
                {
                    var q = await ReadQueryAsync(client, cts.Token);
                    if (q is null) continue; // yêu cầu khác (favicon…) → chờ tiếp
                    if (q.TryGetValue("error", out var err))
                    {
                        await ReplyAsync(client, false);
                        throw new BusinessException(err == "access_denied" ? "Đã hủy đăng nhập Google." : "Google báo lỗi: " + err);
                    }
                    if (!q.TryGetValue("code", out var c)) { await ReplyAsync(client, false); continue; }
                    if (!q.TryGetValue("state", out var st) || st != state) { await ReplyAsync(client, false); throw new BusinessException("Phiên đăng nhập Google không khớp (state) — thử lại."); }
                    await ReplyAsync(client, true);
                    code = c;
                }
            }
            return await ExchangeAsync(code, verifier, redirect, ct);
        }
        finally { listener.Stop(); }
    }

    private async Task<Result> ExchangeAsync(string code, string verifier, string redirect, CancellationToken ct)
    {
        using var h = _handler is null ? new HttpClient() : new HttpClient(_handler, false);
        h.Timeout = TimeSpan.FromSeconds(30);
        HttpResponseMessage res;
        try
        {
            res = await h.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code, ["client_id"] = ClientId, ["client_secret"] = ClientSecret, ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
            }), ct);
        }
        catch (HttpRequestException e) { throw new BusinessException("Không kết nối được Google: " + e.Message); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            JsonObject? o = null; try { o = JsonNode.Parse(text) as JsonObject; } catch { }
            if (!res.IsSuccessStatusCode || o?["id_token"] is null)
            {
                var e = o?["error"]?.ToString() ?? $"HTTP {(int)res.StatusCode}";
                throw new BusinessException(e == "invalid_client" ? "Client ID / Client secret không đúng (Hệ thống › Tài khoản › Đăng nhập Google)." : "Google từ chối đổi mã đăng nhập: " + e);
            }
            return ParseIdToken(o["id_token"]!.ToString(), ClientId);
        }
    }

    /// <summary>Đọc phần thân id_token (nhận thẳng từ Google qua HTTPS ở bước đổi mã nên không cần kiểm chữ ký — OpenID Connect 3.1.3.7),
    /// vẫn kiểm nơi phát hành, đối tượng, hạn dùng, email đã xác minh.</summary>
    public static Result ParseIdToken(string idToken, string clientId, DateTimeOffset? now = null)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2) throw new BusinessException("Mã đăng nhập Google không hợp lệ.");
        JsonObject p;
        try { p = (JsonObject)JsonNode.Parse(Encoding.UTF8.GetString(FromB64Url(parts[1])))!; }
        catch { throw new BusinessException("Mã đăng nhập Google không đọc được."); }
        var iss = p["iss"]?.ToString();
        if (iss is not ("https://accounts.google.com" or "accounts.google.com")) throw new BusinessException("Mã đăng nhập không phải của Google.");
        if (p["aud"]?.ToString() != clientId) throw new BusinessException("Mã đăng nhập Google không dành cho Kho Lạnh.");
        if (p["exp"] is { } exp && long.TryParse(exp.ToString(), out var e) && DateTimeOffset.FromUnixTimeSeconds(e) < (now ?? DateTimeOffset.UtcNow).AddMinutes(-5))
            throw new BusinessException("Mã đăng nhập Google đã hết hạn — thử lại.");
        var email = (p["email"]?.ToString() ?? "").Trim().ToLowerInvariant();
        var verified = p["email_verified"]?.ToString()?.ToLowerInvariant() == "true";
        if (email.Length == 0 || !verified) throw new BusinessException("Tài khoản Google này chưa xác minh email.");
        return new Result(email, p["name"]?.ToString() ?? "");
    }

    public static string NormEmail(string? s) => (s ?? "").Trim().ToLowerInvariant();
    public static bool LooksLikeEmail(string s) => System.Text.RegularExpressions.Regex.IsMatch(s, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    private static async Task<Dictionary<string, string>?> ReadQueryAsync(TcpClient client, CancellationToken ct)
    {
        var s = client.GetStream();
        var buf = new byte[8192]; var n = 0;
        while (n < buf.Length)
        {
            var r = await s.ReadAsync(buf.AsMemory(n), ct);
            if (r == 0) break;
            n += r;
            if (Encoding.ASCII.GetString(buf, 0, n).Contains("\r\n\r\n")) break;
        }
        var line = Encoding.ASCII.GetString(buf, 0, n).Split("\r\n")[0];   // GET /?code=…&state=… HTTP/1.1
        var parts = line.Split(' ');
        if (parts.Length < 2 || parts[0] != "GET") return null;
        var path = parts[1];
        var qi = path.IndexOf('?');
        if (qi < 0) { await ReplyAsync(client, null); return null; }   // /favicon.ico…
        var d = new Dictionary<string, string>();
        foreach (var kv in path[(qi + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var i = kv.IndexOf('=');
                var k = Uri.UnescapeDataString(i < 0 ? kv : kv[..i]);
                d[k] = i < 0 ? "" : Uri.UnescapeDataString(kv[(i + 1)..].Replace('+', ' '));
            }
        return d;
    }

    private static async Task ReplyAsync(TcpClient client, bool? ok)
    {
        var body = ok switch
        {
            true => "<h2>Đã đăng nhập Google.</h2><p>Quay lại phần mềm Kho Lạnh — có thể đóng thẻ này.</p>",
            false => "<h2>Chưa đăng nhập được.</h2><p>Quay lại phần mềm Kho Lạnh để xem lý do.</p>",
            null => "",
        };
        var html = "<!doctype html><meta charset=\"utf-8\"><title>Kho Lạnh</title><body style=\"font-family:system-ui;padding:40px;color:#1A2433\">" + body + "</body>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {(ok is null ? "404 Not Found" : "200 OK")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        try { var s = client.GetStream(); await s.WriteAsync(head); await s.WriteAsync(bytes); await s.FlushAsync(); } catch { }
    }

    private static string Form(Dictionary<string, string> d) => string.Join("&", d.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));
    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromB64Url(string s) { s = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=')); }
}

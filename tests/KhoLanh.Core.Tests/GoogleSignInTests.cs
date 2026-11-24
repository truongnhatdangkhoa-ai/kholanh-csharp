using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Đăng nhập bằng Google (1.70): luồng trình duyệt → 127.0.0.1 → đổi mã lấy id_token, giả lập Google.</summary>
public class GoogleSignInTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-gg-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static string Jwt(string aud, string email, bool verified = true, string iss = "https://accounts.google.com", long? exp = null) =>
        B64("{\"alg\":\"RS256\"}") + "." + B64(new JsonObject { ["iss"] = iss, ["aud"] = aud, ["email"] = email, ["email_verified"] = verified, ["name"] = "Thắng",
            ["exp"] = exp ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }.ToJsonString()) + ".chuky";

    /// <summary>Máy chủ token giả của Google: kiểm tra đủ tham số rồi trả id_token.</summary>
    private sealed class FakeGoogle(string aud, string email) : HttpMessageHandler
    {
        public Dictionary<string, string> Form = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Assert.Equal(GoogleSignIn.TokenEndpoint, req.RequestUri!.ToString());
            Form = (await req.Content!.ReadAsStringAsync(ct)).Split('&').Select(x => x.Split('=')).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1].Replace('+', ' ')));
            var ok = Form["code"] == "MA-123" && Form["client_secret"] == "bi-mat" && Form["code_verifier"].Length >= 43 && Form["grant_type"] == "authorization_code";
            var body = ok ? new JsonObject { ["id_token"] = Jwt(aud, email) } : new JsonObject { ["error"] = "invalid_grant" };
            return new HttpResponseMessage(ok ? HttpStatusCode.OK : HttpStatusCode.BadRequest) { Content = new StringContent(body.ToJsonString()) };
        }
    }

    /// <summary>"Trình duyệt" giả: đọc redirect_uri + state trong đường dẫn Google, gọi lại máy như Google sẽ làm.</summary>
    private static Action<string> Browser(Func<string, string> query) => url =>
    {
        var q = new Uri(url).Query.TrimStart('?').Split('&').Select(x => x.Split('=')).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
        Assert.Equal("openid email profile", q["scope"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        var redirect = new Uri(q["redirect_uri"]);
        Assert.Equal("127.0.0.1", redirect.Host);
        _ = Task.Run(async () =>
        {
            using var c = new TcpClient();
            await c.ConnectAsync(IPAddress.Loopback, redirect.Port);
            var s = c.GetStream();
            await s.WriteAsync(Encoding.ASCII.GetBytes("GET /favicon.ico HTTP/1.1\r\nHost: x\r\n\r\n"));
            await new StreamReader(s).ReadToEndAsync();
            using var c2 = new TcpClient();
            await c2.ConnectAsync(IPAddress.Loopback, redirect.Port);
            var s2 = c2.GetStream();
            await s2.WriteAsync(Encoding.ASCII.GetBytes($"GET /?{query(q["state"])} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"));
            await new StreamReader(s2).ReadToEndAsync();
        });
    };

    private (AppServices S, GoogleSignIn G, FakeGoogle F) Setup()
    {
        var s = new AppServices(Path.Combine(_dir, "kho.db"));
        var f = new FakeGoogle(GoogleSignIn.DefaultClientId, "Nv1@Gmail.com");
        var g = new GoogleSignIn(s.Settings, f);
        g.SaveConfig(GoogleSignIn.DefaultClientId, "bi-mat");
        return (s, g, f);
    }

    [Fact]
    public async Task Dang_nhap_qua_trinh_duyet_lay_dung_email()
    {
        var (_, g, f) = Setup();
        Assert.True(g.Ready);
        var r = await g.SignInAsync(Browser(st => $"state={st}&code=MA-123&scope=email"), TimeSpan.FromSeconds(20));
        Assert.Equal("nv1@gmail.com", r.Email);
        Assert.Equal("Thắng", r.Name);
        Assert.StartsWith("http://127.0.0.1:", f.Form["redirect_uri"]);
    }

    [Fact]
    public async Task Sai_state_hoac_nguoi_dung_huy_thi_bao_loi()
    {
        var (_, g, _) = Setup();
        var e1 = await Assert.ThrowsAsync<BusinessException>(() => g.SignInAsync(Browser(_ => "state=khac&code=MA-123"), TimeSpan.FromSeconds(20)));
        Assert.Contains("state", e1.Message);
        var e2 = await Assert.ThrowsAsync<BusinessException>(() => g.SignInAsync(Browser(st => $"state={st}&error=access_denied"), TimeSpan.FromSeconds(20)));
        Assert.Contains("hủy", e2.Message);
    }

    [Fact]
    public async Task Chua_co_secret_thi_khong_mo_trinh_duyet()
    {
        var s = new AppServices(Path.Combine(_dir, "kho2.db"));
        var g = new GoogleSignIn(s.Settings);
        Assert.False(g.Ready);
        var opened = false;
        await Assert.ThrowsAsync<BusinessException>(() => g.SignInAsync(_ => opened = true));
        Assert.False(opened);
    }

    [Fact]
    public void Kiem_tra_id_token()
    {
        var id = GoogleSignIn.DefaultClientId;
        Assert.Equal("a@b.vn", GoogleSignIn.ParseIdToken(Jwt(id, "A@B.vn"), id).Email);
        Assert.Throws<BusinessException>(() => GoogleSignIn.ParseIdToken(Jwt("khac.apps.googleusercontent.com", "a@b.vn"), id));
        Assert.Throws<BusinessException>(() => GoogleSignIn.ParseIdToken(Jwt(id, "a@b.vn", verified: false), id));
        Assert.Throws<BusinessException>(() => GoogleSignIn.ParseIdToken(Jwt(id, "a@b.vn", iss: "https://evil.example"), id));
        Assert.Throws<BusinessException>(() => GoogleSignIn.ParseIdToken(Jwt(id, "a@b.vn", exp: DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds()), id));
        Assert.Throws<BusinessException>(() => GoogleSignIn.ParseIdToken("khong-phai-jwt", id));
    }

    [Fact]
    public void Gan_email_roi_dang_nhap_google()
    {
        var (s, _, _) = Setup();
        s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        s.Auth.Login("admin", "matkhau1");
        var nv = s.Auth.CreateUser("nv1", "Nhân viên", "matkhau2", Roles.Staff);
        var nv2 = s.Auth.CreateUser("nv2", "Nhân viên 2", "matkhau3", Roles.Staff);
        s.Auth.SetGoogleEmail(nv.Id, "Nv1@Gmail.com");
        Assert.Throws<BusinessException>(() => s.Auth.SetGoogleEmail(nv2.Id, "nv1@gmail.com"));   // 1 email chỉ 1 tài khoản
        Assert.Throws<BusinessException>(() => s.Auth.SetGoogleEmail(nv2.Id, "khong-phai-email"));
        s.Auth.Logout();
        Assert.Throws<BusinessException>(() => s.Auth.LoginGoogle("la@gmail.com"));
        Assert.Equal("nv1", s.Auth.LoginGoogle("NV1@gmail.com").Username);
        Assert.Equal("nv1", s.Session.CurrentUser!.Username);
        s.Auth.Logout();
        s.Auth.Login("admin", "matkhau1");
        s.Auth.UpdateUser(nv.Id, "Nhân viên", Roles.Staff, false);                                 // khóa → không vào được
        s.Auth.Logout();
        Assert.Throws<BusinessException>(() => s.Auth.LoginGoogle("nv1@gmail.com"));
    }
}

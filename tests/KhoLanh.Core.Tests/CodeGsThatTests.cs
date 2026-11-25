using System.Diagnostics;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>
/// 1.110 (người dùng: "vấn đề này sửa rất nhiều lần nhưng vẫn chưa xong"): các bản trước chỉ thử với Web App GIẢ viết tay trong test,
/// còn Code.gs thật trên Google thì chưa từng chạy cùng app → test xanh mà máy thật lỗi. Test này chạy CHÍNH Code.gs
/// (tests/apps-script/Code.gs = bản chép từ repo kholanh-csharp-mobile) bằng node (tests/apps-script/gia-lap-web-app.js) rồi cho app PC
/// đi đủ luồng: Quản trị bật dùng chung + tạo tài khoản → máy mới chỉ đăng nhập → đổi mật khẩu → máy thứ hai bật lại (mã khác) → máy mã cũ.
/// </summary>
public class CodeGsThatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-gas-" + Guid.NewGuid().ToString("N"));
    private readonly Process _node;
    private readonly int _port;

    public CodeGsThatTests()
    {
        var root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "tests", "apps-script"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("Không thấy thư mục tests/apps-script.");
        var dir = Path.Combine(root, "tests", "apps-script");
        var psi = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(Path.Combine(dir, "gia-lap-web-app.js")); psi.ArgumentList.Add(Path.Combine(dir, "Code.gs")); psi.ArgumentList.Add("0");
        _node = Process.Start(psi) ?? throw new InvalidOperationException("Không chạy được node (cần cài Node.js để chạy test này).");
        var line = _node.StandardOutput.ReadLine() ?? throw new InvalidOperationException("node không khởi động: " + _node.StandardError.ReadToEnd());
        _port = int.Parse(line.Split(' ')[^1]);
    }

    public void Dispose()
    {
        try { _node.Kill(true); } catch { }
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>Mọi yêu cầu tới script.google.com → Web App giả lập chạy Code.gs thật.</summary>
    private sealed class ToNode(int port) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { r.RequestUri = new Uri($"http://127.0.0.1:{port}/"); return base.SendAsync(r, ct); }
    }

    private (AppServices s, PhoneWebApp p) May(string n, string pcToken)
    {
        var h = new ToNode(_port);
        var s = new AppServices(Path.Combine(_dir, n, "kho.db"));
        var acc = new AccountSyncService(s.Db, s.Settings, s.Session, s.Audit, h);
        s.Auth.Sync = acc; acc.Updates = s.Updates; s.Accounts = acc;
        var p = new PhoneWebApp(s.Settings, h); acc.Phone = p;
        if (pcToken.Length > 0) p.Save(PhoneWebApp.DefaultUrl, pcToken);   // máy Quản trị: đã nhập PC_TOKEN
        return (s, p);
    }

    [Fact]
    public async Task Quan_tri_tao_tai_khoan_may_khach_chi_can_dang_nhap()
    {
        // 1. Máy Quản trị: đăng nhập admin, bật tài khoản dùng chung, tạo tài khoản khác (tự đẩy lên Google Sheet).
        var (a, ap) = May("A", "pc-token-that");
        a.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1"); a.Auth.Login("admin", "matkhau1");
        var r = await a.Accounts.MoveToPhoneSheetAsync(ap);
        Assert.True(r.Uploaded);
        a.Auth.CreateUser("khoa", "Khoa", "khoa1234", Roles.Staff);
        a.Auth.CreateUser("nv2", "NV 2", "nv2pass1", Roles.Manager);
        await a.Accounts.PublishTask;

        // 2. Máy khách mới tinh (chỉ có đường dẫn cài sẵn trong app, không mã gì): chỉ cần đăng nhập.
        var (b, bp) = May("B", "");
        Assert.Equal(3, await b.Accounts.JoinByPasswordAsync("khoa", "khoa1234"));
        Assert.Equal(Roles.Staff, (await LoginFlow.LoginAsync(b.Auth, b.Accounts, "khoa", "khoa1234")).User.Role);
        Assert.Equal("pc-token-that", bp.Token);   // nhận luôn mã điện thoại
        b.Auth.Logout();

        // 3. Máy khách đang có tài khoản riêng: đăng nhập tài khoản dùng chung → tự nối.
        var (c, _) = May("C", "");
        c.Auth.CreateFirstAdmin("chu", "Chủ máy C", "rieng123");
        Assert.Equal("nv2", (await LoginFlow.LoginAsync(c.Auth, c.Accounts, "nv2", "nv2pass1")).User.Username);
        Assert.True(c.Accounts.Enabled);

        // 4. Quản trị đổi mật khẩu → máy khách đăng nhập NGAY bằng mật khẩu mới; mật khẩu cũ bị từ chối.
        a.Auth.ResetPassword(a.Auth.ListUsers().Single(u => u.Username == "khoa").Id, "moi12345");
        Assert.Equal("khoa", (await LoginFlow.LoginAsync(b.Auth, b.Accounts, "khoa", "moi12345")).User.Username);
        b.Auth.Logout();
        var e = await Assert.ThrowsAsync<BusinessException>(() => LoginFlow.LoginAsync(b.Auth, b.Accounts, "khoa", "khoa1234"));
        Assert.StartsWith("Sai tên đăng nhập", e.Message);

        // 5. Tài khoản chưa có trên Sheet → báo rõ.
        e = await Assert.ThrowsAnyAsync<BusinessException>(() => LoginFlow.LoginAsync(b.Auth, b.Accounts, "khongco", "abc12345"));
        Assert.Contains("chưa có trên Google Sheet chung", e.Message);

        // 6. Máy Quản trị khác bấm Bật tài khoản dùng chung khi Sheet đã có mã của máy A (tình huống ảnh người dùng 25/11) → không bị chặn.
        var (d, dp) = May("D", "pc-token-that");
        d.Auth.CreateFirstAdmin("admin", "Quản trị", "khac9999"); d.Auth.Login("admin", "khac9999");
        var rd = await d.Accounts.MoveToPhoneSheetAsync(dp);
        Assert.False(rd.Uploaded);                          // Sheet có "admin" → giữ danh sách Sheet
        Assert.Contains(d.Auth.ListUsers(), u => u.Username == "khoa" && u.IsActive);

        // 7. Máy A, B giữ mã cũ (Sai mã kết nối) → đăng nhập lại là tự nối, không phải dán mã.
        a.Auth.Logout();
        var la = await LoginFlow.LoginAsync(a.Auth, a.Accounts, "nv2", "nv2pass1");
        Assert.Equal("nv2", la.User.Username);
        a.Auth.Logout();

        // 8. Quyền sửa đi theo TÀI KHOẢN Quản trị, không theo máy: máy mới tinh E, đăng nhập thẳng bằng admin → tạo / đổi tài khoản được;
        //    máy B (đang dùng tài khoản nhân viên) đăng nhập admin → cũng sửa được; nhân viên thì không.
        var (m, mp) = May("E", "");
        var le = await LoginFlow.LoginAsync(m.Auth, m.Accounts, "admin", "matkhau1");
        Assert.Equal(Roles.Admin, le.User.Role);
        await m.Accounts.PublishTask;
        Assert.True(m.Accounts.CanWrite);
        m.Auth.CreateUser("tu_may_e", "Tạo ở máy E", "emay1234", Roles.Staff);
        b.Auth.Logout();
        var lb = await LoginFlow.LoginAsync(b.Auth, b.Accounts, "admin", "matkhau1");
        Assert.True(b.Accounts.CanWrite);
        b.Auth.CreateUser("tu_may_b", "Tạo ở máy B", "bmay1234", Roles.Staff);
        b.Auth.Logout();
        await LoginFlow.LoginAsync(b.Auth, b.Accounts, "khoa", "moi12345");
        Assert.False(b.Accounts.CanWrite);
        Assert.ThrowsAny<Exception>(() => b.Auth.CreateUser("khong_duoc", "x", "x1234567", Roles.Staff));
        // 9. Quyền theo TÀI KHOẢN: Quản lý được tích "Tạo / sửa tài khoản" đăng nhập máy mới G → tạo tài khoản lên Sheet được; không tạo được Quản trị.
        m.Auth.CreateUser("ql1", "Quản lý 1", "ql123456", Roles.Manager, Perm.Serialize(new Dictionary<string, string> { ["taikhoan"] = Perm.Edit }));
        var (g, _) = May("G", "");
        await LoginFlow.LoginAsync(g.Auth, g.Accounts, "ql1", "ql123456");
        Assert.True(g.Session.CanManageAccounts && g.Accounts.CanWrite);
        g.Auth.CreateUser("tu_ql1", "Do ql1 tạo", "tql12345", Roles.Staff);
        Assert.Throws<BusinessException>(() => g.Auth.CreateUser("admin2", "x", "x1234567", Roles.Admin));
        var admin = g.Auth.ListUsers().Single(u => u.Username == "admin");
        Assert.Throws<BusinessException>(() => g.Auth.ResetPassword(admin.Id, "chiem1234"));
        // nv2 (Quản lý, không được tích) thì không.
        g.Auth.Logout();
        await LoginFlow.LoginAsync(g.Auth, g.Accounts, "nv2", "nv2pass1");
        Assert.False(g.Session.CanManageAccounts);
        Assert.Throws<BusinessException>(() => g.Auth.CreateUser("khong", "x", "x1234567", Roles.Staff));

        // 10. 1.112: Client OAuth Google (Gmail sao lưu / Lưu trữ cá nhân / Đăng nhập Google) — máy Quản trị có secret, máy khác tự lấy về qua Sheet chung.
        new GoogleSignIn(a.Settings).SaveConfig(GoogleSignIn.DefaultClientId, "GOCSPX-bimat-dung-chung");
        await LoginFlow.LoginAsync(a.Auth, a.Accounts, "admin", "matkhau1");
        await a.Accounts.GClientTask;
        var (h, _) = May("H", "");
        await LoginFlow.LoginAsync(h.Auth, h.Accounts, "khoa", "moi12345");
        await h.Accounts.GClientTask;
        Assert.Equal("GOCSPX-bimat-dung-chung", h.DriveCaNhan.Client.Secret);
        Assert.True(new GoogleSignIn(h.Settings).Ready);

        // Máy khác thấy cả các tài khoản vừa tạo.
        var (f, _) = May("F", "");
        Assert.Equal("tu_may_e", (await LoginFlow.LoginAsync(f.Auth, f.Accounts, "tu_may_e", "emay1234")).User.Username);
        f.Auth.Logout(); f.Auth.Login("admin", "matkhau1");
        Assert.Contains(f.Auth.ListUsers(), u => u.Username == "tu_may_b");
        Assert.Contains(f.Auth.ListUsers(), u => u.Username == "tu_ql1");
    }
}

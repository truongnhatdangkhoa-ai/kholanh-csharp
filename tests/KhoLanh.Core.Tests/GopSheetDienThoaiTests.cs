using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>
/// 1.104: tài khoản dùng chung gộp vào Google Sheet + Apps Script của app điện thoại (Code.gs kholanh-csharp-mobile có phần tài khoản,
/// 2 mã ở Thuộc tính tập lệnh đặt bằng pc.tkCaiMa). Hai Web App giả lập: Sheet tài khoản cũ (TaiKhoan.gs) và Sheet điện thoại.
/// </summary>
public class GopSheetDienThoaiTests : IDisposable
{
    private const string OldUrl = "https://script.google.com/macros/s/OLD/exec";
    private const string PhoneUrl = "https://script.google.com/macros/s/PHONE/exec";
    private const string PcToken = "pc-token-123";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-gop-" + Guid.NewGuid().ToString("N"));
    private readonly AccountSyncTests.FakeWebApp _old = new();
    private readonly PhoneFake _phone = new();
    private readonly Router _router;

    public GopSheetDienThoaiTests() { _router = new Router(this); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class Router(GopSheetDienThoaiTests t) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
            new HttpMessageInvoker(req.RequestUri!.AbsolutePath.Contains("PHONE") ? t._phone : t._old, false).SendAsync(req, ct);
    }

    /// <summary>Code.gs điện thoại: pc.* cần PC_TOKEN; list / save / replaceAll = phần tài khoản (giống TaiKhoan.gs, mã ở Thuộc tính tập lệnh).</summary>
    private sealed class PhoneFake : HttpMessageHandler
    {
        public readonly AccountSyncTests.FakeWebApp Acct = new();
        public bool OldScript;   // Code.gs bản cũ: chưa có phần tài khoản
        public string Gh = "";
        public readonly Dictionary<string, int> Sai = new();
        /// <summary>Giống dnPost_ của Code.gs 1.106: PBKDF2 ở tab TaiKhoan, 5 lần sai / tài khoản → khóa.</summary>
        private static byte[] B(string v) => v.StartsWith("h:") ? Convert.FromHexString(v[2..]) : Convert.FromBase64String(v);
        private JsonObject DangNhap(string ten, string mk)
        {
            if (Acct.Token.Length == 0) return new() { ["ok"] = false, ["chuaBat"] = true, ["error"] = "Google Sheet chung chưa bật tài khoản dùng chung." };
            var k = ten.ToLowerInvariant();
            if (Sai.GetValueOrDefault(k) >= 5) return new() { ["ok"] = false, ["khoa"] = true, ["error"] = "Tài khoản này nhập sai mật khẩu 5 lần — tạm khóa đăng nhập máy mới 15 phút." };
            var u = Acct.Rows.FirstOrDefault(x => string.Equals(x["username"], ten, StringComparison.OrdinalIgnoreCase) && !ten.StartsWith('#'));
            var ok = u is not null && u.GetValueOrDefault("dang_dung") == "1" && u.GetValueOrDefault("muoi", "").Length > 0
                && System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(mk, B(u["muoi"]), 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32)
                   .SequenceEqual(B(u["mat_khau_bam"]));
            if (!ok)
            {
                Sai[k] = Sai.GetValueOrDefault(k) + 1;
                if (u is null) return new() { ["ok"] = false, ["chuaCo"] = true, ["error"] = $"Tài khoản \"{ten}\" chưa có trên Google Sheet chung." };
                return new() { ["ok"] = false, ["error"] = "Sai tên đăng nhập hoặc mật khẩu (hoặc tài khoản đã bị khóa / tắt trên Sheet)." };
            }
            return new() { ["ok"] = true, ["ten"] = u!["username"], ["maDoc"] = Acct.Token, ["pcToken"] = PcToken, ["gh"] = Gh };
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var text = await req.Content!.ReadAsStringAsync(ct);
            var r = JsonNode.Parse(text)!.AsObject();
            var action = r["action"]?.ToString() ?? "";
            JsonObject res;
            if (action is "list" or "save" or "replaceAll")
            {
                if (OldScript) res = new() { ["ok"] = false, ["code"] = "AUTH", ["error"] = "Tên người dùng hoặc mã truy cập không đúng, hoặc tài khoản đã bị khóa. Liên hệ người quản lý app PC." };
                else if (Acct.Token.Length == 0) res = new() { ["ok"] = false, ["error"] = "Google Sheet này chưa bật tài khoản dùng chung." };
                else return await new HttpMessageInvoker(Acct, false).SendAsync(new HttpRequestMessage(HttpMethod.Post, req.RequestUri) { Content = new StringContent(text) }, ct);
            }
            else if (action == "pcDangNhap" && !OldScript) res = DangNhap(r["ten"]?.ToString() ?? "", r["matKhau"]?.ToString() ?? "");
            else if (r["token"]?.ToString() != PcToken) res = new() { ["ok"] = false, ["code"] = "AUTH", ["error"] = "Mã truy cập không đúng." };
            else if (action == "pc.ghMayMoi") { Gh = r["gh"]?.ToString() ?? ""; res = new() { ["ok"] = true }; }
            else if (action == "pc.tkCaiMa" && !OldScript)
            {
                string doc = r["maDoc"]!.ToString(), qt = r["maQuanTri"]!.ToString();
                if (Acct.Token.Length > 0 && (Acct.Token != doc || Acct.Admin != qt)) res = new() { ["ok"] = false, ["error"] = "Google Sheet này đã bật tài khoản dùng chung từ một máy khác." };
                else { var had = Acct.Token.Length > 0; Acct.Token = doc; Acct.Admin = qt; res = new() { ["ok"] = true, ["daCo"] = had }; }
            }
            else res = new() { ["ok"] = false, ["error"] = $"Không có chức năng \"{action}\"." };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(res.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    private (AppServices s, PhoneWebApp phone) Machine(string name, bool phoneLinked = true)
    {
        var s = new AppServices(Path.Combine(_dir, name, "kho.db"));
        var acc = new AccountSyncService(s.Db, s.Settings, s.Session, s.Audit, _router);
        s.Auth.Sync = acc; acc.Updates = s.Updates; s.Accounts = acc;
        var phone = new PhoneWebApp(s.Settings, _router);
        acc.Phone = phone;
        if (phoneLinked) phone.Save(PhoneUrl, PcToken);
        return (s, phone);
    }

    private static string Between(string s, string a, string b) { var i = s.IndexOf(a) + a.Length; return s[i..s.IndexOf(b, i)]; }

    private async Task<(AppServices s, PhoneWebApp phone)> AdminOnOldSheet()
    {
        var (a, phone) = Machine("A");
        a.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        a.Auth.Login("admin", "matkhau1");
        var script = a.Accounts.BuildScript();
        _old.Token = Between(script, "var MA_DOC = '", "'");
        _old.Admin = Between(script, "var MA_QUAN_TRI = '", "'");
        await a.Accounts.ConnectAsync(OldUrl);
        a.Auth.Logout(); a.Auth.Login("admin", "matkhau1"); await a.Accounts.PublishTask;   // gói khóa ghi lên Sheet cũ
        return (a, phone);
    }

    /// <summary>1.105: 1 mã kết nối máy (KLKN1-) = tài khoản dùng chung + PC_TOKEN điện thoại — máy mới dán 1 lần là có cả hai.</summary>
    [Fact]
    public async Task Ma_ket_noi_may_KLKN_mang_theo_ma_dien_thoai()
    {
        var (a, phone) = Machine("A");
        a.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        a.Auth.Login("admin", "matkhau1");
        await a.Accounts.MoveToPhoneSheetAsync(phone);
        var code = a.Accounts.ConnectionCode(forAdmin: false);
        Assert.StartsWith(AccountSyncService.CodePrefix2, code);
        Assert.True(AccountSyncService.IsCode(code));
        var info = AccountSyncService.ParseAny(code);
        Assert.Equal(PcToken, info.PhoneToken);
        Assert.Equal(PhoneUrl, info.PhoneUrl);
        Assert.Equal("", info.Admin);

        var (b, bPhone) = Machine("B", phoneLinked: false);
        Assert.False(bPhone.Enabled);
        var r = await b.Accounts.ConnectAsync(code);
        Assert.False(r.Admin);
        Assert.True(b.Accounts.OnPhoneSheet);
        Assert.True(bPhone.Enabled);
        Assert.Equal(PcToken, bPhone.Token);
        Assert.Equal(PhoneUrl, bPhone.Url);
        b.Auth.Login("admin", "matkhau1");

        // Bộ cài máy mới (ket-noi.txt) nhận mã KLKN1-
        var dir = Path.Combine(_dir, "bo-cai"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, AccountSyncService.BundleFile), a.Accounts.BundleText());
        Assert.Equal(code, AccountSyncService.BundledCode(dir));
        // Mã cũ KLTK1- vẫn đọc được (không có phần điện thoại)
        var old = AccountSyncService.ParseAny(AccountSyncService.CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(new JsonObject { ["u"] = PhoneUrl, ["t"] = "x" }.ToJsonString())).TrimEnd('='));
        Assert.Equal("", old.PhoneToken);
        Assert.Throws<BusinessException>(() => AccountSyncService.ParseAny("KLLAN1-abc"));
    }

    [Fact]
    public async Task May_moi_chi_can_dang_nhap_tu_ket_noi()
    {
        // Máy Quản trị: dùng chung Sheet điện thoại, có mã GitHub → đăng nhập lại thì tự đẩy mã GitHub cho máy mới.
        var (a, phone) = Machine("A");
        a.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        a.Auth.Login("admin", "matkhau1");
        await a.Accounts.MoveToPhoneSheetAsync(phone);
        a.Auth.CreateUser("nv1", "Nhân viên 1", "matkhau2", Roles.Staff);
        a.Updates.SaveConfig("", "ghp_test123", true);
        a.Auth.Logout();
        a.Auth.Login("admin", "matkhau1");
        await a.Accounts.GhTask;
        Assert.Equal("ghp_test123", _phone.Gh);

        // Máy mới: chưa có mã gì (chỉ có link Web App cài sẵn) → tên + mật khẩu là đủ.
        var (b, bPhone) = Machine("B", phoneLinked: false);
        b.Settings.Set(PhoneWebApp.KeyUrl, PhoneUrl);   // giả lập link cài sẵn (DefaultUrl) trỏ vào Web App giả
        Assert.False(b.Accounts.Enabled);
        BusinessException e = await Assert.ThrowsAsync<JoinException>(() => b.Accounts.JoinByPasswordAsync("nv1", "sai-mat-khau"));
        Assert.Contains("Sai tên đăng nhập", e.Message);
        e = await Assert.ThrowsAsync<JoinException>(() => b.Accounts.JoinByPasswordAsync("khoa", "matkhau2"));
        Assert.Contains("chưa có trên Google Sheet chung", e.Message);
        Assert.False(b.Auth.HasAnyUser());
        var n = await b.Accounts.JoinByPasswordAsync("NV1", "matkhau2");
        Assert.Equal(2, n);
        Assert.True(b.Accounts.Enabled);
        Assert.True(b.Accounts.OnPhoneSheet);
        Assert.Equal(PcToken, bPhone.Token);
        Assert.True(b.Updates.HasToken);
        var u = (await LoginFlow.LoginAsync(b.Auth, b.Accounts, "nv1", "matkhau2")).User;
        Assert.Equal(Roles.Staff, u.Role);
        // Máy đã có tài khoản thì không đăng nhập máy mới nữa (kết nối ở Hệ thống › Kết nối).
        await Assert.ThrowsAsync<BusinessException>(() => b.Accounts.JoinByPasswordAsync("nv1", "matkhau2"));

        // Dò mật khẩu: sai 5 lần → khóa, kể cả mật khẩu đúng.
        var (c, _) = Machine("C", phoneLinked: false);
        c.Settings.Set(PhoneWebApp.KeyUrl, PhoneUrl);
        for (var i = 0; i < 5; i++) await Assert.ThrowsAnyAsync<BusinessException>(() => c.Accounts.JoinByPasswordAsync("admin", "doan" + i));
        e = await Assert.ThrowsAnyAsync<BusinessException>(() => c.Accounts.JoinByPasswordAsync("admin", "matkhau1"));
        Assert.Contains("tạm khóa", e.Message);
        Assert.False(c.Auth.HasAnyUser());

        // Code.gs trên Google còn bản cũ → báo dán Code.gs mới / dán mã KLKN1.
        _phone.OldScript = true;
        var (d, _) = Machine("D", phoneLinked: false);
        d.Settings.Set(PhoneWebApp.KeyUrl, PhoneUrl);
        e = await Assert.ThrowsAnyAsync<BusinessException>(() => d.Accounts.JoinByPasswordAsync("nv1", "matkhau2"));
        Assert.Equal(AccountSyncService.OldJoinScriptMsg, e.Message);
    }

    /// <summary>
    /// 1.107 (người dùng: "đăng nhập máy khác không được" — máy kia đã có tài khoản riêng nên chỉ báo "Sai tên đăng nhập hoặc mật khẩu"):
    /// máy đang dùng tài khoản riêng đăng nhập bằng tài khoản dùng chung → tự hỏi Sheet chung rồi kết nối; tài khoản chưa lên Sheet / Sheet chưa bật → báo rõ.
    /// </summary>
    [Fact]
    public async Task May_da_co_tai_khoan_rieng_dang_nhap_tai_khoan_chung_tu_ket_noi()
    {
        // Sheet chung chưa bật tài khoản dùng chung (máy chính chưa đưa tài khoản lên) → báo rõ, không chỉ "sai mật khẩu".
        var (x, _) = Machine("X", phoneLinked: false);
        x.Settings.Set(PhoneWebApp.KeyUrl, PhoneUrl);
        x.Auth.CreateFirstAdmin("admin", "Quản trị máy X", "rieng123");
        var e = await Assert.ThrowsAsync<BusinessException>(() => LoginFlow.LoginAsync(x.Auth, x.Accounts, "khoa", "matkhau3"));
        Assert.Contains("chưa có tài khoản \"khoa\"", e.Message);
        Assert.Contains("chưa có danh sách tài khoản", e.Message);
        Assert.False(x.Accounts.Enabled);

        // Máy chính bật dùng chung, có tài khoản khoa.
        var (a, phone) = Machine("A");
        a.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        a.Auth.Login("admin", "matkhau1");
        await a.Accounts.MoveToPhoneSheetAsync(phone);
        a.Auth.CreateUser("khoa", "Khoa", "matkhau3", Roles.Admin);

        // Máy X (tài khoản riêng) đăng nhập khoa → hỏi Sheet chung, đúng mật khẩu → tự kết nối và vào luôn.
        var r = await LoginFlow.LoginAsync(x.Auth, x.Accounts, "khoa", "matkhau3");
        Assert.Equal("khoa", r.User.Username);
        Assert.Contains("tự kết nối", r.Note);
        Assert.True(x.Accounts.Enabled);
        Assert.Equal(PcToken, x.Phone.Token);
        x.Auth.Logout();
        // Mật khẩu riêng cũ của admin trên máy X đã thay bằng mật khẩu dùng chung.
        Assert.Equal("admin", (await LoginFlow.LoginAsync(x.Auth, x.Accounts, "admin", "matkhau1")).User.Username);
        x.Auth.Logout();

        // Máy đã nối, tài khoản chỉ có ở máy khác (chưa lên Sheet) → báo cách đưa lên Sheet.
        e = await Assert.ThrowsAsync<BusinessException>(() => LoginFlow.LoginAsync(x.Auth, x.Accounts, "chuaco", "abc123"));
        Assert.Contains("chưa có trên Google Sheet chung", e.Message);
        Assert.Contains("Đồng bộ tài khoản ngay", e.Message);
        // Có tài khoản, sai mật khẩu → câu cũ.
        e = await Assert.ThrowsAsync<BusinessException>(() => LoginFlow.LoginAsync(x.Auth, x.Accounts, "khoa", "sai"));
        Assert.Equal("Sai tên đăng nhập hoặc mật khẩu.", e.Message);

        // Máy riêng khác: tài khoản chưa lên Sheet → báo "chưa có trên Google Sheet chung", vẫn dùng tài khoản riêng.
        var (y, _) = Machine("Y", phoneLinked: false);
        y.Settings.Set(PhoneWebApp.KeyUrl, PhoneUrl);
        y.Auth.CreateFirstAdmin("chu", "Chủ máy Y", "rieng123");
        e = await Assert.ThrowsAsync<JoinException>(() => LoginFlow.LoginAsync(y.Auth, y.Accounts, "khach", "abc123"));
        Assert.Contains("chưa có trên Google Sheet chung", e.Message);
        Assert.False(y.Accounts.Enabled);
        Assert.Equal("chu", (await LoginFlow.LoginAsync(y.Auth, y.Accounts, "chu", "rieng123")).User.Username);
    }

    /// <summary>Code.gs (dnKiemMk_, PBKDF2 JavaScript thuần) và app PC (AuthService.Verify) cho cùng kết quả — đáp án sinh bằng chính Code.gs (Data/dangnhap-maymoi-golden.gen.js).</summary>
    [Fact]
    public void Kiem_mat_khau_Code_gs_khop_app_PC()
    {
        var arr = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "dangnhap-maymoi-golden.json")))!.AsArray();
        Assert.Equal(3, arr.Count);
        foreach (var c in arr)
        {
            string mk = c!["matKhau"]!.ToString(), muoi = Convert.ToBase64String(Convert.FromHexString(c["muoi"]!.ToString()[2..])), bam = Convert.ToBase64String(Convert.FromHexString(c["bam"]!.ToString()[2..]));
            Assert.True(AuthService.Verify(mk, muoi, bam));
            Assert.False(AuthService.Verify(mk + "x", muoi, bam));
            Assert.True(c["dung"]!.GetValue<bool>());
            Assert.False(c["sai"]!.GetValue<bool>());
            Assert.True(c["base64"]!.GetValue<bool>());
        }
    }

    [Fact]
    public async Task Chuyen_sang_Sheet_dien_thoai_giu_ma_va_may_khac_tu_theo()
    {
        var (a, phone) = await AdminOnOldSheet();
        a.Auth.CreateUser("nv1", "Nhân viên 1", "matkhau2", Roles.Staff);
        var (b, _) = Machine("B", phoneLinked: false);                         // máy nhân viên nối Sheet cũ bằng mã kết nối
        await b.Accounts.ConnectAsync(a.Accounts.ConnectionCode(forAdmin: false));

        var r = await a.Accounts.MoveToPhoneSheetAsync(phone);
        Assert.True(r.Uploaded); Assert.True(r.OldMarked); Assert.Equal(2, r.Users);
        Assert.True(a.Accounts.OnPhoneSheet);
        Assert.Equal(PhoneUrl, a.Accounts.Url);
        Assert.Equal(_old.Token, _phone.Acct.Token);                           // giữ 2 mã cũ → mã kết nối KLTK1 các máy vẫn đúng
        Assert.Equal(_old.Admin, _phone.Acct.Admin);
        Assert.Contains(_phone.Acct.Rows, x => x["username"] == AccountSyncService.KeyRowName);   // dòng khóa ghi chép theo
        Assert.DoesNotContain(_phone.Acct.Rows, x => x["username"] == AccountSyncService.MoveRowName);
        Assert.Equal(PhoneUrl, _old.Rows.Single(x => x["username"] == AccountSyncService.MoveRowName)["quyen"]);

        // Quản trị thêm tài khoản → chỉ ghi lên Sheet điện thoại; máy B tự theo dòng "#chuyen-sheet" ở lần tải sau
        a.Auth.CreateUser("nv2", "Nhân viên 2", "matkhau3", Roles.Staff);
        Assert.Contains(_phone.Acct.Rows, x => x["username"] == "nv2");
        Assert.DoesNotContain(_old.Rows, x => x["username"] == "nv2");
        await b.Accounts.PullAsync();
        Assert.Equal(PhoneUrl, b.Accounts.Url);
        b.Auth.Login("nv2", "matkhau3");
        b.Auth.Logout();
        // Quản trị đăng nhập máy B mở được khóa ghi (gói khóa đã nằm ở Sheet điện thoại)
        b.Auth.Login("admin", "matkhau1");
        Assert.True(b.Accounts.CanWrite);
        // Mã kết nối mới chứa đường dẫn Sheet điện thoại
        var (c, _) = Machine("C", phoneLinked: false);
        await c.Accounts.ConnectAsync(a.Accounts.ConnectionCode(forAdmin: false));
        Assert.Equal(PhoneUrl, c.Accounts.Url);
        // Máy mới dán mã KLTK1 CŨ (đường dẫn Sheet cũ) cũng tự theo
        var (d, _) = Machine("D", phoneLinked: false);
        var oldCode = AccountSyncService.CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(new JsonObject { ["u"] = OldUrl, ["t"] = _old.Token }.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await d.Accounts.ConnectAsync(oldCode);
        Assert.Equal(PhoneUrl, d.Accounts.Url);
        d.Auth.Login("nv2", "matkhau3");
    }

    [Fact]
    public async Task May_chua_dung_chung_chuyen_thang_len_Sheet_dien_thoai()
    {
        var (a, phone) = Machine("A");
        a.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        a.Auth.Login("admin", "matkhau1");
        Assert.False(a.Accounts.Enabled);
        var r = await a.Accounts.MoveToPhoneSheetAsync(phone);
        Assert.True(r.Uploaded); Assert.False(r.OldMarked); Assert.Equal(1, r.Users);
        Assert.Matches("^[0-9a-f]{32}$", _phone.Acct.Token);
        Assert.NotEqual(_phone.Acct.Token, _phone.Acct.Admin);
        Assert.True(a.Accounts.CanWrite);
        a.Auth.CreateUser("nv1", "Nhân viên 1", "matkhau2", Roles.Staff);
        Assert.Contains(_phone.Acct.Rows, x => x["username"] == "nv1");

        // Máy quản trị khác (chưa dùng chung) bấm chuyển sau → Sheet đã có mã khác → báo dán mã máy quản trị
        var (b, phoneB) = Machine("B");
        b.Auth.CreateFirstAdmin("admin", "Quản trị", "khac1234");
        b.Auth.Login("admin", "khac1234");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => b.Accounts.MoveToPhoneSheetAsync(phoneB));
        Assert.Contains("máy khác", ex.Message);
        Assert.False(b.Accounts.Enabled);
    }

    [Fact]
    public async Task Code_gs_ban_cu_bao_dan_ban_moi()
    {
        var (a, phone) = await AdminOnOldSheet();
        _phone.OldScript = true;
        var ex = await Assert.ThrowsAsync<BusinessException>(() => a.Accounts.MoveToPhoneSheetAsync(phone));
        Assert.Contains("chưa phải bản mới", ex.Message);
        Assert.Equal(OldUrl, a.Accounts.Url);                                  // không đổi gì
        Assert.DoesNotContain(_old.Rows, x => x["username"] == AccountSyncService.MoveRowName);

        _phone.OldScript = false;
        await a.Accounts.MoveToPhoneSheetAsync(phone);
        _phone.OldScript = true;                                               // ai đó dán lại Code.gs cũ
        var ex2 = Assert.Throws<BusinessException>(() => a.Auth.CreateUser("nv1", "Nhân viên 1", "matkhau2", Roles.Staff));
        Assert.Contains("chưa phải bản mới", ex2.Message);

        var (c, phoneC) = Machine("C", phoneLinked: false);                    // máy chưa nối điện thoại
        c.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        c.Auth.Login("admin", "matkhau1");
        var ex3 = await Assert.ThrowsAsync<BusinessException>(() => c.Accounts.MoveToPhoneSheetAsync(phoneC));
        Assert.Contains("PC_TOKEN", ex3.Message);
    }
}

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
            else if (r["token"]?.ToString() != PcToken) res = new() { ["ok"] = false, ["code"] = "AUTH", ["error"] = "Mã truy cập không đúng." };
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

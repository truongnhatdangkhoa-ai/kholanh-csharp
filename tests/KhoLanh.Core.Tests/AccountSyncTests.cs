using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Hai "máy" (2 CSDL riêng) dùng chung tài khoản qua một Web App giả lập đúng cách Apps Script TaiKhoan.gs xử lý.</summary>
public class AccountSyncTests : IDisposable
{
    private const string Url = "https://script.google.com/macros/s/ABC123/exec";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-acct-" + Guid.NewGuid().ToString("N"));
    private readonly FakeWebApp _web = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>Giả lập Apps Script: mã đọc / mã quản trị, tab TaiKhoan là danh sách dòng chữ.</summary>
    internal sealed class FakeWebApp : HttpMessageHandler
    {
        public string Token = "", Admin = "";
        public readonly List<Dictionary<string, string>> Rows = new();
        public bool Down;
        public readonly List<string> Bodies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (Down) throw new HttpRequestException("mất mạng");
            var text = await req.Content!.ReadAsStringAsync(ct);
            Bodies.Add(text);
            var r = JsonNode.Parse(text)!.AsObject();
            JsonObject res;
            if (r["token"]?.ToString() != Token) res = new() { ["ok"] = false, ["error"] = "Sai mã kết nối." };
            else if (r["action"]?.ToString() == "list")
                res = new() { ["ok"] = true, ["users"] = new JsonArray(Rows.Select(x => (JsonNode?)new JsonObject(x.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value)))).ToArray()) };
            else if (r["adminKey"]?.ToString() != Admin) res = new() { ["ok"] = false, ["error"] = "Máy này không có mã quản trị nên không được sửa tài khoản." };
            else if (r["action"]?.ToString() == "save") { Save(r["user"]!.AsObject()); res = new() { ["ok"] = true }; }
            else if (r["action"]?.ToString() == "replaceAll") { Rows.Clear(); foreach (var u in r["users"]!.AsArray()) Save(u!.AsObject()); res = new() { ["ok"] = true }; }
            else res = new() { ["ok"] = false, ["error"] = "Không có chức năng" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(res.ToJsonString(), Encoding.UTF8, "application/json") };
        }
        private void Save(JsonObject u)
        {
            var d = u.ToDictionary(p => p.Key, p => p.Value?.ToString() ?? "");
            var i = Rows.FindIndex(x => string.Equals(x["username"], d["username"], StringComparison.OrdinalIgnoreCase));
            if (i >= 0) Rows[i] = d; else Rows.Add(d);
        }
    }

    private AppServices Machine(string name)
    {
        var s = new AppServices(Path.Combine(_dir, name, "kho.db"));
        // Cùng Web App giả lập cho mọi máy.
        var acc = new AccountSyncService(s.Db, s.Settings, s.Session, s.Audit, _web);
        s.Auth.Sync = acc;
        acc.Updates = s.Updates;
        s.Accounts = acc;
        return s;
    }

    /// <summary>Máy A (quản trị) tạo mã Apps Script, "dán vào Google Sheet", kết nối bằng đường dẫn.</summary>
    private async Task<AppServices> AdminMachine()
    {
        var a = Machine("A");
        a.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        a.Auth.Login("admin", "matkhau1");
        var script = a.Accounts.BuildScript();
        _web.Token = Between(script, "var MA_DOC = '", "'");
        _web.Admin = Between(script, "var MA_QUAN_TRI = '", "'");
        var r = await a.Accounts.ConnectAsync(Url);
        Assert.True(r.Uploaded); Assert.True(r.Admin);
        return a;
    }

    private static string Between(string s, string a, string b) { var i = s.IndexOf(a) + a.Length; return s[i..s.IndexOf(b, i)]; }

    [Fact]
    public async Task Script_template_gets_fresh_codes()
    {
        var a = await AdminMachine();
        Assert.Matches("^[0-9a-f]{32}$", _web.Token);
        Assert.Matches("^[0-9a-f]{32}$", _web.Admin);
        Assert.NotEqual(_web.Token, _web.Admin);
        Assert.DoesNotContain("__MA_", AccountSyncService.ScriptTemplate().Replace("'__MA_DOC__'", "").Replace("'__MA_QUAN_TRI__'", ""));
        Assert.True(a.Accounts.CanWrite);
        Assert.Single(_web.Rows);
    }

    [Fact]
    public async Task Second_machine_receives_accounts_and_permissions_from_sheet()
    {
        var a = await AdminMachine();
        var perms = Perm.Serialize(new Dictionary<string, string> { ["m1"] = Perm.Edit, ["m2"] = Perm.None });
        a.Auth.CreateUser("nv1", "Nhân viên 1", "matkhau2", Roles.Staff, perms);
        var code = a.Accounts.ConnectionCode(forAdmin: false);

        var b = Machine("B");                       // máy mới, chưa có tài khoản nào
        var r = await b.Accounts.ConnectAsync(code);
        Assert.False(r.Uploaded); Assert.False(r.Admin); Assert.Equal(2, r.Users);
        var u = b.Auth.Login("nv1", "matkhau2");
        Assert.True(b.Session.Can("m1", Perm.Edit));
        Assert.False(b.Session.Can("m2"));
        Assert.Equal("Nhân viên 1", u.FullName);

        // Quản trị đổi quyền ở máy A → máy B nhận ở lần tải sau (lúc đăng nhập).
        var id = a.Auth.ListUsers().Single(x => x.Username == "nv1").Id;
        a.Auth.SetPermissions(id, Perm.Serialize(new Dictionary<string, string> { ["m1"] = Perm.View, ["m2"] = Perm.Export }));
        a.Auth.ResetPassword(id, "matkhau3");
        b.Auth.Logout();
        await b.Accounts.PullAsync();
        Assert.Throws<BusinessException>(() => b.Auth.Login("nv1", "matkhau2"));
        b.Auth.Login("nv1", "matkhau3");
        Assert.False(b.Session.Can("m1", Perm.Edit));
        Assert.True(b.Session.Can("m2", Perm.Export));
    }

    [Fact]
    public async Task Passwords_never_leave_the_machine_in_plain_text()
    {
        var a = await AdminMachine();
        a.Auth.CreateUser("nv1", "NV", "BiMat#2026", Roles.Staff);
        Assert.DoesNotContain(_web.Bodies, x => x.Contains("BiMat#2026") || x.Contains("matkhau1"));
        Assert.All(_web.Rows, r => { Assert.StartsWith("h:", r["mat_khau_bam"]); Assert.StartsWith("h:", r["muoi"]); });
    }

    [Fact]
    public async Task Read_only_machine_cannot_change_accounts()
    {
        var a = await AdminMachine();
        var b = Machine("B");
        await b.Accounts.ConnectAsync(a.Accounts.ConnectionCode(false));
        b.Auth.Login("admin", "matkhau1");
        var ex = Assert.Throws<BusinessException>(() => b.Auth.CreateUser("nv9", "X", "matkhau9", Roles.Staff));
        Assert.Contains("chỉ được đọc", ex.Message);
        Assert.DoesNotContain(b.Auth.ListUsers(), u => u.Username == "nv9");

        // Dán mã quản trị → máy B sửa được.
        await b.Accounts.ConnectAsync(a.Accounts.ConnectionCode(true));
        Assert.True(b.Accounts.CanWrite);
        b.Auth.CreateUser("nv9", "X", "matkhau9", Roles.Staff);
        Assert.Contains(_web.Rows, r => r["username"] == "nv9");
    }

    [Fact]
    public async Task Failed_upload_changes_nothing_and_offline_login_uses_cache()
    {
        var a = await AdminMachine();
        _web.Down = true;
        var ex = Assert.Throws<BusinessException>(() => a.Auth.CreateUser("nv1", "NV", "matkhau2", Roles.Staff));
        Assert.Contains("chưa đổi gì", ex.Message);
        Assert.DoesNotContain(a.Auth.ListUsers(), u => u.Username == "nv1");
        a.Auth.Logout();
        await Assert.ThrowsAsync<BusinessException>(() => a.Accounts.PullAsync());
        a.Auth.Login("admin", "matkhau1"); // bản lưu đệm vẫn đăng nhập được
    }

    [Fact]
    public async Task Account_removed_from_sheet_is_locked_on_other_machines()
    {
        var a = await AdminMachine();
        a.Auth.CreateUser("nv1", "NV", "matkhau2", Roles.Staff);
        var b = Machine("B");
        await b.Accounts.ConnectAsync(a.Accounts.ConnectionCode(false));
        _web.Rows.RemoveAll(r => r["username"] == "nv1");
        await b.Accounts.PullAsync();
        var ex = Assert.Throws<BusinessException>(() => b.Auth.Login("nv1", "matkhau2"));
        Assert.Contains("khóa", ex.Message);
    }

    [Fact]
    public async Task Sheet_without_active_admin_is_rejected()
    {
        await AdminMachine();
        var b = Machine("B");
        _web.Rows[0]["dang_dung"] = "0";
        await Assert.ThrowsAsync<BusinessException>(() => b.Accounts.ConnectAsync(CodeFor()));
        Assert.False(b.Auth.HasAnyUser());
    }

    private string CodeFor() =>
        AccountSyncService.CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(new JsonObject { ["u"] = Url, ["t"] = _web.Token }.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task New_machine_with_bundle_file_only_needs_login_and_gets_update_token()
    {
        var a = await AdminMachine();
        a.Updates.SaveConfig("", "ghp_doc_chi", true);
        a.Auth.CreateUser("nv1", "NV", "matkhau2", Roles.Staff, Perm.Serialize(new Dictionary<string, string> { ["m1"] = Perm.Edit }));
        var bundle = Path.Combine(_dir, "usb");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, AccountSyncService.BundleFile), a.Accounts.BundleText());
        Assert.DoesNotContain(_web.Admin, Encoding.UTF8.GetString(Convert.FromBase64String(Pad(a.Accounts.BundleText().Split('\n').First(l => l.StartsWith("KLTK1-"))[6..].Trim()))));

        var b = Machine("B");
        Assert.False(b.Updates.HasToken);
        var code = AccountSyncService.BundledCode(bundle);
        Assert.NotNull(code);
        await b.Accounts.ConnectAsync(code!);         // màn đăng nhập tự làm bước này
        b.Auth.Login("nv1", "matkhau2");
        Assert.True(b.Session.Can("m1", Perm.Edit));
        Assert.False(b.Accounts.CanWrite);
        Assert.True(b.Updates.HasToken);
        Assert.Null(AccountSyncService.BundledCode(Path.Combine(_dir, "khong-co")));
    }

    private static string Pad(string b64url) { var b = b64url.Replace('-', '+').Replace('_', '/'); return b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='); }

    [Fact]
    public void Codes_and_urls_are_validated()
    {
        Assert.Throws<BusinessException>(() => AccountSyncService.ParseCode("abc"));
        Assert.Throws<BusinessException>(() => AccountSyncService.ParseCode("KLTK1-@@@"));
        Assert.Throws<BusinessException>(() => AccountSyncService.CheckUrl("https://evil.example/exec"));
        Assert.Equal(Url, AccountSyncService.CheckUrl(" " + Url + " "));
        var row = AccountSyncService.ToRow(new User { Username = "a", Role = Roles.Staff, PasswordHash = Convert.ToBase64String(new byte[32]), Salt = Convert.ToBase64String(new byte[16]) });
        row["ho_ten"] = "'=Tên"; // Google Sheet trả về dấu ' bảo vệ
        var back = AccountSyncService.FromRow(row)!;
        Assert.Equal("=Tên", back.FullName);
        Assert.Equal(Convert.ToBase64String(new byte[32]), back.PasswordHash);
    }
}

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
        /// <summary>1.98: giả lập Sheet tài khoản bị dán đè mã của app điện thoại (Code.gs trả AUTH cho mọi lệnh).</summary>
        public bool PhoneScript;
        /// <summary>Đặt thì lệnh "list" chờ tới khi mở (giả lập Apps Script trả lời chậm).</summary>
        public TaskCompletionSource? Gate;
        public int Lists;
        public readonly List<string> Bodies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (Down) throw new HttpRequestException("mất mạng");
            var text = await req.Content!.ReadAsStringAsync(ct);
            var r = JsonNode.Parse(text)!.AsObject();
            if (r["action"]?.ToString() == "list") { Interlocked.Increment(ref Lists); if (Gate is { } g) await g.Task.WaitAsync(ct); }
            lock (Rows) return Handle(text, r);
        }
        private HttpResponseMessage Handle(string text, JsonObject r)
        {
            Bodies.Add(text);
            JsonObject res;
            if (PhoneScript) res = new() { ["ok"] = false, ["code"] = "AUTH", ["error"] = "Tên người dùng hoặc mã truy cập không đúng, hoặc tài khoản đã bị khóa. Liên hệ người quản lý app PC." };
            else if (r["token"]?.ToString() != Token) res = new() { ["ok"] = false, ["error"] = "Sai mã kết nối." };
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
        var up = a.Accounts.UpgradeScript();                     // cập nhật bản mới giữ nguyên 2 mã đang dùng
        Assert.Equal(_web.Token, Between(up, "var MA_DOC = '", "'"));
        Assert.Equal(_web.Admin, Between(up, "var MA_QUAN_TRI = '", "'"));
        Assert.Contains("email_google", up);
    }

    [Fact]
    public async Task Duong_dan_tro_nham_Web_App_dien_thoai_bao_ro_cach_sua()
    {   // người dùng: "vì sao không tạo tài khoản được" — Sheet trả lời bằng mã của app điện thoại
        var a = await AdminMachine();
        _web.PhoneScript = true;
        var ex = Assert.Throws<BusinessException>(() => a.Auth.CreateUser("khoa", "Khoa", "khoa1234", Roles.Admin));
        Assert.Contains("APP ĐIỆN THOẠI", ex.Message);
        Assert.Contains("Ngắt kết nối", ex.Message);
        Assert.DoesNotContain(a.Auth.ListUsers(), u => u.Username == "khoa");   // chưa ghi được lên Sheet → máy không đổi
        _web.PhoneScript = false;
        a.Auth.CreateUser("khoa", "Khoa", "khoa1234", Roles.Admin);
        Assert.Contains(_web.Rows, r => r["username"] == "khoa");
    }

    [Fact]
    public async Task Tai_ngam_loi_khong_thanh_loi_chua_bat()
    {   // báo cáo Trợ lý 1.96: "A Task's exception(s) were not observed" từ AccountSyncService.PullAsync (cửa sổ đăng nhập: _ = PrefetchAsync())
        var a = await AdminMachine();
        _web.PhoneScript = true;
        var seen = 0;
        void H(object? _, UnobservedTaskExceptionEventArgs e) { if (e.Exception.ToString().Contains("APP ĐIỆN THOẠI")) Interlocked.Increment(ref seen); }
        TaskScheduler.UnobservedTaskException += H;
        try
        {
            void Fire() { var t = a.Accounts.PrefetchAsync(); ((IAsyncResult)t).AsyncWaitHandle.WaitOne(10000); Assert.True(t.IsFaulted); }
            Fire();
            a.Accounts.GetType().GetField("_prefetch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(a.Accounts, null);
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            Assert.Equal(0, seen);
        }
        finally { TaskScheduler.UnobservedTaskException -= H; }
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
    public async Task Google_email_is_shared_through_the_sheet()
    {
        var a = await AdminMachine();
        a.Auth.CreateUser("nv1", "Nhân viên 1", "matkhau2", Roles.Staff);
        var id = a.Auth.ListUsers().Single(x => x.Username == "nv1").Id;
        a.Auth.SetGoogleEmail(id, " NV1@Gmail.com ");
        Assert.Equal("nv1@gmail.com", _web.Rows.Single(r => r["username"] == "nv1")["email_google"]);
        var b = Machine("B");
        await b.Accounts.ConnectAsync(a.Accounts.ConnectionCode(forAdmin: false));
        Assert.Equal("nv1", b.Auth.LoginGoogle("nv1@GMAIL.com").Username);
        b.Auth.Logout();
        // Sheet cũ chưa có cột email_google → máy giữ email đang có
        foreach (var r in _web.Rows) r.Remove("email_google");
        await b.Accounts.PullAsync();
        Assert.Equal("nv1", b.Auth.LoginGoogle("nv1@gmail.com").Username);
        b.Auth.Logout();
        // Quản trị bỏ email → máy khác cũng không đăng nhập Google được nữa
        a.Auth.SetGoogleEmail(id, "");
        await b.Accounts.PullAsync();
        Assert.Throws<BusinessException>(() => b.Auth.LoginGoogle("nv1@gmail.com"));
    }

    [Fact]
    public async Task Admin_password_unlocks_writing_on_any_machine()
    {
        var a = await AdminMachine();
        a.Auth.Logout(); a.Auth.Login("admin", "matkhau1"); await a.Accounts.PublishTask;          // máy gốc đăng nhập lại → đưa gói khóa (mã hóa bằng mật khẩu) lên Sheet
        var blobRow = _web.Rows.Single(r => r["username"] == AccountSyncService.KeyRowName);
        Assert.DoesNotContain(_web.Admin, blobRow["quyen"]);        // mã quản trị không bao giờ ở dạng chữ trên Sheet
        Assert.StartsWith("{\"admin\":\"v1:", blobRow["quyen"]);

        var b = Machine("B");                                        // máy mới chỉ có mã đọc, không có "mã quản trị"
        await b.Accounts.ConnectAsync(a.Accounts.ConnectionCode(forAdmin: false));
        Assert.False(b.Accounts.CanWrite);
        b.Auth.Login("admin", "matkhau1");
        Assert.True(b.Accounts.CanWrite);                            // đăng nhập Quản trị → tự mở khóa ghi
        b.Auth.CreateUser("nv2", "Nhân viên 2", "matkhau2", Roles.Staff);
        Assert.Contains(_web.Rows, r => r["username"] == "nv2");
        b.Auth.Logout();
        Assert.False(b.Accounts.CanWrite);                           // đăng xuất → mất khóa, không lưu vào máy

        // nhân viên đăng nhập ở máy B: không có mật khẩu Quản trị → không ghi được
        await b.Accounts.PullAsync();
        b.Auth.Login("nv2", "matkhau2");
        Assert.False(b.Accounts.CanWrite);
        b.Auth.Logout();

        // Quản trị đổi mật khẩu ở máy B → khóa được mã hóa lại theo mật khẩu mới; máy khác đăng nhập mật khẩu mới vẫn mở được
        b.Auth.Login("admin", "matkhau1");
        b.Auth.ChangeOwnPassword("matkhau1", "matkhaumoi");
        b.Auth.Logout();
        var c = Machine("C");
        await c.Accounts.ConnectAsync(a.Accounts.ConnectionCode(forAdmin: false));
        c.Auth.Login("admin", "matkhaumoi");
        Assert.True(c.Accounts.CanWrite);
        c.Auth.CreateUser("nv3", "Nhân viên 3", "matkhau2", Roles.Staff);
        Assert.Contains(_web.Rows, r => r["username"] == "nv3");
    }

    [Fact]
    public void Seal_roundtrip_and_wrong_password()
    {
        var blob = AccountSyncService.Seal("matkhau1", "0123456789abcdef");
        Assert.Equal("0123456789abcdef", AccountSyncService.Unseal("matkhau1", blob));
        Assert.Equal("", AccountSyncService.Unseal("sai-mat-khau", blob));
        Assert.Equal("", AccountSyncService.Unseal("matkhau1", "v1:xyz"));
        Assert.NotEqual(blob, AccountSyncService.Seal("matkhau1", "0123456789abcdef"));   // mỗi lần muối + nonce khác
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
        Assert.Contains("chưa mở được khóa ghi", ex.Message);
        Assert.DoesNotContain(b.Auth.ListUsers(), u => u.Username == "nv9");

        // Dán mã quản trị → máy B sửa được.
        await b.Accounts.ConnectAsync(a.Accounts.ConnectionCode(true));
        Assert.True(b.Accounts.CanWrite);
        b.Auth.CreateUser("nv9", "X", "matkhau9", Roles.Staff);
        Assert.Contains(_web.Rows, r => r["username"] == "nv9");
    }

    [Fact]
    public async Task Admin_can_unlock_writing_from_accounts_screen()
    {
        var a = await AdminMachine();
        var b = Machine("B");
        await b.Accounts.ConnectAsync(a.Accounts.ConnectionCode(false));
        b.Auth.Login("admin", "matkhau1");                            // máy gốc chưa đăng nhập lại → Sheet chưa có gói khóa
        Assert.False(b.Accounts.CanWrite);
        await Assert.ThrowsAsync<BusinessException>(() => b.Accounts.UnlockAsync("sai-mat-khau"));
        var ex = await Assert.ThrowsAsync<BusinessException>(() => b.Accounts.UnlockAsync("matkhau1"));
        Assert.Contains("chưa có khóa ghi", ex.Message);
        await Assert.ThrowsAsync<BusinessException>(() => b.Accounts.UnlockAsync(a.Accounts.ConnectionCode(false)));   // mã nhân viên không đủ

        a.Auth.Logout(); a.Auth.Login("admin", "matkhau1"); await a.Accounts.PublishTask;          // máy gốc đăng nhập → gói khóa lên Sheet
        await b.Accounts.UnlockAsync("matkhau1");                    // máy B không cần đăng nhập lại: tải gói mới nhất rồi mở
        Assert.True(b.Accounts.CanWrite);
        b.Auth.CreateUser("nv7", "X", "matkhau7", Roles.Staff);
        Assert.Contains(_web.Rows, r => r["username"] == "nv7");
        b.Auth.Logout();
        Assert.False(b.Accounts.CanWrite);

        var c = Machine("C");                                        // hoặc dán mã cho máy quản trị ngay ở màn hình Tài khoản
        await c.Accounts.ConnectAsync(a.Accounts.ConnectionCode(false));
        c.Auth.Login("nv7", "matkhau7");
        await Assert.ThrowsAsync<BusinessException>(() => c.Accounts.UnlockAsync(a.Accounts.ConnectionCode(true)));   // không phải Quản trị
        c.Auth.Logout(); c.Auth.Login("admin", "matkhau1");
        c.Accounts.AfterLogout();                                    // giả lập chưa mở được bằng mật khẩu
        Assert.False(c.Accounts.CanWrite);
        await c.Accounts.UnlockAsync(a.Accounts.ConnectionCode(true));
        Assert.True(c.Accounts.CanWrite);
        c.Auth.Logout();
        Assert.True(c.Accounts.CanWrite);                            // mã đã lưu vào máy
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
        Assert.DoesNotContain(_web.Admin, Encoding.UTF8.GetString(Convert.FromBase64String(Pad(a.Accounts.BundleText().Split('\n').First(l => AccountSyncService.IsCode(l))[6..].Trim()))));

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

    // ---------------- đăng nhập nhanh (1.59): tải danh sách ngầm, không bắt chờ Google Sheet ----------------

    private async Task<(AppServices a, AppServices b)> TwoMachines()
    {
        var a = await AdminMachine();
        a.Auth.CreateUser("nv1", "NV", "matkhau2", Roles.Staff);
        var b = Machine("B");
        _code = a.Accounts.ConnectionCode(false);
        await b.Accounts.ConnectAsync(_code);
        return (a, b);
    }

    [Fact]
    public async Task Slow_sheet_does_not_block_login_and_locked_account_is_caught_after()
    {
        var (a, b) = await TwoMachines();
        _web.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await LoginFlow.LoginAsync(b.Auth, b.Accounts, "nv1", "matkhau2", TimeSpan.FromMilliseconds(200));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), "không được chờ Google Sheet");
        Assert.NotNull(r.Pending);
        Assert.Equal("nv1", b.Session.Username);

        // Trong lúc đó Quản trị khóa nv1 trên Sheet → lượt tải ngầm xong thì báo để đăng xuất.
        var id = a.Auth.ListUsers().Single(x => x.Username == "nv1").Id;
        a.Auth.UpdateUser(id, "NV", Roles.Staff, isActive: false);
        _web.Gate.SetResult();
        await r.Pending!;
        Assert.Contains("khóa", LoginFlow.Recheck(b.Db, r.User, r.PasswordHash));
    }

    [Fact]
    public async Task Account_new_on_sheet_waits_for_the_pull()
    {
        var (a, b) = await TwoMachines();
        a.Auth.CreateUser("nv2", "NV 2", "matkhau3", Roles.Staff);   // máy B chưa có nv2 trong bản lưu
        _web.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var login = LoginFlow.LoginAsync(b.Auth, b.Accounts, "nv2", "matkhau3", TimeSpan.FromMilliseconds(100));
        await Task.Delay(300);
        Assert.False(login.IsCompleted);                              // bản lưu không có → chờ danh sách mới
        _web.Gate.SetResult();
        var r = await login;
        Assert.Null(r.Pending);
        Assert.Equal("nv2", b.Session.Username);
        Assert.Null(LoginFlow.Recheck(b.Db, r.User, r.PasswordHash));
    }

    [Fact]
    public async Task Prefetch_is_shared_and_offline_falls_back_to_cache()
    {
        var (_, b) = await TwoMachines();
        var before = _web.Lists;
        var p = b.Accounts.PrefetchAsync();
        await p;
        var r = await LoginFlow.LoginAsync(b.Auth, b.Accounts, "nv1", "matkhau2");
        Assert.Equal(before + 1, _web.Lists);                         // lần bấm Đăng nhập dùng lại lượt tải lúc mở cửa sổ
        Assert.Null(r.Pending); Assert.Equal("", r.Note);
        b.Auth.Logout();

        var c = Machine("C");
        await c.Accounts.ConnectAsync(_code!);
        _web.Down = true;
        var r2 = await LoginFlow.LoginAsync(c.Auth, c.Accounts, "nv1", "matkhau2");
        Assert.Contains("dùng bản lưu", r2.Note);
        Assert.Equal("nv1", c.Session.Username);
    }

    private string? _code;
}

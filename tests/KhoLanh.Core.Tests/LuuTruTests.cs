using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using KhoLanh.Core.LuuTru;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Google Drive + máy chủ token giả lập trong bộ nhớ (đủ các lệnh LtGoogle dùng).</summary>
internal sealed class FakeGoogle : HttpMessageHandler
{
    public sealed class F { public string Id = "", Name = "", Parent = "", Mime = ""; public byte[] Data = Array.Empty<byte>(); public bool Folder; }
    public readonly Dictionary<string, F> Files = new();
    private readonly Dictionary<string, (string? Id, string Name, string Parent, string Mime)> _sessions = new();
    private int _n;
    public bool RefreshDenied;
    public int Uploads, TokenCalls;
    public string Email = "nguoidung@gmail.com";

    private static HttpResponseMessage J(object o, HttpStatusCode st = HttpStatusCode.OK) =>
        new(st) { Content = new StringContent(o is string s ? s : System.Text.Json.JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };

    public IEnumerable<F> In(string parent) => Files.Values.Where(f => f.Parent == parent);
    public F? Folder(string parent, string name) => Files.Values.FirstOrDefault(f => f.Folder && f.Parent == parent && f.Name == name);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var url = req.RequestUri!.ToString();
        if (url.StartsWith(LtGoogle.TokenUrl))
        {
            TokenCalls++;
            var form = await req.Content!.ReadAsStringAsync(ct);
            if (form.Contains("grant_type=refresh_token"))
                return RefreshDenied ? J(new { error = "invalid_grant", error_description = "Token has been expired or revoked." }, HttpStatusCode.BadRequest)
                                     : J(new { access_token = "AT" + TokenCalls, expires_in = 3600 });
            var payload = LtGoogle.B64Url(Encoding.UTF8.GetBytes("{\"email\":\"" + Email + "\"}"));
            return J(new { access_token = "AT0", refresh_token = "RT-" + Email, expires_in = 3600, scope = LtGoogle.Scope, id_token = "x." + payload + ".y" });
        }
        if (url.StartsWith(LtGoogle.RevokeUrl)) return J(new { });
        if (req.Headers.Authorization?.Parameter?.StartsWith("AT") != true) return J(new { error = new { message = "no auth" } }, HttpStatusCode.Unauthorized);
        if (req.Method == HttpMethod.Get && url.StartsWith(LtGoogle.Api + "?"))
        {
            var q = Uri.UnescapeDataString(url[(url.IndexOf("q=") + 2)..].Split('&')[0]);
            var parent = q.Split('\'')[1];
            string? name = q.Contains(" and name='") ? q[(q.IndexOf(" and name='") + 11)..].TrimEnd('\'') : null;
            var list = In(parent).Where(f => name is null || f.Name == name)
                .Select(f => new Dictionary<string, object> { ["id"] = f.Id, ["name"] = f.Name, ["size"] = f.Data.Length.ToString(), ["mimeType"] = f.Folder ? LtGoogle.FolderMime : f.Mime, ["modifiedTime"] = "2026-11-24T10:00:00Z" });
            return J(new { files = list });
        }
        if (req.Method == HttpMethod.Post && url.StartsWith(LtGoogle.Api + "?"))
        {
            var j = JsonNode.Parse(await req.Content!.ReadAsStringAsync(ct))!;
            var f = new F { Id = "d" + ++_n, Name = j["name"]!.ToString(), Parent = j["parents"]![0]!.ToString(), Folder = true };
            Files[f.Id] = f;
            return J(new { id = f.Id });
        }
        if (url.StartsWith(LtGoogle.UploadApi))
        {
            string? id = req.Method == HttpMethod.Patch ? url[(LtGoogle.UploadApi.Length + 1)..].Split('?')[0] : null;
            string name = "", parent = "";
            if (id is null) { var j = JsonNode.Parse(await req.Content!.ReadAsStringAsync(ct))!; name = j["name"]!.ToString(); parent = j["parents"]![0]!.ToString(); }
            var sid = "s" + ++_n;
            _sessions[sid] = (id, name, parent, req.Headers.GetValues("X-Upload-Content-Type").First());
            var r = J(new { }); r.Headers.Location = new Uri("https://upload.fake/" + sid); return r;
        }
        if (url.StartsWith("https://upload.fake/") && req.Method == HttpMethod.Put)
        {
            var (id, name, parent, mime) = _sessions[url[20..]];
            var data = await req.Content!.ReadAsByteArrayAsync(ct);
            Uploads++;
            if (id is not null) { Files[id].Data = data; return J(new { id }); }
            var f = new F { Id = "f" + ++_n, Name = name, Parent = parent, Data = data, Mime = mime };
            Files[f.Id] = f;
            return J(new { id = f.Id });
        }
        if (url.StartsWith(LtGoogle.Api + "/"))
        {
            var id = url[(LtGoogle.Api.Length + 1)..].Split('?')[0];
            if (req.Method == HttpMethod.Delete) { Files.Remove(id); return new HttpResponseMessage(HttpStatusCode.NoContent); }
            if (!Files.TryGetValue(id, out var f)) return J(new { error = new { message = "File not found" } }, HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(f.Data) };
        }
        return J(new { error = new { message = "unknown " + url } }, HttpStatusCode.BadRequest);
    }
}

public class LuuTruTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "kl-lt-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private static Session Sess(string user = "anan") => new() { CurrentUser = new User { Username = user, Role = Roles.Admin } };

    private string NewFile(string name, string content)
    {
        var p = Path.Combine(_tmp, "src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    private LuuTruService Svc(FakeGoogle g, string machine = "may1", Session? s = null) =>
        new(Path.Combine(_tmp, machine), s ?? Sess(), null, g) { ClientOverride = new("123.apps.googleusercontent.com", "sec") };

    [Fact]
    public void Store_notes_files_folders_tags_search_and_trash()
    {
        var st = new LtStore(Path.Combine(_tmp, "kho"), "AnAn Kho");
        Assert.Equal("anan_kho", st.Account);
        var ng = st.AddNgan("Hợp đồng");
        Assert.Throws<BusinessException>(() => st.AddNgan("hợp đồng"));
        var n1 = st.AddNote(ng.Id, "Số tài khoản ngân hàng", "Vietcombank 0123\nchi nhánh Cần Thơ", LtStore.ParseTags("ngân hàng, #quan trọng"));
        var n2 = st.AddNote("", "", "Xem bảng giá https://example.com/gia ngay");
        var f1 = st.AddFile(ng.Id, NewFile("Hop dong 2026.pdf", "PDF"));
        var img = st.AddFile("khong-co", NewFile("anh.JPG", "IMG"));
        Assert.Equal(new[] { "ngân hàng", "quan trọng" }, n1.Tg);
        Assert.Equal("link", n2.Kind); Assert.Equal("note", n1.Kind); Assert.Equal("file", f1.Kind); Assert.Equal("image", img.Kind);
        Assert.Equal(LtStore.DefaultNgan, img.Ng);                         // ngăn không có → Chung
        Assert.Equal("Hop dong 2026.pdf", f1.Title);
        Assert.Equal("Xem bảng giá https://example.com/gia ngay", n2.Title);
        Assert.True(File.Exists(st.AbsPath(f1)));
        Assert.Equal(3, f1.Fs);

        Assert.Single(st.Query(q: "can tho"));                              // tìm không dấu trong nội dung
        Assert.Single(st.Query(q: "QUAN TRONG"));                           // trong thẻ
        Assert.Equal(2, st.Query(ngan: ng.Id).Count);
        Assert.Single(st.Query(kind: "link"));
        Assert.Single(st.Query(tag: "Ngân hàng"));
        st.Edit(n2.Id, new LtStore.Patch(Pinned: true));
        Assert.Equal(n2.Id, st.Query()[0].Id);                              // đã ghim lên đầu

        Assert.Equal(1, st.Move(new[] { img.Id }, ng.Id));
        Assert.Equal(3, st.Ngans().First(x => x.Id == ng.Id).Count);
        st.DeleteNgan(ng.Id);                                               // mục về ngăn Chung, không mất
        Assert.Equal(4, st.Query(ngan: LtStore.DefaultNgan).Count);
        Assert.Single(st.Ngans());

        st.SetDeleted(new[] { f1.Id, n1.Id }, 1);
        Assert.Equal(2, st.Query(kind: "trash").Count);
        Assert.True(File.Exists(st.AbsPath(f1)));                           // thùng rác vẫn giữ tệp
        st.SetDeleted(new[] { n1.Id }, 0);
        Assert.Equal(1, st.EmptyTrash());
        Assert.Equal("", st.Get(f1.Id)!.Lf);
        Assert.Equal(3, st.Stats().Items);
        Assert.Throws<BusinessException>(() => st.Edit(n1.Id, new LtStore.Patch(Title: "", Text: " ")));

        var again = new LtStore(Path.Combine(_tmp, "kho"), "anan kho");     // đọc lại từ đĩa
        Assert.Equal(3, again.Query().Count);
        Assert.Equal(n2.Id, again.Query()[0].Id);
        Assert.Empty(new LtStore(Path.Combine(_tmp, "kho"), "khac").Query());
    }

    [Fact]
    public async Task Sign_in_with_loopback_and_pkce()
    {
        var g = new FakeGoogle();
        var svc = Svc(g);
        string? authUrl = null;
        var mail = await svc.SignInAsync(url =>
        {
            authUrl = url;
            var q = LtGoogle.ParseQuery("GET /" + url[url.IndexOf('?')..] + " HTTP/1.1");
            // "trình duyệt": Google chuyển về cổng nội bộ kèm mã
            _ = Task.Run(async () =>
            {
                using var h = new HttpClient();
                await h.GetStringAsync(q["redirect_uri"] + "?state=" + Uri.EscapeDataString(q["state"]) + "&code=MA-123");
            });
        }, CancellationToken.None);
        Assert.Equal("nguoidung@gmail.com", mail);
        Assert.Contains("code_challenge_method=S256", authUrl);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A", authUrl);
        Assert.Contains(Uri.EscapeDataString(LtGoogle.DriveScope), authUrl);
        var cfg = svc.Config;
        Assert.True(cfg.Linked); Assert.Equal("nguoidung@gmail.com", cfg.Email);
        Assert.Equal("RT-nguoidung@gmail.com", Secret.Unprotect(cfg.Refresh));
        await svc.SignOutAsync();
        Assert.False(svc.Config.Linked);
    }

    private static void Link(LuuTruService svc, string refresh = "RT")
    {
        var c = svc.Config; c.Email = "nguoidung@gmail.com"; c.Refresh = Secret.Protect(refresh); svc.SaveConfig(c);
    }

    [Fact]
    public async Task Backup_uploads_each_file_once_and_restores_on_another_machine()
    {
        var g = new FakeGoogle();
        var svc = Svc(g);
        Link(svc);
        var st = svc.Store;
        var note = st.AddNote("", "Mật khẩu wifi", "kho lanh 2026");
        var f1 = st.AddFile("", NewFile("bao-gia.xlsx", "XLSX-1"));
        var f2 = st.AddFile("", NewFile("anh.png", "PNG-2"));
        Assert.True(svc.Dirty(st, svc.Config));

        var r1 = await svc.BackupAsync();
        Assert.Equal(2, r1.Uploaded);
        var root = g.Folder("root", LuuTruService.DriveRoot)!;
        var user = g.Folder(root.Id, "anan")!;
        var tep = g.Folder(user.Id, "tep")!;
        Assert.Equal(2, g.In(tep.Id).Count());
        Assert.Single(g.In(user.Id), f => f.Name == "data.json");
        Assert.Single(g.In(g.Folder(user.Id, "ban-theo-ngay")!.Id));
        Assert.False(svc.Dirty(st, svc.Config));
        Assert.True(st.Get(f1.Id)!.Fid.Length > 0);

        var r2 = await svc.BackupAsync();                                   // không đổi gì: không tải lại tệp
        Assert.Equal(0, r2.Uploaded);
        Assert.Equal(2, g.In(tep.Id).Count());

        st.SetDeleted(new[] { f2.Id }, 2);                                  // xóa hẳn → xóa luôn trên Drive ở lượt sau
        st.Edit(note.Id, new LtStore.Patch(Text: "kho lanh 2027"));
        var r3 = await svc.BackupAsync();
        Assert.Equal(1, r3.Deleted);
        Assert.Single(g.In(tep.Id));

        // máy mới, cùng tài khoản + cùng Gmail → khôi phục
        var svc2 = Svc(g, "may2");
        Link(svc2);
        var rs = await svc2.RestoreAsync();
        Assert.Equal(1, rs.Files);
        var st2 = svc2.Store;
        Assert.Equal("kho lanh 2027", st2.Get(note.Id)!.Tx);
        Assert.Equal("XLSX-1", File.ReadAllText(st2.AbsPath(st2.Get(f1.Id)!)));
        Assert.Equal(2, st2.Query().Count);
        Assert.False(svc2.Dirty(st2, svc2.Config));
        Assert.Contains(svc2.Config.History, h => h.Kind == "Khôi phục" && h.Ok);
        Assert.Equal(2, (await svc2.SnapshotsAsync()).Count);              // data.json + bản theo ngày
    }

    [Fact]
    public async Task Expired_google_login_marks_relink()
    {
        var g = new FakeGoogle { RefreshDenied = true };
        var svc = Svc(g);
        Link(svc);
        svc.Store.AddNote("", "a", "b");
        await Assert.ThrowsAsync<LtRelinkException>(() => svc.BackupAsync());
        var c = svc.Config;
        Assert.True(c.NeedRelink);
        Assert.Contains("đăng nhập lại", c.LastErr);
        Assert.False(c.IsDue(DateTime.Now.AddHours(5), true, DateTime.Now.AddHours(-1)));   // không tự thử khi cần đăng nhập lại
    }

    [Fact]
    public async Task Old_daily_copies_are_removed()
    {
        var g = new FakeGoogle();
        var svc = Svc(g);
        Link(svc);
        var c = svc.Config; c.KeepDays = 7; svc.SaveConfig(c);
        svc.Store.AddNote("", "a", "b");
        svc.Clock = () => new DateTime(2026, 11, 1, 9, 0, 0);
        await svc.BackupAsync();
        svc.Store.AddNote("", "c", "d");
        svc.Clock = () => new DateTime(2026, 11, 7, 9, 0, 0);
        await svc.BackupAsync();
        svc.Clock = () => new DateTime(2026, 11, 8, 9, 0, 0);
        svc.Store.AddNote("", "e", "f");
        await svc.BackupAsync();
        var days = g.Files.Values.Where(f => f.Name.StartsWith("data-")).Select(f => f.Name).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "data-2026-11-07.json", "data-2026-11-08.json" }, days);
    }

    [Fact]
    public void Schedule_rules()
    {
        var now = new DateTime(2026, 11, 24, 18, 0, 0);
        var c = new LtConfig { Refresh = "x", Auto = LtConfig.AutoChange, LastOk = now.AddMinutes(-5) };
        Assert.False(c.IsDue(now, false, now.AddMinutes(-10)));             // không có thay đổi
        Assert.False(c.IsDue(now, true, now.AddMinutes(-1)));               // vừa sửa: chờ 2 phút
        Assert.True(c.IsDue(now, true, now.AddMinutes(-3)));
        c.Auto = LtConfig.AutoHour;
        Assert.False(c.IsDue(now, true, now.AddMinutes(-3)));
        c.LastOk = now.AddMinutes(-61);
        Assert.True(c.IsDue(now, true, now));
        c.Auto = LtConfig.AutoDaily; c.DailyHour = 17; c.LastOk = now.AddHours(-20);
        Assert.True(c.IsDue(now, true, now));                               // qua 17 giờ, hôm nay chưa sao lưu
        c.LastOk = now.AddHours(-1);
        Assert.False(c.IsDue(now, true, now));
        c.LastOk = now.AddHours(-40);
        Assert.True(c.IsDue(now.Date.AddHours(8), true, now));              // máy không mở lúc 17 giờ: bù sau 24 giờ
        c.Auto = LtConfig.AutoChange; c.LastErr = "lỗi mạng"; c.LastTry = now.AddMinutes(-3);
        Assert.False(c.IsDue(now, true, now.AddMinutes(-30)));              // lỗi vừa xong: chờ 10 phút
        c.Auto = LtConfig.AutoOff; c.LastErr = "";
        Assert.False(c.IsDue(now, true, now.AddDays(-1)));
    }

    [Fact]
    public void Zip_roundtrip()
    {
        var a = Svc(new FakeGoogle());
        var n = a.Store.AddNote(a.Store.AddNgan("Riêng").Id, "Ghi chú", "nội dung", new[] { "x" });
        var f = a.Store.AddFile("", NewFile("so-lieu.csv", "a,b\n1,2"));
        var zip = Path.Combine(_tmp, "sao-luu.zip");
        Assert.Equal(1, a.ExportZip(zip));
        var b = Svc(new FakeGoogle(), "may2");
        var r = b.ImportZip(zip);
        Assert.Equal(1, r.Files);
        Assert.Equal("nội dung", b.Store.Get(n.Id)!.Tx);
        Assert.Equal("Riêng", b.Store.NganName(b.Store.Get(n.Id)!.Ng));
        Assert.Equal("a,b\n1,2", File.ReadAllText(b.Store.AbsPath(b.Store.Get(f.Id)!)));
        Assert.Equal(0, b.ImportZip(zip).Items);                           // nạp lại: không nhân đôi
    }

    [Fact]
    public void Imports_old_app_personal_storage()
    {
        // thư mục app cũ: PM-Kho-Lanh/app/config/luu-tru/anan/data.json + files/2026-10/<id>__<tên>
        var old = Path.Combine(_tmp, "PM-Kho-Lanh");
        var acc = Path.Combine(old, "app", "config", "luu-tru", "anan");
        Directory.CreateDirectory(Path.Combine(acc, "files", "2026-10"));
        File.WriteAllText(Path.Combine(acc, "files", "2026-10", "abc1__hd.pdf"), "OLD-PDF");
        File.WriteAllText(Path.Combine(old, "app", "config", "drive-ca-nhan.json"), "{\"v\":1,\"clientId\":\"999.apps.googleusercontent.com\",\"clientSecret\":\"enc:xyz\",\"users\":{}}");
        File.WriteAllText(Path.Combine(acc, "data.json"), """
            {"v":1,"seq":5,"cur":{"drv":0},"recs":{
              "mydocs":{"id":"mydocs","k":"n","tx":"My Documents","ts":1,"up":1,"dl":0,"pn":0,"tg":[],"src":"pc","sq":0,"pd":0,"lf":""},
              "ng1":{"id":"ng1","k":"n","tx":"Công việc","fn":"💼","ts":1700000000000,"up":1700000000000,"dl":0,"pn":0,"tg":[],"src":"pc","sq":1,"pd":0,"lf":""},
              "msg1":{"id":"msg1","k":"m","ng":"ng1","t":"text","tx":"Gọi lại khách 0909","ts":1700000001000,"up":1700000001000,"dl":0,"pn":1,"tg":["khách"],"src":"dt","sq":2,"pd":0,"lf":"","th":""},
              "abc1":{"id":"abc1","k":"m","ng":"mydocs","t":"file","fn":"hd.pdf","fs":7,"mt":"application/pdf","fid":"oldDrive","ts":1700000002000,"up":1700000002000,"dl":0,"pn":0,"tg":[],"src":"pc","sq":3,"pd":0,"lf":"2026-10/abc1__hd.pdf"},
              "del1":{"id":"del1","k":"m","ng":"mydocs","t":"text","tx":"cũ","ts":1,"up":2,"dl":1,"pn":0,"tg":[],"src":"pc","sq":4,"pd":0,"lf":""}
            }}
            """);
        var found = LuuTruService.FindLegacy(old);
        Assert.Single(found);
        Assert.Equal("anan", found[0].Account);
        Assert.Equal(2, found[0].Items);
        Assert.Equal("999.apps.googleusercontent.com", LuuTruService.ReadLegacyClient(old)!.Id);
        Assert.Equal("", LuuTruService.ReadLegacyClient(old)!.Secret);   // secret mã hóa Electron: dán lại

        var svc = Svc(new FakeGoogle());
        var r = svc.ImportLegacy(found[0].Dir);
        Assert.Equal(1, r.Files);
        var st = svc.Store;
        Assert.Equal("Chung", st.NganName(LtStore.DefaultNgan));             // không đè tên ngăn mặc định
        Assert.Equal("Công việc", st.NganName(st.Get("msg1")!.Ng));
        Assert.Equal("Gọi lại khách 0909", st.Get("msg1")!.Title);
        Assert.Equal("OLD-PDF", File.ReadAllText(st.AbsPath(st.Get("abc1")!)));
        Assert.Equal("", st.Get("abc1")!.Fid);                               // mã Drive cũ bỏ
        Assert.Single(st.Query(kind: "trash"));
        Assert.True(File.Exists(Path.Combine(acc, "data.json")));            // app cũ không bị đụng
        Assert.Equal(0, svc.ImportLegacy(found[0].Dir).Items);
    }

    [Fact]
    public void Helpers()
    {
        var q = LtGoogle.ParseQuery("GET /?state=a%2Bb&code=4%2F0Ab&scope=email+openid HTTP/1.1");
        Assert.Equal("a+b", q["state"]); Assert.Equal("4/0Ab", q["code"]); Assert.Equal("email openid", q["scope"]);
        var payload = LtGoogle.B64Url(Encoding.UTF8.GetBytes("{\"email\":\"a@b.com\",\"sub\":\"1\"}"));
        Assert.Equal("a@b.com", LtGoogle.EmailOf("h." + payload + ".s"));
        Assert.Equal("", LtGoogle.EmailOf("rác"));
        var (v, ch) = LtGoogle.Pkce();
        Assert.True(v.Length >= 43); Assert.DoesNotContain('=', ch);
        Assert.Equal("Lưu trữ cá nhân", Perm.Find(LuuTruService.PermKey)!.Label);
        Assert.Equal(new DateTime(2026, 11, 3), LuuTruService.DayOf("data-2026-11-03.json"));
        Assert.Null(LuuTruService.DayOf("data.json"));
        Assert.Equal("1.5 MB", LuuTruService.Size(1572864));
    }
}

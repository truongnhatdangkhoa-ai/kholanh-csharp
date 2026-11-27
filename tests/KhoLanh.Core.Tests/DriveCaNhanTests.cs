using KhoLanh.Core.Models;
using System.IO.Compression;
using System.Text.Json.Nodes;
using KhoLanh.Core;
using KhoLanh.Core.LuuTru;
using KhoLanh.Core.SaoLuu;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Sao lưu database lên Google Drive cá nhân (đăng nhập Gmail) — 1.68.</summary>
public class DriveCaNhanTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "kl-dcn-" + Guid.NewGuid().ToString("N"));
    private readonly List<AppServices> _live = new();
    public void Dispose()
    {
        foreach (var s in _live) s.Lan.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tmp, true); } catch { }
    }

    private AppServices Machine(string name, bool first = true)
    {
        var s = new AppServices(Path.Combine(_tmp, name, "kho.db"));
        if (first) s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        s.Auth.Login("admin", "matkhau1");
        _live.Add(s);
        return s;
    }

    private static DriveCaNhanService Svc(AppServices s, FakeGoogle g, Func<DateTime> clock) =>
        new(s.Settings, s.Session, s.Backup, () => s.Lan, g) { ClientOverride = new("123.apps.googleusercontent.com", "sec"), Clock = clock };

    private static Task<string> SignIn(DriveCaNhanService d) => d.SignInAsync(url =>
    {
        var q = LtGoogle.ParseQuery("GET /" + url[url.IndexOf('?')..] + " HTTP/1.1");
        _ = Task.Run(async () =>
        {
            using var h = new HttpClient();
            await h.GetStringAsync(q["redirect_uri"] + "?state=" + Uri.EscapeDataString(q["state"]) + "&code=MA-1");
        });
    }, CancellationToken.None);

    private static JsonArray Rows(params string[] v) => new(v.Select(x => (JsonNode)new JsonObject { ["ma"] = x }).ToArray());
    private static string Names(AppServices s) =>
        string.Join(",", JsonNode.Parse(s.Modules.ReadVersioned("ton-kho-an-an", "S").Json)!.AsArray().Select(r => r!["ma"]!.ToString()));

    [Fact]
    public async Task Luu_tru_ca_nhan_va_Sao_luu_dung_chung_mot_dang_nhap_Google()
    {
        var s = Machine("chung"); var g = new FakeGoogle();
        var d = Svc(s, g, () => DateTime.Now);
        var l = new KhoLanh.Core.LuuTru.LuuTruService(Path.Combine(_tmp, "chung", "luu-tru"), s.Session, s.Settings, g)
        { ClientOverride = new("123.apps.googleusercontent.com", "sec") };
        AppServices.LinkGoogleLogin(l, d);

        // đăng nhập ở Sao lưu → Lưu trữ cá nhân dùng luôn
        Assert.False(l.Config.Linked);
        await SignIn(d);
        Assert.True(l.Config.Linked);
        Assert.Equal("nguoidung@gmail.com", l.Config.Email);
        Assert.Equal(d.Config.Refresh, l.Config.Refresh);

        // đăng xuất ở một nơi → cả hai cùng gỡ
        await l.SignOutAsync();
        Assert.False(l.Config.Linked); Assert.False(d.Config.Linked);

        // đăng nhập ở Lưu trữ cá nhân → Sao lưu dùng luôn; token đã có từ trước (chưa chia sẻ) cũng được nhận
        g.Email = "khac@gmail.com";
        await l.SignInAsync(url =>
        {
            var q = LtGoogle.ParseQuery("GET /" + url[url.IndexOf('?')..] + " HTTP/1.1");
            _ = Task.Run(async () => { using var h = new HttpClient(); await h.GetStringAsync(q["redirect_uri"] + "?state=" + Uri.EscapeDataString(q["state"]) + "&code=MA-2"); });
        }, CancellationToken.None);
        Assert.True(d.Config.Linked); Assert.Equal("khac@gmail.com", d.Config.Email);
        // tài khoản phần mềm khác không thấy đăng nhập này
        s.Auth.CreateUser("nv1", "Nhân viên", "matkhau2", Roles.Staff);
        s.Auth.Login("nv1", "matkhau2");
        Assert.False(d.Config.Linked); Assert.False(l.Config.Linked);
    }

    [Fact]
    public async Task Backup_to_personal_drive_and_restore_on_a_new_machine_without_sync_code()
    {
        var g = new FakeGoogle();
        var now = new DateTime(2026, 11, 24, 9, 0, 0);
        var m1 = Machine("may-chinh");
        m1.Modules.WriteSheets("ton-kho-an-an", new Dictionary<string, JsonArray?> { ["S"] = Rows("a", "b") });
        var d1 = Svc(m1, g, () => now);

        Assert.Null(d1.WhyNotMain());                                       // máy đơn giữ dữ liệu chính
        await Assert.ThrowsAsync<BusinessException>(() => d1.BackupNowAsync()); // chưa đăng nhập Gmail
        Assert.Equal("nguoidung@gmail.com", await SignIn(d1));
        Assert.True(d1.Config.Linked);

        var r = await d1.BackupNowAsync();
        Assert.Equal("KhoLanh_2026-11-24_0900.db.gz", r.Name);
        var root = g.Folder("root", DriveCaNhanService.Root)!;
        var folder = g.Folder(root.Id, d1.Machine)!;
        var file = Assert.Single(g.In(folder.Id));
        using (var z = new GZipStream(new MemoryStream(file.Data), CompressionMode.Decompress))
        using (var ms = new MemoryStream()) { z.CopyTo(ms); Assert.StartsWith("SQLite format 3", System.Text.Encoding.ASCII.GetString(ms.ToArray(), 0, 15)); }
        Assert.Equal(now, d1.Config.Last);

        // tự sao lưu: mỗi ngày → chưa đến hạn; 1 ngày sau → đến hạn
        Assert.Null(await d1.TickAsync());
        m1.Modules.WriteSheets("ton-kho-an-an", new Dictionary<string, JsonArray?> { ["S"] = Rows("a", "b", "c") });
        now = now.AddDays(1);
        Assert.NotNull(await d1.TickAsync());
        Assert.Equal(2, g.In(folder.Id).Count());

        // sửa nhầm rồi khôi phục bản hôm trước trên CHÍNH máy này
        m1.Modules.WriteSheets("ton-kho-an-an", new Dictionary<string, JsonArray?> { ["S"] = Rows("xoa-nham") });
        var list = await d1.ListAsync();
        Assert.Equal(new[] { "KhoLanh_2026-11-25_0900.db.gz", "KhoLanh_2026-11-24_0900.db.gz" }, list.Select(i => i.Name));
        await d1.RestoreAsync(list[1]);
        Assert.Equal("a,b", Names(m1));
        Assert.Contains(m1.Backup.ListAutoBackups(), f => f.Name.StartsWith("truoc-khoi-phuc_"));
        Assert.True(d1.Config.Linked);                                      // vẫn đăng nhập Gmail sau khi khôi phục

        // máy chính hỏng: máy MỚI chỉ cần đăng nhập đúng Gmail (không cần mã đồng bộ) là thấy và khôi phục được
        var m2 = Machine("may-moi");
        var d2 = Svc(m2, g, () => now);
        await SignIn(d2);
        var seen = await d2.ListAsync();
        Assert.All(seen, i => Assert.Equal(d1.Machine, i.Machine));
        await d2.RestoreAsync(seen[0]);
        Assert.Equal("a,b,c", Names(m2));
    }

    [Fact]
    public void Keep_today_one_per_day_for_14_days_and_one_per_month_for_a_year()
    {
        var now = new DateTime(2026, 11, 24, 18, 0, 0);
        var files = new List<(string, DateTime)>();
        for (var h = 0; h < 400 * 24; h += 6) { var at = now.AddHours(-h); files.Add((DriveCaNhanService.FileName(at), at)); }
        var keep = DriveCaNhanService.KeepSet(files, now);
        Assert.Contains(DriveCaNhanService.FileName(now), keep);
        Assert.Contains(DriveCaNhanService.FileName(now.AddHours(-12)), keep);                // hôm nay: giữ hết
        Assert.Contains(DriveCaNhanService.FileName(new DateTime(2026, 11, 23, 18, 0, 0)), keep);  // mới nhất mỗi ngày
        Assert.DoesNotContain(DriveCaNhanService.FileName(new DateTime(2026, 11, 23, 12, 0, 0)), keep);
        Assert.Contains(DriveCaNhanService.FileName(new DateTime(2026, 6, 30, 18, 0, 0)), keep);   // mới nhất mỗi tháng
        Assert.DoesNotContain(DriveCaNhanService.FileName(new DateTime(2026, 6, 15, 18, 0, 0)), keep);
        Assert.Equal(new DateTime(2026, 11, 24, 18, 0, 0), DriveCaNhanService.TimeOf("KhoLanh_2026-11-24_1800.db.gz"));
        Assert.Null(DriveCaNhanService.TimeOf("ghi-chu.txt"));
        Assert.InRange(keep.Count, 14 + 3, 14 + 3 + 12);
    }
}

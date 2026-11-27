using System.Net;
using System.Security.Cryptography;
using System.Text;
using KhoLanh.Core;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class UpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-upd-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public UpdateTests()
    {
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public readonly Dictionary<string, (HttpStatusCode code, byte[] body)> Routes = new();
        public readonly List<HttpRequestMessage> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Seen.Add(req);
            var key = req.RequestUri!.AbsolutePath;
            var (code, body) = Routes.TryGetValue(key, out var r) ? r : (HttpStatusCode.NotFound, Array.Empty<byte>());
            return Task.FromResult(new HttpResponseMessage(code) { Content = new ByteArrayContent(body) });
        }
    }

    private static string Release(string tag, long exeSize) => $$"""
        {"tag_name":"{{tag}}","draft":false,"body":"Ghi chú","published_at":"2026-11-22T13:00:00Z",
         "assets":[{"id":11,"name":"KhoLanh.exe","size":{{exeSize}}},{"id":12,"name":"KhoLanh.exe.sha256","size":64}]}
        """;

    private (UpdateService svc, FakeGitHub gh, byte[] exe) Make(string tag, bool goodSha = true)
    {
        var exe = Encoding.ASCII.GetBytes("MZ" + new string('x', 5000));
        var sha = Convert.ToHexString(SHA256.HashData(exe)).ToLowerInvariant();
        var gh = new FakeGitHub();
        gh.Routes["/repos/truongnhatdangkhoa-ai/kholanh-csharp/releases/latest"] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(Release(tag, exe.Length)));
        gh.Routes["/repos/truongnhatdangkhoa-ai/kholanh-csharp/releases/assets/11"] = (HttpStatusCode.OK, exe);
        gh.Routes["/repos/truongnhatdangkhoa-ai/kholanh-csharp/releases/assets/12"] = (HttpStatusCode.OK, Encoding.ASCII.GetBytes((goodSha ? sha : new string('0', 64)) + "  KhoLanh.exe\n"));
        return (new UpdateService(_s.Settings, _s.Session, gh), gh, exe);
    }

    [Fact]
    public async Task Newer_release_is_found_and_downloaded_with_sha_check()
    {
        var (svc, gh, exe) = Make("v99.0.0");
        svc.SaveConfig("", "ghp_test", true);
        var info = await svc.CheckAsync();
        Assert.NotNull(info);
        Assert.Equal(new Version(99, 0, 0), info!.Version);
        Assert.Equal("Bearer", gh.Seen[0].Headers.Authorization?.Scheme);
        var dest = Path.Combine(_dir, "KhoLanh.exe.moi");
        await svc.DownloadAsync(info, dest);
        Assert.Equal(exe, File.ReadAllBytes(dest));
    }

    [Fact]
    public async Task Same_or_older_version_means_no_update()
    {
        var (svc, _, _) = Make("v" + UpdateService.Current);
        Assert.Null(await svc.CheckAsync());
        (svc, _, _) = Make("v0.9.0");
        Assert.Null(await svc.CheckAsync());
    }

    [Fact]
    public async Task Bad_sha_is_rejected_and_file_removed()
    {
        var (svc, _, _) = Make("v99.0.0", goodSha: false);
        var info = (await svc.CheckAsync())!;
        var dest = Path.Combine(_dir, "KhoLanh.exe.moi");
        var e = await Assert.ThrowsAsync<BusinessException>(() => svc.DownloadAsync(info, dest));
        Assert.Contains("SHA-256", e.Message);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".tai"));
    }

    [Fact]
    public async Task Private_repo_without_token_explains_what_to_do()
    {
        var svc = new UpdateService(_s.Settings, _s.Session, new FakeGitHub());
        var e = await Assert.ThrowsAsync<BusinessException>(() => svc.CheckAsync());
        Assert.Contains("mã truy cập", e.Message);
    }

    [Fact]
    public void Repo_text_is_normalised()
    {
        Assert.Equal("a/b", UpdateService.NormalizeRepo("https://github.com/a/b.git"));
        Assert.Equal("a/b", UpdateService.NormalizeRepo(" a/b "));
        Assert.Equal("", UpdateService.NormalizeRepo("không hợp lệ"));
    }

    [Fact]
    public void Only_admin_changes_update_settings()
    {
        _s.Auth.CreateUser("nv1", "NV", "matkhau1", Models.Roles.Manager);
        _s.Auth.Login("nv1", "matkhau1");
        Assert.Throws<BusinessException>(() => _s.Updates.SaveConfig("", "x", true));
    }

    [Fact]
    public void Chi_tai_tiep_khi_GitHub_co_ban_moi_hon_ban_da_tai()
    {
        UpdateInfo I(string v) => new(Version.Parse(v), "v" + v, "", null, 1, 1, null);
        Assert.False(UpdateService.IsNewerThanStaged(null, Version.Parse("1.166.0")));                      // không có bản nào
        Assert.False(UpdateService.IsNewerThanStaged(I("1.166.0"), Version.Parse("1.166.0")));              // bằng bản đã tải
        Assert.False(UpdateService.IsNewerThanStaged(I("1.165.0"), Version.Parse("1.166.0")));              // cũ hơn
        Assert.True(UpdateService.IsNewerThanStaged(I("1.167.0"), Version.Parse("1.166.0")));               // 1.167 > 1.166 → tải tiếp
        Assert.True(UpdateService.IsNewerThanStaged(I("1.167.0"), null));                                   // chưa có bản tải sẵn
    }
}

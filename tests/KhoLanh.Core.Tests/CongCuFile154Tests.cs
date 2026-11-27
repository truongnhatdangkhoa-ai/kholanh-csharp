using KhoLanh.Core.CongCu;
using KhoLanh.Core.Data;
using KhoLanh.Core.LuuTru;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Xunit;

namespace KhoLanh.Core.Tests;

public class CongCuFile154Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "kl-cc-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private (CongCuFileService Cc, LuuTruService Lt, FakeGoogle G, SettingsService Set, Session S) Make(string user = "anan")
    {
        Directory.CreateDirectory(_tmp);
        var set = new SettingsService(new Database(Path.Combine(_tmp, "k.db")));
        var s = new Session { CurrentUser = new User { Username = user, Role = Roles.Admin } };
        var g = new FakeGoogle();
        var lt = new LuuTruService(Path.Combine(_tmp, "lt"), s, set, g) { ClientOverride = new("123.apps.googleusercontent.com", "sec") };
        return (new CongCuFileService(set, s, lt), lt, g, set, s);
    }

    private string NewFile(string name)
    {
        var p = Path.Combine(_tmp, "out", name); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, "nội dung"); return p;
    }

    [Fact]
    public async Task Ghi_nho_file_da_xuat_va_o_tich_nho_theo_tai_khoan()
    {
        var (cc, _, _, set, ses) = Make();
        Assert.False(cc.SyncOn);
        cc.SetSync(true); Assert.True(cc.SyncOn);
        var p = NewFile("a.pdf");
        var e = cc.Record("Scan tài liệu", p);
        cc.Record("Container 3D", NewFile("b.xlsx"));
        Assert.Single(cc.List("Scan tài liệu")); Assert.Equal(2, cc.List().Count);
        cc.Record("Scan tài liệu", p);                              // xuất lại cùng đường dẫn → không nhân đôi
        Assert.Single(cc.List("Scan tài liệu"));
        ses.CurrentUser = new User { Username = "khac", Role = Roles.Admin };
        Assert.False(cc.SyncOn); Assert.Empty(cc.List());           // tài khoản khác: ô tích + danh sách riêng
        ses.CurrentUser = new User { Username = "anan", Role = Roles.Admin };
        cc.Remove(e.Id, deleteFile: true);
        Assert.False(File.Exists(p)); Assert.Single(cc.List());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Chua_dang_nhap_Google_van_luu_tren_may_va_bao_ro()
    {
        var (cc, lt, g, _, _) = Make();
        var e = cc.Record("Scan tài liệu", NewFile("hd.pdf"));
        var r = await cc.SyncAsync(e.Id);
        Assert.Equal("local", r.Sync); Assert.Contains("Chưa đăng nhập Google", r.Note);
        Assert.Contains(lt.Store.Query(), x => x.Fn == "hd.pdf" && lt.Store.Ngans().First(n => n.Id == x.Ng).Name == "Công cụ");
        Assert.Equal(0, g.Uploads);
        Assert.Equal("local", cc.List()[0].Sync);
    }

    [Fact]
    public async Task Da_dang_nhap_Google_thi_day_len_Drive_va_thu_muc_chep_tung_file()
    {
        var (cc, lt, g, _, _) = Make();
        var c = lt.Config; c.Email = "nguoidung@gmail.com"; c.Refresh = Secret.Protect("RT"); lt.SaveConfig(c);
        var dir = Path.Combine(_tmp, "out", "tach"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "1.pdf"), "x"); File.WriteAllText(Path.Combine(dir, "2.pdf"), "y");
        var e = cc.Record("Xử lý PDF & Excel", dir);
        var r = await cc.SyncAsync(e.Id);
        Assert.Equal("ok", r.Sync); Assert.Contains("Google Drive", r.Note);
        Assert.True(g.Uploads >= 2);
        Assert.Equal(2, lt.Store.Query().Count(x => x.IsFile));
        // lần 2 không tạo thêm ngăn
        await cc.SyncAsync(cc.Record("Xử lý PDF & Excel", NewFile("c.xlsx")).Id);
        Assert.Single(lt.Store.Ngans().Where(n => n.Name == "Công cụ"));
    }

    [Fact]
    public async Task File_da_bi_xoa_bao_loi_tieng_Viet()
    {
        var (cc, _, _, _, _) = Make();
        var p = NewFile("m.pdf"); var e = cc.Record("Scan tài liệu", p); File.Delete(p);
        var r = await cc.SyncAsync(e.Id);
        Assert.Equal("err", r.Sync); Assert.Contains("đã bị xóa", r.Note);
    }
}

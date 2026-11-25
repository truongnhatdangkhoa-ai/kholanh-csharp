using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.PhoneSync;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.76 — tài khoản PC dùng chung trên điện thoại (cùng mật khẩu), quyền điện thoại riêng, tab PhanQuyen của app điện thoại mới.</summary>
public class PhoneAccountsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-pa-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private static readonly DateTime Now = new(2026, 11, 25, 9, 0, 0);

    public PhoneAccountsTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
        _s.PhoneAccounts.Now = () => Now;
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private PhoneAccounts.Entry E(string ten) => _s.PhoneAccounts.List().First(e => e.Ten == ten);

    [Fact]
    public void Ma_bam_giong_Apps_Script_sha256Hex()
    {
        // Code.gs: Utilities.computeDigest(SHA_256, text, UTF_8) → hex chữ thường
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", PhoneAccounts.Sha256Hex("abc"));
        Assert.Equal(PhoneAccounts.Sha256Hex("Kho lạnh 1"), PhoneAccounts.Sha256Hex("Kho lạnh 1"));
        Assert.Equal(64, PhoneAccounts.Sha256Hex("Mật khẩu có dấu").Length);
    }

    [Fact]
    public void Ghi_ma_khi_dang_nhap_tao_dat_va_doi_mat_khau()
    {
        Assert.Equal(PhoneAccounts.Sha256Hex("matkhau1"), E("admin").Hash);            // đăng nhập đúng
        _s.Auth.CreateUser("thang", "Thắng", "thang123", Roles.Staff);
        Assert.Equal(PhoneAccounts.Sha256Hex("thang123"), E("thang").Hash);           // tạo tài khoản
        var id = _s.Auth.ListUsers().First(u => u.Username == "thang").Id;
        _s.Auth.ResetPassword(id, "moi45678");
        Assert.Equal(PhoneAccounts.Sha256Hex("moi45678"), E("thang").Hash);           // Quản trị đặt lại
        _s.Auth.Logout(); _s.Auth.Login("thang", "moi45678");
        _s.Auth.ChangeOwnPassword("moi45678", "tudoi999");
        Assert.Equal(PhoneAccounts.Sha256Hex("tudoi999"), E("thang").Hash);           // tự đổi
        Assert.False(E("thang").Bat);                                                  // có mã nhưng chưa bật điện thoại
        Assert.Throws<BusinessException>(() => _s.PhoneAccounts.SaveRights(new[] { new PhoneAccounts.Entry { Ten = "thang", Bat = true } })); // chỉ Quản trị
    }

    [Fact]
    public void Tab_PhanQuyen_gop_tai_khoan_dien_thoai_va_bao_thieu_trung()
    {
        _s.Auth.CreateUser("thang", "Thắng", "thang123", Roles.Staff);
        _s.Auth.CreateUser("lan", "Lan", "lan12345", Roles.Manager);
        _s.Auth.CreateUser("khoa", "Khoa", "khoa1234", Roles.Staff);
        _s.PhoneAccounts.SaveRights(new[]
        {
            new PhoneAccounts.Entry { Ten = "thang", Bat = true, VaiTro = "xuat", Kho = new() { "M02", "M01", "XX" } },
            new PhoneAccounts.Entry { Ten = "admin", Bat = true, VaiTro = "quantri" },               // Quản trị chưa chọn kho = đủ kho
            new PhoneAccounts.Entry { Ten = "lan", Bat = false, VaiTro = "quanly" },
        });
        // "khoa" bật điện thoại nhưng mã bị xóa (giả như chưa đăng nhập PC từ bản này)
        var list = _s.PhoneAccounts.List(); var k = list.First(e => e.Ten == "khoa"); k.Bat = true; k.Hash = "";
        var users = _s.Auth.UsersForPhone();
        Assert.All(users, u => Assert.Equal("", u.PasswordHash));                                 // không lộ mã băm PBKDF2 của PC
        var cur = new List<IDictionary<string, JsonNode?>>
        {
            Row("hashcu", "lan", "Lan cũ", "quanly", "M02", "Bật"),                               // tài khoản PC đã có trên ĐT → PC quyết định
            Row(PhoneAccounts.Sha256Hex("dt999999"), "baove", "Bảo vệ", "xem", "M02", "Bật"),     // chỉ có trên ĐT → giữ nguyên
        };
        var plan = PhoneAccounts.BuildPlan(cur, users, list, Now);
        Dictionary<string, JsonNode?> R(string ten) => plan.Rows.First(r => r["ten"]!.ToString() == ten);
        Assert.Equal(new[] { "lan", "baove", "admin", "khoa", "thang" }, plan.Rows.Select(r => r["ten"]!.ToString()));
        Assert.Equal("Tắt", R("lan")["trangThai"]!.ToString());                                  // tắt điện thoại ở PC → khóa trên ĐT
        Assert.Equal(PhoneAccounts.Sha256Hex("dt999999"), R("baove")["tokenHash"]!.ToString());
        Assert.Equal("Bật", R("thang")["trangThai"]!.ToString());
        Assert.Equal(PhoneAccounts.Sha256Hex("thang123"), R("thang")["tokenHash"]!.ToString()); // đăng nhập ĐT bằng mật khẩu PC
        Assert.Equal("xuat", R("thang")["quyen"]!.ToString());
        Assert.Equal("M02,M01", R("thang")["kho"]!.ToString());                                   // mã kho lạ bị bỏ
        Assert.Equal("", R("thang")["maTruyCap"]!.ToString());                                    // không ghi mật khẩu dạng chữ
        Assert.Equal("Thắng", R("thang")["tenHienThi"]!.ToString());
        Assert.Equal(PhoneAccounts.Kho.Length, R("admin")["kho"]!.ToString().Split(',').Length);
        Assert.Contains(plan.Warnings, w => w.StartsWith("khoa:"));                               // bật mà chưa có mã
        Assert.DoesNotContain(plan.Rows, r => r["ten"]!.ToString() == "nguoila");

        // không đổi gì → bảng như cũ (không ghi lại, giữ mốc capNhat)
        var again = PhoneAccounts.BuildPlan(plan.Rows.Select(r => (IDictionary<string, JsonNode?>)r).ToList(), users, list, Now.AddHours(1));
        Assert.True(PhoneAccounts.SameTable(plan.Rows.Select(r => (IDictionary<string, JsonNode?>)r).ToList(), again.Rows.Select(r => (IDictionary<string, JsonNode?>)r).ToList()));

        // trùng mật khẩu giữa 2 dòng đang bật → báo (app ĐT chỉ nhận người đầu)
        var dup = new List<IDictionary<string, JsonNode?>> { Row(PhoneAccounts.Sha256Hex("thang123"), "baove", "Bảo vệ", "xem", "M02", "Bật") };
        Assert.Contains(PhoneAccounts.BuildPlan(dup, users, list, Now).Warnings, w => w.StartsWith("Trùng mật khẩu") && w.Contains("thang") && w.Contains("baove"));
    }

    [Fact]
    public void Tai_khoan_PC_bi_khoa_thi_tat_tren_dien_thoai()
    {
        _s.Auth.CreateUser("thang", "Thắng", "thang123", Roles.Staff);
        _s.PhoneAccounts.SaveRights(new[] { new PhoneAccounts.Entry { Ten = "thang", Bat = true, VaiTro = "xuat", Kho = new() { "M02" } } });
        var id = _s.Auth.ListUsers().First(u => u.Username == "thang").Id;
        _s.Auth.UpdateUser(id, "Thắng", Roles.Staff, false);
        var plan = PhoneAccounts.BuildPlan(new List<IDictionary<string, JsonNode?>>(), _s.Auth.UsersForPhone(), _s.PhoneAccounts.List(), Now);
        Assert.DoesNotContain(plan.Rows, r => r["ten"]!.ToString() == "thang");                  // chưa từng lên ĐT + đang khóa → không tạo dòng
        var had = new List<IDictionary<string, JsonNode?>> { Row(PhoneAccounts.Sha256Hex("thang123"), "thang", "Thắng", "xuat", "M02", "Bật") };
        Assert.Equal("Tắt", PhoneAccounts.BuildPlan(had, _s.Auth.UsersForPhone(), _s.PhoneAccounts.List(), Now).Rows.Single()["trangThai"]!.ToString());
    }

    private static IDictionary<string, JsonNode?> Row(string hash, string ten, string hienThi, string quyen, string kho, string tt) =>
        new Dictionary<string, JsonNode?>
        {
            ["tokenHash"] = hash, ["ten"] = ten, ["tenHienThi"] = hienThi, ["quyen"] = quyen, ["kho"] = kho, ["trangThai"] = tt,
            ["capNhat"] = "2026-11-01T08:00:00", ["maTruyCap"] = "", ["pcModules"] = "", ["email"] = "", ["_rowNumber"] = 2,
        };
}

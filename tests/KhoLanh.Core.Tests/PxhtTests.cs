using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.PhieuXuat;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.PhieuXuat.PxhtService;

namespace KhoLanh.Core.Tests;

public class PxhtTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-pxht-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public PxhtTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static JsonArray Cell(string json) => new() { new JsonObject { ["data"] = "__JSON__" + json } };
    private static JsonArray Arr(string json) => (JsonNode.Parse(json) as JsonArray)!;

    private void Seed()
    {
        _s.Modules.WriteSheets("ton-kho-an-an", new Dictionary<string, JsonArray?>
        {
            ["PhieuXuat"] = Cell("""
{"slips":[{"id":"PX-0118","createdAt":"2026-09-26T08:05:00","nguoiTao":"","ghiChu":"Nhập container 40ft","deducted":true,
                "lines":[{"maHang":"TPC-ABC123","tenHang":"Tôm sú","size":"16/20","hopDong":"HD01","qty":12},{"maHang":"XYZ9","tenHang":"Tôm thẻ","size":"31/40","qty":"3"}]},
                {"id":"PX-0120","createdAt":1790000000000,"phoneId":"p1","phoneMaPhieu":"DT-1","nguoiTao":"Lan","lines":[]}],"seq":121}
"""),
            ["ViTriBotPhieuDienThoai"] = Cell("""
{"slips":[{"id":"b1","maPhieu":"DT260924-0830","createdAt":"2026-09-24","nguoiTao":"Minh","lines":[{"ma":"BOT1","vt":"Z.01","qty":3}]}]}
"""),
        });
        _s.Modules.WriteSheets("ton-vi-tri-an-an", new Dictionary<string, JsonArray?>
        {
            ["ViTriPhieuDienThoai"] = Cell("""
{"slips":[{"id":"v1","source":"pc","maPhieu":"VT-01","loai":"thaythung","createdAt":"2026-09-25T10:30:00","lines":[{"maGoc":"TPC-AAA","ten":"Mực","lo":"L1","vt":"A.01","tl":10,"qty":4}]},
                {"id":"v2","source":"dt","maPhieu":"DT-VT-2","createdAt":"2026-09-25T11:00:00","lines":[]}],"seq":3}
"""),
        });
        _s.Modules.WriteSheets("ton-kho-gui", new Dictionary<string, JsonArray?>
        {
            ["PhieuXuat"] = Arr("""
[{"id":"21","maPhieu":"PX0021","ngayTao":"26/09/2026","nguon":"Điện thoại","nguoiTao":"Thắng","ghiChu":"Bình Dương"},{"id":"","maPhieu":""}]
"""),
            ["PhieuXuatItems"] = Arr("""
[{"phieuId":"21","maHang":"G1","soLuongXuat":"6","trongLuongTru":"60.5"},{"phieuId":"21","maHang":"G2","soLuongXuat":2,"trongLuongTru":20}]
"""),
        });
        _s.Modules.WriteSheets("nxt-bot-sot", new Dictionary<string, JsonArray?>
        {
            ["SoCai"] = Arr("""
[{"loai":"xuat","chungTu":"PX_07","ngay":"2026-09-25","maHang":"BS1","net":5,"nguoi":"Minh"},{"loai":"xuat","chungTu":"PX_07","ngay":"2026-09-25","maHang":"BS2","net":"2.5"},
                {"loai":"nhap","chungTu":"PN_01","ngay":"2026-09-20","maHang":"BS1","net":100},{"loai":"xuat","phieuId":"ph9","nguon":"dt","ngay":"2026-09-24","maHang":"BS3","net":1}]
"""),
        });
    }

    [Fact]
    public void Loads_all_sources_into_one_list()
    {
        Seed();
        var p = _s.Pxht;
        p.Load();
        Assert.Empty(p.Errors);
        Assert.Equal(8, p.All.Count);

        var a = p.All.Single(x => x.MaPhieu == "PX-0118");
        Assert.Equal("m1xh", a.Src);
        Assert.False(a.Phone);
        Assert.Equal("2026-09-26", a.DayIso);
        Assert.True(a.HasTime);
        Assert.Equal(15, a.Sum("qty"));
        Assert.Contains(("Trừ tồn kho", "Đã trừ"), a.Meta);
        Assert.Equal("ABC123", ItemText(a.Items[0], Cols["m1xh"][0]));   // bỏ tiền tố TPC-

        Assert.True(p.All.Single(x => x.MaPhieu == "PX-0120").Phone);
        var vt = p.All.Single(x => x.MaPhieu == "VT-01");
        Assert.False(vt.Phone);
        Assert.Contains(("Loại phiếu", "Phiếu thay thùng"), vt.Meta);
        Assert.True(p.All.Single(x => x.MaPhieu == "DT-VT-2").Phone);
        Assert.True(p.All.Single(x => x.Src == "m1vtb").Phone);

        var g = p.All.Single(x => x.Src == "m2");
        Assert.Equal("PX0021", g.MaPhieu);
        Assert.True(g.Phone);
        Assert.Equal("2026-09-26", g.DayIso);
        Assert.Equal(8, g.Sum("qty"));
        Assert.Equal(80.5, g.Sum("tlTru"));

        var so = p.All.Where(x => x.Src == "m3").ToList();
        Assert.Equal(2, so.Count);                                        // PX_07 gộp 2 dòng; phiếu điện thoại ph9 riêng; dòng nhập bỏ qua
        Assert.Equal(7.5, so.Single(x => x.MaPhieu == "PX_07").Sum("net"));
        var ph = so.Single(x => x.Phone);
        Assert.Equal("XK-20260924", ph.MaPhieu);
    }

    [Fact]
    public void Filters_and_sorts()
    {
        Seed();
        var p = _s.Pxht;
        p.Load();
        var all = PxhtService.Apply(p.All, new Filter());
        Assert.Equal(8, all.Count);
        Assert.True(all.Zip(all.Skip(1)).All(t => t.First.Ts >= t.Second.Ts));     // mới nhất trước

        Assert.Equal(new[] { "PX0021" }, PxhtService.Apply(p.All, new Filter { Query = "g2" }).Select(x => x.MaPhieu));   // tìm mã hàng bên trong phiếu
        Assert.All(PxhtService.Apply(p.All, new Filter { Nguon = "pc" }), x => Assert.False(x.Phone));
        Assert.All(PxhtService.Apply(p.All, new Filter { Src = "m3" }), x => Assert.Equal("m3", x.Src));
        var day = PxhtService.Apply(p.All, new Filter { From = "2026-09-25", To = "2026-09-25" });
        Assert.All(day, x => Assert.Equal("2026-09-25", x.DayIso));
        Assert.Equal(new[] { "Minh" }, PxhtService.Apply(p.All, new Filter { Nguoi = "Minh" }).Select(x => x.NguoiTao).Distinct());

        Assert.True(NaturalCmp("PX2", "PX10") < 0);
        Assert.True(NaturalCmp("PX010", "PX9") > 0);
    }

    [Fact]
    public void Empty_data_and_export()
    {
        var p = _s.Pxht;
        p.Load();
        Assert.Empty(p.All);
        Seed();
        p.Load();
        var path = Path.Combine(_dir, "px.xlsx");
        p.Export(PxhtService.Apply(p.All, new Filter()), path);
        using var wb = new XLWorkbook(path);
        Assert.Equal(9, wb.Worksheet("Phiếu xuất").LastRowUsed()!.RowNumber());
        Assert.Equal(1 + 9, wb.Worksheet("Dòng hàng").LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void Staff_without_permission_cannot_view()
    {
        _s.Auth.CreateUser("nhanvien", "Nhân viên", "matkhau1", Roles.Staff);
        var u = _s.Auth.ListUsers().Single(x => x.Username == "nhanvien");
        _s.Auth.SetPermissions(u.Id, Perm.Serialize(new Dictionary<string, string> { ["m11"] = Perm.None }));
        _s.Auth.Logout();
        _s.Auth.Login("nhanvien", "matkhau1");
        Assert.Throws<BusinessException>(() => _s.Pxht.Load());
    }
}

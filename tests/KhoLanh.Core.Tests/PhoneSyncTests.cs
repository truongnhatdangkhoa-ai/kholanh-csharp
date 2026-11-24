using System.Text.Json.Nodes;
using KhoLanh.Core.PhoneSync;
using KhoLanh.Core.SoKho;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.SoKho.SoKhoCore;

namespace KhoLanh.Core.Tests;

/// <summary>Đồng bộ điện thoại mọi module với Web App giả: đẩy đủ tab / cột / Meta như app cũ, nhận phiếu từng module, đánh dấu, không trừ 2 lần.</summary>
public class PhoneSyncTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-ps-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private readonly FakeWebApp _fake = new();
    public PhoneSyncTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static JsonArray A(params JsonObject[] o) => new(o.Select(x => (JsonNode)x).ToArray());
    private static JsonArray Cell(string json) => A(new JsonObject { ["data"] = "__JSON__" + json });

    private PhoneSyncService Seed()
    {
        var L = _s.Lan;
        L.Write("ton-kho-an-an", new Dictionary<string, JsonArray?>
        {
            ["LoadDataMeta"] = Cell("{\"headers\":[\"Item ID\",\"Mã hàng\",\"Tên hàng\",\"Tồn cuối\",\"\",\"Mã hàng\"],\"fileName\":\"NXT.xlsx\",\"importedAt\":1795000000000,\"dataDate\":\"23/11/2026\"}"),
            ["LoadData"] = A(new JsonObject { ["c0"] = "I1", ["c1"] = "TPC-VRHLA", ["c2"] = "Tôm", ["c3"] = 50, ["c4"] = "", ["c5"] = "x" }, new JsonObject { ["c0"] = "", ["c1"] = "VRHLB", ["c2"] = "Tôm B", ["c3"] = "1,234.5", ["c4"] = "", ["c5"] = "y" }),
            ["DacTinh"] = A(new JsonObject { ["Đặc Tính"] = "Semi", ["Nhóm đã gộp"] = "BÁN THÀNH PHẨM" }),
        });
        L.Write("ton-vi-tri-an-an", new Dictionary<string, JsonArray?>
        {
            ["ViTriMeta"] = Cell("{\"importedAt\":1795000000000,\"dataDate\":\"23/11/2026\"}"),
            ["ViTri"] = A(new JsonObject { ["Mã hàng"] = "VRHLA", ["Tên hàng"] = "Tôm (2kg) - Semi - HD-1", ["ĐVT"] = "Kiện", ["Lô (Batch)"] = "L1", ["Vị trí"] = "A.01", ["SL Tồn"] = 10, ["T.Lượng"] = 10 },
                          new JsonObject { ["Mã hàng"] = "VRHLA", ["Tên hàng"] = "Tôm", ["ĐVT"] = "Kiện", ["Lô (Batch)"] = "L1", ["Vị trí"] = "A.01", ["SL Tồn"] = 4, ["T.Lượng"] = 10 },
                          new JsonObject { ["Mã hàng"] = "", ["Vị trí"] = "Z" }),
            ["ViTriPhieuDienThoai"] = Cell("{\"slips\":[{\"id\":\"VT-0001\",\"source\":\"pc\",\"createdAt\":\"2026-11-23T01:00:00.000Z\",\"deducted\":true,\"loadAt\":1795000000000,\"lines\":[{\"key\":\"VRHLA|L1|A.01#0\",\"qty\":2,\"maGoc\":\"VRHLA\",\"vt\":\"A.01\"}]}],\"seq\":2}"),
        });
        L.Write("ton-kho-gui", new Dictionary<string, JsonArray?>
        {
            ["DuLieuChinh"] = A(new JsonObject { ["Mã hàng"] = "G1", ["Tên hàng"] = "TÔM, BLOCK, 0046", ["Số lô"] = "LO1", ["Size"] = "NL85", ["Phiếu nhập/Voucher"] = "PN1", ["Số kiện"] = 10, ["Trọng Lượng (KG)"] = 100 },
                                new JsonObject { ["Mã hàng"] = "G2", ["Tên hàng"] = "TÔM, CKQ", ["Số lô"] = "LOX", ["Size"] = "50", ["Phiếu nhập/Voucher"] = "PN2", ["Số kiện"] = 5, ["Trọng Lượng (KG)"] = 50 }),
            ["DacTinh_NhapTay"] = A(new JsonObject { ["Đặc tính"] = "46.EU-H" }),
            ["DacTinh_ChuyenLo"] = A(new JsonObject { ["Số lô"] = "lox", ["Đặc tính"] = "JP" }),
            ["DuLieuMeta"] = Cell("{\"importedAt\":1795000000000,\"dataDate\":\"22/11/2026\"}"),
        });
        var sk = _s.SoKho; sk.Load();
        sk.Commit(new[] { NormLine(new JsonObject { ["loai"] = "nhap", ["ngay"] = "2026-11-01", ["maHang"] = "BOT-A", ["size"] = "25kg", ["soLuong"] = 10, ["netDv"] = 25, ["viTri"] = "B1", ["chungTu"] = "NK-1", ["hopDong"] = "LSX1" }) }, Array.Empty<string>(), Array.Empty<SkLine>(), "nhap", "t");
        _s.Vtb.Load(); _s.Vtb.Push(VtbService.FromLedger(sk.Lines, "2026-11-05"), "2026-11-05");
        _s.Phone.Save("https://script.google.com/macros/s/X/exec", "TK");
        return new PhoneSyncService(_s, new KhoLanh.Core.Services.PhoneWebApp(_s.Settings, _fake)) { Now = () => new DateTime(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc) };
    }
    [Fact]
    public void Duong_dan_Web_App_cai_san_chi_can_nhap_ma()
    {
        var w = new KhoLanh.Core.Services.PhoneWebApp(_s.Settings);
        w.Clear();
        Assert.Equal(KhoLanh.Core.Services.PhoneWebApp.DefaultUrl, w.Url);
        Assert.Null(KhoLanh.Core.Services.PhoneWebApp.CheckUrl(w.Url));
        Assert.False(w.Enabled);                      // chưa có mã
        w.Save("", "PC-ma");                          // để trống đường dẫn = dùng link cài sẵn
        Assert.True(w.Enabled); Assert.False(w.CustomUrl);
        w.Save("https://script.google.com/macros/s/KHAC/exec", "PC-ma");
        Assert.True(w.CustomUrl); Assert.EndsWith("/KHAC/exec", w.Url);
    }
    private PhoneSyncService Svc() => Seed();
    private List<Dictionary<string, string>> Tab(string n) { var t = _fake.Tabs[n]; return t.Rows.Select(r => t.H.Select((h, i) => (h, v: r[i]?.ToString() ?? "")).ToDictionary(x => x.h, x => x.v)).ToList(); }

    [Fact]
    public async Task Day_moi_bang_dung_tab_cot_meta()
    {
        var ps = Svc();
        var r = await ps.PushAsync(new[] { "M01", "M01VT", "M01VTB", "M02", "M03", "M08", "LX", "C3D" });
        Assert.Empty(r.Errors);
        // M01: cột thật, tên trống / trùng được đánh số, bỏ "TPC-", khóa id: / r:
        Assert.Equal(new[] { "Item ID", "Mã hàng", "Tên hàng", "Tồn cuối", "Cột 5", "Mã hàng (2)", "_key", "_row" }, _fake.Tabs["TonKho_M01"].H);
        var m1 = Tab("TonKho_M01"); Assert.Equal(("VRHLA", "id:I1", "r:1"), (m1[0]["Mã hàng"], m1[0]["_key"], m1[1]["_key"]));
        // M01VT: khóa Mã|Lô|Vị trí#n, dòng không mã bị bỏ, Đã xuất (phiếu đang trừ), nhóm đặc tính đã gộp
        var vt = Tab("TonKho_M01_ViTri"); Assert.Equal(2, vt.Count);
        Assert.Equal(("VRHLA|L1|A.01#0", "VRHLA|L1|A.01#1", "2", "BÁN THÀNH PHẨM"), (vt[0]["_key"], vt[1]["_key"], vt[0]["Đã xuất"], vt[0]["Nhóm đặc tính"]));
        // M02: đặc tính suy ra (0046 → 46.EU-H; CKQ → lô đã chuyển tay JP)
        var m2 = Tab("TonKho_M02"); Assert.Equal(("46.EU-H", "JP"), (m2[0]["Đặc tính"], m2[1]["Đặc tính"]));
        // M03: mỗi lô 1 dòng, cột cũ M3COL, khóa lô mã hóa URI
        var m3 = Tab("TonKho_M03"); Assert.Single(m3);
        Assert.Equal(("BOT-A", "25kg", "250", "25", "B1|BOT-A|25kg||2026-11-01"), (m3[0]["Loại Hàng"], m3[0]["Mã Hàng"], m3[0]["Tổng (kg)"], m3[0]["Trọng lượng (kg)"], m3[0]["_key"]));
        Assert.Equal("LSX1", Tab("DanhMuc_M03_LSX")[0]["code"]);
        Assert.Equal(VtbService.PushColumns, _fake.Tabs["TonKho_M01_ViTriBot"].H);
        Assert.Equal(new[] { "seg", "code", "name" }, _fake.Tabs["M08_MaHoa"].H);
        Assert.Contains(Tab("M08_AnhXa"), x => x["pv"] == "SEG" && x["ma"] == "SP" && x["short"] == "1-4");
        // phiếu PC cho điện thoại xem
        Assert.Contains(Tab("PhieuXuat_PC"), x => x["module"] == "M01VT" && x["trangThai"] == PhoneSyncService.TtPc);
        var meta = Tab("Meta").ToDictionary(x => x["key"], x => x["value"]);
        Assert.Equal(("2", "23/11/2026", "2", "1"), (meta["m01Rows"], meta["m01DataDate"], meta["m01ViTriRows"], meta["m03Rows"]));
        Assert.Equal(meta["lastPushAt_M01"], meta["lastPushAt"]);   // đủ 8 bảng → mốc chung = mốc sớm nhất
        Assert.True(ps.Status["M01"].Changed);
        await ps.PushAsync(new[] { "M01" }); Assert.False(ps.Status["M01"].Changed);   // không đổi dữ liệu → không đổi
    }

    [Fact]
    public async Task Nhan_phieu_moi_module()
    {
        var ps = Svc();
        await ps.PushAsync(new[] { "M01", "M01VT", "M01VTB", "M02", "M03" });
        var m3key = Tab("TonKho_M03")[0]["_key"]; var vtbKey = Tab("TonKho_M01_ViTriBot")[0]["_key"];
        void P(string id, string mod, params (string Col, JsonNode? V)[] item)
        {
            _fake.Row("PhieuXuat", ("id", id), ("maPhieu", "DT-" + id), ("ngayTao", "2026-11-24T08:50:00.000Z"), ("nguoiTao", "kho1"), ("module", mod), ("lsxXuat", mod == "M03" ? "LSX9" : ""));
            var cells = new List<(string, JsonNode?)> { ("phieuId", id) }; cells.AddRange(item); _fake.Row("PhieuXuatItems", cells.ToArray());
        }
        P("p1", "M01", ("itemKey", "id:I1"), ("maHang", "VRHLA"), ("soLuongXuat", 5));
        P("p2", "M01VT", ("itemKey", "VRHLA|L1|A.01#1"), ("soLuongXuat", 1));
        P("p3", "", ("maHang", "G1"), ("soLo", "LO1"), ("size", "NL85"), ("phieuNhap", "PN1"), ("soLuongXuat", 4));   // module trống → M02
        P("p4", "M03", ("itemKey", m3key), ("soLuongXuat", 100));
        P("p5", "M01VTB", ("itemKey", vtbKey), ("soLuongXuat", 2));
        P("p6", "M03", ("itemKey", m3key), ("soLuongXuat", 999));   // vượt tồn → bỏ, để "Chưa xử lý"
        var (pull, push) = await ps.SyncAsync();
        Assert.Equal(5, pull.NewPhieu); Assert.Single(pull.Errors); Assert.Contains("vượt tồn", pull.Errors[0]);
        var px = Tab("PhieuXuat"); Assert.Equal(5, px.Count(x => x["trangThai"] == PhoneSyncService.DaXuLy)); Assert.Equal("", px[5]["trangThai"]);
        // M01: Tồn cuối 50 → 45, phiếu PX-0001 trong Xuất Hàng
        var ld = JsonNode.Parse(_s.Lan.Read("ton-kho-an-an", "LoadData"))!.AsArray(); Assert.Equal(45, ld[0]!["c3"]!.GetValue<double>());
        Assert.Contains("\\\"phoneId\\\":\\\"p1\\\"", _s.Lan.Read("ton-kho-an-an", "PhieuXuat"));
        // M01VT: dòng #1 4 → 3
        Assert.Equal(3, JsonNode.Parse(_s.Lan.Read("ton-vi-tri-an-an", "ViTri"))!.AsArray()[1]!["SL Tồn"]!.GetValue<double>());
        // M02: 10 kiện / 100 kg → 6 / 60, đánh dấu đã trừ, phiếu nguồn "Điện thoại"
        var du = JsonNode.Parse(_s.Lan.Read("ton-kho-gui", "DuLieuChinh"))!.AsArray()[0]!;
        Assert.Equal((6.0, 60.0, 1.0), (du["Số kiện"]!.GetValue<double>(), du["Trọng Lượng (KG)"]!.GetValue<double>(), du["Đã trừ theo phiếu xuất"]!.GetValue<double>()));
        Assert.Equal("Điện thoại", JsonNode.Parse(_s.Lan.Read("ton-kho-gui", "PhieuXuat"))!.AsArray()[0]!["nguon"]!.GetValue<string>());
        // M03: dòng xuất nguồn "dt" 100 kg = 4 bao, LSX của phiếu
        _s.SoKho.Load(); var x3 = _s.SoKho.Lines.Single(l => l.Nguon == "dt");
        Assert.Equal((4.0, 100.0, "LSX9", "p4"), (x3.SoLuong!.Value, x3.Net!.Value, x3.HopDong, x3.PhieuId));
        // M01VTB
        Assert.Equal(8, _s.Vtb.Rows[0].Sl);
        // tồn mới đã được đẩy lại cho điện thoại
        Assert.Equal("150", Tab("TonKho_M03")[0]["Tổng (kg)"]);
        Assert.Contains("M03", push.Tables);
        // đồng bộ lần nữa: không trừ 2 lần
        var (pull2, _) = await ps.SyncAsync();
        Assert.Equal(0, pull2.NewPhieu); Assert.Equal(45, JsonNode.Parse(_s.Lan.Read("ton-kho-an-an", "LoadData"))!.AsArray()[0]!["c3"]!.GetValue<double>());
    }

    // ------------------------------------------------------------------ 1.55: phần còn thiếu so với app PC cũ

    [Fact]
    public async Task Bao_cao_Excel_va_phan_loai_cho_dien_thoai()
    {
        var ps = Svc();
        await ps.PushAsync(new[] { "M01", "M02" });
        var b2 = Tab("BaoCao_M02");
        var th = b2.Where(r => r["key"] == "tonghop").OrderBy(r => int.Parse(r["part"])).ToList();
        Assert.NotEmpty(th);
        Assert.All(th, r => Assert.StartsWith("~", r["data"]));
        var bytes = Convert.FromBase64String(string.Concat(th.Select(r => r["data"][1..])));
        Assert.Equal((byte)'P', bytes[0]); Assert.Equal((byte)'K', bytes[1]);      // file xlsx (zip)
        Assert.Equal("22/11/2026", th[0]["reportDate"]);
        var pl = b2.Single(r => r["key"] == "phanloai");
        var json = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pl["data"][1..])))!;
        Assert.Equal("m02", json["nguon"]!.ToString());
        Assert.Equal(6, json["loai"]!.AsArray().Count);
        var nl = json["loai"]![0]!;
        Assert.Equal("Tôm Nguyên Liệu", nl["name"]!.ToString()); Assert.Equal(100, nl["net"]!.GetValue<double>());
        Assert.Equal("NL85", nl["sizes"]![0]!["label"]!.ToString()); Assert.Equal("Lô LO1", nl["sizes"]![0]!["items"]![0]!["sub"]!.ToString());
        Assert.NotNull(nl["cross"]);
        var meta = Tab("Meta").ToDictionary(r => r["key"], r => r["value"]);
        Assert.Equal("2026-11-24T09:00:00.000Z", meta["m02BaoCaoAt"]);
        Assert.True(meta.ContainsKey("m01BaoCaoLoi"));
    }

    [Fact]
    public async Task Khang_sinh_chi_gui_phan_moi()
    {
        var ps = Svc();
        string Rec(string lo, string dat) => new JsonObject { ["date"] = "2026-11-20", ["batch"] = lo, ["dat"] = dat, ["inputs"] = new JsonObject { ["enro"] = 0.5, ["aoz"] = "KPH" }, ["lan2"] = new JsonObject { ["date"] = "2026-11-22", ["dat"] = "Đạt" } }.ToJsonString();
        void Put(params (string K, string V)[] rows) => _s.Lan.Write("ket-qua-khang-sinh", new Dictionary<string, JsonArray?> { ["KhangSinh"] = A(rows.Select(r => new JsonObject { ["key"] = r.K, ["value"] = r.V }).ToArray()) });
        Put(("s:batch:2026-11-20:L1:1", Rec("L1", "Đạt")), ("s:batch:2026-11-20:L2:2", Rec("L2", "Không đạt")), ("s:ksinhRulesData", "{}"));
        var k1 = await ps.PushKhangSinhAsync();
        Assert.True(k1.Full); Assert.Equal(3, k1.Sent);
        var up = _fake.Payloads.Last(p => p["action"]!.ToString() == "pc.ksUpsert");
        Assert.True(up["full"]!.GetValue<bool>());
        var v = up["rows"]![0]![2]!.ToString();
        Assert.StartsWith("~", v);
        var o = JsonNode.Parse(v[1..])!;
        Assert.Equal("L1", o["b"]!.ToString()); Assert.Equal("0.5", o["i"]![0]!.ToString()); Assert.Equal("KPH", o["i"]![5]!.ToString()); Assert.Equal("Đạt", o["L"]![0]![1]!.ToString());
        // không đổi → không gửi
        var n = _fake.Calls.Count(c => c == "pc.ksUpsert");
        Assert.Equal(0, (await ps.PushKhangSinhAsync()).Sent);
        Assert.Equal(n, _fake.Calls.Count(c => c == "pc.ksUpsert"));
        // sửa 1 dòng + xóa 1 dòng → chỉ gửi phần đổi
        Put(("s:batch:2026-11-20:L1:1", Rec("L1", "Không đạt")), ("s:ksinhRulesData", "{}"));
        var k3 = await ps.PushKhangSinhAsync();
        Assert.False(k3.Full); Assert.Equal(1, k3.Sent); Assert.Equal(1, k3.Deleted);
        var up3 = _fake.Payloads.Last(p => p["action"]!.ToString() == "pc.ksUpsert");
        Assert.Equal("s:batch:2026-11-20:L2:2", up3["dels"]![0]![0]!.ToString());
        // Code.gs cũ → đẩy cả bảng kiểu (key, value)
        _fake.Legacy = true; _s.Settings.Set(PhoneSyncService.KeyKsState, "");
        var k4 = await ps.PushKhangSinhAsync();
        Assert.True(k4.Legacy); Assert.Equal(2, Tab("M08_KhangSinh").Count);
    }

    [Fact]
    public async Task Yeu_cau_lich_xuat_tu_dien_thoai_va_luu_tru_phieu()
    {
        var ps = Svc();
        var lx = _s.LichXuat;
        var old = lx.Add(new KhoLanh.Core.LichXuat.LichXuatService.Input { Ngay = "2026-12-02", Lsx = "26.12.050" });
        _fake.Tabs["LichXuat_YeuCau"] = (PhoneSyncService.LxYcHeaders.ToList(), new());
        _fake.Row("LichXuat_YeuCau", ("id", "Y1"), ("hanhDong", "add"), ("loai", "xuat"), ("ngay", "2026-12-01"), ("lsx", "26.12.001 + 26.12.002"), ("trangThaiLich", "plan"));
        _fake.Row("LichXuat_YeuCau", ("id", "Y2"), ("hanhDong", "add"), ("loai", "nhap"), ("ngay", "2026-12-03"), ("lsx", "Xe GC Thuận Phước"));
        _fake.Row("LichXuat_YeuCau", ("id", "Y3"), ("hanhDong", "move"), ("targetId", old.Id), ("ngay", "2026-12-05"), ("trangThaiLich", "wait"));
        _fake.Row("LichXuat_YeuCau", ("id", "Y4"), ("hanhDong", "add"), ("ngay", "1/12"), ("lsx", "26.12.009"));
        _fake.Row("LichXuat_YeuCau", ("id", "Y5"), ("hanhDong", "add"), ("ngay", "2026-12-09"), ("lsx", "26.12.010"), ("trangThai", "Đã xử lý"));
        var r = await ps.PullLichXuatRequestsAsync();
        Assert.Equal(3, r.Done); Assert.Equal(1, r.Err);
        Assert.Equal("Đã xử lý", _fake.Cell("LichXuat_YeuCau", 0, "trangThai"));
        Assert.StartsWith("Lỗi - Ngày không hợp lệ", _fake.Cell("LichXuat_YeuCau", 3, "trangThai"));
        lx.Load();
        var dec1 = lx.Rows.Where(x => x.Ngay == "2026-12-01").ToList();
        Assert.Equal(2, dec1.Count); Assert.Equal(dec1[0].Cont, dec1[1].Cont); Assert.NotEqual("", dec1[0].Cont);   // 2 LSX chung cont
        var moved = lx.Rows.Single(x => x.Id == old.Id);
        Assert.Equal("2026-12-05", moved.Ngay); Assert.Equal("wait", moved.TrangThai);
        Assert.Contains(_s.Modules.ReadSheet("lichxuat", "LichXuatCont").OfType<JsonObject>(), o => o["loai"]?.ToString() == "nhap" && o["lsx"]?.ToString() == "Xe GC Thuận Phước");
        Assert.True(_fake.Tabs.ContainsKey("LichXuat_Cont"));                       // đã đẩy lại bảng lịch trước khi đánh dấu
        // gọi lại: không xử lý lặp
        Assert.Equal(0, (await ps.PullLichXuatRequestsAsync()).Done);
        // lưu trữ phiếu cũ: 1 lần / ngày
        Assert.Equal(3, await ps.MaybeArchiveAsync());
        Assert.Null(await ps.MaybeArchiveAsync());
        Assert.Equal(90, _fake.Payloads.Last(p => p["action"]!.ToString() == "pc.archive")["days"]!.GetValue<int>());
    }
}

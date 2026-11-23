using System.Text.Json.Nodes;
using KhoLanh.Core.Services;
using KhoLanh.Core.TroLy;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.TroLy.TroLyChecks;

namespace KhoLanh.Core.Tests;

/// <summary>Trợ lý hệ thống: từng kiểm tra bằng dữ liệu giả có lỗi cố ý + một lần quét thật trên CSDL tạm.</summary>
public class TroLyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-troly-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private static readonly DateTime Today = new(2026, 11, 23, 10, 0, 0);

    public TroLyTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
        _s.TroLy.Now = () => Today;
        _s.TroLy.Version = "1.25.0";
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static JsonArray Arr(string json) => (JsonNode.Parse(json) as JsonArray)!;
    private static JsonArray Cell(string json) => new() { new JsonObject { ["data"] = "__JSON__" + json } };

    // ------------------------------------------------------------------ hàm thuần

    [Fact]
    public void Id_on_dinh_khi_so_luong_doi()
    {
        var a = Mk("csdl", "vang", "M", "Có 12 dòng", "x");
        var b = Mk("csdl", "vang", "M", "Có 345 dòng", "y");
        Assert.Equal(a.Id, b.Id);
        Assert.NotEqual(a.Id, Mk("csdl", "vang", "N", "Có 12 dòng", "x").Id);
    }

    [Fact]
    public void Ghi_phieu_bat_trung_so_mo_coi_rong_sl_sai()
    {
        var col = new PhieuCol(
            new() { new("p1", "PX1", "M01", "2026-11-20"), new("p2", "PX1", "M01", "2026-11-20"), new("p3", "PX3", "M01", "2026-11-20"), new("p4", "PX4", "M02", "2026-11-20", Huy: true) },
            new() { new("p1", "A", "5"), new("p2", "A", "0"), new("p9", "B", "2"), new("p1", "", "abc") });
        var f = CheckPhieu(col, "Phiếu");
        Assert.Contains(f, x => x.Title == "Trùng số phiếu" && x.Sev == "do");
        Assert.Contains(f, x => x.Title.StartsWith("Dòng hàng mồ côi") && x.Evidence[0].Contains("p9"));
        var empty = Assert.Single(f, x => x.Title == "Phiếu không có dòng hàng");
        Assert.Equal(new[] { "PX3" }, empty.Evidence); // p4 đã hủy → không báo
        Assert.Contains(f, x => x.Title.StartsWith("Số lượng xuất bất thường") && x.Detail.StartsWith("2 "));
        Assert.Contains(f, x => x.Title == "Dòng hàng thiếu mã hàng");
    }

    [Fact]
    public void Trung_khoa_va_trung_hoan_toan()
    {
        var rows = Arr("""
[{"Mã hàng":"A","Số lô":"L1","Size":"16/20","SL":1},{"Mã hàng":"A","Số lô":"L1","Size":"16/20","SL":1},
 {"Mã hàng":"B","Số lô":"L2","Size":"8/12","SL":1},{"Mã hàng":"B","Số lô":"L2","Size":"8/12","SL":9},{"Mã hàng":"C","Số lô":"L3","Size":"x","SL":1}]
""");
        var f = FindDupKeys("Tồn kho gửi", "DuLieuChinh", rows);
        Assert.Contains(f, x => x.Title == "Dòng trùng hoàn toàn" && x.Detail.StartsWith("1 nhóm"));
        Assert.Contains(f, x => x.Title.StartsWith("Nghi trùng khóa (Mã hàng + Số lô + Size)"));
        // mã + size lặp (không có lô / số phiếu) là bình thường ở bảng tồn
        Assert.Empty(FindDupKeys("X", "Y", Arr("""[{"Mã hàng":"A","Size":"1","Vị trí":"Z1","SL":1},{"Mã hàng":"A","Size":"1","Vị trí":"Z1","SL":2}]""")));
    }

    [Fact]
    public void Csdl_va_sao_luu()
    {
        Assert.Contains(CheckDb(new DbInfo { Integrity = "*** in database main ***" }), x => x.Title.StartsWith("integrity_check") && x.Sev == "do");
        Assert.Contains(CheckDb(new DbInfo { FileBytes = (long)(900 * MB) }), x => x.Title == "File dữ liệu lớn");
        Assert.Single(CheckDb(new DbInfo()), x => x.Sev == "xanh");

        Assert.Contains(CheckBackup(new(), 50, null, Today), x => x.Title == "Chưa có bản sao lưu");
        var old = new List<BackupFile> { new("tu-dong_2026-11-10.db", Today.AddDays(-13), 1000) };
        Assert.Contains(CheckBackup(old, 50, null, Today), x => x.Title == "Bản sao lưu cũ" && x.Sev == "do");
        var fresh = new List<BackupFile> { new("tu-dong_2026-11-23.db", Today.AddHours(-2), 1000) };
        Assert.Contains(CheckBackup(fresh, 1000, new RestoreProbe(true, "", 5, 100), Today), x => x.Title.StartsWith("Bản sao lưu ít dòng"));
        Assert.Contains(CheckBackup(fresh, 1000, new RestoreProbe(false, "file is not a database", 0, 0), Today), x => x.Title == "Thử phục hồi THẤT BẠI");
        Assert.Contains(CheckBackup(fresh, 1000, new RestoreProbe(true, "", 5, 990), Today), x => x.Title == "Thử phục hồi đạt");
    }

    [Fact]
    public void Dong_bo_lan_va_file_tam()
    {
        var f = CheckSync(new SyncInfo { LanEnabled = true, LanHealth = 2, Pending = 3, PendingAgeMin = 120, TmpFiles = { ("a.tmp", 30), ("b.tmp", 1) } });
        Assert.Contains(f, x => x.Title == "Không nối được máy chủ LAN");
        Assert.Contains(f, x => x.Title.StartsWith("Thay đổi chờ gửi lên"));
        Assert.Single(f, x => x.Title == "File tạm dở dang").Evidence.Single().StartsWith("a.tmp");
        Assert.Single(CheckSync(new SyncInfo()), x => x.Sev == "xanh");
    }

    [Fact]
    public void Doc_log_loi_gom_theo_loi_bo_loi_nghiep_vu()
    {
        var log = """
2026-11-22 08:00:00 bản 1.24.0
System.NullReferenceException: Object reference not set to an instance of an object.
   at KhoLanh.App.Views.SoDoView.Load() in SoDoView.cs:line 10

2026-11-22 09:00:00 bản 1.24.0
System.NullReferenceException: Object reference not set to an instance of an object.
   at KhoLanh.App.Views.SoDoView.Load() in SoDoView.cs:line 10

2026-11-01 09:00:00 bản 1.20.0
System.IO.IOException: cũ quá
   at KhoLanh.Core.X.Y()

2026-11-23 07:00:00 bản 1.24.0 [Lỗi chưa bắt]
KhoLanh.Core.Services.BusinessException: Không có quyền
""";
        var e = ParseErrorLog(log, Today);
        var one = Assert.Single(e);
        Assert.Equal(2, one.Count);
        Assert.Equal("SoDoView", one.Mod);
        Assert.Equal("NullReferenceException", one.Type);
        var f = Assert.Single(CheckErrors(e));
        Assert.StartsWith("Lỗi phần mềm: Object reference", f.Title);
    }

    [Fact]
    public void So_kho_ton_am_xuat_truoc_nhap_ngay_la_va_dot_bien()
    {
        var L = new List<TroLyNghiepVu.SoKhoLine>
        {
            new("tondau", "2026-10-01", "B1", "", "", 10, 100, ""),
            new("nhap", "2026-09-01", "B1", "", "", 999, 0, ""),          // trước mốc chốt → bỏ qua
            new("xuat", "2026-10-05", "B1", "", "", 15, 0, "CT1"),         // tồn âm 5
            new("xuat", "2026-10-02", "X9", "", "", 1, 0, ""),             // chưa từng nhập
            new("nhap", "2026-10-10", "C1", "", "", 100, 0, ""),
            new("xuat", "2026-10-03", "C1", "", "", 1, 0, ""),             // xuất trước nhập (cũng âm)
            new("nhap", "2030-01-01", "D1", "", "", 1, 0, "CT9"),          // ngày vô lý
        };
        for (var i = 0; i < 6; i++) L.Add(new("xuat", "2026-10-1" + i, "C1", "", "", 2, 0, ""));
        L.Add(new("xuat", "2026-10-20", "C1", "", "", 50, 0, ""));       // đột biến (~2 bình thường)
        var f = TroLyNghiepVu.CheckSoKho("Sổ kho", L, Today);
        Assert.Contains(f, x => x.Title.StartsWith("Tồn âm") && x.Evidence.Any(e => e.StartsWith("B1")));
        Assert.Contains(f, x => x.Title.StartsWith("Xuất mã hàng chưa từng nhập") && x.Evidence[0].StartsWith("X9"));
        Assert.Contains(f, x => x.Title.StartsWith("Ngày xuất sớm hơn ngày nhập") && x.Evidence[0].StartsWith("C1"));
        Assert.Contains(f, x => x.Title.StartsWith("Ngày bất thường"));
        Assert.Contains(f, x => x.Title.StartsWith("Số lượng xuất lớn gấp") && x.Evidence[0].Contains("xuất 50"));
    }

    [Fact]
    public void Phieu_luu_hai_lan_va_ngay_la()
    {
        var col = new PhieuCol(
            new() { new("a", "VT-1", "M01VT", "2026-11-20", Deducted: true), new("b", "VT-2", "M01VT", "2026-11-20", Deducted: true), new("c", "VT-3", "M01VT", "2026-11-20", Deducted: false), new("d", "VT-4", "M01VT", "2027-05-01") },
            new() { new("a", "M1", "4", "A.01"), new("b", "M1", "4", "A.01"), new("c", "M1", "4", "A.01"), new("d", "M2", "1") });
        var f = TroLyNghiepVu.CheckPhieuNghiepVu(col, Today);
        var dup = Assert.Single(f, x => x.Title.StartsWith("Phiếu xuất giống hệt nhau"));
        Assert.Equal("VT-1 = VT-2 (M01VT, 20/11/2026)", dup.Evidence.Single()); // VT-3 chưa trừ tồn → không tính
        Assert.Contains(f, x => x.Title.StartsWith("Phiếu có ngày bất thường") && x.Evidence[0].StartsWith("VT-4"));
    }

    [Fact]
    public void Kiem_tra_cheo_an_an_vi_tri()
    {
        const string A = "AAAA1111111111111111A", B = "BBBB2222222222222222B", C = "CCCC3333333333333333C";
        var an = new TroLyNghiepVu.TonTable("Tồn kho An An", "2026-11-22", new() { new(A, "a", 10, 0), new(B, "b", 5, 0) });
        var vt = new TroLyNghiepVu.TonTable("Tồn kho theo vị trí", "2026-11-22", new() { new(A, "a", 6, 0, "TG1.A.01"), new(A, "a", 3, 0, "TG1.A.02"), new(C, "c", 2, 0), new(C, "c", -1, 0, "Z") });
        var f = TroLyNghiepVu.CheckCheo(an, vt, null);
        Assert.Contains(f, x => x.Title.StartsWith("Tồn âm trong bảng tồn") && x.Mod == "Tồn kho theo vị trí");
        Assert.Contains(f, x => x.Title.StartsWith("Mã có ở Tồn theo vị trí nhưng không có ở Tồn An An") && x.Evidence[0].StartsWith(C));
        Assert.Contains(f, x => x.Title.StartsWith("Mã còn tồn ở An An nhưng không có vị trí") && x.Evidence[0].StartsWith(B));
        Assert.Contains(f, x => x.Title.StartsWith("Tổng số lượng theo mã lệch") && x.Evidence[0].Contains("lệch -1"));
        // khác ngày dữ liệu → không so (chỉ ghi chú xanh)
        var g = TroLyNghiepVu.CheckCheo(an with { DataDate = "2026-11-20" }, vt with { Rows = new() { new(A, "a", 1, 0) } }, null);
        Assert.Single(g, x => x.Id == "cheo-khac-ngay");
        Assert.DoesNotContain(g, x => x.Sev != "xanh");
    }

    [Fact]
    public void Ma_hang_va_ten_hang()
    {
        // mã 21 ký tự = 4+1+1+1+1+2+3+4+2+2
        const string Good = "TOMS" + "A" + "B" + "C" + "D" + "KH" + "MB1" + "1620" + "TL" + "QC";
        const string NotListed = "TOMS" + "A" + "B" + "C" + "D" + "KH" + "MB1" + "3140" + "TL" + "QC";
        const string BadSeg = "MUCX" + "A" + "B" + "C" + "D" + "KH" + "MB1" + "1620" + "TL" + "QC";
        const string Typo = "TOMS" + "A" + "B" + "C" + "D" + "KH" + "MB1" + "1620" + "TL" + "QD"; // khác Good 1 ký tự, đoạn 10 "QD" có khai báo
        var maHoa = new List<TroLyMaHang.SegCode> { new(1, "TOMS"), new(2, "A"), new(3, "B"), new(4, "C"), new(5, "D"), new(6, "KH"), new(7, "MB1"), new(8, "1620"), new(8, "3140"), new(9, "TL"), new(10, "QC"), new(10, "QD") };
        var master = new List<TroLyMaHang.Master> { new(Good, "Tôm sú PTO 16/20 IQF") };
        var rows = new List<TroLyMaHang.Row>
        {
            new("M01", "TPC-" + Good, "Tôm sú PTO 16/20"),                          // thiếu chữ
            new("M01VT", Good, "Tôm sú PTO 16/20 IQF (5kg) - MKS - HD01"),              // đã bỏ phần Đặc tính / HĐ → khớp, nhưng ghép (5kg)
            new("M01", NotListed, "Tôm"), new("M01", Typo, "Tôm"), new("M01", BadSeg, "Mực"),
            new("M01", "ABC 123", "Gì đó"),                                         // sai độ dài + ký tự lạ
            new("M02", "G123456789012345678", "TÃ´m vá»¡ font"),                   // lỗi font (không áp luật 21 ký tự)
            new("M02", "G2", "Tôm  thẻ"), new("M02", "G3", "Cá"), new("M02", "G3", "Mực ống"),
        };
        var f = TroLyMaHang.CheckMaTen(rows, master, maHoa);
        Assert.Contains(f, x => x.Title.StartsWith("Mã hàng sai độ dài") && x.Evidence.Any(e => e.Contains("ABC123")));
        Assert.Contains(f, x => x.Title.StartsWith("Mã hàng chứa ký tự lạ"));
        Assert.Contains(f, x => x.Title.StartsWith("Mã có đoạn chưa khai báo") && x.Evidence[0].Contains("«MUCX»"));
        var nl = Assert.Single(f, x => x.Title.StartsWith("Mã hợp lệ nhưng chưa có trong Danh sách mã hàng"));
        Assert.Contains(nl.Evidence, e => e.Contains(Typo) && e.Contains("gần giống " + Good));
        Assert.Contains(nl.Fixes!, x => x.Ma == NotListed && x.Near == "");
        Assert.Contains(f, x => x.Title.StartsWith("Tên hàng bị THIẾU chữ") && x.Sev == "do");
        Assert.Contains(f, x => x.Title.StartsWith("Tên hàng bị GHÉP THÊM chữ") && x.Evidence[0].Contains("«(5kg)» đứng sau"));
        var font = Assert.Single(f, x => x.Title.StartsWith("Tên hàng lỗi font"));
        Assert.True(font.Nguon);
        Assert.Contains(f, x => x.Title.StartsWith("Tên hàng thừa khoảng trắng"));
        Assert.Contains(f, x => x.Title.StartsWith("Một mã có nhiều tên khác nhau") && x.Evidence[0].Contains("G3"));
        Assert.Equal("sai", TroLyMaHang.CmpName("Tom su PTO 16/21 IQF", "Tôm sú PTO 16/20 IQF"));
        Assert.Equal("khac", TroLyMaHang.CmpName("Mực ống làm sạch", "Tôm sú PTO 16/20 IQF"));
    }

    [Fact]
    public void Hang_doi_vong_doi_va_tai_phat()
    {
        var f1 = Mk("csdl", "vang", "M", "Lỗi A", "x");
        var (items, ch) = TroLyQueue.Update(new(), new[] { f1 }, Today, false, true, "1.0");
        Assert.Equal(1, ch.Moi); Assert.Equal("moi", items[f1.Id].St);
        (items, _) = TroLyQueue.Mark(items, new[] { f1.Id }, "daSua", Today, "1.0");
        (items, ch) = TroLyQueue.Update(items, Array.Empty<Finding>(), Today.AddHours(1), false, true, "1.1");
        Assert.Equal("dong", items[f1.Id].St); Assert.True(items[f1.Id].Verified); Assert.Equal(1, ch.Dong);
        (items, ch) = TroLyQueue.Update(items, new[] { f1 }, Today.AddHours(2), false, true, "1.1");
        Assert.Equal("taiPhat", items[f1.Id].St); Assert.Equal(1, items[f1.Id].Recur); Assert.Equal(1, ch.TaiPhat);
        // bước quét lỗi → không tự đóng
        var (i2, _) = TroLyQueue.Update(items, Array.Empty<Finding>(), Today.AddHours(3), false, false, "1.1");
        Assert.Equal("taiPhat", i2[f1.Id].St);
        // lỗi từ nguồn: lần đầu Mới, lần sau chuyển Lỗi từ nguồn và không tính tái phát
        var src = Mk("ma-hang", "do", "Tên hàng", "Lỗi font", "x"); src.Nguon = true;
        var (i3, _) = TroLyQueue.Update(new(), new[] { src }, Today, false, true, "1");
        (i3, _) = TroLyQueue.Update(i3, new[] { src }, Today.AddHours(1), false, true, "1");
        Assert.Equal("loiNguon", i3[src.Id].St); Assert.Equal(0, i3[src.Id].Recur);
        // xóa lịch sử: chỉ mục đã đóng / bỏ qua
        Assert.Equal(0, TroLyQueue.Purge(items, new[] { f1.Id }).N);
    }

    [Fact]
    public void Bao_cao_an_email_ten()
    {
        var f = Mk("loi-app", "do", "SoDoView", "Lỗi", "Người tạo: Nguyễn Văn A · gửi a.b@x.com", new[] { "tk=lan" });
        f.Recur = 2;
        var t = TroLyReport.Build(new[] { f }, "1.25.0", Today);
        Assert.Contains("[email]", t); Assert.DoesNotContain("a.b@x.com", t); Assert.DoesNotContain("Nguyễn Văn A", t);
        Assert.Contains("TÁI PHÁT 2 lần", t); Assert.Contains("File nghi ngờ:", t);
        Assert.Contains("a.b@x.com", TroLyReport.Build(new[] { f }, "1.25.0", Today, anDanh: false));
    }

    [Fact]
    public void Viec_can_chu_y_hom_nay()
    {
        var inp = new TroLyChuY.Input
        {
            Today = Today.Date,
            Lich =
            {
                new("1", "2026-11-23", "xuat", "", "C1", "", "LSX 26.11.005 - 23/11"),
                new("2", "2026-11-24", "xuat", "", "C2", "", "26.11.007"),
                new("3", "2026-11-24", "xuat", "ok", "C3", "", "26.11.009"),
            },
            ListLsx = { "26.11.7" },
            Phieu = { new("xuat_khau", "26.11.007", "2026-11-24", "TGHU123") },
            VtSlips = { new("v1", "VT-01", "", Today.AddDays(-1), true), new("v2", "VT-02", "thaythung", Today, true), new("v3", "VT-03", "", Today.AddDays(-20), true) },
            LanEnabled = true, LanHealth = 1, LanPending = 2, LanPendingAgeMin = 45, LanConflicts = 1,
            HasBackupToday = false,
            Data = { new("Tồn kho An An", "2026-11-15", 100, "m1"), new("Tồn kho gửi", "2026-11-22", 10, "m2") },
            ErrorsToday = 1,
        };
        var v = TroLyChuY.Build(inp);
        var today = Assert.Single(v, x => x.Id == "lich-hom-nay");
        Assert.Equal("LSX 26.11.005 thiếu List, Phiếu xuất kho, Số cont", today.Detail);
        Assert.DoesNotContain(v, x => x.Id == "lich-ngay-mai"); // 26.11.007 có List (26.11.7 = 26.11.007), phiếu, cont; 26.11.009 đã xong
        Assert.Contains(v, x => x.Id == "vt-chua-loai" && x.Title.StartsWith("1 phiếu") && x.Detail.Contains("VT-01"));
        Assert.Contains(v, x => x.Id == "lan-so-cho");
        Assert.Contains(v, x => x.Id == "lan-xung-dot");
        Assert.Contains(v, x => x.Id == "sao-luu-hom-nay");
        Assert.Contains(v, x => x.Id == "du-lieu-cu|Tồn kho An An" && x.Sev == "vang");
        Assert.DoesNotContain(v, x => x.Id == "du-lieu-cu|Tồn kho gửi");
        Assert.Contains(v, x => x.Id == "loi-hom-nay" && x.Go == "troly");
        Assert.Equal("do", v[0].Sev);
        Assert.Equal(new[] { "26.11.005", "ISHI" }, TroLyChuY.LsxCodes("LSX: 26.11.005 - 23/11 + ISHI + OK"));
    }

    // ------------------------------------------------------------------ quét thật trên CSDL tạm

    private void Seed()
    {
        const string Good = "TOMSABCDKHMB11620TLQC";
        _s.Modules.WriteSheets("ton-vi-tri-an-an", new Dictionary<string, JsonArray?>
        {
            ["ViTri"] = Arr($$"""[{"Mã hàng":"{{Good}}","Tên hàng":"Tôm sú - MKS - HD","ĐVT":"Thùng","Lô (Batch)":"L1","Vị trí":"TG1.A.01.1.1","SL Tồn":-2,"T.Lượng":10}]"""),
            ["ViTriMeta"] = Cell("""{"dataDate":"2026-11-23","fileName":"vt.xlsx"}"""),
            ["ViTriPhieuDienThoai"] = Cell($$"""
{"slips":[{"id":"v1","source":"pc","maPhieu":"VT-01","loai":"","createdAt":"2026-11-22T03:00:00.000Z","deducted":true,"lines":[{"maGoc":"{{Good}}","vt":"TG1.A.01.1.1","qty":3}]},
          {"id":"v2","source":"pc","maPhieu":"VT-02","loai":"","createdAt":"2026-11-22T03:00:00.000Z","deducted":true,"lines":[{"maGoc":"{{Good}}","vt":"TG1.A.01.1.1","qty":3}]}],"seq":3}
"""),
        });
        _s.Modules.WriteSheets("ton-kho-gui", new Dictionary<string, JsonArray?>
        {
            ["PhieuXuat"] = Arr("""[{"id":"1","maPhieu":"PX1","ngayTao":"20/11/2026"},{"id":"2","maPhieu":"PX1","ngayTao":"20/11/2026"}]"""),
            ["PhieuXuatItems"] = Arr("""[{"phieuId":"1","maHang":"G1","soLuongXuat":2},{"phieuId":"7","maHang":"G2","soLuongXuat":1}]"""),
        });
        _s.Modules.WriteSheets("tong-hop", new Dictionary<string, JsonArray?>
        {
            ["MaDaTao"] = Arr("""[{"STT":1,"Mã":"ZZZZ","Ghi chú":"x","Đủ mã":0}]"""),
            ["MaHoa"] = new JsonArray(new[] { (1, "TOMS"), (2, "A"), (3, "B"), (4, "C"), (5, "D"), (6, "KH"), (7, "MB1"), (8, "1620"), (9, "TL"), (10, "QC") }
                .Select(x => (JsonNode)new JsonObject { ["Tổ hợp"] = x.Item1, ["Mã"] = x.Item2, ["Tên"] = x.Item2 }).ToArray()),
        });
    }

    [Fact]
    public void Quet_that_tren_csdl_tam()
    {
        Seed();
        Directory.CreateDirectory(Path.Combine(_dir, "logs"));
        File.WriteAllText(Path.Combine(_dir, "logs", "loi.log"), "2026-11-23 08:00:00 bản 1.24.0\nSystem.InvalidOperationException: hỏng\n   at KhoLanh.App.Views.GuiView.Load()\n\n");
        var r = _s.TroLy.Scan(deep: true);
        Assert.All(r.Steps, s => Assert.Equal("", s.Error));
        Assert.Contains(r.Findings, x => x.Title == "Trùng số phiếu");
        Assert.Contains(r.Findings, x => x.Title.StartsWith("Dòng hàng mồ côi") && x.Evidence.Single() == "phiếu m2|7 · G2");
        Assert.Contains(r.Findings, x => x.Title.StartsWith("Phiếu xuất giống hệt nhau"));
        Assert.Contains(r.Findings, x => x.Title.StartsWith("Tồn âm trong bảng tồn"));
        Assert.Contains(r.Findings, x => x.Title == "Chưa có bản sao lưu");
        Assert.Contains(r.Findings, x => x.Title.StartsWith("Lỗi phần mềm: hỏng") && x.Mod == "GuiView");
        Assert.Contains(r.Findings, x => x.Sev == "xanh" && x.Cat == "csdl");
        Assert.True(r.Do > 0);
        Assert.All(r.Findings.Where(x => x.Sev != "xanh"), x => Assert.Equal("moi", x.Q));
        Assert.True(File.Exists(_s.TroLy.StateFile));

        // mã chuẩn chưa có trong Danh sách mã hàng → thêm (chỉ thêm, không xóa dòng cũ)
        var nl = r.Findings.Single(x => x.Title.StartsWith("Mã hợp lệ nhưng chưa có"));
        var add = _s.TroLy.AddMissingCodes(nl.Id);
        Assert.Equal(1, add.Added);
        var ma = _s.Modules.ReadSheet("tong-hop", "MaDaTao");
        Assert.Equal(2, ma.Count);
        Assert.Equal("Tôm sú", ma[1]!["Ghi chú"]!.GetValue<string>()); // tên Vị trí đã bỏ phần Đặc tính / Hợp đồng

        // báo cáo → Đã gửi Claude; sao lưu + quét lại → cảnh báo sao lưu tự đóng
        var txt = _s.TroLy.Report(new[] { nl.Id, r.Findings.Single(x => x.Title == "Chưa có bản sao lưu").Id });
        Assert.Contains("Kho Lạnh C#", txt);
        Assert.Equal("daGui", _s.TroLy.Last!.Findings.Single(x => x.Id == nl.Id).Q);
        _s.Backup.DailyAutoBackup();
        var r2 = _s.TroLy.Scan(deep: true);
        Assert.Contains(r2.Findings, x => x.Title == "Thử phục hồi đạt");
        var q = _s.TroLy.Queue().ToDictionary(x => x.Id, x => x.Item);
        Assert.Equal("dong", q[r.Findings.Single(x => x.Title == "Chưa có bản sao lưu").Id].St);
        Assert.Equal("dong", q[nl.Id].St); // mã đã thêm → hết cảnh báo

        var chuY = _s.TroLy.ChuY();
        Assert.Contains(chuY, x => x.Id == "vt-chua-loai" && x.Title.StartsWith("2 phiếu"));
        Assert.Contains(chuY, x => x.Id == "loi-hom-nay");
    }

    [Fact]
    public void Chi_quan_tri_duoc_dung()
    {
        _s.Auth.CreateUser("nhanvien", "Nhân viên", "matkhau1", Models.Roles.Staff);
        _s.Auth.Logout();
        _s.Auth.Login("nhanvien", "matkhau1");
        Assert.Throws<BusinessException>(() => _s.TroLy.Scan(false));
        Assert.Throws<BusinessException>(() => _s.TroLy.ChuY());
    }
}

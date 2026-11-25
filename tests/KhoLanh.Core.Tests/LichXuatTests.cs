using System.Text.Json.Nodes;
using KhoLanh.Core.LichXuat;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.LichXuat.LichXuatCore;

namespace KhoLanh.Core.Tests;

/// <summary>Lịch xuất + Trang Tổng quan: lõi (nhóm cont, việc, nhập nhanh), email, dò Thành Phẩm Chính, List từ tồn kho, tạo phiếu.</summary>
public class LichXuatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-lx-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private static readonly DateTime Now = new(2026, 11, 23, 9, 0, 0);
    private LichXuatService Lx => _s.LichXuat;

    public LichXuatTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
        Lx.Now = () => Now;
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static Lich L(string id, string ngay, string lsx, string st = "plan", string cont = "", double kg = 0) =>
        new() { Id = id, Ngay = ngay, Lsx = lsx, TrangThai = st, Cont = cont, Kg = kg };

    // ------------------------------------------------------------------ quyền mục Email (1.75)

    private sealed class NoMail : IMailSource
    {
        public int Calls;
        public Task<List<LichXuatEmail.Msg>> FetchAsync(MailConfig cfg, CancellationToken ct = default) { Calls++; return Task.FromResult(new List<LichXuatEmail.Msg>()); }
    }

    [Fact]
    public async Task Muc_Email_chi_danh_cho_Quan_tri()
    {
        var mail = new NoMail(); Lx.Mail = mail;
        Assert.True(Lx.CanMail);                                   // Quản trị: thấy nút Email, cài đặt / duyệt / quét tay được
        await Lx.ScanEmailAsync();
        _s.Auth.CreateUser("quanly", "Quản lý", "matkhau1", KhoLanh.Core.Models.Roles.Manager);
        _s.Auth.CreateUser("nhanvien", "Nhân viên", "matkhau1", KhoLanh.Core.Models.Roles.Staff);
        foreach (var u in new[] { "quanly", "nhanvien" })
        {
            _s.Auth.Logout(); _s.Auth.Login(u, "matkhau1");
            Assert.False(Lx.CanMail);                              // Quản lý trở xuống: không có mục Email
            await Assert.ThrowsAsync<BusinessException>(() => Lx.ScanEmailAsync());
            Assert.Throws<BusinessException>(() => Lx.SaveMailCfg(Lx.MailCfg(), "abcd efgh ijkl mnop"));
            Assert.Throws<BusinessException>(() => Lx.SaveTrust("@anan.com.vn", true));
        }
        Assert.Equal(1, mail.Calls);                               // chỉ lượt quét của Quản trị chạm tới hộp thư
        _s.Auth.Logout(); _s.Auth.Login("quanly", "matkhau1");
        Assert.True(Lx.CanAutoMail);                               // máy đã khai báo Gmail vẫn tự quét nền khi Quản lý đăng nhập
        Assert.DoesNotContain(KhoLanh.Core.Models.Perm.Items, i => i.Key == "lxmail");   // bỏ ô quyền cũ khỏi Phân quyền
    }

    private sealed class SlowMail : IMailSource
    {
        public async Task<List<LichXuatEmail.Msg>> FetchAsync(MailConfig cfg, CancellationToken ct = default) { await Task.Delay(TimeSpan.FromMinutes(5), ct); return new(); }
    }
    private sealed class BadMail : IMailSource
    {
        public Task<List<LichXuatEmail.Msg>> FetchAsync(MailConfig cfg, CancellationToken ct = default) => throw new BusinessException("Gmail từ chối đăng nhập — thử");
    }

    [Fact]
    public async Task Quet_email_qua_lau_bao_loi_khong_treo_va_ghi_trang_thai()
    {
        // 1.77: trước đây hộp thư lớn làm nút quét đứng mãi ở "Đang đọc hộp thư…"
        Lx.Mail = new SlowMail(); Lx.ScanTimeout = TimeSpan.FromMilliseconds(200);
        var e = await Assert.ThrowsAsync<BusinessException>(() => Lx.ScanEmailAsync());
        Assert.Contains("Quá", e.Message);
        Assert.StartsWith("✖", Lx.MailStatus);
        Lx.Mail = new BadMail();
        await Assert.ThrowsAsync<BusinessException>(() => Lx.ScanEmailAsync());
        Assert.Contains("từ chối đăng nhập", Lx.MailStatus);
    }

    // ------------------------------------------------------------------ lõi

    [Fact]
    public void Home_gop_cont_va_tach_gan_day_du_kien()
    {
        var rows = new List<Lich>
        {
            L("a1", "2026-11-23", "LSX 26.11.001", "run", "c1", 10000), L("a2", "2026-11-23", "LSX 26.11.002", "plan", "c1", 5000),
            L("b", "2026-11-23", "LSX 26.11.003", "ok"), L("c", "2026-11-25", "LSX 26.11.004"), L("d", "2026-11-20", "LSX 26.11.005", "ok"),
            new() { Id = "n", Ngay = "2026-11-23", Loai = "nhap", Lsx = "xe GC" },
        };
        var h = LichXuatCore.BuildHome(rows, "2026-11-23", Now);
        Assert.Equal(2, h.Today.Count);
        var g = h.Today.Single(x => x.Ids.Count == 2);
        Assert.Equal("run", g.St); Assert.Equal(15000, g.Kg); Assert.Equal("ghép 2 LSX", g.Cont);
        Assert.Equal(new[] { "26.11.001", "26.11.002" }, g.Codes);
        Assert.Equal(new[] { "2026-11-23", "2026-11-23", "2026-11-20" }, h.Recent.Select(x => x.Ngay));
        Assert.Equal("2026-11-25", h.Upcoming.Single().Ngay);
        var (done, plan, _, _) = MonthCont(rows, "2026-11");
        Assert.Equal(2, done); Assert.Equal(2, plan);
    }

    [Fact]
    public void Viec_muc_canh_bao_va_gan_viec_cho()
    {
        var r = L("a", "2026-11-25", "LSX 26.11.010");
        ViecPush(r, "Dán băng keo"); ViecPush(r, "dán băng keo");
        Assert.Single(ViecOf(r));
        Assert.Equal("idle", ViecState(r, Now).Level);
        Assert.Equal("amber", ViecState(r, Now.AddDays(1)).Level);   // hạn = 24/11
        Assert.Equal("red", ViecState(r, Now.AddDays(2)).Level);
        var rows = new List<Lich> { r, L("b", "2026-11-28", "LSX 26.11.050 + 26.11.051") };
        var rest = AttachPend(rows, new() { new("p1", "26.11.051", "Chờ chứng thư", "x"), new("p2", "26.12.999", "Khác", "x") }, Now);
        Assert.Equal("p2", rest.Single().Id);
        Assert.Equal("Chờ chứng thư", ViecOf(rows[1]).Single().T);
    }

    [Fact]
    public void Nhap_nhanh_va_ngay()
    {
        var (rows, notes) = QuickParse("Cập nhật kế hoạch:\nNgày 24/11:\n1. Nhập 1 xe GC\n2. Xuất LSX 26.07.051 + 26.08.033 Mitsubishi\n3. Xuất LSX 26.10.054\nNgày 20/11: Xuất LSX 26.10.001", Now);
        Assert.Equal(4, rows.Count);
        Assert.Equal("A", rows[0].Grp); Assert.Equal("A", rows[1].Grp); Assert.Equal("", rows[2].Grp);
        Assert.Equal("LSX 26.07.051", rows[0].Lsx); Assert.Equal("2026-11-24", rows[0].Ngay); Assert.Equal("plan", rows[0].TrangThai);
        Assert.Equal("ok", rows[3].TrangThai);
        Assert.Contains(notes, n => n.Contains("NHẬP"));
        Assert.Equal("2027-01-05", ParseNgay("05/01", Now));
        Assert.Equal("2026-02-01", ParseNgay("1/2/26", Now));
        Assert.Equal("LSX 26.05.024 + 26.05.025", LichXuatService.NormLsxInput("lpf lsx 26.05.024, 26.05.025"));
    }

    [Fact]
    public void Lich_giu_cot_la_khi_ghi_lai()
    {
        var o = new JsonObject { ["id"] = "x", ["ngay"] = "2026-11-24T00:00:00", ["trangThai"] = "plan", ["lsx"] = "LSX 1", ["truongMoi"] = "giữ" };
        var r = Lich.From(o); r.Gio = "08:30";
        var j = r.ToJson();
        Assert.Equal("giữ", j["truongMoi"]!.GetValue<string>()); Assert.Equal("2026-11-24", j["ngay"]!.GetValue<string>()); Assert.Equal("08:30", j["gio"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ email

    private static LichXuatEmail.Msg M(string id, string th, DateTime d, string from, string subj, string text) => new(id, th, d, from, subj, text);

    [Fact]
    public void Email_nhan_dien_tao_doi_huy_xe()
    {
        var msgs = new[]
        {
            M("1", "t1", Now.AddDays(-2), "Sale <sale@an.vn>", "AN AN - LSX 26.11.020 - KEO DONG - ETD 28/11", "Gửi lịch"),
            M("2", "t1", Now.AddDays(-1), "Sale <sale@an.vn>", "Re: AN AN - LSX 26.11.020 - KEO DONG - ETD 28/11", "Dời lịch kéo đóng sang ngày 30/11 lúc 8h30 nhé.\n\nOn Mon, x wrote:\n> cũ"),
            M("3", "t1", Now.AddHours(-3), "Xe <xe@vt.vn>", "Re: AN AN - LSX 26.11.020 - KEO DONG - ETD 28/11", "Họ tên tài xế: Nguyễn A\nSố điện thoại: 0909\nSố xe: 51C-12345\nSố cont: TGHU 1234567\nSố seal: S1"),
            M("4", "t1", Now.AddHours(-2), "Sale <sale@an.vn>", "Re: AN AN - LSX 26.11.020 - KEO DONG - ETD 28/11", "Phí hạ cont trước ngày 01/12 là 5 USD"),
            M("5", "t2", Now.AddDays(-1), "A <a@b>", "Báo giá tháng 12", "LSX 26.11.099 loading"),
            M("6", "t3", Now.AddDays(-1), "Sale <sale@an.vn>", "LSX 26.11.030 - loading 25/11", "x"),
            M("7", "t3", Now, "Sale <sale@an.vn>", "Re: LSX 26.11.030 - loading 25/11", "Khách hủy lịch xuất hàng ngày 25/11"),
        };
        var rows = LichXuatEmail.BuildRows(msgs, null, null, 7, Now);
        Assert.Equal(new[] { "tao", "doi", "tao", "xe", "huy" }, rows.Select(r => r.HanhDong)); // theo giờ nhận
        Assert.Equal("2026-11-28", rows[0].Ngay); Assert.Equal("26.11.020", rows[0].Lsx);
        Assert.Equal("2026-11-30", rows[1].Ngay); Assert.Equal("08:30", rows[1].Gio);
        Assert.Equal("2026-11-25", rows[4].Ngay);
        // lưới an toàn: đổi lịch khi LSX chưa có lịch → giữ chờ duyệt
        Assert.Contains("chưa có lịch", LichXuatEmail.Guard(rows[1], new List<Lich>(), Now));
        Assert.Equal("", LichXuatEmail.Guard(rows[1], new List<Lich> { L("a", "2026-11-28", "LSX 26.11.020") }, Now));
        Assert.Equal("tao", LichXuatEmail.ThuDoc("", "LSX 26.11.020 - ETD 28/11", Now).HanhDong);
    }

    [Fact]
    public void Email_duyet_tu_dong_nguoi_tin_cay_va_hoan_tac()
    {
        Lx.SaveTrust("sale@an.vn", true);
        var cfg = new MailConfig { Auto = true, Days = 7 };
        var msgs = new List<LichXuatEmail.Msg>
        {
            M("1", "t1", Now.AddDays(-1), "Sale <sale@an.vn>", "LSX 26.11.020 - KEO DONG - ETD 28/11", "x"),
            M("2", "t2", Now.AddDays(-1), "Khác <k@x.vn>", "LSX 26.11.021 - KEO DONG - ETD 29/11", "x"),
        };
        var r = Lx.ProcessMessages(msgs, cfg);
        Assert.Equal(2, r.New); Assert.Equal(1, r.Auto); Assert.Equal(1, r.Waiting);
        Lx.Load();
        Assert.Equal("2026-11-28", Lx.Rows.Single().Ngay);
        // thư mới của cùng chuỗi: đổi lịch → tự áp (đúng 1 lịch chưa xuất)
        msgs.Add(M("3", "t1", Now, "Sale <sale@an.vn>", "Re: LSX 26.11.020 - KEO DONG - ETD 28/11", "Đổi ngày kéo đóng sang ngày 02/12"));
        r = Lx.ProcessMessages(msgs, cfg);
        Assert.Equal(1, r.New); Assert.Equal(1, r.Auto);
        Lx.Load(); Assert.Equal("2026-12-02", Lx.Rows.Single().Ngay);
        // duyệt tay thư người lạ
        var msg = Lx.Decide("2", true);
        Assert.Contains("Đã thêm 26.11.021", msg);
        Lx.Load(); Assert.Equal(2, Lx.Rows.Count);
        // hoàn tác thư đổi lịch → về 28/11; thư về chờ duyệt
        Lx.Undo("3");
        Lx.Load(); Assert.Equal("2026-11-28", Lx.Rows.Single(x => x.Lsx.Contains("020")).Ngay);
        Assert.Equal("", Lx.EmailRows().Single(x => x.Id == "3").TrangThai);
    }

    [Fact]
    public void Email_thong_tin_xe_ghi_so_cont_va_tu_tao_lich()
    {
        Lx.SaveTrust("xe@vt.vn", true);
        var xe = "Họ tên tài xế: A\nSố điện thoại: 1\nSố xe: 51C\nSố cont: tghu 1234567\nSố seal: S";
        var r = Lx.ProcessMessages(new() { M("x", "t", Now, "Xe <xe@vt.vn>", "Re: LSX 26.11.040 - loading", xe), M("x0", "t", Now.AddHours(-5), "Sale <s@x>", "LSX 26.11.040 - loading", "a") }, new MailConfig { Days = 7 });
        Lx.Load();
        var l = Lx.Rows.Single();
        Assert.Equal("LSX 26.11.040", l.Lsx); Assert.Equal("2026-11-23", l.Ngay); Assert.Equal("TGHU1234567", l.ContSo);
        Assert.True(r.ContSet >= 1);
        Assert.Equal("Đã duyệt", Lx.EmailRows().Single(e => e.Id == "x").TrangThai);
    }

    // ------------------------------------------------------------------ dò tồn / List / phiếu

    private void SeedAnAn()
    {
        var heads = new[] { "Mã hàng", "Tên hàng", "Size", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn cuối", "Trọng lượng", "Tình trạng", "Khách hàng", "Thị trường" };
        var rows = new[]
        {
            new[] { "VTOMSABCDKHMB1162010QC", "Tôm sú PTO (1kg x 10)", "16/20", "MKS", "LSX 26.11.020-17/11", "Kiện", "30", "10", "Thành phẩm", "CGC", "Nhật" },
            new[] { "VTOMSABCDKHMB1162010QC", "Tôm sú PTO (1kg x 10)", "16/20", "", "LSX 26.11.020-18/11", "Kiện", "20", "10", "Thành phẩm", "CGC", "Nhật" },
            new[] { "VTOMSABCDKHMB1213010QC", "Tôm sú PTO (1kg x 10)", "21/30", "", "26.11.20-18/11", "Kiện", "5", "10", "Thành phẩm", "CGC", "Nhật" },
            new[] { "VTOMSABCDKHMB1213010QC", "Tôm sú PTO", "21/30", "", "LSX 26.11.077-18/11", "Kiện", "9", "10", "Thành phẩm", "ISHI", "Nhật" },
            new[] { "XBTP", "Bán thành phẩm", "", "", "LSX 26.11.020-18/11", "Kg", "99", "1", "BTP", "", "" },
        };
        var data = new JsonArray(rows.Select(r => (JsonNode)new JsonObject(r.Select((v, i) => KeyValuePair.Create("c" + i, (JsonNode?)(double.TryParse(v, out var d) && i is 6 or 7 ? JsonValue.Create(d) : JsonValue.Create(v)))))).ToArray());
        _s.Modules.WriteSheets("ton-kho-an-an", new Dictionary<string, JsonArray?>
        {
            ["LoadDataMeta"] = new JsonArray(new JsonObject { ["data"] = "__JSON__" + new JsonObject { ["headers"] = new JsonArray(heads.Select(h => (JsonNode)JsonValue.Create(h)!).ToArray()), ["dataDate"] = "22/11/2026" }.ToJsonString() }),
            ["LoadData"] = data,
        });
    }

    [Fact]
    public void Do_tpc_them_lich_tao_list_va_phieu()
    {
        SeedAnAn();
        var lk = Lx.Lookup("LSX 26.11.020");
        Assert.True(lk.Found, lk.Message);
        Assert.Equal(3, lk.SoDong); Assert.Equal(55, lk.TonKien); Assert.Equal(550, lk.Kg); Assert.Equal("CGC", lk.Khach);
        Assert.False(Lx.Lookup("26.11.999").Found);

        var a = Lx.Add(new LichXuatService.Input { Ngay = "2026-11-25", Lsx = "26.11.020", Gio = "8h30" });
        Assert.Equal("CGC", a.Khach); Assert.Equal("08:30", a.Gio);
        Assert.Throws<BusinessException>(() => Lx.Add(new LichXuatService.Input { Ngay = "2026-11-25", Lsx = "LSX 26.11.020" }));

        // List từ tồn kho: 2 ngày (17/11, 18/11) × 2 size; MKS lấy từ Đặc tính
        var (lines, _) = LichXuatService.TpcLines(KhoLanh.Core.TonKho.AnAnStore.Load(_s.Lan), "26.11.020");
        var ld = LichXuatService.ListFromStock(lines, "26.11.020", Now);
        Assert.True(ld.Ok);
        Assert.Equal(new[] { "16/20", "21/30" }, ld.Sizes.Select(z => z.Size));
        Assert.Equal(new[] { "17.11.2026", "18.11.2026" }, ld.Rows.Select(r => r.Date));
        Assert.Equal(30, ld.Rows[0].Qty["16/20"]); Assert.True(ld.Rows[0].Mks);
        Assert.Equal(5, ld.Rows[1].Qty["21/30"]);

        var (p, created, notes) = Lx.CreatePhieu(new[] { a.Id });
        Assert.True(created, string.Join("\n", notes));
        Assert.Equal("2026-11-25", p.Ngay); Assert.Contains("26.11.020", p.Lsx); Assert.Equal("CGC", p.Khach);
        Lx.Load();
        var h = Lx.BuildHome("2026-11-25").Today.Single();
        Assert.True(h.HasList); Assert.True(h.HasPhieu);
        Assert.False(Lx.CreatePhieu(new[] { a.Id }).Created);

        // List sản xuất: quét tồn kho → tạo List cho LSX còn tồn chưa có List (26.11.077)
        var sc = Lx.ScanStockLists();
        Assert.Equal(new[] { "26.11.077" }, sc.Codes);
        Lx.Load();
        var info = Lx.ListInfos().Single(i => i.Rec.Lsx == "26.11.020");
        Assert.Equal(55, info.Ton); Assert.Equal(1, info.Phieu); Assert.Equal("Chờ xuất", info.Nhan);
    }

    [Fact]
    public void Tu_tao_list_theo_lich_xuat_va_nhat_ky()
    {
        SeedAnAn();
        Lx.Auto = true;
        var made = new List<string>(); Lx.AutoMade += m => made.AddRange(m);
        // lịch mới: LSX có tồn → List từ tồn kho; LSX chưa có hàng → List TRỐNG
        var a = Lx.Add(new LichXuatService.Input { Ngay = "2026-11-25", Lsx = "26.11.020" });
        var b = Lx.Add(new LichXuatService.Input { Ngay = "2026-11-26", Lsx = "26.12.555" });
        Lx.Add(new LichXuatService.Input { Ngay = "2026-11-20", Lsx = "26.11.077" }); // ngày đã qua → không tạo
        var pnx = _s.PhieuKho; pnx.Load(); pnx.LoadAutoLog();
        Assert.Equal(2, pnx.Lists.Count);
        var l20 = pnx.FindList("26.11.020")!; var l555 = pnx.FindList("26.12.555")!;
        Assert.Equal(2, l20.Data.Rows.Count); Assert.Empty(l555.Data.Rows);
        Assert.Equal("2026-11-25", KhoLanh.Core.PhieuKho.PhieuKhoService.TagOf(l20.Data)!.NgayLich);
        Assert.Equal(2, pnx.AutoLogs.Count(l => l["loaiPhieu"] == "__list" && l["listNew"] == "1"));
        Assert.Equal((0, 2), pnx.Unseen());
        Assert.Contains(made, m => m.Contains("List TRỐNG"));
        Assert.Contains("List trống", pnx.AutoItems().Single(i => i.Lsx == "26.12.555").Note);

        // sửa lịch không đổi LSX → không tạo gì; xóa List rồi sửa lịch → đã có nhật ký nhưng List bị xóa hết → quên nhật ký → rà bù tạo lại
        made.Clear();
        Lx.Mutate(l => l.Single(x => x.Id == a.Id).GhiChu = "gọi xe");
        Assert.Empty(made);
        pnx.DeleteList("26.12.555");
        pnx.LoadAutoLog(); Assert.DoesNotContain(pnx.AutoLogs, l => l["listKey"] == "26.12.555");
        Lx.Reconcile(force: true);
        pnx.Load(); Assert.NotNull(pnx.FindList("26.12.555"));

        // đánh dấu đã xem
        pnx.LoadAutoLog(); Assert.True(pnx.MarkSeen('l', "26.11.020"));
        Assert.Equal((0, 1), pnx.Unseen());
        pnx.MarkSeen(); Assert.Equal((0, 0), pnx.Unseen());

        // chế độ List + Phiếu: đổi LSX của lịch b sang 26.11.077 → List từ tồn + phiếu xuất khẩu gắn nhãn tự động
        Lx.AutoMode = "phieu";
        Lx.Mutate(l => l.Single(x => x.Id == b.Id).Lsx = "LSX 26.11.077");
        pnx.Load(); pnx.LoadAutoLog();
        var p = pnx.List.Single();
        Assert.Contains("26.11.077", p.Lsx); Assert.Equal("2026-11-26", p.Ngay);
        Assert.NotNull(KhoLanh.Core.PhieuKho.PhieuKhoService.TagOf(p));
        Assert.True(pnx.IsNew(p));
        Assert.Contains(pnx.AutoLogs, l => l["key"] == "26.11.077|xuat_khau" && l["phieuId"] == p.Id);
        // sửa lần nữa không tạo phiếu thứ 2
        Lx.Mutate(l => l.Single(x => x.Id == b.Id).Lsx = "LSX 26.11.077 ");
        pnx.Load(); Assert.Single(pnx.List);
        // tắt
        Lx.AutoMode = "off"; made.Clear();
        Lx.Add(new LichXuatService.Input { Ngay = "2026-11-27", Lsx = "26.12.900" });
        Assert.Empty(made);
    }

    [Fact]
    public void Viec_cho_lsx_chua_co_lich_va_tong_ket_thang()
    {
        Assert.Contains("chưa có lịch", Lx.PendAdd("26.12.001", "Chờ chứng thư"));
        Lx.Load(); Assert.Single(Lx.Pending);
        var r = Lx.Add(new LichXuatService.Input { Ngay = "2026-12-03", Lsx = "26.12.001" });
        Lx.Load();
        Assert.Empty(Lx.Pending);
        Assert.Equal("Chờ chứng thư", ViecOf(Lx.Rows.Single()).Single().T);
        var v = ViecOf(Lx.Rows.Single()).Single();
        Lx.ViecTick(r.Id, v.I, true);
        Lx.Load(); Assert.Equal(1, ViecOf(Lx.Rows.Single()).Single().D);
        var tk = _s.Modules.ReadSheet("lichxuat", "TongKetThang");
        Assert.Equal("2026-12", tk[0]!["thang"]!.GetValue<string>());
        // ngày đã qua tự "Đã xuất"
        Lx.Add(new LichXuatService.Input { Ngay = "2026-11-20", Lsx = "26.11.001", TrangThai = "plan" });
        Lx.Load(); Assert.Equal("ok", Lx.Rows.Single(x => x.Ngay == "2026-11-20").TrangThai);
    }

    [Fact]
    public void Doc_email_xe_dan_vao()
    {
        var r = LichXuatService.ReadXeMail("Họ tên tài xế: A\nSố xe: 51C\nSố cont: abcd 1234567\nSố seal: 9");
        Assert.True(r.Ok); Assert.Equal("ABCD1234567", r.Cont);
        Assert.False(LichXuatService.ReadXeMail("Họ tên tài xế: A\nSố xe: 51C").Ok);
    }
}

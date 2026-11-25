using KhoLanh.Core.LichXuat;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.LichXuat.LichXuatCore;

namespace KhoLanh.Core.Tests;

/// <summary>1.90: email / thêm tay / nhập nhanh khớp lịch THEO SỐ LSX — lịch đã sửa tay được chuyển, không tạo lịch trùng; ca không chắc thì hỏi; công cụ LSX trùng lịch.</summary>
public class LichXuatTrungTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-lxt-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private static readonly DateTime Now = new(2026, 11, 24, 9, 0, 0);
    private LichXuatService Lx => _s.LichXuat;
    private static readonly MailConfig Cfg = new() { Auto = true, Days = 7 };

    public LichXuatTrungTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
        Lx.Now = () => Now;
        Lx.SaveTrust("sale@an.vn", true);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static LichXuatEmail.Msg M(string id, string th, DateTime d, string subj, string text = "x", string from = "Sale <sale@an.vn>") => new(id, th, d, from, subj, text);
    private List<Lich> Of(string code) { Lx.Load(); return Lx.Rows.Where(r => Keys(r.Lsx).Contains(code)).OrderBy(r => r.Ngay).ToList(); }
    private LichXuatService.Input In(string ngay, string lsx, string gio = "", string note = "") => new() { Ngay = ngay, Lsx = lsx, Gio = gio, GhiChu = note };

    [Fact]
    public void Email_doi_lich_chuyen_lich_da_sua_tay_khong_tao_trung()
    {
        var msgs = new List<LichXuatEmail.Msg> { M("1", "t1", Now.AddHours(-3), "LSX 26.09.020 - TOYO REIZO - KEO DONG - ETD 26/11") };
        Lx.ProcessMessages(msgs, Cfg);
        var l = Of("26.09.020").Single(); Assert.Equal("2026-11-26", l.Ngay);
        // người dùng sửa tay: sang 25/11, giờ 10h, ghi chú
        Lx.Update(l.Id, In("2026-11-25", l.Lsx, "10:00", "đã gọi xe"));
        // thư trả lời: đổi kéo đóng về 26/11 → CHUYỂN lịch đã sửa tay, giữ giờ + ghi chú, không thêm lịch
        msgs.Add(M("2", "t1", Now, "Re: LSX 26.09.020 - TOYO REIZO - KEO DONG - ETD 26/11", "Đổi ngày kéo đóng sang ngày 26/11"));
        var r = Lx.ProcessMessages(msgs, Cfg);
        Assert.Equal(1, r.Auto);
        var after = Of("26.09.020").Single();
        Assert.Equal(l.Id, after.Id); Assert.Equal("2026-11-26", after.Ngay); Assert.Equal("10:00", after.Gio); Assert.Equal("đã gọi xe", after.GhiChu);
    }

    [Fact]
    public void Thu_tao_lich_moi_khi_LSX_da_co_lich_ngay_khac_thi_cho_nguoi_dung_chon()
    {
        Lx.Add(In("2026-11-25", "26.09.020", "10:00"));
        // thư GỐC mới (chuỗi khác) cho cùng LSX, ETD 26/11 — có thể đổi kế hoạch, cũng có thể là cont thứ 2 → không tự làm
        var r = Lx.ProcessMessages(new() { M("9", "t9", Now, "LSX 26.09.020 - KEO DONG - ETD 26/11") }, Cfg);
        Assert.Equal(0, r.Auto); Assert.Equal(1, r.Waiting);
        Assert.Single(Of("26.09.020"));
        var row = Lx.EmailRows().Single();
        Assert.Contains("chọn chuyển lịch cũ hay giữ cả hai", LichXuatEmail.Guard(row, Lx.Rows, Now));
        var plan = Lx.EmailPlan(row, "2026-11-26");
        Assert.Equal(LichXuatEmail.PlanKind.Choose, plan.Single().Kind);
        Assert.Throws<BusinessException>(() => Lx.Decide("9", true));        // không chọn → không áp
        // chọn chuyển lịch cũ
        var old = plan.Single().Cands.Single();
        var msg = Lx.Decide("9", true, null, null, old.Id);
        Assert.Contains("25/11/2026 → 26/11/2026", msg);
        var l = Of("26.09.020").Single(); Assert.Equal(old.Id, l.Id); Assert.Equal("2026-11-26", l.Ngay); Assert.Equal("10:00", l.Gio);
        // hoàn tác → về 25/11; duyệt lại kiểu "giữ cả hai" → 2 lịch
        Lx.Undo("9");
        Assert.Equal("2026-11-25", Of("26.09.020").Single().Ngay);
        Lx.Decide("9", true, null, null, LichXuatService.ChoiceKeep);
        Assert.Equal(new[] { "2026-11-25", "2026-11-26" }, Of("26.09.020").Select(x => x.Ngay));
    }

    [Fact]
    public void Lich_trong_bo_nho_cu_van_khop_LSX_may_khac_vua_them()
    {
        Lx.Load();                                                          // máy A mở Tổng quan (chưa có lịch)
        var b = new AppServices(Path.Combine(_dir, "kho.db"));             // máy B (cùng dữ liệu) thêm tay lịch 25/11
        b.Auth.Login("admin", "matkhau1"); b.LichXuat.Now = () => Now;
        b.LichXuat.Add(In("2026-11-25", "26.09.020"));
        // máy A tự quét email: thư gốc ETD 26/11 → trước đây (Rows cũ) thêm lịch thứ 2; nay đọc lại → giữ chờ chọn
        var r = Lx.ProcessMessages(new() { M("1", "t1", Now, "LSX 26.09.020 - KEO DONG - ETD 26/11") }, Cfg);
        Assert.Equal(0, r.Auto);
        Assert.Single(Of("26.09.020"));
    }

    [Fact]
    public void Lich_cont_ghep_khop_tung_so_LSX()
    {
        Lx.Add(In("2026-11-25", "26.09.020 + 26.07.044"));
        var msgs = new List<LichXuatEmail.Msg>
        {
            M("1", "t1", Now.AddDays(-2), "LSX 26.09.020 - KEO DONG - ETD 25/11"),
            M("2", "t1", Now, "Re: LSX 26.09.020 - KEO DONG", "Dời kéo đóng sang ngày 27/11 lúc 8h30"),
        };
        var r = Lx.ProcessMessages(msgs, Cfg);
        Lx.Load();
        var l = Lx.Rows.Single();                                           // không thêm lịch riêng 26.09.020
        Assert.Equal("2026-11-27", l.Ngay); Assert.Equal("08:30", l.Gio); Assert.Contains("26.07.044", l.Lsx);
        Assert.Equal(2, r.Auto);
    }

    [Fact]
    public void Them_tay_va_nhap_nhanh_chuyen_lich_cu_thay_vi_them_trung()
    {
        var a = Lx.Add(In("2026-11-26", "26.09.020"));
        Lx.ViecAdd(a.Id, "In nhãn");
        var other = Lx.OtherDays("LSX 26.09.020", "2026-11-25");
        Assert.Equal(a.Id, other.Single().Id);
        Lx.MoveInto(a.Id, In("2026-11-25", "26.09.020", "10:00"));
        var l = Of("26.09.020").Single();
        Assert.Equal(("2026-11-25", "10:00"), (l.Ngay, l.Gio)); Assert.Single(ViecOf(l));
        // nhập nhanh "Ngày 27/11: LSX 26.09.020" → báo trước, chọn chuyển
        var q = new List<QuickRow> { new("2026-11-27", "LSX 26.09.020", "plan", ""), new("2026-11-27", "LSX 26.10.001", "plan", "") };
        Assert.Single(Lx.QuickConflicts(q));
        var res = Lx.AddQuick(q, moveOther: true);
        Assert.Single(res.Moved); Assert.Single(res.Added);
        Assert.Equal("2026-11-27", Of("26.09.020").Single().Ngay);
        // không chọn chuyển → thêm và báo
        res = Lx.AddQuick(new List<QuickRow> { new("2026-11-28", "LSX 26.09.020", "plan", "") });
        Assert.Single(res.OtherDay); Assert.Equal(2, Of("26.09.020").Count);
    }

    [Fact]
    public void Cong_cu_LSX_trung_lich_gop_hoac_danh_dau_hop_le()
    {
        var a = Lx.Add(In("2026-11-25", "26.09.020", "10:00"));
        var b = Lx.Add(In("2026-11-26", "26.09.020", "", "cont 2"));
        Lx.ViecAdd(b.Id, "Gửi list");
        Lx.Add(In("2026-11-26", "26.07.044"));
        Lx.Add(In("2026-11-20", "26.05.001"));                              // ngày đã qua = đã xuất → không tính
        Lx.Add(In("2026-11-27", "26.05.001"));
        Lx.Load();
        var g = Lx.DupGroups().Single();
        Assert.Equal("26.09.020", g.Lsx); Assert.Equal(2, g.Rows.Count);
        // đánh dấu hợp lệ → hết báo; thêm lịch thứ 3 → bộ lịch khác → báo lại
        Lx.MarkDupOk(g);
        Assert.Empty(Lx.DupGroups());
        var c = Lx.Add(In("2026-11-28", "26.09.020"));
        Lx.Load();
        g = Lx.DupGroups().Single(); Assert.Equal(3, g.Rows.Count);
        // gộp: giữ 25/11, lấy ghi chú + việc của lịch 26/11
        Lx.ResolveDup(a.Id, g.Rows.Select(r => r.Id));
        var l = Of("26.09.020").Single();
        Assert.Equal(a.Id, l.Id); Assert.Equal("10:00", l.Gio); Assert.Contains("cont 2", l.GhiChu); Assert.Single(ViecOf(l));
        Assert.Empty(Lx.DupGroups());
        _ = c;
    }
}

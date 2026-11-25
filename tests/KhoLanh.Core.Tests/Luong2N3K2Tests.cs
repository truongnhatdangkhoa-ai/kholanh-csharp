using System.Text.Json.Nodes;
using KhoLanh.Core.KiemKe;
using KhoLanh.Core.Modules;
using KhoLanh.Core.TonKho;
using Xunit;
using static KhoLanh.Core.TonKho.MauFile;

namespace KhoLanh.Core.Tests;

/// <summary>1.73 (luồng 2): N3 nhớ cấu trúc file nạp + cảnh báo đổi mẫu · K2 báo trùng / thiếu khi nhập kiểm kê.</summary>
public class Luong2N3K2Tests
{
    // ------------------------------------------------------------------ N3 · mẫu file

    private static readonly string[] H = { "Item ID", "Mã hàng", "Tên hàng", "Đặc tính", "Size", "Tồn cuối", "Tồn cuối (NET)", "Trọng lượng", "Khách hàng" };
    private static Mau M(string[] h, int rows = 100, string date = "20/11/2026", string file = "TON 20.11.xlsx", string sha = "a", string sheet = "Sheet1") =>
        Tao(file, sheet, h, rows, date, sha);

    [Fact]
    public void Mau_khong_doi_thi_khong_canh_bao()
    {
        var r = SoSanh(M(H), M(H, 110, "21/11/2026", "TON 21.11.xlsx", "b"), BatBuocAnAn);
        Assert.Empty(r);
        Assert.Empty(SoSanh(null, M(H), BatBuocAnAn));   // lần nạp đầu: chưa có mẫu
    }

    [Fact]
    public void Thieu_cot_bat_buoc_la_loi_do_co_goi_y_ten_gan_giong()
    {
        var moi = H.Select(h => h == "Trọng lượng" ? "Trọng lượng (kg)" : h).Where(h => h != "Size").ToArray();
        var r = SoSanh(M(H), M(moi, date: "21/11/2026"), BatBuocAnAn);
        var tl = Assert.Single(r, x => x.Key == "thieu:trongluong");
        Assert.Equal(Muc.Loi, tl.Muc);
        Assert.Contains("Trọng lượng (kg)", tl.Text);                 // gợi ý tên gần giống
        Assert.Contains(r, x => x.Key == "thieu:size" && x.Muc == Muc.Loi);
        Assert.DoesNotContain(r, x => x.Key == "them");               // cột "mới" đã được hiểu là đổi tên
        Assert.True(CoLoi(r));
        Assert.Equal(Muc.Loi, r[0].Muc);                              // đỏ xếp đầu
    }

    [Fact]
    public void Cot_moi_doi_ten_khong_bat_buoc_va_doi_thu_tu_la_vang()
    {
        var moi = new[] { "Item ID", "Mã hàng", "Tên hàng", "Đặc tính", "Size", "Tồn cuối", "Tồn cuối (NET)", "Trọng lượng", "Tên khách hàng", "Ghi chú" };
        var r = SoSanh(M(H), M(moi, date: "21/11/2026"), BatBuocAnAn);
        Assert.Contains(r, x => x.Key == "doiten:khachhang" && x.Muc == Muc.CanhBao && x.Text.Contains("Tên khách hàng"));
        var them = Assert.Single(r, x => x.Key == "them");
        Assert.Contains("Ghi chú", them.Text);
        Assert.DoesNotContain("Tên khách hàng", them.Text);
        Assert.False(CoLoi(r));

        var dao = H.ToArray(); (dao[4], dao[5]) = (dao[5], dao[4]);
        var r2 = SoSanh(M(H), M(dao, date: "21/11/2026"), BatBuocAnAn);
        var tt = Assert.Single(r2);
        Assert.Equal("thutu", tt.Key); Assert.Equal(Muc.CanhBao, tt.Muc);

        var mat = SoSanh(M(H), M(H.Where(h => h != "Khách hàng").ToArray(), date: "21/11/2026"), BatBuocAnAn);
        Assert.Equal("mat:khachhang", Assert.Single(mat).Key);
    }

    [Fact]
    public void Ngay_so_lieu_cu_hon_la_do_bang_la_vang_ngay_dang_co_uu_tien()
    {
        var cu = SoSanh(M(H), M(H, date: "19/11/2026"), BatBuocAnAn);
        Assert.Equal(Muc.Loi, Assert.Single(cu, x => x.Key == "ngay-cu").Muc);
        var bang = SoSanh(M(H), M(H, date: "20/11/2026"), BatBuocAnAn);
        Assert.Equal(Muc.CanhBao, Assert.Single(bang, x => x.Key == "ngay-bang").Muc);
        // người dùng đã sửa ngày số liệu module thành 22/11 → file 21/11 là cũ hơn
        Assert.Contains(SoSanh(M(H), M(H, date: "21/11/2026"), BatBuocAnAn, "22/11/2026"), x => x.Key == "ngay-cu");
        // chưa có ngày → không so
        Assert.DoesNotContain(SoSanh(M(H, date: ""), M(H, date: "19/11/2026"), BatBuocAnAn), x => x.Key.StartsWith("ngay"));
    }

    [Fact]
    public void So_dong_doi_qua_30_phan_tram_va_cung_ten_khac_noi_dung()
    {
        Assert.Contains(SoSanh(M(H, 100), M(H, 60, "21/11/2026"), BatBuocAnAn), x => x.Key == "sodong" && x.Text.Contains("GIẢM 40%"));
        Assert.Contains(SoSanh(M(H, 100), M(H, 140, "21/11/2026"), BatBuocAnAn), x => x.Key == "sodong" && x.Text.Contains("TĂNG"));
        Assert.DoesNotContain(SoSanh(M(H, 100), M(H, 125, "21/11/2026"), BatBuocAnAn), x => x.Key == "sodong");
        var same = SoSanh(M(H, file: "ton.xlsx", sha: "x"), M(H, date: "21/11/2026", file: "TON.xlsx", sha: "y"), BatBuocAnAn);
        Assert.Equal(Muc.ThongTin, Assert.Single(same, x => x.Key == "cungten").Muc);
        Assert.DoesNotContain(SoSanh(M(H, file: "ton.xlsx", sha: "x"), M(H, date: "21/11/2026", file: "ton.xlsx", sha: "x"), BatBuocAnAn), x => x.Key == "cungten");
    }

    [Fact]
    public void Vi_tri_nhan_ten_cot_bi_danh_va_luu_doc_qua_sheet_source()
    {
        var vt = new[] { "STT", "Mã HH", "Tên hàng", "Vị trí", "Số lượng tồn", "T.Lượng" };
        Assert.Empty(SoSanh(null, Tao("vt.xlsx", "S", vt, 10, "", ""), BatBuocViTri));
        Assert.Contains(SoSanh(null, Tao("vt.xlsx", "S", vt.Where(x => x != "Vị trí"), 10, "", ""), BatBuocViTri), x => x.Key == "thieu:vitri");
        Assert.Empty(SoSanh(null, Tao("vt.xlsx", "S", Array.Empty<string>(), 10, "", ""), BatBuocViTri));   // không đọc được tiêu đề → không báo thiếu

        var src = new MemSource();
        Assert.Null(Doc(src, "ton-vi-tri-an-an"));
        var m = Tao("Tồn 21.11.xlsx", "Sheet1", vt, 1234, "21/11/2026", "abc", "an");
        Ghi(src, "ton-vi-tri-an-an", m);
        var back = Doc(src, "ton-vi-tri-an-an")!;
        Assert.Equal(vt, back.Headers);
        Assert.Equal(1234, back.Rows); Assert.Equal("2026-11-21", back.DataDate); Assert.Equal("abc", back.Sha); Assert.Equal("Tồn 21.11.xlsx", back.FileName);
        Assert.StartsWith("[{\"data\":\"__JSON__", src.Read("ton-vi-tri-an-an", MauFile.Sheet));

        var raw = new List<object?[]> { new object?[] { "BÁO CÁO TỒN KHO GỬI" }, new object?[] { "STT", "Mã hàng", null, "Tên hàng", "Số lô" }, new object?[] { 1.0, "A" } };
        Assert.Equal(new[] { "STT", "Mã hàng", "Tên hàng", "Số lô" }, TieuDeTho(raw));
    }

    private sealed class MemSource : ISheetSource
    {
        private readonly Dictionary<string, string> _d = new();
        public string Read(string dbKey, string sheet) => _d.GetValueOrDefault(dbKey + "/" + sheet, "[]");
        public List<string> List(string dbKey) => _d.Keys.Where(k => k.StartsWith(dbKey + "/")).Select(k => k[(dbKey.Length + 1)..]).ToList();
        public void Write(string dbKey, IDictionary<string, JsonArray?> sheets) { foreach (var (k, v) in sheets) _d[dbKey + "/" + k] = v?.ToJsonString() ?? "[]"; }
    }

    // ------------------------------------------------------------------ K2 · kiểm tra trang kiểm kê

    private static readonly string[] KkCols = { "Vị trí", "Mã hàng", "Size", "Trọng lượng", "Số lượng", "Tổng Net(kg)", "Ghi chú", "Ngày nhập", "Số lượng xuất", "Số lượng còn lại", "Tổng Net(kg) còn lại", "Ngày xuất" };
    private static KkKiemTra.KetQua Kk(string[][] rows, Func<string, bool>? vt = null, Func<string, bool>? ma = null)
    {
        var cot = KkKiemTra.Cols(KkCols)!;
        return KkKiemTra.Check(rows.Length, (r, c) => c < rows[r].Length ? rows[r][c] : "", cot, vt, ma);
    }
    private static string[] R(string vt, string ma, string sl = "", string xs = "") => new[] { vt, ma, "", "", sl, "", "", "", xs };

    [Fact]
    public void Kiem_ke_bao_trung_vi_tri_ma_hang()
    {
        var cot = KkKiemTra.Cols(KkCols)!;
        Assert.Equal((0, 1, 4, 8), (cot.Vt, cot.Ma, cot.Sl, cot.SlXuat));
        var k = Kk(new[] { R("TG1.A.01.1.1", "M1", "5"), R("TG1.A.01.1.2", "M1", "3"), R("tg1.a.1.1.1", "m1 ", "2"), R("TG1.A.01.1.1", "M2", "1"), R("", "M1", "1"), R("TG1.A.01.1.1", "M1") });
        Assert.Equal(3, k.TrungDong);                     // dòng 1, 3, 6 (viết thường / thiếu số 0 vẫn là cùng ô + cùng mã)
        Assert.False(k.Muc(0, 1)); Assert.False(k.Muc(0, 0));
        Assert.Contains("dòng 3, 6", k.Tip(0, 1));
        Assert.Null(k.Muc(1, 1)); Assert.Null(k.Muc(4, 1));
        Assert.Equal("3 dòng trùng", k.TomTat());
        Assert.False(k.CoDo);
    }

    [Fact]
    public void Kiem_ke_bao_vi_tri_la_ma_la_va_so_loi()
    {
        var codes = new HashSet<string> { "TG1.A.01.1.1", "TG1.A.01.1.2" };
        var known = new HashSet<string> { "M1", "M2" };
        var k = Kk(new[]
        {
            R("TG1.A.01.1.1", "M1", "5", "2"),       // ổn
            R("TG1.A.09.1.1", "M2", "abc"),          // vị trí ngoài sơ đồ + SL không phải số
            R("kho lạnh", "MX", "-1"),               // vị trí sai dạng + mã lạ + SL âm
            R("TG1.A.01.1.2", "M2", "3", "4"),       // SL xuất > SL
            R("TG1.A.01.1.2", "M1", "", "x"),        // SL xuất không phải số
        }, codes.Contains, known.Contains);
        Assert.True(k.Muc(0, 0) is null && k.Muc(0, 4) is null && k.Muc(0, 8) is null);
        Assert.Equal(2, k.ViTriLa); Assert.Equal(1, k.MaLa); Assert.Equal(4, k.SoLoi);
        Assert.False(k.Muc(1, 0)); Assert.Contains("không có trong Sơ đồ kho", k.Tip(1, 0));
        Assert.True(k.Muc(1, 4)); Assert.Contains("không phải số", k.Tip(1, 4));
        Assert.Contains("không đúng dạng", k.Tip(2, 0));
        Assert.False(k.Muc(2, 1));
        Assert.True(k.Muc(2, 4)); Assert.Contains("âm", k.Tip(2, 4));
        Assert.True(k.Muc(3, 8)); Assert.Contains("lớn hơn", k.Tip(3, 8));
        Assert.True(k.Muc(4, 8));
        Assert.True(k.CoDo);
        Assert.Equal("2 vị trí lạ · 1 mã lạ · 4 số lỗi", k.TomTat());
        // nhảy ô kế tiếp: theo dòng → cột, hết thì quay về đầu
        Assert.Equal((1, 0), (k.Next(0, 8)!.Row, k.Next(0, 8)!.Col));
        Assert.Equal((1, 4), (k.Next(1, 0)!.Row, k.Next(1, 0)!.Col));
        Assert.Equal((1, 0), (k.Next(4, 8)!.Row, k.Next(4, 8)!.Col));
        // không có danh sách vị trí / mã → bỏ qua 2 loại đó
        var k2 = Kk(new[] { R("TG9.Z.01.1.1", "MX", "1") });
        Assert.True(k2.Empty);
    }

    [Fact]
    public void Kiem_ke_2000_dong_nhanh()
    {
        var rows = Enumerable.Range(0, 2000).Select(i => R($"TG1.A.{i % 40 + 1:00}.{i % 3 + 1}.{i % 5 + 1}", "M" + (i % 700), (i % 9).ToString(), (i % 4).ToString())).ToArray();
        var codes = rows.Select(r => KhoLanh.Core.SoDo.SoDoCore.Parse(r[0])!.Code).ToHashSet();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 10; i++) Kk(rows, codes.Contains, _ => true);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"10 lần kiểm tra 2.000 dòng mất {sw.ElapsedMilliseconds} ms");
    }
}

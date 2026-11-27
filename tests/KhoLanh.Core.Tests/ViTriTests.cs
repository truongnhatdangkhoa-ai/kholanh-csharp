using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using KhoLanh.Core.ViTri;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.ViTri.ViTriService;

namespace KhoLanh.Core.Tests;

public class ViTriTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-vt-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private const string A = "TPC-VRHLBTNXECR003140IME1", B = "TPC-VRHLBTNXXXR000914IME1";

    public ViTriTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private ViTriService Vt => _s.ViTri;

    /// <summary>File "Tồn kho theo vị trí" kiểu thật: 2 dòng tiêu đề báo cáo phía trên, tiêu đề cột ở dòng 3.</summary>
    private string File_(string name, params (string ma, string ten, string lo, string vt, double sl, double tl)[] rows)
    {
        var path = Path.Combine(_dir, name);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "BÁO CÁO TỒN KHO THEO VỊ TRÍ";
        var heads = new[] { "Item ID", "Mã hàng", "Tên hàng", "ĐVT", "Lô (Batch)", "Vị trí", "SL Tồn", "T.Lượng", "Net" };
        for (var i = 0; i < heads.Length; i++) ws.Cell(3, i + 1).Value = heads[i];
        var r = 4;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = r; ws.Cell(r, 2).Value = x.ma; ws.Cell(r, 3).Value = x.ten; ws.Cell(r, 4).Value = "Thùng";
            ws.Cell(r, 5).Value = x.lo; ws.Cell(r, 6).Value = x.vt; ws.Cell(r, 7).Value = x.sl; ws.Cell(r, 8).Value = x.tl; ws.Cell(r, 9).Value = x.sl * x.tl; r++;
        }
        wb.SaveAs(path);
        return path;
    }
    private const string TenA = "Tôm sú BTP (1.8 kg x 6 gói) - IQF - HD01", TenA2 = "Tôm sú BTP (1.8 kg x 6 gói) - Semi - HD02", TenB = "Tôm thẻ (2 kg x 5 gói) - IQF - HD03";
    private string F1() => File_("TON 20.11.xlsx",
        (A, TenA, "L1", "A.02.01", 50, 10.8),
        (A, TenA, "L1", "A.10.01", 30, 10.8),
        (A, TenA2, "L2", "A.02.01", 20, 10.8),
        (B, TenB, "L3", "B.01.03", 40, 10),
        (B, TenB, "L3", "B.01.03", 5, 10));   // dòng trùng Mã|Lô|Vị trí → khóa #1

    private int Idx(string ma, string vt, string lo) => Vt.Rows.FindIndex(r => r.MaGoc == ma && r.Vt == vt && r.Lo == lo);
    private ViTriService Reload() { var v = new ViTriService(() => _s.Lan, _s.Session, _s.Audit); v.Load(); return v; }

    [Fact]
    public void Helpers_codes_names_positions_dates()
    {
        Assert.Equal("VRHLBTNXECR003140IME1", ShortCode(A));
        Assert.Equal(("IQF", "HD01"), SplitName(TenA));
        Assert.Equal("1.8KG", QcOf(TenA));
        Assert.Equal("A", ZoneOf("A.02.01"));
        Assert.True(CmpViTri("A.2.1", "A.10.1") < 0);
        Assert.True(CmpViTri("B.01", "A.99") > 0);
        Assert.Equal("23/09/2026", GuessDate("TON 23.9.2026.xlsx"));
        Assert.Equal("01/02/2025", GuessDate("vi tri 01022025.xlsx"));
        Assert.Equal("PHIẾU XUẤT", CleanTitle("  "));
        Assert.Equal("XX 05.11", FileBase("xuatxa", new DateTime(2026, 11, 5)));
        Assert.Equal("XTT 05.11", FileBase("thaythung", new DateTime(2026, 11, 5)));
    }

    [Fact]
    public void Row_keys_number_duplicates_and_relink_after_shift()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var keys = RowKeys(Vt.Rows);
        Assert.Equal(A + "|L1|A.02.01#0", keys[0]);
        Assert.Equal(B + "|L3|B.01.03#0", keys[3]);
        Assert.Equal(B + "|L3|B.01.03#1", keys[4]);
        var lines = new List<Line> { new() { Key = B + "|L3|B.01.03#1" }, new() { Key = A + "|L9|Z#0" } };
        var (moved, missing) = Relink(lines, new List<string> { B + "|L3|B.01.03#0" });
        Assert.Equal((1, 1), (moved, missing));
        Assert.Equal(B + "|L3|B.01.03#0", lines[0].Key);
    }

    [Fact]
    public void Read_file_finds_header_and_rejects_wrong_file()
    {
        Vt.Load();
        var f = Vt.ReadFile(F1());
        Assert.Equal(5, f.Rows.Count); Assert.Equal(3, f.HeaderRow);
        Assert.Equal("VRHLBTNXECR003140IME1", f.Rows[0].Ma);
        Assert.Equal("IQF", f.Rows[0].Dt); Assert.Equal("HD01", f.Rows[0].Hd);
        var bad = Path.Combine(_dir, "bad.xlsx");
        using (var wb = new XLWorkbook()) { wb.AddWorksheet("x").Cell(1, 1).Value = "Mã hàng"; wb.SaveAs(bad); }
        var ex = Assert.Throws<BusinessException>(() => Vt.ReadFile(bad));
        Assert.Contains("Vị trí", ex.Message);
    }

    [Fact]
    public void Tra_cuu_hang_da_xuat_theo_ma_size_vi_tri()
    {
        // 1.132: mọi dòng của mọi phiếu xuất (mới nhất trước) + tồn hiện tại ở đúng Mã · Lô · Vị trí để rà soát
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var i0 = Idx(A, "A.02.01", "L1"); var i4 = 4;
        var a = Vt.CreateSlip(new() { new(i0, 12) }, false, "xuatxa", DateTime.Today, "");
        var b = Vt.CreateSlip(new() { new(i4, 5) }, false, "xuatxa", DateTime.Today, "");
        var l = Vt.DaXuatList();
        Assert.Equal(2, l.Count);
        var la = l.Single(x => x.SlipId == a.Id);
        Assert.Equal((A, "A.02.01", "L1", 12.0), (la.MaGoc, la.Vt, la.Lo, la.Sl));
        Assert.Equal(38, la.TonVt);                         // file tồn (sau khi trừ) còn 38 ở đúng ô
        var lb = l.Single(x => x.SlipId == b.Id);
        // ô B.01.03 có 2 dòng cùng mã + lô: xuất hết 1 dòng thì ô vẫn còn dòng kia → tồn hiện tại = tổng các dòng cùng Mã · Lô · Vị trí
        Assert.Equal(Vt.Rows.Where(r => r.MaGoc == lb.MaGoc && r.Lo == lb.Lo && r.Vt == lb.Vt).Sum(r => r.Sl), lb.TonVt);
        Assert.Equal(0, Vt.Rows[i4].Sl);
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), la.Ngay);
        Assert.Equal(ViTriService.ShortCode(A), la.Ma);
    }

    [Fact]
    public void Create_slip_deducts_persists_and_reloads_like_old_app()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var i0 = Idx(A, "A.02.01", "L1"); var i4 = 4;
        var s = Vt.CreateSlip(new() { new(i0, 12), new(i4, 5) }, false, "xuatxa", DateTime.Today, "Phiếu xả kho A");
        Assert.StartsWith("PCVT-" + DateTime.Now.ToString("ddMMyy"), s.Id);
        Assert.Equal(38, Vt.Rows[i0].Sl); Assert.Equal(0, Vt.Rows[i4].Sl);
        Assert.Equal(B + "|L3|B.01.03#1", s.Lines.Single(l => l.MaGoc == B).Key);
        Assert.Throws<BusinessException>(() => Vt.CreateSlip(new() { new(i0, 39) }, false, "", DateTime.Today, ""));

        var v = Reload();
        Assert.Equal(38, v.Rows[i0].Sl);
        var r = v.Slips.Single();
        Assert.Equal("xuatxa", r.Loai); Assert.Equal("Phiếu xả kho A", r.TieuDe); Assert.True(r.Deducted); Assert.True(v.Active(r));
        Assert.Equal(17, r.TongSl);
        Assert.Equal(12, v.DoneMap()[i0]);
        // Dữ liệu lưu đúng dạng app cũ: __JSON__{slips,seq}, cột tiếng Việt
        var raw = _s.Lan.Read(Db, SheetSlips);
        Assert.Contains("__JSON__", raw); Assert.Contains("slips", raw); Assert.Contains("deducted", raw); Assert.Contains("PCVT-", raw);
        Assert.Contains("SL Tồn", _s.Lan.Read(Db, SheetRows));
    }

    [Fact]
    public void Edit_toggle_delete_restore_stock()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var i0 = Idx(A, "A.02.01", "L1"); var i1 = Idx(A, "A.10.01", "L1");
        var s = Vt.CreateSlip(new() { new(i0, 50) }, false, "thaythung", DateTime.Today, "");
        Assert.Equal(0, Vt.Rows[i0].Sl);

        var e = Vt.BeginEdit(s.Id);
        Assert.Equal(50, e.Sel[i0]); Assert.Equal(50, e.Bonus[i0]);
        Vt.SaveEdit(e, new() { new(i0, 40), new(i1, 3) }, true, "xuatxa", DateTime.Today, "");
        Assert.Equal(10, Vt.Rows[i0].Sl); Assert.Equal(27, Vt.Rows[i1].Sl);
        Assert.Equal("xuatxa", Vt.Slips[0].Loai); Assert.True(Vt.Slips[0].View!.Gop);

        Vt.SetDeducted(s.Id, false);
        Assert.Equal(50, Vt.Rows[i0].Sl); Assert.Equal(30, Vt.Rows[i1].Sl);
        Vt.SetDeducted(s.Id, true);
        Assert.Equal(10, Vt.Rows[i0].Sl);
        Vt.Delete(s.Id);
        Assert.Equal(50, Vt.Rows[i0].Sl); Assert.Empty(Vt.Slips);
    }

    [Fact]
    public void Merge_split_back_and_split_lines_keep_stock()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var i0 = Idx(A, "A.02.01", "L1"); var i3 = 3;
        var a = Vt.CreateSlip(new() { new(i0, 5) }, false, "xuatxa", DateTime.Today, "");
        var b = Vt.CreateSlip(new() { new(i0, 7), new(i3, 4) }, false, "xuatxa", DateTime.Today, "");
        var c = Vt.CreateSlip(new() { new(i3, 1) }, false, "thaythung", DateTime.Today, "");
        Assert.Throws<BusinessException>(() => Vt.Merge(new() { a.Id, c.Id }));   // khác loại
        var stock = Vt.Rows.Select(r => r.Sl).ToList();

        var m = Vt.Merge(new() { a.Id, b.Id });
        Assert.Equal(2, Vt.Slips.Count);
        Assert.Equal(12, m.Lines.Single(l => l.MaGoc == A).Qty);
        Assert.Equal(stock, Vt.Rows.Select(r => r.Sl).ToList());
        Vt.SplitBack(m.Id);
        Assert.Equal(3, Vt.Slips.Count); Assert.Contains(Vt.Slips, s => s.Id == a.Id);

        var bb = Vt.Slips.Single(s => s.Id == b.Id);
        var ns = Vt.SplitLines(new() { new(b.Id, bb.Lines.FindIndex(l => l.MaGoc == B), 4) }, "thaythung", DateTime.Today, "đổi loại");
        Assert.Equal(4, ns.TongSl); Assert.Equal("thaythung", ns.Loai);
        Assert.Equal(7, Vt.Slips.Single(s => s.Id == b.Id).TongSl);
        Assert.Equal(stock, Vt.Rows.Select(r => r.Sl).ToList());
    }

    [Fact]
    public void Gop_view_merges_same_position_and_size_prefix()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var i0 = Idx(A, "A.02.01", "L1"); var i2 = Idx(A, "A.02.01", "L2");
        Assert.NotNull(Vt.GopLabel(Vt.Rows[i0]));
        var items = Vt.Items(new[] { new Pick(i0, 2), new Pick(i2, 3), new Pick(3, 1) });
        Assert.Equal("A.02.01", items[0].R.Vt);   // sắp theo vị trí
        var same = Vt.BuildView(items, false);
        Assert.Equal(3, same.Count);                // khác Đặc tính → không gộp
        var gop = Vt.BuildView(items, true);
        Assert.Equal(2, gop.Count);
        var g = gop.Single(v => v.Vt == "A.02.01");
        Assert.Equal(5, g.X); Assert.Equal(2, g.Gop); Assert.Equal("IQF / Semi", g.Dt); Assert.Equal(Vt.GopLabel(Vt.Rows[i0]), g.Ma);
    }

    [Fact]
    public void Smart_search_exact_size_label_like_old_app()
    {
        const string S50 = "TPC-VRHLBTNXXXR000050IME1", S4150 = "TPC-VRHLBTNXXXR004150IME1", S35 = "TPC-VRHLBTNXXXR000035IME1", S500 = "TPC-VRHLBTNXXXR000500IME1";
        Vt.Load();
        Vt.Import(Vt.ReadFile(File_("TON 21.11.xlsx",
            (S50, "Tôm BTP (1.8 kg x 6 gói) - MKS NAU - 16TC", "L1", "TG1.A.04.6.1", 50, 10.8),
            (S50, "Tôm BTP (1.8 kg x 6 gói) - 23.MKS - Nau.", "L2", "TG1.A.09.7.2", 3, 10.8),
            (S4150, "Tôm BTP (1.8 kg x 6 gói) - MKS NAU - RADONG", "L3", "TG2.E.02.5.2", 7, 9),
            (S35, "Tôm BTP (1.8 kg x 6 gói) - 23.MKS - TRANG", "L4", "TG2.F.11.5.1", 50, 10.8),
            (S500, "Tôm BTP (1.8 kg x 6 gói) - MKS - X", "L5", "TG2.F.11.5.2", 1, 10.8))));
        var tag50 = Vt.GopLabel(Vt.Rows.First(r => r.MaGoc == S50));
        Assert.False(string.IsNullOrEmpty(tag50));
        List<string> Hit(string q) { var m = Vt.Matcher(q); return Vt.Rows.Where(r => m is null || m(r)).Select(r => r.Lo).ToList(); }
        // "<nhãn> <từ khác>": chỉ đúng nhãn BTP 50, không lẫn 41/50, 35, BTP 500
        Assert.Equal(new[] { "L1", "L2" }, Hit(tag50 + " mks"));
        Assert.Equal(new[] { "L1" }, Hit(tag50 + " MKS Nau 16tc"));
        // cả ô là nhãn: nhãn BẮT ĐẦU bằng; có dấu cách cuối → đúng nhãn
        Assert.Equal(new[] { "L1", "L2" }, Hit(tag50!));
        Assert.Equal(new[] { "L1", "L2" }, Hit(tag50 + " "));
        // 1 con số: ô nào ĐÚNG BẰNG số đó (Size "50"), không ra 41/50
        Assert.Equal(new[] { "L1", "L2" }, Hit("50"));
        // tìm thường: mỗi từ ở cột bất kỳ, bỏ dấu
        Assert.Equal(new[] { "L3" }, Hit("radong e.02"));
        Assert.Null(Vt.Matcher("  "));
    }

    [Fact]
    public void Warnings_other_positions_and_duplicate_same_day()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var i0 = Idx(A, "A.02.01", "L1");
        var w = Vt.Warnings(new() { new(i0, 5) }, DateTime.Today);
        Assert.Contains(w, x => x.Contains("A.10.01"));
        Vt.CreateSlip(new() { new(i0, 5) }, false, "", DateTime.Today, "");
        w = Vt.Warnings(new() { new(i0, 5) }, DateTime.Today);
        Assert.Contains(w, x => x.Contains("lưu 2 lần"));
    }

    [Fact]
    public void Import_rededucts_today_slips_and_skips_rows_already_reduced()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var i0 = Idx(A, "A.02.01", "L1"); var i1 = Idx(A, "A.10.01", "L1");
        Vt.CreateSlip(new() { new(i0, 10), new(i1, 6) }, false, "xuatxa", DateTime.Today, "");
        Thread.Sleep(5);
        // File mới: A.02.01 ĐÃ giảm đúng 10 (khớp → không trừ lại); A.10.01 chưa giảm (trừ lại 6); dòng mới đầu file làm lệch thứ tự.
        var f2 = File_("TON 21.11.xlsx",
            (B, TenB, "L9", "C.01.01", 9, 10),
            (A, TenA, "L1", "A.02.01", 40, 10.8),
            (A, TenA, "L1", "A.10.01", 30, 10.8),
            (A, TenA2, "L2", "A.02.01", 20, 10.8),
            (B, TenB, "L3", "B.01.03", 40, 10),
            (B, TenB, "L3", "B.01.03", 5, 10));
        var res = Vt.Import(Vt.ReadFile(f2));
        Assert.Equal(1, res.KeepSlips); Assert.Equal(1, res.KeepSkipped);
        Assert.NotNull(res.Recon);
        Assert.Equal(1, res.Recon!.Khop); Assert.Equal(1, res.Recon.ChuaGiam);
        Assert.Equal(40, Vt.Rows[Idx(A, "A.02.01", "L1")].Sl);
        Assert.Equal(24, Vt.Rows[Idx(A, "A.10.01", "L1")].Sl);
        Assert.True(Vt.Active(Vt.Slips[0]));
        Assert.Equal("21/11/" + (new DateTime(DateTime.Now.Year, 11, 21) - DateTime.Now > TimeSpan.FromDays(7) ? DateTime.Now.Year - 1 : DateTime.Now.Year), Vt.Info.DataDate);
    }

    [Fact]
    public void Older_slips_lock_on_new_import_unlock_rededucts_and_archive_groups_by_day()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var i0 = Idx(A, "A.02.01", "L1");
        var y = DateTime.Today.AddDays(-1);
        var s1 = Vt.CreateSlip(new() { new(i0, 3) }, false, "xuatxa", y, "");
        var s2 = Vt.CreateSlip(new() { new(3, 2) }, false, "xuatxa", y, "");
        var s3 = Vt.CreateSlip(new() { new(3, 1) }, false, "", y, "");
        var (groups, miss) = Vt.Archive();
        Assert.Single(groups); Assert.Equal(2, groups[0].Slips.Count); Assert.Equal(1, miss[y.ToString("yyyy-MM-dd")]);
        Assert.Equal(2, Vt.ArchiveItems(groups[0]).Count);
        Assert.Single(Vt.OpenSlips());

        Thread.Sleep(5);
        Vt.Import(Vt.ReadFile(F1()));
        Assert.Equal(50, Vt.Rows[i0].Sl);        // phiếu hôm qua không trừ lại vào file mới
        Assert.False(Vt.Active(Vt.Slips.Single(s => s.Id == s1.Id)));
        Assert.Throws<BusinessException>(() => Vt.BeginEdit(s1.Id));
        Vt.Unlock(s1.Id);
        Assert.Equal(47, Vt.Rows[i0].Sl);
        Assert.True(Vt.Active(Vt.Slips.Single(s => s.Id == s1.Id)));
    }

    /// <summary>1.90 (Trợ lý báo ArgumentOutOfRange ở ViTriView.RenderDraft): sửa phiếu có dòng không còn trong file → dòng tạm (ghost)
    /// ở cuối Rows; lưu xong PruneGhosts dọn dòng tạm → chỉ số trong phiếu đang soạn vượt Rows. DropStale phải bỏ các chỉ số đó.</summary>
    [Fact]
    public void Sua_phieu_co_dong_tam_luu_xong_bo_chi_so_cu_khoi_phieu_dang_soan()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var ib = Idx(B, "B.01.03", "L3");
        var s = Vt.CreateSlip(new() { new(ib, 4) }, false, "xuatxa", DateTime.Today, "");
        Thread.Sleep(5);
        Vt.Import(Vt.ReadFile(File_("TON 21.11.xlsx", (A, TenA, "L1", "A.02.01", 50, 10.8))));   // dòng B không còn
        Vt.Unlock(s.Id);
        var count = Vt.Rows.Count;
        var e = Vt.BeginEdit(s.Id);
        var ghost = e.Sel.Keys.Single();
        Assert.True(ghost >= count);                         // dòng tạm nằm sau dữ liệu thật
        var draft = new Dictionary<int, double>(e.Sel);     // = _draft của màn hình
        Vt.SaveEdit(e, draft.Select(kv => new Pick(kv.Key, kv.Value)).ToList(), false, "xuatxa", DateTime.Today, "");
        Assert.Equal(count, Vt.Rows.Count);                  // dòng tạm đã dọn → chỉ số cũ vượt Rows
        Assert.Throws<ArgumentOutOfRangeException>(() => draft.Where(kv => kv.Value > Vt.Rows[kv.Key].Sl).ToList());   // đúng lỗi cũ
        Assert.Equal(1, Vt.DropStale(draft));
        Assert.Empty(draft);
        Assert.Equal(0, Vt.DropStale(draft));
    }

    [Fact]
    public void Reconcile_detects_moved_goods()
    {
        var old = new List<Row> { Vt.Build(A, TenA, "", "L1", "A.01", 10, 1) };
        var nw = new List<Row> { Vt.Build(A, TenA, "", "L1", "B.07", 10, 1) };
        var mv = DoiViTri(old, nw);
        Assert.Single(mv); Assert.Equal("A.01", mv[0].Tu.Single()); Assert.Equal("B.07", mv[0].Den.Single());
        var rc = Reconcile(old, nw, new(), "a", "b");
        Assert.NotNull(rc); Assert.Single(rc!.Moves);
    }

    [Fact]
    public void Size_stats_count_today_and_draft()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var i0 = Idx(A, "A.02.01", "L1");
        Vt.CreateSlip(new() { new(i0, 4) }, false, "xuatxa", DateTime.Today, "");
        Vt.CreateSlip(new() { new(3, 2) }, false, "xuatxa", DateTime.Today.AddDays(-2), "");
        var t = Vt.SizeStats("today", new[] { new Pick(Idx(A, "A.10.01", "L1"), 3) });
        var lab = Vt.GopLabel(Vt.Rows[i0])!;
        var a = t.Single(x => x.Label == lab);
        Assert.Equal(4, a.Done); Assert.Equal(3, a.Draft);
        Assert.Equal(6, Vt.SizeStats("week").Sum(x => x.Done));
    }

    [Fact]
    public void Export_excel_uses_old_template()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var items = Vt.BuildView(Vt.Items(new[] { new Pick(0, 2), new Pick(3, 5) }), false);
        var path = Path.Combine(_dir, FileBase("xuatxa", new DateTime(2026, 11, 20)) + ".xlsx");
        Vt.ExportExcel(path, items, "phiếu xả", new DateTime(2026, 11, 20));
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet(1);
        Assert.Equal("phiếu xả : 20/11/2026", ws.Cell(1, 1).GetString());
        Assert.Equal("Mã hàng", ws.Cell(2, 1).GetString()); Assert.Equal("Hợp đồng", ws.Cell(2, 5).GetString());
        Assert.Equal("A.02.01", ws.Cell(3, 2).GetString());
        Assert.Equal(7, ws.Cell(5, 3).GetDouble());
        Assert.Equal("Times New Roman", ws.Cell(3, 1).Style.Font.FontName);
    }

    [Fact]
    public void Staff_with_view_only_cannot_import_or_create()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        _s.Auth.CreateUser("nv01", "NV", "matkhau1", Roles.Staff, Perm.Serialize(new Dictionary<string, string> { ["m7"] = Perm.View }));
        _s.Auth.Login("nv01", "matkhau1");
        var v = Reload();
        Assert.Equal(5, v.Rows.Count);
        Assert.False(v.CanEdit); Assert.False(v.CanExport);
        Assert.Throws<BusinessException>(() => v.CreateSlip(new() { new(0, 1) }, false, "", DateTime.Today, ""));
        Assert.Throws<BusinessException>(() => v.ReadFile(F1()));
        Assert.Equal(Perm.Export, Perm.Defaults(Roles.Staff)["m7"]);
    }

    // ---- 1.160
    private string XlFile(string name, params (string ma, DateTime ngay, string vt, double sl, string lenh)[] rows)
    {
        var p = Path.Combine(Path.GetTempPath(), "xg160_" + Guid.NewGuid().ToString("N") + "_" + name + ".xlsx");
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Sheet");
        ws.Cell(1, 1).Value = "Báo cáo - Kho - 07-Nhập xuất chi tiết";
        var h = new[] { "Đối tác", "Ngày chứng từ", "Ca", "Số lệnh", "NV Lập", "Item ID", "Mã hàng", "Tên hàng", "TLg Net", "TLg Gross", "Số lượng tồn", "Net", "Gross", "ĐVT", "Ngày SX", "Vị trí" };
        for (var c = 0; c < h.Length; c++) ws.Cell(3, c + 1).Value = h[c];
        ws.Cell(3, 17).Value = "Loại chứng từ";
        var r = 4;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = "TPC"; ws.Cell(r, 2).Value = x.ngay; ws.Cell(r, 4).Value = x.lenh; ws.Cell(r, 6).Value = r; ws.Cell(r, 7).Value = x.ma;
            ws.Cell(r, 8).Value = "Tôm (9 kg x 1 gói) - X - Y"; ws.Cell(r, 9).Value = 9; ws.Cell(r, 11).Value = x.sl; ws.Cell(r, 12).Value = x.sl * 9;
            ws.Cell(r, 15).Value = new DateTime(2025, 12, 1); ws.Cell(r, 16).Value = x.vt; ws.Cell(r, 17).Value = "Xuất xả\r\n"; r++;
        }
        ws.Cell(r, 11).FormulaA1 = "SUBTOTAL(9,K4:K" + (r - 1) + ")";
        wb.SaveAs(p); return p;
    }

    [Fact]
    public void Nap_Excel_kho_goc_tach_rieng_cong_don_va_doi_chieu_voi_phieu_app()
    {
        Vt.Load(); Vt.Import(Vt.ReadFile(F1()));
        var i0 = Idx(A, "A.02.01", "L1");
        var s = Vt.CreateSlip(new() { new(i0, 12) }, false, "xuatxa", DateTime.Today, "");
        var f1 = XlFile("a", (A, DateTime.Today, "A.02.01", 12, "L1"), (B, DateTime.Today, "B.01.03", 5, "L2"), (A, DateTime.Today.AddDays(-3), "A.02.01", 7, "L3"));
        var rd = Vt.ReadXuatGoc(f1);
        Assert.Equal(3, rd.Rows.Count);                                   // dòng tổng cuối file bị bỏ
        Assert.Equal("Xuất xả", rd.Rows[0].LoaiCt);                       // _x000d_ + xuống dòng được dọn
        var r1 = Vt.ImportXuatGoc(rd);
        Assert.Equal((3, 0, 3), (r1.Added, r1.Duplicate, r1.Total));
        // nạp lại file chồng nhau + 1 dòng mới → không nhân đôi
        var r2 = Vt.ImportXuatGoc(Vt.ReadXuatGoc(XlFile("b", (A, DateTime.Today, "A.02.01", 12, "L1"), (B, DateTime.Today, "B.01.03", 9, "L9"))));
        Assert.Equal((1, 1, 4), (r2.Added, r2.Duplicate, r2.Total));
        // 2 loại dữ liệu tách riêng; phiếu app không bị đụng
        Assert.Single(Vt.Slips); Assert.Single(Vt.DaXuatList(excel: false));
        Assert.Equal(4, Vt.DaXuatList(app: false).Count);
        var all = Vt.DaXuatList();
        Assert.Equal("Khớp Excel kho gốc", all.Single(x => x.Nguon == "app").DoiChieu);
        Assert.Contains(all, x => x.Nguon == "excel" && x.Sl == 12 && x.DoiChieu == "Khớp phiếu app");
        Assert.Equal(3, all.Count(x => x.Nguon == "excel" && x.DoiChieu == "Chỉ có ở Excel"));
        // lưu + mở lại
        Vt.Load(); Assert.Equal(4, Vt.XuatGoc.Count); Assert.Equal(DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd"), Vt.XuatGocInfo.From);
        Vt.ClearXuatGoc(); Vt.Load(); Assert.Empty(Vt.XuatGoc); Assert.Single(Vt.Slips);
    }

    [Fact]
    public void Doc_file_Excel_kho_goc_that_neu_co()
    {
        var p = Environment.GetEnvironmentVariable("XUATGOC_REAL"); if (string.IsNullOrEmpty(p) || !File.Exists(p)) return;
        Vt.Load(); var f = Vt.ReadXuatGoc(p);
        File.WriteAllText(p + ".out", $"rows={f.Rows.Count} skipped={f.Skipped} first={f.Rows[0].Ngay}|{f.Rows[0].MaGoc}|{f.Rows[0].Vt}|{f.Rows[0].Sl}|{f.Rows[0].NgaySx}|[{f.Rows[0].LoaiCt}]|{f.Rows[0].Dt}|{f.Rows[0].Hd}\nloai=" + string.Join(",", f.Rows.Select(x => x.LoaiCt).Distinct()));
        Assert.Equal(2468, f.Rows.Count);
    }

    [Fact]
    public void File_khong_dung_mau_bao_cot_thieu()
    {
        Vt.Load();
        var bad = Path.Combine(Path.GetTempPath(), "xg160_bad.xlsx");
        using (var wb = new XLWorkbook()) { wb.AddWorksheet("x").Cell(1, 1).Value = "Mã hàng"; wb.SaveAs(bad); }
        Assert.Contains("Ngày chứng từ", Assert.Throws<BusinessException>(() => Vt.ReadXuatGoc(bad)).Message);
    }
}

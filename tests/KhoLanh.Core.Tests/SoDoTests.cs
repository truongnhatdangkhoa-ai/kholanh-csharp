using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.SoDo;
using KhoLanh.Core.ViTri;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.SoDo.SoDoCore;

namespace KhoLanh.Core.Tests;

public class SoDoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-sd-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private const string A = "TPC-VRHLBTNXECR003140IME1", B = "TPC-VRHLBTNXXXR002630IME1", C = "TPC-VRHLBTNXECR002130IME1";
    private const string TenA = "Tôm sú BTP 31/40 (1.8 kg x 6 gói) - IQF", TenB = "Tôm thẻ 26/30 (2 kg x 5 gói) - IQF", TenC = "Tôm sú BTP 21/30 (1.8 kg x 6 gói) - IQF";

    public SoDoTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private SoDoService Sd => _s.SoDo;
    private SoDoService Fresh() { var v = new SoDoService(() => _s.Lan, _s.Session, _s.Audit, _s.Entry); v.Load(); return v; }

    /// <summary>Ghi thẳng bảng tồn theo vị trí (như sau khi nạp file ở module Tồn kho theo vị trí).</summary>
    private void Seed(params (string ma, string ten, string lo, string vt, double sl, double tl)[] rows)
    {
        var arr = new JsonArray(rows.Select(r => (JsonNode)new JsonObject
        {
            ["Mã hàng"] = r.ma, ["Tên hàng"] = r.ten, ["ĐVT"] = "Thùng", ["Lô (Batch)"] = r.lo, ["Vị trí"] = r.vt, ["SL Tồn"] = r.sl, ["T.Lượng"] = r.tl,
        }).ToArray());
        _s.Lan.Write(ViTriService.Db, new Dictionary<string, JsonArray?> { [ViTriService.SheetRows] = arr });
    }
    private static (string, string, string, string, double, double)[] Base(double bSl = 40) => new[]
    {
        (A, TenA, "L1", "TG1.A.01.1.1", 50.0, 10.8),
        (C, TenC, "L2", "TG1.A.01.1.1", 20.0, 10.8),
        (B, TenB, "L3", "TG1.A.01.1.2", bSl, 10.0),
        (C, TenC, "L2", "TG1.A.01.1.2", 5.0, 10.8),
        (A, TenA, "L1", "TG1.A.02.1.1", 10.0, 10.8),
        (B, TenB, "L4", "TG1.Z.01.1.1", 5.0, 10.0),
        (A, TenA, "L5", "KHO SAI", 1.0, 10.8),
    };
    /// <summary>Kho nhỏ: TG1 khu A — 2 dãy × 3 khoang × 2 tầng = 12 vị trí.</summary>
    private void Small()
    {
        Sd.Load();
        var c = new Cfg { ["TG1"] = new List<KhuCfg> { new() { Khu = "A", D = 2, K = 3, T = 2 } } };
        Sd.SaveConfig(c);
    }

    [Fact]
    public void Positions_config_and_floor_overrides()
    {
        var p = Parse("tg1.a.3.2.1")!;
        Assert.Equal(("TG1", "A", 3, 2, 1, false, "TG1.A.03.2.1"), (p.Kho, p.Khu, p.D, p.T, p.K, p.Z, p.Code));
        Assert.True(Parse("TG2.Z.01.1.1")!.Z);
        Assert.Null(Parse("TG1.A.01.8.1"));
        Assert.Null(Parse("A.02.01"));
        Assert.Equal("Kho 3", KhoLabel("TG3"));

        var td = ParseTd("3:6, 10-12:6; 99:9");
        Assert.Equal(new[] { 3, 10, 11, 12 }, td.Keys);
        Assert.Equal("3:6, 10-12:6", TdText(td));

        var d = DefaultCfg();
        Assert.Equal(new[] { "TG1", "TG2", "TG3", "TG4" }, d.Keys);
        Assert.Equal(9, d["TG1"].First(x => x.Khu == "A").D);
        var back = CleanCfg(CfgJson(d));
        Assert.Equal(CfgJson(d).ToJsonString(), CfgJson(back).ToJsonString());
        var e = new KhuCfg { Khu = "B", D = 5, K = 4, T = 5, Td = ParseTd("2:7") };
        Assert.Equal(7, TangOf(e, 2)); Assert.Equal(5, TangOf(e, 1)); Assert.Equal(NT, TangOf(null, 1));

        // dữ liệu vượt cấu hình → tự nới
        var (fc, notes) = FitCfg(new Cfg { ["TG1"] = new() { new() { Khu = "A", D = 1, K = 2, T = 2 } } }, new[] { Parse("TG1.A.03.4.3")!, Parse("TG5.Q.01.1.1")! });
        var a = fc["TG1"][0];
        Assert.Equal((3, 3, 4), (a.D, a.K, TangOf(a, 3)));
        Assert.True(fc.ContainsKey("TG5"));
        Assert.Equal(4, notes.Count);
    }

    [Fact]
    public void Build_counts_pallets_overflow_and_suggestions()
    {
        Seed(Base()); Small();
        var b = Sd.B;
        Assert.Single(b.Unparsed);
        var all = Sd.Scope("TG1", "ALL");
        Assert.Equal(12, all.Count);
        Assert.Equal(new Counts(12, 9, 1, 2, 1, 25), CountsOf(all, b));
        Assert.True(Sd.HasZ("TG1"));
        var z = Sd.Scope("TG1", "Z").Single();
        Assert.Equal(("TG1.Z.01.1.1", 0), (z.Code, z.Pal));

        var p = b.ByCode["TG1.A.01.1.1"];
        Assert.Equal("mm", Status(p));
        Assert.Equal(2, p.Pal);
        Assert.Equal(4, b.Tang[p.TangKey].Used);
        Assert.True(IsOver(p, b));
        Assert.Equal((133, "over"), FillLevel(p, b));
        Assert.Equal((33, "ok"), FillLevel(b.ByCode["TG1.A.02.1.3"], b));
        Assert.Equal(R2(50 * 10.8 + 20 * 10.8), p.Net);

        var sug = Suggest(p, b);
        Assert.Equal(3, sug.Count);
        Assert.Equal(("TG1.A.02.1.1", true), (sug[0].Code, sug[0].Same));   // tầng có cùng mã được ưu tiên

        // lọc
        Assert.Equal(3, all.Count(x => Pass(x, new Filter { St = "ov" }, b, _ => "")));
        Assert.Equal(1, all.Count(x => Pass(x, new Filter { St = "u" }, b, _ => "")));
        Assert.Equal(2, all.Count(x => Pass(x, new Filter { Q = "vrhlbtnxecr002130ime1" }, b, q => TextOf(q, Sd.Label))));
        Assert.Equal(1, all.Count(x => Pass(x, new Filter { Q = "26 / 30" }, b, q => TextOf(q, Sd.Label))));
        Assert.Equal(3, all.Count(x => Pass(x, new Filter { D = 2, T = 1 }, b, _ => "")));

        // mã rút gọn
        Assert.EndsWith("(1.8kg x6)", Sd.Label(A, TenA));
        Assert.Contains("31/40", Sd.Label(A, TenA));
        Assert.NotEqual(Sd.GrpOf(A, TenA), Sd.GrpOf(C, TenC));
        Assert.Equal("VRHLBTNXECR003140IME1", KeyMa(A));
        Assert.Equal((1.8, 6, "1.8kg x6"), GoiY(TenA));
    }

    [Fact]
    public void Quy_cach_is_saved_and_used_for_totals()
    {
        Seed(Base()); Small();
        Assert.Equal(3, MissingQc(Sd.B.Pos, Sd.QcOf).Count);
        Assert.Throws<BusinessException>(() => Sd.SaveQc(new[] { (A, "gói", 0) }));
        Assert.Equal(2, Sd.SaveQc(new[] { (A, "gói", 6), (B, "kiện", 0) }));
        var v = Fresh();
        Assert.Equal(("gói", 6), (v.QcOf(A)!.Dv, v.QcOf(A)!.Gk));
        Assert.Equal("kiện", v.DvOf(B));
        Assert.Single(MissingQc(v.B.Pos, v.QcOf));
        Assert.Equal(new List<(string, double)> { ("gói", 10) }, v.B.ByCode["TG1.A.02.1.1"].Tot);
        Assert.Equal(2, KienOf(12, v.QcOf(A)));
        Assert.Equal(2, v.Config["TG1"][0].T);   // cấu hình đọc lại đúng
    }

    [Fact]
    public void Lock_keeps_snapshot_and_lists_changes_until_unlocked()
    {
        Seed(Base()); Small();
        var added = Sd.AddLocks(new[] { ("TG1", "A", 1), ("TG1", "A", 1) }, "kiểm kê tháng 11");
        Assert.Single(added);
        Assert.Throws<BusinessException>(() => Sd.AddLocks(new[] { ("TG1", "A", 1) }, ""));
        Assert.Equal("Kho 1 · Khu A · Dãy 1", SoDoService.LockName(added[0]));

        Seed(Base(bSl: 30));       // trong lúc khóa: xuất 10 thùng B ở dãy 1
        var v = Fresh();
        Assert.True(v.IsLocked("TG1", "A"));
        Assert.Equal(40, v.B.ByCode["TG1.A.01.1.2"].It.First(x => x.Ma == KeyMa(B)).Sl);   // sơ đồ giữ số lúc khóa
        var d = Assert.Single(v.LockDiff);
        Assert.Equal(("TG1.A.01.1.2", 40.0, 30.0, -10.0, "Xuất"), (d.Vt, d.Was, d.Now, d.D, d.Kind));

        v.Unlock(added[0].Id);
        Assert.Empty(v.LockDiff);
        Assert.Equal(30, v.B.ByCode["TG1.A.01.1.2"].It.First(x => x.Ma == KeyMa(B)).Sl);
        Assert.Empty(Fresh().Locks);
    }

    [Fact]
    public void Diff_rows_kinds()
    {
        var snap = new List<StockRow> { new() { Ma = A, Vt = "TG1.A.01.1.1", Sl = 5 }, new() { Ma = B, Vt = "TG1.A.01.1.2", Sl = 3 } };
        var now = new List<StockRow> { new() { Ma = A, Vt = "TG1.A.1.1.1", Sl = 8 }, new() { Ma = C, Vt = "TG1.A.01.1.3", Sl = 2 } };
        var d = DiffRows(snap, now, "k1");
        Assert.Equal(new[] { "Nhập", "Hết hàng", "Hàng mới" }, d.Select(x => x.Kind));
    }

    [Fact]
    public void Phieu_in_builds_moves_merges_and_undoes()
    {
        Seed(Base().Append((A, TenA, "L9", "TG1.A.02.1.1", 7.0, 10.8)).ToArray()); Small();
        var k = Sd.BuildKk("TG1", "A", 1, "so");
        var pg = Assert.Single(k.Pages);
        Assert.Equal(8, pg.Rows.Count);
        Assert.Equal(4, pg.Rows.Count(r => r.Ma == "trống"));
        var c = pg.Rows.Single(r => r.Code == "TG1.A.01.1.1" && r.M0 == KeyMa(C));
        Assert.Equal("TG1.A.01.1.3", c.Mv);
        Assert.Equal(1, k.MoveCount);
        Assert.Equal("20 kiện", c.Sl);

        k.Snap();
        Assert.True(SoDoService.Move(k, c));
        Assert.Equal(7, pg.Rows.Count);
        Assert.Equal("TG1.A.01.1.3", c.Code);
        Assert.Contains("chuyển từ TG1.A.01.1.1", c.Ghi);
        Assert.Equal(0, k.MoveCount);
        Assert.True(k.Undo());
        Assert.Equal(8, k.Pages[0].Rows.Count);

        var k2 = Sd.BuildKk("TG1", "A", 2, "so");
        Assert.Equal(1, SoDoService.MergeCount(k2));
        Assert.Equal(1, SoDoService.Merge(k2));
        Assert.Equal("17 kiện", k2.Pages[0].Rows.First(r => r.Code == "TG1.A.02.1.1").Sl);

        var all = Sd.BuildKk("TG1", "A", 0, "trong");
        Assert.Equal(2, all.Pages.Count);
        Assert.Equal(6, all.Pages[1].Rows.Count);

        // mã đầy đủ thay vì rút gọn
        Assert.Equal(KeyMa(A), Sd.BuildKk("TG1", "A", 2, "so", rut: false).Pages[0].Rows[0].Ma);

        var path = Path.Combine(_dir, "phieu.xlsx");
        Sd.ExportKk(path, k);
        Assert.True(k.Out);
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Day 01");
        Assert.StartsWith("PHIẾU IN VỊ TRÍ", ws.Cell(1, 1).GetString());
        Assert.Equal("TG1.A.01.1.1", ws.Cell(4, 1).GetString());
        Assert.Equal(XLPaperSize.A4Paper, ws.PageSetup.PaperSize);
    }

    [Fact]
    public void Nhap_lieu_round_trip_and_review()
    {
        Seed(Base()); Small();
        var page = Sd.ToNhapLieu("TG1", "A", 1);
        Assert.StartsWith("Kho 1 A dãy 01", page.Name);
        var rows = _s.Entry.Load(page);
        Assert.Equal(8, rows.Count);
        var cols = page.Columns.Select(c => c.Header).ToList();
        int cVt = cols.IndexOf("Vị trí"), cMa = cols.IndexOf("Mã hàng"), cSl = cols.IndexOf("Số lượng"), cGc = cols.IndexOf("Ghi chú");
        Assert.Equal("lô L1", rows[0][cGc]);
        Assert.Equal("50", rows[0][cSl]);

        rows[0][cSl] = "45";                                    // thực tế thiếu 5
        var extra = new string[cols.Count]; Array.Fill(extra, "");
        extra[cVt] = "TG1.A.01.2.1"; extra[cMa] = "VRXYZ"; extra[cSl] = "3";
        rows.Add(extra);                                         // hàng ngoài sổ
        _s.Entry.Save(page, page.Columns, rows.Select(r => (IReadOnlyList<string>)r).ToList());

        var res = Sd.ReviewDay("TG1", "A", 1);
        Assert.Equal(page.Name, res.PageName);
        Assert.Equal(2, res.R.N);
        Assert.Equal(1, res.R.Extra);
        Assert.Equal(-2, res.R.Dsl);
        Assert.Equal(R2(-5 * 10.8), res.R.Dnet);
        Assert.Contains("TG1.A.01.1", res.R.OverT);

        Assert.Throws<BusinessException>(() => Sd.ReviewDay("TG1", "A", 2));
        var path = Path.Combine(_dir, "cl.xlsx");
        Sd.ExportReview(path, res);
        using var wb = new XLWorkbook(path);
        Assert.Equal(3, wb.Worksheet(1).LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void Export_list_and_permissions()
    {
        Seed(Base()); Small();
        var path = Path.Combine(_dir, "ds.xlsx");
        Sd.ExportList(path, Sd.Scope("TG1", "ALL"));
        Assert.True(File.Exists(path));
        Assert.Equal("m8", ModuleBridge.PermKey("so-do-vi-tri"));
        Assert.Contains(Perm.Items, x => x.Key == "m8");
    }
}

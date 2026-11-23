using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.KhangSinh;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.KhangSinh.KhangSinhCore;

namespace KhoLanh.Core.Tests;

public class KhangSinhTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-ks-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public KhangSinhTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }
    private KhangSinhService Ks => _s.KhangSinh;
    private static Dictionary<string, string> In(params (string k, string v)[] x) => FieldKeys.ToDictionary(k => k, k => x.FirstOrDefault(p => p.k == k).v ?? "");

    [Fact]
    public void Rules_and_best_case_match_old_app()
    {
        var rules = DefaultRules();
        Assert.Equal(83, rules.Count);
        Assert.Equal("01.N-AS2-H", rules[0].KyHieu);
        Assert.Equal(new Cond('<', 0.5), ParseCond("< 0,5"));
        Assert.Equal('N', ParseCond("ND").Type);
        Assert.True(IsBlank(" nd ")); Assert.True(IsBlank(""));
        // ví dụ trong ghi chú của app cũ (PC 7.91)
        Assert.Equal("45.AS1-H-M", PickBest(rules, In(("oxy", "10")))!.KyHieu);
        Assert.Equal("01.N-AS2-H", PickBest(rules, In(("cipro", "4.83")))!.KyHieu);
        Assert.Equal("41.AS1-H", PickBest(rules, In(("oxy", "10"), ("cipro", "4.83")))!.KyHieu);
        Assert.Equal("52.KĐ", PickBest(rules, In(("oxy", "95"), ("enro", "3")))!.KyHieu);
        // 3.004 bộ chỉ tiêu ngẫu nhiên đã chạy bằng đúng hàm ksPickBest của app cũ (node) → phải ra cùng ký hiệu
        var fx = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "ks-pick-fixture.json")))!.AsArray();
        var bad = new List<string>();
        foreach (var x in fx)
        {
            var got = PickBest(rules, InputsOf(x!["i"]))?.KyHieu;
            var want = x["k"]?.GetValue<string>();
            if (got != want) bad.Add($"{x["i"]!.ToJsonString()} → {got} (cũ: {want})");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad.Take(10)));
        var r = PickBest(rules, In(("enro", "5")))!;
        Assert.Contains(AutoPassNotes(r, In(("enro", "5"))), n => n.StartsWith("Cipro: chưa phát hiện"));
        Assert.Equal("red", Tone(In(("aoz", "0.3")))); Assert.Equal("green", Tone(In())); Assert.Equal("amber", Tone(In(("oxy", "1"))));
    }

    [Fact]
    public void Quick_search_like_old_app()
    {
        Assert.Equal("1", LoKey("L01")); Assert.Equal("1", LoKey("Lô 1")); Assert.Equal("10", LoKey("lo10"));
        Assert.Equal((true, 2), MauInfo("MKS chờ 2 ngày 09.05 · LSX: 053/05"));
        Assert.Equal((true, 3), MauInfo("MCS3"));
        Assert.Equal((false, (int?)null), MauInfo("ISHIMITSU · Sushi"));
        Assert.Equal("dong lanh", NormText("  Đông Lạnh "));
        var rec1 = Rec("2026-05-08", "1", lan1Ngay: "2026-05-08");
        var rec10 = Rec("2026-05-08", "10");
        var rec2 = Rec("2026-05-03", "2", lan2: (date: "2026-05-09", mau: "MKS chờ 2 ngày 09.05 · Lô: 2"));
        var rows = BaseRows(new[] { rec1, rec10, rec2 }, ModeL1);
        Assert.Equal(new[] { "1" }, rows.Where(Match("L1").Test).Select(r => r.Batch));
        Assert.Equal(new[] { "1", "10" }, rows.Where(Match("8/5/2026").Test).Select(r => r.Batch));
        Assert.Equal(new[] { "2" }, rows.Where(Match("9.5.26").Test).Select(r => r.Batch));   // ngày kiểm lại cũng tìm được
        var m = Match("MKS2 9.5.26"); Assert.True(m.MksMode);
        var kl = BaseRows(new[] { rec1, rec10, rec2 }, ModeKl);
        Assert.Single(kl); Assert.Equal(2, kl[0].LanNo);
        Assert.True(m.Test(kl[0])); Assert.False(Match("MKS3").Test(kl[0]));
        Assert.Contains("Lô 2", kl[0].Note);
        Assert.Equal(new[] { "1", "10", "2" }, Sort(rows, ModeL1).Select(r => r.Batch));
    }

    private static Rec Rec(string date, string batch, string? lan1Ngay = null, (string date, string mau)? lan2 = null)
    {
        var j = new JsonObject { ["date"] = date, ["batch"] = batch, ["note"] = "", ["inputs"] = InputsJson(In()), ["dat"] = "ĐẠT MKS", ["kyhieu"] = "23.MKS", ["savedAt"] = "2026-10-02T09:00:00.000Z" };
        if (lan1Ngay is not null) { j["source"] = "lan1-import"; j["lan1Src"] = new JsonObject { ["file"] = "KQKS KHO.pdf", ["ngayKiem"] = lan1Ngay, ["stt"] = 1 }; }
        if (lan2 is { } l)
        {
            var e = new JsonObject { ["dat"] = "ĐẠT MKS", ["kyhieu"] = "23.MKS", ["inputs"] = InputsJson(In()), ["mau"] = l.mau, ["date"] = l.date, ["so"] = "09/KS 05", ["file"] = "KQKS MKS chờ 2.pdf", ["stt"] = 1 };
            j["lan2"] = e.DeepClone(); j["lan2All"] = new JsonArray(e);
        }
        return new Rec { Key = $"s:batch:{date}:{batch}:x", J = j };
    }

    [Fact]
    public void Service_save_delete_rules_and_excel()
    {
        Ks.Load();
        Assert.Empty(Ks.Records);
        var e1 = Ks.MakeEntry("08/05/2026", "1", "PHI X2L1", In(("oxy", "10")));
        Assert.Equal("2026-05-08", e1.Date); Assert.Equal("45.AS1-H-M", e1.Rule!.KyHieu);
        Ks.SaveEntries(new[] { e1, Ks.MakeEntry("09/05/2026", "2", "", In()) });
        Assert.True(Ks.Exists("2026-05-08", "1"));
        Assert.Throws<BusinessException>(() => Ks.SaveEntries(new[] { Ks.MakeEntry("x", "3", "", In()) }));
        // ghi đúng dạng dòng {key, value} của app cũ
        var raw = JsonNode.Parse(_s.Lan.Read(KhangSinhService.Db, KhangSinhService.Sheet))!.AsArray();
        Assert.Equal(2, raw.Count);
        Assert.StartsWith("s:batch:2026-05-08:1:", raw.Select(x => x!["key"]!.ToString()).First(k => k.Contains(":1:")));
        var again = new KhangSinhService(() => _s.Lan, _s.Session, _s.Audit); again.Load();
        Assert.Equal(2, again.Records.Count);
        Assert.Equal("45.AS1-H-M", again.Records.Single(r => r.Batch == "1").KyHieu);

        // quy định: sửa, lỗi dạng, khôi phục
        var rules = Ks.Rules.Select(r => Rule.FromJson(r.ToJson())).ToList();
        rules[0].Dat = "ĐẠT THỬ"; rules.RemoveAt(82);
        Ks.SaveRules(rules);
        Assert.Equal(82, Ks.Rules.Count); Assert.True(Ks.RulesCustom); Assert.Equal("ĐẠT THỬ", Ks.Rules[0].Dat);
        rules[1].C["oxy"] = "abc";
        Assert.Throws<BusinessException>(() => Ks.SaveRules(rules));
        Ks.ResetRules(); Assert.Equal(83, Ks.Rules.Count); Assert.False(Ks.RulesCustom);

        // Excel: 1 sheet = 1 ngày, dòng công ty phía trên, bỏ dòng trùng
        var path = Path.Combine(_dir, "ks.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("08.05.2026");
            ws.Cell(1, 1).Value = "CÔNG TY ABC";
            string[] h = { "STT", "Lô hàng", "Code/Mã", "Enro", "Cipro", "Oxy", "AOZ", "Ghi chú" };
            for (var i = 0; i < h.Length; i++) ws.Cell(3, i + 1).Value = h[i];
            ws.Cell(4, 1).Value = 1; ws.Cell(4, 2).Value = "1"; ws.Cell(4, 6).Value = 10;            // trùng lô 1 ngày 08/05 → bỏ
            ws.Cell(5, 1).Value = 2; ws.Cell(5, 2).Value = "5"; ws.Cell(5, 3).Value = "PHI X2L5"; ws.Cell(5, 5).Value = "4.83";
            ws.Cell(6, 1).Value = 3; ws.Cell(6, 6).Value = 3;                                             // thiếu lô
            wb.AddWorksheet("Khong ngay").Cell(1, 1).Value = "Lô hàng"; wb.Worksheet(2).Cell(1, 2).Value = "Enro"; wb.Worksheet(2).Cell(2, 1).Value = "9";
            wb.SaveAs(path);
        }
        var sheets = KhangSinhService.ReadExcel(path);
        Assert.Equal(2, sheets[0].HeaderIdx); Assert.Equal("2026-05-08", sheets[0].Date); Assert.Equal("", sheets[1].Date);
        var map = GuessMapping(sheets[0].Headers);
        Assert.Equal((1, 2, -1, 3, 5), (map["batch"], map["code"], map["date"], map["enro"], map["oxy"]));
        var res = Ks.ImportSheets(sheets, map, skipDup: true);
        Assert.Equal((1, 1, 1), (res.Ok, res.Dup, res.Skip));
        Assert.Equal(new[] { "Khong ngay" }, res.SkippedSheets);
        var r5 = Ks.Records.Single(r => r.Batch == "5");
        Assert.Equal("01.N-AS2-H", r5.KyHieu); Assert.Equal("PHI X2L5", r5.Note); Assert.Equal("excel-import-sheet:08.05.2026", r5.Source);

        // xóa theo ngày (lần 1), xuất Excel
        Assert.Equal(2, Ks.DeleteDay(ModeL1, "2026-05-08"));
        Assert.Single(Ks.Records);
        var outp = Path.Combine(_dir, "out.xlsx");
        Ks.Export(outp, Sort(BaseRows(Ks.Records, ModeL1), ModeL1), ModeL1);
        using (var wb = new XLWorkbook(outp)) Assert.Equal("2", wb.Worksheet(1).Cell(2, 2).GetString());
        Assert.Equal(1, Ks.ClearAll()); Assert.Empty(Ks.Records);
    }

    [Fact]
    public void Lan2_rows_delete_by_phieu_month_and_legacy_import()
    {
        // database app cũ: sheet tong-hop/KhangSinh (dòng {key, value}) → app mới ket-qua-khang-sinh/KhangSinh
        var old = Path.Combine(_dir, "khokhanh.db");
        var r1 = Rec("2026-05-08", "4", lan1Ngay: "2026-05-08", lan2: ("2026-05-09", "MKS chờ 1 ngày 09.05 · Lô: 4")).J.ToJsonString();
        var r2 = Rec("2026-03-02", "MCH 035/26", lan2: ("2026-03-02", "MCH 035/26")).J; r2["source"] = "lan2-import";
        using (var c = new SqliteConnection($"Data Source={old}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE cols(tab TEXT NOT NULL, key TEXT NOT NULL, title TEXT, width INTEGER, ord INTEGER, PRIMARY KEY(tab,key));
                CREATE TABLE rows(tab TEXT NOT NULL, id TEXT NOT NULL, cells TEXT NOT NULL, meta TEXT NOT NULL, seq INTEGER NOT NULL, del INTEGER NOT NULL DEFAULT 0, born REAL NOT NULL, PRIMARY KEY(tab,id));
                CREATE TABLE sheets(tab TEXT PRIMARY KEY, ord INTEGER NOT NULL DEFAULT 0);
                INSERT INTO sheets VALUES('tong-hop/KhangSinh',0),('so-do-vi-tri/CauHinh',0),('ton-vi-tri-an-an/ViTri',0),('so-lieu-kiem-ke/TonChot',0),('so-lieu-kiem-ke/GopKKTrangThai',0);
                INSERT INTO cols VALUES('tong-hop/KhangSinh','key','key',0,0),('tong-hop/KhangSinh','value','value',0,1);
                INSERT INTO rows VALUES('so-do-vi-tri/CauHinh','c','{"data":"x"}','{}',1,0,1.0),('ton-vi-tri-an-an/ViTri','v','{"Vị trí":"TG1.A.01.1.1"}','{}',1,0,1.0),
                                       ('so-lieu-kiem-ke/TonChot','t','{"c0":"1"}','{}',1,0,1.0),('so-lieu-kiem-ke/GopKKTrangThai','g','{"a":1}','{}',1,0,1.0);
                """;
            cmd.ExecuteNonQuery();
            using var ins = c.CreateCommand();
            ins.CommandText = "INSERT INTO rows VALUES('tong-hop/KhangSinh',@id,@cells,'{}',1,0,@born)";
            var n = 0;
            foreach (var (k, v) in new[] { ("s:batch:2026-05-08:4:a", r1), ("s:batch:2026-03-02:MCH 035/26:b", r2.ToJsonString()) })
            {
                ins.Parameters.Clear();
                ins.Parameters.AddWithValue("@id", "r" + n); ins.Parameters.AddWithValue("@born", ++n);
                ins.Parameters.AddWithValue("@cells", new JsonObject { ["key"] = k, ["value"] = v }.ToJsonString());
                ins.ExecuteNonQuery();
            }
        }
        SqliteConnection.ClearAllPools();
        var imp = _s.Legacy.Import(old);
        Assert.Equal(4, imp.Sheets);   // KhangSinh, CauHinh, ViTri, TonChot (không lấy GopKKTrangThai)
        Assert.DoesNotContain("batch", _s.Lan.Read("tong-hop", "KhangSinh"));   // không ghi vào Tổng hợp
        Assert.Contains("TG1.A.01.1.1", _s.Lan.Read("ton-vi-tri-an-an", "ViTri"));

        Ks.Load();
        Assert.Equal(2, Ks.Records.Count);
        var l1 = BaseRows(Ks.Records, ModeL1); var kl = BaseRows(Ks.Records, ModeKl);
        Assert.Equal(new[] { "4" }, l1.Select(r => r.Batch));        // bản ghi chỉ có kiểm lại không hiện ở Lần 1
        Assert.Equal(2, kl.Count);
        Assert.Contains("Lần 2", Lan2Text(l1[0]));
        var ph = Ks.CollectPhieu();
        Assert.Equal(3, ph.Count);
        Assert.Equal(new[] { ("2026-05", 1, 1) }, Ks.Months(ModeL1));
        // xóa lần kiểm lại của lô 4 → lần 1 còn; xóa phiếu lần 2 của MCH → xóa hẳn bản ghi
        Ks.DeleteRow(kl.Single(r => r.ParentBatch == "4"));
        Assert.Empty(Ks.Records.Single(r => r.Batch == "4").Lan2);
        Ks.DeletePhieu(Ks.CollectPhieu().Single(p => p.Kind == 2));
        Assert.Single(Ks.Records);
        Assert.Equal(1, Ks.DeleteMonth(ModeL1, "2026-05"));
        Assert.Empty(Ks.Records);
    }
}

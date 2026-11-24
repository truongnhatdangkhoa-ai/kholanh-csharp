using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.DanhMuc;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.DanhMuc.DanhMucMa;

namespace KhoLanh.Core.Tests;

/// <summary>
/// Danh mục mã hàng: đối chiếu lõi C# với CHÍNH danh-muc-ma-hang.js của app cũ (Data/dm-golden.json do `node Data/dm-golden.gen.js` sinh):
/// giải mã 10 đoạn, diễn giải tự dựng, trùng mã, nghi gõ sai, đoạn chưa khai báo, đếm nơi đang dùng, mục Cần xử lý.
/// </summary>
public class DanhMucTests : IDisposable
{
    private static JsonObject G() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "dm-golden.json")))!.AsObject();
    private static SegMap SegOf(JsonObject g) => SegMap.Build(g["fixed"]!.AsArray().Select(x => (Str(x!["code"]), Str(x["name"]))), g["maHoa"]!.AsArray());

    [Fact]
    public void Giai_ma_kiem_tra_dang_dung_giong_app_cu()
    {
        var g = G(); var seg = SegOf(g);
        var codes = g["data"]!.AsArray().Select(r => NormCode(Str(r!["Mã"]))).ToList();
        Assert.Equal(g["codes"]!.AsArray().Select(x => Str(x)), codes);
        var dec = g["decode"]!.AsArray();
        for (var i = 0; i < codes.Count; i++)
        {
            var exp = dec[i]!.AsArray(); var got = Decode(seg, codes[i]);
            for (var j = 0; j < 10; j++)
            {
                var e = exp[j]!.AsArray();
                Assert.Equal(Str(e[1]), got[j].Code); Assert.Equal(e[2] is null ? null : Str(e[2]), got[j].Name); Assert.Equal(e[3]!.GetValue<bool>(), got[j].Ok);
            }
        }
        Assert.Equal(g["note"]!.AsArray().Select(x => Str(x)), codes.Select(c => NoteOf(seg, c)));
        var chk = Check(seg, codes);
        var st = g["status"]!.AsArray();
        for (var i = 0; i < codes.Count; i++)
        {
            var s = StatusOf(seg, chk, codes[i]);
            if (st[i] is null) { Assert.Null(s); continue; }
            Assert.Equal(Str(st[i]![0]), s!.Level); Assert.Equal(st[i]![1]!.AsArray().Select(x => Str(x)), s.Msgs);
        }
        var use = CountUse(UseSources.Select(u => (u.Tag, g["useSrc"]![u.Db + "/" + u.Sheet]!.DeepClone() as JsonArray)));
        Assert.Equal(g["useCount"]!.AsArray().Select(x => $"{Str(x![0])}|{x[1]}|{x[2]}|{x[3]}"), use.Select(x => $"{x.Key}|{x.Value.M01}|{x.Value.M02}|{x.Value.SK}"));
        var ut = codes.Append("NEWWBTNXEC001162010O1").Select(c => UseText(use, c));
        Assert.Equal(g["useText"]!.AsArray().Select(x => Str(x)), ut);
        var xl = XlData(seg, chk, use, codes);
        Assert.Equal(g["xl"]!["missing"]!.AsArray().Select(x => Str(x)), xl.Missing);
        Assert.Equal(g["xl"]!["dups"]!.AsArray().Select(x => $"{Str(x![0])}×{x[1]}"), xl.Dups.Select(d => $"{d.Code}×{d.N}"));
        Assert.Equal(g["xl"]!["near"]!.AsArray().Select(x => $"{Str(x![0])}>{Str(x[1])}"), xl.Near.Select(d => $"{d.Code}>{d.Other}"));
        Assert.Equal(g["xl"]!["miss"]!.AsArray().Select(x => Str(x)), xl.Miss);
    }

    // ------------------------------------------------------------------ service

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-dm-" + Guid.NewGuid().ToString("N"));
    private AppServices? _s;
    public void Dispose() { _s?.Lan.Dispose(); SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Sua_them_xoa_them_ma_dang_dung_xuat_excel()
    {
        Directory.CreateDirectory(_dir);
        var s = _s = new AppServices(Path.Combine(_dir, "kho.db"));
        s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); s.Auth.Login("admin", "matkhau1");
        const string a = "VRHLBTNXEC00116201001", b = "VRHLBTNXEC00121301001", c = "PRHLXXXXXXXXXXXXX0502";
        s.Lan.Write("tong-hop", new Dictionary<string, JsonArray?>
        {
            ["MaDaTao"] = new JsonArray(new JsonObject { ["STT"] = 1, ["Mã"] = a, ["Ghi chú"] = "Tôm sú" }, new JsonObject { ["STT"] = 2, ["Mã"] = b, ["Ghi chú"] = "" }),
            ["MaHoa"] = new JsonArray(new JsonObject { ["Tổ hợp"] = 8, ["Mã"] = "1620", ["Tên"] = "16/20" }, new JsonObject { ["Tổ hợp"] = 9, ["Mã"] = "05", ["Tên"] = "0.5 kg" }),
        });
        s.Lan.Write("ton-kho-an-an", new Dictionary<string, JsonArray?> { ["LoadData"] = new JsonArray(new JsonObject { ["c1"] = "TPC-" + a }, new JsonObject { ["c1"] = c }) });
        var dm = s.DanhMuc; dm.Load();
        Assert.Equal(new[] { a, b }, dm.Rows.Select(r => r.Code));
        Assert.Equal("Tồn An An 1", dm.UseOf(a));
        Assert.Equal(new[] { c }, dm.XlNow().Missing);
        // sửa dòng 2, thêm dòng mới sau dòng 1 (diễn giải tự dựng), rồi xóa dòng 1
        var id1 = dm.Rows[0].Id; var id2 = dm.Rows[1].Id;
        dm.Apply(new (string?, string, string, string?)[] { (id2, b, "Tôm sú 21/30", null), (null, " prhl xxxx xxxxxxxxx 0502 ", "", id1) }, Array.Empty<string>(), "Sửa trên lưới");
        Assert.Equal(new[] { a, c, b }, dm.Rows.Select(r => r.Code));
        Assert.Contains("0.5 kg", dm.Rows[1].Note);
        dm.Apply(Array.Empty<(string?, string, string, string?)>(), new[] { id1 }, "Xóa dòng");
        dm.Load();
        Assert.Equal(new[] { c, b }, dm.Rows.Select(r => r.Code));
        Assert.Equal("Tôm sú 21/30", dm.Rows[1].Note);
        var raw = JsonNode.Parse(s.Lan.Read("tong-hop", "MaDaTao"))!.AsArray();
        Assert.Equal(1, raw[0]!["Đủ mã"]!.GetValue<int>()); Assert.Equal(2, raw[1]!["STT"]!.GetValue<int>());
        Assert.StartsWith("__JSON__", JsonNode.Parse(s.Lan.Read("tong-hop", "M8_Meta"))![0]!["data"]!.ToString());
        Assert.Equal(new[] { a }, dm.XlNow().Missing);
        Assert.Equal(1, dm.AddMissing(dm.XlNow().Missing)); Assert.Empty(dm.XlNow().Missing);
        Assert.Equal(3, s.TongHop.Results.Count);   // Tổng hợp đang nạp được cập nhật theo
        Assert.Single(dm.Visible("21/30", "all")); Assert.Empty(dm.Visible("", "trung"));
        var path = Path.Combine(_dir, "dm.xlsx"); dm.Export(path);
        using var wb = new XLWorkbook(path); var ws = wb.Worksheet(1);
        Assert.Equal("Mã hàng", ws.Cell(1, 2).GetString()); Assert.Equal("16/20", ws.Cell(4, 4).GetString()); Assert.Equal("Tồn An An 1", ws.Cell(4, 6).GetString());
    }
}

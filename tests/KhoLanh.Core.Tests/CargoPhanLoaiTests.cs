using System.Text.Json.Nodes;
using KhoLanh.Core.KiemKe;
using KhoLanh.Core.PhieuKho;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.PhieuKho.CargoCore;

namespace KhoLanh.Core.Tests;

/// <summary>1.128: phiếu nội địa gợi ý theo phân loại Tồn kho An An (Loại hàng ↔ phân loại, Chọn từ tồn lọc phân loại, gợi ý ô Size-quy cách).</summary>
public class CargoPhanLoaiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-cgpl-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public CargoPhanLoaiTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private const string Btp40 = "VRHLBTNXXXR00" + "0040" + "IME1" + "0001";
    private const string Btp50 = "VRHLBTNXXXR00" + "0050" + "IME1" + "0001";
    private const string Nl130 = "VRHOITNXXXR00" + "0130" + "JPA1" + "0001";

    [Fact]
    public void Loai_hang_gan_phan_loai_mac_dinh_va_sua_duoc()
    {
        var cfg = NormCfg(DefaultCfg());
        Assert.Equal(("hlso", "tomnl", ""), (NhomOf(cfg, "BTP"), NhomOf(cfg, "nl"), NhomOf(cfg, "XYZ")));
        Assert.Equal("hlso", ParseNhom("Tôm HLSO")); Assert.Equal("tomnl", ParseNhom("nguyên liệu")); Assert.Equal("-", ParseNhom("(không gắn)"));
        Assert.Equal("", ParseNhom("  ")); Assert.Null(ParseNhom("Kho lạ"));
        // trùng mặc định → lưu "" (theo mặc định); đổi / bỏ gắn → giữ, đi qua JSON (sheet PNX_CargoCfg)
        var c = new CargoCfg { Loai = { new() { Code = "BTP", Ten = "HLSO", Nhom = "hlso" }, new() { Code = "NL", Ten = "HOSO", Nhom = "-" }, new() { Code = "TGC", Nhom = "tomgs" } } };
        var n = CfgFromJson(CfgToJson(NormCfg(c)))!;
        Assert.Equal(("", "-", "tomgs"), (n.Loai[0].Nhom, n.Loai[1].Nhom, n.Loai[2].Nhom));
        Assert.Equal(("hlso", "", "tomgs"), (NhomOf(n, "BTP"), NhomOf(n, "NL"), NhomOf(n, "TGC")));
        // danh mục cũ (không có trường nhom) → mặc định
        var old = CfgFromJson("{\"loai\":[{\"code\":\"TGS\",\"ten\":\"TGS\"}],\"qc\":[]}")!;
        Assert.Equal("tomgs", NhomOf(NormCfg(old), "TGS"));
    }

    private void SeedAnAn()
    {
        var headers = new[] { "Mã hàng", "Tên hàng", "Đặc tính", "Tồn cuối", "Trọng lượng", "Khách hàng", "Mạ băng", "Tình trạng" };
        string[][] rows =
        {
            new[] { Btp40, "Tôm HLSO 40", "", "30", "10.8", "", "", "Tạm" },              // 0 → Tôm HLSO
            new[] { Btp40, "Tôm HLSO 40", "", "7", "10.8", "TÂN PHONG PHÚ", "", "Tạm" },  // 1 → Tôm Gia Công
            new[] { Nl130, "Tôm NL 130", "", "12", "8", "", "", "Tạm" },                   // 2 → Tôm Nguyên Liệu
            new[] { Btp50, "Tôm HLSO 50", "", "9", "10.8", "", "", "Tạm" },                // 3 → chuyển tay sang Tôm Giám Sát
        };
        var arr = new JsonArray(rows.Select(r => (JsonNode)new JsonObject(r.Select((v, i) => new KeyValuePair<string, JsonNode?>("c" + i, v)))).ToArray());
        var meta = new JsonObject { ["headers"] = new JsonArray(headers.Select(h => (JsonNode)h).ToArray()), ["fileName"] = "TON 26.11.xlsx", ["dataDate"] = "26/11/2026", ["manualAssign"] = new JsonObject { ["3"] = "tomgs" } };
        _s.Lan.Write("ton-kho-an-an", new Dictionary<string, JsonArray?> { ["LoadData"] = arr, ["LoadDataMeta"] = new JsonArray(new JsonObject { ["data"] = "__JSON__" + meta.ToJsonString() }) });
    }

    [Fact]
    public void Chon_tu_ton_tach_theo_phan_loai_An_An_va_goi_y_Size_quy_cach()
    {
        SeedAnAn();
        var svc = _s.Cargo; svc.LoadRefs();
        Assert.Equal(new[] { "hlso", "tomgc", "tomnl", "tomgs" }, svc.StockRows.Select(r => r.Nhom));

        // cont đã có 18 kiện BTP 0040-6 KQ 1 → trừ vào phân loại của Loại BTP (Tôm HLSO), không trừ Tôm Gia Công
        var rec = new CargoRec { Blocks = new() { new CargoBlock { Loai = "ks", Lines = new() { new CargoLine { Loai = "BTP", Sn = "0040-6", KsTay = "1", Dai = new() { ["1"] = "18" } } } } } };
        var rows = svc.StockBySize(rec);
        var h = rows.Single(r => r.Sn == "0040-6" && r.Nhom == "hlso");
        var g = rows.Single(r => r.Sn == "0040-6" && r.Nhom == "tomgc");
        Assert.Equal((30.0, 18.0, 12.0, "Tôm HLSO"), (h.Ton, h.Have, h.Left, h.NhomLabel));
        Assert.Equal((7.0, 0.0), (g.Ton, g.Have));
        Assert.Equal("BTP", g.Loai);   // TGC chưa có tiền tố → vẫn là mã BTP

        // gợi ý ô Size-quy cách theo phân loại của Loại dòng đó
        var cfg = svc.Cfg;
        var btp = CargoStock.SuggestSn(rows, cfg, "BTP", "40");
        var one = Assert.Single(btp);
        Assert.Equal(("0040-6", "BTP", "BTP40", "1.8Kg x 6", 12.0, "Tôm HLSO"), (one.Sn, one.Loai, one.Size, one.Pack, one.Left, one.NhomLabel));
        Assert.Empty(CargoStock.SuggestSn(rows, cfg, "BTP", "50"));                    // BTP50 nằm ở Tôm Giám Sát
        Assert.Single(CargoStock.SuggestSn(rows, cfg, "TGS", "50"));
        Assert.Equal(7.0, Assert.Single(CargoStock.SuggestSn(rows, cfg, "TGC", "")).Left);
        Assert.Equal("0130-8", Assert.Single(CargoStock.SuggestSn(rows, cfg, "", "NL130")).Sn);
        Assert.Single(CargoStock.SuggestSn(rows, cfg, "NL", "0130-8"));
        Assert.Empty(CargoStock.SuggestSn(rows, cfg, "NL", "0130-6"));
        Assert.Equal(3, CargoStock.SuggestSn(rows, cfg, "", "").Count);            // Loại trống: mọi hàng còn tồn, gộp theo Loại + Size-quy cách
        Assert.Empty(CargoStock.SuggestSn(rows, cfg, "BTP", "abc"));

        // TGC có tiền tố riêng TRÙNG tiền tố BTP → hàng Tôm Gia Công nhận Loại TGC
        var c2 = NormCfg(CfgFromJson(CfgToJson(cfg))!);
        c2.Qc.Single(q => q.D == "6").Pre["TGC"] = "VRHLBTNXXXR00";
        if (!c2.Loai.Any(l => l.Code == "TGC")) c2.Loai.Add(new CargoLoai { Code = "TGC" });
        var r2 = CargoStock.BySize(svc.StockRows, c2, null, null);
        Assert.Equal("TGC", r2.Single(r => r.Nhom == "tomgc").Loai);
        Assert.Equal("BTP", r2.Single(r => r.Nhom == "hlso" && r.Sn == "0040-6").Loai);
    }

    [Fact]
    public void Dieu_kien_tu_them_cua_An_An_doc_duoc()
    {
        var rows = new JsonArray(new JsonObject { ["data"] = "__JSON__{\"hlso\":[{\"colLabel\":\"Khách hàng\",\"op\":\"eq\",\"value\":\"X\",\"from\":\"1\",\"to\":2}]}" });
        var d = PhanLoaiM1.CustomOf(rows);
        var f = Assert.Single(d["hlso"]);
        Assert.Equal(("Khách hàng", "eq", "X", 1, 2), (f.ColLabel, f.Op, f.Value, f.From, f.To));
        Assert.Empty(PhanLoaiM1.CustomOf(new JsonArray()));
        Assert.Equal("tomgs", PhanLoaiM1.ManualOf(new JsonObject { ["manualAssign"] = new JsonObject { ["3"] = "tomgs", ["x"] = "a" } })[3]);
    }
}

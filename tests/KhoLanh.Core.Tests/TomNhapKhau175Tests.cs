using System.Text.Json.Nodes;
using KhoLanh.Core.KhangSinh;
using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;
using Nk = KhoLanh.Core.KhangSinh.KhangSinhNhapKhau;

namespace KhoLanh.Core.Tests;

/// <summary>1.175 — kết quả kháng sinh tôm nhập khẩu: đọc PDF (bố cục như phiếu thật, chữ không dấu vì PDF dựng bằng phông chuẩn),
/// tra cứu TP / INV / Size, mẫu nhiễm, phiếu cập nhật thay bản cũ, dùng ở phiếu xuất nội địa, bản gọn cho điện thoại.</summary>
public class TomNhapKhau175Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-nk-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public TomNhapKhau175Tests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quan tri", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>Phiếu mẫu: 5 mẫu — 3 mẫu 31/35 (ND), mẫu 4 31/40 NHIỄM (Doxy 7,52, kết quả xuống 3 dòng), mẫu 5 31/40 ND.</summary>
    private static byte[] MakePdf(string so = "19/KS 11", string invoice = "MNSF-155/2024-25", string contract = "TP-143/24-MAA", bool nhiem = true)
    {
        var b = new PdfDocumentBuilder();
        var f = b.AddStandard14Font(Standard14Font.Helvetica);
        var pg = b.AddPage(842, 595);
        void T(string t, double x, double y, double sz = 9) => pg.AddText(t, sz, new PdfPoint(x, y), f);
        T("KET QUA PHAN TICH DU LUONG KHANG SINH (ANTIBIOTICAL RESIDUES ANALYSIS RESULT)", 90, 503, 12);
        T($"BTP AN DO INVOICE: {invoice} DATE: 29.10.2024", 196, 480, 11);
        T($"CONTRACT: {contract} DATE: 17.10.2024", 256, 462, 11);
        T("Ngay phan tich (Date of analysis): 19.11.2024", 20, 441); T($"So (No): {so}", 703, 441);
        T("Mau", 26, 407); T("Ghi chu", 735, 407); T("KET QUA", 598, 401); T("Dat Tinh", 673, 401);
        var cx = new[] { 267.0, 313, 364, 409, 450, 498, 535 };
        var names = new[] { "Enro", "Cipro", "Oxy", "Doxy", "Sulfonamide", "AOZ", "CAP" };
        for (int i = 0; i < 7; i++) { T(names[i], cx[i], 392); T("ppb", cx[i] + 5, 360); }
        T("LOD : ug/kg", 112, 344);
        var lod = new[] { "2", "1,2", "8", "3", "1,5", "0,2", "0,1" };
        var vx = new[] { 272.0, 320, 367, 415, 462, 503, 539 };
        for (int i = 0; i < 7; i++) T(lod[i], vx[i], 344);
        for (int k = 1; k <= 5; k++)
        {
            var y = 324 - (k - 1) * 25.6; var size = k <= 3 ? "31/35" : "31/40"; var kien = k == 5 ? 60 : 50;
            var bad = nhiem && k == 4;
            T($"Tom HLSO {size} nhap khau", 56, y + 6); T(k.ToString(), 34, y); T("18.11.2024", 204, y);
            for (int i = 0; i < 7; i++) T(bad && i == 3 ? "7,52" : "ND", vx[i], y);
            if (bad)
            {
                T("DAT NHAT,", 573, y + 8); T("ASC-A,B, MY,", 573, y); T("HQ", 594, y - 8);
                T("10.N-AS2-M-", 645, y + 4); T("H", 670, y - 9); T("Doxy: 7,52", 735, y);
            }
            else { T("DAT MKS", 577, y); T("23.MKS", 655, y); }
            T($"Ngay 18.11, {kien} kien, Mau {k}", 56, y - 7);
        }
        return b.Build();
    }

    [Fact]
    public void Doc_phieu_tom_nhap_khau_tach_size_va_mau_nhiem()
    {
        var p = Nk.Read(MakePdf(), "KQKS Tom HLSO INVOICE MNSF-155 TP 143.pdf")!;
        Assert.NotNull(p);
        Assert.Equal("MNSF-155/2024-25", p.Invoice); Assert.Equal("TP-143/24-MAA", p.Contract); Assert.Equal("143", p.Tp);
        Assert.Equal("2024-11-19", p.Date); Assert.Equal("19/KS 11", p.So); Assert.Equal(5, p.Mau.Count);
        Assert.Equal("0,2", p.Lod["aoz"].Replace('.', ','));
        Assert.Empty(p.Issues);
        var by = p.BySize(); Assert.Equal(new[] { "31/35", "31/40" }, by.Select(x => x.Size));
        var recs = Nk.ToRecords(p, "2026-11-27T00:00:00Z").Select(r => Nk.Nhom.Parse("i:nk:x", r.ToJsonString())!).ToList();
        var t35 = Nk.Summarize(recs[0]); Assert.True(t35.AllSame); Assert.Equal("23.MKS", t35.KyHieu); Assert.Equal(150, t35.Kien); Assert.Equal(0, t35.Nhiem);
        var t40 = Nk.Summarize(recs[1]); Assert.Equal(2, t40.Total); Assert.Equal(1, t40.Nhiem); Assert.Equal(110, t40.Kien);
        Assert.Equal("10.N-AS2-M-H", p.Mau[3].DatTinh); Assert.Equal("DAT NHAT, ASC-A,B, MY, HQ", p.Mau[3].Kq); Assert.Equal("7,52", p.Mau[3].Inputs["doxy"]);
        Assert.Contains("mẫu 4 Doxy 7,52", t40.NhiemText);
    }

    [Fact]
    public void Khong_phai_phieu_tom_nhap_khau_tra_ve_null_va_phieu_cu_van_doc_theo_cach_cu()
    {
        var b = new PdfDocumentBuilder(); var f = b.AddStandard14Font(Standard14Font.Helvetica);
        b.AddPage(842, 595).AddText("KET QUA PHAN TICH DU LUONG KHANG SINH Ket qua lan 1 Ket qua lan 2", 10, new PdfPoint(50, 500), f);
        Assert.Null(Nk.Read(b.Build(), "x.pdf"));
    }

    [Fact]
    public void Tim_theo_TP_INV_Contract_Size()
    {
        var p = Nk.Read(MakePdf(), "f.pdf")!;
        var all = Nk.Parse2(p);
        Assert.Single(Nk.Find(all, "TP143 size 31/40")); Assert.Equal("31/40", Nk.Find(all, "tp-143 31/40")[0].Size);
        Assert.Equal(2, Nk.Find(all, "TP 143").Count); Assert.Equal(2, Nk.Find(all, "143").Count);
        Assert.Equal(2, Nk.Find(all, "INV MNSF-155").Count); Assert.Equal(2, Nk.Find(all, "mnsf155").Count);
        Assert.Equal(2, Nk.Find(all, "TP-143/24-MAA").Count);
        Assert.Empty(Nk.Find(all, "TP144")); Assert.Empty(Nk.Find(all, "TP143 size 51/60"));
    }

    [Fact]
    public void Phieu_xuat_noi_dia_cot_Lo_ghi_TP_hoac_INV_va_Size()
    {
        var all = Nk.Parse2(Nk.Read(MakePdf(), "f.pdf")!);
        // Size 31/35: mọi mẫu 23.MKS → tự điền
        var ok = Nk.ForCargo(all, "TP143 31/35"); Assert.NotNull(ok.Ks); Assert.Equal(23, ok.Ks!.Stt); Assert.Equal("23.MKS", ok.Ks.KyHieu);
        // Size 31/40 có mẫu nhiễm → không tự chọn
        var bad = Nk.ForCargo(all, "TP143 31/40"); Assert.Null(bad.Ks); Assert.Contains("mẫu nhiễm", bad.Error); Assert.Contains("Doxy 7,52", bad.Error);
        // thiếu Size → liệt kê
        var multi = Nk.ForCargo(all, "TP143"); Assert.Null(multi.Ks); Assert.Contains("khớp 2 phiếu", multi.Error);
        Assert.Empty(Nk.ForCargo(all, "TP999").Hits);

        var ks = new CargoCore.KsIndex { Nk = all };
        var cfg = CargoCore.NormCfg(CargoCore.DefaultCfg());
        var blk = new CargoBlock { Loai = "ks" };
        var ln = new CargoLine { Loai = "BTP", Sn = "0035-6", Lo = "TP143 31/35", Dai = new() { ["1"] = "5" } };
        var r = CargoCore.ResolveLine(cfg, new CargoBlock(), ln, ks);
        Assert.False(r.ChuaCoKQ); Assert.Equal("23.MKS", r.KsText); Assert.Equal("MKS", r.Market);
        Assert.Equal("23.MKS", CargoCore.DacTinhOfLo(ks, "INV MNSF-155 31/35"));
        var r2 = CargoCore.ResolveLine(cfg, new CargoBlock(), new CargoLine { Loai = "BTP", Sn = "0040-6", Lo = "TP143 31/40", Dai = new() { ["1"] = "5" } }, ks);
        Assert.True(r2.ChuaCoKQ); Assert.Contains(r2.Errors, e => e.Contains("mẫu nhiễm"));
        // lô thường vẫn như cũ
        Assert.True(CargoCore.ResolveLine(cfg, new CargoBlock(), new CargoLine { Loai = "BTP", Sn = "0040-6", Lo = "01.01.26 L1", Dai = new() { ["1"] = "5" } }, ks).ChuaCoKQ);
    }

    [Fact]
    public void Phieu_cap_nhat_thay_ban_cu_giu_lich_su_va_dong_bo_dien_thoai()
    {
        var ks = _s.KhangSinh; ks.Load();
        var p1 = Nk.Read(MakePdf(so: "11/KS 11", nhiem: false), "cu.pdf")!;
        var r1 = ks.ImportNhapKhau(new[] { p1 }); Assert.Equal(2, r1.Added); Assert.Equal(0, r1.Replaced);
        Assert.Equal(2, ks.NhapKhau.Count);
        // nạp lại đúng phiếu đó → không nhân đôi
        var again = ks.ImportNhapKhau(new[] { p1 }); Assert.Equal(2, again.Same); Assert.Equal(0, again.Added); Assert.Equal(2, ks.NhapKhau.Count);
        // phiếu cập nhật: cùng INV + Contract + Size, có mẫu nhiễm
        var p2 = Nk.Read(MakePdf(so: "19/KS 11", nhiem: true), "moi.pdf")!;
        Assert.Equal(2, ks.NkConflicts(new[] { p2 }).Count);
        var r2 = ks.ImportNhapKhau(new[] { p2 });
        Assert.Equal(2, r2.Replaced);
        Assert.Equal(2, ks.NhapKhau.Count); Assert.All(ks.NhapKhau, n => Assert.Equal("19/KS 11", n.So));
        Assert.Equal(2, ks.NhapKhauCu.Count); Assert.All(ks.NhapKhauCu, n => Assert.Equal("19/KS 11", n.ReplacedBy));
        // lưu bền: mở lại
        var re = new KhangSinhService(() => _s.Lan, _s.Session, _s.Audit); re.Load();
        Assert.Equal(2, re.NhapKhau.Count); Assert.Equal(2, re.NhapKhauCu.Count);
        // chỉ mục tra phiếu nội địa lấy được từ sheet
        var ix = CargoCore.KsFromRows(JsonNode.Parse(_s.Lan.Read(KhangSinhService.Db, KhangSinhService.Sheet)) as JsonArray ?? new());
        Assert.Equal(2, ix.Nk.Count);
        // bản gọn cho điện thoại: không có 30 dòng ND, có mẫu nhiễm
        var row = JsonNode.Parse(_s.Lan.Read(KhangSinhService.Db, KhangSinhService.Sheet))!.AsArray().Select(x => x!.AsObject()).First(o => o["key"]!.ToString().StartsWith("i:nk:") && o["value"]!.ToString().Contains("31/40"));
        var compact = JsonNode.Parse(PhoneSync.PhoneSyncService.KsCompact(row["key"]!.ToString(), row["value"]))!.AsObject();
        Assert.Equal(1, (int)compact["v"]!); Assert.Equal("143", compact["tp"]!.ToString()); Assert.Equal("31/40", compact["sz"]!.ToString()); Assert.Equal(2, (int)compact["n"]!);
        Assert.Single(compact["x"]!.AsArray()); Assert.Equal("Doxy", compact["x"]![0]![2]![0]![0]!.ToString()); Assert.Equal("7,52", compact["x"]![0]![2]![0]![1]!.ToString());
        Assert.Null(compact["mau"]);
        // xóa
        Assert.Equal(2, ks.DeleteNhapKhau(ks.NhapKhau)); Assert.Empty(ks.NhapKhau);
    }
}

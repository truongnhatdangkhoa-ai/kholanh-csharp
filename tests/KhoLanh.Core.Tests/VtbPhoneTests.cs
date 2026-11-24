using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using KhoLanh.Core.Services;
using KhoLanh.Core.SoKho;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.SoKho.SoKhoCore;

namespace KhoLanh.Core.Tests;

/// <summary>Web App giả: giữ các tab trong bộ nhớ, trả lời pc.ping / ensureTabs / readTable / writeTable / markRows như Code.gs.</summary>
public sealed class FakeWebApp : HttpMessageHandler
{
    public readonly Dictionary<string, (List<string> H, List<JsonArray> Rows)> Tabs = new();
    public readonly List<string> Calls = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var b = JsonNode.Parse(await req.Content!.ReadAsStringAsync(ct))!.AsObject();
        var a = b["action"]!.ToString(); Calls.Add(a);
        JsonObject res;
        if (b["token"]?.ToString() != "TK") res = new JsonObject { ["ok"] = false, ["error"] = "Sai mã PC_TOKEN" };
        else switch (a)
        {
            case "pc.ping": res = new JsonObject { ["ok"] = true, ["title"] = "Kho Lạnh sync", ["sheetNames"] = new JsonArray(Tabs.Keys.Select(k => (JsonNode)k).ToArray()) }; break;
            case "pc.ensureTabs":
                foreach (var (k, v) in b["tabs"]!.AsObject()) if (!Tabs.ContainsKey(k)) Tabs[k] = ((v as JsonArray)?.Select(x => x!.ToString()).ToList() ?? new(), new());
                res = new JsonObject { ["ok"] = true }; break;
            case "pc.readTable":
            {
                var t = Tabs.GetValueOrDefault(b["name"]!.ToString());
                res = new JsonObject { ["ok"] = true, ["headers"] = new JsonArray((t.H ?? new()).Select(h => (JsonNode)h).ToArray()), ["rows"] = new JsonArray((t.Rows ?? new()).Select(r => (JsonNode)r.DeepClone()).ToArray()), ["rowNumbers"] = new JsonArray(Enumerable.Range(0, t.Rows?.Count ?? 0).Select(i => (JsonNode)(i + 2)).ToArray()) };
                break;
            }
            case "pc.writeTable":
                Tabs[b["name"]!.ToString()] = (b["columns"]!.AsArray().Select(x => x!.ToString()).ToList(), b["rows"]!.AsArray().Select(r => (JsonArray)r!.DeepClone()).ToList());
                res = new JsonObject { ["ok"] = true }; break;
            case "pc.markRows":
            {
                var t = Tabs[b["name"]!.ToString()];
                foreach (var u in b["updates"]!.AsArray()) { var row = t.Rows[u!["rowNumber"]!.GetValue<int>() - 2]; foreach (var (k, v) in u["values"]!.AsObject()) row[t.H.IndexOf(k)] = v!.ToString(); }
                res = new JsonObject { ["ok"] = true }; break;
            }
            default: res = new JsonObject { ["ok"] = false, ["error"] = "Không có chức năng " + a }; break;
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(res.ToJsonString(), Encoding.UTF8, "application/json") };
    }
    public JsonArray Row(string tab, params (string Col, JsonNode? V)[] cells)
    {
        var t = Tabs[tab]; var r = new JsonArray(t.H.Select(_ => (JsonNode)"").ToArray());
        foreach (var (c, v) in cells) r[t.H.IndexOf(c)] = v; t.Rows.Add(r); return r;
    }
    public string Cell(string tab, int row, string col) => Tabs[tab].Rows[row][Tabs[tab].H.IndexOf(col)]!.ToString();
}

public class VtbPhoneTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-vtbp-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public VtbPhoneTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static SkLine L(string ma, double sl, string vt) =>
        NormLine(new JsonObject { ["loai"] = "nhap", ["ngay"] = "2026-11-01", ["maHang"] = ma, ["soLuong"] = sl, ["netDv"] = 25, ["viTri"] = vt, ["chungTu"] = "X" });

    [Fact]
    public async Task Day_ton_nhan_phieu_dien_thoai_danh_dau_va_day_lai()
    {
        var fake = new FakeWebApp(); var w = new PhoneWebApp(_s.Settings, fake);
        Assert.Throws<BusinessException>(() => w.Save("https://example.com/x", "TK"));
        w.Save("https://script.google.com/macros/s/ABC/exec", "TK");
        Assert.True(w.Enabled); Assert.Equal("TK", w.Token);
        var v = _s.Vtb; v.Load(); v.Now = () => new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);
        v.Push(VtbService.FromLedger(new[] { L("BOT-A", 10, "B1"), L("BOT-B", 4, "B2") }, "2026-11-05"), "2026-11-05");
        // lần 1: chưa có phiếu → chỉ đẩy tồn + Meta
        var r1 = await v.SyncPhoneAsync(w);
        Assert.Equal((0, 2), (r1.NewPhieu, r1.Pushed));
        Assert.Equal(VtbService.PushColumns, fake.Tabs[VtbService.TabStock].H);
        var key = fake.Cell(VtbService.TabStock, 0, "_key"); Assert.Equal("BOT-A|B1||#0", key);
        Assert.Equal("10", fake.Cell(VtbService.TabStock, 0, "SL"));
        // điện thoại tạo 3 phiếu: 1 Vị Trí Bột hợp lệ, 1 Vị Trí Bột chưa có dòng (mới tạo), 1 của Tồn kho gửi (không đụng tới)
        fake.Row("PhieuXuat", ("id", "P1"), ("maPhieu", "DT-01"), ("ngayTao", "2026-11-05T07:59:00.000Z"), ("trangThai", "Chưa xử lý"), ("nguoiTao", "kho1"), ("module", "M01VTB"));
        fake.Row("PhieuXuatItems", ("phieuId", "P1"), ("itemKey", key), ("soLuongXuat", "3"));
        fake.Row("PhieuXuat", ("id", "P2"), ("ngayTao", "2026-11-05T07:58:00.000Z"), ("module", "M01VTB"));
        fake.Row("PhieuXuat", ("id", "P3"), ("ngayTao", "2026-11-05T07:00:00.000Z"), ("module", "M02"));
        var r2 = await v.SyncPhoneAsync(w);
        Assert.Equal(1, r2.NewPhieu); Assert.Empty(r2.Errors);
        Assert.Equal(7, v.Rows[0].Sl); Assert.Single(v.Slips); Assert.Equal("P1", v.Slips[0].PhoneId); Assert.True(v.Slips[0].IsPhone); Assert.False(v.Locked(v.Slips[0]));
        Assert.Equal(VtbService.DaXuLy, fake.Cell("PhieuXuat", 0, "trangThai"));
        Assert.Equal("", fake.Cell("PhieuXuat", 1, "trangThai"));      // chưa quá 10 phút
        Assert.Equal("", fake.Cell("PhieuXuat", 2, "trangThai"));      // phiếu module khác giữ nguyên
        Assert.Equal("7", fake.Cell(VtbService.TabStock, 0, "SL"));
        var meta = fake.Tabs["Meta"].Rows.ToDictionary(r => r[0]!.ToString(), r => r[1]!.ToString());
        Assert.Equal("2", meta["m01ViTriBotRows"]); Assert.Equal("05/11/2026", meta["m01ViTriBotDataDate"]); Assert.True(meta.ContainsKey("lastPushAt_M01VTB"));
        // lần sau: P1 đã xử lý → không trừ lần 2; P2 quá 10 phút không có dòng → đánh dấu lỗi
        v.Now = () => new DateTime(2026, 11, 5, 8, 30, 0, DateTimeKind.Utc);
        var r3 = await v.SyncPhoneAsync(w);
        Assert.Equal((0, 1), (r3.NewPhieu, r3.ErrPhieu)); Assert.Equal(7, v.Rows[0].Sl);
        Assert.Equal(VtbService.LoiThieuDong, fake.Cell("PhieuXuat", 1, "trangThai"));
        // phiếu trùng phoneId (điện thoại gửi lại) chỉ đánh dấu, không trừ lần nữa; phiếu chỉ dòng lạ → không nhận, báo lỗi
        fake.Row("PhieuXuat", ("id", "P1"), ("ngayTao", "2026-11-05T08:20:00.000Z"), ("module", "M01VTB"));
        fake.Row("PhieuXuat", ("id", "P4"), ("ngayTao", "2026-11-05T08:20:00.000Z"), ("module", "M01VTB"));
        fake.Row("PhieuXuatItems", ("phieuId", "P4"), ("itemKey", "KHONG|CO||#0"), ("soLuongXuat", 1));
        var r4 = await v.SyncPhoneAsync(w);
        Assert.Equal(0, r4.NewPhieu); Assert.Single(r4.Errors); Assert.Equal(7, v.Rows[0].Sl);
        Assert.Equal(VtbService.DaXuLy, fake.Cell("PhieuXuat", 3, "trangThai")); Assert.Equal("", fake.Cell("PhieuXuat", 4, "trangThai"));
        // hủy trừ phiếu điện thoại hoàn lại SL
        v.SetDeducted(v.Slips[0].Id, false); Assert.Equal(10, v.Rows[0].Sl);
        // sai mã → báo lỗi rõ ràng
        w.Save("https://script.google.com/macros/s/ABC/exec", "SAI");
        var e = await Assert.ThrowsAsync<BusinessException>(() => w.PingAsync()); Assert.Contains("PC_TOKEN", e.Message);
    }

    [Fact]
    public void Nap_file_Vi_tri_Bot_kieu_cu()
    {
        var path = Path.Combine(_dir, "VI TRI BOT 23.11.xlsx");
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("BOT");
            ws.Cell(1, 1).Value = "NGÀY:"; ws.Cell(1, 2).Value = new DateTime(2026, 11, 23);
            var heads = new[] { "VT", "MÃ HÀNG", "SL", "NSX", "HSD", "NHẬP", "NGÀY XUẤT", "VỊ TRÍ CŨ", "VỊ TRÍ MỚI", "GHI CHÚ" };
            for (var c = 0; c < heads.Length; c++) ws.Cell(2, c + 1).Value = heads[c];
            ws.Cell(3, 1).Value = "B1"; ws.Cell(3, 2).Value = "BOT-CHIEN-XU"; ws.Cell(3, 3).Value = 40; ws.Cell(3, 4).Value = new DateTime(2026, 9, 1); ws.Cell(3, 5).Value = "2027-03-01"; ws.Cell(3, 6).Value = 46300; ws.Cell(3, 10).Value = "hàng mới";
            ws.Cell(4, 1).Value = "B2"; ws.Cell(4, 2).Value = "BOT-NHUNG"; ws.Cell(4, 3).Value = "12,5"; ws.Cell(4, 5).Value = "01122026"; ws.Cell(4, 9).Value = "C3";
            ws.Cell(5, 4).Value = "ghi chú lạc";   // không có VT lẫn MÃ HÀNG → bỏ
            ws.Cell(6, 1).Value = "S1"; ws.Cell(6, 2).Value = "SOT-TERIYAKI"; ws.Cell(6, 3).Value = 0;
            wb.AddWorksheet("Trang").Cell(1, 1).Value = "x";
            wb.SaveAs(path);
        }
        var (book, names) = KhoLanh.Core.CuoiKy.CkService.ReadExcel(path, keepDates: true);
        var p = VtbService.ParseBook(book, names);
        Assert.True(p.Ok, p.Error);
        Assert.Equal(("BOT", 2, 1, "23/11/2026"), (p.Sheet, p.HeaderRow, p.Skipped, p.DataDate));
        Assert.Equal(new[] { "B1|BOT-CHIEN-XU|40|01/09/2026|01/03/2027|05/10/2026||hàng mới", "B2|BOT-NHUNG|12.5||01/12/2026||C3|", "S1|SOT-TERIYAKI|0|||||" },
            p.Rows.Select(r => $"{r.Vt}|{r.Ma}|{r.Sl.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{r.Nsx}|{r.Hsd}|{r.Nhap}|{r.VtMoi}|{r.GhiChu}"));
        Assert.Equal("C3", p.Rows[1].VtMoi);
        var bad = VtbService.ParseSheet(new List<IReadOnlyList<object?>> { new object?[] { "Mã", "Tên" } }, "X");
        Assert.False(bad.Ok); Assert.Contains("VT, MÃ HÀNG, SL", bad.Error);
        // nạp: thay dữ liệu, phiếu đợt cũ bị khóa, máy khác đọc lại giống hệt
        var v = _s.Vtb; v.Load(); v.Now = () => new DateTime(2026, 11, 23, 7, 0, 0);
        v.Push(VtbService.FromLedger(new[] { L("BOT-A", 10, "B1") }, "2026-11-20"), "2026-11-20");
        var slip = v.CreateSlip(new[] { new VtbService.Pick(0, 2) }, "");
        v.Now = () => new DateTime(2026, 11, 23, 8, 0, 0);
        Assert.Equal(3, v.LoadFile(p, "VI TRI BOT 23.11.xlsx"));
        Assert.True(v.Locked(v.Slips.First(x => x.Id == slip.Id)));
        var v2 = new VtbService(() => _s.Lan, _s.Session); v2.Load();
        Assert.Equal(("VI TRI BOT 23.11.xlsx", "23/11/2026", "BOT", 3), (v2.FileName, v2.DataDate, v2.Sheet, v2.Rows.Count));
        Assert.Equal("01/12/2026", v2.Rows[1].Hsd);
        Assert.Equal("BOT-NHUNG|B2||01/12/2026#0", VtbService.Keys(v2.Rows)[1]);
    }
}

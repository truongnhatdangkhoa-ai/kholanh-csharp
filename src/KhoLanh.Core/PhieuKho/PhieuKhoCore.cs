using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.PhieuKho;

/// <summary>Đọc chuỗi / số / true-false đều thành chuỗi (app cũ lưu cột "kiện" lúc là chuỗi lúc là số); ghi ra chuỗi.</summary>
public sealed class StrConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
    {
        JsonTokenType.String => r.GetString() ?? "",
        JsonTokenType.Number => r.TryGetInt64(out var l) ? l.ToString(CultureInfo.InvariantCulture) : r.GetDouble().ToString("0.############", CultureInfo.InvariantCulture),
        JsonTokenType.True => "true", JsonTokenType.False => "false", _ => "",
    };
    public override void Write(Utf8JsonWriter w, string v, JsonSerializerOptions o) => w.WriteStringValue(v);
}

public sealed class Xe
{
    public string TaiXe { get; set; } = "";
    public string Sdt { get; set; } = "";
    public string SoXe { get; set; } = "";
    public string Cont { get; set; } = "";
    public string Seal { get; set; } = "";
    public string Booking { get; set; } = "";
    public string NhietDo { get; set; } = "-18℃";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    public string Get(string k) => k switch { "taiXe" => TaiXe, "sdt" => Sdt, "soXe" => SoXe, "cont" => Cont, "seal" => Seal, "booking" => Booking, "nhietDo" => NhietDo, _ => "" };
    public void Set(string k, string v) { switch (k) { case "taiXe": TaiXe = v; break; case "sdt": Sdt = v; break; case "soXe": SoXe = v; break; case "cont": Cont = v; break; case "seal": Seal = v; break; case "booking": Booking = v; break; case "nhietDo": NhietDo = v; break; } }
}

public sealed class GhiChuCuoi
{
    public string HinhAnh { get; set; } = "Đã kiểm tra hình ảnh: … dãy. Có hình ảnh bên ngoài";
    public string NdBatDau { get; set; } = "………";
    public string NdRoi { get; set; } = "………";
    public string Sole { get; set; } = "";
    public string NhietKe { get; set; } = "";
    public string MaNhietKe { get; set; } = "";
    public string BocLuoi { get; set; } = "";
    public string ContDen { get; set; } = "…. Giờ ….";
    public string ContDi { get; set; } = "…. Giờ ….";
    public string CuaXH { get; set; } = "…………";
    public string NguoiBao { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class Dong
{
    public string Date { get; set; } = "";
    public string Size { get; set; } = "";
    public string Ma { get; set; } = "";
    public string Packing { get; set; } = "";
    public string Kien { get; set; } = "";
    public string Mks { get; set; } = "";
    public double? NetTon { get; set; }
    public string NetPk { get; set; } = "";
    public string NetTay { get; set; } = "";
    public bool? NoSize { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class MatHang
{
    public string Ten { get; set; } = "";
    public string Ma { get; set; } = "";
    public string Lo { get; set; } = "";
    public string Sx { get; set; } = "";
    public string Lsx { get; set; } = "";
    public JsonNode? Hint { get; set; }
    public string? PackNote { get; set; }
    [JsonPropertyName("dong")] public List<Dong> DongList { get; set; } = new() { new Dong() };
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class Phieu
{
    public string Id { get; set; } = "";
    public string Loai { get; set; } = "xuat_khau";
    public string Ngay { get; set; } = "";
    public string SoPhieu { get; set; } = "";
    public string SoPhieuTuCap { get; set; } = "";
    public string Kho { get; set; } = "An An";
    public string Lsx { get; set; } = "";
    public string Khach { get; set; } = "";
    public string KieuXuat { get; set; } = "Xuất nước ngoài";
    public string LyDo { get; set; } = "";
    public string HopDong { get; set; } = "";
    public string LoaiCont { get; set; } = "40RF";
    public string O40 { get; set; } = "";
    public string O40TuCap { get; set; } = "";
    public string DonVi { get; set; } = "kiện";
    public string CapNhat { get; set; } = "";
    public string XuatLuc { get; set; } = "";
    public bool LyDoTay { get; set; }
    public bool? DaSua { get; set; }
    public Xe Xe { get; set; } = new();
    public List<MatHang> Mh { get; set; } = new() { new MatHang(), new MatHang() };
    public GhiChuCuoi Gc { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// Lõi tính toán Phiếu xuất kho (port từ phieu-nhap-xuat.js của app cũ): NET theo quy cách, tổng kiện / kg, số thành chữ, nội dung email,
/// tách email "Thông tin xe", cấp số phiếu, kiểm tra trước khi xuất, dựng bảng ô cho mẫu Excel. Không phụ thuộc giao diện.
/// </summary>
public static class PhieuKhoCore
{
    public sealed record Loai(string Key, string Ten, string Nhom);
    public static readonly Loai[] Loais =
    {
        new("xuat_khau", "Xuất khẩu An An", "xuat"), new("xuat_khau_tp", "Xuất khẩu Thuận Phước", "xuat"), new("xuat_thuan_phuoc", "Xuất Thuận Phước", "xuat"), new("xuat_gia_cong", "Xuất gia công", "xuat"),
        new("xuat_noi_dia", "Xuất nội địa", "xuat"), new("xuat_chieu_xa", "Xuất chiếu xạ", "xuat"), new("xuat_gui_kho", "Xuất gửi kho", "xuat"), new("xuat_khac", "Xuất khác", "xuat"),
        new("nhap_khau", "Nhập nước ngoài", "nhap"), new("nhap_gia_cong", "Nhập gia công", "nhap"), new("nhap_thuan_phuoc", "Nhập Thuận Phước", "nhap"),
        new("nhap_mua_ngoai", "Nhập mua ngoài", "nhap"), new("nhap_gui_kho", "Nhập gửi kho", "nhap"), new("nhap_khac", "Nhập khác", "nhap"),
    };
    public static string LoaiTen(string k) => Loais.FirstOrDefault(x => x.Key == k)?.Ten ?? k;

    /// <summary>Ô thông tin xe: khóa, ô, tiền tố (ĐÚNG ĐỘ DÀI — công thức của mẫu cắt chuỗi theo vị trí), nhãn.</summary>
    public static readonly (string Key, string Cell, string Pre, string Label)[] XeFields =
    {
        ("taiXe", "S2", "Họ tên tài xế: ", "Họ tên tài xế"), ("sdt", "S3", "Số điện thoại: ", "Số điện thoại"), ("soXe", "S4", "Số xe: ", "Số xe"),
        ("cont", "S5", "Số cont: ", "Số cont"), ("seal", "S6", "Số seal: ", "Số seal"), ("booking", "S7", "Số booking: ", "Số booking"),
        ("nhietDo", "S8", "Nhiệt độ cài đặt cont: ", "Nhiệt độ cài đặt cont"),
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowReadingFromString, Converters = { new StrConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNameCaseInsensitive = true,
    };

    public static string ToJson(Phieu p) => JsonSerializer.Serialize(p, Json);
    public static Phieu? FromJson(string json)
    {
        try { return Migrate(JsonSerializer.Deserialize<Phieu>(json, Json)); } catch { return null; }
    }
    public static Phieu Clone(Phieu p) => FromJson(ToJson(p))!;

    private static string Pad2(int n) => n.ToString("00");
    public static string TodayKey() => DateTime.Today.ToString("yyyy-MM-dd");
    public static string Dm(string k) => Regex.IsMatch(k ?? "", @"^\d{4}-\d{2}-\d{2}$") ? k![8..10] + "." + k[5..7] : "";

    /// <summary>Số do người dùng gõ: null = trống, NaN = không đọc được.</summary>
    public static double? Num(string? v)
    {
        var s = (v ?? "").Trim().Replace(",", "");
        if (s.Length == 0) return null;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : double.NaN;
    }
    private static double N0(string? v) => Num(v) is { } d && !double.IsNaN(d) ? d : 0;

    public static Phieu Migrate(Phieu? p)
    {
        p ??= new Phieu();
        if (p.KieuXuat.Length == 0) p.KieuXuat = "Xuất nước ngoài";
        if (p.DonVi.Length == 0) p.DonVi = "kiện";
        if (p.LoaiCont.Length == 0) p.LoaiCont = "40RF";
        if (p.Mh.Count == 0) p.Mh.Add(new MatHang());
        foreach (var m in p.Mh) if (m.DongList.Count == 0) m.DongList.Add(new Dong());
        return p;
    }

    public static Phieu NewPhieu(string loai = "xuat_khau") => new() { Loai = loai, Ngay = TodayKey() };

    // ------------------------------------------------------------------ ngày / lý do / LSX

    public static string NormDate(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "";
        var m = Regex.Match(s, @"^(\d{1,2})[\/\-. ](\d{1,2})[\/\-. ](\d{4})$");
        if (m.Success) return Pad2(int.Parse(m.Groups[1].Value)) + "." + Pad2(int.Parse(m.Groups[2].Value)) + "." + m.Groups[3].Value;
        m = Regex.Match(s, @"^(\d{4})[\/\-.](\d{1,2})[\/\-.](\d{1,2})$");
        if (m.Success) return Pad2(int.Parse(m.Groups[3].Value)) + "." + Pad2(int.Parse(m.Groups[2].Value)) + "." + m.Groups[1].Value;
        return s;
    }
    public static bool DateOk(string s) => Regex.IsMatch(s, @"^\d{2}\.\d{2}\.\d{4}$");

    public static string LsxNorm(string? v) => Regex.Replace((v ?? "").Trim().ToUpperInvariant(), @"\s+", "");
    public static List<string> LsxList(string? v)
    {
        var seen = new HashSet<string>(); var o = new List<string>();
        foreach (var x in Regex.Split(v ?? "", @"[+,;&\n]"))
        {
            var t = Regex.Replace(x.Trim(), @"\s+", " "); var k = LsxNorm(t);
            if (t.Length > 0 && seen.Add(k)) o.Add(t);
        }
        return o;
    }
    public static string LsxJoin(IEnumerable<string> a) => string.Join(" + ", a);

    public static string LyDoAuto(Phieu p) => ("Xuất hàng thành phẩm" + (p.Lsx.Length > 0 ? " LSX " + p.Lsx : "") + (p.Khach.Length > 0 ? " " + p.Khach : "")).Trim();
    public static string LyDoOf(Phieu p) => p.LyDoTay ? p.LyDo : LyDoAuto(p);
    /// <summary>Dòng 9 "Lý do xuất", dòng 10 phần từ chữ LSX trở đi.</summary>
    public static (string Head, string Lsx) LyDoLines(Phieu p)
    {
        var t = Regex.Replace(LyDoOf(p) ?? "", @"\s+", " ").Trim();
        var m = Regex.Match(t, @"^(.*?)\s*\b(LSX\b.*)$", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()) : (t, "");
    }

    // ------------------------------------------------------------------ NET / mặt hàng

    /// <summary>NET kg / kiện từ quy cách "1.0 kg x 5 x 2", "24 oz x 12 gói", "0.312kg x 12", "10kg".</summary>
    public static double? NetOf(string? packing)
    {
        var s = (packing ?? "").Trim().ToLowerInvariant().Replace(',', '.');
        if (s.Length == 0) return null;
        var parts = Regex.Split(s, @"\s*[×*]\s*|\s*x\s*(?=\d)");
        var first = Regex.Match(parts[0], @"^(\d+(?:\.\d+)?)\s*(kg|g|oz|lbs?)?$");
        if (!first.Success) return null;
        var v = double.Parse(first.Groups[1].Value, CultureInfo.InvariantCulture);
        var unit = first.Groups[2].Value;
        if (unit == "g") v /= 1000; else if (unit == "oz") v *= 0.028349523125; else if (unit.StartsWith("lb")) v *= 0.45359237;
        if (parts.Length == 1 && unit != "kg") return null;
        for (var i = 1; i < parts.Length; i++)
        {
            var mm = Regex.Match(parts[i], @"^(\d+(?:\.\d+)?)(?:\s*\p{L}+)?$");
            if (!mm.Success) return null;
            v *= double.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return Math.Round(v * 1e6) / 1e6;
    }

    public static double? NetTayOf(Dong r)
    {
        var t = (r.NetTay ?? "").Trim().Replace(',', '.');
        if (t.Length == 0) return null;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) && v >= 0 ? v : null;
    }
    public static double? RowNetAuto(Dong r) => r.NetTon is > 0 && r.NetPk == r.Packing ? r.NetTon : NetOf(r.Packing);
    public static double? RowNet(Dong r) => NetTayOf(r) ?? RowNetAuto(r);

    public static bool UsedRow(Dong r) => r.Date.Trim().Length > 0 || r.Size.Trim().Length > 0 || r.Packing.Trim().Length > 0 || (Num(r.Kien) is { } k && (double.IsNaN(k) || k != 0));
    public static List<Dong> RowsOf(MatHang m) => m.DongList.Where(UsedRow).ToList();
    public static bool UsedBlock(MatHang m) => m.Ten.Trim().Length > 0 || m.Ma.Trim().Length > 0 || m.Lo.Trim().Length > 0 || RowsOf(m).Count > 0;

    /// <summary>Vị trí mã hàng: 1 mã → ghi ở tiêu đề; nhiều mã → ghi phía sau từng dòng size.</summary>
    public static (string Title, bool PerRow, string First) MaLay(MatHang m)
    {
        var bas = m.Ma.Trim(); var seen = new HashSet<string>(); var list = new List<string>();
        foreach (var r in m.DongList) { var c = r.Ma.Trim(); if (c.Length > 0 && seen.Add(c.ToUpperInvariant())) list.Add(c); }
        if (bas.Length > 0 && !seen.Contains(bas.ToUpperInvariant())) list.Insert(0, bas);
        var perRow = list.Count > 1;
        return (perRow ? "" : list.FirstOrDefault() ?? "", perRow, list.FirstOrDefault() ?? "");
    }
    public static string RowMa(MatHang m, Dong r) => MaLay(m).PerRow ? (r.Ma.Trim().Length > 0 ? r.Ma.Trim() : m.Ma.Trim()) : "";
    /// <summary>"AA" (An An) hay "TP" (Thuận Phước): theo lựa chọn tay, không thì theo KÝ TỰ ĐẦU mã hàng (P… = TP).</summary>
    public static string SideOf(MatHang m) => m.Sx is "TP" or "AA" ? m.Sx : Regex.IsMatch(MaLay(m).First, "^p", RegexOptions.IgnoreCase) ? "TP" : "AA";
    public static string TenOut(MatHang m) => m.Ten.Trim().ToUpper(CultureInfo.GetCultureInfo("vi-VN"));
    public static string TenTitle(Phieu p, MatHang m) => TenOut(m) + (m.Lsx.Length > 0 && LsxList(p.Lsx).Count >= 2 ? " - LSX " + m.Lsx.Trim() : "");

    public sealed record BlockTotal(double Kien, double Kg, string Side);
    public sealed record Totals(double Kien, double Kg, double Aa, double Tp, List<BlockTotal> Blocks);
    public static Totals TotalsOf(Phieu p)
    {
        double kien = 0, kg = 0, aa = 0, tp = 0; var blocks = new List<BlockTotal>();
        foreach (var m in p.Mh)
        {
            double bk = 0, bkg = 0; var side = SideOf(m);
            foreach (var r in m.DongList) { var k = N0(r.Kien); var n = RowNet(r); bk += k; bkg += n is null ? 0 : k * n.Value; }
            kien += bk; kg += bkg; if (side == "TP") tp += bk; else aa += bk;
            blocks.Add(new BlockTotal(bk, bkg, side));
        }
        return new Totals(kien, kg, aa, tp, blocks);
    }

    /// <summary>Số → chữ tiếng Việt ("Một nghìn chín trăm sáu mươi").</summary>
    public static string DocSo(double n0)
    {
        var n = (long)Math.Round(n0);
        if (n == 0) return "Không";
        string[] cs = { "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín" };
        string Ba(long x, bool full)
        {
            long tr = x / 100, ch = x % 100 / 10, dv = x % 10; var o = new List<string>();
            if (full || tr > 0) o.Add(cs[tr] + " trăm");
            if (ch > 1) { o.Add(cs[ch] + " mươi"); if (dv == 1) o.Add("mốt"); else if (dv == 5) o.Add("lăm"); else if (dv > 0) o.Add(cs[dv]); }
            else if (ch == 1) { o.Add("mười"); if (dv == 5) o.Add("lăm"); else if (dv > 0) o.Add(cs[dv]); }
            else if (dv > 0) { if (full || tr > 0) o.Add("lẻ"); o.Add(cs[dv]); }
            return string.Join(" ", o);
        }
        string[] units = { "", " nghìn", " triệu", " tỷ" };
        var parts = new List<(long V, string U)>(); var i = 0; var x2 = n;
        while (x2 > 0) { parts.Insert(0, (x2 % 1000, units[i])); x2 /= 1000; i++; }
        var res = Regex.Replace(string.Join(" ", parts.Select((g, j) => g.V > 0 ? Ba(g.V, j > 0) + g.U : "").Where(s => s.Length > 0)), @"\s+", " ").Trim();
        return char.ToUpper(res[0]) + res[1..];
    }

    /// <summary>Ô MKS: số mẫu (&gt;0) → số; có ghi nhưng không phải số → " " (1 dấu cách = "( MKS )" như phiếu thật); trống → "".</summary>
    public static object MksCell(string? v)
    {
        var s = (v ?? "").Trim();
        if (s.Length == 0) return "";
        var n = Num(s);
        return n is { } d && !double.IsNaN(d) && d > 0 ? d : " ";
    }
    public static string MksTxt(string? v) => MksCell(v) switch { "" => "", " " => "( MKS )", double d => "(" + Fmt(d) + "MKS )", _ => "" };
    private static string Fmt(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ kiểm tra

    public sealed record Layout(int NB, int C, int Top, int Bottom0, string Sheet, (int H, int First, int Last, int Tot, int Lot, int Sp)[] Blocks);
    public static readonly Layout XuatKhauLayout = new(6, 20, 15, 160, "01", new[]
    {
        (16, 17, 36, 37, 38, 39), (40, 41, 60, 61, 62, 63), (64, 65, 84, 85, 86, 87), (88, 89, 108, 109, 110, 111), (112, 113, 132, 133, 134, 135), (136, 137, 156, 157, 158, 159),
    });

    public static List<string> Validate(Phieu p, Layout? lay = null)
    {
        lay ??= XuatKhauLayout;
        var e = new List<string>();
        if (p.SoPhieu.Trim().Length == 0) e.Add("Chưa nhập Số phiếu.");
        var used = p.Mh.Where(UsedBlock).ToList();
        if (used.Count == 0) e.Add("Chưa có mặt hàng nào.");
        if (used.Count > lay.NB) e.Add($"Mẫu chỉ chứa {lay.NB} mặt hàng (đang có {used.Count}).");
        for (var i = 0; i < used.Count; i++)
        {
            var m = used[i]; var lb = "Mặt hàng " + (i + 1);
            if (m.Ten.Trim().Length == 0) e.Add(lb + ": chưa có Tên hàng.");
            if (MaLay(m).First.Length == 0) e.Add(lb + ": chưa có Mã hàng.");
            var rows = RowsOf(m);
            if (rows.Count == 0) e.Add(lb + ": chưa có dòng DATE nào.");
            if (MaLay(m).PerRow) for (var j = 0; j < rows.Count; j++) if (RowMa(m, rows[j]).Length == 0) e.Add($"{lb}, dòng {j + 1}: chưa có Mã hàng của size {rows[j].Size} (mặt hàng có nhiều mã).");
            if (rows.Count > lay.C) e.Add($"{lb}: {rows.Count} dòng DATE — mẫu chỉ chứa {lay.C}.");
            for (var j = 0; j < rows.Count; j++)
            {
                var r = rows[j]; var lr = $"{lb}, dòng {j + 1}";
                if (!DateOk(NormDate(r.Date))) e.Add(lr + ": DATE phải dạng 29.05.2026.");
                if (r.Size.Trim().Length == 0 && r.NoSize != true) e.Add(lr + ": chưa có Size.");
                var k = Num(r.Kien);
                if (k is null || double.IsNaN(k.Value) || k <= 0) e.Add(lr + ": Số kiện phải là số lớn hơn 0.");
                if (RowNet(r) is null) e.Add($"{lr}: Packing \"{r.Packing}\" không tính được NET — gõ dạng 1.0 kg x 5 x 2.");
                var mk = r.Mks.Trim();
                if (mk.Length > 0 && double.IsNaN(Num(mk) ?? 0) && !Regex.IsMatch(mk, "^x$", RegexOptions.IgnoreCase)) e.Add(lr + ": MKS gõ x (có) hoặc số mẫu.");
            }
        }
        return e;
    }

    // ------------------------------------------------------------------ số phiếu tự cấp

    private static readonly Regex ReSo = new(@"(\d+)\s*\.\s*(\d{1,2})\s*XK\s*[-\s]*(AA|TP)\s*(\d+)", RegexOptions.IgnoreCase);
    public static (int Nn, int Mm, string Side, int Kk)? ParseSo(string? sp)
    {
        var m = ReSo.Match(sp ?? "");
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Value.ToUpperInvariant(), int.Parse(m.Groups[4].Value)) : null;
    }
    public static string FmtSo(int nn, int mm, string side, int kk) => $"{Pad2(nn)}.{Pad2(mm)} XK{side} {Pad2(kk)}";

    /// <summary>Phiếu thuộc hàng nào: hàng có nhiều kiện nhất trong các mặt hàng đã nhập; chưa có gì → AA.</summary>
    public static string SideOfPhieu(Phieu p)
    {
        double ka = 0, kt = 0; int ua = 0, ut = 0;
        foreach (var m in p.Mh)
        {
            if (!UsedBlock(m)) continue;
            var sd = SideOf(m); if (sd == "TP") ut++; else ua++;
            foreach (var r in m.DongList) if (Num(r.Kien) is { } n && n > 0) { if (sd == "TP") kt += n; else ka += n; }
        }
        return kt > ka || (kt == ka && ut > ua) ? "TP" : "AA";
    }

    /// <summary>"NN.MM XKAA KK": MM = tháng ngày xuất; NN = số phiếu trong tháng; KK = số phiếu trong tháng THEO HÀNG (AA / TP). Sang tháng mới / năm mới → về 01.</summary>
    public static (string SoPhieu, string O40) NextNumbers(IEnumerable<Phieu> list, Phieu p, string? side = null)
    {
        var arr = list.Where(x => x.Id != p.Id && (p.Loai.Length == 0 || x.Loai == p.Loai)).ToList();
        var ng = Regex.Match(p.Ngay, @"^(\d{4})-(\d{2})-\d{2}$"); if (!ng.Success) ng = Regex.Match(TodayKey(), @"^(\d{4})-(\d{2})-\d{2}$");
        var year = ng.Groups[1].Value; var mm = int.Parse(ng.Groups[2].Value); side ??= SideOfPhieu(p);
        int maxNn = 0, maxKk = 0;
        foreach (var x in arr)
        {
            var q = ParseSo(x.SoPhieu);
            if (q is null || q.Value.Mm != mm) continue;
            var y = x.Ngay.Length >= 4 ? x.Ngay[..4] : "";
            if (Regex.IsMatch(y, @"^\d{4}$") && y != year) continue;
            if (q.Value.Nn > maxNn) maxNn = q.Value.Nn;
            if (q.Value.Side == side && q.Value.Kk > maxKk) maxKk = q.Value.Kk;
        }
        var so = FmtSo(maxNn + 1, mm, side, maxKk + 1);
        Match? b40 = null;
        foreach (var x in arr)
        {
            if (!x.Ngay.StartsWith(year)) continue;
            var m = Regex.Match(x.O40.Trim(), @"^(.*?XK\s*)(\d+)(.*)$", RegexOptions.IgnoreCase);
            if (m.Success && (b40 is null || long.Parse(m.Groups[2].Value) > long.Parse(b40.Groups[2].Value))) b40 = m;
        }
        return (so, b40 is null ? "" : b40.Groups[1].Value + (long.Parse(b40.Groups[2].Value) + 1) + b40.Groups[3].Value);
    }

    /// <summary>Tự cập nhật số phiếu / mã 40RF khi đổi ngày hoặc hàng — chỉ động vào số do hệ thống cấp hoặc ô trống; phiếu đã lưu có số thì giữ.</summary>
    public static bool RefreshNumbers(IEnumerable<Phieu> list, Phieu p)
    {
        var changed = false;
        var cur = p.SoPhieu.Trim();
        if ((cur.Length == 0 || (p.SoPhieuTuCap.Length > 0 && cur == p.SoPhieuTuCap)) && !(p.Id.Length > 0 && cur.Length > 0))
        {
            var nx = NextNumbers(list, p).SoPhieu; p.SoPhieuTuCap = nx;
            if (nx != cur) { p.SoPhieu = nx; changed = true; }
        }
        if (p.XuatLuc.Length == 0)
        {
            var c40 = p.O40.Trim();
            if ((c40.Length == 0 || (p.O40TuCap.Length > 0 && c40 == p.O40TuCap)) && !(p.Id.Length > 0 && c40.Length > 0))
            {
                var nx = NextNumbers(list, p).O40;
                if (nx.Length == 0) { if (c40.Length > 0 && c40 == p.O40TuCap) { p.O40 = ""; p.O40TuCap = ""; changed = true; } }
                else { p.O40TuCap = nx; if (nx != c40) { p.O40 = nx; changed = true; } }
            }
        }
        return changed;
    }

    public static string FileName(Phieu p)
    {
        var q = ParseSo(p.SoPhieu);
        var b = q is { } x
            ? $"{x.Side}{Pad2(x.Kk)} {Pad2(x.Nn)}.{Pad2(x.Mm)}XK{x.Side}{(p.Lsx.Length > 0 ? " LSX " + p.Lsx : "")} (PHIẾU XUẤT KHO)"
            : Regex.Replace(p.SoPhieu.Length > 0 ? p.SoPhieu : "Phieu xuat", @"^Số\s*:\s*", "", RegexOptions.IgnoreCase) + (p.Lsx.Length > 0 ? " LSX " + p.Lsx : "") + " (" + Dm(p.Ngay) + ")" + (p.Khach.Length > 0 ? " " + p.Khach : "");
        return Regex.Replace(Regex.Replace(b, @"[\\/:*?""<>|]+", "-"), @"\s+", " ").Trim() + ".xlsm";
    }

    // ------------------------------------------------------------------ email

    /// <summary>Nội dung email gửi kèm phiếu (chỉ liệt kê mặt hàng / dòng CÓ số liệu).</summary>
    public static string EmailText(Phieu p)
    {
        var t = TotalsOf(p); var x = p.Xe; var g = p.Gc; var unit = p.DonVi == "bdl" ? "bdl" : "kiện"; var L = new List<string>();
        L.Add("Dear Anh Chị,");
        L.Add("BP Kho An An gửi List xuất hàng ngày " + Dm(p.Ngay) + " " + LyDoOf(p) + (p.Kho == "An An" || p.Kho.Length == 0 ? " - An An sản xuất " : ""));
        L.Add("Người nhận hàng : " + x.TaiXe + " ( " + x.Sdt + " ) ");
        L.Add("Cont/Xe : " + x.Cont.ToUpperInvariant() + " / " + x.SoXe.ToUpperInvariant());
        L.Add("Seal : " + x.Seal);
        L.Add("");
        var n = 0;
        for (var mi = 0; mi < p.Mh.Count; mi++)
        {
            var m = p.Mh[mi];
            if (!UsedBlock(m)) continue; n++;
            var side = SideOf(m); var ml = MaLay(m);
            L.Add(n + "/ " + TenTitle(p, m) + (side == "TP" ? " - Thuận Phước sản xuất " : " - An An sản xuất ") + (ml.Title.Length > 0 ? "( Mã hàng: " + ml.Title + " )" : ""));
            foreach (var r in m.DongList)
            {
                if (!UsedRow(r)) continue;
                var rm = RowMa(m, r); var mk = MksTxt(r.Mks);
                L.Add("- DATE: " + r.Date + " - " + r.Size + ": " + Fmt(N0(r.Kien)) + " " + unit + (ml.PerRow && rm.Length > 0 ? " ( Mã hàng: " + rm + " )" : mk.Length > 0 ? " " : "") + mk);
            }
            L.Add("Tổng: " + Fmt(t.Blocks[mi].Kien) + " " + unit);
            if (m.Lo.Length > 0) L.Add("Mã số lô hàng:  " + m.Lo);
            L.Add("");
        }
        L.Add("Tổng xuất: " + Fmt(t.Kien) + " " + unit + " ");
        L.Add("- Đóng sole: " + (g.Sole == "co" ? "Có " : "Không"));
        L.Add("- Nhiệt kế: " + (g.NhietKe == "co" ? "Có " + (g.MaNhietKe.Length > 0 ? "- Mã số nhiệt kế: " + g.MaNhietKe : "") : "Không"));
        L.Add("- Người báo: " + g.NguoiBao);
        return string.Join("\n", L);
    }

    private static readonly Dictionary<string, Regex> XeLabel = new()
    {
        ["taiXe"] = new(@"h[oọ]\s*t[eê]n\s*t[aà]i\s*x[eế]|t[aà]i\s*x[eế]", RegexOptions.IgnoreCase),
        ["sdt"] = new(@"s[oố]\s*(?:đi[eệ]n\s*tho[aạ]i|đt|dt)|đi[eệ]n\s*tho[aạ]i|sđt", RegexOptions.IgnoreCase),
        ["soXe"] = new(@"s[oố]\s*xe|bi[eể]n\s*s[oố]", RegexOptions.IgnoreCase),
        ["cont"] = new(@"s[oố]\s*cont(?:ainer)?|cont\s*s[oố]", RegexOptions.IgnoreCase),
        ["seal"] = new(@"s[oố]\s*seal|seal\s*s[oố]", RegexOptions.IgnoreCase),
        ["booking"] = new(@"s[oố]\s*booking|booking", RegexOptions.IgnoreCase),
        ["nhietDo"] = new(@"nhi[eệ]t\s*đ[oộ](?:\s*c[aà]i\s*đ[aặ]t)?(?:\s*cont)?", RegexOptions.IgnoreCase),
    };

    public sealed record XeParse(Dictionary<string, string> Xe, List<string> Found, List<string> Missing);

    /// <summary>Tách email "Thông tin xe": mỗi dòng "Nhãn: giá trị" cho 7 mục. Không thấy nhãn → mục đó báo THIẾU (không đoán).</summary>
    public static XeParse ParseXeEmail(string? text)
    {
        var src = (text ?? "").Replace(' ', ' ').Replace("\r", "");
        var flat = !src.Trim().Contains('\n');
        if (flat) src = Regex.Replace(src, @"\s+(?=(?:h[oọ]\s*t[eê]n\s*t[aà]i\s*x[eế]|s[oố]\s*(?:đi[eệ]n\s*tho[aạ]i|xe|cont|seal|booking)|nhi[eệ]t\s*đ[oộ])[^:：\n]{0,30}[:：])", "\n", RegexOptions.IgnoreCase);
        var o = new Dictionary<string, string>(); var found = new List<string>(); var missing = new List<string>();
        foreach (var (k, _, _, lab) in XeFields)
        {
            var val = "";
            foreach (var line in src.Split('\n'))
            {
                var m = Regex.Match(line, @"^\s*[-•*]?\s*([^:：]{2,45})[:：]\s*(.+?)\s*$");
                if (!m.Success) continue;
                if (XeLabel[k].IsMatch(m.Groups[1].Value) && !(k == "taiXe" && Regex.IsMatch(m.Groups[1].Value, @"xe\s*$", RegexOptions.IgnoreCase) && Regex.IsMatch(m.Groups[1].Value, "s[oố]", RegexOptions.IgnoreCase)))
                { val = m.Groups[2].Value.Trim(); break; }
            }
            if (val.Length > 0)
            {
                if (k == "nhietDo")
                {
                    val = Regex.Replace(Regex.Replace(val, @"\s*°\s*C", "℃", RegexOptions.IgnoreCase), @"\s*độ\s*C", "℃", RegexOptions.IgnoreCase);
                    if (flat) { var mt = Regex.Match(val, @"^[-+−]?\s*\d+(?:[.,]\d+)?\s*(?:℃|°\s*C|độ\s*C)?", RegexOptions.IgnoreCase); if (mt.Success) val = mt.Value.Trim(); }
                }
                o[k] = Regex.Replace(val, @"\s{2,}", " "); found.Add(lab);
            }
            else missing.Add(lab);
        }
        return new XeParse(o, found, missing);
    }

    public static XeParse ApplyXeEmail(Phieu p, string? text)
    {
        var r = ParseXeEmail(text);
        foreach (var (k, v) in r.Xe) p.Xe.Set(k, v);
        return r;
    }

    // ------------------------------------------------------------------ bảng ô cho mẫu Excel

    public sealed record XkPlan(Dictionary<string, object?> Cells, FillOptions Options, string Unit);

    public static (string Text, double Serial) NgayXuat(Phieu p)
    {
        var m = Regex.Match(p.Ngay, @"^(\d{4})-(\d{2})-(\d{2})$"); if (!m.Success) m = Regex.Match(TodayKey(), @"^(\d{4})-(\d{2})-(\d{2})$");
        int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value);
        return ($"Ngày {Pad2(d)} tháng {Pad2(mo)} năm {y}", new DateTime(y, mo, d).ToOADate());
    }

    public static XkPlan BuildPlan(Phieu p, Layout? lay = null)
    {
        lay ??= XuatKhauLayout;
        var c = new Dictionary<string, object?>(); var hide = new List<int>(); var styles = new List<(int, int)>();
        var g = p.Gc; var t = TotalsOf(p); var unit = p.DonVi == "bdl" ? "bdl" : "kiện"; var kho = p.Kho.Length > 0 ? p.Kho : "An An";
        c["A6"] = "( " + kho + " )"; c["A7"] = "Kho xuất: " + kho;
        var nx = NgayXuat(p); var ld = LyDoLines(p);
        c["G7"] = nx.Text; c["G8"] = "Số : " + p.SoPhieu; c["A9"] = "Lý do xuất : " + ld.Head; c["A10"] = ld.Lsx;
        c["A11"] = "Theo hợp đồng số : " + p.HopDong; c["G11"] = "Kiểu xuất : " + p.KieuXuat;
        c["C12"] = p.LoaiCont == "20RF" ? "X" : ""; c["F12"] = p.LoaiCont == "40RF" ? "X" : ""; c["J12"] = p.O40;
        c["N1"] = new Forced(nx.Serial);
        c["S1"] = "Thông tin xe:";
        foreach (var (k, cell, pre, _) in XeFields) c[cell] = pre + p.Xe.Get(k);
        var used = p.Mh.Where(UsedBlock).ToList();
        for (var bi = 0; bi < lay.Blocks.Length; bi++)
        {
            var bl = lay.Blocks[bi];
            if (bi >= used.Count) { for (var r = bl.H; r <= bl.Sp; r++) hide.Add(r); continue; }
            var m = used[bi]; var rows = RowsOf(m); var side = SideOf(m); var ml = MaLay(m);
            c["B" + bl.H] = TenTitle(p, m); c["M" + bl.H] = ml.Title; c["H" + bl.H] = side == "TP" ? "x" : "";
            for (var i = 0; i < rows.Count && i < lay.C; i++)
            {
                var r = rows[i]; var rr = bl.First + i; var k = N0(r.Kien); var net = RowNet(r);
                c["B" + rr] = "DATE: " + r.Date; c["C" + rr] = r.Size; c["E" + rr] = r.Packing;
                c["G" + rr] = side == "AA" ? k : ""; c["H" + rr] = side == "TP" ? k : "";
                c["K" + rr] = new Forced(net is null ? "" : net.Value); c["L" + rr] = MksCell(r.Mks);
                if (ml.PerRow) c["M" + rr] = RowMa(m, r);
            }
            for (var i = rows.Count; i < lay.C; i++) hide.Add(bl.First + i);
            if (rows.Count < lay.C) styles.Add((bl.Last, bl.First + rows.Count - 1));
            c["B" + bl.Lot] = "Mã số lô hàng:" + (m.Lo.Length > 0 ? "  " + m.Lo : "");
            if (m.Lo.Length == 0) hide.Add(bl.Lot);
        }
        var b0 = lay.Bottom0;
        c["B" + b0] = g.HinhAnh; c["B" + (b0 + 2)] = "Bằng chữ: " + (t.Kien > 0 ? DocSo(t.Kien) + " " + unit : "");
        c["C" + (b0 + 4)] = "Bắt đầu xuất: " + g.NdBatDau; c["F" + (b0 + 4)] = "Khi rời nhà máy:   " + g.NdRoi;
        c["C" + (b0 + 6)] = g.Sole == "co" ? "X" : ""; c["F" + (b0 + 6)] = g.Sole == "khong" ? "X" : "";
        c["C" + (b0 + 8)] = g.NhietKe == "co" ? "X" : ""; c["F" + (b0 + 8)] = g.NhietKe == "khong" ? "X" : "";
        c["C" + (b0 + 12)] = g.MaNhietKe; c["I" + (b0 + 6)] = "- Cont đến: " + g.ContDen; c["I" + (b0 + 8)] = "- Cont đi: " + g.ContDi; c["I" + (b0 + 12)] = "- Cửa XH: " + g.CuaXH;
        c["N" + (b0 + 3)] = "- Người báo: " + g.NguoiBao;
        var hasTP = used.Any(m => SideOf(m) == "TP");   // cột TP (H) bị ẩn sẵn trong mẫu; có hàng Thuận Phước thì hiện
        var o = new FillOptions
        {
            Sheet = lay.Sheet, Hide = hide, CopyStyle = styles, Unit = unit,
            ShowCols = hasTP ? new() { 8 } : new(), HideCols = hasTP ? new() : new() { 8 },
            Wrap = new() { new WrapSpec("A9", 50, 20.25, new[] { "G9" }), new WrapSpec("A10", 50, 20.25, new[] { "G10" }) },
            Fit = new FitSpec(new[] { "C", "E", "G", "H", "K", "L", "M" }, new() { ["K"] = 2, ["L"] = 2 }, new() { ["C"] = "D" }, lay.Top, lay.Bottom0 - 1),
        };
        return new XkPlan(c, o, unit);
    }
}

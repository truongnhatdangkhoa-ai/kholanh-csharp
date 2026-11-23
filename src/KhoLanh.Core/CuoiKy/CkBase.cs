using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.CuoiKy;

// SỐ LIỆU CUỐI KỲ (đối chiếu tồn cuối kỳ theo mã, kg) — chuyển từ dc2-*.js của app cũ.
// CÔNG THỨC (kg): Tồn kho = Tồn An An + Tồn kho gửi ; Chênh lệch = Tồn kho − Kế toán.
// Chỉ so TỒN CUỐI KỲ; đầu kỳ / nhập / xuất chỉ để tham khảo + kiểm tra tự khớp. Nguyên nhân gắn nhãn "xác định" / "nghi ngờ" + độ tin cậy;
// không có bằng chứng → "Chưa xác định nguyên nhân".

/// <summary>Tiện ích dùng chung (chuẩn hoá chữ / số / ngày y như app cũ).</summary>
public static class Ck
{
    public static double R6(double n) { var y = Math.Floor(n * 1e6 + 0.5) / 1e6; return y == 0 ? 0 : y; }
    public static double Rnd(double x) => Math.Floor(x + 0.5);

    /// <summary>toLocaleString('en-US', { maximumFractionDigits: 3 })</summary>
    public static string Vn(double n)
    {
        var r = Math.Round(n, 3, MidpointRounding.AwayFromZero);
        var s = Math.Abs(r).ToString("#,0.###", CultureInfo.InvariantCulture);
        return (n < 0 || (n == 0 && double.IsNegative(n)) ? "-" : "") + s;
    }
    public static string Sg(double n) => (n > 0 ? "+" : "") + Vn(n);
    public static string Dmy(string? iso) => string.IsNullOrEmpty(iso) ? "" : string.Join("/", Enumerable.Reverse(iso.Split('-')));
    public static string Pct(double x) => Rnd(x * 100).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>String(v) kiểu JS cho ô Excel.</summary>
    public static string Str(object? v) => v switch
    {
        null => "",
        string s => s,
        double d => JsNum(d),
        int i => i.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };
    public static string JsNum(double d) => double.IsNaN(d) ? "NaN" : d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);

    public static string Txt(object? v) => Regex.Replace(Regex.Replace(Str(v), "_x000d_", " ", RegexOptions.IgnoreCase), @"\s+", " ").Trim();
    public static bool NonEmpty(object? v) => v is not null && !(v is string s && s.Trim().Length == 0) && Str(v).Trim().Length > 0;

    public static string Fold(string? s)
    {
        var t = (s ?? "").Normalize(NormalizationForm.FormD); var sb = new StringBuilder(t.Length);
        foreach (var ch in t) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        var r = sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();
        r = Regex.Replace(r, @"[._\-/\\():#*]+", " ");
        return Regex.Replace(r, @"\s+", " ").Trim();
    }
    public static string CleanFold(object? x) => Regex.Replace(Str(x).Replace(',', ' '), @"\s+", " ").Trim();
    public static string FoldClean(object? s) => CleanFold(Fold(Str(s)));
    public static string NormMa(object? v)
    {
        var s = Regex.Replace(Str(v).Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"\s+", "");
        return s.StartsWith("TPC-", StringComparison.Ordinal) ? s[4..] : s;
    }

    /// <summary>null = ô trống · NaN = không đọc được · số.</summary>
    public static double? Num(object? v)
    {
        if (v is null) return null;
        if (v is double d) return double.IsFinite(d) ? d : double.NaN;
        if (v is int i) return i;
        if (v is string s0 && s0.Length == 0) return null;
        var s = Regex.Replace(Str(v).Trim(), @"\s+", "");
        if (s.Length == 0 || s == "-") return null;
        var neg = false;
        if (Regex.IsMatch(s, @"^\(.*\)$")) { neg = true; s = s[1..^1]; }
        if (Regex.IsMatch(s, @"[^\d.,+\-]")) return double.NaN;
        bool hasDot = s.Contains('.'), hasComma = s.Contains(',');
        if (hasDot && hasComma) s = s.LastIndexOf(',') > s.LastIndexOf('.') ? ReplaceFirst(s.Replace(".", ""), ",", ".") : s.Replace(",", "");
        else if (hasComma) s = Regex.IsMatch(s, @"^[+-]?\d{1,3}(,\d{3})+$") ? s.Replace(",", "") : ReplaceFirst(s, ",", ".");
        else if (hasDot && Regex.IsMatch(s, @"^[+-]?\d{1,3}(\.\d{3})+$")) s = s.Replace(".", "");
        if (s.Length == 0) return 0; // Number('') = 0
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)) return double.NaN;
        return neg ? -n : n;
    }
    private static string ReplaceFirst(string s, string a, string b) { var i = s.IndexOf(a, StringComparison.Ordinal); return i < 0 ? s : s[..i] + b + s[(i + a.Length)..]; }

    public static DateTime? ParseDate(string? s0)
    {
        var s = (s0 ?? "").Trim();
        var m = Regex.Match(s, @"^(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{4}|\d{2})(?!\d)");
        if (m.Success) { var y = int.Parse(m.Groups[3].Value); if (y < 100) y += 2000; return Mk(y, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)); }
        m = Regex.Match(s, @"^(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})");
        if (m.Success) return Mk(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        return null;
        static DateTime? Mk(int y, int mo, int d) { if (mo < 1 || mo > 12 || d < 1) return null; if (d > DateTime.DaysInMonth(Math.Clamp(y, 1, 9999), mo)) return null; return new DateTime(y, mo, d); }
    }
    public static string IsoDay(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    public static string ToIso(object? v) => v switch
    {
        null => "",
        double n => n > 20000 && n < 80000 ? IsoDay(new DateTime(1899, 12, 30).AddMilliseconds(Math.Round((n) * 86400000)).Date) : "",
        DateTime dt => IsoDay(dt.Date),
        string s when s.Length == 0 => "",
        _ => IsoDay(ParseDate(Str(v))),
    };

    public static bool IsBot(string? ten) => Regex.IsMatch(Fold(ten), @"^bot(\s|$)");
}

public sealed record CkField(string Key, string Label, string[] Names);
public sealed record CkSourceSpec(string Key, string Title, string Short, CkField[] Fields, string[] Must, string[][]? MustAny = null, string Hint = "")
{
    public CkField? F(string k) => Fields.FirstOrDefault(f => f.Key == k);
}

/// <summary>Cấu hình: ngưỡng, trạng thái, nguyên nhân, đặc tả các nguồn (giống dc2-config.js).</summary>
public static class CkConfig
{
    public static readonly (int a, int b)[] Seg = { (0, 4), (4, 5), (5, 6), (6, 7), (7, 8), (8, 10), (10, 13), (13, 17), (17, 19), (19, 21) };
    public static readonly string[] SegLabel = { "Sản phẩm", "Cấp đông", "Đóng gói", "Hóa chất", "Thị trường", "Khách hàng", "Mạ băng", "Size", "Trọng lượng", "Quy cách" };

    public static readonly (string v, string label)[] Status = { ("chua", "Chưa phân tích"), ("dang-tim", "Đang tìm nguyên nhân"), ("nghi-ma", "Có mã nghi ngờ"), ("nghi-phieu", "Có phiếu nghi ngờ"), ("cho", "Chờ xác nhận"), ("xong", "Đã xử lý") };
    public static string StatusLabel(string? v) => Status.FirstOrDefault(s => s.v == v).label ?? "";

    public static readonly (string code, string label)[] CauseList =
    {
        ("nhan-ma", "Nghi nhầm mã hàng"), ("quy-cach", "Nghi sai quy cách / size / trọng lượng"), ("chia-ma", "Nghi một mã bị chia thành nhiều mã"),
        ("lech-dau-ky", "Tồn đầu kỳ của kho và kế toán đã lệch nhau"), ("lech-phat-sinh", "Phát sinh nhập / xuất trong kỳ của kho và kế toán lệch nhau"),
        ("ngoai-danh-muc", "Mã không có trong danh mục thành phẩm"), ("ma-sai-mahoa", "Mã không hợp lệ theo Mã hóa sản phẩm"),
        ("ct-lech-nxt", "Chứng từ kế toán không khớp báo cáo NXT"), ("ct-bang-chenh", "Có chứng từ bằng đúng mức chênh lệch"), ("ct-trung", "Chứng từ bị ghi trùng"),
        ("ct-ngoai-ky", "Chứng từ nằm ngoài kỳ / sau thời điểm đối chiếu"), ("ct-khong-doi-ung", "Chứng từ chỉ có ở 1 bên (kho ↔ kế toán)"), ("ct-lech-so", "Cùng chứng từ nhưng số lượng khác nhau"),
        ("tu-khong-khop", "Số liệu một nguồn không tự khớp (đầu + nhập − xuất ≠ cuối)"), ("sai-trong-luong", "Sai trọng lượng Net / đơn vị tính"), ("don-vi", "Nghi nhầm đơn vị (thùng ↔ kg)"),
        ("chi-co-1-ben", "Mã chỉ có ở một nguồn"), ("gui-ngoai-ky", "Báo cáo kho gửi không đúng kỳ"), ("kiem-ke", "Kiểm kê thực tế cho biết bên nào đúng"), ("ton-am", "Tồn âm"),
        ("chua-xac-dinh", "Chưa xác định nguyên nhân"),
    };
    public static readonly Dictionary<string, string> Cause = CauseList.ToDictionary(c => c.code, c => c.label);
    public static readonly List<string> CauseOrder = CauseList.Select(c => c.code).ToList();
    public static readonly string[] Locator = { "lech-phat-sinh", "chi-co-1-ben", "ngoai-danh-muc", "ma-sai-mahoa", "tu-khong-khop", "gui-ngoai-ky" };

    private static CkField Fd(string k, string l, params string[] n) => new(k, l, n);

    public static readonly Dictionary<string, CkSourceSpec> Sources = new()
    {
        ["anan"] = new("anan", "Tồn kho An An", "An An", new[]
        {
            Fd("ma", "Mã hàng", "ma hang", "ma sp", "ma san pham", "ma thanh pham", "ma hh"),
            Fd("ten", "Tên hàng", "ten hang", "ten san pham", "ten sp", "ten thanh pham"),
            Fd("tonDau", "Tồn đầu (số lượng)", "ton dau", "ton dau ky"),
            Fd("nhap", "Nhập (số lượng)", "nhap", "nhap trong ky"),
            Fd("xuat", "Xuất (số lượng)", "xuat", "xuat trong ky"),
            Fd("tonCuoi", "Tồn cuối (số lượng)", "ton cuoi", "ton cuoi ky"),
            Fd("tonCuoiKg", "Tồn cuối (kg / NET)", "ton cuoi net", "ton cuoi ky net", "ton cuoi kg", "ton cuoi ky kg", "ton cuoi trong luong kg"),
            Fd("trongLuong", "Trọng lượng 1 đơn vị (kg)", "trong luong", "trong luong kg", "trong luong don vi"),
            Fd("dvt", "Đơn vị tính", "dvt", "don vi tinh", "don vi"),
        }, new[] { "ma" }, new[] { new[] { "tonCuoiKg" }, new[] { "tonCuoi", "trongLuong" } }, "Báo cáo Kho › Nhập xuất tồn (Mã hàng, Tồn cuối (NET), Trọng lượng…)"),
        ["ketoan"] = new("ketoan", "Kế toán", "Kế toán", new[]
        {
            Fd("ma", "Mã hàng", "ma thanh pham", "ma hang", "ma sp", "ma san pham", "ma vat tu"),
            Fd("ten", "Tên hàng", "ten thanh pham", "ten hang", "ten san pham", "ten sp"),
            Fd("dvt", "Đơn vị tính", "dvt", "don vi tinh"),
            Fd("tonDau", "Tồn đầu kỳ (kg)", "ton dau ky", "ton dau", "ton dau ky kg"),
            Fd("nhap", "Nhập trong kỳ (kg)", "nhap trong ky", "nhap"),
            Fd("xuat", "Xuất trong kỳ (kg)", "xuat trong ky", "xuat"),
            Fd("tonCuoi", "Tồn cuối kỳ (kg)", "ton ck", "ton cuoi ky", "ton cuoi", "ton cuoi ky kg", "so luong ke toan", "sl ke toan"),
        }, new[] { "ma", "tonCuoi" }, null, "Sheet NXT (Mã thành phẩm, Tồn đầu kỳ, Nhập / Xuất trong kỳ, Tồn CK) + sheet chứng từ"),
        ["ct"] = new("ct", "Chứng từ kế toán", "CT kế toán", new[]
        {
            Fd("phieu", "Số chứng từ", "so ct", "so chung tu", "ma phieu", "so phieu", "chung tu"),
            Fd("ngay", "Ngày", "ngay", "ngay ct", "ngay chung tu"),
            Fd("kho", "Kho", "kho"),
            Fd("ma", "Mã hàng", "ma", "ma hang", "ma sp", "ma thanh pham"),
            Fd("ten", "Tên hàng", "ten", "ten hang"),
            Fd("lsx", "Lệnh sản xuất", "lsx", "lenh san xuat"),
            Fd("loai", "Loại nghiệp vụ", "nx", "loai", "loai chung tu"),
            Fd("dir", "Chiều (N / X)", "x"),
            Fd("kien", "Số kiện", "kien goi", "kien", "so kien"),
            Fd("kg", "Số lượng (kg)", "slkg", "sl kg", "so luong kg", "khoi luong kg", "trong luong kg"),
            Fd("netPer", "Net / kiện", "net"),
        }, new[] { "phieu", "ngay", "ma", "kg" }),
        ["gui"] = new("gui", "Tồn kho gửi", "Kho gửi", new[]
        {
            Fd("ma", "Mã hàng", "ma hang", "ma hang item", "ma sp"),
            Fd("ten", "Tên hàng", "ten hang", "ten hang description", "ten san pham"),
            Fd("lo", "Số lô", "so lo", "lo"),
            Fd("size", "Size", "size"),
            Fd("quyCach", "Quy cách", "quy cach"),
            Fd("ngayNhap", "Ngày nhập kho", "ngay nhap kho"),
            Fd("phieu", "Phiếu nhập", "phieu nhap voucher", "phieu nhap", "voucher"),
            Fd("dauKien", "Tồn đầu — số kiện", "ton dau ky so kien"),
            Fd("dauKg", "Tồn đầu — kg", "ton dau ky trong luong kg", "ton dau ky trong luong"),
            Fd("nhapKien", "Nhập — số kiện", "nhap trong ky so kien"),
            Fd("nhapKg", "Nhập — kg", "nhap trong ky trong luong kg", "nhap trong ky trong luong"),
            Fd("xuatKien", "Xuất — số kiện", "xuat trong ky so kien"),
            Fd("xuatKg", "Xuất — kg", "xuat trong ky trong luong kg", "xuat trong ky trong luong"),
            Fd("cuoiKien", "Tồn cuối — số kiện", "ton cuoi ky so kien"),
            Fd("cuoiKg", "Tồn cuối — kg", "ton cuoi ky trong luong kg", "ton cuoi ky trong luong", "ton cuoi ky kg", "ton cuoi trong luong kg", "ton cuoi net", "ton cuoi kg"),
        }, new[] { "ma", "cuoiKg" }, null, "Báo cáo của kho gửi (Mã hàng, Số lô, Size, Tồn cuối kỳ › Trọng lượng (KG))"),
        ["kiemke"] = new("kiemke", "Kiểm kê thực tế", "Kiểm kê", new[]
        {
            Fd("ma", "Mã hàng", "ma hang", "ma sp", "ma san pham", "ma thanh pham"),
            Fd("ten", "Tên hàng", "ten hang", "ten san pham", "ten sp"),
            Fd("kg", "Kiểm kê (kg)", "kiem ke net", "net kiem ke", "net", "tong net", "net kg", "khoi luong kiem ke", "so luong kg", "sl kg", "khoi luong", "khoi luong kg", "tong kg", "kg", "thuc te", "ton thuc te"),
            Fd("ngay", "Ngày kiểm kê", "ngay kiem ke", "ngay"),
        }, new[] { "ma", "kg" }),
    };
    public static readonly Dictionary<string, string> SourceTitle = new()
    {
        ["anan"] = "Tồn kho An An", ["ketoan"] = "Kế toán", ["gui"] = "Tồn kho gửi", ["kiemke"] = "Kiểm kê thực tế", ["ct"] = "Chứng từ kế toán",
        ["anan_nhap"] = "Chứng từ nhập An An", ["anan_xuat"] = "Chứng từ xuất An An",
    };
    public static readonly string[] SourceOrder = { "anan", "ketoan", "gui" };
}

public sealed class CkOptions
{
    public double Tol = 0.05, PairTol = 0.5, MinScore = 40;
    public bool HideBot = true, ShowZero;
    public int MaxCand = 5;
    public string KiemKeNgay = "";
    public CkOptions Clone() => (CkOptions)MemberwiseClone();
}

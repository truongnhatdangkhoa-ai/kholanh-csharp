using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace KhoLanh.App;

/// <summary>
/// Bảng màu dùng chung: "Sáng dịu" (nền trắng ngà, không trắng tinh) và "Tối" (xám đen) + thu phóng cỡ chữ 90–130 %.
/// Mọi màu của app đi qua <see cref="Brush"/> / <see cref="Color"/>: màu viết theo bộ Sáng, khi Tối được đổi tự động
/// (giữ sắc màu, đảo độ sáng: nền sáng → nền tối, chữ đậm → chữ sáng). Brush được giữ lại và đổi màu tại chỗ
/// nên đổi giao diện có hiệu lực ngay, kể cả các brush khai báo static trong View.
/// Ảnh / bản in xuất ra ngoài vẽ trong <see cref="Raw"/> để luôn giữ màu gốc.
/// Lưu theo tài khoản trên máy (bảng settings: ui_theme:&lt;tài khoản&gt;, ui_scale:&lt;tài khoản&gt;).
/// </summary>
public static class AppTheme
{
    public enum Mode { Light, Dark }
    public static Mode Current { get; private set; } = Mode.Light;
    public static double Scale { get; private set; } = 1.0;
    public static event Action? Changed;

    private sealed class Entry { public required SolidColorBrush B; public required Color Src; public Color? Dark; }
    private static readonly Dictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);
    [ThreadStatic] private static int _raw;

    /// <summary>Trong khối using này màu giữ nguyên bản gốc (vẽ ảnh / bản in xuất file).</summary>
    public static IDisposable Raw() { _raw++; return new Leave(); }
    private sealed class Leave : IDisposable { private bool _d; public void Dispose() { if (!_d) { _d = true; _raw--; } } }

    /// <summary>Màu theo giao diện hiện tại (viết mã màu của bộ Sáng).</summary>
    public static Color Color(string hex) => _raw > 0 ? Avalonia.Media.Color.Parse(hex) : Map(Avalonia.Media.Color.Parse(hex), null);

    /// <summary>Brush dùng chung theo giao diện; <paramref name="dark"/> = màu riêng cho bộ Tối (bỏ trống = tự đổi).</summary>
    public static SolidColorBrush Brush(string hex, string? dark = null)
    {
        if (_raw > 0) return new SolidColorBrush(Avalonia.Media.Color.Parse(hex));
        var key = dark is null ? hex : hex + "|" + dark;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var e)) return e.B;
            var src = Avalonia.Media.Color.Parse(hex); Color? d = dark is null ? null : Avalonia.Media.Color.Parse(dark);
            e = new Entry { Src = src, Dark = d, B = new SolidColorBrush(Map(src, d)) };
            Cache[key] = e; return e.B;
        }
    }
    public static SolidColorBrush Brush(Color c) => Brush(c.ToString());

    // Các màu hay dùng
    public static SolidColorBrush Card => Brush("#FFFFFF");
    public static SolidColorBrush Muted => Brush("#5D6B78");
    public static SolidColorBrush Accent => Brush("#1F6FD6");
    public static SolidColorBrush Danger => Brush("#B42318");
    public static SolidColorBrush Text => Brush("#1A2433");

    /// <summary>
    /// Bộ màu "Băng sạch" (1.43): màu chính của bộ Sáng → màu Tối THIẾT KẾ RIÊNG (không đảo tự động). Màu khác ngoài bảng vẫn đổi theo HSL.
    /// Viết mã màu bộ Sáng trong View; màu nào cần cặp Tối riêng thì thêm vào đây.
    /// </summary>
    private static readonly Dictionary<string, string> DesignedDark = new(StringComparer.OrdinalIgnoreCase)
    {
        // nền / bề mặt / đường kẻ
        ["#F4F6F9"] = "#12161C", ["#F3F6F9"] = "#12161C", ["#FFFFFF"] = "#1A2028", ["#F8FAFC"] = "#20272F", ["#F1F4F8"] = "#20272F", ["#F7F9FC"] = "#20272F",
        ["#E3E8EF"] = "#2B333D", ["#E3E7ED"] = "#2B333D", ["#E8EEF3"] = "#2B333D", ["#D9E2EA"] = "#39434F", ["#D5DBE3"] = "#39434F", ["#D0D7E2"] = "#39434F", ["#CBD3DD"] = "#39434F", ["#CDD5DF"] = "#39434F",
        // chữ
        ["#1A2433"] = "#E5EAF0", ["#1F2937"] = "#E5EAF0", ["#4A5869"] = "#AEB8C4", ["#475569"] = "#AEB8C4", ["#5D6B78"] = "#9AA4AF", ["#7A8797"] = "#808C99",
        // màu nhấn + vùng chọn
        ["#1F6FD6"] = "#4C93EA", ["#1F6FD1"] = "#4C93EA", ["#1859AE"] = "#6AA6EF", ["#1F8FD1"] = "#4C93EA", ["#E6F0FC"] = "#1C2E45", ["#E3EEFB"] = "#1C2E45", ["#D9E6F7"] = "#233A57", ["#E3F2FB"] = "#1C2E45",
        ["#10243D"] = "#0D1117", ["#0B2A45"] = "#0D1117",
        // trạng thái: nền nhạt + chữ đậm
        ["#E3F4EA"] = "#173327", ["#1E7B4A"] = "#4CC287", ["#FFF2D6"] = "#3A2C12", ["#9A5B00"] = "#E3A447", ["#FDE8E6"] = "#3E1D1A", ["#B42318"] = "#F07468",
        ["#EEEBFC"] = "#2A2543", ["#5B4BC4"] = "#A99BF2",
    };

    /// <summary>
    /// Quy màu cũ của các View về bộ "Băng sạch" (1.45) — áp cho cả Sáng và Tối, KHÔNG áp khi vẽ ảnh / bản in xuất file (<see cref="Raw"/>):
    /// tô nền đậm (cam, vàng đậm, tím, xanh lá) → nền nhạt trạng thái; màu thương hiệu cũ → màu mới; xanh / cam / đỏ chữ → màu trạng thái chuẩn.
    /// Nhờ vậy mọi màn hình cùng một ngôn ngữ màu mà không phải sửa từng View.
    /// </summary>
    private static readonly Dictionary<string, string> Unify = BuildUnify();
    private static Dictionary<string, string> BuildUnify()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void To(string target, params string[] src) { foreach (var x in src) d[x] = target; }
        To("#E3F4EA", "#E2EFDA", "#DCEBDD", "#E6F4EA", "#E2F0D9", "#D9EAD3", "#E8F5E9", "#C6EFCE");                  // nền "khớp / đạt"
        To("#FFF2D6", "#FFF4CC", "#FFF1C9", "#FFE699", "#FFF2CC", "#FFF2B3", "#FFF1CC", "#FFE2A0", "#FFEB9C", "#FFF3CD"); // nền "cảnh báo"
        To("#FDE8E6", "#F8CBAD", "#FDE7E7", "#FCE4D6", "#FBE3D6", "#FDE2E1", "#FCE8E6", "#FBD5C8", "#F6B5A8", "#FDF0EC", "#FFC7CE", "#F4CCCC"); // nền "lỗi / lệch"
        To("#EEEBFC", "#D9C6F0", "#E4DFEC", "#EDE7F6");                                                                  // nền "thông tin"
        To("#E6F0FC", "#DCE6F0", "#DDEBF7", "#E3F2FB", "#E8F1FB", "#DEEAF6", "#D9E6F7", "#E3EEFB");                     // nền nhấn / đang chọn
        To("#F1F4F8", "#EDEDED", "#F3F3F3", "#EEF2F7", "#EEF1F5", "#EEF0F3", "#E5E9F0", "#F1F3F5");                     // nền xám phụ
        To("#F8FAFC", "#F6F9FC", "#F4F7FB", "#F5F8FB", "#F3F6FA", "#F7F9FC");
        To("#E3E8EF", "#D0D7E2", "#D9E2EA", "#D5DBE3", "#E3E7ED", "#E8EEF3", "#D3D3D3");                                // đường kẻ
        To("#1A2433", "#0B2A45", "#1F2937");                                                                              // chữ đậm / tiêu đề
        To("#4A5869", "#134B73");
        To("#1F6FD6", "#1F8FD1", "#2563EB", "#1F6FD1", "#1877B0");                                                        // màu nhấn
        To("#1E7B4A", "#2E7D32", "#1E7B34", "#1B5E20", "#1E8E3E", "#2E7D32");                                             // chữ "khớp / đạt"
        To("#9A5B00", "#8A5A00", "#7A5200", "#B7791F");                                                                   // chữ "cảnh báo"
        To("#B42318", "#B23B00", "#C0392B", "#C62828", "#D32F2F");                                                        // chữ "lỗi / lệch"
        return d;
    }
    private static Color Unified(Color c) =>
        c.A == 255 && Unify.TryGetValue($"#{c.R:X2}{c.G:X2}{c.B:X2}", out var u) ? Avalonia.Media.Color.Parse(u) : c;

    /// <summary>Đổi màu theo giao diện: Sáng = trắng tinh → trắng dịu (đỡ chói); Tối = bảng thiết kế riêng, còn lại đảo độ sáng.</summary>
    public static Color Map(Color c, Color? dark)
    {
        c = Unified(c);
        if (Current == Mode.Light)
            return c.R >= 252 && c.G >= 252 && c.B >= 252 ? Avalonia.Media.Color.FromArgb(c.A, 0xFB, 0xFC, 0xFD) : c;
        if (dark is { } d) return d;
        if (c.A == 255 && DesignedDark.TryGetValue($"#{c.R:X2}{c.G:X2}{c.B:X2}", out var dd)) return Avalonia.Media.Color.Parse(dd);
        var (h, s, l) = ToHsl(c);
        if (l >= 0.5) { l = 0.145 + (1 - l) * 0.6; s *= 0.45; }
        else { l = 0.93 - l * 0.75; s = Math.Min(s, 0.75); }
        return FromHsl(c.A, h, s, l);
    }

    // Tài nguyên (App.axaml): mã Sáng → mã Tối riêng nếu cần
    private static readonly (string key, string light, string? dark)[] Res =
    {
        ("BrandDark", "#1A2433", null), ("BrandMid", "#4A5869", null), ("Accent", "#1F6FD6", null), ("AccentStrong", "#1859AE", null), ("AccentSoft", "#E6F0FC", null),
        ("PageBg", "#F4F6F9", null), ("CardBg", "#FFFFFF", null), ("Surface2", "#F8FAFC", null), ("Border", "#E3E8EF", null), ("BorderStrong", "#CDD5DF", null),
        ("Text", "#1A2433", null), ("Muted", "#5D6B78", null), ("Faint", "#7A8797", null),
        ("InColor", "#1E7B4A", null), ("OutColor", "#C2410C", null), ("Danger", "#B42318", null),
        ("OkSoft", "#E3F4EA", null), ("Ok", "#1E7B4A", null), ("WarnSoft", "#FFF2D6", null), ("Warn", "#9A5B00", null), ("BadSoft", "#FDE8E6", null), ("Bad", "#B42318", null),
        ("InfoSoft", "#EEEBFC", null), ("Info", "#5B4BC4", null),
        ("NavBg", "#10243D", null), ("NavFg", "#C8D4E3", "#A9B5C3"), ("NavHover", "#1B3452", "#1A212B"), ("NavMuted", "#6F8199", "#5E6B7A"),
        ("GridLine", "#E3E8EF", null), ("GridHead", "#F1F4F8", null),
    };

    /// <summary>Gắn brush dùng chung vào tài nguyên ứng dụng (gọi 1 lần sau khi nạp App.axaml).</summary>
    public static void Install(Application app)
    {
        foreach (var (k, l, d) in Res) app.Resources[k] = Brush(l, d);
    }

    /// <summary>Mật độ bảng: "gon" · "vua" · "thoang" (lưu theo tài khoản: ui_density:&lt;tk&gt;).</summary>
    public static string Density { get; private set; } = "vua";
    public static double RowHeight => Density switch { "gon" => 22, "thoang" => 32, _ => 26 };
    public static readonly (string Key, string Label)[] Densities = { ("gon", "Gọn — nhiều dòng nhất"), ("vua", "Vừa (chuẩn)"), ("thoang", "Thoáng — dễ đọc") };
    public static void SetDensity(string d) { Density = d is "gon" or "thoang" ? d : "vua"; Changed?.Invoke(); }
    public static string KeyDensity(string user) => "ui_density:" + user.ToLowerInvariant();

    public static string KeyTheme(string user) => "ui_theme:" + user.ToLowerInvariant();
    public static string KeyScale(string user) => "ui_scale:" + user.ToLowerInvariant();

    /// <summary>Nạp lựa chọn đã lưu của tài khoản trên máy này.</summary>
    public static void LoadFor(string user)
    {
        var st = App.Services.Settings;
        var m = st.Get(KeyTheme(user), "light") == "dark" ? Mode.Dark : Mode.Light;
        var sc = double.TryParse(st.Get(KeyScale(user), "1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1;
        Density = st.Get(KeyDensity(user), "vua") is var d && d is "gon" or "thoang" ? st.Get(KeyDensity(user), "vua") : "vua";
        Apply(m, sc);
    }

    public static void SaveFor(string user)
    {
        var st = App.Services.Settings;
        st.Set(KeyTheme(user), Current == Mode.Dark ? "dark" : "light");
        st.Set(KeyScale(user), Scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        st.Set(KeyDensity(user), Density);
    }

    public static void Apply(Mode mode, double scale)
    {
        Current = mode; Scale = Math.Clamp(Math.Round(scale, 2), 0.9, 1.3);
        if (Application.Current is { } app) app.RequestedThemeVariant = mode == Mode.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        lock (Cache) foreach (var e in Cache.Values) e.B.Color = Map(e.Src, e.Dark);
        Changed?.Invoke();
    }

    // ------------------------------------------------------------ HSL
    private static (double h, double s, double l) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, h = 0, s = 0;
        if (max - min > 1e-9)
        {
            var d = max - min; s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4; h /= 6;
        }
        return (h, s, l);
    }
    private static Color FromHsl(byte a, double h, double s, double l)
    {
        double Hue(double p, double q, double t) { if (t < 0) t += 1; if (t > 1) t -= 1; return t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p; }
        double r, g, b;
        if (s < 1e-9) r = g = b = l;
        else { var q = l < 0.5 ? l * (1 + s) : l + s - l * s; var p = 2 * l - q; r = Hue(p, q, h + 1 / 3.0); g = Hue(p, q, h); b = Hue(p, q, h - 1 / 3.0); }
        byte B8(double x) => (byte)Math.Clamp(Math.Round(x * 255), 0, 255);
        return Avalonia.Media.Color.FromArgb(a, B8(r), B8(g), B8(b));
    }
}

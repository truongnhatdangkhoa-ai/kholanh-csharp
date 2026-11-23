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
    public static SolidColorBrush Danger => Brush("#B42318");
    public static SolidColorBrush Text => Brush("#1F2937");

    /// <summary>Đổi màu theo giao diện: Sáng = trắng tinh → trắng ngà; Tối = đảo độ sáng, giảm độ rực của nền.</summary>
    public static Color Map(Color c, Color? dark)
    {
        if (Current == Mode.Light)
            return c.R >= 252 && c.G >= 252 && c.B >= 252 ? Avalonia.Media.Color.FromArgb(c.A, 0xFB, 0xFA, 0xF6) : c;
        if (dark is { } d) return d;
        var (h, s, l) = ToHsl(c);
        if (l >= 0.5) { l = 0.145 + (1 - l) * 0.6; s *= 0.45; }
        else { l = 0.93 - l * 0.75; s = Math.Min(s, 0.75); }
        return FromHsl(c.A, h, s, l);
    }

    // Tài nguyên (App.axaml): mã Sáng → mã Tối riêng nếu cần
    private static readonly (string key, string light, string? dark)[] Res =
    {
        ("BrandDark", "#0B2A45", null), ("BrandMid", "#134B73", null), ("Accent", "#1F8FD1", "#2C7DB8"), ("AccentSoft", "#E3F2FB", null),
        ("PageBg", "#F3F6F9", "#181A1E"), ("CardBg", "#FFFFFF", null), ("Border", "#D9E2EA", "#3A3F46"), ("Muted", "#5D6B78", "#9AA4AF"),
        ("InColor", "#1E8E3E", null), ("OutColor", "#C2410C", null), ("Danger", "#B42318", null),
        ("NavBg", "#0B2A45", "#101418"), ("GridLine", "#E8EEF3", null), ("GridHead", "#EAF2F8", null),
    };

    /// <summary>Gắn brush dùng chung vào tài nguyên ứng dụng (gọi 1 lần sau khi nạp App.axaml).</summary>
    public static void Install(Application app)
    {
        foreach (var (k, l, d) in Res) app.Resources[k] = Brush(l, d);
    }

    public static string KeyTheme(string user) => "ui_theme:" + user.ToLowerInvariant();
    public static string KeyScale(string user) => "ui_scale:" + user.ToLowerInvariant();

    /// <summary>Nạp lựa chọn đã lưu của tài khoản trên máy này.</summary>
    public static void LoadFor(string user)
    {
        var st = App.Services.Settings;
        var m = st.Get(KeyTheme(user), "light") == "dark" ? Mode.Dark : Mode.Light;
        var sc = double.TryParse(st.Get(KeyScale(user), "1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1;
        Apply(m, sc);
    }

    public static void SaveFor(string user)
    {
        var st = App.Services.Settings;
        st.Set(KeyTheme(user), Current == Mode.Dark ? "dark" : "light");
        st.Set(KeyScale(user), Scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
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

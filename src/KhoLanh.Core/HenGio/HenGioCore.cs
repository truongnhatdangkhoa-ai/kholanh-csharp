using System.Globalization;

namespace KhoLanh.Core.HenGio;

/// <summary>Hệ điều hành đích của lệnh tắt máy.</summary>
public enum HgOs { Windows, Linux, Mac }

/// <summary>Một dòng lệnh hệ điều hành (chương trình + tham số).</summary>
public record HgLenh(string File, string Args);

/// <summary>Kết quả đặt / hủy hẹn giờ: Ok, thời điểm tắt (nếu đang hẹn), câu báo cho người dùng.</summary>
public record HgKetQua(bool Ok, DateTime? Luc, string Text);

/// <summary>
/// Tính giờ cho "Hẹn giờ tắt máy" (hàm thuần, không đụng hệ điều hành): đổi số phút / giờ đồng hồ thành thời điểm + số giây, dựng lệnh <c>shutdown</c>.
/// </summary>
public static class HenGioCore
{
    /// <summary>Tối đa 7 ngày (lệnh shutdown của Windows chấp nhận tới 10 năm, nhưng hẹn quá xa dễ quên).</summary>
    public const int MaxPhut = 7 * 24 * 60;

    public static HgOs ThisOs() => OperatingSystem.IsWindows() ? HgOs.Windows : OperatingSystem.IsMacOS() ? HgOs.Mac : HgOs.Linux;

    /// <summary>Đếm ngược theo số phút (1 … <see cref="MaxPhut"/>). Ngoài khoảng → null.</summary>
    public static (DateTime Luc, int Giay)? TheoPhut(DateTime now, int phut)
    {
        if (phut < 1 || phut > MaxPhut) return null;
        return (now.AddMinutes(phut), phut * 60);
    }

    /// <summary>
    /// Theo giờ đồng hồ: lần tới gặp giờ:phút đó (đã qua trong hôm nay → ngày mai; trùng phút hiện tại cũng tính là ngày mai).
    /// Số giây tính từ <paramref name="now"/> đến đúng phút đó (giây của now không làm lệch giờ tắt).
    /// </summary>
    public static (DateTime Luc, int Giay)? TheoGio(DateTime now, int gio, int phut)
    {
        if (gio is < 0 or > 23 || phut is < 0 or > 59) return null;
        var luc = new DateTime(now.Year, now.Month, now.Day, gio, phut, 0, now.Kind);
        if (luc <= now) luc = luc.AddDays(1);
        return (luc, (int)Math.Ceiling((luc - now).TotalSeconds));
    }

    /// <summary>Đọc "22:30", "22h30", "22.30", "2230", "22" (= 22:00). Sai → false.</summary>
    public static bool TryParseGio(string? text, out int gio, out int phut)
    {
        gio = phut = 0;
        var s = (text ?? "").Trim().ToLowerInvariant().Replace('h', ':').Replace('.', ':').Replace('g', ':');
        if (s.Length == 0) return false;
        if (s.EndsWith(':')) s += "0";
        string a, b = "0";
        if (s.Contains(':')) { var p = s.Split(':'); if (p.Length != 2) return false; a = p[0]; b = p[1]; }
        else if (s.Length is 3 or 4 && s.All(char.IsDigit)) { a = s[..^2]; b = s[^2..]; }
        else a = s;
        if (!int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out gio) || !int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out phut)) return false;
        return gio is >= 0 and <= 23 && phut is >= 0 and <= 59;
    }

    /// <summary>Lệnh đặt hẹn giờ tắt. Windows: <c>shutdown /s /t giây</c>; Linux / macOS: <c>shutdown +phút</c> (làm tròn lên).</summary>
    public static HgLenh Dat(HgOs os, int giay)
    {
        giay = Math.Max(1, giay);
        var phut = (int)Math.Ceiling(giay / 60.0);
        return os switch
        {
            HgOs.Windows => new HgLenh("shutdown", $"/s /t {giay} /c \"KhoLanh hen gio tat may\""),
            HgOs.Mac => new HgLenh("shutdown", $"-h +{phut}"),
            _ => new HgLenh("shutdown", $"-P +{phut}"),
        };
    }

    /// <summary>Lệnh hủy hẹn giờ (Windows <c>shutdown /a</c>).</summary>
    public static HgLenh Huy(HgOs os) => os switch
    {
        HgOs.Windows => new HgLenh("shutdown", "/a"),
        HgOs.Mac => new HgLenh("pkill", "shutdown"),
        _ => new HgLenh("shutdown", "-c"),
    };

    /// <summary>"1 giờ 05 phút 09 giây" / "5 phút 09 giây" / "9 giây".</summary>
    public static string DemNguoc(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var s = (int)Math.Ceiling(t.TotalSeconds);
        int d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60, g = s % 60;
        var parts = new List<string>();
        if (d > 0) parts.Add($"{d} ngày");
        if (d > 0 || h > 0) parts.Add(d > 0 ? $"{h:00} giờ" : $"{h} giờ");
        if (d > 0 || h > 0 || m > 0) parts.Add(h > 0 || d > 0 ? $"{m:00} phút" : $"{m} phút");
        parts.Add(parts.Count > 0 ? $"{g:00} giây" : $"{g} giây");
        return string.Join(" ", parts);
    }

    /// <summary>"22:30 hôm nay" / "22:30 ngày mai" / "22:30 26/11" — so với <paramref name="now"/>.</summary>
    public static string NhanLuc(DateTime luc, DateTime now)
    {
        var gio = luc.ToString("HH:mm", CultureInfo.InvariantCulture);
        var d = (luc.Date - now.Date).Days;
        return d == 0 ? gio + " hôm nay" : d == 1 ? gio + " ngày mai" : gio + " ngày " + luc.ToString("dd/MM", CultureInfo.InvariantCulture);
    }
}

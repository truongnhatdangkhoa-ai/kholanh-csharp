using System.Globalization;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// Giai đoạn 5: phần thêm cho báo cáo gửi Claude (<see cref="TroLyReport.Build"/>) — "Cài đặt liên quan" (đã che), "Log quanh lỗi" cho lỗi phần mềm,
/// và bước che bí mật cuối cùng. HÀM THUẦN. KHÔNG BAO GIỜ đưa token / mật khẩu / mã / đường dẫn đầy đủ / email đầy đủ vào báo cáo.
/// </summary>
public static class TroLyBaoCao
{
    public const int LogMax = 20;
    public const string Che = "[đã che]";

    /// <summary>Phần thêm của báo cáo. <see cref="Secrets"/> = giá trị bí mật thô của máy — nếu lọt vào chữ báo cáo (vd trong bằng chứng) sẽ bị thay bằng <see cref="Che"/>.</summary>
    public sealed class Extra
    {
        public List<(string Key, string Value)> CaiDat { get; } = new();
        public string LogText { get; set; } = "";
        public List<string> Secrets { get; } = new();
    }

    private static readonly Regex EmailRe = new(@"([\w.+-])[\w.+-]*@([\w-]+\.[\w.-]+)");

    /// <summary>abc.def@gmail.com → a***@gmail.com. Rỗng → "không".</summary>
    public static string MaskEmail(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "không";
        var m = EmailRe.Match(s);
        return m.Success ? m.Groups[1].Value + "***@" + m.Groups[2].Value : "có";
    }

    /// <summary>Đường dẫn web: chỉ "có (…/đoạn cuối)" — đoạn cuối dài &gt; 12 ký tự (mã) thì chỉ "có".</summary>
    public static string MaskUrl(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "không";
        var last = s.TrimEnd('/').Split('/', '?').LastOrDefault(x => x.Length > 0) ?? "";
        return last.Length is > 0 and <= 12 && !last.Contains('=') ? $"có (…/{last})" : "có";
    }

    /// <summary>Đường dẫn thư mục / file: "có (…\tên cuối)".</summary>
    public static string MaskPath(string? s)
    {
        s = (s ?? "").Trim().TrimEnd('\\', '/');
        if (s.Length == 0) return "không";
        var last = s.Split('\\', '/').LastOrDefault(x => x.Length > 0) ?? "";
        return last.Length > 0 ? $"có (…{Path.DirectorySeparatorChar}{TroLyReport.Mask(EmailRe.Replace(last, x => MaskEmail(x.Value)), true)})" : "có";
    }

    public static string CoKhong(string? s) => string.IsNullOrWhiteSpace(s) ? "không" : "có";

    private static readonly Regex Head = new(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})(?: bản (\S+))?(?: \[(.+?)\])?\s*$");

    /// <summary>
    /// ≤ <paramref name="max"/> dòng logs/loi.log quanh LẦN XUẤT HIỆN CUỐI của lỗi phần mềm <paramref name="f"/> (khớp cùng cách gộp với
    /// <see cref="TroLyChecks.ParseErrorLog"/>: loại + lời nhắn lỗi trong cùng). Không phải lỗi phần mềm / không thấy → rỗng.
    /// </summary>
    public static List<string> LogAround(string logText, Finding f, int max = LogMax)
    {
        var res = new List<string>();
        if (f.Cat != "loi-app" || string.IsNullOrEmpty(logText)) return res;
        var lines = logText.Replace("\r", "").Split('\n');
        int start = -1, end = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!Head.IsMatch(lines[i])) continue;
            var j = i + 1; var body = new List<string>();
            while (j < lines.Length && !Head.IsMatch(lines[j])) { if (lines[j].Trim().Length > 0) body.Add(lines[j]); j++; }
            if (body.Count > 0)
            {
                var (type, msg) = TroLyChecks.RootCause(body);
                if (TroLyChecks.Hash("loi-app|" + type + "|" + TroLyChecks.NormMsg(msg)) == f.Id) { start = i; end = j; }
            }
            i = j - 1;
        }
        if (start < 0) return res;
        for (var k = start; k < end && res.Count < max; k++) if (lines[k].Trim().Length > 0) res.Add(lines[k]);
        return res;
    }

    /// <summary>Bước cuối: thay mọi giá trị bí mật (≥ 6 ký tự) bằng <see cref="Che"/>, mọi email thành dạng a***@miền.</summary>
    public static string Scrub(string text, IEnumerable<string>? secrets)
    {
        foreach (var s in (secrets ?? Enumerable.Empty<string>()).Where(x => x is { Length: >= 6 }).Distinct().OrderByDescending(x => x.Length))
            text = text.Replace(s, Che, StringComparison.Ordinal);
        return EmailRe.Replace(text, m => MaskEmail(m.Value));
    }

    public static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}

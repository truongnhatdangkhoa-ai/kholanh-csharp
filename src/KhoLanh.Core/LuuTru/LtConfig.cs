using System.Text.Json;
using System.Text.Json.Serialization;

namespace KhoLanh.Core.LuuTru;

/// <summary>1 lượt sao lưu / khôi phục trong lịch sử.</summary>
public sealed class LtRun
{
    public DateTime At { get; set; }
    public bool Ok { get; set; }
    public string Kind { get; set; } = "";
    public string Msg { get; set; } = "";
}

/// <summary>
/// Cài đặt Google + sao lưu RIÊNG từng tài khoản (luu-tru/&lt;tài khoản&gt;/cai-dat.json). Refresh token cất bằng <see cref="Services.Secret"/>
/// (Windows: DPAPI theo tài khoản Windows — chép file sang máy khác không dùng được).
/// </summary>
public sealed class LtConfig
{
    public const string AutoOff = "off", AutoChange = "change", AutoHour = "1h", Auto6h = "6h", AutoDaily = "daily";
    public static readonly (string Key, string Label)[] AutoOptions =
    {
        (AutoChange, "Khi có thay đổi (chờ 2 phút)"), (AutoHour, "Mỗi giờ (nếu có thay đổi)"), (Auto6h, "Mỗi 6 giờ (nếu có thay đổi)"),
        (AutoDaily, "Mỗi ngày 1 lần"), (AutoOff, "Tắt — chỉ sao lưu khi bấm"),
    };
    public static readonly int[] KeepOptions = { 7, 14, 30, 90, 365 };

    public string Email { get; set; } = "";
    public string Refresh { get; set; } = "";
    public string Auto { get; set; } = AutoChange;
    public int DailyHour { get; set; } = 17;
    public int KeepDays { get; set; } = 30;
    public DateTime? LastOk { get; set; }
    public DateTime? LastTry { get; set; }
    public string LastErr { get; set; } = "";
    public long LastSeq { get; set; }
    public bool NeedRelink { get; set; }
    public List<LtRun> History { get; set; } = new();

    [JsonIgnore] public bool Linked => Refresh.Length > 0;

    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static LtConfig Load(string file)
    {
        try { if (File.Exists(file)) return JsonSerializer.Deserialize<LtConfig>(File.ReadAllText(file), Opt) ?? new(); }
        catch { /* hỏng → mặc định */ }
        return new LtConfig();
    }

    public void Save(string file)
    {
        if (History.Count > 30) History = History.TakeLast(30).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + ".tam", JsonSerializer.Serialize(this, Opt));
        File.Move(file + ".tam", file, true);
    }

    public void AddRun(DateTime at, bool ok, string kind, string msg) => History.Add(new LtRun { At = at, Ok = ok, Kind = kind, Msg = msg });

    /// <summary>
    /// Đến giờ tự sao lưu chưa. dirty = có thay đổi chưa sao lưu; lastChange = lúc sửa gần nhất.
    /// Lỗi gần đây (&lt; 10 phút) thì chờ, không thử liên tục.
    /// </summary>
    public bool IsDue(DateTime now, bool dirty, DateTime lastChange)
    {
        if (!Linked || NeedRelink || Auto == AutoOff || !dirty) return false;
        if (LastErr.Length > 0 && LastTry is { } t && now - t < TimeSpan.FromMinutes(10)) return false;
        return Auto switch
        {
            AutoChange => now - lastChange >= TimeSpan.FromMinutes(2) || (LastOk is { } ok && now - ok >= TimeSpan.FromMinutes(30) && now - lastChange >= TimeSpan.FromSeconds(30)),
            AutoHour => LastOk is not { } a || now - a >= TimeSpan.FromHours(1),
            Auto6h => LastOk is not { } b || now - b >= TimeSpan.FromHours(6),
            // đúng giờ đã chọn; máy không mở lúc đó thì bù khi đã quá 24 giờ
            AutoDaily => LastOk is not { } c || now - c >= TimeSpan.FromHours(24) || (now.Hour >= DailyHour && c.Date < now.Date),
            _ => false,
        };
    }
}

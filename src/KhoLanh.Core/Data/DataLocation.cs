using KhoLanh.Core.Services;

namespace KhoLanh.Core.Data;

/// <summary>
/// Nơi lưu database. Máy mới cài lần đầu → hỏi người dùng chọn thư mục (gợi ý %LOCALAPPDATA%\KhoLanhCS).
/// Lựa chọn ghi vào file trỏ "noi-luu-du-lieu.txt" trong thư mục mặc định; đổi lại được ở Cài đặt máy.
/// Máy đã có dữ liệu ở thư mục mặc định (cài từ bản trước) → dùng luôn, không hỏi.
/// </summary>
public sealed class DataLocation
{
    public const string DbFileName = "khodata.db", PointerName = "noi-luu-du-lieu.txt", EnvVar = "KHOLANH_DATA";
    public string DefaultFolder { get; }
    public DataLocation(string? defaultFolder = null) => DefaultFolder = defaultFolder ?? Database.DefaultDataFolder;

    public string PointerFile => Path.Combine(DefaultFolder, PointerName);
    public static string? EnvFolder => Environment.GetEnvironmentVariable(EnvVar) is { Length: > 0 } e ? e : null;

    /// <summary>Thư mục đã chọn trước đây (file trỏ); chưa chọn → null.</summary>
    public string? Saved()
    {
        try
        {
            if (!File.Exists(PointerFile)) return null;
            var p = File.ReadAllText(PointerFile).Trim();
            return p.Length > 0 ? p : null;
        }
        catch { return null; }
    }

    public static bool HasDb(string folder) => File.Exists(Path.Combine(folder, DbFileName));

    /// <summary>Lần chạy đầu: chưa chọn nơi lưu và thư mục mặc định chưa có dữ liệu.</summary>
    public bool NeedsChoice => EnvFolder is null && Saved() is null && !HasDb(DefaultFolder);

    /// <summary>Thư mục dữ liệu sẽ dùng: biến môi trường → lựa chọn đã lưu → mặc định.</summary>
    public string Resolve() => EnvFolder ?? Saved() ?? DefaultFolder;

    /// <summary>Kiểm tra thư mục ghi được (tạo nếu chưa có). Lỗi → BusinessException dễ hiểu.</summary>
    public static string Validate(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new BusinessException("Chưa chọn thư mục lưu dữ liệu.");
        var full = Path.GetFullPath(folder.Trim());
        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, ".kholanh-thu-ghi");
            File.WriteAllText(probe, "ok"); File.Delete(probe);
        }
        catch (Exception e) { throw new BusinessException($"Không ghi được vào thư mục:\n{full}\n\nChọn thư mục khác (VD D:\\KhoLanhData).\n\n{e.Message}"); }
        return full;
    }

    /// <summary>Ghi lựa chọn. Chọn đúng thư mục mặc định vẫn ghi file trỏ để lần sau không hỏi lại.</summary>
    public string Save(string folder)
    {
        var full = Validate(folder);
        Directory.CreateDirectory(DefaultFolder);
        File.WriteAllText(PointerFile, full);
        return full;
    }

    public static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

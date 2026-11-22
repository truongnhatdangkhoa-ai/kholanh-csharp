using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace KhoLanh.App.Web;

/// <summary>
/// Giải nén mã gốc 2 module (nhúng trong exe) ra thư mục %LOCALAPPDATA%\KhoLanhCS\web\&lt;dấu phiên bản&gt;.
/// Mỗi bản build có dấu riêng nên bản mới luôn dùng đúng tệp của nó; thư mục của bản cũ được dọn.
/// </summary>
public static class WebAssets
{
    private static string? _root;
    private static readonly object Gate = new();

    public static string BridgeScript()
    {
        using var s = typeof(WebAssets).Assembly.GetManifestResourceStream("kl-bridge.js")
                      ?? throw new InvalidOperationException("Thiếu kl-bridge.js trong bản cài.");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>Thư mục gốc chứa html/, lib/, node_modules/ (tương ứng thư mục app/ của app cũ).</summary>
    public static string EnsureExtracted(string dataFolder)
    {
        lock (Gate)
        {
            if (_root is not null) return _root;
            var asm = typeof(WebAssets).Assembly;
            var names = asm.GetManifestResourceNames().Where(n => n.StartsWith("web/")).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var stamp = Stamp(asm, names);
            var baseDir = Path.Combine(dataFolder, "web");
            var root = Path.Combine(baseDir, stamp);
            var done = Path.Combine(root, ".xong");
            if (!File.Exists(done))
            {
                var tmp = root + ".tmp-" + Environment.ProcessId;
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                foreach (var n in names)
                {
                    var rel = n["web/".Length..].Replace('/', Path.DirectorySeparatorChar);
                    var dst = Path.Combine(tmp, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    using var src = asm.GetManifestResourceStream(n)!;
                    using var f = File.Create(dst);
                    src.CopyTo(f);
                }
                File.WriteAllText(Path.Combine(tmp, ".xong"), DateTime.Now.ToString("O"));
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Directory.Move(tmp, root);
            }
            // Dọn bản giải nén của các phiên bản cũ.
            foreach (var d in Directory.GetDirectories(baseDir))
                if (!string.Equals(Path.GetFileName(d), stamp, StringComparison.OrdinalIgnoreCase))
                    try { Directory.Delete(d, true); } catch { /* đang bị dùng: để lần sau */ }
            return _root = root;
        }
    }

    private static string Stamp(Assembly asm, List<string> names)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder(asm.ManifestModule.ModuleVersionId.ToString());
        foreach (var n in names) sb.Append('|').Append(n);
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
    }
}

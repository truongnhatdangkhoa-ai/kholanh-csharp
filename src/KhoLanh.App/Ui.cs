using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using KhoLanh.Core.Services;

namespace KhoLanh.App;

/// <summary>Hộp thoại, chọn file, định dạng số dùng chung cho mọi màn hình.</summary>
public static class Ui
{
    public static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static Window? Owner(Visual v) => TopLevel.GetTopLevel(v) as Window;

    public static string Num(double d) => d.ToString("#,0.##", Vi);

    /// <summary>Đọc số người dùng gõ: chấp nhận "1,5", "1.5", "1.234,5", "1,234.5".</summary>
    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        var s = (text ?? "").Trim().Replace(" ", "");
        if (s.Length == 0) return true;
        int lastDot = s.LastIndexOf('.'), lastComma = s.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
        {
            var dec = lastDot > lastComma ? '.' : ',';
            var thou = dec == '.' ? "," : ".";
            s = s.Replace(thou, "").Replace(dec, '.');
        }
        else s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static double ParseNumber(string? text, string field)
    {
        if (!TryParseNumber(text, out var v)) throw new BusinessException($"\"{text}\" ở ô {field} không phải là số.");
        return v;
    }

    /// <summary>Chạy một thao tác; lỗi nghiệp vụ hiện hộp thông báo thay vì làm sập app.</summary>
    public static async Task<bool> Run(Visual v, Action action)
    {
        try { action(); return true; }
        catch (BusinessException e) { await Message(v, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Message(v, Describe(e), "Lỗi"); }
        return false;
    }

    public static async Task<bool> RunAsync(Visual v, Func<Task> action)
    {
        try { await action(); return true; }
        catch (BusinessException e) { await Message(v, e.Message, "Không thực hiện được"); }
        catch (Exception e) { await Message(v, Describe(e), "Lỗi"); }
        return false;
    }

    /// <summary>Nội dung lỗi luôn có chi tiết (kể cả khi lỗi không kèm lời nhắn) và ghi vào logs\loi.log để gửi cho người hỗ trợ.</summary>
    public static string Describe(Exception e)
    {
        var parts = new List<string>();
        for (Exception? x = e; x is not null && parts.Count < 4; x = x.InnerException)
        {
            var msg = string.IsNullOrWhiteSpace(x.Message) ? "(không có lời nhắn)" : x.Message.Trim();
            parts.Add($"{msg} [{x.GetType().Name}, 0x{x.HResult:X8}]");
        }
        string logPath = "";
        try
        {
            var dir = Path.Combine(App.DataFolder, "logs");
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "loi.log");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} bản {KhoLanh.Core.Services.UpdateService.Current}\n{e}\n\n");
        }
        catch { logPath = ""; }
        return "Có lỗi xảy ra: " + string.Join("\n→ ", parts) + (logPath.Length > 0 ? $"\n\nChi tiết đã ghi vào {logPath}" : "");
    }

    private static Window Dialog(string title, Control body, double width = 440) => new()
    {
        Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        Background = Brushes.White,
        Content = new Border { Padding = new Thickness(22, 18), Child = body },
    };

    private static StackPanel Buttons(params Button[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    public static async Task Message(Visual v, string text, string title = "Thông báo")
    {
        var ok = new Button { Content = "Đóng", IsDefault = true, IsCancel = true, Classes = { "primary" } };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Buttons(ok));
        var w = Dialog(title, body);
        ok.Click += (_, _) => w.Close();
        await Show(v, w);
    }

    public static async Task<bool> Confirm(Visual v, string text, string title = "Xác nhận", string yes = "Đồng ý")
    {
        var result = false;
        var y = new Button { Content = yes, IsDefault = true, Classes = { "primary" } };
        var n = new Button { Content = "Thôi", IsCancel = true };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Buttons(n, y));
        var w = Dialog(title, body);
        y.Click += (_, _) => { result = true; w.Close(); };
        n.Click += (_, _) => w.Close();
        await Show(v, w);
        return result;
    }

    public static async Task<string?> Prompt(Visual v, string text, string title, string initial = "", bool password = false)
    {
        string? result = null;
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 0) };
        if (password) box.PasswordChar = '•';
        var y = new Button { Content = "Đồng ý", IsDefault = true, Classes = { "primary" } };
        var n = new Button { Content = "Thôi", IsCancel = true };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(box);
        body.Children.Add(Buttons(n, y));
        var w = Dialog(title, body);
        y.Click += (_, _) => { result = box.Text ?? ""; w.Close(); };
        n.Click += (_, _) => w.Close();
        w.Opened += (_, _) => box.Focus();
        await Show(v, w);
        return result;
    }

    public static async Task Show(Visual v, Window w)
    {
        if (Owner(v) is { } owner) await w.ShowDialog(owner);
        else { var tcs = new TaskCompletionSource(); w.Closed += (_, _) => tcs.TrySetResult(); w.Show(); await tcs.Task; }
    }

    public static async Task<string?> SaveFile(Visual v, string title, string suggestedName, string ext, string typeName)
    {
        var top = TopLevel.GetTopLevel(v);
        if (top is null) return null;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedName, DefaultExtension = ext, ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType(typeName) { Patterns = new[] { "*." + ext } } },
        });
        return file?.TryGetLocalPath();
    }

    public static async Task<string?> OpenFile(Visual v, string title, string ext, string typeName)
    {
        var top = TopLevel.GetTopLevel(v);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = new[] { "*." + ext } } },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Mở file / thư mục bằng chương trình mặc định của Windows.</summary>
    public static void OpenExternal(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* không có chương trình mở: bỏ qua */ }
    }

    public static string SafeFileName(string s) =>
        string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}

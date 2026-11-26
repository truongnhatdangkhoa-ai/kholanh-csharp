namespace KhoLanh.Core.Modules;

/// <summary>
/// 1.136: ghi lại database module nào vừa được đọc (qua <see cref="ISheetSource"/> / <see cref="ModuleStore"/>) — cửa sổ chính dùng để biết
/// màn hình đã giữ phụ thuộc database nào, quay lại chỉ dựng lại khi CHÍNH các database đó đổi (trước: bất kỳ bảng nào đổi, kể cả máy khác ghi
/// Trợ lý / module khác qua LAN, cũng dựng lại cả trang → máy con đọc lại toàn bộ qua mạng).
/// </summary>
public static class ReadTrack
{
    /// <summary>Gọi mỗi lần đọc (mọi luồng). null = không theo dõi.</summary>
    public static Action<string>? OnRead { get; set; }
    public static void Note(string dbKey) { try { OnRead?.Invoke(dbKey); } catch { /* chỉ để theo dõi */ } }

    /// <summary>Màn hình còn mới khi mọi database nó đọc giữ nguyên phiên bản so với lúc dựng. Chưa thấy đọc database nào → không đủ căn cứ
    /// (dùng mốc chung).</summary>
    public static bool Fresh(IReadOnlyCollection<string> keys, IReadOnlyDictionary<string, long> then, IReadOnlyDictionary<string, long> now) =>
        keys.Count > 0 && keys.All(k => (then.TryGetValue(k, out var a) ? a : 0) == (now.TryGetValue(k, out var b) ? b : 0));
}

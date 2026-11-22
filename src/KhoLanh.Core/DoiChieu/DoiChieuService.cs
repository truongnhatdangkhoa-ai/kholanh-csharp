using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;
using static KhoLanh.Core.DoiChieu.DcxEngine;

namespace KhoLanh.Core.DoiChieu;

/// <summary>
/// Module ĐỐI CHIẾU HÀNG NGÀY (báo cáo hàng ngày) — viết lại bằng C# theo doi-chieu-xuong-kho.js + core/dcx-link.js của app cũ.
/// Đối chiếu số liệu XUẤT của Xưởng với file Xuất của Kho theo từng NGÀY × MÃ HÀNG (Số lượng + Net).
///  - Liên kết 3 file Excel THẬT trên máy này: Xuất thay thùng (kho1) + Xuất rã đông (kho2) — app GHI THẲNG; Xưởng (2 sheet, mỗi sheet ghép 1 file Kho) — CHỈ ĐỌC.
///  - Liên kết + trạng thái làm việc lưu THEO TÀI KHOẢN trên MÁY NÀY (không qua LAN), như app cũ.
///  - Mọi thao tác ghi (chuyển Kho ↔ Kho, sửa SL, đổi mã / ngày, xóa, thêm dòng, nạp file gốc) mô tả theo (ngày, mã, số lượng) rồi sửa file
///    giữ nguyên mẫu (DcxXlsx), ghi cả 2 file hoặc không file nào: kiểm file chưa đổi · không đang mở · sao lưu 1 bản/ngày giữ 7 ngày · ghi tạm rồi đổi tên · đọc lại kiểm tra.
///  - Mỗi lần ghi có thao tác NGƯỢC → Hoàn tác.
/// </summary>
public sealed class DoiChieuService
{
    public const string PermKey = "m5";
    public const int KeepDays = 7;
    public static string KN(int id) => id == 2 ? "Xuất rã đông" : "Xuất thay thùng";
    public static string SlotOf(int id) => id == 2 ? "kho2" : "kho1";
    public static string SlotLabel(string s) => s == "kho1" ? "Xuất thay thùng" : s == "kho2" ? "Xuất rã đông" : "Xưởng";
    public static int OtherId(int id) => id == 1 ? 2 : 1;

    // ------------------------------------------------------------------ trạng thái lưu (theo tài khoản, trên máy này)

    public sealed class Link { public string Path { get; set; } = ""; public string Name { get; set; } = ""; public string At { get; set; } = ""; }
    public sealed class ManualEdit
    {
        public string Id { get; set; } = ""; public string At { get; set; } = ""; public string By { get; set; } = "";
        public int Pair { get; set; } public string Side { get; set; } = ""; public string Ngay { get; set; } = ""; public string Ma { get; set; } = "";
        public string Ten { get; set; } = ""; public string Field { get; set; } = ""; public double? Before { get; set; } public double After { get; set; }
    }
    public sealed class LogEntry { public string Id { get; set; } = ""; public string At { get; set; } = ""; public string By { get; set; } = ""; public string Desc { get; set; } = ""; public List<Op> Inv { get; set; } = new(); }
    public sealed class State
    {
        public int V { get; set; } = 1;
        public Dictionary<string, Link> Links { get; set; } = new();
        public string BackupDir { get; set; } = "";
        public Dictionary<string, Dictionary<string, int>> Maps { get; set; } = new();
        public Dictionary<string, Dictionary<string, string>> Sigs { get; set; } = new();
        public Dictionary<string, int> Hdr { get; set; } = new();
        public Dictionary<string, string> XuongSheet { get; set; } = new();
        public Dictionary<string, string> KhoSheet { get; set; } = new();
        public List<ManualEdit> Edits { get; set; } = new();
        public List<LogEntry> Log { get; set; } = new();
        public bool HideZero { get; set; }
    }
    private static readonly JsonSerializerOptions JsonOpt = new() { IncludeFields = true, WriteIndented = false };

    // ------------------------------------------------------------------ trạng thái chạy

    public sealed class SideData
    {
        public string File = "", Sheet = "", LoadedAt = "";
        public ParseResult Res = new();
    }
    public sealed class Pair
    {
        public int Id;
        public SideData? Kho, Xuong;
        public List<string> KhoSheets = new();
        public Dictionary<string, List<object?[]>> KhoAoa = new();
        public byte[]? Bytes;
        public string Sha = "", LinkErr = "", XuongSheet = "";
        public bool Linked;
        public Result? Result;
        public Dictionary<string, List<Row>> EffIdx = new();
        public List<(string Sheet, int Chung, int Kho, int Xuong)> Sug = new();
        public Dictionary<string, (double Sl, double Net)> AggKho = new(), AggXuong = new();
    }
    public sealed class Xwb { public string File = ""; public List<string> Names = new(); public Dictionary<string, List<object?[]>> Aoa = new(); }
    public sealed record Need(string Key, string Label, NeedMap Map, List<object?[]> Aoa);

    public Pair P1 { get; } = new() { Id = 1 };
    public Pair P2 { get; } = new() { Id = 2 };
    public Pair PairOf(int id) => id == 2 ? P2 : P1;
    public IEnumerable<Pair> Pairs => new[] { P1, P2 };
    public Xwb? Xuong { get; private set; }
    public string XSha { get; private set; } = "";
    public string XErr { get; private set; } = "";
    public State St { get; private set; } = new();
    public List<Need> Needs { get; } = new();
    public bool Loaded { get; private set; }

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly SettingsService _settings;
    private readonly string _dataDir;
    private Dictionary<string, (long len, DateTime mtime)> _stamp = new();

    // từ điển dùng chung (chỉ đọc từ Tổng hợp)
    private Dictionary<string, string> _sizeDict = new();
    private Dictionary<string, (string Ten, double? Tl)> _m8 = new();
    public IReadOnlyDictionary<string, (string Ten, double? Tl)> CodeList => _m8;

    public DoiChieuService(Func<ISheetSource> source, Session session, AuditService audit, SettingsService settings, string dataDir)
    {
        _src = source; _session = session; _audit = audit; _settings = settings; _dataDir = dataDir;
    }

    public bool CanView => _session.Can(PermKey, Models.Perm.View);
    public bool CanEdit => _session.Can(PermKey, Models.Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Models.Perm.Export);
    private void RequireView() { if (!CanView) throw new BusinessException("Tài khoản không có quyền xem Đối chiếu hàng ngày."); }
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa Đối chiếu hàng ngày (ghi vào file Excel Kho)."); }
    private string User => _session.Username;
    private string StateKey => "dcx_state:" + User.ToLowerInvariant();
    public string BackupDefault => Path.Combine(_dataDir, "dcx-backup");
    public string BackupDir => St.BackupDir.Length > 0 ? St.BackupDir : BackupDefault;
    public static string NowTxt() => DateTime.Now.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
    public static string NewId() => "e" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString("x") + Guid.NewGuid().ToString("N")[..4];

    // ------------------------------------------------------------------ nạp / lưu trạng thái

    public void Load()
    {
        RequireView();
        St = ReadState();
        LoadDictionaries();
        Needs.Clear();
        P1.XuongSheet = St.XuongSheet.GetValueOrDefault("1", ""); P2.XuongSheet = St.XuongSheet.GetValueOrDefault("2", "");
        foreach (var slot in new[] { "kho1", "kho2", "xuong" }) if (St.Links.ContainsKey(slot)) LoadFromLink(slot);
        foreach (var p in Pairs) RefreshPair(p);
        Loaded = true;
    }
    private State ReadState()
    {
        try { var t = _settings.Get(StateKey); if (t.Length > 0) return JsonSerializer.Deserialize<State>(t, JsonOpt) ?? new State(); }
        catch { /* trạng thái hỏng → bắt đầu lại */ }
        return new State();
    }
    public void Persist()
    {
        St.XuongSheet["1"] = P1.XuongSheet; St.XuongSheet["2"] = P2.XuongSheet;
        if (St.Log.Count > 200) St.Log = St.Log.Skip(St.Log.Count - 200).ToList();
        _settings.Set(StateKey, JsonSerializer.Serialize(St, JsonOpt));
    }

    private JsonArray Read(string db, string sheet)
    {
        try { return JsonNode.Parse(_src().Read(db, sheet)) as JsonArray ?? new JsonArray(); } catch { return new JsonArray(); }
    }
    private static string S(JsonNode? n)
    {
        var s = n switch { null => "", JsonValue v when v.TryGetValue<string>(out var x) => x, JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture), _ => n.ToJsonString() };
        if (s.StartsWith("__JSON__", StringComparison.Ordinal)) { try { return S(JsonNode.Parse(s[8..])); } catch { return ""; } }
        return s;
    }
    /// <summary>"(0.670 kg x 1 gói)" cuối diễn giải → trọng lượng / đơn vị.</summary>
    public static double? WeightFromNote(string? note)
    {
        var m = Regex.Match((note ?? "").Trim(), @"\(\s*([\d.,]+)\s*kg\s*(?:[x×*]\s*(\d+(?:[.,]\d+)?)\s*[^)]*)?\)\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) return null;
        var n = m.Groups[2].Success && double.TryParse(m.Groups[2].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var nn) ? nn : 1;
        var v = w * n; return double.IsFinite(v) && v > 0 ? Math.Round(v * 10000, MidpointRounding.AwayFromZero) / 10000 : null;
    }
    /// <summary>Từ điển Size (Mã hóa sản phẩm › "8. Size") + Danh sách mã hàng (tên + trọng lượng) — chỉ đọc từ Tổng hợp.</summary>
    public void LoadDictionaries()
    {
        var dict = new Dictionary<string, string>();
        foreach (var e in TongHopService.FixedCatalog().GetValueOrDefault(8) ?? new()) dict[e.Code.Trim().ToUpperInvariant()] = e.Name.Trim();
        foreach (var r in Read(TongHopService.Db, "MaHoa").OfType<JsonObject>())
            if (S(r["Tổ hợp"]) == "8") { var c = S(r["Mã"]).Trim().ToUpperInvariant(); if (c.Length > 0) dict[c] = S(r["Tên"]).Trim(); }
        _sizeDict = dict;
        var m = new Dictionary<string, (string, double?)>();
        foreach (var r in Read(TongHopService.Db, "MaDaTao").OfType<JsonObject>())
        {
            var code = NormMa(S(r["Mã"]).Trim()); if (code.Length == 0 || m.ContainsKey(code)) continue;
            var note = S(r["Ghi chú"]).Trim(); m[code] = (note, WeightFromNote(note));
        }
        _m8 = m;
    }
    /// <summary>Mã 21 ký tự → size (ký tự 14–17 tra từ điển; chưa khai báo → "NN/NNN" trong tên hàng).</summary>
    public string SizeOf(string ma, string ten)
    {
        var s = (ma ?? "").Trim(); if (s.StartsWith("TPC-", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        if (s.Length > 21 && s.Contains('-')) s = s[(s.LastIndexOf('-') + 1)..];
        s = s.ToUpperInvariant(); if (s.Length != 21) return "";
        var code = s.Substring(13, 4); if (code == "XXXX") return "";
        if (_sizeDict.TryGetValue(code, out var nm)) return Regex.Replace(nm.Trim(), @"^size\s+", "", RegexOptions.IgnoreCase).Trim();
        var m = Regex.Match(ten ?? "", @"\d{1,3}\s*\/\s*\d{1,3}");
        return m.Success ? Regex.Replace(m.Value, @"\s+", "") : "";
    }
    public double? WeightOfMa(string ma)
    {
        var nm = NormMa(ma); if (!IsCode21(nm)) return null;
        return _m8.TryGetValue(nm, out var l) && l.Tl is > 0 ? l.Tl : null;
    }

    // ------------------------------------------------------------------ đọc file đã liên kết

    public static string Sha1(byte[] b) => Convert.ToHexString(SHA1.HashData(b)).ToLowerInvariant();
    private static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream(); fs.CopyTo(ms); return ms.ToArray();
    }
    public bool IsLinked(Pair p) => St.Links.ContainsKey(SlotOf(p.Id)) && p.Linked && p.Bytes is not null && p.Kho is not null;
    public Link? LinkOf(string slot) => St.Links.GetValueOrDefault(slot);

    private bool LoadFromLink(string slot)
    {
        var lk = St.Links.GetValueOrDefault(slot); if (lk is null) return false;
        var p = slot == "xuong" ? null : PairOf(slot == "kho2" ? 2 : 1);
        void Err(string m) { if (p is not null) { p.LinkErr = m; p.Linked = false; } else XErr = m; }
        byte[] bytes;
        try { bytes = ReadShared(lk.Path); }
        catch (FileNotFoundException) { Err($"Không thấy file “{lk.Name}” — đã đổi tên / chuyển / xóa? Bấm “Đổi file…” để chọn lại."); return false; }
        catch (DirectoryNotFoundException) { Err($"Không thấy file “{lk.Name}” — đã đổi tên / chuyển / xóa? Bấm “Đổi file…” để chọn lại."); return false; }
        catch (Exception e) { Err("Không đọc được file: " + e.Message); return false; }
        try { var fi = new FileInfo(lk.Path); _stamp[slot] = (fi.Length, fi.LastWriteTimeUtc); } catch { /* bỏ qua */ }
        DcxXlsx.Workbook book;
        try { book = DcxXlsx.ReadWorkbook(bytes); }
        catch (Exception e) { Err("File không mở được: " + e.Message); return false; }
        if (slot == "xuong")
        {
            XErr = ""; Xuong = new Xwb { File = lk.Name, Names = book.Names, Aoa = book.Aoa }; XSha = Sha1(bytes);
            foreach (var q in Pairs) { if (q.XuongSheet.Length > 0 && !book.Names.Contains(q.XuongSheet)) q.XuongSheet = ""; q.Xuong = null; q.Result = null; }
            AutoPickSheets();
            foreach (var q in Pairs) if (q.XuongSheet.Length > 0) LoadXuong(q);
            return true;
        }
        p!.LinkErr = "";
        p.KhoSheets = book.Names; p.KhoAoa = book.Aoa;
        var prev = p.Kho?.Sheet ?? St.KhoSheet.GetValueOrDefault(slot, "");
        var name = prev.Length > 0 && book.Names.Contains(prev) ? prev : book.Names.FirstOrDefault(n => ParseSheet(book.Aoa[n]).Ok) ?? book.Names[0];
        var before = p.Kho;
        LoadKho(p, name, lk.Name);
        if (ReferenceEquals(p.Kho, before) || p.Kho is null) { p.LinkErr = "Chưa đọc được file (chưa chọn đủ cột) — bấm “Nạp lại” để chọn cột."; p.Linked = false; p.Bytes = null; return false; }
        p.Bytes = bytes; p.Linked = true; p.Sha = Sha1(bytes); St.KhoSheet[slot] = name;
        if (Xuong is not null) { p.Xuong = null; AutoPickSheets(); if (p.XuongSheet.Length > 0) LoadXuong(p); }
        return true;
    }
    private void LoadKho(Pair p, string sheet, string file)
    {
        var res = ParseWith(SlotOf(p.Id), p.KhoAoa.GetValueOrDefault(sheet) ?? new(), KN(p.Id) + " — " + file);
        if (res is null) return;
        p.Kho = new SideData { File = file, Sheet = sheet, LoadedAt = NowTxt(), Res = res };
    }
    private void LoadXuong(Pair p)
    {
        if (Xuong is null || p.XuongSheet.Length == 0) return;
        var res = ParseWith("x:" + p.XuongSheet, Xuong.Aoa.GetValueOrDefault(p.XuongSheet) ?? new(), $"Xưởng — sheet “{p.XuongSheet}”");
        p.Xuong = res is null ? null : new SideData { File = Xuong.File, Sheet = p.XuongSheet, LoadedAt = NowTxt(), Res = res };
    }

    // ánh xạ cột đã chọn chỉ dùng lại khi tiêu đề của các cột đó vẫn y như lúc chọn
    private Dictionary<string, int>? ValidMap(string key, List<object?[]> aoa)
    {
        if (!St.Maps.TryGetValue(key, out var m)) return null;
        var h0 = ParseSheet(aoa, new ParseOptions { HeaderRow = St.Hdr.TryGetValue(key, out var hr) ? hr : null });
        var heads = h0.Ok ? h0.Headers : h0.Need?.Headers ?? new();
        var sig = St.Sigs.GetValueOrDefault(key);
        var ok = sig is not null && m.All(kv => sig.TryGetValue(kv.Key, out var nm) && (heads.FirstOrDefault(h => h.J == kv.Value)?.Name ?? "") == nm);
        if (!ok) { St.Maps.Remove(key); St.Sigs.Remove(key); return null; }
        return m;
    }
    private ParseResult ParseKey(string key, List<object?[]> aoa) =>
        ParseSheet(aoa, new ParseOptions { Map = ValidMap(key, aoa), HeaderRow = St.Hdr.TryGetValue(key, out var hr) ? hr : null, DateFromTotals = key.StartsWith('x') });
    private ParseResult? ParseWith(string key, List<object?[]> aoa, string label)
    {
        var res = ParseKey(key, aoa);
        if (res.Ok && res.Rows.Count == 0 && res.Errors.Count > 0 && St.Maps.ContainsKey(key)) { St.Maps.Remove(key); St.Sigs.Remove(key); res = ParseKey(key, aoa); }
        if (res.Ok) { Needs.RemoveAll(n => n.Key == key); return res; }
        Needs.RemoveAll(n => n.Key == key);
        Needs.Add(new Need(key, label, res.Need!, aoa));
        return null;
    }
    /// <summary>Người dùng chọn cột / dòng tiêu đề cho một file / sheet (hộp "Chọn cột").</summary>
    public void AnswerNeed(Need need, Dictionary<string, int> map, int? headerRow)
    {
        if (need.Map.HeaderRow is null) { if (headerRow is null) throw new BusinessException("Hãy chọn dòng tiêu đề."); St.Hdr[need.Key] = headerRow.Value; }
        else
        {
            if (need.Map.Fields.Any(f => !map.ContainsKey(f))) throw new BusinessException("Còn cột chưa chọn.");
            if (map.Values.Distinct().Count() < map.Count) throw new BusinessException("Hai trường đang chọn cùng 1 cột — hãy chọn lại.");
            var old = St.Maps.GetValueOrDefault(need.Key) ?? new(); var sig = St.Sigs.GetValueOrDefault(need.Key) ?? new();
            foreach (var (f, j) in map) { old[f] = j; sig[f] = need.Map.Headers.FirstOrDefault(h => h.J == j)?.Name ?? ""; }
            St.Maps[need.Key] = old; St.Sigs[need.Key] = sig;
        }
        Needs.Remove(need);
        Persist();
        ReloadKey(need.Key);
    }
    public void SkipNeed(Need need) => Needs.Remove(need);
    private void ReloadKey(string key)
    {
        if (key.StartsWith("x:")) { var sh = key[2..]; foreach (var p in Pairs) if (p.XuongSheet == sh) LoadXuong(p); }
        else LoadFromLink(key);
        foreach (var p in Pairs) RefreshPair(p);
    }

    private (string Sheet, int Chung, int Kho, int Xuong)? OverlapOf(Pair p, string sheet)
    {
        if (Xuong is null || p.Kho is null || !Xuong.Aoa.TryGetValue(sheet, out var aoa)) return null;
        var rx = ParseKey("x:" + sheet, aoa);
        if (!rx.Ok) return null;
        var o = Overlap(p.Kho.Res.Rows, rx.Rows); return (sheet, o.Chung, o.Kho, o.Xuong);
    }
    /// <summary>Gợi ý ghép sheet Xưởng ↔ file Kho theo số mã TRÙNG NGUYÊN VĂN; chỉ tự điền khi sheet đó hơn hẳn.</summary>
    private void AutoPickSheets()
    {
        if (Xuong is null) return;
        foreach (var p in Pairs)
        {
            if (p.XuongSheet.Length > 0) continue;
            var ov = Xuong.Names.Select(n => OverlapOf(p, n)).Where(x => x is not null).Select(x => x!.Value).OrderByDescending(x => x.Chung).ToList();
            if (ov.Count > 0 && ov[0].Chung > 0 && (ov.Count == 1 || ov[0].Chung > ov[1].Chung)) p.XuongSheet = ov[0].Sheet;
        }
        if (P1.XuongSheet.Length > 0 && P1.XuongSheet == P2.XuongSheet)
        {
            var oa = OverlapOf(P1, P1.XuongSheet); var ob = OverlapOf(P2, P2.XuongSheet);
            if (oa is not null && ob is not null) { if (oa.Value.Chung >= ob.Value.Chung) P2.XuongSheet = ""; else P1.XuongSheet = ""; }
        }
    }

    // ------------------------------------------------------------------ liên kết

    /// <summary>Liên kết file Excel thật (đường dẫn trên máy này). Xưởng chỉ đọc.</summary>
    public string LinkFile(string slot, string path)
    {
        RequireView();
        if (!File.Exists(path)) throw new BusinessException("Không thấy file: " + path);
        if (!path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Chỉ liên kết được file Excel .xlsx (file .xls cũ: mở bằng Excel rồi Lưu thành .xlsx).");
        foreach (var (s, l) in St.Links)
            if (s != slot && string.Equals(Path.GetFullPath(l.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                throw new BusinessException($"File này đang liên kết cho {SlotLabel(s)} — mỗi ô phải là một file riêng.");
        St.Links[slot] = new Link { Path = Path.GetFullPath(path), Name = Path.GetFileName(path), At = NowTxt() };
        if (slot == "xuong")
        {
            Xuong = null; St.Edits.RemoveAll(e => e.Side == "xuong");
            foreach (var p in Pairs) { p.XuongSheet = ""; p.Xuong = null; p.Result = null; }
        }
        else
        {
            var p = PairOf(slot == "kho2" ? 2 : 1); p.Kho = null; p.Result = null; p.Linked = false; p.Bytes = null; St.KhoSheet.Remove(slot);
            St.Edits.RemoveAll(e => e.Pair == p.Id && e.Side == "kho");
        }
        Needs.RemoveAll(n => n.Key == slot || (slot == "xuong" && n.Key.StartsWith("x:")));
        LoadFromLink(slot);
        foreach (var p in Pairs) RefreshPair(p);
        Persist();
        _audit.Log(User, "doi-chieu-lien-ket", $"{SlotLabel(slot)}: {Path.GetFileName(path)}");
        return CloudWarn(path);
    }
    public static string CloudWarn(string path) => Regex.IsMatch(path, @"OneDrive|Google ?Drive|Dropbox|iCloud", RegexOptions.IgnoreCase)
        ? "File nằm trong thư mục đồng bộ đám mây — module này không đồng bộ gì, nhưng OneDrive / Google Drive / Dropbox có thể tự tạo bản “conflicted copy” khi app ghi file. Nên để file Xuất trên ổ máy." : "";
    public void Unlink(string slot)
    {
        St.Links.Remove(slot);
        if (slot == "xuong") { Xuong = null; XSha = ""; XErr = ""; foreach (var p in Pairs) { p.Xuong = null; p.Result = null; } St.Edits.RemoveAll(e => e.Side == "xuong"); }
        else { var p = PairOf(slot == "kho2" ? 2 : 1); p.Kho = null; p.Result = null; p.Linked = false; p.Bytes = null; p.LinkErr = ""; }
        foreach (var p in Pairs) RefreshPair(p);
        Persist();
    }
    public void UnlinkAll()
    {
        St = new State { BackupDir = St.BackupDir };
        Xuong = null; XSha = ""; XErr = ""; Needs.Clear();
        foreach (var p in Pairs) { p.Kho = null; p.Xuong = null; p.Result = null; p.Linked = false; p.Bytes = null; p.LinkErr = ""; p.XuongSheet = ""; p.Sha = ""; }
        Persist();
    }
    public void Reload(string slot)
    {
        if (slot != "xuong") PairOf(slot == "kho2" ? 2 : 1).Sha = ""; else XSha = "";
        LoadFromLink(slot);
        foreach (var p in Pairs) RefreshPair(p);
        Persist();
    }
    public void SetXuongSheet(int pairId, string sheet)
    {
        var p = PairOf(pairId); p.XuongSheet = sheet; p.Xuong = null; p.Result = null;
        St.Edits.RemoveAll(e => e.Pair == p.Id && e.Side == "xuong");
        if (sheet.Length > 0) LoadXuong(p);
        RefreshPair(p); Persist();
    }
    public void SetBackupDir(string dir) { St.BackupDir = dir; Persist(); }

    /// <summary>File Excel đã đổi bên ngoài app (so kích thước / giờ sửa, rồi mã băm) → đọc lại. Trả các ô đã đọc lại.</summary>
    public List<string> SyncLinks(bool force = false)
    {
        var changed = new List<string>();
        foreach (var slot in new[] { "kho1", "kho2", "xuong" })
        {
            if (!St.Links.TryGetValue(slot, out var lk)) continue;
            var p = slot == "xuong" ? null : PairOf(slot == "kho2" ? 2 : 1);
            FileInfo fi;
            try { fi = new FileInfo(lk.Path); if (!fi.Exists) throw new FileNotFoundException(); }
            catch
            {
                var m = $"Không thấy file “{lk.Name}” — đã đổi tên / chuyển / xóa? Bấm “Đổi file…” để chọn lại.";
                if (p is not null) { if (p.LinkErr != m) { p.LinkErr = m; changed.Add(slot); } } else if (XErr != m) { XErr = m; changed.Add(slot); }
                continue;
            }
            var stamp = (fi.Length, fi.LastWriteTimeUtc);
            if (!force && _stamp.TryGetValue(slot, out var old) && old == stamp && (p is null || p.Linked)) continue;
            string sha;
            try { sha = Sha1(ReadShared(lk.Path)); } catch { continue; }
            var known = p is not null ? p.Sha : XSha;
            if (sha != known || (p is not null && !p.Linked)) { LoadFromLink(slot); changed.Add(slot); }
            else _stamp[slot] = stamp;
        }
        if (changed.Count > 0) { foreach (var p in Pairs) RefreshPair(p); Persist(); }
        return changed;
    }

    // ------------------------------------------------------------------ đối chiếu

    public void RefreshPair(Pair p)
    {
        p.Sug = Xuong is null ? new() : Xuong.Names.Select(n => OverlapOf(p, n)).Where(x => x is not null).Select(x => x!.Value).OrderByDescending(x => x.Chung).ToList();
        p.EffIdx = new();
        if (p.Kho is not null)
            foreach (var r in p.Kho.Res.Rows)
                if (r.Row1 > 0 && r.Sl > 0) { var k = r.Ngay + "|" + r.Ma; if (!p.EffIdx.TryGetValue(k, out var l)) p.EffIdx[k] = l = new(); l.Add(r); }
        p.Result = null;
        if (p.Kho is null || p.Xuong is null) return;
        var (rows, totals) = CmpInput(p);
        p.Result = Compare(rows.kho, rows.xuong, new DcxEngine.CompareOptions
        {
            KhoTotals = totals.kho, XuongTotals = totals.xuong, KhoNoCode = p.Kho.Res.NoCode, XuongNoCode = p.Xuong.Res.NoCode,
            SizeOf = SizeOf, WeightOf = WeightOfMa,
        });
        foreach (var it in p.Result.Items) if (!it.TheoSize) { it.KN -= it.KRows.Count(x => x == "sửa tay"); it.XN -= it.XRows.Count(x => x == "sửa tay"); }
    }
    /// <summary>Số đã sửa tay (Xưởng) được cộng thành 1 dòng "sửa tay" (chênh so với số đọc từ file); dòng tổng ngày cộng thêm phần chênh.</summary>
    private ((List<Row> kho, List<Row> xuong) rows, (List<Total> kho, List<Total> xuong) totals) CmpInput(Pair p)
    {
        var rows = new Dictionary<string, List<Row>> { ["kho"] = p.Kho!.Res.Rows, ["xuong"] = p.Xuong!.Res.Rows };
        var tots = new Dictionary<string, List<Total>> { ["kho"] = p.Kho.Res.Totals, ["xuong"] = p.Xuong.Res.Totals };
        p.AggKho = new(); p.AggXuong = new();
        foreach (var side in new[] { "kho", "xuong" })
        {
            var agg = side == "kho" ? p.AggKho : p.AggXuong;
            foreach (var r in rows[side]) { var k = r.Ngay + "|" + r.Ma; var a = agg.GetValueOrDefault(k); agg[k] = (a.Sl + r.Sl, a.Net + r.Net); }
            var es = St.Edits.Where(e => e.Pair == p.Id && e.Side == side && (side == "xuong" || !IsLinked(p))).ToList();
            if (es.Count == 0) continue;
            var grp = new Dictionary<string, (string ngay, string ma, string ten, double dsl, double dnet)>();
            foreach (var e in es)
            {
                var k = e.Ngay + "|" + e.Ma; var a = agg.GetValueOrDefault(k); (string ngay, string ma, string ten, double dsl, double dnet) g = grp.TryGetValue(k, out var gg) ? gg : (e.Ngay, e.Ma, e.Ten, 0d, 0d);
                if (e.Field == "sl") g.dsl = R6(e.After - a.Sl); else g.dnet = R6(e.After - a.Net);
                grp[k] = g;
            }
            var extra = new List<Row>(); var dayD = new Dictionary<string, (double sl, double net)>();
            foreach (var g in grp.Values)
            {
                extra.Add(new Row { RowLabel = "sửa tay", Ngay = g.ngay, Ma = g.ma, Ten = g.ten, Sl = g.dsl, Net = g.dnet, Edited = true });
                var d = dayD.GetValueOrDefault(g.ngay); dayD[g.ngay] = (d.sl + g.dsl, d.net + g.dnet);
            }
            rows[side] = rows[side].Concat(extra).ToList();
            tots[side] = tots[side].Select(t => dayD.TryGetValue(t.Ngay, out var d) ? t with { Sl = R6(t.Sl + d.sl), Net = R6(t.Net + d.net) } : t).ToList();
        }
        return ((rows["kho"], rows["xuong"]), (tots["kho"], tots["xuong"]));
    }

    public ManualEdit? EditOf(int pair, string side, string ngay, string ma, string field) =>
        St.Edits.FirstOrDefault(e => e.Pair == pair && e.Side == side && e.Ngay == ngay && e.Ma == ma && e.Field == field);
    /// <summary>Sửa tay SL / Net của Xưởng (hoặc Kho chưa liên kết) — chỉ để đối chiếu, file Excel không bị đụng. Nhập lại số gốc = hoàn tác.</summary>
    public string ApplyEdit(int pid, string side, string ngay, string ma, string field, double v)
    {
        var p = PairOf(pid); var it = p.Result?.Items.FirstOrDefault(x => x.Key == ngay + "|" + ma && !x.TheoSize) ?? throw new BusinessException("Không thấy dòng này trong kết quả đối chiếu.");
        var agg = (side == "kho" ? p.AggKho : p.AggXuong).GetValueOrDefault(ngay + "|" + ma);
        double? orig = (side == "kho" ? p.AggKho : p.AggXuong).ContainsKey(ngay + "|" + ma) ? (field == "sl" ? agg.Sl : agg.Net) : null;
        var val = R6(v); var ex = EditOf(pid, side, ngay, ma, field);
        var lab = (field == "sl" ? "SL " : "Net ") + (side == "kho" ? "Kho" : "Xưởng");
        if (ex is not null && Math.Abs(ex.After - val) < 1e-9) return "";
        St.Edits.RemoveAll(e => ReferenceEquals(e, ex));
        string msg;
        if (orig is not null && Math.Abs(orig.Value - val) < 1e-9) msg = $"Đã trả ô {lab} về số gốc.";
        else
        {
            St.Edits.Add(new ManualEdit { Id = NewId(), At = DateTime.Now.ToString("s", CultureInfo.InvariantCulture), By = User, Pair = pid, Side = side, Ngay = ngay, Ma = ma, Ten = it.Ten, Field = field, Before = orig, After = val });
            msg = $"Đã sửa tay {lab} của {ma} ({Dmy(ngay)}): {Fmt(orig)} → {Fmt(val)}";
        }
        RefreshPair(p); Persist();
        return msg;
    }
    public void UndoEdit(string id) { St.Edits.RemoveAll(e => e.Id == id); foreach (var p in Pairs) RefreshPair(p); Persist(); }
    private static string Fmt(double? v) => v is null ? "(trống)" : v.Value.ToString("#,0.###", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ vị trí mã + ghi chú dòng

    public sealed record ViTriInfo(string Kind, int To, string Short, string Text, double Qty);
    /// <summary>Mã lệch ở file này thì đang nằm ở file NÀO (so cùng ngày + mã với cặp còn lại) — chỉ GỢI Ý.</summary>
    public ViTriInfo? ViTri(int pid, Item it)
    {
        if (it.TheoSize) return null;
        var p = PairOf(pid); var q = PairOf(OtherId(pid));
        if (p.Result is null || q.Result is null) return null;
        var o = q.Result.ByKey(it.Key);
        double kS = it.KSl ?? 0, xS = it.XSl ?? 0, qk = o?.KSl ?? 0, qx = o?.XSl ?? 0;
        double exK = R6(kS - xS), exX = R6(xS - kS), tol = Math.Max(EpsNet, 0.01);
        static string I(double n) => n.ToString("#,0", CultureInfo.InvariantCulture);
        static string Kg(double n) => n.ToString("#,0.00#", CultureInfo.InvariantCulture);
        if (exK > 0)
        {
            var qEx = R6(qx - qk);
            if (qEx > 0)
            {
                var qty = Math.Min(exK, qEx); var netQ = R6(((o!.XNet ?? 0) - (o.KNet ?? 0)) * qty / qEx); var netEx = R6((it.KNet ?? 0) - (it.XNet ?? 0));
                var full = qty == exK && qty == qEx && Math.Abs(netQ - netEx) <= tol;
                return new ViTriInfo("chuyen", q.Id, (full ? "⇄ Chuyển sang " + KN(q.Id) : $"⇄ Chuyển tối đa {I(qty)} sang {KN(q.Id)}") + $" ({I(qty)} · {Kg(netQ)} kg)",
                    $"Xưởng ghi thêm {I(qEx)} · {Kg(R6((o.XNet ?? 0) - (o.KNet ?? 0)))} kg ở {KN(q.Id)}" + (full ? $" — khớp SL và Net → nên chuyển sang {KN(q.Id)}" : $" — {KN(p.Id)} đang thừa {I(exK)} · {Kg(netEx)} kg, chưa khớp hẳn; chuyển tối đa {I(qty)}"), qty);
            }
            if (xS == 0 && qx == 0) return new ViTriInfo("khong", 0, "Xưởng chưa ghi ở cả 2 sheet", $"Xưởng chưa ghi mã này ở cả 2 sheet — Kho đang ghi {I(exK)}", 0);
            return new ViTriInfo("khac", 0, "", $"Phần Kho thừa {I(exK)} không thấy Xưởng ghi thêm ở {KN(q.Id)}", 0);
        }
        if (exX > 0)
        {
            var qEx = R6(qk - qx);
            if (qEx > 0)
            {
                var qty = Math.Min(exX, qEx); var netQ = R6(((o!.KNet ?? 0) - (o.XNet ?? 0)) * qty / qEx); var netEx = R6((it.XNet ?? 0) - (it.KNet ?? 0));
                var full = qty == exX && qty == qEx && Math.Abs(netQ - netEx) <= tol;
                return new ViTriInfo("chuyen", p.Id, (full ? "⇄ Lấy về từ " + KN(q.Id) : $"⇄ Lấy tối đa {I(qty)} từ {KN(q.Id)}") + $" ({I(qty)} · {Kg(netQ)} kg)",
                    $"Kho ghi thêm {I(qEx)} · {Kg(R6((o.KNet ?? 0) - (o.XNet ?? 0)))} kg ở {KN(q.Id)}" + (full ? $" — khớp SL và Net → nên lấy về {KN(p.Id)}" : $" — Xưởng đang ghi thừa {I(exX)} · {Kg(netEx)} kg, chưa khớp hẳn; lấy về tối đa {I(qty)}"), qty);
            }
            if (kS == 0 && qk == 0) return new ViTriInfo("khong", 0, "Kho chưa ghi ở cả 2 file", $"Kho chưa ghi mã này ở cả 2 file — Xưởng đang ghi {I(exX)}", 0);
            return new ViTriInfo("khac", 0, "", $"Phần Xưởng thừa {I(exX)} không thấy Kho ghi thêm ở {KN(q.Id)}", 0);
        }
        return null;
    }
    public sealed record RowNote(string Line, string Tip, bool MaFlag, bool Move, bool CanAddToKho, List<string> Flags);
    /// <summary>Ghi chú 1 dòng ngắn (Sai mã · Sai SL · Không có ở …) + chi tiết cho tooltip — chỉ báo, không tự sửa.</summary>
    public RowNote NoteOf(int pid, Item it)
    {
        var tips = new List<string>(); var flags = new List<string>(); var kho = KN(pid);
        bool mv = false, maFlag = false; var line = "";
        var vt = ViTri(pid, it);
        if (vt is { Kind: "chuyen" }) { mv = true; line = vt.Short; tips.Add(vt.Text); }
        else
        {
            if (vt is not null && vt.Text.Length > 0) tips.Add(vt.Text);
            if (!it.TheoSize && _m8.Count > 0)
            {
                var nc = !_m8.ContainsKey(it.Ma) ? NearCodes(it.Ma, _m8.Keys, 2, 1) : new();
                if (nc.Count > 0) { flags.Add("Sai mã"); tips.Add($"Sai mã (khác {nc[0].D} ký tự) — gần giống mã trong Danh sách: {nc[0].Ma}"); }
                else if (it.GoiY is { Count: > 0 }) { flags.Add("Nghi sai mã"); tips.Add("Cùng SL & Net với mã: " + it.GoiY[0].Ma); }
                else if (!_m8.ContainsKey(it.Ma)) flags.Add("Mã không có trong Danh sách");
                maFlag = flags.Count > 0;
            }
            else if (!it.TheoSize && it.GoiY is { Count: > 0 }) { flags.Add("Nghi sai mã"); tips.Add("Cùng SL & Net với mã: " + it.GoiY[0].Ma); maFlag = true; }
            switch (it.Status)
            {
                case "lech":
                    var dsl = (it.KSl ?? 0) != (it.XSl ?? 0); var dnet = Math.Abs((it.KNet ?? 0) - (it.XNet ?? 0)) > EpsNet;
                    flags.Add(dsl && dnet ? "Sai SL & Net" : dsl ? "Sai số lượng" : dnet ? "Sai Net" : "Lệch");
                    tips.Add($"Δ SL {Sgn(it.DSl)} · Δ Net {SgnKg(it.DNet)} kg"); break;
                case "zero": flags.Add("Ghi 0 ở " + (it.KSl == 0 && it.KNet == 0 ? kho : "Xưởng")); break;
                case "chi_xuong": if (!maFlag) flags.Add("Không có ở " + kho); else tips.Add("Không có trong file " + kho); break;
                case "chi_kho": if (!maFlag) flags.Add("Không có ở Xưởng"); else tips.Add("Không có trong file Xưởng"); break;
            }
            static string F0(double d) => d.ToString("#,0.###", CultureInfo.InvariantCulture);
            tips.AddRange(NetFixNotes(it, F0, d => d.ToString("#,0.00#", CultureInfo.InvariantCulture)));
            if (it.GoiYNhom is { Count: > 0 }) tips.Add("Bằng tổng: " + string.Join(" | ", it.GoiYNhom.Select(g => string.Join(" + ", g.Select(x => $"{x.Ma} ({F0(x.Sl ?? 0)})")))));
            if (flags.Count > 0) line = (maFlag ? "⚠ " : "") + string.Join(" · ", flags);
            else if (it.TheoSize) line = "Đối chiếu nhanh theo Size + trọng lượng";
        }
        if (it.TheoSize && it.Ten.Length > 0) tips.Add(it.Ten);
        var canAdd = !mv && it.Status == "chi_xuong" && !maFlag && !it.TheoSize;
        return new RowNote(line, string.Join(" · ", tips), maFlag, mv, canAdd, flags);
    }
    public static string Sgn(double n) => n == 0 ? "0" : (n > 0 ? "+" : "") + n.ToString("#,0.###", CultureInfo.InvariantCulture);
    public static string SgnKg(double n) => Math.Abs(n) <= EpsNet ? "0.00" : (n > 0 ? "+" : "") + n.ToString("#,0.00#", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ ghi thẳng vào file Kho

    public double Movable(Pair p, string ngay, string ma) => R6(p.EffIdx.GetValueOrDefault(ngay + "|" + ma)?.Sum(r => r.Sl) ?? 0);
    public List<Row> RowsOf(Pair p, string ngay, string ma) => p.EffIdx.GetValueOrDefault(ngay + "|" + ma) ?? new();
    public sealed record Part(int Row, double Sl, double Tl);
    /// <summary>Chia số lượng cho các dòng gốc: ưu tiên 1 dòng đủ số (không tách dòng), không thì lấy từ dòng lớn nhất.</summary>
    public List<Part> Allocate(Pair p, string key, double want)
    {
        var rows = (p.EffIdx.GetValueOrDefault(key) ?? new()).ToList(); var avail = R6(rows.Sum(r => r.Sl));
        want = Math.Min(R6(want), avail);
        static double TlOf(Row r) => r.Sl != 0 ? Math.Round(r.Net / r.Sl * 1e6, MidpointRounding.AwayFromZero) / 1e6 : 0;
        var one = rows.FirstOrDefault(r => r.Sl >= want - 1e-9);
        if (one is not null) return new() { new Part(one.Row1, want, TlOf(one)) };
        var parts = new List<Part>(); var left = want;
        foreach (var r in rows.OrderByDescending(r => r.Sl)) { if (left <= 1e-9) break; var t = Math.Min(r.Sl, left); parts.Add(new Part(r.Row1, R6(t), TlOf(r))); left = R6(left - t); }
        return parts;
    }

    private sealed class SlotSpec { public Dictionary<int, double> Rm = new(); public List<DcxXlsx.Add> Adds = new(); public List<DcxXlsx.Rename> Renames = new(); public bool Move; }
    private sealed record Taken(int Row, double Sl, string Ma, string Ten, double? Tl);
    private (Dictionary<int, SlotSpec> specs, List<Op> inverse) PlanOps(List<Op> ops)
    {
        var specs = new Dictionary<int, SlotSpec> { [1] = new(), [2] = new() }; var inverse = new List<Op>();
        static string I(double n) => n.ToString("#,0.###", CultureInfo.InvariantCulture);
        List<Taken> Take(int id, string ngay, string ma, double? sl)
        {
            var p = PairOf(id); var avail = Movable(p, ngay, ma);
            if (!(avail > 0)) throw new BusinessException($"{KN(id)} không có mã {ma} ngày {Dmy(ngay)} (file Excel vừa được sửa?). Bấm “Nạp lại” rồi làm lại.");
            var want = sl ?? avail;
            if (want > avail + 1e-9) throw new BusinessException($"{KN(id)} chỉ còn {I(avail)} của mã {ma} ngày {Dmy(ngay)}, cần {I(want)}.");
            var parts = Allocate(p, ngay + "|" + ma, want);
            var cells = DcxXlsx.ReadCells(p.Bytes!, p.Kho!.Sheet, parts.Select(x => x.Row));
            return parts.Select(pt =>
            {
                if (!cells.TryGetValue(pt.Row, out var c) || c.Ma.Length == 0) throw new BusinessException($"Không đọc được dòng {pt.Row} của file {KN(id)} (file có bị sửa sau khi nạp?).");
                return new Taken(pt.Row, pt.Sl, c.Ma, c.Ten, c.Tl ?? pt.Tl);
            }).ToList();
        }
        void Rm(int id, List<Taken> parts) { foreach (var pt in parts) specs[id].Rm[pt.Row] = R6(specs[id].Rm.GetValueOrDefault(pt.Row) + pt.Sl); }
        void AddTo(int id, string ngay, string ma, string ten, double sl, double? tl, bool noMerge = false) =>
            specs[id].Adds.Add(new DcxXlsx.Add(ngay, ma, ten, sl, tl, tl is not null ? R6(sl * tl.Value) : null, noMerge));
        foreach (var op in ops)
        {
            switch (op.Type)
            {
                case "move":
                {
                    var parts = Take(op.From, op.Ngay, op.Ma, op.Sl); Rm(op.From, parts);
                    foreach (var pt in parts) AddTo(op.To, op.Ngay, pt.Ma, pt.Ten, pt.Sl, pt.Tl);
                    specs[op.From].Move = true; specs[op.To].Move = true;
                    inverse.Add(new Op { Type = "move", From = op.To, To = op.From, Ngay = op.Ngay, Ma = op.Ma, Sl = R6(parts.Sum(x => x.Sl)) });
                    break;
                }
                case "setsl":
                {
                    var p = PairOf(op.Slot); var cur = Movable(p, op.Ngay, op.Ma); var target = R6(op.Target ?? 0); var delta = R6(target - cur);
                    if (!(cur > 0)) throw new BusinessException($"{KN(op.Slot)} không có mã {op.Ma} ngày {Dmy(op.Ngay)}.");
                    if (delta < 0) Rm(op.Slot, Take(op.Slot, op.Ngay, op.Ma, -delta));
                    else if (delta > 0)
                    {
                        var an = RowsOf(p, op.Ngay, op.Ma).OrderByDescending(r => r.Sl).First();
                        var cells = DcxXlsx.ReadCells(p.Bytes!, p.Kho!.Sheet, new[] { an.Row1 });
                        if (!cells.TryGetValue(an.Row1, out var c) || c.Ma.Length == 0) throw new BusinessException($"Không đọc được dòng {an.Row1} của file {KN(op.Slot)}.");
                        AddTo(op.Slot, op.Ngay, c.Ma, c.Ten, delta, c.Tl ?? (an.Sl != 0 ? R6(an.Net / an.Sl) : 0));
                    }
                    inverse.Add(new Op { Type = "setsl", Slot = op.Slot, Ngay = op.Ngay, Ma = op.Ma, Target = cur });
                    break;
                }
                case "delete":
                {
                    var parts = Take(op.Slot, op.Ngay, op.Ma, op.Sl); Rm(op.Slot, parts);
                    foreach (var pt in parts) inverse.Add(new Op { Type = "add", Slot = op.Slot, Ngay = op.Ngay, Ma = pt.Ma, Ten = pt.Ten, Sl = pt.Sl, Tl = pt.Tl });
                    break;
                }
                case "add":
                    if (!(op.Sl > 0)) throw new BusinessException("Thêm dòng cần Số lượng lớn hơn 0.");
                    AddTo(op.Slot, op.Ngay, op.Ma, op.Ten, op.Sl!.Value, op.Tl is > 0 ? op.Tl : null, !(op.Tl > 0));
                    inverse.Add(new Op { Type = "delete", Slot = op.Slot, Ngay = op.Ngay, Ma = NormMa(op.Ma), Sl = op.Sl });
                    break;
                case "retag":
                {
                    var parts = Take(op.Slot, op.Ngay, op.Ma, op.Sl); Rm(op.Slot, parts);
                    var nNgay = op.NewNgay ?? op.Ngay; var nMa = op.NewMa is not null ? CleanMa(op.NewMa) : null;
                    foreach (var pt in parts) AddTo(op.Slot, nNgay, nMa ?? pt.Ma, pt.Ten, pt.Sl, pt.Tl);
                    inverse.Add(new Op { Type = "retag", Slot = op.Slot, Ngay = nNgay, Ma = nMa is not null ? NormMa(nMa) : op.Ma, Sl = R6(parts.Sum(x => x.Sl)),
                        NewNgay = op.NewNgay is not null ? op.Ngay : null, NewMa = op.NewMa is not null ? op.Ma : null });
                    break;
                }
                case "rename":
                {
                    var p = PairOf(op.Slot); var inv = new List<RenameItem>();
                    foreach (var it in op.Items ?? new())
                    {
                        var rows = RowsOf(p, it.Ngay, it.Ma);
                        if (rows.Count == 0) throw new BusinessException($"{KN(op.Slot)} không có mã {it.Ma} ngày {Dmy(it.Ngay)} (file Excel vừa được sửa?). Bấm “Nạp lại” rồi làm lại.");
                        var cells = DcxXlsx.ReadCells(p.Bytes!, p.Kho!.Sheet, rows.Select(r => r.Row1));
                        foreach (var r in rows)
                        {
                            if (!cells.TryGetValue(r.Row1, out var c) || c.Ma.Length == 0) throw new BusinessException($"Không đọc được dòng {r.Row1} của file {KN(op.Slot)}.");
                            if (c.Ten == it.Ten) continue;
                            specs[op.Slot].Renames.Add(new DcxXlsx.Rename(r.Row1, c.Ma, it.Ten)); inv.Add(new RenameItem(it.Ngay, it.Ma, c.Ten));
                        }
                    }
                    if (inv.Count > 0) inverse.Add(new Op { Type = "rename", Slot = op.Slot, Items = inv });
                    break;
                }
                default: throw new BusinessException("Thao tác không hợp lệ: " + op.Type);
            }
        }
        inverse.Reverse();   // hoàn tác theo thứ tự ngược
        return (specs, inverse);
    }

    public sealed class CommitException : BusinessException { public string Code { get; } public CommitException(string code, string msg) : base(msg) { Code = code; } }
    private sealed record Job(string Slot, string Path, byte[] Orig, byte[] Next, string Tmp);

    /// <summary>Ghi tất cả file hoặc không file nào (như dcx:commit của app cũ).</summary>
    public List<string> Commit(List<(string Slot, byte[] Bytes, string ExpectSha)> writes)
    {
        var jobs = new List<Job>();
        foreach (var w in writes)
        {
            if (w.Slot is not ("kho1" or "kho2")) throw new CommitException("RO", "File Xưởng chỉ đọc — không ghi được.");
            var lk = St.Links.GetValueOrDefault(w.Slot) ?? throw new CommitException("NOLINK", $"Chưa liên kết file {SlotLabel(w.Slot)}.");
            byte[] cur;
            try { cur = ReadShared(lk.Path); } catch (Exception e) { throw new CommitException("MISSING", $"Không đọc được file {SlotLabel(w.Slot)} ({e.Message})."); }
            if (Sha1(cur) != w.ExpectSha) throw new CommitException("CHANGED", $"File Excel {SlotLabel(w.Slot)} vừa được thay đổi bên ngoài app.");
            if (IsLocked(lk.Path)) throw new CommitException("LOCKED", $"File Excel {SlotLabel(w.Slot)} đang mở trong Excel (hoặc chương trình khác). Hãy lưu và đóng file rồi thử lại — CHƯA ghi gì.");
            if (w.Bytes.Length < 100 || w.Bytes[0] != 0x50 || w.Bytes[1] != 0x4b) throw new CommitException("BAD", $"File mới tạo không hợp lệ ({SlotLabel(w.Slot)}) — không ghi.");
            try { var wb = DcxXlsx.ReadWorkbook(w.Bytes); if (wb.Names.Count == 0) throw new InvalidDataException("không có sheet"); }
            catch (Exception e) { throw new CommitException("BAD", $"File mới tạo không mở lại được ({SlotLabel(w.Slot)}): {e.Message} — không ghi."); }
            jobs.Add(new Job(w.Slot, lk.Path, cur, w.Bytes, lk.Path + ".kla-" + Environment.ProcessId + ".tmp"));
        }
        var backups = new List<string>();
        try { foreach (var j in jobs) backups.Add(BackupOnce(j.Slot, j.Path, j.Orig)); }
        catch (Exception e) { throw new CommitException("BACKUP", $"Không sao lưu được trước khi ghi ({e.Message}) — CHƯA ghi gì. Kiểm tra thư mục sao lưu."); }
        try { foreach (var j in jobs) WriteSynced(j.Tmp, j.Next); }
        catch (Exception e) { foreach (var j in jobs) TryDelete(j.Tmp); throw new CommitException("TMP", $"Không ghi được file tạm: {e.Message} — CHƯA đổi file nào."); }
        var done = new List<Job>(); Exception? err = null; string? errSlot = null;
        foreach (var j in jobs)
        {
            try { File.Move(j.Tmp, j.Path, true); done.Add(j); }
            catch (Exception e) { err = e; errSlot = j.Slot; break; }
        }
        if (err is null)
            foreach (var j in jobs)
            {
                try { if (Sha1(ReadShared(j.Path)) != Sha1(j.Next)) { err = new IOException("nội dung đọc lại không khớp"); errSlot = j.Slot; break; } }
                catch (Exception e) { err = e; errSlot = j.Slot; break; }
            }
        if (err is not null)
        {
            var bad = new List<string>();
            foreach (var j in jobs) TryDelete(j.Tmp);
            foreach (var j in done)
            {
                try { var t = j.Tmp + ".undo"; WriteSynced(t, j.Orig); File.Move(t, j.Path, true); }
                catch { bad.Add(SlotLabel(j.Slot) + ": " + backups[jobs.IndexOf(j)]); }
            }
            var locked = err is IOException or UnauthorizedAccessException;
            throw new CommitException(locked ? "LOCKED" : "WRITE",
                (locked ? $"File Excel {SlotLabel(errSlot!)} đang bị khóa (đang mở trong Excel?) — " : $"Ghi file {SlotLabel(errSlot!)} lỗi ({err.Message}) — ") +
                (bad.Count > 0 ? "KHÔNG trả lại được nguyên trạng cho: " + string.Join(" ; ", bad) + " (lấy bản sao lưu ở đường dẫn này)." : "đã trả lại nguyên trạng, không file nào bị đổi."));
        }
        return backups;
    }
    private static bool IsLocked(string path)
    {
        try { using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }
    private static void WriteSynced(string path, byte[] data)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(data); fs.Flush(true);
    }
    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { /* bỏ qua */ } }
    /// <summary>Sao lưu 1 bản / ngày / file, giữ 7 ngày.</summary>
    private string BackupOnce(string slot, string path, byte[] orig)
    {
        var dir = BackupDir; Directory.CreateDirectory(dir);
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dest = Path.Combine(dir, slot + "_" + today + "_" + Path.GetFileName(path));
        if (!File.Exists(dest)) WriteSynced(dest, orig);
        var cutoff = DateTime.Now.Date.AddDays(-KeepDays);
        foreach (var f in Directory.GetFiles(dir))
        {
            var m = Regex.Match(Path.GetFileName(f), @"^(kho1|kho2)_(\d{4})-(\d{2})-(\d{2})_");
            if (m.Success && new DateTime(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value)) < cutoff) TryDelete(f);
        }
        return dest;
    }

    public sealed record ApplyResult(string Message, int NetKept);
    /// <summary>Đổi thao tác → sửa file Kho (giữ mẫu) → ghi cả 2 file hoặc không file nào → đọc lại. Ghi lịch sử kèm thao tác ngược.</summary>
    public ApplyResult ApplyOps(List<Op> ops, string desc, bool noLog = false)
    {
        RequireEdit();
        for (var attempt = 0; ; attempt++)
        {
            SyncLinks(true);
            foreach (var op in ops)
            {
                var ids = op.Type == "move" ? new[] { op.From, op.To } : new[] { op.Slot };
                foreach (var id in ids) if (!IsLinked(PairOf(id))) throw new BusinessException($"Cần Liên kết file Excel {KN(id)} (đọc được đủ cột) mới ghi được.");
            }
            var (specs, inverse) = PlanOps(ops);
            var outs = new List<(int id, byte[] bytes, DcxXlsx.Report rep)>();
            foreach (var id in new[] { 1, 2 })
            {
                var sp = specs[id]; if (sp.Rm.Count == 0 && sp.Adds.Count == 0 && sp.Renames.Count == 0) continue;
                var p = PairOf(id);
                var (bytes, rep) = DcxXlsx.Transform(p.Bytes!, p.Kho!.Sheet, new DcxXlsx.Spec
                {
                    Removes = sp.Rm.Select(kv => new DcxXlsx.Remove(kv.Key, kv.Value)).ToList(), Adds = sp.Adds, Renames = sp.Renames, Green = sp.Move, SizeOf = SizeOf,
                });
                try { var f = DcxXlsx.AddTotalFormulas(bytes, p.Kho.Sheet); if (f.Bytes is not null) bytes = f.Bytes; } catch { /* không quan trọng */ }
                outs.Add((id, bytes, rep));
            }
            if (outs.Count == 0) throw new BusinessException("Không có gì để ghi.");
            try { Commit(outs.Select(o => (SlotOf(o.id), o.bytes, PairOf(o.id).Sha)).ToList()); }
            catch (CommitException e) when (e.Code == "CHANGED" && attempt == 0) { continue; }
            foreach (var o in outs) LoadFromLink(SlotOf(o.id));
            foreach (var p in Pairs) RefreshPair(p);
            if (!noLog) St.Log.Add(new LogEntry { Id = NewId(), At = NowTxt(), By = User, Desc = desc, Inv = inverse });
            Persist();
            var kept = outs.Sum(o => o.rep.NetKept.Count);
            _audit.Log(User, "doi-chieu-ghi-file", desc);
            return new ApplyResult("✔ " + desc + " — đã ghi vào file Excel" + (kept > 0 ? $" (⚠ {kept} dòng có Net gõ tay không khớp SL × trọng lượng nên giữ nguyên Net)" : "") + ".", kept);
        }
    }
    public ApplyResult UndoLog(string id)
    {
        var e = St.Log.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không thấy mục lịch sử này.");
        var r = ApplyOps(e.Inv.Select(o => o.Clone()).ToList(), "Hoàn tác: " + e.Desc, true);
        St.Log.Remove(e); Persist();
        return r;
    }

    // ------------------------------------------------------------------ thêm dòng: tự điền tên + trọng lượng

    public sealed record Lookup(string Ten, string SrcTen, double? Tl, string SrcTl, bool InList);
    public Lookup LookupMa(int pid, string maRaw)
    {
        var nm = NormMa(maRaw); string ten = "", srcTen = "", srcTl = ""; double? tl = null;
        foreach (var q in new[] { PairOf(pid), PairOf(OtherId(pid)) })
        {
            var r = q.Kho?.Res.Rows.FirstOrDefault(x => x.Ma == nm && x.Sl > 0); if (r is null) continue;
            if (r.Ten.Length > 0) { ten = r.Ten; srcTen = "file Excel đang liên kết"; }
            var t = R6(r.Net / r.Sl); if (t > 0) { tl = t; srcTl = "file Excel đang liên kết"; }
            break;
        }
        var inList = _m8.TryGetValue(nm, out var l);
        if (inList)
        {
            if (ten.Length == 0 && l.Ten.Length > 0) { ten = l.Ten; srcTen = "Tổng hợp › Danh sách mã hàng"; }
            if (tl is null && l.Tl is > 0) { tl = l.Tl; srcTl = "Tổng hợp › Danh sách mã hàng"; }
        }
        return new Lookup(ten, srcTen, tl, srcTl, inList);
    }
    /// <summary>Gợi ý mã khi gõ: mã có trong 2 file Kho + Danh sách mã hàng (Tổng hợp).</summary>
    public List<string> CodeCandidates()
    {
        var set = new List<string>(); var seen = new HashSet<string>();
        foreach (var p in Pairs) if (p.Kho is not null) foreach (var r in p.Kho.Res.Rows) if (seen.Add(r.Ma)) set.Add(r.Ma);
        foreach (var k in _m8.Keys) if (seen.Add(k)) set.Add(k);
        return set;
    }

    // ------------------------------------------------------------------ nạp MỘT file gốc của phần mềm kho

    public Dictionary<string, Cur> CurOf(Pair p)
    {
        var o = new Dictionary<string, Cur>();
        foreach (var r in p.Kho?.Res.Rows ?? new())
        {
            if (!(r.Sl > 0)) continue; var k = r.Ngay + "|" + r.Ma;
            if (!o.TryGetValue(k, out var x)) o[k] = x = new Cur();
            x.Sl = R6(x.Sl + r.Sl); x.Net = R6(x.Net + r.Net); if (x.Ten.Length == 0 && r.Ten.Length > 0) x.Ten = r.Ten;
        }
        return o;
    }
    public Month PairMonth(Pair p) => MonthOf(CurOf(p).Keys.Select(k => k[..k.IndexOf('|')]));
    public (Month A, Month B, bool Mixed, string Month) LinkMonths()
    {
        var a = PairMonth(P1); var b = PairMonth(P2); var mixed = a.Value.Length > 0 && b.Value.Length > 0 && a.Value != b.Value;
        return (a, b, mixed, mixed ? "" : a.Value.Length > 0 ? a.Value : b.Value);
    }
    public static string MonthsTxt(Month mo) => string.Join(", ", mo.Counts.Select(kv => MonthLabel(kv.Key) + " (" + kv.Value + " ngày)"));

    public sealed class Goc
    {
        public string FileName = ""; public int NRows; public Split Split = new(); public GocPlan Plan = new();
        public List<string> Wrong = new(); public string LinkedMonth = ""; public Month GocMonth = new();
    }
    /// <summary>Đọc file gốc, tách theo Loại chứng từ, so với TỔNG hai file đã liên kết theo (ngày, mã). Chưa ghi gì.</summary>
    public Goc ReadGoc(string path)
    {
        RequireEdit();
        if (!IsLinked(P1) || !IsLinked(P2)) throw new BusinessException("Cần liên kết file Excel của cả Xuất thay thùng và Xuất rã đông — nạp file gốc sẽ cập nhật vào hai file đó.");
        var book = DcxXlsx.ReadWorkbook(ReadShared(path));
        var name = book.Names.FirstOrDefault(n => ParseSheet(book.Aoa[n]).Ok)
            ?? throw new BusinessException("Không nhận ra cột trong file này (cần Ngày chứng từ, Mã hàng, Số lượng, Net, Loại chứng từ).");
        var res = ParseSheet(book.Aoa[name]); var sp = SplitByLoai(res.Rows);
        if (sp.Thay.Count == 0 && sp.RaDong.Count == 0)
            throw new BusinessException(res.Rows.Count > 0 && sp.KhongLoai == res.Rows.Count
                ? "File này không có cột “Loại chứng từ” (hoặc dòng tiêu đề nhóm “Xuất thay bao bì / Xuất xả”) nên app không biết dòng nào thuộc kho nào. Chưa ghi gì vào hai file Excel."
                : "Trong file không có dòng “Xuất thay bao bì” hay “Xuất xả”. Chưa ghi gì vào hai file Excel.");
        SyncLinks(true);
        var plan = PlanGoc(sp.Thay, sp.RaDong, CurOf(P1), CurOf(P2));
        var lm = LinkMonths(); var gocMo = MonthOf(plan.Days.Select(d => d.Ngay));
        if (lm.Mixed)
            throw new BusinessException($"Hai file liên kết đang khác tháng.\nXuất thay thùng: {MonthsTxt(lm.A)}\nXuất rã đông: {MonthsTxt(lm.B)}\n\nMỗi tháng dùng một cặp file riêng. Hãy liên kết lại cho cùng một tháng rồi nạp file gốc. Chưa ghi gì vào hai file Excel.");
        var guard = MonthGuard(plan, lm.Month);
        if (guard.All)
            throw new BusinessException($"Nạp nhầm file — file gốc “{Path.GetFileName(path)}” là dữ liệu {MonthLabel(gocMo.Value)} ({MonthsTxt(gocMo)}), còn hai file đã liên kết là {MonthLabel(lm.Month)}.\n\nMỗi tháng dùng một file riêng nên app không nạp để khỏi ghi nhầm. Chưa ghi gì vào hai file Excel.");
        return new Goc { FileName = Path.GetFileName(path), NRows = res.Rows.Count, Split = sp, Plan = plan, Wrong = guard.Wrong, LinkedMonth = lm.Month, GocMonth = gocMo };
    }
    public ApplyResult ApplyGoc(Goc g, bool replace)
    {
        var days = g.Plan.Days.Where(d => d.State == (replace ? "khac" : "moi")).ToList();
        if (days.Count == 0) throw new BusinessException(replace ? "Không có ngày nào khác số." : "Không có ngày mới để nạp.");
        var ops = days.SelectMany(d => replace ? d.ReplaceOps : d.AddOps).Select(o => o.Clone()).ToList();
        var desc = $"Nạp file gốc “{g.FileName}”: " + (replace ? "thay bằng số mới " : "thêm ") + days.Count + (replace ? " ngày (" : " ngày mới (") + string.Join(", ", days.Select(d => Dmy(d.Ngay))) + ")";
        return ApplyOps(ops, desc);
    }

    // ------------------------------------------------------------------ dùng cho Báo cáo rã đông

    /// <summary>Đường dẫn file "Xuất rã đông" đã liên kết ở Đối chiếu hàng ngày (của tài khoản này, trên máy này) — null nếu chưa liên kết.</summary>
    public string? LinkedPath(string slot)
    {
        var st = Loaded ? St : ReadState();
        return st.Links.TryGetValue(slot, out var l) && File.Exists(l.Path) ? l.Path : null;
    }

    // ------------------------------------------------------------------ xuất Excel kết quả

    public void Export(string path, IEnumerable<int> pairIds)
    {
        if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất Excel Đối chiếu hàng ngày.");
        var pairs = pairIds.Select(PairOf).Where(p => p.Result is not null).ToList();
        if (pairs.Count == 0) throw new BusinessException("Chưa có kết quả để xuất.");
        using var wb = new XLWorkbook();
        var fill = new Dictionary<string, XLColor> { ["lech"] = XLColor.FromHtml("#FFE699"), ["chi_kho"] = XLColor.FromHtml("#F8CBAD"), ["chi_xuong"] = XLColor.FromHtml("#BDD7EE") };
        foreach (var p in pairs)
        {
            var r = p.Result!; var tag = p.Id == 2 ? "Rã đông" : "Thay thùng";
            var ws = wb.AddWorksheet(tag + " - Theo ngày");
            var h1 = new[] { "Ngày", "Thứ", "SL Kho", "Net Kho (kg)", "SL Xưởng", "Net Xưởng (kg)", "Δ SL (Xưởng−Kho)", "Δ Net (kg)", "Số mã lệch", "Chỉ có ở Kho", "Chỉ có ở Xưởng", "Ghi 0",
                "Kho: dòng chưa mã", "Kho: SL chưa mã", "Kho: Net chưa mã (kg)", "Xưởng: dòng chưa mã", "Xưởng: SL chưa mã", "Xưởng: Net chưa mã (kg)", "Cảnh báo dòng tổng trong file" };
            Header(ws, h1);
            var row = 2;
            foreach (var d in r.Days)
            {
                object?[] v = { Dmy(d.Ngay), Weekday(d.Ngay), d.Kho.Sl, d.Kho.Net, d.Xuong.Sl, d.Xuong.Net, d.DSl, d.DNet, d.NLech, d.NChiKho, d.NChiXuong, d.NZero,
                    d.NcKho?.N, d.NcKho?.Sl, d.NcKho?.Net, d.NcXuong?.N, d.NcXuong?.Sl, d.NcXuong?.Net,
                    string.Join(" ; ", new[] { ("Kho", d.CheckKho), ("Xưởng", d.CheckXuong) }.Where(x => x.Item2 is { Khop: false }).Select(x => $"File {x.Item1} ghi tổng {x.Item2!.Sl:0.###} / {x.Item2.Net:0.###} kg, cộng chi tiết {x.Item2.SumSl:0.###} / {x.Item2.SumNet:0.###} kg")) };
                Put(ws, row++, v);
            }
            Finish(ws, new[] { 3, 5, 7, 9, 10, 11, 12, 13, 14, 16, 17 }, new[] { 4, 6, 8, 15, 18 });
            var cols = new[] { "Ngày", "Mã hàng", "Tên hàng (Kho)", "Loại size / Ghi chú (Xưởng)", "SL Kho", "SL Xưởng", "Δ SL", "Net Kho (kg)", "Net Xưởng (kg)", "Δ Net (kg)", "Trạng thái", "Dòng Kho", "Dòng Xưởng", "Gợi ý (chưa xác nhận)" };
            foreach (var (nm, list) in new[] { (" - Chênh lệch", r.Items.Where(i => i.Status is "lech" or "chi_kho" or "chi_xuong").ToList()), (" - Tất cả", r.Items) })
            {
                var w2 = wb.AddWorksheet(tag + nm); Header(w2, cols); var rr = 2;
                foreach (var it in list)
                {
                    static string F0(double d) => d.ToString("#,0.###", CultureInfo.InvariantCulture);
                    var goiY = new List<string>(NetFixNotes(it, F0, F0));
                    if (it.TheoSize) goiY.Add("Đối chiếu nhanh theo Size + trọng lượng");
                    foreach (var g in it.GoiY ?? new()) goiY.Add("Cùng ngày, SL & Net với mã " + g.Ma + (g.Khac is { Count: > 0 } ? " (khác ký tự " + string.Join(",", g.Khac) + ")" : ""));
                    foreach (var g in it.GoiYNhom ?? new()) goiY.Add("Bằng tổng: " + string.Join(" + ", g.Select(x => $"{x.Ma} ({F0(x.Sl ?? 0)})")));
                    Put(w2, rr, new object?[] { Dmy(it.Ngay), it.Ma, it.Ten, string.Join(" / ", new[] { it.Size, it.Ghichu }.Where(s => s.Length > 0)), it.KSl, it.XSl, it.DSl, it.KNet, it.XNet, it.DNet,
                        StatusText[it.Status], string.Join(", ", it.KRows), string.Join(", ", it.XRows), string.Join(" ; ", goiY) });
                    if (fill.TryGetValue(it.Status, out var c)) w2.Range(rr, 1, rr, cols.Length).Style.Fill.BackgroundColor = c;
                    rr++;
                }
                Finish(w2, new[] { 5, 6, 7 }, new[] { 8, 9, 10 });
                w2.Column(2).Width = 26; w2.Column(3).Width = 44; w2.Column(14).Width = 60;
            }
        }
        var ed = St.Edits.Where(e => pairs.Any(p => p.Id == e.Pair)).OrderBy(e => e.Pair).ThenBy(e => e.Ngay).ThenBy(e => e.Ma).ToList();
        if (ed.Count > 0)
        {
            var ws = wb.AddWorksheet("Số liệu đã sửa tay");
            Header(ws, new[] { "Thời gian sửa", "File Xuất", "Ngày", "Mã hàng", "Tên hàng", "Số liệu đã sửa", "Giá trị gốc", "Giá trị sau khi sửa", "Người sửa" });
            var row = 2;
            foreach (var e in ed) Put(ws, row++, new object?[] { e.At.Replace('T', ' '), KN(e.Pair), Dmy(e.Ngay), e.Ma, e.Ten, (e.Field == "sl" ? "SL " : "Net ") + (e.Side == "kho" ? "Kho" : "Xưởng"), e.Before, e.After, e.By });
            Finish(ws, Array.Empty<int>(), new[] { 7, 8 });
        }
        wb.SaveAs(path);
    }
    private static void Header(IXLWorksheet ws, string[] h)
    {
        for (var i = 0; i < h.Length; i++) ws.Cell(1, i + 1).Value = h[i];
        var r = ws.Range(1, 1, 1, h.Length); r.Style.Font.Bold = true; r.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7"); r.Style.Alignment.WrapText = true;
        ws.SheetView.FreezeRows(1);
    }
    private static void Put(IXLWorksheet ws, int row, object?[] v)
    {
        for (var i = 0; i < v.Length; i++)
        {
            var c = ws.Cell(row, i + 1);
            switch (v[i]) { case null: break; case double d: c.Value = Math.Round(d, 6); break; case int n: c.Value = n; break; default: c.Value = Convert.ToString(v[i], CultureInfo.InvariantCulture); break; }
        }
    }
    private static void Finish(IXLWorksheet ws, int[] intCols, int[] kgCols)
    {
        foreach (var c in intCols) ws.Column(c).Style.NumberFormat.Format = "#,##0";
        foreach (var c in kgCols) ws.Column(c).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents(8, 40);
        var used = ws.RangeUsed(); if (used is not null) { used.Style.Border.InsideBorder = XLBorderStyleValues.Thin; used.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; used.SetAutoFilter(); }
    }
    public static string Weekday(string ngay)
    {
        var m = Regex.Match(ngay ?? "", @"^(\d{4})-(\d{2})-(\d{2})$"); if (!m.Success) return "";
        var d = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        return d.DayOfWeek == DayOfWeek.Sunday ? "Chủ nhật" : "Thứ " + ((int)d.DayOfWeek + 1);
    }
}

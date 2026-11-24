using System.Text.Json.Nodes;
using Dapper;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.Lan;

/// <summary>Phần của LanService dùng cho đồng bộ qua thư mục Google Drive (DriveSyncService): máy chính áp thay đổi từ máy xem từ xa, máy xem từ xa giữ sổ chờ.</summary>
public sealed partial class LanService
{
    /// <summary>Dòng trạng thái đồng bộ Drive (máy xem từ xa) cho thanh menu.</summary>
    public string RemoteStatus { get; set; } = "";
    public void RaiseChanged() => Raise();

    /// <summary>Máy giữ dữ liệu chính: máy đơn hoặc máy chủ LAN đang phục vụ.</summary>
    public bool HoldsMainData => Role != RoleRemote && (!Enabled || Serving);

    /// <summary>Áp thay đổi (từ file máy xem từ xa) vào dữ liệu chính — cùng logic gộp 3 bên / xung đột như máy con LAN.</summary>
    public List<JsonObject> ApplyExternal(IEnumerable<JsonObject> items, string machine)
    {
        if (!HoldsMainData) throw new BusinessException("Máy này không giữ dữ liệu chính nên không nhận thay đổi từ máy xem từ xa.");
        var res = new List<JsonObject>();
        foreach (var it in items) res.Add(ApplySyncItem(it, it["u"]?.ToString() ?? "", machine, false));
        if (res.Count > 0) Log("Nhận thay đổi từ Drive", $"{machine}: {res.Count} bảng");
        return res;
    }

    /// <summary>Sổ chờ của máy xem từ xa, dạng gửi lên máy chính.</summary>
    public List<JsonObject> PendingItems()
    {
        var rows = PendingRows();
        MarkSent(rows.Select(p => p.op_id));
        return rows.Select(p => ItemOf(p)).ToList();
    }

    /// <summary>Bỏ các mục sổ chờ máy chính đã nhận (sau đó bảng được thay bằng bản của máy chính).</summary>
    public void Acknowledge(IEnumerable<string> opIds)
    {
        using var c = _db.Open();
        foreach (var op in opIds) c.Execute("DELETE FROM lan_pending WHERE op_id=@op", new { op });
        Raise();
    }

    public bool HasPendingSheet(string db, string sheet) => HasPending(db, sheet);

    /// <summary>Phiên bản từng bảng đang có trên máy này.</summary>
    public Dictionary<(string Db, string Sheet), long> LocalVersions()
    {
        using var c = _db.Open();
        return c.Query<(string db_key, string sheet, long version)>("SELECT db_key, sheet, version FROM module_sheets").ToDictionary(x => (x.db_key, x.sheet), x => x.version);
    }

    /// <summary>Toàn bộ bảng dữ liệu module (để chụp gửi lên Drive).</summary>
    public List<(string Db, string Sheet, long Version, string Data)> ExportAll()
    {
        lock (_dataLock)
        {
            using var c = _db.Open();
            return c.Query<(string db_key, string sheet, long version, string data)>("SELECT db_key, sheet, version, data FROM module_sheets ORDER BY db_key, sheet")
                .Select(x => (x.db_key, x.sheet, x.version, x.data)).ToList();
        }
    }

    public long DataSeq() => _store.CurrentSeq();

    /// <summary>Đặt bảng đúng bản của máy chính cũ, bỏ qua sổ chờ (dùng khi máy này nhận làm máy chính — thay đổi chờ gửi sẽ gộp lên trên ngay sau đó).</summary>
    public void ForceInstallSheet(string db, string sheet, string data, long version)
    {
        lock (_dataLock) _store.SetExact(db, sheet, data == "[]" && version == 0 ? null : data, version);
    }

    /// <summary>Đặt bảng đúng bản của máy chính (bỏ qua nếu bảng đang có thay đổi chờ gửi).</summary>
    public bool InstallSheet(string db, string sheet, string data, long version)
    {
        lock (_dataLock)
        {
            if (HasPending(db, sheet)) return false;
            _store.SetExact(db, sheet, data == "[]" && version == 0 ? null : data, version);
            return true;
        }
    }
}

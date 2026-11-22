using System.Text.Json.Nodes;

namespace KhoLanh.Core.Modules;

/// <summary>Nơi module đọc / ghi bảng: máy đơn (SQLite trong máy) hoặc mạng LAN (máy chủ / bản sao + sổ chờ).</summary>
public interface ISheetSource
{
    string Read(string dbKey, string sheet);
    List<string> List(string dbKey);
    void Write(string dbKey, IDictionary<string, JsonArray?> sheets);
}

/// <summary>Máy đơn: đọc ghi thẳng SQLite trong máy.</summary>
public class LocalSheetSource : ISheetSource
{
    private readonly ModuleStore _store;
    public LocalSheetSource(ModuleStore store) { _store = store; }
    public string Read(string dbKey, string sheet) => _store.ReadSheetJson(dbKey, sheet);
    public List<string> List(string dbKey) => _store.ListSheets(dbKey);
    public void Write(string dbKey, IDictionary<string, JsonArray?> sheets) => _store.WriteSheets(dbKey, sheets);
}

using System.Text.Json.Nodes;
using KhoLanh.Core.Bang;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Lõi bảng (làm sạch / gộp / tách / so sánh / tra cứu) ra đúng như cc-bang-engine.js — Data/cc-golden.json do Data/cc-golden.gen.js tạo.</summary>
public class CcBangTests
{
    private static readonly JsonObject G = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "cc-golden.json")))!;
    private static object? V(JsonNode? n) => n is JsonValue v ? (v.TryGetValue<double>(out var d) ? d : v.GetValue<string>()) : null;
    private static CcTable T(string k) => new(G[k]!["head"]!.AsArray().Select(x => x!.GetValue<string>()), G[k]!["rows"]!.AsArray().Select(r => r!.AsArray().Select(V).ToArray()));
    private static string Cell(object? v) => v switch { null => "∅", double d => "n" + d.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => "s" + v };
    private static List<string> Rows(JsonArray a) => a.Select(r => string.Join("|", r!.AsArray().Select(c => Cell(V(c))))).ToList();
    private static List<string> Rows(IEnumerable<object?[]> a) => a.Select(r => string.Join("|", r.Select(Cell))).ToList();

    [Fact]
    public void Clean_matches_js()
    {
        var o = new CcBang.CleanOpts { Dedupe = new() { 0 }, Cols = { [1] = new() { Title = true }, [2] = new() { Num = true }, [3] = new() { Date = true }, [4] = new() { Upper = true } } };
        var r = CcBang.CleanTable(T("A"), o); var e = G["clean"]!;
        Assert.Equal(Rows(e["rows"]!.AsArray()), Rows(r.Table.Rows));
        Assert.Equal(e["srcRows"]!.AsArray().Select(x => x!.GetValue<int>()), r.SrcRows);
        Assert.Equal(e["removed"]!["blankRows"]!.GetValue<int>(), r.BlankRows); Assert.Equal(e["removed"]!["dups"]!.GetValue<int>(), r.Dups);
        Assert.Equal(e["log"]!.AsArray().Select(l => $"{l!["r"]}:{l["c"]}:{l["from"]}>{l["to"]}:{l["why"]}"), r.Log.Select(l => $"{l.R}:{l.C}:{l.From}>{l.To}:{l.Why}"));
        Assert.Equal(e["issues"]!.AsArray().Select(l => $"{l!["r"]}:{l["c"]}:{l["why"]}"), r.Issues.Select(l => $"{l.R}:{l.C}:{l.Why}"));
        var r2 = CcBang.CleanTable(T("A"), new CcBang.CleanOpts { Trim = false, BlankRows = false });
        Assert.Equal(Rows(G["clean2"]!["rows"]!.AsArray()), Rows(r2.Table.Rows));
    }

    [Fact]
    public void Merge_split_pages_match_js()
    {
        var (t, notes) = CcBang.MergeStack(new[] { ("f1.xlsx", T("A")), ("f2.xlsx", T("B")) });
        Assert.Equal(G["merge"]!["head"]!.AsArray().Select(x => x!.GetValue<string>()), t.Head);
        Assert.Equal(Rows(G["merge"]!["rows"]!.AsArray()), Rows(t.Rows));
        Assert.Equal(G["merge"]!["notes"]!.AsArray().Select(x => x!.GetValue<string>()), notes);
        Assert.Equal(G["split"]!.AsArray().Select(x => $"{x!["key"]}/{x["name"]}/{x["n"]}"), CcBang.SplitByColumn(T("A"), 4).Select(x => $"{x.Key}/{x.Name}/{x.Tbl.Rows.Count}"));
        Assert.Equal(G["splitR"]!.AsArray().Select(x => $"{x!["name"]}/{x["n"]}"), CcBang.SplitByRows(T("A"), 3).Select(x => $"{x.Name}/{x.Tbl.Rows.Count}"));
        var texts = new[] { "1-3,5,8-", "", "2-1", "0", "x", "7-" };
        var pg = G["pages"]!.AsArray();
        for (var i = 0; i < texts.Length; i++)
        {
            var (p, err) = CcBang.ParsePages(texts[i], 9);
            Assert.Equal(pg[i]!["pages"]!.AsArray().Select(x => x!.GetValue<int>()), p); Assert.Equal(pg[i]!["error"]!.GetValue<string>(), err);
        }
    }

    [Fact]
    public void Compare_lookup_match_js()
    {
        var o = new CcBang.MatchOpts { IgnoreDiacritics = true, Tol = 0.05 };
        var c = CcBang.CompareTables(T("A"), T("B"), new[] { 0 }, new[] { 0 }, new[] { new CcBang.Pair(1, 1, "Tên"), new CcBang.Pair(2, 2, "SL") }, o);
        var e = G["cmp"]!;
        Assert.Equal(e["onlyA"]!.AsArray().Select(x => x!.GetValue<int>()), c.OnlyA); Assert.Equal(e["onlyB"]!.AsArray().Select(x => x!.GetValue<int>()), c.OnlyB);
        Assert.Equal(e["diff"]!.AsArray().Select(d => d!["key"] + ":" + string.Join(",", d["cells"]!.AsArray().Select(x => $"{x!["name"]}={x["a"]}/{x["b"]}"))), c.Diff.Select(d => d.Key + ":" + string.Join(",", d.Cells.Select(x => $"{x.Name}={x.A}/{x.B}"))));
        Assert.Equal(e["same"]!.AsArray().Count, c.Same.Count);
        Assert.Equal(e["dupA"]!.AsArray().Select(d => d!["key"]!.GetValue<string>()), c.DupA.Select(d => d.Key)); Assert.Equal(e["dupB"]!.AsArray().Select(d => d!["key"]!.GetValue<string>()), c.DupB.Select(d => d.Key));
        Assert.Equal(e["emptyKeyA"]!.AsArray().Select(x => x!.GetValue<int>()), c.EmptyKeyA); Assert.Equal(e["emptyKeyB"]!.AsArray().Select(x => x!.GetValue<int>()), c.EmptyKeyB);
        var modes = new[] { "first", "last", "sum", "join", "count" };
        for (var i = 0; i < modes.Length; i++)
        {
            var l = CcBang.LookupTables(T("A"), T("B"), new[] { 0 }, new[] { 0 }, new[] { 1, 3 }, modes[i], new CcBang.MatchOpts());
            var x = G["lk"]![i]!;
            Assert.Equal(x["head"]!.AsArray().Select(h => h!.GetValue<string>()), l.Table.Head);
            Assert.Equal(Rows(x["rows"]!.AsArray()), Rows(l.Table.Rows));
            Assert.Equal(x["found"]!.GetValue<int>(), l.Found); Assert.Equal(x["notFound"]!.GetValue<int>(), l.NotFound); Assert.Equal(x["multi"]!.GetValue<int>(), l.Multi); Assert.Equal(x["emptyKey"]!.GetValue<int>(), l.EmptyKey);
        }
    }
}

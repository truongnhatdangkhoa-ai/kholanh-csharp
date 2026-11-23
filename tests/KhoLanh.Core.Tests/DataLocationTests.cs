using KhoLanh.Core.Data;
using Xunit;

namespace KhoLanh.Core.Tests;

public class DataLocationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-loc-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void First_run_asks_then_remembers_choice()
    {
        if (DataLocation.EnvFolder is not null) return;
        var def = Path.Combine(_dir, "macdinh"); var other = Path.Combine(_dir, "D", "KhoLanhData");
        var loc = new DataLocation(def);
        Assert.True(loc.NeedsChoice);
        Assert.Equal(def, loc.Resolve());
        Assert.Equal(Path.GetFullPath(other), loc.Save(other));
        Assert.True(Directory.Exists(other));
        Assert.False(loc.NeedsChoice);
        Assert.Equal(Path.GetFullPath(other), new DataLocation(def).Resolve());
    }

    [Fact]
    public void Existing_install_in_default_folder_is_not_asked()
    {
        if (DataLocation.EnvFolder is not null) return;
        var def = Path.Combine(_dir, "macdinh"); Directory.CreateDirectory(def);
        File.WriteAllText(Path.Combine(def, DataLocation.DbFileName), "");
        var loc = new DataLocation(def);
        Assert.False(loc.NeedsChoice);
        Assert.Equal(def, loc.Resolve());
        Assert.True(DataLocation.Same(def + Path.DirectorySeparatorChar, def));
    }
}

using HousecarlCore;
using Xunit;

namespace HousecarlMcpTests;

[Trait("tier", "unit")]
public sealed class LinuxPathTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "hc-linuxpath-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void ALogicalPathResolvesCaseInsensitivelyAgainstTheDisk()
    {
        Directory.CreateDirectory(Path.Combine(_root, "meshes"));
        File.WriteAllText(Path.Combine(_root, "meshes", "foo.nif"), "x");

        Assert.True(File.Exists(DiskPath.Join(_root, @"Meshes\Foo.NIF")));
    }

    [Fact]
    public void AListingIsInNameOrderWhateverTheDiskReturns()
    {
        Directory.CreateDirectory(_root);
        foreach (var n in new[] { "c.nif", "A.nif", "b.nif" }) File.WriteAllText(Path.Combine(_root, n), "x");

        Assert.Equal(new[] { "A.nif", "b.nif", "c.nif" }, LooseListings.List(_root).Names.ToArray());
    }

    [Fact]
    public void ArchiveGlobsMatchAnyCaseOfTheExtension()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "X.BSA"), "x");

        Assert.Single(Directory.EnumerateFiles(_root, "*.bsa", DiskPath.IgnoreCase));
    }
}

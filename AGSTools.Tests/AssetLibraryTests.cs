using System.Text;

namespace AGSTools.Tests;

public class AssetLibraryTests
{
    /// <summary>Builds a single-part CLIB version 30 library containing the given assets.</summary>
    internal static byte[] BuildV30Library(params (string Name, byte[] Data)[] assets)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Latin1);
        w.Write(Encoding.ASCII.GetBytes("CLIB\x1a"));
        w.Write((byte)30);
        w.Write((byte)0); // library index
        w.Write(0); // reserved
        w.Write(1);
        w.Write(Encoding.ASCII.GetBytes("game.ags\0"));
        w.Write(assets.Length);

        long tableStart = ms.Position;
        int tableSize = assets.Sum(a => a.Name.Length + 1 + 1 + 8 + 8);
        long offset = tableStart + tableSize;
        foreach (var (name, data) in assets)
        {
            w.Write(Encoding.ASCII.GetBytes(name + "\0"));
            w.Write((byte)0);
            w.Write(offset);
            w.Write((long)data.Length);
            offset += data.Length;
        }
        foreach (var (_, data) in assets)
            w.Write(data);
        return ms.ToArray();
    }

    /// <summary>Appends a library to an engine exe, with the tail signature pointing to its start.</summary>
    private static byte[] AppendToExe(byte[] library, int exeSize)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new byte[exeSize]);
        w.Write(library);
        w.Write((long)exeSize);
        w.Write(Encoding.ASCII.GetBytes("CLIB\x01\x02\x03\x04SIGE"));
        return ms.ToArray();
    }

    [Fact]
    public void Reads_standalone_v30_library()
    {
        using var tmp = new TempDir();
        string path = tmp.File("game.ags");
        File.WriteAllBytes(path, BuildV30Library(("game28.dta", new byte[] { 1, 2, 3 }), ("room1.crm", new byte[] { 4, 5 })));

        AssetLibrary lib = AssetLibrary.TryOpen(path)!;

        Assert.Equal(30, lib.Version);
        Assert.True(lib.TryGetAsset("ROOM1.CRM", out AssetInfo room)); // names are case-insensitive
        Assert.Equal(new byte[] { 4, 5 }, lib.ReadAsset(room));
    }

    [Fact]
    public void Asset_offsets_in_exe_are_relative_to_library_start()
    {
        using var tmp = new TempDir();
        string path = tmp.File("game.exe");
        File.WriteAllBytes(path, AppendToExe(BuildV30Library(("game28.dta", new byte[] { 9, 8, 7 })), exeSize: 5000));

        AssetLibrary lib = AssetLibrary.TryOpen(path)!;

        Assert.True(lib.TryGetAsset("game28.dta", out AssetInfo asset));
        Assert.Equal(new byte[] { 9, 8, 7 }, lib.ReadAsset(asset));
    }

    [Fact]
    public void Returns_null_for_other_files()
    {
        using var tmp = new TempDir();
        string path = tmp.File("other.bin");
        File.WriteAllBytes(path, new byte[1000]);

        Assert.Null(AssetLibrary.TryOpen(path));
    }

    [Fact]
    public void Truncated_library_throws_format_exception()
    {
        using var tmp = new TempDir();
        string path = tmp.File("broken.ags");
        byte[] lib = BuildV30Library(("game28.dta", new byte[100]));
        File.WriteAllBytes(path, lib[..20]);

        Assert.Throws<AgsFormatException>(() => AssetLibrary.TryOpen(path));
    }
}

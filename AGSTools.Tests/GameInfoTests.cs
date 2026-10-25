using System.Text;

namespace AGSTools.Tests;

public class GameInfoTests
{
    /// <summary>
    /// Builds a minimal file containing an AGS game data header at <paramref name="sigOffset"/>.
    /// </summary>
    private static byte[] BuildGameFile(int sigOffset, string version, string title, byte[] uid)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(new byte[sigOffset]);
        bw.Write(Encoding.Latin1.GetBytes("Adventure Creator Game File v2"));
        bw.Write(0);                       // data version (ignored)
        bw.Write(version.Length);
        bw.Write(Encoding.Latin1.GetBytes(version));
        if (version[0] >= '3')
            bw.Write(0);                   // extra int32 in v3.x

        long titlePos = ms.Position;
        byte[] titleBuf = new byte[0x40];
        Encoding.Latin1.GetBytes(title).CopyTo(titleBuf, 0);
        bw.Write(titleBuf);

        ms.Position = titlePos + 0x6F4;
        bw.Write(uid);
        bw.Write(new byte[64]);            // trailing data
        return ms.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(1024 - 10)]                // signature spans the old 1 KB block boundary
    [InlineData(64 * 1024 - 15)]           // signature spans a 64 KB read boundary
    [InlineData(200_000)]
    public void Finds_header_at_any_offset(int sigOffset)
    {
        using var tmp = new TempDir();
        string game = tmp.File("game.exe");
        File.WriteAllBytes(game, BuildGameFile(sigOffset, "3.6.0.50", "Blackwell Test", new byte[] { 0x12, 0x34, 0x56, 0x78 }));

        GameInfo? info = GameInfo.GetGameInfo(game);

        Assert.NotNull(info);
        Assert.Equal("3.6.0.50", info.Version);
        Assert.Equal("Blackwell Test", info.GameTitle);
        Assert.Equal("12345678", info.GameUID);
    }

    [Fact]
    public void Reads_v2_header_without_extra_field()
    {
        using var tmp = new TempDir();
        string game = tmp.File("ac2game.dta");
        File.WriteAllBytes(game, BuildGameFile(50, "2.72", "Old Game", new byte[] { 0x0A, 0x0B, 0x0C, 0x0D }));

        GameInfo? info = GameInfo.GetGameInfo(game);

        Assert.NotNull(info);
        Assert.Equal("Old Game", info.GameTitle);
        Assert.Equal("A0B0C0D", info.GameUID);
    }

    [Fact]
    public void Opens_read_only_files()
    {
        using var tmp = new TempDir();
        string game = tmp.File("game.ags");
        File.WriteAllBytes(game, BuildGameFile(10, "3.5.1.0", "Read Only", new byte[] { 1, 2, 3, 4 }));
        File.SetAttributes(game, FileAttributes.ReadOnly);

        Assert.Equal("Read Only", GameInfo.GetGameInfo(game)?.GameTitle);
    }

    [Fact]
    public void Returns_null_without_header()
    {
        using var tmp = new TempDir();
        string file = tmp.File("random.bin");
        File.WriteAllBytes(file, new byte[5000]);

        Assert.Null(GameInfo.GetGameInfo(file));
    }

    [Fact]
    public void Game_uid_survives_tra_export()
    {
        using var tmp = new TempDir();
        string game = tmp.File("game.exe");
        byte[] uid = { 0xDE, 0xAD, 0x00, 0x42 };
        File.WriteAllBytes(game, BuildGameFile(300, "3.6.0.50", "Uid Game", uid));
        string tra = tmp.File("out.tra");

        Translation.CreateTRA_File(GameInfo.GetGameInfo(game)!, tra, new Dictionary<string, string> { ["a b"] = "c d" });

        Assert.Equal(uid, File.ReadAllBytes(tra).AsSpan(23, 4).ToArray());
    }
}

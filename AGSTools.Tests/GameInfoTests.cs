namespace AGSTools.Tests;

public class GameInfoTests
{
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
        var builder = new GameDataBuilder { Title = "Blackwell Test", UniqueIdBytes = new byte[] { 0x12, 0x34, 0x56, 0x78 } };
        File.WriteAllBytes(game, GameDataBuilder.Embed(builder.Build(), sigOffset));

        GameInfo? info = GameInfo.GetGameInfo(game);

        Assert.NotNull(info);
        Assert.Equal("3.6.0.50", info.Version);
        Assert.Equal("Blackwell Test", info.GameTitle);
        Assert.Equal(0x78563412, info.UniqueId);
        Assert.Equal("12345678", info.GameUID); // legacy format: bytes in file order
    }

    [Fact]
    public void Reads_v2_header()
    {
        using var tmp = new TempDir();
        string game = tmp.File("ac2game.dta");
        var builder = new GameDataBuilder { DataVersion = 32, EngineVersion = "2.72", Title = "Old Game", UniqueIdBytes = new byte[] { 0x0A, 0x0B, 0x0C, 0x0D } };
        File.WriteAllBytes(game, builder.Build());

        GameInfo? info = GameInfo.GetGameInfo(game);

        Assert.NotNull(info);
        Assert.Equal("Old Game", info.GameTitle);
        Assert.Equal("A0B0C0D", info.GameUID);
    }

    [Fact]
    public void Full_title_comes_from_extension()
    {
        using var tmp = new TempDir();
        string game = tmp.File("game28.dta");
        string longTitle = new string('x', 80);
        var builder = new GameDataBuilder { Title = longTitle[..49], ObjectNames = (longTitle, Array.Empty<string>(), new[] { "" }) };
        File.WriteAllBytes(game, builder.Build());

        Assert.Equal(longTitle, GameInfo.GetGameInfo(game)?.GameTitle);
    }

    [Fact]
    public void Reports_text_codepage()
    {
        using var tmp = new TempDir();
        string game = tmp.File("game28.dta");
        File.WriteAllBytes(game, new GameDataBuilder { Codepage = 65001, Title = "Ünïcode" }.Build());

        GameInfo info = GameInfo.GetGameInfo(game)!;

        Assert.True(info.IsUnicode);
        Assert.Equal("Ünïcode", info.GameTitle);
        Assert.Equal("UTF-8", info.ToTranslationOptions().Encoding);
    }

    [Fact]
    public void Opens_read_only_files()
    {
        using var tmp = new TempDir();
        string game = tmp.File("game.ags");
        File.WriteAllBytes(game, GameDataBuilder.Embed(new GameDataBuilder { Title = "Read Only" }.Build(), 10));
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
    public void Engine_exe_without_data_uses_ags_file_next_to_it()
    {
        using var tmp = new TempDir();
        string exe = tmp.File("Game.exe");
        File.WriteAllBytes(exe, new byte[4096]);
        File.WriteAllBytes(tmp.File("game.ags"), AssetLibraryTests.BuildV30Library(
            ("game28.dta", new GameDataBuilder { Title = "Steam Build" }.Build())));

        GameInfo? info = GameInfo.GetGameInfo(exe);

        Assert.NotNull(info);
        Assert.Equal("Steam Build", info.GameTitle);
        Assert.Equal(tmp.File("game.ags"), info.DataPath, ignoreCase: true);
    }

    [Fact]
    public void Game_uid_survives_tra_export()
    {
        using var tmp = new TempDir();
        string game = tmp.File("game.exe");
        byte[] uid = { 0xDE, 0xAD, 0x00, 0x42 };
        File.WriteAllBytes(game, GameDataBuilder.Embed(new GameDataBuilder { UniqueIdBytes = uid }.Build(), 300));
        string tra = tmp.File("out.tra");

        Translation.CreateTRA_File(GameInfo.GetGameInfo(game)!, tra, new Dictionary<string, string> { ["a b"] = "c d" });

        Assert.Equal(uid, File.ReadAllBytes(tra).AsSpan(23, 4).ToArray());
    }
}

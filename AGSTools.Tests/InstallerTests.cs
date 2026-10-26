using System.IO.Compression;

namespace AGSTools.Tests;

public class InstallerTests
{
    // Game folder with game28.dta, a matching Deutsch.tra and a font in a separate work folder
    private sealed class Setup : IDisposable
    {
        private readonly TempDir _root = new();

        public Setup()
        {
            GameFolder = Directory.CreateDirectory(_root.File("game")).FullName;
            WorkFolder = Directory.CreateDirectory(_root.File("work")).FullName;
            UserConfigRoot = Directory.CreateDirectory(_root.File("userconf")).FullName;
            Game = Path.Combine(GameFolder, "game28.dta");
            File.WriteAllBytes(Game, new GameDataBuilder { SaveFolder = "Test Game" }.Build());
            GameInfo info = GameInfo.GetGameInfo(Game)!;

            var doc = new TranslationDocument();
            doc.Entries["Hello"] = "Hallo";
            doc.Options.GameUid = info.UniqueId;
            doc.Options.GameName = info.GameTitle;
            Tra = Path.Combine(WorkFolder, "Deutsch.tra");
            TraFile.Write(Tra, doc);
            Font = Path.Combine(WorkFolder, "agsfnt1.wfn");
            File.WriteAllText(Font, "new font");
        }

        public string GameFolder { get; }
        public string WorkFolder { get; }
        public string UserConfigRoot { get; }
        public string Game { get; }
        public string Tra { get; }
        public string Font { get; }

        public string InGame(string name) => Path.Combine(GameFolder, name);

        public void Dispose() => _root.Dispose();
    }

    [Fact]
    public void Install_copies_files_backs_up_replaced_ones_and_selects_the_language()
    {
        using var s = new Setup();
        File.WriteAllText(s.InGame("agsfnt1.wfn"), "original font");
        File.WriteAllText(s.InGame("acsetup.cfg"), "[graphics]\r\nwindowed=1\r\n[language]\r\ntranslation=\r\n");

        InstallResult result = TranslationInstaller.Install(s.Game, s.Tra, new[] { s.Font }, userConfigFolders: new[] { s.UserConfigRoot });

        Assert.Equal("Deutsch", result.Language);
        Assert.Equal(new[] { "Deutsch.tra" }, result.Added);
        Assert.Equal(new[] { "agsfnt1.wfn" }, result.Replaced);
        Assert.Equal("new font", File.ReadAllText(s.InGame("agsfnt1.wfn")));
        Assert.Equal("original font", File.ReadAllText(Path.Combine(result.BackupFolder, "agsfnt1.wfn")));
        Assert.Equal("[graphics]\r\nwindowed=1\r\n[language]\r\ntranslation=Deutsch\r\n", File.ReadAllText(s.InGame("acsetup.cfg")));
    }

    [Fact]
    public void Uninstall_restores_the_game_without_deleting_anything()
    {
        using var s = new Setup();
        File.WriteAllText(s.InGame("agsfnt1.wfn"), "original font");
        File.WriteAllText(s.InGame("acsetup.cfg"), "[sound]\nenabled=1\n");

        InstallResult install = TranslationInstaller.Install(s.Game, s.Tra, new[] { s.Font }, userConfigFolders: new[] { s.UserConfigRoot });
        Assert.Contains("[language]\ntranslation=Deutsch", File.ReadAllText(s.InGame("acsetup.cfg")));
        UninstallResult result = TranslationInstaller.Uninstall(s.Game);

        Assert.False(File.Exists(s.InGame("Deutsch.tra")));
        Assert.True(File.Exists(Path.Combine(install.BackupFolder, "removed", "Deutsch.tra")));
        Assert.Equal("original font", File.ReadAllText(s.InGame("agsfnt1.wfn")));
        Assert.Equal("[sound]\nenabled=1\n", File.ReadAllText(s.InGame("acsetup.cfg")));
        Assert.Empty(TranslationInstaller.FindBackups(s.GameFolder));
        Assert.Throws<IOException>(() => TranslationInstaller.Uninstall(s.Game));
    }

    [Fact]
    public void Install_also_changes_an_existing_per_user_config()
    {
        using var s = new Setup();
        string userConfig = Path.Combine(Directory.CreateDirectory(Path.Combine(s.UserConfigRoot, "Test Game")).FullName, "acsetup.cfg");
        File.WriteAllText(userConfig, "[language]\ntranslation=English\n[mouse]\nspeed=1.0\n");

        InstallResult result = TranslationInstaller.Install(s.Game, s.Tra, userConfigFolders: new[] { s.UserConfigRoot });
        Assert.Equal(new[] { s.InGame("acsetup.cfg"), userConfig }, result.ConfigFiles);
        Assert.Equal("[language]\ntranslation=Deutsch\n[mouse]\nspeed=1.0\n", File.ReadAllText(userConfig));
        Assert.Equal("[language]\ntranslation=Deutsch\n", File.ReadAllText(s.InGame("acsetup.cfg"))); // created

        TranslationInstaller.Uninstall(s.Game);
        Assert.Equal("[language]\ntranslation=English\n[mouse]\nspeed=1.0\n", File.ReadAllText(userConfig));
        Assert.False(File.Exists(s.InGame("acsetup.cfg")));
    }

    [Fact]
    public void Install_refuses_a_tra_for_another_game_unless_forced()
    {
        using var s = new Setup();
        var other = new TranslationDocument();
        other.Options.GameUid = 42;
        other.Options.GameName = "Other Game";
        string tra = Path.Combine(s.WorkFolder, "Other.tra");
        TraFile.Write(tra, other);

        var ex = Assert.Throws<IOException>(() => TranslationInstaller.Install(s.Game, tra, userConfigFolders: Array.Empty<string>()));
        Assert.Contains("\"Other Game\"", ex.Message);
        Assert.False(File.Exists(s.InGame("Other.tra")));
        Assert.Empty(TranslationInstaller.FindBackups(s.GameFolder));

        TranslationInstaller.Install(s.Game, tra, force: true, userConfigFolders: Array.Empty<string>());
        Assert.True(File.Exists(s.InGame("Other.tra")));
    }

    [Fact]
    public void Install_refuses_bad_font_names_and_links()
    {
        using var s = new Setup();
        string badFont = Path.Combine(s.WorkFolder, "myfont.ttf");
        File.WriteAllText(badFont, "x");
        Assert.Throws<IOException>(() => TranslationInstaller.Install(s.Game, s.Tra, new[] { badFont }, userConfigFolders: Array.Empty<string>()));

        if (!OperatingSystem.IsWindows())
        {
            string elsewhere = Path.Combine(s.WorkFolder, "elsewhere.tra");
            File.WriteAllText(elsewhere, "keep");
            File.CreateSymbolicLink(s.InGame("Deutsch.tra"), elsewhere);
            Assert.Throws<IOException>(() => TranslationInstaller.Install(s.Game, s.Tra, userConfigFolders: Array.Empty<string>()));
            Assert.Equal("keep", File.ReadAllText(elsewhere));
        }
    }

    [Fact]
    public void Player_package_contains_the_files_and_scripts()
    {
        using var s = new Setup();
        string zipPath = Path.Combine(s.WorkFolder, "patch.zip");
        List<string> warnings = PlayerPackage.Create(s.Game, s.Tra, new[] { s.Font }, zipPath, "Übersetzung von Taktloss");

        Assert.Contains(warnings, w => w.Contains("fonts"));
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Equal(new[] { "Deutsch-Patch/Deutsch.tra", "Deutsch-Patch/agsfnt1.wfn", "Deutsch-Patch/install.sh", "Deutsch-Patch/uninstall.sh",
            "Deutsch-Patch/install.bat", "Deutsch-Patch/uninstall.bat", "Deutsch-Patch/LIESMICH.txt" }, names);
        string script = new StreamReader(zip.GetEntry("Deutsch-Patch/install.sh")!.Open()).ReadToEnd();
        Assert.Contains("files=\"Deutsch.tra agsfnt1.wfn\"", script);
        Assert.DoesNotContain("@LANG@", script);
        Assert.Equal(0x81ED, (int)((uint)zip.GetEntry("Deutsch-Patch/install.sh")!.ExternalAttributes >> 16));
        string readme = new StreamReader(zip.GetEntry("Deutsch-Patch/LIESMICH.txt")!.Open()).ReadToEnd();
        Assert.StartsWith("Übersetzung von Taktloss", readme);
        Assert.Contains("\r\n", readme);
    }
}

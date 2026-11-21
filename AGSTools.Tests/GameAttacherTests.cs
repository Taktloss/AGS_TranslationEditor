namespace AGSTools.Tests;

public class GameAttacherTests
{
    [Fact]
    public void ParsesTheListOfRunningGames()
    {
        var games = GameAttacher.ParseList("4711\t0\tBlackwell Legacy\n815\t1\tUnavowed\r\nbroken line\n");
        Assert.Equal(new[] { new RunningGame(4711, false, "Blackwell Legacy"), new RunningGame(815, true, "Unavowed") }, games);
    }

    [Fact]
    public void FindsTheHelperNextToTheProgramOrInAGSLive()
    {
        string root = Path.Combine(Path.GetTempPath(), "attach-" + Guid.NewGuid().ToString("N"));
        string live = Path.Combine(root, "AGSLive");
        string bin = Path.Combine(root, "App", "bin", "Debug");
        Directory.CreateDirectory(live);
        Directory.CreateDirectory(bin);
        try
        {
            Assert.Null(GameAttacher.FindFolder(bin));
            File.WriteAllText(Path.Combine(live, GameAttacher.InjectorName), "");
            Assert.Null(GameAttacher.FindFolder(bin)); // the hook is needed too
            File.WriteAllText(Path.Combine(live, GameAttacher.HookName), "");
            Assert.Equal(live, GameAttacher.FindFolder(bin));
            File.WriteAllText(Path.Combine(bin, GameAttacher.InjectorName), "");
            File.WriteAllText(Path.Combine(bin, GameAttacher.HookName), "");
            Assert.Equal(bin, GameAttacher.FindFolder(bin)); // a release has them next to the program
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

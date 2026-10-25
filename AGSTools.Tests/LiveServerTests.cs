using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace AGSTools.Tests;

/// <summary>A test client plays the game side of the live hook (AGSLive/agslive.c).</summary>
public class LiveServerTests : IDisposable
{
    private static readonly Encoding Cp1252 = AgsTextEncoding.ForCodepage(1252);

    // Unix socket paths are limited to 107 characters, so not below a possibly long temp folder
    private readonly string _path = Path.Combine(OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp",
        $"agslive-test-{Guid.NewGuid():N}.sock");
    private readonly LiveServer _server;

    public LiveServerTests()
    {
        _server = new LiveServer(_path, Cp1252);
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    private Socket ConnectGame()
    {
        var game = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        game.Connect(new UnixDomainSocketEndPoint(_path));
        game.ReceiveTimeout = 5000;
        return game;
    }

    private static string ReceiveLines(Socket game, int count)
    {
        var received = new List<byte>();
        var buffer = new byte[4096];
        while (received.Count(b => b == '\n') < count)
        {
            int read = game.Receive(buffer);
            Assert.True(read > 0, "connection closed");
            received.AddRange(buffer[..read]);
        }
        return Cp1252.GetString(received.ToArray());
    }

    private static T Wait<T>(BlockingCollection<T> items) =>
        items.TryTake(out T? item, TimeSpan.FromSeconds(5)) ? item : throw new TimeoutException();

    [Fact]
    public void Receives_displayed_texts_in_the_game_encoding()
    {
        var texts = new BlockingCollection<LiveText>();
        var connections = new BlockingCollection<bool>();
        _server.TextShown += texts.Add;
        _server.ConnectionChanged += connections.Add;

        using Socket game = ConnectGame();
        game.Send(Cp1252.GetBytes("H\t4242\nT\t&5 Good bye\\nAuntie.\tTsch\u00fcss\\tTante \\\\o/\n"));

        Assert.True(Wait(connections));
        Assert.Equal(4242, _server.GameProcessId);
        Assert.Equal(new LiveText("&5 Good bye\nAuntie.", "Tsch\u00fcss\tTante \\o/"), Wait(texts));
    }

    [Fact]
    public void Sends_corrections_and_repeats_them_when_the_game_reconnects()
    {
        var connections = new BlockingCollection<bool>();
        _server.ConnectionChanged += connections.Add;

        using (Socket game = ConnectGame())
        {
            game.Send(Encoding.ASCII.GetBytes("H\t1\n"));
            Wait(connections);
            _server.SetCorrection("&7 Hello", "&7 Gr\u00fc\u00dfe\n[Zeile 2");
            Assert.Equal("S\t&7 Hello\t&7 Gr\u00fc\u00dfe\\n[Zeile 2\n", ReceiveLines(game, 1));
        }
        Assert.False(Wait(connections));

        using Socket restarted = ConnectGame();
        Assert.Equal("S\t&7 Hello\t&7 Gr\u00fc\u00dfe\\n[Zeile 2\n", ReceiveLines(restarted, 1));
    }

    [Fact]
    public void Removing_a_correction_sends_an_empty_text()
    {
        var connections = new BlockingCollection<bool>();
        _server.ConnectionChanged += connections.Add;
        using Socket game = ConnectGame();
        game.Send(Encoding.ASCII.GetBytes("H\t1\n"));
        Wait(connections);

        _server.SetCorrection("Quit", "Beenden");
        _server.SetCorrection("Quit", null);
        _server.ClearCorrections();

        Assert.Equal("S\tQuit\tBeenden\nS\tQuit\t\nC\n", ReceiveLines(game, 3));
    }

    [Fact]
    public void A_second_editor_on_the_same_socket_is_refused()
    {
        using var second = new LiveServer(_path);
        Assert.Throws<IOException>(second.Start);
    }
}

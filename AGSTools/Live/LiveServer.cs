using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AGSTools
{
    /// <summary>A text the running game displayed: the original (the translation key) and what was shown.</summary>
    public sealed record LiveText(string Original, string Shown);

    /// <summary>
    /// Editor side of the live translation hook (AGSLive/agslive.so): receives every text the game shows
    /// and sends corrections that the game uses instead of its TRA, while it runs.
    /// </summary>
    /// <remarks>
    /// The game connects to a Unix socket. One message per line, fields separated by tabs, with backslash,
    /// newline, tab and CR escaped as \\ \n \t \r. Texts are bytes in the game's text encoding.
    /// Game → editor: <c>H pid</c>, <c>T original shown</c>. Editor → game: <c>S original correction</c>
    /// (empty correction = use the TRA again), <c>C</c> (drop all corrections).
    /// Corrections are kept and sent again when the game reconnects, e.g. after a restart.
    /// </remarks>
    public sealed class LiveServer : IDisposable
    {
        private readonly Dictionary<string, string> _corrections = new();
        private readonly object _lock = new();
        private readonly CancellationTokenSource _stop = new();
        private Socket? _listener;
        private Socket? _client;

        public LiveServer(string? socketPath = null, Encoding? encoding = null)
        {
            SocketPath = socketPath ?? DefaultSocketPath;
            Encoding = encoding ?? AgsTextEncoding.ForCodepage(AgsTextEncoding.DefaultAnsiCodepage);
        }

        /// <summary>Same default as the hook: $AGSLIVE_SOCKET, else $XDG_RUNTIME_DIR/agslive.sock.</summary>
        public static string DefaultSocketPath
        {
            get
            {
                string? path = Environment.GetEnvironmentVariable("AGSLIVE_SOCKET");
                if (!string.IsNullOrEmpty(path))
                    return path;
                string? dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                return !string.IsNullOrEmpty(dir)
                    ? Path.Combine(dir, "agslive.sock")
                    : Path.Combine(Path.GetTempPath(), $"agslive-{Environment.UserName}.sock");
            }
        }

        public string SocketPath { get; }

        /// <summary>Text encoding of the game: its codepage, or UTF-8.</summary>
        public Encoding Encoding { get; set; }

        public bool IsConnected => _client != null;

        /// <summary>Process id the connected game reported.</summary>
        public int GameProcessId { get; private set; }

        /// <summary>Raised on a background thread for every text the game shows.</summary>
        public event Action<LiveText>? TextShown;

        /// <summary>Raised on a background thread when a game connects (true) or disconnects (false).</summary>
        public event Action<bool>? ConnectionChanged;

        public void Start()
        {
            if (File.Exists(SocketPath))
            {
                if (IsListening(SocketPath))
                    throw new IOException($"Another editor already waits for the game at {SocketPath}.");
                File.Delete(SocketPath); // left over from an editor that crashed
            }
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
            _listener.Listen(1);
            _ = Task.Run(AcceptLoop);
        }

        /// <summary>Shows <paramref name="text"/> instead of the translation of <paramref name="original"/>;
        /// null goes back to the TRA. Both are runtime texts (see <see cref="TextEscaping.ToRuntime"/>).</summary>
        public void SetCorrection(string original, string? text)
        {
            lock (_lock)
            {
                if (text == null)
                    _corrections.Remove(original);
                else
                    _corrections[original] = text;
            }
            Send(Message('S', original, text ?? ""));
        }

        public void ClearCorrections()
        {
            lock (_lock)
                _corrections.Clear();
            Send(new[] { (byte)'C', (byte)'\n' });
        }

        public void Dispose()
        {
            _stop.Cancel();
            lock (_lock)
            {
                _client?.Dispose();
                _client = null;
            }
            _listener?.Dispose();
            try
            {
                if (_listener != null)
                    File.Delete(SocketPath);
            }
            catch (IOException) { }
        }

        private static bool IsListening(string path)
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                probe.Connect(new UnixDomainSocketEndPoint(path));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                Socket client;
                try
                {
                    client = await _listener!.AcceptAsync(_stop.Token);
                }
                catch (Exception) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (SocketException)
                {
                    continue;
                }

                // A newly started game replaces the previous one
                List<byte[]> resend = new();
                lock (_lock)
                {
                    _client?.Dispose();
                    _client = client;
                    foreach (var (original, text) in _corrections)
                        resend.Add(Message('S', original, text));
                }
                foreach (byte[] message in resend)
                    Send(message);
                _ = Task.Run(() => ReadLoop(client));
            }
        }

        private async Task ReadLoop(Socket client)
        {
            var buffer = new byte[65536];
            var pending = new List<byte>();
            try
            {
                while (true)
                {
                    int read = await client.ReceiveAsync(buffer, SocketFlags.None, _stop.Token);
                    if (read == 0)
                        break;
                    for (int i = 0; i < read; i++)
                    {
                        if (buffer[i] != (byte)'\n')
                        {
                            pending.Add(buffer[i]);
                            continue;
                        }
                        HandleLine(pending.ToArray());
                        pending.Clear();
                    }
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }

            bool wasCurrent;
            lock (_lock)
            {
                wasCurrent = _client == client;
                if (wasCurrent)
                    _client = null;
            }
            client.Dispose();
            if (wasCurrent && !_stop.IsCancellationRequested)
                ConnectionChanged?.Invoke(false);
        }

        private void HandleLine(byte[] line)
        {
            List<byte[]> fields = SplitFields(line);
            if (fields.Count == 2 && line[0] == (byte)'H')
            {
                GameProcessId = int.TryParse(Encoding.ASCII.GetString(fields[1]), out int pid) ? pid : 0;
                ConnectionChanged?.Invoke(true);
            }
            else if (fields.Count == 3 && line[0] == (byte)'T')
            {
                TextShown?.Invoke(new LiveText(Encoding.GetString(Unescape(fields[1])), Encoding.GetString(Unescape(fields[2]))));
            }
        }

        private void Send(byte[] message)
        {
            lock (_lock)
            {
                try
                {
                    _client?.Send(message);
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
            }
        }

        private byte[] Message(char type, string original, string text)
        {
            var bytes = new List<byte> { (byte)type, (byte)'\t' };
            bytes.AddRange(Escape(Encoding.GetBytes(original)));
            bytes.Add((byte)'\t');
            bytes.AddRange(Escape(Encoding.GetBytes(text)));
            bytes.Add((byte)'\n');
            return bytes.ToArray();
        }

        internal static List<byte> Escape(byte[] text)
        {
            var result = new List<byte>(text.Length + 8);
            foreach (byte b in text)
            {
                switch (b)
                {
                    case (byte)'\\': result.Add((byte)'\\'); result.Add((byte)'\\'); break;
                    case (byte)'\n': result.Add((byte)'\\'); result.Add((byte)'n'); break;
                    case (byte)'\t': result.Add((byte)'\\'); result.Add((byte)'t'); break;
                    case (byte)'\r': result.Add((byte)'\\'); result.Add((byte)'r'); break;
                    default: result.Add(b); break;
                }
            }
            return result;
        }

        internal static byte[] Unescape(byte[] text)
        {
            var result = new List<byte>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != (byte)'\\' || i + 1 >= text.Length)
                {
                    result.Add(text[i]);
                    continue;
                }
                byte next = text[++i];
                result.Add(next switch { (byte)'n' => (byte)'\n', (byte)'t' => (byte)'\t', (byte)'r' => (byte)'\r', _ => next });
            }
            return result.ToArray();
        }

        // Escaped tabs are "\t", so every raw tab separates fields
        private static List<byte[]> SplitFields(byte[] line)
        {
            var fields = new List<byte[]>();
            int start = 0;
            for (int i = 0; i <= line.Length; i++)
            {
                if (i < line.Length && line[i] != (byte)'\t')
                    continue;
                fields.Add(line[start..i]);
                start = i + 1;
            }
            return fields;
        }
    }
}

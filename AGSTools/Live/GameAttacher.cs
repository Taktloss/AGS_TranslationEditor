using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AGSTools
{
    /// <summary>A running AGS game found by agsinject --list.</summary>
    public sealed record RunningGame(int ProcessId, bool IsHooked, string Name);

    /// <summary>
    /// Loads the live hook into a game that is already running, with the helper program agsinject (Linux) or
    /// agsinject.exe (Windows, 32-bit games). It lies next to the hook (agslive.so / agslive.dll): next to the
    /// program in a release, in AGSLive/ or AGSLive/win/ in the source tree.
    /// </summary>
    public static class GameAttacher
    {
        public static string InjectorName => OperatingSystem.IsWindows() ? "agsinject.exe" : "agsinject";

        public static string HookName => OperatingSystem.IsWindows() ? "agslive.dll" : "agslive.so";

        /// <summary>Folder with agsinject and agslive.so, or null.</summary>
        public static string? FindFolder(string? start = null)
        {
            string? dir = start ?? AppContext.BaseDirectory;
            for (int depth = 0; dir != null && depth < 8; depth++, dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar)))
            {
                foreach (string candidate in new[] { dir, Path.Combine(dir, "AGSLive"), Path.Combine(dir, "AGSLive", "win") })
                    if (File.Exists(Path.Combine(candidate, InjectorName)) && File.Exists(Path.Combine(candidate, HookName)))
                        return candidate;
            }
            return null;
        }

        /// <summary>Parses the lines of agsinject --list: pid, hooked (0/1), name.</summary>
        public static List<RunningGame> ParseList(string output)
        {
            var games = new List<RunningGame>();
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = line.TrimEnd('\r').Split('\t');
                if (f.Length >= 3 && int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
                    games.Add(new RunningGame(pid, f[1] == "1", f[2]));
            }
            return games;
        }

        public static async Task<List<RunningGame>> ListAsync(string folder, CancellationToken ct = default)
        {
            var (code, output, error) = await RunAsync(folder, new[] { "--list" }, ct);
            if (code != 0)
                throw new InvalidOperationException(error.Trim());
            return ParseList(output);
        }

        /// <summary>Loads the hook into the game. Returns null on success, else the reason.</summary>
        public static async Task<string?> AttachAsync(string folder, int processId, CancellationToken ct = default)
        {
            var (code, output, error) = await RunAsync(folder, new[] { processId.ToString(CultureInfo.InvariantCulture) }, ct);
            if (code is 0 or 3) // 3 = was already loaded
                return null;
            string message = (error.Length > 0 ? error : output).Trim();
            return message.StartsWith("agsinject: ", StringComparison.Ordinal) ? message["agsinject: ".Length..] : message;
        }

        private static async Task<(int Code, string Output, string Error)> RunAsync(string folder, string[] args, CancellationToken ct)
        {
            var info = new ProcessStartInfo(Path.Combine(folder, InjectorName))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string a in args)
                info.ArgumentList.Add(a);
            if (OperatingSystem.IsWindows())
                info.CreateNoWindow = true;
            using var process = Process.Start(info) ?? throw new InvalidOperationException("agsinject could not be started");
            Task<string> output = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> error = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }
    }
}

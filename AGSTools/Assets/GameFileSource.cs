using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AGSTools
{
    /// <summary>
    /// Where the main game data and the room files of a game are stored.
    /// </summary>
    public sealed class GameFileSource
    {
        private GameFileSource(string requestedPath, string dataPath, AssetLibrary? library)
        {
            RequestedPath = requestedPath;
            DataPath = dataPath;
            Library = library;
        }

        /// <summary>The path the user selected (exe, ags, dta, ...).</summary>
        public string RequestedPath { get; }

        /// <summary>The file that actually contains the game data.</summary>
        public string DataPath { get; }

        /// <summary>The asset library, or null if the game data is a loose file.</summary>
        public AssetLibrary? Library { get; }

        /// <summary>
        /// Resolves the game data for any file a user may pick: a game exe with appended data,
        /// an engine-only exe next to a .ags file, a .ags/.dat library, or a loose game28.dta/ac2game.dta.
        /// </summary>
        public static GameFileSource Open(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Game file not found.", path);

            AssetLibrary? lib = AssetLibrary.TryOpen(path);
            if (lib != null)
                return new GameFileSource(path, path, lib);

            if (IsLooseGameData(path))
                return new GameFileSource(path, path, null);

            // An exe without appended data (e.g. Steam builds): the engine looks for a library next to it
            string? sibling = FindSiblingLibrary(path);
            if (sibling != null)
            {
                AssetLibrary? siblingLib = AssetLibrary.TryOpen(sibling);
                if (siblingLib != null)
                    return new GameFileSource(path, sibling, siblingLib);
            }

            throw new AgsFormatException(
                $"'{Path.GetFileName(path)}' contains no AGS game data, and no game data file (.ags) was found next to it.");
        }

        /// <summary>Reads the main game data (game28.dta for 3.x, ac2game.dta for 2.x).</summary>
        public byte[] ReadMainGameData()
        {
            if (Library == null)
                return File.ReadAllBytes(DataPath);

            if (Library.TryGetAsset("game28.dta", out AssetInfo asset) || Library.TryGetAsset("ac2game.dta", out asset))
                return Library.ReadAsset(asset);

            throw new AgsFormatException($"'{Path.GetFileName(DataPath)}' contains no main game data (game28.dta).");
        }

        /// <summary>
        /// Enumerates the compiled room files (roomN.crm), ordered by room number.
        /// </summary>
        public IEnumerable<(string Name, Func<byte[]> Read)> EnumerateRooms()
        {
            if (Library != null)
            {
                foreach (AssetInfo asset in Library.Assets
                    .Where(a => a.Name.EndsWith(".crm", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(a => RoomNumber(a.Name)).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
                {
                    AssetInfo current = asset;
                    yield return (current.Name, () => Library.ReadAsset(current));
                }
                yield break;
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(DataPath)) ?? ".";
            foreach (string file in Directory.GetFiles(dir, "*.crm", CaseInsensitive)
                .OrderBy(f => RoomNumber(Path.GetFileName(f))))
            {
                string current = file;
                yield return (Path.GetFileName(current), () => File.ReadAllBytes(current));
            }
        }

        /// <summary>Room number from "room12.crm", or int.MaxValue for other names.</summary>
        public static int RoomNumber(string assetName)
        {
            string stem = Path.GetFileNameWithoutExtension(assetName);
            return stem.StartsWith("room", StringComparison.OrdinalIgnoreCase) && int.TryParse(stem.AsSpan(4), out int n)
                ? n
                : int.MaxValue;
        }

        private static readonly EnumerationOptions CaseInsensitive = new() { MatchCasing = MatchCasing.CaseInsensitive };

        private static bool IsLooseGameData(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] sig = GameDataReader.Signature;
            if (fs.Length < sig.Length)
                return false;
            byte[] head = new byte[sig.Length];
            fs.ReadExactly(head);
            return head.AsSpan().SequenceEqual(sig);
        }

        private static string? FindSiblingLibrary(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            string sameName = Path.ChangeExtension(path, ".ags");
            if (!string.Equals(sameName, path, StringComparison.OrdinalIgnoreCase) && File.Exists(sameName))
                return sameName;

            // Case-insensitive match (e.g. "Unavowed.exe" + "unavowed.ags"), otherwise the only .ags in the folder
            string[] candidates = Directory.GetFiles(dir, "*.ags", CaseInsensitive);
            string baseName = Path.GetFileNameWithoutExtension(path);
            return candidates.FirstOrDefault(c => string.Equals(Path.GetFileNameWithoutExtension(c), baseName, StringComparison.OrdinalIgnoreCase))
                ?? (candidates.Length == 1 ? candidates[0] : null);
        }
    }
}

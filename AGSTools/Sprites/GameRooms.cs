using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AGSTools
{
    /// <summary>
    /// The room backgrounds of a game. Edited backgrounds are written as whole room files (roomN.crm) into a patch
    /// folder; installed next to the game they are used instead of the rooms in the game package, like fonts and
    /// the sprite patch. Only the pictures change: walkable areas, hotspots and scripts stay the room's own.
    /// </summary>
    public sealed class GameRooms
    {
        private readonly Dictionary<int, (string Name, Func<byte[]> Read)> _sources;
        private readonly Dictionary<int, RoomImages> _loaded = new();
        private readonly object _lock = new();

        private GameRooms(string gamePath, Dictionary<int, (string, Func<byte[]>)> sources)
        {
            GamePath = gamePath;
            _sources = sources;
        }

        public string GamePath { get; }

        /// <summary>Room numbers, ascending.</summary>
        public IReadOnlyList<int> Rooms => _sources.Keys.OrderBy(n => n).ToList();

        /// <summary>Opens the rooms of the game package (or the loose room files of an unpacked game).</summary>
        public static GameRooms Open(string gamePath)
        {
            GameFileSource source = GameFileSource.Open(gamePath);
            var sources = new Dictionary<int, (string, Func<byte[]>)>();
            foreach (var (name, read) in source.EnumerateRooms())
            {
                int number = GameFileSource.RoomNumber(name);
                if (number != int.MaxValue)
                    sources.TryAdd(number, (name, read));
            }
            return new GameRooms(gamePath, sources);
        }

        /// <summary>The images of a room, read on first use; thread-safe.</summary>
        /// <exception cref="AgsFormatException">The room cannot be read.</exception>
        public RoomImages Room(int number)
        {
            lock (_lock)
            {
                if (_loaded.TryGetValue(number, out RoomImages? room))
                    return room;
                if (!_sources.TryGetValue(number, out var source))
                    throw new ArgumentException($"The game has no room {number}.", nameof(number));
                room = RoomImages.Read(source.Name, source.Read());
                _loaded[number] = room;
                return room;
            }
        }

        /// <summary>All backgrounds; rooms that cannot be read are reported in <paramref name="notes"/>.</summary>
        public List<RoomBackground> Backgrounds(List<string>? notes = null)
        {
            var result = new List<RoomBackground>();
            foreach (int number in Rooms)
            {
                try
                {
                    result.AddRange(Room(number).Backgrounds);
                }
                catch (AgsFormatException ex)
                {
                    notes?.Add(ex.Message);
                }
            }
            return result;
        }

        public SpriteImage Read(int room, int frame)
        {
            RoomImages images = Room(room);
            lock (_lock)
                return images.Read(frame);
        }

        /// <summary>File name for exported backgrounds: room005-bg1.png (bg1 is the main background).</summary>
        public static string PngName(int room, int frame) => $"room{room:D3}-bg{frame + 1}.png";

        private static readonly Regex PngNamePattern = new(@"^room0*(\d+)(?:-bg(\d))?\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Room and frame of "room005-bg2.png" (or "room5.png" for the main background); null for other names.</summary>
        public static (int Room, int Frame)? FromFileName(string path)
        {
            Match m = PngNamePattern.Match(Path.GetFileName(path));
            if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int room))
                return null;
            int frame = m.Groups[2].Success ? m.Groups[2].Value[0] - '1' : 0;
            return frame < 0 ? null : (room, frame);
        }

        /// <summary>
        /// Reads the PNG files named after room backgrounds in <paramref name="folder"/> and returns those that differ
        /// from the game's, by room and frame. Notes tell about skipped files.
        /// </summary>
        public Dictionary<int, Dictionary<int, SpriteImage>> LoadChanged(string folder, List<string> notes)
        {
            var changed = new Dictionary<int, Dictionary<int, SpriteImage>>();
            int unchanged = 0;
            foreach (string path in Directory.EnumerateFiles(folder, "*.png").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (FromFileName(path) is not var (room, frame))
                    continue;
                string file = Path.GetFileName(path);
                RoomImages images;
                try
                {
                    images = Room(room);
                }
                catch (ArgumentException)
                {
                    notes.Add($"{file}: the game has no room {room}; skipped.");
                    continue;
                }
                catch (AgsFormatException ex)
                {
                    notes.Add($"{file}: {ex.Message} Skipped.");
                    continue;
                }
                if (frame >= images.Backgrounds.Count)
                {
                    notes.Add($"{file}: room {room} has only {images.Backgrounds.Count} background(s); skipped.");
                    continue;
                }
                SpriteImage image;
                try
                {
                    image = Png.Read(path);
                }
                catch (Exception ex) when (ex is AgsFormatException or InvalidDataException or EndOfStreamException)
                {
                    notes.Add($"{file}: {ex.Message} Skipped.");
                    continue;
                }
                RoomBackground bg = images.Backgrounds[frame];
                if (image.Width != bg.Width || image.Height != bg.Height)
                {
                    notes.Add($"{file}: {image.Width}x{image.Height}, but the room needs exactly {bg.Width}x{bg.Height} "
                        + "(its walkable areas and hotspots have that size); skipped.");
                    continue;
                }
                bool same;
                lock (_lock)
                    same = images.IsUnchanged(frame, image);
                if (same)
                {
                    unchanged++;
                    continue;
                }
                if (!changed.TryGetValue(room, out var frames))
                    changed[room] = frames = new Dictionary<int, SpriteImage>();
                frames[frame] = image;
            }
            if (unchanged > 0)
                notes.Add($"{unchanged} room PNG files are the same as the game's backgrounds and were left out.");
            return changed;
        }

        /// <summary>Writes roomN.crm files with the replaced backgrounds into <paramref name="folder"/>.</summary>
        /// <returns>The written file names.</returns>
        /// <exception cref="IOException">The folder is the game folder.</exception>
        public List<string> WritePatch(IReadOnlyDictionary<int, Dictionary<int, SpriteImage>> replacements, string folder)
        {
            string gameFolder = Path.GetDirectoryName(Path.GetFullPath(GamePath))!;
            if (Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar).Equals(gameFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Write the room patch into another folder; install it into the game with 'install'.");
            Directory.CreateDirectory(folder);
            var written = new List<string>();
            foreach (var (room, frames) in replacements.OrderBy(r => r.Key))
            {
                RoomImages images = Room(room);
                byte[] data;
                lock (_lock)
                    data = images.WritePatched(frames);
                string name = images.Name.ToLowerInvariant();
                AtomicFile.Write(Path.Combine(folder, name), output => output.Write(data));
                written.Add(name);
            }
            return written;
        }

        /// <summary>True for a room file name (room5.crm), as found in a patch folder.</summary>
        public static bool IsRoomFile(string path) =>
            Path.GetExtension(path).Equals(".crm", StringComparison.OrdinalIgnoreCase) && GameFileSource.RoomNumber(Path.GetFileName(path)) != int.MaxValue;
    }
}

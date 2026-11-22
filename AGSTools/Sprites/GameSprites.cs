using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AGSTools
{
    /// <summary>
    /// The sprites (images) of a game: reads them from the game's own sprite file and writes a patched
    /// copy with replaced sprites. AGS loads an acsprset.spr and sprindex.dat in the game folder instead of
    /// the ones in the game package, so the patch is installed like a font, and the game package stays as it is.
    /// </summary>
    public sealed class GameSprites : IDisposable
    {
        private readonly Stream _stream;
        private readonly long _start;
        private readonly object _lock = new();

        private GameSprites(string gamePath, string sourcePath, Stream stream, long start, SpriteFile file, GameData game)
        {
            GamePath = gamePath;
            SourcePath = sourcePath;
            _stream = stream;
            _start = start;
            File = file;
            Game = game;
        }

        public string GamePath { get; }

        /// <summary>The file the sprites are read from (the game package or a loose acsprset.spr).</summary>
        public string SourcePath { get; }

        public SpriteFile File { get; }

        public GameData Game { get; }

        /// <summary>
        /// Opens the original sprites of a game. A sprite file in the game package is preferred over one in the
        /// game folder, which may be an installed patch.
        /// </summary>
        /// <exception cref="AgsFormatException">No sprite file, or one this version cannot read.</exception>
        public static GameSprites Open(string gamePath)
        {
            GameFileSource source = GameFileSource.Open(gamePath);
            GameData game = GameDataReader.Read(source.ReadMainGameData());
            string path;
            long start;
            if (source.Library != null && source.Library.TryGetAsset(SpriteFile.FileName, out AssetInfo asset))
            {
                path = source.Library.FileOf(asset);
                start = asset.Offset;
            }
            else
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(source.DataPath)) ?? ".";
                path = Directory.EnumerateFiles(dir).FirstOrDefault(f => Path.GetFileName(f).Equals(SpriteFile.FileName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new AgsFormatException($"The game has no sprite file ({SpriteFile.FileName}).");
                start = 0;
            }

            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            try
            {
                stream.Position = start;
                SpriteFile file = SpriteFile.Read(stream);
                return new GameSprites(gamePath, path, stream, start, file, game);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>Whether the game draws the sprite with its alpha channel (else magenta is transparent).</summary>
        public bool HasAlpha(int number) =>
            number >= 0 && number < Game.SpriteFlags.Length && (Game.SpriteFlags[number] & SpriteFile.FlagAlphaChannel) != 0;

        /// <summary>Decodes a sprite; thread-safe.</summary>
        public SpriteImage Read(int number)
        {
            SpriteEntry e = File[number] ?? throw new ArgumentException($"Sprite {number} does not exist.", nameof(number));
            lock (_lock)
                return File.ReadImage(_stream, _start, e, HasAlpha(number), Game.DefaultPalette);
        }

        /// <summary>
        /// The image as the game would show it after storing it in this sprite's format: colors reduced to the
        /// sprite's depth, transparency as the engine sees it. Used to tell real changes from unchanged exports.
        /// </summary>
        public SpriteImage Normalize(int number, SpriteImage image)
        {
            SpriteEntry e = File[number] ?? throw new ArgumentException($"Sprite {number} does not exist.", nameof(number));
            bool alpha = HasAlpha(number);
            var result = new SpriteImage(image.Width, image.Height);
            for (int i = 0; i < image.Pixels.Length; i++)
            {
                uint p = image.Pixels[i];
                result.Pixels[i] = e.BytesPerPixel switch
                {
                    1 => SpriteFile.FromGamePalette(SpriteFile.ToGamePalette(p, Game.DefaultPalette), Game.DefaultPalette),
                    2 => SpriteFile.From565(SpriteFile.To565(p)),
                    _ => SpriteFile.From32(SpriteFile.To32(p, alpha), alpha),
                };
                if (result.Pixels[i] >> 24 == 0)
                    result.Pixels[i] = 0;
            }
            return result;
        }

        /// <summary>True if <paramref name="image"/> looks the same in the game as the original sprite.</summary>
        public bool IsUnchanged(int number, SpriteImage image)
        {
            SpriteImage original = Read(number);
            if (original.Width != image.Width || original.Height != image.Height)
                return false;
            SpriteImage a = Normalize(number, original), b = Normalize(number, image);
            return a.Pixels.AsSpan().SequenceEqual(b.Pixels);
        }

        /// <summary>
        /// Writes acsprset.spr and sprindex.dat with the replaced sprites into <paramref name="folder"/>.
        /// The sprite file is as large as the game's own one (all other sprites are copied).
        /// </summary>
        /// <exception cref="IOException">The folder is the game folder.</exception>
        public void WritePatch(IReadOnlyDictionary<int, SpriteImage> replacements, string folder, IProgress<double>? progress = null)
        {
            string gameFolder = Path.GetDirectoryName(Path.GetFullPath(SourcePath))!;
            if (Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar).Equals(gameFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Write the sprite patch into another folder; install it into the game with 'install'.");
            Directory.CreateDirectory(folder);
            var index = new MemoryStream();
            lock (_lock)
            {
                AtomicFile.Write(Path.Combine(folder, SpriteFile.FileName), output =>
                    File.WritePatched(_stream, _start, replacements, HasAlpha, Game.DefaultPalette, output, index, progress: progress));
            }
            AtomicFile.Write(Path.Combine(folder, SpriteFile.IndexFileName), output => index.WriteTo(output));
        }

        /// <summary>
        /// Reads the PNG files named after sprite numbers (see <see cref="PngName"/>) in <paramref name="folder"/>
        /// and returns those that differ from the game's sprites. Notes tell about skipped files and size changes.
        /// </summary>
        public Dictionary<int, SpriteImage> LoadChanged(string folder, List<string> notes)
        {
            var changed = new Dictionary<int, SpriteImage>();
            int unchanged = 0;
            foreach (string path in Directory.EnumerateFiles(folder, "*.png").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                int number = NumberFromFileName(path);
                if (number < 0)
                    continue;
                if (File[number] is not { } entry)
                {
                    notes.Add($"{Path.GetFileName(path)}: the game has no sprite {number}; skipped.");
                    continue;
                }
                if (changed.ContainsKey(number))
                {
                    notes.Add($"{Path.GetFileName(path)}: sprite {number} was already read from another file; skipped.");
                    continue;
                }
                SpriteImage image;
                try
                {
                    image = Png.Read(path);
                }
                catch (Exception ex) when (ex is AgsFormatException or InvalidDataException or EndOfStreamException)
                {
                    notes.Add($"{Path.GetFileName(path)}: {ex.Message} Skipped.");
                    continue;
                }
                if (IsUnchanged(number, image))
                {
                    unchanged++;
                    continue;
                }
                if (image.Width != entry.Width || image.Height != entry.Height)
                    notes.Add($"Sprite {number} changes size from {entry.Width}x{entry.Height} to {image.Width}x{image.Height}; the game may place or cut it differently.");
                changed[number] = image;
            }
            if (unchanged > 0)
                notes.Add($"{unchanged} PNG files are the same as the game's sprites and were left out.");
            return changed;
        }

        /// <summary>File name for exported sprites: sprite00042.png.</summary>
        public static string PngName(int number) => $"sprite{number:D5}.png";

        private static readonly Regex PngNamePattern = new(@"^(?:sprite)?0*(\d+)\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Sprite number of "sprite00042.png" or "42.png"; -1 for other names.</summary>
        public static int NumberFromFileName(string path)
        {
            Match m = PngNamePattern.Match(Path.GetFileName(path));
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -1;
        }

        /// <summary>True for the two files of a sprite patch.</summary>
        public static bool IsPatchFile(string path)
        {
            string name = Path.GetFileName(path);
            return name.Equals(SpriteFile.FileName, StringComparison.OrdinalIgnoreCase)
                || name.Equals(SpriteFile.IndexFileName, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose() => _stream.Dispose();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AGSTools
{
    public enum FontKind { Ttf, Wfn }

    /// <summary>Where the engine finds a font file.</summary>
    public enum FontOrigin
    {
        /// <summary>A loose agsfntN.* file next to the game, which overrides the packaged one.</summary>
        GameFolder,
        /// <summary>Inside the game's asset library (.ags/.exe).</summary>
        Package,
    }

    /// <summary>A font of a game, as the engine loads it.</summary>
    public sealed class GameFont
    {
        internal GameFont(int number, string fileName, FontOrigin origin, string? filePath, byte[] data, FontInfo? info)
        {
            Number = number;
            FileName = fileName;
            Origin = origin;
            FilePath = filePath;
            Data = data;
            Info = info;
            Kind = fileName.EndsWith(".wfn", StringComparison.OrdinalIgnoreCase) ? FontKind.Wfn : FontKind.Ttf;
            try
            {
                if (Kind == FontKind.Wfn)
                    Wfn = WfnFont.Read(data);
                else
                    Ttf = TtfFont.Read(data);
            }
            catch (AgsFormatException ex)
            {
                Error = ex.Message;
            }
        }

        /// <summary>Opens a single font file (agsfntN.ttf/.wfn or any .ttf/.wfn) outside of a game.</summary>
        public static GameFont FromFile(string path) =>
            new(Math.Max(0, GameFonts.FontNumber(path)), Path.GetFileName(path), FontOrigin.GameFolder, Path.GetFullPath(path), File.ReadAllBytes(path), null);

        /// <summary>A font from memory; the kind is taken from the extension of <paramref name="fileName"/>.</summary>
        public static GameFont FromData(string fileName, byte[] data, FontInfo? info = null) =>
            new(Math.Max(0, GameFonts.FontNumber(fileName)), fileName, FontOrigin.Package, null, data, info);

        public int Number { get; }

        /// <summary>Asset or file name, e.g. "agsfnt3.ttf".</summary>
        public string FileName { get; }

        public FontKind Kind { get; }
        public FontOrigin Origin { get; }

        /// <summary>Full path of a loose font file; null for packaged fonts.</summary>
        public string? FilePath { get; }

        public byte[] Data { get; }

        /// <summary>Settings from the game data, if it could be read.</summary>
        public FontInfo? Info { get; }

        public WfnFont? Wfn { get; }
        public TtfFont? Ttf { get; }

        /// <summary>Why the font file could not be parsed, or null.</summary>
        public string? Error { get; }

        /// <summary>Font name for display, e.g. "Font 3 (agsfnt3.ttf, Arial)".</summary>
        public string DisplayName =>
            $"Font {Number} ({FileName}{(Ttf is { Name.Length: > 0 } ttf ? ", " + ttf.Name : "")})";

        /// <summary>True if the font has a glyph for the character code the engine looks up.</summary>
        public bool HasGlyph(int code) => Wfn?.HasGlyph(code) ?? Ttf?.HasGlyph(code) ?? false;

        /// <summary>True if both codes have a glyph and the glyphs look identical.</summary>
        public bool HasSameGlyph(int codeA, int codeB)
        {
            if (Wfn != null)
                return Wfn.HasGlyph(codeA) && Wfn.HasGlyph(codeB) && Wfn.GetGlyph(codeA)!.Equals(Wfn.GetGlyph(codeB));
            return Ttf?.HasSameOutline(codeA, codeB) ?? false;
        }
    }

    /// <summary>Finds the fonts of a game.</summary>
    public static class GameFonts
    {
        private static readonly EnumerationOptions CaseInsensitive = new() { MatchCasing = MatchCasing.CaseInsensitive };

        /// <summary>Loads the fonts of the game at <paramref name="gamePath"/> (exe, .ags or game28.dta).</summary>
        public static List<GameFont> Load(string gamePath)
        {
            GameFileSource source = GameFileSource.Open(gamePath);
            GameData? game = null;
            try
            {
                game = GameDataReader.Read(source.ReadMainGameData());
            }
            catch (AgsFormatException)
            {
                // Font files are still found by name
            }
            return Load(source, game);
        }

        /// <summary>
        /// Loads font 0..N-1 the way the engine does: for each number "agsfntN.ttf", else "agsfntN.wfn",
        /// each looked up in the game folder first and then in the asset library.
        /// </summary>
        /// <param name="game">Game data for the font count and settings; without it, every agsfnt file found is loaded.</param>
        public static List<GameFont> Load(GameFileSource source, GameData? game)
        {
            var looseFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in new[] { source.RequestedPath, source.DataPath }
                .Select(p => Path.GetDirectoryName(Path.GetFullPath(p)) ?? ".").Distinct())
            {
                foreach (string file in Directory.EnumerateFiles(dir, "agsfnt*.*", CaseInsensitive))
                    looseFiles.TryAdd(Path.GetFileName(file), file);
            }

            int count = game?.Fonts.Count ?? 0;
            if (count == 0)
            {
                IEnumerable<string> names = looseFiles.Keys.Concat(source.Library?.Assets.Select(a => a.Name) ?? Array.Empty<string>());
                count = names.Select(FontNumber).DefaultIfEmpty(-1).Max() + 1;
            }

            var fonts = new List<GameFont>();
            for (int number = 0; number < count; number++)
            {
                FontInfo? info = game != null && number < game.Fonts.Count ? game.Fonts[number] : null;
                foreach (string ext in new[] { ".ttf", ".wfn" })
                {
                    string name = $"agsfnt{number}{ext}";
                    if (looseFiles.TryGetValue(name, out string? path))
                    {
                        fonts.Add(new GameFont(number, Path.GetFileName(path), FontOrigin.GameFolder, path, File.ReadAllBytes(path), info));
                        break;
                    }
                    if (source.Library != null && source.Library.TryGetAsset(name, out AssetInfo asset))
                    {
                        fonts.Add(new GameFont(number, asset.Name, FontOrigin.Package, null, source.Library.ReadAsset(asset), info));
                        break;
                    }
                }
            }
            return fonts;
        }

        /// <summary>
        /// Writes the font files unchanged into <paramref name="folder"/> under their file names.
        /// </summary>
        /// <returns>The written paths.</returns>
        /// <exception cref="IOException">A file exists and <paramref name="overwrite"/> is false.</exception>
        public static List<string> Export(IEnumerable<GameFont> fonts, string folder, bool overwrite = false)
        {
            Directory.CreateDirectory(folder);
            var written = new List<string>();
            foreach (GameFont font in fonts)
            {
                string path = Path.Combine(folder, font.FileName);
                if (!overwrite && File.Exists(path))
                    throw new IOException($"'{path}' already exists.");
                AtomicFile.Write(path, s => s.Write(font.Data));
                written.Add(path);
            }
            return written;
        }

        /// <summary>Font number from "agsfnt12.ttf", or -1.</summary>
        public static int FontNumber(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            return stem.StartsWith("agsfnt", StringComparison.OrdinalIgnoreCase)
                && (ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) || ext.Equals(".wfn", StringComparison.OrdinalIgnoreCase))
                && int.TryParse(stem.AsSpan(6), out int n) && n >= 0
                ? n
                : -1;
        }
    }
}

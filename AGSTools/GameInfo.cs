using System;
using System.Buffers.Binary;
using System.IO;

namespace AGSTools
{
    /// <summary>Identification of an AGS game, as needed to create translations for it.</summary>
    public class GameInfo
    {
        /// <summary>Editor version the game was compiled with, e.g. "3.6.1.35".</summary>
        public string Version { get; set; } = "";

        /// <summary>Game name as the engine compares it with a translation's game name.</summary>
        public string GameTitle { get; set; } = "";

        /// <summary>
        /// Unique id as hex string of its bytes in file order (legacy format of this tool,
        /// e.g. "095A19D4" for uid 0xD4195A09). Prefer <see cref="UniqueId"/>.
        /// </summary>
        public string GameUID
        {
            get => BinaryPrimitives.ReverseEndianness(UniqueId).ToString("X");
            set => UniqueId = BinaryPrimitives.ReverseEndianness(int.Parse(value, System.Globalization.NumberStyles.HexNumber));
        }

        /// <summary>Unique id as stored in the game data and expected in TRA/TRS files.</summary>
        public int UniqueId { get; set; }

        /// <summary>Game data format version (0 if unknown).</summary>
        public int DataVersion { get; set; }

        /// <summary>Codepage of game texts (65001 = UTF-8, 0 = 8-bit).</summary>
        public int TextCodepage { get; set; }

        public bool IsUnicode => TextCodepage == 65001;

        /// <summary>The file the game data was read from (may differ from the selected file, e.g. exe → .ags).</summary>
        public string DataPath { get; set; } = "";

        /// <summary>
        /// Reads the game identification from any game file: an exe with appended data, an engine-only exe
        /// next to its .ags file, a .ags/.dat library or a loose game28.dta/ac2game.dta.
        /// Returns null if the file contains no AGS game data.
        /// </summary>
        /// <param name="filename">Game file</param>
        /// <param name="version">Unused; kept for compatibility.</param>
        public static GameInfo? GetGameInfo(string filename, int version = 0)
        {
            GameData? data = TryReadFromSource(filename, out string dataPath) ?? TryReadByScan(filename);
            if (data == null)
                return null;

            return new GameInfo
            {
                Version = data.EngineVersion,
                GameTitle = data.Title,
                UniqueId = data.UniqueId,
                DataVersion = data.DataVersion,
                TextCodepage = data.TextCodepage,
                DataPath = dataPath.Length > 0 ? dataPath : filename,
            };
        }

        /// <summary>Translation options identifying this game.</summary>
        public TranslationOptions ToTranslationOptions() => new()
        {
            GameUid = UniqueId,
            GameName = GameTitle,
            Encoding = IsUnicode ? "UTF-8" : "",
            AnsiCodepage = IsUnicode || TextCodepage <= 0 ? AgsTextEncoding.DefaultAnsiCodepage : TextCodepage,
        };

        private static GameData? TryReadFromSource(string filename, out string dataPath)
        {
            dataPath = "";
            try
            {
                GameFileSource source = GameFileSource.Open(filename);
                dataPath = source.DataPath;
                return GameDataReader.ReadHeader(source.ReadMainGameData());
            }
            catch (AgsFormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Fallback for files with embedded game data that is not in an asset library:
        /// locate the data signature and read the header from there.
        /// </summary>
        private static GameData? TryReadByScan(string filename)
        {
            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
            long start = FindSignature(fs, GameDataReader.Signature);
            if (start < 0)
                return null;

            // The header lies well within the first 64 KB; extensions further on are not needed here
            byte[] header = new byte[(int)Math.Min(64 * 1024, fs.Length - start)];
            fs.Position = start;
            fs.ReadExactly(header);
            try
            {
                return GameDataReader.ReadHeader(header);
            }
            catch (AgsFormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the offset of the first occurrence of <paramref name="signature"/> in the stream, or -1.
        /// Consecutive reads overlap so a match spanning two blocks is not missed.
        /// </summary>
        private static long FindSignature(Stream stream, byte[] signature)
        {
            const int blockSize = 64 * 1024;
            int overlap = signature.Length - 1;
            byte[] buffer = new byte[blockSize + overlap];
            int carried = 0;
            long bufferStart = 0;

            stream.Position = 0;
            int read;
            while ((read = stream.Read(buffer, carried, blockSize)) > 0)
            {
                int valid = carried + read;
                int idx = buffer.AsSpan(0, valid).IndexOf(signature);
                if (idx >= 0)
                    return bufferStart + idx;

                // Keep the tail of this block in front of the next one
                carried = Math.Min(overlap, valid);
                Buffer.BlockCopy(buffer, valid - carried, buffer, 0, carried);
                bufferStart += valid - carried;
            }
            return -1;
        }
    }
}

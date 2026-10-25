using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Reads and writes compiled AGS translations (.tra). Port of Common/data/tra_file.cpp:
    /// a signature followed by blocks (GameID, Dictionary, TextOpts, "ext_sopts", ...), each with
    /// a type and length, terminated by -1.
    /// </summary>
    public static class TraFile
    {
        private static readonly byte[] Signature = "AGSTranslation\0"u8.ToArray();

        private const int BlockDict = 1;
        private const int BlockGameId = 2;
        private const int BlockTextOpts = 3;
        private const int BlockEnd = -1;
        private const int OptAutoTranslateSaid = 0x0001;

        /// <summary>Reads a TRA file.</summary>
        /// <param name="ansiCodepage">Codepage of 8-bit texts, i.e. the game's text codepage (TRA files do not store it).</param>
        public static TranslationDocument Read(string path, int ansiCodepage = AgsTextEncoding.DefaultAnsiCodepage)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                return Read(fs, ansiCodepage);
            }
            catch (AgsFormatException ex)
            {
                throw new AgsFormatException($"'{Path.GetFileName(path)}': {ex.Message}", ex);
            }
        }

        public static TranslationDocument Read(Stream stream, int ansiCodepage = AgsTextEncoding.DefaultAnsiCodepage)
        {
            using var r = new AgsReader(stream, Encoding.Latin1, leaveOpen: true);

            if (r.Length < Signature.Length ||
                !r.ReadBytes(Signature.Length).AsSpan(0, 14).SequenceEqual(Signature.AsSpan(0, 14)))
                throw new AgsFormatException("Not an AGS translation file (signature missing).");

            var doc = new TranslationDocument();
            var options = doc.Options;
            options.AnsiCodepage = ansiCodepage;
            byte[] gameName = Array.Empty<byte>();
            var rawEntries = new List<(byte[] Key, byte[] Value)>();
            var stringOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                if (r.AtEnd)
                    throw new AgsFormatException("Unexpected end of file (missing end-of-data marker).");

                int id = r.ReadInt32();
                if (id == BlockEnd)
                    break;

                string extName = id == 0 ? r.ReadFixedString(16) : "";
                long length = id == 0 ? r.ReadInt64() : r.ReadInt32();
                long start = r.Position;
                if (length < 0 || start + length > r.Length)
                    throw new AgsFormatException($"Block {DescribeBlock(id, extName)} at offset {start} has an invalid length ({length}).");

                switch (id)
                {
                    case BlockDict:
                        ReadDictionary(r, rawEntries);
                        break;
                    case BlockGameId:
                        options.GameUid = r.ReadInt32();
                        gameName = r.ReadEncryptedBytes();
                        // Older writers (pre-3.0 AGS editors, early versions of this tool) stored a wrong
                        // length for this block; its fields are self-describing, so continue right after them.
                        continue;
                    case BlockTextOpts:
                        options.NormalFont = r.ReadInt32();
                        options.SpeechFont = r.ReadInt32();
                        options.TextDirection = ToTextDirection(r.ReadInt32());
                        if (length > 12)
                            options.AutoTranslateParserSaid = (r.ReadInt32() & OptAutoTranslateSaid) != 0;
                        break;
                    case 0 when extName.Equals("ext_sopts", StringComparison.OrdinalIgnoreCase):
                        int count = r.ReadCount("string option", 10_000);
                        for (int i = 0; i < count; i++)
                            stringOptions[r.ReadPrefixedString()] = r.ReadPrefixedString();
                        break;
                    // ext_fonts, ext_parserdict and unknown blocks are skipped
                }

                if (r.Position > start + length)
                    throw new AgsFormatException($"Block {DescribeBlock(id, extName)} at offset {start} is longer than declared.");
                r.Position = start + length;
            }

            options.Encoding = stringOptions.GetValueOrDefault("encoding", "");
            options.GameEncoding = stringOptions.GetValueOrDefault("gameencoding", "");
            options.Language = stringOptions.GetValueOrDefault("language", "");

            // The encoding option is stored after the dictionary, so decode at the end
            Encoding textEncoding = options.GetTextEncoding();
            options.GameName = textEncoding.GetString(gameName);
            foreach (var (key, value) in rawEntries)
            {
                string source = TextEscaping.ToTrs(textEncoding.GetString(key));
                doc.Entries.TryAdd(source, TextEscaping.ToTrs(textEncoding.GetString(value)));
            }
            return doc;
        }

        private static void ReadDictionary(AgsReader r, List<(byte[], byte[])> entries)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                byte[] key = r.ReadEncryptedBytes();
                byte[] value = r.ReadEncryptedBytes();
                if (key.Length == 0 && value.Length == 0)
                    break;
                // Like the engine: skip empty keys and repeated keys
                if (key.Length == 0 || !seen.Add(Encoding.Latin1.GetString(key)))
                    continue;
                entries.Add((key, value));
            }
        }

        /// <summary>
        /// Writes a TRA file. Entries without translation are left out, like the AGS compiler does.
        /// Returns warnings, e.g. about characters that cannot be stored in an 8-bit translation.
        /// </summary>
        public static IReadOnlyList<string> Write(string path, TranslationDocument doc)
        {
            var warnings = new List<string>();
            AtomicFile.Write(path, stream => Write(stream, doc, warnings));
            return warnings;
        }

        public static void Write(Stream stream, TranslationDocument doc, List<string> warnings)
        {
            TranslationOptions options = doc.Options;
            Encoding textEncoding = options.GetTextEncoding();
            var unencodable = new List<string>();
            using var w = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

            w.Write(Signature);

            WriteBlock(w, BlockGameId, "", () =>
            {
                w.Write(options.GameUid);
                WriteEncrypted(w, Encode(options.GameName, textEncoding, options.GameName, unencodable));
            });

            WriteBlock(w, BlockDict, "", () =>
            {
                foreach (var (source, translation) in doc.Entries)
                {
                    if (source.Length == 0 || string.IsNullOrWhiteSpace(translation))
                        continue;
                    string key = TextEscaping.ToRuntime(source);
                    WriteEncrypted(w, Encode(key, textEncoding, Shorten(source), unencodable));
                    WriteEncrypted(w, Encode(TextEscaping.ToRuntime(translation), textEncoding, Shorten(translation), unencodable));
                }
                WriteEncrypted(w, Array.Empty<byte>());
                WriteEncrypted(w, Array.Empty<byte>());
            });

            WriteBlock(w, BlockTextOpts, "", () =>
            {
                w.Write(options.NormalFont);
                w.Write(options.SpeechFont);
                w.Write((int)options.TextDirection);
                w.Write(options.AutoTranslateParserSaid ? OptAutoTranslateSaid : 0);
            });

            WriteBlock(w, 0, "ext_sopts", () =>
            {
                var stringOptions = new SortedDictionary<string, string>(StringComparer.Ordinal);
                if (options.Encoding.Length > 0) stringOptions["encoding"] = options.Encoding;
                if (options.GameEncoding.Length > 0) stringOptions["gameencoding"] = options.GameEncoding;
                if (options.Language.Length > 0) stringOptions["language"] = options.Language;
                w.Write(stringOptions.Count);
                foreach (var (key, value) in stringOptions)
                {
                    WritePrefixed(w, Encoding.ASCII.GetBytes(key));
                    WritePrefixed(w, Encoding.ASCII.GetBytes(value));
                }
            });

            if (unencodable.Count > 0)
            {
                warnings.Add($"{unencodable.Count} text(s) contain characters that do not exist in codepage {textEncoding.CodePage} and were stored as '?' " +
                    $"(e.g. \"{string.Join("\", \"", unencodable.Take(3))}\"). Set the translation encoding to UTF-8 to keep them."
                    + " This requires AGS 3.6 or newer.");
            }
            if (options.FontOverrides.Count > 0)
                warnings.Add($"{options.FontOverrides.Count} font override(s) are kept in the TRS file but not compiled into the TRA file.");

            w.Write(BlockEnd);
        }

        private static void WriteBlock(BinaryWriter w, int id, string extName, Action writeData)
        {
            w.Write(id);
            if (id == 0)
            {
                byte[] name = new byte[16];
                Encoding.ASCII.GetBytes(extName).CopyTo(name, 0);
                w.Write(name);
            }

            long lengthAt = w.BaseStream.Position;
            if (id == 0) w.Write(0L); else w.Write(0);
            long start = w.BaseStream.Position;

            writeData();

            long end = w.BaseStream.Position;
            w.BaseStream.Position = lengthAt;
            if (id == 0) w.Write(end - start); else w.Write(checked((int)(end - start)));
            w.BaseStream.Position = end;
        }

        private static void WriteEncrypted(BinaryWriter w, byte[] text)
        {
            byte[] encrypted = AgsEncryption.EncryptString(text);
            w.Write(encrypted.Length);
            w.Write(encrypted);
        }

        private static void WritePrefixed(BinaryWriter w, byte[] text)
        {
            w.Write(text.Length);
            w.Write(text);
        }

        private static byte[] Encode(string text, Encoding encoding, string description, List<string> unencodable)
        {
            if (!AgsTextEncoding.CanEncode(encoding, text))
                unencodable.Add(description);
            byte[] bytes = encoding.GetBytes(text);
            int nul = Array.IndexOf(bytes, (byte)0);
            return nul >= 0 ? bytes[..nul] : bytes;
        }

        private static TextDirection ToTextDirection(int value) => value switch
        {
            1 => TextDirection.LeftToRight,
            2 => TextDirection.RightToLeft,
            _ => TextDirection.Default,
        };

        private static string DescribeBlock(int id, string extName) => id == 0 ? $"'{extName}'" : id.ToString();

        private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "…";
    }
}

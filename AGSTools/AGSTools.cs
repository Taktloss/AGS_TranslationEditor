/*
    Copyright 2015 Bernd Keilmann

    This file is part of the AGS Translation Editor.

    AGS Translation Editor is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    AGS Translation Editor is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with AGS Translation Editor.  If not, see<http://www.gnu.org/licenses/>.

    Diese Datei ist Teil von AGS Translation Editor.

    AGS Translation Editor ist Freie Software: Sie können es unter den Bedingungen
    der GNU General Public License, wie von der Free Software Foundation,
    Version 3 der Lizenz oder (nach Ihrer Wahl) jeder späteren
    veröffentlichten Version, weiterverbreiten und/oder modifizieren.

    Fubar wird in der Hoffnung, dass es nützlich sein wird, aber
    OHNE JEDE GEWÄHRLEISTUNG, bereitgestellt; sogar ohne die implizite
    Gewährleistung der MARKTFÄHIGKEIT oder EIGNUNG FÜR EINEN BESTIMMTEN ZWECK.
    Siehe die GNU General Public License für weitere Details.

    Sie sollten eine Kopie der GNU General Public License zusammen mit diesem
    Programm erhalten haben.Wenn nicht, siehe <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;


namespace AGSTools
{
    public static class Translation
    {
        //Encryption string
        private static readonly char[] _passwEncString = { 'A', 'v', 'i', 's', ' ', 'D', 'u', 'r', 'g', 'a', 'n' };

        /// <summary>
        /// Reads and parses a TRA file
        /// </summary>
        /// <param name="filename">Filename</param>
        /// <returns>A Dictionary with the translation entries</returns>
        public static Dictionary<string, string> ParseTRA_Translation(string filename)
        {
            try
            {
            using (FileStream fs = File.OpenRead(filename))
            {
                using var br = new BinaryReader(fs, Encoding.Latin1, leaveOpen: true);
                Dictionary<string, string> _transLines = new Dictionary<string, string>();

                //Tranlsation File Signature
                char[] transsig = new char[16];
                transsig = br.ReadChars(15);
                //Check AGS Translation Header
                if (string.Compare(new string(transsig),"AGSTranslation") == 0)
                {
                    //Read Translation File BlockType for Example 1,2,3
                    int blockType = br.ReadInt32();
                    if (blockType == 1)
                    {
                        //Not used
                    }
                    else if (blockType == 2)
                    {
                        //Dummy Read
                        br.ReadInt32();
                        //Read GameID
                        int iGameUID = br.ReadInt32();

                        //Get GameTitle
                        int GameTitleLength = br.ReadInt32();
                        char[] cGameTitle = Encoding.Latin1.GetChars(br.ReadBytes(GameTitleLength));

                        //Game Name
                        DecryptText(cGameTitle);
                        string sGameTitle = new string(cGameTitle);

                        //dummy read
                        br.ReadInt32();
                        //Calculate Translation length
                        long translationLength = br.ReadInt32() + fs.Position;

                        //Loop throught File and decrypt entries
                        while (fs.Position < translationLength)
                        {
                            int newlen = br.ReadInt32();

                            //Read original Text
                            char[] cSourceText = Encoding.Latin1.GetChars(br.ReadBytes(newlen));
                            DecryptText(cSourceText);
                            string sDecSourceText = new string(cSourceText).Trim('\0');

                            //Read Translated Text
                            newlen = br.ReadInt32();
                            char[] cTranslatedText = Encoding.Latin1.GetChars(br.ReadBytes(newlen));
                            DecryptText(cTranslatedText);
                            string sDecTranslatedText = new string(cTranslatedText).Trim('\0');

                            //Check for already existing entry/key and populate List with data
                            if (!_transLines.ContainsKey(sDecSourceText))
                                _transLines.Add(sDecSourceText, sDecTranslatedText);
                        }
                        return _transLines;
                    }
                    else if (blockType == 3)
                    {
                        //Not used
                    }
                }
                return _transLines;
            }
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Parse a TRS file for AGS
        /// </summary>
        /// <param name="filename">Input filename</param>
        /// <returns>Dictionary with Translation entries</returns>
        public static Dictionary<string, string> ParseTRS_Translation(string filename)
        {
            try
            {
            string[] list = File.ReadAllLines(filename, Encoding.Latin1);
            Dictionary<string, string> _transLines = new Dictionary<string, string>();

            //Look for comments and remove them
            var result = Array.FindAll(list, s => !s.StartsWith("//", StringComparison.Ordinal));

            for (int i = 0; i < result.Length;)
            {
                string sSourceText = result[i];
                i++;
                string sTranslationText = "";
                if (i < result.Length)
                {
                    sTranslationText = result[i];
                    i++;
                }

                //Check for already existing entry/key
                if (!_transLines.ContainsKey(sSourceText))
                    _transLines.Add(sSourceText, sTranslationText);
            }
            return _transLines;
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Create a TRA File for AGS
        /// </summary>
        /// <param name="info">Game Information like Title,UID</param>
        /// <param name="filename">Output filename</param>
        /// <param name="entryList">List with Translation entries</param>
        public static void CreateTRA_File(GameInfo info, string filename, Dictionary<string, string> entryList)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Create))
            {
                //Tail
                byte[] tail =
                {
                0x01, 0x00, 0x00, 0x00, 0x41, 0x01, 0x00, 0x00, 0x00, 0x41, 0x03, 0x00, 0x00, 0x00, 0x0C, 0x00,
                0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00,
                };

                //Write always header "AGSTranslation\0
                byte[] agsHeader =
                {0x41, 0x47, 0x53, 0x54, 0x72, 0x61, 0x6E, 0x73, 0x6C, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x00,};
                fs.Write(agsHeader, 0, agsHeader.Length);

                // Prepare GameTitle bytes first so we can calculate the correct block size
                string GameTitle = info.GameTitle + "\0";
                byte[] bGameTitle = Encoding.Latin1.GetBytes(GameTitle);
                char[] cGameTitle = new char[GameTitle.Length];
                GameTitle.CopyTo(0, cGameTitle, 0, GameTitle.Length);
                EncryptText(cGameTitle);
                CharToByte(cGameTitle, bGameTitle);

                // Block type 2 = GameID. Block size = UID(4) + titleLenField(4) + titleBytes
                int gameIdBlockSize = 4 + 4 + bGameTitle.Length;
                fs.Write(BitConverter.GetBytes(2), 0, 4);               // block type
                fs.Write(BitConverter.GetBytes(gameIdBlockSize), 0, 4); // block size (must be exact)

                //Write GameUID. Important or Translation does not load!
                string sGameUID = info.GameUID;
                int decAgain = int.Parse(sGameUID, System.Globalization.NumberStyles.HexNumber);
                byte[] bGameUID = BitConverter.GetBytes(SwapEndianness(decAgain));
                fs.Write(bGameUID, 0, bGameUID.Length);

                //Write GameTitle Length then the encrypted title
                fs.Write(BitConverter.GetBytes(bGameTitle.Length), 0, 4);
                fs.Write(bGameTitle, 0, bGameTitle.Length);

                //Dummy write
                byte[] bDummy = { 0x01, 0x00, 0x00, 0x00, };
                fs.Write(bDummy, 0, bDummy.Length);

                //Write Length translation
                long translationLengthPosition = fs.Position;
                //Dummy write again
                fs.Write(bDummy, 0, bDummy.Length);

                long translationLength = 0;
                if (entryList.Count > 0)
                {
                    foreach (KeyValuePair<string, string> pair in entryList)
                    {
                        if (!string.Equals(pair.Value, ""))
                        {
                            //Entry1
                            string entry1 = pair.Key + "\0";
                            byte[] bEntry1 = Encoding.Latin1.GetBytes(entry1);

                            //Write string length
                            byte[] bEntry1Length = BitConverter.GetBytes(bEntry1.Length);
                            fs.Write(bEntry1Length, 0, bEntry1Length.Length);

                            //Write Encrypted Text
                            char[] cEntry1 = new char[bEntry1.Length];
                            Array.Copy(bEntry1, cEntry1, bEntry1.Length);
                            EncryptText(cEntry1);
                            CharToByte(cEntry1, bEntry1);

                            fs.Write(bEntry1, 0, bEntry1.Length);

                            //Entry2
                            string entry2 = pair.Value + "\0";
                            byte[] bEntry2 = Encoding.Latin1.GetBytes(entry2);

                            //Write string length
                            byte[] bEntry2Length = BitConverter.GetBytes(bEntry2.Length);
                            fs.Write(bEntry2Length, 0, bEntry2Length.Length);

                            //Write Encrypted Text
                            char[] cEntry2 = new char[bEntry2.Length];
                            Array.Copy(bEntry2, cEntry2, bEntry2.Length);
                            EncryptText(cEntry2);
                            CharToByte(cEntry2, bEntry2);
                            fs.Write(bEntry2, 0, bEntry2.Length);

                            long tempLength = BitConverter.ToInt32(bEntry1Length, 0) + 4 +
                                              BitConverter.ToInt32(bEntry2Length, 0) + 4;
                            translationLength = translationLength + tempLength;
                        }
                    }
                    //Write Tail
                    fs.Write(tail, 0, tail.Length);
                    //Write Translation length + 10
                    byte[] b = BitConverter.GetBytes((int)(translationLength + 10));
                    fs.Position = translationLengthPosition;
                    fs.Write(b, 0, b.Length);
                }
            }
        }

        /// <summary>
        /// Decrypt a char array
        /// </summary>
        /// <param name="toDec">char array to decrypt</param>
        private static void DecryptText(char[] toDec)
        {
            int adx = 0;
            int todecx = 0;

            while (todecx < toDec.Length)
            {
                if (toDec[todecx] == 0)
                    break;
                //-
                toDec[todecx] -= _passwEncString[adx];

                adx++;
                todecx++;

                if (adx > 10)
                    adx = 0;
            }
        }

        /// <summary>
        /// Encrypt a char array
        /// </summary>
        /// <param name="toEnc">char array to encrypt</param>
        private static void EncryptText(char[] toEnc)
        {
            int adx = 0;
            int toencx = 0;

            while (toencx < toEnc.Length)
            {
                //+
                toEnc[toencx] += _passwEncString[adx];
                adx++;
                toencx++;

                if (adx > 10)
                    adx = 0;
            }
        }

        /// <summary>
        /// Help function to swap between endianns
        /// </summary>
        /// <param name="value"></param>
        /// <returns>Value to swap</returns>
        private static int SwapEndianness(int value)
        {
            var b1 = (value >> 0) & 0xff;
            var b2 = (value >> 8) & 0xff;
            var b3 = (value >> 16) & 0xff;
            var b4 = (value >> 24) & 0xff;

            return b1 << 24 | b2 << 16 | b3 << 8 | b4 << 0;
        }

        /// <summary>
        /// Copy Char to Byte Array
        /// </summary>
        /// <param name="chars"></param>
        /// <param name="bytes"></param>
        private static void CharToByte(char[] chars, byte[] bytes)
        {
            int x = 0;
            foreach (char c in chars)
            {
                bytes[x] = (byte)c;
                x++;
            }
        }
    }

    public static class Extraction
    {
        // SCOM script header: "SCOM" (4) + version int32 (4) = 8 bytes before data fields
        // version byte is 'Y'(89) for old scripts, 'Z'(90) for newer
        private static readonly byte[] ScomzSignature = { (byte)'S', (byte)'C', (byte)'O', (byte)'M', (byte)'Z' };
        private static readonly byte[] ScomySignature = { (byte)'S', (byte)'C', (byte)'O', (byte)'M', (byte)'Y' };

        // AGS game data signature (30 bytes incl. null terminator)
        private static readonly byte[] GameDataSignature = Encoding.Latin1.GetBytes("Adventure Creator Game File v2");

        // Known AGS internal string prefixes to discard
        private static readonly string[] InternalPrefixes = { "__", "$", "AGS", "Obj_" };

        // Known function names / identifiers in AGS scripts (exact or prefix)
        private static readonly HashSet<string> KnownInternalStrings = new HashSet<string>(StringComparer.Ordinal)
        {
            "on_event", "on_key_press", "on_mouse_click", "game_start", "repeatedly_execute",
            "repeatedly_execute_always", "late_repeatedly_execute_always", "on_call",
            "dialog_request", "getplayercharacter", "IsGamePaused",
        };

        /// <summary>
        /// Extracts all translatable strings from an AGS game file (.exe or .ags/.bin)
        /// and writes them to a .trs file.  Covers:
        ///   • Script string tables (all Say/Display text)
        ///   • Game-data section: character names, inventory names, global messages, dialog options
        /// </summary>
        public static void ParseAGSFile(string filename, string? outputPath = null)
        {
            try
            {
            byte[] fileBytes = File.ReadAllBytes(filename);
            Console.Error.WriteLine($"Read {fileBytes.Length / 1024 / 1024} MB — searching for script blocks...");

            // Deduplicated, ordered result set
            var allStrings = new LinkedList<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void AddString(string s)
            {
                if (!string.IsNullOrWhiteSpace(s) && seen.Add(s))
                    allStrings.AddLast(s);
            }

            // 1. Extract strings from all compiled script (SCOM) blocks
            var scriptStrings = ExtractScriptStrings(fileBytes).ToList();
            Console.Error.WriteLine($"Script strings found: {scriptStrings.Count}");
            foreach (string s in scriptStrings)
                AddString(s);

            // 2. Extract strings from the game data section (game28.dta / ac2game.dta)
            // Try structural CLIB parser first; fall back to heuristic scan if it returns nothing.
            var gameDataStrings = AgsGameDataParser.ExtractFromFile(filename);
            if (gameDataStrings.Count == 0)
                gameDataStrings = ExtractGameDataStrings(fileBytes).ToList();
            Console.Error.WriteLine($"Game-data strings found: {gameDataStrings.Count}");
            foreach (string s in gameDataStrings)
                AddString(s);

            // Build TRS output: each entry is source line + empty translation line
            var outputLines = new List<string>();
            foreach (string s in allStrings)
            {
                outputLines.Add(s);
                outputLines.Add(string.Empty); // empty translation placeholder
            }

            string outPath = outputPath ?? Path.ChangeExtension(filename, ".trs");
            File.WriteAllLines(outPath, outputLines, Encoding.Latin1);
            Console.Error.WriteLine($"Total unique strings: {allStrings.Count} → {outPath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error parsing AGS file: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------ //
        //  Script string extraction                                           //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Finds every SCOM compiled-script block in the file and extracts its
        /// string-literal table.  Uses a byte-level KMP search to avoid block-
        /// boundary misses and multiple-match omissions.
        /// </summary>
        private static IEnumerable<string> ExtractScriptStrings(byte[] data)
        {
            var results = new List<string>();

            foreach (long offset in FindAll(data, ScomzSignature))
                results.AddRange(ParseScomStringTable(data, offset));

            foreach (long offset in FindAll(data, ScomySignature))
                results.AddRange(ParseScomStringTable(data, offset));

            return results;
        }

        /// <summary>
        /// Parses the string table of a single SCOM script block.
        /// SCOM layout (after the 5-byte "SCOMZ" / "SCOMY" tag):
        ///   +0  int32  (version low bytes, usually 0x00 0x00 0x00 making 4 bytes with the tag byte)
        ///   After 8 bytes from tag start:
        ///   +8  int32  globaldata_size
        ///   +12 int32  code_size  (count of 32-bit instructions)
        ///   +16 int32  strings_size
        ///   Then: globaldata (globaldata_size bytes)
        ///         code       (code_size * 4 bytes)
        ///         strings    (strings_size bytes, null-separated)
        /// </summary>
        private static IEnumerable<string> ParseScomStringTable(byte[] data, long tagOffset)
        {
            const int HeaderSize = 8; // "SCOM" (4) + version int32 (4)
            long pos = tagOffset + HeaderSize;

            if (pos + 12 > data.Length) yield break;

            int globalDataSize = ReadInt32LE(data, pos);       pos += 4;
            int codeSize       = ReadInt32LE(data, pos);       pos += 4;
            int stringsSize    = ReadInt32LE(data, pos);       pos += 4;

            // Sanity checks to avoid reading garbage offsets
            if (globalDataSize < 0 || globalDataSize > 10_000_000) yield break;
            if (codeSize       < 0 || codeSize       > 5_000_000)  yield break;
            if (stringsSize    < 0 || stringsSize     > 5_000_000)  yield break;

            long stringsStart = pos + globalDataSize + (long)codeSize * 4;
            if (stringsStart + stringsSize > data.Length) yield break;

            // The string table is a sequence of null-terminated Latin1 strings
            long end = stringsStart + stringsSize;
            long cur = stringsStart;
            while (cur < end)
            {
                int nullPos = IndexOfByte(data, 0x00, cur, end);
                if (nullPos < 0) nullPos = (int)end;

                string s = Encoding.Latin1.GetString(data, (int)cur, nullPos - (int)cur);
                if (IsTranslatableString(s))
                    yield return s;

                cur = nullPos + 1;
            }
        }

        // ------------------------------------------------------------------ //
        //  Game data section extraction                                       //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Finds the AGS game-data section (embedded in EXE or standalone .dta/.ags)
        /// and extracts:
        ///   • Character display names
        ///   • Inventory item names
        ///   • Global game messages
        ///   • Dialog option texts
        /// Strategy: locate the "Adventure Creator Game File v2" signature,
        /// then scan for clusters of fixed-length null-padded strings that match
        /// the known array layouts in GameSetupStructBase.
        /// </summary>
        private static IEnumerable<string> ExtractGameDataStrings(byte[] data)
        {
            var results = new List<string>();

            foreach (long sigOffset in FindAll(data, GameDataSignature))
            {
                // Skip the 30-byte signature + null
                long gameDataStart = sigOffset + GameDataSignature.Length + 1;
                results.AddRange(ScanGameDataSection(data, gameDataStart));
            }

            return results;
        }

        /// <summary>
        /// Scans the game data section for fixed-size string arrays and variable-
        /// length message blocks.  Because the binary layout changes between AGS
        /// versions we use a sliding-window heuristic: a valid "string array" is
        /// a run of slots each exactly <slotSize> bytes where every slot ends with
        /// at least one 0x00 and the non-null prefix is printable Latin-1 text.
        /// Known slot sizes:
        ///   • Character names  – 41 bytes  (MAX_CHAR_NAME_LEN 40 + null)
        ///   • Inventory names  – 26 bytes  (LEGACY_MAX_INVENTORY_NAME_LENGTH 25 + null)
        ///   • Dialog options   – 151 bytes (MAXOPTIONLENGTH 150 + null)
        ///   • Mouse cursor names – 11 bytes
        /// </summary>
        private static IEnumerable<string> ScanGameDataSection(byte[] data, long startOffset)
        {
            int[] fixedSlotSizes = { 41, 26, 151, 11 };

            foreach (int slotSize in fixedSlotSizes)
                foreach (string s in FindStringArrays(data, startOffset, slotSize, minCount: 4))
                    yield return s;

            // Also extract variable-length messages (int32 length prefix + text)
            foreach (string s in ExtractLengthPrefixedStrings(data, startOffset))
                yield return s;
        }

        /// <summary>
        /// Looks for runs of <slotSize>-byte fixed-width string slots.
        /// A valid run must have at least <minCount> consecutive valid slots.
        /// </summary>
        private static IEnumerable<string> FindStringArrays(
            byte[] data, long startOffset, int slotSize, int minCount)
        {
            long end = data.Length - slotSize;
            long pos = startOffset;
            int runCount = 0;
            long runStart = -1;
            var pending = new List<string>();

            while (pos <= end)
            {
                if (IsValidFixedString(data, pos, slotSize))
                {
                    if (runStart < 0) runStart = pos;
                    runCount++;
                    string s = ReadNullTerminated(data, pos, slotSize);
                    if (!string.IsNullOrEmpty(s)) pending.Add(s);
                    pos += slotSize;
                }
                else
                {
                    if (runCount >= minCount)
                    {
                        foreach (string s in pending)
                            if (IsTranslatableString(s))
                                yield return s;
                    }
                    pending.Clear();
                    runCount = 0;
                    runStart = -1;
                    pos++;
                }
            }

            // flush last run
            if (runCount >= minCount)
                foreach (string s in pending)
                    if (IsTranslatableString(s))
                        yield return s;
        }

        /// <summary>
        /// Extracts strings stored with a 4-byte little-endian length prefix
        /// (used for global messages in AGS game data).
        /// Only reads within a bounded region to avoid scanning binary code sections.
        /// </summary>
        private static IEnumerable<string> ExtractLengthPrefixedStrings(byte[] data, long startOffset)
        {
            long pos = startOffset;
            // Only scan up to 5 MB past the game-data signature — beyond that is
            // room/audio/sprite data, not text
            long end = Math.Min(data.Length - 4, startOffset + 5_000_000);

            while (pos < end)
            {
                int len = ReadInt32LE(data, pos);
                if (len > 4 && len < 1000 && pos + 4 + len <= data.Length)
                {
                    // Make sure there are no embedded null bytes (would indicate a false hit)
                    bool hasNull = false;
                    for (int i = 0; i < len; i++)
                        if (data[pos + 4 + i] == 0) { hasNull = true; break; }

                    if (!hasNull)
                    {
                        string s = Encoding.Latin1.GetString(data, (int)(pos + 4), len);
                        if (IsTranslatableString(s))
                        {
                            yield return s;
                            pos += 4 + len;
                            continue;
                        }
                    }
                }
                pos++;
            }
        }

        // ------------------------------------------------------------------ //
        //  Helpers                                                            //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns true if <slotSize> bytes starting at <offset> look like a
        /// valid fixed-width null-padded string slot: printable chars followed
        /// by at least one 0x00 byte.
        /// </summary>
        private static bool IsValidFixedString(byte[] data, long offset, int slotSize)
        {
            bool foundNull = false;
            bool foundPrintable = false;
            for (int i = 0; i < slotSize; i++)
            {
                byte b = data[offset + i];
                if (b == 0x00)
                {
                    foundNull = true;
                }
                else if (foundNull)
                {
                    // Non-null byte after null → not a clean null-padded slot
                    return false;
                }
                else if (b >= 0x20 && b <= 0x7E || b >= 0xA0) // printable Latin-1
                {
                    foundPrintable = true;
                }
                else
                {
                    return false; // control char other than null
                }
            }
            return foundNull && foundPrintable;
        }

        private static string ReadNullTerminated(byte[] data, long offset, int maxLen)
        {
            int len = 0;
            while (len < maxLen && data[offset + len] != 0x00)
                len++;
            return Encoding.Latin1.GetString(data, (int)offset, len);
        }

        /// <summary>
        /// Determines whether a string is likely a user-visible translatable text
        /// (as opposed to an internal function name, file path, or code symbol).
        /// </summary>
        private static bool IsTranslatableString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.Length < 2) return false;

            // Reject strings with embedded null bytes (contaminated reads)
            if (s.IndexOf('\0') >= 0) return false;

            // Skip known internal markers
            foreach (string pfx in InternalPrefixes)
                if (s.StartsWith(pfx, StringComparison.Ordinal)) return false;

            if (KnownInternalStrings.Contains(s)) return false;

            // Skip pure identifiers: no spaces, all alphanumeric/underscore
            bool hasSpace = s.IndexOf(' ') >= 0;
            bool hasPunctuation = false;
            int printableCount = 0;
            int letterCount = 0;

            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) { printableCount++; letterCount++; }
                else if (c == ' ' || c == ',' || c == '.' || c == '!' || c == '?' ||
                         c == '\'' || c == '"' || c == '-' || c == ':' || c == ';')
                { printableCount++; hasPunctuation = true; }
                else if (c >= 0x20 && c <= 0x7E || (int)c >= 0xA0) { printableCount++; }
            }

            // Must be mostly printable
            if ((double)printableCount / s.Length < 0.85) return false;

            // A single word (no space, no punctuation) is likely an identifier
            // unless it starts with an uppercase letter (could be a name) and
            // has reasonable length
            if (!hasSpace && !hasPunctuation)
            {
                // Accept short proper nouns / names (e.g. inventory item "Wrench")
                if (s.Length <= 25 && char.IsUpper(s[0])) return true;
                // Reject long identifiers and camelCase-looking strings
                return false;
            }

            return true;
        }

        /// <summary>Byte-level KMP search; returns all start positions of <pattern> in <data>.</summary>
        private static IEnumerable<long> FindAll(byte[] data, byte[] pattern)
        {
            // Build KMP failure function
            int[] fail = new int[pattern.Length];
            fail[0] = 0;
            for (int i = 1, j = 0; i < pattern.Length; i++)
            {
                while (j > 0 && pattern[i] != pattern[j]) j = fail[j - 1];
                if (pattern[i] == pattern[j]) j++;
                fail[i] = j;
            }

            // Search
            for (long i = 0, j = 0; i < data.Length; i++)
            {
                while (j > 0 && data[i] != pattern[j]) j = fail[j - 1];
                if (data[i] == pattern[j]) j++;
                if (j == pattern.Length)
                {
                    yield return i - pattern.Length + 1;
                    j = fail[j - 1];
                }
            }
        }

        private static int IndexOfByte(byte[] data, byte value, long start, long end)
        {
            for (long i = start; i < end; i++)
                if (data[i] == value) return (int)i;
            return -1;
        }

        private static int ReadInt32LE(byte[] data, long offset) =>
            data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
    }

    /// <summary>
    /// Structurally parses the AGS CLIB v30 container to locate game28.dta, then
    /// reads inventory names, character display names, global messages, and dialog
    /// option texts from the binary game-data format.
    /// </summary>
    public static class AgsGameDataParser
    {
        // Exact struct sizes from AGS source (all fields are little-endian)
        private const int InvItemStructSize = 68;  // name(25)+pad(3)+pic(4)+cursorPic(4)+hotx(4)+hoty(4)+reserved[5](20)+flags(1)+pad(3)
        private const int CursorStructSize  = 24;  // pic(4)+hotx(2)+hoty(2)+view(2)+name(10)+flags(1)+pad(3) — kept for reference
        private const int CharStructSize    = 780; // see inline comment in ReadCharacters
        private const int CharNameOffset    = 718; // byte offset of name[40] within CharacterInfo struct

        // ------------------------------------------------------------------ //
        //  Public API                                                         //
        // ------------------------------------------------------------------ //

        public static List<string> ExtractFromFile(string filename)
        {
            var results = new List<string>();
            try
            {
                var assets = ReadClibAssets(filename);
                if (!assets.TryGetValue("game28.dta", out var gameDataEntry)) return results;

                byte[] gameData;
                using (var fs = File.OpenRead(filename))
                {
                    fs.Seek(gameDataEntry.offset, SeekOrigin.Begin);
                    int sz = (int)Math.Min(gameDataEntry.size, (long)int.MaxValue);
                    gameData = new byte[sz];
                    int bytesRead = fs.Read(gameData, 0, sz);
                    if (bytesRead < sz) return results; // truncated
                }

                ExtractFromGameData(gameData, results);
                ExtractFromRoomFiles(filename, assets, results);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AgsGameDataParser] Error: {ex.Message}");
            }
            return results;
        }

        // ------------------------------------------------------------------ //
        //  CLIB v30 container parser                                          //
        // ------------------------------------------------------------------ //

        private static Dictionary<string, (long offset, long size)> ReadClibAssets(string filename)
        {
            var assets = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
            using var fs  = File.OpenRead(filename);
            using var br  = new BinaryReader(fs, Encoding.Latin1, leaveOpen: true);
            if (fs.Length < 6) return assets;

            byte[] headSig = { (byte)'C', (byte)'L', (byte)'I', (byte)'B', 0x1A };
            byte[] fileHead = br.ReadBytes(5);

            if (!fileHead.SequenceEqual(headSig))
            {
                // Exe-wrapper format: locate CLIB start via 13-byte tail signature.
                byte[] tailSig = { (byte)'C',(byte)'L',(byte)'I',(byte)'B',
                                   0x01, 0x02, 0x03, 0x04,
                                   (byte)'S',(byte)'I',(byte)'G',(byte)'E', 0x00 };
                if (fs.Length < tailSig.Length + 8) return assets;
                fs.Seek(-(tailSig.Length + 8), SeekOrigin.End);
                long clibStartOffset = br.ReadInt64();
                byte[] maybeTail = br.ReadBytes(tailSig.Length);
                if (!maybeTail.SequenceEqual(tailSig)) return assets;
                fs.Seek(clibStartOffset, SeekOrigin.Begin);
                fileHead = br.ReadBytes(5);
                if (!fileHead.SequenceEqual(headSig)) return assets;
            }

            byte version = br.ReadByte();
            if (version == 30)
                ReadClibV30(fs, br, assets);
            else if (version == 20 || version == 21)
                ReadClibV20(fs, br, assets);
            else if (version == 10 || version == 6)
                ReadClibV10(fs, br, assets, version);
            // else: unknown version, return empty (heuristic SCOM scanner will still work)

            return assets;
        }

        // CLIB v30: null-terminated names, int64 offsets/sizes
        private static void ReadClibV30(FileStream fs, BinaryReader br,
            Dictionary<string, (long, long)> assets)
        {
            br.ReadByte();  // file_index
            br.ReadInt32(); // reserved

            int libFileCount = br.ReadInt32();
            if (libFileCount < 0 || libFileCount > 1000) return;
            for (int i = 0; i < libFileCount; i++)
                while (fs.Position < fs.Length && br.ReadByte() != 0) {}

            int assetCount = br.ReadInt32();
            if (assetCount < 0 || assetCount > 100_000) return;

            for (int i = 0; i < assetCount; i++)
            {
                var nameBytes = new List<byte>(32);
                byte b;
                while (fs.Position < fs.Length && (b = br.ReadByte()) != 0)
                    nameBytes.Add(b);
                string assetName = Encoding.Latin1.GetString(nameBytes.ToArray());

                br.ReadByte();               // libUid
                long assetOffset = br.ReadInt64();
                long assetSize   = br.ReadInt64();

                assets[assetName] = (assetOffset, assetSize);
            }
        }

        // CLIB v20/v21: char[15] password, byte numLibFiles, char[20] filenames,
        //               int16 numAssets, char[25] names[], byte libIdx[], int32 offset[], int32 size[]
        private static void ReadClibV20(FileStream fs, BinaryReader br,
            Dictionary<string, (long, long)> assets)
        {
            br.ReadBytes(15); // password (unused)

            int numLibFiles = br.ReadByte();
            if (numLibFiles < 0 || numLibFiles > 100) return;
            for (int i = 0; i < numLibFiles; i++)
                br.ReadBytes(20); // lib filename (fixed 20 chars, ignore)

            int numAssets = br.ReadInt16();
            if (numAssets < 0 || numAssets > 100_000) return;

            var names      = new string[numAssets];
            var libIndices = new byte[numAssets];
            var offsets    = new long[numAssets];
            var sizes      = new long[numAssets];

            for (int i = 0; i < numAssets; i++)
            {
                byte[] nameBuf = br.ReadBytes(25); // fixed 25-char name, null-padded
                names[i] = ReadNullTerminated(nameBuf);
            }
            for (int i = 0; i < numAssets; i++)
                libIndices[i] = br.ReadByte();
            for (int i = 0; i < numAssets; i++)
                offsets[i] = br.ReadInt32(); // 32-bit absolute offset
            for (int i = 0; i < numAssets; i++)
                sizes[i] = br.ReadInt32();   // 32-bit size

            for (int i = 0; i < numAssets; i++)
                if (!string.IsNullOrEmpty(names[i]))
                    assets[names[i]] = (offsets[i], sizes[i]);
        }

        // CLIB v6/v10: int32 numFiles, char[13] names[], int32 offsets[], int32 dataSize
        // Offsets in v10 are from the start of the data block that follows the header.
        private static void ReadClibV10(FileStream fs, BinaryReader br,
            Dictionary<string, (long, long)> assets, byte version)
        {
            int numFiles = br.ReadInt32();
            if (numFiles < 0 || numFiles > 10_000) return;

            int nameLen = version == 6 ? 13 : 13; // both use 13-char names
            var names   = new string[numFiles];
            var offsets = new int[numFiles];

            for (int i = 0; i < numFiles; i++)
            {
                byte[] nameBuf = br.ReadBytes(nameLen);
                names[i] = ReadNullTerminated(nameBuf);
            }
            for (int i = 0; i < numFiles; i++)
                offsets[i] = br.ReadInt32();

            int dataSize    = br.ReadInt32(); // total data area size (unused here)
            long dataStart  = fs.Position;    // data immediately follows header

            for (int i = 0; i < numFiles; i++)
            {
                if (string.IsNullOrEmpty(names[i])) continue;
                long size = (i + 1 < numFiles) ? offsets[i + 1] - offsets[i] : dataSize - offsets[i];
                assets[names[i]] = (dataStart + offsets[i], Math.Max(0, size));
            }
        }

        private static string ReadNullTerminated(byte[] buf)
        {
            int len = 0;
            while (len < buf.Length && buf[len] != 0) len++;
            return Encoding.Latin1.GetString(buf, 0, len);
        }

        // ------------------------------------------------------------------ //
        //  game28.dta structural parser                                       //
        // ------------------------------------------------------------------ //

        private static void ExtractFromGameData(byte[] gameData, List<string> results)
        {
            byte[] sig    = Encoding.Latin1.GetBytes("Adventure Creator Game File v2");
            int    sigPos = IndexOfBytes(gameData, sig, 0);
            if (sigPos < 0) return;

            using var ms = new MemoryStream(gameData);
            using var br = new BinaryReader(ms, Encoding.Latin1, leaveOpen: true);

            // After the 30-byte signature: int32 dataVer, int32 verLen, <verLen bytes>, (v3.x) int32 extra
            ms.Position = sigPos + sig.Length;
            int dataVer = br.ReadInt32(); // game data version number (kGameVersion_* enum)
            int verLen  = br.ReadInt32(); // version string length (e.g. 8 for "3.6.0.50")
            ms.Position += verLen;        // skip version string
            if (dataVer > 272)            // v3.x: one extra int32
                br.ReadInt32();

            Console.Error.WriteLine($"[AgsGameDataParser] dataVer={dataVer}");

            // ---- GameSetupStructBase ----------------------------------------
            ms.Position += 52;   // gamename[50] + 2 bytes padding
            ms.Position += 400;  // options[100] * int32
            ms.Position += 256;  // paluses[256]
            ms.Position += 1024; // defpal[256] (Allegro RGB = r,g,b,filler each 1 byte)

            int numviews      = br.ReadInt32();
            int numcharacters = br.ReadInt32();
            br.ReadInt32(); // playercharacter
            br.ReadInt32(); // totalscore
            int numinvitems   = br.ReadUInt16();
            br.ReadInt16(); // 2 bytes padding after numinvitems
            int numdialog     = br.ReadInt32();
            br.ReadInt32(); // numdlgmessage
            int numfonts      = br.ReadInt32();
            br.ReadInt32(); // color_depth
            br.ReadInt32(); // target_win
            br.ReadInt32(); // dialog_bullet
            br.ReadInt16(); // hotdot
            br.ReadInt16(); // hotdotouter
            br.ReadInt32(); // uniqueid
            br.ReadInt32(); // numgui
            int numcursors    = br.ReadInt32();
            int resolutionType = br.ReadInt32();
            if (resolutionType == 1 && dataVer >= 60) // kGameResolution_Custom=1, kGameVersion_330≈60
            {
                br.ReadInt32(); // custom_screen_width
                br.ReadInt32(); // custom_screen_height
            }
            br.ReadInt32(); // default_lipsync_frame
            br.ReadInt32(); // invhotdotsprite
            ms.Position += 64;  // reserved[16] * int32
            br.ReadInt32(); // ExtensionOffset

            // HasMessages[500]: non-zero means there's a message for that slot
            int[] hasMessages = new int[500];
            for (int i = 0; i < 500; i++) hasMessages[i] = br.ReadInt32();

            int hasWordsDict = br.ReadInt32();
            br.ReadInt32(); // globalscript_ptr (dummy)
            int hasCCScript  = br.ReadInt32(); // HasCCScript (boolean/count)
            br.ReadInt32(); // extra field (chars_ptr dummy or new field in v3.6.x)

            // Sanity checks before proceeding
            if (numcharacters < 0 || numcharacters > 10000 ||
                numinvitems   < 0 || numinvitems   > 10000 ||
                numdialog     < 0 || numdialog     > 10000 ||
                numfonts      < 0 || numfonts      > 1000  ||
                numcursors    < 0 || numcursors    > 1000  ||
                numviews      < 0 || numviews      > 100_000)
            {
                Console.Error.WriteLine($"[AgsGameDataParser] Sanity fail: chars={numcharacters} inv={numinvitems} dialogs={numdialog} fonts={numfonts} cursors={numcursors} views={numviews}");
                return;
            }
            Console.Error.WriteLine($"[AgsGameDataParser] chars={numcharacters} inv={numinvitems} dialogs={numdialog} fonts={numfonts} cursors={numcursors} views={numviews}");

            // ---- read_savegame_info (data_ver > 272) ------------------------
            // In AGS v3.6.x an 8-byte header (int32=0 + int32=1) precedes the guid field.
            if (dataVer > 272)
                ms.Position += 8 + 40 + 20 + 50; // header(8) + guid(40) + ext(20) + folder(50)

            // ---- read_font_infos (data_ver >= 350) --------------------------
            if (dataVer >= 350)
                ms.Position += (long)numfonts * 20; // flags+size+outline+yoffset+linespacing per font

            // ---- ReadSpriteFlags (data_ver >= 256) --------------------------
            if (dataVer >= 256)
            {
                int spriteFlagCount = br.ReadInt32();
                if (spriteFlagCount < 0 || spriteFlagCount > 1_000_000)
                {
                    Console.Error.WriteLine($"[AgsGameDataParser] Bad spriteFlagCount={spriteFlagCount}");
                    return;
                }
                ms.Position += spriteFlagCount; // one flag byte per sprite
            }

            // ---- ReadInvInfo: numinvitems * 68 bytes ------------------------
            int invNamesBefore = results.Count;
            for (int i = 0; i < numinvitems; i++)
            {
                long slotStart = ms.Position;
                byte[] nameBuf = br.ReadBytes(25); // LEGACY_MAX_INVENTORY_NAME_LENGTH
                string name = ReadFixedString(nameBuf, 25);
                ms.Position = slotStart + InvItemStructSize;
                if (!string.IsNullOrWhiteSpace(name))
                    results.Add(name);
            }
            Console.Error.WriteLine($"[AgsGameDataParser] Inventory names found: {results.Count - invNamesBefore}");

            // ---- Dialog option texts (search-based) -------------------------
            // Dialog topics are NOT accessed via sequential parsing (too many unknown
            // version-specific sections in between). Instead, we search the last portion
            // of the game data where dialog topics reside.
            int dialogOptBefore = results.Count;
            FindAndExtractDialogs(gameData, numdialog, results);
            Console.Error.WriteLine($"[AgsGameDataParser] Dialog options found: {results.Count - dialogOptBefore}");
        }

        // ------------------------------------------------------------------ //
        //  Helpers                                                            //
        // ------------------------------------------------------------------ //

        /// <summary>Skips a single SCOM script block starting at the current stream position.</summary>
        private static bool SkipScomBlock(BinaryReader br, MemoryStream ms)
        {
            if (ms.Position + 20 > ms.Length) return false;
            br.ReadBytes(4); // "SCOM" magic
            br.ReadInt32();  // version (low byte is 'Z' or 'Y')
            int globalDataSize = br.ReadInt32();
            int codeSize       = br.ReadInt32();
            int stringsSize    = br.ReadInt32();
            if (globalDataSize < 0 || globalDataSize > 50_000_000 ||
                codeSize       < 0 || codeSize       > 10_000_000 ||
                stringsSize    < 0 || stringsSize     > 10_000_000) return false;
            ms.Position += globalDataSize + (long)codeSize * 4 + stringsSize;
            return true;
        }

        private static string ReadFixedString(byte[] buf, int maxLen)
        {
            int len = 0;
            while (len < maxLen && len < buf.Length && buf[len] != 0) len++;
            return Encoding.Latin1.GetString(buf, 0, len);
        }

        /// <summary>
        /// Finds the dialog topics section by scanning backward from the end of game data,
        /// then extracts all non-empty option texts.
        /// Each DialogTopic is 4696 bytes; option text slots are 150 bytes with text
        /// starting after leading null bytes (AGS v3.x encoding format).
        /// </summary>
        private static void FindAndExtractDialogs(byte[] gameData, int numDialogs, List<string> results)
        {
            if (numDialogs <= 0) return;

            const int TopicSize  = 4696;
            const int SlotSize   = 150;
            const int SlotsPerTopic = 30;
            const int MinTextLen = 3;

            long totalDialogBytes = (long)numDialogs * TopicSize;
            // Dialogs are near the end; search up to 3 MB before the expected end position
            long searchStart = Math.Max(0, gameData.Length - totalDialogBytes - 3_000_000L);
            long searchEnd   = Math.Max(0, gameData.Length - totalDialogBytes + 50_000L);

            long foundAt = -1;
            for (long candidate = searchEnd; candidate >= searchStart; candidate--)
            {
                if (candidate + totalDialogBytes > gameData.Length) continue;

                // Quick check: first 3 topics must each have ≥2 readable option texts
                int validTopics = 0;
                for (int d = 0; d < Math.Min(numDialogs, 3); d++)
                {
                    int textsInTopic = 0;
                    for (int i = 0; i < SlotsPerTopic; i++)
                    {
                        long slotPos = candidate + (long)d * TopicSize + (long)i * SlotSize;
                        // Skip leading null bytes to find text start
                        int offset = 0;
                        while (offset < SlotSize && gameData[slotPos + offset] == 0) offset++;
                        if (offset >= SlotSize - MinTextLen) continue;
                        // Count printable ASCII characters in first 10 bytes of text
                        int printable = 0;
                        for (int k = 0; k < Math.Min(10, SlotSize - offset); k++)
                        {
                            byte b = gameData[slotPos + offset + k];
                            if (b == 0) break;
                            if (b >= 0x20 && b < 0x80) printable++;
                        }
                        if (printable >= MinTextLen) textsInTopic++;
                    }
                    if (textsInTopic >= 2) validTopics++;
                }

                if (validTopics >= 2)
                {
                    foundAt = candidate;
                    break;
                }
            }

            if (foundAt < 0)
            {
                Console.Error.WriteLine("[AgsGameDataParser] Dialog section not found");
                return;
            }

            // Extract option texts from all dialog topics
            for (int d = 0; d < numDialogs; d++)
            {
                for (int i = 0; i < SlotsPerTopic; i++)
                {
                    long slotPos = foundAt + (long)d * TopicSize + (long)i * SlotSize;
                    if (slotPos + SlotSize > gameData.Length) break;

                    // Skip leading null bytes
                    int offset = 0;
                    while (offset < SlotSize && gameData[slotPos + offset] == 0) offset++;
                    if (offset >= SlotSize) continue;

                    // Read null-terminated text from offset
                    int textEnd = offset;
                    while (textEnd < SlotSize && gameData[slotPos + textEnd] != 0) textEnd++;
                    if (textEnd - offset < MinTextLen) continue;

                    string text = Encoding.Latin1.GetString(gameData, (int)slotPos + offset, textEnd - offset).Trim();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length >= MinTextLen)
                        results.Add(text);
                }
            }
        }

        // ------------------------------------------------------------------ //
        //  Room file text extraction                                          //
        // ------------------------------------------------------------------ //

        // Matches camelCase AGS script identifiers: starts lowercase, contains uppercase
        // e.g. "hHotspot0", "hExit", "cEli", "oTable", "iKnife"
        private static readonly Regex ScriptIdentifierRe =
            new(@"^[a-z][a-zA-Z0-9]*[A-Z][a-zA-Z0-9]*$", RegexOptions.Compiled);

        // Generic auto-generated AGS placeholder names
        private static readonly Regex PlaceholderRe =
            new(@"^(No hotspot|Hotspot \d+|Object \d+|Region \d+|Walkable area \d+|Walk-behind area \d+)$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static bool IsRoomDisplayText(string s)
        {
            if (s.Length < 2) return false;
            if (ScriptIdentifierRe.IsMatch(s)) return false;
            if (PlaceholderRe.IsMatch(s)) return false;
            return true;
        }

        /// <summary>
        /// Scans a room main block for length-prefixed strings (int32 len + UTF-8/Latin1 text).
        /// AGS v3.x stores hotspot names, object names and other room text in this format.
        /// </summary>
        private static void ScanRoomMainBlock(byte[] data, List<string> results)
        {
            const int MinLen = 2;
            const int MaxLen = 200;

            for (int pos = 0; pos + 5 <= data.Length; pos++)
            {
                int length = BitConverter.ToInt32(data, pos);
                if (length < MinLen || length > MaxLen) continue;
                if (pos + 4 + length > data.Length) continue;

                bool valid = true;
                for (int k = 0; k < length; k++)
                {
                    byte b = data[pos + 4 + k];
                    // Allow printable ASCII and Latin1 extended; reject nulls and control chars
                    if (b == 0 || b < 0x20 || (b > 0x7e && b < 0xa0))
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid) continue;

                string text = Encoding.Latin1.GetString(data, pos + 4, length);
                if (IsRoomDisplayText(text))
                    results.Add(text);
            }
        }

        private static void ExtractFromRoomFiles(string filename,
            Dictionary<string, (long offset, long size)> assets, List<string> results)
        {
            int roomCount   = 0;
            int stringCount = 0;
            try
            {
                using var fs = File.OpenRead(filename);
                using var br = new BinaryReader(fs, Encoding.Latin1, leaveOpen: true);

                foreach (var kvp in assets)
                {
                    if (!kvp.Key.EndsWith(".crm", StringComparison.OrdinalIgnoreCase)) continue;

                    (long roomOffset, long roomSize) = kvp.Value;
                    if (roomSize < 7) continue;

                    fs.Seek(roomOffset, SeekOrigin.Begin);
                    br.ReadInt16(); // room format version

                    long roomEnd = roomOffset + roomSize;
                    while (fs.Position + 5 <= roomEnd)
                    {
                        byte blockType = br.ReadByte();
                        if (blockType == 0) break; // end-of-blocks marker

                        int blockSize = br.ReadInt32();
                        if (blockSize < 0 || fs.Position + blockSize > roomEnd) break;

                        if (blockType == 1) // main room data block
                        {
                            byte[] blockData = br.ReadBytes(blockSize);
                            int before = results.Count;
                            ScanRoomMainBlock(blockData, results);
                            stringCount += results.Count - before;
                        }
                        else
                        {
                            fs.Seek(blockSize, SeekOrigin.Current);
                        }
                    }
                    roomCount++;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AgsGameDataParser] Room extraction error: {ex.Message}");
            }
            Console.Error.WriteLine($"[AgsGameDataParser] Rooms scanned: {roomCount}, room strings: {stringCount}");
        }

        private static int IndexOfBytes(byte[] data, byte[] pattern, int startPos)
        {
            int limit = data.Length - pattern.Length;
            for (int i = startPos; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                    if (data[i + j] != pattern[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }
    }
}
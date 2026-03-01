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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;


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

        /// <summary>
        /// Parse a TRS file for AGS
        /// </summary>
        /// <param name="filename">Input filename</param>
        /// <returns>Dictionary with Translation entries</returns>
        public static Dictionary<string, string> ParseTRS_Translation(string filename)
        {
            string[] list = File.ReadAllLines(filename);
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

                //Padding not sure what exactly this is used for
                byte[] paddingBytes = { 0x02, 0x00, 0x00, 0x00, 0x16, 0x00, 0x00, 0x00, };
                fs.Write(paddingBytes, 0, paddingBytes.Length);

                //Write GameUID. Important or Translation does not load!
                string sGameUID = info.GameUID;
                int decAgain = int.Parse(sGameUID, System.Globalization.NumberStyles.HexNumber);
                byte[] bGameUID = BitConverter.GetBytes(SwapEndianness(decAgain));
                fs.Write(bGameUID, 0, bGameUID.Length);

                //Encrypt and write the Title
                string GameTitle = info.GameTitle + "\0";
                byte[] bGameTitle = Encoding.UTF8.GetBytes(GameTitle);
                char[] cGameTitle = new char[GameTitle.Length];
                GameTitle.CopyTo(0, cGameTitle, 0, GameTitle.Length);
                EncryptText(cGameTitle);
                //Write GameTitle Length
                byte[] bGameTitleLength = BitConverter.GetBytes(bGameTitle.Length);
                fs.Write(bGameTitleLength, 0, bGameTitleLength.Length);
                //Write the encrypted GameTitle
                CharToByte(cGameTitle, bGameTitle);
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
                            byte[] bEntry1 = Encoding.UTF8.GetBytes(entry1);

                            //Write string length
                            byte[] bEntry1Length = BitConverter.GetBytes(bEntry1.Length);
                            fs.Write(bEntry1Length, 0, bEntry1Length.Length);

                            //Write Encrypted Text
                            char[] cEntry1 = new char[bEntry1.Length];
                            Array.Copy(bEntry1, cEntry1, bEntry1.Length);
                            EncryptText(cEntry1);
                            CharToByte(cEntry1, bEntry1);

                            byte[] btestEntry1 = Encoding.ASCII.GetBytes(cEntry1);
                            fs.Write(bEntry1, 0, bEntry1.Length);

                            //Entry2
                            string entry2 = pair.Value + "\0";
                            byte[] bEntry2 = Encoding.UTF8.GetBytes(entry2);

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
            var gameDataStrings = ExtractGameDataStrings(fileBytes).ToList();
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
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AGSTools
{
    /// <summary>
    /// A compiled AGS script ("SCOM" block). Port of ccScript::Read (Common/script/cc_script.cpp),
    /// keeping only what text extraction needs: string literals, code and fixups.
    /// </summary>
    internal sealed class CompiledScript
    {
        private static readonly byte[] Signature = "SCOM"u8.ToArray();
        private const int VersionSections = 83;
        private const int VersionCurrent = 90;
        private const uint EndFileSig = 0xbeefcafe;

        // VM opcodes and fixup types used by the voice-line analysis
        private const int OpLitToReg = 6;
        private const int OpRegToReg = 3;
        private const int OpPushReg = 29;
        private const int OpPushReal = 34;
        private const int OpCreateString = 64;
        private const int OpCall = 23;
        private const int OpCallExt = 33;
        private const int OpLineNum = 36;
        private const int OpCallAs = 37;
        private const int OpCallObj = 45;
        private const int RegMAR = 2;
        private const int RegAX = 3;
        private const byte FixupString = 3;
        private const byte FixupImport = 4;

        // Argument count per opcode (index = opcode, -1 = unknown)
        private static readonly sbyte[] OpArgCounts =
        {
            -1, 2, 2, 2, 2, 0, 2, 1, 1, 2,
             2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
             2, 2, 2, 1, 1, 1, 1, 1, 1, 1,
             1, 1, 2, 1, 1, 1, 1, 1, 1, 1,
             2, 2, 1, 2, 2, 1, 2, 1, 1, 0,
             1, 1, 0, 2, 2, 2, 2, 2, 2, 2,
             2, 2, 2, 1, 1, 2, 2, 1, 0, 0,
        };

        private int[] _code = Array.Empty<int>();
        private byte[] _fixupTypes = Array.Empty<byte>();
        private int[] _fixups = Array.Empty<int>();
        private readonly List<string> _imports = new();
        private readonly List<(string Name, int Offset)> _sections = new();

        /// <summary>Script name, taken from the last section name (e.g. "GlobalScript.asc").</summary>
        public string Name { get; private set; } = "";

        /// <summary>String literals in the order of the string table, keyed by their table offset.</summary>
        public IReadOnlyList<(int Offset, string Text)> Strings { get; private set; } = Array.Empty<(int, string)>();

        /// <summary>Reads a script at the current reader position.</summary>
        public static CompiledScript Read(AgsReader reader)
        {
            long start = reader.Position;
            if (!reader.ReadBytes(4).AsSpan().SequenceEqual(Signature))
                throw new AgsFormatException($"Script signature not found at offset {start}.");

            int version = reader.ReadInt32();
            if (version > VersionCurrent)
                throw new AgsFormatException($"Unsupported script version {version} at offset {start}.");

            var script = new CompiledScript();
            int globalDataSize = reader.ReadCount("script global data", 100_000_000);
            int codeSize = reader.ReadCount("script code", 50_000_000);
            int stringsSize = reader.ReadCount("script strings", 50_000_000);

            reader.Skip(globalDataSize);

            byte[] codeBytes = reader.ReadBytes(codeSize * 4);
            script._code = new int[codeSize];
            Buffer.BlockCopy(codeBytes, 0, script._code, 0, codeBytes.Length);

            script.Strings = SplitStringTable(reader, reader.ReadBytes(stringsSize));

            int fixupCount = reader.ReadCount("script fixup", 10_000_000);
            script._fixupTypes = reader.ReadBytes(fixupCount);
            byte[] fixupBytes = reader.ReadBytes(fixupCount * 4);
            script._fixups = new int[fixupCount];
            Buffer.BlockCopy(fixupBytes, 0, script._fixups, 0, fixupBytes.Length);

            int importCount = reader.ReadCount("script import", 10_000_000);
            for (int i = 0; i < importCount; i++)
                script._imports.Add(reader.ReadCString());

            int exportCount = reader.ReadCount("script export", 10_000_000);
            for (int i = 0; i < exportCount; i++)
            {
                reader.ReadCString();
                reader.ReadInt32(); // address
            }

            if (version >= VersionSections)
            {
                int sectionCount = reader.ReadCount("script section", 1_000_000);
                for (int i = 0; i < sectionCount; i++)
                {
                    script.Name = reader.ReadCString();
                    script._sections.Add((script.Name, reader.ReadInt32()));
                }
            }

            if (reader.ReadUInt32() != EndFileSig)
                throw new AgsFormatException($"Script end signature not found (script at offset {start}).");

            return script;
        }

        private static List<(int, string)> SplitStringTable(AgsReader reader, byte[] table)
        {
            var result = new List<(int, string)>();
            int offset = 0;
            while (offset < table.Length)
            {
                int end = Array.IndexOf(table, (byte)0, offset);
                if (end < 0)
                    end = table.Length;
                result.Add((offset, reader.TextEncoding.GetString(table, offset, end - offset)));
                offset = end + 1;
            }
            return result;
        }

        /// <summary>
        /// Finds string literals that are passed together with a literal voice number to a
        /// function, i.e. the bytecode pattern of wrapper calls like <c>cEgo.GSay(12, "text")</c>:
        /// <code>
        ///   LITTOREG AX, voiceNum   (plain literal, no fixup)
        ///   PUSHREG  AX
        ///   LITTOREG AX, STR[ofs]   (string fixup)
        ///   [CREATESTRING AX]
        ///   PUSHREG  AX
        /// </code>
        /// Such wrappers build "&amp;N text" at runtime, which is the key the engine looks up.
        /// </summary>
        public IEnumerable<(string Text, int VoiceNumber)> FindVoicedStrings()
        {
            if (_code.Length == 0)
                yield break;

            var stringsByOffset = Strings.ToDictionary(s => s.Offset, s => s.Text);
            var fixupsByAddress = new Dictionary<int, byte>(_fixups.Length);
            for (int i = 0; i < _fixups.Length; i++)
                fixupsByAddress[_fixups[i]] = _fixupTypes[i];

            var instrs = DecodeInstructions();
            for (int i = 0; i < instrs.Count; i++)
            {
                var (_, op, a0, _) = instrs[i];
                if (!IsPushAX(op, a0)) continue;

                // LITTOREG AX = STR[offset], skipping CREATESTRING and unknown words
                int j = i - 1;
                while (j >= 0 && (instrs[j].Op == -1 || instrs[j].Op == OpCreateString)) j--;
                if (j < 0) continue;
                var (pj, opj, a0j, a1j) = instrs[j];
                if (opj != OpLitToReg || a0j != RegAX) continue;
                if (!fixupsByAddress.TryGetValue(pj + 2, out byte ftype) || ftype != FixupString) continue;
                if (!stringsByOffset.TryGetValue(a1j, out string? text) || string.IsNullOrWhiteSpace(text)) continue;

                // PUSH AX of the previous argument
                int k = j - 1;
                while (k >= 0 && (instrs[k].Op == -1 || instrs[k].Op == OpCreateString || instrs[k].Op == OpRegToReg)) k--;
                if (k < 0 || !IsPushAX(instrs[k].Op, instrs[k].A0)) continue;

                // LITTOREG AX = <plain integer literal>
                int m = k - 1;
                while (m >= 0 && instrs[m].Op == -1) m--;
                if (m < 0) continue;
                var (pm, opm, a0m, a1m) = instrs[m];
                if (opm != OpLitToReg || a0m != RegAX) continue;
                if (fixupsByAddress.ContainsKey(pm + 2)) continue;
                if (a1m <= 0 || a1m >= 10_000) continue;

                yield return (text, a1m);
            }
        }

        /// <summary>
        /// Finds who says each string literal. A call like <c>cRosa.Say("…")</c> loads the text into AX, points
        /// MAR at the imported character, marks it as the object with CALLOBJ and then calls the method; texts
        /// passed to Display functions get <see cref="Narration"/>. Old games call DisplaySpeech(CHARACTER, "…")
        /// with the character number. Only literals loaded on the same source line as the call count, so a
        /// speaker is never guessed across statements.
        /// </summary>
        public IEnumerable<(string Text, string Speaker)> FindSpeakers(IReadOnlyList<string> characterScriptNames)
        {
            if (_code.Length == 0)
                yield break;

            var stringsByOffset = Strings.ToDictionary(s => s.Offset, s => s.Text);
            var fixupsByAddress = new Dictionary<int, byte>(_fixups.Length);
            for (int i = 0; i < _fixups.Length; i++)
                fixupsByAddress[_fixups[i]] = _fixupTypes[i];
            string? Import(int pos, int index) =>
                fixupsByAddress.TryGetValue(pos + 2, out byte type) && type == FixupImport && index >= 0 && index < _imports.Count
                    ? _imports[index] : null;

            var characters = new HashSet<string>(characterScriptNames.Where(n => n.Length > 0), StringComparer.Ordinal);
            var pending = new List<string>();
            string? mar = null, ax = null, callObject = null;
            int? number = null;
            foreach (var (pos, op, a0, a1) in DecodeInstructions())
            {
                switch (op)
                {
                    case OpLineNum:
                        pending.Clear();
                        mar = ax = callObject = null;
                        number = null;
                        break;
                    case OpLitToReg when a0 == RegAX:
                        ax = Import(pos, a1);
                        if (ax != null)
                            break;
                        if (!fixupsByAddress.TryGetValue(pos + 2, out byte type))
                            number = a1;
                        else if (type == FixupString && stringsByOffset.TryGetValue(a1, out string? text))
                            pending.Add(text);
                        break;
                    case OpLitToReg when a0 == RegMAR:
                        mar = Import(pos, a1);
                        break;
                    case OpCallObj:
                        callObject = mar;
                        break;
                    case OpCall or OpCallExt or OpCallAs:
                        string? speaker = null;
                        if (callObject != null)
                            speaker = characters.Contains(callObject) || callObject == "player" ? callObject : null;
                        else if (op == OpCallExt && ax != null && (ax.StartsWith("DisplaySpeech", StringComparison.Ordinal)
                                     || ax.StartsWith("DisplayThought", StringComparison.Ordinal)))
                            speaker = number is int n && n >= 0 && n < characterScriptNames.Count && characterScriptNames[n].Length > 0
                                ? characterScriptNames[n] : null;
                        else if (op == OpCallExt && ax != null && ax.StartsWith("Display", StringComparison.Ordinal))
                            speaker = Narration;
                        if (speaker != null)
                            foreach (string said in pending)
                                yield return (said, speaker);
                        pending.Clear();
                        callObject = null;
                        number = null;
                        break;
                }
            }
        }

        /// <summary>
        /// The section each string literal is loaded in. Scripts made of several sources name them in sections:
        /// the dialog scripts have one per dialog ("Dialog 12"). Without sections the script name is used.
        /// </summary>
        public IEnumerable<(string Text, string Section)> FindSections()
        {
            var stringsByOffset = Strings.ToDictionary(s => s.Offset, s => s.Text);
            var stringFixups = new HashSet<int>();
            for (int i = 0; i < _fixups.Length; i++)
                if (_fixupTypes[i] == FixupString)
                    stringFixups.Add(_fixups[i]);

            var sections = _sections.OrderBy(s => s.Offset).ToList();
            int next = 0;
            string section = sections.Count > 0 ? sections[0].Name : Name;
            foreach (var (pos, op, a0, a1) in DecodeInstructions())
            {
                while (next < sections.Count && sections[next].Offset <= pos)
                    section = sections[next++].Name;
                if (op == OpLitToReg && stringFixups.Contains(pos + 2) && stringsByOffset.TryGetValue(a1, out string? text))
                    yield return (text, section);
            }
        }

        /// <summary>Speaker of texts shown with Display().</summary>
        public const string Narration = "Display";

        // Engine functions that look up the text they get in the translation
        private static readonly string[] ShowingFunctions =
        {
            "Label::set_Text", "Button::set_Text", "SetLabelText", "SetButtonText",
            "Character::Say", "Character::Think", "Display", "ListBox::AddItem", "ListBox::InsertItemAt",
            "Overlay::CreateTextual", "Overlay::SetText",
        };

        private static readonly Regex FormatSpec = new(@"%[-+ 0#]*\d*(?:\.\d+)?[sdifcxX]");

        // "%s[[%s" or "Score: %d" make a text that needs a translation; "%d / 7" and "%dkey" do not, and
        // "%s" or "&%d %s" give exactly the (translated) part or a voiced line
        private static bool IsTextFormat(string text)
        {
            if (!FormatSpec.IsMatch(text))
                return false;
            string rest = FormatSpec.Replace(text, "");
            if (rest.All(c => c == ' ' || c == '&'))
                return false;
            return text.Contains("%s", StringComparison.Ordinal) || rest.Any(char.IsLetter) && rest.Contains(' ');
        }

        /// <summary>
        /// Finds texts the script puts together at runtime: a <c>String.Format</c> (or 2.x <c>StrFormat</c>) with a format literal
        /// whose result is shown by a function that translates (a label, Say, Display ...). The engine looks
        /// up the finished text, so translating the parts does not help. Formats on lines that call
        /// GetTranslation (the parts are translated by the script) are skipped. <c>ShownBy</c> is the
        /// showing function called within the next few lines, or null if none is.
        /// </summary>
        public IEnumerable<(string Format, int Line, string? ShownBy)> FindRuntimeFormats()
        {
            if (_code.Length == 0)
                yield break;

            const int ShowDistance = 10, TranslateDistance = 3;
            var stringsByOffset = Strings.ToDictionary(s => s.Offset, s => s.Text);
            var fixupsByAddress = new Dictionary<int, byte>(_fixups.Length);
            for (int i = 0; i < _fixups.Length; i++)
                fixupsByAddress[_fixups[i]] = _fixupTypes[i];

            var pending = new List<(string Format, int Line)>();
            string? lastFormatLiteral = null, ax = null;
            int line = 0, lastTranslateLine = -1000;
            foreach (var (pos, op, a0, a1) in DecodeInstructions())
            {
                switch (op)
                {
                    case OpLineNum:
                        line = a0;
                        lastFormatLiteral = null;
                        foreach (var expired in pending.Where(p => line - p.Line > ShowDistance).ToList())
                        {
                            pending.Remove(expired);
                            yield return (expired.Format, expired.Line, null);
                        }
                        break;
                    case OpLitToReg when a0 == RegAX:
                        ax = null;
                        if (!fixupsByAddress.TryGetValue(pos + 2, out byte type))
                            break;
                        if (type == FixupImport && a1 >= 0 && a1 < _imports.Count)
                            ax = _imports[a1];
                        else if (type == FixupString && stringsByOffset.TryGetValue(a1, out string? text) && IsTextFormat(text))
                            lastFormatLiteral = text;
                        break;
                    case OpCallExt when ax != null:
                        if (ax.Contains("GetTranslation", StringComparison.Ordinal))
                            lastTranslateLine = line;
                        else if (ax.StartsWith("String::Format", StringComparison.Ordinal) || ax == "StrFormat")
                        {
                            if (lastFormatLiteral != null && line - lastTranslateLine > TranslateDistance)
                                pending.Add((lastFormatLiteral, line));
                            lastFormatLiteral = null;
                        }
                        else if (ShowingFunctions.Any(f => ax.StartsWith(f, StringComparison.Ordinal)))
                        {
                            foreach (var (format, formatLine) in pending)
                                yield return (format, formatLine, ax.Split('^')[0]);
                            pending.Clear();
                        }
                        break;
                }
            }
            foreach (var (format, formatLine) in pending)
                yield return (format, formatLine, null);
        }

        private static bool IsPushAX(int op, int arg) => (op == OpPushReg || op == OpPushReal) && arg == RegAX;

        private List<(int Pos, int Op, int A0, int A1)> DecodeInstructions()
        {
            var instrs = new List<(int, int, int, int)>(_code.Length / 2);
            for (int ci = 0; ci < _code.Length;)
            {
                int op = _code[ci];
                int argCount = op >= 0 && op < OpArgCounts.Length ? OpArgCounts[op] : -1;
                if (argCount < 0)
                {
                    instrs.Add((ci, -1, 0, 0));
                    ci++;
                    continue;
                }
                int a0 = argCount >= 1 && ci + 1 < _code.Length ? _code[ci + 1] : 0;
                int a1 = argCount >= 2 && ci + 2 < _code.Length ? _code[ci + 2] : 0;
                instrs.Add((ci, op, a0, a1));
                ci += 1 + argCount;
            }
            return instrs;
        }
    }
}

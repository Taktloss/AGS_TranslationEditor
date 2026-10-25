using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Sequential reader for AGS 3.x main game data. Port of ReadGameData() in
    /// Common/game/main_game_file.cpp and the struct readers it calls; data that is not
    /// needed for translation is skipped, but still parsed exactly to stay in sync.
    /// </summary>
    internal sealed class GameDataReader
    {
        public static readonly byte[] Signature = "Adventure Creator Game File v2"u8.ToArray();

        private const int MaxGlobalMessages = 500;
        private const int GameResolutionCustom = 8;
        private const int OptDebugMode = 0;
        private const int OptGameTextEncoding = 49;
        private const int InvItemSize = 68;
        private const int CursorSize = 24;
        private const int CharacterSize = 780;
        private const int CharacterNameOffset = 718;
        private const int ViewFrameSize = 28;
        private const int LipSyncSize = 20 * 50;
        private const int DialogOptionCount = 30;
        private const int DialogOptionLength = 150;
        private const uint GuiMagic = 0xcafebeef;

        private readonly AgsReader _r;
        private readonly GameData _game = new();
        private int _dataVersion;

        // Counts and flags from the base struct
        private int _numViews, _numCharacters, _numInvItems, _numDialogs, _numFonts, _numGuis, _numCursors;
        private readonly int[] _options = new int[100];
        private readonly int[] _hasMessages = new int[MaxGlobalMessages];
        private uint _extensionOffset;
        private bool _hasWordsDict, _hasScripts;

        private GameDataReader(byte[] data)
        {
            _r = new AgsReader(new MemoryStream(data, writable: false), Encoding.Latin1);
        }

        /// <summary>Reads only the header fields (title, unique id, versions, text encoding).</summary>
        public static GameData ReadHeader(byte[] data)
        {
            var reader = new GameDataReader(data);
            reader.ReadSignatureAndVersion();
            reader.ReadBaseStruct();
            reader.ReadExtensionsIfPresent(headerOnly: true);
            return reader._game;
        }

        /// <summary>
        /// Reads all translation-relevant game data. Format errors after the header do not throw;
        /// they are reported in <see cref="GameData.Error"/> together with everything read so far.
        /// </summary>
        public static GameData Read(byte[] data)
        {
            var reader = new GameDataReader(data);
            reader.ReadSignatureAndVersion();
            if (reader._dataVersion <= GameDataVersion.V272)
                throw new AgsFormatException($"Game data version {reader._dataVersion} ({reader._game.EngineVersion}, AGS 2.x) is not supported by the structural reader.");
            reader.ReadBaseStruct();

            try
            {
                reader.ReadSequentialData();
            }
            catch (AgsFormatException ex)
            {
                reader._game.Error = ex.Message;
            }

            // 3.6+ extensions can be located directly, even if sequential reading failed
            try
            {
                reader.ReadExtensionsIfPresent(headerOnly: false);
            }
            catch (AgsFormatException ex)
            {
                reader._game.Error ??= ex.Message;
            }
            return reader._game;
        }

        private void ReadSignatureAndVersion()
        {
            if (_r.Length < Signature.Length || !_r.ReadBytes(Signature.Length).AsSpan().SequenceEqual(Signature))
                throw new AgsFormatException("Not an AGS game data file (signature missing).");

            _dataVersion = _r.ReadInt32();
            if (_dataVersion >= GameDataVersion.V230)
                _game.EngineVersion = _r.ReadPrefixedString();

            if (_dataVersion > GameDataVersion.Current)
                throw new AgsFormatException($"Game data version {_dataVersion} ({_game.EngineVersion}) is newer than supported.");

            if (_dataVersion >= GameDataVersion.V341)
            {
                int capCount = _r.ReadCount("capability", 1000);
                for (int i = 0; i < capCount; i++)
                    _r.ReadPrefixedString();
            }

            // Same version adjustments as OpenMainGameFileBase()
            if (System.Version.TryParse(_game.EngineVersion, out Version? v))
            {
                if (v.Major == 3 && v.Minor == 5 && v.Build == 1)
                    _dataVersion = GameDataVersion.V351;
                else if (v >= new Version(3, 6, 2, 7) && _dataVersion < GameDataVersion.V362_12)
                    _dataVersion = GameDataVersion.V362_07;
            }
            _game.DataVersion = _dataVersion;
        }

        private void ReadBaseStruct()
        {
            _game.Title = _r.ReadFixedString(50);
            _r.Skip(2);
            for (int i = 0; i < _options.Length; i++)
                _options[i] = _r.ReadInt32();
            _r.Skip(256 + 256 * 4); // paluses, defpal

            _numViews = _r.ReadInt32();
            _numCharacters = _r.ReadInt32();
            _r.ReadInt32(); // playercharacter
            _r.ReadInt32(); // totalscore
            _numInvItems = _r.ReadInt16();
            _r.Skip(2);
            _numDialogs = _r.ReadInt32();
            _r.ReadInt32(); // numdlgmessage
            _numFonts = _r.ReadInt32();
            _r.Skip(3 * 4); // color_depth, target_win, dialog_bullet
            _r.Skip(2 * 2); // inv hotspot colors
            _game.UniqueId = _r.ReadInt32();
            _numGuis = _r.ReadInt32();
            _numCursors = _r.ReadInt32();
            int resolutionType = _r.ReadInt32();
            if (resolutionType == GameResolutionCustom && _dataVersion >= GameDataVersion.V330)
                _r.Skip(2 * 4);
            _r.Skip(2 * 4); // default_lipsync_frame, inv_hot_sprite
            _r.Skip(16 * 4); // reserved
            _extensionOffset = _r.ReadUInt32();
            for (int i = 0; i < MaxGlobalMessages; i++)
                _hasMessages[i] = _r.ReadInt32();
            _hasWordsDict = _r.ReadInt32() != 0;
            _r.ReadInt32(); // globalscript pointer
            _r.ReadInt32(); // chars pointer
            _hasScripts = _r.ReadInt32() != 0;

            Validate("characters", _numCharacters, 100_000);
            Validate("inventory items", _numInvItems, 100_000);
            Validate("dialogs", _numDialogs, 100_000);
            Validate("views", _numViews, 1_000_000);
            Validate("fonts", _numFonts, 10_000);
            Validate("GUIs", _numGuis, 100_000);
            Validate("cursors", _numCursors, 10_000);

            _game.TextCodepage = _options[OptGameTextEncoding];
            _r.TextEncoding = _game.TextEncoding;
            _game.Title = ReDecode(_game.Title);
        }

        private void ReadSequentialData()
        {
            Section("savegame info", () =>
            {
                _game.Guid = _r.ReadFixedString(40);
                _r.Skip(20 + 50); // savegame extension and folder
            });
            Section("fonts", ReadFontInfos);
            Section("sprite flags", () => _r.Skip(_r.ReadCount("sprite", 10_000_000)));
            Section("inventory items", ReadInventoryItems);
            Section("mouse cursors", () => _r.Skip((long)_numCursors * CursorSize));
            Section("interaction events", ReadInteractionEvents);
            if (_hasWordsDict)
                Section("text parser dictionary", ReadWordsDictionary);
            if (_hasScripts)
                Section("scripts", ReadScripts);
            Section("views", ReadViews);
            Section("characters", ReadCharacters);
            if (_dataVersion >= GameDataVersion.V254)
                _r.Skip(LipSyncSize);
            Section("global messages", ReadGlobalMessages);
            Section("dialogs", ReadDialogs);
            Section("GUI", ReadGui);
        }

        private void ReadFontInfos()
        {
            if (_dataVersion < GameDataVersion.V350)
            {
                _r.Skip(_numFonts * 2); // flags and outline bytes
                if (_dataVersion >= GameDataVersion.V341)
                    _r.Skip(_numFonts * (_dataVersion >= GameDataVersion.V341_2 ? 8 : 4));
            }
            else
            {
                _r.Skip(_numFonts * 5 * 4);
            }
        }

        private void ReadInventoryItems()
        {
            for (int i = 0; i < _numInvItems; i++)
            {
                byte[] item = _r.ReadBytes(InvItemSize);
                _game.InventoryNames.Add(_r.Decode(item.AsSpan(0, 25)));
            }
        }

        private void ReadInteractionEvents()
        {
            // Inventory slot 0 is unused and has no events
            int count = _numCharacters + Math.Max(0, _numInvItems - 1);
            for (int i = 0; i < count; i++)
            {
                int events = _r.ReadCount("interaction event", 1000);
                for (int e = 0; e < events; e++)
                    _r.ReadCString();
            }
        }

        private void ReadWordsDictionary()
        {
            int count = _r.ReadCount("parser word", 1_000_000);
            for (int i = 0; i < count; i++)
            {
                _game.ParserWords.Add(_r.ReadEncryptedString());
                _r.ReadInt16(); // word id
            }
        }

        private void ReadScripts()
        {
            _game.Scripts.Add(CompiledScript.Read(_r)); // global script
            if (_dataVersion > GameDataVersion.V310)
                _game.Scripts.Add(CompiledScript.Read(_r)); // dialog script
            if (_dataVersion >= GameDataVersion.V270)
            {
                int modules = _r.ReadCount("script module", 100_000);
                for (int i = 0; i < modules; i++)
                    _game.Scripts.Add(CompiledScript.Read(_r));
            }
        }

        private void ReadViews()
        {
            for (int v = 0; v < _numViews; v++)
            {
                int loops = _r.ReadUInt16();
                for (int l = 0; l < loops; l++)
                {
                    int frames = _r.ReadUInt16();
                    _r.ReadInt32(); // loop flags
                    _r.Skip((long)frames * ViewFrameSize);
                }
            }
        }

        private void ReadCharacters()
        {
            for (int i = 0; i < _numCharacters; i++)
            {
                byte[] ch = _r.ReadBytes(CharacterSize);
                _game.CharacterNames.Add(_r.Decode(ch.AsSpan(CharacterNameOffset, 40)));
            }
        }

        private void ReadGlobalMessages()
        {
            for (int i = 0; i < MaxGlobalMessages; i++)
            {
                if (_hasMessages[i] == 0)
                    continue;
                _game.GlobalMessages.Add(_dataVersion < GameDataVersion.V261 ? _r.ReadCString(500) : _r.ReadEncryptedString());
            }
        }

        private void ReadDialogs()
        {
            var codeSizes = new int[_numDialogs];
            for (int d = 0; d < _numDialogs; d++)
            {
                var names = new string[DialogOptionCount];
                for (int i = 0; i < DialogOptionCount; i++)
                    names[i] = _r.ReadFixedString(DialogOptionLength);
                _r.Skip(DialogOptionCount * 4 + 4 + DialogOptionCount * 2 + 2); // flags, pointer, entry points, start
                codeSizes[d] = _r.ReadUInt16();
                int optionCount = Math.Clamp(_r.ReadInt32(), 0, DialogOptionCount);
                _r.ReadInt32(); // topic flags
                _game.Dialogs.Add(new DialogInfo("", names[..optionCount]));
            }

            if (_dataVersion > GameDataVersion.V310)
                return;

            // Pre-3.1.1: compiled dialog code, encrypted dialog sources and the dialog lines
            for (int d = 0; d < _numDialogs; d++)
            {
                _r.Skip(codeSizes[d]);
                int sourceLength = _r.ReadInt32();
                if (sourceLength > 1)
                    _r.Skip(sourceLength);
                else
                    _r.Position += sourceLength;
            }
            while (true)
            {
                uint length = _r.ReadUInt32();
                if (length == GuiMagic)
                {
                    _r.Position -= 4;
                    break;
                }
                _r.Position -= 4;
                _game.OldDialogLines.Add(_r.ReadEncryptedString());
            }
        }

        private void ReadGui()
        {
            if (_r.ReadUInt32() != GuiMagic)
                throw new AgsFormatException($"GUI signature not found at offset {_r.Position - 4}.");

            int version = _r.ReadInt32();
            int guiCount;
            if (version < 100)
            {
                guiCount = version;
                version = 0;
            }
            else
            {
                guiCount = _r.ReadCount("GUI", 100_000);
            }

            var gui = new GuiReader(_r, version);
            for (int i = 0; i < guiCount; i++)
                gui.SkipGuiMain();
            gui.ReadControls(_game.GuiTexts);
        }

        /// <summary>
        /// Reads the 3.6+ extension blocks. The base struct stores their offset, so they can be
        /// found even if a preceding section could not be parsed.
        /// </summary>
        private void ReadExtensionsIfPresent(bool headerOnly)
        {
            if (_dataVersion < GameDataVersion.V360 || _extensionOffset == 0 || _extensionOffset >= _r.Length)
                return;

            _r.Position = _extensionOffset;
            while (!_r.AtEnd)
            {
                long blockStart = _r.Position;
                sbyte id = _r.ReadSByte();
                if (id < 0)
                    break;

                string name = id == 0 ? _r.ReadFixedString(16) : $"#{id}";
                long length = _r.ReadInt64();
                long dataStart = _r.Position;
                if (length < 0 || dataStart + length > _r.Length)
                    throw new AgsFormatException($"Invalid length of game data extension '{name}' at offset {blockStart}.");

                switch (name.ToLowerInvariant())
                {
                    case "v361_objnames":
                        ReadObjectNamesExtension(headerOnly);
                        break;
                    case "v363_dialogsnew" when !headerOnly:
                        ReadDialogsExtension();
                        break;
                    case "v363_gameinfo" when !headerOnly:
                        int count = _r.ReadCount("game info", 10_000);
                        for (int i = 0; i < count; i++)
                            _game.GameInfo[_r.ReadPrefixedString()] = _r.ReadPrefixedString();
                        break;
                }
                _r.Position = dataStart + length;
            }
        }

        private void ReadObjectNamesExtension(bool headerOnly)
        {
            // Full-length names of entities whose legacy fields are truncated
            _game.Title = _r.ReadPrefixedString();
            _r.ReadPrefixedString(); // savegame folder
            if (headerOnly)
                return;

            int chars = _r.ReadCount("character", 100_000);
            for (int i = 0; i < chars; i++)
            {
                _r.ReadPrefixedString(); // script name
                string name = _r.ReadPrefixedString();
                SetOrAdd(_game.CharacterNames, i, name);
            }
            int items = _r.ReadCount("inventory item", 100_000);
            for (int i = 0; i < items; i++)
                SetOrAdd(_game.InventoryNames, i, _r.ReadPrefixedString());
            // cursor and audio clip names follow; not translatable
        }

        private void ReadDialogsExtension()
        {
            // Replaces the legacy dialog data (no option count or text length limits)
            _game.Dialogs.Clear();
            int count = _r.ReadCount("dialog", 100_000);
            for (int d = 0; d < count; d++)
            {
                string scriptName = _r.ReadPrefixedString();
                _r.Skip(4 * 4); // flags + reserved
                int optionCount = _r.ReadCount("dialog option", 100_000);
                var options = new List<string>(optionCount);
                for (int i = 0; i < optionCount; i++)
                {
                    options.Add(_r.ReadPrefixedString());
                    _r.Skip(4 * 4); // flags + reserved
                }
                _game.Dialogs.Add(new DialogInfo(scriptName, options));
            }
        }

        private static void SetOrAdd(List<string> list, int index, string value)
        {
            if (index < list.Count)
                list[index] = value;
            else
                list.Add(value);
        }

        // The title is read before the text encoding is known
        private string ReDecode(string latin1Text) => _game.TextEncoding.GetString(Encoding.Latin1.GetBytes(latin1Text));

        private void Section(string name, Action read)
        {
            long start = _r.Position;
            try
            {
                read();
            }
            catch (AgsFormatException ex)
            {
                throw new AgsFormatException($"Error in game data section '{name}' (starting at offset {start}): {ex.Message}", ex);
            }
        }

        private static void Validate(string what, int value, int max)
        {
            if (value < 0 || value > max)
                throw new AgsFormatException($"Implausible number of {what} ({value}); the game data may be damaged or of an unknown format.");
        }
    }
}

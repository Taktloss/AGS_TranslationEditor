using System.Text;

namespace AGSTools.Tests;

/// <summary>
/// Writes minimal, structurally valid AGS 3.x main game data (game28.dta) for tests.
/// Field layout follows ReadGameData() in the AGS sources; unused sections are left empty.
/// </summary>
internal sealed class GameDataBuilder
{
    public int DataVersion { get; set; } = 3060000; // 3.6.0
    public string EngineVersion { get; set; } = "3.6.0.50";
    public string Title { get; set; } = "Test Game";
    public byte[] UniqueIdBytes { get; set; } = { 0x12, 0x34, 0x56, 0x78 };
    public int Codepage { get; set; }
    public List<string> Characters { get; } = new();
    public List<string> InventoryItems { get; } = new() { "" }; // slot 0 is unused
    public List<string> GlobalMessages { get; } = new();
    public List<string[]> Dialogs { get; } = new();
    /// <summary>Lines spoken in dialogs of pre-3.1.1 games, stored after the dialogs.</summary>
    public List<string> OldDialogLines { get; } = new();
    public List<string> Buttons { get; } = new();
    public List<string> Labels { get; } = new();
    public string SaveFolder { get; set; } = "";
    /// <summary>Size and font of every button and label.</summary>
    public (int Width, int Height, int Font) ControlSize { get; set; }

    /// <summary>Full-length names for the "v361_objnames" extension; null to leave it out.</summary>
    public (string Title, string[] Characters, string[] Items)? ObjectNames { get; set; }

    private Encoding TextEncoding => Codepage == 65001 ? new UTF8Encoding(false) : Encoding.Latin1;

    public byte[] Build()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Latin1);

        w.Write(Encoding.ASCII.GetBytes("Adventure Creator Game File v2"));
        w.Write(DataVersion);
        WritePrefixed(w, Encoding.ASCII.GetBytes(EngineVersion));
        bool legacy = DataVersion <= 32; // AGS 2.x
        bool sequential = legacy || DataVersion >= 48;
        if (DataVersion >= 48)
            w.Write(0); // capabilities

        // Base struct
        w.Write(Fixed(Title, 50));
        w.Write(new byte[2]);
        var options = new int[100];
        options[49] = Codepage;
        foreach (int o in options) w.Write(o);
        w.Write(new byte[256 + 1024]); // palette
        w.Write(0); // views
        w.Write(Characters.Count);
        w.Write(0); // player
        w.Write(0); // total score
        w.Write((short)InventoryItems.Count);
        w.Write((short)0);
        w.Write(Dialogs.Count);
        w.Write(0); // dialog messages
        w.Write(0); // fonts
        w.Write(32); // color depth
        w.Write(0); // text window gui
        w.Write(0); // dialog bullet
        w.Write((short)0); w.Write((short)0);
        w.Write(UniqueIdBytes);
        w.Write(0); // GUIs
        w.Write(0); // cursors
        w.Write(1); // resolution type (not custom)
        w.Write(0); w.Write(0); // lip sync frame, inv hotspot sprite
        w.Write(new byte[16 * 4]);
        long extensionOffsetAt = ms.Position;
        w.Write(0u);
        for (int i = 0; i < 500; i++)
            w.Write(i < GlobalMessages.Count ? 1 : 0);
        w.Write(0); // no parser dictionary
        w.Write(0); w.Write(0); // pointers
        w.Write(0); // no scripts

        if (!sequential)
            return ms.ToArray(); // enough for header-only tests of 3.0-3.4 games

        if (!legacy)
        {
            w.Write(Fixed("{guid}", 40));
            w.Write(new byte[20]);
            w.Write(Fixed(SaveFolder, 50));
        }
        // fonts: none; sprite flags (fixed 6000 before 2.56)
        if (DataVersion < 24)
            w.Write(new byte[6000]);
        else
            w.Write(0);
        foreach (string item in InventoryItems)
        {
            w.Write(Fixed(item, 25));
            w.Write(new byte[68 - 25]);
        }
        // cursors: none; interaction events (3.x) or interactions without events plus no variables (2.x)
        if (legacy)
        {
            for (int i = 0; i < Characters.Count + InventoryItems.Count; i++)
            {
                w.Write(1); // interaction version
                w.Write(0); // events
            }
            w.Write(0);
        }
        else
        {
            for (int i = 0; i < Characters.Count + InventoryItems.Count - 1; i++)
                w.Write(0);
        }
        // views: none
        if (DataVersion <= 19)
            w.Write(0); // unknown 2.51 data
        // characters
        foreach (string name in Characters)
        {
            byte[] ch = new byte[780];
            Fixed(name, 40).CopyTo(ch, 718);
            w.Write(ch);
        }
        if (DataVersion >= 21)
            w.Write(new byte[1000]); // lip sync
        foreach (string message in GlobalMessages)
        {
            if (DataVersion < 26)
                w.Write(TextEncoding.GetBytes(message + "\0")); // not encrypted before 2.61
            else
                WriteEncrypted(w, TextEncoding.GetBytes(message));
        }
        foreach (string[] options30 in Dialogs)
        {
            for (int i = 0; i < 30; i++)
                w.Write(Fixed(i < options30.Length ? options30[i] : "", 150));
            w.Write(new byte[30 * 4 + 4 + 30 * 2 + 2]);
            w.Write((ushort)0); // code size
            w.Write(options30.Length);
            w.Write(0); // flags
        }
        if (DataVersion <= 37) // pre-3.1.1: dialog code and sources, then the dialog lines
        {
            foreach (var _ in Dialogs)
                w.Write(0); // source length
            foreach (string line in OldDialogLines)
            {
                if (DataVersion <= 25)
                    w.Write(TextEncoding.GetBytes(line + "\0")); // plain before 2.61
                else
                    WriteEncrypted(w, TextEncoding.GetBytes(line));
            }
        }

        // GUI (format 3.5.0)
        w.Write(0xcafebeefu);
        w.Write(119);
        w.Write(0); // GUIs
        w.Write(Buttons.Count);
        for (int i = 0; i < Buttons.Count; i++)
        {
            WriteControlBase(w, $"btn{i}", ControlSize);
            w.Write(new byte[3 * 4]);
            w.Write(ControlSize.Font);
            w.Write(new byte[5 * 4]);
            WritePrefixed(w, TextEncoding.GetBytes(Buttons[i]));
            w.Write(0); // alignment
        }
        w.Write(Labels.Count);
        for (int i = 0; i < Labels.Count; i++)
        {
            WriteControlBase(w, $"lbl{i}", ControlSize);
            WritePrefixed(w, TextEncoding.GetBytes(Labels[i]));
            w.Write(ControlSize.Font);
            w.Write(new byte[2 * 4]);
        }
        w.Write(0); w.Write(0); w.Write(0); w.Write(0); // inv windows, sliders, text boxes, list boxes

        if (ObjectNames is { } names)
        {
            long extStart = ms.Position;
            w.Write((byte)0);
            w.Write(Fixed("v361_objnames", 16));
            long lengthAt = ms.Position;
            w.Write(0L);
            long dataStart = ms.Position;
            WritePrefixed(w, TextEncoding.GetBytes(names.Title));
            WritePrefixed(w, Array.Empty<byte>());
            w.Write(names.Characters.Length);
            foreach (string name in names.Characters)
            {
                WritePrefixed(w, Encoding.ASCII.GetBytes("cChar"));
                WritePrefixed(w, TextEncoding.GetBytes(name));
            }
            w.Write(names.Items.Length);
            foreach (string name in names.Items)
                WritePrefixed(w, TextEncoding.GetBytes(name));
            w.Write(0); // cursors
            w.Write(0); // audio clips
            long end = ms.Position;
            ms.Position = lengthAt;
            w.Write(end - dataStart);
            ms.Position = extensionOffsetAt;
            w.Write((uint)extStart);
            ms.Position = end;
            w.Write((byte)0xFF);
        }
        return ms.ToArray();
    }

    private static void WriteControlBase(BinaryWriter w, string name, (int Width, int Height, int Font) size)
    {
        w.Write(new byte[3 * 4]); // flags, x, y
        w.Write(size.Width);
        w.Write(size.Height);
        w.Write(0); // z-order
        w.Write(Encoding.ASCII.GetBytes(name + "\0"));
        w.Write(0); // event handlers
    }

    private byte[] Fixed(string text, int size)
    {
        byte[] buf = new byte[size];
        byte[] bytes = TextEncoding.GetBytes(text);
        Array.Copy(bytes, buf, Math.Min(bytes.Length, size - 1));
        return buf;
    }

    private static void WritePrefixed(BinaryWriter w, byte[] text)
    {
        w.Write(text.Length);
        w.Write(text);
    }

    /// <summary>AGS string encryption: add "Avis Durgan" bytewise, including the terminating null.</summary>
    internal static void WriteEncrypted(BinaryWriter w, byte[] text)
    {
        byte[] key = Encoding.ASCII.GetBytes("Avis Durgan");
        byte[] buf = new byte[text.Length + 1];
        text.CopyTo(buf, 0);
        for (int i = 0; i < buf.Length; i++)
            buf[i] = (byte)(buf[i] + key[i % key.Length]);
        w.Write(buf.Length);
        w.Write(buf);
    }

    /// <summary>Embeds the data at an offset inside other bytes, like an engine exe with appended data.</summary>
    public static byte[] Embed(byte[] data, int offset)
    {
        byte[] result = new byte[offset + data.Length + 64];
        data.CopyTo(result, offset);
        return result;
    }
}

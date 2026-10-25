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
    public List<string> Buttons { get; } = new();
    public List<string> Labels { get; } = new();

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
        bool v3 = DataVersion >= 48;
        if (v3)
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

        if (!v3)
            return ms.ToArray(); // enough for header-only tests of old games

        w.Write(Fixed("{guid}", 40));
        w.Write(new byte[20 + 50]);
        // fonts: none; sprite flags
        w.Write(0);
        foreach (string item in InventoryItems)
        {
            w.Write(Fixed(item, 25));
            w.Write(new byte[68 - 25]);
        }
        // cursors: none; interaction events
        for (int i = 0; i < Characters.Count + InventoryItems.Count - 1; i++)
            w.Write(0);
        // views: none; characters
        foreach (string name in Characters)
        {
            byte[] ch = new byte[780];
            Fixed(name, 40).CopyTo(ch, 718);
            w.Write(ch);
        }
        w.Write(new byte[1000]); // lip sync
        foreach (string message in GlobalMessages)
            WriteEncrypted(w, TextEncoding.GetBytes(message));
        foreach (string[] options30 in Dialogs)
        {
            for (int i = 0; i < 30; i++)
                w.Write(Fixed(i < options30.Length ? options30[i] : "", 150));
            w.Write(new byte[30 * 4 + 4 + 30 * 2 + 2]);
            w.Write((ushort)0); // code size
            w.Write(options30.Length);
            w.Write(0); // flags
        }

        // GUI (format 3.5.0)
        w.Write(0xcafebeefu);
        w.Write(119);
        w.Write(0); // GUIs
        w.Write(Buttons.Count);
        for (int i = 0; i < Buttons.Count; i++)
        {
            WriteControlBase(w, $"btn{i}");
            w.Write(new byte[3 * 4 + 6 * 4]);
            WritePrefixed(w, TextEncoding.GetBytes(Buttons[i]));
            w.Write(0); // alignment
        }
        w.Write(Labels.Count);
        for (int i = 0; i < Labels.Count; i++)
        {
            WriteControlBase(w, $"lbl{i}");
            WritePrefixed(w, TextEncoding.GetBytes(Labels[i]));
            w.Write(new byte[3 * 4]);
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

    private static void WriteControlBase(BinaryWriter w, string name)
    {
        w.Write(new byte[6 * 4]);
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

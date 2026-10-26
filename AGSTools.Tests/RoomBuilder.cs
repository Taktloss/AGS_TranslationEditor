using System.Text;

namespace AGSTools.Tests;

/// <summary>
/// Writes a minimal compiled AGS 2.5-2.7 room (roomN.crm) with hotspots, objects and messages.
/// Field layout follows ReadMainBlock() in Common/game/room_file.cpp.
/// </summary>
internal sealed class RoomBuilder
{
    public int Version { get; set; } = 25; // 2.72
    public List<string> Hotspots { get; } = new();
    public List<string> Objects { get; } = new();
    public List<string> Messages { get; } = new();

    public byte[] Build()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Latin1);
        w.Write((ushort)Version);

        byte[] main = BuildMainBlock();
        w.Write((byte)1);
        w.Write(main.Length);
        w.Write(main);

        var names = new MemoryStream();
        names.WriteByte((byte)Objects.Count);
        foreach (string name in Objects)
            names.Write(Fixed(name, 30));
        w.Write((byte)5);
        w.Write((int)names.Length);
        w.Write(names.ToArray());

        w.Write((byte)0xFF);
        return ms.ToArray();
    }

    private byte[] BuildMainBlock()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Latin1);
        w.Write(8); // background bpp
        w.Write((short)0); // walk-behinds
        w.Write(Hotspots.Count);
        w.Write(new byte[Hotspots.Count * 4]);
        foreach (string name in Hotspots)
            w.Write(Fixed(name, 30));
        if (Version >= 24)
            w.Write(new byte[Hotspots.Count * 20]); // script names
        w.Write(0); // poly-point areas
        w.Write(new byte[4 * 2]); // edges
        w.Write((ushort)Objects.Count);
        w.Write(new byte[Objects.Count * 10]);
        if (Version >= 19)
            w.Write(0); // interaction variables
        for (int i = 0; i < Hotspots.Count + Objects.Count + 1; i++)
        {
            w.Write(1); // interaction version
            w.Write(0); // events
        }
        if (Version >= 21)
            w.Write(0); // regions
        w.Write(new byte[Objects.Count * 4]); // baselines
        w.Write((short)320);
        w.Write((short)200);
        if (Version >= 23)
            w.Write(new byte[Objects.Count * 2]); // object flags
        w.Write((short)1); // mask resolution
        w.Write(0); // walkable areas
        w.Write(new byte[11 + 6 + 4]); // password, options, reserved
        w.Write((short)Messages.Count);
        if (Version >= 25)
            w.Write(0); // game id
        w.Write(new byte[Messages.Count * 2]);
        foreach (string message in Messages)
        {
            if (Version < 22)
                w.Write(Encoding.Latin1.GetBytes(message + "\0"));
            else
                GameDataBuilder.WriteEncrypted(w, Encoding.Latin1.GetBytes(message));
        }
        w.Write((short)0); // legacy room animations
        return ms.ToArray();
    }

    private static byte[] Fixed(string text, int size)
    {
        byte[] buf = new byte[size];
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        Array.Copy(bytes, buf, Math.Min(bytes.Length, size - 1));
        return buf;
    }
}

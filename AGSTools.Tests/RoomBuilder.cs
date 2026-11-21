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

    /// <summary>Background frames (width, height, raw pixels in <see cref="BackgroundBpp"/> bytes each); none = no images.</summary>
    public List<(int Width, int Height, byte[] Pixels)> Backgrounds { get; } = new();
    public int BackgroundBpp { get; set; } = 4;

    /// <summary>Stands for the area masks that follow the first background.</summary>
    public static readonly byte[] MaskData = Enumerable.Range(0, 97).Select(i => (byte)(i * 7)).ToArray();

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

        if (Backgrounds.Count > 1)
        {
            var anim = new MemoryStream();
            anim.WriteByte((byte)Backgrounds.Count);
            anim.WriteByte(5); // animation speed
            if (Version >= 20)
                anim.Write(new byte[Backgrounds.Count]); // shared palette flags
            foreach (var bg in Backgrounds.Skip(1))
                anim.Write(Image(bg));
            w.Write((byte)6);
            w.Write((int)anim.Length);
            w.Write(anim.ToArray());
        }

        w.Write((byte)0xFF);
        return ms.ToArray();
    }

    private byte[] BuildMainBlock()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.Latin1);
        w.Write(Backgrounds.Count > 0 ? BackgroundBpp : 8); // background bytes per pixel
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
        if (Backgrounds.Count > 0)
        {
            w.Write(new byte[17 * 2]); // player views of the walkable areas
            w.Write(Image(Backgrounds[0]));
            w.Write(MaskData);
        }
        return ms.ToArray();
    }

    /// <summary>A compressed room image: palette, sizes, LZ data of stride, height and rows.</summary>
    private byte[] Image((int Width, int Height, byte[] Pixels) bg)
    {
        var raw = new byte[8 + bg.Pixels.Length];
        BitConverter.GetBytes(bg.Width * BackgroundBpp).CopyTo(raw, 0);
        BitConverter.GetBytes(bg.Height).CopyTo(raw, 4);
        bg.Pixels.CopyTo(raw, 8);
        byte[] packed = AgsLzw.Encode(raw);
        var ms = new MemoryStream();
        for (int i = 0; i < 256; i++)
            ms.Write(new[] { (byte)(i % 64), (byte)(63 - i % 64), (byte)(i / 4 % 64), (byte)0 });
        ms.Write(BitConverter.GetBytes(raw.Length));
        ms.Write(BitConverter.GetBytes(packed.Length));
        ms.Write(packed);
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

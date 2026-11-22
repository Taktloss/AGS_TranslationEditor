using System.Buffers.Binary;
using System.IO.Compression;

namespace AGSTools.Tests;

public class SpriteTests
{
    private const uint Red = 0xFFFF0000, Blue = 0xFF0000FF, Green = 0xFF00FF00;

    // A version 12 sprite file built by hand from the format description:
    //  0: 32-bit 3x2, RLE: a run of three red pixels, then three literals (blue, magenta mask, green)
    //  1: empty slot
    //  2: 32-bit 3x1 with an ARGB palette of 2 entries, uncompressed: indices 1, 0, 1
    //  3: 16-bit 2x1 uncompressed: magenta mask, green
    //  4: 8-bit 2x1 uncompressed: indices 0, 5
    internal static byte[] SpriteFileBytes()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((short)12);
        w.Write(" Sprite File "u8);
        w.Write((byte)1);
        w.Write(0x11223344u);
        w.Write(4);
        w.Write((byte)1);
        w.Write(new byte[3]);

        byte[] rle = new[] { new byte[] { 0xFE }, BitConverter.GetBytes(Red), new byte[] { 0x02 },
            BitConverter.GetBytes(Blue), BitConverter.GetBytes(0x00FF00FFu), BitConverter.GetBytes(Green) }.SelectMany(b => b).ToArray();
        Header(w, 4, 0, 1, 1, 3, 2);
        w.Write(rle.Length);
        w.Write(rle);

        w.Write((short)0);

        Header(w, 4, 33, 2, 0, 3, 1);
        w.Write(0x00FF00FFu);
        w.Write(0x80102030u);
        w.Write(3);
        w.Write(new byte[] { 1, 0, 1 });

        Header(w, 2, 0, 1, 0, 2, 1);
        w.Write(4);
        w.Write((ushort)0xF81F);
        w.Write((ushort)0x07E0);

        Header(w, 1, 0, 1, 0, 2, 1);
        w.Write(2);
        w.Write(new byte[] { 0, 5 });
        return ms.ToArray();

        static void Header(BinaryWriter w, int bpp, int format, int palette, int compression, int width, int height)
        {
            w.Write((byte)bpp);
            w.Write((byte)format);
            w.Write((byte)(palette - 1));
            w.Write((byte)compression);
            w.Write((ushort)width);
            w.Write((ushort)height);
        }
    }

    private static byte[] Palette()
    {
        var palette = new byte[256 * 4];
        palette[5 * 4] = 63; // index 5 is pure red
        return palette;
    }

    private static SpriteImage Decode(byte[] data, int number, bool alpha)
    {
        using var ms = new MemoryStream(data);
        SpriteFile file = SpriteFile.Read(ms);
        return file.ReadImage(ms, 0, file[number]!, alpha, Palette());
    }

    [Fact]
    public void Reads_the_sprite_table_with_empty_slots()
    {
        using var ms = new MemoryStream(SpriteFileBytes());
        SpriteFile file = SpriteFile.Read(ms);

        Assert.Equal(5, file.SlotCount);
        Assert.Null(file[1]);
        Assert.Equal(new[] { 0, 2, 3, 4 }, file.Sprites.Select(s => s.Number));
        Assert.Equal((3, 2, 4, SpriteFile.CompressionRle), (file[0]!.Width, file[0]!.Height, file[0]!.BytesPerPixel, file[0]!.Compression));
        Assert.Equal((SpriteFile.FormatPaletteArgb8888, 2, 1), (file[2]!.Format, file[2]!.PaletteCount, file[2]!.StoredPixelSize));
        Assert.Equal(0x11223344u, file.FileId);
    }

    [Fact]
    public void Decodes_rle_palettes_and_the_transparency_of_each_depth()
    {
        byte[] data = SpriteFileBytes();

        Assert.Equal(new[] { Red, Red, Red, Blue, 0u, Green }, Decode(data, 0, alpha: false).Pixels);
        Assert.Equal(new[] { 0x80102030u, 0x00FF00FFu, 0x80102030u }, Decode(data, 2, alpha: true).Pixels);
        Assert.Equal(new[] { 0xFF102030u, 0u, 0xFF102030u }, Decode(data, 2, alpha: false).Pixels);
        Assert.Equal(new[] { 0u, Green }, Decode(data, 3, alpha: false).Pixels);
        Assert.Equal(new[] { 0u, Red }, Decode(data, 4, alpha: false).Pixels);
    }

    // A sprite file of AGS 3.5 or older built from the format description: per sprite color depth,
    // width, height (16-bit), the data length if compressed, the data.
    //  0: 32-bit 3x1: red, the magenta mask, blue
    //  1: empty slot
    //  2: 8-bit 4x1: palette index 5 (red) four times
    private static byte[] LegacySpriteFile(int version, bool compressed)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((short)version);
        w.Write(" Sprite File "u8);
        if (version >= 6)
        {
            w.Write((byte)(compressed ? 1 : 0));
            w.Write(0x55667788u);
        }
        if (version < 5)
            w.Write(new byte[256 * 3]); // palette
        if (version >= 11)
            w.Write(2);
        else
            w.Write((short)2);

        // 0: 32-bit 3x1: red, mask, blue
        w.Write((short)4); w.Write((short)3); w.Write((short)1);
        byte[] raw = new[] { BitConverter.GetBytes(Red), BitConverter.GetBytes(0x00FF00FFu), BitConverter.GetBytes(Blue) }.SelectMany(b => b).ToArray();
        if (compressed)
        {
            byte[] rle = new byte[] { 0x02 }.Concat(raw).ToArray(); // three literals
            w.Write(rle.Length);
            w.Write(rle);
        }
        else
        {
            w.Write(raw);
        }
        // 1: empty
        w.Write((short)0);
        // 2: 8-bit 4x1: index 5 four times
        w.Write((short)1); w.Write((short)4); w.Write((short)1);
        if (compressed)
        {
            w.Write(2);
            w.Write(new byte[] { 0xFD, 5 }); // a run of four
        }
        else
        {
            w.Write(new byte[] { 5, 5, 5, 5 });
        }
        return ms.ToArray();
    }

    [Theory]
    [InlineData(11, true)]
    [InlineData(10, false)]
    [InlineData(6, true)]
    [InlineData(5, true)]
    [InlineData(4, false)]
    public void Reads_the_sprite_files_of_older_games(int version, bool compressed)
    {
        byte[] data = LegacySpriteFile(version, compressed);
        using var ms = new MemoryStream(data);
        SpriteFile file = SpriteFile.Read(ms);

        Assert.True(file.IsLegacy);
        Assert.Equal(version, file.Version);
        Assert.Equal(new[] { 0, 2 }, file.Sprites.Select(s => s.Number));
        Assert.Equal(new[] { Red, 0u, Blue }, file.ReadImage(ms, 0, file[0]!, false, Palette()).Pixels);
        Assert.Equal(Enumerable.Repeat(Red, 4), file.ReadImage(ms, 0, file[2]!, false, Palette()).Pixels);
    }

    [Theory]
    [InlineData(11, true, 11)]
    [InlineData(10, false, 2)]
    [InlineData(6, true, 2)]
    [InlineData(5, true, 1)]
    [InlineData(4, false, 1)]
    public void Older_sprite_files_are_patched_in_their_own_version(int version, bool compressed, int indexVersion)
    {
        byte[] original = LegacySpriteFile(version, compressed);
        using var source = new MemoryStream(original);
        SpriteFile file = SpriteFile.Read(source);
        var spr = new MemoryStream();
        var index = new MemoryStream();
        // Sprite 2 is 8-bit: palette index 5 is red, transparent stays index 0; it grows to 2x2
        var image = new SpriteImage(2, 2, new[] { Red, 0u, 0u, Red });

        file.WritePatched(source, 0, new Dictionary<int, SpriteImage> { [2] = image }, _ => false, Palette(), spr, index, newFileId: 0x01020304);

        byte[] patched = spr.ToArray();
        SpriteFile result = SpriteFile.Read(new MemoryStream(patched));
        Assert.Equal(version, result.Version);
        Assert.Equal(file.Compression, result.Compression);
        Assert.Equal(version >= 6 ? 0x01020304u : 0u, result.FileId);
        // Everything up to the end of sprite 1 is the same apart from the ID
        long end1 = file[0]!.DataOffset + file[0]!.DataSize + 2;
        Assert.Equal(original.AsSpan(0, 16).ToArray(), patched.AsSpan(0, 16).ToArray());
        Assert.Equal(original.AsSpan(20, (int)end1 - 20).ToArray(), patched.AsSpan(20, (int)end1 - 20).ToArray());
        Assert.Equal(new[] { Red, 0u, Blue }, result.ReadImage(new MemoryStream(patched), 0, result[0]!, false, Palette()).Pixels);
        Assert.Equal(new[] { Red, 0u, 0u, Red }, result.ReadImage(new MemoryStream(patched), 0, result[2]!, false, Palette()).Pixels);

        // Index: version, ID only from version 2, highest number, count, widths, heights, offsets (64-bit from 11)
        byte[] idx = index.ToArray();
        Assert.Equal("SPRINDEX"u8.ToArray(), idx[..8]);
        Assert.Equal(indexVersion, BitConverter.ToInt32(idx, 8));
        int pos = 12;
        if (indexVersion >= 2)
        {
            Assert.Equal(result.FileId, BitConverter.ToUInt32(idx, pos));
            pos += 4;
        }
        Assert.Equal((2, 3), (BitConverter.ToInt32(idx, pos), BitConverter.ToInt32(idx, pos + 4)));
        pos += 8;
        Assert.Equal(new short[] { 3, 0, 2 }, Enumerable.Range(0, 3).Select(i => BitConverter.ToInt16(idx, pos + i * 2)));
        Assert.Equal(new short[] { 1, 0, 2 }, Enumerable.Range(0, 3).Select(i => BitConverter.ToInt16(idx, pos + 6 + i * 2)));
        pos += 12;
        int offsetSize = indexVersion >= 11 ? 8 : 4;
        Assert.Equal(pos + 3 * offsetSize, idx.Length);
        long Offset(int i) => offsetSize == 8 ? BitConverter.ToInt64(idx, pos + i * 8) : BitConverter.ToInt32(idx, pos + i * 4);
        Assert.Equal(result[0]!.Offset, Offset(0));
        Assert.Equal(0, BitConverter.ToInt16(patched, (int)Offset(1)));
        Assert.Equal(result[2]!.Offset, Offset(2));
    }

    [Fact]
    public void Rejects_other_versions_and_broken_headers()
    {
        byte[] data = SpriteFileBytes();
        data[0] = 13;
        Assert.Throws<AgsFormatException>(() => SpriteFile.Read(new MemoryStream(data)));
        data[0] = 3;
        Assert.Throws<AgsFormatException>(() => SpriteFile.Read(new MemoryStream(data)));

        data = SpriteFileBytes();
        data[28] = 3; // bytes per pixel of sprite 0
        Assert.Throws<AgsFormatException>(() => SpriteFile.Read(new MemoryStream(data)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Rle_encoding_round_trips_runs_and_literals(int itemSize)
    {
        var rnd = new Random(itemSize);
        const int Width = 300, Height = 7;
        byte[] raw = new byte[Width * Height * itemSize];
        for (int i = 0; i < Width * Height; i++)
        {
            // Long runs, short runs and noise
            int v = i % Width < 200 ? i % Width / 150 : i % Width < 250 ? i % 3 / 2 : rnd.Next(4);
            for (int k = 0; k < itemSize; k++)
                raw[i * itemSize + k] = (byte)(v * 37 + k);
        }

        byte[] packed = SpriteFile.Rle.Encode(raw, itemSize, Width * itemSize);

        Assert.True(packed.Length < raw.Length / 2);
        Assert.Equal(raw, SpriteFile.Rle.Decode(packed, itemSize, raw.Length));
        Assert.Equal(packed.Length, SpriteFile.Rle.Consumed);
    }

    [Fact]
    public void Patch_replaces_a_sprite_copies_the_others_and_writes_a_matching_index()
    {
        byte[] original = SpriteFileBytes();
        using var source = new MemoryStream(original);
        SpriteFile file = SpriteFile.Read(source);
        var image = new SpriteImage(2, 2, new[] { Red, 0u, Green, Blue });
        var spr = new MemoryStream();
        var index = new MemoryStream();

        file.WritePatched(source, 0, new Dictionary<int, SpriteImage> { [2] = image }, n => false, Palette(), spr, index, newFileId: 0xABCDEF01);

        byte[] patched = spr.ToArray();
        SpriteFile result = SpriteFile.Read(new MemoryStream(patched));
        Assert.Equal(0xABCDEF01u, result.FileId);
        Assert.Equal(5, result.SlotCount);
        Assert.Null(result[1]);
        // Header (apart from the ID) and sprite 0 are copied byte for byte
        Assert.Equal(original.AsSpan(0, 16).ToArray(), patched.AsSpan(0, 16).ToArray());
        long end0 = file[0]!.DataOffset + file[0]!.DataSize;
        Assert.Equal(original.AsSpan(20, (int)end0 - 20).ToArray(), patched.AsSpan(20, (int)end0 - 20).ToArray());
        Assert.Equal(new[] { Red, 0u, Green, Blue }, result.ReadImage(new MemoryStream(patched), 0, result[2]!, false, Palette()).Pixels);
        Assert.Equal(new[] { 0u, Red }, result.ReadImage(new MemoryStream(patched), 0, result[4]!, false, Palette()).Pixels);

        byte[] idx = index.ToArray();
        Assert.Equal("SPRINDEX"u8.ToArray(), idx[..8]);
        Assert.Equal(11, BitConverter.ToInt32(idx, 8));
        Assert.Equal(0xABCDEF01u, BitConverter.ToUInt32(idx, 12));
        Assert.Equal(4, BitConverter.ToInt32(idx, 16));
        Assert.Equal(5, BitConverter.ToInt32(idx, 20));
        Assert.Equal(new short[] { 3, 0, 2, 2, 2 }, Enumerable.Range(0, 5).Select(i => BitConverter.ToInt16(idx, 24 + i * 2)));
        Assert.Equal(new short[] { 2, 0, 2, 1, 1 }, Enumerable.Range(0, 5).Select(i => BitConverter.ToInt16(idx, 34 + i * 2)));
        for (int i = 0; i < 5; i++)
        {
            long offset = BitConverter.ToInt64(idx, 44 + i * 8);
            if (result[i] is { } e)
                Assert.Equal(e.Offset, offset);
            else
                Assert.Equal(0, BitConverter.ToInt16(patched, (int)offset));
        }
    }

    [Fact]
    public void Patched_32_bit_sprites_without_alpha_use_the_exact_mask_color()
    {
        Assert.Equal(0x00FF00FFu, SpriteFile.To32(0x00000000, hasAlpha: false));
        Assert.Equal(0xFFFE00FFu, SpriteFile.To32(0xFFFF00FF, hasAlpha: false));
        Assert.Equal(0xFF123456u, SpriteFile.To32(0xFF123456, hasAlpha: false));
        Assert.Equal(0x80123456u, SpriteFile.To32(0x80123456, hasAlpha: true));
        Assert.Equal((ushort)0xF81F, SpriteFile.To565(0));
        Assert.Equal((ushort)0xF81E, SpriteFile.To565(0xFFFF00FF));
    }

    // ── GameSprites with a loose game28.dta and acsprset.spr ──

    private sealed class Game : IDisposable
    {
        private readonly TempDir _root = new();

        public Game()
        {
            Folder = Directory.CreateDirectory(_root.File("game")).FullName;
            Work = Directory.CreateDirectory(_root.File("work")).FullName;
            DataFile = Path.Combine(Folder, "game28.dta");
            File.WriteAllBytes(DataFile, new GameDataBuilder { SpriteFlags = new byte[] { 0, 0, SpriteFile.FlagAlphaChannel, 0, 0 } }.Build());
            File.WriteAllBytes(Path.Combine(Folder, SpriteFile.FileName), SpriteFileBytes());
        }

        public string Folder { get; }
        public string Work { get; }
        public string DataFile { get; }

        public void Dispose() => _root.Dispose();
    }

    [Fact]
    public void Game_sprites_know_the_alpha_flag_and_find_changed_pngs()
    {
        using var g = new Game();
        using GameSprites sprites = GameSprites.Open(g.DataFile);
        Assert.True(sprites.HasAlpha(2));
        Assert.False(sprites.HasAlpha(0));
        Assert.Equal(new[] { 0x80102030u, 0x00FF00FFu, 0x80102030u }, sprites.Read(2).Pixels);

        string pngs = Directory.CreateDirectory(Path.Combine(g.Work, "png")).FullName;
        foreach (SpriteEntry e in sprites.File.Sprites)
            Png.Write(Path.Combine(pngs, GameSprites.PngName(e.Number)), sprites.Read(e.Number));
        // A transparent pixel with another color is still the same in the game
        SpriteImage zero = Png.Read(Path.Combine(pngs, "sprite00000.png"));
        zero[1, 1] = 0x00123456;
        Png.Write(Path.Combine(pngs, "sprite00000.png"), zero);
        SpriteImage three = sprites.Read(3);
        three[1, 0] = Red;
        Png.Write(Path.Combine(pngs, "3.png"), three);
        File.WriteAllText(Path.Combine(pngs, "notes.png"), "not a sprite name");
        File.Copy(Path.Combine(pngs, "3.png"), Path.Combine(pngs, "sprite00009.png"));

        var notes = new List<string>();
        Dictionary<int, SpriteImage> changed = sprites.LoadChanged(pngs, notes);

        Assert.Equal(new[] { 3 }, changed.Keys);
        Assert.Contains(notes, n => n.Contains("no sprite 9"));
        Assert.Contains(notes, n => n.Contains("already read"));
        Assert.Contains(notes, n => n.StartsWith("3 PNG files are the same"));
    }

    [Fact]
    public void Game_sprites_write_a_patch_but_not_into_the_game_folder()
    {
        using var g = new Game();
        using GameSprites sprites = GameSprites.Open(g.DataFile);
        var changed = new Dictionary<int, SpriteImage> { [0] = new SpriteImage(1, 1, new[] { Green }) };

        Assert.Throws<IOException>(() => sprites.WritePatch(changed, g.Folder));
        string patch = Path.Combine(g.Work, "patch");
        sprites.WritePatch(changed, patch);

        using var fs = File.OpenRead(Path.Combine(patch, SpriteFile.FileName));
        SpriteFile result = SpriteFile.Read(fs);
        Assert.Equal(new[] { Green }, result.ReadImage(fs, 0, result[0]!, false, null).Pixels);
        Assert.NotEqual(sprites.File.FileId, result.FileId);
        Assert.Equal(result.FileId, BitConverter.ToUInt32(File.ReadAllBytes(Path.Combine(patch, SpriteFile.IndexFileName)), 12));
    }

    [Theory]
    [InlineData("sprite00042.png", 42)]
    [InlineData("SPRITE0.PNG", 0)]
    [InlineData("7.png", 7)]
    [InlineData("sprite00042.bmp", -1)]
    [InlineData("map.png", -1)]
    public void Png_names_give_the_sprite_number(string name, int number) =>
        Assert.Equal(number, GameSprites.NumberFromFileName(name));

    // ── PNG ──

    [Fact]
    public void Png_round_trips_rgba()
    {
        var image = new SpriteImage(3, 2, new[] { Red, 0x80123456u, 0u, Blue, Green, 0xFFFFFFFFu });
        var ms = new MemoryStream();
        Png.Write(ms, image);
        ms.Position = 0;
        Assert.Equal(image.Pixels, Png.Read(ms).Pixels);
    }

    [Fact]
    public void Png_reads_palette_images_with_transparency()
    {
        // 2 bits per pixel, 3x1: indices 0, 1, 2; index 0 is transparent
        byte[] png = BuildPng(3, 1, 2, 3, new[] { new byte[] { 0b00_01_10_00 } }, filter: 0,
            ("PLTE", new byte[] { 1, 2, 3, 255, 0, 0, 0, 0, 255 }), ("tRNS", new byte[] { 0 }));
        Assert.Equal(new[] { 0x00010203u, Red, Blue }, Png.Read(new MemoryStream(png)).Pixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Png_reads_every_filter_type(int filter)
    {
        var rows = new[]
        {
            new byte[] { 10, 20, 30, 200, 100, 50 },
            new byte[] { 15, 25, 35, 190, 110, 40 },
        };
        byte[] png = BuildPng(2, 2, 8, 2, rows, filter);
        Assert.Equal(new[] { 0xFF0A141Eu, 0xFFC86432u, 0xFF0F1923u, 0xFFBE6E28u }, Png.Read(new MemoryStream(png)).Pixels);
    }

    [Fact]
    public void Png_reads_16_bit_grey_with_alpha()
    {
        byte[] png = BuildPng(1, 1, 16, 4, new[] { new byte[] { 0x80, 0x00, 0xFF, 0xFF } }, filter: 0);
        Assert.Equal(0xFF808080u, Png.Read(new MemoryStream(png)).Pixels[0]);
    }

    [Fact]
    public void Png_reads_interlaced_images()
    {
        var expected = new uint[5 * 3];
        for (int i = 0; i < expected.Length; i++)
            expected[i] = 0xFF000000u | (uint)(i * 0x0F0503);
        // Adam7: pixels go into seven passes, each a small image of its own
        int[] sx = { 0, 4, 0, 2, 0, 1, 0 }, sy = { 0, 0, 4, 0, 2, 0, 1 }, dx = { 8, 8, 4, 4, 2, 2, 1 }, dy = { 8, 8, 8, 4, 4, 2, 2 };
        var raw = new MemoryStream();
        for (int p = 0; p < 7; p++)
        {
            for (int y = sy[p]; y < 3; y += dy[p])
            {
                var row = new List<byte>();
                for (int x = sx[p]; x < 5; x += dx[p])
                {
                    uint c = expected[y * 5 + x];
                    row.AddRange(new[] { (byte)(c >> 16), (byte)(c >> 8), (byte)c });
                }
                if (row.Count == 0)
                    break;
                raw.WriteByte(0);
                raw.Write(row.ToArray());
            }
        }
        byte[] png = BuildPngRaw(5, 3, 8, 2, raw.ToArray(), interlace: 1);
        Assert.Equal(expected, Png.Read(new MemoryStream(png)).Pixels);
    }

    private static byte[] BuildPng(int width, int height, int depth, int colorType, byte[][] rows, int filter, params (string Type, byte[] Data)[] extra)
    {
        int channels = colorType switch { 2 => 3, 4 => 2, 6 => 4, _ => 1 };
        int bpp = Math.Max(1, channels * depth / 8);
        var raw = new MemoryStream();
        byte[] prev = new byte[rows[0].Length];
        foreach (byte[] row in rows)
        {
            raw.WriteByte((byte)filter);
            for (int i = 0; i < row.Length; i++)
            {
                int a = i >= bpp ? row[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                int predictor = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                raw.WriteByte((byte)(row[i] - predictor));
            }
            prev = row;
        }
        return BuildPngRaw(width, height, depth, colorType, raw.ToArray(), 0, extra);

        static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    private static byte[] BuildPngRaw(int width, int height, int depth, int colorType, byte[] filtered, int interlace, params (string Type, byte[] Data)[] extra)
    {
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = (byte)depth;
        ihdr[9] = (byte)colorType;
        ihdr[12] = (byte)interlace;
        Chunk("IHDR", ihdr);
        foreach (var (type, data) in extra)
            Chunk(type, data);
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Fastest, leaveOpen: true))
            zs.Write(filtered);
        Chunk("IDAT", z.ToArray());
        Chunk("IEND", Array.Empty<byte>());
        return ms.ToArray();

        void Chunk(string type, byte[] data)
        {
            var len = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            ms.Write(len);
            ms.Write(System.Text.Encoding.ASCII.GetBytes(type));
            ms.Write(data);
            ms.Write(new byte[4]); // CRC is not checked by the reader
        }
    }
}

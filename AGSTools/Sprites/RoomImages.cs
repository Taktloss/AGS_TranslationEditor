using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>One background frame of a room (rooms can have up to 5 that the game switches between).</summary>
    public sealed record RoomBackground(int Room, int Frame, int Width, int Height, int BytesPerPixel)
    {
        /// <summary>"Room 5, background 2" style label; frame 0 is the main background.</summary>
        public string Label => Frame == 0 ? $"Room {Room}" : $"Room {Room}, background {Frame + 1}";
    }

    /// <summary>
    /// The background images of one compiled room file (roomN.crm). They are stored compressed in the main block
    /// (the first frame) and in the animated-backgrounds block (the others). Replacing them rewrites only those
    /// parts, so everything else in the room stays byte for byte the same.
    /// </summary>
    public sealed class RoomImages
    {
        private sealed record Frame(int BlockIndex, long Start, long End, int PaletteSize, byte[] Palette, int Width, int Height);

        private readonly byte[] _data;
        private readonly RoomData _room;
        private readonly List<Frame> _frames;

        private RoomImages(int number, string name, byte[] data, RoomData room, List<Frame> frames)
        {
            Number = number;
            Name = name;
            _data = data;
            _room = room;
            _frames = frames;
        }

        public int Number { get; }

        /// <summary>Asset name, e.g. room5.crm.</summary>
        public string Name { get; }

        public int BytesPerPixel => _room.BackgroundBytesPerPixel;

        public IReadOnlyList<RoomBackground> Backgrounds =>
            _frames.Select((f, i) => new RoomBackground(Number, i, f.Width, f.Height, BytesPerPixel)).ToList();

        /// <exception cref="AgsFormatException">The room file cannot be read or its images not found.</exception>
        public static RoomImages Read(string name, byte[] data)
        {
            RoomData room = RoomReader.Read(name, data, Encoding.Latin1);
            if (room.Error != null)
                throw new AgsFormatException($"{name}: {room.Error}");
            if (room.AfterMessages < 0 || room.BackgroundBytesPerPixel is not (1 or 2 or 4))
                throw new AgsFormatException($"{name}: background format not recognized.");
            int main = room.Blocks.FindIndex(b => b.Id == 1);
            RoomBlock block = room.Blocks[main];
            var frames = new List<Frame>();

            // Between the messages and the first image are a few fixed-size fields whose layout changed over the
            // versions; look for the image header there and accept it only if it decodes to a whole bitmap.
            long end = block.Start + block.Length;
            Frame? first = null;
            for (long p = room.AfterMessages; p < Math.Min(room.AfterMessages + 4096, end) && first == null; p += 2)
                first = TryFrame(data, main, p, end, room.BackgroundBytesPerPixel, paletteSize: 0);
            if (first == null)
                throw new AgsFormatException($"{name}: the background image was not found.");
            frames.Add(first);

            int anim = room.Blocks.FindIndex(b => b.Id == RoomReader.BlockAnimatedBackgrounds);
            if (anim >= 0)
            {
                RoomBlock ab = room.Blocks[anim];
                int count = data[ab.Start];
                long p = ab.Start + 2 + (room.Version >= RoomReader.V255a ? count : 0);
                long abEnd = ab.Start + ab.Length;
                for (int i = 1; i < count; i++)
                {
                    Frame f = TryFrame(data, anim, p, abEnd, room.BackgroundBytesPerPixel, first.PaletteSize)
                        ?? throw new AgsFormatException($"{name}: background {i + 1} cannot be read.");
                    frames.Add(f);
                    p = f.End;
                }
            }
            return new RoomImages(GameFileSource.RoomNumber(name), name, data, room, frames);
        }

        // Image: palette (256 x RGB+filler), uncompressed size, compressed size, data that expands to
        // stride (bytes per row), height and the rows
        private static Frame? TryFrame(byte[] data, int blockIndex, long p, long end, int bpp, int paletteSize)
        {
            foreach (int palette in paletteSize > 0 ? new[] { paletteSize } : new[] { 1024, 768 })
            {
                long h = p + palette;
                if (h + 8 > end)
                    continue;
                int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)h));
                int packed = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)h + 4));
                if (size < 8 || size > 256 << 20 || packed <= 0 || h + 8 + packed > end)
                    continue;
                byte[] raw;
                try
                {
                    raw = AgsLzw.Decode(data.AsSpan((int)h + 8, packed), size, out int used);
                    if (used > packed)
                        continue;
                }
                catch (AgsFormatException)
                {
                    continue;
                }
                int stride = BinaryPrimitives.ReadInt32LittleEndian(raw), height = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4));
                if (stride <= 0 || height <= 0 || stride % bpp != 0 || (long)stride * height + 8 != size)
                    continue;
                return new Frame(blockIndex, p, h + 8 + packed, palette, data.AsSpan((int)p, palette).ToArray(), stride / bpp, height);
            }
            return null;
        }

        private byte[] Raw(Frame f)
        {
            long h = f.Start + f.PaletteSize;
            int size = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan((int)h));
            int packed = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan((int)h + 4));
            return AgsLzw.Decode(_data.AsSpan((int)h + 8, packed), size, out _);
        }

        /// <summary>Decodes a background frame. Backgrounds are opaque; magenta is an ordinary color here.</summary>
        public SpriteImage Read(int frame)
        {
            Frame f = _frames[frame];
            byte[] raw = Raw(f);
            var image = new SpriteImage(f.Width, f.Height);
            int bpp = BytesPerPixel;
            for (int i = 0; i < image.Pixels.Length; i++)
            {
                int o = 8 + i * bpp;
                image.Pixels[i] = bpp switch
                {
                    1 => PaletteColor(f.Palette, raw[o]),
                    2 => From565(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(o))),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(o)) | 0xFF000000u,
                };
            }
            return image;
        }

        /// <summary>The image as the game will show it after storing it in this room's color depth.</summary>
        public SpriteImage Normalize(int frame, SpriteImage image)
        {
            Frame f = _frames[frame];
            var result = new SpriteImage(image.Width, image.Height);
            for (int i = 0; i < image.Pixels.Length; i++)
            {
                uint p = image.Pixels[i];
                result.Pixels[i] = BytesPerPixel switch
                {
                    1 => PaletteColor(f.Palette, NearestPaletteIndex(f.Palette, p)),
                    2 => From565(To565(p)),
                    _ => p | 0xFF000000u,
                };
            }
            return result;
        }

        public bool IsUnchanged(int frame, SpriteImage image)
        {
            Frame f = _frames[frame];
            if (image.Width != f.Width || image.Height != f.Height)
                return false;
            return Normalize(frame, Read(frame)).Pixels.AsSpan().SequenceEqual(Normalize(frame, image).Pixels);
        }

        /// <summary>
        /// The room file with replaced backgrounds. A replacement must have the size of the frame it replaces (the
        /// room's walkable areas, hotspots and regions are masks of that size).
        /// </summary>
        /// <exception cref="ArgumentException">A frame does not exist or the size differs.</exception>
        public byte[] WritePatched(IReadOnlyDictionary<int, SpriteImage> replacements)
        {
            foreach (var (frame, image) in replacements)
            {
                if (frame < 0 || frame >= _frames.Count)
                    throw new ArgumentException($"{Name} has no background {frame + 1}.");
                if (image.Width != _frames[frame].Width || image.Height != _frames[frame].Height)
                    throw new ArgumentException($"{Name}, background {frame + 1}: the picture is {image.Width}x{image.Height}, "
                        + $"the room needs {_frames[frame].Width}x{_frames[frame].Height}.");
            }

            var output = new MemoryStream(_data.Length + 1024);
            long copied = 0;
            var lengthChanges = new Dictionary<int, long>();
            foreach (var (frame, image) in replacements.OrderBy(r => _frames[r.Key].Start))
            {
                Frame f = _frames[frame];
                output.Write(_data, (int)copied, (int)(f.Start - copied));
                byte[] encoded = Encode(f, image);
                output.Write(encoded);
                lengthChanges[f.BlockIndex] = lengthChanges.GetValueOrDefault(f.BlockIndex) + encoded.Length - (f.End - f.Start);
                copied = f.End;
            }
            output.Write(_data, (int)copied, _data.Length - (int)copied);
            byte[] result = output.ToArray();

            // Fix the lengths of the changed blocks; positions after an earlier changed block have moved
            foreach (var (blockIndex, change) in lengthChanges)
            {
                RoomBlock b = _room.Blocks[blockIndex];
                long shift = lengthChanges.Where(c => _room.Blocks[c.Key].Start < b.Start).Sum(c => c.Value);
                int at = (int)(b.LengthPosition + shift);
                long length = b.Length + change;
                if (b.LengthSize == 8)
                    BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(at), length);
                else if (length > int.MaxValue)
                    throw new ArgumentException($"{Name}: the room becomes too large.");
                else
                    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(at), (int)length);
            }
            return result;
        }

        private byte[] Encode(Frame f, SpriteImage image)
        {
            int bpp = BytesPerPixel;
            byte[] original = Raw(f);
            var raw = new byte[8 + image.Width * image.Height * bpp];
            BinaryPrimitives.WriteInt32LittleEndian(raw, image.Width * bpp);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(4), image.Height);
            for (int i = 0; i < image.Pixels.Length; i++)
            {
                int o = 8 + i * bpp;
                uint p = image.Pixels[i];
                switch (bpp)
                {
                    case 1:
                        raw[o] = NearestPaletteIndex(f.Palette, p);
                        break;
                    case 2:
                        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(o), To565(p));
                        break;
                    default:
                        // Keep the original's unused top byte, so unchanged pixels stay identical
                        uint top = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(o)) & 0xFF000000u;
                        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(o), (p & 0xFFFFFF) | top);
                        break;
                }
            }
            byte[] packed = AgsLzw.Encode(raw);
            var result = new byte[f.PaletteSize + 8 + packed.Length];
            f.Palette.CopyTo(result, 0);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(f.PaletteSize), raw.Length);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(f.PaletteSize + 4), packed.Length);
            packed.CopyTo(result, f.PaletteSize + 8);
            return result;
        }

        private static uint From565(ushort v)
        {
            uint r = (uint)(v >> 11) * 255 / 31, g = (uint)((v >> 5) & 63) * 255 / 63, b = (uint)(v & 31) * 255 / 31;
            return 0xFF000000u | r << 16 | g << 8 | b;
        }

        private static ushort To565(uint p)
        {
            uint r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;
            return (ushort)(((r * 31 + 127) / 255) << 11 | ((g * 63 + 127) / 255) << 5 | ((b * 31 + 127) / 255));
        }

        private static uint PaletteColor(byte[] palette, byte index)
        {
            int step = palette.Length / 256;
            uint c(int k) => (uint)(Math.Min(palette[index * step + k], (byte)63) * 255 / 63);
            return 0xFF000000u | c(0) << 16 | c(1) << 8 | c(2);
        }

        private static byte NearestPaletteIndex(byte[] palette, uint p)
        {
            int best = 0, bestDistance = int.MaxValue;
            for (int i = 0; i < 256; i++)
            {
                uint c = PaletteColor(palette, (byte)i);
                int dr = (int)((c >> 16) & 255) - (int)((p >> 16) & 255);
                int dg = (int)((c >> 8) & 255) - (int)((p >> 8) & 255);
                int db = (int)(c & 255) - (int)(p & 255);
                int d = dr * dr + dg * dg + db * db;
                if (d < bestDistance)
                {
                    best = i;
                    bestDistance = d;
                    if (d == 0)
                        break;
                }
            }
            return (byte)best;
        }
    }
}

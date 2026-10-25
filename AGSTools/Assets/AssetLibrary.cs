using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AGSTools
{
    /// <summary>A single file packed into an AGS asset library.</summary>
    public sealed record AssetInfo(string Name, int LibraryIndex, long Offset, long Size);

    /// <summary>
    /// Reads the asset table of an AGS "CLIB" multi-file library, either standalone (.ags, .vox, .001)
    /// or appended to the engine executable. Port of Common/data/multifilelib.cpp.
    /// </summary>
    public sealed class AssetLibrary
    {
        private static readonly byte[] HeadSig = "CLIB\x1a"u8.ToArray();
        private static readonly byte[] TailSig = "CLIB\x01\x02\x03\x04SIGE"u8.ToArray();

        private const int SingleFilePswLen = 13;
        private const int MaxAssetFileLen = 100;
        private const int MaxDataFileLen = 50;
        private const int V10LibFileLen = 20;
        private const int V10AssetFileLen = 25;
        private const int EncryptionRandSeed = 9338638;
        // Not a u8 literal: 0xDE would be encoded as two UTF-8 bytes
        private static readonly byte[] EncryptionString = { (byte)'M', (byte)'y', 0x01, 0xDE, 0x04, (byte)'J', (byte)'i', (byte)'b', (byte)'z', (byte)'l', (byte)'e' };

        private readonly Dictionary<string, AssetInfo> _assets = new(StringComparer.OrdinalIgnoreCase);

        private AssetLibrary(string path) => FilePath = path;

        /// <summary>Path of the main library file (index 0).</summary>
        public string FilePath { get; }

        public int Version { get; private set; }

        /// <summary>File names of all library parts; index 0 is the main file.</summary>
        public IReadOnlyList<string> LibraryFiles { get; private set; } = Array.Empty<string>();

        public IReadOnlyCollection<AssetInfo> Assets => _assets.Values;

        public bool TryGetAsset(string name, out AssetInfo asset) => _assets.TryGetValue(name, out asset!);

        /// <summary>
        /// Opens <paramref name="path"/> as an asset library. Returns null if the file is not a library.
        /// </summary>
        public static AssetLibrary? TryOpen(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new AgsReader(fs, Encoding.Latin1);

            long baseOffset = FindLibraryStart(reader);
            if (baseOffset < 0)
                return null;

            var lib = new AssetLibrary(path);
            reader.Position = baseOffset + HeadSig.Length;
            lib.Version = reader.ReadByte();

            List<AssetInfo> assets = lib.Version switch
            {
                6 => lib.ReadSingleFileLib(reader),
                10 or 11 or 15 or 20 or 21 or 30 => lib.ReadMultiFileLib(reader),
                _ => throw new AgsFormatException($"Unsupported asset library version {lib.Version} in '{path}'."),
            };

            // Offsets of assets in the main file are relative to the library start (matters for exe files)
            foreach (AssetInfo asset in assets)
            {
                AssetInfo fixedAsset = asset.LibraryIndex == 0 && baseOffset > 0
                    ? asset with { Offset = asset.Offset + baseOffset }
                    : asset;
                lib._assets[asset.Name] = fixedAsset;
            }
            return lib;
        }

        /// <summary>Reads an asset fully into memory.</summary>
        public byte[] ReadAsset(AssetInfo asset)
        {
            if (asset.Size > int.MaxValue)
                throw new AgsFormatException($"Asset '{asset.Name}' is too large ({asset.Size} bytes).");

            string file = ResolveLibraryFile(asset.LibraryIndex);
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (asset.Offset < 0 || asset.Offset + asset.Size > fs.Length)
                throw new AgsFormatException($"Asset '{asset.Name}' lies outside of '{Path.GetFileName(file)}'.");

            byte[] data = new byte[asset.Size];
            fs.Position = asset.Offset;
            fs.ReadExactly(data);
            return data;
        }

        private string ResolveLibraryFile(int index)
        {
            if (index == 0)
                return FilePath;
            if (index >= LibraryFiles.Count)
                throw new AgsFormatException($"Asset refers to unknown library part {index}.");
            string dir = Path.GetDirectoryName(Path.GetFullPath(FilePath)) ?? ".";
            return Path.Combine(dir, LibraryFiles[index]);
        }

        /// <summary>
        /// Returns the offset of the "CLIB\x1a" header: 0 for standalone libraries, or the offset
        /// stored in front of the tail signature for libraries appended to an executable.
        /// </summary>
        private static long FindLibraryStart(AgsReader reader)
        {
            if (reader.Length < TailSig.Length + 8)
                return -1;

            if (HasHeaderAt(reader, 0))
                return 0;

            reader.Position = reader.Length - TailSig.Length;
            if (!reader.ReadBytes(TailSig.Length).AsSpan().SequenceEqual(TailSig))
                return -1;

            // The offset may be stored as 64-bit or (older engines) 32-bit value
            long tailStart = reader.Length - TailSig.Length;
            reader.Position = tailStart - 8;
            long offset64 = reader.ReadInt64();
            reader.Position = tailStart - 4;
            long offset32 = reader.ReadInt32();

            foreach (long candidate in new[] { offset64, offset32 })
            {
                if (candidate > 0 && candidate < tailStart - HeadSig.Length && HasHeaderAt(reader, candidate))
                    return candidate;
            }
            return -1;
        }

        private static bool HasHeaderAt(AgsReader reader, long offset)
        {
            reader.Position = offset;
            return reader.ReadBytes(HeadSig.Length).AsSpan().SequenceEqual(HeadSig);
        }

        private List<AssetInfo> ReadSingleFileLib(AgsReader reader)
        {
            byte passwModifier = reader.ReadByte();
            reader.ReadByte(); // unused
            int count = reader.ReadUInt16();
            reader.Skip(SingleFilePswLen);

            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                byte[] buf = reader.ReadBytes(SingleFilePswLen);
                for (int k = 0; k < buf.Length && buf[k] != 0; k++)
                    buf[k] = (byte)(buf[k] - passwModifier);
                names[i] = reader.Decode(buf);
            }
            var sizes = new long[count];
            for (int i = 0; i < count; i++)
                sizes[i] = reader.ReadUInt32();
            reader.Skip(2L * count); // flags & ratio

            LibraryFiles = new[] { Path.GetFileName(FilePath) };
            var assets = new List<AssetInfo>(count);
            long offset = reader.Position;
            for (int i = 0; i < count; i++)
            {
                assets.Add(new AssetInfo(names[i], 0, offset, sizes[i]));
                offset += sizes[i];
            }
            return assets;
        }

        private List<AssetInfo> ReadMultiFileLib(AgsReader reader)
        {
            if (reader.ReadByte() != 0)
                throw new AgsFormatException($"'{FilePath}' is not the first part of a multi-file library.");

            return Version switch
            {
                30 => ReadV30(reader),
                21 => ReadV21(reader),
                20 => ReadV20(reader),
                _ => ReadV10(reader),
            };
        }

        private List<AssetInfo> ReadV10(AgsReader reader)
        {
            int libCount = reader.ReadCount("library file", 1000);
            var libs = new string[libCount];
            for (int i = 0; i < libCount; i++)
                libs[i] = reader.ReadFixedString(V10LibFileLen);
            LibraryFiles = libs;

            int count = reader.ReadCount("asset", 100_000);
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                byte[] buf = reader.ReadBytes(V10AssetFileLen);
                if (Version >= 11)
                    DecryptName(buf);
                names[i] = reader.Decode(buf);
            }
            return ReadOffsetsSizesLibs(reader, names);
        }

        private List<AssetInfo> ReadV20(AgsReader reader)
        {
            int libCount = reader.ReadCount("library file", 1000);
            var libs = new string[libCount];
            for (int i = 0; i < libCount; i++)
                libs[i] = reader.ReadCString(MaxDataFileLen);
            LibraryFiles = libs;

            int count = reader.ReadCount("asset", 100_000);
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                int len = reader.ReadInt16() / 5;
                if (len < 0 || len > MaxAssetFileLen)
                    throw new AgsFormatException($"Asset name too long ({len}).");
                byte[] buf = reader.ReadBytes(len);
                DecryptName(buf);
                names[i] = reader.Decode(buf);
            }
            return ReadOffsetsSizesLibs(reader, names);
        }

        private static List<AssetInfo> ReadOffsetsSizesLibs(AgsReader reader, string[] names)
        {
            int count = names.Length;
            var offsets = new long[count];
            var sizes = new long[count];
            for (int i = 0; i < count; i++) offsets[i] = reader.ReadUInt32();
            for (int i = 0; i < count; i++) sizes[i] = reader.ReadUInt32();
            var assets = new List<AssetInfo>(count);
            for (int i = 0; i < count; i++)
                assets.Add(new AssetInfo(names[i], reader.ReadByte(), offsets[i], sizes[i]));
            return assets;
        }

        private List<AssetInfo> ReadV21(AgsReader reader)
        {
            int rand = reader.ReadInt32() + EncryptionRandSeed;

            int libCount = ReadEncInt32(reader, ref rand);
            if (libCount < 0 || libCount > 1000)
                throw new AgsFormatException($"Invalid library file count {libCount}.");
            var libs = new string[libCount];
            for (int i = 0; i < libCount; i++)
                libs[i] = ReadEncString(reader, MaxDataFileLen, ref rand);
            LibraryFiles = libs;

            int count = ReadEncInt32(reader, ref rand);
            if (count < 0 || count > 100_000)
                throw new AgsFormatException($"Invalid asset count {count}.");
            var names = new string[count];
            for (int i = 0; i < count; i++)
                names[i] = ReadEncString(reader, MaxAssetFileLen, ref rand);

            var offsets = new long[count];
            var sizes = new long[count];
            for (int i = 0; i < count; i++) offsets[i] = (uint)ReadEncInt32(reader, ref rand);
            for (int i = 0; i < count; i++) sizes[i] = (uint)ReadEncInt32(reader, ref rand);
            var assets = new List<AssetInfo>(count);
            for (int i = 0; i < count; i++)
            {
                int libIndex = (byte)(reader.ReadByte() - NextPseudoRand(ref rand));
                assets.Add(new AssetInfo(names[i], libIndex, offsets[i], sizes[i]));
            }
            return assets;
        }

        private List<AssetInfo> ReadV30(AgsReader reader)
        {
            reader.ReadInt32(); // reserved flags
            int libCount = reader.ReadCount("library file", 1000);
            var libs = new string[libCount];
            for (int i = 0; i < libCount; i++)
                libs[i] = reader.ReadCString();
            LibraryFiles = libs;

            int count = reader.ReadCount("asset", 1_000_000);
            var assets = new List<AssetInfo>(count);
            for (int i = 0; i < count; i++)
            {
                string name = reader.ReadCString();
                int libIndex = reader.ReadByte();
                long offset = reader.ReadInt64();
                long size = reader.ReadInt64();
                assets.Add(new AssetInfo(name, libIndex, offset, size));
            }
            return assets;
        }

        private static void DecryptName(byte[] buf)
        {
            for (int i = 0, adx = 0; i < buf.Length; i++, adx = adx == 10 ? 0 : adx + 1)
            {
                buf[i] = (byte)(buf[i] - EncryptionString[adx]);
                if (buf[i] == 0)
                    break;
            }
        }

        private static int NextPseudoRand(ref int rand)
        {
            rand = unchecked(rand * 214013 + 2531011);
            return (rand >> 16) & 0x7fff;
        }

        private static int ReadEncInt32(AgsReader reader, ref int rand)
        {
            Span<byte> buf = stackalloc byte[4];
            for (int i = 0; i < 4; i++)
                buf[i] = (byte)(reader.ReadByte() - NextPseudoRand(ref rand));
            return BitConverter.ToInt32(buf);
        }

        private static string ReadEncString(AgsReader reader, int maxLen, ref int rand)
        {
            var bytes = new List<byte>();
            for (int read = 1; ; read++)
            {
                byte b = (byte)(reader.ReadByte() - NextPseudoRand(ref rand));
                if (b == 0)
                    break;
                bytes.Add(b);
                if (read >= maxLen)
                    break; // the engine stops here as well to avoid an endless loop
            }
            return Encoding.Latin1.GetString(bytes.ToArray());
        }
    }
}

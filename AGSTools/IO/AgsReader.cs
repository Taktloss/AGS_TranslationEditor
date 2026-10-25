using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AGSTools
{
    /// <summary>
    /// Little-endian reader with the string conventions used throughout AGS data files.
    /// Every read past the end of the stream throws <see cref="AgsFormatException"/>.
    /// </summary>
    internal sealed class AgsReader : IDisposable
    {
        private readonly BinaryReader _reader;

        public AgsReader(Stream stream, Encoding textEncoding, bool leaveOpen = false)
        {
            _reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen);
            TextEncoding = textEncoding;
        }

        /// <summary>Encoding used to decode text read from the stream.</summary>
        public Encoding TextEncoding { get; set; }

        public long Position
        {
            get => _reader.BaseStream.Position;
            set => _reader.BaseStream.Position = value;
        }

        public long Length => _reader.BaseStream.Length;

        public bool AtEnd => Position >= Length;

        public byte ReadByte() { EnsureAvailable(1); return _reader.ReadByte(); }
        public sbyte ReadSByte() { EnsureAvailable(1); return _reader.ReadSByte(); }
        public short ReadInt16() { EnsureAvailable(2); return _reader.ReadInt16(); }
        public ushort ReadUInt16() { EnsureAvailable(2); return _reader.ReadUInt16(); }
        public int ReadInt32() { EnsureAvailable(4); return _reader.ReadInt32(); }
        public uint ReadUInt32() { EnsureAvailable(4); return _reader.ReadUInt32(); }
        public long ReadInt64() { EnsureAvailable(8); return _reader.ReadInt64(); }

        public byte[] ReadBytes(int count)
        {
            if (count < 0)
                throw new AgsFormatException($"Negative data length {count} at offset {Position}.");
            EnsureAvailable(count);
            return _reader.ReadBytes(count);
        }

        public void Skip(long count)
        {
            if (count < 0)
                throw new AgsFormatException($"Negative skip {count} at offset {Position}.");
            EnsureAvailable(count);
            Position += count;
        }

        /// <summary>
        /// Reads a count value and validates it against a sane upper limit,
        /// which catches misaligned reads early instead of allocating garbage.
        /// </summary>
        public int ReadCount(string what, int max)
        {
            long at = Position;
            int count = ReadInt32();
            if (count < 0 || count > max)
                throw new AgsFormatException($"Invalid {what} count {count} at offset {at} (expected 0..{max}).");
            return count;
        }

        /// <summary>Fixed-size char array, cut at the first null (AGS String::ReadCount).</summary>
        public string ReadFixedString(int size) => Decode(ReadBytes(size));

        /// <summary>Null-terminated string (AGS String::Read / FromStream).</summary>
        public string ReadCString(int maxChars = 5_000_000)
        {
            var bytes = new List<byte>(32);
            while (true)
            {
                byte b = ReadByte();
                if (b == 0)
                    break;
                if (bytes.Count < maxChars)
                    bytes.Add(b);
            }
            return TextEncoding.GetString(bytes.ToArray());
        }

        /// <summary>Int32 length followed by that many bytes (AGS StrUtil::ReadString).</summary>
        public string ReadPrefixedString()
        {
            int length = ReadLength();
            return Decode(ReadBytes(length));
        }

        /// <summary>Int32 length followed by encrypted bytes (AGS ReadStringDecrypt).</summary>
        public string ReadEncryptedString() => TextEncoding.GetString(ReadEncryptedBytes());

        /// <summary>Like <see cref="ReadEncryptedString"/>, but returns the raw decrypted bytes.</summary>
        public byte[] ReadEncryptedBytes()
        {
            int length = ReadLength();
            byte[] buffer = ReadBytes(length);
            int textLength = AgsEncryption.Decrypt(buffer);
            return buffer.AsSpan(0, textLength).ToArray();
        }

        public string Decode(ReadOnlySpan<byte> bytes)
        {
            int nul = bytes.IndexOf((byte)0);
            return TextEncoding.GetString(nul >= 0 ? bytes[..nul] : bytes);
        }

        private int ReadLength()
        {
            long at = Position;
            int length = ReadInt32();
            if (length < 0 || length > Length - Position)
                throw new AgsFormatException($"Invalid string length {length} at offset {at}.");
            return length;
        }

        private void EnsureAvailable(long count)
        {
            if (Position + count > Length)
                throw new AgsFormatException($"Unexpected end of data at offset {Position} (needed {count} more bytes).");
        }

        public void Dispose() => _reader.Dispose();
    }
}

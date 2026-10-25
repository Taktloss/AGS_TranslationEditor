using System;

namespace AGSTools
{
    /// <summary>
    /// The simple text "encryption" AGS uses for translation files, global messages,
    /// room messages and text parser words (see Common/data/data_helpers.cpp).
    /// All arithmetic is done on bytes, exactly like the engine does it.
    /// </summary>
    internal static class AgsEncryption
    {
        private static readonly byte[] Password = "Avis Durgan"u8.ToArray();

        /// <summary>
        /// Decrypts a buffer in place. Like the engine, decryption stops at the first byte that
        /// decrypts to 0. Returns the length of the decrypted string (without terminator).
        /// </summary>
        public static int Decrypt(Span<byte> buffer)
        {
            for (int i = 0, p = 0; i < buffer.Length; i++, p = p == 10 ? 0 : p + 1)
            {
                buffer[i] = (byte)(buffer[i] - Password[p]);
                if (buffer[i] == 0)
                    return i;
            }
            return buffer.Length;
        }

        /// <summary>
        /// Encrypts a buffer in place, up to and including the first 0 byte.
        /// </summary>
        public static void Encrypt(Span<byte> buffer)
        {
            for (int i = 0, p = 0; i < buffer.Length; i++, p = p == 10 ? 0 : p + 1)
            {
                byte plain = buffer[i];
                buffer[i] = (byte)(plain + Password[p]);
                if (plain == 0)
                    break;
            }
        }

        /// <summary>
        /// Returns the encrypted, null-terminated form of <paramref name="text"/>.
        /// </summary>
        public static byte[] EncryptString(ReadOnlySpan<byte> text)
        {
            byte[] buffer = new byte[text.Length + 1];
            text.CopyTo(buffer);
            Encrypt(buffer);
            return buffer;
        }
    }
}

using System;
using System.IO;
using System.Text;

namespace AGSTools
{
    public class GameInfo
    {
        public string Version { get; set; } = "";
        public string GameTitle { get; set; } = "";
        public string GameUID { get; set; } = "";

        private const int GameFileHeaderOffset = 0x1E;
        private const int GameUIDOffset = 0x6F4;
        private const int GameTitleLength = 0x40;

        /// <summary>
        /// Get Game information (GameTitle and GameUID) from AGS EXE File
        /// </summary>
        /// <param name="filename">Game EXE File</param>
        public static GameInfo? GetGameInfo(string filename, int version = 0)
        {
            // Read-only access: game folders are often write-protected (Steam, GOG, system installs)
            using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Search for the common prefix — works for all AGS versions (v2.x, v3.x)
                long sigPosition = FindSignature(fs, Encoding.Latin1.GetBytes("Adventure Creator Game File v2"));
                if (sigPosition < 0)
                    return null;

                // Gameinfo class to hold the information
                GameInfo info = new GameInfo();

                using (BinaryReader br = new BinaryReader(fs, Encoding.Latin1, leaveOpen: true))
                {
                    //Set the position to start reading
                    fs.Position = sigPosition + GameFileHeaderOffset;

                    //Dummy read 4 bytes
                    br.ReadInt32();
                    //Get the AGS version the game was compiled with
                    int VersionLength = br.ReadInt32();
                    info.Version = new string(br.ReadChars(VersionLength));

                    // Some AGS versions have an extra int32 after the version string
                    // Detect this by checking if Version looks like "3.x.x.x"
                    if (info.Version.Length > 0 && info.Version[0] >= '3')
                        br.ReadInt32();

                    //Calculate and save GameUID position for later use
                    long GameUIDPosition = fs.Position + GameUIDOffset;

                    //Get the game title
                    string gameTitle = new string(br.ReadChars(GameTitleLength));
                    int nullIdx = gameTitle.IndexOf('\0', StringComparison.Ordinal);
                    info.GameTitle = nullIdx >= 0 ? gameTitle.Substring(0, nullIdx) : gameTitle.TrimEnd();

                    //Read the GameUID
                    fs.Position = GameUIDPosition;
                    int GameUID = br.ReadInt32();
                    GameUID = SwapEndianness(GameUID);
                    info.GameUID = GameUID.ToString("X");

                    //return the Game information
                    return info;
                }
            }
        }

        /// <summary>
        /// Returns the offset of the first occurrence of <paramref name="signature"/> in the stream, or -1.
        /// Consecutive reads overlap so a match spanning two blocks is not missed.
        /// </summary>
        private static long FindSignature(Stream stream, byte[] signature)
        {
            const int blockSize = 64 * 1024;
            int overlap = signature.Length - 1;
            byte[] buffer = new byte[blockSize + overlap];
            int carried = 0;
            long bufferStart = 0;

            stream.Position = 0;
            int read;
            while ((read = stream.Read(buffer, carried, blockSize)) > 0)
            {
                int valid = carried + read;
                int idx = buffer.AsSpan(0, valid).IndexOf(signature);
                if (idx >= 0)
                    return bufferStart + idx;

                // Keep the tail of this block in front of the next one
                carried = Math.Min(overlap, valid);
                Buffer.BlockCopy(buffer, valid - carried, buffer, 0, carried);
                bufferStart += valid - carried;
            }
            return -1;
        }

        private static int SwapEndianness(int value)
        {
            var b1 = (value >> 0) & 0xff;
            var b2 = (value >> 8) & 0xff;
            var b3 = (value >> 16) & 0xff;
            var b4 = (value >> 24) & 0xff;

            return b1 << 24 | b2 << 16 | b3 << 8 | b4 << 0;
        }
    }
}
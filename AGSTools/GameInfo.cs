using System;
using System.IO;
using System.Text;

namespace AGSTools
{
    public class GameInfo
    {
        public string Version { get; set; }
        public string GameTitle { get; set; }
        public string GameUID { get; set; }

        private const int GameFileHeaderOffset = 0x1E;
        private const int GameUIDOffset = 0x6F4;
        private const int GameTitleLength = 0x40;

        /// <summary>
        /// Get Game information (GameTitle and GameUID) from AGS EXE File
        /// </summary>
        /// <param name="filename">Game EXE File</param>
        public static GameInfo GetGameInfo(string filename, int version = 0)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Open))
            {
                // Search for the common prefix — works for all AGS versions (v2.x, v3.x)
                string searchString = "Adventure Creator Game File v2";

                // Gameinfo class to hold the information
                GameInfo info = new GameInfo();

                const int blockSize = 1024;
                long fileSize = fs.Length;
                long position = 0;

                //Read AGS EXE and search for string, should actually never reach the end
                using (BinaryReader br = new BinaryReader(fs, Encoding.Latin1, leaveOpen: true))
                {
                    while (position < fileSize)
                    {
                        byte[] data = br.ReadBytes(blockSize);
                        string tempData = Encoding.Latin1.GetString(data);

                        //If the search string is found get the game info
                        if (tempData.Contains(searchString, StringComparison.Ordinal))
                        {
                            int startPosition = tempData.IndexOf(searchString, 0, StringComparison.Ordinal);
                            //Calculate and set the position to start reading
                            startPosition = startPosition + GameFileHeaderOffset + (int)position;
                            fs.Position = startPosition;

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
                        //Calculate new postiton
                        position = position + blockSize;
                    }
                }
            }
            //if nothing found return just null
            return null;
        }

        /// <summary>
        /// Help function to swap between endianns
        /// </summary>
        /// <param name="value"></param>
        /// <returns>Value to swap</returns>
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
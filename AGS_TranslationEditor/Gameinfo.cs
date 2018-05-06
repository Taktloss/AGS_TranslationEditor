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

        /// <summary>
        /// Get Game information (GameTitle and GameUID) from AGS EXE File
        /// </summary>
        /// <param name="filename">Game EXE File</param>
        public static GameInfo GetGameInfo(string filename)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Open))
            {
                //The string we want to search in the AGS Game executable
                const string searchString = "Adventure Creator Game File v2";
                // Gameinfo class to hold the information
                GameInfo info = new GameInfo();

                const int blockSize = 1024;
                long fileSize = fs.Length;
                long position = 0;

                //Read AGS EXE and search for string, should actually never reach the end
                BinaryReader br = new BinaryReader(fs);
                while (position < fileSize)
                {
                    byte[] data = br.ReadBytes(blockSize);
                    string tempData = Encoding.Default.GetString(data);

                    //If the search string is found get the game info
                    if (tempData.Contains(searchString))
                    {
                        int startPosition = tempData.IndexOf(searchString, 0, StringComparison.Ordinal);
                        //Calculate and set the position to start reading
                        startPosition = startPosition + 0x1E + (int)position;
                        fs.Position = startPosition;

                        //Dummy read 4 bytes
                        br.ReadInt32();
                        //Get the AGS version the game was compiled with
                        int VersionLength = br.ReadInt32();
                        info.Version = new string(br.ReadChars(VersionLength));

                        //Calculate and save GameUID position for later use
                        long GameUIDPosition = fs.Position + 0x6f4;

                        //Get the game title
                        string gameTitle = new string(br.ReadChars(0x40));
                        info.GameTitle = gameTitle.Substring(0, gameTitle.IndexOf("\0", StringComparison.Ordinal));

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
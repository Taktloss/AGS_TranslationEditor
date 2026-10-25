using System;
using System.IO;

namespace AGSTools
{
    /// <summary>
    /// Writes files via a temporary file in the same directory, so an existing file is only
    /// replaced once the new content has been written completely.
    /// </summary>
    internal static class AtomicFile
    {
        public static void Write(string path, Action<Stream> write)
        {
            string fullPath = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(fullPath) ?? ".";
            string temp = Path.Combine(dir, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                    write(fs);
                File.Move(temp, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
    }
}

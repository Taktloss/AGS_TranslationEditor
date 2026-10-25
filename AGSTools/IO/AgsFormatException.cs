using System;
using System.IO;

namespace AGSTools
{
    /// <summary>
    /// Thrown when an AGS file (game data, room, translation, ...) is malformed or uses an unsupported format.
    /// </summary>
    public class AgsFormatException : IOException
    {
        public AgsFormatException(string message) : base(message) { }

        public AgsFormatException(string message, Exception innerException) : base(message, innerException) { }
    }
}

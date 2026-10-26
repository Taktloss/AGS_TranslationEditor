namespace AGSTools
{
    /// <summary>
    /// The 2.x "new interaction" structures (Common/game/interactions.cpp) in game data and rooms.
    /// They hold no text, so they are only skipped.
    /// </summary>
    internal static class LegacyInteraction
    {
        private const int Version = 1;
        private const int MaxEvents = 30;
        private const int ValueCount = 5;
        private const int ValueSize = 1 + 3 + 4 + 4; // type, padding, value, extra

        /// <summary>Size of an interaction variable: 23-byte name, type, value.</summary>
        public const int VariableSize = 23 + 1 + 4;

        public static void Skip(AgsReader r)
        {
            int version = r.ReadInt32();
            if (version != Version)
                throw new AgsFormatException($"Unknown interaction version {version} at offset {r.Position - 4}.");
            int events = r.ReadCount("interaction event", MaxEvents);
            r.Skip(events * 4L); // event types
            var hasResponse = new bool[events];
            for (int e = 0; e < events; e++)
                hasResponse[e] = r.ReadInt32() != 0;
            for (int e = 0; e < events; e++)
                if (hasResponse[e])
                    SkipCommandList(r);
        }

        private static void SkipCommandList(AgsReader r)
        {
            int count = r.ReadCount("interaction command", 1000);
            r.ReadInt32(); // times run
            var hasChildren = new bool[count];
            for (int c = 0; c < count; c++)
            {
                r.Skip(4 + 4 + ValueCount * ValueSize); // vtable pointer, type, values
                hasChildren[c] = r.ReadInt32() != 0;
                r.Skip(4); // parent pointer
            }
            for (int c = 0; c < count; c++)
                if (hasChildren[c])
                    SkipCommandList(r);
        }
    }
}

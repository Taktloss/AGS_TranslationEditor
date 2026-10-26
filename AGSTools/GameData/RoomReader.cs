using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AGSTools
{
    /// <summary>Translatable contents of a compiled room (roomN.crm).</summary>
    public sealed class RoomData
    {
        public RoomData(string name) => Name = name;

        public string Name { get; }
        public int Version { get; internal set; }
        public List<string> HotspotNames { get; } = new();
        public List<string> ObjectNames { get; } = new();
        public List<string> Messages { get; } = new();
        internal CompiledScript? Script { get; set; }

        /// <summary>Null if the room was read completely, otherwise the reason it was not.</summary>
        public string? Error { get; internal set; }
    }

    /// <summary>
    /// Reads compiled room files. Port of the block reader in Common/game/room_file.cpp,
    /// supporting room formats from AGS 2.5 (kRoomVersion_250a) on.
    /// </summary>
    internal static class RoomReader
    {
        // kRoomVersion_*
        private const int V250a = 16;
        private const int V251 = 18;
        private const int V253 = 19;
        private const int V255b = 21;
        private const int V261 = 22;
        private const int V262 = 23;
        private const int V270 = 24;
        private const int V272 = 25;
        private const int V300a = 26;
        private const int V303a = 28;
        private const int V3415 = 31;
        private const int V350 = 32;

        private const int BlockMain = 1;
        private const int BlockCompScript3 = 7;
        private const int BlockObjectNames = 5;

        private const int MaxRoomObjects = 256;
        private const int MinLegacyHotspots = 20;
        private const int MaxLegacyMessageLength = 3000;

        public static RoomData Read(string name, byte[] data, Encoding textEncoding)
        {
            var room = new RoomData(name);
            using var r = new AgsReader(new MemoryStream(data, writable: false), textEncoding);
            try
            {
                room.Version = r.ReadUInt16();
                if (room.Version < V250a)
                    throw new AgsFormatException($"Room format {room.Version} (before AGS 2.5) is not supported.");

                bool longBlockLengths = room.Version >= V350;
                while (!r.AtEnd)
                {
                    sbyte id = r.ReadSByte();
                    if (id < 0)
                        break;

                    if (id == 0)
                        r.Skip(16); // extension name; no extension carries translatable text
                    long length = id == 0 || longBlockLengths ? r.ReadInt64() : r.ReadInt32();
                    long start = r.Position;
                    if (length < 0 || start + length > r.Length)
                        throw new AgsFormatException($"Invalid length of room block {id} at offset {start}.");

                    switch (id)
                    {
                        case BlockMain:
                            ReadMainBlock(r, room);
                            break;
                        case BlockObjectNames:
                            ReadObjectNames(r, room);
                            break;
                        case BlockCompScript3:
                            room.Script = CompiledScript.Read(r);
                            break;
                    }
                    r.Position = start + length;
                }
            }
            catch (AgsFormatException ex)
            {
                room.Error = ex.Message;
            }
            return room;
        }

        private static void ReadMainBlock(AgsReader r, RoomData room)
        {
            int version = room.Version;
            r.ReadInt32(); // background bpp
            int walkBehinds = r.ReadInt16();
            r.Skip(walkBehinds * 2L);

            int hotspots = r.ReadCount("hotspot", 1000);
            if (version <= V272 && hotspots == 0)
                hotspots = MinLegacyHotspots;
            r.Skip(hotspots * 4L); // walk-to points
            for (int i = 0; i < hotspots; i++)
            {
                room.HotspotNames.Add(version >= V3415 ? r.ReadPrefixedString()
                    : version >= V303a ? r.ReadCString()
                    : r.ReadFixedString(30));
            }
            if (version >= V270)
            {
                for (int i = 0; i < hotspots; i++)
                {
                    if (version >= V3415)
                        r.ReadPrefixedString();
                    else
                        r.Skip(20); // script name
                }
            }

            if (r.ReadInt32() > 0)
                throw new AgsFormatException("Legacy poly-point areas are not supported.");
            r.Skip(4 * 2); // edges

            int objects = r.ReadUInt16();
            if (objects > MaxRoomObjects)
                throw new AgsFormatException($"Too many room objects ({objects}).");
            r.Skip(objects * 10L);

            if (version >= V253)
            {
                int localVars = r.ReadCount("interaction variable", 10_000);
                r.Skip(localVars * (long)LegacyInteraction.VariableSize);
            }

            if (version < V300a)
            {
                // 2.x interactions of hotspots, objects and the room itself
                for (int i = 0; i < hotspots + objects + 1; i++)
                    LegacyInteraction.Skip(r);
            }

            int regions = 0;
            if (version >= V255b)
            {
                regions = r.ReadCount("region", 1000);
                if (version < V300a)
                    for (int i = 0; i < regions; i++)
                        LegacyInteraction.Skip(r);
            }

            if (version >= V300a)
            {
                // Event handler names of the room, hotspots, objects and regions
                int handlerSets = 1 + hotspots + objects + regions;
                for (int i = 0; i < handlerSets; i++)
                {
                    int events = r.ReadCount("event handler", 1000);
                    for (int e = 0; e < events; e++)
                        r.ReadCString();
                }
            }

            r.Skip(objects * 4L); // object baselines
            r.Skip(2 * 2); // width, height
            if (version >= V262)
                r.Skip(objects * 2L); // object flags
            r.ReadInt16(); // mask resolution
            int walkAreas = r.ReadCount("walkable area", 1000);
            // scaling far, player view; from 2.51 also scaling near, top, bottom
            r.Skip(walkAreas * 2L * (version >= V251 ? 5 : 2));
            r.Skip(11); // password
            r.Skip(6 + 4); // options + reserved

            int messages = r.ReadInt16();
            if (messages < 0 || messages > 10_000)
                throw new AgsFormatException($"Invalid room message count {messages}.");
            if (version >= V272)
                r.ReadInt32(); // game id
            r.Skip(messages * 2L); // display flags
            for (int i = 0; i < messages; i++)
                room.Messages.Add(version < V261 ? r.ReadCString(MaxLegacyMessageLength + 1) : r.ReadEncryptedString());
            // remaining data is images and region settings
        }

        private static void ReadObjectNames(AgsReader r, RoomData room)
        {
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
                room.ObjectNames.Add(room.Version >= V3415 ? r.ReadPrefixedString() : r.ReadFixedString(30));
        }
    }
}

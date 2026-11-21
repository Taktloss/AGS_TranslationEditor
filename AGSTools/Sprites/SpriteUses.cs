using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AGSTools
{
    /// <summary>
    /// Where a game shows its sprites: as GUI backgrounds, button images and room objects. Words painted into
    /// pictures are mostly in such places (menus, notes, signs), so this narrows the search for text in images.
    /// Scripts can show other sprites at runtime; those are not found here.
    /// </summary>
    public static class SpriteUses
    {
        /// <summary>Uses per sprite number, e.g. "GUI gMenu: background", "GUI button btnQuit", "room22: object oNewGame".</summary>
        public static Dictionary<int, List<string>> Find(string gamePath, IProgress<string>? progress = null)
        {
            GameFileSource source = GameFileSource.Open(gamePath);
            GameData game = GameDataReader.Read(source.ReadMainGameData());
            var uses = new Dictionary<int, List<string>>();
            void Add(int sprite, string use)
            {
                if (sprite <= 0)
                    return;
                if (!uses.TryGetValue(sprite, out List<string>? list))
                    uses[sprite] = list = new List<string>();
                if (!list.Contains(use))
                    list.Add(use);
            }

            foreach (GuiImage image in game.GuiImages)
            {
                string owner = image.Owner.Length > 0 ? image.Owner : "?";
                Add(image.Sprite, image.Role switch
                {
                    GuiImageRole.Background => $"GUI {owner}: background",
                    GuiImageRole.ButtonMouseOver => $"GUI button {owner} (mouse over)",
                    GuiImageRole.ButtonPushed => $"GUI button {owner} (pushed)",
                    _ => $"GUI button {owner}",
                });
            }

            progress?.Report("Reading rooms...");
            foreach (var (name, read) in source.EnumerateRooms())
            {
                RoomData room;
                try
                {
                    room = RoomReader.Read(name, read(), game.TextEncoding);
                }
                catch (Exception ex) when (ex is IOException or AgsFormatException)
                {
                    continue;
                }
                string roomName = Path.GetFileNameWithoutExtension(name);
                for (int i = 0; i < room.ObjectSprites.Count; i++)
                {
                    string objectName = i < room.ObjectNames.Count && room.ObjectNames[i].Length > 0 ? room.ObjectNames[i] : $"#{i}";
                    Add(room.ObjectSprites[i], $"{roomName}: object {objectName}");
                }
            }
            return uses;
        }
    }
}

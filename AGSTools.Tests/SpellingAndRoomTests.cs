using System.Net;
using System.Text;
using TranslationApi;

namespace AGSTools.Tests;

public class SpellingAndRoomTests
{
    // ── Room backgrounds ──

    [Fact]
    public void Lz_stream_has_literals_and_back_references_with_distance_and_length()
    {
        // Flags 0x08: three literals, then a reference (length 6 - 3 = 3 in the high nibble, distance 3 - 1 = 2)
        byte[] packed = { 0x08, (byte)'A', (byte)'B', (byte)'C', 0x02, 0x30 };
        byte[] raw = AgsLzw.Decode(packed, 9, out int used);
        Assert.Equal("ABCABCABC", Encoding.ASCII.GetString(raw));
        Assert.Equal(packed.Length, used);
    }

    [Fact]
    public void Lz_compression_round_trips()
    {
        var random = new Random(7);
        var data = new byte[70_000];
        for (int i = 0; i < data.Length; i++)
            data[i] = i % 5000 < 3000 ? (byte)(i % 13) : (byte)random.Next(256);
        byte[] packed = AgsLzw.Encode(data);
        Assert.True(packed.Length < data.Length);
        Assert.Equal(data, AgsLzw.Decode(packed, data.Length, out _));
    }

    private static byte[] Pixels(int width, int height, uint seed) =>
        Enumerable.Range(0, width * height).SelectMany(i => BitConverter.GetBytes(0xFF000000u | (uint)(i * 2654435761u + seed) & 0xFFFFFF)).ToArray();

    private static RoomBuilder RoomWithBackgrounds(int frames)
    {
        var room = new RoomBuilder { Version = 25 };
        room.Hotspots.Add("Door");
        room.Messages.Add("A message");
        for (int i = 0; i < frames; i++)
            room.Backgrounds.Add((40, 30, Pixels(40, 30, (uint)i * 1000)));
        return room;
    }

    [Fact]
    public void Room_backgrounds_are_found_and_decoded()
    {
        byte[] data = RoomWithBackgrounds(3).Build();
        RoomImages images = RoomImages.Read("room7.crm", data);
        Assert.Equal(7, images.Number);
        Assert.Equal(3, images.Backgrounds.Count);
        Assert.All(images.Backgrounds, b => Assert.Equal((40, 30, 4), (b.Width, b.Height, b.BytesPerPixel)));
        SpriteImage second = images.Read(1);
        Assert.Equal(0xFF000000u | unchecked(5 * 2654435761u + 1000) & 0xFFFFFF, second[5, 0]);
    }

    [Fact]
    public void Replacing_a_background_keeps_the_rest_of_the_room()
    {
        byte[] data = RoomWithBackgrounds(2).Build();
        RoomImages images = RoomImages.Read("room3.crm", data);
        SpriteImage changed = images.Read(0);
        for (int x = 0; x < 20; x++)
            changed[x, 10] = 0xFF123456;

        byte[] patched = images.WritePatched(new Dictionary<int, SpriteImage> { [0] = changed });
        RoomImages again = RoomImages.Read("room3.crm", patched);
        Assert.Equal(changed.Pixels, again.Read(0).Pixels);
        Assert.Equal(images.Read(1).Pixels, again.Read(1).Pixels);
        // Texts and the masks after the image are untouched
        RoomData room = RoomReader.Read("room3.crm", patched, Encoding.Latin1);
        Assert.Null(room.Error);
        Assert.Equal(new[] { "Door" }, room.HotspotNames);
        Assert.Equal(new[] { "A message" }, room.Messages);
        Assert.Contains(Convert.ToHexString(RoomBuilder.MaskData), Convert.ToHexString(patched));
        // No replacement: the very same bytes
        Assert.Equal(data, images.WritePatched(new Dictionary<int, SpriteImage>()));
    }

    [Fact]
    public void Room_background_must_keep_its_size()
    {
        RoomImages images = RoomImages.Read("room1.crm", RoomWithBackgrounds(1).Build());
        Assert.Throws<ArgumentException>(() => images.WritePatched(new Dictionary<int, SpriteImage> { [0] = new SpriteImage(41, 30) }));
    }

    [Theory]
    [InlineData("room005-bg1.png", 5, 0)]
    [InlineData("room12-bg3.png", 12, 2)]
    [InlineData("ROOM7.PNG", 7, 0)]
    public void Room_png_names_give_room_and_background(string name, int room, int frame)
    {
        Assert.Equal((room, frame), GameRooms.FromFileName(name));
        Assert.Equal($"room{room:D3}-bg{frame + 1}.png", GameRooms.PngName(room, frame));
    }

    [Theory]
    [InlineData("sprite00042.png")]
    [InlineData("room5-bg0.png")]
    [InlineData("roomy.png")]
    public void Other_png_names_are_no_rooms(string name) => Assert.Null(GameRooms.FromFileName(name));

    [Fact]
    public void Installer_accepts_room_files()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(dir.File("room12.crm"), new byte[] { 1 });
        File.WriteAllBytes(dir.File("readme.txt"), new byte[] { 1 });
        Assert.Single(TranslationInstaller.CheckExtraFiles(new[] { dir.File("room12.crm") }));
        Assert.Throws<IOException>(() => TranslationInstaller.CheckExtraFiles(new[] { dir.File("readme.txt") }));
    }

    // ── Spelling ──

    private static SpellChecker Dictionary(TempDir dir)
    {
        File.WriteAllText(dir.File("xx_XX.aff"), "SET UTF-8\nTRY esianrtolcdugmphbyfvkwzESIANRTOLCDUGMPHBYFVKWZ\n");
        File.WriteAllText(dir.File("xx_XX.dic"), "9\ndas\nist\nein\nTest\nHallo\nzweite\nZeile\nmit\nund\n");
        return SpellChecker.Load(dir.File("xx_XX.dic"));
    }

    [Fact]
    public void Suggestions_closest_to_the_misspelling_come_first()
    {
        // Hunspell's own order for "Vieleicht" (de_DE) puts the right word 6th
        var hunspell = new[] { "Vielteich", "Vieleiche", "Vielsicht", "Vielgicht", "Vieleich-", "Vielleicht", "Viel eicht" };
        Assert.Equal("Vielleicht", SpellChecker.Rank("Vieleicht", hunspell)[0]);
        // ß counts as ss, so "weiß" stays ahead of "weis" for "weiss"
        Assert.Equal(new[] { "weiß", "weis" }, SpellChecker.Rank("weiss", new[] { "weis", "weiß" }));
        // A swapped pair of letters is one step; equal distances keep Hunspell's order
        Assert.Equal(new[] { "Fehler", "Fehlern" }, SpellChecker.Rank("Fehlre", new[] { "Fehlern", "Fehler" }));
        Assert.Equal(new[] { "verschlossen", "verschlosse" }, SpellChecker.Rank("verschlosen", new[] { "verschlosse", "verschlossen" }));
        Assert.Equal(new[] { "Hund", "Hand" }, SpellChecker.Rank("Hunt", new[] { "Hund", "Hand" }));
        Assert.Equal(new[] { "Mitte", "mitten" }, SpellChecker.Rank("mitte", new[] { "mitten", "Mitte" }));
    }

    [Fact]
    public void Spell_check_skips_markup_names_from_the_original_and_abbreviations()
    {
        using var dir = new TempDir();
        SpellChecker spelling = Dictionary(dir);
        Assert.Equal("xx_XX", spelling.Name);

        var errors = spelling.Check("&12 Hallo Joey, das ist ein Tset mit %s und @SCORE@[zweite Zeiel. KI", "&12 Hello Joey");
        Assert.Equal(new[] { "Tset", "Zeiel" }, errors.Select(e => e.Word));
        string text = "&12 Hallo Joey, das ist ein Tset mit %s und @SCORE@[zweite Zeiel. KI";
        Assert.All(errors, e => Assert.Equal(e.Word, text.Substring(e.Start, e.Length)));
        Assert.Contains("Test", spelling.Suggest("Tset"));

        spelling.AddWord("Tset");
        Assert.Equal(new[] { "Zeiel" }, spelling.Check(text).Where(e => e.Word != "Joey").Select(e => e.Word));
    }

    [Fact]
    public void Spell_check_reports_unknown_words_in_the_translation_check()
    {
        using var dir = new TempDir();
        SpellChecker spelling = Dictionary(dir);
        var doc = new TranslationDocument();
        doc.Entries["This is a test"] = "das ist ein Tset";
        doc.Entries["Hello"] = "Hallo";
        var issues = TranslationCheck.Run(doc, spelling: spelling).Where(i => i.Category == TranslationCheck.Spelling).ToList();
        CheckIssue issue = Assert.Single(issues);
        Assert.Equal("This is a test", issue.Key);
        Assert.Contains("\"Tset\" (Test", issue.Message);
    }

    // ── LanguageTool ──

    private sealed class FakeHandler : HttpMessageHandler
    {
        public string? Sent;
        public required string Answer;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Answer) };
        }
    }

    [Fact]
    public async Task LanguageTool_matches_are_mapped_to_their_texts_with_markup_hidden()
    {
        // Texts are joined as "Das ist gut.\n\n&5 Er gehen %s heim.\n\n"; the second starts at 14
        var handler = new FakeHandler
        {
            Answer = """
            {"matches":[
              {"message":"Verb passt nicht","offset":20,"length":5,"replacements":[{"value":"geht"}],"rule":{"id":"VERB","category":{"name":"Grammatik"}}},
              {"message":"Leerzeichen","offset":15,"length":2,"replacements":[],"rule":{"id":"WHITESPACE_RULE"}}
            ]}
            """,
        };
        var client = new LanguageToolClient("http://localhost:8081", handler);
        Assert.False(client.IsPublic);
        var texts = new[] { "Das ist gut.", "&5 Er gehen %s heim." };
        var found = new List<GrammarMatch>();
        await client.CheckAsync(texts, "de-DE", found.AddRange);

        GrammarMatch m = Assert.Single(found);
        Assert.Equal(1, m.Index);
        Assert.Equal("gehen", texts[1].Substring(m.Offset, m.Length));
        Assert.Equal("&5 Er geht %s heim.", m.Apply(texts[1]));
        string sent = WebUtility.UrlDecode(handler.Sent!);
        Assert.DoesNotContain("&5", sent);
        Assert.DoesNotContain("%s", sent);
        Assert.Contains("language=de-DE", sent);
    }

    [Fact]
    public void Corrections_apply_near_their_place_after_earlier_changes()
    {
        Assert.Equal("ein Hund und eine Hund", GrammarMatch.ApplyNear("ein Hund und ein Hund", "ein", 13, "eine"));
        Assert.Null(GrammarMatch.ApplyNear("ganz anders", "Hund", 3, "Katze"));
    }
}

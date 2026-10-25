using System.Text;

namespace AGSTools.Tests;

public class TraFileTests
{
    private static TranslationDocument Roundtrip(TranslationDocument doc, out List<string> warnings)
    {
        using var ms = new MemoryStream();
        warnings = new List<string>();
        TraFile.Write(ms, doc, warnings);
        ms.Position = 0;
        return TraFile.Read(ms, doc.Options.AnsiCodepage);
    }

    private static TranslationDocument Doc(params (string Source, string Translation)[] entries)
    {
        var doc = new TranslationDocument();
        foreach (var (s, t) in entries)
            doc.Entries.Add(s, t);
        return doc;
    }

    [Fact]
    public void Options_survive_roundtrip()
    {
        var doc = Doc(("Hello", "Hallo"));
        doc.Options.GameUid = 156899796;
        doc.Options.GameName = "Unavowed";
        doc.Options.NormalFont = 3;
        doc.Options.SpeechFont = 7;
        doc.Options.TextDirection = TextDirection.RightToLeft;
        doc.Options.AutoTranslateParserSaid = true;
        doc.Options.Encoding = "UTF-8";
        doc.Options.Language = "de_DE";

        TranslationOptions o = Roundtrip(doc, out _).Options;

        Assert.Equal(156899796, o.GameUid);
        Assert.Equal("Unavowed", o.GameName);
        Assert.Equal(3, o.NormalFont);
        Assert.Equal(7, o.SpeechFont);
        Assert.Equal(TextDirection.RightToLeft, o.TextDirection);
        Assert.True(o.AutoTranslateParserSaid);
        Assert.True(o.IsUtf8);
        Assert.Equal("de_DE", o.Language);
    }

    [Fact]
    public void Untranslated_entries_are_left_out()
    {
        var result = Roundtrip(Doc(("A", "B"), ("C", ""), ("D", "   ")), out _);
        Assert.Equal(new[] { "A" }, result.Entries.Keys);
    }

    [Fact]
    public void Text_starting_with_byte_that_encrypts_to_zero_is_decrypted()
    {
        // '¿' (0xBF) + 'A' (0x41) = 0x100: the encrypted first byte is 0. AGS checks the decrypted byte
        // for the terminator, so the text must not end there.
        var result = Roundtrip(Doc(("¿Qué?", "¿Was?")), out _);
        Assert.Equal("¿Was?", result.Entries["¿Qué?"]);
    }

    [Fact]
    public void Utf8_translation_keeps_all_characters()
    {
        var doc = Doc(("Hello", "Привет „Welt“ 世界"));
        doc.Options.Encoding = "UTF-8";

        var result = Roundtrip(doc, out var warnings);

        Assert.Equal("Привет „Welt“ 世界", result.Entries["Hello"]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Ansi_translation_uses_game_codepage()
    {
        // German quotes exist in Windows-1252, but not in Latin-1
        var doc = Doc(("Hello", "„Hallo“ – sagte er…"));
        doc.Options.AnsiCodepage = 1252;

        var result = Roundtrip(doc, out var warnings);

        Assert.Equal("„Hallo“ – sagte er…", result.Entries["Hello"]);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Characters_missing_from_codepage_produce_one_warning()
    {
        var doc = Doc(("A", "Привет"), ("B", "世界"), ("C", "fine"));

        Roundtrip(doc, out var warnings);

        string warning = Assert.Single(warnings);
        Assert.Contains("2 text(s)", warning);
        Assert.Contains("UTF-8", warning);
    }

    [Fact]
    public void Escape_sequences_are_compiled_like_the_ags_compiler()
    {
        // TRS text "Line1\nLine2 \"quoted\" \[bracket" becomes a real newline and quotes in the TRA;
        // "\[" stays as is (the engine shows a literal '[')
        using var ms = new MemoryStream();
        TraFile.Write(ms, Doc(("Say \\\"hi\\\"", "Line1\\nLine2 \\[x")), new List<string>());
        ms.Position = 0;

        var runtime = ReadRawDictionary(ms.ToArray());
        Assert.Equal("Line1\nLine2 \\[x", runtime["Say \"hi\""]);

        // Reading converts back to TRS form
        ms.Position = 0;
        Assert.Equal("Line1\\nLine2 \\[x", TraFile.Read(ms).Entries["Say \"hi\""]);
    }

    [Fact]
    public void Reads_legacy_files_with_wrong_game_id_block_length()
    {
        // Early versions of this tool wrote a GameID block length that was 5 bytes too large
        byte[] tra = BuildTra(w =>
        {
            byte[] name = Encrypt("Unavowed");
            w.Write(2);
            w.Write(4 + 4 + name.Length + 5);
            w.Write(156899796);
            w.Write(name.Length);
            w.Write(name);
            WriteDict(w, ("Hello", "Hallo"));
        });

        var doc = TraFile.Read(new MemoryStream(tra));

        Assert.Equal(156899796, doc.Options.GameUid);
        Assert.Equal("Unavowed", doc.Options.GameName);
        Assert.Equal("Hallo", doc.Entries["Hello"]);
    }

    [Fact]
    public void Unknown_blocks_are_skipped()
    {
        byte[] tra = BuildTra(w =>
        {
            w.Write(0);
            w.Write(Encoding.ASCII.GetBytes("ext_future\0\0\0\0\0\0"));
            w.Write(5L);
            w.Write(new byte[5]);
            w.Write(42); // unknown numbered block
            w.Write(3);
            w.Write(new byte[3]);
            WriteDict(w, ("A", "B"));
        });

        Assert.Equal("B", TraFile.Read(new MemoryStream(tra)).Entries["A"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(30)]
    public void Truncated_file_throws_format_exception(int keep)
    {
        using var ms = new MemoryStream();
        TraFile.Write(ms, Doc(("Hello", "Hallo")), new List<string>());
        byte[] truncated = ms.ToArray()[..keep];

        Assert.Throws<AgsFormatException>(() => TraFile.Read(new MemoryStream(truncated)));
    }

    [Fact]
    public void Real_tra_file_parses()
    {
        var doc = TraFile.Read(TestData.Path("Deutsch.tra"));
        Assert.NotEmpty(doc.Entries);
        Assert.DoesNotContain(doc.Entries.Values, v => v.Contains('�'));
    }

    // --- helpers building TRA files by hand ---

    private static byte[] BuildTra(Action<BinaryWriter> writeBlocks)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("AGSTranslation\0"));
        writeBlocks(w);
        w.Write(-1);
        return ms.ToArray();
    }

    private static void WriteDict(BinaryWriter w, params (string, string)[] entries)
    {
        using var block = new MemoryStream();
        using (var bw = new BinaryWriter(block, Encoding.Latin1, leaveOpen: true))
        {
            foreach (var (k, v) in entries.Append(("", "")))
            {
                GameDataBuilder.WriteEncrypted(bw, Encoding.Latin1.GetBytes(k));
                GameDataBuilder.WriteEncrypted(bw, Encoding.Latin1.GetBytes(v));
            }
        }
        w.Write(1);
        w.Write((int)block.Length);
        w.Write(block.ToArray());
    }

    private static byte[] Encrypt(string text)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        GameDataBuilder.WriteEncrypted(w, Encoding.Latin1.GetBytes(text));
        return ms.ToArray()[4..];
    }

    /// <summary>Decrypts the dictionary block without any escape processing.</summary>
    private static Dictionary<string, string> ReadRawDictionary(byte[] tra)
    {
        var r = new BinaryReader(new MemoryStream(tra));
        r.ReadBytes(15);
        var result = new Dictionary<string, string>();
        while (true)
        {
            int id = r.ReadInt32();
            if (id == -1) break;
            if (id == 0)
            {
                r.ReadBytes(16);
                long extLength = r.ReadInt64(); // read before using Position, which "+=" would evaluate first
                r.BaseStream.Position += extLength;
                continue;
            }
            int length = r.ReadInt32();
            if (id != 1) { r.BaseStream.Position += length; continue; }
            while (true)
            {
                string k = Decrypt(r.ReadBytes(r.ReadInt32())), v = Decrypt(r.ReadBytes(r.ReadInt32()));
                if (k.Length == 0 && v.Length == 0) break;
                result[k] = v;
            }
        }
        return result;
    }

    private static string Decrypt(byte[] data)
    {
        byte[] key = Encoding.ASCII.GetBytes("Avis Durgan");
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length; i++)
        {
            byte b = (byte)(data[i] - key[i % key.Length]);
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AGSTools;
using AGSTranslate;
using TranslationApi;

namespace AGSTools.Tests;

public class AiTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<string, (HttpStatusCode, string)> _respond;
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        public FakeHandler(Func<string, (HttpStatusCode, string)> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
            Requests.Add((request, body));
            (HttpStatusCode status, string response) = _respond(body);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Answers with a fixed JSON text and remembers the user messages.</summary>
    private sealed class FakeModel : ILanguageModel
    {
        private readonly Func<string, string> _answer;
        public List<string> Messages { get; } = new();
        public FakeModel(Func<string, string> answer) => _answer = answer;
        public string Name => "Fake";

        public Task<string> CompleteJsonAsync(string systemPrompt, string userMessage, IReadOnlyDictionary<string, JsonElement> schema, CancellationToken cancellationToken = default)
        {
            Messages.Add(userMessage);
            return Task.FromResult(_answer(userMessage));
        }
    }

    private static string Chat(string content) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { role = "assistant", content }, finish_reason = "stop" } },
    });

    [Fact]
    public async Task OpenAi_compatible_translator_sends_a_schema_and_reads_the_answer()
    {
        var handler = new FakeHandler(_ => (HttpStatusCode.OK, Chat("""{"translations":["Hallo","Tschüss"]}""")));
        var translator = new OpenAiCompatibleTranslator("http://localhost:11434/v1/", null, "llama3.3", handler);

        var result = await translator.TranslateAsync(new[] { "Hello", "Bye" }, new MachineTranslationOptions { TargetLanguage = "de" });

        Assert.Equal(new[] { "Hallo", "Tschüss" }, result);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:11434/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("llama3.3", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_schema", doc.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenAi_compatible_translator_falls_back_when_the_server_has_no_json_schema()
    {
        var handler = new FakeHandler(body => body.Contains("response_format")
            ? (HttpStatusCode.BadRequest, """{"error":{"message":"response_format is not supported"}}""")
            : (HttpStatusCode.OK, Chat("```json\n{\"translations\":[\"Hallo\"]}\n```")));
        var translator = new OpenAiCompatibleTranslator("https://example.test/v1", "key", "m", handler);

        var result = await translator.TranslateAsync(new[] { "Hello" }, new MachineTranslationOptions());

        Assert.Equal(new[] { "Hallo" }, result);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("JSON schema", handler.Requests[2].Body);
        Assert.Equal("Bearer", handler.Requests[0].Request.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task OpenAi_compatible_translator_reports_errors()
    {
        var unauthorized = new OpenAiCompatibleTranslator("https://example.test/v1", "bad", "m",
            new FakeHandler(_ => (HttpStatusCode.Unauthorized, """{"error":{"message":"invalid key"}}""")));
        var ex = await Assert.ThrowsAsync<MachineTranslationException>(() => unauthorized.TranslateAsync(new[] { "a" }, new MachineTranslationOptions()));
        Assert.Contains("invalid key", ex.Message);

        var cutOff = new OpenAiCompatibleTranslator("https://example.test/v1", "k", "m", new FakeHandler(_ => (HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "{\"transl" }, finish_reason = "length" } },
        }))));
        await Assert.ThrowsAsync<MachineTranslationException>(() => cutOff.TranslateAsync(new[] { "a" }, new MachineTranslationOptions()));

        Assert.Throws<MachineTranslationException>(() => new OpenAiCompatibleTranslator("ftp://x", null, "m"));
        Assert.Throws<MachineTranslationException>(() => new OpenAiCompatibleTranslator(null, null, " "));
    }

    [Fact]
    public void Only_language_models_can_review()
    {
        Assert.True(MachineTranslators.IsLanguageModel("openai"));
        Assert.False(MachineTranslators.IsLanguageModel(MachineTranslators.DeepL));
        Assert.Throws<MachineTranslationException>(() => MachineTranslators.CreateLanguageModel(MachineTranslators.Google, new ServiceSettings()));
        // A local server needs no key
        var local = MachineTranslators.CreateLanguageModel(MachineTranslators.OpenAi, new ServiceSettings { OpenAiUrl = "http://127.0.0.1:1234/v1", OpenAiModel = "qwen" });
        Assert.Contains("qwen", local.Name);
    }

    [Fact]
    public async Task Review_keeps_voice_prefix_and_spaces_and_skips_unchanged_suggestions()
    {
        var model = new FakeModel(_ => """
            {"problems":[
              {"id":0,"problem":"Tippfehler","suggestion":"Das ist gut."},
              {"id":1,"problem":"nichts","suggestion":"Alles klar"},
              {"id":7,"problem":"unbekannt","suggestion":"x"}
            ]}
            """);
        var items = new[] { new ReviewItem("&12 That is good.", "&12 Das ist gutt. "), new ReviewItem("All right", "Alles klar") };
        var found = new List<AiSuggestion>();

        await AiAssistant.ReviewAsync(model, items, new MachineTranslationOptions { TargetLanguage = "de" }, found.AddRange);

        var suggestion = Assert.Single(found);
        Assert.Equal(0, suggestion.Index);
        Assert.Equal("&12 Das ist gut. ", suggestion.Suggestion);
        Assert.Equal("Tippfehler", suggestion.Problem);
        Assert.DoesNotContain("&12", model.Messages[0]);
    }

    [Fact]
    public async Task Shorten_sends_limits_and_returns_wrapped_texts()
    {
        var model = new FakeModel(_ => """{"results":[{"id":0,"text":"Speichern"}]}""");
        var items = new[] { new ShortenItem("Save", "Spielstand speichern", 10) };

        var result = await AiAssistant.ShortenAsync(model, items, new MachineTranslationOptions());

        Assert.Equal("Speichern", Assert.Single(result).Suggestion);
        Assert.Contains("\"max_characters\": 10", model.Messages[0]);
    }

    private static GuiFit ButtonFit(int width)
    {
        var builder = new GameDataBuilder { ControlSize = (width, 10, 0) };
        builder.Buttons.Add("Save game");
        using var tmp = new TempDir();
        string game = tmp.File("game28.dta");
        File.WriteAllBytes(game, builder.Build());
        var wfn = new WfnFont();
        for (int code = 32; code < 256; code++)
            wfn.SetGlyph(code, new WfnGlyph(5, 8));
        var doc = new TranslationDocument();
        doc.Entries["Save game"] = "";
        return Assert.Single(TranslationCheck.GuiFitsFor(doc, TextExtractor.Extract(game), new[] { GameFont.FromData("agsfnt0.wfn", wfn.ToArray()) }, "Save game"));
    }

    [Fact]
    public async Task Shorten_to_fit_only_offers_answers_that_fit_and_keep_placeholders()
    {
        // 5 px per character: 40 px hold 8 characters
        var fit = ButtonFit(40);
        int round = 0;
        var model = new FakeModel(message =>
        {
            round++;
            return round == 1
                ? """{"results":[{"id":0,"text":"Speichern!!"},{"id":1,"text":"Weg"},{"id":2,"text":"Zu lang immer noch"}]}"""
                : """{"results":[{"id":0,"text":"Sichern"},{"id":1,"text":"Weg"},{"id":2,"text":"Auch zu lang"}]}""";
        });
        var items = new[]
        {
            new FitRequest("Save game", "Spielstand speichern", new[] { fit }),
            new FitRequest("Save game", "%d Spielstände", new[] { fit }),
            new FitRequest("Save game", "Viel zu langer Text", new[] { fit }),
        };

        List<FitSuggestion> results = await AiAssistant.ShortenToFitAsync(model, items, new MachineTranslationOptions());

        Assert.Equal(2, model.Messages.Count);
        Assert.Equal(FitOutcome.Fits, results[0].Outcome);
        Assert.Equal("Sichern", results[0].Suggestion);
        Assert.Equal(35, results[0].After!.Size);
        Assert.Equal(FitOutcome.MarkupChanged, results[1].Outcome);
        Assert.Null(results[1].Suggestion);
        Assert.Equal(FitOutcome.StillTooBig, results[2].Outcome);
        Assert.Null(results[2].Suggestion);
        Assert.Equal("Auch zu lang", results[2].Attempt);
        // The second request asks for less than the first
        Assert.Contains("\"max_characters\": 6", model.Messages[1]);
    }

    [Fact]
    public void Markup_check_rejects_lost_placeholders_and_new_line_breaks()
    {
        Assert.True(AiAssistant.KeepsMarkup("&5 %s hat %d Punkte", "&5 %s: %d"));
        Assert.False(AiAssistant.KeepsMarkup("&5 %s hat %d Punkte", "%s: %d"));
        Assert.False(AiAssistant.KeepsMarkup("%s hat %d Punkte", "%s Punkte"));
        Assert.False(AiAssistant.KeepsMarkup("Kurzer Text", "Kurzer[Text"));
        Assert.True(AiAssistant.KeepsMarkup("Zwei[Zeilen", "Eins"));
    }

    [Fact]
    public async Task Review_stops_when_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var model = new FakeModel(_ =>
        {
            cancellation.Cancel();
            return """{"problems":[]}""";
        });
        var items = Enumerable.Range(0, AiAssistant.ReviewBatchSize * 3).Select(i => new ReviewItem($"Line {i}", $"Zeile {i}")).ToList();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AiAssistant.ReviewAsync(model, items, new MachineTranslationOptions(), _ => { }, null, cancellation.Token));
        Assert.Single(model.Messages);
    }

    [Fact]
    public async Task OpenAi_compatible_translator_passes_cancellation_on()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new FakeHandler(_ => (HttpStatusCode.OK, Chat("""{"translations":["x"]}""")));
        var translator = new OpenAiCompatibleTranslator("http://localhost:1/v1", null, "m", handler);
        await MachineTranslation.TranslateAsync(translator, new[] { "a" }, new MachineTranslationOptions(), (_, _) => { }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MachineTranslation.TranslateAsync(translator, new[] { "b" }, new MachineTranslationOptions(), (_, _) => { }, cancellation.Token));
    }

    [Fact]
    public void Mcp_server_measures_wordings_and_caches_the_game()
    {
        using var temp = new TempDir();
        var builder = new GameDataBuilder { ControlSize = (40, 10, 0) };
        builder.Buttons.Add("Save");
        string game = temp.File("game28.dta");
        File.WriteAllBytes(game, builder.Build());
        string trs = temp.File("t.trs");
        var doc = new TranslationDocument();
        doc.Entries["Save"] = "Speichern";
        TrsFile.Write(trs, doc);
        var server = new McpServer();

        var (error, text) = Tool(server, "open_game", new { game });
        Assert.False(error, text);
        Assert.Contains("\"guiTexts\": 1", text);

        // No font file: the button has no known font, so nothing can be measured
        (error, text) = Tool(server, "check_fit", new { game, file = trs, original = "Save", translation = "Sichern" });
        Assert.False(error, text);
        Assert.Contains("\"onGui\": false", text);
        (error, text) = Tool(server, "list_sprites", new { game });
        Assert.True(error);
    }

    private static JsonObject Call(McpServer server, int id, string method, object? parameters = null)
    {
        string? answer = server.Handle(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } }));
        return (JsonObject)JsonNode.Parse(answer!)!;
    }

    private static (bool IsError, string Text) Tool(McpServer server, string name, object arguments)
    {
        JsonObject result = Call(server, 9, "tools/call", new { name, arguments })["result"]!.AsObject();
        return (result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Mcp_server_answers_the_handshake()
    {
        var server = new McpServer();
        JsonObject init = Call(server, 1, "initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "t", version = "1" } });
        Assert.Equal("2025-06-18", init["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.NotNull(init["result"]!["capabilities"]!["tools"]);

        Assert.Null(server.Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.Equal(-32601, Call(server, 2, "nope")["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32700, ((JsonObject)JsonNode.Parse(server.Handle("{not json")!)!)["error"]!["code"]!.GetValue<int>());

        var names = Call(server, 3, "tools/list")["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("write_translations", names);
        Assert.DoesNotContain(names, n => n.Contains("install"));
    }

    [Fact]
    public void Mcp_server_reads_and_writes_translations()
    {
        using var temp = new TempDir();
        string trs = temp.File("game.trs");
        var doc = new TranslationDocument();
        doc.Entries["Hello"] = "";
        doc.Entries["&5 Bye"] = "";
        TrsFile.Write(trs, doc);
        var server = new McpServer();

        var (error, text) = Tool(server, "read_translation", new { file = trs, filter = "empty", limit = 1 });
        Assert.False(error);
        Assert.Contains("\"next\": 1", text);

        (error, text) = Tool(server, "write_translations", new
        {
            file = trs,
            translations = new object[] { new { original = "Hello", translation = "Hallo" }, new { index = 1, translation = "&5 Tschüss" }, new { original = "Missing", translation = "x" } },
        });
        Assert.False(error);
        Assert.Contains("\"changed\": 2", text);
        Assert.Contains("Missing", text);

        TranslationDocument written = TrsFile.Read(trs, AgsTextEncoding.DefaultAnsiCodepage);
        Assert.Equal("Hallo", written.Entries["Hello"]);
        Assert.Equal("&5 Tschüss", written.Entries["&5 Bye"]);
        Assert.True(File.Exists(trs + ".bak"));
        Assert.Equal("", TrsFile.Read(trs + ".bak", AgsTextEncoding.DefaultAnsiCodepage).Entries["Hello"]);
    }

    [Fact]
    public void Mcp_server_does_not_write_into_the_game_folder()
    {
        using var temp = new TempDir();
        string game = temp.File("game.ags");
        File.WriteAllBytes(game, new byte[16]);
        var (error, text) = Tool(new McpServer(), "extract_texts", new { game, output = temp.File("out.trs") });
        Assert.True(error);
        Assert.Contains("game folder", text);
        Assert.False(File.Exists(temp.File("out.trs")));
    }
}

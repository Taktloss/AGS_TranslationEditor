using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

using System.Net;
using System.Text;
using System.Text.Json;
using TranslationApi;

namespace AGSTools.Tests;

public class MachineTranslationTests
{
    /// <summary>Answers every request with a fixed response and remembers the requests.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, (HttpStatusCode, string)> _respond;
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
            Requests.Add((request, body));
            (HttpStatusCode status, string response) = _respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Upper-cases texts, records batch sizes.</summary>
    private sealed class UpperTranslator : IMachineTranslator
    {
        public List<IReadOnlyList<string>> Batches { get; } = new();
        public string Name => "Upper";
        public int MaxBatchSize => 2;

        public Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, MachineTranslationOptions options, CancellationToken cancellationToken = default)
        {
            Batches.Add(texts);
            return Task.FromResult<IReadOnlyList<string>>(texts.Select(t => t.ToUpperInvariant()).ToList());
        }
    }

    [Fact]
    public void Markup_survives_the_xml_round_trip()
    {
        const string text = "Score: %d of %s[Tom & Jerry <3 \\[sic] @OVERHOTSPOT@";
        string xml = AgsMarkup.ToXml(text);

        Assert.Equal("Score: <x>%d</x> of <x>%s</x><br/>Tom &amp; Jerry &lt;3 <x>\\[</x>sic] <x>@OVERHOTSPOT@</x>", xml);
        Assert.Equal(text, AgsMarkup.FromXml(xml));
        Assert.Equal("a[b", AgsMarkup.FromXml("a<br />b"));
    }

    [Fact]
    public async Task Voice_prefix_spaces_and_empty_texts_are_kept_and_texts_are_batched()
    {
        var translator = new UpperTranslator();
        string[] texts = { "&12 Hello there ", "  ", "menu", "&3 bye" };
        var results = new string[texts.Length];

        await MachineTranslation.TranslateAsync(translator, texts, new MachineTranslationOptions(), (i, t) => results[i] = t);

        Assert.Equal(new[] { "&12 HELLO THERE ", "  ", "MENU", "&3 BYE" }, results);
        Assert.Equal(new[] { 2, 1 }, translator.Batches.Select(b => b.Count));
        Assert.Equal(new[] { "Hello there", "menu" }, translator.Batches[0]);
    }

    [Fact]
    public async Task DeepL_sends_tagged_texts_to_the_free_host_and_restores_the_markup()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.OK,
            """{"translations":[{"detected_source_language":"EN","text":"Hallo<br/>Welt <x>%s</x>"}]}"""));
        var deepl = new DeepLTranslator("secret:fx", handler);

        var result = await deepl.TranslateAsync(new[] { "Hello[world %s" }, new MachineTranslationOptions { Formality = Formality.Informal });

        Assert.Equal(new[] { "Hallo[Welt %s" }, result);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api-free.deepl.com/v2/translate", request.RequestUri!.ToString());
        Assert.Equal("DeepL-Auth-Key secret:fx", request.Headers.GetValues("Authorization").Single());
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal("Hello<br/>world <x>%s</x>", json.RootElement.GetProperty("text")[0].GetString());
        Assert.Equal("DE", json.RootElement.GetProperty("target_lang").GetString());
        Assert.Equal("EN", json.RootElement.GetProperty("source_lang").GetString());
        Assert.Equal("prefer_less", json.RootElement.GetProperty("formality").GetString());
        Assert.Equal("xml", json.RootElement.GetProperty("tag_handling").GetString());
    }

    [Fact]
    public async Task DeepL_reports_a_used_up_quota()
    {
        var deepl = new DeepLTranslator("key", new FakeHandler((_, _) => ((HttpStatusCode)456, "{}")));
        var ex = await Assert.ThrowsAsync<MachineTranslationException>(() => deepl.TranslateAsync(new[] { "Hi" }, new MachineTranslationOptions()));
        Assert.Contains("quota", ex.Message);
        Assert.Equal("https://api.deepl.com/v2/translate", deepl.Endpoint);
    }

    [Fact]
    public async Task Google_translates_each_line_on_its_own()
    {
        var handler = new FakeHandler((request, _) =>
        {
            string q = WebUtility.UrlDecode(request.RequestUri!.Query.Split("&q=")[1]);
            return (HttpStatusCode.OK, JsonSerializer.Serialize(new object[] { new object[] { new object[] { "<" + q + ">", q } } }));
        });

        var result = await new GoogleTranslator(handler).TranslateAsync(new[] { "One[Two" }, new MachineTranslationOptions());

        Assert.Equal(new[] { "<One>[<Two>" }, result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("sl=en&tl=de", handler.Requests[0].Request.RequestUri!.Query);
    }

    [Fact]
    public async Task Claude_gets_context_and_glossary_and_answers_with_structured_json()
    {
        var handler = new FakeHandler((_, _) => (HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "msg_test",
            type = "message",
            role = "assistant",
            model = ClaudeTranslator.DefaultModel,
            content = new[] { new { type = "text", text = """{"translations":["Hallo, Rosa.","Tschüss[bis morgen"]}""" } },
            stop_reason = "end_turn",
            stop_sequence = (string?)null,
            usage = new { input_tokens = 10, output_tokens = 10 },
        })));
        var claude = new ClaudeTranslator("sk-test", handler: handler);
        var options = new MachineTranslationOptions
        {
            GameTitle = "Blackwell Legacy",
            Formality = Formality.Informal,
            Glossary = Glossary.Parse(new[] { "Rosa = Rosa", "Deacon = Deacon" }),
        };

        var result = await claude.TranslateAsync(new[] { "Hello, Rosa.", "Bye[see you" }, options);

        Assert.Equal(new[] { "Hallo, Rosa.", "Tschüss[bis morgen" }, result);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("/v1/messages", request.RequestUri!.AbsolutePath);
        Assert.Contains("server-side-fallback-2026-07-01", request.Headers.GetValues("anthropic-beta").Single());
        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement root = json.RootElement;
        Assert.Equal("claude-opus-5-5", root.GetProperty("model").GetString());
        Assert.Equal("default", root.GetProperty("fallbacks").GetString());
        Assert.Equal("medium", root.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("json_schema", root.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
        string system = root.GetProperty("system").GetString()!;
        Assert.Contains("\"Blackwell Legacy\"", system);
        Assert.Contains("into German", system);
        Assert.Contains("du/dich/dein", system);
        Assert.Contains("- Rosa = Rosa", system);
        Assert.DoesNotContain("Deacon", system); // only terms that occur in this batch
        Assert.Contains("Bye[see you", root.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public void Claude_answer_with_the_wrong_number_of_lines_is_rejected()
    {
        Assert.Throws<MachineTranslationException>(() => ClaudeTranslator.ParseResponse("""{"translations":["a"]}""", 2));
        Assert.Throws<MachineTranslationException>(() => ClaudeTranslator.ParseResponse("not json", 1));
    }
}

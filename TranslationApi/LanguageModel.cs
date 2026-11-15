using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TranslationApi
{
    /// <summary>
    /// A large language model that answers with JSON following a schema. Used for translating, reviewing and
    /// shortening texts (<see cref="AiAssistant"/>).
    /// </summary>
    public interface ILanguageModel
    {
        string Name { get; }

        /// <summary>Sends a system prompt and a user message and returns the JSON answer as text.</summary>
        /// <exception cref="MachineTranslationException">The service rejected the request or refused to answer.</exception>
        Task<string> CompleteJsonAsync(string systemPrompt, string userMessage, IReadOnlyDictionary<string, JsonElement> schema,
            CancellationToken cancellationToken = default);
    }

    /// <summary>JSON schema helpers.</summary>
    internal static class Schemas
    {
        public static Dictionary<string, JsonElement> Object(object properties, params string[] required) => new()
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(properties),
            ["required"] = JsonSerializer.SerializeToElement(required),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };

        public static readonly Dictionary<string, JsonElement> Translations =
            Object(new { translations = new { type = "array", items = new { type = "string" } } }, "translations");
    }
}

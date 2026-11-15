using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AGSTools;

namespace AGSTranslate
{
    /// <summary>
    /// A Model Context Protocol server on stdin/stdout (JSON-RPC 2.0, one message per line), so that AI clients such as
    /// Claude Code or Claude Desktop can read AGS games and work on translations. The tools only read games; they
    /// write translation files (TRS/TRA) but never into a game folder, and there is no install tool.
    /// </summary>
    public sealed class McpServer
    {
        public const string ServerName = "agstranslate";
        private static readonly string[] SupportedVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };
        private const int DefaultPageSize = 100;
        private const int MaxPageSize = 500;

        private static readonly JsonSerializerOptions Compact = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private static readonly JsonSerializerOptions Indented = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private sealed record Tool(string Name, string Description, JsonObject Schema, Func<JsonObject, object> Run);

        private readonly Dictionary<string, Tool> _tools;

        public McpServer()
        {
            _tools = CreateTools().ToDictionary(t => t.Name);
        }

        /// <summary>Answers requests from <paramref name="input"/> until it ends.</summary>
        public void Run(TextReader input, TextWriter output)
        {
            string? line;
            while ((line = input.ReadLine()) != null)
            {
                if (line.Trim().Length == 0)
                    continue;
                string? answer = Handle(line);
                if (answer != null)
                {
                    output.Write(answer);
                    output.Write('\n');
                    output.Flush();
                }
            }
        }

        /// <summary>Handles one JSON-RPC message; returns the answer, or null for notifications.</summary>
        public string? Handle(string message)
        {
            JsonNode? id = null;
            try
            {
                if (JsonNode.Parse(message) is not JsonObject request)
                    return Error(null, -32600, "Invalid request");
                id = request["id"]?.DeepClone();
                string? method = request["method"]?.GetValue<string>();
                bool notification = !request.ContainsKey("id");
                if (method == null)
                    return notification ? null : Error(id, -32600, "Invalid request");
                JsonObject parameters = request["params"] as JsonObject ?? new JsonObject();

                JsonNode? result = method switch
                {
                    "initialize" => Initialize(parameters),
                    "ping" => new JsonObject(),
                    "tools/list" => ListTools(),
                    "tools/call" => CallTool(parameters),
                    _ => null,
                };
                if (notification)
                    return null;
                return result == null ? Error(id, -32601, $"Method not found: {method}") : Result(id, result);
            }
            catch (JsonException)
            {
                return Error(null, -32700, "Parse error");
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                return Error(id, -32602, $"Invalid params: {ex.Message}");
            }
        }

        private static JsonObject Initialize(JsonObject parameters)
        {
            string requested = parameters["protocolVersion"]?.GetValue<string>() ?? "";
            return new JsonObject
            {
                ["protocolVersion"] = SupportedVersions.Contains(requested) ? requested : SupportedVersions[0],
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = ServerName,
                    ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                },
                ["instructions"] = "Tools for translating Adventure Game Studio (AGS) games. Typical flow: game_info, extract_texts to a TRS file, "
                    + "read_translation page by page, write_translations with your translations, check_translation against the game, then compile_tra. "
                    + "In AGS texts \"[\" is a line break, \"&12 \" at the start is a voice prefix, and placeholders like %s or @SCORE@ must stay. "
                    + "Installing into the game is left to the user (AGSTranslate install or the editor).",
            };
        }

        private JsonObject ListTools()
        {
            var tools = new JsonArray();
            foreach (Tool tool in _tools.Values)
                tools.Add(new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["inputSchema"] = tool.Schema.DeepClone() });
            return new JsonObject { ["tools"] = tools };
        }

        private JsonObject CallTool(JsonObject parameters)
        {
            string name = parameters["name"]?.GetValue<string>() ?? throw new InvalidOperationException("missing tool name");
            if (!_tools.TryGetValue(name, out Tool? tool))
                return ToolResult($"Unknown tool \"{name}\".", isError: true);
            JsonObject arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
            try
            {
                object result = tool.Run(arguments);
                return ToolResult(result as string ?? JsonSerializer.Serialize(result, Indented), isError: false);
            }
            catch (Exception ex) when (ex is ToolException or IOException or UnauthorizedAccessException or InvalidDataException
                or InvalidOperationException or FormatException or ArgumentException)
            {
                return ToolResult($"Error: {ex.Message}", isError: true);
            }
        }

        private static JsonObject ToolResult(string text, bool isError) => new()
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = isError,
        };

        private static string Result(JsonNode? id, JsonNode result) =>
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString(Compact);

        private static string Error(JsonNode? id, int code, string message) =>
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString(Compact);

        private sealed class ToolException : Exception
        {
            public ToolException(string message) : base(message) { }
        }

        // ── Tools ──────────────────────────────────────────────────────────────

        private static JsonObject Schema(object properties, params string[] required) => new()
        {
            ["type"] = "object",
            ["properties"] = JsonSerializer.SerializeToNode(properties),
            ["required"] = JsonSerializer.SerializeToNode(required),
            ["additionalProperties"] = false,
        };

        private static readonly object GameProperty = new { type = "string", description = "Path of the game: its .exe, .ags/.dta data file or folder" };

        private IEnumerable<Tool> CreateTools()
        {
            yield return new Tool("game_info",
                "Title, unique ID, AGS version and text encoding of an AGS game.",
                Schema(new { game = GameProperty }, "game"),
                a =>
                {
                    GameInfo info = RequireGame(a);
                    return new
                    {
                        title = info.GameTitle,
                        uniqueId = info.UniqueId,
                        agsVersion = info.Version,
                        encoding = info.IsUnicode ? "UTF-8" : $"8-bit, codepage {(info.TextCodepage > 0 ? info.TextCodepage : AgsTextEncoding.DefaultAnsiCodepage)}",
                        dataFile = info.DataPath,
                    };
                });

            yield return new Tool("extract_texts",
                "Extracts all player-visible texts of a game into a new TRS translation file (with an empty translation for every text) "
                + "and a context file with the speaker of each line. Refuses to overwrite an existing file unless overwrite is true.",
                Schema(new
                {
                    game = GameProperty,
                    output = new { type = "string", description = "Path of the TRS file to write; not inside the game folder" },
                    overwrite = new { type = "boolean", description = "Replace an existing file (default false)" },
                }, "game", "output"),
                a =>
                {
                    string game = GameArg(a);
                    string output = OutputPath(a, "output", game);
                    ExtractionResult result = TextExtractor.Extract(game);
                    TranslationDocument doc = result.ToTranslationDocument();
                    var warnings = result.Warnings.Concat(TrsFile.Write(output, doc))
                        .Concat(EncodingAdvice.Check(doc, result.Game?.EngineVersion, result.Game?.IsUnicode ?? false)).ToList();
                    TextContext.Write(TextContext.PathFor(output), TextContext.FromExtraction(result));
                    return new
                    {
                        file = output,
                        texts = result.Texts.Count,
                        rooms = result.RoomCount,
                        bySource = result.Texts.GroupBy(t => t.Source.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                        warnings,
                    };
                });

            yield return new Tool("read_translation",
                "Reads entries of a TRS or TRA file, page by page and in game order: index, original, translation and (if a context file "
                + "exists next to the TRS) who says it. Filter by state or by text.",
                Schema(new
                {
                    file = new { type = "string", description = "Path of the .trs or .tra file" },
                    offset = new { type = "integer", description = "Index of the first entry among the filtered ones (default 0)" },
                    limit = new { type = "integer", description = $"Number of entries (default {DefaultPageSize}, at most {MaxPageSize})" },
                    filter = new { type = "string", @enum = new[] { "all", "empty", "translated" }, description = "Which entries (default all)" },
                    search = new { type = "string", description = "Only entries whose original or translation contains this text (case-insensitive)" },
                }, "file"),
                a =>
                {
                    string file = PathArg(a, "file");
                    TranslationDocument doc = ReadTranslation(file);
                    var context = Path.GetExtension(file).Equals(".trs", StringComparison.OrdinalIgnoreCase) ? TextContext.Read(TextContext.PathFor(file)) : null;
                    string filter = StringArg(a, "filter") ?? "all";
                    string? search = StringArg(a, "search");
                    int offset = Math.Max(0, IntArg(a, "offset") ?? 0);
                    int limit = Math.Clamp(IntArg(a, "limit") ?? DefaultPageSize, 1, MaxPageSize);

                    var matching = doc.Entries.Select((e, i) => (index: i, e.Key, e.Value))
                        .Where(e => filter switch
                        {
                            "empty" => string.IsNullOrEmpty(e.Value),
                            "translated" => !string.IsNullOrEmpty(e.Value),
                            "all" => true,
                            _ => throw new ToolException("filter must be all, empty or translated."),
                        })
                        .Where(e => search == null || e.Key.Contains(search, StringComparison.OrdinalIgnoreCase) || e.Value.Contains(search, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    return new
                    {
                        game = doc.Options.GameName,
                        totalEntries = doc.Entries.Count,
                        translated = doc.Entries.Count(e => !string.IsNullOrEmpty(e.Value)),
                        matching = matching.Count,
                        offset,
                        entries = matching.Skip(offset).Take(limit).Select(e => new
                        {
                            index = e.index,
                            original = e.Key,
                            translation = e.Value,
                            speaker = context != null && context.TryGetValue(e.Key, out TextOrigin? origin) ? origin.Speaker : null,
                            location = context != null && context.TryGetValue(e.Key, out TextOrigin? where) ? where.Location : null,
                        }),
                        next = offset + limit < matching.Count ? offset + limit : (int?)null,
                    };
                });

            yield return new Tool("write_translations",
                "Sets translations in a TRS file. Each item names an existing entry by its exact original text (or its index from "
                + "read_translation). Unknown originals are reported and skipped; other entries stay as they are. The file is written "
                + "atomically and a .bak copy of the first version is kept.",
                Schema(new
                {
                    file = new { type = "string", description = "Path of the .trs file" },
                    translations = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                original = new { type = "string", description = "The original text exactly as read" },
                                index = new { type = "integer", description = "Alternative to original: the entry index" },
                                translation = new { type = "string" },
                            },
                            required = new[] { "translation" },
                        },
                    },
                }, "file", "translations"),
                a =>
                {
                    string file = PathArg(a, "file");
                    if (!Path.GetExtension(file).Equals(".trs", StringComparison.OrdinalIgnoreCase))
                        throw new ToolException("Only TRS files can be written; use compile_tra to make a TRA from it.");
                    TranslationDocument doc = ReadTranslation(file);
                    List<string> keys = doc.Entries.Keys.ToList();
                    var unknown = new List<string>();
                    int changed = 0;
                    foreach (JsonNode? node in a["translations"] as JsonArray ?? throw new ToolException("translations must be a list."))
                    {
                        if (node is not JsonObject item || item["translation"]?.GetValue<string>() is not { } translation)
                            throw new ToolException("Every item needs a translation.");
                        string? key = item["original"]?.GetValue<string>();
                        if (key == null && item["index"]?.GetValue<int>() is int index)
                            key = index >= 0 && index < keys.Count ? keys[index] : null;
                        if (key == null || !doc.Entries.TryGetValue(key, out string? old))
                        {
                            unknown.Add(item["original"]?.GetValue<string>() ?? $"#{item["index"]}");
                            continue;
                        }
                        if (old != translation)
                        {
                            doc.Entries[key] = translation;
                            changed++;
                        }
                    }
                    var warnings = new List<string>();
                    if (changed > 0)
                    {
                        string backup = file + ".bak";
                        if (!File.Exists(backup))
                            File.Copy(file, backup);
                        warnings.AddRange(TrsFile.Write(file, doc));
                    }
                    return new
                    {
                        file,
                        changed,
                        unknown,
                        translated = doc.Entries.Count(e => !string.IsNullOrEmpty(e.Value)),
                        totalEntries = doc.Entries.Count,
                        warnings,
                    };
                });

            yield return new Tool("check_translation",
                "Checks a translation against its game: broken placeholders, voice prefixes and line breaks, inconsistent translations, "
                + "glossary terms, names, and texts too wide or too high for their GUI buttons and labels (measured with the game fonts).",
                Schema(new
                {
                    game = GameProperty,
                    file = new { type = "string", description = "Path of the .trs or .tra file" },
                    glossary = new { type = "string", description = "Optional glossary file (lines \"Original = Translation\")" },
                    limit = new { type = "integer", description = "Maximum number of findings to list (default 200)" },
                }, "game", "file"),
                a =>
                {
                    string game = GameArg(a);
                    TranslationDocument doc = ReadTranslation(PathArg(a, "file"));
                    Glossary? glossary = StringArg(a, "glossary") is { } g ? Glossary.Read(Path.GetFullPath(g)) : null;
                    List<CheckIssue> issues = TranslationCheck.Run(doc, TextExtractor.Extract(game), GameFonts.Load(game), glossary);
                    int limit = Math.Max(1, IntArg(a, "limit") ?? 200);
                    return new
                    {
                        errors = issues.Count(i => i.Severity == CheckSeverity.Error),
                        warnings = issues.Count(i => i.Severity == CheckSeverity.Warning),
                        notes = issues.Count(i => i.Severity == CheckSeverity.Info),
                        byCategory = issues.GroupBy(i => i.Category).ToDictionary(g => g.Key, g => g.Count()),
                        findings = issues.OrderBy(i => i.Severity).Take(limit)
                            .Select(i => new { severity = i.Severity.ToString(), category = i.Category, original = i.Key, message = i.Message }),
                    };
                });

            yield return new Tool("compile_tra",
                "Compiles a TRS file into the TRA file the game loads, with the game's ID and encoding. Not into the game folder: "
                + "installing is left to the user.",
                Schema(new
                {
                    game = GameProperty,
                    file = new { type = "string", description = "Path of the .trs file" },
                    output = new { type = "string", description = "Path of the .tra file to write" },
                }, "game", "file", "output"),
                a =>
                {
                    string game = GameArg(a);
                    GameInfo info = RequireGame(a);
                    string output = OutputPath(a, "output", game, allowOverwrite: true);
                    int codepage = info.TextCodepage > 0 && !info.IsUnicode ? info.TextCodepage : AgsTextEncoding.DefaultAnsiCodepage;
                    TranslationDocument doc = TrsFile.Read(PathArg(a, "file"), codepage);
                    doc.Options.GameUid = info.UniqueId;
                    doc.Options.GameName = info.GameTitle;
                    doc.Options.AnsiCodepage = codepage;
                    if (info.IsUnicode)
                        doc.Options.Encoding = "UTF-8";
                    var warnings = doc.ReadWarnings.Concat(TraFile.Write(output, doc))
                        .Concat(EncodingAdvice.Check(doc, info.Version, info.IsUnicode)).Concat(FontCoverage.CheckGame(doc, game)).ToList();
                    return new
                    {
                        file = output,
                        entries = doc.Entries.Count,
                        translated = doc.Entries.Count(e => !string.IsNullOrWhiteSpace(e.Value)),
                        encoding = doc.Options.IsUtf8 ? "UTF-8" : "8-bit",
                        warnings,
                    };
                });

            yield return new Tool("list_fonts",
                "The fonts of a game: number, file, kind (WFN/TTF), size, outline and the number of characters, with problems found.",
                Schema(new { game = GameProperty }, "game"),
                a => GameFonts.Load(GameArg(a)).Select(f => new
                {
                    number = f.Number,
                    file = f.FileName,
                    kind = f.Kind.ToString().ToUpperInvariant(),
                    origin = f.Origin == FontOrigin.Package ? "package" : "folder",
                    size = f.Info?.Size,
                    outline = f.Info?.Outline switch
                    {
                        null => null,
                        FontInfo.NoOutline => "none",
                        FontInfo.AutoOutline => "auto",
                        int n => $"font {n}",
                    },
                    characters = f.Wfn?.Glyphs.Count(g => g is { IsEmpty: false }) ?? f.Ttf?.CodePoints.Count ?? 0,
                    name = f.Ttf?.Name,
                    error = f.Error,
                    problems = f.Wfn?.Problems.Take(5),
                }).ToList());

            yield return new Tool("measure_text",
                "Width in pixels of a text in one of the game's fonts, per line (\"[\" breaks lines), to check whether a translation fits.",
                Schema(new
                {
                    game = GameProperty,
                    font = new { type = "integer", description = "Font number (see list_fonts)" },
                    text = new { type = "string" },
                }, "game", "font", "text"),
                a =>
                {
                    int number = IntArg(a, "font") ?? throw new ToolException("font is missing.");
                    GameFont font = GameFonts.Load(GameArg(a)).FirstOrDefault(f => f.Number == number)
                        ?? throw new ToolException($"The game has no font {number}.");
                    FontMetrics metrics = FontMetrics.For(font) ?? throw new ToolException($"Font {number} cannot be measured ({font.Error ?? "unknown size"}).");
                    string text = StringArg(a, "text") ?? "";
                    var options = new TranslationOptions();
                    var lines = TranslationApi.AgsMarkup.SplitLines(TranslationApi.AgsMarkup.SplitVoicePrefix(text).Body);
                    return new
                    {
                        font = number,
                        lineHeight = metrics.LineHeight,
                        approximate = metrics.IsApproximate,
                        lines = lines.Select(l => new { text = l, width = metrics.MeasureWidth(l, options) }),
                    };
                });
        }

        // ── Arguments ──────────────────────────────────────────────────────────

        private static string? StringArg(JsonObject a, string name) => a[name]?.GetValue<string>() is { Length: > 0 } s ? s : null;

        private static int? IntArg(JsonObject a, string name) => a[name] is JsonValue v && v.TryGetValue(out int i) ? i : null;

        private static string PathArg(JsonObject a, string name) =>
            Path.GetFullPath(StringArg(a, name) ?? throw new ToolException($"{name} is missing."));

        /// <summary>The game data file; for a folder its .ags, game28.dta, ac2game.dta or only .exe.</summary>
        private static string GameArg(JsonObject a)
        {
            string game = PathArg(a, "game");
            if (!Directory.Exists(game))
                return File.Exists(game) ? game : throw new ToolException($"'{game}' does not exist.");
            string? found = Directory.GetFiles(game, "*.ags").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault()
                ?? new[] { "game28.dta", "ac2game.dta" }.Select(n => Path.Combine(game, n)).FirstOrDefault(File.Exists);
            if (found != null)
                return found;
            string[] exes = Directory.GetFiles(game, "*.exe").Where(f => !Path.GetFileName(f).StartsWith("winsetup", StringComparison.OrdinalIgnoreCase)).ToArray();
            return exes.Length == 1 ? exes[0] : throw new ToolException($"No AGS game file found in '{game}'; give the path of the .exe or .ags file.");
        }

        private static GameInfo RequireGame(JsonObject a)
        {
            string game = GameArg(a);
            return GameInfo.GetGameInfo(game) ?? throw new ToolException($"No AGS game data found in '{game}'.");
        }

        private static TranslationDocument ReadTranslation(string file)
        {
            if (!File.Exists(file))
                throw new ToolException($"'{file}' does not exist.");
            return Path.GetExtension(file).Equals(".tra", StringComparison.OrdinalIgnoreCase)
                ? TraFile.Read(file, AgsTextEncoding.DefaultAnsiCodepage)
                : TrsFile.Read(file, AgsTextEncoding.DefaultAnsiCodepage);
        }

        /// <summary>An output path outside the game folder; an existing file only with "overwrite".</summary>
        private static string OutputPath(JsonObject a, string name, string game, bool allowOverwrite = false)
        {
            string output = PathArg(a, name);
            string gameDir = Path.GetDirectoryName(game) ?? "";
            string outputDir = Path.GetDirectoryName(output) ?? "";
            if (string.Equals(Path.TrimEndingDirectorySeparator(outputDir), Path.TrimEndingDirectorySeparator(gameDir), StringComparison.OrdinalIgnoreCase))
                throw new ToolException("Choose a folder other than the game folder; installing into the game is left to the user.");
            if (!Directory.Exists(outputDir))
                throw new ToolException($"The folder '{outputDir}' does not exist.");
            bool overwrite = a["overwrite"] is JsonValue v && v.TryGetValue(out bool b) && b;
            if (File.Exists(output) && !allowOverwrite && !overwrite)
                throw new ToolException($"'{output}' exists already; pass overwrite: true to replace it.");
            return output;
        }
    }
}

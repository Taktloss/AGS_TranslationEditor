# AGSTranslate as an MCP server

`AGSTranslate mcp` starts a [Model Context Protocol](https://modelcontextprotocol.io) server. AI
programs such as **Claude Code**, **Claude Desktop** or other MCP clients can then work with an AGS
game themselves: extract texts, read the TRS page by page, fill in translations, check them against the
game and build the TRA. You can then tell the program, for example: *"Translate the empty entries in
German.trs, then check against the game and fix texts that are too long."*

Deutsche Fassung: [../MCP.md](../MCP.md)

## Setup

First build AGSTranslate or publish it as a single file ([CLI.md](CLI.md#1-installation-and-usage)).
Below, `/path/to/AGSTranslate` stands for the program file; from source,
`dotnet /path/to/AGSTranslate/bin/Release/net9.0/AGSTranslate.dll` works too.

**Claude Code:**

```bash
claude mcp add agstranslate -- /path/to/AGSTranslate mcp
```

**Claude Desktop** (`claude_desktop_config.json`, via *Settings → Developer → Edit Config*):

```json
{
  "mcpServers": {
    "agstranslate": {
      "command": "/path/to/AGSTranslate",
      "args": ["mcp"]
    }
  }
}
```

Other clients start the program the same way: command `AGSTranslate`, argument `mcp`, transport *stdio*.

## Tools

| Tool | What it does |
|---|---|
| `game_info` | title, game ID, AGS version and encoding |
| `extract_texts` | all texts of the game into a new TRS, plus the context file (who says what) |
| `read_translation` | read entries of a TRS/TRA: page by page (`offset`, `limit`), filtered (`empty`, `translated`, search text), with speaker and location |
| `write_translations` | fill translations into a TRS, entries named by original text or index |
| `check_translation` | the editor's check against the game: placeholders, voice prefixes, line breaks, consistency, glossary, names, text width in GUI controls |
| `compile_tra` | build the TRA for the game (with game ID and encoding) |
| `list_fonts` | the game's fonts with size, outline and number of characters |
| `measure_text` | width of a text in pixels in a game font, per line |

As on the command line, the game can be the exe, the `.ags` or `game28.dta`, and here also the game
folder.

## Safety

- Game files are **only read**. No tool writes into the game folder: `extract_texts` and `compile_tra`
  refuse targets there, and there is deliberately no install tool – that stays with
  `AGSTranslate install` or the editor, which make backups first.
- `extract_texts` doesn't overwrite an existing file unless `overwrite: true` is given.
- `write_translations` only changes existing entries, writes the file atomically and keeps a copy
  `<file>.trs.bak` the first time.
- The server only talks over stdin/stdout to the client that started it; it opens no port and sends
  nothing to the internet. What the client (the AI program) does with the texts depends on it – with
  cloud models the texts it reads go to that provider.

## Notes for the model

On start the client gets short instructions: `[` is a line break, `&12 ` at the start a voice prefix,
placeholders like `%s` or `@SCORE@` must stay. Running `check_translation` after translating is still
worth it.

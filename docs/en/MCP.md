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
| `open_game` | read a game once and keep it in memory, with an overview (texts by source, rooms, GUI texts, fonts, sprites) |
| `game_info` | title, game ID, AGS version and encoding |
| `extract_texts` | all texts of the game into a new TRS, plus the context file (who says what) |
| `read_translation` | read entries of a TRS/TRA: page by page (`offset`, `limit`), filtered (`empty`, `translated`, search text), with speaker and location |
| `write_translations` | fill translations into a TRS, entries named by original text or index |
| `check_translation` | the editor's check against the game: placeholders, voice prefixes, line breaks, consistency, glossary, names, text width in GUI controls; with `spell: "de"` also spelling |
| `spellcheck` | spell check texts offline with a Hunspell dictionary: unknown words with suggestions, skipping placeholders and names from the original |
| `compile_tra` | build the TRA for the game (with game ID and encoding) |
| `list_fonts` | the game's fonts with size, outline and number of characters |
| `measure_text` | width of a text in pixels in a game font, per line |
| `check_fit` | whether a wording fits every button or label that shows the entry, and whether the placeholders are kept – for shortening |
| `list_sprites` | the pictures (sprites) of an AGS 3.6 game with size and color depth, filtered by minimum size |
| `view_sprite` | show one sprite as a picture, e.g. to read text painted into it |
| `export_sprites` | save sprites as `spriteNNNNN.png` into a folder |
| `list_rooms` | the room backgrounds with room, background number and size |
| `view_room` | show a room background as a picture, e.g. to read signs |
| `export_rooms` | save room backgrounds as `roomNNN-bgN.png` into a folder |
| `build_sprite_patch` | build the patch from edited PNGs: `acsprset.spr` + `sprindex.dat` for sprites, `roomN.crm` for rooms (as in the [image editor](Image-Editor.md)) |

The game is read by the first tool that needs it and stays in memory while the server runs, so further
calls (e.g. `check_translation` after every change) don't wait. If files in the game folder change, the
server reads it again. `open_game` loads it in advance and gives an overview.

As on the command line, the game can be the exe, the `.ags` or `game28.dta`, and here also the game
folder.

## Safety

- Game files are **only read**. No tool writes into the game folder: `extract_texts` and `compile_tra`
  refuse targets there, and there is deliberately no install tool – that stays with
  `AGSTranslate install` or the editor, which make backups first.
- `extract_texts` doesn't overwrite an existing file unless `overwrite: true` is given; `export_sprites`,
  `export_rooms` and `build_sprite_patch` never write into the game folder either.
- `write_translations` only changes existing entries, writes the file atomically and keeps a copy
  `<file>.trs.bak` the first time.
- The server only talks over stdin/stdout to the client that started it; it opens no port and sends
  nothing to the internet. What the client (the AI program) does with the texts depends on it – with
  cloud models the texts it reads go to that provider.

## Notes for the model

On start the client gets short instructions: `[` is a line break, `&12 ` at the start a voice prefix,
placeholders like `%s` or `@SCORE@` must stay. Running `check_translation` after translating is still
worth it.

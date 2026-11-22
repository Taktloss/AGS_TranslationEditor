[![Build and Release](https://github.com/Taktloss/AGS_TranslationEditor/actions/workflows/build.yml/badge.svg)](https://github.com/Taktloss/AGS_TranslationEditor/actions/workflows/build.yml)

# AGS Translation Editor #

### What can you do with AGS Translation Editor ? ###
* Read, edit, convert and create TRS and TRA Translation Files for AGS Games.
* Check a translation against the game: placeholders, voice prefixes, inconsistent translations, a glossary, and texts too wide for their GUI buttons (Tools > Check Translation, chapter 14 of the user guide).
* Machine translation suggestions with DeepL, Claude, Google or any OpenAI-compatible service (OpenAI, OpenRouter, Mistral, local models with Ollama or LM Studio) that keep voice prefixes, line breaks and placeholders; the AI services also get the game title, the form of address and the glossary (Translate button, chapter 16).
* AI menu: let Claude or an OpenAI-compatible model review the translation or shorten texts that don't fit into their buttons (every shorter text is measured with the game fonts before it can be applied); suggestions are applied one by one, running AI work can be cancelled.
* Spell checking with Hunspell dictionaries (the ones of LibreOffice/Firefox, offline): unknown words are listed below the translation box with suggestions and reported by the check; optional grammar check with a public or local LanguageTool server (Tools > Check Grammar with LanguageTool).
* Themes: dark, light and Catppuccin (Mocha, Macchiato, Frappé, Latte).
* MCP server (`AGSTranslate mcp`): AI programs such as Claude Code or Claude Desktop can read games, extract texts, read and write translations, check them, measure shorter wordings, spell check, view and patch sprites and room backgrounds and build the TRA ([English](docs/en/MCP.md) · [Deutsch](docs/MCP.md)).
* Optional context: who says each text and where it comes from (room, dialog, GUI), read from the compiled scripts; shown as a column with a character/room filter (Settings > Context, chapter 17).
* Install a translation into a game with backup and undo, or create a zip with install scripts for players (Tools > Install into Game / Create Player Package, chapter 15).

### Where do i get the script of a game ? ###
* Since release version 1.1 its possible to use a Script extractor that comes with the Editor (Tools > Extract Script) ;).

### TRS file format ###
A TRS file should look like this example

```
&1 Original Text1

&2 Original Text2
&2 Test translation
&3 Original Text3


```

### Download ###
* Ready-to-run builds for Windows, Linux and macOS (x64 and ARM64) are on the [Releases](https://github.com/Taktloss/AGS_TranslationEditor/releases) page. Each archive contains the translator, the font editor, the command line tool AGSTranslate and the user guide; no .NET installation is needed.
* Check real games: `scripts/check-games.sh <AGSTranslate> <outdir> <game>...` runs info, extract, a TRA round trip, the check, fonts, sprites and rooms over each game (only reading it) and writes a table to `<outdir>/summary.md`.
* New release, built locally for all platforms: tag the commit, then run the release script, e.g. `git tag -a v2.0.0 -m v2.0.0 && git push origin v2.0.0 && scripts/release.sh 2.0.0 --upload`. Without `--upload` the archives are only written to `publish/release-<version>/`. Versions with a suffix such as `2.0.0-beta1` become pre-releases. The osx-arm64 archive needs [rcodesign](https://github.com/indygreg/apple-platform-rs) for the ad-hoc signature and is skipped without it.
* The GitHub workflow (*Actions → Build and Release*) only runs when started by hand; started on a tag (`gh workflow run build.yml --ref v2.0.0`) it builds the same release there.

### Documentation ###
* User guide: [English](docs/en/UserGuide.md) · [Deutsch](docs/Anleitung.md) (translation workflow, special characters, font editor, checklist)
* Font editor step by step with pictures (umlauts, accents, quotation marks): [English](docs/en/Font-Editor.md) · [Deutsch](docs/Font-Editor.md)
* Image editor for text painted into pictures (letters, maps, menus) and room backgrounds (signs, posters), installed as a patch next to the game: [English](docs/en/Image-Editor.md) · [Deutsch](docs/Grafik-Editor.md)
* Command line tool AGSTranslate: [English](docs/en/CLI.md) · [Deutsch](docs/CLI.md)
* Test a translation in the game without touching the game folder (Linux): `tools/ags-test.sh --help`, described in chapter 12 of the user guide
* Live mode (Linux and Windows): see every text of the running game in the translator and correct it while playing (start it with `ags-test.sh --live`, or attach to a game that is already running with the *Attach* button in the live panel; usage in chapter 13 of the user guide, how the hook works: [English](docs/en/Live-Hook.md) · [Deutsch](docs/Live-Hook.md))

### Special characters ###
* Use an 8-bit translation if the game stores its texts as 8-bit and all characters fit its codepage (works on every OS); a UTF-8 translation of such a game only works with the Windows engine. Make sure the game fonts contain the characters: the TRA export warns about missing glyphs, and the AGS Font Editor (Tools > Font Editor) can add them.

### Third-party components ###
* Spell checking uses [WeCantSpell.Hunspell](https://github.com/aarondandy/WeCantSpell.Hunspell), a .NET port of Hunspell, licensed under MPL 1.1 / GPL 2.0 / LGPL 2.1; it is included unchanged (bundled into the program files), its source is at the link. Dictionaries are not shipped; the program uses those installed on the computer.

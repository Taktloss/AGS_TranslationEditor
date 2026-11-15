[![Build and Release](https://github.com/Taktloss/AGS_TranslationEditor/actions/workflows/build.yml/badge.svg)](https://github.com/Taktloss/AGS_TranslationEditor/actions/workflows/build.yml)

# AGS Translation Editor #

### What can you do with AGS Translation Editor ? ###
* Read, edit, convert and create TRS and TRA Translation Files for AGS Games.
* Check a translation against the game: placeholders, voice prefixes, inconsistent translations, a glossary, and texts too wide for their GUI buttons (Tools > Check Translation, chapter 14 of the user guide).
* Machine translation suggestions with DeepL, Claude, Google or any OpenAI-compatible service (OpenAI, OpenRouter, Mistral, local models with Ollama or LM Studio) that keep voice prefixes, line breaks and placeholders; the AI services also get the game title, the form of address and the glossary (Translate button, chapter 16).
* AI menu: let Claude or an OpenAI-compatible model review the translation or shorten texts that don't fit into their buttons; every suggestion can be applied one by one.
* MCP server (`AGSTranslate mcp`): AI programs such as Claude Code or Claude Desktop can read games, extract texts, read and write translations, check them and build the TRA ([English](docs/en/MCP.md) · [Deutsch](docs/MCP.md)).
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
* Every push also builds all platforms; the archives are attached to the workflow run under *Actions*.
* New release: push a version tag, e.g. `git tag v2.0.0 && git push origin v2.0.0`. Tags with a suffix such as `v2.0.0-beta1` become pre-releases.

### Documentation ###
* User guide: [English](docs/en/UserGuide.md) · [Deutsch](docs/Anleitung.md) (translation workflow, special characters, font editor, checklist)
* Font editor step by step with pictures (umlauts, accents, quotation marks): [English](docs/en/Font-Editor.md) · [Deutsch](docs/Font-Editor.md)
* Image editor for text painted into pictures (letters, maps, menus), installed as a sprite patch: [English](docs/en/Image-Editor.md) · [Deutsch](docs/Grafik-Editor.md)
* Command line tool AGSTranslate: [English](docs/en/CLI.md) · [Deutsch](docs/CLI.md)
* Test a translation in the game without touching the game folder (Linux): `tools/ags-test.sh --help`, described in chapter 12 of the user guide
* Live mode (Linux): see every text of the running game in the translator and correct it while playing (`ags-test.sh --live`, the *Live* button; usage in chapter 13 of the user guide, how the hook works: [English](docs/en/Live-Hook.md) · [Deutsch](docs/Live-Hook.md))

### Special characters ###
* Use an 8-bit translation if the game stores its texts as 8-bit and all characters fit its codepage (works on every OS); a UTF-8 translation of such a game only works with the Windows engine. Make sure the game fonts contain the characters: the TRA export warns about missing glyphs, and the AGS Font Editor (Tools > Font Editor) can add them.

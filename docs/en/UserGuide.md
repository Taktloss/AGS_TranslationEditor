# AGS Translation Editor – User Guide

*Deutsche Fassung: [Anleitung.md](../Anleitung.md)*

This guide describes how to create a translation for an Adventure Game Studio game with the
AGS Translation Editor, how to update it and get it into the game, how to check and edit the game's
fonts, and what to watch out for along the way.

The examples translate into German, but everything works the same for other languages.

## Contents

1. [The programs](#1-the-programs)
2. [Basic terms](#2-basic-terms)
3. [Creating a new translation](#3-creating-a-new-translation)
4. [Getting the translation into the game](#4-getting-the-translation-into-the-game)
5. [Updating a translation after a game update](#5-updating-a-translation-after-a-game-update)
6. [Umlauts, accents and quotation marks](#6-umlauts-accents-and-quotation-marks)
7. [Font Editor](#7-font-editor)
8. [Other games](#8-other-games)
9. [What to watch out for (checklist)](#9-what-to-watch-out-for-checklist)
10. [Troubleshooting](#10-troubleshooting)
11. [Command line (AGSTranslate)](#11-command-line-agstranslate)
12. [Testing games and finding problems](#12-testing-games-and-finding-problems)
13. [Live mode: correcting the translation while playing (Linux)](#13-live-mode-correcting-the-translation-while-playing-linux)
14. [Checking a translation](#14-checking-a-translation)
15. [Installing and sharing with players](#15-installing-and-sharing-with-players)
16. [Machine translation (DeepL, Claude, Google)](#16-machine-translation-deepl-claude-google)
17. [Context: who says what?](#17-context-who-says-what)

---

## 1. The programs

| Program | Project | Purpose |
|---|---|---|
| **AGS Translation Editor** | `AGS_TranslationEditor.Avalonia` | Translating (editing TRS files), extracting texts from the game, creating TRA files, updating translations |
| **AGS Font Editor** | `AGSFontEditor.App` (also in the translator under *Tools → Font Editor...*) | Viewing, extracting, checking and editing a game's fonts |
| **AGSTranslate** | `AGSTranslate` | The same on the command line, e.g. for scripts |

Running from the repository (needs the .NET SDK):

```bash
dotnet run --project AGS_TranslationEditor.Avalonia      # translator
dotnet run --project AGSFontEditor.App -- [game or font file]
dotnet run --project AGSTranslate -- <command> ...
```

**User interface language:** The translator and the font editor are available in English and German.
Under *Tools → Settings... → Language* you choose *System language*, *English* or *Deutsch*; the
choice takes effect after a restart and also applies to the standalone font editor. This guide uses
the English menu names. Messages of the check and of the extraction itself are always in English.

Wherever a "game file" is asked for, you can choose the **game EXE**, the **`.ags` file** or a loose
**`game28.dta`**. Steam versions often keep the game in an `.ags` file next to a small EXE; the tool
finds the data in both cases.

## 2. Basic terms

- **TRS** – the translation as a text file. Always one line of original text, then one line of
  translation. This is what you work on.
- **TRA** – the compiled translation the game loads. It is created from the TRS and must **match the
  game** (the game ID and title are written into it).
- **Source text/key** – the original line. The game looks up the translation by the *exact* text,
  including spaces and punctuation. So never change the original lines.
- **Voice prefix `&N `** – lines with voice acting start with `&123 `. The translation must have the
  same prefix (`&123 Hallo.`), otherwise the voice line does not match.
- **Encoding** – UTF-8 or 8-bit (a codepage, e.g. 1252). See [chapter 6](#which-encoding).

## 3. Creating a new translation

1. **Start the translator → *Tools → Extract Strings...*** and choose the game file.
   The tool reads all translatable texts from the game in a structured way: dialogs, dialog options,
   script texts, GUI texts, inventory, hotspots and objects of all rooms, character names.
2. The texts appear in the table. Write the **translation** into the right column.
   - *Untranslated only* shows only untranslated lines, *Next untranslated* jumps to the next one.
   - Empty translations are allowed: the game then shows the original text.
   - The *Translate* button suggests machine translations (DeepL, Claude or Google), see
     [chapter 16](#16-machine-translation-deepl-claude-google).
3. **Save** regularly (*File → Save*); this produces the TRS file.
4. Choose the encoding (*Tools → Settings... → Save encoding*), see
   [chapter 6](#which-encoding): for games with 8-bit texts and languages like German, French or
   Spanish choose **8-bit (Latin-1)**, for games with UTF-8 texts **UTF-8**.

## 4. Getting the translation into the game

> Faster: ***Tools → Install into Game...*** does steps 1–4 with a backup and an undo function
> ([chapter 15](#15-installing-and-sharing-with-players)). Here is the manual way:

1. ***File → Export → Export as TRA...*** (the currently open translation) or
   ***Tools → Create TRA File...*** (a TRS file from disk); in both cases choose the game file and
   the target file. The file name is the name of the language in the game, e.g. `German.tra`.
2. Pay attention to the **warnings** afterwards (see below). The font warnings matter most: they
   tell you which characters the game *cannot* show.
3. Copy the `.tra` into the **game folder** (next to the EXE or `.ags`).
4. Select the language:
   - in the game's setup program (`winsetup.exe` or the game settings), or
   - in `acsetup.cfg` in the game folder:
     ```ini
     [language]
     translation=German
     ```
   Some games have their own language selection in the options menu.
5. Start the game and check. Unknown lines appear in the original language – that usually means
   they are not translated yet or the original text has changed
   ([chapter 5](#5-updating-a-translation-after-a-game-update)).

## 5. Updating a translation after a game update

When the game was updated or an old TRS/TRA no longer fits completely:

1. Open the old translation in the translator.
2. ***Tools → Update from Game...*** and choose the (new) game file.
3. Result: the list now contains exactly the texts of the current game, in game order.
   - **unchanged** – translation kept,
   - **similar** – only spaces at the edges or the `&N` prefix changed; translation kept and the
     prefix adjusted automatically,
   - **untranslated** – new texts that still need translating,
   - **obsolete** – translations whose original text no longer exists. They stay at the end of the
     file (the game ignores them) so no work is lost.
4. Save and create a new TRA.

This is also the way to complete a translation that was made with an older tool: older extractors
missed e.g. GUI texts and some dialog options.

## 6. Umlauts, accents and quotation marks

Whether a character appears in the game depends on **two things**: the encoding of the TRA and the
font.

### Which encoding?

First check how the game itself stores its texts: *Tools → Game Info...* or `AGSTranslate info`
shows `Text encoding: UTF-8` or `8-bit, codepage 1252`.

| Game stores texts as | Translation contains | TRA encoding |
|---|---|---|
| UTF-8 | any characters | **UTF-8** |
| 8-bit (e.g. codepage 1252) | only characters of that codepage (German, French, Spanish, Italian …) | **8-bit** – works with every engine on Windows, Linux and macOS |
| 8-bit | characters outside the codepage (e.g. Polish, Russian in a 1252 game) | **UTF-8**, but only AGS 3.6+ and **only on Windows** |

Background:

- With an **8-bit TRA** the engine looks up each character in the font by its byte in the codepage
  (ä = 0xE4, ß = 0xDF). For umlauts and ß this is the same code as in Unicode.
- With a **UTF-8 TRA** the engine (from AGS 3.6) switches to UTF-8 and looks up the Unicode code. If
  the game stores its texts as 8-bit, the engine has to convert the original texts for this – with a
  Windows function (locale `.1252`). **On Linux and macOS this fails, and the whole game stays in the
  original language.** The translator and `AGSTranslate` warn about it. (Found with Blackwell Legacy
  on Linux; see `Engine/ac/translation.cpp`.)
- **„ “ – …** sit on bytes 0x80–0x9F in codepage 1252. In an 8-bit TRA they only appear if the font
  has a character at these codes – TrueType fonts practically never do. Either use `"` and `-`, or
  add the characters in the font editor at code `0x84` („), `0x93` (“), `0x96` (–), `0x85` (…)
  (*Go to* `0x84` → *Create Glyph* or *Copy from*).
- Engines before 3.6 do not know UTF-8; always use 8-bit there.

### Font: the character must be in it

Every text in the game is drawn with a particular font (speech, dialog options, GUI, ...). If a
character is missing there, the spot stays empty. Missing umlauts are therefore almost always a
**font problem**, not a problem of the translation.

When creating the TRA, the tool checks all special characters of the translation against all fonts of
the game and warns, for example:

```
Font 8 (agsfnt8.ttf, IsWasted): no glyph for ß (179×), „ (30×)
Font 5 (agsfnt5.ttf, BBSesque): ä ö ü look identical to a o u (accents or umlaut dots missing in the font)
```

- *no glyph for* – the character is missing from the font.
- *look identical to* – the character exists but looks like the base letter (the font has
  "umlauts without dots", like Unavowed's original speech font).
- Not every font is used for translated text (symbol or title fonts). Fonts that also lack the base
  letter (e.g. capitals-only fonts) are not reported at all.

You fix this with the [Font Editor](#7-font-editor).

## 7. Font Editor

**Step by step with pictures:** [adding umlauts and special characters](Font-Editor.md) (examples `ä`, `ß`,
`é` and `„` for 8-bit translations).

Start: standalone (`AGSFontEditor.App`) or in the translator via *Tools → Font Editor...*
(automatically opens the game chosen last).

### The window

- **Open Game...** – loads all fonts of a game exactly the way the engine loads them
  (see [How AGS loads fonts](#how-ags-loads-fonts)).
- **Open Font Files...** – opens single `.ttf`/`.wfn` files.
- **FONTS** (left) – all fonts with number, file, origin (*loose file* = file in the game folder,
  *in package* = inside the `.ags`/EXE), size in the game and outline.
- **GLYPHS** (middle) – all characters of the font. Missing characters are marked red with ×. Filters
  *All codes / With glyph / Missing*; **Go to** jumps to a character (`ß`, `U+201E`, `0x84` or a
  decimal number).
- **PREVIEW (GAME SIZE)** – sample text at game size without anti-aliasing, i.e. the way it looks in
  the game. Below it you see which characters are missing. To its right you choose whether the preview
  looks characters up like a **UTF-8** or an **8-bit** translation.
- **GLYPH EDITOR** (right) – pixel editor for the selected character.

### Editing characters

1. Choose a font on the left, choose a character in the grid (or via *Go to*).
2. If the character is missing: **Create Glyph** – or better **Copy from** a similar character
   (e.g. copy `a` for `ä`, `B` for `ß`) and then adjust it.
3. Drawing: **left click** toggles a pixel, dragging paints; **right click**/dragging erases.
   The blue line is the baseline. **Width**/**Height** change the size, **Undo**/**Redo** with
   Ctrl+Z / Ctrl+Y.
4. Check the result in the preview.
5. **Save Font As...** – save under the **same file name** (`agsfntN.ttf` or `.wfn`).
   The editor never overwrites a file on its own; you always choose the target in the dialog.

Which fonts can be edited:

- **WFN** (bitmap fonts of old AGS versions): always.
- **TTF pixel fonts** (most fonts in retro adventures): yes. The editor detects the font's pixel grid
  and edits the characters as pixels. If the grid is not detected, you can enter a grid size under
  *Grid (units/pixel)*.
- **Regular vector fonts** (Arial, Times, script fonts): view only. Such fonts usually have the
  common special characters anyway.

### Extracting fonts

**Export Selected...** / **Export All...** copy the font files **unchanged** into a folder, e.g. as
a backup or to edit them in another program. The game folder itself is blocked as a target, because
files there would replace the game's fonts.

### Getting changed fonts into the game

1. Make a **backup** of the font files in the game folder (if there are any).
2. Put the saved `agsfntN.ttf`/`.wfn` into the **game folder**. A loose file in the game folder takes
   priority over the font in the `.ags` – the game package itself is not changed.
3. Create a new TRA (the font warnings should be gone now) and check in the game.

Back to the original: delete the loose file or copy the backup back.

### How AGS loads fonts

- Font number *N* is called `agsfntN.ttf` (TrueType) or `agsfntN.wfn` (bitmap).
- If both exist, the **`.ttf`** is used.
- A file in the **game folder** takes priority over the one in the game package.
- The size at which a TTF font is drawn is stored in the game data (column *size*).
- *Outline: font 9* means: the text is first drawn with font 9 in the outline color, then the actual
  font on top. A new character then also belongs in the outline font.

## 8. Other games

**The programs** work with every AGS game:

- **AGS 3.x** (especially 3.4–3.6): fully – texts are read in a structured way, fonts, translation,
  updating, everything. Tested with Unavowed (AGS 3.6.1).
- **AGS 2.5–2.7** (games from about 2002 to 2007): also structured, including rooms, dialog lines and
  room messages. Tested with nine freeware games from AGS 2.51 to 3.4.
- **Before AGS 2.5**: text extraction only works heuristically and can miss texts or contain garbage –
  so check the result. Creating TRA files and fonts still work.
- UTF-8 translations only work from engine **3.6**, and for games with 8-bit texts only on Windows
  ([chapter 6](#which-encoding)). *Tools → Game Info...* or `AGSTranslate info` shows the engine
  version and text encoding.
- Also tested with all five Blackwell games (AGS 3.6.1, 8-bit texts, WFN fonts with only 128
  characters – umlauts have to be added with the font editor there).

**Font files adjusted for Unavowed** belong to Unavowed:

- The numbers are game-specific: `agsfnt8.ttf` is a completely different font in another game.
  Putting a foreign file into another game replaces the wrong font there.
- If another game uses **the same font** (e.g. the free pixel font *Munro*), you can use the adjusted
  file: check in the other game with the font editor which number the font has there (column *Name*),
  put the file into the game folder under that number (`agsfntN.ttf`) and check the preview.
- The fonts of a commercial game are part of the game. Adjusting them for your own use is one thing;
  **passing them on** (e.g. together with a fan translation) only if the font's license or the
  maker allows it. Many pixel fonts are free (e.g. SIL Open Font License) – the license is often in
  the font itself or on the font author's page.

## 9. What to watch out for (checklist)

**Translating**

- [ ] Never change original lines – not even spaces. Some dialog options end with a space; it is part
      of the key.
- [ ] Keep the voice prefix: `&123 Text` → `&123 Translation`.
- [ ] Keep the engine's special characters: `[` is a line break in AGS, `%s`/`%d` are placeholders
      that the script fills in (keep their order and number).
- [ ] Don't make translations much longer than the original when the text has to fit into a fixed
      GUI field (buttons, menus).
- [ ] No invisible characters (e.g. zero-width space U+200B from copied texts).
- [ ] Run *Tools → Check Translation...*: finds placeholder, prefix and length errors and
      inconsistent translations ([chapter 14](#14-checking-a-translation)).

**Creating the TRA**

- [ ] Matching encoding ([chapter 6](#which-encoding)): 8-bit if the game has 8-bit texts and all
      characters fit into the codepage; UTF-8 only for UTF-8 games or when unavoidable (then
      Windows only).
- [ ] Always create the TRA with the **current** game file – the game ID must match, otherwise the
      game does not load the translation.
- [ ] Read the warnings, especially the font warnings.

**Game folder**

- [ ] Make a **backup** before changing or replacing files.
- [ ] Only add loose files (`.tra`, `agsfntN.*`); don't change the game package (`.ags`/EXE).
- [ ] After a **game update**: run *Update from Game* and create a new TRA.
      Platforms like Steam can reset files in the game folder on updates or "Verify integrity of
      game files" – also keep your own files outside the game folder.

**Before publishing a translation**

- [ ] Test in the game: menus/GUI, dialogs, speech, a few later scenes.
- [ ] Clear the rights (texts and fonts belong to the game's makers).

## 10. Troubleshooting

| Problem | Cause / solution |
|---|---|
| The game shows everything in English | `.tra` not in the game folder, language not selected, or TRA created for another game version → recreate it with the current game file |
| Single lines stay English | Not translated yet, or the original text has changed → *Update from Game* |
| Umlauts/ß missing or blank | The font lacks the character → read the TRA warnings, add it in the font editor |
| Umlaut appears without dots ("Damon") | The font character is a copy of the base letter → add the dots in the font editor |
| Everything stays English on Linux/macOS, works on Windows | UTF-8 TRA for a game with 8-bit texts → create it as an 8-bit TRA ([chapter 6](#which-encoding)) |
| „ “ – … missing, umlauts work | 8-bit TRA: add the characters to the font at code 0x84/0x93/0x96/0x85, or use `"`/`-`; UTF-8 only on Windows |
| Text with characters like `$` `=` `\` instead of umlauts | Old translation with substitute characters for a custom font → convert to real umlauts and put the umlauts at their real codes in the font |
| Text overlaps the line above | Characters reach far above the font height (e.g. umlaut dots too high) → draw them flatter in the font editor |
| Log: "font has mistakes in data format" (WFN) | Engine 3.6.1 reports WFN fonts with unassigned characters; harmless |
| Log: "data blocks nonsequential, TextOpts" | TRA created with an old version of this tool → recreate it |

You enable the engine log in `acsetup.cfg` with
```ini
[log]
file=all:debug
```
It is then in *Saved Games/Adventure Game Studio/ags.log* (Windows: in the user folder) or
`~/.local/share/ags/ags.log` (Linux). More about the log, launch options and debug keys:
[chapter 12](#12-testing-games-and-finding-problems).

## 11. Command line (AGSTranslate)

Detailed description of all commands with examples, scripts and error handling:
**[CLI.md](CLI.md)**. Quick overview:

```
AGSTranslate info <game>                         Title, ID, AGS version, encoding
AGSTranslate extract <game> [out.trs]            All texts into a TRS
AGSTranslate trs2tra <game> <in.trs> <out.tra>   Create a TRA (with font check)
AGSTranslate tra2trs <in.tra> <out.trs> [game]   TRA back into a TRS
AGSTranslate merge <game> <old.trs|tra> <out.trs>
                                                 Bring a translation up to date with the game
AGSTranslate fonts <game> [folder]               List fonts; with a folder: export them
AGSTranslate check <game> <in.trs|tra> [glossary.txt]
                                                 Check a translation (chapter 14)
AGSTranslate install <game> <in.tra> [fonts...]  Install into the game folder (chapter 15)
AGSTranslate uninstall <game>                    Undo the last installation
AGSTranslate pack <game> <in.tra> <out.zip> [fonts...]
                                                 Create a player package (chapter 15)
AGSTranslate translate <in.trs> <out.trs> [--service deepl|claude|google] [--to de] ...
                                                 Machine-translate empty entries (chapter 16)

Options: --8bit / --utf8  set the encoding of the written TRS/TRA (see chapter 6)
```

Exit codes: `0` = OK, `1` = wrong usage, `2` = error (file, format).
Warnings appear on stderr with the prefix `Warning:`.

## 12. Testing games and finding problems

Finished AGS games have no console to type commands into. For testing and finding problems there are
three tools: the engine **log**, **launch options** and a few **debug keys**. The script
`tools/ags-test.sh` combines them for Linux.

### Test script (Linux)

`tools/ags-test.sh` starts a game from its own test folder. It contains links to the game files and
real copies of the files you want to test, plus its own `acsetup.cfg`, its own saved games and
settings. **The game folder itself is never changed**, and your real saved games stay untouched.

```bash
# Test translation and fonts, in a window, without sound
tools/ags-test.sh -t German.tra -f fonts/ --mute ~/.steam/steam/steamapps/common/"Blackwell Legacy"

# The same in debug mode (Ctrl+D shows room, characters, objects)
tools/ags-test.sh -t German.tra -f fonts/ -d "<game folder>"

# Original texts for comparison
tools/ags-test.sh -l "" "<game folder>"

# Start directly in room 5 (everything after -- goes to the engine)
tools/ags-test.sh -t German.tra "<game folder>" -- --startr 5
```

| Option | Meaning |
|---|---|
| `-t FILE` | test this TRA; it is copied into the test folder and selected |
| `-f FILE` | replace a font (can be repeated); for a folder, all `agsfnt*.wfn` and `*.ttf` in it |
| `-l NAME` | select a translation; default is the name of the `-t` file, `-l ""` = original texts |
| `-d` | the engine's debug mode (`--test`, see below) |
| `--live` | load the live hook: correct texts in the translator while playing ([live mode](#13-live-mode-correcting-the-translation-while-playing-linux)) |
| `-o FOLDER` | test folder; default `~/.cache/ags-test/<name of the game folder>` |
| `-e FILE` | engine; default is the game's `ags64`/`ags32`, otherwise `ags` from the PATH |
| `--mute` / `--software` / `--fullscreen` | no sound / software graphics (for graphics problems) / fullscreen instead of a window |
| `--fresh` | discard saved games and settings of earlier test runs |

After quitting, the script shows the important log lines and the engine's exit code. The whole log is
then in `<test folder>/ags.log`, the terminal output in `<test folder>/output.txt`.

Linux games from Steam (e.g. Wadjet Eye) bring their own engine (`ags64`, `lib64/`). For games that
only have a Windows EXE you need an installed AGS engine for Linux (`-e`), or you test on Windows by
hand as described below.

### Live mode

With `--live` the test script starts the game so that you can correct texts in the translator while
playing: [chapter 13](#13-live-mode-correcting-the-translation-while-playing-linux).

### The engine log

In the game's `acsetup.cfg` (or with the launch option `--log-file=all:debug`):

```ini
[log]
file=all:debug
```

| System | Log file |
|---|---|
| Linux | `~/.local/share/ags/ags.log` |
| Windows | `Saved Games\Adventure Game Studio\ags.log` in the user folder |
| Proton/Wine | the same file in the game's Wine prefix |

There is only one log file for all games; every start overwrites it. Engine 3.6.1 ignores the option
`--log-file-path` (tested), so the test script copies the log into the test folder after playing.
Instead of `all:debug` you can choose areas and levels, e.g. `main:info,game:debug`
(areas `main`, `game`, `script`, `sprcache`, `manobj`, `sdl`; levels `fatal`, `error`, `warn`,
`info`, `debug`).

| Log line | Meaning |
|---|---|
| `Translation loaded: German.tra` and `Translation initialized: German (format: ASCII)` | translation found and loaded; `ASCII` = 8-bit TRA |
| `Translation initialized: … (format: UTF-8)` for an 8-bit game | on Linux/macOS everything then stays English ([chapter 6](#which-encoding)) |
| `WARNING: font 'agsfnt6.wfn' has mistakes in data format` | WFN font with unassigned characters; harmless |
| `FileOpen: success, … path: …` | which files the game script opens (settings, saved games) |
| `Abort key pressed` | game aborted with Alt+X |
| `Error: the program has exited without requesting it` | the engine was not quit through the game (window closed, crash) |
| `Quitting the game...` | normal quit |

### Engine launch options

On Linux the engine is `ags64` (on Steam via `startgame`), on Windows the game's EXE.
`--help` shows all options; the most important ones (engine 3.6.1):

| Option | Effect |
|---|---|
| `--windowed` / `--fullscreen` | in a window / fullscreen |
| `--translation NAME` / `--no-translation` | choose a translation / original texts |
| `--test` | debug mode (keys see below) |
| `--startr N` | start directly in room N |
| `--loadsavedgame FILE` | load a saved game directly |
| `--log-file=all:debug` / `--log-stdout=all:debug` | log to the file / to the terminal |
| `--gfxdriver software` | software graphics instead of OpenGL |
| `--fps` | show the frame rate |
| `--novideo` | skip videos |
| `--user-data-dir FOLDER` | saved games in this folder |
| `--localuserconf` | store settings in the game folder instead of the user folder |
| `--tell-filepath` / `--tell-config` | print the paths used / the merged configuration and quit |

On Steam you can enter options under *Properties → General → Launch Options*, e.g.
`%command% --windowed --test`.

### Debug keys

| Key | Effect | Requirement |
|---|---|---|
| Alt+Enter | window ↔ fullscreen | always |
| Alt+X | abort the game immediately | as long as the game doesn't disable the key |
| Ctrl+D | room number, position of the player character, objects and characters in the room | `--test` |
| Ctrl+Alt+V | engine runtime information | `--test` |
| Ctrl+E | don't limit the frame rate | `--fps` |
| Ctrl+X, Ctrl+S, Ctrl+A … | change room, all items, walkable areas | `--test` **and** the game script contains these keys (AGS template); usually removed in commercial games |

Ctrl+D works with `--test` in commercial games too (tested with Blackwell Legacy). Real script
debugging with breakpoints and variables is only possible in the AGS Editor with the game's source
project.

### Typical questions and how to answer them

| Question | How |
|---|---|
| Is my translation loaded at all? | Log: look for `Translation initialized` |
| Why is this text still English? | Search for the text in the game's TRS (`grep -n "Text" game.trs`); if it's missing, it is a graphic or put together in the script |
| Are characters missing from the font? | `AGSTranslate trs2tra` warns; font editor preview; test in the game with `-f` |
| Which room am I in? | `-d`, then Ctrl+D |
| Where does the game save? | `--tell-filepath` or the `FileOpen` lines in the log |
| How does my translation read in the game, and where does it stumble? | [Live mode](#13-live-mode-correcting-the-translation-while-playing-linux): correct it while playing |

## 13. Live mode: correcting the translation while playing (Linux)

In live mode the translator shows every text the running game is displaying. If you change a
translation, the game shows the new text the next time, and the translator saves the TRS. So you can
enter corrections right while playing instead of noting mistakes and searching for them later.

```
 Game                                        Translator
 ┌───────────────────────┐    every text    ┌─────────────────────────────┐
 │ shows "Lebewohl, ..." ├────────────────► │ live panel: newest on top   │
 │                       │                  │ click it, edit below        │
 │ shows the correction  │ ◄────────────────┤ correction + save TRS       │
 └───────────────────────┘                  └─────────────────────────────┘
```

How this works technically is described in **[Live-Hook.md](Live-Hook.md)**.

### What you need

- **Linux** and a game with **its own 64-bit Linux engine** (`ags64` in the game folder), e.g. the
  Linux versions of the Wadjet Eye games on Steam. Tested with Blackwell Legacy (AGS 3.6.1).
  Windows games don't work yet.
- The **TRS** of the translation and the **TRA** created from it, plus the adjusted fonts if any.
- Either the **release package** `…-linux-x64.tar.gz` (contains the translator, `ags-test.sh` and
  the hook `agslive.so`) or the **source code** with .NET 9 SDK, `gcc` and `make`.

### Step by step

You need two terminals: one for the translator, one for the game. In the examples the translation and
fonts are in `~/translation/` (`German.trs`, `German.tra`, `fonts/`).

**1. Start the translator with the TRS**

Release package (in the unpacked folder):
```bash
./AGS_TranslationEditor ~/translation/German.trs
```
Source code (in the repository folder):
```bash
dotnet run -c Release --project AGS_TranslationEditor.Avalonia -- ~/translation/German.trs
```
You can also open the TRS as usual via *File → Open*.

**2. Turn live mode on**

Click **● Live** in the toolbar. The live panel appears on the right with "Waiting for the game".
The translator now waits for the game; the order doesn't matter, you can also start the game first.

**3. Start the game with the hook (second terminal)**

Release package:
```bash
./ags-test.sh --live -t ~/translation/German.tra -f ~/translation/fonts "<game folder>"
```
Source code:
```bash
tools/ags-test.sh --live -t ~/translation/German.tra -f ~/translation/fonts "<game folder>"
```
The game folder is e.g. `~/.steam/steam/steamapps/common/Blackwell Legacy`. It is not changed:
the game runs from a test folder under `~/.cache/ags-test/`, with its own saved games
([chapter 12](#test-script-linux)). From source, the script builds the hook itself the first time.

The terminal must show:
```
agslive: get_translation hooked
agslive: editor connected
```
and the translator "Game connected".

**4. Play and correct**

- Every displayed text appears on the right, **newest on top**: large the translation shown, below it
  small the original. Below that you may see *untranslated*, *corrected live* or
  *not in this translation file*.
- **Click** a text: the entry is selected in the table.
- Change it at the bottom of the editor under **TRANSLATION**. Changes made directly in the table or
  with *Translate* also go to the game.
- The change appears **as soon as the game shows the text the next time**. A speech line that is on
  screen right now does not change retroactively.

How to see a change again quickly:

| Kind of text | How it appears again |
|---|---|
| Hotspot and object names ("window", "door") | move the mouse away and back over it |
| Descriptions when looking at things | look at the item again |
| Conversation lines | save before the conversation, load the saved game after the correction |
| Menus and dialog options | close the menu and open it again |

**5. Saving**

With the checkbox **Save the TRS after each correction** (default: on) the translator saves the TRS
1.5 seconds after the last change. This only works for `.trs` files; an open TRA has to be saved as a
TRS with *Save As* first. Without the checkbox you save as usual with Ctrl+S.

If you want to try things out safely first, work with a copy of the TRS.

**6. Finally: create a new TRA**

The corrections only apply in the game while the translator is running with live mode. If you
restart the game, the translator sends them again automatically. They only get into the game
permanently with a new TRA:

```bash
AGSTranslate trs2tra "<game folder>/<game>.ags" ~/translation/German.trs ~/translation/German.tra
```
or in the translator *File → Export → Export as TRA*. Then copy the TRA into the game folder as in
[chapter 4](#4-getting-the-translation-into-the-game).

### Good to know

- The translator adds the voice prefix `&N ` automatically, so voice acting keeps working.
- An empty translation shows the original in the game, just like the newly created TRA would.
- Texts marked *not in this translation file* are put together by the game itself or are missing from
  the TRS; they cannot be edited. If many are missing, *Tools → Update from Game* helps.
- If you load another file in the translator, it discards the previous live corrections in the game.
- The translator and the game connect via `$XDG_RUNTIME_DIR/agslive.sock`. The environment variable
  `AGSLIVE_SOCKET` chooses a different path (set it the same for both, at most 107 characters).
- You can build the hook by hand: `make -C AGSLive`.

### If something doesn't work

| Problem | Cause / solution |
|---|---|
| The live panel stays at "Waiting for the game" | game started without `--live`, or `AGSLIVE_SOCKET` set for only one of the two |
| `agslive: get_translation hooked` missing in the terminal | the hook was not loaded: `--live` forgotten, or the game doesn't use a 64-bit Linux engine |
| `agslive: _Z15get_translationPKc not found` | engine without exported function names; live mode doesn't work with this game |
| `agslive: unknown instructions at the start of get_translation` | unknown engine build; to be safe the hook changes nothing. Please report it with the bytes shown |
| "Another editor already waits for the game" | live mode is already on in a second translator; close it |
| "Not saved automatically: only .trs files are saved" | a TRA, CSV or PO is open; save it as a TRS with *Save As* |
| A change does not appear | the text is only fetched again the next time it is shown (table above) |
| The game doesn't start with `--live` | test without `--live`; the output is in `~/.cache/ags-test/<game>/output.txt` |

## 14. Checking a translation

Many mistakes otherwise only show up while playing: a button text that gets cut off, a forgotten
`%d`, a sentence translated once with the informal and once with the formal "you". The check finds
such spots in the whole translation at once.

**In the translator:** open the translation → *Tools → Check Translation...* → choose the game file
(`.ags`, EXE or `game28.dta`). A list of findings appears on the right. Clicking a finding jumps to
the entry, which you can then fix directly. The check doesn't change the file.

**On the command line:**

```bash
AGSTranslate check <game> <translation.trs|tra> [glossary.txt]
```

### What is checked

| Kind | Level | What it means |
|---|---|---|
| Placeholders | error | `%d`, `%s` etc. are missing or in a different order, or special texts like `@SCORE@` are missing. The script fills the placeholders in order – swapped placeholders show wrong values or crash the game. |
| Voice prefix | warning | The translation doesn't start with the original's `&123`. The game then doesn't play the voice line. |
| Too wide | warning | The translated text is wider than its button (measured in the real game font, in pixels). Buttons cut off texts that are too long. |
| Too many lines | warning | A label wraps the text; the translation needs more lines than the label is tall. |
| Spaces | warning | Spaces at the start or end are missing or extra – visible in dialog options and texts put together from parts. Spoken lines (`&123`) are not reported here. |
| Glossary | warning | A term from the glossary is translated differently than defined (see below). |
| Inconsistent | note | The same original text is translated differently in different places. Short spoken lines like "All right." may differ and are only reported from four words on. |
| Names | note | A character name the translation keeps (e.g. "Rosa") is missing from a line whose original contains it. |
| Line breaks | note | The number of line breaks `[` differs from the original. |
| Built at runtime | warning/note | The script puts a text together while playing with `String.Format`, e.g. `String.Format("%s[[%s", title, text)` for a notes popup, and shows it on a label, with Say or Display. The engine then looks up the *finished* text in the translation – the translated parts don't help, the text stays in the original language. Fix: extra entries for the finished texts (original `Allen Reiken's obituary[[We found…` → `Allen Reikens Nachruf[[Wir haben…`). If such entries exist, only a note with their number is shown. Without a visible display nearby it is a note as well. |
| Formal address (Sie) | note | German only: "Sie", "Ihnen", "Ihr" in the middle of a sentence. This finds places where a character is suddenly addressed formally – and typos like "bis **Sie** fertig ist" instead of "sie". |

*Notes* are not errors: you look through them and decide yourself. In the German Blackwell Legacy
translation, for example, the check found "Was wollen **sie** wissen?" and a line that once reads
"Wenn Sie hier wohnen würden…" and once "Wenn du hier wohnen würdest…".

### Text width: how exact is it?

- **WFN fonts** (bitmap): exact, the widths are stored pixel by pixel in the font.
- **TrueType fonts**: estimated from the font's character widths and the font size from the game
  data. The engine's hinting and kerning are missing, so a pixel or two of difference is possible. The
  message says "(TrueType: estimated)".
- It is only reported when the translation is **too wide and wider than the original**. Where the
  original doesn't fit either, it is evidently intended.
- Only texts that are fixed in the GUI are checked. Many games set button texts from the script
  (Blackwell's GUI only contains placeholders like "New Button") – the check can't see those; only
  testing ([chapter 12](#12-testing-games-and-finding-problems)) or
  [live mode](#13-live-mode-correcting-the-translation-while-playing-linux) helps there.
- Labels with `@OVERHOTSPOT@` (shows the name under the mouse cursor) are checked with every
  translated hotspot, object, character and item name.

### Glossary

A glossary defines how terms and names are translated. It is a simple text file (UTF-8), one line per
term:

```
# Comments start with #
Auntie = Tante
spirit guide = Geistführer | Geisterführer
Deacon = Deacon
```

- Original on the left, translation on the right. Several allowed translations are separated by `|`.
- If a term should stay untranslated, write it on both sides (`Deacon = Deacon`).
- Case doesn't matter. The original must appear as a whole word ("spirit" doesn't match "spirited");
  the translation only has to be contained somewhere ("Tante" matches "liebe Tante").

The translator automatically uses a file **`glossary.txt`** or **`Glossar.txt`** in the same folder
as the open translation. On the command line you give it as the third argument.

### Tip: decide on the form of address

Who addresses whom informally or formally is best decided before translating and written at the top
of the glossary as a comment (`# Rosa ↔ Joey: du`, `# Rosa → policeman: Sie`). The notes
"Formal address" and "Inconsistent" then show the spots that don't fit.

## 15. Installing and sharing with players

### Installing into your own game

*Tools → Install into Game...* does steps 1–4 from [chapter 4](#4-getting-the-translation-into-the-game)
at once:

1. Open the translation, choose *Install into Game...* and give the game file.
2. Confirm. If you like, give a folder with changed fonts (`agsfntN.ttf`/`.wfn`).
3. The translator creates the TRA (name = name of the open file, e.g. `German.trs` → language
   "German"), copies it and the fonts into the game folder and sets the language.

This is what happens:

- **Backup:** every file that gets replaced is first copied to `agstranslate-backup/<date-time>/`
  in the game folder. There is also a list (`manifest.txt`) of what was added and what was replaced.
- **Language:** `[language] translation=German` is set in the game folder's `acsetup.cfg`. Also in
  the per-user `acsetup.cfg` if it exists: the engine stores the language chosen last there when
  quitting (Linux: `~/.local/share/ags/<game>/acsetup.cfg`), and that setting takes priority over
  the one in the game folder.
- **Checks beforehand:** the TRA must match the game (game ID and name); fonts must be called
  `agsfntN.ttf`/`agsfntN.wfn`. If a file in the game folder is a link (e.g. from a test folder), the
  installation stops, so that no other file is changed through the link.
- **Font warning afterwards:** if the game doesn't use an installed font at all (e.g. because there
  is an `agsfntN.ttf` that takes priority over `agsfntN.wfn`), the message says so.

**Undo:** *Tools → Uninstall from Game...* puts the backed-up files back and restores
`acsetup.cfg`. **Nothing** is deleted in the process: files the installation added are moved into the
subfolder `removed` of the backup folder. Several installations in a row (e.g. after improvements) are
undone in reverse order – each uninstall undoes the most recent installation.

Command line: `AGSTranslate install <game> <German.tra> [fonts...]` and
`AGSTranslate uninstall <game>` ([CLI guide](CLI.md#install--uninstall--installing-into-the-game-folder)).

> Steam & co. can reset files in the game folder on an update or "Verify integrity of game files".
> Then simply install again.

### Creating a player package

*Tools → Create Player Package...* creates a zip file you can pass on to players.
It contains a folder `German-Patch` with:

| File | Purpose |
|---|---|
| `German.tra` | the translation |
| `agsfntN.*` | changed fonts, if given |
| `install.sh` / `uninstall.sh` | installation for Linux and macOS (`sh install.sh`) |
| `install.bat` / `uninstall.bat` | installation for Windows (double-click) |
| `LIESMICH.txt` | instructions for players (German, with a short English part) |

For players:

1. Unpack the folder `German-Patch` into the game folder.
2. Double-click `install.bat` (Windows) or run `sh install.sh` in a terminal (Linux/macOS).
   The scripts back up every replaced file in `German-backup` in the game folder. A second run does
   not overwrite this backup.
3. On Linux/macOS the script sets the language in `acsetup.cfg`. On Windows you choose it in the
   game's setup (`winsetup.exe` or the game EXE with `--setup`).

`uninstall` restores the previous state and moves added files into the backup folder instead of
deleting them.

Good to know:

- The TRA is built for **the chosen game file**. Players need the same game version; after a game
  update you have to create a new package.
- For a package that works on Linux and macOS, use an **8-bit TRA** if the game has 8-bit texts
  ([chapter 6](#which-encoding)). The program warns about a UTF-8 TRA.
- **Rights:** fonts and texts belong to the game's makers. Only include fonts if you are allowed to –
  the program reminds you of this when creating the package. Best ask the makers beforehand.
- The Linux script is tested (installation, second run, uninstallation). The Windows scripts are
  simple batch files but not yet tried on a real Windows.

## 16. Machine translation (DeepL, Claude, Google)

The **Translate** button in the toolbar translates by machine:

- If a line is selected, only that one.
- If none is selected, **all empty entries**. The program first asks, showing how many there are.
  Lines that are already translated are never overwritten.

The result is a **suggestion**: you should read and adjust every line. Afterwards
*Tools → Check Translation...* is worth running ([chapter 14](#14-checking-a-translation)).

### Setting up the service

*Tools → Settings...*, section *Machine translation*:

| Setting | Meaning |
|---|---|
| Service | **Google** (free, no key, unofficial interface, can be blocked), **DeepL** (API key; a free account covers 500,000 characters a month) or **Claude** (Anthropic API key, paid by tokens) |
| Languages | language codes, e.g. `en` → `de`. Source empty or `auto`: the service detects it |
| Form of address | default, informal (*du*) or formal (*Sie*) (DeepL and Claude only) |
| DeepL / Anthropic API key | the service's key. Alternatively the environment variables `DEEPL_AUTH_KEY` or `ANTHROPIC_API_KEY` |
| Claude model | empty = `claude-opus-5-5`; you can enter another model here, e.g. a cheaper one |

The keys are stored in plain text in the settings file
(`~/.config/AGSTranslationEditor/settings.json` or `%APPDATA%\AGSTranslationEditor\settings.json`).
On Linux and macOS only your own user can read it. If you don't want that, leave the field empty and
set the environment variable.

### What happens to the AGS characters

All services get the texts in a way that keeps the control characters:

- The **voice prefix** `&123 ` is split off before sending and put back in front afterwards.
- **Spaces** at the start and end stay as in the original.
- **Line breaks** `[`: DeepL gets them as protected tags, Google translates each line separately,
  Claude is instructed to keep them.
- **Placeholders** like `%s`, `%d`, `@SCORE@` and the literal `\[`: protected for DeepL, by instruction
  for Claude. *Check Translation* shows whether they really are all still there.

### Differences between the services

- **Claude** gets the lines in game order (40 per request), plus the game title, the chosen form of
  address and the matching terms from the **glossary file** (`glossary.txt` or `Glossar.txt` next to
  the translation, [chapter 14](#glossary)). Dialogs and names therefore usually fit together better.
  If a request is declined for safety reasons, the API itself retries with a suitable other model.
- **DeepL** translates 50 lines per request, very fast, with a selectable form of address, but without
  a glossary.
- **Google** translates line by line and is therefore slow with many entries.

If a translation stops (wrong key, quota used up, no connection), the lines translated up to then are
kept; the status bar shows the reason.

**Privacy and rights:** the game's texts go to the chosen service. Only do this if you may use the
texts for it.

### On the command line

```bash
export DEEPL_AUTH_KEY=...           # or ANTHROPIC_API_KEY for Claude
AGSTranslate translate German.trs German-mt.trs --service deepl --to de --informal
AGSTranslate translate German.trs German-mt.trs --service claude --glossary glossary.txt
```

Details in [CLI.md](CLI.md#translate--machine-translating-empty-entries).

---

## 17. Context: who says what?

Whether "you" becomes informal or formal, whether "Open" is a button or a sentence, and which gender a
character has is not in the text itself. The editor can therefore show for each entry **who** says it
and **where** it comes from. This is optional and off by default.

**Turning it on:** *Tools → Settings... → Context: Show speaker and origin of texts*. After that,
*Extract Strings...* and *Update from Game...* also save a file `Name.context.json` next to the
TRS. For an existing translation, run *Update from Game...* once (or on the command line
`extract`/`merge` with `--context`).

**What you see:**

- a column **Context**, e.g. `Rosa · Dialog 19`, `Joey · room12.crm`, `(Display) · GlobalScript.asc`
  or, for buttons, the name of the GUI element,
- a filter field **Character or room...** in the toolbar: `Rosa` shows only Rosa's lines,
  `room12` only the texts from room 12, `Dialog 19` only that dialog. Together with
  *Untranslated only* you see e.g. all open lines of one character.

**Where the information comes from:** the speaker is read from the compiled script
(`cRosa.Say("…")`, in old games `DisplaySpeech(ROSA, "…")`). This works for most dialog lines – in
Blackwell Legacy, for example, for over 90 % of the script texts. Texts without a speaker are mostly
menu, note or debug texts. A text said by two characters shows the first one.

The context file is only for the editor: it doesn't belong in the game and doesn't need to be passed
on. If it is missing or the setting is off, the editor looks the same as always.

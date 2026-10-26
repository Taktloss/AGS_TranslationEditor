# AGSTranslate – Command Line

*Deutsche Fassung: [CLI.md](../CLI.md)*

`AGSTranslate` does everything you need for an AGS translation without a graphical interface:
inspect a game, extract its texts, update a translation, create a TRA and convert it back, list and
export fonts. This also lets you build repeatable workflows in scripts.

The graphical programs (translator and font editor) are described in the [user guide](UserGuide.md).

## Contents

1. [Installation and usage](#1-installation-and-usage)
2. [Overview](#2-overview)
3. [Commands](#3-commands)
   - [info](#info--inspecting-a-game)
   - [extract](#extract--getting-the-texts-out-of-the-game)
   - [merge](#merge--bringing-a-translation-up-to-date-with-the-game)
   - [trs2tra](#trs2tra--creating-the-tra-for-the-game)
   - [tra2trs](#tra2trs--turning-a-tra-back-into-a-trs)
   - [fonts](#fonts--listing-and-exporting-fonts)
   - [check](#check--checking-a-translation)
   - [install / uninstall](#install--uninstall--installing-into-the-game-folder)
   - [pack](#pack--creating-a-player-package)
   - [translate](#translate--machine-translating-empty-entries)
   - [Converting from CSV/TSV/PO](#converting-from-csvtsvpo-old-form)
4. [Encoding: --8bit and --utf8](#4-encoding---8bit-and---utf8)
5. [Understanding warnings](#5-understanding-warnings)
6. [Exit codes and error messages](#6-exit-codes-and-error-messages)
7. [The TRS format](#7-the-trs-format)
8. [Typical workflows](#8-typical-workflows)
9. [Scripts](#9-scripts)
10. [FAQ](#10-faq)

---

## 1. Installation and usage

### Running from source

Needs the [.NET SDK](https://dotnet.microsoft.com/download) (9 or later):

```bash
dotnet run --project AGSTranslate -- info "Unavowed/Unavowed.exe"
```

Everything after `--` goes to AGSTranslate. This is handy for trying things out but rebuilds on every
call. For frequent use, build once:

```bash
dotnet build AGSTranslate -c Release
dotnet AGSTranslate/bin/Release/net9.0/AGSTranslate.dll info "Unavowed/Unavowed.exe"
```

### As a single executable

`dotnet publish` creates a single file that runs without an installed .NET:

```bash
# Linux
dotnet publish AGSTranslate/AGSTranslate.csproj -c Release -r linux-x64 \
    --self-contained -p:PublishSingleFile=true -o publish/linux
# Windows
dotnet publish AGSTranslate/AGSTranslate.csproj -c Release -r win-x64 \
    --self-contained -p:PublishSingleFile=true -o publish/windows
```

Then run `publish/linux/AGSTranslate` or `publish\windows\AGSTranslate.exe` directly (about 70 MB,
because the .NET runtime is included). Without `--self-contained` the file is small but needs an
installed .NET.

The release archives already contain AGSTranslate. The examples below simply say `AGSTranslate`.

### Giving the game file

Wherever `<game>` appears, you can give:

- the **game EXE** (game data appended to the EXE, or an EXE with an `.ags` next to it – typical for
  Steam/GOG),
- the **`.ags` file**,
- a loose **`game28.dta`** (or `ac2game.dta` for very old games).

Put paths with spaces in quotes.

## 2. Overview

```
AGSTranslate info <game>                         Title, ID, AGS version, encoding
AGSTranslate extract <game> [out.trs]            All texts of the game into a TRS
AGSTranslate merge <game> <old.trs|tra> <out.trs>
                                                 Bring a translation up to date with the game
AGSTranslate trs2tra <game> <in.trs> <out.tra>   Create the TRA for the game (with font check)
AGSTranslate tra2trs <in.tra> <out.trs> [game]   TRA back into a TRS
AGSTranslate fonts <game> [folder]               List fonts; with a folder: export them
AGSTranslate check <game> <in.trs|tra> [glossary.txt]
                                                 Check a translation: placeholders, consistency,
                                                 glossary, text width in GUI buttons
AGSTranslate install <game> <in.tra> [fonts...] [--force]
                                                 TRA and fonts into the game folder (with backup)
AGSTranslate uninstall <game>                    Undo the last installation
AGSTranslate pack <game> <in.tra> <out.zip> [fonts...]
                                                 Zip for players with install scripts
AGSTranslate translate <in.trs|tra> <out.trs> [--service google|deepl|claude] [--from en] [--to de]
                       [--informal|--formal] [--glossary file] [--model claude-model]
                                                 Machine-translate empty entries
AGSTranslate <game> <in.csv|tsv|po|trs> <out.tra>
                                                 Convert into a TRA (old form)

Options: --8bit  write the TRS/TRA in the game's 8-bit codepage (works everywhere)
         --utf8  write the TRS/TRA as UTF-8 (AGS 3.6+; for 8-bit games Windows only)
         --context  extract, merge: also write <out>.context.json with speaker and origin
```

Without arguments (or with wrong ones) AGSTranslate shows this help and exits with code 1.

Output:
- normal messages on **stdout**,
- progress (`Reading rooms...`), warnings (`Warning: ...`) and errors (`Error: ...`) on
  **stderr**. So you can redirect the output without losing warnings.

## 3. Commands

### info – inspecting a game

```bash
AGSTranslate info <game>
```

Shows which game it is and how it stores texts. The first command for a new game.

```
$ AGSTranslate info Unavowed/Unavowed.exe
Title:       Unavowed
Game ID:     156899796
AGS version: 3.6.1.35
Encoding:    8-bit, codepage 1252
Game data:   /home/.../Unavowed/unavowed.ags
```

| Line | Meaning |
|---|---|
| Title / Game ID | Written into every TRA. The game only loads TRAs with a matching ID. |
| AGS version | Engine version. **UTF-8 translations only from 3.6.** |
| Encoding | How the game itself stores texts: `UTF-8` or `8-bit, codepage N` (1252 = Western Europe). Decides which TRA encoding fits ([chapter 4](#4-encoding---8bit-and---utf8)). |
| Game data | Only if the data is in a different file than the one given (e.g. EXE + `.ags`). |

### extract – getting the texts out of the game

```bash
AGSTranslate extract <game> [out.trs] [--8bit|--utf8] [--context]
```

Reads all translatable texts from the game data and writes a TRS with empty translations.
Without `out.trs` the file is created next to the game file (`Unavowed.trs`) – but usually you want
to give your own working folder.

With `--context` you also get `out.context.json` next to the TRS: for every text it lists the source,
room/script/dialog and – where it can be detected – the speaking character (or `(Display)` for
narration). The editor shows this as the *Context* column when it is turned on in the settings
([user guide, chapter 17](UserGuide.md#17-context-who-says-what)). The TRS itself stays unchanged.

```
$ AGSTranslate extract Unavowed/Unavowed.exe work/unavowed.trs
Extracting texts from: Unavowed/Unavowed.exe
Game: Unavowed (AGS 3.6.1.35), 125 rooms
  Script          10279
  DialogOption     2515
  Gui                90
  CharacterName      63
  InventoryItem      53
  Hotspot           376
  RoomObject        196
  RoomScript       3865
17437 texts → work/unavowed.trs
```

What is found:

| Source | Content |
|---|---|
| Script | Texts from the global scripts and dialog scripts (speech, `Display`, ...) |
| DialogOption | Dialog options (including ones with a trailing space) |
| Gui | Captions of buttons, labels, lists in menus/GUIs |
| CharacterName / InventoryItem | Names of characters and items (in full length) |
| Hotspot / RoomObject | Names of hotspots and objects in all rooms |
| RoomScript | Texts from the room scripts |

Every text appears only once. Texts without letters (`...`, `&12 ...`) are left out, as are script
identifiers and similar things that are not display text.

The tool reads games from **AGS 2.5** on in a structured way. Only for even older games does it search
for texts heuristically; the result may then have gaps or nonsense lines.

The TRS gets the game's encoding. How to change it if needed is described in
[chapter 4](#4-encoding---8bit-and---utf8).

### merge – bringing a translation up to date with the game

```bash
AGSTranslate merge <game> <old.trs|old.tra> <out.trs> [--8bit|--utf8] [--context]
```

`--context` rewrites `out.context.json` just like with `extract`.

Gets the current texts from the game and carries the existing translations over to them. You need
this:

- after a **game update**,
- when an **old translation** (e.g. made with an older tool) doesn't contain all texts,
- when you only have a **TRA** left (it is read directly).

```
$ AGSTranslate merge Unavowed/Unavowed.exe Unavowed/German.trs work/German.trs
17437 texts in the game
  unchanged:            5724
  similar (spaces/&N):   164
  untranslated:        11549
  obsolete (kept):         0
Done → work/German.trs
```

| Line | Meaning |
|---|---|
| unchanged | Original text identical, translation kept. |
| similar | The original text only differs in spaces at the edges or in the voice prefix `&N`. Translation kept; the translation's prefix is adjusted to the new original (`&32 Raus damit.` → `&40 Raus damit.`). |
| untranslated | Texts without a translation – new in the game or not captured before. |
| obsolete (kept) | Translated lines whose original no longer exists in the game. They are at the end of the file (the game ignores them) so nothing is lost. Delete them by hand if needed. |

The output file has exactly the texts of the game in game order. The settings of the old translation
(encoding, fonts, language) are kept; the game ID and title come from the game.

**Important:** don't choose `out.trs` equal to `old.trs`, but a new file – that way the old one stays
as a backup until you have checked the result.

### trs2tra – creating the TRA for the game

```bash
AGSTranslate trs2tra <game> <in.trs> <out.tra> [--8bit|--utf8]
```

Compiles the TRS into the TRA the game loads. The game ID and title are taken from the game.
Afterwards the tool checks whether the game's fonts can show all special characters of the
translation.

```
$ AGSTranslate trs2tra Unavowed/Unavowed.exe work/German.trs work/German.tra
Game: Unavowed  ID: 156899796  AGS 3.6.1.35
Entries in TRS: 17461 (translated: 5912)
Done → work/German.tra (UTF-8)
```

- Only translated lines go into the TRA; the game shows empty translations in the original.
- The encoding (`UTF-8` or `8-bit`) comes from the TRS (`//#Encoding=`) or from `--8bit`/`--utf8`.
- The **file name** is the name of the language in the game: `German.tra` → language "German".
- Copy the TRA into the game folder and choose the language (the game's setup or `acsetup.cfg`:
  `[language]` / `translation=German`).
- For warnings see [chapter 5](#5-understanding-warnings).

### tra2trs – turning a TRA back into a TRS

```bash
AGSTranslate tra2trs <in.tra> <out.trs> [game] [--8bit|--utf8]
```

Turns a compiled translation back into an editable TRS – e.g. when only the TRA is left. The TRS only
contains the lines that are translated in the TRA; for a complete list use `merge` afterwards (or call
`merge` with the TRA right away).

```
$ AGSTranslate tra2trs Unavowed/German.tra work/old.trs Unavowed/Unavowed.exe --utf8
Entries in TRA: 5748
Done → work/old.trs
```

The game file is optional but recommended for **8-bit TRAs**: the TRA doesn't store its codepage, so
the tool otherwise assumes 1252. With `--utf8` you get a UTF-8 TRS even if the TRA was 8-bit
(umlauts are converted correctly).

### fonts – listing and exporting fonts

```bash
AGSTranslate fonts <game> [folder]
```

Lists all fonts the way the engine loads them:

```
$ AGSTranslate fonts Unavowed/Unavowed.exe
  #  File           Kind Origin  Size Outline Chars  Name
  0  agsfnt0.ttf    TTF  folder    10 auto      122  Munro
  1  agsfnt1.ttf    TTF  folder    10 auto      156  LucasFan-Font
  2  agsfnt2.wfn    WFN  folder     1 auto      143
  4  agsfnt4.ttf    TTF  package   16 none     3007  Arial
  5  agsfnt5.ttf    TTF  folder    16 auto      448  BBSesque
  8  agsfnt8.ttf    TTF  folder    16 font 9    110  IsWasted
  ...
```

| Column | Meaning |
|---|---|
| # | Font number in the game |
| File | File name (`agsfntN.ttf` or `.wfn`; if both exist, `.ttf` wins) |
| Kind | TTF (TrueType) or WFN (bitmap font) |
| Origin | `folder` = loose file in the game folder (takes priority), `package` = inside the `.ags`/EXE |
| Size | Size at which the font is drawn |
| Outline | `none`, `auto` (automatic outline) or `font N` (outline with another font) |
| Chars | Number of characters in the font |
| Name | Name of the font (TTF only) |

With a **folder**, all font files are copied there **unchanged**:

```
$ AGSTranslate fonts Unavowed/Unavowed.exe work/fonts
...
19 font files → work/fonts
```

The game folder is blocked as a target (`Error: choose an output folder other than the game folder.`),
because files there would replace the game's fonts. Existing files in the target folder are
overwritten. You can edit the fonts with the font editor ([user guide](UserGuide.md#7-font-editor)).

### check – checking a translation

```bash
AGSTranslate check <game> <translation.trs|tra> [glossary.txt]
```

Checks the translation against the game and lists the findings grouped by kind. The file is not
changed.

```
$ AGSTranslate check Unavowed/unavowed.ags work/German.trs glossary.txt
== Too wide (1) ==
WARNING: Skip to Origin
    Button "Button1" (font 0, 81×18 px): text is 93 px wide, 12 px too wide (TrueType: estimated)

== Inconsistent (2) ==
INFO: &15 What do you want to know?
    translated in 3 ways: "Was wollen Sie wissen?" (1×), "Was wollen sie wissen?" (1×), "Was willst du wissen?" (1×)
...
0 errors, 1 warnings, 2 notes
```

Each finding shows the level (`ERROR`, `WARNING`, `INFO`), the original text (as in the TRS) and an
explanation. What the individual kinds mean, how exact the width measurement is and what a glossary
looks like is described in the [user guide, chapter 14](UserGuide.md#14-checking-a-translation).

Exit code: `2` if at least one **error** (wrong placeholders) was found, otherwise `0`.
So a script can stop the TRA build:

```bash
AGSTranslate check game.ags German.trs glossary.txt > check.txt && \
AGSTranslate trs2tra game.ags German.trs German.tra
```

### install / uninstall – installing into the game folder

```bash
AGSTranslate install <game> <German.tra> [fonts...] [--force]
AGSTranslate uninstall <game>
```

`install` copies the TRA and the given fonts into the game folder and sets the language in
`acsetup.cfg` (in the game folder and in the engine's per-user `acsetup.cfg`, if it exists).
Fonts can be files (`agsfntN.ttf`/`.wfn`) or folders; for a folder all `agsfnt*` files in it are
used.

```
$ AGSTranslate install "Blackwell Legacy/Blackwell Legacy.ags" work/German.tra work/fonts
Installed "German" into /games/Blackwell Legacy
  added     German.tra
  added     agsfnt6.wfn
  replaced  agsfnt7.wfn (original backed up)
  language  set in /games/Blackwell Legacy/acsetup.cfg
Backup: /games/Blackwell Legacy/agstranslate-backup/20261026-053505
Undo with: AGSTranslate uninstall <game>
```

- Every replaced file and every changed `acsetup.cfg` is first backed up in
  `agstranslate-backup/<date-time>/`.
- It stops **without changes** if the TRA was created for another game or another version (with
  `--force` it goes ahead anyway), a font is named wrongly, the source files are already in the game
  folder, or a target file is a link.

`uninstall` undoes the **last** installation: backed-up files come back, added files are moved to
`agstranslate-backup/<…>/removed/` (not deleted). Called several times, it goes back installation by
installation.

### pack – creating a player package

```bash
AGSTranslate pack <game> <German.tra> <out.zip> [fonts...]
```

Creates a zip file with the folder `German-Patch`: TRA, fonts, `install.sh`/`uninstall.sh`
(Linux/macOS), `install.bat`/`uninstall.bat` (Windows) and `LIESMICH.txt` (German, with a short
English part). Players unpack the folder into the game folder and run the install script. Details:
[user guide, chapter 15](UserGuide.md#creating-a-player-package).

The name of the TRA must not contain spaces or special characters (it is used in the scripts).
There are warnings for a TRA made for another game version, for a UTF-8 TRA for an 8-bit game and
when fonts are included (rights!).

### translate – machine-translating empty entries

```bash
AGSTranslate translate <in.trs|tra> <out.trs> [--service google|deepl|claude] [--from en] [--to de]
                       [--informal|--formal] [--glossary file] [--model claude-model]
```

Translates all **empty** entries with the chosen service and writes the result as a TRS.
Existing translations stay unchanged.

| Option | Default | Meaning |
|---|---|---|
| `--service` | `google` | `google` (no key, unofficial), `deepl` or `claude` |
| `--from` / `--to` | `en` / `de` | language codes; `--from auto` lets the service detect the language |
| `--informal` / `--formal` | – | informal or formal form of address, e.g. German *du* / *Sie* (DeepL, Claude) |
| `--glossary` | – | glossary file ([format](UserGuide.md#glossary)); only Claude uses it |
| `--model` | `claude-opus-5-5` | another Claude model |

The keys come from the environment variables `DEEPL_AUTH_KEY` and `ANTHROPIC_API_KEY`, not from the
command line, so they don't end up in the shell history.

```bash
export DEEPL_AUTH_KEY=xxxxxxxx:fx
AGSTranslate translate German.trs German-mt.trs --service deepl --informal
```

```
Translating 1532 empty entries with DeepL (en → de)
1532 entries translated → German-mt.trs
```

Voice prefixes, spaces at the edges, `[` line breaks and placeholders are kept (details in
[chapter 16 of the user guide](UserGuide.md#16-machine-translation-deepl-claude-google)). If the
service stops halfway, AGSTranslate still writes the entries translated up to then and exits with
code 2. Check the result with `check` afterwards.

### Converting from CSV/TSV/PO (old form)

```bash
AGSTranslate <game> <in.csv|in.tsv|in.po|in.trs> <out.tra> [--8bit|--utf8]
```

Creates a TRA directly from a table or a PO file (e.g. from a translation service):

- **CSV**: column 1 original, column 2 translation, separator `;`. Fields containing `;`, quotes or
  line breaks go in `"..."` (double the quotes inside).
- **TSV**: like CSV, separator tab.
- **PO**: `msgid` = original, `msgstr` = translation.
- Save CSV/TSV/PO as UTF-8. The TRA becomes 8-bit (UTF-8 for UTF-8 games); `--utf8` forces UTF-8.

## 4. Encoding: --8bit and --utf8

`--8bit` and `--utf8` can be placed anywhere and apply to `extract`, `merge`, `tra2trs`, `trs2tra`
and the conversion form. They set the encoding of the written TRS/TRA (`//#Encoding=ASCII` or
`//#Encoding=UTF-8`); the texts are converted correctly. Without an option the encoding of the input
or of the game applies.

### Which encoding?

`info` shows under `Encoding` how the game stores its texts:

| Game | Translation contains | Encoding |
|---|---|---|
| `UTF-8` | any characters | UTF-8 |
| `8-bit, codepage 1252` | only characters of the codepage (German, French, Spanish, Italian …) | **8-bit** – runs with every engine on Windows, Linux and macOS |
| `8-bit, codepage 1252` | characters outside the codepage (Polish, Russian …) | UTF-8 – only AGS 3.6+ and **only on Windows** |

**Why not always UTF-8?** If the game stores its texts as 8-bit, with a UTF-8 TRA the engine converts
all original texts with a Windows function (`setlocale(LC_CTYPE, ".1252")` in
`Engine/ac/translation.cpp`). This locale doesn't exist on Linux and macOS: every key becomes empty,
and the game stays entirely in the original language – without an error message. Noticed with
Blackwell Legacy on Linux: the same translation as an 8-bit TRA works. The tool warns:

```
Warning: The game stores its texts as 8-bit, so for a UTF-8 translation the engine converts the keys with a
Windows locale (".1252"). The Linux and macOS engine cannot do that and shows the original texts. All characters
of this translation exist in codepage 1252: an 8-bit translation works everywhere.
```

**„ “ – … in an 8-bit TRA:** these characters exist in codepage 1252 (bytes 0x84, 0x93, 0x96, 0x85),
but the engine looks them up in the font at exactly these codes – where TrueType fonts have no
character. Either use `"` and `-` or create the characters at these codes in the font editor
(*Go to* `0x84` → *Copy from* or draw).

**Engines before 3.6** don't know UTF-8:

```
Warning: UTF-8 translations need AGS 3.6 or later, but the game uses AGS 3.5.1.14; it will show garbled special characters. Use an 8-bit translation.
```

**Converting a TRS:** don't just change the header line (the file would stay stored in the old
encoding), but let it be rewritten:

```bash
AGSTranslate merge <game> old.trs new.trs --8bit     # or --utf8
```

## 5. Understanding warnings

Warnings go to stderr and start with `Warning:`. The result is written anyway.

**Font warnings** (with `trs2tra` and the conversion form):

```
Warning: Font 8 (agsfnt8.ttf, IsWasted): no glyph for ß (179×), “ (30×), „ (30×)
Warning: Font 5 (agsfnt5.ttf, BBSesque): ä ö ü Ä Ö Ü look identical to a o u A O U (accents or umlaut dots missing in the font)
Warning: Not every font is used for translated text; fonts that only show e.g. symbols or titles can be ignored.
```

- *no glyph for* – the character is missing from this font; the number says how often it appears in
  the translation. If the font is used for translated text, the spot stays empty in the game.
- *look identical to* – the character in the font is just a copy of the base letter (umlaut without
  dots).
- For 8-bit TRAs the code is shown too (`„ (code 132, 30×)`): the font must have the character at
  this code.
- Fonts without the matching base letter (e.g. capitals-only fonts) are not reported.
- Remedy: add the character with the font editor or avoid it in the translation.

**Other warnings**

| Warning | Meaning |
|---|---|
| `N text(s) contain characters that do not exist in codepage 1252 and were stored as '?' ...` | The 8-bit TRA/TRS can't store some characters (e.g. Polish letters in 1252); they became `?` → `--utf8` |
| `... converts the keys with a Windows locale ...` | UTF-8 TRA for an 8-bit game: only runs on Windows → `--8bit` if all characters fit into the codepage ([chapter 4](#4-encoding---8bit-and---utf8)) |
| `UTF-8 translations need AGS 3.6 or later ...` | Game too old for UTF-8 → `--8bit` |
| `The file uses backslashes as plain characters ...` | Old TRS (before AGS 3.5.1) in which `\` is a normal character; read correctly, written as `\\` when saving |
| `N translation(s) contain garbled characters ...` | Mojibake like `ï¿½` or `Ã¼` from a wrong conversion → fix it in the TRS |
| `The game fonts could not be checked: ...` | Fonts not readable; the TRA was created anyway |

## 6. Exit codes and error messages

| Code | Meaning |
|---|---|
| `0` | Success (also with warnings; for `check`: no errors found) |
| `1` | Wrong usage – the help was shown |
| `2` | Error: file missing, not an AGS game, damaged file, write error; for `check`: placeholder errors in the translation |

Examples:

```
$ AGSTranslate info /wrong/game.exe
Error: Game file not found.                        (code 2)
$ AGSTranslate info README.md
Error: No AGS game data found in 'README.md'.      (code 2)
```

TRS, TRA and exported font files are written **atomically**: on an error an existing target file stays unchanged.

## 7. The TRS format

A TRS is a text file with a header followed by pairs of original and translation lines:

```
// AGS TRANSLATION SOURCE FILE
// ...
//#GameID=156899796
//#GameName=Unavowed
//#NormalFont=DEFAULT
//#SpeechFont=DEFAULT
//#TextDirection=DEFAULT
//#Encoding=UTF-8
//#Language=
//#AutoTranslateParserSaid=OFF
// ...
&179 Are you demon, man or woman?
&179 Bist du ein Dämon, Mann oder Frau?
Where am I?
Wo bin ich?
Open the door
                                        ← empty = not translated
```

Rules:

- The line after an original line is **always** its translation – even if it is empty or starts with
  `//`. Don't insert or delete lines, otherwise everything shifts.
- **Never change original lines**, not even trailing spaces.
- `&N ` at the start = voice line number N. Keep it in the translation.
- `[` = line break in the game, `%s`/`%d` = placeholders: keep them in a suitable place.

Header options:

| Option | Values | Effect |
|---|---|---|
| `GameID`, `GameName` | set by the tool | game identification; `trs2tra` takes them from the game |
| `NormalFont`, `SpeechFont` | `DEFAULT` or a font number | use other fonts for this language (e.g. a font with umlauts) |
| `TextDirection` | `DEFAULT`, `LEFT`, `RIGHT` | writing direction (RIGHT for Hebrew/Arabic) |
| `Encoding` | `ASCII` (8-bit) or `UTF-8` | encoding of TRS and TRA |
| `Language` | e.g. `de`, `de_DE` | language code for scripts |
| `AutoTranslateParserSaid` | `ON`/`OFF` | only for text parser games |

`NormalFont`/`SpeechFont` are a way out when a font has no umlauts but another font of the game in a
suitable size does (numbers see `fonts`).

## 8. Typical workflows

### New translation

```bash
AGSTranslate info  Game/Game.exe                            # check version and text encoding
AGSTranslate extract Game/Game.exe work/German.trs          # TRS in the game's encoding
# ... translate work/German.trs (translator GUI or text editor) ...
AGSTranslate trs2tra Game/Game.exe work/German.trs work/German.tra
cp work/German.tra Game/                                    # choose the language in the game
```

### After a game update

```bash
cp work/German.trs work/German.before-update.trs            # backup
AGSTranslate merge Game/Game.exe work/German.before-update.trs work/German.trs
# translate new lines ("untranslated"), check "obsolete" at the end of the file
AGSTranslate trs2tra Game/Game.exe work/German.trs work/German.tra
```

### Only an old TRA left

```bash
AGSTranslate merge Game/Game.exe Game/German.tra work/German.trs
```

Result: a complete TRS with all texts of the game, with the old translations filled in.

### Checking fonts

```bash
AGSTranslate fonts Game/Game.exe                        # which fonts, from where, which size
AGSTranslate trs2tra Game/Game.exe work/German.trs /tmp/test.tra   # read the font warnings
AGSTranslate fonts Game/Game.exe work/fonts             # backup/export for editing
```

## 9. Scripts

### Bash: build the TRA, copy it into the game only on success

```bash
#!/usr/bin/env bash
set -euo pipefail
GAME="$HOME/Games/Unavowed/Unavowed.exe"
TRS="work/German.trs"
OUT="work/German.tra"

AGSTranslate trs2tra "$GAME" "$TRS" "$OUT" 2> work/warnings.txt
cat work/warnings.txt
cp "$OUT" "$(dirname "$GAME")/"
echo "Installed. Warnings: $(grep -c '^Warning:' work/warnings.txt || true)"
```

`set -e` stops at exit code 2 before copying.

### PowerShell (Windows)

```powershell
$game = "C:\Games\Unavowed\Unavowed.exe"
.\AGSTranslate.exe trs2tra $game work\German.trs work\German.tra 2> work\warnings.txt
if ($LASTEXITCODE -ne 0) { Get-Content work\warnings.txt; exit $LASTEXITCODE }
Copy-Item work\German.tra (Split-Path $game)
```

### Several languages at once

```bash
for lang in German French Spanish; do
  AGSTranslate trs2tra "$GAME" "work/$lang.trs" "work/$lang.tra" || exit 1
done
```

## 10. FAQ

**The game doesn't show the translation.**
TRA not in the game folder, language not selected, or the TRA was created for another game version
(game ID). Check the ID with `info` and recreate the TRA with the current game file.

**Single lines stay English.**
Not translated yet, or the original has changed → `merge`.

**Umlauts are missing.**
Read the font warnings of `trs2tra`; it is almost always a font problem (see the font editor).

**Everything stays English on Linux/macOS, it works on Windows.**
UTF-8 TRA for a game with 8-bit texts → recreate it with `--8bit` ([chapter 4](#4-encoding---8bit-and---utf8)).

**„ “ – … are missing, umlauts work.**
8-bit TRA: the font needs the characters at codes 0x84/0x93/0x96/0x85 (font editor), or use `"`
and `-`. A UTF-8 TRA solves it too, but for 8-bit games it only runs on Windows.

**My old translation has `$`, `=` or `\` instead of umlauts.**
That used to be the usual trick: a custom font draws umlauts at these positions.
Better: convert the substitute characters into real umlauts and copy the umlaut characters in the font
to their real codes (ä = 0xE4 etc.) – then fonts and texts added later work too.

**Can I edit the TRS with a normal text editor?**
Yes. Keep the encoding when saving (UTF-8 for `//#Encoding=UTF-8`) and don't insert or delete
lines.

**Does this work with other games?**
Yes, with every AGS game; best with AGS 3.x. See the [user guide, chapter 8](UserGuide.md#8-other-games).

# AGS Translation Editor – Entwicklungsnotizen

Dieses Dokument fasst alle gesammelten technischen Erkenntnisse, durchgeführten Verbesserungen und offene Punkte zusammen.

---

## Was bisher erreicht wurde

### Phase 1 – Code-Qualität
- `Encoding.UTF7` (deprecated, unsicher) → `Encoding.Latin1` ersetzt
- `Encoding.Default` (plattformabhängig) → `Encoding.Latin1` ersetzt
- `BinaryReader` in `using`-Blöcke eingepackt (keine Resource-Leaks mehr)
- Statisches Feld `_transLines` in `Translation` entfernt → lokale Variable (kein State-Leak)
- `File.Contains(".tra")` → `Path.GetExtension()` ersetzt
- Dead Code in `AGSTranslate/Program.cs` entfernt
- Grundlegendes Fehler-Handling (try/catch) bei File-Operationen ergänzt

### Phase 2 – .NET 8 Migration
- Alle `.csproj` auf SDK-Style umgestellt
- `TargetFramework` von `net4.6.1` (Windows-only) auf `net8.0` (cross-platform)
- `packages.config` → `PackageReference`
- GitHub Actions CI/CD für automatischen Build auf Windows, Linux, macOS

### Phase 3 – Avalonia UI (Cross-Platform)
- Neues Projekt `AGS_TranslationEditor.Avalonia` mit MVVM-Architektur
- DataGrid-Ansicht mit Quelltext + Übersetzungs-Spalte
- VS Code-artiges dunkles Theme
- Filter/Suche nach Einträgen
- „Nächster unübersetzter" Button
- Datei öffnen (`.ags`, `.exe`, `.bin`, `game28.dta`)
- Export als `.tra` (binäres AGS-Format)
- TRS-Extraktion aus dem Spiel
- Settings-Dialog mit persistenter Fenstergröße/-position
- Tastaturkürzel (Strg+O, Strg+S, F3 etc.)
- Doppelklick auf Zeile öffnet Bearbeitungsfeld

### AGS-Dateiformat-Unterstützung (AGSTools, strukturell)
Alle Formate werden als saubere C#-Portierung der Strukturen aus den AGS-Quellen gelesen
(keine Code-Übernahme; AGS steht unter Artistic License 2.0, dieses Projekt unter GPL).

| Bereich | Klasse | AGS-Referenz |
|---|---|---|
| CLIB-Container v6/10/11/15/20/21/30, Exe mit angehängten Daten, Engine-Exe + `.ags` daneben | `Assets/AssetLibrary`, `Assets/GameFileSource` | `Common/data/multifilelib.cpp` |
| game28.dta (ab 3.0): Titel, UID, Codepage, Charaktere, Inventar, globale Nachrichten, Dialoge, GUI-Texte, Parser-Wörter, Scripts, Erweiterungen `v361_objnames`, `v363_dialogsnew`, `v363_gameinfo` | `GameData/GameDataReader`, `GameData/GuiReader` | `Common/game/main_game_file.cpp`, `Common/gui/*` |
| Räume (ab Format 3.0): Hotspot-/Objektnamen, Nachrichten, Raumscript | `GameData/RoomReader` | `Common/game/room_file.cpp` |
| SCOM-Scripts inkl. Voice-Paar-Erkennung | `GameData/CompiledScript` | `Common/script/cc_script.cpp` |
| TRA lesen/schreiben (blockbasiert, TextOpts, `ext_sopts`, UTF-8) | `Translations/TraFile` | `Common/data/tra_file.cpp` |
| TRS lesen/schreiben (`//#`-Optionen, Standard-Header wie der AGS-Editor) | `Translations/TrsFile` | `Tools/data/tra_utils.cpp`, `Editor/AGS.Types/Translation.cs` |
| Escape-Sequenzen TRS ↔ Laufzeit | `Translations/TextEscaping` | `trac`: `Unescape(PreprocessLineForOldStyleLinebreaks())` |
| AGS 2.x | `Extraction/LegacyExtraction` (heuristischer Fallback) | – |
| Übersetzung auf neuen Spielstand bringen (GUI „Tools → Update from Game“, CLI `merge`) | `Translations/TranslationMerger` | – |

Formatfehler werfen `AgsFormatException` (eine `IOException`) mit Abschnitt und Offset.
Der Game-Data-Reader liefert bei Fehlern nach dem Header Teilergebnisse plus `GameData.Error`;
Erweiterungen werden über den gespeicherten `ExtensionOffset` trotzdem gelesen.

Die alten statischen APIs (`Translation.*`, `Extraction.ParseAGSFile`, `GameInfo.GetGameInfo`)
bleiben als dünne Wrapper erhalten. Texte in `TranslationDocument.Entries` sind immer in TRS-Form
(mit Escape-Sequenzen); erst beim Schreiben einer TRA werden sie wie von `trac` aufgelöst.

---

## Technische Erkenntnisse (AGS Internals)

### Textkodierung
- `options[49]` (OPT_GAMETEXTENCODING) im Spiel: 65001 = UTF-8, sonst Windows-Codepage (Unavowed: **1252**), 0 = Standard.
- 8-Bit-Texte werden mit der **Codepage des Spiels** gelesen und geschrieben (Standard 1252), nicht Latin-1:
  „ “ – … ’ existieren nur in 1252. Alle verwendeten Codepages sind byte-verlustfrei (Roundtrip getestet).
- Der AGS-Editor schreibt 8-Bit-TRS mit `Encoding.Default` (System-ANSI).
- TRA: `ext_sopts["encoding"] = "UTF-8"` schaltet die Engine auf UTF-8. Bei UTF-8-TRA in einem ANSI-Spiel
  konvertiert die Engine (ab 3.6) die Schlüssel selbst in die Spiel-Codepage („Mixed encoding support“
  in `Engine/ac/translation.cpp`) – per `setlocale(LC_CTYPE, ".1252")` + `wcstombs`
  (`StrUtil::ConvertUtf8ToAscii`). **Diese Locale gibt es nur unter Windows**: Unter Linux/macOS schlägt
  `setlocale` fehl, alle Schlüssel werden leer, nichts wird übersetzt (getestet: Blackwell Legacy, native
  Linux-Engine 3.6.1; dieselbe Übersetzung als 8-Bit-TRA funktioniert). Unavowed lief unter Wine, daher
  fiel es dort nicht auf. `EncodingAdvice` warnt; für 8-Bit-Spiele ist 8-Bit-TRA die Standardempfehlung.
- TRS vor AGS 3.5.1 wurden wörtlich kompiliert, neuere Compiler lösen Escapes auf. Alte TRS mit `\` als
  normalem Zeichen (z.B. als Umlaut-Ersatz: `f\rchte`) erkennt `TrsFile` an ungültigen Escapes (`\s`) und
  nimmt dann alle Backslashes wörtlich.

### Umlaute und Fonts (im Spiel getestet, Unavowed / Engine 3.6.1)
- Die Engine zeichnet ein 8-Bit-Zeichen über seinen Code: TTF über die cmap (0xE4 → U+00E4 „ä“),
  WFN über den Index 228. Umlaute erscheinen nur, wenn der Font dort eine Glyphe hat.
- Lose `agsfntN.*`-Dateien im Spielordner haben Vorrang vor denen in der `.ags` (`kAssetPriorityDir`).
- Fehlt der Umlaut im Spiel, ist das ein Font-Problem, keine Frage der TRA-Kodierung: alte 8-Bit-TRA
  und neue UTF-8-TRA zeigen identisch „Damon“.
- Unavowed: Der Sprechfont **BBSesque (agsfnt5/agsfnt16)** hat zwar Glyphen für äöüÄÖÜ, aber ohne Punkte
  (Kopien von a/o/u/A/O/U). Mit Punkten nachgepatcht (64er-Pixelraster, 1 px über x-Höhe bzw. Versalhöhe) wird
  „Dämon“ korrekt angezeigt. Die angepassten Fonts im Spielordner (0, 8, 9, 12, 13, 17, WFN 1/2) decken das nicht ab.
- Die angepassten WFN-Fonts haben für ungenutzte Zeichen 128–255 Offset 0; Engine 3.6.1 meldet dafür
  „font has mistakes in data format“ (harmlos, die Zeichen bleiben leer). 3.6.1 meldet das auch für leere
  0x0-Glyphen; ab 3.6.2 ist beides erlaubt.
- **Zeichencode je nach TRA-Kodierung** (`Engine/ac/translation.cpp` schaltet den Textmodus um, alfont
  holt die Glyphe per `FT_Get_Char_Index(code)`): UTF-8-TRA → Unicode-Codepoint („ = U+201E);
  8-Bit-TRA → Byte der Codepage („ = 0x84 → TTF bräuchte U+0084, das hat praktisch kein Font).
  Für „ “ – … in ANSI-Spielen: entweder Glyphen an 0x84/0x93/0x96/0x85 im Font anlegen (8-Bit-TRA, läuft
  überall) oder UTF-8-TRA (nur Windows-Engine, s.o.).
- WFN-Fonts sind nach dem Code indiziert (bis 65535): Für eine UTF-8-TRA kann man die Offset-Tabelle bis
  z.B. U+2026 verlängern, dann zeigt auch ein WFN „ “ – … an.
- `FontCoverage` (beim TRA-Export) prüft genau das und meldet fehlende Glyphen sowie Umlaute, die nur
  Kopien des Grundbuchstabens sind.
- Unavowed: Die angepassten Umlaute in Font 12 (iso8), 13 (KarenBook) und 17 (Conformity) waren defekt
  (Grundbuchstabe verschoben, riesige Punkte weit über der Oberlänge → Font-Extent −4/−2/−4). Neu gebaut
  aus Grundbuchstabe + i-Punkt; ß, „ “ – … ergänzt (Pixel-Art bzw. Komponenten aus Komma/Punkt/Bindestrich).
  Im Spiel geprüft.

### Font-Editor (AGSFontEditor)
- `AGSTools/Fonts`: `WfnFont`, `TtfFont` (lesen), `TtfEditor` (Glyphen ersetzen/hinzufügen, schreibt
  glyf/loca/hmtx/hhea/maxp, cmap neu als Format 4/12/6, post, Prüfsummen), `TtfPixelGrid` (Pixelraster
  erkennen, Glyphen rastern und als Rechteck-Konturen zurückschreiben), `GameFonts` (Laden wie die Engine).
- `AGSFontEditor` (Avalonia-Bibliothek, Fenster `FontEditorWindow`), `AGSFontEditor.App` (eigenständig),
  im Translator *Tools → Font Editor...*; CLI `AGSTranslate fonts <spiel> [ordner]`.
- TTF-Darstellung über SkiaSharp ohne Kantenglättung (wie die Engine bei Pixelfonts).
- Benutzeranleitung: `docs/Anleitung.md`.

### TRA-Binärformat
```
Signatur:  "AGSTranslation\0" (15 Bytes)
Blöcke:    int32 id; id 0 → char[16] name + int64 length, sonst int32 length; id -1 = Ende
  2 GameID:   int32 uid + encrypted string (len inkl. Null)
  1 Dict:     Paare [int32 len + encrypted bytes], Abschluss: leeres Paar
  3 TextOpts: NormalFont, SpeechFont, RightToLeft (-1 Default, 1 LTR, 2 RTL), OptFlags (seit 3.6.3.10;
              wird nur geschrieben, wenn ein Flag gesetzt ist – ältere Engines lesen 12 Bytes und warnen sonst)
  ext_sopts:  int32 count + Paare prefixed strings ("encoding", "gameencoding", "language")
Verschlüsselung: byte += "Avis Durgan"[i % 11], inkl. Null-Terminator.
Entschlüsselung: die Engine prüft das *entschlüsselte* Byte auf 0 (sonst bricht "¿" am Anfang ab).
```
- Die Engine lehnt eine TRA ab, wenn uid ≠ 0 und ungleich der Spiel-UID ist, oder der Name nicht leer und ungleich ist.
- Alte Dateien dieses Tools (und Editoren vor 3.0) haben eine falsche GameID-Blocklänge (22 statt 17) – der Leser toleriert das.
- `trac` lässt Einträge mit leerer oder nur aus Leerzeichen bestehender Übersetzung weg.

### TRS-Format
- Zeile nach einer Quellzeile ist **immer** deren Übersetzung, auch wenn sie mit `//` beginnt (wie `trac`).
- Leerzeilen im Header (manche Tools schreiben welche, z.B. `Unavowed/Deutsch.trs`) würden bei `trac`
  alle folgenden Einträge um eine Zeile verschieben – unser Leser ignoriert Leerzeilen vor dem ersten Eintrag.
- Texte, die mit `//` beginnen, sind nicht speicherbar; der AGS-Editor lässt sie weg, wir auch.
- Weder Editor noch `trac` trimmen: Dialogoptionen mit Leerzeichen am Ende brauchen das Leerzeichen im Schlüssel.

### Merge (TranslationMerger)
- Ergebnis = Texte des Spiels in Spielreihenfolge; Übersetzung exakt per Quelltext, sonst per normalisiertem
  Quelltext (ohne `&N `-Präfix, getrimmt). Das Voice-Präfix der Übersetzung folgt dem neuen Quelltext.
- Übersetzer-Einstellungen (Encoding, Fonts, Sprache) bleiben, Spiel-ID/-Name kommen aus dem Spiel.
- Übersetzte Einträge, die es im Spiel nicht mehr gibt, bleiben standardmäßig am Ende erhalten (die Engine ignoriert sie).
- Unavowed + `Deutsch.trs`: 5.724 exakt, 164 ähnlich, keine verlorene Übersetzung.

### SCOM-Bytecode-Format
```
Header:   "SCOM" (4) + version int32 + globaldata_size + code_size + stringssize
Layout:   [globaldata][code (codeSize × 4 Bytes)][strings][fixups][imports][exports][sections]

Fixup-Format (WICHTIG – zwei getrennte Arrays):
  int32(numFixups)
  numFixups × BYTE   (Typ-Array)
  numFixups × int32  (Adress-Array)
  
Fixup-Typen:
  2 = Funktion
  3 = String-Referenz (Code-Wort enthält String-Tabellen-Offset)
  4 = Import
```

### AGS VM Opcodes (relevant)
| Opcode | Name        | Args | Bedeutung                            |
|--------|-------------|------|--------------------------------------|
| 6      | LITTOREG    | 2    | Lade Literal in Register (reg, val)  |
| 29     | PUSHREG     | 1    | Push Register auf Stack              |
| 34     | PUSHREAL    | 1    | Push Real-Value                      |
| 64     | CREATESTRING| 1    | Erstelle String-Objekt               |

Register AX = 3

### Voice-Prefix-System (`&N text`)
Blackwell Epiphany (und möglicherweise andere Wadjet Eye Games) verwenden eine benutzerdefinierte `Character::GSay`-Wrapper-Funktion, die **zur Laufzeit** den String `"&N text"` mit `String.Format("&%d %s", voiceNum, text)` zusammensetzt, bevor `Character::Say()` aufgerufen wird.

Deshalb sucht die AGS-Engine in der TRA nach `"&313 Look at him..."`, nicht nach `"Look at him..."`.

**Lösung**: `CompiledScript.FindVoicedStrings()` scannt alle Scripts (global, Module, Räume) nach dem Muster:
```
LITTOREG AX = voiceNum   (kein Fixup → einfache Zahl)
PUSHREG/PUSHREAL AX
LITTOREG AX = STR[offset] (Fixup-Typ 3 → String-Referenz)
[CREATESTRING AX]         (optional)
PUSHREG/PUSHREAL AX
```
Daraus wird ein Dictionary `text → voiceNum` erstellt. Beim TRS-Export werden Strings, für die ein `voiceNum` gefunden wurde, als `"&N text"` eingetragen.

**Guard**: Strings, die bereits mit `&[Ziffer]` beginnen (z.B. Unavowed), werden nicht doppelt geprefixed.

**Ergebnis**: 7.256 Voice-annotierte Zeilen für Blackwell Epiphany. Unavowed hat 0 Doppel-Prefixe.

### Dialog-Optionen (game28.dta)
Pro Dialog 4696 Bytes: 30 × 150 Bytes Optionstext (null-terminiert), 30 int32 Flags, int32, 30 int16 Entry-Points,
int16 Start, uint16 Codegröße, int32 Optionsanzahl, int32 Flags. Ab 3.6.3 ersetzt die Erweiterung
`v363_dialogsnew` diese Daten (ohne Längenlimit). Vor 3.1.1 folgen kompilierter Dialogcode, verschlüsselte
Dialogquellen und die Dialogzeilen bis zur GUI-Signatur `0xCAFEBEEF`.

### Live-Hook (AGSLive/agslive.c)
- Alle angezeigten Texte laufen durch `const char *get_translation(const char *)` (`Engine/ac/global_translation.cpp`).
  Die Funktion setzt nebenbei `source_text_length` (Anzeigedauer von Sprechtext), deshalb ruft der Hook das
  Original immer auf und tauscht nur das Ergebnis.
- Die Linux-Engines von Wadjet Eye (z.B. Blackwell, 3.6.1.35) sind nicht-PIE und exportieren ihre Symbole
  (für Plugins); `dlsym(RTLD_DEFAULT, "_Z15get_translationPKc")` findet die Funktion. `LD_PRELOAD` allein
  reicht nicht, weil Aufrufe innerhalb der Programmdatei nicht über die PLT laufen: der Hook überschreibt den
  Funktionsanfang mit `jmp [rip+0]` (14 Bytes) und verschiebt die überschriebenen Befehle in ein Trampolin.
  Erlaubt sind nur einfache Prolog-Befehle ohne RIP-relative Operanden (in Blackwell: `push rbp`, `push rbx`, `mov rbx,rdi`,
  `sub rsp,0x28`, `mov rax,fs:[0x28]`).
- Manche Texte laufen zweimal durch die Übersetzung (z.B. Hinweisboxen: erst Original, dann die Übersetzung);
  der Translator ignoriert Texte, die nicht als Schlüssel, aber als Übersetzung vorkommen.
- Die Engine hat auch einen Plugin-Hook `kPluginEvt_TranslateText`, Plugins lädt sie aber nur, wenn das Spiel sie
  einträgt.
- Windows: Engine steckt ohne Symbole in der EXE; dort bräuchte es Signatursuche je Engine-Version und eine
  Proxy-DLL.

---

## Bekannte Punkte / Mögliche Verbesserungen
- WinForms-Altprojekt (`AGS_TranslationEditor/`) baut nicht (`frmMain.Designer1.cs` ist beschädigt, unabhängig von AGSTools).
- Benutzerdefinierte Text-Properties werden nicht extrahiert (das Translated-Flag steht nicht in den Spieldaten).
- Räume vor Format 3.0 und Spieldaten vor 3.0 nur heuristisch.
- AppImage / .deb Packaging, README mit Screenshots, PO-Header-Sprache als Parameter.

---

## Unterstützte Spiele (getestet)

| Spiel              | Publisher    | Status                                                      |
|--------------------|-------------|-------------------------------------------------------------|
| Blackwell Epiphany | Wadjet Eye  | ✅ TRA/TRS-Testdaten in `AGSTools.Tests`                     |
| Unavowed (3.6.1.35)| Wadjet Eye  | ✅ Integrationstests (`UnavowedTests`, übersprungen ohne Spieldateien): alle 125 Räume, 17.437 Texte in ~0,2 s / ~115 MB (vorher 25 s / 1,7 GB), jeder übersetzte Text aus `Deutsch.trs` wird gefunden |
| Brick (Demo)       | -           | ✅ Einfache Strings übersetzen funktioniert                  |

---

## Commit-Historie (Zusammenfassung)

| Commit    | Beschreibung                                                          |
|-----------|----------------------------------------------------------------------|
| `c223fa6` | Fix dialog translation: extract `&N text` voice-prefixed keys from SCOM bytecode |
| `882addb` | Fix: preserve &N voice prefix when exporting TRA                    |
| `5d09a24` | Fix ExportAsTRA: use file picker instead of folder picker            |
| `a660e57` | Fix file picker: show *.ags, *.bin, game28.dta instead of only *.exe |
| `14380fb` | Code quality: BinaryReader using, encoding, static state, dead code cleanup |
| `9096345` | UI: Save as TRA export + async file loading with spinner             |
| `c1cde1f` | UI: Settings dialog + persist window layout                          |
| `1a413d3` | UI: Auto-translate button + error handling improvements              |
| `93ffa17` | UX improvements: shortcuts, row numbers, dbl-click, validation + CI fix |
| `e8da209` | UI redesign: VS Code-style dark theme                                |
| `892f3fb` | UI redesign: dark mode, cleaner toolbar, improved layout             |
| `b5e90e8` | Avalonia UI: filter, next-untranslated, Latin-1 save, net9.0 fix    |
| `15c10eb` | Fix encoding (Latin-1) and add CLIB v6/v10/v20/v21 support          |
| `51a0a08` | Fix TRA blockSize: calculate dynamically instead of hardcoded 22    |
| `5b99930` | Extract room hotspot/object names from .crm files                   |
| `92b0cb8` | Fix extraction quality: reject null-byte strings, tighten length-prefix scanner |
| `0a40c33` | Improve AGS text extraction: fix SCOMZ parser + add game-data section extraction |
| `ba6298e` | Phase 3: Avalonia cross-platform UI + GitHub Actions CI/CD          |
| `8f09695` | Phase 1+2: Code quality fixes and .NET 8 SDK-style migration        |

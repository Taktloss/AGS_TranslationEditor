# AGSTranslate – Kommandozeile

`AGSTranslate` kann alles, was man für eine AGS-Übersetzung braucht, ohne grafische Oberfläche:
Spiel prüfen, Texte herausholen, Übersetzung aktualisieren, TRA erstellen und zurückwandeln, Fonts
auflisten und exportieren. Damit lassen sich auch wiederholbare Abläufe in Skripten bauen.

Die grafischen Programme (Translator und Font-Editor) beschreibt die [Anleitung](Anleitung.md).

## Inhalt

1. [Installation und Aufruf](#1-installation-und-aufruf)
2. [Überblick](#2-überblick)
3. [Befehle](#3-befehle)
   - [info](#info--spiel-prüfen)
   - [extract](#extract--texte-aus-dem-spiel-holen)
   - [merge](#merge--übersetzung-auf-den-aktuellen-spielstand-bringen)
   - [trs2tra](#trs2tra--tra-für-das-spiel-erstellen)
   - [tra2trs](#tra2trs--tra-zurück-in-eine-trs)
   - [fonts](#fonts--fonts-auflisten-und-exportieren)
   - [check](#check--übersetzung-prüfen)
   - [Umwandlung aus CSV/TSV/PO](#umwandlung-aus-csvtsvpo-alte-form)
4. [Kodierung: --8bit und --utf8](#4-kodierung---8bit-und---utf8)
5. [Warnungen verstehen](#5-warnungen-verstehen)
6. [Rückgabewerte und Fehlermeldungen](#6-rückgabewerte-und-fehlermeldungen)
7. [Das TRS-Format](#7-das-trs-format)
8. [Typische Abläufe](#8-typische-abläufe)
9. [Skripte](#9-skripte)
10. [Häufige Fragen](#10-häufige-fragen)

---

## 1. Installation und Aufruf

### Aus dem Quellcode starten

Benötigt das [.NET SDK](https://dotnet.microsoft.com/download) (9 oder neuer):

```bash
dotnet run --project AGSTranslate -- info "Unavowed/Unavowed.exe"
```

Alles nach `--` geht an AGSTranslate. Das ist bequem zum Ausprobieren, baut aber bei jedem Aufruf
neu. Für häufige Nutzung besser einmal bauen:

```bash
dotnet build AGSTranslate -c Release
dotnet AGSTranslate/bin/Release/net9.0/AGSTranslate.dll info "Unavowed/Unavowed.exe"
```

### Als einzelne Programmdatei

Mit `dotnet publish` entsteht eine einzelne Datei, die ohne installiertes .NET läuft:

```bash
# Linux
dotnet publish AGSTranslate/AGSTranslate.csproj -c Release -r linux-x64 \
    --self-contained -p:PublishSingleFile=true -o publish/linux
# Windows
dotnet publish AGSTranslate/AGSTranslate.csproj -c Release -r win-x64 \
    --self-contained -p:PublishSingleFile=true -o publish/windows
```

Danach `publish/linux/AGSTranslate` bzw. `publish\windows\AGSTranslate.exe` direkt aufrufen (rund
70 MB, weil die .NET-Laufzeit enthalten ist). Ohne `--self-contained` wird die Datei klein, braucht
aber ein installiertes .NET.

In den Beispielen unten steht einfach `AGSTranslate`.

### Spieldatei angeben

Überall, wo `<spiel>` steht, kann man angeben:

- die **Spiel-EXE** (Spieldaten an die EXE angehängt, oder eine EXE, neben der eine `.ags` liegt –
  typisch für Steam/GOG),
- die **`.ags`-Datei**,
- eine lose **`game28.dta`** (bzw. `ac2game.dta` bei sehr alten Spielen).

Pfade mit Leerzeichen in Anführungszeichen setzen.

## 2. Überblick

```
AGSTranslate info <spiel>                        Titel, ID, AGS-Version, Kodierung
AGSTranslate extract <spiel> [aus.trs]           Alle Texte des Spiels in eine TRS
AGSTranslate merge <spiel> <alt.trs|tra> <aus.trs>
                                                 Übersetzung auf den aktuellen Spielstand bringen
AGSTranslate trs2tra <spiel> <ein.trs> <aus.tra> TRA für das Spiel erstellen (mit Font-Prüfung)
AGSTranslate tra2trs <ein.tra> <aus.trs> [spiel] TRA zurück in eine TRS
AGSTranslate fonts <spiel> [ordner]              Fonts auflisten; mit Ordner: exportieren
AGSTranslate check <spiel> <ein.trs|tra> [glossar.txt]
                                                 Übersetzung prüfen: Platzhalter, Einheitlichkeit,
                                                 Glossar, Textbreite in GUI-Buttons
AGSTranslate <spiel> <ein.csv|tsv|po|trs> <aus.tra>
                                                 Umwandlung in eine TRA (alte Form)

Optionen: --8bit  TRS/TRA in der 8-Bit-Codepage des Spiels schreiben (läuft überall)
          --utf8  TRS/TRA als UTF-8 schreiben (AGS 3.6+; bei 8-Bit-Spielen nur Windows)
```

Ohne Argumente (oder mit falschen) zeigt AGSTranslate diese Hilfe und beendet sich mit Code 1.

Ausgaben:
- normale Meldungen auf **stdout**,
- Fortschritt (`Reading rooms...`), Warnungen (`Warning: ...`) und Fehler (`Error: ...`) auf
  **stderr**. So kann man die Ausgabe umleiten, ohne Warnungen zu verlieren.

## 3. Befehle

### info – Spiel prüfen

```bash
AGSTranslate info <spiel>
```

Zeigt, um welches Spiel es sich handelt und wie es Texte speichert. Der erste Befehl bei einem
neuen Spiel.

```
$ AGSTranslate info Unavowed/Unavowed.exe
Title:       Unavowed
Game ID:     156899796
AGS version: 3.6.1.35
Encoding:    8-bit, codepage 1252
Game data:   /home/.../Unavowed/unavowed.ags
```

| Zeile | Bedeutung |
|---|---|
| Title / Game ID | Werden in jede TRA geschrieben. Das Spiel lädt nur TRAs mit passender ID. |
| AGS version | Engine-Version. **UTF-8-Übersetzungen erst ab 3.6.** |
| Encoding | Wie das Spiel selbst Texte speichert: `UTF-8` oder `8-bit, codepage N` (1252 = Westeuropa). Bestimmt, welche TRA-Kodierung passt ([Kapitel 4](#4-kodierung---8bit-und---utf8)). |
| Game data | Nur wenn die Daten in einer anderen Datei liegen als angegeben (z.B. EXE + `.ags`). |

### extract – Texte aus dem Spiel holen

```bash
AGSTranslate extract <spiel> [aus.trs] [--8bit|--utf8]
```

Liest alle übersetzbaren Texte aus den Spieldaten und schreibt eine TRS mit leeren Übersetzungen.
Ohne `aus.trs` wird die Datei neben der Spieldatei angelegt (`Unavowed.trs`) – meist will man aber
einen eigenen Arbeitsordner angeben.

```
$ AGSTranslate extract Unavowed/Unavowed.exe arbeit/unavowed.trs
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
17437 texts → arbeit/unavowed.trs
```

Gefunden werden:

| Quelle | Inhalt |
|---|---|
| Script | Texte aus den globalen Skripten und Dialogskripten (Sprechtexte, `Display`, ...) |
| DialogOption | Dialogoptionen (auch solche mit Leerzeichen am Ende) |
| Gui | Beschriftungen von Buttons, Labels, Listen in Menüs/GUIs |
| CharacterName / InventoryItem | Namen von Figuren und Gegenständen (in voller Länge) |
| Hotspot / RoomObject | Namen von Hotspots und Objekten in allen Räumen |
| RoomScript | Texte aus den Raumskripten |

Jeder Text kommt nur einmal vor. Texte ohne Buchstaben (`...`, `&12 ...`) werden ausgelassen,
ebenso Skript-Bezeichner und ähnliches, was kein Anzeigetext ist.

**Bei AGS 2.x** (sehr alte Spiele) kann das Tool die Daten nicht strukturiert lesen und sucht Texte
heuristisch. Das Ergebnis kann dann Lücken oder unsinnige Zeilen enthalten.

Die TRS bekommt die Kodierung des Spiels. Wie man sie bei Bedarf ändert, steht in
[Kapitel 4](#4-kodierung---8bit-und---utf8).

### merge – Übersetzung auf den aktuellen Spielstand bringen

```bash
AGSTranslate merge <spiel> <alt.trs|alt.tra> <aus.trs> [--8bit|--utf8]
```

Holt die aktuellen Texte aus dem Spiel und überträgt die vorhandenen Übersetzungen darauf. Das
braucht man:

- nach einem **Spiel-Update**,
- wenn eine **alte Übersetzung** (z.B. mit einem älteren Tool erstellt) nicht alle Texte enthält,
- wenn man nur noch eine **TRA** hat (sie wird direkt gelesen).

```
$ AGSTranslate merge Unavowed/Unavowed.exe Unavowed/Deutsch.trs arbeit/Deutsch.trs
17437 texts in the game
  unchanged:            5724
  similar (spaces/&N):   164
  untranslated:        11549
  obsolete (kept):         0
Done → arbeit/Deutsch.trs
```

| Zeile | Bedeutung |
|---|---|
| unchanged | Originaltext identisch, Übersetzung übernommen. |
| similar | Originaltext unterscheidet sich nur in Leerzeichen am Rand oder im Voice-Präfix `&N`. Übersetzung übernommen; das Präfix der Übersetzung wird an das neue Original angepasst (`&32 Raus damit.` → `&40 Raus damit.`). |
| untranslated | Texte ohne Übersetzung – neu im Spiel oder bisher nicht erfasst. |
| obsolete (kept) | Übersetzte Zeilen, deren Original es im Spiel nicht mehr gibt. Sie stehen am Ende der Datei (das Spiel ignoriert sie), damit nichts verloren geht. Bei Bedarf von Hand löschen. |

Die Ausgabedatei hat genau die Texte des Spiels in Spielreihenfolge. Die Einstellungen der alten
Übersetzung (Kodierung, Fonts, Sprache) bleiben erhalten, Spiel-ID und Titel kommen aus dem Spiel.

**Wichtig:** `aus.trs` nicht gleich `alt.trs` wählen, sondern eine neue Datei – so bleibt die alte
als Sicherung, bis man das Ergebnis geprüft hat.

### trs2tra – TRA für das Spiel erstellen

```bash
AGSTranslate trs2tra <spiel> <ein.trs> <aus.tra> [--8bit|--utf8]
```

Kompiliert die TRS in die TRA, die das Spiel lädt. Spiel-ID und Titel werden aus dem Spiel
übernommen. Danach prüft das Tool, ob die Fonts des Spiels alle Sonderzeichen der Übersetzung
darstellen können.

```
$ AGSTranslate trs2tra Unavowed/Unavowed.exe arbeit/Deutsch.trs arbeit/Deutsch.tra
Game: Unavowed  ID: 156899796  AGS 3.6.1.35
Entries in TRS: 17461 (translated: 5912)
Done → arbeit/Deutsch.tra (UTF-8)
```

- Nur übersetzte Zeilen kommen in die TRA; leere Übersetzungen zeigt das Spiel im Original.
- Die Kodierung (`UTF-8` oder `8-bit`) kommt aus der TRS (`//#Encoding=`) bzw. aus `--8bit`/`--utf8`.
- Der **Dateiname** ist der Name der Sprache im Spiel: `Deutsch.tra` → Sprache „Deutsch“.
- Die TRA in den Spielordner kopieren und die Sprache wählen (Setup des Spiels oder `acsetup.cfg`:
  `[language]` / `translation=Deutsch`).
- Warnungen siehe [Kapitel 5](#5-warnungen-verstehen).

### tra2trs – TRA zurück in eine TRS

```bash
AGSTranslate tra2trs <ein.tra> <aus.trs> [spiel] [--8bit|--utf8]
```

Wandelt eine kompilierte Übersetzung wieder in eine bearbeitbare TRS – z.B. wenn nur noch die TRA
vorhanden ist. Die TRS enthält nur die Zeilen, die in der TRA übersetzt sind; für eine vollständige
Liste danach `merge` verwenden (oder gleich `merge` mit der TRA aufrufen).

```
$ AGSTranslate tra2trs Unavowed/Deutsch.tra arbeit/alt.trs Unavowed/Unavowed.exe --utf8
Entries in TRA: 5748
Done → arbeit/alt.trs
```

Die Spieldatei ist optional, wird aber für **8-Bit-TRAs** empfohlen: die TRA speichert ihre
Codepage nicht, das Tool nimmt sonst 1252 an. Mit `--utf8` entsteht eine UTF-8-TRS, auch wenn die
TRA 8-Bit war (Umlaute werden dabei korrekt umgewandelt).

### fonts – Fonts auflisten und exportieren

```bash
AGSTranslate fonts <spiel> [ordner]
```

Listet alle Fonts so auf, wie die Engine sie lädt:

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

| Spalte | Bedeutung |
|---|---|
| # | Font-Nummer im Spiel |
| File | Dateiname (`agsfntN.ttf` oder `.wfn`; bei beiden gewinnt `.ttf`) |
| Kind | TTF (TrueType) oder WFN (Bitmap-Font) |
| Origin | `folder` = lose Datei im Spielordner (hat Vorrang), `package` = in der `.ags`/EXE |
| Size | Größe, in der der Font gezeichnet wird |
| Outline | `none`, `auto` (automatische Kontur) oder `font N` (Kontur mit einem anderen Font) |
| Chars | Anzahl der enthaltenen Zeichen |
| Name | Name des Fonts (nur TTF) |

Mit einem **Ordner** werden alle Font-Dateien **unverändert** dorthin kopiert:

```
$ AGSTranslate fonts Unavowed/Unavowed.exe arbeit/fonts
...
19 font files → arbeit/fonts
```

Der Spielordner ist als Ziel gesperrt (`Error: choose an output folder other than the game folder.`),
weil Dateien dort die Fonts des Spiels ersetzen würden. Vorhandene Dateien im Zielordner werden
überschrieben. Bearbeiten kann man die Fonts mit dem Font-Editor ([Anleitung](Anleitung.md#7-font-editor)).

### check – Übersetzung prüfen

```bash
AGSTranslate check <spiel> <übersetzung.trs|tra> [glossar.txt]
```

Prüft die Übersetzung gegen das Spiel und listet die Funde nach Art geordnet. Die Datei wird
nicht verändert.

```
$ AGSTranslate check Unavowed/unavowed.ags arbeit/Deutsch.trs glossar.txt
== Too wide (1) ==
WARNING: Skip to Origin
    Button "Button1" (font 0, 81×18 px): text is 93 px wide, 12 px too wide (TrueType: estimated)

== Inconsistent (2) ==
INFO: &15 What do you want to know?
    translated in 3 ways: "Was wollen Sie wissen?" (1×), "Was wollen sie wissen?" (1×), "Was willst du wissen?" (1×)
...
0 errors, 1 warnings, 2 notes
```

Jeder Fund zeigt die Stufe (`ERROR`, `WARNING`, `INFO`), den Originaltext (wie in der TRS) und
eine Erklärung. Was die einzelnen Arten bedeuten, wie genau die Breitenmessung ist und wie ein
Glossar aussieht, steht in der [Anleitung, Kapitel 14](Anleitung.md#14-übersetzung-prüfen).

Rückgabewert: `2`, wenn mindestens ein **Fehler** (falsche Platzhalter) gefunden wurde, sonst `0`.
So kann ein Skript den TRA-Bau abbrechen:

```bash
AGSTranslate check spiel.ags Deutsch.trs glossar.txt > pruefung.txt && \
AGSTranslate trs2tra spiel.ags Deutsch.trs Deutsch.tra
```

### Umwandlung aus CSV/TSV/PO (alte Form)

```bash
AGSTranslate <spiel> <ein.csv|ein.tsv|ein.po|ein.trs> <aus.tra> [--8bit|--utf8]
```

Erstellt eine TRA direkt aus einer Tabelle oder einer PO-Datei (z.B. aus einem Übersetzungsdienst):

- **CSV**: Spalte 1 Original, Spalte 2 Übersetzung, Trennzeichen `;`. Felder mit `;`,
  Anführungszeichen oder Zeilenumbrüchen in `"..."` (Anführungszeichen darin verdoppeln).
- **TSV**: wie CSV, Trennzeichen Tabulator.
- **PO**: `msgid` = Original, `msgstr` = Übersetzung.
- CSV/TSV/PO als UTF-8 speichern. Die TRA wird 8-Bit (bei UTF-8-Spielen UTF-8); `--utf8` erzwingt UTF-8.

## 4. Kodierung: --8bit und --utf8

`--8bit` und `--utf8` können an beliebiger Stelle stehen und gelten für `extract`, `merge`,
`tra2trs`, `trs2tra` und die Umwandlungsform. Sie legen die Kodierung der geschriebenen TRS/TRA
fest (`//#Encoding=ASCII` bzw. `//#Encoding=UTF-8`); die Texte werden dabei korrekt umgerechnet.
Ohne Option gilt die Kodierung der Eingabe bzw. des Spiels.

### Welche Kodierung?

`info` zeigt unter `Encoding`, wie das Spiel seine Texte speichert:

| Spiel | Übersetzung enthält | Kodierung |
|---|---|---|
| `UTF-8` | beliebige Zeichen | UTF-8 |
| `8-bit, codepage 1252` | nur Zeichen der Codepage (Deutsch, Französisch, Spanisch, Italienisch …) | **8-Bit** – läuft mit jeder Engine unter Windows, Linux und macOS |
| `8-bit, codepage 1252` | Zeichen außerhalb der Codepage (Polnisch, Russisch …) | UTF-8 – nur AGS 3.6+ und **nur unter Windows** |

**Warum nicht immer UTF-8?** Speichert das Spiel seine Texte 8-bittig, rechnet die Engine bei einer
UTF-8-TRA alle Originaltexte mit einer Windows-Funktion um (`setlocale(LC_CTYPE, ".1252")` in
`Engine/ac/translation.cpp`). Unter Linux und macOS gibt es diese Locale nicht: jeder Schlüssel wird
leer, und das Spiel bleibt komplett in der Originalsprache – ohne Fehlermeldung. Aufgefallen mit
Blackwell Legacy unter Linux: dieselbe Übersetzung als 8-Bit-TRA funktioniert. Das Tool warnt:

```
Warning: The game stores its texts as 8-bit, so for a UTF-8 translation the engine converts the keys with a
Windows locale (".1252"). The Linux and macOS engine cannot do that and shows the original texts. All characters
of this translation exist in codepage 1252: an 8-bit translation works everywhere.
```

**„ “ – … in einer 8-Bit-TRA:** Diese Zeichen gibt es in Codepage 1252 (Bytes 0x84, 0x93, 0x96,
0x85), aber die Engine sucht sie im Font unter genau diesen Codes – dort haben TrueType-Fonts kein
Zeichen. Entweder `"` und `-` verwenden oder die Zeichen im Font-Editor an diesen Codes anlegen
(*Go to* `0x84` → *Copy from* bzw. Zeichnen).

**Engines vor 3.6** kennen kein UTF-8:

```
Warning: UTF-8 translations need AGS 3.6 or later, but the game uses AGS 3.5.1.14; it will show garbled special characters. Use an 8-bit translation.
```

**Eine TRS umstellen:** nicht einfach die Kopfzeile ändern (die Datei bliebe in der alten Kodierung
gespeichert), sondern umschreiben lassen:

```bash
AGSTranslate merge <spiel> alt.trs neu.trs --8bit     # oder --utf8
```

## 5. Warnungen verstehen

Warnungen stehen auf stderr und beginnen mit `Warning:`. Das Ergebnis wird trotzdem geschrieben.

**Font-Warnungen** (bei `trs2tra` und der Umwandlungsform):

```
Warning: Font 8 (agsfnt8.ttf, IsWasted): no glyph for ß (179×), “ (30×), „ (30×)
Warning: Font 5 (agsfnt5.ttf, BBSesque): ä ö ü Ä Ö Ü look identical to a o u A O U (accents or umlaut dots missing in the font)
Warning: Not every font is used for translated text; fonts that only show e.g. symbols or titles can be ignored.
```

- *no glyph for* – das Zeichen fehlt in diesem Font; die Zahl sagt, wie oft es in der Übersetzung
  vorkommt. Wird der Font für übersetzten Text benutzt, bleibt die Stelle im Spiel leer.
- *look identical to* – das Zeichen ist im Font nur eine Kopie des Grundbuchstabens (Umlaut ohne
  Punkte).
- Bei 8-Bit-TRAs steht zusätzlich der Code dabei (`„ (code 132, 30×)`): unter diesem Code muss der
  Font das Zeichen haben.
- Fonts ohne den passenden Grundbuchstaben (z.B. reine Großbuchstaben-Fonts) werden nicht gemeldet.
- Abhilfe: Zeichen mit dem Font-Editor ergänzen oder das Zeichen in der Übersetzung vermeiden.

**Weitere Warnungen**

| Warnung | Bedeutung |
|---|---|
| `N text(s) contain characters that do not exist in codepage 1252 and were stored as '?' ...` | Die 8-Bit-TRA/TRS kann Zeichen nicht speichern (z.B. polnische Buchstaben in 1252); sie wurden zu `?` → `--utf8` |
| `... converts the keys with a Windows locale ...` | UTF-8-TRA für ein 8-Bit-Spiel: läuft nur unter Windows → `--8bit`, wenn alle Zeichen in die Codepage passen ([Kapitel 4](#4-kodierung---8bit-und---utf8)) |
| `UTF-8 translations need AGS 3.6 or later ...` | Spiel zu alt für UTF-8 → `--8bit` |
| `The file uses backslashes as plain characters ...` | Alte TRS (vor AGS 3.5.1), in der `\` ein normales Zeichen ist; wird richtig gelesen, beim Speichern als `\\` geschrieben |
| `N translation(s) contain garbled characters ...` | Zeichensalat wie `ï¿½` oder `Ã¼` aus einer falschen Umwandlung → in der TRS korrigieren |
| `The game fonts could not be checked: ...` | Fonts nicht lesbar; die TRA wurde trotzdem erstellt |

## 6. Rückgabewerte und Fehlermeldungen

| Code | Bedeutung |
|---|---|
| `0` | Erfolg (auch mit Warnungen; bei `check`: keine Fehler gefunden) |
| `1` | Falsche Benutzung – die Hilfe wurde angezeigt |
| `2` | Fehler: Datei fehlt, kein AGS-Spiel, beschädigte Datei, Schreibfehler; bei `check`: Platzhalterfehler in der Übersetzung |

Beispiele:

```
$ AGSTranslate info /falsch/spiel.exe
Error: Game file not found.                        (Code 2)
$ AGSTranslate info README.md
Error: No AGS game data found in 'README.md'.      (Code 2)
```

TRS-, TRA- und exportierte Font-Dateien werden **atomar** geschrieben: bei einem Fehler bleibt eine vorhandene Zieldatei unverändert.

## 7. Das TRS-Format

Eine TRS ist eine Textdatei mit einem Kopf und danach Paaren aus Original- und Übersetzungszeile:

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
                                        ← leer = nicht übersetzt
```

Regeln:

- Die Zeile nach einer Originalzeile ist **immer** deren Übersetzung – auch wenn sie leer ist oder
  mit `//` beginnt. Keine Zeilen einfügen oder löschen, sonst verschiebt sich alles.
- **Originalzeilen nie ändern**, auch keine Leerzeichen am Ende.
- `&N ` am Anfang = Sprachausgabe Nummer N. In der Übersetzung beibehalten.
- `[` = Zeilenumbruch im Spiel, `%s`/`%d` = Platzhalter: an passender Stelle beibehalten.

Kopfoptionen:

| Option | Werte | Wirkung |
|---|---|---|
| `GameID`, `GameName` | vom Tool gesetzt | Spielerkennung; `trs2tra` übernimmt sie aus dem Spiel |
| `NormalFont`, `SpeechFont` | `DEFAULT` oder Font-Nummer | Andere Fonts für diese Sprache verwenden (z.B. einen Font mit Umlauten) |
| `TextDirection` | `DEFAULT`, `LEFT`, `RIGHT` | Schreibrichtung (RIGHT für Hebräisch/Arabisch) |
| `Encoding` | `ASCII` (8-Bit) oder `UTF-8` | Kodierung von TRS und TRA |
| `Language` | z.B. `de`, `de_DE` | Sprachkennung für Skripte |
| `AutoTranslateParserSaid` | `ON`/`OFF` | Nur für Text-Parser-Spiele |

`NormalFont`/`SpeechFont` sind ein Ausweg, wenn ein Font keine Umlaute hat, aber ein anderer
Font des Spiels in passender Größe schon (Nummern siehe `fonts`).

## 8. Typische Abläufe

### Neue Übersetzung

```bash
AGSTranslate info  Spiel/Spiel.exe                          # Version und Textkodierung prüfen
AGSTranslate extract Spiel/Spiel.exe arbeit/Deutsch.trs     # TRS in der Kodierung des Spiels
# ... arbeit/Deutsch.trs übersetzen (Translator-GUI oder Texteditor) ...
AGSTranslate trs2tra Spiel/Spiel.exe arbeit/Deutsch.trs arbeit/Deutsch.tra
cp arbeit/Deutsch.tra Spiel/                                # Sprache im Spiel wählen
```

### Nach einem Spiel-Update

```bash
cp arbeit/Deutsch.trs arbeit/Deutsch.vor-update.trs          # Sicherung
AGSTranslate merge Spiel/Spiel.exe arbeit/Deutsch.vor-update.trs arbeit/Deutsch.trs
# neue Zeilen ("untranslated") übersetzen, "obsolete" am Dateiende prüfen
AGSTranslate trs2tra Spiel/Spiel.exe arbeit/Deutsch.trs arbeit/Deutsch.tra
```

### Nur eine alte TRA vorhanden

```bash
AGSTranslate merge Spiel/Spiel.exe Spiel/Deutsch.tra arbeit/Deutsch.trs
```

Ergebnis: vollständige TRS mit allen Texten des Spiels, die alten Übersetzungen eingetragen.

### Fonts prüfen

```bash
AGSTranslate fonts Spiel/Spiel.exe                      # welche Fonts, woher, welche Größe
AGSTranslate trs2tra Spiel/Spiel.exe arbeit/Deutsch.trs /tmp/test.tra   # Font-Warnungen lesen
AGSTranslate fonts Spiel/Spiel.exe arbeit/fonts         # Sicherung/Export zum Bearbeiten
```

## 9. Skripte

### Bash: TRA bauen, nur bei Erfolg ins Spiel kopieren

```bash
#!/usr/bin/env bash
set -euo pipefail
GAME="$HOME/Spiele/Unavowed/Unavowed.exe"
TRS="arbeit/Deutsch.trs"
OUT="arbeit/Deutsch.tra"

AGSTranslate trs2tra "$GAME" "$TRS" "$OUT" 2> arbeit/warnungen.txt
cat arbeit/warnungen.txt
cp "$OUT" "$(dirname "$GAME")/"
echo "Installiert. Warnungen: $(grep -c '^Warning:' arbeit/warnungen.txt || true)"
```

`set -e` bricht beim Rückgabewert 2 ab, bevor kopiert wird.

### PowerShell (Windows)

```powershell
$game = "C:\Spiele\Unavowed\Unavowed.exe"
.\AGSTranslate.exe trs2tra $game arbeit\Deutsch.trs arbeit\Deutsch.tra 2> arbeit\warnungen.txt
if ($LASTEXITCODE -ne 0) { Get-Content arbeit\warnungen.txt; exit $LASTEXITCODE }
Copy-Item arbeit\Deutsch.tra (Split-Path $game)
```

### Mehrere Sprachen auf einmal

```bash
for lang in Deutsch Francais Espanol; do
  AGSTranslate trs2tra "$GAME" "arbeit/$lang.trs" "arbeit/$lang.tra" || exit 1
done
```

## 10. Häufige Fragen

**Das Spiel zeigt die Übersetzung nicht an.**
TRA nicht im Spielordner, Sprache nicht ausgewählt, oder die TRA wurde für eine andere
Spielversion erstellt (Spiel-ID). Mit `info` die ID prüfen und die TRA mit der aktuellen Spieldatei
neu erstellen.

**Einzelne Zeilen bleiben englisch.**
Noch nicht übersetzt, oder das Original hat sich geändert → `merge`.

**Umlaute fehlen.**
Font-Warnungen von `trs2tra` lesen; das ist fast immer ein Font-Problem (siehe Font-Editor).

**Unter Linux/macOS bleibt alles englisch, unter Windows geht es.**
UTF-8-TRA für ein Spiel mit 8-Bit-Texten → mit `--8bit` neu erstellen ([Kapitel 4](#4-kodierung---8bit-und---utf8)).

**„ “ – … fehlen, Umlaute gehen.**
8-Bit-TRA: der Font braucht die Zeichen an den Codes 0x84/0x93/0x96/0x85 (Font-Editor), oder `"`
und `-` verwenden. Eine UTF-8-TRA löst es auch, läuft bei 8-Bit-Spielen aber nur unter Windows.

**Meine alte Übersetzung hat `$`, `=` oder `\` statt Umlauten.**
Das war früher der übliche Trick: ein selbst gebauter Font zeichnet an diesen Stellen Umlaute.
Besser: die Ersatzzeichen in echte Umlaute umwandeln und die Umlaut-Zeichen im Font an ihre echten
Codes (ä = 0xE4 usw.) kopieren – dann funktionieren auch Fonts und Texte, die später dazukommen.

**Kann ich die TRS mit einem normalen Texteditor bearbeiten?**
Ja. Beim Speichern die Kodierung beibehalten (UTF-8 bei `//#Encoding=UTF-8`) und keine Zeilen
einfügen oder löschen.

**Funktioniert das mit anderen Spielen?**
Ja, mit jedem AGS-Spiel; am besten mit AGS 3.x. Siehe [Anleitung, Kapitel 8](Anleitung.md#8-andere-spiele).

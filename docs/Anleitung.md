# AGS Translation Editor – Anleitung

Diese Anleitung beschreibt, wie man mit dem AGS Translation Editor eine Übersetzung für ein
Adventure-Game-Studio-Spiel erstellt, aktualisiert und ins Spiel bringt, wie man die Fonts des Spiels
prüft und bearbeitet und worauf man dabei achten muss.

## Inhalt

1. [Die Programme](#1-die-programme)
2. [Grundbegriffe](#2-grundbegriffe)
3. [Neue Übersetzung erstellen](#3-neue-übersetzung-erstellen)
4. [Übersetzung ins Spiel bringen](#4-übersetzung-ins-spiel-bringen)
5. [Übersetzung nach einem Spiel-Update aktualisieren](#5-übersetzung-nach-einem-spiel-update-aktualisieren)
6. [Umlaute, ß und Anführungszeichen](#6-umlaute-ß-und-anführungszeichen)
7. [Font-Editor](#7-font-editor)
8. [Andere Spiele](#8-andere-spiele)
9. [Worauf man achten muss (Checkliste)](#9-worauf-man-achten-muss-checkliste)
10. [Fehlerbehebung](#10-fehlerbehebung)
11. [Kommandozeile (AGSTranslate)](#11-kommandozeile-agstranslate)
12. [Spiele testen und Fehler suchen](#12-spiele-testen-und-fehler-suchen)
13. [Live-Modus: Übersetzung beim Spielen korrigieren (Linux)](#13-live-modus-übersetzung-beim-spielen-korrigieren-linux)
14. [Übersetzung prüfen](#14-übersetzung-prüfen)
15. [Installieren und an Spieler weitergeben](#15-installieren-und-an-spieler-weitergeben)
16. [Maschinelle Übersetzung (DeepL, Claude, Google)](#16-maschinelle-übersetzung-deepl-claude-google)

---

## 1. Die Programme

| Programm | Projekt | Wofür |
|---|---|---|
| **AGS Translation Editor** | `AGS_TranslationEditor.Avalonia` | Übersetzen (TRS bearbeiten), Texte aus dem Spiel holen, TRA erstellen, Übersetzung aktualisieren |
| **AGS Font Editor** | `AGSFontEditor.App` (auch im Translator unter *Tools → Font Editor...*) | Fonts eines Spiels ansehen, entpacken, Zeichen prüfen und bearbeiten |
| **AGSTranslate** | `AGSTranslate` | Dasselbe auf der Kommandozeile, z.B. für Skripte |

Starten aus dem Repository (benötigt das .NET SDK):

```bash
dotnet run --project AGS_TranslationEditor.Avalonia      # Translator
dotnet run --project AGSFontEditor.App -- [Spiel oder Fontdatei]
dotnet run --project AGSTranslate -- <Befehl> ...
```

**Sprache der Oberfläche:** Translator und Font-Editor gibt es auf Deutsch und Englisch. Unter
*Tools → Settings... → Language* wählt man *Systemsprache*, *English* oder *Deutsch*; die Wahl gilt
nach einem Neustart und auch für den eigenständigen Font-Editor. Die Menünamen in dieser Anleitung
stehen auf Englisch (*Tools → Settings...* heißt auf Deutsch *Werkzeuge → Einstellungen...*).
Meldungen der Prüfung und der Extraktion selbst bleiben englisch.

Als „Spieldatei“ kann man überall die **Spiel-EXE**, die **`.ags`-Datei** oder eine lose
**`game28.dta`** auswählen. Bei Steam-Versionen liegt das Spiel oft in einer `.ags` neben einer
kleinen EXE; das Tool findet die Daten in beiden Fällen.

## 2. Grundbegriffe

- **TRS** – die Übersetzung als Textdatei. Immer abwechselnd eine Zeile Originaltext, eine Zeile
  Übersetzung. Daran arbeitet man.
- **TRA** – die kompilierte Übersetzung, die das Spiel lädt. Wird aus der TRS erzeugt und muss
  **zum Spiel passen** (Spiel-ID und Titel werden hineingeschrieben).
- **Quelltext/Schlüssel** – die Originalzeile. Das Spiel sucht die Übersetzung über den *exakten*
  Text, inklusive Leerzeichen und Satzzeichen. Daher die Originalzeilen nie verändern.
- **Voice-Präfix `&N `** – Zeilen mit Sprachausgabe beginnen mit `&123 `. Die Übersetzung muss
  dasselbe Präfix haben (`&123 Hallo.`), sonst passt die Sprachausgabe nicht.
- **Kodierung** – UTF-8 oder 8-Bit (Codepage, z.B. 1252). Siehe [Kapitel 6](#welche-kodierung).

## 3. Neue Übersetzung erstellen

1. **Translator starten → *Tools → Extract Strings...*** und die Spieldatei wählen.
   Das Tool liest alle übersetzbaren Texte strukturiert aus dem Spiel: Dialoge, Dialogoptionen,
   Skripttexte, GUI-Texte, Inventar, Hotspots und Objekte aller Räume, Charakternamen.
2. Die Texte erscheinen in der Tabelle. **Übersetzung** in die rechte Spalte schreiben.
   - *Untranslated only* zeigt nur unübersetzte Zeilen, *Next untranslated* springt zur nächsten.
   - Leere Übersetzungen sind erlaubt: das Spiel zeigt dann den Originaltext.
   - Der Knopf *Translate* schlägt maschinelle Übersetzungen vor (DeepL, Claude oder Google),
     siehe [Kapitel 16](#16-maschinelle-übersetzung-deepl-claude-google).
3. Regelmäßig **speichern** (*File → Save*), das ergibt die TRS-Datei.
4. Kodierung wählen (*Tools → Settings... → Save encoding*), siehe
   [Kapitel 6](#welche-kodierung): Bei Spielen mit 8-Bit-Texten und Sprachen wie Deutsch,
   Französisch oder Spanisch **8-Bit (Latin-1)**, bei Spielen mit UTF-8-Texten **UTF-8**.

## 4. Übersetzung ins Spiel bringen

> Schneller: ***Tools → Install into Game...*** erledigt die Schritte 1–4 mit Sicherung und
> Rückgängig-Funktion ([Kapitel 15](#15-installieren-und-an-spieler-weitergeben)). Hier der Weg
> von Hand:

1. ***File → Export → Export as TRA...*** (aktuell geöffnete Übersetzung) oder
   ***Tools → Create TRA File...*** (eine TRS-Datei von der Platte); jeweils Spieldatei und
   Zieldatei wählen. Der Dateiname ist der Name der Sprache im Spiel, z.B. `Deutsch.tra`.
2. Achte auf die **Warnungen** danach (siehe unten). Besonders wichtig sind die Font-Warnungen:
   sie sagen dir, welche Zeichen das Spiel *nicht* anzeigen kann.
3. Die `.tra` in den **Spielordner** kopieren (neben die EXE bzw. `.ags`).
4. Sprache auswählen:
   - im Setup-Programm des Spiels (`winsetup.exe` bzw. die Spieleinstellungen), oder
   - in der `acsetup.cfg` im Spielordner:
     ```ini
     [language]
     translation=Deutsch
     ```
   Manche Spiele haben eine eigene Sprachauswahl im Optionsmenü.
5. Spiel starten und prüfen. Unbekannte Zeilen erscheinen im Original – das heißt meist, dass sie
   noch nicht übersetzt sind oder dass sich der Originaltext geändert hat
   ([Kapitel 5](#5-übersetzung-nach-einem-spiel-update-aktualisieren)).

## 5. Übersetzung nach einem Spiel-Update aktualisieren

Wenn das Spiel aktualisiert wurde oder eine alte TRS/TRA nicht mehr vollständig passt:

1. Die alte Übersetzung im Translator öffnen.
2. ***Tools → Update from Game...*** und die (neue) Spieldatei wählen.
3. Ergebnis: Die Liste enthält jetzt genau die Texte des aktuellen Spiels in Spielreihenfolge.
   - **unchanged** – Übersetzung übernommen,
   - **similar** – nur Leerzeichen am Rand oder das `&N`-Präfix hat sich geändert; Übersetzung
     übernommen und das Präfix automatisch angepasst,
   - **untranslated** – neue Texte, die noch übersetzt werden müssen,
   - **obsolete** – Übersetzungen, deren Originaltext es nicht mehr gibt. Sie bleiben am Ende der
     Datei erhalten (das Spiel ignoriert sie), damit keine Arbeit verloren geht.
4. Speichern und eine neue TRA erstellen.

Das ist auch der Weg, um eine Übersetzung, die mit einem älteren Tool erstellt wurde, zu
vervollständigen: ältere Extraktoren haben z.B. GUI-Texte und einige Dialogoptionen nicht gefunden.

## 6. Umlaute, ß und Anführungszeichen

Ob ein Zeichen im Spiel erscheint, hängt von **zwei Dingen** ab: der Kodierung der TRA und dem Font.

### Welche Kodierung?

Zuerst nachsehen, wie das Spiel selbst seine Texte speichert: *Tools → Game Info...* bzw.
`AGSTranslate info` zeigt `Text encoding: UTF-8` oder `8-bit, codepage 1252`.

| Spiel speichert Texte als | Übersetzung enthält | TRA-Kodierung |
|---|---|---|
| UTF-8 | beliebige Zeichen | **UTF-8** |
| 8-Bit (z.B. Codepage 1252) | nur Zeichen dieser Codepage (Deutsch, Französisch, Spanisch, Italienisch …) | **8-Bit** – funktioniert mit jeder Engine unter Windows, Linux und macOS |
| 8-Bit | Zeichen außerhalb der Codepage (z.B. Polnisch, Russisch in einem 1252-Spiel) | **UTF-8**, aber nur AGS 3.6+ und **nur unter Windows** |

Hintergrund:

- Mit einer **8-Bit-TRA** sucht die Engine jedes Zeichen über sein Byte in der Codepage im Font
  (ä = 0xE4, ß = 0xDF). Für Umlaute und ß ist das derselbe Code wie in Unicode.
- Mit einer **UTF-8-TRA** schaltet die Engine (ab AGS 3.6) auf UTF-8 um und sucht über den
  Unicode-Code. Speichert das Spiel seine Texte 8-bittig, muss die Engine dafür die Originaltexte
  umrechnen – mit einer Windows-Funktion (Locale `.1252`). **Unter Linux und macOS schlägt das fehl,
  dann bleibt das ganze Spiel in der Originalsprache.** Der Translator und `AGSTranslate` warnen
  davor. (Gefunden mit Blackwell Legacy unter Linux; siehe `Engine/ac/translation.cpp`.)
- **„ “ – …** liegen in Codepage 1252 auf den Bytes 0x80–0x9F. In einer 8-Bit-TRA erscheinen sie
  nur, wenn der Font an diesen Codes ein Zeichen hat – TrueType-Fonts haben das praktisch nie.
  Entweder `"` und `-` verwenden, oder die Zeichen im Font-Editor an Code `0x84` („), `0x93` (“),
  `0x96` (–), `0x85` (…) ergänzen (*Go to* `0x84` → *Create Glyph* bzw. *Copy from*).
- Engines vor 3.6 kennen kein UTF-8; dort immer 8-Bit.

### Font: das Zeichen muss enthalten sein

Jeder Text im Spiel wird mit einem bestimmten Font gezeichnet (Sprechtext, Dialogoptionen, GUI, ...).
Fehlt dort ein Zeichen, bleibt die Stelle leer. Fehlende Umlaute sind also fast immer ein
**Font-Problem**, kein Problem der Übersetzung.

Beim Erstellen der TRA prüft das Tool alle Sonderzeichen der Übersetzung gegen alle Fonts des
Spiels und warnt z.B.:

```
Font 8 (agsfnt8.ttf, IsWasted): no glyph for ß (179×), „ (30×)
Font 5 (agsfnt5.ttf, BBSesque): ä ö ü look identical to a o u (accents or umlaut dots missing in the font)
```

- *no glyph for* – das Zeichen fehlt im Font.
- *look identical to* – das Zeichen ist zwar da, sieht aber aus wie der Grundbuchstabe (Font hat
  „Umlaute ohne Punkte“, wie der Original-Sprechfont von Unavowed).
- Nicht jeder Font wird für übersetzten Text benutzt (Symbol- oder Titel-Fonts). Fonts, denen auch
  der Grundbuchstabe fehlt (z.B. reine Großbuchstaben-Fonts), meldet das Tool gar nicht erst.

Behoben wird das mit dem [Font-Editor](#7-font-editor).

## 7. Font-Editor

Start: eigenständig (`AGSFontEditor.App`) oder im Translator über *Tools → Font Editor...*
(öffnet automatisch das zuletzt gewählte Spiel).

### Fenster

- **Open Game...** – lädt alle Fonts eines Spiels genau so, wie die Engine sie lädt
  (siehe [Wie AGS Fonts lädt](#wie-ags-fonts-lädt)).
- **Open Font Files...** – einzelne `.ttf`/`.wfn`-Dateien öffnen.
- **FONTS** (links) – alle Fonts mit Nummer, Datei, Herkunft (*loose file* = Datei im Spielordner,
  *in package* = in der `.ags`/EXE), Größe im Spiel und Outline.
- **GLYPHS** (Mitte) – alle Zeichen des Fonts. Fehlende Zeichen sind rot mit × markiert. Filter
  *All codes / With glyph / Missing*; **Go to** springt zu einem Zeichen (`ß`, `U+201E`, `0x84`
  oder Dezimalzahl).
- **PREVIEW (GAME SIZE)** – Beispieltext in Spielgröße ohne Kantenglättung, also so, wie er im Spiel
  aussieht. Darunter steht, welche Zeichen fehlen. Rechts daneben wählt man, ob die Vorschau wie eine
  **UTF-8**- oder eine **8-Bit**-Übersetzung nachschlägt.
- **GLYPH EDITOR** (rechts) – Pixel-Editor für das gewählte Zeichen.

### Zeichen bearbeiten

1. Font links wählen, Zeichen im Raster wählen (oder über *Go to*).
2. Fehlt das Zeichen: **Create Glyph** – oder besser **Copy from** mit einem ähnlichen Zeichen
   (z.B. für `ä` von `a` kopieren, für `ß` von `B`) und dann anpassen.
3. Zeichnen: **Linksklick** schaltet ein Pixel um, Ziehen malt; **Rechtsklick**/Ziehen löscht.
   Die blaue Linie ist die Grundlinie. **Width**/**Height** ändern die Größe, **Undo**/**Redo**
   mit Strg+Z / Strg+Y.
4. In der Vorschau kontrollieren.
5. **Save Font As...** – unter dem **gleichen Dateinamen** (`agsfntN.ttf` bzw. `.wfn`) speichern.
   Der Editor überschreibt nie selbst eine Datei; das Ziel wählst du immer im Dialog.

Welche Fonts sich bearbeiten lassen:

- **WFN** (Bitmap-Fonts der alten AGS-Versionen): immer.
- **TTF-Pixelfonts** (die meisten Fonts in Retro-Adventures): ja. Der Editor erkennt das Pixelraster
  des Fonts und bearbeitet die Zeichen als Pixel. Wird das Raster nicht erkannt, kann man unter
  *Grid (units/pixel)* eine Rastergröße eingeben.
- **Normale Vektorfonts** (Arial, Times, Schreibschriften): nur ansehen. Solche Fonts haben die
  üblichen Sonderzeichen meistens ohnehin.

### Fonts entpacken

**Export Selected...** / **Export All...** kopieren die Font-Dateien **unverändert** in einen Ordner,
z.B. als Sicherung oder um sie in einem anderen Programm zu bearbeiten. Der Spielordner selbst ist
als Ziel gesperrt, weil Dateien dort die Fonts des Spiels ersetzen würden.

### Geänderte Fonts ins Spiel bringen

1. **Sicherung** der Font-Dateien im Spielordner anlegen (falls dort schon welche liegen).
2. Die gespeicherte `agsfntN.ttf`/`.wfn` in den **Spielordner** legen. Eine lose Datei im
   Spielordner hat Vorrang vor dem Font in der `.ags` – das Spielpaket selbst wird nicht verändert.
3. Neue TRA erstellen (die Font-Warnungen sollten jetzt verschwunden sein) und im Spiel prüfen.

Zurück zum Original: die lose Datei löschen bzw. die Sicherung zurückkopieren.

### Wie AGS Fonts lädt

- Font Nummer *N* heißt `agsfntN.ttf` (TrueType) oder `agsfntN.wfn` (Bitmap).
- Gibt es beide, wird die **`.ttf`** genommen.
- Eine Datei im **Spielordner** hat Vorrang vor der im Spielpaket.
- Die Größe, in der ein TTF-Font gezeichnet wird, steht in den Spieldaten (Spalte *size*).
- *Outline: font 9* heißt: erst wird der Text mit Font 9 in der Outline-Farbe gezeichnet, darüber
  der eigentliche Font. Ein neues Zeichen gehört dann auch in den Outline-Font.

## 8. Andere Spiele

**Die Programme** funktionieren mit jedem AGS-Spiel:

- **AGS 3.x** (insbesondere 3.4–3.6): vollständig – Texte werden strukturiert gelesen, Fonts,
  Übersetzung, Aktualisierung, alles. Getestet mit Unavowed (AGS 3.6.1).
- **AGS 2.5–2.7** (Spiele etwa von 2002 bis 2007): ebenfalls strukturiert, inklusive Räumen,
  Dialogzeilen und Raumnachrichten. Getestet mit neun Freeware-Spielen von AGS 2.51 bis 3.4.
- **Vor AGS 2.5**: Die Textextraktion arbeitet nur heuristisch und kann Texte übersehen oder Müll
  enthalten – das Ergebnis also prüfen. TRA erstellen und Fonts gehen trotzdem.
- UTF-8-Übersetzungen funktionieren erst ab Engine **3.6**, und für Spiele mit 8-Bit-Texten nur
  unter Windows ([Kapitel 6](#welche-kodierung)). Engine-Version und Textkodierung zeigt
  *Tools → Game Info...* bzw. `AGSTranslate info`.
- Getestet außerdem mit allen fünf Blackwell-Teilen (AGS 3.6.1, 8-Bit-Texte, WFN-Fonts mit nur
  128 Zeichen – Umlaute müssen dort mit dem Font-Editor ergänzt werden).

**Die für Unavowed angepassten Font-Dateien** gehören zu Unavowed:

- Die Nummern sind spielspezifisch: `agsfnt8.ttf` ist in einem anderen Spiel ein ganz anderer Font.
  Eine fremde Datei in ein anderes Spiel zu legen, ersetzt dort einen falschen Font.
- Benutzt ein anderes Spiel **denselben Font** (z.B. den freien Pixelfont *Munro*), kann man die
  angepasste Datei verwenden: im anderen Spiel mit dem Font-Editor nachsehen, welche Nummer der Font
  dort hat (Spalte *Name*), die Datei unter dieser Nummer (`agsfntN.ttf`) in den Spielordner legen und
  in der Vorschau prüfen.
- Die Fonts eines kommerziellen Spiels sind Teil des Spiels. Für den eigenen Gebrauch anpassen ist
  das eine; **weitergeben** (z.B. zusammen mit einer Fan-Übersetzung) nur, wenn die Lizenz des Fonts
  bzw. der Hersteller das erlaubt. Viele Pixelfonts sind frei (z.B. SIL Open Font License) – die
  Lizenz steht oft im Font selbst oder auf der Seite des Font-Autors.

## 9. Worauf man achten muss (Checkliste)

**Übersetzen**

- [ ] Originalzeilen nie ändern – auch keine Leerzeichen. Einige Dialogoptionen enden mit einem
      Leerzeichen; das gehört zum Schlüssel.
- [ ] Voice-Präfix übernehmen: `&123 Text` → `&123 Übersetzung`.
- [ ] Sonderzeichen der Engine beibehalten: `[` ist in AGS ein Zeilenumbruch, `%s`/`%d` sind
      Platzhalter, die das Skript füllt (Reihenfolge und Anzahl gleich lassen).
- [ ] Übersetzungen nicht wesentlich länger machen als das Original, wenn der Text in ein festes
      GUI-Feld passen muss (Buttons, Menüs).
- [ ] Keine unsichtbaren Zeichen (z.B. Zero-Width-Space U+200B aus kopierten Texten).
- [ ] *Tools → Check Translation...* ausführen: findet Platzhalter-, Präfix- und Längenfehler und
      uneinheitliche Übersetzungen ([Kapitel 14](#14-übersetzung-prüfen)).

**TRA erstellen**

- [ ] Passende Kodierung ([Kapitel 6](#welche-kodierung)): 8-Bit, wenn das Spiel 8-Bit-Texte hat und
      alle Zeichen in die Codepage passen; UTF-8 nur bei UTF-8-Spielen oder wenn unvermeidbar
      (dann nur Windows).
- [ ] TRA immer mit der **aktuellen** Spieldatei erstellen – die Spiel-ID muss passen, sonst lädt
      das Spiel die Übersetzung nicht.
- [ ] Warnungen lesen, vor allem die Font-Warnungen.

**Spielordner**

- [ ] Vor dem Ändern oder Ersetzen von Dateien eine **Sicherung** anlegen.
- [ ] Nur lose Dateien hinzufügen (`.tra`, `agsfntN.*`); das Spielpaket (`.ags`/EXE) nicht verändern.
- [ ] Nach einem **Spiel-Update**: *Update from Game* ausführen und eine neue TRA erstellen.
      Plattformen wie Steam können bei Updates oder „Dateien überprüfen“ Dateien im Spielordner
      zurücksetzen – eigene Dateien zusätzlich außerhalb des Spielordners aufbewahren.

**Vor dem Veröffentlichen einer Übersetzung**

- [ ] Im Spiel testen: Menüs/GUI, Dialoge, Sprechtexte, ein paar spätere Szenen.
- [ ] Rechte klären (Texte und Fonts gehören dem Spielhersteller).

## 10. Fehlerbehebung

| Problem | Ursache / Lösung |
|---|---|
| Spiel zeigt alles auf Englisch | `.tra` nicht im Spielordner, Sprache nicht ausgewählt, oder TRA für eine andere Spielversion erstellt → mit aktueller Spieldatei neu erstellen |
| Einzelne Zeilen bleiben englisch | Noch nicht übersetzt, oder Originaltext hat sich geändert → *Update from Game* |
| Umlaute/ß fehlen oder sind leer | Font hat das Zeichen nicht → TRA-Warnungen lesen, Font im Font-Editor ergänzen |
| Umlaut erscheint ohne Punkte („Damon“) | Font-Zeichen ist eine Kopie des Grundbuchstabens → im Font-Editor Punkte ergänzen |
| Unter Linux/macOS bleibt alles englisch, unter Windows geht es | UTF-8-TRA für ein Spiel mit 8-Bit-Texten → als 8-Bit-TRA erstellen ([Kapitel 6](#welche-kodierung)) |
| „ “ – … fehlen, Umlaute gehen | 8-Bit-TRA: Zeichen im Font an Code 0x84/0x93/0x96/0x85 ergänzen, oder `"`/`-` verwenden; UTF-8 nur unter Windows |
| Text mit Zeichen wie `$` `=` `\` statt Umlauten | Alte Übersetzung mit Ersatzzeichen für einen selbst gebauten Font → in echte Umlaute umwandeln und die Umlaute im Font an ihre echten Codes setzen |
| Text überlappt die Zeile darüber | Zeichen ragen weit über die Font-Höhe (z.B. zu hohe Umlautpunkte) → im Font-Editor flacher zeichnen |
| Log: „font has mistakes in data format“ (WFN) | Engine 3.6.1 meldet WFN-Fonts mit nicht belegten Zeichen; harmlos |
| Log: „data blocks nonsequential, TextOpts“ | TRA mit einer alten Version dieses Tools erstellt → neu erstellen |

Das Log der Engine aktiviert man in der `acsetup.cfg` mit
```ini
[log]
file=all:debug
```
Es liegt dann unter *Saved Games/Adventure Game Studio/ags.log* (Windows: im Benutzerordner) bzw.
`~/.local/share/ags/ags.log` (Linux). Mehr zum Log, zu Startoptionen und Debug-Tasten:
[Kapitel 12](#12-spiele-testen-und-fehler-suchen).

## 11. Kommandozeile (AGSTranslate)

Ausführliche Beschreibung aller Befehle mit Beispielen, Skripten und Fehlerbehandlung:
**[docs/CLI.md](CLI.md)**. Kurzübersicht:

```
AGSTranslate info <spiel>                        Titel, ID, AGS-Version, Kodierung
AGSTranslate extract <spiel> [aus.trs]           Alle Texte in eine TRS
AGSTranslate trs2tra <spiel> <ein.trs> <aus.tra> TRA erstellen (mit Font-Prüfung)
AGSTranslate tra2trs <ein.tra> <aus.trs> [spiel] TRA zurück in eine TRS
AGSTranslate merge <spiel> <alt.trs|tra> <aus.trs>
                                                 Übersetzung auf den aktuellen Spielstand bringen
AGSTranslate fonts <spiel> [ordner]              Fonts auflisten; mit Ordner: exportieren
AGSTranslate check <spiel> <ein.trs|tra> [glossar.txt]
                                                 Übersetzung prüfen (Kapitel 14)
AGSTranslate install <spiel> <ein.tra> [fonts...] In den Spielordner installieren (Kapitel 15)
AGSTranslate uninstall <spiel>                   Letzte Installation rückgängig machen
AGSTranslate pack <spiel> <ein.tra> <aus.zip> [fonts...]
                                                 Spielerpaket erstellen (Kapitel 15)
AGSTranslate translate <ein.trs> <aus.trs> [--service deepl|claude|google] [--to de] ...
                                                 Leere Einträge maschinell übersetzen (Kapitel 16)

Optionen: --8bit / --utf8  Kodierung der geschriebenen TRS/TRA festlegen (siehe Kapitel 6)
```

Rückgabewerte: `0` = OK, `1` = falsche Benutzung, `2` = Fehler (Datei, Format).
Warnungen erscheinen auf stderr mit dem Präfix `Warning:`.

## 12. Spiele testen und Fehler suchen

Fertige AGS-Spiele haben keine Konsole, in die man Befehle tippt. Zum Testen und zur Fehlersuche gibt
es drei Werkzeuge: das **Log** der Engine, **Startoptionen** und einige **Debug-Tasten**. Das Skript
`tools/ags-test.sh` fasst das für Linux zusammen.

### Testskript (Linux)

`tools/ags-test.sh` startet ein Spiel aus einem eigenen Testordner. Dieser enthält Verknüpfungen auf
die Spieldateien und echte Kopien der Dateien, die man testen will, dazu eine eigene `acsetup.cfg`,
eigene Spielstände und Einstellungen. **Der Spielordner selbst wird nie verändert**, die echten
Spielstände bleiben unberührt.

```bash
# Übersetzung und Fonts testen, im Fenster, ohne Ton
tools/ags-test.sh -t Deutsch.tra -f fonts/ --mute ~/.steam/steam/steamapps/common/"Blackwell Legacy"

# Dasselbe mit Debug-Modus (Strg+D zeigt Raum, Figuren, Objekte)
tools/ags-test.sh -t Deutsch.tra -f fonts/ -d "<Spielordner>"

# Originaltexte zum Vergleich
tools/ags-test.sh -l "" "<Spielordner>"

# Direkt in Raum 5 starten (alles nach -- geht an die Engine)
tools/ags-test.sh -t Deutsch.tra "<Spielordner>" -- --startr 5
```

| Option | Bedeutung |
|---|---|
| `-t DATEI` | diese TRA testen; wird in den Testordner kopiert und ausgewählt |
| `-f DATEI` | Font ersetzen (mehrfach möglich); bei einem Ordner alle `agsfnt*.wfn` und `*.ttf` darin |
| `-l NAME` | Übersetzung auswählen; Standard ist der Name der `-t`-Datei, `-l ""` = Originaltexte |
| `-d` | Debug-Modus der Engine (`--test`, siehe unten) |
| `--live` | Live-Hook laden: Texte beim Spielen im Translator korrigieren ([Live-Modus](#13-live-modus-übersetzung-beim-spielen-korrigieren-linux)) |
| `-o ORDNER` | Testordner; Standard `~/.cache/ags-test/<Name des Spielordners>` |
| `-e DATEI` | Engine; Standard ist `ags64`/`ags32` des Spiels, sonst `ags` aus dem PATH |
| `--mute` / `--software` / `--fullscreen` | ohne Ton / Software-Grafik (bei Grafikproblemen) / Vollbild statt Fenster |
| `--fresh` | Spielstände und Einstellungen früherer Testläufe verwerfen |

Nach dem Beenden zeigt das Skript die wichtigen Zeilen aus dem Log und den Rückgabewert der Engine.
Das ganze Log liegt danach in `<Testordner>/ags.log`, die Terminalausgabe in `<Testordner>/output.txt`.

Linux-Spiele von Steam (z.B. Wadjet Eye) bringen ihre Engine mit (`ags64`, `lib64/`). Für Spiele, die
nur eine Windows-EXE haben, braucht man eine installierte AGS-Engine für Linux (`-e`), oder man testet
unter Windows von Hand wie unten beschrieben.

### Live-Modus

Mit `--live` startet das Testskript das Spiel so, dass man beim Spielen im Translator Texte korrigieren
kann: [Kapitel 13](#13-live-modus-übersetzung-beim-spielen-korrigieren-linux).

### Das Engine-Log

In der `acsetup.cfg` des Spiels (oder mit der Startoption `--log-file=all:debug`):

```ini
[log]
file=all:debug
```

| System | Logdatei |
|---|---|
| Linux | `~/.local/share/ags/ags.log` |
| Windows | `Gespeicherte Spiele\Adventure Game Studio\ags.log` im Benutzerordner |
| Proton/Wine | dieselbe Datei im Wine-Prefix des Spiels |

Es gibt nur eine Logdatei für alle Spiele; jeder Start überschreibt sie. Die Engine 3.6.1 ignoriert
die Option `--log-file-path` (getestet), daher kopiert das Testskript das Log nach dem Spielen in den
Testordner. Statt `all:debug` kann man Bereiche und Stufen wählen, z.B. `main:info,game:debug`
(Bereiche `main`, `game`, `script`, `sprcache`, `manobj`, `sdl`; Stufen `fatal`, `error`, `warn`,
`info`, `debug`).

| Logzeile | Bedeutung |
|---|---|
| `Translation loaded: Deutsch.tra` und `Translation initialized: Deutsch (format: ASCII)` | Übersetzung gefunden und geladen; `ASCII` = 8-Bit-TRA |
| `Translation initialized: … (format: UTF-8)` bei einem 8-Bit-Spiel | unter Linux/macOS bleibt dann alles englisch ([Kapitel 6](#welche-kodierung)) |
| `WARNING: font 'agsfnt6.wfn' has mistakes in data format` | WFN-Font mit unbelegten Zeichen; harmlos |
| `FileOpen: success, … path: …` | welche Dateien das Spielskript öffnet (Einstellungen, Spielstände) |
| `Abort key pressed` | Spiel mit Alt+X abgebrochen |
| `Error: the program has exited without requesting it` | Engine wurde nicht über das Spiel beendet (Fenster geschlossen, Absturz) |
| `Quitting the game...` | normales Beenden |

### Startoptionen der Engine

Unter Linux ist die Engine `ags64` (bei Steam über `startgame`), unter Windows die EXE des Spiels.
Alle Optionen zeigt `--help`; die wichtigsten (Engine 3.6.1):

| Option | Wirkung |
|---|---|
| `--windowed` / `--fullscreen` | im Fenster / im Vollbild |
| `--translation NAME` / `--no-translation` | Übersetzung wählen / Originaltexte |
| `--test` | Debug-Modus (Tasten siehe unten) |
| `--startr N` | direkt in Raum N starten |
| `--loadsavedgame DATEI` | Spielstand direkt laden |
| `--log-file=all:debug` / `--log-stdout=all:debug` | Log in die Datei / ins Terminal |
| `--gfxdriver software` | Software-Grafik statt OpenGL |
| `--fps` | Bildrate anzeigen |
| `--novideo` | Videos überspringen |
| `--user-data-dir ORDNER` | Spielstände in diesem Ordner |
| `--localuserconf` | Einstellungen im Spielordner statt im Benutzerordner speichern |
| `--tell-filepath` / `--tell-config` | benutzte Pfade / zusammengeführte Konfiguration ausgeben und beenden |

In Steam kann man Optionen unter *Eigenschaften → Allgemein → Startoptionen* eintragen, z.B.
`%command% --windowed --test`.

### Debug-Tasten

| Taste | Wirkung | Voraussetzung |
|---|---|---|
| Alt+Enter | Fenster ↔ Vollbild | immer |
| Alt+X | Spiel sofort abbrechen | solange das Spiel die Taste nicht abschaltet |
| Strg+D | Raumnummer, Position der Spielfigur, Objekte und Figuren im Raum | `--test` |
| Strg+Alt+V | Laufzeit-Informationen der Engine | `--test` |
| Strg+E | Bildrate nicht begrenzen | `--fps` |
| Strg+X, Strg+S, Strg+A … | Raum wechseln, alle Gegenstände, begehbare Flächen | `--test` **und** das Spielskript enthält diese Tasten (AGS-Vorlage); bei kommerziellen Spielen meist entfernt |

Strg+D funktioniert mit `--test` auch in kommerziellen Spielen (getestet mit Blackwell Legacy). Echte
Fehlersuche im Skript mit Haltepunkten und Variablen gibt es nur im AGS-Editor mit dem Quellprojekt
des Spiels.

### Typische Fragen, die man so beantwortet

| Frage | Vorgehen |
|---|---|
| Wird meine Übersetzung überhaupt geladen? | Log: `Translation initialized` suchen |
| Warum ist dieser Text noch englisch? | Text in der TRS des Spiels suchen (`grep -n "Text" spiel.trs`); fehlt er, ist er eine Grafik oder wird im Skript zusammengesetzt |
| Fehlen Zeichen im Font? | `AGSTranslate trs2tra` warnt; Font-Editor-Vorschau; im Spiel mit `-f` testen |
| In welchem Raum bin ich? | `-d`, dann Strg+D |
| Wo speichert das Spiel? | `--tell-filepath` oder `FileOpen`-Zeilen im Log |
| Wie klingt meine Übersetzung im Spiel, und wo hakt sie? | [Live-Modus](#13-live-modus-übersetzung-beim-spielen-korrigieren-linux): beim Spielen korrigieren |

## 13. Live-Modus: Übersetzung beim Spielen korrigieren (Linux)

Im Live-Modus zeigt der Translator jeden Text, den das laufende Spiel gerade anzeigt. Ändert man eine
Übersetzung, zeigt das Spiel beim nächsten Mal den neuen Text, und der Translator speichert die TRS. So
kann man beim Spielen direkt Korrekturen eintragen, statt sich Fehler zu notieren und später zu suchen.

```
 Spiel                                       Translator
 ┌───────────────────────┐   jeder Text     ┌─────────────────────────────┐
 │ zeigt "Lebewohl, ..." ├────────────────► │ Live-Bereich: neuester oben │
 │                       │                  │ anklicken, unten ändern     │
 │ zeigt die Korrektur   │ ◄────────────────┤ Korrektur + TRS speichern   │
 └───────────────────────┘                  └─────────────────────────────┘
```

Wie das technisch funktioniert, steht in **[docs/Live-Hook.md](Live-Hook.md)**.

### Was man braucht

- **Linux** und ein Spiel mit **eigener 64-Bit-Linux-Engine** (`ags64` im Spielordner), z.B. die
  Linux-Versionen der Wadjet-Eye-Spiele auf Steam. Getestet mit Blackwell Legacy (AGS 3.6.1).
  Windows-Spiele gehen noch nicht.
- Die **TRS** der Übersetzung und die daraus erstellte **TRA**, dazu ggf. die angepassten Fonts.
- Entweder das **Release-Paket** `…-linux-x64.tar.gz` (enthält Translator, `ags-test.sh` und den Hook
  `agslive.so`) oder den **Quellcode** mit .NET 9 SDK, `gcc` und `make`.

### Schritt für Schritt

Man braucht zwei Terminals: eins für den Translator, eins für das Spiel. In den Beispielen liegen
Übersetzung und Fonts in `~/Übersetzung/` (`Deutsch.trs`, `Deutsch.tra`, `fonts/`).

**1. Translator mit der TRS starten**

Release-Paket (im entpackten Ordner):
```bash
./AGS_TranslationEditor ~/Übersetzung/Deutsch.trs
```
Quellcode (im Repository-Ordner):
```bash
dotnet run -c Release --project AGS_TranslationEditor.Avalonia -- ~/Übersetzung/Deutsch.trs
```
Man kann die TRS auch wie gewohnt über *File → Open* öffnen.

**2. Live einschalten**

In der Werkzeugleiste auf **● Live** klicken. Rechts erscheint der Live-Bereich mit
„Waiting for the game“. Der Translator wartet jetzt auf das Spiel; die Reihenfolge ist egal, man kann
das Spiel auch zuerst starten.

**3. Spiel mit dem Hook starten (zweites Terminal)**

Release-Paket:
```bash
./ags-test.sh --live -t ~/Übersetzung/Deutsch.tra -f ~/Übersetzung/fonts "<Spielordner>"
```
Quellcode:
```bash
tools/ags-test.sh --live -t ~/Übersetzung/Deutsch.tra -f ~/Übersetzung/fonts "<Spielordner>"
```
Der Spielordner ist z.B. `~/.steam/steam/steamapps/common/Blackwell Legacy`. Er wird nicht verändert:
Das Spiel läuft aus einem Testordner unter `~/.cache/ags-test/`, mit eigenen Spielständen
([Kapitel 12](#testskript-linux)). Im Quellcode baut das Skript beim ersten Mal den Hook selbst.

Im Terminal muss stehen:
```
agslive: get_translation hooked
agslive: editor connected
```
und im Translator „Game connected“.

**4. Spielen und korrigieren**

- Jeder angezeigte Text erscheint rechts, **der neueste oben**: groß die gezeigte Übersetzung, darunter
  klein das Original. Darunter steht ggf. *untranslated*, *corrected live* oder
  *not in this translation file*.
- Einen Text **anklicken**: Der Eintrag wird in der Tabelle ausgewählt.
- Unten im Editor bei **TRANSLATION** ändern. Auch Änderungen direkt in der Tabelle oder per *Translate*
  gehen ans Spiel.
- Die Änderung erscheint, **sobald das Spiel den Text das nächste Mal anzeigt**. Eine Sprechzeile, die
  gerade auf dem Bildschirm steht, ändert sich nicht mehr rückwirkend.

So sieht man eine Änderung schnell wieder:

| Art von Text | So erscheint er erneut |
|---|---|
| Hotspot- und Objektnamen („Fenster“, „Tür“) | Maus wegbewegen und wieder darüberfahren |
| Beschreibungen beim Anschauen | den Gegenstand noch einmal anschauen |
| Gesprächszeilen | vor dem Gespräch speichern, nach der Korrektur den Spielstand laden |
| Menüs und Dialogoptionen | Menü schließen und wieder öffnen |

**5. Speichern**

Mit dem Häkchen **Save the TRS after each correction** (Standard: an) speichert der Translator die TRS
1,5 Sekunden nach der letzten Änderung. Das geht nur bei `.trs`-Dateien; eine geöffnete TRA muss man
erst mit *Save As* als TRS speichern. Ohne Häkchen speichert man wie gewohnt mit Strg+S.

Wer erst gefahrlos probieren will, arbeitet mit einer Kopie der TRS.

**6. Zum Schluss: TRA neu erstellen**

Die Korrekturen gelten im Spiel nur, solange der Translator mit Live läuft. Startet man das Spiel neu,
schickt der Translator sie automatisch noch einmal. Dauerhaft ins Spiel kommen sie erst mit einer neuen TRA:

```bash
AGSTranslate trs2tra "<Spielordner>/<Spiel>.ags" ~/Übersetzung/Deutsch.trs ~/Übersetzung/Deutsch.tra
```
oder im Translator *File → Export → Export as TRA*. Danach die TRA wie in [Kapitel 4](#4-übersetzung-ins-spiel-bringen)
in den Spielordner kopieren.

### Gut zu wissen

- Das Sprachausgabe-Präfix `&N ` ergänzt der Translator automatisch, die Sprachausgabe bleibt erhalten.
- Eine leere Übersetzung zeigt im Spiel das Original, so wie es die neu erstellte TRA auch tun würde.
- Texte mit *not in this translation file* setzt das Spiel selbst zusammen oder sie fehlen in der TRS;
  sie lassen sich nicht bearbeiten. Fehlen viele, hilft *Tools → Update from Game*.
- Lädt man im Translator eine andere Datei, verwirft er die bisherigen Live-Korrekturen im Spiel.
- Translator und Spiel verbinden sich über `$XDG_RUNTIME_DIR/agslive.sock`. Mit der Umgebungsvariable
  `AGSLIVE_SOCKET` kann man einen anderen Pfad wählen (für beide gleich setzen, höchstens 107 Zeichen).
- Den Hook kann man von Hand bauen: `make -C AGSLive`.

### Wenn etwas nicht klappt

| Problem | Ursache / Lösung |
|---|---|
| Live-Bereich bleibt bei „Waiting for the game“ | Spiel ohne `--live` gestartet, oder `AGSLIVE_SOCKET` nur bei einem der beiden gesetzt |
| `agslive: get_translation hooked` fehlt im Terminal | Hook wurde nicht geladen: `--live` vergessen, oder das Spiel nutzt keine 64-Bit-Linux-Engine |
| `agslive: _Z15get_translationPKc not found` | Engine ohne exportierte Funktionsnamen; mit diesem Spiel geht der Live-Modus nicht |
| `agslive: unknown instructions at the start of get_translation` | unbekannter Engine-Build; der Hook verändert sicherheitshalber nichts. Bitte mit den angezeigten Bytes melden |
| „Another editor already waits for the game“ | in einem zweiten Translator ist Live schon an; diesen schließen |
| „Not saved automatically: only .trs files are saved“ | geöffnet ist eine TRA, CSV oder PO; mit *Save As* als TRS speichern |
| Änderung erscheint nicht | Text wird erst beim nächsten Anzeigen neu geholt (Tabelle oben) |
| Spiel startet mit `--live` nicht | ohne `--live` testen; die Ausgabe steht in `~/.cache/ags-test/<Spiel>/output.txt` |

## 14. Übersetzung prüfen

Viele Fehler sieht man sonst erst beim Spielen: ein Button-Text, der abgeschnitten wird, ein
vergessenes `%d`, ein Satz, der einmal mit „du“ und einmal mit „Sie“ übersetzt ist. Die Prüfung
findet solche Stellen in der ganzen Übersetzung auf einmal.

**Im Translator:** Übersetzung öffnen → *Tools → Check Translation...* → Spieldatei wählen
(`.ags`, EXE oder `game28.dta`). Rechts erscheint eine Liste der Funde. Ein Klick auf einen Fund
springt zum Eintrag, den man dann direkt verbessert. Die Prüfung ändert nichts an der Datei.

**Auf der Kommandozeile:**

```bash
AGSTranslate check <spiel> <übersetzung.trs|tra> [glossar.txt]
```

### Was geprüft wird

| Art | Stufe | Was es bedeutet |
|---|---|---|
| Placeholders | Fehler | `%d`, `%s` usw. fehlen oder stehen in anderer Reihenfolge, oder Spezialtexte wie `@SCORE@` fehlen. Das Skript füllt die Platzhalter der Reihe nach – vertauschte Platzhalter zeigen falsche Werte oder bringen das Spiel zum Absturz. |
| Voice prefix | Warnung | Die Übersetzung beginnt nicht mit dem `&123` des Originals. Dann spielt das Spiel die Sprachausgabe nicht ab. |
| Too wide | Warnung | Der übersetzte Text ist breiter als sein Button (gemessen im echten Spiel-Font, in Pixeln). Buttons schneiden zu lange Texte ab. |
| Too many lines | Warnung | Ein Label bricht den Text um; die Übersetzung braucht mehr Zeilen, als das Label hoch ist. |
| Spaces | Warnung | Leerzeichen am Anfang oder Ende fehlen oder sind zu viel – bei Dialogoptionen und zusammengesetzten Texten sichtbar. Gesprochene Zeilen (`&123`) werden hier nicht gemeldet. |
| Glossary | Warnung | Ein Begriff aus dem Glossar ist anders übersetzt als festgelegt (siehe unten). |
| Inconsistent | Hinweis | Derselbe Originaltext ist an verschiedenen Stellen verschieden übersetzt. Kurze gesprochene Zeilen wie „All right.“ dürfen sich unterscheiden und werden erst ab vier Wörtern gemeldet. |
| Names | Hinweis | Ein Figurenname, den die Übersetzung beibehält (z.B. „Rosa“), fehlt in einer Zeile, deren Original ihn enthält. |
| Line breaks | Hinweis | Anzahl der Zeilenumbrüche `[` weicht vom Original ab. |
| Formal address (Sie) | Hinweis | „Sie“, „Ihnen“, „Ihr“ mitten im Satz. So findet man Stellen, an denen eine Figur plötzlich gesiezt wird – und Tippfehler wie „bis **Sie** fertig ist“ statt „sie“. |

*Hinweise* sind keine Fehler: man schaut sie durch und entscheidet selbst. Bei der deutschen
Blackwell-Legacy-Übersetzung fand die Prüfung zum Beispiel „Was wollen **sie** wissen?“ und eine
Zeile, die einmal „Wenn Sie hier wohnen würden…“ und einmal „Wenn du hier wohnen würdest…“ lautet.

### Textbreite: wie genau ist das?

- **WFN-Fonts** (Bitmap): exakt, die Breiten stehen pixelgenau im Font.
- **TrueType-Fonts**: geschätzt aus den Zeichenbreiten des Fonts und der Font-Größe aus den
  Spieldaten. Hinting und Kerning der Engine fehlen, ein, zwei Pixel Abweichung sind möglich. Die
  Meldung sagt dazu „(TrueType: estimated)“.
- Gemeldet wird nur, wenn die Übersetzung **zu breit und breiter als das Original** ist. Wo schon
  das Original nicht passt, ist es offenbar gewollt.
- Geprüft werden nur Texte, die fest im GUI stehen. Viele Spiele setzen Button-Texte per Skript
  (bei Blackwell stehen im GUI nur Platzhalter wie „New Button“) – die kann die Prüfung nicht sehen;
  dafür hilft nur Testen ([Kapitel 12](#12-spiele-testen-und-fehler-suchen)) oder der
  [Live-Modus](#13-live-modus-übersetzung-beim-spielen-korrigieren-linux).
- Labels mit `@OVERHOTSPOT@` (zeigt den Namen unter dem Mauszeiger) werden mit jedem übersetzten
  Hotspot-, Objekt-, Figuren- und Gegenstandsnamen geprüft.

### Glossar

Ein Glossar legt fest, wie Begriffe und Namen übersetzt werden. Es ist eine einfache Textdatei
(UTF-8), eine Zeile pro Begriff:

```
# Kommentare beginnen mit #
Auntie = Tante
spirit guide = Geistführer | Geisterführer
Deacon = Deacon
```

- Links das Original, rechts die Übersetzung. Mehrere erlaubte Übersetzungen trennt `|`.
- Soll ein Begriff unübersetzt bleiben, schreibt man ihn auf beide Seiten (`Deacon = Deacon`).
- Groß-/Kleinschreibung spielt keine Rolle. Das Original muss als ganzes Wort vorkommen
  („spirit“ passt nicht auf „spirited“); die Übersetzung muss nur irgendwo enthalten sein
  („Tante“ passt auf „liebe Tante“).

Der Translator nimmt automatisch eine Datei **`glossary.txt`** oder **`Glossar.txt`** im selben
Ordner wie die geöffnete Übersetzung. Auf der Kommandozeile gibt man sie als drittes Argument an.

### Tipp: Anrede festlegen

Wer wen duzt oder siezt, legt man am besten vor dem Übersetzen fest und schreibt es oben ins
Glossar als Kommentar (`# Rosa ↔ Joey: du`, `# Rosa → Polizist: Sie`). Die Hinweise
„Formal address“ und „Inconsistent“ zeigen dann die Stellen, die nicht dazu passen.

## 15. Installieren und an Spieler weitergeben

### In das eigene Spiel installieren

*Tools → Install into Game...* erledigt Schritt 1–4 aus [Kapitel 4](#4-übersetzung-ins-spiel-bringen)
auf einmal:

1. Übersetzung öffnen, *Install into Game...* wählen und die Spieldatei angeben.
2. Bestätigen. Auf Wunsch einen Ordner mit geänderten Fonts (`agsfntN.ttf`/`.wfn`) angeben.
3. Der Translator erstellt die TRA (Name = Name der geöffneten Datei, z.B. `Deutsch.trs` → Sprache
   „Deutsch“), kopiert sie und die Fonts in den Spielordner und stellt die Sprache ein.

Dabei passiert Folgendes:

- **Sicherung:** Jede Datei, die ersetzt wird, wird vorher nach
  `agstranslate-backup/<Datum-Uhrzeit>/` im Spielordner kopiert. Dort steht auch eine Liste
  (`manifest.txt`), was hinzugefügt und was ersetzt wurde.
- **Sprache:** In der `acsetup.cfg` des Spielordners wird `[language] translation=Deutsch` gesetzt.
  Zusätzlich in der `acsetup.cfg` pro Benutzer, falls es sie gibt: Die Engine speichert die
  zuletzt gewählte Sprache beim Beenden dort (Linux: `~/.local/share/ags/<Spiel>/acsetup.cfg`),
  und diese Einstellung hat Vorrang vor der im Spielordner.
- **Prüfungen vorher:** Die TRA muss zum Spiel passen (Spiel-ID und Name); Fonts müssen
  `agsfntN.ttf`/`agsfntN.wfn` heißen. Ist eine Datei im Spielordner ein Link (z.B. aus einem
  Testordner), bricht die Installation ab, damit nicht über den Link eine andere Datei geändert wird.
- **Font-Warnung danach:** Wird ein installierter Font vom Spiel gar nicht benutzt (z.B. weil es
  ein `agsfntN.ttf` gibt, das Vorrang vor `agsfntN.wfn` hat), sagt die Meldung das.

**Rückgängig:** *Tools → Uninstall from Game...* legt die gesicherten Dateien zurück und stellt
die `acsetup.cfg` wieder her. Gelöscht wird dabei **nichts**: Dateien, die die Installation
hinzugefügt hat, werden in den Unterordner `removed` des Sicherungsordners verschoben. Mehrere
Installationen hintereinander (z.B. nach Verbesserungen) werden in umgekehrter Reihenfolge
zurückgenommen – jedes Uninstall macht die jeweils letzte Installation rückgängig.

Kommandozeile: `AGSTranslate install <spiel> <Deutsch.tra> [fonts...]` und
`AGSTranslate uninstall <spiel>` ([CLI-Anleitung](CLI.md#install--uninstall--in-den-spielordner-installieren)).

> Steam & Co. können bei einem Update oder „Dateien überprüfen“ Dateien im Spielordner
> zurücksetzen. Dann einfach erneut installieren.

### Spielerpaket erstellen

*Tools → Create Player Package...* erstellt eine Zip-Datei, die man an Spieler weitergeben kann.
Sie enthält einen Ordner `Deutsch-Patch` mit:

| Datei | Zweck |
|---|---|
| `Deutsch.tra` | die Übersetzung |
| `agsfntN.*` | geänderte Fonts, falls angegeben |
| `install.sh` / `uninstall.sh` | Installation für Linux und macOS (`sh install.sh`) |
| `install.bat` / `uninstall.bat` | Installation für Windows (Doppelklick) |
| `LIESMICH.txt` | Anleitung für Spieler (deutsch, kurz auch englisch) |

Für Spieler:

1. Den Ordner `Deutsch-Patch` in den Spielordner entpacken.
2. `install.bat` doppelklicken (Windows) bzw. `sh install.sh` im Terminal (Linux/macOS).
   Die Skripte sichern jede ersetzte Datei in `Deutsch-backup` im Spielordner. Ein zweiter
   Durchlauf überschreibt diese Sicherung nicht.
3. Unter Linux/macOS stellt das Skript die Sprache in der `acsetup.cfg` ein. Unter Windows wählt
   man sie im Setup des Spiels (`winsetup.exe` bzw. Spiel-exe mit `--setup`).

`uninstall` stellt den alten Zustand wieder her und verschiebt hinzugefügte Dateien in den
Sicherungsordner, statt sie zu löschen.

Gut zu wissen:

- Die TRA wird für **die gewählte Spieldatei** gebaut. Spieler brauchen dieselbe Spielversion;
  nach einem Spiel-Update muss man ein neues Paket erstellen.
- Für ein Paket, das unter Linux und macOS funktioniert, eine **8-Bit-TRA** verwenden, wenn das
  Spiel 8-Bit-Texte hat ([Kapitel 6](#welche-kodierung)). Bei einer UTF-8-TRA warnt das Programm.
- **Rechte:** Fonts und Texte gehören dem Spielhersteller. Fonts nur mitgeben, wenn man darf –
  das Programm erinnert beim Erstellen daran. Am besten vorher beim Hersteller nachfragen.
- Das Linux-Skript ist getestet (Installation, zweiter Durchlauf, Deinstallation). Die
  Windows-Skripte sind einfache Batch-Dateien, aber noch nicht auf einem echten Windows erprobt.

## 16. Maschinelle Übersetzung (DeepL, Claude, Google)

Der Knopf **Translate** in der Werkzeugleiste übersetzt maschinell:

- Ist eine Zeile ausgewählt, nur diese.
- Ist keine ausgewählt, **alle leeren Einträge**. Vorher fragt das Programm nach, wie viele es sind.
  Bereits übersetzte Zeilen werden nie überschrieben.

Das Ergebnis ist ein **Vorschlag**: Man sollte jede Zeile lesen und anpassen. Danach lohnt sich
*Tools → Check Translation...* ([Kapitel 14](#14-übersetzung-prüfen)).

### Dienst einstellen

*Tools → Settings...*, Abschnitt *Machine translation*:

| Einstellung | Bedeutung |
|---|---|
| Service | **Google** (kostenlos, ohne Schlüssel, inoffizielle Schnittstelle, kann gesperrt werden), **DeepL** (API-Schlüssel, Free-Konto reicht für 500.000 Zeichen im Monat) oder **Claude** (Anthropic-API-Schlüssel, kostenpflichtig nach Tokens) |
| Languages | Sprachcodes, z.B. `en` → `de`. Quelle leer oder `auto`: der Dienst erkennt sie |
| Form of address | Standard, *du* oder *Sie* (nur DeepL und Claude) |
| DeepL / Anthropic API key | Schlüssel des Dienstes. Alternativ die Umgebungsvariablen `DEEPL_AUTH_KEY` bzw. `ANTHROPIC_API_KEY` |
| Claude model | leer = `claude-opus-5-5`; ein anderes Modell, z.B. ein günstigeres, kann man hier eintragen |

Die Schlüssel stehen im Klartext in der Einstellungsdatei
(`~/.config/AGSTranslationEditor/settings.json` bzw. `%APPDATA%\AGSTranslationEditor\settings.json`).
Unter Linux und macOS kann nur der eigene Benutzer sie lesen. Wer das nicht möchte, lässt das Feld
leer und setzt die Umgebungsvariable.

### Was mit den AGS-Zeichen passiert

Alle Dienste bekommen die Texte so, dass die Steuerzeichen erhalten bleiben:

- Das **Voice-Präfix** `&123 ` wird vor dem Senden abgetrennt und danach wieder vorangestellt.
- **Leerzeichen** am Anfang und Ende bleiben wie im Original.
- **Zeilenumbrüche** `[`: DeepL bekommt sie als geschützte Tags, Google übersetzt jede Zeile einzeln,
  Claude wird angewiesen, sie zu behalten.
- **Platzhalter** wie `%s`, `%d`, `@SCORE@` und das wörtliche `\[`: bei DeepL geschützt, bei Claude per
  Anweisung. Ob sie wirklich alle noch da sind, zeigt *Check Translation*.

### Unterschiede der Dienste

- **Claude** bekommt die Zeilen in Spielreihenfolge (40 pro Anfrage), dazu den Spieltitel, die
  gewünschte Anrede und die passenden Begriffe aus der **Glossar-Datei** (`glossary.txt` bzw.
  `Glossar.txt` neben der Übersetzung, [Kapitel 14](#glossar)). Dadurch passen Dialoge und Namen
  meist besser zusammen. Falls eine Anfrage aus Sicherheitsgründen abgelehnt wird, versucht es die
  API selbst noch einmal mit einem passenden anderen Modell.
- **DeepL** übersetzt 50 Zeilen pro Anfrage, sehr schnell, mit wählbarer Anrede, aber ohne Glossar.
- **Google** übersetzt Zeile für Zeile und ist deshalb bei vielen Einträgen langsam.

Bricht eine Übersetzung ab (Schlüssel falsch, Kontingent aufgebraucht, keine Verbindung), bleiben
die bis dahin übersetzten Zeilen erhalten; die Statusleiste zeigt den Grund.

**Datenschutz und Rechte:** Die Texte des Spiels gehen an den gewählten Dienst. Das sollte man nur
tun, wenn man die Texte dafür verwenden darf.

### Auf der Kommandozeile

```bash
export DEEPL_AUTH_KEY=...           # oder ANTHROPIC_API_KEY für Claude
AGSTranslate translate Deutsch.trs Deutsch-mt.trs --service deepl --to de --informal
AGSTranslate translate Deutsch.trs Deutsch-mt.trs --service claude --glossary glossary.txt
```

Details in [CLI.md](CLI.md#translate--leere-einträge-maschinell-übersetzen).

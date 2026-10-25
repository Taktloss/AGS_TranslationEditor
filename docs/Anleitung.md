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
- **Kodierung** – UTF-8 oder 8-Bit (Codepage, z.B. 1252). Siehe [Kapitel 6](#6-umlaute-ß-und-anführungszeichen).

## 3. Neue Übersetzung erstellen

1. **Translator starten → *Tools → Extract Strings...*** und die Spieldatei wählen.
   Das Tool liest alle übersetzbaren Texte strukturiert aus dem Spiel: Dialoge, Dialogoptionen,
   Skripttexte, GUI-Texte, Inventar, Hotspots und Objekte aller Räume, Charakternamen.
2. Die Texte erscheinen in der Tabelle. **Übersetzung** in die rechte Spalte schreiben.
   - *Untranslated only* zeigt nur unübersetzte Zeilen, *Next untranslated* springt zur nächsten.
   - Leere Übersetzungen sind erlaubt: das Spiel zeigt dann den Originaltext.
3. Regelmäßig **speichern** (*File → Save*), das ergibt die TRS-Datei.
4. Kodierung: *Tools → Settings... → Save encoding* auf **UTF-8 (AGS 3.6+)** stellen
   (in der TRS steht dann `//#Encoding=UTF-8`). Bei Spielen mit älterer Engine 8-Bit lassen.

## 4. Übersetzung ins Spiel bringen

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

### Kodierung: UTF-8 verwenden

- Mit einer **UTF-8-TRA** schaltet die Engine (ab AGS 3.6) das ganze Spiel auf UTF-8 um und sucht
  jedes Zeichen über seinen Unicode-Code im Font. Das funktioniert auch in Spielen, die selbst
  8-Bit-Texte haben (die Engine rechnet die Originaltexte passend um).
- Mit einer **8-Bit-TRA** sucht die Engine das Byte der Codepage. „ “ – … liegen in Codepage 1252 auf
  den Bytes 0x80–0x9F; an diesen Stellen hat praktisch kein TrueType-Font ein Zeichen. Diese Zeichen
  erscheinen dann nie.
- Für Spiele mit älteren Engines (vor 3.6) gibt es kein UTF-8; dort muss man bei 8-Bit bleiben und
  „ “ durch " ersetzen.

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
- **AGS 2.x**: TRA erstellen und Fonts gehen; die Textextraktion arbeitet bei diesen alten Spielen
  nur heuristisch und kann Texte übersehen oder Müll enthalten – das Ergebnis also prüfen.
- UTF-8-Übersetzungen funktionieren erst ab Engine **3.6**. Die Engine-Version zeigt
  *Tools → Game Info...* bzw. `AGSTranslate info`.

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

**TRA erstellen**

- [ ] Kodierung **UTF-8** (bei Engine 3.6+).
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
| „ “ – … fehlen, Umlaute gehen | 8-Bit-TRA → als UTF-8 erstellen (Engine 3.6+) |
| Text überlappt die Zeile darüber | Zeichen ragen weit über die Font-Höhe (z.B. zu hohe Umlautpunkte) → im Font-Editor flacher zeichnen |
| Log: „font has mistakes in data format“ (WFN) | Engine 3.6.1 meldet WFN-Fonts mit nicht belegten Zeichen; harmlos |
| Log: „data blocks nonsequential, TextOpts“ | TRA mit einer alten Version dieses Tools erstellt → neu erstellen |

Das Log der Engine aktiviert man in der `acsetup.cfg` mit
```ini
[log]
file=all:debug
```
Es liegt dann unter *Saved Games/Adventure Game Studio/ags.log* (Windows: im Benutzerordner).

## 11. Kommandozeile (AGSTranslate)

```
AGSTranslate info <spiel>                        Titel, ID, AGS-Version, Kodierung
AGSTranslate extract <spiel> [aus.trs]           Alle Texte in eine TRS
AGSTranslate trs2tra <spiel> <ein.trs> <aus.tra> TRA erstellen (mit Font-Prüfung)
AGSTranslate tra2trs <ein.tra> <aus.trs> [spiel] TRA zurück in eine TRS
AGSTranslate merge <spiel> <alt.trs|tra> <aus.trs>
                                                 Übersetzung auf den aktuellen Spielstand bringen
AGSTranslate fonts <spiel> [ordner]              Fonts auflisten; mit Ordner: exportieren
```

Rückgabewerte: `0` = OK, `1` = falsche Benutzung, `2` = Fehler (Datei, Format).
Warnungen erscheinen auf stderr mit dem Präfix `Warning:`.

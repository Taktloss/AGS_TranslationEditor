# Grafik-Editor: Text in Bildern übersetzen

Manche Texte stehen nicht in den Spieldaten, sondern sind in Bilder gemalt: Briefe, Karten,
Menüs („Speichern“, „Abbrechen“), Credits. AGS speichert diese Bilder als **Sprites** in der
Datei `acsprset.spr` im Spielpaket. Schilder, Plakate und Aufschriften stehen oft im **Hintergrund
eines Raums** (`roomN.crm`). Der Grafik-Editor zeigt beides an, lässt es ändern und baut daraus einen
**Patch**, der wie eine Schrift neben das Spiel gelegt wird. Das Spielpaket selbst bleibt unverändert.

Start: im Translator über *Tools → Grafik-Editor...* (öffnet das zuletzt gewählte Spiel) oder
eigenständig mit `AGSFontEditor.App --images [spiel]`.

## Inhalt

1. [Bilder finden](#bilder-finden)
2. [Im Editor ändern](#im-editor-ändern)
3. [Mit einem Malprogramm ändern](#mit-einem-malprogramm-ändern)
4. [Raumhintergründe](#raumhintergründe)
5. [Patch erstellen und installieren](#patch-erstellen-und-installieren)
6. [Kommandozeile](#kommandozeile)
7. [Gut zu wissen](#gut-zu-wissen)

## Bilder finden

Links stehen alle Sprites mit Vorschaubild, Nummer und Größe. Das Filterfeld nimmt

- eine Nummer (`273`), einen Bereich (`250-300`),
- eine Mindestgröße (`300x200`) – Briefe, Karten und Menüs sind meist groß,
- oder einen Namen: `gMenu`, `newgame_btn`, `button` oder `room22`.

**Wo steckt Text?** Unter jedem Bild steht in Blau, wo das Spiel es zeigt: als **GUI-Hintergrund**
(`GUI gNotebook: background`), als **Knopf** (`GUI button newgame_btn`, auch die Bilder für
Maus darüber und gedrückt) oder als **Raumobjekt** (`room22: object …`). Gemalte Wörter sind
fast immer in solchen Bildern: Menüpunkte, Notizbücher, Briefe, Schilder. Das Häkchen *Nur Bilder
aus GUIs und Raumobjekten* zeigt nur diese. In Blackwell Legacy bleiben so mit dem Filter `button`
56 Bilder übrig, ganz oben die Menüpunkte „New Game“, „Load Game“, „Quit“ und „Options“. Bilder,
die ein Skript erst beim Spielen anzeigt, findet diese Suche nicht.

*Kleine Sprites ausblenden* blendet alles unter 32 × 32 Pixel aus, *Nur geänderte Bilder* zeigt,
was du schon bearbeitet hast. Mit *Liste als PNG speichern...* kannst du alle gefilterten Bilder
auf einmal exportieren und in einem Bildbetrachter durchsehen.

## Im Editor ändern

Für kurze Texte (Knöpfe, Überschriften, Schilder) reicht der Editor selbst:

1. **Bereich wählen:** mit gedrückter linker Maustaste über den alten Text ziehen. Rechtsklick hebt
   die Auswahl auf.
2. **Farbe aufnehmen:** Strg+Klick auf den Hintergrund neben dem Text.
3. **Auswahl mit Farbe füllen** übermalt den alten Text.
4. **Text schreiben:** Text eingeben (mehrere Zeilen möglich), Schrift und Ausrichtung wählen,
   Textfarbe mit Strg+Klick aus dem Bild nehmen oder als `#RRGGBB` eintippen, bei Bedarf einen
   Umriss einschalten, dann *Text schreiben*. Der Text sitzt senkrecht mittig in der Auswahl.

Geschrieben wird mit den Schriften des Spiels, Pixel für Pixel wie im Spiel. Haben die
Originalschriften keine Umlaute, lade mit *Schriftdatei...* deine eingedeutschte `agsfntN.wfn`
oder `.ttf`. Die Statuszeile meldet Zeichen, die der Schrift fehlen.

*Rückgängig* (Strg+Z) nimmt den letzten Schritt zurück, *Zurück zum Original* verwirft alle
Änderungen am Bild. *Original zeigen* blendet zum Vergleich das unveränderte Bild ein.

## Mit einem Malprogramm ändern

Für Briefe, Karten oder Text mit Verläufen ist ein Malprogramm (GIMP, Krita, ...) besser:

1. *Bild als PNG speichern...* (oder *Liste als PNG speichern...*). Die Dateien heißen
   `sprite00273.png`.
2. Im Malprogramm bearbeiten und als PNG speichern. Den Dateinamen beibehalten; die Größe sollte
   gleich bleiben, sonst sitzt das Bild im Spiel womöglich anders.
3. *Durch PNG ersetzen...* für ein Bild, oder *PNG-Ordner laden...* für einen ganzen Ordner.
   Dabei werden nur Bilder übernommen, die sich wirklich vom Spiel unterscheiden – du kannst also
   alles exportieren, einige Bilder ändern und den ganzen Ordner wieder laden.

Durchsichtige Pixel bleiben durchsichtig: Bei Bildern ohne Alphakanal wird alles mit weniger als
halber Deckkraft zur Transparenzfarbe des Spiels (Magenta); Bilder mit Alphakanal behalten ihre
Deckkraft.

## Raumhintergründe

Oben in der Liste von *Sprites* auf **Raumhintergründe** umschalten. Die Liste zeigt dann jeden Raum
mit einem blauen **RAUM**-Abzeichen, darunter Hintergrundnummer und Größe; über dem Bild steht
**RAUMHINTERGRUND**. So verwechselt man Räume nicht mit Sprites. Räume mit wechselnden Hintergründen
(z.B. Blitze, Tag und Nacht) haben bis zu fünf Hintergründe; jeder lässt sich einzeln bearbeiten.
Das Filterfeld nimmt eine Raumnummer (`75`), einen Bereich (`10-20`) oder eine Mindestgröße.

Bearbeitet wird genau wie bei Sprites: im Editor (Bereich füllen, Text schreiben) oder im
Malprogramm. Exportierte Hintergründe heißen `room075-bg1.png` (`bg1` ist der Haupthintergrund).

- **Die Größe muss gleich bleiben.** Begehbare Bereiche, Hotspots und Regionen des Raums sind Masken
  genau dieser Größe. Ein PNG mit anderer Größe wird abgelehnt.
- Hintergründe sind nicht durchsichtig; Magenta ist hier eine normale Farbe.
- Bei 8-Bit-Räumen (256 Farben) wird jede Farbe auf die nächste der Raumpalette gesetzt.

## Patch erstellen und installieren

*Patch erstellen...* schreibt in einen Ordner deiner Wahl (nicht den Spielordner):

- `acsprset.spr` – eine Kopie der Sprite-Datei des Spiels mit deinen geänderten Bildern
- `sprindex.dat` – das passende Inhaltsverzeichnis
- `roomN.crm` – für jeden geänderten Raum die Raumdatei mit den neuen Hintergründen. Alles andere
  (Skript, Hotspots, Masken) ist Byte für Byte das Original.
- `sprites/` und `rooms/` – deine geänderten Bilder als PNG, damit du später mit
  *PNG-Ordner laden...* weiterarbeiten kannst

Sind nur Sprites oder nur Räume geändert, entsteht nur der jeweilige Teil.

Installiert wird der Patch zusammen mit der Übersetzung, genau wie Schriften:

- im Translator: *Ins Spiel installieren...* bzw. *Spieler-Paket erstellen...* und bei der Frage nach
  den Schriften den Ordner mit dem Patch wählen (Schriften und Patch dürfen im selben Ordner liegen)
- oder auf der Kommandozeile: `AGSTranslate install <spiel> Deutsch.tra <patch-ordner>`

Die Engine nimmt Dateien im Spielordner vor denen im Spielpaket. *Deinstallieren* stellt den
alten Zustand wieder her (*Aus dem Spiel deinstallieren...* bzw. `AGSTranslate uninstall`).

## Kommandozeile

```
AGSTranslate sprites <spiel> [--used]                Sprites auflisten; --used: nur die auf GUIs
                                                     und als Raumobjekte, mit Verwendung
AGSTranslate sprites <spiel> <ordner> [--min 300x200] [--numbers 1,5,10-20]
                                                     Sprites als PNG exportieren
AGSTranslate rooms <spiel> [ordner] [--numbers 1,5,10-20]
                                                     Raumhintergründe auflisten bzw. als PNG exportieren
AGSTranslate spritepatch <spiel> <png-ordner> <ausgabe>
                                                     Patch aus den geänderten PNGs bauen (Sprites und Räume)
```

Siehe auch die [Kommandozeilen-Anleitung](CLI.md#sprites--spritepatch--bilder-exportieren-und-patchen).

## Gut zu wissen

- **Ältere Spiele:** Ansehen, Suchen, Exportieren und Patchen geht mit allen Sprite-Dateien (Version 4
  bis 12, AGS 2.x bis 3.6). Der Patch behält das Format der Sprite-Datei des Spiels, damit dessen
  eigene Engine ihn lesen kann. Im echten Spiel geprüft mit The Excavation of Hob's Barrow (AGS 3.5,
  Windows-Version unter Wine): 690 eingefärbte Bilder waren im Hauptmenü zu sehen. Für AGS 2.x ist das
  Format an zwölf Freeware-Spielen nachgemessen, aber nicht im laufenden Spiel getestet.
- **Größe:** Der Patch ist so groß wie die Sprite-Datei des Spiels (bei Blackwell Epiphany etwa
  180 MB, bei Unavowed etwa 1,5 GB), denn er enthält auch alle unveränderten Bilder.
- **Spiel-Updates:** Nach einem Update des Spiels den Patch neu bauen (*PNG-Ordner laden...* mit
  dem Patch-Ordner, dann *Patch erstellen...*). Bei Räumen ist das besonders wichtig: Eine
  `roomN.crm` enthält auch das Raumskript der alten Version und passt nicht zu einem neueren Spiel.
- **Im Spiel getestet:** Lesen und Zurückschreiben der Räume ist an allen 240 Räumen von
  Blackwell Legacy, Epiphany und Unavowed geprüft. Im laufenden Blackwell Legacy (AGS 3.6.1) nimmt die
  Engine die losen Dateien im Spielordner vor denen im Spielpaket: ein geänderter Menü-Hintergrund
  (`room22.crm`) und ein geänderter Menüpunkt (Sprite 298 in `acsprset.spr`/`sprindex.dat`) waren
  zu sehen, und das Spiel lief normal weiter. Ausprobieren ohne den Spielordner zu ändern:
  `ags-test.sh -t Deutsch.tra -f patch/acsprset.spr -f patch/sprindex.dat -f patch/room22.crm <Spielordner>`.
- **Rechte:** Die Sprite-Datei enthält alle Bilder des Spiels, eine Raumdatei den ganzen Raum. Gib
  einen Patch nur weiter, wenn die Rechteinhaber das erlauben.

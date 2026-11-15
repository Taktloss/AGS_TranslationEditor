# Grafik-Editor: Text in Bildern übersetzen

Manche Texte stehen nicht in den Spieldaten, sondern sind in Bilder gemalt: Briefe, Karten,
Menüs („Speichern“, „Abbrechen“), Credits. AGS speichert diese Bilder als **Sprites** in der
Datei `acsprset.spr` im Spielpaket. Der Grafik-Editor zeigt sie an, lässt sie ändern und baut
daraus einen **Sprite-Patch**, der wie eine Schrift neben das Spiel gelegt wird. Das Spielpaket
selbst bleibt unverändert.

Start: im Translator über *Tools → Grafik-Editor...* (öffnet das zuletzt gewählte Spiel) oder
eigenständig mit `AGSFontEditor.App --images [spiel]`.

## Inhalt

1. [Bilder finden](#bilder-finden)
2. [Im Editor ändern](#im-editor-ändern)
3. [Mit einem Malprogramm ändern](#mit-einem-malprogramm-ändern)
4. [Sprite-Patch erstellen und installieren](#sprite-patch-erstellen-und-installieren)
5. [Kommandozeile](#kommandozeile)
6. [Gut zu wissen](#gut-zu-wissen)

## Bilder finden

Links stehen alle Sprites mit Vorschaubild, Nummer und Größe. Das Filterfeld nimmt

- eine Nummer (`273`), einen Bereich (`250-300`)
- oder eine Mindestgröße (`300x200`) – Briefe, Karten und Menüs sind meist groß.

*Kleine Sprites ausblenden* blendet alles unter 32 × 32 Pixel aus, *Nur geänderte Sprites* zeigt,
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

## Sprite-Patch erstellen und installieren

*Sprite-Patch erstellen...* schreibt in einen Ordner deiner Wahl (nicht den Spielordner):

- `acsprset.spr` – eine Kopie der Sprite-Datei des Spiels mit deinen geänderten Bildern
- `sprindex.dat` – das passende Inhaltsverzeichnis
- `sprites/` – deine geänderten Bilder als PNG, damit du später mit *PNG-Ordner laden...*
  weiterarbeiten kannst

Installiert wird der Patch zusammen mit der Übersetzung, genau wie Schriften:

- im Translator: *Ins Spiel installieren...* bzw. *Spieler-Paket erstellen...* und bei der Frage nach
  den Schriften den Ordner mit dem Patch wählen (Schriften und Patch dürfen im selben Ordner liegen)
- oder auf der Kommandozeile: `AGSTranslate install <spiel> Deutsch.tra <patch-ordner>`

Die Engine nimmt Dateien im Spielordner vor denen im Spielpaket. *Deinstallieren* stellt den
alten Zustand wieder her (*Aus dem Spiel deinstallieren...* bzw. `AGSTranslate uninstall`).

## Kommandozeile

```
AGSTranslate sprites <spiel>                         Sprites auflisten
AGSTranslate sprites <spiel> <ordner> [--min 300x200] [--numbers 1,5,10-20]
                                                     Sprites als PNG exportieren
AGSTranslate spritepatch <spiel> <png-ordner> <ausgabe>
                                                     Sprite-Patch aus den geänderten PNGs bauen
```

Siehe auch die [Kommandozeilen-Anleitung](CLI.md#sprites--spritepatch--bilder-exportieren-und-patchen).

## Gut zu wissen

- **Nur AGS 3.6 und neuer:** gelesen wird die Sprite-Datei in Version 12, wie sie AGS 3.6 schreibt.
  Bei älteren Spielen meldet der Editor, dass das Format nicht unterstützt wird.
- **Größe:** Der Patch ist so groß wie die Sprite-Datei des Spiels (bei Blackwell Epiphany etwa
  180 MB, bei Unavowed etwa 1,5 GB), denn er enthält auch alle unveränderten Bilder.
- **Spiel-Updates:** Nach einem Update des Spiels den Patch neu bauen (*PNG-Ordner laden...* mit
  dem `sprites`-Ordner, dann *Sprite-Patch erstellen...*), sonst zeigt das Spiel alte Bilder.
- **Hintergründe von Räumen** sind keine Sprites, sondern stecken in den Raumdateien (`roomN.crm`);
  die kann der Grafik-Editor nicht ändern.
- **Rechte:** Die Sprite-Datei enthält alle Bilder des Spiels. Gib einen Patch nur weiter, wenn die
  Rechteinhaber das erlauben.

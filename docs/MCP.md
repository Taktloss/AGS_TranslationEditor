# AGSTranslate als MCP-Server

`AGSTranslate mcp` startet einen Server für das [Model Context Protocol](https://modelcontextprotocol.io).
Damit können KI-Programme wie **Claude Code**, **Claude Desktop** oder andere MCP-Clients selbst mit
einem AGS-Spiel arbeiten: Texte extrahieren, die TRS seitenweise lesen, Übersetzungen eintragen, gegen
das Spiel prüfen und die TRA erstellen. Man kann dem Programm dann z.B. sagen: *„Übersetze die leeren
Einträge in Deutsch.trs, prüfe danach gegen das Spiel und korrigiere zu lange Texte.“*

English version: [en/MCP.md](en/MCP.md)

## Einrichten

Zuerst AGSTranslate bauen oder als einzelne Datei veröffentlichen ([CLI.md](CLI.md#1-installation-und-aufruf)).
Im Folgenden steht `/pfad/zu/AGSTranslate` für die fertige Programmdatei; aus dem Quellcode geht auch
`dotnet /pfad/zu/AGSTranslate/bin/Release/net9.0/AGSTranslate.dll`.

**Claude Code:**

```bash
claude mcp add agstranslate -- /pfad/zu/AGSTranslate mcp
```

**Claude Desktop** (`claude_desktop_config.json`, über *Einstellungen → Entwickler → Konfiguration bearbeiten*):

```json
{
  "mcpServers": {
    "agstranslate": {
      "command": "/pfad/zu/AGSTranslate",
      "args": ["mcp"]
    }
  }
}
```

Andere Clients starten das Programm genauso: Befehl `AGSTranslate`, Argument `mcp`, Übertragung *stdio*.

## Werkzeuge

| Werkzeug | Was es tut |
|---|---|
| `open_game` | Spiel einmal einlesen und im Speicher halten, mit Überblick (Texte nach Herkunft, Räume, GUI-Texte, Fonts, Sprites) |
| `game_info` | Titel, Spiel-ID, AGS-Version und Kodierung |
| `extract_texts` | alle Texte des Spiels in eine neue TRS, dazu die Kontextdatei (wer sagt was) |
| `read_translation` | Einträge einer TRS/TRA lesen: seitenweise (`offset`, `limit`), gefiltert (`empty`, `translated`, Suchtext), mit Sprecher und Fundort |
| `write_translations` | Übersetzungen in eine TRS eintragen, Einträge per Originaltext oder Nummer |
| `check_translation` | Prüfung gegen das Spiel wie im Editor: Platzhalter, Voice-Präfixe, Umbrüche, Einheitlichkeit, Glossar, Namen, Textbreite in den GUI-Elementen; mit `spell: "de"` auch Rechtschreibung |
| `spellcheck` | Texte offline mit einem Hunspell-Wörterbuch prüfen: unbekannte Wörter mit Vorschlägen, ohne Platzhalter und Namen aus dem Original |
| `compile_tra` | TRA für das Spiel erstellen (mit Spiel-ID und Kodierung) |
| `list_fonts` | die Fonts des Spiels mit Größe, Kontur und Zeichenzahl |
| `measure_text` | Breite eines Textes in Pixeln in einem Spiel-Font, pro Zeile |
| `check_fit` | ob eine Formulierung in jeden Button/jedes Label passt, das den Eintrag zeigt, und ob die Platzhalter erhalten sind – zum Kürzen |
| `list_sprites` | die Grafiken (Sprites) eines Spiels mit Größe und Farbtiefe, filterbar nach Mindestgröße; mit `used_only` nur die Bilder auf GUIs und als Raumobjekte, mit Verwendung |
| `view_sprite` | eine Grafik als Bild anzeigen, z.B. um eingemalten Text zu lesen |
| `export_sprites` | Grafiken als `spriteNNNNN.png` in einen Ordner speichern |
| `list_rooms` | die Raumhintergründe mit Raum, Hintergrundnummer und Größe |
| `view_room` | einen Raumhintergrund als Bild anzeigen, z.B. um Schilder zu lesen |
| `export_rooms` | Raumhintergründe als `roomNNN-bgN.png` in einen Ordner speichern |
| `build_sprite_patch` | aus bearbeiteten PNGs den Patch bauen: `acsprset.spr` + `sprindex.dat` für Sprites, `roomN.crm` für Räume (wie im [Grafik-Editor](Grafik-Editor.md)) |

Das Spiel wird beim ersten Werkzeug, das es braucht, eingelesen und bleibt im Speicher, solange der
Server läuft – weitere Aufrufe (z.B. `check_translation` nach jeder Änderung) gehen dann ohne
Wartezeit. Ändern sich Dateien im Spielordner, liest der Server neu. `open_game` lädt vorab und gibt
einen Überblick.

`<spiel>` kann wie auf der Kommandozeile die EXE, die `.ags` oder `game28.dta` sein, hier zusätzlich
auch der Spielordner.

## Sicherheit

- Spieldateien werden **nur gelesen**. In den Spielordner schreibt kein Werkzeug: `extract_texts` und
  `compile_tra` lehnen Ziele dort ab, und ein Installieren gibt es absichtlich nicht – das bleibt bei
  `AGSTranslate install` bzw. dem Editor, die vorher sichern.
- `extract_texts` überschreibt keine vorhandene Datei, außer mit `overwrite: true`; `export_sprites`,
  `export_rooms` und `build_sprite_patch` schreiben ebenfalls nie in den Spielordner.
- `write_translations` ändert nur vorhandene Einträge, schreibt die Datei atomar und legt beim ersten
  Schreiben eine Kopie `<datei>.trs.bak` an.
- Der Server spricht nur über stdin/stdout mit dem Client, der ihn gestartet hat; er öffnet keinen Port
  und sendet nichts ins Internet. Was der Client (das KI-Programm) mit den Texten macht, hängt von
  diesem ab – bei Cloud-Modellen gehen die gelesenen Texte an dessen Anbieter.

## Hinweise für das Modell

Beim Start bekommt der Client eine kurze Anleitung mit: `[` ist ein Zeilenumbruch, `&12 ` am Anfang
ein Voice-Präfix, Platzhalter wie `%s` oder `@SCORE@` müssen bleiben. Es lohnt sich trotzdem, nach dem
Übersetzen `check_translation` laufen zu lassen.

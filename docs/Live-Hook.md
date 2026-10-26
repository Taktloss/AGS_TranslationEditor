# Live-Hook: wie der Live-Modus funktioniert

*English version: [en/Live-Hook.md](en/Live-Hook.md)*

Diese Doku erklärt, wie der Live-Modus technisch gebaut ist: wie sich `agslive.so` in die AGS-Engine
einklinkt, wie Spiel und Translator miteinander sprechen und wie das Ganze getestet wurde.
Die Bedienung steht in der [Anleitung, Kapitel 13](Anleitung.md#13-live-modus-übersetzung-beim-spielen-korrigieren-linux).

## Inhalt

1. [Die Idee](#1-die-idee)
2. [Die richtige Stelle: get_translation](#2-die-richtige-stelle-get_translation)
3. [Die Funktion im Spiel finden](#3-die-funktion-im-spiel-finden)
4. [Einklinken: Inline-Hook mit Trampolin](#4-einklinken-inline-hook-mit-trampolin)
5. [Was der Hook bei jedem Text tut](#5-was-der-hook-bei-jedem-text-tut)
6. [Verbindung zum Translator](#6-verbindung-zum-translator)
7. [Die Seite des Translators](#7-die-seite-des-translators)
8. [Wie es getestet wurde](#8-wie-es-getestet-wurde)
9. [Grenzen und Ausbau](#9-grenzen-und-ausbau)
10. [Dateien](#10-dateien)

---

## 1. Die Idee

Tools wie OptiScaler hängen sich in ein laufendes Spiel ein und ersetzen dort Funktionen. Für
Übersetzungen braucht man nur eine einzige Stelle: die, an der das Spiel zu einem Text die Übersetzung
holt. Hängt man sich dort ein, sieht man jeden Text, den das Spiel zeigt, und kann eine andere
Übersetzung zurückgeben.

```
 Spiel (ags64)                                 Translator
 ┌──────────────────────────────┐   Unix-     ┌──────────────────────────┐
 │ Skript: Say("Good bye...")   │   Socket    │ LiveServer (AGSTools)    │
 │   └► get_translation() ──────┼──"T ..."──► │   └► Live-Bereich        │
 │        ▲ umgeleitet auf      │             │                          │
 │        agslive.so            │ ◄─"S ..."───┼── Korrektur im Editor    │
 └──────────────────────────────┘             └──────────────────────────┘
```

Die Spieldateien werden nicht verändert. Der Hook lebt nur im Arbeitsspeicher des laufenden Spiels.

## 2. Die richtige Stelle: get_translation

AGS ist Open Source. Im Engine-Quellcode (`Engine/ac/global_translation.cpp`) läuft jeder Text, der
übersetzt werden kann, durch diese Funktion: Sprechtext, Hinweisboxen, Hotspot-Namen, Menüs,
Dialogoptionen.

```cpp
const char *get_translation(const char *text)
{
    if (!text)
        text = "";
    source_text_length = GetTextDisplayLength(text);   // Anzeigedauer von Sprechtext
    if (text[0] == 0)
        return "";
    // ... Plugin-Hook, dann Suche in der Übersetzungstabelle der TRA
    return text;   // nicht übersetzt: Original
}
```

Zwei Dinge daraus sind wichtig:

- Die Funktion bekommt **genau den Originaltext**, der in der TRS als Schlüssel steht, z.B.
  `&793 Good bye, Auntie.` mit Sprachausgabe-Präfix. Der Translator findet den Eintrag also direkt.
- Sie setzt nebenbei `source_text_length`. Davon hängt ab, wie lange ein Sprechtext stehen bleibt.
  Der Hook ruft deshalb **immer die Originalfunktion auf** und tauscht nur ihr Ergebnis aus.

Die Engine hat sogar einen eigenen Plugin-Hook für Übersetzungen (`kPluginEvt_TranslateText`). Plugins
lädt sie aber nur, wenn das Spiel sie in seinen Daten einträgt. Für fertige Spiele fällt das weg.

## 3. Die Funktion im Spiel finden

In der Linux-Engine von Blackwell Legacy (`ags64`, AGS 3.6.1.35) sind die Funktionsnamen exportiert,
weil Plugins sie brauchen:

```
$ nm -D --defined-only ags64 | grep translation
00000000005ae180 T _Z15get_translationPKc        ← const char *get_translation(const char *)
...
```

Der Hook findet die Adresse beim Start mit `dlsym(RTLD_DEFAULT, "_Z15get_translationPKc")`.

**Warum `LD_PRELOAD` allein nicht reicht:** Normalerweise ersetzt man mit `LD_PRELOAD` eine Funktion,
indem die eigene Bibliothek eine gleichnamige Funktion mitbringt. Das wirkt aber nur bei Aufrufen, die
über den dynamischen Linker laufen (PLT). `ags64` ist eine normale, nicht verschiebbare Programmdatei
(`readelf -h`: `Type: EXEC`); ihre internen Aufrufe springen direkt an die Adresse `0x5ae180`. Deshalb
muss der Hook den Anfang der Funktion selbst umschreiben.

## 4. Einklinken: Inline-Hook mit Trampolin

So sieht der Anfang der Funktion in Blackwell aus (`objdump -d -M intel`):

```
5ae180:  55                     push   rbp
5ae181:  53                     push   rbx
5ae182:  48 89 fb               mov    rbx,rdi
5ae185:  48 83 ec 28            sub    rsp,0x28
5ae189:  64 48 8b 04 25 28 00…  mov    rax,QWORD PTR fs:0x28
5ae192:  48 89 44 24 18         mov    QWORD PTR [rsp+0x18],rax
```

Der Hook überschreibt die ersten 14 Bytes mit einem Sprung auf die eigene Funktion:

```
FF 25 00 00 00 00   jmp qword ptr [rip+0]
xx xx xx xx xx xx xx xx   Adresse von hooked_get_translation
```

Diese Form braucht kein Register und erreicht jede 64-Bit-Adresse.

Damit die Originalfunktion trotzdem aufrufbar bleibt, entsteht ein **Trampolin**: ein kleiner
ausführbarer Speicherbereich mit den überschriebenen Befehlen und einem Sprung zurück hinter die
überschriebene Stelle.

```
Trampolin:  push rbp / push rbx / mov rbx,rdi / sub rsp,0x28 / mov rax,fs:0x28   (18 Bytes, unverändert kopiert)
            jmp 0x5ae192                                              (zurück in die Originalfunktion)
```

Ganze Befehle kopieren geht nur, wenn man weiß, wie lang jeder Befehl ist und dass er an einer anderen
Adresse genauso funktioniert. Der Hook hat dafür einen kleinen Decoder (`prologue_length`), der **nur
eine feste Liste einfacher Prolog-Befehle** kennt: `endbr64`, `push`, `mov`/`xor`/`test` zwischen
Registern, `add`/`sub` mit Konstante und das Laden des Stack-Schutzwerts aus `fs:`. Keiner davon
verweist relativ auf seine eigene Adresse. Taucht etwas anderes auf, verändert der Hook nichts und
meldet `agslive: unknown instructions at the start of get_translation` mit den ersten Bytes.

Der Ablauf beim Laden (`agslive_init`, eine Konstruktorfunktion, die vor `main` läuft, also bevor die
Engine irgendetwas tut):

1. Adresse mit `dlsym` holen.
2. Länge der zu verschiebenden Befehle bestimmen (mindestens 14 Bytes; in Blackwell 18).
3. Trampolin mit `mmap` anlegen, Befehle hineinkopieren, Rücksprung anhängen.
4. Die Codeseite mit `mprotect` kurz beschreibbar machen, den Sprung schreiben, wieder schützen.
5. Den Verbindungs-Thread starten.

## 5. Was der Hook bei jedem Text tut

Vereinfacht (ohne die Sperre für die Korrekturtabelle):

```c
static const char *hooked_get_translation(const char *text)
{
    const char *shown = original(text);          // Original über das Trampolin (setzt die Anzeigedauer)
    if (!text || !*text)
        return shown;
    const char *corrected = get_correction(text);  // Korrektur vom Translator?
    if (corrected && *corrected)
        shown = corrected;
    if (sock >= 0)
        report(text, shown);                     // an den Translator melden
    return shown;
}
```

Dabei gelten ein paar Regeln, damit das Spiel nie leidet:

- **Nie blockieren:** Gesendet wird mit `MSG_DONTWAIT`. Kommt der Translator nicht hinterher, gehen
  Meldungen verloren, das Spiel läuft weiter.
- **Nicht fluten:** Manche Texte holt das Spiel in jedem Bild neu, z.B. den Hotspot-Namen unter der Maus.
  Dieselbe Kombination aus Original und gezeigtem Text meldet der Hook höchstens alle 2 Sekunden.
- **Speicher nie freigeben:** Die Engine kann den zurückgegebenen Zeiger länger behalten. Alte
  Korrekturen bleiben deshalb im Speicher, auch wenn sie ersetzt werden.
- **Threads:** Die Korrekturtabelle (eine Hashtabelle) ist mit einem Mutex geschützt, weil sie der
  Verbindungs-Thread füllt und der Spiel-Thread liest.

## 6. Verbindung zum Translator

Der Translator öffnet einen **Unix-Socket** unter `$XDG_RUNTIME_DIR/agslive.sock` (änderbar mit
`AGSLIVE_SOCKET`). Der Hook verbindet sich in einem eigenen Thread und versucht es jede Sekunde neu. So
ist die Startreihenfolge egal, und ein neu gestarteter Translator wird automatisch gefunden.

Das Protokoll ist zeilenbasiert, die Felder sind durch Tabs getrennt:

| Richtung | Nachricht | Bedeutung |
|---|---|---|
| Spiel → Translator | `H <pid>` | Hallo nach dem Verbinden |
| Spiel → Translator | `T <original> <gezeigt>` | ein Text wurde angezeigt |
| Translator → Spiel | `S <original> <korrektur>` | diesen Text statt der Übersetzung zeigen; leer = wieder die TRA |
| Translator → Spiel | `C` | alle Korrekturen verwerfen |

- Backslash, Zeilenumbruch, Tab und CR werden als `\\`, `\n`, `\t`, `\r` geschrieben. Ein echtes Tab
  trennt also immer Felder.
- Die Texte werden als **Bytes in der Kodierung des Spiels** übertragen (bei Blackwell Codepage 1252),
  genau so, wie sie in der Engine liegen. Umwandeln muss nur der Translator.
- Unix-Socket-Pfade dürfen höchstens 107 Zeichen lang sein. Ein zu langer `AGSLIVE_SOCKET` wird mit
  einer Warnung gemeldet.

## 7. Die Seite des Translators

**`AGSTools/Live/LiveServer.cs`** (ohne Oberfläche, mit Tests):

- Wartet auf dem Socket. Ein neu gestartetes Spiel ersetzt das vorherige.
- Merkt sich alle Korrekturen und **schickt sie beim Verbinden erneut**, z.B. nach einem Neustart des
  Spiels.
- Ist der Socket von einem abgestürzten Translator übrig, wird er ersetzt. Wartet dort noch ein
  laufender Translator, kommt „Another editor already waits for the game“.

**`LiveViewModel` im Translator:**

- **Schlüssel zuordnen:** Das Spiel liefert Laufzeittext (z.B. mit echtem Zeilenumbruch), die TRS
  enthält die geschriebene Form (`\n`). `TextEscaping.ToTrs` wandelt um, dann wird der Eintrag gesucht.
- **Doppelte Übersetzung ausblenden:** Manche Texte laufen zweimal durch `get_translation`. Bei den
  Hinweisboxen von Blackwell kommt erst das Original, dann noch einmal der schon deutsche Text. Ein Text,
  der kein Schlüssel ist, aber als Übersetzung vorkommt, wird deshalb ignoriert.
- **Sprachausgabe erhalten:** Fehlt der Korrektur das Präfix `&N `, ergänzt es der Translator
  (`EnsureVoicePrefix`). Sonst spielt das Spiel keine Sprachausgabe.
- **Leere Übersetzung** schickt das Original, so wie es die neu erstellte TRA zeigen würde.
- **Jede Änderung zählt:** Der Translator beobachtet alle Einträge. Änderungen im Editor, in der Tabelle
  und per *Translate* gehen ans Spiel.
- **Automatisch speichern:** 1,5 Sekunden nach der letzten Korrektur wird die TRS gespeichert.
- Die Liste zeigt höchstens 300 Texte, den neuesten oben. Den gerade ausgewählten Text verschiebt sie
  nicht, damit er beim Bearbeiten nicht wegspringt.

## 8. Wie es getestet wurde

**Unit-Tests** (`AGSTools.Tests/LiveServerTests.cs`): Ein Test-Client spielt das Spiel. Geprüft wird:
Texte in Codepage 1252 empfangen (mit Umlauten und Escapes), Korrekturen senden und nach einem
Neuverbinden erneut senden, Korrektur entfernen, und dass ein zweiter Translator abgelehnt wird.

**Im echten Spiel** (Blackwell Legacy, Linux-Engine, virtueller Bildschirm mit Xvfb, Klicks mit
`xdotool`, Screenshots):

1. **Hook allein:** Ein kleines Python-Skript spielte den Translator und gab alle Meldungen aus. Ergebnis:
   Jeder Sprechtext kam mit Original und deutscher Übersetzung an, z.B. `&793 Good bye, Auntie.` →
   `&793 Lebewohl, Tantchen.`
2. **Korrektur im Spiel:** Das Python-Skript schickte beim Verbinden Korrekturen für zwei Zeilen, die
   später kommen. Im Screenshot sagte Rosa „LIVE-KORREKTUR: Änderung übernommen!“, inklusive Umlauten.
3. **Gesamtablauf mit dem Translator:** Translator mit einer Kopie der TRS, Live an, Spiel mit
   `ags-test.sh --live`. Die Hinweisbox erschien im Live-Bereich (ohne den doppelten Durchlauf). Während
   die Hinweisbox das Spiel anhielt, wurde `&793` im Translator geändert. Die TRS war 1,5 Sekunden später
   gespeichert, und im Spiel erschien „Mach's gut, Tante. (LIVE geändert)“.

Dabei aufgefallen und gelöst:

| Beobachtung | Lösung |
|---|---|
| Hinweisboxen werden zweimal übersetzt | Translator ignoriert Texte, die nur als Übersetzung vorkommen |
| Hotspot-Namen kommen in jedem Bild | Hook meldet dieselbe Kombination höchstens alle 2 Sekunden |
| Korrektur ohne `&N ` würde die Sprachausgabe abschalten | Translator ergänzt das Präfix |
| Socket-Pfad im Testordner war zu lang (über 107 Zeichen) | Standardpfad unter `$XDG_RUNTIME_DIR`, Warnung bei zu langem Pfad |
| Anzeigedauer von Sprechtext hängt an der Originalfunktion | Hook ruft das Original immer auf |

## 9. Grenzen und Ausbau

- **Nur beim nächsten Anzeigen:** Ein Text, der schon auf dem Bildschirm steht, ändert sich nicht. Die
  Engine zeichnet ihn nicht neu.
- **Nur Linux, 64 Bit, exportierte Symbole.** Andere Engine-Builds mit anderem Funktionsanfang lehnt der
  Hook ab. Neue Befehle lassen sich in `prologue_length` ergänzen, wenn sie nicht relativ zur eigenen
  Adresse arbeiten.
- **Windows:** Dort steckt die Engine ohne Funktionsnamen in der EXE des Spiels. Man müsste
  `get_translation` über typische Bytefolgen suchen (je Engine-Version) und den Hook als Proxy-DLL laden,
  wie es OptiScaler macht.
- **Alternative:** eine eigene Engine mit eingebautem Live-Editor. AGS ist Open Source; wegen der Lizenz
  wäre das ein eigenes Projekt.

## 10. Dateien

| Datei | Inhalt |
|---|---|
| `AGSLive/agslive.c` | der Hook: Funktion finden, Trampolin, Korrekturtabelle, Socket-Verbindung |
| `AGSLive/Makefile` | baut `agslive.so` (`make -C AGSLive`) |
| `AGSTools/Live/LiveServer.cs` | Socket-Server und Protokoll auf Seite des Translators |
| `AGSTools.Tests/LiveServerTests.cs` | Tests des Protokolls |
| `AGS_TranslationEditor.Avalonia/ViewModels/LiveViewModel.cs` | Live-Bereich: Zuordnung, Korrekturen, Speichern |
| `AGS_TranslationEditor.Avalonia/Models/LiveItem.cs` | ein Eintrag im Live-Bereich |
| `tools/ags-test.sh` | `--live`: baut bzw. findet `agslive.so` und startet das Spiel mit `LD_PRELOAD` |

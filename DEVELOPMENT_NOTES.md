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

### AGS-Dateiformat-Unterstützung
- **CLIB-Container**: Versionen 6, 10, 20, 21, 30 (`.ags`-Archivformat)
- **SCOM-Bytecode**: Versionen `SCOMZ` (v90) und `SCOMY` (v89) – Script-Blöcke
- **TRA-Binärformat**: Lesen und Schreiben (Verschlüsselung mit „Avis Durgan"-Key)
- **TRS-Format**: Textbasiertes Quell-/Übersetzungsformat (Zeile 1 = Original, Zeile 2 = Übersetzung)
- **Room-Dateien** (`.crm`): Extraktion von Hotspot-/Objekt-Namen
- **game28.dta**: Struktureller Parser für Charakternamen, Inventar-Namen, Dialog-Optionen

---

## Technische Erkenntnisse (AGS Internals)

### TRA-Binärformat
```
Signatur:   "AGSTranslation\0" (15 Bytes)
Schlüssel:  "Avis Durgan" (11 Bytes, rotierend)
Verschl.:   byte += key[pos % 11]  (inkl. Null-Terminator)

Block 2 (GameID):   int32(2) + int32(blockSize) + int32(uid) + encrypted_title
Block 1 (Dict):     Paare aus [int32(len) + encrypted_bytes]
                    Abschluss: leeres Paar (int32(0) + int32(0))
```
- Der `GameUID` im TRA-Block muss mit der Spiel-UID übereinstimmen
- Das Spiel lädt eine `.tra`-Datei dessen Name der Sprache entspricht (z.B. `Deutsch1.tra`)
- Bestätigt durch `log.txt`: `"Translation initialized: Deutsch1"`

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

**Lösung**: `ExtractVoicePairs()` scannt alle SCOM-Blöcke nach dem Muster:
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

### Dialog-Optionen-Struktur (game28.dta)
```
TopicSize  = 4696 Bytes
SlotSize   = 150 Bytes
SlotsPerTopic = 30

Pro Slot:
  [0..9]   = Metadaten-Header (Flags, reserviert) – immer < 0x20
  [10..]   = Null-terminierter Optionstext (max. ~139 Zeichen)
```
- Blackwell Epiphany: 64 Dialog-Topics, true section bei offset 3.747.097 in game28.dta
- Die konsistenten 10 Null-/Metadaten-Bytes am Anfang jedes Slots sind das wichtigste Erkennungsmerkmal zum Unterscheiden echter Dialog-Sektionen von False Positives

### CLIB-Container (AGS Archivformat)
Blackwell Epiphany: CLIB Version 30 (`CLIB\x1a` + Byte `0x1e`), 381 Assets
- `game28.dta`: offset 25.528.121 im `.ags`-Archiv, Größe 4.103.728 Bytes

---

## Bekannte Bugs / Offene Punkte

### In Arbeit: Dialog-Optionen fehlen in TRS-Extraktion
**Problem**: Texte wie „Chat with Joey", „Plan your next move", „End conversation" fehlen in der extrahierten `.trs`-Datei für Blackwell Epiphany.

**Ursache**: `FindAndExtractDialogs()` findet eine falsche Position im Datenstrom (False Positive), weil der Rückwärts-Suchalgorithmus auf echte Dialog-Texte trifft, die zufällig mit dem 4696-Byte-Raster übereinstimmen.

**Status**: Der Fix ist teilweise implementiert (Header-Bytes-Check: alle Bytes 0–9 müssen `< 0x20` sein). Es gibt noch einen verbleibenden False Positive bei Offset 3.771.027. Die korrekte Position ist 3.747.097 (verifiziert mit Python).

**Nächster Schritt**: Schwelle von „≥ checkCount - 1 Topics valide" auf „alle Topics valide" erhöhen, kombiniert mit dem Header-Clean-Check.

### Mögliche Verbesserungen (nicht kritisch)
- Unit-Tests für AGSTools (xUnit)
- Unavowed: Verifikation ob alle Dialog-Optionen korrekt extrahiert werden
- AppImage / .deb Packaging für Linux
- README verbessern: Screenshots, Installationsanleitung
- `Newtonsoft.Json` → `System.Text.Json` (optionale Abhängigkeit entfernen)
- PO-Header: Sprache als Parameter statt hart-codiert `"de"`

---

## Unterstützte Spiele (getestet)

| Spiel              | Publisher    | Status                                                      |
|--------------------|-------------|-------------------------------------------------------------|
| Blackwell Epiphany | Wadjet Eye  | ✅ Zeichennamen, Inventar ✅ | ⚠️ Dialog-Optionen fehlen noch |
| Unavowed           | Wadjet Eye  | ✅ Vollständig getestet, 11.202 `&N`-prefixe korrekt        |
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

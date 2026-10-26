# AGS Translation Editor #

### What can you do with AGS Translation Editor ? ###
* Read, edit, convert and create TRS and TRA Translation Files for AGS Games.
* Check a translation against the game: placeholders, voice prefixes, inconsistent translations, a glossary, and texts too wide for their GUI buttons (Tools > Check Translation, chapter 14 of the user guide).
* Install a translation into a game with backup and undo, or create a zip with install scripts for players (Tools > Install into Game / Create Player Package, chapter 15).

### Where do i get the script of a game ? ###
* Since release version 1.1 its possible to use a Script extractor that comes with the Editor Tools > Extract Script ;).

### TRS file format ###
A TRS file should look like this example

```
&1 Original Text1

&2 Original Text2
&2 Test translation
&3 Original Text3


```

### Download ###
* Ready-to-run builds for Windows, Linux and macOS (x64 and ARM64) are on the [Releases](https://github.com/Taktloss/AGS_TranslationEditor/releases) page. Each archive contains the translator, the font editor, the command line tool AGSTranslate and the user guide; no .NET installation is needed.
* Every push also builds all platforms; the archives are attached to the workflow run under *Actions*.
* New release: push a version tag, e.g. `git tag v2.0.0 && git push origin v2.0.0`. Tags with a suffix such as `v2.0.0-beta1` become pre-releases.

### Documentation ###
* German user guide: [docs/Anleitung.md](docs/Anleitung.md) (translation workflow, special characters, font editor, checklist)
* Command line tool AGSTranslate (German): [docs/CLI.md](docs/CLI.md)
* Test a translation in the game without touching the game folder (Linux): `tools/ags-test.sh --help`, described in chapter 12 of the user guide
* Live mode (Linux): see every text of the running game in the translator and correct it while playing (`ags-test.sh --live`, the *Live* button; usage in chapter 13 of the user guide, how the hook works in [docs/Live-Hook.md](docs/Live-Hook.md), German)

### Special characters ###
* Use an 8-bit translation if the game stores its texts as 8-bit and all characters fit its codepage (works on every OS); a UTF-8 translation of such a game only works with the Windows engine. Make sure the game fonts contain the characters: the TRA export warns about missing glyphs, and the AGS Font Editor (Tools > Font Editor) can add them.

# AGS Translation Editor #

### What can you do with AGS Translation Editor ? ###
* Read, edit, convert and create TRS and TRA Translation Files for AGS Games.

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

### Documentation ###
* German user guide: [docs/Anleitung.md](docs/Anleitung.md) (translation workflow, special characters, font editor, checklist)
* Command line tool AGSTranslate (German): [docs/CLI.md](docs/CLI.md)
* Test a translation in the game without touching the game folder (Linux): `tools/ags-test.sh --help`, described in chapter 12 of the user guide

### Special characters ###
* Use an 8-bit translation if the game stores its texts as 8-bit and all characters fit its codepage (works on every OS); a UTF-8 translation of such a game only works with the Windows engine. Make sure the game fonts contain the characters: the TRA export warns about missing glyphs, and the AGS Font Editor (Tools > Font Editor) can add them.

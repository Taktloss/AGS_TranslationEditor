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

### Special characters ###
* Use a UTF-8 translation (AGS 3.6+) and make sure the game fonts contain the characters: the TRA export warns about missing glyphs, and the AGS Font Editor (Tools > Font Editor) can add them.

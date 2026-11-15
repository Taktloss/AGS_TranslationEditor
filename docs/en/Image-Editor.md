# Image Editor: translating text in pictures

Some texts are not in the game data but painted into pictures: letters, maps, menus ("Save",
"Cancel"), credits. AGS stores these pictures as **sprites** in the file `acsprset.spr` inside the
game package. The image editor shows them, lets you change them and builds a **sprite patch** that
goes next to the game like a font. The game package itself stays unchanged.

Start: in the translator via *Tools → Image Editor...* (opens the last selected game) or standalone
with `AGSFontEditor.App --images [game]`.

## Contents

1. [Finding pictures](#finding-pictures)
2. [Changing them in the editor](#changing-them-in-the-editor)
3. [Changing them in a paint program](#changing-them-in-a-paint-program)
4. [Creating and installing a sprite patch](#creating-and-installing-a-sprite-patch)
5. [Command line](#command-line)
6. [Good to know](#good-to-know)

## Finding pictures

The list on the left shows all sprites with a thumbnail, number and size. The filter box takes

- a number (`273`), a range (`250-300`)
- or a minimum size (`300x200`): letters, maps and menus are usually large.

*Hide small sprites* hides everything below 32 × 32 pixels, *Only changed sprites* shows what you
have edited. *Save Listed as PNG...* exports all filtered pictures at once, so you can look through
them in an image viewer.

## Changing them in the editor

For short texts (buttons, headings, signs) the editor itself is enough:

1. **Select an area:** drag over the old text with the left mouse button. Right click removes the
   selection.
2. **Pick a color:** Ctrl+click on the background next to the text.
3. **Fill Selection with Color** paints over the old text.
4. **Write the text:** enter it (several lines are possible), choose font and alignment, pick the
   text color with Ctrl+click or type it as `#RRGGBB`, turn on an outline if needed, then *Write
   Text*. The text is centered vertically in the selection.

The text is drawn with the game's fonts, pixel for pixel as in the game. If the original fonts lack
characters of your language, load your own `agsfntN.wfn` or `.ttf` with *Font File...*. The status
bar names characters the font does not have.

*Undo* (Ctrl+Z) takes back the last step, *Back to Original* drops all changes of the picture.
*Show original* shows the unchanged picture for comparison.

## Changing them in a paint program

For letters, maps or text with gradients a paint program (GIMP, Krita, ...) works better:

1. *Save Image as PNG...* (or *Save Listed as PNG...*). The files are named `sprite00273.png`.
2. Edit them and save as PNG. Keep the file name; the size should stay the same, otherwise the game
   may place the picture differently.
3. *Replace with PNG...* for one picture, or *Load PNG Folder...* for a whole folder. Only pictures
   that really differ from the game's ones are taken, so you can export everything, change a few
   pictures and load the whole folder again.

Transparent pixels stay transparent: in pictures without an alpha channel, everything below half
opacity becomes the game's transparent color (magenta); pictures with an alpha channel keep their
opacity.

## Creating and installing a sprite patch

*Create Sprite Patch...* writes into a folder of your choice (not the game folder):

- `acsprset.spr`: a copy of the game's sprite file with your changed pictures
- `sprindex.dat`: the matching index
- `sprites/`: your changed pictures as PNG, so you can continue later with *Load PNG Folder...*

The patch is installed together with the translation, just like fonts:

- in the translator: *Install into Game...* or *Create Player Package...*, and when asked for fonts,
  choose the folder with the patch (fonts and patch may share a folder)
- or on the command line: `AGSTranslate install <game> German.tra <patch-folder>`

The engine prefers files in the game folder to those in the game package. *Uninstall from Game...* (or
`AGSTranslate uninstall`) restores the previous state.

## Command line

```
AGSTranslate sprites <game>                          List the sprites
AGSTranslate sprites <game> <folder> [--min 300x200] [--numbers 1,5,10-20]
                                                     Export sprites as PNG
AGSTranslate spritepatch <game> <png-folder> <out>   Build a sprite patch from the changed PNGs
```

See also the [command line guide](CLI.md#sprites--spritepatch--export-and-patch-pictures).

## Good to know

- **AGS 3.6 and later only:** the editor reads sprite files of version 12, as AGS 3.6 writes them.
  For older games it reports that the format is not supported.
- **Size:** the patch is as large as the game's sprite file (about 180 MB for Blackwell Epiphany,
  about 1.5 GB for Unavowed), because it also holds all unchanged pictures.
- **Game updates:** after an update of the game, build the patch again (*Load PNG Folder...* with
  the `sprites` folder, then *Create Sprite Patch...*), otherwise the game shows old pictures.
- **Room backgrounds** are not sprites but part of the room files (`roomN.crm`); the image editor
  cannot change them.
- **Rights:** the sprite file holds all pictures of the game. Only pass a patch on if the rights
  holders allow it.

# Image Editor: translating text in pictures

Some texts are not in the game data but painted into pictures: letters, maps, menus ("Save",
"Cancel"), credits. AGS stores these pictures as **sprites** in the file `acsprset.spr` inside the
game package. Signs, posters and labels are often in the **background of a room** (`roomN.crm`). The
image editor shows both, lets you change them and builds a **patch** that goes next to the game like
a font. The game package itself stays unchanged.

Start: in the translator via *Tools → Image Editor...* (opens the last selected game) or standalone
with `AGSFontEditor.App --images [game]`.

## Contents

1. [Finding pictures](#finding-pictures)
2. [Changing them in the editor](#changing-them-in-the-editor)
3. [Changing them in a paint program](#changing-them-in-a-paint-program)
4. [Room backgrounds](#room-backgrounds)
5. [Creating and installing a patch](#creating-and-installing-a-patch)
6. [Command line](#command-line)
7. [Good to know](#good-to-know)

## Finding pictures

The list on the left shows all sprites with a thumbnail, number and size. The filter box takes

- a number (`273`), a range (`250-300`),
- a minimum size (`300x200`): letters, maps and menus are usually large,
- or a name: `gMenu`, `newgame_btn`, `button` or `room22`.

**Where is text?** Below each picture it says in blue where the game shows it: as a **GUI background**
(`GUI gNotebook: background`), a **button** (`GUI button newgame_btn`, also the mouse-over and pushed
images) or a **room object** (`room22: object …`). Painted words are nearly always in such pictures:
menu items, notebooks, letters, signs. The checkbox *Only GUI and room object images* shows only these.
In Blackwell Legacy the filter `button` then leaves 56 pictures, at the top the menu items "New Game",
"Load Game", "Quit" and "Options". Pictures that a script shows only while playing are not found.

*Hide small sprites* hides everything below 32 × 32 pixels, *Only changed images* shows what you
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

## Room backgrounds

Signs, posters and labels are often painted into the **background of a room** (`roomN.crm`). At the
top of the list, switch from *Sprites* to **Room backgrounds**. Every room then shows a blue **ROOM**
badge with the background number and size below it, and the image header says **ROOM BACKGROUND**,
so rooms are not mistaken for sprites. Rooms with changing backgrounds (lightning, day and night)
have up to five; each can be edited on its own. The filter takes a room number (`75`), a range
(`10-20`) or a minimum size.

Editing works as for sprites: in the editor (fill an area, write text) or in a paint program.
Exported backgrounds are named `room075-bg1.png` (`bg1` is the main background).

- **The size must stay the same.** The room's walkable areas, hotspots and regions are masks of
  exactly that size; a PNG of another size is refused.
- Backgrounds are not transparent; magenta is an ordinary color here.
- In 8-bit rooms (256 colors) every color is set to the nearest one of the room palette.

## Creating and installing a patch

*Create Patch...* writes into a folder of your choice (not the game folder):

- `acsprset.spr`: a copy of the game's sprite file with your changed pictures
- `sprindex.dat`: the matching index
- `roomN.crm`: for every changed room, the room file with the new backgrounds. Everything else
  (script, hotspots, masks) is the original, byte for byte.
- `sprites/` and `rooms/`: your changed pictures as PNG, so you can continue later with
  *Load PNG Folder...*

If only sprites or only rooms changed, only that part is written.

The patch is installed together with the translation, just like fonts:

- in the translator: *Install into Game...* or *Create Player Package...*, and when asked for fonts,
  choose the folder with the patch (fonts and patch may share a folder)
- or on the command line: `AGSTranslate install <game> German.tra <patch-folder>`

The engine prefers files in the game folder to those in the game package. *Uninstall from Game...* (or
`AGSTranslate uninstall`) restores the previous state.

## Command line

```
AGSTranslate sprites <game> [--used]                 List the sprites; --used: only those on GUIs and
                                                     as room objects, with where
AGSTranslate sprites <game> <folder> [--min 300x200] [--numbers 1,5,10-20]
                                                     Export sprites as PNG
AGSTranslate rooms <game> [folder] [--numbers 1,5,10-20]
                                                     List the room backgrounds, or export them as PNG
AGSTranslate spritepatch <game> <png-folder> <out>   Build the patch from the changed PNGs (sprites and rooms)
```

See also the [command line guide](CLI.md#sprites--spritepatch--export-and-patch-pictures).

## Good to know

- **Older games:** viewing, searching and exporting works with all sprite files (versions 4 to 12,
  AGS 2.x to 3.6; tested with The Excavation of Hob's Barrow, AGS 3.5). A **sprite patch** can only be
  built for games from AGS 3.6, because older engines cannot read the new sprite file; for older
  games the editor says so. Changed room backgrounds work there too.
- **Size:** the patch is as large as the game's sprite file (about 180 MB for Blackwell Epiphany,
  about 1.5 GB for Unavowed), because it also holds all unchanged pictures.
- **Game updates:** after an update of the game, build the patch again (*Load PNG Folder...* with
  the patch folder, then *Create Patch...*). This matters most for rooms: a `roomN.crm` also holds
  the room script of the old version and does not fit a newer game.
- **Tested in the game:** reading and writing rooms is checked on all 240 rooms of Blackwell Legacy,
  Epiphany and Unavowed. In the running Blackwell Legacy (AGS 3.6.1) the engine takes the loose files
  in the game folder over those in the game package: a changed menu background (`room22.crm`) and a
  changed menu item (sprite 298 in `acsprset.spr`/`sprindex.dat`) were shown, and the game ran on
  normally. To try it without changing the game folder:
  `ags-test.sh -t German.tra -f patch/acsprset.spr -f patch/sprindex.dat -f patch/room22.crm <game folder>`.
- **Rights:** the sprite file holds all pictures of the game, a room file the whole room. Only pass
  a patch on if the rights holders allow it.

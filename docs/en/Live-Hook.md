# Live hook: how live mode works

*Deutsche Fassung: [Live-Hook.md](../Live-Hook.md)*

This document explains how live mode is built technically: how `agslive.so` hooks into the AGS
engine, how the game and the translator talk to each other and how it was all tested.
Usage is described in the [user guide, chapter 13](UserGuide.md#13-live-mode-correcting-the-translation-while-playing-linux).

## Contents

1. [The idea](#1-the-idea)
2. [The right spot: get_translation](#2-the-right-spot-get_translation)
3. [Finding the function in the game](#3-finding-the-function-in-the-game)
4. [Hooking in: inline hook with a trampoline](#4-hooking-in-inline-hook-with-a-trampoline)
5. [What the hook does for every text](#5-what-the-hook-does-for-every-text)
6. [Connection to the translator](#6-connection-to-the-translator)
7. [The translator's side](#7-the-translators-side)
8. [How it was tested](#8-how-it-was-tested)
9. [Limits and extensions](#9-limits-and-extensions)
10. [Files](#10-files)

---

## 1. The idea

Tools like OptiScaler hook into a running game and replace functions there. For translations you only
need a single spot: the one where the game fetches the translation for a text. Hooking in there, you
see every text the game shows and can return a different translation.

```
 Game (ags64)                                  Translator
 ┌──────────────────────────────┐   Unix      ┌──────────────────────────┐
 │ script: Say("Good bye...")   │   socket    │ LiveServer (AGSTools)    │
 │   └► get_translation() ──────┼──"T ..."──► │   └► live panel          │
 │        ▲ redirected to       │             │                          │
 │        agslive.so            │ ◄─"S ..."───┼── correction in editor   │
 └──────────────────────────────┘             └──────────────────────────┘
```

The game files are not changed. The hook only lives in the memory of the running game.

## 2. The right spot: get_translation

AGS is open source. In the engine source (`Engine/ac/global_translation.cpp`) every text that can be
translated passes through this function: speech, message boxes, hotspot names, menus, dialog options.

```cpp
const char *get_translation(const char *text)
{
    if (!text)
        text = "";
    source_text_length = GetTextDisplayLength(text);   // display time of speech
    if (text[0] == 0)
        return "";
    // ... plugin hook, then lookup in the TRA's translation table
    return text;   // not translated: original
}
```

Two things about it are important:

- The function gets **exactly the original text** that is the key in the TRS, e.g.
  `&793 Good bye, Auntie.` with the voice prefix. So the translator finds the entry directly.
- It also sets `source_text_length`. How long a speech line stays on screen depends on it. The hook
  therefore **always calls the original function** and only swaps its result.

The engine even has its own plugin hook for translations (`kPluginEvt_TranslateText`). But it only
loads plugins that the game lists in its data. For finished games that is not an option.

## 3. Finding the function in the game

In Blackwell Legacy's Linux engine (`ags64`, AGS 3.6.1.35) the function names are exported because
plugins need them:

```
$ nm -D --defined-only ags64 | grep translation
00000000005ae180 T _Z15get_translationPKc        ← const char *get_translation(const char *)
...
```

The hook finds the address at startup with `dlsym(RTLD_DEFAULT, "_Z15get_translationPKc")`.

**Why `LD_PRELOAD` alone is not enough:** normally you replace a function with `LD_PRELOAD` by having
your own library provide a function of the same name. But that only affects calls that go through the
dynamic linker (PLT). `ags64` is a normal, non-relocatable executable (`readelf -h`: `Type: EXEC`);
its internal calls jump directly to the address `0x5ae180`. So the hook has to rewrite the start of the
function itself.

## 4. Hooking in: inline hook with a trampoline

This is what the start of the function looks like in Blackwell (`objdump -d -M intel`):

```
5ae180:  55                     push   rbp
5ae181:  53                     push   rbx
5ae182:  48 89 fb               mov    rbx,rdi
5ae185:  48 83 ec 28            sub    rsp,0x28
5ae189:  64 48 8b 04 25 28 00…  mov    rax,QWORD PTR fs:0x28
5ae192:  48 89 44 24 18         mov    QWORD PTR [rsp+0x18],rax
```

The hook overwrites the first 14 bytes with a jump to its own function:

```
FF 25 00 00 00 00   jmp qword ptr [rip+0]
xx xx xx xx xx xx xx xx   address of hooked_get_translation
```

This form needs no register and reaches any 64-bit address.

So that the original function can still be called, a **trampoline** is created: a small executable
memory area with the overwritten instructions and a jump back behind the overwritten spot.

```
Trampoline: push rbp / push rbx / mov rbx,rdi / sub rsp,0x28 / mov rax,fs:0x28   (18 bytes, copied unchanged)
            jmp 0x5ae192                                              (back into the original function)
```

Copying whole instructions only works if you know how long each instruction is and that it works the
same at a different address. For this the hook has a small decoder (`prologue_length`) that **only
knows a fixed list of simple prologue instructions**: `endbr64`, `push`, `mov`/`xor`/`test` between
registers, `add`/`sub` with a constant and loading the stack protector value from `fs:`. None of them
refers to its own address. If anything else shows up, the hook changes nothing and reports
`agslive: unknown instructions at the start of get_translation` with the first bytes.

The sequence when loading (`agslive_init`, a constructor function that runs before `main`, i.e.
before the engine does anything):

1. Get the address with `dlsym`.
2. Determine the length of the instructions to move (at least 14 bytes; 18 in Blackwell).
3. Create the trampoline with `mmap`, copy the instructions into it, append the jump back.
4. Briefly make the code page writable with `mprotect`, write the jump, protect it again.
5. Start the connection thread.

## 5. What the hook does for every text

Simplified (without the lock for the correction table):

```c
static const char *hooked_get_translation(const char *text)
{
    const char *shown = original(text);          // original via the trampoline (sets the display time)
    if (!text || !*text)
        return shown;
    const char *corrected = get_correction(text);  // correction from the translator?
    if (corrected && *corrected)
        shown = corrected;
    if (sock >= 0)
        report(text, shown);                     // report to the translator
    return shown;
}
```

A few rules make sure the game never suffers:

- **Never block:** sending uses `MSG_DONTWAIT`. If the translator can't keep up, messages are lost and
  the game keeps running.
- **Don't flood:** the game fetches some texts again every frame, e.g. the hotspot name under the
  mouse. The hook reports the same combination of original and shown text at most every 2 seconds.
- **Never free memory:** the engine may keep the returned pointer for a while. Old corrections
  therefore stay in memory even when they are replaced.
- **Threads:** the correction table (a hash table) is protected by a mutex, because the connection
  thread fills it and the game thread reads it.

## 6. Connection to the translator

The translator opens a **Unix socket** at `$XDG_RUNTIME_DIR/agslive.sock` (changeable with
`AGSLIVE_SOCKET`). The hook connects in its own thread and retries every second. So the start order
doesn't matter, and a newly started translator is found automatically.

The protocol is line-based; the fields are separated by tabs:

| Direction | Message | Meaning |
|---|---|---|
| game → translator | `H <pid>` | hello after connecting |
| game → translator | `T <original> <shown>` | a text was displayed |
| translator → game | `S <original> <correction>` | show this text instead of the translation; empty = back to the TRA |
| translator → game | `C` | discard all corrections |

- Backslash, line feed, tab and CR are written as `\\`, `\n`, `\t`, `\r`. So a real tab always
  separates fields.
- The texts are transferred as **bytes in the game's encoding** (codepage 1252 for Blackwell), exactly
  as they are in the engine. Only the translator has to convert.
- Unix socket paths may be at most 107 characters long. A `AGSLIVE_SOCKET` that is too long is
  reported with a warning.

## 7. The translator's side

**`AGSTools/Live/LiveServer.cs`** (no UI, with tests):

- Waits on the socket. A newly started game replaces the previous one.
- Remembers all corrections and **sends them again on connecting**, e.g. after the game was restarted.
- If the socket is left over from a crashed translator, it is replaced. If a running translator is
  still waiting there, you get "Another editor already waits for the game".

**`LiveViewModel` in the translator:**

- **Matching keys:** the game delivers runtime text (e.g. with a real line break), the TRS contains the
  written form (`\n`). `TextEscaping.ToTrs` converts it, then the entry is looked up.
- **Hiding double translations:** some texts pass through `get_translation` twice. With Blackwell's
  message boxes, first the original comes, then the already German text once more. A text that is not
  a key but appears as a translation is therefore ignored.
- **Keeping the voice acting:** if the correction lacks the prefix `&N `, the translator adds it
  (`EnsureVoicePrefix`). Otherwise the game plays no voice line.
- **An empty translation** sends the original, just as the newly created TRA would show it.
- **Every change counts:** the translator watches all entries. Changes in the editor, in the table and
  via *Translate* go to the game.
- **Saving automatically:** the TRS is saved 1.5 seconds after the last correction.
- The list shows at most 300 texts, the newest on top. It doesn't move the currently selected text, so
  it doesn't jump away while you edit it.

## 8. How it was tested

**Unit tests** (`AGSTools.Tests/LiveServerTests.cs`): a test client plays the game. Checked:
receiving texts in codepage 1252 (with umlauts and escapes), sending corrections and sending them
again after reconnecting, removing a correction, and that a second translator is refused.

**In the real game** (Blackwell Legacy, Linux engine, virtual screen with Xvfb, clicks with
`xdotool`, screenshots):

1. **Hook alone:** a small Python script played the translator and printed all messages. Result:
   every speech line arrived with the original and the German translation, e.g.
   `&793 Good bye, Auntie.` → `&793 Lebewohl, Tantchen.`
2. **Correction in the game:** on connecting, the Python script sent corrections for two lines that
   come later. In the screenshot Rosa said "LIVE-KORREKTUR: Änderung übernommen!", including umlauts.
3. **Complete workflow with the translator:** translator with a copy of the TRS, live on, game with
   `ags-test.sh --live`. The message box appeared in the live panel (without the double pass). While
   the message box paused the game, `&793` was changed in the translator. The TRS was saved 1.5
   seconds later, and the game showed "Mach's gut, Tante. (LIVE geändert)".

Noticed and solved along the way:

| Observation | Solution |
|---|---|
| Message boxes are translated twice | The translator ignores texts that only appear as a translation |
| Hotspot names arrive every frame | The hook reports the same combination at most every 2 seconds |
| A correction without `&N ` would turn off the voice line | The translator adds the prefix |
| The socket path in the test folder was too long (over 107 characters) | Default path under `$XDG_RUNTIME_DIR`, warning for a path that is too long |
| The display time of speech depends on the original function | The hook always calls the original |

## 9. Limits and extensions

- **Only on the next display:** a text that is already on screen doesn't change. The engine doesn't
  redraw it.
- **Linux only, 64-bit, exported symbols.** The hook refuses other engine builds with a different
  function start. New instructions can be added to `prologue_length` if they don't work relative to
  their own address.
- **Windows:** there the engine is inside the game's EXE without function names. You would have to
  find `get_translation` by typical byte patterns (per engine version) and load the hook as a proxy
  DLL, the way OptiScaler does.
- **Alternative:** an own engine with a built-in live editor. AGS is open source; because of the
  license that would be a separate project.

## 10. Files

| File | Content |
|---|---|
| `AGSLive/agslive.c` | the hook: finding the function, trampoline, correction table, socket connection |
| `AGSLive/Makefile` | builds `agslive.so` (`make -C AGSLive`) |
| `AGSTools/Live/LiveServer.cs` | socket server and protocol on the translator's side |
| `AGSTools.Tests/LiveServerTests.cs` | tests of the protocol |
| `AGS_TranslationEditor.Avalonia/ViewModels/LiveViewModel.cs` | live panel: matching, corrections, saving |
| `AGS_TranslationEditor.Avalonia/Models/LiveItem.cs` | one entry in the live panel |
| `tools/ags-test.sh` | `--live`: builds or finds `agslive.so` and starts the game with `LD_PRELOAD` |

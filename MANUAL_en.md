# PC-E500 Series Emulator Manual (PokeCom)

[日本語](MANUAL.md) · [README (overview)](README_en.md)

How to use the emulator and how it works, in detail. The overview and highlights are in [README_en.md](README_en.md). The illustrated user manual is at [app/manual/index_en.html](app/manual/index_en.html) (the Help button opens it). The UI is available in Japanese and English; pick the language in Settings.

Supported models: the PC-E500, PC-E550, PC-1480U, PC-1490U and PC-1490UII (256KB ROM), and the PC-E500S, PC-E650 and PC-U6000 (384KB ROM).

**ROM images are not included.** Dump the ROM from your own machine (see [Dumping the ROM](#dumping-the-rom-and-toolsmerge_savem_rompy)).

## Running

Releases on the [releases page](https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases) come as an installer (`PC-E500-Emulator-<version>-win-x64-setup.exe`) and a zip (`PC-E500-Emulator-<version>-win-x64.zip`). Neither is signed. From the release after 0.0.4 on, the release notes list each file’s SHA-256: compare it with what PowerShell’s `Get-FileHash <file>` prints to check a download.

- **Installer**: install for you only (no administrator; the default) or for all users. The program goes to `%LocalAppData%\Programs\PockeComE500\` (or `C:\Program Files\PockeComE500\` for all users); ROMs, RAM, settings and state go per user to `%LocalAppData%\PockeComE500\` either way. Uninstalling leaves them.
- **Zip**: run `PC-E500.exe` in the unpacked folder; the data stays beside it, as before.

Built from source, double-click `Start-PC-E500.cmd`, or open `app/PC-E500.exe` directly.

- Windows x64. The .NET runtime is bundled; no Python or developer tools are needed to run it.
- With the zip or a source build everything the program uses lives in the `app` folder, which can be copied elsewhere as a whole. Installed, the folders below are in `%LocalAppData%\PockeComE500\`.

| Folder | Contents |
|---|---|
| `app/rom/` | Internal ROM images, loaded at start-up (a ROM opened from elsewhere with Open ROM is opened again at the next start) |
| `app/ram/` | RAM card image files (default location for creating and attaching) |
| `app/drive-x/` | Default folder of the CE-140F (drive X:) |
| `app/state/` | Settings, controller presets (`controller.json`), internal RAM contents, automatic snapshots, pre-reset backups |
| `app/manual/` | The illustrated manual (`index.html` Japanese, `index_en.html` English) |

- ROMs are read from `app/rom/`. "Open ROM" accepts 256KB and 384KB images; a dump straight from the machine with its 16-byte `SAVEM` header (256KB+16) opens as it is. Verified to work with ROMs of the PC-E500, PC-E500S, PC-E550, PC-1480U, PC-1490UII, PC-E650 and PC-U6000.
- The system requirements (OS, CPU, memory and so on) are in [README_en.md](README_en.md#system-requirements). Tested on Windows 11 (x64); Windows 10 and Windows on ARM are untested.

### New versions and updating

![New version notice](app/manual/images-en/update.png)

At start-up, once a day at most, the emulator asks GitHub [Releases](https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases) whether a new version is out, and announces it on the toolbar and in the right-click menu. It never downloads or replaces anything. To update, quit the emulator and run the new `-setup.exe` if you installed it, or unpack the zip over the folder that holds `PC-E500.exe` now; your data stays either way.

The Start menu's "PC-E500 Emulator data folder", or "Open the data folder" in the right-click menu, opens the data folder.

The question goes to GitHub's public API (`api.github.com`) without an account, sending only the running version (as the User-Agent). Turn the automatic check off with "Check for a new version at start-up" in Settings; "Check for a new version..." in the right-click menu checks at any time.

## First steps

![Initialisation prompt](app/manual/images-en/lcd-new-card.png)

1. When `S1(MAIN): NEW CARD` appears (katakana on a Japanese ROM), press **PF1** (**F1** on the PC) to initialise memory. A freshly attached RAM card asks for the same. Large capacities take a few seconds.
2. Press **BASIC** (**F7**). **RUN** lights up at the top of the LCD.
3. Type `1+2` and **Enter**; the result `3` is displayed.

To enter a program, **press BASIC again to switch to PRO**:

```basic
10 PRINT 123
```

Enter registers the line; press **BASIC** to return to **RUN**, then type `RUN` and Enter to see `123`. Numbered lines typed in RUN mode give a Syntax error.

To print a string, type `PRINT "HELLO"` and Enter in RUN mode.

## Keys

Click the on-screen keys or use the PC keyboard. Letters are sent as upper case. Input goes through the key matrix just like the real machine.

The ROM scans the matrix at 64 Hz, so about 55 ms per key (18 keys a second) is the limit. While a PC key is down, the machine's key is held down, and it comes up when you let go, as on the real keys. Whether a held key repeats is the ROM's business: BS, DEL and the left and right arrows repeat; letters and digits type once. Keystrokes you actually made are never dropped.

| PC key | PC-E500 |
|---|---|
| Letters, digits, space, operators | Matching key |
| Enter / Backspace / Delete / Insert | ENTER / BS / DEL / INS |
| Arrows | Cursor |
| F1–F5 | Function keys |
| F6 | MENU |
| F7 | BASIC, RUN/PRO toggle |
| F8 | 2ndF |
| Esc | ON/BRK |
| Ctrl | CTRL |
| Shift | SHIFT |

Clicking **SHIFT** or **CTRL** on the screen holds it until the next key; click again to release. Symbols such as `"`, `:` and `$` are converted to SHIFT sequences following the 7.3 ROM's table. The ROM reads SHIFT from the previous matrix scan, so SHIFT is pressed one scan (about 23 ms) ahead of the character key and released one scan after. Japanese IME input is not supported.

## Display and window

### Status bar

The status bar along the bottom of the window is split into fields.

- **Left**: the machine's state (a green dot running, yellow paused, grey switched off). Click it to pause or resume.
- **Next to it**: messages such as Load program's, for five seconds, in place of the fields at the right. A failed autosave stays on show beside the fields.
- **Right**: the ROM version and family, internal RAM and the RAM card (`RAM 32KB+64KB` and the like), the machine-code area, the CE-140F, the CE-126P, sound, the controller (only while "Use a controller" is ticked; it is by default) and the speed (0.5x to 4x). Hover for the details (the ROM's size and file, the card's file name and so on). The memory field opens Settings, the ML field the window that sizes the machine-code area, the CE-140F and CE-126P fields open their windows, the sound field mutes or unmutes, the controller field opens the Controller window, and the speed field goes round 0.5x → 1x → 2x → 4x (use 1x when checking sound).

When the window is narrow (or the display is scaled up), the fields at the right switch to short forms ("off", "1x" and the like) one by one, CE-140F, CE-126P, controller, speed, sound, ML, RAM and ROM in that order; the icon tells which field it is, and hovering shows the full text. Only if that is still too wide do the leftmost fields go. The case-only and screen-only views have no status bar.

### Right-click menu

![Right-click menu](app/manual/images-en/context-menu.png)

Right-click anywhere on the case, the toolbar or the status line for a menu with the toolbar's commands, plus the speed, the window view, Always on top, Check for a new version..., Machine-code area..., Open the data folder, Minimize and Exit. In the views without a title bar and buttons, this menu is how you operate the emulator.

### Window views (double-click to switch)

Double-click the case away from the keys to step through **normal → case only → screen only → normal**. "Window" in the right-click menu and the Display window choose a view too.

| Case only | Screen only |
|---|---|
| ![Case-only view](app/manual/images-en/window-body.png) | ![Screen-only view](app/manual/images-en/window-screen.png) |
| No title bar or buttons, only the case (its corners are see-through) | Only the LCD |

- Switching keeps the LCD where it is and the same size on the screen; back in normal, the window returns to its earlier place and size.
- In the case-only and screen-only views, drag the picture to move the window, and drag its edge (the outline of the case, or the edge of the LCD) to resize it. The proportions are kept.
- The view, size and place carry over to the next start.
- A double-click on a key is still two presses.

### LCD look and contrast (Display)

| | |
|---|---|
| ![Display window](app/manual/images-en/display.png) | ![Real LCD](app/manual/images-en/lcd-dot-lines.png) |
| Display on the toolbar | The LCD in the real display |

- **LCD**: Standard packs the dots together; Real leaves a little space between them, as on the real LCD.
- **LCD contrast**: 0 to 100, 50 by default. As with the real unit's dial, the dots and the legends darken together: lower hides the legends that are off, higher shows the dots that are off, and 100 makes the whole glass black.
- **Afterimage (frame interpolation)**: a dot turning on or off changes over a few frames instead of at once (most of the way in about 0.01 s on, 0.02 s off; a dot going off is gone in about two frames). It is close to the slow response of the real LCD and keeps a picture caught half redrawn from flickering. Something that moves a dot at a time (a game's ship, scrolling stars) is drawn sliding smoothly from its last place to the next. The lines between the dots in the Real look do not move. A moving picture shows up to one step late. On by default. The screen is drawn once per refresh of the display (60 times a second, say), so movement runs evenly with or without it.
- Changes show at once and are saved in the settings.

## Copying and pasting screen text

![Text I/O](app/manual/images-en/text-transfer.png)

"Text I/O" on the toolbar, or **Ctrl+C**, shows the current LCD as 40 columns × 4 rows of text, recognised (normal and inverse) from the 6×8 font in the ROM. Drag to select and press "Copy selection"; with no selection the whole screen is copied. "Refresh" rereads the LCD. Graphics, double-size and shapes not in the font may come out as `□`.

"Paste from clipboard", or **Ctrl+V**, converts ASCII text to key presses: line breaks become ENTER and tabs four spaces. Lower-case letters are typed as upper case, characters not on the keyboard are skipped, up to 4096 characters at a time. Long BASIC listings are typed in order, so wait or raise the speed temporarily.

There is a small **RESET hole** between the BASIC and A keys, as on the real machine; the area around it is clickable too. There are two kinds of reset.

| Action | Effect |
|---|---|
| RESET hole | Restarts the CPU, LCD, keys and external bus only. Data survives |
| "All Reset" | The real ON+RESET. The ROM shows `S1(MAIN): CLEAR ALL DATA? (Y/N)`; Y erases everything, N resumes with the data intact |

All Reset merely enters the ROM's reset routine with ON/BRK held; the decision and the erase are the ROM's own code. The previous state is saved to `app/state/before-reset-<date>.e500state`. To wipe internal RAM, answer Y to an All Reset — the same procedure as on the real machine.

## Sub-windows

![Toolbar](app/manual/images-en/toolbar.png)

Settings, Display, CE-140F, CE-126P, Text I/O, Controller, Machine-code area and Debug are modeless: keys and toolbar buttons of the main window keep working while they are open, so you can watch the transfer log or the registers while the machine runs. Pressing the same button again brings the existing window forward; closing the main window closes them all. "Apply" in Settings reports a rejected value and keeps the window open so you can fix it.

The file choosers of Open ROM, Load program, Save state, Load state and BIN load/save are the standard Windows dialogs and therefore modal.

Every sub-window is built the same way: the content on cards with headings, and the buttons such as Close in a footer at the bottom right (the one Enter presses, in the accent colour, to the left of Close). Checks such as Auto refresh and Connected, and result messages, sit at the left of the footer.

The Settings, Controller, Text I/O and CE-126P windows cannot be made wider, as their content would not change; they can be made taller. Debug can be made narrower, but not wider than it opens.

## Game controller

![Controller window](app/manual/images-en/controller.png)

"Controller" on the toolbar opens it. Map the machine's keys to the buttons of a gamepad to play games and the like. XInput (Xbox-style) pads and Windows game controllers (DirectInput) work.

- **Use**: the pad to use. "Automatic (the first one found)" takes the first one found. After plugging a pad in, press "Search again" (a pad plugged in or out is also found on its own).
- **The pad picture**: the buttons you press light up, and it shows where the sticks are and how far the triggers are pressed. The "Pressed: …" line below it shows the buttons and the machine's keys. Click a button on the picture to go to its row.
- **Presets**: keep as many sets of mappings as you like. "Add..." copies the preset shown into a new one; "Rename..." and "Delete" are there too. Two come built in: "Number keys 2/4/6/8" (the default) and "Cursor keys".
- **Buttons**: choose the machine key for each button. "With" presses SHIFT or CTRL along with it. While the pad button is down, the machine's key is held down.
- **Match button places...**: on a DirectInput pad, the button numbers may not sit where the picture puts them. Then press the buttons at the places the picture shows, one by one ("Skip" and "Stop" are there too).
- **Use a controller**: untick it and the pad is not used.

"Apply" saves to `state/controller.json`. The pad works only while the main window is in front (when another window is in front, the keys it holds are let go).

## Debug (CPU registers, memory view and editing)

"Debug" on the toolbar, or **Ctrl+D**, opens a window on the CPU state. It follows the running machine and refreshes every 250 ms (untick "Auto refresh" to update only when you press "Refresh").

![Debug window](app/manual/images-en/debugger.png)

- **Memory map** (top): the internal RAM as a coloured bar (variables, program, PF keys and AER, free space, stacks, work areas, machine-code area, system), with a second bar for the RAM card when one is in. Click a region to take the memory view to its start; the marker under the bar is where the view is. Hover for the region's name, range and size.
- **CPU** (left): A, B, BA, I, IL, IH, X, Y, U, S, PC and F as tiles, the running / HALT / power-off / fault state as a coloured badge, the C and Z flags as lamps, and the cycle count. Below them, the 256 bytes of SC62015 internal RAM, with the BP, PX and PY pointers and the KOL–SSR I/O registers listed by name and address.
- **Memory** (right): `00000-FFFFF` as a hex dump, 16 or 8 bytes a row (the "16 bytes/row" and "8 bytes/row" choice on the toolbar; 16 by default), each group of eight followed by ": XX", its checksum (below). Move with the scrollbar, a hex address in the box (`B8000`, `&HB8000` or `0xB8000` and Enter), the mouse wheel, the arrow keys, PageUp/PageDown, Ctrl+Home/End, or the PC, S, X, Y, U, Internal RAM, Card and ROM buttons. The bottom line names the region of the visible range and of the caret.

```
B8010  00 20 18 00 00 00 00 00 : 38  - FB 44 41 54 41 20 20 20 : 75
```

Bytes are coloured by region.

| Colour | Region |
|---|---|
| Black | Internal RAM and RAM card with their mirrors (writable) |
| Blue | Internal ROM (CE2), E500S/E650/U6000 extension ROM (CE3) |
| Grey | Unmapped (card slot without a card, CE6, the internal area, …) |

![Boundary between internal RAM and ROM](app/manual/images-en/debugger-ram-rom.png)

### Hex editor

Click a byte to place the caret and type `0`–`9`, `A`–`F`. Digits go in high nibble first and the caret advances after two. Only internal RAM and the RAM card can be edited; typing over ROM or unmapped space changes nothing and the reason is shown at the bottom. Edits are autosaved like any other RAM change.

### Checksum of each eight bytes

After each ":" in the memory view is the low byte of the sum of the eight bytes before it (in blue). Machine code listings in magazines print this value for every eight bytes, so a program typed in can be checked line by line. It changes as you edit.

### Load BIN / Save BIN

| ![Load BIN](app/manual/images-en/bin-load.png) | ![Save BIN](app/manual/images-en/bin-save.png) |
|---|---|

- **Load BIN** writes a file into memory at the given address. The 16-byte header written by the CE-140F `SAVE M` is detected automatically and the data returns to the start address recorded in it (untick to load anywhere). The whole range must be RAM; if a single byte would land in ROM or unmapped space nothing is written.
- **Save BIN** writes the given length from the given address to a file, by default with the same 16-byte header as `SAVE M`, so `LOAD M` or Load BIN can put it back where it came from.

Memory reads bypass the LCD controller, so looking at `2000-3FFF` does not disturb the display.

### Machine-code area

![Machine-code area](app/manual/images-en/machine-code.png)

The ML field of the status bar (or Machine-code area... in the right-click menu) reserves RAM for machine code. The window shows the internal RAM as two bars, now and after applying, so you can see where the area goes and how much free space it takes. Set the size in hex bytes and press Apply: that many bytes below `BFBFF` are taken. 0 gives them back. The most you can take follows from the free RAM at the time.

The machine's own ROM routine does it (`POKE &HBFE03,...:CALL &HFFFD8` is typed for you). The size given is the size the area ends up, not an amount added. The ROM moves the other work areas and stacks down and ends at the main menu, and the BASIC key is then pressed to come back. The BASIC program and variables stay. Use it at BASIC's RUN or PRO prompt. The field shows the size now; hover for the address range.

Loading a BIN reserves nothing (as `LOAD M` on the machine). When Load program puts a BIN outside the machine-code area, you are asked whether to reserve it first (see "Loading a file" in the CE-140F chapter).

## Hardware settings and BEEP

Settings is split into Display, Machine, CE-140F, and Sound, power and more. It covers the UI language, the theme (as Windows, light or dark; a new language or theme restarts the emulator when applied), the appearance (PC-E500 / PC-E650), internal RAM size, RAM card creation/attach/detach, CE-140F connection, folder and write protection, volume and mute, the auto power off, and always on top. With "Always on top" ticked, the main window is never hidden under other windows (the right-click menu switches it too).

With "Auto power off" cleared, the machine no longer switches itself off when left without a key (the real one does after about 10 minutes): once a second of the machine's time, the count the ROM runs down is put back to its start, as a key press does.

Display covers the LCD look, its contrast and the window view. By default the LCD scales its dots without gaps (Standard); "Real" leaves a little space between them, as on the real LCD. The contrast works like the real unit's dial, on the dots and the legends together: lower hides the legends that are off, higher shows the dots and legends that are off, and at 100 the whole glass is black.

Right-click the case for a menu with the toolbar's commands (plus the speed, the window view, Always on top, Minimize and Exit). A double-click on the case away from the keys steps the window through Normal → Case only (no frame) → Screen only (the LCD alone) → Normal. Without the frame, drag the picture to move the window and its edge to resize it (keeping its proportions). The view, its size and its place are saved, and the next start opens in that view.

![Settings window](app/manual/images-en/settings.png)

Appearance switches the case shape and colour, LCD and key positions and the nameplate. The PC-U6000 shares the PC-E650 case, so only PC-E500 and PC-E650 are offered. Appearance does not change the ROM or RAM size; choose those separately.

### The 384KB PC-E500S / PC-E650 / PC-U6000 ROM

The E500S/E650/U6000 internal ROM is mapped as follows.

| In the file | CPU address | Size | Chip select |
|---|---:|---:|---|
| `00000-1FFFF` | `20000-3FFFF` | 128KB | CE3 |
| `20000-5FFFF` | `C0000-FFFFF` | 256KB | CE2 |

"Open ROM" tells 256KB PC-E500 images and 384KB PC-E500S/E650/U6000 images apart automatically; a 384KB image whose main ROM has model code 3 (the version's lower digit) is taken as a PC-E500S. The ROM field of the status bar shows the family (E500, E500S or E650), and its tip the size and the file. The 384KB file has the low-address extension ROM first and the 256KB main ROM after it.

The status bar also shows `ROM Ver.x.y`, read from the major/minor version bytes in the ROM's 16-byte trailer (the ones the service screen uses), not from the file name. The PC-E500 ROM used for testing is Ver.7.2, the PC-E650 ROM Ver.8.5.

### Dumping the ROM and `tools/merge_savem_rom.py`

On the real machine, `SAVEM "d:filename",start,end` writes memory to a file (`d:` is the device name of whatever is connected).

```basic
SAVEM "L:S3A.ROM",&HC0000,&HFFFFF
SAVEM "L:S3B.ROM",&H20000,&H3FFFF     ' PC-E500S / PC-E650 / PC-U6000 only
```

`SAVEM` output starts with a 16-byte header (`FF 00 06 01 10` + 3-byte length + 3-byte start address + `FF FF FF 00 0F`). A PC-E500 family `S3A.ROM` opens with the header in place. For the PC-E500S/E650/U6000, merge `S3A.ROM` and `S3B.ROM` into a 384KB image with `tools/merge_savem_rom.py` (Python 3).

```powershell
python tools/merge_savem_rom.py S3A.ROM S3B.ROM --info                              # inspect the headers
python tools/merge_savem_rom.py S3A.ROM S3B.ROM -o app/rom/PC-U6000-full.rom         # merge into 384KB
python tools/merge_savem_rom.py S3A.ROM -o "app/rom/PC-E500(7.2).ROM"                 # strip the header only (256KB); name it model(version).ROM
python tools/merge_savem_rom.py S3A.ROM S3B.ROM --strip                              # strip each file in place
python tools/merge_savem_rom.py S3B.ROM --main-rom PC-U6000.rom --layout full -o app/rom/PC-U6000-full.rom   # merge with a stripped main ROM
```

Coverage below 100% means the dump is incomplete. Chapter 13 of the manual walks through the whole procedure.

Stock internal RAM on the E650/U6000 is 64KB at `B0000-BFFFF`, and on the E500S 32KB at `B8000-BFFFF`, as on the E500; the RAM card sits at `40000` according to its size.

#### Internal RAM expansion (62256 → 628128)

Internal RAM sits directly below `C0000` and grows downwards. The real E500 and E500S have one 32KB SRAM (62256), the E650/U6000 two of them (64KB); replacing each with a 128KB 628128 is a well-known expansion mod, and the emulator offers the same result.

| Model | Stock | 628128×1 | 628128×2 |
|---|---:|---:|---:|
| PC-E500 / PC-E500S | 32KB (`B8000-BFFFF`) | 128KB (`A0000-BFFFF`) | — |
| PC-E650 / PC-U6000 | 64KB (`B0000-BFFFF`) | 128KB (`A0000-BFFFF`) | 256KB (`80000-BFFFF`) |

With an E650/U6000 384KB ROM the internal RAM choices are 64 / 128 / 256KB. The ROM itself measures the size at boot, so the choice shows up in BASIC (`PRINT FRE(0)` gives 61368, 126904 and 257976). 8/16/32KB do not exist on a real E650/U6000 and are not offered; such a stored setting starts as 64KB. The PC-E500S's 384KB ROM offers the same sizes as the PC-E500.

Reference: [E500 series memory expansion](https://www.oit.ac.jp/labs/rd/rssrv/kobayashi-lab/~yagshi/old_web/misc/pocketcom/memup/index.html)

- Internal RAM is **8 / 16 / 32 / 64 / 128 / 256 KB**, 32KB by default; **64 / 128 / 256 KB** (64KB by default) with an E650/U6000 384KB ROM; the PC-E500S is as the PC-E500.
- Changing the size saves the current contents and restarts. Contents are kept per size, so switching back restores what that size held.

### RAM cards (image files)

![Create RAM card](app/manual/images-en/card-size.png)

Each RAM card is an image file. The file size is the card capacity (**8 / 16 / 32 / 64 / 128 / 256 KB**) and its bytes are the card contents, so cards can be created and swapped like real ones.

| Action | Meaning |
|---|---|
| Create... | Choose a capacity and create a new image (all FF). The default location is `app/ram/`. The new card is attached |
| Attach... | Attach an existing image file |
| Detach | Empty the slot |

"Apply" performs the change after saving the current contents, then restarts. With a new card the ROM asks for PF1 as `NEW CARD`.

The card contents are written to its image file every 5 seconds, at power off, at exit and on detach. Copying the file makes a duplicate card. Card changes never affect internal RAM.

- In RUN mode, `BEEP 1` and Enter beeps. The CPU's XOUT waveform is sent to the Windows audio output. Check sound at 1x; audio is not in sync at other speeds.
- Audio is written to winmm (waveOut) from a dedicated thread, because `waveOutWrite` blocks its caller when restarting a stalled stream (91 ms on average, 1.4 s at worst here). Called on the thread that runs the CPU, it would stop the whole machine, so the UI thread only hands over the samples. The device always holds 42 ms (256 samples × 8); less than about 32 ms runs dry between frames and the stream restarts with audible gaps.
- Key click length and auto-repeat interval come from the SC62015 sub-timer (64 Hz), matched against a recording of a real PC-E500: 13.5 ms click, 62.5 ms repeat.
- Toolbar buttons never take focus: a focused button would swallow Space and Enter, which are pocket computer keys.

## CE-140F (drive X:)

Following `Sharp_ce140f_emul`, the 11-pin protocol (device select, ACK/BUSY, 4-bit transfer, checksum) is implemented with a PC folder as the virtual disk, `app/drive-x/` by default. The "CE-140F" window shows the transfer log (refreshed every 0.5 s) and opens the folder.

Enter a program in PRO mode, then save and load it like this.

```basic
SAVE "X:TEST.BAS"
SAVE "X:TEXT.BAS",A
LOAD "X:TEST.BAS"
```

Line 2 saves as ASCII. An ASCII file is saved without a closing 1Ah (EOF), as a hardware CE-140F emulator saves it; see [Exchanging files with a real machine](#exchanging-files-with-a-real-machine) before sending text to a real machine. Loading stops at a 1Ah if the file has one, so a file a PC or another tool ended with 1Ah loads too. To run a program you loaded, press BASIC to return to RUN and type `RUN`.

Sequential file I/O in RUN mode:

```basic
OPEN "X:DATA.DAT" FOR OUTPUT AS #2
PRINT #2,"HELLO"
CLOSE #2
OPEN "X:DATA.DAT" FOR INPUT AS #2
INPUT #2,A$
CLOSE #2
PRINT A$
```

FILES, COPY, NAME, SET, KILL, INIT and EOF/LOF/LOC/DSKF are implemented. File names are 8.3 with letters, digits, `_` and `-`, up to 1MB each; no subfolders. **INIT deletes the disk files in the folder, so give the CE-140F a folder of its own.** Sector images and drive mechanics are not reproduced.

![CE-140F window](app/manual/images-en/disk-trace.png)

### Loading a file (Load program, drag and drop)

Load program on the toolbar, or a file dropped on the window, loads a PC file into the machine. With "Run after loading (RUN for a BAS)" ticked at the bottom of the file window, a BAS is loaded with `LOAD "X:name",R` and runs at once. The tick is remembered, and a dropped file follows it too. Files load one at a time: the next cannot load until the keys the last one types (a machine-code area reservation, a `LOAD`) are done. Nothing loads while the machine is paused.

- **BAS (as the machine saves it)**: written straight into RAM. No `LOAD` is typed, and neither the CE-140F nor a BASIC mode is needed (not while a program runs). It replaces the program in memory and leaves the variables, as `LOAD` does. With "Run after loading", `RUN` is typed when BASIC's RUN or PRO prompt is up. A file with a password loads as a text one does, below.
- **BAS (text)**: the file is put in the CE-140F folder and `LOAD "X:name"` is typed. Connect the CE-140F and be at BASIC's RUN or PRO prompt (not while a program runs). The name is cut down to the 8.3 form the CE-140F takes (`my prog.bas` becomes `MYPROG.BAS`); if the folder has another file of that name, you are asked before it is overwritten.
- **BIN (with the 16-byte `SAVE M` header)**: written straight into memory at the address in the header. The whole range must lie in the internal RAM or the RAM card there now (an address with no RAM is a mirror of another place, so the file is not loaded).
  - When the range lies only in free space outside the machine-code area (BASIC's free space), BASIC can overwrite it, so you are asked whether to reserve the machine-code area from its start up to `BFBFF` first. Yes reserves and writes once that is done, No writes without reserving, Cancel does nothing.
  - When the range touches space in use (BASIC's program, variables, stacks, work areas, the system and so on), it is not written as it is. Only when that range can be reserved as the machine-code area first are you asked whether to reserve it and then load (No does nothing).
  - Reserving works at BASIC's RUN or PRO prompt, when there is enough free space. For a BIN that could not be loaded, or one without the header, use Load BIN in the debugger and give the address.

### Exchanging files with a real machine

| Format | Emulator → real machine | Real machine → emulator |
|---|---|---|
| Normal (tokenized, `SAVE "X:..."`) | Fine: the file is byte for byte what the real machine saves | Fine |
| Machine code (`SAVE M` / `LOAD M`) | Fine | Fine |
| Text (`SAVE "X:...",A`) | Every line arrives, but a hardware CE-140F emulator ends the LOAD with an error (below) | Fine |

**A text LOAD through a hardware CE-140F emulator ends with an error.** At the end of a text file, the hardware emulator (ffxx68's, and those built on it) sends its end-of-file value (FFh) as a character before the closing 1Ah. The ROM takes it for one more line, so whatever the file's form, the LOAD ends with an error.

- With a line end (CR+LF) on every line, all lines arrive and only "Undefined line" shows at the end; the program runs as it is.
- With no line end on the last line, that line is mangled or lost (e.g. "Line buffer overflow"); with a 1Ah at the end, "I/O error".
- For a real machine, use the normal form (no `,A`). A text file becomes one when the emulator `LOAD`s it and saves it again with `SAVE "X:..."`; the emulator reads text with no line end on the last line, or with a 1Ah.

The emulator saves text as the hardware emulator does: no 1Ah, and CR+LF on every line, the last one too.

## CE-126P (printer)

The 11-pin CE-126P is reproduced so the ROM's own printer driver runs unchanged (protocol in [CE-126P_PRINTER.md](https://github.com/litchi-gh/pc-e500_tools/blob/main/Analysis/CE-126P_PRINTER.md) in pc-e500_tools). It is connected from start-up, and `LPRINT`, `LLIST` and `PRINT=LPRINT` print 24 columns. The "CE-126P" window shows the paper, drawn with the ROM's character font; it can be copied or saved as text or PNG.

![CE-126P window](app/manual/images-en/printer.png)

```basic
LPRINT "HELLO"
LLIST
```

- Untick "Connected" and LPRINT reports `Printer error`, as on a real machine with no printer. The setting is kept.
- It shares the 11-pin bus with the CE-140F; both work together.
- **Printing on a printer**: "Print to" in the CE-126P window sends the lines on to a printer as well; the choice is kept in the settings.
  - **A USB thermal printer** (58 mm, 384 dots across, ESC/POS, such as the MPT-II): written to directly, with no driver or print queue, a line at a time as it comes. The ROM's glyphs are drawn at three times their size, 2 mm a character and 4 mm a line. After printing has paused for 3 seconds, the paper is fed 12 mm.
  - **A Windows printer**: when printing has paused for 3 seconds, the lines since the last job print as one job at their real size (2 mm a character, 4 mm a line), page after page.
  - "Print paper" sends the whole paper to the printer.
  - A line that cannot reach the printer (unplugged, or in use elsewhere) stays on the paper in the window, the reason shows at the bottom of the window, and the machine goes on.
- The paper keeps up to 2000 lines, dropping the oldest. The paper is not part of snapshots, but the printer's place on the bus is, so saving works at any time mid-line (a loaded snapshot prints that line again in full).
- Print time is not reproduced; lines print at once.

## Saving and restoring

- RAM is saved every 5 seconds, at OFF and at exit, and survives power off. After a crash you may lose up to the last autosave.
- The paths below are inside the data folder (`app/` for the zip and source builds, `%LocalAppData%\PockeComE500\` when installed).
- Internal RAM goes to `state/memory/<ROM SHA256>/internal-<size>KB.ram`; the RAM card goes to its own image file.
- A snapshot of the CPU, internal RAM, LCD, timers and where the printer and CE-140F are on the bus is saved automatically to `state/<ROM SHA256>.i<internal>-c<card>.e500state`. Settings are in `state/settings.json`, and the controller presets and mappings in `state/controller.json`.
- "Save state" and "Load state" keep and restore snapshots of your choice; loading restores the internal RAM size of the snapshot. The RAM card is not part of a snapshot: after a load the card keeps what its image file holds, and a snapshot cannot be loaded with a card of a different capacity attached (one saved without a card is loaded without a card).
- Snapshots cannot be saved or loaded during a CE-140F transfer or with a file OPEN; `CLOSE` first. RAM autosave continues. Virtual disk files are not part of snapshots.
- "All Reset" saves the previous state to `state/before-reset-<date>.e500state` first. The RAM card is kept.
- Files are hash-checked and replaced atomically from a temporary file. The ROM itself is not stored.
- The state file is version 4 (holding `E500ST05`, which records the ROM's size and checksum, so another ROM's state is refused); version 3 (`E500ST04`) from 0.0.4 and versions 1 and 2 (`E500ST01`, `E500ST02`) up to 0.0.3 still load. A file from a newer emulator does not load in an older one, which says so from 0.0.4 on. Not compatible with other emulators.

## Scope

Implemented: SC62015 instruction execution, the 256KB ROM (PC-E500, PC-E550, PC-1480U, PC-1490U, PC-1490UII) and the 384KB ROM (PC-E500S, PC-E650, PC-U6000), internal RAM and RAM card with mirrors, two HD61102 LCD controllers (240×32 dots), key matrix, timer interrupts, ON/BRK and OFF, 0.5/1/2/4x speed, state save/restore, BEEP, folder-based CE-140F, CE-126P printer.

**Not implemented:** printers other than the CE-126P, cassette, RS-232C/UART, RTC. Not every instruction, BASIC command or timing has been verified against the real machine; LCD busy time is omitted. Machine-code programs and peripheral-heavy programs need further verification.

Verified to work with ROMs of the PC-E500, PC-E500S, PC-E550, PC-1480U, PC-1490UII, PC-E650 and PC-U6000.

## Manual

The user manual is at [app/manual/index_en.html](app/manual/index_en.html) (Japanese: [index.html](app/manual/index.html)); the Help button opens the one for the current language. Its pictures are rendered by the emulator itself with `PC-E500.exe --manual-shots <ROM> <folder> [ja|en]`; `build.ps1` regenerates `app/manual/images/` and `app/manual/images-en/` on every build.

## Development

Build tools: Visual Studio C++ x64 build tools and the .NET 10 SDK. Python 3 and Pillow are needed for the full test run (not for running the app).

```powershell
cd Emulator
./build.ps1
./test.ps1
./package.ps1   # the release zip and installer, into dist\
```

`package.ps1` builds, gathers the release's files into one folder and makes both the zip and the installer from it. The version is `<Version>` in `src/desktop/PcE500.csproj`, and the first line of `app/README.txt` must name it too. The installer needs [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`); it is defined in `installer/PockeComE500.iss`. Last, it writes the SHA-256 of the zip and the installer to `dist/PC-E500-Emulator-<version>-win-x64-SHA256.txt` (in the form `sha256sum -c` reads) and prints them ready to paste: every release’s notes carry that "## SHA-256" section.

- `src/native/`: CPU core, hardware and the C ABI DLL
- `src/desktop/`: C# / Windows Forms UI, input and persistence (`Lang.cs` holds the language switch; every visible string is written as `L.T(japanese, english)` at its point of use)
- `src/desktop/ManualShots.cs`: renders the manual's pictures (`--manual-shots`)
- `tests/test_core.cpp`: some twenty thousand checks on the CPU, every RAM size, debugger writes, LCD, timers, audio, the CE-140F, the CE-126P and the 11-pin bus, and the snapshot format
- `src/desktop/SmokeTest.cs`: integration test with the installed ROM: UI input, internal RAM file and card image restore, settings dialog, debugger editing and BIN I/O, Windows audio, the CE-126P paper, the right-click menu and window views, the LCD look, the new-version notice and where data lives
- `tests/test_hardware.py`: on the real ROM: every RAM size, E650/U6000 expansion, the E500S's internal RAM, the BATT symbol, ON+RESET, BEEP waveform and key click timing, multi-block and ASCII save/load, sequential files and file commands
- `tests/test_roms.py`: every ROM in `app/rom` through start-up, a calculation, a program, SAVE and LOAD on the CE-140F and LPRINT (screens in `tests/output/roms/`)
- `installer/`, `package.ps1`: the installer's definition and the script that makes the zip and the installer
- `tests/output/`: test results and screenshots
- `third_party/pockemul/`: the original sources and license

The CPU files are extracted from the original with `tools/prepare_core.py` (Python needed only to regenerate them).

## License

See [README_en.md](README_en.md#license).

## Acknowledgements

See [README_en.md](README_en.md#acknowledgements).

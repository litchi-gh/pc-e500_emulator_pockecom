# PC-E500 Series Emulator for Windows (PokeCom)

[日本語](README.md)

An emulator that runs the SHARP **PC-E500 series** pocket computers on Windows, straight from the real machine's ROM. The CPU (SC62015), the LCD, the keys and the peripherals are emulated, so BASIC and the scientific calculator are the machine's own code running from its ROM.

![Main window (PC-E500)](app/manual/images-en/main-e500.png)

## Supported models

| Model | ROM | Case |
|---|---|---|
| PC-E500, PC-E550, PC-1480U, PC-1490U, PC-1490UII | 256KB | PC-E500 |
| PC-E650, PC-U6000 | 384KB (the main S3A merged with the S3B extension) | PC-E650 |

**ROM images are not included.** Dump the ROM from your own machine ([how](MANUAL_en.md#dumping-the-rom-and-toolsmerge_savem_rompy)). Verified to work with ROMs of the PC-E500, PC-E550, PC-1480U, PC-1490UII, PC-E650 and PC-U6000.

## System requirements

| Item | Requirement |
|---|---|
| OS | **Windows 10 / 11, 64-bit (x64)**. The .NET runtime is bundled; nothing else to install |
| CPU | Any x64 CPU; about 2 cores at 2 GHz or more as a guide (any PC from the last ten years or so) |
| Memory | The app itself uses about 150 MB; 4 GB or more for the PC is recommended |
| Disk | About 120 MB (plus your ROM, RAM cards and other data) |
| Display | 1280×800 or more recommended (the window can be resized) |
| Sound | A sound device for BEEP (it runs without one) |

- Measured on a Core i5-1235U: the emulation runs about 50 times faster than the real machine on one core, so normal speed takes about 2% of one core, and 4x about a tenth. The app used about 130 MB of memory at most.
- Tested on Windows 11 (x64). Windows 10 and Windows on ARM (which should run it through x64 emulation) have not been tested.

## Highlights

### A case like the real one

The keys' layout, shapes and lettering, and the LCD legends, sit where photographs of the real machines put them. Two cases are available, the PC-E500 and the PC-E650. Press the keys with the mouse or on the PC keyboard.

| | |
|---|---|
| ![PC-E650 appearance](app/manual/images-en/main-e650.png) | ![Real LCD](app/manual/images-en/lcd-dot-lines.png) |
| The PC-E650 case | The Real LCD look, with space between the dots |

### Real LCD look and contrast

Choose dots drawn apart as on the real glass, and a contrast from 0 to 100. As on the real unit, the legends that are off fade and darken with the contrast too.

### Case-only and screen-only windows

Double-click the case away from the keys to step through normal → case only → screen only. The frameless views still move and resize by dragging, and the right-click menu holds every command.

| Case only | Screen only | Right-click menu |
|---|---|---|
| ![Case-only view](app/manual/images-en/window-body.png) | ![Screen-only view](app/manual/images-en/window-screen.png) | ![Right-click menu](app/manual/images-en/context-menu.png) |

### Peripherals: CE-140F and CE-126P

- **CE-140F (floppy)**: a PC folder serves as drive X:. `SAVE "X:..."`, `LOAD`, `OPEN`, `FILES` and the rest run over the same 11-pin protocol as the real drive. To exchange programs with a real machine, use the normal form (no `,A`): a bug of the hardware CE-140F emulator ends a text LOAD on the real machine with an error ([details](MANUAL_en.md#exchanging-files-with-a-real-machine)).
- **CE-126P (printer)**: `LPRINT` and `LLIST` print 24 columns; save the paper as text or PNG.

| | |
|---|---|
| ![CE-140F window](app/manual/images-en/disk-trace.png) | ![CE-126P window](app/manual/images-en/printer.png) |
| The CE-140F transfer log | The CE-126P paper |

### Copying the screen, pasting programs

Copy the LCD as text. Clipboard text is typed in as key presses, so a BASIC program written on the PC can be pasted in.

![Text I/O](app/manual/images-en/text-transfer.png)

### Debugger

Watch the CPU registers, the internal RAM and memory, and edit them in place. Load and save BIN files (SAVEM headers included) and compute checksums: handy for trying machine-code programs.

![Debug window](app/manual/images-en/debugger.png)

### And more

- **Memory**: 8 to 256KB of internal RAM (up to the E650/U6000 expansion mod). RAM cards are image files you create and swap.
- **Saving**: the contents survive power-off. Save and restore the whole state at any time.
- **Speed**: 0.5x to 4x, with BEEP sound.
- **Language**: Japanese or English.
- **Updates**: a newer release is announced on the toolbar (you update by hand).

## Getting started

1. Get the installer (`-setup.exe`) or the zip from [Releases](https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases). It is for Windows x64 and needs no separate .NET install.
2. Put the ROM dumped from your machine in the data folder's `rom`. "Open the data folder" in the right-click menu opens it.
3. Start it, press **PF1** (F1 on the PC) to initialise memory, then **BASIC** (F7).

## What is new in 0.0.4 (since 0.0.3)

- **CE-126P printer**: `LPRINT` and `LLIST`, with paper to view and save.
- **Right-click menu**: the toolbar's commands from any view.
- **Window views**: a double-click switches to case only or screen only; the view is kept.
- **Real LCD and contrast**, in Display.
- **New-version notice** and an **installer**.
- **Models**: the PC-E550, PC-1480U, PC-1490U and PC-1490UII join the list.
- The LCD legends and the lettering on the case follow the real machine.

The full list is in [CHANGELOG.md](CHANGELOG.md) (Japanese).

## Documents

| File | Contents |
|---|---|
| [MANUAL_en.md](MANUAL_en.md) | How to use it and how it works, in detail (keys, settings, RAM, CE-140F and CE-126P, saving, dumping the ROM, building and testing) |
| [app/manual/index_en.html](app/manual/index_en.html) | The illustrated user manual (the Help button opens it) |
| [CHANGELOG.md](CHANGELOG.md) | Change history (Japanese) |

## License

| Component | License |
|---|---|
| This project's sources (`src/`, `tests/`, `tools/`) | [GNU General Public License v2.0 or later](LICENSE) |
| SC62015 CPU core (`src/native/sc62015.*`, `third_party/pockemul/`) | GPL-2.0, ported from [matsumo/PockEmul](https://github.com/matsumo/PockEmul/tree/898c891abb25da94df8951f13a9fc4a0b986c69d). Copyright (C) Matsumo 1998,1999 |
| .NET runtime / Windows Forms bundled in the build | MIT; notices in `app/DOTNET-LICENSE.txt`, `app/DOTNET-THIRD-PARTY-NOTICES.txt`, `app/WINDOWSDESKTOP-LICENSE.txt` |
| Internal ROM, Technical Reference Manual | **Not included.** Copyright Sharp Corporation; dump the ROM from your own machine. No ROM is downloaded, embedded or redistributed |

Provenance details are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). SHARP, PC-E500, CE-140F and other names are trademarks of their respective owners. This project is not affiliated with Sharp Corporation.

## Acknowledgements

- **[matsumo/PockEmul](https://github.com/matsumo/PockEmul)** — the SC62015 core and E500 peripheral implementation this emulator is built on.
- **[ffxx68/Sharp_ce140f_emul](https://github.com/ffxx68/Sharp_ce140f_emul)** — analysis and implementation of the CE-140F 11-pin protocol, the reference for drive X:.
- **[E500 series memory expansion](https://www.oit.ac.jp/labs/rd/rssrv/kobayashi-lab/~yagshi/old_web/misc/pocketcom/memup/index.html)** — the 62256 → 628128 mod behind the 128KB/256KB settings.
- Users who contributed recordings and measurements of real PC-E500s (13.5 ms key click, 62.5 ms repeat) and the ROM, CE-2S01 and CE-140F analysis notes.
- Implementation, tests and documentation were written together with Anthropic's [Claude Code](https://claude.com/claude-code).

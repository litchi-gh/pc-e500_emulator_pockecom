# 2. On Ubuntu (Linux)

The emulator runs on the Ubuntu desktop and is used just like the Windows program. The ROM does the same work, and the files it saves are the same.

![Running on Ubuntu](../images/linux-main.png)

## Requirements

| Item | |
|---|---|
| OS | Ubuntu 24.04 or later (x86-64), Wayland or X11 (through XWayland on Wayland) |
| To install first | Nothing: the .NET runtime is included. The `.deb` brings the libraries it needs (X11, fonts, ALSA) with it |
| Fonts | Japanese text uses Noto Sans CJK (`fonts-noto-cjk`, already there on a Japanese Ubuntu) |
| Disk | About 180 MB |

## Installing

Get one of these from the [releases](https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases).

### .deb (an entry in the app list)

```sh
sudo apt install ./pc-e500-emulator_<version>_amd64.deb
```

"PC-E500 Emulator" joins the app list; in a terminal it is `pc-e500`. The command line, [`pokecom`](16_CLI.md), comes with it.

| What | Where |
|---|---|
| The program | `/opt/pc-e500/` |
| Data (ROMs, RAM cards, the X: folder, settings) | `~/.local/share/PockeComE500/`, per user |

`sudo apt remove pc-e500-emulator` takes it out; the data stays.

### tar.gz (anywhere you like)

```sh
tar xzf PC-E500-Emulator-<version>-linux-x64.tar.gz
cd PC-E500-Emulator-<version>-linux-x64
./PC-E500
```

The data lives inside the unpacked folder, as with the Windows zip.

## Putting the ROM in place

No ROM is included. Put [one dumped from your own machine](04_ROM.md) in the data's `rom` folder and start the program.

```sh
mkdir -p ~/.local/share/PockeComE500/rom      # installed from the .deb
cp 'PC-E500(7.2).ROM' ~/.local/share/PockeComE500/rom/
```

"Open the data folder" in the right-click menu opens the folder. "Open ROM" chooses a ROM anywhere else.

## What differs from Windows

Everything from [chapter 5](05_FIRST_STEPS.md) on applies. Only these differ.

| Item | Ubuntu |
|---|---|
| Choosing files | The desktop's own window (GNOME's file chooser) |
| Running after "Load program" | Asked after the file is chosen (on Windows, a check box in the file window). A dropped file follows the last answer |
| Sound | To ALSA's default output (PipeWire on Ubuntu) |
| Where the CE-126P prints | A USB thermal printer (`/dev/usb/lp0` and so on) or a printer set up in CUPS ([chapter 12](12_CE-126P.md)) |
| Game controllers | The kernel's joysticks (`/dev/input/js0` and so on; [chapter 13](13_CONTROLLER.md)) |
| Theme | "As the system" follows the desktop's light or dark |
| Standard LCD look | The dots' edges are smoothed to the screen's pixels |

The case-only and screen-only views, moving by dragging, sizing by the edges and "Always on top" work the same way.

![Case only](../images/linux-body.png)

## Updating

When a new version is out, a "New version" button appears on the toolbar. Quit the emulator, then `apt install` the new `.deb`, or unpack the new tar.gz over the folder. ROMs, RAM and settings stay.

## Moving data to and from Windows

State files (`.e500state`), RAM card images (`.ram`) and the files in the X: folder are in the same form as on Windows: copy them and go on. The settings (`state/settings.json`) hold the places of the ROM and the X: folder, so choose those again with "Open ROM" and Settings after copying them.

BASIC text whose lines end with LF alone, as files made on Linux do, loads as well.

---

[← 1. On Windows](01_WINDOWS.md) ｜ [Contents](../README_en.md) ｜ [3. On a Mac (command line) →](03_MACOS.md)

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

Type (or paste) these in a terminal: they download and install. The `get` line is long, so copy and paste it.

```sh
sudo apt update
sudo apt install -y curl
mkdir -p ~/pockecom/download
get() { curl -fL -o "$3" "$(curl -fsSL "https://api.github.com/repos/litchi-gh/$1/releases/latest" | grep -o "\"browser_download_url\": *\"[^\"]*$2\"" | cut -d'"' -f4)"; }
```

| Command | What it does |
|---|---|
| `sudo apt update` | Renews the list of what can be installed; installs nothing yet |
| `sudo apt install -y curl` | Installs `curl`, which downloads files. `-y` answers yes to the question |
| `mkdir -p folder` | Makes the folder for what is downloaded, if it is not there |
| `get() { … }` | Makes a command of your own, `get`, that takes the file whose name ends as asked from the newest release on GitHub, with no version to look up. It lasts until this terminal closes; paste the line again in a new one |

Then install in **one** of these two ways. `curl: (2)` or `curl: option : blank argument` says the newest release has no file of that name: see the [releases](https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases) page.

The folders and `get` are those of the [PC-E500 development tools](https://github.com/litchi-gh/pc-e500_tools). If you use both, installing by either one's steps is enough.

### A. Installed (.deb)

```sh
get pc-e500_emulator_pockecom amd64.deb ~/pockecom/download/emulator.deb
sudo apt install ~/pockecom/download/emulator.deb
```

| Command | What it does |
|---|---|
| `get …` | Takes the newest package of the emulator |
| `sudo apt install file.deb` | Installs from the package file, with any libraries missing (X11, fonts, ALSA...) |

"PC-E500 Emulator" joins the app list; in a terminal it is `pc-e500`. The command line, [`pokecom`](16_CLI.md), comes with it.

| What | Where |
|---|---|
| The program | `/opt/pc-e500/` |
| Data (ROMs, RAM cards, the X: folder, settings) | `~/.local/share/PockeComE500/`, per user |

### B. Unpacked (tar.gz)

```sh
mkdir -p ~/pockecom/Emulator
get pc-e500_emulator_pockecom linux-x64.tar.gz ~/pockecom/download/emulator.tar.gz
tar xzf ~/pockecom/download/emulator.tar.gz -C ~/pockecom/Emulator --strip-components=1
~/pockecom/Emulator/PC-E500
```

| Command | What it does |
|---|---|
| `mkdir -p folder` | Makes the folder for it |
| `get …` | Takes the newest emulator |
| `tar xzf file -C folder` | Unpacks the `.tar.gz`: `x` takes out, `z` is for gzip, `f` says the file name follows. `-C` is where to, and `--strip-components=1` leaves out the one folder inside, named for the version |
| `~/pockecom/Emulator/PC-E500` | Starts the emulator |

This needs no administrator. If libraries are missing, see [chapter 20](20_TROUBLESHOOTING.md). The data lives in the folder too, as with the Windows zip.

To have `pokecom` from anywhere, type these two lines and log in again.

```sh
mkdir -p ~/.local/bin
ln -sf ~/pockecom/Emulator/pokecom ~/.local/bin/pokecom
```

| Command | What it does |
|---|---|
| `mkdir -p ~/.local/bin` | Makes the folder for commands of your own. Ubuntu puts it on the PATH (where commands are looked for) from the next login |
| `ln -sf from to` | Puts a link to the file. `-f` replaces one already there |

## Putting the ROM in place

No ROM is included. Put [one dumped from your own machine](04_ROM.md) in the data's `rom` folder and start the program. Here your ROM is `~/PC-E500(7.2).ROM`, in your home folder.

Installed from the `.deb`:

```sh
mkdir -p ~/.local/share/PockeComE500/rom
cp ~/'PC-E500(7.2).ROM' ~/.local/share/PockeComE500/rom/
```

From the tar.gz:

```sh
cp ~/'PC-E500(7.2).ROM' ~/pockecom/Emulator/rom/
```

`cp from to` copies a file. `( )` mean something to the shell, so the name goes in `'…'`.

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

## Updating and uninstalling

When a new version is out, a "New version" button appears on the toolbar. Quit the emulator and type the lines of [Installing](#installing) again, from `get`. The `.deb` replaces the old one; the tar.gz unpacks over the folder. ROMs, RAM and settings stay.

To take it out: for the `.deb`, `sudo apt remove pc-e500-emulator` (the data stays); for the tar.gz, delete `~/pockecom/Emulator` and `~/.local/bin/pokecom` (the data goes too).

## Moving data to and from Windows

State files (`.e500state`), RAM card images (`.ram`) and the files in the X: folder are in the same form as on Windows: copy them and go on. The settings (`state/settings.json`) hold the places of the ROM and the X: folder, so choose those again with "Open ROM" and Settings after copying them.

BASIC text whose lines end with LF alone, as files made on Linux do, loads as well.

---

[← 1. On Windows](01_WINDOWS.md) ｜ [Contents](../README_en.md) ｜ [3. On a Mac (command line) →](03_MACOS.md)

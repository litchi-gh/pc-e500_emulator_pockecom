# 20. Troubleshooting

## Starting and the ROM

| Symptom | What to do |
|---|---|
| It asks for a ROM, or says none was found | No ROM is included. Put one in the `rom` folder ([chapter 4](04_ROM.md)), or choose it with "Open ROM" |
| The ROM does not open | Its size must be 256 KB (or 256 KB + 16) or 384 KB. For the PC-E500S, PC-E650 and PC-U6000, [merge](04_ROM.md#dumping-the-rom-and-toolsmerge_savem_rompy) the two files first |
| `S1(MAIN): NEW CARD` | Normal at the first start. PF1 (F1) initialises the memory ([chapter 5](05_FIRST_STEPS.md)) |
| `./PC-E500` does not start on Ubuntu | Start it from a terminal and read the message. The tar.gz brings no missing libraries: install the `.deb`, or try `sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libasound2t64` |
| Japanese shows as boxes on Ubuntu | `sudo apt install fonts-noto-cjk` |
| A Mac says the developer cannot be verified | Run `xattr -dr com.apple.quarantine .` ([chapter 3](03_MACOS.md#installing)) |

## In use

| Symptom | What to do |
|---|---|
| Keys do nothing | See whether the power is off (the left end of the status bar is grey): Esc (ON/BRK) turns it on. If paused (yellow), click there to go on |
| The power goes off when left alone | As on the real machine. "Auto power off" in Settings stops it |
| A line with a number gives `Syntax error` | It is in RUN. Press BASIC (F7) for PRO |
| No sound, or broken sound | See that it is not muted and the speed is 1x |
| `LPRINT` does nothing | The CE-126P is not "Connected"; the ROM then skips it without an error ([chapter 12](12_CE-126P.md)) |
| "Load state" refuses | Use the same ROM and a RAM card of the same size as when it was saved ([chapter 15](15_SAVE.md)) |
| It hangs | Press the RESET hole; data survives. If that does not help, All Reset ([chapter 6](06_KEYS.md#resetting)) |

## CE-140F (X:)

| Symptom | What to do |
|---|---|
| `File not found` | Check the name and the folder in Settings. Names are 8.3; subfolders are not seen |
| `File write protected` | Clear the write protection, or the file's read-only attribute |
| `I/O error` whatever you do | See that the CE-140F is connected |
| On a real machine, a text LOAD ends with an error | See [chapter 10](10_PROGRAMS.md#exchanging-files-with-a-real-machine): hand it over in the normal form (no `,A`) |

[Chapter 11](11_CE-140F.md#errors) lists the errors; the CE-140F window (`drive-trace` on the command line) shows the transfers.

## Printers and controllers

| Symptom | What to do |
|---|---|
| A USB thermal printer does not print on Ubuntu (permission denied) | Run `sudo usermod -aG lp $USER` and log in again |
| A CUPS printer is not listed on Ubuntu | See whether `lpstat -e` shows it (from `cups-client`) |
| The pad does nothing | Bring the main window to the front, check "Use a controller" and "Use", and press "Search again" |
| The buttons are in other places | Set them with "Match button places..." ([chapter 13](13_CONTROLLER.md)) |

## The command line and MCP

| Symptom | What to do |
|---|---|
| Exit code 3 (not ended in time) | It waits for input or never ends. Answer with `--input`, or give it more `--seconds` |
| Exit code 4 (a BASIC error) | The error is on standard error; put the program right |
| Only four lines come out | The LCD has four. What is `LPRINT`ed is all shown |
| The program loaded before is gone | The machine starts afresh each time. `--state` goes on ([chapter 16](16_CLI.md#go-on-from-before---state)) |
| `"` does not get through PowerShell on Windows | Put the program in a file, or give it on standard input with `-`; a `script` file works too |
| An AI tool does not connect | See [chapter 18](18_MCP.md#when-it-does-not-connect) |

---

[← 19. Scope and differences between systems](19_COMPATIBILITY.md) ｜ [Contents](../README_en.md)

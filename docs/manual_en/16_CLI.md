# 16. The command line (pokecom)

`pokecom` runs the emulator with no window: run a line of BASIC, run a program and take its results, press keys and read the LCD, from a terminal, a script or an automatic test. It is the same on Windows, Linux and the Mac.

```console
$ pokecom exec "PRINT 12*34"
PRINT 12*34
                                     408
>
```

- The real ROM runs: it is the same machine as in the emulator with a window.
- It runs as fast as the PC goes. Every "second" is a second of the machine's own time.
- The machine starts afresh each time. **The RAM, settings and autosave of the emulator with a window are never touched.**
- The automatic power off is held off at first.

## What you need

| System | Where `pokecom` is |
|---|---|
| Windows | `pokecom.exe` in the folder `pokecom-<version>-win-x64.zip` unpacks to |
| Ubuntu | `pokecom`, once installed with the line of [chapter 2](02_LINUX.md#installing) |
| Mac | `pokecom`, once installed with the line of [chapter 3](03_MACOS.md#installing) |

### Where the ROM is looked for

In this order; the first found is used.

1. The file given with `--rom`
2. The file in the `POKECOM_ROM` environment variable
3. A `rom` folder: in the current folder, the folder of `pokecom`, or the user's data folder, in that order. The first folder with a ROM is used: the newest of the files named `PC-E500(version).ROM` there, or else the first by name. A file of 256 KB (or 256 KB + 16, with its header) or 384 KB is taken for a ROM

| System | The user's data folder |
|---|---|
| Windows | `%LocalAppData%\PockeComE500\rom` |
| Ubuntu | `~/.local/share/PockeComE500/rom` |
| Mac | `~/Library/Application Support/PockeComE500/rom` |

`pokecom roms` lists the ROMs found.

## Basics

```
pokecom <command> [arguments] [options]
```

`pokecom help` lists the commands and `pokecom help <command>` tells more. All of them are in [chapter 17](17_CLI_REFERENCE.md).

### Run a line of BASIC: exec

```console
$ pokecom exec "PRINT SQR 2"
$ pokecom exec 'SAVE "X:TEST.BAS"' --disk ./mydisk
```

When it ends, the LCD's characters (40 × 4) are shown.

### Run a program: run

```console
$ cat sq.bas
10 INPUT "N=";N
20 FOR I=1 TO N
30 LPRINT I;I*I
40 NEXT I
50 PRINT "OK"

$ pokecom run sq.bas --input 3
RUN
N=3
OK
>
Printed:
 1  1
 2  4
 3  9
```

- `--input` answers `INPUT`. Several answers are separated by `\n` (`--input "3\n5"`).
- Only the last four lines stay on the LCD. **Use `LPRINT` for long output**: what the CE-126P printed is shown as it is.
- `-` for the file reads standard input; `--program "10 PRINT 1\n20 PRINT 2"` gives the text itself.
- BASIC text (UTF-8), the form the machine saves, and BINs with the `SAVE M` header are read. A BIN is loaded, not run.
- `--seconds` sets how long to wait for the end (60 seconds).

### Go on from before: --state

Each call makes a new machine. With `--state` it starts from the state in that file, if it is there, and writes the state at the end back to it.

```console
$ pokecom exec "A=5" --state work.e500state
$ pokecom exec "PRINT A" --state work.e500state
PRINT A
                                       5
>
```

The file is the same as "Save state" and "Load state" use in the emulator with a window.

### Many at once: script

A file with a command a line runs on one machine from the first line to the last.

```console
$ cat demo.txt
# A command a line; what follows a # is skipped
load sq.bas
type "RUN\n"
wait-text "N="
type "4\n"
wait 2
printed

$ pokecom script demo.txt
```

- Words are separated by spaces. A word with spaces goes in `"..."`, and a `"` inside it is written `""`.
- It stops at a line that fails, and tells the line number and the error.
- `-` for the file reads standard input.
- `start` (a fresh machine) and `key-down` / `key-up` (hold keys and let go) are for scripts and MCP only.

### Keep the screen, the sound and the paper

```console
$ pokecom exec "PRINT 1" --png lcd.png
$ pokecom record beep.wav --line "BEEP 3"
$ pokecom script work.txt
```

These keep the LCD at the end as a PNG, the sound as a WAV, and the paper as text and a PNG (for the paper, `work.txt` holds `printed --to out.txt --png out.png`).

## Peripherals and set-up

These options go with any command.

| Option | |
|---|---|
| `--rom <file>` | The ROM |
| `--ram <KB>` | Internal RAM (8, 16, 32, 64, 128, 256; 32 by default, and at least 64 with an E650/U6000 ROM) |
| `--card <file>` | A RAM card image (`pokecom card-create card.ram 64` makes one) |
| `--disk <folder>` | The CE-140F's (X:) folder; without it, a temporary one that goes at the end |
| `--disk-read-only` | Write-protect X: |
| `--no-printer` | Without the CE-126P |
| `--state <file>` | Go on from a state |
| `--png <file>` | Save the LCD at the end as a PNG |
| `--json` | Answer in JSON |
| `--lang ja` / `--lang en` | The language of the messages |

A RAM card's image is written back when its contents change.

## Taking the results

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Fine |
| 1 | An error (no such file, another ROM...) |
| 2 | Wrong usage |
| 3 | Not ended in time (running, or waiting for input) |
| 4 | A BASIC error |

BASIC errors are told by the ROM's own table of messages.

```console
$ pokecom exec "PRIMT 5"; echo $?
PRIMT 5
Syntax error
BASIC error: Syntax error
4
```

Results and the screen go to standard output; why something failed goes to standard error.

### JSON

```console
$ pokecom run sq.bas --input 3 --json --lang en
{"code":0,"text":"Printed:\n 1  1\n 2  4\n 3  9","screen":["RUN","N=3","OK",">"],"symbols":["RUN","CAPS","DEG"]}
```

| Key | |
|---|---|
| `code` | The same number as the exit code |
| `text` | What was printed, what went wrong |
| `screen` | The LCD's four lines (for commands that show the screen) |
| `symbols` | The symbols lit (`RUN`, `PRO`, `BUSY`, `CAPS`, `DEG`...) |

## Examples

### Testing a BASIC program automatically

```sh
#!/bin/sh
# Compares what was printed with what it should be
pokecom run sq.bas --input 3 --json | python3 -c '
import json, sys
r = json.load(sys.stdin)
assert r["code"] == 0, r["text"]
assert "3  9" in r["text"], r["text"]
print("OK")'
```

### Loading machine code and running it

```
# ml.txt: reserves 400h bytes for machine code, puts the BIN there and calls it
reserve 400
load-bin prog.bin
exec "CALL &HBF800"
registers
```

```console
$ pokecom script ml.txt
```

`load-bin` writes to the address in the `SAVE M` header, or to the address given.

### Holding a game's keys

```
# game.txt
load game.bas
type "RUN\n"
wait 2
key-down RIGHT
wait 1.5
key-up RIGHT
screen --png after.png
```

Key names are letters and digits, `ENTER` `SPACE` `BS` `DEL` `INS` `LEFT` `RIGHT` `UP` `DOWN` `SHIFT` `CTRL` `BASIC` `MENU` `F1`-`F5` (`PF1`-`PF5`) `2ndF` `C-CE` `BRK` `OFF` and what is printed on the other keys. `SHIFT+Q` presses the two together.

### With other ROM versions

```sh
for rom in roms/*.ROM; do
  echo "$rom"
  pokecom run test.bas --rom "$rom" || exit 1
done
```

---

[← 15. Saving and restoring](15_SAVE.md) ｜ [Contents](../README_en.md) ｜ [17. pokecom command reference →](17_CLI_REFERENCE.md)

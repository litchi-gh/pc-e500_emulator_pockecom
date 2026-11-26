# 17. pokecom command reference

Every command of [chapter 16](16_CLI.md); `pokecom help <command>` shows the same. The MCP tools ([chapter 18](18_MCP.md)) are these too, with `_` for each `-` in a name.

- `<...>` is required and `[...]` may be left out. Any argument can also be written `--name value`.
- A number in parentheses is the value when it is left out. Seconds are the machine's own.
- Addresses are in hex (`B8000`, `&HB8000`, `0xB8000`).

## Options of every command

| Option | |
|---|---|
| `--rom <file>` | ROM image (else POKECOM_ROM, or one in a rom folder) |
| `--ram <KB>` | Internal RAM in KB (32) |
| `--card <file>` | RAM card image file |
| `--disk <folder>` | Folder for the CE-140F drive X: (else a temporary one) |
| `--disk-read-only` | Write-protect the drive |
| `--no-printer` | Without the CE-126P |
| `--state <file>` | Go on from this state file and write it back at the end (made if missing) |
| `--png <file>` | Save the screen at the end as a PNG |
| `--json` | Answer in JSON |
| `--lang <ja or en>` | Language of the messages: ja or en |
| `--help` | Show help |

## The machine

### start

```
pokecom start
```

Starts a fresh machine (RAM empty) at BASIC's RUN prompt. Other operations start one by themselves; this is for a clean start, another ROM or another set-up.

| Argument | |
|---|---|
| `--rom` | ROM image file (omit to keep, or find one) |
| `--ram` | Internal RAM in KB: 8, 16, 32, 64, 128, 256 |
| `--card` | RAM card image file |
| `--disk` | Folder the CE-140F drive (X:) keeps its files in |

### info

```
pokecom info
```

Tells the ROM, RAM, card, drive, printer, power, symbols lit, machine-code area and free memory.

### configure

```
pokecom configure
```

Changes the set-up. Another RAM size or card switches the machine on afresh (the internal RAM's contents stay when its size does); the rest changes at once.

| Argument | |
|---|---|
| `--ram` | Internal RAM in KB |
| `--card` | RAM card image file, or "none" to take it out |
| `--disk` | Folder for the CE-140F, "none" to disconnect it, or "temporary" |
| `--disk-read-only` | Write-protect the CE-140F |
| `--printer` | Connect the CE-126P printer |
| `--auto-power-off` | Let the machine switch itself off when left alone (about 10 minutes) |

### reset

```
pokecom reset
```

Presses the RESET hole: the CPU, LCD and keys start again, the data stays.

### all-reset

```
pokecom all-reset [answer]
```

ON+RESET, which asks whether to erase everything.

| Argument | |
|---|---|
| `answer` | "yes" erases all data, "no" keeps it; omit to stop at the question |

### save-state

```
pokecom save-state <path>
```

Saves the whole machine to a file, to go back to later (the emulator with a window reads it too).

| Argument | |
|---|---|
| `path` | File name ending in .e500state |

### load-state

```
pokecom load-state <path>
```

Goes back to a saved state (of the same ROM and RAM card size).

| Argument | |
|---|---|
| `path` | File name ending in .e500state |

### roms

```
pokecom roms
```

Lists the ROM images found.

## Programs

### run

```
pokecom run [file]
```

Replaces the BASIC program with this one, runs it, and shows the screen and what it printed (LPRINT). Only the last four lines of PRINT stay on the screen: use LPRINT for long output. A BIN is only loaded.

| Argument | |
|---|---|
| `file` | Program file: BASIC text (UTF-8), one the machine saved, or a BIN; - is standard input |
| `--program` | The program text itself, one numbered line per line |
| `--input` | Typed while it runs, one line per INPUT |
| `--seconds` | Emulated seconds to wait for it to end (60) |

### load

```
pokecom load [file]
```

Loads a program without running it, as the emulator's "Load program" does: BASIC text, one the machine saved, or a BIN with the SAVE M header.

| Argument | |
|---|---|
| `file` | The file; - is standard input |
| `--program` | BASIC text itself |
| `--no-reserve` | Do not reserve the machine-code area for a BIN outside it |

### program

```
pokecom program
```

Reads the BASIC program in the machine, changing nothing.

| Argument | |
|---|---|
| `--to` | Write it to this file instead of showing it |
| `--saved` | In the form the machine saves (with to) |

### exec

```
pokecom exec <line>
```

Runs one line of BASIC at the RUN prompt (PRINT 12*34, LIST, RUN ...), stopping a program that is running.

| Argument | |
|---|---|
| `line` | The line, without ENTER |
| `--seconds` | Emulated seconds to wait for it to end (60) |

### reserve

```
pokecom reserve <size>
```

Sets the machine-code area's size (0 gives it back). The program and variables stay.

| Argument | |
|---|---|
| `size` | Bytes in hex |

## Keys and the screen

### type

```
pokecom type <text>
```

Types text on the keyboard as it stands (a newline is ENTER). Letters come out as capitals.

| Argument | |
|---|---|
| `text` | The text |
| `--seconds` | Emulated seconds to run afterwards (0.5) |

### key

```
pokecom key <keys...>
```

Presses keys one after another. Names: letters, digits, ENTER SPACE BS DEL INS LEFT RIGHT UP DOWN SHIFT CTRL BASIC MENU F1-F5 (PF1-PF5) 2ndF C-CE BRK OFF and the others printed on the case. SHIFT+Q holds SHIFT with Q.

| Argument | |
|---|---|
| `keys` | The keys in order |
| `--hold` | Emulated seconds to hold each (0 for a tap) |
| `--seconds` | Emulated seconds to run afterwards (0.3) |

### key-down

```
pokecom key-down <keys...>
```

Presses keys and keeps them down (a game's keys, with wait between).

| Argument | |
|---|---|
| `keys` | The keys |

### key-up

```
pokecom key-up [keys...]
```

Lets go of keys.

| Argument | |
|---|---|
| `keys` | The keys, or none for all of them |

### wait

```
pokecom wait [seconds]
```

Lets the machine run for a while.

| Argument | |
|---|---|
| `seconds` | Emulated seconds (1) |

### wait-text

```
pokecom wait-text <text>
```

Lets the machine run until the screen shows a text.

| Argument | |
|---|---|
| `text` | The text |
| `--seconds` | Emulated seconds at most (60) |

### screen

```
pokecom screen
```

Shows the LCD: the 40x4 characters read from it and the symbols lit, and its dots as a picture.

| Argument | |
|---|---|
| `--picture` | Also return the dots as a PNG (MCP) |
| `--png` | Save the dots as a PNG file |
| `--scale` | Pixels per dot, 1 to 16 (4) |

## Sound

### record

```
pokecom record <to>
```

Records the sound (BEEP) to a WAV file while the machine runs, after a line of BASIC if one is given.

| Argument | |
|---|---|
| `to` | The WAV file |
| `--line` | A line of BASIC to run first |
| `--seconds` | Emulated seconds to record (3) |

## CE-126P (printer)

### printed

```
pokecom printed
```

Shows everything on the CE-126P printer's paper.

| Argument | |
|---|---|
| `--to` | Save it as a text file |
| `--png` | Save a picture of the paper as a PNG file |

### paper-cut

```
pokecom paper-cut
```

Cuts the CE-126P's paper: what was printed is gone.

## CE-140F (X:)

### drive-files

```
pokecom drive-files
```

Lists the files on the CE-140F (X:).

### drive-put

```
pokecom drive-put <from> [name]
```

Copies a file to the CE-140F (X:).

| Argument | |
|---|---|
| `from` | The file |
| `name` | Its name there, 8.3 (as it is) |

### drive-get

```
pokecom drive-get <name> <to>
```

Copies a file from the CE-140F (X:).

| Argument | |
|---|---|
| `name` | Its name there |
| `to` | Where to |

### drive-trace

```
pokecom drive-trace
```

Shows the CE-140F's commands and errors.

## RAM card

### card-create

```
pokecom card-create <path> <kb>
```

Makes a RAM card image file (empty), for configure or --card.

| Argument | |
|---|---|
| `path` | The file |
| `kb` | Its size in KB: 8, 16, 32, 64, 128 or 256 |

## Debugger

### registers

```
pokecom registers
```

Shows the CPU's registers.

### set-register

```
pokecom set-register <name> <value>
```

Sets a CPU register.

| Argument | |
|---|---|
| `name` | A IL BA I X Y U S P F |
| `value` | The value in hex |

### memory

```
pokecom memory <address> [length]
```

Shows memory in hex (ROM, RAM, the card).

| Argument | |
|---|---|
| `address` | Where from, in hex |
| `length` | Bytes, up to 4096 (64) |

### poke

```
pokecom poke <address> <bytes>
```

Writes bytes to RAM: all of them, or none if any is not RAM.

| Argument | |
|---|---|
| `address` | Where to, in hex |
| `bytes` | The bytes in hex, e.g. "08 05 06" |

### imem

```
pokecom imem
```

Shows the CPU's 256 bytes of internal RAM.

### poke-imem

```
pokecom poke-imem <address> <bytes>
```

Writes the CPU's internal RAM, 00 to EE.

| Argument | |
|---|---|
| `address` | Where to, in hex |
| `bytes` | The bytes in hex |

### ram-map

```
pokecom ram-map
```

Shows how the RAM (and the card) is laid out: program, variables, free space, stacks, work areas, machine-code area.

### load-bin

```
pokecom load-bin <file> [address]
```

Writes a file's bytes to RAM at an address (any RAM), or at the one its SAVE M header names.

| Argument | |
|---|---|
| `file` | The file |
| `address` | Where to, in hex |

### save-bin

```
pokecom save-bin <file> <address> <length>
```

Saves a range of memory to a file.

| Argument | |
|---|---|
| `file` | The file |
| `address` | Where from, in hex |
| `length` | Bytes in hex |
| `--header` | With the SAVE M header, to load back to its place |

## Many at once

| Command | |
|---|---|
| `script <file>` | Runs a command a line on one machine (`-` is standard input; [chapter 16](16_CLI.md#many-at-once-script)) |
| `mcp` | The MCP server ([chapter 18](18_MCP.md)) |
| `help [command]` | Help |
| `version` | The version |

---

[← 16. The command line (pokecom)](16_CLI.md) ｜ [Contents](../README_en.md) ｜ [18. The MCP server (for AI tools) →](18_MCP.md)

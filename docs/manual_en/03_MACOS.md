# 3. On a Mac (command line)

On a Mac there is no emulator with a window; the command line, `pokecom`, runs instead. The commands of [chapter 16](16_CLI.md) and the [MCP server](18_MCP.md) for AI tools are the same as on Windows and Linux.

## Requirements

| Item | |
|---|---|
| OS | macOS 15 or later (14 should work too) |
| CPU | Apple silicon or Intel |
| To install first | Nothing (the .NET runtime is included) |

## Installing

Open Terminal (Applications, Utilities) and paste this line. It takes the newest version from GitHub and installs it. To update, run the same line again (ROMs stay).

```sh
curl -fsSL https://raw.githubusercontent.com/litchi-gh/pc-e500_emulator_pockecom/main/install.sh | sh
```

`curl -fsSL address` takes the script `install.sh` from GitHub, and `| sh` runs it. Options go after `| sh -s --`. To put a ROM of yours (here `~/PC-E500(7.2).ROM`) in place too:

```sh
curl -fsSL https://raw.githubusercontent.com/litchi-gh/pc-e500_emulator_pockecom/main/install.sh | sh -s -- --rom ~/'PC-E500(7.2).ROM'
```

To see what it does first, split it.

```sh
curl -fsSL https://raw.githubusercontent.com/litchi-gh/pc-e500_emulator_pockecom/main/install.sh > install.sh
less install.sh
sh install.sh
```

| Command | What it does |
|---|---|
| `curl -fsSL address > install.sh` | Takes the script and saves it in the current folder |
| `less install.sh` | Shows it (`q` ends) |
| `sh install.sh` | Runs it |

| Option | |
|---|---|
| `--rom file` | Copies a ROM of yours into the `rom` folder. `( )` mean something to the shell, so the name goes in `'…'` |
| `--emulator file` | Uses a `.tar.gz` you have instead of downloading |

### What is installed

| What | Where |
|---|---|
| `pokecom` and `libe500core.dylib` (keep them in the same folder) | `~/pockecom/Emulator` |
| The file that makes `pokecom` a command | `~/.local/bin/pokecom` |
| ROMs | `~/pockecom/Emulator/rom` |

- Run it without `sudo`: it installs into your own folder and needs no administrator. The script finds whether the Mac is Apple silicon or Intel.
- If `~/.local/bin` is not on the PATH (where commands are looked for), a line is added to `~/.zshrc`. **Open a new Terminal** and `pokecom` works.
- For another folder than `~/pockecom`, write `POCKECOM_HOME=folder sh install.sh`.
- The folders are those of the [PC-E500 development tools](https://github.com/litchi-gh/pc-e500_tools)' `install.sh`; an emulator installed by that one is used as it is.
- A file taken with a browser carries macOS's block on running it (these are not notarized by Apple). Installed with `--emulator`, the script lifts it.

See that it is installed:

```sh
pokecom version
pokecom roms
```

| Command | What it does | When all is well |
|---|---|---|
| `pokecom version` | Shows the version | `0.1.0` or the like |
| `pokecom roms` | Lists the ROMs found | Yours is there |

### Uninstalling

```sh
rm -r ~/pockecom/Emulator
rm ~/.local/bin/pokecom
```

`rm -r folder` deletes a folder with what is in it, `rm file` a file. ROMs in `rom` go too, so move them first.

## Putting the ROM in place

Installed with `--rom`, it is there already. To put one later, put [a ROM dumped from your own machine](04_ROM.md) in one of these.

- `rom` in the folder of `pokecom` (`~/pockecom/Emulator/rom`)
- `~/Library/Application Support/PockeComE500/rom`
- `rom` in the current folder

With your ROM at `~/PC-E500(7.2).ROM`, in your home folder. `cp from to` copies a file; `( )` mean something to the shell, so the name goes in `'…'`.

```sh
cp ~/'PC-E500(7.2).ROM' ~/pockecom/Emulator/rom/
```

`pokecom roms` lists the ROMs found.

```sh
pokecom roms
pokecom exec "PRINT 12*34"
```

A ROM anywhere else is named with `--rom` or the `POKECOM_ROM` environment variable.

```sh
pokecom exec "PRINT 1+2" --rom "$HOME/PC-E500(7.2).ROM"
```

## Read next

- [Chapter 16, the command line](16_CLI.md)
- [Chapter 18, the MCP server](18_MCP.md)

---

[← 2. On Ubuntu (Linux)](02_LINUX.md) ｜ [Contents](../README_en.md) ｜ [4. Getting the ROM →](04_ROM.md)

# 3. On a Mac (command line)

On a Mac there is no emulator with a window; the command line, `pokecom`, runs instead. The commands of [chapter 16](16_CLI.md) and the [MCP server](18_MCP.md) for AI tools are the same as on Windows and Linux.

## Requirements

| Item | |
|---|---|
| OS | macOS 15 or later (14 should work too) |
| CPU | Apple silicon or Intel |
| To install first | Nothing (the .NET runtime is included) |

## Installing

Get the one for your CPU from the [releases](https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases).

| Mac | File |
|---|---|
| Apple silicon (M1 and later) | `pokecom-<version>-osx-arm64.tar.gz` |
| Intel | `pokecom-<version>-osx-x64.tar.gz` |

Unpack it in Terminal and, once, lift the block macOS puts on downloaded files: these are not notarized by Apple, and macOS stops them otherwise.

```sh
tar xzf pokecom-<version>-osx-arm64.tar.gz
cd pokecom-<version>-osx-arm64
xattr -dr com.apple.quarantine .
./pokecom version
```

To use it from anywhere, link it from a folder on the PATH.

```sh
sudo mkdir -p /usr/local/bin
sudo ln -s "$PWD/pokecom" /usr/local/bin/pokecom
```

Keep `pokecom` and `libe500core.dylib` in the same folder.

## Putting the ROM in place

Put [a ROM dumped from your own machine](04_ROM.md) in one of these.

- `rom` in the folder of `pokecom`
- `~/Library/Application Support/PockeComE500/rom`
- `rom` in the current folder

```sh
./pokecom roms                 # the ROMs found
./pokecom exec "PRINT 12*34"   # try it
```

A ROM anywhere else is named with `--rom` or the `POKECOM_ROM` environment variable.

```sh
./pokecom exec "PRINT 1+2" --rom "$HOME/PC-E500(7.2).ROM"
```

## Read next

- [Chapter 16, the command line](16_CLI.md)
- [Chapter 18, the MCP server](18_MCP.md)

---

[← 2. On Ubuntu (Linux)](02_LINUX.md) ｜ [Contents](../README_en.md) ｜ [4. Getting the ROM →](04_ROM.md)

# 3. On a Mac (command line)

On a Mac there is no emulator with a window; the command line, `pokecom`, runs instead. The commands of [chapter 16](16_CLI.md) and the [MCP server](18_MCP.md) for AI tools are the same as on Windows and Linux.

## Requirements

| Item | |
|---|---|
| OS | macOS 15 or later (14 should work too) |
| CPU | Apple silicon or Intel |
| To install first | Nothing (the .NET runtime is included) |

## Installing

Type (or paste) these in Terminal (Applications, Utilities): they download and install. The `get` line is long, so copy and paste it.

```sh
mkdir -p ~/pockecom/download
get() { curl -fL -o "$3" "$(curl -fsSL "https://api.github.com/repos/litchi-gh/$1/releases/latest" | grep -o "\"browser_download_url\": *\"[^\"]*$2\"" | cut -d'"' -f4)"; }
```

| Command | What it does |
|---|---|
| `mkdir -p folder` | Makes the folder for what is downloaded, if it is not there |
| `get() { … }` | Makes a command of your own, `get`, that takes the file whose name ends as asked from the newest release on GitHub, with no version to look up. It lasts until this terminal closes; paste the line again in a new one |

`curl: (2)` or `curl: option : blank argument` says the newest release has no file of that name: see the [releases](https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases) page.

```sh
mkdir -p ~/pockecom/Emulator
ARCH=$(uname -m | sed 's/x86_64/x64/')
get pc-e500_emulator_pockecom "osx-$ARCH.tar.gz" ~/pockecom/download/emulator.tar.gz
tar xzf ~/pockecom/download/emulator.tar.gz -C ~/pockecom/Emulator --strip-components=1
~/pockecom/Emulator/pokecom version
```

| Command | What it does |
|---|---|
| `mkdir -p folder` | Makes the folder for it |
| `ARCH=…` | Finds the kind of Mac: `arm64` for Apple silicon, `x64` for Intel |
| `get …` | Takes the newest one for that kind of Mac |
| `tar xzf file -C folder` | Unpacks the `.tar.gz`: `x` takes out, `z` is for gzip, `f` says the file name follows. `-C` is where to, and `--strip-components=1` leaves out the one folder inside, named for the version |
| `~/pockecom/Emulator/pokecom version` | Shows the version: it is installed |

The folders and `get` are those of the [PC-E500 development tools](https://github.com/litchi-gh/pc-e500_tools). If you use both, installing by either one's steps is enough.

Files taken with `curl` do not carry macOS's block on running them. For one downloaded with a browser, lift it after unpacking with `xattr -dr com.apple.quarantine ~/pockecom/Emulator` (these are not notarized by Apple).

### From anywhere

```sh
echo 'export PATH="$PATH:$HOME/pockecom/Emulator"' >> ~/.zshrc
source ~/.zshrc
pokecom version
```

| Command | What it does |
|---|---|
| `echo '…' >> ~/.zshrc` | Adds a line to the end of `~/.zshrc`, read when a terminal opens: here, `pokecom`'s folder to the PATH (where commands are looked for). **Typed again, it adds the line again** |
| `source ~/.zshrc` | Reads `~/.zshrc` again in this terminal; new terminals need not |
| `pokecom version` | Sees that it works from anywhere |

### Updating and uninstalling

To update, paste the `get` line and type `get …` and `tar …` again. ROMs in `rom` stay.

To take it out, delete `~/pockecom/Emulator` and the line added to `~/.zshrc`. ROMs in `rom` go too, so move them first.

Keep `pokecom` and `libe500core.dylib` in the same folder.

## Putting the ROM in place

Put [a ROM dumped from your own machine](04_ROM.md) in one of these.

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

# 18. The MCP server (for AI tools)

`pokecom mcp` is an MCP server through which AI tools (Claude Code, Claude Desktop, VS Code, Cursor and others) work the emulator: the AI writes a BASIC program, really runs it, looks at the LCD and the print-out, and puts it right.

[MCP](https://modelcontextprotocol.io/) (Model Context Protocol) is the common way to give AI tools outside tools.

```mermaid
flowchart LR
  U["You"] --> C["AI tool<br/>(MCP client)"]
  C -- "standard input/output<br/>JSON-RPC" --> P["pokecom mcp<br/>(MCP server)"]
  P --> E["The emulated machine<br/>(the real ROM)"]
```

- It talks over standard input and output only. No network port is opened.
- Every operation of [chapter 17](17_CLI_REFERENCE.md) is an MCP tool (37 of them, all but `script` and `mcp`).
- There is one machine, started the first time it is used and kept until the AI tool ends.
- The RAM and settings of the emulator with a window are not touched, and an open window is not what is worked.

## What you need

1. `pokecom` ([chapter 16](16_CLI.md#what-you-need))
2. A ROM ([where it is looked for](16_CLI.md#where-the-rom-is-looked-for)). AI tools may start the server in an unexpected folder, so **say where it is with the `POKECOM_ROM` environment variable or `--rom`**

First see that it works in a terminal.

```console
$ pokecom exec "PRINT 1+2" --rom "/path/to/PC-E500(7.2).ROM"
```

## Connecting

There are three things to set, whatever the tool.

| Item | Value |
|---|---|
| Command | `pokecom` (best with its full path) |
| Arguments | `mcp` and the options wanted (`--rom`, `--ram`, `--card`, `--disk`...) |
| Type | stdio |

Paths, for example:

| System | Command |
|---|---|
| Windows | `C:\\Tools\\pokecom\\pokecom.exe` (a `\` is written twice in JSON) |
| Ubuntu (.deb) | `/usr/bin/pokecom` |
| Mac | `/usr/local/bin/pokecom` |

### Claude Code

```sh
claude mcp add pokecom -- /usr/bin/pokecom mcp --rom "/home/me/PC-E500(7.2).ROM"
```

To share it in a project, put a `.mcp.json` in its folder.

```json
{
  "mcpServers": {
    "pokecom": {
      "command": "/usr/bin/pokecom",
      "args": ["mcp", "--disk", "./disk"],
      "env": { "POKECOM_ROM": "/home/me/PC-E500(7.2).ROM" }
    }
  }
}
```

`/mcp` shows whether it is connected.

### Claude Desktop

Open the configuration file (`claude_desktop_config.json`) from the Developer settings, add to `mcpServers`, and start the app again.

| System | File |
|---|---|
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| Mac | `~/Library/Application Support/Claude/claude_desktop_config.json` |

```json
{
  "mcpServers": {
    "pokecom": {
      "command": "C:\\Tools\\pokecom\\pokecom.exe",
      "args": ["mcp"],
      "env": { "POKECOM_ROM": "C:\\Tools\\pokecom\\rom\\PC-E500(7.2).ROM" }
    }
  }
}
```

### VS Code (GitHub Copilot and others)

In `.vscode/mcp.json`:

```json
{
  "servers": {
    "pokecom": {
      "type": "stdio",
      "command": "/usr/bin/pokecom",
      "args": ["mcp"],
      "env": { "POKECOM_ROM": "/home/me/PC-E500(7.2).ROM" }
    }
  }
}
```

### Cursor

In the project's `.cursor/mcp.json` (or `~/.cursor/mcp.json` for every project), in the same form as Claude Desktop's.

### Codex CLI

In `~/.codex/config.toml`:

```toml
[mcp_servers.pokecom]
command = "/usr/bin/pokecom"
args = ["mcp"]
env = { POKECOM_ROM = "/home/me/PC-E500(7.2).ROM" }
```

### Other tools

Any tool that takes a stdio MCP server works with the same three settings. The protocol versions supported are `2025-06-18`, `2025-03-26` and `2024-11-05`.

## Using it

Once connected, ask in plain words.

| Asked for | Tools the AI uses |
|---|---|
| "Write a PC-E500 BASIC program that finds primes, and run it" | `run` |
| "Load this game.bas and show me the screen after the right key is held for 2 seconds" | `load`, `type`, `key_down`, `wait`, `key_up`, `screen` |
| "Show me 64 bytes from B8000" | `memory` |
| "List the files on X: and find out why it fails" | `drive_files`, `drive_trace` |
| "Load this BIN and show the registers after CALLing it" | `load_bin`, `exec`, `registers` |

### Getting good results

- **Have long results printed with `LPRINT`.** The LCD has four lines. `run` and `exec` return what was printed as well.
- **Give the answers a program with `INPUT` needs.** They go in `input` of `run`.
- **Have graphics shown as a picture.** `screen` with `picture` returns the LCD's dots as a PNG.
- **A program waiting for input, or that never ends, comes back as a time-out (code 3).** Give it more `seconds`, or have BRK pressed with `key`.
- **Save a state before trying things.** `save_state` and `load_state` go back to the same place any number of times.

## The tools

Their arguments are as in [chapter 17](17_CLI_REFERENCE.md). A `-` in a name is a `_` in MCP (`wait-text` is `wait_text`).

| Kind | Tools |
|---|---|
| Machine | `start` `info` `configure` `reset` `all_reset` `save_state` `load_state` `roms` |
| Programs | `run` `load` `program` `exec` `reserve` |
| Keys and screen | `type` `key` `key_down` `key_up` `wait` `wait_text` `screen` |
| Sound | `record` |
| CE-126P | `printed` `paper_cut` |
| CE-140F | `drive_files` `drive_put` `drive_get` `drive_trace` |
| RAM card | `card_create` |
| Debugger | `registers` `set_register` `memory` `poke` `imem` `poke_imem` `ram_map` `load_bin` `save_bin` |

### What comes back

```
Screen:
RUN
                                      25
>
Symbols: RUN CAPS DEG
```

- Tools that show the screen return the LCD's four lines and the symbols lit.
- `screen` with `picture: true` adds a PNG image.
- A failure comes with `isError` and its reason in words; the server goes on.
- Standard input carries the conversation, so `file` cannot be `-`. Give the program in `program`.

### Addresses and bytes

Addresses are text in hex (`"B8000"`, `"&HB8000"`, `"0xB8000"`). Bytes are written like `"08 05 06"`.

## Files

The server runs with the rights of the user who started it. A file name given to a tool is read or written as that user could (`load`, `drive_put`, `save_state`, `png` of `screen` and so on). The X: folder (`--disk`) also changes as programs `SAVE`, `KILL` or `INIT`: **`INIT` deletes the disk files in that folder**, so give it a folder of its own. Without one, a temporary folder is used that goes at the end.

Most AI tools ask before using a tool. Keep that on at first.

## When it does not connect

| Symptom | Look at |
|---|---|
| The server does not start | Does the command's path run in a terminal? On a Mac, has [`xattr`](03_MACOS.md#installing) been done? |
| "No ROM was found" | Is `POKECOM_ROM` or `--rom` given with a full path? |
| The tools do not appear | Has the AI tool read its settings again (most need restarting)? |
| The screen does not change | Is the last program still running? Press `BRK` with `key`, or start afresh with `start` |
| You want to see the conversation | JSON can be fed in by hand, a line a message |

```console
$ printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"run","arguments":{"program":"10 PRINT 5*5"}}}' \
  | pokecom mcp
```

---

[← 17. pokecom command reference](17_CLI_REFERENCE.md) ｜ [Contents](../README_en.md) ｜ [19. Scope and differences between systems →](19_COMPATIBILITY.md)

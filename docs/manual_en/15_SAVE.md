# 15. Saving and restoring

- RAM is saved every 5 seconds, at OFF and at exit, and survives power off. After a crash you may lose up to the last autosave.
- The paths below are inside the data folder (see the table below).
- Internal RAM goes to `state/memory/<ROM SHA256>/internal-<size>KB.ram`; the RAM card goes to its own image file.
- A snapshot of the CPU, internal RAM, LCD, timers and where the printer and CE-140F are on the bus is saved automatically to `state/<ROM SHA256>.i<internal>-c<card>.e500state`. Settings are in `state/settings.json`, and the controller presets and mappings in `state/controller.json`.
- "Save state" and "Load state" keep and restore snapshots of your choice; loading restores the internal RAM size of the snapshot. The RAM card is not part of a snapshot: after a load the card keeps what its image file holds, and a snapshot cannot be loaded with a card of a different capacity attached (one saved without a card is loaded without a card).
- Snapshots cannot be saved or loaded during a CE-140F transfer or with a file OPEN; `CLOSE` first. RAM autosave continues. Virtual disk files are not part of snapshots.
- "All Reset" saves the previous state to `state/before-reset-<date>.e500state` first. The RAM card is kept.
- Files are hash-checked and replaced atomically from a temporary file. The ROM itself is not stored.
- The state file is version 4 (holding `E500ST05`, which records the ROM's size and checksum, so another ROM's state is refused); version 3 (`E500ST04`) from 0.0.4 and versions 1 and 2 (`E500ST01`, `E500ST02`) up to 0.0.3 still load. A file from a newer emulator does not load in an older one, which says so from 0.0.4 on. Not compatible with other emulators.

## The data folder

| Installed by | Where |
|---|---|
| Windows: installer | `%LocalAppData%\PockeComE500\` |
| Windows: zip | The folder of `PC-E500.exe` |
| Windows: source build | `app\` |
| Ubuntu: `--deb` | `~/.local/share/PockeComE500/` |
| Ubuntu: without `--deb` | `~/pockecom/Emulator/` |

"Open the data folder" in the right-click menu opens it.

The `pokecom` command line neither reads nor writes the RAM and settings there (only ROMs are looked for, in `rom` of the user's data folder). [`--state`](16_CLI.md#go-on-from-before---state) goes on from before. State files are the same on every system.

---

[← 14. Debugger and the machine-code area](14_DEBUGGER.md) ｜ [Contents](../README_en.md) ｜ [16. The command line (pokecom) →](16_CLI.md)

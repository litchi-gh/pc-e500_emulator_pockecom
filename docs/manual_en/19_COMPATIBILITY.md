# 19. Scope and differences between systems

Implemented: SC62015 instruction execution, the 256KB ROM (PC-E500, PC-E550, PC-1480U, PC-1490U, PC-1490UII) and the 384KB ROM (PC-E500S, PC-E650, PC-U6000), internal RAM and RAM card with mirrors, two HD61102 LCD controllers (240×32 dots), key matrix, timer interrupts, ON/BRK and OFF, 0.5/1/2/4x speed, state save/restore, BEEP, folder-based CE-140F, CE-126P printer.

**Not implemented:** printers other than the CE-126P, cassette, RS-232C/UART, RTC. Not every instruction, BASIC command or timing has been verified against the real machine; LCD busy time is omitted. Machine-code programs and peripheral-heavy programs need further verification.

Verified to work with ROMs of the PC-E500, PC-E500S, PC-E550, PC-1480U, PC-1490UII, PC-E650 and PC-U6000.

## Differences between systems

The machine itself (the CPU, the ROM's work, the peripherals) is the same code everywhere.

| | Windows | Ubuntu | Command line (Windows, Ubuntu, Mac) |
|---|---|---|---|
| Window, keyboard, mouse | Yes | Yes | No (keys by `key` and `type`) |
| Settings, Display, Debug windows... | Yes | Yes | No (commands do the same) |
| Sound | Yes (winmm) | Yes (ALSA) | Recorded to a WAV (`record`) |
| CE-140F (X:) | Yes | Yes | Yes (`--disk`) |
| The CE-126P's paper | Yes | Yes | Yes (`printed`) |
| Printing on a printer | USB thermal, Windows printers | USB thermal, CUPS | No |
| Game controllers | XInput, DirectInput | Joysticks (`/dev/input/js*`) | No |
| State files, RAM cards | Yes | Yes | Yes (`--state`, `--card`) |
| Autosave of RAM and settings | Yes | Yes | No |
| New version notice | Yes | Yes | No |
| MCP (for AI tools) | No | No | Yes (`pokecom mcp`) |

There is no emulator with a window for the Mac ([chapter 3](03_MACOS.md)).

---

[← 18. The MCP server (for AI tools)](18_MCP.md) ｜ [Contents](../README_en.md) ｜ [20. Troubleshooting →](20_TROUBLESHOOTING.md)

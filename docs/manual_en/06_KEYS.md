# 6. Keys and resetting

Click the on-screen keys or use the PC keyboard. Letters are sent as upper case. Input goes through the key matrix just like the real machine.

The ROM scans the matrix at 64 Hz, so about 55 ms per key (18 keys a second) is the limit. While a PC key is down, the machine's key is held down, and it comes up when you let go, as on the real keys. Whether a held key repeats is the ROM's business: BS, DEL and the left and right arrows repeat; letters and digits type once. Keystrokes you actually made are never dropped.

| PC key | PC-E500 |
|---|---|
| Letters, digits, space, operators | Matching key |
| Enter / Backspace / Delete / Insert | ENTER / BS / DEL / INS |
| Arrows | Cursor |
| F1–F5 | Function keys |
| F6 | MENU |
| F7 | BASIC, RUN/PRO toggle |
| F8 | 2ndF |
| Esc | ON/BRK |
| Ctrl | CTRL |
| Shift | SHIFT |

Clicking **SHIFT** or **CTRL** on the screen holds it until the next key; click again to release. Symbols such as `"`, `:` and `$` are converted to SHIFT sequences following the 7.3 ROM's table. The ROM reads SHIFT from the previous matrix scan, so SHIFT is pressed one scan (about 23 ms) ahead of the character key and released one scan after. Japanese IME input is not supported.

## Resetting

There is a small **RESET hole** between the BASIC and A keys, as on the real machine; the area around it is clickable too. There are two kinds of reset.

| Action | Effect |
|---|---|
| RESET hole | Restarts the CPU, LCD, keys and external bus only. Data survives |
| "All Reset" | The real ON+RESET. The ROM shows `S1(MAIN): CLEAR ALL DATA? (Y/N)`; Y erases everything, N resumes with the data intact |

All Reset merely enters the ROM's reset routine with ON/BRK held; the decision and the erase are the ROM's own code. The previous state is saved to `state/before-reset-<date>.e500state`. To wipe internal RAM, answer Y to an All Reset — the same procedure as on the real machine.

---

[← 5. First steps](05_FIRST_STEPS.md) ｜ [Contents](../README_en.md) ｜ [7. Display and windows →](07_WINDOW.md)

# AMC address translation

The native core implements the SC62015 AMC register at internal EFh.
Its source is the Sharp ESR-L Instruction Manual, printed8-9
([original scan](https://github.com/Robert-van-Engelen/Forth500/blob/main/resources/ESR%20L%20CPU%20technical%20manual.pdf)).
No manufacturer ROM code, font or conversion table is used.

With AME (bit7) clear, ordinary internal/card mirroring is unchanged. With
AME set, AM5..0 describes CE0 capacity in2KB units, with zero meaning2KB.
Bit6 is reserved. The populated CE0 boundary is:

    boundary = C0000h - ((AMC & 3Fh) + 1) * 2048

Addresses in the PC-E500 CE0 decode region80000h..BFFFFh below that boundary
select CE1 instead. Their offset before the boundary wraps within the CE1
decode width, then the inserted card's ordinary physical mirroring applies.
The CPU has no register describing the inserted CE1 card's capacity. Thus
the translated region includes aliases below the populated card window.
Direct40000h..7FFFFh accesses remain physical card accesses.

For32KB CE0 and64KB CE1, AMC8Fh maps virtual A8000h..B7FFFh to the physical
card and leaves B8000h..BFFFFh as internal RAM. The core keeps physical card
images at40000h, independently of this CPU-visible window. Snapshot formats
are unchanged: existing internal-register storage retains AMC.

Hardware CPU reset clears AME while retaining the size bits. The original
generated CPU used the manual reset table's ACM/FEh typo and instead cleared
LCC bit7. The generator now selects AMC/EFh; generation changes exactly that
reset reference. The desktop e500_reset API intentionally initializes the
entire CPU/LCD and is broader than the pin-level Csc62015::Reset.

CPU reads, writes, instruction fetches, debugger reads and debugger writes
share translation. Debugger writes accept a valid joined CE1-to-CE0 boundary
within one logical range. They still preflight the whole range, reject a
card/internal mirror wrapping over itself, and leave memory unchanged on
ROM/unmapped/overflow failure. Physical card import/export remains physical.

The16-bit program counter and its page segment remain separate. The own-code
instruction/operand test crosses the RAM bank boundary at B7FFFh/B8000h,
within one program page; it does not assume carry into the page segment.

## Verification

Emulator/tools/test-native.cmd runs ROM-free native tests. AMC cases include
the published example, every documented capacity code, reserved bit6,
2..256KB card mirroring, absent cards, actual cross-bank instruction fetch,
atomic debugger failures, snapshot restoration and reset/LCC preservation.

CustomROM/tools/check_desktop_core.py verifies the actual published DLL on
64/128/256KB configurations. Its synthetic own ROM enables AMC and fetches
an instruction across the bank boundary through the normal public APIs;
no coverage or test-entry export is used. The report includes source, native
DLL, own-fixture and independent-DOS-ROM hashes.

Actual pairing through the independent ROM's device6/3F remains separate
work. AMC alone does not implement its memory allocator or protect the DOS
working areas. The CPU control encodes CE0 sizes only through128KB; do not
infer a valid arbitrary combined mapping for modified256KB internal RAM or
a combined range outside the CE0 decode window.

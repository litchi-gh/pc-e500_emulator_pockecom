# 4. Getting the ROM

ROM images are not included. Put the one dumped from your own machine in the data folder's `rom`, named for its model and version, as `PC-E500(7.2).ROM`.

| System | Where |
|---|---|
| Windows (zip, source build) | `app\rom\` (`rom` beside `PC-E500.exe`) |
| Windows (installer) | `%LocalAppData%\PockeComE500\rom\` |
| Ubuntu (.deb) | `~/.local/share/PockeComE500/rom/` |
| Ubuntu (tar.gz) | `rom/` in the unpacked folder |
| Mac | `~/Library/Application Support/PockeComE500/rom/`, or `rom/` in the folder of `pokecom` |

## The 384KB PC-E500S / PC-E650 / PC-U6000 ROM

The E500S/E650/U6000 internal ROM is mapped as follows.

| In the file | CPU address | Size | Chip select |
|---|---:|---:|---|
| `00000-1FFFF` | `20000-3FFFF` | 128KB | CE3 |
| `20000-5FFFF` | `C0000-FFFFF` | 256KB | CE2 |

"Open ROM" tells 256KB PC-E500 images and 384KB PC-E500S/E650/U6000 images apart automatically; a 384KB image whose main ROM has model code 3 (the version's lower digit) is taken as a PC-E500S. The ROM field of the status bar shows the family (E500, E500S or E650), and its tip the size and the file. The 384KB file has the low-address extension ROM first and the 256KB main ROM after it.

The status bar also shows `ROM Ver.x.y`, read from the major/minor version bytes in the ROM's 16-byte trailer (the ones the service screen uses), not from the file name. The PC-E500 ROM used for testing is Ver.7.2, the PC-E650 ROM Ver.8.5.

## Dumping the ROM and `tools/merge_savem_rom.py`

On the real machine, `SAVEM "d:filename",start,end` writes memory to a file (`d:` is the device name of whatever is connected).

```basic
SAVEM "L:S3A.ROM",&HC0000,&HFFFFF
SAVEM "L:S3B.ROM",&H20000,&H3FFFF     ' PC-E500S / PC-E650 / PC-U6000 only
```

`SAVEM` output starts with a 16-byte header (`FF 00 06 01 10` + 3-byte length + 3-byte start address + `FF FF FF 00 0F`). A PC-E500 family `S3A.ROM` opens with the header in place. For the PC-E500S/E650/U6000, merge `S3A.ROM` and `S3B.ROM` into a 384KB image with `tools/merge_savem_rom.py` (Python 3).

```powershell
python tools/merge_savem_rom.py S3A.ROM S3B.ROM --info                              # inspect the headers
python tools/merge_savem_rom.py S3A.ROM S3B.ROM -o app/rom/PC-U6000-full.rom         # merge into 384KB
python tools/merge_savem_rom.py S3A.ROM -o "app/rom/PC-E500(7.2).ROM"                 # strip the header only (256KB); name it model(version).ROM
python tools/merge_savem_rom.py S3A.ROM S3B.ROM --strip                              # strip each file in place
python tools/merge_savem_rom.py S3B.ROM --main-rom PC-U6000.rom --layout full -o app/rom/PC-U6000-full.rom   # merge with a stripped main ROM
```

Coverage below 100% means the dump is incomplete. Chapter 13 of the [illustrated manual](../../app/manual/index_en.html) (Help on the toolbar) walks through the whole procedure.

Run `tools/merge_savem_rom.py` with `python` on Windows and `python3` on Ubuntu and the Mac.

Stock internal RAM on the E650/U6000 is 64KB at `B0000-BFFFF`, and on the E500S 32KB at `B8000-BFFFF`, as on the E500; the RAM card sits at `40000` according to its size.

---

[← 3. On a Mac (command line)](03_MACOS.md) ｜ [Contents](../README_en.md) ｜ [5. First steps →](05_FIRST_STEPS.md)

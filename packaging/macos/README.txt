pokecom for macOS @VERSION@ - SHARP PC-E500 series emulator, command line and MCP server
https://github.com/litchi-gh/pc-e500_emulator_pockecom

動作環境 / Requirements: macOS 15 以降（14 でも動く見込み）、Apple シリコンか Intel
                         macOS 15 or later (14 should work too), Apple silicon or Intel

ターミナルでこのフォルダに移って使います。初めに一度、ダウンロードしたファイルの
実行の制限を外してください（Apple の公証を受けていないため）。
In Terminal, in this folder. Once, first, lift the block macOS puts on downloaded
files (these are not notarized by Apple):

  xattr -dr com.apple.quarantine .

  ./pokecom help

ROMイメージは含まれていません。お手持ちの実機から吸い出した ROM を rom/ フォルダに
置くか、--rom で指定してください。
No ROM image is included: dump one from your own machine, and put it in rom/ or
name it with --rom.

ライセンス / License: GPL-2.0-or-later (LICENSE, THIRD_PARTY_NOTICES.md)

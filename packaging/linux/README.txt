PC-E500 Series Emulator for Linux (PokeCom) @VERSION@
対応機種 / Models: PC-E500, PC-E500S, PC-E550, PC-1480U, PC-1490U, PC-1490UII, PC-E650, PC-U6000
https://github.com/litchi-gh/pc-e500_emulator_pockecom

動作環境 / Requirements: Ubuntu 24.04 以降 (x86-64) / Ubuntu 24.04 or later (x86-64)

  ./PC-E500        エミュレータ / the emulator
  ./pokecom help   コマンドラインと MCP サーバー / command line and MCP server

ROMイメージは含まれていません。お手持ちの実機から吸い出した ROM を
rom/ フォルダに置いてから起動してください。
No ROM image is included: dump one from your own machine and put it in rom/.

  rom/      内蔵ROMイメージ / ROM images
  ram/      RAMカードのイメージファイル / RAM card images
  drive-x/  CE-140F (X:) の保存先 / the CE-140F drive
  state/    設定・自動保存（起動時に作られます）/ settings and autosave
  manual/   使い方 / the manual

.deb で入れた場合、これらのフォルダは ~/.local/share/PockeComE500 にあります。
Installed from the .deb, these folders are in ~/.local/share/PockeComE500.

ライセンス / License: GPL-2.0-or-later (LICENSE, THIRD_PARTY_NOTICES.md)

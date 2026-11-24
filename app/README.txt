PC-E500 Series Emulator for Windows (PokeCom) 0.1.0
対応機種 / Models: PC-E500, PC-E550, PC-1480U, PC-1490U, PC-1490UII (256KB ROM), PC-E650, PC-U6000 (384KB ROM)
https://github.com/litchi-gh/pc-e500_emulator_pockecom

動作環境 / Requirements:
  Windows 10 / 11 (64-bit, x64)、x64 CPU（2コア・2GHz 程度以上）、メモリ 4GB 以上推奨
  （アプリが使うのは 150MB 程度）、ディスク約 120MB、画面 1280×800 以上推奨

使い方は manual\index.html（English: manual\index_en.html）をブラウザで開いてください（起動後は「使い方」ボタンでも開けます）。
表示言語は「設定」で日本語／English を切り替えられます。

ROMイメージは含まれていません。お手持ちの実機から SAVEM で吸い出した
S3A.ROM（PC-E650/U6000 は S3A+S3B を結合した384KB）を rom\ フォルダに置いてから
PC-E500.exe を起動してください。手順は説明書の13章にあります。

  rom\      内蔵ROMイメージ
  ram\      RAMカードのイメージファイル
  drive-x\  CE-140F (X:) の保存先
  state\    設定・自動保存（起動時に作られます）

インストーラーで入れた場合、これらのフォルダは利用者ごとの
%LocalAppData%\PockeComE500\ にあります（スタートメニューの
「PC-E500 Emulator のデータフォルダ」で開けます）。

更新：新しい版が出ると、ツールバーに「新しい版」のボタンが出ます。
エミュレータを終了してから、インストーラーで入れた場合は新しい
-setup.exe を実行、zip の場合は中身をこのフォルダに上書きで展開して
ください。ROM・RAM・設定はそのまま残ります。

ライセンス: GPL-2.0-or-later（LICENSE、THIRD_PARTY_NOTICES.md を参照）

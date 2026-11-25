# マニュアル

[English](README_en.md) ・ [README（概要）](../README.md)

| やりたいこと | 読む章 |
|---|---|
| Windows で使い始める | [1章](manual/01_WINDOWS.md) → [4章](manual/04_ROM.md) → [5章](manual/05_FIRST_STEPS.md) |
| Ubuntu で使い始める | [2章](manual/02_LINUX.md) → [4章](manual/04_ROM.md) → [5章](manual/05_FIRST_STEPS.md) |
| Mac で使う | [3章](manual/03_MACOS.md) → [16章](manual/16_CLI.md) |
| 端末やスクリプトから動かす | [16章](manual/16_CLI.md)、[17章](manual/17_CLI_REFERENCE.md) |
| AI のツールから使う | [18章](manual/18_MCP.md) |
| うまく動かない | [20章](manual/20_TROUBLESHOOTING.md) |

## 環境ごとの入れかた

| 章 | 内容 |
|---|---|
| [1. Windows で使う](manual/01_WINDOWS.md) | インストーラーと zip、フォルダの構成、更新 |
| [2. Ubuntu（Linux）で使う](manual/02_LINUX.md) | Ubuntu での入れかた、Windows 版との違い |
| [3. Mac で使う（コマンドライン）](manual/03_MACOS.md) | Mac での入れかた |
| [4. ROM を用意する](manual/04_ROM.md) | ROM の置き場所、実機からの吸い出し（ROMDump・SAVEM） |

## 使い方

| 章 | 内容 |
|---|---|
| [5. 最初の操作](manual/05_FIRST_STEPS.md) | 初期化と、最初の計算・プログラム |
| [6. キー操作とリセット](manual/06_KEYS.md) | PC のキーとの対応、RESET とオールリセット |
| [7. 表示とウィンドウ](manual/07_WINDOW.md) | ステータスバー、ウィンドウの表示、液晶 |
| [8. 設定と音](manual/08_SETTINGS.md) | 「設定」の内容、音 |
| [9. 内蔵 RAM と RAM カード](manual/09_RAM.md) | 内蔵 RAM の容量、RAM カードのイメージ |
| [10. プログラムと文字のやり取り](manual/10_PROGRAMS.md) | プログラム読込、画面の文字のコピー、実機とのやり取り |
| [11. CE-140F（X: ドライブ）](manual/11_CE-140F.md) | X: ドライブ、エラー |
| [12. CE-126P（プリンタ）](manual/12_CE-126P.md) | 紙、プリンタへの印刷 |
| [13. コントローラー](manual/13_CONTROLLER.md) | ゲームパッドの割り当て |
| [14. デバッグと機械語エリア](manual/14_DEBUGGER.md) | レジスタ、メモリ、BIN、機械語エリア |
| [15. 保存と復元](manual/15_SAVE.md) | 自動保存、状態ファイル、データフォルダ |

## コマンドラインと AI

| 章 | 内容 |
|---|---|
| [16. コマンドライン（pokecom）](manual/16_CLI.md) | 画面なしで動かす、スクリプト、結果の受け取り |
| [17. pokecom のコマンド一覧](manual/17_CLI_REFERENCE.md) | 全部のコマンドと引数 |
| [18. MCP サーバー（AI から使う）](manual/18_MCP.md) | Claude Code・VS Code などへのつなぎかた、ツール |

## 資料

| 章 | 内容 |
|---|---|
| [19. 対応範囲と、環境ごとの違い](manual/19_COMPATIBILITY.md) | 実装しているもの、Windows・Ubuntu・コマンドラインの違い |
| [20. 困ったとき](manual/20_TROUBLESHOOTING.md) | 症状ごとの対処 |

## そのほか

| 文書 | 内容 |
|---|---|
| [ビルドとテスト](design/01_BUILD.md) | コードの構成、ビルド、テスト、配布物の作りかた |
| [AMC](design/02_AMC.md) | CPU の番地変換の実装と検証 |
| [カセット](design/03_CASSETTE.md) | カセットの信号の実装 |
| [変更履歴](CHANGELOG.md) | 版ごとの変更 |
| [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES.md) | 移植元と同梱物のライセンス |
| [画像付きの取扱説明書](../app/manual/index.html) | ツールバーの「使い方」で開くもの |

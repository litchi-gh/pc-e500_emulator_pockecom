# 17. pokecom のコマンド一覧

[16章](16_CLI.md)のコマンドをすべて並べています。`pokecom help <コマンド>` でも見られます。MCP のツール（[18章](18_MCP.md)）も同じもので、名前の `-` は `_` になります。

- `<...>` は必ず要るもの、`[...]` は省けるものです。どの引数も `--名前 値` の形でも書けます。
- かっこの中の数は省いたときの値です。秒はエミュレータの中の時間です。
- 番地は16進です（`B8000`・`&HB8000`・`0xB8000`）。

## どのコマンドにも付けられるオプション

| オプション | 内容 |
|---|---|
| `--rom <ファイル>` | ROM（省くと `POKECOM_ROM`、または `rom` フォルダのもの） |
| `--ram <KB>` | 内蔵 RAM（32） |
| `--card <ファイル>` | RAM カードのイメージ |
| `--disk <フォルダ>` | CE-140F（X:）のフォルダ（省くと一時フォルダ） |
| `--disk-read-only` | X: を書き込み禁止にする |
| `--no-printer` | CE-126P をつながない |
| `--state <ファイル>` | この状態から始め、終わりの状態を書き戻す（なければ作る） |
| `--png <ファイル>` | 終わりの液晶を PNG に保存 |
| `--json` | JSON で答える |
| `--lang <ja か en>` | メッセージの言語 |
| `--help` | 説明を表示 |

## 本体

### start

```
pokecom start
```

まっさらな本体（RAM は空）を起動し、BASIC の RUN の表示にします。ほかのコマンドも、必要なら本体を起動するので、やり直したいときや、別の ROM・構成にするときに使います。

| 引数 | 内容 |
|---|---|
| `--rom` | ROM のファイル（省くと今のまま、または探す） |
| `--ram` | 内蔵 RAM（KB）：8・16・32・64・128・256 |
| `--card` | RAM カードのイメージ |
| `--disk` | CE-140F（X:）のフォルダ |

### info

```
pokecom info
```

ROM、RAM、カード、CE-140F、CE-126P、電源、点いている表示シンボル、機械語エリア、空きを表示します。

### configure

```
pokecom configure
```

構成を変えます。内蔵 RAM の容量かカードを変えると電源を入れ直します（容量が同じなら内蔵 RAM の中身は残ります）。ほかはすぐに変わります。

| 引数 | 内容 |
|---|---|
| `--ram` | 内蔵 RAM（KB） |
| `--card` | RAM カードのイメージ。`none` で取り外す |
| `--disk` | CE-140F のフォルダ。`none` で外す、`temporary` で一時フォルダ |
| `--disk-read-only` | CE-140F を書き込み禁止にする |
| `--printer` | CE-126P をつなぐ |
| `--auto-power-off` | 自動電源 OFF（放っておくと約10分）を働かせる |

### reset

```
pokecom reset
```

RESET 穴を押します。CPU・液晶・キーが初めの状態になり、データは残ります。

### all-reset

```
pokecom all-reset [answer]
```

ON＋RESET。全データを消すかを ROM が聞きます。

| 引数 | 内容 |
|---|---|
| `answer` | `yes` で全消去、`no` で残す。省くと、聞かれた所で止まる |

### save-state

```
pokecom save-state <path>
```

本体の状態をファイルに保存します（画面のあるエミュレータでも読めます）。

| 引数 | 内容 |
|---|---|
| `path` | `.e500state` で終わるファイル名 |

### load-state

```
pokecom load-state <path>
```

保存した状態に戻します（同じ ROM、同じ容量のカードのもの）。

| 引数 | 内容 |
|---|---|
| `path` | `.e500state` で終わるファイル名 |

### roms

```
pokecom roms
```

見つかった ROM の一覧を表示します。

## プログラム

### run

```
pokecom run [file]
```

BASIC のプログラムを入れ替えて実行し、画面と、印字（`LPRINT`）された行を表示します。`PRINT` した行は最後の4行しか残らないので、長い出力には `LPRINT` を使います。BIN は読み込むだけです。

| 引数 | 内容 |
|---|---|
| `file` | BASIC のテキスト（UTF-8）、本体が保存した形、BIN。`-` は標準入力 |
| `--program` | プログラムの文字。1行に1つの行番号 |
| `--input` | 実行中に打つ文字。`INPUT` 1つに1行 |
| `--seconds` | 終わるのを待つ秒数（60） |

### load

```
pokecom load [file]
```

プログラムを読み込みます（実行はしません）。エミュレータの「プログラム読込」と同じ決まりです。

| 引数 | 内容 |
|---|---|
| `file` | ファイル。`-` は標準入力 |
| `--program` | BASIC の文字 |
| `--no-reserve` | 機械語エリアの外の BIN でも、エリアを確保しない |

### program

```
pokecom program
```

本体にある BASIC のプログラムを取り出します。本体は何も変わりません。

| 引数 | 内容 |
|---|---|
| `--to` | 表示の代わりに、このファイルに書く |
| `--saved` | 本体が保存する形で書く（`to` と一緒に） |

### exec

```
pokecom exec <line>
```

RUN の表示で BASIC の1行を実行します（`PRINT 12*34`、`LIST`、`RUN` など）。動いているプログラムは止めます。

| 引数 | 内容 |
|---|---|
| `line` | 1行（ENTER は要りません） |
| `--seconds` | 終わるのを待つ秒数（60） |

### reserve

```
pokecom reserve <size>
```

機械語エリアの大きさを決めます（0 で返します）。プログラムと変数は残ります。

| 引数 | 内容 |
|---|---|
| `size` | バイト数（16進） |

## キーと画面

### type

```
pokecom type <text>
```

今の状態のまま、文字を打ちます（改行は ENTER）。英字は大文字になります。

| 引数 | 内容 |
|---|---|
| `text` | 文字。コマンドラインでは `\n` が改行 |
| `--seconds` | 打ったあとに動かす秒数（0.5） |

### key

```
pokecom key <keys...>
```

キーを順に押します。`SHIFT+Q` のように書くと、同時に押します。キーの名前は [16章](16_CLI.md#ゲームのキーを押し続ける)。

| 引数 | 内容 |
|---|---|
| `keys` | 押すキー（順に） |
| `--hold` | 1つを押し続ける秒数（0 なら軽く押す） |
| `--seconds` | 押したあとに動かす秒数（0.3） |

### key-down

```
pokecom key-down <keys...>
```

キーを押したままにします（ゲームのキーなど。間に `wait` を入れます）。スクリプトと MCP 用です。

| 引数 | 内容 |
|---|---|
| `keys` | 押すキー |

### key-up

```
pokecom key-up [keys...]
```

キーを離します。スクリプトと MCP 用です。

| 引数 | 内容 |
|---|---|
| `keys` | 離すキー。省くと全部 |

### wait

```
pokecom wait [seconds]
```

本体を動かして待ちます。

| 引数 | 内容 |
|---|---|
| `seconds` | 秒数（1） |

### wait-text

```
pokecom wait-text <text>
```

画面に文字が出るまで動かします。出なければ終了コードは 3 です。

| 引数 | 内容 |
|---|---|
| `text` | 待つ文字 |
| `--seconds` | 長くて待つ秒数（60） |

### screen

```
pokecom screen
```

液晶を表示します（読み取った 40×4 文字と、点いている表示シンボル）。

| 引数 | 内容 |
|---|---|
| `--picture` | ドットを PNG でも返す（MCP 用） |
| `--png` | ドットを PNG に保存 |
| `--scale` | 1ドットのピクセル数、1〜16（4） |

## 音

### record

```
pokecom record <to>
```

動かしながら音（BEEP）を WAV に録ります。BASIC の1行を渡せば、それを実行しながら録ります。

| 引数 | 内容 |
|---|---|
| `to` | WAV のファイル |
| `--line` | 先に実行する BASIC の1行 |
| `--seconds` | 録る秒数（3） |

## CE-126P（プリンタ）

### printed

```
pokecom printed
```

CE-126P の紙の文字をすべて表示します。

| 引数 | 内容 |
|---|---|
| `--to` | テキストに保存 |
| `--png` | 紙の絵を PNG に保存 |

### paper-cut

```
pokecom paper-cut
```

CE-126P の紙を切ります（印字は消えます）。

## CE-140F（X:）

### drive-files

```
pokecom drive-files
```

X: のファイルの一覧を表示します。

### drive-put

```
pokecom drive-put <from> [name]
```

ファイルを X: に置きます。

| 引数 | 内容 |
|---|---|
| `from` | 置くファイル |
| `name` | X: での名前（8.3 形式。省くと元の名前） |

### drive-get

```
pokecom drive-get <name> <to>
```

X: のファイルを取り出します。

| 引数 | 内容 |
|---|---|
| `name` | X: での名前 |
| `to` | 保存先 |

### drive-trace

```
pokecom drive-trace
```

CE-140F の通信の記録（コマンドとエラー）を表示します。

## RAM カード

### card-create

```
pokecom card-create <path> <kb>
```

空の RAM カードのイメージを作ります。

| 引数 | 内容 |
|---|---|
| `path` | 作るファイル |
| `kb` | 容量（KB）：8・16・32・64・128・256 |

## デバッグ

### registers

```
pokecom registers
```

CPU のレジスタを表示します。

### set-register

```
pokecom set-register <name> <value>
```

CPU のレジスタに書きます。

| 引数 | 内容 |
|---|---|
| `name` | `A` `IL` `BA` `I` `X` `Y` `U` `S` `P` `F` |
| `value` | 値（16進） |

### memory

```
pokecom memory <address> [length]
```

メモリを16進で表示します（ROM・RAM・カード）。

| 引数 | 内容 |
|---|---|
| `address` | 先頭の番地（16進） |
| `length` | バイト数、4096 まで（64） |

### poke

```
pokecom poke <address> <bytes>
```

RAM に書きます。1バイトでも RAM でなければ、何も書きません。

| 引数 | 内容 |
|---|---|
| `address` | 先頭の番地（16進） |
| `bytes` | 書くバイト列（16進、`"08 05 06"` など） |

### imem

```
pokecom imem
```

CPU の内部 RAM（256 バイト）を表示します。

### poke-imem

```
pokecom poke-imem <address> <bytes>
```

CPU の内部 RAM（00〜EE）に書きます。

| 引数 | 内容 |
|---|---|
| `address` | 先頭（16進） |
| `bytes` | 書くバイト列（16進） |

### ram-map

```
pokecom ram-map
```

RAM（とカード）の並びを表示します：プログラム、変数、空き、スタック、作業域、機械語エリア。

### load-bin

```
pokecom load-bin <file> [address]
```

ファイルを RAM に書きます。番地を省くと、`SAVE M` のヘッダーの番地に書きます。

| 引数 | 内容 |
|---|---|
| `file` | ファイル |
| `address` | 先頭の番地（16進） |

### save-bin

```
pokecom save-bin <file> <address> <length>
```

メモリの範囲をファイルに保存します。

| 引数 | 内容 |
|---|---|
| `file` | ファイル |
| `address` | 先頭の番地（16進） |
| `length` | バイト数（16進） |
| `--header` | `SAVE M` のヘッダーを付ける（元の番地に戻せます） |

## まとめて動かす

| コマンド | 内容 |
|---|---|
| `script <file>` | 1行に1つのコマンドを、同じ本体で順に実行します（`-` は標準入力。[16章](16_CLI.md#まとめて動かすscript)） |
| `mcp` | MCP サーバー（[18章](18_MCP.md)） |
| `help [コマンド]` | 説明 |
| `version` | 版 |

---

[← 16. コマンドライン（pokecom）](16_CLI.md) ｜ [目次](../README.md) ｜ [18. MCP サーバー（AI から使う） →](18_MCP.md)

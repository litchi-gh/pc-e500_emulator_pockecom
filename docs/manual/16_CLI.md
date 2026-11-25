# 16. コマンドライン（pokecom）

`pokecom` は、エミュレータを画面なしで動かすコマンドです。BASIC の1行を実行する、プログラムを走らせて結果を受け取る、キーを押して液晶を読む、といったことが、端末・スクリプト・自動テストからできます。Windows・Linux・Mac で同じように使えます。

```console
$ pokecom exec "PRINT 12*34"
PRINT 12*34
                                     408
>
```

- 実機の ROM がそのまま動きます。画面のあるエミュレータと同じ本体です。
- PC の速さいっぱいで動きます。「秒」はすべて、エミュレータの中の時間です。
- 本体は毎回まっさらな状態から始まります。**画面のあるエミュレータの RAM・設定・自動保存には触れません。**
- 自動電源 OFF は、初めは止めてあります。

## 用意するもの

| システム | `pokecom` の場所 |
|---|---|
| Windows | `pokecom-<版>-win-x64.zip` を展開したフォルダの `pokecom.exe` |
| Ubuntu | [2章](02_LINUX.md#入れかた)の1行で入れれば `pokecom` |
| Mac | [3章](03_MACOS.md#入れかた)の1行で入れれば `pokecom` |

### ROM の探し方

次の順に探し、見つかったものを使います。

1. `--rom` で指定したファイル
2. 環境変数 `POKECOM_ROM` のファイル
3. `rom` フォルダの中（いまいるフォルダ、`pokecom` のあるフォルダ、利用者のデータフォルダ、の順）。ROM のある最初のフォルダを使い、その中に `PC-E500(版).ROM` という名前があればいちばん新しい版を、なければ名前の順で最初のものを使います。大きさが 256KB（ヘッダー付きの 256KB+16 も可）か 384KB のファイルを ROM とみなします

| システム | 利用者のデータフォルダ |
|---|---|
| Windows | `%LocalAppData%\PockeComE500\rom` |
| Ubuntu | `~/.local/share/PockeComE500/rom` |
| Mac | `~/Library/Application Support/PockeComE500/rom` |

`pokecom roms` で、見つかった ROM の一覧が出ます。

## 基本の使い方

```
pokecom <コマンド> [引数] [オプション]
```

`pokecom help` で一覧、`pokecom help <コマンド>` でくわしい説明が出ます。全部のコマンドは [17章](17_CLI_REFERENCE.md)にあります。

### BASIC の1行を実行する：exec

```console
$ pokecom exec "PRINT SQR 2"
$ pokecom exec 'SAVE "X:TEST.BAS"' --disk ./mydisk
```

終わると、液晶の文字（40桁×4行）を表示します。

### プログラムを走らせる：run

```console
$ cat sq.bas
10 INPUT "N=";N
20 FOR I=1 TO N
30 LPRINT I;I*I
40 NEXT I
50 PRINT "OK"

$ pokecom run sq.bas --input 3
RUN
N=3
OK
>
印字:
 1  1
 2  4
 3  9
```

- `--input` は、`INPUT` に答える文字です。何行か答えるときは `\n` で区切ります（`--input "3\n5"`）。
- 液晶には最後の4行しか残りません。**長い結果は `LPRINT` で出してください。** CE-126P に印字された行が、そのまま表示されます。
- ファイル名を `-` にすると、標準入力から読みます。`--program "10 PRINT 1\n20 PRINT 2"` で、直接書くこともできます。
- BASIC のテキスト（UTF-8）、本体が保存した形、`SAVE M` のヘッダー付きの BIN を読めます。BIN は読み込むだけで、実行はしません。
- `--seconds` で、終わるのを待つ時間（既定は60秒）を決めます。

### 状態を引き継ぐ：--state

1回ごとに本体が作り直されるので、続きから動かすには `--state` を付けます。ファイルがあればその状態から始め、終わりの状態を書き戻します（なければ作ります）。

```console
$ pokecom exec "A=5" --state work.e500state
$ pokecom exec "PRINT A" --state work.e500state
PRINT A
                                       5
>
```

状態ファイルは、画面のあるエミュレータの「ステータス保存」「ステータス読込」と同じ形式です。

### まとめて動かす：script

1行に1つのコマンドを書いたファイルを、同じ本体で順に実行します。

```console
$ cat demo.txt
# 1行に1コマンド。# から後は読み飛ばします
load sq.bas
type "RUN\n"
wait-text "N="
type "4\n"
wait 2
printed

$ pokecom script demo.txt
```

- 空白で区切ります。空白を含む語は `"..."` でくくり、その中の `"` は `""` と書きます。
- エラーが出た行で止まり、行番号とエラーを表示します。
- ファイル名を `-` にすると標準入力から読みます。
- `start`（まっさらな本体を起動し直す）、`key-down`・`key-up`（キーを押したままにする・離す）は、スクリプトと MCP だけで使えます。

### 画面・音・紙を残す

```console
$ pokecom exec "PRINT 1" --png lcd.png
$ pokecom record beep.wav --line "BEEP 3"
$ pokecom script work.txt
```

上から、終わりの液晶を PNG に、音を WAV に、紙をテキストと PNG に残します（紙は、`work.txt` の中に `printed --to out.txt --png out.png` と書きます）。

## 周辺機器と構成

どのコマンドにも、次のオプションを付けられます。

| オプション | 内容 |
|---|---|
| `--rom <ファイル>` | ROM |
| `--ram <KB>` | 内蔵 RAM（8・16・32・64・128・256。既定は 32。E650/U6000 の ROM では 64 以上） |
| `--card <ファイル>` | RAM カードのイメージ（`pokecom card-create card.ram 64` で作れます） |
| `--disk <フォルダ>` | CE-140F（X:）のフォルダ。指定しなければ、終わると消える一時フォルダです |
| `--disk-read-only` | X: を書き込み禁止にする |
| `--no-printer` | CE-126P をつながない |
| `--state <ファイル>` | 状態を引き継ぐ |
| `--png <ファイル>` | 終わりの液晶を PNG に保存する |
| `--json` | JSON で答える |
| `--lang ja` / `--lang en` | メッセージの言語 |

RAM カードのイメージは、中身が変わると書き戻します。

## 結果を受け取る

### 終了コード

| コード | 意味 |
|---|---|
| 0 | 正常 |
| 1 | エラー（ファイルがない、ROM が違うなど） |
| 2 | 使い方の誤り |
| 3 | 時間内に終わらなかった（実行中か、入力待ち） |
| 4 | BASIC のエラー |

BASIC のエラーは、ROM が持つメッセージの表と照らし合わせて見分けます。

```console
$ pokecom exec "PRIMT 5"; echo $?
PRIMT 5
Syntax error
BASIC のエラー: Syntax error
4
```

正常な結果と画面は標準出力へ、うまくいかなかった理由は標準エラー出力へ出します。

### JSON

```console
$ pokecom run sq.bas --input 3 --json
{"code":0,"text":"印字:\n 1  1\n 2  4\n 3  9","screen":["RUN","N=3","OK",">"],"symbols":["RUN","CAPS","DEG"]}
```

| 項目 | 内容 |
|---|---|
| `code` | 終了コードと同じ数 |
| `text` | 印字、エラーの説明など |
| `screen` | 液晶の4行（画面を見せるコマンドだけ） |
| `symbols` | 点いている表示シンボル（`RUN`・`PRO`・`BUSY`・`CAPS`・`DEG` など） |

## 例

### BASIC のプログラムを自動でテストする

```sh
#!/bin/sh
# 期待する印字と比べる
pokecom run sq.bas --input 3 --json | python3 -c '
import json, sys
r = json.load(sys.stdin)
assert r["code"] == 0, r["text"]
assert "3  9" in r["text"], r["text"]
print("OK")'
```

### 機械語を読み込んで動かす

```
# ml.txt：機械語エリアを 400h バイト確保し、BIN を置いて呼ぶ
reserve 400
load-bin prog.bin
exec "CALL &HBF800"
registers
```

```console
$ pokecom script ml.txt
```

`load-bin` は、`SAVE M` のヘッダーがあればそこに書かれた番地へ、なければ引数の番地へ書きます。

### ゲームのキーを押し続ける

```
# game.txt
load game.bas
type "RUN\n"
wait 2
key-down RIGHT
wait 1.5
key-up RIGHT
screen --png after.png
```

キーの名前は、英数字、`ENTER` `SPACE` `BS` `DEL` `INS` `LEFT` `RIGHT` `UP` `DOWN` `SHIFT` `CTRL` `BASIC` `MENU` `F1`〜`F5`（`PF1`〜`PF5`）`2ndF` `C-CE` `BRK` `OFF` と、ほかのキーの印字です。`SHIFT+Q` のように書くと同時に押します。

### ほかの版の ROM でも試す

```sh
for rom in roms/*.ROM; do
  echo "$rom"
  pokecom run test.bas --rom "$rom" || exit 1
done
```

---

[← 15. 保存と復元](15_SAVE.md) ｜ [目次](../README.md) ｜ [17. pokecom のコマンド一覧 →](17_CLI_REFERENCE.md)
